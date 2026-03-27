using SmartKb.Contracts.Enums;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Tests;

public sealed class ConnectorEntityExtensionsTests
{
    private static ConnectorEntity CreateConnector() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = "tenant-1",
        Name = "Test Connector",
        ConnectorType = ConnectorType.AzureDevOps,
        Status = ConnectorStatus.Enabled,
        AuthType = SecretAuthType.Pat,
        KeyVaultSecretName = "kv-secret-ado",
        SourceConfig = """{"org":"contoso"}""",
        FieldMapping = """{"title":"title"}""",
    };

    [Fact]
    public void ToSyncJobMessage_Maps_All_Connector_Fields()
    {
        var connector = CreateConnector();
        var syncRunId = Guid.NewGuid();
        var checkpoint = "cp-42";
        var correlationId = "corr-1";

        var msg = connector.ToSyncJobMessage(syncRunId, checkpoint, correlationId);

        Assert.Equal(syncRunId, msg.SyncRunId);
        Assert.Equal(connector.Id, msg.ConnectorId);
        Assert.Equal(connector.TenantId, msg.TenantId);
        Assert.Equal(connector.ConnectorType, msg.ConnectorType);
        Assert.False(msg.IsBackfill);
        Assert.Equal(connector.SourceConfig, msg.SourceConfig);
        Assert.Equal(connector.FieldMapping, msg.FieldMapping);
        Assert.Equal(connector.KeyVaultSecretName, msg.KeyVaultSecretName);
        Assert.Equal(connector.AuthType, msg.AuthType);
        Assert.Equal(checkpoint, msg.Checkpoint);
        Assert.Equal(correlationId, msg.CorrelationId);
        Assert.True(msg.EnqueuedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ToSyncJobMessage_IsBackfill_Defaults_False()
    {
        var connector = CreateConnector();

        var msg = connector.ToSyncJobMessage(Guid.NewGuid(), null, "corr");

        Assert.False(msg.IsBackfill);
    }

    [Fact]
    public void ToSyncJobMessage_IsBackfill_Can_Be_Set_True()
    {
        var connector = CreateConnector();

        var msg = connector.ToSyncJobMessage(Guid.NewGuid(), null, "corr", isBackfill: true);

        Assert.True(msg.IsBackfill);
    }

    [Fact]
    public void ToSyncJobMessage_Uses_Provided_EnqueuedAt()
    {
        var connector = CreateConnector();
        var fixedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var msg = connector.ToSyncJobMessage(Guid.NewGuid(), null, "corr", enqueuedAt: fixedTime);

        Assert.Equal(fixedTime, msg.EnqueuedAt);
    }

    [Fact]
    public void ToSyncJobMessage_Null_Checkpoint_Propagated()
    {
        var connector = CreateConnector();

        var msg = connector.ToSyncJobMessage(Guid.NewGuid(), null, "corr");

        Assert.Null(msg.Checkpoint);
    }

    [Fact]
    public void ToSyncJobMessage_Null_Optional_Fields_Propagated()
    {
        var connector = CreateConnector();
        connector.SourceConfig = null;
        connector.FieldMapping = null;
        connector.KeyVaultSecretName = null;

        var msg = connector.ToSyncJobMessage(Guid.NewGuid(), null, "corr");

        Assert.Null(msg.SourceConfig);
        Assert.Null(msg.FieldMapping);
        Assert.Null(msg.KeyVaultSecretName);
    }
}
