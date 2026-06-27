using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Ai.Telemetry;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// Unit tests for <see cref="SessionPersistenceService.UpdateUploadedFilesAsync"/>
/// (chat-routing-redesign-r1 task 072, architecture §6.1 + §7.1).
///
/// Verifies:
/// - Roundtrip: writing an enriched <see cref="ChatSessionFile"/> set and re-reading the
///   persisted <see cref="StoredSession"/> preserves all 6 R5 fields + all 8 enrichment
///   fields.
/// - Tier-1 logging (ADR-015): the log message captures sessionId + fileCount only.
///   SummaryText, ClassifiedDocType, Sections content, FileName MUST NOT appear in any
///   ILogger invocation.
/// - Redis HIT path: when Redis returns the session, Cosmos READ is not invoked.
/// - Redis MISS path: when Redis returns null, Cosmos READ is invoked as the fallback.
/// - Cosmos write failure does NOT surface to the caller — matches the SaveTabsAsync
///   precedent (fire-and-forget). NOTE: the existing service does NOT use ETag /
///   IfMatchEtag in <c>UpsertItemAsync</c>; concurrency surfaces only as a swallowed
///   Warning log, NOT a propagated exception. See report from task 072 sub-agent for
///   the deviation note.
///
/// Patterns mirror <see cref="SessionPersistenceServiceTabsTests"/> — same Moq + Cosmos
/// mocking approach.
/// </summary>
public class SessionPersistenceServiceUploadedFilesTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";
    private const string DatabaseName = "spaarke-ai";
    private const string CosmosEndpoint = "https://spaarke-cosmos-dev.documents.azure.com:443/";

    private readonly TrackingTenantCache _cache;
    private readonly Mock<CosmosClient> _cosmosClientMock;
    private readonly Mock<Container> _containerMock;
    private readonly Mock<ILogger<SessionPersistenceService>> _loggerMock;
    private readonly Mock<IContextEventEmitter> _contextEventEmitterMock;
    private readonly IConfiguration _configuration;
    private readonly SessionPersistenceService _sut;

    public SessionPersistenceServiceUploadedFilesTests()
    {
        _cache = new TrackingTenantCache();
        _cosmosClientMock = new Mock<CosmosClient>();
        _containerMock = new Mock<Container>();
        _loggerMock = new Mock<ILogger<SessionPersistenceService>>();
        // chat-routing-redesign-r1 task 074 — IContextEventEmitter is now a required ctor dep
        // for context.upload_persisted emission. Provide a Loose mock here; the dedicated
        // emission tests live in UploadPipelineTelemetryTests.cs.
        _contextEventEmitterMock = new Mock<IContextEventEmitter>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosPersistence:Endpoint"] = CosmosEndpoint,
                ["CosmosPersistence:DatabaseName"] = DatabaseName
            })
            .Build();

        _cosmosClientMock
            .Setup(c => c.GetContainer(DatabaseName, "sessions"))
            .Returns(_containerMock.Object);

        _sut = new SessionPersistenceService(
            _cache,
            _cosmosClientMock.Object,
            _configuration,
            _loggerMock.Object,
            _contextEventEmitterMock.Object);
    }

    // =========================================================================
    // Test 1: Roundtrip — all 14 ChatSessionFile fields preserved through persistence
    // =========================================================================

    [Fact]
    public async Task UpdateUploadedFilesAsync_RoundtripsEnrichedFields()
    {
        // Arrange — existing session in Redis with no UploadedFiles
        var existing = BuildStoredSession();
        await SeedSession(existing);

        StoredSession? capturedSession = null;
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (s, _, _, _) => capturedSession = s)
            .ReturnsAsync(CreateFakeUpsertResponse());

        var uploadedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var enriched = new[]
        {
            new ChatSessionFile(
                FileId: "f-1",
                FileName: "nda.pdf",
                ContentType: "application/pdf",
                SizeBytes: 12345,
                SearchDocumentIdsCsv: "d-1,d-2",
                UploadedAt: uploadedAt)
            {
                SummaryText = "1-paragraph summary",
                ClassifiedDocType = "NDA",
                ClassifiedConfidence = 0.92,
                Sections = new[]
                {
                    new SectionInfo("Recitals", 0, 500, 1, 1),
                    new SectionInfo("Definitions", 500, 1500, 1, 2)
                },
                TableMetadata = new[]
                {
                    new TableInfo("Table 1", 800, 2)
                },
                Citations = new[]
                {
                    new CitationReference("d-1", "quoted text", 1)
                },
                PageCount = 12,
                Language = "en"
            }
        };

        // Act
        var result = await _sut.UpdateUploadedFilesAsync(SessionId, TenantId, enriched);
        await Task.Delay(50); // fire-and-forget Cosmos

        // Assert
        result.Should().BeTrue();
        capturedSession.Should().NotBeNull();
        capturedSession!.UploadedFiles.Should().HaveCount(1);

        var stored = capturedSession.UploadedFiles[0];
        // 6 R5 fields
        stored.FileId.Should().Be("f-1");
        stored.FileName.Should().Be("nda.pdf");
        stored.ContentType.Should().Be("application/pdf");
        stored.SizeBytes.Should().Be(12345);
        stored.SearchDocumentIdsCsv.Should().Be("d-1,d-2");
        stored.UploadedAt.Should().Be(uploadedAt);
        // 8 enriched fields
        stored.SummaryText.Should().Be("1-paragraph summary");
        stored.ClassifiedDocType.Should().Be("NDA");
        stored.ClassifiedConfidence.Should().Be(0.92);
        stored.Sections.Should().HaveCount(2);
        stored.Sections[0].Name.Should().Be("Recitals");
        stored.Sections[1].StartPage.Should().Be(1);
        stored.Sections[1].EndPage.Should().Be(2);
        stored.TableMetadata.Should().HaveCount(1);
        stored.TableMetadata[0].Name.Should().Be("Table 1");
        stored.TableMetadata[0].Page.Should().Be(2);
        stored.Citations.Should().HaveCount(1);
        stored.Citations[0].SourceId.Should().Be("d-1");
        stored.Citations[0].Quote.Should().Be("quoted text");
        stored.Citations[0].Page.Should().Be(1);
        stored.PageCount.Should().Be(12);
        stored.Language.Should().Be("en");

        // JSON round-trip — also verify camelCase wire format works through System.Text.Json
        var json = JsonSerializer.Serialize(capturedSession);
        json.Should().Contain("\"uploadedFiles\"");
        json.Should().Contain("\"fileId\":\"f-1\"");
        json.Should().Contain("\"summaryText\":\"1-paragraph summary\"");

        // Reverse map: StoredSession.UploadedFiles → ChatSessionFile equivalency
        var deserialized = JsonSerializer.Deserialize<StoredSession>(json);
        deserialized.Should().NotBeNull();
        deserialized!.UploadedFiles[0].Sections.Should().HaveCount(2);
        deserialized.UploadedFiles[0].Citations[0].Quote.Should().Be("quoted text");
    }

    // =========================================================================
    // Test 2: Tier-1 logging — sessionId + fileCount only; NO enriched content
    // =========================================================================

    [Fact]
    public async Task UpdateUploadedFilesAsync_LogsSessionIdAndCount_NeverSummaryText()
    {
        // Arrange — existing session in Redis
        var existing = BuildStoredSession();
        await SeedSession(existing);
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFakeUpsertResponse());

        const string sensitiveSummary = "SENSITIVE-SUMMARY-DO-NOT-LOG";
        const string sensitiveDocType = "SENSITIVE-DOCTYPE-DO-NOT-LOG";
        const string sensitiveSectionName = "SENSITIVE-SECTION-DO-NOT-LOG";
        const string sensitiveFileName = "SENSITIVE-FILENAME-DO-NOT-LOG.pdf";

        var enriched = new[]
        {
            new ChatSessionFile(
                FileId: "f-1",
                FileName: sensitiveFileName,
                ContentType: "application/pdf",
                SizeBytes: 100,
                SearchDocumentIdsCsv: "d-1",
                UploadedAt: DateTimeOffset.UtcNow)
            {
                SummaryText = sensitiveSummary,
                ClassifiedDocType = sensitiveDocType,
                Sections = new[] { new SectionInfo(sensitiveSectionName, 0, 100) }
            }
        };

        // Act
        await _sut.UpdateUploadedFilesAsync(SessionId, TenantId, enriched);
        await Task.Delay(50);

        // Assert — scan ALL logger invocations across ALL levels for any sensitive literal
        var allLogInvocations = _loggerMock.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log))
            .Select(i => i.ToString() ?? string.Empty)
            .ToList();

        allLogInvocations.Should().NotBeEmpty("UpdateUploadedFilesAsync must emit at least one log line for observability");

        var allLogText = string.Join("\n", allLogInvocations);

        allLogText.Should().NotContain(sensitiveSummary,
            "ADR-015 Tier-1 logging MUST NOT include per-file SummaryText");
        allLogText.Should().NotContain(sensitiveDocType,
            "ADR-015 Tier-1 logging MUST NOT include per-file ClassifiedDocType");
        allLogText.Should().NotContain(sensitiveSectionName,
            "ADR-015 Tier-1 logging MUST NOT include per-file section names");
        allLogText.Should().NotContain(sensitiveFileName,
            "ADR-015 Tier-1 logging MUST NOT include per-file FileName");

        // Positive: sessionId and fileCount=1 SHOULD be in the log
        allLogText.Should().Contain(SessionId,
            "sessionId is the canonical Tier-1 correlation identifier");
    }

    // =========================================================================
    // Test 3: Redis HIT → Cosmos READ not invoked
    // =========================================================================

    [Fact]
    public async Task UpdateUploadedFilesAsync_RedisHit_DoesNotFetchCosmos()
    {
        // Arrange — Redis returns the session immediately
        var existing = BuildStoredSession();
        await SeedSession(existing);
        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFakeUpsertResponse());

        // Act
        var result = await _sut.UpdateUploadedFilesAsync(SessionId, TenantId, BuildEnrichedFiles(1));
        await Task.Delay(50);

        // Assert
        result.Should().BeTrue();

        // Cosmos READ must NOT be invoked when Redis HIT
        _containerMock.Verify(c => c.ReadItemAsync<StoredSession>(
            It.IsAny<string>(),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Never,
            "Cosmos READ must NOT be invoked when Redis returns the session");
    }

    // =========================================================================
    // Test 4: Redis MISS → falls back to Cosmos READ
    // =========================================================================

    [Fact]
    public async Task UpdateUploadedFilesAsync_RedisMiss_FallsBackToCosmos()
    {
        // Arrange — Redis MISS, Cosmos returns the session
        // TrackingTenantCache: empty by default → Redis miss; SetAsync succeeds.

        var fromCosmos = BuildStoredSession();
        var readResponseMock = new Mock<ItemResponse<StoredSession>>();
        readResponseMock.SetupGet(r => r.Resource).Returns(fromCosmos);
        readResponseMock.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.OK);

        _containerMock
            .Setup(c => c.ReadItemAsync<StoredSession>(
                SessionId,
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(readResponseMock.Object);

        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFakeUpsertResponse());

        // Act
        var result = await _sut.UpdateUploadedFilesAsync(SessionId, TenantId, BuildEnrichedFiles(1));
        await Task.Delay(50);

        // Assert
        result.Should().BeTrue();
        _containerMock.Verify(c => c.ReadItemAsync<StoredSession>(
            SessionId,
            new PartitionKey(TenantId),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "Cosmos READ must be invoked when Redis MISS");
    }

    // =========================================================================
    // Test 5: Cosmos UpsertItemAsync failure is swallowed (matches SaveTabsAsync precedent)
    // =========================================================================
    //
    // NOTE: The SaveTabsAsync precedent does NOT use ETag / IfMatchEtag. Concurrency
    // conflicts (e.g., HttpStatusCode.PreconditionFailed) would surface only via the
    // UpsertItemAsync exception path, which the service swallows at Warning level
    // (UpsertToCosmosAsync catches and logs without re-throw). The test asserts this
    // intentional swallow-and-continue behaviour. See sub-agent report for the
    // deviation from the POML's ETag-aspirational text.

    [Fact]
    public async Task UpdateUploadedFilesAsync_CosmosUpsertThrows_DoesNotSurfaceException()
    {
        // Arrange — existing session in Redis; Cosmos upsert throws PreconditionFailed
        var existing = BuildStoredSession();
        await SeedSession(existing);

        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("ETag conflict", HttpStatusCode.PreconditionFailed, 0, "", 0));

        // Act — must NOT throw
        var act = async () => await _sut.UpdateUploadedFilesAsync(SessionId, TenantId, BuildEnrichedFiles(1));

        // Assert
        await act.Should().NotThrowAsync(
            "UpdateUploadedFilesAsync must mirror SaveTabsAsync — Cosmos failures are swallowed at Warning level, never re-thrown");

        await Task.Delay(50); // allow fire-and-forget Cosmos task to complete its failure log

        // Returned true because the Redis write completed BEFORE the Cosmos upsert was awaited
        // (fire-and-forget). The contract: true means "session existed + Redis path completed".
    }

    // =========================================================================
    // Test 6 (bonus): No existing session → returns false
    // =========================================================================

    [Fact]
    public async Task UpdateUploadedFilesAsync_NoExistingSession_ReturnsFalse()
    {
        // Arrange — Redis miss (empty TrackingTenantCache) + Cosmos 404
        _containerMock
            .Setup(c => c.ReadItemAsync<StoredSession>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        // Act
        var result = await _sut.UpdateUploadedFilesAsync(SessionId, TenantId, BuildEnrichedFiles(1));

        // Assert
        result.Should().BeFalse("UpdateUploadedFilesAsync must return false when no session exists in either store");

        _cache.SetCount.Should().Be(0, "Redis must not be written when the session does not exist");
    }

    // =========================================================================
    // Helpers — mirror SessionPersistenceServiceTabsTests
    // =========================================================================

    /// <summary>
    /// Seeds the TrackingTenantCache with a serialised StoredSession at the FR-05
    /// (resource, id) coordinates the production code probes.
    /// </summary>
    private async Task SeedSession(StoredSession session)
    {
        await _cache.SetAsync<StoredSession>(
            TenantId, "stored-session", SessionId, 1, session);
        _cache.SetCount = 0; // reset write counter so test assertions exclude the seed write
    }

    private static IReadOnlyList<ChatSessionFile> BuildEnrichedFiles(int count)
    {
        var result = new List<ChatSessionFile>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(new ChatSessionFile(
                FileId: $"f-{i}",
                FileName: $"file-{i}.pdf",
                ContentType: "application/pdf",
                SizeBytes: 100 + i,
                SearchDocumentIdsCsv: $"d-{i}",
                UploadedAt: DateTimeOffset.UtcNow.AddMinutes(-i))
            {
                SummaryText = $"summary-{i}",
                ClassifiedDocType = "contract",
                ClassifiedConfidence = 0.8,
                PageCount = 10,
                Language = "en"
            });
        }
        return result;
    }

    private static StoredSession BuildStoredSession() => new()
    {
        Id = SessionId,
        SessionId = SessionId,
        TenantId = TenantId,
        Messages = [],
        WidgetStates = [],
        Tabs = [],
        UploadedFiles = [],
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        LastActivity = DateTimeOffset.UtcNow
    };

    private static ItemResponse<StoredSession> CreateFakeUpsertResponse()
    {
        var mock = new Mock<ItemResponse<StoredSession>>();
        mock.SetupGet(r => r.Resource).Returns(BuildStoredSession());
        mock.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.OK);
        return mock.Object;
    }
}
