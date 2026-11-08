// §11 justification (CLAUDE.md, ebilling-r1 task 082d):
//   (1) Existing — InvoiceSubmissionPersister.BuildFindingsJson builds every sprk_findingsjson member inline; InvoiceSubmissionAfterSteps.StoredFindings
//       and InvoiceReviewReadService.ParsedFindings read their members back. None stores or reads the Procedure basis (model 7.4).
//   (2) Extension — the builder could sit inline in BuildFindingsJson, but the same member names are read back by the Decision Record summary here,
//       and by 083 (applied versions) and 113 (the comparison) later: one place owns the shape, so the persister keeps one hunk (one member).
//   (3) Cost of doing nothing — 083 has no stored list of the policy versions that judged an invoice (it would have to re-resolve, which its POML
//       forbids), 113 has nothing to compare with, and the Decision Record carries no pointer to the basis.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 082d (model 7.4): the submission's snapshot of the Procedure basis, the <see cref="Member"/> member of <c>sprk_findingsjson</c>.
/// </summary>
/// <remarks>
/// <para><b>What it stores.</b> Built from <see cref="BillingEvaluationResult.ProcedureBasis"/>, the objects the evaluator judged with (never
/// re-resolved): the core-record chain; every override row that decided anything (mode, chain record, distance, Procedure, pinned version, rule,
/// firm, reason); per group of work dates with the same outcome, the Procedures applied (Procedure Version, level, distance, firm-specific, sources,
/// pinned, deciding rows), the rules in force (policy version, Procedure, Procedure Version, record-rule slot), the rules and Procedures excluded and
/// the gaps; and per selected rule version, how precedence treated it (applied, displaced, cap displaced) by subject. Ids, enums, numbers and the
/// reasons only: <b>no name</b>. A name (Procedure, record, rule) is resolved at read time from the id, behind the reader's record check, and never
/// frozen here (uac-r2, 082d snapshot design).</para>
/// <para><b>Internal part (uac-r2, binding; 082d resolves the U3 tension of the 082 notes).</b> Chain ids, Procedure Version ids, override row ids
/// and override reasons are in this member ONLY. <c>sprk_findingsjson</c> is field-secured with no reader profile and read app-only
/// (<c>FindingsJsonFieldSecurityAgreementTests</c>, <c>FindingsJsonAppOnlyReadGuardTests</c>), and no reader projection emits this member: the review
/// read model, the status view, the admin attention text and the hold readers read named members field by field and never this one. A reader-facing
/// member about Procedures is built from <see cref="ProcedureRulesOnDate.ForReaders"/> / <see cref="ProcedureGap.ForReaders"/> only (the gap
/// pipeline finding). Tests: <c>ProcedureSnapshotTests</c>.</para>
/// <para><b>Versions.</b> <see cref="Version"/> is 1. A row stored before 082d has no member: <see cref="Read(JsonObject)"/> answers null, which
/// means "no snapshot" (fail closed: 083 shows no clause, 113 shows "Not yet checked"). So does an unknown version or a damaged member.</para>
/// </remarks>
internal static class ProcedureSnapshot
{
    /// <summary>The <c>sprk_findingsjson</c> member.</summary>
    internal const string Member = "procedureSnapshot";

    /// <summary>The snapshot's own version (the findings JSON's <c>schema</c> stays 1: the member is additive).</summary>
    internal const int Version = 1;

    /// <summary>The Decision Record fact that carries <see cref="DecisionRecordSummary"/>.</summary>
    internal const string DecisionRecordFact = "procedureSnapshot";

    /// <summary>The snapshot of <paramref name="evaluation"/>'s basis, or null when nothing was evaluated (a held fault, a pre-evaluation stop).</summary>
    internal static JsonObject? Build(BillingEvaluationResult? evaluation)
    {
        if (evaluation?.ProcedureBasis is not { } basis)
        {
            return null;
        }

        var rows = new Dictionary<Guid, ProcedureOverrideRow>();
        void Collect(IEnumerable<ProcedureOverrideRow> deciding)
        {
            foreach (var row in deciding)
            {
                rows.TryAdd(row.RowId, row);
            }
        }

        // Dates with the same outcome share one entry (a version boundary is what changes it), so 150 dated lines do not repeat the basis.
        var groups = new List<(List<string> Dates, JsonObject Content, string Key)>();
        foreach (var onDate in basis.Dates)
        {
            var content = DateContent(onDate, Collect);
            var key = content.ToJsonString();
            var date = onDate.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (groups.FirstOrDefault(g => g.Key == key) is { Dates: not null } group)
            {
                group.Dates.Add(date);
            }
            else
            {
                groups.Add(([date], content, key));
            }
        }

        var resolutions = new JsonArray();
        foreach (var (dates, content, _) in groups)
        {
            var entry = new JsonObject { ["dates"] = new JsonArray(dates.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()) };
            foreach (var (name, value) in content.ToList())
            {
                content.Remove(name);
                entry[name] = value;
            }

            resolutions.Add(entry);
        }

        return new JsonObject
        {
            ["version"] = Version,
            ["chain"] = new JsonArray(basis.Chain.Select(c => (JsonNode)Link(c)).ToArray()),
            ["overrides"] = new JsonArray(rows.Values.OrderBy(r => r.RowId).Select(r => (JsonNode)new JsonObject
            {
                ["rowId"] = Id(r.RowId),
                ["mode"] = r.Mode.ToString(),
                ["record"] = Link(r.ChainRecord),
                ["procedureId"] = Id(r.ProcedureId),
                ["pinnedVersionId"] = Id(r.PinnedVersionId),
                ["policyId"] = Id(r.PolicyId),
                ["firmId"] = Id(r.FirmId),
                ["reason"] = r.Reason,
            }).ToArray()),
            ["resolutions"] = resolutions,
            ["applications"] = new JsonArray(basis.Applications.Select(a =>
            {
                var entry = new JsonObject
                {
                    ["policyVersionId"] = Id(a.PolicyVersionId),
                    ["source"] = a.Source.ToString(),
                    ["applied"] = Subjects(a.Applied),
                };
                if (a.Displaced.Count > 0)
                {
                    entry["displaced"] = Subjects(a.Displaced);
                }

                if (a.CapDisplaced.Count > 0)
                {
                    entry["capDisplaced"] = Subjects(a.CapDisplaced);
                }

                return (JsonNode)entry;
            }).ToArray()),
        };
    }

    /// <summary>
    /// 082d fix round F4: the snapshot a PATCHED findings JSON (a correction, an added line) should store: the re-run's own basis
    /// (<paramref name="after"/>), because the patch rewrote the findings the stored basis judged. A whole-invoice snapshot describes the stored
    /// findings only while every decided line's entries are the ones the re-run would produce; the patch leaves a decided line's old entries as stored,
    /// so when the findings of a decided line, or the rules applied to / displaced on it, differ between <paramref name="before"/> and
    /// <paramref name="after"/>, the stored findings mix two bases and the answer is null ("no snapshot": 083 and 113 fail closed, never a wrong
    /// basis). Also null when <paramref name="after"/> carries no basis.
    /// </summary>
    internal static JsonObject? ForPatchedFindings(BillingEvaluationResult before, BillingEvaluationResult after, IReadOnlySet<int> decided) =>
        DecidedLinesDiffer(before, after, decided) ? null : Build(after);

    private static bool DecidedLinesDiffer(BillingEvaluationResult before, BillingEvaluationResult after, IReadOnlySet<int> decided)
    {
        if (decided.Count == 0)
        {
            return false;
        }

        static List<string> Findings(BillingEvaluationResult result, IReadOnlySet<int> decided) => result.Findings
            .Where(f => f.Subject.LineIndex is { } line && decided.Contains(line))
            .Select(f => InvoiceSubmissionPersister.FindingEntryJson(0, f, null, scrub: false).ToJsonString())
            .Order(StringComparer.Ordinal).ToList();

        static List<string> Applications(BillingEvaluationResult result, IReadOnlySet<int> decided)
        {
            static string Lines(IReadOnlyList<BillingSubject> subjects, IReadOnlySet<int> decided) =>
                string.Join(',', subjects.Where(s => s.LineIndex is { } line && decided.Contains(line)).Select(s => s.LineIndex!.Value).Order());

            return (result.ProcedureBasis?.Applications ?? [])
                .Select(a => $"{a.PolicyVersionId:D}|{a.Source}|{Lines(a.Applied, decided)}|{Lines(a.Displaced, decided)}|{Lines(a.CapDisplaced, decided)}")
                .Where(text => text.Split('|')[2..].Any(part => part.Length > 0))
                .Order(StringComparer.Ordinal).ToList();
        }

        return !Findings(before, decided).SequenceEqual(Findings(after, decided), StringComparer.Ordinal)
            || !Applications(before, decided).SequenceEqual(Applications(after, decided), StringComparer.Ordinal);
    }

    private static JsonObject DateContent(ProcedureRulesOnDate onDate, Action<IEnumerable<ProcedureOverrideRow>> collect)
    {
        JsonArray RowIds(IReadOnlyList<ProcedureOverrideRow> deciding)
        {
            collect(deciding);
            return new JsonArray(deciding.Select(r => r.RowId).Distinct().Order().Select(r => (JsonNode)JsonValue.Create(Id(r))!).ToArray());
        }

        return new JsonObject
        {
            ["procedures"] = new JsonArray(onDate.Procedures.Select(p => (JsonNode)new JsonObject
            {
                ["procedureId"] = Id(p.ProcedureId),
                ["procedureVersionId"] = Id(p.ProcedureVersionId),
                ["level"] = p.Rank.Level.ToString(),
                ["distance"] = p.Rank.Distance,
                ["firmSpecific"] = p.Rank.FirmSpecific,
                ["sources"] = p.Sources.ToString(),
                ["pinned"] = p.Pinned,
                ["overrides"] = RowIds(p.Overrides),
            }).ToArray()),
            ["rules"] = new JsonArray(onDate.Rules.Select(r => (JsonNode)new JsonObject
            {
                ["policyId"] = Id(r.Version.PolicyId),
                ["policyVersionId"] = Id(r.Version.PolicyVersionId),
                ["procedureId"] = Id(r.Procedure.ProcedureId),
                ["procedureVersionId"] = Id(r.Procedure.ProcedureVersionId),
                ["recordRule"] = r.RecordRule is not { } slot
                    ? null
                    : new JsonObject
                    {
                        ["basePolicyId"] = Id(slot.BasePolicyId),
                        ["basePolicyVersionId"] = Id(slot.BasePolicyVersionId),
                        ["record"] = Link(slot.ChainRecord),
                        ["excludeRowId"] = slot.Exclude is { } exclude ? RowIdOf(exclude, collect) : null,
                    },
            }).ToArray()),
            ["excludedRules"] = new JsonArray(onDate.ExcludedRules.Select(e => (JsonNode)new JsonObject
            {
                ["policyId"] = Id(e.Version.PolicyId),
                ["policyVersionId"] = Id(e.Version.PolicyVersionId),
                ["procedureId"] = Id(e.Procedure.ProcedureId),
                ["procedureVersionId"] = Id(e.Procedure.ProcedureVersionId),
                ["distance"] = e.Distance,
                ["replacedByRecordRule"] = e.ReplacedByRecordRule,
                ["overrides"] = RowIds(e.Overrides),
            }).ToArray()),
            ["excludedProcedures"] = new JsonArray(onDate.ExcludedProcedures.Select(e => (JsonNode)new JsonObject
            {
                ["procedureId"] = Id(e.ProcedureId),
                ["overrides"] = RowIds(e.Overrides),
            }).ToArray()),
            ["gaps"] = new JsonArray(onDate.Gaps.Select(g => (JsonNode)new JsonObject
            {
                ["procedureId"] = Id(g.ProcedureId),
                ["kind"] = g.Kind.ToString(),
                ["overrides"] = RowIds(g.Overrides),
            }).ToArray()),
        };
    }

    private static string RowIdOf(ProcedureOverrideRow row, Action<IEnumerable<ProcedureOverrideRow>> collect)
    {
        collect([row]);
        return Id(row.RowId);
    }

    private static JsonObject Link(CoreRecordLink link) => new() { ["table"] = link.Table, ["id"] = Id(link.Id), ["distance"] = link.Distance };

    private static JsonObject Subjects(IReadOnlyList<BillingSubject> subjects) => new()
    {
        ["invoice"] = subjects.Any(s => s.Kind == BillingSubjectKind.Invoice),
        ["lines"] = new JsonArray(subjects.Where(s => s.LineIndex is not null).Select(s => (JsonNode)JsonValue.Create(s.LineIndex!.Value)!).ToArray()),
    };

    private static string Id(Guid id) => id.ToString("D");

    private static string? Id(Guid? id) => id?.ToString("D");

    // ── Reading it back ──

    /// <summary>
    /// The stored snapshot of <paramref name="root"/> (the parsed <c>sprk_findingsjson</c>), or null: no member (a row stored before 082d), an
    /// unknown <c>version</c>, or a damaged member. Null always means "no snapshot", never "nothing applied". Never throws.
    /// </summary>
    internal static StoredProcedureSnapshot? Read(JsonObject root)
    {
        try
        {
            if (root[Member] is not JsonObject member
                || member["version"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != Version
                || member["applications"] is not JsonArray applications || member["resolutions"] is not JsonArray resolutions)
            {
                return null;
            }

            var applied = new List<Guid>();
            var displaced = new List<Guid>();
            foreach (var application in applications)
            {
                if (application is not JsonObject entry || !Guid.TryParse(Text(entry, "policyVersionId"), out var versionId))
                {
                    return null;
                }

                if (HasSubject(entry["applied"]))
                {
                    applied.Add(versionId);
                }
                else if (HasSubject(entry["displaced"]))
                {
                    displaced.Add(versionId);
                }
            }

            var procedures = new HashSet<string>(StringComparer.Ordinal);
            var gaps = 0;
            foreach (var resolution in resolutions)
            {
                if (resolution is not JsonObject entry)
                {
                    return null;
                }

                procedures.UnionWith(Ids(entry["procedures"], "procedureId"));
                gaps += (entry["gaps"] as JsonArray)?.Count ?? 0;
            }

            return new StoredProcedureSnapshot(number, applied, displaced, procedures.Count, gaps);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary><see cref="Read(JsonObject)"/> over the stored text; null for a missing or malformed JSON as well.</summary>
    internal static StoredProcedureSnapshot? Read(string? findingsJson)
    {
        if (string.IsNullOrWhiteSpace(findingsJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(findingsJson) is JsonObject root ? Read(root) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Model 7.4 (ontology O2): the bounded summary a Decision Record written by evaluation carries instead of the basis (<c>sprk_factsnapshot</c>
    /// is capped at 100,000 characters): the pointer to the submission's member and a fixed set of counts. No id but the submission's (already a
    /// fact of the record), no name, no reason. <b>No count of excluded rules or excluded Procedures (082d fix round K1):</b> an Exclude row sits on
    /// a core record of the chain, possibly a Secure parent, and a Decision Record reader who cannot read that parent must learn nothing about the
    /// chain from the record. The counts that stay describe the outcome (Procedures, rules applied, rules displaced by precedence) and the gaps (a
    /// gap is also a reader-facing finding via <see cref="ProcedureGap.ForReaders"/>), never an override or Exclude row.
    /// </summary>
    internal static Dictionary<string, object?> DecisionRecordSummary(StoredProcedureSnapshot snapshot, Guid submissionId) => new(StringComparer.Ordinal)
    {
        ["version"] = snapshot.Version,
        ["submissionId"] = submissionId.ToString("D"),
        ["member"] = Member,
        ["procedures"] = snapshot.ProcedureCount,
        ["appliedRules"] = snapshot.AppliedPolicyVersionIds.Count,
        ["displacedRules"] = snapshot.DisplacedPolicyVersionIds.Count,
        ["gaps"] = snapshot.GapCount,
    };

    private static bool HasSubject(JsonNode? subjects) =>
        subjects is JsonObject s && (s["invoice"]?.GetValue<bool>() == true || (s["lines"] as JsonArray)?.Count > 0);

    private static IEnumerable<string> Ids(JsonNode? list, string member) =>
        (list as JsonArray ?? []).OfType<JsonObject>().Select(e => Text(e, member)).OfType<string>();

    private static string? Text(JsonObject entry, string member) =>
        entry[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>
/// The parts of a stored <see cref="ProcedureSnapshot"/> its readers need today. <see cref="AppliedPolicyVersionIds"/>: the versions that judged at
/// least one subject (task 083: a clause is shown only for these); <see cref="DisplacedPolicyVersionIds"/>: those applied on no subject and displaced
/// on at least one. The counts feed the Decision Record summary (never a count of Exclude rows: see <see cref="ProcedureSnapshot.DecisionRecordSummary"/>).
/// Internal: it carries policy version ids.
/// </summary>
internal sealed record StoredProcedureSnapshot(
    int Version, IReadOnlyList<Guid> AppliedPolicyVersionIds, IReadOnlyList<Guid> DisplacedPolicyVersionIds, int ProcedureCount, int GapCount)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Version = ").Append(Version).Append(", Applied = ").Append(AppliedPolicyVersionIds.Count)
            .Append(", Displaced = ").Append(DisplacedPolicyVersionIds.Count).Append(", Procedures = ").Append(ProcedureCount)
            .Append(", Gaps = ").Append(GapCount);
        return true;
    }
}
