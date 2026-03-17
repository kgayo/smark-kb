using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IRoutingRuleService
{
    Task<RoutingRuleListResponse> GetRulesAsync(string tenantId, CancellationToken ct = default);
    Task<RoutingRuleDto?> GetRuleAsync(string tenantId, Guid ruleId, CancellationToken ct = default);
    Task<RoutingRuleDto> CreateRuleAsync(string tenantId, string userId, string correlationId, CreateRoutingRuleRequest request, CancellationToken ct = default);
    Task<RoutingRuleDto?> UpdateRuleAsync(string tenantId, string userId, string correlationId, Guid ruleId, UpdateRoutingRuleRequest request, CancellationToken ct = default);
    Task<bool> DeleteRuleAsync(string tenantId, string userId, string correlationId, Guid ruleId, CancellationToken ct = default);
}
