using System.Text;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Ai;

/// <summary>
/// <c>tests/integration/seam/**</c> — vertical-slice seam for spaarkeai-compose-r8 FR-B02 (task 061):
/// a session whose hot-index chunks were evicted at 24h must recall from its files on day 60 with results
/// IDENTICAL to day 1, rebuilt lazily from the durable byte copy.
/// </summary>
/// <remarks>
/// <para>
/// <b>What "identical" means here, and why it is asserted that way.</b> The day-1 and day-60 tool payloads
/// are compared as RAW JSON — <c>ToolResult.Data.GetRawText()</c>. Not "non-empty", not "contains the
/// word", not a citation count: the exact bytes the LLM would receive. A recall that came back with the
/// right content in a different order, or with one chunk missing, or with a different citation page,
/// would be a silent degradation of the answer a lawyer reads, and every weaker assertion passes it.
/// </para>
/// <para>
/// <b>The slice.</b> Production types throughout: <c>RagIndexingPipeline</c> (the same
/// <c>IndexSessionFileAsync</c> call, with the same <c>documentId</c>/<c>speFileId</c> arguments, that
/// <c>ChatDocumentEndpoints</c> makes at upload), <see cref="SessionFileBlobStore"/>,
/// <see cref="SessionFileRehydrationService"/> and <see cref="RecallSessionFileHandler"/>. Two boundaries
/// are faked, both external: Azure AI Search (an in-memory index this session cannot provision) and Azure
/// Blob (<see cref="InMemorySessionFileBlobGateway"/>). Text extraction is faked in a way that MATTERS —
/// the stub decodes the actual stream it is handed, so the recovered text is a genuine function of the
/// bytes the store returned. A rehydration that read the wrong blob produces different text, different
/// chunks and a failing equality assertion, rather than a stub answer that hides the mistake.
/// </para>
/// <para>
/// <b>Observed to fail before it passed — and what was NOT observed.</b> Two controls were run against
/// deliberately broken code, and both are recorded here with what they actually proved:
/// <list type="number">
///   <item>With the FR-B02 wiring removed from the handler (<c>sessionFileRehydration: null</c>, i.e.
///     the pre-task-061 world), <see cref="DaySixty_AfterTheHotIndexWasEvicted_RecallsIdenticallyToDayOne"/>
///     failed on the raw JSON: day 1 returned an 885-character payload with three citations, day 60
///     returned <c>{"content":"","citations":[],…}</c>. That empty body IS the R7 UAT defect.</item>
///   <item>With the existence probe short-circuited (rehydrate on ANY empty file-scoped result),
///     <see cref="Recall_WhenTheSessionIsStillIndexed_DoesNotRehydrate"/> failed at 1 extraction instead
///     of 0. <b>An earlier draft of that test did not fail under the same break</b> — it used a file with
///     no durable copy, so the rehydration bailed at the blob read and never reached the extractor. The
///     test now uses a file that has BOTH a durable copy and live index chunks, which is the only shape
///     in which the assertion means anything. The vacuous draft was found by running the control, not by
///     reading the test.</item>
/// </list>
/// <b>Not observed:</b> a production-side break for
/// <see cref="Rehydration_UnderAnotherTenant_CannotReachTheOwningTenantsDurableCopy"/>. The tenant
/// argument is the ONLY tenant in scope inside <c>RehydrateAsync</c>, so there is no careless-refactor
/// shape to inject without hard-coding a test constant into production code. The underlying property —
/// that a tenant-prefixed blob name is unreachable from another tenant — was observed failing under a
/// real break by task 060 (<c>SessionFileBlobStoreTenantIsolationTests</c>, four failures including a
/// genuine cross-tenant read); what this test adds on top is that the rehydration passes the CALLING
/// tenant through to both the read and the re-index, asserted with a positive control first.
/// </para>
/// </remarks>
public sealed class SessionFileLazyReindexSeamTests
{
    private const string IndexName = "test-session-files-index";
    private const string TenantA = "00000000-0000-0000-0000-00000000aaaa";
    private const string TenantB = "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb";

    /// <summary>Long enough to chunk into several pieces, so "identical" is a statement about more than one chunk.</summary>
    private const string DocumentText =
        "SECTION 4.2 INDEMNIFICATION. The Supplier shall indemnify the Customer against all losses. " +
        "SECTION 4.3 LIMITATION OF LIABILITY. Aggregate liability shall not exceed USD 250,000. " +
        "SECTION 4.4 TERM. This agreement commences on 2026-01-15 and continues for thirty-six months. " +
        "SECTION 4.5 GOVERNING LAW. This agreement is governed by the laws of the State of Delaware.";

    private readonly Guid _sessionGuid = Guid.NewGuid();
    private readonly string _fileId = Guid.NewGuid().ToString("N");

    private readonly FakeSessionFilesIndex _hotIndex = new();
    private readonly InMemorySessionFileBlobGateway _durableBlobs = new();
    private readonly SessionFileBlobStore _durableStore;
    private readonly CountingTextExtractor _textExtractor = new();
    private readonly RagIndexingPipeline _indexingPipeline;
    private readonly SessionFileRehydrationService _rehydration;

    private string SessionId => _sessionGuid.ToString("N");

    public SessionFileLazyReindexSeamTests()
    {
        _durableStore = new SessionFileBlobStore(_durableBlobs, NullLogger<SessionFileBlobStore>.Instance);
        _indexingPipeline = BuildRealIndexingPipeline();
        _rehydration = new SessionFileRehydrationService(
            durableStore: _durableStore,
            textExtractor: _textExtractor,
            indexingPipeline: _indexingPipeline,
            logger: NullLogger<SessionFileRehydrationService>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-B02 — the headline: day 60 recalls exactly what day 1 recalled.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DaySixty_AfterTheHotIndexWasEvicted_RecallsIdenticallyToDayOne()
    {
        var file = await UploadAsync(TenantA, DocumentText);
        var session = BuildSession(TenantA, file);

        var dayOne = await RecallAsync(TenantA, session, file);
        dayOne.Success.Should().BeTrue();
        var dayOnePayload = dayOne.Data!.Value.GetRawText();
        dayOnePayload.Should().Contain("INDEMNIFICATION",
            "positive control: day 1 must actually recall content, or the equality below means nothing");

        // Day 60. SessionFilesCleanupJob swept this session's chunks out of the hot index when its Redis
        // key expired on the 24h sliding TTL; the Cosmos manifest (90 days) still names them, and the
        // durable byte copy is untouched (FR-B03).
        _hotIndex.EvictSession(TenantA, SessionId);
        _hotIndex.DocumentCount.Should().Be(0, "positive control: the eviction really emptied the index");

        var daySixty = await RecallAsync(TenantA, session, file);

        daySixty.Success.Should().BeTrue();
        daySixty.Data!.Value.GetRawText().Should().Be(dayOnePayload,
            "a conversation still in the 90-day History window must answer from its files on day 60 " +
            "exactly as it did on day 1. Anything short of byte-equality here is a silent degradation " +
            "of the text a lawyer reads — the failure mode that made 'no longer available' the R7 UAT " +
            "defect this track exists to close");
    }

    /// <summary>
    /// The mechanism behind the equality above: the rebuild reproduces the SAME chunk ids the manifest
    /// already names, so <c>SearchDocumentIdsCsv</c> keeps matching and nothing has to be migrated.
    /// </summary>
    [Fact]
    public async Task Rehydration_ReproducesTheManifestsChunkIdsExactly()
    {
        var file = await UploadAsync(TenantA, DocumentText);
        var manifestIds = file.SearchDocumentIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        manifestIds.Should().NotBeEmpty("positive control: the upload recorded chunk ids");

        _hotIndex.EvictSession(TenantA, SessionId);

        var result = await _rehydration.RehydrateAsync(TenantA, SessionId, file, CancellationToken.None);

        result.Outcome.Should().Be(SessionFileRehydrationOutcome.Reindexed);
        _hotIndex.IdsFor(TenantA, SessionId).Should().BeEquivalentTo(manifestIds,
            "the rebuilt chunk ids must equal the ones the persisted manifest points at. If they drifted, " +
            "every recall consumer's post-filter on SearchDocumentIdsCsv would silently drop the content " +
            "that was just restored");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADR-014 / ADR-015 — a rehydration cannot read another tenant's durable copy.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rehydration_UnderAnotherTenant_CannotReachTheOwningTenantsDurableCopy()
    {
        const string privileged =
            "PRIVILEGED — tenant A settlement figures. Must never reach tenant B. " +
            "Mediated settlement authority is USD 4,750,000 with a walk-away floor of USD 3,100,000. " +
            "Opposing counsel has not been told either number.";
        var file = await UploadAsync(TenantA, privileged);
        _hotIndex.EvictSession(TenantA, SessionId);

        // Positive control: the owning tenant CAN recover it. Without this, the negative below passes
        // whenever nothing was stored — the vacuous shape task 060 had to fix in its own suite.
        var owning = await _rehydration.RehydrateAsync(TenantA, SessionId, file, CancellationToken.None);
        owning.Outcome.Should().Be(SessionFileRehydrationOutcome.Reindexed);
        owning.ExtractedText.Should().Be(privileged);

        _hotIndex.EvictSession(TenantA, SessionId);

        // Tenant B holds the exact session id and file id — a leaked identifier, not a guess.
        var crossTenant = await _rehydration.RehydrateAsync(TenantB, SessionId, file, CancellationToken.None);

        crossTenant.Outcome.Should().Be(SessionFileRehydrationOutcome.NoDurableCopy,
            "a rehydration performed under another tenant must be indistinguishable from 'no such file' " +
            "(ADR-014 / ADR-015) — knowing the identifiers must not be enough");
        crossTenant.ExtractedText.Should().BeNull();
        _hotIndex.IdsFor(TenantB, SessionId).Should().BeEmpty(
            "and it must not have written anything into tenant B's partition either");

        // The owning tenant's bytes are untouched by the failed cross-tenant attempt.
        (await _durableStore.ReadAsync(TenantA, SessionId, _fileId))!.Content.ToString()
            .Should().Be(privileged);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Laziness — the rebuild is triggered by eviction, not by an empty answer.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A zero-result recall is normally just "the query matched nothing". Rehydrating on that would fire a
    /// Document Intelligence re-extraction on every unlucky question — a cost regression wearing the
    /// costume of a fix. The trigger is the EXISTENCE probe, not the empty result.
    /// </summary>
    /// <remarks>
    /// The file under test HAS a durable copy and IS indexed. Only the query fails to match. That
    /// combination is what makes this test load-bearing: with the existence probe removed, the durable
    /// read succeeds, the extractor runs, and the assertion below fails at 1 instead of 0. (Verified —
    /// an earlier draft used a file with no durable copy, which passed with the probe removed and
    /// therefore proved nothing.)
    /// </remarks>
    [Fact]
    public async Task Recall_WhenTheSessionIsStillIndexed_DoesNotRehydrate()
    {
        var file = await UploadAsync(TenantA, DocumentText);
        var session = BuildSession(TenantA, file);

        _hotIndex.DocumentCount.Should().BeGreaterThan(0,
            "positive control: the session's chunks are still in the hot index");
        _durableBlobs.Count.Should().Be(1,
            "positive control: a durable copy EXISTS, so a rehydration would genuinely re-extract — " +
            "without this the assertion below could pass simply because there was nothing to recover");

        var extractionsBefore = _textExtractor.Calls;

        var result = await RecallAsync(TenantA, session, file, query: "xyzzy-appears-in-no-chunk");

        result.Success.Should().BeTrue();
        result.Data!.Value.GetRawText().Should().NotContain("INDEMNIFICATION",
            "positive control: the query really did match nothing");
        _textExtractor.Calls.Should().Be(extractionsBefore,
            "the session's chunks are still in the hot index, so this empty result is a query miss and " +
            "NOT the 24h-eviction state. Re-extracting here would put a Document Intelligence call behind " +
            "every question that happens to match nothing");
    }

    [Fact]
    public async Task Recall_WhenNoDurableCopyExists_ReturnsTheHonestEmptyResultRatherThanInventingOne()
    {
        // A file uploaded before FR-B01 shipped: manifest + index, but no durable bytes.
        var file = await UploadAsync(TenantA, DocumentText, writeDurableCopy: false);
        var session = BuildSession(TenantA, file);
        _hotIndex.EvictSession(TenantA, SessionId);

        var result = await RecallAsync(TenantA, session, file);

        result.Success.Should().BeTrue("recall degrades, it does not throw (architecture §9.2)");
        result.Data!.Value.GetRawText().Should().NotContain("INDEMNIFICATION",
            "with no durable copy there is nothing to restore, and the handler must not fabricate content");
        _durableBlobs.Count.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Harness — production types wired the way ChatDocumentEndpoints wires them.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reproduces the upload path's two durable effects: the byte copy (step 9c) and the session-files
    /// index write (step 10a), using the SAME <c>IndexSessionFileAsync</c> arguments the endpoint passes —
    /// <c>documentId</c> and <c>speFileId</c> both set to the manifest's FileId. That argument choice is
    /// what makes the chunk ids reproducible, so it is reproduced here rather than paraphrased.
    /// </summary>
    private async Task<ChatSessionFile> UploadAsync(
        string tenantId, string text, bool writeDurableCopy = true)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        if (writeDurableCopy)
        {
            (await _durableStore.WriteAsync(
                    tenantId, SessionId, _fileId, BinaryData.FromBytes(bytes), "application/pdf"))
                .Should().Be(SessionFileStoreOutcome.Written);
        }

        var indexed = await _indexingPipeline.IndexSessionFileAsync(
            document: new ParsedDocument { Text = text, Pages = 0, ExtractedAt = DateTimeOffset.UtcNow },
            documentId: _fileId,
            tenantId: tenantId,
            sessionId: SessionId,
            fileName: "agreement.pdf",
            speFileId: _fileId,
            cancellationToken: CancellationToken.None);

        indexed.KnowledgeChunksIndexed.Should().BeGreaterThan(1,
            "the fixture text must chunk into more than one piece, or 'identical results' is a claim " +
            "about a single chunk and proves much less than it appears to");

        var chunkIds = string.Join(",",
            Enumerable.Range(0, indexed.KnowledgeChunksIndexed).Select(i => $"{_fileId}_s_{i}"));

        return new ChatSessionFile(
            FileId: _fileId,
            FileName: "agreement.pdf",
            ContentType: "application/pdf",
            SizeBytes: bytes.Length,
            SearchDocumentIdsCsv: chunkIds,
            UploadedAt: DateTimeOffset.UtcNow);
    }

    private ChatSession BuildSession(string tenantId, params ChatSessionFile[] files)
        => new(
            SessionId: SessionId,
            TenantId: tenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-60),
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: files) { OwnerOid = TestSessionOwner.Oid };

    private async Task<ToolResult> RecallAsync(
        string tenantId, ChatSession session, ChatSessionFile file, string query = "SECTION")
    {
        var handler = BuildHandler(tenantId, session);

        var argsJson = $$"""
            {
              "fileId": "{{file.FileId}}",
              "purpose": "answer_question",
              "query": "{{query}}",
              "scope": "relevant_sections",
              "requireCitations": false
            }
            """;

        return await handler.ExecuteChatAsync(
            new ChatInvocationContext
            {
                ChatSessionId = _sessionGuid,
                TenantId = tenantId,
                DecisionId = Guid.NewGuid(),
                ToolArgumentsJson = argsJson
            },
            new AnalysisTool { Id = Guid.NewGuid(), Name = "recall_session_file", Type = ToolType.Custom },
            CancellationToken.None);
    }

    private RecallSessionFileHandler BuildHandler(string tenantId, ChatSession session)
    {
        var sessionManager = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!)
        { CallBase = false };
        sessionManager
            .Setup(m => m.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        return new RecallSessionFileHandler(
            ragService: BuildIndexBackedRagService(),
            sessionManager: sessionManager.Object,
            timeProvider: TimeProvider.System,
            logger: NullLogger<RecallSessionFileHandler>.Instance,
            contextEventEmitter: null,
            recentlyDiscussedTracker: null,
            sessionFileRehydration: _rehydration);
    }

    /// <summary>
    /// <see cref="IRagService"/> reading from <see cref="FakeSessionFilesIndex"/>, applying the tenant +
    /// session filter <c>RagService</c> always ANDs onto a session-scoped query. Relevance is deliberately
    /// NOT modelled: this seam is about whether the CONTENT is reachable, and a fake ranker would make
    /// "identical results" an assertion about the fake.
    /// </summary>
    private IRagService BuildIndexBackedRagService()
    {
        var rag = new Mock<IRagService>();
        rag.Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, RagSearchOptions options, CancellationToken _) =>
            {
                var docs = _hotIndex
                    .Query(options.TenantId ?? string.Empty, options.SessionId ?? string.Empty)
                    // RagService issues a match-all "*" when UseKeywordSearch is off (the existence
                    // probe's shape) and the caller's text otherwise. Modelling that distinction is
                    // what lets a test tell "the query matched nothing" apart from "the index is empty"
                    // — the two states FR-B02's trigger has to separate.
                    .Where(d => !options.UseKeywordSearch
                                || d.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(options.TopK)
                    .Select(d => new RagSearchResult
                    {
                        Id = d.Id!,
                        DocumentId = d.DocumentId,
                        DocumentName = d.FileName,
                        Content = d.Content,
                        KnowledgeSourceName = d.FileName,
                        Score = 0.9,
                        ChunkIndex = d.ChunkIndex ?? 0,
                        ChunkCount = d.ChunkCount,
                    })
                    .ToList();

                return new RagSearchResponse { Query = query, Results = docs, TotalCount = docs.Count };
            });
        return rag.Object;
    }

    /// <summary>The real pipeline, over an in-memory index and a deterministic chunker.</summary>
    private RagIndexingPipeline BuildRealIndexingPipeline()
    {
        var searchClient = new Mock<SearchClient>();

        // Delete-stale step: report nothing to delete. The in-memory index upserts by id, so a rebuild
        // replaces in place — the same net effect MergeOrUploadDocuments has in Azure.
        searchClient
            .Setup(c => c.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.SearchResults(
                    values: new List<SearchResult<KnowledgeDocument>>(),
                    totalCount: 0, facets: null, coverage: null, rawResponse: null!),
                null!));

        searchClient
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<KnowledgeDocument> docs, IndexDocumentsOptions _, CancellationToken __) =>
            {
                var stored = docs.ToList();
                _hotIndex.Upsert(stored);
                return Response.FromValue(
                    SearchModelFactory.IndexDocumentsResult(
                        stored.Select(d => SearchModelFactory.IndexingResult(d.Id, null, true, 201)).ToList()),
                    null!);
            });

        var searchIndexClient = new Mock<SearchIndexClient>();
        searchIndexClient.Setup(c => c.GetSearchClient(It.IsAny<string>())).Returns(searchClient.Object);

        var chunking = new Mock<ITextChunkingService>();
        chunking
            .Setup(c => c.ChunkTextAsync(It.IsAny<string>(), It.IsAny<ChunkingOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, ChunkingOptions _, CancellationToken __) => SplitDeterministically(text));

        var openAi = new Mock<IOpenAiClient>();
        openAi
            .Setup(o => o.GenerateEmbeddingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));

        return new RagIndexingPipeline(
            chunking.Object,
            Mock.Of<IRagService>(),
            searchIndexClient.Object,
            openAi.Object,
            Options.Create(new AiSearchOptions { SessionFilesIndexName = IndexName }),
            NullLogger<RagIndexingPipeline>.Instance);
    }

    /// <summary>
    /// A pure function of the input text. That is the property under test: the same bytes must yield the
    /// same chunks, so a rebuild reproduces the same chunk ids. A random or time-dependent chunker would
    /// make the identity assertion meaningless.
    /// </summary>
    private static List<TextChunk> SplitDeterministically(string text)
    {
        const int size = 100;
        var chunks = new List<TextChunk>();
        for (var offset = 0; offset < text.Length; offset += size)
        {
            var length = Math.Min(size, text.Length - offset);
            chunks.Add(new TextChunk
            {
                Content = text.Substring(offset, length),
                Index = chunks.Count,
                StartPosition = offset,
                EndPosition = offset + length,
            });
        }
        return chunks;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Doubles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// In-memory stand-in for the <c>spaarke-session-files</c> Azure AI Search index. Upserts by document
    /// id and filters by (tenantId, sessionId) — the two predicates the production filter always carries.
    /// <see cref="EvictSession"/> is what <c>SessionFilesCleanupJob.EvictSessionAsync</c> does.
    /// </summary>
    private sealed class FakeSessionFilesIndex
    {
        private readonly Dictionary<string, KnowledgeDocument> _docs = new(StringComparer.Ordinal);

        public int DocumentCount => _docs.Count;

        public void Upsert(IEnumerable<KnowledgeDocument> docs)
        {
            foreach (var doc in docs)
            {
                _docs[doc.Id] = doc;
            }
        }

        public IEnumerable<KnowledgeDocument> Query(string tenantId, string sessionId)
            => _docs.Values
                .Where(d => string.Equals(d.TenantId, tenantId, StringComparison.Ordinal)
                         && string.Equals(d.SessionId, sessionId, StringComparison.Ordinal))
                .OrderBy(d => d.ChunkIndex ?? 0);

        public IReadOnlyList<string> IdsFor(string tenantId, string sessionId)
            => Query(tenantId, sessionId).Select(d => d.Id!).ToList();

        public void EvictSession(string tenantId, string sessionId)
        {
            foreach (var id in IdsFor(tenantId, sessionId))
            {
                _docs.Remove(id);
            }
        }
    }

    /// <summary>
    /// Decodes the stream it is handed, so the "extracted text" is a real function of the bytes the
    /// durable store returned — a rehydration that read the wrong blob is then visible as different text
    /// rather than hidden behind a canned string. Also counts calls, which is how the laziness test proves
    /// a re-extraction did NOT happen.
    /// </summary>
    private sealed class CountingTextExtractor : ITextExtractor
    {
        public int Calls { get; private set; }

        public async Task<TextExtractionResult> ExtractAsync(
            Stream fileStream, string fileName, CancellationToken cancellationToken = default)
        {
            Calls++;
            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync(cancellationToken);
            return TextExtractionResult.Succeeded(text, TextExtractionMethod.Native);
        }

        public Task<TextExtractionResult> ExtractAsync(
            Stream fileStream, string fileName, string? driveId, string? itemId, string? etag,
            CancellationToken cancellationToken = default)
            => ExtractAsync(fileStream, fileName, cancellationToken);

        public bool IsSupported(string extension) => true;

        public ExtractionMethod? GetMethod(string extension) => null;
    }
}
