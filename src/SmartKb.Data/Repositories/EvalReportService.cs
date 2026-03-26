using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class EvalReportService : IEvalReportService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _audit;
    private readonly IEvalNotificationService? _notificationService;
    private readonly ILogger<EvalReportService> _logger;

    public EvalReportService(
        SmartKbDbContext db,
        IAuditEventWriter audit,
        ILogger<EvalReportService> logger,
        IEvalNotificationService? notificationService = null)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task<EvalReportDetail> PersistReportAsync(
        string tenantId,
        PersistEvalReportRequest request,
        string actorId,
        CancellationToken ct = default)
    {
        var entity = new EvalReportEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RunId = request.RunId,
            RunType = request.RunType,
            TotalCases = request.TotalCases,
            SuccessfulCases = request.SuccessfulCases,
            FailedCases = request.FailedCases,
            MetricsJson = request.MetricsJson,
            ViolationsJson = request.ViolationsJson,
            BaselineComparisonJson = request.BaselineComparisonJson,
            HasBlockingRegression = request.HasBlockingRegression,
            ViolationCount = request.ViolationCount,
            TriggeredBy = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.EvalReports.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.EvalReportPersisted,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: entity.Id.ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Eval report persisted: {request.RunId} ({request.RunType}), {request.TotalCases} cases, {request.ViolationCount} violations, blocking={request.HasBlockingRegression}"), ct);

        _logger.LogInformation(
            "Eval report persisted. TenantId={TenantId}, RunId={RunId}, RunType={RunType}, Cases={Cases}, Violations={Violations}",
            tenantId, request.RunId, request.RunType, request.TotalCases, request.ViolationCount);

        var detail = MapToDetail(entity);

        // Send webhook notification if configured and report has issues (P3-007).
        if (_notificationService is not null && (request.HasBlockingRegression || request.ViolationCount > 0))
        {
            try
            {
                var payload = new EvalNotificationPayload
                {
                    RunId = request.RunId,
                    RunType = request.RunType,
                    TotalCases = request.TotalCases,
                    SuccessfulCases = request.SuccessfulCases,
                    FailedCases = request.FailedCases,
                    HasBlockingRegression = request.HasBlockingRegression,
                    ViolationCount = request.ViolationCount,
                    Violations = detail.Violations,
                    BaselineComparison = detail.BaselineComparison,
                };

                var sent = await _notificationService.NotifyAsync(payload, ct);
                var notifyEventType = sent ? AuditEventTypes.EvalNotificationSent : AuditEventTypes.EvalNotificationFailed;
                await _audit.WriteAsync(new AuditEvent(
                    EventId: Guid.NewGuid().ToString(),
                    EventType: notifyEventType,
                    TenantId: tenantId,
                    ActorId: actorId,
                    CorrelationId: entity.Id.ToString(),
                    Timestamp: DateTimeOffset.UtcNow,
                    Detail: $"Eval notification {(sent ? "sent" : "failed")}: {request.RunId} ({request.RunType}), {request.ViolationCount} violations, blocking={request.HasBlockingRegression}"), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to send eval notification for RunId={RunId}", request.RunId);
            }
        }

        return detail;
    }

    public async Task<EvalReportListResponse> ListReportsAsync(
        string tenantId,
        string? runType = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _db.EvalReports.Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(runType))
            query = query.Where(r => r.RunType == runType);

        var totalCount = await query.CountAsync(ct);

        // Load all and sort in memory (DateTimeOffset ordering not translatable to SQLite).
        var allReports = await query.ToListAsync(ct);
        var reports = allReports
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToSummary)
            .ToList();

        return new EvalReportListResponse
        {
            Reports = reports,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount,
        };
    }

    public async Task<EvalReportDetail?> GetReportAsync(
        string tenantId,
        Guid reportId,
        CancellationToken ct = default)
    {
        var entity = await _db.EvalReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.TenantId == tenantId, ct);

        return entity is null ? null : MapToDetail(entity);
    }

    private static EvalReportSummary MapToSummary(EvalReportEntity entity)
    {
        return new EvalReportSummary
        {
            Id = entity.Id,
            RunId = entity.RunId,
            RunType = entity.RunType,
            TotalCases = entity.TotalCases,
            SuccessfulCases = entity.SuccessfulCases,
            FailedCases = entity.FailedCases,
            HasBlockingRegression = entity.HasBlockingRegression,
            ViolationCount = entity.ViolationCount,
            TriggeredBy = entity.TriggeredBy,
            CreatedAt = entity.CreatedAt,
        };
    }

    private static EvalReportDetail MapToDetail(EvalReportEntity entity)
    {
        var metrics = DeserializeMetrics(entity.MetricsJson);
        var violations = DeserializeViolations(entity.ViolationsJson);
        var baseline = DeserializeBaseline(entity.BaselineComparisonJson);

        return new EvalReportDetail
        {
            Id = entity.Id,
            RunId = entity.RunId,
            RunType = entity.RunType,
            TotalCases = entity.TotalCases,
            SuccessfulCases = entity.SuccessfulCases,
            FailedCases = entity.FailedCases,
            HasBlockingRegression = entity.HasBlockingRegression,
            ViolationCount = entity.ViolationCount,
            TriggeredBy = entity.TriggeredBy,
            CreatedAt = entity.CreatedAt,
            Metrics = metrics,
            Violations = violations,
            BaselineComparison = baseline,
        };
    }

    internal static EvalMetricsDto DeserializeMetrics(string json, ILogger? logger = null) =>
        JsonDeserializeHelper.Deserialize(json, SharedJsonOptions.CamelCaseWrite, logger!, new EvalMetricsDto());

    internal static IReadOnlyList<EvalViolationDto> DeserializeViolations(string? json, ILogger? logger = null) =>
        JsonDeserializeHelper.Deserialize<List<EvalViolationDto>>(json, SharedJsonOptions.CamelCaseWrite, logger!, []);

    internal static EvalBaselineComparisonDto? DeserializeBaseline(string? json, ILogger? logger = null) =>
        JsonDeserializeHelper.DeserializeOrNull<EvalBaselineComparisonDto>(json, SharedJsonOptions.CamelCaseWrite, logger!);
}
