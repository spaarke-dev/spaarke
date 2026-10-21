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
/// <para>Task 189 (D7 T7a, #2126): a document holds max(its own narrowing, what its parents give it). Its own Restricted is
/// never widened (AC 1); an inherited value still follows its parents both ways (AC 2); anything it cannot read writes
/// nothing (AC 3); a concurrent narrowing is never overwritten. The To Do, Event and Communication tests above are
/// unchanged (AC 4).</para>
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

    [Fact]
    public async Task Run_WhenTheWriteCapIsReached_TheWindowIsKept_AndTheNextRunWritesTheRest_ThenItConverges()
    {
        // Verifier F3: a matter with many children turning Restricted must not be written in one run.
        var job = new Harness(maxWritesPerRun: 2);
        var matter = job.Root("sprk_matter", Standard);
        var todos = Enumerable.Range(0, 3)
            .Select(_ => job.Child("sprk_todo", Standard, ("sprk_regardingmatter", "sprk_matter", matter)))
            .ToArray();
        await job.RunAsync(); // settles (everything correct) and finishes the sweep
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);

        var capped = (await job.RunAsync()).GetProperty("accessPermission");
        var firstWrites = job.World.AccessPermissionWrites.Count;
        var next = (await job.RunAsync()).GetProperty("accessPermission");
        var settled = (await job.RunAsync()).GetProperty("accessPermission");

        firstWrites.Should().Be(2, "the run stops writing at its cap");
        capped.GetProperty("writeCapReached").GetBoolean().Should().BeTrue();
        capped.GetProperty("deferred").GetInt32().Should().Be(1);
        next.GetProperty("since").GetDateTime().Should().Be(capped.GetProperty("since").GetDateTime(),
            "a capped run keeps its watermark, so the next run lists the same window");
        next.GetProperty("changed").GetInt32().Should().Be(1, "only the row still differing is written");
        next.GetProperty("writeCapReached").GetBoolean().Should().BeFalse();
        job.World.AccessPermissionWrites.Select(w => w.Id).Should().BeEquivalentTo(todos, "every child, each written once");
        settled.GetProperty("changed").GetInt32().Should().Be(0, "it converges: correct rows are not written again");
        settled.GetProperty("since").GetDateTime().Should().BeAfter(capped.GetProperty("since").GetDateTime(),
            "the window moves once a run finishes under the cap");
    }

    // ── Task 189 (D7 T7a, #2126): a document's own narrowing ─────────────────────────────────────────────────────────

    private const string Record = AccessInheritance.Column;

    [Fact]
    public async Task Run_ADocumentsOwnRestricted_UnderAStandardMatter_IsKept_AndRecordedAsItsOwn()
    {
        // AC 1 (must fire). Before task 189 the run reset the document to its matter's Standard.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Standard);
        var document = job.Child("sprk_document", Standard, ("sprk_matter", "sprk_matter", matter));
        await job.RunAsync(); // settles: Standard under Standard, nothing written
        job.World.AccessRecordWrites.Should().BeEmpty("the Standard majority gets no record (no modifiedon churn)");

        job.World.Set("sprk_document", document, Column, new OptionSetValue(Restricted)); // a user's narrowing
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        var report = (await job.RunAsync()).GetProperty("accessPermission");
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        var again = (await job.RunAsync()).GetProperty("accessPermission");

        job.World.AccessPermissionWrites.Should().BeEmpty("the document's own Restricted is never widened to Standard");
        ValueOf(job, document).Should().Be(Restricted);
        var record = RecordOf(job, document);
        record.OwnPermission.Should().Be(Restricted, "the narrowing is recorded as the document's own");
        record.FloorPermission.Should().Be(Standard);
        record.Parents.Should().BeEquivalentTo(new[] { $"sprk_matter:{matter:D}" });
        report.GetProperty("ownKept").GetInt32().Should().Be(1);
        report.GetProperty("recordsWritten").GetInt32().Should().Be(1);
        report.GetProperty("changed").GetInt32().Should().Be(0);
        again.GetProperty("recordsWritten").GetInt32().Should().Be(0, "a current record is not written again");
        again.GetProperty("ownKept").GetInt32().Should().Be(1);
        job.LastResult!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Run_AParentlessDocumentsOwnRestricted_IsRecorded_AndSurvivesFilingAndAWidening()
    {
        // Must fire: the parentless narrowing is recorded while parentless, so a later filing knows it was the document's.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var document = job.Child("sprk_document", Restricted);
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        var parentless = (await job.RunAsync()).GetProperty("accessPermission");

        job.World.AccessPermissionWrites.Should().BeEmpty("a parentless document's value is never written (round 81)");
        parentless.GetProperty("parentless").GetInt32().Should().Be(1);
        RecordOf(job, document).Parents.Should().BeEmpty();
        RecordOf(job, document).OwnPermission.Should().Be(Restricted);

        job.World.Set("sprk_document", document, "sprk_matter", new EntityReference("sprk_matter", matter)); // filed
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        await job.RunAsync();
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Standard)); // the matter widens
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().BeEmpty("its own Restricted stays through the filing and the widening");
        ValueOf(job, document).Should().Be(Restricted);
        RecordOf(job, document).FloorPermission.Should().Be(Standard);
        RecordOf(job, document).OwnPermission.Should().Be(Restricted);
    }

    [Fact]
    public async Task Run_ADocumentCreatedWithTheOfficeFallbackRecord_KeepsRestricted_WhenItsRestrictedMatterWidens()
    {
        // Must fire (D5, T8): under a Restricted matter a fallback equals its floor; only its record says it is its own.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var document = job.Child("sprk_document", Restricted, ("sprk_matter", "sprk_matter", matter));
        job.World.Set("sprk_document", document, Record, AccessInheritance.OwnNarrowing(Restricted).Serialize());
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().BeEmpty();
        ValueOf(job, document).Should().Be(Restricted);
        RecordOf(job, document).OwnPermission.Should().Be(Restricted);
    }

    [Fact]
    public async Task Run_ADocumentsInheritedRestricted_FollowsItsMatter_BothWays()
    {
        // AC 2 (must not fire): a value the document only inherits follows a widening, as before task 189.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Standard);
        var document = job.Child("sprk_document", Standard, ("sprk_matter", "sprk_matter", matter));
        await job.RunAsync();

        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        var widened = (await job.RunAsync()).GetProperty("accessPermission");

        job.World.AccessPermissionWrites.Should().Equal(
            ("sprk_document", document, Restricted), ("sprk_document", document, Standard));
        job.World.CombinedAccessWrites.Should().Equal(new[] { ("sprk_document", document), ("sprk_document", document) },
            "the value and its record go in ONE update");
        RecordOf(job, document).FloorPermission.Should().Be(Standard);
        RecordOf(job, document).OwnPermission.Should().BeNull();
        widened.GetProperty("ownKept").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Run_ALegacyDocumentWithNoRecord_UnderARestrictedMatter_FollowsAWidening()
    {
        // Must not fire (legacy rule): task 173 kept it equal to its floor, so its Restricted is inherited, not its own.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var document = job.Child("sprk_document", Restricted, ("sprk_matter", "sprk_matter", matter));
        await job.RunAsync(); // the sweep records {floor R, own none}

        RecordOf(job, document).FloorPermission.Should().Be(Restricted);
        RecordOf(job, document).OwnPermission.Should().BeNull();
        job.World.AccessPermissionWrites.Should().BeEmpty();

        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().ContainSingle().Which.Should().Be(("sprk_document", document, Standard));
    }

    [Fact]
    public async Task Run_ADocumentHandEditedBelowItsFloor_IsPutBack()
    {
        // Must not fire: nothing goes below the parents' value.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Limited);
        var document = job.Child("sprk_document", Limited, ("sprk_matter", "sprk_matter", matter));
        await job.RunAsync();

        job.World.Set("sprk_document", document, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().ContainSingle().Which.Should().Be(("sprk_document", document, Limited));
    }

    [Fact]
    public async Task Run_ADocumentEditedBackToItsFloor_DropsItsOwnNarrowing_AndFollowsItsMatterAgain()
    {
        // D3 (must not fire): a user with Write removes their own narrowing; the document follows its parents again.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Standard);
        var document = job.Child("sprk_document", Standard, ("sprk_matter", "sprk_matter", matter));
        await job.RunAsync();
        job.World.Set("sprk_document", document, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.Set("sprk_document", document, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);
        await job.RunAsync();
        RecordOf(job, document).OwnPermission.Should().BeNull("the edit back to the floor removed the own narrowing");

        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Restricted));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        await job.RunAsync();

        job.World.AccessPermissionWrites.Should().Equal(
            ("sprk_document", document, Restricted), ("sprk_document", document, Standard));
        ValueOf(job, document).Should().Be(Standard);
    }

    [Fact]
    public async Task Run_ADocumentWhoseRecordCannotBeRead_IsNotWritten_AndIsCarried()
    {
        // AC 3 (fail closed): an unreadable own narrowing is never guessed at.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var document = job.Child("sprk_document", Standard, ("sprk_matter", "sprk_matter", matter));
        job.World.Set("sprk_document", document, Record, "{not json");
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);

        var report = (await job.RunAsync()).GetProperty("accessPermission");

        job.World.AccessPermissionWrites.Should().BeEmpty();
        job.World.AccessRecordWrites.Should().BeEmpty();
        report.GetProperty("undetermined").GetInt32().Should().Be(1);
        report.GetProperty("carriedRows").GetInt32().Should().Be(1);
        report.GetProperty("problems").EnumerateArray().Single().GetString().Should().Contain(Record)
            .And.NotContain("{not json", "the reason names the column, never the stored text");
    }

    [Fact]
    public async Task Run_ADocumentHoldingAnUnknownLevel_IsNotWritten()
    {
        // AC 3 (fail closed): an option that is not Standard, Limited or Restricted is never ranked by guess.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var document = job.Child("sprk_document", 100000099, ("sprk_matter", "sprk_matter", matter));
        job.World.Modified("sprk_document", document, job.Now.UtcDateTime);

        var report = (await job.RunAsync()).GetProperty("accessPermission");

        job.World.AccessPermissionWrites.Should().BeEmpty();
        job.World.AccessRecordWrites.Should().BeEmpty();
        report.GetProperty("undetermined").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Run_WhenDocumentsLackTheRecordColumn_NoDocumentIsWritten_AndTheOtherTablesAreReconciled()
    {
        // AC 3 (fail closed) and AC 4: before the schema script runs, a document's own narrowing cannot be read.
        var job = new Harness();
        job.World.DocumentsLackAccessRecordColumn = true;
        var matter = job.Root("sprk_matter", Restricted);
        var document = job.Child("sprk_document", Standard, ("sprk_matter", "sprk_matter", matter));
        var todo = job.Child("sprk_todo", Standard, ("sprk_regardingmatter", "sprk_matter", matter));

        var report = (await job.RunAsync()).GetProperty("accessPermission");

        job.World.AccessPermissionWrites.Should().ContainSingle().Which.Should().Be(("sprk_todo", todo, Restricted));
        ValueOf(job, document).Should().Be(Standard);
        report.GetProperty("tablesWithoutColumn").EnumerateArray().Select(t => t.GetString())
            .Should().Contain("sprk_document");
    }

    [Fact]
    public async Task Run_ADocumentNarrowedBetweenTheListingAndTheWrite_IsNotOverwritten_AndDecidedAgainNextRun()
    {
        // Must fire (re-read, notes §2.2 step 6): an explicit own narrowing (the Office fallback, a future Manage Access
        // route) lands while the run is about to follow the matter's widening. Writing the run's plan would widen it.
        var job = new Harness();
        var matter = job.Root("sprk_matter", Restricted);
        var document = job.Child("sprk_document", Restricted, ("sprk_matter", "sprk_matter", matter));
        await job.RunAsync(); // records {floor R, own none}
        job.World.Set("sprk_matter", matter, Column, new OptionSetValue(Standard));
        job.World.Modified("sprk_matter", matter, job.Now.UtcDateTime);
        job.World.BeforeAccessRecordReadOf("sprk_document", document,
            w => w.Set("sprk_document", document, Record, AccessInheritance.OwnNarrowing(Restricted).Serialize()));

        var raced = (await job.RunAsync()).GetProperty("accessPermission");
        var next = (await job.RunAsync()).GetProperty("accessPermission");

        raced.GetProperty("changedConcurrently").GetInt32().Should().Be(1);
        raced.GetProperty("carriedRows").GetInt32().Should().Be(1, "it is decided again next run");
        job.World.AccessPermissionWrites.Should().BeEmpty("the narrowing that landed mid-run is never overwritten");
        ValueOf(job, document).Should().Be(Restricted);
        RecordOf(job, document).FloorPermission.Should().Be(Standard);
        RecordOf(job, document).OwnPermission.Should().Be(Restricted);
        next.GetProperty("changedConcurrently").GetInt32().Should().Be(0);
        next.GetProperty("ownKept").GetInt32().Should().Be(1);
    }

    private static int? ValueOf(Harness job, Guid document) =>
        job.World.ValueOf<OptionSetValue>("sprk_document", document, Column)?.Value;

    private static AccessInheritance RecordOf(Harness job, Guid document)
    {
        var (record, unreadable) = AccessInheritance.Parse(job.World.ValueOf<string>("sprk_document", document, Record));
        unreadable.Should().BeFalse();
        record.Should().NotBeNull("the document has a record");
        return record!;
    }

    /// <summary>The job over a world, as the scheduler builds it: one instance, the real reconciler, synchronizer and core-ancestor resolver per run.</summary>
    private sealed class Harness
    {
        private readonly FakeTimeProvider _time = new(Start);
        private readonly SecureChildReconciliationJob _job;
        private readonly ServiceProvider _provider;

        public Harness(int? maxRowsPerRun = null, int? maxWritesPerRun = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => World).Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => World, Shares, null!));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => World, Shares));
            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => World));
            _provider = services.BuildServiceProvider();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecureChildReconciliationJob.MaxAccessPermissionRowsPerRunConfigKey] = maxRowsPerRun?.ToString(),
                [SecureChildReconciliationJob.MaxAccessPermissionWritesPerRunConfigKey] = maxWritesPerRun?.ToString(),
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
