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
/// OPERATOR PREREQUISITE: the App Service managed identity needs the
/// "Cognitive Services User" role on the account serving Content Safety. That role grants
/// dataActions <c>Microsoft.CognitiveServices/*</c>, which covers
/// <c>accounts/ContentSafety/text:shieldprompt/action</c>. NOTE that
/// "Cognitive Services OpenAI User" does NOT cover it — its dataActions are OpenAI-only, so a
/// principal can hold OpenAI inference rights on the very same account and still get HTTP 401
/// PermissionDenied here. Content Safety is served by the multi-service AIServices account
/// (dev: <c>spaarke-openai-dev</c>); there is no dedicated ContentSafety-kind resource in any
/// Spaarke subscription. Verified present for the dev UAMI on 2026-08-21
/// (spaarke-auth-v4-dataverse-MI FR-E1). There is no API key: the setting was removed from
/// appsettings.template.json because it referenced a Key Vault secret that does not exist.
///
/// ⚠️ CORRECT AUTH IS NOT SUFFICIENT FOR A WORKING PERIMETER. Over the full 90-day App
/// Insights retention window measured 2026-08-21, dev recorded 122 Prompt Shield scans and
/// ZERO completions: every one was cancelled at the 100ms
/// <see cref="PromptShieldService"/> deadline and failed OPEN. Auth is not the bottleneck —
/// <c>DefaultAzureCredential.GetToken</c> averages 7ms (p95 1ms) and the token is cached by
/// <see cref="ContentSafetyTokenProvider"/>; the shieldPrompt call itself does not answer
/// inside the budget (43 dependency records, all resultCode=0, p50 92ms / p95 99ms — i.e.
/// client-side cancellation, never a server response). Restoring an API key would NOT fix
/// this. Before trusting this perimeter in ANY environment, check
/// scripts/kql/ai-metering/shield-coverage.kql for a non-zero completed count.
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
