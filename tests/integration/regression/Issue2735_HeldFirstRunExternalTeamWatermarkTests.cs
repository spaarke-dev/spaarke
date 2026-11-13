using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildShareReconciliationJob;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2735 (the #2709 pattern): <see cref="Job"/>'s External-team pass computed its first window as
/// <c>startedAt - ExternalInitialLookback</c> and never stored it. A first run that HELD the watermark (the recent-changes
/// listing threw, or the recent sync failed) left it unset, so the next run looked back an hour from its OWN start: the
/// window slid forward, and a row changed between the two starts was never listed (only the hourly sweep reached it). The fix
/// stores the first window's start before anything can fail.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job and the REAL
/// <see cref="ExternalTeamShareSynchronizer"/>; the visibility predicate is the one substituted seam, as in
/// <c>ExternalTeamShareTests</c>. The listing's <c>modifiedon</c> bound is read from the query the engine sends. The arrange is
/// the External Collaborators unit and team, a matter and its event: an inherent multi-record setup at a KEEP integration
/// path.</para>
/// </summary>
public class Issue2735_HeldFirstRunExternalTeamWatermarkTests
{
    private static readonly Guid ExternalBu = Guid.Parse("e2735000-0000-4000-8000-000000000001");
    private static readonly Guid ExternalTeam = Guid.Parse("e2735000-0000-4000-8000-000000000002");
    private static readonly Guid DirectBu = Guid.Parse("e2735000-0000-4000-8000-000000000003");
    private static readonly Guid DirectTeam = Guid.Parse("e2735000-0000-4000-8000-000000000004");
    private static readonly Guid Matter = Guid.Parse("e2735000-0000-4000-8000-0000000000a1");
    private static readonly Guid Event = Guid.Parse("e2735000-0000-4000-8000-0000000000b1");
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly FakeTimeProvider _time = new(T0);
    private readonly List<DateTime> _listedSince = new();
    private bool _listingFaults;
    private bool _prerequisiteFaults;

    public Issue2735_HeldFirstRunExternalTeamWatermarkTests()
    {
        _world.Add("businessunit", ExternalBu, ("name", "External Collaborators"),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)));
        _world.Team(ExternalTeam, ExternalBu, "External Collaborators", isDefault: true, teamType: 0);
        // An event changed inside the first run's window: the first run's recent sync has a row to decide.
        _world.OrdinaryRoot("sprk_matter", Matter)
            .OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", Matter))
            .Modified("sprk_event", Event, (T0 - TimeSpan.FromMinutes(30)).UtcDateTime);
    }

    public enum HoldCause
    {
        /// <summary>The recent-changes listing throws.</summary>
        ListingFaults,

        /// <summary>The recent sync fails: a prerequisite team (here the Direct team) cannot be read, so nothing is decided.</summary>
        PrerequisiteReadFaults,
    }

    [Theory]
    [InlineData(HoldCause.ListingFaults)]
    [InlineData(HoldCause.PrerequisiteReadFaults)]
    public async Task ExecuteAsync_AfterAFirstRunThatHeldTheExternalWatermark_ListsFromTheFirstRunsWindowStart(HoldCause cause)
    {
        var job = NewJob();
        _listingFaults = cause == HoldCause.ListingFaults;
        _prerequisiteFaults = cause == HoldCause.PrerequisiteReadFaults;

        var first = await RunAsync(job);
        Status(first).Should().BeOneOf(["failed", "listing-failed"], "the first run held the watermark");

        _listingFaults = false;
        _prerequisiteFaults = false;
        _listedSince.Clear();
        _time.Advance(Gap);
        await RunAsync(job);

        _listedSince.Should().NotBeEmpty().And.OnlyContain(since => since == (T0 - Job.ExternalInitialLookback).UtcDateTime,
            "the window the first run could not list is listed from its start, not from the second run's own look-back");
    }

    [Fact]
    public async Task ExecuteAsync_AfterAFirstRunThatCompleted_ListsFromThatRunsStartLessTheOverlap()
    {
        var job = NewJob();
        var first = await RunAsync(job);
        Status(first).Should().NotBe("failed").And.NotBe("listing-failed");

        _listedSince.Clear();
        _time.Advance(Gap);
        await RunAsync(job);

        _listedSince.Should().NotBeEmpty().And.OnlyContain(since => since == (T0 - Job.ExternalOverlap).UtcDateTime,
            "a completed run still moves the watermark forward");
    }

    // ── The job, as the host composes it ──

    private Job NewJob()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureChildShareWorld.SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureChildShareWorld.SecureOwnerTeamName,
            [ExternalTeamShareSynchronizer.WritesEnabledConfigKey] = "true",
        }).Build();

        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (ListedSince(query) is { } since)
                {
                    if (_listingFaults)
                        throw new InvalidOperationException("Test: the recent changes could not be listed.");
                    _listedSince.Add(since);
                }

                return Task.FromResult(_world.Answer(query));
            });

        var engine = new ExternalTeamShareSynchronizer(
            entities.Object, _shares, Decide, configuration, NullLogger<ExternalTeamShareSynchronizer>.Instance,
            ownership: new RecordOwnershipResolver(entities.Object, configuration, NullLogger<RecordOwnershipResolver>.Instance))
        {
            DirectTeamIdentity = _ => _prerequisiteFaults
                ? throw new InvalidOperationException("Test: the Direct-message owner team could not be read.")
                : Task.FromResult(new DirectMessageOwnerTeamIdentity(DirectBu, DirectTeam, null, null)),
        };
        var services = new ServiceCollection()
            .AddSingleton(new SecureChildShareSynchronizer(entities.Object, _shares, SecureChildShareWorld.NobodyWalled(),
                configuration, NullLogger<SecureChildShareSynchronizer>.Instance, engine))
            .AddSingleton(engine);

        return new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _time,
            NullLogger<Job>.Instance);
    }

    private Task<JobRunResult> RunAsync(Job job) =>
        job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-2735", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);

    /// <summary>The predicate seam: every row Visible, filed under <see cref="Matter"/> (a root under itself).</summary>
    private static Task<ExternalVisibilityResult> Decide(IReadOnlyCollection<(string Table, Guid Id)> rows, CancellationToken ct)
    {
        var decided = new Dictionary<ExternalVisibility.Node, ExternalVisibilityDecision>();
        var reached = new Dictionary<ExternalVisibility.Node, IReadOnlyCollection<ExternalVisibility.Node>>();
        foreach (var (table, id) in rows)
        {
            var node = new ExternalVisibility.Node(table, id);
            decided[node] = new ExternalVisibilityDecision(ExternalVisibilityVerdict.Visible, ExternalVisibilityReason.Visible, table, id);
            reached[node] = [SecureChildLineage.IsRoot(table) ? node : new ExternalVisibility.Node("sprk_matter", Matter)];
        }

        return Task.FromResult(new ExternalVisibilityResult(decided, faulted: false)
        {
            RootsReached = reached,
            EffectivelySecureRoots = new HashSet<ExternalVisibility.Node>(),
        });
    }

    /// <summary>The <c>modifiedon</c> bound of the recent-changes listing of the event table, or null for any other query.</summary>
    private static DateTime? ListedSince(QueryExpression query) =>
        query.EntityName == "sprk_event"
        && query.Criteria.Conditions.FirstOrDefault(c =>
            c.AttributeName == "modifiedon" && c.Operator == ConditionOperator.GreaterEqual) is { } bound
            ? (DateTime)bound.Values.Single()
            : null;

    private static string Status(JobRunResult run) =>
        JsonDocument.Parse(run.ResultJson!).RootElement.GetProperty("externalTeam").GetProperty("status").GetString()!;
}
