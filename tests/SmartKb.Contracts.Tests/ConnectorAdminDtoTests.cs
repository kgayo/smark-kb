using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public sealed class ConnectorAdminDtoTests
{
    [Fact]
    public void CreateConnectorRequest_RequiredProperties_AreSet()
    {
        var request = new CreateConnectorRequest
        {
            Name = "test-connector",
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
        };

        Assert.Equal("test-connector", request.Name);
        Assert.Equal(ConnectorType.AzureDevOps, request.ConnectorType);
        Assert.Equal(SecretAuthType.Pat, request.AuthType);
        Assert.Null(request.KeyVaultSecretName);
        Assert.Null(request.SourceConfig);
        Assert.Null(request.FieldMapping);
        Assert.Null(request.ScheduleCron);
    }

    [Fact]
    public void ConnectorResponse_HasSecret_IsTrueWhenSecretNameSet()
    {
        var response = new ConnectorResponse
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ConnectorType = ConnectorType.SharePoint,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.OAuth,
            HasSecret = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        Assert.True(response.HasSecret);
    }

    [Fact]
    public void ConnectorValidationResult_Valid_ReturnsNoErrors()
    {
        var result = ConnectorValidationResult.Valid();
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ConnectorValidationResult_Invalid_ReturnsErrors()
    {
        var result = ConnectorValidationResult.Invalid("Error 1", "Error 2");
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void SyncRunSummary_DefaultValues()
    {
        var summary = new SyncRunSummary
        {
            Id = Guid.NewGuid(),
            Status = SyncRunStatus.Pending,
            IsBackfill = false,
            StartedAt = DateTimeOffset.UtcNow,
            RecordsProcessed = 0,
            RecordsFailed = 0,
        };

        Assert.Null(summary.CompletedAt);
        Assert.Null(summary.ErrorDetail);
    }

    [Fact]
    public void TestConnectionResponse_SuccessAndFailure()
    {
        var success = new TestConnectionResponse { Success = true, Message = "Connected" };
        var failure = new TestConnectionResponse { Success = false, Message = "Failed", DiagnosticDetail = "timeout" };

        Assert.True(success.Success);
        Assert.False(failure.Success);
        Assert.Equal("timeout", failure.DiagnosticDetail);
    }

    [Fact]
    public void PreviewResponse_EmptyRecordsAndErrors()
    {
        var response = new PreviewResponse { Records = [], ValidationErrors = [] };
        Assert.Empty(response.Records);
        Assert.Empty(response.ValidationErrors);
    }

    [Fact]
    public void SyncNowRequest_DefaultsToIncrementalSync()
    {
        var request = new SyncNowRequest();
        Assert.False(request.IsBackfill);
        Assert.Null(request.IdempotencyKey);
    }

    [Fact]
    public void PreviewRequest_DefaultSampleSize()
    {
        var request = new PreviewRequest();
        Assert.Equal(5, request.SampleSize);
    }

    [Fact]
    public void ConnectorListResponse_EmptyList()
    {
        var response = new ConnectorListResponse { Connectors = [], TotalCount = 0 };
        Assert.Empty(response.Connectors);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public void UpdateConnectorRequest_AllFieldsNullable()
    {
        var request = new UpdateConnectorRequest();
        Assert.Null(request.Name);
        Assert.Null(request.SourceConfig);
        Assert.Null(request.FieldMapping);
        Assert.Null(request.ScheduleCron);
        Assert.Null(request.KeyVaultSecretName);
        Assert.Null(request.AuthType);
    }
}
