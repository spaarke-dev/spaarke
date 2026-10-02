using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

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
internal sealed record TaskActionInput(
    string Subject,
    string? Description,
    DateTime? ScheduledEnd,
    Guid? RegardingObjectId,
    string? RegardingObjectType,
    Guid? OwnerId);

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

    private readonly IGenericEntityService _entityService;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly ILogger _logger;

    public TaskActionCore(
        IGenericEntityService entityService,
        CoreAncestorResolver coreAncestors,
        IRecordOwnershipResolver ownership,
        ILogger logger)
    {
        _entityService = entityService;
        _coreAncestors = coreAncestors;
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger;
    }

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

        if (input.Description is not null)
            entity["sprk_description"] = input.Description;

        if (input.ScheduledEnd.HasValue)
            entity["sprk_duedate"] = input.ScheduledEnd.Value;

        if (input.RegardingObjectId.HasValue && !string.IsNullOrWhiteSpace(input.RegardingObjectType))
        {
            if (RegardingFieldByEntity.TryGetValue(input.RegardingObjectType, out var regardingField))
            {
                entity[regardingField] = new EntityReference(input.RegardingObjectType, input.RegardingObjectId.Value);

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
        // task 152's Assigned To (owner B2 accepts the interim briefing drop-out). With NO parent, that user is the
        // acting user whose business unit owns it (I-6); with neither, nothing is created.
        var regardingParent = input.RegardingObjectId is { } rid && !string.IsNullOrWhiteSpace(input.RegardingObjectType)
            ? new RecordOwnershipParent(input.RegardingObjectType!, rid)
            : null;
        var ownerContext = RecordOwnershipContext.ForChild(entity, regardingParent) with
        {
            CallerSystemUserId = input.OwnerId,
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
}
