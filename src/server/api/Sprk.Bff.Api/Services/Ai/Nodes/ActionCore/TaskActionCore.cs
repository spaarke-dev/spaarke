using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

// ---------------------------------------------------------------------------
// Session-agnostic Layer-A action seam — CreateTask (task 031 / FR-07).
//
// Extracted from CreateTaskNodeExecutor.ExecuteAsync's inline `task` Entity
// construction + degraded-success create, so a Dataverse task can be created
// WITHOUT a NodeExecutionContext. The executor renders ConfigJson templates
// then hands already-resolved typed values here; ActionSeam supplies typed
// values directly. Behavior (exact field set + degraded-success Guid.Empty on
// create failure) is byte-for-byte identical to the pre-031 executor —
// proven by task 030's characterization tests passing unmodified.
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
/// Session-agnostic core that builds the <c>task</c> entity and creates it, preserving the
/// executor's "degraded success" contract (a Dataverse rejection is swallowed and surfaced as
/// <see cref="Guid.Empty"/> rather than propagated). Constructed inline by the executor (from its
/// own injected fields) and by <c>ActionSeam</c> (from its own injected fields).
/// </summary>
internal sealed class TaskActionCore
{
    private readonly IGenericEntityService _entityService;
    private readonly ILogger _logger;

    public TaskActionCore(IGenericEntityService entityService, ILogger logger)
    {
        _entityService = entityService;
        _logger = logger;
    }

    /// <summary>
    /// Builds and creates the <c>task</c> record. Returns the created id, or <see cref="Guid.Empty"/>
    /// when the Dataverse create is rejected (degraded success — the payload was assembled correctly).
    /// </summary>
    public async Task<Guid> CreateAsync(TaskActionInput input, CancellationToken cancellationToken)
    {
        var entity = new Entity("task");
        entity["subject"] = input.Subject;
        if (input.Description is not null)
            entity["description"] = input.Description;

        if (input.ScheduledEnd.HasValue)
            entity["scheduledend"] = input.ScheduledEnd.Value;

        if (input.RegardingObjectId.HasValue && !string.IsNullOrWhiteSpace(input.RegardingObjectType))
            entity["regardingobjectid"] = new EntityReference(input.RegardingObjectType, input.RegardingObjectId.Value);

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
                "Dataverse task creation failed: {Error}",
                createEx.Message);

            // Return a degraded success — the task payload was assembled correctly
            // but Dataverse rejected it.
            return Guid.Empty;
        }
    }
}
