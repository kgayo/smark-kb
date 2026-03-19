using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class PatternUsageMetricsServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly FakeTimeProvider _time;
    private readonly PatternUsageMetricsService _service;

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private const string TenantId = "t1";
    private const string PatternId = "pattern-abc-123";

    public PatternUsageMetricsServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero));

        _db.Tenants.Add(new TenantEntity
        {
            TenantId = TenantId,
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new PatternUsageMetricsService(
            _db, NullLogger<PatternUsageMetricsService>.Instance, _time);
    }

    public void Dispose() => _db.Dispose();

    private void AddTrace(string tenantId, string patternId, string userId, float confidence, DateTimeOffset createdAt)
    {
        _db.Set<AnswerTraceEntity>().Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            CorrelationId = Guid.NewGuid().ToString(),
            Query = "test query",
            ResponseType = "answer",
            Confidence = confidence,
            ConfidenceLabel = "High",
            CitedChunkIds = $"""["{patternId}", "evidence-chunk-1"]""",
            RetrievedChunkIds = "[]",
            HasEvidence = true,
            SystemPromptVersion = "v1",
            CreatedAt = createdAt,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetUsageAsync_NoCitations_ReturnsZeroCounts()
    {
        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(PatternId, result.PatternId);
        Assert.Equal(0, result.TotalCitations);
        Assert.Equal(0, result.CitationsLast7Days);
        Assert.Equal(0, result.CitationsLast30Days);
        Assert.Equal(0, result.CitationsLast90Days);
        Assert.Equal(0, result.UniqueUsers);
        Assert.Equal(0, result.AverageConfidence);
        Assert.Null(result.LastCitedAt);
        Assert.Null(result.FirstCitedAt);
        Assert.Equal(30, result.DailyBreakdown.Count);
        Assert.All(result.DailyBreakdown, b => Assert.Equal(0, b.Citations));
    }

    [Fact]
    public async Task GetUsageAsync_SingleCitation_ReturnsCounts()
    {
        var now = _time.GetUtcNow();
        AddTrace(TenantId, PatternId, "user-1", 0.85f, now.AddHours(-1));

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(1, result.TotalCitations);
        Assert.Equal(1, result.CitationsLast7Days);
        Assert.Equal(1, result.CitationsLast30Days);
        Assert.Equal(1, result.CitationsLast90Days);
        Assert.Equal(1, result.UniqueUsers);
        Assert.Equal(0.85f, result.AverageConfidence, 2);
        Assert.NotNull(result.LastCitedAt);
        Assert.NotNull(result.FirstCitedAt);
    }

    [Fact]
    public async Task GetUsageAsync_MultipleCitations_AggregatesCorrectly()
    {
        var now = _time.GetUtcNow();
        AddTrace(TenantId, PatternId, "user-1", 0.8f, now.AddDays(-2));
        AddTrace(TenantId, PatternId, "user-2", 0.6f, now.AddDays(-10));
        AddTrace(TenantId, PatternId, "user-1", 0.9f, now.AddDays(-40));

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(3, result.TotalCitations);
        Assert.Equal(1, result.CitationsLast7Days);
        Assert.Equal(2, result.CitationsLast30Days);
        Assert.Equal(3, result.CitationsLast90Days);
        Assert.Equal(2, result.UniqueUsers);
        Assert.Equal((0.8f + 0.6f + 0.9f) / 3, result.AverageConfidence, 2);
    }

    [Fact]
    public async Task GetUsageAsync_TimeWindowBoundaries()
    {
        var now = _time.GetUtcNow();
        // Exactly 7 days ago (in range)
        AddTrace(TenantId, PatternId, "user-1", 0.7f, now.AddDays(-7).AddMinutes(1));
        // 8 days ago (out of 7d window, in 30d)
        AddTrace(TenantId, PatternId, "user-2", 0.5f, now.AddDays(-8));
        // 31 days ago (out of 30d, in 90d)
        AddTrace(TenantId, PatternId, "user-3", 0.6f, now.AddDays(-31));
        // 91 days ago (out of all windows)
        AddTrace(TenantId, PatternId, "user-4", 0.4f, now.AddDays(-91));

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(3, result.TotalCitations); // only 90d window
        Assert.Equal(1, result.CitationsLast7Days);
        Assert.Equal(2, result.CitationsLast30Days);
        Assert.Equal(3, result.CitationsLast90Days);
    }

    [Fact]
    public async Task GetUsageAsync_TenantIsolation()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t2",
            DisplayName = "Other",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        var now = _time.GetUtcNow();
        AddTrace(TenantId, PatternId, "user-1", 0.8f, now.AddHours(-1));
        AddTrace("t2", PatternId, "user-2", 0.7f, now.AddHours(-1));

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(1, result.TotalCitations);
        Assert.Equal(1, result.UniqueUsers);
    }

    [Fact]
    public async Task GetUsageAsync_OtherPatternNotCounted()
    {
        var now = _time.GetUtcNow();
        AddTrace(TenantId, PatternId, "user-1", 0.8f, now.AddHours(-1));
        AddTrace(TenantId, "pattern-other-456", "user-2", 0.7f, now.AddHours(-1));

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(1, result.TotalCitations);
    }

    [Fact]
    public async Task GetUsageAsync_DailyBreakdown_PopulatesCorrectDays()
    {
        var now = _time.GetUtcNow();
        AddTrace(TenantId, PatternId, "user-1", 0.8f, now.AddDays(-1));
        AddTrace(TenantId, PatternId, "user-2", 0.7f, now.AddDays(-1).AddHours(-2));
        AddTrace(TenantId, PatternId, "user-3", 0.6f, now.AddDays(-5));

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(30, result.DailyBreakdown.Count);

        var yesterday = DateOnly.FromDateTime(now.AddDays(-1).UtcDateTime);
        var dayBucket = result.DailyBreakdown.FirstOrDefault(b => b.Date == yesterday);
        Assert.NotNull(dayBucket);
        Assert.Equal(2, dayBucket.Citations);

        var fiveDaysAgo = DateOnly.FromDateTime(now.AddDays(-5).UtcDateTime);
        var fiveBucket = result.DailyBreakdown.FirstOrDefault(b => b.Date == fiveDaysAgo);
        Assert.NotNull(fiveBucket);
        Assert.Equal(1, fiveBucket.Citations);

        // Other days should be zero
        var zeroDays = result.DailyBreakdown.Where(b => b.Date != yesterday && b.Date != fiveDaysAgo);
        Assert.All(zeroDays, b => Assert.Equal(0, b.Citations));
    }

    [Fact]
    public async Task GetUsageAsync_FirstAndLastCitedAt()
    {
        var now = _time.GetUtcNow();
        var older = now.AddDays(-20);
        var newer = now.AddDays(-1);
        AddTrace(TenantId, PatternId, "user-1", 0.8f, older);
        AddTrace(TenantId, PatternId, "user-2", 0.7f, newer);

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.NotNull(result.FirstCitedAt);
        Assert.NotNull(result.LastCitedAt);
        Assert.Equal(older, result.FirstCitedAt);
        Assert.Equal(newer, result.LastCitedAt);
    }

    [Fact]
    public async Task GetUsageAsync_NonPatternChunksIgnored()
    {
        var now = _time.GetUtcNow();
        // Add trace that only has evidence chunks, no pattern chunks
        _db.Set<AnswerTraceEntity>().Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            UserId = "user-1",
            CorrelationId = Guid.NewGuid().ToString(),
            Query = "test",
            ResponseType = "answer",
            Confidence = 0.9f,
            ConfidenceLabel = "High",
            CitedChunkIds = """["evidence-chunk-1", "evidence-chunk-2"]""",
            RetrievedChunkIds = "[]",
            HasEvidence = true,
            SystemPromptVersion = "v1",
            CreatedAt = now.AddHours(-1),
        });
        _db.SaveChanges();

        var result = await _service.GetUsageAsync(TenantId, PatternId);

        Assert.Equal(0, result.TotalCitations);
    }

    [Theory]
    [InlineData("pattern-ABC-123", true)]   // case-insensitive match
    [InlineData("Pattern-abc-123", true)]
    [InlineData("PATTERN-ABC-123", true)]
    [InlineData("evidence-chunk-1", false)]
    [InlineData("", false)]
    public void ExtractPatternIds_HandlesVariousInputs(string chunkId, bool isPattern)
    {
        var json = $"""["{chunkId}"]""";
        var result = PatternUsageMetricsService.ExtractPatternIds(json);
        Assert.Equal(isPattern, result.Count > 0);
    }

    [Fact]
    public void ExtractPatternIds_EmptyJson_ReturnsEmpty()
    {
        Assert.Empty(PatternUsageMetricsService.ExtractPatternIds(""));
        Assert.Empty(PatternUsageMetricsService.ExtractPatternIds("[]"));
    }

    [Fact]
    public void ExtractPatternIds_MalformedJson_ReturnsEmpty()
    {
        Assert.Empty(PatternUsageMetricsService.ExtractPatternIds("not json"));
    }
}
