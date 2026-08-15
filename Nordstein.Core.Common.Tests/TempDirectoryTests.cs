using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Lifecycle;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class TempDirectoryTests : BaseTest<Module>
{
    private ITempDirectory.Create ResolveFactory()
        => GetServices().GetRequiredService<ITempDirectory.Create>();

    [TestMethod]
    public void Create_CreatesDirectoryThatExists()
    {
        ITempDirectory.Create factory = ResolveFactory();

        using ITempDirectory temp = factory(null, null);

        Directory.Exists(temp.Path).Should().BeTrue();
    }

    [TestMethod]
    public void Create_AllowsWritingFiles()
    {
        ITempDirectory.Create factory = ResolveFactory();

        using ITempDirectory temp = factory(null, null);
        string file = temp.Combine("data.txt");
        File.WriteAllText(file, "hello");

        File.Exists(file).Should().BeTrue();
        File.ReadAllText(file).Should().Be("hello");
    }

    [TestMethod]
    public void Combine_ReturnsPathUnderTheTempDirectory()
    {
        ITempDirectory.Create factory = ResolveFactory();

        using ITempDirectory temp = factory(null, null);
        string combined = temp.Combine("child.txt");

        combined.Should().StartWith(temp.Path);
    }

    [TestMethod]
    public void Dispose_RemovesTheDirectory()
    {
        ITempDirectory.Create factory = ResolveFactory();
        ITempDirectory temp = factory(null, null);
        string path = temp.Path;
        Directory.Exists(path).Should().BeTrue();

        temp.Dispose();

        Directory.Exists(path).Should().BeFalse();
    }

    [TestMethod]
    public void Dispose_RemovesDirectoryEvenWhenItContainsFiles()
    {
        ITempDirectory.Create factory = ResolveFactory();
        ITempDirectory temp = factory(null, null);
        string path = temp.Path;
        File.WriteAllText(temp.Combine("file.txt"), "content");

        temp.Dispose();

        Directory.Exists(path).Should().BeFalse();
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        ITempDirectory.Create factory = ResolveFactory();
        ITempDirectory temp = factory(null, null);
        temp.Dispose();

        var act = temp.Dispose;

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Create_WithPrefix_HonorsPrefixInDirectoryName()
    {
        ITempDirectory.Create factory = ResolveFactory();
        const string prefix = "nordstein-test";

        using ITempDirectory temp = factory(null, prefix);

        Path.GetFileName(temp.Path).Should().StartWith(prefix + "_");
    }

    [TestMethod]
    public void Create_WithoutPrefix_ProducesADistinctDirectoryPerCall()
    {
        ITempDirectory.Create factory = ResolveFactory();

        using ITempDirectory first = factory(null, null);
        using ITempDirectory second = factory(null, null);

        second.Path.Should().NotBe(first.Path);
    }

    [TestMethod]
    public void Create_WithParentDirectory_CreatesUnderThatParent()
    {
        ITempDirectory.Create factory = ResolveFactory();
        using ITempDirectory parent = factory(null, null);

        using ITempDirectory child = factory(parent.Path, null);

        Directory.Exists(child.Path).Should().BeTrue();
        Path.GetDirectoryName(child.Path).Should().Be(parent.Path);
    }
}
