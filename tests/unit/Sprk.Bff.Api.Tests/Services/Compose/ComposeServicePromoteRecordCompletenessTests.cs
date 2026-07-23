// UAT #7/#8/#9 regression guard — ComposeService create-on-save must write a COMPLETE sprk_document.
//
// Root cause (one bug, three symptoms): SaveAsync uploaded the file to SPE correctly but the
// promoted sprk_document row carried ONLY sprk_graphitemid — never sprk_graphdriveid, never
// sprk_hasfile=true, never the file metadata (size/mime/filepath). Every downstream reader
// (open-in-Word links, preview) validates the SPE pointer, found drive-id empty + has-file false,
// and threw HTTP 409 "No file is attached to this document yet." Save appeared to do nothing.
//
// These tests capture the Entity handed to IGenericEntityService.CreateAsync during the first Save
// and assert the record is complete. If a future change drops any of the threaded SPE-pointer /
// metadata fields, the 409s return and these tests fail — a concrete, caller-visible behavior.
//
// Field set + Dataverse logical column names mirror the canonical
// OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync write (Services/Office):
//   sprk_graphdriveid / sprk_graphitemid / sprk_filename / sprk_filesize (int) /
//   sprk_mimetype / sprk_hasfile (bool) / sprk_filepath.
//
// Mocking boundary: identical genuine-external-boundary set as ComposeServiceUploadFidelityTests
// (ISpeFileOperations, IGenericEntityService, ChatSessionManager, IPostUploadIndexingEnqueuer) —
// ADR-038 §4 module-boundary mocking, not the banned in-process-collaborator mocking.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeServicePromoteRecordCompletenessTests
{
    private const string Tenant = "tenant-aad-completeness";
    private const string ContainerId = "b!container-completeness";
    private const string ResolvedDriveId = "drive-completeness-abc";
    private const string NewSpeItemId = "spe-item-completeness-new";
    private const string WebUrl = "https://contoso.sharepoint.com/spe/draft.docx";
    private const long UploadedSize = 4242;

    private const string DocxMime =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    public ComposeServicePromoteRecordCompletenessTests()
    {
        _sessions = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);

        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    private static ReadOnlyMemory<byte> DraftBytes() =>
        new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x01, 0x02, 0x03 };

    private static FileHandleDto UploadedDriveItem() => new(
        Id: NewSpeItemId,
        Name: "Contract draft.docx",
        ParentId: null,
        Size: UploadedSize,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-completeness-v1\"",
        IsFolder: false,
        WebUrl: WebUrl,
        DriveId: ResolvedDriveId);

    private void ArrangeContainerCreate()
    {
        _spe.Setup(s => s.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedDriveId);
        _spe.Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), ResolvedDriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadedDriveItem());
    }

    private Func<Entity> ArrangeNoExistingRecordThenCaptureCreate(Guid newId)
    {
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);

        Entity? captured = null;
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(newId);

        return () => captured ?? throw new InvalidOperationException("CreateAsync was never invoked.");
    }

    private void ArrangeIndexingSubmitted() =>
        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

    // ── The core regression: the created record carries a NON-EMPTY drive-id AND has-file=true ──
    [Fact]
    public async Task SaveAsync_FirstSaveOfTransientDraft_CreatesRecordWithDriveIdAndHasFileTrue()
    {
        ArrangeContainerCreate();
        var captureCreated = ArrangeNoExistingRecordThenCaptureCreate(Guid.NewGuid());
        ArrangeIndexingSubmitted();

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,           // transient draft — first Save runs create-on-save
            ContainerId = ContainerId,
            Content = DraftBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
            DisplayName = "Contract draft",
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        var created = captureCreated();

        created.GetAttributeValue<string>("sprk_graphdriveid")
            .Should().Be(ResolvedDriveId, "the drive-id must be persisted or downstream readers 409 'no file attached'");
        created.GetAttributeValue<bool>("sprk_hasfile")
            .Should().BeTrue("has-file gates open-links + preview; a false/absent flag is the 409 root cause");
    }

    // ── The file metadata (size / mime / filepath / item-id / file-name) is also complete ───────
    [Fact]
    public async Task SaveAsync_FirstSaveOfTransientDraft_CreatesRecordWithFullFileMetadata()
    {
        ArrangeContainerCreate();
        var captureCreated = ArrangeNoExistingRecordThenCaptureCreate(Guid.NewGuid());
        ArrangeIndexingSubmitted();

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DraftBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
            DisplayName = "Contract draft",
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        var created = captureCreated();

        // SPE item id (was the ONLY field written before the fix — must still be present).
        created.GetAttributeValue<string>("sprk_graphitemid").Should().Be(NewSpeItemId);
        // sprk_filesize is a Whole Number column → written as int (cast from the upload's long size).
        created.GetAttributeValue<int>("sprk_filesize").Should().Be((int)UploadedSize);
        created.GetAttributeValue<string>("sprk_mimetype").Should().Be(DocxMime);
        created.GetAttributeValue<string>("sprk_filepath").Should().Be(WebUrl);
        // File name is the SPE-returned name (carries the .docx extension), not the bare display name.
        created.GetAttributeValue<string>("sprk_filename").Should().Be("Contract draft.docx");
    }
}
