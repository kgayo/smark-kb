using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface ITeamPlaybookService
{
    Task<TeamPlaybookListResponse> GetPlaybooksAsync(string tenantId, CancellationToken ct = default);
    Task<TeamPlaybookDto?> GetPlaybookAsync(string tenantId, Guid playbookId, CancellationToken ct = default);
    Task<TeamPlaybookDto?> GetPlaybookByTeamAsync(string tenantId, string teamName, CancellationToken ct = default);
    Task<TeamPlaybookDto> CreatePlaybookAsync(string tenantId, string userId, string correlationId, CreateTeamPlaybookRequest request, CancellationToken ct = default);
    Task<TeamPlaybookDto?> UpdatePlaybookAsync(string tenantId, string userId, string correlationId, Guid playbookId, UpdateTeamPlaybookRequest request, CancellationToken ct = default);
    Task<bool> DeletePlaybookAsync(string tenantId, string userId, string correlationId, Guid playbookId, CancellationToken ct = default);

    /// <summary>
    /// Validate an escalation draft against the target team's playbook.
    /// Returns validation result with missing fields and policy violations.
    /// </summary>
    Task<PlaybookValidationResult> ValidateDraftAsync(string tenantId, string targetTeam, CreateEscalationDraftRequest draft, CancellationToken ct = default);
}
