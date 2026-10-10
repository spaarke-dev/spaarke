// -----------------------------------------------------------------------------
// GraphAppRegistrationProvisioner.cs
//
// Task 130 (Wave G-3, xhigh) — production IEntraAppRegProvisioner. Ports the
// retired PS script's (scripts/Register-EntraAppRegistrations.ps1) 5-step app-registration
// reconciler (create-or-get / signInAudience / identifierUri / exposed scope /
// requiredResourceAccess / client secret) to Microsoft.Graph 6.5.0
// (Applications / ServicePrincipals), and ADDS the Model 2 federated-
// identity-credential step (auth-v4 §3.1 recipe, spec.md FR-39) the retired
// script never had.
//
// SDK SHAPES GROUND-TRUTHED VIA REFLECTION (per Wave G-2/G-3 discipline —
// task 123/125 precedent) against the installed Microsoft.Graph 6.5.0 package
// (lib/net10.0/Microsoft.Graph.dll) + its transitive Microsoft.Kiota.Authentication
// .Azure / Azure.Core deps BEFORE writing this file. Two real gotchas caught:
//
//   GOTCHA 1 — GraphServiceClient(TokenCredential, IEnumerable<string> scopes,
//   string? baseUrl) exists, but there is NO overload (and no
//   AzureIdentityAuthenticationProvider constructor) that accepts a per-request
//   tenantId. Per-tenant scoping (§4D I5) therefore requires constructing a
//   FRESH TokenCredential per call with TenantId baked into
//   DefaultAzureCredentialOptions — exactly the pattern
//   GraphRestAppRoleGranter.cs (H10) already established
//   (`new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId =
//   tenantId })`) — NOT the shared DI singleton TokenCredential (which is L2's
//   OWN platform UAMI, permanently pinned to Spaarke's tenant).
//
//   GOTCHA 2 — the POML's "exchange-based verification (not creation-200
//   alone)" instruction assumes L2 can mint a token AS the customer's per-
//   customer UAMI (the FIC's `subject`) to use as a `client_assertion` in a
//   real OAuth2 token exchange. This is NOT POSSIBLE: L2's Worker process runs
//   under L2's OWN platform UAMI (Spaarke's tenant), not the customer's
//   per-customer BFF UAMI (`mi-spaarke-{customerId}-{env}`, assigned to the
//   customer's App Service, not to L2). L2 has no credential path to
//   impersonate that UAMI. The pragmatic, structurally-honest substitute
//   (documented as a Path C pivot per root CLAUDE.md §6.5 in task 130's
//   completion notes) is an INDEPENDENT RE-GET of the just-written FIC object
//   (a fresh GET, not the POST response echo) confirming Issuer/Subject/
//   Audiences persisted byte-for-byte — the same "never trust the write call's
//   own response, always re-query" discipline H10's T2/T3 traps already apply
//   elsewhere in this project. A literal token-exchange proof belongs to
//   whichever component actually runs AS the customer's UAMI at runtime (the
//   deployed BFF itself, H9's domain) — out of L2's credential reach by
//   construction, not an oversight.
//
// NOT UNIT-TESTED IN THE CI SUITE (real Microsoft.Graph HTTP calls) — parity
// with the established project precedent for every other live-Graph/live-KV
// collaborator (GraphRestAppRoleGranter, DataverseWebApiAppUserCreator — each
// documents this same posture in its own file header). The pure planners here
// (PlanKeylessProofAppRoles, PlanClientAccess) ARE unit-tested. Handler unit tests (H3EntraAppRegHandlerTests)
// substitute a fake IEntraAppRegProvisioner — that is the real coverage
// surface for H3's orchestration logic. Live-Graph coverage belongs in
// env-guarded smoke tests when a dedicated dev tenant is available.
//
// KV WRITES (reuses task 125's SecretClientKvWriter idiom — same SDK class +
// same per-vault-per-call construction, a DIFFERENT instance since H3's
// 3-secret write here is a distinct concern from H4's ~26-entry canonical-
// manifest population): after a fresh secret is generated, this provisioner
// writes BFF-API-ClientSecret / BFF-API-ClientId / BFF-API-Audience directly
// into the CUSTOMER's OWN target vault (request.VaultName) under their exact
// canonical §7.9 names, using the SHARED DI TokenCredential singleton (KV
// RBAC is granted to L2's own UAMI directly — Model 2 customer-owned KV
// cross-tenant reachability is an Azure Lighthouse subscription delegation,
// per H1's SubscriptionReadinessProbe — NOT a per-call tenant-scoped
// credential like the Graph calls above need). These writes are the ONLY
// source of BFF-API-ClientId / BFF-API-Audience: manifest.yaml labels them
// `written-by-h3` and H4 (which runs BEFORE H3) skips them. H3 hands nothing
// on through RunParameters.Secrets (task 245a, G25 — that hand-off was a
// deadlock: H4 waited on refs H3 only wrote after H4).
//
// FIC RECIPE (auth-v4 §3.1; runs for BOTH tenancy models post-task-222 per
// D-13 — every per-customer app-reg gets a FIC trusting the customer's BFF
// UAMI): subject = the UAMI's principalId (request.UamiPrincipalId — NOT its
// clientId, the documented most-common misconfiguration trap); audiences =
// ["api://AzureADTokenExchange"]; issuer = Spaarke's own tenant for
// spaarke-hosted-model2 + Model 1 (UAMI lives in Spaarke's subscription for
// both — intra-Spaarke-tenant) OR the customer's own tenant for
// customer-owned-model2 (UAMI lives in the customer's subscription).
//
// L2 WORKER FIC (ISS-015, 2026-10-09): H6 / H7 / H7b sign in to the customer's Dataverse AS this registration (D-13),
// secret-free, with an assertion from the L2 Worker's OWN UAMI (WorkerDataverseCredentialFactory, MI-FIC). So every
// Spaarke-tenant registration carries a SECOND credential, EntraAppRegOptions.WorkerFicName ("spaarke-l2-worker"):
// subject = ControlPlaneIdentityOptions.PrincipalObjectId (the Worker UAMI's OBJECT id), issuer = Spaarke's tenant,
// audience api://AzureADTokenExchange. Same triple-idempotency, drift repair and re-GET verification as the first
// (PlanFederatedCredentials); kept for the registration's lifetime (re-runs and upgrades sign in through it). It grants
// L2 nothing new: L2 already owns the registration (it could add any credential to it), and D-13 already has the Worker
// act as it — this only makes that sign-in possible without a secret. Not for customer-owned-model2 (cross-tenant;
// Model 2 out of scope). Two FICs of Entra's 20 per application.
//
// CLIENT ACCESS (T240a, 2026-10-07): H3 sets the app's spa.redirectUris to exactly the customer's Dataverse origin
// (its code pages sign in through this app with redirectUri = window.location.origin) and its
// api.preAuthorizedApplications to exactly the platform's shared clients on user_impersonation (Office add-in; the
// Teams client later), so they get this BFF's token without a consent prompt. It only ever PATCHes this customer's own
// app object; the client apps themselves are never touched. A second run with the same inputs sends no PATCH.
//
// ACCT OPTIONAL CLAIM (task 255, INCOMING-141 §5 from unified-access-control-r2 task 141): every per-customer BFF
// registration carries the `acct` optional claim (member = 0, guest = 1) on its ACCESS tokens — the stamp BFF's
// workforce member test reads it and fails closed without it (workforce_acct_claim_missing). A new registration is
// created with it; an existing one gets it added (every other optional claim preserved — the retired script's
// -AcctClaimOnly mode, Get-SpaarkeAccessTokenOptionalClaimsWithAcct). Read fresh, PATCH only when missing, read back.
//
// KEYLESS-PROOF APP ROLE (task 230b, owner D13): the app-reg exposes the application role
// KeylessProofContract.AppRoleValue (fixed id, allowedMemberTypes ["Application"]) and H3 assigns it to
// the L2 Worker identity (ControlPlaneIdentityOptions.PrincipalObjectId) — the only caller of the
// stamp BFF's POST /api/platform/keyless-proof, which H13 calls. Create, reconcile and assignment are
// idempotent (the role is matched by value, the assignment by principal + role). L2 already holds the
// Graph permissions this needs: it owns the app it created (Application.ReadWrite.OwnedBy is enough to
// PATCH appRoles) and H10 already assigns app roles with AppRoleAssignment.ReadWrite.All. MODEL 2 GAP
// (out of scope, owner 2026-09-30): a customer-owned-model2 app-reg lives in the customer's tenant,
// where the Spaarke-tenant L2 identity has no service principal — the role is defined but not
// assigned, and H13's keyless proof cannot authenticate there (recorded in task 230b's notes).
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Spaarke.Contracts.Provisioning;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <inheritdoc cref="IEntraAppRegProvisioner"/>
public sealed class GraphAppRegistrationProvisioner : IEntraAppRegProvisioner
{
    private static readonly string[] GraphDefaultScope = { "https://graph.microsoft.com/.default" };

    /// <summary>Canonical KV secret name for the app-reg's client secret (never changes — H4's BindingNeverDeleteSecrets guard depends on this exact literal).</summary>
    public const string ClientSecretName = "BFF-API-ClientSecret";

    /// <summary>Canonical KV secret name for the app-reg's client (application) id.</summary>
    public const string ClientIdSecretName = "BFF-API-ClientId";

    /// <summary>Canonical KV secret name for the app-reg's audience URI.</summary>
    public const string AudienceSecretName = "BFF-API-Audience";

    /// <summary>Shared UAMI-pinned credential — used for KV writes only (see file-header KV WRITES note). Graph calls build a FRESH per-tenant credential (GOTCHA 1).</summary>
    private readonly TokenCredential _sharedCredential;
    private readonly EntraAppRegOptions _options;
    private readonly ControlPlaneIdentityOptions _identity;
    private readonly ILogger<GraphAppRegistrationProvisioner> _logger;

    /// <summary>
    /// Constructs the production provisioner. <paramref name="sharedCredential"/> is L2's own platform UAMI-pinned
    /// credential (KV writes only); <paramref name="identity"/> names that identity, which H3 assigns the keyless-proof
    /// app role (task 230b).
    /// </summary>
    public GraphAppRegistrationProvisioner(
        TokenCredential sharedCredential,
        IOptions<EntraAppRegOptions> options,
        IOptions<ControlPlaneIdentityOptions> identity,
        ILogger<GraphAppRegistrationProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(sharedCredential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(logger);
        _sharedCredential = sharedCredential;
        _options = options.Value;
        _identity = identity.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<EntraAppRegOutcome> ProvisionAsync(
        EntraAppRegRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UamiPrincipalId);

        // A42 / SF-5 (W-1): tenancy guard runs BEFORE ANY Graph mutation —
        // not only ahead of the FIC step. Model 2 provisioning reaches the
        // FIC step unconditionally, so a cross-tenant pair is doomed to
        // refusal anyway; refusing up-front prevents creating an ORPHAN
        // app-reg (with a live client secret) in the wrong tenant first.
        // Blank issuer-tenant is NOT refused here — the FIC step returns its
        // pre-existing config-fault Failure for that case.
        var uamiTenantId = ResolveUamiTenantId(request.Profile, request.TenantId, _options.SpaarkeTenantId);
        if (!string.IsNullOrWhiteSpace(uamiTenantId))
        {
            AssertFicTenancy(request.TenantId, uamiTenantId, request.Profile);
        }

        // ISS-015: plan both federated credentials (stamp BFF UAMI + L2 Worker UAMI) before any Graph write, so a
        // configuration fault refuses here instead of leaving a registration nothing can sign in as.
        var requiredFics = BuildRequiredFicSpecs(
            request.Profile, request.TenantId, _options.SpaarkeTenantId, request.UamiPrincipalId,
            _identity.PrincipalObjectId, _options);
        if (requiredFics.Error is not null)
        {
            return new EntraAppRegOutcome.Failure(requiredFics.Error);
        }

        var graph = BuildTenantScopedGraphClient(request.TenantId);
        var displayName = $"spaarke-bff-api-{request.CustomerId}";

        try
        {
            // (1) Get-or-create the app-reg. An existing one is adopted only when it is provably ours (T240a review):
            //     the name is predictable, and an adopted registration gets the stamp's FIC and, via H10, Dataverse admin.
            var matches = await FindByDisplayNameAsync(graph, displayName, cancellationToken).ConfigureAwait(false);
            if (matches.Count > 1)
            {
                return new EntraAppRegOutcome.Failure(
                    $"{matches.Count} application registrations are named '{displayName}' " +
                    $"({string.Join(", ", matches.Select(m => m.AppId))}); H3 adopts none of them. Remove the ones that " +
                    $"are not this customer's BFF registration, then resume. [{EntraAppRegRejectionCodes.AdoptionRefused}]");
            }
            var app = matches.SingleOrDefault();
            var created = false;
            if (app is null)
            {
                app = await CreateAppAsync(graph, displayName, cancellationToken).ConfigureAwait(false);
                created = true;
            }
            else
            {
                var refusal = await CheckAdoptionAsync(graph, app, request, requiredFics.Names, cancellationToken)
                    .ConfigureAwait(false);
                if (refusal is not null)
                {
                    _logger.LogError(
                        "H3 refused to adopt the existing registration {DisplayName} ({AppId}): {Reason} customerId={CustomerId}",
                        displayName, app.AppId, refusal, request.CustomerId);
                    return new EntraAppRegOutcome.Failure(
                        $"The existing application registration '{displayName}' ({app.AppId}) is not safe to adopt: " +
                        $"{refusal}. Nothing was written. Investigate who created it; if it is not this customer's BFF " +
                        $"registration, delete it and resume. [{EntraAppRegRejectionCodes.AdoptionRefused}]");
                }
            }

            if (string.IsNullOrWhiteSpace(app.Id) || string.IsNullOrWhiteSpace(app.AppId))
            {
                return new EntraAppRegOutcome.Failure(
                    $"Graph returned an application object for '{displayName}' with a blank id/appId (created={created}).");
            }

            // (2) Reconcile signInAudience / identifierUris / exposed scope /
            //     requiredResourceAccess when NOT freshly created (a fresh
            //     create already carries the full target manifest).
            if (!created)
            {
                await ReconcileExistingAppAsync(graph, app, cancellationToken).ConfigureAwait(false);
            }

            // (2b) T240a: the code pages' SPA redirect + the pre-authorized shared clients, set exactly.
            var accessFailure = await EnsureClientAccessAsync(graph, app.Id!, request, cancellationToken).ConfigureAwait(false);
            if (accessFailure is not null)
            {
                return accessFailure;
            }

            // (2c) T255: the `acct` optional claim on the access tokens (new and existing registrations alike).
            var acctFailure = await EnsureAcctOptionalClaimAsync(graph, app.Id!, request, cancellationToken).ConfigureAwait(false);
            if (acctFailure is not null)
            {
                return acctFailure;
            }

            // (3) Ensure service principal.
            var servicePrincipalId = await EnsureServicePrincipalAsync(graph, app.AppId!, cancellationToken).ConfigureAwait(false);

            // (3b) Task 230b: the keyless-proof app role goes to the L2 Worker identity (H13 calls the stamp BFF with it).
            var roleFailure = await EnsureKeylessProofRoleAssignmentAsync(
                graph, app, servicePrincipalId, request, cancellationToken).ConfigureAwait(false);
            if (roleFailure is not null)
            {
                return roleFailure;
            }

            // (4) Ensure client secret (skip-if-valid) — GATED on
            //     RequireSecretFreeIdentity. Bucket B HIGH#3 SESSION 18: when
            //     true (secure default per EntraAppRegRequest doc), NEVER call
            //     Graph AddPassword — the mint itself is forbidden, not just
            //     the KV write. This closes the E-3 / auth-v4 task 033 (2026-
            //     08-24) contract at the earliest possible layer: no cleartext
            //     ever exists in-process for a secret-free profile. A prior
            //     draft placed the guard only at the pendingWrites.Add site,
            //     which still executed the network call + held cleartext long
            //     enough for an accidental log line to leak it — the constraint
            //     rules out that entire window, not just the KV write itself.
            string secretText = string.Empty;
            if (!request.RequireSecretFreeIdentity)
            {
                secretText = await EnsureClientSecretAsync(graph, app, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation(
                    "H3 skipped BFF-API-ClientSecret mint (RequireSecretFreeIdentity=true, ADR-028 A4 / " +
                    ".claude/constraints/provisioning.md KV credential lifecycle rule 1). " +
                    "customerId={CustomerId} profile={Profile}",
                    request.CustomerId, request.Profile);
            }

            // (5) FICs — both models post-task-222 (D-13), auth-v4 §3.1 recipe: the stamp BFF UAMI, plus (ISS-015,
            //     Spaarke-tenant profiles) the L2 Worker UAMI that H6/H7/H7b sign in through.
            var ficFailure = await EnsureFederatedIdentityCredentialsAsync(
                graph, app.Id!, requiredFics.Specs, request, cancellationToken).ConfigureAwait(false);
            if (ficFailure is not null)
            {
                return ficFailure;
            }

            // (6) STAGE (do NOT write yet) the KV secrets for the customer's
            //     own target vault — DS-4 §3 BINDING ordering: KV writes only
            //     commit AFTER admin-consent verification succeeds (see
            //     IEntraAppRegProvisioner.CommitPendingSecretsAsync doc +
            //     file-header). Cleartext (ClientSecret) stays in-process only.
            //     Bucket B HIGH#3 SESSION 18: the ClientSecret write is
            //     unreachable when RequireSecretFreeIdentity=true (secretText
            //     stays empty above), but the guard here is also explicit for
            //     defense-in-depth — should a future edit accidentally reroute
            //     secretText, this second layer refuses the KV write.
            var pendingWrites = new List<PendingKvSecretWrite>
            {
                new(request.VaultName, ClientIdSecretName, app.AppId!),
                new(request.VaultName, AudienceSecretName, $"api://{app.AppId}"),
            };
            if (!request.RequireSecretFreeIdentity && !string.IsNullOrEmpty(secretText))
            {
                pendingWrites.Add(new PendingKvSecretWrite(request.VaultName, ClientSecretName, secretText));
            }

            var kvUriRef = BuildKvUriReference(request.VaultName, ClientSecretName);
            _logger.LogInformation(
                "H3 Graph app-reg provisioning succeeded: customerId={CustomerId} tenantId={TenantId} " +
                "appId={AppId} created={Created} pendingKvWrites={PendingCount}",
                request.CustomerId, request.TenantId, app.AppId, created, pendingWrites.Count);

            return new EntraAppRegOutcome.Success(new EntraAppRegOutputs
            {
                BffAppRegId = app.AppId!,
                BffClientSecretKvUri = kvUriRef,
                PendingKvWrites = pendingWrites,
                // A42 / SF-8: the FIC persisted + re-GET-confirmed its triple,
                // but L2 can NEVER exchange-verify at creation time (GOTCHA 2)
                // — this is the script exit-2 equivalent, and it REQUIRES a
                // recorded post-App-Service verification (H13/T4). Never
                // terminal success.
                FicVerification = FicVerificationState.PendingPostAppServiceVerification,
            });
        }
        catch (ODataError ex)
        {
            // NFR-09: catch ODataError (not ServiceException); ResponseStatusCode is int.
            _logger.LogError(ex,
                "H3 Graph app-reg provisioning ODataError: customerId={CustomerId} tenantId={TenantId} status={Status}",
                request.CustomerId, request.TenantId, ex.ResponseStatusCode);
            return new EntraAppRegOutcome.Failure(
                $"Graph ODataError {ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}");
        }
    }

    // VerifySharedAsync REMOVED 2026-09-29 (task 222 per D-13): the shared-app-reg
    // verification path was deleted along with the interface member — H3 now
    // provisions ONE app-reg per customer, unconditionally, in both models. See
    // H3EntraAppRegHandler.cs TASK 222 REWRITE for the D-13 mechanism.

    // ---------------------------------------------------------------------
    // Graph client construction (GOTCHA 1 — see file header)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a Graph client scoped to a SPECIFIC tenant. Per §4D I5 (explicit
    /// per-tenant scope, never an ambient default-tenant credential) AND
    /// GOTCHA 1 (GraphServiceClient/AzureIdentityAuthenticationProvider have
    /// no per-request tenantId override), this constructs a FRESH
    /// <see cref="DefaultAzureCredential"/> per call with
    /// <see cref="DefaultAzureCredentialOptions.TenantId"/> set — the exact
    /// pattern GraphRestAppRoleGranter.cs (H10) already established. This is
    /// intentionally NOT the shared DI TokenCredential singleton (L2's own
    /// platform UAMI, permanently pinned to Spaarke's tenant).
    /// </summary>
    private static GraphServiceClient BuildTenantScopedGraphClient(string tenantId)
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        return new GraphServiceClient(credential, GraphDefaultScope);
    }

    // ---------------------------------------------------------------------
    // App-reg ensure / reconcile
    // ---------------------------------------------------------------------

    private async Task<IReadOnlyList<Application>> FindByDisplayNameAsync(
        GraphServiceClient graph, string displayName, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);
        var filter = $"displayName eq '{EscapeODataLiteral(displayName)}'";
        var page = await graph.Applications.GetAsync(rc =>
        {
            rc.QueryParameters.Filter = filter;
        }, timeoutCts.Token).ConfigureAwait(false);
        return page?.Value ?? new List<Application>();
    }

    /// <summary>
    /// Reads the existing registration's owners and federated credentials and returns why it must not be adopted, or
    /// null (T240a review). The owner check runs for Spaarke-tenant profiles only: in a customer-owned (Model 2) tenant
    /// the control plane's principal id is a different object.
    /// </summary>
    private async Task<string?> CheckAdoptionAsync(
        GraphServiceClient graph, Application app, EntraAppRegRequest request, IReadOnlyCollection<string> managedFicNames,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);

        var owners = await graph.Applications[app.Id].Owners
            .GetAsync(rc => rc.QueryParameters.Select = ["id"], timeoutCts.Token).ConfigureAwait(false);
        var fics = await graph.Applications[app.Id].FederatedIdentityCredentials
            .GetAsync(rc => rc.QueryParameters.Select = ["name"], timeoutCts.Token).ConfigureAwait(false);

        var customerOwned = string.Equals(request.Profile, "customer-owned-model2", StringComparison.OrdinalIgnoreCase);
        return AdoptionRefusal(
            app,
            (owners?.Value ?? []).Select(o => o.Id ?? string.Empty).ToList(),
            (fics?.Value ?? []).Select(f => f.Name ?? string.Empty).ToList(),
            customerOwned ? null : _identity.CanonicalPrincipalObjectId(),
            managedFicNames,
            request.RequireSecretFreeIdentity);
    }

    /// <summary>
    /// Why an existing <c>spaarke-bff-api-{customerId}</c> registration must not be adopted, or null when it is safe
    /// (T240a review). Safe means nobody but the control plane can act as it, now or later: no client secret or
    /// certificate (stamps are secret-free), no federated credential other than the ones H3 keeps
    /// (<paramref name="managedFicNames"/> — the stamp UAMI's and, ISS-015, the L2 Worker's; their triples are then
    /// reconciled by <see cref="PlanFederatedCredentials"/>, so a drifted one is repaired, not adopted as is), and no
    /// owner other than the control plane (an owner could add a credential after H3 finishes). An app with no owner at
    /// all is safe. <paramref name="controlPlanePrincipalId"/> null skips the owner check (Model 2).
    /// </summary>
    internal static string? AdoptionRefusal(
        Application existing,
        IReadOnlyCollection<string> ownerIds,
        IReadOnlyCollection<string> ficNames,
        string? controlPlanePrincipalId,
        IReadOnlyCollection<string> managedFicNames,
        bool requireSecretFreeIdentity)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var reasons = new List<string>();

        var secrets = existing.PasswordCredentials?.Count ?? 0;
        var certificates = existing.KeyCredentials?.Count ?? 0;
        if (requireSecretFreeIdentity && secrets + certificates > 0)
        {
            reasons.Add($"it holds {secrets} client secret(s) and {certificates} certificate(s), and a stamp's BFF " +
                        "registration is secret-free");
        }

        ArgumentNullException.ThrowIfNull(managedFicNames);
        var foreignFics = ficNames.Where(n => !managedFicNames.Contains(n, StringComparer.Ordinal)).ToList();
        if (foreignFics.Count > 0)
        {
            reasons.Add($"it carries federated credential(s) H3 did not create: {string.Join(", ", foreignFics)}");
        }

        if (controlPlanePrincipalId is not null)
        {
            var foreignOwners = ownerIds
                .Where(id => !string.Equals(id, controlPlanePrincipalId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (foreignOwners.Count > 0)
            {
                reasons.Add($"it is owned by {string.Join(", ", foreignOwners)}, not only by the provisioning control plane");
            }
        }

        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }

    private async Task<Application> CreateAppAsync(
        GraphServiceClient graph, string displayName, CancellationToken ct)
    {
        var manifest = new Application
        {
            DisplayName = displayName,
            SignInAudience = _options.RequiredSignInAudience,
            Web = new Microsoft.Graph.Models.WebApplication
            {
                RedirectUris = new List<string> { $"https://{displayName}.spaarke.com{_options.RedirectUriPath}" },
                ImplicitGrantSettings = new ImplicitGrantSettings
                {
                    EnableAccessTokenIssuance = false,
                    EnableIdTokenIssuance = true,
                },
            },
            RequiredResourceAccess = BuildRequiredResourceAccess(),
            Api = new ApiApplication
            {
                Oauth2PermissionScopes = new List<PermissionScope> { BuildExposedScope() },
            },
            AppRoles = new List<AppRole> { BuildKeylessProofAppRole() },
            // T255: created with the `acct` optional claim; EnsureAcctOptionalClaimAsync then finds it and writes nothing.
            OptionalClaims = PlanAcctOptionalClaim(null),
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);
        var created = await graph.Applications.PostAsync(manifest, cancellationToken: timeoutCts.Token)
            .ConfigureAwait(false);
        if (created is null)
        {
            throw new InvalidOperationException($"Graph POST /applications returned null for '{displayName}'.");
        }

        // identifierUris cannot be set at creation time for some tenant
        // configurations (api://{appId} requires the appId to already exist);
        // PATCH it in as a second call — matches the retired script's own
        // two-phase approach (create THEN append identifierUri).
        await graph.Applications[created.Id].PatchAsync(new Application
        {
            IdentifierUris = new List<string> { $"api://{created.AppId}" },
        }, cancellationToken: timeoutCts.Token).ConfigureAwait(false);

        return created;
    }

    private async Task ReconcileExistingAppAsync(GraphServiceClient graph, Application app, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);

        var patch = new Application();
        var dirty = false;

        if (!string.Equals(app.SignInAudience, _options.RequiredSignInAudience, StringComparison.Ordinal))
        {
            patch.SignInAudience = _options.RequiredSignInAudience;
            dirty = true;
        }

        var requiredUri = $"api://{app.AppId}";
        var currentUris = app.IdentifierUris ?? new List<string>();
        if (!currentUris.Contains(requiredUri, StringComparer.Ordinal))
        {
            patch.IdentifierUris = currentUris.Append(requiredUri).ToList();
            dirty = true;
        }

        var currentScopes = app.Api?.Oauth2PermissionScopes ?? new List<PermissionScope>();
        if (!currentScopes.Any(s => string.Equals(s.Value, "user_impersonation", StringComparison.Ordinal)))
        {
            patch.Api = new ApiApplication
            {
                Oauth2PermissionScopes = currentScopes.Append(BuildExposedScope()).ToList(),
            };
            dirty = true;
        }

        var currentPerms = (app.RequiredResourceAccess ?? new List<RequiredResourceAccess>())
            .SelectMany(rra => (rra.ResourceAccess ?? new List<ResourceAccess>())
                .Select(ra => (ResourceAppId: rra.ResourceAppId, Id: ra.Id?.ToString())))
            .ToHashSet();
        var missing = EntraAppRegPermissionCatalog.All
            .Where(p => !currentPerms.Contains((p.ResourceAppId, p.PermissionId)))
            .ToList();
        if (missing.Count > 0)
        {
            patch.RequiredResourceAccess = MergeRequiredResourceAccess(
                app.RequiredResourceAccess ?? new List<RequiredResourceAccess>(), missing);
            dirty = true;
        }

        var plannedRoles = PlanKeylessProofAppRoles(app.AppRoles);
        if (plannedRoles is not null)
        {
            patch.AppRoles = plannedRoles;
            dirty = true;
        }

        if (dirty)
        {
            await graph.Applications[app.Id].PatchAsync(patch, cancellationToken: timeoutCts.Token)
                .ConfigureAwait(false);
            if (plannedRoles is not null)
            {
                // The assignment step reads the role from the app object it was handed.
                app.AppRoles = plannedRoles;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Client access (T240a) — see file header
    // ---------------------------------------------------------------------

    /// <summary>What <see cref="PlanClientAccess"/> decided: a PATCH body, nothing to do (both null), or an error.</summary>
    internal sealed record ClientAccessPlan(Application? Patch, string? Error);

    /// <summary>
    /// Plans the PATCH that makes the app's <c>spa.redirectUris</c> exactly <paramref name="spaRedirectUris"/> and its
    /// <c>api.preAuthorizedApplications</c> exactly <paramref name="preAuthorizedClientAppIds"/> on the enabled
    /// <c>user_impersonation</c> scope. Order-insensitive; the app's own id is never pre-authorized on itself. When the
    /// pre-authorization changes, the PATCH carries the app's existing scopes and other <c>api</c> values unchanged, so it
    /// cannot drop them whether Graph merges or replaces the <c>api</c> object.
    /// </summary>
    internal static ClientAccessPlan PlanClientAccess(
        Application current, IReadOnlyList<string>? spaRedirectUris, IReadOnlyList<string>? preAuthorizedClientAppIds)
    {
        ArgumentNullException.ThrowIfNull(current);

        // null = leave that part untouched; an empty list = make it empty.
        var desiredSpa = spaRedirectUris?.Distinct(StringComparer.Ordinal).ToList();
        var currentSpa = current.Spa?.RedirectUris ?? new List<string>();
        var spaChanged = desiredSpa is not null && !currentSpa.ToHashSet(StringComparer.Ordinal).SetEquals(desiredSpa);

        if (preAuthorizedClientAppIds is null)
        {
            return spaChanged
                ? new ClientAccessPlan(new Application { Spa = new SpaApplication { RedirectUris = desiredSpa } }, null)
                : new ClientAccessPlan(null, null);
        }

        var clients = preAuthorizedClientAppIds
            .Where(id => !string.Equals(id, current.AppId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var scopes = current.Api?.Oauth2PermissionScopes ?? new List<PermissionScope>();
        var scopeId = scopes.FirstOrDefault(s =>
            string.Equals(s.Value, "user_impersonation", StringComparison.Ordinal) && s.IsEnabled == true)?.Id;
        if (clients.Count > 0 && scopeId is null)
        {
            return new ClientAccessPlan(null,
                $"application {current.AppId} has no enabled user_impersonation scope to pre-authorize " +
                $"{string.Join(", ", clients)} on");
        }

        var scopeIdText = scopeId?.ToString("D");
        var currentPre = current.Api?.PreAuthorizedApplications ?? new List<PreAuthorizedApplication>();
        var preChanged = currentPre.Count != clients.Count
            || !clients.All(client => currentPre.Any(p =>
                string.Equals(p.AppId, client, StringComparison.OrdinalIgnoreCase)
                && p.DelegatedPermissionIds is { Count: 1 } ids
                && string.Equals(ids[0], scopeIdText, StringComparison.OrdinalIgnoreCase)));

        if (!spaChanged && !preChanged)
        {
            return new ClientAccessPlan(null, null);
        }

        var patch = new Application();
        if (spaChanged)
        {
            patch.Spa = new SpaApplication { RedirectUris = desiredSpa! };
        }
        if (preChanged)
        {
            var api = new ApiApplication
            {
                Oauth2PermissionScopes = scopes.ToList(),
                PreAuthorizedApplications = clients
                    .Select(client => new PreAuthorizedApplication
                    {
                        AppId = client,
                        DelegatedPermissionIds = new List<string> { scopeIdText! },
                    })
                    .ToList(),
            };
            // Carried only when set: an explicit null would ask Graph to clear them.
            if (current.Api?.KnownClientApplications is { } known)
            {
                api.KnownClientApplications = known;
            }
            if (current.Api?.RequestedAccessTokenVersion is { } version)
            {
                api.RequestedAccessTokenVersion = version;
            }
            if (current.Api?.AcceptMappedClaims is { } acceptMapped)
            {
                api.AcceptMappedClaims = acceptMapped;
            }
            patch.Api = api;
        }
        return new ClientAccessPlan(patch, null);
    }

    /// <summary>
    /// Reads the app fresh (the create response and a reconcile PATCH leave the in-memory object without its current
    /// <c>api</c> / <c>spa</c>), plans with <see cref="PlanClientAccess"/>, and PATCHes this app only when something
    /// differs. Retries a 404 while Entra propagates a just-created app. Returns null on success or a Failure.
    /// </summary>
    private async Task<EntraAppRegOutcome?> EnsureClientAccessAsync(
        GraphServiceClient graph, string appObjectId, EntraAppRegRequest request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.GraphRequestTimeout);

                var current = await graph.Applications[appObjectId].GetAsync(rc =>
                {
                    rc.QueryParameters.Select = ["id", "appId", "api", "spa"];
                }, timeoutCts.Token).ConfigureAwait(false);
                if (current is null)
                {
                    return new EntraAppRegOutcome.Failure(
                        $"Graph GET /applications/{appObjectId} returned nothing. [{EntraAppRegRejectionCodes.ClientAccessFailed}]");
                }

                var plan = PlanClientAccess(current, request.SpaRedirectUris, request.PreAuthorizedClientAppIds);
                if (plan.Error is not null)
                {
                    return new EntraAppRegOutcome.Failure($"{plan.Error}. [{EntraAppRegRejectionCodes.ClientAccessFailed}]");
                }
                if (plan.Patch is null)
                {
                    return null;
                }

                await graph.Applications[appObjectId].PatchAsync(plan.Patch, cancellationToken: timeoutCts.Token)
                    .ConfigureAwait(false);
                _logger.LogInformation(
                    "H3 set client access: customerId={CustomerId} appId={AppId} spaRedirects={SpaCount} preAuthorizedClients={ClientCount}",
                    request.CustomerId, current.AppId, request.SpaRedirectUris?.Count ?? 0,
                    request.PreAuthorizedClientAppIds?.Count ?? 0);
                return null;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == 404 && attempt < _options.RoleAssignmentRetryCount)
            {
                _logger.LogInformation(ex,
                    "H3 client-access read attempt {Attempt}/{Max} returned 404 — retrying after propagation delay.",
                    attempt, _options.RoleAssignmentRetryCount);
                await Task.Delay(_options.RoleAssignmentRetryDelay, ct).ConfigureAwait(false);
            }
            catch (ODataError ex)
            {
                return new EntraAppRegOutcome.Failure(
                    $"Setting the SPA redirect and pre-authorized clients on application {appObjectId} failed: Graph " +
                    $"ODataError {ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. " +
                    $"[{EntraAppRegRejectionCodes.ClientAccessFailed}]");
            }
        }
    }

    // ---------------------------------------------------------------------
    // `acct` optional claim (task 255) — see file header
    // ---------------------------------------------------------------------

    /// <summary>The optional claim the stamp BFF's workforce member test reads (member = 0, guest = 1).</summary>
    internal const string AcctClaimName = "acct";

    /// <summary>
    /// The <c>optionalClaims</c> to PATCH so the access tokens carry <c>acct</c>, or null when they already do. Graph
    /// replaces the whole <c>optionalClaims</c> object, so the plan carries every existing access-, id- and SAML-token
    /// claim unchanged. Each claim is COPIED into a new object: a model read from Graph is not re-serialised whole when
    /// reused in a PATCH body (the SDK's backing store sends only changed values).
    /// </summary>
    internal static OptionalClaims? PlanAcctOptionalClaim(OptionalClaims? current)
    {
        var accessToken = current?.AccessToken ?? [];
        if (accessToken.Any(c => string.Equals(c.Name, AcctClaimName, StringComparison.Ordinal)))
        {
            return null;
        }

        return new OptionalClaims
        {
            AccessToken = accessToken.Select(Copy)
                .Append(new OptionalClaim { Name = AcctClaimName, Essential = false, AdditionalProperties = [] })
                .ToList(),
            IdToken = (current?.IdToken ?? []).Select(Copy).ToList(),
            Saml2Token = (current?.Saml2Token ?? []).Select(Copy).ToList(),
        };

        static OptionalClaim Copy(OptionalClaim claim) => new()
        {
            Name = claim.Name,
            Source = claim.Source,
            Essential = claim.Essential,
            AdditionalProperties = claim.AdditionalProperties?.ToList() ?? [],
        };
    }

    /// <summary>
    /// Reads the app's <c>optionalClaims</c> fresh, adds <c>acct</c> to the access tokens when missing
    /// (<see cref="PlanAcctOptionalClaim"/>), and reads it back. Idempotent: a registration that has it is not written.
    /// Returns null on success or a Failure.
    /// </summary>
    private async Task<EntraAppRegOutcome?> EnsureAcctOptionalClaimAsync(
        GraphServiceClient graph, string appObjectId, EntraAppRegRequest request, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.GraphRequestTimeout);

            var current = await graph.Applications[appObjectId]
                .GetAsync(rc => rc.QueryParameters.Select = ["id", "appId", "optionalClaims"], timeoutCts.Token)
                .ConfigureAwait(false);
            if (current is null)
            {
                return new EntraAppRegOutcome.Failure(
                    $"Graph GET /applications/{appObjectId} returned nothing. [{EntraAppRegRejectionCodes.AcctClaimFailed}]");
            }

            var plan = PlanAcctOptionalClaim(current.OptionalClaims);
            if (plan is null)
            {
                return null;
            }

            await graph.Applications[appObjectId].PatchAsync(new Application { OptionalClaims = plan }, cancellationToken: timeoutCts.Token)
                .ConfigureAwait(false);

            // A write we did not observe is not a write we can report (the retired script read it back too). Entra
            // replicates writes, so a read straight after the PATCH may be stale: retry before calling it a failure.
            for (var attempt = 1; ; attempt++)
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(_options.GraphRequestTimeout);
                var after = await graph.Applications[appObjectId]
                    .GetAsync(rc => rc.QueryParameters.Select = ["id", "optionalClaims"], readCts.Token)
                    .ConfigureAwait(false);
                if (PlanAcctOptionalClaim(after?.OptionalClaims) is null)
                {
                    break;
                }
                if (attempt >= _options.RoleAssignmentRetryCount)
                {
                    return new EntraAppRegOutcome.Failure(
                        $"The acct optional claim was written to application {current.AppId} but is not on its access " +
                        $"tokens when read back ({attempt} reads). Resume H3. [{EntraAppRegRejectionCodes.AcctClaimFailed}]");
                }
                await Task.Delay(_options.RoleAssignmentRetryDelay, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "H3 added the acct optional claim: customerId={CustomerId} appId={AppId} accessTokenClaims={Claims}",
                request.CustomerId, current.AppId, string.Join(",", plan.AccessToken!.Select(c => c.Name)));
            return null;
        }
        catch (ODataError ex)
        {
            return new EntraAppRegOutcome.Failure(
                $"Adding the acct optional claim to application {appObjectId} failed: Graph ODataError " +
                $"{ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. " +
                $"[{EntraAppRegRejectionCodes.AcctClaimFailed}]");
        }
    }

    // ---------------------------------------------------------------------
    // Keyless-proof app role (task 230b) — see file header
    // ---------------------------------------------------------------------

    /// <summary>The application role that admits the L2 Worker identity to the stamp BFF's keyless proof.</summary>
    internal static AppRole BuildKeylessProofAppRole() => new()
    {
        Id = Guid.Parse(KeylessProofContract.AppRoleId),
        Value = KeylessProofContract.AppRoleValue,
        AllowedMemberTypes = new List<string> { "Application" },
        DisplayName = "Provisioning keyless proof",
        Description = "Lets the Spaarke provisioning control plane run the stamp's keyless proof (one managed-identity " +
                      "call per Azure service). Assigned only to the control plane's identity.",
        IsEnabled = true,
    };

    /// <summary>
    /// The app roles to PATCH so the keyless-proof role is present and enabled, or null when nothing changes. The role
    /// is matched by VALUE: an existing role keeps its id (an enabled role's id cannot change), every other role is
    /// carried over unchanged, and a disabled one is re-enabled.
    /// </summary>
    internal static List<AppRole>? PlanKeylessProofAppRoles(IReadOnlyList<AppRole>? current)
    {
        var roles = current ?? Array.Empty<AppRole>();
        var existing = roles.FirstOrDefault(r => string.Equals(r.Value, KeylessProofContract.AppRoleValue, StringComparison.Ordinal));
        if (existing is null)
        {
            return roles.Append(BuildKeylessProofAppRole()).ToList();
        }
        if (existing.IsEnabled == true
            && existing.AllowedMemberTypes?.Count == 1
            && string.Equals(existing.AllowedMemberTypes[0], "Application", StringComparison.Ordinal))
        {
            return null;
        }
        return roles
            .Select(r => ReferenceEquals(r, existing)
                ? new AppRole
                {
                    Id = existing.Id,
                    Value = existing.Value,
                    AllowedMemberTypes = new List<string> { "Application" },
                    DisplayName = existing.DisplayName,
                    Description = existing.Description,
                    IsEnabled = true,
                }
                : r)
            .ToList();
    }

    /// <summary>The keyless-proof role's id on this app — the existing role's when present, else the contract's.</summary>
    internal static Guid KeylessProofRoleId(IReadOnlyList<AppRole>? roles)
        => roles?.FirstOrDefault(r => string.Equals(r.Value, KeylessProofContract.AppRoleValue, StringComparison.Ordinal))?.Id
           ?? Guid.Parse(KeylessProofContract.AppRoleId);

    /// <summary>True when <paramref name="principalId"/> already holds <paramref name="appRoleId"/>.</summary>
    internal static bool HasRoleAssignment(IEnumerable<AppRoleAssignment>? assignments, Guid principalId, Guid appRoleId)
        => assignments?.Any(a => a.PrincipalId == principalId && a.AppRoleId == appRoleId) == true;

    /// <summary>
    /// Assignments of <paramref name="appRoleId"/> to anyone but <paramref name="principalId"/> — the role admits a caller
    /// to the stamp's keyless proof, so only the L2 Worker identity may hold it (task 230b).
    /// </summary>
    internal static IReadOnlyList<AppRoleAssignment> ForeignRoleHolders(IEnumerable<AppRoleAssignment>? assignments, Guid principalId, Guid appRoleId)
        => (assignments ?? Enumerable.Empty<AppRoleAssignment>())
            .Where(a => a.AppRoleId == appRoleId && a.PrincipalId != principalId && !string.IsNullOrWhiteSpace(a.Id))
            .ToList();

    /// <summary>
    /// Assigns the keyless-proof role on the BFF service principal to the L2 Worker identity, idempotently, and removes
    /// the role from anyone else (only L2 may hold it). Retries while Entra has not yet propagated a just-added role
    /// (400) or a just-created service principal (404). Returns null on success or a Failure.
    /// </summary>
    private async Task<EntraAppRegOutcome?> EnsureKeylessProofRoleAssignmentAsync(
        GraphServiceClient graph, Application app, string servicePrincipalId, EntraAppRegRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Profile, "customer-owned-model2", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "H3: keyless-proof role defined but NOT assigned — the {Profile} app-reg is in the customer's tenant, where " +
                "the L2 identity has no service principal (Model 2 gap, task 230b). customerId={CustomerId}",
                request.Profile, request.CustomerId);
            return null;
        }

        var principalId = Guid.Parse(_identity.CanonicalPrincipalObjectId());
        var resourceId = Guid.Parse(servicePrincipalId);
        var appRoleId = KeylessProofRoleId(app.AppRoles);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.GraphRequestTimeout);

                var existing = await graph.ServicePrincipals[servicePrincipalId].AppRoleAssignedTo
                    .GetAsync(rc => rc.QueryParameters.Top = 999, timeoutCts.Token).ConfigureAwait(false);

                foreach (var foreign in ForeignRoleHolders(existing?.Value, principalId, appRoleId))
                {
                    _logger.LogWarning(
                        "H3 removing the keyless-proof role from {PrincipalType} {PrincipalId} ({PrincipalName}) — only the L2 " +
                        "identity may hold it (task 230b). customerId={CustomerId}",
                        foreign.PrincipalType, foreign.PrincipalId, foreign.PrincipalDisplayName, request.CustomerId);
                    await graph.ServicePrincipals[servicePrincipalId].AppRoleAssignedTo[foreign.Id]
                        .DeleteAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);
                }

                if (HasRoleAssignment(existing?.Value, principalId, appRoleId))
                {
                    return null;
                }

                await graph.ServicePrincipals[servicePrincipalId].AppRoleAssignedTo.PostAsync(new AppRoleAssignment
                {
                    PrincipalId = principalId,
                    ResourceId = resourceId,
                    AppRoleId = appRoleId,
                }, cancellationToken: timeoutCts.Token).ConfigureAwait(false);

                _logger.LogInformation(
                    "H3 assigned the keyless-proof role to the L2 identity: customerId={CustomerId} appId={AppId} principal={PrincipalId}",
                    request.CustomerId, app.AppId, principalId);
                return null;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode is 400 or 404 && attempt < _options.RoleAssignmentRetryCount)
            {
                // A role added moments ago (400) or a service principal created moments ago (404) may not be visible yet.
                _logger.LogInformation(ex,
                    "H3 keyless-proof role assignment attempt {Attempt}/{Max} returned {Status} — retrying after propagation delay.",
                    attempt, _options.RoleAssignmentRetryCount, ex.ResponseStatusCode);
                await Task.Delay(_options.RoleAssignmentRetryDelay, ct).ConfigureAwait(false);
            }
            catch (ODataError ex)
            {
                return new EntraAppRegOutcome.Failure(
                    $"Assigning the keyless-proof app role ({KeylessProofContract.AppRoleValue}) to the L2 identity {principalId} on " +
                    $"the BFF service principal {servicePrincipalId} failed: Graph ODataError {ex.ResponseStatusCode}: " +
                    $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. [{EntraAppRegRejectionCodes.KeylessProofRoleAssignmentFailed}]");
            }
        }
    }

    private static List<RequiredResourceAccess> MergeRequiredResourceAccess(
        IReadOnlyList<RequiredResourceAccess> current, IReadOnlyList<EntraAppRegPermission> missing)
    {
        var byResource = current.ToDictionary(rra => rra.ResourceAppId!, rra => rra, StringComparer.Ordinal);
        foreach (var group in missing.GroupBy(p => p.ResourceAppId))
        {
            if (!byResource.TryGetValue(group.Key, out var rra))
            {
                rra = new RequiredResourceAccess { ResourceAppId = group.Key, ResourceAccess = new List<ResourceAccess>() };
                byResource[group.Key] = rra;
            }
            rra.ResourceAccess ??= new List<ResourceAccess>();
            foreach (var perm in group)
            {
                rra.ResourceAccess.Add(new ResourceAccess { Id = Guid.Parse(perm.PermissionId), Type = "Scope" });
            }
        }
        return byResource.Values.ToList();
    }

    private static List<RequiredResourceAccess> BuildRequiredResourceAccess()
        => EntraAppRegPermissionCatalog.All
            .GroupBy(p => p.ResourceAppId)
            .Select(g => new RequiredResourceAccess
            {
                ResourceAppId = g.Key,
                ResourceAccess = g.Select(p => new ResourceAccess { Id = Guid.Parse(p.PermissionId), Type = "Scope" }).ToList(),
            })
            .ToList();

    private static PermissionScope BuildExposedScope() => new()
    {
        Id = Guid.NewGuid(),
        Value = "user_impersonation",
        Type = "User",
        IsEnabled = true,
        AdminConsentDescription = "Allow the application to access the BFF API on behalf of the signed-in user.",
        AdminConsentDisplayName = "Access BFF API",
        UserConsentDescription = "Allow the application to access the BFF API on your behalf.",
        UserConsentDisplayName = "Access BFF API",
    };

    // ---------------------------------------------------------------------
    // Service principal + client secret
    // ---------------------------------------------------------------------

    /// <summary>Ensures the app's service principal exists and returns its object id.</summary>
    private async Task<string> EnsureServicePrincipalAsync(GraphServiceClient graph, string appId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);
        var existing = await graph.ServicePrincipals.GetAsync(rc =>
        {
            rc.QueryParameters.Filter = $"appId eq '{EscapeODataLiteral(appId)}'";
        }, timeoutCts.Token).ConfigureAwait(false);

        var found = existing?.Value?.FirstOrDefault()?.Id;
        if (!string.IsNullOrWhiteSpace(found))
        {
            return found;
        }

        var created = await graph.ServicePrincipals.PostAsync(new ServicePrincipal { AppId = appId },
            cancellationToken: timeoutCts.Token).ConfigureAwait(false);
        return created?.Id
            ?? throw new InvalidOperationException($"Graph POST /servicePrincipals returned no id for app '{appId}'.");
    }

    private async Task<string> EnsureClientSecretAsync(GraphServiceClient graph, Application app, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var hasValidCredential = (app.PasswordCredentials ?? new List<PasswordCredential>())
            .Any(pc => pc.EndDateTime is not null && pc.EndDateTime.Value > now);

        if (hasValidCredential)
        {
            // A valid credential exists on the app-reg. This provisioner
            // cannot re-derive its cleartext value (Graph never returns it
            // again after creation) — returning empty signals "no NEW secret
            // text available" to the KV-write step, which skips the
            // ClientSecretName write in that case (never overwrites a live
            // KV secret with a blank).
            return string.Empty;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);
        var result = await graph.Applications[app.Id].AddPassword.PostAsync(new Microsoft.Graph.Applications.Item.AddPassword.AddPasswordPostRequestBody
        {
            PasswordCredential = new PasswordCredential
            {
                DisplayName = $"Production-{now:yyyyMMdd}",
                EndDateTime = now.AddMonths(_options.SecretExpiryMonths),
            },
        }, cancellationToken: timeoutCts.Token).ConfigureAwait(false);

        if (result?.SecretText is null)
        {
            throw new InvalidOperationException(
                $"Graph POST /applications/{app.Id}/addPassword returned no SecretText for app '{app.AppId}'.");
        }

        return result.SecretText;
    }

    // ---------------------------------------------------------------------
    // FIC (both models post-task-222 per D-13, auth-v4 §3.1) — see
    // file-header GOTCHA 2.
    // A42 (task 205b, FR-C4) hardening: cross-tenant refusal guard (SF-5),
    // triple-keyed idempotency (SF-7), exit-2-equivalent verification state
    // (SF-8). Parity contract:
    // projects/customer-provisioning-orchestration-r1/notes/decisions/
    // 205b-a42-fic-parity-contract.md
    // ---------------------------------------------------------------------

    /// <summary>One federated identity credential H3 keeps on the registration: its name and its (issuer, subject, audience) triple.</summary>
    internal sealed record FicSpec(string Name, string Issuer, string Subject, string Audience, string Description);

    /// <summary>What <see cref="BuildRequiredFicSpecs"/> decided: the credentials H3 keeps, or why it cannot plan them.</summary>
    internal sealed record RequiredFics(IReadOnlyList<FicSpec> Specs, string? Error)
    {
        /// <summary>The names H3 keeps — the only federated credentials an adopted registration may carry.</summary>
        public IReadOnlyList<string> Names => Specs.Select(s => s.Name).ToList();
    }

    /// <summary>What <see cref="PlanFederatedCredentials"/> decided: credentials to delete first, then specs to create.</summary>
    internal sealed record FicReconcilePlan(IReadOnlyList<FederatedIdentityCredential> Deletes, IReadOnlyList<FicSpec> Creates)
    {
        /// <summary>True when every required credential is already in place and nothing needs writing.</summary>
        public bool IsSatisfied => Deletes.Count == 0 && Creates.Count == 0;
    }

    /// <summary>
    /// The federated credentials H3 keeps on the customer's BFF registration, or why it cannot plan them (pure; no I/O):
    /// <list type="number">
    /// <item><paramref name="options"/>.<see cref="EntraAppRegOptions.FicName"/> — subject = the stamp BFF UAMI's
    /// principalId (auth-v4 §3.1; the BFF's own OBO credential). Issuer tenant per profile
    /// (<see cref="ResolveUamiTenantId"/>), cross-tenant pairs refused (<see cref="AssertFicTenancy"/> throws).</item>
    /// <item>ISS-015: <paramref name="options"/>.<see cref="EntraAppRegOptions.WorkerFicName"/> — subject = the L2
    /// Worker UAMI's principalId (<see cref="ControlPlaneIdentityOptions.PrincipalObjectId"/>, the OBJECT id, never its
    /// clientId — a clientId subject creates cleanly and fails at exchange with AADSTS700213), issuer = Spaarke's
    /// tenant, where that UAMI lives. H6 / H7 / H7b sign in as this registration through it
    /// (<c>WorkerDataverseCredentialFactory</c>, D-13). Spaarke-tenant profiles only: a <c>customer-owned-model2</c>
    /// registration lives in the customer's tenant and MI-FIC cannot cross tenants (Model 2 is out of scope, owner
    /// 2026-09-30 — the same gap as the keyless-proof role).</item>
    /// </list>
    /// The Worker credential is refused (<see cref="EntraAppRegRejectionCodes.WorkerFicIdentityMissing"/>) when the
    /// Worker principal id is blank, not a GUID or the empty GUID, when it equals the stamp UAMI (the two triples would
    /// coincide and the stamp's trust would be the control plane's), or when the two names are blank or equal.
    /// </summary>
    internal static RequiredFics BuildRequiredFicSpecs(
        string profile,
        string requestTenantId,
        string? spaarkeTenantId,
        string stampUamiPrincipalId,
        string? workerPrincipalObjectId,
        EntraAppRegOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var issuerTenantId = ResolveUamiTenantId(profile, requestTenantId, spaarkeTenantId);
        if (string.IsNullOrWhiteSpace(issuerTenantId))
        {
            return new RequiredFics([],
                $"Cannot compute FIC issuer — profile='{profile}' requires " +
                $"{(string.Equals(profile, "customer-owned-model2", StringComparison.OrdinalIgnoreCase) ? "request.TenantId" : "EntraAppRegOptions:SpaarkeTenantId config (Worker setting EntraAppRegOptions__SpaarkeTenantId)")}, " +
                $"which is blank. [{EntraAppRegRejectionCodes.FicCreationFailed}]");
        }

        // A42 / SF-5: a cross-tenant (app registration, UAMI) pair is refused before anything is planned.
        AssertFicTenancy(requestTenantId, issuerTenantId, profile);
        var issuer = $"https://login.microsoftonline.com/{issuerTenantId}/v2.0";

        var stamp = new FicSpec(
            options.FicName,
            issuer,
            // §3.1 documented trap: subject MUST be the UAMI's principalId (object id), NOT its clientId — the
            // wrong-subject FIC creates cleanly and dies at exchange with AADSTS700213 (auth-v4 §11 invariant 1).
            stampUamiPrincipalId,
            options.FicAudience,
            "Managed-identity trust for the stamp's BFF UAMI (auth-v4 §3.1 recipe; issuer tenant per profile) — created by H3 (task 130; A42 triple-idempotency).");

        if (string.Equals(profile, "customer-owned-model2", StringComparison.OrdinalIgnoreCase))
        {
            return new RequiredFics([stamp], null);
        }

        if (!Guid.TryParse(workerPrincipalObjectId?.Trim(), out var worker) || worker == Guid.Empty)
        {
            return new RequiredFics([],
                $"ControlPlaneIdentity:PrincipalObjectId must be the object id (GUID) of the L2 Worker UAMI — H3 gives the " +
                $"customer's BFF registration a federated credential trusting it so H6/H7/H7b can sign in as that " +
                $"registration (ISS-015); got '{workerPrincipalObjectId}'. Nothing was written. Set the Worker setting " +
                $"ControlPlaneIdentity__PrincipalObjectId (platform-controlplane.bicep: uami.outputs.principalId) and resume. " +
                $"[{EntraAppRegRejectionCodes.WorkerFicIdentityMissing}]");
        }

        if (string.IsNullOrWhiteSpace(options.WorkerFicName) || string.IsNullOrWhiteSpace(options.FicName)
            || string.Equals(options.WorkerFicName, options.FicName, StringComparison.Ordinal))
        {
            return new RequiredFics([],
                $"EntraAppRegOptions:FicName ('{options.FicName}') and EntraAppRegOptions:WorkerFicName " +
                $"('{options.WorkerFicName}') must both be set and differ — H3 keeps two federated credentials on the " +
                $"registration. Nothing was written. [{EntraAppRegRejectionCodes.WorkerFicIdentityMissing}]");
        }

        if (Guid.TryParse(stampUamiPrincipalId?.Trim(), out var stampPrincipal) && stampPrincipal == worker)
        {
            return new RequiredFics([],
                $"The stamp's BFF UAMI ({stampUamiPrincipalId}) is the L2 control plane's own identity — a stamp's BFF " +
                $"must run as its own UAMI (H2a output InterStepState.MiObjectId). Nothing was written. " +
                $"[{EntraAppRegRejectionCodes.WorkerFicIdentityMissing}]");
        }

        var workerSpec = new FicSpec(
            options.WorkerFicName,
            issuer,
            worker.ToString("D"),
            options.FicAudience,
            "Lets the Spaarke provisioning control plane (L2 Worker UAMI) sign in as this registration for solution import, " +
            "environment-variable values and Secure Record setup (D-13; ISS-015) — created by H3.");

        return new RequiredFics([stamp, workerSpec], null);
    }

    /// <summary>
    /// Plans what makes the registration carry every credential in <paramref name="required"/> (pure; no I/O). A
    /// credential is in place when one with its (issuer, subject, audience) triple exists under its own name, or under a
    /// name H3 does not manage (A42 / SF-7: the triple is what Entra matches; a legacy name is a label). Otherwise H3
    /// deletes, first, (a) a credential carrying the triple under the OTHER managed name (misnamed — Entra keeps
    /// (issuer, subject) unique per application, so it must go before the triple can be re-created) and (b) whatever
    /// holds the spec's own name with a different triple (drift: issuer/subject/audience cannot be PATCHed — the
    /// provisioning-run Force-equivalent, A42 parity contract §4); then creates the spec. A credential that satisfies one
    /// spec is never deleted for another (two specs never share a triple — <see cref="BuildRequiredFicSpecs"/> refuses it).
    /// </summary>
    internal static FicReconcilePlan PlanFederatedCredentials(
        IEnumerable<FederatedIdentityCredential>? existing, IReadOnlyList<FicSpec> required)
    {
        ArgumentNullException.ThrowIfNull(required);
        var current = existing?.ToList() ?? [];
        var managedNames = required.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var deletes = new List<FederatedIdentityCredential>();
        var creates = new List<FicSpec>();

        foreach (var spec in required)
        {
            var carriers = current
                .Where(f => FindEquivalentByTriple([f], spec.Issuer, spec.Subject, spec.Audience) is not null)
                .ToList();
            if (carriers.Any(f => string.Equals(f.Name, spec.Name, StringComparison.Ordinal)
                                  || !managedNames.Contains(f.Name ?? string.Empty)))
            {
                continue;
            }

            deletes.AddRange(carriers);
            deletes.AddRange(current.Where(f => string.Equals(f.Name, spec.Name, StringComparison.Ordinal)));
            creates.Add(spec);
        }

        return new FicReconcilePlan(deletes.Distinct().ToList(), creates);
    }

    /// <summary>
    /// Ensures the registration carries every credential in <paramref name="specs"/>
    /// (<see cref="PlanFederatedCredentials"/>), then performs an INDEPENDENT re-GET (never trusting the write call's own
    /// echoed response) to confirm they persisted exactly as planned — see GOTCHA 2 in the file header for why a literal
    /// OAuth2 exchange is not something L2 can perform for the stamp UAMI here (the Worker credential's first exchange is
    /// H6's first token request). Returns null on success (the caller reports
    /// <see cref="FicVerificationState.PendingPostAppServiceVerification"/> — the script exit-2 equivalent, NEVER
    /// terminal success per SF-8), or a Failure outcome to propagate. The cross-tenant guard (SF-5) ran when the specs
    /// were built, before any Graph call.
    /// </summary>
    private async Task<EntraAppRegOutcome?> EnsureFederatedIdentityCredentialsAsync(
        GraphServiceClient graph, string appObjectId, IReadOnlyList<FicSpec> specs, EntraAppRegRequest request,
        CancellationToken ct)
    {
        try
        {
            FederatedIdentityCredentialCollectionResponse? existing;
            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                readCts.CancelAfter(_options.GraphRequestTimeout);
                existing = await graph.Applications[appObjectId].FederatedIdentityCredentials
                    .GetAsync(cancellationToken: readCts.Token).ConfigureAwait(false);
            }

            var plan = PlanFederatedCredentials(existing?.Value, specs);
            foreach (var stale in plan.Deletes)
            {
                if (string.IsNullOrWhiteSpace(stale.Id))
                {
                    return new EntraAppRegOutcome.Failure(
                        $"Federated credential '{stale.Name}' on app object {appObjectId} must be replaced but Graph " +
                        $"returned it without an id. [{EntraAppRegRejectionCodes.FicCreationFailed}]");
                }

                _logger.LogWarning(
                    "FIC '{FicName}' (subject {Subject}) does not match the credential H3 keeps under its name, or carries " +
                    "another managed credential's triple — drift; deleting before re-creating (provisioning-run " +
                    "Force-equivalent, A42 parity contract §4). appObjectId={AppObjectId} customerId={CustomerId}",
                    stale.Name, stale.Subject, appObjectId, request.CustomerId);
                using var deleteCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deleteCts.CancelAfter(_options.GraphRequestTimeout);
                await graph.Applications[appObjectId].FederatedIdentityCredentials[stale.Id]
                    .DeleteAsync(cancellationToken: deleteCts.Token).ConfigureAwait(false);
            }

            foreach (var spec in plan.Creates)
            {
                using var createCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                createCts.CancelAfter(_options.GraphRequestTimeout);
                await graph.Applications[appObjectId].FederatedIdentityCredentials.PostAsync(new FederatedIdentityCredential
                {
                    Name = spec.Name,
                    Issuer = spec.Issuer,
                    Subject = spec.Subject,
                    Audiences = new List<string> { spec.Audience },
                    Description = spec.Description,
                }, cancellationToken: createCts.Token).ConfigureAwait(false);
                _logger.LogInformation(
                    "H3 created FIC '{FicName}' (subject {Subject}) on appObjectId={AppObjectId} customerId={CustomerId}",
                    spec.Name, spec.Subject, appObjectId, request.CustomerId);
            }
        }
        catch (ODataError ex)
        {
            return new EntraAppRegOutcome.Failure(
                $"FIC creation failed for app object {appObjectId}: Graph ODataError {ex.ResponseStatusCode}: " +
                $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. [{EntraAppRegRejectionCodes.FicCreationFailed}]");
        }

        // Independent re-GET verification (GOTCHA 2) — retries a few times to absorb read-after-write propagation lag,
        // NOT AADSTS70025 (there is no live OAuth2 exchange call here — see file header; the exchange-side propagation
        // policy lives in FicExchangeOutcomeClassifier for the exchange-capable hosts). Verified with the same planner
        // as the write: every required triple in place (SF-7: a legacy-named equivalent is a satisfied state).
        IReadOnlyList<FicSpec> unconfirmed = specs;
        for (var attempt = 1; attempt <= _options.FicExchangeRetryCount; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var verifyTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                verifyTimeoutCts.CancelAfter(_options.GraphRequestTimeout);
                var reGet = await graph.Applications[appObjectId].FederatedIdentityCredentials
                    .GetAsync(cancellationToken: verifyTimeoutCts.Token).ConfigureAwait(false);
                var check = PlanFederatedCredentials(reGet?.Value, specs);

                if (check.IsSatisfied)
                {
                    // Persisted + structurally verified. NOT exchange-verified (GOTCHA 2) — the caller reports the
                    // exit-2 equivalent (PendingPostAppServiceVerification), never terminal success (SF-8).
                    return null;
                }
                unconfirmed = check.Creates;
            }
            catch (ODataError ex) when (attempt < _options.FicExchangeRetryCount)
            {
                _logger.LogInformation(ex,
                    "FIC re-GET verify attempt {Attempt}/{Max} transient ODataError {Status} — retrying",
                    attempt, _options.FicExchangeRetryCount, ex.ResponseStatusCode);
            }

            if (attempt < _options.FicExchangeRetryCount)
            {
                await Task.Delay(_options.FicExchangeRetryDelay, ct).ConfigureAwait(false);
            }
        }

        return new EntraAppRegOutcome.Failure(
            $"FIC re-GET verification did not confirm the (issuer, subject, audience) triple of " +
            $"{string.Join(", ", unconfirmed.Select(s => $"'{s.Name}'"))} after {_options.FicExchangeRetryCount} attempts. " +
            $"[{EntraAppRegRejectionCodes.FicVerificationFailed}]");
    }

    /// <summary>
    /// Derives the tenant the FIC-issuing UAMI lives in, per profile
    /// (auth-v4 §3.1 + §9.2 reading (a), owner-ratified 2026-08-25):
    /// <c>customer-owned-model2</c> → the customer's own tenant (stamp UAMI
    /// lives in the customer's subscription); every other profile → Spaarke's
    /// tenant (shared/stamp UAMI lives in Spaarke's subscription). Extracted
    /// internal-static (A42) so the derivation + guard are unit-testable
    /// without live Graph.
    /// </summary>
    internal static string? ResolveUamiTenantId(string profile, string requestTenantId, string? spaarkeTenantId)
        => string.Equals(profile, "customer-owned-model2", StringComparison.OrdinalIgnoreCase)
            ? requestTenantId
            : spaarkeTenantId;

    /// <summary>
    /// C# port of master `Register-EntraAppRegistrations.ps1`'s
    /// `Assert-SpaarkeFicTenancy` (script :350-396) — task 205b row A42,
    /// SF-5 closure. Throws <see cref="CrossTenantFicRefusedException"/> when
    /// the app registration's tenant and the UAMI's tenant differ. The
    /// refusal is UNCONDITIONAL (PS parity — Entra's same-tenant FIC rule has
    /// no profile exception); <paramref name="profile"/> is diagnostic
    /// context only. Tenant GUIDs compare case-insensitively (PS `-ne`
    /// parity).
    /// </summary>
    internal static void AssertFicTenancy(string appRegistrationTenantId, string uamiTenantId, string profile)
    {
        if (!string.Equals(appRegistrationTenantId, uamiTenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrossTenantFicRefusedException(appRegistrationTenantId, uamiTenantId, profile);
        }
    }

    /// <summary>
    /// Returns the first federated credential whose (issuer, subject,
    /// audience) TRIPLE matches — regardless of its name (SF-7; parity with
    /// the script's `Find-SpaarkeEquivalentFederatedCredential`, incl. the
    /// exactly-one-audience requirement). The name of a FIC is a label; the
    /// triple is what Entra matches assertions against and enforces
    /// uniqueness on. Null when no credential carries the triple.
    /// </summary>
    internal static FederatedIdentityCredential? FindEquivalentByTriple(
        IEnumerable<FederatedIdentityCredential>? candidates,
        string issuer, string subject, string audience)
    {
        if (candidates is null)
        {
            return null;
        }

        return candidates.FirstOrDefault(f =>
            string.Equals(f.Issuer, issuer, StringComparison.Ordinal)
            && string.Equals(f.Subject, subject, StringComparison.Ordinal)
            && f.Audiences is { Count: 1 }
            && string.Equals(f.Audiences[0], audience, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // KV writes (deferred — see file-header + IEntraAppRegProvisioner doc)
    // ---------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<string?> CommitPendingSecretsAsync(
        IReadOnlyList<PendingKvSecretWrite> pendingWrites, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pendingWrites);
        if (pendingWrites.Count == 0)
        {
            return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.KvOperationTimeout);

            // Group by vault so a (hypothetical) multi-vault pending set only
            // constructs one SecretClient per vault.
            foreach (var group in pendingWrites.GroupBy(w => w.VaultName, StringComparer.Ordinal))
            {
                var vaultUri = new Uri($"https://{group.Key}.vault.azure.net/");
                var client = new SecretClient(vaultUri, _sharedCredential);
                foreach (var write in group)
                {
                    await client.SetSecretAsync(write.SecretName, write.Value, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"KV commit failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds the canonical <c>@Microsoft.KeyVault(SecretUri=...)</c> URI
    /// reference literal. Exposed internal so unit tests (and
    /// H3EntraAppRegHandler) can construct expected refs without duplicating
    /// the format.
    /// </summary>
    internal static string BuildKvUriReference(string keyVaultName, string secretName)
        => $"@Microsoft.KeyVault(SecretUri=https://{keyVaultName}.vault.azure.net/secrets/{secretName}/)";

    private static string EscapeODataLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
