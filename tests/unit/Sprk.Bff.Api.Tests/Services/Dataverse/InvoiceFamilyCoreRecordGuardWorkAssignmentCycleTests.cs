using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

/// <summary>
/// unified-access-control-r2 ruling on RecordFamilyEdges (2026-10-28): an invoice may be filed under a work assignment
/// (<c>sprk_invoice.sprk_regardingworkassignment</c>), and a work assignment under an invoice
/// (<c>sprk_workassignment.sprk_regardinginvoice</c>), but never each under the other. An update that would file an invoice
/// under a work assignment already filed under that invoice is refused with
/// <see cref="InvoiceFamilyCoreRecordGuard.WorkAssignmentCycleCode"/>; the work assignment is read as the APP, failing closed.
/// </summary>
public class InvoiceFamilyCoreRecordGuardWorkAssignmentCycleTests
{
    private static readonly Guid InvoiceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherInvoiceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WorkAssignmentId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly EntityReference WorkAssignment = new("sprk_workassignment", WorkAssignmentId);

    private static Func<string, object?> Reads(params (string Column, object? Value)[] values) =>
        column => values.FirstOrDefault(v => v.Column == column).Value;

    private static readonly Func<EntityReference, CancellationToken, Task<InvoiceFamilyCoreRecord>> NeverRead =
        (_, _) => throw new InvalidOperationException("must not be read");

    private static readonly Func<EntityReference, CancellationToken, Task<bool>> NoLines = (_, _) => Task.FromResult(false);

    /// <summary>The app-only reader: the work assignment, filed under <paramref name="filedUnder"/> (null = under no invoice), and no lines.</summary>
    private static Mock<IGenericEntityService> AppOnly(EntityReference? filedUnder)
    {
        var app = InvoiceFamilyCoreRecordGuardLineSplitTests.AppOnlyOver([]);
        var row = new Entity("sprk_workassignment", WorkAssignmentId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        };
        if (filedUnder is not null)
            row[InvoiceFamilyCoreRecordGuard.WorkAssignmentInvoiceColumn] = filedUnder;
        app.Setup(a => a.RetrieveAsync("sprk_workassignment", WorkAssignmentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        app.Setup(a => a.RetrieveAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) });
        return app;
    }

    private static Task<InvoiceFamilyCoreRecord> InvoiceUnderTheWorkAssignment(IGenericEntityService? app) =>
        InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_regardingworkassignment", WorkAssignment)), NeverRead, default, NoLines,
            InvoiceFamilyCoreRecordGuard.WorkAssignmentFiledUnderInvoice(app, InvoiceId));

    // ── CheckWriteAsync ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckWrite_AnInvoiceFiledUnderAWorkAssignmentFiledUnderThatInvoice_IsRefusedWithTheCycleCode()
    {
        var app = AppOnly(new EntityReference("sprk_invoice", InvoiceId));

        var result = await InvoiceUnderTheWorkAssignment(app.Object);

        result.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.WorkAssignmentCycleCode);
        result.Reason.Should().Contain("Nothing was written").And.NotContain(WorkAssignmentId.ToString("D"));
        app.Verify(a => a.RetrieveAsync("sprk_workassignment", WorkAssignmentId,
            It.Is<string[]>(c => c.SequenceEqual(new[] { "sprk_regardinginvoice" })), It.IsAny<CancellationToken>()), Times.Once,
            "the work assignment's own filing is read once, as the app");
    }

    [Fact]
    public async Task CheckWrite_AWorkAssignmentFiledUnderAnotherInvoice_IsDecided()
    {
        var result = await InvoiceUnderTheWorkAssignment(AppOnly(new EntityReference("sprk_invoice", OtherInvoiceId)).Object);

        result.IsRefused.Should().BeFalse(result.Reason);
        result.Regarding.Should().BeEquivalentTo(WorkAssignment);
    }

    [Fact]
    public async Task CheckWrite_AWorkAssignmentFiledUnderNoInvoice_IsDecided()
    {
        var result = await InvoiceUnderTheWorkAssignment(AppOnly(filedUnder: null).Object);

        result.IsRefused.Should().BeFalse(result.Reason);
        result.Regarding.Should().BeEquivalentTo(WorkAssignment);
    }

    [Fact]
    public async Task CheckWrite_TheCycleCheck_IsNotAskedForAMatterOrProject_ForARefusedInvoice_OrForALine()
    {
        Func<EntityReference, CancellationToken, Task<bool>> never = (_, _) => throw new InvalidOperationException("must not be asked");

        var matter = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId))), NeverRead, default, NoLines, never);
        var twoCores = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_invoice",
            Reads(("sprk_matter", new EntityReference("sprk_matter", MatterId)), ("sprk_regardingworkassignment", WorkAssignment)),
            NeverRead, default, NoLines, never);
        var line = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync("sprk_billingevent",
            Reads(("sprk_regardingworkassignment", WorkAssignment)), NeverRead, default, null, never);

        matter.IsRefused.Should().BeFalse(matter.Reason);
        twoCores.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        line.IsRefused.Should().BeFalse(line.Reason);
    }

    [Fact]
    public async Task CheckWrite_WhenTheWorkAssignmentCannotBeRead_FailsClosedReadFailed()
    {
        var faulting = new Mock<IGenericEntityService>(MockBehavior.Strict);
        faulting.Setup(a => a.RetrieveAsync("sprk_workassignment", WorkAssignmentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("socket 10.0.0.7"));
        var missing = new Mock<IGenericEntityService>(MockBehavior.Strict);
        missing.Setup(a => a.RetrieveAsync("sprk_workassignment", WorkAssignmentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);

        var thrown = await InvoiceUnderTheWorkAssignment(faulting.Object);
        var notFound = await InvoiceUnderTheWorkAssignment(missing.Object);
        var noReader = await InvoiceUnderTheWorkAssignment(app: null);

        thrown.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        thrown.Reason.Should().NotContain("10.0.0.7");
        notFound.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        noReader.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "no app-only reader is never \"no cycle\"");
    }

    // ── CheckGenericUpdateAsync (the playbook update, output orchestrator and field-mapping push) ─────────────────────

    private static Dictionary<string, object?> MoveToTheWorkAssignment() => new()
    {
        ["sprk_Matter@odata.bind"] = null,
        ["sprk_RegardingWorkAssignment@odata.bind"] = $"/sprk_workassignments({WorkAssignmentId:D})",
    };

    [Fact]
    public async Task GenericUpdate_AnInvoiceMovedToAWorkAssignmentFiledUnderIt_IsRefusedWithTheCycleCode()
    {
        var result = await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId,
            MoveToTheWorkAssignment(), AppOnly(new EntityReference("sprk_invoice", InvoiceId)).Object);

        result!.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.WorkAssignmentCycleCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenericUpdate_AnInvoiceMovedToAWorkAssignmentFiledElsewhereOrNowhere_IsAllowed(bool filedUnderAnotherInvoice)
    {
        var app = AppOnly(filedUnderAnotherInvoice ? new EntityReference("sprk_invoice", OtherInvoiceId) : null);

        (await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId, MoveToTheWorkAssignment(), app.Object))
            .Should().BeNull();
        app.Verify(a => a.RetrieveAsync("sprk_workassignment", WorkAssignmentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenericUpdate_AnInvoiceRenamed_DoesNotReadTheWorkAssignment()
    {
        var app = AppOnly(new EntityReference("sprk_invoice", InvoiceId));
        app.Setup(a => a.RetrieveAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", InvoiceId) { ["sprk_regardingworkassignment"] = WorkAssignment });

        (await InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync("sprk_invoice", InvoiceId,
            new Dictionary<string, object?> { ["sprk_name"] = "Renamed" }, app.Object)).Should().BeNull();

        app.Verify(a => a.RetrieveAsync("sprk_workassignment", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "a write that touches no core lookup is not a re-file");
    }
}
