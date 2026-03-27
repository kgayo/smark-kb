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

public sealed class SessionService : ISessionService
{
    private readonly SmartKbDbContext _db;
    private readonly SessionSettings _sessionSettings;
    private readonly IChatOrchestrator? _chatOrchestrator;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        SmartKbDbContext db,
        SessionSettings sessionSettings,
        ILogger<SessionService> logger,
        IChatOrchestrator? chatOrchestrator = null)
    {
        _db = db;
        _sessionSettings = sessionSettings;
        _logger = logger;
        _chatOrchestrator = chatOrchestrator;
    }

    public async Task<SessionResponse> CreateSessionAsync(
        string tenantId, string userId, CreateSessionRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Title = request.Title,
            CustomerRef = request.CustomerRef,
            CreatedAt = now,
            CreatedAtEpoch = now.ToUnixTimeSeconds(),
            UpdatedAt = now,
            ExpiresAt = _sessionSettings.HasExpiry
                ? now.AddHours(_sessionSettings.DefaultExpiryHours)
                : null,
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Session created. SessionId={SessionId}, TenantId={TenantId}, UserId={UserId}",
            session.Id, tenantId, userId);

        return MapSession(session, 0);
    }

    public async Task<SessionListResponse> ListSessionsAsync(
        string tenantId, string userId, CancellationToken ct = default)
    {
        var sessions = await _db.Sessions
            .Where(s => s.TenantId == tenantId && s.UserId == userId)
            .Select(s => new
            {
                Session = s,
                MessageCount = s.Messages.Count(m => m.DeletedAt == null),
            })
            .ToListAsync(ct);

        var responses = sessions
            .OrderByDescending(s => s.Session.UpdatedAt)
            .Select(s => MapSession(s.Session, s.MessageCount))
            .ToList();

        return new SessionListResponse
        {
            Sessions = responses,
            TotalCount = responses.Count,
        };
    }

    public async Task<SessionResponse?> GetSessionAsync(
        string tenantId, string userId, Guid sessionId, CancellationToken ct = default)
    {
        var result = await _db.Sessions
            .Where(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId)
            .Select(s => new
            {
                Session = s,
                MessageCount = s.Messages.Count(m => m.DeletedAt == null),
            })
            .FirstOrDefaultAsync(ct);

        return result is null ? null : MapSession(result.Session, result.MessageCount);
    }

    public async Task<bool> DeleteSessionAsync(
        string tenantId, string userId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (session is null) return false;

        session.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Session soft-deleted. SessionId={SessionId}, TenantId={TenantId}",
            sessionId, tenantId);

        return true;
    }

    public async Task<MessageListResponse?> GetMessagesAsync(
        string tenantId, string userId, Guid sessionId, CancellationToken ct = default)
    {
        var sessionExists = await _db.Sessions
            .AnyAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (!sessionExists) return null;

        var messages = await _db.Messages
            .Where(m => m.SessionId == sessionId && m.TenantId == tenantId)
            .ToListAsync(ct);

        var responses = messages
            .OrderBy(m => m.CreatedAt)
            .Select(MapMessage)
            .ToList();

        return new MessageListResponse
        {
            SessionId = sessionId,
            Messages = responses,
            TotalCount = responses.Count,
        };
    }

    public async Task<SessionChatResponse?> SendMessageAsync(
        string tenantId, string userId, string correlationId,
        Guid sessionId, SendMessageRequest request, CancellationToken ct = default)
    {
        // Validate session ownership and existence.
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (session is null) return null;

        // Check session expiry.
        if (session.ExpiresAt.HasValue && session.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            _logger.LogInformation(
                "Session expired. SessionId={SessionId}, ExpiresAt={ExpiresAt}",
                sessionId, session.ExpiresAt);
            return null;
        }

        // Check message limit.
        if (_sessionSettings.MaxMessagesPerSession > 0)
        {
            var messageCount = await _db.Messages
                .CountAsync(m => m.SessionId == sessionId && m.TenantId == tenantId, ct);

            if (messageCount >= _sessionSettings.MaxMessagesPerSession)
            {
                _logger.LogWarning(
                    "Session message limit reached. SessionId={SessionId}, Limit={Limit}",
                    sessionId, _sessionSettings.MaxMessagesPerSession);
                return null;
            }
        }

        var now = DateTimeOffset.UtcNow;

        // Persist user message.
        var userMessage = new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TenantId = tenantId,
            Role = MessageRole.User,
            Content = request.Query,
            CorrelationId = correlationId,
            CreatedAt = now,
            CreatedAtEpoch = now.ToUnixTimeSeconds(),
        };
        _db.Messages.Add(userMessage);

        // Build session history from prior messages for context.
        var priorMessageEntities = await _db.Messages
            .Where(m => m.SessionId == sessionId && m.TenantId == tenantId)
            .ToListAsync(ct);

        var priorMessages = priorMessageEntities
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessage
            {
                Role = m.Role == MessageRole.User ? MessageRoleName.User : MessageRoleName.Assistant,
                Content = m.Content,
            })
            .ToList();

        // Orchestrate chat response.
        ChatResponse chatResponse;
        if (_chatOrchestrator is not null)
        {
            var chatRequest = new ChatRequest
            {
                Query = request.Query,
                SessionHistory = priorMessages,
                UserGroups = request.UserGroups,
                MaxCitations = request.MaxCitations,
                Filters = request.Filters,
            };
            chatResponse = await _chatOrchestrator.OrchestrateAsync(
                tenantId, userId, correlationId, chatRequest, ct);
        }
        else
        {
            chatResponse = new ChatResponse
            {
                ResponseType = ChatResponseType.NextStepsOnly,
                Answer = "Chat orchestration is not configured.",
                Citations = [],
                Confidence = 0f,
                ConfidenceLabel = ConfidenceLabel.Low,
                ConfidenceRationale = "Chat orchestration is not configured.",
                NextSteps = ["Ensure OpenAI and Search Service are configured."],
                TraceId = correlationId,
                HasEvidence = false,
                SystemPromptVersion = ChatOrchestrationSettings.DefaultSystemPromptVersion,
            };
        }

        // Persist assistant message with citations.
        var assistantMessage = new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TenantId = tenantId,
            Role = MessageRole.Assistant,
            Content = chatResponse.Answer,
            CitationsJson = chatResponse.Citations.Count > 0
                ? JsonSerializer.Serialize(chatResponse.Citations, SharedJsonOptions.CamelCaseWrite)
                : null,
            Confidence = chatResponse.Confidence,
            ConfidenceLabel = chatResponse.ConfidenceLabel,
            ConfidenceRationale = chatResponse.ConfidenceRationale,
            ResponseType = chatResponse.ResponseType,
            TraceId = chatResponse.TraceId,
            CorrelationId = correlationId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.Messages.Add(assistantMessage);

        // Update session metadata.
        session.UpdatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(session.Title))
        {
            // Auto-title from first user query (truncated).
            session.Title = request.Query.Truncate(TruncationLimits.SessionTitle, "...");
        }

        // Extend expiry on activity.
        if (_sessionSettings.HasExpiry)
        {
            session.ExpiresAt = DateTimeOffset.UtcNow.AddHours(_sessionSettings.DefaultExpiryHours);
        }

        await _db.SaveChangesAsync(ct);

        var totalMessages = await _db.Messages
            .CountAsync(m => m.SessionId == sessionId && m.TenantId == tenantId, ct);

        return new SessionChatResponse
        {
            Session = MapSession(session, totalMessages),
            UserMessage = MapMessage(userMessage),
            AssistantMessage = MapMessage(assistantMessage),
            ChatResponse = chatResponse,
        };
    }

    private static SessionResponse MapSession(SessionEntity entity, int messageCount) => new()
    {
        SessionId = entity.Id,
        TenantId = entity.TenantId,
        UserId = entity.UserId,
        Title = entity.Title,
        CustomerRef = entity.CustomerRef,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        ExpiresAt = entity.ExpiresAt,
        MessageCount = messageCount,
    };

    private static MessageResponse MapMessage(MessageEntity entity) => new()
    {
        MessageId = entity.Id,
        SessionId = entity.SessionId,
        Role = entity.Role.ToString(),
        Content = entity.Content,
        Citations = DeserializeCitations(entity.CitationsJson),
        Confidence = entity.Confidence,
        ConfidenceLabel = entity.ConfidenceLabel,
        ConfidenceRationale = entity.ConfidenceRationale,
        ResponseType = entity.ResponseType,
        TraceId = entity.TraceId,
        CorrelationId = entity.CorrelationId,
        CreatedAt = entity.CreatedAt,
    };

    private static IReadOnlyList<CitationDto>? DeserializeCitations(string? json, ILogger? logger = null) =>
        JsonDeserializeHelper.DeserializeOrNull<List<CitationDto>>(json, SharedJsonOptions.CamelCaseWrite, logger);
}
