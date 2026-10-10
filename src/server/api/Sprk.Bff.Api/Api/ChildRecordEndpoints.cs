using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;

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
/// records the caller in <c>sprk_createdbyperson</c>. The core is <see cref="OwnedChildWrite.CreateAsync"/>, the one the
/// chat create tool uses (owner round 7 item 3). The core-ancestor stamp is then derived by the server
/// (<see cref="CoreAncestorRestamper"/>, WP-1): a stamp the client previewed is only a preview.</para>
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
/// (<see cref="CompleteRootCreateAsync"/>), then its "Assigned *" contacts get their access. Provisioning's own columns are
/// refused (<see cref="RootServerOwnedColumns"/>). The Create Work Assignment wizard posts its payload here; a decision's
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
        // isolation, task 158 r1) and this route completes it (CompleteRootCreateAsync).
        "sprk_workassignment",
    };

    /// <summary>
    /// Ontology task 046: the columns of a work assignment the server owns — its own SPE container and the secure business
    /// unit it is isolated in (provisioning, <c>SecureRecordRoot</c>) and its Access Permission (the cascade, task 175).
    /// None is field-secured (live, spaarkedev1 2026-10-10), so the G5 check would let an app-only create carry a caller's
    /// value: refused here. The secure flag and the access
    /// record are field-secured and refused by the G5 check and the secure-create plan (task 175).
    /// </summary>
    internal static readonly IReadOnlySet<string> RootServerOwnedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_containerid", "sprk_securitybu",
        // D-113 / task 175: a filed work assignment takes its Access Permission from its parent; the column is form-locked
        // but NOT field-secured, and nothing on the create path sets it from the parent, so a caller's value is refused.
        "sprk_accesspermission",
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
    internal static async Task<IResult> CreateAsync(
        string table,
        [FromBody] JsonElement body,
        [FromServices] IDataverseUserClient user,
        [FromServices] IRecordOwnershipResolver ownership,
        [FromServices] IFieldMappingDataverseService appOnly,
        [FromServices] CoreAncestorRestamper restamper,
        [FromServices] SecureChildShareSynchronizer shares,
        [FromServices] SecureRootFilingGate rootFiling,
        [FromServices] IServiceScopeFactory scopes,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var entity = (table ?? string.Empty).Trim().ToLowerInvariant();
        if (!CreateTables.Contains(entity))
            return Problem(StatusCodes.Status400BadRequest, UnsupportedTableCode, $"'{table}' records are not created here.");

        // Ontology task 046 (NFR-10, D-29): a caller who cannot be named in Dataverse is the single 403, before anything about
        // the payload or the records it binds is asked. WhoAmI needs no privilege, so only "no Dataverse identity" fails it.
        var who = await OwnedChildWrite.WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (who.Failure is { } whoFailure)
        {
            if (IsUnresolvedCaller(whoFailure))
            {
                logger.LogWarning("[CHILD-RECORD] create of {Entity} refused: the caller has no Dataverse identity ({ErrorCode})",
                    entity, whoFailure.ErrorCode);
                return Problem(StatusCodes.Status403Forbidden, CallerUnresolvedReasonCode,
                    $"You could not be identified in Dataverse, so the {Noun(entity)} was not created.");
            }

            return CallerFailure(whoFailure, entity);
        }

        var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(user, entity, body, ct).ConfigureAwait(false);
        if (mapped.ValidationError is { } invalid)
            return Problem(StatusCodes.Status400BadRequest, InvalidPayloadCode, invalid);
        if (mapped.ClientFailure is { } mapFailure)
            return CallerFailure(mapFailure, entity);

        var isRoot = SecureRootInheritance.Inherits(entity);
        if (isRoot && mapped.Item!.Columns.FirstOrDefault(RootServerOwnedColumns.Contains) is { } rootColumn)
        {
            return Problem(StatusCodes.Status403Forbidden, DeniedCode,
                $"Column '{rootColumn}' of a {Noun(entity)} is set by Spaarke — omit it.");
        }

        // Task 046 review F1: the ADR-024 pair (sprk_regardingrecordid) is text, so the G5 check asks AppendTo only on typed
        // lookups, and the secure-create plan asks it on a pair-only parent ONLY when that parent is secure — a pair naming a
        // record no typed lookup names would answer "exists and is secure" (404) differently from "missing or ordinary"
        // (201). On this route a root's pair must name a record its typed regarding lookup also names (the wizard always
        // sends both), so every parent is AppendTo-checked as the caller and no answer depends on whether it is secure.
        if (isRoot && PairNamesAnUntypedRecord(mapped.Item!))
        {
            return Problem(StatusCodes.Status400BadRequest, InvalidPayloadCode,
                $"'{SecureRootInheritance.PairIdColumn}' must name the same record as the {Noun(entity)}'s regarding lookup " +
                "(send both). Nothing was saved.");
        }

        // The secure-create plan is passed for every table; it decides only for a root (work assignment / project) and costs
        // nothing for a child.
        var owned = await OwnedChildWrite.CreateAsync(
            user, ownership, appOnly, entity, mapped.Item!, serverSetLookupColumns: null,
            CallerObjectId(httpContext), ct, rootFiling).ConfigureAwait(false);

        if (owned.CreatedId is not { } id)
        {
            logger.LogWarning(
                "[CHILD-RECORD] create of {Entity} refused: denied={Denied} parentUnavailable={ParentUnavailable} " +
                "owner={OwnerCode} plan={PlanCode} secureFiling={SecureFiling} clientStatus={ClientStatus}",
                entity, owned.Denied is not null, owned.ParentUnavailable, owned.OwnerRefusal?.RefusalCode,
                owned.PlanRefusal?.RefusalCode, owned.SecureFilingRefused is not null, owned.ClientFailure?.StatusCode);
            return owned switch
            {
                { ClientFailure: { } failure } => CallerFailure(failure, entity),
                { ParentUnavailable: true } => ParentNotFound(entity),
                { Denied: { } denied } => Problem(StatusCodes.Status403Forbidden, DeniedCode, denied),
                { PlanRefusal: { } plan } => PlanRefused(plan, entity, httpContext.TraceIdentifier),
                { OwnerRefusal: { } refusal } => ProblemDetailsHelper.RecordOwnerRefused(refusal, Noun(entity), httpContext.TraceIdentifier),
                { SecureFilingRefused: { } secure } => ProblemDetailsHelper.RecordOwnerRefused(
                    RecordOwnerRefusal.NoOwnerSource, secure, Noun(entity), httpContext.TraceIdentifier),
                _ => Problem(StatusCodes.Status500InternalServerError, "child_record.no_decision", "The record was not created."),
            };
        }

        var warnings = new List<string>();
        if (isRoot)
        {
            var completion = await CompleteRootCreateAsync(
                entity, id, owned, rootFiling, scopes, CallerObjectId(httpContext), httpContext.TraceIdentifier, logger, ct)
                .ConfigureAwait(false);
            if (completion.Removed is { } removed)
                return removed;
            warnings.AddRange(completion.Warnings);
        }

        // WP-1: the core-ancestor stamp is the server's. Whatever the client previewed, the row's own copy is derived from
        // what it is filed under now. Never thrown: a stamp that fails is logged and the stamp job repairs it within a cycle.
        var restamp = await restamper.AfterWriteAsync(entity, id, mapped.Item!.Columns, CancellationToken.None).ConfigureAwait(false);
        if (!restamp.Complete)
        {
            logger.LogWarning(
                "[CHILD-RECORD] created {Entity} {Id}, but its core-ancestor stamp did not finish (failures={Failures}); " +
                "the stamp job completes it within one cycle", entity, id, restamp.Failures.Count);
        }

        // Task 173 (owner round 81): a To Do, Event, Communication or Document filed under a record shows that record's
        // Access Permission from the moment it exists (a parentless one keeps the value its creator chose). Never thrown.
        await restamper.RefreshInheritedAccessPermissionAsync(entity, id, CancellationToken.None).ConfigureAwait(false);

        // Task 149's mirror, inline (task 147 r1): a child created under a secure record is shared with the record's
        // sharees now, not at the next two-minute reconcile. The create stands either way; a mirror that did not finish is
        // completed by SecureChildShareReconciliationJob (owner round 11 item 2). A root is not a secure child: an isolated
        // root got its parents' sharees from CompleteRootCreateAsync.
        if (SecureChildLineage.IsChild(entity))
            await MirrorAsync(shares, entity, id, logger).ConfigureAwait(false);

        logger.LogInformation("[CHILD-RECORD] created {Entity} {Id} (G5, team-owned, isolated={Isolated}, warnings={Warnings})",
            entity, id, owned.Isolated is not null, warnings.Count);
        return Results.Created($"/api/v1/child-records/{entity}/{id:D}",
            warnings.Count == 0 ? new { id } : (object)new { id, warnings });
    }

    /// <summary>What completing a root's create came to: a refusal when the row was removed again, otherwise warnings.</summary>
    internal sealed record RootCreateCompletion(IResult? Removed, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Ontology task 046: after the app-only create of a ROOT (a work assignment) — the steps the chat create tool runs after
    /// the same core (<c>DataverseCreateRecordHandler</c>, task 158 r1 and task 142). (1) A row created INTO isolation is
    /// completed now through provisioning's own re-entry steps: the creator's share (read back; a share that fails deletes
    /// the row again — then the create is refused, nothing is left), its own container, its secure parents' sharees. The
    /// person is the one the plan checked and the create stamped (<see cref="OwnedChildWrite.Outcome.IsolatedFor"/>),
    /// never asked again. The messages are <c>RecordCreationService</c>'s for an isolated project. (2) The "Assigned *"
    /// contacts get their grant or share now (I-12 L1; never throws — a fault is the job's).
    /// </summary>
    internal static async Task<RootCreateCompletion> CompleteRootCreateAsync(
        string entity, Guid id, OwnedChildWrite.Outcome owned, SecureRootFilingGate rootFiling, IServiceScopeFactory scopes,
        Guid? callerObjectId, string traceId, ILogger logger, CancellationToken ct)
    {
        var warnings = new List<string>();
        var noun = Noun(entity);
        if (owned.Isolated is not null)
        {
            var secured = await rootFiling.CompleteIsolatedCreateAsync(entity, id, owned.IsolatedFor!.Value, traceId)
                .ConfigureAwait(false);

            if (secured.RowRemoved)
            {
                logger.LogWarning("[CHILD-RECORD] isolated {Entity} {Id} removed again: its creator could not be shared ({Code})",
                    entity, id, secured.ReasonCode);
                return new RootCreateCompletion(Problem(StatusCodes.Status500InternalServerError,
                    secured.ReasonCode ?? SecureRootInheritance.ReasonUnexpectedResult,
                    $"The {noun} is filed under a secure record, so it is created secure and shared to you, and that share " +
                    $"could not be made, so the new {noun} was removed again. Nothing was created. " +
                    (secured.CompletesAutomatically
                        ? "Try again in a few minutes."
                        : $"Securing it was refused ({secured.ReasonCode}), and trying again will not change that: an " +
                          "administrator needs to review your access to the secure record it would be filed under.")),
                    Array.Empty<string>());
            }

            if (secured.RowStranded)
            {
                warnings.Add(
                    $"The {noun} was created as a secure record, but it could not be shared to you and could not be removed " +
                    $"again ({secured.ReasonCode}); " +
                    (secured.CompletesAutomatically
                        ? "it is shared to you automatically once that step succeeds (it is retried every few minutes)."
                        : "it will not be shared to you automatically — only an administrator can open it, and an administrator " +
                          "needs to review and remove it."));
            }
            else if (!secured.IsComplete)
            {
                warnings.Add(
                    $"The {noun} was created as a secure record shared to you, but securing it could not be finished yet " +
                    $"({secured.ReasonCode}); it is completed automatically within a few minutes.");
            }
        }

        await Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer.RunAfterWriteAsync(
                scopes, entity, id, writtenColumns: null, grantorOid: callerObjectId?.ToString("D"), logger, ct)
            .ConfigureAwait(false);

        return new RootCreateCompletion(null, warnings);
    }

    /// <summary>
    /// Ontology task 046: the secure-create plan's refusal (task 158 r1), in this route's contract. No AppendTo on a secure
    /// parent named only by the polymorphic pair is the uniform not-found (a typed lookup's is, too — the plan must not tell
    /// a caller which record is secure; on this route a root's pair-only parent is already refused as a 400 above, so this
    /// arm is defence in depth); a caller walled off a secure parent is 403; a check that could not be made or a
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
    /// Task 046 review F1: true when the row's ADR-024 pair id (<c>sprk_regardingrecordid</c>) is set and is not the id of a
    /// record one of its typed lookups binds — including a value that is not a record id at all.
    /// </summary>
    internal static bool PairNamesAnUntypedRecord(DataverseWriteItemMapper.MappedItem item)
    {
        var pair = OwnedChildWrite.WritesOf(item)
            .FirstOrDefault(w => string.Equals(w.Key, SecureRootInheritance.PairIdColumn, StringComparison.OrdinalIgnoreCase));
        if (pair.Key is null || pair.Value is not string text || string.IsNullOrWhiteSpace(text))
            return false;

        return !Guid.TryParse(text.Trim().Trim('{', '}'), out var id) || item.Lookups.All(l => l.RecordId != id);
    }

    /// <summary>
    /// A WhoAmI failure that means the caller has no Dataverse identity — Dataverse refusing them, no user context on the
    /// request, or no On-Behalf-Of token for them — the codes <c>SignalCoreRecordAccess</c> reads as unresolved (D-29).
    /// Throttling, a 5xx or a transport fault says nothing about who they are and stays their own error.
    /// </summary>
    private static bool IsUnresolvedCaller(DataverseUserResponse failure) =>
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
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var entity = (table ?? string.Empty).Trim().ToLowerInvariant();
        return RefileTables.Contains(entity)
            ? UpdateAsync(entity, id, body, user, ownership, restamper, children, httpContext, logger, ct)
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
        bool filingOnly = false)
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

        var outcome = await OwnedChildWrite.RefileAsync(
            user, ownership, entity, id, entitySet, primaryId, mapped.Item!, CallerObjectId(httpContext), ct)
            .ConfigureAwait(false);

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
                "clientStatus={ClientStatus}", entity, id, outcome.Denied is not null, outcome.Forbidden is not null,
                outcome.OwnerRefusal?.RefusalCode, outcome.ClientFailure?.StatusCode);
            return outcome switch
            {
                { ClientFailure: { } failure } => CallerFailure(failure, entity),
                { ParentUnavailable: true } => ParentNotFound(entity),
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
        if (outcome.Written || mapped.Item!.Columns.Contains(InheritedAccessPermission.Column, StringComparer.OrdinalIgnoreCase))
            await restamper.RefreshInheritedAccessPermissionAsync(entity, id, CancellationToken.None).ConfigureAwait(false);

        if (outcome.Written)
        {
            // After the re-file, inline (task 147; round 36): the row's own mirror (moved under a secure record → shared with
            // its sharees now; moved OUT → its mirror goes now, only from a row that WAS isolated — its owning team, read as
            // the caller before the write, rides along on `row`), then task 148's pass over everything filed under it when
            // it moved under, out of or between secure records. One implementation for every re-file writer.
            await children.AfterRefileAsync(
                entity, id,
                async () => GetString(row.Body, "_owningteam_value") is { } before && Guid.TryParse(before, out var beforeTeam)
                            && await children.IsSecureOwnerTeamAsync(beforeTeam, CancellationToken.None).ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    /// <summary>Task 149's mirror for one child; logs (never throws) when it did not finish.</summary>
    private static async Task<SecureChildShareSyncStatus> MirrorAsync(
        SecureChildShareSynchronizer shares, string entity, Guid id, ILogger logger)
    {
        try
        {
            var result = await shares.SyncChildAsync(entity, id, CancellationToken.None).ConfigureAwait(false);
            if (result.Status is SecureChildShareSyncStatus.Failed or SecureChildShareSyncStatus.Incomplete)
            {
                logger.LogWarning(
                    "[CHILD-RECORD] {Entity} {Id}: the secure record's sharees were not mirrored ({Status}: {Detail}); the " +
                    "two-minute reconcile completes it", entity, id, result.Status, result.Detail);
            }

            return result.Status;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[CHILD-RECORD] {Entity} {Id}: the share mirror faulted; the two-minute reconcile completes it",
                entity, id);
            return SecureChildShareSyncStatus.Failed;
        }
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
