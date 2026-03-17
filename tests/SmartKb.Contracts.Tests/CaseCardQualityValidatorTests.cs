using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class CaseCardQualityValidatorTests
{
    private static CaseCardQualitySettings DefaultSettings() => new();

    private static CasePattern CreatePattern(
        string title = "Pattern: Auth token validation failure on SSO login",
        string problemStatement = "Users cannot log in via SSO due to expired token cache causing AuthenticationException.",
        IReadOnlyList<string>? symptoms = null,
        IReadOnlyList<string>? diagnosisSteps = null,
        IReadOnlyList<string>? resolutionSteps = null,
        IReadOnlyList<string>? verificationSteps = null,
        IReadOnlyList<string>? relatedEvidenceIds = null)
    {
        return new CasePattern
        {
            PatternId = "pattern-abc123",
            TenantId = "t1",
            Title = title,
            ProblemStatement = problemStatement,
            Symptoms = symptoms ?? ["AuthenticationException on login", "SSO redirect loop"],
            DiagnosisSteps = diagnosisSteps ?? ["Check token cache expiry", "Verify Entra ID config"],
            ResolutionSteps = resolutionSteps ?? ["Clear token cache and restart the auth service", "Update Entra ID app registration with new redirect URI"],
            VerificationSteps = verificationSteps ?? ["Verify SSO login completes successfully"],
            RelatedEvidenceIds = relatedEvidenceIds ?? ["ev-1", "ev-2"],
            TrustLevel = TrustLevel.Draft,
            Confidence = 0.6f,
        };
    }

    // --- Full Pass ---

    [Fact]
    public void Validate_HighQualityPattern_Passes()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var report = validator.Validate(CreatePattern());
        Assert.True(report.Passed);
        Assert.False(report.Rejected);
        Assert.True(report.QualityScore >= 0.8f);
    }

    // --- Title Validation ---

    [Fact]
    public void Validate_EmptyTitle_ErrorWithHighPenalty()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(title: "");
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "Title" && i.Severity == "error");
    }

    [Fact]
    public void Validate_ShortTitle_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(title: "Bug fix");
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "Title" && i.Message.Contains("too short"));
    }

    [Fact]
    public void Validate_GenericTitle_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(title: "Pattern from session abc123");
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "Title" && i.Message.Contains("generic"));
    }

    [Fact]
    public void Validate_LongTitle_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(title: new string('A', 250));
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "Title" && i.Message.Contains("too long"));
    }

    // --- Problem Statement Validation ---

    [Fact]
    public void Validate_EmptyProblemStatement_Error()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(problemStatement: "");
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "ProblemStatement" && i.Severity == "error");
    }

    [Fact]
    public void Validate_ShortProblemStatement_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(problemStatement: "Issue found.");
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "ProblemStatement" && i.Message.Contains("too short"));
    }

    [Fact]
    public void Validate_GenericProblemStatement_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(problemStatement: "Problem identified from solved ticket evidence.");
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "ProblemStatement" && i.Message.Contains("generic"));
    }

    // --- Symptoms Validation ---

    [Fact]
    public void Validate_NoSymptoms_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(symptoms: []);
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "Symptoms");
    }

    // --- Resolution Steps Validation ---

    [Fact]
    public void Validate_NoResolutionSteps_Error()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(resolutionSteps: []);
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "ResolutionSteps" && i.Severity == "error");
    }

    [Fact]
    public void Validate_ShortResolutionSteps_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(resolutionSteps: ["Fix it.", "Done."]);
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "ResolutionSteps" && i.Message.Contains("too short"));
    }

    [Fact]
    public void Validate_DuplicateResolutionSteps_Warning()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(resolutionSteps: ["Restart the service to clear cache", "Restart the service to clear cache"]);
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "ResolutionSteps" && i.Message.Contains("duplicate"));
    }

    // --- Evidence Validation ---

    [Fact]
    public void Validate_NoRelatedEvidence_Error()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(relatedEvidenceIds: []);
        var report = validator.Validate(pattern);
        Assert.Contains(report.Issues, i => i.Field == "RelatedEvidenceIds" && i.Severity == "error");
    }

    // --- Diagnosis & Verification Warnings ---

    [Fact]
    public void Validate_NoDiagnosisSteps_LowPenalty()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(diagnosisSteps: []);
        var report = validator.Validate(pattern);
        var issue = report.Issues.Single(i => i.Field == "DiagnosisSteps");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal(0.05f, issue.Penalty);
    }

    [Fact]
    public void Validate_NoVerificationSteps_LowPenalty()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        var pattern = CreatePattern(verificationSteps: []);
        var report = validator.Validate(pattern);
        var issue = report.Issues.Single(i => i.Field == "VerificationSteps");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal(0.05f, issue.Penalty);
    }

    // --- Quality Score & Thresholds ---

    [Fact]
    public void Validate_QualityScore_ClampedBetween0And1()
    {
        var validator = new CaseCardQualityValidator(DefaultSettings());
        // Minimal bad pattern — many issues
        var pattern = CreatePattern(
            title: "",
            problemStatement: "",
            symptoms: [],
            diagnosisSteps: [],
            resolutionSteps: [],
            verificationSteps: [],
            relatedEvidenceIds: []);
        var report = validator.Validate(pattern);
        Assert.True(report.QualityScore >= 0f);
        Assert.True(report.QualityScore <= 1.0f);
    }

    [Fact]
    public void Validate_RejectedBelowThreshold()
    {
        var settings = new CaseCardQualitySettings { RejectThreshold = 0.5f };
        var validator = new CaseCardQualityValidator(settings);
        var pattern = CreatePattern(
            title: "",
            problemStatement: "",
            resolutionSteps: [],
            relatedEvidenceIds: []);
        var report = validator.Validate(pattern);
        Assert.True(report.Rejected);
    }

    [Fact]
    public void Validate_NotRejectedWhenThresholdIsZero()
    {
        var settings = new CaseCardQualitySettings { RejectThreshold = 0f };
        var validator = new CaseCardQualityValidator(settings);
        var pattern = CreatePattern(
            title: "",
            problemStatement: "",
            resolutionSteps: [],
            relatedEvidenceIds: []);
        var report = validator.Validate(pattern);
        Assert.False(report.Rejected);
    }

    [Fact]
    public void Validate_ConfigurableMinQualityScore()
    {
        var strictSettings = new CaseCardQualitySettings { MinQualityScore = 0.95f };
        var validator = new CaseCardQualityValidator(strictSettings);
        // Even a good pattern may not pass a very strict threshold
        var pattern = CreatePattern(diagnosisSteps: [], verificationSteps: []);
        var report = validator.Validate(pattern);
        // Two warnings (0.05 + 0.05 = 0.10 penalty → score 0.9), so fails 0.95 threshold
        Assert.False(report.Passed);
        Assert.False(report.Rejected); // but not rejected (above default reject threshold)
    }
}
