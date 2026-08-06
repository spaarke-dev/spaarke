// teams-app-r1 Task 060 (2026-08-03) — BFF `tid`→environment router.
//
// Reads the `tid` claim from the ALREADY-VALIDATED workforce principal (HttpContext.User) — it does
// NOT re-validate the token (the workforce default JwtBearer scheme in AuthorizationModule already
// did that). It resolves the tenant to EXACTLY ONE configured environment, or DENIES.
//
// Deny-by-design (FR-09 / project constraint): the router has no fallback/default branch. The only
// code path that yields an environment is "exactly one well-formed mapping matched this tid". Every
// other case — missing tid, zero matches, >1 match, or a malformed single match — returns an explicit
// deny with NO environment attached. This makes a cross-tenant misroute impossible by construction.
//
// Broker-only / ADR-028 A2: reads claims only; no Graph SDK types, no AI-internal types, no OBO. The
// resolved value is an opaque environment IDENTIFIER; the router never selects or dereferences a
// credential (least-privilege — downstream reaches only the environment resolved for this tid).

using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Infrastructure.Routing;

/// <summary>
/// Resolves an authenticated workforce principal's <c>tid</c> claim to exactly one target
/// environment (per the three deployment models) or an explicit deny — never a default environment.
/// </summary>
/// <remarks>
/// The interface is a testing seam (ADR-010 — interface permitted when a testing seam is needed),
/// mirroring <c>IWorkforcePrincipalResolver</c>. Registered as a singleton: the mapping is deploy-time
/// static config, precomputed once into a case-insensitive lookup at construction.
/// </remarks>
public interface ITenantEnvironmentRouter
{
    /// <summary>
    /// Resolves the supplied validated principal's <c>tid</c> to a single environment, or denies.
    /// </summary>
    /// <param name="user">
    /// The <see cref="ClaimsPrincipal"/> from the already-validated workforce JWT
    /// (<c>HttpContext.User</c>). Only the <c>tid</c> claim is read.
    /// </param>
    TenantEnvironmentResolution Resolve(ClaimsPrincipal user);
}

/// <inheritdoc />
public sealed class TenantEnvironmentRouter : ITenantEnvironmentRouter
{
    // Deny codes (auth.md format: {domain}.{area}.{action}.{reason}).
    internal const string DenyMissingTenantClaim = "sdap.routing.deny.missing_tenant_claim";
    internal const string DenyUnmappedTenant = "sdap.routing.deny.unmapped_tenant";
    internal const string DenyAmbiguousMapping = "sdap.routing.deny.ambiguous_mapping";
    internal const string DenyMalformedMapping = "sdap.routing.deny.malformed_mapping";

    // Precomputed tid → all mappings that claim it (case-insensitive). >1 entry ⇒ ambiguous ⇒ deny.
    private readonly IReadOnlyDictionary<string, IReadOnlyList<TenantEnvironmentMapping>> _byTid;
    private readonly ILogger<TenantEnvironmentRouter> _logger;

    public TenantEnvironmentRouter(
        IOptions<TenantEnvironmentRoutingOptions> options,
        ILogger<TenantEnvironmentRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        // Group by tid WITHOUT collapsing duplicates — a duplicated tid is a config error that MUST
        // surface as an ambiguous deny at resolution time (never first-wins). Entries with a blank
        // tid are dropped from the lookup so they can never match a real caller (they will instead
        // fall through to the unmapped deny for whatever real tid arrives).
        var opts = options.Value ?? new TenantEnvironmentRoutingOptions();
        _byTid = (opts.Tenants ?? new List<TenantEnvironmentMapping>())
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Tid))
            .GroupBy(m => m.Tid.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TenantEnvironmentMapping>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);

        if (_byTid.Count == 0)
        {
            // Fail-closed posture is intentional; loud so an unconfigured deployment is diagnosable.
            _logger.LogWarning(
                "[TID-ROUTE] No TenantRouting mappings configured — ALL requests will be denied " +
                "(deny-by-default; no environment is served without an explicit tid mapping).");
        }
    }

    /// <inheritdoc />
    public TenantEnvironmentResolution Resolve(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        // ── 1. Extract tid from the already-validated principal (no re-validation) ──
        // Deliberately NOT reusing MembershipEndpoints.ExtractTenantId: its "anonymous" fallback
        // would turn a missing tid into a lookup key, which is exactly the silent-default footgun
        // this router exists to prevent. A missing/blank tid is an explicit deny here.
        var tid = user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrWhiteSpace(tid))
        {
            _logger.LogWarning("[TID-ROUTE] Denying request: no usable tid claim on principal.");
            return TenantEnvironmentResolution.Denied(
                TenantEnvironmentDenyReason.MissingTenantClaim, DenyMissingTenantClaim);
        }

        tid = tid.Trim();

        // ── 2. Look up the tid. NO match ⇒ deny (never a default environment). ──
        if (!_byTid.TryGetValue(tid, out var matches) || matches.Count == 0)
        {
            _logger.LogWarning(
                "[TID-ROUTE] Denying request: tid={Tid} has no configured environment mapping.", tid);
            return TenantEnvironmentResolution.Denied(
                TenantEnvironmentDenyReason.UnmappedTenant, DenyUnmappedTenant);
        }

        // ── 3. Ambiguous (>1 mapping for the same tid) ⇒ deny, never best-guess. ──
        if (matches.Count > 1)
        {
            _logger.LogError(
                "[TID-ROUTE] Denying request: tid={Tid} matches {Count} environment mappings — " +
                "ambiguous config, refusing to guess.", tid, matches.Count);
            return TenantEnvironmentResolution.Denied(
                TenantEnvironmentDenyReason.AmbiguousMapping, DenyAmbiguousMapping);
        }

        // ── 4. Exactly one match — validate it is well-formed before resolving. ──
        var mapping = matches[0];
        if (!IsWellFormed(mapping, out var reason))
        {
            _logger.LogError(
                "[TID-ROUTE] Denying request: tid={Tid} mapping is malformed ({Reason}).", tid, reason);
            return TenantEnvironmentResolution.Denied(
                TenantEnvironmentDenyReason.MalformedMapping, DenyMalformedMapping);
        }

        var resolved = new ResolvedTenantEnvironment
        {
            Tid = tid,
            DeploymentModel = mapping.DeploymentModel,
            EnvironmentId = mapping.EnvironmentId.Trim(),
            TenantScoped = mapping.TenantScoped
        };

        _logger.LogInformation(
            "[TID-ROUTE] Resolved tid={Tid} → environment={EnvironmentId} model={Model} tenantScoped={Scoped}.",
            tid, resolved.EnvironmentId, resolved.DeploymentModel, resolved.TenantScoped);

        return TenantEnvironmentResolution.Resolved(resolved);
    }

    /// <summary>
    /// A mapping is well-formed iff it names a non-empty environment, a concrete deployment model,
    /// and a tenant-scoping flag consistent with that model (SaaS-shared MUST be scoped; dedicated /
    /// customer-hosted MUST NOT be). Any inconsistency is a config error we DENY rather than default.
    /// </summary>
    private static bool IsWellFormed(TenantEnvironmentMapping mapping, out string reason)
    {
        if (string.IsNullOrWhiteSpace(mapping.EnvironmentId))
        {
            reason = "missing EnvironmentId";
            return false;
        }

        switch (mapping.DeploymentModel)
        {
            case TenantDeploymentModel.SaaSShared when !mapping.TenantScoped:
                reason = "SaaSShared mapping must set TenantScoped=true";
                return false;

            case TenantDeploymentModel.SpaarkeHostedDedicated when mapping.TenantScoped:
            case TenantDeploymentModel.CustomerHosted when mapping.TenantScoped:
                reason = "dedicated/customer-hosted mapping must set TenantScoped=false";
                return false;

            case TenantDeploymentModel.SpaarkeHostedDedicated:
            case TenantDeploymentModel.CustomerHosted:
            case TenantDeploymentModel.SaaSShared:
                reason = string.Empty;
                return true;

            case TenantDeploymentModel.Unspecified:
            default:
                reason = "DeploymentModel is Unspecified";
                return false;
        }
    }
}
