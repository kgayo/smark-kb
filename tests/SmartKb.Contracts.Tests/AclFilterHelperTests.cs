using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Tests;

public class AclFilterHelperTests
{
    [Fact]
    public void ApplyAclFilter_PublicDocuments_AlwaysPassThrough()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Public),
            CreateItem("c2", visibility: VisibilityLevel.Public),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, null);

        Assert.Equal(2, filtered.Count);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_InternalDocuments_AlwaysPassThrough()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Internal),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, null);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_FilteredWhenNoUserGroups()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Restricted, allowedGroups: ["team-a"]),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, null);

        Assert.Empty(filtered);
        Assert.Equal(1, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_FilteredWhenEmptyUserGroups()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Restricted, allowedGroups: ["team-a"]),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, []);

        Assert.Empty(filtered);
        Assert.Equal(1, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_PassWhenUserInAllowedGroup()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Restricted, allowedGroups: ["team-a", "team-b"]),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, ["team-b"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_CaseInsensitiveGroupMatching()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Restricted, allowedGroups: ["Team-A"]),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, ["team-a"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_CaseInsensitiveVisibility()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: "RESTRICTED", allowedGroups: ["team-a"]),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, ["team-a"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_MixedVisibility_CorrectCounts()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Public),
            CreateItem("c2", visibility: VisibilityLevel.Internal),
            CreateItem("c3", visibility: VisibilityLevel.Restricted, allowedGroups: ["team-a"]),
            CreateItem("c4", visibility: VisibilityLevel.Restricted, allowedGroups: ["team-b"]),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, ["team-a"]);

        Assert.Equal(3, filtered.Count);
        Assert.Equal(1, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_EmptyResults_ReturnsEmpty()
    {
        var results = new List<TestAclItem>();

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, ["team-a"]);

        Assert.Empty(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedWithNoAllowedGroups_FilteredOut()
    {
        var results = new List<TestAclItem>
        {
            CreateItem("c1", visibility: VisibilityLevel.Restricted, allowedGroups: []),
        };

        var (filtered, filteredOut) = AclFilterHelper.ApplyAclFilter(results, ["team-a"]);

        Assert.Empty(filtered);
        Assert.Equal(1, filteredOut);
    }

    private static TestAclItem CreateItem(string id, string visibility = "Internal", IReadOnlyList<string>? allowedGroups = null)
    {
        return new TestAclItem
        {
            Id = id,
            Visibility = visibility,
            AllowedGroups = allowedGroups ?? [],
        };
    }
}

internal sealed class TestAclItem : IAclFilterable
{
    public required string Id { get; init; }
    public required string Visibility { get; init; }
    public IReadOnlyList<string> AllowedGroups { get; init; } = [];
}
