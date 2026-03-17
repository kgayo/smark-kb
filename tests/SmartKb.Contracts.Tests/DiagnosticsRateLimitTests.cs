using SmartKb.Contracts;

namespace SmartKb.Contracts.Tests;

public sealed class DiagnosticsRateLimitTests
{
    [Fact]
    public void SourceRateLimitTotal_CounterExists()
    {
        Assert.NotNull(Diagnostics.SourceRateLimitTotal);
    }

    [Fact]
    public void SourceRateLimitTotal_CanRecord()
    {
        // Should not throw.
        Diagnostics.SourceRateLimitTotal.Add(1,
            new KeyValuePair<string, object?>("smartkb.connector_type", "AzureDevOps"),
            new KeyValuePair<string, object?>("smartkb.tenant_id", "test-tenant"));
    }

    [Fact]
    public void MeterName_IsSmartKb()
    {
        Assert.Equal("SmartKb", Diagnostics.MeterName);
        Assert.Equal("SmartKb", Diagnostics.Meter.Name);
    }
}
