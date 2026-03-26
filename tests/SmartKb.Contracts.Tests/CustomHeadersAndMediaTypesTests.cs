using System.Reflection;

namespace SmartKb.Contracts.Tests;

public sealed class CustomHeadersTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(CustomHeaders).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
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
        var fields = typeof(CustomHeaders).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(nameof(CustomHeaders.CorrelationId), "X-Correlation-Id")]
    [InlineData(nameof(CustomHeaders.TenantId), "X-Tenant-Id")]
    public void ExpectedConstants_HaveCorrectValues(string fieldName, string expectedValue)
    {
        var field = typeof(CustomHeaders).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedValue, (string)field.GetValue(null)!);
    }
}

public sealed class EntraClaimTypesTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(EntraClaimTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
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
        var fields = typeof(EntraClaimTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ExpectedConstantCount()
    {
        var fields = typeof(EntraClaimTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Equal(5, fields.Length);
    }

    [Theory]
    [InlineData(nameof(EntraClaimTypes.TenantId), "tid")]
    [InlineData(nameof(EntraClaimTypes.ObjectId), "oid")]
    [InlineData(nameof(EntraClaimTypes.Subject), "sub")]
    [InlineData(nameof(EntraClaimTypes.Groups), "groups")]
    [InlineData(nameof(EntraClaimTypes.Roles), "roles")]
    public void ExpectedConstants_HaveCorrectValues(string fieldName, string expectedValue)
    {
        var field = typeof(EntraClaimTypes).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedValue, (string)field.GetValue(null)!);
    }
}

public sealed class CustomMediaTypesTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(CustomMediaTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
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
        var fields = typeof(CustomMediaTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(nameof(CustomMediaTypes.Ndjson), "application/x-ndjson")]
    [InlineData(nameof(CustomMediaTypes.TextPlainUtf8), "text/plain; charset=utf-8")]
    public void ExpectedConstants_HaveCorrectValues(string fieldName, string expectedValue)
    {
        var field = typeof(CustomMediaTypes).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedValue, (string)field.GetValue(null)!);
    }
}
