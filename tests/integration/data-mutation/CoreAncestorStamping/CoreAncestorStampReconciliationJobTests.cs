using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// unified-access-control-r2 task 156 — the 5-minute safety net (owner round 3 R3/R4: minutes, not hours): every child
/// whose copy of its intermediate's root went stale OUTSIDE the BFF (a form, a flow, an import, a client wizard, a failed
/// cascade, a regarding cleared on a form) is re-stamped on the next run; a second run changes nothing; a scan that fails
/// is a FAILED run, never "nothing stale".
/// </summary>
public class CoreAncestorStampReconciliationJobTests
{
    private static readonly Guid MatterA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid Communication = Guid.Parse("15600000-0000-0000-0000-000000000c01");
    private static readonly Guid Event = Guid.Parse("15600000-0000-0000-0000-000000000e01");
    private static readonly Guid Todo = Guid.Parse("15600000-0000-0000-0000-000000000101");
    private static readonly Guid TodoUnderEvent = Guid.Parse("15600000-0000-0000-0000-000000000104");
    private static readonly Guid CarrierTodo = Guid.Parse("15600000-0000-0000-0000-000000000105");
    private static readonly Guid CommunicationTypeRef = Guid.Parse("0c0c0c0c-0000-0000-0000-00000000000c");

    [Fact(DisplayName = "Task 156: the job finds a child made stale OUT OF BAND and re-stamps it; a second run changes nothing")]
    public async Task StaleChild_IsRepaired_AndASecondRunChangesNothing()
    {
        // The communication was re-filed A → B on its form: no BFF path ran, the to-do still says A.
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        var job = Job(world);

        var first = await job.ExecuteAsync(Context(), CancellationToken.None);

        first.Success.Should().BeTrue(first.ErrorMessage);
        first.ProcessedItems.Should().Be(1);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);

        world.Patches.Clear();
        var second = await job.ExecuteAsync(Context(), CancellationToken.None);

        second.Success.Should().BeTrue();
        second.ProcessedItems.Should().Be(0);
        world.Patches.Should().BeEmpty("the copy now equals the root — a second run is a no-op");
        Counts(second).GetProperty("stale").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "Task 156: a child whose cascade PATCH failed is repaired by the job's next run")]
    public async Task ChildTheCascadeFailedOn_IsRepairedByTheJob()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString())
            .FailWrite("sprk_todo", Todo);

        var cascade = await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);
        cascade.Failures.Should().ContainSingle().Which.Id.Should().Be(Todo);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterA);

        world.HealWrite("sprk_todo", Todo);
        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156: a stale event and the to-do under it converge in ONE run (the repair cascades)")]
    public async Task StaleChain_ConvergesInOneRun()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_event", Event,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString())
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Event.ToString());

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_event", Event, "sprk_regardingmatter").Should().Be(MatterB);
        world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156: a scan that FAILS records a FAILED run — never 'nothing stale' — and the other tables are still reconciled")]
    public async Task FailedScan_IsAFailedRun_NeverZero()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString())
            .FailScan("sprk_event");

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeFalse("an unread table is not a clean table");
        run.ErrorMessage.Should().Contain("sprk_event");
        Counts(run).GetProperty("status").GetString().Should().Be(CoreAncestorStampReconciliationJob.StatusError);
        Counts(run).GetProperty("scanFailures").GetInt32().Should().Be(1);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB, "the readable tables still repaired");
    }

    [Fact(DisplayName = "Task 156: report-only (dry run) reports every stale row and writes NOTHING; an unparseable switch is report-only too")]
    public async Task ReportOnly_WritesNothing()
    {
        foreach (var value in new[] { "false", "not-a-bool" })
        {
            var world = new StampWorld()
                .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
                .Row("sprk_todo", Todo,
                    [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                    pairId: Communication.ToString());

            var run = await Job(world, writesEnabled: value).ExecuteAsync(Context(), CancellationToken.None);

            world.Patches.Should().BeEmpty();
            Counts(run).GetProperty("stale").GetInt32().Should().Be(1);
            Counts(run).GetProperty("mode").GetString().Should().Be("report-only");
        }
    }

    [Fact(DisplayName = "Task 156: the Office carrier to-do (a direct matter) is never touched by the job")]
    public async Task CarrierTodo_IsNeverTouched()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", CarrierTodo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: MatterA.ToString());

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 156 (F-051-6): the job clears a copy orphaned by a regarding CLEARED on the form")]
    public async Task OrphanedCopy_IsClearedByTheJob()
    {
        var world = new StampWorld()
            .RecordType(CommunicationTypeRef, "sprk_communication")
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", Todo, [("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString(), pairType: CommunicationTypeRef);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().BeNull();
    }

    [Fact(DisplayName = "Task 156 (ADR-036 A1 rule 3): a retry of the SAME run does not re-apply a repair it already marked complete")]
    public async Task RepairAlreadyMarkedComplete_IsNotReappliedByARetryOfTheSameRun()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        var idempotency = new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<IdempotencyService>.Instance);
        var runId = Guid.NewGuid();

        // Attempt 1's repair of THIS row to THIS value is marked complete (the write landed, the row was then put back
        // out of band before the host's retry of the same run re-planned it).
        await Job(world, idempotency: idempotency).ExecuteAsync(Context(runId: runId), CancellationToken.None);
        world.OutOfBand("sprk_todo", Todo, "sprk_regardingmatter", "sprk_matter", MatterA);
        world.Patches.Clear();

        var retry = await Job(world, idempotency: idempotency)
            .ExecuteAsync(Context(attempt: 2, runId: runId), CancellationToken.None);

        world.Patches.Should().BeEmpty("the marker for this exact repair in this run is still live");
        Counts(retry).GetProperty("alreadyApplied").GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Task 156: a row put back out of band AFTER a run repaired it is repaired again by the NEXT run — the completion marker never suppresses a new staleness")]
    public async Task RowStaleAgainAfterARepair_IsRepairedByTheNextRun()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        var idempotency = new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<IdempotencyService>.Instance);

        await Job(world, idempotency: idempotency).ExecuteAsync(Context(), CancellationToken.None);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);

        // The manual live gate's shape: someone sets the child's stamp back on its form, minutes after the repair.
        world.OutOfBand("sprk_todo", Todo, "sprk_regardingmatter", "sprk_matter", MatterA);

        var next = await Job(world, idempotency: idempotency).ExecuteAsync(Context(), CancellationToken.None);

        next.Success.Should().BeTrue(next.ErrorMessage);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB,
            "the same repair, needed again, is a new staleness — repaired within one cycle, not after the marker expires");
        Counts(next).GetProperty("alreadyApplied").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "Task 156: the scan asks for every row filed under ANY source column, plus the F-051-6 orphans (no source, a pair type, a stamp)")]
    public void ScanFetchXml_CoversEverySourceColumnAndTheOrphans()
    {
        var sources = CoreAncestorResolver.StampSourceColumns["sprk_todo"].Select(s => s.Column).ToArray();
        string[] stamps = ["sprk_regardingmatter", "sprk_regardingproject"];

        var fetch = System.Xml.Linq.XDocument.Parse(CoreAncestorStampReconciliationJob.BuildScanFetchXml(
            "sprk_todo", ["sprk_regardingmatter"], sources, stamps, hasPairType: true, page: 1, pagingCookie: null));
        var top = fetch.Root!.Element("entity")!.Element("filter")!;

        top.Attribute("type")!.Value.Should().Be("or");
        var filedUnder = top.Elements("filter").First();
        filedUnder.Elements("condition").Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value))
            .Should().BeEquivalentTo(sources.Select(s => (s, "not-null")), "a row filed under ANY source is a candidate");

        var orphan = top.Elements("filter").Last();
        orphan.Attribute("type")!.Value.Should().Be("and");
        orphan.Elements("condition").Should().Contain(c =>
            c.Attribute("attribute")!.Value == "sprk_regardingrecordtype" && c.Attribute("operator")!.Value == "not-null");
        orphan.Elements("condition").Where(c => c.Attribute("operator")!.Value == "null")
            .Select(c => c.Attribute("attribute")!.Value).Should().BeEquivalentTo(sources);

        var noPairType = System.Xml.Linq.XDocument.Parse(CoreAncestorStampReconciliationJob.BuildScanFetchXml(
            "sprk_todo", ["sprk_regardingmatter"], sources, stamps, hasPairType: false, page: 1, pagingCookie: null));
        noPairType.Root!.Element("entity")!.Element("filter")!.Elements("filter").Should().ContainSingle(
            "a table without the pair's type column has no orphan clause (the column would fault the scan)");
    }

    private static CoreAncestorStampReconciliationJob Job(
        StampWorld world, string? writesEnabled = null, IIdempotencyService? idempotency = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(world.Service);
        services.AddSingleton(idempotency ?? new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<IdempotencyService>.Instance));

        var settings = new Dictionary<string, string?>();
        if (writesEnabled is not null)
        {
            settings[CoreAncestorStampReconciliationJob.WritesEnabledConfigKey] = writesEnabled;
        }

        return new CoreAncestorStampReconciliationJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            world.Resolver,
            world.Restamper,
            TimeProvider.System,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<CoreAncestorStampReconciliationJob>.Instance);
    }

    private static JobRunContext Context(int attempt = 1, Guid? runId = null)
        => new(runId ?? Guid.NewGuid(), "test-correlation", JobRunTrigger.Scheduled, new Dictionary<string, object>(), attempt);

    private static JsonElement Counts(JobRunResult result)
        => JsonDocument.Parse(result.ResultJson!).RootElement;
}
