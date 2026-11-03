using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

/// <summary>
/// ebilling-r1 task 026 fix-round-2. D-96 (owner, on uac-r2's 026 re-check F1): an update that moves an invoice to another
/// core record is refused while any of its lines names another one (or none), counted with the APP-only reader, failing
/// closed. DEF-022 (#2056): <see cref="InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync"/>, the same rule for the generic
/// update paths that do not write through <c>OwnedChildWrite</c>.
/// </summary>
public class InvoiceFamilyCoreRecordGuardLineSplitTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherMatterId = Guid.Parse("11111111-1111-1111-1111-1111111111ff");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid InvoiceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherInvoiceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid LineId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static Func<string, object?> Reads(params (string Column, object? Value)[] values) =>
        column => values.FirstOrDefault(v => v.Column == column).Value;

    private static readonly Func<EntityReference, CancellationToken, Task<InvoiceFamilyCoreRecord>> NeverRead =
        (_, _) => throw new InvalidOperationException("must not be read");

    private static Entity Line(Guid invoice, params (string Column, string Core, Guid Id)[] cores)
    {
        var line = new Entity("sprk_billingevent", Guid.NewGuid()) { ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice) };
        foreach (var (column, core, id) in cores)
            line[column] = new EntityReference(core, id);
        return line;
    }

    /// <summary>An app-only reader whose RetrieveMultiple evaluates the query over <paramref name="lines"/>.</summary>
    internal static Mock<IGenericEntityService> AppOnlyOver(IEnumerable<Entity> lines, List<QueryExpression>? asked = null)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                asked?.Add(q);
                return new EntityCollection(QueryEvaluator.Run(q, lines).ToList());
            });
        return app;
    }

    // ── D-96 in CheckWriteAsync ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckWrite_AnInvoiceMovedWhileALineNamesAnotherCoreRecord_IsRefusedLineMismatch_NamingNoLine()
    {
        EntityReference? askedFor = null;
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_project", new EntityReference("sprk_project", ProjectId))), NeverRead, default,
            (core, _) => { askedFor = core; return Task.FromResult(true); });

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        result.Reason.Should().NotContain(LineId.ToString("D"));
        askedFor.Should().BeEquivalentTo(new EntityReference("sprk_project", ProjectId), "the invoice's NEW single core record");
    }

    [Fact]
    public async Task CheckWrite_AnInvoiceMovedWhoseLinesAllMatch_OrThatHasNoLines_IsDecided()
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_project", new EntityReference("sprk_project", ProjectId))), NeverRead, default,
            (_, _) => Task.FromResult(false));

        result.IsRefused.Should().BeFalse(result.Reason);
        result.Regarding.Should().BeEquivalentTo(new EntityReference("sprk_project", ProjectId));
    }

    [Fact]
    public async Task CheckWrite_WhenTheLinesCannotBeRead_FailsClosedReadFailed_WithoutTheFaultsText()
    {
        var thrown = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId))), NeverRead, default,
            (_, _) => throw new HttpRequestException("socket 10.0.0.7"));
        var noReader = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId))), NeverRead, default,
            InvoiceFamilyCoreRecordGuard.LinesOfAnotherCoreRecord(null, InvoiceId));

        thrown.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        thrown.Reason.Should().NotContain("10.0.0.7");
        noReader.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "no app-only reader is never \"no lines\"");
    }

    [Fact]
    public async Task CheckWrite_TheLineCheck_IsNeverAskedForARefusedInvoice_OrForAnyOtherTable()
    {
        Func<EntityReference, CancellationToken, Task<bool>> never = (_, _) => throw new InvalidOperationException("must not be asked");

        var twoCores = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)), ("sprk_project", new EntityReference("sprk_project", ProjectId))),
            NeverRead, default, never);
        var line = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId))), NeverRead, default, never);

        twoCores.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        line.IsRefused.Should().BeFalse(line.Reason);
    }

    // ── D-96: the one bounded app-only query ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinesOfAnotherCoreRecord_FindsOnlyThisInvoicesLinesThatAreNotExactlyTheNewCore_WithOneBoundedQuery()
    {
        var matching = Line(InvoiceId, ("sprk_project", "sprk_project", ProjectId));
        var otherInvoicesStrayLine = Line(OtherInvoiceId, ("sprk_matter", "sprk_matter", MatterId));
        var asked = new List<QueryExpression>();

        var clean = await InvoiceFamilyCoreRecordGuard.LinesOfAnotherCoreRecord(
            AppOnlyOver([matching, otherInvoicesStrayLine], asked).Object, InvoiceId)(new EntityReference("sprk_project", ProjectId), default);

        clean.Should().BeFalse("a matching line and another invoice's line are not this invoice's split");
        var query = asked.Should().ContainSingle().Subject;
        query.EntityName.Should().Be("sprk_billingevent");
        query.TopCount.Should().Be(1);
        query.ColumnSet.Columns.Should().Equal("sprk_billingeventid");

        foreach (var split in new[]
                 {
                     Line(InvoiceId, ("sprk_matter", "sprk_matter", MatterId)), // the old matter
                     Line(InvoiceId, ("sprk_project", "sprk_project", Guid.NewGuid())), // another project
                     Line(InvoiceId), // no core record
                     Line(InvoiceId, ("sprk_project", "sprk_project", ProjectId), ("sprk_matter", "sprk_matter", OtherMatterId)), // two
                     Line(InvoiceId, ("sprk_regardingworkassignment", "sprk_workassignment", ProjectId)), // same id, another table
                 })
        {
            var found = await InvoiceFamilyCoreRecordGuard.LinesOfAnotherCoreRecord(
                AppOnlyOver([matching, split]).Object, InvoiceId)(new EntityReference("sprk_project", ProjectId), default);
            found.Should().BeTrue();
        }
    }

    [Fact]
    public void WritesCoreLookup_IsTrueForACoreColumnOrItsBind_NeverForSupersedesOrText()
    {
        InvoiceFamilyCoreRecordGuard.WritesCoreLookup("sprk_invoice", ["sprk_Project@odata.bind"]).Should().BeTrue();
        InvoiceFamilyCoreRecordGuard.WritesCoreLookup("sprk_invoice", ["sprk_matter"]).Should().BeTrue();
        InvoiceFamilyCoreRecordGuard.WritesCoreLookup("sprk_invoice", ["sprk_SupersedesInvoice@odata.bind", "sprk_name"]).Should().BeFalse();
        InvoiceFamilyCoreRecordGuard.WritesCoreLookup("sprk_todo", ["sprk_matter"]).Should().BeFalse();
    }

    // ── DEF-022: the generic update paths ────────────────────────────────────────────────────────────────────────

    private static Mock<IGenericEntityService> StoredInvoiceUnderMatter(IEnumerable<Entity>? lines = null)
    {
        var app = AppOnlyOver(lines ?? []);
        app.Setup(a => a.RetrieveAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });
        return app;
    }

    private static Dictionary<string, object?> MoveToProject() => new()
    {
        ["sprk_Matter@odata.bind"] = null,
        ["sprk_Project@odata.bind"] = $"/sprk_projects({ProjectId:D})",
    };

    [Fact]
    public async Task GenericUpdate_ATableOutsideTheTwo_IsNotChecked_AndReadsNothing()
    {
        var strict = new Mock<IGenericEntityService>(MockBehavior.Strict);

        (await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_todo", InvoiceId,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) }, strict.Object))
            .Should().BeNull();
        (await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_todo", InvoiceId,
            new Dictionary<string, object?> { ["sprk_name"] = "x" }, appOnly: null))
            .Should().BeNull("another table is the upsert fix's other half (#2081), not this guard's");
    }

    // ── #2081 (ebilling-r1 follow-up): the generic paths PATCH with no If-Match, which is an UPSERT. For the invoice family
    //    the stored row is read for EVERY write, so a stale or deleted id is refused instead of creating an orphan row. ───

    private static FaultException<OrganizationServiceFault> NoSuchRow() =>
        new(new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist"));

    private static Dictionary<string, object?> Plain() => new() { ["sprk_name"] = "Renamed", ["sprk_totalamount"] = 1m };

    private static Dictionary<string, object?> ProjectLookup() => new() { ["sprk_project"] = new EntityReference("sprk_project", ProjectId) };

    public static TheoryData<string, bool, bool> OrphanCases()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var table in new[] { "sprk_invoice", "sprk_billingevent" })
            foreach (var answersNull in new[] { true, false })       // a reader that returns null / one that throws object-does-not-exist
                foreach (var touchesLookup in new[] { true, false }) // the patch sets a guarded lookup / only text
                    data.Add(table, answersNull, touchesLookup);
        return data;
    }

    [Theory]
    [MemberData(nameof(OrphanCases))]
    public async Task GenericUpdate_ARowThatDoesNotExist_IsRefusedRecordNotFound_WhateverThePatchTouches(
        string table, bool answersNull, bool touchesLookup)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var setup = app.Setup(a => a.RetrieveAsync(table, InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (answersNull) setup.ReturnsAsync((Entity)null!); else setup.ThrowsAsync(NoSuchRow());

        var result = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(table, InvoiceId, touchesLookup ? ProjectLookup() : Plain(), app.Object);

        result!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        app.Verify(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_billingevent")]
    public async Task GenericUpdate_ARowThatExists_AndAPatchTouchingNoGuardedLookup_IsAllowed_AfterOneRead(string table)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveAsync(table, InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(table, InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });

        (await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(table, InvoiceId, Plain(), app.Object)).Should().BeNull();

        app.Verify(a => a.RetrieveAsync(table, InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("sprk_invoice", true)]
    [InlineData("sprk_invoice", false)]
    [InlineData("sprk_billingevent", true)]
    [InlineData("sprk_billingevent", false)]
    public async Task GenericUpdate_WhenTheRowCannotBeRead_FailsClosedReadFailed_EvenForAPatchTouchingNoLookup(string table, bool noReader)
    {
        var faulting = new Mock<IGenericEntityService>(MockBehavior.Strict);
        faulting.Setup(a => a.RetrieveAsync(table, InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("socket 10.0.0.7"));

        var result = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(table, InvoiceId, Plain(), noReader ? null : faulting.Object);

        result!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "a fault is never \"the row exists\"");
        result.Reason.Should().NotContain("10.0.0.7");
    }

    // ── #2081 fix round 2: the existence oracle. The guard reads as the APP before any check of the user a write acts as,
    //    so for a write that runs impersonated "not found" must look like "cannot be read". ─────────────────────────────

    [Theory]
    [MemberData(nameof(OrphanCases))]
    public async Task GenericUpdate_ForAnImpersonatedWrite_ARowThatDoesNotExist_IsReadFailed_NotNotFound(
        string table, bool answersNull, bool touchesLookup)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var setup = app.Setup(a => a.RetrieveAsync(table, InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (answersNull) setup.ReturnsAsync((Entity)null!); else setup.ThrowsAsync(NoSuchRow());

        var impersonated = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(
            table, InvoiceId, touchesLookup ? ProjectLookup() : Plain(), app.Object, writeIsImpersonated: true);
        var appOnly = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(
            table, InvoiceId, touchesLookup ? ProjectLookup() : Plain(), app.Object);

        impersonated!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        impersonated.Reason.Should().NotContain(InvoiceId.ToString("D"));
        appOnly!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode, "an app-only write hides nothing");
    }

    [Fact]
    public async Task GenericUpdate_ForAnImpersonatedWrite_TheOtherInvoiceNotFound_IsReadFailed_AndAnyOtherRefusalIsUnchanged()
    {
        var app = StoredInvoiceUnderMatter();
        app.Setup(a => a.RetrieveAsync("sprk_invoice", OtherInvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NoSuchRow());
        var supersedes = new Dictionary<string, object?> { ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({OtherInvoiceId:D})" };

        var impersonated = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(
            "sprk_invoice", InvoiceId, supersedes, app.Object, writeIsImpersonated: true);
        var second = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync(
            "sprk_invoice", InvoiceId, ProjectLookup(), app.Object, writeIsImpersonated: true);

        impersonated!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        second!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
    }

    // ── #2081 fix round 2: check-then-act. The write itself is update-only, and a vanished row is a refusal ─────────────

    [Theory]
    [InlineData(false, false, InvoiceFamilyCoreRecordGuard.RecordNotFoundCode)]
    [InlineData(true, false, InvoiceFamilyCoreRecordGuard.RecordNotFoundCode)]
    [InlineData(true, true, InvoiceFamilyCoreRecordGuard.ReadFailedCode)]
    public async Task UpdateExisting_ARowGoneBetweenTheCheckAndTheWrite_IsACodeNotACreate(bool asUser, bool hide, string expected)
    {
        var records = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        Guid? caller = asUser ? Guid.NewGuid() : null;
        records.Setup(r => r.UpdateExistingRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), caller, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var code = await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(records.Object, "sprk_invoice", InvoiceId, Plain(), caller, hide, default);

        code.Should().Be(expected);
        records.Verify(r => r.UpdateRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(),
            It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never, "the upsert is never the fallback");
    }

    [Fact]
    public async Task UpdateExisting_AWrittenRow_IsNull_AndAnyOtherFaultPropagates()
    {
        var records = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        records.SetupSequence(r => r.UpdateExistingRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new TimeoutException());

        (await InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(records.Object, "sprk_invoice", InvoiceId, Plain(), null, false, default)).Should().BeNull();
        var act = () => InvoiceFamilyCoreRecordGuard.UpdateExistingAsync(records.Object, "sprk_invoice", InvoiceId, Plain(), null, false, default);

        await act.Should().ThrowAsync<TimeoutException>("a fault is not \"gone\"");
    }

    [Theory]
    [InlineData("sprk_invoice", true)]
    [InlineData("SPRK_BILLINGEVENT", true)]
    [InlineData("sprk_todo", false)]
    [InlineData("sprk_invoicesubmission", false)]
    [InlineData("", false)]
    public void IsUpdateOnlyTarget_IsTheTwoGuardedTables_AndNoOther(string table, bool expected) =>
        InvoiceFamilyCoreRecordGuard.IsUpdateOnlyTarget(table).Should().Be(expected);

    // ── The detach, move, re-attach hint (#2081 follow-up): a stable code plus a fixed sentence, no user data ─────────

    private const string HintFragment =
        "To move an invoice that has lines, or a line of one, detach each line (clear its invoice), move the invoice, move each line to the same matter, project or work assignment, "
        + "then re-attach the lines to the invoice.";

    [Fact]
    public async Task TheLineMismatchRefusals_CarryTheDetachMoveReattachHint_AndTheStableCode()
    {
        var invoiceMoved = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId, MoveToProject(),
            StoredInvoiceUnderMatter([Line(InvoiceId, ("sprk_matter", "sprk_matter", MatterId))]).Object);

        invoiceMoved!.RefusalCode.Should().Be("invoice_family_line_invoice_core_record_mismatch");
        invoiceMoved.Reason.Should().Contain("Nothing was written.").And.EndWith(HintFragment);
        InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage(invoiceMoved.RefusalCode!).Should().Be(
            "The invoice-family core-record rule refused this write (invoice_family_line_invoice_core_record_mismatch); "
            + "nothing was written. " + HintFragment);
    }

    [Fact]
    public void ACodeOnlyRefusal_OfAnyOtherCode_CarriesNoHint()
    {
        InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode).Should().Be(
            "The invoice-family core-record rule refused this write (invoice_family_record_not_found); nothing was written.");
    }

    [Fact]
    public async Task GenericUpdate_AnInvoiceUnderAMatterGivenAProjectToo_IsRefusedMultiple()
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId,
            new Dictionary<string, object?> { ["sprk_project"] = new EntityReference("sprk_project", ProjectId) },
            StoredInvoiceUnderMatter().Object);

        result!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode, "the stored matter stays");
    }

    [Fact]
    public async Task GenericUpdate_AnInvoiceMovedToAProject_IsRefusedWhileALineStaysOnTheMatter_AndAllowedWithNoSuchLine()
    {
        var split = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId, MoveToProject(),
            StoredInvoiceUnderMatter([Line(InvoiceId, ("sprk_matter", "sprk_matter", MatterId))]).Object);
        var matching = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId, MoveToProject(),
            StoredInvoiceUnderMatter([Line(InvoiceId, ("sprk_project", "sprk_project", ProjectId))]).Object);
        var noLines = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId, MoveToProject(),
            StoredInvoiceUnderMatter().Object);

        split!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        matching.Should().BeNull();
        noLines.Should().BeNull();
    }

    [Fact]
    public async Task GenericUpdate_WhenAReadIsNotPossible_FailsClosed()
    {
        var faulting = new Mock<IGenericEntityService>(MockBehavior.Strict);
        faulting.Setup(a => a.RetrieveAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("socket 10.0.0.7"));

        var noReader = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId, MoveToProject(), null);
        var thrown = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId, MoveToProject(), faulting.Object);

        noReader!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        thrown!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        thrown.Reason.Should().NotContain("10.0.0.7");
    }

    [Fact]
    public async Task GenericUpdate_ALineMovedToAnInvoiceOfAnotherMatter_IsRefusedLineMismatch_AndAPlainValueIsNeverALookup()
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveAsync("sprk_billingevent", LineId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_billingevent", LineId) { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });
        app.Setup(a => a.RetrieveAsync("sprk_invoice", OtherInvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", OtherInvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatterId) });

        var moved = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_billingevent", LineId,
            new Dictionary<string, object?> { ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({OtherInvoiceId:D})" }, app.Object);
        var plain = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_billingevent", LineId,
            new Dictionary<string, object?> { ["sprk_project"] = ProjectId.ToString("D") }, app.Object);

        moved!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        plain!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode);
    }
}

/// <summary>
/// Evaluates the subset of <see cref="QueryExpression"/> the D-96 line query uses (Equal, NotEqual, Null, NotNull on lookup
/// columns; nested And / Or filters; TopCount) over in-memory rows, with Dataverse's SQL semantics: NotEqual never matches
/// an empty column.
/// </summary>
internal static class QueryEvaluator
{
    public static IEnumerable<Entity> Run(QueryExpression query, IEnumerable<Entity> rows) =>
        rows.Where(r => string.Equals(r.LogicalName, query.EntityName, StringComparison.OrdinalIgnoreCase) && Matches(query.Criteria, r))
            .Take(query.TopCount ?? int.MaxValue);

    private static bool Matches(FilterExpression filter, Entity row)
    {
        var results = filter.Conditions.Select(c => Matches(c, row)).Concat(filter.Filters.Select(f => Matches(f, row))).ToList();
        return filter.FilterOperator == LogicalOperator.Or ? results.Any(x => x) : results.All(x => x);
    }

    private static bool Matches(ConditionExpression condition, Entity row)
    {
        Guid? value = row.Attributes.TryGetValue(condition.AttributeName, out var raw) && raw is EntityReference r && r.Id != Guid.Empty
            ? r.Id
            : null;
        return condition.Operator switch
        {
            ConditionOperator.Null => value is null,
            ConditionOperator.NotNull => value is not null,
            ConditionOperator.Equal => value is { } v && v == (Guid)condition.Values[0],
            ConditionOperator.NotEqual => value is { } v && v != (Guid)condition.Values[0],
            _ => throw new NotSupportedException(condition.Operator.ToString()),
        };
    }
}
