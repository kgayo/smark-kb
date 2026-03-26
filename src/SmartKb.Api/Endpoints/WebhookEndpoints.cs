using Microsoft.AspNetCore.Mvc;
using SmartKb.Api.Webhooks;

namespace SmartKb.Api.Endpoints;

public static class WebhookEndpoints
{
    public static WebApplication MapWebhookEndpoints(this WebApplication app)
    {
        // --- Webhook Receiver Endpoints (anonymous — HMAC-verified inside handler) ---

        app.MapPost("/api/webhooks/ado/{connectorId:guid}", async (
            Guid connectorId,
            HttpContext httpContext,
            [FromServices] AdoWebhookHandler handler) =>
        {
            // Read raw body for signature verification.
            var ct = httpContext.RequestAborted;
            using var reader = new StreamReader(httpContext.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();

            var (statusCode, message) = await handler.HandleAsync(connectorId, body, authHeader, ct);
            return Results.Json(new { message }, statusCode: statusCode);
        }).AllowAnonymous();

        // Graph change notification endpoint — supports validation handshake (POST with validationToken query param)
        // and change notification processing. Anonymous because Graph validates via clientState.
        app.MapPost("/api/webhooks/msgraph/{connectorId:guid}", async (
            Guid connectorId,
            HttpContext httpContext,
            [FromServices] SharePointWebhookHandler handler) =>
        {
            // Graph subscription validation handshake: POST with ?validationToken=...
            var validationToken = httpContext.Request.Query["validationToken"].FirstOrDefault();
            if (!string.IsNullOrEmpty(validationToken))
            {
                var (code, contentType, body) = handler.HandleValidation(validationToken);
                return Results.Content(body, contentType, statusCode: code);
            }

            // Normal change notification.
            var ct = httpContext.RequestAborted;
            using var reader = new StreamReader(httpContext.Request.Body);
            var requestBody = await reader.ReadToEndAsync(ct);

            var (statusCode, message) = await handler.HandleNotificationAsync(connectorId, requestBody, ct);
            return Results.Json(new { message }, statusCode: statusCode);
        }).AllowAnonymous();

        // HubSpot webhook endpoint — validates HMAC-SHA256 signature via X-HubSpot-Signature-v3 header.
        app.MapPost("/api/webhooks/hubspot/{connectorId:guid}", async (
            Guid connectorId,
            HttpContext httpContext,
            [FromServices] HubSpotWebhookHandler handler) =>
        {
            var ct = httpContext.RequestAborted;
            using var reader = new StreamReader(httpContext.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var signatureHeader = httpContext.Request.Headers["X-HubSpot-Signature-v3"].FirstOrDefault()
                ?? httpContext.Request.Headers["X-HubSpot-Signature"].FirstOrDefault();
            var timestampHeader = httpContext.Request.Headers["X-HubSpot-Request-Timestamp"].FirstOrDefault();

            var (statusCode, message) = await handler.HandleAsync(connectorId, body, signatureHeader, timestampHeader, ct);
            return Results.Json(new { message }, statusCode: statusCode);
        }).AllowAnonymous();

        // ClickUp webhook endpoint — validates HMAC-SHA256 signature via X-Signature header.
        app.MapPost("/api/webhooks/clickup/{connectorId:guid}", async (
            Guid connectorId,
            HttpContext httpContext,
            [FromServices] ClickUpWebhookHandler handler) =>
        {
            var ct = httpContext.RequestAborted;
            using var reader = new StreamReader(httpContext.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var signatureHeader = httpContext.Request.Headers["X-Signature"].FirstOrDefault();

            var (statusCode, message) = await handler.HandleAsync(connectorId, body, signatureHeader, ct);
            return Results.Json(new { message }, statusCode: statusCode);
        }).AllowAnonymous();

        return app;
    }
}
