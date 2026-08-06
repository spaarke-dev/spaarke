// teams-app-r1 Task 025 (2026-08-05) — Principal-agnostic collaboration caller resolution
// (R2 spec FR-22 · Option A).
//
// The reusable abstraction that makes the /api/v1/external/* collaboration endpoints serve BOTH
// auth planes through ONE endpoint set:
//
//   • CIAM external contact   (Entra External ID token, the "Ciam" scheme)     — the existing SPA
//   • Workforce user          (workforce Entra token, the default JwtBearer scheme) — the Teams host
//
// A handler asks the resolver for a single plane-agnostic <see cref="CallerPrincipal"/> — it never
// branches on if(ciam)…else…. The principal carries the caller's Tier-2 RECORD SCOPE (the set of
// projects the caller may access), composed per plane:
//
//   CIAM contact  → sprk_externalrecordaccess participations (the existing model, unchanged)
//   Workforce     → the common accessible-record-set (task 022): systemuser → ADR-034 membership;
//                   contact → grants ∪ standing-grant membership. NEVER "all projects" (R2 NFR-08).
//
// R2 (spec FR-22) lifts this resolver + the two strategies into its module framework as-is; a THIRD
// plane plugs in by adding one more <see cref="ICallerPrincipalStrategy"/> registration — no handler
// change. See projects/teams-app-r1/notes/teams-app-r1-coordination.md for the binding guardrails.
//
// Broker-only (ADR-028 A1/A2 · NFR-02): both strategies read the ALREADY-VALIDATED token's claims and
// query Dataverse APP-ONLY (via the existing participation / membership services). Neither exchanges
// the caller token downstream (no OBO) nor injects Graph SDK / AI-internal types.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Sprk.Bff.Api.Api.Membership;
using Sprk.Bff.Api.Infrastructure.Errors;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Which authentication plane a collaboration caller authenticated on. Selected by the token issuer
/// (CIAM authority vs workforce), NOT by inline handler branching.
/// </summary>
public enum CallerPrincipalPlane
{
    /// <summary>Entra External ID (CIAM) contact — the standalone external SPA.</summary>
    CiamContact,

    /// <summary>Workforce Entra user (systemuser or contact) — the Teams host (ADR-028 A2).</summary>
    Workforce
}

/// <summary>
/// A single project the caller may access, with the access level that governs effective rights.
/// For a CIAM contact the level is the granted <c>sprk_accesslevel</c>; for a workforce caller every
/// accessible project is surfaced at <see cref="ExternalAccessLevel.Collaborate"/> (see
/// <c>WorkforcePrincipalStrategy</c> for the rationale).
/// </summary>
public sealed class CallerProjectAccess
{
    public required Guid ProjectId { get; init; }
    public required ExternalAccessLevel AccessLevel { get; init; }
}

/// <summary>
/// The plane-agnostic collaboration caller consumed by every /api/v1/external handler. Unifies the
/// CIAM <see cref="ExternalCallerContext"/> and the workforce <see cref="WorkforcePrincipal"/> +
/// accessible-record-set into ONE shape carrying identity + the Tier-2 record scope. Set on
/// HttpContext.Items by the caller-principal authorization filter; there is no "unscoped" principal —
/// an unresolvable caller is denied by the strategy, never represented here.
/// </summary>
public sealed class CallerPrincipal
{
    /// <summary>HttpContext.Items key under which the resolved principal is stored.</summary>
    public static readonly object HttpContextItemsKey = new();

    /// <summary>The plane the caller authenticated on.</summary>
    public required CallerPrincipalPlane Plane { get; init; }

    /// <summary>The Dataverse contactid. For a CIAM caller this is the resolved external contact.
    /// For a workforce caller this is the anchor contact (contact-only) or the derived contact
    /// (systemuser) and MAY be <see cref="Guid.Empty"/> when a systemuser has no linked contact.</summary>
    public required Guid ContactId { get; init; }

    /// <summary>The Dataverse systemuserid; non-null only for a workforce systemuser principal.</summary>
    public Guid? SystemUserId { get; init; }

    /// <summary>The caller's email/UPN (from token claims). May be empty for an oid-resolved caller
    /// whose token carries no email claim.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>The stable directory object id (<c>oid</c>) the caller was resolved by. Null on a
    /// transitional email-only CIAM resolution.</summary>
    public string? Oid { get; init; }

    /// <summary>The caller's Tier-2 record scope: the projects they may access, with levels.</summary>
    public required IReadOnlyList<CallerProjectAccess> ProjectAccess { get; init; }

    /// <summary>All project ids the caller can access (for list construction).</summary>
    public IEnumerable<Guid> GetAccessibleProjectIds() => ProjectAccess.Select(p => p.ProjectId);

    /// <summary>Whether the caller can access the specified project (the record∈set check).</summary>
    public bool HasProjectAccess(Guid projectId) => ProjectAccess.Any(p => p.ProjectId == projectId);

    /// <summary>The access level for the specified project, or null if the caller has no access.</summary>
    public ExternalAccessLevel? GetAccessLevel(Guid projectId) =>
        ProjectAccess.FirstOrDefault(p => p.ProjectId == projectId)?.AccessLevel;

    /// <summary>The effective <see cref="AccessRights"/> for the project (same mapping the CIAM
    /// <see cref="ExternalCallerContext"/> used, so CIAM behavior is unchanged).</summary>
    public AccessRights GetEffectiveRights(Guid projectId)
    {
        var level = GetAccessLevel(projectId);
        return level switch
        {
            ExternalAccessLevel.ViewOnly => AccessRights.Read,
            ExternalAccessLevel.Collaborate => AccessRights.Read | AccessRights.Create | AccessRights.Write,
            ExternalAccessLevel.FullAccess => AccessRights.Read | AccessRights.Create | AccessRights.Write | AccessRights.Delete,
            _ => AccessRights.None
        };
    }
}

/// <summary>
/// The outcome of resolving a request to a <see cref="CallerPrincipal"/>: exactly one of a resolved
/// principal or a failure <see cref="IResult"/> (401/403 ProblemDetails) to short-circuit the request.
/// </summary>
public sealed class CallerPrincipalResolution
{
    /// <summary>The resolved principal on success; null on failure.</summary>
    public CallerPrincipal? Principal { get; private init; }

    /// <summary>The short-circuit ProblemDetails result on failure; null on success.</summary>
    public IResult? Failure { get; private init; }

    /// <summary>True when a principal was resolved.</summary>
    public bool IsResolved => Principal is not null;

    /// <summary>Constructs a resolved outcome.</summary>
    public static CallerPrincipalResolution Resolved(CallerPrincipal principal)
        => new() { Principal = principal ?? throw new ArgumentNullException(nameof(principal)) };

    /// <summary>Constructs a failure outcome carrying the ProblemDetails result to return.</summary>
    public static CallerPrincipalResolution Denied(IResult failure)
        => new() { Failure = failure ?? throw new ArgumentNullException(nameof(failure)) };
}

/// <summary>
/// One plane's resolution strategy. R2's "add a third plane = add one registration" seam: the resolver
/// selects the strategy whose <see cref="Plane"/> matches the request, so a new plane is purely
/// additive.
/// </summary>
public interface ICallerPrincipalStrategy
{
    /// <summary>The plane this strategy resolves.</summary>
    CallerPrincipalPlane Plane { get; }

    /// <summary>Resolves the (already-authenticated) request to a principal or a failure result.</summary>
    Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct);
}

/// <summary>
/// Resolves an authenticated collaboration request to a plane-agnostic <see cref="CallerPrincipal"/>.
/// </summary>
public interface ICallerPrincipalResolver
{
    /// <summary>Resolves the request: selects the plane by token issuer, delegates to that strategy.</summary>
    Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct);
}

/// <inheritdoc />
public sealed class CallerPrincipalResolver : ICallerPrincipalResolver
{
    private readonly IReadOnlyDictionary<CallerPrincipalPlane, ICallerPrincipalStrategy> _strategies;
    private readonly string? _ciamTenantId;
    private readonly ILogger<CallerPrincipalResolver> _logger;

    public CallerPrincipalResolver(
        IEnumerable<ICallerPrincipalStrategy> strategies,
        IConfiguration configuration,
        ILogger<CallerPrincipalResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _strategies = strategies.ToDictionary(s => s.Plane);
        _ciamTenantId = configuration.GetSection("Ciam")["TenantId"];
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var plane = DeterminePlane(httpContext.User);
        if (!_strategies.TryGetValue(plane, out var strategy))
        {
            // No strategy registered for the detected plane — fail closed rather than serve unscoped.
            _logger.LogError(
                "[CALLER] No caller-principal strategy registered for plane {Plane}; denying (fail-closed).",
                plane);
            return Task.FromResult(CallerPrincipalResolution.Denied(
                Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "The authenticated caller could not be resolved to a collaboration principal.",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1")));
        }

        return strategy.ResolveAsync(httpContext, ct);
    }

    /// <summary>
    /// Selects the plane by token issuer: CIAM iff the issuer is a <c>*.ciamlogin.com</c> authority OR
    /// the tenant (<c>tid</c>) equals the configured CIAM tenant; otherwise workforce. Deterministic —
    /// a token validates against exactly one authority (the multi-scheme policy authenticated whichever
    /// one matched), so exactly one plane applies.
    /// </summary>
    internal CallerPrincipalPlane DeterminePlane(ClaimsPrincipal user)
    {
        var issuer = user.FindFirst("iss")?.Value;
        if (!string.IsNullOrEmpty(issuer) &&
            issuer.Contains("ciamlogin.com", StringComparison.OrdinalIgnoreCase))
        {
            return CallerPrincipalPlane.CiamContact;
        }

        if (!string.IsNullOrEmpty(_ciamTenantId))
        {
            var tid = MembershipEndpoints.ExtractTenantId(user);
            if (!string.IsNullOrEmpty(tid) &&
                string.Equals(tid, _ciamTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return CallerPrincipalPlane.CiamContact;
            }
        }

        return CallerPrincipalPlane.Workforce;
    }
}

/// <summary>
/// CIAM contact strategy — the EXISTING external-SPA resolution, unchanged (ADR-028 A1). Resolves the
/// Dataverse contact by stable <c>oid</c> (email as first-login fallback) and loads its
/// sprk_externalrecordaccess participations. Reproduces <c>ExternalCallerAuthorizationFilter</c>'s
/// deny semantics byte-for-byte (R1 FR-15 / R2 guardrail #3).
/// </summary>
public sealed class CiamContactPrincipalStrategy : ICallerPrincipalStrategy
{
    private readonly ExternalParticipationService _participations;
    private readonly ILogger<CiamContactPrincipalStrategy> _logger;

    public CiamContactPrincipalStrategy(
        ExternalParticipationService participations,
        ILogger<CiamContactPrincipalStrategy> logger)
    {
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public CallerPrincipalPlane Plane => CallerPrincipalPlane.CiamContact;

    /// <inheritdoc />
    public async Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct)
    {
        var user = httpContext.User;

        // Stable identity: the CIAM 'oid' is the canonical link to Contact.sprk_externalobjectid
        // (ADR-028 A1). Email/UPN is a first-login fallback that then binds the oid.
        var oid = user.FindFirstValue("oid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
        var email = user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue("upn")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email");

        if (string.IsNullOrEmpty(oid) && string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("[EXT-AUTH] CIAM token missing both oid and email/UPN identity claims");
            return CallerPrincipalResolution.Denied(Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Identity token is missing required user identity claims",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1"));
        }

        var contactId = await _participations
            .ResolveExternalContactAsync(oid, email, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!contactId.HasValue)
        {
            _logger.LogWarning(
                "[EXT-AUTH] Cannot resolve Dataverse Contact (oid present: {HasOid}, email present: {HasEmail})",
                !string.IsNullOrEmpty(oid), !string.IsNullOrEmpty(email));
            return CallerPrincipalResolution.Denied(
                ProblemDetailsHelper.Forbidden("sdap.access.deny.contact_not_found"));
        }

        var participations = await _participations
            .GetParticipationsAsync(contactId.Value, httpContext.RequestAborted)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "[EXT-AUTH] Contact {ContactId} authenticated (oid-resolved: {ByOid}) — {Count} active project participations",
            contactId.Value, !string.IsNullOrEmpty(oid), participations.Count);

        var projectAccess = participations
            .Select(p => new CallerProjectAccess { ProjectId = p.ProjectId, AccessLevel = p.AccessLevel })
            .ToList();

        return CallerPrincipalResolution.Resolved(new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = contactId.Value,
            SystemUserId = null,
            Email = email ?? string.Empty,
            Oid = oid,
            ProjectAccess = projectAccess
        });
    }
}

/// <summary>
/// Workforce strategy — wraps task 020's <see cref="IWorkforcePrincipalResolver"/> and task 022's
/// <see cref="IAccessibleRecordSetService"/> to produce the Tier-2 record scope (R2 NFR-08). A
/// workforce caller sees ONLY the projects in its composed accessible set (systemuser → ADR-034
/// membership; contact → grants ∪ standing-grant membership) — NEVER all projects. Reproduces
/// <c>WorkforceCallerAuthorizationFilter</c>'s deny semantics.
/// </summary>
public sealed class WorkforcePrincipalStrategy : ICallerPrincipalStrategy
{
    // Every project in a workforce caller's accessible set is surfaced at Collaborate
    // (Read|Create|Write, no Delete). Rationale: the workforce plane is a full collaboration
    // workspace — a systemuser is internal staff (ADR-034 membership) and an accessible-set contact
    // is a deliberately-granted collaborator. The accessible set itself is the record-scope (NFR-08)
    // boundary; the level governs within-project rights only. R2's F3/F5 admin layer may later refine
    // per-project levels. Documented decision — see notes/r2-coordination-response.md.
    internal const ExternalAccessLevel WorkforceProjectAccessLevel = ExternalAccessLevel.Collaborate;

    /// <summary>Grants are project-scoped in R1; record scope for the collaboration surface is projects.</summary>
    internal const string ProjectEntity = "sprk_project";

    private readonly IWorkforcePrincipalResolver _resolver;
    private readonly IAccessibleRecordSetService _accessibleSet;
    private readonly ILogger<WorkforcePrincipalStrategy> _logger;

    public WorkforcePrincipalStrategy(
        IWorkforcePrincipalResolver resolver,
        IAccessibleRecordSetService accessibleSet,
        ILogger<WorkforcePrincipalStrategy> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _accessibleSet = accessibleSet ?? throw new ArgumentNullException(nameof(accessibleSet));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public CallerPrincipalPlane Plane => CallerPrincipalPlane.Workforce;

    /// <inheritdoc />
    public async Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct)
    {
        var resolution = await _resolver
            .ResolveAsync(httpContext.User, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!resolution.IsResolved)
        {
            // Same deny mapping as WorkforceCallerAuthorizationFilter (task 020).
            switch (resolution.DenyReason)
            {
                case WorkforceDenyReason.MissingIdentityClaims:
                    _logger.LogWarning("[WF-AUTH] Denying request: {DenyCode}", resolution.DenyCode);
                    return CallerPrincipalResolution.Denied(Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Unauthorized",
                        detail: "Identity token is missing required user identity claims",
                        type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                        extensions: new Dictionary<string, object?> { ["reasonCode"] = resolution.DenyCode }));

                case WorkforceDenyReason.PrincipalNotResolved:
                default:
                    _logger.LogWarning("[WF-AUTH] Denying request: {DenyCode}", resolution.DenyCode);
                    return CallerPrincipalResolution.Denied(ProblemDetailsHelper.Forbidden(
                        resolution.DenyCode ?? WorkforcePrincipalResolver.DenyPrincipalNotResolved));
            }
        }

        var principal = resolution.Principal!;

        // Compose the Tier-2 record scope (task 022) — the SAME common accessible-record-set the
        // workforce download gate uses. This is what prevents a workforce caller from seeing all
        // projects merely by authenticating (R2 NFR-08).
        var accessibleProjects = await _accessibleSet
            .ComposeAsync(principal, ProjectEntity, httpContext.RequestAborted)
            .ConfigureAwait(false);

        var projectAccess = accessibleProjects.RecordIds
            .Select(id => new CallerProjectAccess { ProjectId = id, AccessLevel = WorkforceProjectAccessLevel })
            .ToList();

        _logger.LogInformation(
            "[WF-AUTH] Workforce {Kind} (systemuser={SystemUserId}, contact={ContactId}) resolved with " +
            "{Count} accessible projects (sources: {Sources}).",
            principal.Kind, principal.SystemUserId, principal.ContactId, projectAccess.Count,
            accessibleProjects.Sources);

        return CallerPrincipalResolution.Resolved(new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = principal.ContactId ?? Guid.Empty,
            SystemUserId = principal.SystemUserId,
            Email = WorkforcePrincipalResolver.ExtractVerifiedEmail(httpContext.User) ?? string.Empty,
            Oid = principal.Oid,
            ProjectAccess = projectAccess
        });
    }
}
