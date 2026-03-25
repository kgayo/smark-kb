using System.Text;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts.Services;

/// <summary>
/// API-side webhook notification client for eval regression alerts (P3-007).
/// Uses IHttpClientFactory for proper HttpClient lifecycle management.
/// </summary>
public sealed class WebhookEvalNotificationClient : IEvalNotificationService
{
    private readonly EvalNotificationSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookEvalNotificationClient> _logger;

    public WebhookEvalNotificationClient(
        EvalNotificationSettings settings,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookEvalNotificationClient> logger)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> NotifyAsync(EvalNotificationPayload payload, CancellationToken ct = default)
    {
        if (!_settings.IsConfigured)
            return true;

        if (!ShouldNotify(payload))
            return true;

        var body = BuildPayload(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientNames.EvalNotification);
            client.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

            using var response = await client.PostAsync(_settings.WebhookUrl, content, ct);

            if (response.IsSuccessStatusCode)
            {
                Diagnostics.EvalNotificationsSentTotal.Add(1);
                _logger.LogInformation(
                    "Eval notification sent. RunId={RunId}, Violations={Violations}, Blocking={Blocking}",
                    payload.RunId, payload.ViolationCount, payload.HasBlockingRegression);
                return true;
            }

            Diagnostics.EvalNotificationFailuresTotal.Add(1);
            _logger.LogWarning(
                "Eval notification failed. RunId={RunId}, StatusCode={StatusCode}",
                payload.RunId, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Diagnostics.EvalNotificationFailuresTotal.Add(1);
            _logger.LogWarning(ex,
                "Eval notification failed with exception. RunId={RunId}", payload.RunId);
            return false;
        }
    }

    private bool ShouldNotify(EvalNotificationPayload payload) =>
        EvalPayloadBuilder.ShouldNotify(payload, _settings.NotifyOnRegressions, _settings.NotifyOnViolations);

    private string BuildPayload(EvalNotificationPayload payload) =>
        EvalPayloadBuilder.BuildPayload(_settings.Format ?? "generic", payload);
}
