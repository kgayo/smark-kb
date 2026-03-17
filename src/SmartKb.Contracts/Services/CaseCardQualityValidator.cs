using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// P1-011: Validates distilled case-card quality with configurable thresholds.
/// Computes an overall quality score and reports issues per field.
/// </summary>
public sealed class CaseCardQualityValidator : ICaseCardQualityValidator
{
    private readonly CaseCardQualitySettings _settings;

    // Generic titles that indicate low-effort distillation.
    private static readonly string[] GenericTitlePrefixes =
    [
        "Pattern from session",
        "Untitled",
        "No title",
    ];

    // Generic problem statements that carry no diagnostic value.
    private static readonly string[] GenericProblemStatements =
    [
        "Problem identified from solved ticket evidence.",
    ];

    public CaseCardQualityValidator(CaseCardQualitySettings settings)
    {
        _settings = settings;
    }

    public CaseCardQualityReport Validate(CasePattern pattern)
    {
        var issues = new List<QualityIssue>();

        // Title checks.
        ValidateTitle(pattern, issues);

        // Problem statement checks.
        ValidateProblemStatement(pattern, issues);

        // Symptoms checks.
        ValidateSymptoms(pattern, issues);

        // Resolution steps checks.
        ValidateResolutionSteps(pattern, issues);

        // Related evidence checks.
        ValidateEvidence(pattern, issues);

        // Diagnosis and verification checks (warnings only — not hard requirements).
        ValidateDiagnosisSteps(pattern, issues);
        ValidateVerificationSteps(pattern, issues);

        // Compute overall quality score.
        var totalPenalty = issues.Sum(i => i.Penalty);
        var qualityScore = Math.Clamp(1.0f - totalPenalty, 0f, 1.0f);

        return new CaseCardQualityReport
        {
            QualityScore = qualityScore,
            Passed = qualityScore >= _settings.MinQualityScore,
            Rejected = qualityScore < _settings.RejectThreshold,
            Issues = issues,
        };
    }

    private void ValidateTitle(CasePattern pattern, List<QualityIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(pattern.Title))
        {
            issues.Add(new QualityIssue
            {
                Field = "Title",
                Severity = "error",
                Message = "Title is empty.",
                Penalty = 0.3f,
            });
            return;
        }

        if (pattern.Title.Length < _settings.MinTitleLength)
        {
            issues.Add(new QualityIssue
            {
                Field = "Title",
                Severity = "warning",
                Message = $"Title too short ({pattern.Title.Length} chars, minimum {_settings.MinTitleLength}).",
                Penalty = 0.15f,
            });
        }

        if (pattern.Title.Length > _settings.MaxTitleLength)
        {
            issues.Add(new QualityIssue
            {
                Field = "Title",
                Severity = "warning",
                Message = $"Title too long ({pattern.Title.Length} chars, maximum {_settings.MaxTitleLength}).",
                Penalty = 0.05f,
            });
        }

        if (IsGenericTitle(pattern.Title))
        {
            issues.Add(new QualityIssue
            {
                Field = "Title",
                Severity = "warning",
                Message = "Title appears generic and non-descriptive.",
                Penalty = 0.15f,
            });
        }
    }

    private void ValidateProblemStatement(CasePattern pattern, List<QualityIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(pattern.ProblemStatement))
        {
            issues.Add(new QualityIssue
            {
                Field = "ProblemStatement",
                Severity = "error",
                Message = "Problem statement is empty.",
                Penalty = 0.25f,
            });
            return;
        }

        if (pattern.ProblemStatement.Length < _settings.MinProblemStatementLength)
        {
            issues.Add(new QualityIssue
            {
                Field = "ProblemStatement",
                Severity = "warning",
                Message = $"Problem statement too short ({pattern.ProblemStatement.Length} chars, minimum {_settings.MinProblemStatementLength}).",
                Penalty = 0.15f,
            });
        }

        if (IsGenericProblemStatement(pattern.ProblemStatement))
        {
            issues.Add(new QualityIssue
            {
                Field = "ProblemStatement",
                Severity = "warning",
                Message = "Problem statement is generic placeholder text.",
                Penalty = 0.2f,
            });
        }
    }

    private void ValidateSymptoms(CasePattern pattern, List<QualityIssue> issues)
    {
        if (pattern.Symptoms.Count < _settings.MinSymptomCount)
        {
            issues.Add(new QualityIssue
            {
                Field = "Symptoms",
                Severity = "warning",
                Message = $"Insufficient symptoms ({pattern.Symptoms.Count}, minimum {_settings.MinSymptomCount}).",
                Penalty = 0.1f,
            });
        }
    }

    private void ValidateResolutionSteps(CasePattern pattern, List<QualityIssue> issues)
    {
        if (pattern.ResolutionSteps.Count < _settings.MinResolutionStepCount)
        {
            issues.Add(new QualityIssue
            {
                Field = "ResolutionSteps",
                Severity = "error",
                Message = $"Insufficient resolution steps ({pattern.ResolutionSteps.Count}, minimum {_settings.MinResolutionStepCount}).",
                Penalty = 0.3f,
            });
            return;
        }

        // Check individual step quality.
        var shortStepCount = pattern.ResolutionSteps
            .Count(s => s.Length < _settings.MinResolutionStepLength);

        if (shortStepCount > 0)
        {
            var ratio = shortStepCount / (float)pattern.ResolutionSteps.Count;
            issues.Add(new QualityIssue
            {
                Field = "ResolutionSteps",
                Severity = "warning",
                Message = $"{shortStepCount} of {pattern.ResolutionSteps.Count} resolution steps are too short (< {_settings.MinResolutionStepLength} chars).",
                Penalty = 0.1f * ratio,
            });
        }

        // Check for duplicate resolution steps.
        var distinctSteps = pattern.ResolutionSteps.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctSteps < pattern.ResolutionSteps.Count)
        {
            var duplicateCount = pattern.ResolutionSteps.Count - distinctSteps;
            issues.Add(new QualityIssue
            {
                Field = "ResolutionSteps",
                Severity = "warning",
                Message = $"{duplicateCount} duplicate resolution step(s) found.",
                Penalty = 0.1f,
            });
        }
    }

    private void ValidateEvidence(CasePattern pattern, List<QualityIssue> issues)
    {
        if (pattern.RelatedEvidenceIds.Count < _settings.MinRelatedEvidenceCount)
        {
            issues.Add(new QualityIssue
            {
                Field = "RelatedEvidenceIds",
                Severity = "error",
                Message = $"Insufficient related evidence ({pattern.RelatedEvidenceIds.Count}, minimum {_settings.MinRelatedEvidenceCount}).",
                Penalty = 0.2f,
            });
        }
    }

    private static void ValidateDiagnosisSteps(CasePattern pattern, List<QualityIssue> issues)
    {
        if (pattern.DiagnosisSteps.Count == 0)
        {
            issues.Add(new QualityIssue
            {
                Field = "DiagnosisSteps",
                Severity = "warning",
                Message = "No diagnosis steps provided.",
                Penalty = 0.05f,
            });
        }
    }

    private static void ValidateVerificationSteps(CasePattern pattern, List<QualityIssue> issues)
    {
        if (pattern.VerificationSteps.Count == 0)
        {
            issues.Add(new QualityIssue
            {
                Field = "VerificationSteps",
                Severity = "warning",
                Message = "No verification steps provided.",
                Penalty = 0.05f,
            });
        }
    }

    private static bool IsGenericTitle(string title)
    {
        foreach (var prefix in GenericTitlePrefixes)
        {
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsGenericProblemStatement(string statement)
    {
        foreach (var generic in GenericProblemStatements)
        {
            if (statement.Equals(generic, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
