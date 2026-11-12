// unified-access-control-r2 task 191c (#2179 design v2 §3.4, §3.7, §3.8; owner answers 2026-11-11 Q3 = a, Q5 = a, Q7).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureChildShareSynchronizer keeps SECURE children's shares equal to their root's; RestrictedExternalShareRemover
//       takes flagged users' per-user shares off a Restricted root; DirectMessageParticipantShares keeps Direct messages' participant
//       shares. None of them shares an ORDINARY row with a team, and none decides a row by ExternalVisibility (task 191b).
//   (2) Extension — It is the synchronizer's "External-team mode" (design §3.4): the same POA seam (IDataverseRecordShareService),
//       the same read-the-difference / write-the-difference / read-back / carry shape, the same Secure-team rule
//       (SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync), the same lineage registry (SecureChildLineage) and the same
//       predicate (ExternalVisibility). It is a sibling class rather than more members of the 2,600-line synchronizer because it
//       shares none of the synchronizer's per-run state (secure roots, root mirrors, the No Access wall): the synchronizer takes it
//       as an optional collaborator for its flagged-principal filter, and the existing jobs and L1 hooks call it.
//   (3) Cost of doing nothing — once flagged users move into the External Collaborators unit (task 191e) they read nothing but
//       their per-user roots; and without the revoke side, a row that turns internal-only, Restricted or secure stays shared with
//       them, and per-user shares a form Share made on hidden rows stay.
//
// Placement (bff-extensions.md; ADR-052): BFF domain code, one scoped registration in ExternalAccessModule beside the
// predicate's decider, no package, no endpoint, no new job (ADR-036: it runs in SecureChildShareReconciliationJob, inline from
// the existing L1 writers, ADR-002: no plugin).

using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// uac-r2 task 191c: <see cref="ExternalVisibility.DecideAsync"/> over the BFF's app-only readers (bound in
/// <c>AddExternalAccess</c>). Never throws for a read fault: every row of a faulted decision is Undecided.
/// </summary>
internal delegate Task<ExternalVisibilityResult> ExternalVisibilityDecider(
    IReadOnlyCollection<(string Table, Guid Id)> rows, CancellationToken ct);

/// <summary>How one External-team pass ended.</summary>
public enum ExternalTeamShareStatus
{
    /// <summary>Every row asked about was decided and (with writes on) brought into line.</summary>
    Completed = 0,

    /// <summary>Some rows were carried: a read or write failed, or the predicate could not decide them.</summary>
    Incomplete = 1,

    /// <summary>Nothing to do (no row of a lineage table was asked about).</summary>
    NotApplicable = 2,

    /// <summary>A prerequisite could not be read (the Secure team, the External team); nothing was written.</summary>
    Failed = 3,
}

/// <summary>One table's plan in an External-team pass: what it would write (writes off) or wrote (writes on).</summary>
/// <param name="Table">The table's logical name.</param>
/// <param name="TeamGrants">External-team shares to grant (Read + AppendTo, or Read).</param>
/// <param name="TeamChanges">External-team shares whose mask is not exactly the table's.</param>
/// <param name="TeamRevokes">External-team shares to revoke (the row is hidden, undecided or in a secure family).</param>
/// <param name="FlaggedUserRevokes">Per-user shares of flagged users on hidden or undecided rows to revoke.</param>
/// <param name="Reowns">Rows owned by a flagged user to re-own to their I-6 team.</param>
/// <param name="GrantRows">Up to <see cref="ExternalTeamShareSynchronizer.ReportIdsPerTable"/> row ids planned for a grant.</param>
/// <param name="RevokeRows">Up to <see cref="ExternalTeamShareSynchronizer.ReportIdsPerTable"/> row ids planned for a revoke.</param>
public sealed record ExternalTeamShareTableReport(
    string Table, int TeamGrants, int TeamChanges, int TeamRevokes, int FlaggedUserRevokes, int Reowns,
    IReadOnlyList<Guid> GrantRows, IReadOnlyList<Guid> RevokeRows);

/// <summary>What an External-team pass found and did.</summary>
public sealed record ExternalTeamShareResult(ExternalTeamShareStatus Status, string? Detail)
{
    /// <summary>Whether the share writes were on (<see cref="ExternalTeamShareSynchronizer.WritesEnabledConfigKey"/>).</summary>
    public bool WritesEnabled { get; init; }

    /// <summary>Whether the re-own writes were on (<see cref="ExternalTeamShareSynchronizer.ReownWritesEnabledConfigKey"/>).</summary>
    public bool ReownWritesEnabled { get; init; }

    /// <summary>Rows decided (rows that no longer exist are not counted).</summary>
    public int Examined { get; init; }

    /// <summary>Rows the predicate said Visible and outside every secure family.</summary>
    public int Shareable { get; init; }

    /// <summary>Rows the predicate could not decide (no grant; an existing team or flagged-user share revoked; carried).</summary>
    public int Undecided { get; init; }

    /// <summary>Rows in a secure family (Secure-team-owned, or under a secure or unreadable root): never team-shared.</summary>
    public int SecureFamily { get; init; }

    /// <summary>Share writes made (grants, changes, revokes) and read back.</summary>
    public int Written { get; init; }

    /// <summary>Owner writes made and read back.</summary>
    public int Reowned { get; init; }

    /// <summary>Rows whose owner is the External Collaborators team itself: the census alarm (that team must never own a row).</summary>
    public int ExternalTeamOwned { get; init; }

    /// <summary>Flagged-user revokes withheld because the Direct-message owner team could not be told (the rows are carried).</summary>
    public int FlaggedRevokesWithheld { get; init; }

    /// <summary>Flagged-user shares left on rows owned by the Direct-message owner team (task 198's writer owns them; reported).</summary>
    public int DirectOwnedFlaggedShares { get; init; }

    /// <summary>Per table, the plan (sorted by table name).</summary>
    public IReadOnlyList<ExternalTeamShareTableReport> Tables { get; init; } = [];

    /// <summary>Rows to look at again next run: a read or write failed, or the predicate could not decide them.</summary>
    public IReadOnlyList<(string Table, Guid Id)> Carried { get; init; } = [];

    /// <summary>Changed rows whose family walk stopped short (too many rows, or a read fault): walked again next run (F2).</summary>
    public IReadOnlyList<(string Table, Guid Id)> WalkAgain { get; init; } = [];

    /// <summary>Rows carried for a FAULT (a read, write or read-back failed, a flagged-user revoke was withheld, a re-own was
    /// refused) rather than for an Undecided answer: the job is not successful while any remain (pass-2 F2).</summary>
    public int Failures { get; init; }

    /// <summary>One result from the passes of one call (disjoint rows): the worst status, the sums, the tables merged.</summary>
    internal static ExternalTeamShareResult Combine(IReadOnlyList<ExternalTeamShareResult> parts)
    {
        if (parts.Count == 0)
            return Nothing;
        if (parts.Count == 1)
            return parts[0];

        var status = parts.Any(p => p.Status == ExternalTeamShareStatus.Failed) ? ExternalTeamShareStatus.Failed
            : parts.Any(p => p.Status == ExternalTeamShareStatus.Incomplete) ? ExternalTeamShareStatus.Incomplete
            : parts.Any(p => p.Status == ExternalTeamShareStatus.Completed) ? ExternalTeamShareStatus.Completed
            : ExternalTeamShareStatus.NotApplicable;
        var details = parts.Select(p => p.Detail).Where(d => d is not null && IsReal(d)).Distinct().ToList();
        return new ExternalTeamShareResult(status, details.Count == 0 ? null : string.Join("; ", details))
        {
            WritesEnabled = parts.Any(p => p.WritesEnabled),
            ReownWritesEnabled = parts.Any(p => p.ReownWritesEnabled),
            Examined = parts.Sum(p => p.Examined),
            Shareable = parts.Sum(p => p.Shareable),
            Undecided = parts.Sum(p => p.Undecided),
            SecureFamily = parts.Sum(p => p.SecureFamily),
            Written = parts.Sum(p => p.Written),
            Reowned = parts.Sum(p => p.Reowned),
            ExternalTeamOwned = parts.Sum(p => p.ExternalTeamOwned),
            FlaggedRevokesWithheld = parts.Sum(p => p.FlaggedRevokesWithheld),
            DirectOwnedFlaggedShares = parts.Sum(p => p.DirectOwnedFlaggedShares),
            Failures = parts.Sum(p => p.Failures),
            Tables = parts.SelectMany(p => p.Tables)
                .GroupBy(t => t.Table, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ExternalTeamShareTableReport(
                    g.Key, g.Sum(t => t.TeamGrants), g.Sum(t => t.TeamChanges), g.Sum(t => t.TeamRevokes),
                    g.Sum(t => t.FlaggedUserRevokes), g.Sum(t => t.Reowns),
                    g.SelectMany(t => t.GrantRows).Take(ExternalTeamShareSynchronizer.ReportIdsPerTable).ToArray(),
                    g.SelectMany(t => t.RevokeRows).Take(ExternalTeamShareSynchronizer.ReportIdsPerTable).ToArray()))
                .ToList(),
            Carried = parts.SelectMany(p => p.Carried).Distinct().ToList(),
            WalkAgain = parts.SelectMany(p => p.WalkAgain).Distinct().ToList(),
        };

        static bool IsReal(string? detail) => detail != Nothing.Detail;
    }

    /// <summary>True when nothing was left out of line.</summary>
    public bool IsComplete => Status is ExternalTeamShareStatus.Completed or ExternalTeamShareStatus.NotApplicable;

    /// <summary>Planned or made team grants, all tables.</summary>
    public int TeamGrants => Tables.Sum(t => t.TeamGrants);

    /// <summary>Planned or made team changes, all tables.</summary>
    public int TeamChanges => Tables.Sum(t => t.TeamChanges);

    /// <summary>Planned or made team revokes, all tables.</summary>
    public int TeamRevokes => Tables.Sum(t => t.TeamRevokes);

    /// <summary>Planned or made flagged-user revokes, all tables.</summary>
    public int FlaggedUserRevokes => Tables.Sum(t => t.FlaggedUserRevokes);

    /// <summary>Planned or made re-owns, all tables.</summary>
    public int Reowns => Tables.Sum(t => t.Reowns);

    internal static ExternalTeamShareResult Nothing { get; } = new(ExternalTeamShareStatus.NotApplicable, "no lineage row was asked about");
}

/// <summary>
/// uac-r2 task 191c: the External-team mode of the share synchronizer (#2179 design §3.4). For each lineage row (the three roots
/// and every <see cref="SecureChildLineage.Children"/> table): the External Collaborators unit's DEFAULT team holds exactly
/// <see cref="TeamRightsFor"/> when <see cref="ExternalVisibility"/> says Visible and the row is in no secure family, and nothing
/// otherwise; a flagged user (a stored <c>sprk_isexternal = true</c>, never inferred) keeps no per-user share on a child the
/// predicate does not show; and a row a flagged user owns is re-owned to its I-6 team.
/// </summary>
/// <remarks>
/// <para><b>Only two kinds of principal are ever written.</b> The External team, and a system user whose stored flag reads
/// <c>true</c>. A blank, false or unreadable flag is not flagged: that user's shares are never touched (design §3.8 treats an
/// unreadable flag as external for GRANTS only, which is the secure mirror's side).</para>
/// <para><b>Secure families are never team-shared</b> (design §3.4, §3.5): a row owned by the Secure Record owner team, a root
/// whose <c>sprk_issecure</c> is not a stored <c>false</c>, and a row whose closure reaches such a root (or a root that could not
/// be read) gets no team share, and an existing one is revoked. Flagged users' shares on Secure-team-owned rows are the secure
/// mirror's (it applies the predicate for flagged principals since this task).</para>
/// <para><b>Fail closed</b> (design §3.8). Undecided never grants; it revokes an existing team or flagged-user share and the row is
/// carried. A row, share or flag read that faults writes nothing for the rows it concerns (carried). A prerequisite that faults
/// (the Secure team, the External team) ends the pass Failed with nothing written. Every change is read back.</para>
/// <para><b>Out of scope here.</b> Roots' per-user shares (RestrictedExternalShareRemover, I-14), Direct-team-owned rows' per-user
/// shares (DirectMessageParticipantShares: reported, never fought), and rows outside the registry.</para>
/// <para><b>Writes are off by default</b>: <see cref="WritesEnabledConfigKey"/> and <see cref="ReownWritesEnabledConfigKey"/>.
/// Off, a pass reads, decides and REPORTS the plan per table (counts and row ids), and writes nothing.</para>
/// </remarks>
public sealed class ExternalTeamShareSynchronizer
{
    /// <summary>The settings section.</summary>
    internal const string SectionKey = "ExternalAccess:ExternalTeamShare";

    /// <summary>Share writes (team grants, changes, revokes; flagged-user revokes). Absent, empty or unparseable = off.</summary>
    internal const string WritesEnabledConfigKey = SectionKey + ":WritesEnabled";

    /// <summary>Owner writes (re-own of rows a flagged user owns). Absent, empty or unparseable = off.</summary>
    internal const string ReownWritesEnabledConfigKey = SectionKey + ":ReownWritesEnabled";

    /// <summary>The External Collaborators business unit's name; default <see cref="DefaultBusinessUnitName"/>.</summary>
    internal const string BusinessUnitNameConfigKey = SectionKey + ":BusinessUnitName";

    /// <summary>The name <c>config/external-collaborator-role.json</c> gives the unit (pinned by ExternalShareGrantAllowListTests).</summary>
    internal const string DefaultBusinessUnitName = "External Collaborators";

    /// <summary>Ids per <c>In</c> condition (far below SQL Server's parameter ceiling).</summary>
    internal const int Chunk = SecureChildShareSynchronizer.DescendantConditionsPerQuery;

    /// <summary>The most descendants one family walk collects; past it the walk reports itself truncated.</summary>
    internal const int MaxFamilyRows = 5000;

    /// <summary>Row ids per table the report lists (the counts are complete).</summary>
    internal const int ReportIdsPerTable = 20;

    internal const string LogTag = "[EXTERNAL-TEAM-SHARE]";

    private const string OwningTeamColumn = "owningteam";
    private const string OwningUserColumn = "owninguser";
    private const string IsSecureColumn = "sprk_issecure";
    private const string IsExternalColumn = "sprk_isexternal";
    private const string ModifiedOnColumn = "modifiedon";
    private const string VersionNumberColumn = "versionnumber";

    /// <summary>The most predicate calls one pass makes while bisecting a faulted decision (F2).</summary>
    internal const int MaxDecideCalls = 16;

    /// <summary>The tables partners file under: the role grants AppendTo there (design §3.2; config lineage entries with AppendTo).</summary>
    internal static readonly IReadOnlySet<string> FilingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_matter", "sprk_project", "sprk_workassignment", "sprk_document", "sprk_communication", "sprk_event", "sprk_todo",
    };

    /// <summary>
    /// Registry tables the team never shares, whatever the predicate says: the role holds no privilege on them (design §3.2,
    /// config "never"): hard-deny and communication-content tables (the predicate hides them anyway) and the invoice family
    /// (Q6 = a: invoices reach flagged users through the SPA firm route only).
    /// </summary>
    internal static readonly IReadOnlySet<string> NeverSharedTables = new HashSet<string>(
        // The communication-content tables come from the lineage registry's markers (task 191b: one list, never restated).
        SecureChildLineage.NeverExternalTables.Where(t => SecureChildLineage.IsRoot(t) || SecureChildLineage.IsChild(t))
            .Concat(
            [
                "sprk_procedureassignment", "sprk_storedfile", "sprk_ruleset", "sprk_policy", "sprk_policyversion",
                "sprk_invoice", "sprk_invoicesubmission", "sprk_invoiceapproval", "sprk_invoicecorrection", "sprk_externalidentifier",
                "sprk_billingevent",
            ]),
        StringComparer.OrdinalIgnoreCase);

    private readonly IGenericEntityService _dataverse;
    private readonly IDataverseRecordShareService _shares;
    private readonly ExternalVisibilityDecider _visibility;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalTeamShareSynchronizer> _logger;
    private readonly IRecordOwnershipResolver? _ownership;
    private readonly CoreAncestorResolver? _coreAncestors;
    private readonly DirectMessageOwnerTeam? _directMessageOwnerTeam;

    internal ExternalTeamShareSynchronizer(
        IGenericEntityService dataverse,
        IDataverseRecordShareService shares,
        ExternalVisibilityDecider visibility,
        IConfiguration configuration,
        ILogger<ExternalTeamShareSynchronizer> logger,
        IRecordOwnershipResolver? ownership = null,
        CoreAncestorResolver? coreAncestors = null,
        DirectMessageOwnerTeam? directMessageOwnerTeam = null)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _shares = shares ?? throw new ArgumentNullException(nameof(shares));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownership = ownership;
        _coreAncestors = coreAncestors;
        _directMessageOwnerTeam = directMessageOwnerTeam;
    }

    /// <summary>Test seam: the Direct-message owner team's identity (default: <see cref="DirectMessageOwnerTeam"/>, or "not
    /// configured" when none was given).</summary>
    internal Func<CancellationToken, Task<DirectMessageOwnerTeamIdentity>>? DirectTeamIdentity { get; init; }

    /// <summary>Whether share writes are on (<see cref="WritesEnabledConfigKey"/> parses to <c>true</c>).</summary>
    internal bool WritesEnabled => IsOn(WritesEnabledConfigKey);

    /// <summary>Whether owner writes are on (<see cref="ReownWritesEnabledConfigKey"/> parses to <c>true</c>).</summary>
    internal bool ReownWritesEnabled => IsOn(ReownWritesEnabledConfigKey);

    private bool IsOn(string key) => bool.TryParse(_configuration[key]?.Trim(), out var on) && on;

    /// <summary>
    /// The External team's share for a visible row of <paramref name="table"/>: Read + AppendTo on the tables partners file under,
    /// Read on every other lineage table, <c>null</c> (nothing, ever) on <see cref="NeverSharedTables"/> and on any table outside
    /// the registry.
    /// </summary>
    internal static RecordShareRights? TeamRightsFor(string table) =>
        !(SecureChildLineage.IsRoot(table) || SecureChildLineage.IsChild(table)) || NeverSharedTables.Contains(table)
            ? null
            : FilingTables.Contains(table) ? RecordShareLevels.ExternalTeamFilingRights : RecordShareLevels.ExternalTeamReadRights;

    /// <summary>Every lineage table the passes list, roots first, then children by name.</summary>
    internal static IReadOnlyList<string> LineageTables { get; } =
        SecureChildLineage.Roots.Order(StringComparer.Ordinal)
            .Concat(SecureChildLineage.Children.Keys.Order(StringComparer.Ordinal))
            .Select(t => t.ToLowerInvariant())
            .ToArray();

    // ── The predicate and the flags, for the secure mirror's flagged-principal filter ───────────────────────────────

    /// <summary>The predicate over the BFF's app-only readers (never throws for a read fault: Undecided).</summary>
    internal async Task<ExternalVisibilityResult> DecideAsync(IReadOnlyCollection<(string Table, Guid Id)> rows, CancellationToken ct)
    {
        try
        {
            return await _visibility(rows, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("{Tag} the visibility decision faulted ({ErrorType}); every one of the {Rows} rows is undecided.",
                LogTag, ex.GetType().Name, rows.Count);
            return new ExternalVisibilityResult(new Dictionary<ExternalVisibility.Node, ExternalVisibilityDecision>(), faulted: true);
        }
    }

    /// <summary>
    /// The stored <c>sprk_isexternal</c> of each system user: <c>true</c>, <c>false</c> (blank reads as false: not flagged, owner
    /// round 67), or absent from the answer when the user's row did not come back. A read fault propagates.
    /// </summary>
    internal async Task<IReadOnlyDictionary<Guid, bool>> ReadExternalFlagsAsync(IEnumerable<Guid> systemUserIds, CancellationToken ct)
    {
        var flags = new Dictionary<Guid, bool>();
        foreach (var chunk in systemUserIds.Where(id => id != Guid.Empty).Distinct().Chunk(Chunk))
        {
            var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet(IsExternalColumn), NoLock = true };
            query.Criteria.AddCondition("systemuserid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var user in (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
                flags[user.Id] = user.GetAttributeValue<bool?>(IsExternalColumn) == true;
        }

        return flags;
    }

    // ── Entry points ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Brings ONE root and every row filed under it (its family, walked downward through the registry's lookups, a work
    /// assignment's typed filing, a project's pair and a Signal's or Decision Record's core pair) into line. The L1 hook of the
    /// Restricted remover and of Make Secure / Unsecure. Never throws for a Dataverse fault.
    /// </summary>
    public Task<ExternalTeamShareResult> SyncFamilyAsync(
        string rootTable, Guid rootId, string trigger, CancellationToken ct, int maxRows = MaxWalkRows) =>
        SyncFamiliesAsync([(rootTable, rootId)], trigger, ct, maxRows);

    /// <summary>The most family rows an inline (L1) hook walks and brings into line before returning to its caller; a larger
    /// family is the two-minute pass's, which lists the changed row (F3: a save is not held for a large family).</summary>
    internal const int MaxInlineFamilyRows = 100;

    /// <summary>The most rows one family walk collects (ids only); past it the changed rows are walked again, one by one, next
    /// run (pass-2 F1).</summary>
    internal const int MaxWalkRows = 50_000;

    /// <summary>
    /// Brings <paramref name="changed"/> and every row filed under each of them into line (the recent-changes reverse walk: a
    /// modified communication or thread reaches the documents derived from it and the records about it; a modified root reaches its
    /// descendants), in passes of <see cref="MaxFamilyRows"/> rows. At most <paramref name="maxRows"/> rows are walked: past
    /// it (or when the walk's read faults) the result is Incomplete and the changed rows are returned to walk again
    /// (<see cref="ExternalTeamShareResult.WalkAgain"/>). Never throws for a Dataverse fault.
    /// </summary>
    public async Task<ExternalTeamShareResult> SyncFamiliesAsync(
        IReadOnlyCollection<(string Table, Guid Id)> changed, string trigger, CancellationToken ct, int maxRows = MaxWalkRows)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var start = Normalize(changed);
        if (start.Count == 0)
            return ExternalTeamShareResult.Nothing;

        var walkCap = Math.Clamp(maxRows, 1, MaxWalkRows);
        IReadOnlyList<(string Table, Guid Id)> family;
        bool truncated;
        try
        {
            (family, truncated) = await DescendantsAsync(start, ct, walkCap).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Tag} the rows filed under {Count} changed row(s) could not be read ({Trigger}); only the changed " +
                "rows themselves are brought into line, and they are walked again next run.", LogTag, start.Count, trigger);
            var own = await SyncRowsAsync(start, trigger, ct).ConfigureAwait(false);
            return Incomplete(own, start, "the rows filed under a changed row could not be read", fault: true);
        }

        // Every row walked is brought into line, in passes (the walk collects ids only), so a large family does not stop at
        // the same rows every run.
        var parts = new List<ExternalTeamShareResult>();
        foreach (var pass in start.Concat(family).Chunk(MaxFamilyRows))
            parts.Add(await SyncRowsAsync(pass, trigger, ct).ConfigureAwait(false));
        var result = ExternalTeamShareResult.Combine(parts);
        if (!truncated)
            return result;
        _logger.LogWarning("{Tag} the family walk from {Count} row(s) stopped at {Max} rows ({Trigger}); the rows walked were " +
            "brought into line and the changed rows are walked again next run.", LogTag, start.Count, walkCap, trigger);
        return Incomplete(result, start, $"the family walk stopped at {walkCap} rows", fault: false);
    }

    private static ExternalTeamShareResult Incomplete(
        ExternalTeamShareResult result, IReadOnlyCollection<(string Table, Guid Id)> walkAgain, string detail, bool fault) =>
        result.Status == ExternalTeamShareStatus.Failed
            ? result with { WalkAgain = result.WalkAgain.Concat(walkAgain).Distinct().ToList() }
            : result with
            {
                Status = ExternalTeamShareStatus.Incomplete,
                Detail = result.Detail is null ? detail : $"{result.Detail}; {detail}",
                WalkAgain = result.WalkAgain.Concat(walkAgain).Distinct().ToList(),
                Failures = result.Failures + (fault ? walkAgain.Count : 0),
            };

    /// <summary>
    /// Brings exactly <paramref name="rows"/> into line (no walk): the sweep's page, carried rows. Rows outside the lineage
    /// registry are ignored. Never throws for a Dataverse fault; cancellation propagates.
    /// </summary>
    public async Task<ExternalTeamShareResult> SyncRowsAsync(
        IReadOnlyCollection<(string Table, Guid Id)> rows, string trigger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var asked = Normalize(rows);
        if (asked.Count == 0)
            return ExternalTeamShareResult.Nothing;

        var pass = new Pass(this, trigger, ct);
        try
        {
            if (await pass.PrepareAsync().ConfigureAwait(false) is { } refusal)
            {
                _logger.LogError("{Tag} nothing was decided or written for {Rows} row(s) ({Trigger}): {Refusal}.",
                    LogTag, asked.Count, trigger, refusal);
                return new ExternalTeamShareResult(ExternalTeamShareStatus.Failed, refusal)
                {
                    WritesEnabled = WritesEnabled,
                    ReownWritesEnabled = ReownWritesEnabled,
                    Carried = asked,
                };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Tag} the Secure Record or External Collaborators team could not be read; nothing was decided or " +
                "written for {Rows} row(s) ({Trigger}).", LogTag, asked.Count, trigger);
            return new ExternalTeamShareResult(ExternalTeamShareStatus.Failed, "a prerequisite team could not be read")
            {
                WritesEnabled = WritesEnabled,
                ReownWritesEnabled = ReownWritesEnabled,
                Carried = asked,
            };
        }

        return await pass.RunAsync(asked).ConfigureAwait(false);
    }

    /// <summary>
    /// The rows of every lineage table modified at or after <paramref name="defaultSinceUtc"/> (or, for a table held by an earlier
    /// capped listing, after its held position), in (<c>modifiedon</c>, id) order, at most <paramref name="perTableCap"/> per
    /// table, with the last position read per capped table (the caller holds that table there). The position is a key set
    /// (<c>modifiedon</c>, id), so more than <paramref name="perTableCap"/> rows sharing one <c>modifiedon</c> (a bulk import)
    /// still advance (F2). A read fault propagates.
    /// </summary>
    internal async Task<(List<(string Table, Guid Id)> Rows, Dictionary<string, (DateTime At, Guid AfterId)> CappedAt)> ListModifiedSinceAsync(
        IReadOnlyDictionary<string, (DateTime At, Guid AfterId)> heldByTable, DateTime defaultSinceUtc, int perTableCap, CancellationToken ct)
    {
        var rows = new List<(string Table, Guid Id)>();
        var capped = new Dictionary<string, (DateTime At, Guid AfterId)>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in LineageTables)
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(ModifiedOnColumn),
                TopCount = perTableCap,
                NoLock = true,
            };
            if (heldByTable.TryGetValue(table, out var held))
            {
                // (modifiedon > At) OR (modifiedon = At AND id > AfterId)
                var after = new FilterExpression(LogicalOperator.Or);
                after.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterThan, held.At);
                var sameInstant = new FilterExpression(LogicalOperator.And);
                sameInstant.AddCondition(ModifiedOnColumn, ConditionOperator.Equal, held.At);
                sameInstant.AddCondition(table + "id", ConditionOperator.GreaterThan, held.AfterId);
                after.AddFilter(sameInstant);
                query.Criteria.AddFilter(after);
            }
            else
            {
                query.Criteria.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterEqual, defaultSinceUtc);
            }

            query.AddOrder(ModifiedOnColumn, OrderType.Ascending);
            query.AddOrder(table + "id", OrderType.Ascending);
            var found = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
            rows.AddRange(found.Where(e => e.Id != Guid.Empty).Select(e => (table, e.Id)));
            if (found.Count >= perTableCap && found[^1].GetAttributeValue<DateTime?>(ModifiedOnColumn) is { } last)
            {
                capped[table] = (last, found[^1].Id);
                _logger.LogWarning("{Tag} {Table}: {Cap} or more rows changed since the last run; the table is held at the last row " +
                    "read and continues next run.", LogTag, table, perTableCap);
            }
        }

        return (rows, capped);
    }

    /// <summary>
    /// The sweep's next page: up to <paramref name="pageSize"/> rows of the lineage tables in table order then id order, after
    /// <paramref name="cursor"/>. Returns the next cursor, or <c>null</c> when this page ended the last table (a full pass).
    /// A read fault propagates.
    /// </summary>
    internal async Task<(List<(string Table, Guid Id)> Rows, (string Table, Guid After)? Next)> ListSweepPageAsync(
        (string Table, Guid After)? cursor, int pageSize, CancellationToken ct)
    {
        var rows = new List<(string Table, Guid Id)>();
        var tableIndex = cursor is { } c ? Math.Max(0, LineageTables.ToList().IndexOf(c.Table)) : 0;
        var after = cursor?.After ?? Guid.Empty;
        while (tableIndex < LineageTables.Count && rows.Count < pageSize)
        {
            var table = LineageTables[tableIndex];
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = pageSize - rows.Count,
                NoLock = true,
            };
            if (after != Guid.Empty)
                query.Criteria.AddCondition(table + "id", ConditionOperator.GreaterThan, after);
            query.AddOrder(table + "id", OrderType.Ascending);
            var found = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities
                .Where(e => e.Id != Guid.Empty).ToList();
            rows.AddRange(found.Select(e => (table, e.Id)));
            if (found.Count > 0 && rows.Count >= pageSize)
                return (rows, (table, found[^1].Id));
            tableIndex++;
            after = Guid.Empty;
        }

        return (rows, tableIndex < LineageTables.Count ? (LineageTables[tableIndex], Guid.Empty) : null);
    }

    // ── The downward walk ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every row filed under <paramref name="start"/>, level by level (at most <see cref="SecureChildShareSynchronizer.MaxLineageDepth"/>),
    /// through each child table's lineage lookups, a work assignment's typed filing lookups, a project's pair and a Signal's or
    /// Decision Record's core pair; one query per table per level (an <c>Or</c> of <c>In</c> conditions, chunked). Stops at
    /// <see cref="MaxFamilyRows"/> (truncated). A read fault propagates.
    /// </summary>
    internal async Task<(IReadOnlyList<(string Table, Guid Id)> Rows, bool Truncated)> DescendantsAsync(
        IReadOnlyCollection<(string Table, Guid Id)> start, CancellationToken ct, int maxRows = MaxFamilyRows)
    {
        var seen = new HashSet<(string Table, Guid Id)>(start);
        var found = new List<(string Table, Guid Id)>();
        var frontier = start.ToList();
        for (var depth = 0; depth < SecureChildShareSynchronizer.MaxLineageDepth && frontier.Count > 0; depth++)
        {
            var next = new List<(string Table, Guid Id)>();
            foreach (var table in LineageTables)
            {
                foreach (var conditions in DownwardConditions(table, frontier).Chunk(Chunk))
                {
                    var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false), NoLock = true };
                    var any = new FilterExpression(LogicalOperator.Or);
                    foreach (var group in conditions.GroupBy(c => c.Column, StringComparer.OrdinalIgnoreCase))
                        any.AddCondition(group.Key, ConditionOperator.In, group.Select(g => g.Value).ToArray());
                    query.Criteria.AddFilter(any);
                    foreach (var row in await ReadAllAsync(query, ct).ConfigureAwait(false))
                    {
                        if (row.Id == Guid.Empty || !seen.Add((table, row.Id)))
                            continue;
                        next.Add((table, row.Id));
                        found.Add((table, row.Id));
                        if (found.Count >= maxRows)
                            return (found, true);
                    }
                }
            }

            frontier = next;
        }

        return (found, false);
    }

    /// <summary>The (column, value) conditions that find rows of <paramref name="table"/> filed under one of
    /// <paramref name="frontier"/>.</summary>
    private static IEnumerable<(string Column, object Value)> DownwardConditions(
        string table, IReadOnlyCollection<(string Table, Guid Id)> frontier)
    {
        IEnumerable<(string Column, string Target)> lookups =
            SecureChildLineage.Children.TryGetValue(table, out var child)
                ? child.Lookups.Select(l => (l.Key, l.Value))
                : SecureRootInheritance.TypedLookupsOf(table);
        foreach (var (column, target) in lookups)
        {
            foreach (var parent in frontier.Where(f => string.Equals(f.Table, target, StringComparison.OrdinalIgnoreCase)))
                yield return (column, parent.Id);
        }

        // A project is filed under the record its BFF-managed pair names (SecureRootInheritance.PairNamesAParentOnItsOwn).
        if (SecureRootInheritance.BffManagedPairTables.Contains(table))
        {
            foreach (var parent in frontier.Where(f => SecureChildLineage.IsRoot(f.Table)))
            {
                foreach (var format in SecureChildLineage.StoredCoreIdFormats)
                    yield return (SecureRootInheritance.PairIdColumn, parent.Id.ToString(format, CultureInfo.InvariantCulture).ToLowerInvariant());
            }
        }

        foreach (var match in SecureChildLineage.CorePairMatches(table, frontier))
            yield return match;
    }

    private async Task<List<Entity>> ReadAllAsync(QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = SecureChildShareSynchronizer.PageSize, PageNumber = 1 };
        var all = new List<Entity>();
        for (var page = 1; page <= SecureChildShareSynchronizer.MaxPages; page++)
        {
            var answer = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            all.AddRange(answer.Entities);
            if (!answer.MoreRecords)
                return all;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = answer.PagingCookie;
        }

        throw new InvalidOperationException($"{query.EntityName}: more than {SecureChildShareSynchronizer.MaxPages} pages under one family.");
    }

    /// <summary>The Undecided reasons a later read may answer (carried to the next run). The structural ones (no root, too deep,
    /// a chain too long, a malformed pair, an unknown table, an unreadable link value) change only when the row or a parent is
    /// edited, which the recent-changes pass sees, and the hourly sweep re-decides them.</summary>
    internal static bool IsTransient(ExternalVisibilityReason reason) => reason is
        ExternalVisibilityReason.ReadFailed or ExternalVisibilityReason.RootUnreadable or ExternalVisibilityReason.RowNotFound
        or ExternalVisibilityReason.DocumentUndecided or ExternalVisibilityReason.InvoiceUndecided or ExternalVisibilityReason.NotAsked;

    private static List<(string Table, Guid Id)> Normalize(IEnumerable<(string Table, Guid Id)> rows) =>
        rows.Where(r => r.Id != Guid.Empty && !string.IsNullOrWhiteSpace(r.Table))
            .Select(r => (Table: r.Table.Trim().ToLowerInvariant(), r.Id))
            .Where(r => SecureChildLineage.IsRoot(r.Table) || SecureChildLineage.IsChild(r.Table))
            .Distinct()
            .ToList();

    // ── One pass ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One row's facts as read.</summary>
    private sealed record Facts(Guid? OwningTeam, Guid? OwningUser, bool? IsSecureFlag);

    /// <summary>One row's plan.</summary>
    private sealed class RowPlan((string Table, Guid Id) row)
    {
        public (string Table, Guid Id) Row { get; } = row;
        public int TeamHave { get; set; }
        public int TeamWant { get; set; }
        public string? TeamRightsCsv { get; set; }
        public List<DataversePrincipalRef> FlaggedRevokes { get; } = new();
        public Guid? ReownFrom { get; set; }

        public bool TeamGrant => TeamWant > 0 && TeamHave == 0;
        public bool TeamChange => TeamWant > 0 && TeamHave > 0 && TeamHave != TeamWant;
        public bool TeamRevoke => TeamWant == 0 && TeamHave > 0;
        public bool HasShareWrite => TeamGrant || TeamChange || TeamRevoke || FlaggedRevokes.Count > 0;
    }

    private sealed class Pass(ExternalTeamShareSynchronizer owner, string trigger, CancellationToken ct)
    {
        private readonly HashSet<(string Table, Guid Id)> _carried = new();
        private readonly HashSet<(string Table, Guid Id)> _faults = new();

        /// <summary>Carries a row for a FAULT (counted in <see cref="ExternalTeamShareResult.Failures"/>).</summary>
        private void Carry((string Table, Guid Id) row)
        {
            _carried.Add(row);
            _faults.Add(row);
        }
        private Guid? _secureTeam;
        private Guid? _externalTeam;
        private Guid? _directTeam;
        private bool _directUnknown;
        private int _externalTeamOwned;
        private int _withheld;
        private int _directOwnedFlagged;
        private int _written;
        private int _reowned;

        private ILogger Log => owner._logger;

        /// <summary>The Secure team, the External team and the Direct team. A refusal (ambiguity) is returned; a fault throws.</summary>
        public async Task<string?> PrepareAsync()
        {
            var secure = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(owner._dataverse, owner._configuration, ct)
                .ConfigureAwait(false);
            if (secure.Refusal is { } secureRefusal)
                return $"the Secure Record owner team cannot be told ({secureRefusal})";
            _secureTeam = secure.TeamId;

            var external = await owner.ResolveExternalTeamAsync(ct).ConfigureAwait(false);
            if (external.Refusal is { } externalRefusal)
                return externalRefusal;
            _externalTeam = external.TeamId;

            var identity = owner.DirectTeamIdentity is { } seam
                ? await seam(ct).ConfigureAwait(false)
                : owner._directMessageOwnerTeam is { } direct
                    ? await direct.IdentifyDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false)
                    : DirectMessageOwnerTeamIdentity.NotConfigured;
            if (identity.IsIdentified)
                _directTeam = identity.TeamId;
            else if (!identity.IsNotConfigured)
                _directUnknown = true;
            return null;
        }

        public async Task<ExternalTeamShareResult> RunAsync(IReadOnlyList<(string Table, Guid Id)> asked)
        {
            // 1. The rows' own facts (owner; a root's flag). A row that no longer exists drops out.
            var facts = await ReadFactsAsync(asked).ConfigureAwait(false);
            var present = asked.Where(facts.ContainsKey).ToList();
            if (present.Count == 0)
                return Report([], 0, 0, 0, 0);

            // 2. The predicate, then the facts of every root a closure reached (a secure family is never team-shared).
            var (visibility, decideFaulted) = await DecideBisectingAsync(present).ConfigureAwait(false);
            var rootsNeeded = present
                .SelectMany(r => visibility.RootsOf(r.Table, r.Id) ?? [])
                .Where(r => !facts.ContainsKey(r))
                .Distinct()
                .ToList();
            var rootReadFailed = new HashSet<(string, Guid)>();
            var rootFacts = rootsNeeded.Count == 0
                ? new Dictionary<(string, Guid), Facts>()
                : await ReadFactsAsync(rootsNeeded, carryFailures: false, failed: rootReadFailed).ConfigureAwait(false);

            // 3. The shares on every present row (a table whose read faults is carried, nothing written for it).
            var shares = await ReadSharesAsync(present).ConfigureAwait(false);

            // 4. The stored flags of every user who holds a share on, or owns, a present row.
            var users = present
                .SelectMany(r => shares.TryGetValue(r, out var s) ? s.Where(p => p.Principal.Kind == DataversePrincipalKind.SystemUser && p.AccessRightsMask > 0).Select(p => p.Principal.Id) : [])
                .Concat(present.Select(r => facts[r].OwningUser).OfType<Guid>())
                .Distinct()
                .ToList();
            IReadOnlyDictionary<Guid, bool>? flags;
            try
            {
                flags = await owner.ReadExternalFlagsAsync(users, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "{Tag} the flags of {Users} user(s) could not be read ({Trigger}); no flagged-user revoke or re-own " +
                    "this pass, and the rows are carried.", LogTag, users.Count, trigger);
                flags = null;
            }

            // 5. The plan.
            var plans = new List<RowPlan>();
            int shareable = 0, undecided = 0, secureFamily = 0;
            foreach (var row in present)
            {
                var fact = facts[row];
                if (fact.OwningTeam is { } ownerTeam && ownerTeam == _externalTeam)
                {
                    _externalTeamOwned++;
                    Log.LogError("{Tag} BU OWNS ROWS: {Table} {Id} is owned by the External Collaborators team, which must never own " +
                        "a row (census alarm; the re-own and placement steps are the owner's).", LogTag, row.Table, row.Id);
                }

                // A row whose decision still faulted after bisecting, or whose root could not be read, is carried (F2/F3). It
                // fails closed below: no team grant, and an existing team or flagged-user share is revoked (task constraint).
                if (decideFaulted.Contains(row)
                    || (visibility.RootsOf(row.Table, row.Id) is { } reached && reached.Any(rootReadFailed.Contains)))
                    Carry(row);

                var decision = visibility.For(row.Table, row.Id);
                var secure = InSecureFamily(row, fact, visibility, facts, rootFacts);
                if (secure)
                    secureFamily++;
                if (decision.Verdict == ExternalVisibilityVerdict.Undecided)
                {
                    undecided++;
                    if (IsTransient(decision.Reason))
                        _carried.Add(row); // an Undecided answer, not a fault: a read may answer next run; a structural one waits for a change or the sweep
                }

                var rights = TeamRightsFor(row.Table);
                var shareableRow = !secure && decision.IsVisible && rights is not null && _externalTeam is not null;
                if (shareableRow)
                    shareable++;

                if (!shares.TryGetValue(row, out var held))
                    continue; // its shares could not be read: carried, nothing planned

                var plan = new RowPlan(row);
                if (_externalTeam is { } team)
                {
                    plan.TeamHave = held.Where(p => p.Principal == DataversePrincipalRef.Team(team)).Select(p => p.AccessRightsMask)
                        .DefaultIfEmpty(0).Max();
                    plan.TeamWant = shareableRow ? rights!.Value.AccessRightsMask : 0;
                    plan.TeamRightsCsv = shareableRow ? rights!.Value.AccessRightsCsv : null;
                }

                // Flagged users' per-user shares on a child the predicate does not show (roots are I-14's; Secure-team-owned rows
                // the mirror's; Direct-team-owned rows task 198's writer's — reported, never fought). Undecided revokes too, a
                // transient one included (task constraint: fail closed); the bisecting decision keeps one faulting row from
                // making the rest of its batch Undecided (F2).
                if (!decision.IsVisible && !SecureChildLineage.IsRoot(row.Table) && !(_secureTeam is { } st && fact.OwningTeam == st))
                {
                    var flaggedHolders = flags is null
                        ? []
                        : held.Where(p => p.Principal.Kind == DataversePrincipalKind.SystemUser && p.AccessRightsMask > 0
                                && flags.TryGetValue(p.Principal.Id, out var flagged) && flagged)
                            .Select(p => p.Principal).Distinct().ToList();
                    if (flags is null && held.Any(p => p.Principal.Kind == DataversePrincipalKind.SystemUser && p.AccessRightsMask > 0))
                        Carry(row);
                    if (flaggedHolders.Count > 0)
                    {
                        if (_directTeam is { } dt && fact.OwningTeam == dt)
                        {
                            _directOwnedFlagged += flaggedHolders.Count;
                            Log.LogWarning("{Tag} {Table} {Id} is a Direct message row the predicate does not show, and {Count} flagged " +
                                "user(s) hold a share on it (DirectMessageParticipantShares grants them; task 191c leaves them and " +
                                "reports it).", LogTag, row.Table, row.Id, flaggedHolders.Count);
                        }
                        else if (_directUnknown)
                        {
                            _withheld += flaggedHolders.Count;
                            Carry(row);
                        }
                        else
                        {
                            plan.FlaggedRevokes.AddRange(flaggedHolders);
                        }
                    }
                }

                if (flags is not null && fact.OwningUser is { } ownerUser && flags.TryGetValue(ownerUser, out var ownerFlagged) && ownerFlagged)
                    plan.ReownFrom = ownerUser;
                else if (flags is null && fact.OwningUser is not null)
                    Carry(row);

                if (plan.HasShareWrite || plan.ReownFrom is not null)
                    plans.Add(plan);
            }

            // 6. The writes (or the report).
            if (owner.WritesEnabled)
                await WriteSharesAsync(plans.Where(p => p.HasShareWrite).ToList(), facts).ConfigureAwait(false);
            if (owner.ReownWritesEnabled)
                await ReownAsync(plans.Where(p => p.ReownFrom is not null).ToList()).ConfigureAwait(false);

            return Report(plans, present.Count, shareable, undecided, secureFamily);
        }

        /// <summary>A Secure-team-owned row, a root not stored <c>false</c>, or a row whose closure reaches such a root or a root
        /// that could not be read (or whose roots are not known).</summary>
        private bool InSecureFamily(
            (string Table, Guid Id) row, Facts fact, ExternalVisibilityResult visibility,
            IReadOnlyDictionary<(string, Guid), Facts> facts, IReadOnlyDictionary<(string, Guid), Facts> rootFacts)
        {
            if (_secureTeam is { } secureTeam && fact.OwningTeam == secureTeam)
                return true;
            if (SecureChildLineage.IsRoot(row.Table) && fact.IsSecureFlag != false)
                return true;
            if (visibility.RootsOf(row.Table, row.Id) is not { } roots)
                return true;
            // Secure INHERITED from an ancestor root (a work assignment or project filed under a secure matter, before the
            // inheritance job stamps it): the predicate's effective root flags, Secure folded from the ancestors (F1).
            if (visibility.ReachesEffectivelySecureRoot(row.Table, row.Id))
                return true;
            foreach (var root in roots)
            {
                var rootFact = facts.TryGetValue(root, out var f) ? f : rootFacts.TryGetValue(root, out var r) ? r : null;
                if (rootFact is null || rootFact.IsSecureFlag != false
                    || (_secureTeam is { } st && rootFact.OwningTeam == st))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The predicate over <paramref name="rows"/>; a faulted decision is split in halves and asked again (at most
        /// <see cref="MaxDecideCalls"/> calls), so one bad row does not leave the batch undecided. The rows still faulted are
        /// returned (nothing is written for them; they are carried).
        /// </summary>
        private async Task<(ExternalVisibilityResult Result, HashSet<(string Table, Guid Id)> Faulted)> DecideBisectingAsync(
            List<(string Table, Guid Id)> rows)
        {
            var parts = new List<ExternalVisibilityResult>();
            var faulted = new HashSet<(string Table, Guid Id)>();
            var queue = new Queue<List<(string Table, Guid Id)>>();
            queue.Enqueue(rows);
            var calls = 0;
            while (queue.Count > 0)
            {
                var batch = queue.Dequeue();
                // The whole batch and both its halves faulted: a systemic fault (a reader down), not one bad row; splitting
                // further would only multiply the load (pass-2 K2).
                if (calls >= MaxDecideCalls || (calls >= 3 && parts.Count == 0))
                {
                    faulted.UnionWith(batch);
                    continue;
                }

                calls++;
                var result = await owner.DecideAsync(batch, ct).ConfigureAwait(false);
                if (!result.Faulted)
                {
                    parts.Add(result);
                    continue;
                }

                if (batch.Count == 1)
                {
                    faulted.Add(batch[0]);
                    continue;
                }

                var half = batch.Count / 2;
                queue.Enqueue(batch.Take(half).ToList());
                queue.Enqueue(batch.Skip(half).ToList());
            }

            if (faulted.Count > 0)
                Log.LogWarning("{Tag} the visibility decision faulted for {Count} of {Rows} row(s) after {Calls} call(s) ({Trigger}); " +
                    "no grant is made for them, an existing team or flagged-user share is revoked (fail closed), and they are carried.", LogTag, faulted.Count, rows.Count, calls, trigger);
            return (ExternalVisibilityResult.Merge(parts), faulted);
        }

        private async Task<Dictionary<(string, Guid), Facts>> ReadFactsAsync(
            IReadOnlyCollection<(string Table, Guid Id)> rows, bool carryFailures = true, HashSet<(string, Guid)>? failed = null)
        {
            var facts = new Dictionary<(string, Guid), Facts>();
            foreach (var group in rows.GroupBy(r => r.Table))
            {
                var isRoot = SecureChildLineage.IsRoot(group.Key);
                foreach (var chunk in group.Select(r => r.Id).Chunk(Chunk))
                {
                    var query = new QueryExpression(group.Key)
                    {
                        ColumnSet = isRoot
                            ? new ColumnSet(OwningTeamColumn, OwningUserColumn, IsSecureColumn)
                            : new ColumnSet(OwningTeamColumn, OwningUserColumn),
                        NoLock = true,
                    };
                    query.Criteria.AddCondition(group.Key + "id", ConditionOperator.In, chunk.Cast<object>().ToArray());
                    try
                    {
                        foreach (var entity in (await owner._dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
                        {
                            facts[(group.Key, entity.Id)] = new Facts(
                                entity.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } t && t != Guid.Empty ? t : null,
                                entity.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id is { } u && u != Guid.Empty ? u : null,
                                isRoot ? entity.GetAttributeValue<bool?>(IsSecureColumn) : null);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        Log.LogWarning(ex, "{Tag} {Count} {Table} row(s) could not be read ({Trigger}); nothing is written for them " +
                            "and they are carried.", LogTag, chunk.Length, group.Key, trigger);
                        if (carryFailures)
                        {
                            foreach (var id in chunk)
                                Carry((group.Key, id));
                        }

                        foreach (var id in chunk)
                            failed?.Add((group.Key, id));
                    }
                }
            }

            return facts;
        }

        private async Task<Dictionary<(string Table, Guid Id), IReadOnlyList<DataversePrincipalAccess>>> ReadSharesAsync(
            IReadOnlyCollection<(string Table, Guid Id)> rows)
        {
            var shares = new Dictionary<(string Table, Guid Id), IReadOnlyList<DataversePrincipalAccess>>();
            foreach (var group in rows.GroupBy(r => r.Table))
            {
                foreach (var chunk in group.Select(r => r.Id).Chunk(Chunk))
                {
                    try
                    {
                        var read = await owner._shares.GetPrincipalAccessForRecordsOrThrowAsync(group.Key, chunk, ct).ConfigureAwait(false);
                        foreach (var id in chunk)
                        {
                            if (read.TryGetValue(id, out var held))
                                shares[(group.Key, id)] = held;
                            else
                                Carry((group.Key, id));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        Log.LogWarning(ex, "{Tag} the shares of {Count} {Table} row(s) could not be read ({Trigger}); nothing is written " +
                            "for them and they are carried.", LogTag, chunk.Length, group.Key, trigger);
                        foreach (var id in chunk)
                            Carry((group.Key, id));
                    }
                }
            }

            return shares;
        }

        /// <summary>Revokes first, then changes, then grants, per row; then every written row is read back.</summary>
        private async Task WriteSharesAsync(IReadOnlyList<RowPlan> plans, IReadOnlyDictionary<(string, Guid), Facts> facts)
        {
            var writtenRows = new List<RowPlan>();
            foreach (var plan in plans)
            {
                var (table, id) = plan.Row;
                var entitySet = EntitySetOf(table);
                try
                {
                    foreach (var user in plan.FlaggedRevokes)
                    {
                        if (facts.TryGetValue(plan.Row, out var f) && f.OwningUser == user.Id)
                            await owner._shares.RevokeAccessAsync(entitySet, id, user, DataversePrincipalRef.User(user.Id), ct).ConfigureAwait(false);
                        else
                            await owner._shares.RevokeAccessAsync(entitySet, id, user, ct).ConfigureAwait(false);
                    }

                    // No External unit in this environment: no team plan (TeamHave = TeamWant = 0), only flagged-user revokes.
                    if (_externalTeam is { } externalTeam)
                    {
                        var team = DataversePrincipalRef.Team(externalTeam);
                        if (plan.TeamRevoke)
                            await owner._shares.RevokeAccessAsync(entitySet, id, team, ct).ConfigureAwait(false);
                        else if (plan.TeamChange)
                            await owner._shares.ModifyAccessAsync(entitySet, id, team, plan.TeamRightsCsv!, ct).ConfigureAwait(false);
                        else if (plan.TeamGrant)
                            await owner._shares.GrantAccessAsync(entitySet, id, team, plan.TeamRightsCsv!, ct).ConfigureAwait(false);
                    }
                    writtenRows.Add(plan);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Log.LogWarning(ex, "{Tag} a share write on {Table} {Id} failed ({Trigger}); it is carried.", LogTag, table, id, trigger);
                    Carry(plan.Row);
                }
            }

            // Read back: the team holds exactly what was planned (or nothing), and no revoked flagged user holds a share.
            foreach (var group in writtenRows.GroupBy(p => p.Row.Table))
            {
                foreach (var chunk in group.Chunk(Chunk))
                {
                    IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>> after;
                    try
                    {
                        after = await owner._shares.GetPrincipalAccessForRecordsOrThrowAsync(
                            group.Key, chunk.Select(p => p.Row.Id).ToArray(), ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        Log.LogWarning(ex, "{Tag} the read-back of {Count} {Table} row(s) failed ({Trigger}); they are carried.",
                            LogTag, chunk.Length, group.Key, trigger);
                        foreach (var plan in chunk)
                            Carry(plan.Row);
                        continue;
                    }

                    foreach (var plan in chunk)
                    {
                        var held = after.TryGetValue(plan.Row.Id, out var h) ? h : null;
                        var teamMask = held is null ? -1
                            : _externalTeam is { } externalTeam
                                ? held.Where(p => p.Principal == DataversePrincipalRef.Team(externalTeam))
                                    .Select(p => p.AccessRightsMask).DefaultIfEmpty(0).Max()
                                : 0;
                        var flaggedLeft = held is null || plan.FlaggedRevokes.Any(u => held.Any(p => p.Principal == u && p.AccessRightsMask > 0));
                        if (held is null || teamMask != plan.TeamWant || flaggedLeft)
                        {
                            Log.LogWarning("{Tag} {Table} {Id} did not read back as written ({Trigger}); it is carried.",
                                LogTag, plan.Row.Table, plan.Row.Id, trigger);
                            Carry(plan.Row);
                            continue;
                        }

                        _written += (plan.TeamGrant || plan.TeamChange || plan.TeamRevoke ? 1 : 0) + plan.FlaggedRevokes.Count;
                    }
                }
            }
        }

        /// <summary>Each row a flagged user owns, to the owner its I-6 question gives (an unfiled row: the customer business
        /// unit's team, through the resolver's flagged-acting-user rule), written and read back. Never the External team.</summary>
        private async Task ReownAsync(IReadOnlyList<RowPlan> plans)
        {
            if (plans.Count == 0)
                return;
            if (owner._ownership is null)
            {
                Log.LogWarning("{Tag} {Count} row(s) owned by a flagged user were not re-owned: no ownership resolver in this " +
                    "composition; they are carried.", LogTag, plans.Count);
                foreach (var plan in plans)
                    Carry(plan.Row);
                return;
            }

            var reads = new ExistingRowReads(owner._dataverse, owner._coreAncestors, null);
            foreach (var plan in plans)
            {
                var (table, id) = plan.Row;
                try
                {
                    var query = new QueryExpression(table) { ColumnSet = new ColumnSet(true), TopCount = 1, NoLock = true };
                    query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
                    var row = (await owner._dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
                    if (row is null)
                        continue;
                    if (row.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id != plan.ReownFrom)
                        continue; // someone else re-owned it since the facts were read

                    var question = await RecordOwnershipContext.ForExistingRowAsync(row, reads, ct).ConfigureAwait(false);
                    if (question.Undetermined is not null || question.NoCoreOwnerRule is not null)
                    {
                        Log.LogWarning("{Tag} {Table} {Id} is owned by a flagged user, but its ownership question cannot be asked " +
                            "({Reason}); it is carried.", LogTag, table, id, question.Undetermined ?? question.NoCoreOwnerRule);
                        Carry(plan.Row);
                        continue;
                    }

                    if (question.Context.Parents.Count == 0 && OwnedChildWrite.KeepsItsCreatorWhenUnfiled(table))
                    {
                        Log.LogWarning("{Tag} {Table} {Id} is an unfiled communication row owned by a flagged user; unfiled " +
                            "communications keep their creator (task 146 E1), so it is reported and not re-owned.", LogTag, table, id);
                        // not carried: it stays its creator's until it is filed (the refile hook re-owns it then)
                        continue;
                    }

                    var resolution = await owner._ownership.ResolveOwnerAsync(question.Context with
                    {
                        CallerSystemUserId = plan.ReownFrom,
                        WhenUnfiled = UnfiledOwnership.ActingUserTeam,
                    }, ct).ConfigureAwait(false);
                    // Never the External team; never INTO isolation (a Secure answer is the secure reconciler's move, with its
                    // mirror and its file relocation: SecureChildReconciler).
                    if (!resolution.IsOwned || resolution.OwningTeamId == _externalTeam || resolution.IsSecureOwner
                        || resolution.OwningTeamId == _secureTeam)
                    {
                        Log.LogWarning("{Tag} {Table} {Id} is owned by a flagged user and was not re-owned: {Outcome} ({Reason}); " +
                            "it is carried.", LogTag, table, id, resolution.Outcome,
                            resolution.OwningTeamId == _externalTeam ? "the answer is the External Collaborators team"
                            : resolution.IsSecureOwner || resolution.OwningTeamId == _secureTeam ? "the answer is the Secure Record owner team (the secure reconciler's)"
                            : resolution.Reason);
                        Carry(plan.Row);
                        continue;
                    }

                    // Conditional on the version read with the owner check (a concurrent re-file or re-own wins; it is carried).
                    var version = row.RowVersion
                        ?? (row.GetAttributeValue<long?>(VersionNumberColumn) is { } number
                            ? number.ToString(CultureInfo.InvariantCulture)
                            : null);
                    if (version is null)
                    {
                        Log.LogWarning("{Tag} {Table} {Id} carries no row version, so its owner is not written; it is carried.",
                            LogTag, table, id);
                        Carry(plan.Row);
                        continue;
                    }

                    await owner._dataverse.UpdateIfRowVersionMatchesAsync(table, id, version,
                        new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", resolution.OwningTeamId!.Value) }, ct)
                        .ConfigureAwait(false);
                    var check = new QueryExpression(table) { ColumnSet = new ColumnSet(OwningTeamColumn), TopCount = 1, NoLock = true };
                    check.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
                    var written = (await owner._dataverse.RetrieveMultipleAsync(check, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
                    if (written?.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id != resolution.OwningTeamId)
                    {
                        Log.LogWarning("{Tag} {Table} {Id}'s re-own did not read back; it is carried.", LogTag, table, id);
                        Carry(plan.Row);
                        continue;
                    }

                    _reowned++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Log.LogWarning(ex, "{Tag} the re-own of {Table} {Id} failed ({Trigger}); it is carried.", LogTag, table, id, trigger);
                    Carry(plan.Row);
                }
            }
        }

        private ExternalTeamShareResult Report(
            IReadOnlyList<RowPlan> plans, int examined, int shareable, int undecided, int secureFamily)
        {
            var tables = plans
                .GroupBy(p => p.Row.Table)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ExternalTeamShareTableReport(
                    g.Key,
                    g.Count(p => p.TeamGrant),
                    g.Count(p => p.TeamChange),
                    g.Count(p => p.TeamRevoke),
                    g.Sum(p => p.FlaggedRevokes.Count),
                    g.Count(p => p.ReownFrom is not null),
                    g.Where(p => p.TeamGrant || p.TeamChange).Select(p => p.Row.Id).Take(ReportIdsPerTable).ToArray(),
                    g.Where(p => p.TeamRevoke || p.FlaggedRevokes.Count > 0).Select(p => p.Row.Id).Take(ReportIdsPerTable).ToArray()))
                .ToList();

            var writes = owner.WritesEnabled;
            var reownWrites = owner.ReownWritesEnabled;
            foreach (var table in tables.Where(t => t.TeamGrants + t.TeamChanges + t.TeamRevokes + t.FlaggedUserRevokes + t.Reowns > 0))
            {
                Log.LogInformation(
                    "{Tag} {Mode} table={Table} teamGrants={Grants} teamChanges={Changes} teamRevokes={Revokes} " +
                    "flaggedUserRevokes={FlaggedRevokes} reowns={Reowns} reownMode={ReownMode} grantRows={GrantRows} revokeRows={RevokeRows} " +
                    "trigger={Trigger}",
                    LogTag, writes ? "applied" : "report-only", table.Table, table.TeamGrants, table.TeamChanges, table.TeamRevokes,
                    table.FlaggedUserRevokes, table.Reowns, reownWrites ? "applied" : "report-only",
                    string.Join(",", table.GrantRows), string.Join(",", table.RevokeRows), trigger);
            }

            if (_withheld > 0)
            {
                Log.LogWarning("{Tag} {Count} flagged-user revoke(s) withheld: the Direct-message owner team could not be told ({Trigger}); " +
                    "the rows are carried.", LogTag, _withheld, trigger);
            }

            var carried = _carried.ToList();
            if (carried.Count > 0)
            {
                Log.LogInformation("{Tag} {Count} row(s) carried ({Trigger}): {Rows}", LogTag, carried.Count, trigger,
                    string.Join(",", carried.Take(50).Select(c => $"{c.Table}:{c.Id:D}")));
            }

            return new ExternalTeamShareResult(
                carried.Count > 0 ? ExternalTeamShareStatus.Incomplete : ExternalTeamShareStatus.Completed,
                carried.Count > 0 ? $"{carried.Count} row(s) carried" : null)
            {
                WritesEnabled = writes,
                ReownWritesEnabled = reownWrites,
                Examined = examined,
                Shareable = shareable,
                Undecided = undecided,
                SecureFamily = secureFamily,
                Written = _written,
                Reowned = _reowned,
                ExternalTeamOwned = _externalTeamOwned,
                FlaggedRevokesWithheld = _withheld,
                DirectOwnedFlaggedShares = _directOwnedFlagged,
                Tables = tables,
                Carried = carried,
                Failures = _faults.Count,
            };
        }
    }

    private static string EntitySetOf(string table) =>
        SecureChildLineage.Children.TryGetValue(table, out var child)
            ? child.EntitySet
            : SecureDesignationRemoval.EntitySetFor(table);

    /// <summary>
    /// The External Collaborators unit's DEFAULT team: the unit by its configured name (TOP 2), then its default team (TOP 2).
    /// No unit: <c>(null, null)</c> (no team share in this environment yet; flagged-user revokes still run). Two units, or a unit
    /// with no or two default teams: a refusal. A Dataverse fault propagates.
    /// </summary>
    internal async Task<(Guid? TeamId, string? Refusal)> ResolveExternalTeamAsync(CancellationToken ct)
    {
        var name = _configuration[BusinessUnitNameConfigKey]?.Trim() is { Length: > 0 } configured ? configured : DefaultBusinessUnitName;
        var buQuery = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 2, NoLock = true };
        buQuery.Criteria.AddCondition("name", ConditionOperator.Equal, name);
        var units = (await _dataverse.RetrieveMultipleAsync(buQuery, ct).ConfigureAwait(false)).Entities;
        if (units.Count > 1)
            return (null, "more than one business unit carries the External Collaborators name");
        if (units.Count == 0 || units[0].Id == Guid.Empty)
            return (null, null);

        var teamQuery = new QueryExpression("team") { ColumnSet = new ColumnSet("teamid"), TopCount = 2, NoLock = true };
        teamQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, units[0].Id);
        teamQuery.Criteria.AddCondition("isdefault", ConditionOperator.Equal, true);
        var teams = (await _dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false)).Entities;
        return teams.Count == 1 && teams[0].Id != Guid.Empty
            ? (teams[0].Id, null)
            : (null, $"the External Collaborators business unit has {teams.Count} default team(s); exactly one is required");
    }
}
