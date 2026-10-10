// -----------------------------------------------------------------------------
// EntraAppRegOptions.cs
//
// Bound options for the H3 handler's collaborators (Graph SDK provisioner +
// real admin-consent verifier). Loaded from the "EntraAppRegOptions" configuration
// section by Worker/Program.cs (nameof(EntraAppRegOptions); App Service form EntraAppRegOptions__*).
//
// TASK 130 (Wave G-3) REWRITE: replaces the Wave-C4 scaffold's
// PwshExecutable / RegisterEntraAppRegistrationsScriptPath / ExpectedAppRoleCount
// shape (shell-out era) with the Graph-SDK-port shape:
//   - ExpectedAppRoleCount -> ExpectedDelegatedScopeCount (RENAMED + re-scoped;
//     see file-header note in EntraAppRegPermissionCatalog.cs — this counts the
//     5 DELEGATED oauth2PermissionGrant scopes H3's own consent verifier checks,
//     NOT the 14 application-only app-roles H10 already owns).
//   - NEW Model 1 fields (SharedBffAppRegistrationId/Audience/KeyVaultName) —
//     RETIRED 2026-09-29 (task 222 per D-13). The shared-app-reg branch was
//     deleted from H3EntraAppRegHandler; H3 now provisions ONE app-reg per
//     customer, UNCONDITIONALLY, in both models. See H3EntraAppRegHandler.cs
//     TASK 222 REWRITE for the D-13 mechanism.
//   - NEW Model 2 FIC fields (auth-v4 §3.1 recipe) — federated identity
//     credential trusting the shared BFF UAMI, per spec.md FR-39 / design.md
//     §4.1 H3 row v3.5 split. (Superseded by D-12/D-13: the FIC trusts the
//     stamp's own BFF UAMI, in both models — there is no shared BFF UAMI.)
//   - T240a (2026-10-07): PreAuthorizedClientAppIds (shared M365 clients H3
//     pre-authorizes on each customer BFF app). The three shell-out-era fields
//     (PwshExecutable / RegisterEntraAppRegistrationsScriptPath / ProvisionTimeout)
//     were removed: the retired script provisioner they served is gone.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Bound options for <see cref="H3EntraAppRegHandler"/> collaborators.
/// Configuration key: <c>EntraAppRegOptions</c> (Worker/Program.cs binds <c>nameof(EntraAppRegOptions)</c>; the Worker
/// Bicep sets <c>EntraAppRegOptions__SpaarkeTenantId</c> and <c>EntraAppRegOptions__PreAuthorizedClientAppIds__N</c>).
/// </summary>
public sealed class EntraAppRegOptions
{
    /// <summary>
    /// Expected number of DELEGATED oauth2PermissionGrant scopes H3's real
    /// <see cref="IAdminConsentVerifier"/> checks for (per
    /// <see cref="EntraAppRegPermissionCatalog.All"/> — 5 as of task 130).
    /// Renamed from the Wave-C4 scaffold's <c>ExpectedAppRoleCount</c> (which
    /// conflated this with H10's app-only catalog — see
    /// EntraAppRegPermissionCatalog.cs file header for the scope correction).
    /// </summary>
    public int ExpectedDelegatedScopeCount { get; set; } = EntraAppRegPermissionCatalog.All.Count;

    /// <summary>
    /// Sign-in audience for a newly created per-customer BFF app-reg (both
    /// tenancy models post-task-222 per D-13). Defaults to
    /// <c>AzureADMultipleOrgs</c> — matches
    /// scripts/Register-EntraAppRegistrations.ps1's existing constant (no
    /// behavior change from the pre-task-130 script default); operators can
    /// override per spec.md FR-06 v3 consent semantics.
    /// </summary>
    public string RequiredSignInAudience { get; set; } = "AzureADMultipleOrgs";

    /// <summary>Client-secret lifetime (months) when a NEW secret is generated. Default 24 (parity with the retired script's <c>-SecretExpiryMonths</c>).</summary>
    public int SecretExpiryMonths { get; set; } = 24;

    /// <summary>Redirect URI path appended to the production API domain for a newly created app-reg's <c>web.redirectUris</c>.</summary>
    public string RedirectUriPath { get; set; } = "/.auth/login/aad/callback";

    // ---- FIC (auth-v4 §3.1 recipe, spec.md FR-39; both models post-task-222 per D-13) ----

    /// <summary>
    /// Federated identity credential audience. FIXED per auth-v4's §3.1
    /// recipe — the AAD token-exchange audience, not a per-tenant value.
    /// </summary>
    public string FicAudience { get; set; } = "api://AzureADTokenExchange";

    /// <summary>Deterministic FIC display name on the app-reg (idempotent re-run finds this by name).</summary>
    public string FicName { get; set; } = "spaarke-uami-trust";

    /// <summary>
    /// ISS-015: name of the SECOND federated identity credential H3 keeps on every Spaarke-tenant BFF registration — the
    /// one trusting the L2 Worker UAMI (subject = <see cref="ControlPlaneIdentityOptions.PrincipalObjectId"/>), so H6, H7
    /// and H7b can sign in as the registration secret-free (D-13; <c>WorkerDataverseCredentialFactory</c>). Must differ
    /// from <see cref="FicName"/>. Kept for the registration's lifetime: every re-run and upgrade signs in through it.
    /// </summary>
    public string WorkerFicName { get; set; } = "spaarke-l2-worker";

    /// <summary>
    /// Spaarke's own Entra tenant id — used to compute the FIC <c>issuer</c>
    /// for the <c>spaarke-hosted-model2</c> profile (UAMI lives in Spaarke's
    /// tenant/subscription). For <c>customer-owned-model2</c>, the issuer is
    /// the run's own <c>tenantId</c> parameter instead (UAMI lives in the
    /// customer's tenant/subscription) — see
    /// <see cref="GraphAppRegistrationProvisioner"/>'s issuer-selection logic.
    /// </summary>
    public string? SpaarkeTenantId { get; set; }

    /// <summary>
    /// Max attempts for the FIC's INDEPENDENT re-GET verification (a fresh
    /// GET of the just-written FIC object, confirming Subject/Issuer/Audiences
    /// persisted exactly as requested — NOT a live OAuth2 token exchange; see
    /// GraphAppRegistrationProvisioner.cs file-header GOTCHA 2 for why L2
    /// cannot perform a literal exchange using the customer's UAMI identity).
    /// Retries absorb Graph read-after-write propagation lag.
    /// </summary>
    public int FicExchangeRetryCount { get; set; } = 5;

    /// <summary>Delay between FIC re-GET verification retry attempts.</summary>
    public TimeSpan FicExchangeRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Attempts for H3's Entra propagation retries: the keyless-proof app-role assignment (task 230b; Entra answers 400
    /// while the role is not yet visible or 404 while the service principal is not yet replicated) and the client-access
    /// read of a just-created app (T240a; 404).
    /// </summary>
    public int RoleAssignmentRetryCount { get; set; } = 6;

    /// <summary>Delay between H3's Entra propagation retries (keyless-proof role assignment, client-access read).</summary>
    public TimeSpan RoleAssignmentRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Timeout for a single Graph SDK call (create/patch/get). Graph is normally sub-second; generous ceiling for throttle/backoff.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout for a single KV SecretClient call (parity with KvSecretsPopulationOptions.KvOperationTimeout).</summary>
    public TimeSpan KvOperationTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// T240a: the shared client apps (application ids) H3 pre-authorizes on every customer BFF app registration for
    /// its <c>user_impersonation</c> scope, so they get that BFF's token without a consent prompt — today the Office
    /// add-in; the Teams client when it exists (T240c). Platform-wide, never per customer; H3 sets exactly this list.
    /// Empty by default; the Worker Bicep supplies it. Each entry must be a GUID.
    /// </summary>
    public List<string> PreAuthorizedClientAppIds { get; set; } = [];
}
