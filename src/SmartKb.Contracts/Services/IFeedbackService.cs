using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IFeedbackService
{
    Task<FeedbackResponse> SubmitFeedbackAsync(
        string tenantId, string userId, string correlationId,
        Guid sessionId, Guid messageId,
        SubmitFeedbackRequest request, CancellationToken ct = default);

    Task<FeedbackResponse?> GetFeedbackAsync(
        string tenantId, string userId,
        Guid sessionId, Guid messageId, CancellationToken ct = default);

    Task<FeedbackListResponse?> ListFeedbacksAsync(
        string tenantId, string userId,
        Guid sessionId, CancellationToken ct = default);
}
