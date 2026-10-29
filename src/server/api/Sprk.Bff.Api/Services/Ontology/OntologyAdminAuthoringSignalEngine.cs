using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Ontology;

/// <summary>
/// The two evaluator calls the Ontology admin authoring routes make (ontology platform R1 task 101, D-22 slice b), and nothing else:
/// the dry run's side-effect-free evaluation of a policy built in memory, and the publish's single-policy pass.
/// </summary>
/// <remarks>
/// <para><b>Identity (#39, D-13).</b> Both run as the EVALUATOR, never as the admin: the dry run must see what production sees (as the
/// admin, Dataverse would trim rows and a <c>notExists</c> clause would be true for every matter), and Signals are written only by
/// the writer identity (ADR-028 path A). Neither call is handed the admin's request: the dry run passes a policy the service built
/// from the caller's own read plus the draft body (<see cref="SignalEvaluator.EvaluateAsync"/>, which writes nothing), and the publish
/// passes a policy id the caller's own read admitted (<see cref="SignalReevaluationJob.ReevaluatePolicyAsync"/>).</para>
/// <para><b>Why a seam.</b> Both targets are concrete singletons over the application-identity Dataverse client and the writer, so the
/// route tests could not run them without a full Dataverse fake. The production implementation is these two lines; the route tests
/// substitute this interface and assert what it was asked.</para>
/// <para>§11: <i>Existing</i> — <see cref="SignalEvaluator"/> and <see cref="SignalReevaluationJob"/> are the cores (tasks 031, 033);
/// this adds no logic. <i>Extension</i> — they cannot be substituted in a route test (sealed, concrete). <i>Cost of doing nothing</i>
/// — the dry run's "rows filtered by the caller" and the publish ordering could only be tested against live data.</para>
/// </remarks>
public interface IOntologyAdminAuthoringSignalEngine
{
    /// <summary>Evaluates <paramref name="policy"/> as the evaluator, over every subject or, with <paramref name="matterScope"/> (task
    /// 102b, D-165), over that one matter: the same subject-scoped evaluation the nightly job runs for a matter-scoped policy. The caller
    /// has checked that the rule's subject is <c>sprk_matter</c>. Writes nothing.</summary>
    Task<SignalEvaluation> EvaluateAsync(SignalEvaluationPolicy policy, Guid? matterScope, CancellationToken ct);

    /// <summary>The nightly job's single-policy pass for one enabled policy, now (supersede, then raise the new version's episodes).</summary>
    Task<PolicyReevaluationResult> ReevaluateAsync(Guid policyId, CancellationToken ct);
}

/// <summary>The production <see cref="IOntologyAdminAuthoringSignalEngine"/>: the shipped evaluator and job, unchanged.</summary>
public sealed class OntologyAdminAuthoringSignalEngine : IOntologyAdminAuthoringSignalEngine
{
    private readonly SignalEvaluator _evaluator;
    private readonly SignalReevaluationJob _job;

    public OntologyAdminAuthoringSignalEngine(SignalEvaluator evaluator, SignalReevaluationJob job)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _job = job ?? throw new ArgumentNullException(nameof(job));
    }

    /// <inheritdoc/>
    public Task<SignalEvaluation> EvaluateAsync(SignalEvaluationPolicy policy, Guid? matterScope, CancellationToken ct) =>
        _evaluator.EvaluateAsync(policy, subjectId: matterScope, ct);

    /// <inheritdoc/>
    public Task<PolicyReevaluationResult> ReevaluateAsync(Guid policyId, CancellationToken ct) =>
        _job.ReevaluatePolicyAsync(policyId, ct);
}
