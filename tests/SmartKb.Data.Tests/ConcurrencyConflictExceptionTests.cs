using SmartKb.Data.Exceptions;

namespace SmartKb.Data.Tests;

public class ConcurrencyConflictExceptionTests
{
    [Fact]
    public void Constructor_SetsEntityNameAndMessage()
    {
        var ex = new ConcurrencyConflictException("synonym rule");

        Assert.Equal("synonym rule", ex.EntityName);
        Assert.Contains("synonym rule", ex.Message);
        Assert.Contains("modified by another request", ex.Message);
    }

    [Fact]
    public void Constructor_WithInnerException_PreservesInnerException()
    {
        var inner = new InvalidOperationException("db conflict");
        var ex = new ConcurrencyConflictException("stop word", inner);

        Assert.Equal("stop word", ex.EntityName);
        Assert.Same(inner, ex.InnerException);
    }
}
