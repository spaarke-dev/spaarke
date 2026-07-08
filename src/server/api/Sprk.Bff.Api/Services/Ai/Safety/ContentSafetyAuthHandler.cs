using System.Net.Http.Headers;

namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Auth handler for the named "ContentSafety" <c>HttpClient</c> (AI-ARCHITECTURE assessment
/// rec 3): selects managed-identity bearer auth vs API-key auth per request, following the
/// platform's MI cascade convention (<see cref="Infrastructure.Graph.GraphClientFactory"/>,
/// ADR-028 — managed identity in deployed environments, key/dev-credential fallback locally).
///
/// Auth selection (evaluated at call time so Key Vault rotation and config flips apply
/// without restart):
///   - <c>AiSafety:ContentSafety:ManagedIdentity:Enabled</c> = true  → Entra ID bearer token
///     (DefaultAzureCredential via <see cref="ContentSafetyTokenProvider"/>)
///   - API key ABSENT (<c>AiSafety:ContentSafety:ApiKey</c> unset)   → Entra ID bearer token
///   - otherwise                                                     → <c>Ocp-Apim-Subscription-Key</c>
///
/// The config section mirrors the existing <c>Graph:ManagedIdentity:Enabled</c> shape, nested
/// under the pre-existing <c>AiSafety:ContentSafety</c> section.
///
/// Applies uniformly to BOTH consumers of the named client:
/// <see cref="PromptShieldService"/> (shieldPrompt) and <see cref="GroundednessCheckService"/>
/// (detectGroundedness — which previously attached NO auth header at all and silently
/// failed open on 401).
///
/// Failure semantics: token acquisition failures throw and surface through each consumer's
/// existing fail-open path (never fail the chat turn). ADR-015: no prompt/document content
/// is logged here — the handler only touches headers.
///
/// OPERATOR PREREQUISITE (before enabling MI in any environment): grant the App Service
/// managed identity the "Cognitive Services User" role on the Content Safety resource.
/// </summary>
public sealed class ContentSafetyAuthHandler : DelegatingHandler
{
    /// <summary>Configuration key for the Content Safety API key (Key Vault-rotatable).</summary>
    public const string ApiKeyConfigKey = "AiSafety:ContentSafety:ApiKey";

    /// <summary>Configuration key for the managed-identity opt-in flag.</summary>
    public const string ManagedIdentityEnabledConfigKey = "AiSafety:ContentSafety:ManagedIdentity:Enabled";

    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";

    private readonly IConfiguration _configuration;
    private readonly ContentSafetyTokenProvider _tokenProvider;
    private readonly ILogger<ContentSafetyAuthHandler> _logger;

    public ContentSafetyAuthHandler(
        IConfiguration configuration,
        ContentSafetyTokenProvider tokenProvider,
        ILogger<ContentSafetyAuthHandler> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Read config at call time (supports Key Vault secret rotation + config flips).
        var apiKey = _configuration[ApiKeyConfigKey];
        var managedIdentityEnabled = _configuration.GetValue<bool>(ManagedIdentityEnabledConfigKey);

        if (managedIdentityEnabled || string.IsNullOrEmpty(apiKey))
        {
            if (!managedIdentityEnabled)
            {
                _logger.LogDebug(
                    "ContentSafetyAuth: no API key configured — falling back to managed-identity " +
                    "bearer auth (DefaultAzureCredential).");
            }

            var token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            request.Headers.Remove(SubscriptionKeyHeader);
            request.Headers.Add(SubscriptionKeyHeader, apiKey);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
