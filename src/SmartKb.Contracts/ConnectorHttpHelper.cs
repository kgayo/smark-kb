using System.Text.Json;

namespace SmartKb.Contracts;

/// <summary>
/// Shared HTTP response deserialization helper for connector clients and webhook managers.
/// Eliminates duplicate private static DeserializeAsync methods across 8 connector files.
/// </summary>
public static class ConnectorHttpHelper
{
    public static async Task<T?> DeserializeAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions options,
        CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, options, ct);
    }
}
