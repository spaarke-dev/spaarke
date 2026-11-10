using System.Data;
using System.Text;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Files;

/// <summary>
/// ebilling-r1 task 116 (procedure-and-file-model 3.2 "one file per version", 4.2 write-once): <see cref="StoredFileService.ReadRowsForProcedureVersionAsync"/>
/// and <see cref="StoredFileService.ReplaceForCallerAsync"/>, which composes the caller upload with the ONE withdraw that
/// <see cref="StoredFileService.WithdrawForUndoAsync"/> also uses (one conditional write of Removed per row, under the row version read).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees drafting and publish rely on — after a replace the version holds exactly one Servable source file,
/// the new file is Servable BEFORE the earlier one is withdrawn, a refused or failed upload leaves the earlier file Servable, and a failed
/// withdraw withdraws the new file instead. Blob, Dataverse and caller rights are the file store's fakes; step 0 is the real definition.
/// </remarks>
public sealed class StoredFileServiceReplaceTests
{
    private static readonly Guid VersionId = Guid.Parse("7e500000-0000-4000-8000-000000000116");
    private static readonly StoredFileParent Version = new(StoredFileParentKind.ProcedureVersion, VersionId);
    private static readonly byte[] Old = Encoding.UTF8.GetBytes("%PDF-1.7 the earlier guideline");
    private static readonly byte[] New = Encoding.UTF8.GetBytes("%PDF-1.7 the new guideline");

    private static StoredFileRig Rig(Spaarke.Dataverse.AccessRights rights = Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write)
    {
        var rig = new StoredFileRig();
        rig.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, rights);
        return rig;
    }

    private static Task<StoredFileCreateResult> Replace(StoredFileRig rig, StoredFileParent? parent = null) =>
        rig.Service.ReplaceForCallerAsync(StoredFileRig.Caller(), "token", StoredFilePurpose.ProcedureSource, parent ?? Version,
            "guidelines.pdf", "application/pdf", new MemoryStream(New), CancellationToken.None);

    private static StoredFileStatus StatusOf(StoredFileRig rig, Guid id) =>
        (StoredFileStatus)rig.Dataverse.Rows[id].GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value;

    private static int IndexOf(StoredFileRig rig, string call) => rig.Dataverse.Calls.ToList().FindIndex(c => c == call);

    // ── ReadRowsForProcedureVersionAsync ────────────────────────────────────────────────────────

    [Fact(DisplayName = "116: the version read returns every row of that version and purpose, whatever its status, and nothing else")]
    public async Task ReadRows_ReturnsTheVersionsRowsOfThePurpose_AnyStatus_AndNoOtherVersionsOrPurposes()
    {
        var rig = Rig();
        var servable = Guid.NewGuid();
        var removed = Guid.NewGuid();
        rig.SeedFile(servable, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        rig.SeedFile(removed, Version, Old, StoredFileStatus.Removed, StoredFilePurpose.ProcedureSource);
        rig.SeedFile(Guid.NewGuid(), new StoredFileParent(StoredFileParentKind.ProcedureVersion, Guid.NewGuid()), Old, purpose: StoredFilePurpose.ProcedureSource);
        rig.SeedFile(Guid.NewGuid(), new StoredFileParent(StoredFileParentKind.Invoice, VersionId), Old, purpose: StoredFilePurpose.InvoiceSupportingFile);

        var rows = await rig.Service.ReadRowsForProcedureVersionAsync(VersionId, StoredFilePurpose.ProcedureSource, CancellationToken.None);

        rows.Select(r => r.FileId).Should().BeEquivalentTo(new[] { servable, removed });
        rows.Should().OnlyContain(r => r.Parent == Version);
    }

    [Fact(DisplayName = "116: a fault of the version read throws (never an empty list)")]
    public async Task ReadRows_AFault_Throws()
    {
        var rig = Rig();
        rig.Dataverse.QueryFault = new InvalidOperationException("simulated Dataverse fault");

        var act = () => rig.Service.ReadRowsForProcedureVersionAsync(VersionId, StoredFilePurpose.ProcedureSource, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── ReplaceForCallerAsync: must fire ────────────────────────────────────────────────────────

    [Fact(DisplayName = "116 must-fire: a replace makes the new file Servable FIRST, then sets the earlier Servable file Removed under its row version")]
    public async Task Replace_NewFileServableFirst_ThenTheEarlierOneRemoved()
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);

        var result = await Replace(rig);

        result.IsCreated.Should().BeTrue();
        var created = result.FileId!.Value;
        StatusOf(rig, created).Should().Be(StoredFileStatus.Servable);
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Removed);
        var flip = IndexOf(rig, $"update {StoredFileSchema.Entity} {created}");
        var withdraw = IndexOf(rig, $"update {StoredFileSchema.Entity} {earlier}");
        flip.Should().BeGreaterThanOrEqualTo(0);
        withdraw.Should().BeGreaterThan(flip, "the earlier file is withdrawn only after the new one is Servable");
        rig.Dataverse.Calls.Count(c => c == $"update {StoredFileSchema.Entity} {earlier}").Should().Be(1, "ONE write of Removed per row");
        rig.Gateway.Calls.Should().NotContain(c => c.StartsWith("delete", StringComparison.Ordinal), "write-once: no blob is deleted by a replace");
        (await rig.Service.ReadRowsForProcedureVersionAsync(VersionId, StoredFilePurpose.ProcedureSource, CancellationToken.None))
            .Count(r => r.Status == StoredFileStatus.Servable).Should().Be(1);
    }

    [Fact(DisplayName = "116 must-fire: two earlier Servable files (a lost concurrent replace) are both withdrawn; an already Removed row is not written")]
    public async Task Replace_WithdrawsEveryEarlierServableRow_AndSkipsRemovedOnes()
    {
        var rig = Rig();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var gone = Guid.NewGuid();
        rig.SeedFile(a, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        rig.SeedFile(b, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        rig.SeedFile(gone, Version, Old, StoredFileStatus.Removed, StoredFilePurpose.ProcedureSource);

        var result = await Replace(rig);

        StatusOf(rig, a).Should().Be(StoredFileStatus.Removed);
        StatusOf(rig, b).Should().Be(StoredFileStatus.Removed);
        StatusOf(rig, result.FileId!.Value).Should().Be(StoredFileStatus.Servable);
        rig.Dataverse.Calls.Should().NotContain($"update {StoredFileSchema.Entity} {gone}");
    }

    [Theory(DisplayName = "116 must-fire: a failed withdraw of the earlier file withdraws the NEW file, rethrows, and the earlier file stays the one Servable file")]
    [InlineData("fault")]
    [InlineData("changed since the read")]
    public async Task Replace_AFailedWithdraw_WithdrawsTheNewFile_AndRethrows(string how)
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        Exception fault = how == "fault" ? new InvalidOperationException("simulated Dataverse 429") : new DBConcurrencyException("ConcurrencyVersionMismatch");
        rig.Dataverse.Mock.Setup(e => e.UpdateIfRowVersionMatchesAsync(
                StoredFileSchema.Entity, earlier, It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(fault);

        var act = () => Replace(rig);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(fault);
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Servable);
        var created = rig.Dataverse.Rows.Keys.Single(id => id != earlier);
        StatusOf(rig, created).Should().Be(StoredFileStatus.Removed, "the new file is withdrawn so the version keeps one Servable file");
    }

    // ── ReplaceForCallerAsync: must not fire ────────────────────────────────────────────────────

    [Fact(DisplayName = "116 must-not-fire: a refused upload (no Write on the version) leaves the earlier file Servable and writes nothing")]
    public async Task Replace_ARefusedUpload_LeavesTheEarlierFileServable()
    {
        var rig = Rig(Spaarke.Dataverse.AccessRights.Read);
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        var callsBefore = rig.Dataverse.Writes;

        var result = await Replace(rig);

        result.RefusalCode.Should().Be(StoredFileRefusal.NoWriteOnParent);
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Servable);
        rig.Dataverse.Writes.Should().Be(callsBefore);
    }

    [Fact(DisplayName = "116 must-not-fire: a flip that fails leaves the earlier file Servable; the earlier row is never written")]
    public async Task Replace_AFailedFlip_LeavesTheEarlierFileServable()
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        rig.Dataverse.Mock.Setup(e => e.UpdateIfRowVersionMatchesAsync(
                StoredFileSchema.Entity, It.Is<Guid>(id => id != earlier), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated flip failure"));

        StoredFileCreateResult? result = null;
        try
        {
            result = await Replace(rig);
        }
        catch (InvalidOperationException)
        {
            // Either answer is a failed upload; what matters is the earlier file.
        }

        (result?.IsCreated ?? false).Should().BeFalse();
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Servable);
        rig.Dataverse.Calls.Should().NotContain($"update {StoredFileSchema.Entity} {earlier}");
    }

    [Fact(DisplayName = "116: a parent that is not a Procedure Version is refused before any read or write")]
    public async Task Replace_AnotherParentKind_IsInvalid_AndTouchesNothing()
    {
        var rig = Rig();

        var result = await Replace(rig, new StoredFileParent(StoredFileParentKind.Invoice, VersionId));

        result.RefusalCode.Should().Be(StoredFileRefusal.InvalidRequest);
        rig.Dataverse.Calls.Should().BeEmpty();
        rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116: a first upload (no earlier file) is a plain upload with no withdraw")]
    public async Task Replace_WithNoEarlierFile_CreatesOnly()
    {
        var rig = Rig();

        var result = await Replace(rig);

        result.IsCreated.Should().BeTrue();
        rig.Dataverse.Calls.Count(c => c.StartsWith($"update {StoredFileSchema.Entity}", StringComparison.Ordinal)).Should().Be(1, "only the new row's flip");
    }

    [Fact(DisplayName = "116: the undo path still withdraws through the same conditional write (WithdrawForUndoAsync unchanged)")]
    public async Task WithdrawForUndo_StillOneConditionalWritePerRow()
    {
        var rig = Rig();
        var row = Guid.NewGuid();
        rig.SeedFile(row, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        var read = await rig.Service.ReadRowsForProcedureVersionAsync(VersionId, StoredFilePurpose.ProcedureSource, CancellationToken.None);

        await rig.Service.WithdrawForUndoAsync(read, CancellationToken.None);

        StatusOf(rig, row).Should().Be(StoredFileStatus.Removed);
        rig.Dataverse.Mock.Verify(e => e.UpdateIfRowVersionMatchesAsync(
            StoredFileSchema.Entity, row, read.Single().RowVersion!,
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
