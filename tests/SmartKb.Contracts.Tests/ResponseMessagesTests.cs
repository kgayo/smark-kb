using System.Reflection;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Tests;

public sealed class ResponseMessagesTests
{
    [Fact]
    public void AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(ResponseMessages).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(fields);

        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{field.Name} must not be null or whitespace.");
        }
    }

    [Fact]
    public void AllConstants_AreUnique()
    {
        var fields = typeof(ResponseMessages).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var values = fields.Select(f => (string)f.GetValue(null)!).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Theory]
    [InlineData(nameof(ResponseMessages.ConnectorNotFound), "Connector not found.")]
    [InlineData(nameof(ResponseMessages.SessionNotFound), "Session not found.")]
    [InlineData(nameof(ResponseMessages.SearchServiceNotConfigured), "Search service is not configured.")]
    [InlineData(nameof(ResponseMessages.ConnectorDisabledEventIgnored), "Connector is disabled; event ignored.")]
    [InlineData(nameof(ResponseMessages.NoActiveWebhookSubscriptions), "No active webhook subscriptions.")]
    [InlineData(nameof(ResponseMessages.InvalidWebhookPayload), "Invalid webhook payload.")]
    [InlineData(nameof(ResponseMessages.InvalidWebhookSignature), "Invalid webhook signature.")]
    [InlineData(nameof(ResponseMessages.FailedToVerifyWebhookSignature), "Failed to verify webhook signature.")]
    [InlineData(nameof(ResponseMessages.SystemActorId), "system")]
    [InlineData(nameof(ResponseMessages.InvalidOrMissingSourceConfiguration), "Invalid or missing source configuration.")]
    [InlineData(nameof(ResponseMessages.NoCredentialsProvided), "No credentials provided.")]
    [InlineData(nameof(ResponseMessages.EscalationDraftNotFound), "Escalation draft not found.")]
    [InlineData(nameof(ResponseMessages.EvidenceChunkNotFound), "Evidence chunk not found.")]
    [InlineData(nameof(ResponseMessages.SynonymRuleNotFound), "Synonym rule not found.")]
    [InlineData(nameof(ResponseMessages.PlaybookNotFound), "Playbook not found.")]
    [InlineData(nameof(ResponseMessages.RoutingRuleNotFound), "Routing rule not found.")]
    [InlineData(nameof(ResponseMessages.GoldCaseNotFound), "Gold case not found.")]
    [InlineData(nameof(ResponseMessages.EvalReportNotFound), "Eval report not found.")]
    [InlineData(nameof(ResponseMessages.DeletionRequestNotFound), "Deletion request not found.")]
    [InlineData(nameof(ResponseMessages.SyncRunNotFound), "Sync run not found.")]
    [InlineData(nameof(ResponseMessages.FeedbackNotFound), "Feedback not found.")]
    public void ExpectedConstants_HaveCorrectValues(string fieldName, string expectedValue)
    {
        var field = typeof(ResponseMessages).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);

        var value = (string)field.GetValue(null)!;
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void AllMessageConstants_EndWithPeriod_ExceptSystemActorId()
    {
        var fields = typeof(ResponseMessages).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var field in fields)
        {
            if (field.Name == nameof(ResponseMessages.SystemActorId))
                continue;

            var value = (string)field.GetValue(null)!;
            Assert.True(value.EndsWith('.'), $"{field.Name} value '{value}' should end with a period.");
        }
    }
}
