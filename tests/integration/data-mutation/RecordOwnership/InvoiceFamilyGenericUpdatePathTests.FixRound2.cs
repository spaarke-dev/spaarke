using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1, #2081 fix round 2 (unified-access-control-r2 re-check). (1) Check-then-act: the guard reads the row, but the
/// PATCH that follows is an upsert, so a row deleted in between would be re-created as an orphan; an invoice or line is now
/// written update-only (<c>If-Match: *</c>, impersonation kept) and a vanished row is a refusal. (2) The existence oracle:
/// a write that runs AS a user must not answer "not found" for a row that user may not read.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private void TheRowIsDeletedAfterTheGuardRead() =>
        _writes.Setup(w => w.UpdateExistingRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("gone"));

    private void NothingWasCreated() =>
        Sent().Should().NotContain(w => w.Method == nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync),
            "the upsert would have re-created the deleted row as an orphan");

    public static TheoryData<GenericPath, string> RaceCases()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in Enum.GetValues<GenericPath>())
            foreach (var table in new[] { "sprk_invoice", "sprk_billingevent" })
                data.Add(path, table);
        return data;
    }

    // ── Item 1: check-then-act ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RaceCases))]
    public async Task ARowDeletedBetweenTheGuardReadAndTheWrite_IsRefusedRecordNotFound_AndNothingIsCreated(GenericPath path, string table)
    {
        TheRowIsDeletedAfterTheGuardRead();
        var id = table == "sprk_invoice" ? Invoice : Line;

        var outcome = await RunAsync(path, table, id, NoLookups, RenameOnly, WorldWithLines().Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        outcome.Message.Should().Contain(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        NoGuid(outcome);
        NothingWasCreated();
        Sent().Should().ContainSingle("the one update-only attempt");
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnInvoiceFamilyWrite_GoesUpdateOnly_NeverTheUpsert(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoice", Invoice, NoLookups, RenameOnly, InvoiceUnderAMatter().Object);

        outcome.Code.Should().BeNull();
        Sent().Should().ContainSingle().Which.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));
    }

    [Fact]
    public async Task TheOutputOrchestrator_OnItsOptimisticPath_WritesUpdateOnly_AndRefusesARowDeletedMeanwhile()
    {
        var fields = new Dictionary<string, object?> { ["sprk_name"] = "Renamed" };

        await Handler(InvoiceUnderAMatter().Object).UpdateAsync(
            "sprk_invoice", Invoice, fields, Sprk.Bff.Api.Services.Ai.ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None);
        Sent().Should().ContainSingle().Which.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));

        _writes.Invocations.Clear();
        TheRowIsDeletedAfterTheGuardRead();
        var act = () => Handler(InvoiceUnderAMatter().Object).UpdateAsync(
            "sprk_invoice", Invoice, fields, Sprk.Bff.Api.Services.Ai.ConcurrencyMode.Optimistic, maxRetries: 1, CancellationToken.None);

        CodeOnly((await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which, InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        NothingWasCreated();
    }

    [Fact]
    public async Task ATableOutsideTheInvoiceFamily_KeepsTheUpsert()
    {
        // #2081 is not widened in this round: the update-only write is for the two guarded tables only.
        var outcome = await RunAsync(GenericPath.PlaybookUpdate, "sprk_todo", Invoice, NoLookups, RenameOnly, app: null);

        outcome.Code.Should().BeNull();
        Sent().Should().ContainSingle().Which.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync));
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)] // master merge (#2257): a person's output mapping writes AS that person
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task TheUpdateOnlyWrite_StillActsAsTheImpersonatedCaller(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoice", Invoice, NoLookups, RenameOnly, InvoiceUnderAMatter().Object, playbookActsAs: Caller);

        outcome.Code.Should().BeNull();
        var sent = Sent().Should().ContainSingle().Subject;
        sent.Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));
        sent.As.Should().Be(Caller, "the playbook update and the output mapping (when they act for a person) and the push write as that caller");
    }

    [Fact]
    public async Task ThePlaybookUpdateWithNoCaller_AndTheOrchestrator_WriteAppOnly()
    {
        await RunAsync(GenericPath.PlaybookUpdate, "sprk_invoice", Invoice, NoLookups, RenameOnly, InvoiceUnderAMatter().Object);
        await RunAsync(GenericPath.OutputOrchestrator, "sprk_invoice", Invoice, NoLookups, RenameOnly, InvoiceUnderAMatter().Object);

        Sent().Should().HaveCount(2).And.OnlyContain(w => w.As == null);
    }

    // ── Item 2: the existence oracle ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoice", true)]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoice", false)]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_billingevent", true)]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_billingevent", false)]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoice", true)] // master merge (#2257): a person's output mapping
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoice", false)]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_billingevent", true)]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_billingevent", false)]
    public async Task AnImpersonatedPlaybookUpdate_OfARowThatDoesNotExist_AnswersReadFailed_LikeARowItCannotRead(
        GenericPath path, string table, bool answersNull)
    {
        var missing = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var read = missing.Setup(a => a.RetrieveAsync(table, MissingRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (answersNull) read.ReturnsAsync((Entity)null!); else read.ThrowsAsync(ObjectDoesNotExist());
        var unreadable = new Mock<IGenericEntityService>(MockBehavior.Strict);
        unreadable.Setup(a => a.RetrieveAsync(table, Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("socket 10.0.0.7"));

        var notThere = await RunAsync(path, table, MissingRow, NoLookups, RenameOnly, missing.Object, playbookActsAs: Caller);
        var cannotRead = await RunAsync(path, table, Invoice, NoLookups, RenameOnly, unreadable.Object, playbookActsAs: Caller);

        notThere.Should().Be(cannotRead, "the two are indistinguishable: same code, same text");
        notThere.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoice")]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_billingevent")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoice")] // master merge (#2257): a person's output mapping
    [InlineData(GenericPath.OutputOrchestrator, "sprk_billingevent")]
    public async Task AnImpersonatedPlaybookUpdate_WhoseRowIsDeletedBeforeTheWrite_AnswersReadFailed_NotNotFound(GenericPath path, string table)
    {
        TheRowIsDeletedAfterTheGuardRead();
        var id = table == "sprk_invoice" ? Invoice : Line;

        var outcome = await RunAsync(path, table, id, NoLookups, RenameOnly, WorldWithLines().Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        NothingWasCreated();
    }

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_billingevent")]
    public async Task TheFieldMappingPush_OfARowThatDoesNotExist_KeepsNotFound_ItsIdsComeFromAQueryTheCallerRead(string table)
    {
        var missing = new Mock<IGenericEntityService>(MockBehavior.Strict);
        missing.Setup(a => a.RetrieveAsync(table, MissingRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(ObjectDoesNotExist());

        var atTheGuard = await RunAsync(GenericPath.FieldMappingPush, table, MissingRow, NoLookups, RenameOnly, missing.Object);
        TheRowIsDeletedAfterTheGuardRead();
        var atTheWrite = await RunAsync(GenericPath.FieldMappingPush, table, table == "sprk_invoice" ? Invoice : Line, NoLookups, RenameOnly,
            WorldWithLines().Object);

        atTheGuard.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        atTheWrite.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
    }

    /// <summary>
    /// The master merge of 2026-10-18 (uac-r2 #2094): the push re-files an invoice whose lookup it moves, and the update-only write
    /// runs INSIDE that re-file (the per-child Write delegate). A row deleted after the guard read still fails THIS child with the
    /// guard's not-found code, and nothing is created.
    /// </summary>
    [Fact]
    public async Task TheFieldMappingPush_ReFilingAnInvoiceDeletedAfterTheGuardRead_IsRefusedRecordNotFound_InsideTheReFile()
    {
        TheRowIsDeletedAfterTheGuardRead();

        var outcome = await RunAsync(GenericPath.FieldMappingPush, "sprk_invoice", Invoice,
            Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain, WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        outcome.Message.Should().Be(
            $"Not written: {InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode)}");
        NoGuid(outcome);
        NothingWasCreated();
        Sent().Should().ContainSingle("the one update-only attempt");
        _pushOwnership.Reparents.Should().ContainSingle("the write ran inside the re-file");
    }
}
