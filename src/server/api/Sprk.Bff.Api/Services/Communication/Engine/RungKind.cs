namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The kind of association rung, per the FR-10/FR-14/FR-15 rung taxonomy (ADR-045). The engine
/// evaluates rungs in ascending <see cref="IAssociationRung.Order"/> and takes the first rung that
/// yields any <see cref="RungMatch"/> (deterministic cascade). <see cref="IAssociationRung.Order"/>
/// — NOT this enum's numeric value — governs evaluation sequence, so the existing inbound cascade
/// (thread → participant → subject) is preserved regardless of taxonomy numbering.
/// </summary>
/// <remarks>
/// Rungs 0–3 are deterministic (auto-file eligible per task 015); rungs 4–5 are AI/semantic and
/// NEVER auto-file. Task 011 ships the abstraction plus adapters wrapping the current inline logic;
/// tasks 012 (rungs 0–1), 013 (rung 2), 014 (rung 3) and W3 (rungs 4–5) supply real rung content.
/// </remarks>
public enum RungKind
{
    /// <summary>Rung 0 — explicit reference (e.g. a matter reference number in the subject/body).</summary>
    ExplicitReference = 0,

    /// <summary>Rung 1 — thread continuity (In-Reply-To/References/conversationId → parent communication).</summary>
    ThreadContinuity = 1,

    /// <summary>Rung 2 — participant correlation (sender/recipient email + domain → contact/org/account).</summary>
    ParticipantCorrelation = 2,

    /// <summary>Rung 3 — structural detectors over message/attachment structure.</summary>
    StructuralDetector = 3,

    /// <summary>Rung 4 — semantic record match (RecordSearchService).</summary>
    SemanticMatch = 4,

    /// <summary>Rung 5 — AI extract + classify.</summary>
    AiClassification = 5,

    /// <summary>
    /// Rung 3.5 — deterministic record-NAME/number match. Retrieves candidates from the records index
    /// (keyword ranking) then deterministically VERIFIES that a candidate's name or reference number appears
    /// verbatim (normalized) in the email subject/body, and surfaces EVERY verified record type (matter AND
    /// project AND invoice) as a high-confidence review candidate. Runs in the deterministic pass (before the
    /// AI rungs) but — per owner spec (2026-07-17) — is <b>surface-for-review, never auto-file</b>: the user
    /// picks the primary among the surfaced matches. Deliberately NOT in the mapper's auto-file-eligible set,
    /// and NOT an AI rung.
    /// </summary>
    RecordNameMatch = 6,

    /// <summary>
    /// Rung 3.6 — deterministic CONTACT-NAME match (email-r4 UAT R2 B1, owner spec 2026-07-20). Extracts
    /// candidate PERSON-NAME phrases (a run of 2+ Title-Case tokens) from the envelope (subject + body +
    /// attachment text) and resolves each by EXACT full-name lookup against existing <c>contact</c> records
    /// (contacts are NOT in the records index → a Dataverse query, not a RecordSearch). Closes the gap that
    /// let a contact NAMED in the body ("Working on this file will be Eyal Iffergan and Sara Chen") never
    /// surface — today contacts match only as sender/recipient (rung 2). Runs in the deterministic pass but
    /// is <b>SUGGEST-ONLY, never auto-file</b>: it emits <c>sprk_regardingperson</c> — a FALLBACK field the
    /// mapper structurally excludes from auto-file — at a Suggested-band confidence (below the 0.85 threshold),
    /// and is (like <see cref="RecordNameMatch"/>) NOT in the mapper's auto-file-eligible set and NOT an AI
    /// rung. Precision guard: a 2-token full name that EXACTLY matches an existing contact (a single common
    /// name, or a name resolving to no contact, matches nothing) — bias to precision over recall.
    /// </summary>
    ContactNameMatch = 7,
}
