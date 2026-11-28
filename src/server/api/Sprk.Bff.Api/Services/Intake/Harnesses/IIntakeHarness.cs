namespace Sprk.Bff.Api.Services.Intake.Harnesses;

/// <summary>
/// A harness owns the Analyse and Build stages, and the Persist hook, for ONE kind of request
/// (spaarke-intake-pipeline-r1 spec FR-08; design §3). The orchestrator (<see cref="IntakeRunJobHandler"/>) owns every
/// other stage, and owns the run record: a harness never writes the run.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adding a harness</b> (FR-08d) takes exactly three things: a class implementing this interface,
/// one DI line (<c>services.AddScoped&lt;IIntakeHarness, MyHarness&gt;()</c> in
/// <c>JobProcessingModule</c>), and one <c>intake-harness</c> Binding row whose
/// <c>sprk_consumercode</c> equals <see cref="Key"/> (<c>infra/dataverse/sprk_playbookconsumer-rows.json</c>).
/// <see cref="IntakeHarnessRegistry"/> does the rest.
/// </para>
/// <para>
/// <b>Steps, not one call</b> (task 013 decision, FR-07a). Analyse and Build are a list of named
/// <see cref="Steps"/> that the orchestrator runs in order, one at a time. It stores each completed step's
/// <see cref="IntakeHarnessResultReference"/> on the run BEFORE it runs the next one, and never calls a stored step
/// again (on resume, redelivery or replay). So an AI step (the NDA edit plan) runs once per run, and a step that needs
/// the output of an engine job (the NDA segmentation) can wait for it: an external step answers
/// <see cref="IntakeHarnessStepOutcome.Dispatched"/>, the run waits, and the engine completion (task 014) records the
/// step's result through <see cref="IntakeRunStore.RecordExternalStepResultAsync"/> and re-enqueues the run.
/// The NDA harness (task 015) declares <c>segment</c> (Analyse, external), <c>edit-plan</c> (Analyse, AI) and
/// <c>redline</c> (Build, external).
/// </para>
/// <para>
/// <b>Content is by reference</b> (NFR-03, ADR-004 claim-check, ADR-015): requests and step results carry document
/// ids, staged blob paths and keys, never document bytes or text. A step result is stored on the run row, so it must
/// never hold content: an edit plan is stored in staging or as a record and referenced by path or id.
/// </para>
/// <para>
/// <b>ADR-010</b>: this is a genuine seam — one implementation per request type (NDA first, task 015;
/// invoices in Phase 3).
/// </para>
/// </remarks>
public interface IIntakeHarness
{
    /// <summary>
    /// The harness key: the <c>sprk_consumercode</c> of this harness's <c>intake-harness</c> Binding
    /// row (e.g. <c>nda</c>). Compared case-insensitively; unique across registered harnesses.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Deterministic precondition check, called by the registry after the Binding resolved to this
    /// harness. Return <c>false</c> when the harness cannot take this request (for example a
    /// document type it does not process); the request then goes to manual triage (FR-10).
    /// Must not call AI or do I/O.
    /// </summary>
    bool CanHandle(IntakeHarnessRoute route);

    /// <summary>
    /// The Analyse and Build steps, in the order they run. Names are tokens, unique within the harness, and stable:
    /// a resumed run finds its stored results by name.
    /// </summary>
    IReadOnlyList<IntakeHarnessStep> Steps { get; }

    /// <summary>
    /// Runs one step. <paramref name="completedSteps"/> holds the stored results of every step before it, by name.
    /// Called again for the same step only when no result was stored (a crash before the orchestrator stored it, or a
    /// redelivery of an external step whose completion has not arrived), so a dispatch must be idempotent on the run id.
    /// </summary>
    Task<IntakeHarnessStepOutcome> RunStepAsync(
        IntakeHarnessStep step,
        IntakeHarnessRequest request,
        IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
        CancellationToken cancellationToken);

    /// <summary>
    /// The persist hook (orchestrator stage 7): write the outputs of <paramref name="completedSteps"/> to the request
    /// and run in one step. It may be called again after a crash before its result was stored, so it must be
    /// idempotent: deterministic ids, and a redelivered run calling it again creates nothing new (FR-07b).
    /// </summary>
    Task<IntakeHarnessPersistResult> PersistAsync(
        IntakeHarnessRequest request,
        IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
        CancellationToken cancellationToken);
}
