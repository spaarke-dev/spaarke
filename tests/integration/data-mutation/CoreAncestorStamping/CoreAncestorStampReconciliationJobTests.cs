using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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
    private static readonly Guid MatterTypeRef = Guid.Parse("0a0a0a0a-0000-0000-0000-00000000000a");
    private static readonly Guid InvoiceTypeRef = Guid.Parse("0f0f0f0f-0000-0000-0000-00000000000f");

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

    [Fact(DisplayName = "Task 156 (F-051-6): the job clears a copy orphaned by a regarding CLEARED on the form (a TYPED pair — the shape for an intermediate that has a sprk_recordtype_ref row)")]
    public async Task OrphanedCopy_IsClearedByTheJob()
    {
        // An INVOICE has a sprk_recordtype_ref row live (a communication does not — that shape is the untyped test below),
        // so the regarding writers record its type with the pair; its root is its typed sprk_matter.
        var invoice = Guid.Parse("15600000-0000-0000-0000-000000000f01");
        var world = new StampWorld()
            .RecordType(InvoiceTypeRef, "sprk_invoice")
            .Row("sprk_invoice", invoice, [("sprk_matter", "sprk_matter", MatterA)])
            .Row("sprk_todo", Todo, [("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: invoice.ToString(), pairType: InvoiceTypeRef);

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

    [Fact(DisplayName = "Task 156: the scan asks for every row filed under ANY source column, plus ONLY the F-051-6 candidates (no source, a stamp, and a pair TYPE naming an intermediate — or a pair id with no type)")]
    public void ScanFetchXml_CoversEverySourceColumnAndOnlyTheOrphanCandidates()
    {
        var sources = CoreAncestorResolver.StampSourceColumns["sprk_todo"].Select(s => s.Column).ToArray();
        string[] stamps = ["sprk_regardingmatter", "sprk_regardingproject"];
        var orphans = new CoreAncestorStampReconciliationJob.OrphanClause([InvoiceTypeRef], Untyped: true);

        var fetch = System.Xml.Linq.XDocument.Parse(CoreAncestorStampReconciliationJob.BuildScanFetchXml(
            "sprk_todo", ["sprk_regardingmatter"], sources, stamps, orphans, after: null, pageSize: 1000, page: 1,
            pagingCookie: null));
        var top = fetch.Root!.Element("entity")!.Element("filter")!;

        top.Attribute("type")!.Value.Should().Be("or");
        var clauses = top.Elements("filter").ToList();
        clauses.Should().HaveCount(3);
        clauses[0].Elements("condition").Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value))
            .Should().BeEquivalentTo(sources.Select(s => (s, "not-null")), "a row filed under ANY source is a candidate");

        foreach (var orphan in clauses.Skip(1))
        {
            orphan.Attribute("type")!.Value.Should().Be("and");
            orphan.Elements("condition").Where(c => c.Attribute("operator")!.Value == "null"
                    && c.Attribute("attribute")!.Value != "sprk_regardingrecordtype")
                .Select(c => c.Attribute("attribute")!.Value).Should().BeEquivalentTo(sources);
        }

        var typed = clauses[1].Elements("condition").Single(c => c.Attribute("attribute")!.Value == "sprk_regardingrecordtype");
        typed.Attribute("operator")!.Value.Should().Be("in",
            "a pair TYPE is a candidate only when it names an intermediate — never 'any type' (verifier round 1 item 4)");
        typed.Elements("value").Select(v => Guid.Parse(v.Value)).Should().Equal(InvoiceTypeRef);

        clauses[2].Elements("condition").Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value))
            .Should().Contain([("sprk_regardingrecordid", "not-null"), ("sprk_regardingrecordtype", "null")],
                "an untyped pair (communication / agreement have no sprk_recordtype_ref row) is the other candidate");

        var none = System.Xml.Linq.XDocument.Parse(CoreAncestorStampReconciliationJob.BuildScanFetchXml(
            "sprk_todo", ["sprk_regardingmatter"], sources, stamps, CoreAncestorStampReconciliationJob.OrphanClause.None,
            after: null, pageSize: 1000, page: 1, pagingCookie: null));
        none.Root!.Element("entity")!.Element("filter")!.Elements("filter").Should().ContainSingle(
            "a table without the pair's columns has no orphan clause (the column would fault the scan)");
    }

    // ---------------------------------------------------------------------------------------------
    // Verifier round 1 item 4: the scan reads the rows that can hold a copy — not every regarding-filed row
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156 (verifier round 1 item 4): rows a regarding builder filed DIRECTLY to a matter (pair + type naming it) are never read by the scan — it scales with the copies, not the table")]
    public async Task RowsFiledDirectlyToARoot_AreNeverScanned()
    {
        var world = new StampWorld()
            .RecordType(MatterTypeRef, "sprk_matter")
            .RecordType(InvoiceTypeRef, "sprk_invoice")
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        for (var i = 0; i < 25; i++)
        {
            // The live shape of 24 of 50 to-dos: the regarding builder wrote the matter, its pair id and its pair TYPE.
            world.Row("sprk_todo", Guid.Parse($"15600000-0000-0000-0000-00000000d{i:D3}"),
                [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: MatterA.ToString(), pairType: MatterTypeRef);
        }

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Counts(run).GetProperty("scanned").GetInt32().Should().Be(1,
            "only the to-do filed under the communication carries a copy; the 25 filed directly are not candidates");
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);
    }

    // ---------------------------------------------------------------------------------------------
    // Verifier round 1 item 3: every fail-closed / no-silent-truncation guard of the job is pinned
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156 (verifier round 1, M1): the regarding types (sprk_recordtype_ref) UNREADABLE is a FAILED run — typed orphans were not looked for, never 'nothing orphaned' — and the other rows are still repaired")]
    public async Task RecordTypesUnreadable_IsAFailedRun()
    {
        var world = new StampWorld()
            .RecordType(CommunicationTypeRef, "sprk_communication")
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString())
            .FailScan("sprk_recordtype_ref");

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeFalse("an unread type table is not a clean F-051-6 scan");
        run.ErrorMessage.Should().Contain("sprk_recordtype_ref");
        Counts(run).GetProperty("status").GetString().Should().Be(CoreAncestorStampReconciliationJob.StatusError);
        Counts(run).GetProperty("scanFailures").GetInt32().Should().Be(1);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB, "the stale copy is still repaired");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1, M2): a source whose root cannot be derived leaves its child UNVERIFIED — a PARTIAL run (Success false), never ok, and nothing is written for it")]
    public async Task UnverifiableSource_IsAPartialRun()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString())
            .FailRead("sprk_communication", Communication);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeFalse("a row the job could not verify is not a verified row");
        Counts(run).GetProperty("status").GetString().Should().Be(CoreAncestorStampReconciliationJob.StatusPartial);
        Counts(run).GetProperty("unverified").GetInt32().Should().Be(1);
        world.Patches.Should().BeEmpty("an unknown root is never guessed");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1, M11 + item 4): a scan stopped by the PAGE bound is a PARTIAL run — and the NEXT run continues where it stopped, so no row is left unchecked forever")]
    public async Task PageBound_IsAPartialRun_AndTheNextRunContinuesWhereItStopped()
    {
        var todos = Enumerable.Range(1, 3).Select(i => Guid.Parse($"15600000-0000-0000-0000-00000000f00{i}")).ToArray();
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)]);
        foreach (var todo in todos)
        {
            world.Row("sprk_todo", todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        }

        var cursors = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        var first = await Job(world, cursors: cursors, pageSize: 1, maxPages: 2).ExecuteAsync(Context(), CancellationToken.None);

        first.Success.Should().BeFalse("two of three candidate rows were checked — never reported as a clean run");
        Counts(first).GetProperty("status").GetString().Should().Be(CoreAncestorStampReconciliationJob.StatusPartial);
        Counts(first).GetProperty("truncated").GetBoolean().Should().BeTrue();
        Counts(first).GetProperty("scanned").GetInt32().Should().Be(2);
        world.Lookup("sprk_todo", todos[2], "sprk_regardingmatter").Should().Be(MatterA, "past the page bound — not yet checked");

        var second = await Job(world, cursors: cursors, pageSize: 1, maxPages: 2).ExecuteAsync(Context(), CancellationToken.None);

        Counts(second).GetProperty("scanned").GetInt32().Should().Be(1, "it continued AFTER the last row the first run read");
        Counts(second).GetProperty("resumed").GetBoolean().Should().BeTrue();
        Counts(second).GetProperty("status").GetString().Should().Be(CoreAncestorStampReconciliationJob.StatusPartial,
            "a run that only checked the tail is not a full pass");
        world.Lookup("sprk_todo", todos[2], "sprk_regardingmatter").Should().Be(MatterB,
            "the row beyond the first run's page bound is repaired by the next run — not left stale forever");
    }

    // ---------------------------------------------------------------------------------------------
    // Verifier round 1 item 5: F-051-6 where the regarding has NO sprk_recordtype_ref row (communication, agreement)
    // ---------------------------------------------------------------------------------------------

    [Theory(DisplayName = "Task 156 (verifier round 1 item 5): a copy orphaned by a form clear of a COMMUNICATION or an AGREEMENT — whose pair has no type, because neither has a sprk_recordtype_ref row live — is found by its pair id and cleared")]
    [InlineData("sprk_todo", "sprk_communication")]
    [InlineData("sprk_event", "sprk_communication")]
    [InlineData("sprk_analysis", "sprk_communication")]
    [InlineData("sprk_todo", "sprk_agreement")]
    [InlineData("sprk_event", "sprk_agreement")]
    public async Task OrphanedCopy_WithAnUntypedPair_IsClearedByTheJob(string child, string intermediate)
    {
        // The live record types, read-only 2026-10-02: no communication, no agreement (and no invoice ref here either —
        // only the types that DO exist matter for the typed clause).
        var source = Guid.Parse("15600000-0000-0000-0000-00000000aa01");
        var world = new StampWorld()
            .RecordType(MatterTypeRef, "sprk_matter")
            .Row(intermediate, source, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row(child, Todo, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: source.ToString());

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup(child, Todo, "sprk_regardingmatter").Should().BeNull(
            $"the {child} shows no regarding any more, so it must stop inheriting the {intermediate}'s matter (fail closed)");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 5): an untyped pair naming a record that NO LONGER EXISTS is left alone and counted — never a partial run that repeats every 5 minutes")]
    public async Task OrphanCandidate_WhoseRecordIsGone_IsLeftAlone_AndTheRunIsOk()
    {
        var gone = Guid.Parse("15600000-0000-0000-0000-00000000aa02");
        var world = new StampWorld()
            .Row("sprk_todo", Todo, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: gone.ToString());

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Patches.Should().BeEmpty("a copy whose record is gone cannot be shown to be its copy (interpretation xvi)");
        Counts(run).GetProperty("orphanSourceGone").GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 5): an untyped pair naming the root the row CARRIES is a direct link — never read as an orphan, never cleared")]
    public async Task UntypedPairNamingItsOwnRoot_IsNeverAnOrphan()
    {
        var world = new StampWorld()
            .Row("sprk_matter", MatterA, [])
            .Row("sprk_todo", Todo, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: MatterA.ToString());

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Patches.Should().BeEmpty();
        Counts(run).GetProperty("orphanSourceGone").GetInt32().Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // Verifier round 3 (AC7): the job's guards that seeds K8, K7 and K5 removed with the suite still green
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156 (verifier round 3, K8): a repair whose PATCH FAILS is a PARTIAL run (Success false) — the stale copy left behind is never reported as a clean run")]
    public async Task RepairWhosePatchFails_IsAPartialRun_NeverOk()
    {
        // The write never heals: the job's own repair fails, so the stale copy is still there when the run ends.
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString())
            .FailWrite("sprk_todo", Todo);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        world.PatchesTo("sprk_todo", Todo).Should().ContainSingle("the repair was attempted");
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterA, "the PATCH was refused");
        run.Success.Should().BeFalse("a stale copy the run could not repair is not a reconciled table");
        Counts(run).GetProperty("status").GetString().Should().Be(CoreAncestorStampReconciliationJob.StatusPartial);
        Counts(run).GetProperty("repairFailures").GetInt32().Should().Be(1);
        Counts(run).GetProperty("repaired").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "Task 156 (verifier round 3, K7): an untyped pair whose id is found in TWO intermediate tables is UNVERIFIED — never guessed, nothing cleared, a PARTIAL run")]
    public async Task UntypedPairFoundInTwoIntermediateTables_IsUnverified_NothingCleared()
    {
        // Both candidates name matter A, so a guess at either one would "match" the copy and clear it.
        var shared = Guid.Parse("15600000-0000-0000-0000-00000000aa03");
        var world = new StampWorld()
            .Row("sprk_communication", shared, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_agreement", shared, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", Todo, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: shared.ToString());

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        world.Patches.Should().BeEmpty("which record's regarding was cleared is not known — never a guess");
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterA);
        run.Success.Should().BeFalse("a row the job could not decide is not a verified row");
        Counts(run).GetProperty("status").GetString().Should().Be(CoreAncestorStampReconciliationJob.StatusPartial);
        Counts(run).GetProperty("unverified").GetInt32().Should().Be(1);
    }

    [Fact(DisplayName = "Task 156 (verifier round 3, K5): in an org whose child table LACKS a stamp column, the job never plans or writes that column — and a row whose only difference is there is not stale")]
    public async Task StampColumnTheChildTableLacks_IsNeverPlannedOrWritten()
    {
        // Todo: its copy of the communication's matter is stale (A, the communication is now under B) — repaired, matter
        // only. OnlyDifferenceTodo: its matter copy is fresh; the communication's work assignment has no column to land on.
        var onlyDifferenceTodo = Guid.Parse("15600000-0000-0000-0000-000000000106");
        var otherCommunication = Guid.Parse("15600000-0000-0000-0000-000000000c02");
        var world = new StampWorld()
            .WithoutColumn("sprk_todo", "sprk_regardingworkassignment")
            .Row("sprk_communication", Communication,
                [("sprk_regardingmatter", "sprk_matter", MatterB), ("sprk_regardingworkassignment", "sprk_workassignment", Guid.Parse("d0000000-0000-0000-0000-00000000000d"))])
            .Row("sprk_communication", otherCommunication,
                [("sprk_regardingmatter", "sprk_matter", MatterA), ("sprk_regardingworkassignment", "sprk_workassignment", Guid.Parse("d0000000-0000-0000-0000-00000000000e"))])
            .Row("sprk_todo", Todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString())
            .Row("sprk_todo", onlyDifferenceTodo,
                [("sprk_regardingcommunication", "sprk_communication", otherCommunication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: otherCommunication.ToString());

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Counts(run).GetProperty("stale").GetInt32().Should().Be(1, "only the matter copy differs on a column the table has");
        world.PatchesTo("sprk_todo", onlyDifferenceTodo).Should().BeEmpty();
        world.PatchesTo("sprk_todo", Todo).Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo(["sprk_regardingmatter"]);
        world.Lookup("sprk_todo", Todo, "sprk_regardingmatter").Should().Be(MatterB);
    }

    // ---------------------------------------------------------------------------------------------
    // Owner decisions round 8 item 2 (F-051-6): TaskActionCore writes the ADR-024 regarding pair, so a form clear of a task
    // it created is found by the job. Before, it wrote the typed lookup alone and the cleared copy was undetectable.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The task create seam (the AI / communication "create task" follow-ups) writing into this world: the real
    /// <see cref="Sprk.Bff.Api.Services.Ai.Nodes.TaskActionCore"/> over the world's rows and resolver; the record-type
    /// lookup answers <paramref name="recordTypes"/> (logical name → <c>sprk_recordtype_ref</c> id) and nothing else.
    /// </summary>
    private static Sprk.Bff.Api.Services.Ai.Nodes.TaskActionCore TaskCore(
        StampWorld world, IReadOnlyDictionary<string, Guid>? recordTypes = null)
    {
        var lookup = new Mock<ICommunicationDataverseService>();
        lookup.Setup(l => l.QueryRecordTypeRefAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string logicalName, CancellationToken _) => Task.FromResult<Microsoft.Xrm.Sdk.Entity?>(
                recordTypes is not null && recordTypes.TryGetValue(logicalName, out var refId)
                    ? new Microsoft.Xrm.Sdk.Entity("sprk_recordtype_ref", refId) { ["sprk_recorddisplayname"] = logicalName }
                    : null));

        return new Sprk.Bff.Api.Services.Ai.Nodes.TaskActionCore(
            world.Service,
            world.Resolver,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(),
            lookup.Object,
            NullLogger.Instance);
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 2, F-051-6): a task TaskActionCore created under a COMMUNICATION, whose communication a form later clears, is found by its untyped pair and its orphaned copy cleared")]
    public async Task TaskActionCoreTask_UnderACommunication_FormClear_IsFoundAndClearedByTheJob()
    {
        // Live 2026-10-02 (read-only): sprk_recordtype_ref has no row for sprk_communication, so the pair is untyped.
        var world = new StampWorld()
            .RecordType(MatterTypeRef, "sprk_matter")
            .Row("sprk_matter", MatterA, [])
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)]);

        var task = await TaskCore(world).CreateAsync(
            new Sprk.Bff.Api.Services.Ai.Nodes.TaskActionInput("Follow up", null, null, Communication, "sprk_communication", null),
            CancellationToken.None);
        task.Should().NotBe(Guid.Empty);
        world.Lookup("sprk_event", task, "sprk_regardingmatter").Should().Be(MatterA, "the task copies its communication's matter");

        // A native form clears the task's communication: no BFF path runs, and the copy of matter A stays behind.
        world.OutOfBand("sprk_event", task, "sprk_regardingcommunication", "sprk_communication", null);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_event", task, "sprk_regardingmatter").Should().BeNull(
            "the task's pair still names the communication, so the job knows the matter was its copy and clears it — the task "
            + "stops inheriting matter A's access (fail closed). Without the pair the copy reads as a direct link and stays.");
    }

    [Fact(DisplayName = "Task 156 (owner round 8 item 2, F-051-6): a task TaskActionCore created under an INVOICE carries the TYPED pair (a sprk_recordtype_ref row exists); a later form clear of its invoice is found by the job and the orphaned copy cleared")]
    public async Task TaskActionCoreTask_UnderAnInvoice_FormClear_IsFoundByTheTypedPair()
    {
        var invoice = Guid.Parse("15600000-0000-0000-0000-00000000aa21");
        var world = new StampWorld()
            .RecordType(MatterTypeRef, "sprk_matter")
            .RecordType(InvoiceTypeRef, "sprk_invoice")
            .Row("sprk_matter", MatterA, [])
            .Row("sprk_invoice", invoice, [("sprk_matter", "sprk_matter", MatterA)]);

        var task = await TaskCore(world, new Dictionary<string, Guid> { ["sprk_invoice"] = InvoiceTypeRef }).CreateAsync(
            new Sprk.Bff.Api.Services.Ai.Nodes.TaskActionInput("Chase invoice", null, null, invoice, "sprk_invoice", null),
            CancellationToken.None);
        task.Should().NotBe(Guid.Empty);
        var pairType = world.RowOf("sprk_event", task)
            .GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(CoreAncestorResolver.RegardingRecordTypeColumn);
        pairType.Should().NotBeNull("a sprk_recordtype_ref row exists for an invoice, so the pair is typed");
        pairType!.Id.Should().Be(InvoiceTypeRef);

        world.OutOfBand("sprk_event", task, "sprk_regardinginvoice", "sprk_invoice", null);

        var run = await Job(world).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        world.Lookup("sprk_event", task, "sprk_regardingmatter").Should().BeNull(
            "the typed pair names an invoice the row no longer carries, and the copy equals that invoice's matter");
    }

    private static CoreAncestorStampReconciliationJob Job(
        StampWorld world,
        string? writesEnabled = null,
        IIdempotencyService? idempotency = null,
        IDistributedCache? cursors = null,
        int? pageSize = null,
        int? maxPages = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(world.Service);
        services.AddSingleton(idempotency ?? new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<IdempotencyService>.Instance));
        services.AddSingleton(cursors ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

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
            NullLogger<CoreAncestorStampReconciliationJob>.Instance)
        {
            ScanPageSize = pageSize ?? CoreAncestorStampReconciliationJob.PageSize,
            ScanMaxPages = maxPages ?? CoreAncestorStampReconciliationJob.MaxPages,
        };
    }

    private static JobRunContext Context(int attempt = 1, Guid? runId = null)
        => new(runId ?? Guid.NewGuid(), "test-correlation", JobRunTrigger.Scheduled, new Dictionary<string, object>(), attempt);

    private static JsonElement Counts(JobRunResult result)
        => JsonDocument.Parse(result.ResultJson!).RootElement;
}
