using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Connectors;
using SmartKb.Api.Tests.Auth;
using SmartKb.Api.Tests.Connectors;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Tests.Chat;

public class SessionEndpointTests : IAsyncLifetime
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

    [Fact]
    public async Task CreateSession_ReturnsCreated()
    {
        var request = new CreateSessionRequest { Title = "SSO issue", CustomerRef = "customer:contoso" };
        var response = await _client.PostAsJsonAsync("/api/sessions", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>();
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.Equal("SSO issue", body.Data!.Title);
        Assert.Equal("customer:contoso", body.Data.CustomerRef);
        Assert.Equal("tenant-1", body.Data.TenantId);
        Assert.Equal("test-user", body.Data.UserId);
        Assert.NotEqual(Guid.Empty, body.Data.SessionId);
        Assert.NotNull(body.Data.ExpiresAt);
    }

    [Fact]
    public async Task CreateSession_WithoutTitle_ReturnsCreated()
    {
        var request = new CreateSessionRequest();
        var response = await _client.PostAsJsonAsync("/api/sessions", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Null(body.Data!.Title);
    }

    [Fact]
    public async Task ListSessions_ReturnsOk_WithUserSessions()
    {
        // Create two sessions.
        await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { Title = "Session A" });
        await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { Title = "Session B" });

        var response = await _client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SessionListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.True(body.Data!.TotalCount >= 2);
    }

    [Fact]
    public async Task ListSessions_TenantIsolation_DoesNotLeakSessions()
    {
        // Create session as tenant-1 user.
        await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { Title = "Tenant 1 Session" });

        // List as tenant-2 user.
        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", userId: "other-user");
        var response = await otherClient.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SessionListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(0, body.Data!.TotalCount);
    }

    [Fact]
    public async Task GetSession_ReturnsOk_ForOwnSession()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { Title = "Test" });
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var response = await _client.GetAsync($"/api/sessions/{created.SessionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(created.SessionId, body.Data!.SessionId);
    }

    [Fact]
    public async Task GetSession_ReturnsNotFound_ForOtherUsersSession()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { Title = "Mine" });
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var otherClient = _factory.CreateAuthenticatedClient(userId: "other-user");
        var response = await otherClient.GetAsync($"/api/sessions/{created.SessionId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSession_SoftDeletes_ReturnsOk()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { Title = "To delete" });
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{created.SessionId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Session should no longer appear.
        var getResponse = await _client.GetAsync($"/api/sessions/{created.SessionId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteSession_ReturnsNotFound_ForNonexistent()
    {
        var response = await _client.DeleteAsync($"/api/sessions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_ReturnsNotFound_ForNonexistentSession()
    {
        var response = await _client.GetAsync($"/api/sessions/{Guid.NewGuid()}/messages");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_ReturnsEmpty_ForNewSession()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var response = await _client.GetAsync($"/api/sessions/{created.SessionId}/messages");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<MessageListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(0, body.Data!.TotalCount);
    }

    [Fact]
    public async Task SendMessage_PersistsUserAndAssistantMessages()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest { Title = "Chat session" });
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var sendRequest = new SendMessageRequest { Query = "How do I reset my password?" };
        var sendResponse = await _client.PostAsJsonAsync($"/api/sessions/{created.SessionId}/messages", sendRequest);
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var body = await sendResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>();
        Assert.True(body!.IsSuccess);

        // Verify user message.
        Assert.Equal("User", body.Data!.UserMessage.Role);
        Assert.Equal("How do I reset my password?", body.Data.UserMessage.Content);

        // Verify assistant message.
        Assert.Equal("Assistant", body.Data.AssistantMessage.Role);
        Assert.NotEmpty(body.Data.AssistantMessage.Content);
        Assert.NotNull(body.Data.AssistantMessage.ResponseType);
        Assert.NotNull(body.Data.AssistantMessage.ConfidenceLabel);

        // Verify chat response.
        Assert.NotEmpty(body.Data.ChatResponse.Answer);
        Assert.NotEmpty(body.Data.ChatResponse.TraceId);

        // Verify session updated.
        Assert.Equal(2, body.Data.Session.MessageCount);
    }

    [Fact]
    public async Task SendMessage_AutoTitlesSession_WhenNoTitleSet()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;
        Assert.Null(created.Title);

        var sendRequest = new SendMessageRequest { Query = "My first question" };
        var sendResponse = await _client.PostAsJsonAsync($"/api/sessions/{created.SessionId}/messages", sendRequest);
        var body = (await sendResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;

        Assert.Equal("My first question", body.Session.Title);
    }

    [Fact]
    public async Task SendMessage_ReturnsNotFound_ForNonexistentSession()
    {
        var sendRequest = new SendMessageRequest { Query = "test" };
        var response = await _client.PostAsJsonAsync($"/api/sessions/{Guid.NewGuid()}/messages", sendRequest);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SendMessage_FollowUp_CarriesSessionContext()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        // First message.
        await _client.PostAsJsonAsync($"/api/sessions/{created.SessionId}/messages",
            new SendMessageRequest { Query = "What is the deployment process?" });

        // Follow-up.
        var followUp = await _client.PostAsJsonAsync($"/api/sessions/{created.SessionId}/messages",
            new SendMessageRequest { Query = "Can you elaborate on step 2?" });

        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        var body = (await followUp.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;
        Assert.Equal(4, body.Session.MessageCount); // 2 user + 2 assistant
    }

    [Fact]
    public async Task SendMessage_PersistsCitations()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        await _client.PostAsJsonAsync($"/api/sessions/{created.SessionId}/messages",
            new SendMessageRequest { Query = "test with citations" });

        // Retrieve messages and check citations are persisted.
        var msgResponse = await _client.GetAsync($"/api/sessions/{created.SessionId}/messages");
        var msgBody = (await msgResponse.Content.ReadFromJsonAsync<ApiResponse<MessageListResponse>>())!.Data!;

        var assistantMsg = msgBody.Messages.FirstOrDefault(m => m.Role == "Assistant");
        Assert.NotNull(assistantMsg);
        Assert.NotNull(assistantMsg!.Citations);
        Assert.NotEmpty(assistantMsg.Citations!);
        Assert.Equal("test_chunk_0", assistantMsg.Citations![0].ChunkId);
    }

    [Fact]
    public async Task SessionEndpoints_RequireAuthentication()
    {
        var unauthClient = _factory.CreateClient();

        var createResult = await unauthClient.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, createResult.StatusCode);

        var listResult = await unauthClient.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, listResult.StatusCode);
    }

    [Fact]
    public async Task SessionEndpoints_RequireChatQueryPermission()
    {
        var viewerClient = _factory.CreateAuthenticatedClient(roles: "EngineeringViewer");

        var createResult = await viewerClient.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        Assert.Equal(HttpStatusCode.Forbidden, createResult.StatusCode);
    }
}

internal sealed class SessionTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "<test>",
                ["AzureAd:ClientId"] = "<test>",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            services.AddDbContext<SmartKbDbContext>(options =>
                options.UseSqlite(_connection));
            services.AddScoped<IAuditEventWriter, SqlAuditEventWriter>();
            services.AddScoped<IAnswerTraceWriter, SqlAnswerTraceWriter>();
            services.AddScoped<ISessionService, SessionService>();
            services.AddSingleton<ISyncJobPublisher, TestSyncJobPublisher>();
            services.AddSingleton(new WebhookSettings());
            services.AddSingleton(new SessionSettings { DefaultExpiryHours = 24 });
            services.AddSingleton(new EscalationSettings());
            services.AddSingleton<ISecretProvider>(new InMemorySecretProvider());
            services.AddScoped<IEscalationDraftService, EscalationDraftService>();
            services.AddScoped<IFeedbackService, FeedbackService>();
            services.AddScoped<IOutcomeService, OutcomeService>();
            services.AddScoped<IAuditEventQueryService, AuditEventQueryService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.DistillationSettings());
            services.AddScoped<IPatternDistillationService, PatternDistillationService>();
            services.AddScoped<IPatternGovernanceService, PatternGovernanceService>();
            services.AddScoped<ConnectorAdminService>();
            services.AddScoped<AdoWebhookHandler>();
            services.AddScoped<SharePointWebhookHandler>();
            services.AddSingleton(new RetrievalSettings());
            services.AddScoped<ITenantRetrievalSettingsService, TenantRetrievalSettingsService>();
            services.AddScoped<IWebhookStatusService, SmartKb.Data.Repositories.WebhookStatusService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.RoutingAnalyticsSettings());
            services.AddScoped<IRoutingRuleService, SmartKb.Data.Repositories.RoutingRuleService>();
            services.AddScoped<IRoutingAnalyticsService, SmartKb.Data.Repositories.RoutingAnalyticsService>();
            services.AddScoped<IRoutingImprovementService, SmartKb.Data.Repositories.RoutingImprovementService>();

            // Use stub orchestrator — no real OpenAI or search calls.
            services.AddScoped<IChatOrchestrator, StubChatOrchestrator>();
        });
    }

    public HttpClient CreateAuthenticatedClient(
        string tenantId = "tenant-1",
        string roles = "SupportAgent",
        string userId = "test-user")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    public async Task EnsureDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Tenants.AnyAsync(t => t.TenantId == "tenant-1"))
        {
            db.Tenants.Add(new TenantEntity
            {
                TenantId = "tenant-1",
                DisplayName = "Test Tenant",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        if (!await db.Tenants.AnyAsync(t => t.TenantId == "tenant-2"))
        {
            db.Tenants.Add(new TenantEntity
            {
                TenantId = "tenant-2",
                DisplayName = "Test Tenant 2",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }
}
