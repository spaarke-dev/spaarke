using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.ReviewMemo;

/// <summary>
/// FR-13 (ai-advanced-capabilities-agreements-r1 task 050) — pure, stateless assembly of the Review
/// Summary Memo from a caller-supplied section list. Mirrors the reuse pattern established by
/// <see cref="Sprk.Bff.Api.Services.Compose.ComposeSummaryPageGenerator"/> (NDA Summary Page, task
/// 041 of nda-r1): the CLIENT is the party with live access to the document's final tracked-change
/// disposition (accept/reject) at the moment memo generation is requested — pre-FR-16 there is no
/// server-side persisted store of per-finding disposition (<c>AnchoredAnnotation</c> is mutable UI
/// state with no disposition field; <c>ComposeCommentThreadModel.resolved</c> is explicitly "UI-only
/// ... NOT written to native <c>w:comment</c> on save"; see task 050 execution notes §Audit for the
/// full trace). The client supplies the per-section tuple (mirroring how
/// <c>SaveComposeDocumentRequest.SummaryPage</c> already carries the client-held ledgered NDA-REVIEW
/// result to the server for deterministic, no-second-LLM-call assembly) and this class performs the
/// ONE piece of real assembly logic: Decision #4's before/after semantics.
/// </summary>
/// <remarks>
/// <b>Decision #4 semantics (spec, binding)</b>: before = original text (<c>QuotedText</c>, the
/// grounded verbatim excerpt the Action extracted); after = the FINAL disposition state at
/// generation time — no per-accept event capture, no timestamps. A caller-supplied
/// <see cref="ReviewMemoSectionInput.AfterText"/> represents an ACCEPTED edit (the final text
/// differs from the original). Its absence (null/empty) represents EITHER a rejected suggestion OR
/// a section the reviewer never acted on — both converge on the identical observable fact that the
/// original text is what stands in the final document, so <c>after</c> defaults to <c>before</c> in
/// both cases. The memo's closed field set is exactly {location, before, after, why, golden-ref}
/// (spec FR-13) — there is no separate accepted/rejected/untouched status enum, by design.
/// </remarks>
public static class ReviewMemoAssembler
{
    /// <summary>Schema tag on the persisted JSON body — bump on any breaking shape change.</summary>
    public const string SchemaVersion = "review-memo-v1";

    /// <summary>
    /// Assembles the canonical <see cref="ReviewMemoDocument"/> from <paramref name="request"/>.
    /// Pure — no I/O, no second LLM call (ADR-039 complied by construction, same posture as
    /// <c>ComposeSummaryPageGenerator</c>). Caller MUST validate <see cref="GenerateReviewMemoRequest.Sections"/>
    /// is non-empty BEFORE calling (the "no completed review" negative path never reaches this method).
    /// </summary>
    public static ReviewMemoDocument Assemble(GenerateReviewMemoRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sections = new List<ReviewMemoSection>(request.Sections.Count);
        foreach (var input in request.Sections)
        {
            // Decision #4: after = final disposition state. An explicit, non-empty AfterText is the
            // ONLY signal that an edit was accepted; its absence means the original stands (rejected
            // suggestion or an untouched flagged section — both look identical from "what's in the
            // final document", by design, per the class remarks above).
            var after = string.IsNullOrEmpty(input.AfterText) ? input.QuotedText : input.AfterText;

            sections.Add(new ReviewMemoSection(
                Location: input.SectionRef,
                Before: input.QuotedText,
                After: after,
                Why: input.Assessment,
                FlaggedClause: input.FlaggedClause,
                StandardRef: input.StandardRef,
                RiskLevel: input.RiskLevel));
        }

        return new ReviewMemoDocument(
            SchemaVersion: SchemaVersion,
            OverallRisk: request.OverallRisk,
            SectionCount: sections.Count,
            Sections: sections);
    }
}

// ---------------------------------------------------------------------------
// Wire contracts (mirror-first — camelCase JsonPropertyName regardless of the
// serializer options in force at the call site, matching
// Services.Compose.NdaReviewSummaryPageInput's precedent).
// ---------------------------------------------------------------------------

/// <summary>
/// Request payload for <c>POST /api/ai/chat/sessions/{sessionId}/review-memo</c>. The client supplies
/// the full per-section tuple (see class remarks on <see cref="ReviewMemoAssembler"/> for why) —
/// <c>analysisId</c> is deliberately NOT part of this payload; it is resolved server-side from the
/// session's <c>HostContext</c> (sentinel-aware — see <c>ReviewMemoEndpoints.GenerateReviewMemo</c>).
/// </summary>
public sealed record GenerateReviewMemoRequest
{
    /// <summary>Overall risk rating for the whole agreement (Part A rubric) — carried through from the
    /// agreement-review Action's ledgered result, unchanged (no re-derivation).</summary>
    [property: JsonPropertyName("overallRisk")]
    public required string OverallRisk { get; init; }

    /// <summary>
    /// One entry per changed/flagged section. MUST be non-empty — an empty/absent list is the "no
    /// completed review" negative path (<c>ReviewMemoEndpoints</c> returns ProblemDetails and persists
    /// nothing; this type is never asked to assemble a partial/empty memo).
    /// </summary>
    [property: JsonPropertyName("sections")]
    public required IReadOnlyList<ReviewMemoSectionInput> Sections { get; init; }
}

/// <summary>
/// One caller-supplied section tuple. Mirrors the agreement-review Action's discrete output fields
/// (<c>infra/dataverse/outputschemas/agreement-review.schema.json</c>, FR-05 split) plus the ONE piece
/// of information the server cannot derive itself: <see cref="AfterText"/>.
/// </summary>
public sealed record ReviewMemoSectionInput
{
    /// <summary>Location — the finding's page/section/paragraph locator (Action's <c>sectionRef</c>).</summary>
    [property: JsonPropertyName("sectionRef")]
    public required string SectionRef { get; init; }

    /// <summary>Before (original) — the grounded verbatim excerpt (Action's <c>quotedText</c>).</summary>
    [property: JsonPropertyName("quotedText")]
    public required string QuotedText { get; init; }

    /// <summary>
    /// After (final disposition), ONLY when an edit was ACCEPTED (the final text differs from
    /// <see cref="QuotedText"/>). Null/empty for a rejected suggestion or an untouched flagged section
    /// — <see cref="ReviewMemoAssembler.Assemble"/> then defaults <c>after</c> to <c>before</c>.
    /// </summary>
    [property: JsonPropertyName("afterText")]
    public string? AfterText { get; init; }

    /// <summary>Why — the reasoned-judgment prose (Action's <c>assessment</c>).</summary>
    [property: JsonPropertyName("assessment")]
    public required string Assessment { get; init; }

    /// <summary>Golden-ref (standard side) — the retrieved firm-standard citation (Action's <c>standardRef</c>).</summary>
    [property: JsonPropertyName("standardRef")]
    public required string StandardRef { get; init; }

    /// <summary>Golden-ref (grounded fact) — what the clause actually says (Action's <c>flaggedClause</c>).</summary>
    [property: JsonPropertyName("flaggedClause")]
    public required string FlaggedClause { get; init; }

    /// <summary>Optional coarse risk signal (Action's <c>riskLevel</c>) — carried through for completeness; never a numeric score (ADR-039).</summary>
    [property: JsonPropertyName("riskLevel")]
    public string? RiskLevel { get; init; }
}

/// <summary>
/// The assembled, self-contained memo (ADR-015 — everything needed to stand alone; no Cosmos
/// back-references). Serialized verbatim into <c>sprk_analysisoutput.sprk_value</c> by
/// <c>AnalysisResultPersistence.PersistReviewMemoAsync</c>.
/// </summary>
public sealed record ReviewMemoDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("overallRisk")] string OverallRisk,
    [property: JsonPropertyName("sectionCount")] int SectionCount,
    [property: JsonPropertyName("sections")] IReadOnlyList<ReviewMemoSection> Sections);

/// <summary>One assembled memo row: {location, before, after, why, golden-ref} per spec FR-13.</summary>
public sealed record ReviewMemoSection(
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("before")] string Before,
    [property: JsonPropertyName("after")] string After,
    [property: JsonPropertyName("why")] string Why,
    [property: JsonPropertyName("flaggedClause")] string FlaggedClause,
    [property: JsonPropertyName("standardRef")] string StandardRef,
    [property: JsonPropertyName("riskLevel")] string? RiskLevel);
