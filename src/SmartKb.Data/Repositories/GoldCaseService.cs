using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class GoldCaseService : IGoldCaseService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _audit;
    private readonly ILogger<GoldCaseService> _logger;

    public GoldCaseService(SmartKbDbContext db, IAuditEventWriter audit, ILogger<GoldCaseService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<GoldCaseDetail> CreateAsync(string tenantId, CreateGoldCaseRequest request, string actorId, CancellationToken ct = default)
    {
        var errors = Validate(request.CaseId, request.Query, request.Expected);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors));

        // Check for duplicate CaseId within tenant.
        var exists = await _db.GoldCases
            .AnyAsync(g => g.TenantId == tenantId && g.CaseId == request.CaseId, ct);
        if (exists)
            throw new InvalidOperationException($"Gold case '{request.CaseId}' already exists.");

        var now = DateTimeOffset.UtcNow;
        var entity = new GoldCaseEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CaseId = request.CaseId,
            Query = request.Query,
            ContextJson = request.Context is not null ? JsonSerializer.Serialize(request.Context, SharedJsonOptions.CamelCase) : null,
            ExpectedJson = JsonSerializer.Serialize(request.Expected, SharedJsonOptions.CamelCase),
            TagsJson = JsonSerializer.Serialize(request.Tags ?? (IReadOnlyList<string>)[]),
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.GoldCases.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(Guid.NewGuid().ToString(), AuditEventTypes.GoldCaseCreated,
            tenantId, actorId, string.Empty, DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(new { entity.CaseId, entity.Id })), ct);

        _logger.LogInformation("Gold case {CaseId} created by {Actor} in tenant {TenantId}", entity.CaseId, actorId, tenantId);

        return MapToDetail(entity);
    }

    public async Task<GoldCaseDetail?> GetAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GoldCases
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct);
        return entity is not null ? MapToDetail(entity) : null;
    }

    public async Task<GoldCaseListResponse> ListAsync(string tenantId, string? tag = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        pageSize = PaginationDefaults.ClampPageSize(pageSize);
        page = Math.Max(1, page);

        var query = _db.GoldCases.Where(g => g.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(g => g.TagsJson.Contains(tag));

        var totalCount = await query.CountAsync(ct);

        var entities = await query
            .OrderBy(g => g.CaseId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new GoldCaseListResponse
        {
            Cases = entities.Select(MapToSummary).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount,
        };
    }

    public async Task<GoldCaseDetail?> UpdateAsync(string tenantId, Guid id, UpdateGoldCaseRequest request, string actorId, CancellationToken ct = default)
    {
        var entity = await _db.GoldCases
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct);
        if (entity is null) return null;

        if (request.Query is not null)
        {
            if (request.Query.Length < 5)
                throw new ArgumentException("Query must be at least 5 characters.");
            entity.Query = request.Query;
        }

        if (request.Context is not null)
            entity.ContextJson = JsonSerializer.Serialize(request.Context, SharedJsonOptions.CamelCase);

        if (request.Expected is not null)
        {
            var errors = ValidateExpected(request.Expected);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join("; ", errors));
            entity.ExpectedJson = JsonSerializer.Serialize(request.Expected, SharedJsonOptions.CamelCase);
        }

        if (request.Tags is not null)
            entity.TagsJson = JsonSerializer.Serialize(request.Tags);

        entity.UpdatedBy = actorId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(Guid.NewGuid().ToString(), AuditEventTypes.GoldCaseUpdated,
            tenantId, actorId, string.Empty, DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(new { entity.CaseId, entity.Id })), ct);

        _logger.LogInformation("Gold case {CaseId} updated by {Actor}", entity.CaseId, actorId);

        return MapToDetail(entity);
    }

    public async Task<bool> DeleteAsync(string tenantId, Guid id, string actorId, CancellationToken ct = default)
    {
        var entity = await _db.GoldCases
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct);
        if (entity is null) return false;

        _db.GoldCases.Remove(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(Guid.NewGuid().ToString(), AuditEventTypes.GoldCaseDeleted,
            tenantId, actorId, string.Empty, DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(new { entity.CaseId, entity.Id })), ct);

        _logger.LogInformation("Gold case {CaseId} deleted by {Actor}", entity.CaseId, actorId);

        return true;
    }

    public async Task<GoldCaseDetail> PromoteFromFeedbackAsync(string tenantId, PromoteFromFeedbackRequest request, string actorId, CancellationToken ct = default)
    {
        // Load the feedback + associated message.
        var feedback = await _db.Feedbacks
            .Include(f => f.Message)
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Id == request.FeedbackId, ct);

        if (feedback is null)
            throw new InvalidOperationException("Feedback not found.");

        // Build query from the original user message in the session.
        var userMessages = await _db.Messages
            .Where(m => m.SessionId == feedback.SessionId && m.TenantId == tenantId && m.Role == Contracts.Enums.MessageRole.User)
            .ToListAsync(ct);
        var userMessage = userMessages
            .Where(m => m.CreatedAt <= feedback.Message.CreatedAt)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        var query = feedback.CorrectionText
            ?? userMessage?.Content
            ?? feedback.Comment
            ?? "Promoted from feedback";

        if (query.Length < 5)
            query = query.PadRight(5, '.');

        var createRequest = new CreateGoldCaseRequest
        {
            CaseId = request.CaseId,
            Query = query,
            Expected = request.Expected,
            Tags = request.Tags,
        };

        var errors = Validate(createRequest.CaseId, createRequest.Query, createRequest.Expected);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors));

        var exists = await _db.GoldCases
            .AnyAsync(g => g.TenantId == tenantId && g.CaseId == request.CaseId, ct);
        if (exists)
            throw new InvalidOperationException($"Gold case '{request.CaseId}' already exists.");

        var now = DateTimeOffset.UtcNow;
        var entity = new GoldCaseEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CaseId = request.CaseId,
            Query = query,
            ExpectedJson = JsonSerializer.Serialize(request.Expected, SharedJsonOptions.CamelCase),
            TagsJson = JsonSerializer.Serialize(request.Tags ?? (IReadOnlyList<string>)[]),
            SourceFeedbackId = request.FeedbackId,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.GoldCases.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(Guid.NewGuid().ToString(), AuditEventTypes.GoldCasePromoted,
            tenantId, actorId, string.Empty, DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(new { entity.CaseId, entity.Id, request.FeedbackId })), ct);

        _logger.LogInformation("Gold case {CaseId} promoted from feedback {FeedbackId} by {Actor}", entity.CaseId, request.FeedbackId, actorId);

        return MapToDetail(entity);
    }

    public async Task<string> ExportAsJsonlAsync(string tenantId, CancellationToken ct = default)
    {
        var entities = await _db.GoldCases
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.CaseId)
            .ToListAsync(ct);

        var lines = entities.Select(e =>
        {
            var obj = new Dictionary<string, object?>
            {
                ["id"] = e.CaseId,
                ["tenant_id"] = e.TenantId,
                ["query"] = e.Query,
            };

            if (e.ContextJson is not null)
                obj["context"] = JsonDeserializeHelper.DeserializeOrNull<object>(e.ContextJson, SharedJsonOptions.CamelCase, _logger);

            obj["expected"] = JsonDeserializeHelper.DeserializeOrNull<object>(e.ExpectedJson, SharedJsonOptions.CamelCase, _logger)
                              ?? new { responseType = "unknown" };

            var tags = DeserializeTags(e.TagsJson);
            if (tags.Count > 0)
                obj["tags"] = tags;

            return JsonSerializer.Serialize(obj, SharedJsonOptions.CamelCase);
        });

        return string.Join("\n", lines);
    }

    // ── Validation ──

    private static readonly HashSet<string> ValidResponseTypes = [ChatResponseType.FinalAnswer, ChatResponseType.NextStepsOnly, ChatResponseType.Escalate];

    private static List<string> Validate(string caseId, string query, GoldCaseExpected expected)
    {
        var errors = new List<string>();

        if (!EvalCaseId.IsValid(caseId))
            errors.Add("CaseId must match format 'eval-NNNNN' (eval- prefix + exactly 5 digits).");

        if (string.IsNullOrWhiteSpace(query) || query.Length < 5)
            errors.Add("Query must be at least 5 characters.");

        errors.AddRange(ValidateExpected(expected));

        return errors;
    }

    private static List<string> ValidateExpected(GoldCaseExpected expected)
    {
        var errors = new List<string>();

        if (!ValidResponseTypes.Contains(expected.ResponseType))
            errors.Add($"ResponseType must be one of: {string.Join(", ", ValidResponseTypes)}.");

        if (expected.MinConfidence.HasValue && (expected.MinConfidence < 0 || expected.MinConfidence > 1))
            errors.Add("MinConfidence must be between 0 and 1.");

        if (expected.MinCitations.HasValue && expected.MinCitations < 0)
            errors.Add("MinCitations must be >= 0.");

        return errors;
    }

    // ── Mapping ──

    private GoldCaseSummary MapToSummary(GoldCaseEntity e)
    {
        var expected = JsonDeserializeHelper.DeserializeOrNull<GoldCaseExpected>(e.ExpectedJson, SharedJsonOptions.CamelCase, _logger);
        return new GoldCaseSummary
        {
            Id = e.Id,
            CaseId = e.CaseId,
            Query = e.Query,
            ResponseType = expected?.ResponseType ?? "unknown",
            Tags = DeserializeTags(e.TagsJson),
            SourceFeedbackId = e.SourceFeedbackId,
            CreatedBy = e.CreatedBy,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };
    }

    private GoldCaseDetail MapToDetail(GoldCaseEntity e)
    {
        return new GoldCaseDetail
        {
            Id = e.Id,
            CaseId = e.CaseId,
            Query = e.Query,
            Context = e.ContextJson is not null
                ? JsonDeserializeHelper.DeserializeOrNull<GoldCaseContext>(e.ContextJson, SharedJsonOptions.CamelCase, _logger)
                : null,
            Expected = JsonDeserializeHelper.DeserializeOrNull<GoldCaseExpected>(e.ExpectedJson, SharedJsonOptions.CamelCase, _logger)
                       ?? new GoldCaseExpected { ResponseType = "unknown" },
            Tags = DeserializeTags(e.TagsJson),
            SourceFeedbackId = e.SourceFeedbackId,
            CreatedBy = e.CreatedBy,
            UpdatedBy = e.UpdatedBy,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };
    }

    private IReadOnlyList<string> DeserializeTags(string json) =>
        JsonDeserializeHelper.Deserialize<List<string>>(json, SharedJsonOptions.CamelCase, _logger, []);
}
