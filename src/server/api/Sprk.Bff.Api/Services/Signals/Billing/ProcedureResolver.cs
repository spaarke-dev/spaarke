// ebilling-r1 task 082b (notes/procedure-and-file-model.md 6.1-6.5, 7.1, 7.2; ontology D-172, D-175, D-177; notes/082-procedure-resolver.md
// "082a review and decisions"): which Procedures, Procedure Versions and rules judge an invoice, for each work date.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — BillingFactLoader.LoadRulesAsync selects legacy rule sets by ClientDefault / Firm and skips every Procedure set; the
//       assignment service (089) writes the override rows but reads them only to refuse duplicates; nothing resolves a Procedure.
//   (2) Extension — the loader's set-level read cannot express Applies to, Procedure Versions, pins, excludes or record rules, and the
//       same answer is needed by evaluation (082c), the banner (8.2), 083, 084, 085 and 113. One resolver, reused, never a second filter.
//   (3) Cost of doing nothing — no Procedure's rules ever judge an invoice, overrides have no effect, and a gap passes an invoice with
//       no rules instead of holding it (O-22).
//
// Placement (bff-extensions.md): BFF, Services/Signals/Billing, beside the loader that will call it. A static reader over the app-only
// IGenericEntityService, like CoreRecordChain: no DI registration, no endpoint, no package.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>The level of a Procedure's source (model 6.5): Manual d0 &gt; Manual d1 &gt; Firm &gt; All records.</summary>
public enum ProcedureLevel
{
    AllRecords = 0,
    Firm = 1,
    ManualParent = 2,
    ManualOwn = 3,
}

/// <summary>Where a Procedure comes from (model 6.1). One Procedure can have several; its level is the highest.</summary>
[Flags]
public enum ProcedureSources
{
    None = 0,
    AllRecords = 1,
    Firm = 2,
    Manual = 4,
}

/// <summary>
/// The rank key (model 6.5, 7.3): (Level, −Distance, FirmSpecific), compared in that order. Distance is the chain distance of the
/// assignment that gave a Manual level (0 or 1) and 0 for a computed source. FirmSpecific: the Procedure names a firm. Ties at the same
/// rank all apply (7.3); the resolver never breaks them.
/// </summary>
public readonly record struct ProcedureRank(ProcedureLevel Level, int Distance, bool FirmSpecific) : IComparable<ProcedureRank>
{
    public int CompareTo(ProcedureRank other)
    {
        var byLevel = Level.CompareTo(other.Level);
        if (byLevel != 0)
        {
            return byLevel;
        }

        // −Distance: the nearer record ranks higher.
        var byDistance = other.Distance.CompareTo(Distance);
        return byDistance != 0 ? byDistance : FirmSpecific.CompareTo(other.FirmSpecific);
    }
}

/// <summary>The mode of an override row (<c>sprk_procedureassignment.sprk_mode</c>). A rule-level row is always <see cref="Excluded"/>.</summary>
public enum ProcedureOverrideMode
{
    FollowLatest,
    Pinned,
    Excluded,
}

/// <summary>
/// One override row on the chain as read (model 6.2, 6.3): a Procedure-level row (<see cref="ProcedureId"/>) or a rule-level Exclude
/// (<see cref="PolicyId"/>). <see cref="ChainRecord"/> is the chain record it sits on.
/// </summary>
/// <remarks><b>Internal basis (uac-r2 U3).</b> <see cref="ChainRecord"/> and <see cref="Reason"/> may name or describe a Secure parent. They
/// belong in the evaluation's internal snapshot (7.4) only; a reader-facing output uses <see cref="ProcedureRulesOnDate.ForReaders"/>.
/// <see cref="object.ToString"/> prints the mode and distance only (K2): no record id, row id, Procedure id or reason.
/// <see cref="CreatedOn"/> is the row's <c>createdon</c>: the start of a change's current episode (see the record rules, 6.3).</remarks>
public sealed record ProcedureOverrideRow(
    Guid RowId, ProcedureOverrideMode Mode, CoreRecordLink ChainRecord, Guid? ProcedureId, Guid? PinnedVersionId, Guid? PolicyId, Guid? FirmId,
    string? Reason, DateTime? CreatedOn = null)
{
    public int Distance => ChainRecord.Distance;

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Mode = ").Append(Mode).Append(", Distance = ").Append(Distance).Append(", Subject = ").Append(ProcedureId is not null ? "Procedure" : "Rule");
        return true;
    }
}

/// <summary>One Published policy version as read: what the gate, the handlers and the Signal mapping need (082c maps it to a candidate).
/// <see cref="object.ToString"/> prints the code, version number, rule type and from-date only (082c C17): no id, rule body or text.</summary>
public sealed record ResolvedPolicyVersion
{
    public required Guid PolicyId { get; init; }

    public required string PolicyCode { get; init; }

    public required Guid PolicyVersionId { get; init; }

    public required Guid RuleSetId { get; init; }

    public int VersionNumber { get; init; }

    /// <summary><c>sprk_ruletype</c> as the gate reads it: the option value in invariant text.</summary>
    public string? RuleType { get; init; }

    public string? RuleBody { get; init; }

    public string? MessageTemplate { get; init; }

    public string? ShortHeadline { get; init; }

    public string? ClauseReference { get; init; }

    public string? ClauseLocation { get; init; }

    /// <summary><c>sprk_inforcefrom</c> as a UTC date: the version's own effective-from.</summary>
    public DateOnly? InForceFrom { get; init; }

    public int? Lane { get; init; }

    public int? Severity { get; init; }

    public int? Consequence { get; init; }

    /// <summary><c>sprk_procedureversion</c>: the Procedure Version that published it (null on a record rule's version).</summary>
    public Guid? ProcedureVersionId { get; init; }

    /// <summary><c>sprk_basedonversion</c>: on a record rule's version, the base rule's version it was copied from.</summary>
    public Guid? BasedOnVersionId { get; init; }

    /// <summary><c>createdon</c> (UTC): which episode of a change a record rule's version belongs to. Internal: it is not part of the snapshot.</summary>
    internal DateTime? CreatedOn { get; init; }

    /// <summary>
    /// The code as <see cref="object.ToString"/> prints it. A record rule's code is <see cref="ProcedureAssignmentService.RecordRuleCodePrefix"/> plus
    /// its policy id (<see cref="ProcedureAssignmentService.RecordRuleCode"/>), so it prints the token <c>record rule</c> instead (082c C17, K2).
    /// </summary>
    internal static string Printable(string policyCode) =>
        policyCode.StartsWith(ProcedureAssignmentService.RecordRuleCodePrefix, StringComparison.Ordinal) ? "record rule" : policyCode;

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("PolicyCode = ").Append(Printable(PolicyCode)).Append(", VersionNumber = ").Append(VersionNumber)
            .Append(", RuleType = ").Append(RuleType).Append(", InForceFrom = ").Append(InForceFrom);
        return true;
    }
}

/// <summary>
/// A Procedure in force for one work date: the version chosen, its rank and sources, and the rows that decided it.
/// <see cref="object.ToString"/> prints names, numbers and the rank only (K2): no Procedure id, version id or deciding row.
/// </summary>
public sealed record AppliedProcedure(
    Guid ProcedureId, string ProcedureName, Guid ProcedureVersionId, int ProcedureVersionNumber, ProcedureRank Rank, ProcedureSources Sources,
    bool Pinned, IReadOnlyList<ProcedureOverrideRow> Overrides)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("ProcedureName = ").Append(ProcedureName).Append(", ProcedureVersionNumber = ").Append(ProcedureVersionNumber)
            .Append(", Rank = ").Append(Rank).Append(", Sources = ").Append(Sources).Append(", Pinned = ").Append(Pinned)
            .Append(", Overrides = ").Append(Overrides.Count);
        return true;
    }
}

/// <summary>
/// A record rule in its base rule's slot (model 6.3): the base it replaces, the change's Exclude row, the record it sits on.
/// <see cref="object.ToString"/> prints the base rule's code and the distance only (K2): no record id, policy id or Exclude row.
/// </summary>
public sealed record RecordRuleSlot(
    Guid BasePolicyId, string BasePolicyCode, Guid BasePolicyVersionId, CoreRecordLink ChainRecord, ProcedureOverrideRow? Exclude)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("BasePolicyCode = ").Append(BasePolicyCode).Append(", Distance = ").Append(ChainRecord.Distance)
            .Append(", HasExclude = ").Append(Exclude is not null);
        return true;
    }
}

/// <summary>
/// A rule in force for one work date. <see cref="Procedure"/> is the Procedure whose slot it takes (a record rule's is its base rule's), so
/// its rank is that Procedure's. <see cref="RecordRule"/> is set on a record rule. <see cref="object.ToString"/> prints the code, the version
/// numbers, the Procedure name, the rank and the record-rule flag only (082c C17): no id, rule body or override reason.
/// </summary>
public sealed record ResolvedRule(ResolvedPolicyVersion Version, AppliedProcedure Procedure, RecordRuleSlot? RecordRule)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("PolicyCode = ").Append(ResolvedPolicyVersion.Printable(Version.PolicyCode)).Append(", VersionNumber = ").Append(Version.VersionNumber)
            .Append(", ProcedureName = ").Append(Procedure.ProcedureName).Append(", ProcedureVersionNumber = ").Append(Procedure.ProcedureVersionNumber)
            .Append(", Rank = ").Append(Procedure.Rank).Append(", RecordRule = ").Append(RecordRule is not null);
        return true;
    }
}

/// <summary>
/// A rule of an applied Procedure that a rule-level override removed for this date: the rows that decided it, the distance of the deciding
/// override, and whether a record rule took its slot. <see cref="object.ToString"/> prints the code, the Procedure name, the distance, the
/// replaced flag and the row count only (082c C17): no id, rule body or override reason.
/// </summary>
public sealed record ExcludedRule(
    ResolvedPolicyVersion Version, AppliedProcedure Procedure, IReadOnlyList<ProcedureOverrideRow> Overrides, int Distance, bool ReplacedByRecordRule)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("PolicyCode = ").Append(ResolvedPolicyVersion.Printable(Version.PolicyCode)).Append(", ProcedureName = ").Append(Procedure.ProcedureName)
            .Append(", Distance = ").Append(Distance).Append(", ReplacedByRecordRule = ").Append(ReplacedByRecordRule)
            .Append(", Overrides = ").Append(Overrides.Count);
        return true;
    }
}

/// <summary>A Procedure that would apply but a Procedure-level Exclude removed (nearer row wins; between two d1 parents Exclude wins).
/// <see cref="object.ToString"/> prints the name and the row count only (082c C17): no Procedure id and no deciding row.</summary>
public sealed record ExcludedProcedure(Guid ProcedureId, string ProcedureName, IReadOnlyList<ProcedureOverrideRow> Overrides)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("ProcedureName = ").Append(ProcedureName).Append(", Overrides = ").Append(Overrides.Count);
        return true;
    }
}

/// <summary>Why an applicable Procedure has no version for a work date (model 7.1 gap reading, owner F2-d; D-177 (3)).</summary>
public enum ProcedureGapKind
{
    /// <summary>The date falls between two versions in force (a hole, or a withdrawn version's window).</summary>
    HoleBetweenVersions,

    /// <summary>The date is after the last version's end, which has no successor.</summary>
    AfterLastVersion,

    /// <summary>The pin names a Retired version.</summary>
    PinnedVersionRetired,

    /// <summary>The pin names a version that is not in force (a Draft, another Procedure's version, or one that no longer exists).</summary>
    PinnedVersionNotInForce,

    /// <summary>Two rows at the same (nearest) distance choose different versions for the date: two different pins (D-177 (3)).</summary>
    ConflictingParents,
}

/// <summary>
/// An applicable Procedure with no version for a work date. 082c raises the one blocking BILL-SYSTEM-SUBMISSION finding from it.
/// <see cref="object.ToString"/> prints the name, date, kind and row count only (K2): no Procedure id and no deciding row.
/// </summary>
public sealed record ProcedureGap(
    Guid ProcedureId, string ProcedureName, DateOnly WorkDate, ProcedureGapKind Kind, IReadOnlyList<ProcedureOverrideRow> Overrides)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("ProcedureName = ").Append(ProcedureName).Append(", WorkDate = ").Append(WorkDate).Append(", Kind = ").Append(Kind)
            .Append(", Overrides = ").Append(Overrides.Count);
        return true;
    }
}

/// <summary>What judges one work date. Every list is in a stable order: rules by (policy code, policy id), the rest by Procedure.</summary>
public sealed record ProcedureRulesOnDate(
    DateOnly WorkDate,
    IReadOnlyList<AppliedProcedure> Procedures,
    IReadOnlyList<ResolvedRule> Rules,
    IReadOnlyList<ExcludedRule> ExcludedRules,
    IReadOnlyList<ExcludedProcedure> ExcludedProcedures,
    IReadOnlyList<ProcedureGap> Gaps)
{
    /// <summary>
    /// The reader-facing projection (uac-r2 U3): built field by field from an allow-list, it carries Procedure names, version numbers,
    /// rule codes, levels and distances only. No record id, record name, override reason or row id: the chain may include a Secure parent
    /// that the reader of the invoice cannot read, and an override reason is free text typed on that parent.
    /// </summary>
    // uac-r2 K1 (082b review): Level and Distance reveal that an assignment exists at that level. Acceptable only while Procedures are catalogue-level;
    // if Procedures are ever created per record, or named from the record, this becomes an F finding and the fields must go.
    public ProcedureResolutionReaderView ForReaders() => new(
        WorkDate,
        Rules.Select(r => new ProcedureReaderRule(
            r.Version.PolicyCode, r.Procedure.ProcedureName, r.Procedure.ProcedureVersionNumber, r.Procedure.Rank.Level.ToString(),
            r.Procedure.Rank.Distance, r.Procedure.Pinned, r.RecordRule is not null)).ToList(),
        ExcludedRules.Select(e => new ProcedureReaderExcludedRule(
            e.Version.PolicyCode, e.Procedure.ProcedureName, e.Distance, e.ReplacedByRecordRule)).ToList(),
        ExcludedProcedures.Select(e => new ProcedureReaderExcludedProcedure(e.ProcedureName, e.Overrides.Min(o => o.Distance))).ToList(),
        Gaps.Select(g => new ProcedureReaderGap(g.ProcedureName, g.WorkDate, g.Kind.ToString())).ToList());
}

/// <summary>The reader-facing projection of <see cref="ProcedureRulesOnDate"/> (uac-r2 U3). Names no record.</summary>
public sealed record ProcedureResolutionReaderView(
    DateOnly WorkDate,
    IReadOnlyList<ProcedureReaderRule> Rules,
    IReadOnlyList<ProcedureReaderExcludedRule> ExcludedRules,
    IReadOnlyList<ProcedureReaderExcludedProcedure> ExcludedProcedures,
    IReadOnlyList<ProcedureReaderGap> Gaps);

public sealed record ProcedureReaderRule(
    string PolicyCode, string ProcedureName, int ProcedureVersionNumber, string Level, int Distance, bool Pinned, bool RecordRule);

public sealed record ProcedureReaderExcludedRule(string PolicyCode, string ProcedureName, int Distance, bool ReplacedByRecordRule);

public sealed record ProcedureReaderExcludedProcedure(string ProcedureName, int Distance);

public sealed record ProcedureReaderGap(string ProcedureName, DateOnly WorkDate, string Kind);

/// <summary>
/// The Procedure model's rows could not be resolved: a row the BFF never writes (an unknown mode, a row naming both or neither of a
/// Procedure and a rule, a record rule that does not name a version of its base rule, an enabled record rule with no Exclude row or none
/// of whose versions is in the current episode), or two versions in force on one date (I-P1). The evaluation faults and the job retries;
/// nothing is judged on a partial set.
/// </summary>
/// <remarks>
/// <b>For operators only (K2).</b> The message names row, Procedure and rule ids so an operator can find the row. <c>ex.Message</c> must
/// NEVER be shown to a reader, and the SPA warning at submit time (owner decision 2026-11-04) in particular: it names ids of records the
/// reader may not be able to read. A reader-facing text is built from a reason code and
/// <see cref="ProcedureRulesOnDate.ForReaders"/>.
/// </remarks>
public sealed class ProcedureResolutionFaultException(string message) : InvalidOperationException(message);

/// <summary>
/// Everything one invoice's Procedure resolution needs, read once (<see cref="ProcedureResolver.ReadAsync"/>). <see cref="RulesOn"/>
/// resolves a work date in memory, so the reads do not depend on the number of lines (model 8.2 cost bound).
/// </summary>
public sealed class ProcedureResolution
{
    private readonly ProcedureRows _rows;
    private readonly IReadOnlyList<Candidate> _candidates;
    private readonly IReadOnlyList<ExcludedProcedure> _excludedProcedures;
    private readonly ILookup<Guid, ProcedureVersionRow> _versionsByProcedure;
    private readonly ILookup<Guid, PolicyRow> _policiesBySet;
    private readonly ILookup<Guid, ResolvedPolicyVersion> _versionsByPolicy;
    private readonly IReadOnlyDictionary<Guid, ResolvedPolicyVersion> _versionsById;
    private readonly IReadOnlySet<Guid> _versionsWithPublishedRules;

    internal ProcedureResolution(ProcedureRows rows)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _versionsByProcedure = rows.Versions.ToLookup(v => v.ProcedureId);
        _policiesBySet = rows.Policies.ToLookup(p => p.RuleSetId);
        _versionsByPolicy = rows.PolicyVersions.ToLookup(v => v.PolicyId);
        _versionsById = rows.PolicyVersions.ToDictionary(v => v.PolicyVersionId);
        // Every policy version in the rows is Published (the read loads nothing else), so a Procedure Version named here has a Published
        // policy version under it: the same test as ProcedureService.ScopeLockedAsync, made over the rows already read (no new read).
        _versionsWithPublishedRules = rows.PolicyVersions.Where(v => v.ProcedureVersionId is not null).Select(v => v.ProcedureVersionId!.Value).ToHashSet();
        (_candidates, _excludedProcedures) = Applicability(rows);
    }

    /// <summary>The invoice's core-record chain (distance 0, then its direct parents); empty when the invoice has no core record.</summary>
    public IReadOnlyList<CoreRecordLink> Chain => _rows.Chain;

    /// <summary>
    /// ebilling-r1 082c (C4, C10): every Published policy version this resolution read, whatever date or Procedure Version it belongs to. A
    /// superset of the rules any <see cref="RulesOn"/> returns, for the loader's Duplicate gate (is a prior read worth it) and its check that
    /// the legacy and Procedure streams never share a version. Never a selection of what judges a date.
    /// </summary>
    internal IReadOnlyList<ResolvedPolicyVersion> PolicyVersions => _rows.PolicyVersions;

    private int _datesResolved;

    /// <summary>ebilling-r1 082c (C9): how many times <see cref="RulesOn"/> ran on this resolution (the evaluator resolves each distinct
    /// subject date once). Read by the tests that pin that bound; it decides nothing.</summary>
    internal int DatesResolved => Volatile.Read(ref _datesResolved);

    /// <summary>The Procedures, rules, exclusions and gaps for <paramref name="workDate"/>. Pure; reads nothing.</summary>
    /// <exception cref="ProcedureResolutionFaultException">Two versions of one Procedure are in force on the date (I-P1), a record rule
    /// does not name a version of its base rule, an enabled record rule has no Exclude row, or its known episode has no version. The message
    /// is for operators: never show <c>ex.Message</c> to a reader.</exception>
    public ProcedureRulesOnDate RulesOn(DateOnly workDate)
    {
        Interlocked.Increment(ref _datesResolved);
        var applied = new List<AppliedProcedure>();
        var gaps = new List<ProcedureGap>();
        var baseRules = new List<(ResolvedPolicyVersion Version, AppliedProcedure Procedure)>();
        foreach (var candidate in _candidates)
        {
            switch (ChooseVersion(candidate, workDate))
            {
                case VersionChoice.Chosen chosen:
                    var procedure = new AppliedProcedure(
                        candidate.Procedure.Id, candidate.Procedure.Name, chosen.Version.Id, chosen.Version.Number, candidate.Rank,
                        candidate.Sources, chosen.Pinned, candidate.Decisive);
                    applied.Add(procedure);
                    baseRules.AddRange(RulesOf(candidate.Procedure.Id, chosen.Version.Id, chosen.Pinned, workDate).Select(v => (v, procedure)));
                    break;
                case VersionChoice.Gap gap:
                    gaps.Add(new ProcedureGap(candidate.Procedure.Id, candidate.Procedure.Name, workDate, gap.Kind, candidate.Decisive));
                    break;
                default:
                    break; // not applicable on this date (before its first version): no gap
            }
        }

        // Rule-level overrides: per base rule, the rows at the nearest distance decide (6.5; a rule-level override follows the same rules).
        // A record rule runs only where its base rule was resolved for this date, BEFORE exclusion (089c K1 / K3, confirmed by ontology-r1):
        // a deleted base, a retired Procedure or a dropped rule leaves it with no slot.
        var rules = new List<ResolvedRule>();
        var excluded = new List<ExcludedRule>();
        foreach (var (version, procedure) in baseRules)
        {
            switch (DecideRuleOverrides(version.PolicyId, workDate))
            {
                case RuleDecision.Kept:
                    rules.Add(new ResolvedRule(version, procedure, null));
                    break;
                case RuleDecision.Excluded plain:
                    excluded.Add(new ExcludedRule(version, procedure, plain.Rows, plain.Distance, ReplacedByRecordRule: false));
                    break;
                case RuleDecision.Changed changed:
                    excluded.Add(new ExcludedRule(version, procedure, changed.Rows, changed.Distance, ReplacedByRecordRule: true));
                    rules.AddRange(changed.RecordRules.Select(r => new ResolvedRule(
                        r.Version, procedure, new RecordRuleSlot(version.PolicyId, version.PolicyCode, version.PolicyVersionId, r.ChainRecord, r.Exclude))));
                    break;
            }
        }

        return new ProcedureRulesOnDate(
            workDate,
            applied.OrderBy(p => p.ProcedureId).ToList(),
            rules.OrderBy(r => r.Version.PolicyCode, StringComparer.Ordinal).ThenBy(r => r.Version.PolicyId).ToList(),
            excluded.OrderBy(e => e.Version.PolicyCode, StringComparer.Ordinal).ThenBy(e => e.Version.PolicyId).ToList(),
            _excludedProcedures,
            gaps.OrderBy(g => g.ProcedureId).ToList());
    }

    // ── Procedure applicability (date-independent) ─────────────────────────────────────────────────────────────

    private sealed record Candidate(ProcedureRow Procedure, ProcedureRank Rank, ProcedureSources Sources, IReadOnlyList<ProcedureOverrideRow> Decisive);

    private static (IReadOnlyList<Candidate>, IReadOnlyList<ExcludedProcedure>) Applicability(ProcedureRows rows)
    {
        var procedureRows = rows.Overrides.Where(o => o.ProcedureId is not null).ToLookup(o => o.ProcedureId!.Value);
        var candidates = new List<Candidate>();
        var excluded = new List<ExcludedProcedure>();
        foreach (var procedure in rows.Procedures.OrderBy(p => p.Id))
        {
            // The firm condition, on EVERY source (O-20): a Procedure that names a firm applies only to that firm's invoices, whether it
            // comes from all records, the firm or a manual assignment.
            if (!FirmConditionHolds(procedure, rows.FirmId))
            {
                continue;
            }

            // The nearer row wins, whatever its mode (6.5): an Exclude at d1 is lifted only by a d0 row of the same Procedure. Between two
            // rows at the same distance (a work assignment's two d1 parents), Exclude wins (D-177 (3)).
            var onChain = procedureRows[procedure.Id].ToList();
            var decisive = onChain.Count == 0
                ? []
                : onChain.Where(o => o.Distance == onChain.Min(r => r.Distance)).OrderBy(o => o.RowId).ToList();
            if (decisive.Any(o => o.Mode == ProcedureOverrideMode.Excluded))
            {
                excluded.Add(new ExcludedProcedure(procedure.Id, procedure.Name, decisive));
                continue;
            }

            var sources = ProcedureSources.None;
            if (procedure.AppliesTo == ProcedureAppliesTo.AllRecords)
            {
                sources |= ProcedureSources.AllRecords;
            }

            if (procedure.AppliesTo == ProcedureAppliesTo.Firm && procedure.FirmId == rows.FirmId)
            {
                sources |= ProcedureSources.Firm;
            }

            if (decisive.Count > 0)
            {
                sources |= ProcedureSources.Manual;
            }

            // "Assigned records only" with no assignment on the chain has no source (081 MUST-DO must-fire): not applicable, no gap.
            if (sources == ProcedureSources.None)
            {
                continue;
            }

            // One Procedure from several sources takes the highest level (6.5).
            var level = decisive.Count > 0
                ? decisive[0].Distance == 0 ? ProcedureLevel.ManualOwn : ProcedureLevel.ManualParent
                : sources.HasFlag(ProcedureSources.Firm) ? ProcedureLevel.Firm : ProcedureLevel.AllRecords;
            var distance = decisive.Count > 0 ? decisive[0].Distance : 0;
            candidates.Add(new Candidate(procedure, new ProcedureRank(level, distance, procedure.FirmId is not null), sources, decisive));
        }

        return (candidates, excluded);
    }

    private static bool FirmConditionHolds(ProcedureRow procedure, Guid firmId) => procedure.FirmId is null || procedure.FirmId == firmId;

    // ── Version per date (7.1, 7.2) ────────────────────────────────────────────────────────────────────────────

    private abstract record VersionChoice
    {
        public sealed record Chosen(ProcedureVersionRow Version, bool Pinned) : VersionChoice;

        public sealed record Gap(ProcedureGapKind Kind) : VersionChoice;

        public sealed record NotApplicable : VersionChoice;
    }

    private VersionChoice ChooseVersion(Candidate candidate, DateOnly date)
    {
        var versions = _versionsByProcedure[candidate.Procedure.Id].ToList();

        // Owner F2-d, run FIRST for every choice, pinned or not (ontology-r1 D-203 Q2): work dated before the Procedure's first version
        // opened is not applicable, and no pin outcome (a Retired or missing pinned version included) turns that into a gap.
        if (NotApplicableOn(versions, date))
        {
            return new VersionChoice.NotApplicable();
        }

        // Each decisive row's choice: a Pin names its version, a Follow takes the version in force on the date. A computed source alone
        // follows the date. Each is a version or a gap.
        var choices = candidate.Decisive.Count == 0
            ? [ByDate(candidate.Procedure, versions, date)]
            : candidate.Decisive.Select(row => row.Mode == ProcedureOverrideMode.Pinned
                ? Pinned(row.PinnedVersionId, versions)
                : ByDate(candidate.Procedure, versions, date)).ToList();

        // Rows at the same distance that choose different outcomes for the date conflict (D-177 (3), D-203 Q1): two different pins, a Follow
        // beside a Pin of another version, or a version beside none (a gap) raise one blocking gap. Rows that all end in a gap keep the
        // first gap; rows that agree on a version choose it, pinned if any of them pins.
        var gaps = choices.OfType<VersionChoice.Gap>().ToList();
        if (gaps.Count > 0)
        {
            return gaps.Count == choices.Count ? gaps[0] : new VersionChoice.Gap(ProcedureGapKind.ConflictingParents);
        }

        var chosen = choices.OfType<VersionChoice.Chosen>().ToList();
        return chosen.Select(c => c.Version.Id).Distinct().Count() > 1
            ? new VersionChoice.Gap(ProcedureGapKind.ConflictingParents)
            : new VersionChoice.Chosen(chosen[0].Version, chosen.Any(c => c.Pinned));
    }

    /// <summary>
    /// Owner F2-d / D-203: before the date the Procedure's first version opened (the earliest effective-from of a version that opened a
    /// window: <see cref="ProcedureVersionRow.OpenedAWindow"/>; a version withdrawn later still opened its window, 3.1 I-P5), the Procedure
    /// is not applicable. So is a Procedure with no such version (Draft-only, or a Draft that was only ever retired). It is not a gap.
    /// </summary>
    private bool NotApplicableOn(IReadOnlyList<ProcedureVersionRow> versions, DateOnly date)
    {
        var opened = versions.Where(v => v.OpenedAWindow(_versionsWithPublishedRules)).Select(v => v.From!.Value).ToList();
        return opened.Count == 0 || date < opened.Min();
    }

    private static VersionChoice Pinned(Guid? pinnedVersionId, IReadOnlyList<ProcedureVersionRow> versions)
    {
        var pinned = pinnedVersionId is { } id ? versions.FirstOrDefault(v => v.Id == id) : null;
        return pinned switch
        {
            { Status: ProcedureEntity.VersionStatusRetired } => new VersionChoice.Gap(ProcedureGapKind.PinnedVersionRetired),
            { Status: ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded } => new VersionChoice.Chosen(pinned, Pinned: true),
            _ => new VersionChoice.Gap(ProcedureGapKind.PinnedVersionNotInForce),
        };
    }

    private VersionChoice ByDate(ProcedureRow procedure, IReadOnlyList<ProcedureVersionRow> versions, DateOnly date)
    {
        // In force for its window: Active, and Superseded (model 3.1: a Superseded version stays in force for its own window). A Retired
        // version is never in force (I-P5): work inside its window is a gap, below.
        var inForce = versions.Where(v => v.IsInForce).ToList();
        var containing = inForce.Where(v => v.Contains(date)).ToList();
        if (containing.Count > 1)
        {
            throw new ProcedureResolutionFaultException(
                $"Procedure {procedure.Id} has {containing.Count} versions in force on {date:yyyy-MM-dd} (I-P1); the evaluation cannot choose one.");
        }

        if (containing.Count == 1)
        {
            return new VersionChoice.Chosen(containing[0], Pinned: false);
        }

        // The caller has run the F2-d test (the date is not before the first version opened), so a date in no window of a version in
        // force is a gap: a hole before a later version, the window of a Retired version that was in force (a withdrawn version, D-203;
        // a Retired Draft that never was has no window), or past the last end.
        return new VersionChoice.Gap(inForce.Any(v => v.From!.Value > date)
            || versions.Any(v => v.Status == ProcedureEntity.VersionStatusRetired && v.OpenedAWindow(_versionsWithPublishedRules) && v.Contains(date))
            ? ProcedureGapKind.HoleBetweenVersions
            : ProcedureGapKind.AfterLastVersion);
    }

    // ── Rules of a Procedure Version (7.2) ─────────────────────────────────────────────────────────────────────

    private IEnumerable<ResolvedPolicyVersion> RulesOf(Guid procedureId, Guid procedureVersionId, bool pinned, DateOnly date)
    {
        foreach (var set in _rows.Sets.Where(s => s.ProcedureId == procedureId))
        {
            foreach (var policy in _policiesBySet[set.Id])
            {
                // A rule applies on a date only with a Published version under the Procedure Version chosen for that date: enabled is
                // required but not sufficient (089c K1, ontology-r1 D-177 (1)(2)(14), D-203). sprk_enabled is Off only for a removed record
                // rule or a rule of a retired Procedure; it never picks a version, and "dropped" is never inferred from Off (a rule a later
                // version dropped stays enabled with no version under it). Within that Procedure Version the latest version with
                // effective-from <= the date applies (corrections). A pinned version applies whatever the date, once the Procedure is
                // applicable: work dated before its first policy version takes that first one.
                var under = _versionsByPolicy[policy.Id]
                    .Where(v => v.ProcedureVersionId == procedureVersionId && v.InForceFrom is not null)
                    .ToList();
                var selected = Latest(under, date) ?? (pinned ? Earliest(under) : null);
                if (selected is not null)
                {
                    yield return selected;
                }
            }
        }
    }

    private static ResolvedPolicyVersion? Latest(IEnumerable<ResolvedPolicyVersion> versions, DateOnly date) => versions
        .Where(v => v.InForceFrom is { } from && from <= date)
        .OrderByDescending(v => v.InForceFrom)
        .ThenByDescending(v => v.VersionNumber)
        .ThenByDescending(v => v.PolicyVersionId)
        .FirstOrDefault();

    private static ResolvedPolicyVersion? Earliest(IEnumerable<ResolvedPolicyVersion> versions) => versions
        .OrderBy(v => v.InForceFrom)
        .ThenBy(v => v.VersionNumber)
        .ThenBy(v => v.PolicyVersionId)
        .FirstOrDefault();

    // ── Rule-level overrides and record rules (6.3, 7.1) ───────────────────────────────────────────────────────

    private abstract record RuleDecision
    {
        public sealed record Kept : RuleDecision;

        public sealed record Excluded(IReadOnlyList<ProcedureOverrideRow> Rows, int Distance) : RuleDecision;

        public sealed record Changed(IReadOnlyList<ProcedureOverrideRow> Rows, int Distance, IReadOnlyList<RecordRuleRun> RecordRules) : RuleDecision;
    }

    private sealed record RecordRuleRun(ResolvedPolicyVersion Version, CoreRecordLink ChainRecord, ProcedureOverrideRow? Exclude);

    /// <summary>A rule-level override of one base rule: a plain Exclude row, or a change (a record rule, with the change's Exclude row).</summary>
    private sealed record RuleOverride(CoreRecordLink ChainRecord, ProcedureOverrideRow? Exclude, PolicyRow? RecordRule);

    private RuleDecision DecideRuleOverrides(Guid basePolicyId, DateOnly date)
    {
        var overrides = RuleOverridesOf(basePolicyId);
        if (overrides.Count == 0)
        {
            return new RuleDecision.Kept();
        }

        // The nearer override wins (6.5). At one distance (a work assignment's two d1 parents) a plain Exclude wins over a change.
        var nearest = overrides.Min(o => o.ChainRecord.Distance);
        var decisive = overrides.Where(o => o.ChainRecord.Distance == nearest).ToList();
        var rows = decisive.Select(o => o.Exclude).OfType<ProcedureOverrideRow>().OrderBy(r => r.RowId).ToList();
        if (decisive.Any(o => o.RecordRule is null))
        {
            return new RuleDecision.Excluded(rows, nearest);
        }

        // Owner decision (2026-11-04): a record rule applies only to work dates on or after its own effective-from. Earlier dates are judged
        // by the base version in force on that date, without the record's change, so the change's Exclude does not remove the base either.
        var running = decisive
            .Select(o => (o, Version: RecordRuleVersionOn(o.RecordRule!, o.Exclude!, date)))
            .Where(x => x.Version is not null)
            .Select(x => new RecordRuleRun(x.Version!, x.o.ChainRecord, x.o.Exclude))
            .ToList();
        return running.Count == 0 ? new RuleDecision.Kept() : new RuleDecision.Changed(rows, nearest, running);
    }

    /// <summary>
    /// The version of a record rule in effect on <paramref name="date"/>, or null while the change is not (the base rule then runs).
    /// <para><b>Episodes (ontology-r1 D-203 (5)-(7); 089b I-A1, 089c D-202).</b> A record rule's policy is reused across a removal and a new
    /// change of the same rule, so its older versions belong to a removed change and must never come back. Only the versions of the CURRENT
    /// EPISODE count: those created on or after the change's Exclude row. The Exclude row and the first version of an episode are written
    /// in one transaction, the row first, so a version of that episode is never created before its row; a removal deletes the row, so a
    /// later change has a newer one. Inside an episode the latest version whose effective-from is on or before the date applies (an edit
    /// adds a version in force from then on: the change was never removed). Before the episode's first effective-from the base rule runs.</para>
    /// <para>A change whose Exclude row has no <c>createdon</c> has no known boundary: every version counts (the previous behaviour). A
    /// version with no <c>createdon</c> beside a known boundary does not count. A record rule whose Exclude row is missing is a fault
    /// (<see cref="RuleOverridesOf"/>), and so is a known boundary that no Published version of the rule reaches (review K2, ontology-r1
    /// D-203): the rule has versions but none of them belongs to the current episode, a state the BFF never writes (it writes the row
    /// and the episode's first version in one transaction). The base rule must not quietly run in its place. A date before the episode's
    /// first effective-from is not that: the episode has versions, and the base rule runs.</para>
    /// </summary>
    private ResolvedPolicyVersion? RecordRuleVersionOn(PolicyRow recordRule, ProcedureOverrideRow exclude, DateOnly date)
    {
        var all = _versionsByPolicy[recordRule.Id].ToList();
        if (exclude.CreatedOn is not { } episodeStart)
        {
            return Latest(all, date);
        }

        var inEpisode = all.Where(v => v.CreatedOn is { } created && created >= episodeStart).ToList();
        if (all.Count > 0 && inEpisode.Count == 0)
        {
            throw new ProcedureResolutionFaultException(
                $"Record rule {recordRule.Id} has {all.Count} Published version(s) but none was created on or after its change's Exclude row {exclude.RowId}; "
                + "the current change has no version, so it cannot be told from an older change that was removed.");
        }

        return Latest(inEpisode, date);
    }

    private List<RuleOverride> RuleOverridesOf(Guid basePolicyId)
    {
        // Rule-level rows for this invoice's firm: the row's Firm is empty or the invoice's firm (an excluded rule for F still runs on G's).
        var excludes = _rows.Overrides
            .Where(o => o.PolicyId == basePolicyId && (o.FirmId is null || o.FirmId == _rows.FirmId))
            .ToList();

        // The record rule of this rule in each record-scoped set on the chain (already narrowed to this firm at read). It is found by its
        // DERIVED id, the one the change wrote (K1): the id is a hash of (core table, record, firm, base rule), so a cleared or wrong
        // sprk_basedonversion can never hide it and let its Exclude act as a plain Exclude. A record rule whose base rule is not resolved
        // for the date (deleted, dropped by a later version, or of a retired Procedure) is never looked up: it has no slot.
        var result = new List<RuleOverride>();
        var paired = new HashSet<Guid>();
        foreach (var set in _rows.Sets.Where(s => s.Record is not null))
        {
            var recordRuleId = ProcedureAssignmentService.RecordRulePolicyIdFor(
                ProcedureAssignmentService.RecordRuleSetIdFor(set.Record!.Table, set.Record.Id, set.FirmId), basePolicyId);
            if (_policiesBySet[set.Id].FirstOrDefault(p => p.Id == recordRuleId) is not { } policy)
            {
                continue;
            }

            RequireBasedOn(policy, basePolicyId);

            // The change's Exclude row sits on the same record, for the same firm as the set (Firm record: its firm; Record: no firm). An
            // enabled record rule with no such row is a state the BFF never writes (the row, the record rule and its first version are
            // written in one transaction, and a removal deletes the row and switches the rule Off in one): its episode has no start, so it
            // is not read as a change with every version counted (review K3, ontology-r1 D-203). It faults; the base rule never runs in its place.
            var exclude = excludes.FirstOrDefault(e => e.ChainRecord == set.Record && e.FirmId == set.FirmId)
                ?? throw new ProcedureResolutionFaultException(
                    $"Record rule {policy.Id} of base rule {basePolicyId} is enabled but its change has no Exclude row on its record; "
                    + "the change cannot be read, and it is never read as having every version in force.");
            paired.Add(exclude.RowId);

            result.Add(new RuleOverride(set.Record, exclude, policy));
        }

        result.AddRange(excludes.Where(e => !paired.Contains(e.RowId)).Select(e => new RuleOverride(e.ChainRecord, e, null)));
        return result;
    }

    /// <summary>
    /// A record rule found by its derived id must name, in every Published version, a version of its base rule (the check
    /// <c>ProcedureAssignmentService.EnsureRecordRuleAsync</c> makes before it writes one). A link that is missing or names another rule's
    /// version is a row the BFF never writes: the change cannot be read, and it is never read as a plain Exclude (K1). The message names
    /// ids for operators; it is never shown to a reader.
    /// </summary>
    private void RequireBasedOn(PolicyRow recordRule, Guid basePolicyId)
    {
        foreach (var version in _versionsByPolicy[recordRule.Id])
        {
            if (version.BasedOnVersionId is not { } baseVersion || !_versionsById.TryGetValue(baseVersion, out var based) || based.PolicyId != basePolicyId)
            {
                throw new ProcedureResolutionFaultException(
                    $"Record rule {recordRule.Id} has a version {version.PolicyVersionId} that does not name a version of its base rule {basePolicyId}; "
                    + "the change cannot be told from a plain Exclude.");
            }
        }
    }
}

/// <summary>
/// The Procedure resolver (model 7.1, re-keyed version selection of 7.2): reads, once per invoice and app-only, the stored rows that
/// decide which Procedures and rules judge it, and returns a <see cref="ProcedureResolution"/> that resolves each work date in memory.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Reads</b> (each once, ids chunked, every query paged): the core-record chain (<see cref="CoreRecordChain"/>, D-172: d0 and its
/// direct parents); the override rows on the chain; the Billing Procedures that are Active and either apply to all records, apply to the
/// invoice's firm, or are assigned on the chain; their Procedure Versions; their rule sets and the chain's record-scoped sets (Record, or
/// Firm record of the invoice's firm); those sets' enabled Billing policies; and those policies' Published versions. Every row is
/// re-checked after its query. <c>sprk_currentversion</c> is never read: it means "latest published", for display only (7.2).</item>
/// <item><b>Fails closed:</b> any read fault propagates, the chain's <see cref="CoreRecordChainUnreadableException"/> included, and an
/// override row with a mode the BFF never writes, a row naming both or neither of a Procedure and a rule, a record rule that does not
/// name a version of its base rule, an enabled record rule with no Exclude row, and a record rule none of whose versions is in the
/// current episode throw <see cref="ProcedureResolutionFaultException"/> (operator text: never show its message to a reader). Nothing
/// is judged on a partial set.</item>
/// <item><b>Not here</b> (082c, 082d): the loader and evaluator re-key, the precedence between rules by
/// <see cref="BillingRuleCatalogue.PrecedenceOf"/> and rank, the gap finding and hold, the snapshot, the rate card.
/// <c>BILL-SYSTEM-SUBMISSION</c> is exempt (D-125): it is never returned here; the loader keeps loading it.</item>
/// </list>
/// </remarks>
public static class ProcedureResolver
{
    private const int StateActive = 0;
    private const int PageSize = 5000;
    private const string UnnamedProcedure = "(unnamed Procedure)";
    private const string PolicyCodeColumn = "sprk_policycode";
    private const string RuleSetColumn = "sprk_ruleset";
    private const string ScopeColumn = "sprk_scope";
    private const string FirmColumn = "sprk_firm";
    private const string CreatedOnColumn = "createdon";

    /// <summary>The core tables whose lookups place a row on the chain (assignment rows and record-scoped sets carry the same three).</summary>
    private static readonly string[] CoreTables = [SecureRootInheritance.Matter, SecureRootInheritance.Project, SecureRootInheritance.WorkAssignment];

    /// <summary>Reads the rows for an invoice of <paramref name="firmId"/> on <paramref name="core"/> (<c>null</c> = no core record).</summary>
    public static async Task<ProcedureResolution> ReadAsync(
        IGenericEntityService entities, ILogger logger, CoreRecordReference? core, Guid firmId, CancellationToken ct) =>
        new(await ReadRowsAsync(entities, logger, core, firmId, ct).ConfigureAwait(false));

    internal static async Task<ProcedureRows> ReadRowsAsync(
        IGenericEntityService entities, ILogger logger, CoreRecordReference? core, Guid firmId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(logger);
        if (firmId == Guid.Empty)
        {
            throw new ArgumentException("The invoice's firm must not be empty.", nameof(firmId));
        }

        IReadOnlyList<CoreRecordLink> chain = core is null ? [] : await CoreRecordChain.ReadAsync(entities, logger, core, ct).ConfigureAwait(false);
        var overrides = chain.Count == 0 ? [] : await ReadOverridesAsync(entities, chain, ct).ConfigureAwait(false);
        var procedures = await ReadProceduresAsync(entities, firmId, overrides, ct).ConfigureAwait(false);
        var procedureIds = procedures.Select(p => p.Id).ToList();
        var versions = await ReadVersionsAsync(entities, procedureIds, ct).ConfigureAwait(false);
        var sets = (await ReadProcedureSetsAsync(entities, procedureIds, ct).ConfigureAwait(false))
            .Concat(chain.Count == 0 ? [] : await ReadRecordSetsAsync(entities, chain, firmId, ct).ConfigureAwait(false))
            .ToList();
        var policies = await ReadPoliciesAsync(entities, sets.Select(s => s.Id).ToList(), ct).ConfigureAwait(false);
        var policyVersions = await ReadPolicyVersionsAsync(entities, policies, ct).ConfigureAwait(false);
        return new ProcedureRows(firmId, chain, overrides, procedures, versions, sets, policies, policyVersions);
    }

    // ── Override rows on the chain ─────────────────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<ProcedureOverrideRow>> ReadOverridesAsync(
        IGenericEntityService entities, IReadOnlyList<CoreRecordLink> chain, CancellationToken ct)
    {
        var query = new QueryExpression(ProcedureAssignmentEntity.Entity)
        {
            ColumnSet = new ColumnSet(CoreTables.Concat(new[]
            {
                ProcedureAssignmentEntity.ProcedureColumn, ProcedureAssignmentEntity.ModeColumn, ProcedureAssignmentEntity.PinnedVersionColumn,
                ProcedureAssignmentEntity.ExcludedPolicyColumn, ProcedureAssignmentEntity.FirmColumn, ProcedureAssignmentEntity.ReasonColumn,
                "statecode", CreatedOnColumn,
            }).ToArray()),
            NoLock = true,
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
        query.Criteria.AddFilter(OnChain(chain));

        var rows = new List<ProcedureOverrideRow>();
        foreach (var row in await AllAsync(entities, query, ct).ConfigureAwait(false))
        {
            // Re-checked: an inactive row, or a row whose core lookups name no chain record (every core lookup null included: the 080
            // parental limit, ontology-r1 2026-10-21), contributes nothing. It is never read as unscoped.
            if (row.GetAttributeValue<OptionSetValue>("statecode")?.Value is not (null or StateActive) || NearestLink(row, chain) is not { } link)
            {
                continue;
            }

            // A row overrides exactly one subject: a Procedure or a rule. A row naming both, or neither, is one the BFF never writes (K6):
            // it is not guessed at, whichever lookup would win.
            var procedureId = row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ProcedureColumn)?.Id;
            var policyId = row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.ExcludedPolicyColumn)?.Id;
            if (procedureId is null == policyId is null)
            {
                throw new ProcedureResolutionFaultException(
                    $"Override row {row.Id} names {(procedureId is null ? "neither a Procedure nor a rule" : "both a Procedure and a rule")}, which the BFF never writes; the Procedures that judge the invoice cannot be told.");
            }

            var mode = row.GetAttributeValue<OptionSetValue>(ProcedureAssignmentEntity.ModeColumn)?.Value switch
            {
                ProcedureAssignmentEntity.ModeFollowLatest => ProcedureOverrideMode.FollowLatest,
                ProcedureAssignmentEntity.ModePinned => ProcedureOverrideMode.Pinned,
                ProcedureAssignmentEntity.ModeExcluded => ProcedureOverrideMode.Excluded,
                var other => throw new ProcedureResolutionFaultException(
                    $"Override row {row.Id} has mode {other?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)"}, which the BFF never writes; the Procedures that judge the invoice cannot be told."),
            };

            // A rule-level row is an Exclude (a change is an Exclude plus a record rule); any other mode on it is not an override the BFF writes.
            if (policyId is not null && mode != ProcedureOverrideMode.Excluded)
            {
                throw new ProcedureResolutionFaultException($"Rule override row {row.Id} has mode {mode}; only Excluded is written for a rule.");
            }

            rows.Add(new ProcedureOverrideRow(
                row.Id, mode, link, procedureId,
                row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.PinnedVersionColumn)?.Id,
                policyId,
                row.GetAttributeValue<EntityReference>(ProcedureAssignmentEntity.FirmColumn)?.Id,
                row.GetAttributeValue<string>(ProcedureAssignmentEntity.ReasonColumn),
                row.GetAttributeValue<DateTime?>(CreatedOnColumn)));
        }

        return rows.OrderBy(r => r.RowId).ToList();
    }

    // ── Procedures and versions ────────────────────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<ProcedureRow>> ReadProceduresAsync(
        IGenericEntityService entities, Guid firmId, IReadOnlyList<ProcedureOverrideRow> overrides, CancellationToken ct)
    {
        // Only Follow and Pin rows bring a Procedure in; an Exclude only removes one that comes from somewhere else.
        var assigned = overrides
            .Where(o => o.ProcedureId is not null && o.Mode != ProcedureOverrideMode.Excluded)
            .Select(o => o.ProcedureId!.Value)
            .Distinct()
            .Order()
            .ToList();

        var found = new Dictionary<Guid, ProcedureRow>();
        // The id list is chunked; the computed sources ride on the first query only.
        var chunks = assigned.Count == 0 ? [Array.Empty<Guid>()] : assigned.Chunk(BillingFactLoader.MaxInValues).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            var query = new QueryExpression(ProcedureEntity.Entity)
            {
                ColumnSet = new ColumnSet(
                    ProcedureEntity.NameColumn, ProcedureEntity.AreaColumn, ProcedureEntity.AppliesToColumn, ProcedureEntity.FirmColumn,
                    ProcedureEntity.StatusColumn),
                NoLock = true,
            };
            query.Criteria.AddCondition(ProcedureEntity.AreaColumn, ConditionOperator.Equal, RuleDomains.PolicyDomainBilling);
            query.Criteria.AddCondition(ProcedureEntity.StatusColumn, ConditionOperator.Equal, ProcedureEntity.StatusActive);
            var sources = new FilterExpression(LogicalOperator.Or);
            if (i == 0)
            {
                sources.AddCondition(ProcedureEntity.AppliesToColumn, ConditionOperator.Equal, (int)ProcedureAppliesTo.AllRecords);
                var firm = new FilterExpression(LogicalOperator.And);
                firm.AddCondition(ProcedureEntity.AppliesToColumn, ConditionOperator.Equal, (int)ProcedureAppliesTo.Firm);
                firm.AddCondition(ProcedureEntity.FirmColumn, ConditionOperator.Equal, firmId);
                sources.AddFilter(firm);
            }

            if (chunks[i].Length > 0)
            {
                sources.AddCondition(ProcedureEntity.Entity + "id", ConditionOperator.In, chunks[i].Cast<object>().ToArray());
            }

            query.Criteria.AddFilter(sources);
            foreach (var row in await AllAsync(entities, query, ct).ConfigureAwait(false))
            {
                // Re-checked: Billing area, Active (an assignment to a Retired Procedure is ignored: 089b review 2 K1).
                if (row.GetAttributeValue<OptionSetValue>(ProcedureEntity.AreaColumn)?.Value != RuleDomains.PolicyDomainBilling
                    || row.GetAttributeValue<OptionSetValue>(ProcedureEntity.StatusColumn)?.Value != ProcedureEntity.StatusActive)
                {
                    continue;
                }

                // An Active Billing Procedure whose Applies to is unknown or empty cannot be placed (7.1): fail closed, as an unknown mode does.
                if (row.GetAttributeValue<OptionSetValue>(ProcedureEntity.AppliesToColumn)?.Value is not { } appliesTo
                    || !Enum.IsDefined((ProcedureAppliesTo)appliesTo))
                {
                    throw new ProcedureResolutionFaultException(
                        $"Procedure {row.Id} has an unknown or empty Applies to; the Procedures that judge the invoice cannot be told.");
                }

                // The name reaches the reader view; a missing name never falls back to the row id (U3).
                var name = row.GetAttributeValue<string>(ProcedureEntity.NameColumn);
                found[row.Id] = new ProcedureRow(
                    row.Id, string.IsNullOrWhiteSpace(name) ? UnnamedProcedure : name, (ProcedureAppliesTo)appliesTo,
                    row.GetAttributeValue<EntityReference>(ProcedureEntity.FirmColumn)?.Id);
            }
        }

        return found.Values.OrderBy(p => p.Id).ToList();
    }

    private static async Task<IReadOnlyList<ProcedureVersionRow>> ReadVersionsAsync(
        IGenericEntityService entities, IReadOnlyList<Guid> procedureIds, CancellationToken ct)
    {
        var versions = new List<ProcedureVersionRow>();
        foreach (var chunk in procedureIds.Chunk(BillingFactLoader.MaxInValues))
        {
            var query = new QueryExpression(ProcedureEntity.VersionEntity)
            {
                ColumnSet = new ColumnSet(
                    ProcedureEntity.VersionProcedureColumn, ProcedureEntity.VersionNumberColumn, ProcedureEntity.VersionEffectiveFromColumn,
                    ProcedureEntity.VersionEffectiveToColumn, ProcedureEntity.VersionStatusColumn),
                NoLock = true,
            };
            query.Criteria.AddCondition(ProcedureEntity.VersionProcedureColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var row in await AllAsync(entities, query, ct).ConfigureAwait(false))
            {
                if (row.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id is not { } procedureId
                    || !chunk.Contains(procedureId))
                {
                    continue;
                }

                versions.Add(new ProcedureVersionRow(
                    row.Id, procedureId, row.GetAttributeValue<int?>(ProcedureEntity.VersionNumberColumn) ?? 0,
                    row.GetAttributeValue<OptionSetValue>(ProcedureEntity.VersionStatusColumn)?.Value ?? ProcedureEntity.VersionStatusDraft,
                    DateOf(row.GetAttributeValue<DateTime?>(ProcedureEntity.VersionEffectiveFromColumn)),
                    DateOf(row.GetAttributeValue<DateTime?>(ProcedureEntity.VersionEffectiveToColumn))));
            }
        }

        return versions.OrderBy(v => v.Id).ToList();
    }

    // ── Rule sets, policies and policy versions ────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<RuleSetRow>> ReadProcedureSetsAsync(
        IGenericEntityService entities, IReadOnlyList<Guid> procedureIds, CancellationToken ct)
    {
        var sets = new List<RuleSetRow>();
        foreach (var chunk in procedureIds.Chunk(BillingFactLoader.MaxInValues))
        {
            var query = new QueryExpression(BillingFactLoader.RuleSetEntity)
            {
                ColumnSet = new ColumnSet(BillingFactLoader.ProcedureColumn, RuleDomains.PolicyDomainColumn, "statecode"),
                NoLock = true,
            };
            RuleDomains.AddBillingFilter(query);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            query.Criteria.AddCondition(BillingFactLoader.ProcedureColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var row in await AllAsync(entities, query, ct).ConfigureAwait(false))
            {
                if (RuleDomains.IsBillingRow(row)
                    && row.GetAttributeValue<OptionSetValue>("statecode")?.Value is null or StateActive
                    && row.GetAttributeValue<EntityReference>(BillingFactLoader.ProcedureColumn)?.Id is { } procedureId
                    && chunk.Contains(procedureId))
                {
                    sets.Add(new RuleSetRow(row.Id, procedureId, null, null));
                }
            }
        }

        return sets;
    }

    private static async Task<IReadOnlyList<RuleSetRow>> ReadRecordSetsAsync(
        IGenericEntityService entities, IReadOnlyList<CoreRecordLink> chain, Guid firmId, CancellationToken ct)
    {
        var query = new QueryExpression(BillingFactLoader.RuleSetEntity)
        {
            ColumnSet = new ColumnSet(CoreTables.Concat(new[]
            {
                ScopeColumn, FirmColumn, BillingFactLoader.ProcedureColumn, RuleDomains.PolicyDomainColumn, "statecode",
            }).ToArray()),
            NoLock = true,
        };
        RuleDomains.AddBillingFilter(query);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
        query.Criteria.AddCondition(ScopeColumn, ConditionOperator.In, RecordRuleSet.ScopeRecord, RecordRuleSet.ScopeFirmRecord);
        query.Criteria.AddFilter(OnChain(chain));

        var sets = new List<RuleSetRow>();
        foreach (var row in await AllAsync(entities, query, ct).ConfigureAwait(false))
        {
            // A record-scoped set needs its scope AND a record lookup on the chain; scope Record carries no firm, Firm record carries the
            // invoice's firm (another firm's set is not this invoice's). A set that is not of one home (RecordRuleSet.RequireOneHome) is skipped.
            var scope = row.GetAttributeValue<OptionSetValue>(ScopeColumn)?.Value;
            var firm = row.GetAttributeValue<EntityReference>(FirmColumn)?.Id;
            var forThisFirm = scope == RecordRuleSet.ScopeRecord ? firm is null : scope == RecordRuleSet.ScopeFirmRecord && firm == firmId;
            if (!RuleDomains.IsBillingRow(row)
                || row.GetAttributeValue<OptionSetValue>("statecode")?.Value is not (null or StateActive)
                || row.GetAttributeValue<EntityReference>(BillingFactLoader.ProcedureColumn) is not null
                || CoreTables.Count(c => row.GetAttributeValue<EntityReference>(c) is not null) != 1
                || !forThisFirm
                || NearestLink(row, chain) is not { } link)
            {
                continue;
            }

            sets.Add(new RuleSetRow(row.Id, null, link, firm));
        }

        return sets;
    }

    private static async Task<IReadOnlyList<PolicyRow>> ReadPoliciesAsync(IGenericEntityService entities, IReadOnlyList<Guid> setIds, CancellationToken ct)
    {
        var policies = new List<PolicyRow>();
        foreach (var chunk in setIds.Distinct().Order().Chunk(BillingFactLoader.MaxInValues))
        {
            var query = new QueryExpression(BillingFactLoader.PolicyEntity)
            {
                ColumnSet = new ColumnSet(PolicyCodeColumn, RuleSetColumn, "sprk_lane", "sprk_severity", "sprk_enabled", "statecode", RuleDomains.PolicyDomainColumn),
                NoLock = true,
            };
            RuleDomains.AddBillingFilter(query);
            query.Criteria.AddCondition(RuleSetColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            foreach (var row in await AllAsync(entities, query, ct).ConfigureAwait(false))
            {
                var code = row.GetAttributeValue<string>(PolicyCodeColumn)?.Trim();
                // Re-checked as the loader does. BILL-SYSTEM-SUBMISSION is exempt (D-125): it has no rule set and is never resolved here.
                if (RuleDomains.IsBillingRow(row)
                    && row.GetAttributeValue<bool?>("sprk_enabled") == true
                    && row.GetAttributeValue<OptionSetValue>("statecode")?.Value == StateActive
                    && row.GetAttributeValue<EntityReference>(RuleSetColumn)?.Id is { } setId && chunk.Contains(setId)
                    && !string.IsNullOrWhiteSpace(code)
                    && !string.Equals(code, BillingRuleCatalogue.SystemPolicyCode, StringComparison.Ordinal))
                {
                    policies.Add(new PolicyRow(row.Id, code, setId,
                        row.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value, row.GetAttributeValue<OptionSetValue>("sprk_severity")?.Value));
                }
            }
        }

        return policies.OrderBy(p => p.Id).ToList();
    }

    private static async Task<IReadOnlyList<ResolvedPolicyVersion>> ReadPolicyVersionsAsync(
        IGenericEntityService entities, IReadOnlyList<PolicyRow> policies, CancellationToken ct)
    {
        var byId = policies.ToDictionary(p => p.Id);
        var versions = new List<ResolvedPolicyVersion>();
        foreach (var chunk in byId.Keys.Order().Chunk(BillingFactLoader.MaxInValues))
        {
            var query = new QueryExpression(BillingFactLoader.VersionEntity)
            {
                ColumnSet = new ColumnSet(
                    "sprk_policy", "sprk_versionnumber", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline",
                    "sprk_consequence", "sprk_clausereference", "sprk_clauselocation", "sprk_inforcefrom", RuleSetService.ProcedureVersionColumn,
                    RuleSetService.PublishStateColumn, ProcedureAssignmentService.BasedOnVersionColumn, CreatedOnColumn),
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, chunk.Cast<object>().ToArray());
            query.Criteria.AddCondition(RuleSetService.PublishStateColumn, ConditionOperator.Equal, RuleSetService.PublishStatePublished);
            foreach (var row in await AllAsync(entities, query, ct).ConfigureAwait(false))
            {
                // Only Published versions are ever loaded (7.2): a Draft never judges anything.
                if (row.GetAttributeValue<OptionSetValue>(RuleSetService.PublishStateColumn)?.Value != RuleSetService.PublishStatePublished
                    || row.GetAttributeValue<EntityReference>("sprk_policy")?.Id is not { } policyId
                    || !byId.TryGetValue(policyId, out var policy))
                {
                    continue;
                }

                versions.Add(new ResolvedPolicyVersion
                {
                    PolicyId = policyId,
                    PolicyCode = policy.Code,
                    PolicyVersionId = row.Id,
                    RuleSetId = policy.RuleSetId,
                    VersionNumber = row.GetAttributeValue<int?>("sprk_versionnumber") ?? 0,
                    RuleType = row.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RuleBody = row.GetAttributeValue<string>("sprk_rulebody"),
                    MessageTemplate = row.GetAttributeValue<string>("sprk_messagetemplate"),
                    ShortHeadline = row.GetAttributeValue<string>("sprk_shortheadline"),
                    ClauseReference = row.GetAttributeValue<string>("sprk_clausereference"),
                    ClauseLocation = row.GetAttributeValue<string>("sprk_clauselocation"),
                    InForceFrom = UtcDate(row.GetAttributeValue<DateTime?>("sprk_inforcefrom")),
                    Lane = policy.Lane,
                    Severity = policy.Severity,
                    Consequence = row.GetAttributeValue<OptionSetValue>("sprk_consequence")?.Value,
                    ProcedureVersionId = row.GetAttributeValue<EntityReference>(RuleSetService.ProcedureVersionColumn)?.Id,
                    BasedOnVersionId = row.GetAttributeValue<EntityReference>(ProcedureAssignmentService.BasedOnVersionColumn)?.Id,
                    CreatedOn = row.GetAttributeValue<DateTime?>(CreatedOnColumn),
                });
            }
        }

        return versions.OrderBy(v => v.PolicyVersionId).ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>(sprk_matter IN chain matters) OR (sprk_project IN …) OR (sprk_workassignment IN …), for the tables the chain holds.</summary>
    private static FilterExpression OnChain(IReadOnlyList<CoreRecordLink> chain)
    {
        var filter = new FilterExpression(LogicalOperator.Or);
        foreach (var group in chain.GroupBy(l => l.Table))
        {
            filter.AddCondition(group.Key, ConditionOperator.In, group.Select(l => (object)l.Id).ToArray());
        }

        return filter;
    }

    /// <summary>The nearest chain record a row's core lookups name, or null when they name none.</summary>
    private static CoreRecordLink? NearestLink(Entity row, IReadOnlyList<CoreRecordLink> chain) => chain
        .Where(l => row.GetAttributeValue<EntityReference>(l.Table)?.Id == l.Id)
        .OrderBy(l => l.Distance)
        .FirstOrDefault();

    private static async Task<List<Entity>> AllAsync(IGenericEntityService entities, QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
        var rows = new List<Entity>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(page.Entities);
            if (!page.MoreRecords)
            {
                return rows;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    /// <summary>A date-only column as stored (the SDK returns it at midnight, kind Unspecified), as <c>ProcedureService</c> reads it.</summary>
    private static DateOnly? DateOf(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    /// <summary><c>sprk_inforcefrom</c> as a UTC date, as the loader reads it.</summary>
    private static DateOnly? UtcDate(DateTime? value) => value is { } v
        ? DateOnly.FromDateTime(v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v)
        : null;
}

/// <summary>The rows one resolution reads (internal so a test can resolve the same rows over a permuted chain).</summary>
internal sealed record ProcedureRows(
    Guid FirmId,
    IReadOnlyList<CoreRecordLink> Chain,
    IReadOnlyList<ProcedureOverrideRow> Overrides,
    IReadOnlyList<ProcedureRow> Procedures,
    IReadOnlyList<ProcedureVersionRow> Versions,
    IReadOnlyList<RuleSetRow> Sets,
    IReadOnlyList<PolicyRow> Policies,
    IReadOnlyList<ResolvedPolicyVersion> PolicyVersions);

internal sealed record ProcedureRow(Guid Id, string Name, ProcedureAppliesTo AppliesTo, Guid? FirmId);

internal sealed record ProcedureVersionRow(Guid Id, Guid ProcedureId, int Number, int Status, DateOnly? From, DateOnly? To)
{
    /// <summary>Active or Superseded with an effective-from: in force for its own window (model 3.1).</summary>
    public bool IsInForce => Status is ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded && From is not null;

    /// <summary>
    /// The version opened a window at its effective-from (ontology-r1 D-203 (5a), (9)): it is Active or Superseded with an effective-from,
    /// or Retired with an effective-from and at least one Published policy version under it. A Retired version is never in force, but a
    /// window it had is not "before the first version" either: work in it is a gap. A Draft never opened one, and neither did a Draft that
    /// was retired unpublished (<paramref name="versionsWithPublishedRules"/> does not name it): publish refuses a Draft whose dates overlap,
    /// so a discarded Draft dated early must not hold work as a gap.
    /// </summary>
    /// <remarks>
    /// Publish marks the version's Accepted rules Published in the same transaction that makes it Active (<c>ProcedureService.PublishVersionAsync</c>,
    /// the only path to Active), so an Active version that had rules has a Published policy version. Two known limits keep the other answer:
    /// a version published with NO Accepted rule (an empty rule set, or every rule rejected) and then retired is read as never opened, as
    /// <c>ProcedureService.ScopeLockedAsync</c> reads it (it governed nothing); and a version that opened a window by a published RATE CARD
    /// alone also counts per model 7.1, but the resolver reads no rate card keyed to a Procedure Version today. Task 085 must extend this
    /// method (and <c>ScopeLockedAsync</c>) with a published-card test when it keys the card to the Procedure Version.
    /// </remarks>
    public bool OpenedAWindow(IReadOnlySet<Guid> versionsWithPublishedRules) =>
        From is not null
        && (Status is ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded
            || Status == ProcedureEntity.VersionStatusRetired && versionsWithPublishedRules.Contains(Id));

    /// <summary>Half-open [from, to); a null <c>to</c> is open-ended.</summary>
    public bool Contains(DateOnly date) => From is { } from && from <= date && (To is not { } to || date < to);
}

/// <summary>A Procedure's rule set (<see cref="ProcedureId"/>) or a chain record's record-scoped set (<see cref="Record"/>, <see cref="FirmId"/>).</summary>
internal sealed record RuleSetRow(Guid Id, Guid? ProcedureId, CoreRecordLink? Record, Guid? FirmId);

internal sealed record PolicyRow(Guid Id, string Code, Guid RuleSetId, int? Lane, int? Severity);
