namespace Sprk.Bff.Api.Services.Intake.Harnesses;

/// <summary>
/// A harness owns the Analyse and Build stages, and the Persist hook, for ONE kind of request
/// (spaarke-intake-pipeline-r1 spec FR-08; design §3). The orchestrator owns every other stage.
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
/// <b>Content is by reference</b> (NFR-03, ADR-004 claim-check): requests and results carry document
/// ids and staged blob paths, never document bytes. A harness reads only what it needs.
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
    /// Analyse and Build: produce the outputs for one run. Returns references only; nothing is
    /// written to the request yet (that is <see cref="PersistAsync"/>).
    /// </summary>
    Task<IntakeHarnessResult> RunAsync(IntakeHarnessRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The persist hook (orchestrator stage 7): write the <paramref name="result"/> outputs to the
    /// request and run in one step. Called with the stored result, so it must be idempotent: a
    /// redelivered run calling it again creates nothing new (FR-07b).
    /// </summary>
    Task<IntakeHarnessPersistResult> PersistAsync(
        IntakeHarnessRequest request,
        IntakeHarnessResult result,
        CancellationToken cancellationToken);
}
