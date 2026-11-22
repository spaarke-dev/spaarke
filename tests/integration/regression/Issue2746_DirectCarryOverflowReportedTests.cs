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
/// #2746: a run of <see cref="Job"/> whose Direct-message carry passed <see cref="Job.MaxDirectCarried"/> dropped messages (the
/// watermark held; the ones it had not listed were left to the sweep) and still reported <c>Success = true</c>, with only a warning
/// in the log, so the run history and the admin job status showed green. The fix makes such a run not a success (the scheduler
/// retries only a run that throws, so this alerts without an immediate retry) and reports the dropped counts in its error message
/// and result JSON.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job and Direct-team lookup. Each arrange is
/// more than one run's budget plus the carry cap of messages, spread over three listed tables so no listing caps: an inherent
/// multi-record setup at a KEEP integration path.</para>
/// </summary>
public class Issue2746_DirectCarryOverflowReportedTests
{
    private const int Read = 1;
    private const string Communication = "sprk_communication";
    private const string Attachment = "sprk_communicationattachment";
    private const string Participant = "sprk_communicationparticipant";

    /// <summary>The recent and carried messages one run works: everything but the sweep's reserve.</summary>
    private const int PriorityBudget = Job.MaxDirectMessagesPerRun - Job.DirectSweepReserve;

    /// <summary>Under one listing's cap, so a burst spread over three tables caps none of them.</summary>
    private const int PerListing = Job.MaxDirectListedPerTable - 1;

    private static readonly Guid DirectTeam = Guid.Parse("d2746000-0000-4000-8000-0000000000d1");
    private static readonly Guid Sender = Guid.Parse("c2746000-0000-4000-8000-0000000000a1");
    private static readonly Guid Recipient = Guid.Parse("c2746000-0000-4000-8000-0000000000a2");
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Sender, Recipient);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly HashSet<Guid> _unreadable = new();
    private readonly List<Guid> _sweepable = new();
    private Guid _thread;

    public Issue2746_DirectCarryOverflowReportedTests()
    {
        var directUnit = Guid.Parse("d2746000-0000-4000-8000-0000000000b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
        _thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", _thread,
            ("ownerid", new EntityReference("systemuser", Sender)),
            ("sprk_threadtype", new OptionSetValue(100000001)),
            ("sprk_isdefaultthread", false));
        _shares.Seed("sprk_communicationthread", _thread, DataversePrincipalRef.User(Recipient), Read);
    }

    [Theory]
    [InlineData(Job.MaxDirectCarried, false)]
    [InlineData(Job.MaxDirectCarried + 1, true)]
    public async Task ExecuteAsync_ACarryPastItsCapWithEveryMessageItWorkedFinished_IsNotASuccess_AndReportsTheDropped(
        int toCarry, bool overflows)
    {
        // Every message is readable and no listing caps: the overflow is the only thing undecided.
        SpreadBurst(PriorityBudget + toCarry);

        var run = await RunAsync(NewJob());

        if (overflows)
        {
            run.Success.Should().BeFalse("a run that drops messages from its carry is degraded, and the run history must show it");
            run.ErrorMessage.Should().Contain("; Direct messages: ok, 0 not complete, 0 backed off, 1 dropped from a full carry " +
                "(0 not listed this run)");
        }
        else
        {
            run.Success.Should().BeTrue("exactly the cap is carried without dropping any message");
            run.ErrorMessage.Should().BeNull();
        }

        Int(run, "incomplete").Should().Be(0);
        Int(run, "backedOff").Should().Be(0);
        Bool(run, "listingCapped").Should().BeFalse();
        Int(run, "carried").Should().Be(Job.MaxDirectCarried);
        Int(run, "carryDropped").Should().Be(overflows ? 1 : 0);
        Int(run, "carryDroppedUnlisted").Should().Be(0, "the dropped message was listed this run: the held window lists it again");
    }

    [Fact]
    public async Task ExecuteAsync_ACarryPastItsCapThatDropsMessagesItDidNotList_ReportsThemAsNotListed()
    {
        // First run: exactly the budget plus the cap, all finished or carried; the listing is complete, so the watermark moves past
        // every one of them. From then on only the carry holds them.
        var burst = SpreadBurst(PriorityBudget + Job.MaxDirectCarried);
        var job = NewJob();
        var first = await RunAsync(job);
        Int(first, "carryDropped").Should().Be(0);
        first.Success.Should().BeTrue();

        // Second run: the carried messages it works stay incomplete, and the sweep brings in a slice of messages that stay
        // incomplete too. The incomplete come first in the carry, so its tail of carried-in (unlisted) messages is dropped.
        _unreadable.UnionWith(burst);
        var sweepSlice = Job.MaxDirectMessagesPerRun - PriorityBudget;
        for (var i = 0; i < sweepSlice; i++)
        {
            var swept = Message(changedAt: null);
            _unreadable.Add(swept);
            _sweepable.Add(swept);
        }

        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Int(second, "listed").Should().Be(0, "nothing changed since the watermark");
        Int(second, "incomplete").Should().Be(PriorityBudget + sweepSlice);
        Int(second, "carryDropped").Should().Be(sweepSlice);
        Int(second, "carryDroppedUnlisted").Should().Be(sweepSlice, "the dropped tail is carried-in messages this run did not list");
        second.Success.Should().BeFalse();
        second.ErrorMessage.Should().Contain($", {sweepSlice} dropped from a full carry ({sweepSlice} not listed this run)");
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
                if (IsSweep(query))
                {
                    // The sweep answers only the messages the test names, in id order, up to the slice asked for.
                    return Task.FromResult(new EntityCollection(_sweepable.OrderBy(id => id).Take(query.TopCount ?? int.MaxValue)
                        .Select(id => new Entity(Communication, id)).ToList()));
                }

                return Task.FromResult(_world.Answer(query));
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
        job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-2746", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);

    // ── The world ──

    /// <summary><paramref name="count"/> messages, the i-th changed at its own instant through its own row (the first listing's
    /// worth), an attachment row (the next) or a participant row (the rest) — no listing reaches its cap.</summary>
    private List<Guid> SpreadBurst(int count)
    {
        var burst = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var changedAt = T0 - TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(i);
            burst.Add(i < PerListing
                ? Message(changedAt)
                : ContentRowFor(i < 2 * PerListing ? Attachment : Participant, changedAt));
        }

        return burst;
    }

    /// <summary>A Direct-team message on the thread, from <see cref="Sender"/> to <see cref="Recipient"/>, changed at
    /// <paramref name="changedAt"/> (never, for null: only a content row or the sweep can reach it).</summary>
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
        _world.Add(table, Guid.NewGuid(),
            ("owningteam", new EntityReference("team", DirectTeam)),
            ("sprk_communication", new EntityReference(Communication, message)),
            ("modifiedon", changedAt.UtcDateTime));
        return message;
    }

    /// <summary>The message a read of one message row by its id names.</summary>
    private static Guid? ByIdRead(QueryExpression query) =>
        query.EntityName == Communication
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
}
