using System.Net;
using System.Text;
using System.Text.Json;
using SmartKb.Contracts;

namespace SmartKb.Contracts.Tests;

public class ConnectorHttpHelperTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task DeserializeAsync_ReturnsDeserializedObject()
    {
        var json = """{"name":"test","value":42}""";
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        var result = await ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("test", result!.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_ReturnsNull_ForNullJsonBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        };

        var result = await ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_Throws_ForMalformedJson()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{invalid", Encoding.UTF8, "application/json"),
        };

        await Assert.ThrowsAsync<JsonException>(
            () => ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, CancellationToken.None));
    }

    [Fact]
    public async Task DeserializeAsync_RespectsJsonSerializerOptions()
    {
        var json = """{"Name":"PascalCase","Value":99}""";
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        // CamelCase policy won't match PascalCase property names unless case-insensitive
        var strictCamel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var result = await ConnectorHttpHelper.DeserializeAsync<TestDto>(response, strictCamel, CancellationToken.None);

        Assert.NotNull(result);
        // Default JsonSerializer is case-sensitive for property names when no PropertyNameCaseInsensitive is set
        Assert.Null(result!.Name);
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ConnectorHttpHelper.DeserializeAsync<TestDto>(response, CamelCase, cts.Token));
    }

    private sealed class TestDto
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }
}
