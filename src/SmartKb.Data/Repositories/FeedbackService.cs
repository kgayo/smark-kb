using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class FeedbackService : IFeedbackService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<FeedbackService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public FeedbackService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<FeedbackService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<FeedbackResponse> SubmitFeedbackAsync(
        string tenantId, string userId, string correlationId,
        Guid sessionId, Guid messageId,
        SubmitFeedbackRequest request, CancellationToken ct = default)
    {
        // Validate session ownership.
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (session is null)
            throw new InvalidOperationException("Session not found or not owned by current user.");

        // Validate message belongs to session.
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.SessionId == sessionId && m.TenantId == tenantId, ct);

        if (message is null)
            throw new InvalidOperationException("Message not found in session.");

        // Check for existing feedback on this message by this user (one feedback per message per user).
        var existing = await _db.Feedbacks
            .FirstOrDefaultAsync(f => f.MessageId == messageId && f.UserId == userId && f.TenantId == tenantId, ct);

        var now = DateTimeOffset.UtcNow;

        if (existing is not null)
        {
            // Update existing feedback (allows changing thumbs up/down or adding reason codes).
            existing.Type = request.Type;
            existing.ReasonCodesJson = request.ReasonCodes.Count > 0
                ? JsonSerializer.Serialize(request.ReasonCodes.Select(r => r.ToString()).ToList(), JsonOpts)
                : null;
            existing.Comment = request.Comment;
            existing.CorrectionText = request.CorrectionText;
            existing.CorrectedAnswer = request.CorrectedAnswer;
            existing.CreatedAt = now;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Feedback updated. FeedbackId={FeedbackId}, MessageId={MessageId}, Type={Type}, TenantId={TenantId}",
                existing.Id, messageId, request.Type, tenantId);

            return MapFeedback(existing);
        }

        var feedback = new FeedbackEntity
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            SessionId = sessionId,
            TenantId = tenantId,
            UserId = userId,
            Type = request.Type,
            ReasonCodesJson = request.ReasonCodes.Count > 0
                ? JsonSerializer.Serialize(request.ReasonCodes.Select(r => r.ToString()).ToList(), JsonOpts)
                : null,
            Comment = request.Comment,
            CorrectionText = request.CorrectionText,
            CorrectedAnswer = request.CorrectedAnswer,
            TraceId = message.TraceId,
            CorrelationId = correlationId,
            CreatedAt = now,
        };

        _db.Feedbacks.Add(feedback);
        await _db.SaveChangesAsync(ct);

        // Write audit event.
        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: feedback.Id.ToString(),
            EventType: AuditEventTypes.ChatFeedback,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Feedback submitted: {request.Type} on message {messageId} in session {sessionId}"), ct);

        _logger.LogInformation(
            "Feedback submitted. FeedbackId={FeedbackId}, MessageId={MessageId}, Type={Type}, TenantId={TenantId}",
            feedback.Id, messageId, request.Type, tenantId);

        return MapFeedback(feedback);
    }

    public async Task<FeedbackResponse?> GetFeedbackAsync(
        string tenantId, string userId,
        Guid sessionId, Guid messageId, CancellationToken ct = default)
    {
        // Validate session ownership.
        var sessionExists = await _db.Sessions
            .AnyAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (!sessionExists) return null;

        var feedback = await _db.Feedbacks
            .FirstOrDefaultAsync(f => f.MessageId == messageId && f.UserId == userId && f.TenantId == tenantId, ct);

        return feedback is null ? null : MapFeedback(feedback);
    }

    public async Task<FeedbackListResponse?> ListFeedbacksAsync(
        string tenantId, string userId,
        Guid sessionId, CancellationToken ct = default)
    {
        var sessionExists = await _db.Sessions
            .AnyAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (!sessionExists) return null;

        var feedbacks = await _db.Feedbacks
            .Where(f => f.SessionId == sessionId && f.TenantId == tenantId && f.UserId == userId)
            .ToListAsync(ct);

        feedbacks = feedbacks.OrderByDescending(f => f.CreatedAt).ToList();

        return new FeedbackListResponse
        {
            MessageId = sessionId, // Session-level list; MessageId field used as SessionId for context.
            Feedbacks = feedbacks.Select(MapFeedback).ToList(),
            TotalCount = feedbacks.Count,
        };
    }

    private static FeedbackResponse MapFeedback(FeedbackEntity entity) => new()
    {
        FeedbackId = entity.Id,
        MessageId = entity.MessageId,
        SessionId = entity.SessionId,
        Type = entity.Type.ToString(),
        ReasonCodes = DeserializeReasonCodes(entity.ReasonCodesJson),
        Comment = entity.Comment,
        CorrectionText = entity.CorrectionText,
        CorrectedAnswer = entity.CorrectedAnswer,
        TraceId = entity.TraceId,
        CreatedAt = entity.CreatedAt,
    };

    private static IReadOnlyList<string> DeserializeReasonCodes(string? json)
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
