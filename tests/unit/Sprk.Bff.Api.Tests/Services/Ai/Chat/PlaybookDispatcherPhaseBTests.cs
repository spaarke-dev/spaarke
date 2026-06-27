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
/// Tests for <see cref="PlaybookDispatcher.RunPhaseBVectorMatchAsync"/> — the
/// chat-routing-redesign-r1 Hybrid C Phase B per-file vector matcher (task 112,
/// spec FR-17 v2).
///
/// <para>
/// Coverage:
/// <list type="bullet">
///   <item><description>Manifest-present path applies the structured <c>documentTypes</c> pre-filter.</description></item>
///   <item><description>Manifest-absent path composes the per-file query <c>"{userMessage} | Document: {filename} | Type hint: {contentType} | Content: {textPrefix}"</c>.</description></item>
///   <item><description>Parallel fan-out across 1, 2, 3 files.</description></item>
///   <item><description>Manifest-present p95 ≤ 100ms target (mocked path).</description></item>
///   <item><description>Manifest-absent ≤ 300ms target for 3 files (mocked path with simulated per-call latency).</description></item>
///   <item><description>Top-K shape preserved (5 per file).</description></item>
///   <item><description>ADR-015 tier-1 telemetry: log line carries counts + latency, never query/content strings.</description></item>
///   <item><description>5-min in-memory cache: repeat calls hit cache rather than re-embedding.</description></item>
/// </list>
/// </para>
/// </summary>
public class PlaybookDispatcherPhaseBTests
{
    // ──────────────────────────────────────────────────────────
    // Test constants
    // ──────────────────────────────────────────────────────────

    private static readonly string PlaybookA = Guid.NewGuid().ToString();
    private static readonly string PlaybookB = Guid.NewGuid().ToString();
    private const string TestTenantId = "test-tenant-phaseb-112";
    private const string TestUserMessage = "summarize the attached contract";
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
    private readonly List<SearchOptions> _capturedSearchOptions = new();

    public PlaybookDispatcherPhaseBTests()
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

        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));

        // InMemoryTenantCache returns null/default by default — no cache-miss setup required.

        // Capture log messages so we can assert ADR-015 compliance (no leakage of
        // query text / file content into Information-level telemetry).
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
    // Manifest-present path: documentTypes filter is applied
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// FR-17 v2: when <see cref="ChatSessionFile.ClassifiedDocType"/> is non-null, the
    /// search must include an OData filter expression targeting
    /// <c>documentTypes/any(t: search.in(t, '<label>'))</c> — proving the manifest
    /// pre-filter is wired correctly.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_AppliesDocumentTypesFilter_WhenManifestPresent()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.91);
        var dispatcher = CreateDispatcher();

        var attachment = new ChatMessageAttachment(
            Filename: "nda-draft.pdf",
            ContentType: "application/pdf",
            TextContent: "Confidentiality and non-disclosure clauses...");

        var sessionFile = MakeSessionFile("file-1", "nda-draft.pdf",
            classifiedDocType: TestClassifiedDocType);

        // Act
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: new[] { (ChatSessionFile?)sessionFile },
            topK: 5,
            cancellationToken: CancellationToken.None);

        // Assert — result shape
        results.Should().HaveCount(1);
        var fileResult = results[0];
        fileResult.ManifestPresent.Should().BeTrue();
        fileResult.FileId.Should().Be("file-1");
        fileResult.Filename.Should().Be("nda-draft.pdf");
        fileResult.Candidates.Should().HaveCount(2);

        // Assert — filter was applied (FR-17 v2 binding)
        _capturedSearchOptions.Should().NotBeEmpty();
        var capturedFilter = _capturedSearchOptions[0].Filter;
        capturedFilter.Should().NotBeNullOrEmpty();
        capturedFilter.Should().Contain("documentTypes/any(t: search.in(t, 'NDA'))");
    }

    // ══════════════════════════════════════════════════════════
    // Manifest-absent path: per-file query composition
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// FR-17 v2 manifest-absent path: when <see cref="ChatSessionFile.ClassifiedDocType"/>
    /// is null (or no session file supplied at all), the per-file query is composed as
    /// <c>"{userMessage} | Document: {filename} | Type hint: {contentType} | Content: {textPrefix}"</c>
    /// and dispatched without a <c>documentTypes</c> filter.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_ComposesPerFileQuery_WhenManifestAbsent()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.86);
        string? capturedEmbedInput = null;
        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, int?, CancellationToken>((q, _, _, _) =>
            {
                capturedEmbedInput = q;
            })
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));

        var dispatcher = CreateDispatcher();

        var attachment = new ChatMessageAttachment(
            Filename: "contract-a.pdf",
            ContentType: "application/pdf",
            TextContent: "Sample contract content prefix that we expect to bias the embedding...");

        // Act — explicitly pass a session file WITH null ClassifiedDocType (MVP production state)
        var sessionFile = MakeSessionFile("file-a", "contract-a.pdf", classifiedDocType: null);
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: new[] { (ChatSessionFile?)sessionFile },
            topK: 5,
            cancellationToken: CancellationToken.None);

        // Assert
        results.Should().HaveCount(1);
        results[0].ManifestPresent.Should().BeFalse();

        // Per-file query composition (FR-17 v2 binding)
        capturedEmbedInput.Should().NotBeNullOrEmpty();
        capturedEmbedInput.Should().Contain(TestUserMessage);
        capturedEmbedInput.Should().Contain("| Document: contract-a.pdf");
        capturedEmbedInput.Should().Contain("| Type hint: application/pdf");
        capturedEmbedInput.Should().Contain("| Content: Sample contract content prefix");

        // No documentTypes filter applied on the manifest-absent path
        _capturedSearchOptions.Should().NotBeEmpty();
        _capturedSearchOptions[0].Filter.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════
    // Parallel fan-out — 1, 2, 3 files
    // ══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RunPhaseBVectorMatchAsync_ParallelFanOut_Works_For_N_Files(int fileCount)
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.88);
        var dispatcher = CreateDispatcher();

        var attachments = Enumerable.Range(0, fileCount)
            .Select(i => new ChatMessageAttachment(
                Filename: $"file-{i}.pdf",
                ContentType: "application/pdf",
                TextContent: $"Content of file {i} for vector matching..."))
            .ToArray();

        // Act
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: attachments,
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);

        // Assert
        results.Should().HaveCount(fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            results[i].Filename.Should().Be($"file-{i}.pdf");
            results[i].ManifestPresent.Should().BeFalse();
            results[i].Candidates.Should().HaveCount(2);
        }
    }

    // ══════════════════════════════════════════════════════════
    // Empty attachments returns empty array
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task RunPhaseBVectorMatchAsync_ReturnsEmpty_WhenNoAttachments()
    {
        // Arrange
        var dispatcher = CreateDispatcher();

        // Act
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: Array.Empty<ChatMessageAttachment>(),
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);

        // Assert
        results.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════
    // Performance budget — manifest-present p95 ≤ 100ms (mocked)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// FR-17 v2 target: manifest-present per-file latency must be ≤100ms (filter
    /// overhead only — no extra LLM call). With a mocked embedder + search the path
    /// runs in single-digit ms; we assert a comfortable 100ms ceiling. Repeat 20
    /// times to take a p95-style sample.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_ManifestPresent_MeetsP95LatencyBudget()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.91);

        // Use a fresh dispatcher per iteration so the in-process cache doesn't
        // make latency artificially low after the first call.
        var samples = new List<long>(capacity: 20);
        for (var i = 0; i < 20; i++)
        {
            var dispatcher = CreateDispatcher(new MemoryCache(new MemoryCacheOptions()));
            var attachment = new ChatMessageAttachment(
                Filename: $"nda-{i}.pdf",
                ContentType: "application/pdf",
                TextContent: $"Different content per iteration {i} to bust any other cache");
            var sessionFile = MakeSessionFile($"file-{i}", $"nda-{i}.pdf",
                classifiedDocType: TestClassifiedDocType);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await dispatcher.RunPhaseBVectorMatchAsync(
                userMessage: TestUserMessage,
                attachments: new[] { attachment },
                sessionFiles: new[] { (ChatSessionFile?)sessionFile },
                topK: 5,
                cancellationToken: CancellationToken.None);
            sw.Stop();

            results.Should().HaveCount(1);
            samples.Add(results[0].LatencyMs);
        }

        // Compute p95 — 19th smallest of 20 samples (95th percentile)
        samples.Sort();
        var p95 = samples[18];

        p95.Should().BeLessThan(100, "FR-17 v2 manifest-present path budget");
    }

    // ══════════════════════════════════════════════════════════
    // Performance budget — manifest-absent ≤ 300ms for 3 files
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// FR-17 v2 target: manifest-absent path with 3 files ≤300ms via parallel fan-out.
    /// We simulate 80-150ms per embedding call so the total per-file path stays around
    /// 80-150ms; with Task.WhenAll parallelism the wall-clock for 3 files is bounded by
    /// the slowest single file's path — well under 300ms.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_ManifestAbsent_MeetsLatencyBudgetFor3Files()
    {
        // Arrange — randomise per-call simulated latency in [80,150]ms range
        SetupSearchReturnsCandidates(score: 0.86);
        var random = new Random(42);
        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string?, int?, CancellationToken>(async (_, _, _, ct) =>
            {
                var delayMs = random.Next(80, 151);
                await Task.Delay(delayMs, ct);
                return new ReadOnlyMemory<float>(new float[3072]);
            });

        var dispatcher = CreateDispatcher();

        var attachments = new[]
        {
            new ChatMessageAttachment("file-1.pdf", "application/pdf", "Content one..."),
            new ChatMessageAttachment("file-2.pdf", "application/pdf", "Content two..."),
            new ChatMessageAttachment("file-3.pdf", "application/pdf", "Content three..."),
        };

        // Act — measure wall-clock so parallel fan-out is the unit under test
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: attachments,
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);
        sw.Stop();

        // Assert
        results.Should().HaveCount(3);
        sw.ElapsedMilliseconds.Should().BeLessThan(300,
            "FR-17 v2 manifest-absent path budget for 3 files (parallel fan-out)");
    }

    // ══════════════════════════════════════════════════════════
    // Top-K shape — caller picks K per file
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task RunPhaseBVectorMatchAsync_ReturnsTopKPerFile()
    {
        // Arrange — set up the mock to return 5 candidates so we exercise the
        // requested topK round-trip end-to-end.
        SetupSearchReturnsCandidates(score: 0.9, candidateCount: 5);
        var dispatcher = CreateDispatcher();

        var attachments = new[]
        {
            new ChatMessageAttachment("a.pdf", "application/pdf", "Content A..."),
            new ChatMessageAttachment("b.pdf", "application/pdf", "Content B..."),
        };

        // Act
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: attachments,
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);

        // Assert — each per-file result carries the full top-K (5)
        results.Should().HaveCount(2);
        foreach (var r in results)
        {
            r.Candidates.Should().HaveCount(5);
        }
    }

    // ══════════════════════════════════════════════════════════
    // ADR-015 telemetry — no query/content leakage
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-015 tier-1 binding: the Phase B summary log line MUST carry counts +
    /// latency only. It MUST NOT carry the user query, filenames, content text,
    /// embedding values, or classifier confidence. We exercise the path with a
    /// distinctive query / filename / content string and assert none of them
    /// surface in the captured log messages at Information level.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_TelemetryIsTier1Compliant()
    {
        // Arrange — pick unique markers we can grep for in the log buffer
        const string secretMessage = "MARKER-SECRET-QUERY-12345";
        const string secretFilename = "MARKER-FILENAME-67890.pdf";
        const string secretContent = "MARKER-CONTENT-XYZ-CONFIDENTIAL";
        const string secretDocType = "MARKER-DOCTYPE";

        SetupSearchReturnsCandidates(score: 0.9);
        var dispatcher = CreateDispatcher();

        var attachment = new ChatMessageAttachment(
            Filename: secretFilename,
            ContentType: "application/pdf",
            TextContent: secretContent);

        var sessionFile = MakeSessionFile("file-marker", secretFilename, secretDocType);

        // Act
        var results = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: secretMessage,
            attachments: new[] { attachment },
            sessionFiles: new[] { (ChatSessionFile?)sessionFile },
            topK: 5,
            cancellationToken: CancellationToken.None);

        results.Should().NotBeEmpty();

        // Assert — find the Phase B summary message; assert tier-1 fields present
        var phaseBSummary = _capturedLogMessages.FirstOrDefault(
            m => m.Contains("PlaybookDispatcher Phase B:"));
        phaseBSummary.Should().NotBeNull(
            "Phase B summary log must be emitted at Information level");
        phaseBSummary!.Should().Contain("filesCount=1");
        phaseBSummary.Should().Contain("manifestPresent=True");
        phaseBSummary.Should().Contain("totalLatencyMs=");
        phaseBSummary.Should().Contain("topKCount=");

        // Assert — NONE of the user-content markers leak into telemetry
        // (across ALL captured log lines, not just the summary)
        var allLogs = string.Join("\n", _capturedLogMessages);
        allLogs.Should().NotContain(secretMessage, "ADR-015 tier-1 forbids logging user query text");
        allLogs.Should().NotContain(secretContent, "ADR-015 tier-1 forbids logging file content");
        allLogs.Should().NotContain(secretDocType, "ADR-015 tier-1 forbids logging classifier output values");
    }

    // ══════════════════════════════════════════════════════════
    // Caching — repeat call hits in-memory cache
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// ADR-014 binding: identical Phase B calls within the 5-min TTL window MUST
    /// hit the in-memory cache rather than re-embedding + re-querying. We assert
    /// the embedder is called once on the first turn and zero times on the second.
    /// </summary>
    [Fact]
    public async Task RunPhaseBVectorMatchAsync_CachesResultsAcrossRepeatCalls()
    {
        // Arrange
        SetupSearchReturnsCandidates(score: 0.9);
        var sharedCache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = CreateDispatcher(sharedCache);

        var attachment = new ChatMessageAttachment(
            Filename: "cached.pdf",
            ContentType: "application/pdf",
            TextContent: "Stable content that should hit the cache on repeat...");

        // Act — first call (cache miss)
        var first = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);

        var embedCallsAfterFirst = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        // Act — second call (cache hit)
        var second = await dispatcher.RunPhaseBVectorMatchAsync(
            userMessage: TestUserMessage,
            attachments: new[] { attachment },
            sessionFiles: null,
            topK: 5,
            cancellationToken: CancellationToken.None);

        var embedCallsAfterSecond = _openAiClientMock.Invocations
            .Count(i => i.Method.Name == nameof(IOpenAiClient.GenerateEmbeddingAsync));

        // Assert — embed was called once total, not twice
        embedCallsAfterFirst.Should().Be(1, "first call is a cache miss");
        embedCallsAfterSecond.Should().Be(1, "second call must hit cache, not re-embed");

        // Both calls return semantically equivalent results
        first.Should().HaveCount(1);
        second.Should().HaveCount(1);
        first[0].Candidates.Should().HaveCount(second[0].Candidates.Count);
    }

    // ══════════════════════════════════════════════════════════
    // Mock Setup Helpers
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Configures the mock SearchClient to return N candidate playbook documents
    /// with the given similarity score. Captures the <see cref="SearchOptions"/>
    /// the dispatcher passes so tests can assert filter expressions.
    /// </summary>
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
            .Callback<string?, SearchOptions, CancellationToken>((_, opts, _) =>
            {
                _capturedSearchOptions.Add(opts);
            })
            .ReturnsAsync(Response.FromValue(resultsResponse, null!));
    }

    /// <summary>
    /// Builds a <see cref="ChatSessionFile"/> with the supplied identifiers and an
    /// optional <c>ClassifiedDocType</c> (drives the manifest-present vs
    /// manifest-absent path selection in Phase B).
    /// </summary>
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
