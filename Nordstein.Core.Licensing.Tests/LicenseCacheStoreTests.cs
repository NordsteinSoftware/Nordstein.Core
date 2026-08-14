using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Licensing.Internal;

namespace Nordstein.Core.Licensing.Tests;

[TestClass]
public sealed class LicenseCacheStoreTests
{
    public required TestContext TestContext { get; init; }

    private readonly string root = Path.Combine(Path.GetTempPath(), $"license-cache-tests-{Guid.NewGuid():N}");

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private LicenseCacheStore Create(string filePath)
    {
        var factory = new TestLicenseFactory();
        var config = factory.Configuration() with { CacheFilePath = filePath };
        return new LicenseCacheStore(config, NullLogger<LicenseCacheStore>.Instance);
    }

    [TestMethod]
    public void Load_MissingFile_ReturnsNull()
    {
        var store = Create(Path.Combine(root, "absent.json"));

        store.Load().Should().BeNull();
    }

    [TestMethod]
    public void Load_CorruptFile_ReturnsNull()
    {
        var path = Path.Combine(root, "corrupt.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "{not json at all");

        Create(path).Load().Should().BeNull();
    }

    [TestMethod]
    public void SaveThenLoad_RoundTrips_CreatingMissingDirectories()
    {
        // The cache path typically lives under a data dir that may not exist on first boot.
        var path = Path.Combine(root, "nested", "deeper", "cache.json");
        var store = Create(path);
        var entry = new LicenseCacheEntry("jti-1", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), "valid");

        store.Save(entry);

        store.Load().Should().Be(entry);
    }

    [TestMethod]
    public void Save_UnwritablePath_DoesNotThrow()
    {
        // A plain file where a directory is needed makes CreateDirectory fail; persistence is
        // best-effort by contract, so the failure must be swallowed (only the grace anchor
        // degrades to service start).
        Directory.CreateDirectory(root);
        var blocking = Path.Combine(root, "blocking");
        File.WriteAllText(blocking, "in the way");
        var store = Create(Path.Combine(blocking, "sub", "cache.json"));

        FluentActions
            .Invoking(() => store.Save(new LicenseCacheEntry("jti", null, null)))
            .Should().NotThrow();
    }
}
