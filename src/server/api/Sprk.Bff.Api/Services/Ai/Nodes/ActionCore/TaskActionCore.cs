using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

// ---------------------------------------------------------------------------
// Session-agnostic Layer-A action seam — CreateTask (task 031 / FR-07).
//
// Creates a Spaarke TASK, which in this platform is a `sprk_event` record with
// event type = Task (sprk_eventtype_ref → the Task event-type ref) — NOT the OOB
// Dataverse `task` activity. Corrected 2026-08-06 (operator directive; email-
// communication-intelligence-r2): the prior implementation created new Entity("task"),
// an OOB activity that never appears in any sprk_event task view and cannot hold the
// sprk_event date fields (sprk_duedate / sprk_finalduedate / sprk_basedate). The entire
// read/report model (DailyBriefingCollector, RegardingFieldMap, TodoGenerationService)
// is sprk_event type=task; this is the single write point that all task consumers
// (IActionSeam.CreateTaskAsync + CreateTaskNodeExecutor) funnel through.
//
// The executor renders ConfigJson templates then hands already-resolved typed values
// here; ActionSeam supplies typed values directly. The "degraded success"
// contract (a Dataverse rejection is swallowed and surfaced as Guid.Empty) is preserved.
// ---------------------------------------------------------------------------

/// <summary>Session-agnostic input for a task create — all values pre-rendered/typed.</summary>
/// <param name="ActingUserId">
/// unified-access-control-r2 task 152: the systemuser on whose behalf the task is created (the playbook's acting user,
/// the confirming user). Their LINKED contact (task 141) is the triggering person written to
/// <c>sprk_event.sprk_assignedto</c> when no assignee is supplied — the create is app-only, so Created By cannot say
/// who the task is for.
/// </param>
/// <param name="AssignedToContactId">A supplied assignee (contact). Never overwritten.</param>
internal sealed record TaskActionInput(
    string Subject,
    string? Description,
    DateTime? ScheduledEnd,
    Guid? RegardingObjectId,
    string? RegardingObjectType,
    Guid? OwnerId,
    Guid? ActingUserId = null,
    Guid? AssignedToContactId = null,
    /// <summary><c>sprk_finalduedate</c> — the OUTER bound, where <paramref name="ScheduledEnd"/>
    /// (<c>sprk_duedate</c>) is the target. Optional; defaulted so existing call sites are unaffected.</summary>
    DateTime? FinalDueDate = null,
    /// <summary>Task 146 c1-r1 (owner round 13 item 9): the systemuser who ASKED for the task (e.g. the user confirming
    /// a proposal) — recorded as the app-created task's creator person. Distinct from <paramref name="ActingUserId"/>,
    /// the person the task is FOR. Null: nobody asked (a playbook node) and nobody is recorded.</summary>
    Guid? RequestedBySystemUserId = null);

/// <summary>
/// Session-agnostic core that builds a <c>sprk_event</c> (event type = Task) and creates it, preserving the
/// executor's "degraded success" contract (a Dataverse rejection is swallowed and surfaced as
/// <see cref="Guid.Empty"/> rather than propagated). Constructed inline by the executor (from its
/// own injected fields) and by <c>ActionSeam</c> (from its own injected fields).
/// </summary>
internal sealed class TaskActionCore
{
    private const string EventEntity = "sprk_event";

    /// <summary>The Task event-type ref row id (source of truth: <c>sprk_eventtype_ref</c> records in spaarkedev1).
    /// Setting this lookup is what makes the created <c>sprk_event</c> a Task.</summary>
    private const string EventTypeRefEntity = "sprk_eventtype_ref";
    private static readonly Guid EventTypeTaskId = new("124f5fc9-98ff-f011-8406-7c1e525abd8b");

    /// <summary>
    /// <c>sprk_event.statuscode</c> = Open. MUST match <c>DailyBriefingCollector</c>'s own
    /// <c>EventStatusOpen</c> — the briefing's Upcoming/Overdue task channels filter on exactly this value,
    /// so a task created with any other status cannot be surfaced. Live option set: Draft(1) /
    /// Open(659490001) / Completed(659490002) / Cancelled(659490004).
    /// </summary>
    private const int EventStatusOpen = EventStatusCode.Open; // task 097 review F8: the one source of truth

    /// <summary>
    /// A caller-supplied regarding (polymorphic on the OOB task) maps to <c>sprk_event</c>'s TYPED regarding
    /// lookup for that target entity. This is <c>sprk_event</c>'s OWN regarding family — it differs from the
    /// <c>sprk_communication</c> <c>RegardingFieldMap</c> (e.g. <c>contact</c> → <c>sprk_regardingcontact</c> here vs
    /// <c>sprk_regardingperson</c> there), so it is NOT reused. The complete family, verified against the deployed
    /// <c>sprk_event</c> schema — including <c>sprk_regardingcommunication</c> (a task CAN regard the communication
    /// it follows up).
    /// <para>
    /// 2026-09-04: the schema's <c>sprk_regardingorganziation</c> misspelling (transposed z/i) was CORRECTED in
    /// Dataverse to <c>sprk_regardingorganization</c>; the typo'd column no longer exists. This map and the
    /// prior "do not fix it in code" instruction were updated with it. Confirmed via
    /// <c>describe('tables/sprk_event')</c>. Fixed while pre-deployment, when a Dataverse logical name is still
    /// cheap to change — after a customer holds rows in a column, renaming means a live-data migration.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RegardingFieldByEntity =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_regardingmatter",
            ["sprk_project"] = "sprk_regardingproject",
            ["sprk_invoice"] = "sprk_regardinginvoice",
            ["sprk_servicerequest"] = "sprk_regardingservicerequest",
            ["sprk_workassignment"] = "sprk_regardingworkassignment",
            ["sprk_analysis"] = "sprk_regardinganalysis",
            ["sprk_budget"] = "sprk_regardingbudget",
            ["sprk_reportcard"] = "sprk_regardingreportcard",
            ["sprk_event"] = "sprk_regardingevent",
            ["sprk_communication"] = "sprk_regardingcommunication",
            ["sprk_organization"] = "sprk_regardingorganization",
            ["sprk_recordtype_ref"] = "sprk_regardingrecordtype",
            ["account"] = "sprk_regardingaccount",
            ["contact"] = "sprk_regardingcontact",
        };

    /// <summary>
    /// <c>sprk_event.sprk_regardingrecordname</c>'s live maximum length (spaarkedev1 describe, read-only, 2026-10-02:
    /// NVARCHAR(1000)). A longer name would make Dataverse refuse the whole create (degraded Guid.Empty), so it is capped —
    /// the <c>InvoiceReviewService</c> precedent for a resolver name.
    /// </summary>
    internal const int RegardingRecordNameMaxLength = 1000;

    private readonly IGenericEntityService _entityService;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IIdentityNormalizationService _identity;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly ILogger _logger;

    /// <param name="recordTypes">
    /// The <c>sprk_recordtype_ref</c> lookup the regarding pair's type comes from — the same source every SDK-path
    /// regarding builder uses (<see cref="Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder"/>; owner round 8 item 2).
    /// </param>
    public TaskActionCore(
        IGenericEntityService entityService,
        CoreAncestorResolver coreAncestors,
        IRecordOwnershipResolver ownership,
        IIdentityNormalizationService identity,
        ICommunicationDataverseService recordTypes,
        ILogger logger)
    {
        _entityService = entityService;
        _coreAncestors = coreAncestors;
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _identity = identity;
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _logger = logger;
    }

    /// <summary>The calendar date of <paramref name="value"/> as written (its own <see cref="DateTime.Kind"/>), at
    /// midnight with <see cref="DateTimeKind.Unspecified"/> — what a Date Only column stores unchanged (task 098).</summary>
    internal static DateTime AsCalendarDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    /// <summary>
    /// Builds and creates the <c>sprk_event</c> (event type = Task) record. Returns the created id, or
    /// <see cref="Guid.Empty"/> when the Dataverse create is rejected (degraded success — the payload was
    /// assembled correctly).
    /// </summary>
    public async Task<Guid> CreateAsync(TaskActionInput input, CancellationToken cancellationToken)
    {
        var entity = new Entity(EventEntity);
        entity["sprk_eventname"] = input.Subject;
        // Event type = Task — the discriminator that makes this sprk_event a task.
        entity["sprk_eventtype_ref"] = new EntityReference(EventTypeRefEntity, EventTypeTaskId);
        // 🔴 Added 2026-09-29 (spaarke-ontology-platform-r1): set the status EXPLICITLY to Open.
        // Without this the row took sprk_event's default statuscode of 1 (Draft), and
        // DailyBriefingCollector's task channels filter `sprk_eventtype_ref = Task AND statuscode = Open
        // (659490001)` — so EVERY task this core created was invisible to the briefing that exists to
        // surface it. 49 sprk_event rows in spaarkedev1 were sitting in Draft when this was found.
        // "Draft" means a task whose authoring is unfinished; a task the system creates FOR someone to act
        // on is Open by definition. Open pairs with statecode 0 (Active) — verified against the live
        // option set, which is Draft(1) / Open(659490001) / Completed(659490002) / Cancelled(659490004).
        entity["statuscode"] = new OptionSetValue(EventStatusOpen);

        if (input.Description is not null)
            entity["sprk_description"] = input.Description;

        // Task 098: sprk_duedate / sprk_finalduedate are Date Only. The SDK stores the date part of a Utc/Unspecified
        // DateTime as written but converts a Local one to UTC first (22:00 EDT on 10-20 was stored as 10-21 — probed
        // live 2026-10-05), so the value is pinned to its calendar date at midnight, Unspecified.
        if (input.ScheduledEnd.HasValue)
            entity["sprk_duedate"] = AsCalendarDate(input.ScheduledEnd.Value);

        // sprk_finalduedate is the OUTER bound. DailyBriefingCollector reads it FIRST and falls back to
        // sprk_duedate, and its task channels filter by date -- a task with neither set cannot surface.
        if (input.FinalDueDate.HasValue)
            entity["sprk_finalduedate"] = AsCalendarDate(input.FinalDueDate.Value);

        if (input.RegardingObjectId.HasValue && !string.IsNullOrWhiteSpace(input.RegardingObjectType))
        {
            if (RegardingFieldByEntity.TryGetValue(input.RegardingObjectType, out var regardingField))
            {
                entity[regardingField] = new EntityReference(input.RegardingObjectType, input.RegardingObjectId.Value);

                // Owner decisions round 8 item 2 (unified-access-control-r2 task 156, F-051-6): the standard ADR-024
                // regarding PAIR — id, name, url and, when a sprk_recordtype_ref row exists, type — written by the SAME
                // builder the to-do writers use (TodoRegardingBuilder.ApplyResolverPairAsync), never a copy of it. Before,
                // this core wrote the typed lookup alone, so a later form clear of that lookup left the row's stamp copy
                // with nothing saying it was one: the core-ancestor reconciliation job could not find it and the old
                // root's access stayed granted. With the pair's id the job finds the cleared record and clears the
                // orphaned copy. New rows only; nothing is backfilled. Skipped for a sprk_recordtype_ref target, whose
                // typed lookup IS the pair's type column.
                if (!string.Equals(regardingField, TodoRegardingBuilder.FieldRegardingRecordType, StringComparison.OrdinalIgnoreCase))
                {
                    var regardingName = await ReadRegardingNameAsync(
                        input.RegardingObjectType, input.RegardingObjectId.Value, cancellationToken).ConfigureAwait(false);
                    await new TodoRegardingBuilder(_recordTypes, _coreAncestors, _logger)
                        .ApplyResolverPairAsync(
                            entity, input.RegardingObjectType, input.RegardingObjectId.Value, regardingName, cancellationToken)
                        .ConfigureAwait(false);
                }

                // FR-26 core-ancestor stamp (task 052) - applied after the typed lookup. Four of the
                // regarding types above are child-class (invoice, analysis, event, communication), so a
                // playbook can already create a task regarding a communication; without the stamp that
                // task inherits nothing from the communication's matter and is invisible to everyone
                // whose access comes from there.
                var stamp = await _coreAncestors
                    .StampAsync(entity, input.RegardingObjectType, input.RegardingObjectId.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (!stamp.Succeeded)
                {
                    // NFR-01 fail-closed, expressed in THIS class's existing error contract: the
                    // "degraded success" Guid.Empty that a Dataverse rejection already returns. The task
                    // is NOT created - an unstamped task would be silently unreachable rather than
                    // visibly absent.
                    _logger.LogWarning(
                        "CreateTask: core-ancestor derivation failed for {RegardingType} {RegardingId}; " +
                        "refusing to create an unstamped sprk_event (FR-26 / NFR-01). {Error}",
                        input.RegardingObjectType, input.RegardingObjectId.Value, stamp.Error);
                    return Guid.Empty;
                }
            }
            else
            {
                // sprk_event has no typed regarding lookup for this entity — record the fact rather than
                // silently mis-filing onto the wrong lookup. The task is still created (degraded regarding).
                _logger.LogWarning(
                    "CreateTask: no sprk_event regarding lookup for entity type '{RegardingType}'; creating the task without a regarding.",
                    input.RegardingObjectType);
            }
        }

        // Task 146 (C10 part 2): the task is a CHILD of what it regards, so its owner comes from the ONE resolver over
        // every parent lookup now on the row (the typed regarding plus the FR-26 core-ancestor stamps just applied) —
        // the named Secure team when any of them is secure, else the primary parent's business-unit team. A
        // caller-supplied OwnerId no longer becomes the owner when the task has a parent: the person a task is FOR is
        // task 152's Assigned To (written below). With NO parent, the supplied owner — else the acting user — is the
        // user whose business unit owns it (I-6; owner round 5: a BFF-created row goes to the creating identity's
        // business-unit team); with neither, nothing is created.
        var regardingParent = input.RegardingObjectId is { } rid && !string.IsNullOrWhiteSpace(input.RegardingObjectType)
            ? new RecordOwnershipParent(input.RegardingObjectType!, rid)
            : null;
        var ownerContext = RecordOwnershipContext.ForChild(entity, regardingParent) with
        {
            CallerSystemUserId = input.OwnerId ?? input.ActingUserId,
            RequestedBy = RecordRequester.Of(input.RequestedBySystemUserId), // task 146 c1-r1
        };

        // A Dataverse fault here PROPAGATES (it is not a refusal, PR #1045 F2) — the callers' own catch turns it into
        // their error; only a refusal takes the degraded-success branch below.
        var owner = await _ownership.ResolveOwnerAsync(ownerContext, cancellationToken).ConfigureAwait(false);
        if (!owner.IsOwned)
        {
            // Fail closed in THIS class's existing error contract (the degraded-success Guid.Empty): nothing is
            // created, never an app-owned task in the root business unit.
            _logger.LogWarning(
                "CreateTask: refusing to create the sprk_event — no owner resolved ({Code}: {Reason}) (task 146).",
                owner.RefusalCode, owner.Reason);
            return Guid.Empty;
        }

        if (input.OwnerId.HasValue && ownerContext.HasParent)
        {
            _logger.LogInformation(
                "CreateTask: the supplied owner {OwnerId} is not the owner of a task filed to a record; the record's "
                + "team {TeamId} owns it (task 146). The assignee belongs in Assigned To (task 152).",
                input.OwnerId.Value, owner.OwningTeamId);
        }

        entity["ownerid"] = new EntityReference("team", owner.OwningTeamId!.Value);
        owner.StampCreatorOn(entity); // task 146 c1-r1 — the person who asked, when one did

        // Task 152 (#1044 split agreed with word-add-in-r1): the task names the PERSON it is for. A supplied assignee
        // wins; otherwise the acting user's linked contact; otherwise the regarding parent's responsible internal
        // contact; otherwise blank + todo_unassigned. Never a team, never an email match.
        if (input.AssignedToContactId is { } supplied && supplied != Guid.Empty)
        {
            entity[AssignedToDefaults.AssignedToAttribute] = new EntityReference("contact", supplied);
        }

        var triggeringContactId = await ResolveLinkedContactAsync(input.ActingUserId, cancellationToken).ConfigureAwait(false);
        await AssignedToDefaults.ApplyAsync(
            _entityService,
            entity,
            triggeringContactId,
            input.RegardingObjectType,
            input.RegardingObjectId,
            _logger,
            cancellationToken).ConfigureAwait(false);

        try
        {
            return await _entityService.CreateAsync(entity, cancellationToken);
        }
        catch (Exception createEx)
        {
            _logger.LogWarning(
                createEx,
                "Dataverse sprk_event (task) creation failed: {Error}",
                createEx.Message);

            // Return a degraded success — the task payload was assembled correctly
            // but Dataverse rejected it.
            return Guid.Empty;
        }
    }

    /// <summary>
    /// The regarding record's display name for the pair's <c>sprk_regardingrecordname</c>: its primary-name column from the
    /// shared <see cref="RegardingNameFields"/> map, read app-only like this core's other reads, capped at
    /// <see cref="RegardingRecordNameMaxLength"/>. For display only, so never fatal: a type the map does not cover (a report
    /// card) or a read that fails yields <see langword="null"/>, which the builder writes as an empty name — the builders'
    /// "empty when unknown" convention. The pair's id, which F-051-6 detection uses, never depends on it.
    /// </summary>
    private async Task<string?> ReadRegardingNameAsync(string regardingType, Guid regardingId, CancellationToken ct)
    {
        if (RegardingNameFields.PrimaryNameField(regardingType) is not { } nameField)
        {
            return null;
        }

        try
        {
            var row = await _entityService.RetrieveAsync(regardingType, regardingId, [nameField], ct).ConfigureAwait(false);
            var name = row?.GetAttributeValue<string>(nameField)?.Trim();
            return string.IsNullOrEmpty(name)
                ? null
                : name.Length > RegardingRecordNameMaxLength ? name[..RegardingRecordNameMaxLength] : name;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "CreateTask: the regarding {RegardingType} {RegardingId}'s name could not be read; the pair's name is left empty",
                regardingType, regardingId);
            return null;
        }
    }

    /// <summary>
    /// The acting user's LINKED contact (task 141's <c>PersonIdentity.ContactId</c> — never an email match), or null
    /// when there is no acting user, no link, or the identity read failed.
    /// </summary>
    private async Task<Guid?> ResolveLinkedContactAsync(Guid? actingUserId, CancellationToken ct)
    {
        if (actingUserId is not { } userId || userId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var identity = await _identity.ResolveAsync(userId, ct).ConfigureAwait(false);
            return identity.ContactId;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CreateTask: the acting user's linked contact could not be resolved for {ActingUserId}; falling back to the parent's responsible contact",
                userId);
            return null;
        }
    }
}
