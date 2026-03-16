using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
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

    [Fact]
    public void AuditEvent_StoresAllFields()
    {
        var ts = DateTimeOffset.UtcNow;
        var evt = new Models.AuditEvent("e1", "test.type", "t1", "u1", "c1", ts, "detail text");
        Assert.Equal("e1", evt.EventId);
        Assert.Equal("test.type", evt.EventType);
        Assert.Equal("t1", evt.TenantId);
        Assert.Equal("u1", evt.ActorId);
        Assert.Equal("c1", evt.CorrelationId);
        Assert.Equal(ts, evt.Timestamp);
        Assert.Equal("detail text", evt.Detail);
    }

    [Fact]
    public void AuditEvent_IsImmutableRecord()
    {
        var evt1 = new Models.AuditEvent("e1", "type", "t", "u", "c", DateTimeOffset.UtcNow, "d");
        var evt2 = evt1 with { EventId = "e2" };
        Assert.NotEqual(evt1.EventId, evt2.EventId);
        Assert.Equal(evt1.EventType, evt2.EventType);
    }

    [Theory]
    [InlineData(SecretAuthType.OAuth)]
    [InlineData(SecretAuthType.Pat)]
    [InlineData(SecretAuthType.PrivateKey)]
    [InlineData(SecretAuthType.ServiceAccount)]
    public void SecretAuthType_AllValuesAreDefined(SecretAuthType authType)
    {
        Assert.True(Enum.IsDefined(authType));
    }

    [Fact]
    public void SecretAuthType_HasExpectedCount()
    {
        Assert.Equal(4, Enum.GetValues<SecretAuthType>().Length);
    }

    [Fact]
    public void OpenAiSettings_HasCorrectDefaults()
    {
        var settings = new OpenAiSettings();

        Assert.Equal(string.Empty, settings.ApiKey);
        Assert.Equal("gpt-4o", settings.Model);
        Assert.Equal("https://api.openai.com/v1", settings.Endpoint);
    }

    [Fact]
    public void OpenAiSettings_SectionName_IsCorrect()
    {
        Assert.Equal("OpenAi", OpenAiSettings.SectionName);
    }

    [Fact]
    public void KeyVaultSettings_HasCorrectDefaults()
    {
        var settings = new KeyVaultSettings();

        Assert.Equal(string.Empty, settings.VaultUri);
    }

    [Fact]
    public void KeyVaultSettings_SectionName_IsCorrect()
    {
        Assert.Equal("KeyVault", KeyVaultSettings.SectionName);
    }
}
