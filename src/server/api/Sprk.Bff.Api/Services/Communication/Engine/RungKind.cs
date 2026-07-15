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
}
