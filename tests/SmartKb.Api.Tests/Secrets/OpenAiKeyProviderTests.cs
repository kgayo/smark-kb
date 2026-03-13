using Microsoft.Extensions.Options;
using SmartKb.Api.Secrets;
using SmartKb.Contracts.Configuration;

namespace SmartKb.Api.Tests.Secrets;

public class OpenAiKeyProviderTests
{
    [Fact]
    public void GetApiKey_WithConfiguredKey_ReturnsKey()
    {
        var settings = new OpenAiSettings { ApiKey = "sk-test-key-12345" };
        var provider = new OpenAiKeyProvider(Options.Create(settings));

        var key = provider.GetApiKey();

        Assert.Equal("sk-test-key-12345", key);
    }

    [Fact]
    public void GetApiKey_WithEmptyKey_ThrowsInvalidOperationException()
    {
        var settings = new OpenAiSettings { ApiKey = "" };
        var provider = new OpenAiKeyProvider(Options.Create(settings));

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetApiKey());
        Assert.Contains("OpenAi:ApiKey", ex.Message);
    }

    [Fact]
    public void GetApiKey_WithWhitespaceKey_ThrowsInvalidOperationException()
    {
        var settings = new OpenAiSettings { ApiKey = "   " };
        var provider = new OpenAiKeyProvider(Options.Create(settings));

        Assert.Throws<InvalidOperationException>(() => provider.GetApiKey());
    }

    [Fact]
    public void GetModel_ReturnsConfiguredModel()
    {
        var settings = new OpenAiSettings { Model = "gpt-4-turbo" };
        var provider = new OpenAiKeyProvider(Options.Create(settings));

        Assert.Equal("gpt-4-turbo", provider.GetModel());
    }

    [Fact]
    public void GetModel_DefaultsToGpt4o()
    {
        var settings = new OpenAiSettings();
        var provider = new OpenAiKeyProvider(Options.Create(settings));

        Assert.Equal("gpt-4o", provider.GetModel());
    }

    [Fact]
    public void GetEndpoint_ReturnsConfiguredEndpoint()
    {
        var settings = new OpenAiSettings { Endpoint = "https://custom.openai.azure.com" };
        var provider = new OpenAiKeyProvider(Options.Create(settings));

        Assert.Equal("https://custom.openai.azure.com", provider.GetEndpoint());
    }

    [Fact]
    public void GetEndpoint_DefaultsToOpenAiApi()
    {
        var settings = new OpenAiSettings();
        var provider = new OpenAiKeyProvider(Options.Create(settings));

        Assert.Equal("https://api.openai.com/v1", provider.GetEndpoint());
    }
}
