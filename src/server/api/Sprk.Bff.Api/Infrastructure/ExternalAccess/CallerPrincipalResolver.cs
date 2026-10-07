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
//   CIAM contact  → the common accessible-record-set, contact plane, explicit grants only
//                   (unified-access-control-r2 task 135: the same grant term + veto pipeline as below)
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
/// A single project the caller may access, with the <see cref="AccessRights"/> that govern what they
/// may do on it, taken from the ONE evaluator's <c>(recordId → rights)</c> answer on both planes
/// (unified-access-control-r2 task 033 / FR-19 for the shape): the workforce plane through
/// <see cref="IAccessibleRecordSetService.ComposeAsync"/>, the CIAM plane through
/// <see cref="IAccessibleRecordSetService.ComposeForCiamContactAsync"/>, which share one contact-plane
/// composition and one veto pipeline.
/// </summary>
/// <remarks>
/// ⚠️ Until task 135 (defect C1, 2026-10-01) this summary claimed both planes already sourced these from the
/// evaluator. Only the workforce plane did: the CIAM strategy built them from the grant rows directly, so no
/// Restricted, Secure or No Access rule ran on a CIAM token.
/// </remarks>
/// <remarks>
/// <b>Rights are stored; the level is derived.</b> This class used to hold an
/// <see cref="ExternalAccessLevel"/> and map it to rights on demand. That direction cannot represent
/// the evaluator's answer: rights compose by highest-wins union across terms, and the union of two
/// terms need not land on one of the three level constants. Storing the level and re-deriving rights
/// would round-trip the evaluator's answer through a coarser type — the exact flattening FR-19 exists
/// to remove.
/// <para>
/// <see cref="AccessLevel"/> remains available as a lossy DISPLAY projection (see
/// <see cref="ExternalAccessLevels.ToDisplayLevel"/>) for <c>/api/v1/external/me</c>, whose response
/// contract is a level string. Never authorize on it.
/// </para>
/// </remarks>
public sealed class CallerProjectAccess
{
    public required Guid ProjectId { get; init; }

    /// <summary>What the caller may do on this project — the authorization value.</summary>
    public required AccessRights Rights { get; init; }

    /// <summary>
    /// The coarse level to display for <see cref="Rights"/>, or null when the caller holds nothing.
    /// DISPLAY ONLY — see the class remarks.
    /// </summary>
    public ExternalAccessLevel? AccessLevel => ExternalAccessLevels.ToDisplayLevel(Rights);

    /// <summary>
    /// Builds an entry from a level. The single conversion point for level-sourced construction, so the
    /// mapping table stays in <see cref="ExternalAccessLevels"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ No authorization path may build a principal from grant rows with this: a grant's level is not its
    /// effective rights until the evaluator has applied Secure suppression and the vetoes (task 135 removed
    /// the CIAM strategy's use of it for exactly that reason). Test fixtures that state a principal directly
    /// are its remaining callers.
    /// </remarks>
    public static CallerProjectAccess FromLevel(Guid projectId, ExternalAccessLevel? level) =>
        new() { ProjectId = projectId, Rights = ExternalAccessLevels.ToAccessRights(level) };
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

    /// <summary>
    /// The caller's Tier-2 record scope: the projects they may access, with rights. Both strategies put only
    /// Read-bearing entries here (task 136); readers that list or gate should still use
    /// <see cref="ReadableProjects"/> / <see cref="HasProjectAccess"/>, which require Read on their own.
    /// </summary>
    public required IReadOnlyList<CallerProjectAccess> ProjectAccess { get; init; }

    /// <summary>
    /// The caller's accessible MATTER roots as <c>(recordId → rights)</c> (task 028 shape, upgraded from
    /// a bare id set by task 033 / FR-19). Composed per plane (CIAM: matter grants; workforce: membership
    /// ∪ matter grants). Empty when the caller has no accessible matters — never "all matters" (NFR-08).
    /// </summary>
    public IReadOnlyDictionary<Guid, AccessRights> MatterAccess { get; init; } = EmptyRights;

    /// <summary>
    /// The caller's accessible WORK-ASSIGNMENT roots as <c>(recordId → rights)</c> (task 033). A work
    /// assignment is a first-class root: a standalone WA (no project/matter) can be granted to outside
    /// counsel and carry its own documents. Empty when the caller has none (NFR-08).
    /// </summary>
    public IReadOnlyDictionary<Guid, AccessRights> WorkAssignmentAccess { get; init; } = EmptyRights;

    private static readonly IReadOnlyDictionary<Guid, AccessRights> EmptyRights =
        new Dictionary<Guid, AccessRights>();

    /// <summary>
    /// The root entity types (LOGICAL names, e.g. <c>sprk_project</c>) on which an UNDATED access term ran for this
    /// caller — standing-grant membership or organization expansion (<see cref="AccessibleRecordSetSources"/>), which
    /// confer access with no expiry date (unified-access-control-r2 task 140). Empty when every right the caller holds
    /// comes from dated grant rows — always so for a CIAM contact, whose composition has no derived-member terms (owner
    /// A2). Provenance, not authorization: it tells the contact-side grant route whether a grantor's level may rest on
    /// something with no date, which decides whether their own grant's expiry caps the one they issue (owner G2 (i)).
    /// </summary>
    public IReadOnlySet<string> UndatedAccessTermEntityTypes { get; init; } = EmptyEntityTypes;

    private static readonly IReadOnlySet<string> EmptyEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlySet<Guid>? _matterIds;
    private IReadOnlySet<Guid>? _workAssignmentIds;

    // ── Every read view below requires Read (unified-access-control-r2 task 136 · defect C2) ─────────────
    //
    // These views used to be key views: "the id is in the map". Two live paths put ids in the map with no
    // rights — a Secure root reached only through an organization grant, and a matter or work-assignment grant
    // row with no level — and every /api/v1/external read route, the module /fetch and /record scope, and /me
    // treated presence as authorization. Both strategies now drop such entries when they build the principal
    // (FromReadBearing), and the views ALSO require Read: two independent layers, so a future construction
    // path that forgets to prune still fails closed here.
    //
    // ⚠️ Test HasFlag(AccessRights.Read), never HasFlag(None): AccessRights is [Flags], so HasFlag(None) is
    // true for every value, including None itself.

    /// <summary>
    /// Matter ids the caller may READ. A DERIVED VIEW over <see cref="MatterAccess"/> (task 033) — not a second
    /// stored collection, so ids and rights cannot disagree. Read-scope injection (<c>Tier2ScopeFilterInjector</c>,
    /// the module <c>ScopeDimension</c>s) consumes this shape unchanged; since task 136 an entry without Read is
    /// not in it.
    /// </summary>
    public IReadOnlySet<Guid> AccessibleMatterIds => _matterIds ??= ReadableIdsOf(MatterAccess);

    /// <summary>Work-assignment ids the caller may READ — a DERIVED VIEW over <see cref="WorkAssignmentAccess"/>.</summary>
    public IReadOnlySet<Guid> AccessibleWorkAssignmentIds =>
        _workAssignmentIds ??= ReadableIdsOf(WorkAssignmentAccess);

    /// <summary>
    /// The projects the caller may READ, with their rights — the entries /me lists and the project list returns.
    /// An entry on <see cref="ProjectAccess"/> without Read is not one of them (task 136).
    /// </summary>
    public IEnumerable<CallerProjectAccess> ReadableProjects =>
        ProjectAccess.Where(p => p.Rights.HasFlag(AccessRights.Read));

    /// <summary>All project ids the caller can READ (for list construction).</summary>
    public IEnumerable<Guid> GetAccessibleProjectIds() => ReadableProjects.Select(p => p.ProjectId);

    /// <summary>All matter ids the caller can READ (task 028; Read-gated by task 136).</summary>
    public IReadOnlySet<Guid> GetAccessibleMatterIds() => AccessibleMatterIds;

    /// <summary>All work-assignment ids the caller can READ (task 028; Read-gated by task 136).</summary>
    public IReadOnlySet<Guid> GetAccessibleWorkAssignmentIds() => AccessibleWorkAssignmentIds;

    /// <summary>
    /// Whether the caller holds <see cref="AccessRights.Read"/> on the specified project. Until task 136 this was
    /// "the id is in <see cref="ProjectAccess"/>", which a None-rights entry satisfied.
    /// </summary>
    public bool HasProjectAccess(Guid projectId) => GetEffectiveRights(projectId).HasFlag(AccessRights.Read);

    /// <summary>
    /// The entries of an evaluator answer that carry <see cref="AccessRights.Read"/> — the construction-time
    /// half of task 136's two layers. Both plane strategies build <see cref="ProjectAccess"/>,
    /// <see cref="MatterAccess"/> and <see cref="WorkAssignmentAccess"/> through this, so neither can admit a
    /// record the caller holds nothing on, whatever the evaluator returned.
    /// </summary>
    public static IReadOnlyDictionary<Guid, AccessRights> FromReadBearing(IReadOnlyDictionary<Guid, AccessRights> rights)
    {
        ArgumentNullException.ThrowIfNull(rights);
        return rights
            .Where(kvp => kvp.Value.HasFlag(AccessRights.Read))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static IReadOnlySet<Guid> ReadableIdsOf(IReadOnlyDictionary<Guid, AccessRights> rights) =>
        rights.Where(kvp => kvp.Value.HasFlag(AccessRights.Read)).Select(kvp => kvp.Key).ToHashSet();

    /// <summary>
    /// The DISPLAY level for the specified project, or null if the caller has no access.
    /// ⚠️ Display only — authorize on <see cref="GetEffectiveRights(Guid)"/>.
    /// </summary>
    public ExternalAccessLevel? GetAccessLevel(Guid projectId) =>
        ProjectAccess.FirstOrDefault(p => p.ProjectId == projectId)?.AccessLevel;

    /// <summary>
    /// The effective <see cref="AccessRights"/> on a PROJECT. Fail-closed: a project the caller cannot
    /// access yields <see cref="AccessRights.None"/>.
    /// </summary>
    /// <remarks>
    /// Task 033 made this a direct read of the stored rights. It previously re-derived them from a
    /// coarse level, which is why the workforce plane's blanket Collaborate stamp could overwrite a
    /// deliberate ViewOnly grant (register A-8).
    /// </remarks>
    public AccessRights GetEffectiveRights(Guid projectId) =>
        ProjectAccess.FirstOrDefault(p => p.ProjectId == projectId)?.Rights ?? AccessRights.None;

    /// <summary>
    /// The effective <see cref="AccessRights"/> on a MATTER (task 033). Fail-closed: absence is
    /// <see cref="AccessRights.None"/>, never a default grant.
    /// </summary>
    public AccessRights GetMatterRights(Guid matterId) =>
        MatterAccess.TryGetValue(matterId, out var rights) ? rights : AccessRights.None;

    /// <summary>
    /// The effective <see cref="AccessRights"/> on a WORK ASSIGNMENT (task 033). Fail-closed.
    /// </summary>
    public AccessRights GetWorkAssignmentRights(Guid workAssignmentId) =>
        WorkAssignmentAccess.TryGetValue(workAssignmentId, out var rights) ? rights : AccessRights.None;

    /// <summary>
    /// The effective <see cref="AccessRights"/> on a grant root of any type (unified-access-control-r2 task 140) — the
    /// one dispatch over <see cref="GetEffectiveRights"/>, <see cref="GetMatterRights"/> and
    /// <see cref="GetWorkAssignmentRights"/>. Fail-closed: a root the caller cannot reach is <see cref="AccessRights.None"/>,
    /// so "out of scope" and "insufficient rights" are one answer.
    /// </summary>
    internal AccessRights RightsOn(ExternalGrantRootType rootType, Guid rootId) => rootType switch
    {
        ExternalGrantRootType.Project => GetEffectiveRights(rootId),
        ExternalGrantRootType.Matter => GetMatterRights(rootId),
        ExternalGrantRootType.WorkAssignment => GetWorkAssignmentRights(rootId),
        _ => AccessRights.None,
    };

    /// <summary>
    /// Whether the caller is a CONTACT principal (unified-access-control-r2 task 140): a CIAM contact, or a workforce
    /// caller that resolved to a contact only. The principal KIND decides — a workforce SYSTEMUSER carries
    /// <see cref="SystemUserId"/> and is never a contact principal, even when it also carries a linked contact id (task
    /// 141's user↔contact link). Not a plane test: both planes produce contact principals (ADR-028 A3).
    /// </summary>
    public bool IsContactPrincipal => SystemUserId is null && ContactId != Guid.Empty;
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
/// CIAM contact strategy — the external SPA's caller (ADR-028 A1). Resolves the Dataverse contact by the
/// stable <c>oid</c> bound to <c>contact.sprk_externalobjectid</c> (task 141), then takes the Tier-2 record
/// scope from the unified evaluator (<see cref="IAccessibleRecordSetService.ComposeForCiamContactAsync"/>), so
/// the Restricted veto (FR-21), Secure direct-only suppression (FR-22) and the No Access List (FR-23) apply
/// to a ciamlogin.com token exactly as they do to the same contact on a workforce token (task 135 · defect C1).
/// The email bind survives only as the repair path for an invite whose oid write failed; the CIAM plane never
/// creates a contact (task 141).
/// </summary>
/// <remarks>
/// <para>Task 141: resolution moved from <c>ExternalParticipationService</c> (the grant-data reader) to
/// <see cref="ContactIdentityBinder"/>, which runs the one binding decision both planes share. Each deny now
/// carries the decision's OWN code — ambiguity, collision, unreadable binding, inactive contact — instead of
/// one <c>contact_not_found</c> for all of them, so the audit trail can tell an outage from a hijack attempt.</para>
/// <para>Task 135: before it this strategy built the principal from <c>ExternalParticipationService.GetGrantSetAsync</c>
/// alone, on each grant's all-sources level: none of the three rules ran, so a contact on the No Access
/// List, holding a grant on a Restricted record, or inheriting an organization grant on a Secure record kept
/// full access here. The strategy now holds no authorization logic of its own — it maps the evaluator's
/// <c>(recordId → rights)</c> answer onto the principal, the same way <see cref="WorkforcePrincipalStrategy"/>
/// does. An evaluator fault propagates (the request fails); there is no grants-only fallback.</para>
/// </remarks>
public sealed class CiamContactPrincipalStrategy : ICallerPrincipalStrategy
{
    private readonly ContactIdentityBinder _binder;
    private readonly IAccessibleRecordSetService _accessibleSet;
    private readonly ILogger<CiamContactPrincipalStrategy> _logger;

    public CiamContactPrincipalStrategy(
        ContactIdentityBinder binder,
        IAccessibleRecordSetService accessibleSet,
        ILogger<CiamContactPrincipalStrategy> logger)
    {
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _accessibleSet = accessibleSet ?? throw new ArgumentNullException(nameof(accessibleSet));
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

        var binding = await _binder
            .ResolveCiamCallerAsync(oid, email, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!binding.IsResolved)
        {
            var code = binding.DenyCode ?? ContactBindingDecision.DenyContactNotFound;
            _logger.LogWarning(
                "[EXT-AUTH] Cannot resolve Dataverse Contact ({DenyCode}; oid present: {HasOid}, email present: {HasEmail})",
                code, !string.IsNullOrEmpty(oid), !string.IsNullOrEmpty(email));
            return CallerPrincipalResolution.Denied(ProblemDetailsHelper.Forbidden(code, CiamDenyDetail(code)));
        }

        var contactId = binding.ContactId!.Value;

        // Outside-counsel access is GRANT-ONLY and explicit (task 028 / design §2): the caller sees the
        // roots granted via sprk_externalrecordaccess — projects, matters and work assignments, each at
        // its granted level — and NO membership/assignment/rollup-derived access.
        //
        // Task 135 (C1): that grant term, and the three rules that narrow it, come from the ONE contact-
        // plane composition the workforce contact also uses. Nothing here reads a grant's level: the
        // evaluator decides which level counts (DirectAccessLevel on a Secure root) and which records
        // survive (deny list, then Restricted).
        var reqCt = httpContext.RequestAborted;
        var accessibleProjects = await _accessibleSet
            .ComposeForCiamContactAsync(contactId, AccessibleRecordSetService.ProjectEntity, reqCt)
            .ConfigureAwait(false);
        var accessibleMatters = await _accessibleSet
            .ComposeForCiamContactAsync(contactId, AccessibleRecordSetService.MatterEntity, reqCt)
            .ConfigureAwait(false);
        var accessibleWorkAssignments = await _accessibleSet
            .ComposeForCiamContactAsync(contactId, AccessibleRecordSetService.WorkAssignmentEntity, reqCt)
            .ConfigureAwait(false);

        // Per-record rights, straight from the evaluator — as WorkforcePrincipalStrategy does. Only entries
        // carrying Read reach the principal (task 136 · C2): a record the caller holds nothing on is not one
        // of its records, whatever map it came in.
        var projectAccess = CallerPrincipal.FromReadBearing(accessibleProjects.Rights)
            .Select(kvp => new CallerProjectAccess { ProjectId = kvp.Key, Rights = kvp.Value })
            .ToList();
        var matterAccess = CallerPrincipal.FromReadBearing(accessibleMatters.Rights);
        var workAssignmentAccess = CallerPrincipal.FromReadBearing(accessibleWorkAssignments.Rights);

        _logger.LogInformation(
            "[EXT-AUTH] Contact {ContactId} authenticated (oid-resolved: {ByOid}) — accessible roots: {Projects} project / {Matters} matter / {Was} work-assignment",
            contactId, !string.IsNullOrEmpty(oid), projectAccess.Count,
            matterAccess.Count, workAssignmentAccess.Count);

        return CallerPrincipalResolution.Resolved(new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = contactId,
            SystemUserId = null,
            Email = email ?? string.Empty,
            Oid = oid,
            ProjectAccess = projectAccess,
            MatterAccess = matterAccess,
            WorkAssignmentAccess = workAssignmentAccess,
        });
    }

    /// <summary>
    /// The ProblemDetails detail for a CIAM deny. States only what the decision established (FR-B03) — and
    /// says so plainly where the fix is an administrator's, so a refused external user is not left guessing.
    /// </summary>
    internal static string CiamDenyDetail(string code) => code switch
    {
        ContactBindingDecision.DenyContactNotFound =>
            "No contact is registered for this sign-in. External users are added by invitation.",
        ContactBindingDecision.DenyContactInactive =>
            "The contact for this sign-in has been deactivated.",
        ContactBindingDecision.DenyContactOidAmbiguous or ContactBindingDecision.DenyContactEmailAmbiguous =>
            "More than one contact matches this sign-in, so access cannot be resolved. An administrator has been "
            + "alerted through an identity-collision flag.",
        ContactBindingDecision.DenyContactBoundToDifferentOid or ContactBindingDecision.DenyContactLinkedToOtherUser =>
            "The contact for this email belongs to another sign-in. An administrator has been alerted through an "
            + "identity-collision flag.",
        ContactBindingDecision.DenyContactBindingUnreadable =>
            "The contact for this sign-in carries an identity binding that cannot be read. An administrator has "
            + "been alerted through an identity-collision flag.",
        ContactBindingDecision.DenyContactKeyConflict =>
            "Another contact holds the identity key for this sign-in, so it cannot be bound. An administrator has been "
            + "alerted through an identity-collision flag.",
        _ => "Access could not be resolved for this sign-in. Please try again.",
    };
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
    // ── The blanket Collaborate stamp is GONE (task 033 / FR-19 / register A-8) ───────────────────
    //
    // Until 2026-09-04 this class carried a blanket `Collaborate` constant and stamped it over
    // EVERY project in the accessible set. (The constant's name is deliberately not written here:
    // the FR-19 acceptance check is a repo-wide grep that must return ZERO, and a prose mention
    // would defeat it.) It was justified as "the accessible set is the
    // record-scope (NFR-08) boundary; the level governs within-project rights only" — but those are
    // not separable, because the stamp WAS the level every downstream rights check read. A deliberate
    // ViewOnly grant to a workforce caller therefore conferred Read|Create|Write, and the Create/Write
    // gates already sitting on the mutating routes could never fire.
    //
    // Rights now come from the evaluator's (recordId -> rights) answer, per record. Nothing here
    // supplies a default: a record absent from the map is absent from the principal.
    //
    // ⚠️ Do NOT reintroduce a per-plane default level. If a plane needs one it belongs in the
    // evaluator as a TERM (see AccessibleRecordSetService.MembershipTermRights), where it composes
    // under highest-wins max instead of overwriting every other term.

    /// <summary>The root entity types whose accessible sets are composed onto the principal (task 028 —
    /// polymorphic Tier-2 scoping). Each is membership/assignment ∪ own-contact grants for that type.</summary>
    internal const string ProjectEntity = "sprk_project";
    internal const string MatterEntity = "sprk_matter";
    internal const string WorkAssignmentEntity = "sprk_workassignment";

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

        // Compose the Tier-2 record scope (task 022, generalized to polymorphic roots by task 028) — the
        // SAME common accessible-record-set the workforce download gate uses, now composed for EACH root
        // type (project / matter / work assignment). This is what prevents a workforce caller from seeing
        // all records merely by authenticating (R2 NFR-08). Each set = membership/assignment ∪ own-contact
        // grants for that type.
        var reqCt = httpContext.RequestAborted;
        var accessibleProjects = await _accessibleSet
            .ComposeAsync(principal, ProjectEntity, reqCt).ConfigureAwait(false);
        var accessibleMatters = await _accessibleSet
            .ComposeAsync(principal, MatterEntity, reqCt).ConfigureAwait(false);
        var accessibleWorkAssignments = await _accessibleSet
            .ComposeAsync(principal, WorkAssignmentEntity, reqCt).ConfigureAwait(false);

        // Per-record rights, straight from the evaluator — no stamp, no per-plane default. Only entries
        // carrying Read reach the principal (task 136 · C2); until then this copied every key, a None-rights
        // one included, and every presence-gated read admitted it.
        var projectAccess = CallerPrincipal.FromReadBearing(accessibleProjects.Rights)
            .Select(kvp => new CallerProjectAccess { ProjectId = kvp.Key, Rights = kvp.Value })
            .ToList();
        var matterAccess = CallerPrincipal.FromReadBearing(accessibleMatters.Rights);
        var workAssignmentAccess = CallerPrincipal.FromReadBearing(accessibleWorkAssignments.Rights);

        _logger.LogInformation(
            "[WF-AUTH] Workforce {Kind} (systemuser={SystemUserId}, contact={ContactId}) resolved with " +
            "{Projects} project / {Matters} matter / {Was} work-assignment accessible roots (project sources: {Sources}).",
            principal.Kind, principal.SystemUserId, principal.ContactId, projectAccess.Count,
            matterAccess.Count, workAssignmentAccess.Count, accessibleProjects.Sources);

        return CallerPrincipalResolution.Resolved(new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = principal.ContactId ?? Guid.Empty,
            SystemUserId = principal.SystemUserId,
            Email = WorkforcePrincipalResolver.ExtractTokenEmail(httpContext.User) ?? string.Empty,
            Oid = principal.Oid,
            ProjectAccess = projectAccess,
            MatterAccess = matterAccess,
            WorkAssignmentAccess = workAssignmentAccess,
            // Task 140: where an UNDATED term (standing-grant membership, organization expansion) ran — the contact-side
            // grant route caps an issued grant's expiry at the grantor's own only when their level rests on a dated grant.
            UndatedAccessTermEntityTypes = new[] { accessibleProjects, accessibleMatters, accessibleWorkAssignments }
                .Where(set => set.Sources.StandingGrantMembership || set.Sources.OrgExpansionMembership)
                .Select(set => set.EntityType)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
        });
    }
}
