using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2731: <see cref="Job"/>'s Direct-message pass HOLDS its watermark when the carry passes <see cref="Job.MaxDirectCarried"/>.
/// The held window was listed again, so the messages already finished came back in, the carry overflowed again, and every run
/// re-reconciled the same messages while a change at the carry's tail was dropped run after run (only the rolling sweep could
/// reach it). The fix keeps the hold (moving the watermark instead lost a message whose change sits at the window's start) and
/// skips a listed message this instance finished after its latest listed change, so the carry drains.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job and Direct-team lookup; the sweep answers
/// nothing, so only the listing and the carry can reach a message. Each arrange is a burst of more than
/// <see cref="Job.MaxDirectCarried"/> + one run's budget messages across three listed tables: an inherent multi-record setup at a
/// KEEP integration path.</para>
/// </summary>
public class Issue2731_DirectCarryOverflowWatermarkTests
{
    private const int Read = 1;
    private const string Communication = "sprk_communication";
    private const string Attachment = "sprk_communicationattachment";
    private const string Participant = "sprk_communicationparticipant";

    /// <summary>The recent and carried messages one run works: everything but the sweep's reserve.</summary>
    private const int PriorityBudget = Job.MaxDirectMessagesPerRun - Job.DirectSweepReserve;

    /// <summary>One past what a run works plus what it may carry: the first run's carry overflows.</summary>
    private const int Burst = PriorityBudget + Job.MaxDirectCarried + 1;

    /// <summary>Backed-off messages the carry-order test holds ahead of the left-over tail.</summary>
    private const int DeferredCount = 3;

    /// <summary>Messages (listed) that fill the carry-order test's carry one past its cap beside the backed-off ones.</summary>
    private const int BurstOverBackedOff = PriorityBudget + Job.MaxDirectCarried - DeferredCount + 1;

    /// <summary>Under one listing's cap, so a burst spread over three tables caps none of them.</summary>
    private const int PerListing = Job.MaxDirectListedPerTable - 1;

    private static readonly Guid DirectTeam = Guid.Parse("d2731000-0000-4000-8000-0000000000d1");
    private static readonly Guid Sender = Guid.Parse("c2731000-0000-4000-8000-0000000000a1");
    private static readonly Guid Recipient = Guid.Parse("c2731000-0000-4000-8000-0000000000a2");
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Sender, Recipient);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly Dictionary<Guid, int> _participantReads = new();
    private readonly HashSet<Guid> _unreadable = new();
    private Guid _thread;

    public Issue2731_DirectCarryOverflowWatermarkTests()
    {
        var directUnit = Guid.Parse("d2731000-0000-4000-8000-0000000000b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
        DirectThread();
    }

    [Fact]
    public async Task ExecuteAsync_HeldWindowListedAgain_SkipsTheMessagesItFinished_AndReachesAChangeMadeAfterTheBurst()
    {
        // Each burst message changed at its own instant, oldest first, through its own row, an attachment row or a participant row.
        var burst = SpreadBurst(i => T0 - TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(i));

        // Changed after the whole burst, through a participant row: the last change in the window, so the carry's tail.
        var late = Message(changedAt: null);
        ContentRow(Participant, late, T0 - TimeSpan.FromMinutes(5));
        var job = NewJob();

        var first = await RunAsync(job);
        var finishedFirst = burst.Take(PriorityBudget).ToList(); // the oldest changes
        finishedFirst.Should().OnlyContain(m => Granted(m), "the first run works the oldest changes");
        var readsAfterFirst = finishedFirst.ToDictionary(m => m, ReadsOf);

        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Int(first, "carried").Should().Be(Job.MaxDirectCarried, "the carry is full");
        Since(second).Should().Be(Since(first), "a carry past its cap holds the watermark");
        finishedFirst.Should().OnlyContain(m => ReadsOf(m) == readsAfterFirst[m], "a finished message is not reconciled again");
        Int(second, "carried").Should().BeLessThan(Job.MaxDirectCarried, "skipping the finished messages drains the carry");

        // At most ten runs in all: the carry left by the second run drains at one run's budget each.
        var runs = 2 + await RunUntilAsync(job, () => Granted(late) && burst.All(Granted), maxRuns: 8);

        Granted(late).Should().BeTrue($"a change made after the burst is worked once the carry drains, without the sweep (runs: {runs})");
        burst.Should().OnlyContain(m => Granted(m), "every burst message is worked by the listing and the carry alone");
        Int(second, "skippedFinished").Should().Be(PriorityBudget, "the held window lists the first run's messages again, all finished");
    }

    [Fact]
    public async Task ExecuteAsync_ACarryPastItsCap_WhenEveryRowSharesTheWindowsStart_LosesNoMessage()
    {
        // The tie: every row changed at the first run's window start (a bulk import's single modifiedon). Moving the watermark
        // past that instant would lose every dropped message to the sweep, which answers nothing here.
        var since = T0 - Job.DirectInitialLookback;
        var burst = SpreadBurst(_ => since);
        var job = NewJob();

        var first = await RunAsync(job);
        Int(first, "carried").Should().Be(Job.MaxDirectCarried, "the carry is full");
        Since(first).Should().Be(since.UtcDateTime);

        var runs = await RunUntilAsync(job, () => burst.All(Granted), maxRuns: 9);

        burst.Should().OnlyContain(m => Granted(m), $"no message is lost to a watermark stepped past the tie (runs: {runs + 1})");
    }

    [Fact]
    public async Task ExecuteAsync_AMessageChangedAfterItWasFinished_IsNotSkipped_AndIsReconciledAgain()
    {
        var burst = SpreadBurst(i => T0 - TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(i));
        var job = NewJob();

        await RunAsync(job);
        var changed = burst[0]; // the oldest change: finished by the first run
        Granted(changed).Should().BeTrue();
        var reads = ReadsOf(changed);

        // After its finish: a new participant row (that listing stays under its cap), which its participants do not yet hold.
        _time.Advance(TimeSpan.FromMinutes(2));
        var row = ContentRow(Participant, changed, _time.GetUtcNow() - TimeSpan.FromSeconds(30));

        // And enough new messages that the second run's carry overflows too: the window stays held, so the first run's finished
        // record is kept and the change times alone decide the skip. (A second run that fit its carry would move the watermark and
        // prune every finished record, and a skip that ignored the change times would then pass.) Candidates of the second run:
        // the 1000 carried, the changed message, the one the first run dropped, and these.
        for (var i = 0; i < PriorityBudget; i++)
            ContentRowFor(Participant, _time.GetUtcNow() - TimeSpan.FromSeconds(20) + TimeSpan.FromMilliseconds(i));
        var second = await RunAsync(job);

        Bool(second, "listingCapped").Should().BeFalse();
        Int(second, "carried").Should().Be(Job.MaxDirectCarried, "the second run's carry overflows, so the window is held and the finished record kept");

        await RunUntilAsync(job, () => ReadsOf(changed) > reads, maxRuns: 9);

        ReadsOf(changed).Should().BeGreaterThan(reads, "a change after the finish is reconciled again");
        HoldsRow(Participant, row, Sender).Should().BeTrue("the new content row is shared with the participants");
        HoldsRow(Participant, row, Recipient).Should().BeTrue();
        Int(second, "skippedFinished").Should().Be(PriorityBudget - 1, "every first-run message but the changed one is skipped");
    }

    [Fact]
    public async Task ExecuteAsync_ACarryPastItsCap_WithACappedListing_HoldsThenMovesToTheCappedHorizon_AndLosesNoMessage()
    {
        // The messages' own rows cap their listing: 600 changed, 500 listed, so the horizon is the 500th change. The attachment
        // and participant listings (later changes) do not cap; with them the first run's carry overflows.
        var start = T0 - TimeSpan.FromMinutes(50);
        const int ownRows = Job.MaxDirectListedPerTable + 100;
        var messages = new List<Guid>();
        for (var i = 0; i < ownRows; i++)
            messages.Add(Message(start + TimeSpan.FromSeconds(i)));
        var horizon = (start + TimeSpan.FromSeconds(Job.MaxDirectListedPerTable - 1)).UtcDateTime;
        for (var i = 0; i < PerListing; i++)
        {
            messages.Add(ContentRowFor(Attachment, T0 - TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(i)));
            messages.Add(ContentRowFor(Participant, T0 - TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(i)));
        }

        var job = NewJob();
        var runs = new List<JobRunResult> { await RunAsync(job) };
        Bool(runs[0], "listingCapped").Should().BeTrue();
        Int(runs[0], "carried").Should().Be(Job.MaxDirectCarried, "the carry is full");

        while (!messages.All(Granted) && runs.Count < 15)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            runs.Add(await RunAsync(job));
        }

        var sinces = runs.Select(Since).ToList();
        sinces.Should().BeInAscendingOrder("the watermark never moves back");
        sinces.Should().Contain(horizon, "once the carry fits, a capped listing moves the watermark to its last row's change");
        sinces.TakeWhile(s => s != horizon).Should().OnlyContain(s => s == sinces[0], "while the carry overflows, the watermark is held");
        messages.Should().OnlyContain(m => Granted(m), $"no message is lost, the ones past the capped horizon included (runs: {runs.Count})");
    }

    [Fact]
    public async Task ExecuteAsync_ACarryPastItsCap_KeepsTheBackedOffMessages_AndDropsTheNewestListedChange()
    {
        // Three messages that stay incomplete until they are backed off, then fall out of the window (the watermark moves past
        // them): only the carry holds them, with their attempt counts.
        var deferred = new List<Guid>();
        for (var i = 0; i < DeferredCount; i++)
            deferred.Add(Message(T0 - TimeSpan.FromMinutes(10)));
        _unreadable.UnionWith(deferred);
        var job = NewJob();

        for (var run = 1; run <= Job.MaxDirectAttempts; run++)
        {
            var attempt = await RunAsync(job);
            Int(attempt, "incomplete").Should().Be(DeferredCount, $"run {run}: the unreadable messages stay incomplete");
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        // A burst after the watermark: one message over what the carry can hold next to the backed-off ones. The newest change is
        // the last of them, listed through a participant row.
        var changedAfter = _time.GetUtcNow() - TimeSpan.FromSeconds(30);
        var burst = SpreadBurst(i => changedAfter + TimeSpan.FromMilliseconds(i), BurstOverBackedOff - 1);
        var newest = Message(changedAt: null);
        var newestRow = ContentRow(Participant, newest, changedAfter + TimeSpan.FromMilliseconds(BurstOverBackedOff));

        var overflow = await RunAsync(job);

        Int(overflow, "backedOff").Should().Be(DeferredCount, "the unreadable messages are backed off");
        Int(overflow, "carried").Should().Be(Job.MaxDirectCarried, "the carry is full: one message over");

        // Take the dropped change out of the window, so that only the carry could still reach what it kept.
        _world.Set(Participant, newestRow, "modifiedon", (T0 - TimeSpan.FromMinutes(5)).UtcDateTime);
        _time.Advance(TimeSpan.FromMinutes(2));
        var next = await RunAsync(job);

        Since(next).Should().Be(Since(overflow), "a carry past its cap holds the watermark");
        Int(next, "backedOff").Should().Be(DeferredCount, "the backed-off messages and their attempt counts are carried ahead of the left-over tail");

        await RunUntilAsync(job, () => burst.All(Granted), maxRuns: 8);

        burst.Should().OnlyContain(m => Granted(m), "every listed message but the newest change is kept by the carry and worked");
        ReadsOf(newest).Should().Be(0, "the newest listed change is the one the overflowing carry drops");
        Granted(newest).Should().BeFalse();
    }

    // ── The job, as the host composes it ──

    private Job NewJob()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (ByIdRead(query) is { } unreadable && _unreadable.Contains(unreadable))
                    throw new InvalidOperationException("Test: this message cannot be read.");
                if (ParticipantRead(query) is { } message)
                    _participantReads[message] = ReadsOf(message) + 1;
                return Task.FromResult(IsSweep(query) ? new EntityCollection() : _world.Answer(query));
            });

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
        }).Build();

        var services = new ServiceCollection()
            .AddSingleton(_world.Synchronizer(_shares))
            .AddSingleton(SecureChildShareWorld.CoreAncestorsOver(() => _world))
            .AddSingleton<DataverseWebApiClient>(_users)
            .AddSingleton(entities.Object)
            .AddSingleton<IDataverseRecordShareService>(_shares)
            .AddSingleton(_directory.Lookup(_time))
            .AddSingleton<IConfiguration>(configuration);

        return new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _time,
            NullLogger<Job>.Instance);
    }

    private Task<JobRunResult> RunAsync(Job job) =>
        job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-2731", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);

    /// <summary>Runs every two minutes until <paramref name="done"/> or <paramref name="maxRuns"/> more runs; returns the count.</summary>
    private async Task<int> RunUntilAsync(Job job, Func<bool> done, int maxRuns)
    {
        var runs = 0;
        while (!done() && runs < maxRuns)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            await RunAsync(job);
            runs++;
        }

        return runs;
    }

    // ── The world ──

    private void DirectThread()
    {
        _thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", _thread,
            ("ownerid", new EntityReference("systemuser", Sender)),
            ("sprk_threadtype", new OptionSetValue(100000001)),
            ("sprk_isdefaultthread", false));
        _shares.Seed("sprk_communicationthread", _thread, DataversePrincipalRef.User(Recipient), Read);
    }

    /// <summary><paramref name="count"/> (<see cref="Burst"/>) messages, the i-th changed at <paramref name="changedAt"/>(i) through its own row (the first
    /// listing's worth), an attachment row (the next) or a participant row (the rest) — no listing reaches its cap.</summary>
    private List<Guid> SpreadBurst(Func<int, DateTimeOffset> changedAt, int count = Burst)
    {
        var burst = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            burst.Add(i < PerListing
                ? Message(changedAt(i))
                : ContentRowFor(i < 2 * PerListing ? Attachment : Participant, changedAt(i)));
        }

        return burst;
    }

    /// <summary>A Direct-team message on the thread, from <see cref="Sender"/> to <see cref="Recipient"/>, changed at
    /// <paramref name="changedAt"/> (never, for null: only a content row can list it).</summary>
    private Guid Message(DateTimeOffset? changedAt)
    {
        var message = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("owningteam", new EntityReference("team", DirectTeam)),
            ("sprk_communicationthread", new EntityReference("sprk_communicationthread", _thread)),
        };
        if (changedAt is { } at)
            columns.Add(("modifiedon", at.UtcDateTime));
        _world.Add(Communication, message, columns.ToArray());
        _world.Set(Communication, message, "sprk_sentby", new EntityReference("systemuser", Sender));
        _world.Set(Communication, message, "sprk_directrecipient", new EntityReference("systemuser", Recipient));
        return message;
    }

    /// <summary>A message listed only through one content row of <paramref name="table"/>, changed at <paramref name="changedAt"/>.</summary>
    private Guid ContentRowFor(string table, DateTimeOffset changedAt)
    {
        var message = Message(changedAt: null);
        ContentRow(table, message, changedAt);
        return message;
    }

    private Guid ContentRow(string table, Guid message, DateTimeOffset changedAt)
    {
        var row = Guid.NewGuid();
        _world.Add(table, row,
            ("owningteam", new EntityReference("team", DirectTeam)),
            ("sprk_communication", new EntityReference(Communication, message)),
            ("modifiedon", changedAt.UtcDateTime));
        return row;
    }

    private bool HoldsRow(string table, Guid row, Guid user) =>
        (_shares.MaskOf(table, row, DataversePrincipalRef.User(user)) ?? 0) != 0;

    private bool Granted(Guid message) => HoldsRow(Communication, message, Sender) && HoldsRow(Communication, message, Recipient);

    /// <summary>How many times the reconciler read the message's participants: once per reconcile of it.</summary>
    private int ReadsOf(Guid message) => _participantReads.TryGetValue(message, out var reads) ? reads : 0;

    /// <summary>The message a read of one message row by its id names.</summary>
    private static Guid? ByIdRead(QueryExpression query) =>
        query.EntityName == Communication
        && query.Criteria.Conditions.SingleOrDefault(c => c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal)
            is { } byId && byId.Values.Single() is Guid id
            ? id
            : null;

    private static Guid? ParticipantRead(QueryExpression query) =>
        query.EntityName == Communication && query.ColumnSet.Columns.Contains(DirectMessageParticipantShares.SentByColumn)
        && query.Criteria.Conditions.SingleOrDefault(c => c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal)
            is { } byId && byId.Values.Single() is Guid id
            ? id
            : null;

    private static bool OwnedByDirectTeam(QueryExpression query) => query.Criteria.Conditions.Any(c =>
        c.AttributeName == "owningteam" && c.Operator == ConditionOperator.Equal && c.Values.Single() is Guid team && team == DirectTeam);

    private static bool IsSweep(QueryExpression query) =>
        query.EntityName == Communication && OwnedByDirectTeam(query)
        && !query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon")
        && !query.Criteria.Conditions.Any(c => c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal);

    private static JsonElement Direct(JobRunResult run) =>
        JsonDocument.Parse(run.ResultJson!).RootElement.GetProperty("directMessages");

    private static int Int(JobRunResult run, string field) => Direct(run).GetProperty(field).GetInt32();

    private static bool Bool(JobRunResult run, string field) => Direct(run).GetProperty(field).GetBoolean();

    private static DateTime Since(JobRunResult run) => Direct(run).GetProperty("since").GetDateTime();
}
