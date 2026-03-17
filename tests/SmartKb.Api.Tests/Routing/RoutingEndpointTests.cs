using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartKb.Api.Tests.Auth;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Routing;

public class RoutingEndpointTests : IAsyncLifetime
{
    private readonly AuthTestFactory _factory = new();
    private HttpClient _adminClient = null!;
    private HttpClient _agentClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = CreateClient("Admin");
        _agentClient = CreateClient("SupportAgent");
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _agentClient.Dispose();
        await _factory.DisposeAsync();
    }

    private HttpClient CreateClient(string role, string tenant = "tenant-1")
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, role);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenant);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, "test-user");
        return client;
    }

    // --- Routing Rules CRUD ---

    [Fact]
    public async Task CreateRoutingRule_ReturnsCreated()
    {
        var request = new CreateRoutingRuleRequest
        {
            ProductArea = "Billing",
            TargetTeam = "Finance",
            EscalationThreshold = 0.35f,
            MinSeverity = "P1",
        };
        var response = await _adminClient.PostAsJsonAsync("/api/admin/routing-rules", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RoutingRuleDto>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Billing", body.Data!.ProductArea);
        Assert.Equal("Finance", body.Data.TargetTeam);
        Assert.Equal(0.35f, body.Data.EscalationThreshold);
        Assert.Equal("P1", body.Data.MinSeverity);
    }

    [Fact]
    public async Task ListRoutingRules_ReturnsAll()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/routing-rules",
            new CreateRoutingRuleRequest { ProductArea = "A1", TargetTeam = "T1" });
        await _adminClient.PostAsJsonAsync("/api/admin/routing-rules",
            new CreateRoutingRuleRequest { ProductArea = "B1", TargetTeam = "T2" });

        var response = await _adminClient.GetAsync("/api/admin/routing-rules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RoutingRuleListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.True(body.Data!.TotalCount >= 2);
    }

    [Fact]
    public async Task UpdateRoutingRule_ReturnsUpdated()
    {
        var createResp = await _adminClient.PostAsJsonAsync("/api/admin/routing-rules",
            new CreateRoutingRuleRequest { ProductArea = "U1", TargetTeam = "Old" });
        var created = (await createResp.Content.ReadFromJsonAsync<ApiResponse<RoutingRuleDto>>())!.Data!;

        var updateResp = await _adminClient.PutAsJsonAsync(
            $"/api/admin/routing-rules/{created.RuleId}",
            new UpdateRoutingRuleRequest { TargetTeam = "New" });

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = (await updateResp.Content.ReadFromJsonAsync<ApiResponse<RoutingRuleDto>>())!.Data!;
        Assert.Equal("New", updated.TargetTeam);
    }

    [Fact]
    public async Task DeleteRoutingRule_ReturnsOk()
    {
        var createResp = await _adminClient.PostAsJsonAsync("/api/admin/routing-rules",
            new CreateRoutingRuleRequest { ProductArea = "D1", TargetTeam = "T1" });
        var created = (await createResp.Content.ReadFromJsonAsync<ApiResponse<RoutingRuleDto>>())!.Data!;

        var deleteResp = await _adminClient.DeleteAsync($"/api/admin/routing-rules/{created.RuleId}");
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        var getResp = await _adminClient.GetAsync($"/api/admin/routing-rules/{created.RuleId}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task RoutingRules_SupportAgent_Returns403()
    {
        var response = await _agentClient.GetAsync("/api/admin/routing-rules");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RoutingRules_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/routing-rules");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Analytics ---

    [Fact]
    public async Task GetAnalytics_ReturnsOk()
    {
        var response = await _adminClient.GetAsync("/api/admin/routing/analytics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RoutingAnalyticsSummary>>();
        Assert.True(body!.IsSuccess);
        Assert.NotNull(body.Data);
        Assert.Equal("tenant-1", body.Data!.TenantId);
    }

    [Fact]
    public async Task GetAnalytics_WithWindowDays_ReturnsOk()
    {
        var response = await _adminClient.GetAsync("/api/admin/routing/analytics?windowDays=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Recommendations ---

    [Fact]
    public async Task GenerateRecommendations_ReturnsOk()
    {
        var response = await _adminClient.PostAsync("/api/admin/routing/recommendations/generate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RoutingRecommendationListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.NotNull(body.Data);
    }

    [Fact]
    public async Task GetRecommendations_ReturnsOk()
    {
        var response = await _adminClient.GetAsync("/api/admin/routing/recommendations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<RoutingRecommendationListResponse>>();
        Assert.True(body!.IsSuccess);
    }

    [Fact]
    public async Task GetRecommendations_WithStatusFilter_ReturnsOk()
    {
        var response = await _adminClient.GetAsync("/api/admin/routing/recommendations?status=Pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApplyRecommendation_NotFound_Returns404()
    {
        var response = await _adminClient.PostAsJsonAsync(
            $"/api/admin/routing/recommendations/{Guid.NewGuid()}/apply",
            new ApplyRecommendationRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DismissRecommendation_NotFound_Returns404()
    {
        var response = await _adminClient.PostAsync(
            $"/api/admin/routing/recommendations/{Guid.NewGuid()}/dismiss", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Analytics_SupportAgent_Returns403()
    {
        var response = await _agentClient.GetAsync("/api/admin/routing/analytics");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
