using Azure.Search.Documents.Models;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Tests;

public class SearchDocumentHelperTests
{
    [Fact]
    public void GetString_ReturnsValue_WhenKeyExists()
    {
        var doc = new SearchDocument(new Dictionary<string, object> { ["title"] = "Hello" });
        Assert.Equal("Hello", SearchDocumentHelper.GetString(doc, "title"));
    }

    [Fact]
    public void GetString_ReturnsEmpty_WhenKeyMissing()
    {
        var doc = new SearchDocument(new Dictionary<string, object>());
        Assert.Equal(string.Empty, SearchDocumentHelper.GetString(doc, "title"));
    }

    [Fact]
    public void GetString_ReturnsEmpty_WhenValueIsNotString()
    {
        var doc = new SearchDocument(new Dictionary<string, object> { ["title"] = 42 });
        Assert.Equal(string.Empty, SearchDocumentHelper.GetString(doc, "title"));
    }

    [Fact]
    public void GetStringOrNull_ReturnsValue_WhenKeyExists()
    {
        var doc = new SearchDocument(new Dictionary<string, object> { ["area"] = "Billing" });
        Assert.Equal("Billing", SearchDocumentHelper.GetStringOrNull(doc, "area"));
    }

    [Fact]
    public void GetStringOrNull_ReturnsNull_WhenKeyMissing()
    {
        var doc = new SearchDocument(new Dictionary<string, object>());
        Assert.Null(SearchDocumentHelper.GetStringOrNull(doc, "area"));
    }

    [Fact]
    public void GetStringOrNull_ReturnsNull_WhenValueIsEmpty()
    {
        var doc = new SearchDocument(new Dictionary<string, object> { ["area"] = "" });
        Assert.Null(SearchDocumentHelper.GetStringOrNull(doc, "area"));
    }

    [Fact]
    public void GetStringList_ReturnsList_WhenStrings()
    {
        var doc = new SearchDocument(new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "tag1", "tag2" },
        });
        var result = SearchDocumentHelper.GetStringList(doc, "tags");
        Assert.Equal(2, result.Count);
        Assert.Equal("tag1", result[0]);
        Assert.Equal("tag2", result[1]);
    }

    [Fact]
    public void GetStringList_ReturnsList_WhenObjects()
    {
        var doc = new SearchDocument(new Dictionary<string, object>
        {
            ["tags"] = new List<object> { "a", "b" },
        });
        var result = SearchDocumentHelper.GetStringList(doc, "tags");
        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0]);
    }

    [Fact]
    public void GetStringList_ReturnsEmpty_WhenKeyMissing()
    {
        var doc = new SearchDocument(new Dictionary<string, object>());
        Assert.Empty(SearchDocumentHelper.GetStringList(doc, "tags"));
    }

    [Fact]
    public void GetStringList_ReturnsEmpty_WhenValueIsNotCollection()
    {
        var doc = new SearchDocument(new Dictionary<string, object> { ["tags"] = "single" });
        Assert.Empty(SearchDocumentHelper.GetStringList(doc, "tags"));
    }
}
