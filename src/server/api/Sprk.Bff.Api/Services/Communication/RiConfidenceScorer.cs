namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The email-specific FR-04 RI-confidence scorer:
/// <c>confidence = (UrgencyWeightFactor × urgencyWeight) + (AgreementWeightFactor × deterministicAgreement)</c>,
/// clamped to <c>[0, 1]</c>. Pure and deterministic (ADR-013 / NFR-03 — no AI call, no I/O); consumes only
/// values already produced elsewhere in the pipeline (triage priority / classification urgency +
/// <see cref="Engine.AssociationDecisionTrace.TopDeterministicConfidence"/>).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Corrected 2026-09-29 (<c>spaarke-ontology-platform-r1</c>) — do not re-derive the product form.</b>
/// This was <c>urgencyWeight × deterministicAgreement</c>. Because agreement was a MULTIPLICATIVE factor, a
/// communication whose association had not deterministically resolved scored <b>exactly zero regardless of
/// urgency</b>, so the RI notification path could never fire for it — and those are precisely the messages a
/// human most needs surfaced, since nobody will stumble across an unfiled email. Two live captures made it
/// concrete: <c>High × 0.90 = 0.675</c>, and <c>High × 0.0 = 0.0</c> for a message asking approval of
/// ~$140–145k of additional work. The second case was NOT an association failure in substance — the
/// <c>RecordNameMatch</c> rung identified the matter at 0.97 and wrote it; it is simply excluded from the
/// deterministic pool by design (surface-for-review, never auto-file), which zeroed the product.
/// </para>
/// <para>
/// <b>The product form conflated two unrelated questions</b> — "how sure am I WHICH MATTER this belongs to"
/// (agreement) and "how much does this MATTER" (urgency) — and let the first veto the second. A $145k budget
/// request is worth surfacing whether or not we know where to file it. The additive blend lets agreement
/// <i>inform</i> the score without vetoing it: neither factor can zero the other.
/// </para>
/// <para>
/// <b>Recall over precision, by owner decision (2026-09-29)</b>: "we would rather screen out the noise and
/// make adjustments than miss a flag." Hence urgency is weighted higher than agreement — importance drives
/// notification-worthiness; agreement only reinforces it.
/// </para>
/// <para>
/// <b>This does NOT loosen auto-filing.</b> The 0.85 auto-file threshold and the deterministic-eligibility
/// rules in the association engine are untouched. Auto-filing WRITES data, where a false positive is worse
/// than a miss; notification only SURFACES, where a miss is worse than noise. Recall-first applies to
/// notifying, not to filing.
/// </para>
/// <para>
/// Resulting scores (threshold for comparison: see <c>CommsPolicyOptions.DefaultConfidenceThreshold</c>):
/// <code>
///                 det=0.0   det=0.90   det=1.0
///   Urgent 1.00     0.70      0.97       1.00
///   High   0.75     0.525     0.795      0.825
///   Medium 0.50     0.35      0.62       0.65
///   Low    0.25     0.175     0.445      0.475
/// </code>
/// Under the product form the entire <c>det=0.0</c> column was 0.000.
/// </para>
/// </remarks>
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
    /// auto-boosts the notification path.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Corrected 2026-09-29</b>: this previously claimed that "combined with the deterministic-agreement
    /// factor (which is 0 when no association has resolved yet), an entirely unassessed communication still
    /// scores 0 overall". That was true of the product form and is **no longer true** — under the weighted sum
    /// an unassessed communication scores <c>0.7 × 0.5 = 0.35</c>, which is deliberately AT the default gate
    /// threshold so it surfaces rather than vanishing — below the 0.45 default gate on its own, but able to
    /// clear it as soon as ANY association evidence arrives. That change is the point, not a side effect:
    /// silently scoring 0 is how the notification path went dark for every unfiled email.
    /// </remarks>
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
    /// Weight on <c>urgencyWeight</c> in the blend. Higher than <see cref="AgreementWeightFactor"/> because
    /// notification-worthiness is driven by how much the message MATTERS, not by how confident we are about
    /// where to file it (owner decision 2026-09-29, recall over precision).
    /// </summary>
    internal const double UrgencyWeightFactor = 0.7;

    /// <summary>
    /// Weight on <c>deterministicAgreement</c> in the blend. Deliberately the smaller term: association
    /// quality REINFORCES a notification but must never veto one (see the type remarks — it previously could,
    /// as a multiplicative zero).
    /// </summary>
    internal const double AgreementWeightFactor = 0.3;

    /// <summary>
    /// Computes the FR-04 RI-confidence as a weighted SUM:
    /// <c>(0.7 × urgencyWeight) + (0.3 × deterministicAgreement)</c>, each input clamped to <c>[0, 1]</c>
    /// before blending and the result clamped again (defensive — the weights sum to 1.0, so a blend of two
    /// clamped values is already in range, but the contract stays explicit for any future caller that
    /// recomposes the factors).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>This was a PRODUCT and is now a SUM</b> — see the type-level remarks for why, and do not
    /// re-derive the product form: it made unresolved associations unnotifiable regardless of urgency.
    /// </remarks>
    public static double Compute(double urgencyWeight, double deterministicAgreement) =>
        Clamp((UrgencyWeightFactor * Clamp(urgencyWeight))
              + (AgreementWeightFactor * Clamp(deterministicAgreement)));

    private static double Clamp(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
}
