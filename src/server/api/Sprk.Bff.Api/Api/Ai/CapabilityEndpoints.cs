using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Capabilities;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Minimal API endpoints for AI capability management.
///
/// Currently exposes a single webhook endpoint used by Dataverse plugins to signal
/// that a capability record has been created, updated, or deactivated. Receiving
/// this signal causes <see cref="ManifestRefreshService"/> to perform an immediate
/// out-of-schedule refresh of the in-memory <see cref="CapabilityManifest"/>.
///
/// Authentication: shared-secret header (<c>X-Webhook-Secret</c>) rather than
/// Azure AD Bearer token, because the caller is a Dataverse plugin (server-to-server,
/// no user context). The secret is read from <c>AiCapabilities:WebhookSecret</c>
/// in Key Vault / App Settings.
///
/// ADR-001: Minimal API (no Azure Functions).
/// ADR-008: No global auth middleware — authentication is handled inline.
/// </summary>
public static class CapabilityEndpoints
{
    private const string WebhookSecretHeader = "X-Webhook-Secret";

    /// <summary>
    /// Registers the capability management endpoints.
    /// Called from <see cref="Infrastructure.DI.EndpointMappingExtensions.MapSpaarkeEndpoints"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapCapabilityEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/ai/capabilities/refresh
        // Called by Dataverse plugins when a capability record changes.
        // Does NOT use RequireAuthorization() because this endpoint uses a shared-secret
        // scheme rather than Azure AD Bearer token (plugin → BFF server-to-server).
        app.MapPost("/api/ai/capabilities/refresh", HandleWebhookRefreshAsync)
            .AllowAnonymous()
            .WithTags("AI Capabilities")
            .WithName("RefreshCapabilityManifest")
            .WithSummary("Trigger an immediate capability manifest refresh")
            .WithDescription(
                "Called by Dataverse plugins when a capability record is created, updated, or deactivated. " +
                "Requires the X-Webhook-Secret header matching AiCapabilities:WebhookSecret. " +
                "Returns 204 on success, 401 when the secret is missing or incorrect.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    // ── Handler ───────────────────────────────────────────────────────────────

    private static IResult HandleWebhookRefreshAsync(
        HttpContext context,
        IManifestRefreshTrigger trigger,
        IOptions<ManifestRefreshOptions> options,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.CapabilityEndpoints");

        // ── Secret validation ────────────────────────────────────────────────
        var configuredSecret = options.Value.WebhookSecret;

        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            // No secret configured — deny all requests to avoid an open endpoint.
            logger.LogWarning(
                "CapabilityEndpoints: POST /api/ai/capabilities/refresh rejected — " +
                "AiCapabilities:WebhookSecret is not configured. " +
                "Configure the secret to enable webhook-triggered refreshes.");

            return Results.Problem(
                detail: "Webhook secret is not configured on the server. Contact the platform administrator.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        if (!context.Request.Headers.TryGetValue(WebhookSecretHeader, out var providedSecret) ||
            !string.Equals(configuredSecret, providedSecret.ToString(), StringComparison.Ordinal))
        {
            logger.LogWarning(
                "CapabilityEndpoints: POST /api/ai/capabilities/refresh rejected — " +
                "missing or incorrect {Header} header",
                WebhookSecretHeader);

            return Results.Problem(
                detail: $"Missing or incorrect {WebhookSecretHeader} header.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        // ── Trigger refresh ──────────────────────────────────────────────────
        trigger.TriggerRefresh();

        logger.LogInformation(
            "CapabilityEndpoints: immediate manifest refresh triggered via webhook");

        // 204 No Content — refresh is async (fire-and-forget from the caller's perspective).
        return Results.NoContent();
    }
}
