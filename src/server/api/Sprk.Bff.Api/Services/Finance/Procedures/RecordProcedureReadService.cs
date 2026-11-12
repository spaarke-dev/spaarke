using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

// ebilling-r1 task 084 Part A (notes/procedure-and-file-model.md 3.2, 6.1-6.6, 7.1; EB-D-218, EB-D-219; notes/084-procedure-review-ui.md):
// the two read models of one core record, as the routes of RecordProcedureEndpoints.cs return them.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — no route lists a record's Procedures or its rules in force. ProcedureResolver (082) computes both for evaluation and
//       RuleSetEndpoints lists rule sets for billing admins without a record. AffectedInvoiceCounter + CallerRecordAccessProbe already own
//       the "caller's Read on a batch of records, unknown kept apart from no" question.
//   (2) Extension — this is a thin reader over the resolver's own output (one ReadRowsAsync + RulesOn; no second selection) plus the
//       probe's batched method. A new class rather than a method on the resolver because ProcedureResolver.cs is ontology-r1-reviewed code
//       and 084 changes none of it; rows are reached through the resolver's internal reader.
//   (3) Cost of doing nothing — O-3 and O-9 fail: the rules that judge a record are neither visible nor reviewable, so an override cannot
//       be checked before an invoice is judged, and 084a-084d have nothing to render.
//
// Placement (bff-extensions.md): BFF, Services/Finance/Procedures, beside ProcedureAssignmentService. A scoped concrete class (ADR-010);
// one DI registration in FinanceModule; no package, no background work; writes nothing.

/// <summary>Whether a read model was produced.</summary>
public enum RecordProcedureReadStatus
{
    /// <summary>The answer is complete and the caller can Read every row in it.</summary>
    Done,

    /// <summary>The core record is gone (it disappeared between the route's Read check and this read): the uniform 404.</summary>
    RecordNotFound,

    /// <summary>
    /// Nothing is returned: a row read faulted, the resolver could not resolve the rows, the caller could not be established, a rights
    /// question was not answered, or the caller cannot Read a row the answer would expose (EB-D-218: never a partial list). The route
    /// answers 503 <c>procedures.unavailable</c>.
    /// </summary>
    Unavailable,
}

/// <summary>A read model, or why there is none.</summary>
public sealed record RecordProcedureReadResult<T>(RecordProcedureReadStatus Status, T? Value) where T : class
{
    internal static RecordProcedureReadResult<T> Of(T value) => new(RecordProcedureReadStatus.Done, value);

    internal static RecordProcedureReadResult<T> NotFound() => new(RecordProcedureReadStatus.RecordNotFound, null);

    internal static RecordProcedureReadResult<T> Unavailable() => new(RecordProcedureReadStatus.Unavailable, null);
}

// ── Shared shapes ───────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A record on the chain: the route's record (distance 0) or a direct parent (distance 1). <see cref="Table"/> is the logical name.</summary>
public sealed record ChainRecordView(string Table, Guid Id, string? Name, int Distance);

/// <summary>A firm (<c>sprk_organization</c>) by id and name.</summary>
public sealed record FirmView(Guid Id, string? Name);

/// <summary>Who did something and when: the row's creator person (<c>sprk_createdbyperson</c>) and <c>createdon</c> (UTC). Either part is null when the row carries none.</summary>
public sealed record StampView(Guid? ById, string? ByName, DateTime? On);

/// <summary>A Procedure Version: number, status name and half-open window <c>[From, To)</c> (<c>To</c> null = open-ended).</summary>
public sealed record ProcedureVersionView2(Guid Id, int Number, string Status, DateOnly? From, DateOnly? To);

/// <summary>A policy version by id and number (the rule's own version chain).</summary>
public sealed record PolicyVersionRefView(Guid Id, int Number);

// ── Procedures list (model 6.6, questions a-e) ──────────────────────────────────────────────────────────────────

/// <summary>
/// The Procedures one core record is subject to for a firm and a date. <see cref="FirmSource"/> says where the firm came from:
/// <c>Query</c>, <c>Record</c> (the record's <c>sprk_billingfirm</c>) or <c>None</c> (then only Procedures with no Firm apply).
/// <see cref="Gaps"/> are the blocking gaps (O-22), a separate array as well as each row's <see cref="ProcedureListItem.Status"/>.
/// </summary>
public sealed record RecordProceduresView(
    ChainRecordView Record, DateOnly Date, FirmView? Firm, string FirmSource,
    IReadOnlyList<ProcedureListItem> Procedures, IReadOnlyList<ProcedureGapView> Gaps);

/// <summary>
/// One Procedure on the record. <see cref="Status"/>: <c>InForce</c> (a version applies; <see cref="VersionInForce"/> says which and whether
/// it is pinned), <c>Excluded</c> (returned, not applied: the Exclude is in <see cref="Overrides"/>), <c>Gap</c> (applies but no version can
/// be chosen: the invoice is held, see <see cref="ProcedureGapView"/>) or <c>NotYetInForce</c> (the date is before its first version opened:
/// not applicable, not a gap). <see cref="Level"/> is the highest source's level (<c>AllRecords</c>, <c>Firm</c>, <c>ManualParent</c>,
/// <c>ManualOwn</c>); <see cref="Sources"/> lists EVERY matching source, highest first, the winning one marked.
/// </summary>
public sealed record ProcedureListItem(
    Guid Id, string Name, string Area, string? Type, string AppliesTo, FirmView? Firm, string Status, string Level,
    IReadOnlyList<ProcedureSourceView> Sources, ProcedureVersionView2? VersionInForce, bool Pinned, ProcedureVersionView2? NewerVersion,
    IReadOnlyList<ProcedureOverrideView> Overrides);

/// <summary>
/// One way a Procedure reaches the record. <see cref="Kind"/>: <c>AllRecords</c>, <c>Firm</c>, <c>ManualOwn</c> (an assignment row on this
/// record) or <c>ManualParent</c> (on a direct parent, <see cref="Via"/>). A manual source carries its <see cref="Mode"/>
/// (<c>FollowLatest</c> or <c>Pinned</c>) and the row.
/// </summary>
public sealed record ProcedureSourceView(string Kind, bool Winning, int Distance, ChainRecordView? Via, string? Mode, Guid? RowId);

/// <summary>
/// A Procedure-level assignment row on the chain (any mode). <see cref="Decisive"/>: the row (or one of the rows) that decided the
/// Procedure on this record (nearer wins, 6.5). A pinned row carries <see cref="PinnedVersion"/> and the <see cref="NewerVersion"/> it
/// skips (the newest version in force that is newer than the pin), when there is one.
/// </summary>
public sealed record ProcedureOverrideView(
    Guid RowId, string Mode, bool Decisive, int Distance, ChainRecordView ChainRecord, string? Reason,
    ProcedureVersionView2? PinnedVersion, ProcedureVersionView2? NewerVersion, StampView Stamp);

/// <summary>
/// An applicable Procedure with no version for the date (model 7.1). <see cref="Kind"/> is a <see cref="ProcedureGapKind"/> name;
/// <see cref="Text"/> is a readable sentence; <see cref="Overrides"/> are the rows that decided (for <c>ConflictingParents</c>, the
/// parents' conflicting pins).
/// </summary>
public sealed record ProcedureGapView(
    Guid ProcedureId, string ProcedureName, string Kind, DateOnly WorkDate, string Text, IReadOnlyList<ProcedureOverrideView> Overrides);

// ── Rules in force (model 6.6, questions f-g) ───────────────────────────────────────────────────────────────────

/// <summary>
/// The rules in force on the record for a firm and a date, as <c>ProcedureResolution.RulesOn</c> returns them: <see cref="Rules"/> holds
/// every rule that runs (<see cref="RuleInForceView.InForce"/> true, one per resolved rule, the resolver's order) and then every rule that
/// does not (excluded, or a record rule whose base rule is not in force on the date), each with its marks.
/// </summary>
public sealed record RecordRulesInForceView(
    ChainRecordView Record, DateOnly Date, FirmView? Firm, string FirmSource,
    IReadOnlyList<RuleInForceView> Rules, IReadOnlyList<ProcedureGapView> Gaps);

/// <summary>A Procedure by id and name, with the Procedure Version that supplied a rule and whether it is pinned.</summary>
public sealed record RuleProcedureView(Guid Id, string Name, ProcedureVersionView2? Version, bool Pinned);

/// <summary>
/// One rule. <see cref="InForce"/> false: the rule does not run on this date (<c>excluded</c>, or <c>baseRetired</c>). A record rule
/// (<see cref="RecordRule"/>) is a change made for this record: <see cref="PolicyId"/> is the record rule's, its <see cref="Code"/> the
/// BASE rule's catalogue code (uac-r2 option (a)), and its <c>changed</c> mark names the base rule. <see cref="FirmScope"/> null: all firms.
/// <see cref="Level"/> and <see cref="Distance"/> are the source Procedure's rank. <see cref="ParameterSummary"/> is the rule body in
/// plain language (null when the describer refuses it).
/// </summary>
public sealed record RuleInForceView(
    Guid PolicyId, Guid? PolicyVersionId, string Code, string? Name, string? RuleType, string? ParameterSummary, int? VersionNumber,
    DateOnly? InForceFrom, bool InForce, bool RecordRule, Guid? RuleSetId, RuleProcedureView? Procedure, string? Level, int? Distance,
    FirmView? FirmScope, IReadOnlyList<RuleMarkView> Marks);

/// <summary>
/// A mark on a rule. <see cref="Kind"/>: <c>Excluded</c> (the rule was excluded for this record: reason, optional firm, distance, who and
/// when), <c>Changed</c> (a record rule runs in its slot: <see cref="RecordRuleId"/>, the version it is <see cref="BasedOn"/>, the base
/// rule), <c>Stale</c> (BasedOn is not the base rule's version in force on the date: <see cref="CurrentBase"/>) or <c>BaseRetired</c> (the
/// base rule is not in force on the date, so the record rule does not run). The set is open: a reader must ignore a kind it does not know
/// (task 084e adds <c>Outranked</c>). Members a kind does not use are null.
/// </summary>
public sealed record RuleMarkView(
    string Kind, string? Reason = null, FirmView? Firm = null, int? Distance = null, ChainRecordView? ChainRecord = null, StampView? Stamp = null,
    Guid? OverrideRowId = null, Guid? RecordRuleId = null, PolicyVersionRefView? BasedOn = null, PolicyVersionRefView? CurrentBase = null,
    string? BaseCode = null, string? BaseParameterSummary = null);

/// <summary>
/// Reads the two lists of model 6.6 for a core record (task 084 Part A). <b>Read model, EB-D-218 option A:</b> the rows are read ONCE,
/// app-only, through the existing <see cref="IGenericEntityService"/> and <see cref="ProcedureResolver"/> (no caller-identity entity
/// service, no impersonation); then ONE batched caller-rights check covers EVERY row the answer exposes. Any row the caller cannot Read,
/// a rights question that was not answered, a caller that cannot be established, a faulted row read or a resolver fault is
/// <see cref="RecordProcedureReadStatus.Unavailable"/>: no list, never a partial one. Writes nothing.
/// </summary>
/// <remarks>
/// <para><b>One resolution.</b> Both lists come from <c>new ProcedureResolution(rows).RulesOn(date)</c>, the evaluator's own output. Marks
/// are computed from that output and the rows already read, at read time; nothing is stamped. The rules list's in-force members equal
/// <c>RulesOn(date).Rules</c> one for one. The Procedures list adds only what the resolver's output does not carry (every source, the Type,
/// who and when), read once, and the Procedures that are "not yet in force" on the date: the resolver neither applies nor gaps those, so
/// they are found by asking it for a date on which every Procedure that ever opened a window has opened (still its own output).</para>
/// <para><b>No firm.</b> The resolver refuses an empty firm. With no query firm and no <c>sprk_billingfirm</c>, <see cref="NoFirm"/> (a
/// fixed id that names no firm) is passed, so only Procedures with no Firm apply, only rule-level rows with no Firm count, and no
/// Firm-record set is read. The id never leaves this class.</para>
/// <para><b>Known cost.</b> The caller's rights are asked per distinct row (the probe runs 4 at a time), so a record with many rules
/// asks many questions; the ids are de-duplicated and paged (<see cref="RightsPageSize"/>).</para>
/// </remarks>
public class RecordProcedureReadService
{
    /// <summary>The 503 <c>errorCode</c> of every refusal that returns no list.</summary>
    public const string UnavailableErrorCode = "procedures.unavailable";

    /// <summary>
    /// The firm id passed to the resolver when the answer has no firm. It is a fixed value that no <c>sprk_organization</c> row can have
    /// (version 4 GUID with the reserved nil-prefix pattern), so no Procedure, rule row or record set matches it.
    /// </summary>
    internal static readonly Guid NoFirm = new("00000000-0000-4000-8000-00000000f1f0");

    /// <summary>The rows per batched rights call (a page), as <see cref="AffectedInvoiceCounter.RightsPageSize"/>.</summary>
    internal const int RightsPageSize = 250;

    private const int MaxInValues = 500;
    private const int PageSize = 5000;
    private const string QueryFirmSource = "Query";
    private const string BillingFirmColumn = "sprk_billingfirm";
    private const string CreatedByPersonColumn = "sprk_createdbyperson";
    private const string RuleSetSet = "sprk_rulesets";
    private const string PolicySet = "sprk_policies";
    private const string PolicyVersionSet = "sprk_policyversions";
    private const string OrganizationSet = FinanceAuthorizationFilter.VendorOrganizationEntitySet;
    private const string SystemUserSet = "systemusers";

    private readonly IGenericEntityService _entities;
    private readonly CallerRecordAccessProbe _probe;
    private readonly RuleBodyDescriber _describer;
    private readonly ILogger<RecordProcedureReadService> _logger;

    /// <summary>The clock for the default date ("today", UTC). A test seam; production uses the system clock.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    public RecordProcedureReadService(
        IGenericEntityService entities, CallerRecordAccessProbe probe, RuleBodyDescriber describer, ILogger<RecordProcedureReadService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _describer = describer ?? throw new ArgumentNullException(nameof(describer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Entry points
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The Procedures <paramref name="coreTable"/>(<paramref name="recordId"/>) is subject to (model 6.6, first list).</summary>
    public virtual async Task<RecordProcedureReadResult<RecordProceduresView>> ListProceduresAsync(
        string coreTable, Guid recordId, Guid? firmId, DateOnly? date, string? callerBearerToken, CancellationToken ct)
    {
        try
        {
            var reading = await ReadAsync(coreTable, recordId, firmId, date, probeFutureDate: true, ct).ConfigureAwait(false);
            if (reading is null)
            {
                return RecordProcedureReadResult<RecordProceduresView>.NotFound();
            }

            var view = BuildProcedures(reading);
            return await CallerCanReadAsync(reading, callerBearerToken, ct).ConfigureAwait(false)
                ? RecordProcedureReadResult<RecordProceduresView>.Of(view)
                : RecordProcedureReadResult<RecordProceduresView>.Unavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogUnavailable(ex, coreTable);
            return RecordProcedureReadResult<RecordProceduresView>.Unavailable();
        }
    }

    /// <summary>The rules in force on <paramref name="coreTable"/>(<paramref name="recordId"/>) (model 6.6, second list).</summary>
    public virtual async Task<RecordProcedureReadResult<RecordRulesInForceView>> ListRulesInForceAsync(
        string coreTable, Guid recordId, Guid? firmId, DateOnly? date, string? callerBearerToken, CancellationToken ct)
    {
        try
        {
            var reading = await ReadAsync(coreTable, recordId, firmId, date, probeFutureDate: false, ct).ConfigureAwait(false);
            if (reading is null)
            {
                return RecordProcedureReadResult<RecordRulesInForceView>.NotFound();
            }

            var view = await BuildRulesAsync(reading, ct).ConfigureAwait(false);
            return await CallerCanReadAsync(reading, callerBearerToken, ct).ConfigureAwait(false)
                ? RecordProcedureReadResult<RecordRulesInForceView>.Of(view)
                : RecordProcedureReadResult<RecordRulesInForceView>.Unavailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogUnavailable(ex, coreTable);
            return RecordProcedureReadResult<RecordRulesInForceView>.Unavailable();
        }
    }

    private void LogUnavailable(Exception ex, string coreTable)
    {
        // K2 (ProcedureResolutionFaultException): its message names row ids for operators, so it is logged and never returned.
        // The 503 is the same for every fault; the level is not: a fault the code expects (bad data, a Dataverse or HTTP fault) is a
        // warning, anything else is a defect an operator must see.
        if (IsExpectedFault(ex))
        {
            _logger.LogWarning(ex, "Record procedures: the answer for a {CoreTable} could not be built; 503 {ErrorCode}", coreTable, UnavailableErrorCode);
        }
        else
        {
            _logger.LogError(ex, "Record procedures: unexpected fault building the answer for a {CoreTable}; 503 {ErrorCode}", coreTable, UnavailableErrorCode);
        }
    }

    /// <summary>The faults this reader expects: the resolver's own (bad rows), a Dataverse service fault or an HTTP fault (rights or WhoAmI call).</summary>
    private static bool IsExpectedFault(Exception ex) =>
        ex is ProcedureResolutionFaultException or System.ServiceModel.FaultException or HttpRequestException;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // One reading: the rows (once), the resolution (once), the names
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private sealed class Reading
    {
        public required string CoreTable { get; init; }
        public required Guid RecordId { get; init; }
        public required DateOnly Date { get; init; }
        public required Guid? FirmId { get; init; }
        public required string FirmSource { get; init; }
        public required ProcedureRows Rows { get; init; }
        public required ProcedureRulesOnDate OnDate { get; init; }
        public ProcedureRulesOnDate? OnFutureDate { get; init; }
        public required IReadOnlyDictionary<Guid, string?> RecordNames { get; init; }
        public required IReadOnlyDictionary<Guid, string?> FirmNames { get; init; }
        public required IReadOnlyDictionary<Guid, int?> ProcedureTypes { get; init; }
        public required IReadOnlyDictionary<Guid, Guid?> RowCreators { get; init; }
        public required IReadOnlyDictionary<Guid, string?> UserNames { get; init; }

        /// <summary>Every row the answer exposes, as (entity set, id): what the caller must be able to Read (EB-D-218).</summary>
        public HashSet<(string Set, Guid Id)> Exposed { get; } = new();

        public Guid Expose(string set, Guid id)
        {
            Exposed.Add((set, id));
            return id;
        }
    }

    private async Task<Reading?> ReadAsync(
        string coreTable, Guid recordId, Guid? firmId, DateOnly? date, bool probeFutureDate, CancellationToken ct)
    {
        var kind = KindOf(coreTable);
        var core = (await ReadByIdsAsync(coreTable, [recordId], [NameColumnOf(coreTable), BillingFirmColumn], ct).ConfigureAwait(false))
            .FirstOrDefault();
        if (core is null)
        {
            return null;
        }

        var billingFirm = core.GetAttributeValue<EntityReference>(BillingFirmColumn)?.Id;
        var (firm, source) = firmId is { } chosen && chosen != Guid.Empty ? (chosen, QueryFirmSource)
            : billingFirm is { } own && own != Guid.Empty ? (own, "Record")
            : ((Guid?)null, "None");

        var rows = await ProcedureResolver.ReadRowsAsync(
            _entities, _logger, new CoreRecordReference(kind, recordId), firm ?? NoFirm, ct).ConfigureAwait(false);
        var resolution = new ProcedureResolution(rows);
        var workDate = date ?? DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        var onDate = resolution.RulesOn(workDate);

        // A Procedure whose first version opens after the date is neither applied nor a gap (F2-d), so the date's output does not list it.
        // The same resolution, asked for a date on which every Procedure that ever opened a window has opened, names every candidate.
        ProcedureRulesOnDate? onFuture = null;
        if (probeFutureDate)
        {
            var latestFrom = rows.Versions.Where(v => v.From is not null).Select(v => v.From!.Value).DefaultIfEmpty(workDate).Max();
            onFuture = latestFrom > workDate ? resolution.RulesOn(latestFrom) : onDate;
        }

        // The names and stamps the resolver's rows do not carry: one read per table, of ids the rows already hold.
        var recordNames = new Dictionary<Guid, string?> { [recordId] = NameOf(core, coreTable) };
        foreach (var group in rows.Chain.Where(l => l.Id != recordId).GroupBy(l => l.Table))
        {
            foreach (var row in await ReadByIdsAsync(group.Key, group.Select(l => l.Id), [NameColumnOf(group.Key)], ct).ConfigureAwait(false))
            {
                recordNames[row.Id] = NameOf(row, group.Key);
            }
        }

        var firmIds = rows.Procedures.Where(p => p.FirmId is not null).Select(p => p.FirmId!.Value)
            .Concat(rows.Overrides.Where(o => o.FirmId is not null).Select(o => o.FirmId!.Value))
            .Concat(rows.Sets.Where(s => s.FirmId is not null).Select(s => s.FirmId!.Value))
            .Concat(firm is { } f ? [f] : []);
        var firmNames = (await ReadByIdsAsync("sprk_organization", firmIds, ["sprk_organizationname"], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => (string?)r.GetAttributeValue<string>("sprk_organizationname"));

        var types = (await ReadByIdsAsync(
                ProcedureEntity.Entity, rows.Procedures.Select(p => p.Id), [ProcedureEntity.TypeColumn], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => r.GetAttributeValue<OptionSetValue>(ProcedureEntity.TypeColumn)?.Value);

        var creators = (await ReadByIdsAsync(
                ProcedureAssignmentEntity.Entity, rows.Overrides.Select(o => o.RowId), [CreatedByPersonColumn], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => r.GetAttributeValue<EntityReference>(CreatedByPersonColumn)?.Id);
        var userNames = (await ReadByIdsAsync("systemuser", creators.Values.OfType<Guid>(), ["fullname"], ct).ConfigureAwait(false))
            .ToDictionary(r => r.Id, r => (string?)r.GetAttributeValue<string>("fullname"));

        return new Reading
        {
            CoreTable = coreTable,
            RecordId = recordId,
            Date = workDate,
            FirmId = firm,
            FirmSource = source,
            Rows = rows,
            OnDate = onDate,
            OnFutureDate = onFuture,
            RecordNames = recordNames,
            FirmNames = firmNames,
            ProcedureTypes = types.ToDictionary(kv => kv.Key, kv => kv.Value),
            RowCreators = creators,
            UserNames = userNames,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The caller's Read on every row the answer exposes (EB-D-218)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ONE batched rights check, as <see cref="AffectedInvoiceCounter"/> asks it: the caller first (an unestablished caller gives every
    /// entry "unknown"), then <see cref="CallerRecordAccessProbe.GetCallerRightsForRecordsOrUnknownAsync"/> in pages. True only when every
    /// row was answered and carries Read. "Dataverse said nothing" is a refusal here, never a quietly shorter answer.
    /// </summary>
    private async Task<bool> CallerCanReadAsync(Reading reading, string? callerBearerToken, CancellationToken ct)
    {
        // The route's own record is always part of what is exposed, so the set is never empty and the caller is always established.
        reading.Expose(SetOf(reading.CoreTable), reading.RecordId);
        if (await _probe.GetCallerSystemUserIdAsync(callerBearerToken, ct).ConfigureAwait(false) is not { } caller || caller == Guid.Empty)
        {
            _logger.LogWarning("Record procedures: the caller could not be established; no list is returned");
            return false;
        }

        foreach (var page in reading.Exposed.OrderBy(t => t.Set, StringComparer.Ordinal).ThenBy(t => t.Id).Chunk(RightsPageSize))
        {
            var rights = await _probe.GetCallerRightsForRecordsOrUnknownAsync(callerBearerToken, page.ToList(), ct).ConfigureAwait(false);
            if (rights is null || rights.Count != page.Length || rights.Any(r => r is null))
            {
                _logger.LogWarning("Record procedures: the caller's rights on {Count} row(s) were not answered; no list is returned", page.Length);
                return false;
            }

            if (rights.Any(r => (r!.Value & AccessRights.Read) != AccessRights.Read))
            {
                // Which row it was is not logged with an id: the caller may not be allowed to know it exists (K2).
                _logger.LogInformation("Record procedures: the caller cannot Read a row the answer would expose; no list is returned");
                return false;
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The Procedures list
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private RecordProceduresView BuildProcedures(Reading r)
    {
        var onDate = r.OnDate;
        var applied = onDate.Procedures.ToDictionary(p => p.ProcedureId);
        var gaps = onDate.Gaps.ToDictionary(g => g.ProcedureId);
        var excluded = onDate.ExcludedProcedures.ToDictionary(e => e.ProcedureId);
        var candidates = (r.OnFutureDate ?? onDate).Procedures.Select(p => p.ProcedureId)
            .Concat((r.OnFutureDate ?? onDate).Gaps.Select(g => g.ProcedureId))
            .Concat(excluded.Keys)
            .Concat(applied.Keys).Concat(gaps.Keys)
            .Distinct().ToList();

        var items = new List<ProcedureListItem>();
        foreach (var id in candidates)
        {
            var procedure = r.Rows.Procedures.First(p => p.Id == id);
            var status = excluded.ContainsKey(id) ? "Excluded"
                : applied.ContainsKey(id) ? "InForce"
                : gaps.ContainsKey(id) ? "Gap"
                : "NotYetInForce";
            var decisive = applied.TryGetValue(id, out var a) ? a.Overrides
                : gaps.TryGetValue(id, out var g) ? g.Overrides
                : excluded.TryGetValue(id, out var e) ? e.Overrides
                : NearestRows(r.Rows.Overrides, id);
            items.Add(BuildProcedure(r, procedure, status, applied.GetValueOrDefault(id), decisive));
        }

        var ordered = items
            .OrderBy(i => StatusOrder(i.Status))
            .ThenByDescending(i => Enum.Parse<ProcedureLevel>(i.Level))
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .ThenBy(i => i.Id)
            .ToList();
        var gapViews = onDate.Gaps.Select(g => BuildGap(r, g)).ToList();
        return new RecordProceduresView(
            RecordView(r, r.RecordId), r.Date, ResolvedFirmOf(r), r.FirmSource, ordered, gapViews);
    }

    private static int StatusOrder(string status) => status switch { "InForce" => 0, "Gap" => 1, "NotYetInForce" => 2, _ => 3 };

    /// <summary>The Procedure-level rows at the nearest distance: the rows that decide, for a Procedure the resolver does not report (not yet in force).</summary>
    private static IReadOnlyList<ProcedureOverrideRow> NearestRows(IReadOnlyList<ProcedureOverrideRow> rows, Guid procedureId)
    {
        var own = rows.Where(o => o.ProcedureId == procedureId).ToList();
        return own.Count == 0 ? [] : own.Where(o => o.Distance == own.Min(x => x.Distance)).ToList();
    }

    private ProcedureListItem BuildProcedure(
        Reading r, ProcedureRow procedure, string status, AppliedProcedure? applied, IReadOnlyList<ProcedureOverrideRow> decisive)
    {
        r.Expose(ProcedureEntity.EntitySet, procedure.Id);
        ExposeVersionsOf(r, procedure.Id);
        var sources = SourcesOf(r, procedure);
        var level = sources.Count == 0 ? ProcedureLevel.AllRecords : LevelOf(sources[0]);

        ProcedureVersionView2? versionInForce = null;
        ProcedureVersionView2? newer = null;
        if (applied is not null)
        {
            var chosen = r.Rows.Versions.FirstOrDefault(v => v.Id == applied.ProcedureVersionId);
            versionInForce = VersionView(r, chosen);
            newer = applied.Pinned ? NewerThan(r, procedure.Id, applied.ProcedureVersionNumber) : null;
        }

        var decisiveIds = decisive.Select(d => d.RowId).ToHashSet();
        var overrides = r.Rows.Overrides.Where(o => o.ProcedureId == procedure.Id)
            .OrderBy(o => o.Distance).ThenBy(o => o.RowId)
            .Select(o => OverrideView(r, o, decisiveIds.Contains(o.RowId)))
            .ToList();
        return new ProcedureListItem(
            procedure.Id, procedure.Name, "Billing", TypeName(r, procedure.Id), procedure.AppliesTo.ToString(), FirmOf(r, procedure.FirmId, FirmOrigin.Lookup),
            status, level.ToString(), sources, versionInForce, applied?.Pinned ?? false, newer, overrides);
    }

    private static ProcedureLevel LevelOf(ProcedureSourceView source) => Enum.Parse<ProcedureLevel>(source.Kind);

    private static string? TypeName(Reading r, Guid procedureId) =>
        r.ProcedureTypes.GetValueOrDefault(procedureId) is { } value ? Enum.IsDefined((ProcedureType)value) ? ((ProcedureType)value).ToString() : value.ToString() : null;

    /// <summary>Every source of <paramref name="procedure"/> on the record, highest first, the highest marked winning (6.1, 6.5).</summary>
    private List<ProcedureSourceView> SourcesOf(Reading r, ProcedureRow procedure)
    {
        var found = new List<(ProcedureLevel Level, int Distance, ProcedureSourceView View)>();
        if (procedure.AppliesTo == ProcedureAppliesTo.AllRecords)
        {
            found.Add((ProcedureLevel.AllRecords, 0, new ProcedureSourceView(nameof(ProcedureLevel.AllRecords), false, 0, null, null, null)));
        }

        if (procedure.AppliesTo == ProcedureAppliesTo.Firm && procedure.FirmId == r.Rows.FirmId)
        {
            found.Add((ProcedureLevel.Firm, 0, new ProcedureSourceView(nameof(ProcedureLevel.Firm), false, 0, null, null, null)));
        }

        foreach (var row in r.Rows.Overrides.Where(o => o.ProcedureId == procedure.Id && o.Mode != ProcedureOverrideMode.Excluded))
        {
            var level = row.Distance == 0 ? ProcedureLevel.ManualOwn : ProcedureLevel.ManualParent;
            found.Add((level, row.Distance, new ProcedureSourceView(
                level.ToString(), false, row.Distance, row.Distance == 0 ? null : ChainRecordOf(r, row.ChainRecord), row.Mode.ToString(),
                r.Expose(ProcedureAssignmentEntity.EntitySet, row.RowId))));
        }

        var ordered = found.OrderByDescending(x => x.Level).ThenBy(x => x.Distance).ThenBy(x => x.View.RowId).ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var top = ordered[0];
        return ordered.Select(x => x.View with { Winning = x.Level == top.Level && x.Distance == top.Distance }).ToList();
    }

    private ProcedureOverrideView OverrideView(Reading r, ProcedureOverrideRow row, bool decisive)
    {
        ProcedureVersionView2? pinned = null;
        ProcedureVersionView2? newer = null;
        if (row.Mode == ProcedureOverrideMode.Pinned && r.Rows.Versions.FirstOrDefault(v => v.Id == row.PinnedVersionId) is { } pin
            && pin.ProcedureId == row.ProcedureId)
        {
            pinned = VersionView(r, pin);
            newer = NewerThan(r, pin.ProcedureId, pin.Number);
        }

        r.Expose(ProcedureAssignmentEntity.EntitySet, row.RowId);
        return new ProcedureOverrideView(
            row.RowId, row.Mode.ToString(), decisive, row.Distance, ChainRecordOf(r, row.ChainRecord), row.Reason, pinned, newer, StampOf(r, row));
    }

    /// <summary>
    /// Exposes EVERY version of a Procedure the answer shows (fix round 3, F-B). What the answer says about a Procedure derives from its whole
    /// version set, not from the version it picked: <c>NotYetInForce</c>, the gap kinds <c>HoleBetweenVersions</c> and <c>AfterLastVersion</c>,
    /// an absent <c>NewerVersion</c> and the version chosen for a date are each decided by versions that are not shown.
    /// </summary>
    private static void ExposeVersionsOf(Reading r, Guid procedureId)
    {
        foreach (var version in r.Rows.Versions.Where(v => v.ProcedureId == procedureId))
        {
            r.Expose(ProcedureEntity.VersionEntitySet, version.Id);
        }
    }

    /// <summary>The newest version in force (Active or Superseded, with a window) that is newer than version <paramref name="number"/>, or null: what a pin skips.</summary>
    private ProcedureVersionView2? NewerThan(Reading r, Guid procedureId, int number) =>
        VersionView(r, r.Rows.Versions
            .Where(v => v.ProcedureId == procedureId && v.IsInForce && v.Number > number)
            .OrderByDescending(v => v.Number).FirstOrDefault());

    private ProcedureVersionView2? VersionView(Reading r, ProcedureVersionRow? version)
    {
        if (version is null)
        {
            return null;
        }

        r.Expose(ProcedureEntity.VersionEntitySet, version.Id);
        return new ProcedureVersionView2(version.Id, version.Number, VersionStatusName(version.Status), version.From, version.To);
    }

    private static string VersionStatusName(int status) => status switch
    {
        ProcedureEntity.VersionStatusDraft => "Draft",
        ProcedureEntity.VersionStatusActive => "Active",
        ProcedureEntity.VersionStatusSuperseded => "Superseded",
        ProcedureEntity.VersionStatusRetired => "Retired",
        _ => status.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private ProcedureGapView BuildGap(Reading r, ProcedureGap gap)
    {
        r.Expose(ProcedureEntity.EntitySet, gap.ProcedureId);
        ExposeVersionsOf(r, gap.ProcedureId);
        var decisiveIds = gap.Overrides.Select(o => o.RowId).ToHashSet();
        return new ProcedureGapView(
            gap.ProcedureId, gap.ProcedureName, gap.Kind.ToString(), gap.WorkDate, GapText(gap),
            gap.Overrides.OrderBy(o => o.Distance).ThenBy(o => o.RowId).Select(o => OverrideView(r, o, decisiveIds.Contains(o.RowId))).ToList());
    }

    private static string GapText(ProcedureGap gap) => gap.Kind switch
    {
        ProcedureGapKind.HoleBetweenVersions => $"No version of this Procedure is in force on {gap.WorkDate:yyyy-MM-dd}: the date falls between two versions.",
        ProcedureGapKind.AfterLastVersion => $"No version of this Procedure is in force on {gap.WorkDate:yyyy-MM-dd}: its last version has ended.",
        ProcedureGapKind.PinnedVersionRetired => "The pinned version of this Procedure is retired.",
        ProcedureGapKind.PinnedVersionNotInForce => "The pinned version of this Procedure is not in force.",
        ProcedureGapKind.ConflictingParents => "The records this one is filed under choose different versions of this Procedure.",
        _ => "No version of this Procedure can be chosen.",
    };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The rules in force
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private async Task<RecordRulesInForceView> BuildRulesAsync(Reading r, CancellationToken ct)
    {
        var onDate = r.OnDate;
        var views = new List<RuleInForceView>();

        // The rules that run, one for one, in the resolver's order.
        foreach (var rule in onDate.Rules)
        {
            views.Add(await InForceRuleAsync(r, rule, ct).ConfigureAwait(false));
        }

        // The rules a plain Exclude removed (a changed rule's base is shown by its record rule's "changed" mark).
        foreach (var excluded in onDate.ExcludedRules.Where(e => !e.ReplacedByRecordRule))
        {
            views.Add(await ExcludedRuleAsync(r, excluded, ct).ConfigureAwait(false));
        }

        // Record rules whose base rule is not in force on the date: the resolver gives them no slot, so they do not run.
        views.AddRange(await BaseRetiredRulesAsync(r, ct).ConfigureAwait(false));

        // What is absent is derived from rows too: a Procedure a Procedure-level Exclude removed lists none of its rules, and a base rule a record
        // rule replaced is listed only through the record rule's mark. The rows that decided that are part of what the caller must be able to Read.
        foreach (var excludedProcedure in onDate.ExcludedProcedures)
        {
            ExposeOverrides(r, excludedProcedure.Overrides);
        }

        foreach (var replaced in onDate.ExcludedRules.Where(e => e.ReplacedByRecordRule))
        {
            ExposeOverrides(r, replaced.Overrides);
        }

        return new RecordRulesInForceView(
            RecordView(r, r.RecordId), r.Date, ResolvedFirmOf(r), r.FirmSource, views, onDate.Gaps.Select(g => BuildGap(r, g)).ToList());
    }

    /// <summary>Exposes override rows that decided something the answer shows (or omits), and the chain record each sits on.</summary>
    private void ExposeOverrides(Reading r, IEnumerable<ProcedureOverrideRow> rows)
    {
        foreach (var row in rows)
        {
            r.Expose(ProcedureAssignmentEntity.EntitySet, row.RowId);
            ChainRecordOf(r, row.ChainRecord);
        }
    }

    private async Task<RuleInForceView> InForceRuleAsync(Reading r, ResolvedRule rule, CancellationToken ct)
    {
        var marks = new List<RuleMarkView>();
        if (rule.RecordRule is { } slot)
        {
            var basedOn = PolicyVersionRef(r, rule.Version.BasedOnVersionId);
            var current = PolicyVersionRef(r, slot.BasePolicyVersionId);
            var baseVersion = r.Rows.PolicyVersions.FirstOrDefault(v => v.PolicyVersionId == slot.BasePolicyVersionId);
            var exclude = slot.Exclude;
            r.Expose(PolicySet, slot.BasePolicyId);
            marks.Add(new RuleMarkView(
                "Changed", exclude?.Reason, FirmOf(r, exclude?.FirmId, FirmOrigin.Lookup), exclude?.Distance ?? slot.ChainRecord.Distance, ChainRecordOf(r, slot.ChainRecord),
                exclude is null ? null : StampOf(r, exclude), exclude?.RowId, rule.Version.PolicyId, basedOn, current, slot.BasePolicyCode,
                baseVersion is null ? null : await SummaryAsync(r, baseVersion, ct).ConfigureAwait(false)));
            if (exclude is not null)
            {
                r.Expose(ProcedureAssignmentEntity.EntitySet, exclude.RowId);
            }

            if (basedOn is null || current is null || basedOn.Id != current.Id)
            {
                marks.Add(new RuleMarkView("Stale", RecordRuleId: rule.Version.PolicyId, BasedOn: basedOn, CurrentBase: current));
            }
        }

        return await RuleViewAsync(
            r, rule.Version, rule.Procedure, ProcedureRulesOnDate.ReaderCodeOf(rule, _logger), inForce: true, rule.RecordRule is not null, marks, ct).ConfigureAwait(false);
    }

    private async Task<RuleInForceView> ExcludedRuleAsync(Reading r, ExcludedRule excluded, CancellationToken ct)
    {
        var marks = excluded.Overrides.OrderBy(o => o.Distance).ThenBy(o => o.RowId).Select(o =>
        {
            r.Expose(ProcedureAssignmentEntity.EntitySet, o.RowId);
            return new RuleMarkView(
                "Excluded", o.Reason, FirmOf(r, o.FirmId, FirmOrigin.Lookup), o.Distance, ChainRecordOf(r, o.ChainRecord), StampOf(r, o), o.RowId);
        }).ToList();
        return await RuleViewAsync(
            r, excluded.Version, excluded.Procedure, excluded.Version.PolicyCode, inForce: false, recordRule: false, marks, ct).ConfigureAwait(false);
    }

    private async Task<RuleInForceView> RuleViewAsync(
        Reading r, ResolvedPolicyVersion version, AppliedProcedure applied, string code, bool inForce, bool recordRule,
        IReadOnlyList<RuleMarkView> marks, CancellationToken ct)
    {
        var set = r.Rows.Sets.FirstOrDefault(s => s.Id == version.RuleSetId);
        var procedure = r.Rows.Procedures.FirstOrDefault(p => p.Id == applied.ProcedureId);
        var firm = set?.Record is not null ? set.FirmId : procedure?.FirmId;
        r.Expose(PolicySet, version.PolicyId);
        r.Expose(PolicyVersionSet, version.PolicyVersionId);
        r.Expose(RuleSetSet, version.RuleSetId);
        r.Expose(ProcedureEntity.EntitySet, applied.ProcedureId);
        ExposeVersionsOf(r, applied.ProcedureId);
        // Pinned, Level and Distance below derive from the rows that decided the Procedure (EB-D-218: the caller must be able to Read them).
        ExposeOverrides(r, applied.Overrides);
        var procedureVersion = r.Rows.Versions.FirstOrDefault(v => v.Id == applied.ProcedureVersionId);
        return new RuleInForceView(
            version.PolicyId, version.PolicyVersionId,
            code, version.ShortHeadline, RuleTypeName(version.RuleType), await SummaryAsync(r, version, ct).ConfigureAwait(false), version.VersionNumber,
            version.InForceFrom, inForce, recordRule, version.RuleSetId,
            new RuleProcedureView(applied.ProcedureId, applied.ProcedureName, VersionView(r, procedureVersion), applied.Pinned),
            applied.Rank.Level.ToString(), applied.Rank.Distance, FirmOf(r, firm, FirmOrigin.Lookup), marks);
    }

    /// <summary>
    /// Record rules on the chain whose base rule is NOT among the rules the resolver resolved for the date (its Procedure retired, the rule
    /// dropped by the version in force, or the Procedure not applicable): the resolver gives such a record rule no slot, so it does not run
    /// (089c K1 / K3, ontology-r1). Found from the rows already read: a rule-level Exclude row whose derived record rule exists in a
    /// record-scoped set on the chain. The base rule's code and the version the record rule was based on need two small reads of ids those
    /// rows name.
    /// </summary>
    private async Task<List<RuleInForceView>> BaseRetiredRulesAsync(Reading r, CancellationToken ct)
    {
        var resolvedBases = r.OnDate.Rules.Select(x => x.RecordRule?.BasePolicyId ?? x.Version.PolicyId)
            .Concat(r.OnDate.ExcludedRules.Select(x => x.Version.PolicyId)).ToHashSet();
        var orphans = new List<(ProcedureOverrideRow Exclude, Guid RecordRuleId, Guid SetId, PolicyRow Rule)>();
        foreach (var row in r.Rows.Overrides.Where(o => o.PolicyId is { } id && !resolvedBases.Contains(id) && ProcedureResolver.IsRuleOverrideFor(o, id, r.Rows.FirmId)))
        {
            var setId = ProcedureAssignmentService.RecordRuleSetIdFor(row.ChainRecord.Table, row.ChainRecord.Id, row.FirmId);
            var recordRuleId = ProcedureAssignmentService.RecordRulePolicyIdFor(setId, row.PolicyId!.Value);
            if (r.Rows.Policies.FirstOrDefault(p => p.Id == recordRuleId && p.RuleSetId == setId) is { } rule
                && r.Rows.PolicyVersions.Any(v => v.PolicyId == recordRuleId))
            {
                orphans.Add((row, recordRuleId, setId, rule));
            }
        }

        if (orphans.Count == 0)
        {
            return [];
        }

        var baseCodes = (await ReadByIdsAsync(BillingFactLoader.PolicyEntity, orphans.Select(o => o.Exclude.PolicyId!.Value), ["sprk_policycode"], ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.GetAttributeValue<string>("sprk_policycode"));
        var latest = orphans.ToDictionary(o => o.RecordRuleId, o => CurrentEpisodeVersion(r.Rows.PolicyVersions, r.Date, o.RecordRuleId, o.Exclude));
        var basedOnIds = latest.Values.Where(v => v.BasedOnVersionId is not null).Select(v => v.BasedOnVersionId!.Value);
        var basedOnNumbers = (await ReadByIdsAsync(BillingFactLoader.VersionEntity, basedOnIds, ["sprk_versionnumber"], ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => x.GetAttributeValue<int?>("sprk_versionnumber") ?? 0);

        var views = new List<RuleInForceView>();
        foreach (var (exclude, recordRuleId, setId, rule) in orphans.OrderBy(o => o.RecordRuleId))
        {
            var basePolicy = exclude.PolicyId!.Value;
            var version = latest[recordRuleId];
            // basedOn (shown in both marks) is read from this version row, so the row itself is part of what the caller must be able to Read (fix round 3, F-A).
            r.Expose(PolicyVersionSet, version.PolicyVersionId);
            PolicyVersionRefView? basedOn = version.BasedOnVersionId is { } b && basedOnNumbers.TryGetValue(b, out var number)
                ? new PolicyVersionRefView(r.Expose(PolicyVersionSet, b), number) : null;
            // The base rule's own catalogue code (fail closed to blank if it is missing or itself a record-rule code), as a reader sees a record rule.
            var code = ProcedureRulesOnDate.ReaderCodeOf(ProcedureAssignmentService.RecordRuleCodePrefix, baseCodes.GetValueOrDefault(basePolicy), _logger);
            r.Expose(PolicySet, basePolicy);
            r.Expose(PolicySet, recordRuleId);
            r.Expose(RuleSetSet, setId);
            r.Expose(ProcedureAssignmentEntity.EntitySet, exclude.RowId);
            var stamp = StampOf(r, exclude);
            var chainRecord = ChainRecordOf(r, exclude.ChainRecord);
            var firmView = FirmOf(r, exclude.FirmId, FirmOrigin.Lookup);
            views.Add(new RuleInForceView(
                recordRuleId, null, code, null, null, null, null, null, InForce: false, RecordRule: true, setId, null, null, null, firmView,
                [
                    new RuleMarkView("Changed", exclude.Reason, firmView, exclude.Distance, chainRecord, stamp, exclude.RowId, recordRuleId, basedOn, null, code),
                    new RuleMarkView("BaseRetired", RecordRuleId: recordRuleId, BasedOn: basedOn, BaseCode: code),
                ]));
        }

        return views;
    }

    /// <summary>
    /// The version of a record rule the resolver would count, as <c>ProcedureResolution.RecordRuleVersionOn</c> counts it (fix round 3, K2): only the
    /// versions of the CURRENT episode, by the resolver's own filter (<see cref="ProcedureResolution.CurrentEpisodeVersions"/>, 084e: one
    /// implementation), then the latest in force on the date (effective-from, then number, then id).
    /// A record rule keeps its policy across a removal and a new change, so an older episode's version must never be shown. When the change is
    /// not yet in force on the date (no version of the episode is), its first version names what it was based on. A known boundary that no
    /// version reaches is the resolver's own fault state and is a fault here too (503, never a guess).
    /// </summary>
    internal static ResolvedPolicyVersion CurrentEpisodeVersion(IReadOnlyList<ResolvedPolicyVersion> versions, DateOnly date, Guid recordRuleId, ProcedureOverrideRow exclude)
    {
        var all = versions.Where(v => v.PolicyId == recordRuleId).ToList();
        var episode = ProcedureResolution.CurrentEpisodeVersions(recordRuleId, all, exclude);
        if (episode.Count == 0)
        {
            throw new ProcedureResolutionFaultException(
                $"Record rule {recordRuleId} has {all.Count} Published version(s) but none was created on or after its change's Exclude row {exclude.RowId}.");
        }

        return episode.Where(v => ProcedureResolution.InForceOn(v.InForceFrom, date))
                   .OrderByDescending(v => v.InForceFrom).ThenByDescending(v => v.VersionNumber).ThenByDescending(v => v.PolicyVersionId)
                   .FirstOrDefault()
               ?? episode.OrderBy(v => v.InForceFrom).ThenBy(v => v.VersionNumber).ThenBy(v => v.PolicyVersionId).First();
    }

    private PolicyVersionRefView? PolicyVersionRef(Reading r, Guid? versionId)
    {
        if (versionId is not { } id || r.Rows.PolicyVersions.FirstOrDefault(v => v.PolicyVersionId == id) is not { } version)
        {
            return null;
        }

        r.Expose(PolicyVersionSet, id);
        return new PolicyVersionRefView(id, version.VersionNumber);
    }

    private async Task<string?> SummaryAsync(Reading r, ResolvedPolicyVersion version, CancellationToken ct)
    {
        r.Expose(PolicyVersionSet, version.PolicyVersionId);
        var result = await _describer.DescribeAsync(version.RuleBody, ct).ConfigureAwait(false);
        return result.Description is { } description ? string.Join(" ", description.Clauses.Select(c => c.Text)) : null;
    }

    private static string? RuleTypeName(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
        && Enum.IsDefined((RuleType)number) ? ((RuleType)number).ToString() : value;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Views of shared parts (each records what it exposes)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static ChainRecordView RecordView(Reading r, Guid id) =>
        new(r.CoreTable, id, r.RecordNames.GetValueOrDefault(id), 0);

    private ChainRecordView ChainRecordOf(Reading r, CoreRecordLink link)
    {
        r.Expose(SetOf(link.Table), link.Id);
        return new ChainRecordView(link.Table, link.Id, r.RecordNames.GetValueOrDefault(link.Id), link.Distance);
    }

    /// <summary>Where a firm id in the answer came from (EB-D-218, fix round 3 F-D): this decides whether the caller's Read on the firm is asked.</summary>
    private enum FirmOrigin
    {
        /// <summary>A lookup value on a row that is already in the checked set (the record's billing firm, <c>sprk_firm</c> on a Procedure, an override row or a rule set): its id and name come with that row's own read, so the organization is not asked.</summary>
        Lookup,

        /// <summary>The caller's <c>?firmId=</c>: nothing else vouches for it, so asking about the organization is the only thing that stops the parameter being a firm-existence and firm-name oracle.</summary>
        Query,
    }

    /// <summary>The route's own firm: asked when the caller named it (<see cref="FirmOrigin.Query"/>), not when it is the record's billing firm.</summary>
    private FirmView? ResolvedFirmOf(Reading r) =>
        FirmOf(r, r.FirmId, r.FirmSource == QueryFirmSource ? FirmOrigin.Query : FirmOrigin.Lookup);

    private FirmView? FirmOf(Reading r, Guid? id, FirmOrigin origin)
    {
        if (id is not { } firm || firm == Guid.Empty)
        {
            return null;
        }

        if (origin == FirmOrigin.Query)
        {
            r.Expose(OrganizationSet, firm);
        }

        return new FirmView(firm, r.FirmNames.GetValueOrDefault(firm));
    }

    private StampView StampOf(Reading r, ProcedureOverrideRow row)
    {
        var by = r.RowCreators.GetValueOrDefault(row.RowId);
        if (by is { } person)
        {
            r.Expose(SystemUserSet, person);
        }

        return new StampView(by, by is { } id ? r.UserNames.GetValueOrDefault(id) : null, row.CreatedOn);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Reads and table names
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The rows of <paramref name="entity"/> with these ids, app-only, ids chunked and every query paged. A fault throws (the answer is then unavailable).</summary>
    private async Task<List<Entity>> ReadByIdsAsync(string entity, IEnumerable<Guid> ids, string[] columns, CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var chunk in ids.Where(id => id != Guid.Empty).Distinct().Order().Chunk(MaxInValues))
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(columns),
                NoLock = true,
                PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
            };
            query.Criteria.AddCondition(entity + "id", ConditionOperator.In, chunk.Cast<object>().ToArray());
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                rows.AddRange(page.Entities.Where(e => chunk.Contains(e.Id)));
                if (!page.MoreRecords)
                {
                    break;
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        return rows;
    }

    private static CoreRecordKind KindOf(string coreTable) => coreTable switch
    {
        SecureRootInheritance.Matter => CoreRecordKind.Matter,
        SecureRootInheritance.Project => CoreRecordKind.Project,
        SecureRootInheritance.WorkAssignment => CoreRecordKind.WorkAssignment,
        _ => throw new ArgumentOutOfRangeException(nameof(coreTable), coreTable, "Not a core record table."),
    };

    private static string SetOf(string table) => table switch
    {
        SecureRootInheritance.Matter => FinanceAuthorizationFilter.MatterEntitySet,
        SecureRootInheritance.Project => FinanceAuthorizationFilter.ProjectEntitySet,
        SecureRootInheritance.WorkAssignment => FinanceAuthorizationFilter.WorkAssignmentEntitySet,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a core record table."),
    };

    // The name column of each core table (the same three SecureRecordRoot uses): a matter has no sprk_name.
    private static string NameColumnOf(string table) => table switch
    {
        SecureRootInheritance.Matter => "sprk_mattername",
        SecureRootInheritance.Project => "sprk_projectname",
        _ => "sprk_name",
    };

    private static string? NameOf(Entity row, string table) => row.GetAttributeValue<string>(NameColumnOf(table));
}
