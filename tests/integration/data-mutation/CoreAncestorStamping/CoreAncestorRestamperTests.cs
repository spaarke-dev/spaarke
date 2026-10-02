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
        var world = new StampWorld()
            .RecordType(CommunicationTypeRef, "sprk_communication")
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_todo", TodoUnderComm, [("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Communication.ToString(), pairType: CommunicationTypeRef);

        await world.Restamper.RestampChildAsync("sprk_todo", TodoUnderComm);

        world.Lookup("sprk_todo", TodoUnderComm, "sprk_regardingmatter").Should().BeNull(
            "the to-do shows no regarding any more, so it must stop inheriting the old matter's access (fail closed)");
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
