// -----------------------------------------------------------------------------
// WorkerDataverseCredentialFactory.cs
//
// FR-39 ordered-credential factory for the L2 Worker's own Dataverse auth as
// the shared BFF app registration (punch row A44.5;
// customer-provisioning-orchestration-r1 task 205i, 2026-08-25). Consumed by
// BOTH H7's DataverseWebApiEnvVarValuesWriter and H6's
// DataverseWebApiSolutionImporter / DataverseWebApiSolutionVerifier — the two
// handler surfaces that CONSUME BFF-API-ClientSecret as their own
// client-credentials identity (the H7/task-142 half of A30's sentinel
// contract).
//
// MIRROR PROVENANCE — structurally mirrors master's migrated
// `DataverseServiceClientImpl` (src/server/shared/Spaarke.Dataverse/
// DataverseServiceClientImpl.cs:55-193, auth-v4 tasks 021/022/033, brought in
// via A35) + the BFF's OrderedCredentialClientProvider, translated to this
// project's Azure.Identity idiom:
//   - Ordered, config-driven selection (`{Section}:Credentials:Order`) —
//     rollback is a credential reorder + restart, never a redeploy (NFR-06).
//   - MI-FIC = the Worker UAMI mints a federated assertion (audience
//     `api://AzureADTokenExchange`) which the BFF app-reg trusts via the FIC
//     created by H3 / `-FicOnly`; implemented with Azure.Identity's
//     ClientAssertionCredential over ManagedIdentityCredential — the exact
//     exchange MSAL's ManagedIdentityClientAssertion performs inside the
//     BFF's provider (OrderedCredentialClientProvider.AcquireAsync,
//     ManagedIdentityFederated branch). The UAMI clientId resolves from
//     `ManagedIdentity:ClientId` (falling back to
//     `Graph:ManagedIdentity:ClientId`) — the SAME lookup + fallback
//     DataverseServiceClientImpl.cs:83-84 performs, and the SAME app setting
//     modules/controlplane-worker-app-service.bicep already emits.
//   - "Not configured" vs "configured but broken" are different answers
//     (mirror of OrderedCredentialClientProvider.AcquireAsync remarks): an
//     EMPTY client secret on the ClientSecret kind is "not configured" and
//     falls through with a logged warning (on secret-free envs empty is the
//     SIGNAL — §9.1; never a sentinel); an exhausted chain FAILS CLOSED with
//     an actionable message (mirror of the provider's fail-closed throw).
//
// DELIBERATE NARROWING vs the BFF provider (documented, not accidental): no
// probe-before-bind, no negative cache, no suppression window, no runtime
// fall-through FROM ManagedIdentityFederated. The BFF machinery exists for a
// hot OBO path on hosts that may lack IMDS (developer workstations). The L2
// Worker host is ALWAYS App Service (IMDS present) and H6/H7 run once per
// provisioning run: an MI failure surfaces at token acquisition, which the
// writer/importer classify as AuthFailure → §4C Resumable on the affected run
// — the correct failure boundary here. Consequence (STRONGER than the BFF,
// intentionally): once ManagedIdentityFederated is selected the Worker can
// NEVER silently downgrade to the transitional secret — the FR-B4
// wrong-identity signature fails loud by construction.
//
// §11 justification: see CredentialKind.cs file header (same component
// family). A42 (task 205b) coordination: A42 landed NO shared
// credential-factory helper (its surface is H3's FIC *creation* — classifier/
// exception/verification-state types), so authoring this EnvVarValues/
// SolutionImport-scoped factory does not parallel an existing component;
// follow-on unification (CustomerRunGuard + future BFF-app-reg-identity
// consumers) proposed in the A44.5 punch-list annotation.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;

namespace Sprk.Provisioning.ControlPlane.Handlers.Credentials;

/// <summary>
/// The credential an FR-39 ordered selection actually chose. Carrying the
/// <see cref="Kind"/> (and, for MI-FIC, the pinned UAMI clientId) makes the
/// selection assertable as BEHAVIOR in tests without reflecting on
/// Azure.Identity internals — mirror of the BFF provider's
/// <c>SelectedKindFor</c> test-observability surface.
/// </summary>
/// <param name="Kind">Which credential kind won the ordered selection.</param>
/// <param name="ManagedIdentityClientId">
/// The UAMI clientId the MI-FIC assertion is pinned to (<c>null</c> =
/// system-assigned, or a non-MI kind). Model 1 pins the single shared
/// per-environment UAMI; Model 2 pins the per-stamp UAMI — the per-customer
/// Worker's own <c>ManagedIdentity__ClientId</c> app setting (SF-2
/// plumbing-chain discipline: the field NAME carries the semantic).
/// </param>
/// <param name="Credential">The ready-to-use token credential.</param>
public sealed record SelectedWorkerCredential(
    CredentialKind Kind,
    string? ManagedIdentityClientId,
    TokenCredential Credential);

/// <summary>
/// Builds the L2 Worker's confidential-client <see cref="TokenCredential"/>
/// for a (tenant, BFF app-reg clientId) pair from the configured FR-39
/// ordered credential chain (<see cref="WorkerCredentialSelectionOptions"/>).
/// Registered as a singleton; injected concretely (ADR-010 — single
/// implementation, no interface ceremony; tests construct it directly over an
/// in-memory <see cref="IConfiguration"/> since <see cref="Create"/> performs
/// NO network I/O — token acquisition happens later, at the call sites'
/// <c>GetTokenAsync</c>).
/// </summary>
public sealed class WorkerDataverseCredentialFactory
{
    /// <summary>
    /// The federated-token exchange scope: an MI token minted for this
    /// audience is what Entra accepts as a client assertion for an app-reg
    /// that trusts the UAMI via a federated identity credential (H3 /
    /// <c>-FicOnly</c> issuer+subject+audience triple — audience
    /// <c>api://AzureADTokenExchange</c>).
    /// </summary>
    public const string FederatedTokenExchangeScope = "api://AzureADTokenExchange/.default";

    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkerDataverseCredentialFactory> _logger;

    /// <summary>Constructs the factory over the host configuration (UAMI clientId lookup) + logger.</summary>
    public WorkerDataverseCredentialFactory(
        IConfiguration configuration,
        ILogger<WorkerDataverseCredentialFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Selects the first available credential from the configured chain and
    /// returns it with its kind. Throws <see cref="InvalidOperationException"/>
    /// (fail-closed, mirror of the BFF provider) when the chain is exhausted
    /// — e.g. a legacy <c>[ClientSecret]</c> chain with an empty secret slot.
    /// Performs no network I/O.
    /// </summary>
    /// <param name="credentials">The bound ordered-credential sub-options.</param>
    /// <param name="sectionName">Owning configuration section name (for messages), e.g. <c>EnvVarValues</c>.</param>
    /// <param name="tenantId">Entra tenant the client-credentials grant targets (§4D I1 — always explicit).</param>
    /// <param name="clientId">The shared BFF app registration id (H3 output — InterStepState.BffAppRegId).</param>
    /// <param name="clientSecret">
    /// The resolved secret slot value — <c>null</c>/empty on secret-free
    /// environments (empty is the SIGNAL, §9.1; the KV-ref app setting is
    /// conditionally omitted by modules/controlplane-worker-app-service.bicep
    /// when <c>requireSecretFreeIdentity=true</c>).
    /// </param>
    public SelectedWorkerCredential Create(
        WorkerCredentialSelectionOptions credentials,
        string sectionName,
        string tenantId,
        string clientId,
        string? clientSecret)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var chain = credentials.ResolveEffectiveOrder(sectionName);
        var skipped = new List<string>();

        foreach (var kind in chain)
        {
            switch (kind)
            {
                case CredentialKind.ManagedIdentityFederated:
                {
                    // Mirror of DataverseServiceClientImpl.cs:83-84's UAMI
                    // clientId lookup + fallback; null → system-assigned
                    // (same "(system-assigned)" convention as master).
                    var miClientId = ResolveManagedIdentityClientId();
                    var miCredential = new ManagedIdentityCredential(
                        miClientId is null
                            ? ManagedIdentityId.SystemAssigned
                            : ManagedIdentityId.FromUserAssignedClientId(miClientId));

                    // FIC exchange: the UAMI token for api://AzureADTokenExchange
                    // IS the client assertion. ClientAssertionCredential invokes
                    // the callback per token request; ManagedIdentityCredential
                    // caches the underlying MI token until expiry, so this is
                    // not a per-call IMDS round-trip (parity with the BFF's
                    // ManagedIdentityClientAssertion caching note).
                    var credential = new ClientAssertionCredential(
                        tenantId,
                        clientId,
                        async ct =>
                        {
                            var assertion = await miCredential.GetTokenAsync(
                                new TokenRequestContext(new[] { FederatedTokenExchangeScope }), ct)
                                .ConfigureAwait(false);
                            return assertion.Token;
                        });

                    _logger.LogInformation(
                        "L2 Worker Dataverse credential for {ClientId}: {Kind} (UAMI {UamiClientId}); " +
                        "section {Section}; chain {Chain}.",
                        clientId, kind, miClientId ?? "(system-assigned)", sectionName,
                        string.Join(" > ", chain));
                    return new SelectedWorkerCredential(kind, miClientId, credential);
                }

                case CredentialKind.ClientSecret:
                {
                    if (string.IsNullOrWhiteSpace(clientSecret))
                    {
                        // "Not configured" — the ordinary secret-free shape.
                        // NEVER treated as an error here (empty is the signal,
                        // §9.1) — but if nothing else is configured either,
                        // the exhausted-chain throw below fails closed.
                        _logger.LogWarning(
                            "L2 Worker Dataverse credential {Kind} is not configured for section {Section} " +
                            "(empty secret slot); falling through to the next configured credential.",
                            kind, sectionName);
                        skipped.Add($"{kind} (not configured — empty secret slot)");
                        continue;
                    }

                    _logger.LogInformation(
                        "L2 Worker Dataverse credential for {ClientId}: {Kind} (transitional — prong-3 " +
                        "unmigrated environment per the §6.5 resolution record); section {Section}.",
                        clientId, kind, sectionName);
                    return new SelectedWorkerCredential(
                        kind, null, new ClientSecretCredential(tenantId, clientId, clientSecret));
                }

                default:
                {
                    skipped.Add($"{kind} (unsupported kind)");
                    continue;
                }
            }
        }

        // Fail closed — mirror of OrderedCredentialClientProvider's exhausted-
        // chain throw. A handler with no credential cannot authenticate to any
        // Dataverse env; degrading quietly would produce the silent-WRONG class
        // this project exists to eliminate.
        throw new InvalidOperationException(
            $"No credential could be selected for client '{clientId}' in tenant '{tenantId}'. " +
            $"Configured order: {string.Join(" > ", chain)}. Attempts: {string.Join("; ", skipped)}. " +
            $"Set '{sectionName}:Credentials:Order' to a credential this environment can actually provide " +
            $"(secret-free environments: {nameof(CredentialKind.ManagedIdentityFederated)} as the only entry; " +
            "prong-3 unmigrated environments: populate the ClientSecret KV-reference app setting). " +
            "NEVER unblock by writing a placeholder into the secret slot — a sentinel fails opaquely with " +
            "AADSTS7000215 at first use (auth-v4 §9.1).");
    }

    /// <summary>
    /// Resolves the UAMI clientId the MI-FIC assertion pins to — canonical
    /// key first, mirroring <c>DataverseServiceClientImpl</c>'s lookup. The
    /// Worker Bicep module emits <c>ManagedIdentity__ClientId</c> (task 110
    /// DS-5 C5.1 convention); <c>null</c> means system-assigned.
    /// </summary>
    internal string? ResolveManagedIdentityClientId()
    {
        var value = _configuration["ManagedIdentity:ClientId"];
        if (string.IsNullOrWhiteSpace(value))
        {
            value = _configuration["Graph:ManagedIdentity:ClientId"];
        }
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
