namespace SmartKb.Contracts;

/// <summary>
/// Entra ID (Azure AD) JWT claim type names used for tenant context, user identity, and group membership.
/// </summary>
public static class EntraClaimTypes
{
    /// <summary>Tenant ID claim from Entra ID tokens.</summary>
    public const string TenantId = "tid";

    /// <summary>Object ID (user principal) claim from Entra ID tokens.</summary>
    public const string ObjectId = "oid";

    /// <summary>Subject claim — fallback user identifier when "oid" is absent.</summary>
    public const string Subject = "sub";

    /// <summary>Group memberships claim from Entra ID tokens (used for ACL security trimming).</summary>
    public const string Groups = "groups";

    /// <summary>App roles claim from Entra ID tokens.</summary>
    public const string Roles = "roles";
}
