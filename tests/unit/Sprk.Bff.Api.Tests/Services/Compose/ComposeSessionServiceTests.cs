using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeSessionService"/>.
///
/// <para>
/// Focus: the THIN BINDING SEAM contract — that this service correctly delegates to
/// <see cref="ChatSessionManager"/> and enforces idempotency on rebind, WITHOUT
/// duplicating the three-tier persistence pipeline.
/// </para>
///
/// <para>
/// Mocking strategy (ADR-038, tests/CLAUDE.md "module-boundary mocks"): we use
/// <see cref="Mock{T}"/> for the underlying <see cref="ITenantCache"/> +
/// <see cref="IChatDataverseRepository"/> + <see cref="ISessionPersistenceService"/>
/// (the same boundaries that <see cref="ChatSessionManager"/>'s own tests mock — see
/// <c>ChatSessionManagerTests</c>) — these are genuine module boundaries (Redis,
/// Dataverse, Cosmos) per ADR-038 mock-boundary rule. We do NOT mock
/// <see cref="ChatSessionManager"/> itself — that would mock the SUT's collaborator
/// rather than its boundary (B7 anti-pattern banned per tests/CLAUDE.md). Instead we
/// instantiate the real ChatSessionManager with the boundary mocks, which exercises
/// the real persistence semantics through the binding seam.
/// </para>
///
/// <para>
/// Banned-pattern check (per ADR-038 + tests/CLAUDE.md §"Banned Antipatterns"):
/// no <c>Mock&lt;HttpMessageHandler&gt;</c> (B1) — none used; no DI-registration tests
/// (B3) — none; no ctor null-check tests (B4) — argument validation is asserted via
/// behavior (a method call), not a constructor call; no mirror tests (B6) — every
/// assertion is on observable behavior (the session returned, the cache call made);
/// no all-mocks + trivial assertion (B7) — assertions are on session state, not
/// <c>Verify.Once()</c> shapes alone; test names follow
/// <c>{Method}_{Scenario}_{ExpectedResult}</c> (B13 compliant).
/// </para>
/// </summary>
public class ComposeSessionServiceTests
{
    private const string TenantId = "tenant-compose";
    private const string EphemeralDocId = "spe-driveitem-aaa-111";
    private const string PromotedDocId = "11111111-2222-3333-4444-555555555555"; // sprk_documentid
    private static readonly Guid PlaybookId = Guid.Parse("47686eb1-9916-f111-8343-7c1e520aa4df"); // Document Summary per FR-09/FR-10

    private readonly Mock<ITenantCache> _cacheMock;
    private readonly Mock<IChatDataverseRepository> _repoMock;
    private readonly Mock<ILogger<ChatSessionManager>> _sessionManagerLoggerMock;
    private readonly Mock<ILogger<ComposeSessionService>> _composeLoggerMock;
    private readonly ChatSessionManager _realSessionManager;
    private readonly ComposeSessionService _sut;

    public ComposeSessionServiceTests()
    {
        _cacheMock = new Mock<ITenantCache>();
        _repoMock = new Mock<IChatDataverseRepository>();
        _sessionManagerLoggerMock = new Mock<ILogger<ChatSessionManager>>();
        _composeLoggerMock = new Mock<ILogger<ComposeSessionService>>();

        // Real ChatSessionManager wired to boundary mocks — see class XML doc rationale.
        _realSessionManager = new ChatSessionManager(
            cache: _cacheMock.Object,
            dataverseRepository: _repoMock.Object,
            logger: _sessionManagerLoggerMock.Object);

        _sut = new ComposeSessionService(_realSessionManager, _composeLoggerMock.Object);
    }

    // =========================================================================
    // EnsureSessionForDocumentAsync — Path B (ephemeral) creation
    // =========================================================================

    [Fact]
    public async Task EnsureSessionForDocumentAsync_WhenCalledWithEphemeralDocId_CreatesSessionBoundToDocumentId()
    {
        // Arrange — boundary mocks accept the writes that ChatSessionManager.CreateSessionAsync makes
        _repoMock
            .Setup(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cacheMock
            .Setup(c => c.SetSlidingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<ChatSession>(), It.IsAny<TimeSpan>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var session = await _sut.EnsureSessionForDocumentAsync(
            tenantId: TenantId,
            documentId: EphemeralDocId,
            playbookId: PlaybookId);

        // Assert — session is bound to the SPE drive-item id (FR-07 Path B)
        session.Should().NotBeNull();
        session.TenantId.Should().Be(TenantId);
        session.DocumentId.Should().Be(EphemeralDocId);
        session.PlaybookId.Should().Be(PlaybookId);
        session.HostContext.Should().BeNull("HostContext is NOT extended in R1 per CLAUDE.md MUST rule");
    }

    [Fact]
    public async Task EnsureSessionForDocumentAsync_WhenCalledWithEmptyTenantId_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.EnsureSessionForDocumentAsync(
            tenantId: "",
            documentId: EphemeralDocId);

        // Assert — observable behavior: caller gets a clear error
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*tenantId*");
    }

    [Fact]
    public async Task EnsureSessionForDocumentAsync_WhenCalledWithEmptyDocumentId_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.EnsureSessionForDocumentAsync(
            tenantId: TenantId,
            documentId: "");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*documentId*");
    }

    // =========================================================================
    // GetSessionAsync — pure delegation to ChatSessionManager
    // =========================================================================

    [Fact]
    public async Task GetSessionAsync_WhenSessionExistsInCache_ReturnsSessionWithoutDataverseCall()
    {
        // Arrange — cache HIT path of the underlying ChatSessionManager
        var sessionId = Guid.NewGuid().ToString("N");
        var cachedSession = new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: EphemeralDocId,
            PlaybookId: PlaybookId,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>());

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId,
                ChatSessionManager.CacheResource,
                sessionId,
                ChatSessionManager.CacheVersion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedSession);

        // Act
        var actual = await _sut.GetSessionAsync(TenantId, sessionId);

        // Assert — Dataverse is never consulted on cache HIT
        actual.Should().NotBeNull();
        actual!.SessionId.Should().Be(sessionId);
        actual.DocumentId.Should().Be(EphemeralDocId);
        _repoMock.Verify(
            r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Cache HIT path does not consult Dataverse (FR-07 reuses ChatSession three-tier semantics)");
    }

    // =========================================================================
    // RebindToDocumentIdAsync — idempotency + state transition
    // =========================================================================

    [Fact]
    public async Task RebindToDocumentIdAsync_WhenCurrentEqualsNew_ReturnsExistingSessionWithoutWriting()
    {
        // Arrange — idempotency short-circuit #1: caller submits the same id on both sides
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: PromotedDocId,
            PlaybookId: PlaybookId,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>());

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, ChatSessionManager.CacheResource, sessionId,
                ChatSessionManager.CacheVersion, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _sut.RebindToDocumentIdAsync(
            tenantId: TenantId,
            sessionId: sessionId,
            currentDocumentId: PromotedDocId,
            newDocumentId: PromotedDocId);

        // Assert — observable behavior: session returned unchanged; no write to cache
        result.Should().NotBeNull();
        result!.DocumentId.Should().Be(PromotedDocId);
        _cacheMock.Verify(
            c => c.SetSlidingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<ChatSession>(), It.IsAny<TimeSpan>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Idempotent no-op MUST NOT write to the cache");
    }

    [Fact]
    public async Task RebindToDocumentIdAsync_WhenStoredAlreadyMatchesNew_ReturnsExistingSessionWithoutWriting()
    {
        // Arrange — idempotency short-circuit #2: previous Rebind already promoted; client retries
        var sessionId = Guid.NewGuid().ToString("N");
        var alreadyPromoted = new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: PromotedDocId,
            PlaybookId: PlaybookId,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>());

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, ChatSessionManager.CacheResource, sessionId,
                ChatSessionManager.CacheVersion, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadyPromoted);

        // Act — caller still thinks the session is at the ephemeral id
        var result = await _sut.RebindToDocumentIdAsync(
            tenantId: TenantId,
            sessionId: sessionId,
            currentDocumentId: EphemeralDocId,
            newDocumentId: PromotedDocId);

        // Assert — already-at-target wins; no cache write
        result.Should().NotBeNull();
        result!.DocumentId.Should().Be(PromotedDocId);
        _cacheMock.Verify(
            c => c.SetSlidingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<ChatSession>(), It.IsAny<TimeSpan>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RebindToDocumentIdAsync_WhenEphemeralToPromoted_UpdatesDocumentIdAndPersistsThroughCache()
    {
        // Arrange — happy path FR-06/FR-07: post-Save promotion of an ephemeral session
        var sessionId = Guid.NewGuid().ToString("N");
        var beforeRebind = new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: EphemeralDocId,        // SPE drive-item id (Path B)
            PlaybookId: PlaybookId,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            LastActivity: DateTimeOffset.UtcNow.AddMinutes(-5),
            Messages: Array.Empty<ChatMessage>());

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, ChatSessionManager.CacheResource, sessionId,
                ChatSessionManager.CacheVersion, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(beforeRebind);

        ChatSession? captured = null;
        _cacheMock
            .Setup(c => c.SetSlidingAsync(
                TenantId, ChatSessionManager.CacheResource, sessionId,
                ChatSessionManager.CacheVersion,
                It.IsAny<ChatSession>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, ChatSession, TimeSpan, string, CancellationToken>(
                (_, _, _, _, payload, _, _, _) => captured = payload)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RebindToDocumentIdAsync(
            tenantId: TenantId,
            sessionId: sessionId,
            currentDocumentId: EphemeralDocId,
            newDocumentId: PromotedDocId);

        // Assert — DocumentId flipped to the promoted sprk_documentid, persisted through the
        // existing ChatSessionManager.UpdateSessionCacheAsync path (FR-07 reuse).
        result.Should().NotBeNull();
        result!.DocumentId.Should().Be(PromotedDocId);
        result.SessionId.Should().Be(sessionId);
        result.PlaybookId.Should().Be(PlaybookId, "playbook binding survives the rebind");

        captured.Should().NotBeNull("rebind MUST persist through the existing three-tier write path");
        captured!.DocumentId.Should().Be(PromotedDocId);
        captured.LastActivity.Should().BeAfter(beforeRebind.LastActivity, "LastActivity advances on rebind");
    }

    [Fact]
    public async Task RebindToDocumentIdAsync_WhenSessionDoesNotExist_ReturnsNull()
    {
        // Arrange — session not in cache and not in Dataverse
        var sessionId = Guid.NewGuid().ToString("N");
        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, ChatSessionManager.CacheResource, sessionId,
                ChatSessionManager.CacheVersion, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);
        _repoMock
            .Setup(r => r.GetSessionAsync(TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var result = await _sut.RebindToDocumentIdAsync(
            tenantId: TenantId,
            sessionId: sessionId,
            currentDocumentId: EphemeralDocId,
            newDocumentId: PromotedDocId);

        // Assert — FR-06 contract: hard error signalled via null (endpoint translates to 404)
        result.Should().BeNull();
    }

    // =========================================================================
    // Tenant scope inherited from ChatSessionManager (POML step 3c — ADR-015 Tier 3)
    // =========================================================================
    //
    // ADR-038 KEEP category: domain-logic (multi-tenant scoping invariant for the binding seam).
    // This test pins the contract that the tenantId argument flows verbatim into the underlying
    // ChatSessionManager cache key. Without this assertion, a regression that hard-coded a
    // tenant or dropped the argument would silently cross-tenant-leak — a security-relevant
    // failure mode (ADR-015 Tier 3 binding). The boundary captured is the ITenantCache mock —
    // a true Spaarke module boundary (Redis facade) per ADR-038 §4 acceptable mocking row 1.

    [Fact]
    public async Task GetSessionAsync_WhenCalledWithCustomTenant_ForwardsTenantIdToCacheKey()
    {
        // Arrange — distinct tenantId (NOT the class-level TenantId const) — guarantees the
        // assertion below cannot pass by accident if the SUT hard-coded the default.
        const string customTenant = "tenant-isolation-probe-99";
        var sessionId = Guid.NewGuid().ToString("N");
        string? capturedTenantOnCacheGet = null;

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(),
                ChatSessionManager.CacheResource,
                sessionId,
                ChatSessionManager.CacheVersion,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, string, CancellationToken>(
                (tenant, _, _, _, _, _) => capturedTenantOnCacheGet = tenant)
            .ReturnsAsync((ChatSession?)null);

        _repoMock
            .Setup(r => r.GetSessionAsync(It.IsAny<string>(), sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);

        // Act
        await _sut.GetSessionAsync(customTenant, sessionId);

        // Assert — ADR-015 Tier 3 invariant: tenantId reached the cache key UNCHANGED.
        // A regression that dropped or rewrote this would cause cross-tenant cache reads.
        capturedTenantOnCacheGet.Should().Be(
            customTenant,
            "ADR-015 Tier 3: ComposeSessionService MUST forward tenantId verbatim into the underlying " +
            "ChatSessionManager cache key — never hard-code, never swap in a default, never bypass.");
    }

    [Fact]
    public async Task RebindToDocumentIdAsync_WhenCallerCurrentMismatchesStored_ProceedsWithNewValueWinning()
    {
        // Arrange — out-of-order Save race: caller asserts X, store says Y, target is Z
        var sessionId = Guid.NewGuid().ToString("N");
        const string staleStoredDocId = "spe-driveitem-old-xxx";
        var stored = new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: staleStoredDocId,
            PlaybookId: PlaybookId,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>());

        _cacheMock
            .Setup(c => c.GetAsync<ChatSession>(
                TenantId, ChatSessionManager.CacheResource, sessionId,
                ChatSessionManager.CacheVersion, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        ChatSession? captured = null;
        _cacheMock
            .Setup(c => c.SetSlidingAsync(
                TenantId, ChatSessionManager.CacheResource, sessionId,
                ChatSessionManager.CacheVersion,
                It.IsAny<ChatSession>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, ChatSession, TimeSpan, string, CancellationToken>(
                (_, _, _, _, payload, _, _, _) => captured = payload)
            .Returns(Task.CompletedTask);

        // Act — caller insists current is EphemeralDocId; reality says staleStoredDocId
        var result = await _sut.RebindToDocumentIdAsync(
            tenantId: TenantId,
            sessionId: sessionId,
            currentDocumentId: EphemeralDocId,
            newDocumentId: PromotedDocId);

        // Assert — new-value-wins: rebind proceeds to PromotedDocId regardless
        result.Should().NotBeNull();
        result!.DocumentId.Should().Be(PromotedDocId);
        captured.Should().NotBeNull();
        captured!.DocumentId.Should().Be(PromotedDocId);
    }
}
