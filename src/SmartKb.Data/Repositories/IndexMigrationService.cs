using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// Blue-green search index migration service with version tracking and rollback.
/// P3-005: Search schema versioning and rollback strategy (NFR-OPS-003).
/// </summary>
public sealed class IndexMigrationService : IIndexMigrationService
{
    private readonly SmartKbDbContext _db;
    private readonly SearchIndexClient _indexClient;
    private readonly SearchServiceSettings _searchSettings;
    private readonly AzureSearchIndexingService _evidenceIndexing;
    private readonly AzureSearchPatternIndexingService _patternIndexing;
    private readonly ILogger<IndexMigrationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public IndexMigrationService(
        SmartKbDbContext db,
        SearchIndexClient indexClient,
        SearchServiceSettings searchSettings,
        AzureSearchIndexingService evidenceIndexing,
        AzureSearchPatternIndexingService patternIndexing,
        ILogger<IndexMigrationService> logger)
    {
        _db = db;
        _indexClient = indexClient;
        _searchSettings = searchSettings;
        _evidenceIndexing = evidenceIndexing;
        _patternIndexing = patternIndexing;
        _logger = logger;
    }

    public async Task<IndexSchemaVersionInfo?> GetCurrentVersionAsync(
        string indexType, CancellationToken cancellationToken = default)
    {
        var entity = await _db.IndexSchemaVersions
            .Where(v => v.IndexType == indexType && v.Status == IndexVersionStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MapToInfo(entity);
    }

    public async Task<IReadOnlyList<IndexSchemaVersionInfo>> ListVersionsAsync(
        string indexType, CancellationToken cancellationToken = default)
    {
        var entities = await _db.IndexSchemaVersions
            .Where(v => v.IndexType == indexType)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToInfo).ToList();
    }

    public async Task<MigrationPlan> PlanMigrationAsync(
        string indexType, CancellationToken cancellationToken = default)
    {
        var current = await _db.IndexSchemaVersions
            .Where(v => v.IndexType == indexType && v.Status == IndexVersionStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        var desiredIndex = BuildDesiredIndex(indexType);
        var desiredHash = ComputeSchemaHash(desiredIndex);

        if (current is null)
        {
            var baseName = GetBaseIndexName(indexType);
            return new MigrationPlan(
                indexType,
                CurrentIndexName: baseName,
                CurrentVersion: 0,
                CurrentSchemaHash: string.Empty,
                DesiredSchemaHash: desiredHash,
                MigrationNeeded: true,
                NewIndexName: $"{baseName}-v1",
                NewVersion: 1);
        }

        var migrationNeeded = current.SchemaHash != desiredHash;
        var newVersion = current.Version + 1;
        var newIndexName = $"{GetBaseIndexName(indexType)}-v{newVersion}";

        return new MigrationPlan(
            indexType,
            CurrentIndexName: current.IndexName,
            CurrentVersion: current.Version,
            CurrentSchemaHash: current.SchemaHash,
            DesiredSchemaHash: desiredHash,
            MigrationNeeded: migrationNeeded,
            NewIndexName: newIndexName,
            NewVersion: newVersion);
    }

    public async Task<MigrationResult> ExecuteMigrationAsync(
        string indexType, string actorId, CancellationToken cancellationToken = default)
    {
        var plan = await PlanMigrationAsync(indexType, cancellationToken);

        if (!plan.MigrationNeeded)
        {
            _logger.LogInformation(
                "No migration needed for {IndexType}: schema hash unchanged ({Hash}).",
                indexType, plan.CurrentSchemaHash);
            return new MigrationResult(true, null, indexType, plan.CurrentIndexName,
                plan.CurrentIndexName, plan.CurrentVersion, 0);
        }

        _logger.LogInformation(
            "Starting blue-green migration for {IndexType}: v{OldVer} ({OldIndex}) → v{NewVer} ({NewIndex})",
            indexType, plan.CurrentVersion, plan.CurrentIndexName, plan.NewVersion, plan.NewIndexName);

        // 1. Create new index with versioned name.
        var desiredIndex = BuildDesiredIndexWithName(indexType, plan.NewIndexName);

        // Record migration-in-progress.
        var versionEntity = new IndexSchemaVersionEntity
        {
            Id = Guid.NewGuid(),
            IndexType = indexType,
            IndexName = plan.NewIndexName,
            Version = plan.NewVersion,
            SchemaHash = plan.DesiredSchemaHash,
            Status = IndexVersionStatus.Migrating,
            CreatedBy = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.IndexSchemaVersions.Add(versionEntity);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _indexClient.CreateOrUpdateIndexAsync(desiredIndex, cancellationToken: cancellationToken);
            _logger.LogInformation("Created new index '{IndexName}'.", plan.NewIndexName);

            // 2. Reindex all documents from SQL.
            int documentsReindexed;
            if (indexType == IndexType.Evidence)
            {
                documentsReindexed = await ReindexEvidenceChunksAsync(plan.NewIndexName, cancellationToken);
            }
            else
            {
                documentsReindexed = await ReindexPatternsAsync(plan.NewIndexName, cancellationToken);
            }

            // 3. Swap: update settings and activate the new version.
            if (indexType == IndexType.Evidence)
                _searchSettings.EvidenceIndexName = plan.NewIndexName;
            else
                _searchSettings.PatternIndexName = plan.NewIndexName;

            // Retire old version.
            var oldVersion = await _db.IndexSchemaVersions
                .Where(v => v.IndexType == indexType && v.Status == IndexVersionStatus.Active)
                .FirstOrDefaultAsync(cancellationToken);

            if (oldVersion is not null)
            {
                oldVersion.Status = IndexVersionStatus.Retired;
                oldVersion.RetiredAt = DateTimeOffset.UtcNow;
            }

            // Activate new version.
            versionEntity.Status = IndexVersionStatus.Active;
            versionEntity.ActivatedAt = DateTimeOffset.UtcNow;
            versionEntity.DocumentCount = documentsReindexed;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Migration complete for {IndexType}: v{NewVer} ({NewIndex}) active with {DocCount} documents. Old index '{OldIndex}' retired.",
                indexType, plan.NewVersion, plan.NewIndexName, documentsReindexed, plan.CurrentIndexName);

            return new MigrationResult(true, null, indexType, plan.CurrentIndexName,
                plan.NewIndexName, plan.NewVersion, documentsReindexed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Migration failed for {IndexType}: rolling back version record.", indexType);

            // Clean up: remove the failed version record and try to delete the new index.
            _db.IndexSchemaVersions.Remove(versionEntity);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                await _indexClient.DeleteIndexAsync(plan.NewIndexName, cancellationToken);
            }
            catch (Exception deleteEx) when (deleteEx is not OperationCanceledException)
            {
                _logger.LogWarning(deleteEx, "Failed to clean up index '{IndexName}' after migration failure.", plan.NewIndexName);
            }

            return new MigrationResult(false, ex.Message, indexType, plan.CurrentIndexName,
                plan.NewIndexName, plan.NewVersion, 0);
        }
    }

    public async Task<MigrationResult> RollbackAsync(
        string indexType, string actorId, CancellationToken cancellationToken = default)
    {
        var current = await _db.IndexSchemaVersions
            .Where(v => v.IndexType == indexType && v.Status == IndexVersionStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
            return new MigrationResult(false, "No active version to roll back from.", indexType, "", "", 0, 0);

        var previous = await _db.IndexSchemaVersions
            .Where(v => v.IndexType == indexType && v.Status == IndexVersionStatus.Retired)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (previous is null)
            return new MigrationResult(false, "No retired version available for rollback.", indexType,
                current.IndexName, "", current.Version, 0);

        // Verify the retired index still exists in Azure AI Search.
        try
        {
            await _indexClient.GetIndexAsync(previous.IndexName, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new MigrationResult(false,
                $"Retired index '{previous.IndexName}' no longer exists in Azure AI Search. Cannot rollback.",
                indexType, current.IndexName, previous.IndexName, previous.Version, 0);
        }

        _logger.LogInformation(
            "Rolling back {IndexType}: v{CurrentVer} ({CurrentIndex}) → v{PrevVer} ({PrevIndex})",
            indexType, current.Version, current.IndexName, previous.Version, previous.IndexName);

        // Swap: reactivate previous, retire current.
        current.Status = IndexVersionStatus.Retired;
        current.RetiredAt = DateTimeOffset.UtcNow;

        previous.Status = IndexVersionStatus.Active;
        previous.ActivatedAt = DateTimeOffset.UtcNow;
        previous.RetiredAt = null;

        if (indexType == IndexType.Evidence)
            _searchSettings.EvidenceIndexName = previous.IndexName;
        else
            _searchSettings.PatternIndexName = previous.IndexName;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rollback complete for {IndexType}: v{PrevVer} ({PrevIndex}) is now active.",
            indexType, previous.Version, previous.IndexName);

        return new MigrationResult(true, null, indexType, current.IndexName,
            previous.IndexName, previous.Version, previous.DocumentCount ?? 0);
    }

    public async Task<bool> DeleteRetiredVersionAsync(
        Guid versionId, string actorId, CancellationToken cancellationToken = default)
    {
        var version = await _db.IndexSchemaVersions
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

        if (version is null) return false;

        if (version.Status != IndexVersionStatus.Retired)
        {
            _logger.LogWarning("Cannot delete non-retired index version {VersionId} (status: {Status}).",
                versionId, version.Status);
            return false;
        }

        // Delete from Azure AI Search.
        try
        {
            await _indexClient.DeleteIndexAsync(version.IndexName, cancellationToken);
            _logger.LogInformation("Deleted retired index '{IndexName}' from Azure AI Search.", version.IndexName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Retired index '{IndexName}' already deleted from Azure AI Search.", version.IndexName);
        }

        // Remove version record.
        _db.IndexSchemaVersions.Remove(version);
        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IndexSchemaVersionInfo> EnsureVersionTrackingAsync(
        string indexType, string actorId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.IndexSchemaVersions
            .Where(v => v.IndexType == indexType && v.Status == IndexVersionStatus.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
            return MapToInfo(existing);

        // Bootstrap: register current index as v1.
        var currentIndexName = GetActiveIndexName(indexType);
        var desiredIndex = BuildDesiredIndex(indexType);
        var schemaHash = ComputeSchemaHash(desiredIndex);

        var entity = new IndexSchemaVersionEntity
        {
            Id = Guid.NewGuid(),
            IndexType = indexType,
            IndexName = currentIndexName,
            Version = 1,
            SchemaHash = schemaHash,
            Status = IndexVersionStatus.Active,
            CreatedBy = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow,
        };

        _db.IndexSchemaVersions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bootstrapped version tracking for {IndexType}: v1 ({IndexName}), schema hash {Hash}.",
            indexType, currentIndexName, schemaHash);

        return MapToInfo(entity);
    }

    // --- Private helpers ---

    private SearchIndex BuildDesiredIndex(string indexType)
    {
        return indexType == IndexType.Evidence
            ? _evidenceIndexing.BuildIndexDefinition()
            : _patternIndexing.BuildIndexDefinition();
    }

    private SearchIndex BuildDesiredIndexWithName(string indexType, string indexName)
    {
        var template = BuildDesiredIndex(indexType);
        var index = new SearchIndex(indexName);
        foreach (var field in template.Fields) index.Fields.Add(field);
        if (template.VectorSearch is not null) index.VectorSearch = template.VectorSearch;
        if (template.SemanticSearch is not null) index.SemanticSearch = template.SemanticSearch;
        return index;
    }

    private string GetBaseIndexName(string indexType)
    {
        return indexType == IndexType.Evidence ? "evidence" : "patterns";
    }

    private string GetActiveIndexName(string indexType)
    {
        return indexType == IndexType.Evidence
            ? _searchSettings.EvidenceIndexName
            : _searchSettings.PatternIndexName;
    }

    internal static string ComputeSchemaHash(SearchIndex index)
    {
        var sb = new StringBuilder();

        // Sort fields by name for deterministic hashing.
        foreach (var field in index.Fields.OrderBy(f => f.Name))
        {
            sb.Append(field.Name).Append(':');
            sb.Append(field.Type).Append(':');
            sb.Append(field.IsKey).Append(':');
            sb.Append(field.IsFilterable).Append(':');
            sb.Append(field.IsSortable).Append(':');
            sb.Append(field.IsFacetable).Append(':');
            sb.Append(field.IsSearchable).Append(':');
            sb.Append(field.AnalyzerName?.ToString() ?? "").Append(':');
            sb.Append(field.SynonymMapNames != null ? string.Join(",", field.SynonymMapNames) : "").Append(';');
        }

        // Include vector search config.
        if (index.VectorSearch is not null)
        {
            foreach (var profile in index.VectorSearch.Profiles.OrderBy(p => p.Name))
            {
                sb.Append("vp:").Append(profile.Name).Append(':').Append(profile.AlgorithmConfigurationName).Append(';');
            }
            foreach (var algo in index.VectorSearch.Algorithms.OrderBy(a => a.Name))
            {
                sb.Append("va:").Append(algo.Name).Append(';');
            }
        }

        // Include semantic config.
        if (index.SemanticSearch is not null)
        {
            foreach (var config in index.SemanticSearch.Configurations.OrderBy(c => c.Name))
            {
                sb.Append("sc:").Append(config.Name).Append(';');
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(bytes);
    }

    private async Task<int> ReindexEvidenceChunksAsync(string targetIndexName, CancellationToken ct)
    {
        var searchClient = _indexClient.GetSearchClient(targetIndexName);
        var totalIndexed = 0;
        const int batchSize = 500;
        var offset = 0;

        while (true)
        {
            var chunks = await _db.EvidenceChunks
                .AsNoTracking()
                .OrderBy(c => c.ChunkId)
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync(ct);

            if (chunks.Count == 0) break;

            var documents = chunks.Select(MapEvidenceEntityToSearchDocument).ToList();

            for (var i = 0; i < documents.Count; i += _searchSettings.IndexBatchSize)
            {
                var batch = documents.Skip(i).Take(_searchSettings.IndexBatchSize).ToList();
                try
                {
                    var response = await searchClient.MergeOrUploadDocumentsAsync(batch, cancellationToken: ct);
                    totalIndexed += response.Value.Results.Count(r => r.Succeeded);
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogError(ex, "Failed to reindex batch at offset {Offset}.", offset + i);
                    throw;
                }
            }

            offset += chunks.Count;
            _logger.LogInformation("Reindexed {Count}/{Total} evidence chunks into '{IndexName}'.",
                offset, offset, targetIndexName);

            if (chunks.Count < batchSize) break;
        }

        return totalIndexed;
    }

    private async Task<int> ReindexPatternsAsync(string targetIndexName, CancellationToken ct)
    {
        var searchClient = _indexClient.GetSearchClient(targetIndexName);
        var totalIndexed = 0;

        var patterns = await _db.CasePatterns
            .AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .ToListAsync(ct);

        var documents = patterns.Select(MapPatternEntityToSearchDocument).ToList();

        for (var i = 0; i < documents.Count; i += _searchSettings.IndexBatchSize)
        {
            var batch = documents.Skip(i).Take(_searchSettings.IndexBatchSize).ToList();
            try
            {
                var response = await searchClient.MergeOrUploadDocumentsAsync(batch, cancellationToken: ct);
                totalIndexed += response.Value.Results.Count(r => r.Succeeded);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to reindex pattern batch at offset {Offset}.", i);
                throw;
            }
        }

        _logger.LogInformation("Reindexed {Count} patterns into '{IndexName}'.", totalIndexed, targetIndexName);
        return totalIndexed;
    }

    private static SearchDocument MapEvidenceEntityToSearchDocument(EvidenceChunkEntity e)
    {
        return new SearchDocument
        {
            [SearchFieldNames.ChunkId] = e.ChunkId,
            [SearchFieldNames.ChunkText] = e.ChunkText,
            [SearchFieldNames.ChunkContext] = e.ChunkContext ?? string.Empty,
            [SearchFieldNames.Title] = e.Title,
            // Embedding vector is not stored in SQL — will be null during reindex.
            // A subsequent re-embedding pass or incremental sync will repopulate.
            [SearchFieldNames.TenantId] = e.TenantId,
            [SearchFieldNames.EvidenceId] = e.EvidenceId,
            [SearchFieldNames.SourceSystem] = e.SourceSystem,
            [SearchFieldNames.SourceType] = e.SourceType,
            [SearchFieldNames.Status] = e.Status,
            [SearchFieldNames.UpdatedAt] = e.UpdatedAt,
            [SearchFieldNames.ProductArea] = e.ProductArea ?? string.Empty,
            [SearchFieldNames.Tags] = DeserializeJsonList(e.Tags),
            [SearchFieldNames.Visibility] = e.Visibility,
            [SearchFieldNames.AllowedGroups] = DeserializeJsonList(e.AllowedGroups),
            [SearchFieldNames.AccessLabel] = e.AccessLabel,
            [SearchFieldNames.SourceUrl] = e.SourceUrl,
        };
    }

    private static SearchDocument MapPatternEntityToSearchDocument(CasePatternEntity p)
    {
        return new SearchDocument
        {
            [PatternFieldNames.PatternId] = p.PatternId,
            [PatternFieldNames.Title] = p.Title,
            [PatternFieldNames.ProblemStatement] = p.ProblemStatement,
            [PatternFieldNames.Symptoms] = string.Join("\n", DeserializeJsonList(p.SymptomsJson)),
            [PatternFieldNames.ResolutionSteps] = string.Join("\n", DeserializeJsonList(p.ResolutionStepsJson)),
            // Embedding vector is not stored in SQL — will be null during reindex.
            [PatternFieldNames.TenantId] = p.TenantId,
            [PatternFieldNames.TrustLevel] = p.TrustLevel,
            [PatternFieldNames.ProductArea] = p.ProductArea ?? string.Empty,
            [PatternFieldNames.UpdatedAt] = p.UpdatedAt,
            [PatternFieldNames.Confidence] = (double)p.Confidence,
            [PatternFieldNames.Tags] = DeserializeJsonList(p.TagsJson),
            [PatternFieldNames.Version] = p.Version,
            [PatternFieldNames.Visibility] = p.Visibility,
            [PatternFieldNames.AllowedGroups] = DeserializeJsonList(p.AllowedGroupsJson),
            [PatternFieldNames.AccessLabel] = p.AccessLabel,
            [PatternFieldNames.SourceUrl] = p.SourceUrl,
        };
    }

    private List<string> DeserializeJsonList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize JSON in {MethodName}", nameof(DeserializeJsonList));
            return [];
        }
    }

    private static IndexSchemaVersionInfo MapToInfo(IndexSchemaVersionEntity e)
    {
        return new IndexSchemaVersionInfo(
            e.Id, e.IndexType, e.IndexName, e.Version,
            e.SchemaHash, e.Status, e.DocumentCount,
            e.CreatedBy, e.CreatedAt, e.ActivatedAt, e.RetiredAt);
    }
}
