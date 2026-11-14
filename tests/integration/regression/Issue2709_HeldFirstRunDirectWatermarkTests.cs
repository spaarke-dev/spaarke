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
/// #2709: <see cref="Job"/>'s Direct-message pass computed its first window as <c>startedAt - DirectInitialLookback</c> and
/// never stored it. A first run that HELD the watermark (the listing failed, or the Direct team could not be resolved) left it
/// unset, so the next run looked back an hour from its OWN start: the window slid forward, and a message changed between the
/// two starts was never listed (only the rolling sweep reached it, hours or days later on a busy tenant). The fix stores the
/// first window's start before anything can fail.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job and Direct-team lookup. The sweep
/// answers nothing here, so a grant can only come from the recent-changes listing. The arrange is a Direct thread, its message
/// and the Direct team's business unit: an inherent multi-record setup at a KEEP integration path.</para>
/// </summary>
public class Issue2709_HeldFirstRunDirectWatermarkTests
{
    private const int Read = 1;
    private const string Communication = "sprk_communication";

    private static readonly Guid DirectTeam = Guid.Parse("d2709000-0000-4000-8000-0000000000d1");
    private static readonly Guid Sender = Guid.Parse("c2709000-0000-4000-8000-0000000000a1");
    private static readonly Guid Recipient = Guid.Parse("c2709000-0000-4000-8000-0000000000a2");
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new UserFlagDirectoryDouble().Internal(Sender, Recipient);
    private readonly DirectMessageOwnerTeamDouble _directory = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
    private readonly FakeTimeProvider _time = new(T0);
    private bool _listingFaults;

    public Issue2709_HeldFirstRunDirectWatermarkTests()
    {
        var directUnit = Guid.Parse("d2709000-0000-4000-8000-0000000000b1");
        _world.Add("businessunit", directUnit, ("name", DirectMessageOwnerTeamDouble.BusinessUnitName),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(DirectTeam, directUnit, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false, teamType: 0);
    }

    public enum HoldCause
    {
        /// <summary>The recent-changes listing faults.</summary>
        ListingFaults,

        /// <summary>The Direct-message owner team cannot be resolved.</summary>
        DirectTeamUnresolved,
    }

    [Theory]
    [InlineData(HoldCause.ListingFaults)]
    [InlineData(HoldCause.DirectTeamUnresolved)]
    public async Task ExecuteAsync_AfterAFirstRunThatHeldTheWatermark_ListsFromTheFirstRunsWindowStart_AndGrantsAMessageChangedBeforeTheSecondRunsLookBack(
        HoldCause cause)
    {
        var gap = TimeSpan.FromMinutes(10);
        // Inside the first run's window, but before the second run's own look-back (T0 + gap - 60 min).
        var message = DirectMessageChangedAt(T0 - Job.DirectInitialLookback + gap / 2);
        var job = NewJob();

        _listingFaults = cause == HoldCause.ListingFaults;
        _directory.Faults = cause == HoldCause.DirectTeamUnresolved;
        var first = await RunAsync(job);
        first.Success.Should().BeFalse("the first run held the watermark");
        Granted(message).Should().BeFalse("nothing listed the message in the first run");

        _listingFaults = false;
        _directory.Faults = false;
        _time.Advance(gap);
        var second = await RunAsync(job);

        second.Success.Should().BeTrue(second.ErrorMessage);
        Since(second).Should().Be((T0 - Job.DirectInitialLookback).UtcDateTime,
            "the window the first run could not list is listed from its start, not from the second run's own look-back");
        Granted(message).Should().BeTrue("the listing reaches a message changed inside the first run's window");
    }

    [Fact]
    public async Task ExecuteAsync_AfterAFirstRunThatCompleted_ListsFromThatRunsStartLessTheOverlap()
    {
        var job = NewJob();
        await RunAsync(job);
        _time.Advance(TimeSpan.FromMinutes(10));

        var second = await RunAsync(job);

        Since(second).Should().Be((T0 - Job.DirectOverlap).UtcDateTime, "a completed run still moves the watermark forward");
    }

    // ── The job, as the host composes it ──

    private Job NewJob()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (IsListing(query) && _listingFaults)
                    throw new InvalidOperationException("Test: the recent changes could not be listed.");
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
        job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-2709", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);

    // ── The world ──

    /// <summary>A Direct-team message on a Direct thread, from <see cref="Sender"/> to <see cref="Recipient"/>, changed at
    /// <paramref name="modifiedOn"/>.</summary>
    private Guid DirectMessageChangedAt(DateTimeOffset modifiedOn)
    {
        var thread = Guid.NewGuid();
        _world.Add("sprk_communicationthread", thread,
            ("ownerid", new EntityReference("systemuser", Sender)),
            ("sprk_threadtype", new OptionSetValue(100000001)),
            ("sprk_isdefaultthread", false));
        _shares.Seed("sprk_communicationthread", thread, DataversePrincipalRef.User(Recipient), Read);

        var message = Guid.NewGuid();
        _world.Add(Communication, message,
            ("owningteam", new EntityReference("team", DirectTeam)),
            ("sprk_communicationthread", new EntityReference("sprk_communicationthread", thread)),
            ("modifiedon", modifiedOn.UtcDateTime));
        _world.Set(Communication, message, "sprk_sentby", new EntityReference("systemuser", Sender));
        _world.Set(Communication, message, "sprk_directrecipient", new EntityReference("systemuser", Recipient));
        return message;
    }

    private bool Holds(Guid message, Guid user) =>
        (_shares.MaskOf(Communication, message, DataversePrincipalRef.User(user)) ?? 0) != 0;

    private bool Granted(Guid message) => Holds(message, Sender) && Holds(message, Recipient);

    private static bool OwnedByDirectTeam(QueryExpression query) => query.Criteria.Conditions.Any(c =>
        c.AttributeName == "owningteam" && c.Operator == ConditionOperator.Equal && c.Values.Single() is Guid team && team == DirectTeam);

    private static bool IsListing(QueryExpression query) =>
        OwnedByDirectTeam(query) && query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon");

    private static bool IsSweep(QueryExpression query) =>
        query.EntityName == Communication && OwnedByDirectTeam(query)
        && !query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon")
        && !query.Criteria.Conditions.Any(c => c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal);

    private static DateTime Since(JobRunResult run) =>
        JsonDocument.Parse(run.ResultJson!).RootElement.GetProperty("directMessages").GetProperty("since").GetDateTime();
}
