namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The email-specific FR-04 RI-confidence scorer: <c>confidence = urgencyWeight × deterministicAgreement</c>,
/// clamped to <c>[0, 1]</c>. Pure and deterministic (ADR-013 / NFR-03 — no AI call, no I/O); consumes only
/// values already produced elsewhere in the pipeline (triage priority / classification urgency +
/// <see cref="Engine.AssociationDecisionTrace.TopDeterministicConfidence"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>D-08 — email-specific, not the Workspace/Portfolio scorer.</b> This type lives in
/// <c>Services/Communication/</c> and reads ONLY communication-shaped inputs (triage priority / rung
/// agreement). It shares no code with the Workspace/Portfolio priority scoring path by design.
/// </para>
/// <para>
/// <b>Reuse contract for task 025 (persistence).</b> This is the SINGLE formula both the notification-path
/// signal wiring (task 024, <see cref="CommunicationEnrichmentService"/>'s assessment-emission step) and the
/// <c>sprk_riconfidence</c> persistence (task 025) MUST call, so the number is computed via one shared
/// implementation. Call <see cref="UrgencyWeightFromPriority"/> with the resolved
/// <c>CommunicationTriageResult.Priority</c> label, <see cref="UrgencyWeightFromClassification"/> with the
/// <c>CommunicationClassificationResult.Urgency</c> fallback label, and <see cref="Compute"/> with the
/// resulting urgency weight + <c>AssociationDecisionTrace.TopDeterministicConfidence</c> to get the final
/// score. Task 025 should call the SAME <see cref="Compute"/> overload (or read the already-computed value
/// off this task's wiring) rather than re-deriving the blend independently.
/// </para>
/// </remarks>
public static class RiConfidenceScorer
{
    /// <summary>
    /// Neutral default urgency weight for a missing or unrecognized priority/urgency label — deliberately
    /// the scale's midpoint (not the max, not the min) so an unresolved label neither auto-suppresses nor
    /// auto-boosts the notification path. Combined with the deterministic-agreement factor (which is 0 when
    /// no association has resolved yet), an entirely unassessed communication still scores 0 overall — the
    /// same conservative outcome the pre-024 hardcoded <c>Confidence = 0</c> produced.
    /// </summary>
    internal const double DefaultUrgencyWeight = 0.5;

    /// <summary>
    /// Four-tier urgency→weight scale. Keys cover BOTH vocabularies that can supply urgency at the
    /// assessment-emission point: the closed <c>CommunicationTriageResult.Priority</c> option-set
    /// (<c>Urgent</c>/<c>High</c>/<c>Medium</c>/<c>Low</c>, task 022/023) and the open-text
    /// <c>CommunicationClassificationResult.Urgency</c> fallback the rung-5 classify prompt produces (e.g.
    /// <c>urgent</c>/<c>elevated</c>/<c>routine</c>) — mapped onto the SAME four-tier scale so both sources
    /// produce a consistent weight regardless of which one supplied the label.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, double> UrgencyWeights =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // CommunicationTriageResult.Priority closed set.
            ["Urgent"] = 1.0,
            ["High"] = 0.75,
            ["Medium"] = 0.5,
            ["Low"] = 0.25,
            // CommunicationClassificationResult.Urgency open-text fallback — same four-tier scale.
            ["urgent"] = 1.0,
            ["elevated"] = 0.75,
            ["routine"] = 0.25,
        };

    /// <summary>Maps a <c>CommunicationTriageResult.Priority</c> label (Urgent/High/Medium/Low) to its
    /// urgency weight; an unrecognized or missing label resolves to <see cref="DefaultUrgencyWeight"/>.</summary>
    public static double UrgencyWeightFromPriority(string? priority) => Resolve(priority);

    /// <summary>Maps a <c>CommunicationClassificationResult.Urgency</c> label (e.g. urgent/elevated/routine)
    /// to its urgency weight; an unrecognized, "unspecified", or missing label resolves to
    /// <see cref="DefaultUrgencyWeight"/>.</summary>
    public static double UrgencyWeightFromClassification(string? urgency) => Resolve(urgency);

    private static double Resolve(string? label) =>
        !string.IsNullOrWhiteSpace(label) && UrgencyWeights.TryGetValue(label, out var weight)
            ? weight
            : DefaultUrgencyWeight;

    /// <summary>
    /// Computes the FR-04 RI-confidence: <c>urgencyWeight × deterministicAgreement</c>, each input clamped to
    /// <c>[0, 1]</c> before multiplying, and the product clamped again (defensive — the product of two
    /// clamped values is already in range, but this keeps the contract explicit for any future caller that
    /// composes the factors differently).
    /// </summary>
    public static double Compute(double urgencyWeight, double deterministicAgreement) =>
        Clamp(Clamp(urgencyWeight) * Clamp(deterministicAgreement));

    private static double Clamp(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
}
