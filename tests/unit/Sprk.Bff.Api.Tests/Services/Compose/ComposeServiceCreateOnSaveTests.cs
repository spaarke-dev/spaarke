// FR-05 create-on-save backbone — unit tests for ComposeService.SaveAsync (task 013).
//
// Scope (per POML §acceptance-criteria + NFR-08): exercise the create-on-save backbone that
// task 013 added to ComposeService.SaveAsync — client-supplied container (Fork A), transient
// drive-item creation (Fork B), idempotent record promotion, sync-OBO indexing, deferred
// profile-analysis (Fork C), and the per-step JobAwareCompletionState projection + interim
// R5-E bar. These are BEHAVIOR tests (drive-item created? record created? each step's projected
// state? interim success?), not wiring tests.
//
// Mocking boundary (ADR-038 §4 "mock at module boundaries" — NOT the banned in-process-collaborator
// mocking of §4 B5): every collaborator mocked here is a genuine external boundary —
//   • ISpeFileOperations  → SPE / Graph facade (ADR-007)
//   • IGenericEntityService → Dataverse
//   • ChatSessionManager (virtual GetSessionAsync) → Redis-backed session store
//   • IPostUploadIndexingEnqueuer → the RAG indexing seam
// The endpoint-contract layer (ComposeEndpointsContractTests) mocks IComposeService wholesale and
// therefore cannot reach SaveAsync's internal backbone; this unit suite is the right tool for it.
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): no Mock<HttpMessageHandler> (B1), no
// typed-HttpClient mock (B2), no DI-registration test (B3), no ctor null-check test (B4), no
// mirror/getter/coverage-filler shapes (B6/B10/B16). Each test names a concrete production
// behavior that breaks if the test is deleted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeServiceCreateOnSaveTests
{
    private const string Tenant = "tenant-aad-013";
    private const string ContainerId = "b!container-abc";
    private const string ResolvedDriveId = "drive-xyz";
    private const string NewSpeItemId = "spe-item-new-1";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;
    // FR-05 Fork C (compose-r2): the profile step now ENQUEUES an AppOnlyDocumentAnalysis job
    // (SubmitJobAsync is virtual precisely for this override). Loose so un-arranged calls return a
    // completed Task; profile-focused tests capture the submitted JobContract.
    private readonly Mock<JobSubmissionService> _jobSubmission;

    public ComposeServiceCreateOnSaveTests()
    {
        _sessions = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);

        // The rebind step looks up the session; a null session is a benign no-op (warns + returns).
        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        _jobSubmission = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            Options.Create(new ServiceBusOptions()),
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance,
        jobSubmission: _jobSubmission.Object);

    private static ReadOnlyMemory<byte> DocxBytes() =>
        new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 }; // DOCX ZIP signature

    private static FileHandleDto NewDriveItem() => new(
        Id: NewSpeItemId,
        Name: "draft.docx",
        ParentId: null,
        Size: 1234,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-v1\"",
        IsFolder: false,
        WebUrl: "https://spe/web",
        DriveId: ResolvedDriveId);

    private void ArrangeContainerCreate()
    {
        _spe.Setup(s => s.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedDriveId);
        _spe.Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), ResolvedDriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewDriveItem());
    }

    private void ArrangeNoExistingRecordThenCreate(Guid newId)
    {
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);
    }

    private void ArrangeIndexing(PostUploadIndexingResult result)
    {
        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static JobAwareState StateOf(JobAwareCompletionState completion, string stepName) =>
        completion.Steps.Single(s => s.StepName == stepName).State;

    // ── Acceptance #1 + #2 (happy path): transient draft → drive-item + record + index ─────────
    [Fact]
    public async Task SaveAsync_TransientDraftWithClientContainer_CreatesDriveItemRecordAndIndexes_InterimSuccess()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,           // transient draft — no SPE item yet
            ContainerId = ContainerId,      // client-supplied (Fork A)
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
            DisplayName = "Draft",
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        // Fork B: the drive-item was created (not replaced).
        _spe.Verify(s => s.UploadSmallAsUserAsync(
            It.IsAny<HttpContext>(), ResolvedDriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);

        result.DocumentSpeId.Should().Be(NewSpeItemId);
        result.DriveId.Should().Be(ResolvedDriveId);
        result.DocumentRecordId.Should().Be(recordId);
        result.WasPromotedThisSave.Should().BeTrue();

        var completion = result.CompletionState!;
        StateOf(completion, ComposeService.StepContainer).Should().Be(JobAwareState.Completed);
        StateOf(completion, ComposeService.StepRecord).Should().Be(JobAwareState.Completed);
        StateOf(completion, ComposeService.StepIndexing).Should().Be(JobAwareState.Completed);
        // profile-analysis deferred → non-terminal → aggregate Partial (record exists, downstream pending).
        completion.Aggregate.Should().Be(JobAwareState.Partial);
        ComposeService.IsInterimCreateOnSaveSuccess(completion).Should().BeTrue();
    }

    // ── Acceptance (Fork C, compose-r2): profile-analysis ENQUEUES a background profile job ─────
    [Fact]
    public async Task SaveAsync_TransientDraft_EnqueuesProfileJobForCreatedDocument()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        JobContract? submitted = null;
        _jobSubmission
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((j, _) => submitted = j)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        // The SAME AppOnlyDocumentAnalysis job the Office save path enqueues — keyed to the created
        // sprk_document id, idempotency analysis-{docId}-documentprofile, so the app-only/MI worker
        // resolves the SPE pointers from Dataverse and profiles the file.
        _jobSubmission.Verify(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Once);
        submitted.Should().NotBeNull();
        submitted!.JobType.Should().Be(AppOnlyDocumentAnalysisJobHandler.JobTypeName);
        submitted.SubjectId.Should().Be(recordId.ToString());
        submitted.IdempotencyKey.Should().Be($"analysis-{recordId}-documentprofile");
        submitted.Payload!.RootElement.GetProperty("DocumentId").GetGuid().Should().Be(recordId);

        // The profile step is non-terminal (Queued — enqueued, worker not yet run); aggregate stays Partial.
        var profile = result.CompletionState!.Steps.Single(s => s.StepName == ComposeService.StepProfileAnalysis);
        profile.State.Should().Be(JobAwareState.Queued);
        profile.Detail.Should().Contain("enqueued");
        result.CompletionState!.Aggregate.Should().Be(JobAwareState.Partial);
    }

    // ── Acceptance (best-effort): a profile-enqueue failure NEVER fails the save ────────────────
    [Fact]
    public async Task SaveAsync_WhenProfileEnqueueThrows_SaveStillSucceeds_ProfileNonTerminal()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        _jobSubmission
            .Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service Bus unavailable"));

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        // Save is unaffected: record created + indexed → interim success; profile degrades to Queued.
        result.DocumentRecordId.Should().Be(recordId);
        var profile = result.CompletionState!.Steps.Single(s => s.StepName == ComposeService.StepProfileAnalysis);
        profile.State.Should().Be(JobAwareState.Queued);
        profile.Detail.Should().Contain("failed");
        ComposeService.IsInterimCreateOnSaveSuccess(result.CompletionState!).Should().BeTrue();
    }

    // ── Acceptance (negative): missing client container fails the container step honestly ───────
    [Fact]
    public async Task SaveAsync_TransientDraftWithoutContainer_FailsContainerStep_NeverSuccess()
    {
        // No SPE facade / Dataverse / indexing calls should occur — Strict mocks assert that.
        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = null,             // missing — no server-side resolver, so container step fails
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        _spe.Verify(s => s.UploadSmallAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _dataverse.Verify(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);

        result.DocumentRecordId.Should().BeNull();
        result.WasPromotedThisSave.Should().BeFalse();
        var completion = result.CompletionState!;
        StateOf(completion, ComposeService.StepContainer).Should().Be(JobAwareState.Failed);
        completion.Aggregate.Should().Be(JobAwareState.Failed);
        ComposeService.IsInterimCreateOnSaveSuccess(completion).Should().BeFalse();
    }

    // ── Acceptance (negative interim R5-E): a record with no index is never a success ───────────
    [Fact]
    public async Task SaveAsync_WhenIndexingFails_RecordCreatedButNeverReturnedAsSuccess()
    {
        ArrangeContainerCreate();
        ArrangeNoExistingRecordThenCreate(Guid.NewGuid());
        ArrangeIndexing(PostUploadIndexingResult.Failed("Graph 500"));

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        var completion = result.CompletionState!;
        StateOf(completion, ComposeService.StepRecord).Should().Be(JobAwareState.Completed, "the row was created");
        StateOf(completion, ComposeService.StepIndexing).Should().Be(JobAwareState.Failed);
        completion.Aggregate.Should().Be(JobAwareState.Failed);
        ComposeService.IsInterimCreateOnSaveSuccess(completion).Should().BeFalse("no index → never a success");
    }

    // ── Acceptance: existing drive-item path replaces content, does NOT create a drive-item ─────
    [Fact]
    public async Task SaveAsync_ExistingDriveItem_ReplacesContent_DoesNotCreateDriveItem()
    {
        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), "drive-existing", "spe-existing", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto("spe-existing", "existing.docx", null, 99, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "\"e2\"", false, null, "drive-existing"));
        ArrangeNoExistingRecordThenCreate(Guid.NewGuid());
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = "spe-existing",
            DriveId = "drive-existing",
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
            It.IsAny<HttpContext>(), "drive-existing", "spe-existing", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _spe.Verify(s => s.UploadSmallAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        ComposeService.IsInterimCreateOnSaveSuccess(result.CompletionState!).Should().BeTrue();
    }

    // ── Acceptance: idempotency — re-Save of an already-promoted document does not double-create ─
    [Fact]
    public async Task SaveAsync_ReSaveWhenRowAlreadyExists_DoesNotDoubleCreateRow()
    {
        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), "drive-existing", "spe-existing", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto("spe-existing", "existing.docx", null, 99, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "\"e3\"", false, null, "drive-existing"));

        var existing = new Entity("sprk_document") { Id = Guid.NewGuid() };
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = "spe-existing",
            DriveId = "drive-existing",
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        _dataverse.Verify(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        result.DocumentRecordId.Should().Be(existing.Id);
        result.WasPromotedThisSave.Should().BeFalse("the row already existed — idempotent re-Save");
    }

    // ── Acceptance: creation does not require a parent association (standalone Document is valid) ─
    [Fact]
    public async Task SaveAsync_TransientDraft_IndexesWithoutParentAssociation()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);

        PostUploadIndexingRequest? captured = null;
        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<PostUploadIndexingRequest, HttpContext, CancellationToken>((r, _, _) => captured = r)
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.DocumentRecordId.Should().Be(recordId, "a standalone Document is created without a parent");
        captured.Should().NotBeNull();
        captured!.ParentEntity.Should().BeNull("parent association is task 014 — not required for creation");
        captured.DocumentId.Should().Be(recordId.ToString());
    }

    // ── Precondition: empty content is rejected before any I/O ─────────────────────────────────
    [Fact]
    public async Task SaveAsync_WhenContentEmpty_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = ReadOnlyMemory<byte>.Empty,
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var act = () => sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
