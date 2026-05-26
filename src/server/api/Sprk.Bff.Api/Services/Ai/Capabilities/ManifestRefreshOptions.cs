namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Configuration options for <see cref="ManifestRefreshService"/>.
///
/// Bound from the <c>Capabilities</c> configuration section:
/// <code>
/// {
///   "Capabilities": {
///     "RefreshIntervalMinutes": 15,
///     "WebhookPath": "/api/ai/capabilities/refresh"
///   }
/// }
/// </code>
/// </summary>
public sealed class ManifestRefreshOptions
{
    /// <summary>
    /// Configuration section name used for <see cref="IOptions{T}"/> binding.
    /// </summary>
    public const string SectionName = "Capabilities";

    /// <summary>
    /// How often (in minutes) the background poller re-loads the capability manifest
    /// from Dataverse. Defaults to 15 minutes.
    ///
    /// Set to a lower value in development to see refreshes faster.
    /// The timer uses <see cref="System.Threading.PeriodicTimer"/> which does not
    /// drift — each interval begins when the previous tick completes, not from the
    /// start of the previous tick.
    /// </summary>
    public int RefreshIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Shared secret that Dataverse plugin callbacks must supply in the
    /// <c>X-Webhook-Secret</c> request header to trigger an immediate refresh.
    ///
    /// If null or empty the webhook endpoint returns 401 for every request.
    /// Set via <c>AiCapabilities:WebhookSecret</c> in Key Vault / App Settings.
    /// </summary>
    public string? WebhookSecret { get; set; }
}
