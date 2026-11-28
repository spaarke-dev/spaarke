namespace Sprk.Bff.Api.Services.Intake.Harnesses;

// The intake harness contract records (spec FR-08: "takes request, run and content, and returns
// documents, results, an approval-required flag, a return payload and a persist hook"). Task 013 split the
// single RunAsync into named steps (IIntakeHarness remarks); the approval flag and return payload now come from
// the persist hook, because the outputs only exist once it has filed them.
// Every member is an id, a deterministic token or a staged-blob reference — never document bytes
// and never user-supplied text (NFR-03, ADR-015). The records are plain and serializable so the
// orchestrator can store a stage's result on the run and resume from it (FR-07).

/// <summary>The routing key a harness is chosen by: request type plus classified document type.</summary>
/// <param name="RequestType">Deterministic request-type token (e.g. <c>nda</c>).</param>
/// <param name="DocumentType">Classified document type (e.g. <c>nda</c>); null when not classified.</param>
public sealed record IntakeHarnessRoute(string RequestType, string? DocumentType);

/// <summary>One piece of submitted content, by reference.</summary>
public sealed record IntakeContentReference
{
    /// <summary>The <c>sprk_document</c> id when the content is already filed; otherwise null.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>Stamp-private staging blob path when the content is staged; otherwise null.</summary>
    public string? StagedBlobPath { get; init; }

    /// <summary>File name as filed (display and extension routing only).</summary>
    public string? FileName { get; init; }

    /// <summary>MIME type, when known.</summary>
    public string? MimeType { get; init; }
}

/// <summary>The input to a harness: the request, the run and the content.</summary>
public sealed record IntakeHarnessRequest
{
    /// <summary>The request (<c>sprk_servicerequest</c>) id.</summary>
    public required Guid RequestId { get; init; }

    /// <summary>The run (<c>sprk_processingjob</c>) id: one pass over the request.</summary>
    public required Guid RunId { get; init; }

    /// <summary>The routing key the harness was chosen by.</summary>
    public required IntakeHarnessRoute Route { get; init; }

    /// <summary>The submitted content, by reference.</summary>
    public IReadOnlyList<IntakeContentReference> Content { get; init; } = Array.Empty<IntakeContentReference>();

    /// <summary>End-to-end correlation id (door → run → engine → return).</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>Which FR-07 stage a harness step belongs to (it is shown as the run's current stage while it runs).</summary>
public enum IntakeHarnessStage
{
    /// <summary>Stage 5: work out what to change (NDA: segmentation, then the edit plan).</summary>
    Analyse = 5,

    /// <summary>Stage 6: produce the outputs (NDA: the redline engine job).</summary>
    Build = 6,
}

/// <summary>One named step of a harness (task 013: the split of the former single <c>RunAsync</c>).</summary>
/// <param name="Name">Token, unique within the harness and stable across releases (the stored results are keyed by it).</param>
/// <param name="Stage">The FR-07 stage the step belongs to.</param>
/// <param name="External">
/// True when the step's result arrives later from outside the BFF (an engine job). The orchestrator marks the step
/// awaiting BEFORE it is called (marker before send, FR-07), and a duplicate delivery does not call it again while the
/// mark stands.
/// </param>
public sealed record IntakeHarnessStep(string Name, IntakeHarnessStage Stage, bool External = false);

/// <summary>One structured result of a run (a step result or an analysis output), by reference.</summary>
public sealed record IntakeHarnessResultReference
{
    /// <summary>Harness-defined kind token (e.g. <c>segmentation</c>, <c>edit-plan</c>, <c>redline</c>).</summary>
    public required string Kind { get; init; }

    /// <summary>Where the result lives: a staging blob path or a record id, as the kind defines. Never content.</summary>
    public required string Reference { get; init; }
}

/// <summary>How a step ended.</summary>
public enum IntakeHarnessStepOutcomeKind
{
    /// <summary>The step's result exists and is referenced by <see cref="IntakeHarnessStepOutcome.Result"/>.</summary>
    Completed = 0,

    /// <summary>External work was started; its completion (task 014) records the result and re-enqueues the run.</summary>
    Dispatched = 1,

    /// <summary>The run cannot continue without an administrator (<see cref="IntakeHarnessStepOutcome.HoldCode"/>).</summary>
    Held = 2,
}

/// <summary>The answer of <see cref="IIntakeHarness.RunStepAsync"/>.</summary>
public sealed record IntakeHarnessStepOutcome
{
    public required IntakeHarnessStepOutcomeKind Kind { get; init; }

    /// <summary>The step's result (<see cref="IntakeHarnessStepOutcomeKind.Completed"/> only).</summary>
    public IntakeHarnessResultReference? Result { get; init; }

    /// <summary>The external execution id (<see cref="IntakeHarnessStepOutcomeKind.Dispatched"/>; recorded on <c>sprk_engineexecutionid</c>).</summary>
    public string? ExecutionId { get; init; }

    /// <summary>A closed reason code (<see cref="IntakeHarnessStepOutcomeKind.Held"/>), shown as admin-attention text. Never content.</summary>
    public string? HoldCode { get; init; }

    public static IntakeHarnessStepOutcome Completed(IntakeHarnessResultReference result) =>
        new() { Kind = IntakeHarnessStepOutcomeKind.Completed, Result = result ?? throw new ArgumentNullException(nameof(result)) };

    public static IntakeHarnessStepOutcome Dispatched(string? executionId) =>
        new() { Kind = IntakeHarnessStepOutcomeKind.Dispatched, ExecutionId = executionId };

    public static IntakeHarnessStepOutcome Held(string holdCode) =>
        new() { Kind = IntakeHarnessStepOutcomeKind.Held, HoldCode = holdCode };
}

/// <summary>What returns through the door the request came in by, by document role.</summary>
public sealed record IntakeHarnessReturnPayload
{
    /// <summary>Roles (keys of <see cref="IntakeHarnessPersistResult.DocumentIdsByRole"/>) of the documents to return.</summary>
    public IReadOnlyList<string> DocumentRoles { get; init; } = Array.Empty<string>();
}

/// <summary>The output of the persist hook: the ids it wrote, and what happens next.</summary>
public sealed record IntakeHarnessPersistResult
{
    /// <summary>Filed <c>sprk_document</c> ids by document role.</summary>
    public IReadOnlyDictionary<string, Guid> DocumentIdsByRole { get; init; } =
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Written result record ids (e.g. <c>sprk_analysis</c>), by result kind.</summary>
    public IReadOnlyDictionary<string, Guid> ResultIdsByKind { get; init; } =
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The document the run produced, recorded on the run's <c>sprk_outputdocument</c> lookup; null when none.</summary>
    public Guid? OutputDocumentId { get; init; }

    /// <summary>
    /// Whether an attorney must approve before anything returns (FR-11). Defaults to <c>true</c>:
    /// AI output never leaves without approval unless a harness states otherwise.
    /// </summary>
    public bool ApprovalRequired { get; init; } = true;

    /// <summary>What to return through the door once approved (or immediately when no approval is required).</summary>
    public IntakeHarnessReturnPayload ReturnPayload { get; init; } = new();
}

/// <summary>Why the registry did or did not return a harness.</summary>
public enum IntakeHarnessResolutionOutcome
{
    /// <summary>A registered harness accepted the route.</summary>
    Resolved = 0,

    /// <summary>The request type was blank or not a deterministic token.</summary>
    InvalidRequestType = 1,

    /// <summary>No enabled <c>intake-harness</c> Binding matched the route.</summary>
    NoBinding = 2,

    /// <summary>A Binding matched but no harness is registered under its key.</summary>
    UnregisteredKey = 3,

    /// <summary>The harness's <see cref="IIntakeHarness.CanHandle"/> refused the route.</summary>
    Declined = 4,
}

/// <summary>
/// The registry's answer. When <see cref="Harness"/> is null the orchestrator sends the request to
/// manual triage (FR-08c, FR-10); this is a result, never an exception.
/// </summary>
public sealed record IntakeHarnessResolution
{
    /// <summary>The outcome.</summary>
    public required IntakeHarnessResolutionOutcome Outcome { get; init; }

    /// <summary>The harness, when <see cref="Outcome"/> is <see cref="IntakeHarnessResolutionOutcome.Resolved"/>.</summary>
    public IIntakeHarness? Harness { get; init; }

    /// <summary>The harness key looked up (the Binding's consumer code), when there was one.</summary>
    public string? HarnessKey { get; init; }

    /// <summary>The matched Binding row id, when routing matched one.</summary>
    public Guid? BindingId { get; init; }

    /// <summary>True when a harness was resolved.</summary>
    public bool HasHarness => Harness is not null;
}
