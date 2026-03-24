using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// P2-004: Detects contradictions between case patterns using token-overlap analysis.
/// Compares symptom/problem domains and flags patterns with similar problems but diverging resolutions.
/// All detections require human review before action.
/// </summary>
public sealed class ContradictionDetectionService : IContradictionDetectionService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly PatternMaintenanceSettings _settings;
    private readonly ILogger<ContradictionDetectionService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly char[] TokenSeparators = [' ', ',', '.', ';', ':', '!', '?', '\n', '\r', '\t', '(', ')', '[', ']', '{', '}', '"', '\''];

    public ContradictionDetectionService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        PatternMaintenanceSettings settings,
        ILogger<ContradictionDetectionService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ContradictionDetectionResult> DetectContradictionsAsync(
        string tenantId, string actorId, string correlationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Load all active (non-deprecated) patterns for the tenant.
        var patterns = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId && p.TrustLevel != "Deprecated")
            .ToListAsync(ct);

        // Load existing pending contradictions to avoid duplicates.
        var existingPairs = (await _db.PatternContradictions
            .Where(c => c.TenantId == tenantId && c.Status == "Pending")
            .ToListAsync(ct))
            .Select(c => NormalizePair(c.PatternIdA, c.PatternIdB))
            .ToHashSet();

        int contradictionsFound = 0;
        int newContradictions = 0;
        int skippedExisting = 0;
        int pairsCompared = 0;

        // Compare each pair of patterns.
        for (int i = 0; i < patterns.Count && pairsCompared < _settings.MaxComparisonPairs; i++)
        {
            for (int j = i + 1; j < patterns.Count && pairsCompared < _settings.MaxComparisonPairs; j++)
            {
                pairsCompared++;
                var a = patterns[i];
                var b = patterns[j];

                var analysis = AnalyzeContradiction(a, b);
                if (analysis is null)
                    continue;

                contradictionsFound++;
                var pair = NormalizePair(a.PatternId, b.PatternId);

                if (existingPairs.Contains(pair))
                {
                    skippedExisting++;
                    continue;
                }

                var entity = new PatternContradictionEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PatternIdA = a.PatternId,
                    PatternIdB = b.PatternId,
                    ContradictionType = analysis.Value.Type,
                    SimilarityScore = analysis.Value.SimilarityScore,
                    Description = analysis.Value.Description,
                    ConflictingFieldsJson = JsonSerializer.Serialize(analysis.Value.ConflictingFields, JsonOpts),
                    Status = "Pending",
                    CreatedAt = now,
                };

                _db.PatternContradictions.Add(entity);
                existingPairs.Add(pair);
                newContradictions++;

                _logger.LogInformation(
                    "Contradiction detected between {PatternA} and {PatternB}: {Type} (similarity={Score:F2})",
                    a.PatternId, b.PatternId, analysis.Value.Type, analysis.Value.SimilarityScore);
            }
        }

        if (newContradictions > 0)
            await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: correlationId,
            EventType: AuditEventTypes.ContradictionDetectionRun,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Contradiction detection: {patterns.Count} patterns analyzed, {contradictionsFound} contradictions found, {newContradictions} new, {skippedExisting} existing"));

        return new ContradictionDetectionResult
        {
            PatternsAnalyzed = patterns.Count,
            ContradictionsFound = contradictionsFound,
            NewContradictions = newContradictions,
            SkippedExisting = skippedExisting,
            DetectedAt = now,
        };
    }

    public async Task<ContradictionListResponse> GetContradictionsAsync(
        string tenantId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PatternContradictions
            .Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(c => c.Status == status);

        var totalCount = await query.CountAsync(ct);

        // Materialize then sort/page client-side (SQLite compatibility for tests).
        var allEntities = await query.ToListAsync(ct);
        var entities = allEntities
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Resolve pattern titles for display.
        var patternIds = entities.SelectMany(c => new[] { c.PatternIdA, c.PatternIdB }).Distinct().ToList();
        var patternTitles = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId && patternIds.Contains(p.PatternId))
            .Select(p => new { p.PatternId, p.Title })
            .ToListAsync(ct);
        var titleMap = patternTitles.ToDictionary(p => p.PatternId, p => p.Title);

        var summaries = entities.Select(c => new ContradictionSummary
        {
            Id = c.Id,
            PatternIdA = c.PatternIdA,
            PatternIdB = c.PatternIdB,
            PatternTitleA = titleMap.GetValueOrDefault(c.PatternIdA, "(unknown)"),
            PatternTitleB = titleMap.GetValueOrDefault(c.PatternIdB, "(unknown)"),
            ContradictionType = c.ContradictionType,
            SimilarityScore = c.SimilarityScore,
            Description = c.Description,
            ConflictingFields = DeserializeStringList(c.ConflictingFieldsJson),
            Status = c.Status,
            Resolution = c.Resolution,
            ResolvedBy = c.ResolvedBy,
            ResolvedAt = c.ResolvedAt,
            CreatedAt = c.CreatedAt,
        }).ToList();

        return new ContradictionListResponse
        {
            Contradictions = summaries,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount,
        };
    }

    public async Task<ContradictionSummary?> ResolveContradictionAsync(
        Guid contradictionId, string tenantId, string actorId,
        string correlationId, ResolveContradictionRequest request, CancellationToken ct = default)
    {
        var validResolutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Merged", "Deprecated", "Kept", "Dismissed" };

        if (!validResolutions.Contains(request.Resolution))
            return null;

        var entity = await _db.PatternContradictions
            .Where(c => c.Id == contradictionId && c.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (entity is null || entity.Status != "Pending")
            return null;

        var now = DateTimeOffset.UtcNow;
        entity.Status = "Resolved";
        entity.Resolution = request.Resolution;
        entity.ResolvedBy = actorId;
        entity.ResolvedAt = now;
        entity.ResolutionNotes = request.Notes;

        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: contradictionId.ToString(),
            EventType: AuditEventTypes.ContradictionResolved,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Contradiction {contradictionId} resolved as {request.Resolution}: {entity.PatternIdA} vs {entity.PatternIdB}"));

        // Resolve pattern titles.
        var titles = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId &&
                        (p.PatternId == entity.PatternIdA || p.PatternId == entity.PatternIdB))
            .Select(p => new { p.PatternId, p.Title })
            .ToListAsync(ct);
        var titleMap = titles.ToDictionary(p => p.PatternId, p => p.Title);

        return new ContradictionSummary
        {
            Id = entity.Id,
            PatternIdA = entity.PatternIdA,
            PatternIdB = entity.PatternIdB,
            PatternTitleA = titleMap.GetValueOrDefault(entity.PatternIdA, "(unknown)"),
            PatternTitleB = titleMap.GetValueOrDefault(entity.PatternIdB, "(unknown)"),
            ContradictionType = entity.ContradictionType,
            SimilarityScore = entity.SimilarityScore,
            Description = entity.Description,
            ConflictingFields = DeserializeStringList(entity.ConflictingFieldsJson),
            Status = entity.Status,
            Resolution = entity.Resolution,
            ResolvedBy = entity.ResolvedBy,
            ResolvedAt = entity.ResolvedAt,
            CreatedAt = entity.CreatedAt,
        };
    }

    // --- Contradiction Analysis ---

    internal readonly record struct ContradictionAnalysis(
        string Type, float SimilarityScore, string Description, List<string> ConflictingFields);

    internal ContradictionAnalysis? AnalyzeContradiction(CasePatternEntity a, CasePatternEntity b)
    {
        // Only compare patterns in the same product area (or both null).
        if (!string.Equals(a.ProductArea, b.ProductArea, StringComparison.OrdinalIgnoreCase))
            return null;

        var symptomsA = Tokenize(a.SymptomsJson);
        var symptomsB = Tokenize(b.SymptomsJson);
        var titleProblemA = Tokenize(a.Title + " " + a.ProblemStatement);
        var titleProblemB = Tokenize(b.Title + " " + b.ProblemStatement);

        // Compute symptom overlap.
        var symptomOverlap = JaccardSimilarity(symptomsA, symptomsB);
        var problemOverlap = JaccardSimilarity(titleProblemA, titleProblemB);

        // Check for duplicate patterns (high similarity across problem + symptoms).
        var combinedSimilarity = (symptomOverlap + problemOverlap) / 2f;
        if (combinedSimilarity >= _settings.DuplicateThreshold)
        {
            return new ContradictionAnalysis(
                Type: "DuplicatePattern",
                SimilarityScore: combinedSimilarity,
                Description: $"Patterns appear to address the same issue (similarity: {combinedSimilarity:F2}). " +
                             "Consider merging or deprecating one.",
                ConflictingFields: ["Title", "ProblemStatement", "Symptoms"]);
        }

        // Check for resolution conflict: similar symptoms but different resolutions.
        var domainOverlap = Math.Max(symptomOverlap, problemOverlap);
        if (domainOverlap >= _settings.SymptomOverlapThreshold)
        {
            var resolutionA = Tokenize(a.ResolutionStepsJson);
            var resolutionB = Tokenize(b.ResolutionStepsJson);
            var resolutionSimilarity = JaccardSimilarity(resolutionA, resolutionB);

            // If resolutions are very different despite similar problem domain → conflict.
            if (resolutionSimilarity < (1.0f - _settings.ResolutionDivergenceThreshold))
            {
                var conflictingFields = new List<string> { "ResolutionSteps" };
                if (symptomOverlap >= _settings.SymptomOverlapThreshold)
                    conflictingFields.Add("Symptoms");

                return new ContradictionAnalysis(
                    Type: "ResolutionConflict",
                    SimilarityScore: domainOverlap,
                    Description: $"Patterns share similar problem domain (overlap: {domainOverlap:F2}) " +
                                 $"but have diverging resolution steps (similarity: {resolutionSimilarity:F2}). " +
                                 "Review whether both resolutions are valid for different contexts.",
                    ConflictingFields: conflictingFields);
            }
        }

        return null;
    }

    internal static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.ToLowerInvariant()
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2) // skip very short tokens
            .ToHashSet();
    }

    internal static float JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 0f;
        if (a.Count == 0 || b.Count == 0)
            return 0f;

        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0f : (float)intersection / union;
    }

    private static string NormalizePair(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    private IReadOnlyList<string> DeserializeStringList(string? json) =>
        JsonDeserializeHelper.Deserialize<List<string>>(json, JsonOpts, _logger, []);
}
