using AwesomeAssertions;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class ServiceProviderExtensionsTests : BaseTest<Module>
{
    [TestMethod]
    public void GetTempDirectory_WithDefaultPrefix_CreatesExistingDirectory()
    {
        IServiceProvider services = GetServices();

        using ITempDirectory temp = services.GetTempDirectory();

        temp.Path.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(temp.Path).Should().BeTrue();
    }

    [TestMethod]
    public void GetTempDirectory_WithPrefix_HonorsPrefixInDirectoryName()
    {
        IServiceProvider services = GetServices();

        using ITempDirectory temp = services.GetTempDirectory(prefix: "nordstein-test");

        Path.GetFileName(temp.Path).Should().StartWith("nordstein-test_");
        Directory.Exists(temp.Path).Should().BeTrue();
    }

    [TestMethod]
    public void GetTempDirectory_CalledTwice_ReturnsDistinctExistingDirectories()
    {
        IServiceProvider services = GetServices();

        using ITempDirectory first = services.GetTempDirectory(prefix: "nordstein-test");
        using ITempDirectory second = services.GetTempDirectory(prefix: "nordstein-test");

        first.Path.Should().NotBe(second.Path);
        Directory.Exists(first.Path).Should().BeTrue();
        Directory.Exists(second.Path).Should().BeTrue();
    }
}
