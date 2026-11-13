using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 ruling on RecordFamilyEdges (2026-10-28), end to end through each generic update path. Since task
/// 045g rule 1 (owner, EB-D-231) an update cannot move an invoice: a move under a work assignment is
/// <see cref="InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode"/> wherever the work assignment is filed, and the work
/// assignment is not read. The cycle check (<see cref="InvoiceFamilyCoreRecordGuard.WorkAssignmentCycleCode"/>) runs when an
/// update names the invoice's own work assignment again, and on the create.
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

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "this invoice")]
    [InlineData(GenericPath.PlaybookUpdate, "another invoice")]
    [InlineData(GenericPath.PlaybookUpdate, "no invoice")]
    [InlineData(GenericPath.OutputOrchestrator, "this invoice")]
    [InlineData(GenericPath.OutputOrchestrator, "another invoice")]
    [InlineData(GenericPath.OutputOrchestrator, "no invoice")]
    [InlineData(GenericPath.FieldMappingPush, "this invoice")]
    [InlineData(GenericPath.FieldMappingPush, "another invoice")]
    [InlineData(GenericPath.FieldMappingPush, "no invoice")]
    public async Task AnInvoiceMovedUnderAWorkAssignment_IsRefusedCoreRecordFixed_AndTheWorkAssignmentIsNotRead(GenericPath path, string filedUnder)
    {
        // Task 045g rule 1 (owner, EB-D-231): an update cannot move an invoice, wherever the work assignment is filed. The cycle
        // check runs when an update names the invoice's own work assignment again.
        var app = CycleWorld(filedUnder switch
        {
            "this invoice" => Invoice,
            "another invoice" => OtherInvoice,
            _ => null,
        }, storedUnderMatter: path != GenericPath.FieldMappingPush);
        var lookups = path != GenericPath.FieldMappingPush
            ? Lookups(("sprk_matter", null), ("sprk_regardingworkassignment", new EntityReference("sprk_workassignment", CycleWorkAssignment)))
            : Lookups(("sprk_regardingworkassignment", new EntityReference("sprk_workassignment", CycleWorkAssignment)));

        var outcome = await RunAsync(path, "sprk_invoice", Invoice, lookups, NoPlain, app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode);
        NoGuid(outcome);
        NothingWritten();
        app.Verify(a => a.RetrieveAsync("sprk_workassignment", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
