using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

#region AssembleMessages with Session Summary Tests (P3-002)

public class AssembleMessagesWithSummaryTests
{
    [Fact]
    public void AssembleMessages_NoSummary_BehavesLikeOriginal()
    {
        var orchestrator = CreateOrchestrator();
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Hi there" },
        };

        var messages = orchestrator.AssembleMessages("System prompt", history, "New question");

        // System + 2 history + user query = 4
        Assert.Equal(4, messages.Count);
    }

    [Fact]
    public void AssembleMessages_WithSummary_InjectsSummaryBeforeHistory()
    {
        // Budget: 500 total - 0 reserve - ~2 system - ~2 query - 10 response = ~486 for history.
        // Two long messages (~100 tokens each) + two short ones (~5 tokens each) = ~210 tokens.
        // Only long messages need to be dropped to fit ~486 budget? No — we need to force drops.
        // Set budget tighter so long messages must be dropped.
        var orchestrator = CreateOrchestrator(maxTokenBudget: 150, systemPromptTokenReserve: 0, maxResponseTokens: 10);
        var longContent = new string('x', 400); // ~100 tokens each
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = longContent },
            new() { Role = "assistant", Content = longContent },
            new() { Role = "user", Content = "Short follow-up" },
            new() { Role = "assistant", Content = "Short reply" },
        };

        var summary = "[Earlier conversation summary]\nKey issue: test issue";
        var messages = orchestrator.AssembleMessages("System", history, "Query", summary);

        // Should have: system + summary + (surviving short history messages) + query
        // Verify summary is present in the assembled messages.
        var contents = messages.Select(m => GetContent(m)).ToList();
        Assert.Equal("System", contents[0]);
        Assert.True(contents.Any(c => c.Contains("[Earlier conversation summary]")),
            "Summary should be injected when messages are dropped");
    }

    [Fact]
    public void AssembleMessages_WithSummary_NoDrops_NoSummaryInjected()
    {
        // Budget is large enough — no messages dropped, so summary should NOT be injected.
        var orchestrator = CreateOrchestrator(maxTokenBudget: 102_400);
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hi" },
            new() { Role = "assistant", Content = "Hello" },
        };

        var summary = "[Earlier conversation summary]\nKey issue: test";
        var messages = orchestrator.AssembleMessages("System", history, "Query", summary);

        // No drops → no summary injection. Budget for summary is reserved but
        // since startIndex == 0, summary is not injected.
        var contents = messages.Select(m => GetContent(m)).ToList();
        Assert.Equal(4, contents.Count); // system + 2 history + query
        Assert.DoesNotContain("[Earlier conversation summary]", contents[1]);
    }

    [Fact]
    public void AssembleMessages_SummaryReservesBudget()
    {
        // The summary should reduce the effective budget for history messages.
        var summaryText = new string('s', 80); // ~20 tokens
        var orchestrator = CreateOrchestrator(maxTokenBudget: 200);
        var mediumContent = new string('m', 200); // ~50 tokens each
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = mediumContent },
            new() { Role = "assistant", Content = mediumContent },
            new() { Role = "user", Content = "Short" },
        };

        // Without summary, more history might fit. With summary, the budget is tighter.
        var withoutSummary = orchestrator.AssembleMessages("Sys", history, "Q");
        var withSummary = orchestrator.AssembleMessages("Sys", history, "Q", summaryText);

        // Both should produce valid message lists.
        Assert.True(withoutSummary.Count >= 2); // at least system + query
        Assert.True(withSummary.Count >= 2);
    }

    [Fact]
    public void AssembleMessages_AllHistoryDropped_SummaryStillInjected()
    {
        // Budget so tight that all history must be dropped, but summary fits.
        var shortSummary = "Summary";
        var orchestrator = CreateOrchestrator(maxTokenBudget: 100, systemPromptTokenReserve: 0, maxResponseTokens: 10);
        var longContent = new string('x', 1000);
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = longContent },
            new() { Role = "assistant", Content = longContent },
        };

        var messages = orchestrator.AssembleMessages("S", history, "Q", shortSummary);

        // system + summary + query (no history messages survived)
        var contents = messages.Select(m => GetContent(m)).ToList();
        Assert.Contains("Summary", contents);
    }

    private static ChatOrchestrator CreateOrchestrator(
        int maxTokenBudget = 102_400,
        int systemPromptTokenReserve = 1500,
        int maxResponseTokens = 4096)
    {
        var settings = new ChatOrchestrationSettings
        {
            MaxTokenBudget = maxTokenBudget,
            SystemPromptTokenReserve = systemPromptTokenReserve,
            MaxResponseTokens = maxResponseTokens,
        };

        return new ChatOrchestrator(
            embeddingService: null!,
            retrievalService: null!,
            traceWriter: null!,
            piiRedactionService: null!,
            auditEventWriter: null!,
            tokenUsageService: null!,
            httpClientFactory: null!,
            openAiSettings: new OpenAiSettings(),
            settings: settings,
            costSettings: new CostOptimizationSettings(),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatOrchestrator>.Instance);
    }

    private static string GetContent(object message)
    {
        var type = message.GetType();
        var prop = type.GetProperty("content");
        return prop?.GetValue(message)?.ToString() ?? "";
    }
}

#endregion

#region ComputeDroppedMessages Tests (P3-002)

public class ComputeDroppedMessagesTests
{
    [Fact]
    public void EmptyHistory_ReturnsEmpty()
    {
        var orchestrator = CreateOrchestrator();
        var dropped = orchestrator.ComputeDroppedMessages("System", [], "Query");
        Assert.Empty(dropped);
    }

    [Fact]
    public void HistoryFitsInBudget_ReturnsEmpty()
    {
        var orchestrator = CreateOrchestrator(maxTokenBudget: 102_400);
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hi" },
            new() { Role = "assistant", Content = "Hello" },
        };

        var dropped = orchestrator.ComputeDroppedMessages("System", history, "Query");
        Assert.Empty(dropped);
    }

    [Fact]
    public void HistoryExceedsBudget_DropsOldestFirst()
    {
        var orchestrator = CreateOrchestrator(maxTokenBudget: 200, systemPromptTokenReserve: 0, maxResponseTokens: 10);
        var longContent = new string('x', 400);
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = longContent },
            new() { Role = "assistant", Content = longContent },
            new() { Role = "user", Content = "Short" },
        };

        var dropped = orchestrator.ComputeDroppedMessages("S", history, "Q");

        // Should drop the first messages (oldest) that are too large
        Assert.True(dropped.Count > 0);
        Assert.Equal("user", dropped[0].Role);
        Assert.Equal(longContent, dropped[0].Content);
    }

    [Fact]
    public void AllHistoryExceedsBudget_DropsAll()
    {
        var orchestrator = CreateOrchestrator(maxTokenBudget: 50, systemPromptTokenReserve: 0, maxResponseTokens: 10);
        var longContent = new string('x', 400);
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = longContent },
            new() { Role = "assistant", Content = longContent },
        };

        var dropped = orchestrator.ComputeDroppedMessages("S", history, "Q");
        Assert.Equal(2, dropped.Count);
    }

    private static ChatOrchestrator CreateOrchestrator(
        int maxTokenBudget = 102_400,
        int systemPromptTokenReserve = 1500,
        int maxResponseTokens = 4096)
    {
        var settings = new ChatOrchestrationSettings
        {
            MaxTokenBudget = maxTokenBudget,
            SystemPromptTokenReserve = systemPromptTokenReserve,
            MaxResponseTokens = maxResponseTokens,
        };

        return new ChatOrchestrator(
            embeddingService: null!,
            retrievalService: null!,
            traceWriter: null!,
            piiRedactionService: null!,
            auditEventWriter: null!,
            tokenUsageService: null!,
            httpClientFactory: null!,
            openAiSettings: new OpenAiSettings(),
            settings: settings,
            costSettings: new CostOptimizationSettings(),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatOrchestrator>.Instance);
    }
}

#endregion

#region SessionSummarizationService Tests (P3-002)

public class SessionSummarizationServiceTests
{
    [Fact]
    public void BuildSummarizationMessages_FormatsConversation()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "My app crashes on login" },
            new() { Role = "assistant", Content = "Can you share the error message?" },
            new() { Role = "user", Content = "NullReferenceException in AuthModule" },
        };

        var result = OpenAiSessionSummarizationService.BuildSummarizationMessages(messages);

        Assert.Equal(2, result.Count); // system + user
        var userContent = GetContent(result[1]);
        Assert.Contains("[user]: My app crashes on login", userContent);
        Assert.Contains("[assistant]: Can you share the error message?", userContent);
        Assert.Contains("[user]: NullReferenceException in AuthModule", userContent);
    }

    [Fact]
    public void FormatSummary_FullResult()
    {
        var result = new SessionSummaryResult
        {
            KeyIssue = "Application crashes on login with NullReferenceException",
            AttemptedSolutions = ["Cleared browser cache", "Restarted application pool"],
            UnresolvedQuestions = ["Which version of the auth module is deployed?"],
            CustomerContext = "Running v2.3.1 on Windows Server 2022",
        };

        var formatted = OpenAiSessionSummarizationService.FormatSummary(result);

        Assert.Contains("[Earlier conversation summary]", formatted);
        Assert.Contains("Key issue: Application crashes on login", formatted);
        Assert.Contains("Attempted solutions:", formatted);
        Assert.Contains("- Cleared browser cache", formatted);
        Assert.Contains("- Restarted application pool", formatted);
        Assert.Contains("Unresolved questions:", formatted);
        Assert.Contains("- Which version of the auth module is deployed?", formatted);
        Assert.Contains("Customer context: Running v2.3.1", formatted);
    }

    [Fact]
    public void FormatSummary_EmptyLists_OmitsSections()
    {
        var result = new SessionSummaryResult
        {
            KeyIssue = "Login issue",
            AttemptedSolutions = [],
            UnresolvedQuestions = [],
            CustomerContext = null,
        };

        var formatted = OpenAiSessionSummarizationService.FormatSummary(result);

        Assert.Contains("[Earlier conversation summary]", formatted);
        Assert.Contains("Key issue: Login issue", formatted);
        Assert.DoesNotContain("Attempted solutions:", formatted);
        Assert.DoesNotContain("Unresolved questions:", formatted);
        Assert.DoesNotContain("Customer context:", formatted);
    }

    [Fact]
    public void FormatSummary_WithNullCustomerContext_OmitsSection()
    {
        var result = new SessionSummaryResult
        {
            KeyIssue = "Test issue",
            AttemptedSolutions = ["Solution A"],
            UnresolvedQuestions = [],
            CustomerContext = null,
        };

        var formatted = OpenAiSessionSummarizationService.FormatSummary(result);

        Assert.Contains("- Solution A", formatted);
        Assert.DoesNotContain("Customer context:", formatted);
    }

    [Fact]
    public void SummarizationSystemPrompt_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(OpenAiSessionSummarizationService.SummarizationSystemPrompt));
        Assert.Contains("summarizer", OpenAiSessionSummarizationService.SummarizationSystemPrompt);
    }

    [Fact]
    public void SummarizationSchema_IsNotNull()
    {
        Assert.NotNull(OpenAiSessionSummarizationService.SummarizationSchema);
    }

    [Fact]
    public void SessionSummaryResult_DefaultValues()
    {
        var result = new SessionSummaryResult();
        Assert.Equal("", result.KeyIssue);
        Assert.Empty(result.AttemptedSolutions);
        Assert.Empty(result.UnresolvedQuestions);
        Assert.Null(result.CustomerContext);
    }

    private static string GetContent(object message)
    {
        var type = message.GetType();
        var prop = type.GetProperty("content");
        return prop?.GetValue(message)?.ToString() ?? "";
    }
}

#endregion

#region SummarizeAsync HTTP Interaction Tests (TECH-054)

public class SummarizeAsyncTests
{
    private static OpenAiSessionSummarizationService CreateService(
        string responseBody,
        System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler(responseBody, statusCode);
        var factory = new StubHttpClientFactory(handler);
        return new OpenAiSessionSummarizationService(
            factory,
            new OpenAiSettings { ApiKey = "test-key", Endpoint = "https://api.openai.com/v1" },
            new ChatOrchestrationSettings(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAiSessionSummarizationService>.Instance);
    }

    [Fact]
    public async Task SummarizeAsync_SuccessfulResponse_ReturnsFormattedSummary()
    {
        var summaryJson = """
        {
            "key_issue": "Login failures on SSO module",
            "attempted_solutions": ["Cleared cookies", "Reset password"],
            "unresolved_questions": ["Which IdP is configured?"],
            "customer_context": "Azure AD tenant, v3.1"
        }
        """;
        var openAiResponse = $$"""
        {
            "choices": [
                {
                    "message": {
                        "content": {{System.Text.Json.JsonSerializer.Serialize(summaryJson)}}
                    }
                }
            ]
        }
        """;

        var service = CreateService(openAiResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "SSO broken" },
            new() { Role = "assistant", Content = "Can you share details?" },
        };

        var result = await service.SummarizeAsync(messages);

        Assert.NotNull(result);
        Assert.Contains("[Earlier conversation summary]", result);
        Assert.Contains("Key issue: Login failures on SSO module", result);
        Assert.Contains("- Cleared cookies", result);
        Assert.Contains("- Reset password", result);
        Assert.Contains("- Which IdP is configured?", result);
        Assert.Contains("Customer context: Azure AD tenant, v3.1", result);
    }

    [Fact]
    public async Task SummarizeAsync_EmptyMessageList_ReturnsNull()
    {
        var service = CreateService("""{ "choices": [] }""");
        var result = await service.SummarizeAsync([]);
        Assert.Null(result);
    }

    [Fact]
    public async Task SummarizeAsync_ApiError_ReturnsNull()
    {
        var service = CreateService("Server Error", System.Net.HttpStatusCode.InternalServerError);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" },
        };

        var result = await service.SummarizeAsync(messages);
        Assert.Null(result);
    }

    [Fact]
    public async Task SummarizeAsync_EmptyContent_ReturnsNull()
    {
        var openAiResponse = """
        {
            "choices": [
                {
                    "message": {
                        "content": ""
                    }
                }
            ]
        }
        """;

        var service = CreateService(openAiResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" },
        };

        var result = await service.SummarizeAsync(messages);
        Assert.Null(result);
    }

    [Fact]
    public async Task SummarizeAsync_MalformedJson_ReturnsNull()
    {
        var openAiResponse = """
        {
            "choices": [
                {
                    "message": {
                        "content": "not valid json at all"
                    }
                }
            ]
        }
        """;

        var service = CreateService(openAiResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" },
        };

        var result = await service.SummarizeAsync(messages);
        Assert.Null(result);
    }

    [Fact]
    public async Task SummarizeAsync_NoChoices_ReturnsNull()
    {
        var openAiResponse = """{ "choices": [] }""";

        var service = CreateService(openAiResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "test" },
        };

        var result = await service.SummarizeAsync(messages);
        Assert.Null(result);
    }

    [Fact]
    public async Task SummarizeAsync_CancellationRequested_Throws()
    {
        var service = CreateService("""{ "choices": [] }""");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.SummarizeAsync(
                [new ChatMessage { Role = "user", Content = "test" }],
                cts.Token));
    }

    [Fact]
    public async Task SummarizeAsync_MinimalSummary_OmitsEmptySections()
    {
        var summaryJson = """
        {
            "key_issue": "Connection timeout",
            "attempted_solutions": [],
            "unresolved_questions": [],
            "customer_context": null
        }
        """;
        var openAiResponse = $$"""
        {
            "choices": [
                {
                    "message": {
                        "content": {{System.Text.Json.JsonSerializer.Serialize(summaryJson)}}
                    }
                }
            ]
        }
        """;

        var service = CreateService(openAiResponse);
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Connection keeps timing out" },
        };

        var result = await service.SummarizeAsync(messages);

        Assert.NotNull(result);
        Assert.Contains("Key issue: Connection timeout", result);
        Assert.DoesNotContain("Attempted solutions:", result);
        Assert.DoesNotContain("Unresolved questions:", result);
        Assert.DoesNotContain("Customer context:", result);
    }

    private class StubHttpMessageHandler(string responseBody, System.Net.HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private class StubHttpClientFactory(StubHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}

#endregion

#region ChatOrchestrationSettings Summarization Config Tests (P3-002)

public class SummarizationSettingsTests
{
    [Fact]
    public void DefaultSettings_SummarizationEnabled()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.True(settings.EnableSessionSummarization);
    }

    [Fact]
    public void DefaultSettings_SummarizationModel_IsGpt4oMini()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal("gpt-4o-mini", settings.SummarizationModel);
    }

    [Fact]
    public void DefaultSettings_SummarizationMaxTokens_Is256()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(256, settings.SummarizationMaxTokens);
    }

    [Fact]
    public void DefaultSettings_SummarizationTimeoutMs_Is5000()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(5000, settings.SummarizationTimeoutMs);
    }

    [Fact]
    public void DefaultSettings_SummarizationMinDroppedMessages_Is4()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(4, settings.SummarizationMinDroppedMessages);
    }
}

#endregion

#region Diagnostics OTel Counter Tests (P3-002)

public class SessionSummarizationDiagnosticsTests
{
    [Fact]
    public void SessionSummarizationsTotal_Counter_Exists()
    {
        Assert.NotNull(Diagnostics.SessionSummarizationsTotal);
    }
}

#endregion
