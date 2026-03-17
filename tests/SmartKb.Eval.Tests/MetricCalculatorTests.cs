using SmartKb.Contracts.Models;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class MetricCalculatorTests
{
    [Fact]
    public void MustIncludeHitRate_AllPresent_Returns1()
    {
        var rate = MetricCalculator.ComputeMustIncludeHitRate(
            new[] { "password", "reset" },
            "Please reset your password using the forgot password link.");

        Assert.Equal(1f, rate);
    }

    [Fact]
    public void MustIncludeHitRate_SomePresent_ReturnsRatio()
    {
        var rate = MetricCalculator.ComputeMustIncludeHitRate(
            new[] { "password", "reset", "MFA" },
            "Please reset your password.");

        Assert.Equal(2f / 3f, rate, 3);
    }

    [Fact]
    public void MustIncludeHitRate_NonePresent_Returns0()
    {
        var rate = MetricCalculator.ComputeMustIncludeHitRate(
            new[] { "certificate", "rotation" },
            "The server is running normally.");

        Assert.Equal(0f, rate);
    }

    [Fact]
    public void MustIncludeHitRate_NullList_Returns1()
    {
        Assert.Equal(1f, MetricCalculator.ComputeMustIncludeHitRate(null, "any answer"));
    }

    [Fact]
    public void MustIncludeHitRate_EmptyList_Returns1()
    {
        Assert.Equal(1f, MetricCalculator.ComputeMustIncludeHitRate([], "any answer"));
    }

    [Fact]
    public void MustIncludeHitRate_CaseInsensitive()
    {
        var rate = MetricCalculator.ComputeMustIncludeHitRate(
            new[] { "PASSWORD", "Reset" },
            "password reset completed");

        Assert.Equal(1f, rate);
    }

    [Fact]
    public void SafetyPass_NoForbiddenWords_ReturnsTrue()
    {
        Assert.True(MetricCalculator.ComputeSafetyPass(
            new[] { "SSN", "credit card" },
            "The data has been redacted for security."));
    }

    [Fact]
    public void SafetyPass_ContainsForbiddenWord_ReturnsFalse()
    {
        Assert.False(MetricCalculator.ComputeSafetyPass(
            new[] { "123-45-6789" },
            "The SSN is 123-45-6789."));
    }

    [Fact]
    public void SafetyPass_NullList_ReturnsTrue()
    {
        Assert.True(MetricCalculator.ComputeSafetyPass(null, "any answer"));
    }

    [Fact]
    public void ComputeCaseMetrics_FinalAnswer_AllMatch()
    {
        var evalCase = MakeCase("final_answer",
            mustInclude: new[] { "password", "reset" },
            mustCiteSources: true,
            shouldHaveEvidence: true,
            expectedEscalation: new EvalExpectedEscalation { Recommended = false });

        var response = MakeResponse("final_answer",
            answer: "Please reset your password via the admin panel.",
            citations: new[] { MakeCitation() },
            hasEvidence: true,
            confidence: 0.8f);

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);

        Assert.True(metrics.ResponseTypeMatch);
        Assert.Equal(1f, metrics.Groundedness);
        Assert.Equal(1f, metrics.MustIncludeHitRate);
        Assert.True(metrics.CitationCoverageMet);
        Assert.True(metrics.EscalationMatch);
        Assert.True(metrics.RoutingMatch);
        Assert.True(metrics.EvidenceMatch);
        Assert.True(metrics.SafetyPass);
    }

    [Fact]
    public void ComputeCaseMetrics_ResponseTypeMismatch()
    {
        var evalCase = MakeCase("final_answer");
        var response = MakeResponse("next_steps_only");

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.False(metrics.ResponseTypeMatch);
    }

    [Fact]
    public void ComputeCaseMetrics_MissingCitations_FailsCoverage()
    {
        var evalCase = MakeCase("final_answer", mustCiteSources: true, minCitations: 2);
        var response = MakeResponse("final_answer", citations: new[] { MakeCitation() });

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.False(metrics.CitationCoverageMet);
    }

    [Fact]
    public void ComputeCaseMetrics_EscalationExpected_ButNotRecommended()
    {
        var evalCase = MakeCase("escalate",
            expectedEscalation: new EvalExpectedEscalation { Recommended = true, TargetTeam = "Security" });
        var response = MakeResponse("final_answer", escalation: null);

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.False(metrics.EscalationMatch);
        Assert.False(metrics.RoutingMatch);
    }

    [Fact]
    public void ComputeCaseMetrics_EscalationCorrect_WrongTeam()
    {
        var evalCase = MakeCase("escalate",
            expectedEscalation: new EvalExpectedEscalation { Recommended = true, TargetTeam = "Security" });
        var response = MakeResponse("escalate",
            escalation: new EscalationSignal { Recommended = true, TargetTeam = "Engineering" });

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.True(metrics.EscalationMatch);
        Assert.False(metrics.RoutingMatch);
    }

    [Fact]
    public void ComputeCaseMetrics_EscalationCorrectTeam()
    {
        var evalCase = MakeCase("escalate",
            expectedEscalation: new EvalExpectedEscalation { Recommended = true, TargetTeam = "Security" });
        var response = MakeResponse("escalate",
            escalation: new EscalationSignal { Recommended = true, TargetTeam = "Security" });

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.True(metrics.EscalationMatch);
        Assert.True(metrics.RoutingMatch);
    }

    [Fact]
    public void ComputeCaseMetrics_NoEscalationExpected_NoEscalationExpectation_Passes()
    {
        var evalCase = MakeCase("final_answer");
        var response = MakeResponse("final_answer");

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.True(metrics.EscalationMatch);
        Assert.True(metrics.RoutingMatch);
    }

    [Fact]
    public void ComputeCaseMetrics_EvidenceMismatch()
    {
        var evalCase = MakeCase("final_answer", shouldHaveEvidence: true);
        var response = MakeResponse("final_answer", hasEvidence: false);

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.False(metrics.EvidenceMatch);
    }

    [Fact]
    public void ComputeCaseMetrics_SafetyViolation()
    {
        var evalCase = MakeCase("final_answer", mustNotInclude: new[] { "123-45-6789" });
        var response = MakeResponse("final_answer", answer: "The SSN is 123-45-6789.");

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.False(metrics.SafetyPass);
    }

    [Fact]
    public void ComputeCaseMetrics_ConfidenceBelowMin()
    {
        var evalCase = MakeCase("final_answer", minConfidence: 0.7f);
        var response = MakeResponse("final_answer", confidence: 0.5f);

        var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);
        Assert.False(metrics.ConfidenceMet);
    }

    [Fact]
    public void ComputeAggregateMetrics_EmptyResults_ReturnsDefaults()
    {
        var aggregate = MetricCalculator.ComputeAggregateMetrics([]);
        Assert.Equal(0f, aggregate.Groundedness);
    }

    [Fact]
    public void ComputeAggregateMetrics_AllPassing()
    {
        var results = new[]
        {
            MakeResult("eval-00001", responseType: "final_answer", hasEvidence: true,
                confidence: 0.8f, groundedness: 1f, citationMet: true, escalationMatch: true,
                routingMatch: true, mustIncludeRate: 1f, safetyPass: true, durationMs: 500),
            MakeResult("eval-00002", responseType: "final_answer", hasEvidence: true,
                confidence: 0.9f, groundedness: 0.8f, citationMet: true, escalationMatch: true,
                routingMatch: true, mustIncludeRate: 0.8f, safetyPass: true, durationMs: 700),
        };

        var aggregate = MetricCalculator.ComputeAggregateMetrics(results);

        Assert.Equal(0.9f, aggregate.Groundedness, 2);
        Assert.Equal(1f, aggregate.CitationCoverage);
        Assert.Equal(1f, aggregate.RoutingAccuracy);
        Assert.Equal(0f, aggregate.NoEvidenceRate);
        Assert.Equal(1f, aggregate.ResponseTypeAccuracy);
        Assert.Equal(0.9f, aggregate.MustIncludeHitRate, 2);
        Assert.Equal(1f, aggregate.SafetyPassRate);
        Assert.Equal(0.85f, aggregate.AverageConfidence, 2);
        Assert.Equal(600, aggregate.AverageDurationMs);
    }

    [Fact]
    public void ComputeAggregateMetrics_NoEvidenceRate_ComputedCorrectly()
    {
        var results = new[]
        {
            MakeResult("eval-00001", hasEvidence: true),
            MakeResult("eval-00002", hasEvidence: false),
            MakeResult("eval-00003", hasEvidence: true),
            MakeResult("eval-00004", hasEvidence: false),
        };

        var aggregate = MetricCalculator.ComputeAggregateMetrics(results);
        Assert.Equal(0.5f, aggregate.NoEvidenceRate, 2);
    }

    [Fact]
    public void ComputeAggregateMetrics_SkipsErrorResults()
    {
        var results = new[]
        {
            MakeResult("eval-00001", hasEvidence: true, confidence: 0.9f),
            new EvalResult
            {
                CaseId = "eval-00002",
                Response = MakeResponse("error"),
                Metrics = new CaseMetrics(),
                Error = "Orchestrator error",
            },
        };

        var aggregate = MetricCalculator.ComputeAggregateMetrics(results);
        Assert.Equal(0.9f, aggregate.AverageConfidence, 2);
    }

    // --- Helpers ---

    private static EvalCase MakeCase(
        string responseType,
        IReadOnlyList<string>? mustInclude = null,
        IReadOnlyList<string>? mustNotInclude = null,
        bool? mustCiteSources = null,
        int? minCitations = null,
        EvalExpectedEscalation? expectedEscalation = null,
        float? minConfidence = null,
        bool? shouldHaveEvidence = null)
    {
        return new EvalCase
        {
            Id = "eval-00001",
            TenantId = "test-tenant",
            Query = "Test query",
            Expected = new EvalExpected
            {
                ResponseType = responseType,
                MustInclude = mustInclude,
                MustNotInclude = mustNotInclude,
                MustCiteSources = mustCiteSources,
                MinCitations = minCitations,
                ExpectedEscalation = expectedEscalation,
                MinConfidence = minConfidence,
                ShouldHaveEvidence = shouldHaveEvidence,
            },
        };
    }

    private static ChatResponse MakeResponse(
        string responseType = "final_answer",
        string answer = "Test answer",
        CitationDto[]? citations = null,
        bool hasEvidence = true,
        float confidence = 0.8f,
        EscalationSignal? escalation = null)
    {
        return new ChatResponse
        {
            ResponseType = responseType,
            Answer = answer,
            Citations = citations ?? [],
            Confidence = confidence,
            ConfidenceLabel = confidence >= 0.7f ? "High" : confidence >= 0.4f ? "Medium" : "Low",
            HasEvidence = hasEvidence,
            TraceId = "test-trace",
            SystemPromptVersion = "1.0",
            Escalation = escalation,
        };
    }

    private static CitationDto MakeCitation(string? chunkId = null) => new()
    {
        ChunkId = chunkId ?? "chunk-1",
        EvidenceId = "evidence-1",
        Title = "Test Evidence",
        SourceUrl = "https://example.com",
        SourceSystem = "AzureDevOps",
        Snippet = "Test snippet",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = "Internal",
    };

    private static EvalResult MakeResult(
        string caseId,
        string responseType = "final_answer",
        bool hasEvidence = true,
        float confidence = 0.8f,
        float groundedness = 1f,
        bool citationMet = true,
        bool escalationMatch = true,
        bool routingMatch = true,
        float mustIncludeRate = 1f,
        bool safetyPass = true,
        long durationMs = 500)
    {
        return new EvalResult
        {
            CaseId = caseId,
            Response = MakeResponse(responseType, hasEvidence: hasEvidence, confidence: confidence),
            Metrics = new CaseMetrics
            {
                ResponseTypeMatch = true,
                Groundedness = groundedness,
                CitationCoverageMet = citationMet,
                CitationCount = 1,
                EscalationMatch = escalationMatch,
                RoutingMatch = routingMatch,
                EvidenceMatch = true,
                MustIncludeHitRate = mustIncludeRate,
                SafetyPass = safetyPass,
                ConfidenceMet = true,
            },
            DurationMs = durationMs,
        };
    }
}
