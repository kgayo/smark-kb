using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class EvalCaseIdTests
{
    [Theory]
    [InlineData("eval-00001", true)]
    [InlineData("eval-99999", true)]
    [InlineData("eval-12345", true)]
    public void IsValid_ValidIds_ReturnsTrue(string id, bool expected)
    {
        Assert.Equal(expected, EvalCaseId.IsValid(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("eval-")]
    [InlineData("eval-1234")]         // too short (9 chars)
    [InlineData("eval-123456")]       // too long (11 chars)
    [InlineData("test-00001")]        // wrong prefix
    [InlineData("EVAL-00001")]        // case-sensitive prefix
    public void IsValid_InvalidIds_ReturnsFalse(string? id)
    {
        Assert.False(EvalCaseId.IsValid(id));
    }

    [Fact]
    public void Prefix_IsEvalDash()
    {
        Assert.Equal("eval-", EvalCaseId.Prefix);
    }

    [Fact]
    public void ExpectedLength_Is10()
    {
        Assert.Equal(10, EvalCaseId.ExpectedLength);
    }
}
