// -----------------------------------------------------------------------------
// EntraAppRegOptions.cs
//
// Bound options for the H3 handler's collaborators (Graph SDK provisioner +
// real admin-consent verifier). Loaded from the "EntraAppReg" configuration
// section by Worker/Program.cs.
//
// TASK 130 (Wave G-3) REWRITE: replaces the Wave-C4 scaffold's
// PwshExecutable / RegisterEntraAppRegistrationsScriptPath / ExpectedAppRoleCount
// shape (shell-out era) with the Graph-SDK-port shape:
//   - ExpectedAppRoleCount -> ExpectedDelegatedScopeCount (RENAMED + re-scoped;
//     see file-header note in EntraAppRegPermissionCatalog.cs — this counts the
//     5 DELEGATED oauth2PermissionGrant scopes H3's own consent verifier checks,
//     NOT the 14 application-only app-roles H10 already owns).
//   - NEW Model 1 fields (SharedBffAppRegistrationId/Audience/KeyVaultName) —
//     the shared multitenant BFF app-reg already exists; H3's Model 1 branch
//     is a no-op for creation and instead verifies + references it.
//   - NEW Model 2 FIC fields (auth-v4 §3.1 recipe) — federated identity
//     credential trusting the shared BFF UAMI, per spec.md FR-39 / design.md
//     §4.1 H3 row v3.5 split.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Bound options for <see cref="H3EntraAppRegHandler"/> collaborators.
/// Configuration key: <c>EntraAppReg</c>.
/// </summary>
public sealed class EntraAppRegOptions
{
    /// <summary>
    /// Expected number of DELEGATED oauth2PermissionGrant scopes H3's real
    /// <see cref="IAdminConsentVerifier"/> checks for (per
    /// <see cref="EntraAppRegPermissionCatalog.All"/> — 5 as of task 130).
    /// Renamed from the Wave-C4 scaffold's <c>ExpectedAppRoleCount</c> (which
    /// conflated this with H10's 14-role app-only catalog — see
    /// EntraAppRegPermissionCatalog.cs file header for the scope correction).
    /// </summary>
    public int ExpectedDelegatedScopeCount { get; set; } = EntraAppRegPermissionCatalog.All.Count;

    /// <summary>
    /// Sign-in audience for a NEWLY CREATED Model 2 (dedicated) app-reg.
    /// Defaults to <c>AzureADMultipleOrgs</c> — matches
    /// scripts/Register-EntraAppRegistrations.ps1's existing constant (no
    /// behavior change from the pre-task-130 script default); operators can
    /// override per spec.md FR-06 v3 Model 2 consent semantics.
    /// </summary>
    public string RequiredSignInAudience { get; set; } = "AzureADMultipleOrgs";

    /// <summary>Client-secret lifetime (months) when a NEW secret is generated. Default 24 (parity with the retired script's <c>-SecretExpiryMonths</c>).</summary>
    public int SecretExpiryMonths { get; set; } = 24;

    /// <summary>Redirect URI path appended to the production API domain for a newly created app-reg's <c>web.redirectUris</c>.</summary>
    public string RedirectUriPath { get; set; } = "/.auth/login/aad/callback";

    // ---- Model 1 (shared multitenant BFF app-reg) ----

    /// <summary>
    /// The shared multitenant BFF app-registration's Entra <c>appId</c> (client
    /// id). Model 1's H3 branch is a NO-OP for app-reg creation — this is the
    /// pre-existing value H3 references instead of provisioning a new object
    /// (spec.md FR-39 + design.md §4.1 H3 row v3.5 split).
    /// </summary>
    public string? SharedBffAppRegistrationId { get; set; }

    /// <summary>Key Vault name where the shared app-reg's ClientSecret/ClientId/Audience already live (Model 1 KV-reference target — H3 does NOT write to this shared vault; it only references pre-existing entries).</summary>
    public string? SharedPlatformKeyVaultName { get; set; }

    // ---- Model 2 FIC (auth-v4 §3.1 recipe, spec.md FR-39) ----

    /// <summary>
    /// Federated identity credential audience. FIXED per auth-v4's §3.1
    /// recipe — the AAD token-exchange audience, not a per-tenant value.
    /// </summary>
    public string FicAudience { get; set; } = "api://AzureADTokenExchange";

    /// <summary>Deterministic FIC display name on the app-reg (idempotent re-run finds this by name).</summary>
    public string FicName { get; set; } = "spaarke-uami-trust";

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

    /// <summary>Timeout for a single Graph SDK call (create/patch/get). Graph is normally sub-second; generous ceiling for throttle/backoff.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout for a single KV SecretClient call (parity with KvSecretsPopulationOptions.KvOperationTimeout).</summary>
    public TimeSpan KvOperationTimeout { get; set; } = TimeSpan.FromSeconds(15);

    // ---- RETIRED (task 130) — referenced ONLY by the unregistered
    // RegisterEntraAppRegScriptProvisioner.cs (kept on disk, inert, per that
    // file's retirement banner). Kept here so the retired file still
    // compiles without resurrecting its shell-out code path. Do NOT wire
    // these into new code — see EntraAppRegOptions class header. ----

    /// <summary>RETIRED (task 130) — see class-header note. Path to the pwsh executable (shell-out era, unused by GraphAppRegistrationProvisioner).</summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>RETIRED (task 130) — see class-header note. Absolute path to the retired Register-EntraAppRegistrations.ps1 script.</summary>
    public string RegisterEntraAppRegistrationsScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Register-EntraAppRegistrations.ps1");

    /// <summary>RETIRED (task 130) — see class-header note. Max wait for the retired script invocation.</summary>
    public TimeSpan ProvisionTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
