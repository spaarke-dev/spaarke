using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The before/after findings diff of one change to an invoice under review (ebilling-r1 task 164a-3, extracted from
/// <see cref="InvoiceCorrectionReevaluation"/>): the evaluation request rebuilt from the stored rows on each side of the change
/// (<see cref="RequestBasis"/>), and the patch of the stored findings JSON with the difference of the two evaluations (<see cref="PatchFindings"/>,
/// <see cref="PatchPlan"/>, over the Signal subjects of <see cref="SubjectsOf"/>). A change is either one field correction (task 101) or one line a
/// reviewer adds (task 164, Add line). Pure: it reads and writes nothing; the caller loads the facts once, runs the evaluator on both requests and
/// writes the patched JSON.
/// </summary>
/// <remarks>
/// <para><b>Line index.</b> A line's index is its <c>sprk_linesequence</c> - 1 (the persister's key, <see cref="StoredLine"/>). An added line takes
/// the highest sequence + 1, so it is a new index on the "after" side only, and the diff sees its findings and Signal group as added.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceCorrectionReevaluation"/>, whose private request builder
/// and patch helpers this is. Extension: this IS the extension; the correction follow-through calls it unchanged and Add line calls it with an
/// added-line basis, so the rules run for a new line through the same builder and the same pure evaluator, never a copy. Cost of doing nothing:
/// Add line copies the builder and the patch, and the copy drifts from the correction path the stored findings were built by.</para>
/// </remarks>
internal static class InvoiceFindingsDiff
{
    /// <summary>A fee or expense line of the invoice as the diff reads it: its index (<c>sprk_linesequence</c> - 1), its row, its parsed extraction
    /// JSON (null when it has none or it is unreadable) and whether a reviewer has decided it (Accepted, Reduced, Disallowed).</summary>
    internal sealed record StoredLine(int Index, Entity Row, JsonObject? Extraction, bool Decided);

    /// <summary>The roster match, approved rate and reasons a timekeeper or date correction is diffed FROM.</summary>
    internal sealed record ResolutionFacts(Guid? TimekeeperId, decimal? ApprovedRate, TimekeeperUnmatchedReason TimekeeperReason, RateResolutionStatus RateStatus, RateNotFoundReason RateReason);

    /// <summary>The gaps of an evaluation as (Procedure, kind, subject) triples: a subject is a line index, or -1 for the invoice itself.</summary>
    internal static HashSet<(Guid ProcedureId, ProcedureGapKind Kind, int Subject)> GapSubjects(BillingEvaluationResult result) =>
        result.Gaps
            .SelectMany(g => g.LineIndexes.Select(line => (g.Gap.ProcedureId, g.Gap.Kind, line))
                .Concat(g.AppliesToInvoice ? [(g.Gap.ProcedureId, g.Gap.Kind, -1)] : []))
            .ToHashSet();

    // ── The reads both kinds of change share (task 164a-4a: moved from InvoiceCorrectionReevaluation, unchanged apart from the store passed in) ──

    /// <summary>One correction row of the invoice as the diff reads it: its line (none for a header field), field, AI and final value, and whether it is a
    /// label (Corrected or Confirmed individually).</summary>
    internal sealed record CorrectionRow(Guid Id, Guid? LineId, string Field, string? Ai, string? Final, bool IsLabel);

    private static readonly string[] CorrectionColumns =
        ["sprk_fieldname", "sprk_aivalue", "sprk_finalvalue", "sprk_outcometype", "sprk_invoiceline", "sprk_reviewedon", "createdon"];

    /// <summary>The invoice's fee and expense lines by index (<c>sprk_linesequence</c> - 1, the persister's key); Adjustment lines are not evaluated.
    /// App-only, through <paramref name="entities"/>.</summary>
    internal static async Task<IReadOnlyDictionary<int, StoredLine>> ReadLinesAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.LineEntity)
        {
            ColumnSet = new ColumnSet(InvoiceReviewReadService.LineColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        query.AddOrder(InvoiceSubmissionPersister.LineSequence, OrderType.Ascending);
        var lines = new Dictionary<int, StoredLine>();
        foreach (var row in await InvoiceReviewReadService.ReadPagedAsync(entities, query, ct).ConfigureAwait(false))
        {
            if (row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is not ((int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense)
                || row.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) is not { } sequence || sequence < 1)
            {
                continue;
            }

            JsonObject? extraction = null;
            if (row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExtractionConfidence) is { Length: > 0 } text)
            {
                try
                {
                    extraction = JsonNode.Parse(text) as JsonObject;
                }
                catch (JsonException)
                {
                    extraction = null;
                }
            }

            var status = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
            var decided = status is (int)InvoiceLineStatus.Accepted or (int)InvoiceLineStatus.Reduced or (int)InvoiceLineStatus.Disallowed;
            if (!lines.TryAdd(sequence - 1, new StoredLine(sequence - 1, row, extraction, decided)))
            {
                throw new InvalidOperationException("Two lines of the invoice share a sequence.");
            }
        }

        return lines;
    }

    /// <summary>Every correction row of the invoice, oldest first (the order the correction service reads them in). App-only, through <paramref name="entities"/>.</summary>
    internal static async Task<IReadOnlyList<CorrectionRow>> ReadCorrectionsAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceLineDecisionService.CorrectionEntity)
        {
            ColumnSet = new ColumnSet(CorrectionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_invoice", ConditionOperator.Equal, invoiceId);
        query.AddOrder($"{InvoiceLineDecisionService.CorrectionEntity}id", OrderType.Ascending);
        var rows = await InvoiceReviewReadService.ReadPagedAsync(entities, query, ct).ConfigureAwait(false);
        return rows
            .OrderBy(r => r.GetAttributeValue<DateTime?>("sprk_reviewedon") ?? r.GetAttributeValue<DateTime?>("createdon") ?? DateTime.MinValue)
            .ThenBy(r => r.GetAttributeValue<DateTime?>("createdon"))
            .Select(r => new CorrectionRow(
                r.Id,
                r.GetAttributeValue<EntityReference>("sprk_invoiceline")?.Id,
                r.GetAttributeValue<string>("sprk_fieldname") ?? string.Empty,
                r.GetAttributeValue<string>("sprk_aivalue"),
                r.GetAttributeValue<string>("sprk_finalvalue"),
                r.GetAttributeValue<OptionSetValue>("sprk_outcometype")?.Value is InvoiceCorrectionService.OutcomeCorrected or InvoiceCorrectionService.OutcomeConfirmed))
            .ToList();
    }

    /// <summary>The fields a reviewer has seen (any Corrected / Confirmed row), by line (none for a header field): the <c>verified</c> set of a basis.</summary>
    internal static IReadOnlySet<(Guid? LineId, string Field)> VerifiedOf(IReadOnlyList<CorrectionRow> rows) =>
        rows.Where(r => r.IsLabel).Select(r => (r.LineId, r.Field)).ToHashSet();

    // ── The patch ──

    private static string KeyOf(BillingFinding f) =>
        string.Join('|', f.PolicyVersionId.ToString("D"), f.Subject.Kind.ToString(), f.Subject.LineIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, f.ReasonCode);

    private static string? KeyOf(JsonObject entry) =>
        Text(entry, "policyVersionId") is { } version && Guid.TryParse(version, out var id) && Text(entry, "subject") is { } subject && Text(entry, "reasonCode") is { } reason
            ? string.Join('|', id.ToString("D"), subject, IntOf(entry["line"])?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, reason)
            : null;

    /// <summary><c>findings[]</c>: per key, the entries that differ between the two evaluations are replaced, added or removed.</summary>
    internal static void PatchFindings(JsonObject root, BillingEvaluationResult before, BillingEvaluationResult after, IReadOnlySet<int> decided, HashSet<int> touched)
    {
        if (root["findings"] is not JsonArray stored)
        {
            root["findings"] = stored = [];
        }

        var beforeByKey = before.Findings.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var afterByKey = after.Findings.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var withheld = after.Findings.Where(f => f.DetailWithheld).Select(f => (f.PolicyId, f.Subject)).ToHashSet();

        // A new entry takes an index no stored entry (nor 073's deciding list) has used, so a removed finding's index is never reused.
        var next = stored.OfType<JsonObject>().Select(e => IntOf(e["index"]) ?? -1)
            .Concat((root["decision"]?["deciding"] as JsonArray ?? []).Select(IntOf).Select(i => i ?? -1))
            .DefaultIfEmpty(-1).Max() + 1;
        foreach (var key in beforeByKey.Keys.Union(afterByKey.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var was = beforeByKey.GetValueOrDefault(key) ?? [];
            var now = afterByKey.GetValueOrDefault(key) ?? [];
            if (Same(was, now))
            {
                continue;
            }

            var line = (was.Count > 0 ? was[0] : now[0]).Subject.LineIndex;
            if (line is { } decidedLine && decided.Contains(decidedLine))
            {
                continue;
            }

            if (line is { } index)
            {
                touched.Add(index);
            }

            var existing = stored.OfType<JsonObject>().Where(e => KeyOf(e) == key).ToList();
            for (var i = 0; i < Math.Max(existing.Count, now.Count); i++)
            {
                if (i >= now.Count)
                {
                    stored.Remove(existing[i]);
                    continue;
                }

                var finding = now[i];
                var scrub = withheld.Contains((finding.PolicyId, finding.Subject));
                if (i < existing.Count)
                {
                    var slot = stored.IndexOf(existing[i]);
                    var decision = DecisionOf(existing[i], finding, after);
                    stored[slot] = InvoiceSubmissionPersister.FindingEntryJson(IntOf(existing[i]["index"]) ?? next++, finding, decision, scrub);
                }
                else
                {
                    stored.Add(InvoiceSubmissionPersister.FindingEntryJson(next++, finding, DecisionOf(null, finding, after), scrub));
                }
            }
        }

        static bool Same(List<BillingFinding> a, List<BillingFinding> b) =>
            a.Count == b.Count && a.Zip(b).All(p => JsonNode.DeepEquals(
                InvoiceSubmissionPersister.FindingEntryJson(0, p.First, null, scrub: false),
                InvoiceSubmissionPersister.FindingEntryJson(0, p.Second, null, scrub: false)));
    }

    /// <summary>A replaced entry keeps 073's decision members as stored; a new one carries the rule's consequence and does not reject (no 073 re-run).</summary>
    private static FindingDecision DecisionOf(JsonObject? stored, BillingFinding finding, BillingEvaluationResult after)
    {
        BillingConsequence? consequence = null;
        if (stored is not null && Enum.TryParse<BillingConsequence>(Text(stored, "consequence"), out var kept))
        {
            consequence = kept;
        }
        else if (after.RulesByVersion.TryGetValue(finding.PolicyVersionId, out var rule) && rule.Consequence is { } value
            && Enum.IsDefined(typeof(BillingConsequence), value))
        {
            consequence = (BillingConsequence)value;
        }

        return new FindingDecision
        {
            Index = 0,
            Finding = finding,
            Consequence = consequence,
            Rejects = stored?["rejects"] is JsonValue rejects && rejects.TryGetValue<bool>(out var r) && r,
            HeldBecause = stored is null ? null : Text(stored, "heldBecause"),
            ValuesRead = finding.ValuesRead,
        };
    }

    /// <summary>The Signal subjects of the invoice and of <paramref name="lines"/> (index to row id and line name). For an added line, pass
    /// <see cref="RequestBasis.LinesAfter"/>, so the added line is a subject of the "after" side.</summary>
    internal static BillingSignalSubjects SubjectsOf(Guid invoiceId, JsonObject root, IReadOnlyDictionary<int, StoredLine> lines) => new(
        invoiceId,
        Text(root, "invoiceName"),
        lines.ToDictionary(l => l.Key, l => l.Value.Row.Id),
        lines.ToDictionary(l => l.Key, l => l.Value.Row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineName) ?? string.Empty));

    internal static string PlanKey(string? policyCode, string? entity, string? subjectId) =>
        string.Join('|', policyCode, entity, Guid.TryParse(subjectId, out var id) ? id.ToString("D") : subjectId);

    /// <summary>
    /// 073's stored decision of each finding of <paramref name="result"/> (B2b-2): the n-th finding of a key takes the n-th stored entry of that key in
    /// <paramref name="stored"/> (<see cref="DecisionOf"/>: its <c>consequence</c>, <c>rejects</c>, <c>heldBecause</c>); a finding with no stored entry
    /// carries the rule's consequence and does not reject. Null when the stored findings carry no 073 decision (the persister mapped without one).
    /// </summary>
    private static IReadOnlyList<FindingDecision>? StoredDecisions(JsonObject root, BillingEvaluationResult result, JsonArray stored)
    {
        if (root["decision"] is not JsonObject)
        {
            return null;
        }

        var byKey = stored.OfType<JsonObject>().GroupBy(e => KeyOf(e) ?? string.Empty).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var decisions = new List<FindingDecision>(result.Findings.Count);
        for (var i = 0; i < result.Findings.Count; i++)
        {
            var finding = result.Findings[i];
            var key = KeyOf(finding);
            var n = seen.GetValueOrDefault(key);
            seen[key] = n + 1;
            var entry = byKey.TryGetValue(key, out var entries) && n < entries.Count ? entries[n] : null;
            decisions.Add(DecisionOf(entry, finding, result) with { Index = i });
        }

        return decisions;
    }

    /// <summary>The stored outcome kind, passed to the mapper only with 073's decisions (a Blocked outcome needs them).</summary>
    private static SubmissionEvaluationKind? StoredKind(JsonObject root) =>
        Enum.TryParse<SubmissionEvaluationKind>(Text(root, "kind"), out var kind) ? kind : null;

    /// <summary>
    /// <c>signalPlan[]</c>: per (policy, subject), the entry is replaced, added or removed when the mapped group differs; the change is recorded for B2b-2.
    /// Both sides are mapped with 073's stored decisions and the stored kind (<see cref="StoredDecisions"/>). A stored Blocked entry the after side would
    /// present otherwise is kept and marked <see cref="InvoiceCorrectionReevaluation.CorrectionReviewMember"/>; a replacement the writer would refuse is
    /// recorded in <see cref="InvoiceSubmissionPersister.SignalPlanRefusalsMember"/>.
    /// </summary>
    internal static void PatchPlan(
        JsonObject root, BillingEvaluationResult before, BillingEvaluationResult after, JsonArray storedFindings, BillingSignalSubjects subjects,
        IReadOnlySet<int> decided, HashSet<int> touched)
    {
        if (root["signalPlan"] is not JsonArray plan)
        {
            root["signalPlan"] = plan = [];
        }

        var lineByRow = subjects.LineIds.ToDictionary(p => p.Value, p => p.Key);
        var beforeDecisions = StoredDecisions(root, before, storedFindings);
        var afterDecisions = StoredDecisions(root, after, root["findings"] as JsonArray ?? []);
        var kind = beforeDecisions is null ? null : StoredKind(root);
        var was = Entries(BillingFindingSignalMapper.MapAll(before, subjects, beforeDecisions, kind));
        var now = Entries(BillingFindingSignalMapper.MapAll(after, subjects, afterDecisions, kind));
        foreach (var key in was.Keys.Union(now.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var old = was.GetValueOrDefault(key);
            var current = now.GetValueOrDefault(key);
            if (old is not null && current is not null && JsonNode.DeepEquals(old.Value.Entry, current.Value.Entry))
            {
                continue;
            }

            var sample = (current ?? old)!.Value.Mapping.Request;
            if (sample.SubjectEntityLogicalName == BillingFindingSignalMapper.LineEntity && lineByRow.TryGetValue(sample.SubjectId, out var line))
            {
                if (decided.Contains(line))
                {
                    continue;
                }

                touched.Add(line);
            }

            var stored = plan.OfType<JsonObject>().FirstOrDefault(e => PlanKey(Text(e, "policyCode"), Text(e, "subjectEntity"), Text(e, "subjectId")) == key);
            if (stored is not null && current is { } already && JsonNode.DeepEquals(stored, already.Entry))
            {
                // Fix round, part 3: a repeat whose patch already landed. The plan holds the entry; its Signal change is either still recorded (the
                // Signals step has not finished) or done; recording it again would only make the Signals step write the same entry once more.
                continue;
            }

            var storedFingerprint = stored is null ? null : Text(stored, "fingerprint");

            // A correction never drops a hold: a stored entry presented Blocked stays as stored (its Signal too) when the after side would not present
            // it Blocked; it is marked for the reviewer, who leaves the hold through override or return (105).
            if (stored is not null && Text(stored, "decision") == nameof(BillingGroupDecision.Blocked)
                && current is not { Mapping.Decision: BillingGroupDecision.Blocked })
            {
                stored[InvoiceCorrectionReevaluation.CorrectionReviewMember] = true;
                continue;
            }

            string? raiseFingerprint = null;
            var refusal = current is { } candidate ? SignalWriter.PreflightRefusal(candidate.Mapping.Request) : null;
            if (refusal is not null)
            {
                RecordPlanRefusal(root, sample, refusal);
            }

            if (current is { } replacement && refusal is null)
            {
                if (stored is null)
                {
                    plan.Add(replacement.Entry.DeepClone());
                }
                else
                {
                    plan[plan.IndexOf(stored)] = replacement.Entry.DeepClone();
                }

                raiseFingerprint = replacement.Mapping.Fingerprint;
            }
            else if (stored is not null)
            {
                plan.Remove(stored);
            }

            RecordRuleChange(root, sample, storedFingerprint, raiseFingerprint);
        }

        static Dictionary<string, (BillingSignalMapping Mapping, JsonObject Entry)?> Entries(IReadOnlyList<BillingSignalMapping> mappings) =>
            mappings.ToDictionary(
                m => PlanKey(m.Request.PolicyCode, m.Request.SubjectEntityLogicalName, m.Request.SubjectId.ToString("D")),
                m => ((BillingSignalMapping, JsonObject)?)(m, InvoiceSubmissionPersister.SignalPlanEntryJson(m)),
                StringComparer.Ordinal);
    }

    /// <summary>A replacement the writer's preflight refuses is left out of the plan and recorded as the persister records one (049 F1-3), once.</summary>
    private static void RecordPlanRefusal(JsonObject root, SignalWriteRequest request, string code)
    {
        if (root[InvoiceSubmissionPersister.SignalPlanRefusalsMember] is not JsonArray refusals)
        {
            root[InvoiceSubmissionPersister.SignalPlanRefusalsMember] = refusals = [];
        }

        var entry = InvoiceSubmissionPersister.SignalPlanRefusalEntryJson(
            new InvoiceSubmissionPersister.SignalPlanRefusal(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, code));
        if (!refusals.Any(r => JsonNode.DeepEquals(r, entry)))
        {
            refusals.Add(entry);
        }
    }

    /// <summary>The <see cref="InvoiceCorrectionReevaluation.PendingSignalChangesMember"/> object of <paramref name="root"/>, created with empty
    /// <c>rule</c> and <c>system</c> arrays when missing.</summary>
    internal static JsonObject Pending(JsonObject root)
    {
        if (root[InvoiceCorrectionReevaluation.PendingSignalChangesMember] is not JsonObject pending)
        {
            root[InvoiceCorrectionReevaluation.PendingSignalChangesMember] = pending = new JsonObject { ["rule"] = new JsonArray(), ["system"] = new JsonArray() };
        }

        if (pending["rule"] is not JsonArray)
        {
            pending["rule"] = new JsonArray();
        }

        if (pending["system"] is not JsonArray)
        {
            pending["system"] = new JsonArray();
        }

        return pending;
    }

    /// <summary>One <c>rule</c> entry per (policy, subject): the fingerprint to close is the one stored before the FIRST change, the one to raise the latest.</summary>
    private static void RecordRuleChange(JsonObject root, SignalWriteRequest request, string? storedFingerprint, string? raiseFingerprint)
    {
        var key = PlanKey(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId.ToString("D"));
        var entry = (root[InvoiceCorrectionReevaluation.PendingSignalChangesMember]?["rule"] as JsonArray ?? [])
            .OfType<JsonObject>().FirstOrDefault(e => PlanKey(Text(e, "policyCode"), Text(e, "subjectEntity"), Text(e, "subjectId")) == key);
        if (entry is null)
        {
            if (storedFingerprint is null && raiseFingerprint is null)
            {
                return;
            }

            ((JsonArray)Pending(root)["rule"]!).Add(new JsonObject
            {
                ["policyCode"] = request.PolicyCode,
                ["subjectEntity"] = request.SubjectEntityLogicalName,
                ["subjectId"] = request.SubjectId.ToString("D"),
                ["closeFingerprint"] = storedFingerprint,
                ["raiseFingerprint"] = raiseFingerprint,
            });
            return;
        }

        entry["raiseFingerprint"] = raiseFingerprint;
        if (Text(entry, "closeFingerprint") is null && raiseFingerprint is null)
        {
            ((JsonArray)entry.Parent!).Remove(entry);
        }
    }

    // ── The request, rebuilt from the stored rows ──

    /// <summary>
    /// The evaluation request on each side of ONE change, rebuilt from the stored rows. Two kinds:
    /// <list type="bullet">
    /// <item><b>A field correction</b> (<see cref="ForCorrection"/>, task 101): both sides hold the same lines; the corrected field is at its
    /// pre-correction value on the "before" side and its final value on the "after" side (<see cref="Build(string, bool, bool)"/>).</item>
    /// <item><b>An added line</b> (<see cref="ForAddedLine"/>, task 164): the "before" side holds the stored lines, the "after" side the stored lines
    /// plus the added one (<see cref="Build(bool)"/>). The added line is passed as the row the caller is about to create, and is read exactly as every
    /// later rebuild reads it from its stored row (cost type, date, timekeeper as billed and roster match, approved rate, hours, rate, amount,
    /// narrative, codes as billed and as coded, field confidence from its extraction JSON). So a correction made after the add diffs from the same facts
    /// the add evaluated; reading the line any other way would show a spurious difference on the next correction.</item>
    /// </list>
    /// </summary>
    internal sealed class RequestBasis
    {
        private readonly CorrectionApplied? _applied;
        private readonly IReadOnlyDictionary<int, StoredLine> _lines;
        private readonly Entity _invoice;
        private readonly ExtractionProvenance.SubmissionProvenance? _provenance;
        private readonly IReadOnlySet<(Guid? LineId, string Field)> _verified;
        private readonly Guid _submissionId;
        private readonly JsonObject _root;
        private readonly (string Entity, Guid Id) _core;
        private readonly ResolutionFacts? _resolutionBefore;

        private RequestBasis(
            CorrectionApplied? applied, StoredLine? added, Entity invoice, IReadOnlyDictionary<int, StoredLine> lines,
            ExtractionProvenance.SubmissionProvenance? provenance, IReadOnlySet<(Guid? LineId, string Field)> verified, Guid submissionId, JsonObject root,
            (string Entity, Guid Id) core, ResolutionFacts? resolutionBefore)
        {
            _applied = applied;
            Added = added;
            _invoice = invoice;
            _lines = lines;
            _provenance = provenance;
            _verified = verified;
            _submissionId = submissionId;
            _root = root;
            _core = core;
            _resolutionBefore = resolutionBefore;
            LinesAfter = added is null ? lines : new Dictionary<int, StoredLine>(lines) { [added.Index] = added };
        }

        /// <summary>The added line of an added-line basis; null for a correction.</summary>
        public StoredLine? Added { get; }

        /// <summary>The lines of the "after" side: the stored lines, plus the added line of an added-line basis. Pass them to <see cref="SubjectsOf"/>.</summary>
        public IReadOnlyDictionary<int, StoredLine> LinesAfter { get; }

        /// <summary>A field correction (task 101): <paramref name="applied"/> names the field and its line (none for a header field);
        /// <paramref name="resolutionBefore"/> is the roster match and rate a timekeeper or date correction is diffed from (null otherwise).</summary>
        public static RequestBasis ForCorrection(
            CorrectionApplied applied, Entity invoice, IReadOnlyDictionary<int, StoredLine> lines, ExtractionProvenance.SubmissionProvenance? provenance,
            IReadOnlySet<(Guid? LineId, string Field)> verified, Guid submissionId, JsonObject root, (string Entity, Guid Id) core, ResolutionFacts? resolutionBefore) =>
            new(applied, added: null, invoice, lines, provenance, verified, submissionId, root, core, resolutionBefore);

        /// <summary>
        /// An added line (task 164): <paramref name="added"/> is the line about to be created, undecided, a fee or expense line whose index is its
        /// <c>sprk_linesequence</c> - 1 and comes after every stored line (the highest sequence + 1). <paramref name="verified"/> is the same set
        /// <see cref="InvoiceCorrectionReevaluation"/> passes (the fields with a Corrected / Confirmed row), so both sides read the stored lines as the stored
        /// findings saw them. Throws <see cref="ArgumentException"/> for a line that breaks any of these.
        /// </summary>
        public static RequestBasis ForAddedLine(
            StoredLine added, Entity invoice, IReadOnlyDictionary<int, StoredLine> lines, ExtractionProvenance.SubmissionProvenance? provenance,
            IReadOnlySet<(Guid? LineId, string Field)> verified, Guid submissionId, JsonObject root, (string Entity, Guid Id) core)
        {
            ArgumentNullException.ThrowIfNull(added);
            ArgumentNullException.ThrowIfNull(lines);
            if (added.Row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value is not ((int)InvoiceLineCostType.Fee or (int)InvoiceLineCostType.Expense))
            {
                throw new ArgumentException("The added line is not a fee or expense line.", nameof(added));
            }

            if (added.Index < 0 || added.Row.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) != added.Index + 1)
            {
                throw new ArgumentException("The added line's index is not its sequence - 1.", nameof(added));
            }

            if (lines.Count > 0 && added.Index <= lines.Keys.Max())
            {
                throw new ArgumentException("The added line does not come after every stored line.", nameof(added));
            }

            if (lines.Values.Any(l => l.Row.Id == added.Row.Id))
            {
                throw new ArgumentException("The added line's id is already a line of the invoice.", nameof(added));
            }

            if (added.Decided)
            {
                throw new ArgumentException("An added line is undecided.", nameof(added));
            }

            return new(applied: null, added, invoice, lines, provenance, verified, submissionId, root, core, resolutionBefore: null);
        }

        /// <summary>A correction basis: the request with the corrected field at <paramref name="value"/>; <paramref name="preVerified"/>: the field counts
        /// as verified (confidence 1.0); <paramref name="before"/>: the side of the diff before the correction (a timekeeper or date correction reads the
        /// line's pre-correction roster match and rate).</summary>
        public BillingEvaluationRequest Build(string? value, bool preVerified, bool before) =>
            _applied is null
                ? throw new InvalidOperationException("An added-line basis is built with Build(before).")
                : BuildSide(value, preVerified, before, _lines);

        /// <summary>An added-line basis: the request without the added line (<paramref name="before"/>) or with it.</summary>
        public BillingEvaluationRequest Build(bool before) =>
            Added is null
                ? throw new InvalidOperationException("A correction basis is built with Build(value, preVerified, before).")
                : BuildSide(value: null, preVerified: false, before, before ? _lines : LinesAfter);

        private BillingEvaluationRequest BuildSide(string? value, bool preVerified, bool before, IReadOnlyDictionary<int, StoredLine> lines)
        {
            // A header field as the evaluator saw it: the corrected field at the value of this side; a field a reviewer corrected or confirmed at its
            // column (the correction wrote it there); any other at the value the analyzer read (the header provenance: the invoice number column
            // holds the number the submitter typed, not the one read), else its column.
            string? Header(string name)
            {
                if (_applied is { LineId: null } applied && applied.Field == name)
                {
                    return value;
                }

                if (!_verified.Contains(((Guid?)null, name)) && _provenance is not null
                    && _provenance.Header.TryGetValue(name, out var read) && read.Value is not null)
                {
                    return read.Value;
                }

                return ColumnText(name);
            }

            var confidence = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (name, field) in _provenance?.Header ?? new Dictionary<string, ExtractionProvenance.HeaderField>())
            {
                if (field.Combined is { } combined)
                {
                    confidence[name] = combined;
                }
            }

            foreach (var name in ExtractionProvenance.HeaderFieldNames.Where(n => IsVerified(null, n, preVerified)))
            {
                confidence[name] = 1m;
            }

            return new BillingEvaluationRequest
            {
                SubmissionId = _submissionId,
                FirmId = _invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id is { } firm && firm != Guid.Empty
                    ? firm
                    : throw new CorrectionFirmMissingException(),
                CoreRecord = new CoreRecordReference(
                    _core.Entity switch { "sprk_matter" => CoreRecordKind.Matter, "sprk_project" => CoreRecordKind.Project, _ => CoreRecordKind.WorkAssignment },
                    _core.Id),
                TrackingNumber = Text(_root, "trackingNumber"),
                FirmInvoiceNumber = Header(BillingFieldNames.FirmInvoiceNumber),
                InvoiceDate = DateOf(Header(BillingFieldNames.InvoiceDate)),
                ReceivedOn = DateOf(Text(_root, "receivedOn")) ?? throw new InvalidOperationException("The stored findings have no received date."),
                BillingPeriodStart = DateOf(Header(BillingFieldNames.BillingPeriodStart)),
                BillingPeriodEnd = DateOf(Header(BillingFieldNames.BillingPeriodEnd)),
                Currency = Header(BillingFieldNames.Currency),
                HeaderConfidence = confidence,
                Lines = lines.Values.OrderBy(l => l.Index).Select(l => LineFacts(l, value, preVerified, before)).ToList(),
            };
        }

        /// <summary>A field the reviewer has seen counts as verified: any Corrected / Confirmed row, except that the corrected field before this correction counts only when an earlier row did.</summary>
        private bool IsVerified(Guid? lineId, string field, bool preVerified) =>
            _applied is { } applied && applied.LineId == lineId && applied.Field == field ? preVerified : _verified.Contains((lineId, field));

        private string? ColumnText(string name) => name switch
        {
            BillingFieldNames.FirmInvoiceNumber => _invoice.GetAttributeValue<string>(InvoiceReviewService.InvInvoiceNumber),
            BillingFieldNames.InvoiceDate => DateText(_invoice, InvoiceReviewService.InvInvoiceDate),
            BillingFieldNames.BillingPeriodStart => DateText(_invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodStart),
            BillingFieldNames.BillingPeriodEnd => DateText(_invoice, InvoiceSubmissionPersister.InvoiceBillingPeriodEnd),
            BillingFieldNames.Currency => _invoice.GetAttributeValue<string>(InvoiceSubmissionPersister.InvoiceCurrency),
            _ => null,
        };

        private BillingLineFacts LineFacts(StoredLine line, string? value, bool preVerified, bool before)
        {
            var row = line.Row;
            var isFee = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value == (int)InvoiceLineCostType.Fee;
            var target = _applied is { } applied && applied.LineId == row.Id;
            string? Field(string name, string? stored) => target && _applied!.Field == name ? value : stored;

            var coding = line.Extraction?["coding"] as JsonObject;
            var hours = isFee ? DecimalOf(Field(ResolutionFieldNames.Hours, Canonical(row.GetAttributeValue<decimal?>(InvoiceSubmissionPersister.LineQuantity)))) : null;
            var rate = isFee ? DecimalOf(Field(ResolutionFieldNames.Rate, Canonical(row.GetAttributeValue<Money>(InvoiceSubmissionPersister.LineRate)?.Value))) : null;
            var amount = DecimalOf(Field(ResolutionFieldNames.Amount, Canonical(row.GetAttributeValue<Money>(InvoiceTotalsService.LineAmount)?.Value)));

            var confidence = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (name, node) in line.Extraction?["fields"] as JsonObject ?? new JsonObject())
            {
                if (node is JsonObject field && field["combined"] is JsonValue combined && combined.TryGetValue<decimal>(out var c))
                {
                    confidence[name] = c;
                }
            }

            var observations = new List<InvoiceLineObservation>();
            if (coding?["confidence"] is JsonValue codingConfidence && codingConfidence.TryGetValue<decimal>(out var codingValue))
            {
                confidence[BillingFieldNames.TaskCode] = codingValue;
                confidence[BillingFieldNames.ActivityCode] = codingValue;
                confidence[BillingFieldNames.ExpenseCode] = codingValue;
            }

            foreach (var observation in (coding?["observations"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Enum.TryParse<InvoiceLineObservationType>(Text(observation, "type"), out var type)
                    && observation["confidence"] is JsonValue oc && oc.TryGetValue<decimal>(out var ov))
                {
                    // The quoted text is not stored; it is the same (empty) on both sides of the diff.
                    observations.Add(new InvoiceLineObservation { Type = type, Confidence = ov, QuotedText = string.Empty });
                    var key = BillingFieldNames.ObservationPrefix + type;
                    confidence[key] = confidence.TryGetValue(key, out var seen) ? Math.Max(seen, ov) : ov;
                }
            }

            foreach (var name in new[] { ResolutionFieldNames.Hours, ResolutionFieldNames.Rate, ResolutionFieldNames.Amount, BillingFieldNames.Narrative,
                BillingFieldNames.TaskCode, BillingFieldNames.ActivityCode, BillingFieldNames.ExpenseCode,
                ResolutionFieldNames.Date, ResolutionFieldNames.TimekeeperAsBilled })
            {
                if (IsVerified(row.Id, name, preVerified))
                {
                    confidence[name] = 1m;
                }
            }

            var timekeeperId = row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineResolvedTimekeeper)?.Id;
            var approvedRate = row.GetAttributeValue<Money>(InvoiceSubmissionPersister.LineApprovedRate)?.Value;
            var timekeeperReason = TimekeeperUnmatchedReason.None;
            var rateStatus = RateResolutionStatus.NotFound;
            var rateReason = RateNotFoundReason.None;
            if (line.Extraction?[InvoiceCorrectionReevaluation.ResolutionChangeMember] is JsonObject change)
            {
                var after = change["after"] as JsonObject;
                timekeeperReason = Enum.TryParse<TimekeeperUnmatchedReason>(after is null ? null : Text(after, "timekeeperReason"), out var tr) ? tr : timekeeperReason;
                rateStatus = Enum.TryParse<RateResolutionStatus>(after is null ? null : Text(after, "rateStatus"), out var rs) ? rs : rateStatus;
                rateReason = Enum.TryParse<RateNotFoundReason>(after is null ? null : Text(after, "rateReason"), out var rr) ? rr : rateReason;
            }

            if (before && target && _resolutionBefore is { } was)
            {
                // A timekeeper or date correction (fix round, part 3): the stored columns hold the pair the latest correction gave; the "before" side is
                // the pair the stored findings were last patched with (see ResolutionBefore), so a correction whose findings step never ran is diffed too.
                timekeeperId = was.TimekeeperId;
                approvedRate = was.ApprovedRate;
                (timekeeperReason, rateStatus, rateReason) = (was.TimekeeperReason, was.RateStatus, was.RateReason);
            }

            return new BillingLineFacts
            {
                Index = line.Index,
                LineType = isFee ? InvoiceLineType.Fee : InvoiceLineType.Expense,
                Date = DateOf(Field(ResolutionFieldNames.Date, DateText(row, InvoiceSubmissionPersister.LineEventDate))),
                TimekeeperName = Field(ResolutionFieldNames.TimekeeperAsBilled, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTimekeeper)),
                Timekeeper = !isFee ? null
                    : timekeeperId is { } tk && tk != Guid.Empty
                        ? new TimekeeperResolution { TimekeeperId = tk, Stage = TimekeeperMatchStage.Exact, MatchConfidence = 1m }
                        : new TimekeeperResolution { Reason = timekeeperReason },
                ApprovedRate = !isFee ? new RateResolution { Status = RateResolutionStatus.NotApplicable }
                    : approvedRate is { } approved ? new RateResolution { Status = RateResolutionStatus.Found, ApprovedRate = approved }
                    : new RateResolution { Status = rateStatus == RateResolutionStatus.Found ? RateResolutionStatus.NotFound : rateStatus, Reason = rateReason },
                Hours = hours,
                BilledRate = rate,
                Amount = amount,
                Narrative = Field(BillingFieldNames.Narrative, row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineDescription)),
                TaskCode = Field(BillingFieldNames.TaskCode, CodeOf(coding, "task")),
                ActivityCode = Field(BillingFieldNames.ActivityCode, CodeOf(coding, "activity")),
                ExpenseCode = Field(BillingFieldNames.ExpenseCode, CodeOf(coding, "expense")),
                BilledTaskCode = row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineTaskCodeAsBilled),
                BilledActivityCode = row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineActivityCodeAsBilled),
                BilledExpenseCode = row.GetAttributeValue<string>(InvoiceSubmissionPersister.LineExpenseCodeAsBilled),
                Observations = observations,
                FieldConfidence = confidence,
            };
        }

        /// <summary>The unmatched / not-found reasons of a line as the first evaluation stored them: the reason codes of its ApprovedTimekeeper and ApprovedRate findings.</summary>
        internal static (TimekeeperUnmatchedReason, RateResolutionStatus, RateNotFoundReason) StoredReasons(JsonObject root, int line)
        {
            var timekeeper = TimekeeperUnmatchedReason.None;
            var status = RateResolutionStatus.NotFound;
            var reason = RateNotFoundReason.None;
            foreach (var entry in (root["findings"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(e => Text(e, "subject") == nameof(BillingSubjectKind.Line) && IntOf(e["line"]) == line))
            {
                switch (Text(entry, "reasonCode"))
                {
                    case BillingReasonCodes.TimekeeperNameMissing: timekeeper = TimekeeperUnmatchedReason.NameMissing; break;
                    case BillingReasonCodes.TimekeeperLineDateMissing: timekeeper = TimekeeperUnmatchedReason.LineDateMissing; break;
                    case BillingReasonCodes.TimekeeperNameIncomplete: timekeeper = TimekeeperUnmatchedReason.NameIncomplete; break;
                    case BillingReasonCodes.TimekeeperNotOnRoster: timekeeper = TimekeeperUnmatchedReason.NotOnRoster; break;
                    case BillingReasonCodes.TimekeeperNotApprovedOnLineDate: timekeeper = TimekeeperUnmatchedReason.NotApprovedOnLineDate; break;
                    case BillingReasonCodes.TimekeeperAmbiguous: timekeeper = TimekeeperUnmatchedReason.Ambiguous; break;
                    case BillingReasonCodes.TimekeeperAiBelowThreshold: timekeeper = TimekeeperUnmatchedReason.AiBelowThreshold; break;
                    case BillingReasonCodes.TimekeeperAiNotClearlyAhead: timekeeper = TimekeeperUnmatchedReason.AiNotClearlyAhead; break;
                    case BillingReasonCodes.TimekeeperAiUnavailable: timekeeper = TimekeeperUnmatchedReason.AiUnavailable; break;
                    case BillingReasonCodes.RateAmbiguous: status = RateResolutionStatus.Ambiguous; break;
                    case BillingReasonCodes.RateTimekeeperUnmatched: reason = RateNotFoundReason.TimekeeperUnmatched; break;
                    case BillingReasonCodes.RateLineDateMissing: reason = RateNotFoundReason.LineDateMissing; break;
                    case BillingReasonCodes.RateNoRateInForce: reason = RateNotFoundReason.NoRateInForce; break;
                }
            }

            return (timekeeper, status, reason);
        }

        private static string? CodeOf(JsonObject? coding, string member) =>
            coding?[member] is JsonObject slot && Text(slot, "code") is { } code && !string.IsNullOrWhiteSpace(code) ? code.Trim() : null;

        private static string? Canonical(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static decimal? DecimalOf(string? text) =>
            decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static string? DateText(Entity row, string column) =>
            row.GetAttributeValue<DateTime?>(column) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    // ── JSON helpers ──

    internal static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static int? IntOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static DateOnly? DateOf(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
