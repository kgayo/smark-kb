using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public class SyncJobMessageTests
{
    [Fact]
    public void SyncJobMessage_RequiredProperties_AreSet()
    {
        var id = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var message = new SyncJobMessage
        {
            SyncRunId = id,
            ConnectorId = connectorId,
            TenantId = "tenant-1",
            ConnectorType = ConnectorType.AzureDevOps,
            IsBackfill = true,
            CorrelationId = "corr-123",
            EnqueuedAt = now,
        };

        Assert.Equal(id, message.SyncRunId);
        Assert.Equal(connectorId, message.ConnectorId);
        Assert.Equal("tenant-1", message.TenantId);
        Assert.Equal(ConnectorType.AzureDevOps, message.ConnectorType);
        Assert.True(message.IsBackfill);
        Assert.Equal("corr-123", message.CorrelationId);
        Assert.Equal(now, message.EnqueuedAt);
    }

    [Fact]
    public void SyncJobMessage_OptionalProperties_DefaultToNull()
    {
        var message = new SyncJobMessage
        {
            SyncRunId = Guid.NewGuid(),
            ConnectorId = Guid.NewGuid(),
            TenantId = "t",
            ConnectorType = ConnectorType.SharePoint,
            IsBackfill = false,
            CorrelationId = "c",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        Assert.Null(message.SourceConfig);
        Assert.Null(message.FieldMapping);
        Assert.Null(message.KeyVaultSecretName);
        Assert.Null(message.Checkpoint);
        Assert.Equal(SecretAuthType.OAuth, message.AuthType); // enum default
    }

    [Fact]
    public void FetchResult_RequiredProperties_AreSet()
    {
        var records = new List<CanonicalRecord>();
        var errors = new List<string> { "error1" };

        var result = new FetchResult
        {
            Records = records,
            FailedRecords = 3,
            Errors = errors,
            NewCheckpoint = "cp-1",
            HasMore = true,
        };

        Assert.Same(records, result.Records);
        Assert.Equal(3, result.FailedRecords);
        Assert.Single(result.Errors);
        Assert.Equal("cp-1", result.NewCheckpoint);
        Assert.True(result.HasMore);
    }

    [Fact]
    public void FetchResult_NewCheckpoint_DefaultsToNull()
    {
        var result = new FetchResult
        {
            Records = [],
            FailedRecords = 0,
            Errors = [],
        };

        Assert.Null(result.NewCheckpoint);
        Assert.False(result.HasMore);
    }

    [Fact]
    public void ServiceBusSettings_HasCorrectDefaults()
    {
        var settings = new Configuration.ServiceBusSettings();

        Assert.Equal("", settings.FullyQualifiedNamespace);
        Assert.Equal("", settings.ConnectionString);
        Assert.Equal("ingestion-jobs", settings.QueueName);
        Assert.Equal(10, settings.MaxDeliveryCount);
        Assert.Equal(5, settings.MaxConcurrentCalls);
        Assert.Equal("ServiceBus", Configuration.ServiceBusSettings.SectionName);
        Assert.False(settings.IsConfigured);
        Assert.False(settings.UsesManagedIdentity);
    }

    [Fact]
    public void ServiceBusSettings_IsConfigured_TrueWhenNamespaceSet()
    {
        var settings = new Configuration.ServiceBusSettings
        {
            FullyQualifiedNamespace = "sb-smartkb-dev.servicebus.windows.net",
        };

        Assert.True(settings.IsConfigured);
        Assert.True(settings.UsesManagedIdentity);
    }

    [Fact]
    public void ServiceBusSettings_IsConfigured_TrueWhenConnectionStringSet()
    {
        var settings = new Configuration.ServiceBusSettings
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=key;SharedAccessKey=val",
        };

        Assert.True(settings.IsConfigured);
        Assert.False(settings.UsesManagedIdentity);
    }

    [Fact]
    public void ServiceBusSettings_UsesManagedIdentity_PrefersNamespaceOverConnectionString()
    {
        var settings = new Configuration.ServiceBusSettings
        {
            FullyQualifiedNamespace = "sb-smartkb-dev.servicebus.windows.net",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=key;SharedAccessKey=val",
        };

        Assert.True(settings.UsesManagedIdentity);
    }
}
