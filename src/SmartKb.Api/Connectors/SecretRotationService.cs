using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;

namespace SmartKb.Api.Connectors;

public sealed class SecretRotationService : ISecretRotationService
{
    private readonly SmartKbDbContext _db;
    private readonly ISecretProvider? _secretProvider;
    private readonly IAuditEventWriter _auditWriter;
    private readonly SecretRotationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecretRotationService> _logger;

    public SecretRotationService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        SecretRotationSettings settings,
        ILogger<SecretRotationService> logger,
        TimeProvider? timeProvider = null,
        ISecretProvider? secretProvider = null)
    {
        _db = db;
        _secretProvider = secretProvider;
        _auditWriter = auditWriter;
        _settings = settings;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<ConnectorCredentialStatus> GetCredentialStatusAsync(
        Guid connectorId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var connector = await _db.Connectors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectorId && c.TenantId == tenantId, cancellationToken);

        if (connector is null)
            return new ConnectorCredentialStatus(
                connectorId, "Unknown", "Unknown", "Unknown",
                CredentialHealth.Unknown, null, null, null, null, null,
                ResponseMessages.ConnectorNotFound);

        return await EvaluateCredentialAsync(connector, cancellationToken);
    }

    public async Task<CredentialStatusSummary> GetAllCredentialStatusesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var connectors = await _db.Connectors
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var statuses = new List<ConnectorCredentialStatus>(connectors.Count);
        foreach (var connector in connectors)
        {
            var status = await EvaluateCredentialAsync(connector, cancellationToken);
            statuses.Add(status);

            if (status.Health is CredentialHealth.Warning or CredentialHealth.Critical or CredentialHealth.Expired)
            {
                Diagnostics.CredentialExpiryWarningsTotal.Add(1,
                    new System.Diagnostics.TagList
                    {
                        { "connector_type", connector.ConnectorType.ToString() },
                        { "health", status.Health.ToString() },
                    });
            }
        }

        return new CredentialStatusSummary(
            statuses,
            statuses.Count,
            statuses.Count(s => s.Health == CredentialHealth.Healthy),
            statuses.Count(s => s.Health == CredentialHealth.Warning),
            statuses.Count(s => s.Health == CredentialHealth.Critical),
            statuses.Count(s => s.Health == CredentialHealth.Expired),
            statuses.Count(s => s.Health == CredentialHealth.Missing));
    }

    public async Task<CredentialRotationResult> RotateSecretAsync(
        Guid connectorId,
        string tenantId,
        string newSecretValue,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (_secretProvider is null)
            return new CredentialRotationResult(false, "Key Vault is not configured.", null);

        var connector = await _db.Connectors
            .FirstOrDefaultAsync(c => c.Id == connectorId && c.TenantId == tenantId, cancellationToken);

        if (connector is null)
            return new CredentialRotationResult(false, ResponseMessages.ConnectorNotFound, null);

        if (string.IsNullOrEmpty(connector.KeyVaultSecretName))
            return new CredentialRotationResult(false, "Connector has no Key Vault secret configured.", null);

        if (connector.AuthType == SecretAuthType.OAuth)
            return new CredentialRotationResult(false,
                "OAuth connectors use automatic token refresh. Manual rotation is not supported.", null);

        try
        {
            await _secretProvider.SetSecretAsync(connector.KeyVaultSecretName, newSecretValue, cancellationToken);

            var now = _timeProvider.GetUtcNow();
            connector.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);

            await _auditWriter.WriteAsync(new Contracts.Models.AuditEvent(
                Guid.NewGuid().ToString(),
                AuditEventTypes.CredentialRotationCompleted,
                tenantId,
                userId,
                string.Empty,
                now,
                $"Credential rotated for connector '{connector.Name}' ({connector.Id})"),
                cancellationToken);

            Diagnostics.CredentialRotationsTotal.Add(1,
                new System.Diagnostics.TagList
                {
                    { "connector_type", connector.ConnectorType.ToString() },
                });

            _logger.LogInformation(
                "Credential rotated for connector {ConnectorId} ({ConnectorName}) by {UserId}",
                connectorId, connector.Name, userId);

            return new CredentialRotationResult(true, "Credential rotated successfully.", now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Credential rotation failed for connector {ConnectorId}", connectorId);

            await _auditWriter.WriteAsync(new Contracts.Models.AuditEvent(
                Guid.NewGuid().ToString(),
                AuditEventTypes.CredentialRotationFailed,
                tenantId,
                userId,
                string.Empty,
                _timeProvider.GetUtcNow(),
                $"Credential rotation failed for connector '{connector.Name}' ({connector.Id}): {ex.Message}"),
                cancellationToken);

            return new CredentialRotationResult(false, $"Rotation failed: {ex.Message}", null);
        }
    }

    internal async Task<ConnectorCredentialStatus> EvaluateCredentialAsync(
        Data.Entities.ConnectorEntity connector,
        CancellationToken cancellationToken)
    {
        var connectorType = connector.ConnectorType.ToString();
        var authType = connector.AuthType.ToString();

        if (string.IsNullOrEmpty(connector.KeyVaultSecretName))
        {
            return new ConnectorCredentialStatus(
                connector.Id, connector.Name, connectorType, authType,
                CredentialHealth.Missing, null, null, null, null, null,
                "No Key Vault secret configured.");
        }

        if (_secretProvider is null)
        {
            return new ConnectorCredentialStatus(
                connector.Id, connector.Name, connectorType, authType,
                CredentialHealth.Unknown, connector.KeyVaultSecretName,
                null, null, null, null,
                "Key Vault is not configured.");
        }

        SecretProperties? props;
        try
        {
            props = await _secretProvider.GetSecretPropertiesAsync(
                connector.KeyVaultSecretName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to read secret properties for connector {ConnectorId}", connector.Id);
            return new ConnectorCredentialStatus(
                connector.Id, connector.Name, connectorType, authType,
                CredentialHealth.Unknown, connector.KeyVaultSecretName,
                null, null, null, null,
                $"Failed to read secret metadata: {ex.Message}");
        }

        if (props is null)
        {
            return new ConnectorCredentialStatus(
                connector.Id, connector.Name, connectorType, authType,
                CredentialHealth.Missing, connector.KeyVaultSecretName,
                null, null, null, null,
                "Secret not found in Key Vault.");
        }

        var now = _timeProvider.GetUtcNow();
        int? daysUntilExpiry = props.ExpiresOn.HasValue
            ? (int)(props.ExpiresOn.Value - now).TotalDays
            : null;
        int? ageDays = props.CreatedOn.HasValue
            ? (int)(now - props.CreatedOn.Value).TotalDays
            : null;

        var (health, message) = EvaluateHealth(daysUntilExpiry, ageDays, props);

        return new ConnectorCredentialStatus(
            connector.Id, connector.Name, connectorType, authType,
            health, connector.KeyVaultSecretName,
            props.CreatedOn, props.ExpiresOn, daysUntilExpiry, ageDays,
            message);
    }

    internal (CredentialHealth Health, string Message) EvaluateHealth(
        int? daysUntilExpiry, int? ageDays, SecretProperties props)
    {
        if (!props.Enabled)
            return (CredentialHealth.Expired, "Secret is disabled in Key Vault.");

        if (daysUntilExpiry.HasValue)
        {
            if (daysUntilExpiry.Value <= 0)
                return (CredentialHealth.Expired, "Secret has expired.");

            if (daysUntilExpiry.Value <= _settings.CriticalThresholdDays)
                return (CredentialHealth.Critical,
                    $"Secret expires in {daysUntilExpiry.Value} day(s).");

            if (daysUntilExpiry.Value <= _settings.WarningThresholdDays)
                return (CredentialHealth.Warning,
                    $"Secret expires in {daysUntilExpiry.Value} day(s).");
        }

        if (_settings.MaxAgeDays > 0 && ageDays.HasValue && ageDays.Value > _settings.MaxAgeDays)
        {
            return (CredentialHealth.Warning,
                $"Secret is {ageDays.Value} day(s) old (max recommended: {_settings.MaxAgeDays}).");
        }

        return (CredentialHealth.Healthy, "Credential is healthy.");
    }
}
