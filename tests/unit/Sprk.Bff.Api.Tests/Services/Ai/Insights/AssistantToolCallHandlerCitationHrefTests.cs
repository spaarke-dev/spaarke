using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Insights;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights;

/// <summary>
/// Unit tests for the Wave F task 052 / contract v1.1 citation <c>Href</c> projection in
/// <see cref="AssistantToolCallHandler"/>. Covers both the playbook path
/// (<c>BuildArtifactResponse</c>) and the RAG path (<c>ExecuteRagPathAsync</c> + the
/// streaming-shared <c>BuildRagResult</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Acceptance criteria (Full scope per F1 spike §F)</b>:
/// <list type="number">
///   <item>RAG hit with sprk_document Guid → href emitted (preview URL)</item>
///   <item>RAG hit orphan (ObservationId = null) → href = null</item>
///   <item>Playbook evidence with bare Guid ref → href emitted</item>
///   <item>Playbook evidence with spe://drive/X/item/Y → href = null (v1.2 deferred)</item>
///   <item>Playbook evidence with non-document RefType → href = null</item>
///   <item>BffBaseUrl unconfigured → href = null for all citations</item>
///   <item>BffBaseUrl with trailing slash → normalized in URL</item>
///   <item>Non-Guid ObservationId on RAG hit → href = null (defensive)</item>
/// </list>
/// </para>
/// <para>
/// <b>AIPU2-027 privilege filtering</b>: the href URL itself routes through the existing
/// <c>GET /api/documents/{id}/preview</c> endpoint which enforces OBO + Graph/Dataverse
/// ACL — no URL signing required. These unit tests don't exercise that auth layer
/// (covered separately by <c>FileAccessEndpointsTests</c>); they verify the citation
/// projection emits the correct URL shape.
/// </para>
/// </remarks>
public class AssistantToolCallHandlerCitationHrefTests
{
    private const string BffBaseUrl = "https://spaarke-bff-dev.azurewebsites.net";
    private const string SampleDocumentGuid = "11111111-2222-3333-4444-555555555555";
    private const string TenantId = "tenant-test";
    private const string CallerOid = "00000000-aaaa-bbbb-cccc-000000000001";
    private const string SubjectId = "M-2024-0001";
    private const string Subject = "matter:M-2024-0001";

    private readonly Mock<IInsightsIntentClassifier> _classifierMock = new(MockBehavior.Strict);
    private readonly TestOptionsMonitor<InsightsPlaybookNameMapOptions> _playbookNameMap =
        new(new InsightsPlaybookNameMapOptions
        {
            Map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["predict-matter-cost@v1"] = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            }
        });

    // ─────────────────────────────────────────────────────────────────────────
    // Pure helper tests (no handler instantiation required)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildHrefForObservationId_ReturnsPreviewUrl_WhenGuidAndBaseUrlValid()
    {
        // Act
        var href = AssistantToolCallHandler.BuildHrefForObservationId(SampleDocumentGuid, BffBaseUrl);

        // Assert — matches F1 spike §C recommended URL shape
        href.Should().Be($"{BffBaseUrl}/api/documents/{SampleDocumentGuid}/preview");
    }

    [Fact]
    public void BuildHrefForObservationId_ReturnsNull_WhenObservationIdIsNull()
    {
        var href = AssistantToolCallHandler.BuildHrefForObservationId(null, BffBaseUrl);
        href.Should().BeNull();
    }

    [Fact]
    public void BuildHrefForObservationId_ReturnsNull_WhenObservationIdIsNotGuid()
    {
        // Defensive: if the index ever carries a non-Guid ObservationId, surface null rather
        // than emit a URL guaranteed to 400 at the preview endpoint.
        var href = AssistantToolCallHandler.BuildHrefForObservationId("not-a-guid", BffBaseUrl);
        href.Should().BeNull();
    }

    [Fact]
    public void BuildHrefForObservationId_ReturnsNull_WhenBaseUrlIsNull()
    {
        // BffBaseUrl unconfigured → consumer falls back to display-name-only per §3.5.
        var href = AssistantToolCallHandler.BuildHrefForObservationId(SampleDocumentGuid, null);
        href.Should().BeNull();
    }

    [Fact]
    public void TryExtractDocumentIdFromEvidenceRef_ReturnsGuid_WhenBareGuid()
    {
        var ev = new EvidenceRef { RefType = "document", Ref = SampleDocumentGuid };
        var docId = AssistantToolCallHandler.TryExtractDocumentIdFromEvidenceRef(ev);
        docId.Should().Be(Guid.Parse(SampleDocumentGuid));
    }

    [Fact]
    public void TryExtractDocumentIdFromEvidenceRef_ReturnsNull_WhenSpeUriForm()
    {
        // Empirical (2026-06-03): spe://drive/X/item/Y IS the dominant production emission
        // (FilesIndexIngestDocumentSource.cs:166). v1.1 returns null + defers driveItemId
        // → sprk_document resolution to v1.2 (would require async projection path).
        var ev = new EvidenceRef
        {
            RefType = "document",
            Ref = "spe://drive/b!abc123/item/01ABCDEF"
        };
        var docId = AssistantToolCallHandler.TryExtractDocumentIdFromEvidenceRef(ev);
        docId.Should().BeNull();
    }

    [Theory]
    [InlineData("fact-source")]
    [InlineData("comparable-matter")]
    [InlineData("supporting-matter")]
    [InlineData("playbook-run")]
    public void TryExtractDocumentIdFromEvidenceRef_ReturnsNull_WhenNonDocumentRefType(string refType)
    {
        var ev = new EvidenceRef { RefType = refType, Ref = SampleDocumentGuid };
        var docId = AssistantToolCallHandler.TryExtractDocumentIdFromEvidenceRef(ev);
        docId.Should().BeNull();
    }

    [Fact]
    public async Task BuildHrefForObservationId_NormalizesTrailingSlash_ViaHandler()
    {
        // Helper itself does not strip trailing slash — the handler's GetBffBaseUrl does
        // because BffBaseUrl is environment-configured. Test the integrated handler path
        // by exercising the RAG projection through ExecuteAsync with a trailing-slash url.
        var handler = BuildHandler(BffBaseUrl + "/");

        var ragResult = new InsightsSearchFacadeResult
        {
            Results = new[]
            {
                new InsightsSearchHit(
                    ChunkId: "chunk-1",
                    ObservationId: SampleDocumentGuid,
                    DocumentName: "Test.pdf",
                    Snippet: "snippet",
                    Predicate: null,
                    Confidence: 0.9)
            },
            Summary = "summary",
            Query = "query"
        };

        var facadeRequest = BuildFacadeRequest(forceMode: "rag");
        var result = await handler.ExecuteAsync(
            facadeRequest,
            playbookInvoker: (_, _) => throw new InvalidOperationException("playbook should not be invoked"),
            ragInvoker: (_, _) => Task.FromResult(ragResult),
            CancellationToken.None);

        result.Citations.Should().HaveCount(1);
        // Trailing slash on env config should NOT produce double-slash in href.
        result.Citations[0].Href.Should().Be($"{BffBaseUrl}/api/documents/{SampleDocumentGuid}/preview");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Integrated RAG-path tests via ExecuteAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RagPath_EmitsHrefForLinkedHit_AndNullForOrphan()
    {
        // Arrange — two RAG hits: one linked to sprk_document Guid, one orphan.
        var handler = BuildHandler(BffBaseUrl);
        var ragResult = new InsightsSearchFacadeResult
        {
            Results = new[]
            {
                new InsightsSearchHit(
                    ChunkId: "chunk-1",
                    ObservationId: SampleDocumentGuid,  // linked → href expected
                    DocumentName: "ContractA.pdf",
                    Snippet: "snippet A",
                    Predicate: null,
                    Confidence: 0.92),
                new InsightsSearchHit(
                    ChunkId: "chunk-2",
                    ObservationId: null,                 // orphan → href = null
                    DocumentName: "OrphanChunk.txt",
                    Snippet: "snippet B",
                    Predicate: null,
                    Confidence: 0.71)
            },
            Summary = "Summary [1] [2]",
            Query = "query"
        };

        // Act
        var facadeRequest = BuildFacadeRequest(forceMode: "rag");
        var result = await handler.ExecuteAsync(
            facadeRequest,
            playbookInvoker: (_, _) => throw new InvalidOperationException("playbook should not be invoked"),
            ragInvoker: (_, _) => Task.FromResult(ragResult),
            CancellationToken.None);

        // Assert
        result.Path.Should().Be("rag");
        result.Citations.Should().HaveCount(2);
        result.Citations[0].Href.Should().Be($"{BffBaseUrl}/api/documents/{SampleDocumentGuid}/preview");
        result.Citations[1].Href.Should().BeNull("orphan chunk has no sprk_document parent — consumer falls back to display-name-only per §3.5");
    }

    [Fact]
    public async Task ExecuteAsync_RagPath_AllHrefNull_WhenBffBaseUrlUnconfigured()
    {
        // Arrange — RAG hit linked to a real sprk_document, but BffBaseUrl is not configured.
        var handler = BuildHandler(bffBaseUrl: null);
        var ragResult = new InsightsSearchFacadeResult
        {
            Results = new[]
            {
                new InsightsSearchHit(
                    ChunkId: "chunk-1",
                    ObservationId: SampleDocumentGuid,
                    DocumentName: "ContractA.pdf",
                    Snippet: "snippet",
                    Predicate: null,
                    Confidence: 0.9)
            },
            Summary = "summary",
            Query = "query"
        };

        // Act
        var facadeRequest = BuildFacadeRequest(forceMode: "rag");
        var result = await handler.ExecuteAsync(
            facadeRequest,
            playbookInvoker: (_, _) => throw new InvalidOperationException("playbook should not be invoked"),
            ragInvoker: (_, _) => Task.FromResult(ragResult),
            CancellationToken.None);

        // Assert
        result.Citations.Should().HaveCount(1);
        result.Citations[0].Href.Should().BeNull("BffBaseUrl unset → all citations omit href; consumer renders display-name-only");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Integrated playbook-path tests via ExecuteAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PlaybookPath_EmitsHref_ForBareGuidEvidence()
    {
        // Arrange — InferenceArtifact carrying one document-type Evidence with bare Guid ref.
        var handler = BuildHandler(BffBaseUrl);
        var artifact = BuildInferenceArtifact(documentEvidenceRef: SampleDocumentGuid);
        var agentResult = new InsightsAgentResult { Artifact = artifact, Decline = null, CacheHit = false };

        // Act
        var facadeRequest = BuildFacadeRequest(forceMode: "playbook");
        var result = await handler.ExecuteAsync(
            facadeRequest,
            playbookInvoker: (_, _) => Task.FromResult(agentResult),
            ragInvoker: (_, _) => throw new InvalidOperationException("RAG should not be invoked"),
            CancellationToken.None);

        // Assert — first citation (document evidence) gets href; second (playbook-run) does not.
        result.Path.Should().Be("playbook");
        result.Citations.Should().HaveCount(2);
        result.Citations[0].Source.Should().Be(SampleDocumentGuid, "document evidence is projected first");
        result.Citations[0].Href.Should().Be($"{BffBaseUrl}/api/documents/{SampleDocumentGuid}/preview");
        result.Citations[1].Source.Should().StartWith("playbook://", "playbook-run evidence trails");
        result.Citations[1].Href.Should().BeNull("non-document evidence types have no preview URL");
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookPath_HrefNull_ForSpeUriEvidence()
    {
        // Arrange — dominant production emission shape (spe://drive/X/item/Y).
        var handler = BuildHandler(BffBaseUrl);
        var artifact = BuildInferenceArtifact(documentEvidenceRef: "spe://drive/b!testDrive/item/01ABCDEF");
        var agentResult = new InsightsAgentResult { Artifact = artifact, Decline = null, CacheHit = false };

        // Act
        var facadeRequest = BuildFacadeRequest(forceMode: "playbook");
        var result = await handler.ExecuteAsync(
            facadeRequest,
            playbookInvoker: (_, _) => Task.FromResult(agentResult),
            ragInvoker: (_, _) => throw new InvalidOperationException("RAG should not be invoked"),
            CancellationToken.None);

        // Assert
        result.Citations.Should().HaveCount(2);
        result.Citations[0].Href.Should().BeNull(
            "spe://drive/X/item/Y requires async sprk_document lookup — deferred to v1.2");
        // Citation still emits (Source + Excerpt) so consumer falls back to display-name-only.
        result.Citations[0].Source.Should().StartWith("spe://drive/");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V1.0 backwards-compat — verify the record default keeps existing call sites alive
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AssistantQueryCitation_DefaultsHrefNull_WhenConstructedV10Style()
    {
        // V1.0 call site (positional, no Href arg) should compile and surface Href = null.
        // This guards against future contract drift forcing all consumers to know about Href.
        var c = new AssistantQueryCitation(
            N: 1,
            Source: "doc.pdf",
            Excerpt: "snippet",
            ObservationId: SampleDocumentGuid,
            ChunkId: "chunk-1");

        c.Href.Should().BeNull();
    }

    [Fact]
    public void AssistantQueryCitation_SerializesHrefAsLowercaseKey_WhenPresent()
    {
        // Contract v1.1 §3.6: JSON key is "href" (lowercase).
        var c = new AssistantQueryCitation(
            N: 1,
            Source: "doc.pdf",
            Excerpt: "snippet",
            ObservationId: SampleDocumentGuid,
            ChunkId: "chunk-1",
            Href: "https://example.com/preview");

        var json = JsonSerializer.Serialize(c, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("\"href\":\"https://example.com/preview\"");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private AssistantToolCallHandler BuildHandler(string? bffBaseUrl)
    {
        var options = new AssistantCitationHrefOptions { BffBaseUrl = bffBaseUrl };
        return new AssistantToolCallHandler(
            _classifierMock.Object,
            _playbookNameMap,
            new TestOptionsMonitor<AssistantCitationHrefOptions>(options),
            new ConfigurationBuilder().Build(),
            NullLogger<AssistantToolCallHandler>.Instance);
    }

    private static AssistantQueryFacadeRequest BuildFacadeRequest(string forceMode)
        => new(
            Query: "Test query",
            ParentEntityType: "matter",
            ParentEntityId: SubjectId,
            Subject: Subject,
            ForceMode: forceMode,
            ConversationId: null,
            PreviousTurnSummary: null,
            TenantId: TenantId,
            CallerOid: CallerOid,
            CallerPrincipal: null);

    private static InferenceArtifact BuildInferenceArtifact(string documentEvidenceRef)
    {
        var evidence = new List<EvidenceRef>
        {
            new()
            {
                RefType = "document",
                Ref = documentEvidenceRef,
                Quote = "Supporting quote"
            },
            new()
            {
                RefType = "playbook-run",
                Ref = $"playbook://predict-matter-cost@v1/run-{Guid.NewGuid():N}",
                Quote = null
            }
        };

        return new InferenceArtifact
        {
            Id = $"inf:predict-matter-cost:{SubjectId}",
            Subject = Subject,
            Predicate = "predictedCost",
            Value = new Value
            {
                Raw = JsonDocument.Parse("{\"p50\":250000}").RootElement,
                DisplayHint = "currency-usd"
            },
            Evidence = evidence,
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy { Kind = "agent", Id = "agent://insights-v1", Version = "v1" },
            Scope = new Scope { TenantId = TenantId },
            TenantId = TenantId,
            Confidence = 0.74,
            Reasoning = "Reasoning text"
        };
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) { _value = value; }
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
