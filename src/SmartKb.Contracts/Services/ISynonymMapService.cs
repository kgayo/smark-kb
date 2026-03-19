using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// CRUD management for per-tenant synonym rules and synchronization to Azure AI Search synonym maps.
/// P3-004: Synonym maps for domain vocabulary.
/// </summary>
public interface ISynonymMapService
{
    Task<SynonymRuleListResponse> ListAsync(string tenantId, string? groupName = null, CancellationToken ct = default);
    Task<SynonymRuleResponse?> GetAsync(string tenantId, Guid ruleId, CancellationToken ct = default);
    Task<(SynonymRuleResponse? Response, SynonymRuleValidationResult? Validation)> CreateAsync(
        string tenantId, string actorId, string correlationId, CreateSynonymRuleRequest request, CancellationToken ct = default);
    Task<(SynonymRuleResponse? Response, SynonymRuleValidationResult? Validation, bool NotFound)> UpdateAsync(
        string tenantId, string actorId, string correlationId, Guid ruleId, UpdateSynonymRuleRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(string tenantId, string actorId, string correlationId, Guid ruleId, CancellationToken ct = default);
    Task<SynonymMapSyncResult> SyncToSearchAsync(string tenantId, string correlationId, CancellationToken ct = default);
    Task<int> SeedDefaultsAsync(string tenantId, string actorId, string correlationId, bool overwriteExisting = false, CancellationToken ct = default);
}
