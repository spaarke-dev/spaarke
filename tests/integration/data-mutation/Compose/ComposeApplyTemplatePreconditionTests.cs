// #776 — apply-template must not clobber a concurrent save at the head SPE version.
//
// THE DEFECT. `ComposeService.ApplyTemplateAsync` is a read-merge-write: it downloads the document's
// current bytes (T1), merges the template into them, and wrote the result back with a BLIND PUT (T2).
// A sibling tab saving inside that window was silently overwritten — the merged payload never
// contained their change, and nothing asserted the version it was computed from.
//
// THE SUBTLETY THAT MAKES THIS FILE NECESSARY. The obvious fix — reuse the save path's
// `ReplaceWithPreconditionAsync` — does NOT fix it. That helper retries ONCE against the fresh version
// on a failed precondition (last-writer-wins), which is sound on the save path only because
// `ReanchorStaleSaveAsync` has already rebased the caller's edits onto those very bytes, so the retried
// write CONTAINS the concurrent change. Nothing rebases an apply-template merge. Retrying there writes
// the stale payload anyway and erases the other writer — the If-Match would be decorative and the bug
// would survive a fix that looks correct.
//
// So the two modes have to genuinely differ, and that difference is what these tests pin. A test that
// only asserted "an If-Match was sent" would pass in both worlds, including the broken one.
//
// KEEP path: tests/integration/data-mutation/** — "every new write path => >=1 integration test
// verifying rollback semantics" (tests/CLAUDE.md). The rollback semantic here is: on conflict, NOTHING
// is written.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Compose;

public sealed class ComposeApplyTemplatePreconditionTests
{
    private const string DriveId = "drive-apply-template";
    private const string ItemId = "item-apply-template";
    private const string MergedPayloadETag = "\"etag-at-T1\"";
    private const string ConcurrentWriterETag = "\"etag-someone-else-saved\"";

    private static readonly byte[] MergedBytes = { 0x50, 0x4B, 0x03, 0x04, 0xAA, 0xBB };

    private static FileHandleDto Handle(string etag) => new(
        Id: ItemId,
        Name: "contract.docx",
        ParentId: null,
        Size: MergedBytes.Length,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: etag,
        IsFolder: false,
        WebUrl: null,
        DriveId: DriveId);

    private static ComposeSaveStorageCoordinator NewCoordinator(Mock<ISpeFileOperations> spe) =>
        new(spe.Object, documentRenderer: null!, cache: null, NullLogger.Instance);

    /// <summary>A facade whose If-Match write always reports "the version moved under you".</summary>
    private static Mock<ISpeFileOperations> SpeThatRejectsThePrecondition()
    {
        var spe = new Mock<ISpeFileOperations>();
        spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtagPreconditionFailedException(
                $"SPE write precondition failed: drive-item '{ItemId}' changed since ETag '{MergedPayloadETag}' was read.",
                MergedPayloadETag));
        spe.Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Handle(ConcurrentWriterETag));
        return spe;
    }

    [Fact]
    public async Task ReplaceWithPrecondition_WhenRebaseDisabledAndVersionMoved_WritesNothingAndSurfacesTheConflict()
    {
        // This is the apply-template contract. The merged payload was computed from the T1 bytes, so a
        // conflict is TERMINAL — the caller re-applies against the new version.
        var spe = SpeThatRejectsThePrecondition();
        var sut = NewCoordinator(spe);

        var act = async () => await sut.ReplaceWithPreconditionAsync(
            httpContext: null!, DriveId, ItemId, MergedBytes, MergedPayloadETag,
            CancellationToken.None, rebaseOnConflict: false);

        await act.Should().ThrowAsync<EtagPreconditionFailedException>(
            "a merge computed at T1 must not be written over a version it never contained");

        // The rollback semantic: exactly ONE write attempt, and it carried the precondition. No blind
        // retry, and critically no write with the CONCURRENT WRITER's etag — that is what clobbering
        // would look like from the facade's side.
        spe.Verify(s => s.ReplaceFileContentAsUserAsync(
            It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<Stream>(),
            MergedPayloadETag, It.IsAny<CancellationToken>()), Times.Once);
        spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                ConcurrentWriterETag, It.IsAny<CancellationToken>()), Times.Never,
            "retrying against the fresh etag is exactly the clobber #776 removes");
        spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never,
            "and never degrades to the etag-less blind PUT");
    }

    [Fact]
    public async Task ReplaceWithPrecondition_WhenRebaseEnabledAndVersionMoved_RetriesOnceAgainstTheFreshVersion()
    {
        // The SAVE path contract, unchanged by #776 — asserted here so the default stays last-writer-wins.
        // If this ever starts throwing, the save route has silently become a refusal again, which is the
        // 422 treadmill R4 removed.
        var spe = SpeThatRejectsThePrecondition();
        spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), ConcurrentWriterETag, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Handle("\"etag-after-retry\""));
        var sut = NewCoordinator(spe);

        var result = await sut.ReplaceWithPreconditionAsync(
            httpContext: null!, DriveId, ItemId, MergedBytes, MergedPayloadETag,
            CancellationToken.None);   // default rebaseOnConflict: true

        result!.ETag.Should().Be("\"etag-after-retry\"",
            "the save path rebased its edits onto the fresh bytes first, so last-writer-wins is sound there");
        spe.Verify(s => s.ReplaceFileContentAsUserAsync(
            It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<Stream>(),
            ConcurrentWriterETag, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceWithPrecondition_WithNoResolvedVersion_DegradesToABlindPutInBothModes()
    {
        // A null stamp means no metadata read happened; there is nothing to assert. Both modes degrade
        // to the pre-existing blind PUT rather than blocking the write — apply-template must not start
        // failing on paths that never had a version to compare.
        foreach (var rebase in new[] { true, false })
        {
            var spe = new Mock<ISpeFileOperations>();
            spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Handle("\"etag-blind\""));

            var result = await NewCoordinator(spe).ReplaceWithPreconditionAsync(
                httpContext: null!, DriveId, ItemId, MergedBytes, ifMatch: null,
                CancellationToken.None, rebaseOnConflict: rebase);

            result!.ETag.Should().Be("\"etag-blind\"", $"rebaseOnConflict={rebase} must not change the no-stamp path");
            spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
