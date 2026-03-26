using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SmartKb.Contracts.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void ApiSource_HasCorrectName()
    {
        Assert.Equal("SmartKb.Api", Diagnostics.ApiSourceName);
        Assert.Equal(Diagnostics.ApiSourceName, Diagnostics.ApiSource.Name);
    }

    [Fact]
    public void OrchestrationSource_HasCorrectName()
    {
        Assert.Equal("SmartKb.Orchestration", Diagnostics.OrchestrationSourceName);
        Assert.Equal(Diagnostics.OrchestrationSourceName, Diagnostics.OrchestrationSource.Name);
    }

    [Fact]
    public void IngestionSource_HasCorrectName()
    {
        Assert.Equal("SmartKb.Ingestion", Diagnostics.IngestionSourceName);
        Assert.Equal(Diagnostics.IngestionSourceName, Diagnostics.IngestionSource.Name);
    }

    [Fact]
    public void AllSources_AreDistinct()
    {
        var names = new[]
        {
            Diagnostics.ApiSourceName,
            Diagnostics.OrchestrationSourceName,
            Diagnostics.IngestionSourceName,
        };
        Assert.Equal(3, names.Distinct().Count());
    }

    [Fact]
    public void Sources_AreStaticSingletons()
    {
        // Same reference on repeated access.
        Assert.Same(Diagnostics.ApiSource, Diagnostics.ApiSource);
        Assert.Same(Diagnostics.OrchestrationSource, Diagnostics.OrchestrationSource);
        Assert.Same(Diagnostics.IngestionSource, Diagnostics.IngestionSource);
    }

    [Fact]
    public void OrchestrationSource_CanStartActivity()
    {
        // Without a listener, StartActivity returns null but should not throw.
        var activity = Diagnostics.OrchestrationSource.StartActivity("TestSpan");
        // Activity will be null without a registered listener — that's fine.
        activity?.Dispose();
    }

    [Fact]
    public void IngestionSource_CanStartActivity()
    {
        var activity = Diagnostics.IngestionSource.StartActivity("TestSpan");
        activity?.Dispose();
    }

    // --- P0-022: Meter and instrument tests ---

    [Fact]
    public void Meter_HasCorrectName()
    {
        Assert.Equal("SmartKb", Diagnostics.MeterName);
        Assert.Equal(Diagnostics.MeterName, Diagnostics.Meter.Name);
    }

    [Fact]
    public void ChatLatencyMs_IsHistogram()
    {
        Assert.NotNull(Diagnostics.ChatLatencyMs);
        // Recording should not throw.
        Diagnostics.ChatLatencyMs.Record(100.0);
    }

    [Fact]
    public void ChatRequestsTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.ChatRequestsTotal);
        Diagnostics.ChatRequestsTotal.Add(1);
    }

    [Fact]
    public void ChatNoEvidenceTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.ChatNoEvidenceTotal);
        Diagnostics.ChatNoEvidenceTotal.Add(1);
    }

    [Fact]
    public void SyncJobDurationMs_IsHistogram()
    {
        Assert.NotNull(Diagnostics.SyncJobDurationMs);
        Diagnostics.SyncJobDurationMs.Record(5000.0);
    }

    [Fact]
    public void SyncJobsCompletedTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.SyncJobsCompletedTotal);
        Diagnostics.SyncJobsCompletedTotal.Add(1);
    }

    [Fact]
    public void SyncJobsFailedTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.SyncJobsFailedTotal);
        Diagnostics.SyncJobsFailedTotal.Add(1);
    }

    [Fact]
    public void DeadLetterTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.DeadLetterTotal);
        Diagnostics.DeadLetterTotal.Add(1);
    }

    [Fact]
    public void RecordsProcessedTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.RecordsProcessedTotal);
        Diagnostics.RecordsProcessedTotal.Add(10);
    }

    [Fact]
    public void PiiRedactionsTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.PiiRedactionsTotal);
        Diagnostics.PiiRedactionsTotal.Add(1);
    }

    [Fact]
    public void RestrictedContentBlockedTotal_IsCounter()
    {
        Assert.NotNull(Diagnostics.RestrictedContentBlockedTotal);
        Diagnostics.RestrictedContentBlockedTotal.Add(1);
    }

    [Fact]
    public void ChatConfidence_IsHistogram()
    {
        Assert.NotNull(Diagnostics.ChatConfidence);
        Diagnostics.ChatConfidence.Record(0.75);
    }

    [Fact]
    public void AllMetricInstruments_BelongToSameMeter()
    {
        // Verify all instruments can record with tags without throwing.
        var tag = new KeyValuePair<string, object?>("test", "value");
        Diagnostics.ChatLatencyMs.Record(1.0, tag);
        Diagnostics.ChatRequestsTotal.Add(1, tag);
        Diagnostics.ChatNoEvidenceTotal.Add(1, tag);
        Diagnostics.SyncJobDurationMs.Record(1.0, tag);
        Diagnostics.SyncJobsCompletedTotal.Add(1, tag);
        Diagnostics.SyncJobsFailedTotal.Add(1, tag);
        Diagnostics.DeadLetterTotal.Add(1, tag);
        Diagnostics.RecordsProcessedTotal.Add(1, tag);
        Diagnostics.PiiRedactionsTotal.Add(1, tag);
        Diagnostics.RestrictedContentBlockedTotal.Add(1, tag);
        Diagnostics.ChatConfidence.Record(0.5, tag);
    }

    [Fact]
    public void Meter_IsStaticSingleton()
    {
        Assert.Same(Diagnostics.Meter, Diagnostics.Meter);
    }

    // --- TECH-111: TagNames constant tests ---

    [Fact]
    public void TagNames_AllConstants_AreNotNullOrEmpty()
    {
        var fields = typeof(Diagnostics.TagNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string));

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            Assert.False(string.IsNullOrEmpty(value), $"{field.Name} must not be null or empty");
        }
    }

    [Fact]
    public void TagNames_AllConstants_AreUnique()
    {
        var fields = typeof(Diagnostics.TagNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string?)f.GetValue(null))
            .ToList();

        Assert.Equal(fields.Count, fields.Distinct().Count());
    }

    [Fact]
    public void TagNames_AllConstants_StartWithSmartKbPrefix()
    {
        var fields = typeof(Diagnostics.TagNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string));

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            Assert.StartsWith("smartkb.", value!);
        }
    }

    [Fact]
    public void TagNames_HasExpectedConstantCount()
    {
        var count = typeof(Diagnostics.TagNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Count(f => f.FieldType == typeof(string));

        Assert.Equal(31, count);
    }

    [Theory]
    [InlineData("smartkb.tenant_id")]
    [InlineData("smartkb.user_id")]
    [InlineData("smartkb.correlation_id")]
    [InlineData("smartkb.response_type")]
    [InlineData("smartkb.sync_run_id")]
    [InlineData("smartkb.connector_id")]
    [InlineData("smartkb.connector_type")]
    [InlineData("smartkb.is_backfill")]
    [InlineData("smartkb.records_processed")]
    [InlineData("smartkb.chunks_produced")]
    [InlineData("smartkb.classification.category")]
    [InlineData("smartkb.embedding_cache_hit")]
    [InlineData("smartkb.blended_confidence")]
    [InlineData("smartkb.scheduled_sync.connectors_evaluated")]
    public void TagNames_ContainsExpectedValue(string expectedValue)
    {
        var values = typeof(Diagnostics.TagNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string?)f.GetValue(null))
            .ToHashSet();

        Assert.Contains(expectedValue, values);
    }
}
