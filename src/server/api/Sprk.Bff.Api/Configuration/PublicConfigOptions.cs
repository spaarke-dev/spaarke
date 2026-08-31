namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Tier-1 (fail-fast-on-start in deployed envs) public runtime configuration
/// surfaced to browser clients (external-spa + code-pages) via GET /api/config.
///
/// Introduced by customer-provisioning-orchestration-r1 task 087 per spec.md FR-36
/// + §7.9 close-pattern: today external-spa + code-pages consume env-config baked
/// at build time (npm run build produces different bundles per env). Every
/// env-config change requires re-build + re-deploy per surface. This options
/// class + the /api/config endpoint close that coupling — one build, N envs.
///
/// CANONICAL SHAPE (per POML task 087 acceptance criteria):
///   { bffUrl, msalClientId, tenantId, featureFlags }
///
/// SECURITY INVARIANT (spec.md §7.9 + POML constraint):
///   PublicConfigOptions MUST contain ONLY public values. Never:
///     - Client secrets, API keys, storage account keys
///     - Key Vault references (@Microsoft.KeyVault(...))
///     - ConnectionStrings entries
///     - Any credential of any kind
///   The endpoint is anonymous. If a secret leaks into this shape, it leaks to
///   the public internet.
///
/// TIER-1 VALIDATION (r3 task 061 pattern — custom IValidateOptions):
///   BffUrl / MsalClientId / TenantId are enforced by
///   <see cref="PublicConfigOptionsValidator"/> at startup in deployed envs.
///   In Development / Testing env the validator short-circuits (per
///   .claude/constraints/bff-extensions.md §F.2.1 Testing allow-list) so
///   the 30+ per-endpoint test fixtures don't each need to add
///   PublicConfig:* entries. The bare fields do NOT carry [Required]
///   DataAnnotations — the AgentServiceOptions pattern (task 061) demonstrates
///   why: bare [Required] evaluates eagerly on every option read and would
///   crash the Development / Testing boot path. The validator is the single
///   source of truth for requiredness semantics.
/// </summary>
public class PublicConfigOptions
{
    public const string SectionName = "PublicConfig";

    /// <summary>
    /// Public HTTPS URL of the BFF API — the origin browser clients call.
    /// Example: https://spaarke-bff-dev.azurewebsites.net
    /// Not a secret; already reachable without auth.
    /// Required in deployed envs (see <see cref="PublicConfigOptionsValidator"/>).
    /// </summary>
    public string BffUrl { get; set; } = string.Empty;

    /// <summary>
    /// Client ID (application ID) of the BFF's Entra app registration — used by
    /// MSAL public clients to acquire an SDAP.Access token via the OBO flow.
    /// Not a secret; MSAL client IDs are public per OAuth 2.0 spec.
    /// Required in deployed envs (see <see cref="PublicConfigOptionsValidator"/>).
    /// </summary>
    public string MsalClientId { get; set; } = string.Empty;

    /// <summary>
    /// Entra tenant ID (GUID) hosting the BFF app registration and the user
    /// directory browser clients authenticate against.
    /// Not a secret; tenant IDs are semi-public identifiers.
    /// Required in deployed envs (see <see cref="PublicConfigOptionsValidator"/>).
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Public feature flags surfaced to browser clients for runtime feature
    /// gating (e.g. { "featureA": true, "featureB": false }). Consumers read
    /// flags at bootstrap and decide whether to enable specific UI paths.
    ///
    /// Kept intentionally OPEN (Dictionary&lt;string,bool&gt;) so new flags can
    /// be added via config without a code change or app restart.
    ///
    /// MUST NOT be used to gate security-sensitive behavior (that lives on the
    /// server side). Client-visible flags are advisory; the server always
    /// enforces its own policies.
    /// </summary>
    public Dictionary<string, bool> FeatureFlags { get; set; } = new();
}
