namespace SmartKb.Contracts.Configuration;

public sealed class OpenAiSettings
{
    public const string SectionName = "OpenAi";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
}
