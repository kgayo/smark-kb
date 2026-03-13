using SmartKb.Contracts;
using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Tests;

public class RolePermissionsTests
{
    [Fact]
    public void AllRolesHavePermissions()
    {
        foreach (var role in Enum.GetValues<AppRole>())
        {
            Assert.True(
                RolePermissions.Matrix.ContainsKey(role),
                $"Role {role} missing from permission matrix");
        }
    }

    [Theory]
    [InlineData(AppRole.SupportAgent, "chat:query", true)]
    [InlineData(AppRole.SupportAgent, "connector:manage", false)]
    [InlineData(AppRole.Admin, "connector:manage", true)]
    [InlineData(AppRole.Admin, "audit:read", true)]
    [InlineData(AppRole.SecurityAuditor, "audit:read", true)]
    [InlineData(AppRole.SecurityAuditor, "connector:manage", false)]
    [InlineData(AppRole.EngineeringViewer, "report:read", true)]
    [InlineData(AppRole.EngineeringViewer, "chat:query", false)]
    [InlineData(AppRole.SupportLead, "pattern:approve", true)]
    public void HasPermission_ReturnsExpectedResult(AppRole role, string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.HasPermission(role, permission));
    }

    [Fact]
    public void ApiResponse_Success_HasNoError()
    {
        var response = Models.ApiResponse<string>.Success("test", "corr-123");
        Assert.True(response.IsSuccess);
        Assert.Null(response.Error);
        Assert.Equal("test", response.Data);
        Assert.Equal("corr-123", response.CorrelationId);
    }

    [Fact]
    public void ApiResponse_Failure_HasError()
    {
        var response = Models.ApiResponse<string>.Failure("bad request", "corr-456");
        Assert.False(response.IsSuccess);
        Assert.Equal("bad request", response.Error);
        Assert.Null(response.Data);
    }

    [Fact]
    public void TenantContext_StoresValues()
    {
        var ctx = new Models.TenantContext("tenant-1", "user-1", "corr-1");
        Assert.Equal("tenant-1", ctx.TenantId);
        Assert.Equal("user-1", ctx.UserId);
        Assert.Equal("corr-1", ctx.CorrelationId);
    }
}
