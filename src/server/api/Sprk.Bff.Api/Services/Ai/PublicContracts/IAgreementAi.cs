namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for app-only agreement AI: classify a document and write an NDA edit plan, from a background job,
/// outside any chat session (spaarke-intake-pipeline-r1 task 004, FR-02). The intake orchestrator's NDA harness is the
/// consumer; nothing else in CRUD code may reach <c>IActionResolver</c> / <c>IActionRunner</c> for these Actions (ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="IInvoiceAi"/>: each method resolves its Action through its <see cref="ConsumerTypes"/> key,
/// runs it with the request as the <c>## Input</c> operand and parses the structured output. The Console's
/// <c>agreement-review</c> Action is not involved and its closed output contract is untouched: the edit plan comes from
/// the separate <see cref="ConsumerTypes.AgreementEditPlan"/> Action.
/// </para>
/// <para>
/// Both methods fail closed with <see cref="AgreementAiException"/>; the AI kill switch surfaces as
/// <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/> (<see cref="NullAgreementAi"/>, ADR-032 P3). They are
/// separate calls on purpose: the orchestrator records the classification on the run and a resumed run never repeats it.
/// </para>
/// </remarks>
public interface IAgreementAi
{
    /// <summary>
    /// Runs <see cref="ConsumerTypes.AgreementClassify"/> over the document. The live <c>sprk_agreementtype</c> registry is
    /// injected into the prompt as the closed set of keys, and a candidate outside it is refused.
    /// </summary>
    /// <exception cref="AgreementAiException">Binding missing, completion failed, or the output does not meet its contract.</exception>
    Task<AgreementClassifyResult> ClassifyAsync(
        AgreementClassifyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <see cref="ConsumerTypes.AgreementEditPlan"/> over the NDA and returns a complete edit plan. The model writes only
    /// topic-keyed edits; this method fills <c>author</c>, <c>date</c>, <c>review_basis</c>, <c>provenance</c>, the bookmarks and
    /// the summary numbering, then validates the whole plan against <c>edit-plan.schema.json</c> and the pinned standard's topic
    /// ids. The caller decides, from <see cref="ClassifyAsync"/>, whether the document is an NDA at all.
    /// </summary>
    /// <exception cref="AgreementAiException">
    /// Binding missing, completion failed, the output does not meet its contract, or the plan is refused
    /// (<see cref="AgreementAiException.PlanInvalid"/>). No partial plan is ever returned.
    /// </exception>
    Task<AgreementEditPlan> BuildEditPlanAsync(
        AgreementEditPlanRequest request,
        CancellationToken cancellationToken = default);
}
