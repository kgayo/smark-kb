using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface ISessionService
{
    Task<SessionResponse> CreateSessionAsync(string tenantId, string userId, CreateSessionRequest request, CancellationToken ct = default);
    Task<SessionListResponse> ListSessionsAsync(string tenantId, string userId, CancellationToken ct = default);
    Task<SessionResponse?> GetSessionAsync(string tenantId, string userId, Guid sessionId, CancellationToken ct = default);
    Task<bool> DeleteSessionAsync(string tenantId, string userId, Guid sessionId, CancellationToken ct = default);
    Task<MessageListResponse?> GetMessagesAsync(string tenantId, string userId, Guid sessionId, CancellationToken ct = default);
    Task<SessionChatResponse?> SendMessageAsync(string tenantId, string userId, string correlationId, Guid sessionId, SendMessageRequest request, CancellationToken ct = default);
}
