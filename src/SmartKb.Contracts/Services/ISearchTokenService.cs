using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages per-tenant stop words and special tokens for search query preprocessing.
/// P3-028: Stop-words and special tokens management.
/// </summary>
public interface ISearchTokenService
{
    // --- Stop Words ---
    Task<StopWordListResponse> ListStopWordsAsync(string tenantId, string? groupName = null, CancellationToken ct = default);
    Task<StopWordResponse?> GetStopWordAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<(StopWordResponse? Response, SearchTokenValidationResult? Validation)> CreateStopWordAsync(
        string tenantId, string actorId, string correlationId, CreateStopWordRequest request, CancellationToken ct = default);
    Task<(StopWordResponse? Response, SearchTokenValidationResult? Validation, bool NotFound)> UpdateStopWordAsync(
        string tenantId, string actorId, string correlationId, Guid id, UpdateStopWordRequest request, CancellationToken ct = default);
    Task<bool> DeleteStopWordAsync(string tenantId, string actorId, string correlationId, Guid id, CancellationToken ct = default);
    Task<int> SeedDefaultStopWordsAsync(string tenantId, string actorId, string correlationId, bool overwriteExisting = false, CancellationToken ct = default);

    // --- Special Tokens ---
    Task<SpecialTokenListResponse> ListSpecialTokensAsync(string tenantId, string? category = null, CancellationToken ct = default);
    Task<SpecialTokenResponse?> GetSpecialTokenAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<(SpecialTokenResponse? Response, SearchTokenValidationResult? Validation)> CreateSpecialTokenAsync(
        string tenantId, string actorId, string correlationId, CreateSpecialTokenRequest request, CancellationToken ct = default);
    Task<(SpecialTokenResponse? Response, SearchTokenValidationResult? Validation, bool NotFound)> UpdateSpecialTokenAsync(
        string tenantId, string actorId, string correlationId, Guid id, UpdateSpecialTokenRequest request, CancellationToken ct = default);
    Task<bool> DeleteSpecialTokenAsync(string tenantId, string actorId, string correlationId, Guid id, CancellationToken ct = default);
    Task<int> SeedDefaultSpecialTokensAsync(string tenantId, string actorId, string correlationId, bool overwriteExisting = false, CancellationToken ct = default);

    // --- Query Preprocessing ---
    Task<QueryPreprocessingResult> PreprocessQueryAsync(string tenantId, string query, CancellationToken ct = default);
}
