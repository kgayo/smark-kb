using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class PaginationDefaultsTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(1, PaginationDefaults.MinPageSize);
        Assert.Equal(100, PaginationDefaults.MaxPageSize);
        Assert.Equal(200, PaginationDefaults.AuditMaxPageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(999, 100)]
    public void ClampPageSize_ClampsToStandardRange(int input, int expected)
    {
        Assert.Equal(expected, PaginationDefaults.ClampPageSize(input));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(150, 150)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(999, 200)]
    public void ClampAuditPageSize_ClampsToAuditRange(int input, int expected)
    {
        Assert.Equal(expected, PaginationDefaults.ClampAuditPageSize(input));
    }
}
