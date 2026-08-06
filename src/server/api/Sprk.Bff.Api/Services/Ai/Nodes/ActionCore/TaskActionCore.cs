using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

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
    /// <c>sprk_regardingperson</c> there), so it is NOT reused. Field names match the deployed schema exactly —
    /// including the schema's own <c>sprk_regardingorganziation</c> misspelling (do not "fix" it in code).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RegardingFieldByEntity =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_regardingmatter",
            ["sprk_project"] = "sprk_regardingproject",
            ["sprk_invoice"] = "sprk_regardinginvoice",
            ["sprk_workassignment"] = "sprk_regardingworkassignment",
            ["sprk_analysis"] = "sprk_regardinganalysis",
            ["sprk_budget"] = "sprk_regardingbudget",
            ["sprk_organization"] = "sprk_regardingorganziation", // schema spelling (SIC)
            ["sprk_recordtype_ref"] = "sprk_regardingrecordtype",
            ["account"] = "sprk_regardingaccount",
            ["contact"] = "sprk_regardingcontact",
        };

    private readonly IGenericEntityService _entityService;
    private readonly ILogger _logger;

    public TaskActionCore(IGenericEntityService entityService, ILogger logger)
    {
        _entityService = entityService;
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

        if (input.OwnerId.HasValue)
            entity["ownerid"] = new EntityReference("systemuser", input.OwnerId.Value);

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
