using Microsoft.Extensions.Options;
using SmartKb.Contracts.Configuration;

namespace SmartKb.Api.Secrets;

public sealed class OpenAiKeyProvider
{
    private readonly OpenAiSettings _settings;

    public OpenAiKeyProvider(IOptions<OpenAiSettings> settings)
    {
        _settings = settings.Value;
    }

    public string GetApiKey()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key is not configured. Set the 'OpenAi:ApiKey' application setting.");
        }

        return _settings.ApiKey;
    }

    public string GetModel() => _settings.Model;

    public string GetEndpoint() => _settings.Endpoint;
}
