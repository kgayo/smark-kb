using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class EvalReportServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly EvalReportService _service;

    public EvalReportServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();

        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t2",
            DisplayName = "Other Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new EvalReportService(_db, _auditWriter, NullLogger<EvalReportService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static PersistEvalReportRequest MakeRequest(string runId = "eval-run-20260319-100000", string runType = "full") => new()
    {
        RunId = runId,
        RunType = runType,
        TotalCases = 50,
        SuccessfulCases = 48,
        FailedCases = 2,
        MetricsJson = """{"groundedness":0.85,"citationCoverage":0.72,"routingAccuracy":0.65,"noEvidenceRate":0.1,"responseTypeAccuracy":0.9,"mustIncludeHitRate":0.88,"safetyPassRate":1.0,"averageConfidence":0.75,"averageDurationMs":3200}""",
        ViolationsJson = null,
        BaselineComparisonJson = null,
        HasBlockingRegression = false,
        ViolationCount = 0,
    };

    [Fact]
    public async Task PersistReport_StoresAndReturnsDetail()
    {
        var request = MakeRequest();
        var result = await _service.PersistReportAsync("t1", request, "admin-1");

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("eval-run-20260319-100000", result.RunId);
        Assert.Equal("full", result.RunType);
        Assert.Equal(50, result.TotalCases);
        Assert.Equal(48, result.SuccessfulCases);
        Assert.Equal(2, result.FailedCases);
        Assert.False(result.HasBlockingRegression);
        Assert.Equal(0, result.ViolationCount);
        Assert.Equal("admin-1", result.TriggeredBy);
        Assert.Equal(0.85f, result.Metrics.Groundedness, 0.01f);
        Assert.Equal(0.72f, result.Metrics.CitationCoverage, 0.01f);
        Assert.Empty(result.Violations);
        Assert.Null(result.BaselineComparison);
    }

    [Fact]
    public async Task PersistReport_WritesAuditEvent()
    {
        await _service.PersistReportAsync("t1", MakeRequest(), "admin-1");

        Assert.Single(_auditWriter.Events);
        Assert.Equal(AuditEventTypes.EvalReportPersisted, _auditWriter.Events[0].EventType);
        Assert.Contains("eval-run-20260319-100000", _auditWriter.Events[0].Detail);
    }

    [Fact]
    public async Task PersistReport_WithViolationsAndBaseline()
    {
        var request = MakeRequest() with
        {
            ViolationsJson = """[{"metricName":"Groundedness","actualValue":0.75,"thresholdValue":0.8,"direction":">="}]""",
            BaselineComparisonJson = """{"hasRegression":true,"shouldBlock":false,"details":[{"metricName":"Groundedness","baselineValue":0.85,"currentValue":0.75,"delta":-0.1,"severity":"warning"}]}""",
            HasBlockingRegression = false,
            ViolationCount = 1,
        };

        var result = await _service.PersistReportAsync("t1", request, "admin-1");

        Assert.Equal(1, result.ViolationCount);
        Assert.Single(result.Violations);
        Assert.Equal("Groundedness", result.Violations[0].MetricName);
        Assert.Equal(0.75f, result.Violations[0].ActualValue, 0.01f);
        Assert.NotNull(result.BaselineComparison);
        Assert.True(result.BaselineComparison!.HasRegression);
        Assert.False(result.BaselineComparison.ShouldBlock);
        Assert.Single(result.BaselineComparison.Details);
        Assert.Equal(EvalSeverity.Warning, result.BaselineComparison.Details[0].Severity);
    }

    [Fact]
    public async Task GetReport_ReturnsById()
    {
        var persisted = await _service.PersistReportAsync("t1", MakeRequest(), "admin-1");

        var result = await _service.GetReportAsync("t1", persisted.Id);

        Assert.NotNull(result);
        Assert.Equal(persisted.Id, result!.Id);
        Assert.Equal("full", result.RunType);
    }

    [Fact]
    public async Task GetReport_ReturnsNullForWrongTenant()
    {
        var persisted = await _service.PersistReportAsync("t1", MakeRequest(), "admin-1");

        var result = await _service.GetReportAsync("t2", persisted.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetReport_ReturnsNullForNonExistent()
    {
        var result = await _service.GetReportAsync("t1", Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListReports_ReturnsPaginated()
    {
        for (int i = 0; i < 5; i++)
            await _service.PersistReportAsync("t1", MakeRequest($"run-{i}"), "admin-1");

        var result = await _service.ListReportsAsync("t1", page: 1, pageSize: 3);

        Assert.Equal(3, result.Reports.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.True(result.HasMore);
        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.PageSize);
    }

    [Fact]
    public async Task ListReports_FiltersByRunType()
    {
        await _service.PersistReportAsync("t1", MakeRequest("run-1", "smoke"), "admin-1");
        await _service.PersistReportAsync("t1", MakeRequest("run-2", "full"), "admin-1");
        await _service.PersistReportAsync("t1", MakeRequest("run-3", "smoke"), "admin-1");

        var result = await _service.ListReportsAsync("t1", runType: "smoke");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Reports, r => Assert.Equal("smoke", r.RunType));
    }

    [Fact]
    public async Task ListReports_TenantIsolation()
    {
        await _service.PersistReportAsync("t1", MakeRequest("run-t1"), "admin-1");
        await _service.PersistReportAsync("t2", MakeRequest("run-t2"), "admin-2");

        var t1Reports = await _service.ListReportsAsync("t1");
        var t2Reports = await _service.ListReportsAsync("t2");

        Assert.Single(t1Reports.Reports);
        Assert.Equal("run-t1", t1Reports.Reports[0].RunId);
        Assert.Single(t2Reports.Reports);
        Assert.Equal("run-t2", t2Reports.Reports[0].RunId);
    }

    [Fact]
    public async Task ListReports_OrdersByCreatedAtDescending()
    {
        await _service.PersistReportAsync("t1", MakeRequest("run-1"), "admin-1");
        await Task.Delay(10); // ensure different timestamps
        await _service.PersistReportAsync("t1", MakeRequest("run-2"), "admin-1");

        var result = await _service.ListReportsAsync("t1");

        Assert.Equal("run-2", result.Reports[0].RunId);
        Assert.Equal("run-1", result.Reports[1].RunId);
    }

    [Fact]
    public async Task ListReports_PageSizeClamped()
    {
        var result = await _service.ListReportsAsync("t1", pageSize: 200);

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task ListReports_EmptyList()
    {
        var result = await _service.ListReportsAsync("t1");

        Assert.Empty(result.Reports);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.HasMore);
    }

    [Fact]
    public void DeserializeMetrics_EmptyString_ReturnsDefaults()
    {
        var metrics = EvalReportService.DeserializeMetrics("");

        Assert.Equal(0f, metrics.Groundedness);
        Assert.Equal(0f, metrics.CitationCoverage);
    }

    [Fact]
    public void DeserializeMetrics_ValidJson_ParsesCorrectly()
    {
        var json = """{"groundedness":0.9,"citationCoverage":0.8,"routingAccuracy":0.7,"noEvidenceRate":0.1,"responseTypeAccuracy":0.95,"mustIncludeHitRate":0.88,"safetyPassRate":1.0,"averageConfidence":0.75,"averageDurationMs":2500}""";
        var metrics = EvalReportService.DeserializeMetrics(json);

        Assert.Equal(0.9f, metrics.Groundedness, 0.01f);
        Assert.Equal(0.8f, metrics.CitationCoverage, 0.01f);
        Assert.Equal(2500, metrics.AverageDurationMs);
    }

    [Fact]
    public void DeserializeViolations_NullJson_ReturnsEmpty()
    {
        var result = EvalReportService.DeserializeViolations(null);
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializeBaseline_NullJson_ReturnsNull()
    {
        var result = EvalReportService.DeserializeBaseline(null);
        Assert.Null(result);
    }

    private sealed class StubAuditWriter : IAuditEventWriter
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
