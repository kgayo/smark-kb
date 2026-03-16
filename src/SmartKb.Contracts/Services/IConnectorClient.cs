using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Abstraction for connector-specific operations. Each connector type (ADO, SharePoint, etc.)
/// implements this interface. Registered in DI as keyed services by ConnectorType.
/// </summary>
public interface IConnectorClient
{
    ConnectorType Type { get; }

    Task<TestConnectionResponse> TestConnectionAsync(
        string tenantId,
        string? sourceConfig,
        string? secretValue,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CanonicalRecord>> PreviewAsync(
        string tenantId,
        string? sourceConfig,
        FieldMappingConfig? fieldMapping,
        string? secretValue,
        int sampleSize,
        CancellationToken cancellationToken = default);
}
