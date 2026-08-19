using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Io;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class SecretFileLoaderTests : BaseTest<Module>
{
    private const UnixFileMode Mode0600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode Mode0700 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private ISecretFileLoader Loader => GetServices().GetRequiredService<ISecretFileLoader>();

    private ITempDirectory NewTempDirectory()
        => GetServices().GetRequiredService<ITempDirectory.Create>()(null, "secret-file-test");

    [TestMethod]
    public void Load_WithValidModeAndParent_ReturnsBytes()
    {
        SkipIfUnixModesUnavailable();
        using ITempDirectory temp = NewTempDirectory();
        string secret = CreateSecretFile(temp, "key.bin", "0600 file"u8.ToArray());

        byte[] bytes = Loader.Load(secret);

        bytes.Should().Equal("0600 file"u8.ToArray());
    }

    [TestMethod]
    public void Load_WhenMissing_ThrowsMissing()
    {
        using ITempDirectory temp = NewTempDirectory();

        Action act = () => Loader.Load(temp.Combine("absent.bin"));

        act.Should().Throw<SecretFileException>()
            .Which.Rejection.Should().Be(SecretFileRejection.Missing);
    }

    [TestMethod]
    public void Load_WhenFileMode0644_ThrowsWrongMode()
    {
        SkipIfUnixModesUnavailable();
        using ITempDirectory temp = NewTempDirectory();
        string dir = CreateDirectory(temp, "vault", Mode0700);
        string secret = Path.Combine(dir, "key.bin");
        File.WriteAllBytes(secret, "loose"u8.ToArray());
        File.SetUnixFileMode(secret, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        Action act = () => Loader.Load(secret);

        act.Should().Throw<SecretFileException>()
            .Which.Rejection.Should().Be(SecretFileRejection.WrongMode);
    }

    [TestMethod]
    public void Load_WhenParentMode0755_ThrowsWrongMode()
    {
        SkipIfUnixModesUnavailable();
        using ITempDirectory temp = NewTempDirectory();
        string dir = CreateDirectory(temp, "vault", Mode0700 | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        string secret = Path.Combine(dir, "key.bin");
        File.WriteAllBytes(secret, "content"u8.ToArray());
        File.SetUnixFileMode(secret, Mode0600);

        Action act = () => Loader.Load(secret);

        act.Should().Throw<SecretFileException>()
            .Which.Rejection.Should().Be(SecretFileRejection.WrongMode);
    }

    [TestMethod]
    public void Load_WhenSymlink_ThrowsIsSymlink()
    {
        SkipIfUnixModesUnavailable();
        using ITempDirectory temp = NewTempDirectory();
        string secret = CreateSecretFile(temp, "real.bin", "target"u8.ToArray());
        string link = temp.Combine("link.bin");
        File.CreateSymbolicLink(link, secret);

        Action act = () => Loader.Load(link);

        act.Should().Throw<SecretFileException>()
            .Which.Rejection.Should().Be(SecretFileRejection.IsSymlink);
    }

    [TestMethod]
    public void TryLoad_WhenMissing_ReturnsFalseWithoutThrowing()
    {
        using ITempDirectory temp = NewTempDirectory();

        bool loaded = Loader.TryLoad(temp.Combine("absent.bin"), out byte[] bytes, out SecretFileRejection rejection);

        loaded.Should().BeFalse();
        bytes.Should().BeEmpty();
        rejection.Should().Be(SecretFileRejection.Missing);
    }

    [TestMethod]
    public void TryLoad_WithValidFile_ReturnsTrueAndNone()
    {
        SkipIfUnixModesUnavailable();
        using ITempDirectory temp = NewTempDirectory();
        string secret = CreateSecretFile(temp, "key.bin", "ok"u8.ToArray());

        bool loaded = Loader.TryLoad(secret, out byte[] bytes, out SecretFileRejection rejection);

        loaded.Should().BeTrue();
        rejection.Should().Be(SecretFileRejection.None);
        bytes.Should().Equal("ok"u8.ToArray());
    }

    private string CreateSecretFile(ITempDirectory temp, string name, byte[] content)
    {
        string dir = CreateDirectory(temp, "vault", Mode0700);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, content);
        File.SetUnixFileMode(path, Mode0600);
        return path;
    }

    private static string CreateDirectory(ITempDirectory temp, string name, UnixFileMode mode)
    {
        string dir = temp.Combine(name);
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, mode);
        return dir;
    }

    private static void SkipIfUnixModesUnavailable()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix file-mode custody checks are not applicable on Windows.");
        }
    }
}
