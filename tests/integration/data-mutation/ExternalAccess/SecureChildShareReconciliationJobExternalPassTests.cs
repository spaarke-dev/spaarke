using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191c, strengthened for the <c>mutation-nightly</c> quality alert (#1821): what
/// <see cref="SecureChildShareReconciliationJob"/> REPORTS and REMEMBERS in its External-team pass, over the REAL
/// <see cref="ExternalTeamShareSynchronizer"/> and the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) that
/// <see cref="ExternalTeamShareTests"/> drives. Those tests assert which shares the engine writes; these pin the contract an
/// operator and the NEXT run act on:
/// <list type="bullet">
/// <item>the run result: <c>Success</c>, the <c>externalTeam</c> status, every count and the failure text, and the heartbeat's level;</item>
/// <item>the per-instance state between runs: the sweep (one slice a run within a time and page budget, held on a failed page,
/// restarted an hour after the last one started), the carried rows (five runs, then left to the sweep; capped), the family walks
/// that stopped short (walked again three times, then left to the sweep; capped) and the recent-changes position (held at a capped
/// table, then released);</item>
/// <item>"never throws for a Dataverse fault": a listing or sweep fault is contained whatever else is cancelled; the caller's own
/// cancellation ends the pass where it surfaced.</item>
/// </list>
/// Placement: <c>tests/integration/data-mutation/**</c> — every scenario decides which share rows get written and when. The
/// visibility predicate is the one substituted seam, as in <see cref="ExternalTeamShareTests"/>; the arranges are inherently
/// multi-record (a world of rows in different states), which is acceptable at this KEEP path.
/// </summary>
public class SecureChildShareReconciliationJobExternalPassTests
{
    private const int Read = 1;
    private const int ReadAppendTo = 17;
    private const string Event = "sprk_event";
    private const string MatterTable = "sprk_matter";

    private static readonly Guid ExternalBu = Guid.Parse("e1821b00-0000-4000-8000-000000000001");
    private static readonly Guid ExternalTeam = Guid.Parse("e1821b00-0000-4000-8000-000000000002");
    private static readonly Guid DirectBu = Guid.Parse("e1821b00-0000-4000-8000-000000000003");
    private static readonly Guid DirectTeam = Guid.Parse("e1821b00-0000-4000-8000-000000000004");
    private static readonly Guid Partner = Guid.Parse("e1821b00-0000-4000-8000-00000000000a");
    private static readonly Guid Matter = Guid.Parse("e1821b00-0000-4000-8000-0000000000a1");

    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A change inside the first run's look-back window: the recent-changes pass lists it.</summary>
    private static readonly DateTime RecentlyChanged = (T0 - TimeSpan.FromMinutes(10)).UtcDateTime;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly FakeTimeProvider _time = new(T0);
    private readonly CapturingLogger<Job> _jobLog = new();
    private readonly CapturingLogger<ExternalTeamShareSynchronizer> _engineLog = new();
    private readonly Visibility _visibility = new();
    private readonly List<QueryExpression> _queries = new();
    private readonly CancellationTokenSource _caller = new();

    private bool _reown;
    private bool _writes = true;

    /// <summary>Answers (or throws for) a query before the world does; null lets the world answer.</summary>
    private Func<QueryExpression, EntityCollection?>? _intercept;

    /// <summary>The first this-many reads of the Direct-message owner team throw (the pass's prerequisite fails: Failed).</summary>
    private int _directTeamFaults;

    private int _directTeamReads;
    private Guid _lastRunId;

    public SecureChildShareReconciliationJobExternalPassTests()
    {
        _world.Add("businessunit", ExternalBu, ("name", "External Collaborators"),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(ExternalTeam, ExternalBu, "External Collaborators", isDefault: true, teamType: 0);
        _world.Add("systemuser", Partner, ("businessunitid", new EntityReference("businessunit", ExternalBu)), ("sprk_isexternal", true));
        _world.OrdinaryRoot(MatterTable, Matter);
        // The matter's own team share is in line, so it never counts as a grant.
        _shares.Seed(MatterTable, Matter, DataversePrincipalRef.Team(ExternalTeam), ReadAppendTo);
    }

    // ── The job, as the host composes it ────────────────────────────────────────────────────────────────────────────

    private Job NewJob(bool withEngine = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
            [ExternalTeamShareSynchronizer.WritesEnabledConfigKey] = _writes ? "true" : "false",
            [ExternalTeamShareSynchronizer.ReownWritesEnabledConfigKey] = _reown ? "true" : "false",
        }).Build();

        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                lock (_queries)
                    _queries.Add(query);
                return Task.FromResult(_intercept?.Invoke(query) ?? _world.Answer(query));
            });

        var engine = new ExternalTeamShareSynchronizer(
            entities.Object, _shares, _visibility.Decide, configuration, _engineLog,
            ownership: new RecordOwnershipResolver(entities.Object, configuration,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordOwnershipResolver>.Instance))
        {
            DirectTeamIdentity = _ => ++_directTeamReads <= _directTeamFaults
                ? throw new InvalidOperationException("Test: the Direct-message owner team could not be read.")
                : Task.FromResult(new DirectMessageOwnerTeamIdentity(DirectBu, DirectTeam, null, null)),
        };
        var services = new ServiceCollection()
            .AddSingleton(new SecureChildShareSynchronizer(entities.Object, _shares, SecureChildShareWorld.NobodyWalled(),
                configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<SecureChildShareSynchronizer>.Instance,
                withEngine ? engine : null));
        if (withEngine)
            services.AddSingleton(engine);

        return new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _time, _jobLog);
    }

    private Task<JobRunResult> RunAsync(Job job)
    {
        _lastRunId = Guid.NewGuid();
        return job.ExecuteAsync(
            new JobRunContext(_lastRunId, "corr-1821b", JobRunTrigger.Scheduled, new Dictionary<string, object>()), _caller.Token);
    }

    private async Task<(JobRunResult Run, JsonElement External)> RunExternalAsync(Job job)
    {
        var run = await RunAsync(job);
        return (run, External(run));
    }

    private static JsonElement External(JobRunResult run) =>
        JsonDocument.Parse(run.ResultJson!).RootElement.GetProperty("externalTeam").Clone();

    private static int Int(JsonElement external, string property) => external.GetProperty(property).GetInt32();

    private static string Status(JsonElement external) => external.GetProperty("status").GetString()!;

    // ── The world ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static Guid Row(int n) => new($"e1821b10-0000-4000-8000-{n:D12}");

    private static (string, object) FiledUnderTheMatter => ("sprk_regardingmatter", new EntityReference(MatterTable, Matter));

    /// <summary>An ordinary event filed under the matter, changed at <paramref name="modifiedOn"/> (never, for null: only the
    /// sweep reaches it).</summary>
    private Guid FiledEvent(int n, DateTime? modifiedOn = null)
    {
        var id = Row(n);
        _world.OrdinaryChild(Event, id, ("sprk_regardingmatter", MatterTable, Matter));
        if (modifiedOn is { } at)
            _world.Modified(Event, id, at);
        return id;
    }

    private bool TeamHolds(string table, Guid id, int mask) =>
        _shares.MaskOf(table, id, DataversePrincipalRef.Team(ExternalTeam)) == mask;

    private static EntityCollection Page(IEnumerable<Entity> entities) => new(entities.ToList());

    // ── Query shapes the engine sends (the fault and synthetic-page seams recognise them) ─────────────────────────────

    /// <summary>The sweep's page read: ids only, ordered by id, limited, no filter group.</summary>
    private static bool IsSweepPage(QueryExpression q) =>
        q.TopCount is > 0 && q.Criteria.Filters.Count == 0 && !q.ColumnSet.AllColumns && q.ColumnSet.Columns.Count == 0
        && q.Orders.Count == 1 && q.Orders[0].AttributeName == q.EntityName + "id";

    /// <summary>The recent-changes listing: ordered by (modifiedon, id), limited per table.</summary>
    private static bool IsRecentListing(QueryExpression q) =>
        q.TopCount == Job.MaxExternalListedPerTable && q.Orders.Count == 2 && q.Orders[0].AttributeName == "modifiedon";

    /// <summary>The downward family walk: ids only, one <c>Or</c> group of <c>In</c> conditions, not limited.</summary>
    private static bool IsFamilyWalk(QueryExpression q) =>
        q.TopCount is null && !q.ColumnSet.AllColumns && q.ColumnSet.Columns.Count == 0
        && q.Criteria.Filters.Any(f => f.FilterOperator == LogicalOperator.Or && f.Conditions.Any(c => c.Operator == ConditionOperator.In));

    private int SweepPageReads(string? table = null)
    {
        lock (_queries)
            return _queries.Count(q => IsSweepPage(q) && (table is null || q.EntityName == table));
    }

    private int FamilyWalks()
    {
        lock (_queries)
            return _queries.Count(IsFamilyWalk);
    }

    /// <summary>The sweep reads <paramref name="total"/> matters that exist only as ids (no row behind them: they decide nothing),
    /// <see cref="Job.ExternalSweepPageSize"/> a page, advancing the clock by <paramref name="perPage"/> as each page is read.</summary>
    private void SweepOverSyntheticMatters(int total, TimeSpan perPage = default)
    {
        _intercept = q =>
        {
            if (!IsSweepPage(q) || q.EntityName != MatterTable)
                return null;
            var after = q.Criteria.Conditions
                .Where(c => c.AttributeName == MatterTable + "id" && c.Operator == ConditionOperator.GreaterThan)
                .Select(c => (Guid)c.Values.Single()).DefaultIfEmpty(Guid.Empty).Single();
            var first = after == Guid.Empty ? 1 : SyntheticOrdinal(after) + 1;
            _time.Advance(perPage);
            return Page(Enumerable.Range(first, Math.Max(0, Math.Min(q.TopCount!.Value, total - first + 1)))
                .Select(i => new Entity(MatterTable, SyntheticId(i)) { Id = SyntheticId(i) }));
        };
    }

    private static Guid SyntheticId(int ordinal) => new(ordinal, 0, 0, new byte[8]);

    private static int SyntheticOrdinal(Guid id) => BitConverter.ToInt32(id.ToByteArray(), 0);

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The result and the heartbeat
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_WithoutTheEngine_ReportsUnavailable_Succeeds_AndWarnsOncePerInstance()
    {
        var job = NewJob(withEngine: false);

        var (first, firstExternal) = await RunExternalAsync(job);
        var firstRunId = _lastRunId;
        var (second, secondExternal) = await RunExternalAsync(job);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        Status(firstExternal).Should().Be("unavailable");
        Status(secondExternal).Should().Be("unavailable");
        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().ContainSingle(
                "a host without the engine says so once per instance, not every two minutes").Which
            .Properties["RunId"].Should().Be(firstRunId);
        _jobLog.Matching(LogLevel.Information, "[EXTERNAL-TEAM-SHARE] heartbeat").Should().HaveCount(2,
            "a pass that is not composed in this host is clean");
        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] heartbeat").Should().BeEmpty();

        await RunAsync(NewJob(withEngine: false));
        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().HaveCount(2, "another instance warns again");
    }

    [Fact]
    public async Task ExecuteAsync_ACleanRun_ReportsOk_SharesTheVisibleRows_AndLogsAnInformationHeartbeat()
    {
        var shared = FiledEvent(1);
        var job = NewJob();

        var (run, external) = await RunExternalAsync(job);

        run.Success.Should().BeTrue();
        run.ErrorMessage.Should().BeNull();
        Status(external).Should().Be("ok");
        TeamHolds(Event, shared, ReadAppendTo).Should().BeTrue();
        Int(external, "teamGrants").Should().Be(1);
        Int(external, "written").Should().Be(1);
        Int(external, "failures").Should().Be(0);
        Int(external, "carried").Should().Be(0);
        external.GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
        external.GetProperty("sweepPassComplete").GetBoolean().Should().BeTrue();
        external.GetProperty("writesEnabled").GetBoolean().Should().BeTrue();
        external.GetProperty("reownWritesEnabled").GetBoolean().Should().BeFalse();
        var heartbeat = _jobLog.Single(LogLevel.Information, "[EXTERNAL-TEAM-SHARE] heartbeat", ("Status", "ok"), ("RunId", _lastRunId));
        heartbeat.Properties["SweepPassComplete"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_ARunThatLeavesARowUndecided_IsIncomplete_ButSucceeds_WithAWarningHeartbeat()
    {
        var undecided = FiledEvent(1);
        _visibility.Undecided(Event, undecided);

        var (run, external) = await RunExternalAsync(NewJob());

        run.Success.Should().BeTrue("a row carried only for an Undecided answer is not a failure: it would hold the job incomplete for ever");
        Status(external).Should().Be("incomplete");
        Int(external, "undecided").Should().Be(1);
        Int(external, "carried").Should().Be(1);
        _jobLog.Single(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] heartbeat", ("Status", "incomplete"), ("Carried", 1));
        _jobLog.Matching(LogLevel.Information, "[EXTERNAL-TEAM-SHARE] heartbeat").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "listing-failed")]
    [InlineData(true, "failed")]
    public async Task ExecuteAsync_WhenTheListingOrAPrerequisiteFails_FailsTheJobWithNoCarriedFault(bool prerequisite, string status)
    {
        FiledEvent(1, RecentlyChanged);
        if (prerequisite)
            _directTeamFaults = int.MaxValue;
        else
            _intercept = q => IsRecentListing(q) ? throw new InvalidOperationException("Test: the recent changes could not be listed.") : null;

        var (run, external) = await RunExternalAsync(NewJob());

        Status(external).Should().Be(status);
        Int(external, "failures").Should().Be(0, "the status alone fails the job, not a counted fault");
        run.Success.Should().BeFalse();
        run.ErrorMessage.Should().Contain($"External team: {status}");
        _jobLog.Single(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] heartbeat", ("Status", status));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAWriteDoesNotReadBack_CountsEveryFaultAcrossTheRecentAndSweepPasses()
    {
        FiledEvent(1, RecentlyChanged);
        FiledEvent(2);
        _shares.IgnoreWrites = true;

        var (run, external) = await RunExternalAsync(NewJob());

        run.Success.Should().BeFalse();
        // The recent pass fails the first event; the sweep then decides both events again (neither share took).
        Int(external, "failures").Should().Be(3);
        Int(external, "carried").Should().Be(2, "each row is carried once, however many passes decided it");
        Int(external, "written").Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Each pass adds its own counts
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every count of the pass is the SUM of the passes that ran. Each state is arranged twice — a row the recent-changes pass
    /// lists (changed ten minutes before the run) and a row only the sweep reaches — so the recent pass and the sweep each report
    /// it once, and the totals say two.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ReportsTheSumOfTheRecentAndSweepPasses_ForEveryCount()
    {
        _reown = true;
        var partner = DataversePrincipalRef.User(Partner);
        var team = DataversePrincipalRef.Team(ExternalTeam);
        var next = 1;

        void Pair(Action<Guid, DateTime?> arrange)
        {
            arrange(Row(next++), RecentlyChanged);
            arrange(Row(next++), null);
        }

        // Visible, no share yet: a grant.
        Pair((id, at) => FiledEventAt(id, at));
        // Visible, the team's share at the wrong mask: a change.
        Pair((id, at) =>
        {
            FiledEventAt(id, at);
            _shares.Seed(Event, id, team, Read);
        });
        // Hidden, the team holds a share: a revoke.
        Pair((id, at) =>
        {
            FiledEventAt(id, at);
            _shares.Seed(Event, id, team, ReadAppendTo);
            _visibility.Hides(Event, id);
        });
        // Hidden, a flagged user holds a share: a flagged-user revoke.
        Pair((id, at) =>
        {
            FiledEventAt(id, at);
            _shares.Seed(Event, id, partner, Read);
            _visibility.Hides(Event, id);
        });
        // Visible, owned by a flagged user, the team share in line: a re-own to the parent's team.
        Pair((id, at) =>
        {
            _world.Add(Event, id, ("owninguser", new EntityReference("systemuser", Partner)), FiledUnderTheMatter);
            Stamp(id, at);
            _shares.Seed(Event, id, team, ReadAppendTo);
        });
        // Owned by the External team itself: the census alarm.
        Pair((id, at) =>
        {
            _world.Add(Event, id, ("owningteam", new EntityReference("team", ExternalTeam)), FiledUnderTheMatter);
            Stamp(id, at);
            _shares.Seed(Event, id, team, ReadAppendTo);
        });
        // A hidden message the Direct team owns, a flagged participant holds a share: left to task 198's writer, reported.
        Pair((id, at) =>
        {
            _world.Add("sprk_communication", id, ("owningteam", new EntityReference("team", DirectTeam)));
            Stamp("sprk_communication", id, at);
            _shares.Seed("sprk_communication", id, partner, Read);
            _visibility.Says("sprk_communication", id, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.CommunicationHidden);
        });
        // The predicate cannot decide: carried.
        Pair((id, at) =>
        {
            FiledEventAt(id, at);
            _visibility.Undecided(Event, id);
        });

        var (run, external) = await RunExternalAsync(NewJob());

        Status(external).Should().Be("incomplete");
        Int(external, "listed").Should().Be(8, "the recent pass lists the eight rows changed inside the window");
        Int(external, "swept").Should().Be(17, "the sweep reads the matter and all sixteen rows");
        Int(external, "examined").Should().Be(8 + 17, "the recent pass decides its eight rows, the sweep decides all seventeen again");
        Int(external, "teamGrants").Should().Be(2);
        Int(external, "teamChanges").Should().Be(2);
        Int(external, "teamRevokes").Should().Be(2);
        Int(external, "flaggedUserRevokes").Should().Be(2);
        Int(external, "reowns").Should().Be(2);
        // A state that persists is reported by every pass that sees it: the recent pass its row, the sweep both.
        Int(external, "externalTeamOwned").Should().Be(1 + 2);
        Int(external, "directOwnedFlaggedShares").Should().Be(1 + 2);
        Int(external, "undecided").Should().Be(1 + 2);
        Int(external, "written").Should().Be(2 + 2 + 2 + 2);
        Int(external, "reowned").Should().Be(2);
        Int(external, "carried").Should().Be(2);
        Int(external, "failures").Should().Be(0);
        run.Success.Should().BeTrue();
        _jobLog.Single(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] heartbeat", ("Examined", 25), ("Reowned", 2));

        // The trigger of each pass is on the engine's own lines: the recent pass and the sweep each carried one row.
        _engineLog.Matching(LogLevel.Information, "{Tag} {Count} row(s) carried").Select(e => e.Properties["Trigger"])
            .Should().BeEquivalentTo(["recent", "sweep"]);
    }

    private void FiledEventAt(Guid id, DateTime? at)
    {
        _world.OrdinaryChild(Event, id, ("sprk_regardingmatter", MatterTable, Matter));
        Stamp(id, at);
    }

    private void Stamp(Guid id, DateTime? at) => Stamp(Event, id, at);

    private void Stamp(string table, Guid id, DateTime? at)
    {
        if (at is { } when)
            _world.Modified(table, id, when);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The sweep: when it runs, how far one run reads, where it resumes
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_TheSweepRunsAtInstanceStart_ThenAnHourAfterTheLastOneStarted_NeverBefore()
    {
        FiledEvent(1);
        var job = NewJob();

        var first = (await RunExternalAsync(job)).External;
        Int(first, "swept").Should().BeGreaterThan(0, "the sweep runs at instance start");
        first.GetProperty("sweepPassComplete").GetBoolean().Should().BeTrue();

        foreach (var offset in new[] { TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(30), Job.ExternalSweepInterval - TimeSpan.FromSeconds(1) })
        {
            _time.SetUtcNow(T0 + offset);
            var between = (await RunExternalAsync(job)).External;
            Int(between, "swept").Should().Be(0, $"{offset} after the sweep started it is not an hour yet");
            between.GetProperty("sweepPassComplete").GetBoolean().Should().BeFalse();
        }

        _time.SetUtcNow(T0 + Job.ExternalSweepInterval);
        var hourly = (await RunExternalAsync(job)).External;
        Int(hourly, "swept").Should().BeGreaterThan(0, "an hour after the previous sweep started");
        hourly.GetProperty("sweepPassComplete").GetBoolean().Should().BeTrue();

        // The next one counts from THIS sweep's start, not from the first.
        _time.SetUtcNow(T0 + Job.ExternalSweepInterval + TimeSpan.FromMinutes(2));
        Int((await RunExternalAsync(job)).External, "swept").Should().Be(0);
        _time.SetUtcNow(T0 + 2 * Job.ExternalSweepInterval - TimeSpan.FromSeconds(1));
        Int((await RunExternalAsync(job)).External, "swept").Should().Be(0);
        _time.SetUtcNow(T0 + 2 * Job.ExternalSweepInterval);
        Int((await RunExternalAsync(job)).External, "swept").Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_ASweepLongerThanOnePage_ReadsEveryPageInTheRun_AndIsCompleteOnTheLast()
    {
        SweepOverSyntheticMatters(total: 1200);
        var job = NewJob();

        var (_, first) = await RunExternalAsync(job);

        Int(first, "swept").Should().Be(1200, "three pages: 500, 500 and 200, then the sweep has ended");
        first.GetProperty("sweepPassComplete").GetBoolean().Should().BeTrue();
        SweepPageReads(MatterTable).Should().Be(3, "a finished sweep is not started again inside the same run");
        _time.Advance(TimeSpan.FromMinutes(2));
        Int((await RunExternalAsync(job)).External, "swept").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ASweepPageThatFails_HoldsTheCursor_SoTheNextRunReadsThePageAgain()
    {
        SweepOverSyntheticMatters(total: 1200);
        _directTeamFaults = 1; // the first pass of the run cannot read its prerequisite: Failed
        var job = NewJob();

        var (run, first) = await RunExternalAsync(job);

        Status(first).Should().Be("failed");
        run.Success.Should().BeFalse();
        Int(first, "swept").Should().Be(500, "the failed page ends the run's sweep");
        first.GetProperty("sweepPassComplete").GetBoolean().Should().BeFalse();
        first.GetProperty("failure").GetString().Should().Contain("prerequisite");

        _time.Advance(TimeSpan.FromMinutes(2));
        var (_, second) = await RunExternalAsync(job);

        Int(second, "swept").Should().Be(1200, "the cursor was held, so the sweep starts again from its first page");
        second.GetProperty("sweepPassComplete").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ASweepStopsStartingPages_OnceTheRunsTimeBudgetIsSpent_AndResumesNextRun()
    {
        SweepOverSyntheticMatters(total: 1600, perPage: Job.ExternalSweepBudget);
        var job = NewJob();

        var swept = new List<int>();
        var complete = new List<bool>();
        for (var run = 0; run < 4; run++)
        {
            var (_, external) = await RunExternalAsync(job);
            swept.Add(Int(external, "swept"));
            complete.Add(external.GetProperty("sweepPassComplete").GetBoolean());
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        swept.Should().Equal([500, 500, 500, 100], "a page that ends exactly on the budget is the last one of the run");
        complete.Should().Equal([false, false, false, true]);
    }

    [Fact]
    public async Task ExecuteAsync_ASweepReadsAtMostTwentyPagesPerRun()
    {
        SweepOverSyntheticMatters(total: 100_000);

        var (_, external) = await RunExternalAsync(NewJob());

        Int(external, "swept").Should().Be(Job.MaxExternalSweepPagesPerRun * Job.ExternalSweepPageSize);
        external.GetProperty("sweepPassComplete").GetBoolean().Should().BeFalse();
        SweepPageReads(MatterTable).Should().Be(Job.MaxExternalSweepPagesPerRun);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Carried rows and walks that stopped short
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ARowTheVerdictCannotDecide_IsCarriedFiveRuns_ThenLeftToTheHourlySweep()
    {
        var undecided = FiledEvent(1);
        _visibility.Undecided(Event, undecided);
        var job = NewJob();

        for (var run = 1; run <= Job.MaxExternalCarriedRuns; run++)
        {
            var (_, external) = await RunExternalAsync(job);
            Int(external, "carriedIn").Should().Be(run == 1 ? 0 : 1);
            Int(external, "carried").Should().Be(1, $"run {run}: the row is carried while it has been carried {Job.MaxExternalCarriedRuns} runs or fewer");
            Int(external, "leftToSweep").Should().Be(0);
            Status(external).Should().Be("incomplete");
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().BeEmpty();
        var (_, sixth) = await RunExternalAsync(job);

        Int(sixth, "carriedIn").Should().Be(1);
        Int(sixth, "carried").Should().Be(0, "the sixth run's answer is the row's sixth: it is left to the sweep");
        Int(sixth, "leftToSweep").Should().Be(1);
        Status(sixth).Should().Be("ok");
        _jobLog.Single(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=",
            ("RunId", _lastRunId), ("Count", 1), ("Runs", Job.MaxExternalCarriedRuns), ("Cap", Job.MaxExternalCarried));

        _time.Advance(TimeSpan.FromMinutes(2));
        var (_, seventh) = await RunExternalAsync(job);
        Int(seventh, "carriedIn").Should().Be(0, "the row is no longer carried");
        Int(seventh, "leftToSweep").Should().Be(0);
        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().ContainSingle("only the run that dropped the row warns");

        // The engine's own line names the pass that decided the rows.
        _engineLog.Matching(LogLevel.Information, "{Tag} {Count} row(s) carried").Select(e => (string)e.Properties["Trigger"]!)
            .Should().Contain(["sweep", "carried"]);
    }

    [Fact]
    public async Task ExecuteAsync_MoreRowsCarriedThanTheCap_KeepsTheCap_AndLeavesTheRestToTheSweep()
    {
        const int total = Job.MaxExternalCarried + 1;
        _visibility.DefaultAnswer = (ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed);
        for (var i = 1; i <= total; i++)
            _world.OrdinaryRoot(MatterTable, Row(1000 + i));

        var (_, external) = await RunExternalAsync(NewJob());

        Int(external, "carried").Should().Be(Job.MaxExternalCarried);
        Int(external, "leftToSweep").Should().Be(total + 1 - Job.MaxExternalCarried, "the 2,001 matters and the standing one: the rest are left to the sweep");
        _jobLog.Single(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=", ("RunId", _lastRunId), ("Count", 2), ("Cap", Job.MaxExternalCarried));
    }

    [Fact]
    public async Task ExecuteAsync_AFamilyWalkThatStopsShort_IsWalkedAgainNextRun_AndBringsTheFamilyIntoLine()
    {
        var matter = Row(1);
        var child = Row(3);
        _world.OrdinaryRoot(MatterTable, matter).Modified(MatterTable, matter, RecentlyChanged);
        _world.OrdinaryChild(Event, child, ("sprk_regardingmatter", MatterTable, matter));
        _intercept = q => IsFamilyWalk(q) ? throw new InvalidOperationException("Test: the rows filed under the matter could not be read.") : null;
        var job = NewJob();

        var (first, firstExternal) = await RunExternalAsync(job);

        first.Success.Should().BeFalse("a walk that read nothing is a fault");
        Int(firstExternal, "failures").Should().Be(1);
        Int(firstExternal, "carried").Should().Be(1, "the changed matter is walked again");
        Status(firstExternal).Should().Be("incomplete");
        TeamHolds(Event, child, ReadAppendTo).Should().BeTrue("the instance-start sweep shared the event");

        // The family drifts after the sweep, and the read recovers: only a walk from the matter reaches the event.
        _visibility.Hides(Event, child);
        _intercept = null;
        var walksBefore = FamilyWalks();
        _time.Advance(TimeSpan.FromMinutes(2));
        var (second, secondExternal) = await RunExternalAsync(job);

        FamilyWalks().Should().BeGreaterThan(walksBefore, "the walk is made again");
        TeamHolds(Event, child, ReadAppendTo).Should().BeFalse("the walked family was brought into line: the hidden event lost its share");
        second.Success.Should().BeTrue();
        Status(secondExternal).Should().Be("ok");
        _engineLog.Matching(LogLevel.Warning, "{Tag} the rows filed under").Select(e => e.Properties["Trigger"])
            .Should().Equal(["recent"], "the one walk that stopped short");

        // A walk that completed is not remembered: nothing is walked in the run after it.
        _time.Advance(TimeSpan.FromMinutes(2));
        var walksAfterSecond = FamilyWalks();
        var (_, third) = await RunExternalAsync(job);
        FamilyWalks().Should().Be(walksAfterSecond);
        Int(third, "carried").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_AFamilyWalkThatKeepsStoppingShort_IsWalkedAgainThreeTimes_ThenLeftToTheSweep()
    {
        var matter = Row(1);
        _world.OrdinaryRoot(MatterTable, matter).Modified(MatterTable, matter, RecentlyChanged);
        _intercept = q => IsFamilyWalk(q) ? throw new InvalidOperationException("Test: the rows filed under the matter could not be read.") : null;
        var job = NewJob();

        // Run 1 walks the changed matter (the recent pass); runs 2 to 4 walk it again.
        for (var run = 1; run <= Job.MaxExternalWalkAttempts; run++)
        {
            var walksBefore = FamilyWalks();
            var (_, external) = await RunExternalAsync(job);
            FamilyWalks().Should().Be(walksBefore + 1, $"run {run} makes one walk (a read that faults ends it)");
            Int(external, "carried").Should().Be(1, $"run {run}");
            Int(external, "leftToSweep").Should().Be(0);
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().BeEmpty();
        var beforeLast = FamilyWalks();
        var (_, last) = await RunExternalAsync(job);

        FamilyWalks().Should().Be(beforeLast + 1, "the walk is made a fourth time");
        Int(last, "carried").Should().Be(0, "the fourth attempt is the one past the cap");
        Int(last, "leftToSweep").Should().Be(1);
        _jobLog.Single(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=", ("RunId", _lastRunId), ("Count", 1));

        _time.Advance(TimeSpan.FromMinutes(2));
        var beforeNext = FamilyWalks();
        await RunExternalAsync(job);
        FamilyWalks().Should().Be(beforeNext, "a walk left to the sweep is not made again");
        _engineLog.Matching(LogLevel.Warning, "{Tag} the rows filed under").Select(e => e.Properties["Trigger"])
            .Should().Equal(["recent", "walk-again", "walk-again", "walk-again"]);
    }

    [Fact]
    public async Task ExecuteAsync_MoreChangedRowsThanTheCap_KeepsTheCapOfWalks_AndLeavesTheRestToTheSweep()
    {
        // Five tables list their cap of changed rows (ids only: nothing is behind them), and every walk faults.
        var tables = ExternalTeamShareSynchronizer.LineageTables.Take(5).ToList();
        _intercept = q =>
        {
            if (IsFamilyWalk(q))
                throw new InvalidOperationException("Test: the rows filed under the changed rows could not be read.");
            if (!IsRecentListing(q) || !tables.Contains(q.EntityName))
                return null;
            var seed = tables.IndexOf(q.EntityName) * 1000;
            return Page(Enumerable.Range(1, Job.MaxExternalListedPerTable).Select(i =>
                new Entity(q.EntityName, SyntheticId(seed + i)) { Id = SyntheticId(seed + i), ["modifiedon"] = RecentlyChanged }));
        };

        var (_, external) = await RunExternalAsync(NewJob());

        Int(external, "listed").Should().Be(5 * Job.MaxExternalListedPerTable);
        Int(external, "carried").Should().Be(Job.MaxExternalCarried);
        Int(external, "leftToSweep").Should().Be(5 * Job.MaxExternalListedPerTable - Job.MaxExternalCarried);
        _jobLog.Single(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=",
            ("RunId", _lastRunId), ("Count", 500), ("Runs", Job.MaxExternalCarriedRuns), ("Cap", Job.MaxExternalCarried));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The recent-changes position
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ATableOverTheListingCap_ContinuesWhereItStopped_ThenListsFromTheWatermarkAgain()
    {
        _writes = false;
        const int total = Job.MaxExternalListedPerTable + 1;
        for (var i = 1; i <= total; i++)
            FiledEvent(i, RecentlyChanged.AddSeconds(i));
        var job = NewJob();

        var listed = new List<int>();
        for (var run = 0; run < 3; run++)
        {
            listed.Add(Int((await RunExternalAsync(job)).External, "listed"));
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        listed.Should().Equal([Job.MaxExternalListedPerTable, 1, 0],
            "a capped table continues after its last row read; once it is read to the end the hold is released, and the rows changed " +
            "before that run's watermark are not listed again");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // A fault in a listing is contained; the caller's cancellation is not
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    public enum Phase
    {
        /// <summary>The recent-changes listing (the second run of a job: the instance-start sweep is over).</summary>
        Recent,

        /// <summary>The sweep's page read (the first run of a job).</summary>
        Sweep,
    }

    public enum Thrown
    {
        /// <summary>A Dataverse fault.</summary>
        Fault,

        /// <summary>An <see cref="OperationCanceledException"/>: a timeout when the caller has not cancelled.</summary>
        Cancellation,
    }

    [Theory]
    [InlineData(Phase.Recent, Thrown.Fault, false)]
    [InlineData(Phase.Recent, Thrown.Fault, true)]
    [InlineData(Phase.Recent, Thrown.Cancellation, false)]
    [InlineData(Phase.Sweep, Thrown.Fault, false)]
    [InlineData(Phase.Sweep, Thrown.Fault, true)]
    [InlineData(Phase.Sweep, Thrown.Cancellation, false)]
    public async Task ExecuteAsync_AListingThatFaults_IsContained_WhateverIsCancelled(Phase phase, Thrown thrown, bool callerCancels)
    {
        FiledEvent(1);
        var job = NewJob();
        if (phase == Phase.Recent)
            await RunAsync(job); // the instance-start sweep
        var armed = false;
        _intercept = q =>
        {
            if (!armed || !(phase == Phase.Recent ? IsRecentListing(q) : IsSweepPage(q)))
                return null;
            if (callerCancels)
                _caller.Cancel();
            throw thrown == Thrown.Fault
                ? new InvalidOperationException("Test: the listing faulted.")
                : new OperationCanceledException("Test: the read timed out.");
        };
        armed = true;
        _time.Advance(TimeSpan.FromMinutes(2));
        var sweepReadsBefore = SweepPageReads();

        var (run, external) = await RunExternalAsync(job);

        Status(external).Should().Be("listing-failed");
        run.Success.Should().BeFalse();
        var keyword = phase == Phase.Recent ? "recent-changes listing" : "sweep listing";
        external.GetProperty("failure").GetString().Should().Contain(keyword);
        var warning = _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().ContainSingle().Which;
        warning.Properties["RunId"].Should().Be(_lastRunId);
        warning.Exception.Should().NotBeNull("the fault is logged with its exception");
        if (phase == Phase.Sweep)
        {
            SweepPageReads().Should().Be(sweepReadsBefore + 1, "a failed sweep page ends the run's sweep");
            external.GetProperty("sweepPassComplete").GetBoolean().Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(Phase.Recent)]
    [InlineData(Phase.Sweep)]
    public async Task ExecuteAsync_WhenTheCallerCancelsDuringAListing_TheCancellationEndsTheRun(Phase phase)
    {
        FiledEvent(1);
        var job = NewJob();
        if (phase == Phase.Recent)
            await RunAsync(job);
        _intercept = q =>
        {
            if (!(phase == Phase.Recent ? IsRecentListing(q) : IsSweepPage(q)))
                return null;
            _caller.Cancel();
            throw new OperationCanceledException(_caller.Token);
        };
        _time.Advance(TimeSpan.FromMinutes(2));

        var run = () => RunAsync(job);

        await run.Should().ThrowAsync<OperationCanceledException>();
        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().BeEmpty("a cancellation is not a listing failure");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRecentListingAndTheSweepBothFail_ReportsTheFirstFailure()
    {
        FiledEvent(1);
        _intercept = q => IsRecentListing(q) || IsSweepPage(q) ? throw new InvalidOperationException("Test: the listing faulted.") : null;

        var (_, external) = await RunExternalAsync(NewJob());

        Status(external).Should().Be("listing-failed");
        external.GetProperty("failure").GetString().Should().Contain("recent-changes listing", "the sweep's failure does not replace it");
        _jobLog.Matching(LogLevel.Warning, "[EXTERNAL-TEAM-SHARE] run=").Should().HaveCount(2);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // A pass that fails: which failure the run reports
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_WhenOnlyOnePassFails_TheRunFails_AndNamesThatPassFailure()
    {
        FiledEvent(1, RecentlyChanged);
        FiledEvent(2);
        _directTeamFaults = 1; // the recent pass fails; the sweep that follows is read normally

        var (run, external) = await RunExternalAsync(NewJob());

        Status(external).Should().Be("failed");
        run.Success.Should().BeFalse();
        external.GetProperty("failure").GetString().Should().Contain("prerequisite");
        Int(external, "carried").Should().BeGreaterThan(0, "the rows of the failed pass are carried");
        run.ErrorMessage.Should().Contain("External team: failed");
        TeamHolds(Event, Row(2), ReadAppendTo).Should().BeTrue("the pass that did not fail still wrote");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAListingFailsAndAPassFails_ReportsTheListingFailure()
    {
        FiledEvent(1);
        _directTeamFaults = 1; // the sweep's pass fails
        _intercept = q => IsRecentListing(q) ? throw new InvalidOperationException("Test: the recent changes could not be listed.") : null;

        var (_, external) = await RunExternalAsync(NewJob());

        Status(external).Should().Be("listing-failed");
        external.GetProperty("failure").GetString().Should().Contain("recent-changes listing");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Doubles
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The predicate seam: Visible unless a test says otherwise; a child's root is the standing matter, a root's is itself.</summary>
    private sealed class Visibility
    {
        private readonly Dictionary<(string, Guid), (ExternalVisibilityVerdict Verdict, ExternalVisibilityReason Reason)> _answers = new();

        public (ExternalVisibilityVerdict Verdict, ExternalVisibilityReason Reason) DefaultAnswer { get; set; } =
            (ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible);

        public void Says(string table, Guid id, ExternalVisibilityVerdict verdict, ExternalVisibilityReason reason) =>
            _answers[(table, id)] = (verdict, reason);

        public void Hides(string table, Guid id) => Says(table, id, ExternalVisibilityVerdict.Hidden, ExternalVisibilityReason.RootRestricted);

        public void Undecided(string table, Guid id) => Says(table, id, ExternalVisibilityVerdict.Undecided, ExternalVisibilityReason.ReadFailed);

        public Task<ExternalVisibilityResult> Decide(IReadOnlyCollection<(string Table, Guid Id)> rows, CancellationToken ct)
        {
            var decided = new Dictionary<ExternalVisibility.Node, ExternalVisibilityDecision>();
            var reached = new Dictionary<ExternalVisibility.Node, IReadOnlyCollection<ExternalVisibility.Node>>();
            foreach (var (table, id) in rows)
            {
                var node = new ExternalVisibility.Node(table, id);
                var (verdict, reason) = _answers.TryGetValue((table, id), out var said) ? said : DefaultAnswer;
                decided[node] = new ExternalVisibilityDecision(verdict, reason, table, id);
                reached[node] = [SecureChildLineage.IsRoot(table) ? node : new ExternalVisibility.Node(MatterTable, Matter)];
            }

            return Task.FromResult(new ExternalVisibilityResult(decided, faulted: false)
            {
                RootsReached = reached,
                EffectivelySecureRoots = new HashSet<ExternalVisibility.Node>(),
            });
        }
    }

    private sealed record LogEntry(LogLevel Level, string Format, Exception? Exception, IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = new();

        public IEnumerable<LogEntry> All
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        /// <summary>The logs at <paramref name="level"/> whose template starts with <paramref name="prefix"/>.</summary>
        public IEnumerable<LogEntry> Matching(LogLevel level, string prefix) =>
            All.Where(e => e.Level == level && e.Format.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>The one log at <paramref name="level"/> whose template starts with <paramref name="prefix"/> and that carries every
        /// property in <paramref name="properties"/>.</summary>
        public LogEntry Single(LogLevel level, string prefix, params (string Key, object Value)[] properties)
        {
            var matches = Matching(level, prefix)
                .Where(e => properties.All(p => e.Properties.TryGetValue(p.Key, out var value) && Equals(value, p.Value)))
                .ToList();
            matches.Should().ContainSingle(
                $"one {level} log starting '{prefix}' with [{string.Join(", ", properties.Select(p => $"{p.Key}={p.Value}"))}] is expected; " +
                $"logged: {string.Join(" | ", All.Select(e => $"{e.Level}: {e.Format} [{string.Join(", ", e.Properties.Where(p => p.Key != "{OriginalFormat}").Select(p => $"{p.Key}={p.Value}"))}]"))}");
            return matches[0];
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = (state as IEnumerable<KeyValuePair<string, object?>>)?
                .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value)
                ?? new Dictionary<string, object?>();
            var format = properties.TryGetValue("{OriginalFormat}", out var original) ? original as string ?? "" : "";
            lock (_entries)
                _entries.Add(new LogEntry(logLevel, format, exception, properties));
        }
    }
}
