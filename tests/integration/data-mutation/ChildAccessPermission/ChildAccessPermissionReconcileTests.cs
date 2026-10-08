using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// unified-access-control-r2 task 173 (owner rounds 81/84, GitHub #1423) — <see cref="SecureChildReconciliationJob"/> keeps
/// <c>sprk_accesspermission</c> on To Do, Event, Communication and Document equal to the most restrictive value of what
/// they are filed under, within ONE run, through the job exactly as the scheduler runs it (one instance, a scope per run)
/// over an in-memory Dataverse that evaluates every query (<see cref="SecureChildShareWorld"/>).
/// </summary>
/// <remarks>
/// In scope (task 173 AC): a parent's change reaching every child of the four tables at any depth; a hand edit reverted;
/// rows already correct not written; a parentless row (and an un-filed one) never written — the one seeding proof the task
/// asks for is on that test; an unreadable parent leaving the row as it is; the sweep window backfilling rows nobody
/// changed, page by page.
/// </remarks>
public class ChildAccessPermissionReconcileTests
{
    private const int Standard = InheritedAccessPermission.Standard;
    private const int Limited = InheritedAccessPermission.Limited;
    private const int Restricted = InheritedAccessPermission.Restricted;
    private const string Column = InheritedAccessPermission.Column;

    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Run_WhenAMatterTurnsRestricted_EveryChildOfTheFourTablesFollowsInOneRun_AndCorrectRowsAreNotWritten()
    {
        var job = new Harness();
        var matter = job.Root("sprk_matter", Standard);
        var otherMatter = job.Root("sprk_matter", Standard);
        var todo = job.Child("sprk_todo", Standard, ("sprk_regardingmatter", "sprk_matter", matter));
        var evt = job.Child("sprk_event", Standard, ("sprk_regardingmatter", "sprk_matter", matter));
        var message = job.Child("sprk_communication", Standard, ("sprk_regardingmatter", "sprk_matter", matter));
        var document = job.Child("sprk_document", Standard, ("sprk_matter", "sprk_matter", matter));
        var todoOnDocument = job.Child("sprk_todo", Standard, ("sprk_regardingdocument", "sprk_document", document));
        job.Child("sprk_todo", Standard, ("sprk_regardingmatter", "sprk_matter", otherMatter));
        await job.RunAsync(); // settles: everything already correct, nothing written
        job.World.AccessPermissionWrites.Should().BeEmpty();

        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        var report = await job.RunAsync();

        job.World.AccessPermissionWrites.Select(w => (w.Table, w.Id, w.Value)).Should().BeEquivalentTo(new[]
        {
            ("sprk_todo", todo, Restricted), ("sprk_event", evt, Restricted), ("sprk_communication", message, Restricted),
            ("sprk_document", document, Restricted), ("sprk_todo", todoOnDocument, Restricted),
        }, "every child of the matter, at any depth, and nothing under the other matter");
        report.GetProperty("accessPermission").GetProperty("changed").GetInt32().Should().Be(5);
        job.LastResult!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Run_WhenAParentedChildIsHandEditedToStandard_ItIsRevertedInOneRun()
    {
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var todo = job.Child("sprk_todo", Restricted, ("sprk_regardingmatter", "sprk_matter", matter));
        await job.RunAsync();

        job.World.Set("sprk_todo", todo, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_todo", todo, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().ContainSingle().Which.Should().Be(("sprk_todo", todo, Restricted));
    }

    [Fact]
    public async Task Run_AParentlessChildsOwnValue_AndAnUnfiledChildsLastValue_AreNeverOverwritten()
    {
        // SEEDING PROOF (task 173 AC; notes §7), run 2026-10-08: replacing the `parents.Count == 0` branch in
        // ChildAccessPermissionReconciler.RunAsync with "a parentless row inherits Standard" turns this test red on the
        // write assertion (both rows written Standard); dropping the branch alone turns it red on `parentless`.
        var job = new Harness();
        var parentless = job.Child("sprk_todo", Limited);
        var unfiled = job.Child("sprk_event", Restricted); // was filed under a Restricted matter; its lookup was cleared
        job.World.Modified("sprk_todo", parentless, job.Now.UtcDateTime);
        job.World.Modified("sprk_event", unfiled, job.Now.UtcDateTime);

        var first = await job.RunAsync();
        var second = await job.RunAsync();

        job.World.AccessPermissionWrites.Should().BeEmpty("a parentless row's own value is the user's (round 81)");
        first.GetProperty("accessPermission").GetProperty("parentless").GetInt32().Should().Be(2);
        first.GetProperty("accessPermission").GetProperty("undetermined").GetInt32().Should().Be(0);
        second.GetProperty("accessPermission").GetProperty("changed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Run_WhenAParentCannotBeRead_TheChildIsLeftAsItIs_AndReportedUndetermined()
    {
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var todo = job.Child("sprk_todo", Standard, ("sprk_regardingmatter", "sprk_matter", matter));
        job.World.Modified("sprk_todo", todo, job.Now.UtcDateTime);
        job.World.FailingIdListReadsOf("sprk_matter", matter);

        var report = await job.RunAsync();

        job.World.AccessPermissionWrites.Should().BeEmpty("an unreadable parent leaves the stored value as it is");
        report.GetProperty("accessPermission").GetProperty("undetermined").GetInt32().Should().Be(1);
        report.GetProperty("accessPermission").GetProperty("carriedRows").GetInt32().Should().Be(1);

        job.World.ClearQueryFaults();
        await job.RunAsync();
        job.World.AccessPermissionWrites.Should().ContainSingle().Which.Should().Be(("sprk_todo", todo, Restricted),
            "the carried row is decided again in the next run, once its parent reads");
    }

    [Fact]
    public async Task Run_TheSweepWindowBackfillsRowsNobodyChanged_PageByPage_ThenStops()
    {
        var job = new Harness(maxRowsPerRun: 1);
        var matter = job.Root("sprk_matter", Restricted);
        var first = job.Child("sprk_todo", null, ("sprk_regardingmatter", "sprk_matter", matter));
        var second = job.Child("sprk_todo", null, ("sprk_regardingmatter", "sprk_matter", matter));

        await job.RunAsync();
        await job.RunAsync();
        var third = await job.RunAsync();

        job.World.AccessPermissionWrites.Select(w => w.Id).Should().BeEquivalentTo(new[] { first, second },
            "rows created before task 173 (stored null, never modified since) are set by the sweep, one per run at cap 1");
        third.GetProperty("accessPermission").GetProperty("sweep").GetProperty("complete").GetBoolean().Should().BeTrue();
    }

    /// <summary>The job over a world, as the scheduler builds it: one instance, the real reconciler, synchronizer and core-ancestor resolver per run.</summary>
    private sealed class Harness
    {
        private readonly FakeTimeProvider _time = new(Start);
        private readonly SecureChildReconciliationJob _job;
        private readonly ServiceProvider _provider;

        public Harness(int? maxRowsPerRun = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => World).Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => World, Shares, null!));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => World, Shares));
            services.AddSingleton(sp => new CoreAncestorResolver(
                sp.GetRequiredService<IGenericEntityService>(),
                InheritedAccessPermissionStampTests.Probe(),
                NullLogger<CoreAncestorResolver>.Instance));
            _provider = services.BuildServiceProvider();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecureChildReconciliationJob.MaxAccessPermissionRowsPerRunConfigKey] = maxRowsPerRun?.ToString(),
            }).Build();
            _job = new SecureChildReconciliationJob(
                _provider.GetRequiredService<IServiceScopeFactory>(), _time, configuration,
                NullLogger<SecureChildReconciliationJob>.Instance);
        }

        public SecureChildShareWorld World { get; } = SecureChildShareWorld.Standard();

        public FakeRecordShareTable Shares { get; } = new();

        public JobRunResult? LastResult { get; private set; }

        public DateTimeOffset Now => _time.GetUtcNow();

        public Guid Root(string table, int? value)
        {
            var id = Guid.NewGuid();
            World.OrdinaryRoot(table, id);
            if (value is { } v)
                World.Set(table, id, Column, new OptionSetValue(v));
            return id;
        }

        public Guid Child(string table, int? value, params (string Column, string Target, Guid Id)[] lookups)
        {
            var id = Guid.NewGuid();
            World.OrdinaryChild(table, id, lookups);
            if (value is { } v)
                World.Set(table, id, Column, new OptionSetValue(v));
            return id;
        }

        /// <summary>One scheduled run, two minutes after the previous one.</summary>
        public async Task<JsonElement> RunAsync()
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            LastResult = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
                CancellationToken.None);
            using var doc = JsonDocument.Parse(LastResult.ResultJson!);
            return doc.RootElement.Clone();
        }
    }
}
