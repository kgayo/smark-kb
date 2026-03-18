using System.Net;
using System.Net.Http.Json;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.E2E;

/// <summary>
/// End-to-end test for the support agent journey:
/// create session → ask question → receive answer with citations →
/// follow-up question → submit feedback → create escalation draft →
/// edit escalation → export escalation → record outcome → verify session history.
/// </summary>
public class AgentJourneyE2ETests : IAsyncLifetime
{
    private readonly E2ETestFactory _factory = new();
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
    public async Task FullAgentJourney_SessionThroughOutcome()
    {
        // Step 1: Create a new support session.
        var createResponse = await _client.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest { Title = "Login SSO failure", CustomerRef = "customer:acme-corp" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var session = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;
        Assert.Equal("Login SSO failure", session.Title);
        Assert.Equal("customer:acme-corp", session.CustomerRef);
        Assert.Equal("tenant-1", session.TenantId);
        var sessionId = session.SessionId;

        // Step 2: Send first question — expect grounded answer with citations.
        var sendResponse = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/messages",
            new SendMessageRequest { Query = "Customer reports SSO login returns 500 error" });
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var chatResult = (await sendResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;
        Assert.Equal("User", chatResult.UserMessage.Role);
        Assert.Equal("Customer reports SSO login returns 500 error", chatResult.UserMessage.Content);
        Assert.Equal("Assistant", chatResult.AssistantMessage.Role);
        Assert.NotEmpty(chatResult.ChatResponse.Answer);
        Assert.NotEmpty(chatResult.ChatResponse.TraceId);
        Assert.True(chatResult.ChatResponse.HasEvidence);
        Assert.NotEmpty(chatResult.ChatResponse.Citations);
        Assert.Equal("High", chatResult.ChatResponse.ConfidenceLabel);
        Assert.Equal(2, chatResult.Session.MessageCount); // 1 user + 1 assistant

        // Session should have been auto-titled if no title was set; here we set one explicitly.
        Assert.Equal("Login SSO failure", chatResult.Session.Title);

        var firstAssistantMessageId = chatResult.AssistantMessage.MessageId;

        // Step 3: Send follow-up question — multi-turn context.
        var followUpResponse = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/messages",
            new SendMessageRequest { Query = "Can you elaborate on the SSO configuration steps?" });
        Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);

        var followUp = (await followUpResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;
        Assert.Equal(4, followUp.Session.MessageCount); // 2 user + 2 assistant

        // Step 4: Retrieve message history — verify all messages persisted with citations.
        var historyResponse = await _client.GetAsync($"/api/sessions/{sessionId}/messages");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var history = (await historyResponse.Content.ReadFromJsonAsync<ApiResponse<MessageListResponse>>())!.Data!;
        Assert.Equal(4, history.TotalCount);

        var assistantMessages = history.Messages.Where(m => m.Role == "Assistant").ToList();
        Assert.Equal(2, assistantMessages.Count);
        Assert.All(assistantMessages, m =>
        {
            Assert.NotNull(m.Citations);
            Assert.NotEmpty(m.Citations!);
            Assert.NotNull(m.ConfidenceLabel);
            Assert.NotNull(m.ResponseType);
        });

        // Step 5: Submit positive feedback on first answer.
        var feedbackResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{firstAssistantMessageId}/feedback",
            new SubmitFeedbackRequest
            {
                Type = FeedbackType.ThumbsUp,
                ReasonCodes = [],
            });
        Assert.Equal(HttpStatusCode.OK, feedbackResponse.StatusCode);

        var feedback = (await feedbackResponse.Content.ReadFromJsonAsync<ApiResponse<FeedbackResponse>>())!.Data!;
        Assert.Equal("ThumbsUp", feedback.Type);
        Assert.Equal(firstAssistantMessageId, feedback.MessageId);

        // Step 6: Change feedback to negative with reason codes (upsert).
        var negativeFeedbackResponse = await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/messages/{firstAssistantMessageId}/feedback",
            new SubmitFeedbackRequest
            {
                Type = FeedbackType.ThumbsDown,
                ReasonCodes = [FeedbackReasonCode.WrongAnswer, FeedbackReasonCode.MissingContext],
                Comment = "Answer missed the IdP configuration section.",
                CorrectedAnswer = "The SSO failure is caused by expired IdP certificate.",
            });
        Assert.Equal(HttpStatusCode.OK, negativeFeedbackResponse.StatusCode);

        var updatedFeedback = (await negativeFeedbackResponse.Content.ReadFromJsonAsync<ApiResponse<FeedbackResponse>>())!.Data!;
        Assert.Equal("ThumbsDown", updatedFeedback.Type);
        Assert.Equal(2, updatedFeedback.ReasonCodes.Count);
        Assert.Contains("WrongAnswer", updatedFeedback.ReasonCodes);
        Assert.Contains("MissingContext", updatedFeedback.ReasonCodes);
        Assert.Equal("Answer missed the IdP configuration section.", updatedFeedback.Comment);

        // Step 7: Verify feedback is retrievable.
        var getFeedbackResponse = await _client.GetAsync(
            $"/api/sessions/{sessionId}/messages/{firstAssistantMessageId}/feedback");
        Assert.Equal(HttpStatusCode.OK, getFeedbackResponse.StatusCode);

        var retrievedFeedback = (await getFeedbackResponse.Content.ReadFromJsonAsync<ApiResponse<FeedbackResponse>>())!.Data!;
        Assert.Equal("ThumbsDown", retrievedFeedback.Type);
        Assert.Equal(feedback.FeedbackId, retrievedFeedback.FeedbackId); // Same entity, updated.

        // Step 8: List session feedbacks.
        var listFeedbacksResponse = await _client.GetAsync($"/api/sessions/{sessionId}/feedbacks");
        Assert.Equal(HttpStatusCode.OK, listFeedbacksResponse.StatusCode);

        var feedbackList = (await listFeedbacksResponse.Content.ReadFromJsonAsync<ApiResponse<FeedbackListResponse>>())!.Data!;
        Assert.Equal(1, feedbackList.TotalCount); // Upserted, not duplicated.

        // Step 9: Create escalation draft from the assistant message.
        var escalationRequest = new CreateEscalationDraftRequest
        {
            SessionId = sessionId,
            MessageId = firstAssistantMessageId,
            Title = "SSO 500 Error - IdP Certificate Expired",
            CustomerSummary = "Enterprise customer Acme Corp cannot SSO into portal; 500 errors.",
            StepsToReproduce = "1. Navigate to portal. 2. Click SSO login. 3. 500 Internal Server Error.",
            LogsIdsRequested = "correlation-id-xyz-123",
            SuspectedComponent = "Auth Service",
            Severity = "P2",
            EvidenceLinks =
            [
                new CitationDto
                {
                    ChunkId = "test_chunk_0",
                    EvidenceId = "test-ev-1",
                    Title = "SSO Configuration Guide",
                    SourceUrl = "https://wiki.example.com/sso-config",
                    SourceSystem = "AzureDevOps",
                    Snippet = "SSO IdP certificate must be renewed annually.",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    AccessLabel = "Internal",
                },
            ],
            TargetTeam = "Identity Platform",
            Reason = "Repeated SSO failures impacting enterprise customers.",
        };

        var createDraftResponse = await _client.PostAsJsonAsync("/api/escalations/draft", escalationRequest);
        Assert.Equal(HttpStatusCode.Created, createDraftResponse.StatusCode);

        var draft = (await createDraftResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>())!.Data!;
        Assert.Equal("SSO 500 Error - IdP Certificate Expired", draft.Title);
        Assert.Equal("P2", draft.Severity);
        Assert.Equal("Identity Platform", draft.TargetTeam);
        Assert.Single(draft.EvidenceLinks);

        // Step 10: Edit escalation draft — upgrade severity.
        var updateDraftResponse = await _client.PutAsJsonAsync($"/api/escalations/draft/{draft.DraftId}",
            new UpdateEscalationDraftRequest
            {
                Severity = "P1",
                Title = "URGENT: SSO 500 Error - IdP Certificate Expired",
            });
        Assert.Equal(HttpStatusCode.OK, updateDraftResponse.StatusCode);

        var updatedDraft = (await updateDraftResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftResponse>>())!.Data!;
        Assert.Equal("P1", updatedDraft.Severity);
        Assert.Equal("URGENT: SSO 500 Error - IdP Certificate Expired", updatedDraft.Title);
        Assert.Equal("Identity Platform", updatedDraft.TargetTeam); // Unchanged.

        // Step 11: Export escalation as markdown.
        var exportResponse = await _client.GetAsync($"/api/escalations/draft/{draft.DraftId}/export");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);

        var export = (await exportResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftExportResponse>>())!.Data!;
        Assert.Contains("# URGENT: SSO 500 Error - IdP Certificate Expired", export.Markdown);
        Assert.Contains("**Severity:** P1", export.Markdown);
        Assert.Contains("## Customer Summary", export.Markdown);
        Assert.Contains("## Steps to Reproduce", export.Markdown);
        Assert.Contains("## Evidence Links", export.Markdown);
        Assert.Contains("SSO Configuration Guide", export.Markdown);
        Assert.NotEqual(default, export.ExportedAt);

        // Step 12: List escalation drafts for session.
        var listDraftsResponse = await _client.GetAsync($"/api/sessions/{sessionId}/escalations/drafts");
        Assert.Equal(HttpStatusCode.OK, listDraftsResponse.StatusCode);

        var draftList = (await listDraftsResponse.Content.ReadFromJsonAsync<ApiResponse<EscalationDraftListResponse>>())!.Data!;
        Assert.Equal(1, draftList.TotalCount);
        Assert.Equal(sessionId, draftList.SessionId);

        // Step 13: Record session outcome — escalated.
        var outcomeResponse = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/outcome",
            new RecordOutcomeRequest
            {
                ResolutionType = ResolutionType.Escalated,
                TargetTeam = "Identity Platform",
                Acceptance = true,
                EscalationTraceId = draft.DraftId.ToString(),
            });
        Assert.Equal(HttpStatusCode.Created, outcomeResponse.StatusCode);

        var outcome = (await outcomeResponse.Content.ReadFromJsonAsync<ApiResponse<OutcomeResponse>>())!.Data!;
        Assert.Equal("Escalated", outcome.ResolutionType);
        Assert.Equal("Identity Platform", outcome.TargetTeam);
        Assert.True(outcome.Acceptance);
        Assert.Equal(sessionId, outcome.SessionId);

        // Step 14: Verify outcomes retrievable.
        var getOutcomesResponse = await _client.GetAsync($"/api/sessions/{sessionId}/outcome");
        Assert.Equal(HttpStatusCode.OK, getOutcomesResponse.StatusCode);

        var outcomes = (await getOutcomesResponse.Content.ReadFromJsonAsync<ApiResponse<OutcomeListResponse>>())!.Data!;
        Assert.Equal(1, outcomes.TotalCount);

        // Step 15: Verify session is still accessible with all state intact.
        var finalSessionResponse = await _client.GetAsync($"/api/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, finalSessionResponse.StatusCode);

        var finalSession = (await finalSessionResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;
        Assert.Equal(sessionId, finalSession.SessionId);
        Assert.Equal("Login SSO failure", finalSession.Title);
        Assert.Equal(4, finalSession.MessageCount);

        // Step 16: Verify tenant isolation — another tenant cannot see anything.
        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", userId: "other-user");

        var otherSessionResponse = await otherClient.GetAsync($"/api/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, otherSessionResponse.StatusCode);

        var otherFeedbackResponse = await otherClient.GetAsync(
            $"/api/sessions/{sessionId}/messages/{firstAssistantMessageId}/feedback");
        Assert.Equal(HttpStatusCode.NotFound, otherFeedbackResponse.StatusCode);

        var otherDraftResponse = await otherClient.GetAsync($"/api/escalations/draft/{draft.DraftId}");
        Assert.Equal(HttpStatusCode.NotFound, otherDraftResponse.StatusCode);

        var otherOutcomeResponse = await otherClient.GetAsync($"/api/sessions/{sessionId}/outcome");
        Assert.Equal(HttpStatusCode.NotFound, otherOutcomeResponse.StatusCode);

        otherClient.Dispose();
    }

    [Fact]
    public async Task AgentJourney_ResolvedWithoutEscalation()
    {
        // Shorter journey: ask question → get answer → positive feedback → resolve.
        var createResponse = await _client.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest { CustomerRef = "customer:simple" });
        var session = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        // Ask question — auto-titles session.
        var sendResponse = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/messages",
            new SendMessageRequest { Query = "How do I configure SAML?" });
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var chatResult = (await sendResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;
        Assert.Equal("How do I configure SAML?", chatResult.Session.Title); // Auto-titled.
        Assert.NotEmpty(chatResult.ChatResponse.Citations);

        // Positive feedback.
        await _client.PostAsJsonAsync(
            $"/api/sessions/{session.SessionId}/messages/{chatResult.AssistantMessage.MessageId}/feedback",
            new SubmitFeedbackRequest { Type = FeedbackType.ThumbsUp, ReasonCodes = [] });

        // Resolve without escalation.
        var outcomeResponse = await _client.PostAsJsonAsync($"/api/sessions/{session.SessionId}/outcome",
            new RecordOutcomeRequest { ResolutionType = ResolutionType.ResolvedWithoutEscalation });
        Assert.Equal(HttpStatusCode.Created, outcomeResponse.StatusCode);

        var outcome = (await outcomeResponse.Content.ReadFromJsonAsync<ApiResponse<OutcomeResponse>>())!.Data!;
        Assert.Equal("ResolvedWithoutEscalation", outcome.ResolutionType);

        // Delete session — soft delete.
        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Session no longer accessible.
        var getResponse = await _client.GetAsync($"/api/sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task AgentJourney_MultipleSessionsIsolated()
    {
        // Create two sessions for the same user — verify they don't interfere.
        var session1Response = await _client.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest { Title = "Session Alpha" });
        var session1 = (await session1Response.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var session2Response = await _client.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest { Title = "Session Beta" });
        var session2 = (await session2Response.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        // Send message to session 1.
        await _client.PostAsJsonAsync($"/api/sessions/{session1.SessionId}/messages",
            new SendMessageRequest { Query = "Question for alpha" });

        // Send message to session 2.
        await _client.PostAsJsonAsync($"/api/sessions/{session2.SessionId}/messages",
            new SendMessageRequest { Query = "Question for beta" });

        // Verify each session has its own messages.
        var msgs1 = (await (await _client.GetAsync($"/api/sessions/{session1.SessionId}/messages"))
            .Content.ReadFromJsonAsync<ApiResponse<MessageListResponse>>())!.Data!;
        Assert.Equal(2, msgs1.TotalCount);
        Assert.Contains(msgs1.Messages, m => m.Content == "Question for alpha");

        var msgs2 = (await (await _client.GetAsync($"/api/sessions/{session2.SessionId}/messages"))
            .Content.ReadFromJsonAsync<ApiResponse<MessageListResponse>>())!.Data!;
        Assert.Equal(2, msgs2.TotalCount);
        Assert.Contains(msgs2.Messages, m => m.Content == "Question for beta");

        // List sessions — both visible.
        var listResponse = await _client.GetAsync("/api/sessions");
        var sessionList = (await listResponse.Content.ReadFromJsonAsync<ApiResponse<SessionListResponse>>())!.Data!;
        Assert.True(sessionList.TotalCount >= 2);
    }
}
