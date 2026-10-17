using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// Ontology task 144 (D-131, child-record contract C2): <c>sprk_signal</c> and <c>sprk_decisionrecord</c> are registered with
/// the ONE stamp machinery (<see cref="CoreAncestorResolver.StampSourceColumns"/>), so re-filing their subject re-stamps them in
/// the same operation as every other stamped child — no parallel restamper.
/// </summary>
/// <remarks>
/// The stamp follows the SUBJECT (live filing); the decision context — typed <c>sprk_matter</c>, <c>sprk_corerecordid</c>,
/// <c>sprk_corerecordtype</c>, the policy version and the snapshot — is never written (D-34). Assertions are on the PATCHes the
/// real <see cref="CoreAncestorRestamper"/> sends to the in-memory Dataverse (<see cref="StampWorld"/>).
/// </remarks>
public class OntologyStampedChildRestampTests
{
    private static readonly Guid OrdinaryMatter = Guid.Parse("14400000-0000-0000-0000-00000000000a");
    private static readonly Guid SecureMatter = Guid.Parse("14400000-0000-0000-0000-00000000000b");
    private static readonly Guid Project = Guid.Parse("14400000-0000-0000-0000-00000000000c");
    private static readonly Guid WorkAssignment = Guid.Parse("14400000-0000-0000-0000-00000000000d");
    private static readonly Guid Communication = Guid.Parse("14400000-0000-0000-0000-000000000c01");
    private static readonly Guid TodoUnderCommunication = Guid.Parse("14400000-0000-0000-0000-000000000101");
    private static readonly Guid Row = Guid.Parse("14400000-0000-0000-0000-000000000501");
    private static readonly Guid CoreType = Guid.Parse("14400000-0000-0000-0000-0000000000f1");
    private static readonly Guid PolicyVersion = Guid.Parse("14400000-0000-0000-0000-0000000000f2");

    private static readonly string[] DecisionContextColumns =
        ["sprk_matter", "sprk_corerecordid", "sprk_corerecordtype", "sprk_policyversion", "sprk_snapshot"];

    public static TheoryData<string> OntologyTables() => new() { "sprk_signal", "sprk_decisionrecord" };

    // ── Registration ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 144: a Signal / Decision Record is stamped from its subject's intermediate lookups — and from no core lookup")]
    [MemberData(nameof(OntologyTables))]
    public void Registration_SourcesAreTheIntermediateSubjectLookups(string table)
    {
        CoreAncestorResolver.StampSourceColumns[table].Should().BeEquivalentTo(new[]
        {
            ("sprk_regardingcommunication", "sprk_communication"), ("sprk_regardingdocument", "sprk_document"),
            ("sprk_regardingevent", "sprk_event"), ("sprk_regardinginvoice", "sprk_invoice"),
            ("sprk_regardingtodo", "sprk_todo"),
            // ebilling-r1 task 026 (FR-15, D-94): the invoice-line subject.
            ("sprk_regardinginvoiceline", "sprk_billingevent"),
        });

        CoreAncestorResolver.StampSourceColumns[table].Select(s => s.Column).Should().NotIntersectWith(
            CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute),
            "a core subject lookup is the row's own root, never a source (uac-r2 condition: root classification)");
        CoreAncestorResolver.IntermediateRootColumns.Should().NotContainKey(table, "uac-r2: never an intermediate");
        CoreAncestorResolver.IsStampedOutsideAccessTaxonomy(table).Should().BeTrue();
    }

    // ── Root classification (uac-r2 condition) ──────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 144 (uac-r2): a Signal / Decision Record whose subject IS a core is filed under no intermediate — its sprk_regarding{core} is the root, not a copy")]
    [InlineData("sprk_signal", "sprk_regardingmatter", "sprk_matter")]
    [InlineData("sprk_signal", "sprk_regardingproject", "sprk_project")]
    [InlineData("sprk_signal", "sprk_regardingworkassignment", "sprk_workassignment")]
    [InlineData("sprk_decisionrecord", "sprk_regardingmatter", "sprk_matter")]
    [InlineData("sprk_decisionrecord", "sprk_regardingproject", "sprk_project")]
    [InlineData("sprk_decisionrecord", "sprk_regardingworkassignment", "sprk_workassignment")]
    public void CoreSubject_IsTheRoot_NotAStampSource(string table, string column, string core)
    {
        var subject = core switch { "sprk_matter" => OrdinaryMatter, "sprk_project" => Project, _ => WorkAssignment };
        var row = new Entity(table, Row)
        {
            [column] = new EntityReference(core, subject),
            [CoreAncestorResolver.RegardingRecordIdColumn] = subject.ToString(),
        };

        CoreAncestorResolver.ClassifyStampSource(table, row, CoreAncestorResolver.PartyRegardingColumnNames(table))
            .Kind.Should().Be(StampSourceKind.NotFiledUnderAnIntermediate);
    }

    [Theory(DisplayName = "Task 144 (uac-r2): a core subject beside an intermediate lookup is a DIRECT root link — the intermediate is only a carrier")]
    [MemberData(nameof(OntologyTables))]
    public void CoreSubjectWithACarrier_IsADirectRootLink(string table)
    {
        var row = new Entity(table, Row)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", Communication),
            [CoreAncestorResolver.RegardingRecordIdColumn] = OrdinaryMatter.ToString(),
        };

        CoreAncestorResolver.ClassifyStampSource(table, row).Kind.Should().Be(StampSourceKind.DirectRootLink);
    }

    [Theory(DisplayName = "Task 144: a Signal / Decision Record whose subject is a communication copies the communication's root")]
    [MemberData(nameof(OntologyTables))]
    public void IntermediateSubject_IsTheSource(string table)
    {
        var row = new Entity(table, Row)
        {
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", Communication),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            [CoreAncestorResolver.RegardingRecordIdColumn] = Communication.ToString(),
        };

        var decision = CoreAncestorResolver.ClassifyStampSource(table, row);
        decision.Kind.Should().Be(StampSourceKind.Source);
        decision.Source!.Intermediate.Should().Be("sprk_communication");
        decision.Source.Id.Should().Be(Communication);
    }

    // ── The cascade (contract §5 items 4-6, restamp half) ──────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 144: re-filing a communication ordinary → secure re-stamps its Signal / Decision Record in the same operation, and writes no decision-context column")]
    [MemberData(nameof(OntologyTables))]
    public async Task RefiledSubject_RestampsTheRow_AndLeavesTheDecisionContextByteIdentical(string table)
    {
        var world = SubjectCommunication(communicationMatter: SecureMatter, table, copy: OrdinaryMatter);
        var before = DecisionContext(world, table);

        var report = await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        report.Complete.Should().BeTrue();
        world.Lookup(table, Row, "sprk_regardingmatter").Should().Be(SecureMatter, "the stamp follows the subject");
        world.PatchesTo(table, Row).Should().ContainSingle()
            .Which.Fields.Keys.Should().BeSubsetOf(CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute),
                "the restamper writes stamp columns only — never the owner, never the decision context (D-34)");
        DecisionContext(world, table).Should().Equal(before, "sprk_corerecordid/type, policy version and snapshot are immutable");
    }

    [Theory(DisplayName = "Task 144: moving the communication back secure → ordinary re-stamps the row back (no secure stamp is left behind)")]
    [MemberData(nameof(OntologyTables))]
    public async Task MovedBack_RestampsTheRowBack(string table)
    {
        var world = SubjectCommunication(communicationMatter: OrdinaryMatter, table, copy: SecureMatter);
        var before = DecisionContext(world, table);

        await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        world.Lookup(table, Row, "sprk_regardingmatter").Should().Be(OrdinaryMatter);
        DecisionContext(world, table).Should().Equal(before);
    }

    [Theory(DisplayName = "Task 144: a Signal / Decision Record whose subject IS the matter is never re-stamped (its root is its own)")]
    [MemberData(nameof(OntologyTables))]
    public async Task CoreSubject_IsNeverRestamped(string table)
    {
        var world = new StampWorld()
            .Row(table, Row, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)], pairId: OrdinaryMatter.ToString());

        var report = await world.Restamper.RestampChildAsync(table, Row);

        report.Changed.Should().Be(0);
        world.PatchesTo(table, Row).Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 144: a Signal about a To Do filed under the communication follows it transitively (communication → To Do → Signal)")]
    public async Task SignalAboutAToDo_FollowsTransitively()
    {
        // Justification beyond the stated contract (task notes): a To Do is a real Signal subject (the Do lane), and the
        // To Do is itself a stamped child of the communication, so the stamp reaches the Signal through one more hop.
        var world = new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", SecureMatter)],
                pairId: SecureMatter.ToString())
            .Row("sprk_todo", TodoUnderCommunication,
                [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)],
                pairId: Communication.ToString())
            .Row("sprk_signal", Row,
                [("sprk_regardingtodo", "sprk_todo", TodoUnderCommunication), ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)],
                pairId: TodoUnderCommunication.ToString());

        await world.Restamper.AfterWriteAsync("sprk_communication", Communication, ["sprk_regardingmatter"]);

        world.Lookup("sprk_todo", TodoUnderCommunication, "sprk_regardingmatter").Should().Be(SecureMatter);
        world.Lookup("sprk_signal", Row, "sprk_regardingmatter").Should().Be(SecureMatter);
    }

    // ── ebilling-r1 task 026 (D-94 condition 2): a Signal / Decision Record about an invoice line ────────────────────────

    private static readonly Guid Line = Guid.Parse("02600000-0000-0000-0000-000000000b01");
    private static readonly Guid Invoice = Guid.Parse("02600000-0000-0000-0000-000000000a01");

    /// <summary>A line filed under <paramref name="lineRoots"/> (its invoice under the ordinary matter) and a <paramref name="table"/>
    /// row about the line stamped with the ordinary matter, with a full decision context.</summary>
    private static StampWorld SubjectLine(string table, params (string Column, string Target, Guid Id)[] lineRoots) =>
        new StampWorld()
            .Row("sprk_invoice", Invoice, [("sprk_matter", "sprk_matter", OrdinaryMatter)])
            .Row("sprk_billingevent", Line, [.. lineRoots, ("sprk_invoice", "sprk_invoice", Invoice)])
            .Row(table, Row,
                [
                    ("sprk_regardinginvoiceline", "sprk_billingevent", Line),
                    ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter),
                    ("sprk_matter", "sprk_matter", OrdinaryMatter),
                    ("sprk_corerecordtype", "sprk_recordtype_ref", CoreType),
                    ("sprk_policyversion", "sprk_policyversion", PolicyVersion),
                ],
                pairId: Line.ToString())
            .Set(table, Row, "sprk_corerecordid", OrdinaryMatter.ToString("D"))
            .Set(table, Row, "sprk_snapshot", "{\"evidence\":[1]}");

    [Theory(DisplayName = "ebilling-r1 task 026: re-filing an invoice line matter → project re-stamps its Signal / Decision Record, and writes no decision-context column")]
    [MemberData(nameof(OntologyTables))]
    public async Task RefiledInvoiceLine_RestampsTheRow_AndLeavesTheDecisionContextByteIdentical(string table)
    {
        var world = SubjectLine(table, ("sprk_project", "sprk_project", Project));
        var before = DecisionContext(world, table);

        var report = await world.Restamper.AfterWriteAsync("sprk_billingevent", Line, ["sprk_matter", "sprk_project"]);

        report.Complete.Should().BeTrue();
        world.Lookup(table, Row, "sprk_regardingproject").Should().Be(Project, "the stamp follows the line's own root");
        world.Lookup(table, Row, "sprk_regardingmatter").Should().BeNull("the line's invoice's matter is never the stamp (D-75)");
        world.PatchesTo(table, Row).Should().ContainSingle()
            .Which.Fields.Keys.Should().BeSubsetOf(CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute));
        DecisionContext(world, table).Should().Equal(before, "sprk_corerecordid/type, policy version and snapshot are immutable");
    }

    [Theory(DisplayName = "ebilling-r1 task 026: an invoice line naming two roots re-stamps nothing (refused, never a guess)")]
    [MemberData(nameof(OntologyTables))]
    public async Task InvoiceLineWithTwoRoots_RestampsNothing(string table)
    {
        var world = SubjectLine(table, ("sprk_project", "sprk_project", Project), ("sprk_matter", "sprk_matter", SecureMatter));

        var report = await world.Restamper.AfterWriteAsync("sprk_billingevent", Line, ["sprk_matter", "sprk_project"]);

        report.Complete.Should().BeFalse("a two-root line cannot be derived; the stamp is left for the reconciler to report");
        world.PatchesTo(table, Row).Should().BeEmpty();
        world.Lookup(table, Row, "sprk_regardingmatter").Should().Be(OrdinaryMatter);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A communication filed under <paramref name="communicationMatter"/> and a <paramref name="table"/> row about it whose
    /// stamp says <paramref name="copy"/>, with a full decision context.</summary>
    private static StampWorld SubjectCommunication(Guid communicationMatter, string table, Guid copy) =>
        new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", communicationMatter)],
                pairId: communicationMatter.ToString())
            .Row(table, Row,
                [
                    ("sprk_regardingcommunication", "sprk_communication", Communication),
                    ("sprk_regardingmatter", "sprk_matter", copy),
                    ("sprk_matter", "sprk_matter", OrdinaryMatter),
                    ("sprk_corerecordtype", "sprk_recordtype_ref", CoreType),
                    ("sprk_policyversion", "sprk_policyversion", PolicyVersion),
                ],
                pairId: Communication.ToString())
            .Set(table, Row, "sprk_corerecordid", OrdinaryMatter.ToString("D"))
            .Set(table, Row, "sprk_snapshot", "{\"evidence\":[1,2,3]}");

    private static List<string> DecisionContext(StampWorld world, string table)
    {
        var row = world.RowOf(table, Row);
        return DecisionContextColumns
            .Select(c => row.Attributes.TryGetValue(c, out var v)
                ? c + "=" + (v is EntityReference r ? $"{r.LogicalName}:{r.Id:D}" : v?.ToString())
                : c + "=<absent>")
            .ToList();
    }
}
