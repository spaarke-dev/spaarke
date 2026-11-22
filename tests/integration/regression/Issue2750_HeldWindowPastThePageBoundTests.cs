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
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Job = Sprk.Bff.Api.Services.Access.SecureChildReconciliationJob;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2750: four passes of <see cref="Job"/> — recent changes, the inherited Access Permission, the re-file pass and ordinary
/// parent moves — hold their watermark while a run cannot complete (the emergency stop, a listing fault), and a held window
/// keeps growing. Each window listing read at most the page bound (20 pages) per table and THREW past it, and the watermark
/// moved only past a completed listing, so once the held window grew past the bound the pass failed EVERY run, even after
/// the cause cleared, until the instance restarted. The fix caps a listing at the bound, oldest change first, and moves the
/// watermark only to the last listed instant (inclusive, so rows sharing it are listed again): the window drains.
/// <para>Over the in-memory Dataverse (<see cref="SecureChildShareWorld"/>, answering one row a page so the 20-page bound is
/// 20 rows) with the REAL job, reconcilers, synchronizer, relocator and resolver (the #2735 harness), on a clock the test
/// moves. Each pass gets 24 rows changed in the held window — four past the bound — and six of them share the one instant the
/// cap falls on, three before the cap and three after. Each row needs its own correction (its own secure record, its own
/// access value, its own file move, its own reassigned record), so a skipped row is seen. An inherent multi-record arrange at
/// a KEEP integration path.</para>
/// </summary>
public class Issue2750_HeldWindowPastThePageBoundTests
{
    private const int Rows = 24;
    private const int Restricted = InheritedAccessPermission.Restricted;
    private const int Standard = InheritedAccessPermission.Standard;

    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Business unit B, where the ordinary records were reassigned, and its default team.</summary>
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-000000275001");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-000000275002");

    private readonly FakeTimeProvider _time = new(T0);
    private readonly SecureChildShareWorld _world = R.JobWorld();
    private bool _ordinaryListingFaults;

    public enum Pass
    {
        RecentChanges,
        AccessPermission,
        Refile,
        OrdinaryMoves,
    }

    /// <summary>
    /// The held window grows past the page bound; once the cause clears, the pass drains it within a few runs and every row
    /// changed in it — the three that share the cap's instant past the cap included — gets its correction.
    /// </summary>
    [Theory]
    [InlineData(Pass.RecentChanges)]
    [InlineData(Pass.AccessPermission)]
    [InlineData(Pass.Refile)]
    [InlineData(Pass.OrdinaryMoves)]
    public async Task ExecuteAsync_AfterAHeldWindowGrewPastThePageBound_RecoversWithinAFewRuns_AndSkipsNoRow(Pass pass)
    {
        var harness = new Harness(this);
        var ordinary = pass == Pass.OrdinaryMoves;
        await harness.RunAsync(recentWrites: true, ordinaryWrites: ordinary); // stores the watermark; the sweeps complete

        var done = Seed(harness, pass);

        // Hold the window while it grows past the bound: the emergency stop (the ordinary pass: a listing fault — the stop
        // switches its mode, and a switch restarts its catch-up sweep, which would walk the records whatever the listing did).
        _ordinaryListingFaults = ordinary;
        _time.Advance(TimeSpan.FromMinutes(5));
        var held = await harness.RunAsync(recentWrites: ordinary, ordinaryWrites: ordinary);
        Report(held, pass).GetProperty("mode").GetString().Should().Be(ordinary ? Job.ModeWrite : Job.ModeReportOnly);
        done().Should().BeFalse("nothing is corrected while the window is held");

        _ordinaryListingFaults = false;
        var runs = new List<JsonElement>();
        while (!done() && runs.Count < 4)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            runs.Add(await harness.RunAsync(recentWrites: true, ordinaryWrites: ordinary));
        }

        using var scope = new FluentAssertions.Execution.AssertionScope();
        runs.Select(r => Report(r, pass).GetProperty("failure").ValueKind).Should().OnlyContain(k => k == JsonValueKind.Null,
            "a listing past the page bound is capped there, not a failure");
        runs.Should().Contain(r => Report(r, pass).GetProperty("cappedAt").ValueKind == JsonValueKind.String,
            "the held window's listing reached the page bound");
        done().Should().BeTrue($"the {pass} pass drains its held window within a few runs, the rows sharing the cap's instant included");
    }

    /// <summary>
    /// The ordinary pass's carry holds fewer roots than one capped listing returns: past it, the watermark moves to the oldest
    /// change of the roots left out (they are listed again), so the window still drains rather than being held for ever.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OrdinaryMovesPastTheCarryCap_DrainsTheHeldWindow_AndSkipsNoRoot()
    {
        var harness = new Harness(this, ordinaryCarryCap: 2);
        await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "5");
        var done = Seed(harness, Pass.OrdinaryMoves);

        var runs = 0;
        while (!done() && runs < 12)
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            var run = await harness.RunAsync(recentWrites: true, ordinaryWrites: true, maxRoots: "5");
            Report(run, Pass.OrdinaryMoves).GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
            runs++;
        }

        done().Should().BeTrue("five roots a run, the roots past the carry listed again: 24 roots drain in a few runs");
    }

    // ── The world ──

    /// <summary>When the <paramref name="i"/>-th row changed: one second apart, except rows 17–22, which share one instant.
    /// One row a page and 20 pages read rows 0–19, so the cap falls inside that instant.</summary>
    private static DateTime At(int i) => (T0 + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(i is >= 17 and <= 22 ? 17 : i)).UtcDateTime;

    /// <summary>The 24 rows the pass must correct, changed in the held window; returns whether all are corrected.</summary>
    private Func<bool> Seed(Harness harness, Pass pass)
    {
        _world.MaxRowsPerPage = 1; // the 20-page bound is 20 rows: a listing of 24 rows reaches it
        var ids = Enumerable.Range(0, Rows).Select(_ => Guid.NewGuid()).ToArray();
        switch (pass)
        {
            case Pass.RecentChanges:
                {
                    // A to-do written outside the product under each of 24 secure records (8 per root table, within the bound).
                    var roots = new[] { ("sprk_matter", "sprk_regardingmatter"), ("sprk_project", "sprk_regardingproject"),
                    ("sprk_workassignment", "sprk_regardingworkassignment") };
                    for (var i = 0; i < Rows; i++)
                    {
                        var (table, lookup) = roots[i % roots.Length];
                        var root = Guid.NewGuid();
                        _world.SecureRoot(table, root)
                            .UserOwnedChild("sprk_todo", ids[i], (lookup, table, root))
                            .Modified("sprk_todo", ids[i], At(i));
                    }

                    return () => ids.All(id =>
                        _world.OwnerOf("sprk_todo", id) == DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
                }

            case Pass.AccessPermission:
                {
                    // 24 to-dos under one Restricted matter that did not change: each to-do is reached only by its own listing.
                    var matter = Guid.NewGuid();
                    _world.OrdinaryRoot("sprk_matter", matter);
                    _world.Set("sprk_matter", matter, InheritedAccessPermission.Column, new OptionSetValue(Restricted));
                    for (var i = 0; i < Rows; i++)
                    {
                        _world.OrdinaryChild("sprk_todo", ids[i], ("sprk_regardingmatter", "sprk_matter", matter));
                        _world.Set("sprk_todo", ids[i], InheritedAccessPermission.Column, new OptionSetValue(Standard));
                        _world.Modified("sprk_todo", ids[i], At(i));
                    }

                    return () => ids.All(id =>
                        _world.ValueOf<OptionSetValue>("sprk_todo", id, InheritedAccessPermission.Column)?.Value == Restricted);
                }

            case Pass.Refile:
                {
                    // 24 documents re-filed under the plain matter whose files are still in another business unit's container.
                    for (var i = 0; i < Rows; i++)
                    {
                        harness.Pointer.Rows[("sprk_document", ids[i])] = R.Doc(ids[i], T.PlainMatter, T.CustomerBContainer);
                        _world.Add("sprk_document", ids[i],
                            ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                            ("sprk_graphdriveid", T.CustomerBContainer));
                        _world.Modified("sprk_document", ids[i], At(i));
                    }

                    return () => ids.All(id => harness.Rig.DriveOf(id) == T.CustomerA1Container);
                }

            default:
                {
                    // 24 ordinary matters reassigned to business unit B, each with an event its old default team still owns.
                    _world.Add("businessunit", BuB, ("name", "Litigation"))
                        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0);
                    var events = Enumerable.Range(0, Rows).Select(_ => Guid.NewGuid()).ToArray();
                    for (var i = 0; i < Rows; i++)
                    {
                        _world.Add("sprk_matter", ids[i], ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false));
                        _world.Modified("sprk_matter", ids[i], At(i));
                        _world.Add("sprk_event", events[i],
                            ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
                            ("sprk_regardingmatter", new EntityReference("sprk_matter", ids[i])),
                            ("versionnumber", 1L));
                    }

                    return () => events.All(id => _world.OwnerOf("sprk_event", id) == DataversePrincipalRef.Team(TeamB));
                }
        }
    }

    private static JsonElement Report(JsonElement run, Pass pass) => run.GetProperty(pass switch
    {
        Pass.RecentChanges => "recentChanges",
        Pass.AccessPermission => "accessPermission",
        Pass.Refile => "refileRelocations",
        _ => "ordinaryMoves",
    });

    private static bool IsOrdinaryListing(QueryExpression query) =>
        SecureChildLineage.IsRoot(query.EntityName)
        && query.Criteria.Conditions.Any(c => c.AttributeName == "sprk_issecure")
        && query.Criteria.Conditions.Any(c => c.AttributeName == "modifiedon");

    // ── The job, as the #2735 harness composes it (with the ordinary pass's conditional Web API), on the test's clock ──

    private sealed class Harness
    {
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly Job _job;

        public Harness(Issue2750_HeldWindowPastThePageBoundTests test, int ordinaryCarryCap = Job.MaxCarriedRows)
        {
            var world = test._world;
            Rig = new T.Rig(Pointer);
            var entities = SecureChildShareWorld.EntitiesOver(() => world);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) => test._ordinaryListingFaults && IsOrdinaryListing(query)
                    ? throw new InvalidOperationException("Test: the reassigned ordinary records could not be listed.")
                    : Task.FromResult(world.Answer(query)));

            var webApi = new SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi(() => world);
            var services = new ServiceCollection();
            services.AddSingleton(entities.Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), webApi));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => world, new FakeRecordShareTable()));
            services.AddScoped(_ => Rig.Relocator);
            services.AddScoped(_ => Rig.Resolver);
            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => world));
            foreach (var (key, value) in SecureChildShareWorld.Configuration().AsEnumerable())
                _configuration[key] = value;
            _configuration[Job.MaxRelocationsPerRunConfigKey] = "100";
            _job = new Job(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), test._time,
                _configuration, NullLogger<Job>.Instance)
            {
                OrdinaryCarryCap = ordinaryCarryCap,
            };
        }

        /// <summary>The relocator's view of the documents (task 166's pointer world).</summary>
        public TestRecordContainerResolver.DocumentPointerWorld Pointer { get; } = R.Env();

        public T.Rig Rig { get; }

        /// <summary>A manual run: the secure catch-up stands aside, so only the recent-changes listing reaches the to-dos.</summary>
        public async Task<JsonElement> RunAsync(bool recentWrites, bool ordinaryWrites, string? maxRoots = null)
        {
            _configuration[Job.RecentChangesWritesEnabledConfigKey] = recentWrites ? null : "false";
            _configuration[Job.OrdinaryMovesWritesEnabledConfigKey] = ordinaryWrites ? "true" : null;
            _configuration[Job.MaxRootsPerRunConfigKey] = maxRoots;
            var result = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "corr-2750", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
                CancellationToken.None);
            using var doc = JsonDocument.Parse(result.ResultJson!);
            return doc.RootElement.Clone();
        }
    }
}
