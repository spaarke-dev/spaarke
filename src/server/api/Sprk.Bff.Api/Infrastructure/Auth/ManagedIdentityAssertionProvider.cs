using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// <see cref="IClientAssertionProvider"/> backed by a <b>Managed Identity federated credential</b>
/// (MI-FIC) — the default secret-free confidential credential under ADR-028 Amendment <b>A4</b>.
///
/// <para><b>How the mechanism works, since it is not obvious.</b> Two different identities are
/// involved on purpose. The App Service's <b>user-assigned managed identity</b> mints a token for the
/// audience <c>api://AzureADTokenExchange</c>; that token is presented to Entra as the
/// <c>client_assertion</c> for the <b>app registration</b>, which trusts it through a federated
/// identity credential whose subject is the UAMI's <b>principalId</b>. The BFF therefore still acts as
/// itself — the app registration — while proving it without a secret. Confusing the two identities is
/// the designated silent-failure hazard (FR-B4): pass the app registration's clientId here and
/// assertion minting fails with <c>managed_identity_request_failed</c> / "No User Assigned or Delegated
/// Managed Identity found", which task 002 verified as a loud, catchable failure rather than a
/// permissive one.</para>
///
/// <para><b>Why this class holds one assertion object for the process.</b> ADR-028 A4 prescribes
/// instance reuse, and reuse avoids rebuilding the underlying managed-identity application and
/// re-entering MSAL's cache on every token acquisition.
/// <b>Note what the reason is NOT</b>: it is <i>not</i> that a per-instance object would cause an IMDS
/// round trip per call. An earlier version of this comment claimed that, and it is false — measured at
/// task 020's code-review gate, five freshly-constructed <c>ManagedIdentityClientAssertion</c>
/// instances produced <b>zero</b> additional IMDS requests, because MSAL's managed-identity token cache
/// is <b>process-static and keyed by identity</b>, not per-assertion-object. The singleton is still
/// correct; the cost it avoids is an allocation and a cache lookup, not a network call.</para>
///
/// <para><b>Construction never touches the network.</b> Deliberately: on a workstation there is no IMDS
/// endpoint, and a constructor that probed it would fail at <i>startup</i>, taking down local
/// development and defeating the ordered credential selection task 021 builds. Failure surfaces at
/// first call instead — measured at ~80 ms, not a timeout — as a catchable
/// <c>MsalServiceException</c>.</para>
///
/// <para><b>The conflation guard is NOT here, deliberately (task 023).</b> Checking the two identities
/// at construction is impossible without breaking the property above — this constructor must never
/// fail, or local development stops booting and task 021's ordered selection loses the fall-through it
/// depends on. Configuration <i>coherence</i>, unlike credential <i>availability</i>, is knowable
/// without a network, so it is asserted at startup instead by
/// <see cref="Configuration.IdentityConfigurationValidator"/>. This class needed no change for FR-B4:
/// it already resolves the UAMI through the shared resolver and never reads the app-registration id, so
/// there is no cross-defaulting here to remove.</para>
///
/// <para>Introduced by <c>spaarke-auth-v4-dataverse-MI</c> task 020 (FR-B1).</para>
/// </summary>
public sealed class ManagedIdentityAssertionProvider : IClientAssertionProvider
{
    /// <summary>
    /// Optional override for the token-exchange audience. Defaults to
    /// <c>api://AzureADTokenExchange</c>, which is correct for the public cloud —
    /// <b>sovereign clouds differ</b>, and ADR-028 A4 calls that out explicitly. Threaded now because
    /// it is three lines here and a six-call-site change after task 022 (code review W-10).
    /// </summary>
    private const string TokenExchangeUrlSetting = "Graph:ManagedIdentity:TokenExchangeUrl";

    private readonly ManagedIdentityClientAssertion _assertion;

    public ManagedIdentityAssertionProvider(
        IConfiguration configuration,
        ILogger<ManagedIdentityAssertionProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        // Blank is normalised to null by the resolver, so this is genuinely "no identity configured"
        // rather than "configured empty" — the distinction that made an earlier version of the warning
        // below fire on the wrong branch (code review W-2).
        //
        // ⚠️ PRECEDENCE IS NOT YET UNIFORM ACROSS THE CODEBASE (code review W-3). This provider and
        // ManagedIdentityCredentialFactory.Create share this resolver, which prefers
        // Graph:ManagedIdentity:ClientId over ManagedIdentity:ClientId. FOUR shared-lib sites still
        // read the two keys in the OPPOSITE order and do not call this resolver at all:
        // DataverseAccessDataSource, DataverseServiceClientImpl, DataverseWebApiClient,
        // DataverseWebApiService. If both keys were ever set to DIFFERENT UAMIs, this provider and
        // those app-only credentials would resolve to different identities — the FR-B4 conflation
        // class. Harmless while only one key is set (the current state everywhere), but do not read
        // "single shared resolver" as already true estate-wide. Convergence booked onto task 022.
        var uamiClientId = ManagedIdentityCredentialFactory.ResolveUamiClientId(configuration);
        var tokenExchangeUrl = configuration[TokenExchangeUrlSetting];

        // Null clientId means system-assigned, which this type supports. It is NOT what the Azure
        // environments use — spaarke-bff-dev is UserAssigned-only — but it is warned rather than
        // thrown so local dev boots and task 021's ordered selection can fall through. See the class
        // remarks on why construction must not fail.
        _assertion = string.IsNullOrWhiteSpace(tokenExchangeUrl)
            ? new ManagedIdentityClientAssertion(uamiClientId)
            : new ManagedIdentityClientAssertion(uamiClientId, tokenExchangeUrl);

        if (uamiClientId is null)
        {
            logger.LogWarning(
                "MI-FIC assertion provider initialised WITHOUT a user-assigned identity "
                + "(Graph:ManagedIdentity:ClientId and ManagedIdentity:ClientId are both unset or blank). "
                + "Assertions will be requested from a system-assigned identity, which the Azure "
                + "environments do not have. Expect fall-through to the configured fallback credential.");
        }
        else
        {
            // The clientId is a non-secret identifier already present in app settings; logging it is
            // what makes a UAMI/app-registration mix-up (FR-B4) diagnosable from logs alone.
            logger.LogInformation(
                "MI-FIC assertion provider initialised for user-assigned identity {UamiClientId} "
                + "(token-exchange audience: {TokenExchangeUrl}). This must be the UAMI's clientId, "
                + "not the app registration's.",
                uamiClientId,
                string.IsNullOrWhiteSpace(tokenExchangeUrl) ? "api://AzureADTokenExchange (default)" : tokenExchangeUrl);
        }
    }

    /// <inheritdoc />
    public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default)
        // The instance caches the signed assertion until expiry, so this is cheap after the first mint.
        // No try/catch: MsalServiceException must reach the caller intact so task 021's ordered
        // selection can branch on ErrorCode. Wrapping or logging-and-rethrowing would destroy that.
        => _assertion.GetSignedAssertionAsync(
            new AssertionRequestOptions { CancellationToken = cancellationToken });
}
