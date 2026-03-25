using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Summarizes about-to-be-dropped session messages using OpenAI gpt-4o-mini (P3-002, D-016).
/// Produces a structured ~200-token summary: key issue, attempted solutions, unresolved questions.
/// </summary>
public sealed class OpenAiSessionSummarizationService : ISessionSummarizationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiSettings _openAiSettings;
    private readonly ChatOrchestrationSettings _settings;
    private readonly ILogger<OpenAiSessionSummarizationService> _logger;

    public OpenAiSessionSummarizationService(
        IHttpClientFactory httpClientFactory,
        OpenAiSettings openAiSettings,
        ChatOrchestrationSettings settings,
        ILogger<OpenAiSessionSummarizationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _openAiSettings = openAiSettings;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string?> SummarizeAsync(
        IReadOnlyList<ChatMessage> messagesToSummarize,
        CancellationToken cancellationToken = default)
    {
        if (messagesToSummarize.Count == 0)
            return null;

        var messages = BuildSummarizationMessages(messagesToSummarize);

        var httpClient = _httpClientFactory.CreateClient(HttpClientNames.OpenAi);

        var requestBody = new
        {
            model = _settings.SummarizationModel,
            messages,
            max_tokens = _settings.SummarizationMaxTokens,
            temperature = 0.1,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "session_summary",
                    strict = true,
                    schema = SummarizationSchema,
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_openAiSettings.Endpoint}/chat/completions");
        OpenAiResponseHelper.AddAuthorizationHeader(request, _openAiSettings.ApiKey);
        request.Content = JsonContent.Create(requestBody);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Summarization API error {StatusCode}: {Body}. Dropping messages without summary.",
                response.StatusCode, errorBody);
            return null;
        }

        var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        var parsed = OpenAiResponseHelper.ExtractContent<SessionSummaryResult>(responseJson, SharedJsonOptions.SnakeCase, _logger);
        if (parsed is not null)
        {
            var summary = FormatSummary(parsed);
            _logger.LogInformation(
                "Session summarization complete. SummarizedMessageCount={Count}, SummaryLength={Length}",
                messagesToSummarize.Count, summary.Length);
            return summary;
        }

        _logger.LogWarning("Summarization returned empty/unparseable response. Dropping messages without summary.");
        return null;
    }

    internal static List<object> BuildSummarizationMessages(IReadOnlyList<ChatMessage> messagesToSummarize)
    {
        var conversationText = new System.Text.StringBuilder();
        foreach (var msg in messagesToSummarize)
        {
            conversationText.AppendLine($"[{msg.Role}]: {msg.Content}");
        }

        return
        [
            new { role = "system", content = SummarizationSystemPrompt },
            new { role = "user", content = conversationText.ToString() },
        ];
    }

    internal static string FormatSummary(SessionSummaryResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Earlier conversation summary]");
        sb.AppendLine($"Key issue: {result.KeyIssue}");

        if (result.AttemptedSolutions.Count > 0)
        {
            sb.AppendLine("Attempted solutions:");
            foreach (var solution in result.AttemptedSolutions)
            {
                sb.AppendLine($"- {solution}");
            }
        }

        if (result.UnresolvedQuestions.Count > 0)
        {
            sb.AppendLine("Unresolved questions:");
            foreach (var question in result.UnresolvedQuestions)
            {
                sb.AppendLine($"- {question}");
            }
        }

        if (!string.IsNullOrWhiteSpace(result.CustomerContext))
        {
            sb.AppendLine($"Customer context: {result.CustomerContext}");
        }

        return sb.ToString().TrimEnd();
    }

    internal const string SummarizationSystemPrompt =
        "You are a concise support conversation summarizer. Given a conversation between a user and an assistant, " +
        "produce a structured summary that preserves the essential context needed for continued troubleshooting.\n\n" +
        "Guidelines:\n" +
        "- key_issue: The primary problem or request in 1-2 sentences.\n" +
        "- attempted_solutions: List solutions already tried or suggested (include outcome if known).\n" +
        "- unresolved_questions: List open questions or pending investigations.\n" +
        "- customer_context: Relevant environment details, versions, or configurations mentioned.\n" +
        "- Be factual and specific. Do not invent details not present in the conversation.\n" +
        "- Keep the total summary under 200 tokens.\n\n" +
        "Return JSON only — no markdown, no explanations.";

    internal static readonly object SummarizationSchema = new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["key_issue"] = new
            {
                type = "string",
                description = "The primary problem or request in 1-2 sentences",
            },
            ["attempted_solutions"] = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Solutions already tried or suggested, with outcomes if known",
            },
            ["unresolved_questions"] = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Open questions or pending investigations",
            },
            ["customer_context"] = new
            {
                type = new[] { "string", "null" },
                description = "Relevant environment details, versions, or configurations (null if none mentioned)",
            },
        },
        required = new[] { "key_issue", "attempted_solutions", "unresolved_questions", "customer_context" },
        additionalProperties = false,
    };
}

/// <summary>Structured summary result from the summarization model call.</summary>
public sealed class SessionSummaryResult
{
    public string KeyIssue { get; set; } = "";
    public List<string> AttemptedSolutions { get; set; } = [];
    public List<string> UnresolvedQuestions { get; set; } = [];
    public string? CustomerContext { get; set; }
}
