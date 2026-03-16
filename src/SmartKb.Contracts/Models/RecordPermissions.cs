using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

/// <summary>
/// ACL metadata for a canonical record. Used for security trimming before retrieval.
/// </summary>
public sealed record RecordPermissions(
    AccessVisibility Visibility,
    IReadOnlyList<string> AllowedGroups);
