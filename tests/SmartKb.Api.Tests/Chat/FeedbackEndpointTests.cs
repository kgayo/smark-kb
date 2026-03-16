using System.Net;
using System.Net.Http.Json;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Chat;

public class FeedbackEndpointTests : IAsyncLifetime
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
            new SendMessageRequest { Query = "How do I reset a user's password?" });
        var chatResult = (await sendResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;

        return (session.SessionId, chatResult.AssistantMessage.MessageId);
    }

    [Fact]
    public async Task SubmitFeedback_ThumbsUp_ReturnsOk()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();

        var request = new SubmitFeedbackRequest
        {
            Type = Contracts.Enums.FeedbackType.ThumbsUp,
            ReasonCodes = [],
        };
        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<FeedbackResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("ThumbsUp", body.Data!.Type);
        Assert.Equal(messageId, body.Data.MessageId);
        Assert.Equal(sessionId, body.Data.SessionId);
        Assert.NotEqual(Guid.Empty, body.Data.FeedbackId);
    }

    [Fact]
    public async Task SubmitFeedback_ThumbsDown_WithReasonCodes_ReturnsOk()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();

        var request = new SubmitFeedbackRequest
        {
            Type = Contracts.Enums.FeedbackType.ThumbsDown,
            ReasonCodes = [Contracts.Enums.FeedbackReasonCode.WrongAnswer, Contracts.Enums.FeedbackReasonCode.OutdatedInfo],
            Comment = "The answer is wrong.",
            CorrectedAnswer = "The correct answer is ...",
        };
        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<FeedbackResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("ThumbsDown", body.Data!.Type);
        Assert.Equal(2, body.Data.ReasonCodes.Count);
        Assert.Contains("WrongAnswer", body.Data.ReasonCodes);
        Assert.Contains("OutdatedInfo", body.Data.ReasonCodes);
        Assert.Equal("The answer is wrong.", body.Data.Comment);
        Assert.Equal("The correct answer is ...", body.Data.CorrectedAnswer);
    }

    [Fact]
    public async Task SubmitFeedback_UpdatesExistingFeedback()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();

        // Submit thumbs up first.
        await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback",
            new SubmitFeedbackRequest { Type = Contracts.Enums.FeedbackType.ThumbsUp });

        // Then change to thumbs down.
        var request = new SubmitFeedbackRequest
        {
            Type = Contracts.Enums.FeedbackType.ThumbsDown,
            ReasonCodes = [Contracts.Enums.FeedbackReasonCode.TooVague],
        };
        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<FeedbackResponse>>();
        Assert.Equal("ThumbsDown", body!.Data!.Type);
        Assert.Contains("TooVague", body.Data.ReasonCodes);
    }

    [Fact]
    public async Task SubmitFeedback_Returns422_WhenSessionNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{Guid.NewGuid()}/messages/{Guid.NewGuid()}/feedback",
            new SubmitFeedbackRequest { Type = Contracts.Enums.FeedbackType.ThumbsUp });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task SubmitFeedback_Returns422_WhenMessageNotFound()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new CreateSessionRequest());
        var session = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{session.SessionId}/messages/{Guid.NewGuid()}/feedback",
            new SubmitFeedbackRequest { Type = Contracts.Enums.FeedbackType.ThumbsUp });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetFeedback_ReturnsExisting()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();

        await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback",
            new SubmitFeedbackRequest
            {
                Type = Contracts.Enums.FeedbackType.ThumbsDown,
                ReasonCodes = [Contracts.Enums.FeedbackReasonCode.MissingContext],
            });

        var response = await _client.GetAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<FeedbackResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal("ThumbsDown", body.Data!.Type);
        Assert.Contains("MissingContext", body.Data.ReasonCodes);
    }

    [Fact]
    public async Task GetFeedback_ReturnsNotFound_WhenNoFeedback()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();

        var response = await _client.GetAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListFeedbacks_ReturnsSessionFeedbacks()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();

        await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback",
            new SubmitFeedbackRequest { Type = Contracts.Enums.FeedbackType.ThumbsUp });

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/feedbacks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<FeedbackListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(1, body.Data!.TotalCount);
    }

    [Fact]
    public async Task ListFeedbacks_ReturnsNotFound_WhenSessionMissing()
    {
        var response = await _client.GetAsync($"/api/sessions/{Guid.NewGuid()}/feedbacks");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FeedbackEndpoints_RequireAuthentication()
    {
        var unauthClient = _factory.CreateClient();

        var postResponse = await unauthClient.PostAsJsonAsync(
            $"/api/sessions/{Guid.NewGuid()}/messages/{Guid.NewGuid()}/feedback",
            new SubmitFeedbackRequest { Type = Contracts.Enums.FeedbackType.ThumbsUp });
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        var getResponse = await unauthClient.GetAsync(
            $"/api/sessions/{Guid.NewGuid()}/messages/{Guid.NewGuid()}/feedback");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
    }

    [Fact]
    public async Task FeedbackEndpoints_RequireChatFeedbackPermission()
    {
        var viewerClient = _factory.CreateAuthenticatedClient(roles: "EngineeringViewer");

        var response = await viewerClient.PostAsJsonAsync(
            $"/api/sessions/{Guid.NewGuid()}/messages/{Guid.NewGuid()}/feedback",
            new SubmitFeedbackRequest { Type = Contracts.Enums.FeedbackType.ThumbsUp });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FeedbackNotVisibleToOtherTenant()
    {
        var (sessionId, messageId) = await CreateSessionWithMessageAsync();

        await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback",
            new SubmitFeedbackRequest { Type = Contracts.Enums.FeedbackType.ThumbsUp });

        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", userId: "other-user");
        var response = await otherClient.GetAsync(
            $"/api/sessions/{sessionId}/messages/{messageId}/feedback");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
