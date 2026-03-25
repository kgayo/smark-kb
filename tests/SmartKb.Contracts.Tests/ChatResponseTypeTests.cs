using System.Reflection;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public sealed class ChatResponseTypeTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(ChatResponseType).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(string));
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
        var fields = typeof(ChatResponseType).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(string));
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllConstants_UseSnakeCase()
    {
        Assert.Equal("final_answer", ChatResponseType.FinalAnswer);
        Assert.Equal("next_steps_only", ChatResponseType.NextStepsOnly);
        Assert.Equal("escalate", ChatResponseType.Escalate);
    }

    [Fact]
    public void AllValues_ContainsAllConstants()
    {
        Assert.Equal(3, ChatResponseType.AllValues.Length);
        Assert.Contains(ChatResponseType.FinalAnswer, ChatResponseType.AllValues);
        Assert.Contains(ChatResponseType.NextStepsOnly, ChatResponseType.AllValues);
        Assert.Contains(ChatResponseType.Escalate, ChatResponseType.AllValues);
    }
}

public sealed class ConfidenceLabelTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(ConfidenceLabel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
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
        var fields = typeof(ConfidenceLabel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void AllConstants_ArePascalCase()
    {
        var fields = typeof(ConfidenceLabel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Equal(field.Name, value);
        }
    }

    [Theory]
    [InlineData("High")]
    [InlineData("Medium")]
    [InlineData("Low")]
    public void ExpectedConstants_Exist(string expected)
    {
        var fields = typeof(ConfidenceLabel).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Contains(expected, values);
    }
}
