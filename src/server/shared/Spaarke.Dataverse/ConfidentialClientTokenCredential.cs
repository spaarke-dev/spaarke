using Azure.Core;
using Microsoft.Identity.Client;

namespace Spaarke.Dataverse;

/// <summary>
/// An <see cref="Azure.Core.TokenCredential"/> whose app-only tokens come from the ordered credential
/// provider (<see cref="IConfidentialClientProvider"/>) instead of from an inline
/// <c>ClientSecretCredential</c>. Introduced by <c>spaarke-auth-v4-dataverse-MI</c> task 022 (FR-B3).
///
/// <para><b>Why this type has to exist.</b> Four app-only call sites — <c>GraphClientFactory</c>,
/// <c>DataverseAccessDataSource</c>, <c>DataverseWebApiService</c>, <c>DataverseWebApiClient</c> — plus
/// <c>DataverseServiceClientImpl</c>'s token-provider function consume their credential as an
/// <c>Azure.Core.TokenCredential</c>, while ordered credential selection produces an MSAL
/// <see cref="IConfidentialClientApplication"/>. The two abstractions come from different SDKs and
/// neither can be extended into the other, so the join is an adapter. It is ~40 lines and it is what
/// removes the last five inline <c>ClientSecretCredential</c> / <c>AuthType=ClientSecret</c>
/// constructions for the BFF's own identity.</para>
///
/// <para><b>The identity is unchanged from the credential it replaces.</b> These sites previously built
/// <c>ClientSecretCredential(tenantId, appRegistrationId, secret)</c> — the app registration
/// authenticating with a secret. <c>AcquireTokenForClient</c> against a provider-supplied client is the
/// same OAuth client-credentials grant for the same app registration; only the credential that proves
/// it changes (MI-FIC first, secret last). It is emphatically <b>not</b> the managed identity's own
/// principal — that is what the <c>DefaultAzureCredential</c> branch above each call site already does,
/// and those branches are untouched.</para>
///
/// <para><b>Where it lives.</b> <c>Spaarke.Dataverse</c>, alongside the contract it consumes: three of
/// the five consumers are in this assembly, which is the BASE layer and cannot reference the BFF
/// (<c>LayerDependencyTests</c>, FR-14). Both dependencies — <c>Azure.Core</c> and
/// <c>Microsoft.Identity.Client</c> — are already referenced by this project, so no new
/// <c>PackageReference</c> and no new <c>ProjectReference</c> is introduced.</para>
/// </summary>
public sealed class ConfidentialClientTokenCredential : TokenCredential
{
    private readonly IConfidentialClientProvider _provider;
    private readonly string _tenantId;
    private readonly string _clientId;

    /// <param name="provider">Supplies the credential-bound confidential client (ordered selection).</param>
    /// <param name="tenantId">Directory (tenant) id.</param>
    /// <param name="clientId">Application (client) id of the app registration to authenticate AS —
    /// the app registration, never the managed identity's clientId (FR-B4).</param>
    public ConfidentialClientTokenCredential(
        IConfidentialClientProvider provider,
        string tenantId,
        string clientId)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        _tenantId = tenantId;
        _clientId = clientId;
    }

    /// <inheritdoc />
    public override async ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        var client = await _provider
            .GetClientAsync(_tenantId, _clientId, cancellationToken)
            .ConfigureAwait(false);

        // MSAL keeps its own app-token cache on the client, so repeat calls inside a token lifetime do
        // not reach the network. The provider is asked every time on purpose: it owns the ONE client
        // cache and re-evaluates the credential when a skipped higher-priority one becomes available
        // again, so caching the client here would pin the process to a fallback after one blip.
        var result = await client
            .AcquireTokenForClient(requestContext.Scopes)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AccessToken(result.AccessToken, result.ExpiresOn);
    }

    /// <summary>
    /// Synchronous acquisition. MSAL exposes no synchronous API, so this blocks — on a pool thread via
    /// <see cref="Task.Run(Func{Task{AccessToken}})"/> rather than on the caller's, so it cannot deadlock even if a
    /// caller ever has a synchronization context.
    ///
    /// <para><b>Only one caller uses it</b>: <c>DataverseServiceClientImpl</c>'s
    /// <c>tokenProviderFunction</c>, which acquires synchronously by deliberate design (#3b: an eager
    /// connect on the startup thread aborted the process with SIGABRT when the token was fetched
    /// sync-over-async). That mitigation is unaffected here — the connect is still deferred behind a
    /// <c>Lazy&lt;ServiceClient&gt;</c>, so this runs on a request thread at first use and at most once
    /// per token lifetime, never during host startup.</para>
    /// </summary>
    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
        => Task.Run(
            () => GetTokenAsync(requestContext, cancellationToken).AsTask(),
            cancellationToken).GetAwaiter().GetResult();
}
