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
/// #2731: <see cref="Job"/>'s Direct-message pass HELD its watermark whenever the carry passed <see cref="Job.MaxDirectCarried"/>.
/// The held window was listed again, so the messages already finished came back in, the carry overflowed again, and every run
/// re-reconciled the same messages while a change made after the burst sat at the carry's tail and was dropped run after run
/// (only the rolling sweep could reach it). The fix works recent changes oldest first and moves the watermark to the earliest
/// change it could not carry.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job and Direct-team lookup; the sweep answers
/// nothing, so only the listing and the carry can reach a message. The arrange is a burst of
/// <see cref="Job.MaxDirectCarried"/> + one run's budget + 1 messages across three listed tables (each under its listing cap):
/// an inherent multi-record setup at a KEEP integration path.</para>
/// </summary>
public class Issue2731_DirectCarryOverflowWatermarkTests
{
    private const int Read = 1;
    private const string Communication = "sprk_communication";
    private const string Attachment = "sprk_communicationattachment";
    private const string Participant = "sprk_communicationparticipant";

    /// <summary>The recent and carried messages one run works: everything but the sweep's reserve.</summary>
    private const int PriorityBudget = Job.MaxDirectMessagesPerRun - Job.DirectSweepReserve;

    private static readonly Guid DirectTeam = Guid.Parse("d2731000-0000-4000-8000-0000000000d1");
    private static readonly Guid Sender = Guid.Parse("c2731000-0000-4000-8000-0000000000a1");
    private static readonly Guid Recipient = Guid.Parse("c2731000-0000-4000-8000-0000000000a2");
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Sender, Recipient);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(T0);
    private Guid _thread;

    public Issue2731_DirectCarryOverflowWatermarkTests()
    {
        var directUnit = Guid.Parse("d2731000-0000-4000-8000-0000000000b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
    }

    [Fact]
    public async Task ExecuteAsync_AfterACarryPastItsCap_ListsOnlyWhatItDropped_AndReachesAChangeMadeAfterTheBurst()
    {
        // A burst one past what a run works plus what it may carry, each message changed at its own instant, through its own row
        // (the first listing's worth), an attachment row (the next) or a participant row (the rest) — no listing reaches its cap.
        var burst = PriorityBudget + Job.MaxDirectCarried + 1;
        var perListing = Job.MaxDirectListedPerTable - 1;
        var burstStart = T0 - TimeSpan.FromMinutes(30);
        DirectThread();
        for (var i = 0; i < burst; i++)
        {
            var at = burstStart + TimeSpan.FromSeconds(i);
            if (i < perListing)
                Message(at);
            else
                ContentRow(i < 2 * perListing ? Attachment : Participant, Message(changedAt: null), at);
        }

        // Changed after the whole burst, through a participant row: the last change in the window.
        var late = Message(changedAt: null);
        ContentRow(Participant, late, T0 - TimeSpan.FromMinutes(5));
        var job = NewJob();

        var first = await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);

        Int(first, "carried").Should().Be(Job.MaxDirectCarried, "the carry is full");
        Since(second).Should().BeAfter(Since(first), "the watermark moves past what the first run finished or carried");
        Int(second, "listed").Should().Be(burst + 1 - PriorityBudget - Job.MaxDirectCarried,
            "only the messages the first run could not carry are listed again, not the ones it finished");

        var runs = 2;
        while (!Granted(late) && runs < 10)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            await RunAsync(job);
            runs++;
        }

        Granted(late).Should().BeTrue("a change made after the burst is worked once the carry drains, without the sweep");
    }

    // ── The job, as the host composes it ──

    private Job NewJob()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
                Task.FromResult(IsSweep(query) ? new EntityCollection() : _world.Answer(query)));

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

    private void ContentRow(string table, Guid message, DateTimeOffset changedAt) =>
        _world.Add(table, Guid.NewGuid(),
            ("owningteam", new EntityReference("team", DirectTeam)),
            ("sprk_communication", new EntityReference(Communication, message)),
            ("modifiedon", changedAt.UtcDateTime));

    private bool Holds(Guid message, Guid user) =>
        (_shares.MaskOf(Communication, message, DataversePrincipalRef.User(user)) ?? 0) != 0;

    private bool Granted(Guid message) => Holds(message, Sender) && Holds(message, Recipient);

    private static bool OwnedByDirectTeam(QueryExpression query) => query.Criteria.Conditions.Any(c =>
        c.AttributeName == "owningteam" && c.Operator == ConditionOperator.Equal && c.Values.Single() is Guid team && team == DirectTeam);

    private static bool IsSweep(QueryExpression query) =>
        query.EntityName == Communication && OwnedByDirectTeam(query)
        && !query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon")
        && !query.Criteria.Conditions.Any(c => c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal);

    private static JsonElement Direct(JobRunResult run) =>
        JsonDocument.Parse(run.ResultJson!).RootElement.GetProperty("directMessages");

    private static int Int(JobRunResult run, string field) => Direct(run).GetProperty(field).GetInt32();

    private static DateTime Since(JobRunResult run) => Direct(run).GetProperty("since").GetDateTime();
}
