using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Attaches <see cref="ContainerDocumentAuthorizationFilter"/> to a container-keyed document route.
/// </summary>
/// <remarks>
/// <para>This uses the lambda form rather than <c>AddEndpointFilter&lt;T&gt;()</c>, and the choice is
/// load-bearing rather than stylistic. <c>AddEndpointFilter&lt;T&gt;()</c> builds its instance ONCE from the
/// application (root) provider at endpoint-build time, so a filter holding <b>Scoped</b> dependencies —
/// <see cref="RecordContainerResolver"/> and <see cref="AuthorizationService"/> are both Scoped
/// (<c>Program.cs:63</c>, <c>Infrastructure/DI/SpaarkeCore.cs:26</c>) — becomes a captive-dependency bug:
/// one Dataverse client and one per-request cache captured for the process lifetime. Resolving from
/// <c>HttpContext.RequestServices</c> per request is the idiom the sibling gates already use
/// (<see cref="DocumentAuthorizationFilterExtensions.AddDocumentAuthorizationFilter{TBuilder}"/>,
/// <see cref="EntityAccessFilterExtensions.AddEntityAccessFilter{TBuilder}"/>).</para>
///
/// <para>The method NAME is also load-bearing: <c>tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs</c>
/// recognises a per-resource gate by matching <c>.Add\w*AuthorizationFilter[&lt;(]</c> at the call site
/// (its <c>FilterMarker</c> regex), and Rule B locates the endpoint files attaching a filter by searching
/// for <c>Add{FilterTypeName}(</c>. Renaming this method to anything that does not end in
/// <c>AuthorizationFilter</c> would make the route read as UNGATED to that guard even though it is gated —
/// which is the failure mode task 074 exists to prevent, inverted.</para>
/// </remarks>
public static class ContainerDocumentAuthorizationFilterExtensions
{
    /// <summary>
    /// Requires the caller to hold Read on the record that OWNS the container named by the
    /// <c>{containerId}</c> route parameter. Refuses when no owner can be established.
    /// </summary>
    public static TBuilder AddContainerDocumentAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;

            var filter = new ContainerDocumentAuthorizationFilter(
                services.GetRequiredService<RecordContainerResolver>(),
                services.GetRequiredService<AuthorizationService>(),
                services.GetService<ILogger<ContainerDocumentAuthorizationFilter>>());

            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// unified-access-control-r2 task 078 — the per-resource gate for
/// <c>GET /api/v1/containers/{containerId}/documents</c>.
/// </summary>
/// <remarks>
/// <para><b>The hole.</b> That route took a container id straight off the URL and returned that
/// container's document metadata behind <c>.RequireAuthorization()</c> alone — authentication, not
/// authorization. Nothing checked that the caller had any relationship to the container or to the record
/// owning it. It is the READ-side twin of task 073's write-side hole, and like 073 it is NOT mitigated by
/// SharePoint Embedded ACLs, because the enumeration runs through the BFF's own app-only identity: SPE is
/// never consulted, so the fact that no user holds a container ACL stops nothing. Found by task 074's
/// route-authorization ArchTest on its first run — the sixth miss on a surface already enumerated four
/// times by hand.</para>
///
/// <para><b>The subject of authorization is the OWNING RECORD, not the container.</b> A container is not a
/// Dataverse row, so <c>RetrievePrincipalAccess</c> has no answer about one. Per
/// <c>SECURE-DOCUMENTS-BUILD-PLAN.md</c> invariant 2, access flows from the parent: a caller who may read a
/// project / matter / work assignment may read its documents. So this filter resolves container → owning
/// record and asks Dataverse, as the caller, whether they hold Read on THAT.</para>
///
/// <para><b>Everything here is borrowed; nothing is new.</b> Per root CLAUDE.md §11:</para>
/// <list type="bullet">
///   <item><description>container → record is <see cref="RecordContainerResolver.ResolveOwningRecordAsync"/>
///   (task 075) — the ONE such mapping in the codebase, in both directions. Task 075 delivered the FORWARD
///   direction's first consumer (<c>Services/Communication/Engine/CommunicationContainerResolver.cs</c>) and
///   task 076 wires the upload call sites; this is the REVERSE direction's first consumer, which is what
///   <see cref="OwningSecureRecord"/>'s doc comment named it for. (Task 073 consumed NEITHER direction — it
///   retired <c>Api/UploadEndpoints.cs</c> outright rather than gating it.) Depended on as a CONCRETE type,
///   per ADR-010 and the explicit note there that adding an <c>IRecordContainerResolver</c> would consume
///   the last of the 1:1-interface ratchet's audited headroom.</description></item>
///   <item><description>logical name → entity SET is
///   <see cref="SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet"/>, whose own remarks
///   state it is "internal, and deliberately the ONLY such map" — task 080 already consumes it rather than
///   declaring a copy, "because a second copy would be a second access policy, and the two would
///   drift".</description></item>
///   <item><description>the rights decision is <see cref="AuthorizationService.GetCallerRecordAccessAsync"/>
///   (task 070) — the canonical evaluator's entity-generic sibling, evaluated OBO as the caller. This is
///   deliberately NOT a direct <c>CallerRecordAccessProbe</c> call: the probe predates
///   <c>IAccessDataSource.GetRecordAccessAsync</c>, and <see cref="EntityAccessFilter"/>'s own remarks say
///   a filter should go back through <see cref="AuthorizationService"/> "so there is one access path again"
///   once that seam was generalized. Task 070 generalized it, so this filter starts there rather than
///   adding a second consumer to the older seam.</description></item>
/// </list>
///
/// <para><b>Fail closed — an unownable container is REFUSED (ADR-003).</b>
/// <see cref="RecordContainerResolver.ResolveOwningRecordAsync"/> returns <see langword="null"/> when no
/// SECURE record claims the container, i.e. it is a shared business-unit or archive container. This filter
/// treats that as DENY, not as permission. Two reasons, and the second is the one that decides it:</para>
/// <list type="number">
///   <item><description>An unknown answer read as "permitted" is the disclosure this task exists to close.
///   There is no owning record to evaluate, so there is no basis on which to allow — and "no basis to deny"
///   is not a basis to allow.</description></item>
///   <item><description>A shared container legitimately holds documents belonging to MANY records with
///   different access. A per-container gate is structurally incapable of answering "may you see these";
///   the correct control for that case is RESULT TRIMMING against the caller's accessible-record set
///   (Wave 3, <c>AccessibleRecordSetService</c> — the same reasoning the Permanent waiver on
///   <c>GET /api/v1/documents</c> records). Until trimming exists, refusing is the only honest
///   answer.</description></item>
/// </list>
///
/// <para>⚠️ <b>Recorded tension with task 075, deliberately not resolved silently.</b>
/// <c>RecordContainerResolver</c>'s early return for the zero-secure-claimant case carries the comment
/// that probing further would "turn the ordinary shared-container case into a refusal, breaking task 078
/// for every normal container" — i.e. 075 anticipated that THIS filter would ALLOW on
/// <see langword="null"/>. It does not, and the discrepancy is real rather than a misreading. What makes
/// refusing safe is evidence, not preference: the route has <b>zero callers</b> anywhere in <c>src/</c>,
/// <c>tests/</c> or <c>scripts/</c> (verified 2026-08-28), so there is no "normal container" list view to
/// break. See the task 078 notes for the full inventory and the modelling gap this surfaces.</para>
///
/// <para><b>Every refusal is INDISTINGUISHABLE to the caller — one status, one detail, one error code.</b>
/// This is the rule <see cref="SemanticSearchAuthorizationFilter"/> states for its own denials: <i>"the two
/// cases must stay indistinguishable to the caller in EVERY channel, not just the prose"</i>. Getting this
/// wrong is subtle, and the first version of this filter did: it emitted a distinct <c>errorCode</c> per
/// branch (no-owner / owner-not-authorizable / no-Read) and let the resolver's <c>container_ownership_*</c>
/// problems propagate as 409s carrying <i>"More than one record claims this container"</i>. Uniform prose
/// with a discriminating code is not uniform — together those were a four-way oracle letting an
/// unauthorized caller partition container ids by whether a secure record claims one, before any rights
/// check ran. Caught by the task's Step 9.5 review.</para>
///
/// <para>So the resolver's <see cref="Infrastructure.Exceptions.SdapProblemException"/> is caught here and
/// folded into the same 403 as every other denial. That is deliberately NOT the §11 duplication it looks
/// like: nothing re-derives a status code (the pipeline mapping at
/// <c>Infrastructure/DI/MiddlewarePipelineExtensions.cs:40</c> is still the only place that translates
/// <c>SdapProblemException</c> generally) — this route COLLAPSES its refusals to one answer, which is a
/// different act. Diagnosability is preserved where it belongs: the resolver already logs both conditions at
/// Error with the container id, and the fold logs the original problem code too, so an operator loses
/// nothing that a caller should not have been told.</para>
///
/// <para><b>ADR-008.</b> An endpoint filter, not handler code and not middleware.</para>
/// </remarks>
public class ContainerDocumentAuthorizationFilter : IEndpointFilter
{
    /// <summary>The route parameter naming the container. Must match the route template.</summary>
    /// <remarks>
    /// ⚠️ This constant and the literal <c>"/api/v1/containers/{containerId}/documents"</c> in
    /// <c>DataverseDocumentsEndpoints</c> are coupled by CONVENTION, not by the compiler: if the template's
    /// parameter is renamed, this filter takes its no-container-id branch and denies 100% of requests. That
    /// is fail-closed (an outage, never a disclosure), but it is silent, and ArchTest Rule A cannot see it —
    /// Rule A only checks that a filter is ATTACHED.
    ///
    /// <para>Building the template FROM this constant is the obvious fix and is <b>forbidden</b>: Rule A's
    /// scanner reads the route path with <c>^\s*\.Map\w+\s*\(\s*"([^"]*)"</c>, which requires a plain string
    /// literal. An interpolated <c>$"…"</c> would not match, the route would scan as
    /// <c>&lt;unresolved&gt;</c>, and the guard would report it as an ungated hole. The literal has to stay
    /// literal. Closing this properly needs one request through the real route template
    /// (<c>WebApplicationFactory</c>) — recorded as a successor obligation in the task 078 notes.</para>
    /// </remarks>
    internal const string ContainerRouteParameter = "containerId";

    /// <summary>
    /// The ONE error code every denial carries. Uniform by construction so the code cannot become an oracle
    /// — see the class remarks on why a per-branch code was wrong. The branch that actually fired is
    /// recorded in the LOG, which is where an operator should read it from.
    /// </summary>
    private const string DeniedErrorCode = "container_documents_access_denied";

    /// <summary>The one caller-facing denial message. Says nothing about why.</summary>
    private const string DeniedDetail = "You do not have access to this container.";

    private readonly RecordContainerResolver _containerResolver;
    private readonly AuthorizationService _authorizationService;
    private readonly ILogger<ContainerDocumentAuthorizationFilter>? _logger;

    public ContainerDocumentAuthorizationFilter(
        RecordContainerResolver containerResolver,
        AuthorizationService authorizationService,
        ILogger<ContainerDocumentAuthorizationFilter>? logger = null)
    {
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var correlationId = httpContext.TraceIdentifier;

        // The caller's Entra object id (`oid`) — the identifier IAccessDataSource matches against
        // systemuser.azureactivedirectoryobjectid. CallerResolution deliberately has no
        // ClaimTypes.NameIdentifier fallback: under inbound claim mapping that yields `sub`, a pairwise
        // non-GUID id that can never match a systemuser, so every caller would be denied on every route.
        var callerObjectId = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(callerObjectId))
        {
            // 401, not 403, and a distinct code: this is about the CALLER's credential, not about the
            // container, so it is not part of the resource oracle the denial codes are collapsed to avoid.
            // Matches the sibling gates (SemanticSearchAuthorizationFilter, RecordSearchAuthorizationFilter).
            return Deny(401, "Unauthorized", "User identity not found.",
                "container_documents_no_caller_identity", correlationId);
        }

        // Resolved BEFORE the container work, deliberately. AuthorizationService fails closed on a blank
        // token, so leaving this implicit was still safe — but it reported a missing CREDENTIAL as a 403
        // access denial, and it paid for the resolver's up-to-2×|securableEntities| uncached Dataverse
        // round trips first. Hoisting it makes the answer honest (401) and free.
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        if (string.IsNullOrWhiteSpace(callerToken))
        {
            _logger?.LogWarning(
                "Container document listing DENIED for caller {CallerId}: no bearer token on the request, so "
                + "access cannot be evaluated AS THE CALLER. Refusing rather than falling back to an "
                + "app-only read, which on a BFF-served surface answers yes for everyone (finding A-2). "
                + "CorrelationId: {CorrelationId}",
                callerObjectId, correlationId);

            return Deny(401, "Unauthorized",
                "A caller bearer token is required to evaluate access.",
                "container_documents_no_caller_token", correlationId);
        }

        var containerId = httpContext.Request.RouteValues.TryGetValue(ContainerRouteParameter, out var raw)
            ? raw?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(containerId))
        {
            // A gate that cannot see its own resource key must refuse, never pass through. The only way
            // to reach this is a route template that stopped carrying {containerId}, in which case
            // silently allowing would re-open the hole while the filter still read as attached.
            _logger?.LogError(
                "Container document listing DENIED: the '{Parameter}' route value was absent, so there is "
                + "no container to authorize. The route template and this filter have diverged. "
                + "CorrelationId: {CorrelationId}",
                ContainerRouteParameter, correlationId);

            return Denied(correlationId);
        }

        // Task 075's mapping, reverse direction. Returns null when NO secure record claims the container.
        //
        // Its SdapProblemException (container_ownership_ambiguous / _indeterminate, both 409) is CAUGHT and
        // folded into the uniform 403: propagating it told an unauthorized caller that a secure record
        // claims this container and that it is co-mingled, before any rights check ran. The resolver has
        // already logged both conditions at Error, and the code is logged again here, so nothing an
        // operator needs is lost.
        OwningSecureRecord? owner;
        try
        {
            owner = await _containerResolver
                .ResolveOwningRecordAsync(containerId, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (SdapProblemException ex)
        {
            _logger?.LogWarning(
                "Container document listing DENIED for caller {CallerId}: ownership of container "
                + "'{ContainerId}' could not be established ({ProblemCode}). Answering with the uniform "
                + "denial rather than the resolver's 409, which would confirm that a secure record claims "
                + "this container. CorrelationId: {CorrelationId}",
                callerObjectId, containerId, ex.Code, correlationId);

            return Denied(correlationId);
        }

        if (owner is null)
        {
            _logger?.LogWarning(
                "Container document listing DENIED for caller {CallerId}: no owning record could be "
                + "established for container '{ContainerId}', so there is nothing to authorize against. "
                + "Refusing rather than listing — a shared container holds documents of many records with "
                + "different access, and trimming that collection is Wave 3's mechanism, not this gate's. "
                + "CorrelationId: {CorrelationId}",
                callerObjectId, containerId, correlationId);

            return Denied(correlationId);
        }

        // The ONE entity-type → entity-set map (SemanticSearchAuthorizationFilter). A logical name with no
        // entry DENIES: a mapping that computed a set name could always compute one, so it could never
        // deny, and the value is interpolated into the Dataverse request path.
        if (!SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(
                owner.EntityLogicalName, out var entitySetName))
        {
            _logger?.LogWarning(
                "Container document listing DENIED for caller {CallerId}: container '{ContainerId}' is "
                + "owned by {Entity} {RecordId}, which is not an authorizable parent type. Refusing rather "
                + "than guessing its Dataverse collection. CorrelationId: {CorrelationId}",
                callerObjectId, containerId, owner.EntityLogicalName, owner.RecordId, correlationId);

            return Denied(correlationId);
        }

        var snapshot = await _authorizationService.GetCallerRecordAccessAsync(
            callerObjectId,
            entitySetName,
            owner.RecordId,
            callerToken,
            httpContext.RequestAborted).ConfigureAwait(false);

        if (!snapshot.AccessRights.HasFlag(AccessRights.Read))
        {
            // The uniform denial — same status, detail and code as every branch above, whether the record
            // is unreadable, absent, unowned or ambiguously owned. Distinguishing them in ANY channel would
            // confirm the existence of records the caller cannot see. Same rule as the sibling
            // parent-record gate on POST /api/ai/search.
            _logger?.LogWarning(
                "Container document listing DENIED: caller {CallerId} has no Read on "
                + "{EntitySet}({RecordId}), the owner of container '{ContainerId}' (rights={Rights}). "
                + "CorrelationId: {CorrelationId}",
                callerObjectId, entitySetName, owner.RecordId, containerId, snapshot.AccessRights,
                correlationId);

            return Denied(correlationId);
        }

        _logger?.LogInformation(
            "Container document listing authorized: caller {CallerId} holds {Rights} on "
            + "{EntitySet}({RecordId}), the owner of container '{ContainerId}'. CorrelationId: {CorrelationId}",
            callerObjectId, snapshot.AccessRights, entitySetName, owner.RecordId, containerId, correlationId);

        return await next(context);
    }

    /// <summary>
    /// THE denial. Every resource-side refusal returns exactly this — one status, one detail, one code — so
    /// a caller cannot tell which branch fired. Taking no parameter but the correlation id is the point: a
    /// future branch physically cannot supply a distinguishing reason through it.
    /// </summary>
    private static IResult Denied(string correlationId) =>
        Deny(403, "Forbidden", DeniedDetail, DeniedErrorCode, correlationId);

    /// <summary>
    /// One construction site for every refusal, so a new deny branch cannot accidentally ship a different
    /// body shape than the others. Only the two 401s (caller identity / caller token) call this directly;
    /// resource-side refusals go through <see cref="Denied"/>.
    /// </summary>
    private static IResult Deny(
        int statusCode, string title, string detail, string errorCode, string correlationId) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
}
