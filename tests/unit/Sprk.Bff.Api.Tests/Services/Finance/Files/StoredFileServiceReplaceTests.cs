using System.Data;
using System.Text;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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

    [Fact(DisplayName = "116 fix 1 (A5): the version read returns the SERVABLE rows of that version and purpose, and nothing else")]
    public async Task ReadRows_ReturnsTheVersionsServableRowsOfThePurpose_AndNoOtherVersionsOrPurposes()
    {
        var rig = Rig();
        var servable = Guid.NewGuid();
        var removed = Guid.NewGuid();
        rig.SeedFile(servable, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        rig.SeedFile(removed, Version, Old, StoredFileStatus.Removed, StoredFilePurpose.ProcedureSource);
        rig.SeedFile(Guid.NewGuid(), new StoredFileParent(StoredFileParentKind.ProcedureVersion, Guid.NewGuid()), Old, purpose: StoredFilePurpose.ProcedureSource);
        rig.SeedFile(Guid.NewGuid(), new StoredFileParent(StoredFileParentKind.Invoice, VersionId), Old, purpose: StoredFilePurpose.InvoiceSupportingFile);

        var rows = await rig.Service.ReadRowsForProcedureVersionAsync(VersionId, StoredFilePurpose.ProcedureSource, CancellationToken.None);

        rows.Select(r => r.FileId).Should().BeEquivalentTo(new[] { servable });
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

    // ── Fix round 1 ────────────────────────────────────────────────────────

    /// <summary>The stored-file query answered as Dataverse answers it: its status condition applied, at most TopCount rows, MoreRecords set.</summary>
    private static void QueriesAnsweredLikeDataverse(StoredFileRig rig, Guid? last = null) =>
        rig.Dataverse.Mock.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == StoredFileSchema.Entity), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var status = q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == StoredFileSchema.Status)?.Values[0];
                var rows = rig.Dataverse.Rows.Values
                    .Where(r => status is null || r.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status)?.Value == (int)status)
                    .OrderBy(r => r.Id == last ? 1 : 0).ThenBy(r => r.Id)
                    .ToList();
                var top = q.TopCount ?? 5000;
                return new EntityCollection(rows.Take(top).ToList()) { MoreRecords = rows.Count > top };
            });

    [Fact(DisplayName = "116 fix 1 (A5) must-fire: more than 100 Removed rows cannot crowd the Servable row out of the read; it is still withdrawn")]
    public async Task Replace_ManyRemovedRows_StillWithdrawsTheServableOne()
    {
        var rig = Rig();
        var servable = Guid.NewGuid();
        for (var i = 0; i < StoredFileService.MaxRowsPerProcedureVersion + 5; i++)
        {
            rig.SeedFile(Guid.NewGuid(), Version, Old, StoredFileStatus.Removed, StoredFilePurpose.ProcedureSource);
        }

        rig.SeedFile(servable, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        QueriesAnsweredLikeDataverse(rig, last: servable);

        var result = await Replace(rig);

        result.IsCreated.Should().BeTrue();
        StatusOf(rig, servable).Should().Be(StoredFileStatus.Removed);
        StatusOf(rig, result.FileId!.Value).Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "116 fix 1 (A5): a read that reports more Servable rows than it returned refuses before anything is staged")]
    public async Task Replace_AnIncompleteRead_Throws_AndStagesNothing()
    {
        var rig = Rig();
        for (var i = 0; i <= StoredFileService.MaxRowsPerProcedureVersion; i++)
        {
            rig.SeedFile(Guid.NewGuid(), Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        }

        QueriesAnsweredLikeDataverse(rig);
        var writes = rig.Dataverse.Writes;
        var storage = rig.Gateway.StorageCalls;

        var act = () => Replace(rig);

        await act.Should().ThrowAsync<InvalidOperationException>();
        rig.Dataverse.Writes.Should().Be(writes);
        rig.Gateway.StorageCalls.Should().Be(storage);
    }

    private static Task<StoredFileCreateResult> ReplaceChecked(StoredFileRig rig, Func<CancellationToken, Task<bool>> check, CancellationToken ct = default) =>
        rig.Service.ReplaceForCallerAsync(StoredFileRig.Caller(), "token", StoredFilePurpose.ProcedureSource, Version,
            "guidelines.pdf", "application/pdf", new MemoryStream(New), ct, parentStillAccepts: check);

    [Fact(DisplayName = "116 fix 1 (A1) must-fire: a parent that stopped accepting a file once the new file is Servable withdraws the new file and refuses; the earlier stays Servable")]
    public async Task Replace_AParentNoLongerADraft_WithdrawsTheNewFile_AndRefuses()
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        StoredFileStatus? newStatusAtCheck = null;

        var result = await ReplaceChecked(rig, _ =>
        {
            newStatusAtCheck = rig.Dataverse.Rows.Keys.Where(id => id != earlier).Select(id => StatusOf(rig, id)).Single();
            return Task.FromResult(false);
        });

        result.IsCreated.Should().BeFalse();
        result.RefusalCode.Should().Be(StoredFileRefusal.ParentNoLongerAccepts);
        newStatusAtCheck.Should().Be(StoredFileStatus.Servable, "the parent is re-read once the new file is Servable");
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Servable);
        var created = rig.Dataverse.Rows.Keys.Where(id => id != earlier).Should().ContainSingle().Subject;
        StatusOf(rig, created).Should().Be(StoredFileStatus.Removed);
    }

    [Fact(DisplayName = "116 fix 1 (A1) must-not-fire: a parent still accepting a file completes the replace")]
    public async Task Replace_AParentStillADraft_Replaces()
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);

        var result = await ReplaceChecked(rig, _ => Task.FromResult(true));

        result.IsCreated.Should().BeTrue();
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Removed);
        StatusOf(rig, result.FileId!.Value).Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "116 fix 1 (A1): a fault in the parent's re-check withdraws the new file and rethrows; the earlier stays Servable")]
    public async Task Replace_AFaultingParentCheck_WithdrawsTheNewFile_AndRethrows()
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);

        var act = () => ReplaceChecked(rig, _ => Task.FromException<bool>(new InvalidOperationException("simulated Dataverse 503")));

        await act.Should().ThrowAsync<InvalidOperationException>();
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Servable);
        StatusOf(rig, rig.Dataverse.Rows.Keys.Single(id => id != earlier)).Should().Be(StoredFileStatus.Removed);
    }

    [Fact(DisplayName = "116 fix 1 (A3) must-fire: a request cancelled right after the new file is Servable still withdraws the earlier file: exactly one Servable file")]
    public async Task Replace_CancelledOnceServable_StillLeavesExactlyOneServableFile()
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        using var request = new CancellationTokenSource();

        // The earlier row's withdraw observes the token it is given, as a real Dataverse call does.
        rig.Dataverse.Mock.Setup(e => e.UpdateIfRowVersionMatchesAsync(
                StoredFileSchema.Entity, earlier, It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string _, Dictionary<string, object> fields, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                rig.Dataverse.Rows[id][StoredFileSchema.Status] = fields[StoredFileSchema.Status];
                return Task.CompletedTask;
            });

        var result = await ReplaceChecked(rig, _ =>
        {
            request.Cancel(); // the client goes away just after the new file became Servable
            return Task.FromResult(true);
        }, request.Token);

        result.IsCreated.Should().BeTrue();
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Removed);
        rig.Dataverse.Rows.Keys.Count(id => StatusOf(rig, id) == StoredFileStatus.Servable).Should().Be(1);
    }

    [Fact(DisplayName = "116 fix 2 (item 7) must-fire: a request cancelled as the flip commits still finishes the replace: exactly one Servable file")]
    public async Task Replace_CancelledAsTheFlipCommits_StillLeavesExactlyOneServableFile()
    {
        var rig = Rig();
        var earlier = Guid.NewGuid();
        rig.SeedFile(earlier, Version, Old, purpose: StoredFilePurpose.ProcedureSource);
        using var request = new CancellationTokenSource();

        // The new row's flip commits, the client goes away, and the call then observes the token it was given, as a real Dataverse call does.
        rig.Dataverse.Mock.Setup(e => e.UpdateIfRowVersionMatchesAsync(
                StoredFileSchema.Entity, It.Is<Guid>(id => id != earlier), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string _, Dictionary<string, object> fields, CancellationToken token) =>
            {
                rig.Dataverse.Rows[id][StoredFileSchema.Status] = fields[StoredFileSchema.Status];
                request.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        var result = await ReplaceChecked(rig, _ => Task.FromResult(true), request.Token);

        result.IsCreated.Should().BeTrue();
        StatusOf(rig, earlier).Should().Be(StoredFileStatus.Removed);
        rig.Dataverse.Rows.Keys.Count(id => StatusOf(rig, id) == StoredFileStatus.Servable).Should().Be(1);
    }
}
