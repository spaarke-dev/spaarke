namespace Sprk.Bff.Api.Services.Ai.Classification;

/// <summary>
/// A single row of the <c>sprk_agreementtype</c> registry — the live source of truth for the
/// Agreement Analysis sub-domain key set (design Lens 3d). Read by
/// <see cref="IAgreementTypeRegistryReader"/> and formatted into the classifier prompt by
/// <see cref="AgreementTypeRegistryPromptAssembler"/> so the <c>agreement-classify</c> Action's
/// candidate key set is REGISTRY-DRIVEN (a new registered type extends the classifier with zero code).
/// </summary>
/// <remarks>
/// <para>
/// ai-advanced-capabilities-agreements-r1 task 020 (FR-07). The registry table is owned as data by
/// this project (mirror at <c>infra/dataverse/sprk_agreementtype-rows.json</c>); the identity columns
/// are hub-owned. Only the columns the classifier + gate need are projected here.
/// </para>
/// <para>
/// <see cref="ConfidenceThreshold"/> is carried for completeness (the per-type override of the global
/// 0.85 baseline) but is NOT injected into the classifier prompt — it is consumed by the downstream
/// confirmation gate (task 021), never by the classifier itself, so it cannot bias the model's honest
/// confidence calibration.
/// </para>
/// </remarks>
/// <param name="Key">
/// <c>sprk_key</c> — the stable, unique sub-domain key the classifier emits as <c>subDomainKey</c>
/// (e.g. <c>nda</c>, <c>employment</c>, <c>general</c>). The alternate-key uniqueness is enforced in
/// Dataverse (task 001), so routing/classifier code may key on it safely.
/// </param>
/// <param name="Name">
/// <c>sprk_name</c> — the human display label (e.g. "NDA Confidentiality"). May be null on a bare row;
/// the assembler derives a title from <see cref="Key"/> when absent.
/// </param>
/// <param name="ClassificationCue">
/// <c>sprk_classificationcue</c> — the distinguishing signals prose that tells the model how to
/// recognize this sub-domain. Populated only on <c>nda</c> + <c>general</c> today (task 001); null on
/// the other rows until their sibling per-type pack authors a cue. The assembler emits a key-derived
/// fallback guidance line when this is null.
/// </param>
/// <param name="IsFallback">
/// <c>sprk_isfallback</c> — true on exactly one row (<c>general</c>): the fallback sub-domain the
/// classifier routes to when a document is an agreement but matches no specific type. Selected via this
/// flag, never a magic <c>"general"</c> string.
/// </param>
/// <param name="ConfidenceThreshold">
/// <c>sprk_confidencethreshold</c> — the per-type override of the global 0.85 auto-proceed baseline
/// (null = use the global baseline). Consumed by the task-021 gate, not the classifier.
/// </param>
/// <param name="KnowledgePackRef">
/// <c>sprk_knowledgepackref</c> — the type's knowledge-source id (e.g. <c>KNW-011</c>) in the
/// <c>spaarke-rag-references</c> corpus. Added by task 021 (FR-08 pack binding): read here so the
/// interactive orientation gate can thread it into the review dispatch's <see cref="Sprk.Bff.Api.Services.Ai.LinearConsumers.LinearRunContext.KnowledgeSourceIds"/>
/// (scoping <c>ActionRunner.RetrieveReferenceGroundingAsync</c> to the classified type's own pack
/// instead of the whole corpus). Defaulted (trailing optional positional param) so every task-020
/// call site (assembler, tests, eval fixtures) compiles unchanged — 020 did not need this column;
/// 021 is its first consumer. Null when the row has no pack registered yet.
/// </param>
public sealed record AgreementTypeRow(
    string Key,
    string? Name,
    string? ClassificationCue,
    bool IsFallback,
    decimal? ConfidenceThreshold,
    string? KnowledgePackRef = null);
