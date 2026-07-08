using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Ai.Telemetry;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-P0-01 acceptance tests (spaarke-ai-architecture-redesign-r1 task 001, ADR-040):
/// the session-ledger round-trip — write → Redis → Cosmos → restore — preserves the four
/// typed ledger collections (Outputs keyed <c>{bindingId}@t{n}</c>, ToolChains,
/// WidgetEvents, Gates) AND the document/file references the prior Cosmos mapping dropped
/// (DocumentId, AdditionalDocumentIds, the UploadedFiles manifest — the audited P2
/// violation).
///
/// Serialization fidelity is exercised for real on both wire legs:
///   - Redis leg: <see cref="TrackingTenantCache"/>/<c>InMemoryTenantCache</c> serialize
///     through System.Text.Json (Web defaults) — identical to production
///     <c>TenantCache</c>.
///   - Cosmos leg: the captured <see cref="StoredSession"/> upsert document is
///     round-tripped through Newtonsoft.Json with the camelCase contract resolver —
///     identical to the Cosmos SDK built-in serializer configured in
///     <c>AiPersistenceModule</c> (<c>CosmosPropertyNamingPolicy.CamelCase</c>).
///
/// These are regression-protecting contract anchors for the ledger persistence shape all
/// later phases (P1 universal ledger write, P2 ToolChain persistence, P3 ledger-referencing
/// capabilities) build on — maintain-class per ADR-038.
/// </summary>
public class ChatSessionLedgerRoundTripTests
{
    private const string TenantId = "tenant-ledger";
    private const string DatabaseName = "spaarke-ai";

    // ---------------------------------------------------------------------
    // Test 1 — Redis leg: the hot-tier ChatSession JSON round-trip preserves
    // all four ledger collections and file references.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UpdateSessionCache_WithLedgerEntriesAndFileRefs_SurvivesRedisRoundTrip()
    {
        // Arrange
        var cache = new TrackingTenantCache();
        var manager = new ChatSessionManager(
            cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object);
        var original = BuildFullyPopulatedSession();

        // Act — write through the production path, read back through the production path
        await manager.UpdateSessionCacheAsync(original);
        var restored = await manager.GetSessionAsync(TenantId, original.SessionId);

        // Assert — the round-trip deserialized a NEW instance from real JSON bytes
        restored.Should().NotBeNull();
        AssertSessionEquivalence(restored!, original);
    }

    // ---------------------------------------------------------------------
    // Test 2 — THE FR-P0-01 acceptance round-trip:
    // write → Redis → Cosmos upsert → Cosmos wire (Newtonsoft camelCase) →
    // restore on a cold node (Redis miss → Cosmos hit) → ChatSession.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetSession_AfterCosmosRestore_PreservesLedgerEntriesAndFileReferences()
    {
        // ---- Arrange: "hot" node — real SessionPersistenceService capturing the upsert ----
        var upsertCaptured = new TaskCompletionSource<StoredSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hotContainer = new Mock<Container>();
        hotContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<StoredSession, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (s, _, _, _) => upsertCaptured.TrySetResult(s))
            .ReturnsAsync(Mock.Of<ItemResponse<StoredSession>>());

        var hotManager = new ChatSessionManager(
            new TrackingTenantCache(),
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object,
            CreatePersistenceService(hotContainer, new TrackingTenantCache()));

        var original = BuildFullyPopulatedSession();

        // ---- Act 1: write-through (Redis + fire-and-forget Cosmos upsert) ----
        await hotManager.UpdateSessionCacheAsync(original);
        var upserted = await upsertCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // ---- Act 2: simulate the Cosmos wire exactly as the SDK's built-in serializer
        // does (Newtonsoft + camelCase contract resolver per AiPersistenceModule) ----
        var wireSettings = new Newtonsoft.Json.JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
        var wireJson = Newtonsoft.Json.JsonConvert.SerializeObject(upserted, wireSettings);
        var fromWire = Newtonsoft.Json.JsonConvert.DeserializeObject<StoredSession>(wireJson, wireSettings)!;

        // Wire sanity: the document carries the ledger + file refs in camelCase
        wireJson.Should().Contain("\"outputs\"").And.Contain("\"toolChains\"")
            .And.Contain("\"widgetEvents\"").And.Contain("\"gates\"")
            .And.Contain("\"uploadedFiles\"").And.Contain("\"additionalDocumentIds\"")
            .And.Contain("\"documentId\"");

        // ---- Arrange: "cold" node — empty Redis, Cosmos returns the wire document ----
        var coldContainer = new Mock<Container>();
        var readResponse = new Mock<ItemResponse<StoredSession>>();
        readResponse.SetupGet(r => r.Resource).Returns(fromWire);
        readResponse.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.OK);
        coldContainer
            .Setup(c => c.ReadItemAsync<StoredSession>(
                original.SessionId,
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(readResponse.Object);
        coldContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<StoredSession>>());

        var dataverseRepo = new Mock<IChatDataverseRepository>();
        var coldManager = new ChatSessionManager(
            new TrackingTenantCache(),
            dataverseRepo.Object,
            new Mock<ILogger<ChatSessionManager>>().Object,
            CreatePersistenceService(coldContainer, new TrackingTenantCache()));

        // ---- Act 3: restore (Redis miss → Cosmos hit → StoredSession → ChatSession) ----
        var restored = await coldManager.GetSessionAsync(TenantId, original.SessionId);

        // ---- Assert ----
        restored.Should().NotBeNull("the session must be restorable from the Cosmos warm tier");
        AssertSessionEquivalence(restored!, original);

        // Cold-tier fallback must NOT have been needed — Cosmos alone restored everything
        dataverseRepo.Verify(
            r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "document/file references must be restored from Cosmos, not require the Dataverse cold path");
    }

    // ---------------------------------------------------------------------
    // Test 3 — additive schema evolution: a pre-ledger Cosmos document (no
    // ledger fields, no document-reference fields) restores cleanly with
    // null ledger collections and null file references.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetSession_FromPreLedgerCosmosDocument_RestoresWithNullLedgerAndNullFileRefs()
    {
        // Arrange — a warm document shaped exactly as pre-FR-P0-01 code wrote it
        const string sessionId = "pre-ledger-session";
        var preLedgerJson = $$"""
        {
          "id": "{{sessionId}}",
          "sessionId": "{{sessionId}}",
          "tenantId": "{{TenantId}}",
          "playbookId": null,
          "messages": [
            {
              "messageId": "m-1",
              "role": "user",
              "content": "hello",
              "timestamp": "2026-07-01T10:00:00+00:00",
              "metadata": { "tokenCount": "3", "sequenceNumber": "0" }
            }
          ],
          "widgetStates": {},
          "createdAt": "2026-07-01T10:00:00+00:00",
          "lastActivity": "2026-07-01T10:05:00+00:00"
        }
        """;
        var fromWire = Newtonsoft.Json.JsonConvert.DeserializeObject<StoredSession>(
            preLedgerJson,
            new Newtonsoft.Json.JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() })!;

        var container = new Mock<Container>();
        var readResponse = new Mock<ItemResponse<StoredSession>>();
        readResponse.SetupGet(r => r.Resource).Returns(fromWire);
        readResponse.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.OK);
        container
            .Setup(c => c.ReadItemAsync<StoredSession>(
                sessionId,
                new PartitionKey(TenantId),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(readResponse.Object);
        container
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<StoredSession>(),
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<StoredSession>>());

        var manager = new ChatSessionManager(
            new TrackingTenantCache(),
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object,
            CreatePersistenceService(container, new TrackingTenantCache()));

        // Act
        var restored = await manager.GetSessionAsync(TenantId, sessionId);

        // Assert — old documents restore; absent fields surface as "none" semantics
        restored.Should().NotBeNull();
        restored!.Messages.Should().ContainSingle(m => m.Content == "hello");
        restored.DocumentId.Should().BeNull();
        restored.AdditionalDocumentIds.Should().BeNull();
        restored.UploadedFiles.Should().BeNull();
        restored.Outputs.Should().BeNull();
        restored.ToolChains.Should().BeNull();
        restored.WidgetEvents.Should().BeNull();
        restored.Gates.Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // Test 4 — the addressable-key contract: SessionLedger.BuildOutputKey
    // produces the canonical {bindingId}@t{n} shape (ADR-040 MUST).
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("chat-summarize", 3, "chat-summarize@t3")]
    [InlineData("loop", 12, "loop@t12")]
    public void BuildOutputKey_GivenBindingAndTurn_ProducesAddressableKey(
        string bindingId, int turn, string expected)
        => SessionLedger.BuildOutputKey(bindingId, turn).Should().Be(expected);

    // =========================================================================
    // Helpers
    // =========================================================================

    private static SessionPersistenceService CreatePersistenceService(
        Mock<Container> container, TrackingTenantCache cache)
    {
        var cosmosClient = new Mock<CosmosClient>();
        cosmosClient
            .Setup(c => c.GetContainer(DatabaseName, "sessions"))
            .Returns(container.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosPersistence:DatabaseName"] = DatabaseName
            })
            .Build();

        return new SessionPersistenceService(
            cache,
            cosmosClient.Object,
            configuration,
            new Mock<ILogger<SessionPersistenceService>>().Object,
            new Mock<IContextEventEmitter>().Object);
    }

    /// <summary>
    /// Builds a ChatSession populated with every collection the round-trip must preserve:
    /// messages, document references (DocumentId + AdditionalDocumentIds + a 2-file
    /// UploadedFiles manifest with nested sections/tables/citations), and all four typed
    /// ledger collections.
    /// </summary>
    private static ChatSession BuildFullyPopulatedSession()
    {
        var t0 = DateTimeOffset.Parse("2026-07-05T09:00:00+00:00");
        var sessionId = Guid.NewGuid().ToString("N");

        var payload = JsonSerializer.SerializeToElement(new
        {
            summary = "Executive summary of the NDA.",
            riskFlags = new[] { "auto-renewal", "unlimited-liability" }
        });
        var widgetPayload = JsonSerializer.SerializeToElement(new { selectedText = "clause 4.2" });

        return new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: "spe-doc-001",
            PlaybookId: Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"),
            CreatedAt: t0,
            LastActivity: t0.AddMinutes(10),
            Messages: new List<ChatMessage>
            {
                new(MessageId: "m-1", SessionId: sessionId, Role: ChatMessageRole.User,
                    Content: "Summarize the uploaded NDA.", TokenCount: 8, CreatedAt: t0, SequenceNumber: 0),
                new(MessageId: "m-2", SessionId: sessionId, Role: ChatMessageRole.Assistant,
                    Content: "Here is the summary…", TokenCount: 42, CreatedAt: t0.AddMinutes(1), SequenceNumber: 1)
            },
            AdditionalDocumentIds: new List<string> { "spe-doc-002", "spe-doc-003" },
            UploadedFiles: new List<ChatSessionFile>
            {
                new(FileId: "f-1", FileName: "nda.pdf", ContentType: "application/pdf",
                    SizeBytes: 12_345, SearchDocumentIdsCsv: "d-1,d-2", UploadedAt: t0.AddMinutes(2))
                {
                    SummaryText = "1-paragraph NDA summary",
                    ClassifiedDocType = "NDA",
                    ClassifiedConfidence = 0.92,
                    Sections = new[] { new SectionInfo("Recitals", 0, 500, 1, 1) },
                    TableMetadata = new[] { new TableInfo("Table 1", 800, 2) },
                    Citations = new[] { new CitationReference("d-1", "quoted text", 1) },
                    PageCount = 12,
                    Language = "en"
                },
                new(FileId: "f-2", FileName: "amendment.docx",
                    ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    SizeBytes: 6_789, SearchDocumentIdsCsv: "d-3", UploadedAt: t0.AddMinutes(3))
            })
        {
            Outputs = new List<SessionOutput>
            {
                new()
                {
                    Key = SessionLedger.BuildOutputKey("chat-summarize", 2),
                    BindingId = "chat-summarize",
                    UcId = "UC-A-1",
                    Turn = 2,
                    Disposition = "informational",
                    Payload = payload,
                    WidgetId = null,
                    SourceRefs = new[] { "f-1", "d-1" },
                    CreatedAt = t0.AddMinutes(4)
                },
                new()
                {
                    Key = SessionLedger.BuildOutputKey("loop", 3),
                    BindingId = "loop",
                    UcId = "UC-A-2",
                    Turn = 3,
                    Disposition = "overlay",
                    Payload = JsonSerializer.SerializeToElement(new { note = "second output" }),
                    WidgetId = "widget-42",
                    SourceRefs = new[] { "chat-summarize@t2" },
                    CreatedAt = t0.AddMinutes(5)
                }
            },
            ToolChains = new List<SessionToolChain>
            {
                new()
                {
                    Turn = 3,
                    CreatedAt = t0.AddMinutes(5),
                    Calls = new List<SessionToolCall>
                    {
                        new()
                        {
                            ToolId = "spaarke.search.documents",
                            ArgsSummary = "matterId=123; top=5",
                            ResultCount = 5,
                            Citations = new[] { "d-1", "d-2" },
                            DurationMs = 420
                        },
                        new() { ToolId = "spaarke.session.recall", ResultCount = 1 }
                    }
                }
            },
            WidgetEvents = new List<SessionWidgetEvent>
            {
                new()
                {
                    WidgetId = "widget-42",
                    EventType = "selection",
                    Turn = 3,
                    EntryRefs = new[] { "loop@t3" },
                    Payload = widgetPayload,
                    CreatedAt = t0.AddMinutes(6)
                }
            },
            Gates = new List<SessionGate>
            {
                new()
                {
                    GateId = "gate-1",
                    Kind = "confirmation",
                    Status = "pending",
                    Turn = 3,
                    BindingId = "draft-correspondence",
                    SideEffectClass = "communicate",
                    OutputKey = null,
                    CreatedAt = t0.AddMinutes(7),
                    ResolvedAt = null
                }
            }
        };
    }

    /// <summary>
    /// Full structural equivalence between a restored session and the original — every
    /// collection the FR-P0-01 round-trip is contractually required to preserve.
    /// </summary>
    private static void AssertSessionEquivalence(ChatSession restored, ChatSession original)
    {
        // Identity + document references (the previously-dropped fields)
        restored.SessionId.Should().Be(original.SessionId);
        restored.TenantId.Should().Be(original.TenantId);
        restored.PlaybookId.Should().Be(original.PlaybookId);
        restored.DocumentId.Should().Be(original.DocumentId,
            "ADR-040: document references MUST survive warm-store restore");
        restored.AdditionalDocumentIds.Should().BeEquivalentTo(
            original.AdditionalDocumentIds, o => o.WithStrictOrdering());

        // Uploaded-file manifest — the audited Cosmos file-reference drop
        restored.UploadedFiles.Should().NotBeNull("the file manifest MUST survive restore");
        var originalFiles = original.UploadedFiles!;
        var restoredFiles = restored.UploadedFiles!;
        restoredFiles.Should().HaveCount(originalFiles.Count);
        for (var i = 0; i < originalFiles.Count; i++)
        {
            var expected = originalFiles[i];
            var actual = restoredFiles[i];
            actual.FileId.Should().Be(expected.FileId);
            actual.FileName.Should().Be(expected.FileName);
            actual.ContentType.Should().Be(expected.ContentType);
            actual.SizeBytes.Should().Be(expected.SizeBytes);
            actual.SearchDocumentIdsCsv.Should().Be(expected.SearchDocumentIdsCsv,
                "AI Search document references are the cleanup + retrieval keys");
            actual.UploadedAt.Should().Be(expected.UploadedAt);
            actual.SummaryText.Should().Be(expected.SummaryText);
            actual.ClassifiedDocType.Should().Be(expected.ClassifiedDocType);
            actual.ClassifiedConfidence.Should().Be(expected.ClassifiedConfidence);
            actual.Sections.Should().BeEquivalentTo(expected.Sections, o => o.WithStrictOrdering());
            actual.TableMetadata.Should().BeEquivalentTo(expected.TableMetadata, o => o.WithStrictOrdering());
            actual.Citations.Should().BeEquivalentTo(expected.Citations, o => o.WithStrictOrdering());
            actual.PageCount.Should().Be(expected.PageCount);
            actual.Language.Should().Be(expected.Language);
        }

        // Messages
        restored.Messages.Should().HaveCount(original.Messages.Count);
        for (var i = 0; i < original.Messages.Count; i++)
        {
            restored.Messages[i].MessageId.Should().Be(original.Messages[i].MessageId);
            restored.Messages[i].Role.Should().Be(original.Messages[i].Role);
            restored.Messages[i].Content.Should().Be(original.Messages[i].Content);
        }

        // Ledger: Outputs — addressable keys, order, payload fidelity
        restored.Outputs.Should().NotBeNull();
        var originalOutputs = original.Outputs!;
        var restoredOutputs = restored.Outputs!;
        restoredOutputs.Should().HaveCount(originalOutputs.Count);
        for (var i = 0; i < originalOutputs.Count; i++)
        {
            var expected = originalOutputs[i];
            var actual = restoredOutputs[i];
            actual.Key.Should().Be(expected.Key, "outputs are addressable by {bindingId}@t{n}");
            actual.Key.Should().Be($"{expected.BindingId}@t{expected.Turn}");
            actual.BindingId.Should().Be(expected.BindingId);
            actual.UcId.Should().Be(expected.UcId);
            actual.Turn.Should().Be(expected.Turn);
            actual.Disposition.Should().Be(expected.Disposition);
            actual.WidgetId.Should().Be(expected.WidgetId);
            actual.SourceRefs.Should().BeEquivalentTo(expected.SourceRefs, o => o.WithStrictOrdering());
            actual.CreatedAt.Should().Be(expected.CreatedAt);
            actual.Payload.GetRawText().Should().Be(expected.Payload.GetRawText(),
                "the schema-validated payload must round-trip byte-identical");
        }

        // Ledger: ToolChains (identifiers/filters/counts only — NFR-07 shape)
        restored.ToolChains.Should().NotBeNull();
        restored.ToolChains!.Should().BeEquivalentTo(original.ToolChains, o => o.WithStrictOrdering());

        // Ledger: WidgetEvents (payload compared as raw JSON)
        restored.WidgetEvents.Should().NotBeNull();
        var originalEvents = original.WidgetEvents!;
        var restoredEvents = restored.WidgetEvents!;
        restoredEvents.Should().HaveCount(originalEvents.Count);
        for (var i = 0; i < originalEvents.Count; i++)
        {
            var expected = originalEvents[i];
            var actual = restoredEvents[i];
            actual.WidgetId.Should().Be(expected.WidgetId);
            actual.EventType.Should().Be(expected.EventType);
            actual.Turn.Should().Be(expected.Turn);
            actual.EntryRefs.Should().BeEquivalentTo(expected.EntryRefs, o => o.WithStrictOrdering());
            actual.CreatedAt.Should().Be(expected.CreatedAt);
            actual.Payload?.GetRawText().Should().Be(expected.Payload?.GetRawText());
        }

        // Ledger: Gates
        restored.Gates.Should().NotBeNull();
        restored.Gates!.Should().BeEquivalentTo(original.Gates, o => o.WithStrictOrdering());
    }
}
