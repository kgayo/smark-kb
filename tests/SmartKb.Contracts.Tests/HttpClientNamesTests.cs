using System.Reflection;

namespace SmartKb.Contracts.Tests;

public sealed class HttpClientNamesTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(HttpClientNames).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
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
        var fields = typeof(HttpClientNames).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("AzureDevOps")]
    [InlineData("SharePoint")]
    [InlineData("HubSpot")]
    [InlineData("ClickUp")]
    [InlineData("OpenAi")]
    [InlineData("oauth")]
    [InlineData("EvalNotification")]
    public void ExpectedConstants_HaveCorrectValues(string expected)
    {
        var fields = typeof(HttpClientNames).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }
}
