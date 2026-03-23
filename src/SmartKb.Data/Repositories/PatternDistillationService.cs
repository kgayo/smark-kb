using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// P1-005: Discovers solved-ticket candidates and distills them into draft case patterns.
/// </summary>
public sealed class PatternDistillationService : IPatternDistillationService
{
    private readonly SmartKbDbContext _db;
    private readonly DistillationSettings _settings;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IPatternIndexingService? _patternIndexing;
    private readonly ICaseCardQualityValidator? _qualityValidator;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<PatternDistillationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PatternDistillationService(
        SmartKbDbContext db,
        DistillationSettings settings,
        IAuditEventWriter auditWriter,
        ILogger<PatternDistillationService> logger,
        IEmbeddingService? embeddingService = null,
        IPatternIndexingService? patternIndexing = null,
        ICaseCardQualityValidator? qualityValidator = null)
    {
        _db = db;
        _settings = settings;
        _embeddingService = embeddingService;
        _patternIndexing = patternIndexing;
        _qualityValidator = qualityValidator;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<DistillationCandidateListResponse> FindCandidatesAsync(
        string tenantId, CancellationToken ct = default)
    {
        // Step 1: Find sessions with ResolvedWithoutEscalation outcomes.
        var resolvedSessionIds = await _db.OutcomeEvents
            .Where(o => o.TenantId == tenantId
                        && o.ResolutionType == ResolutionType.ResolvedWithoutEscalation)
            .Select(o => o.SessionId)
            .Distinct()
            .ToListAsync(ct);

        if (resolvedSessionIds.Count == 0)
            return new DistillationCandidateListResponse { TotalCount = 0 };

        // Step 2: Find sessions with sufficient positive feedback.
        var sessionFeedbackCounts = await _db.Feedbacks
            .Where(f => resolvedSessionIds.Contains(f.SessionId) && f.TenantId == tenantId)
            .GroupBy(f => new { f.SessionId, f.Type })
            .Select(g => new { g.Key.SessionId, g.Key.Type, Count = g.Count() })
            .ToListAsync(ct);

        var qualifiedSessionIds = new HashSet<Guid>();
        var positiveCounts = new Dictionary<Guid, int>();
        var negativeCounts = new Dictionary<Guid, int>();

        foreach (var fc in sessionFeedbackCounts)
        {
            if (fc.Type == FeedbackType.ThumbsUp)
                positiveCounts[fc.SessionId] = fc.Count;
            else
                negativeCounts[fc.SessionId] = fc.Count;
        }

        foreach (var sid in resolvedSessionIds)
        {
            if (positiveCounts.TryGetValue(sid, out var pos) && pos >= _settings.MinPositiveFeedback)
                qualifiedSessionIds.Add(sid);
        }

        if (qualifiedSessionIds.Count == 0)
            return new DistillationCandidateListResponse { TotalCount = 0 };

        // Step 3: Get session details.
        var sessions = await _db.Sessions
            .Where(s => qualifiedSessionIds.Contains(s.Id) && s.TenantId == tenantId)
            .Select(s => new { s.Id, s.TenantId, s.UserId, s.Title, s.UpdatedAt })
            .ToListAsync(ct);

        // Step 4: Get answer traces for cited chunk IDs — link via TenantId + session correlation.
        // AnswerTraces are linked to sessions via messages' CorrelationIds.
        var sessionMessageCorrelationIds = await _db.Messages
            .Where(m => qualifiedSessionIds.Contains(m.SessionId)
                        && m.TenantId == tenantId
                        && m.CorrelationId != null)
            .Select(m => new { m.SessionId, m.CorrelationId })
            .ToListAsync(ct);

        var correlationToSession = sessionMessageCorrelationIds
            .Where(m => m.CorrelationId != null)
            .GroupBy(m => m.CorrelationId!)
            .ToDictionary(g => g.Key, g => g.First().SessionId);

        var allCorrelationIds = correlationToSession.Keys.ToList();

        var traces = await _db.AnswerTraces
            .Where(t => t.TenantId == tenantId && allCorrelationIds.Contains(t.CorrelationId))
            .Select(t => new { t.CorrelationId, t.CitedChunkIds })
            .ToListAsync(ct);

        // Map session → cited chunk IDs.
        var sessionCitedChunks = new Dictionary<Guid, HashSet<string>>();
        foreach (var trace in traces)
        {
            if (!correlationToSession.TryGetValue(trace.CorrelationId, out var sessionId))
                continue;

            if (!sessionCitedChunks.TryGetValue(sessionId, out var chunkSet))
            {
                chunkSet = [];
                sessionCitedChunks[sessionId] = chunkSet;
            }

            var chunkIds = DeserializeStringList(trace.CitedChunkIds);
            foreach (var cid in chunkIds) chunkSet.Add(cid);
        }

        // Step 5: Get evidence chunk metadata for product area and tags.
        var allChunkIds = sessionCitedChunks.Values.SelectMany(s => s).Distinct().ToList();
        var chunkMetadata = allChunkIds.Count > 0
            ? await _db.EvidenceChunks
                .Where(c => allChunkIds.Contains(c.ChunkId) && c.TenantId == tenantId)
                .Select(c => new { c.ChunkId, c.EvidenceId, c.ProductArea, c.Tags })
                .ToListAsync(ct)
            : [];

        var chunkToEvidence = chunkMetadata.ToDictionary(c => c.ChunkId, c => c.EvidenceId);

        // Step 6: Check which sessions already have distilled patterns.
        var existingPatternSessions = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.SourceUrl)
            .ToListAsync(ct);

        var distilledSessionIds = existingPatternSessions
            .Where(url => url.StartsWith("session://"))
            .Select(url => url.Replace("session://", ""))
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        // Step 7: Build candidates.
        var candidates = new List<DistillationCandidate>();
        foreach (var session in sessions)
        {
            var citedChunks = sessionCitedChunks.GetValueOrDefault(session.Id);
            if (citedChunks is null || citedChunks.Count < _settings.MinCitedChunks)
                continue;

            var evidenceIds = citedChunks
                .Where(cid => chunkToEvidence.ContainsKey(cid))
                .Select(cid => chunkToEvidence[cid])
                .Distinct()
                .ToList();

            var productAreas = chunkMetadata
                .Where(c => citedChunks.Contains(c.ChunkId) && c.ProductArea != null)
                .Select(c => c.ProductArea!)
                .Distinct()
                .ToList();

            var tags = chunkMetadata
                .Where(c => citedChunks.Contains(c.ChunkId) && c.Tags != null)
                .SelectMany(c => DeserializeStringList(c.Tags!))
                .Distinct()
                .ToList();

            candidates.Add(new DistillationCandidate
            {
                SessionId = session.Id,
                TenantId = session.TenantId,
                UserId = session.UserId,
                SessionTitle = session.Title,
                CitedEvidenceIds = evidenceIds,
                CitedChunkIds = citedChunks.ToList(),
                PositiveFeedbackCount = positiveCounts.GetValueOrDefault(session.Id),
                NegativeFeedbackCount = negativeCounts.GetValueOrDefault(session.Id),
                ProductArea = productAreas.Count == 1 ? productAreas[0] : null,
                Tags = tags,
                ResolvedAt = session.UpdatedAt,
                AlreadyDistilled = distilledSessionIds.Contains(session.Id),
            });
        }

        candidates = candidates
            .OrderByDescending(c => c.PositiveFeedbackCount)
            .ThenByDescending(c => c.CitedEvidenceIds.Count)
            .Take(_settings.MaxCandidates)
            .ToList();

        return new DistillationCandidateListResponse
        {
            Candidates = candidates,
            TotalCount = candidates.Count,
        };
    }

    public async Task<DistillationResult> DistillAsync(
        string tenantId, string actorId, string correlationId,
        CancellationToken ct = default)
    {
        var candidateList = await FindCandidatesAsync(tenantId, ct);
        var eligibleCandidates = candidateList.Candidates
            .Where(c => !c.AlreadyDistilled)
            .Take(_settings.MaxBatchSize)
            .ToList();

        var createdPatternIds = new List<string>();
        var errors = new List<string>();
        var skipped = 0;

        foreach (var candidate in eligibleCandidates)
        {
            try
            {
                var pattern = await DistillCandidateAsync(candidate, tenantId, ct);
                if (pattern is null)
                {
                    skipped++;
                    continue;
                }

                createdPatternIds.Add(pattern.PatternId);

                await _auditWriter.WriteAsync(new AuditEvent(
                    EventId: pattern.PatternId,
                    EventType: AuditEventTypes.PatternDistilled,
                    TenantId: tenantId,
                    ActorId: actorId,
                    CorrelationId: correlationId,
                    Timestamp: DateTimeOffset.UtcNow,
                    Detail: $"Pattern distilled from session {candidate.SessionId}: {pattern.Title}"), ct);
            }
            catch (Exception ex)
            {
                errors.Add($"Session {candidate.SessionId}: {ex.Message}");
                _logger.LogWarning(ex,
                    "Failed to distill pattern from session {SessionId} in tenant {TenantId}",
                    candidate.SessionId, tenantId);
            }
        }

        var now = DateTimeOffset.UtcNow;

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.PatternDistillationRun,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Distillation run: {createdPatternIds.Count} created, {skipped} skipped, {errors.Count} errors from {eligibleCandidates.Count} candidates"), ct);

        _logger.LogInformation(
            "Distillation run complete. PatternsCreated={Created}, Skipped={Skipped}, Errors={Errors}, Candidates={Candidates}, TenantId={TenantId}",
            createdPatternIds.Count, skipped, errors.Count, eligibleCandidates.Count, tenantId);

        return new DistillationResult
        {
            CandidatesEvaluated = eligibleCandidates.Count,
            PatternsCreated = createdPatternIds.Count,
            PatternsSkipped = skipped,
            CreatedPatternIds = createdPatternIds,
            Errors = errors,
            CompletedAt = now,
        };
    }

    private async Task<CasePatternEntity?> DistillCandidateAsync(
        DistillationCandidate candidate, string tenantId, CancellationToken ct)
    {
        // Fetch the cited evidence chunk texts for content extraction.
        var chunks = await _db.EvidenceChunks
            .Where(c => candidate.CitedChunkIds.Contains(c.ChunkId) && c.TenantId == tenantId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

        if (chunks.Count == 0)
            return null;

        // Extract pattern fields from evidence content.
        var patternId = $"pattern-{Guid.NewGuid():N}";
        var title = BuildTitle(candidate, chunks);
        var problemStatement = BuildProblemStatement(candidate, chunks);
        var symptoms = ExtractSymptoms(chunks);
        var rootCause = ExtractRootCause(chunks);
        var resolutionSteps = ExtractResolutionSteps(candidate, chunks);
        var diagnosisSteps = ExtractDiagnosisSteps(chunks);
        var verificationSteps = ExtractVerificationSteps(chunks);
        var errorTokens = ExtractErrorTokens(chunks);

        if (resolutionSteps.Count == 0)
            return null; // Cannot create a pattern without resolution steps.

        // Compute confidence.
        var confidence = ComputeConfidence(candidate);

        // Compute embedding if service available.
        float[]? embedding = null;
        if (_embeddingService is not null)
        {
            var embeddingText = $"{title}\n{problemStatement}\n{string.Join("\n", symptoms)}";
            try
            {
                embedding = await _embeddingService.GenerateEmbeddingAsync(embeddingText, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for pattern {PatternId}", patternId);
            }
        }

        var now = DateTimeOffset.UtcNow;

        // Determine ACL from evidence chunks — use the most restrictive.
        var visibility = DetermineVisibility(chunks);
        var allowedGroups = chunks
            .Where(c => c.AllowedGroups != null)
            .SelectMany(c => DeserializeStringList(c.AllowedGroups!))
            .Distinct()
            .ToList();

        var entity = new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = patternId,
            TenantId = tenantId,
            Title = title,
            ProblemStatement = problemStatement,
            RootCause = rootCause,
            SymptomsJson = JsonSerializer.Serialize(symptoms, JsonOpts),
            DiagnosisStepsJson = JsonSerializer.Serialize(diagnosisSteps, JsonOpts),
            ResolutionStepsJson = JsonSerializer.Serialize(resolutionSteps, JsonOpts),
            VerificationStepsJson = JsonSerializer.Serialize(verificationSteps, JsonOpts),
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = JsonSerializer.Serialize(
                candidate.CitedEvidenceIds.ToList(), JsonOpts),
            Confidence = confidence,
            TrustLevel = TrustLevel.Draft.ToString(),
            Version = 1,
            ProductArea = candidate.ProductArea,
            TagsJson = JsonSerializer.Serialize(candidate.Tags.ToList(), JsonOpts),
            Visibility = visibility,
            AllowedGroupsJson = JsonSerializer.Serialize(allowedGroups, JsonOpts),
            AccessLabel = visibility == "Restricted" ? "Restricted" : "Internal",
            SourceUrl = $"session://{candidate.SessionId}",
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Quality gate: validate the pattern before persisting.
        if (_qualityValidator is not null)
        {
            var tempPattern = MapEntityToModel(entity, embedding);
            var qualityReport = _qualityValidator.Validate(tempPattern);

            if (qualityReport.Rejected)
            {
                _logger.LogInformation(
                    "Pattern {PatternId} rejected by quality gate (score={Score:F2}): {Issues}",
                    patternId, qualityReport.QualityScore,
                    string.Join("; ", qualityReport.Issues.Select(i => i.Message)));
                return null;
            }

            // Persist quality metadata on the entity.
            entity.QualityScore = qualityReport.QualityScore;

            if (!qualityReport.Passed)
            {
                _logger.LogInformation(
                    "Pattern {PatternId} below quality threshold (score={Score:F2}) but not rejected. Saving as draft with quality flag.",
                    patternId, qualityReport.QualityScore);
            }
        }

        _db.CasePatterns.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Index into Pattern Store if service available.
        if (_patternIndexing is not null && embedding is not null)
        {
            var casePattern = MapEntityToModel(entity, embedding);
            try
            {
                await _patternIndexing.IndexPatternsAsync([casePattern], ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index pattern {PatternId}", patternId);
            }
        }

        return entity;
    }

    internal static string BuildTitle(DistillationCandidate candidate, List<EvidenceChunkEntity> chunks)
    {
        if (!string.IsNullOrWhiteSpace(candidate.SessionTitle))
            return $"Pattern: {candidate.SessionTitle}";

        var firstTitle = chunks.FirstOrDefault()?.Title;
        if (!string.IsNullOrWhiteSpace(firstTitle))
            return $"Pattern: {firstTitle}";

        return $"Pattern from session {candidate.SessionId:N}";
    }

    internal static string BuildProblemStatement(DistillationCandidate candidate, List<EvidenceChunkEntity> chunks)
    {
        // Use the first chunk's text as problem description, truncated.
        var firstChunk = chunks.FirstOrDefault();
        if (firstChunk is null)
            return "Problem identified from solved ticket evidence.";

        var text = firstChunk.ChunkText;
        return text.Length > 500 ? text[..500] + "..." : text;
    }

    internal static List<string> ExtractSymptoms(List<EvidenceChunkEntity> chunks)
    {
        var symptoms = new List<string>();

        // Extract error tokens as symptoms.
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrEmpty(chunk.ErrorTokens)) continue;
            var tokens = DeserializeStringList(chunk.ErrorTokens);
            foreach (var token in tokens)
            {
                if (!symptoms.Contains(token, StringComparer.OrdinalIgnoreCase))
                    symptoms.Add(token);
            }
        }

        // Add chunk titles as symptom indicators if different.
        var titles = chunks.Select(c => c.Title).Distinct().Take(3).ToList();
        foreach (var title in titles)
        {
            if (!string.IsNullOrWhiteSpace(title) && !symptoms.Contains(title, StringComparer.OrdinalIgnoreCase))
                symptoms.Add($"Related to: {title}");
        }

        return symptoms.Take(10).ToList();
    }

    internal static List<string> ExtractResolutionSteps(DistillationCandidate candidate, List<EvidenceChunkEntity> chunks)
    {
        var steps = new List<string>();

        // Resolution steps come from the evidence content — each chunk represents
        // a piece of the resolution narrative.
        foreach (var chunk in chunks)
        {
            var text = chunk.ChunkText.Trim();
            if (text.Length > 200)
                text = text[..200] + "...";

            if (!string.IsNullOrWhiteSpace(text))
                steps.Add(text);
        }

        return steps.Take(10).ToList();
    }

    internal static List<string> ExtractDiagnosisSteps(List<EvidenceChunkEntity> chunks)
    {
        var steps = new List<string>();

        // Look for diagnostic content in chunk context fields.
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.ChunkContext)) continue;
            var context = chunk.ChunkContext.Trim();
            if (context.Length > 200)
                context = context[..200] + "...";
            steps.Add(context);
        }

        return steps.Take(5).ToList();
    }

    internal static List<string> ExtractVerificationSteps(List<EvidenceChunkEntity> chunks)
    {
        // Baseline: derive from the last chunk which often contains verification/follow-up.
        var lastChunk = chunks.LastOrDefault();
        if (lastChunk is null || string.IsNullOrWhiteSpace(lastChunk.ChunkText))
            return ["Verify the issue is resolved by reproducing the original conditions."];

        return ["Verify the fix by confirming the symptoms no longer occur."];
    }

    internal static string? ExtractRootCause(List<EvidenceChunkEntity> chunks)
    {
        // Look for chunks whose context indicates a "Root Cause" section (from ticket-structure chunking).
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.ChunkContext)) continue;
            if (chunk.ChunkContext.Contains("Root Cause", StringComparison.OrdinalIgnoreCase))
            {
                var text = chunk.ChunkText.Trim();
                if (text.Length > 0)
                    return text.Length > 2000 ? text[..2000] : text;
            }
        }

        // Fallback: scan chunk text for root-cause keyword indicators.
        foreach (var chunk in chunks)
        {
            var text = chunk.ChunkText;
            if (text.Contains("root cause", StringComparison.OrdinalIgnoreCase)
                || text.Contains("caused by", StringComparison.OrdinalIgnoreCase)
                || text.Contains("underlying issue", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = text.Trim();
                if (trimmed.Length > 0)
                    return trimmed.Length > 2000 ? trimmed[..2000] : trimmed;
            }
        }

        return null;
    }

    internal static List<string> ExtractErrorTokens(List<EvidenceChunkEntity> chunks)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrEmpty(chunk.ErrorTokens)) continue;
            foreach (var token in DeserializeStringList(chunk.ErrorTokens))
                tokens.Add(token);
        }
        return tokens.ToList();
    }

    internal float ComputeConfidence(DistillationCandidate candidate)
    {
        var confidence = _settings.BaseConfidence;

        // Boost for positive feedback beyond minimum.
        var extraPositive = candidate.PositiveFeedbackCount - _settings.MinPositiveFeedback;
        if (extraPositive > 0)
            confidence += extraPositive * _settings.PositiveFeedbackBoost;

        // Penalty for negative feedback.
        confidence -= candidate.NegativeFeedbackCount * _settings.NegativeFeedbackPenalty;

        // Boost for more cited evidence (richer pattern).
        if (candidate.CitedEvidenceIds.Count >= 3)
            confidence += 0.05f;

        return Math.Clamp(confidence, 0.1f, _settings.MaxConfidence);
    }

    internal static string DetermineVisibility(List<EvidenceChunkEntity> chunks)
    {
        // Use the most restrictive visibility across all cited chunks.
        if (chunks.Any(c => c.Visibility == "Restricted"))
            return "Restricted";
        if (chunks.Any(c => c.Visibility == "Internal"))
            return "Internal";
        return "Public";
    }

    private static CasePattern MapEntityToModel(CasePatternEntity entity, float[]? embedding)
    {
        return new CasePattern
        {
            PatternId = entity.PatternId,
            TenantId = entity.TenantId,
            Title = entity.Title,
            ProblemStatement = entity.ProblemStatement,
            RootCause = entity.RootCause,
            Symptoms = DeserializeStringList(entity.SymptomsJson),
            DiagnosisSteps = DeserializeStringList(entity.DiagnosisStepsJson),
            ResolutionSteps = DeserializeStringList(entity.ResolutionStepsJson),
            VerificationSteps = DeserializeStringList(entity.VerificationStepsJson),
            EscalationCriteria = DeserializeStringList(entity.EscalationCriteriaJson),
            RelatedEvidenceIds = DeserializeStringList(entity.RelatedEvidenceIdsJson),
            Confidence = entity.Confidence,
            TrustLevel = Enum.TryParse<TrustLevel>(entity.TrustLevel, out var tl) ? tl : TrustLevel.Draft,
            Version = entity.Version,
            ProductArea = entity.ProductArea,
            Tags = DeserializeStringList(entity.TagsJson),
            EmbeddingVector = embedding,
            Visibility = Enum.TryParse<AccessVisibility>(entity.Visibility, out var v) ? v : AccessVisibility.Internal,
            AllowedGroups = DeserializeStringList(entity.AllowedGroupsJson),
            AccessLabel = entity.AccessLabel,
            SourceUrl = entity.SourceUrl,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    private static IReadOnlyList<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
