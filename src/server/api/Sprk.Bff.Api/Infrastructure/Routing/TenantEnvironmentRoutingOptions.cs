// teams-app-r1 Task 060 (2026-08-03) — BFF `tid`→environment routing (config model + result types).
//
// ADR-028 Amendment A2 sanctions the workforce collaboration plane (multitenant workforce Entra app,
// one registration). Per design.md §4 (D6/D7) + spec FR-09, hosting/data isolation across the three
// deployment models (Spaarke-hosted dedicated env / customer-hosted / true SaaS) is handled by THIS
// BFF `tid`→environment routing layer — NOT by the Entra registration.
//
// Mapping source (design §4 "config-driven" / POML step 2): a configuration-driven map of
// `tid` → environment, bound from the `TenantRouting` section (appsettings + Key Vault references in
// production, mirroring the existing `AzureAd` / `Ciam` / `Rag:ApiKey` config convention). A new
// customer tenant is onboarded by ADDING one `TenantRouting:Tenants[]` entry — ops/deployment owned.
//
// DENY-BY-DESIGN (project constraint / FR-09): there is NO default environment. The ONLY path to a
// resolved environment is exactly-one well-formed config entry keyed by the caller's own `tid`. An
// unmapped, missing, ambiguous, or malformed `tid` resolves to an explicit DENY with NO environment
// attached — a misroute is impossible by construction because the code contains no fallback branch
// that returns an environment for an unmatched tenant.

namespace Sprk.Bff.Api.Infrastructure.Routing;

/// <summary>
/// The three enterprise deployment models a workforce <c>tid</c> can be routed to (design.md §4 /
/// spec FR-09). <see cref="Unspecified"/> is the zero/sentinel value: a config entry that leaves the
/// model unset is treated as MALFORMED and DENIED — it never silently resolves to any environment.
/// </summary>
public enum TenantDeploymentModel
{
    /// <summary>
    /// Sentinel for an unset/unparsed model. A mapping with this value is malformed → deny.
    /// Deliberately the default(enum) so a config entry missing <c>DeploymentModel</c> cannot
    /// accidentally resolve.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Spaarke-hosted dedicated environment: a 1:1 <c>tid</c> → dedicated Dataverse/BFF environment.
    /// </summary>
    SpaarkeHostedDedicated = 1,

    /// <summary>
    /// Customer-hosted: the <c>tid</c> maps to the customer's OWN environment identifier.
    /// </summary>
    CustomerHosted = 2,

    /// <summary>
    /// True SaaS: the <c>tid</c> maps to a SHARED environment, with tenant-scoped data partitioning.
    /// A SaaS-shared mapping MUST carry <see cref="TenantEnvironmentMapping.TenantScoped"/> = true so
    /// downstream data access always applies the tenant partition (never a raw shared-env read).
    /// </summary>
    SaaSShared = 3
}

/// <summary>
/// One <c>tid</c> → environment mapping entry (bound from <c>TenantRouting:Tenants[]</c>).
/// Every field is validated at resolution time; any malformed entry DENIES rather than defaults.
/// </summary>
public sealed class TenantEnvironmentMapping
{
    /// <summary>
    /// The workforce Entra tenant id (<c>tid</c> claim). Matched case-insensitively against the
    /// authenticated principal's <c>tid</c>. Required; an empty value makes the entry malformed.
    /// </summary>
    public string Tid { get; set; } = string.Empty;

    /// <summary>
    /// Which deployment model this tenant is served under. Required (non-<see cref="TenantDeploymentModel.Unspecified"/>).
    /// </summary>
    public TenantDeploymentModel DeploymentModel { get; set; } = TenantDeploymentModel.Unspecified;

    /// <summary>
    /// The opaque target environment/connection identifier this <c>tid</c> resolves to (e.g. a
    /// Dataverse environment url/name or a named connection key). Required non-empty. This is an
    /// identifier only — the router never dereferences a credential; least-privilege connection
    /// selection is the downstream consumer's job, scoped to exactly this resolved value.
    /// </summary>
    public string EnvironmentId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the resolved environment requires tenant-scoping context on every downstream read.
    /// MUST be <c>true</c> for <see cref="TenantDeploymentModel.SaaSShared"/> (shared env) and
    /// <c>false</c> for the dedicated / customer-hosted models (their own env). A contradictory
    /// combination makes the entry malformed → deny.
    /// </summary>
    public bool TenantScoped { get; set; }
}

/// <summary>
/// Root options bound from the <c>TenantRouting</c> configuration section.
/// </summary>
public sealed class TenantEnvironmentRoutingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "TenantRouting";

    /// <summary>
    /// The configured <c>tid</c> → environment mappings. An EMPTY list means every request denies
    /// (fail-closed) — there is no implicit default environment.
    /// </summary>
    public List<TenantEnvironmentMapping> Tenants { get; set; } = new();
}

/// <summary>
/// The successful resolution outcome: exactly one environment for exactly one authenticated
/// <c>tid</c>. Placed on <c>HttpContext.Items</c> by the routing filter for downstream consumers.
/// </summary>
public sealed class ResolvedTenantEnvironment
{
    /// <summary>Key under which this is stored on <c>HttpContext.Items</c>.</summary>
    public static readonly object HttpContextItemsKey = new();

    /// <summary>The authenticated tenant id this environment was resolved for.</summary>
    public required string Tid { get; init; }

    /// <summary>The deployment model the tenant is served under.</summary>
    public required TenantDeploymentModel DeploymentModel { get; init; }

    /// <summary>The resolved target environment/connection identifier.</summary>
    public required string EnvironmentId { get; init; }

    /// <summary>Whether downstream reads must apply tenant-scoping (true only for SaaS-shared).</summary>
    public required bool TenantScoped { get; init; }
}

/// <summary>
/// Machine-readable reason a <c>tid</c> was denied a routed environment. Every value corresponds to
/// a stable deny code (<c>sdap.routing.deny.*</c>) surfaced in the ProblemDetails <c>reasonCode</c>.
/// </summary>
public enum TenantEnvironmentDenyReason
{
    /// <summary>No usable <c>tid</c> claim on the (already-validated) principal → 401-class.</summary>
    MissingTenantClaim = 0,

    /// <summary>The <c>tid</c> has no configured mapping → 403-class. Never a default env.</summary>
    UnmappedTenant = 1,

    /// <summary>The <c>tid</c> matched more than one mapping → 403-class. Deny, never best-guess.</summary>
    AmbiguousMapping = 2,

    /// <summary>The single matched mapping is malformed (missing env / model / bad scope) → 403-class.</summary>
    MalformedMapping = 3
}

/// <summary>
/// The result of <see cref="ITenantEnvironmentRouter.Resolve"/>: either a single resolved
/// environment, or an explicit deny with a machine-readable reason + code. There is deliberately NO
/// third "default" state.
/// </summary>
public sealed class TenantEnvironmentResolution
{
    private TenantEnvironmentResolution(
        bool isResolved,
        ResolvedTenantEnvironment? environment,
        TenantEnvironmentDenyReason? denyReason,
        string? denyCode)
    {
        IsResolved = isResolved;
        Environment = environment;
        DenyReason = denyReason;
        DenyCode = denyCode;
    }

    /// <summary>True only when a single environment was resolved.</summary>
    public bool IsResolved { get; }

    /// <summary>The resolved environment; non-null iff <see cref="IsResolved"/> is true.</summary>
    public ResolvedTenantEnvironment? Environment { get; }

    /// <summary>The deny reason; non-null iff <see cref="IsResolved"/> is false.</summary>
    public TenantEnvironmentDenyReason? DenyReason { get; }

    /// <summary>The stable deny code (<c>sdap.routing.deny.*</c>); non-null on deny.</summary>
    public string? DenyCode { get; }

    /// <summary>Build a resolved outcome.</summary>
    public static TenantEnvironmentResolution Resolved(ResolvedTenantEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return new TenantEnvironmentResolution(true, environment, null, null);
    }

    /// <summary>Build a deny outcome — NO environment is ever attached.</summary>
    public static TenantEnvironmentResolution Denied(TenantEnvironmentDenyReason reason, string code)
        => new(false, null, reason, code);
}
