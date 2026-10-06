// -----------------------------------------------------------------------------
// IE2EValidationRunner.cs
//
// L2 abstraction over H13's live checks against the deployed customer BFF (spec.md SC #5): /healthz, /ping, the
// Dataverse CORS origin, and — since task 230b — the stamp's keyless proof (the BFF calls each Azure service of its
// stamp with its own managed identity). Production impl: E2EValidationRunner (pure C#, HttpClient); unit tests inject
// fakes that return canned outcomes.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 implementations by design (the production runner + per-test fakes in
// H13E2EAcceptanceGateHandlerTests), so H13's decision logic is testable without a live BFF.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Runs H13's live checks against the deployed customer BFF and surfaces a typed outcome.
/// </summary>
public interface IE2EValidationRunner
{
    /// <summary>
    /// Runs the checks and returns a typed outcome. Domain failures (a check failed) do NOT throw — the handler
    /// decides how to react. Unexpected infrastructure faults may throw — the handler catches and classifies them
    /// Resumable.
    /// </summary>
    Task<E2EValidationOutcome> RunAsync(E2EValidationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single validation run. Immutable record; the caller (<see cref="H13E2EAcceptanceGateHandler"/>)
/// constructs one per invocation.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines.</param>
/// <param name="RunId">RunId — flows into log lines for cross-reference with Cosmos state.</param>
/// <param name="DataverseUrl">Target Dataverse environment URL (e.g. <c>https://sprk-acme.crm.dynamics.com</c>).</param>
/// <param name="BffApiUrl">Target BFF API URL (production slot post-H9 swap).</param>
/// <param name="TargetSlotName">App Service slot the checks target (<c>production</c> or <c>staging</c>).</param>
/// <param name="BffAppRegId">
/// The customer's BFF app registration (client) id — H3's output. The keyless proof's token is requested for
/// <c>api://{BffAppRegId}</c>, the audience the BFF validates (task 230b).
/// </param>
public sealed record E2EValidationRequest(
    string CustomerId,
    string RunId,
    string DataverseUrl,
    string BffApiUrl,
    string TargetSlotName,
    string BffAppRegId);

/// <summary>
/// Typed outcome of a single validation run. Discriminated union: <see cref="Success"/> when every check passed;
/// <see cref="Failure"/> when at least one check failed (the stamp is wrong — QuarantineRequired);
/// <see cref="Inconclusive"/> when nothing failed but a check could not reach a verdict (transient — Resumable).
/// </summary>
public abstract record E2EValidationOutcome
{
    private E2EValidationOutcome() { }

    /// <summary>Every check passed.</summary>
    /// <param name="ChecksPassed">Names of the checks that passed.</param>
    /// <param name="ChecksSkipped">Names of the checks this runner does not perform per run, each with its reason in the name.</param>
    public sealed record Success(IReadOnlyList<string> ChecksPassed, IReadOnlyList<string> ChecksSkipped) : E2EValidationOutcome;

    /// <summary>At least one check failed — including any auth refusal (task 230b: an auth failure is never a skip).</summary>
    /// <param name="ChecksFailed">Names of the checks that failed.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic aggregating every failing check's message.</param>
    public sealed record Failure(IReadOnlyList<string> ChecksFailed, string Diagnostic) : E2EValidationOutcome;

    /// <summary>
    /// No check failed, but at least one could not reach a verdict — a transport fault, a timeout, throttling or a
    /// server error (task 230b). Resume the run; nothing about the stamp has been shown to be wrong.
    /// </summary>
    /// <param name="ChecksInconclusive">Names of the checks without a verdict.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic.</param>
    public sealed record Inconclusive(IReadOnlyList<string> ChecksInconclusive, string Diagnostic) : E2EValidationOutcome;
}
