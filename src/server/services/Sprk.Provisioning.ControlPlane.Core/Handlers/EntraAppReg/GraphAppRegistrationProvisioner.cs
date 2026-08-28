// -----------------------------------------------------------------------------
// GraphAppRegistrationProvisioner.cs
//
// Task 130 (Wave G-3, xhigh) — production IEntraAppRegProvisioner. Ports the
// retired PS script's (see RegisterEntraAppRegScriptProvisioner.cs's
// retirement banner for the exact filename) 5-step app-registration
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
// collaborator (GraphRestAppRoleGranter, DataverseWebApiAppUserCreator,
// CreateNewContainerTypeScriptProvisioner, the retired
// RegisterEntraAppRegScriptProvisioner — each documents this same posture in
// its own file header). Handler unit tests (H3EntraAppRegHandlerTests)
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
// credential like the Graph calls above need). H3EntraAppRegHandler then
// points RunParameters.Secrets at these SAME (vault, name) pairs so H4's
// FromRunParameters resolver (task 126) can "copy" them — a harmless
// self-referential re-write — per manifest.yaml's BFF-API-ClientId/Audience
// exception_note (task 129 reclassification, owner E3).
//
// FIC RECIPE (auth-v4 §3.1, Model 2 only): subject = the UAMI's principalId
// (request.UamiPrincipalId — NOT its clientId, the documented most-common
// misconfiguration trap); audiences = ["api://AzureADTokenExchange"];
// issuer = Spaarke's own tenant for spaarke-hosted-model2 (UAMI lives in
// Spaarke's subscription) OR the customer's own tenant for
// customer-owned-model2 (UAMI lives in the customer's subscription).
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

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
    private readonly ILogger<GraphAppRegistrationProvisioner> _logger;

    /// <summary>Constructs the production provisioner. <paramref name="sharedCredential"/> is L2's own platform UAMI-pinned credential (KV writes only).</summary>
    public GraphAppRegistrationProvisioner(
        TokenCredential sharedCredential,
        IOptions<EntraAppRegOptions> options,
        ILogger<GraphAppRegistrationProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(sharedCredential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _sharedCredential = sharedCredential;
        _options = options.Value;
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

        var graph = BuildTenantScopedGraphClient(request.TenantId);
        var displayName = $"spaarke-bff-api-{request.CustomerId}";

        try
        {
            // (1) Get-or-create the app-reg.
            var app = await FindByDisplayNameAsync(graph, displayName, cancellationToken).ConfigureAwait(false);
            var created = false;
            if (app is null)
            {
                app = await CreateAppAsync(graph, displayName, cancellationToken).ConfigureAwait(false);
                created = true;
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

            // (3) Ensure service principal.
            await EnsureServicePrincipalAsync(graph, app.AppId!, cancellationToken).ConfigureAwait(false);

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

            // (5) FIC — Model 2 ONLY, auth-v4 §3.1 recipe.
            var ficFailure = await EnsureFederatedIdentityCredentialAsync(
                graph, app.Id!, request, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async Task<EntraAppRegSharedVerifyOutcome> VerifySharedAsync(
        EntraAppRegSharedVerifyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SharedAppId);

        if (string.IsNullOrWhiteSpace(_options.SpaarkeTenantId))
        {
            return new EntraAppRegSharedVerifyOutcome.Failure(
                "EntraAppReg:SpaarkeTenantId is not configured — required to query the shared app-reg " +
                "(it lives in Spaarke's own tenant, not the customer's).");
        }

        // Model 1 read-only verification: use Spaarke's own tenant (the shared
        // app-reg lives there) rather than a per-customer tenant credential.
        var graph = BuildTenantScopedGraphClient(_options.SpaarkeTenantId);

        try
        {
            var filter = $"appId eq '{EscapeODataLiteral(request.SharedAppId)}'";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.GraphRequestTimeout);
            var page = await graph.Applications.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = filter;
            }, timeoutCts.Token).ConfigureAwait(false);

            var app = page?.Value?.FirstOrDefault();
            if (app is null)
            {
                return new EntraAppRegSharedVerifyOutcome.Failure(
                    $"Shared app-reg (appId={request.SharedAppId}) not found in Spaarke's own tenant. " +
                    "Model 1 requires the shared multitenant app-reg to already exist — verify " +
                    "EntraAppReg:SharedBffAppRegistrationId + operator has run the initial platform setup.");
            }

            var driftReasons = new List<string>();
            if (!string.Equals(app.SignInAudience, _options.RequiredSignInAudience, StringComparison.Ordinal))
            {
                driftReasons.Add($"signInAudience='{app.SignInAudience}' (expected '{_options.RequiredSignInAudience}')");
            }

            var currentPerms = (app.RequiredResourceAccess ?? new List<RequiredResourceAccess>())
                .SelectMany(rra => (rra.ResourceAccess ?? new List<ResourceAccess>())
                    .Select(ra => (ResourceAppId: rra.ResourceAppId, Id: ra.Id?.ToString())))
                .ToHashSet();
            var missingPerms = EntraAppRegPermissionCatalog.All
                .Where(p => !currentPerms.Contains((p.ResourceAppId, p.PermissionId)))
                .Select(p => p.Name)
                .ToList();
            if (missingPerms.Count > 0)
            {
                driftReasons.Add($"missing delegated permission(s): {string.Join(", ", missingPerms)}");
            }

            var hasExposedScope = (app.Api?.Oauth2PermissionScopes ?? new List<PermissionScope>())
                .Any(s => string.Equals(s.Value, "user_impersonation", StringComparison.Ordinal));
            if (!hasExposedScope)
            {
                driftReasons.Add("missing exposed scope 'user_impersonation'");
            }

            if (driftReasons.Count > 0)
            {
                return new EntraAppRegSharedVerifyOutcome.Drifted(string.Join("; ", driftReasons));
            }

            return new EntraAppRegSharedVerifyOutcome.Current();
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "H3 Model 1 shared-app verification ODataError: appId={AppId} status={Status}",
                request.SharedAppId, ex.ResponseStatusCode);
            return new EntraAppRegSharedVerifyOutcome.Failure(
                $"Graph ODataError {ex.ResponseStatusCode} while verifying shared app-reg: " +
                $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message}");
        }
    }

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

    private async Task<Application?> FindByDisplayNameAsync(
        GraphServiceClient graph, string displayName, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);
        var filter = $"displayName eq '{EscapeODataLiteral(displayName)}'";
        var page = await graph.Applications.GetAsync(rc =>
        {
            rc.QueryParameters.Filter = filter;
        }, timeoutCts.Token).ConfigureAwait(false);
        return page?.Value?.FirstOrDefault();
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

        if (dirty)
        {
            await graph.Applications[app.Id].PatchAsync(patch, cancellationToken: timeoutCts.Token)
                .ConfigureAwait(false);
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

    private async Task EnsureServicePrincipalAsync(GraphServiceClient graph, string appId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);
        var existing = await graph.ServicePrincipals.GetAsync(rc =>
        {
            rc.QueryParameters.Filter = $"appId eq '{EscapeODataLiteral(appId)}'";
        }, timeoutCts.Token).ConfigureAwait(false);

        if (existing?.Value?.Count > 0)
        {
            return;
        }

        await graph.ServicePrincipals.PostAsync(new ServicePrincipal { AppId = appId },
            cancellationToken: timeoutCts.Token).ConfigureAwait(false);
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
    // FIC (Model 2 only, auth-v4 §3.1) — see file-header GOTCHA 2.
    // A42 (task 205b, FR-C4) hardening: cross-tenant refusal guard (SF-5),
    // triple-keyed idempotency (SF-7), exit-2-equivalent verification state
    // (SF-8). Parity contract:
    // projects/customer-provisioning-orchestration-r1/notes/decisions/
    // 205b-a42-fic-parity-contract.md
    // ---------------------------------------------------------------------

    /// <summary>
    /// Ensures the FIC exists with the correct issuer/subject, then performs
    /// an INDEPENDENT re-GET (never trusting the write call's own echoed
    /// response) to confirm it persisted exactly as requested — see
    /// GOTCHA 2 in the file header for why a literal OAuth2 exchange is not
    /// something L2 can legitimately perform here. Returns null on success
    /// (the caller reports <see cref="FicVerificationState.PendingPostAppServiceVerification"/>
    /// — the script exit-2 equivalent, NEVER terminal success per SF-8), or a
    /// Failure outcome to propagate. Throws
    /// <see cref="CrossTenantFicRefusedException"/> BEFORE any Graph call when
    /// the (app-reg tenant, UAMI tenant) pair is cross-tenant — the
    /// `Assert-SpaarkeFicTenancy` port (SF-5, A42).
    /// </summary>
    private async Task<EntraAppRegOutcome?> EnsureFederatedIdentityCredentialAsync(
        GraphServiceClient graph, string appObjectId, EntraAppRegRequest request, CancellationToken ct)
    {
        var issuerTenantId = ResolveUamiTenantId(request.Profile, request.TenantId, _options.SpaarkeTenantId);
        if (string.IsNullOrWhiteSpace(issuerTenantId))
        {
            return new EntraAppRegOutcome.Failure(
                $"Cannot compute FIC issuer — profile='{request.Profile}' requires " +
                $"{(string.Equals(request.Profile, "customer-owned-model2", StringComparison.OrdinalIgnoreCase) ? "request.TenantId" : "EntraAppReg:SpaarkeTenantId config")}, " +
                $"which is blank. [{EntraAppRegRejectionCodes.FicCreationFailed}]");
        }

        // A42 / SF-5: cross-tenant refusal FIRST, before any Graph FIC call —
        // the `Assert-SpaarkeFicTenancy` port. A cross-tenant pair would
        // CREATE successfully and fail only at token exchange, weeks later at
        // the customer's first OBO (see CrossTenantFicRefusedException header
        // + notes/decisions/adr-028-a4-integration-conflict-resolution.md
        // §9.2 contingency). Under §9.2 reading (a) — owner-ratified Q2,
        // 2026-08-25 — every sanctioned profile derives an intra-tenant pair,
        // so this guard is inert protection that only fires on genuine
        // misconfiguration (e.g. a spaarke-hosted profile dispatched with a
        // customer tenantId).
        AssertFicTenancy(request.TenantId, issuerTenantId, request.Profile);

        var issuer = $"https://login.microsoftonline.com/{issuerTenantId}/v2.0";

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.GraphRequestTimeout);

            var existing = await graph.Applications[appObjectId].FederatedIdentityCredentials
                .GetAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            // A42 / SF-7: idempotency is keyed by the (issuer, subject,
            // audience) TRIPLE — NEVER by name. Entra matches assertions
            // against the triple and enforces (issuer, subject) uniqueness
            // per application; a name-only check turns a correct no-op into a
            // failed run (the PS estate hit exactly this live on its first
            // run: a differently-named equivalent caused the create to be
            // rejected with "The combination of issuer and subject must be
            // unique for the application"). An equivalent triple under ANY
            // name already satisfies the request.
            var equivalent = FindEquivalentByTriple(
                existing?.Value, issuer, request.UamiPrincipalId, _options.FicAudience);

            if (equivalent is null)
            {
                // No equivalent triple exists. If OUR name is squatted by a
                // credential with a DIFFERENT triple, that is drift —
                // delete + recreate (subject/issuer/audience are immutable on
                // PATCH per Graph's documented FIC contract). This is the
                // provisioning-run equivalent of the script's explicit
                // -ForceFederatedCredentialUpdate reconcile path — a
                // DOCUMENTED divergence from the operator script's
                // refuse-without-Force default; see the parity contract §4.
                var nameCollision = existing?.Value?.FirstOrDefault(
                    f => string.Equals(f.Name, _options.FicName, StringComparison.Ordinal));
                if (nameCollision is not null)
                {
                    _logger.LogWarning(
                        "FIC '{FicName}' exists with a NON-matching (issuer, subject, audience) triple — " +
                        "drift; deleting + recreating (provisioning-run Force-equivalent, A42 parity contract §4). " +
                        "appObjectId={AppObjectId}", _options.FicName, appObjectId);
                    await graph.Applications[appObjectId].FederatedIdentityCredentials[nameCollision.Id]
                        .DeleteAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);
                }

                await graph.Applications[appObjectId].FederatedIdentityCredentials.PostAsync(new FederatedIdentityCredential
                {
                    Name = _options.FicName,
                    Issuer = issuer,
                    // §3.1 documented trap: subject MUST be the UAMI's
                    // principalId (object id), NOT its clientId — the
                    // wrong-subject FIC creates cleanly and dies at exchange
                    // with AADSTS700213 (auth-v4 §11 invariant 1).
                    Subject = request.UamiPrincipalId,
                    Audiences = new List<string> { _options.FicAudience },
                    Description = "Managed-identity trust for the stamp's BFF UAMI (auth-v4 §3.1 recipe; issuer tenant per profile) — created by H3 (task 130; A42 triple-idempotency).",
                }, cancellationToken: timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch (ODataError ex)
        {
            return new EntraAppRegOutcome.Failure(
                $"FIC creation failed for app object {appObjectId}: Graph ODataError {ex.ResponseStatusCode}: " +
                $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. [{EntraAppRegRejectionCodes.FicCreationFailed}]");
        }

        // Independent re-GET verification (GOTCHA 2) — retries a few times to
        // absorb read-after-write propagation lag, NOT AADSTS70025 (there is
        // no live OAuth2 exchange call here — see file header; the exchange-
        // side propagation policy lives in FicExchangeOutcomeClassifier for
        // the exchange-capable hosts). Verification matches by TRIPLE, not
        // name — a pre-existing equivalent under a different name is a
        // legitimate satisfied state (SF-7).
        for (var attempt = 1; attempt <= _options.FicExchangeRetryCount; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var verifyTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                verifyTimeoutCts.CancelAfter(_options.GraphRequestTimeout);
                var reGet = await graph.Applications[appObjectId].FederatedIdentityCredentials
                    .GetAsync(cancellationToken: verifyTimeoutCts.Token).ConfigureAwait(false);
                var confirmed = FindEquivalentByTriple(
                    reGet?.Value, issuer, request.UamiPrincipalId, _options.FicAudience);

                if (confirmed is not null)
                {
                    // Persisted + structurally verified. NOT exchange-verified
                    // (GOTCHA 2) — the caller reports the exit-2 equivalent
                    // (PendingPostAppServiceVerification), never terminal
                    // success (SF-8).
                    return null;
                }
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
            $"FIC re-GET verification did not confirm the (issuer, subject, audience) triple after " +
            $"{_options.FicExchangeRetryCount} attempts. [{EntraAppRegRejectionCodes.FicVerificationFailed}]");
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
