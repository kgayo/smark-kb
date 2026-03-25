using System.Reflection;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public sealed class WorkflowStatusTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(WorkflowStatus).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(WorkflowStatus).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllConstants_ArePascalCase()
    {
        var fields = typeof(WorkflowStatus).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Equal(field.Name, value);
        }
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Processing")]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Resolved")]
    [InlineData("Applied")]
    [InlineData("Dismissed")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(WorkflowStatus).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }
}

public sealed class VisibilityLevelTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(VisibilityLevel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(VisibilityLevel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllConstants_ArePascalCase()
    {
        var fields = typeof(VisibilityLevel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Equal(field.Name, value);
        }
    }

    [Theory]
    [InlineData("Internal")]
    [InlineData("Restricted")]
    [InlineData("Public")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(VisibilityLevel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }

    [Fact]
    public void Constants_MatchAccessVisibilityEnumNames()
    {
        Assert.Equal(nameof(SmartKb.Contracts.Enums.AccessVisibility.Internal), VisibilityLevel.Internal);
        Assert.Equal(nameof(SmartKb.Contracts.Enums.AccessVisibility.Restricted), VisibilityLevel.Restricted);
        Assert.Equal(nameof(SmartKb.Contracts.Enums.AccessVisibility.Public), VisibilityLevel.Public);
    }
}

public sealed class TrustLevelNameTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(TrustLevelName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(TrustLevelName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllConstants_ArePascalCase()
    {
        var fields = typeof(TrustLevelName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Equal(field.Name, value);
        }
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Reviewed")]
    [InlineData("Approved")]
    [InlineData("Deprecated")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(TrustLevelName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }

    [Fact]
    public void Constants_MatchTrustLevelEnumNames()
    {
        Assert.Equal(nameof(SmartKb.Contracts.Enums.TrustLevel.Draft), TrustLevelName.Draft);
        Assert.Equal(nameof(SmartKb.Contracts.Enums.TrustLevel.Reviewed), TrustLevelName.Reviewed);
        Assert.Equal(nameof(SmartKb.Contracts.Enums.TrustLevel.Approved), TrustLevelName.Approved);
        Assert.Equal(nameof(SmartKb.Contracts.Enums.TrustLevel.Deprecated), TrustLevelName.Deprecated);
    }
}

public sealed class ContradictionResolutionTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(ContradictionResolution).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(ContradictionResolution).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Theory]
    [InlineData("Merged")]
    [InlineData("Deprecated")]
    [InlineData("Kept")]
    [InlineData("Dismissed")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(ContradictionResolution).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }
}

public sealed class EscalationExternalStatusTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(EscalationExternalStatus).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(EscalationExternalStatus).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Created")]
    [InlineData("Completed")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(EscalationExternalStatus).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }
}
