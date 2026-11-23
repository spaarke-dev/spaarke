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
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildReconciliationJob;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2735 (the #2709 pattern): four passes of <see cref="Job"/> — recent changes, ordinary parent moves, the inherited Access
/// Permission and the re-file pass — each computed its first window as <c>startedAt - InitialRecentChangesLookback</c> and
/// never stored it. A first run that HELD the watermark left it unset, so the next run looked back from its OWN start: the
/// window slid forward and a row changed between the two starts was never listed. The fix stores the first window's start
/// before anything can fail (and, for every pass but the ordinary one, in a report-only run too: owner option A).
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>) with the REAL job, reconciler, synchronizer,
/// relocator and resolver (the re-file harness's composition, <see cref="R.JobHarness"/>), on a clock the test moves. The
/// arrange is a secure work assignment, its to-do and (for the re-file window) two documents in place: an inherent
/// multi-record setup at a KEEP integration path.</para>
/// </summary>
public class Issue2735_HeldFirstRunSecureChildWatermarkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    private readonly FakeTimeProvider _time = new(T0);
    private readonly SecureChildShareWorld _world = R.JobWorld();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _todo = Guid.NewGuid();
    private bool _ordinaryListingFaults;

    public Issue2735_HeldFirstRunSecureChildWatermarkTests()
    {
        // A to-do written outside the product under a secure record, changed inside the first run's window but before the
        // second run's own look-back (T0 + Gap - 60 min): only a window that starts where the first one did lists it.
        _world.SecureRoot("sprk_workassignment", _root)
            .UserOwnedChild("sprk_todo", _todo, ("sprk_regardingworkassignment", "sprk_workassignment", _root))
            .Modified("sprk_todo", _todo, (T0 - Job.InitialRecentChangesLookback + Gap / 2).UtcDateTime);
    }

    public enum Pass
    {
        RecentChanges,
        OrdinaryMoves,
        AccessPermission,
        Refile,
    }

    public enum HoldCause
    {
        /// <summary>The pass's listing faults.</summary>
        ListingFaults,

        /// <summary>The recent-changes emergency stop: every pass but the ordinary one runs report-only (owner option A).</summary>
        ReportOnly,

        /// <summary>The re-file window does not drain within the examination budget.</summary>
        WindowNotDrained,
    }

    [Theory]
    [InlineData(Pass.RecentChanges, HoldCause.ListingFaults)]
    [InlineData(Pass.RecentChanges, HoldCause.ReportOnly)]
    [InlineData(Pass.OrdinaryMoves, HoldCause.ListingFaults)]
    [InlineData(Pass.AccessPermission, HoldCause.ListingFaults)]
    [InlineData(Pass.AccessPermission, HoldCause.ReportOnly)]
    [InlineData(Pass.Refile, HoldCause.ListingFaults)]
    [InlineData(Pass.Refile, HoldCause.WindowNotDrained)]
    [InlineData(Pass.Refile, HoldCause.ReportOnly)]
    public async Task ExecuteAsync_AfterAFirstRunThatHeldThePassesWatermark_ListsFromTheFirstRunsWindowStart(Pass pass, HoldCause cause)
    {
        var harness = new Harness(this);
        var budget = (string?)null;
        switch (cause)
        {
            case HoldCause.ListingFaults when pass == Pass.RecentChanges:
                _world.FailingQueriesOf("sprk_agreement"); // a lineage child table only the recent-changes pass lists
                break;
            case HoldCause.ListingFaults when pass == Pass.OrdinaryMoves:
                _ordinaryListingFaults = true;
                break;
            case HoldCause.ListingFaults when pass == Pass.AccessPermission:
                _world.FailingQueriesOf("sprk_event");
                break;
            case HoldCause.ListingFaults when pass == Pass.Refile:
                _world.FailingQueriesOf("sprk_document");
                break;
            case HoldCause.WindowNotDrained:
                DocumentsInPlace(harness, 2);
                budget = "1";
                break;
        }

        var first = await harness.RunAsync(
            recentWrites: cause != HoldCause.ReportOnly, ordinaryWrites: pass == Pass.OrdinaryMoves, examinationBudget: budget);
        Held(first, pass).Should().BeTrue("the first run held this pass's watermark");

        _world.ClearQueryFaults();
        _ordinaryListingFaults = false;
        _time.Advance(Gap);
        var second = await harness.RunAsync(recentWrites: true, ordinaryWrites: pass == Pass.OrdinaryMoves);

        Since(second, pass).Should().Be((T0 - Job.InitialRecentChangesLookback).UtcDateTime,
            "the window the first run could not finish is listed from its start, not from the second run's own look-back");
        if (pass == Pass.RecentChanges)
        {
            _world.OwnerOf("sprk_todo", _todo).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
                "the listing reaches a to-do changed inside the first run's window");
        }
    }

    /// <summary>
    /// Review F1: the secure-record listing runs before every pass and is not caught, so a fault there (or the page bound)
    /// makes the whole first run throw before any pass is reached. Every pass's first window must already be stored by then;
    /// the re-file pass most of all, since no catch-up walks documents.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AfterAFirstRunThatThrewListingTheSecureRecords_EveryPassListsFromTheFirstRunsWindowStart()
    {
        var harness = new Harness(this);
        _world.FailingQueriesOf("sprk_workassignment"); // a table the secure-record listing reads

        var first = () => harness.RunAsync(recentWrites: true, ordinaryWrites: true);
        await first.Should().ThrowAsync<InvalidOperationException>("the secure-record listing is not caught: the run throws");

        _world.ClearQueryFaults();
        _time.Advance(Gap);
        var second = await harness.RunAsync(recentWrites: true, ordinaryWrites: true);

        using var scope = new FluentAssertions.Execution.AssertionScope();
        foreach (var pass in Enum.GetValues<Pass>())
        {
            Since(second, pass).Should().Be((T0 - Job.InitialRecentChangesLookback).UtcDateTime,
                $"the {pass} pass lists from the window start the thrown first run stored, not from the second run's own look-back");
        }

        _world.OwnerOf("sprk_todo", _todo).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "the recent-changes listing reaches a to-do changed inside the first run's window");
    }

    [Theory]
    [InlineData(Pass.RecentChanges)]
    [InlineData(Pass.OrdinaryMoves)]
    [InlineData(Pass.AccessPermission)]
    [InlineData(Pass.Refile)]
    public async Task ExecuteAsync_AfterAFirstRunThatCompleted_ListsFromThatRunsStartLessTheOverlap(Pass pass)
    {
        var harness = new Harness(this);
        await harness.RunAsync(recentWrites: true, ordinaryWrites: pass == Pass.OrdinaryMoves);
        _time.Advance(Gap);

        var second = await harness.RunAsync(recentWrites: true, ordinaryWrites: pass == Pass.OrdinaryMoves);

        Since(second, pass).Should().Be((T0 - Job.RecentChangesOverlap).UtcDateTime,
            "a completed run still moves the watermark forward");
    }

    /// <summary>
    /// The ordinary-parent-move pass is report-only by default, so it does NOT store the first window in a report-only run
    /// (a start only a writing run moves would grow its window without bound). The first writing run looks back from its own
    /// start, and the switch to writes starts the catch-up sweep over, which walks every ordinary root.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryMovesAfterAReportOnlyRun_LooksBackFromItsOwnStart_AndSweepsEveryRootAgain()
    {
        var harness = new Harness(this);
        await harness.RunAsync(recentWrites: true, ordinaryWrites: false);
        _time.Advance(Gap);

        var second = await harness.RunAsync(recentWrites: true, ordinaryWrites: true);

        Since(second, Pass.OrdinaryMoves).Should().Be((T0 + Gap - Job.InitialRecentChangesLookback).UtcDateTime);
        second.GetProperty("ordinaryMoves").GetProperty("sweep").GetProperty("ran").GetBoolean().Should().BeTrue(
            "a switch to writes starts the catch-up sweep over (F4-a)");
    }

    // ── The world ──

    /// <summary><paramref name="count"/> documents in place under the plain matter, changed inside the first window.</summary>
    private void DocumentsInPlace(Harness harness, int count)
    {
        foreach (var id in Enumerable.Range(0, count).Select(_ => Guid.NewGuid()))
        {
            harness.Pointer.Rows[("sprk_document", id)] = R.Doc(id, T.PlainMatter, T.CustomerA1Container);
            _world.Add("sprk_document", id,
                ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                ("sprk_graphdriveid", T.CustomerA1Container));
            _world.Modified("sprk_document", id, (T0 - TimeSpan.FromMinutes(5)).UtcDateTime);
        }
    }

    private static string Name(Pass pass) => pass switch
    {
        Pass.RecentChanges => "recentChanges",
        Pass.OrdinaryMoves => "ordinaryMoves",
        Pass.AccessPermission => "accessPermission",
        _ => "refileRelocations",
    };

    private static DateTime Since(JsonElement run, Pass pass) => run.GetProperty(Name(pass)).GetProperty("since").GetDateTime();

    /// <summary>Whether the run left the pass's window where it was: the window it reports, or (report-only) its mode.</summary>
    private static bool Held(JsonElement run, Pass pass)
    {
        var report = run.GetProperty(Name(pass));
        return pass == Pass.Refile
            ? report.GetProperty("watermarkHeld").GetBoolean()
            : report.GetProperty("mode").GetString() == Job.ModeReportOnly
              || report.GetProperty("failure").ValueKind == JsonValueKind.String;
    }

    private static bool IsOrdinaryListing(QueryExpression query) =>
        SecureChildLineage.IsRoot(query.EntityName)
        && query.Criteria.Conditions.Any(c => c.AttributeName == "sprk_issecure")
        && query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon");

    // ── The job, as the re-file harness composes it, on the test's clock ──

    private sealed class Harness
    {
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly Job _job;

        public Harness(Issue2735_HeldFirstRunSecureChildWatermarkTests test)
        {
            var world = test._world;
            var rig = new T.Rig(Pointer);
            var entities = SecureChildShareWorld.EntitiesOver(() => world);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) => test._ordinaryListingFaults && IsOrdinaryListing(query)
                    ? throw new InvalidOperationException("Test: the reassigned ordinary records could not be listed.")
                    : Task.FromResult(world.Answer(query)));

            var services = new ServiceCollection();
            services.AddSingleton(entities.Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), null!));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => world, new FakeRecordShareTable()));
            services.AddScoped(_ => rig.Relocator);
            services.AddScoped(_ => rig.Resolver);
            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => world));
            foreach (var (key, value) in SecureChildShareWorld.Configuration().AsEnumerable())
                _configuration[key] = value;
            _job = new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), test._time,
                _configuration, NullLogger<Job>.Instance);
        }

        /// <summary>The relocator's view of the documents (task 166's pointer world).</summary>
        public TestRecordContainerResolver.DocumentPointerWorld Pointer { get; } = R.Env();

        /// <summary>A manual run: the catch-up stands aside, so only the recent-changes listing can reach the to-do.</summary>
        public async Task<JsonElement> RunAsync(bool recentWrites, bool ordinaryWrites, string? examinationBudget = null)
        {
            _configuration[Job.RecentChangesWritesEnabledConfigKey] = recentWrites ? null : "false";
            _configuration[Job.OrdinaryMovesWritesEnabledConfigKey] = ordinaryWrites ? "true" : null;
            _configuration[Job.RefileExaminationBudgetConfigKey] = examinationBudget;
            var result = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "corr-2735", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
                CancellationToken.None);
            using var doc = JsonDocument.Parse(result.ResultJson!);
            return doc.RootElement.Clone();
        }
    }
}
