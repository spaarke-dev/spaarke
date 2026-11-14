using System.Text.Json;
using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 198 Part C, strengthened for the <c>mutation-nightly</c> quality alert (#1821): what
/// <see cref="SecureChildShareReconciliationJob"/> REPORTS and REMEMBERS in its Direct-message pass, over the same in-memory
/// Dataverse (<see cref="SecureChildShareWorld"/>), share table (<see cref="FakeRecordShareTable"/>), user-flag read
/// (<see cref="UserFlagDirectoryDouble"/>) and REAL Direct-team lookup (<see cref="DirectMessageOwnerTeamDouble"/>) that
/// <see cref="DirectMessageParticipantSharesTests"/> drives. Those tests assert who ends up holding a share; these pin the
/// contract an operator and the next run act on:
/// <list type="bullet">
/// <item>the run result: <c>Success</c>, the exact <c>ErrorMessage</c>, <c>ProcessedItems</c> and every <c>directMessages</c>
/// field of <c>ResultJson</c>;</item>
/// <item>the one heartbeat per run (ADR-036 A1 rule 5), its level and its structured properties, and each warning or error the
/// pass logs, by their structured properties;</item>
/// <item>the per-instance state between runs: the watermark (initial look-back, overlap, held on a failure or an overflowing
/// carry, moved only to a capped listing's last row), the carry (ordered, capped, cleared once a message lands), the back-off
/// (from exactly its retry time) and the sweep cursor (one slice a run, resumed after its last id, restarted once complete);</item>
/// <item>"never throws (cancellation aside)": a listing or sweep fault, and a timeout that is not the caller's cancellation, are
/// contained; the caller's own cancellation ends the pass where it surfaced.</item>
/// </list>
/// Placement: <c>tests/integration/data-mutation/**</c> — every scenario decides which share rows get written and when.
/// </summary>
public class SecureChildShareReconciliationJobDirectPassTests
{
    private const int Read = 1;
    private const int ThreadTypeDirect = 100000001;
    private const string Communication = "sprk_communication";
    private const string Attachment = "sprk_communicationattachment";
    private const string Participant = "sprk_communicationparticipant";

    private static readonly Guid DirectTeam = Guid.Parse("d1821000-0000-4000-8000-0000000000d1");
    private static readonly Guid Caller = Guid.Parse("c1821000-0000-4000-8000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("c1821000-0000-4000-8000-0000000000a2");
    private static readonly Guid GateWriter = Guid.Parse("c1821000-0000-4000-8000-0000000000f1");

    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] ContentTables = [Attachment, Participant, "sprk_emailreviewlog", "sprk_document"];

    /// <summary>The priority budget of one run: everything but the sweep's reserve.</summary>
    private const int PriorityBudget = Job.MaxDirectMessagesPerRun - Job.DirectSweepReserve;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Caller, Other);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly CapturingLogger _log = new();
    private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
        ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
    }).Build();

    /// <summary>Every query the Direct pass (and the reconciler it calls) sends, in order — the synchronizer's are not here.</summary>
    private readonly List<QueryExpression> _directQueries = new();

    /// <summary>Answers (or throws for) a Direct-pass query before the world does; <c>Handled = false</c> lets the world answer.</summary>
    private Func<QueryExpression, (bool Handled, EntityCollection? Answer)>? _intercept;

    private DirectMessageOwnerTeam? _lookup;

    public SecureChildShareReconciliationJobDirectPassTests()
    {
        var directUnit = Guid.Parse("d1821000-0000-4000-8000-0000000000b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
    }

    // ── The job, as the host composes it ────────────────────────────────────────────────────────────────────────────

    private DirectMessageOwnerTeam Lookup() => _lookup ??= _directory.Lookup(_time);

    /// <summary>The REAL job over the world; <paramref name="without"/> withholds one of the Direct pass's seams.</summary>
    private Job NewJob(string? without = null, GateDecisionRecordSharing? gate = null)
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                _directQueries.Add(query);
                return Task.FromResult(_intercept?.Invoke(query) is { Handled: true } handled ? handled.Answer! : _world.Answer(query));
            });

        var services = new ServiceCollection()
            .AddSingleton(_world.Synchronizer(_shares))
            .AddSingleton(SecureChildShareWorld.CoreAncestorsOver(() => _world))
            .AddSingleton<DataverseWebApiClient>(_users);
        if (without != "dataverse")
            services.AddSingleton(entities.Object);
        if (without != "shares")
            services.AddSingleton<IDataverseRecordShareService>(_shares);
        if (without != "lookup")
            services.AddSingleton(Lookup());
        if (without != "configuration")
            services.AddSingleton<IConfiguration>(_configuration);
        if (gate is not null)
            services.AddSingleton(gate);

        return new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _time, _log);
    }

    private static JobRunContext RunContext() =>
        new(Guid.NewGuid(), "corr-1821", JobRunTrigger.Scheduled, new Dictionary<string, object>(), Attempt: 2);

    private Task<JobRunResult> RunAsync(Job job, CancellationToken ct = default) => job.ExecuteAsync(RunContext(), ct);

    // ── The world ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Guid DirectThread()
    {
        var thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", thread,
            ("ownerid", new EntityReference("systemuser", Caller)),
            ("sprk_threadtype", new OptionSetValue(ThreadTypeDirect)),
            ("sprk_isdefaultthread", false));
        _shares.Seed("sprk_communicationthread", thread, DataversePrincipalRef.User(Other), Read);
        return thread;
    }

    private static (string, object) OwnedBy(Guid team) => ("owningteam", new EntityReference("team", team));

    /// <summary>A message in <paramref name="thread"/> owned by <paramref name="owner"/>, its sender and recipient stamped, changed at
    /// <paramref name="modifiedOn"/> (never, for null: only the sweep reaches it).</summary>
    private Guid Message(Guid thread, (string Column, object Value) owner, DateTime? modifiedOn = null, Guid? id = null)
    {
        var message = id ?? Guid.NewGuid();
        var columns = new List<(string, object)> { owner, ("sprk_communicationthread", new EntityReference("sprk_communicationthread", thread)) };
        if (modifiedOn is { } at)
            columns.Add(("modifiedon", DateTime.SpecifyKind(at, DateTimeKind.Utc)));
        _world.Add(Communication, message, columns.ToArray());
        _world.Set(Communication, message, "sprk_sentby", new EntityReference("systemuser", Caller));
        _world.Set(Communication, message, "sprk_directrecipient", new EntityReference("systemuser", Other));
        return message;
    }

    /// <summary>A Direct-team message with one row per content table (owned by the Direct team), never changed.</summary>
    private (Guid Message, IReadOnlyDictionary<string, Guid> Rows) DirectMessageWithContent()
    {
        var message = Message(DirectThread(), OwnedBy(DirectTeam));
        var rows = new Dictionary<string, Guid>();
        foreach (var table in ContentTables)
            rows[table] = ContentRow(table, message);
        return (message, rows);
    }

    private Guid ContentRow(string table, Guid message, DateTime? modifiedOn = null)
    {
        var id = Guid.NewGuid();
        var lookup = table == "sprk_document" ? "sprk_relatedcommunication" : "sprk_communication";
        var columns = new List<(string, object)> { OwnedBy(DirectTeam), (lookup, new EntityReference(Communication, message)) };
        if (modifiedOn is { } at)
            columns.Add(("modifiedon", DateTime.SpecifyKind(at, DateTimeKind.Utc)));
        _world.Add(table, id, columns.ToArray());
        return id;
    }

    /// <summary><paramref name="count"/> Direct-team messages on one thread (no content rows), sorted by id; message i is changed at
    /// <paramref name="modifiedOn"/>(i), and is <paramref name="id"/>(i) when given.</summary>
    private List<Guid> DirectMessages(int count, Func<int, DateTime?> modifiedOn, Func<int, Guid>? id = null)
    {
        var thread = DirectThread();
        return Enumerable.Range(0, count).Select(i => Message(thread, OwnedBy(DirectTeam), modifiedOn(i), id?.Invoke(i)))
            .OrderBy(m => m).ToList();
    }

    private static List<Guid> SortedIds(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).OrderBy(g => g).ToList();

    private bool Holds(string table, Guid id, Guid user) => (_shares.MaskOf(table, id, DataversePrincipalRef.User(user)) ?? 0) != 0;

    private bool Granted(Guid message) => Holds(Communication, message, Caller) && Holds(Communication, message, Other);

    private int Grants() => _shares.WriteLog.Count(w => w.Action == "GrantAccess");

    // ── The Direct pass's three kinds of read ──

    private static bool OwnedByDirectTeam(QueryExpression query) => query.Criteria.Conditions.Any(c =>
        c.AttributeName == "owningteam" && c.Operator == ConditionOperator.Equal && c.Values.Single() is Guid team && team == DirectTeam);

    /// <summary>A recent-changes listing: Direct-team rows of one table changed since the watermark.</summary>
    private static bool IsListing(QueryExpression query) =>
        OwnedByDirectTeam(query) && query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon");

    /// <summary>The rolling sweep: Direct-team messages in id order, no change filter, not one row by id.</summary>
    private static bool IsSweep(QueryExpression query) =>
        query.EntityName == Communication && OwnedByDirectTeam(query)
        && !query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon")
        && !query.Criteria.Conditions.Any(c => c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal);

    /// <summary>The reconciler's read of one message by its id.</summary>
    private static bool ReadsMessage(QueryExpression query, Guid message) =>
        query.EntityName == Communication && query.Criteria.Conditions.Any(c =>
            c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal && c.Values.Single() is Guid id && id == message);

    /// <summary>Every read of <paramref name="message"/> by its id faults (the reconcile is retried) while <paramref name="failing"/>.</summary>
    private void FailReadsOf(Guid message, Func<bool> failing) =>
        _intercept = query => failing() && ReadsMessage(query, message)
            ? throw new InvalidOperationException("Test: this message cannot be read.")
            : (false, null);

    // ── Faults ──

    public enum FaultKind
    {
        /// <summary>A Dataverse / HTTP fault.</summary>
        Error,

        /// <summary>An HttpClient timeout: a <see cref="TaskCanceledException"/> while the caller's token is NOT cancelled.</summary>
        TimeoutWithoutCallerCancellation,

        /// <summary>The caller cancelled: its token is cancelled and the read throws <see cref="OperationCanceledException"/>.</summary>
        CallerCancelled,
    }

    private static Exception FaultOf(FaultKind kind, CancellationTokenSource caller)
    {
        switch (kind)
        {
            case FaultKind.Error:
                return new InvalidOperationException("Test: Dataverse fault.");
            case FaultKind.TimeoutWithoutCallerCancellation:
                return new TaskCanceledException("Test: the HTTP call timed out.");
            default:
                caller.Cancel();
                return new OperationCanceledException(caller.Token);
        }
    }

    // ── The result ──

    private static JsonElement Root(JobRunResult run) => JsonDocument.Parse(run.ResultJson!).RootElement;

    private static JsonElement Direct(JobRunResult run) => Root(run).GetProperty("directMessages");

    private static int Int(JobRunResult run, string field) => Direct(run).GetProperty(field).GetInt32();

    private static bool Bool(JobRunResult run, string field) => Direct(run).GetProperty(field).GetBoolean();

    private static string? Text(JobRunResult run, string field) => Direct(run).GetProperty(field).GetString();

    private static DateTime Since(JobRunResult run) => Direct(run).GetProperty("since").GetDateTime();

    /// <summary>The run's error message as the job words it: the secure-child part (this world has no secure child, so none is
    /// left not updated or held), then <paramref name="direct"/> (empty when the Direct pass is Ok).</summary>
    private static string ErrorMessageWith(JobRunResult run, string direct)
    {
        Root(run).GetProperty("notUpdated").GetInt32().Should().Be(0);
        Root(run).GetProperty("held").GetInt32().Should().Be(0);
        return "0 child(ren) not updated and 0 held" + direct + "; the next run retries.";
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The result and the heartbeat
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ACompleteRun_ReportsSuccessWithNoError_EveryDirectCount_AndOneInformationHeartbeat()
    {
        var (message, _) = DirectMessageWithContent();
        var context = RunContext();

        var run = await NewJob().ExecuteAsync(context, CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        run.ErrorMessage.Should().BeNull("a complete run carries no error");
        Granted(message).Should().BeTrue();

        Text(run, "status").Should().Be(Job.DirectMessageRun.StatusOk);
        Since(run).Should().Be((T0 - Job.DirectInitialLookback).UtcDateTime, "the first run on an instance looks back an hour");
        Int(run, "carriedIn").Should().Be(0);
        Int(run, "listed").Should().Be(0, "nothing changed");
        Int(run, "swept").Should().Be(1);
        Int(run, "examined").Should().Be(1);
        Int(run, "granted").Should().Be(10, "two participants on the message and its four content rows");
        Int(run, "revoked").Should().Be(0);
        Int(run, "incomplete").Should().Be(0);
        Int(run, "carried").Should().Be(0);
        Bool(run, "capReached").Should().BeFalse("one message is far inside the budget");
        Bool(run, "listingCapped").Should().BeFalse();
        Int(run, "backedOff").Should().Be(0);
        Bool(run, "sweepPassComplete").Should().BeTrue();
        Direct(run).GetProperty("incompleteMessages").GetArrayLength().Should().Be(0);
        Direct(run).GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
        Root(run).GetProperty("attempt").GetInt32().Should().Be(2);
        run.ProcessedItems.Should().Be(Root(run).GetProperty("inScope").GetInt32() + 1, "the secure children in scope plus the messages examined");

        var heartbeat = _log.Single(LogLevel.Information, "[SECURE-CHILD-SHARES] heartbeat",
            ("DirectStatus", Job.DirectMessageRun.StatusOk), ("DirectExamined", 1), ("DirectGranted", 10), ("DirectRevoked", 0),
            ("DirectIncomplete", 0), ("DirectCarried", 0), ("DirectCapReached", false), ("Attempt", 2),
            ("Trigger", JobRunTrigger.Scheduled), ("RunId", context.RunId), ("CorrelationId", "corr-1821"));
        heartbeat.Properties.Keys.Should().Contain(new[]
        {
            "Status", "InScope", "Updated", "Unchanged", "NotUpdated", "Held", "Outside", "Granted", "Changed", "Revoked",
            "HardDenyWiderThanRead", "HardDenyUnchecked", "HardDenyNarrowFailed", "Detail", "DurationMs",
        });
        _log.All.Count(e => e.Format.StartsWith("[SECURE-CHILD-SHARES] heartbeat", StringComparison.Ordinal)).Should().Be(1);
        _log.Matching(LogLevel.Warning, "[DIRECT-SHARES]").Should().BeEmpty("nothing was capped, carried or unresolved");
    }

    [Fact]
    public async Task ExecuteAsync_AFailedGrant_IsReportedAndCarried_AndTheCarryClearsOnceTheGrantLands()
    {
        var (message, _) = DirectMessageWithContent();
        _shares.FailWritesOnRecord = ("sprk_communications", message);
        var job = NewJob();

        var first = await RunAsync(job);

        first.Success.Should().BeFalse();
        first.ErrorMessage.Should().Be(ErrorMessageWith(first, "; Direct messages: ok, 1 not complete, 0 backed off"));
        Int(first, "incomplete").Should().Be(1);
        Int(first, "carried").Should().Be(1);
        Direct(first).GetProperty("incompleteMessages").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(message);
        _log.Single(LogLevel.Warning, "[SECURE-CHILD-SHARES] heartbeat", ("DirectIncomplete", 1), ("DirectCarried", 1));

        _shares.FailWritesOnRecord = null;
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        second.Success.Should().BeTrue(second.ErrorMessage);
        Granted(message).Should().BeTrue();
        Int(second, "carriedIn").Should().Be(1);
        Int(second, "examined").Should().Be(1, "carried and swept are the same message");
        Int(second, "carried").Should().Be(0, "a message that landed is no longer carried");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The Direct team and the seams
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("lookup")]
    [InlineData("dataverse")]
    [InlineData("shares")]
    [InlineData("configuration")]
    public async Task ExecuteAsync_WhenADirectSeamIsNotRegistered_SkipsThePassAsUnavailable_WritesNothing_AndWarns(string missing)
    {
        DirectMessageWithContent();
        var context = RunContext();

        var run = await NewJob(without: missing).ExecuteAsync(context, CancellationToken.None);

        Text(run, "status").Should().Be(Job.DirectMessageRun.StatusUnavailable);
        run.Success.Should().BeTrue("an unavailable pass decides nothing, so it does not fail the secure-child mirror");
        Grants().Should().Be(0);
        _directQueries.Should().BeEmpty("nothing is listed or read without every seam");
        _log.Single(LogLevel.Warning, "[DIRECT-SHARES]", ("RunId", context.RunId));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheDirectTeamCannotBeResolved_GrantsNothing_KeepsTheCarryAndTheWatermark_AndSaysWhy()
    {
        var (message, _) = DirectMessageWithContent();
        _shares.FailWritesOnRecord = ("sprk_communications", message);
        var job = NewJob();
        await RunAsync(job); // carries the message; the watermark moves to T0 - overlap

        _shares.FailWritesOnRecord = null;
        _directory.Faults = true;
        _time.Advance(DirectMessageOwnerTeam.CacheLifetime);
        var context = RunContext();
        var writes = _shares.WriteLog.Count;
        var unresolved = await job.ExecuteAsync(context, CancellationToken.None);

        _shares.WriteLog.Count.Should().Be(writes, "nothing is granted while the Direct team is unknown");
        unresolved.Success.Should().BeFalse();
        Text(unresolved, "status").Should().Be(Job.DirectMessageRun.StatusTeamUnknown);
        Text(unresolved, "failure").Should().Be("the Direct-message owner team cannot be resolved");
        Int(unresolved, "carriedIn").Should().Be(1);
        Int(unresolved, "carried").Should().Be(1);
        Int(unresolved, "incomplete").Should().Be(1);
        Since(unresolved).Should().Be((T0 - Job.DirectOverlap).UtcDateTime);
        unresolved.ErrorMessage.Should().Be(ErrorMessageWith(unresolved,
            "; Direct messages: direct-team-unknown, 1 not complete, 0 backed off (the Direct-message owner team cannot be resolved)"));
        var warning = _log.Single(LogLevel.Warning, "[DIRECT-SHARES]", ("RunId", context.RunId), ("Carried", 1));
        warning.Properties["Refusal"].Should().BeOfType<string>().Which.Should().NotBeNullOrEmpty();

        _directory.Faults = false;
        _time.Advance(TimeSpan.FromMinutes(2));
        var recovered = await RunAsync(job);

        recovered.Success.Should().BeTrue(recovered.ErrorMessage);
        Int(recovered, "carriedIn").Should().Be(1, "the unresolved run kept the carry it could not work");
        Granted(message).Should().BeTrue();
        Since(recovered).Should().Be((T0 - Job.DirectOverlap).UtcDateTime, "the unresolved run did not move the watermark");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheCallerCancelsDuringTheDirectTeamLookup_Throws_AndGrantsNothing()
    {
        DirectMessageWithContent();
        using var caller = new CancellationTokenSource();
        _directory.Fault = () =>
        {
            caller.Cancel();
            return new OperationCanceledException(caller.Token);
        };

        var act = () => RunAsync(NewJob(), caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("the caller's cancellation is not a Direct team that cannot be resolved");
        Grants().Should().Be(0);
        _directQueries.Should().BeEmpty("nothing is listed after the cancellation");
        _log.Matching(LogLevel.Warning, "[DIRECT-SHARES]").Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The listing and the sweep: what they read, and their faults
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_TheRecentChangesListing_NamesExactlyTheDirectMessagesChangedSinceTheWatermark_ThroughTheMessageOrAContentRow()
    {
        var recently = (T0 - TimeSpan.FromMinutes(5)).UtcDateTime;
        var thread = DirectThread();
        var changed = Message(thread, OwnedBy(DirectTeam), recently);
        var viaAttachment = Message(thread, OwnedBy(DirectTeam));
        ContentRow(Attachment, viaAttachment, recently);
        var viaDocument = Message(thread, OwnedBy(DirectTeam));
        ContentRow("sprk_document", viaDocument, recently);
        var stale = Message(thread, OwnedBy(DirectTeam), (T0 - TimeSpan.FromHours(2)).UtcDateTime);
        var userOwned = Message(thread, ("owninguser", new EntityReference("systemuser", Caller)), recently);
        _intercept = query => IsSweep(query) ? (true, new EntityCollection()) : (false, null); // only the listing names messages

        var run = await RunAsync(NewJob());

        Int(run, "listed").Should().Be(3);
        Int(run, "examined").Should().Be(3);
        Granted(changed).Should().BeTrue("its own row changed");
        Granted(viaAttachment).Should().BeTrue("an attachment row naming it changed");
        Granted(viaDocument).Should().BeTrue("a document naming it (sprk_relatedcommunication) changed");
        Granted(stale).Should().BeFalse("it changed before the window");
        Holds(Communication, userOwned, Other).Should().BeFalse("only Direct-team rows are listed");
        _directQueries.Should().NotContain(q => ReadsMessage(q, stale) || ReadsMessage(q, userOwned));
    }

    [Fact]
    public async Task ExecuteAsync_TheListingsAndTheSweep_AskDataverseForAStableOldestFirstAndIdOrder()
    {
        // The in-memory world answers in id order before it applies a query's own orders, so a dropped id tie-break, or a dropped
        // id order on the sweep, is invisible in its answers. Dataverse returns an unordered TopCount page in no defined order, and
        // the watermark (a capped listing's last row) and the sweep cursor (the slice's last id) are only right over these orders —
        // so the query is the contract asserted here.
        DirectMessageWithContent();

        await RunAsync(NewJob());

        var listings = _directQueries.Where(IsListing).ToList();
        listings.Select(q => q.EntityName).Should().BeEquivalentTo(new[] { Communication }.Concat(ContentTables));
        var lookups = Sprk.Bff.Api.Services.Access.DirectMessageParticipantShares.ContentRows
            .ToDictionary(r => r.Table, r => r.MessageLookup);
        foreach (var listing in listings)
        {
            // The world ignores the column set; Dataverse refuses a null column name.
            listing.ColumnSet.Columns.Should().Equal(listing.EntityName == Communication
                ? new[] { "modifiedon" }
                : new[] { lookups[listing.EntityName], "modifiedon" });
            listing.Orders.Select(o => (o.AttributeName, o.OrderType)).Should().Equal(
                ("modifiedon", OrderType.Ascending), (listing.EntityName + "id", OrderType.Ascending));
            listing.TopCount.Should().Be(Job.MaxDirectListedPerTable);
            listing.Criteria.Conditions.Should().Contain(c => c.AttributeName == "modifiedon"
                && c.Operator == ConditionOperator.GreaterEqual && (DateTime)c.Values.Single() == (T0 - Job.DirectInitialLookback).UtcDateTime);
        }

        var sweep = _directQueries.Single(IsSweep);
        sweep.Orders.Select(o => (o.AttributeName, o.OrderType)).Should().Equal((Communication + "id", OrderType.Ascending));
        sweep.TopCount.Should().Be(Job.DirectSweepSlice, "nothing was due, so the sweep takes its full slice and no more");
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task ExecuteAsync_WhenTheListingFaults_ContainsIt_TheSweepStillRuns_AndTheWatermarkIsHeld(FaultKind kind)
    {
        var job = NewJob();
        await RunAsync(job); // sets the watermark: T0 - overlap
        _directQueries.Clear();
        _time.Advance(TimeSpan.FromMinutes(2));
        var (message, _) = DirectMessageWithContent();
        using var caller = new CancellationTokenSource();
        _intercept = query => IsListing(query) ? throw FaultOf(kind, caller) : (false, null);
        var context = RunContext();

        if (kind == FaultKind.CallerCancelled)
        {
            var act = () => job.ExecuteAsync(context, caller.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
            _log.At(LogLevel.Error).Should().BeEmpty("the caller's cancellation is not a fault");
            _directQueries.Should().NotContain(q => IsSweep(q), "the pass ended at the listing");
            Grants().Should().Be(0);
            return;
        }

        var run = await job.ExecuteAsync(context, caller.Token);

        run.Success.Should().BeFalse();
        Text(run, "status").Should().Be(Job.DirectMessageRun.StatusListingFailed);
        Text(run, "failure").Should().Be("the recently changed Direct messages could not be listed");
        run.ErrorMessage.Should().Be(ErrorMessageWith(run,
            "; Direct messages: listing-failed, 0 not complete, 0 backed off (the recently changed Direct messages could not be listed)"));
        Int(run, "swept").Should().Be(1);
        Granted(message).Should().BeTrue("the sweep still reconciles what it reaches");
        _log.Single(LogLevel.Error, "[DIRECT-SHARES]", ("RunId", context.RunId)).Exception.Should().NotBeNull();

        _intercept = null;
        _time.Advance(TimeSpan.FromMinutes(2));
        var next = await RunAsync(job);

        Since(next).Should().Be((T0 - Job.DirectOverlap).UtcDateTime, "the window that could not be listed is listed again");
        Text(next, "status").Should().Be(Job.DirectMessageRun.StatusOk);
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task ExecuteAsync_WhenTheSweepFaults_ContainsIt_TheListedMessagesStillRun_AndTheWatermarkIsHeld(FaultKind kind)
    {
        var job = NewJob();
        await RunAsync(job); // sets the watermark: T0 - overlap
        _directQueries.Clear();
        _time.Advance(TimeSpan.FromMinutes(2));
        var listed = DirectMessages(1, _ => (T0 + TimeSpan.FromMinutes(1)).UtcDateTime).Single(); // changed after the watermark
        using var caller = new CancellationTokenSource();
        _intercept = query => IsSweep(query) ? throw FaultOf(kind, caller) : (false, null);
        var context = RunContext();

        if (kind == FaultKind.CallerCancelled)
        {
            var act = () => job.ExecuteAsync(context, caller.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
            _log.At(LogLevel.Error).Should().BeEmpty("the caller's cancellation is not a fault");
            _directQueries.Should().NotContain(q => ReadsMessage(q, listed), "the pass ended at the sweep, before any message");
            Grants().Should().Be(0);
            return;
        }

        var run = await job.ExecuteAsync(context, caller.Token);

        run.Success.Should().BeFalse();
        Text(run, "status").Should().Be(Job.DirectMessageRun.StatusListingFailed);
        Text(run, "failure").Should().Be("the Direct-message sweep could not be read");
        Int(run, "swept").Should().Be(0);
        Bool(run, "sweepPassComplete").Should().BeFalse("a sweep that could not be read did not complete its pass");
        Int(run, "listed").Should().Be(1);
        Granted(listed).Should().BeTrue("the listed message is still reconciled");
        _log.Single(LogLevel.Error, "[DIRECT-SHARES]", ("RunId", context.RunId)).Exception.Should().NotBeNull();

        _intercept = null;
        _time.Advance(TimeSpan.FromMinutes(2));
        var next = await RunAsync(job);

        Since(next).Should().Be((T0 - Job.DirectOverlap).UtcDateTime, "a run with a failure holds the watermark");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheListingAndTheSweepBothFault_ReportsTheListingFailure()
    {
        DirectMessageWithContent();
        _intercept = query => IsListing(query) || IsSweep(query) ? throw new InvalidOperationException("Test: Dataverse fault.") : (false, null);

        var run = await RunAsync(NewJob());

        Text(run, "failure").Should().Be("the recently changed Direct messages could not be listed", "the first failure is the one reported");
        _log.Matching(LogLevel.Error, "[DIRECT-SHARES]").Should().HaveCount(2, "each fault is logged");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheSweepAnswersNoCollection_ReadsItAsAnEmptySlice_NotAFailure()
    {
        var listed = DirectMessages(1, _ => (T0 - TimeSpan.FromMinutes(5)).UtcDateTime).Single();
        _intercept = query => IsSweep(query) ? (true, null) : (false, null);

        var run = await RunAsync(NewJob());

        run.Success.Should().BeTrue(run.ErrorMessage);
        Text(run, "status").Should().Be(Job.DirectMessageRun.StatusOk);
        Int(run, "swept").Should().Be(0);
        Bool(run, "sweepPassComplete").Should().BeTrue();
        Granted(listed).Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The sweep cursor
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_TheSweep_TakesOneSliceARun_ResumesAfterItsLastId_AndStartsOverOnceThePassCompletes()
    {
        var messages = DirectMessages(Job.DirectSweepSlice + 50, _ => null); // never changed: only the sweep reaches them
        Message(DirectThread(), ("owninguser", new EntityReference("systemuser", Caller))); // never swept: not the Direct team's
        var job = NewJob();

        var first = await RunAsync(job);

        Int(first, "swept").Should().Be(Job.DirectSweepSlice);
        Int(first, "examined").Should().Be(Job.DirectSweepSlice);
        Bool(first, "sweepPassComplete").Should().BeFalse("a full slice may have more after it");
        messages.Take(Job.DirectSweepSlice).Should().OnlyContain(m => Granted(m), "the slice is the lowest ids");
        messages.Skip(Job.DirectSweepSlice).Should().NotContain(m => Granted(m));

        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Int(second, "swept").Should().Be(50, "the sweep resumes after the last id it read");
        Bool(second, "sweepPassComplete").Should().BeTrue();
        messages.Should().OnlyContain(m => Granted(m));

        _time.Advance(TimeSpan.FromMinutes(2));
        var third = await RunAsync(job);

        Int(third, "swept").Should().Be(Job.DirectSweepSlice, "a completed pass starts over from the lowest id");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The budget and the carry
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_MoreDueThanTheBudget_WorksTheOldestChangesFirst_CarriesTheRest_AndWorksTheCarryInIdOrder()
    {
        // The higher the id, the older the change: the listing (oldest change first) leaves the carry in DESCENDING id order, so
        // only the carry's own ordering makes the next run work its lowest ids first.
        const int due = 400;
        var ids = SortedIds(due);
        var start = (T0 - TimeSpan.FromMinutes(40)).UtcDateTime;
        var messages = DirectMessages(due, i => start.AddSeconds(due - i), i => ids[i]);
        var job = NewJob();
        var context = RunContext();

        var first = await job.ExecuteAsync(context, CancellationToken.None);

        Int(first, "listed").Should().Be(due);
        Int(first, "swept").Should().Be(Job.DirectSweepReserve, "what is left of the run's budget after the priority work");
        Int(first, "examined").Should().Be(PriorityBudget + Job.DirectSweepReserve, "the oldest changes, then the sweep's lowest ids");
        Int(first, "carried").Should().Be(due - PriorityBudget, "the swept messages that were also left over are still carried");
        Bool(first, "capReached").Should().BeTrue();
        messages.Skip(due - PriorityBudget).Should().OnlyContain(m => Granted(m), "the oldest changes are the highest ids");
        messages.Take(Job.DirectSweepReserve).Should().OnlyContain(m => Granted(m), "the sweep's slice");
        messages.Skip(Job.DirectSweepReserve).Take(due - PriorityBudget - Job.DirectSweepReserve).Should().NotContain(m => Granted(m));
        _log.Single(LogLevel.Warning, "[DIRECT-SHARES]", ("RunId", context.RunId), ("Total", due), ("Budget", PriorityBudget),
            ("LeftOver", due - PriorityBudget));

        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Int(second, "carriedIn").Should().Be(due - PriorityBudget);
        messages.Take(PriorityBudget).Should().OnlyContain(m => Granted(m), "the carry is worked lowest id first");
        messages.Skip(PriorityBudget).Take(due - 2 * PriorityBudget).Should().NotContain(m => Granted(m),
            "the carry's highest ids wait for the next run");
        Int(second, "carried").Should().Be(due - 2 * PriorityBudget);
    }

    [Theory]
    [InlineData(Job.MaxDirectCarried, false)]
    [InlineData(Job.MaxDirectCarried + 1, true)]
    public async Task ExecuteAsync_ACarryPastItsCap_CarriesOnlyTheCap_HoldsTheWatermark_AndWarns(int toCarry, bool overflows)
    {
        // An inherent multi-record arrange: the carry cap is 1,000 messages, reached in one run without capping any one listing
        // (at most MaxDirectListedPerTable - 1 rows each), across the messages' own rows and two content tables.
        var job = NewJob();
        await RunAsync(job); // sets the watermark: T0 - overlap
        _time.Advance(TimeSpan.FromMinutes(2));
        var recently = (T0 + TimeSpan.FromMinutes(1)).UtcDateTime; // changed after the watermark
        var perListing = Job.MaxDirectListedPerTable - 1;
        var thread = DirectThread();
        for (var i = 0; i < PriorityBudget + toCarry; i++)
        {
            if (i < perListing)
            {
                Message(thread, OwnedBy(DirectTeam), recently);
                continue;
            }

            ContentRow(i < 2 * perListing ? Attachment : Participant, Message(thread, OwnedBy(DirectTeam)), recently);
        }

        var context = RunContext();

        var first = await job.ExecuteAsync(context, CancellationToken.None);

        Bool(first, "listingCapped").Should().BeFalse();
        Int(first, "carried").Should().Be(Math.Min(toCarry, Job.MaxDirectCarried));
        var overflowWarnings = _log.Matching(LogLevel.Warning, "[DIRECT-SHARES]").Where(e => e.Properties.ContainsKey("Carry")).ToList();
        if (overflows)
            overflowWarnings.Should().ContainSingle().Which.Should().Match<LogEntry>(e =>
                Equals(e.Properties["Carry"], toCarry) && Equals(e.Properties["Cap"], Job.MaxDirectCarried)
                && Equals(e.Properties["Unlisted"], 0) && Equals(e.Properties["RunId"], context.RunId));
        else
            overflowWarnings.Should().BeEmpty("exactly the cap is carried without overflow");

        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Since(second).Should().Be(overflows
            ? (T0 - Job.DirectOverlap).UtcDateTime
            : (T0 + TimeSpan.FromMinutes(2) - Job.DirectOverlap).UtcDateTime,
            overflows ? "a carry past its cap drops messages, so the watermark is held" : "a run within the cap moves the watermark");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The watermark
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_EachLaterRun_ListsFromThePreviousRunsStartLessTheOverlap()
    {
        DirectMessageWithContent();
        var job = NewJob();

        var first = await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        var third = await RunAsync(job);

        Since(first).Should().Be((T0 - Job.DirectInitialLookback).UtcDateTime);
        Since(second).Should().Be((T0 - Job.DirectOverlap).UtcDateTime);
        Since(third).Should().Be((T0 + TimeSpan.FromMinutes(2) - Job.DirectOverlap).UtcDateTime);
    }

    [Fact]
    public async Task ExecuteAsync_ACappedListing_MovesTheWatermarkOnlyToItsLastListedRow_OldestChangeFirst()
    {
        // The higher the id, the older the change: listed oldest change first, the cap's last row is NOT the highest id.
        const int count = Job.MaxDirectListedPerTable + 1;
        var start = (T0 - TimeSpan.FromMinutes(40)).UtcDateTime;
        var ids = SortedIds(count);
        DirectMessages(count, i => start.AddSeconds(count - i), i => ids[i]);
        var job = NewJob();
        var context = RunContext();

        var first = await job.ExecuteAsync(context, CancellationToken.None);

        Bool(first, "listingCapped").Should().BeTrue();
        Bool(first, "capReached").Should().BeTrue("a capped listing is a capped run");
        Int(first, "listed").Should().Be(Job.MaxDirectListedPerTable);
        first.Success.Should().BeFalse("changed rows were left unlisted");
        first.ErrorMessage.Should().Be(ErrorMessageWith(first,
            "; Direct messages: ok, 0 not complete, 0 backed off, a recent-changes listing capped"));
        _log.Single(LogLevel.Warning, "[DIRECT-SHARES]", ("RunId", context.RunId), ("Capped", 1), ("Cap", Job.MaxDirectListedPerTable));
        _log.Matching(LogLevel.Warning, "[DIRECT-SHARES]").Should().NotContain(e => e.Properties.ContainsKey("Since"),
            "the listing moved past the window's start");

        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Since(second).Should().Be(start.AddSeconds(count - 1),
            "the 500th-oldest change (message 1); everything before it was listed, nothing after it was");
    }

    [Fact]
    public async Task ExecuteAsync_TwoCappedListings_MoveTheWatermarkToTheEarlierOfTheirLastRows()
    {
        var start = (T0 - TimeSpan.FromMinutes(40)).UtcDateTime;
        DirectMessages(Job.MaxDirectListedPerTable, i => start.AddSeconds(i)); // its last row: start + 499 s
        var gone = Guid.NewGuid(); // the attachments name a message that is no longer there: it settles as not found
        for (var i = 0; i < Job.MaxDirectListedPerTable; i++)
            ContentRow(Attachment, gone, start.AddSeconds(1000 + i)); // its last row: start + 1499 s
        var job = NewJob();

        var first = await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Bool(first, "listingCapped").Should().BeTrue();
        Int(first, "listed").Should().Be(Job.MaxDirectListedPerTable + 1, "500 messages, and the one message 500 attachments name");
        Since(second).Should().Be(start.AddSeconds(Job.MaxDirectListedPerTable - 1),
            "past the earlier last row, the other listing still has unlisted changes");
    }

    [Fact]
    public async Task ExecuteAsync_ACappedListingWithNothingLeftOver_IsStillACappedRun()
    {
        var gone = Guid.NewGuid(); // 500 changed attachments, all naming one message (no longer there: it settles as not found)
        for (var i = 0; i < Job.MaxDirectListedPerTable; i++)
            ContentRow(Attachment, gone, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime);

        var run = await RunAsync(NewJob());

        Int(run, "listed").Should().Be(1);
        Int(run, "carried").Should().Be(0, "one message is far inside the budget");
        Bool(run, "listingCapped").Should().BeTrue();
        Bool(run, "capReached").Should().BeTrue("a capped listing leaves changed rows for the next runs");
        _log.Single(LogLevel.Warning, "[SECURE-CHILD-SHARES] heartbeat", ("DirectCapReached", true));
    }

    [Fact]
    public async Task ExecuteAsync_ACappedListingThatDoesNotMovePastTheWindowStart_StepsTheWatermarkOneTickPastIt_AndWarns()
    {
        var windowStart = T0 - Job.DirectInitialLookback;
        DirectMessages(Job.MaxDirectListedPerTable, _ => windowStart.UtcDateTime);
        var job = NewJob();
        var context = RunContext();

        var first = await job.ExecuteAsync(context, CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Bool(first, "listingCapped").Should().BeTrue();
        Since(second).Should().Be(windowStart.UtcDateTime.AddTicks(1), "listing again from the same instant would return the same rows");
        _log.Single(LogLevel.Warning, "[DIRECT-SHARES]", ("RunId", context.RunId), ("Since", windowStart));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The back-off
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_AMessageIncompleteThreeRunsInARow_IsBackedOffUntilExactlyItsRetryTime_AndStaysCarried()
    {
        var (message, _) = DirectMessageWithContent();
        FailReadsOf(message, () => true);
        var job = NewJob();

        for (var run = 1; run <= Job.MaxDirectAttempts; run++)
        {
            await RunAsync(job);
            if (run < Job.MaxDirectAttempts)
                _time.Advance(TimeSpan.FromMinutes(2));
        }

        var retryAt = _time.GetUtcNow() + Job.DirectBackoff;
        _log.Single(LogLevel.Error, "[DIRECT-SHARES]", ("Id", message), ("Failures", Job.MaxDirectAttempts),
            ("Detail", "the reconcile faulted"), ("RetryAt", retryAt));

        _time.Advance(Job.DirectBackoff - TimeSpan.FromTicks(1));
        var backedOff = await RunAsync(job);

        Int(backedOff, "examined").Should().Be(0, "the message is not tried before its retry time");
        Int(backedOff, "backedOff").Should().Be(1);
        Int(backedOff, "carried").Should().Be(1, "a backed-off message stays carried");
        backedOff.ErrorMessage.Should().Be(ErrorMessageWith(backedOff, "; Direct messages: ok, 0 not complete, 1 backed off"));

        _time.Advance(TimeSpan.FromTicks(1));
        var retried = await RunAsync(job);

        Int(retried, "examined").Should().Be(1, "at its retry time it is tried again");
        Int(retried, "backedOff").Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_AMessageThatRecovers_StartsItsFailureCountAgain()
    {
        var (message, _) = DirectMessageWithContent();
        var failing = true;
        FailReadsOf(message, () => failing);
        var job = NewJob();

        await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        await RunAsync(job); // two failures in a row

        failing = false;
        _time.Advance(TimeSpan.FromMinutes(2));
        var recovered = await RunAsync(job);
        Granted(message).Should().BeTrue();
        Int(recovered, "carried").Should().Be(0);

        failing = true;
        _time.Advance(TimeSpan.FromMinutes(2));
        await RunAsync(job); // one failure since it recovered
        _time.Advance(TimeSpan.FromMinutes(2));
        var next = await RunAsync(job); // two

        _log.Matching(LogLevel.Error, "[DIRECT-SHARES]").Should().BeEmpty("no run saw three failures in a row");
        Int(next, "examined").Should().Be(1);
        Int(next, "backedOff").Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Cancellation between messages; the gate mirror
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_TheCallersCancellationBetweenMessages_EndsThePass_BeforeTheNextMessageIsRead()
    {
        var ids = SortedIds(2);
        DirectMessages(2, _ => null, i => ids[i]);
        using var caller = new CancellationTokenSource();
        _intercept = query =>
        {
            if (!ReadsMessage(query, ids[0]))
                return (false, null);
            caller.Cancel(); // observed after the first message's read, which answers "not found": it settles at once
            return (true, new EntityCollection());
        };

        var act = () => RunAsync(NewJob(), caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _directQueries.Should().NotContain(q => ReadsMessage(q, ids[1]), "the pass ended before the next message");
        Grants().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_AGrantOnlyOnContentRows_DoesNotRunTheGateRecordMirror()
    {
        var (message, rows) = DirectMessageWithContent();
        _world.User(Caller, SecureChildShareWorld.GeneralBu).User(Other, SecureChildShareWorld.GeneralBu);
        _shares.Seed(Communication, message, DataversePrincipalRef.User(Caller), Read);
        _shares.Seed(Communication, message, DataversePrincipalRef.User(Other), Read); // the message itself needs no grant
        foreach (var (table, id) in rows)
            _shares.Seed(table, id, DataversePrincipalRef.User(Caller), Read); // Other reads no content row: the only grants
        var record = Guid.NewGuid();
        _world.Add("sprk_decisionrecord", record,
            ("sprk_regardingcommunication", new EntityReference(Communication, message)),
            OwnedBy(DirectTeam),
            ("createdby", new EntityReference("systemuser", GateWriter)));
        _shares.Seed("sprk_decisionrecord", record, DataversePrincipalRef.User(Caller), Read);

        var run = await RunAsync(NewJob(gate: GateOverTheWorld()));

        run.Success.Should().BeTrue(run.ErrorMessage);
        Holds(Attachment, rows[Attachment], Other).Should().BeTrue("the content rows were granted");
        Holds("sprk_decisionrecord", record, Other).Should().BeFalse("the mirror follows a grant on the MESSAGE only");
    }

    /// <summary>The REAL <see cref="GateDecisionRecordSharing"/> over this world and share table, as the host composes it.</summary>
    private GateDecisionRecordSharing GateOverTheWorld()
    {
        var org = new Mock<IOrganizationServiceAsync2>();
        org.Setup(o => o.ExecuteAsync(It.IsAny<WhoAmIRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<OrganizationResponse>(new WhoAmIResponse { Results = { ["UserId"] = GateWriter } }));
        return new GateDecisionRecordSharing(
            SecureChildShareWorld.EntitiesOver(() => _world).Object, _shares,
            new OntologyWriterDataverseClient(() => org.Object, NullLogger<OntologyWriterDataverseClient>.Instance),
            Mock.Of<IRecordOwnershipResolver>(), Mock.Of<IMembershipCacheInvalidator>(), NullLogger<GateDecisionRecordSharing>.Instance,
            Lookup(), _configuration);
    }

    // ── Logs, by their structured properties ────────────────────────────────────────────────────────────────────────

    public sealed record LogEntry(LogLevel Level, string Format, Exception? Exception, IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLogger : ILogger<Job>
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> All
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        public IEnumerable<LogEntry> At(LogLevel level) => All.Where(e => e.Level == level);

        /// <summary>The logs at <paramref name="level"/> whose template starts with <paramref name="prefix"/>.</summary>
        public IEnumerable<LogEntry> Matching(LogLevel level, string prefix) =>
            At(level).Where(e => e.Format.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>The one log at <paramref name="level"/> whose template starts with <paramref name="prefix"/> and that carries every
        /// property in <paramref name="properties"/>.</summary>
        public LogEntry Single(LogLevel level, string prefix, params (string Key, object Value)[] properties)
        {
            var matches = Matching(level, prefix)
                .Where(e => properties.All(p => e.Properties.TryGetValue(p.Key, out var value) && Equals(value, p.Value)))
                .ToList();
            matches.Should().ContainSingle(
                $"one {level} log starting '{prefix}' with [{string.Join(", ", properties.Select(p => $"{p.Key}={p.Value}"))}] is expected; " +
                $"logged: {string.Join(" | ", All.Select(e => $"{e.Level}: {e.Format}"))}");
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
