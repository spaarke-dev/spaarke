using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 ruling on RecordFamilyEdges (2026-10-28), end to end through each generic update path: an invoice
/// moved under a work assignment that is itself filed under that invoice (<c>sprk_workassignment.sprk_regardinginvoice</c>) is
/// refused with <see cref="InvoiceFamilyCoreRecordGuard.WorkAssignmentCycleCode"/> and nothing is written; a work assignment
/// filed under another invoice, or under none, takes the invoice as before.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private static readonly Guid CycleWorkAssignment = Guid.Parse("02200000-0000-4000-8000-0000000000d1");

    /// <summary>
    /// The app-only world: the invoice (filed under <see cref="Matter"/>, or under nothing when <paramref name="storedUnderMatter"/>
    /// is false) with no lines, and the work assignment, filed under <paramref name="workAssignmentFiledUnder"/> (null = no invoice).
    /// </summary>
    private static Mock<IGenericEntityService> CycleWorld(Guid? workAssignmentFiledUnder, bool storedUnderMatter)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var invoice = new Entity("sprk_invoice", Invoice);
        if (storedUnderMatter)
            invoice["sprk_matter"] = MatterRef(Matter);
        app.Setup(a => a.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        var workAssignment = new Entity("sprk_workassignment", CycleWorkAssignment) { ["sprk_regardingmatter"] = MatterRef(Matter) };
        if (workAssignmentFiledUnder is { } filedUnder)
            workAssignment[InvoiceFamilyCoreRecordGuard.WorkAssignmentInvoiceColumn] = new EntityReference("sprk_invoice", filedUnder);
        app.Setup(a => a.RetrieveAsync("sprk_workassignment", CycleWorkAssignment, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workAssignment);
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        return app;
    }

    /// <summary>
    /// The move under the work assignment. The playbook update and the output orchestrator clear the matter in the same write;
    /// the push cannot clear a lookup, so it moves an invoice stored under no core record.
    /// </summary>
    private Task<PathOutcome> MoveUnderTheWorkAssignmentAsync(GenericPath path, Guid? workAssignmentFiledUnder)
    {
        var clearsMatter = path != GenericPath.FieldMappingPush;
        var lookups = clearsMatter
            ? Lookups(("sprk_matter", null), ("sprk_regardingworkassignment", new EntityReference("sprk_workassignment", CycleWorkAssignment)))
            : Lookups(("sprk_regardingworkassignment", new EntityReference("sprk_workassignment", CycleWorkAssignment)));
        return RunAsync(path, "sprk_invoice", Invoice, lookups, NoPlain, CycleWorld(workAssignmentFiledUnder, clearsMatter).Object);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnInvoiceMovedUnderAWorkAssignmentFiledUnderThatInvoice_IsRefusedWithTheCycleCode_AndNothingIsWritten(GenericPath path)
    {
        var outcome = await MoveUnderTheWorkAssignmentAsync(path, workAssignmentFiledUnder: Invoice);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.WorkAssignmentCycleCode);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, true)]
    [InlineData(GenericPath.PlaybookUpdate, false)]
    [InlineData(GenericPath.OutputOrchestrator, true)]
    [InlineData(GenericPath.OutputOrchestrator, false)]
    [InlineData(GenericPath.FieldMappingPush, true)]
    [InlineData(GenericPath.FieldMappingPush, false)]
    public async Task AnInvoiceMovedUnderAWorkAssignmentFiledUnderAnotherInvoiceOrNone_IsWritten(GenericPath path, bool filedUnderAnotherInvoice)
    {
        var outcome = await MoveUnderTheWorkAssignmentAsync(path, filedUnderAnotherInvoice ? OtherInvoice : null);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_invoice", Invoice);
    }
}
