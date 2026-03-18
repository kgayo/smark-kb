using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Pre-retrieval query classification using OpenAI structured output (P3-001).
/// Uses a lighter model (gpt-4o-mini by default) to classify support queries before retrieval,
/// biasing search filters and populating triage metadata.
/// </summary>
public sealed class OpenAiQueryClassificationService : IQueryClassificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiSettings _openAiSettings;
    private readonly ChatOrchestrationSettings _settings;
    private readonly ILogger<OpenAiQueryClassificationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public OpenAiQueryClassificationService(
        IHttpClientFactory httpClientFactory,
        OpenAiSettings openAiSettings,
        ChatOrchestrationSettings settings,
        ILogger<OpenAiQueryClassificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _openAiSettings = openAiSettings;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ClassificationResult> ClassifyAsync(
        string query,
        IReadOnlyList<ChatMessage>? sessionHistory = null,
        CancellationToken cancellationToken = default)
    {
        var messages = BuildClassificationMessages(query, sessionHistory);

        var httpClient = _httpClientFactory.CreateClient("OpenAi");

        var requestBody = new
        {
            model = _settings.ClassificationModel,
            messages,
            max_tokens = 512,
            temperature = 0.1,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "support_classification",
                    strict = true,
                    schema = ClassificationSchema,
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_openAiSettings.Endpoint}/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {_openAiSettings.ApiKey}");
        request.Content = JsonContent.Create(requestBody);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Classification API error {StatusCode}: {Body}. Falling back to unclassified retrieval.",
                response.StatusCode, errorBody);
            return ClassificationResult.Empty;
        }

        var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        if (responseJson.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            var messageContent = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (!string.IsNullOrEmpty(messageContent))
            {
                var parsed = JsonSerializer.Deserialize<ClassificationResult>(messageContent, JsonOptions);
                if (parsed is not null)
                {
                    _logger.LogInformation(
                        "Query classified: Category={Category}, ProductArea={ProductArea}, " +
                        "Severity={Severity}, Confidence={Confidence:F2}, EscalationLikelihood={EscalationLikelihood:F2}",
                        parsed.IssueCategory, parsed.ProductArea, parsed.SeverityHint,
                        parsed.ClassificationConfidence, parsed.EscalationLikelihood);
                    return parsed;
                }
            }
        }

        _logger.LogWarning("Classification returned empty/unparseable response. Falling back to unclassified.");
        return ClassificationResult.Empty;
    }

    internal static List<object> BuildClassificationMessages(
        string query,
        IReadOnlyList<ChatMessage>? sessionHistory)
    {
        var messages = new List<object>
        {
            new { role = "system", content = ClassificationSystemPrompt },
        };

        // Include condensed session context if available.
        if (sessionHistory is { Count: > 0 })
        {
            var contextParts = new List<string> { "Prior conversation context:" };
            // Include last 3 messages max to keep classification prompt small.
            var recentHistory = sessionHistory.Count > 3
                ? sessionHistory.Skip(sessionHistory.Count - 3).ToList()
                : sessionHistory;

            foreach (var msg in recentHistory)
            {
                contextParts.Add($"[{msg.Role}]: {(msg.Content.Length > 200 ? msg.Content[..200] + "..." : msg.Content)}");
            }

            contextParts.Add($"\nCurrent question:\n{query}");
            messages.Add(new { role = "user", content = string.Join("\n", contextParts) });
        }
        else
        {
            messages.Add(new { role = "user", content = query });
        }

        return messages;
    }

    internal const string ClassificationSystemPrompt =
        "You are a support triage specialist. Analyze the customer's support inquiry and classify it to help route the request effectively.\n\n" +
        "Guidelines:\n" +
        "- Be conservative with severity: only assign P1 for clear production-down or data-loss scenarios.\n" +
        "- Be specific with product_area: use the most specific module/feature name you can identify.\n" +
        "- For missing_info_suggestions: list concrete questions the support agent should ask to clarify the issue.\n" +
        "- For source_type_preference: suggest which evidence types are most likely helpful (ticket, work_item, wiki_page, doc, case_pattern).\n" +
        "- Set escalation_likelihood based on severity, complexity, and whether multiple teams may be involved.\n" +
        "- If the query is vague, set classification_confidence low and provide missing_info_suggestions.\n\n" +
        "Return JSON only — no markdown, no explanations.";

    internal static readonly object ClassificationSchema = new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["issue_category"] = new
            {
                type = "string",
                description = "Primary issue category (e.g., Authentication, Billing, Integration, Performance, Bug, Feature Request, Configuration, Data, Deployment)",
            },
            ["product_area"] = new
            {
                type = "string",
                description = "Product/module area (e.g., SSO, Payment, API, Dashboard, Connector, Search, Ingestion)",
            },
            ["severity_hint"] = new
            {
                type = "string",
                @enum = new[] { "P1", "P2", "P3", "P4", "Unknown" },
                description = "Estimated severity: P1=critical/production-down, P2=significant impact, P3=minor, P4=cosmetic, Unknown=insufficient info",
            },
            ["needs_customer_lookup"] = new
            {
                type = "boolean",
                description = "Whether customer-specific context (account, config, logs) is needed for accurate diagnosis",
            },
            ["missing_info_suggestions"] = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Concrete questions to ask the customer to clarify the issue (FR-TRIAGE-002)",
            },
            ["classification_confidence"] = new
            {
                type = "number",
                description = "Confidence in this classification (0.0=very uncertain, 1.0=very confident)",
            },
            ["escalation_likelihood"] = new
            {
                type = "number",
                description = "Estimated likelihood that this issue will need escalation (0.0=unlikely, 1.0=certain)",
            },
            ["source_type_preference"] = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Suggested evidence source types for retrieval (e.g., ticket, work_item, wiki_page, doc, case_pattern)",
            },
            ["time_horizon_days"] = new
            {
                type = new[] { "integer", "null" },
                description = "Suggested time horizon in days for filtering evidence (null if no time constraint)",
            },
        },
        required = new[]
        {
            "issue_category", "product_area", "severity_hint", "needs_customer_lookup",
            "missing_info_suggestions", "classification_confidence", "escalation_likelihood",
            "source_type_preference", "time_horizon_days",
        },
        additionalProperties = false,
    };
}
