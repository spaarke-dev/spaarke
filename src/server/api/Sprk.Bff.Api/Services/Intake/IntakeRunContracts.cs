using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Intake.Harnesses;

namespace Sprk.Bff.Api.Services.Intake;

// The intake run's stored shape (spaarke-intake-pipeline-r1 task 013, FR-07). Every value is an id, a key, a token, a
// number or a time — never document text, classification reasoning or plan content (ADR-015): the state is stored on
// the run row (sprk_processingjob.sprk_stagestatus, 10 000 characters) and read by operators.

/// <summary>The ten stages of an intake run (spec FR-07), in order. The name is what <c>sprk_currentstage</c> shows.</summary>
public enum IntakeRunStage
{
    Receive = 1,
    Store = 2,
    Classify = 3,
    Route = 4,
    Analyse = 5,
    Build = 6,
    Persist = 7,
    Approve = 8,
    Return = 9,
    Record = 10,
}

/// <summary><c>sprk_servicerequest.sprk_intakestage</c> option values (notes/decisions.md 2026-11-27). Written by the BFF only.</summary>
public static class IntakeRequestStages
{
    public const int Submitted = 100000000;
    public const int InReview = 100000001;
    public const int NeedsTriage = 100000002;
    public const int AwaitingApproval = 100000003;
    public const int Returned = 100000004;
    public const int Closed = 100000005;
}

/// <summary><c>sprk_servicerequest.sprk_intakechannel</c> option values (notes/decisions.md 2026-11-27).</summary>
public static class IntakeChannels
{
    public const int FrontDoor = 100000000;
    public const int Console = 100000001;
    public const int Email = 100000002;
    public const int Copilot = 100000003;
}

/// <summary>
/// The closed codes a run is held or triaged with. Shown as admin-attention text (<c>sprk_errorcode</c>,
/// <c>sprk_errormessage</c>) and logged; never content.
/// </summary>
public static class IntakeRunCodes
{
    public const string HarnessDisabled = "intake.harness_disabled";
    public const string AiDisabled = "intake.ai_disabled";
    public const string ReturnUnavailable = "intake.return_unavailable";
    public const string HarnessContract = "intake.harness_contract";
    public const string Exhausted = "intake.retries_exhausted";
    public const string ProcessingFailed = "intake.processing_failed";
    public const string NotAnIntakeRequest = "intake.not_an_intake_request";
    public const string ContentInvalid = "intake.content_invalid";

    // Triage reasons (FR-10).
    public const string TriageNoContent = "triage.no_content";
    public const string TriageUnreadable = "triage.content_unreadable";
    public const string TriageNotAgreement = "triage.not_an_agreement";
    public const string TriageLowConfidence = "triage.low_confidence";
    public const string TriageNoHarness = "triage.no_harness";
}

/// <summary>The classification the run stored (FR-07 stage 3). The model's reasoning is never stored.</summary>
public sealed record IntakeClassification(bool IsAgreement, string? SubDomain, double Confidence);

/// <summary>The routing decision the run stored (stage 4): a harness key, or a triage code.</summary>
public sealed record IntakeRouteDecision(string? RequestType, string? DocumentType, string? HarnessKey, string? TriageCode);

/// <summary>
/// An external harness step the run waits on. Written BEFORE the step is called (marker before send); <see cref="Dispatched"/>
/// is set once the call returned. A delivery that finds the mark without <see cref="Dispatched"/> (the dispatching delivery
/// crashed) calls the step again, and the dispatch is idempotent on the run id (task 014); one that finds it dispatched waits.
/// </summary>
public sealed record IntakeAwaitingStep(string Step, DateTimeOffset Since, bool Dispatched = false);

/// <summary>What the persist hook wrote (stage 7), by id.</summary>
public sealed record IntakePersistedOutputs(
    IReadOnlyDictionary<string, Guid> DocumentIdsByRole,
    IReadOnlyDictionary<string, Guid> ResultIdsByKind,
    Guid? OutputDocumentId,
    bool ApprovalRequired,
    IReadOnlyList<string> ReturnDocumentRoles);

/// <summary>Why a run stopped for an administrator.</summary>
public sealed record IntakeRunHold(string Stage, string Code, DateTimeOffset At);

/// <summary>How an external step's wait ends without a result (<c>IntakeRunStore.StopAwaitedStepAsync</c>, task 014).</summary>
public enum IntakeExternalStopKind
{
    /// <summary>Held for an administrator: status stays In Progress, nothing continues.</summary>
    Hold = 0,

    /// <summary>Failed for good: terminal Failed with the code; the request moves to Needs triage.</summary>
    Fail = 1,
}

/// <summary>Why and how the wait ends: a closed code, admin-attention text (ids and codes only), and the stage shown.</summary>
public sealed record IntakeExternalStop(IntakeExternalStopKind Kind, IntakeRunStage Stage, string Code, string Message);

/// <summary>The answer of <c>IntakeRunStore.StopAwaitedStepAsync</c>.</summary>
public enum IntakeExternalWriteResult
{
    /// <summary>The run was updated.</summary>
    Written = 0,

    /// <summary>The run is gone, terminal, not awaiting that dispatch (or not at all): nothing was written.</summary>
    NotApplicable = 1,

    /// <summary>A live delivery holds the run: nothing was written, the caller retries later.</summary>
    Busy = 2,
}

/// <summary>The delivery that holds the run (the atomic receive-side claim, ADR-004 A1 §4).</summary>
public sealed record IntakeRunClaim(string Owner, DateTimeOffset At);

/// <summary>
/// The run's stored state (<c>sprk_stagestatus</c>). Stage results are written here BEFORE the run advances, so a
/// resumed run reads them instead of computing them again (FR-07a).
/// </summary>
public sealed class IntakeRunState
{
    public int Version { get; set; } = 1;

    /// <summary>Stages whose result is stored, by name.</summary>
    public List<string> Completed { get; set; } = new();

    /// <summary>The run's input: the request's filed documents the door named at create; verified by Store.</summary>
    public List<Guid> ContentDocumentIds { get; set; } = new();

    public IntakeClassification? Classification { get; set; }

    public IntakeRouteDecision? Route { get; set; }

    /// <summary>Stored harness step results by step name (stages 5–6). A stored step never runs again.</summary>
    public Dictionary<string, IntakeHarnessResultReference> Steps { get; set; } = new(StringComparer.Ordinal);

    public IntakeAwaitingStep? Awaiting { get; set; }

    public IntakePersistedOutputs? Outputs { get; set; }

    /// <summary>Set by the approval endpoint (task 017) when the attorney approves; the run then continues at Return.</summary>
    public DateTimeOffset? ApprovedOn { get; set; }

    /// <summary>Markers written BEFORE a create or a send (FR-07): marker name → deterministic key passed to the step.</summary>
    public Dictionary<string, string> Markers { get; set; } = new(StringComparer.Ordinal);

    public IntakeRunHold? Held { get; set; }

    public IntakeRunClaim? Claim { get; set; }

    public bool IsCompleted(IntakeRunStage stage) => Completed.Contains(stage.ToString(), StringComparer.Ordinal);

    public void MarkCompleted(IntakeRunStage stage)
    {
        if (!IsCompleted(stage))
        {
            Completed.Add(stage.ToString());
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads the stored state; an empty column is a fresh state. A column that is not this shape throws.</summary>
    public static IntakeRunState Parse(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new IntakeRunState()
            : JsonSerializer.Deserialize<IntakeRunState>(json, Json) ?? new IntakeRunState();
}

/// <summary>The run row as one delivery read it.</summary>
public sealed record IntakeRunSnapshot
{
    public required Guid RunId { get; init; }
    public Guid? RequestId { get; init; }
    public int? JobType { get; init; }
    public int? Status { get; init; }
    public string? CurrentStage { get; init; }
    public string? CorrelationId { get; init; }
    public string? HarnessKey { get; init; }

    /// <summary>
    /// The engine dispatch the run waits on (<c>sprk_engineexecutionid</c>): the idempotency key of the engine request
    /// (<c>IntakeEngine:{runId}:{operation}:{dispatchAttempt}</c>, task 014), written by the orchestrator from the step's
    /// <see cref="Harnesses.IntakeHarnessStepOutcome.ExecutionId"/>. Null before any dispatch was recorded.
    /// </summary>
    public string? EngineExecutionId { get; init; }

    public required IntakeRunState State { get; init; }

    /// <summary>The row's <c>versionnumber</c> as read: every write of the delivery is conditional on it.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>The request row as one delivery read it.</summary>
public sealed record IntakeRequestSnapshot
{
    public required Guid RequestId { get; init; }

    /// <summary>Null for a row that is not an intake request (ontology's outbound inquiry rows): never processed.</summary>
    public int? IntakeStage { get; init; }

    public int? RequestType { get; init; }
    public int? Channel { get; init; }
    public Guid? CurrentRunId { get; init; }

    /// <summary>The <c>versionnumber</c> read, for the conditional stage write.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>
/// The ids-only payload of an <c>IntakeRun</c> job (ADR-004: no content in a payload). The correlation id travels on the
/// <see cref="Sprk.Bff.Api.Services.Jobs.JobContract"/> itself.
/// </summary>
public sealed record IntakeRunJobPayload(Guid RequestId, Guid RunId);

/// <summary>What a door's return receives (stage 9). Ids and keys only.</summary>
public sealed record IntakeReturnContext
{
    public required Guid RequestId { get; init; }
    public required Guid RunId { get; init; }

    /// <summary>The deterministic key the door's send is idempotent on (the marker written before the call).</summary>
    public required string IdempotencyKey { get; init; }

    public IReadOnlyDictionary<string, Guid> DocumentIdsByRole { get; init; } = new Dictionary<string, Guid>();
    public IReadOnlyList<string> ReturnDocumentRoles { get; init; } = Array.Empty<string>();
    public string? CorrelationId { get; init; }
}

/// <summary>
/// The Return stage's slot (FR-07 stage 9): one implementation per door, chosen by the request's
/// <c>sprk_intakechannel</c>. The door bodies are later tasks (Front Door 021, Console 022, email 043, Copilot 044); each
/// adds one class and one <c>AddScoped&lt;IIntakeDoorReturn, …&gt;</c> line in <c>JobProcessingModule</c>.
/// </summary>
/// <remarks>
/// The orchestrator writes the <c>return</c> marker before it calls <see cref="ReturnAsync"/> and passes its key; a
/// redelivered run calls the door again with the SAME key, so the door's send must be idempotent on it (marker before
/// send, FR-07; one return, FR-11b). No door registered for the channel holds the run (<see cref="IntakeRunCodes.ReturnUnavailable"/>):
/// nothing is reported as returned that was not. ADR-010: a genuine seam, four doors.
/// </remarks>
public interface IIntakeDoorReturn
{
    /// <summary>The <see cref="IntakeChannels"/> value this door returns through.</summary>
    int Channel { get; }

    Task ReturnAsync(IntakeReturnContext context, CancellationToken cancellationToken);
}
