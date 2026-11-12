using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191e (#2179 design v2 section 3.13; owner rounds 114-115 Q5, 2026-11-11 Q7 = a): every
/// two minutes, moves each systemuser whose stored <c>sprk_isexternal</c> reads <c>true</c> into the External Collaborators
/// business unit, assigns it the Spaarke External Collaborator role from that unit, and reads both back.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A flagged user left in a customer business unit reads every non-secure row there through role depth
/// and that unit's default team, Restricted and internal-only rows included (design section 1). Operators set the flag;
/// this job closes the gap within one tick instead of waiting for someone to move the user by hand.</para>
///
/// <para><b>Who it moves (and never).</b> Only a user whose flag reads <c>true</c> in THIS run, twice: in the candidate
/// read and again in a fresh read of that one user just before the write. Blank, false, masked or unreadable is internal
/// and is never moved; nothing here looks at a UPN, an email domain or a <c>#EXT#</c> guest marker (Model 1: customer
/// core users are B2B guests and are not external). It never moves an application user, a disabled user, a user whose
/// access mode is not interactive (Read-Write or Read), or an administrator (System Administrator held directly or through
/// a team, matched by role template id, or the Administrative access mode). A user whose state cannot be read is skipped.
/// The BFF's own identities are application users.</para>
///
/// <para><b>One-way.</b> The only business-unit write is TO the unit, and only for a user outside it. A user already in the
/// unit is never moved, flag or no flag. A user in the unit whose flag no longer reads true is REPORTED
/// (<c>unflaggedInUnit</c>) for an administrator, who moves it back by hand (round 115).</para>
///
/// <para><b>The write</b> (spike S2, design A.4): PATCH <c>businessunitid</c>, which strips the user's direct roles and swaps
/// its default team; then associate the role copy that lives IN the unit (task 191d D-4); then read the user's unit and
/// direct roles back. Success is: in the unit, holding that role, holding no other direct role. Anything else, or a write
/// or read-back that throws, is a placement failure: ONE Critical alert line for that user, the run is unsuccessful, and
/// the next tick tries again (a user moved but left without the role is picked up as "assign the role"). A successful run
/// logs no Critical line.</para>
///
/// <para><b>Writes are off by default.</b> <see cref="WritesEnabledConfigKey"/> is named positively, so an absent, empty
/// or unparseable value is REPORT-ONLY: nothing is written and the result lists the ids it would move or give the role
/// to. Turning it on is an owner go per environment (design 3.19 step 7, task 191i).</para>
///
/// <para><b>Fail closed.</b> Every read happens before any write. If one fails, nothing is written this run, the
/// heartbeat says <c>error</c>, and the job throws (ADR-036 A1 rule 4) so the scheduler retries. If the unit or the role
/// cannot be resolved to exactly one, nobody is moved (a move without the role would leave the user with none); with
/// writes on that is an alert.</para>
///
/// <para><b>ADR-036 A1.</b> One dispatch per schedule comes from the host's lease. Rule 3: every write is decided from a
/// fresh read of the user and repeats as a no-op once done (a placed user is not a candidate), so no claim row is kept.
/// Rule 4: a read failure throws; a per-user failure is counted and retried on the next tick. Rule 5: one heartbeat per
/// attempt. Rule 6: <c>AddScheduledJob</c> in <c>ExternalAccessModule</c>. At most <see cref="MaxPlacementsPerRun"/>
/// users are written per run; the rest wait for the next tick (reported).</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): the BFF's in-process <c>Spaarke.Scheduling</c> host, beside the other
/// access reconciliation jobs. B2 (it reuses the census reads and the BFF's Dataverse seam), B3 (a handful of reads per
/// tick, the BFF's own application user, which already holds System Administrator, so no new right: ADR-028). No F-signal
/// is material. Logs and results carry systemuser ids and counts only, never a name, email or UPN.</para>
/// </remarks>
public sealed class FlaggedUserPlacementJob : IScheduledJob
{
    /// <summary>Stable job id.</summary>
    public const string JobIdConstant = "flagged-user-placement";

    /// <summary>Every 2 minutes (design 3.13). Must match <see cref="TickInterval"/> (a test pins the two together).</summary>
    internal const string DefaultCronSchedule = "*/2 * * * *";

    /// <summary>The time between two runs, as <see cref="DefaultCronSchedule"/> fires them.</summary>
    internal static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(2);

    /// <summary>The write switch. Absent, empty or unparseable = report-only. Turning it on is an owner go (task 191i).</summary>
    internal const string WritesEnabledConfigKey = "ExternalAccess:FlaggedUserPlacement:WritesEnabled";

    /// <summary>Users written per run. Past it the rest wait for the next tick (reported as <c>deferred</c>).</summary>
    internal const int MaxPlacementsPerRun = 25;

    /// <summary>How many ids each list in the result carries (the counts are always complete).</summary>
    internal const int MaxListedIds = 200;

    internal const string StatusOk = "ok";
    internal const string StatusFailed = "failed";
    internal const string StatusBlocked = "blocked";
    internal const string StatusError = "error";

    private const string LogPrefix = "[FLAGGED-PLACEMENT]";

    /// <summary>Access modes of an interactive person: 0 = Read-Write, 2 = Read.</summary>
    private static readonly HashSet<int> InteractiveAccessModes = [0, 2];

    /// <summary>Access mode 1 = Administrative.</summary>
    private const int AdministrativeAccessMode = 1;

    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FlaggedUserPlacementJob> _logger;

    public FlaggedUserPlacementJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<FlaggedUserPlacementJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Flagged User Placement";

    /// <inheritdoc />
    public string Description =>
        "Every 2 minutes, moves each systemuser whose stored sprk_isexternal is true into the 'External Collaborators' " +
        "business unit and assigns it the 'Spaarke External Collaborator' role from that unit, then reads both back. One-way; " +
        "never moves application users, disabled users or administrators; alerts only when a placement fails. Report-only " +
        $"unless {WritesEnabledConfigKey} is true.";

    /// <summary>Whether the job may write. Report-only unless explicitly true.</summary>
    internal bool WritesEnabled => bool.TryParse(_configuration[WritesEnabledConfigKey], out var enabled) && enabled;

    /// <summary>
    /// What to do with one flagged user, from its state as read. Pure, so the rules are pinned by tests without Dataverse.
    /// </summary>
    /// <param name="user">The user as read now.</param>
    /// <param name="unitId">The unit, or null when it could not be resolved to exactly one.</param>
    /// <param name="roleId">The role copy in the unit, or null when unresolved.</param>
    /// <param name="directRoleIds">Role copies assigned to the user directly.</param>
    /// <param name="teamRoleIds">Role copies the user holds through its teams.</param>
    /// <param name="administratorRoleIds">Every copy of System Administrator.</param>
    internal static PlacementDecision Classify(
        FlaggedUser user,
        Guid? unitId,
        Guid? roleId,
        IReadOnlyCollection<Guid> directRoleIds,
        IReadOnlyCollection<Guid> teamRoleIds,
        IReadOnlySet<Guid> administratorRoleIds)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.IsDefinitelyExternal)
            return PlacementDecision.SkipNotFlagged;
        if (user.HasApplicationId)
            return PlacementDecision.SkipApplicationUser;
        if (user.IsDisabled is null || user.AccessMode is null || user.BusinessUnitId is null)
            return PlacementDecision.SkipUnreadable;
        if (user.IsDisabled.Value)
            return PlacementDecision.SkipDisabled;
        if (user.AccessMode == AdministrativeAccessMode
            || directRoleIds.Concat(teamRoleIds).Any(administratorRoleIds.Contains))
            return PlacementDecision.SkipAdministrator;
        if (!InteractiveAccessModes.Contains(user.AccessMode.Value))
            return PlacementDecision.SkipAccessMode;
        if (unitId is null)
            return PlacementDecision.Unplaceable;
        if (user.BusinessUnitId == unitId)
        {
            if (roleId is not { } role || !directRoleIds.Contains(role))
                return PlacementDecision.AssignRole;

            // Placed means THE role only, the same test the read-back applies: a user that also holds another direct role
            // (a move that kept roles, contrary to spike S2, or a role an administrator added) is alerted on every run, never
            // counted as placed. The job does not remove roles (known limit K-3).
            return directRoleIds.All(id => id == role)
                ? PlacementDecision.Placed
                : PlacementDecision.PlacedWithOtherRoles;
        }

        return PlacementDecision.Move;
    }

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var writes = WritesEnabled;
        var report = new RunReport(writes ? "writes" : "report-only");
        Exception? readFailure = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dataverse = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            await RunAsync(dataverse, writes, report, context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            readFailure = ex;
            report.Status = StatusError;
            _logger.LogError(ex,
                "{Prefix} The directory could not be read, so NOBODY was moved this run; the scheduler retries. " +
                "attempt={Attempt} correlationId={CorrelationId}",
                LogPrefix, context.Attempt, context.CorrelationId);
        }

        var duration = _timeProvider.GetElapsedTime(started);

        // THE HEARTBEAT (ADR-036 A1 rule 5): every attempt, including one with nothing to do.
        _logger.Log(report.Status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "{Prefix} heartbeat status={Status} mode={Mode} unit={Unit} role={Role} flagged={Flagged} placed={Placed} " +
            "moved={Moved} roleAssigned={RoleAssigned} wouldMove={WouldMove} wouldAssignRole={WouldAssignRole} " +
            "unplaceable={Unplaceable} otherRoles={OtherRoles} failed={Failed} unverified={Unverified} changed={Changed} deferred={Deferred} " +
            "skipped={Skipped} unflaggedInUnit={UnflaggedInUnit} attempt={Attempt} durationMs={DurationMs} trigger={Trigger} " +
            "runId={RunId} correlationId={CorrelationId}",
            LogPrefix, report.Status, report.Mode, report.Unit, report.Role, report.Flagged, report.AlreadyPlaced,
            report.Moved.Count, report.RoleAssigned.Count, report.WouldMove.Count, report.WouldAssignRole.Count,
            report.Unplaceable.Count, report.OtherRoles.Count, report.Failed.Count, report.Unverified.Count, report.Changed, report.Deferred,
            report.Skipped.Values.Sum(), report.UnflaggedInUnit.Count, context.Attempt, (long)duration.TotalMilliseconds,
            context.Trigger, context.RunId, context.CorrelationId);

        if (readFailure is not null)
        {
            // ADR-036 A1 rule 4: a retry this tick can still place the users.
            throw new InvalidOperationException(
                "The flagged-user placement could not read the directory; nobody was moved this run.", readFailure);
        }

        return new JobRunResult(
            Success: report.Status == StatusOk,
            ErrorMessage: report.Status == StatusOk ? null : report.ErrorMessage(),
            ProcessedItems: report.Moved.Count + report.RoleAssigned.Count,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(report.ToResult(context.Attempt), ResultJsonOptions));
    }

    private async Task RunAsync(
        IGenericEntityService dataverse, bool writes, RunReport report, JobRunContext context, CancellationToken ct)
    {
        // ── Every read first. Any failure throws before anything is written. ──
        var unit = await ExternalCollaboratorPlacement.ResolveUnitAsync(dataverse, ct).ConfigureAwait(false);
        report.Unit = Describe(unit.State);
        var role = unit.State == UnitState.Found
            ? await ExternalCollaboratorPlacement.ResolveRoleAsync(dataverse, unit.UnitId!.Value, ct).ConfigureAwait(false)
            : new RoleResolution(UnitState.Missing, null);
        report.Role = unit.State == UnitState.Found ? Describe(role.State) : "not-read";

        var administrators = await ExternalCollaboratorPlacement.AdministratorRoleIdsAsync(dataverse, ct).ConfigureAwait(false);
        var flagged = await ExternalCollaboratorPlacement.ReadFlaggedUsersAsync(dataverse, [], ct).ConfigureAwait(false);
        report.Flagged = flagged.Count;

        var userIds = flagged.Select(user => user.Id).ToArray();
        var directRoles = await ExternalCollaboratorPlacement.ReadDirectRolesAsync(dataverse, userIds, ct).ConfigureAwait(false);
        var memberships = await ExternalCollaboratorPlacement.ReadMembershipsAsync(dataverse, userIds, ct).ConfigureAwait(false);
        var teamRoles = await ExternalCollaboratorPlacement
            .ReadTeamRolesAsync(dataverse, memberships.Values.SelectMany(ids => ids).ToHashSet(), ct).ConfigureAwait(false);

        if (unit.State == UnitState.Found)
        {
            report.UnflaggedInUnit.AddRange(await ExternalCollaboratorPlacement
                .ReadUnflaggedUsersInUnitAsync(dataverse, unit.UnitId!.Value, ct).ConfigureAwait(false));
        }

        // A role is only usable when it is THE one copy in a resolved unit.
        var unitId = unit.UnitId;
        var roleId = role.State == UnitState.Found ? role.RoleId : null;

        IReadOnlyList<Guid> TeamRolesOf(Guid userId) =>
            memberships.GetValueOrDefault(userId, []).SelectMany(teamId => teamRoles.GetValueOrDefault(teamId, [])).ToList();

        var pending = new List<(FlaggedUser User, PlacementDecision Decision)>();
        foreach (var user in flagged.OrderBy(u => u.Id))
        {
            var decision = Classify(user, unitId, roleId, directRoles.GetValueOrDefault(user.Id, []), TeamRolesOf(user.Id),
                administrators);
            switch (decision)
            {
                case PlacementDecision.Placed:
                    report.AlreadyPlaced++;
                    break;
                case PlacementDecision.PlacedWithOtherRoles:
                    report.OtherRoles.Add(user.Id);
                    break;
                case PlacementDecision.Move:
                case PlacementDecision.AssignRole:
                    pending.Add((user, decision));
                    break;
                case PlacementDecision.Unplaceable:
                    report.Unplaceable.Add(user.Id);
                    break;
                default:
                    report.Skip(decision);
                    break;
            }
        }

        if (report.UnflaggedInUnit.Count > 0)
        {
            // Round 115: one-way. An administrator moves these users back by hand.
            _logger.LogWarning(
                "{Prefix} {Count} user(s) in '{Unit}' no longer read sprk_isexternal = true; the job never moves anyone out, an " +
                "administrator decides. systemuserIds={Ids} correlationId={CorrelationId}",
                LogPrefix, report.UnflaggedInUnit.Count, ExternalCollaboratorPlacement.BusinessUnitName,
                string.Join(",", report.UnflaggedInUnit.Take(MaxListedIds)), context.CorrelationId);
        }

        if (report.OtherRoles.Count > 0)
        {
            // In the unit with THE role and another direct role: not placed. Alerted on every run (both modes) until an
            // administrator removes the other role; the job never removes roles.
            report.Status = StatusFailed;
            _logger.LogCritical(
                "{Prefix} ALERT {Count} flagged user(s) in '{Unit}' hold another direct role beside '{RoleName}'. After a move this " +
                "means the move did not behave as spike S2 found (escalate); otherwise an administrator added the role. The job " +
                "does not remove roles: remove the other role. systemuserIds={Ids} correlationId={CorrelationId}",
                LogPrefix, report.OtherRoles.Count, ExternalCollaboratorPlacement.BusinessUnitName,
                ExternalCollaboratorPlacement.RoleName, string.Join(",", report.OtherRoles.Take(MaxListedIds)),
                context.CorrelationId);
        }

        if (!writes)
        {
            // REPORT-ONLY: list what a write run would do. Nothing is written.
            report.WouldMove.AddRange(pending.Where(p => p.Decision == PlacementDecision.Move).Select(p => p.User.Id));
            report.WouldAssignRole.AddRange(pending.Where(p => p.Decision == PlacementDecision.AssignRole).Select(p => p.User.Id));
            if (pending.Count > 0 || report.Unplaceable.Count > 0)
            {
                _logger.LogInformation(
                    "{Prefix} REPORT-ONLY ({Key} is not true): would move {WouldMove} and assign the role to {WouldAssignRole}; " +
                    "{Unplaceable} cannot be placed (unit={Unit}, role={Role}). wouldMove={WouldMoveIds} " +
                    "wouldAssignRole={WouldAssignRoleIds} unplaceable={UnplaceableIds} correlationId={CorrelationId}",
                    LogPrefix, WritesEnabledConfigKey, report.WouldMove.Count, report.WouldAssignRole.Count,
                    report.Unplaceable.Count, report.Unit, report.Role,
                    string.Join(",", report.WouldMove.Take(MaxListedIds)),
                    string.Join(",", report.WouldAssignRole.Take(MaxListedIds)),
                    string.Join(",", report.Unplaceable.Take(MaxListedIds)), context.CorrelationId);
            }

            return;
        }

        if (unitId is null || roleId is null)
        {
            // Nobody can be placed: a move without the role would leave the user with no role at all.
            var waiting = pending.Select(p => p.User.Id).Concat(report.Unplaceable).Distinct().ToList();
            report.Unplaceable.Clear();
            report.Unplaceable.AddRange(waiting);
            if (waiting.Count > 0)
            {
                report.Status = StatusBlocked;
                _logger.LogCritical(
                    "{Prefix} ALERT {Count} flagged user(s) cannot be placed: unit '{UnitName}' is {Unit} and role '{RoleName}' " +
                    "is {Role} (exactly one of each is required; task 191d's script creates them). Nothing was written. " +
                    "systemuserIds={Ids} correlationId={CorrelationId}",
                    LogPrefix, waiting.Count, ExternalCollaboratorPlacement.BusinessUnitName, report.Unit,
                    ExternalCollaboratorPlacement.RoleName, report.Role, string.Join(",", waiting.Take(MaxListedIds)),
                    context.CorrelationId);
            }

            return;
        }

        report.Deferred = Math.Max(0, pending.Count - MaxPlacementsPerRun);
        foreach (var (user, decision) in pending.Take(MaxPlacementsPerRun))
        {
            await PlaceAsync(dataverse, user.Id, decision, unitId.Value, roleId.Value, administrators, TeamRolesOf(user.Id),
                report, context, ct).ConfigureAwait(false);
        }

        if (report.Failed.Count > 0 || report.Unverified.Count > 0)
            report.Status = StatusFailed;
    }

    /// <summary>
    /// Re-reads one user, writes only if the decision still holds, then reads the result back. One Critical line when the
    /// read-back does not show the user placed.
    /// </summary>
    private async Task PlaceAsync(
        IGenericEntityService dataverse, Guid userId, PlacementDecision decision, Guid unitId, Guid roleId,
        IReadOnlySet<Guid> administrators, IReadOnlyList<Guid> teamRoleIds, RunReport report, JobRunContext context,
        CancellationToken ct)
    {
        // ── A fresh read of THIS user, just before writing: the flag must still read true, and the rest still hold. ──
        try
        {
            var fresh = await ExternalCollaboratorPlacement.ReadUserAsync(dataverse, userId, ct).ConfigureAwait(false);
            var freshRoles = (await ExternalCollaboratorPlacement.ReadDirectRolesAsync(dataverse, [userId], ct).ConfigureAwait(false))
                .GetValueOrDefault(userId, []);
            var now = Classify(fresh, unitId, roleId, freshRoles, teamRoleIds, administrators);
            if (now != decision)
            {
                report.Changed++;
                _logger.LogInformation(
                    "{Prefix} systemuser {SystemUserId} changed since the candidate read ({Before} -> {Now}); nothing written. " +
                    "correlationId={CorrelationId}", LogPrefix, userId, decision, now, context.CorrelationId);
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Nothing was written: an unreadable user is never moved. Retried next tick.
            report.Unverified.Add(userId);
            _logger.LogError(ex,
                "{Prefix} systemuser {SystemUserId} could not be re-read before the write, so it was NOT moved; retried next " +
                "tick. correlationId={CorrelationId}", LogPrefix, userId, context.CorrelationId);
            return;
        }

        // ── The write: move (Move only), then the role from the unit. A failure skips what follows; the read-back decides. ──
        Exception? writeFailure = null;
        var stage = decision == PlacementDecision.Move ? "move" : "assign-role";
        try
        {
            if (decision == PlacementDecision.Move)
            {
                await dataverse.UpdateAsync("systemuser", userId,
                    new Dictionary<string, object> { ["businessunitid"] = new EntityReference("businessunit", unitId) }, ct)
                    .ConfigureAwait(false);
                stage = "assign-role";
            }

            await dataverse.AssociateAsync("systemuser", userId, ExternalCollaboratorPlacement.UserRoleRelationship,
                [new EntityReference("role", roleId)], ct).ConfigureAwait(false);
            stage = "read-back";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            writeFailure = ex;
        }

        // ── The read-back ──
        FlaggedUser back;
        IReadOnlyList<Guid> backRoles;
        try
        {
            back = await ExternalCollaboratorPlacement.ReadUserAsync(dataverse, userId, ct).ConfigureAwait(false);
            backRoles = (await ExternalCollaboratorPlacement.ReadDirectRolesAsync(dataverse, [userId], ct).ConfigureAwait(false))
                .GetValueOrDefault(userId, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            report.Failed.Add(userId);
            _logger.LogCritical(ex,
                "{Prefix} ALERT placement of systemuser {SystemUserId} could not be read back (failedStage={Stage}, " +
                "writeError={WriteError}); its unit and roles are UNKNOWN. Retried next tick. correlationId={CorrelationId}",
                LogPrefix, userId, writeFailure is null ? "read-back" : stage, writeFailure?.GetType().Name ?? "none",
                context.CorrelationId);
            return;
        }

        var inUnit = back.BusinessUnitId == unitId;
        var holdsRole = backRoles.Contains(roleId);
        var otherRoles = backRoles.Count(id => id != roleId);
        if (inUnit && holdsRole && otherRoles == 0)
        {
            (decision == PlacementDecision.Move ? report.Moved : report.RoleAssigned).Add(userId);
            if (writeFailure is not null)
            {
                _logger.LogWarning(writeFailure,
                    "{Prefix} systemuser {SystemUserId}: the {Stage} call failed but the read-back shows it placed. " +
                    "correlationId={CorrelationId}", LogPrefix, userId, stage, context.CorrelationId);
            }

            _logger.LogInformation(
                "{Prefix} systemuser {SystemUserId} placed ({Decision}): in unit {UnitId}, holding role {RoleId} only. " +
                "correlationId={CorrelationId}", LogPrefix, userId, decision, unitId, roleId, context.CorrelationId);
            return;
        }

        report.Failed.Add(userId);
        _logger.LogCritical(writeFailure,
            "{Prefix} ALERT placement of systemuser {SystemUserId} failed ({Decision}, failedStage={Stage}, " +
            "writeError={WriteError}): read back inUnit={InUnit} businessUnitId={BusinessUnitId} holdsExternalRole={HoldsRole} " +
            "otherDirectRoles={OtherRoles}. A user still outside the unit keeps its old reach; other direct roles after a move " +
            "mean the move did not behave as spike S2 found (escalate). Retried next tick. correlationId={CorrelationId}",
            LogPrefix, userId, decision, writeFailure is null ? "read-back" : stage, writeFailure?.GetType().Name ?? "none",
            inUnit, back.BusinessUnitId?.ToString() ?? "(unreadable)", holdsRole, otherRoles, context.CorrelationId);
    }

    private static string Describe(UnitState state) => state switch
    {
        UnitState.Found => "found",
        UnitState.Missing => "missing",
        _ => "ambiguous",
    };

    /// <summary>What one run read, decided and did.</summary>
    private sealed class RunReport(string mode)
    {
        public string Status { get; set; } = StatusOk;
        public string Mode { get; } = mode;
        public string Unit { get; set; } = "not-read";
        public string Role { get; set; } = "not-read";
        public int Flagged { get; set; }
        public int AlreadyPlaced { get; set; }
        public int Changed { get; set; }
        public int Deferred { get; set; }
        public List<Guid> Moved { get; } = [];
        public List<Guid> RoleAssigned { get; } = [];
        public List<Guid> WouldMove { get; } = [];
        public List<Guid> WouldAssignRole { get; } = [];
        public List<Guid> Unplaceable { get; } = [];
        public List<Guid> OtherRoles { get; } = [];
        public List<Guid> Failed { get; } = [];
        public List<Guid> Unverified { get; } = [];
        public List<Guid> UnflaggedInUnit { get; } = [];
        public SortedDictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);

        public void Skip(PlacementDecision decision)
        {
            var key = decision.ToString();
            Skipped[key] = Skipped.GetValueOrDefault(key) + 1;
        }

        public string ErrorMessage() => Status switch
        {
            StatusBlocked => $"{Unplaceable.Count} flagged user(s) cannot be placed: unit {Unit}, role {Role}.",
            StatusError => "The directory could not be read; nobody was moved.",
            _ => $"{Failed.Count} placement(s) failed, {Unverified.Count} user(s) could not be re-read and {OtherRoles.Count} placed user(s) hold another direct role; retried next tick.",
        };

        public object ToResult(int attempt) => new
        {
            status = Status,
            mode = Mode,
            unit = Unit,
            role = Role,
            flagged = Flagged,
            alreadyPlaced = AlreadyPlaced,
            moved = Ids(Moved),
            roleAssigned = Ids(RoleAssigned),
            wouldMove = Ids(WouldMove),
            wouldAssignRole = Ids(WouldAssignRole),
            unplaceable = Ids(Unplaceable),
            otherRoles = Ids(OtherRoles),
            failed = Ids(Failed),
            unverified = Ids(Unverified),
            unflaggedInUnit = Ids(UnflaggedInUnit),
            changed = Changed,
            deferred = Deferred,
            skipped = Skipped,
            attempt,
        };

        private static object Ids(List<Guid> ids) => new { count = ids.Count, ids = ids.Take(MaxListedIds).ToArray() };
    }
}

/// <summary>What <see cref="FlaggedUserPlacementJob.Classify"/> decides for one flagged user.</summary>
internal enum PlacementDecision
{
    /// <summary>In the unit, holding the role and no other direct role: nothing to do.</summary>
    Placed,

    /// <summary>In the unit, holding the role AND another direct role: not placed; alerted every run, never repaired.</summary>
    PlacedWithOtherRoles,

    /// <summary>Outside the unit: move it, then assign the role.</summary>
    Move,

    /// <summary>In the unit without the role (a move whose role step failed): assign the role.</summary>
    AssignRole,

    /// <summary>Eligible, but the unit could not be resolved to exactly one.</summary>
    Unplaceable,

    /// <summary>The flag does not read true now.</summary>
    SkipNotFlagged,

    /// <summary>An application user (the BFF's own identities included).</summary>
    SkipApplicationUser,

    /// <summary>A disabled user.</summary>
    SkipDisabled,

    /// <summary>System Administrator (direct or through a team) or the Administrative access mode.</summary>
    SkipAdministrator,

    /// <summary>A support, non-interactive or unknown access mode.</summary>
    SkipAccessMode,

    /// <summary>Its unit, disabled state or access mode was not returned.</summary>
    SkipUnreadable,
}
