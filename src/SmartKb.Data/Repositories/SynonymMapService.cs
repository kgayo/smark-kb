using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Exceptions;

namespace SmartKb.Data.Repositories;

/// <summary>
/// Per-tenant synonym rule CRUD with Azure AI Search synonym map synchronization.
/// P3-004: Synonym maps for domain vocabulary.
/// </summary>
public sealed class SynonymMapService : ISynonymMapService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _audit;
    private readonly SearchIndexClient? _searchIndexClient;
    private readonly SearchServiceSettings _searchSettings;
    private readonly ILogger<SynonymMapService> _logger;

    public SynonymMapService(
        SmartKbDbContext db,
        IAuditEventWriter audit,
        SearchServiceSettings searchSettings,
        ILogger<SynonymMapService> logger,
        SearchIndexClient? searchIndexClient = null)
    {
        _db = db;
        _audit = audit;
        _searchSettings = searchSettings;
        _logger = logger;
        _searchIndexClient = searchIndexClient;
    }

    public async Task<SynonymRuleListResponse> ListAsync(string tenantId, string? groupName = null, CancellationToken ct = default)
    {
        var query = _db.SynonymMaps.Where(s => s.TenantId == tenantId);

        if (!string.IsNullOrEmpty(groupName))
            query = query.Where(s => s.GroupName == groupName);

        var rules = await query
            .OrderBy(s => s.GroupName).ThenBy(s => s.Id)
            .ToListAsync(ct);

        var groups = rules.Select(r => r.GroupName).Distinct().OrderBy(g => g).ToList();

        return new SynonymRuleListResponse
        {
            Rules = rules.Select(ToResponse).ToList(),
            TotalCount = rules.Count,
            Groups = groups,
        };
    }

    public async Task<SynonymRuleResponse?> GetAsync(string tenantId, Guid ruleId, CancellationToken ct = default)
    {
        var entity = await _db.SynonymMaps
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == ruleId, ct);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<(SynonymRuleResponse? Response, SynonymRuleValidationResult? Validation)> CreateAsync(
        string tenantId, string actorId, string correlationId, CreateSynonymRuleRequest request, CancellationToken ct = default)
    {
        var validation = ValidateRule(request.Rule);
        if (!validation.IsValid)
            return (null, validation);

        var now = DateTimeOffset.UtcNow;
        var entity = new SynonymMapEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GroupName = request.GroupName.Trim(),
            Rule = request.Rule.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };

        _db.SynonymMaps.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: "SynonymRule.Created",
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Created synonym rule '{entity.Rule}' in group '{entity.GroupName}'."), ct);

        _logger.LogInformation("Created synonym rule {RuleId} for tenant {TenantId} in group {GroupName}.",
            entity.Id, tenantId, entity.GroupName);

        return (ToResponse(entity), null);
    }

    public async Task<(SynonymRuleResponse? Response, SynonymRuleValidationResult? Validation, bool NotFound)> UpdateAsync(
        string tenantId, string actorId, string correlationId, Guid ruleId, UpdateSynonymRuleRequest request, CancellationToken ct = default)
    {
        var entity = await _db.SynonymMaps
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == ruleId, ct);

        if (entity is null)
            return (null, null, true);

        if (request.Rule is not null)
        {
            var validation = ValidateRule(request.Rule);
            if (!validation.IsValid)
                return (null, validation, false);
            entity.Rule = request.Rule.Trim();
        }

        if (request.GroupName is not null)
            entity.GroupName = request.GroupName.Trim();

        if (request.Description is not null)
            entity.Description = request.Description.Trim();

        if (request.IsActive.HasValue)
            entity.IsActive = request.IsActive.Value;

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = actorId;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("synonym rule", ex);
        }

        await _audit.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: "SynonymRule.Updated",
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Updated synonym rule {ruleId}."), ct);

        return (ToResponse(entity), null, false);
    }

    public async Task<bool> DeleteAsync(string tenantId, string actorId, string correlationId, Guid ruleId, CancellationToken ct = default)
    {
        var entity = await _db.SynonymMaps
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == ruleId, ct);

        if (entity is null)
            return false;

        _db.SynonymMaps.Remove(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: "SynonymRule.Deleted",
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Deleted synonym rule '{entity.Rule}' from group '{entity.GroupName}'."), ct);

        return true;
    }

    public async Task<SynonymMapSyncResult> SyncToSearchAsync(string tenantId, string correlationId, CancellationToken ct = default)
    {
        if (_searchIndexClient is null)
        {
            return new SynonymMapSyncResult
            {
                Success = false,
                RuleCount = 0,
                EvidenceSynonymMapName = "",
                PatternSynonymMapName = "",
                ErrorDetail = "Azure AI Search is not configured.",
            };
        }

        var activeRules = await _db.SynonymMaps
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .OrderBy(s => s.GroupName).ThenBy(s => s.Id)
            .Select(s => s.Rule)
            .ToListAsync(ct);

        var evidenceMapName = $"{_searchSettings.EvidenceIndexName}-synonyms";
        var patternMapName = $"{_searchSettings.PatternIndexName}-synonyms";

        // Azure AI Search synonym maps use newline-separated rules in Solr format.
        var synonymBody = string.Join("\n", activeRules);

        try
        {
            // Create or update synonym maps for both indexes.
            var evidenceMap = new SynonymMap(evidenceMapName, synonymBody);
            await _searchIndexClient.CreateOrUpdateSynonymMapAsync(evidenceMap, cancellationToken: ct);

            var patternMap = new SynonymMap(patternMapName, synonymBody);
            await _searchIndexClient.CreateOrUpdateSynonymMapAsync(patternMap, cancellationToken: ct);

            // Update the Evidence index to reference the synonym map on searchable fields.
            await ApplySynonymMapToIndexAsync(_searchSettings.EvidenceIndexName, evidenceMapName, ct);
            await ApplySynonymMapToIndexAsync(_searchSettings.PatternIndexName, patternMapName, ct);

            _logger.LogInformation(
                "Synced {RuleCount} synonym rules for tenant {TenantId} to Azure AI Search maps '{EvidenceMap}' and '{PatternMap}'.",
                activeRules.Count, tenantId, evidenceMapName, patternMapName);

            return new SynonymMapSyncResult
            {
                Success = true,
                RuleCount = activeRules.Count,
                EvidenceSynonymMapName = evidenceMapName,
                PatternSynonymMapName = patternMapName,
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to sync synonym maps for tenant {TenantId}.", tenantId);
            return new SynonymMapSyncResult
            {
                Success = false,
                RuleCount = activeRules.Count,
                EvidenceSynonymMapName = evidenceMapName,
                PatternSynonymMapName = patternMapName,
                ErrorDetail = ex.Message,
            };
        }
    }

    public async Task<int> SeedDefaultsAsync(string tenantId, string actorId, string correlationId, bool overwriteExisting = false, CancellationToken ct = default)
    {
        if (overwriteExisting)
        {
            var existing = await _db.SynonymMaps.Where(s => s.TenantId == tenantId).ToListAsync(ct);
            _db.SynonymMaps.RemoveRange(existing);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            var hasRules = await _db.SynonymMaps.AnyAsync(s => s.TenantId == tenantId, ct);
            if (hasRules)
                return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var defaults = GetDefaultSynonymRules();
        var seeded = 0;

        foreach (var (group, rule, description) in defaults)
        {
            _db.SynonymMaps.Add(new SynonymMapEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                GroupName = group,
                Rule = rule,
                Description = description,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = actorId,
            });
            seeded++;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: "SynonymRule.Seeded",
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Seeded {seeded} default synonym rules (overwrite={overwriteExisting})."), ct);

        _logger.LogInformation("Seeded {Count} default synonym rules for tenant {TenantId}.", seeded, tenantId);

        return seeded;
    }

    private async Task ApplySynonymMapToIndexAsync(string indexName, string synonymMapName, CancellationToken ct)
    {
        if (_searchIndexClient is null)
            throw new InvalidOperationException("Azure AI Search is not configured.");

        var index = await _searchIndexClient.GetIndexAsync(indexName, ct);
        var definition = index.Value;

        var updated = false;
        foreach (var field in definition.Fields)
        {
            if (field.IsSearchable == true && !field.SynonymMapNames.Contains(synonymMapName))
            {
                field.SynonymMapNames.Add(synonymMapName);
                updated = true;
            }
        }

        if (updated)
        {
            await _searchIndexClient.CreateOrUpdateIndexAsync(definition, cancellationToken: ct);
            _logger.LogInformation("Applied synonym map '{SynonymMapName}' to index '{IndexName}'.", synonymMapName, indexName);
        }
    }

    internal static SynonymRuleValidationResult ValidateRule(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
            return SynonymRuleValidationResult.Invalid("Rule cannot be empty.");

        if (rule.Length > ValidationLimits.SynonymRuleMaxLength)
            return SynonymRuleValidationResult.Invalid($"Rule must not exceed {ValidationLimits.SynonymRuleMaxLength} characters.");

        // Solr synonym format: either "term1, term2, term3" (equivalent) or "term1 => term2" (explicit).
        var trimmed = rule.Trim();
        if (trimmed.Contains("=>", StringComparison.Ordinal))
        {
            var parts = trimmed.Split("=>", 2);
            if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                return SynonymRuleValidationResult.Invalid("Explicit mapping must have terms on both sides of '=>'.");
        }
        else if (trimmed.Contains(','))
        {
            var terms = trimmed.Split(',');
            if (terms.Length < 2)
                return SynonymRuleValidationResult.Invalid("Equivalent synonym rule must have at least two terms separated by commas.");
            if (terms.Any(t => string.IsNullOrWhiteSpace(t)))
                return SynonymRuleValidationResult.Invalid("Synonym terms cannot be empty.");
        }
        else
        {
            // Single term is valid but not useful — allow it but note it.
            if (trimmed.Length == 0)
                return SynonymRuleValidationResult.Invalid("Rule cannot be empty.");
        }

        return SynonymRuleValidationResult.Valid();
    }

    internal static IReadOnlyList<(string Group, string Rule, string Description)> GetDefaultSynonymRules()
    {
        return
        [
            ("general", "crash, BSOD, blue screen, blue screen of death", "Common crash terminology"),
            ("general", "ticket, case, incident, issue, support request", "Support case terminology"),
            ("general", "error, failure, fault, exception, bug", "Error terminology"),
            ("general", "fix, resolution, solution, workaround, remediation", "Resolution terminology"),
            ("general", "deploy, deployment, release, rollout, push", "Deployment terminology"),
            ("general", "config, configuration, settings, preferences, options", "Configuration terminology"),
            ("general", "auth, authentication, login, sign-in, sso", "Authentication terminology"),
            ("general", "authz, authorization, permissions, access control, RBAC", "Authorization terminology"),
            ("general", "perf, performance, latency, speed, throughput, response time", "Performance terminology"),
            ("general", "DB, database, data store, datastore, SQL", "Database terminology"),
            ("error-codes", "HTTP 500, 500 error, internal server error, server error", "HTTP 500 error variations"),
            ("error-codes", "HTTP 502, 502 error, bad gateway", "HTTP 502 error variations"),
            ("error-codes", "HTTP 503, 503 error, service unavailable", "HTTP 503 error variations"),
            ("error-codes", "HTTP 404, 404 error, not found, page not found", "HTTP 404 error variations"),
            ("error-codes", "HTTP 401, 401 error, unauthorized", "HTTP 401 error variations"),
            ("error-codes", "HTTP 403, 403 error, forbidden, access denied", "HTTP 403 error variations"),
            ("error-codes", "timeout, timed out, request timeout, connection timeout", "Timeout terminology"),
            ("product-names", "K8s, Kubernetes, kube", "Kubernetes synonyms"),
            ("product-names", "VM, virtual machine", "Virtual machine synonyms"),
            ("product-names", "LB, load balancer", "Load balancer synonyms"),
        ];
    }

    private static SynonymRuleResponse ToResponse(SynonymMapEntity entity) => new()
    {
        Id = entity.Id,
        TenantId = entity.TenantId,
        GroupName = entity.GroupName,
        Rule = entity.Rule,
        Description = entity.Description,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        CreatedBy = entity.CreatedBy,
        UpdatedBy = entity.UpdatedBy,
    };
}
