using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The confidence→status ladder (R4 FR-11) — the single highest-consequence decision in the Association
/// Engine. Aggregates the rung matches for a communication, reinforces independent signals, and maps the
/// result to a <c>sprk_associationstatus</c> plus an auto-file decision:
///
/// <list type="bullet">
///   <item>DETERMINISTIC (rungs 0–3) reinforced ≥ threshold, kill-switch on → <b>Resolved + auto-file</b>.</item>
///   <item>Reinforced in [0.50, threshold), OR any AI rung (4–5) involved, OR kill-switch off → <b>Suggested</b>.</item>
///   <item>Reinforced &lt; 0.50, or no writable match → <b>Pending Review</b>.</item>
///   <item>Two+ distinct targets on the SAME field each ≥ threshold → <b>Ambiguous</b> (never guess).</item>
/// </list>
///
/// <para>
/// <b>Signal reinforcement (owner priority (b)):</b> when INDEPENDENT rungs agree on the same
/// (field, target), their confidences COMBINE via a bounded noisy-OR (<c>1 − Π(1 − cᵢ)</c>, saturating at
/// 1.0) rather than taking the max — so e.g. a participant-membership (0.80) + a subject-token (0.90)
/// agreeing on the same matter reinforce to ~0.98 and auto-file. Only DISTINCT rung kinds reinforce
/// (a single rung's internal matches for a target are collapsed to its max first) so a rung cannot
/// inflate its own confidence.
/// </para>
///
/// <para>
/// <b>AI never auto-files (constraint):</b> auto-file eligibility is computed from the DETERMINISTIC-only
/// reinforced confidence. An AI rung can only ADD to the full confidence (raising a Pending Review to
/// Suggested) — it can never push a target to Resolved on its own, and it never blocks a deterministic
/// target that already clears the bar. This encodes the W3 rungs 4–5 treatment now, so those tasks only
/// register higher rungs with zero ladder change.
/// </para>
/// </summary>
public sealed class AssociationStatusMapper
{
    private readonly AutoFileGate _gate;
    private readonly ILogger<AssociationStatusMapper> _logger;

    /// <summary>Below this reinforced confidence a match is not worth surfacing → Pending Review.</summary>
    private const double SuggestFloor = 0.50;

    /// <summary>A field-winner below this confidence is not asserted as a regarding write.</summary>
    private const double WriteFloor = 0.50;

    public AssociationStatusMapper(AutoFileGate gate, ILogger<AssociationStatusMapper> logger)
    {
        _gate = gate;
        _logger = logger;
    }

    /// <summary>
    /// Apply the ladder to the aggregated rung matches for one communication.
    /// </summary>
    /// <param name="matches">All matches from the evaluated rungs (writable + metadata-only signals).</param>
    /// <param name="direction">Message direction (recorded in provenance; the decision is direction-symmetric).</param>
    /// <param name="tenantKey">Optional tenant key for the ADR-018 per-tenant kill-switch resolution.</param>
    public AssociationDecision Decide(
        IReadOnlyList<RungMatch> matches,
        CommunicationDirection direction,
        string? tenantKey)
    {
        var settings = _gate.Resolve(tenantKey);

        var rungsFired = matches
            .Select(m => m.Rung.ToString())
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        // Metadata-only structural signals (rung 3 category/obligations, no target) — recorded regardless
        // of the association decision (carry-forward #3: always-run detector pass).
        var signals = matches
            .Where(m => (m.Target is null || m.RegardingFieldName is null) && m.Category is not null)
            .Select(m => new SignalTrace
            {
                Category = m.Category!,
                Confidence = Clamp(m.Confidence),
                Provenance = m.Provenance,
                Obligations = m.Obligations,
            })
            .ToList();

        // Writable matches: a real regarding target. Group by field → per distinct target reinforce.
        var writable = matches
            .Where(m => m.Target is not null && m.RegardingFieldName is not null)
            .ToList();

        var fieldWinners = BuildFieldWinners(writable);

        // Conflict is "2+ distinct targets on the same field each ≥ the (tenant-resolved) threshold";
        // evaluate it now that the threshold is known.
        foreach (var fw in fieldWinners)
            fw.EvaluateConflict(settings.Threshold);

        // Ladder inputs.
        var anyConflict = fieldWinners.Any(f => f.Conflict);
        var topFull = fieldWinners.Count > 0 ? fieldWinners.Max(f => f.FullConfidence) : 0.0;
        var topDet = fieldWinners.Count > 0 ? fieldWinners.Max(f => f.DeterministicConfidence) : 0.0;
        var aiInvolvedTop = fieldWinners
            .Where(f => Math.Abs(f.FullConfidence - topFull) < 1e-9)
            .Any(f => f.AiInvolved);

        int status;
        bool autoFiled = false;
        string reason;
        var writes = new Dictionary<string, EntityReference>();

        if (fieldWinners.Count == 0)
        {
            status = AssociationStatusCodes.PendingReview;
            reason = "No rung resolved a regarding target.";
        }
        else if (anyConflict)
        {
            var conflictFields = string.Join(", ", fieldWinners.Where(f => f.Conflict).Select(f => f.Field));
            status = AssociationStatusCodes.Ambiguous;
            reason = $"Conflicting high-confidence matches (≥ {settings.Threshold:F2}) on field(s): {conflictFields}. Engine will not guess.";
        }
        else if (topFull < SuggestFloor)
        {
            status = AssociationStatusCodes.PendingReview;
            reason = $"Top reinforced confidence {topFull:F2} < suggest floor {SuggestFloor:F2}.";
        }
        else if (topDet >= settings.Threshold && settings.Enabled)
        {
            status = AssociationStatusCodes.Resolved;
            autoFiled = true;
            reason = $"Deterministic reinforced confidence {topDet:F2} ≥ threshold {settings.Threshold:F2}; auto-file enabled ⇒ Resolved.";
            // Auto-file asserts only deterministic winners (AI-derived fields are never auto-filed).
            AddWrites(writes, fieldWinners, useDeterministic: true);
        }
        else
        {
            status = AssociationStatusCodes.Suggested;
            reason = BuildSuggestedReason(topDet, topFull, aiInvolvedTop, settings);
            // Suggestions may include AI-derived fields.
            AddWrites(writes, fieldWinners, useDeterministic: false);
        }

        var candidates = BuildCandidateTraces(fieldWinners, writes);

        var provenance = new AssociationProvenance
        {
            Direction = direction.ToString(),
            RungsFired = rungsFired,
            Candidates = candidates,
            Signals = signals,
            Decision = new AssociationDecisionTrace
            {
                Status = AssociationStatusCodes.Name(status),
                AutoFiled = autoFiled,
                KillSwitchEnabled = settings.Enabled,
                AutoFileThreshold = settings.Threshold,
                TopDeterministicConfidence = Math.Round(topDet, 4),
                TopConfidence = Math.Round(topFull, 4),
                AiInvolved = aiInvolvedTop,
                Reason = reason,
            },
        };

        _logger.LogInformation(
            "Association decision | Direction: {Direction}, Status: {Status}, AutoFiled: {AutoFiled}, " +
            "TopDet: {TopDet:F2}, TopFull: {TopFull:F2}, Threshold: {Threshold:F2}, KillSwitch: {KillSwitch}, Reason: {Reason}",
            direction, AssociationStatusCodes.Name(status), autoFiled, topDet, topFull,
            settings.Threshold, settings.Enabled, reason);

        return new AssociationDecision
        {
            Status = status,
            RegardingWrites = writes,
            AutoFiled = autoFiled,
            Provenance = provenance,
        };
    }

    // ── Aggregation ──────────────────────────────────────────────────────────────

    private static List<FieldWinner> BuildFieldWinners(IReadOnlyList<RungMatch> writable)
    {
        var winners = new List<FieldWinner>();

        foreach (var fieldGroup in writable.GroupBy(m => m.RegardingFieldName!))
        {
            var targets = new List<TargetAgg>();

            foreach (var targetGroup in fieldGroup.GroupBy(m => m.Target!.Id))
            {
                var contributors = targetGroup.ToList();

                // Reinforce across DISTINCT rung kinds (max per kind first, then noisy-OR across kinds)
                // so a single rung cannot inflate its own confidence by emitting duplicates.
                var perKindMax = contributors
                    .GroupBy(m => m.Rung)
                    .Select(g => g.OrderByDescending(m => m.Confidence).First())
                    .ToList();

                var fullConf = NoisyOr(perKindMax.Select(m => m.Confidence));
                var detConf = NoisyOr(perKindMax.Where(m => IsDeterministic(m.Rung)).Select(m => m.Confidence));
                var aiInvolved = perKindMax.Any(m => !IsDeterministic(m.Rung));

                targets.Add(new TargetAgg(
                    Target: contributors[0].Target!,
                    FullConfidence: fullConf,
                    DeterministicConfidence: detConf,
                    AiInvolved: aiInvolved,
                    Contributors: perKindMax));
            }

            targets.Sort((a, b) => b.FullConfidence.CompareTo(a.FullConfidence));

            winners.Add(new FieldWinner(
                field: fieldGroup.Key,
                targets: targets));
        }

        return winners;
    }

    private static void AddWrites(
        Dictionary<string, EntityReference> writes,
        List<FieldWinner> fieldWinners,
        bool useDeterministic)
    {
        foreach (var fw in fieldWinners)
        {
            if (fw.Conflict) continue; // never assert a conflicting field
            var winner = fw.Winner;
            var conf = useDeterministic ? winner.DeterministicConfidence : winner.FullConfidence;
            if (conf >= WriteFloor)
            {
                writes[fw.Field] = winner.Target;
            }
        }
    }

    private static List<CandidateTrace> BuildCandidateTraces(
        List<FieldWinner> fieldWinners,
        Dictionary<string, EntityReference> writes)
    {
        var traces = new List<CandidateTrace>();

        foreach (var fw in fieldWinners)
        {
            // Record the winner, plus any runner-up that participates in a high-confidence conflict.
            var relevant = fw.Conflict ? fw.Targets.Where(t => t.FullConfidence >= fw.ConflictFloor).ToList()
                                       : new List<TargetAgg> { fw.Winner };

            foreach (var t in relevant)
            {
                var written = writes.TryGetValue(fw.Field, out var w) && w.Id == t.Target.Id;
                traces.Add(new CandidateTrace
                {
                    Field = fw.Field,
                    TargetEntity = t.Target.LogicalName,
                    TargetId = t.Target.Id.ToString("D"),
                    ReinforcedConfidence = Math.Round(t.FullConfidence, 4),
                    DeterministicConfidence = Math.Round(t.DeterministicConfidence, 4),
                    Written = written,
                    Conflict = fw.Conflict,
                    Contributors = t.Contributors
                        .Select(c => new ContributorTrace
                        {
                            Rung = c.Rung.ToString(),
                            Confidence = Math.Round(Clamp(c.Confidence), 4),
                            Provenance = c.Provenance,
                        })
                        .ToList(),
                });
            }
        }

        return traces;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string BuildSuggestedReason(double topDet, double topFull, bool aiInvolved, AutoFileSettings settings)
    {
        if (topDet >= settings.Threshold && !settings.Enabled)
            return $"Deterministic reinforced confidence {topDet:F2} ≥ threshold {settings.Threshold:F2}, but auto-file kill-switch is OFF ⇒ suggest-only.";
        if (topDet < settings.Threshold && topFull >= settings.Threshold && aiInvolved)
            return $"Reinforced confidence {topFull:F2} ≥ threshold {settings.Threshold:F2} only with an AI rung; AI never auto-files ⇒ Suggested.";
        return $"Reinforced confidence {topFull:F2} in [{SuggestFloor:F2}, {settings.Threshold:F2}) ⇒ Suggested.";
    }

    /// <summary>
    /// Bounded signal-reinforcement combiner. Treats each confidence as independent evidence:
    /// <c>combined = 1 − Π(1 − cᵢ)</c>. Monotonic, saturates at 1.0, and returns the single value
    /// unchanged when there is only one contributor. Inputs are clamped to [0, 1].
    /// </summary>
    internal static double NoisyOr(IEnumerable<double> confidences)
    {
        var product = 1.0;
        var any = false;
        foreach (var c in confidences)
        {
            any = true;
            product *= 1.0 - Clamp(c);
        }
        return any ? 1.0 - product : 0.0;
    }

    private static double Clamp(double c) => c < 0.0 ? 0.0 : c > 1.0 ? 1.0 : c;

    private static bool IsDeterministic(RungKind kind) =>
        kind is RungKind.ExplicitReference or RungKind.ThreadContinuity
             or RungKind.ParticipantCorrelation or RungKind.StructuralDetector;

    // ── Internal working types ───────────────────────────────────────────────────

    private sealed record TargetAgg(
        EntityReference Target,
        double FullConfidence,
        double DeterministicConfidence,
        bool AiInvolved,
        IReadOnlyList<RungMatch> Contributors);

    private sealed class FieldWinner
    {
        public FieldWinner(string field, List<TargetAgg> targets)
        {
            Field = field;
            Targets = targets;
        }

        public string Field { get; }

        /// <summary>Distinct targets for this field, sorted by full confidence descending.</summary>
        public List<TargetAgg> Targets { get; }

        public TargetAgg Winner => Targets[0];

        public double FullConfidence => Winner.FullConfidence;
        public double DeterministicConfidence => Winner.DeterministicConfidence;
        public bool AiInvolved => Winner.AiInvolved;

        /// <summary>Conflict floor: a target is "high-confidence" for conflict purposes at ≥ this value.</summary>
        public double ConflictFloor { get; private set; } = double.NaN;

        /// <summary>
        /// True when 2+ distinct targets on this field are each high-confidence (≥ the auto-file threshold);
        /// evaluated lazily against the threshold the ladder resolved. The mapper sets this via
        /// <see cref="EvaluateConflict"/>.
        /// </summary>
        public bool Conflict { get; private set; }

        internal bool EvaluateConflict(double threshold)
        {
            ConflictFloor = threshold;
            Conflict = Targets.Count(t => t.FullConfidence >= threshold) >= 2;
            return Conflict;
        }
    }

    // NOTE: conflict evaluation depends on the resolved threshold, so it runs after gate resolution.
    // BuildFieldWinners produces the winners; the Decide method calls EvaluateConflict on each below.
}
