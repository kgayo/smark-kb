using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartKb.Api.Tests.Auth;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Routing;

public class PlaybookEndpointTests : IAsyncLifetime
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

    // --- CRUD ---

    [Fact]
    public async Task CreatePlaybook_ReturnsCreated()
    {
        var request = new CreateTeamPlaybookRequest
        {
            TeamName = "Billing-C",
            Description = "Billing team playbook",
            RequiredFields = ["CustomerSummary"],
            Checklist = ["Verify account"],
            ContactChannel = "#billing-oncall",
            RequiresApproval = true,
            MinSeverity = "P2",
        };
        var response = await _adminClient.PostAsJsonAsync("/api/admin/playbooks", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("Billing-C", body.Data!.TeamName);
        Assert.Equal("Billing team playbook", body.Data.Description);
        Assert.Contains("CustomerSummary", body.Data.RequiredFields);
        Assert.True(body.Data.RequiresApproval);
    }

    [Fact]
    public async Task ListPlaybooks_ReturnsAll()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "ListA" });
        await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "ListB" });

        var response = await _adminClient.GetAsync("/api/admin/playbooks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.True(body.Data!.TotalCount >= 2);
    }

    [Fact]
    public async Task GetPlaybook_ById_ReturnsSpecific()
    {
        var createRes = await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "GetById" });
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>())!.Data!;

        var response = await _adminClient.GetAsync($"/api/admin/playbooks/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>();
        Assert.Equal("GetById", body!.Data!.TeamName);
    }

    [Fact]
    public async Task GetPlaybook_ByTeamName_ReturnsSpecific()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "GetByTeam" });

        var response = await _adminClient.GetAsync("/api/admin/playbooks/team/GetByTeam");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>();
        Assert.Equal("GetByTeam", body!.Data!.TeamName);
    }

    [Fact]
    public async Task GetPlaybook_NotFound_Returns404()
    {
        var response = await _adminClient.GetAsync($"/api/admin/playbooks/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePlaybook_ReturnsUpdated()
    {
        var createRes = await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "UpdateMe", Description = "Original" });
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>())!.Data!;

        var response = await _adminClient.PutAsJsonAsync($"/api/admin/playbooks/{created.Id}",
            new UpdateTeamPlaybookRequest { Description = "Updated", RequiresApproval = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>();
        Assert.Equal("Updated", body!.Data!.Description);
        Assert.True(body.Data.RequiresApproval);
    }

    [Fact]
    public async Task DeletePlaybook_ReturnsOk()
    {
        var createRes = await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "DeleteMe" });
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>())!.Data!;

        var response = await _adminClient.DeleteAsync($"/api/admin/playbooks/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify it's gone.
        var getResponse = await _adminClient.GetAsync($"/api/admin/playbooks/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // --- Permission Tests ---

    [Fact]
    public async Task CreatePlaybook_SupportAgent_Forbidden()
    {
        var response = await _agentClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "X" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListPlaybooks_SupportAgent_Forbidden()
    {
        var response = await _agentClient.GetAsync("/api/admin/playbooks");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Tenant Isolation ---

    [Fact]
    public async Task GetPlaybook_DifferentTenant_NotFound()
    {
        var createRes = await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest { TeamName = "TenantIso" });
        var created = (await createRes.Content.ReadFromJsonAsync<ApiResponse<TeamPlaybookDto>>())!.Data!;

        var otherClient = CreateClient("Admin", "tenant-other");
        var response = await otherClient.GetAsync($"/api/admin/playbooks/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        otherClient.Dispose();
    }

    // --- Validation Endpoint ---

    [Fact]
    public async Task ValidatePlaybook_ReturnsValidationResult()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/playbooks",
            new CreateTeamPlaybookRequest
            {
                TeamName = "ValidateTeam",
                RequiredFields = ["Title", "CustomerSummary"],
                Checklist = ["Step 1"],
                RequiresApproval = true,
                ContactChannel = "#test",
            });

        var validateRequest = new PlaybookValidateRequest
        {
            TargetTeam = "ValidateTeam",
            Draft = new CreateEscalationDraftRequest
            {
                SessionId = Guid.NewGuid(),
                MessageId = Guid.NewGuid(),
                Title = "Has title",
                // CustomerSummary missing
            },
        };

        var response = await _agentClient.PostAsJsonAsync("/api/admin/playbooks/validate", validateRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PlaybookValidationResult>>();
        Assert.True(body!.IsSuccess);
        Assert.False(body.Data!.IsValid);
        Assert.Contains("CustomerSummary", body.Data.MissingRequiredFields);
        Assert.DoesNotContain("Title", body.Data.MissingRequiredFields);
        Assert.True(body.Data.RequiresApproval);
        Assert.Equal("#test", body.Data.ContactChannel);
        Assert.Equal(["Step 1"], body.Data.Checklist);
    }

    [Fact]
    public async Task ValidatePlaybook_NoPlaybook_ReturnsValid()
    {
        var validateRequest = new PlaybookValidateRequest
        {
            TargetTeam = "NonExistent",
            Draft = new CreateEscalationDraftRequest
            {
                SessionId = Guid.NewGuid(),
                MessageId = Guid.NewGuid(),
            },
        };

        var response = await _agentClient.PostAsJsonAsync("/api/admin/playbooks/validate", validateRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PlaybookValidationResult>>();
        Assert.True(body!.Data!.IsValid);
    }
}
