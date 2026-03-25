using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Exceptions;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class SearchTokenServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly SearchTokenService _service;

    private const string TenantId = "t1";
    private const string ActorId = "admin-1";
    private const string CorrelationId = "corr-1";

    public SearchTokenServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        SeedTenant();
        _service = new SearchTokenService(_db, _auditWriter, NullLogger<SearchTokenService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedTenant()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = TenantId,
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    // ==================== Stop Words ====================

    [Fact]
    public async Task CreateStopWord_ValidWord_ReturnsResponse()
    {
        var (response, validation) = await _service.CreateStopWordAsync(
            TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "Hello" });

        Assert.Null(validation);
        Assert.NotNull(response);
        Assert.Equal("hello", response!.Word); // normalized to lower
        Assert.Equal("general", response.GroupName);
        Assert.True(response.IsActive);
    }

    [Fact]
    public async Task CreateStopWord_EmptyWord_ReturnsValidation()
    {
        var (response, validation) = await _service.CreateStopWordAsync(
            TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "" });

        Assert.NotNull(validation);
        Assert.False(validation!.IsValid);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateStopWord_WordWithSpaces_ReturnsValidation()
    {
        var (response, validation) = await _service.CreateStopWordAsync(
            TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello world" });

        Assert.NotNull(validation);
        Assert.False(validation!.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("single word"));
    }

    [Fact]
    public async Task CreateStopWord_Duplicate_ReturnsValidation()
    {
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });
        var (response, validation) = await _service.CreateStopWordAsync(
            TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "HELLO" });

        Assert.NotNull(validation);
        Assert.Contains(validation!.Errors, e => e.Contains("already exists"));
    }

    [Fact]
    public async Task ListStopWords_ReturnsTenantScoped()
    {
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "thanks", GroupName = "greeting" });

        var result = await _service.ListStopWordsAsync(TenantId);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains("general", result.Groups);
        Assert.Contains("greeting", result.Groups);
    }

    [Fact]
    public async Task ListStopWords_FilterByGroup()
    {
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello", GroupName = "greeting" });
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "please", GroupName = "filler" });

        var result = await _service.ListStopWordsAsync(TenantId, "greeting");

        Assert.Single(result.Words);
        Assert.Equal("hello", result.Words[0].Word);
    }

    [Fact]
    public async Task UpdateStopWord_ToggleActive()
    {
        var (created, _) = await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });
        var (updated, _, notFound) = await _service.UpdateStopWordAsync(
            TenantId, ActorId, CorrelationId, created!.Id, new UpdateStopWordRequest { IsActive = false });

        Assert.False(notFound);
        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task UpdateStopWord_NotFound_ReturnsNotFound()
    {
        var (_, _, notFound) = await _service.UpdateStopWordAsync(
            TenantId, ActorId, CorrelationId, Guid.NewGuid(), new UpdateStopWordRequest { IsActive = false });

        Assert.True(notFound);
    }

    [Fact]
    public async Task DeleteStopWord_ExistingWord_ReturnsTrue()
    {
        var (created, _) = await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });
        var deleted = await _service.DeleteStopWordAsync(TenantId, ActorId, CorrelationId, created!.Id);

        Assert.True(deleted);
        var list = await _service.ListStopWordsAsync(TenantId);
        Assert.Empty(list.Words);
    }

    [Fact]
    public async Task DeleteStopWord_NotFound_ReturnsFalse()
    {
        var deleted = await _service.DeleteStopWordAsync(TenantId, ActorId, CorrelationId, Guid.NewGuid());
        Assert.False(deleted);
    }

    [Fact]
    public async Task SeedDefaultStopWords_CreatesDefaults()
    {
        var count = await _service.SeedDefaultStopWordsAsync(TenantId, ActorId, CorrelationId);

        Assert.True(count > 0);
        var list = await _service.ListStopWordsAsync(TenantId);
        Assert.Equal(count, list.TotalCount);
    }

    [Fact]
    public async Task SeedDefaultStopWords_SkipsExisting()
    {
        await _service.SeedDefaultStopWordsAsync(TenantId, ActorId, CorrelationId);
        var count2 = await _service.SeedDefaultStopWordsAsync(TenantId, ActorId, CorrelationId, overwriteExisting: false);
        Assert.Equal(0, count2);
    }

    // ==================== Special Tokens ====================

    [Fact]
    public async Task CreateSpecialToken_ValidToken_ReturnsResponse()
    {
        var (response, validation) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId,
            new CreateSpecialTokenRequest { Token = "0x80070005", Category = "hex-error", BoostFactor = 3, Description = "Access denied" });

        Assert.Null(validation);
        Assert.NotNull(response);
        Assert.Equal("0x80070005", response!.Token);
        Assert.Equal("hex-error", response.Category);
        Assert.Equal(3, response.BoostFactor);
        Assert.Equal("Access denied", response.Description);
    }

    [Fact]
    public async Task CreateSpecialToken_EmptyToken_ReturnsValidation()
    {
        var (response, validation) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "" });

        Assert.NotNull(validation);
        Assert.False(validation!.IsValid);
    }

    [Fact]
    public async Task CreateSpecialToken_InvalidBoost_ReturnsValidation()
    {
        var (response, validation) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD", BoostFactor = 15 });

        Assert.NotNull(validation);
        Assert.Contains(validation!.Errors, e => e.Contains("Boost factor"));
    }

    [Fact]
    public async Task CreateSpecialToken_Duplicate_ReturnsValidation()
    {
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });
        var (_, validation) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });

        Assert.NotNull(validation);
        Assert.Contains(validation!.Errors, e => e.Contains("already exists"));
    }

    [Fact]
    public async Task ListSpecialTokens_ReturnsTenantScoped()
    {
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "HTTP 500", Category = "http-status" });
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD", Category = "error-code" });

        var result = await _service.ListSpecialTokensAsync(TenantId);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains("http-status", result.Categories);
        Assert.Contains("error-code", result.Categories);
    }

    [Fact]
    public async Task ListSpecialTokens_FilterByCategory()
    {
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "HTTP 500", Category = "http-status" });
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD", Category = "error-code" });

        var result = await _service.ListSpecialTokensAsync(TenantId, "http-status");

        Assert.Single(result.Tokens);
        Assert.Equal("HTTP 500", result.Tokens[0].Token);
    }

    [Fact]
    public async Task UpdateSpecialToken_ChangeBoost()
    {
        var (created, _) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });
        var (updated, _, notFound) = await _service.UpdateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, created!.Id, new UpdateSpecialTokenRequest { BoostFactor = 5 });

        Assert.False(notFound);
        Assert.Equal(5, updated!.BoostFactor);
    }

    [Fact]
    public async Task UpdateSpecialToken_InvalidBoost_ReturnsValidation()
    {
        var (created, _) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });
        var (_, validation, _) = await _service.UpdateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, created!.Id, new UpdateSpecialTokenRequest { BoostFactor = 20 });

        Assert.NotNull(validation);
        Assert.Contains(validation!.Errors, e => e.Contains("Boost factor"));
    }

    [Fact]
    public async Task DeleteSpecialToken_ExistingToken_ReturnsTrue()
    {
        var (created, _) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });
        var deleted = await _service.DeleteSpecialTokenAsync(TenantId, ActorId, CorrelationId, created!.Id);

        Assert.True(deleted);
    }

    [Fact]
    public async Task SeedDefaultSpecialTokens_CreatesDefaults()
    {
        var count = await _service.SeedDefaultSpecialTokensAsync(TenantId, ActorId, CorrelationId);

        Assert.True(count > 0);
        var list = await _service.ListSpecialTokensAsync(TenantId);
        Assert.Equal(count, list.TotalCount);
    }

    // ==================== Query Preprocessing ====================

    [Fact]
    public async Task PreprocessQuery_RemovesStopWords()
    {
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "please" });

        var result = await _service.PreprocessQueryAsync(TenantId, "hello please fix my issue");

        Assert.DoesNotContain("hello", result.ProcessedQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("please", result.ProcessedQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fix", result.ProcessedQuery);
        Assert.Contains("hello", result.RemovedStopWords);
        Assert.Contains("please", result.RemovedStopWords);
    }

    [Fact]
    public async Task PreprocessQuery_PreservesSpecialTokens()
    {
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "help" });
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId,
            new CreateSpecialTokenRequest { Token = "HTTP 500", BoostFactor = 3 });

        var result = await _service.PreprocessQueryAsync(TenantId, "help I got HTTP 500 error");

        Assert.Contains("HTTP 500", result.BoostedQuery);
        Assert.Contains("HTTP 500", result.DetectedSpecialTokens);
    }

    [Fact]
    public async Task PreprocessQuery_BoostsSpecialTokens()
    {
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId,
            new CreateSpecialTokenRequest { Token = "BSOD", BoostFactor = 3 });

        var result = await _service.PreprocessQueryAsync(TenantId, "I got a BSOD error");

        // BSOD should appear extra times in boosted query (boost=3, so 2 extra)
        var count = result.BoostedQuery.Split("BSOD").Length - 1;
        Assert.True(count >= 3, $"Expected BSOD at least 3 times, got {count}");
    }

    [Fact]
    public async Task PreprocessQuery_EmptyQuery_ReturnsEmpty()
    {
        var result = await _service.PreprocessQueryAsync(TenantId, "");

        Assert.Equal("", result.ProcessedQuery);
        Assert.Empty(result.RemovedStopWords);
        Assert.Empty(result.DetectedSpecialTokens);
    }

    [Fact]
    public async Task PreprocessQuery_InactiveStopWords_AreNotApplied()
    {
        var (created, _) = await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });
        await _service.UpdateStopWordAsync(TenantId, ActorId, CorrelationId, created!.Id, new UpdateStopWordRequest { IsActive = false });

        var result = await _service.PreprocessQueryAsync(TenantId, "hello world");

        Assert.Contains("hello", result.ProcessedQuery);
        Assert.Empty(result.RemovedStopWords);
    }

    [Fact]
    public async Task StopWord_TenantIsolation()
    {
        // Create stop word in TenantId
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });

        // Other tenant should not see it
        var list = await _service.ListStopWordsAsync("other-tenant");
        Assert.Empty(list.Words);
    }

    [Fact]
    public async Task SpecialToken_TenantIsolation()
    {
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });

        var list = await _service.ListSpecialTokensAsync("other-tenant");
        Assert.Empty(list.Tokens);
    }

    [Fact]
    public async Task CreateStopWord_WritesAuditEvent()
    {
        await _service.CreateStopWordAsync(TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });

        Assert.Contains(_auditWriter.Events, e => e.EventType == "search.stop_word.created");
    }

    [Fact]
    public async Task CreateSpecialToken_WritesAuditEvent()
    {
        await _service.CreateSpecialTokenAsync(TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });

        Assert.Contains(_auditWriter.Events, e => e.EventType == "search.special_token.created");
    }

    [Fact]
    public async Task UpdateStopWord_ConcurrentModification_ThrowsConcurrencyConflictException()
    {
        var (created, _) = await _service.CreateStopWordAsync(
            TenantId, ActorId, CorrelationId, new CreateStopWordRequest { Word = "hello" });

        var entity = await _db.StopWords.FirstAsync(s => s.Id == created!.Id);
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE StopWords SET UpdatedAt = {0} WHERE Id = {1}",
            entity.UpdatedAt.AddMinutes(5), entity.Id);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _service.UpdateStopWordAsync(TenantId, ActorId, CorrelationId, created!.Id,
                new UpdateStopWordRequest { GroupName = "updated" }));
    }

    [Fact]
    public async Task UpdateSpecialToken_ConcurrentModification_ThrowsConcurrencyConflictException()
    {
        var (created, _) = await _service.CreateSpecialTokenAsync(
            TenantId, ActorId, CorrelationId, new CreateSpecialTokenRequest { Token = "BSOD" });

        var entity = await _db.SpecialTokens.FirstAsync(s => s.Id == created!.Id);
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE SpecialTokens SET UpdatedAt = {0} WHERE Id = {1}",
            entity.UpdatedAt.AddMinutes(5), entity.Id);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _service.UpdateSpecialTokenAsync(TenantId, ActorId, CorrelationId, created!.Id,
                new UpdateSpecialTokenRequest { Category = "updated" }));
    }

    // ==================== Test Helpers ====================

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
