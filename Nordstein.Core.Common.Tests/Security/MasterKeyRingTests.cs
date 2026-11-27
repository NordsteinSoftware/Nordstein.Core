using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Cryptography;
using Nordstein.Core.Common.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Tests.Security.Internal;
using Nordstein.Core.Testing;
using NSubstitute;

namespace Nordstein.Core.Common.Tests.Security;

/// <summary>
/// The file-backed <see cref="IMasterKeyRing"/>: a valid ring wraps and unwraps; a bad ring fails closed with a
/// typed failure from the first access on. Rings are written by hand (<see cref="KeyRingFiles"/>), so the tests
/// pin the file format. Unix mode custody is exercised on Unix only; Windows does not evaluate modes.
/// </summary>
[TestClass]
public sealed class MasterKeyRingTests : BaseTest<Module>
{
    [TestMethod]
    public void WrapAndUnwrap_ValidRing_RoundTrips()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        var ringId = Guid.NewGuid();
        KeyRingFiles.Write(keyDir, ringId, (1, "active", RandomNumberGenerator.GetBytes(32)));
        IMasterKeyRing ring = Ring(keyDir);
        byte[] secret = RandomNumberGenerator.GetBytes(32);

        MasterWrappedKey wrapped = ring.Wrap(secret, "slot-1"u8);
        byte[] restored = ring.Unwrap(wrapped, "slot-1"u8);

        ring.RingId.Should().Be(ringId);
        ring.ActiveKeyVersion.Should().Be(1);
        wrapped.KeyVersion.Should().Be(1);
        wrapped.Wrapped.Nonce.Length.Should().Be(12);
        wrapped.Wrapped.Ciphertext.Length.Should().Be(32 + 16);
        restored.Should().Equal(secret);
    }

    [TestMethod]
    public void Wrap_AssociatedData_IsTheCallersBytesThenTheBigEndianVersion()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        byte[] key = RandomNumberGenerator.GetBytes(32);
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "decrypt-only", RandomNumberGenerator.GetBytes(32)), (258, "active", key));

        MasterWrappedKey wrapped = Ring(keyDir).Wrap("value"u8, "acme-setting-v1"u8);

        byte[] associatedData = [.. "acme-setting-v1"u8, 0x00, 0x00, 0x01, 0x02];
        wrapped.KeyVersion.Should().Be(258);
        KeyRingFiles.Open(key, wrapped.Wrapped.Nonce.Span, wrapped.Wrapped.Ciphertext.Span, associatedData)
            .Should().Equal("value"u8.ToArray());
    }

    [TestMethod]
    public void Wrap_EmptyValueAndEmptyBinding_RoundTrips()
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));

        MasterWrappedKey wrapped = ring.Wrap([], []);

        wrapped.Wrapped.Ciphertext.Length.Should().Be(16);
        ring.Unwrap(wrapped, []).Should().BeEmpty();
    }

    [TestMethod]
    public void Wrap_EachCall_DrawsAFreshNonce()
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));

        MasterWrappedKey first = ring.Wrap("same"u8, "slot"u8);
        MasterWrappedKey second = ring.Wrap("same"u8, "slot"u8);

        first.Wrapped.Nonce.ToArray().Should().NotEqual(second.Wrapped.Nonce.ToArray());
        first.Wrapped.Ciphertext.ToArray().Should().NotEqual(second.Wrapped.Ciphertext.ToArray());
    }

    [TestMethod]
    public void Unwrap_OtherBinding_FailsClosedWithUnwrapFailed()
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        MasterWrappedKey wrapped = ring.Wrap("value"u8, "slot-1"u8);

        Action act = () => ring.Unwrap(wrapped, "slot-2"u8);

        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.UnwrapFailed);
        thrown.InnerException.Should().BeOfType<AeadAuthenticationFailedException>();
    }

    [TestMethod]
    public void Unwrap_BindingThatSpellsTheVersionItself_DoesNotUnwrap()
    {
        // A value wrapped for "slot" under version 1 binds "slot" ‖ 00000001; a caller passing those bytes as its
        // own binding gets "slot" ‖ 00000001 ‖ 00000001 and must not unwrap it.
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        MasterWrappedKey wrapped = ring.Wrap("value"u8, "slot"u8);

        Action act = () => ring.Unwrap(wrapped, [.. "slot"u8, 0, 0, 0, 1]);

        act.Should().Throw<MasterKeyRingException>().Which.Failure.Should().Be(MasterKeyRingFailure.UnwrapFailed);
    }

    [TestMethod]
    public void Unwrap_RelabelledToAnotherVersionInTheRing_FailsClosedWithUnwrapFailed()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        byte[] shared = RandomNumberGenerator.GetBytes(32);
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "active", shared), (2, "decrypt-only", RandomNumberGenerator.GetBytes(32)));
        IMasterKeyRing ring = Ring(keyDir);
        MasterWrappedKey wrapped = ring.Wrap("value"u8, "slot"u8);

        Action act = () => ring.Unwrap(wrapped with { KeyVersion = 2 }, "slot"u8);

        act.Should().Throw<MasterKeyRingException>().Which.Failure.Should().Be(MasterKeyRingFailure.UnwrapFailed);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(99)]
    public void Unwrap_VersionNotInTheRing_FailsClosedWithUnknownKeyVersion(int version)
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        MasterWrappedKey wrapped = ring.Wrap("value"u8, "slot"u8);

        Action act = () => ring.Unwrap(wrapped with { KeyVersion = version }, "slot"u8);

        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.UnknownKeyVersion);
        thrown.Message.Should().Contain(version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(11)]
    [DataRow(13)]
    public void Unwrap_NonceNotTwelveBytes_FailsClosedWithUnwrapFailed(int nonceLength)
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        MasterWrappedKey wrapped = ring.Wrap("value"u8, "slot"u8);
        var damaged = wrapped with { Wrapped = wrapped.Wrapped with { Nonce = new byte[nonceLength] } };

        Action act = () => ring.Unwrap(damaged, "slot"u8);

        act.Should().Throw<MasterKeyRingException>().Which.Failure.Should().Be(MasterKeyRingFailure.UnwrapFailed);
    }

    [TestMethod]
    public void Unwrap_TamperedCiphertext_FailsClosedWithUnwrapFailed()
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        MasterWrappedKey wrapped = ring.Wrap("value"u8, "slot"u8);
        byte[] tampered = wrapped.Wrapped.Ciphertext.ToArray();
        tampered[0] ^= 1;

        Action act = () => ring.Unwrap(wrapped with { Wrapped = wrapped.Wrapped with { Ciphertext = tampered } }, "slot"u8);

        act.Should().Throw<MasterKeyRingException>().Which.Failure.Should().Be(MasterKeyRingFailure.UnwrapFailed);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(15)]
    public void Unwrap_CiphertextShorterThanATag_FailsClosedWithUnwrapFailed(int length)
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        MasterWrappedKey wrapped = ring.Wrap("value"u8, "slot"u8);

        Action act = () => ring.Unwrap(wrapped with { Wrapped = wrapped.Wrapped with { Ciphertext = new byte[length] } }, "slot"u8);

        act.Should().Throw<MasterKeyRingException>().Which.Failure.Should().Be(MasterKeyRingFailure.UnwrapFailed);
    }

    [TestMethod]
    public void Unwrap_Null_ThrowsArgumentNull()
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        MasterWrappedKey missing = (new MasterWrappedKey[1])[0];

        Action act = () => ring.Unwrap(missing, "slot"u8);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("wrapped");
    }

    [TestMethod]
    public void Unwrap_WithoutNonceAndCiphertext_ThrowsArgument()
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(NewRing(parent));
        WrappedKey missing = (new WrappedKey[1])[0];

        Action act = () => ring.Unwrap(new MasterWrappedKey(1, missing), "slot"u8);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("wrapped");
    }

    [TestMethod]
    public void Unwrap_UnderAKeyNowDecryptOnly_StillUnwraps()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        byte[] first = RandomNumberGenerator.GetBytes(32);
        var ringId = Guid.NewGuid();
        KeyRingFiles.Write(keyDir, ringId, (1, "active", first));
        MasterWrappedKey wrapped = Ring(keyDir).Wrap("value"u8, "slot"u8);
        KeyRingFiles.Write(keyDir, ringId, (1, "decrypt-only", first), (2, "active", RandomNumberGenerator.GetBytes(32)));
        IMasterKeyRing reloaded = Ring(keyDir);

        byte[] restored = reloaded.Unwrap(wrapped, "slot"u8);

        restored.Should().Equal("value"u8.ToArray());
        reloaded.ActiveKeyVersion.Should().Be(2);
        reloaded.Wrap("next"u8, "slot"u8).KeyVersion.Should().Be(2);
    }

    [TestMethod]
    public void Resolve_MissingFile_DoesNoIo()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        ISecretFileLoader loader = Substitute.For<ISecretFileLoader>();
        IServiceProvider services = Services(Options(keyDir), builder => builder.RegisterInstance(loader).As<ISecretFileLoader>());

        _ = services.GetRequiredService<IMasterKeyRing>();

        loader.ReceivedCalls().Should().BeEmpty();
    }

    [TestMethod]
    public void EveryMember_MissingFile_FailsClosedWithFileRejected()
    {
        using ITempDirectory parent = Parent();
        IMasterKeyRing ring = Ring(KeyRingFiles.CreateKeyDirectory(parent.Combine("keys")));

        foreach (Action act in Members(ring))
        {
            MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
            thrown.Failure.Should().Be(MasterKeyRingFailure.FileRejected);
            thrown.InnerException.Should().BeOfType<SecretFileException>()
                .Which.Rejection.Should().Be(SecretFileRejection.Missing);
        }
    }

    [TestMethod]
    public void RingId_ReadTwice_LoadsOnce()
    {
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        ISecretFileLoader loader = Substitute.For<ISecretFileLoader>();
        loader.Load(Arg.Any<string>()).Returns(call => File.ReadAllBytes(call.Arg<string>() ?? throw new InvalidOperationException()));
        IMasterKeyRing ring = Services(Options(keyDir), builder => builder.RegisterInstance(loader).As<ISecretFileLoader>())
            .GetRequiredService<IMasterKeyRing>();

        _ = ring.RingId;
        _ = ring.ActiveKeyVersion;
        _ = ring.Wrap("value"u8, "slot"u8);

        loader.Received(1).Load(Path.Combine(keyDir, KeyRingFiles.FileName));
    }

    [TestMethod]
    public async Task RingId_ConcurrentFirstAccesses_LoadOnceAndAgree()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        var ringId = Guid.NewGuid();
        KeyRingFiles.Write(keyDir, ringId, (1, "active", RandomNumberGenerator.GetBytes(32)));
        int loads = 0;
        ISecretFileLoader loader = Substitute.For<ISecretFileLoader>();
        loader.Load(Arg.Any<string>()).Returns(call =>
        {
            Interlocked.Increment(ref loads);
            Thread.Sleep(20);
            return File.ReadAllBytes(call.Arg<string>() ?? throw new InvalidOperationException());
        });
        IMasterKeyRing ring = Services(Options(keyDir), builder => builder.RegisterInstance(loader).As<ISecretFileLoader>())
            .GetRequiredService<IMasterKeyRing>();
        using var start = new ManualResetEventSlim();

        Task<Guid>[] readers = [.. Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            start.Wait(CancellationToken);
            return ring.RingId;
        }, CancellationToken))];
        start.Set();
        Guid[] seen = await Task.WhenAll(readers);

        seen.Should().AllBeEquivalentTo(ringId);
        loads.Should().Be(1);
    }

    [TestMethod]
    public void EveryMember_RingFailedToLoad_RethrowsTheFailureAfterARepair()
    {
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        KeyRingFiles.Edit(keyDir, ring => KeyRingFiles.FirstKey(ring)["status"] = "decrypt-only");
        IMasterKeyRing keyRing = Ring(keyDir);
        Action first = () => _ = keyRing.RingId;
        first.Should().Throw<MasterKeyRingException>();
        KeyRingFiles.Edit(keyDir, ring => KeyRingFiles.FirstKey(ring)["status"] = "active");

        foreach (Action act in Members(keyRing))
        {
            act.Should().Throw<MasterKeyRingException>().Which.Failure.Should().Be(MasterKeyRingFailure.NoActiveKey);
        }
    }

    [TestMethod]
    [DataRow(nameof(IOException))]
    [DataRow(nameof(UnauthorizedAccessException))]
    public void RingId_ReadFailsOutright_FailsClosedWithFileRejectedKeepingTheCause(string kind)
    {
        // A loader that fails with an I/O error reproduces as root too, where file modes do not.
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        Exception failure = kind == nameof(IOException)
            ? new IOException("input/output error")
            : new UnauthorizedAccessException("access denied");
        ISecretFileLoader loader = Substitute.For<ISecretFileLoader>();
        loader.Load(Arg.Any<string>()).Returns<byte[]>(_ => throw failure);
        IMasterKeyRing ring = Services(Options(keyDir), builder => builder.RegisterInstance(loader).As<ISecretFileLoader>())
            .GetRequiredService<IMasterKeyRing>();

        Action act = () => _ = ring.RingId;

        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.FileRejected);
        thrown.InnerException.Should().BeSameAs(failure);
        thrown.Message.Should().Contain(Path.Combine(keyDir, KeyRingFiles.FileName));
    }

    [TestMethod]
    public void RingId_FileOpenToTheGroup_FailsClosedWithFileRejected()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        KeyRingFiles.SetMode(Path.Combine(keyDir, KeyRingFiles.FileName), KeyRingFiles.Mode0600 | UnixFileMode.GroupRead);

        RejectionOf(() => _ = Ring(keyDir).RingId).Should().Be(SecretFileRejection.WrongMode);
    }

    [TestMethod]
    public void RingId_DirectoryOpenToOthers_FailsClosedWithFileRejected()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        KeyRingFiles.SetMode(keyDir, KeyRingFiles.Mode0700 | UnixFileMode.OtherExecute);

        RejectionOf(() => _ = Ring(keyDir).RingId).Should().Be(SecretFileRejection.WrongMode);
    }

    [TestMethod]
    public void RingId_FileIsASymbolicLink_FailsClosedWithFileRejected()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        string path = Path.Combine(keyDir, KeyRingFiles.FileName);
        string target = Path.Combine(keyDir, "elsewhere.json");
        File.Move(path, target); // a valid 0600 ring, reachable only through the link
        File.CreateSymbolicLink(path, target);

        RejectionOf(() => _ = Ring(keyDir).RingId).Should().Be(SecretFileRejection.IsSymlink);
    }

    [TestMethod]
    public void RingId_FileInsideAnExcludedRoot_FailsClosedWithPathInExcludedRoot()
    {
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backed-up")).FullName;
        string keyDir = KeyRingFiles.CreateKeyDirectory(Path.Combine(backedUp, "keys"));
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "active", RandomNumberGenerator.GetBytes(32)));

        FailureOf(() => _ = Ring(keyDir, [backedUp]).RingId).Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
        FailureOf(() => _ = Ring(keyDir, [backedUp + Path.DirectorySeparatorChar]).RingId).Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
    }

    [TestMethod]
    public void RingId_FileInsideTheSecondExcludedRoot_FailsClosedWithPathInExcludedRoot()
    {
        using ITempDirectory parent = Parent();
        string served = Directory.CreateDirectory(parent.Combine("served")).FullName;
        string backedUp = Directory.CreateDirectory(parent.Combine("backed-up")).FullName;
        string keyDir = KeyRingFiles.CreateKeyDirectory(Path.Combine(backedUp, "keys"));
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "active", RandomNumberGenerator.GetBytes(32)));

        FailureOf(() => _ = Ring(keyDir, [served, backedUp]).RingId).Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
    }

    [TestMethod]
    public void RingId_FileInsideAnExcludedRootSpelledInAnotherCase_FailsClosedWithPathInExcludedRoot()
    {
        // On SMB, a NAS share or NTFS, <parent>/Backup/keys is <parent>/backup/keys: a backup would hold the ring.
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backup")).FullName;
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine(Path.Combine("Backup", "keys")));
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "active", RandomNumberGenerator.GetBytes(32)));

        FailureOf(() => _ = Ring(keyDir, [backedUp]).RingId).Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
    }

    [TestMethod]
    public void RingId_KeyDirectoryIsALinkIntoAnExcludedRoot_FailsClosedWithPathInExcludedRoot()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backed-up")).FullName;
        string realKeyDir = KeyRingFiles.CreateKeyDirectory(Path.Combine(backedUp, "keys"));
        KeyRingFiles.Write(realKeyDir, Guid.NewGuid(), (1, "active", RandomNumberGenerator.GetBytes(32)));
        string keyDir = parent.Combine("keys");
        Directory.CreateSymbolicLink(keyDir, realKeyDir);

        FailureOf(() => _ = Ring(keyDir, [backedUp]).RingId).Should().Be(MasterKeyRingFailure.PathInExcludedRoot);
    }

    [TestMethod]
    public void RingId_KeyDirectoryInASiblingSharingTheRootsPrefix_Loads()
    {
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backup")).FullName;
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine(Path.Combine("backup2", "keys")));
        var ringId = Guid.NewGuid();
        KeyRingFiles.Write(keyDir, ringId, (1, "active", RandomNumberGenerator.GetBytes(32)));

        Ring(keyDir, [backedUp]).RingId.Should().Be(ringId);
    }

    [TestMethod]
    public void RingId_KeyDirectoryIsALinkLoop_FailsClosedWithFileRejected()
    {
        SkipWithoutUnixModes();
        using ITempDirectory parent = Parent();
        string backedUp = Directory.CreateDirectory(parent.Combine("backed-up")).FullName;
        string keyDir = parent.Combine("keys");
        string other = parent.Combine("other");
        Directory.CreateSymbolicLink(keyDir, other);
        Directory.CreateSymbolicLink(other, keyDir);

        Action act = () => _ = Ring(keyDir, [backedUp]).RingId;

        // The placement check refuses it before the loader's own ELOOP could.
        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.FileRejected);
        thrown.InnerException.Should().BeOfType<IOException>();
    }

    [TestMethod]
    public void RingId_ExcludedRootThatCannotBeResolved_FailsClosedWithFileRejected()
    {
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        IPathContainment containment = Substitute.For<IPathContainment>();
        containment.MayBeWithin(Arg.Any<string>(), Arg.Any<string>()).Returns(_ => throw new UnauthorizedAccessException("cannot search"));
        IMasterKeyRing ring = Services(Options(keyDir, ["/excluded"]), builder => builder.RegisterInstance(containment).As<IPathContainment>())
            .GetRequiredService<IMasterKeyRing>();

        Action act = () => _ = ring.RingId;

        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.FileRejected);
        thrown.InnerException.Should().BeOfType<UnauthorizedAccessException>();
    }

    [TestMethod]
    [DataRow("not json at all")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void RingId_FileIsNotARingDocument_FailsClosedWithMalformed(string text)
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        KeyRingFiles.WriteText(keyDir, text);

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.Malformed);
    }

    [TestMethod]
    [DataRow("format-wrong")]
    [DataRow("format-missing")]
    [DataRow("ringId-missing")]
    [DataRow("ringId-not-a-guid")]
    [DataRow("ringId-empty-guid")]
    [DataRow("ringId-braced")]
    [DataRow("ringId-number")]
    [DataRow("keys-missing")]
    [DataRow("keys-null")]
    [DataRow("key-entry-null")]
    [DataRow("version-missing")]
    [DataRow("version-zero")]
    [DataRow("version-negative")]
    [DataRow("version-fraction")]
    [DataRow("version-duplicate")]
    [DataRow("status-missing")]
    [DataRow("status-unknown")]
    [DataRow("status-case")]
    [DataRow("key-missing")]
    [DataRow("key-not-base64")]
    [DataRow("canary-missing")]
    [DataRow("canary-nonce-missing")]
    [DataRow("canary-ciphertext-missing")]
    [DataRow("canary-nonce-not-base64")]
    [DataRow("canary-nonce-eleven-bytes")]
    [DataRow("canary-ciphertext-shorter-than-a-tag")]
    [DataRow("member-unknown-in-ring")]
    [DataRow("member-unknown-in-key")]
    [DataRow("member-unknown-in-canary")]
    public void RingId_StructurallyBrokenRing_FailsClosedWithMalformed(string breakage)
    {
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        KeyRingFiles.Edit(keyDir, ring => Break(ring, breakage));

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.Malformed);
    }

    [TestMethod]
    [DataRow("format")]
    [DataRow("ringId")]
    [DataRow("key")]
    public void RingId_DuplicatedMember_FailsClosedWithMalformed(string member)
    {
        // A second spelling of a member would let a reader of the file and the loader disagree on its value.
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        string path = Path.Combine(keyDir, KeyRingFiles.FileName);
        string text = File.ReadAllText(path);
        string duplicated = member switch
        {
            "format" => text.Replace("{\"format\":", "{\"format\":\"other\",\"format\":", StringComparison.Ordinal),
            "ringId" => text.Replace("\"ringId\":", $"\"ringId\":\"{Guid.NewGuid():D}\",\"ringId\":", StringComparison.Ordinal),
            _ => text.Replace("\"key\":", $"\"key\":\"{Convert.ToBase64String(new byte[32])}\",\"key\":", StringComparison.Ordinal),
        };
        duplicated.Should().NotBe(text);
        File.WriteAllText(path, duplicated);

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.Malformed);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(16)]
    [DataRow(31)]
    [DataRow(33)]
    public void RingId_KeyNotThirtyTwoBytes_FailsClosedWithWrongKeyLength(int length)
    {
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        KeyRingFiles.Edit(keyDir, ring => KeyRingFiles.FirstKey(ring)["key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(length)));

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.WrongKeyLength);
    }

    [TestMethod]
    public void RingId_KeyBytesReplaced_FailsClosedWithCanaryFailed()
    {
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        string replacement = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        KeyRingFiles.Edit(keyDir, ring => KeyRingFiles.FirstKey(ring)["key"] = replacement);

        Action act = () => _ = Ring(keyDir).RingId;

        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.CanaryFailed);
        thrown.Message.Should().NotContain(replacement);
        thrown.ToString().Should().NotContain(replacement);
    }

    [TestMethod]
    public void RingId_CanaryOfAnotherText_FailsClosedWithCanaryFailed()
    {
        // It authenticates under the real key but decrypts to the wrong text.
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        byte[] key = RandomNumberGenerator.GetBytes(32);
        KeyRingFiles.WriteText(keyDir, new JsonObject
        {
            ["format"] = KeyRingFiles.FormatMarker,
            ["ringId"] = Guid.NewGuid().ToString("D"),
            ["keys"] = new JsonArray(KeyRingFiles.Key(1, "active", key, canaryText: "nordstein-keyring-canary-v2")),
        }.ToJsonString());

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.CanaryFailed);
    }

    [TestMethod]
    public void RingId_CanaryUnderNonEmptyAssociatedData_FailsClosedWithCanaryFailed()
    {
        using ITempDirectory parent = Parent();
        string keyDir = NewRing(parent);
        byte[] key = KeyRingFiles.ReadKey(keyDir, 1);
        (byte[] nonce, byte[] ciphertext) = KeyRingFiles.Seal(key, Encoding.ASCII.GetBytes(KeyRingFiles.CanaryPlaintext), [1]);
        KeyRingFiles.Edit(keyDir, ring =>
        {
            JsonObject canary = KeyRingFiles.CanaryOf(KeyRingFiles.FirstKey(ring));
            canary["nonce"] = Convert.ToBase64String(nonce);
            canary["ciphertext"] = Convert.ToBase64String(ciphertext);
        });

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.CanaryFailed);
    }

    [TestMethod]
    public void RingId_DecryptOnlyKeyWithABadCanary_FailsTheWholeRing()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        JsonObject broken = KeyRingFiles.Key(2, "decrypt-only", RandomNumberGenerator.GetBytes(32));
        broken["key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        KeyRingFiles.WriteText(keyDir, new JsonObject
        {
            ["format"] = KeyRingFiles.FormatMarker,
            ["ringId"] = Guid.NewGuid().ToString("D"),
            ["keys"] = new JsonArray(KeyRingFiles.Key(1, "active", RandomNumberGenerator.GetBytes(32)), broken),
        }.ToJsonString());

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.CanaryFailed);
    }

    [TestMethod]
    public void RingId_NoActiveKey_FailsClosedWithNoActiveKey()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "decrypt-only", RandomNumberGenerator.GetBytes(32)));

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.NoActiveKey);
    }

    [TestMethod]
    public void RingId_NoKeysAtAll_FailsClosedWithNoActiveKey()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        KeyRingFiles.Write(keyDir, Guid.NewGuid());

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.NoActiveKey);
    }

    [TestMethod]
    public void RingId_TwoActiveKeys_FailsClosedWithMultipleActiveKeys()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "active", RandomNumberGenerator.GetBytes(32)), (2, "active", RandomNumberGenerator.GetBytes(32)));

        FailureOf(() => _ = Ring(keyDir).RingId).Should().Be(MasterKeyRingFailure.MultipleActiveKeys);
    }

    [TestMethod]
    public void RingId_ManyDecryptOnlyKeys_LoadsAndWrapsUnderTheActiveOne()
    {
        using ITempDirectory parent = Parent();
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        KeyRingFiles.Write(
            keyDir,
            Guid.NewGuid(),
            (3, "decrypt-only", RandomNumberGenerator.GetBytes(32)),
            (7, "active", RandomNumberGenerator.GetBytes(32)),
            (1, "decrypt-only", RandomNumberGenerator.GetBytes(32)));

        IMasterKeyRing ring = Ring(keyDir);

        ring.ActiveKeyVersion.Should().Be(7);
        ring.Wrap("value"u8, "slot"u8).KeyVersion.Should().Be(7);
    }

    [TestMethod]
    [DataRow("", null, null)]
    [DataRow("   ", null, null)]
    [DataRow("/keys", "", null)]
    [DataRow("/keys", ".", null)]
    [DataRow("/keys", "..", null)]
    [DataRow("/keys", "sub/ring.json", null)]
    [DataRow("/keys", "sub\\ring.json", null)]
    [DataRow("/keys", "ring\0.json", null)]
    [DataRow("/keys", null, " ")]
    [DataRow("/keys\0ring", null, null)]
    [DataRow("/keys", null, "/served\0x")]
    public void Resolve_InvalidOptions_FailsWithValidationForTheRingAndTheInitializer(string keyDirectory, string? fileName, string? excludedRoot)
    {
        var options = new MasterKeyRingOptions
        {
            KeyDirectory = keyDirectory,
            FileName = fileName ?? MasterKeyRingOptions.DefaultFileName,
            ExcludedRoots = excludedRoot is null ? [] : ["/served", excludedRoot],
        };
        IServiceProvider services = Services(options);

        Action ring = () => services.GetRequiredService<IMasterKeyRing>();
        Action initializer = () => services.GetRequiredService<IMasterKeyRingInitializer>();

        CauseOf(ring.Should().Throw<InvalidOperationException>().Which).Should().BeOfType<ValidationException>();
        CauseOf(initializer.Should().Throw<InvalidOperationException>().Which).Should().BeOfType<ValidationException>();
    }

    [TestMethod]
    public void Options_NullExcludedRoots_AreRefused()
    {
        IReadOnlyList<string> missing = (new IReadOnlyList<string>[1])[0];
        var options = new MasterKeyRingOptions { KeyDirectory = "/keys", ExcludedRoots = missing };

        options.Validate(new ValidationContext(options)).Should().ContainSingle()
            .Which.MemberNames.Should().Equal(nameof(MasterKeyRingOptions.ExcludedRoots));
    }

    [TestMethod]
    public void Options_Defaults_NameTheFileAndExcludeNothing()
    {
        var options = new MasterKeyRingOptions { KeyDirectory = "/keys" };

        options.FileName.Should().Be("master-key-ring.json");
        options.ExcludedRoots.Should().BeEmpty();
        options.Validate(new ValidationContext(options)).Should().BeEmpty();
    }

    [TestMethod]
    public void Resolve_WithoutOptions_Fails()
    {
        IServiceProvider services = GetServices();

        Action act = () => services.GetRequiredService<IMasterKeyRing>();

        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Container_ResolvesOneInstance()
    {
        IServiceProvider services = Services(Options("/keys"));

        services.GetRequiredService<IMasterKeyRing>().Should().BeSameAs(services.GetRequiredService<IMasterKeyRing>());
    }

    [TestMethod]
    public void Failures_HaveTheirStableNumbers()
    {
        int[] numbers =
        [
            (int)MasterKeyRingFailure.FileRejected, (int)MasterKeyRingFailure.Malformed, (int)MasterKeyRingFailure.NoActiveKey,
            (int)MasterKeyRingFailure.MultipleActiveKeys, (int)MasterKeyRingFailure.WrongKeyLength, (int)MasterKeyRingFailure.CanaryFailed,
            (int)MasterKeyRingFailure.PathInExcludedRoot, (int)MasterKeyRingFailure.UnknownKeyVersion, (int)MasterKeyRingFailure.UnwrapFailed,
            (int)MasterKeyRingFailure.AlreadyInitialized,
        ];

        numbers.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
    }

    [TestMethod]
    public void Exception_KeepsItsFailureMessageAndCause()
    {
        var cause = new IOException("cause");

        var plain = new MasterKeyRingException(MasterKeyRingFailure.Malformed, "plain");
        var wrapped = new MasterKeyRingException(MasterKeyRingFailure.FileRejected, "wrapped", cause);

        plain.Failure.Should().Be(MasterKeyRingFailure.Malformed);
        plain.Message.Should().Be("plain");
        plain.InnerException.Should().BeNull();
        wrapped.Failure.Should().Be(MasterKeyRingFailure.FileRejected);
        wrapped.InnerException.Should().BeSameAs(cause);
    }

    private static void Break(JsonObject ring, string breakage)
    {
        JsonObject key = KeyRingFiles.FirstKey(ring);
        switch (breakage)
        {
            case "format-wrong": ring["format"] = "nordstein-keyring-v2"; break;
            case "format-missing": ring.Remove("format"); break;
            case "ringId-missing": ring.Remove("ringId"); break;
            case "ringId-not-a-guid": ring["ringId"] = "not-a-guid"; break;
            case "ringId-empty-guid": ring["ringId"] = Guid.Empty.ToString("D"); break;
            case "ringId-braced": ring["ringId"] = Guid.NewGuid().ToString("B"); break;
            case "ringId-number": ring["ringId"] = 42; break;
            case "keys-missing": ring.Remove("keys"); break;
            case "keys-null": ring["keys"] = null; break;
            case "key-entry-null": ring["keys"]?.AsArray().Add(null); break;
            case "version-missing": key.Remove("version"); break;
            case "version-zero": key["version"] = 0; break;
            case "version-negative": key["version"] = -1; break;
            case "version-fraction": key["version"] = 1.5; break;
            case "version-duplicate":
                ring["keys"]?.AsArray().Add(KeyRingFiles.Key(1, "decrypt-only", RandomNumberGenerator.GetBytes(32)));
                break;
            case "status-missing": key.Remove("status"); break;
            case "status-unknown": key["status"] = "retired"; break;
            case "status-case": key["status"] = "Active"; break;
            case "key-missing": key.Remove("key"); break;
            case "key-not-base64": key["key"] = "!!! not base64 !!!"; break;
            case "canary-missing": key.Remove("canary"); break;
            case "canary-nonce-missing": KeyRingFiles.CanaryOf(key).Remove("nonce"); break;
            case "canary-ciphertext-missing": KeyRingFiles.CanaryOf(key).Remove("ciphertext"); break;
            case "canary-nonce-not-base64": KeyRingFiles.CanaryOf(key)["nonce"] = "!!! not base64 !!!"; break;
            case "canary-nonce-eleven-bytes": KeyRingFiles.CanaryOf(key)["nonce"] = Convert.ToBase64String(new byte[11]); break;
            case "canary-ciphertext-shorter-than-a-tag": KeyRingFiles.CanaryOf(key)["ciphertext"] = Convert.ToBase64String(new byte[15]); break;
            case "member-unknown-in-ring": ring["comment"] = "x"; break;
            case "member-unknown-in-key": key["kid"] = 1; break;
            case "member-unknown-in-canary": KeyRingFiles.CanaryOf(key)["tag"] = "x"; break;
            default: throw new ArgumentOutOfRangeException(nameof(breakage), breakage, "Unknown breakage.");
        }
    }

    private static Action[] Members(IMasterKeyRing ring) =>
    [
        () => _ = ring.RingId,
        () => _ = ring.ActiveKeyVersion,
        () => _ = ring.Wrap("value"u8, "slot"u8),
        () => _ = ring.Unwrap(new MasterWrappedKey(1, new WrappedKey(new byte[12], new byte[16])), "slot"u8),
    ];

    private static MasterKeyRingFailure? FailureOf(Action act)
    {
        try
        {
            act();
            return null;
        }
        catch (MasterKeyRingException exception)
        {
            return exception.Failure;
        }
    }

    private static SecretFileRejection RejectionOf(Action act)
    {
        MasterKeyRingException thrown = act.Should().Throw<MasterKeyRingException>().Which;
        thrown.Failure.Should().Be(MasterKeyRingFailure.FileRejected);
        return thrown.InnerException.Should().BeOfType<SecretFileException>().Which.Rejection;
    }

    private static MasterKeyRingOptions Options(string keyDirectory, IReadOnlyList<string>? excludedRoots = null)
        => new() { KeyDirectory = keyDirectory, ExcludedRoots = excludedRoots ?? [] };

    private IMasterKeyRing Ring(string keyDirectory, IReadOnlyList<string>? excludedRoots = null)
        => Services(Options(keyDirectory, excludedRoots)).GetRequiredService<IMasterKeyRing>();

    private IServiceProvider Services(MasterKeyRingOptions options, Action<ContainerBuilder>? configure = null)
        => GetServices(builder =>
        {
            builder.RegisterInstance(options);
            configure?.Invoke(builder);
        });

    private ITempDirectory Parent() => GetServices().GetTempDirectory("keyring-test");

    /// <summary>
    /// A valid one-key ring in a new <c>0700</c> key directory under <paramref name="parent"/>.
    /// </summary>
    private static string NewRing(ITempDirectory parent)
    {
        string keyDir = KeyRingFiles.CreateKeyDirectory(parent.Combine("keys"));
        KeyRingFiles.Write(keyDir, Guid.NewGuid(), (1, "active", RandomNumberGenerator.GetBytes(32)));
        return keyDir;
    }

    private static void SkipWithoutUnixModes()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix file modes and symbolic links are not evaluated on Windows.");
        }
    }

    /// <summary>
    /// The cause of a failed <c>GetRequiredService</c>: Microsoft's service-provider contract wraps Autofac's
    /// resolution failure in an <see cref="InvalidOperationException"/>.
    /// </summary>
    private static Exception CauseOf(InvalidOperationException thrown)
        => (thrown.InnerException ?? thrown).UnwrapResolutionFailures();
}
