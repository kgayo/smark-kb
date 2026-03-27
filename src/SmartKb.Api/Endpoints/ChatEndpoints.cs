using Microsoft.AspNetCore.Mvc;
using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using Microsoft.EntityFrameworkCore;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class ChatEndpoints
{
    public static WebApplication MapChatEndpoints(this WebApplication app)
    {
        // --- Session Endpoints ---

        app.MapPost("/api/sessions", async (
            HttpContext httpContext,
            CreateSessionRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISessionService sessionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await sessionService.CreateSessionAsync(tenant.TenantId, tenant.UserId, request, ct);
            return Results.Created($"/api/sessions/{response.SessionId}",
                ApiResponse<SessionResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapGet("/api/sessions", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISessionService sessionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await sessionService.ListSessionsAsync(tenant.TenantId, tenant.UserId, ct);
            return Results.Ok(ApiResponse<SessionListResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapGet("/api/sessions/{sessionId:guid}", async (
            HttpContext httpContext,
            Guid sessionId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISessionService sessionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await sessionService.GetSessionAsync(tenant.TenantId, tenant.UserId, sessionId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SessionNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<SessionResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapDelete("/api/sessions/{sessionId:guid}", async (
            HttpContext httpContext,
            Guid sessionId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISessionService sessionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await sessionService.DeleteSessionAsync(tenant.TenantId, tenant.UserId, sessionId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SessionNotFound, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapGet("/api/sessions/{sessionId:guid}/messages", async (
            HttpContext httpContext,
            Guid sessionId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISessionService sessionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await sessionService.GetMessagesAsync(tenant.TenantId, tenant.UserId, sessionId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SessionNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<MessageListResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapPost("/api/sessions/{sessionId:guid}/messages", async (
            HttpContext httpContext,
            Guid sessionId,
            SendMessageRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISessionService sessionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            // P0-014: Inject JWT-extracted user groups for ACL enforcement.
            var effectiveRequest = request with
            {
                UserGroups = tenant.UserGroups.Count > 0 ? tenant.UserGroups : request.UserGroups,
            };
            var response = await sessionService.SendMessageAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, sessionId, effectiveRequest, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SessionNotFoundOrExpired, tenant.CorrelationId))
                : Results.Ok(ApiResponse<SessionChatResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        // --- Feedback Endpoints ---

        app.MapPost("/api/sessions/{sessionId:guid}/messages/{messageId:guid}/feedback", async (
            HttpContext httpContext,
            Guid sessionId,
            Guid messageId,
            SubmitFeedbackRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IFeedbackService feedbackService,
            [FromServices] ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            try
            {
                var response = await feedbackService.SubmitFeedbackAsync(
                    tenant.TenantId, tenant.UserId, tenant.CorrelationId,
                    sessionId, messageId, request, ct);
                return Results.Ok(ApiResponse<FeedbackResponse>.Success(response, tenant.CorrelationId));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Feedback submission failed for session {SessionId}, message {MessageId}", sessionId, messageId);
                return Results.UnprocessableEntity(
                    ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.ChatFeedback);

        app.MapGet("/api/sessions/{sessionId:guid}/messages/{messageId:guid}/feedback", async (
            HttpContext httpContext,
            Guid sessionId,
            Guid messageId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IFeedbackService feedbackService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await feedbackService.GetFeedbackAsync(
                tenant.TenantId, tenant.UserId, sessionId, messageId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.FeedbackNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<FeedbackResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatFeedback);

        app.MapGet("/api/sessions/{sessionId:guid}/feedbacks", async (
            HttpContext httpContext,
            Guid sessionId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IFeedbackService feedbackService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await feedbackService.ListFeedbacksAsync(
                tenant.TenantId, tenant.UserId, sessionId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SessionNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<FeedbackListResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatFeedback);

        // --- Outcome Endpoints ---

        app.MapPost("/api/sessions/{sessionId:guid}/outcome", async (
            HttpContext httpContext,
            Guid sessionId,
            RecordOutcomeRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IOutcomeService outcomeService,
            [FromServices] ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            try
            {
                var response = await outcomeService.RecordOutcomeAsync(
                    tenant.TenantId, tenant.UserId, tenant.CorrelationId,
                    sessionId, request, ct);
                return Results.Created($"/api/sessions/{sessionId}/outcome",
                    ApiResponse<OutcomeResponse>.Success(response, tenant.CorrelationId));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Outcome recording failed for session {SessionId}", sessionId);
                return Results.UnprocessableEntity(
                    ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.ChatOutcome);

        app.MapGet("/api/sessions/{sessionId:guid}/outcome", async (
            HttpContext httpContext,
            Guid sessionId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IOutcomeService outcomeService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await outcomeService.GetOutcomesAsync(
                tenant.TenantId, tenant.UserId, sessionId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SessionNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<OutcomeListResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatOutcome);

        // --- Escalation Draft Endpoints ---

        app.MapPost("/api/escalations/draft", async (
            HttpContext httpContext,
            CreateEscalationDraftRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IEscalationDraftService escalationService,
            [FromServices] ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            try
            {
                var response = await escalationService.CreateDraftAsync(
                    tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, ct);
                return Results.Created($"/api/escalations/draft/{response.DraftId}",
                    ApiResponse<EscalationDraftResponse>.Success(response, tenant.CorrelationId));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Escalation draft creation failed");
                return Results.UnprocessableEntity(
                    ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.ChatQuery);

        app.MapGet("/api/escalations/draft/{draftId:guid}", async (
            HttpContext httpContext,
            Guid draftId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IEscalationDraftService escalationService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await escalationService.GetDraftAsync(tenant.TenantId, tenant.UserId, draftId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.EscalationDraftNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<EscalationDraftResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapGet("/api/sessions/{sessionId:guid}/escalations/drafts", async (
            HttpContext httpContext,
            Guid sessionId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IEscalationDraftService escalationService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await escalationService.ListDraftsAsync(tenant.TenantId, tenant.UserId, sessionId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SessionNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<EscalationDraftListResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapPut("/api/escalations/draft/{draftId:guid}", async (
            HttpContext httpContext,
            Guid draftId,
            UpdateEscalationDraftRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IEscalationDraftService escalationService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (response, notFound) = await escalationService.UpdateDraftAsync(
                tenant.TenantId, tenant.UserId, draftId, request, ct);
            if (notFound)
                return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.EscalationDraftNotFound, tenant.CorrelationId));
            if (response is null)
                return Results.Problem("Unexpected null response from service.", statusCode: StatusCodes.Status500InternalServerError);

            return Results.Ok(ApiResponse<EscalationDraftResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapGet("/api/escalations/draft/{draftId:guid}/export", async (
            HttpContext httpContext,
            Guid draftId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IEscalationDraftService escalationService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await escalationService.ExportDraftAsMarkdownAsync(
                tenant.TenantId, tenant.UserId, draftId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.EscalationDraftNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<EscalationDraftExportResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        app.MapPost("/api/escalations/draft/{draftId:guid}/approve", async (
            HttpContext httpContext,
            Guid draftId,
            ApproveEscalationDraftRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IEscalationDraftService escalationService,
            [FromServices] ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            try
            {
                var result = await escalationService.ApproveAndCreateExternalAsync(
                    tenant.TenantId, tenant.UserId, tenant.CorrelationId, draftId, request, ct);
                if (result is null)
                    return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.EscalationDraftNotFound, tenant.CorrelationId));

                return result.ExternalStatus == EscalationExternalStatus.Created
                    ? Results.Ok(ApiResponse<ExternalEscalationResult>.Success(result, tenant.CorrelationId))
                    : Results.UnprocessableEntity(ApiResponse<ExternalEscalationResult>.Success(result, tenant.CorrelationId));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "External escalation creation failed for draft {DraftId}", draftId);
                return Results.UnprocessableEntity(
                    ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.ChatQuery);

        app.MapDelete("/api/escalations/draft/{draftId:guid}", async (
            HttpContext httpContext,
            Guid draftId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IEscalationDraftService escalationService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await escalationService.DeleteDraftAsync(tenant.TenantId, tenant.UserId, draftId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.EscalationDraftNotFound, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        // --- Evidence Content Endpoint (P3-025: Source Viewer drill-down) ---

        app.MapGet("/api/evidence/{chunkId}/content", async (
            string chunkId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] SmartKb.Data.SmartKbDbContext db,
            HttpContext httpContext,
            [FromServices] ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();

            var chunk = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.EvidenceChunks.AsNoTracking(),
                    c => c.ChunkId == chunkId && c.TenantId == tenant.TenantId, ct);

            if (chunk is null)
                return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.EvidenceChunkNotFound, tenant.CorrelationId));

            // ACL enforcement: restricted content requires user membership in allowed groups.
            if (string.Equals(chunk.Visibility, VisibilityLevel.Restricted, StringComparison.OrdinalIgnoreCase))
            {
                string[] allowedGroups;
                try
                {
                    allowedGroups = string.IsNullOrEmpty(chunk.AllowedGroups)
                        ? Array.Empty<string>()
                        : System.Text.Json.JsonSerializer.Deserialize<string[]>(chunk.AllowedGroups) ?? Array.Empty<string>();
                }
                catch (System.Text.Json.JsonException ex)
                {
                    logger.LogWarning(ex, "Malformed AllowedGroups JSON for chunk {ChunkId}, treating as empty", chunkId);
                    allowedGroups = Array.Empty<string>();
                }
                var userGroups = tenant.UserGroups;
                var hasAccess = allowedGroups.Any(ag => userGroups.Any(ug =>
                    string.Equals(ag, ug, StringComparison.OrdinalIgnoreCase)));
                if (!hasAccess)
                    return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.EvidenceChunkNotFound, tenant.CorrelationId));
            }

            // Attempt to load raw content from blob storage if available.
            string? rawContent = null;
            string? contentType = null;
            var snapshot = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.RawContentSnapshots.AsNoTracking(),
                    s => s.EvidenceId == chunk.EvidenceId && s.TenantId == tenant.TenantId, ct);

            if (snapshot is not null)
            {
                contentType = snapshot.ContentType;
                var blobService = httpContext.RequestServices.GetService<SmartKb.Contracts.Services.IBlobStorageService>();
                if (blobService is not null)
                {
                    try
                    {
                        rawContent = await blobService.DownloadRawContentAsync(snapshot.BlobPath, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Non-fatal: raw content is optional enhancement over chunk text.
                        logger.LogWarning(ex, "Failed to download raw blob content for snapshot {BlobPath}", snapshot.BlobPath);
                    }
                }
            }

            string[] tags;
            try
            {
                tags = string.IsNullOrEmpty(chunk.Tags)
                    ? Array.Empty<string>()
                    : System.Text.Json.JsonSerializer.Deserialize<string[]>(chunk.Tags) ?? Array.Empty<string>();
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogWarning(ex, "Malformed Tags JSON for chunk {ChunkId}, treating as empty", chunkId);
                tags = Array.Empty<string>();
            }

            var response = new EvidenceContentResponse
            {
                ChunkId = chunk.ChunkId,
                EvidenceId = chunk.EvidenceId,
                Title = chunk.Title,
                SourceUrl = chunk.SourceUrl,
                SourceSystem = chunk.SourceSystem,
                SourceType = chunk.SourceType,
                ChunkText = chunk.ChunkText,
                ChunkContext = chunk.ChunkContext,
                RawContent = rawContent,
                ContentType = contentType,
                UpdatedAt = chunk.UpdatedAt,
                AccessLabel = chunk.AccessLabel,
                ProductArea = chunk.ProductArea,
                Tags = tags,
            };

            return Results.Ok(ApiResponse<EvidenceContentResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        // --- Chat Endpoint (stateless, kept for backward compatibility) ---

        app.MapPost("/api/chat", async (
            ChatRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var orchestrator = httpContext.RequestServices.GetService<IChatOrchestrator>();
            if (orchestrator is null)
                return Results.Json(
                    ApiResponse<object>.Failure("Chat orchestration is not configured. Ensure OpenAI and Search Service are set up.", tenant.CorrelationId),
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            // P0-014: Inject JWT-extracted user groups for ACL enforcement.
            // Merge with any groups provided in the request body (server-side groups take precedence).
            var effectiveRequest = request with
            {
                UserGroups = tenant.UserGroups.Count > 0 ? tenant.UserGroups : request.UserGroups,
            };

            var response = await orchestrator.OrchestrateAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, effectiveRequest, ct);
            return Results.Ok(ApiResponse<ChatResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ChatQuery);

        return app;
    }
}
