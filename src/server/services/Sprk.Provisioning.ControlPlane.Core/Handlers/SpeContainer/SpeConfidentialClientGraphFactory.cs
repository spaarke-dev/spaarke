// -----------------------------------------------------------------------------
// SpeConfidentialClientGraphFactory.cs
//
// The ONE way L2 acts as an SPE container type's OWNING app: a confidential-client
// (app-only) Graph client whose credential is the Worker UAMI's federated identity
// credential (MI-FIC) on the owning app. Consumed by H0's SpeOwnerCredentialProbe,
// H8's GraphContainerProvisioner + GraphAppOnlyContainerVerifier and H13's T6 probe
// (GraphContainersListAppOnlyProbe) — all four need the identical credential, so it
// lives in one component (CLAUDE.md §11).
//
// TASK 248 (G28, owner decision D16, 2026-10-03) — MI-FIC, NOT A CERTIFICATE.
// Until T248 this class loaded a password-less base64 PFX (`SPE-OwnerCert-Pfx`) from a
// Spaarke platform Key Vault and built a ClientCertificateCredential. No such secret
// ever existed, so every run stopped at H0. ADR-028 A4 makes MI-FIC the default
// credential for confidential clients: the owning app `Spaarke SPE Model 1 Owner`
// carries a federated identity credential whose subject is the Worker UAMI, and the
// UAMI's token for `api://AzureADTokenExchange` is the client assertion
// (WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential — the
// single place the Worker mints that assertion). Verified live 2026-10-03 from compute
// carrying the dev Worker UAMI: the exchange returned an owning-app Graph token with
// `appidacr` = 2 (client-assertion class, the same as a certificate) and both SPE
// roles, and Graph accepted it for the container-type registration GET and the app-only
// containers listing (T248 POML notes). There is no certificate path and no fallback:
// if the exchange fails, the run stops with the error (H0 code
// `spe-owner-token-failed`).
//
// TENANT SCOPING + CACHING: one credential per (tenantId, ownerAppId) pair — never
// DefaultAzureCredential — so §4D I5 (explicit per-tenant scope) holds by construction. The
// credential is cached for the factory's (singleton) lifetime, keyed by that pair (ADR-028 A4), so
// H0, H8's provision + verify and T6 reuse the owning app's token instead of re-exchanging per call.
// Graph clients are cheap wrappers — callers dispose them (`using var graph`).
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Azure.Core;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Authentication.Azure;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>
/// Builds the owning-app (app-only, confidential-client) credential and Graph client L2 uses for SPE,
/// from the Worker UAMI's federated identity credential on the owning app (task 248). Singleton; performs
/// no network I/O until a token or Graph call is requested.
/// </summary>
public sealed class SpeConfidentialClientGraphFactory
{
    /// <summary>The app-only Graph scope every SPE call L2 makes requests.</summary>
    public static readonly string[] GraphDefaultScope = { "https://graph.microsoft.com/.default" };

    /// <summary>
    /// T6 regression-detector phrase — parity with the historical PS-script-based stdout scan. Under
    /// app-only (client-assertion) auth this should never fire; it exists as a defense-in-depth signal
    /// for H13's T6 acceptance gate.
    /// </summary>
    internal const string DelegatedTokenTrapPhrase = "public client not allowed";

    private readonly Func<string, string, TokenCredential> _createCredential;
    private readonly HttpMessageHandler? _graphHandler;
    private readonly ConcurrentDictionary<(string TenantId, string OwnerAppId), TokenCredential> _credentials = new();

    /// <summary>Production constructor — owning-app credentials come from the Worker UAMI (MI-FIC).</summary>
    public SpeConfidentialClientGraphFactory(WorkerDataverseCredentialFactory credentials)
        : this(ResolveFrom(credentials), graphHandler: null)
    {
    }

    /// <summary>
    /// Test seam: <paramref name="createCredential"/> replaces the MI-FIC credential and
    /// <paramref name="graphHandler"/> (when set) carries Graph requests to a fake handler.
    /// </summary>
    internal SpeConfidentialClientGraphFactory(
        Func<string, string, TokenCredential> createCredential,
        HttpMessageHandler? graphHandler)
    {
        ArgumentNullException.ThrowIfNull(createCredential);
        _createCredential = createCredential;
        _graphHandler = graphHandler;
    }

    /// <summary>
    /// The owning app's app-only credential in <paramref name="tenantId"/>: a client assertion minted by
    /// the Worker UAMI. Never a certificate, never a secret. Cached per (tenant, owning app).
    /// </summary>
    public TokenCredential CreateCredential(string tenantId, string ownerAppId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAppId);
        return _credentials.GetOrAdd(
            (tenantId.Trim().ToLowerInvariant(), ownerAppId.Trim().ToLowerInvariant()),
            key => _createCredential(key.TenantId, key.OwnerAppId));
    }

    /// <summary>A Graph client authenticated as the owning app (app-only) in <paramref name="tenantId"/>. Caller disposes it.</summary>
    public GraphServiceClient CreateGraphClient(string tenantId, string ownerAppId)
    {
        var credential = CreateCredential(tenantId, ownerAppId);
        return _graphHandler is null
            ? new GraphServiceClient(credential, GraphDefaultScope)
            : new GraphServiceClient(
                new HttpClient(_graphHandler, disposeHandler: false),
                new AzureIdentityAuthenticationProvider(credential, scopes: GraphDefaultScope));
    }

    /// <summary>
    /// T6 regression detector — checks a Graph ODataError for the delegated-token trap signature
    /// (case-insensitive). H13's T6 probe consumes this to classify "trap manifested" vs "generic error".
    /// </summary>
    internal static bool IsDelegatedTokenTrapError(ODataError ex)
    {
        var message = ex.Error?.Message ?? ex.Message ?? string.Empty;
        return message.Contains(DelegatedTokenTrapPhrase, StringComparison.OrdinalIgnoreCase);
    }

    private static Func<string, string, TokenCredential> ResolveFrom(WorkerDataverseCredentialFactory credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return credentials.CreateManagedIdentityFederatedCredential;
    }
}
