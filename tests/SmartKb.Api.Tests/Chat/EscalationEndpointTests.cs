using System.Net;
using System.Net.Http.Json;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Chat;

public class EscalationEndpointTests : IAsyncLifetime
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

    private async Task<(Guid SessionId, Guid MessageId)> CreateSessionWithMessageAsync()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        var session = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var sendResponse = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/messages",
            new SendMessageRequest { Query = "I need help with login errors" });
        var chatResult = (await sendResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;

        return (session.SessionId, chatResult.AssistantMessage.MessageId);
    }

    private CreateEscalationDraftRequest MakeRequest(Guid sessionId, Guid messageId) => new()
    {
        SessionId = sessionId,
        MessageId = messageId,
        Title = "Login Failure Escalation",
        CustomerSummary = "Customer cannot log in to portal.",
        StepsToReproduce = "1. Navigate to login. 2. Enter valid creds. 3. 500 error.",
        LogsIdsRequested = "correlation-id-abc",
        SuspectedComponent = "Auth Service",
        Severity = "P2",
        EvidenceLinks =
        [
            new CitationDto
            {
                ChunkId = "test_chunk_0",
                EvidenceId = "ev-1",
                Title = "Login Error",
                SourceUrl = "https://example.com/login-error",
                SourceSystem = "Wiki",
                Snippet = "Login failure observed.",
                UpdatedAt = DateTimeOffset.UtcNow,
                AccessLabel = "Internal",
            }
        ],
        TargetTeam = "Auth Team",
        Reason = "Repeated login failures affecting enterprise customers.",
    };

    [Fact]
    public async Task CreateDraft_ReturnsCreated()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();
        var request = MakeRequest(sessionId, messageId);

        var response = await _client.PostAsJsonAsync("/api/escalations/draft", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Login Failure Escalation", body.Data!.Title);
        Assert.Equal("P2", body.Data.Severity);
        Assert.Equal("Auth Team", body.Data.TargetTeam);
        Assert.Single(body.Data.EvidenceLinks);
    }

    [Fact]
    public async Task CreateDraft_ReturnsUnprocessable_WhenSessionNotFound()
    {
        var request = MakeRequest(Guid.NewGuid(), Guid.NewGuid());
        var response = await _client.PostAsJsonAsync("/api/escalations/draft", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetDraft_ReturnsOk()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/escalations/draft", MakeRequest(sessionId, messageId));
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>())!.Data!;

        var response = await _client.GetAsync($"/api/escalations/draft/{created.DraftId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(created.DraftId, body.Data!.DraftId);
    }

    [Fact]
    public async Task GetDraft_ReturnsNotFound_WhenMissing()
    {
        var response = await _client.GetAsync($"/api/escalations/draft/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListDrafts_ReturnsSessionDrafts()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();
        await _client.PostAsJsonAsync("/api/escalations/draft", MakeRequest(sessionId, messageId));

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/escalations/drafts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(1, body.Data!.TotalCount);
        Assert.Equal(sessionId, body.Data.SessionId);
    }

    [Fact]
    public async Task ListDrafts_ReturnsNotFound_WhenSessionMissing()
    {
        var response = await _client.GetAsync($"/api/sessions/{Guid.NewGuid()}/escalations/drafts");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDraft_ReturnsUpdated()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/escalations/draft", MakeRequest(sessionId, messageId));
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>())!.Data!;

        var update = new UpdateEscalationDraftRequest
        {
            Title = "Updated Escalation",
            Severity = "P1",
        };
        var response = await _client.PutAsJsonAsync($"/api/escalations/draft/{created.DraftId}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Updated Escalation", body.Data!.Title);
        Assert.Equal("P1", body.Data.Severity);
        Assert.Equal("Auth Team", body.Data.TargetTeam); // Unchanged.
    }

    [Fact]
    public async Task UpdateDraft_ReturnsNotFound_WhenMissing()
    {
        var update = new UpdateEscalationDraftRequest { Title = "x" };
        var response = await _client.PutAsJsonAsync($"/api/escalations/draft/{Guid.NewGuid()}", update);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExportDraft_ReturnsMarkdown()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/escalations/draft", MakeRequest(sessionId, messageId));
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>())!.Data!;

        var response = await _client.GetAsync($"/api/escalations/draft/{created.DraftId}/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftExportResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Contains("# Login Failure Escalation", body.Data!.Markdown);
        Assert.Contains("**Severity:** P2", body.Data.Markdown);
        Assert.Contains("## Customer Summary", body.Data.Markdown);
        Assert.Contains("## Steps to Reproduce", body.Data.Markdown);
        Assert.Contains("## Evidence Links", body.Data.Markdown);
        Assert.NotEqual(default, body.Data.ExportedAt);
    }

    [Fact]
    public async Task ExportDraft_ReturnsNotFound_WhenMissing()
    {
        var response = await _client.GetAsync($"/api/escalations/draft/{Guid.NewGuid()}/export");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDraft_SoftDeletes()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/escalations/draft", MakeRequest(sessionId, messageId));
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>())!.Data!;

        var deleteResponse = await _client.DeleteAsync($"/api/escalations/draft/{created.DraftId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/escalations/draft/{created.DraftId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteDraft_ReturnsNotFound_WhenMissing()
    {
        var response = await _client.DeleteAsync($"/api/escalations/draft/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EscalationEndpoints_RequireAuthentication()
    {
        var unauthClient = _factory.CreateClient();

        var response = await unauthClient.PostAsJsonAsync("/api/escalations/draft",
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var getResponse = await unauthClient.GetAsync($"/api/escalations/draft/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
    }

    [Fact]
    public async Task EscalationEndpoints_RequireChatQueryPermission()
    {
        var viewerClient = _factory.CreateAuthenticatedClient(roles: "EngineeringViewer");

        var response = await viewerClient.PostAsJsonAsync("/api/escalations/draft",
            MakeRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DraftNotVisibleToOtherTenant()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/escalations/draft", MakeRequest(sessionId, messageId));
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>())!.Data!;

        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", userId: "other-user");
        var getResponse = await otherClient.GetAsync($"/api/escalations/draft/{created.DraftId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}
