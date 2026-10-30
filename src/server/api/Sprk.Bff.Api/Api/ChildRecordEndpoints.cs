using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// The BFF write path for CHILD records created or re-filed in the BROWSER (unified-access-control-r2 task 147 r1; owner
/// round 28 item 1, "E1 = A1, the existing G5 pattern"). Every product writer that used to call
/// <c>Xrm.WebApi.createRecord</c> / <c>updateRecord</c> on a to-do, event, memo, invoice, report card, analysis or document
/// sends the SAME Web API payload here instead.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A browser create runs as the user, so Dataverse makes the USER the owner, in the user's own business
/// unit — a child of a secure project would be readable by everyone in that unit with ordinary depth (C10 part 2). The
/// owner is a server invariant (ADR-002 WP-1/WP-3): it must be decided here, never by the client.</para>
/// <para><b>Create (G5).</b> AS THE CALLER, under their own token: Create (and Append) on the table, AppendTo on every
/// record the payload binds, and no owner, audit, creator-person or field-secured column in the payload (the column
/// policy, so an app-only create can never pass field security). Then the APPLICATION creates the row, owned by the team
/// <see cref="IRecordOwnershipResolver"/> names — the named Secure team under a secure parent (secure-if-any), the
/// parent's business-unit team otherwise, the caller's business-unit team for an unfiled row (owner round 5) — and
/// records the caller in <c>sprk_createdbyperson</c>. The core is the ONE server create,
/// <see cref="RecordCreationService"/> (field-mapping-server-write-path-r1 task 017; it ported
/// <c>OwnedChildWrite.CreateAsync</c>, the core the chat create tool used, owner round 7 item 3). The core-ancestor stamp is
/// then derived by the server (<see cref="CoreAncestorRestamper"/>, WP-1): a stamp the client previewed is only a preview.</para>
/// <para><b>Re-file.</b> The caller's own PATCH (as the caller — Dataverse authorizes it, field security included),
/// inside <see cref="OwnedChildWrite.RefileAsync"/>: AppendTo on every record the row is moved under, F3 on a move out
/// of a secure record (owner round 10 item 7), the owner decided BEFORE the PATCH and assigned after it, read back. The
/// same core the chat update tool uses. Events and communications are re-filed through their own route families
/// (<c>PATCH /api/v1/events/{id}/filing</c>, <c>PATCH /api/communications/{id}/filing</c> — round 28: "never a second
/// one"; round 36: those two change ONLY the filing — the regarding lookups and their ADR-024 resolver fields — and carry
/// their family's as-caller Write check as an endpoint filter), which call <see cref="UpdateAsync"/>; a document is
/// re-filed through <c>PUT /api/v1/documents/{id}</c>. After every re-file, <see cref="SecureChildReconciler.AfterRefileAsync"/>
/// mirrors the row and runs task 148's pass over everything filed under it when it moved under, out of or between secure
/// records (round 36).</para>
/// <para><b>Uniform not-found (round 9).</b> A record the payload binds that the caller cannot append to and one that does
/// not exist get the SAME 404; so do a row the caller cannot read and one that does not exist.</para>
/// <para><b>Placement (CLAUDE.md §10; bff-extensions.md).</b> In the BFF: it is the invariant owner's write path (the
/// resolver, the synchronizer's sharees via the 2-minute job, the restamper all live here), BFF identity for the app-only
/// create, request-scoped, no background work, no package. CRUD code: it consumes no AI capability (no
/// <c>IOpenAiClient</c> / <c>IPlaybookService</c>); <see cref="OwnedChildWrite"/> and
/// <see cref="DataverseWriteItemMapper"/> are the shared Dataverse write cores (Services/Dataverse; the chat tools use them too, D-66)
/// and depend on nothing in Services/Ai.</para>
/// <para><b>Work assignments (spaarke-ontology-platform-r1 task 046, D-21; uac-r2 D-113).</b> The one server create path for
/// <c>sprk_workassignment</c>, a ROOT: the same G5 core with the secure-create plan (<see cref="SecureRootFilingGate"/>), so a
/// work assignment filed under a secure matter or project is created INTO isolation and completed here
/// (the service's post-create step), then its "Assigned *" contacts get their access. Provisioning's own columns are
/// refused (the registry's <see cref="RecordTableRule.TableServerOwnedColumns"/>). The Create Work Assignment wizard posts its payload here; a decision's
/// Assign Work follow-on calls the same handler in-process (<c>DecisionRouteCores.CreateChildAsync</c>). A caller with no
/// Dataverse identity is #1312's single 403 on this create route.</para>
/// </remarks>
public static class ChildRecordEndpoints
{
    /// <summary>
    /// The tables a browser writer creates through this route (task 147's census, note §2a). <c>sprk_budget</c>,
    /// <c>sprk_kpiassessment</c> and <c>sprk_billingevent</c> are the secure-record ribbon's "New Budget" / "New KPI
    /// Assessment" / "New Billing Event" (owner round 28 item 2, E2; the r1c live inventory of every main form found the
    /// last two on the matter, project, report card and invoice forms): the native subgrid "+ New" under a secure record is
    /// replaced by a command that creates the row here and then opens it — these tables have no product create surface of
    /// their own.
    /// </summary>
    internal static readonly IReadOnlySet<string> CreateTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_todo", "sprk_event", "sprk_memo", "sprk_invoice", "sprk_reportcard", "sprk_analysis", "sprk_document",
        "sprk_budget", "sprk_kpiassessment", "sprk_billingevent",
        // spaarke-ontology-platform-r1 task 046 (D-21; uac-r2's answer D-113 on #1355): the Create Work Assignment wizard's
        // create and a decision's Assign Work follow-on — ONE server create path for the table, this one. A work assignment is
        // a ROOT, not a child: the secure-create plan decides how it is made (filed under a secure matter or project → INTO
        // isolation, task 158 r1) and the service completes it (its post-create step).
        "sprk_workassignment",
    };

    /// <summary>
    /// The tables a browser writer re-files through <c>PATCH /api/v1/child-records/{table}/{id}</c> (note §2b). An event
    /// and a communication are re-filed through their own families, a document through <c>PUT /api/v1/documents/{id}</c>
    /// — one re-file route per table.
    /// </summary>
    internal static readonly IReadOnlySet<string> RefileTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_todo", "sprk_memo", "sprk_invoice", "sprk_reportcard", "sprk_analysis",
    };

    /// <summary>The stable reason code of the uniform not-found (a record the payload binds, or the row itself).</summary>
    internal const string NotFoundCode = "child_record.not_found";

    /// <summary>A table these routes do not write.</summary>
    internal const string UnsupportedTableCode = "child_record.table_unsupported";

    /// <summary>
    /// Ontology task 046 (NFR-10, D-29): a caller WhoAmI cannot name under their own token is #1312's single 403 — the same
    /// code the events list and the decision plan answer.
    /// </summary>
    internal const string CallerUnresolvedReasonCode = Sprk.Bff.Api.Api.Events.EventEndpoints.CallerUnresolvedReasonCode;

    /// <summary>The payload is not a Web API payload this route accepts.</summary>
    internal const string InvalidPayloadCode = "child_record.invalid_payload";

    /// <summary>The caller's own rights refused the write (a table privilege, a field-secured or server-owned column).</summary>
    internal const string DeniedCode = "child_record.denied";

    /// <summary>
    /// A filing route's payload names something other than the filing (owner round 36: "it changes only the filing: the
    /// regarding lookups and their ADR-024 pair"). Nothing is read or written.
    /// </summary>
    internal const string NotFilingCode = "child_record.not_filing";

    private const string FilingColumnPrefix = "sprk_regarding";

    private const string BindSuffix = "@odata.bind";

    /// <summary>
    /// The filing columns of an event or a communication: its entity-specific regarding lookups (<c>sprk_regarding{table}</c>
    /// — every ownership-parent lookup either table has, <see cref="SecureChildLineage"/>) and the ADR-024 resolver fields
    /// (<c>sprk_regardingrecordtype</c>, <c>…recordid</c>, <c>…recordname</c>, <c>…recordurl</c>, <c>…recordnumber</c>) —
    /// all, and only, the columns named <c>sprk_regarding…</c>. Navigation properties carry the schema-name casing
    /// (<c>sprk_RegardingMatter</c>), so the comparison ignores case.
    /// </summary>
    internal static bool IsFilingColumn(string column) =>
        column.StartsWith(FilingColumnPrefix, StringComparison.OrdinalIgnoreCase) && column.Length > FilingColumnPrefix.Length;

    /// <summary>
    /// The request-shape rule of a filing route (no I/O — it runs before the authorization filter, so a 400 never becomes a
    /// 403 and discloses nothing about a record): a JSON object naming at least one property, every one a filing column or
    /// its <c>@odata.bind</c>. <c>null</c> when the body may proceed.
    /// </summary>
    internal static IResult? FilingShapeProblem(JsonElement body, string entity)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.EnumerateObject().Any())
        {
            return Problem(StatusCodes.Status400BadRequest, NotFilingCode,
                $"Name what the {Noun(entity)} is filed under (its regarding lookups and regarding fields). Nothing was saved.");
        }

        foreach (var property in body.EnumerateObject())
        {
            var name = property.Name.EndsWith(BindSuffix, StringComparison.Ordinal)
                ? property.Name[..^BindSuffix.Length]
                : property.Name;
            if (!IsFilingColumn(name))
            {
                return Problem(StatusCodes.Status400BadRequest, NotFilingCode,
                    $"Only what the {Noun(entity)} is filed under can be changed here: '{property.Name}' is not one of its " +
                    "regarding lookups or regarding fields. Nothing was saved.");
            }
        }

        return null;
    }

    /// <summary>Registers the child-record routes.</summary>
    public static void MapChildRecordEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/child-records")
            .WithTags("Child Records")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        // POST /api/v1/child-records/{table} — create (G5): checked as the caller, created by the application, owned by
        // the resolver's team. Authorization is decided in the handler, as the caller, on every record the body binds.
        group.MapPost("/{table}", CreateAsync)
            .WithName("CreateChildRecord")
            .WithSummary("Create a child record (to-do, event, memo, invoice, report card, analysis, document, budget, KPI assessment, billing event) or a work assignment")
            .WithDescription("Takes the Dataverse Web API payload a browser writer would have sent. Checks AS THE CALLER " +
                "that they could create it (table privilege, AppendTo on every record it binds, no owner or field-secured " +
                "column), then the application creates it owned by the team the ownership rule names (the Secure Record " +
                "Owners team under a secure record) and records the caller as its creator person.")
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // PATCH /api/v1/child-records/{table}/{id} — re-file (the caller's own PATCH, owner decided and assigned).
        group.MapPatch("/{table}/{id:guid}", RefileAsync)
            .WithName("RefileChildRecord")
            .WithSummary("Re-file a child record (to-do, memo, invoice, report card, analysis)")
            .WithDescription("Applies the caller's own update (as the caller). When it changes what the row is filed " +
                "under, the caller needs AppendTo on every record it is moved under and, to move it out of a secure " +
                "record, Full Access on that record or to be its creator; the owner is re-derived and assigned.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>POST /api/v1/child-records/{table}.</summary>
    /// <remarks>
    /// Field-mapping-server-write-path-r1 task 017: a thin adapter over the ONE server create
    /// (<see cref="RecordCreationService"/>, as the caller). Every payload-dependent check (WhoAmI, the column policy,
    /// AppendTo, the pre-write rules, the secure-create plan), the owner, the app-only create and every post-create step (the
    /// root's completion, the "Assigned *" grants, the I-1 re-stamp, I-16, the sharee mirror, the membership event) run
    /// there, once (ADR-008 path A). The route keeps its own accept-list and renders the service's refusal in its own
    /// contract (<see cref="RefusalResult"/>, NFR-07). No field mapping is asked for here: the browser writers still map on
    /// the client until their wizard moves (FR-15/FR-16, tasks 020-029).
    /// </remarks>
    internal static async Task<IResult> CreateAsync(
        string table,
        [FromBody] JsonElement body,
        [FromServices] IDataverseUserClient user,
        [FromServices] RecordCreationService recordCreation,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var entity = (table ?? string.Empty).Trim().ToLowerInvariant();
        if (!CreateTables.Contains(entity))
            return Problem(StatusCodes.Status400BadRequest, UnsupportedTableCode, $"'{table}' records are not created here.");

        var outcome = await recordCreation.CreateAsync(
                new RecordCreationRequest(entity, new RecordCreationPayload.WebApi(body))
                {
                    CorrelationId = httpContext.TraceIdentifier,
                },
                RecordCreationPrincipal.ForCaller(user, CallerObjectId(httpContext)),
                ct)
            .ConfigureAwait(false);

        if (outcome.Refusal is { } refusal)
        {
            logger.LogWarning(
                "[CHILD-RECORD] create of {Entity} refused: {Kind} {Code} (clientStatus={ClientStatus}, correlationId={CorrelationId})",
                entity, refusal.Kind, refusal.Code, refusal.ClientFailure?.StatusCode, outcome.CorrelationId);
            return RefusalResult(refusal, entity, httpContext.TraceIdentifier);
        }

        if (outcome.CreatedId is not { } id)
        {
            // Neither a row nor a refusal: never a 201 (fail closed).
            return Problem(StatusCodes.Status500InternalServerError, NoDecisionCode, "The record was not created.");
        }

        logger.LogInformation("[CHILD-RECORD] created {Entity} {Id} (team-owned, isolated={Isolated}, warnings={Warnings})",
            entity, id, outcome.Isolated, outcome.Warnings.Count);
        return Results.Created($"/api/v1/child-records/{entity}/{id:D}",
            outcome.Warnings.Count == 0 ? new { id } : (object)new { id, warnings = outcome.Warnings });
    }

    /// <summary>The route's code for a create that ended without a decision.</summary>
    internal const string NoDecisionCode = "child_record.no_decision";

    /// <summary>
    /// Task 017 (NFR-07): a <see cref="RecordCreationService"/> refusal in THIS route's contract. Every refusal the route gave
    /// before it moved onto the service keeps its status, its <c>reasonCode</c> and its body builder:
    /// <list type="bullet">
    /// <item>a caller Dataverse cannot name: the single 403 <see cref="CallerUnresolvedReasonCode"/>, its words unchanged;</item>
    /// <item>a question asked as the caller that failed at Dataverse: the caller's own status, code and message
    /// (<see cref="CallerFailure"/>) — an upstream 401 stays 401 here (task 017 notes, K-4);</item>
    /// <item>a record the row names that is missing or not the caller's to use: the uniform not-found
    /// (<see cref="ParentNotFound"/>), one body whatever the cause;</item>
    /// <item>a server-owned column, or the caller's own rights: 403 <see cref="DeniedCode"/>;</item>
    /// <item>a payload the route does not accept: 400 <see cref="InvalidPayloadCode"/>;</item>
    /// <item>a pre-write rule's coded refusal (the invoice-family guard): its own code, 400 — 500 when it could not decide —
    /// and "The {noun} was not saved: …", <see cref="InvoiceFamilyRefused"/>'s shape;</item>
    /// <item>the secure-create plan's 403 and 500 codes, and an isolated root removed again: their codes with the service's
    /// words (the route's own, <see cref="PlanRefused"/>);</item>
    /// <item>no owner: 409 through <see cref="ProblemDetailsHelper.RecordOwnerRefused(string?, string?, string, string?)"/>,
    /// with <c>traceId</c>;</item>
    /// <item>no decision: 500 <see cref="NoDecisionCode"/>.</item>
    /// </list>
    /// A refusal the route never gave — a check that faulted (503, NFR-01), a number conflict — is rendered by the shared map
    /// (<see cref="RecordCreationProblemMap"/>, ADR-019).
    /// </summary>
    internal static IResult RefusalResult(RecordCreationRefusal refusal, string entity, string traceId)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var noun = Noun(entity);
        return refusal switch
        {
            { Kind: RecordCreationRefusalKind.PrincipalUnresolved } => Problem(StatusCodes.Status403Forbidden,
                CallerUnresolvedReasonCode, $"You could not be identified in Dataverse, so the {noun} was not created."),
            { Kind: RecordCreationRefusalKind.Upstream, ClientFailure: { } failure } => CallerFailure(failure, entity),
            { Kind: RecordCreationRefusalKind.NotFoundOrDenied } => ParentNotFound(entity),
            { Kind: RecordCreationRefusalKind.BlockedColumn } => Problem(StatusCodes.Status403Forbidden, DeniedCode, refusal.Detail),
            { Kind: RecordCreationRefusalKind.Forbidden, Code: RecordCreationRefusal.DeniedCode } =>
                Problem(StatusCodes.Status403Forbidden, DeniedCode, refusal.Detail),
            { Kind: RecordCreationRefusalKind.Invalid, Code: RecordCreationRefusal.InvalidPayloadCode } =>
                Problem(StatusCodes.Status400BadRequest, InvalidPayloadCode, refusal.Detail),
            { Kind: RecordCreationRefusalKind.Invalid, Code: RecordCreationRefusal.TableUnsupportedCode } =>
                Problem(StatusCodes.Status400BadRequest, UnsupportedTableCode, $"'{entity}' records are not created here."),
            { Kind: RecordCreationRefusalKind.Invalid or RecordCreationRefusalKind.Undecided, PreWrite.Code: not null } =>
                Problem(refusal.Kind == RecordCreationRefusalKind.Undecided
                        ? StatusCodes.Status500InternalServerError
                        : StatusCodes.Status400BadRequest,
                    refusal.Code, $"The {noun} was not saved: {refusal.Detail}"),
            { Kind: RecordCreationRefusalKind.Undecided, Code: RecordCreationRefusal.NoDecisionCode } =>
                Problem(StatusCodes.Status500InternalServerError, NoDecisionCode, "The record was not created."),
            { Kind: RecordCreationRefusalKind.Forbidden } => Problem(StatusCodes.Status403Forbidden, refusal.Code, refusal.Detail),
            { Kind: RecordCreationRefusalKind.Undecided } =>
                Problem(StatusCodes.Status500InternalServerError, refusal.Code, refusal.Detail),
            { Kind: RecordCreationRefusalKind.OwnerUnresolved, OwnerDecision: { IsForbidden: true } forbidden } =>
                ProblemDetailsHelper.RecordOwnerRefused(forbidden, noun, traceId),
            { Kind: RecordCreationRefusalKind.OwnerUnresolved } =>
                ProblemDetailsHelper.RecordOwnerRefused(refusal.Code, refusal.Detail, noun, traceId),
            _ => RecordCreationProblemMap.ToResult(refusal, entity, traceId),
        };
    }

    /// <summary>
    /// Ontology task 046: the secure-create plan's refusal (task 158 r1), in this route's contract. No AppendTo on a secure
    /// parent named only by the polymorphic pair is the uniform not-found (a typed lookup's is, too — the plan must not tell
    /// a caller which record is secure; the core (<see cref="CallerWriteAuthorization.PairTargetAsync"/>, #1612) decides the pair before
    /// the plan runs, so this arm is defence in depth); a caller walled off a secure parent is 403; a check that could not be made or a
    /// named team that could not be resolved is 500; a server-only column is 403; anything else (an unreadable parent flag)
    /// is the owner refusal, 409 — never "not secure". The split is <c>RecordCreationService.KindFor</c>'s.
    /// </summary>
    internal static IResult PlanRefused(RecordOwnerResolution plan, string entity, string traceId) => plan.RefusalCode switch
    {
        DataverseUserClientErrorCodes.AccessDenied => ParentNotFound(entity),
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccess
            or AccessFollowsParent.SecureFlagReasonCode
            or AccessInheritance.ServerOnlyReasonCode =>
            Problem(StatusCodes.Status403Forbidden, plan.RefusalCode!,
                $"The {Noun(entity)} was not created: {plan.Reason ?? "it may not be filed there"}."),
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable
            or RecordOwnerRefusal.SecureOwnerTeamUnresolved =>
            Problem(StatusCodes.Status500InternalServerError, plan.RefusalCode!,
                $"The {Noun(entity)} was not created: {plan.Reason ?? "whether it may be filed there could not be decided"}."),
        _ => ProblemDetailsHelper.RecordOwnerRefused(plan, Noun(entity), traceId),
    };

    /// <summary>
    /// ebilling-r1 FR-05 (ISS-010, #1973): the invoice-family guard's refusal, run by the write core
    /// (<see cref="OwnedChildWrite.CheckInvoiceFamilyAsync"/>), in this route's contract with the guard's own stable code as
    /// <c>reasonCode</c>. What the row names is wrong (none, two or three core records, a value that is not a lookup, an
    /// invoice superseding one of another core record): 400. A read the guard needed that could not be used: 500 (fail
    /// closed, never "not set"). A record that cannot be found: the uniform not-found.
    /// </summary>
    internal static IResult InvoiceFamilyRefused(InvoiceFamilyCoreRecord refusal, string entity) => refusal.RefusalCode switch
    {
        InvoiceFamilyCoreRecordGuard.RecordNotFoundCode => ParentNotFound(entity),
        InvoiceFamilyCoreRecordGuard.ReadFailedCode or InvoiceFamilyCoreRecordGuard.UnknownEntityCode =>
            Problem(StatusCodes.Status500InternalServerError, refusal.RefusalCode!, $"The {Noun(entity)} was not saved: {refusal.Reason}"),
        _ => Problem(StatusCodes.Status400BadRequest, refusal.RefusalCode ?? InvalidPayloadCode,
            $"The {Noun(entity)} was not saved: {refusal.Reason}"),
    };

    /// <summary>
    /// A WhoAmI failure that means the caller has no Dataverse identity — Dataverse refusing them, no user context on the
    /// request, or no On-Behalf-Of token for them — the codes <c>SignalCoreRecordAccess</c> reads as unresolved (D-29).
    /// Throttling, a 5xx or a transport fault says nothing about who they are and stays their own error.
    /// </summary>
    internal static bool IsUnresolvedCaller(DataverseUserResponse failure) =>
        failure.ErrorCode is DataverseUserClientErrorCodes.AccessDenied
            or DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboExchangeFailed
            or DataverseUserClientErrorCodes.OboNotConfigured;

    /// <summary>PATCH /api/v1/child-records/{table}/{id}.</summary>
    internal static Task<IResult> RefileAsync(
        string table,
        Guid id,
        [FromBody] JsonElement body,
        [FromServices] IDataverseUserClient user,
        [FromServices] IRecordOwnershipResolver ownership,
        [FromServices] CoreAncestorRestamper restamper,
        [FromServices] SecureChildReconciler children,
        [FromServices] IGenericEntityService appOnlyEntities,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var entity = (table ?? string.Empty).Trim().ToLowerInvariant();
        return RefileTables.Contains(entity)
            ? UpdateAsync(entity, id, body, user, ownership, restamper, children, httpContext, logger, ct,
                appOnlyEntities: appOnlyEntities)
            : Task.FromResult(Problem(StatusCodes.Status400BadRequest, UnsupportedTableCode,
                $"'{table}' records are not re-filed here."));
    }

    /// <summary>
    /// The ONE browser re-file, shared by <c>PATCH /api/v1/child-records/{table}/{id}</c>,
    /// <c>PATCH /api/v1/events/{id}/filing</c> and <c>PATCH /api/communications/{id}/filing</c>.
    /// </summary>
    /// <param name="filingOnly">The event and communication filing routes (owner round 36): the payload may name only filing
    /// columns (<see cref="FilingShapeProblem"/>) — the routes' shape filter asks first; the handler asks again, so the
    /// contract holds for any caller of this method. A navigation property is the schema name of its lookup column
    /// (<c>sprk_RegardingMatter</c> → <c>sprk_regardingmatter</c>, live metadata, task 159 note §0.2(b)), so naming only
    /// <c>sprk_regarding…</c> properties writes only <c>sprk_regarding…</c> columns; an unknown navigation property is the
    /// mapper's 400.</param>
    /// <param name="appOnlyEntities">ebilling-r1 D-96: the app-only reader an invoice's lines are counted with when an update
    /// moves the invoice to another core record. Null on the event and communication filing routes, which write no invoice;
    /// an invoice core-lookup change without it fails closed.</param>
    internal static async Task<IResult> UpdateAsync(
        string entity,
        Guid id,
        JsonElement body,
        IDataverseUserClient user,
        IRecordOwnershipResolver ownership,
        CoreAncestorRestamper restamper,
        SecureChildReconciler children,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct,
        bool filingOnly = false,
        IGenericEntityService? appOnlyEntities = null)
    {
        if (filingOnly && FilingShapeProblem(body, entity) is { } shape)
            return shape;

        var meta = await user.GetAsync(
            $"EntityDefinitions(LogicalName='{entity}')?$select=EntitySetName,PrimaryIdAttribute", ct).ConfigureAwait(false);
        if (!meta.IsSuccess)
            return CallerFailure(meta, entity);
        var entitySet = GetString(meta.Body, "EntitySetName");
        var primaryId = GetString(meta.Body, "PrimaryIdAttribute") ?? entity + "id";
        if (entitySet is null)
            return Problem(StatusCodes.Status500InternalServerError, "child_record.metadata", $"Table '{entity}' has no entity set.");

        // The caller must be able to READ the row; a row they cannot read and a row that does not exist get the same 404.
        // Its owning team rides along: a row that was isolated and is moved out loses its mirrored shares below.
        var row = await user.GetAsync($"{entitySet}({id:D})?$select={primaryId},_owningteam_value", ct).ConfigureAwait(false);
        if (!row.IsSuccess)
        {
            return row.StatusCode is StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound
                ? Problem(StatusCodes.Status404NotFound, NotFoundCode, $"The {Noun(entity)} was not found.")
                : CallerFailure(row, entity);
        }

        var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(user, entity, body, ct).ConfigureAwait(false);
        if (mapped.ValidationError is { } invalid)
            return Problem(StatusCodes.Status400BadRequest, InvalidPayloadCode, invalid);
        if (mapped.ClientFailure is { } mapFailure)
            return CallerFailure(mapFailure, entity);

        var serverOwned = mapped.Item!.Columns.FirstOrDefault(c =>
            OwnedChildWrite.ServerOwnedColumns.Contains(c) || RecordCreatorPerson.NamesColumn(c));
        if (serverOwned is not null)
        {
            return Problem(StatusCodes.Status403Forbidden, DeniedCode,
                $"Column '{serverOwned}' is set by the server — omit it.");
        }

        OwnedChildWrite.RefileOutcome outcome;
        try
        {
            outcome = await OwnedChildWrite.RefileAsync(
                user, ownership, entity, id, entitySet, primaryId, mapped.Item!, CallerObjectId(httpContext), ct, appOnlyEntities)
                .ConfigureAwait(false);
        }
        catch (RecordOwnerUnsettledException)
        {
            // #2143: a concurrent writer kept the re-filed row's owner from settling. The caller's PATCH was written and its
            // filing put back or superseded (RecordOwnerUnsettledException remarks): reload and retry — not the global 500.
            logger.LogWarning("[CHILD-RECORD] re-file of {Entity} {Id}: its owner could not be settled against concurrent writers",
                entity, id);
            return ProblemDetailsHelper.RecordOwnerUnsettled(Noun(entity), httpContext.TraceIdentifier);
        }

        if (outcome.NotARefile)
        {
            // An update that changes nothing the row is filed under: the caller's own PATCH, authorized by Dataverse.
            var patch = await user.PatchAsync($"{entitySet}({id:D})", mapped.Item!.JsonBody, ct).ConfigureAwait(false);
            if (!patch.IsSuccess)
                return CallerFailure(patch, entity);
        }
        else if (!outcome.Written)
        {
            logger.LogWarning(
                "[CHILD-RECORD] re-file of {Entity} {Id} refused: denied={Denied} forbidden={Forbidden} owner={OwnerCode} " +
                "family={FamilyCode} clientStatus={ClientStatus}", entity, id, outcome.Denied is not null, outcome.Forbidden is not null,
                outcome.OwnerRefusal?.RefusalCode, outcome.InvoiceFamilyRefusal?.RefusalCode, outcome.ClientFailure?.StatusCode);
            return outcome switch
            {
                { ClientFailure: { } failure } => CallerFailure(failure, entity),
                { Invalid: { } pairInvalid } => Problem(StatusCodes.Status400BadRequest, InvalidPayloadCode, pairInvalid),
                { ParentUnavailable: true } => ParentNotFound(entity),
                { InvoiceFamilyRefusal: { } family } => InvoiceFamilyRefused(family, entity),
                { Denied: { } denied } => Problem(StatusCodes.Status403Forbidden, DeniedCode, denied),
                { Forbidden: { } forbidden } => ProblemDetailsHelper.RecordOwnerRefused(forbidden, Noun(entity), httpContext.TraceIdentifier),
                { OwnerRefusal: { } refusal } => ProblemDetailsHelper.RecordOwnerRefused(refusal, Noun(entity), httpContext.TraceIdentifier),
                { SecureFilingRefused: { } secure } => ProblemDetailsHelper.RecordOwnerRefused(secure, Noun(entity), httpContext.TraceIdentifier),
                _ => Problem(StatusCodes.Status500InternalServerError, "child_record.no_decision", "The record was not updated."),
            };
        }

        var restamp = await restamper.AfterWriteAsync(entity, id, mapped.Item!.Columns, CancellationToken.None).ConfigureAwait(false);
        if (!restamp.Complete)
        {
            logger.LogWarning(
                "[CHILD-RECORD] updated {Entity} {Id}, but the core-ancestor re-stamp did not finish (failures={Failures}); " +
                "the stamp job completes it within one cycle", entity, id, restamp.Failures.Count);
        }

        // Task 173 (owner rounds 81/84): after a re-file, or a caller's own update of the column on a record that has a
        // parent (locked on the form), the row shows what its parents give it now. A parentless row is left as written.
        // Task 189: a document shows max(its own narrowing, what its parents give it); its own narrowing is never widened.
        if (outcome.Written || mapped.Item!.Columns.Contains(InheritedAccessPermission.Column, StringComparer.OrdinalIgnoreCase))
            await restamper.RefreshInheritedAccessPermissionAsync(entity, id, CancellationToken.None).ConfigureAwait(false);

        // #2091: not when the reparent moved none of the row's parents (ParentsUnchanged — a re-bind onto the record a column
        // already holds): written, but no re-file.
        if (outcome.Written && !outcome.ParentsUnchanged)
        {
            // After the re-file, inline (task 147; round 36): the row's own mirror (moved under a secure record → shared with
            // its sharees now; moved OUT → its mirror goes now, only from a row that WAS isolated — its owning team, read as
            // the caller before the write, rides along on `row`), then task 148's pass over everything filed under it when
            // it moved under, out of or between secure records. One implementation for every re-file writer (it also lets a
            // re-filed communication's thread follow the filing — spaarkeai-word-add-in-r1 task 129).
            // #2143 round 7 (F1): with the caller the re-file's F3 check acted for, so the thread that follows moves on it.
            await children.AfterRefileAsync(
                entity, id,
                async () => GetString(row.Body, "_owningteam_value") is { } before && Guid.TryParse(before, out var beforeTeam)
                            && await children.IsSecureOwnerTeamAsync(beforeTeam, CancellationToken.None).ConfigureAwait(false),
                RefileThreadStep.Reconcile, outcome.SecureExitCaller, outcome.Resolution,
                CancellationToken.None).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    private static Guid? CallerObjectId(HttpContext httpContext) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(httpContext.User), out var oid) && oid != Guid.Empty ? oid : null;

    /// <summary>A failure the caller's own token met at Dataverse — their own status and message (never escalated).</summary>
    private static IResult CallerFailure(DataverseUserResponse response, string entity) =>
        response.StatusCode is >= 400 and < 600
            ? Problem(response.StatusCode, response.ErrorCode ?? "child_record.dataverse",
                response.ErrorMessage ?? $"The {Noun(entity)} could not be saved.")
            : Problem(StatusCodes.Status502BadGateway, response.ErrorCode ?? "child_record.dataverse",
                response.ErrorMessage ?? $"The {Noun(entity)} could not be saved.");

    private static IResult ParentNotFound(string entity) =>
        Problem(StatusCodes.Status404NotFound, NotFoundCode,
            $"A record this {Noun(entity)} is filed under was not found. The {Noun(entity)} was not saved.");

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: status switch
        {
            StatusCodes.Status400BadRequest => "Invalid request",
            StatusCodes.Status403Forbidden => "Not permitted",
            StatusCodes.Status404NotFound => "Not found",
            _ => "Record not saved",
        }, detail: detail, extensions: new Dictionary<string, object?> { ["reasonCode"] = code });

    /// <summary>The user-facing noun for a table, in the refusal messages.</summary>
    internal static string Noun(string entity) => entity switch
    {
        "sprk_todo" => "to-do",
        "sprk_event" => "event",
        "sprk_memo" => "memo",
        "sprk_invoice" => "invoice",
        "sprk_reportcard" => "report card",
        "sprk_analysis" => "analysis",
        "sprk_document" => "document",
        "sprk_communication" => "communication",
        "sprk_budget" => "budget",
        "sprk_kpiassessment" => "KPI assessment",
        "sprk_billingevent" => "billing event",
        "sprk_workassignment" => "work assignment",
        _ => "record",
    };

    private static string? GetString(JsonElement? element, string property) =>
        element is { } e && e.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
