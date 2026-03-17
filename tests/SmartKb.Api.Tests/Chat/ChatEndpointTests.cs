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

public class ChatEndpointTests : IAsyncLifetime
{
    private readonly ChatTestFactory _factory = new();
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
    public async Task PostChat_ReturnsOk_WithStructuredResponse()
    {
        var request = new ChatRequest { Query = "How do I reset my password?" };
        var response = await _client.PostAsJsonAsync("/api/chat", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatResponse>>();
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.Equal("final_answer", body.Data!.ResponseType);
        Assert.Equal("High", body.Data.ConfidenceLabel);
        Assert.True(body.Data.HasEvidence);
        Assert.NotEmpty(body.Data.Answer);
    }

    [Fact]
    public async Task PostChat_RequiresAuthentication()
    {
        var unauthClient = _factory.CreateClient();
        var request = new ChatRequest { Query = "test" };
        var response = await unauthClient.PostAsJsonAsync("/api/chat", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_RequiresChatQueryPermission()
    {
        // EngineeringViewer does NOT have chat:query permission
        var viewerClient = _factory.CreateAuthenticatedClient(roles: "EngineeringViewer");
        var request = new ChatRequest { Query = "test" };
        var response = await viewerClient.PostAsJsonAsync("/api/chat", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_SupportLead_HasAccess()
    {
        var leadClient = _factory.CreateAuthenticatedClient(roles: "SupportLead");
        var request = new ChatRequest { Query = "How do I fix a timeout error?" };
        var response = await leadClient.PostAsJsonAsync("/api/chat", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_Admin_HasAccess()
    {
        var adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        var request = new ChatRequest { Query = "What is the deployment process?" };
        var response = await adminClient.PostAsJsonAsync("/api/chat", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// Stub orchestrator for endpoint integration tests. Returns a deterministic response.
/// </summary>
internal sealed class StubChatOrchestrator : IChatOrchestrator
{
    public Task<ChatResponse> OrchestrateAsync(
        string tenantId, string userId, string correlationId,
        ChatRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = $"Grounded answer for: {request.Query}",
            Citations =
            [
                new CitationDto
                {
                    ChunkId = "test_chunk_0",
                    EvidenceId = "test-ev-1",
                    Title = "Test Article",
                    SourceUrl = "https://example.com/article",
                    SourceSystem = "AzureDevOps",
                    Snippet = "Test snippet content",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    AccessLabel = "Internal",
                }
            ],
            Confidence = 0.85f,
            ConfidenceLabel = "High",
            NextSteps = ["Check the documentation for further details."],
            Escalation = null,
            TraceId = correlationId,
            HasEvidence = true,
            SystemPromptVersion = "1.0",
        });
    }
}

internal sealed class ChatTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
            services.AddSingleton(new SessionSettings());
            services.AddSingleton(new EscalationSettings());
            services.AddSingleton<ISecretProvider>(new InMemorySecretProvider());
            services.AddScoped<IEscalationDraftService, EscalationDraftService>();
            services.AddScoped<IFeedbackService, FeedbackService>();
            services.AddScoped<IOutcomeService, OutcomeService>();
            services.AddSingleton(new DistillationSettings());
            services.AddScoped<IPatternDistillationService, PatternDistillationService>();
            services.AddScoped<IPatternGovernanceService, PatternGovernanceService>();
            services.AddScoped<IAuditEventQueryService, AuditEventQueryService>();
            services.AddScoped<ConnectorAdminService>();
            services.AddScoped<AdoWebhookHandler>();
            services.AddScoped<SharePointWebhookHandler>();

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
    }
}
