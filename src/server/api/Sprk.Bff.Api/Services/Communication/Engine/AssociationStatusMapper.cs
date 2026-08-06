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
///   <item>
///   C-1 narrowing (ADR-045 path-A exception, kill-switch-governed per ADR-018): rung 0 (ExplicitReference)
///   + rung 1 (ThreadContinuity) reinforced ≥ threshold, kill-switch on → <b>Resolved + auto-file</b>.
///   Rung 2 (ParticipantCorrelation) + rung 3 (StructuralDetector) still MATCH and contribute to full
///   confidence, but do NOT clear the auto-file bar by default → <b>Suggested</b>, unless the tenant
///   has <see cref="Configuration.AutoFileOptions.Rung2And3AutoFileEnabled"/> toggled on (legacy pre-C-1
///   behavior).
///   </item>
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

    /// <summary>Empty core set fallback when settings carry none (treated as "nothing auto-associates").</summary>
    private static readonly IReadOnlySet<string> EmptyCore = new HashSet<string>();

    /// <summary>
    /// Whether a resolved regarding target may be AUTO-ASSOCIATED — written to its <c>sprk_regarding*</c>
    /// lookup at capture — which is true ONLY when the target's ENTITY is in the tenant's core-writable set
    /// (<see cref="Configuration.AutoFileOptions.CoreWritableEntities"/>: matter + project + service request
    /// by default). A non-core target (contact / organization / account / invoice / work-assignment / event
    /// / budget / report-card / analysis) is surfaced as a <c>Suggested</c> review candidate but NEVER
    /// written automatically (owner rule, 061 UAT round-3, 2026-07-31: "only auto-associate to our core
    /// records; contacts/orgs/invoices/etc. can be suggestions the user associates, never auto-associated").
    /// <para>
    /// This is BOTH the auto-file-STATUS gate (only a core target can push a communication to
    /// <c>Resolved</c>, via <c>topDetCore</c>) AND the WRITE gate (<see cref="AddWrites"/> persists core
    /// fields only; non-core stays candidate-only). The set is resolved per-decision from the ADR-018 gate,
    /// so an operator retunes "core" without a redeploy. Superseded the earlier "fallback identity field"
    /// concept (which still WROTE contacts/orgs) — the owner tightened the rule from "don't auto-file on a
    /// contact" to "don't auto-associate a contact at all."
    /// </para>
    /// </summary>
    private static bool IsCoreWritable(EntityReference target, IReadOnlySet<string> coreEntities) =>
        coreEntities.Contains(target.LogicalName);

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

        var fieldWinners = BuildFieldWinners(writable, settings.Rung2And3AutoFileEnabled);

        // Conflict is "2+ distinct targets on the same field each ≥ the (tenant-resolved) threshold";
        // evaluate it now that the threshold is known.
        foreach (var fw in fieldWinners)
            fw.EvaluateConflict(settings.Threshold);

        // Ladder inputs.
        var anyConflict = fieldWinners.Any(f => f.Conflict);
        var topFull = fieldWinners.Count > 0 ? fieldWinners.Max(f => f.FullConfidence) : 0.0;
        var topDet = fieldWinners.Count > 0 ? fieldWinners.Max(f => f.DeterministicConfidence) : 0.0;
        // Auto-file eligibility keys off the top CORE-record deterministic winner — only a matter /
        // project / service request (the tenant's core-writable set) can push a communication to Resolved.
        // A non-core target (contact / organization / account / invoice / …) never clears the auto-file
        // bar and is never written automatically (owner rule, 061 UAT round-3).
        var coreEntities = settings.CoreWritableEntities ?? EmptyCore;
        var topDetCore = fieldWinners
            .Where(f => IsCoreWritable(f.Winner.Target, coreEntities))
            .Select(f => f.DeterministicConfidence)
            .DefaultIfEmpty(0.0)
            .Max();
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
            reason = $"Conflicting high-confidence matches (≥ {settings.Threshold:F2}) on field(s): {conflictFields}. Engine will not guess on those; other fields written for review.";
            // A conflict on ONE field (e.g. duplicate same-named projects — legitimate in production, never
            // auto-dedup'd) must NOT suppress a clean, unambiguous association on ANOTHER field (e.g. the one
            // exact-name matter). AddWrites skips the conflicting field(s) and writes the rest so the review UI
            // surfaces both the filed clean match AND the ambiguous choices (from provenance).
            AddWrites(writes, fieldWinners, coreEntities, useDeterministic: false);
        }
        else if (topFull < SuggestFloor)
        {
            status = AssociationStatusCodes.PendingReview;
            reason = $"Top reinforced confidence {topFull:F2} < suggest floor {SuggestFloor:F2}.";
        }
        else if (topDetCore >= settings.Threshold && settings.Enabled)
        {
            status = AssociationStatusCodes.Resolved;
            autoFiled = true;
            reason = $"Core-record deterministic reinforced confidence {topDetCore:F2} ≥ threshold {settings.Threshold:F2}; auto-file enabled ⇒ Resolved.";
            // Auto-file asserts only deterministic winners on CORE fields (AI-derived and non-core targets
            // are never auto-filed).
            AddWrites(writes, fieldWinners, coreEntities, useDeterministic: true);
        }
        else
        {
            status = AssociationStatusCodes.Suggested;
            // A high-confidence NON-CORE match (contact/org/invoice/…) that can't auto-file lands here: it is
            // NOT written — it is surfaced as a review candidate the user confirms (owner rule, round-3).
            reason = topDet >= settings.Threshold && topDetCore < settings.Threshold
                ? $"Only a non-core match (contact/organization/invoice/…) reached the threshold ({topDet:F2}); no core record (matter/project/service request) auto-filed ⇒ Suggested (confirm to associate)."
                : BuildSuggestedReason(topDet, topFull, aiInvolvedTop, settings);
            // Suggestions may include AI-derived fields; only CORE fields are written (AddWrites gate).
            AddWrites(writes, fieldWinners, coreEntities, useDeterministic: false);
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

    private static List<FieldWinner> BuildFieldWinners(IReadOnlyList<RungMatch> writable, bool includeRung23)
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
                // Auto-file STATUS confidence: C-1-narrowed set (rung 0+1, or 0–3 when the kill-switch
                // toggles rung 2/3 back on). Drives topDetSubstantive → the Resolved/auto-file decision.
                var detConf = NoisyOr(perKindMax.Where(m => IsAutoFileEligible(m.Rung, includeRung23)).Select(m => m.Confidence));
                // Deterministic WRITE confidence: the PRE-C-1 deterministic set (all rungs 0–3, never AI,
                // never RecordNameMatch/ContactNameMatch). Independent of the auto-file narrowing so that when
                // an email DOES auto-file, ALL its deterministic (rung 0–3) associations are still written to
                // Dataverse exactly as the shipped design specifies ("fallback matches are still WRITTEN...
                // they just don't clear the auto-file bar"). Consumed only by the Resolved-branch write gate.
                var writeConf = NoisyOr(perKindMax.Where(m => IsDeterministicWriteEligible(m.Rung)).Select(m => m.Confidence));
                // "AI involved" is TRUE only when a genuine AI rung contributed. RecordNameMatch is a
                // deterministic exact-match rung that is (intentionally) not auto-file-eligible — it must NOT
                // be mislabeled as AI in the provenance/reason.
                var aiInvolved = perKindMax.Any(m => IsAi(m.Rung));

                targets.Add(new TargetAgg(
                    Target: contributors[0].Target!,
                    FullConfidence: fullConf,
                    DeterministicConfidence: detConf,
                    WriteConfidence: writeConf,
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
        IReadOnlySet<string> coreEntities,
        bool useDeterministic)
    {
        foreach (var fw in fieldWinners)
        {
            if (fw.Conflict) continue; // never assert a conflicting field
            var winner = fw.Winner;
            // Surface-only rungs (DocumentAssociation / F1) contribute review CANDIDATES but are NEVER written
            // as a filed association (061 UAT round-2): a document's record is INDIRECT, twice-removed evidence,
            // so it must be confirmed by the reviewer (r5 "Suggested · confirm to link"), not auto-linked and
            // shown as "Filed automatically". A field whose winning target has ONLY surface-only contributors is
            // surfaced as a candidate (BuildCandidateTraces) but skipped here; if a real rung also matched the
            // same target, it writes normally (the surface-only rung merely reinforced).
            if (winner.Contributors.All(c => IsSurfaceOnly(c.Rung)))
                continue;
            // Non-core target (contact / organization / account / invoice / work-assignment / …): surface as a
            // review candidate (BuildCandidateTraces) but NEVER write it automatically — only CORE records
            // (matter / project / service request, per the tenant's core-writable set) are auto-associated
            // (owner rule, 061 UAT round-3). The user confirms a non-core suggestion through r5's review
            // surface — a separate, user-initiated write path — so nothing is lost; it just isn't auto-filed.
            if (!IsCoreWritable(winner.Target, coreEntities))
                continue;
            // Resolved branch (useDeterministic) writes the PRE-C-1 deterministic set (rung 0–3 via
            // WriteConfidence) so no fallback/structural association is dropped when an email auto-files;
            // Suggested branch writes on FullConfidence (may include AI-derived fields).
            var conf = useDeterministic ? winner.WriteConfidence : winner.FullConfidence;
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
            // Which targets to surface in the review trace for this field.
            List<TargetAgg> relevant;
            if (fw.Conflict)
            {
                // High-confidence conflict (2+ targets each ≥ threshold) → surface each conflicting
                // target so the review UI can present the Ambiguous choice.
                relevant = fw.Targets.Where(t => t.FullConfidence >= fw.ConflictFloor).ToList();
            }
            else
            {
                // No high-confidence conflict, but a field can still carry MULTIPLE valid review
                // candidates below the auto-file threshold — e.g. two contacts both named in the body
                // each emit a Suggested-band sprk_regardingperson match (R4 UAT-R2-B1). Surface EVERY
                // candidate worth reviewing (≥ the suggest floor), not just the single winner, so the
                // reviewer sees and picks among all of them. Owner principle: "surface all matches, user
                // picks primary; never auto-dedup." Only the winner is WRITTEN (a single-value lookup
                // can hold one); the rest are review-only (Written = false).
                relevant = fw.Targets.Where(t => t.FullConfidence >= SuggestFloor).ToList();
                if (relevant.Count == 0) relevant.Add(fw.Winner);
            }

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

    /// <summary>
    /// Rungs whose confidence is AUTO-FILE-ELIGIBLE (a match here can push a substantive target to Resolved).
    /// C-1 narrowing (ADR-045 path-A exception, email-communication-intelligence-r1): by default (
    /// <paramref name="includeRung23"/> = <c>false</c>) only rung 0 (<see cref="RungKind.ExplicitReference"/>)
    /// and rung 1 (<see cref="RungKind.ThreadContinuity"/>) are eligible — misfiling is the #1 trust-killer,
    /// so participant/sender-only (rung 2) and structural (rung 3) matches still contribute to full
    /// confidence and are written as Suggested candidates, but do not clear the auto-file bar alone.
    /// <paramref name="includeRung23"/> = <c>true</c> restores the legacy pre-C-1 behavior (rungs 0–3 all
    /// eligible) — kill-switch-governed per ADR-018 via <see cref="Configuration.AutoFileOptions.Rung2And3AutoFileEnabled"/>,
    /// togglable without a redeploy. Deliberately EXCLUDES <see cref="RungKind.RecordNameMatch"/> regardless
    /// of the flag: per owner spec (2026-07-17) a name match is surfaced for review (the user picks the
    /// primary among matches), never auto-filed.
    /// </summary>
    private static bool IsAutoFileEligible(RungKind kind, bool includeRung23) =>
        // RecipientAlias (FR-A2) is a per-record intake address — a deliberate, unambiguous routing
        // instruction as authoritative as an explicit subject reference, so it is auto-file-eligible
        // UNCONDITIONALLY (rung-0 tier), not gated behind the rung-2/3 kill-switch. This does not widen the
        // C-1 misfile surface: C-1 narrows auto-file to EXPLICIT deterministic signals precisely to avoid
        // misfiling on weaker participant/structural inference — an alias resolved to one specific record is
        // the strongest explicit signal there is, so it belongs with ExplicitReference/ThreadContinuity.
        kind is RungKind.ExplicitReference or RungKind.ThreadContinuity or RungKind.RecipientAlias
             || (includeRung23 && kind is RungKind.ParticipantCorrelation or RungKind.StructuralDetector);

    /// <summary>
    /// Rungs whose confidence is DETERMINISTIC-WRITE-ELIGIBLE — the PRE-C-1 deterministic set (all four
    /// hard-deterministic rungs 0–3). This is the WRITE set for the Resolved branch and is INDEPENDENT of
    /// the C-1 auto-file narrowing (<see cref="IsAutoFileEligible"/>): the narrowing changes only WHICH rungs
    /// can push a communication to auto-file STATUS, not which deterministic associations are persisted once
    /// it does. When an email auto-files, all rung 0–3 associations (incl. participant/structural fallbacks)
    /// are still written — the shipped design keeps fallback matches WRITTEN even though they don't clear the
    /// auto-file bar; r5's review surface displays these denormalized associations. Excludes
    /// <see cref="RungKind.RecordNameMatch"/> / <see cref="RungKind.ContactNameMatch"/> and AI rungs exactly
    /// as the deterministic write set always has.
    /// </summary>
    private static bool IsDeterministicWriteEligible(RungKind kind) =>
        kind is RungKind.ExplicitReference or RungKind.ThreadContinuity
             or RungKind.ParticipantCorrelation or RungKind.StructuralDetector
             // RecipientAlias (FR-A2) is a hard-deterministic rung-0 signal — written like ExplicitReference
             // when a communication auto-files, so its association is never dropped.
             or RungKind.RecipientAlias;

    /// <summary>Genuine AI rungs (semantic + LLM classify) — these set the provenance "AI involved" flag.</summary>
    private static bool IsAi(RungKind kind) =>
        kind is RungKind.SemanticMatch or RungKind.AiClassification;

    /// <summary>
    /// Surface-only rungs whose matches are review CANDIDATES but are NEVER written as a filed association nor
    /// auto-filed — INDIRECT evidence the reviewer confirms (061 UAT round-2). Currently the F1
    /// attachment→document rung (<see cref="RungKind.DocumentAssociation"/>): a document's own record links are
    /// twice-removed from the email, so they are suggested ("confirm to link"), not auto-linked. Distinct from
    /// <see cref="RungKind.RecordNameMatch"/> (a DIRECT name/number appearance in the email text, still written).
    /// </summary>
    private static bool IsSurfaceOnly(RungKind kind) =>
        kind is RungKind.DocumentAssociation;

    // ── Internal working types ───────────────────────────────────────────────────

    private sealed record TargetAgg(
        EntityReference Target,
        double FullConfidence,
        double DeterministicConfidence,
        double WriteConfidence,
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
