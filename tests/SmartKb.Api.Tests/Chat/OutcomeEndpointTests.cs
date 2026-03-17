using System.Net;
using System.Net.Http.Json;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Chat;

public class OutcomeEndpointTests : IAsyncLifetime
{
    private readonly SessionTestFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateAuthenticatedClient(roles: "SupportAgent");
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> CreateSessionAsync()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        var session = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;
        return session.SessionId;
    }

    [Fact]
    public async Task RecordOutcome_ResolvedWithoutEscalation_ReturnsCreated()
    {
        var sessionId = await CreateSessionAsync();

        var request = new RecordOutcomeRequest
        {
            ResolutionType = ResolutionType.ResolvedWithoutEscalation,
        };
        var response = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/outcome", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OutcomeResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("ResolvedWithoutEscalation", body.Data!.ResolutionType);
        Assert.Equal(sessionId, body.Data.SessionId);
        Assert.NotEqual(Guid.Empty, body.Data.OutcomeId);
    }

    [Fact]
    public async Task RecordOutcome_Escalated_WithFields_ReturnsCreated()
    {
        var sessionId = await CreateSessionAsync();

        var request = new RecordOutcomeRequest
        {
            ResolutionType = ResolutionType.Escalated,
            TargetTeam = "Engineering",
            Acceptance = true,
            EscalationTraceId = "trace-1",
        };
        var response = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/outcome", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OutcomeResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Escalated", body.Data!.ResolutionType);
        Assert.Equal("Engineering", body.Data.TargetTeam);
        Assert.True(body.Data.Acceptance);
        Assert.Equal("trace-1", body.Data.EscalationTraceId);
    }

    [Fact]
    public async Task RecordOutcome_Returns422_WhenSessionNotFound()
    {
        var request = new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated };
        var response = await _client.PostAsJsonAsync($"/api/sessions/{Guid.NewGuid()}/outcome", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetOutcomes_ReturnsRecordedOutcomes()
    {
        var sessionId = await CreateSessionAsync();

        await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/outcome",
            new RecordOutcomeRequest { ResolutionType = ResolutionType.ResolvedWithoutEscalation });

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/outcome");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OutcomeListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(1, body.Data!.TotalCount);
        Assert.Equal(sessionId, body.Data.SessionId);
    }

    [Fact]
    public async Task GetOutcomes_ReturnsNotFound_WhenSessionMissing()
    {
        var response = await _client.GetAsync($"/api/sessions/{Guid.NewGuid()}/outcome");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OutcomeEndpoints_RequireAuthentication()
    {
        var unauthClient = _factory.CreateClient();

        var postResponse = await unauthClient.PostAsJsonAsync(
            $"/api/sessions/{Guid.NewGuid()}/outcome",
            new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated });
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        var getResponse = await unauthClient.GetAsync(
            $"/api/sessions/{Guid.NewGuid()}/outcome");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
    }

    [Fact]
    public async Task OutcomeEndpoints_RequireChatOutcomePermission()
    {
        var viewerClient = _factory.CreateAuthenticatedClient(roles: "EngineeringViewer");

        var response = await viewerClient.PostAsJsonAsync(
            $"/api/sessions/{Guid.NewGuid()}/outcome",
            new RecordOutcomeRequest { ResolutionType = ResolutionType.Escalated });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OutcomeNotVisibleToOtherTenant()
    {
        var sessionId = await CreateSessionAsync();

        await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/outcome",
            new RecordOutcomeRequest { ResolutionType = ResolutionType.ResolvedWithoutEscalation });

        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", userId: "other-user");
        var response = await otherClient.GetAsync($"/api/sessions/{sessionId}/outcome");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
