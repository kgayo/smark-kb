using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Audit;
using SmartKb.Api.Connectors;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Connectors;

public sealed class SecretRotationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _sp;
    private readonly SmartKbDbContext _db;
    private readonly InMemorySecretProvider _secretProvider;
    private readonly InMemoryAuditEventWriter _auditWriter;
    private readonly FixedTimeProvider _timeProvider;
    private readonly SecretRotationSettings _settings;

    private const string TenantId = "test-tenant";

    public SecretRotationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SmartKbDbContext>(o => o.UseSqlite(_connection));

        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<SmartKbDbContext>();
        _db.Database.EnsureCreated();

        // Seed a tenant.
        _db.Tenants.Add(new TenantEntity { TenantId = TenantId, DisplayName = "Test Tenant" });
        _db.SaveChanges();

        _secretProvider = new InMemorySecretProvider();
        _auditWriter = new InMemoryAuditEventWriter(
            NullLogger<InMemoryAuditEventWriter>.Instance);
        _timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero));
        _settings = new SecretRotationSettings
        {
            WarningThresholdDays = 30,
            CriticalThresholdDays = 7,
            MaxAgeDays = 90,
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _sp.Dispose();
    }

    private SecretRotationService CreateService(ISecretProvider? provider = null)
    {
        return new SecretRotationService(
            _db, _auditWriter, _settings,
            NullLogger<SecretRotationService>.Instance,
            _timeProvider,
            provider ?? _secretProvider);
    }

    private ConnectorEntity SeedConnector(
        string name = "Test ADO",
        ConnectorType type = ConnectorType.AzureDevOps,
        SecretAuthType authType = SecretAuthType.Pat,
        string? secretName = "test-secret")
    {
        var entity = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = name,
            ConnectorType = type,
            Status = ConnectorStatus.Enabled,
            AuthType = authType,
            KeyVaultSecretName = secretName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    // --- EvaluateHealth tests ---

    [Fact]
    public void EvaluateHealth_Healthy_WhenNoExpiryAndYoungSecret()
    {
        var service = CreateService();
        var props = new SecretProperties("s", DateTimeOffset.UtcNow.AddDays(-10), null, null, true);
        var (health, _) = service.EvaluateHealth(null, 10, props);
        Assert.Equal(CredentialHealth.Healthy, health);
    }

    [Fact]
    public void EvaluateHealth_Warning_WhenExpiresWithin30Days()
    {
        var service = CreateService();
        var props = new SecretProperties("s", null, null, null, true);
        var (health, msg) = service.EvaluateHealth(20, null, props);
        Assert.Equal(CredentialHealth.Warning, health);
        Assert.Contains("20 day(s)", msg);
    }

    [Fact]
    public void EvaluateHealth_Critical_WhenExpiresWithin7Days()
    {
        var service = CreateService();
        var props = new SecretProperties("s", null, null, null, true);
        var (health, _) = service.EvaluateHealth(3, null, props);
        Assert.Equal(CredentialHealth.Critical, health);
    }

    [Fact]
    public void EvaluateHealth_Expired_WhenDaysUntilExpiryIsNegative()
    {
        var service = CreateService();
        var props = new SecretProperties("s", null, null, null, true);
        var (health, _) = service.EvaluateHealth(-1, null, props);
        Assert.Equal(CredentialHealth.Expired, health);
    }

    [Fact]
    public void EvaluateHealth_Expired_WhenDisabled()
    {
        var service = CreateService();
        var props = new SecretProperties("s", null, null, null, false);
        var (health, _) = service.EvaluateHealth(100, null, props);
        Assert.Equal(CredentialHealth.Expired, health);
    }

    [Fact]
    public void EvaluateHealth_Warning_WhenSecretTooOld()
    {
        var service = CreateService();
        var props = new SecretProperties("s", null, null, null, true);
        var (health, msg) = service.EvaluateHealth(null, 120, props);
        Assert.Equal(CredentialHealth.Warning, health);
        Assert.Contains("120 day(s) old", msg);
    }

    [Fact]
    public void EvaluateHealth_Healthy_WhenMaxAgeDaysIsZero()
    {
        _settings.MaxAgeDays = 0;
        var service = CreateService();
        var props = new SecretProperties("s", null, null, null, true);
        var (health, _) = service.EvaluateHealth(null, 500, props);
        Assert.Equal(CredentialHealth.Healthy, health);
    }

    // --- GetCredentialStatusAsync tests ---

    [Fact]
    public async Task GetCredentialStatus_ReturnsUnknown_WhenConnectorNotFound()
    {
        var service = CreateService();
        var result = await service.GetCredentialStatusAsync(Guid.NewGuid(), TenantId);
        Assert.Equal(CredentialHealth.Unknown, result.Health);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public async Task GetCredentialStatus_ReturnsMissing_WhenNoSecretName()
    {
        var connector = SeedConnector(secretName: null);
        var service = CreateService();
        var result = await service.GetCredentialStatusAsync(connector.Id, TenantId);
        Assert.Equal(CredentialHealth.Missing, result.Health);
    }

    [Fact]
    public async Task GetCredentialStatus_ReturnsMissing_WhenSecretNotInVault()
    {
        var connector = SeedConnector(secretName: "missing-secret");
        var service = CreateService();
        var result = await service.GetCredentialStatusAsync(connector.Id, TenantId);
        Assert.Equal(CredentialHealth.Missing, result.Health);
        Assert.Contains("not found in Key Vault", result.Message);
    }

    [Fact]
    public async Task GetCredentialStatus_ReturnsHealthy_WhenSecretIsGood()
    {
        var connector = SeedConnector(secretName: "good-secret");
        _secretProvider.Secrets["good-secret"] = "value";
        _secretProvider.SecretPropertiesStore["good-secret"] = new SecretProperties(
            "good-secret",
            _timeProvider.GetUtcNow().AddDays(-5),
            _timeProvider.GetUtcNow().AddDays(-5),
            _timeProvider.GetUtcNow().AddDays(60),
            true);

        var service = CreateService();
        var result = await service.GetCredentialStatusAsync(connector.Id, TenantId);
        Assert.Equal(CredentialHealth.Healthy, result.Health);
        Assert.Equal(60, result.DaysUntilExpiry);
        Assert.Equal(5, result.AgeDays);
    }

    [Fact]
    public async Task GetCredentialStatus_ReturnsUnknown_WhenNoKeyVault()
    {
        var connector = SeedConnector();
        var service = CreateService(provider: null!);
        // Pass null explicitly via a wrapper
        var svc = new SecretRotationService(
            _db, _auditWriter, _settings,
            NullLogger<SecretRotationService>.Instance,
            _timeProvider,
            secretProvider: null);
        var result = await svc.GetCredentialStatusAsync(connector.Id, TenantId);
        Assert.Equal(CredentialHealth.Unknown, result.Health);
    }

    // --- GetAllCredentialStatusesAsync tests ---

    [Fact]
    public async Task GetAllCredentialStatuses_ReturnsCorrectCounts()
    {
        var c1 = SeedConnector(name: "C1", secretName: "s1");
        var c2 = SeedConnector(name: "C2", secretName: null);
        var c3 = SeedConnector(name: "C3", secretName: "s3");

        _secretProvider.Secrets["s1"] = "val";
        _secretProvider.SecretPropertiesStore["s1"] = new SecretProperties(
            "s1", _timeProvider.GetUtcNow().AddDays(-5), null,
            _timeProvider.GetUtcNow().AddDays(60), true);
        _secretProvider.Secrets["s3"] = "val";
        _secretProvider.SecretPropertiesStore["s3"] = new SecretProperties(
            "s3", _timeProvider.GetUtcNow().AddDays(-5), null,
            _timeProvider.GetUtcNow().AddDays(-1), true); // expired

        var service = CreateService();
        var result = await service.GetAllCredentialStatusesAsync(TenantId);

        Assert.Equal(3, result.TotalConnectors);
        Assert.Equal(1, result.HealthyCount);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(1, result.ExpiredCount);
    }

    [Fact]
    public async Task GetAllCredentialStatuses_ReturnsEmpty_WhenNoConnectors()
    {
        var service = CreateService();
        var result = await service.GetAllCredentialStatusesAsync("non-existent-tenant");
        Assert.Empty(result.Connectors);
        Assert.Equal(0, result.TotalConnectors);
    }

    // --- RotateSecretAsync tests ---

    [Fact]
    public async Task RotateSecret_Succeeds_WhenValid()
    {
        var connector = SeedConnector(secretName: "rotate-me");
        _secretProvider.Secrets["rotate-me"] = "old-value";

        var service = CreateService();
        var result = await service.RotateSecretAsync(
            connector.Id, TenantId, "new-value", "admin@test.com");

        Assert.True(result.Success);
        Assert.Equal("new-value", _secretProvider.Secrets["rotate-me"]);
        Assert.NotNull(result.NewSecretCreatedOn);

        var events = _auditWriter.GetEvents();
        Assert.Contains(events, e => e.EventType == AuditEventTypes.CredentialRotationCompleted);
    }

    [Fact]
    public async Task RotateSecret_Fails_WhenConnectorNotFound()
    {
        var service = CreateService();
        var result = await service.RotateSecretAsync(
            Guid.NewGuid(), TenantId, "new", "admin");
        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public async Task RotateSecret_Fails_WhenNoSecretName()
    {
        var connector = SeedConnector(secretName: null);
        var service = CreateService();
        var result = await service.RotateSecretAsync(
            connector.Id, TenantId, "new", "admin");
        Assert.False(result.Success);
        Assert.Contains("no Key Vault secret", result.Message);
    }

    [Fact]
    public async Task RotateSecret_Fails_WhenOAuthConnector()
    {
        var connector = SeedConnector(authType: SecretAuthType.OAuth, secretName: "oauth-secret");
        var service = CreateService();
        var result = await service.RotateSecretAsync(
            connector.Id, TenantId, "new", "admin");
        Assert.False(result.Success);
        Assert.Contains("OAuth", result.Message);
    }

    [Fact]
    public async Task RotateSecret_Fails_WhenNoKeyVault()
    {
        var connector = SeedConnector();
        var svc = new SecretRotationService(
            _db, _auditWriter, _settings,
            NullLogger<SecretRotationService>.Instance,
            _timeProvider,
            secretProvider: null);
        var result = await svc.RotateSecretAsync(
            connector.Id, TenantId, "new", "admin");
        Assert.False(result.Success);
        Assert.Contains("not configured", result.Message);
    }

    [Fact]
    public async Task RotateSecret_WritesFailureAuditEvent_OnException()
    {
        var connector = SeedConnector(secretName: "fail-secret");
        var failingProvider = new FailingSecretProvider();

        var svc = new SecretRotationService(
            _db, _auditWriter, _settings,
            NullLogger<SecretRotationService>.Instance,
            _timeProvider,
            failingProvider);

        var result = await svc.RotateSecretAsync(
            connector.Id, TenantId, "new", "admin");

        Assert.False(result.Success);
        var events = _auditWriter.GetEvents();
        Assert.Contains(events, e => e.EventType == AuditEventTypes.CredentialRotationFailed);
    }

    [Fact]
    public async Task RotateSecret_EnforcesTenantIsolation()
    {
        var connector = SeedConnector();
        var service = CreateService();
        var result = await service.RotateSecretAsync(
            connector.Id, "other-tenant", "new", "admin");
        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    // --- Settings tests ---

    [Fact]
    public void SecretRotationSettings_HasCorrectDefaults()
    {
        var s = new SecretRotationSettings();
        Assert.Equal(30, s.WarningThresholdDays);
        Assert.Equal(7, s.CriticalThresholdDays);
        Assert.Equal(90, s.MaxAgeDays);
    }

    /// <summary>A secret provider that throws on SetSecretAsync.</summary>
    private sealed class FailingSecretProvider : ISecretProvider
    {
        public Task<string> GetSecretAsync(string secretName, CancellationToken ct = default) =>
            Task.FromResult("value");
        public Task SetSecretAsync(string secretName, string secretValue, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated Key Vault failure");
        public Task DeleteSecretAsync(string secretName, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<SecretProperties?> GetSecretPropertiesAsync(string secretName, CancellationToken ct = default) =>
            Task.FromResult<SecretProperties?>(new SecretProperties(secretName, DateTimeOffset.UtcNow, null, null, true));
    }

    /// <summary>A simple TimeProvider that returns a fixed time.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
