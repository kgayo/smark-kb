using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Eval.Cli;

/// <summary>
/// Sends eval regression/violation notifications via webhook (Slack, Teams, or generic HTTP).
/// Supports three payload formats configurable via <see cref="EvalNotificationSettings.Format"/>.
/// </summary>
public sealed class WebhookEvalNotificationService : IEvalNotificationService, IDisposable
{
    private readonly EvalNotificationSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger<WebhookEvalNotificationService> _logger;

    public WebhookEvalNotificationService(EvalNotificationSettings settings, HttpClient? httpClient = null, ILogger<WebhookEvalNotificationService>? logger = null)
    {
        _settings = settings;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) };
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<WebhookEvalNotificationService>();
    }

    public async Task<bool> NotifyAsync(EvalNotificationPayload payload, CancellationToken ct = default)
    {
        if (!_settings.IsConfigured)
            return true; // No webhook configured — nothing to do.

        if (!ShouldNotify(payload))
            return true; // Notification not needed for this payload.

        var body = BuildPayload(payload);
        using var content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json);

        try
        {
            using var response = await _httpClient.PostAsync(_settings.WebhookUrl, content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Eval notification failed with exception. RunId={RunId}", payload.RunId);
            return false;
        }
    }

    internal bool ShouldNotify(EvalNotificationPayload payload) =>
        EvalPayloadBuilder.ShouldNotify(payload, _settings.NotifyOnRegressions, _settings.NotifyOnViolations);

    internal string BuildPayload(EvalNotificationPayload payload) =>
        EvalPayloadBuilder.BuildPayload(_settings.Format ?? "generic", payload);

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
