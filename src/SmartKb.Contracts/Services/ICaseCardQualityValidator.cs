using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Validates case-card (pattern) quality before indexing, producing a quality
/// score and actionable issues. Used as a gate in the distillation pipeline.
/// </summary>
public interface ICaseCardQualityValidator
{
    CaseCardQualityReport Validate(CasePattern pattern);
}
