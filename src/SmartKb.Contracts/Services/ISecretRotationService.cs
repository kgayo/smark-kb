using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface ISecretRotationService
{
    Task<ConnectorCredentialStatus> GetCredentialStatusAsync(
        Guid connectorId,
        string tenantId,
        CancellationToken cancellationToken = default);

    Task<CredentialStatusSummary> GetAllCredentialStatusesAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    Task<CredentialRotationResult> RotateSecretAsync(
        Guid connectorId,
        string tenantId,
        string newSecretValue,
        string userId,
        CancellationToken cancellationToken = default);
}
