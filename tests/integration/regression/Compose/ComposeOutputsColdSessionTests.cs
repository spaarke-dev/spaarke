using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Compose;

/// <summary>
/// Regression — GET /api/ai/chat/sessions/{sessionId}/compose-outputs must NOT return HTTP 500
/// for a client-minted document session that was never server-persisted.
///
/// Root cause (owner UAT, spaarke-bff-dev; App Insights stack captured):
///   The two-session model mints the Compose <b>document session</b> client-side
///   (crypto.randomUUID) — it is NOT created via POST /api/ai/chat/sessions, so it is absent from
///   both the Redis hot cache and the Cosmos warm store. When ComposeWorkspace fetched
///   compose-outputs for that id, ChatSessionManager.GetSessionAsync fell through to the COLD
///   Dataverse path (ChatDataverseRepository.GetSessionAsync → GetMessagesAsync). That method
///   called IFieldMappingDataverseService.QueryChildRecordIdsAsync purely to <i>discard</i> its
///   result (it always returned an empty list). Because IFieldMappingDataverseService resolves to
///   DataverseWebApiService (GraphModule DI), the discarded call reached the unimplemented metadata
///   helper GetEntitySetNameAsync and threw NotImplementedException — surfacing as a 500 and the
///   client banner "Could not insert AI draft ... (NotImplementedException)".
///
///   Deployed stack:
///     DataverseWebApiService.GetEntitySetNameAsync         (DataverseWebApiService.cs:1099) — throw
///     DataverseWebApiService.QueryChildRecordIdsAsync      (DataverseWebApiService.cs:1851)
///     ChatDataverseRepository.GetMessagesAsync             (ChatDataverseRepository.cs:161)
///     ChatDataverseRepository.GetSessionAsync              (ChatDataverseRepository.cs:81)
///     ChatSessionManager.GetSessionAsync                   (ChatSessionManager.cs:204)  ← cold path
///     ChatEndpoints.GetComposeOutputsAsync                 (ChatEndpoints.cs:1268)
///
/// Fix: the dead QueryChildRecordIdsAsync call (and the now-unused IFieldMappingDataverseService
/// dependency) is removed from ChatDataverseRepository. A session that is absent everywhere resolves
/// to null → the endpoint returns a clean 404, which the client already treats as "no compose
/// outputs yet — nothing to materialize" (ComposeWorkspace.tsx:1192). Never a 500.
///
/// Why this test would have caught it: the pre-existing ChatSessionManager coverage mocked
/// IChatDataverseRepository, so the real cold-path stub was never executed. These tests wire the
/// REAL ChatDataverseRepository so the cold path is exercised end-to-end.
///
/// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md): regression — "every bug = one regression
/// file". Compiled into Sprk.Bff.Api.Tests via the `..\..\integration\regression\**\*.cs` glob.
/// </summary>
public class ComposeOutputsColdSessionTests
{
    private const string TenantId = "tenant-compose-uat";

    private static Mock<ITenantCache> CacheThatAlwaysMisses()
    {
        var cache = new Mock<ITenantCache>();
        cache
            .Setup(c => c.GetAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);
        return cache;
    }

    private static ChatDataverseRepository RealColdRepository()
        => new ChatDataverseRepository(
            new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            NullLogger<ChatDataverseRepository>.Instance);

    [Fact]
    public async Task GetSessionAsync_WhenDocumentSessionMissesCacheAndColdPath_ReturnsNullWithoutThrowing()
    {
        // Arrange — a client-minted document session that exists in NO server store (no Redis, no
        // Cosmos). Manager falls through to the REAL cold Dataverse repository (the buggy code).
        var clientMintedDocumentSessionId = Guid.NewGuid().ToString();
        var manager = new ChatSessionManager(
            CacheThatAlwaysMisses().Object,
            RealColdRepository(),
            NullLogger<ChatSessionManager>.Instance);

        // Act
        var act = async () => await manager.GetSessionAsync(TenantId, clientMintedDocumentSessionId);

        // Assert — a not-found session resolves to null (→ endpoint 404), never a
        // NotImplementedException surfacing as a 500.
        var session = await act.Should().NotThrowAsync();
        session.Which.Should().BeNull();
    }

    [Fact]
    public async Task ColdRepository_GetSessionAsync_ForUnknownSession_ReturnsNullWithoutTouchingDataverseStub()
    {
        // Arrange — the strict IGenericEntityService mock has NO setups, so any Dataverse call the
        // cold read path makes would fail the test. This locks the contract: the cold session read
        // must be self-contained and must not invoke an unimplemented Dataverse stub.
        var repository = RealColdRepository();

        // Act
        var act = async () => await repository.GetSessionAsync(TenantId, Guid.NewGuid().ToString());

        // Assert
        var session = await act.Should().NotThrowAsync();
        session.Which.Should().BeNull();
    }
}
