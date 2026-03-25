using System.Reflection;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public sealed class MessageRoleNameTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(MessageRoleName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
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
        var fields = typeof(MessageRoleName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllConstants_AreLowercase()
    {
        var fields = typeof(MessageRoleName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Equal(value.ToLowerInvariant(), value);
        }
    }

    [Theory]
    [InlineData("system")]
    [InlineData("user")]
    [InlineData("assistant")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(MessageRoleName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }

    [Fact]
    public void HasExpectedCount()
    {
        var fields = typeof(MessageRoleName).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.Equal(3, fields.Length);
    }
}
