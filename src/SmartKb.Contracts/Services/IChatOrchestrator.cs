using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Orchestrates grounded chat responses: embed query → retrieve evidence → assemble prompt →
/// call OpenAI structured output → blend confidence → persist trace → return response.
/// </summary>
public interface IChatOrchestrator
{
    /// <summary>
    /// Processes a chat request through the full orchestration pipeline.
    /// </summary>
    /// <param name="tenantId">Tenant ID for isolation.</param>
    /// <param name="userId">Requesting user ID.</param>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="request">The chat request with query and optional session history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured chat response with citations, confidence, and optional escalation.</returns>
    Task<ChatResponse> OrchestrateAsync(
        string tenantId,
        string userId,
        string correlationId,
        ChatRequest request,
        CancellationToken cancellationToken = default);
}
