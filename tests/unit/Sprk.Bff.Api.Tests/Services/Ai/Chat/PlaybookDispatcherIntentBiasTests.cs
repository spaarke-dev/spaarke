using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Tests for the FR-20 / task 115 extension of
/// <see cref="PlaybookDispatcher.RunPhaseBVectorMatchAsync"/> with the optional
/// <c>intentHint</c> parameter — the slash-derived vector-query bias.
///
/// <para>
/// <b>FR-20 invariants covered</b>:
/// <list type="bullet">
///   <item><description>When <c>intentHint</c> is non-null/non-whitespace, the composed
///     per-file query is prefixed with <c>"Intent: {intentHint} | "</c> on both the
///     manifest-present and manifest-absent paths.</description></item>
///   <item><description>When <c>intentHint</c> is null / empty / whitespace, the composed
///     query is IDENTICAL to the pre-task-115 task-112 behaviour (no Intent segment).</description></item>
///   <item><description>For the same <c>userMessage</c>, the embed-input string when
///     <c>intentHint="summarize"</c> is DIFFERENT from the embed-input string when
///     <c>intentHint=null</c> — the FR-20 bias is observable in the embedding pipeline
///     and cannot be silently dropped.</description></item>
///   <item><description>Multi-file: every file in the same turn receives the same Intent
///     segment in its composed query.</description></item>
///   <item><description>ADR-014 cache key: identical message + identical intent hit the
///     cache; identical message + different intent does NOT hit the cache (different
///     intent must busts cache so stale bias-free results aren't returned).</description></item>
///   <item><description>ADR-015 tier-1 telemetry: the summary log line carries the
///     <c>intentHintProvided</c> boolean but NOT the hint value itself.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Mock setup mirrors <see cref="PlaybookDispatcherPhaseBTests"/>. The
/// <see cref="IOpenAiClient.GenerateEmbeddingAsync"/> mock captures the embed input
/// string so tests can assert on the composed query directly (the SearchPlaybooksAsync
/// path always embeds the query before searching).
/// </para>
/// </summary>
public class PlaybookDispatcherIntentBiasTests
{
    // ──────────────────────────────────────────────────────────
    // Test constants
    // ──────────────────────────────────────────────────────────

    private static readonly string PlaybookA = Guid.NewGuid().ToString();
    private static readonly string PlaybookB = Guid.NewGuid().ToString();
    private const string TestTenantId = "test-tenant-intent-115";
    private const string TestUserMessage = "summarize the attached contract";
    private const string TestIntentHint = "summarize";
    private const string TestClassifiedDocType = "NDA";

    // ──────────────────────────────────────────────────────────
    // Mocks
    // ──────────────────────────────────────────────────────────

    private readonly Mock<IChatClient> _executionClientMock;
    private readonly Mock<INodeService> _nodeServiceMock;
    private readonly InMemoryTenantCache _cache;
    private readonly Mock<ILogger<PlaybookDispatcher>> _loggerMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly Mock<SearchClient> _searchClientMock;
    private readonly Mock<ILogger<PlaybookEmbeddingService>> _embeddingLoggerMock;
    private readonly List<string> _capturedLogMessages = new();
    private readonly List<string> _capturedEmbedInputs = new();

    public PlaybookDispatcherIntentBiasTests()
    {
        _executionClientMock = new Mock<IChatClient>();
        _nodeServiceMock = new Mock<INodeService>();
        _cache = new InMemoryTenantCache();
        _loggerMock = new Mock<ILogger<PlaybookDispatcher>>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _searchIndexClientMock = new Mock<SearchIndexClient>(MockBehavior.Loose);
        _searchClientMock = new Mock<SearchClient>(MockBehavior.Loose);
        _embeddingLoggerMock = new Mock<ILogger<PlaybookEmbeddingService>>();

        _searchIndexClientMock
            .Setup(c => c.GetSearchClient(It.IsAny<string>()))
            .Returns(_searchClientMock.Object);

        // Capture every embed input so we can assert composed query shape.
        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, int?, CancellationToken>((q, _, _, _) =>
            {
                _capturedEmbedInputs.Add(q);
            })
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));

        // InMemoryTenantCache returns null/default by default — no cache-miss setup required.

        // Capture log messages so we can assert ADR-015 compliance (intent value never
        // leaks; provided-flag is logged).
        _loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(invocation =>
            {
                var state = invocation.Arguments[2];
                var ex = (Exception?)invocation.Arguments[3];
                var formatter = invocation.Arguments[4];
                var formatted = formatter.GetType()
                    .GetMethod("Invoke")!
                    .Invoke(formatter, new[] { state, ex });
                if (formatted is string s)
                {
                    _capturedLogMessages.Add(s);
                }
            }));
    }

    private PlaybookDispatcher CreateDispatcher(IMemoryCache? memoryCache = null)
    {
        var embeddingService = new PlaybookEmbeddingService(
            _searchIndexClientMock.Object,
            _openAiClientMock.Object,
            _embeddingLoggerMock.Object);

        return new PlaybookDispatcher(
            embeddingService,
            _executionClientMock.Object,
            _nodeServiceMock.Object,
            _cache,
            TestTenantId,
            _loggerMock.Object,
            memoryCache ?? new MemoryCache(new MemoryCacheOptions()));
    }

    // ══════════════════════════════════════════════════════════
    // Manifest-absent: intentHint prefixes composed query
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// FR-20 binding: when <c>intentHint="summarize"</c> is passed, the manifest-absent
    /// path's per-file composed query carries the <c>"Intent: summarize | "</c> prefix
    /// at the start of the embed input — biasing the embedding toward the slash intent.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_PrefixesIntentHint_OnManifestAbsentPath()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.86);
        var dispatcher = CreateDispatcher();
        var attachment = new ChatMessageAttachment(
            Filename: "contract-a.pdf",
            ContentType: "application/pdf",
            TextContent: "Sample contract content prefix...");

        // Act
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: TestIntentHint);

        // Assert
        results.Should().HaveCount(1);
        _capturedEmbedInputs.Should().HaveCount(1);
        var embedInput = _capturedEmbedInputs[0];
        embedInput.Should().StartWith($"Intent: {TestIntentHint} | ",
            "FR-20: intentHint must be the leading bias segment in the composed query");
        embedInput.Should().Contain(TestUserMessage);
        embedInput.Should().Contain("| Document: contract-a.pdf");
        embedInput.Should().Contain("| Type hint: application/pdf");
        embedInput.Should().Contain("| Content: Sample contract content prefix");
    }

    // ══════════════════════════════════════════════════════════
    // Manifest-present: intentHint prefixes composed query
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// FR-20 binding (manifest-present path): when <c>intentHint="summarize"</c> is
    /// passed, the structured-filter path STILL receives the bias on the embed input —
    /// the documentTypes filter narrows by structure while the embedding picks up
    /// intent semantics.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_PrefixesIntentHint_OnManifestPresentPath()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.91);
        var dispatcher = CreateDispatcher();
        var attachment = new ChatMessageAttachment(
            Filename: "nda-draft.pdf",
            ContentType: "application/pdf",
            TextContent: "Confidentiality clauses...");
        var sessionFile = MakeSessionFile("file-1", "nda-draft.pdf",
            classifiedDocType: TestClassifiedDocType);

        // Act
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: new[] { (ChatSessionFile?)sessionFile },
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: TestIntentHint);

        // Assert
        results.Should().HaveCount(1);
        results[0].ManifestPresent.Should().BeTrue();
        _capturedEmbedInputs.Should().HaveCount(1);
        var embedInput = _capturedEmbedInputs[0];
        embedInput.Should().StartWith($"Intent: {TestIntentHint} | ",
            "FR-20: bias is applied uniformly on manifest-present + manifest-absent paths");
        embedInput.Should().Contain(TestUserMessage);
    }

    // ══════════════════════════════════════════════════════════
    // Null/empty/whitespace intentHint → no bias segment
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Backward-compat (FR-20 / FR-17 v2): when intentHint is null, the composed query
    /// is IDENTICAL to the task-112 pre-task-115 shape — no leading <c>"Intent: …"</c>
    /// segment is introduced.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_NoBias_WhenIntentHintIsNull()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.86);
        var dispatcher = CreateDispatcher();
        var attachment = new ChatMessageAttachment(
            Filename: "contract-a.pdf",
            ContentType: "application/pdf",
            TextContent: "Sample contract content prefix...");

        // Act — intentHint omitted (null default)
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);

        // Assert
        _capturedEmbedInputs.Should().HaveCount(1);
        var embedInput = _capturedEmbedInputs[0];
        embedInput.Should().NotStartWith("Intent:",
            "FR-20 backward-compat: null intentHint must not introduce a bias segment");
        embedInput.Should().StartWith(TestUserMessage,
            "task-112 manifest-absent path shape is preserved when no intent is supplied");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public async Task RunPhaseBVectorMatchAsync_NoBias_WhenIntentHintIsEmptyOrWhitespace(string emptyHint)
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.86);
        var dispatcher = CreateDispatcher();
        var attachment = new ChatMessageAttachment(
            Filename: "contract-a.pdf",
            ContentType: "application/pdf",
            TextContent: "Sample contract content prefix...");

        // Act
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: emptyHint);

        // Assert — empty / whitespace-only hint is treated as absent (no bias segment).
        _capturedEmbedInputs.Should().HaveCount(1);
        _capturedEmbedInputs[0].Should().NotStartWith("Intent:",
            "FR-20: empty / whitespace intentHint is treated as absent");
    }

    // ══════════════════════════════════════════════════════════
    // Multi-file: same Intent segment on every per-file query
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Multi-file invariant: when intentHint is passed, every per-file composed query
    /// in the same turn carries the same Intent prefix. The slash bias is per-turn,
    /// not per-file.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_AllFilesGetSameIntentSegment()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.88);
        var dispatcher = CreateDispatcher();
        var attachments = new[]
        {
            new ChatMessageAttachment("a.pdf", "application/pdf", "Content A..."),
            new ChatMessageAttachment("b.pdf", "application/pdf", "Content B..."),
            new ChatMessageAttachment("c.pdf", "application/pdf", "Content C..."),
        };

        // Act
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: attachments,
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: TestIntentHint);

        // Assert — every composed query starts with the SAME bias segment
        _capturedEmbedInputs.Should().HaveCount(3);
        foreach (var embed in _capturedEmbedInputs)
        {
            embed.Should().StartWith($"Intent: {TestIntentHint} | ",
                "FR-20: per-turn intent bias applies uniformly to every per-file query");
        }
    }

    // ══════════════════════════════════════════════════════════
    // FR-20 invariant test — bias is observable / cannot be dropped
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// FR-20 binding regression guard: for the SAME <c>userMessage</c>, the composed
    /// query string MUST DIFFER when <c>intentHint="summarize"</c> vs <c>intentHint=null</c>.
    /// This guards against a silent parameter drop (whether by signature regression or
    /// a path that forgets to thread the hint into the helper). The reverse direction
    /// of "slash + NL flows produce identical routing for same query text" — when slash
    /// IS present, the embedding input MUST shift.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_BiasObservable_WhenSameMessageDifferentIntent()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.86);

        // Use a fresh in-memory cache so the second call's cache key differs (different
        // intent) without returning a stale result from the first call.
        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = CreateDispatcher(sharedCache);

        var attachment = new ChatMessageAttachment(
            Filename: "contract.pdf",
            ContentType: "application/pdf",
            TextContent: "Stable content...");

        // Act — turn 1: no intent hint
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: null);

        var embedWithoutIntent = _capturedEmbedInputs.Last();

        // Act — turn 2: same message + attachment, with "summarize" intent
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: TestIntentHint);

        var embedWithIntent = _capturedEmbedInputs.Last();

        // Assert — embed inputs MUST differ (the bias is observable in the pipeline).
        embedWithIntent.Should().NotBe(embedWithoutIntent,
            "FR-20: the slash-derived intent bias MUST shift the composed query; " +
            "if these are equal, the intentHint parameter has been silently dropped");
        embedWithIntent.Should().StartWith($"Intent: {TestIntentHint} | ");
        embedWithoutIntent.Should().NotStartWith("Intent:");
    }

    // ══════════════════════════════════════════════════════════
    // ADR-014 cache key — intent shifts must bust cache
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-014: identical message + identical intent hit the 5-min TTL cache. Identical
    /// message + DIFFERENT intent must MISS the cache so the (no-bias) result is not
    /// returned for a biased query. This applies to BOTH manifest-present and
    /// manifest-absent paths.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_CacheBustsOnIntentChange_ManifestAbsent()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.9);
        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = CreateDispatcher(sharedCache);
        var attachment = new ChatMessageAttachment(
            Filename: "cached.pdf",
            ContentType: "application/pdf",
            TextContent: "Stable content...");

        // Act — three calls: noIntent, noIntent (cache hit), summarize (cache miss)
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: null);
        var embedCallsAfterCall1 = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: null);
        var embedCallsAfterCall2 = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: TestIntentHint);
        var embedCallsAfterCall3 = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        // Assert
        embedCallsAfterCall1.Should().Be(1, "first call (no intent) is a cache miss");
        embedCallsAfterCall2.Should().Be(1, "second call (same key) hits cache");
        embedCallsAfterCall3.Should().Be(2,
            "ADR-014: different intent must bust the cache — a stale no-bias result " +
            "must NOT be returned for a biased query");
    }

    [Fact]
    public async Task RunPhaseBVectorMatchAsync_CacheBustsOnIntentChange_ManifestPresent()
    {
        // Arrange — manifest-present path: same message + classified doc type, different intent
        SetupSearchReturnsCandidates(score: 0.91);
        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = CreateDispatcher(sharedCache);
        var attachment = new ChatMessageAttachment(
            Filename: "nda-draft.pdf",
            ContentType: "application/pdf",
            TextContent: "NDA clauses...");
        var sessionFile = MakeSessionFile("file-1", "nda-draft.pdf",
            classifiedDocType: TestClassifiedDocType);

        // Act — three calls
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: new[] { (ChatSessionFile?)sessionFile },
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: null);
        var afterCall1 = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: new[] { (ChatSessionFile?)sessionFile },
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: null);
        var afterCall2 = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: new[] { (ChatSessionFile?)sessionFile },
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: TestIntentHint);
        var afterCall3 = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        // Assert
        afterCall1.Should().Be(1);
        afterCall2.Should().Be(1, "same message + same intent + same docType hits cache");
        afterCall3.Should().Be(2,
            "ADR-014: intent shift on manifest-present path must bust the cache");
    }

    // ══════════════════════════════════════════════════════════
    // ADR-015 telemetry — intentHintProvided logged; value NOT logged
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-015 tier-1: the Phase B summary log line MUST carry the
    /// <c>intentHintProvided</c> boolean (so operators can see the bias was applied)
    /// but MUST NOT carry the hint value itself. We use a sentinel-marker intent hint
    /// and assert it is absent from every captured log line.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_LogsIntentHintProvidedFlag_NotValue()
    {
        // Arrange — sentinel-marker hint value (closed-vocab in production; sentinel here)
        const string sentinelIntent = "MARKER-INTENT-SENTINEL-9999";
        SetupSearchReturnsCandidates(score: 0.9);
        var dispatcher = CreateDispatcher();
        var attachment = new ChatMessageAttachment(
            Filename: "x.pdf",
            ContentType: "application/pdf",
            TextContent: "Content x...");

        // Act
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None,
            intentHint: sentinelIntent);

        // Assert — summary log line carries the provided flag
        var summary = _capturedLogMessages.FirstOrDefault(
            m => m.Contains("PlaybookDispatcher Phase B:"));
        summary.Should().NotBeNull("Phase B summary log must be emitted");
        summary!.Should().Contain("intentHintProvided=True",
            "FR-20 / ADR-015: provided-flag MUST appear in tier-1 telemetry");

        // Assert — the hint VALUE must NOT leak into any log line
        var allLogs = string.Join("\n", _capturedLogMessages);
        allLogs.Should().NotContain(sentinelIntent,
            "ADR-015 tier-1 (conservative): intent hint value MUST NOT appear in logs");
    }

    [Fact]
    public async Task RunPhaseBVectorMatchAsync_LogsIntentHintProvidedFalse_WhenAbsent()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.9);
        var dispatcher = CreateDispatcher();
        var attachment = new ChatMessageAttachment(
            Filename: "x.pdf",
            ContentType: "application/pdf",
            TextContent: "Content x...");

        // Act — no intent hint
        await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);

        // Assert
        var summary = _capturedLogMessages.FirstOrDefault(
            m => m.Contains("PlaybookDispatcher Phase B:"));
        summary.Should().NotBeNull();
        summary!.Should().Contain("intentHintProvided=False");
    }

    // ══════════════════════════════════════════════════════════
    // Mock setup helpers
    // ══════════════════════════════════════════════════════════

    private void SetupSearchReturnsCandidates(double score, int candidateCount = 2)
    {
        var docs = Enumerable.Range(0, candidateCount).Select(i =>
            new PlaybookEmbeddingDocument
            {
                Id = i == 0 ? PlaybookA : (i == 1 ? PlaybookB : Guid.NewGuid().ToString()),
                PlaybookId = i == 0 ? PlaybookA : (i == 1 ? PlaybookB : Guid.NewGuid().ToString()),
                PlaybookName = $"playbook-{i}",
                Description = $"Test playbook number {i}.",
                TriggerPhrases = [$"trigger-{i}"],
                RecordType = "sprk_matter",
                EntityType = "matter",
                Tags = ["chat"],
                DocumentTypes = ["NDA", "contract"]
            }).ToList();

        var searchResults = docs
            .Select(d => SearchModelFactory.SearchResult(d, score, null))
            .ToList();

        var resultsResponse = SearchModelFactory.SearchResults(
            values: searchResults,
            totalCount: searchResults.Count,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _searchClientMock
            .Setup(c => c.SearchAsync<PlaybookEmbeddingDocument>(
                It.IsAny<string?>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(resultsResponse, null!));
    }

    private static ChatSessionFile MakeSessionFile(
        string fileId,
        string filename,
        string? classifiedDocType)
    {
        return new ChatSessionFile(
            FileId: fileId,
            FileName: filename,
            ContentType: "application/pdf",
            SizeBytes: 1024,
            SearchDocumentIdsCsv: $"{fileId}-chunk-0",
            UploadedAt: DateTimeOffset.UtcNow)
        {
            ClassifiedDocType = classifiedDocType,
        };
    }
}
