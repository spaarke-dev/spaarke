using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045g (owner, EB-D-231, #2794), through the three app-only update paths — the playbook
/// update, the output orchestrator and the field-mapping push. Rule 1: an invoice's core record is fixed at create. Rule 2: an
/// approval, a submission, a correction and an external identifier filed under a core record regard their invoice's core record
/// (an external identifier filed under its invoice only keeps D-68). Rule 3: a line's invoice is never cleared. On a run as a
/// person, a refusal of these rules is decided again on the rows as that person reads them, and a row they cannot read is
/// "could not be read". Rule 1 and rule 3 on the lines are in the D-96 file; the derive in the resolver-fields file.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    /// <summary>The tables rule 2 compares with their invoice, each with the column naming that invoice.</summary>
    public static TheoryData<GenericPath, string, string> EveryPathAndInvoiceChild()
    {
        var data = new TheoryData<GenericPath, string, string>();
        foreach (var path in Enum.GetValues<GenericPath>())
        {
            data.Add(path, "sprk_invoiceapproval", "sprk_invoice");
            data.Add(path, "sprk_invoicesubmission", "sprk_invoice");
            data.Add(path, "sprk_invoicecorrection", "sprk_invoice");
            data.Add(path, "sprk_externalidentifier", InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn);
        }

        return data;
    }

    /// <summary>
    /// The app-only world of rule 2: the row (<see cref="FamilyRow"/>, under <see cref="Matter"/>, with
    /// <paramref name="storedInvoice"/> when set), <see cref="Invoice"/> under <see cref="Matter"/> and <see cref="OtherInvoice"/>
    /// under <see cref="OtherMatter"/>.
    /// </summary>
    private static Mock<IGenericEntityService> ChildWorld(string table, string invoiceColumn, Guid? storedInvoice = null)
    {
        var app = storedInvoice is { } stored
            ? FamilyRowUnderAMatter(table, (invoiceColumn, new EntityReference("sprk_invoice", stored)))
            : FamilyRowUnderAMatter(table);
        app.Setup(a => a.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = MatterRef(Matter) });
        app.Setup(a => a.RetrieveAsync("sprk_invoice", OtherInvoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", OtherInvoice) { ["sprk_matter"] = MatterRef(OtherMatter) });
        return app;
    }

    /// <summary>
    /// The submission's core lookups and its invoice are not written by the two app-only handlers when they are access columns
    /// there: those handlers refuse that write before the guard. Either way nothing is written.
    /// </summary>
    private static string ExpectedChildRefusal(GenericPath path, string table, string ruleCode, PathOutcome outcome) =>
        CoreLookupIsAnAccessColumnOn(path, table) && outcome.Code == InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode
            ? InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode
            : ruleCode;

    // ── Rule 2, must fire: a row given an invoice of another core record, or moved away from its invoice's ────────────

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceChild))]
    public async Task ARowGivenAnInvoiceOfAnotherMatter_IsRefusedLineMismatch_WithTheHint_AndNothingIsWritten(GenericPath path, string table, string invoiceColumn)
    {
        var outcome = await RunAsync(path, table, FamilyRow, Lookups((invoiceColumn, new EntityReference("sprk_invoice", OtherInvoice))), NoPlain,
            ChildWorld(table, invoiceColumn, storedInvoice: Invoice).Object);

        outcome.Code.Should().Be(ExpectedChildRefusal(path, table, InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode, outcome),
            outcome.Message);
        if (outcome.Code == InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode)
            outcome.Message.Should().Contain(ReturnAndResubmitHint);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceChild))]
    public async Task ARowMovedAwayFromItsInvoicesMatter_IsRefusedLineMismatch_AndNothingIsWritten(GenericPath path, string table, string invoiceColumn)
    {
        var outcome = await RunAsync(path, table, FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(OtherMatter))), NoPlain,
            ChildWorld(table, invoiceColumn, storedInvoice: Invoice).Object);

        outcome.Code.Should().Be(ExpectedChildRefusal(path, table, InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode, outcome),
            outcome.Message);
        NoGuid(outcome);
        NothingWritten();
        _deriveReads.Should().BeEmpty("a refused write derives nothing");
    }

    // ── Rule 2, must not fire ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceChild))]
    public async Task ARowGivenAnInvoiceOfItsOwnMatter_IsWritten(GenericPath path, string table, string invoiceColumn)
    {
        var outcome = await RunAsync(path, table, FamilyRow, Lookups((invoiceColumn, new EntityReference("sprk_invoice", Invoice))), NoPlain,
            ChildWorld(table, invoiceColumn).Object);

        if (CoreLookupIsAnAccessColumnOn(path, table) && outcome.Code == InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode)
        {
            NothingWritten();
            return;
        }

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce(table, FamilyRow);
    }

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceChild))]
    public async Task ARowWithItsInvoiceRenamedOnly_IsWritten_AndItsInvoiceIsNotRead(GenericPath path, string table, string invoiceColumn)
    {
        var app = ChildWorld(table, invoiceColumn, storedInvoice: OtherInvoice); // stored rows are not re-judged by a rename

        var outcome = await RunAsync(path, table, FamilyRow, NoLookups, RenameOnly, app.Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce(table, FamilyRow);
        app.Verify(a => a.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoicesubmission", "sprk_invoice")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoicesubmission", "sprk_invoice")]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_externalidentifier", "sprk_regardinginvoice")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_externalidentifier", "sprk_regardinginvoice")]
    public async Task ARowWhoseInvoiceIsCleared_IsNotRefusedByTheseRules(GenericPath path, string table, string invoiceColumn)
    {
        // Rules 3 and 3b keep the invoice of a line, an approval and a correction. A submission exists before its invoice (its
        // invoice is cleared when its commit is undone), and an external identifier may be filed under its core record alone,
        // so clearing theirs is not refused by these rules. The push cannot clear a lookup.
        var outcome = await RunAsync(path, table, FamilyRow, Lookups((invoiceColumn, null)), NoPlain,
            ChildWorld(table, invoiceColumn, storedInvoice: Invoice).Object);

        outcome.Code.Should().NotBe(InvoiceFamilyCoreRecordGuard.LineInvoiceClearedCode);
        outcome.Code.Should().NotBe(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        if (outcome.Code is null)
            WrittenOnce(table, FamilyRow);
        else
            outcome.Code.Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode, "only the handlers' access-column rule");
    }

    /// <summary>Rule 3b (owner, 2026-11-13): an approval's and a correction's invoice is never cleared after create.</summary>
    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoiceapproval")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoiceapproval")]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoicecorrection")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoicecorrection")]
    public async Task AnApprovalOrCorrectionWhoseInvoiceIsCleared_IsRefusedInvoiceCleared_AndNothingIsWritten(GenericPath path, string table)
    {
        var outcome = await RunAsync(path, table, FamilyRow, Lookups(("sprk_invoice", null)), NoPlain,
            ChildWorld(table, "sprk_invoice", storedInvoice: Invoice).Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceClearedCode, outcome.Message);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task ALineRePointedToAnotherInvoiceOfItsOwnMatter_IsWritten(GenericPath path)
    {
        var app = WorldWithLines();
        app.Setup(a => a.RetrieveAsync("sprk_invoice", OtherInvoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", OtherInvoice) { ["sprk_matter"] = MatterRef(Matter) });

        var outcome = await RunAsync(path, "sprk_billingevent", Line, Lookups(("sprk_invoice", new EntityReference("sprk_invoice", OtherInvoice))),
            NoPlain, app.Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_billingevent", Line);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnInvoiceUpdateTouchingNoCoreLookup_IsWritten(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoice", Invoice, NoLookups, RenameOnly, InvoiceUnderAMatter().Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_invoice", Invoice);
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task ARowGivenAnInvoiceThatCannotBeRead_IsRefusedReadFailed_AndNothingIsWritten(GenericPath path)
    {
        var app = ChildWorld(Approval, "sprk_invoice");
        app.Setup(a => a.RetrieveAsync("sprk_invoice", OtherInvoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("socket 10.0.0.9 timed out"));

        var outcome = await RunAsync(path, Approval, FamilyRow, Lookups(("sprk_invoice", new EntityReference("sprk_invoice", OtherInvoice))),
            NoPlain, app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        outcome.Message.Should().NotContain("10.0.0.9");
        NothingWritten();
    }

    [Fact]
    public async Task ACancellationWhileTheInvoiceIsRead_IsRethrown_AndNothingIsWritten()
    {
        using var cancel = new CancellationTokenSource();
        var app = ChildWorld(Approval, "sprk_invoice");
        app.Setup(a => a.RetrieveAsync("sprk_invoice", OtherInvoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancel.Cancel();
                throw new OperationCanceledException(cancel.Token);
            });

        var act = () => Handler(app.Object).UpdateAsync(
            Approval, FamilyRow, new Dictionary<string, object?> { ["sprk_invoice"] = new EntityReference("sprk_invoice", OtherInvoice) },
            Sprk.Bff.Api.Services.Ai.ConcurrencyMode.None, maxRetries: 1, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        NothingWritten();
    }

    // ── A run as a person: no refusal of these rules turns on a row the person cannot read ─────────────────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)] // the push always writes as its caller
    public async Task OnAPersonsRun_AnInvoiceMove_IsRefusedCoreRecordFixed_WhenThePersonReadsTheInvoice_AndReadFailedWhenNot(GenericPath path)
    {
        _personRows = (table, id) => table == "sprk_invoice" && id == Invoice
            ? new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = MatterRef(Matter) }
            : null;
        var reads = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines().Object, playbookActsAs: Caller);
        _personRowReads.Should().Contain($"sprk_invoice:{Invoice:D}", "the refusal is decided again on the row as the person reads it");

        _personRows = (_, _) => null;
        var cannotRead = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines().Object, playbookActsAs: Caller);

        reads.Code.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode);
        cannotRead.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        NoGuid(cannotRead);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task OnAPersonsRun_ARowGivenAnInvoiceOfAnotherMatter_IsRefusedLineMismatch_OnlyWhenThePersonReadsThatInvoice(GenericPath path)
    {
        var otherInvoiceReadable = true;
        _personRows = (table, id) => (table, id) switch
        {
            ("sprk_invoiceapproval", var row) when row == FamilyRow =>
                new Entity(Approval, FamilyRow) { ["sprk_regardingmatter"] = MatterRef(Matter), ["sprk_invoice"] = new EntityReference("sprk_invoice", Invoice) },
            ("sprk_invoice", var invoice) when invoice == OtherInvoice && otherInvoiceReadable =>
                new Entity("sprk_invoice", OtherInvoice) { ["sprk_matter"] = MatterRef(OtherMatter) },
            _ => null,
        };

        var reads = await RunAsync(path, Approval, FamilyRow, Lookups(("sprk_invoice", new EntityReference("sprk_invoice", OtherInvoice))), NoPlain,
            ChildWorld(Approval, "sprk_invoice", storedInvoice: Invoice).Object, playbookActsAs: Caller);
        otherInvoiceReadable = false;
        var cannotRead = await RunAsync(path, Approval, FamilyRow, Lookups(("sprk_invoice", new EntityReference("sprk_invoice", OtherInvoice))), NoPlain,
            ChildWorld(Approval, "sprk_invoice", storedInvoice: Invoice).Object, playbookActsAs: Caller);

        reads.Code.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        cannotRead.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "never a refusal that turns on a row the person cannot read");
        NoGuid(cannotRead);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task OnAPersonsRun_ALinesInvoiceCleared_IsRefusedLineInvoiceCleared_WhenThePersonReadsTheLine_AndReadFailedWhenNot(GenericPath path)
    {
        _personRows = (table, id) => table == "sprk_billingevent" && id == Line
            ? new Entity("sprk_billingevent", Line) { ["sprk_matter"] = MatterRef(Matter), ["sprk_invoice"] = new EntityReference("sprk_invoice", Invoice) }
            : null;
        var reads = await RunAsync(path, "sprk_billingevent", Line, Lookups(("sprk_invoice", null)), NoPlain, WorldWithLines().Object,
            playbookActsAs: Caller);

        _personRows = (_, _) => throw new TimeoutException("socket 10.0.0.10 timed out");
        var faulted = await RunAsync(path, "sprk_billingevent", Line, Lookups(("sprk_invoice", null)), NoPlain, WorldWithLines().Object,
            playbookActsAs: Caller);

        reads.Code.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceClearedCode);
        faulted.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        faulted.Message.Should().NotContain("10.0.0.10");
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoiceapproval")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoiceapproval")]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoicecorrection")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoicecorrection")]
    public async Task OnAPersonsRun_AnApprovalOrCorrectionsInvoiceCleared_IsRefusedInvoiceCleared_WhenThePersonReadsTheRow_AndReadFailedWhenNot(
        GenericPath path, string table)
    {
        _personRows = (t, id) => t == table && id == FamilyRow
            ? new Entity(table, FamilyRow) { ["sprk_regardingmatter"] = MatterRef(Matter), ["sprk_invoice"] = new EntityReference("sprk_invoice", Invoice) }
            : null;
        var reads = await RunAsync(path, table, FamilyRow, Lookups(("sprk_invoice", null)), NoPlain,
            ChildWorld(table, "sprk_invoice", storedInvoice: Invoice).Object, playbookActsAs: Caller);

        _personRows = (_, _) => null;
        var cannotRead = await RunAsync(path, table, FamilyRow, Lookups(("sprk_invoice", null)), NoPlain,
            ChildWorld(table, "sprk_invoice", storedInvoice: Invoice).Object, playbookActsAs: Caller);

        reads.Code.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceClearedCode);
        cannotRead.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        NoGuid(cannotRead);
        NothingWritten();
    }

    // ── Fix round 2: on a run as a person, an invoice the write sets is read as that person before any rule compares it ──

    /// <summary>The person reads <see cref="Invoice"/> (under the row's own matter) only when <paramref name="readable"/>, and
    /// never <see cref="OtherInvoice"/>; every other row as the app does not matter here (none is read).</summary>
    private void ThePersonReads(bool readable) =>
        _personRows = (table, id) => table == "sprk_invoice" && id == Invoice && readable
            ? new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = MatterRef(Matter) }
            : null;

    /// <summary>Every path, each family table with the column naming its invoice.</summary>
    public static TheoryData<GenericPath, string, string> EveryPathAndInvoiceReference()
    {
        var data = new TheoryData<GenericPath, string, string>();
        foreach (var path in Enum.GetValues<GenericPath>())
        {
            data.Add(path, "sprk_invoiceapproval", "sprk_invoice");
            data.Add(path, "sprk_invoicecorrection", "sprk_invoice");
            data.Add(path, "sprk_invoicesubmission", "sprk_invoice");
            data.Add(path, "sprk_externalidentifier", InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceReference))]
    public async Task OnAPersonsRun_ARowGivenAnInvoiceOfItsOwnMatterThePersonCannotRead_IsRefusedReadFailed_AndNothingIsWritten(
        GenericPath path, string table, string invoiceColumn)
    {
        ThePersonReads(readable: false);

        var outcome = await RunAsync(path, table, FamilyRow, Lookups((invoiceColumn, new EntityReference("sprk_invoice", Invoice))), NoPlain,
            ChildWorld(table, invoiceColumn).Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(ExpectedChildRefusal(path, table, InvoiceFamilyCoreRecordGuard.ReadFailedCode, outcome), outcome.Message);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceReference))]
    public async Task OnAPersonsRun_ARowGivenAnInvoiceOfAnotherMatterThePersonCannotRead_IsRefusedReadFailed_WithoutReadingThatInvoiceAsTheApp(
        GenericPath path, string table, string invoiceColumn)
    {
        // The person reads the row itself; only the invoice it is given is one they cannot read.
        _personRows = (t, id) => t == table && id == FamilyRow
            ? new Entity(table, FamilyRow) { ["sprk_regardingmatter"] = MatterRef(Matter) }
            : null;
        var app = ChildWorld(table, invoiceColumn);

        var outcome = await RunAsync(path, table, FamilyRow, Lookups((invoiceColumn, new EntityReference("sprk_invoice", OtherInvoice))), NoPlain,
            app.Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(ExpectedChildRefusal(path, table, InvoiceFamilyCoreRecordGuard.ReadFailedCode, outcome), outcome.Message);
        NoGuid(outcome);
        NothingWritten();
        app.Verify(a => a.RetrieveAsync("sprk_invoice", OtherInvoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "an invoice the person cannot read is never compared for them");
        if (outcome.Code == InvoiceFamilyCoreRecordGuard.ReadFailedCode)
            _personRowReads.Should().Equal($"sprk_invoice:{OtherInvoice:D}");
    }

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceReference))]
    public async Task OnAPersonsRun_ARowGivenAnInvoiceOfItsOwnMatterThePersonReads_IsWritten_ReadingOnlyThatInvoiceAsThePerson(
        GenericPath path, string table, string invoiceColumn)
    {
        ThePersonReads(readable: true);

        var outcome = await RunAsync(path, table, FamilyRow, Lookups((invoiceColumn, new EntityReference("sprk_invoice", Invoice))), NoPlain,
            ChildWorld(table, invoiceColumn).Object, playbookActsAs: Caller);

        if (CoreLookupIsAnAccessColumnOn(path, table) && outcome.Code == InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode)
        {
            NothingWritten();
            return;
        }

        outcome.Code.Should().BeNull(outcome.Message);
        _personRowReads.Should().Equal($"sprk_invoice:{Invoice:D}");
        WrittenOnce(table, FamilyRow);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task OnAPersonsRun_AWriteThatSetsNoInvoice_ReadsNoRowAsThePerson(GenericPath path)
    {
        _personRows = (_, _) => throw new InvalidOperationException("must not be read");

        var outcome = await RunAsync(path, Approval, FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(Matter))), NoPlain,
            ChildWorld(Approval, "sprk_invoice").Object, playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        _personRowReads.Should().BeEmpty();
        WrittenOnce(Approval, FamilyRow);
    }

    // ── Fix round 3: the stored invoice of a row whose core lookup the write sets, and an invoice's superseded invoice ──

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceReference))]
    public async Task OnAPersonsRun_ARowFiledUnderItsInvoicesMatterWhoseStoredInvoiceThePersonCannotRead_IsRefusedReadFailed_AndNothingIsWritten(
        GenericPath path, string table, string invoiceColumn)
    {
        ThePersonReads(readable: false);

        var outcome = await RunAsync(path, table, FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(Matter))), NoPlain,
            ChildWorld(table, invoiceColumn, storedInvoice: Invoice).Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(ExpectedChildRefusal(path, table, InvoiceFamilyCoreRecordGuard.ReadFailedCode, outcome), outcome.Message);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryPathAndInvoiceReference))]
    public async Task OnAPersonsRun_ARowFiledUnderItsInvoicesMatterWhoseStoredInvoiceThePersonReads_IsWritten(
        GenericPath path, string table, string invoiceColumn)
    {
        ThePersonReads(readable: true);

        var outcome = await RunAsync(path, table, FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(Matter))), NoPlain,
            ChildWorld(table, invoiceColumn, storedInvoice: Invoice).Object, playbookActsAs: Caller);

        if (CoreLookupIsAnAccessColumnOn(path, table) && outcome.Code == InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode)
        {
            NothingWritten();
            return;
        }

        outcome.Code.Should().BeNull(outcome.Message);
        _personRowReads.Should().Equal($"sprk_invoice:{Invoice:D}");
        WrittenOnce(table, FamilyRow);
    }

    /// <summary>An invoice of <see cref="Matter"/>, the stored invoice's own matter, that an invoice may supersede.</summary>
    private static readonly Guid SupersededOfTheSameMatter = Guid.Parse("02200000-0000-4000-8000-0000000000e1");

    private static Mock<IGenericEntityService> InvoiceWithASupersededInvoiceOfTheSameMatter()
    {
        var app = InvoiceUnderAMatter();
        app.Setup(a => a.RetrieveAsync("sprk_invoice", SupersededOfTheSameMatter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", SupersededOfTheSameMatter) { ["sprk_matter"] = MatterRef(Matter) });
        return app;
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task OnAPersonsRun_AnInvoiceGivenASupersededInvoiceOfItsMatterThePersonCannotRead_IsRefusedReadFailed_AndNothingIsWritten(
        GenericPath path)
    {
        _personRows = (table, id) => table == "sprk_invoice" && id == Invoice
            ? new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = MatterRef(Matter) }
            : null;

        var outcome = await RunAsync(path, "sprk_invoice", Invoice,
            Lookups((InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn, new EntityReference("sprk_invoice", SupersededOfTheSameMatter))), NoPlain,
            InvoiceWithASupersededInvoiceOfTheSameMatter().Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, outcome.Message);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task OnAPersonsRun_AnInvoiceGivenASupersededInvoiceOfItsMatterThePersonReads_IsWritten(GenericPath path)
    {
        _personRows = (table, id) => table == "sprk_invoice" && (id == Invoice || id == SupersededOfTheSameMatter)
            ? new Entity("sprk_invoice", id) { ["sprk_matter"] = MatterRef(Matter) }
            : null;

        var outcome = await RunAsync(path, "sprk_invoice", Invoice,
            Lookups((InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn, new EntityReference("sprk_invoice", SupersededOfTheSameMatter))), NoPlain,
            InvoiceWithASupersededInvoiceOfTheSameMatter().Object, playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        _personRowReads.Should().Equal($"sprk_invoice:{SupersededOfTheSameMatter:D}");
        WrittenOnce("sprk_invoice", Invoice);
    }

    // ── Fix round 3: the person's read of the invoice faults, or the request is cancelled during it ──────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task OnAPersonsRun_WhenThePersonsReadOfTheInvoiceFaults_IsRefusedReadFailed_AndNothingIsWritten(GenericPath path)
    {
        _personRows = (_, _) => throw new TimeoutException("socket 10.0.0.12 timed out");

        var outcome = await RunAsync(path, Approval, FamilyRow, Lookups(("sprk_invoice", new EntityReference("sprk_invoice", Invoice))), NoPlain,
            ChildWorld(Approval, "sprk_invoice").Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, outcome.Message);
        outcome.Message.Should().NotContain("10.0.0.12");
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task OnAPersonsRun_ACancellationDuringThePersonsReadOfTheInvoice_IsRethrown_AndNothingIsWritten(GenericPath path)
    {
        using var cancel = new CancellationTokenSource();
        _personRows = (_, _) =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        };

        var act = () => RunAsync(path, Approval, FamilyRow, Lookups(("sprk_invoice", new EntityReference("sprk_invoice", Invoice))), NoPlain,
            ChildWorld(Approval, "sprk_invoice").Object, playbookActsAs: Caller, ct: cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        NothingWritten();
    }
}
