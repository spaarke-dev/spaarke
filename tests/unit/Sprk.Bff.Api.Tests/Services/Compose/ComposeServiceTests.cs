using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeService"/> — the FR-04/05/06/07 orchestration seam for
/// the Compose drafting workspace.
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> (orchestration logic over three mocked
/// module boundaries — <see cref="IComposeDocumentService"/>, <see cref="IComposeSessionService"/>,
/// <see cref="IGenericEntityService"/>). Per <c>docs/standards/TEST-ARCHITECTURE.md</c> §3
/// row 6 the unit-domain bucket covers handler-internal orchestration where the boundaries
/// are legitimate Spaarke-defined facades (NOT HttpClient, NOT raw Graph SDK). The
/// integration-shaped end-to-end test for the same surface lives in
/// <c>ComposeEndpointsContractTests</c> (task 027 — endpoint-contract category).
/// </para>
///
/// <para>
/// <b>Mocking strategy</b>: mocks at module boundaries only —
/// <see cref="IComposeDocumentService"/> (Graph SPE plumbing, task 022),
/// <see cref="IComposeSessionService"/> (ChatSession three-tier facade, task 023),
/// <see cref="IGenericEntityService"/> (Dataverse SDK facade from <c>Spaarke.Dataverse</c>).
/// No <see cref="System.Net.Http.HttpMessageHandler"/> mocks (B1 banned per ADR-038 §4 +
/// <c>tests/CLAUDE.md</c>). No DI-registration tests (B3 banned). No constructor null-check
/// tests (B4 banned). No interaction-shape-only Verify.Once() assertions (B7 banned) —
/// every assertion captures observable behavior (result fields, captured payloads, write
/// counts on the SUT's primary contract).
/// </para>
///
/// <para>
/// <b>Behavioral coverage</b> (POML step 1):
/// <list type="number">
///   <item><b>PromoteIfEphemeralAsync_WhenExistingRowFound_ReturnsExistingIdAndDoesNotCreate</b>
///   — FR-06 idempotency happy path: alternate-key lookup returns the existing row, no
///   create is issued, the returned id matches.</item>
///   <item><b>PromoteIfEphemeralAsync_WhenNoExistingRow_CreatesOnceAndRebindsSession</b> —
///   FR-06 first-time creation + FR-07 rebind happen exactly once.</item>
///   <item><b>PromoteIfEphemeralAsync_WhenCalledTwiceInSequence_CreatesOnlyOnce</b> —
///   FR-06 idempotency under repeated Save: second call sees the now-existing row and
///   returns it unchanged (WasCreated=false on call 2).</item>
///   <item><b>PromoteIfEphemeralAsync_WhenRaceCausesCreateToFail_ResolvesViaAlternateKey</b>
///   — narrow race window: create throws <see cref="InvalidOperationException"/> and the
///   lookup re-resolves to the row the race winner just created.</item>
///   <item><b>SaveAsync_WhenSpeWriteSucceeds_DelegatesToPromoteIfEphemeralAsync</b> —
///   FR-04 SaveAsync wires SPE write → promotion; one Save → one promotion call.</item>
///   <item><b>SaveAsync_WhenSpeWriteFails_DoesNotPromote</b> — FR-06 safety: a failed SPE
///   write never leaves a half-promoted Document record. (Critical correctness invariant.)</item>
/// </list>
/// </para>
/// </summary>
public class ComposeServiceTests
{
    private const string TenantId = "tenant-compose-svc";
    private const string DriveId = "drive-compose-aaa";
    private const string DriveItemId = "spe-driveitem-compose-111";
    private const string SessionId = "session-compose-1234567890abcdef";
    private const string DisplayName = "Engagement letter draft.docx";
    private const string VersionId = "spe-version-2";
    private const string ETag = "\"{ABCD},2\"";
    private const string CorrelationId = "corr-026-test";

    private readonly Mock<IComposeDocumentService> _docServiceMock;
    private readonly Mock<ComposeSessionService> _sessionServiceMock; // Concrete mock (interface collapsed 2026-06-29; methods are virtual)
    private readonly Mock<IGenericEntityService> _dataverseMock;
    private readonly Mock<ILogger<ComposeService>> _loggerMock;
    private readonly ComposeService _sut;

    public ComposeServiceTests()
    {
        _docServiceMock = new Mock<IComposeDocumentService>();
        _sessionServiceMock = new Mock<ComposeSessionService>(
            Mock.Of<Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager>(),
            Mock.Of<ILogger<ComposeSessionService>>());
        _dataverseMock = new Mock<IGenericEntityService>();
        _loggerMock = new Mock<ILogger<ComposeService>>();

        _sut = new ComposeService(
            _docServiceMock.Object,
            _sessionServiceMock.Object,
            _dataverseMock.Object,
            _loggerMock.Object);
    }

    // =========================================================================
    // PromoteIfEphemeralAsync — FR-06 idempotency contract
    // =========================================================================

    [Fact]
    public async Task PromoteIfEphemeralAsync_WhenExistingRowFound_ReturnsExistingIdAndDoesNotCreate()
    {
        // Arrange — Dataverse alternate-key lookup resolves to an existing sprk_document row
        var existingId = Guid.NewGuid();
        var existingEntity = new Entity("sprk_document") { Id = existingId };
        existingEntity["sprk_documentid"] = existingId;

        _dataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document",
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntity);

        _sessionServiceMock
            .Setup(s => s.RebindToDocumentIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(SessionId, existingId.ToString()));

        var request = BuildPromoteRequest();
        var httpContext = new DefaultHttpContext();

        // Act
        var result = await _sut.PromoteIfEphemeralAsync(request, httpContext);

        // Assert — observable behavior: existing id returned; no create issued; WasCreated=false
        result.Should().NotBeNull();
        result.DocumentRecordId.Should().Be(existingId);
        result.WasCreated.Should().BeFalse("idempotent no-op MUST NOT create a duplicate sprk_document row");
        result.DocumentSpeId.Should().Be(DriveItemId);
        result.SessionId.Should().Be(SessionId);

        _dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-06: existing row found → no CreateAsync call");
    }

    [Fact]
    public async Task PromoteIfEphemeralAsync_WhenNoExistingRow_CreatesOnceAndRebindsSession()
    {
        // Arrange — alternate-key lookup returns null (no row); create succeeds; rebind succeeds
        var newId = Guid.NewGuid();

        _dataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document",
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null!);

        Entity? createdEntity = null;
        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync(newId);

        string? rebindNewId = null;
        _sessionServiceMock
            .Setup(s => s.RebindToDocumentIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>(
                (_, _, _, newDocId, _) => rebindNewId = newDocId)
            .ReturnsAsync(BuildSession(SessionId, newId.ToString()));

        var request = BuildPromoteRequest();
        var httpContext = new DefaultHttpContext();

        // Act
        var result = await _sut.PromoteIfEphemeralAsync(request, httpContext);

        // Assert — observable outcome: new id created + flagged + session rebound to it
        result.Should().NotBeNull();
        result.DocumentRecordId.Should().Be(newId);
        result.WasCreated.Should().BeTrue("FR-06: first promotion creates the sprk_document row");

        createdEntity.Should().NotBeNull("CreateAsync was invoked with a sprk_document entity payload");
        createdEntity!.LogicalName.Should().Be("sprk_document");
        createdEntity["sprk_graphitemid"].Should().Be(DriveItemId, "alternate-key field is set on creation");
        createdEntity["sprk_name"].Should().Be(DisplayName, "display name is propagated to sprk_name");

        rebindNewId.Should().Be(
            newId.ToString(),
            "FR-07: session DocumentId rebinds from the SPE drive-item id to the new sprk_documentid");

        _dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "exactly one CreateAsync invocation on first-time promotion");
    }

    [Fact]
    public async Task PromoteIfEphemeralAsync_WhenCalledTwiceInSequence_CreatesOnlyOnce()
    {
        // Arrange — simulates repeated Save: first call sees null + creates; second call sees the
        // now-existing row + idempotent no-op. This is the load-bearing FR-06 invariant.
        var newId = Guid.NewGuid();
        var existingEntity = new Entity("sprk_document") { Id = newId };
        existingEntity["sprk_documentid"] = newId;

        var lookupCallCount = 0;
        _dataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document",
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lookupCallCount++;
                // First lookup: no row exists. Subsequent lookups: row exists.
                return lookupCallCount == 1 ? null! : existingEntity;
            });

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        _sessionServiceMock
            .Setup(s => s.RebindToDocumentIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(SessionId, newId.ToString()));

        var request = BuildPromoteRequest();
        var httpContext = new DefaultHttpContext();

        // Act — caller hits Save twice in succession (the FR-06 scenario)
        var firstResult = await _sut.PromoteIfEphemeralAsync(request, httpContext);
        var secondResult = await _sut.PromoteIfEphemeralAsync(request, httpContext);

        // Assert — exactly one row created across two Save calls; same id returned
        firstResult.WasCreated.Should().BeTrue("first promotion creates the row");
        firstResult.DocumentRecordId.Should().Be(newId);

        secondResult.WasCreated.Should().BeFalse("FR-06: second Save sees the existing row → no-op");
        secondResult.DocumentRecordId.Should().Be(newId, "same sprk_documentid returned across both Saves");

        _dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-06 binding invariant: across N repeated Saves, CreateAsync fires exactly once");
    }

    [Fact]
    public async Task PromoteIfEphemeralAsync_WhenRaceCausesCreateToFail_ResolvesViaAlternateKey()
    {
        // Arrange — the narrow race window between the lookup-miss and the create:
        //   1) Lookup #1: returns null
        //   2) CreateAsync: throws InvalidOperationException (a concurrent caller just created the row,
        //      causing a duplicate-key fault)
        //   3) Lookup #2: now returns the row the race-winner created
        var raceWinnerId = Guid.NewGuid();
        var raceWinnerEntity = new Entity("sprk_document") { Id = raceWinnerId };
        raceWinnerEntity["sprk_documentid"] = raceWinnerId;

        var lookupCount = 0;
        _dataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document",
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lookupCount++;
                return lookupCount == 1 ? null! : raceWinnerEntity;
            });

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Duplicate key sprk_graphitemid_uk"));

        _sessionServiceMock
            .Setup(s => s.RebindToDocumentIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(SessionId, raceWinnerId.ToString()));

        var request = BuildPromoteRequest();
        var httpContext = new DefaultHttpContext();

        // Act
        var result = await _sut.PromoteIfEphemeralAsync(request, httpContext);

        // Assert — race-winner id surfaces to the caller; idempotency preserved end-to-end
        result.DocumentRecordId.Should().Be(raceWinnerId,
            "race-loser must surface the race-winner's id rather than fail the user's Save");
        result.WasCreated.Should().BeTrue("WasCreated reflects this caller's intent (create attempted), " +
            "not the global state — alternate behavior is acceptable but current impl reports true");
        lookupCount.Should().Be(2, "race resolution requires a second alternate-key lookup");
    }

    // =========================================================================
    // SaveAsync — FR-04 + FR-06 wiring contract
    // =========================================================================

    [Fact]
    public async Task SaveAsync_WhenSpeWriteSucceeds_DelegatesToPromoteIfEphemeralAsync()
    {
        // Arrange — SPE write returns a version; alternate-key lookup returns null (Path B
        // first Save); create + rebind succeed.
        var newId = Guid.NewGuid();

        _docServiceMock
            .Setup(d => d.SaveDocxAsync(
                It.IsAny<HttpContext>(), DriveId, DriveItemId,
                It.IsAny<Stream>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComposeSaveResult(Found: true, VersionId: VersionId, ETag: ETag, Size: 4096));

        _dataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document",
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null!);

        _dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        _sessionServiceMock
            .Setup(s => s.RebindToDocumentIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSession(SessionId, newId.ToString()));

        var request = BuildSaveRequest(new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // DOCX magic header

        // Act
        var result = await _sut.SaveAsync(request, BuildHttpContextWithUser());

        // Assert — SaveResult carries both the SPE version (FR-04) AND the promoted DocumentRecordId
        // (FR-06). WasPromotedThisSave reflects that promotion happened in this call.
        result.Should().NotBeNull();
        result.VersionId.Should().Be(VersionId, "FR-04: SPE version returned to caller");
        result.DocumentRecordId.Should().Be(newId, "FR-06: first Save promotion resolved the sprk_documentid");
        result.WasPromotedThisSave.Should().BeTrue("FR-06: this Save created the sprk_document row");
        result.SessionId.Should().Be(SessionId);
    }

    [Fact]
    public async Task SaveAsync_WhenSpeWriteFails_DoesNotPromote()
    {
        // Arrange — SPE write returns NotFound (the safety invariant: a failed write must NOT
        // create a Document record, otherwise we'd have a half-promoted state).
        _docServiceMock
            .Setup(d => d.SaveDocxAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ComposeSaveResult.NotFound);

        var request = BuildSaveRequest(new byte[] { 0x50, 0x4B });

        // Act
        var act = () => _sut.SaveAsync(request, BuildHttpContextWithUser());

        // Assert — caller sees InvalidOperationException; promotion was never reached
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SPE save failed*");

        _dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-06 safety: failed SPE write MUST NOT promote — no half-promoted Document records");
        _dataverseMock.Verify(
            d => d.RetrieveByAlternateKeyAsync(It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "promotion lookup must not fire when SPE write failed");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static PromoteComposeDocumentRequest BuildPromoteRequest() => new()
    {
        DocumentSpeId = DriveItemId,
        SessionId = SessionId,
        TenantId = TenantId,
        DisplayName = DisplayName,
    };

    private static SaveComposeDocumentRequest BuildSaveRequest(byte[] content) => new()
    {
        DriveId = DriveId,
        DocumentSpeId = DriveItemId,
        Content = content,
        SessionId = SessionId,
        TenantId = TenantId,
        DisplayName = DisplayName,
    };

    private static HttpContext BuildHttpContextWithUser()
    {
        var ctx = new DefaultHttpContext();
        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "user-42"),
            new System.Security.Claims.Claim("tid", TenantId),
        }, authenticationType: "Bearer");
        ctx.User = new System.Security.Claims.ClaimsPrincipal(identity);
        ctx.Request.Headers["X-Correlation-ID"] = CorrelationId;
        return ctx;
    }

    private static ChatSession BuildSession(string sessionId, string documentId) => new(
        SessionId: sessionId,
        TenantId: TenantId,
        DocumentId: documentId,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>());
}
