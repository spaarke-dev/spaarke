using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
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

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 198 Part C: the participants of a Direct 1:1 message owned by the Direct-message owner team
/// (fm 054a) hold Read on it and on its content rows, and nobody else holds a direct user share on the message. Driven over the REAL
/// <see cref="SecureChildShareReconciliationJob"/> (the scheduled backstop and retry), the REAL <see cref="SecureChildReconciler"/>
/// (its <c>AfterRefileAsync</c>, the step after an unfile or a secure exit), the REAL <see cref="DirectMessageOwnerTeam"/> lookup,
/// one in-memory Dataverse (<see cref="SecureChildShareWorld"/>) and the strict share double (<see cref="FakeRecordShareTable"/>).
/// <list type="bullet">
/// <item>The audience is the message's own thread's user owner and its systemuser sharees; nobody else is ever granted.</item>
/// <item>Every unknown (a grant fault, the Direct team refused or faulted, the Secure check undecided) grants nothing and is
/// retried by the next run until the grant lands; a run after that writes nothing.</item>
/// <item>A Secure-owned message is never granted here. The Restricted bar applies to every grant.</item>
/// <item>Record-anchored, secure, the per-user master thread, unfiled inbound email and user-owned Direct messages are unchanged.</item>
/// </list>
/// </summary>
public class DirectMessageParticipantSharesTests
{
    private const int Read = 1;
    private const int ThreadTypeDirect = 100000001;
    private const int ThreadTypeRecordAnchored = 100000000;

    private static readonly Guid DirectTeam = Guid.Parse("d1980000-0000-4000-8000-0000000000d1");
    private static readonly Guid Caller = Guid.Parse("c1980000-0000-4000-8000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("c1980000-0000-4000-8000-0000000000a2");
    private static readonly Guid Outsider = Guid.Parse("c1980000-0000-4000-8000-0000000000a3");
    private static readonly Guid AnchorGrantee = Guid.Parse("c1980000-0000-4000-8000-0000000000a4");

    /// <summary>R2: a user on no thread, stamped as a message's recipient.</summary>
    private static readonly Guid Stamped = Guid.Parse("c1980000-0000-4000-8000-0000000000a5");

    private static readonly string[] ContentTables =
        ["sprk_communicationattachment", "sprk_communicationparticipant", "sprk_emailreviewlog", "sprk_document"];

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Caller, Other, Outsider, AnchorGrantee, Stamped);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
        ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
    }).Build();

    private DirectMessageOwnerTeam? _lookup;

    public DirectMessageParticipantSharesTests()
    {
        var directUnit = Guid.Parse("d1980000-0000-4000-8000-0000000000b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
    }

    /// <summary>One lookup per test (its verdict cache runs on the test clock), as the host's singleton.</summary>
    private DirectMessageOwnerTeam Lookup() => _lookup ??= _directory.Lookup(_time);

    // ── The world ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A Direct thread owned by <see cref="Caller"/> and shared to <see cref="Other"/> (the two-party list).</summary>
    private Guid DirectThread(bool isDefault = false, int type = ThreadTypeDirect)
    {
        var thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", thread,
            ("ownerid", new EntityReference("systemuser", Caller)),
            ("sprk_threadtype", new OptionSetValue(type)),
            ("sprk_isdefaultthread", isDefault));
        _shares.Seed("sprk_communicationthread", thread, DataversePrincipalRef.User(Other), Read);
        return thread;
    }

    /// <summary>A message owned by <paramref name="owner"/> in <paramref name="thread"/>, with one row per content table owned by the
    /// same owner, filed through <paramref name="filing"/>. R2: its sender (<see cref="Caller"/>) and recipient (<see cref="Other"/>)
    /// are stamped, as fm's Direct writer stamps them (the same two users as the thread's two-party list).</summary>
    private (Guid Message, IReadOnlyList<(string Table, Guid Id)> Rows) Message(
        Guid thread, (string Column, object Value) owner, params (string Column, string Target, Guid Id)[] filing)
    {
        var message = Guid.NewGuid();
        var columns = new List<(string, object)> { owner, ("sprk_communicationthread", new EntityReference("sprk_communicationthread", thread)) };
        columns.AddRange(filing.Select(f => (f.Column, (object)new EntityReference(f.Target, f.Id))));
        _world.Add("sprk_communication", message, columns.ToArray());
        Stamp(message, Caller, Other);
        var rows = new List<(string Table, Guid Id)> { ("sprk_communication", message) };
        foreach (var table in ContentTables)
        {
            var id = Guid.NewGuid();
            var lookup = table == "sprk_document" ? "sprk_relatedcommunication" : "sprk_communication";
            _world.Add(table, id, owner, (lookup, new EntityReference("sprk_communication", message)));
            rows.Add((table, id));
        }

        return (message, rows);
    }

    /// <summary>R2: stamps (or, for <c>null</c>, clears) the message's sender and recipient lookups.</summary>
    private void Stamp(Guid message, Guid? sender, Guid? recipient)
    {
        _world.Set("sprk_communication", message, "sprk_sentby", sender is { } s ? new EntityReference("systemuser", s) : null);
        _world.Set("sprk_communication", message, "sprk_directrecipient", recipient is { } r ? new EntityReference("systemuser", r) : null);
    }

    private static (string, object) OwnedBy(Guid team) => ("owningteam", new EntityReference("team", team));

    private (Guid Message, IReadOnlyList<(string Table, Guid Id)> Rows) DirectMessage(params (string Column, string Target, Guid Id)[] filing) =>
        Message(DirectThread(), OwnedBy(DirectTeam), filing);

    private bool Holds(string table, Guid id, Guid user) => (_shares.MaskOf(table, id, DataversePrincipalRef.User(user)) ?? 0) != 0;

    private int Grants() => _shares.WriteLog.Count(w => w.Action == "GrantAccess");

    private int Revokes() => _shares.WriteLog.Count(w => w.Action == "RevokeAccess");

    private Guid Matter(int accessPermission)
    {
        var matter = Guid.NewGuid();
        _world.OrdinaryRoot("sprk_matter", matter);
        _world.Set("sprk_matter", matter, InheritedAccessPermission.Column, new OptionSetValue(accessPermission));
        return matter;
    }

    // ── The scheduled job ────────────────────────────────────────────────────────────────────────────────────────────

    private SecureChildShareReconciliationJob Job(GateDecisionRecordSharing? gate = null)
    {
        var services = new ServiceCollection();
        if (gate is not null)
            services.AddSingleton(gate); // 198 pass 2 N2: the host registers it (SignalsModule)
        var provider = services
            .AddSingleton(_world.Synchronizer(_shares))
            .AddSingleton(SecureChildShareWorld.EntitiesOver(() => _world).Object)
            .AddSingleton<IDataverseRecordShareService>(_shares)
            .AddSingleton(Lookup())
            .AddSingleton<IConfiguration>(_configuration)
            .AddSingleton(SecureChildShareWorld.CoreAncestorsOver(() => _world))
            .AddSingleton<DataverseWebApiClient>(_users)
            .BuildServiceProvider();
        return new SecureChildShareReconciliationJob(
            provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), _time,
            NullLogger<SecureChildShareReconciliationJob>.Instance);
    }

    private static JobRunContext RunContext() =>
        new(Guid.NewGuid(), "corr-198c", JobRunTrigger.Scheduled, new Dictionary<string, object>());

    // ── The re-file step ─────────────────────────────────────────────────────────────────────────────────────────────

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, _shares, _users, directMessageOwnerTeam: Lookup());

    private Task RefileAsync(Guid message, bool wasIsolated = false) =>
        Reconciler().AfterRefileAsync("sprk_communication", message, () => Task.FromResult(wasIsolated), CancellationToken.None);

    /// <summary>The unfile fm's writer makes: the filing lookup cleared and the owner moved to the Direct team.</summary>
    private void UnfileToDirect(Guid message, string filingColumn)
    {
        _world.Set("sprk_communication", message, filingColumn, null);
        _world.MoveOwner("sprk_communication", message, DataversePrincipalRef.Team(DirectTeam));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // K3: the Direct-team identity never matches on a null id
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "198 C K3 must-fire: a refusal with no id never matches any owner, Guid.Empty included")]
    public void K3_ARefusalWithNoId_NeverMatches_GuidEmptyIncluded()
    {
        var refused = new DirectMessageOwnerTeamResolution(null, DirectMessageOwnerTeamRefusal.NotConfigured, null);
        var faulted = new DirectMessageOwnerTeamResolution(null, "unreadable", new HttpRequestException("x"));

        DirectMessageParticipantShares.Matches(refused, Guid.Empty).Should().BeFalse();
        DirectMessageParticipantShares.Matches(refused, DirectTeam).Should().BeFalse();
        DirectMessageParticipantShares.Matches(faulted, Guid.Empty).Should().BeFalse();
        DirectMessageParticipantShares.Matches(faulted, DirectTeam).Should().BeFalse();
        // An id carried beside a refusal is not an answer.
        DirectMessageParticipantShares.Matches(new DirectMessageOwnerTeamResolution(DirectTeam, "bu_has_users", null), DirectTeam)
            .Should().BeFalse();
        // A resolved empty id names no team.
        DirectMessageParticipantShares.Matches(new DirectMessageOwnerTeamResolution(Guid.Empty, null, null), Guid.Empty)
            .Should().BeFalse();
        // Resolved: exactly its own id.
        var resolved = new DirectMessageOwnerTeamResolution(DirectTeam, null, null);
        DirectMessageParticipantShares.Matches(resolved, DirectTeam).Should().BeTrue();
        DirectMessageParticipantShares.Matches(resolved, Guid.Empty).Should().BeFalse();
        DirectMessageParticipantShares.Matches(resolved, Other).Should().BeFalse();
    }

    [Fact(DisplayName = "198 C K3 must-fire: a refused lookup answers unknown for a team and no for an empty owner, never yes")]
    public async Task K3_ARefusedLookup_IsUnknownForATeam_AndNoForAnEmptyOwner()
    {
        _directory.WithUserInDirectUnit(Outsider); // the lookup refuses: bu_has_users

        (await DirectMessageParticipantShares.IsDirectOwnerTeamAsync(SecureChildShareWorld.EntitiesOver(() => _world).Object, Lookup(), DirectTeam, NullLogger.Instance, CancellationToken.None))
            .Should().BeNull("a refusal is unknown, never a match");
        (await DirectMessageParticipantShares.IsDirectOwnerTeamAsync(SecureChildShareWorld.EntitiesOver(() => _world).Object, Lookup(), Guid.Empty, NullLogger.Instance, CancellationToken.None))
            .Should().BeFalse("an empty owner is no team");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The scheduled backstop: grants what is missing, retries every unknown until it lands
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "198 C must-fire: the backstop grants every participant on a Direct message and its content rows")]
    public async Task Job_GrantsEveryParticipant_OnTheMessageAndItsContentRows()
    {
        var (_, rows) = DirectMessage();

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeTrue($"the thread's owner reads {table}");
            Holds(table, id, Other).Should().BeTrue($"the thread's sharee reads {table}");
        }

        run.ResultJson.Should().Contain("\"directMessages\"").And.Contain("\"granted\":10");
    }

    [Fact(DisplayName = "198 C must-fire: the backstop grants a missing content-row share, and only that one")]
    public async Task Job_GrantsAMissingContentRowShare_AndOnlyThatOne()
    {
        var (_, rows) = DirectMessage();
        var attachment = rows.Single(r => r.Table == "sprk_communicationattachment").Id;
        foreach (var (table, id) in rows)
            foreach (var user in new[] { Caller, Other })
            {
                if (id != attachment || user != Other)
                    _shares.Seed(table, id, DataversePrincipalRef.User(user), Read);
            }

        await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        _shares.WriteLog.Should().ContainSingle()
            .Which.Should().Be(("GrantAccess", "sprk_communicationattachments", attachment, DataversePrincipalRef.User(Other), (string?)"ReadAccess"));
    }

    [Fact(DisplayName = "198 C must-fire: a failed grant is retried by the next run and lands; a run after that writes nothing")]
    public async Task Job_AFailedGrant_IsRetried_AndLands_AndTheNextRunWritesNothing()
    {
        var (message, rows) = DirectMessage();
        _shares.FailWritesOnRecord = ("sprk_communications", message);
        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        first.Success.Should().BeFalse("the message grant failed");
        first.ErrorMessage.Should().Contain("Direct messages");
        Holds("sprk_communication", message, Caller).Should().BeFalse();

        _shares.FailWritesOnRecord = null;
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        second.Success.Should().BeTrue(second.ErrorMessage);
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeTrue();
            Holds(table, id, Other).Should().BeTrue();
        }

        var writes = _shares.WriteLog.Count;
        _time.Advance(TimeSpan.FromMinutes(2));
        var third = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        third.Success.Should().BeTrue();
        _shares.WriteLog.Count.Should().Be(writes, "every participant already holds Read: idempotent");
    }

    [Fact(DisplayName = "198 C must-fire: the Direct-team lookup faults, nothing is granted; once it recovers, the participants are granted")]
    public async Task Job_WhenTheDirectLookupFaults_GrantsNothing_ThenGrantsOnceItRecovers()
    {
        var (message, _) = DirectMessage();
        _directory.Faults = true;
        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        first.Success.Should().BeFalse();
        first.ResultJson.Should().Contain(SecureChildShareReconciliationJob.DirectMessageRun.StatusTeamUnknown);
        Grants().Should().Be(0);

        _directory.Faults = false;
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        second.Success.Should().BeTrue(second.ErrorMessage);
        Holds("sprk_communication", message, Caller).Should().BeTrue();
        Holds("sprk_communication", message, Other).Should().BeTrue();
    }

    [Fact(DisplayName = "198 C must-fire: the Direct team is refused, nothing is granted; once the refusal clears, the participants are granted")]
    public async Task Job_WhenTheDirectTeamIsRefused_GrantsNothing_ThenGrantsOnceTheRefusalClears()
    {
        var (message, _) = DirectMessage();
        _directory.WithUserInDirectUnit(Outsider);
        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        first.Success.Should().BeFalse();
        Grants().Should().Be(0);

        _directory.WithoutUsersInDirectUnit();
        _time.Advance(DirectMessageOwnerTeam.CacheLifetime + TimeSpan.FromSeconds(1));
        var second = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        second.Success.Should().BeTrue(second.ErrorMessage);
        Holds("sprk_communication", message, Caller).Should().BeTrue();
        Holds("sprk_communication", message, Other).Should().BeTrue();
    }

    [Fact(DisplayName = "198 C must-fire: whether the owner is the Secure team cannot be told, nothing is granted; once it is known ordinary, it is granted")]
    public async Task Job_WhenSecureIsUnknown_GrantsNothing_ThenGrantsOnceKnownOrdinary()
    {
        var (message, _) = DirectMessage();
        _configuration["SecureRecord:OwnerTeamName"] = "Secure Owners That Do Not Exist";
        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        first.Success.Should().BeFalse();
        Grants().Should().Be(0);

        _configuration["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName;
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        second.Success.Should().BeTrue(second.ErrorMessage);
        Holds("sprk_communication", message, Caller).Should().BeTrue();
        Holds("sprk_communication", message, Other).Should().BeTrue();
    }

    [Fact(DisplayName = "198 C must-fire: a capped run says so and carries the rest to the next run")]
    public async Task Job_ACappedRun_SaysSo_AndCarriesTheRestToTheNextRun()
    {
        var thread = DirectThread();
        var recent = _time.GetUtcNow().UtcDateTime.AddMinutes(-5);
        var messages = new List<Guid>();
        for (var i = 0; i < SecureChildShareReconciliationJob.MaxDirectMessagesPerRun + 1; i++)
        {
            var message = Guid.NewGuid();
            _world.Add("sprk_communication", message, OwnedBy(DirectTeam),
                ("sprk_communicationthread", new EntityReference("sprk_communicationthread", thread)), ("modifiedon", recent));
            Stamp(message, Caller, Other);
            messages.Add(message);
        }

        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        first.ResultJson.Should().Contain("\"capReached\":true");
        var budget = SecureChildShareReconciliationJob.MaxDirectMessagesPerRun - SecureChildShareReconciliationJob.DirectSweepReserve;
        first.ResultJson.Should().Contain($"\"carried\":{messages.Count - budget}");
        messages.Count(m => Holds("sprk_communication", m, Caller)).Should().Be(budget,
            "the sweep's reserve went to the same oldest ids, so the run granted exactly its priority budget");

        _time.Advance(TimeSpan.FromMinutes(2));
        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        messages.Should().OnlyContain(m => Holds("sprk_communication", m, Caller) && Holds("sprk_communication", m, Other));
    }

    // ── Fix round 1 R4: bounded, but nothing starves ──

    /// <summary>Direct-team messages on one Direct thread (no content rows), each changed at <paramref name="modifiedOn"/>(i), or
    /// never (null).</summary>
    private List<Guid> DirectMessages(int count, Func<int, DateTime?> modifiedOn, Func<int, Guid>? id = null)
    {
        var thread = DirectThread();
        var messages = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var message = id?.Invoke(i) ?? Guid.NewGuid();
            var columns = new List<(string, object)>
            {
                OwnedBy(DirectTeam), ("sprk_communicationthread", new EntityReference("sprk_communicationthread", thread)),
            };
            if (modifiedOn(i) is { } at)
                columns.Add(("modifiedon", at));
            _world.Add("sprk_communication", message, columns.ToArray());
            Stamp(message, Caller, Other);
            messages.Add(message);
        }

        return messages;
    }

    private async Task<int> RunUntilAsync(SecureChildShareReconciliationJob job, Func<bool> done, int maxRuns, Action? beforeEach = null)
    {
        for (var run = 1; run <= maxRuns; run++)
        {
            beforeEach?.Invoke();
            await job.ExecuteAsync(RunContext(), CancellationToken.None);
            if (done())
                return run;
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        return -1;
    }

    [Fact(DisplayName = "198 R4 must-fire: more changed rows than one listing holds drain over several runs, and a capped listing is not Ok")]
    public async Task Job_MoreChangedRowsThanOneListing_DrainOverSeveralRuns()
    {
        var start = _time.GetUtcNow().UtcDateTime.AddMinutes(-40);
        var messages = DirectMessages(SecureChildShareReconciliationJob.MaxDirectListedPerTable + 20, i => start.AddSeconds(i));
        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);
        first.Success.Should().BeFalse("a capped listing leaves changed rows unlisted");
        first.ResultJson.Should().Contain("\"listingCapped\":true");
        _time.Advance(TimeSpan.FromMinutes(2));

        var runs = await RunUntilAsync(job, () => messages.All(m => Holds("sprk_communication", m, Caller)), maxRuns: 10);

        runs.Should().BePositive("every changed message is listed by a later run once the watermark moves to the last row listed");
    }

    [Fact(DisplayName = "198 R4 must-fire: the sweep progresses while more than a run's budget of messages keeps changing")]
    public async Task Job_TheSweepProgresses_WhileRecentChangesExceedTheBudget()
    {
        var busy = DirectMessages(SecureChildShareReconciliationJob.MaxDirectMessagesPerRun + 50, _ => _time.GetUtcNow().UtcDateTime);
        var quiet = DirectMessages(1, _ => null).Single(); // never listed as changed: only the sweep reaches it
        var job = Job();

        var runs = await RunUntilAsync(job, () => Holds("sprk_communication", quiet, Caller), maxRuns: 10, beforeEach: () =>
        {
            foreach (var message in busy)
                _world.Modified("sprk_communication", message, _time.GetUtcNow().UtcDateTime);
        });

        runs.Should().BePositive("part of every run's budget is the sweep's");
    }

    [Fact(DisplayName = "198 R4 must-fire: messages that fail every run do not starve the others, and are backed off")]
    public async Task Job_PermanentlyFailingMessages_DoNotStarveTheOthers()
    {
        // The failing messages sort first (smaller ids, the same change time), so without a reserve or a backoff they fill every run.
        var ids = Enumerable.Range(0, SecureChildShareReconciliationJob.MaxDirectMessagesPerRun + 20).Select(_ => Guid.NewGuid())
            .OrderBy(id => id).ToList();
        var at = _time.GetUtcNow().UtcDateTime.AddMinutes(-5);
        var messages = DirectMessages(ids.Count, _ => at, i => ids[i]);
        var failing = messages.Take(SecureChildShareReconciliationJob.MaxDirectMessagesPerRun).ToList();
        var good = messages.Skip(failing.Count).ToList();
        foreach (var message in failing)
            _world.FailingRowReadsOf("sprk_communication", message);
        var job = Job();

        var runs = await RunUntilAsync(job, () => good.All(m => Holds("sprk_communication", m, Caller)), maxRuns: 10);

        runs.Should().BePositive("the others are granted while the failing messages keep failing");
    }

    [Fact(DisplayName = "198 R4 must-fire: a message incomplete three runs in a row is backed off and not tried again until its time")]
    public async Task Job_AMessageThatKeepsFailing_IsBackedOff()
    {
        var (message, _) = DirectMessage();
        _world.FailingRowReadsOf("sprk_communication", message);
        var job = Job();

        for (var run = 0; run < 3; run++)
        {
            await job.ExecuteAsync(RunContext(), CancellationToken.None);
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        var backedOff = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        backedOff.Success.Should().BeFalse("a backed-off message is still not granted");
        backedOff.ResultJson.Should().Contain("\"backedOff\":1").And.Contain("\"examined\":0");

        _time.Advance(SecureChildShareReconciliationJob.DirectBackoff);
        var retried = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        retried.ResultJson.Should().Contain("\"examined\":1", "after the backoff it is tried again");
    }

    [Fact(DisplayName = "198 R5 must-fire: an owner that changes between the read and the write leaves nothing written, and is retried")]
    public async Task Reconcile_AnOwnerThatChangesBeforeTheWrite_WritesNothing_AndIsRetried()
    {
        var (message, _) = DirectMessage();
        _world.AfterQueriesOf("sprk_communication", 1,
            w => w.MoveOwner("sprk_communication", message, DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam)));

        var outcome = await new DirectMessageParticipantShares(
            SecureChildShareWorld.EntitiesOver(() => _world).Object, _shares, Lookup(), _configuration,
            SecureChildShareWorld.ColumnProbe(), _users, NullLogger.Instance).AfterRefileAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeFalse();
        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.Unknown);
        _shares.WriteLog.Should().BeEmpty();
    }

    // ── Must-not-fire ──

    [Fact(DisplayName = "198 C must-not-fire: a non-participant is never granted, and a non-participant's share on the message is removed")]
    public async Task Job_NeverGrantsANonParticipant()
    {
        var (message, rows) = DirectMessage();
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);

        await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        _shares.WriteLog.Where(w => w.Action == "GrantAccess")
            .Should().OnlyContain(w => w.Principal == DataversePrincipalRef.User(Caller) || w.Principal == DataversePrincipalRef.User(Other));
        foreach (var (table, id) in rows)
            Holds(table, id, Outsider).Should().BeFalse($"{table}: the Outsider is not on the thread");
    }

    [Fact(DisplayName = "198 C must-not-fire: an external participant on a message filed under a Restricted record (inherited) is never granted")]
    public async Task Job_AnExternalParticipant_UnderAnInheritedRestrictedFiling_IsNeverGranted()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var evt = Guid.NewGuid();
        _world.OrdinaryChild("sprk_event", evt, ("sprk_regardingmatter", "sprk_matter", matter));
        var (_, rows) = DirectMessage(("sprk_regardingevent", "sprk_event", evt));
        _users.Flags[Other] = true;

        var job = Job();
        await job.ExecuteAsync(RunContext(), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(2));
        await job.ExecuteAsync(RunContext(), CancellationToken.None);

        foreach (var (table, id) in rows)
        {
            Holds(table, id, Other).Should().BeFalse($"{table} inherits Restricted and the user is flagged external");
            Holds(table, id, Caller).Should().BeTrue($"an internal participant reads {table}");
        }
    }

    [Fact(DisplayName = "198 C must-not-fire: an external participant on a message whose own Access Permission is Restricted is never granted")]
    public async Task Job_AnExternalParticipant_OnAMessageRestrictedByItsOwnValue_IsNeverGranted()
    {
        var (message, _) = DirectMessage();
        _world.Set("sprk_communication", message, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));
        _users.Flags[Other] = true;

        await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        Holds("sprk_communication", message, Other).Should().BeFalse();
        Holds("sprk_communication", message, Caller).Should().BeTrue();
    }

    [Fact(DisplayName = "198 C must-not-fire: a message the Secure team owns is never granted by the Direct pass")]
    public async Task Refile_ASecureOwnedMessage_IsNeverGrantedByTheDirectStep()
    {
        // The Direct lookup itself names the Secure team (a misconfiguration): the Secure answer still wins.
        var directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(_world.OwnerTeam);
        var secureRoot = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", secureRoot);
        var thread = DirectThread();
        var message = Guid.NewGuid();
        _world.SecureChild("sprk_communication", message, ("sprk_regardingmatter", "sprk_matter", secureRoot));
        _world.Set("sprk_communication", message, "sprk_communicationthread", new EntityReference("sprk_communicationthread", thread));
        Stamp(message, Caller, Other);

        var shares = new DirectMessageParticipantShares(
            SecureChildShareWorld.EntitiesOver(() => _world).Object, _shares, directory.Lookup(_time), _configuration,
            SecureChildShareWorld.ColumnProbe(), _users, NullLogger.Instance);
        var outcome = await shares.AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.Secure);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact(DisplayName = "198 C AC6 pin: the per-user master thread grants and revokes nothing, and is not retried")]
    public async Task Job_ThePerUserMasterThread_IsUnchanged()
    {
        var (message, _) = Message(DirectThread(isDefault: true), OwnedBy(DirectTeam));
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        _shares.WriteLog.Should().BeEmpty();
        Holds("sprk_communication", message, Outsider).Should().BeTrue();
    }

    [Fact(DisplayName = "198 C AC6 pin: a record-anchored thread's message under the Direct team grants and revokes nothing")]
    public async Task Job_ARecordAnchoredThread_IsUnchanged()
    {
        var (message, _) = Message(DirectThread(type: ThreadTypeRecordAnchored), OwnedBy(DirectTeam));

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact(DisplayName = "198 C AC6 pin: user-owned Direct messages (before fm 054a) and unfiled inbound email are never listed or touched")]
    public async Task Job_UserOwnedMessages_AreUnchanged()
    {
        var thread = DirectThread();
        var (preDirect, _) = Message(thread, ("owninguser", new EntityReference("systemuser", Caller)));
        var (inbound, _) = Message(DirectThread(type: ThreadTypeRecordAnchored), ("owninguser", new EntityReference("systemuser", Other)));
        _shares.Seed("sprk_communication", preDirect, DataversePrincipalRef.User(Outsider), Read);
        _shares.Seed("sprk_communication", inbound, DataversePrincipalRef.User(Outsider), Read);

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        _shares.WriteLog.Should().BeEmpty();
    }

    [Fact(DisplayName = "198 C: with no Direct team configured the pass reports not-configured and writes nothing")]
    public async Task Job_WithNoDirectTeamConfigured_WritesNothing()
    {
        DirectMessage();
        _lookup = DirectMessageOwnerTeamDouble.Unconfigured();

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        run.ResultJson.Should().Contain(SecureChildShareReconciliationJob.DirectMessageRun.StatusNotConfigured);
        _shares.WriteLog.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The re-file step: unfile or secure exit back to the Direct team
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "198 C must-fire: unfiled back to the Direct team, the anchor record's grantees lose the message; the participants keep it")]
    public async Task Refile_UnfiledBackToDirect_RevokesTheAnchorGrantees_AndKeepsTheParticipants()
    {
        var matter = Matter(InheritedAccessPermission.Standard);
        var (message, rows) = Message(DirectThread(), OwnedBy(SecureChildShareWorld.GeneralTeam), ("sprk_regardingmatter", "sprk_matter", matter));
        foreach (var user in new[] { Caller, Other, AnchorGrantee })
            _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(user), Read);
        foreach (var (table, id) in rows.Skip(1))
            _world.MoveOwner(table, id, DataversePrincipalRef.Team(DirectTeam));

        UnfileToDirect(message, "sprk_regardingmatter");
        await RefileAsync(message);

        Holds("sprk_communication", message, AnchorGrantee).Should().BeFalse("the anchor record's grantee is not on the thread");
        _shares.WriteLog.Should().Contain(w => w.Action == "RevokeAccess" && w.Principal == DataversePrincipalRef.User(AnchorGrantee));
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeTrue($"a participant keeps {table}");
            Holds(table, id, Other).Should().BeTrue($"a participant keeps {table}");
        }
    }

    [Fact(DisplayName = "198 C must-fire: a secure exit back to the Direct team leaves exactly the participants")]
    public async Task Refile_ASecureExitBackToDirect_LeavesExactlyTheParticipants()
    {
        var secureRoot = Guid.NewGuid();
        _world.SecureRoot("sprk_matter", secureRoot);
        _shares.Seed("sprk_matter", secureRoot, DataversePrincipalRef.User(AnchorGrantee), Read);
        var thread = DirectThread();
        var message = Guid.NewGuid();
        _world.SecureChild("sprk_communication", message, ("sprk_regardingmatter", "sprk_matter", secureRoot));
        _world.Set("sprk_communication", message, "sprk_communicationthread", new EntityReference("sprk_communicationthread", thread));
        Stamp(message, Caller, Other);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(AnchorGrantee), Read); // the mirror

        UnfileToDirect(message, "sprk_regardingmatter");
        await RefileAsync(message, wasIsolated: true);

        Holds("sprk_communication", message, AnchorGrantee).Should().BeFalse();
        Holds("sprk_communication", message, Caller).Should().BeTrue();
        Holds("sprk_communication", message, Other).Should().BeTrue();
    }

    [Fact(DisplayName = "198 C AC6 pin: a record-anchored re-file (to an ordinary record) is unchanged by the Direct step")]
    public async Task Refile_ToAnOrdinaryRecord_IsUnchangedByTheDirectStep()
    {
        var matter = Matter(InheritedAccessPermission.Standard);
        var (message, rows) = Message(DirectThread(), OwnedBy(SecureChildShareWorld.GeneralTeam), ("sprk_regardingmatter", "sprk_matter", matter));
        foreach (var (table, id) in rows)
            _shares.Seed(table, id, DataversePrincipalRef.User(AnchorGrantee), Read);

        await RefileAsync(message);

        foreach (var (table, id) in rows)
            Holds(table, id, AnchorGrantee).Should().BeTrue($"{table} is filed under an ordinary record: its grantees stay");
        Holds("sprk_communication", message, Caller).Should().BeFalse("the Direct step grants only Direct-team-owned messages");
        Revokes().Should().Be(0);
    }

    [Fact(DisplayName = "198 C AC6 pin: a user-owned message re-filed is unchanged by the Direct step")]
    public async Task Refile_AUserOwnedMessage_IsUnchangedByTheDirectStep()
    {
        var (message, _) = Message(DirectThread(), ("owninguser", new EntityReference("systemuser", Caller)));
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);

        var outcome = await new DirectMessageParticipantShares(
            SecureChildShareWorld.EntitiesOver(() => _world).Object, _shares, Lookup(), _configuration,
            SecureChildShareWorld.ColumnProbe(), _users, NullLogger.Instance).AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.NotDirect);
        _shares.WriteLog.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Fix round 1 R2: the participants are the message's BFF-stamped pair (sprk_sentby, sprk_directrecipient), never the thread's
    // owner or sharees. An invalid pair grants and revokes nothing and is settled; a read fault is retried.
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private DirectMessageParticipantShares Reconciler198() => new(
        SecureChildShareWorld.EntitiesOver(() => _world).Object, _shares, Lookup(), _configuration,
        SecureChildShareWorld.ColumnProbe(), _users, NullLogger.Instance);

    [Fact(DisplayName = "198 R2 must-fire: the job grants the stamped pair and revokes a third user's share on the message")]
    public async Task R2_Job_GrantsTheStampedPair_AndRevokesAThirdUsersShare()
    {
        var (message, rows) = DirectMessage();
        Stamp(message, Caller, Stamped);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeTrue($"the stamped sender reads {table}");
            Holds(table, id, Stamped).Should().BeTrue($"the stamped recipient reads {table}");
        }

        Holds("sprk_communication", message, Outsider).Should().BeFalse("a user outside the pair loses the message");
        _shares.WriteLog.Should().Contain(w => w.Action == "RevokeAccess" && w.Principal == DataversePrincipalRef.User(Outsider));
    }

    [Fact(DisplayName = "198 R2 must-fire: the thread's sharee is not granted when it is not in the pair (the old source is gone)")]
    public async Task R2_TheThreadsSharee_IsNotGranted_WhenNotInThePair()
    {
        // The thread is owned by Caller and shared to Other; the message's pair is Caller -> Stamped.
        var (message, rows) = DirectMessage();
        Stamp(message, Caller, Stamped);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Other), Read);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.Reconciled);
        outcome.Complete.Should().BeTrue();
        _shares.WriteLog.Where(w => w.Action == "GrantAccess")
            .Should().OnlyContain(w => w.Principal == DataversePrincipalRef.User(Caller) || w.Principal == DataversePrincipalRef.User(Stamped));
        foreach (var (table, id) in rows)
            Holds(table, id, Other).Should().BeFalse($"{table}: the thread's sharee is not the message's recipient");
    }

    [Fact(DisplayName = "198 R2 must-fire: the thread's owner is not granted when it is not in the pair")]
    public async Task R2_TheThreadsOwner_IsNotGranted_WhenNotInThePair()
    {
        var (message, rows) = DirectMessage();
        Stamp(message, Other, Stamped);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeTrue();
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeFalse($"{table}: the thread's owner is not in the pair");
            Holds(table, id, Other).Should().BeTrue();
            Holds(table, id, Stamped).Should().BeTrue();
        }
    }

    [Fact(DisplayName = "198 R2 must-fire: the message row read selects the sender and the recipient by name, with the owner and the thread")]
    public async Task R2_TheMessageRead_SelectsTheSenderAndTheRecipient()
    {
        var (message, _) = DirectMessage();

        await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        _world.Queries.Should().Contain(q => q.EntityName == "sprk_communication"
            && q.ColumnSet.Columns.Contains("owningteam") && q.ColumnSet.Columns.Contains("sprk_communicationthread")
            && q.ColumnSet.Columns.Contains("sprk_sentby") && q.ColumnSet.Columns.Contains("sprk_directrecipient"));
    }

    [Fact(DisplayName = "198 R2 must-fire: no recipient stamped, the job grants and revokes nothing, settles it, and does not carry it")]
    public async Task R2_Job_NoRecipient_GrantsAndRevokesNothing_IsSettled_AndIsNotCarried_UntilItIsStamped()
    {
        var (message, rows) = DirectMessage();
        Stamp(message, Caller, null);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);
        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        first.Success.Should().BeTrue(first.ErrorMessage);
        first.ResultJson.Should().Contain("\"incomplete\":0").And.Contain("\"carried\":0").And.Contain("\"examined\":1");
        _shares.WriteLog.Should().BeEmpty("an unstamped pair grants and revokes nothing");
        Holds("sprk_communication", message, Outsider).Should().BeTrue();

        // A later repair stamps the recipient (modifiedon moves): the pass lists it again and grants the pair.
        Stamp(message, Caller, Other);
        _world.Modified("sprk_communication", message, _time.GetUtcNow().UtcDateTime);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        second.Success.Should().BeTrue(second.ErrorMessage);
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeTrue();
            Holds(table, id, Other).Should().BeTrue();
        }
    }

    [Fact(DisplayName = "198 R2 must-fire: no recipient stamped is a settled NoAudience outcome, not a retry")]
    public async Task R2_NoRecipient_IsSettledNoAudience()
    {
        var (message, _) = DirectMessage();
        Stamp(message, Caller, null);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.NoAudience);
        outcome.Complete.Should().BeTrue();
        outcome.Detail.Should().Contain("recipient");
        _shares.WriteLog.Should().BeEmpty();
    }

    [Theory(DisplayName = "198 R2 must-fire: an invalid pair grants and revokes nothing, and is settled")]
    [InlineData("no sender")]
    [InlineData("neither")]
    [InlineData("sender is recipient")]
    [InlineData("recipient is a contact")]
    [InlineData("sender is a team")]
    public async Task R2_AnInvalidPair_GrantsAndRevokesNothing(string shape)
    {
        var (message, _) = DirectMessage();
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);
        switch (shape)
        {
            case "no sender": Stamp(message, null, Other); break;
            case "neither": Stamp(message, null, null); break;
            case "sender is recipient": Stamp(message, Caller, Caller); break;
            case "recipient is a contact":
                _world.Set("sprk_communication", message, "sprk_directrecipient", new EntityReference("contact", Other));
                break;
            case "sender is a team":
                _world.Set("sprk_communication", message, "sprk_sentby", new EntityReference("team", Caller));
                break;
        }

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.NoAudience);
        outcome.Complete.Should().BeTrue();
        _shares.WriteLog.Should().BeEmpty("an invalid pair grants nothing and revokes nothing");
        Holds("sprk_communication", message, Outsider).Should().BeTrue();
    }

    [Fact(DisplayName = "198 R2 must-fire: a pair absent from the returned row grants nothing and revokes nothing")]
    public async Task R2_APairAbsentFromTheReturnedRow_GrantsAndRevokesNothing()
    {
        // The row as Dataverse answers a select that did not take the lookups, or a row never stamped: the attributes are not in it.
        var (message, rows) = DirectMessage();
        Stamp(message, null, null);
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        _shares.WriteLog.Should().BeEmpty();
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeFalse("the thread's owner is not a source");
            Holds(table, id, Other).Should().BeFalse("the thread's sharee is not a source");
        }

        Holds("sprk_communication", message, Outsider).Should().BeTrue("nothing is revoked");
    }

    [Fact(DisplayName = "198 R2 must-fire: a disabled recipient is not granted; the sender still is, and the outcome is complete")]
    public async Task R2_ADisabledRecipient_IsNotGranted_TheSenderIs_AndItIsComplete()
    {
        var (message, rows) = DirectMessage();
        _users.Disabled[Other] = true;

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.Reconciled);
        outcome.Complete.Should().BeTrue("a disabled participant is settled, not retried");
        foreach (var (table, id) in rows)
        {
            Holds(table, id, Caller).Should().BeTrue($"the sender reads {table}");
            Holds(table, id, Other).Should().BeFalse($"the disabled recipient is not granted {table}");
        }

        _users.Selects.Should().OnlyContain(select => select.Split(',').Contains("isdisabled"), "the one user read carries isdisabled");
    }

    [Fact(DisplayName = "198 R2 must-fire: whether a participant is disabled cannot be read, it is not granted and the message is retried")]
    public async Task R2_AnUnreadableDisabledFlag_IsNotGranted_AndIsRetried()
    {
        var (message, _) = DirectMessage();
        _users.Disabled[Other] = null;

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeFalse();
        Holds("sprk_communication", message, Other).Should().BeFalse();
        Holds("sprk_communication", message, Caller).Should().BeTrue();
    }

    [Fact(DisplayName = "198 R2 must-fire: the user read faults, nothing is granted and the message is retried")]
    public async Task R2_AUserReadFault_GrantsNothing_AndIsRetried()
    {
        var (message, _) = DirectMessage();
        _users.Faults = true;

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeFalse();
        Grants().Should().Be(0);
    }

    [Fact(DisplayName = "198 R2 must-fire: a fault reading the message is retried, nothing written")]
    public async Task R2_AFaultReadingTheMessage_IsRetried()
    {
        var (message, _) = DirectMessage();
        _world.FailingRowReadsOf("sprk_communication", message);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.Unknown);
        outcome.Complete.Should().BeFalse();
        _shares.WriteLog.Should().BeEmpty();
    }

    // ── Must-not-fire ──

    [Fact(DisplayName = "198 R2 must-not-fire: a valid pair already holding exactly its shares writes nothing")]
    public async Task R2_AValidPairHoldingItsShares_WritesNothing()
    {
        var (message, rows) = DirectMessage();
        Stamp(message, Caller, Stamped);
        foreach (var (table, id) in rows)
            foreach (var user in new[] { Caller, Stamped })
                _shares.Seed(table, id, DataversePrincipalRef.User(user), Read);

        var run = await Job().ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        _shares.WriteLog.Should().BeEmpty("idempotent");
    }

    [Fact(DisplayName = "198 R2 must-not-fire: a thread that is not Direct is NoAudience with a valid pair stamped, unchanged")]
    public async Task R2_ANonDirectThread_IsNoAudience_WithAValidPair()
    {
        var (message, _) = Message(DirectThread(type: ThreadTypeRecordAnchored), OwnedBy(DirectTeam));
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Outsider), Read);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Status.Should().Be(DirectMessageParticipantShares.Status.NoAudience);
        outcome.Detail.Should().Be("its thread is not a Direct thread");
        outcome.Complete.Should().BeTrue();
        _shares.WriteLog.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Pass 2, F1: a content row re-owned between its listing and its grant keeps no participant Read
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The message's attachment is re-owned to <paramref name="team"/> right after its first grant lands (a filing into a
    /// Secure or Restricted record in between).</summary>
    private Guid ReownAttachmentOnItsFirstGrant(IReadOnlyList<(string Table, Guid Id)> rows, Guid team)
    {
        var attachment = rows.Single(r => r.Table == "sprk_communicationattachment").Id;
        _shares.AfterGrant = (_, id, _) =>
        {
            if (id == attachment)
                _world.MoveOwner("sprk_communicationattachment", attachment, DataversePrincipalRef.Team(team));
        };
        return attachment;
    }

    [Theory(DisplayName = "198 pass 2 F1 must-fire: a content row re-owned during its grant keeps no participant Read, and the message is retried")]
    [InlineData("secure")]
    [InlineData("restricted")]
    public async Task F1_Reconcile_AContentRowReownedDuringItsGrant_KeepsNoRead_AndIsRetried(string target)
    {
        var (message, rows) = DirectMessage();
        var attachment = ReownAttachmentOnItsFirstGrant(rows,
            target == "secure" ? SecureChildShareWorld.SecureTeam : SecureChildShareWorld.RestrictedContentTeam);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        Holds("sprk_communicationattachment", attachment, Caller).Should().BeFalse("its owner is no longer the Direct team");
        Holds("sprk_communicationattachment", attachment, Other).Should().BeFalse();
        outcome.Complete.Should().BeFalse("a mismatch is retried, never success");
        outcome.Granted.Should().NotContain(g => g.RowId == attachment);
        foreach (var (table, id) in rows.Where(r => r.Id != attachment))
            Holds(table, id, Caller).Should().BeTrue($"{table} is still the Direct team's");
    }

    [Fact(DisplayName = "198 pass 2 F1 must-fire (job): a content row re-owned to Secure mid-loop keeps no Read, the run is not Ok, the next run settles")]
    public async Task F1_Job_AContentRowReownedToSecureMidLoop_KeepsNoRead_AndTheRunIsRetried()
    {
        var (_, rows) = DirectMessage();
        var attachment = ReownAttachmentOnItsFirstGrant(rows, SecureChildShareWorld.SecureTeam);
        var job = Job();

        var first = await job.ExecuteAsync(RunContext(), CancellationToken.None);
        _shares.AfterGrant = null;
        var second = await job.ExecuteAsync(RunContext(), CancellationToken.None);

        first.Success.Should().BeFalse("the job reports the mismatch so the message is retried");
        Holds("sprk_communicationattachment", attachment, Caller).Should().BeFalse();
        Holds("sprk_communicationattachment", attachment, Other).Should().BeFalse();
        second.Success.Should().BeTrue(second.ErrorMessage);
        Holds("sprk_communicationattachment", attachment, Caller).Should().BeFalse("the row is no longer listed as the Direct team's");
    }

    [Fact(DisplayName = "198 pass 2 F1 must-fire: a content row whose owner cannot be read back after its grant keeps no Read, and is retried")]
    public async Task F1_Reconcile_AnUnreadableContentRowOwner_KeepsNoRead_AndIsRetried()
    {
        var (message, rows) = DirectMessage();
        var attachment = rows.Single(r => r.Table == "sprk_communicationattachment").Id;
        _world.TransientRowReadFaultsOf("sprk_communicationattachment", attachment, _ => true);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        _world.TransientRowReadFaultsFired("sprk_communicationattachment", attachment).Should().BeGreaterThan(0);
        Holds("sprk_communicationattachment", attachment, Caller).Should().BeFalse("unknown is never success");
        Holds("sprk_communicationattachment", attachment, Other).Should().BeFalse();
        outcome.Complete.Should().BeFalse();
    }

    [Fact(DisplayName = "198 pass 2 F1 must-not-fire: content rows whose owner is unchanged are granted, nothing is revoked, and it is complete")]
    public async Task F1_Reconcile_AnUnchangedContentRowOwner_IsGranted_AndComplete()
    {
        var (message, rows) = DirectMessage();

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeTrue();
        foreach (var (table, id) in rows)
            foreach (var user in new[] { Caller, Other })
                Holds(table, id, user).Should().BeTrue($"{table}");
        Revokes().Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // 198 fix round K1/K2: every ATTEMPTED content-row grant is undone when the read-back does not confirm the Direct team (a
    // grant that faulted may have committed; a cancellation does not skip the undo), and a share held before the call is never
    // revoked (a user holding any share is not granted here)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private const int Write = 2;

    [Fact(DisplayName = "198 K1 must-fire: a content-row grant that throws after it was applied, on a row re-owned to Secure, is revoked")]
    public async Task K1_AGrantThatThrowsAfterItWasApplied_OnARowReownedToSecure_IsRevoked()
    {
        var (message, rows) = DirectMessage();
        var attachment = rows.Single(r => r.Table == "sprk_communicationattachment").Id;
        _shares.Seed("sprk_communicationattachment", attachment, DataversePrincipalRef.User(Caller), Read); // Other is the only grant
        _shares.AfterGrant = (_, id, _) =>
        {
            if (id != attachment)
                return;
            _world.MoveOwner("sprk_communicationattachment", attachment, DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
            throw new TimeoutException("Test: the grant committed, then the call timed out.");
        };

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        Holds("sprk_communicationattachment", attachment, Other).Should().BeFalse(
            "a grant that faulted may have committed; the row is Secure on read-back, so the attempt is undone");
        Holds("sprk_communicationattachment", attachment, Caller).Should().BeTrue("a share held before the call is never touched");
        outcome.Complete.Should().BeFalse();
        outcome.Granted.Should().NotContain(g => g.RowId == attachment);
    }

    [Fact(DisplayName = "198 K1 must-fire: a cancellation after a content-row grant still revokes it, and the cancellation still propagates")]
    public async Task K1_ACancellationAfterAContentRowGrant_StillRevokesIt_AndStillThrows()
    {
        var (message, rows) = DirectMessage();
        var attachment = rows.Single(r => r.Table == "sprk_communicationattachment").Id;
        _shares.Seed("sprk_communicationattachment", attachment, DataversePrincipalRef.User(Caller), Read); // Other is the only grant
        using var cts = new CancellationTokenSource();
        _shares.AfterGrant = (_, id, _) =>
        {
            if (id != attachment)
                return;
            cts.Cancel();
            throw new OperationCanceledException(cts.Token); // the server applied the grant; the caller's token was observed after it
        };

        var act = () => Reconciler198().AfterRefileAsync(message, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("the caller's cancellation propagates after the undo");
        Holds("sprk_communicationattachment", attachment, Other).Should().BeFalse("the undo runs on its own token, not the cancelled one");
        Holds("sprk_communicationattachment", attachment, Caller).Should().BeTrue("a share held before the call is never touched");
    }

    [Fact(DisplayName = "198 K2 must-not-fire: a participant holding a Write-only share on a content row is not granted Read, and keeps that share when the read-back fails")]
    public async Task K2_AWriteOnlyShare_IsNotGrantedRead_AndIsNotRevoked_WhenTheReadBackFails()
    {
        var (message, rows) = DirectMessage();
        var attachment = rows.Single(r => r.Table == "sprk_communicationattachment").Id;
        _shares.Seed("sprk_communicationattachment", attachment, DataversePrincipalRef.User(Other), Write);
        ReownAttachmentOnItsFirstGrant(rows, SecureChildShareWorld.SecureTeam);

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        _shares.MaskOf("sprk_communicationattachment", attachment, DataversePrincipalRef.User(Other)).Should().Be(Write,
            "a share held before the call is never revoked, and no Read is added to it on a row that may turn Secure");
        Holds("sprk_communicationattachment", attachment, Caller).Should().BeFalse("this call's own grant is undone on the mismatch");
        outcome.Complete.Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // 198 fix round: the MESSAGE row's participant grant is confirmed by an owner read-back too (the K1 class on the message)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private int MessageGrants(Guid message) =>
        _shares.WriteLog.Count(w => w.Action == "GrantAccess" && w.EntitySet == "sprk_communications" && w.RecordId == message);

    [Fact(DisplayName = "198 fix round must-fire: the message re-owned to Secure between its owner pre-check and its grant keeps no participant share, and is retried")]
    public async Task Message_ReownedToSecureBetweenThePreCheckAndTheGrant_KeepsNoParticipantShare_AndIsRetried()
    {
        var (message, _) = DirectMessage();
        var flipped = false;
        _shares.BeforeStrictRead = (table, id) =>
        {
            // The message row's strict share read sits between its owner pre-check (OwnerUnchangedAsync) and its grant.
            if (table == "sprk_communication" && id == message && !flipped)
            {
                flipped = true;
                _world.MoveOwner("sprk_communication", message, DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
            }
        };

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        flipped.Should().BeTrue();
        MessageGrants(message).Should().BeGreaterThan(0, "the flip landed after the pre-check, so the grant was sent");
        Holds("sprk_communication", message, Caller).Should().BeFalse("the read-back finds the Secure team, so the grant is undone");
        Holds("sprk_communication", message, Other).Should().BeFalse();
        outcome.Complete.Should().BeFalse("a mismatch is retried, never success");
        outcome.Granted.Should().NotContain(g => g.RowId == message);
    }

    [Fact(DisplayName = "198 fix round must-fire: a message grant that throws after it was applied, with the owner Secure on read-back, is revoked")]
    public async Task Message_AGrantThatThrowsAfterItWasApplied_WithTheOwnerSecureOnReadBack_IsRevoked()
    {
        var (message, _) = DirectMessage();
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.User(Caller), Read); // Other is the only grant
        _shares.AfterGrant = (_, id, _) =>
        {
            if (id != message)
                return;
            _world.MoveOwner("sprk_communication", message, DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
            throw new TimeoutException("Test: the grant committed, then the call timed out.");
        };

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        Holds("sprk_communication", message, Other).Should().BeFalse("a grant that faulted may have committed; the owner is Secure, so it is undone");
        Holds("sprk_communication", message, Caller).Should().BeTrue("a share held before the call is never touched");
        outcome.Complete.Should().BeFalse();
    }

    [Fact(DisplayName = "198 fix round must-not-fire: a message whose owner is unchanged keeps the participants' grants, revokes nothing, and is complete")]
    public async Task Message_AnUnchangedOwner_KeepsTheParticipantGrants_AndIsComplete()
    {
        var (message, _) = DirectMessage();

        var outcome = await Reconciler198().AfterRefileAsync(message, CancellationToken.None);

        Holds("sprk_communication", message, Caller).Should().BeTrue();
        Holds("sprk_communication", message, Other).Should().BeTrue();
        outcome.Granted.Should().Contain(("sprk_communication", message, Caller)).And.Contain(("sprk_communication", message, Other));
        outcome.Complete.Should().BeTrue();
        Revokes().Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Pass 2, N2: a participant the job grants on the message also gets the message's gate Decision Record
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Guid GateWriter = Guid.Parse("c1980000-0000-4000-8000-0000000000f1");

    /// <summary>The REAL <see cref="GateDecisionRecordSharing"/> over this world and share table (the writer's identity is
    /// <see cref="GateWriter"/>), with the Direct lookup and the Secure settings, as the host composes it.</summary>
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

    /// <summary>A Direct message whose Sender (<see cref="Caller"/>) reads every row and whose Recipient (<see cref="Other"/>) reads none
    /// (a secure exit back to Direct whose Part C step did not re-grant them), and its gate record, owned by the Direct team and shared
    /// with the Sender only (the gate step computed the audience as the Sender).</summary>
    private (Guid Message, Guid Record, IReadOnlyList<(string Table, Guid Id)> Rows) SecureExitWhoseRecipientWasNotRegranted()
    {
        var (message, rows) = DirectMessage();
        _world.User(Caller, SecureChildShareWorld.GeneralBu).User(Other, SecureChildShareWorld.GeneralBu);
        foreach (var (table, id) in rows)
            _shares.Seed(table, id, DataversePrincipalRef.User(Caller), Read);
        var record = Guid.NewGuid();
        _world.Add("sprk_decisionrecord", record,
            ("sprk_regardingcommunication", new EntityReference("sprk_communication", message)),
            OwnedBy(DirectTeam),
            ("createdby", new EntityReference("systemuser", GateWriter)));
        _shares.Seed("sprk_decisionrecord", record, DataversePrincipalRef.User(Caller), Read);
        return (message, record, rows);
    }

    [Fact(DisplayName = "198 pass 2 N2 must-fire: the job grants the Recipient on the message, and the gate record then gets the Recipient's Read")]
    public async Task N2_Job_AParticipantItGrantsOnTheMessage_IsMirroredToTheGateRecord()
    {
        var (message, record, _) = SecureExitWhoseRecipientWasNotRegranted();

        var run = await Job(GateOverTheWorld()).ExecuteAsync(RunContext(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Holds("sprk_communication", message, Other).Should().BeTrue("the job re-granted the Recipient on the message");
        Holds("sprk_decisionrecord", record, Other).Should().BeTrue("D-215: the message's gate record follows its participants");
        Holds("sprk_decisionrecord", record, Caller).Should().BeTrue();
    }

    [Fact(DisplayName = "198 pass 2 N2 must-not-fire: a pass that grants nothing does not run the gate mirror")]
    public async Task N2_Job_APassThatGrantsNothing_DoesNotRunTheMirror()
    {
        var (_, record, rows) = SecureExitWhoseRecipientWasNotRegranted();
        foreach (var (table, id) in rows)
            _shares.Seed(table, id, DataversePrincipalRef.User(Other), Read);

        await Job(GateOverTheWorld()).ExecuteAsync(RunContext(), CancellationToken.None);

        Grants().Should().Be(0, "steady state: nothing is granted, and the mirror is not run");
        Holds("sprk_decisionrecord", record, Other).Should().BeFalse("the mirror runs only after a pass that granted");
    }
}
