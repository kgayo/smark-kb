using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public sealed class OpenAiResponseHelperTests
{
    private sealed class SampleResult
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    // --- ExtractContent ---

    [Fact]
    public void ExtractContent_ValidResponse_ReturnsDeserializedObject()
    {
        var json = JsonDocument.Parse("""
        {
          "choices": [
            {
              "message": {
                "content": "{\"name\": \"test\", \"value\": 42}"
              }
            }
          ]
        }
        """).RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase);

        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ExtractContent_NoChoices_ReturnsNull()
    {
        var json = JsonDocument.Parse("""{ "choices": [] }""").RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractContent_MissingChoicesProperty_ReturnsNull()
    {
        var json = JsonDocument.Parse("""{ "id": "test" }""").RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractContent_EmptyContent_ReturnsNull()
    {
        var json = JsonDocument.Parse("""
        {
          "choices": [
            {
              "message": {
                "content": ""
              }
            }
          ]
        }
        """).RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractContent_NullContent_ReturnsNull()
    {
        var json = JsonDocument.Parse("""
        {
          "choices": [
            {
              "message": {
                "content": null
              }
            }
          ]
        }
        """).RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractContent_MalformedJson_ReturnsNullAndLogsWarning()
    {
        var json = JsonDocument.Parse("""
        {
          "choices": [
            {
              "message": {
                "content": "not valid json {"
              }
            }
          ]
        }
        """).RootElement;

        var logger = new CapturingLogger();

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase, logger);

        Assert.Null(result);
        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("SampleResult", logger.Entries[0].Message);
    }

    [Fact]
    public void ExtractContent_MalformedJson_NoLogger_DoesNotThrow()
    {
        var json = JsonDocument.Parse("""
        {
          "choices": [
            {
              "message": {
                "content": "{{bad}}"
              }
            }
          ]
        }
        """).RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractContent_MissingMessageProperty_ReturnsNull()
    {
        var json = JsonDocument.Parse("""
        {
          "choices": [
            {
              "index": 0
            }
          ]
        }
        """).RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.CamelCase);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractContent_RespectsJsonOptions()
    {
        var json = JsonDocument.Parse("""
        {
          "choices": [
            {
              "message": {
                "content": "{\"name\": \"snake_test\", \"value\": 7}"
              }
            }
          ]
        }
        """).RootElement;

        var result = OpenAiResponseHelper.ExtractContent<SampleResult>(
            json, SharedJsonOptions.SnakeCase);

        Assert.NotNull(result);
        Assert.Equal("snake_test", result.Name);
    }

    // --- ExtractTokenUsage ---

    [Fact]
    public void ExtractTokenUsage_ValidUsage_ReturnsAllFields()
    {
        var json = JsonDocument.Parse("""
        {
          "usage": {
            "prompt_tokens": 100,
            "completion_tokens": 50,
            "total_tokens": 150
          }
        }
        """).RootElement;

        var (prompt, completion, total) = OpenAiResponseHelper.ExtractTokenUsage(json);

        Assert.Equal(100, prompt);
        Assert.Equal(50, completion);
        Assert.Equal(150, total);
    }

    [Fact]
    public void ExtractTokenUsage_MissingUsage_ReturnsZeros()
    {
        var json = JsonDocument.Parse("""{ "id": "test" }""").RootElement;

        var (prompt, completion, total) = OpenAiResponseHelper.ExtractTokenUsage(json);

        Assert.Equal(0, prompt);
        Assert.Equal(0, completion);
        Assert.Equal(0, total);
    }

    [Fact]
    public void ExtractTokenUsage_PartialUsage_ReturnsAvailableFields()
    {
        var json = JsonDocument.Parse("""
        {
          "usage": {
            "prompt_tokens": 42
          }
        }
        """).RootElement;

        var (prompt, completion, total) = OpenAiResponseHelper.ExtractTokenUsage(json);

        Assert.Equal(42, prompt);
        Assert.Equal(0, completion);
        Assert.Equal(0, total);
    }

    // --- AddAuthorizationHeader ---

    [Fact]
    public void AddAuthorizationHeader_SetsCorrectBearerHeader()
    {
        using var request = new HttpRequestMessage();
        OpenAiResponseHelper.AddAuthorizationHeader(request, "sk-test-key-123");

        Assert.True(request.Headers.Contains("Authorization"));
        Assert.Equal("Bearer sk-test-key-123",
            request.Headers.GetValues("Authorization").Single());
    }

    // --- Helpers ---

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
