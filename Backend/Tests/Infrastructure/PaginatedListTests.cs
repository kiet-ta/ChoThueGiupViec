using CommonService.Application.Common.Helpers;

namespace CommonService.Tests.Infrastructure;

public class PaginatedListTests
{
    [Fact]
    public void Constructor_calculates_pages_correctly()
    {
        var items = new List<int> { 1, 2, 3, 4, 5 };
        var list = new PaginatedList<int>(items, count: 25, pageNumber: 1, pageSize: 10);

        Assert.Equal(5, list.Items.Count);
        Assert.Equal(25, list.TotalCount);
        Assert.Equal(1, list.PageNumber);
        Assert.Equal(10, list.PageSize);
        Assert.Equal(3, list.TotalPages);
        Assert.False(list.HasPreviousPage);
        Assert.True(list.HasNextPage);
    }

    [Fact]
    public void HasPreviousPage_and_HasNextPage_on_middle_and_last_page()
    {
        var items = new List<int> { 11, 12, 13 };
        var middle = new PaginatedList<int>(items, count: 30, pageNumber: 2, pageSize: 10);
        Assert.True(middle.HasPreviousPage);
        Assert.True(middle.HasNextPage);

        var last = new PaginatedList<int>(items, count: 30, pageNumber: 3, pageSize: 10);
        Assert.True(last.HasPreviousPage);
        Assert.False(last.HasNextPage);
    }

    [Fact]
    public void Create_slices_source_collection_properly()
    {
        var source = Enumerable.Range(1, 50).ToList();

        // Page 1
        var page1 = PaginatedList<int>.Create(source, pageNumber: 1, pageSize: 10);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(1, page1.Items.First());
        Assert.Equal(10, page1.Items.Last());
        Assert.Equal(5, page1.TotalPages);

        // Page 3
        var page3 = PaginatedList<int>.Create(source, pageNumber: 3, pageSize: 10);
        Assert.Equal(10, page3.Items.Count);
        Assert.Equal(21, page3.Items.First());
        Assert.Equal(30, page3.Items.Last());

        // Page 5 (last page)
        var page5 = PaginatedList<int>.Create(source, pageNumber: 5, pageSize: 10);
        Assert.Equal(10, page5.Items.Count);
        Assert.Equal(41, page5.Items.First());
        Assert.Equal(50, page5.Items.Last());
        Assert.False(page5.HasNextPage);
    }

    [Fact]
    public void Empty_source_returns_zero_pages()
    {
        var empty = PaginatedList<string>.Create(Enumerable.Empty<string>(), 1, 10);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);
        Assert.False(empty.HasPreviousPage);
        Assert.False(empty.HasNextPage);
    }

    [Fact]
    public void Clamps_negative_or_zero_page_inputs()
    {
        var list = new PaginatedList<int>(Array.Empty<int>(), count: -5, pageNumber: -1, pageSize: 0);
        Assert.Equal(1, list.PageNumber);
        Assert.Equal(10, list.PageSize);
        Assert.Equal(0, list.TotalCount);
    }
}
