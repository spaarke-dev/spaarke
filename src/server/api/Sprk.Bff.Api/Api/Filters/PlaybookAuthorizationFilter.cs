using System.Security.Claims;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding playbook authorization to endpoints.
/// </summary>
public static class PlaybookAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds authorization for playbook owner operations (update, delete, share).
    /// The caller must own the playbook: the caller's Dataverse systemuserid equals the row's owner.
    /// </summary>
    public static TBuilder AddPlaybookOwnerAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddPlaybookAuthorizationFilter(PlaybookAuthorizationMode.OwnerOnly);

    /// <summary>
    /// Adds authorization for playbook access operations (read): the playbook-use decision (public, or the
    /// caller's own Dataverse Read on the row). An unknown playbook answers 404 and a denied one 403.
    /// </summary>
    public static TBuilder AddPlaybookAccessAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddPlaybookAuthorizationFilter(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);

    /// <summary>
    /// Adds the playbook-use decision for <c>GET /api/ai/playbooks/by-id/{id}</c>, keyed on the route value
    /// <c>id</c>, with ONE uniform 404 for an unknown id, a denied id and a decision fault
    /// (<see cref="PlaybookAuthorizationMode.UniformById"/>). It runs before the handler, so before the
    /// handler's response cache is read.
    /// </summary>
    public static TBuilder AddPlaybookByIdAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddPlaybookAuthorizationFilter(PlaybookAuthorizationMode.UniformById);

    /// <summary>
    /// Adds the run decision for <c>POST /api/ai/playbooks/{id:guid}/execute</c> and
    /// <c>POST /api/agent/run-playbook</c> (<see cref="PlaybookAuthorizationMode.Run"/>): the shared playbook-parameter
    /// policy's syntax half (400), the playbook-use decision (uniform 404), then the caller's own rights on every document
    /// and every record parameter the run reads or writes (uniform 403); the run's user is the caller's systemuserid.
    /// </summary>
    public static TBuilder AddPlaybookRunAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddPlaybookAuthorizationFilter(PlaybookAuthorizationMode.Run);

    private static TBuilder AddPlaybookAuthorizationFilter<TBuilder>(
        this TBuilder builder, PlaybookAuthorizationMode mode) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new PlaybookAuthorizationFilter(
                services.GetRequiredService<IPlaybookService>(),
                services.GetService<ILogger<PlaybookAuthorizationFilter>>(),
                mode,
                // GetService: a missing registration denies inside the filter (ADR-003), never passes.
                services.GetService<AuthorizationService>());
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization mode for playbook endpoints. Values are appended, never renumbered.
/// </summary>
public enum PlaybookAuthorizationMode
{
    /// <summary>The caller must own the playbook (caller systemuserid == the row's owner).</summary>
    OwnerOnly,

    /// <summary>The playbook-use decision: public, or the caller's own Dataverse Read on the row (404 unknown / 403 denied).</summary>
    OwnerOrSharedOrPublic,

    /// <summary>
    /// <c>GET /api/ai/playbooks/by-id/{id}</c> (task 164): the playbook-use decision on the route id, with one
    /// uniform 404 for unknown, denied and faulting cases.
    /// </summary>
    UniformById,

    /// <summary>
    /// <c>POST /api/ai/playbooks/{id:guid}/execute</c> and <c>POST /api/agent/run-playbook</c> (task 164): the
    /// playbook-use decision (uniform 404), then Read — or Write, when the run can write to them — on every
    /// document the run is given and every record parameter (uniform 403), after the parameter policy's 400s.
    /// </summary>
    Run
}

/// <summary>
/// Authorization filter for Playbook endpoints.
/// Validates the caller has appropriate access to playbook records.
/// </summary>
/// <remarks>
/// Follows ADR-008: Use endpoint filters for resource-level authorization. Fails closed (ADR-003).
///
/// Authorization strategy (unified-access-control-r2 task 164):
/// - OwnerOnly: the caller's Dataverse systemuserid (WhoAmI over the caller's OBO token) equals the playbook's
///   <c>_ownerid_value</c>. Before task 164 this compared the Entra <c>oid</c> with that systemuserid — two
///   GUID spaces — so it denied every caller (owner round 12 item 6).
/// - OwnerOrSharedOrPublic / UniformById / Run: the ONE playbook-use decision,
///   <see cref="BuildPlaybookUseCheckAsync(IPlaybookService, Guid, string, CancellationToken)"/> (task 162): public,
///   or the caller's own Dataverse Read on the <c>sprk_analysisplaybook</c> row — ownership, team GrantAccess
///   shares (how <see cref="IPlaybookSharingService"/> shares) and role depth. The former owner/shared branch
///   compared the oid with a systemuserid and passed the oid to a teammemberships query keyed by systemuserid,
///   so it reduced to "public".
/// </remarks>
public class PlaybookAuthorizationFilter : IEndpointFilter
{
    private readonly IPlaybookService _playbookService;
    private readonly ILogger<PlaybookAuthorizationFilter>? _logger;
    private readonly PlaybookAuthorizationMode _mode;
    private readonly AuthorizationService? _authorizationService;

    /// <summary>The <see cref="Spaarke.Core.Auth.OperationAccessPolicy"/> key the playbook-use decision asks of a playbook row.</summary>
    public const string PlaybookUseOperation = "read";

    /// <summary>
    /// The playbook-use decision — "may this caller run this playbook" — shared by every route that runs or binds a
    /// caller-chosen playbook (unified-access-control-r2 task 162: /api/ai/analysis/execute and /promote; task 164
    /// switches this filter's own routes and the chat/agent playbook routes to it).
    /// </summary>
    /// <returns>
    /// <c>null</c> when no check is needed: the playbook exists and is PUBLIC (<c>sprk_ispublic</c>, an application
    /// flag, not a Dataverse share). Otherwise a <see cref="FinanceCheckPath.Record"/> check, operation
    /// <see cref="PlaybookUseOperation"/>, on that <c>sprk_analysisplaybook</c> row — so Dataverse's OWN answer decides
    /// (ownership, team POA shares, role depth; owner round 9). A playbook <see cref="IPlaybookService.GetPlaybookAsync"/>
    /// does not find gets the same check, which Dataverse answers None for, so unknown and denied are one answer.
    /// </returns>
    /// <remarks>
    /// Deliberately does NOT compare <c>playbook.OwnerId</c> (a Dataverse systemuserid) with the caller's Entra oid:
    /// the two GUID spaces differ. That comparison was the pre-164 defect in this filter's owner branch, which task 164
    /// fixed (owner round 12 item 6): OwnerOnly now resolves the caller's systemuserid
    /// (<see cref="ResolveCallerSystemUserIdAsync"/>) and compares THAT with the owner. A fault in the lookup propagates;
    /// every caller denies on it (ADR-003).
    /// </remarks>
    public static async Task<FinanceAuthorizationCheck?> BuildPlaybookUseCheckAsync(
        IPlaybookService playbookService, Guid playbookId, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playbookService);

        if (playbookId != Guid.Empty)
        {
            var playbook = await playbookService.GetPlaybookAsync(playbookId, cancellationToken);
            if (playbook is { IsPublic: true })
            {
                return null;
            }
        }

        // An empty id reaches the evaluator as a check with no record, which it denies (no_target).
        return new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record,
            EntitySetName = PlaybookService.EntitySetName,
            RecordId = playbookId,
            Operation = PlaybookUseOperation,
            Source = source,
        };
    }

    /// <summary>
    /// The same decision as <see cref="BuildPlaybookUseCheckAsync(IPlaybookService, Guid, string, CancellationToken)"/>,
    /// resolving <see cref="IPlaybookService"/> from <paramref name="services"/>. For callers OUTSIDE the AI API surface
    /// (e.g. AnalysisAuthorizationFilter): ADR-013 / FR-C6 keeps <see cref="IPlaybookService"/> out of their type
    /// dependencies (ADR013_AiBoundaryTests), and this filter is the grandfathered owner of that lookup. A missing
    /// registration throws, which every caller denies on (ADR-003).
    /// </summary>
    public static Task<FinanceAuthorizationCheck?> BuildPlaybookUseCheckAsync(
        IServiceProvider services, Guid playbookId, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        return BuildPlaybookUseCheckAsync(
            services.GetRequiredService<IPlaybookService>(), playbookId, source, cancellationToken);
    }

    /// <summary>
    /// The ONE detail of the uniform record/document 403 on the run routes. It names no id: the same text for a
    /// document that does not exist, one the caller may not use, and a check that faulted.
    /// </summary>
    public const string RecordAccessDeniedDetail = "You do not have access to one or more of the requested records.";

    /// <summary>The <c>type</c> URI of the playbook-unavailable 404 (Spaarke problems convention, unchanged from FR-01).</summary>
    public const string PlaybookNotFoundType = "https://spaarke.com/problems/playbook-not-found";

    /// <summary>The <c>title</c> of the playbook-unavailable 404 (unchanged from FR-01).</summary>
    public const string PlaybookNotFoundTitle = "Playbook Not Found";

    /// <summary>The ONE <c>detail</c> of the playbook-unavailable 404. It names no id (it used to echo it).</summary>
    public const string PlaybookNotFoundDetail = "The requested playbook was not found.";

    /// <summary>
    /// The ONE "playbook unavailable" response for the playbook-id kind on by-id, execute and agent run-playbook:
    /// identical (status, type, title, detail, reasonCode, extension keys) for an unknown playbook, a playbook the
    /// caller may not use and a decision fault. No detail or extension carries the requested id; <c>instance</c> is
    /// the request path. The by-id handler returns the same body when its own lookup misses.
    /// </summary>
    public static IResult UniformPlaybookNotFound(HttpContext httpContext) =>
        Results.Problem(
            type: PlaybookNotFoundType,
            title: PlaybookNotFoundTitle,
            statusCode: StatusCodes.Status404NotFound,
            detail: PlaybookNotFoundDetail,
            instance: httpContext.Request.Path.Value,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = FinanceAuthorizationFilter.RecordUnavailableReasonCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    /// <summary>The ONE uniform 403 for the record/document kind on the run routes (constant reasonCode and detail).</summary>
    public static IResult UniformRecordAccessDenied(HttpContext httpContext) =>
        ProblemDetailsHelper.Forbidden(
            FinanceAuthorizationFilter.InsufficientRightsReasonCode,
            RecordAccessDeniedDetail,
            httpContext.TraceIdentifier);

    /// <summary>
    /// The caller's Dataverse <c>systemuserid</c> — the identity <c>_ownerid_value</c> holds — resolved by
    /// <c>WhoAmI()</c> on the caller's OBO token (<see cref="CallerRecordAccessProbe.GetCallerSystemUserIdAsync"/>).
    /// <c>null</c> on any failure (no token, no probe, a failed exchange, a fault): every caller treats null as
    /// "not the owner" (fail closed). Shared by OwnerOnly and the owned-playbook lists (owner round 12 item 6).
    /// </summary>
    public static async Task<Guid?> ResolveCallerSystemUserIdAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var probe = httpContext.RequestServices.GetService<CallerRecordAccessProbe>();
        if (probe is null)
        {
            return null;
        }

        try
        {
            return await probe.GetCallerSystemUserIdAsync(TokenHelper.ExtractBearerTokenOrNull(httpContext), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            httpContext.RequestServices.GetService<ILogger<PlaybookAuthorizationFilter>>()?.LogWarning(
                ex, "Playbook authorization: resolving the caller's systemuserid faulted; treating as unresolved (fail closed)");
            return null;
        }
    }

    public PlaybookAuthorizationFilter(
        IPlaybookService playbookService,
        ILogger<PlaybookAuthorizationFilter>? logger,
        PlaybookAuthorizationMode mode,
        AuthorizationService? authorizationService)
    {
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _logger = logger;
        _mode = mode;
        _authorizationService = authorizationService;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Order matters: NameIdentifier carries `sub` (non-GUID) under inbound claim mapping and is
        // ALWAYS present, so the former `?? oid` tail never ran and the Guid.TryParse below always
        // failed. See CallerResolution.
        var userIdClaim = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out _))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        return _mode switch
        {
            PlaybookAuthorizationMode.OwnerOnly => await AuthorizeOwnerAsync(context, next, userIdClaim),
            PlaybookAuthorizationMode.OwnerOrSharedOrPublic => await AuthorizeAccessAsync(context, next, userIdClaim),
            PlaybookAuthorizationMode.UniformById => await AuthorizeByIdAsync(context, next, userIdClaim),
            PlaybookAuthorizationMode.Run => await AuthorizeRunAsync(context, next, userIdClaim),
            // ADR-003: a mode with no case is a configuration fault — never a pass-through.
            _ => ProblemDetailsHelper.Forbidden(
                FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: httpContext.TraceIdentifier),
        };
    }

    /// <summary>OwnerOnly: the caller's systemuserid must equal the playbook's owner. Sibling routes keep 404/403.</summary>
    private async ValueTask<object?> AuthorizeOwnerAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, string userId)
    {
        var httpContext = context.HttpContext;
        if (!TryGetRouteGuid(httpContext, out var playbookId))
        {
            return PlaybookIdentifierMissing();
        }

        var playbook = await _playbookService.GetPlaybookAsync(playbookId);
        if (playbook == null)
        {
            _logger?.LogWarning("Playbook not found: {PlaybookId}", playbookId);
            return Results.NotFound();
        }

        var callerSystemUserId = await ResolveCallerSystemUserIdAsync(httpContext, httpContext.RequestAborted);
        if (callerSystemUserId is not { } systemUserId || playbook.OwnerId != systemUserId)
        {
            _logger?.LogWarning(
                "Playbook authorization denied: User {UserId} (systemuserid resolved: {Resolved}) does not own playbook {PlaybookId}",
                userId, callerSystemUserId.HasValue, playbookId);
            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "You do not have permission to modify this playbook");
        }

        _logger?.LogDebug("Playbook authorization granted: User {UserId} owns playbook {PlaybookId}", userId, playbookId);
        return await next(context);
    }

    /// <summary>
    /// OwnerOrSharedOrPublic (sibling read routes): the playbook-use decision. The existing 404-for-unknown /
    /// 403-for-denied split is kept on these routes, which are outside task 164's findings.
    /// </summary>
    private async ValueTask<object?> AuthorizeAccessAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, string userId)
    {
        var httpContext = context.HttpContext;
        if (!TryGetRouteGuid(httpContext, out var playbookId))
        {
            return PlaybookIdentifierMissing();
        }

        if (await IsPlaybookUseAllowedAsync(httpContext, playbookId, "route.id", userId))
        {
            return await next(context);
        }

        if (await _playbookService.GetPlaybookAsync(playbookId) == null)
        {
            _logger?.LogWarning("Playbook not found: {PlaybookId}", playbookId);
            return Results.NotFound();
        }

        return Results.Problem(
            statusCode: 403,
            title: "Forbidden",
            detail: "You do not have permission to access this playbook");
    }

    /// <summary>
    /// UniformById (<c>GET /api/ai/playbooks/by-id/{id}</c>): the playbook-use decision on the route id. The route
    /// carries no <c>:guid</c> constraint, so a non-GUID id — which cannot name a playbook — gets the same 404.
    /// </summary>
    private async ValueTask<object?> AuthorizeByIdAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, string userId)
    {
        var httpContext = context.HttpContext;
        if (!TryGetRouteGuid(httpContext, out var playbookId)
            || !await IsPlaybookUseAllowedAsync(httpContext, playbookId, "route.id", userId))
        {
            return UniformPlaybookNotFound(httpContext);
        }

        return await next(context);
    }

    /// <summary>
    /// Run (<c>POST /api/ai/playbooks/{id:guid}/execute</c>, <c>POST /api/agent/run-playbook</c>): the ids come from
    /// the SAME argument the handler binds. Validation 400s first (no rights query) — including the syntax half of the
    /// shared playbook-parameter policy — then the playbook-use decision (uniform 404), then every document and every
    /// record parameter (uniform 403), then the run's user. The handler writes SSE headers only after this passes.
    /// </summary>
    /// <remarks>
    /// <para><b>Documents: Read, or Write when the run can write them.</b> The same rule task 162 applies to
    /// <c>POST /api/ai/analysis/execute</c> (<see cref="AnalysisAuthorizationFilter.RunCanWriteDocuments"/>): no
    /// nodes (Legacy mode writes the document's profile outputs), a node with no executor type, or any
    /// side-effecting node requires Write; otherwise Read. One rule for one question.</para>
    /// <para><b>Parameters</b> (owner round 16 item 3): <see cref="PlaybookParameterPolicy.Evaluate"/> refuses a
    /// server-owned key, a record key that is not a GUID, a typed key of the wrong type and an undeclared GUID (400).
    /// Each record parameter is then authorized as the caller: Read, plus Write when a node that can write references it
    /// (<see cref="RecordParameterOperation"/>). Values are escaped wherever they land in query text, at the substitution
    /// point (<see cref="PlaybookTemplateContextBuilder.EscapeForQueryText"/>).</para>
    /// <para><b>The run's user is the caller</b>, set server-side: the caller's systemuserid (WhoAmI over OBO) is published
    /// for the handler (<see cref="GetRunUserId"/> → <see cref="PlaybookRunRequest.RunUserId"/>), so <c>run.userId</c>
    /// and every <c>eq-userid</c> query act for the caller. An unresolvable caller denies (uniform 403).</para>
    /// </remarks>
    private async ValueTask<object?> AuthorizeRunAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, string userId)
    {
        var httpContext = context.HttpContext;
        Guid playbookId;
        IReadOnlyList<Guid> documentIds;
        string source;
        IReadOnlyDictionary<string, string>? parameters;

        var agentRequest = context.Arguments.OfType<AgentPlaybookRequest>().FirstOrDefault();
        var executeRequest = context.Arguments.OfType<ExecutePlaybookRequest>().FirstOrDefault();
        if (agentRequest is not null)
        {
            // The agent handler's existing 400s, returned before any lookup or rights query.
            if (agentRequest.PlaybookId == Guid.Empty)
            {
                return BadRequestProblem(AgentEndpoints.PlaybookIdRequiredDetail);
            }

            if (agentRequest.DocumentId == Guid.Empty)
            {
                return BadRequestProblem(AgentEndpoints.DocumentIdRequiredDetail);
            }

            playbookId = agentRequest.PlaybookId;
            documentIds = [agentRequest.DocumentId];
            source = "body.playbookId";
            parameters = agentRequest.Parameters;
        }
        else if (executeRequest is not null && TryGetRouteGuid(httpContext, out playbookId))
        {
            // The execute handler's existing 400 (same constant, same ProblemDetails body), before any rights query.
            if (executeRequest.DocumentIds is not { Length: > 0 } requested)
            {
                return ProblemDetailsHelper.FromLegacyError(
                    StatusCodes.Status400BadRequest, PlaybookRunEndpoints.DocumentIdsRequiredMessage);
            }

            documentIds = requested.Distinct().ToArray();
            source = "route.id";
            parameters = executeRequest.Parameters;
        }
        else
        {
            // A Run-mode route whose request this filter cannot read is a configuration fault: deny.
            _logger?.LogError("Playbook run authorization: no AgentPlaybookRequest or ExecutePlaybookRequest argument (or no route id); denying (fail closed)");
            return UniformPlaybookNotFound(httpContext);
        }

        // 0. The shared playbook-parameter policy, syntax half — a 400 that depends on no record, before any rights query.
        var parameterEvaluation = PlaybookParameterPolicy.Evaluate(parameters);
        if (!parameterEvaluation.IsValid)
        {
            _logger?.LogWarning(
                "Playbook run REFUSED for caller {UserId}: parameter {Key} not accepted ({Reason})",
                userId, parameterEvaluation.RejectedKey, parameterEvaluation.Reason);
            return ParameterRejected(httpContext, parameterEvaluation);
        }

        // 1. The playbook-use decision — the playbook-id kind answers the uniform 404.
        if (!await IsPlaybookUseAllowedAsync(httpContext, playbookId, source, userId))
        {
            return UniformPlaybookNotFound(httpContext);
        }

        // 2. Every document and every record parameter, as the caller — the record kind answers the uniform 403.
        if (!await AreRunRecordsAllowedAsync(httpContext, playbookId, documentIds, parameterEvaluation.RecordParameters, userId))
        {
            return UniformRecordAccessDenied(httpContext);
        }

        // 3. The run's user: the authenticated caller's systemuserid, never a caller value (owner round 16 item 3).
        var runUserId = await ResolveCallerSystemUserIdAsync(httpContext, httpContext.RequestAborted);
        if (runUserId is not { } resolvedRunUserId || resolvedRunUserId == Guid.Empty)
        {
            _logger?.LogWarning("Playbook run DENIED for caller {UserId}: the caller's systemuserid could not be resolved", userId);
            return UniformRecordAccessDenied(httpContext);
        }

        httpContext.Items[RunUserIdItemKey] = resolvedRunUserId;
        return await next(context);
    }

    /// <summary>The <see cref="HttpContext.Items"/> key under which the run mode publishes the caller's systemuserid.</summary>
    private const string RunUserIdItemKey = "Sprk.PlaybookAuthorizationFilter.RunUserId";

    /// <summary>
    /// The run's user (the caller's Dataverse systemuserid) the run mode resolved and published, for the handler to put on
    /// <see cref="PlaybookRunRequest.RunUserId"/>; <c>null</c> when the run-mode filter did not pass on this request.
    /// </summary>
    public static Guid? GetRunUserId(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(RunUserIdItemKey, out var value) && value is Guid id ? id : null;
    }

    /// <summary>The stable <c>errorCode</c> of a refused playbook parameter (400).</summary>
    public const string ParameterRejectedErrorCode = "playbook.parameter-rejected";

    /// <summary>
    /// The 400 for a refused parameter. It names the caller's own key and the rule — never a record, and it does not
    /// depend on whether any record exists, so it is not an oracle. Public so every HTTP entry that applies the shared
    /// policy answers with the SAME body (task 163: <c>POST /api/insights/ask</c>).
    /// </summary>
    public static IResult ParameterRejected(HttpContext httpContext, PlaybookParameterPolicy.Evaluation evaluation) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: $"Playbook parameter '{evaluation.RejectedKey}' is not accepted: {evaluation.Reason}.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = ParameterRejectedErrorCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    /// <summary>
    /// The right a run needs on a record parameter: <c>"write"</c> when a node that can write — a side-effecting
    /// executor, or one with no executor type (unclassifiable, fail closed) — references the parameter anywhere in its
    /// ConfigJson (<see cref="PlaybookParameterPolicy.ReferencesParameter"/>; e.g. <c>matter-health-single</c>'s
    /// UpdateRecord <c>recordId: {{matterId}}</c>); otherwise <c>"read"</c>.
    /// </summary>
    internal static string RecordParameterOperation(IReadOnlyCollection<PlaybookNodeDto> nodes, string parameterName) =>
        ExecutorSideEffects.CanWriteThroughParameter(nodes, parameterName) ? "write" : "read";

    /// <summary>
    /// The checks a run of <paramref name="playbookId"/> needs AS THE CALLER when its entry route binds it to a SUBJECT
    /// record (unified-access-control-r2 task 163: <c>POST /api/insights/ask</c>; owner round 16 items 1 and 3), for the
    /// route's <see cref="FinanceAuthorizationFilter"/> evaluation:
    /// <list type="bullet">
    ///   <item>the subject: the run reaches it through the parameter the subject is bound to
    ///   (<paramref name="subjectParameterName"/> — <c>matterId</c> for <c>matter:{id}</c>, set by the Insights
    ///   orchestrator), so it gets the SAME rule as any record parameter: <c>"write"</c> when a node that can write
    ///   references that parameter (<see cref="ExecutorSideEffects.CanWriteThroughParameter"/> — e.g.
    ///   <c>matter-health-single</c>'s UpdateRecord <c>recordId: {{matterId}}</c> onto
    ///   <c>sprk_matter.sprk_performancesummary</c>), otherwise <c>"read"</c> (e.g. <c>predict-matter-cost</c>, which
    ///   writes nothing). Round 16 item 1: "Read suffices only for non-persisting playbooks". An EMPTY node list (the
    ///   Legacy run mode, which writes) is <c>"write"</c> — fail closed;</item>
    ///   <item>every record parameter the shared policy returned (<see cref="PlaybookParameterPolicy.Evaluate"/>): the
    ///   SAME rule as this filter's run mode — Read, plus Write when a node that can write references it
    ///   (<see cref="RecordParameterOperation"/>) — on its entity set from the existing allow-list.</item>
    /// </list>
    /// A record parameter naming the subject itself folds into the subject's check (the stronger right wins), so the
    /// caller is asked once per record. The subject check's <see cref="FinanceAuthorizationCheck.Operation"/> is what the
    /// route then passes on as the run's established subject right (<c>InsightsAgentRequest.SubjectWriteAuthorized</c>).
    /// </summary>
    /// <remarks>
    /// <b>The AI engine is not registered</b> (no <see cref="INodeService"/>: the compound AI gate is off, ADR-032): no
    /// playbook can run — the facade is its kill-switch Null peer — so every record is asked for Read and the subject's
    /// Write is NOT established. The facade's run guard refuses any run that can write without it (defence in depth),
    /// and the route keeps its kill-switch 503 instead of turning it into a deny.
    /// </remarks>
    /// <returns>The checks, subject first.</returns>
    /// <exception cref="InvalidOperationException">The node list cannot be read, or a record parameter's entity has no
    /// entity set. The caller DENIES on any exception (ADR-003).</exception>
    public static async Task<IReadOnlyList<FinanceAuthorizationCheck>> BuildSubjectRunChecksAsync(
        IServiceProvider services,
        Guid playbookId,
        string subjectEntitySet,
        Guid subjectId,
        string subjectParameterName,
        IReadOnlyList<PlaybookParameterPolicy.RecordParameter> recordParameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectEntitySet);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectParameterName);
        ArgumentNullException.ThrowIfNull(recordParameters);

        // The SAME source PlaybookOrchestrationService dispatches from. Null only when the engine is not registered.
        IReadOnlyCollection<PlaybookNodeDto>? nodes = null;
        var nodeService = services.GetService<INodeService>();
        if (nodeService is not null)
        {
            nodes = await nodeService.GetNodesAsync(playbookId, cancellationToken)
                ?? throw new InvalidOperationException("The playbook's node list could not be read.");
        }

        var checks = new List<FinanceAuthorizationCheck>
        {
            new()
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = subjectEntitySet,
                RecordId = subjectId,
                Operation = nodes is not null && (nodes.Count == 0 || ExecutorSideEffects.CanWriteThroughParameter(nodes, subjectParameterName))
                    ? "write"
                    : "read",
                Source = "body.subject",
            },
        };

        foreach (var parameter in recordParameters)
        {
            if (!SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(parameter.EntityLogicalName, out var entitySet))
            {
                throw new InvalidOperationException(
                    $"Record parameter '{parameter.Name}' names an entity with no entity set; the run's rights cannot be decided.");
            }

            var operation = nodes is null ? "read" : RecordParameterOperation(nodes, parameter.Name);
            var existing = checks.FindIndex(c =>
                string.Equals(c.EntitySetName, entitySet, StringComparison.Ordinal) && c.RecordId == parameter.RecordId);
            if (existing >= 0)
            {
                if (operation == "write")
                {
                    checks[existing] = checks[existing] with { Operation = "write" };
                }

                continue;
            }

            checks.Add(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = parameter.RecordId,
                Operation = operation,
                Source = "body.parameters." + parameter.Name,
            });
        }

        return checks;
    }

    /// <summary>
    /// The playbook-use decision (<see cref="BuildPlaybookUseCheckAsync(IPlaybookService, Guid, string, CancellationToken)"/>)
    /// evaluated as the caller: public → allowed; otherwise the caller's own Dataverse rights on the row must carry
    /// <see cref="PlaybookUseOperation"/>. Any fault, a missing token or a missing service answers false.
    /// </summary>
    private Task<bool> IsPlaybookUseAllowedAsync(HttpContext httpContext, Guid playbookId, string source, string userId) =>
        EvaluatePlaybookUseAsync(_playbookService, _authorizationService, httpContext, playbookId, source, userId, _logger);

    /// <summary>
    /// The playbook-use decision AS THE CALLER, for a route OUTSIDE this filter that binds a caller-chosen playbook — the
    /// chat session's <c>PlaybookId</c> (task 164: <c>AiAuthorizationFilter</c>'s chat-context evaluation; the definition
    /// reaches the prompt app-only through <c>PlaybookChatContextProvider</c>). The same rule as this filter's own routes:
    /// <see cref="BuildPlaybookUseCheckAsync(IPlaybookService, Guid, string, CancellationToken)"/>, then the caller's own
    /// Dataverse rights on the row. A missing oid, token or service, and any fault, answer false (ADR-003).
    /// </summary>
    public static Task<bool> IsPlaybookUseAllowedForCallerAsync(HttpContext httpContext, Guid playbookId, string source)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var userId = CallerResolution.ResolveObjectId(httpContext.User);
        var services = httpContext.RequestServices;
        var playbookService = services.GetService<IPlaybookService>();
        if (string.IsNullOrEmpty(userId) || playbookService is null)
        {
            return Task.FromResult(false);
        }

        return EvaluatePlaybookUseAsync(
            playbookService, services.GetService<AuthorizationService>(), httpContext, playbookId, source, userId,
            services.GetService<ILogger<PlaybookAuthorizationFilter>>());
    }

    private static async Task<bool> EvaluatePlaybookUseAsync(
        IPlaybookService playbookService,
        AuthorizationService? authorizationService,
        HttpContext httpContext,
        Guid playbookId,
        string source,
        string userId,
        ILogger? logger)
    {
        var ct = httpContext.RequestAborted;
        try
        {
            var check = await BuildPlaybookUseCheckAsync(playbookService, playbookId, source, ct);
            if (check is null)
            {
                return true;
            }

            if (authorizationService is null || check.Path != FinanceCheckPath.Record || check.RecordId == Guid.Empty)
            {
                logger?.LogWarning(
                    "Playbook-use DENIED for caller {UserId} [{Source}]: no evaluable check (service registered: {HasService})",
                    userId, source, authorizationService is not null);
                return false;
            }

            var snapshot = await authorizationService.GetCallerRecordAccessAsync(
                userId, check.EntitySetName, check.RecordId, TokenHelper.ExtractBearerTokenOrNull(httpContext), ct);
            var allowed = OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, check.Operation);
            if (!allowed)
            {
                logger?.LogWarning(
                    "Playbook-use DENIED: caller {UserId} on {EntitySet}({PlaybookId}) [{Source}] rights {Rights}",
                    userId, check.EntitySetName, playbookId, source, snapshot.AccessRights);
            }

            return allowed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Playbook-use decision faulted for caller {UserId} [{Source}]; denying (fail closed)", userId, source);
            return false;
        }
    }

    /// <summary>
    /// Read — or Write when <see cref="AnalysisAuthorizationFilter.RunCanWriteDocuments"/> says the run can write
    /// them — on EVERY document, as the caller (the document path of <see cref="AuthorizationService.AuthorizeAsync"/>,
    /// OBO); then <see cref="RecordParameterOperation"/> on EVERY record parameter (the entity-generic record path,
    /// <see cref="AuthorizationService.GetCallerRecordAccessAsync"/>). Any denial, missing id, missing token, missing
    /// service, a record entity with no entity set, or a fault answers false.
    /// </summary>
    private async Task<bool> AreRunRecordsAllowedAsync(
        HttpContext httpContext,
        Guid playbookId,
        IReadOnlyList<Guid> documentIds,
        IReadOnlyList<PlaybookParameterPolicy.RecordParameter> recordParameters,
        string userId)
    {
        var ct = httpContext.RequestAborted;
        try
        {
            var nodeService = httpContext.RequestServices.GetService<INodeService>();
            if (_authorizationService is null || nodeService is null)
            {
                _logger?.LogWarning("Playbook run documents DENIED for caller {UserId}: a decision service is not registered", userId);
                return false;
            }

            // The SAME source PlaybookOrchestrationService uses to pick the mode and dispatch each node.
            var nodes = await nodeService.GetNodesAsync(playbookId, ct)
                ?? throw new InvalidOperationException("The playbook's node list could not be read.");
            var operation = AnalysisAuthorizationFilter.RunCanWriteDocuments(nodes) ? "write" : "read";
            var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

            foreach (var documentId in documentIds)
            {
                if (documentId == Guid.Empty)
                {
                    _logger?.LogWarning("Playbook run documents DENIED for caller {UserId}: an empty document id", userId);
                    return false;
                }

                var result = await _authorizationService.AuthorizeAsync(new AuthorizationContext
                {
                    UserId = userId,
                    ResourceId = documentId.ToString(),
                    Operation = operation,
                    CorrelationId = httpContext.TraceIdentifier,
                    UserAccessToken = callerToken,
                }, ct);

                if (!result.IsAllowed)
                {
                    _logger?.LogWarning(
                        "Playbook run documents DENIED: caller {UserId} lacks {Operation} on document {DocumentId} (reason {Reason})",
                        userId, operation, documentId, result.ReasonCode);
                    return false;
                }
            }

            // Record parameters (owner round 16 item 3): the entity set comes from the existing allow-list, never by
            // pluralizing; a parameter whose entity has no set is a configuration fault and denies.
            foreach (var parameter in recordParameters)
            {
                if (!SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(parameter.EntityLogicalName, out var entitySet))
                {
                    _logger?.LogError(
                        "Playbook run DENIED for caller {UserId}: record parameter {Parameter} names {Entity}, which has no entity set",
                        userId, parameter.Name, parameter.EntityLogicalName);
                    return false;
                }

                var parameterOperation = RecordParameterOperation(nodes, parameter.Name);
                var snapshot = await _authorizationService.GetCallerRecordAccessAsync(
                    userId, entitySet, parameter.RecordId, callerToken, ct);
                if (!OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, parameterOperation))
                {
                    _logger?.LogWarning(
                        "Playbook run DENIED: caller {UserId} lacks {Operation} on {EntitySet}({RecordId}) named by parameter {Parameter}",
                        userId, parameterOperation, entitySet, parameter.RecordId, parameter.Name);
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Playbook run document decision faulted for caller {UserId}; denying (fail closed)", userId);
            return false;
        }
    }

    private static bool TryGetRouteGuid(HttpContext httpContext, out Guid playbookId)
    {
        playbookId = Guid.Empty;
        return httpContext.Request.RouteValues.TryGetValue("id", out var raw)
            && Guid.TryParse(raw?.ToString(), out playbookId)
            && playbookId != Guid.Empty;
    }

    private static IResult PlaybookIdentifierMissing() =>
        Results.Problem(
            statusCode: 400,
            title: "Bad Request",
            detail: "Playbook identifier not found in request");

    private static IResult BadRequestProblem(string detail) =>
        Results.Problem(
            statusCode: 400,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
}
