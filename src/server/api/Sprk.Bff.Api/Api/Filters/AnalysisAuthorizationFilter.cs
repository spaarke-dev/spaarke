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
    /// Adds authorization for the analysis execute endpoint: the caller must hold Read on every document named in
    /// the body (<see cref="AuthorizationMode.DocumentAccess"/>, through <see cref="IAiAuthorizationService"/>).
    /// </summary>
    public static TBuilder AddAnalysisExecuteAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddAnalysisAuthorizationFilter(AuthorizationMode.DocumentAccess);

    /// <summary>
    /// Adds the G5 pre-check for <c>POST /api/ai/analysis/create</c> (<see cref="AuthorizationMode.AnalysisCreate"/>;
    /// owner/main-session round 25 item 2, matching promote): the Create privilege on sprk_analysis,
    /// "analysis.attach" (Read + AppendTo) on the body document, the playbook-use decision for a body PlaybookId, and
    /// Read on every skill, knowledge and tool row the body associates.
    /// </summary>
    public static TBuilder AddAnalysisCreateAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.AddAnalysisAuthorizationFilter(AuthorizationMode.AnalysisCreate);

    /// <summary>
    /// Adds authorization for an analysis-id route (<see cref="AuthorizationMode.AnalysisAccess"/>): the
    /// analysis-read rule (<see cref="AnalysisAuthorizationFilter.ResolveAnalysisReadTargetsAsync"/>) — Read on EVERY
    /// populated anchor (parent) record, or, for an analysis with no anchor, the caller is the person who created it.
    /// Any failure answers the uniform 404.
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
    /// <summary>Read on every document id in the request body (/execute).</summary>
    DocumentAccess,

    /// <summary>The analysis-read rule on the route's analysis (GET /{analysisId}).</summary>
    AnalysisAccess,

    /// <summary>G5 pre-check for /promote (task 162).</summary>
    AnalysisPromote,

    /// <summary>Write / playbook-use check for /execute, after <see cref="DocumentAccess"/> (task 162).</summary>
    AnalysisRun,

    /// <summary>G5 pre-check for /create (task 162 f1; owner/main-session round 25 item 2).</summary>
    AnalysisCreate
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
///   <item><b>/execute</b> (<see cref="AuthorizationMode.DocumentAccess"/>): Read on every document id in the body,
///   through <see cref="IAiAuthorizationService"/> (RetrievePrincipalAccess over OBO). Unchanged.</item>
///   <item><b>GET /{analysisId}</b> (<see cref="AuthorizationMode.AnalysisAccess"/>): the analysis-read rule, declared
///   ONCE in <see cref="ResolveAnalysisReadTargetsAsync"/> (task 164's chat analysis host calls the same method). An
///   analysis is a CHILD record: the filter reads its anchor (parent) ids app-only — ids only, never content — and
///   requires Read on EVERY populated anchor (AND). An analysis with NO anchor is PERSONAL (owner round 15 item 4):
///   only the person who created it may read it, matched by Dataverse systemuserid (WhoAmI on the caller's OBO token —
///   never the Entra oid, never a row Read on the analysis). An unknown id, a denied id, an anchor of a type with no
///   entity set, an unverifiable creator and a fault all answer <see cref="FinanceAuthorizationFilter.UniformRecordNotFound"/>.</item>
///   <item><b>/create</b> (<see cref="AuthorizationMode.AnalysisCreate"/>) and <b>/promote</b>
///   (<see cref="AuthorizationMode.AnalysisPromote"/>): G5 — the Create privilege on sprk_analysis plus
///   "analysis.attach" (Read + AppendTo) on each parent the new row will point at, plus the playbook-use decision
///   (and, on /create, Read on each skill / knowledge / tool row the body associates); checked as the caller before
///   the app-only create.</item>
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
    /// The Read privilege on <c>sprk_analysis</c>, by its live name (spaarkedev1 <c>privileges</c>, 2026-10-04; held at
    /// Deep by Spaarke Core User and Spaarke Basic User, Basic by Spaarke AI Analysis User). The ONE check the personal
    /// (no-anchor) branch hands the evaluator once the caller is proven to be the creator: a TABLE privilege, never a row
    /// Read — a row Read at business-unit depth would let a colleague read another user's personal analysis.
    /// </summary>
    public const string ReadAnalysisPrivilege = "prvReadsprk_analysis";

    /// <summary>The system creator column of <c>sprk_analysis</c> (a systemuser lookup; live, always present).</summary>
    internal const string CreatedByColumn = "createdby";

    // ── Entity SET names of the scope rows /create associates (live EntityDefinitions.EntitySetName, spaarkedev1
    //    2026-10-04). Per-route constants, not a logical-name → set map (the FinanceAuthorizationFilter precedent).
    /// <summary><c>sprk_analysisskill</c>'s set name.</summary>
    public const string SkillEntitySet = "sprk_analysisskills";

    /// <summary><c>sprk_analysisknowledge</c>'s set name.</summary>
    public const string KnowledgeEntitySet = "sprk_analysisknowledges";

    /// <summary><c>sprk_analysistool</c>'s set name.</summary>
    public const string ToolEntitySet = "sprk_analysistools";

    /// <summary>
    /// The detail of the existing 400 for a /create or /execute body that names no document — one constant, so the
    /// /create G5 mode answers it byte-identically to the DocumentAccess mode it replaced.
    /// </summary>
    internal const string NoDocumentIdentifierDetail = "No document identifier found in request";

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

    /// <summary>The anchor columns: every anchor in <see cref="LookupColumns"/>.</summary>
    internal static readonly string[] AnchorColumns = LookupColumns
        .Where(c => c.Role != AnalysisLookupRole.NotAnchor)
        .Select(c => c.Column)
        .ToArray();

    /// <summary>
    /// What the analysis-read rule's ONE app-only retrieve selects: every anchor column plus <c>createdby</c> (the
    /// personal branch's first creator source). Every column exists live (pinned by a unit test against the snapshot):
    /// a column that does not exist would fault every read and deny every honest caller. The server-stamped person
    /// (<see cref="RecordCreatorPersonColumn.LogicalName"/>, task 146) is NOT here — it is read in its own query, so a BFF
    /// deployed before that column exists still serves every anchored analysis.
    /// </summary>
    internal static readonly string[] ReadRuleColumns = AnchorColumns.Append(CreatedByColumn).ToArray();

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
            AuthorizationMode.AnalysisCreate => await AuthorizeCreateAsync(context, next),
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
    /// Used for /execute (/create moved to <see cref="AuthorizationMode.AnalysisCreate"/>, task 162 f1).
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
            return NoDocumentIdentifier();
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

    /// <summary>The existing 400 for a body that names no document (one shape for /execute and /create).</summary>
    private static IResult NoDocumentIdentifier() =>
        Results.Problem(
            statusCode: 400,
            title: "Bad Request",
            detail: NoDocumentIdentifierDetail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    /// <summary>
    /// GET /{analysisId}: the analysis-read rule (<see cref="ResolveAnalysisReadTargetsAsync"/>), or the uniform 404
    /// (GitHub #233 item 1).
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

        var targets = await ResolveAnalysisReadTargetsAsync(httpContext, analysisId, _logger);
        return await EvaluateAsync(context, next, targets, FinanceDenial.UniformNotFound);
    }

    /// <summary>
    /// The analysis-read rule's ONE declaration: the checks a caller must pass to read analysis
    /// <paramref name="analysisId"/>, or a rejection (the uniform 404). Used by <c>GET /{analysisId}</c> and by the
    /// chat analysis host (task 164, <c>AiAuthorizationFilter</c>), so the two can never disagree.
    /// <list type="bullet">
    ///   <item>An analysis WITH anchors: Read on EVERY populated anchor (<see cref="BuildAnchorTargets"/>), read app-only
    ///   — ids only, never content.</item>
    ///   <item>An analysis with NO anchor is PERSONAL (owner round 15 item 4): readable only by the person who created
    ///   it, matched by Dataverse systemuserid (<see cref="BuildCreatorTargetsAsync"/>). This covers an analysis created
    ///   standalone and one whose parent was later deleted (the document relationship's Delete is RemoveLink, so the
    ///   lookup is cleared). Never the Entra oid, never a row Read on the analysis itself (a business-unit-depth Read
    ///   would expose one user's analysis to colleagues).</item>
    ///   <item>No caller token, an unknown id, an anchor type with no entity set, an unverifiable creator and any fault
    ///   are the uniform 404.</item>
    /// </list>
    /// </summary>
    internal static async Task<FinanceAuthorizationTargets> ResolveAnalysisReadTargetsAsync(
        HttpContext httpContext, Guid analysisId, ILogger? logger)
    {
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        if (callerToken is null)
        {
            // Every check would deny without a caller token; do not spend an app-only read first.
            return FinanceAuthorizationTargets.Reject(FinanceAuthorizationFilter.UniformRecordNotFound(httpContext));
        }

        try
        {
            var entityService = httpContext.RequestServices.GetRequiredService<IGenericEntityService>();
            var row = await entityService.RetrieveAsync(
                AnalysisEntityLogicalName, analysisId, ReadRuleColumns, httpContext.RequestAborted);

            var anchored = BuildAnchorTargets(row, httpContext, logger);
            if (anchored.Rejection is not null || anchored.Checks.Count > 0)
            {
                return anchored;
            }

            return await BuildCreatorTargetsAsync(httpContext, analysisId, row, callerToken, entityService, logger);
        }
        catch (Exception ex)
        {
            // Not found and any other fault are ONE answer (ADR-003): never distinguishable to the caller.
            if (RecordContainerResolver.IsRecordNotFound(ex))
            {
                logger?.LogInformation("[ANALYSIS-AUTH] Analysis {AnalysisId} does not exist; uniform 404", analysisId);
            }
            else
            {
                logger?.LogWarning(ex, "[ANALYSIS-AUTH] Analysis-read rule for {AnalysisId} faulted; uniform 404 (fail closed)", analysisId);
            }

            return FinanceAuthorizationTargets.Reject(FinanceAuthorizationFilter.UniformRecordNotFound(httpContext));
        }
    }

    /// <summary>
    /// The PERSONAL branch of the analysis-read rule, for an analysis with no populated anchor (owner round 15 item 4).
    /// The caller is the creator when the caller's Dataverse systemuserid — WhoAmI on the caller's OWN OBO token, which
    /// cannot name anyone else — equals the analysis's <c>createdby</c> or, for a row the BFF created as the application,
    /// its server-stamped <see cref="RecordCreatorPersonColumn.LogicalName"/> (task 146; read in its own query, so a
    /// missing column is "cannot tell", never "allowed"). The creator then needs only the TABLE privilege
    /// <see cref="ReadAnalysisPrivilege"/>, evaluated as the caller. Anything that cannot be verified — no probe, no
    /// systemuserid, no creator, a failed read — is the uniform 404.
    /// </summary>
    private static async Task<FinanceAuthorizationTargets> BuildCreatorTargetsAsync(
        HttpContext httpContext, Guid analysisId, Entity row, string callerToken, IGenericEntityService entityService, ILogger? logger)
    {
        var notFound = FinanceAuthorizationTargets.Reject(FinanceAuthorizationFilter.UniformRecordNotFound(httpContext));
        var ct = httpContext.RequestAborted;

        var probe = httpContext.RequestServices.GetService<CallerRecordAccessProbe>();
        if (probe is null)
        {
            logger?.LogError("[ANALYSIS-AUTH] No-anchor analysis {AnalysisId}: CallerRecordAccessProbe is not registered; uniform 404 (fail closed)", analysisId);
            return notFound;
        }

        if (await probe.GetCallerSystemUserIdAsync(callerToken, ct) is not { } callerSystemUserId || callerSystemUserId == Guid.Empty)
        {
            logger?.LogWarning("[ANALYSIS-AUTH] No-anchor analysis {AnalysisId}: the caller's systemuserid could not be established; uniform 404", analysisId);
            return notFound;
        }

        if (!IsPerson(row.GetAttributeValue<EntityReference>(CreatedByColumn), callerSystemUserId))
        {
            Entity personRow;
            try
            {
                personRow = await entityService.RetrieveAsync(
                    AnalysisEntityLogicalName, analysisId, [RecordCreatorPersonColumn.LogicalName], ct);
            }
            catch (Exception ex)
            {
                logger?.LogInformation(ex,
                    "[ANALYSIS-AUTH] No-anchor analysis {AnalysisId}: the creator person could not be read; uniform 404 (fail closed)", analysisId);
                return notFound;
            }

            if (!IsPerson(personRow.GetAttributeValue<EntityReference>(RecordCreatorPersonColumn.LogicalName), callerSystemUserId))
            {
                logger?.LogInformation("[ANALYSIS-AUTH] No-anchor analysis {AnalysisId} is personal and the caller did not create it; uniform 404", analysisId);
                return notFound;
            }
        }

        return FinanceAuthorizationTargets.Authorize(
            FinanceAuthorizationCheck.CallerPrivilege(ReadAnalysisPrivilege, AnalysisEntitySetLabel, "personal.creator"));
    }

    private static bool IsPerson(EntityReference? principal, Guid callerSystemUserId) =>
        principal is { } reference && reference.Id != Guid.Empty && reference.Id == callerSystemUserId;

    /// <summary>
    /// Whether the caller may read analysis <paramref name="analysisId"/>: the analysis-read rule
    /// (<see cref="ResolveAnalysisReadTargetsAsync"/>) evaluated by the ONE per-route evaluator on every path it
    /// declares (Document, Record and Privilege). For a caller outside a route filter chain — task 164's chat analysis
    /// host — so it never re-implements the evaluation loop.
    /// </summary>
    internal static async Task<bool> IsAnalysisReadableAsync(HttpContext httpContext, Guid analysisId, ILogger? logger)
    {
        var targets = await ResolveAnalysisReadTargetsAsync(httpContext, analysisId, logger);
        return await EvaluateOutsideFilterAsync(httpContext, targets, FinanceDenial.UniformNotFound, logger) is null;
    }

    /// <summary>
    /// Evaluates <paramref name="targets"/> with the ONE per-route evaluator (<see cref="FinanceAuthorizationFilter"/>,
    /// unchanged) where there is no filter chain — inside a handler, for a check that depends on data only the handler
    /// reads (promote's session document). Returns <c>null</c> when every check allows, otherwise the evaluator's own
    /// deny response, so a deny here is byte-identical to the same deny in a filter. An evaluator that cannot be
    /// resolved, or anything but the allow signal, denies (ADR-003).
    /// </summary>
    internal static async Task<IResult?> EvaluateOutsideFilterAsync(
        HttpContext httpContext, FinanceAuthorizationTargets targets, FinanceDenial denial, ILogger? logger)
    {
        var services = httpContext.RequestServices;
        Spaarke.Core.Auth.AuthorizationService? authorizationService;
        try
        {
            authorizationService = services.GetService<Spaarke.Core.Auth.AuthorizationService>();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[ANALYSIS-AUTH] AuthorizationService could not be resolved; denying (fail closed)");
            authorizationService = null;
        }

        if (authorizationService is null)
        {
            return DenyEvaluatorUnavailable(httpContext, denial);
        }

        var allowed = new object();
        var outcome = await new FinanceAuthorizationFilter(
            authorizationService,
            _ => targets,
            denial,
            services.GetService<CallerRecordAccessProbe>())
            .InvokeAsync(new DefaultEndpointFilterInvocationContext(httpContext), _ => ValueTask.FromResult<object?>(allowed));

        if (ReferenceEquals(outcome, allowed))
        {
            return null;
        }

        return outcome as IResult ?? DenyEvaluatorUnavailable(httpContext, denial);
    }

    /// <summary>
    /// Builds the Read checks for every populated anchor of <paramref name="row"/>. An analysis with no populated
    /// anchor yields NO check — on its own every evaluator denies that; <see cref="ResolveAnalysisReadTargetsAsync"/>
    /// then applies the personal (creator-only) branch. An anchor whose type has no entity set rejects with the uniform
    /// 404 rather than guessing one.
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
                checks.Add(AttachDocumentCheck(documentId, "body.documentId"));
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
    /// "analysis.attach" (Read + AppendTo) on a document a NEW app-created sprk_analysis will point at — the G5 parent
    /// check shared by /create, /promote's body document and /promote's session document, so all three ask the same
    /// question on the same path.
    /// </summary>
    internal static FinanceAuthorizationCheck AttachDocumentCheck(Guid documentId, string source) => new()
    {
        Path = FinanceCheckPath.Document,
        EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
        RecordId = documentId,
        Operation = AnalysisAttachOperation,
        Source = source,
    };

    /// <summary>
    /// POST /create (owner/main-session round 25 item 2: G5, matching promote). The row is created APP-ONLY, so the
    /// caller is checked first: the Create privilege on sprk_analysis; "analysis.attach" on the body document; the
    /// playbook-use decision for a body PlaybookId; and Read on every skill, knowledge and tool row the handler
    /// associates to the new row app-only (caller-chosen ids it consumes — the same "may this caller use this
    /// configuration row" question the playbook-use decision answers). <c>ActionId</c> is not consumed by the handler.
    /// </summary>
    private async ValueTask<object?> AuthorizeCreateAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<CreateAnalysisRequest>().FirstOrDefault();

        // The DocumentAccess mode's existing 400 for a body with no document, then the handler's own validation — so
        // every malformed body gets exactly the 400 it got before, and before any rights query.
        if (request is null || request.DocumentId == Guid.Empty)
        {
            return NoDocumentIdentifier();
        }

        var invalid = AnalysisEndpoints.ValidateCreateRequest(request);
        if (invalid is not null)
        {
            return invalid;
        }

        FinanceAuthorizationTargets targets;
        try
        {
            var checks = new List<FinanceAuthorizationCheck>
            {
                FinanceAuthorizationCheck.CallerPrivilege(CreateAnalysisPrivilege, AnalysisEntitySetLabel, "privilege.create"),
                AttachDocumentCheck(request.DocumentId, "body.documentId"),
            };

            if (request.PlaybookId is { } playbookId)
            {
                var playbookCheck = await PlaybookAuthorizationFilter.BuildPlaybookUseCheckAsync(
                    context.HttpContext.RequestServices, playbookId, "body.playbookId", context.HttpContext.RequestAborted);
                if (playbookCheck is not null)
                {
                    checks.Add(playbookCheck);
                }
            }

            checks.AddRange(ScopeReadChecks(request.SkillIds, SkillEntitySet, "body.skillIds"));
            checks.AddRange(ScopeReadChecks(request.KnowledgeIds, KnowledgeEntitySet, "body.knowledgeIds"));
            checks.AddRange(ScopeReadChecks(request.ToolIds, ToolEntitySet, "body.toolIds"));

            targets = FinanceAuthorizationTargets.Authorize(checks.ToArray());
        }
        catch (Exception ex)
        {
            return await EvaluateFaultAsync(context, next, ex, FinanceDenial.Forbidden);
        }

        return await EvaluateAsync(context, next, targets, FinanceDenial.Forbidden);
    }

    /// <summary>A Record-path Read on each distinct, non-empty scope row id (a null array is none).</summary>
    private static IEnumerable<FinanceAuthorizationCheck> ScopeReadChecks(Guid[]? ids, string entitySet, string source) =>
        (ids ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = id,
                Operation = ReadOperation,
                Source = source,
            });

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
            return ProblemDetailsHelper.FromLegacyError(StatusCodes.Status400BadRequest, AnalysisEndpoints.PlaybookIdRequiredMessage);
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
    /// Supports AnalysisExecuteRequest with DocumentIds array. (/create's CreateAnalysisRequest is decided by
    /// <see cref="AuthorizationMode.AnalysisCreate"/> since task 162 f1, so it is no longer read here.)
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

                case Guid documentId when documentId != Guid.Empty:
                    documentIds.Add(documentId);
                    break;
            }
        }

        return documentIds.Distinct().ToList();
    }
}
