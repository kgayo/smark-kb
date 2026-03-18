namespace SmartKb.Contracts.Models;

/// <summary>
/// Team playbook DTO for API responses.
/// </summary>
public sealed record TeamPlaybookDto
{
    public required Guid Id { get; init; }
    public required string TeamName { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> RequiredFields { get; init; }
    public required IReadOnlyList<string> Checklist { get; init; }
    public string? ContactChannel { get; init; }
    public required bool RequiresApproval { get; init; }
    public string? MinSeverity { get; init; }
    public string? AutoRouteSeverity { get; init; }
    public int? MaxConcurrentEscalations { get; init; }
    public string? FallbackTeam { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// List of team playbooks for a tenant.
/// </summary>
public sealed record TeamPlaybookListResponse
{
    public required IReadOnlyList<TeamPlaybookDto> Playbooks { get; init; }
    public required int TotalCount { get; init; }
}

/// <summary>
/// Request to create a team playbook.
/// </summary>
public sealed record CreateTeamPlaybookRequest
{
    public required string TeamName { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> RequiredFields { get; init; } = [];
    public IReadOnlyList<string> Checklist { get; init; } = [];
    public string? ContactChannel { get; init; }
    public bool RequiresApproval { get; init; }
    public string? MinSeverity { get; init; }
    public string? AutoRouteSeverity { get; init; }
    public int? MaxConcurrentEscalations { get; init; }
    public string? FallbackTeam { get; init; }
}

/// <summary>
/// Request to update a team playbook. All fields are optional (patch semantics).
/// </summary>
public sealed record UpdateTeamPlaybookRequest
{
    public string? Description { get; init; }
    public IReadOnlyList<string>? RequiredFields { get; init; }
    public IReadOnlyList<string>? Checklist { get; init; }
    public string? ContactChannel { get; init; }
    public bool? RequiresApproval { get; init; }
    public string? MinSeverity { get; init; }
    public string? AutoRouteSeverity { get; init; }
    public int? MaxConcurrentEscalations { get; init; }
    public string? FallbackTeam { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>
/// Request to validate a draft against a team's playbook (standalone validation endpoint).
/// </summary>
public sealed record PlaybookValidateRequest
{
    public required string TargetTeam { get; init; }
    public required CreateEscalationDraftRequest Draft { get; init; }
}

/// <summary>
/// Result of validating an escalation draft against a team's playbook.
/// </summary>
public sealed record PlaybookValidationResult
{
    public required bool IsValid { get; init; }
    public required string TeamName { get; init; }
    public required IReadOnlyList<string> MissingRequiredFields { get; init; }
    public required IReadOnlyList<string> Checklist { get; init; }
    public string? ContactChannel { get; init; }
    public required bool RequiresApproval { get; init; }
    public string? PolicyViolation { get; init; }
}
