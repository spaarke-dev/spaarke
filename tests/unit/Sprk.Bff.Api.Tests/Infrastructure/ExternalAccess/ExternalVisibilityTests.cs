using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191b (#2179 design v2 §3.3): THE external visibility predicate,
/// <see cref="ExternalVisibility"/>, over an in-memory Dataverse. The lineage rows are SDK entities (the walk's reads, projected
/// through each query's column set); documents, communications and threads are also OData rows, so the document branch runs
/// task 188's REAL decision (<see cref="ExternalDocumentNarrowing.ReadAndDecideAsync"/>) and the communication branch THE 065
/// rule, as the composed predicate does. Root flags and the invoice decision are the readers' answers (their own rules are
/// pinned by their own tests). Each marker kind and each taint path has a must-fire and a must-not-fire test.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalVisibilityTests
{
    private const int Restricted = 100000002;
    private const int Open = 100000000;
    private const int Private = 100000001;
    private const string Library = "b!procedure-library-drive";

    private readonly World _world = new();

    private Task<ExternalVisibilityResult> Decide(params (string Table, Guid Id)[] rows) =>
        ExternalVisibility.DecideAsync(rows, _world.Readers(), CancellationToken.None);

    private async Task<ExternalVisibilityDecision> DecideOne(string table, Guid id) => (await Decide((table, id))).For(table, id);

    // ── Must-not-fire baseline ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOrdinaryRow_UnderAStandardOrLimitedRoot_IsVisible(bool limited)
    {
        var matter = _world.Root(limited: limited);
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", matter));

        var decision = await DecideOne("sprk_todo", todo);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
        decision.Reason.Should().Be(ExternalVisibilityReason.Visible);
    }

    // ── Leg 1: a Restricted root, effective through filing ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_todo", "sprk_regardingmatter")]
    [InlineData("sprk_event", "sprk_regardingmatter")]
    [InlineData("sprk_memo", "sprk_regardingmatter")]
    [InlineData("sprk_budget", "sprk_matter")]
    public async Task ARowUnderARestrictedRoot_IsHidden(string table, string column)
    {
        var matter = _world.Root(restricted: true);
        var row = _world.Add(table, (column, matter));

        var decision = await DecideOne(table, row);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        decision.Reason.Should().Be(ExternalVisibilityReason.RootRestricted);
        decision.SourceId.Should().Be(matter);
    }

    /// <summary>Restricted THROUGH FILING: the reader's effective answer for a work assignment filed under a Restricted matter
    /// (<c>EffectiveRootFlags</c>) hides the work assignment's to-do, though the work assignment's own value is Standard.</summary>
    [Fact]
    public async Task ARowUnderARootRestrictedThroughFiling_IsHidden()
    {
        var workAssignment = _world.Root("sprk_workassignment", restricted: true);
        var todo = _world.Add("sprk_todo", ("sprk_regardingworkassignment", workAssignment));

        (await DecideOne("sprk_todo", todo)).Reason.Should().Be(ExternalVisibilityReason.RootRestricted);
        _world.RootFlagReads.Should().ContainSingle().Which.Should().Be("sprk_workassignment");
    }

    [Fact]
    public async Task ARowWithOneRestrictedRootAmongSeveral_IsHidden()
    {
        var open = _world.Root();
        var restricted = _world.Root("sprk_project", restricted: true);
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", open), ("sprk_regardingproject", restricted));

        (await DecideOne("sprk_todo", todo)).Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
    }

    [Theory]
    [InlineData(true, "Hidden")]
    [InlineData(false, "Visible")]
    public async Task ARootAsked_IsDecidedByItsOwnEffectiveFlags(bool restricted, string expectedName)
    {
        var expected = Enum.Parse<ExternalVisibilityVerdict>(expectedName);
        var matter = _world.Root(restricted: restricted);

        (await DecideOne("sprk_matter", matter)).Verdict.Should().Be(expected);
    }

    // uac-r2 191c (adversarial F1): the roots a decision reached, and which are Secure by their EFFECTIVE flags (Secure folded
    // from the ancestors), for the External-team share engine.
    [Fact]
    public async Task ARowUnderARootSecureThroughFiling_ReachesAnEffectivelySecureRoot()
    {
        var workAssignment = _world.Root("sprk_workassignment", secure: true);
        var todo = _world.Add("sprk_todo", ("sprk_regardingworkassignment", workAssignment));

        var result = await Decide(("sprk_todo", todo));

        result.RootsOf("sprk_todo", todo).Should().Equal(("sprk_workassignment", workAssignment));
        result.ReachesEffectivelySecureRoot("sprk_todo", todo).Should().BeTrue();
    }

    [Fact]
    public async Task ARowUnderAnOrdinaryRoot_ReachesNoEffectivelySecureRoot_AndAnUnaskedRowFailsClosed()
    {
        var matter = _world.Root();
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", matter));

        var result = await Decide(("sprk_todo", todo));

        result.ReachesEffectivelySecureRoot("sprk_todo", todo).Should().BeFalse();
        result.ReachesEffectivelySecureRoot("sprk_todo", Guid.NewGuid()).Should().BeTrue("roots not known fail closed");
    }

    [Fact]
    public async Task ARowUnderARootWhoseFlagsCannotBeRead_IsUndecided()
    {
        var matter = _world.Root(unreadable: true);
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", matter));

        var decision = await DecideOne("sprk_todo", todo);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Undecided);
        decision.Reason.Should().Be(ExternalVisibilityReason.RootUnreadable);
    }

    // ── Leg 2: own markers ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_emailartifact", "sprk_document")]
    [InlineData("sprk_emailreviewlog", "sprk_communication")]
    public async Task ANeverTable_IsHidden_UnderAStandardRoot(string table, string column)
    {
        var parentTable = SecureChildLineage.Children[table].Lookups[column];
        var parent = parentTable == "sprk_document"
            ? _world.Document(matter: _world.Root())
            : _world.Communication(internalOnly: false, _world.Thread(Open), matter: _world.Root());
        var row = _world.Add(table, (column, parent));

        var decision = await DecideOne(table, row);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        decision.Reason.Should().Be(ExternalVisibilityReason.NeverExternal);
    }

    /// <summary>The five hard-deny tables (design §0 item 1) are Hidden under a Standard root, whatever their parent answers,
    /// and so is a row filed under one.</summary>
    [Theory]
    [InlineData("sprk_policy")]
    [InlineData("sprk_policyversion")]
    [InlineData("sprk_procedureassignment")]
    [InlineData("sprk_ruleset")]
    [InlineData("sprk_storedfile")]
    public async Task AHardDenyTable_IsHidden_UnderAStandardRoot(string table)
    {
        var matter = _world.Root();
        var row = table switch
        {
            "sprk_policyversion" => _world.Add(table, ("sprk_policy", _world.Add("sprk_policy", ("sprk_matter", matter)))),
            "sprk_storedfile" => _world.Add(table, ("sprk_invoice", _world.Add("sprk_invoice", ("sprk_matter", matter)))),
            _ => _world.Add(table, ("sprk_matter", matter)),
        };

        var decision = await DecideOne(table, row);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        decision.Reason.Should().Be(ExternalVisibilityReason.HardDeny);
    }

    [Fact]
    public async Task ANeverTableOutsideTheLineage_IsHidden_WithoutARead()
    {
        var decision = await DecideOne("sprk_affinity", Guid.NewGuid());

        decision.Reason.Should().Be(ExternalVisibilityReason.NeverExternal);
        _world.EntityReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("blank")]
    [InlineData("unreadable")]
    [InlineData("private")]
    [InlineData("nothread")]
    public async Task AHiddenCommunication_IsHidden(string shape)
    {
        var matter = _world.Root();
        var communication = shape switch
        {
            "internal" => _world.Communication(internalOnly: true, _world.Thread(Open), matter),
            "blank" => _world.Communication(internalOnly: null, _world.Thread(Open), matter),
            "private" => _world.Communication(internalOnly: false, _world.Thread(Private), matter),
            "nothread" => _world.Communication(internalOnly: false, thread: null, matter),
            _ => _world.Communication(internalOnly: false, _world.Thread(Open), matter, flag: JsonSerializer.SerializeToElement("false")),
        };

        var decision = await DecideOne("sprk_communication", communication);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        decision.Reason.Should().BeOneOf(ExternalVisibilityReason.CommunicationHidden, ExternalVisibilityReason.ThreadNotOpen);
    }

    [Fact]
    public async Task AVisibleCommunication_OnAnOpenThread_UnderAStandardRoot_IsVisible()
    {
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open), _world.Root());

        (await DecideOne("sprk_communication", communication)).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    [Theory]
    [InlineData(Private, "Hidden")]
    [InlineData(Open, "Visible")]
    public async Task AThread_IsVisibleOnlyWhenExplicitlyOpen(int privacy, string expectedName)
    {
        var expected = Enum.Parse<ExternalVisibilityVerdict>(expectedName);
        var thread = _world.Thread(privacy, _world.Root());

        var decision = await DecideOne("sprk_communicationthread", thread);

        decision.Verdict.Should().Be(expected);
        if (expected == ExternalVisibilityVerdict.Hidden)
            decision.Reason.Should().Be(ExternalVisibilityReason.ThreadNotOpen);
    }

    [Fact]
    public async Task ADocumentTask188Hides_IsHidden_AndAVisibleOneIsVisible()
    {
        var matter = _world.Root();
        var hidden = _world.Document(permission: Restricted, matter: matter);
        var visible = _world.Document(matter: matter);

        var result = await Decide(("sprk_document", hidden), ("sprk_document", visible));

        result.For("sprk_document", hidden).Reason.Should().Be(ExternalVisibilityReason.DocumentHidden);
        result.For("sprk_document", visible).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    [Fact]
    public async Task ALegacyProcedureLibraryDocument_IsHidden_ByItsDrive()
    {
        var matter = _world.Root();
        var library = _world.Document(matter: matter, drive: Library.ToUpperInvariant());
        var other = _world.Document(matter: matter, drive: "b!some-matter-container");

        var result = await Decide(("sprk_document", library), ("sprk_document", other));

        result.For("sprk_document", library).Reason.Should().Be(ExternalVisibilityReason.ProcedureLibraryDocument);
        result.For("sprk_document", other).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    [Fact]
    public async Task WithNoProcedureLibraryConfigured_NoDocumentIsOne()
    {
        _world.LibraryDrive = null;
        var document = _world.Document(matter: _world.Root(), drive: Library);

        (await DecideOne("sprk_document", document)).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    [Fact]
    public async Task AnAttachmentUnderAProcedureLibraryDocument_IsHidden()
    {
        var parent = _world.Document(matter: _world.Root(), drive: Library);
        var attachment = _world.Document(parent: parent);

        (await DecideOne("sprk_document", attachment)).Reason.Should().Be(ExternalVisibilityReason.ProcedureLibraryDocument);
    }

    // ── The document chain: task 188's three hops ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The AC chain: an internal-only communication, the document linked to it, and documents 1, 2, 3 and 4 parent hops below
    /// it, each with a current version (the document ↔ version pair is a path cycle the walk skips). The communication and the
    /// documents up to 3 hops below are Hidden; the document 4 hops below is Undecided (188's bound), never Visible and never
    /// Hidden by a chain member's own 188 verdict.
    /// </summary>
    [Fact]
    public async Task DocumentsBelowAHiddenCommunication_AreHiddenToThreeHops_AndUndecidedAtFour()
    {
        var communication = _world.Communication(internalOnly: true, _world.Thread(Open), _world.Root());
        var chain = Chain(communication, length: 5);

        var result = await Decide(chain.Select(d => ("sprk_document", d)).Append(("sprk_communication", communication)).ToArray());

        result.For("sprk_communication", communication).Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        for (var hops = 0; hops <= 3; hops++)
            result.For("sprk_document", chain[hops]).Verdict.Should().Be(ExternalVisibilityVerdict.Hidden, $"{hops} hops below");
        var fourth = result.For("sprk_document", chain[4]);
        fourth.Verdict.Should().Be(ExternalVisibilityVerdict.Undecided, "188 cannot know a chain past its bound");
    }

    [Fact]
    public async Task DocumentsBelowAVisibleCommunication_AreVisibleToThreeHops_AndUndecidedAtFour()
    {
        var matter = _world.Root();
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open), matter);
        var chain = Chain(communication, length: 5, matter);

        var result = await Decide(chain.Select(d => ("sprk_document", d)).ToArray());

        for (var hops = 0; hops <= 3; hops++)
            result.For("sprk_document", chain[hops]).Verdict.Should().Be(ExternalVisibilityVerdict.Visible, $"{hops} hops below");
        result.For("sprk_document", chain[4]).Verdict.Should().Be(ExternalVisibilityVerdict.Undecided);
    }

    /// <summary>No hidden lineage parent (design §3.3 rule 3): an attachment with its own visible link, under an .eml linked to
    /// a hidden communication, is HIDDEN with the .eml (the parent document's communication link is a lineage edge like any
    /// other, even though 188's first link alone would show the attachment).</summary>
    [Fact]
    public async Task AnAttachmentUnderTheEmlOfAHiddenCommunication_IsHidden()
    {
        var matter = _world.Root();
        var eml = _world.Document(communication: _world.Communication(internalOnly: true, _world.Thread(Open), matter), matter: matter);
        var attachment = _world.Document(
            communication: _world.Communication(internalOnly: false, _world.Thread(Open), matter), parent: eml, matter: matter);

        var result = await Decide(("sprk_document", attachment), ("sprk_document", eml));

        result.For("sprk_document", attachment).Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        result.For("sprk_document", attachment).Reason.Should().Be(ExternalVisibilityReason.CommunicationHidden);
        result.For("sprk_document", eml).Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
    }

    /// <summary>Taint through the chain: a parent document filed under a Restricted matter hides its attachment, whose own
    /// filing is Standard (188 does not read roots; the closure does).</summary>
    [Fact]
    public async Task AnAttachmentWhoseParentIsUnderARestrictedRoot_IsHidden()
    {
        var parent = _world.Document(matter: _world.Root(restricted: true));
        var attachment = _world.Document(parent: parent, matter: _world.Root());

        (await DecideOne("sprk_document", attachment)).Reason.Should().Be(ExternalVisibilityReason.RootRestricted);
    }

    [Fact]
    public async Task ADocumentParentCycle_IsUndecided()
    {
        var first = Guid.NewGuid();
        var second = _world.Document(parent: first);
        _world.Document(id: first, parent: second);

        (await DecideOne("sprk_document", first)).Verdict.Should().Be(ExternalVisibilityVerdict.Undecided);
    }

    // ── Leg 3: taint closure ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_memo")]
    [InlineData("sprk_analysis")]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    public async Task ARowRegardingAHiddenCommunication_IsHidden(string table)
    {
        var matter = _world.Root();
        var communication = _world.Communication(internalOnly: true, _world.Thread(Open), matter);
        var row = _world.Add(table, ("sprk_regardingcommunication", communication));

        var decision = await DecideOne(table, row);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        decision.Reason.Should().Be(ExternalVisibilityReason.CommunicationHidden);
        decision.SourceId.Should().Be(communication);
    }

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_memo")]
    [InlineData("sprk_analysis")]
    [InlineData("sprk_signal")]
    [InlineData("sprk_decisionrecord")]
    public async Task ARowRegardingAVisibleCommunication_IsVisible(string table)
    {
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open), _world.Root());
        var row = _world.Add(table, ("sprk_regardingcommunication", communication));

        (await DecideOne(table, row)).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_regardingdocument")]
    [InlineData("sprk_memo", "sprk_regardingdocument")]
    [InlineData("sprk_analysis", "sprk_documentid")]
    [InlineData("sprk_signal", "sprk_regardingdocument")]
    [InlineData("sprk_decisionrecord", "sprk_regardingdocument")]
    public async Task ARowRegardingAHiddenDocument_IsHidden(string table, string column)
    {
        var matter = _world.Root();
        var document = _world.Document(
            communication: _world.Communication(internalOnly: true, _world.Thread(Open), matter), matter: matter);
        var row = _world.Add(table, (column, document));

        var decision = await DecideOne(table, row);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        decision.SourceId.Should().NotBe(row, "the row is hidden by what it regards");
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_regardingdocument")]
    [InlineData("sprk_analysis", "sprk_documentid")]
    public async Task ARowRegardingAVisibleDocument_IsVisible(string table, string column)
    {
        var document = _world.Document(matter: _world.Root());
        var row = _world.Add(table, (column, document));

        (await DecideOne(table, row)).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    [Fact]
    public async Task AnAnalysisOutput_ThroughItsAnalysis_OfAHiddenDocument_IsHidden()
    {
        var document = _world.Document(permission: Restricted, matter: _world.Root());
        var analysis = _world.Add("sprk_analysis", ("sprk_documentid", document));
        var output = _world.Add("sprk_analysisoutput", ("sprk_analysisid", analysis));

        (await DecideOne("sprk_analysisoutput", output)).Reason.Should().Be(ExternalVisibilityReason.DocumentHidden);
    }

    [Fact]
    public async Task AnEventLog_UnderAnEventRegardingAHiddenCommunication_IsHidden()
    {
        var communication = _world.Communication(internalOnly: true, _world.Thread(Open), _world.Root());
        var @event = _world.Add("sprk_event", ("sprk_regardingcommunication", communication));
        var log = _world.Add("sprk_eventlog", ("sprk_event", @event));

        (await DecideOne("sprk_eventlog", log)).Reason.Should().Be(ExternalVisibilityReason.CommunicationHidden);
    }

    // ── The core record pair (Signals and Decision Records) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, "Hidden")]
    [InlineData(false, "Visible")]
    public async Task ASignalWhoseCorePairNamesARoot_TakesThatRootsVerdict(bool restricted, string expectedName)
    {
        var expected = Enum.Parse<ExternalVisibilityVerdict>(expectedName);
        var project = _world.Root("sprk_project", restricted: restricted);
        var signal = _world.Add("sprk_signal", corePair: (_world.RecordType("sprk_project"), project.ToString()));

        (await DecideOne("sprk_signal", signal)).Verdict.Should().Be(expected);
    }

    [Fact]
    public async Task ASignalWithAMalformedCorePair_IsUndecided()
    {
        var signal = _world.Add("sprk_signal", corePair: (_world.RecordType("sprk_project"), "not-a-guid"));

        (await DecideOne("sprk_signal", signal)).Reason.Should().Be(ExternalVisibilityReason.CorePairUndecided);
    }

    [Fact]
    public async Task ADecisionRecordWhoseCoreTypeNamesNoCoreTable_IsUndecided()
    {
        var record = _world.Add("sprk_decisionrecord", corePair: (_world.RecordType("contact"), Guid.NewGuid().ToString()));

        (await DecideOne("sprk_decisionrecord", record)).Reason.Should().Be(ExternalVisibilityReason.CorePairUndecided);
    }

    // ── ebilling (a′) ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SourceHidden")]
    [InlineData("RootRestricted")]
    public async Task AnInvoiceWhoseSourceIsHidden_AndItsDerivedRows_AreHidden(string reasonName)
    {
        var matter = _world.Root();
        var invoice = _world.Add("sprk_invoice", ("sprk_matter", matter));
        _world.InvoiceReasons[invoice] = Enum.Parse<InvoiceHiddenReason>(reasonName);
        var line = _world.Add("sprk_billingevent", ("sprk_invoice", invoice), ("sprk_matter", matter));
        var approval = _world.Add("sprk_invoiceapproval", ("sprk_invoice", invoice));

        var result = await Decide(("sprk_invoice", invoice), ("sprk_billingevent", line), ("sprk_invoiceapproval", approval));

        // (A Stored File under the invoice is a hard-deny table: Hidden whatever its invoice answers; see the HardDeny test.)
        foreach (var (table, id) in new[] { ("sprk_invoice", invoice), ("sprk_billingevent", line), ("sprk_invoiceapproval", approval) })
        {
            result.For(table, id).Reason.Should().Be(ExternalVisibilityReason.InvoiceHidden, table);
        }
    }

    [Fact]
    public async Task AVisibleInvoice_AndItsDerivedRows_AreVisible()
    {
        var matter = _world.Root();
        var invoice = _world.Add("sprk_invoice", ("sprk_matter", matter));
        var line = _world.Add("sprk_billingevent", ("sprk_invoice", invoice));

        var result = await Decide(("sprk_invoice", invoice), ("sprk_billingevent", line));

        result.For("sprk_invoice", invoice).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
        result.For("sprk_billingevent", line).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    /// <summary>An (a′) decision whose read failed for one invoice is a fault of the whole decision (one notion of a fault):
    /// every row is Undecided and the result says Faulted.</summary>
    [Fact]
    public async Task AnInvoiceWhoseDecisionFailed_FaultsTheDecision()
    {
        var matter = _world.Root();
        var invoice = _world.Add("sprk_invoice", ("sprk_matter", matter));
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", matter));
        _world.InvoiceReasons[invoice] = InvoiceHiddenReason.ReadFailed;

        var result = await Decide(("sprk_invoice", invoice), ("sprk_todo", todo));

        result.Faulted.Should().BeTrue();
        result.For("sprk_invoice", invoice).Reason.Should().Be(ExternalVisibilityReason.ReadFailed);
        result.For("sprk_todo", todo).Reason.Should().Be(ExternalVisibilityReason.ReadFailed);
    }

    /// <summary>A rule that answers "my read failed" without throwing (188's or (a′)'s faulted result) faults the decision
    /// exactly as a throw does.</summary>
    [Theory]
    [InlineData("documents")]
    [InlineData("invoices")]
    public async Task ARuleThatAnswersFaulted_FaultsTheDecision(string rule)
    {
        var matter = _world.Root();
        var document = _world.Document(matter: matter);
        var invoice = _world.Add("sprk_invoice", ("sprk_matter", matter));
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", matter));
        var readers = rule == "documents"
            ? _world.Readers() with { Documents = (_, _) => Task.FromResult(DocumentNarrowingResult.ReadFailed) }
            : _world.Readers() with { Invoices = (ids, _) => Task.FromResult(InvoiceVisibilityResult.ReadFailed(ids)) };

        var result = await ExternalVisibility.DecideAsync(
            new[] { ("sprk_document", document), ("sprk_invoice", invoice), ("sprk_todo", todo) }, readers, CancellationToken.None);

        result.Faulted.Should().BeTrue();
        foreach (var (table, id) in new[] { ("sprk_document", document), ("sprk_invoice", invoice), ("sprk_todo", todo) })
            result.For(table, id).Reason.Should().Be(ExternalVisibilityReason.ReadFailed, table);
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARowWhoseParentDoesNotComeBack_IsUndecided()
    {
        var todo = _world.Add("sprk_todo", ("sprk_regardingevent", Guid.NewGuid()));

        var decision = await DecideOne("sprk_todo", todo);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Undecided);
        decision.Reason.Should().Be(ExternalVisibilityReason.RowNotFound);
    }

    [Fact]
    public async Task ARowThatDoesNotComeBack_IsUndecided()
    {
        (await DecideOne("sprk_todo", Guid.NewGuid())).Reason.Should().Be(ExternalVisibilityReason.RowNotFound);
    }

    /// <summary>Hidden beats Undecided whatever the order the findings were made in: an unreadable link found first does not
    /// mask a hidden communication found after it.</summary>
    /// <summary>A rule that answers faulted does not throw away a hide another sound rule already decided: a row under a
    /// Restricted root stays Hidden when (a′) or 188 reports a failed read in the same batch; the rest are ReadFailed.</summary>
    [Theory]
    [InlineData("invoices")]
    [InlineData("documents")]
    public async Task AHiddenRow_StaysHidden_WhenAnotherRuleAnswersFaulted(string rule)
    {
        var standard = _world.Root();
        var invoice = _world.Add("sprk_invoice", ("sprk_matter", standard));
        var document = _world.Document(matter: standard);
        var hidden = _world.Add("sprk_todo", ("sprk_regardingmatter", _world.Root(restricted: true)));
        var readers = rule == "documents"
            ? _world.Readers() with { Documents = (_, _) => Task.FromResult(DocumentNarrowingResult.ReadFailed) }
            : _world.Readers() with { Invoices = (ids, _) => Task.FromResult(InvoiceVisibilityResult.ReadFailed(ids)) };

        var result = await ExternalVisibility.DecideAsync(
            new[] { ("sprk_invoice", invoice), ("sprk_document", document), ("sprk_todo", hidden) }, readers, CancellationToken.None);

        result.Faulted.Should().BeTrue();
        result.For("sprk_todo", hidden).Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        result.For("sprk_todo", hidden).Reason.Should().Be(ExternalVisibilityReason.RootRestricted);
        result.For(rule == "documents" ? "sprk_document" : "sprk_invoice", rule == "documents" ? document : invoice)
            .Reason.Should().Be(ExternalVisibilityReason.ReadFailed);
    }

    [Fact]
    public async Task AHiddenFinding_BeatsAnEarlierUndecidedOne()
    {
        var matter = _world.Root();
        var todo = _world.Add("sprk_todo",
            ("sprk_regardingcommunication", _world.Communication(internalOnly: true, _world.Thread(Open), matter)));
        _world.Entity("sprk_todo", todo)["sprk_regardingevent"] = "not-a-reference";

        var decision = await DecideOne("sprk_todo", todo);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Hidden);
        decision.Reason.Should().Be(ExternalVisibilityReason.CommunicationHidden);
    }

    /// <summary>A closure that reaches no root is never Visible: external access is granted on roots only (a parentless row, a
    /// rootless cycle, a Signal whose core is a service request).</summary>
    [Theory]
    [InlineData("parentless")]
    [InlineData("rootlessCycle")]
    [InlineData("serviceRequestCore")]
    public async Task ARowWhoseClosureReachesNoRoot_IsUndecided(string shape)
    {
        (string Table, Guid Id) row;
        switch (shape)
        {
            case "parentless":
                row = ("sprk_todo", _world.Add("sprk_todo"));
                break;
            case "rootlessCycle":
                var first = Guid.NewGuid();
                var second = _world.Add("sprk_event", ("sprk_regardingevent", first));
                _world.Add("sprk_event", id: first, ("sprk_regardingevent", second));
                row = ("sprk_event", second);
                break;
            default:
                row = ("sprk_signal", _world.Add("sprk_signal",
                    corePair: (_world.RecordType("sprk_servicerequest"), Guid.NewGuid().ToString())));
                break;
        }

        var decision = await DecideOne(row.Table, row.Id);

        decision.Verdict.Should().Be(ExternalVisibilityVerdict.Undecided);
        decision.Reason.Should().Be(ExternalVisibilityReason.NoRoot);
    }

    /// <summary>A parent document's communication link is a lineage edge: a document whose only way to a root runs through it
    /// gets that root (visible here), instead of reaching none.</summary>
    [Fact]
    public async Task ADocumentWhoseOnlyRootIsThroughItsParentsCommunication_IsDecidedByThatRoot()
    {
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open), _world.Root());
        var eml = _world.Document(communication: communication);
        var attachment = _world.Document(parent: eml);

        var result = await Decide(("sprk_document", eml), ("sprk_document", attachment));

        result.For("sprk_document", eml).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
        result.For("sprk_document", attachment).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
    }

    [Fact]
    public async Task ALookupValueThatIsNotAReference_IsUndecided()
    {
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", _world.Root()));
        _world.Entity("sprk_todo", todo)["sprk_regardingevent"] = "not-a-reference";

        (await DecideOne("sprk_todo", todo)).Reason.Should().Be(ExternalVisibilityReason.LinkUnreadable);
    }

    /// <summary>A filing deeper than the lineage walk's bound (6) is Undecided; one level inside it is decided.</summary>
    [Theory]
    [InlineData(5, "Visible")]
    [InlineData(6, "Undecided")]
    public async Task AFilingDeeperThanTheBound_IsUndecided(int events, string expectedName)
    {
        var expected = Enum.Parse<ExternalVisibilityVerdict>(expectedName);
        var parent = _world.Add("sprk_event", ("sprk_regardingmatter", _world.Root()));
        for (var i = 1; i < events; i++)
            parent = _world.Add("sprk_event", ("sprk_regardingevent", parent));
        var todo = _world.Add("sprk_todo", ("sprk_regardingevent", parent));

        var decision = await DecideOne("sprk_todo", todo);

        decision.Verdict.Should().Be(expected);
        if (expected == ExternalVisibilityVerdict.Undecided)
            decision.Reason.Should().Be(ExternalVisibilityReason.TooDeep);
    }

    /// <summary>A lineage cycle outside the document chain (two events regarding each other) adds nothing: the closure is the
    /// same set of rows, so the row is decided by them (the lineage walk's own rule).</summary>
    [Theory]
    [InlineData(false, "Visible")]
    [InlineData(true, "Hidden")]
    public async Task ALineageCycle_IsDecidedByTheRowsOnIt(bool restricted, string expectedName)
    {
        var expected = Enum.Parse<ExternalVisibilityVerdict>(expectedName);
        var first = Guid.NewGuid();
        var second = _world.Add("sprk_event", ("sprk_regardingevent", first));
        _world.Add("sprk_event", id: first, ("sprk_regardingevent", second), ("sprk_regardingmatter", _world.Root(restricted: restricted)));

        (await DecideOne("sprk_event", second)).Verdict.Should().Be(expected);
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_communications")]
    [InlineData("sprk_documents")]
    public async Task AFailedRead_MakesEveryRowUndecided(string fault)
    {
        var matter = _world.Root();
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open), matter);
        var todo = _world.Add("sprk_todo", ("sprk_regardingcommunication", communication));
        var document = _world.Document(matter: matter);
        var @event = _world.Add("sprk_event", ("sprk_regardingmatter", matter));
        _world.Fault = fault;

        var result = await Decide(("sprk_todo", todo), ("sprk_document", document), ("sprk_event", @event));

        result.Faulted.Should().BeTrue();
        foreach (var (table, id) in new[] { ("sprk_todo", todo), ("sprk_document", document), ("sprk_event", @event) })
            result.For(table, id).Reason.Should().Be(ExternalVisibilityReason.ReadFailed);
    }

    [Fact]
    public async Task ARootFlagReadThatThrows_MakesEveryRowUndecided()
    {
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", _world.Root()));
        _world.Fault = "rootflags";

        var result = await Decide(("sprk_todo", todo));

        result.Faulted.Should().BeTrue();
        result.For("sprk_todo", todo).Verdict.Should().Be(ExternalVisibilityVerdict.Undecided);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", _world.Root()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ExternalVisibility.DecideAsync(new[] { ("sprk_todo", todo) }, _world.Readers(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ATableTheRegistryDoesNotKnow_AndARowNotAsked_AreUndecided()
    {
        var todo = _world.Add("sprk_todo", ("sprk_regardingmatter", _world.Root()));

        var result = await Decide(("contact", Guid.NewGuid()), ("sprk_todo", todo));

        result.For("contact", Guid.NewGuid()).Reason.Should().Be(ExternalVisibilityReason.NotAsked);
        result.For("sprk_todo", Guid.NewGuid()).Reason.Should().Be(ExternalVisibilityReason.NotAsked);
        result.For("sprk_todo", Guid.Empty).Verdict.Should().Be(ExternalVisibilityVerdict.Undecided);
        (await Decide(("contact", todo))).For("contact", todo).Reason.Should().Be(ExternalVisibilityReason.UnknownTable);
    }

    // ── Cost ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ManyRowsSharingParents_ReadEachLevelOnce()
    {
        var matter = _world.Root();
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open), matter);
        var todos = Enumerable.Range(0, 30).Select(_ => _world.Add("sprk_todo", ("sprk_regardingcommunication", communication))).ToList();

        var result = await Decide(todos.Select(t => ("sprk_todo", t)).ToArray());

        todos.Should().OnlyContain(t => result.IsVisible("sprk_todo", t));
        _world.EntityReads.Count(t => t == "sprk_todo").Should().Be(1);
        _world.EntityReads.Count(t => t == "sprk_communication").Should().Be(1);
        _world.EntityReads.Count(t => t == "sprk_communicationthread").Should().Be(1);
        _world.RootFlagReads.Should().ContainSingle();
    }

    [Fact]
    public async Task OnlyDocumentsReachedOutsideAChain_AreAskedOf188()
    {
        var parent = _world.Document(matter: _world.Root());
        var attachment = _world.Document(parent: parent);

        await Decide(("sprk_document", attachment));

        _world.NarrowingAsked.Should().BeEquivalentTo(new[] { attachment }, "the parent is the attachment's 188 walk's to decide");
    }

    /// <summary>The closure keeps 188's chain bound itself: even when the document seam answers Visible, a parent-document
    /// chain past <see cref="ExternalDocumentNarrowing.MaxHops"/> hops is Undecided, never Visible.</summary>
    [Fact]
    public async Task TheClosureBoundsTheDocumentChain_EvenWhenTheSeamSaysVisible()
    {
        var matter = _world.Root();
        var communication = _world.Communication(internalOnly: false, _world.Thread(Open), matter);
        var chain = Chain(communication, length: 5, matter);
        var readers = _world.Readers() with
        {
            Documents = (ids, _) => Task.FromResult(DocumentNarrowingResult.NotNarrowed(
                ids.Select(id => new DocumentNarrowingRow(id, default, Guid.Empty, Guid.Empty)))),
        };

        var result = await ExternalVisibility.DecideAsync(
            new[] { ("sprk_document", chain[3]), ("sprk_document", chain[4]) }, readers, CancellationToken.None);

        result.For("sprk_document", chain[3]).Verdict.Should().Be(ExternalVisibilityVerdict.Visible);
        result.For("sprk_document", chain[4]).Reason.Should().Be(ExternalVisibilityReason.ChainTooLong);
    }

    // ── Own rules are dispatched on the declared marker ─────────────────────────────────────────────────────────

    /// <summary>The (table, rule) pairs the registry declares are exactly the ones the predicate implements: a rule declared on
    /// a new table, or a new rule, fails here before it can fault every decision that reaches it.</summary>
    [Fact]
    public void TheDeclaredOwnRules_AreExactlyTheOnesThePredicateImplements()
    {
        var declared = SecureChildLineage.Children.Values
            .Where(t => t.External.Kind == ExternalMarkerKind.Predicate)
            .Select(t => (t.External.Rule, Table: t.LogicalName.ToLowerInvariant()))
            .OrderBy(p => p.Rule)
            .ToList();

        declared.Should().Equal(ExternalVisibility.RuleTables.Select(kv => (kv.Key, kv.Value)).OrderBy(p => p.Key));
        ExternalVisibility.RuleTables.Keys.Should().BeEquivalentTo(
            Enum.GetValues<ExternalOwnRule>().Where(r => r != ExternalOwnRule.None), "every own rule has an implementation");
    }

    // ── The AI guard's two lists, now derived from the registry (results unchanged) ─────────────────────────────

    [Fact]
    public void TheGuardsRefusedContentTables_AreUnchanged()
    {
        ExternalCallerCommunicationGuard.RefusedContentTables.Should().BeEquivalentTo(
            "sprk_emailartifact", "sprk_attachmentartifact", "sprk_emailreviewlog", "sprk_affinity");
    }

    [Fact]
    public void TheGuardsDocumentDerivedTables_AreUnchanged()
    {
        var derived = ExternalCallerCommunicationGuard.DocumentDerivedTablesForTest;

        derived.Keys.Should().BeEquivalentTo("sprk_analysis", "sprk_analysisoutput");
        var analysis = derived["sprk_analysis"];
        analysis.EntitySet.Should().Be("sprk_analysises");
        analysis.IdColumn.Should().Be("sprk_analysisid");
        analysis.LinkValues.Should().Equal("_sprk_documentid_value", "_sprk_outputfileid_value", "_sprk_regardingdocument_value");
        analysis.Via.Should().BeNull();
        var output = derived["sprk_analysisoutput"];
        output.EntitySet.Should().Be("sprk_analysisoutputs");
        output.IdColumn.Should().Be("sprk_analysisoutputid");
        output.LinkValues.Should().Equal("_sprk_analysisid_value");
        output.Via.Should().Be("sprk_analysis");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A document linked to <paramref name="communication"/>, then <paramref name="length"/> - 1 documents each the
    /// child of the one before (each filed under <paramref name="matter"/> when given); every document has a current version.
    /// Index = hops below the linked document.</summary>
    private List<Guid> Chain(Guid communication, int length, Guid? matter = null)
    {
        var chain = new List<Guid> { _world.Document(communication: communication, matter: matter) };
        for (var i = 1; i < length; i++)
            chain.Add(_world.Document(parent: chain[i - 1], matter: matter));
        foreach (var document in chain)
            _world.Version(document);
        return chain;
    }

    /// <summary>
    /// An in-memory Dataverse: SDK rows for the lineage walk (each read recorded by table and projected through its column set)
    /// and OData rows for the documents, communications and threads (each read projected through its <c>$select</c>).
    /// </summary>
    private sealed class World
    {
        private readonly Dictionary<(string Table, Guid Id), Entity> _entities = new();
        private readonly Dictionary<Guid, RootRecordFlags> _rootFlags = new();
        private readonly Dictionary<Guid, string> _catalog = new();
        private readonly List<string> _entityReads = new();
        private readonly List<string> _rootFlagReads = new();
        private readonly List<Guid> _narrowingAsked = new();

        private readonly Dictionary<string, (string Key, List<Dictionary<string, JsonElement>> Rows)> _sets = new()
        {
            ["sprk_documents"] = ("sprk_documentid", new()),
            ["sprk_communications"] = ("sprk_communicationid", new()),
            ["sprk_communicationthreads"] = ("sprk_communicationthreadid", new()),
        };

        public Dictionary<Guid, InvoiceHiddenReason> InvoiceReasons { get; } = new();

        public string? LibraryDrive { get; set; } = Library;

        /// <summary>A table (SDK), an entity set (OData) or <c>rootflags</c> whose reads throw.</summary>
        public string? Fault { get; set; }

        public IReadOnlyList<string> EntityReads { get { lock (_entityReads) return _entityReads.ToList(); } }

        public IReadOnlyList<string> RootFlagReads { get { lock (_rootFlagReads) return _rootFlagReads.ToList(); } }

        public IReadOnlyList<Guid> NarrowingAsked { get { lock (_narrowingAsked) return _narrowingAsked.ToList(); } }

        public ExternalVisibility.Readers Readers()
        {
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken ct) => RetrieveMultiple(query, ct));
            entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, string[] _, CancellationToken _) =>
                {
                    var row = new Entity("sprk_recordtype_ref", id) { ["sprk_recordlogicalname"] = _catalog[id] };
                    return Task.FromResult(row);
                });

            return new ExternalVisibility.Readers(
                entities.Object,
                Query,
                (ids, ct) =>
                {
                    lock (_narrowingAsked) _narrowingAsked.AddRange(ids);
                    return ExternalDocumentNarrowing.ReadAndDecideAsync(ids, Query, ct);
                },
                (table, ids, ct) =>
                {
                    lock (_rootFlagReads) _rootFlagReads.Add(table);
                    if (Fault == "rootflags")
                        throw new InvalidOperationException("root flags read failed");
                    IReadOnlyDictionary<Guid, RootRecordFlags> flags = ids.ToDictionary(
                        id => id, id => _rootFlags.TryGetValue(id, out var f) ? f : RootRecordFlags.Unreadable);
                    return Task.FromResult(flags);
                },
                (ids, ct) => Task.FromResult(new InvoiceVisibilityResult(
                    ids, ids.Where(InvoiceReasons.ContainsKey).ToDictionary(id => id, id => InvoiceReasons[id]), faulted: false)),
                LibraryDrive,
                NullLogger.Instance);
        }

        private Task<EntityCollection> RetrieveMultiple(QueryExpression query, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_entityReads) _entityReads.Add(query.EntityName);
            if (query.EntityName == Fault)
                throw new InvalidOperationException($"read of {query.EntityName} failed");

            var ids = query.Criteria.Conditions.Single().Values.Cast<Guid>().ToHashSet();
            var columns = query.ColumnSet.Columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = _entities
                .Where(kv => kv.Key.Table == query.EntityName && ids.Contains(kv.Key.Id))
                .Select(kv =>
                {
                    var projected = new Entity(kv.Key.Table, kv.Key.Id);
                    foreach (var attribute in kv.Value.Attributes.Where(a => columns.Contains(a.Key)))
                        projected[attribute.Key] = attribute.Value;
                    return projected;
                })
                .ToList();
            return Task.FromResult(new EntityCollection(rows));
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> Query(string set, string odata, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (set == Fault)
                throw new InvalidOperationException($"read of {set} failed");

            var (key, rows) = _sets[set];
            var ids = Regex.Matches(odata, $@"{key} eq ([0-9a-fA-F-]{{36}})").Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var select = Regex.Match(odata, @"\$select=([^&]*)").Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<Dictionary<string, JsonElement>> hit = rows
                .Where(r => ids.Contains(r[key].GetString()!))
                .Select(r => r.Where(kv => select.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();
            return Task.FromResult(hit);
        }

        public Entity Entity(string table, Guid id) => _entities[(table, id)];

        public Guid Root(string table = "sprk_matter", bool restricted = false, bool limited = false, bool unreadable = false, bool secure = false)
        {
            var id = Guid.NewGuid();
            _rootFlags[id] = unreadable
                ? RootRecordFlags.Unreadable
                : new RootRecordFlags(IsSecure: secure, IsRestricted: restricted, IsLimited: limited);
            _ = table; // the root's table is the lookup's target; its flags are keyed by id
            return id;
        }

        /// <summary>A catalog row naming <paramref name="table"/>.</summary>
        public Guid RecordType(string table)
        {
            var id = Guid.NewGuid();
            _catalog[id] = table;
            return id;
        }

        public Guid Add(string table, params (string Column, Guid Id)[] lookups) => Add(table, id: null, lookups);

        public Guid Add(string table, Guid? id, params (string Column, Guid Id)[] lookups)
        {
            var rowId = id ?? Guid.NewGuid();
            var entity = new Entity(table, rowId);
            var registry = SecureChildLineage.Children[table];
            foreach (var (column, target) in lookups)
                entity[column] = new EntityReference(registry.Lookups[column], target);
            _entities[(table, rowId)] = entity;
            return rowId;
        }

        public Guid Add(string table, (Guid Type, string Core) corePair)
        {
            var id = Add(table);
            Entity(table, id)["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", corePair.Type);
            Entity(table, id)["sprk_corerecordid"] = corePair.Core;
            return id;
        }

        public Guid Thread(int privacy, Guid? matter = null)
        {
            var id = matter is { } m ? Add("sprk_communicationthread", ("sprk_regardingmatter", m)) : Add("sprk_communicationthread");
            _sets["sprk_communicationthreads"].Rows.Add(Json(new Dictionary<string, object?>
            {
                ["sprk_communicationthreadid"] = id.ToString(),
                ["sprk_privacystate"] = privacy,
            }));
            return id;
        }

        public Guid Communication(bool? internalOnly, Guid? thread, Guid? matter = null, JsonElement? flag = null)
        {
            var lookups = new List<(string, Guid)>();
            if (thread is { } t)
                lookups.Add(("sprk_communicationthread", t));
            if (matter is { } m)
                lookups.Add(("sprk_regardingmatter", m));
            var id = Add("sprk_communication", lookups.ToArray());
            var row = Json(new Dictionary<string, object?>
            {
                ["sprk_communicationid"] = id.ToString(),
                ["sprk_isinternalonly"] = internalOnly,
                ["_sprk_communicationthread_value"] = thread?.ToString(),
            });
            if (flag is { } f)
                row["sprk_isinternalonly"] = f;
            _sets["sprk_communications"].Rows.Add(row);
            return id;
        }

        public Guid Document(
            Guid? communication = null, Guid? parent = null, Guid? matter = null, int? permission = null, string? drive = null,
            Guid? id = null)
        {
            var lookups = new List<(string, Guid)>();
            if (communication is { } c)
                lookups.Add(("sprk_relatedcommunication", c));
            if (parent is { } p)
                lookups.Add(("sprk_parentdocument", p));
            if (matter is { } m)
                lookups.Add(("sprk_matter", m));
            var documentId = Add("sprk_document", id, lookups.ToArray());
            if (drive is not null)
                Entity("sprk_document", documentId)["sprk_graphdriveid"] = drive;
            _sets["sprk_documents"].Rows.Add(Json(new Dictionary<string, object?>
            {
                ["sprk_documentid"] = documentId.ToString(),
                ["sprk_documentname"] = "seeded",
                ["sprk_accesspermission"] = permission,
                ["_sprk_relatedcommunication_value"] = communication?.ToString(),
                ["_sprk_parentdocument_value"] = parent?.ToString(),
            }));
            return documentId;
        }

        /// <summary>A current version of <paramref name="document"/>: the document ↔ version path cycle.</summary>
        public Guid Version(Guid document)
        {
            var version = Add("sprk_fileversion", ("sprk_document", document));
            Entity("sprk_document", document)["sprk_currentversionid"] = new EntityReference("sprk_fileversion", version);
            return version;
        }

        private static Dictionary<string, JsonElement> Json(Dictionary<string, object?> values) =>
            values.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
    }
}
