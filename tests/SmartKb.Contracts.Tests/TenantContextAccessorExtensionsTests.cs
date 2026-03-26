using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class TenantContextAccessorExtensionsTests
{
    [Fact]
    public void GetRequiredTenant_ReturnsContext_WhenCurrentIsSet()
    {
        var expected = new TenantContext("tenant-1", "user-1", "corr-1");
        var accessor = new TestTenantContextAccessor { Current = expected };

        var result = accessor.GetRequiredTenant();

        Assert.Same(expected, result);
    }

    [Fact]
    public void GetRequiredTenant_ThrowsInvalidOperationException_WhenCurrentIsNull()
    {
        var accessor = new TestTenantContextAccessor { Current = null };

        var ex = Assert.Throws<InvalidOperationException>(() => accessor.GetRequiredTenant());

        Assert.Contains("Tenant context is not available", ex.Message);
    }

    [Fact]
    public void GetRequiredTenant_ReturnsFullContext_WithUserGroups()
    {
        var expected = new TenantContext("tenant-2", "user-2", "corr-2", new[] { "group-a", "group-b" });
        var accessor = new TestTenantContextAccessor { Current = expected };

        var result = accessor.GetRequiredTenant();

        Assert.Equal("tenant-2", result.TenantId);
        Assert.Equal(2, result.UserGroups.Count);
    }

    private sealed class TestTenantContextAccessor : ITenantContextAccessor
    {
        public TenantContext? Current { get; set; }
    }
}
