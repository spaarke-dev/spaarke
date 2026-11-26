namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// Request / result DTOs and the typed failure of IAgreementAi (spaarke-intake-pipeline-r1 task 004, FR-02).
// SDAP-domain types only: nothing here names an OpenAI, Microsoft.Extensions.AI or other AI-internal type
// (ADR-013 facade boundary).

/// <summary>
/// The agreement sub-domain keys a caller may test a classification against. The set itself is the live
/// <c>sprk_agreementtype</c> registry; this holds only the keys that code branches on.
/// </summary>
public static class AgreementSubDomains
{
    /// <summary>The registry key of the NDA row (<c>infra/dataverse/sprk_agreementtype-rows.json</c>).</summary>
    public const string Nda = "nda";
}

/// <summary>Input of <see cref="IAgreementAi.ClassifyAsync"/>.</summary>
public sealed record AgreementClassifyRequest
{
    /// <summary>The whole text of the document to classify.</summary>
    public required string DocumentText { get; init; }

    /// <summary>The tenant the run is billed and logged to; carried on the run context, never in the operand.</summary>
    public string? TenantId { get; init; }

    /// <summary>An identifier for log correlation (the request, run or document id). Never content.</summary>
    public Guid? SubjectId { get; init; }
}

/// <summary>One plausible agreement sub-domain and the model's calibrated confidence (0 to 1) in it.</summary>
public sealed record AgreementCandidate(string SubDomainKey, double Confidence);

/// <summary>
/// The classifier's verdict: whether the document is an agreement at all, which registered sub-domain(s) it is,
/// and whether it is a composite of several. A non-agreement has no candidates.
/// </summary>
public sealed record AgreementClassifyResult
{
    public required bool IsAgreement { get; init; }

    /// <summary>Candidates by descending confidence. Every key is a key of the live <c>sprk_agreementtype</c> registry.</summary>
    public required IReadOnlyList<AgreementCandidate> Candidates { get; init; }

    public required bool Composite { get; init; }

    /// <summary>The model's one-paragraph explanation. For display to a reviewer; never logged.</summary>
    public string? Reasoning { get; init; }

    /// <summary>The most confident candidate, or null when there is none.</summary>
    public AgreementCandidate? Top => Candidates.Count == 0 ? null : Candidates.MaxBy(c => c.Confidence);

    /// <summary>
    /// True when the document is an agreement whose most confident candidate is <paramref name="subDomainKey"/>
    /// at or above <paramref name="minConfidence"/>. The caller owns the threshold (FR-10 triage), so a
    /// document that is not an NDA, or is one only weakly, is simply false here.
    /// </summary>
    public bool IsSubDomain(string subDomainKey, double minConfidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subDomainKey);
        var top = Top;
        return IsAgreement
            && top is not null
            && string.Equals(top.SubDomainKey, subDomainKey, StringComparison.OrdinalIgnoreCase)
            && top.Confidence >= minConfidence;
    }
}

/// <summary>Input of <see cref="IAgreementAi.BuildEditPlanAsync"/>.</summary>
public sealed record AgreementEditPlanRequest
{
    /// <summary>
    /// The deterministic segmentation of the draft: the accelerator's <c>segment_clauses.py</c> JSON (<c>sections</c> with
    /// <c>index</c>, <c>number</c>, <c>heading</c>, <c>para_key</c> and <c>text</c>, plus <c>has_tracked_changes</c>,
    /// <c>has_comments</c> and <c>has_hidden_text</c>), as the intake engine job's <c>segment</c> operation produces it. Never free
    /// extracted text: the model may anchor an edit only on a <c>para_key</c> in this list.
    /// </summary>
    public required string SegmentationJson { get; init; }

    public string? TenantId { get; init; }

    /// <summary>An identifier for log correlation (the request, run or document id). Never content.</summary>
    public Guid? SubjectId { get; init; }

    /// <summary>The submitted file's name, recorded on the plan's <c>agreement</c> block when given.</summary>
    public string? FileName { get; init; }

    /// <summary>The tracked-change and comment author. Default <see cref="DefaultAuthor"/>.</summary>
    public string? Author { get; init; }

    /// <summary>The author's initials. Default <see cref="DefaultInitials"/>.</summary>
    public string? Initials { get; init; }

    /// <summary>
    /// The plan's date, stamped on every tracked change. Default: now (UTC). A caller that resumes a run passes
    /// the original instant so a re-planned run produces the same stamp.
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; init; }

    public const string DefaultAuthor = "Spaarke Reviewer";

    public const string DefaultInitials = "SR";
}

/// <summary>
/// An edit plan: valid against the accelerator's <c>edit-plan.schema.json</c>, every topic id from the pinned NDA standard, and every
/// edit anchored on a <c>para_key</c> of the supplied segmentation with quotes that occur exactly once. Ready to hand to the redline
/// engines unchanged. Edits the anchor pre-check refused are NOT in <see cref="PlanJson"/>: they are listed in
/// <see cref="RefusedEdits"/> so the caller can ask for one rewrite. The engine remains the final authority on fit.
/// </summary>
public sealed record AgreementEditPlan
{
    /// <summary>The plan, as compact JSON.</summary>
    public required string PlanJson { get; init; }

    /// <summary>The standard the plan was written against, for example "Spaarke NDA – Mutual" (NFR-09).</summary>
    public required string StandardName { get; init; }

    /// <summary>The pinned version of that standard, for example "1.2" (NFR-09).</summary>
    public required string StandardVersion { get; init; }

    /// <summary>The distinct topic ids the plan edits, in order of first appearance.</summary>
    public required IReadOnlyList<string> TopicIds { get; init; }

    /// <summary>How many edits the plan holds (refused edits are not counted). Zero is valid for an NDA that already meets the standard.</summary>
    public required int EditCount { get; init; }

    /// <summary>Edits the exact-match anchor pre-check refused, in the model's order. Never repaired, never sent to the engine.</summary>
    public required IReadOnlyList<AgreementRefusedEdit> RefusedEdits { get; init; }

    /// <summary>
    /// Findings the model could not place on an editable section (no <c>para_key</c>): reported for an attorney, never anchored
    /// anywhere. Their comment is model output for display; it is never logged.
    /// </summary>
    public required IReadOnlyList<AgreementUnplaceableFinding> UnplaceableFindings { get; init; }
}

/// <summary>An edit the anchor pre-check refused: its 0-based index in the model's <c>edits</c> array, why, and its (pinned) topic id.</summary>
public sealed record AgreementRefusedEdit(int Index, string Code, string TopicId);

/// <summary>A finding on a section that cannot be edited (its <c>para_key</c> is null), with the model's comment for an attorney.</summary>
public sealed record AgreementUnplaceableFinding(string TopicId, int SectionIndex, string Comment);

/// <summary>
/// The closed set of reasons the anchor pre-check refuses an edit. The codes are the ones the intake harness switches on to ask for a
/// rewrite; none of them is ever followed by a fuzzy or approximate retry.
/// </summary>
public static class AgreementRefusalCodes
{
    /// <summary><c>para</c> is not one of the supplied <c>para_key</c> values.</summary>
    public const string ParaNotFound = "para_not_found";

    /// <summary><c>find</c> or <c>after</c> does not occur in the section's text (a paraphrase, a misquote).</summary>
    public const string NotFound = "not_found";

    /// <summary><c>find</c> or <c>after</c> occurs more than once in the section's text.</summary>
    public const string NotUnique = "not_unique";

    /// <summary>The quote starts or ends inside a word.</summary>
    public const string WordBoundary = "word_boundary";

    /// <summary><c>old</c> does not occur exactly once inside <c>find</c>.</summary>
    public const string OldNotInFind = "old_not_in_find";
}

/// <summary>
/// Raised by <see cref="IAgreementAi"/> when a capability cannot return a trustworthy result. Carries a reason code,
/// the failing consumer type and a message with no document, clause or plan content (ADR-015). The AI kill switch
/// is a different failure: it surfaces as <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/>.
/// </summary>
public sealed class AgreementAiException : Exception
{
    /// <summary>No Binding row (or no Action behind it) could be resolved for the consumer type.</summary>
    public const string BindingMissing = "ai.agreement.binding_missing";

    /// <summary>The Action could not run to a result: resolving it, reading the type registry, or the model call failed.</summary>
    public const string CompletionFailed = "ai.agreement.completion_failed";

    /// <summary>The Action's JSON does not meet its own output contract, or classifies against a key outside the registry.</summary>
    public const string OutputInvalid = "ai.agreement.output_invalid";

    /// <summary>
    /// The edit plan assembled from the model's edits is refused: an edit names a topic the pinned standard does not
    /// have, or the plan does not validate against <c>edit-plan.schema.json</c>. No partial plan is ever returned.
    /// </summary>
    public const string PlanInvalid = "ai.agreement.plan_invalid";

    public AgreementAiException(string reasonCode, string consumerType, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
        ConsumerType = consumerType ?? throw new ArgumentNullException(nameof(consumerType));
    }

    /// <summary>One of <see cref="BindingMissing"/>, <see cref="CompletionFailed"/>, <see cref="OutputInvalid"/>, <see cref="PlanInvalid"/>.</summary>
    public string ReasonCode { get; }

    /// <summary>The <see cref="ConsumerTypes"/> key of the capability that failed.</summary>
    public string ConsumerType { get; }
}
