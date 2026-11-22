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
/// #2745: <see cref="Job"/>'s Direct-message pass lists each table's recently changed rows in (<c>modifiedon</c>, id) order, at most
/// <see cref="Job.MaxDirectListedPerTable"/> per run. When a capped listing's rows all shared the watermark's instant, the watermark
/// stepped one tick past it, so the rows past the cap at that instant (more than 500 written in one second: <c>modifiedon</c> is
/// stored to the second) were never listed; only the rolling sweep could reach them. The fix keys the watermark by the last listed
/// row's id per table when a capped listing stops at the window's own start (an instant at least one run old), and the next listing
/// goes on past that key; a watermark that moves to a later instant carries no key, so a row stamped with it but committed after
/// the read (possibly with a lower id) is still listed.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job and Direct-team lookup; the sweep answers
/// nothing, so only the listing and the carry can reach a message. Each arrange is more than one listing's cap of rows at one
/// instant: an inherent multi-record setup at a KEEP integration path.</para>
/// </summary>
public class Issue2745_DirectListingSameInstantKeySetTests
{
    private const string Communication = "sprk_communication";
    private const string Attachment = "sprk_communicationattachment";
    private const int Read = 1;

    /// <summary>Past one listing's cap: the rows beyond it share the cap's last instant.</summary>
    private const int AtOneInstant = Job.MaxDirectListedPerTable + 200;

    private static readonly Guid DirectTeam = Guid.Parse("d2745000-0000-4000-8000-0000000000d1");
    private static readonly Guid Sender = Guid.Parse("c2745000-0000-4000-8000-0000000000a1");
    private static readonly Guid Recipient = Guid.Parse("c2745000-0000-4000-8000-0000000000a2");
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Sender, Recipient);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(T0);
    private Guid _thread;

    public Issue2745_DirectListingSameInstantKeySetTests()
    {
        var directUnit = Guid.Parse("d2745000-0000-4000-8000-0000000000b1");
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

    [Fact]
    public async Task ExecuteAsync_MoreMessagesThanTheCapChangedAtOneInstant_ListsAndGrantsEveryOne()
    {
        // A bulk write inside the window: 700 messages share one modifiedon.
        var instant = T0 - TimeSpan.FromMinutes(30);
        var messages = Enumerable.Range(0, AtOneInstant).Select(_ => Message(instant)).ToList();
        var job = NewJob();

        var first = await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        var third = await RunAsync(job);
        var runs = 3 + await RunUntilAsync(job, () => messages.All(Granted), maxRuns: 5);

        messages.Should().OnlyContain(m => Granted(m), $"every message changed at the instant is listed, without the sweep (runs: {runs})");
        Bool(first, "listingCapped").Should().BeTrue();
        Since(second).Should().Be(instant.UtcDateTime, "the watermark moves to the capped listing's instant");
        Int(second, "listed").Should().Be(Job.MaxDirectListedPerTable,
            "a watermark that moved to the instant carries no key: the whole instant is listed again, up to the cap");
        Since(third).Should().Be(instant.UtcDateTime, "the listing capped at the window's own start: the watermark stays, keyed");
        Int(third, "listed").Should().Be(AtOneInstant - Job.MaxDirectListedPerTable,
            "the third run lists only the rows past the key at that instant");
    }

    [Fact]
    public async Task ExecuteAsync_ARowStampedAtACappedListingsLastInstantWithALowerIdAfterTheRead_IsStillListedAndGranted()
    {
        // A capped listing that ends at an instant after the window's start (the current second, in a bulk write): a row stamped
        // with that instant but committed after the read can carry a lower id. Keying the watermark there would list only past the
        // last row's id, and the late row would be left to the sweep (answering nothing here).
        var instant = T0 - TimeSpan.FromMinutes(30);
        var messages = Enumerable.Range(0, Job.MaxDirectListedPerTable).Select(_ => Message(instant)).ToList();
        var job = NewJob();

        var first = await RunAsync(job);
        Bool(first, "listingCapped").Should().BeTrue();
        var late = Message(instant, Guid.Parse("00000000-0000-0000-0000-000000000001")); // lower than every listed row's id

        var runs = 1 + await RunUntilAsync(job, () => Granted(late) && messages.All(Granted), maxRuns: 6);

        Granted(late).Should().BeTrue($"the late row at the capped listing's last instant is listed and granted (runs: {runs})");
        messages.Should().OnlyContain(m => Granted(m));
    }

    [Fact]
    public async Task ExecuteAsync_MoreContentRowsThanTheCapChangedAtTheWindowsStart_ListsAndGrantsEveryMessage()
    {
        // The tie with the first run's own window start, through a content table: the key is the content ROW's id, not the
        // message's (the listing is ordered by the row's id).
        var since = T0 - Job.DirectInitialLookback;
        var messages = Enumerable.Range(0, AtOneInstant).Select(_ => MessageThroughAttachment(since)).ToList();
        var job = NewJob();

        var first = await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await RunAsync(job);
        var runs = 2 + await RunUntilAsync(job, () => messages.All(Granted), maxRuns: 6);

        messages.Should().OnlyContain(m => Granted(m), $"every message whose content row shares the window's start is listed (runs: {runs})");
        Bool(first, "listingCapped").Should().BeTrue();
        Since(second).Should().Be(since.UtcDateTime, "the capped listing did not move past the window's start: the watermark stays");
        Int(second, "listed").Should().Be(AtOneInstant - Job.MaxDirectListedPerTable);
    }

    [Fact]
    public async Task ExecuteAsync_TwoTablesCappedAtDifferentInstants_ListsAndGrantsEveryMessageOfBoth()
    {
        // The messages' own rows cap at an earlier instant than the attachments: the watermark moves to the earlier one first, then
        // is keyed there for the messages' table only; the attachments, listed past that instant, cap at theirs and are keyed there
        // next.
        var earlier = T0 - TimeSpan.FromMinutes(40);
        var later = T0 - TimeSpan.FromMinutes(20);
        var own = Enumerable.Range(0, AtOneInstant).Select(_ => Message(earlier)).ToList();
        var throughAttachments = Enumerable.Range(0, AtOneInstant).Select(_ => MessageThroughAttachment(later)).ToList();
        var job = NewJob();

        var results = new List<JobRunResult> { await RunAsync(job) };
        while (!(own.All(Granted) && throughAttachments.All(Granted)) && results.Count < 14)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            results.Add(await RunAsync(job));
        }

        own.Should().OnlyContain(m => Granted(m), $"every message changed at the earlier instant is listed (runs: {results.Count})");
        throughAttachments.Should().OnlyContain(m => Granted(m), "every message whose attachment changed at the later instant is listed");
        var sinces = results.Select(Since).ToList();
        sinces.Should().BeInAscendingOrder("the watermark never moves back");
        sinces.Should().Contain(earlier.UtcDateTime).And.Contain(later.UtcDateTime, "each capped instant is the watermark in turn");
        Int(results[1], "listed").Should().Be(2 * Job.MaxDirectListedPerTable,
            "unkeyed at the earlier instant, each table is listed again from it, up to its cap");
        Int(results[2], "listed").Should().Be((AtOneInstant - Job.MaxDirectListedPerTable) + Job.MaxDirectListedPerTable,
            "the 200 messages past the key at the earlier instant, and the attachments' 500 listed again up to their cap");
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
        job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-2745", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
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

    /// <summary>A Direct-team message on the thread, from <see cref="Sender"/> to <see cref="Recipient"/>, changed at
    /// <paramref name="changedAt"/> (never, for null: only a content row can list it).</summary>
    private Guid Message(DateTimeOffset? changedAt, Guid? id = null)
    {
        var message = id ?? Guid.NewGuid();
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

    /// <summary>A message listed only through one attachment row, changed at <paramref name="changedAt"/>.</summary>
    private Guid MessageThroughAttachment(DateTimeOffset changedAt)
    {
        var message = Message(changedAt: null);
        _world.Add(Attachment, Guid.NewGuid(),
            ("owningteam", new EntityReference("team", DirectTeam)),
            ("sprk_communication", new EntityReference(Communication, message)),
            ("modifiedon", changedAt.UtcDateTime));
        return message;
    }

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

    private static bool Bool(JobRunResult run, string field) => Direct(run).GetProperty(field).GetBoolean();

    private static DateTime Since(JobRunResult run) => Direct(run).GetProperty("since").GetDateTime();
}
