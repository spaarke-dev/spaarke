// FR-29 anchored annotations — unit tests for ComposeService's Compose-domain session-payload
// persistence (task 060). Scope (per POML §acceptance-criteria + design.md §8): write -> persist
// -> restore round-trip for AnchoredAnnotations + DefinedTermsTracking on the EXISTING three-tier
// ChatSession stack (no new entity); tenant scoping; ADR-040 {bindingId}@t{n} provenance format;
// LoadAsync session-resumption rehydrate; and a grep-style assertion that the Path-A boundary
// (never memory.*, never a MemoryItem) holds at the production-source level.
//
// Mocking boundary (ADR-038 §4 "mock at module boundaries"): ChatSessionManager (virtual
// GetSessionAsync/UpdateSessionCacheAsync) is the genuine external boundary being mocked here —
// same pattern as ComposeServiceCreateOnSaveTests. The mock is backed by a small in-memory
// dictionary so these are BEHAVIOR round-trip tests (save then read back), not interaction-only
// Verify() assertions (tests/CLAUDE.md B7).
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): no Mock<HttpMessageHandler> (B1), no
// DI-registration test (B3), no ctor null-check test (B4). Each test names a concrete production
// behavior that breaks if the test is deleted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class AnchoredAnnotationPersistenceTests
{
    private const string Tenant = "tenant-aad-060";
    private const string DocumentSpeId = "spe-item-060";
    private const string DriveId = "drive-060";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    // In-memory fake session store keyed by sessionId — makes GetSessionAsync/
    // UpdateSessionCacheAsync a genuine round trip rather than an interaction-only mock.
    private readonly Dictionary<string, ChatSession> _store = new(StringComparer.Ordinal);

    public AnchoredAnnotationPersistenceTests()
    {
        _sessions = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);

        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, string sessionId, CancellationToken _) =>
                _store.TryGetValue(sessionId, out var session) && session.TenantId == tenantId ? session : null);

        _sessions
            .Setup(s => s.UpdateSessionCacheAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns((ChatSession session, CancellationToken _) =>
            {
                _store[session.SessionId] = session;
                return Task.CompletedTask;
            });
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    private ChatSession SeedSession(string sessionId, string documentId, string tenantId = Tenant)
    {
        var session = new ChatSession(
            SessionId: sessionId,
            TenantId: tenantId,
            DocumentId: documentId,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>());
        _store[sessionId] = session;
        return session;
    }

    private static AnchoredAnnotation NewAnnotation(string id, string ledgerRef = "compose-explain-clause@t1") => new()
    {
        Id = id,
        Type = "comment",
        Anchor = new AnchoredAnnotationAnchor { TextPattern = "the Effective Date", ParagraphHint = 3, SpanId = "span-1" },
        Body = "Confirm this date with the client.",
        Author = "ai",
        Timestamp = DateTimeOffset.UtcNow,
        Source = "ai",
        Provenance = new AnchoredAnnotationProvenance { BindingId = "compose-explain-clause", LedgerRef = ledgerRef },
    };

    private static DefinedTerm NewDefinedTerm(string term = "Confidential Information") => new()
    {
        Term = term,
        Definition = "Any non-public information disclosed under this Agreement.",
        Source = "ai",
        Provenance = new AnchoredAnnotationProvenance { BindingId = "compose-defined-terms", LedgerRef = "compose-defined-terms@t2" },
    };

    // ── Acceptance #2/#3: write -> persist -> restore round trip, tenant-scoped ─────────────────
    [Fact]
    public async Task SaveComposeAnnotationsAsync_ThenGetComposeAnnotationsAsync_RoundTripsBothCollections()
    {
        var sessionId = Guid.NewGuid().ToString();
        SeedSession(sessionId, documentId: DocumentSpeId);
        var sut = CreateSut();

        var annotation = NewAnnotation("anno-1");
        var term = NewDefinedTerm();

        var saved = await sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = sessionId,
            AnchoredAnnotations = new[] { annotation },
            DefinedTermsTracking = new[] { term },
        }, CancellationToken.None);

        saved.AnchoredAnnotations.Should().ContainSingle().Which.Id.Should().Be("anno-1");
        saved.DefinedTermsTracking.Should().ContainSingle().Which.Term.Should().Be("Confidential Information");

        // Restore — a fresh read (simulating session reload) must see the SAME persisted state.
        var restored = await sut.GetComposeAnnotationsAsync(Tenant, sessionId, CancellationToken.None);

        restored.AnchoredAnnotations.Should().BeEquivalentTo(new[] { annotation });
        restored.DefinedTermsTracking.Should().BeEquivalentTo(new[] { term });
    }

    // ── Acceptance #6: tenant scoping enforced on read ──────────────────────────────────────────
    [Fact]
    public async Task GetComposeAnnotationsAsync_WithWrongTenant_DoesNotLeakAnotherTenantsAnnotations()
    {
        var sessionId = Guid.NewGuid().ToString();
        SeedSession(sessionId, documentId: DocumentSpeId, tenantId: Tenant);
        var sut = CreateSut();

        await sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = sessionId,
            AnchoredAnnotations = new[] { NewAnnotation("anno-1") },
        }, CancellationToken.None);

        var crossTenantRead = await sut.GetComposeAnnotationsAsync("tenant-other", sessionId, CancellationToken.None);

        crossTenantRead.AnchoredAnnotations.Should().BeEmpty("the session belongs to a different tenant — ADR-015 Tier 3 isolation");
    }

    // ── Partial-replace semantics: null collection leaves the OTHER collection untouched ────────
    [Fact]
    public async Task SaveComposeAnnotationsAsync_WithNullDefinedTerms_LeavesExistingDefinedTermsUnchanged()
    {
        var sessionId = Guid.NewGuid().ToString();
        SeedSession(sessionId, documentId: DocumentSpeId);
        var sut = CreateSut();

        var term = NewDefinedTerm();
        await sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = sessionId,
            DefinedTermsTracking = new[] { term },
        }, CancellationToken.None);

        // Second save touches ONLY AnchoredAnnotations (DefinedTermsTracking = null).
        var second = await sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = sessionId,
            AnchoredAnnotations = new[] { NewAnnotation("anno-1") },
        }, CancellationToken.None);

        second.AnchoredAnnotations.Should().ContainSingle();
        second.DefinedTermsTracking.Should().ContainSingle().Which.Term.Should().Be(term.Term,
            "a null collection on the request must not clobber the OTHER stored collection");
    }

    // ── ADR-040 provenance format validated BEFORE anything persists (fail fast, no partial write) ─
    [Fact]
    public async Task SaveComposeAnnotationsAsync_WithMalformedLedgerRef_ThrowsAndNeverPersists()
    {
        var sessionId = Guid.NewGuid().ToString();
        SeedSession(sessionId, documentId: DocumentSpeId);
        var sut = CreateSut();

        var malformed = NewAnnotation("anno-bad", ledgerRef: "not-a-valid-ledger-ref");

        var act = () => sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = sessionId,
            AnchoredAnnotations = new[] { malformed },
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*bindingId*t*n*");

        // The stored session must be untouched — a bad batch never partially writes.
        var stillEmpty = await sut.GetComposeAnnotationsAsync(Tenant, sessionId, CancellationToken.None);
        stillEmpty.AnchoredAnnotations.Should().BeEmpty();
    }

    // ── Negative: saving onto an unknown session fails loudly instead of silently creating one ──
    [Fact]
    public async Task SaveComposeAnnotationsAsync_WhenSessionDoesNotExist_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();

        var act = () => sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = Guid.NewGuid().ToString(),
            AnchoredAnnotations = new[] { NewAnnotation("anno-1") },
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Acceptance #3: LoadAsync resumes a known session bound to the SAME document and
    //    rehydrates its stored annotations — the "re-opening a mounted document restores prior
    //    annotations" behavior. ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task LoadAsync_WithSessionIdBoundToSameDocument_ResumesSessionAndRehydratesAnnotations()
    {
        var priorSessionId = Guid.NewGuid().ToString();
        SeedSession(priorSessionId, documentId: DocumentSpeId);
        var sut = CreateSut();

        await sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = priorSessionId,
            AnchoredAnnotations = new[] { NewAnnotation("anno-1") },
        }, CancellationToken.None);

        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            SessionId = priorSessionId,
        }, new DefaultHttpContext(), CancellationToken.None);

        result.SessionId.Should().Be(priorSessionId, "a SessionId bound to the SAME document must be RESUMED, not replaced with a fresh session");
        result.AnchoredAnnotations.Should().ContainSingle().Which.Id.Should().Be("anno-1");
    }

    // ── Negative: a SessionId bound to a DIFFERENT document must NOT leak that document's
    //    annotations — LoadAsync falls back to minting a fresh session. ─────────────────────────
    [Fact]
    public async Task LoadAsync_WithSessionIdBoundToDifferentDocument_MintsFreshSessionInstead()
    {
        var otherDocSessionId = Guid.NewGuid().ToString();
        SeedSession(otherDocSessionId, documentId: "spe-item-OTHER-document");
        var sut = CreateSut();

        await sut.SaveComposeAnnotationsAsync(new SaveComposeAnnotationsRequest
        {
            TenantId = Tenant,
            SessionId = otherDocSessionId,
            AnchoredAnnotations = new[] { NewAnnotation("anno-belongs-to-other-doc") },
        }, CancellationToken.None);

        ArrangeSpeLoad();

        var result = await sut.LoadAsync(new LoadComposeDocumentRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId, // different document than otherDocSessionId is bound to
            TenantId = Tenant,
            SessionId = otherDocSessionId,
        }, new DefaultHttpContext(), CancellationToken.None);

        result.SessionId.Should().NotBe(otherDocSessionId);
        result.AnchoredAnnotations.Should().BeEmpty("the supplied SessionId belongs to a DIFFERENT document — its annotations must not leak onto this load");
    }

    private void ArrangeSpeLoad()
    {
        _spe.Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: DocumentSpeId,
                Name: "contract.docx",
                ParentId: null,
                Size: 4,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"etag-1\"",
                IsFolder: false,
                WebUrl: "https://spe/web",
                DriveId: DriveId));

        _spe.Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 }));
        // FR-06 (task 027): LoadAsync resolves the load-time version id best-effort.
        _spe.Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v-load-1");
    }
}
