using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// Manages per-tenant stop words and special tokens for search query preprocessing.
/// P3-028: Stop-words and special tokens management.
/// </summary>
public sealed partial class SearchTokenService : ISearchTokenService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _audit;
    private readonly ILogger<SearchTokenService> _logger;

    public SearchTokenService(SmartKbDbContext db, IAuditEventWriter audit, ILogger<SearchTokenService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    // ========== Stop Words ==========

    public async Task<StopWordListResponse> ListStopWordsAsync(string tenantId, string? groupName = null, CancellationToken ct = default)
    {
        var query = _db.StopWords.Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(groupName))
            query = query.Where(s => s.GroupName == groupName);

        var words = await query.OrderBy(s => s.GroupName).ThenBy(s => s.Word).ToListAsync(ct);
        var groups = await _db.StopWords.Where(s => s.TenantId == tenantId).Select(s => s.GroupName).Distinct().OrderBy(g => g).ToListAsync(ct);

        return new StopWordListResponse
        {
            Words = words.Select(MapStopWord).ToList(),
            TotalCount = words.Count,
            Groups = groups,
        };
    }

    public async Task<StopWordResponse?> GetStopWordAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.StopWords.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        return entity is null ? null : MapStopWord(entity);
    }

    public async Task<(StopWordResponse? Response, SearchTokenValidationResult? Validation)> CreateStopWordAsync(
        string tenantId, string actorId, string correlationId, CreateStopWordRequest request, CancellationToken ct = default)
    {
        var validation = ValidateStopWord(request.Word);
        if (!validation.IsValid)
            return (null, validation);

        var normalizedWord = request.Word.Trim().ToLowerInvariant();

        var exists = await _db.StopWords.AnyAsync(s => s.TenantId == tenantId && s.Word == normalizedWord, ct);
        if (exists)
            return (null, SearchTokenValidationResult.Invalid($"Stop word '{normalizedWord}' already exists for this tenant."));

        var now = DateTimeOffset.UtcNow;
        var entity = new StopWordEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Word = normalizedWord,
            GroupName = string.IsNullOrWhiteSpace(request.GroupName) ? "general" : request.GroupName.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };

        _db.StopWords.Add(entity);
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.stop_word.created", tenantId, actorId, correlationId,
            $"Stop word '{entity.Word}' created in group '{entity.GroupName}'.", ct);

        return (MapStopWord(entity), null);
    }

    public async Task<(StopWordResponse? Response, SearchTokenValidationResult? Validation, bool NotFound)> UpdateStopWordAsync(
        string tenantId, string actorId, string correlationId, Guid id, UpdateStopWordRequest request, CancellationToken ct = default)
    {
        var entity = await _db.StopWords.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        if (entity is null)
            return (null, null, true);

        if (request.Word is not null)
        {
            var validation = ValidateStopWord(request.Word);
            if (!validation.IsValid)
                return (null, validation, false);

            var normalizedWord = request.Word.Trim().ToLowerInvariant();
            var duplicate = await _db.StopWords.AnyAsync(s => s.TenantId == tenantId && s.Word == normalizedWord && s.Id != id, ct);
            if (duplicate)
                return (null, SearchTokenValidationResult.Invalid($"Stop word '{normalizedWord}' already exists for this tenant."), false);

            entity.Word = normalizedWord;
        }

        if (request.GroupName is not null)
            entity.GroupName = request.GroupName.Trim();
        if (request.IsActive.HasValue)
            entity.IsActive = request.IsActive.Value;

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.stop_word.updated", tenantId, actorId, correlationId,
            $"Stop word '{entity.Word}' updated.", ct);

        return (MapStopWord(entity), null, false);
    }

    public async Task<bool> DeleteStopWordAsync(string tenantId, string actorId, string correlationId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.StopWords.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        if (entity is null)
            return false;

        _db.StopWords.Remove(entity);
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.stop_word.deleted", tenantId, actorId, correlationId,
            $"Stop word '{entity.Word}' deleted.", ct);

        return true;
    }

    public async Task<int> SeedDefaultStopWordsAsync(string tenantId, string actorId, string correlationId, bool overwriteExisting = false, CancellationToken ct = default)
    {
        var defaults = GetDefaultStopWords();
        var existing = await _db.StopWords
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.Word)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var seeded = 0;

        foreach (var (word, group) in defaults)
        {
            if (!overwriteExisting && existingSet.Contains(word))
                continue;

            if (overwriteExisting && existingSet.Contains(word))
            {
                var existingEntity = await _db.StopWords.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Word == word, ct);
                if (existingEntity is not null)
                {
                    existingEntity.GroupName = group;
                    existingEntity.IsActive = true;
                    existingEntity.UpdatedAt = now;
                }
            }
            else
            {
                _db.StopWords.Add(new StopWordEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Word = word,
                    GroupName = group,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actorId,
                });
            }

            seeded++;
        }

        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.stop_words.seeded", tenantId, actorId, correlationId,
            $"Seeded {seeded} default stop words.", ct);

        return seeded;
    }

    // ========== Special Tokens ==========

    public async Task<SpecialTokenListResponse> ListSpecialTokensAsync(string tenantId, string? category = null, CancellationToken ct = default)
    {
        var query = _db.SpecialTokens.Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(s => s.Category == category);

        var tokens = await query.OrderBy(s => s.Category).ThenBy(s => s.Token).ToListAsync(ct);
        var categories = await _db.SpecialTokens.Where(s => s.TenantId == tenantId).Select(s => s.Category).Distinct().OrderBy(c => c).ToListAsync(ct);

        return new SpecialTokenListResponse
        {
            Tokens = tokens.Select(MapSpecialToken).ToList(),
            TotalCount = tokens.Count,
            Categories = categories,
        };
    }

    public async Task<SpecialTokenResponse?> GetSpecialTokenAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.SpecialTokens.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        return entity is null ? null : MapSpecialToken(entity);
    }

    public async Task<(SpecialTokenResponse? Response, SearchTokenValidationResult? Validation)> CreateSpecialTokenAsync(
        string tenantId, string actorId, string correlationId, CreateSpecialTokenRequest request, CancellationToken ct = default)
    {
        var validation = ValidateSpecialToken(request.Token, request.BoostFactor);
        if (!validation.IsValid)
            return (null, validation);

        var normalizedToken = request.Token.Trim();

        var exists = await _db.SpecialTokens.AnyAsync(s => s.TenantId == tenantId && s.Token == normalizedToken, ct);
        if (exists)
            return (null, SearchTokenValidationResult.Invalid($"Special token '{normalizedToken}' already exists for this tenant."));

        var now = DateTimeOffset.UtcNow;
        var entity = new SpecialTokenEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Token = normalizedToken,
            Category = string.IsNullOrWhiteSpace(request.Category) ? "error-code" : request.Category.Trim(),
            BoostFactor = request.BoostFactor,
            IsActive = true,
            Description = request.Description?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };

        _db.SpecialTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.special_token.created", tenantId, actorId, correlationId,
            $"Special token '{entity.Token}' created in category '{entity.Category}' with boost {entity.BoostFactor}.", ct);

        return (MapSpecialToken(entity), null);
    }

    public async Task<(SpecialTokenResponse? Response, SearchTokenValidationResult? Validation, bool NotFound)> UpdateSpecialTokenAsync(
        string tenantId, string actorId, string correlationId, Guid id, UpdateSpecialTokenRequest request, CancellationToken ct = default)
    {
        var entity = await _db.SpecialTokens.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        if (entity is null)
            return (null, null, true);

        if (request.Token is not null)
        {
            var validation = ValidateSpecialToken(request.Token, request.BoostFactor ?? entity.BoostFactor);
            if (!validation.IsValid)
                return (null, validation, false);

            var normalizedToken = request.Token.Trim();
            var duplicate = await _db.SpecialTokens.AnyAsync(s => s.TenantId == tenantId && s.Token == normalizedToken && s.Id != id, ct);
            if (duplicate)
                return (null, SearchTokenValidationResult.Invalid($"Special token '{normalizedToken}' already exists for this tenant."), false);

            entity.Token = normalizedToken;
        }

        if (request.Category is not null)
            entity.Category = request.Category.Trim();
        if (request.BoostFactor.HasValue)
        {
            if (request.BoostFactor.Value < 1 || request.BoostFactor.Value > 10)
                return (null, SearchTokenValidationResult.Invalid("Boost factor must be between 1 and 10."), false);
            entity.BoostFactor = request.BoostFactor.Value;
        }
        if (request.IsActive.HasValue)
            entity.IsActive = request.IsActive.Value;
        if (request.Description is not null)
            entity.Description = request.Description.Trim();

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.special_token.updated", tenantId, actorId, correlationId,
            $"Special token '{entity.Token}' updated.", ct);

        return (MapSpecialToken(entity), null, false);
    }

    public async Task<bool> DeleteSpecialTokenAsync(string tenantId, string actorId, string correlationId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.SpecialTokens.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        if (entity is null)
            return false;

        _db.SpecialTokens.Remove(entity);
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.special_token.deleted", tenantId, actorId, correlationId,
            $"Special token '{entity.Token}' deleted.", ct);

        return true;
    }

    public async Task<int> SeedDefaultSpecialTokensAsync(string tenantId, string actorId, string correlationId, bool overwriteExisting = false, CancellationToken ct = default)
    {
        var defaults = GetDefaultSpecialTokens();
        var existing = await _db.SpecialTokens
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.Token)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var seeded = 0;

        foreach (var (token, category, boost, description) in defaults)
        {
            if (!overwriteExisting && existingSet.Contains(token))
                continue;

            if (overwriteExisting && existingSet.Contains(token))
            {
                var existingEntity = await _db.SpecialTokens.FirstOrDefaultAsync(
                    s => s.TenantId == tenantId && s.Token == token, ct);
                if (existingEntity is not null)
                {
                    existingEntity.Category = category;
                    existingEntity.BoostFactor = boost;
                    existingEntity.Description = description;
                    existingEntity.IsActive = true;
                    existingEntity.UpdatedAt = now;
                }
            }
            else
            {
                _db.SpecialTokens.Add(new SpecialTokenEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Token = token,
                    Category = category,
                    BoostFactor = boost,
                    IsActive = true,
                    Description = description,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actorId,
                });
            }

            seeded++;
        }

        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("search.special_tokens.seeded", tenantId, actorId, correlationId,
            $"Seeded {seeded} default special tokens.", ct);

        return seeded;
    }

    // ========== Query Preprocessing ==========

    public async Task<QueryPreprocessingResult> PreprocessQueryAsync(string tenantId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryPreprocessingResult
            {
                ProcessedQuery = query,
                RemovedStopWords = [],
                DetectedSpecialTokens = [],
                BoostedQuery = query,
            };
        }

        // Load active stop words and special tokens for this tenant.
        var stopWords = await _db.StopWords
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .Select(s => s.Word)
            .ToListAsync(ct);
        var stopWordSet = new HashSet<string>(stopWords, StringComparer.OrdinalIgnoreCase);

        var specialTokens = await _db.SpecialTokens
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .ToListAsync(ct);

        // Detect special tokens in the query first (preserve them).
        var detectedTokens = new List<(string Token, int BoostFactor)>();
        var tokenProtectedRanges = new List<(int Start, int End)>();

        foreach (var st in specialTokens)
        {
            var idx = query.IndexOf(st.Token, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                detectedTokens.Add((st.Token, st.BoostFactor));
                tokenProtectedRanges.Add((idx, idx + st.Token.Length));
                idx = query.IndexOf(st.Token, idx + st.Token.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Deduplicate detected tokens.
        var uniqueDetected = detectedTokens.Select(d => d.Token).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Split query into words and remove stop words (but preserve special tokens).
        var words = WordSplitRegex().Split(query);
        var removedStopWords = new List<string>();
        var filteredWords = new List<string>();

        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;

            // Check if this word is part of a special token — always preserve.
            var isProtected = specialTokens.Any(st =>
                string.Equals(word, st.Token, StringComparison.OrdinalIgnoreCase) ||
                st.Token.Contains(word, StringComparison.OrdinalIgnoreCase));

            if (!isProtected && stopWordSet.Contains(word))
            {
                removedStopWords.Add(word);
                continue;
            }

            filteredWords.Add(word);
        }

        var processedQuery = string.Join(" ", filteredWords);

        // Build boosted query: append special tokens with their boost factor.
        var boostedParts = new List<string> { processedQuery };
        foreach (var dt in detectedTokens.DistinctBy(d => d.Token, StringComparer.OrdinalIgnoreCase))
        {
            for (var i = 1; i < dt.BoostFactor; i++)
                boostedParts.Add(dt.Token);
        }

        var boostedQuery = string.Join(" ", boostedParts);

        _logger.LogInformation(
            "Query preprocessed. TenantId={TenantId} StopWordsRemoved={StopWordsRemoved} SpecialTokensDetected={SpecialTokensDetected}",
            tenantId, removedStopWords.Count, uniqueDetected.Count);

        return new QueryPreprocessingResult
        {
            ProcessedQuery = processedQuery,
            RemovedStopWords = removedStopWords,
            DetectedSpecialTokens = uniqueDetected,
            BoostedQuery = boostedQuery,
        };
    }

    // ========== Audit Helper ==========

    private Task WriteAuditAsync(string eventType, string tenantId, string actorId, string correlationId, string detail, CancellationToken ct) =>
        _audit.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: eventType,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: detail), ct);

    // ========== Validation ==========

    private static SearchTokenValidationResult ValidateStopWord(string word)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(word))
            errors.Add("Word cannot be empty.");
        else if (word.Trim().Length > 128)
            errors.Add("Word cannot exceed 128 characters.");
        else if (word.Trim().Contains(' '))
            errors.Add("Stop word must be a single word (no spaces).");

        return errors.Count > 0
            ? SearchTokenValidationResult.Invalid(errors.ToArray())
            : SearchTokenValidationResult.Valid();
    }

    private static SearchTokenValidationResult ValidateSpecialToken(string token, int boostFactor)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(token))
            errors.Add("Token cannot be empty.");
        else if (token.Trim().Length > 256)
            errors.Add("Token cannot exceed 256 characters.");

        if (boostFactor < 1 || boostFactor > 10)
            errors.Add("Boost factor must be between 1 and 10.");

        return errors.Count > 0
            ? SearchTokenValidationResult.Invalid(errors.ToArray())
            : SearchTokenValidationResult.Valid();
    }

    // ========== Mapping ==========

    private static StopWordResponse MapStopWord(StopWordEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Word = e.Word,
        GroupName = e.GroupName,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        CreatedBy = e.CreatedBy,
    };

    private static SpecialTokenResponse MapSpecialToken(SpecialTokenEntity e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Token = e.Token,
        Category = e.Category,
        BoostFactor = e.BoostFactor,
        IsActive = e.IsActive,
        Description = e.Description,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        CreatedBy = e.CreatedBy,
    };

    // ========== Defaults ==========

    private static IReadOnlyList<(string Word, string Group)> GetDefaultStopWords() =>
    [
        // Greeting/filler words common in support tickets
        ("hello", "greeting"),
        ("hi", "greeting"),
        ("hey", "greeting"),
        ("thanks", "greeting"),
        ("thank", "greeting"),
        ("please", "filler"),
        ("kindly", "filler"),
        ("regards", "greeting"),
        ("dear", "greeting"),
        // Common support noise words
        ("help", "filler"),
        ("need", "filler"),
        ("want", "filler"),
        ("urgent", "filler"),
        ("asap", "filler"),
        ("immediately", "filler"),
    ];

    private static IReadOnlyList<(string Token, string Category, int Boost, string? Description)> GetDefaultSpecialTokens() =>
    [
        // Common error codes
        ("BSOD", "error-code", 3, "Blue screen of death"),
        ("0x80070005", "hex-error", 3, "Access denied Windows error"),
        ("0x80004005", "hex-error", 3, "Unspecified Windows error"),
        ("0x800700E1", "hex-error", 3, "Virus detected error"),
        // HTTP status codes
        ("HTTP 400", "http-status", 2, "Bad Request"),
        ("HTTP 401", "http-status", 2, "Unauthorized"),
        ("HTTP 403", "http-status", 2, "Forbidden"),
        ("HTTP 404", "http-status", 2, "Not Found"),
        ("HTTP 500", "http-status", 2, "Internal Server Error"),
        ("HTTP 502", "http-status", 2, "Bad Gateway"),
        ("HTTP 503", "http-status", 2, "Service Unavailable"),
        // Azure AD error codes
        ("AADSTS50076", "aad-error", 3, "MFA required"),
        ("AADSTS700016", "aad-error", 3, "Application not found"),
        ("AADSTS90002", "aad-error", 3, "Tenant not found"),
    ];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WordSplitRegex();
}
