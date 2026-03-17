using SmartKb.Contracts.Models;
using SmartKb.Eval.Models;

namespace SmartKb.Eval;

/// <summary>
/// Computes per-case and aggregate evaluation metrics.
/// </summary>
public static class MetricCalculator
{
    /// <summary>
    /// Computes metrics for a single eval case against its expected outcomes.
    /// </summary>
    public static CaseMetrics ComputeCaseMetrics(EvalCase evalCase, ChatResponse response)
    {
        var expected = evalCase.Expected;
        var answer = response.Answer ?? string.Empty;

        // Response type match
        var responseTypeMatch = string.Equals(expected.ResponseType, response.ResponseType, StringComparison.OrdinalIgnoreCase);

        // Must-include hit rate and groundedness
        var mustIncludeHitRate = ComputeMustIncludeHitRate(expected.MustInclude, answer);

        // Groundedness: for cases with must_include, use hit rate as proxy.
        // For cases without must_include, check that citations exist when expected.
        var groundedness = expected.MustInclude is { Count: > 0 }
            ? mustIncludeHitRate
            : (expected.MustCiteSources == true && response.Citations.Count > 0) ? 1f : (expected.MustCiteSources == true ? 0f : 1f);

        // Citation coverage
        var citationCoverageMet = ComputeCitationCoverage(expected, response);

        // Escalation match
        var (escalationMatch, routingMatch) = ComputeEscalationMetrics(expected, response);

        // Evidence match
        var evidenceMatch = expected.ShouldHaveEvidence is null || expected.ShouldHaveEvidence == response.HasEvidence;

        // Safety check (must_not_include)
        var safetyPass = ComputeSafetyPass(expected.MustNotInclude, answer);

        // Confidence check
        var confidenceMet = expected.MinConfidence is null || response.Confidence >= expected.MinConfidence;

        return new CaseMetrics
        {
            ResponseTypeMatch = responseTypeMatch,
            Groundedness = groundedness,
            CitationCoverageMet = citationCoverageMet,
            CitationCount = response.Citations.Count,
            EscalationMatch = escalationMatch,
            RoutingMatch = routingMatch,
            EvidenceMatch = evidenceMatch,
            MustIncludeHitRate = mustIncludeHitRate,
            SafetyPass = safetyPass,
            ConfidenceMet = confidenceMet,
        };
    }

    /// <summary>
    /// Computes aggregate metrics across all evaluated cases.
    /// </summary>
    public static AggregateMetrics ComputeAggregateMetrics(IReadOnlyList<EvalResult> results)
    {
        if (results.Count == 0)
            return new AggregateMetrics();

        var successful = results.Where(r => r.Error is null).ToList();
        if (successful.Count == 0)
            return new AggregateMetrics();

        // Groundedness: average across cases that have must_include expectations
        var groundednessCases = successful.Where(r => r.Metrics.Groundedness >= 0).ToList();
        var groundedness = groundednessCases.Count > 0
            ? groundednessCases.Average(r => r.Metrics.Groundedness)
            : 0f;

        // Citation coverage: proportion of cases where citation expectations were met
        var citationCoverage = successful.Average(r => r.Metrics.CitationCoverageMet ? 1f : 0f);

        // Routing accuracy: among cases with escalation expectations
        var routingCases = successful
            .Where(r => r.Metrics.RoutingMatch || !r.Metrics.EscalationMatch)
            .ToList();
        // Only count cases that have escalation expectations
        var escalationCases = successful.ToList(); // All cases are evaluated for escalation
        var routingAccuracy = escalationCases.Count > 0
            ? escalationCases.Average(r => r.Metrics.RoutingMatch ? 1f : 0f)
            : 0f;

        // No-evidence rate: proportion of cases where response has no evidence
        var noEvidenceRate = successful.Average(r => r.Response.HasEvidence ? 0f : 1f);

        // Response type accuracy
        var responseTypeAccuracy = successful.Average(r => r.Metrics.ResponseTypeMatch ? 1f : 0f);

        // Must-include hit rate
        var mustIncludeHitRate = successful.Average(r => r.Metrics.MustIncludeHitRate);

        // Safety pass rate
        var safetyPassRate = successful.Average(r => r.Metrics.SafetyPass ? 1f : 0f);

        // Average confidence
        var averageConfidence = successful.Average(r => r.Response.Confidence);

        // Average duration
        var averageDurationMs = (long)successful.Average(r => r.DurationMs);

        return new AggregateMetrics
        {
            Groundedness = (float)groundedness,
            CitationCoverage = (float)citationCoverage,
            RoutingAccuracy = (float)routingAccuracy,
            NoEvidenceRate = (float)noEvidenceRate,
            ResponseTypeAccuracy = (float)responseTypeAccuracy,
            MustIncludeHitRate = (float)mustIncludeHitRate,
            SafetyPassRate = (float)safetyPassRate,
            AverageConfidence = (float)averageConfidence,
            AverageDurationMs = averageDurationMs,
        };
    }

    /// <summary>
    /// Computes what proportion of must_include keywords appear in the answer (case-insensitive).
    /// Returns 1.0 if no must_include list is specified.
    /// </summary>
    public static float ComputeMustIncludeHitRate(IReadOnlyList<string>? mustInclude, string answer)
    {
        if (mustInclude is null or { Count: 0 })
            return 1f;

        var hits = mustInclude.Count(keyword =>
            answer.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        return (float)hits / mustInclude.Count;
    }

    /// <summary>
    /// Checks whether all must_not_include keywords are absent from the answer.
    /// Returns true if no must_not_include list is specified.
    /// </summary>
    public static bool ComputeSafetyPass(IReadOnlyList<string>? mustNotInclude, string answer)
    {
        if (mustNotInclude is null or { Count: 0 })
            return true;

        return mustNotInclude.All(keyword =>
            !answer.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool ComputeCitationCoverage(EvalExpected expected, ChatResponse response)
    {
        if (expected.MustCiteSources == true && response.Citations.Count == 0)
            return false;

        if (expected.MinCitations is > 0 && response.Citations.Count < expected.MinCitations)
            return false;

        return true;
    }

    internal static (bool EscalationMatch, bool RoutingMatch) ComputeEscalationMetrics(
        EvalExpected expected, ChatResponse response)
    {
        if (expected.ExpectedEscalation is null)
            return (true, true); // No expectation → pass

        var actualRecommended = response.Escalation?.Recommended ?? false;
        var escalationMatch = expected.ExpectedEscalation.Recommended == actualRecommended;

        var routingMatch = escalationMatch;
        if (escalationMatch && expected.ExpectedEscalation.Recommended && expected.ExpectedEscalation.TargetTeam is not null)
        {
            routingMatch = string.Equals(
                expected.ExpectedEscalation.TargetTeam,
                response.Escalation?.TargetTeam,
                StringComparison.OrdinalIgnoreCase);
        }

        return (escalationMatch, routingMatch);
    }
}
