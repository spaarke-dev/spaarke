using System.Security.Claims;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding analysis authorization to endpoints.
/// </summary>
public static class AnalysisAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds authorization for the analysis create/execute endpoints: the caller must hold Read on every
    /// document named in the body (<see cref="AuthorizationMode.DocumentAccess"/>, through
    /// <see cref="IAiAuthorizationService"/>).
    /// </summary>
    public static TBuilder AddAnalysisExecuteAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddAnalysisAuthorizationFilter(AuthorizationMode.DocumentAccess);

    /// <summary>
    /// Adds authorization for an analysis-id route (<see cref="AuthorizationMode.AnalysisAccess"/>): the caller
    /// must hold Read on EVERY populated anchor (parent) record of the analysis named by the
    /// <c>analysisId</c> route value. Any failure answers the uniform 404.
    /// </summary>
    public static TBuilder AddAnalysisRecordAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddAnalysisAuthorizationFilter(AuthorizationMode.AnalysisAccess);

    /// <summary>
    /// Adds the G5 pre-check for <c>POST /api/ai/analysis/promote</c> (<see cref="AuthorizationMode.AnalysisPromote"/>):
    /// the Create privilege on sprk_analysis, "analysis.attach" on the body document and regarding record, and the
    /// playbook-use decision for a body PlaybookId. The session-derived checks run in the handler.
    /// </summary>
    public static TBuilder AddAnalysisPromoteAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddAnalysisAuthorizationFilter(AuthorizationMode.AnalysisPromote);

    /// <summary>
    /// Adds the run check for <c>POST /api/ai/analysis/execute</c> (<see cref="AuthorizationMode.AnalysisRun"/>),
    /// chained AFTER <see cref="AddAnalysisExecuteAuthorizationFilter{TBuilder}"/>: Write on every document for the
    /// document-profile branch or a playbook that can write, and the playbook-use decision otherwise.
    /// </summary>
    public static TBuilder AddAnalysisRunAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddAnalysisAuthorizationFilter(AuthorizationMode.AnalysisRun);

    private static TBuilder AddAnalysisAuthorizationFilter<TBuilder>(
        this TBuilder builder, AuthorizationMode mode) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<AnalysisAuthorizationFilter>>();
            var filter = new AnalysisAuthorizationFilter(authService, logger, mode);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization mode for analysis endpoints. The numeric values are pinned by a unit test: append only.
/// </summary>
public enum AuthorizationMode
{
    /// <summary>Read on every document id in the request body (/create, /execute).</summary>
    DocumentAccess,

    /// <summary>Read on every populated anchor of the route's analysis (GET /{analysisId}).</summary>
    AnalysisAccess,

    /// <summary>G5 pre-check for /promote (task 162).</summary>
    AnalysisPromote,

    /// <summary>Write / playbook-use check for /execute, after <see cref="DocumentAccess"/> (task 162).</summary>
    AnalysisRun
}

/// <summary>How a <c>sprk_analysis</c> lookup column takes part in the AnalysisAccess decision.</summary>
public enum AnalysisLookupRole
{
    /// <summary>Not a parent of the analysis (configuration, principal, business unit, assignee).</summary>
    NotAnchor,

    /// <summary>A parent <c>sprk_document</c>: checked on the document path.</summary>
    DocumentAnchor,

    /// <summary>A parent record of another type: checked on the entity-generic record path.</summary>
    RecordAnchor,
}

/// <summary>One Lookup column of <c>sprk_analysis</c>, as the live metadata sweep returned it, and its role.</summary>
public sealed record AnalysisLookupColumn(string Column, string TargetLogicalName, AnalysisLookupRole Role, string Reason);

/// <summary>
/// Authorization filter for the Analysis endpoints. Follows ADR-008 (endpoint filters for resource-level
/// authorization) and ADR-003 (fail closed).
/// </summary>
/// <remarks>
/// <para>Rules (unified-access-control-r2 task 162; owner round 9: the caller's OWN Dataverse rights decide):</para>
/// <list type="bullet">
///   <item><b>/create, /execute</b> (<see cref="AuthorizationMode.DocumentAccess"/>): Read on every document id in the
///   body, through <see cref="IAiAuthorizationService"/> (RetrievePrincipalAccess over OBO). Unchanged.</item>
///   <item><b>GET /{analysisId}</b> (<see cref="AuthorizationMode.AnalysisAccess"/>): an analysis is a CHILD record. The
///   filter reads its anchor (parent) ids app-only — ids only, never content — and requires Read on EVERY populated
///   anchor (AND). An unknown id, a denied id, an analysis with no anchor, an anchor of a type with no entity set, and
///   a fault all answer <see cref="FinanceAuthorizationFilter.UniformRecordNotFound"/>.</item>
///   <item><b>/promote</b> (<see cref="AuthorizationMode.AnalysisPromote"/>): G5 — the Create privilege on
///   sprk_analysis plus "analysis.attach" (Read + AppendTo) on each parent the new row will point at, plus the
///   playbook-use decision; checked as the caller before the app-only create.</item>
///   <item><b>/execute</b> (<see cref="AuthorizationMode.AnalysisRun"/>): Write on every document when the run can
///   write to them (the document-profile branch, an empty or unclassifiable node list, or a side-effecting node),
///   otherwise Read; plus the playbook-use decision for any playbook other than the document-profile Binding's.</item>
/// </list>
/// <para>Every new check is a <see cref="FinanceAuthorizationCheck"/> evaluated by the ONE per-route evaluator,
/// <see cref="FinanceAuthorizationFilter"/> (task 130). This class only declares the checks.</para>
/// </remarks>
public class AnalysisAuthorizationFilter : IEndpointFilter
{
    /// <summary>
    /// The <see cref="Spaarke.Core.Auth.OperationAccessPolicy"/> key for attaching a NEW app-created sprk_analysis
    /// to a parent record (Read + AppendTo).
    /// </summary>
    public const string AnalysisAttachOperation = "analysis.attach";

    /// <summary>
    /// The Create privilege on <c>sprk_analysis</c>, by its live name (spaarkedev1 <c>privileges</c>, 2026-10-03 —
    /// the table's schema name is <c>sprk_analysis</c>, all lower case, unlike <c>prvCreatesprk_Invoice</c>). A
    /// misspelt name answers "not held" for every caller, which is why a test pins it.
    /// </summary>
    public const string CreateAnalysisPrivilege = "prvCreatesprk_analysis";

    /// <summary>
    /// <c>sprk_analysis</c>'s entity set per live metadata (spaarkedev1, 2026-10-03: <c>sprk_analysises</c>). Used
    /// ONLY as the Privilege check's deny-log label; it reaches no URL.
    /// </summary>
    public const string AnalysisEntitySetLabel = "sprk_analysises";

    /// <summary>The analysis entity's logical name, for the app-only anchor retrieve.</summary>
    internal const string AnalysisEntityLogicalName = "sprk_analysis";

    /// <summary>
    /// EVERY Lookup attribute on <c>sprk_analysis</c>, as the live metadata sweep returned it (spaarkedev1
    /// <c>EntityDefinitions(LogicalName='sprk_analysis')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata</c>,
    /// 2026-10-03; 27 columns), each classified. A unit test fails on a column missing from this table or listed
    /// twice. The anchor retrieve selects ONLY the anchor columns below — a column that does not exist live would
    /// fault every read and deny every honest caller.
    /// </summary>
    public static readonly IReadOnlyList<AnalysisLookupColumn> LookupColumns =
    [
        // ── Parents that carry content or confer access: Read on each populated one is required (AND).
        new("sprk_documentid", "sprk_document", AnalysisLookupRole.DocumentAnchor, "the analysed document"),
        new("sprk_outputfileid", "sprk_document", AnalysisLookupRole.DocumentAnchor, "the document the analysis output was saved as"),
        new("sprk_regardingdocument", "sprk_document", AnalysisLookupRole.DocumentAnchor, "typed regarding: document"),
        new("sprk_regardingmatter", "sprk_matter", AnalysisLookupRole.RecordAnchor, "typed regarding / core-ancestor stamp: matter"),
        new("sprk_regardingproject", "sprk_project", AnalysisLookupRole.RecordAnchor, "typed regarding / core-ancestor stamp: project"),
        new("sprk_regardingworkassignment", "sprk_workassignment", AnalysisLookupRole.RecordAnchor, "typed regarding / core-ancestor stamp: work assignment"),
        new("sprk_regardinginvoice", "sprk_invoice", AnalysisLookupRole.RecordAnchor, "typed regarding: invoice"),
        new("sprk_regardingbudget", "sprk_budget", AnalysisLookupRole.RecordAnchor, "typed regarding: budget (no entity set in EntityAccessFilter: denies)"),
        new("sprk_regardingcommunication", "sprk_communication", AnalysisLookupRole.RecordAnchor, "typed regarding: communication (no entity set in EntityAccessFilter: denies)"),
        new("sprk_regardingservicerequest", "sprk_servicerequest", AnalysisLookupRole.RecordAnchor, "typed regarding: service request (no entity set in EntityAccessFilter: denies)"),

        // ── Configuration rows: they confer nothing about this analysis.
        new("sprk_actionid", "sprk_analysisaction", AnalysisLookupRole.NotAnchor, "configuration: the analysis action"),
        new("sprk_agreementtype", "sprk_agreementtype", AnalysisLookupRole.NotAnchor, "configuration: the agreement-type registry row"),
        new("sprk_playbook", "sprk_analysisplaybook", AnalysisLookupRole.NotAnchor, "configuration: the playbook that produced it"),
        new("sprk_regardingrecordtype", "sprk_recordtype_ref", AnalysisLookupRole.NotAnchor, "configuration: the polymorphic pair's type row; the typed lookup written with it is the anchor"),

        // ── Assignees: people assigned to the analysis, not parents of it. Their own access to the analysis comes
        //    from the Assigned-To grant model; the CALLER's rights on a contact row say nothing about the analysis.
        new("sprk_assignedattorney1", "contact", AnalysisLookupRole.NotAnchor, "assignee (contact), not a parent"),
        new("sprk_assignedattorney2", "contact", AnalysisLookupRole.NotAnchor, "assignee (contact), not a parent"),
        new("sprk_assignedparalegal1", "contact", AnalysisLookupRole.NotAnchor, "assignee (contact), not a parent"),
        new("sprk_assignedparalegal2", "contact", AnalysisLookupRole.NotAnchor, "assignee (contact), not a parent"),

        // ── Principals and business units.
        new("sprk_reviewerby", "systemuser", AnalysisLookupRole.NotAnchor, "principal: reviewer"),
        new("createdby", "systemuser", AnalysisLookupRole.NotAnchor, "principal"),
        new("createdonbehalfby", "systemuser", AnalysisLookupRole.NotAnchor, "principal"),
        new("modifiedby", "systemuser", AnalysisLookupRole.NotAnchor, "principal"),
        new("modifiedonbehalfby", "systemuser", AnalysisLookupRole.NotAnchor, "principal"),
        new("ownerid", "systemuser,team", AnalysisLookupRole.NotAnchor, "principal: owner"),
        new("owningbusinessunit", "businessunit", AnalysisLookupRole.NotAnchor, "business unit"),
        new("owningteam", "team", AnalysisLookupRole.NotAnchor, "principal: owning team"),
        new("owninguser", "systemuser", AnalysisLookupRole.NotAnchor, "principal: owning user"),
    ];

    /// <summary>The columns the anchor retrieve selects: every anchor in <see cref="LookupColumns"/>.</summary>
    internal static readonly string[] AnchorColumns = LookupColumns
        .Where(c => c.Role != AnalysisLookupRole.NotAnchor)
        .Select(c => c.Column)
        .ToArray();

    private const string ReadOperation = "read";
    private const string WriteOperation = "write";

    private readonly IAiAuthorizationService _authorizationService;
    private readonly ILogger<AnalysisAuthorizationFilter>? _logger;
    private readonly AuthorizationMode _mode;

    public AnalysisAuthorizationFilter(
        IAiAuthorizationService authorizationService,
        ILogger<AnalysisAuthorizationFilter>? logger,
        AuthorizationMode mode)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
        _mode = mode;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Verify user has identity claims
        var userId = CallerResolution.ResolveObjectId(user);

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        return _mode switch
        {
            AuthorizationMode.DocumentAccess => await AuthorizeDocumentAccessAsync(context, user, next),
            AuthorizationMode.AnalysisAccess => await AuthorizeAnalysisAccessAsync(context, next),
            AuthorizationMode.AnalysisPromote => await AuthorizePromoteAsync(context, next),
            AuthorizationMode.AnalysisRun => await AuthorizeRunAsync(context, next),
            // ADR-003: a mode with no case is a configuration fault — never a pass-through.
            _ => DenyUnhandledMode(httpContext)
        };
    }

    private IResult DenyUnhandledMode(HttpContext httpContext)
    {
        _logger?.LogError("[ANALYSIS-AUTH] Unhandled authorization mode {Mode}; denying (fail closed)", _mode);
        return ProblemDetailsHelper.Forbidden(
            FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: httpContext.TraceIdentifier);
    }

    /// <summary>
    /// Authorize access to documents in the request body.
    /// Used for /create and /execute.
    /// </summary>
    /// <remarks>
    /// Uses FullUAC authorization via IAiAuthorizationService.
    /// </remarks>
    private async ValueTask<object?> AuthorizeDocumentAccessAsync(
        EndpointFilterInvocationContext context,
        ClaimsPrincipal user,
        EndpointFilterDelegate next)
    {
        // Extract document IDs from request arguments
        var documentIds = ExtractDocumentIds(context);
        if (documentIds.Count == 0)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "No document identifier found in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        try
        {
            var result = await _authorizationService.AuthorizeAsync(
                user,
                documentIds,
                context.HttpContext,
                context.HttpContext.RequestAborted);

            if (!result.Success)
            {
                _logger?.LogWarning(
                    "[ANALYSIS-AUTH] Document access DENIED: DocumentCount={Count}, Reason={Reason}",
                    documentIds.Count,
                    result.Reason);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: result.Reason ?? "Access denied to one or more documents",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            }

            _logger?.LogDebug(
                "[ANALYSIS-AUTH] Document access GRANTED: DocumentCount={Count}",
                documentIds.Count);

            return await next(context);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ANALYSIS-AUTH] Authorization check failed with exception");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization check failed",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// GET /{analysisId}: Read on EVERY populated anchor of the analysis, or the uniform 404 (GitHub #233 item 1).
    /// </summary>
    private async ValueTask<object?> AuthorizeAnalysisAccessAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // A missing or unparseable id does not depend on any record, so the existing 400 is not an oracle.
        // (The {analysisId:guid} route constraint makes it unreachable in practice.)
        if (!httpContext.Request.RouteValues.TryGetValue("analysisId", out var analysisIdValue) ||
            !Guid.TryParse(analysisIdValue?.ToString(), out var analysisId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Analysis identifier not found in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        FinanceAuthorizationTargets targets;
        if (TokenHelper.ExtractBearerTokenOrNull(httpContext) is null)
        {
            // Every check would deny without a caller token; do not spend an app-only read first.
            targets = FinanceAuthorizationTargets.Reject(FinanceAuthorizationFilter.UniformRecordNotFound(httpContext));
        }
        else
        {
            try
            {
                var entityService = httpContext.RequestServices.GetRequiredService<IGenericEntityService>();
                var row = await entityService.RetrieveAsync(
                    AnalysisEntityLogicalName, analysisId, AnchorColumns, httpContext.RequestAborted);
                targets = BuildAnchorTargets(row, httpContext, _logger);
            }
            catch (Exception ex)
            {
                // Not found and any other fault are ONE answer (ADR-003): never distinguishable to the caller.
                if (RecordContainerResolver.IsRecordNotFound(ex))
                {
                    _logger?.LogInformation("[ANALYSIS-AUTH] Analysis {AnalysisId} does not exist; uniform 404", analysisId);
                }
                else
                {
                    _logger?.LogWarning(ex, "[ANALYSIS-AUTH] Anchor read for analysis {AnalysisId} faulted; uniform 404 (fail closed)", analysisId);
                }

                targets = FinanceAuthorizationTargets.Reject(FinanceAuthorizationFilter.UniformRecordNotFound(httpContext));
            }
        }

        return await EvaluateAsync(context, next, targets, FinanceDenial.UniformNotFound);
    }

    /// <summary>
    /// Builds the Read checks for every populated anchor of <paramref name="row"/>. An analysis with no populated
    /// anchor yields NO check (the evaluator denies it); an anchor whose type has no entity set rejects with the
    /// uniform 404 rather than guessing one.
    /// </summary>
    internal static FinanceAuthorizationTargets BuildAnchorTargets(Entity row, HttpContext httpContext, ILogger? logger)
    {
        var checks = new List<FinanceAuthorizationCheck>();
        foreach (var column in LookupColumns)
        {
            if (column.Role == AnalysisLookupRole.NotAnchor
                || !row.Attributes.TryGetValue(column.Column, out var value)
                || value is not EntityReference reference
                || reference.Id == Guid.Empty)
            {
                continue;
            }

            if (column.Role == AnalysisLookupRole.DocumentAnchor)
            {
                checks.Add(new FinanceAuthorizationCheck
                {
                    Path = FinanceCheckPath.Document,
                    EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
                    RecordId = reference.Id,
                    Operation = ReadOperation,
                    Source = "anchor." + column.Column,
                });
                continue;
            }

            if (!EntityAccessFilter.TryResolveEntitySet(column.TargetLogicalName, out var entitySet))
            {
                logger?.LogWarning(
                    "[ANALYSIS-AUTH] Analysis anchor {Column} targets {Target}, which has no entity set in EntityAccessFilter; uniform 404",
                    column.Column, column.TargetLogicalName);
                return FinanceAuthorizationTargets.Reject(FinanceAuthorizationFilter.UniformRecordNotFound(httpContext));
            }

            checks.Add(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = reference.Id,
                Operation = ReadOperation,
                Source = "anchor." + column.Column,
            });
        }

        return FinanceAuthorizationTargets.Authorize(checks.ToArray());
    }

    /// <summary>POST /promote: the G5 checks that depend only on the BODY (the session-derived ones are in the handler).</summary>
    private async ValueTask<object?> AuthorizePromoteAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<AnalysisPromoteRequest>().FirstOrDefault();

        // The handler's own body validation runs first, so a malformed body gets its existing 400 before any
        // rights query.
        var invalid = AnalysisEndpoints.ValidatePromoteRequest(request, out var regarding);
        if (invalid is not null)
        {
            return await EvaluateAsync(context, next, FinanceAuthorizationTargets.Reject(invalid), FinanceDenial.Forbidden);
        }

        FinanceAuthorizationTargets targets;
        try
        {
            var checks = new List<FinanceAuthorizationCheck>
            {
                FinanceAuthorizationCheck.CallerPrivilege(CreateAnalysisPrivilege, AnalysisEntitySetLabel, "privilege.create"),
            };

            if (request!.DocumentId is { } documentId && documentId != Guid.Empty)
            {
                checks.Add(new FinanceAuthorizationCheck
                {
                    Path = FinanceCheckPath.Document,
                    EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
                    RecordId = documentId,
                    Operation = AnalysisAttachOperation,
                    Source = "body.documentId",
                });
            }

            if (regarding is not null)
            {
                // The validator admits only sprk_matter | sprk_project; both resolve. A miss is a fault.
                if (!EntityAccessFilter.TryResolveEntitySet(regarding.EntityLogicalName, out var regardingSet))
                {
                    throw new InvalidOperationException(
                        $"No entity set for regarding type '{regarding.EntityLogicalName}'.");
                }

                checks.Add(new FinanceAuthorizationCheck
                {
                    Path = FinanceCheckPath.Record,
                    EntitySetName = regardingSet,
                    RecordId = regarding.RecordId,
                    Operation = AnalysisAttachOperation,
                    Source = "body.regardingEntityId",
                });
            }

            if (request.PlaybookId is { } playbookId)
            {
                var playbookCheck = await PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync(
                    context.HttpContext.RequestServices, playbookId, "body.playbookId", context.HttpContext.RequestAborted);
                if (playbookCheck is not null)
                {
                    checks.Add(playbookCheck);
                }
            }

            targets = FinanceAuthorizationTargets.Authorize(checks.ToArray());
        }
        catch (Exception ex)
        {
            return await EvaluateFaultAsync(context, next, ex, FinanceDenial.Forbidden);
        }

        return await EvaluateAsync(context, next, targets, FinanceDenial.Forbidden);
    }

    /// <summary>
    /// POST /execute, after the DocumentAccess Read check: Write on every document when the run can write to them,
    /// and the playbook-use decision for any playbook other than the document-profile Binding's.
    /// </summary>
    private async ValueTask<object?> AuthorizeRunAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = context.Arguments.OfType<AnalysisExecuteRequest>().FirstOrDefault();

        // The handler's existing 400 (same constant), returned before any lookup.
        if (request?.PlaybookId is not { } playbookId)
        {
            return Results.Json(new { error = AnalysisEndpoints.PlaybookIdRequiredMessage }, statusCode: StatusCodes.Status400BadRequest);
        }

        FinanceAuthorizationTargets targets;
        try
        {
            var ct = httpContext.RequestAborted;
            var services = httpContext.RequestServices;
            var documentIds = request.DocumentIds.Where(id => id != Guid.Empty).Distinct().ToArray();
            var checks = new List<FinanceAuthorizationCheck>();
            bool needsWrite;

            // The SAME call the handler makes to pick the document-profile branch.
            var documentProfilePlaybookId = await services.GetRequiredService<IConsumerRoutingService>()
                .ResolveAsync(ConsumerTypes.DocumentProfile, cancellationToken: ct);

            if (documentProfilePlaybookId is { } profileId && profileId == playbookId)
            {
                // The profile branch writes the document's profile fields and enqueues its indexing. Its playbook
                // is the system Binding's, not caller-chosen, so it needs no playbook-use check.
                needsWrite = true;
            }
            else
            {
                var playbookCheck = await PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync(
                    services, playbookId, "body.playbookId", ct);
                if (playbookCheck is not null)
                {
                    checks.Add(playbookCheck);
                }

                // The SAME source PlaybookOrchestrationService uses to pick the mode and dispatch each node.
                var nodes = await services.GetRequiredService<INodeService>().GetNodesAsync(playbookId, ct)
                    ?? throw new InvalidOperationException("The playbook's node list could not be read.");
                needsWrite = RunCanWriteDocuments(nodes);
            }

            foreach (var documentId in documentIds)
            {
                checks.Add(new FinanceAuthorizationCheck
                {
                    Path = FinanceCheckPath.Document,
                    EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
                    RecordId = documentId,
                    Operation = needsWrite ? WriteOperation : ReadOperation,
                    Source = "body.documentIds",
                });
            }

            targets = FinanceAuthorizationTargets.Authorize(checks.ToArray());
        }
        catch (Exception ex)
        {
            return await EvaluateFaultAsync(context, next, ex, FinanceDenial.Forbidden);
        }

        return await EvaluateAsync(context, next, targets, FinanceDenial.Forbidden);
    }

    /// <summary>
    /// True when running a playbook with these nodes can write to the request's documents: no nodes at all (Legacy
    /// mode writes the document's profile outputs and enqueues indexing), any node with no executor type (it cannot
    /// be classified before the run), or any side-effecting node, active or not.
    /// </summary>
    internal static bool RunCanWriteDocuments(IReadOnlyCollection<PlaybookNodeDto> nodes) =>
        nodes.Count == 0
        || nodes.Any(n => n.SprkExecutortype is not { } executorType || ExecutorSideEffects.IsSideEffecting(executorType));

    /// <summary>
    /// Hands the declared checks to the ONE per-route evaluator (task 130). An evaluator that cannot be resolved (a
    /// missing or broken registration) is a configuration fault and DENIES with this route's deny shape — never a
    /// 500 from an unhandled exception, never a pass-through (ADR-003).
    /// </summary>
    private ValueTask<object?> EvaluateAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        FinanceAuthorizationTargets targets,
        FinanceDenial denial)
    {
        var services = context.HttpContext.RequestServices;
        var authorizationService = ResolveEvaluatorOrNull(services);
        if (authorizationService is null)
        {
            return ValueTask.FromResult<object?>(DenyEvaluatorUnavailable(context.HttpContext, denial));
        }

        return new FinanceAuthorizationFilter(
            authorizationService,
            _ => targets,
            denial,
            services.GetService<CallerRecordAccessProbe>()).InvokeAsync(context, next);
    }

    /// <summary>
    /// A fault while DECLARING the checks (a playbook, node or routing lookup, a missing registration) is handed to
    /// the evaluator as a throwing declaration, so it is denied with the evaluator's own system-failure response and
    /// log line — one deny rendering, never a pass-through.
    /// </summary>
    private ValueTask<object?> EvaluateFaultAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        Exception fault,
        FinanceDenial denial)
    {
        _logger?.LogWarning(fault, "[ANALYSIS-AUTH] {Mode}: declaring the checks faulted; denying (fail closed)", _mode);
        var services = context.HttpContext.RequestServices;
        var authorizationService = ResolveEvaluatorOrNull(services);
        if (authorizationService is null)
        {
            return ValueTask.FromResult<object?>(DenyEvaluatorUnavailable(context.HttpContext, denial));
        }

        return new FinanceAuthorizationFilter(
            authorizationService,
            _ => throw new InvalidOperationException("Analysis authorization: declaring the checks faulted.", fault),
            denial,
            services.GetService<CallerRecordAccessProbe>()).InvokeAsync(context, next);
    }

    /// <summary>
    /// The evaluator's <see cref="Spaarke.Core.Auth.AuthorizationService"/>, or <c>null</c> when it is not registered or
    /// its construction throws (a dependency missing). Both are configuration faults the caller denies on.
    /// </summary>
    private Spaarke.Core.Auth.AuthorizationService? ResolveEvaluatorOrNull(IServiceProvider services)
    {
        try
        {
            var authorizationService = services.GetService<Spaarke.Core.Auth.AuthorizationService>();
            if (authorizationService is null)
            {
                _logger?.LogError("[ANALYSIS-AUTH] {Mode}: AuthorizationService is not registered; denying (fail closed)", _mode);
            }

            return authorizationService;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ANALYSIS-AUTH] {Mode}: AuthorizationService could not be resolved; denying (fail closed)", _mode);
            return null;
        }
    }

    /// <summary>
    /// The deny for an unresolvable evaluator, in the route's own deny shape: the uniform 404 on GET (so it is not an
    /// oracle either), otherwise 403 <c>sdap.access.error.system_failure</c> — the same bodies the evaluator renders.
    /// </summary>
    private static IResult DenyEvaluatorUnavailable(HttpContext httpContext, FinanceDenial denial) =>
        denial == FinanceDenial.UniformNotFound
            ? FinanceAuthorizationFilter.UniformRecordNotFound(httpContext)
            : ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: httpContext.TraceIdentifier);

    /// <summary>
    /// Extract document IDs from request arguments.
    /// Supports AnalysisExecuteRequest with DocumentIds array.
    /// </summary>
    private static List<Guid> ExtractDocumentIds(EndpointFilterInvocationContext context)
    {
        var documentIds = new List<Guid>();

        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                case AnalysisExecuteRequest request when request.DocumentIds.Length > 0:
                    documentIds.AddRange(request.DocumentIds);
                    break;

                case CreateAnalysisRequest request when request.DocumentId != Guid.Empty:
                    documentIds.Add(request.DocumentId);
                    break;

                case Guid documentId when documentId != Guid.Empty:
                    documentIds.Add(documentId);
                    break;
            }
        }

        return documentIds.Distinct().ToList();
    }
}
