namespace SmartKb.Contracts.Models;

public sealed record HealthStatus(
    string Service,
    string Status,
    string Version,
    DateTimeOffset Timestamp);
