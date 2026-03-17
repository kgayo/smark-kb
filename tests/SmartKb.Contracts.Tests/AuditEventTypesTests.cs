using System.Reflection;

namespace SmartKb.Contracts.Tests;

public sealed class AuditEventTypesTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_FollowDotNotation()
    {
        var fields = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.Contains('.', value);
            Assert.Matches(@"^[a-z][a-z0-9_.]+$", value);
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Theory]
    [InlineData(nameof(AuditEventTypes.TenantMissing), "tenant.missing")]
    [InlineData(nameof(AuditEventTypes.ChatFeedback), "chat.feedback")]
    [InlineData(nameof(AuditEventTypes.ChatOutcome), "chat.outcome")]
    [InlineData(nameof(AuditEventTypes.PiiRedaction), "pii.redaction")]
    [InlineData(nameof(AuditEventTypes.EscalationDraftCreated), "escalation.draft.created")]
    [InlineData(nameof(AuditEventTypes.ConnectorCreated), "connector.created")]
    [InlineData(nameof(AuditEventTypes.ConnectorUpdated), "connector.updated")]
    [InlineData(nameof(AuditEventTypes.ConnectorDeleted), "connector.deleted")]
    [InlineData(nameof(AuditEventTypes.ConnectorEnabled), "connector.enabled")]
    [InlineData(nameof(AuditEventTypes.ConnectorDisabled), "connector.disabled")]
    [InlineData(nameof(AuditEventTypes.ConnectorTestPassed), "connector.test_passed")]
    [InlineData(nameof(AuditEventTypes.ConnectorTestFailed), "connector.test_failed")]
    [InlineData(nameof(AuditEventTypes.ConnectorSyncTriggered), "connector.sync_triggered")]
    [InlineData(nameof(AuditEventTypes.ConnectorPreview), "connector.preview")]
    [InlineData(nameof(AuditEventTypes.SyncCompleted), "sync.completed")]
    [InlineData(nameof(AuditEventTypes.SyncFailed), "sync.failed")]
    [InlineData(nameof(AuditEventTypes.WebhookReceived), "webhook.received")]
    [InlineData(nameof(AuditEventTypes.WebhookSignatureFailed), "webhook.signature_failed")]
    [InlineData(nameof(AuditEventTypes.WebhookClientStateMismatch), "webhook.clientstate_mismatch")]
    [InlineData(nameof(AuditEventTypes.WebhookPollFallback), "webhook.poll_fallback")]
    public void Constant_HasExpectedValue(string fieldName, string expectedValue)
    {
        var field = typeof(AuditEventTypes).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedValue, (string)field.GetValue(null)!);
    }

    [Fact]
    public void HasAtLeast20Constants()
    {
        var count = typeof(AuditEventTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Length;
        Assert.True(count >= 20, $"Expected at least 20 audit event type constants, found {count}.");
    }
}
