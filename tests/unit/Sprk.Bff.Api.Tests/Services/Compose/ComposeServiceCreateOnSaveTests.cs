// FR-05 create-on-save backbone — unit tests for ComposeService.SaveAsync (task 013).
//
// Scope (per POML §acceptance-criteria + NFR-08): exercise the create-on-save backbone that
// task 013 added to ComposeService.SaveAsync — client-supplied container (Fork A), transient
// drive-item creation (Fork B), idempotent record promotion, sync-OBO indexing, FIRE-AND-FORGET
// background profile-analysis (Fork C, compose-r2), and the per-step JobAwareCompletionState
// projection + interim R5-E bar. These are BEHAVIOR tests (drive-item created? record created?
// each step's projected state? profiling dispatched off the response path? interim success?),
// not wiring tests.
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
using Microsoft.Extensions.DependencyInjection;
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
    // FR-05 Fork C (compose-r2, UAT #7b): the profile step now runs INLINE under OBO via the
    // IDocumentProfileAi facade (Services/Ai/PublicContracts) — the module boundary ComposeService
    // consumes for profiling. Loose so un-arranged calls fall through to the default success setup
    // below; profile-focused tests override it.
    private readonly Mock<IDocumentProfileAi> _documentProfile = new();

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

        // Default: profiling succeeds (fields written under OBO) → profile step is terminal Completed.
        // Individual tests override this to capture args, simulate a skip/failure, or throw.
        _documentProfile
            .Setup(p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentProfileOutcome.Succeeded());
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance,
        documentProfileAi: _documentProfile.Object);

    // Fire-and-forget SUT (compose-r2): profiling is DISPATCHED to a detached DI scope, not awaited.
    // A real IServiceScopeFactory whose provider resolves the supplied gated fake facade lets us assert
    // the background dispatch deterministically (gate the fake with a TCS — no real delays). The
    // returned provider is disposed by the test once the background task has settled.
    private ComposeService CreateSutWithBackgroundProfile(IDocumentProfileAi facade, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IDocumentProfileAi>(_ => facade);
        provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new ComposeService(
            _spe.Object,
            _sessions.Object,
            _dataverse.Object,
            new DocxAnnotationWriter(),
            _indexing.Object,
            NullLogger<ComposeService>.Instance,
            documentProfileAi: facade,   // non-null availability gate
            scopeFactory: scopeFactory);
    }

    private static DefaultHttpContext HttpContextWithBearer(string authorization = "Bearer obo-user-token")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = authorization;
        return ctx;
    }

    /// <summary>
    /// A gate-controlled <see cref="IDocumentProfileAi"/> fake for the fire-and-forget profile tests.
    /// It records the document id + the (detached) HttpContext + its Authorization header on invocation,
    /// signals <see cref="Started"/>, then blocks on a release gate before returning/throwing — so a
    /// test can deterministically observe that SaveAsync returned WITHOUT awaiting profiling, then release
    /// the gate. No real time is used (pure TCS gating, per tests/CLAUDE.md).
    /// </summary>
    private sealed class GatedProfileFake : IDocumentProfileAi
    {
        private readonly TaskCompletionSource _startedGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finishedGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly DocumentProfileOutcome? _outcome;
        private readonly Exception? _throw;

        public GatedProfileFake(DocumentProfileOutcome? outcome = null, Exception? toThrow = null)
        {
            _outcome = outcome;
            _throw = toThrow;
        }

        public Guid? InvokedDocumentId { get; private set; }
        public HttpContext? InvokedContext { get; private set; }
        public string? InvokedAuthorization { get; private set; }
        public Task Started => _startedGate.Task;
        public Task Finished => _finishedGate.Task;
        public void Release() => _releaseGate.TrySetResult();

        public async Task<DocumentProfileOutcome> ProfileDocumentAsUserAsync(
            Guid documentId, HttpContext httpContext, CancellationToken cancellationToken)
        {
            InvokedDocumentId = documentId;
            InvokedContext = httpContext;
            InvokedAuthorization = httpContext.Request.Headers.Authorization.ToString();
            _startedGate.TrySetResult();
            try
            {
                await _releaseGate.Task.ConfigureAwait(false);
                if (_throw is not null)
                {
                    throw _throw;
                }
                return _outcome ?? DocumentProfileOutcome.Succeeded();
            }
            finally
            {
                _finishedGate.TrySetResult();
            }
        }
    }

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
        // profile-analysis is now FIRE-AND-FORGET (dispatched to a background scope, not awaited). This
        // SUT has no scope factory, so the profile is not dispatched here → non-terminal (Queued); the
        // dispatched (Running) path is exercised by the dedicated fire-and-forget tests below. Either
        // way the profile step is non-terminal, so the SYNCHRONOUS aggregate reads Partial (record +
        // index exist, profile pending) — never Completed-on-claim — and the interim R5-E bar holds.
        StateOf(completion, ComposeService.StepProfileAnalysis).Should().Be(JobAwareState.Queued);
        completion.Aggregate.Should().Be(JobAwareState.Partial);
        ComposeService.IsInterimCreateOnSaveSuccess(completion).Should().BeTrue();
    }

    // ── Acceptance (Fork C, compose-r2): profile is DISPATCHED fire-and-forget; the save returns
    //    WITHOUT awaiting it, on a DETACHED context carrying the captured OBO bearer token ──────────
    [Fact]
    public async Task SaveAsync_TransientDraft_DispatchesProfileFireAndForget_ReturnsWithoutAwaiting()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // The fake blocks on its release gate; if SaveAsync awaited profiling, the await below would
        // deadlock and the test would time out — so completing at all proves fire-and-forget.
        var fake = new GatedProfileFake();
        var sut = CreateSutWithBackgroundProfile(fake, out var provider);
        var httpContext = HttpContextWithBearer("Bearer obo-user-token");
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, httpContext, CancellationToken.None);

        // Returned synchronously while the background profile is still blocked: record + index are the
        // interim success bar; the profile step is the non-terminal "dispatched" (Running) signal, so
        // the aggregate reads Partial (profile pending), not Completed.
        result.DocumentRecordId.Should().Be(recordId);
        var profile = result.CompletionState!.Steps.Single(s => s.StepName == ComposeService.StepProfileAnalysis);
        profile.State.Should().Be(JobAwareState.Running);
        profile.Detail.Should().Contain("background");
        result.CompletionState!.Aggregate.Should().Be(JobAwareState.Partial);
        ComposeService.IsInterimCreateOnSaveSuccess(result.CompletionState!).Should().BeTrue();

        // The background profile runs on a DETACHED HttpContext (not the request one) carrying the
        // captured OBO bearer token — proving the user assertion survives the response boundary (OBO).
        await fake.Started.WaitAsync(TimeSpan.FromSeconds(5));
        fake.InvokedDocumentId.Should().Be(recordId);
        fake.InvokedContext.Should().NotBeSameAs(httpContext,
            "the profile runs on a synthetic detached HttpContext, not the disposed request one");
        fake.InvokedAuthorization.Should().Be("Bearer obo-user-token",
            "the OBO user assertion is captured before the response and threaded into the detached context");

        fake.Release();
        await fake.Finished.WaitAsync(TimeSpan.FromSeconds(5));
        provider.Dispose();
    }

    // ── Acceptance (constraint #2): no bearer token on the save → do NOT dispatch a doomed OBO task ─
    [Fact]
    public async Task SaveAsync_WhenNoBearerToken_DoesNotDispatchBackgroundProfile()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        var fake = new GatedProfileFake();
        var sut = CreateSutWithBackgroundProfile(fake, out var provider);
        var httpContext = new DefaultHttpContext();   // no Authorization header
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, httpContext, CancellationToken.None);

        // A background OBO download with no user token would 401 — so the profile is NOT dispatched;
        // the step degrades to a non-terminal not-attempted signal and the fake is never invoked.
        var profile = result.CompletionState!.Steps.Single(s => s.StepName == ComposeService.StepProfileAnalysis);
        profile.State.Should().Be(JobAwareState.Queued);
        profile.Detail.Should().Contain("Authorization");
        fake.Started.IsCompleted.Should().BeFalse("no dispatch occurred, so the facade was never invoked");
        fake.InvokedDocumentId.Should().BeNull();
        result.DocumentRecordId.Should().Be(recordId);
        ComposeService.IsInterimCreateOnSaveSuccess(result.CompletionState!).Should().BeTrue();

        provider.Dispose();
    }

    // ── Acceptance (constraint #4, best-effort): a THROWING background profile never affects the save
    //    result and never crashes the process (unobserved-exception safe) ──────────────────────────
    [Fact]
    public async Task SaveAsync_WhenBackgroundProfileThrows_SaveResultUnaffected_ExceptionSwallowed()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        var fake = new GatedProfileFake(toThrow: new InvalidOperationException("OBO exchange failed"));
        fake.Release();   // let it throw as soon as the background task reaches it
        var sut = CreateSutWithBackgroundProfile(fake, out var provider);
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        // SaveAsync completes normally — the background throw happens off the response path.
        var result = await sut.SaveAsync(request, HttpContextWithBearer(), CancellationToken.None);

        result.DocumentRecordId.Should().Be(recordId);
        ComposeService.IsInterimCreateOnSaveSuccess(result.CompletionState!).Should().BeTrue();

        // The background task runs, throws, and the exception is SWALLOWED by RunBackgroundProfileAsync
        // (never rethrown) — awaiting Finished completes without faulting the test.
        await fake.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await fake.Finished.WaitAsync(TimeSpan.FromSeconds(5));
        provider.Dispose();
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

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // FR-30 durable memory-capture (compose-r2 task 063, deferral #629) — the STEP 5 capture leg
    // of SaveAsync (ComposeService.CaptureDocumentMemoryAsync). These exercise the invariant +
    // distillation logic at the SaveAsync INTEGRATION point — the facade-layer tests
    // (ComposeMemoryCapture*) can't reach the SaveAsync seam that owns the "capture NEVER fails a
    // Save" contract and the defined-term → ComposeInsight distillation. Reuses the harness above
    // (SPE / Dataverse / indexing / session mocks + Arrange* helpers) — the session mock is extended
    // (ArrangeBoundSession) to return a ChatSession carrying DefinedTermsTracking, which the base
    // ctor setup returns as null.
    //
    // Banned-pattern compliance (tests/CLAUDE.md): each test names a concrete production behavior
    // that breaks if deleted — the best-effort invariant (B-none: a real regression class), the
    // distillation filter (Term+Definition), the origin mapping, and the provenance stamping. The
    // memory store is mocked ONLY at its genuine module boundary (IComposeMemoryCapture, the ADR-013
    // PublicContracts facade) — not the class-under-test's own collaborators (§4 B5 / B7).
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A test double for the ADR-013 <see cref="IComposeMemoryCapture"/> module boundary. Records the
    /// (subjectType, subjectId, insights, provenance) of the single expected capture call and returns a
    /// <c>Captured(n)</c> outcome — or THROWS when constructed to, to prove the Save swallows it.
    /// </summary>
    private sealed class RecordingMemoryCapture : IComposeMemoryCapture
    {
        private readonly bool _throw;

        public RecordingMemoryCapture(bool throwOnCapture = false) => _throw = throwOnCapture;

        public int CallCount { get; private set; }
        public string? SubjectType { get; private set; }
        public string? SubjectId { get; private set; }
        public IReadOnlyList<ComposeInsight>? Insights { get; private set; }
        public ComposeMemoryProvenance? Provenance { get; private set; }

        public Task<ComposeMemoryCaptureOutcome> CaptureRecordInsightsAsync(
            string subjectType,
            string subjectId,
            IReadOnlyList<ComposeInsight> insights,
            ComposeMemoryProvenance provenance,
            CancellationToken ct = default)
        {
            CallCount++;
            SubjectType = subjectType;
            SubjectId = subjectId;
            Insights = insights;
            Provenance = provenance;
            if (_throw)
            {
                throw new InvalidOperationException("memory store unavailable");
            }
            return Task.FromResult(ComposeMemoryCaptureOutcome.Captured(insights.Count));
        }
    }

    // Same construction as CreateSut() (facade non-null availability gate, no scope factory so the
    // fire-and-forget profile stays a no-op) plus the FR-30 memory-capture facade under test.
    private ComposeService CreateSutWithMemoryCapture(IComposeMemoryCapture? memoryCapture) => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance,
        documentProfileAi: _documentProfile.Object,
        memoryCapture: memoryCapture);

    // Extend the harness's session fake so the bound session carries DefinedTermsTracking. The base
    // ctor setup returns null; CaptureDocumentMemoryAsync loads the session via this SAME
    // GetSessionAsync(tenantId, sessionId, ct) call and reads its DefinedTermsTracking.
    private void ArrangeBoundSession(ChatSession session) =>
        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

    private static ChatSession SessionWithTerms(string sessionId, params DefinedTerm[] terms) => new(
        SessionId: sessionId,
        TenantId: Tenant,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>())
    {
        DefinedTermsTracking = terms,
    };

    private static DefinedTerm AiTerm(string term, string? definition, string? bindingId = null, string? ledgerRef = null) => new()
    {
        Term = term,
        Definition = definition,
        Source = "ai",
        Provenance = bindingId is null
            ? null
            : new AnchoredAnnotationProvenance { BindingId = bindingId, LedgerRef = ledgerRef! },
    };

    private static DefinedTerm HumanTerm(string term, string? definition) => new()
    {
        Term = term,
        Definition = definition,
        Source = "human",
    };

    // ── FR-30 invariant: a THROWING memory capture NEVER fails a Save (the load-bearing contract) ──
    [Fact]
    public async Task SaveAsync_WhenMemoryCaptureThrows_SaveResultUnaffected()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        var sessionId = Guid.NewGuid().ToString();
        ArrangeBoundSession(SessionWithTerms(sessionId,
            AiTerm("Confidential Information", "information marked confidential")));

        var capture = new RecordingMemoryCapture(throwOnCapture: true);
        var sut = CreateSutWithMemoryCapture(capture);
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = sessionId,
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        // The load-bearing invariant: the promoted record + interim success stand even though the
        // best-effort STEP 5 capture threw — CaptureDocumentMemoryAsync swallowed it, no exception
        // reached the response path.
        result.DocumentRecordId.Should().Be(recordId);
        result.WasPromotedThisSave.Should().BeTrue();
        ComposeService.IsInterimCreateOnSaveSuccess(result.CompletionState!).Should().BeTrue();
        capture.CallCount.Should().Be(1, "capture was attempted (and threw) — proving the Save absorbed the failure");
    }

    // ── FR-30 distillation: only terms with BOTH a term AND a definition become Record-scope insights,
    //    with correct origin mapping + provenance stamping ──────────────────────────────────────────
    [Fact]
    public async Task SaveAsync_DistillsDefinedTermsWithDefinitions_IntoRecordScopeCapture()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        var sessionId = Guid.NewGuid().ToString();
        ArrangeBoundSession(SessionWithTerms(sessionId,
            AiTerm("Confidential Information", "information marked confidential", bindingId: "binding-1", ledgerRef: "binding-1@t2"), // (a) ai + definition
            HumanTerm("Effective Date", "the date the agreement takes effect"),                                                     // (b) human + definition
            HumanTerm("Undefined Term", definition: "   "),                                                                         // (c) whitespace definition → skipped
            AiTerm("   ", "an orphan definition with no term")));                                                                    // (d) whitespace term → skipped

        var capture = new RecordingMemoryCapture();
        var sut = CreateSutWithMemoryCapture(capture);
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = sessionId,
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.DocumentRecordId.Should().Be(recordId);

        // Exactly one capture, keyed by the Record-scope subject = the promoted document.
        capture.CallCount.Should().Be(1);
        capture.SubjectType.Should().Be("sprk_document");
        capture.SubjectId.Should().Be(recordId.ToString());

        // Only (a) + (b) survive distillation; (c) and (d) are dropped.
        capture.Insights.Should().HaveCount(2, "only terms carrying BOTH a term and a definition are durable");

        var ai = capture.Insights!.Single(i => i.Key == "Confidential Information");
        ai.FactType.Should().Be("defined-term");
        ai.Value.Should().Be("information marked confidential", "Value maps from the term's Definition");
        ai.Origin.Should().Be(MemoryOrigin.AiDerived, "an ai-sourced term is AiDerived");
        ai.BindingId.Should().Be("binding-1", "provenance BindingId threads through from the term");
        ai.LedgerRef.Should().Be("binding-1@t2", "provenance LedgerRef threads through from the term");

        var human = capture.Insights!.Single(i => i.Key == "Effective Date");
        human.Value.Should().Be("the date the agreement takes effect");
        human.Origin.Should().Be(MemoryOrigin.User, "a human-sourced term is User origin");

        // Provenance envelope carries the request tenant + originating session.
        capture.Provenance!.TenantId.Should().Be(Tenant);
        capture.Provenance!.SessionId.Should().Be(sessionId);
    }

    // ── FR-30 availability gate: no facade registered → Save succeeds, capture is a clean no-op ─────
    [Fact]
    public async Task SaveAsync_WhenNoMemoryCaptureRegistered_SaveSucceeds_NoOp()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        var sessionId = Guid.NewGuid().ToString();
        // A bound session WITH a durable term — proving the null gate (not an empty session) is what
        // short-circuits capture.
        ArrangeBoundSession(SessionWithTerms(sessionId,
            AiTerm("Confidential Information", "information marked confidential")));

        var sut = CreateSutWithMemoryCapture(memoryCapture: null); // default: no facade in this host
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = sessionId,
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.DocumentRecordId.Should().Be(recordId);
        ComposeService.IsInterimCreateOnSaveSuccess(result.CompletionState!).Should().BeTrue(
            "a missing memory-capture facade must never affect the Save");
    }

    // ── FR-30 precondition: a bound session with no defined terms → capture is never invoked ────────
    [Fact]
    public async Task SaveAsync_WhenSessionHasNoDefinedTerms_DoesNotCallCapture()
    {
        ArrangeContainerCreate();
        var recordId = Guid.NewGuid();
        ArrangeNoExistingRecordThenCreate(recordId);
        ArrangeIndexing(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
        var sessionId = Guid.NewGuid().ToString();
        ArrangeBoundSession(SessionWithTerms(sessionId)); // no terms

        var capture = new RecordingMemoryCapture();
        var sut = CreateSutWithMemoryCapture(capture);
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = null,
            ContainerId = ContainerId,
            Content = DocxBytes(),
            SessionId = sessionId,
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.DocumentRecordId.Should().Be(recordId);
        capture.CallCount.Should().Be(0, "no defined terms → nothing to distil → the facade is never invoked");
    }
}
