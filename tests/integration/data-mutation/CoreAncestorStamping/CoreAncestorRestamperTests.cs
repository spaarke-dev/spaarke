using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// unified-access-control-r2 task 156 (owner round 4 item 5 — option b): when a record others are filed under is
/// re-filed, every child carrying a COPY of its root is re-stamped in the same operation — and nothing else is written.
/// </summary>
/// <remarks>
/// <para><b>Why the copy matters.</b> A to-do filed under a communication carries the communication's matter in its own
/// <c>sprk_regardingmatter</c> (FR-26). That copy decides who can see the to-do (one-hop inheritance) and where its file
/// bytes go (task 155). Re-file the communication from ordinary matter A to SECURE matter B without re-stamping and the
/// to-do still says "A": an over-grant to A's principals, and — for a resolver that trusted it — a leak into A's shared
/// container.</para>
/// <para>The assertions are on the PATCHes the real <see cref="CoreAncestorRestamper"/> sends to an in-memory Dataverse
/// (<see cref="StampWorld"/>): what was written, to which row, and — as load-bearing — what was NOT written.</para>
/// </remarks>
public class CoreAncestorRestamperTests
{
    private static readonly Guid MatterA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid ProjectP = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    private static readonly Guid WorkAssignment = Guid.Parse("d0000000-0000-0000-0000-00000000000d");
    private static readonly Guid Communication = Guid.Parse("15600000-0000-0000-0000-000000000c01");
    private static readonly Guid OtherCommunication = Guid.Parse("15600000-0000-0000-0000-000000000c02");
    private static readonly Guid Event = Guid.Parse("15600000-0000-0000-0000-000000000e01");
    private static readonly Guid OtherEvent = Guid.Parse("15600000-0000-0000-0000-000000000e02");
    private static readonly Guid Document = Guid.Parse("15600000-0000-0000-0000-000000000d01");
    private static readonly Guid Invoice = Guid.Parse("15600000-0000-0000-0000-000000000f01");
    private static readonly Guid TodoUnderComm = Guid.Parse("15600000-0000-0000-0000-000000000101");
    private static readonly Guid EventUnderComm = Guid.Parse("15600000-0000-0000-0000-000000000102");
    private static readonly Guid AnalysisUnderComm = Guid.Parse("15600000-0000-0000-0000-000000000103");
    private static readonly Guid TodoUnderEvent = Guid.Parse("15600000-0000-0000-0000-000000000104");
    private static readonly Guid CarrierTodo = Guid.Parse("15600000-0000-0000-0000-000000000105");
    private static readonly Guid TodoCopyingAnotherRecord = Guid.Parse("15600000-0000-0000-0000-000000000106");
    private static readonly Guid TodoUnderDocument = Guid.Parse("15600000-0000-0000-0000-000000000107");
    private static readonly Guid CommunicationTypeRef = Guid.Parse("0c0c0c0c-0000-0000-0000-00000000000c");

    // ---------------------------------------------------------------------------------------------
    // The cascade: every copy of the re-filed record's root, in the same operation
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156: re-filing a communication (matter A → B) re-stamps its to-do, event and analysis in the same operation")]
    public async Task RefiledCommunication_RestampsEveryStampedChild()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA);

        var report = await world.Restamper.AfterWriteAsync(
            "sprk_communication", Communication, ["sprk_regardingmatter"]);

        report.Complete.Should().BeTrue();
        foreach (var (entity, id) in new[] { ("sprk_todo", TodoUnderComm), ("sprk_event", EventUnderComm), ("sprk_analysis", AnalysisUnderComm) })
        {
            world.PatchesTo(entity, id).Should().ContainSingle(
                    $"{entity} is filed under the communication, so its copy of the communication's matter must follow it")
                .Which.Fields.Should().ContainKey("sprk_regardingmatter")
                .WhoseValue.Should().BeOfType<EntityReference>().Which.Id.Should().Be(MatterB);
            world.Lookup(entity, id, "sprk_regardingmatter").Should().Be(MatterB);
        }

        world.PatchesTo("sprk_communication", Communication).Should().BeEmpty(
            "the cascade writes the CHILDREN only — the communication's own update is the caller's");
    }

    [Fact(DisplayName = "Task 156: the cascade is TRANSITIVE — an event under the communication is re-stamped, and then the to-do under that event")]
    public async Task Cascade_IsTransitive()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA)
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", EventUnderComm), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: EventUnderComm.ToString());

        await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        world.Lookup("sprk_event", EventUnderComm, "sprk_regardingmatter").Should().Be(MatterB);
        world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterB,
            "the to-do's copy comes from the event, whose copy just changed");
    }

    [Fact(DisplayName = "Task 156: the Office CARRIER to-do — a direct, user-chosen matter plus the email it came from — is NEVER re-stamped")]
    public async Task CarrierTodo_DirectLinkIsNeverChanged()
    {
        // The Office add-in shape (OfficeService.CreateTodoAsync): the record regarding is the matter the user chose (the
        // pair names it), and sprk_regardingcommunication is only the email the to-do was created from.
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA)
            .Row("sprk_todo", CarrierTodo,
                [("sprk_regardingmatter", "sprk_matter", MatterA), ("sprk_regardingcommunication", "sprk_communication", Communication)],
                pairId: MatterA.ToString());

        var report = await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        world.PatchesTo("sprk_todo", CarrierTodo).Should().BeEmpty("the user chose matter A; it is not a copy");
        world.Lookup("sprk_todo", CarrierTodo, "sprk_regardingmatter").Should().Be(MatterA);
        report.Skipped.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "Task 156: a to-do whose copy comes from ANOTHER record (the pair names its event) is not touched by the communication's cascade")]
    public async Task ChildWhoseCopyComesFromAnotherRecord_IsNotTouched()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA)
            .Row("sprk_event", OtherEvent, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoCopyingAnotherRecord,
            [
                ("sprk_regardingevent", "sprk_event", OtherEvent), ("sprk_regardingcommunication", "sprk_communication", Communication),
                ("sprk_regardingmatter", "sprk_matter", MatterA),
            ], pairId: OtherEvent.ToString());

        await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        world.PatchesTo("sprk_todo", TodoCopyingAnotherRecord).Should().BeEmpty(
            "its copy is the event's matter; the communication is only a carrier on it");
    }

    [Fact(DisplayName = "Task 156: a root type the source cannot carry is never written (a communication filed under an invoice keeps its own work assignment)")]
    public async Task RootTypeTheSourceCannotCarry_IsNeverWritten()
    {
        var world = new StampWorld()
            .Row("sprk_invoice", Invoice, [("sprk_matter", "sprk_matter", MatterB)])
            .Row("sprk_communication", Communication,
            [
                ("sprk_regardinginvoice", "sprk_invoice", Invoice), ("sprk_regardingmatter", "sprk_matter", MatterA),
                ("sprk_regardingworkassignment", "sprk_workassignment", WorkAssignment),
            ], pairId: Invoice.ToString());

        await world.Restamper.RestampChildAsync("sprk_communication", Communication);

        var patch = world.PatchesTo("sprk_communication", Communication).Should().ContainSingle().Subject;
        patch.Fields.Keys.Should().BeEquivalentTo(["sprk_regardingmatter"],
            "an invoice names a project or matter — never a work assignment, so that column is not a copy of it");
        world.Lookup("sprk_communication", Communication, "sprk_regardingworkassignment").Should().Be(WorkAssignment);
    }

    [Fact(DisplayName = "Task 156: re-filing a record to NO root CLEARS the copies (they would otherwise keep granting the old root's access)")]
    public async Task RefiledToNoRoot_ClearsTheCopy()
    {
        var world = CommunicationWithChildren(communicationMatter: null, childCopy: MatterA);

        await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        world.PatchesTo("sprk_todo", TodoUnderComm).Should().ContainSingle()
            .Which.Fields["sprk_regardingmatter"].Should().Be(DBNull.Value);
        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingmatter").Should().BeNull();
    }

    [Fact(DisplayName = "Task 156: a document re-filed through its typed sprk_matter (Web API bind shape) re-stamps the to-do under it")]
    public async Task RefiledDocument_ByWebApiBindKey_RestampsTheTodo()
    {
        var world = new StampWorld()
            .Row("sprk_document", Document, [("sprk_matter", "sprk_matter", MatterB)])
            .Row("sprk_todo", TodoUnderDocument,
                [("sprk_regardingdocument", "sprk_document", Document), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Document.ToString());

        await world.Restamper.AfterWriteAsync("sprk_document", Document, ["sprk_Matter@odata.bind"]);

        world.Lookup("sprk_todo", TodoUnderDocument, "sprk_regardingmatter").Should().Be(MatterB,
            "a document names its root through typed sprk_matter — the copy follows it (task 156 extends derivation to it)");
    }

    [Fact(DisplayName = "Task 156: a re-filed CHILD (its own source changed by a generic write) is re-stamped from its NEW source")]
    public async Task RefiledChild_IsRestampedFromItsNewSource()
    {
        // The write re-files the to-do itself (its typed source and its pair), as a regarding builder writes them —
        // leaving its old copy of matter A behind.
        var world = CommunicationWithChildren(communicationMatter: MatterA, childCopy: MatterA)
            .Row("sprk_communication", OtherCommunication, [("sprk_regardingproject", "sprk_project", ProjectP)])
            .Row("sprk_todo", TodoUnderComm,
                [("sprk_regardingcommunication", "sprk_communication", OtherCommunication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: OtherCommunication.ToString());

        await world.Restamper.AfterWriteAsync(
            "sprk_todo", TodoUnderComm, ["sprk_regardingcommunication", "sprk_regardingrecordid"]);

        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingproject").Should().Be(ProjectP);
        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingmatter").Should().BeNull(
            "the new communication names a project only — the old matter copy is cleared");
    }

    [Fact(DisplayName = "Task 156: a write that cannot move a stamp reads and writes NOTHING")]
    public async Task WriteThatCannotMoveAStamp_CostsNothing()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA);

        var report = await world.Restamper.AfterWriteAsync(
            "sprk_communication", Communication, ["sprk_triagesummary", "statuscode"]);

        report.Examined.Should().Be(0);
        world.Patches.Should().BeEmpty();
        CoreAncestorRestamper.WriteCanMoveAStamp("sprk_communication", ["sprk_triagesummary"]).Should().BeFalse();
        CoreAncestorRestamper.WriteCanMoveAStamp("sprk_communication", ["_sprk_regardingmatter_value"]).Should().BeTrue();
        CoreAncestorRestamper.WriteCanMoveAStamp("sprk_matter", ["sprk_regardingproject"]).Should().BeFalse(
            "a matter is a root: nothing copies a matter's own links");
    }

    // ---------------------------------------------------------------------------------------------
    // Failure: reported, never thrown; the caller's write stands
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156: a child PATCH that FAILS is reported, the other children are still re-stamped, and nothing is thrown")]
    public async Task ChildPatchFailure_IsReported_AndDoesNotStopTheCascade()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA)
            .FailWrite("sprk_event", EventUnderComm);

        var act = async () => await world.Restamper.AfterWriteAsync(
            "sprk_communication", Communication, ["sprk_regardingmatter"]);

        var report = (await act.Should().NotThrowAsync(
            "the communication's own re-file already happened and must stand")).Subject;

        report.Complete.Should().BeFalse();
        report.Failures.Should().ContainSingle().Which.Id.Should().Be(EventUnderComm);
        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingmatter").Should().Be(MatterB);
        world.Lookup("sprk_event", EventUnderComm, "sprk_regardingmatter").Should().Be(MatterA, "its PATCH failed");
    }

    [Fact(DisplayName = "Task 156: when the re-filed record's root cannot be derived the cascade writes nothing and reports it")]
    public async Task UnderivableRoot_WritesNothing_AndIsReported()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA)
            .FailRead("sprk_communication", Communication);

        var report = await world.Restamper.RestampChildrenOfAsync("sprk_communication", Communication);

        world.Patches.Should().BeEmpty("an unknown root is never guessed into a copy");
        report.Complete.Should().BeFalse();
    }

    [Fact(DisplayName = "Task 156: a cycle (an event filed under an event filed under the first) terminates")]
    public async Task Cycle_Terminates()
    {
        var world = new StampWorld()
            .Row("sprk_event", Event,
                [("sprk_regardingevent", "sprk_event", OtherEvent), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: OtherEvent.ToString())
            .Row("sprk_event", OtherEvent,
                [("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", MatterB)],
                pairId: Event.ToString());

        var report = await world.Restamper.RestampChildrenOfAsync("sprk_event", Event);

        report.Truncated.Should().BeFalse("the second visit finds the copy already equal — the cycle converges");
        world.Lookup("sprk_event", OtherEvent, "sprk_regardingmatter").Should().Be(MatterA);
    }

    [Fact(DisplayName = "Task 156: a chain deeper than the cascade bound is reported TRUNCATED — the job finishes it, never a silent prefix")]
    public async Task ChainPastTheDepthBound_IsReportedTruncated()
    {
        // E0 ← E1 ← … ← E5, each event filed under the one before it, every copy stale.
        var chain = Enumerable.Range(0, CoreAncestorRestamper.MaxCascadeDepth + 2)
            .Select(i => Guid.Parse($"15600000-0000-0000-0000-0000000e{i:D4}")).ToArray();
        var world = new StampWorld().Row("sprk_event", chain[0], [("sprk_regardingmatter", "sprk_matter", MatterB)]);
        for (var i = 1; i < chain.Length; i++)
        {
            world.Row("sprk_event", chain[i],
                [("sprk_regardingevent", "sprk_event", chain[i - 1]), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: chain[i - 1].ToString());
        }

        var report = await world.Restamper.RestampChildrenOfAsync("sprk_event", chain[0]);

        report.Truncated.Should().BeTrue();
        report.Complete.Should().BeFalse();
        world.Lookup("sprk_event", chain[^1], "sprk_regardingmatter").Should().Be(MatterA, "past the bound — left for the job");
        world.Lookup("sprk_event", chain[1], "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156 (verifier round 1, M12): more children under ONE intermediate than the per-source bound → the cascade is reported TRUNCATED (never a silent prefix) and the rest is left for the job")]
    public async Task ChildrenPastThePerSourceBound_AreReportedTruncated()
    {
        // Escalation trigger 2's mitigation: the bound (5000 live) is lowered so it is reachable — 5 to-dos, pages of 2,
        // bound 3: two pages are listed (4 rows) and the cascade stops with more to come.
        var todos = Enumerable.Range(1, 5).Select(i => Guid.Parse($"15600000-0000-0000-0000-00000000b0{i:D2}")).ToArray();
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)]);
        foreach (var todo in todos)
        {
            world.Row("sprk_todo", todo,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());
        }

        var report = await world.RestamperWith(childPageSize: 2, childrenPerSourceBound: 3)
            .RestampChildrenOfAsync("sprk_communication", Communication);

        report.Truncated.Should().BeTrue("the bound stopped the listing with rows still to come");
        report.Complete.Should().BeFalse("a truncated cascade is never reported complete");
        todos.Count(t => world.Lookup("sprk_todo", t, "sprk_regardingmatter") == MatterA).Should().Be(1,
            "the row past the bound is left for the reconciliation job, and the report says so");
    }

    // ---------------------------------------------------------------------------------------------
    // Verifier round 1 item 10: a generic write to the COPY column of a record that is itself filed under another
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156 (verifier round 1 item 10): a generic write of sprk_regardingmatter on an EVENT filed under a communication re-derives the event from its source — the hand-written value is never cascaded to the event's children")]
    public async Task WriteToTheCopyColumnOfAFiledRecord_RederivesIt_AndNeverCascadesTheWrittenValue()
    {
        // Everything is fresh at matter B; then a field-mapping push / UpdateRecord writes matter A onto the event.
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterB)
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", EventUnderComm), ("sprk_regardingmatter", "sprk_matter", MatterB)],
                pairId: EventUnderComm.ToString())
            .OutOfBand("sprk_event", EventUnderComm, "sprk_regardingmatter", "sprk_matter", MatterA);

        var report = await world.Restamper.AfterWriteAsync("sprk_event", EventUnderComm, ["sprk_regardingmatter"]);

        report.Complete.Should().BeTrue();
        world.Lookup("sprk_event", EventUnderComm, "sprk_regardingmatter").Should().Be(MatterB,
            "the event is filed under the communication: its matter is the communication's (the job would revert it anyway)");
        world.PatchesTo("sprk_todo", TodoUnderEvent).Should().BeEmpty(
            "the to-do already equals the source's root — the hand-written matter A is never carried down (no churn)");
        world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 10): when the written record's own source CANNOT be read, nothing is cascaded from the hand-written value — the failure is reported and the job repairs both")]
    public async Task WriteToTheCopyColumn_WhenTheSourceIsUnreadable_CascadesNothing()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterB)
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", EventUnderComm), ("sprk_regardingmatter", "sprk_matter", MatterB)],
                pairId: EventUnderComm.ToString())
            .OutOfBand("sprk_event", EventUnderComm, "sprk_regardingmatter", "sprk_matter", MatterA)
            .FailRead("sprk_communication", Communication);

        var report = await world.Restamper.AfterWriteAsync("sprk_event", EventUnderComm, ["sprk_regardingmatter"]);

        report.Complete.Should().BeFalse();
        report.Failures.Should().Contain(f => f.Entity == "sprk_event" && f.Id == EventUnderComm);
        world.PatchesTo("sprk_todo", TodoUnderEvent).Should().BeEmpty(
            "the event's matter is not known to be its own, so it is never copied down");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 10): a root write on an event filed under NOTHING is its own direct link — cascaded to its children unchanged")]
    public async Task WriteToTheRootOfAnUnfiledRecord_IsCascadedAsWritten()
    {
        var world = new StampWorld()
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Event.ToString());

        await world.Restamper.AfterWriteAsync("sprk_event", Event, ["sprk_regardingmatter"]);

        world.PatchesTo("sprk_event", Event).Should().BeEmpty("the event's matter is its own — the write stands");
        world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156: two intermediates set and nothing saying which one the row is filed under — never written")]
    public async Task AmbiguousSource_IsNeverWritten()
    {
        var world = CommunicationWithChildren(communicationMatter: MatterB, childCopy: MatterA)
            .Row("sprk_event", OtherEvent, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoCopyingAnotherRecord,
            [
                ("sprk_regardingevent", "sprk_event", OtherEvent), ("sprk_regardingcommunication", "sprk_communication", Communication),
                ("sprk_regardingmatter", "sprk_matter", MatterA),
            ]);

        await world.Restamper.RestampChildAsync("sprk_todo", TodoCopyingAnotherRecord);

        world.PatchesTo("sprk_todo", TodoCopyingAnotherRecord).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // Task 051 F-051-6 — a source lookup CLEARED on a form leaves its copy behind
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156 (F-051-6): a to-do whose communication was CLEARED on its form has the orphaned copy removed — when it still equals that communication's root")]
    public async Task OrphanedCopy_MatchingTheClearedSource_IsCleared()
    {
        // A TYPED pair (a communication type row is seeded here; live has none — the untyped shape is the next test).
        var world = new StampWorld()
            .RecordType(CommunicationTypeRef, "sprk_communication")
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoUnderComm, [("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString(), pairType: CommunicationTypeRef);

        await world.Restamper.RestampChildAsync("sprk_todo", TodoUnderComm);

        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingmatter").Should().BeNull(
            "the to-do shows no regarding any more, so it must stop inheriting the old matter's access (fail closed)");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 5): the LIVE communication shape — the pair id with NO type (sprk_recordtype_ref has no communication row) — is still found by its id, and the orphaned copy cleared")]
    public async Task OrphanedCopy_WithAnUntypedPair_IsCleared()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoUnderComm, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: Communication.ToString());

        var report = await world.Restamper.RestampChildAsync("sprk_todo", TodoUnderComm);

        report.Complete.Should().BeTrue();
        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingmatter").Should().BeNull();
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 5): an untyped pair naming a record that NO LONGER EXISTS — the copy is left alone and the repair is not a failure (an enqueued re-stamp would otherwise retry it to poison)")]
    public async Task OrphanCandidate_WhoseRecordIsGone_IsLeftAlone_NotAFailure()
    {
        var gone = Guid.Parse("15600000-0000-0000-0000-00000000aa02");
        var world = new StampWorld()
            .Row("sprk_todo", TodoUnderComm, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: gone.ToString());

        var report = await world.Restamper.RestampChildAsync("sprk_todo", TodoUnderComm);

        report.Complete.Should().BeTrue("nothing is unreadable — the record is simply gone");
        world.Patches.Should().BeEmpty("a copy whose record is gone cannot be shown to be its copy (interpretation xvi)");
    }

    [Fact(DisplayName = "Task 156 (verifier round 1 item 5): the orphan path on the AFTER-WRITE route never clears a root the write just set (it may be the user's direct choice) — only the job does")]
    public async Task RootWriteOnAnOrphanShapedRow_IsNotClearedByTheAfterWritePath()
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_event", EventUnderComm, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: Communication.ToString());

        await world.Restamper.AfterWriteAsync("sprk_event", EventUnderComm, ["sprk_regardingmatter"]);

        world.PatchesTo("sprk_event", EventUnderComm).Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 156 (F-051-6): a stamp that does NOT equal the cleared communication's root may be a direct choice — it is left alone")]
    public async Task OrphanCandidate_NotMatchingTheClearedSource_IsLeftAlone()
    {
        var world = new StampWorld()
            .RecordType(CommunicationTypeRef, "sprk_communication")
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoUnderComm, [("sprk_regardingmatter", "sprk_matter", MatterB)],
                pairId: Communication.ToString(), pairType: CommunicationTypeRef);

        await world.Restamper.RestampChildAsync("sprk_todo", TodoUnderComm);

        world.Patches.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // Verifier round 3 (AC7): the two restamper guards seeds K7 and K5 removed with the suite still green
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 156 (verifier round 3, K7): an untyped pair whose id is found in TWO intermediate tables is never guessed — nothing is cleared, and the row is reported as a failure")]
    public async Task UntypedPairFoundInTwoIntermediateTables_IsNeverGuessed()
    {
        // The same id in a communication AND an agreement (both have no sprk_recordtype_ref row live, so the pair carries no
        // type). Both name matter A, so whichever one a guess picked, the copy would "match" and be cleared.
        var shared = Guid.Parse("15600000-0000-0000-0000-00000000aa03");
        var world = new StampWorld()
            .Row("sprk_communication", shared, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_agreement", shared, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoUnderComm, [("sprk_regardingmatter", "sprk_matter", MatterA)], pairId: shared.ToString());

        var report = await world.Restamper.RestampChildAsync("sprk_todo", TodoUnderComm);

        world.Patches.Should().BeEmpty("which record was cleared is not known, so the copy is not shown to be its copy");
        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingmatter").Should().Be(MatterA);
        report.Complete.Should().BeFalse("an undecidable row is reported, never passed as repaired");
        report.Failures.Should().ContainSingle().Which.Should().Match<RestampFailure>(f =>
            f.Entity == "sprk_todo" && f.Id == TodoUnderComm && f.Reason.Contains("more than one table"));
    }

    [Fact(DisplayName = "Task 156 (verifier round 3, K5): a stamp column the org's child table LACKS is never written by the cascade — the other copies are still refreshed")]
    public async Task StampColumnTheChildTableLacks_IsNeverWrittenByTheCascade()
    {
        // The communication was re-filed from matter A to a work assignment, in an org whose to-do has no
        // sprk_regardingworkassignment column. The to-do's stale matter copy is cleared; the work assignment cannot be
        // copied onto a column that does not exist (Dataverse would refuse the whole PATCH).
        var world = new StampWorld()
            .WithoutColumn("sprk_todo", "sprk_regardingworkassignment")
            .Row("sprk_communication", Communication, [("sprk_regardingworkassignment", "sprk_workassignment", WorkAssignment)])
            .Row("sprk_todo", TodoUnderComm,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString());

        var report = await world.Restamper.AfterWriteAsync(
            "sprk_communication", Communication, ["sprk_regardingworkassignment", "sprk_regardingmatter"]);

        report.Complete.Should().BeTrue();
        var patch = world.PatchesTo("sprk_todo", TodoUnderComm).Should().ContainSingle().Subject;
        patch.Fields.Keys.Should().BeEquivalentTo(["sprk_regardingmatter"],
            "only a column the table HAS may be planned — the work assignment column does not exist on this org's to-do");
        patch.Fields["sprk_regardingmatter"].Should().Be(DBNull.Value);
    }

    [Fact(DisplayName = "Task 156 (verifier round 3, K5): PlanStamp skips a root type whose stamp column the host table lacks")]
    public void PlanStamp_SkipsAStampColumnTheHostLacks()
    {
        var row = new Entity("sprk_todo", TodoUnderComm);
        IReadOnlyList<CoreAncestorStamp> root =
        [
            new("sprk_workassignment", "sprk_regardingworkassignment", WorkAssignment),
        ];
        var carriable = CoreAncestorResolver.CarriableRootTypes("sprk_communication");
        var withoutTheColumn = new HashSet<string>(
            ["sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingservicerequest"], StringComparer.OrdinalIgnoreCase);

        CoreAncestorRestamper.PlanStamp(row, carriable, root, withoutTheColumn).Should().BeNull(
            "the only difference is on a column this table does not have");

        var withTheColumn = new HashSet<string>(withoutTheColumn, StringComparer.OrdinalIgnoreCase) { "sprk_regardingworkassignment" };
        CoreAncestorRestamper.PlanStamp(row, carriable, root, withTheColumn).Should().ContainKey("sprk_regardingworkassignment",
            "control: with the column present the same difference IS planned");
    }

    /// <summary>
    /// A communication filed under <paramref name="communicationMatter"/> (or nothing), with a to-do, an event and an
    /// analysis filed under it whose copies say <paramref name="childCopy"/> — the pair names the communication, as the
    /// regarding builders write it.
    /// </summary>
    private static StampWorld CommunicationWithChildren(Guid? communicationMatter, Guid childCopy)
    {
        var world = new StampWorld()
            .Row("sprk_communication", Communication,
                communicationMatter is { } m ? [("sprk_regardingmatter", "sprk_matter", m)] : []);

        foreach (var (entity, id) in new[] { ("sprk_todo", TodoUnderComm), ("sprk_event", EventUnderComm), ("sprk_analysis", AnalysisUnderComm) })
        {
            world.Row(entity, id,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", childCopy)],
                pairId: Communication.ToString());
        }

        return world;
    }
}
