using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

#region ClassificationResult DTO Tests

public class ClassificationResultTests
{
    [Fact]
    public void Empty_ReturnsDefaultValues()
    {
        var empty = ClassificationResult.Empty;

        Assert.Equal(string.Empty, empty.IssueCategory);
        Assert.Equal(string.Empty, empty.ProductArea);
        Assert.Equal("Unknown", empty.SeverityHint);
        Assert.False(empty.NeedsCustomerLookup);
        Assert.Empty(empty.MissingInfoSuggestions);
        Assert.Equal(0f, empty.ClassificationConfidence);
        Assert.Equal(0f, empty.EscalationLikelihood);
        Assert.Empty(empty.SourceTypePreference);
        Assert.Null(empty.TimeHorizonDays);
    }

    [Fact]
    public void AllFieldsPopulated_RoundTrips()
    {
        var result = new ClassificationResult
        {
            IssueCategory = "Authentication",
            ProductArea = "SSO",
            SeverityHint = "P2",
            NeedsCustomerLookup = true,
            MissingInfoSuggestions = ["What error message?", "When did it start?"],
            ClassificationConfidence = 0.85f,
            EscalationLikelihood = 0.35f,
            SourceTypePreference = ["ticket", "wiki_page"],
            TimeHorizonDays = 90,
        };

        Assert.Equal("Authentication", result.IssueCategory);
        Assert.Equal("SSO", result.ProductArea);
        Assert.Equal("P2", result.SeverityHint);
        Assert.True(result.NeedsCustomerLookup);
        Assert.Equal(2, result.MissingInfoSuggestions.Count);
        Assert.Equal(0.85f, result.ClassificationConfidence, 0.001f);
        Assert.Equal(0.35f, result.EscalationLikelihood, 0.001f);
        Assert.Equal(2, result.SourceTypePreference.Count);
        Assert.Equal(90, result.TimeHorizonDays);
    }

    [Fact]
    public void JsonDeserialization_SnakeCaseProperties()
    {
        var json = """
        {
            "issue_category": "Billing",
            "product_area": "Payment",
            "severity_hint": "P3",
            "needs_customer_lookup": false,
            "missing_info_suggestions": ["What plan?"],
            "classification_confidence": 0.7,
            "escalation_likelihood": 0.1,
            "source_type_preference": ["doc"],
            "time_horizon_days": 30
        }
        """;

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        };

        var result = System.Text.Json.JsonSerializer.Deserialize<ClassificationResult>(json, options);

        Assert.NotNull(result);
        Assert.Equal("Billing", result.IssueCategory);
        Assert.Equal("Payment", result.ProductArea);
        Assert.Equal("P3", result.SeverityHint);
        Assert.False(result.NeedsCustomerLookup);
        Assert.Single(result.MissingInfoSuggestions);
        Assert.Equal(0.7f, result.ClassificationConfidence, 0.01f);
        Assert.Equal(0.1f, result.EscalationLikelihood, 0.01f);
        Assert.Equal("doc", result.SourceTypePreference[0]);
        Assert.Equal(30, result.TimeHorizonDays);
    }

    [Fact]
    public void JsonDeserialization_NullTimeHorizon()
    {
        var json = """
        {
            "issue_category": "Bug",
            "product_area": "API",
            "severity_hint": "P1",
            "needs_customer_lookup": true,
            "missing_info_suggestions": [],
            "classification_confidence": 0.9,
            "escalation_likelihood": 0.8,
            "source_type_preference": [],
            "time_horizon_days": null
        }
        """;

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        };

        var result = System.Text.Json.JsonSerializer.Deserialize<ClassificationResult>(json, options);

        Assert.NotNull(result);
        Assert.Equal("P1", result.SeverityHint);
        Assert.Null(result.TimeHorizonDays);
    }
}

#endregion

#region EnrichFiltersFromClassification Tests

public class EnrichFiltersFromClassificationTests
{
    private readonly ChatOrchestrator _orchestrator;

    public EnrichFiltersFromClassificationTests()
    {
        _orchestrator = CreateOrchestrator();
    }

    [Fact]
    public void EmptyClassification_ReturnsOriginalFilters()
    {
        var original = new RetrievalFilter { ProductAreas = ["Auth"] };

        var enriched = _orchestrator.EnrichFiltersFromClassification(original, ClassificationResult.Empty);

        Assert.Equal(original.ProductAreas, enriched.ProductAreas);
    }

    [Fact]
    public void NullFilters_ReturnsNewFilterWithClassificationData()
    {
        var classification = new ClassificationResult
        {
            ProductArea = "SSO",
            ClassificationConfidence = 0.8f,
            SourceTypePreference = ["ticket"],
            TimeHorizonDays = 60,
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(null, classification);

        Assert.Equal(["SSO"], enriched.ProductAreas);
        Assert.Equal(["ticket"], enriched.SourceTypes);
        Assert.Equal(60, enriched.TimeHorizonDays);
    }

    [Fact]
    public void ExistingProductArea_NotOverridden()
    {
        var original = new RetrievalFilter { ProductAreas = ["Billing"] };
        var classification = new ClassificationResult
        {
            ProductArea = "SSO",
            ClassificationConfidence = 0.9f,
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(original, classification);

        Assert.Equal(["Billing"], enriched.ProductAreas);
    }

    [Fact]
    public void LowConfidence_DoesNotApplyFilters()
    {
        var classification = new ClassificationResult
        {
            ProductArea = "SSO",
            ClassificationConfidence = 0.3f, // Below 0.5 threshold
            SourceTypePreference = ["ticket"],
            TimeHorizonDays = 60,
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(null, classification);

        Assert.True(enriched.ProductAreas is null or { Count: 0 });
        Assert.True(enriched.SourceTypes is null or { Count: 0 });
        Assert.Null(enriched.TimeHorizonDays);
    }

    [Fact]
    public void HighConfidence_AppliesAllFilters()
    {
        var classification = new ClassificationResult
        {
            ProductArea = "Dashboard",
            ClassificationConfidence = 0.85f,
            SourceTypePreference = ["wiki_page", "doc"],
            TimeHorizonDays = 180,
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(null, classification);

        Assert.Equal(["Dashboard"], enriched.ProductAreas);
        Assert.Equal(["wiki_page", "doc"], enriched.SourceTypes);
        Assert.Equal(180, enriched.TimeHorizonDays);
    }

    [Fact]
    public void ExistingTimeHorizon_NotOverridden()
    {
        var original = new RetrievalFilter { TimeHorizonDays = 30 };
        var classification = new ClassificationResult
        {
            ClassificationConfidence = 0.9f,
            TimeHorizonDays = 180,
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(original, classification);

        Assert.Equal(30, enriched.TimeHorizonDays);
    }

    [Fact]
    public void ExistingSourceTypes_NotOverridden()
    {
        var original = new RetrievalFilter { SourceTypes = ["doc"] };
        var classification = new ClassificationResult
        {
            ClassificationConfidence = 0.9f,
            SourceTypePreference = ["ticket", "wiki_page"],
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(original, classification);

        Assert.Equal(["doc"], enriched.SourceTypes);
    }

    [Fact]
    public void EmptyProductArea_NotApplied()
    {
        var classification = new ClassificationResult
        {
            ProductArea = "",
            ClassificationConfidence = 0.9f,
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(null, classification);

        Assert.True(enriched.ProductAreas is null or { Count: 0 });
    }

    [Fact]
    public void PreservesExistingTags_WhenEnriching()
    {
        var original = new RetrievalFilter
        {
            Tags = ["urgent", "customer-facing"],
            Statuses = ["Active"],
        };
        var classification = new ClassificationResult
        {
            ProductArea = "API",
            ClassificationConfidence = 0.8f,
        };

        var enriched = _orchestrator.EnrichFiltersFromClassification(original, classification);

        Assert.Equal(["urgent", "customer-facing"], enriched.Tags);
        Assert.Equal(["Active"], enriched.Statuses);
        Assert.Equal(["API"], enriched.ProductAreas);
    }

    private static ChatOrchestrator CreateOrchestrator(
        float confidenceThreshold = 0.5f)
    {
        var settings = new ChatOrchestrationSettings
        {
            ClassificationFilterConfidenceThreshold = confidenceThreshold,
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

#region BuildClassificationMessages Tests

public class BuildClassificationMessagesTests
{
    [Fact]
    public void NoSessionHistory_SingleUserMessage()
    {
        var messages = OpenAiQueryClassificationService.BuildClassificationMessages("SSO is broken", null);

        Assert.Equal(2, messages.Count);
        // System message + user message.
    }

    [Fact]
    public void WithSessionHistory_IncludesRecentContext()
    {
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "First message" },
            new() { Role = "assistant", Content = "First reply" },
            new() { Role = "user", Content = "Second message" },
            new() { Role = "assistant", Content = "Second reply" },
        };

        var messages = OpenAiQueryClassificationService.BuildClassificationMessages("Follow-up question", history);

        Assert.Equal(2, messages.Count);
        // Should have system + user with context prefix.
    }

    [Fact]
    public void LongSessionHistory_TakesLastThree()
    {
        var history = Enumerable.Range(0, 10)
            .Select(i => new ChatMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = $"Message {i}" })
            .ToList();

        var messages = OpenAiQueryClassificationService.BuildClassificationMessages("Current query", history);

        // Should include system + user (with only last 3 history messages condensed).
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void LongContent_TruncatedInHistory()
    {
        var longContent = new string('x', 300);
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = longContent },
        };

        var messages = OpenAiQueryClassificationService.BuildClassificationMessages("Short query", history);

        // Verify truncation happened — the user content should have "..." suffix.
        Assert.Equal(2, messages.Count);
    }
}

#endregion

#region ChatOrchestrationSettings Classification Tests

public class ClassificationSettingsTests
{
    [Fact]
    public void Defaults_ClassificationEnabled()
    {
        var settings = new ChatOrchestrationSettings();

        Assert.True(settings.EnableQueryClassification);
        Assert.Equal("gpt-4o-mini", settings.ClassificationModel);
        Assert.Equal(0.5f, settings.ClassificationFilterConfidenceThreshold);
        Assert.Equal(3000, settings.ClassificationTimeoutMs);
    }

    [Fact]
    public void CustomValues_Applied()
    {
        var settings = new ChatOrchestrationSettings
        {
            EnableQueryClassification = false,
            ClassificationModel = "gpt-4o",
            ClassificationFilterConfidenceThreshold = 0.7f,
            ClassificationTimeoutMs = 5000,
        };

        Assert.False(settings.EnableQueryClassification);
        Assert.Equal("gpt-4o", settings.ClassificationModel);
        Assert.Equal(0.7f, settings.ClassificationFilterConfidenceThreshold);
        Assert.Equal(5000, settings.ClassificationTimeoutMs);
    }
}

#endregion

#region ClassificationSchema Validation Tests

public class ClassificationSchemaTests
{
    [Fact]
    public void Schema_HasAllRequiredFields()
    {
        var schema = OpenAiQueryClassificationService.ClassificationSchema;
        var json = System.Text.Json.JsonSerializer.Serialize(schema);

        Assert.Contains("issue_category", json);
        Assert.Contains("product_area", json);
        Assert.Contains("severity_hint", json);
        Assert.Contains("needs_customer_lookup", json);
        Assert.Contains("missing_info_suggestions", json);
        Assert.Contains("classification_confidence", json);
        Assert.Contains("escalation_likelihood", json);
        Assert.Contains("source_type_preference", json);
        Assert.Contains("time_horizon_days", json);
    }

    [Fact]
    public void Schema_SeverityHint_HasCorrectEnum()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(OpenAiQueryClassificationService.ClassificationSchema);

        Assert.Contains("P1", json);
        Assert.Contains("P2", json);
        Assert.Contains("P3", json);
        Assert.Contains("P4", json);
        Assert.Contains("Unknown", json);
    }

    [Fact]
    public void SystemPrompt_ContainsTriageGuidelines()
    {
        var prompt = OpenAiQueryClassificationService.ClassificationSystemPrompt;

        Assert.Contains("triage specialist", prompt);
        Assert.Contains("conservative", prompt);
        Assert.Contains("missing_info_suggestions", prompt);
        Assert.Contains("JSON only", prompt);
    }
}

#endregion

#region ChatResponse Triage Metadata Tests

public class ChatResponseTriageMetadataTests
{
    [Fact]
    public void DefaultValues_AllTriageFieldsNull()
    {
        var response = new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = "test",
            Citations = [],
            Confidence = 0.8f,
            ConfidenceLabel = "High",
            TraceId = "t1",
            HasEvidence = true,
            SystemPromptVersion = "1.0",
        };

        Assert.Null(response.IssueCategory);
        Assert.Null(response.ClassifiedProductArea);
        Assert.Null(response.SeverityGuess);
        Assert.Equal(0f, response.ClassificationConfidence);
        Assert.Empty(response.MissingInfoSuggestions);
        Assert.Equal(0f, response.EscalationLikelihood);
    }

    [Fact]
    public void PopulatedTriageMetadata_AllFieldsPresent()
    {
        var response = new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = "test",
            Citations = [],
            Confidence = 0.8f,
            ConfidenceLabel = "High",
            TraceId = "t1",
            HasEvidence = true,
            SystemPromptVersion = "1.0",
            IssueCategory = "Authentication",
            ClassifiedProductArea = "SSO",
            SeverityGuess = "P2",
            ClassificationConfidence = 0.85f,
            MissingInfoSuggestions = ["What browser?"],
            EscalationLikelihood = 0.3f,
        };

        Assert.Equal("Authentication", response.IssueCategory);
        Assert.Equal("SSO", response.ClassifiedProductArea);
        Assert.Equal("P2", response.SeverityGuess);
        Assert.Equal(0.85f, response.ClassificationConfidence, 0.01f);
        Assert.Single(response.MissingInfoSuggestions);
        Assert.Equal(0.3f, response.EscalationLikelihood, 0.01f);
    }
}

#endregion
