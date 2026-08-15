using AwesomeAssertions;
using Nordstein.Core.Domain.Paging;

namespace Nordstein.Core.Domain.Tests;

[TestClass]
public sealed class PagedResultTests
{
    [TestMethod]
    public void Constructor_StoresItemsAndPagingMetadata()
    {
        IReadOnlyList<int> items = [1, 2, 3];

        var result = new PagedResult<int>(items, 42, 2, 3);

        result.Items.Should().Equal(1, 2, 3);
        result.Total.Should().Be(42);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
    }

    [TestMethod]
    public void Constructor_ForEmptyPage_HasNoItems()
    {
        var result = new PagedResult<string>([], 0, 1, 20);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [TestMethod]
    public void Map_ProjectsItemsAndPreservesPagingMetadata()
    {
        var result = new PagedResult<int>([1, 2, 3], 42, 2, 3);

        PagedResult<string> mapped = result.Map(value => value.ToString());

        mapped.Items.Should().Equal("1", "2", "3");
        mapped.Total.Should().Be(42);
        mapped.Page.Should().Be(2);
        mapped.PageSize.Should().Be(3);
    }

    [TestMethod]
    public void Map_ForEmptyPage_ReturnsEmptyItemsWithMetadata()
    {
        var result = new PagedResult<int>([], 0, 3, 10);

        PagedResult<int> mapped = result.Map(value => value * 2);

        mapped.Items.Should().BeEmpty();
        mapped.Total.Should().Be(0);
        mapped.Page.Should().Be(3);
        mapped.PageSize.Should().Be(10);
    }
}
