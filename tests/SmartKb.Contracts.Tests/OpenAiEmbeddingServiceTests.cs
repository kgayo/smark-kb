using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using static SmartKb.Contracts.Services.OpenAiEmbeddingService;

namespace SmartKb.Contracts.Tests;

public class OpenAiEmbeddingServiceTests
{
    [Fact]
    public void EmbeddingResponse_Deserializes_CorrectStructure()
    {
        var response = new EmbeddingResponse
        {
            Data =
            [
                new EmbeddingData { Embedding = [0.1f, 0.2f, 0.3f] }
            ],
        };

        Assert.Single(response.Data);
        Assert.Equal(3, response.Data[0].Embedding.Length);
        Assert.Equal(0.1f, response.Data[0].Embedding[0]);
    }

    [Fact]
    public void EmbeddingResponse_EmptyData()
    {
        var response = new EmbeddingResponse();
        Assert.Empty(response.Data);
    }

    [Fact]
    public void EmbeddingSettings_Defaults_MatchConfig()
    {
        var settings = new EmbeddingSettings();
        Assert.Equal("text-embedding-3-large", settings.ModelId);
        Assert.Equal(1536, settings.Dimensions);
        Assert.Equal(8191, settings.MaxInputTokens);
    }

    [Fact]
    public void OpenAiSettings_Defaults()
    {
        var settings = new OpenAiSettings();
        Assert.Equal("gpt-4o", settings.Model);
        Assert.Equal("https://api.openai.com/v1", settings.Endpoint);
        Assert.Equal(string.Empty, settings.ApiKey);
    }

    [Fact]
    public void OpenAiSettings_SectionName()
    {
        Assert.Equal("OpenAi", OpenAiSettings.SectionName);
    }
}
