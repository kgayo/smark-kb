using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

public sealed record ConnectorSecretReference(
    string ConnectorId,
    string TenantId,
    SecretAuthType AuthType,
    string KeyVaultSecretName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt);
