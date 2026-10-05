// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: the legacy migration (unified-access-control-r2 task 166 f1; owner round 21 item 1 (ii)) is the
//     gate the STRICT document-pointer rule is flipped behind. These tests pin what a run writes (nothing in report-only
//     mode; relocations only through the relocator with writes enabled), the flip gate (a document the interim rule
//     serves and the strict rule would refuse is counted and blocks a clean run), and contiguous batching (the script
//     proves a pass from startAfter / endAt).
//
// Doubles are module boundaries only: the REAL job, relocator and resolver over the document-pointer world and the
// SpeFileStore facade's virtual methods (DocumentContainerRelocatorTests.Rig).

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class DocumentContainerMigrationJobTests
{
    private static readonly Guid InPlace = Guid.Parse("40000000-0000-4000-8000-00000000f1a0");
    private static readonly Guid Misplaced = Guid.Parse("41000000-0000-4000-8000-00000000f1a0");
    private static readonly Guid Undecidable = Guid.Parse("42000000-0000-4000-8000-00000000f1a0");
    private static readonly Guid ForgedInPlace = Guid.Parse("43000000-0000-4000-8000-00000000f1a0");

    private static Entity Document(Guid id, Guid matter, string drive, string item)
    {
        var row = TestRecordContainerResolver.DocumentPointerWorld.Document(id, owningBusinessUnit: CustomerA1);
        row["sprk_matter"] = new EntityReference("sprk_matter", matter);
        row["sprk_graphdriveid"] = drive;
        row["sprk_graphitemid"] = item;
        return row;
    }

    private static (DocumentContainerMigrationJob Job, Rig Rig) Arrange(
        TestRecordContainerResolver.DocumentPointerWorld world, bool writes = false, int? batch = null, Rig? rig = null)
    {
        rig ??= new Rig(world);
        var services = new ServiceCollection()
            .AddSingleton(world.EntityService!)
            .AddSingleton(rig.Resolver)
            .AddSingleton(rig.Relocator)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DocumentContainerMigrationJob.WritesEnabledKey] = writes ? "true" : null,
            [DocumentContainerMigrationJob.MaxDocumentsPerRunKey] = batch?.ToString(),
        }).Build();
        var job = new DocumentContainerMigrationJob(
            services.GetRequiredService<IServiceScopeFactory>(), configuration, TimeProvider.System,
            NullLogger<DocumentContainerMigrationJob>.Instance);
        return (job, rig);
    }

    private static Task<JobRunResult> RunAsync(DocumentContainerMigrationJob job)
        => job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-166", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);

    [Fact]
    public async Task AReportOnlyRun_PlansTheMoves_CountsTheFlipBlockers_AndWritesNothing()
    {
        var world = Environment();
        world.Rows[("sprk_document", InPlace)] = Document(InPlace, PlainMatter, CustomerA1Container, "01INPLACE");
        world.Rows[("sprk_document", Misplaced)] = Document(Misplaced, PlainMatter, CustomerBContainer, "01MISPLACED");
        // Linked to a matter that cannot be read: undecidable for the strict rule, yet served today by the interim rule
        // (the creator's own upload, in the owner's customer subtree) — the flip would newly refuse it.
        world.Rows[("sprk_document", Undecidable)] = Document(Undecidable, Guid.NewGuid(), CustomerA1Container, "01UNDECIDABLE");
        var (job, rig) = Arrange(world);

        var result = await RunAsync(job);

        result.Success.Should().BeFalse("a planned move and a flip blocker are work left to do");
        var report = JsonNode.Parse(result.ResultJson!)!;
        report["mode"]!.GetValue<string>().Should().Be("report-only");
        report["examined"]!.GetValue<int>().Should().Be(3);
        report["counts"]!["InPlace"]!.GetValue<int>().Should().Be(1);
        report["counts"]!["WouldRelocate"]!.GetValue<int>().Should().Be(1);
        report["counts"]!["Undecidable"]!.GetValue<int>().Should().Be(1);
        report["wouldNewlyRefuse"]!.GetValue<int>().Should().Be(1,
            "flipping the strict rule would refuse a document the interim rule serves — the flip gate must see it");
        report["passComplete"]!.GetValue<bool>().Should().BeTrue();
        world.Updates.Should().BeEmpty();
        rig.Steps.Should().BeEmpty("report-only downloads, uploads and deletes nothing");
    }

    [Fact]
    public async Task AWriteRun_RelocatesTheMisplacedFile_AndTheNextRunIsClean()
    {
        var world = Environment();
        world.Rows[("sprk_document", Misplaced)] = Document(Misplaced, PlainMatter, CustomerBContainer, Item);
        var (job, rig) = Arrange(world, writes: true);

        var first = await RunAsync(job);
        var second = await RunAsync(job);

        var report = JsonNode.Parse(first.ResultJson!)!;
        report["counts"]!["Relocated"]!.GetValue<int>().Should().Be(1);
        world.Updates.First().Fields["sprk_graphdriveid"].Should().Be(CustomerA1Container);
        // F1 / owner round 37 item 3: the moved file is SERVED by the interim rule in force — not merely by the strict one.
        var row = report["rows"]!.AsArray().Should().ContainSingle().Subject!;
        row["interim"]!.GetValue<bool>().Should().BeTrue("a relocated file stays downloadable before the strict flip");
        row["strict"]!.GetValue<bool>().Should().BeTrue();
        report["relocatedButRefused"]!.GetValue<int>().Should().Be(0);
        report["servedOnlyAfterFlip"]!.GetValue<int>().Should().Be(0);
        report["pending"]!.GetValue<int>().Should().Be(0);
        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue("a re-run repeats nothing: the moved file is now in place");
        JsonNode.Parse(second.ResultJson!)!["counts"]!["InPlace"]!.GetValue<int>().Should().Be(1);
        rig.Steps.Count(s => s.StartsWith("upload", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task AWriteRun_WhoseMoveStillOwesAStep_IsNotClean_AndTheNextWriteRunSettlesIt()
    {
        // Round 37 / F2: the job is a repeat caller too — a source delete that failed is settled by the next pass.
        var world = Environment();
        world.Rows[("sprk_document", Misplaced)] = Document(Misplaced, PlainMatter, CustomerBContainer, Item);
        var (job, rig) = Arrange(world, writes: true);
        rig.DeleteFails = (drive, item) => drive == CustomerBContainer && item == Item;

        var first = await RunAsync(job);

        first.Success.Should().BeFalse("a relocation that still owes its source delete is work left to do");
        var report = JsonNode.Parse(first.ResultJson!)!;
        report["counts"]!["RelocationPending"]!.GetValue<int>().Should().Be(1);
        report["pending"]!.GetValue<int>().Should().Be(1);
        report["rows"]!.AsArray().Should().ContainSingle().Which!["pending"]!.AsArray()
            .Select(p => p!.GetValue<string>()).Should().Contain(p => p.StartsWith("source-pending", StringComparison.Ordinal));

        rig.DeleteFails = (_, _) => false;
        var second = await RunAsync(job);

        second.Success.Should().BeTrue();
        JsonNode.Parse(second.ResultJson!)!["counts"]!["InPlace"]!.GetValue<int>().Should().Be(1);
        world.ItemFacts(CustomerBContainer, Item).Should().BeNull("the second pass deleted the source the first one could not");
    }

    [Fact]
    public async Task TheReport_CountsAndListsADocumentOnlyTheStrictRuleWouldServe()
    {
        // F1: interim refuses, strict serves — no longer invisible. A BFF-created row (no recorded person) whose item a
        // person uploaded, in its derived container: a census class the interim rule refuses by design.
        var world = Environment();
        var row = TestRecordContainerResolver.DocumentPointerWorld.Document(InPlace, TestRecordContainerResolver.PointerWorldBffUser, CustomerA1);
        row["sprk_matter"] = new EntityReference("sprk_matter", PlainMatter);
        row["sprk_graphdriveid"] = CustomerA1Container;
        row["sprk_graphitemid"] = "01PERSONUPLOAD";
        world.Rows[("sprk_document", InPlace)] = row;
        var (job, _) = Arrange(world);

        var result = await RunAsync(job);

        var report = JsonNode.Parse(result.ResultJson!)!;
        report["servedOnlyAfterFlip"]!.GetValue<int>().Should().Be(1);
        report["wouldNewlyRefuse"]!.GetValue<int>().Should().Be(0);
        var listed = report["rows"]!.AsArray().Should().ContainSingle().Subject!;
        listed["servedOnlyAfterFlip"]!.GetValue<bool>().Should().BeTrue();
        listed["interim"]!.GetValue<bool>().Should().BeFalse();
        result.Success.Should().BeTrue("the flip would SERVE it — it does not block the flip");
    }

    [Fact]
    public async Task ARelocatedFileTheRuleInForceRefuses_MakesTheRunUnclean()
    {
        // The guard behind round 37 item 3: if a relocated file were refused by the rule in force, the run is not clean
        // and -Verify fails. Modelled with a copy Graph reports as uploaded by an application that is not the BFF.
        var world = Environment();
        world.Rows[("sprk_document", Misplaced)] = Document(Misplaced, PlainMatter, CustomerBContainer, Item);
        var rig = new Rig(world, copyFacts: source => source with { UserObjectId = null, ApplicationId = Guid.NewGuid().ToString("D") });
        var (job, _) = Arrange(world, writes: true, rig: rig);

        var result = await RunAsync(job);

        var report = JsonNode.Parse(result.ResultJson!)!;
        report["relocatedButRefused"]!.GetValue<int>().Should().Be(1);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task UnderTheStrictRuleInForce_ARelocatedFileOnlyTheInterimRuleWouldRefuse_IsServed_AndTheRunIsClean()
    {
        // F-C / V5: relocatedButRefused asks the rule IN FORCE. With the strict rule in force, a moved file in its derived
        // container is served — even one the interim rule would refuse (here: a copy Graph reports as written by an
        // application that is not the BFF). Counting it against the interim rule would block a run that is clean.
        var world = new TestRecordContainerResolver.DocumentPointerWorld { ArchiveContainerId = ArchiveContainer, Strict = true };
        foreach (var (unit, value) in Environment().BusinessUnits)
        {
            world.BusinessUnits[unit] = value;
        }

        world.Rows[("sprk_matter", PlainMatter)] = Environment().Rows[("sprk_matter", PlainMatter)];
        world.Rows[("sprk_document", Misplaced)] = Document(Misplaced, PlainMatter, CustomerBContainer, Item);
        var rig = new Rig(world, copyFacts: source => source with { UserObjectId = null, ApplicationId = Guid.NewGuid().ToString("D") });
        var (job, _) = Arrange(world, writes: true, rig: rig);

        var result = await RunAsync(job);

        var report = JsonNode.Parse(result.ResultJson!)!;
        var row = report["rows"]!.AsArray().Should().ContainSingle().Subject!;
        row["interim"]!.GetValue<bool>().Should().BeFalse("precondition: the interim rule would refuse this copy");
        row["strict"]!.GetValue<bool>().Should().BeTrue();
        report["relocatedButRefused"]!.GetValue<int>().Should().Be(0, "the rule in force (strict) serves it");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task TheReport_StatesATruncatedHistory_WithCounts_WithoutBlockingTheRun()
    {
        // Round 45 item 1: the target's version limit dropping the oldest replayed versions is stated, never silent — and
        // it is not work left to do (no retry could change the limit).
        var world = Environment();
        world.Rows[("sprk_document", Misplaced)] = Document(Misplaced, PlainMatter, CustomerBContainer, Item);
        var rig = new Rig(world) { CopyVersionLimit = 1 };
        rig.Versions[(CustomerBContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, Rig.Written, 100, Rig.CreatorName),
            new VersionInfoDto("2.0", null, Rig.Written.AddDays(1), 1234, Rig.CreatorName),
        ];
        var (job, _) = Arrange(world, writes: true, rig: rig);

        var result = await RunAsync(job);

        var report = JsonNode.Parse(result.ResultJson!)!;
        report["versionsTruncated"]!.GetValue<int>().Should().Be(1);
        var listed = report["versionsTruncatedRows"]!.AsArray().Should().ContainSingle().Subject!;
        listed["documentId"]!.GetValue<Guid>().Should().Be(Misplaced);
        listed["replayedVersions"]!.GetValue<int>().Should().Be(2);
        listed["keptVersions"]!.GetValue<int>().Should().Be(1);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AFileBothRulesRefuse_IsListedForAnAdministrator_ButDoesNotBlockTheFlip()
    {
        // Misplaced AND another person's upload: never copied (not verifiably the row's own), refused today and after
        // the flip alike — so the flip changes nothing for it.
        var world = Environment();
        world.Rows[("sprk_document", ForgedInPlace)] = Document(ForgedInPlace, PlainMatter, CustomerBContainer, Item);
        world.Items[(CustomerBContainer, Item)] = new SpeItemCreator("payroll.xlsx", OtherPersonObjectId.ToString("D"), null, 10);
        var (job, _) = Arrange(world);

        var result = await RunAsync(job);

        var report = JsonNode.Parse(result.ResultJson!)!;
        report["counts"]!["SourceUnverified"]!.GetValue<int>().Should().Be(1);
        report["wouldNewlyRefuse"]!.GetValue<int>().Should().Be(0);
        report["refusedByBoth"]!.GetValue<int>().Should().Be(1);
        report["rows"]!.AsArray().Should().ContainSingle().Which!["documentId"]!.GetValue<Guid>().Should().Be(ForgedInPlace);
        result.Success.Should().BeTrue("nothing is left that the migration may do, and the flip refuses nothing new");
    }

    [Fact]
    public async Task Batches_AreContiguous_AndAPassCompletesOnlyAtTheEnd()
    {
        var world = Environment();
        world.Rows[("sprk_document", InPlace)] = Document(InPlace, PlainMatter, CustomerA1Container, "01A");
        world.Rows[("sprk_document", Misplaced)] = Document(Misplaced, PlainMatter, CustomerA1Container, "01B");
        var (job, _) = Arrange(world, batch: 1);

        var one = JsonNode.Parse((await RunAsync(job)).ResultJson!)!;
        var two = JsonNode.Parse((await RunAsync(job)).ResultJson!)!;
        var three = JsonNode.Parse((await RunAsync(job)).ResultJson!)!;
        var fourth = JsonNode.Parse((await RunAsync(job)).ResultJson!)!;

        one["startAfter"].Should().BeNull("the first run of a pass starts at the first document");
        one["passComplete"]!.GetValue<bool>().Should().BeFalse();
        two["startAfter"]!.GetValue<Guid>().Should().Be(one["endAt"]!.GetValue<Guid>(), "each run begins where the last ended");
        three["startAfter"]!.GetValue<Guid>().Should().Be(two["endAt"]!.GetValue<Guid>());
        three["examined"]!.GetValue<int>().Should().Be(0);
        three["passComplete"]!.GetValue<bool>().Should().BeTrue();
        fourth["startAfter"].Should().BeNull("after a complete pass the next run starts a new one");
    }

    [Fact]
    public async Task TheBatchQuery_IsAKeysetReadOfPointeredDocuments_AfterTheCursor()
    {
        var cursor = Guid.NewGuid();
        QueryExpression? captured = null;
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback((QueryExpression q, CancellationToken _) => captured = q)
            .ReturnsAsync(new EntityCollection());

        await DocumentContainerMigrationJob.ReadBatchAsync(dataverse.Object, cursor, 25, CancellationToken.None);

        captured!.EntityName.Should().Be("sprk_document");
        captured.TopCount.Should().Be(25);
        captured.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "sprk_graphitemid" && c.Operator == ConditionOperator.NotNull);
        captured.Criteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "sprk_documentid" && c.Operator == ConditionOperator.GreaterThan && (Guid)c.Values[0] == cursor);
        captured.Orders.Should().ContainSingle(o => o.AttributeName == "sprk_documentid" && o.OrderType == OrderType.Ascending);
    }

    [Fact]
    public async Task AnEnumerationFault_FailsTheAttempt_SoTheSchedulerRetries()
    {
        var world = Environment();
        world.Rows[("sprk_document", InPlace)] = Document(InPlace, PlainMatter, CustomerA1Container, "01A");
        var faulty = new TestRecordContainerResolver.DocumentPointerWorld { RetrieveMultipleFault = new TimeoutException("Dataverse unavailable") };
        var (job, _) = Arrange(faulty);

        var act = () => RunAsync(job);

        await act.Should().ThrowAsync<InvalidOperationException>("ADR-036 A1 rule 4: an enumeration fault is retryable, never 'nothing to do'");
    }
}
