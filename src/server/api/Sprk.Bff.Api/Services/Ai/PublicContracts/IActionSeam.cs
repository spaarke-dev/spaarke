namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013) for session-agnostic Layer-A record actions: create an
/// <c>appnotification</c>, create a <c>task</c>, or PATCH a Dataverse record — WITHOUT a
/// chat session or playbook run.
/// </summary>
/// <remarks>
/// <para>
/// Introduced by task 031 (FR-07). The three node executors
/// (<c>CreateNotificationNodeExecutor</c>/<c>CreateTaskNodeExecutor</c>/<c>UpdateRecordNodeExecutor</c>)
/// and this facade share the SAME extracted cores (<c>Services/Ai/Nodes/ActionCore/*</c>), so the
/// records produced here are identical to what the executors produce today — the executors just add
/// template rendering + ConfigJson parsing on top.
/// </para>
/// <para>
/// Per refined ADR-013 (2026-05-20), non-AI/CRUD code (Phase 4 comms-RI, Phase 5 suggestions) MUST
/// consume Layer-A record creation THROUGH this facade — never by injecting the node executors
/// directly, and never by synthesizing a <c>NodeExecutionContext</c> to satisfy an executor signature.
/// </para>
/// <para>
/// Failure contract: each method returns a typed result carrying <c>Success</c> + <c>Error</c> rather
/// than throwing for expected validation failures (missing required field, unmatchable Choice label).
/// This is at least as strict as the executors' failure handling today.
/// </para>
/// </remarks>
public interface IActionSeam
{
    /// <summary>
    /// Creates an <c>appnotification</c> for the given recipient (with the same idempotency
    /// skip-if-unread-duplicate behavior as the CreateNotification node). Returns a failure result
    /// when <see cref="CreateNotificationRequest.RecipientId"/> is absent (the seam has no run-context
    /// recipient fallback), never a throw or a silent no-op.
    /// </summary>
    Task<CreateNotificationResult> CreateNotificationAsync(
        CreateNotificationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a <c>task</c> record. Preserves the "degraded success" contract — a Dataverse
    /// rejection yields <see cref="Guid.Empty"/> in <see cref="CreateTaskResult.TaskId"/> rather than
    /// propagating the exception.
    /// </summary>
    Task<CreateTaskResult> CreateTaskAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PATCHes a Dataverse record from typed field mappings and/or legacy flat fields, applying the
    /// same coercion (Choice/Boolean/Number, metadata-driven fail-loud) as the UpdateRecord node.
    /// Returns a failure result (never a throw) for an unmatchable metadata-confirmed Choice value.
    /// </summary>
    Task<UpdateRecordResult> UpdateRecordAsync(
        UpdateRecordRequest request,
        CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------
// CreateNotification
// ---------------------------------------------------------------------------

/// <summary>Session-agnostic request to create an appnotification.</summary>
public sealed record CreateNotificationRequest
{
    public required string Title { get; init; }
    public required string Body { get; init; }

    /// <summary>Recipient systemuserid. Nullable so the seam can return a typed failure when absent
    /// (unlike the executor, the seam has no run-context recipient fallback).</summary>
    public Guid? RecipientId { get; init; }

    public string? Category { get; init; }

    /// <summary>Priority option-set value. Default 200000000 (Important) — matches the node default.</summary>
    public int Priority { get; init; } = 200_000_000;

    /// <summary>Toast option-set value. Default 200000000 (Timed) — matches the node default.</summary>
    public int ToastType { get; init; } = 200_000_000;

    public string? ActionUrl { get; init; }
    public Guid? RegardingId { get; init; }
    public string? RegardingType { get; init; }
    public string? DueDate { get; init; }

    // FR-6 enrichment (optional).
    public string? RegardingName { get; init; }
    public string? SourceEntityType { get; init; }
    public string? SourceId { get; init; }
    public string? SourceModifiedOn { get; init; }
    public string? SourceOwningUser { get; init; }
    public string? ViaMatterId { get; init; }
    public string? ViaMatterName { get; init; }
    public IReadOnlyList<object>? ViaMatterMemberships { get; init; }

    /// <summary>Written to <c>appnotification.sprk_source</c>. Non-playbook callers supply their own
    /// (the CreateNotification node passes "playbook"). Default "system".</summary>
    public string Source { get; init; } = "system";

    /// <summary>Written to <c>appnotification.sprk_playbookrunid</c> (the node passes its RunId). A
    /// correlation id for the originating operation; null → empty.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>Result of a <see cref="IActionSeam.CreateNotificationAsync"/> call.</summary>
public sealed record CreateNotificationResult(
    bool Success,
    Guid? NotificationId,
    bool Skipped,
    string? Error);

// ---------------------------------------------------------------------------
// CreateTask
// ---------------------------------------------------------------------------

/// <summary>Session-agnostic request to create a task.</summary>
public sealed record CreateTaskRequest
{
    public required string Subject { get; init; }
    public string? Description { get; init; }

    /// <summary>Due date; converted to UTC (matching the node's <c>ToUniversalTime()</c>).</summary>
    public DateTime? DueDate { get; init; }

    public Guid? RegardingObjectId { get; init; }
    public string? RegardingObjectType { get; init; }
    public Guid? OwnerId { get; init; }
}

/// <summary>Result of a <see cref="IActionSeam.CreateTaskAsync"/> call. <see cref="TaskId"/> is
/// <see cref="Guid.Empty"/> on degraded success (Dataverse rejected the create).</summary>
public sealed record CreateTaskResult(
    bool Success,
    Guid TaskId,
    string? Error);

// ---------------------------------------------------------------------------
// UpdateRecord
// ---------------------------------------------------------------------------

/// <summary>Public field-type discriminator for <see cref="ActionFieldMapping"/> (maps 1:1 to the
/// executor's internal FieldMappingType).</summary>
public enum ActionFieldType
{
    String,
    Choice,
    Boolean,
    Number,
    Lookup
}

/// <summary>A single typed field mapping. <see cref="Value"/> is the final resolved value (the seam
/// does no template rendering).</summary>
public sealed record ActionFieldMapping(
    string Field,
    ActionFieldType Type,
    string? Value,
    IReadOnlyDictionary<string, int>? Options = null);

/// <summary>A lookup field with a resolved target id.</summary>
public sealed record ActionLookup(
    string Field,
    string TargetEntity,
    string TargetId);

/// <summary>Session-agnostic request to update a Dataverse record. Provide <see cref="FieldMappings"/>
/// (preferred) and/or <see cref="LegacyFields"/>; mappings win when both are present.</summary>
public sealed record UpdateRecordRequest
{
    public required string EntityLogicalName { get; init; }
    public required Guid RecordId { get; init; }
    public IReadOnlyList<ActionFieldMapping>? FieldMappings { get; init; }
    public IReadOnlyDictionary<string, string?>? LegacyFields { get; init; }
    public IReadOnlyList<ActionLookup>? Lookups { get; init; }
}

/// <summary>Result of a <see cref="IActionSeam.UpdateRecordAsync"/> call. On a fail-loud coercion
/// failure, <see cref="Success"/> is false and <see cref="Error"/> carries the descriptive message
/// (no PATCH was issued).</summary>
public sealed record UpdateRecordResult(
    bool Success,
    IReadOnlyList<string> FieldsUpdated,
    string? Error);
