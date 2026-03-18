using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using DiagnosticsHelper = SmartKb.Contracts.Diagnostics;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Orchestrates chat responses: embed → retrieve → prompt → generate → blend confidence → trace → respond.
/// Implements jtbd-04 structured output contract with D-003 confidence, D-010 token budget,
/// D-013 hallucination degradation.
/// </summary>
public sealed class ChatOrchestrator : IChatOrchestrator
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IRetrievalService _retrievalService;
    private readonly IAnswerTraceWriter _traceWriter;
    private readonly IPiiRedactionService _piiRedactionService;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ITokenUsageService _tokenUsageService;
    private readonly IEmbeddingCacheService? _embeddingCacheService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiSettings _openAiSettings;
    private readonly ChatOrchestrationSettings _settings;
    private readonly CostOptimizationSettings _costSettings;
    private readonly ILogger<ChatOrchestrator> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public ChatOrchestrator(
        IEmbeddingService embeddingService,
        IRetrievalService retrievalService,
        IAnswerTraceWriter traceWriter,
        IPiiRedactionService piiRedactionService,
        IAuditEventWriter auditEventWriter,
        ITokenUsageService tokenUsageService,
        IHttpClientFactory httpClientFactory,
        OpenAiSettings openAiSettings,
        ChatOrchestrationSettings settings,
        CostOptimizationSettings costSettings,
        ILogger<ChatOrchestrator> logger,
        IEmbeddingCacheService? embeddingCacheService = null)
    {
        _embeddingService = embeddingService;
        _retrievalService = retrievalService;
        _traceWriter = traceWriter;
        _piiRedactionService = piiRedactionService;
        _auditEventWriter = auditEventWriter;
        _tokenUsageService = tokenUsageService;
        _embeddingCacheService = embeddingCacheService;
        _httpClientFactory = httpClientFactory;
        _openAiSettings = openAiSettings;
        _settings = settings;
        _costSettings = costSettings;
        _logger = logger;
    }

    public async Task<ChatResponse> OrchestrateAsync(
        string tenantId,
        string userId,
        string correlationId,
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        using var orchestrationActivity = DiagnosticsHelper.OrchestrationSource.StartActivity(
            "ChatOrchestrate", ActivityKind.Internal);
        orchestrationActivity?.SetTag("smartkb.tenant_id", tenantId);
        orchestrationActivity?.SetTag("smartkb.user_id", userId);
        orchestrationActivity?.SetTag("smartkb.correlation_id", correlationId);

        var sw = Stopwatch.StartNew();
        var traceId = correlationId;

        _logger.LogInformation(
            "Chat orchestration started. TenantId={TenantId}, UserId={UserId}, TraceId={TraceId}, QueryLength={QueryLength}",
            tenantId, userId, traceId, request.Query.Length);

        // Step 1: Generate query embedding (with P2-003 cache support).
        float[] queryEmbedding;
        var embeddingCacheHit = false;
        try
        {
            using var embeddingActivity = DiagnosticsHelper.OrchestrationSource.StartActivity("EmbedQuery");

            if (_embeddingCacheService is not null && _costSettings.EnableEmbeddingCache)
            {
                var (cached, cacheHit) = await _embeddingCacheService.GetOrGenerateAsync(request.Query, cancellationToken);
                queryEmbedding = cached!;
                embeddingCacheHit = cacheHit;
                embeddingActivity?.SetTag("smartkb.embedding_cache_hit", cacheHit);

                if (cacheHit)
                    DiagnosticsHelper.EmbeddingCacheHitsTotal.Add(1,
                        new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
                else
                    DiagnosticsHelper.EmbeddingCacheMissesTotal.Add(1,
                        new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
            }
            else
            {
                queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Query, cancellationToken);
            }

            embeddingActivity?.SetTag("smartkb.embedding_dims", queryEmbedding.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding generation failed. TraceId={TraceId}", traceId);
            orchestrationActivity?.SetStatus(ActivityStatusCode.Error, "Embedding failed");
            return BuildNoEvidenceResponse(traceId, "Unable to process your query at this time. Please try again.");
        }

        // Step 2: Retrieve evidence with tenant isolation and ACL filtering.
        RetrievalResult retrievalResult;
        try
        {
            using var retrievalActivity = DiagnosticsHelper.OrchestrationSource.StartActivity("RetrieveEvidence");
            retrievalResult = await _retrievalService.RetrieveAsync(
                tenantId, request.Query, queryEmbedding, request.UserGroups, request.Filters, correlationId, cancellationToken);
            retrievalActivity?.SetTag("smartkb.chunk_count", retrievalResult.Chunks.Count);
            retrievalActivity?.SetTag("smartkb.has_evidence", retrievalResult.HasEvidence);
            retrievalActivity?.SetTag("smartkb.acl_filtered", retrievalResult.AclFilteredOutCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retrieval failed. TraceId={TraceId}", traceId);
            orchestrationActivity?.SetStatus(ActivityStatusCode.Error, "Retrieval failed");
            return BuildNoEvidenceResponse(traceId, "Unable to search the knowledge base at this time. Please try again.");
        }

        _logger.LogInformation(
            "Retrieval complete. TraceId={TraceId}, HasEvidence={HasEvidence}, ChunkCount={ChunkCount}, " +
            "AclFiltered={AclFiltered}",
            traceId, retrievalResult.HasEvidence, retrievalResult.Chunks.Count,
            retrievalResult.AclFilteredOutCount);

        // Step 3: If no evidence, produce degraded response (D-013).
        if (!retrievalResult.HasEvidence)
        {
            _logger.LogInformation("No evidence path triggered. TraceId={TraceId}", traceId);
            DiagnosticsHelper.ChatRequestsTotal.Add(1,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId),
                new KeyValuePair<string, object?>("smartkb.response_type", "next_steps_only"));
            DiagnosticsHelper.ChatNoEvidenceTotal.Add(1,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
            DiagnosticsHelper.ChatLatencyMs.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId),
                new KeyValuePair<string, object?>("smartkb.response_type", "next_steps_only"));
            return BuildNoEvidenceResponse(traceId);
        }

        // Step 3.5 (P0-014): Defense-in-depth — enforce restricted content exclusion before prompt assembly.
        // The retrieval layer already applies ACL filtering, but this guard prevents any Restricted
        // content from reaching the model if the retrieval layer has a bug or is bypassed.
        var (safeChunks, restrictedRemovedCount) = EnforceRestrictedContentExclusion(
            retrievalResult.Chunks, request.UserGroups);

        if (restrictedRemovedCount > 0)
        {
            _logger.LogCritical(
                "SECURITY: Restricted content detected in orchestration layer after retrieval ACL filter. " +
                "RemovedCount={RemovedCount}, TraceId={TraceId}. This indicates a bug in the retrieval layer.",
                restrictedRemovedCount, traceId);
        }

        // Step 3.75 (P0-014A): PII redaction — detect and mask PII in chunk text before prompt assembly.
        // Defense-in-depth: even if PII was flagged during enrichment, redact at orchestration time
        // to ensure no PII reaches the model context regardless of indexing pipeline behavior.
        var (redactedChunks, piiRedactedCount) = RedactPiiInChunks(safeChunks, _piiRedactionService);

        if (piiRedactedCount > 0)
        {
            _logger.LogWarning(
                "PII redacted in {RedactedCount} chunk(s) before model context assembly. TraceId={TraceId}",
                piiRedactedCount, traceId);

            try
            {
                await _auditEventWriter.WriteAsync(new AuditEvent(
                    EventId: Guid.NewGuid().ToString(),
                    EventType: AuditEventTypes.PiiRedaction,
                    TenantId: tenantId,
                    ActorId: userId,
                    CorrelationId: correlationId,
                    Timestamp: DateTimeOffset.UtcNow,
                    Detail: $"PII redacted in {piiRedactedCount} chunk(s) before model context assembly."),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist PII redaction audit event. TraceId={TraceId}", traceId);
            }
        }

        // Step 3.9 (P2-003): Retrieval compression — truncate long chunks to reduce token usage.
        var compressionTruncatedCount = 0;
        if (_costSettings.EnableRetrievalCompression)
        {
            var (compressed, truncCount) = RetrievalCompressionService.CompressChunks(
                redactedChunks, _costSettings.MaxChunkCharsCompressed);
            redactedChunks = compressed;
            compressionTruncatedCount = truncCount;

            if (truncCount > 0)
            {
                _logger.LogInformation(
                    "Retrieval compression truncated {TruncatedCount} chunk(s). TraceId={TraceId}",
                    truncCount, traceId);
                DiagnosticsHelper.RetrievalCompressionTruncatedTotal.Add(truncCount,
                    new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
            }
        }

        // Step 4: Assemble prompt with evidence context and session history (D-010 token budget).
        var evidenceChunks = redactedChunks
            .Take(_settings.MaxEvidenceChunksInPrompt)
            .ToList();

        var systemPrompt = BuildSystemPrompt(evidenceChunks);
        var messages = AssembleMessages(systemPrompt, request.SessionHistory, request.Query);

        // Step 5: Call OpenAI with structured output.
        OpenAiCallResult callResult;
        try
        {
            using var generationActivity = DiagnosticsHelper.OrchestrationSource.StartActivity("GenerateAnswer");
            generationActivity?.SetTag("smartkb.model", _openAiSettings.Model);
            callResult = await CallOpenAiStructuredAsync(messages, cancellationToken);
            generationActivity?.SetTag("smartkb.response_type", callResult.Response?.ResponseType);
            generationActivity?.SetTag("smartkb.prompt_tokens", callResult.PromptTokens);
            generationActivity?.SetTag("smartkb.completion_tokens", callResult.CompletionTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI generation failed. TraceId={TraceId}", traceId);
            orchestrationActivity?.SetStatus(ActivityStatusCode.Error, "Generation failed");
            return BuildNoEvidenceResponse(traceId, "Unable to generate a response at this time. Please try again.");
        }

        var modelResponse = callResult.Response;
        if (modelResponse is null)
        {
            _logger.LogWarning("OpenAI returned null/unparseable response. TraceId={TraceId}", traceId);
            return BuildNoEvidenceResponse(traceId, "Unable to parse the model response. Please try again.");
        }

        // Step 6: Compute blended confidence (D-003).
        var retrievalConfidence = ComputeRetrievalConfidence(evidenceChunks);
        var blendedConfidence = Math.Clamp(
            _settings.ModelConfidenceWeight * modelResponse.Confidence +
            _settings.RetrievalConfidenceWeight * retrievalConfidence,
            0f, 1f);

        var confidenceLabel = _settings.GetConfidenceLabel(blendedConfidence);

        // Step 7: Apply D-013 degradation — if blended confidence below threshold, override to next_steps_only.
        var responseType = modelResponse.ResponseType;
        var answer = modelResponse.Answer;
        var nextSteps = modelResponse.NextSteps.AsReadOnly();

        if (blendedConfidence < _settings.DegradationThreshold && responseType == "final_answer")
        {
            _logger.LogInformation(
                "Degradation triggered: blended confidence {Confidence} < {Threshold}. TraceId={TraceId}",
                blendedConfidence, _settings.DegradationThreshold, traceId);

            responseType = "next_steps_only";
            answer = "I don't have enough information to answer this question confidently. " +
                     "Here are some diagnostic steps that may help:";

            if (nextSteps.Count == 0)
            {
                nextSteps = new List<string>
                {
                    "Try rephrasing your question with more specific details.",
                    "Check if there are related tickets or wiki pages that might contain the information.",
                    "Consider escalating to the relevant engineering team for assistance.",
                }.AsReadOnly();
            }
        }

        // Step 8: Map citations from chunk IDs to CitationDto (only cited chunks, capped).
        var maxCitations = request.MaxCitations ?? _settings.MaxCitations;
        var citations = MapCitations(modelResponse.Citations, evidenceChunks, maxCitations);

        // Step 9: Build escalation signal.
        EscalationSignal? escalation = modelResponse.Escalation.Recommended
            ? new EscalationSignal
            {
                Recommended = true,
                TargetTeam = modelResponse.Escalation.TargetTeam,
                Reason = modelResponse.Escalation.Reason,
                HandoffNote = modelResponse.Escalation.HandoffNote,
            }
            : null;

        sw.Stop();

        // P0-022: Record SLO metrics.
        DiagnosticsHelper.ChatLatencyMs.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId),
            new KeyValuePair<string, object?>("smartkb.response_type", responseType));
        DiagnosticsHelper.ChatRequestsTotal.Add(1,
            new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId),
            new KeyValuePair<string, object?>("smartkb.response_type", responseType));
        DiagnosticsHelper.ChatConfidence.Record(blendedConfidence,
            new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
        if (piiRedactedCount > 0)
            DiagnosticsHelper.PiiRedactionsTotal.Add(piiRedactedCount,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
        if (restrictedRemovedCount > 0)
            DiagnosticsHelper.RestrictedContentBlockedTotal.Add(restrictedRemovedCount,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));

        orchestrationActivity?.SetTag("smartkb.response_type", responseType);
        orchestrationActivity?.SetTag("smartkb.blended_confidence", blendedConfidence);
        orchestrationActivity?.SetTag("smartkb.citation_count", citations.Count);
        orchestrationActivity?.SetTag("smartkb.duration_ms", sw.ElapsedMilliseconds);
        orchestrationActivity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation(
            "Chat orchestration complete. TraceId={TraceId}, ResponseType={ResponseType}, " +
            "BlendedConfidence={Confidence:F3}, ModelConfidence={ModelConfidence:F3}, " +
            "RetrievalConfidence={RetrievalConfidence:F3}, CitationCount={CitationCount}, " +
            "EscalationRecommended={EscalationRecommended}, DurationMs={DurationMs}",
            traceId, responseType, blendedConfidence, modelResponse.Confidence,
            retrievalConfidence, citations.Count, escalation?.Recommended ?? false, sw.ElapsedMilliseconds);

        // Step 10: Persist evidence-to-answer trace links (non-fatal on failure).
        try
        {
            await _traceWriter.WriteTraceAsync(
                Guid.NewGuid(), tenantId, userId, correlationId, request.Query,
                responseType, blendedConfidence, confidenceLabel,
                citations.Select(c => c.ChunkId).ToList(),
                evidenceChunks.Select(c => c.ChunkId).ToList(),
                retrievalResult.AclFilteredOutCount, true, escalation?.Recommended ?? false,
                _settings.SystemPromptVersion, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist answer trace. TraceId={TraceId}", traceId);
        }

        // Step 11 (P2-003): Record token usage and cost metrics.
        try
        {
            var embeddingTokens = embeddingCacheHit ? 0 : EstimateTokens(request.Query);
            var estimatedCost = ComputeEstimatedCost(
                callResult.PromptTokens, callResult.CompletionTokens, embeddingTokens);

            var usageRecord = new TokenUsageRecord
            {
                PromptTokens = callResult.PromptTokens,
                CompletionTokens = callResult.CompletionTokens,
                TotalTokens = callResult.TotalTokens,
                EmbeddingTokens = embeddingTokens,
                EmbeddingCacheHit = embeddingCacheHit,
                EvidenceChunksUsed = evidenceChunks.Count,
                EstimatedCostUsd = estimatedCost,
            };

            await _tokenUsageService.RecordUsageAsync(tenantId, userId, correlationId, usageRecord, cancellationToken);

            DiagnosticsHelper.PromptTokensUsed.Record(callResult.PromptTokens,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
            DiagnosticsHelper.CompletionTokensUsed.Record(callResult.CompletionTokens,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
            DiagnosticsHelper.EstimatedCostUsd.Record((double)estimatedCost,
                new KeyValuePair<string, object?>("smartkb.tenant_id", tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record token usage. TraceId={TraceId}", traceId);
        }

        return new ChatResponse
        {
            ResponseType = responseType,
            Answer = answer,
            Citations = citations,
            Confidence = blendedConfidence,
            ConfidenceLabel = confidenceLabel,
            NextSteps = nextSteps,
            Escalation = escalation,
            TraceId = traceId,
            HasEvidence = true,
            SystemPromptVersion = _settings.SystemPromptVersion,
            PiiRedactedCount = piiRedactedCount,
        };
    }

    /// <summary>Builds the system prompt with evidence context.</summary>
    internal static string BuildSystemPrompt(IReadOnlyList<RetrievedChunk> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an intelligent support copilot for an enterprise support team. " +
            "Your job is to provide grounded, accurate answers based exclusively on the evidence provided below.");
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("1. ONLY use information from the provided evidence chunks to answer questions. " +
            "Do not use your training data for factual claims about the user's systems, products, or incidents.");
        sb.AppendLine("2. For every factual claim, cite the evidence chunk(s) that support it using their chunk IDs in the citations array.");
        sb.AppendLine("3. If the evidence is insufficient to answer confidently, set response_type to \"next_steps_only\" " +
            "and provide diagnostic steps the support agent can take.");
        sb.AppendLine("4. If the situation appears to require escalation (high severity, complex cross-team issue, " +
            "or insufficient evidence for a critical problem), set response_type to \"escalate\" and fill in the escalation fields.");
        sb.AppendLine("5. Report your confidence honestly as a value between 0.0 and 1.0 based on how well the evidence supports your answer.");
        sb.AppendLine("6. Always include at least one next step suggestion, even for high-confidence answers.");
        sb.AppendLine("7. Never fabricate information. If you are unsure, say so explicitly.");
        sb.AppendLine();
        sb.AppendLine("## Evidence Chunks");

        foreach (var chunk in chunks)
        {
            sb.AppendLine($"### [{chunk.ChunkId}] {chunk.Title}");
            sb.AppendLine($"Source: {chunk.SourceSystem}/{chunk.SourceType} — {chunk.SourceUrl}");
            sb.AppendLine($"Updated: {chunk.UpdatedAt:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(chunk.ProductArea))
                sb.AppendLine($"Product Area: {chunk.ProductArea}");
            sb.AppendLine();
            sb.AppendLine(chunk.ChunkText);
            if (!string.IsNullOrEmpty(chunk.ChunkContext))
            {
                sb.AppendLine();
                sb.AppendLine($"Context: {chunk.ChunkContext}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Assembles the message list for the OpenAI API call, applying D-010 token budget.
    /// Trims oldest session messages first when budget is exceeded.
    /// </summary>
    internal List<object> AssembleMessages(
        string systemPrompt,
        IReadOnlyList<ChatMessage> sessionHistory,
        string currentQuery)
    {
        var messages = new List<object>();

        // System prompt is always included.
        var systemTokens = EstimateTokens(systemPrompt);
        var queryTokens = EstimateTokens(currentQuery);

        // Budget available for session history.
        var budgetForHistory = _settings.MaxTokenBudget
            - _settings.SystemPromptTokenReserve
            - systemTokens
            - queryTokens
            - _settings.MaxResponseTokens;

        messages.Add(new { role = "system", content = systemPrompt });

        // Add session history from oldest to newest, trimming oldest first if over budget.
        if (sessionHistory.Count > 0 && budgetForHistory > 0)
        {
            var historyTokens = sessionHistory
                .Select(m => (message: m, tokens: EstimateTokens(m.Content)))
                .ToList();

            var totalHistoryTokens = historyTokens.Sum(h => h.tokens);
            var startIndex = 0;

            // Drop oldest messages until we fit within budget.
            while (totalHistoryTokens > budgetForHistory && startIndex < historyTokens.Count)
            {
                totalHistoryTokens -= historyTokens[startIndex].tokens;
                startIndex++;
            }

            for (var i = startIndex; i < historyTokens.Count; i++)
            {
                var m = historyTokens[i].message;
                messages.Add(new { role = m.Role, content = m.Content });
            }

            if (startIndex > 0)
            {
                _logger.LogInformation(
                    "Token budget: dropped {DroppedCount} oldest session messages to fit within {Budget} token budget",
                    startIndex, _settings.MaxTokenBudget);
            }
        }

        // Current user query always included.
        messages.Add(new { role = "user", content = currentQuery });

        return messages;
    }

    /// <summary>Result of an OpenAI structured call including token usage (P2-003).</summary>
    internal sealed record OpenAiCallResult(
        OpenAiStructuredResponse? Response,
        int PromptTokens,
        int CompletionTokens,
        int TotalTokens);

    /// <summary>Calls OpenAI Chat Completions with structured output (json_schema response_format).</summary>
    internal async Task<OpenAiCallResult> CallOpenAiStructuredAsync(
        List<object> messages,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("OpenAi");

        var requestBody = new
        {
            model = _openAiSettings.Model,
            messages,
            max_tokens = _settings.MaxResponseTokens,
            temperature = 0.2,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "grounded_answer",
                    strict = true,
                    schema = StructuredOutputSchema,
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_openAiSettings.Endpoint}/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {_openAiSettings.ApiKey}");
        request.Content = JsonContent.Create(requestBody);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI Chat API error {StatusCode}: {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"OpenAI Chat API returned {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        // P2-003: Extract token usage from API response.
        int promptTokens = 0, completionTokens = 0, totalTokens = 0;
        if (responseJson.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct2)) completionTokens = ct2.GetInt32();
            if (usage.TryGetProperty("total_tokens", out var tt)) totalTokens = tt.GetInt32();
        }

        // Extract the content from choices[0].message.content.
        if (responseJson.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            var messageContent = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (!string.IsNullOrEmpty(messageContent))
            {
                var parsed = JsonSerializer.Deserialize<OpenAiStructuredResponse>(messageContent, JsonOptions);
                return new OpenAiCallResult(parsed, promptTokens, completionTokens, totalTokens);
            }
        }

        return new OpenAiCallResult(null, promptTokens, completionTokens, totalTokens);
    }

    /// <summary>
    /// Computes retrieval confidence heuristic based on chunk scores and count (D-003).
    /// Normalized: average RRF score of returned chunks * saturation factor.
    /// </summary>
    internal static float ComputeRetrievalConfidence(IReadOnlyList<RetrievedChunk> chunks)
    {
        if (chunks.Count == 0) return 0f;

        var avgScore = (float)chunks.Average(c => c.RrfScore);
        // Saturation: reaches ~1.0 when we have 5+ relevant chunks, diminishing returns after that.
        var saturation = Math.Min(1f, chunks.Count / 5f);
        return Math.Clamp(avgScore * saturation, 0f, 1f);
    }

    /// <summary>Maps cited chunk IDs to CitationDto with metadata from retrieved chunks.</summary>
    internal static IReadOnlyList<CitationDto> MapCitations(
        IReadOnlyList<string> citedChunkIds,
        IReadOnlyList<RetrievedChunk> chunks,
        int maxCitations)
    {
        var chunkLookup = chunks.ToDictionary(c => c.ChunkId, StringComparer.OrdinalIgnoreCase);
        var citations = new List<CitationDto>();

        foreach (var chunkId in citedChunkIds)
        {
            if (citations.Count >= maxCitations) break;

            if (chunkLookup.TryGetValue(chunkId, out var chunk))
            {
                citations.Add(new CitationDto
                {
                    ChunkId = chunk.ChunkId,
                    EvidenceId = chunk.EvidenceId,
                    Title = chunk.Title,
                    SourceUrl = chunk.SourceUrl,
                    SourceSystem = chunk.SourceSystem,
                    Snippet = chunk.ChunkText.Length > 200
                        ? chunk.ChunkText[..200] + "..."
                        : chunk.ChunkText,
                    UpdatedAt = chunk.UpdatedAt,
                    AccessLabel = chunk.AccessLabel,
                });
            }
        }

        return citations;
    }

    /// <summary>
    /// P0-014: Defense-in-depth ACL enforcement. Removes any Restricted chunks where the user
    /// is not in the allowed groups. This should never find anything (retrieval already filters),
    /// but guarantees restricted content never reaches the model even if retrieval has a bug.
    /// </summary>
    internal static (IReadOnlyList<RetrievedChunk> Safe, int RemovedCount) EnforceRestrictedContentExclusion(
        IReadOnlyList<RetrievedChunk> chunks,
        IReadOnlyList<string>? userGroups)
    {
        var groupSet = userGroups is { Count: > 0 }
            ? new HashSet<string>(userGroups, StringComparer.OrdinalIgnoreCase)
            : null;

        var safe = new List<RetrievedChunk>();
        var removed = 0;

        foreach (var chunk in chunks)
        {
            if (string.Equals(chunk.Visibility, "Restricted", StringComparison.OrdinalIgnoreCase))
            {
                // Restricted: user must be in at least one allowed group.
                if (groupSet is not null && chunk.AllowedGroups.Any(g => groupSet.Contains(g)))
                {
                    safe.Add(chunk);
                }
                else
                {
                    removed++;
                }
            }
            else
            {
                // Public and Internal: always safe for authenticated users.
                safe.Add(chunk);
            }
        }

        return (safe, removed);
    }

    /// <summary>
    /// P0-014A: Redacts PII in retrieved chunk text before model context assembly.
    /// Returns new chunk instances with redacted text and a count of chunks that had PII redacted.
    /// </summary>
    internal static (IReadOnlyList<RetrievedChunk> Redacted, int RedactedChunkCount) RedactPiiInChunks(
        IReadOnlyList<RetrievedChunk> chunks,
        IPiiRedactionService piiRedactionService)
    {
        var result = new List<RetrievedChunk>(chunks.Count);
        var redactedCount = 0;

        foreach (var chunk in chunks)
        {
            var textResult = piiRedactionService.Redact(chunk.ChunkText);
            var contextResult = !string.IsNullOrEmpty(chunk.ChunkContext)
                ? piiRedactionService.Redact(chunk.ChunkContext)
                : null;

            if (textResult.TotalRedactions > 0 || (contextResult?.TotalRedactions ?? 0) > 0)
            {
                redactedCount++;
                result.Add(chunk with
                {
                    ChunkText = textResult.RedactedText,
                    ChunkContext = contextResult?.RedactedText ?? chunk.ChunkContext,
                });
            }
            else
            {
                result.Add(chunk);
            }
        }

        return (result, redactedCount);
    }

    /// <summary>
    /// P2-001: Policy-aware PII redaction in chunks. Respects tenant PII policy configuration.
    /// Returns redacted chunks, count, aggregated redaction counts by type, and affected chunk IDs.
    /// </summary>
    internal static (IReadOnlyList<RetrievedChunk> Redacted, int RedactedChunkCount, Dictionary<string, int> AggregatedCounts, List<string> AffectedChunkIds) RedactPiiInChunksWithPolicy(
        IReadOnlyList<RetrievedChunk> chunks,
        IPiiRedactionService piiRedactionService,
        PiiPolicyResponse policy)
    {
        var result = new List<RetrievedChunk>(chunks.Count);
        var redactedCount = 0;
        var aggregatedCounts = new Dictionary<string, int>();
        var affectedChunkIds = new List<string>();

        foreach (var chunk in chunks)
        {
            var textResult = piiRedactionService.Redact(chunk.ChunkText, policy);
            var contextResult = !string.IsNullOrEmpty(chunk.ChunkContext)
                ? piiRedactionService.Redact(chunk.ChunkContext, policy)
                : null;

            if (textResult.TotalRedactions > 0 || (contextResult?.TotalRedactions ?? 0) > 0)
            {
                redactedCount++;
                affectedChunkIds.Add(chunk.ChunkId);

                // Aggregate counts.
                foreach (var kvp in textResult.RedactionCounts)
                    aggregatedCounts[kvp.Key] = aggregatedCounts.GetValueOrDefault(kvp.Key) + kvp.Value;
                if (contextResult is not null)
                    foreach (var kvp in contextResult.RedactionCounts)
                        aggregatedCounts[kvp.Key] = aggregatedCounts.GetValueOrDefault(kvp.Key) + kvp.Value;

                result.Add(chunk with
                {
                    ChunkText = textResult.RedactedText,
                    ChunkContext = contextResult?.RedactedText ?? chunk.ChunkContext,
                });
            }
            else
            {
                result.Add(chunk);
            }
        }

        return (result, redactedCount, aggregatedCounts, affectedChunkIds);
    }

    /// <summary>Approximate token count using chars/4 heuristic for English text.</summary>
    internal static int EstimateTokens(string text) => (text.Length + 3) / 4;

    /// <summary>Estimates cost in USD based on token usage and configured pricing (P2-003).</summary>
    internal decimal ComputeEstimatedCost(int promptTokens, int completionTokens, int embeddingTokens)
    {
        var promptCost = promptTokens * _costSettings.PromptTokenCostPerMillion / 1_000_000m;
        var completionCost = completionTokens * _costSettings.CompletionTokenCostPerMillion / 1_000_000m;
        var embeddingCost = embeddingTokens * _costSettings.EmbeddingTokenCostPerMillion / 1_000_000m;
        return promptCost + completionCost + embeddingCost;
    }

    private ChatResponse BuildNoEvidenceResponse(string traceId, string? customMessage = null)
    {
        return new ChatResponse
        {
            ResponseType = "next_steps_only",
            Answer = customMessage ?? "I don't have enough information in the knowledge base to answer this question confidently.",
            Citations = [],
            Confidence = 0f,
            ConfidenceLabel = "Low",
            NextSteps =
            [
                "Try rephrasing your question with more specific keywords or error messages.",
                "Check if there are related tickets or wiki pages that might contain the information you need.",
                "If this is a recurring issue, consider creating a knowledge base article for it.",
                "Escalate to the relevant engineering team if the issue is blocking a customer.",
            ],
            Escalation = new EscalationSignal
            {
                Recommended = true,
                TargetTeam = "Engineering",
                Reason = "Insufficient evidence in knowledge base to provide a grounded answer.",
                HandoffNote = string.Empty,
            },
            TraceId = traceId,
            HasEvidence = false,
            SystemPromptVersion = _settings.SystemPromptVersion,
        };
    }

    /// <summary>
    /// JSON Schema for OpenAI structured output (strict mode).
    /// All properties required, additionalProperties false — per OpenAI strict schema requirements.
    /// </summary>
    internal static readonly object StructuredOutputSchema = new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["response_type"] = new
            {
                type = "string",
                @enum = new[] { "final_answer", "next_steps_only", "escalate" },
            },
            ["answer"] = new { type = "string" },
            ["citations"] = new
            {
                type = "array",
                items = new { type = "string" },
            },
            ["confidence"] = new { type = "number" },
            ["confidence_rationale"] = new { type = "string" },
            ["next_steps"] = new
            {
                type = "array",
                items = new { type = "string" },
            },
            ["escalation"] = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["recommended"] = new { type = "boolean" },
                    ["target_team"] = new { type = "string" },
                    ["reason"] = new { type = "string" },
                    ["handoff_note"] = new { type = "string" },
                },
                required = new[] { "recommended", "target_team", "reason", "handoff_note" },
                additionalProperties = false,
            },
        },
        required = new[]
        {
            "response_type", "answer", "citations", "confidence",
            "confidence_rationale", "next_steps", "escalation",
        },
        additionalProperties = false,
    };
}
