using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages search index schema versions with blue-green migration and rollback capability.
/// P3-005: Search schema versioning and rollback strategy (NFR-OPS-003).
/// </summary>
public interface IIndexMigrationService
{
    /// <summary>
    /// Returns the current active version for the given index type, or null if untracked.
    /// </summary>
    Task<IndexSchemaVersionInfo?> GetCurrentVersionAsync(
        string indexType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all schema versions for the given index type (active, migrating, retired).
    /// </summary>
    Task<IReadOnlyList<IndexSchemaVersionInfo>> ListVersionsAsync(
        string indexType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the current active schema hash against the desired schema and returns a migration plan.
    /// </summary>
    Task<MigrationPlan> PlanMigrationAsync(
        string indexType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a blue-green migration: creates a new versioned index, reindexes all documents
    /// from SQL, swaps the active index, and retires the old one.
    /// </summary>
    Task<MigrationResult> ExecuteMigrationAsync(
        string indexType,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back to the most recent retired version (if it still exists in Azure AI Search).
    /// Reactivates the old index and retires the current one.
    /// </summary>
    Task<MigrationResult> RollbackAsync(
        string indexType,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a retired index from Azure AI Search and removes the version record.
    /// </summary>
    Task<bool> DeleteRetiredVersionAsync(
        Guid versionId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the current index as version 1 if no version tracking exists yet.
    /// Called during startup to bootstrap version tracking for existing indexes.
    /// </summary>
    Task<IndexSchemaVersionInfo> EnsureVersionTrackingAsync(
        string indexType,
        string actorId,
        CancellationToken cancellationToken = default);
}
