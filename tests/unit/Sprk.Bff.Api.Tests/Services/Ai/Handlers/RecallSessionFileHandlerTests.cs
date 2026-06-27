using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Ai.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="RecallSessionFileHandler"/> (chat-routing-redesign-r1 task 085 /
/// architecture §6.3 + §8.1 + §8.3).
/// </summary>
/// <remarks>
/// <para>
/// Coverage matrix:
/// </para>
/// <list type="bullet">
///   <item>6 purposes × 5 scopes shape coverage (Theory-driven matrix Smoke test plus targeted Facts)</item>
///   <item><c>requireCitations: true</c> is the default per architecture §8.3</item>
///   <item>scope=<c>summary</c> reads <see cref="ChatSessionFile.SummaryText"/> directly (no RagService call)</item>
///   <item>scope=<c>relevant_sections</c> calls <c>RagService.SearchAsync</c> with <c>SessionId</c> set
///         (RagService session-scoped branch resolves the <c>spaarke-session-files</c> index per
///         architecture §5.2.1)</item>
///   <item>scope=<c>full_text</c> truncation → returns <c>truncation_reason: "exceeded_8K"</c></item>
///   <item><c>fileId</c> not_found → structured payload (not exception)</item>
///   <item><c>IRecentlyDiscussedTracker.MarkAsync</c> called once per successful recall</item>
///   <item><c>IContextEventEmitter.ToolCallCompleted</c> invoked with tier-1 safe payload only
///         (no content, citation text, query strings, summary text)</item>
///   <item><c>requireCitations: true</c> with zero citations → <c>Error = "NO_CITATIONS_AVAILABLE"</c></item>
/// </list>
///
/// <para>
/// Design note: <c>ChatSessionManager</c> is a concrete class with a public-virtual
/// <c>GetSessionAsync</c>; we mock it directly via Moq with a stub constructor injecting
/// null-object collaborators. <c>IRagService</c> + <c>IContextEventEmitter</c> +
/// <c>IRecentlyDiscussedTracker</c> are mocked normally.
/// </para>
/// </remarks>
[Trait("status", "new")]
[Trait("project", "chat-routing-redesign-r1")]
[Trait("task", "085")]
public sealed class RecallSessionFileHandlerTests
{
    private const string DefaultTenantId = "tenant-test-fixture";
    private const string DefaultFileId = "file-deterministic-123";
    private const string DefaultSearchDocId = "search-doc-001";

    private readonly Mock<IRagService> _ragService = new();
    private readonly Mock<IContextEventEmitter> _contextEventEmitter = new();
    private readonly Mock<IRecentlyDiscussedTracker> _recentlyDiscussedTracker = new();

    private RecallSessionFileHandler CreateHandler(ChatSession? session)
    {
        // Build a Mock<ChatSessionManager> using its concrete constructor — we pass safe-default
        // collaborators so the Setup chain on GetSessionAsync resolves cleanly. Only
        // GetSessionAsync is exercised by the handler under test.
        // ChatSessionManager's last 2 ctor params (persistence + cleanupSignal) default to null;
        // Castle Proxy needs explicit values via reflection — pass null! to satisfy both the
        // nullability check and the params object[] signature.
        var sessionManagerMock = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,  // ISessionPersistenceService (optional)
            null!   // ISessionFilesCleanupSignal (optional)
        )
        { CallBase = false };
        sessionManagerMock
            .Setup(m => m.GetSessionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        return new RecallSessionFileHandler(
            ragService: _ragService.Object,
            sessionManager: sessionManagerMock.Object,
            timeProvider: TimeProvider.System,
            logger: NullLogger<RecallSessionFileHandler>.Instance,
            contextEventEmitter: _contextEventEmitter.Object,
            recentlyDiscussedTracker: _recentlyDiscussedTracker.Object);
    }

    private static AnalysisTool BuildRecallTool() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "recall_session_file",
            Type = ToolType.Custom
        };

    private static ChatInvocationContext BuildContext(
        string toolArgumentsJson,
        Guid? chatSessionId = null,
        string? tenantId = null)
    {
        return new ChatInvocationContext
        {
            ChatSessionId = chatSessionId ?? Guid.NewGuid(),
            TenantId = tenantId ?? DefaultTenantId,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = toolArgumentsJson
        };
    }

    private static ChatSession BuildSessionWithFile(
        Guid chatSessionId,
        string? summaryText = null,
        string fileId = DefaultFileId,
        string searchDocIdsCsv = DefaultSearchDocId)
    {
        var file = new ChatSessionFile(
            FileId: fileId,
            FileName: "fixture.pdf",
            ContentType: "application/pdf",
            SizeBytes: 1024,
            SearchDocumentIdsCsv: searchDocIdsCsv,
            UploadedAt: DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            SummaryText = summaryText
        };

        return new ChatSession(
            SessionId: chatSessionId.ToString("N"),
            TenantId: DefaultTenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: new[] { file });
    }

    private static RagSearchResponse BuildRagResponse(int resultCount, string? content = null)
    {
        var results = Enumerable.Range(0, resultCount)
            .Select(i => new RagSearchResult
            {
                Id = i == 0 ? DefaultSearchDocId : $"search-doc-{i:D3}",
                DocumentId = $"doc-{i}",
                DocumentName = $"fixture-doc-{i}.pdf",
                Content = content ?? $"Fixture content chunk {i} containing 2026-06-22 reference.",
                Score = 0.85,
                ChunkIndex = i,
                ChunkCount = resultCount
            })
            .ToList();
        return new RagSearchResponse
        {
            Query = "fixture",
            Results = results,
            TotalCount = resultCount,
            SearchDurationMs = 50,
            EmbeddingDurationMs = 10,
            EmbeddingCacheHit = true
        };
    }

    private static string BuildArgsJson(
        string fileId,
        string purpose,
        string query,
        string? scope = null,
        bool? requireCitations = null)
    {
        var scopeFragment = scope is null ? "" : $",\"scope\":\"{scope}\"";
        var citeFragment = requireCitations.HasValue
            ? $",\"requireCitations\":{(requireCitations.Value ? "true" : "false")}"
            : "";
        return $$"""
                 {
                   "fileId": "{{fileId}}",
                   "purpose": "{{purpose}}",
                   "query": "{{query}}"
                   {{scopeFragment}}
                   {{citeFragment}}
                 }
                 """;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Matrix: 6 purposes × 5 scopes (30 cases) — shape smoke test
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One [Theory] driving 30 [InlineData] rows (6 purposes × 5 scopes). Smokes the dispatch
    /// + parsing surface — asserts NO exception, ToolResult returned, structured payload
    /// present. Specific assertions per scope/purpose live in the targeted Facts below.
    /// </summary>
    [Theory]
    [InlineData(RecallSessionFileHandler.PurposeAnswerQuestion, RecallSessionFileHandler.ScopeSummary)]
    [InlineData(RecallSessionFileHandler.PurposeAnswerQuestion, RecallSessionFileHandler.ScopeRelevantSections)]
    [InlineData(RecallSessionFileHandler.PurposeAnswerQuestion, RecallSessionFileHandler.ScopeFullText)]
    [InlineData(RecallSessionFileHandler.PurposeAnswerQuestion, RecallSessionFileHandler.ScopeTables)]
    [InlineData(RecallSessionFileHandler.PurposeAnswerQuestion, RecallSessionFileHandler.ScopeCitations)]
    [InlineData(RecallSessionFileHandler.PurposeQuote, RecallSessionFileHandler.ScopeSummary)]
    [InlineData(RecallSessionFileHandler.PurposeQuote, RecallSessionFileHandler.ScopeRelevantSections)]
    [InlineData(RecallSessionFileHandler.PurposeQuote, RecallSessionFileHandler.ScopeFullText)]
    [InlineData(RecallSessionFileHandler.PurposeQuote, RecallSessionFileHandler.ScopeTables)]
    [InlineData(RecallSessionFileHandler.PurposeQuote, RecallSessionFileHandler.ScopeCitations)]
    [InlineData(RecallSessionFileHandler.PurposeCompare, RecallSessionFileHandler.ScopeSummary)]
    [InlineData(RecallSessionFileHandler.PurposeCompare, RecallSessionFileHandler.ScopeRelevantSections)]
    [InlineData(RecallSessionFileHandler.PurposeCompare, RecallSessionFileHandler.ScopeFullText)]
    [InlineData(RecallSessionFileHandler.PurposeCompare, RecallSessionFileHandler.ScopeTables)]
    [InlineData(RecallSessionFileHandler.PurposeCompare, RecallSessionFileHandler.ScopeCitations)]
    [InlineData(RecallSessionFileHandler.PurposeSummarize, RecallSessionFileHandler.ScopeSummary)]
    [InlineData(RecallSessionFileHandler.PurposeSummarize, RecallSessionFileHandler.ScopeRelevantSections)]
    [InlineData(RecallSessionFileHandler.PurposeSummarize, RecallSessionFileHandler.ScopeFullText)]
    [InlineData(RecallSessionFileHandler.PurposeSummarize, RecallSessionFileHandler.ScopeTables)]
    [InlineData(RecallSessionFileHandler.PurposeSummarize, RecallSessionFileHandler.ScopeCitations)]
    [InlineData(RecallSessionFileHandler.PurposeExtractDates, RecallSessionFileHandler.ScopeSummary)]
    [InlineData(RecallSessionFileHandler.PurposeExtractDates, RecallSessionFileHandler.ScopeRelevantSections)]
    [InlineData(RecallSessionFileHandler.PurposeExtractDates, RecallSessionFileHandler.ScopeFullText)]
    [InlineData(RecallSessionFileHandler.PurposeExtractDates, RecallSessionFileHandler.ScopeTables)]
    [InlineData(RecallSessionFileHandler.PurposeExtractDates, RecallSessionFileHandler.ScopeCitations)]
    [InlineData(RecallSessionFileHandler.PurposeVerify, RecallSessionFileHandler.ScopeSummary)]
    [InlineData(RecallSessionFileHandler.PurposeVerify, RecallSessionFileHandler.ScopeRelevantSections)]
    [InlineData(RecallSessionFileHandler.PurposeVerify, RecallSessionFileHandler.ScopeFullText)]
    [InlineData(RecallSessionFileHandler.PurposeVerify, RecallSessionFileHandler.ScopeTables)]
    [InlineData(RecallSessionFileHandler.PurposeVerify, RecallSessionFileHandler.ScopeCitations)]
    public async Task ExecuteChatAsync_Matrix_AllPurposesXScopes_ProduceStructuredToolResult(
        string purpose, string scope)
    {
        var chatSessionId = Guid.NewGuid();
        var session = BuildSessionWithFile(chatSessionId, summaryText: "Fixture precomputed summary.");
        var argsJson = BuildArgsJson(DefaultFileId, purpose, "what does this file say?", scope: scope,
            // requireCitations=false avoids the NO_CITATIONS_AVAILABLE error path so the matrix
            // smoke test asserts dispatch shape rather than the citation enforcement contract.
            requireCitations: false);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        // Provide a RagService response so non-summary scopes have results to compose.
        _ragService
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<RagSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(resultCount: 1));

        var handler = CreateHandler(session);
        var result = await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        // Smoke: result returned, dispatched cleanly; payload populated per the documented shape.
        result.Should().NotBeNull();
        var payload = result.GetData<RecallSessionFileHandler.RecallSessionFilePayload>();
        payload.Should().NotBeNull();
        payload!.Content.Should().NotBeNull();
        payload.Citations.Should().NotBeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // requireCitations default = TRUE (architecture §8.3 trust frame)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Metadata_DefaultsRequireCitationsToTrue()
    {
        var handler = CreateHandler(null);
        var requireCitationsParam = handler.Metadata.Parameters
            .FirstOrDefault(p => p.Name == "requireCitations");

        requireCitationsParam.Should().NotBeNull();
        requireCitationsParam!.DefaultValue.Should().Be(true,
            because: "architecture §8.3 trust-frame is non-negotiable for legal-domain accuracy");
        requireCitationsParam.Required.Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    // scope=summary reads ChatSessionFile.SummaryText directly (NO RagService call)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ScopeSummary_ReadsSummaryTextDirectly_AndDoesNotCallRagService()
    {
        var chatSessionId = Guid.NewGuid();
        const string summary = "Pre-computed summary body for fixture file (NOT authoritative per §2 P3).";
        var session = BuildSessionWithFile(chatSessionId, summaryText: summary);
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            "what's in this file?",
            scope: RecallSessionFileHandler.ScopeSummary);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        var handler = CreateHandler(session);
        var result = await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<RecallSessionFileHandler.RecallSessionFilePayload>();
        payload!.Content.Should().Be(summary);
        payload.Citations.Should().BeEmpty(because: "summary is NOT authoritative — NEVER cite the summary");
        payload.ScopeTruncated.Should().BeFalse();
        payload.TruncationReason.Should().BeNull();

        // Critical: NO RagService call for the summary fast path
        _ragService.Verify(r => r.SearchAsync(
            It.IsAny<string>(),
            It.IsAny<RagSearchOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // scope=relevant_sections calls RagService.SearchAsync with SessionId set
    // (architecture §5.2.1 binding — session-scoped routing picks spaarke-session-files)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ScopeRelevantSections_CallsRagService_WithSessionIdSet()
    {
        var chatSessionId = Guid.NewGuid();
        var expectedSessionIdString = chatSessionId.ToString("N");
        var session = BuildSessionWithFile(chatSessionId);
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            "what does the file say?",
            scope: RecallSessionFileHandler.ScopeRelevantSections);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        RagSearchOptions? capturedOptions = null;
        _ragService
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<RagSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(resultCount: 1));

        var handler = CreateHandler(session);
        var result = await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.SessionId.Should().Be(expectedSessionIdString,
            because: "session-scoped routing in RagService resolves the spaarke-session-files index " +
                     "directly when SessionId is set — architecture §5.2.1 binding");
        capturedOptions.TenantId.Should().Be(DefaultTenantId,
            because: "ADR-014: tenantId must be forwarded for the AND-tenantId-AND-sessionId isolation invariant");
        capturedOptions.SearchIndexName.Should().BeNullOrWhiteSpace(
            because: "we MUST NOT route via SearchIndexName which targets the knowledge index family");
    }

    // ════════════════════════════════════════════════════════════════════════
    // scope=full_text — truncation: total >8K returns truncation_reason="exceeded_8K"
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ScopeFullText_TotalExceedsCap_ReturnsExceeded8KTruncation()
    {
        var chatSessionId = Guid.NewGuid();
        var session = BuildSessionWithFile(
            chatSessionId,
            summaryText: "Fallback summary.",
            // Empty CSV bypasses file-scoped filtering so all RagService results flow into the
            // full-text composition path.
            searchDocIdsCsv: string.Empty);
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            "give me the whole document",
            scope: RecallSessionFileHandler.ScopeFullText,
            requireCitations: false);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        // 20 chunks × 5000 chars each = 100K chars > FullTextMaxChars (32K)
        var largeChunk = new string('A', 5000);
        _ragService
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<RagSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(resultCount: 20, content: largeChunk));

        var handler = CreateHandler(session);
        var result = await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<RecallSessionFileHandler.RecallSessionFilePayload>();
        payload!.ScopeTruncated.Should().BeTrue();
        payload.TruncationReason.Should().Be(RecallSessionFileHandler.TruncationReasonExceeded8K);
        payload.Content.Should().Contain("Fallback summary.",
            because: "architecture §9.2: truncated full_text returns summary + first 2K chars");
    }

    // ════════════════════════════════════════════════════════════════════════
    // fileId not in session → structured payload (NOT exception); §9.2 graceful error
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_FileIdNotInSession_ReturnsNotFoundPayload_AndDoesNotThrow()
    {
        var chatSessionId = Guid.NewGuid();
        // Session has a DIFFERENT file — the queried fileId is absent.
        var session = BuildSessionWithFile(chatSessionId, fileId: "different-file");
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            "what's in this file?",
            requireCitations: false);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        var handler = CreateHandler(session);
        var result = await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        result.Success.Should().BeTrue(
            because: "architecture §9.2 binds not_found to a structured payload, NEVER an exception");
        var payload = result.GetData<RecallSessionFileHandler.RecallSessionFilePayload>();
        payload!.ScopeTruncated.Should().BeTrue();
        payload.TruncationReason.Should().Be(RecallSessionFileHandler.TruncationReasonNotFound);
        payload.Content.Should().BeEmpty();
        payload.Citations.Should().BeEmpty();
    }

    // ════════════════════════════════════════════════════════════════════════
    // IRecentlyDiscussedTracker.MarkAsync called once per successful recall
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_OnSuccessfulRecall_MarksRecentlyDiscussedOnce()
    {
        var chatSessionId = Guid.NewGuid();
        var session = BuildSessionWithFile(chatSessionId, summaryText: "Some summary.");
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            "test query",
            scope: RecallSessionFileHandler.ScopeSummary);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        var handler = CreateHandler(session);
        await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        _recentlyDiscussedTracker.Verify(
            t => t.MarkAsync(
                It.IsAny<string>(),
                chatSessionId.ToString("N"),
                DefaultFileId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteChatAsync_OnNotFoundRecall_DoesNotMarkRecentlyDiscussed()
    {
        var chatSessionId = Guid.NewGuid();
        var session = BuildSessionWithFile(chatSessionId, fileId: "different-file");
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            "test query",
            requireCitations: false);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        var handler = CreateHandler(session);
        await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        _recentlyDiscussedTracker.Verify(
            t => t.MarkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // IContextEventEmitter.ToolCallCompleted invoked with tier-1 safe payload
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_EmitsToolCallCompleted_WithTier1SafePayloadOnly()
    {
        const string privilegedQuery = "PRIVILEGED LEGAL QUERY do not leak this string into telemetry";
        const string privilegedSummary = "PRIVILEGED LEGAL SUMMARY do not leak this string into telemetry";

        var chatSessionId = Guid.NewGuid();
        var session = BuildSessionWithFile(chatSessionId, summaryText: privilegedSummary);
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            privilegedQuery,
            scope: RecallSessionFileHandler.ScopeSummary);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        var handler = CreateHandler(session);
        await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        // The IContextEventEmitter.ToolCallCompleted signature accepts: toolName, decisionId,
        // sessionId, tenantId, outcome, durationMs — all tier-1 safe per ADR-015. Assert it
        // was called with non-leaking arguments. The signature is structurally constrained
        // (no object / JsonElement / freeform string content parameters that could carry
        // query/summary body), so we additionally verify NO call signature accidentally
        // includes the privileged strings.
        _contextEventEmitter.Verify(e => e.ToolCallCompleted(
            "recall_session_file",
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            DefaultTenantId,
            // outcome must be an enum-like short string
            It.Is<string>(s => s == RecallSessionFileHandler.OutcomeOk),
            It.IsAny<long>()), Times.Once);

        // Defensive cross-check: no ToolCallCompleted invocation EVER carried the privileged
        // strings in ANY argument slot.
        _contextEventEmitter.Verify(e => e.ToolCallCompleted(
            It.Is<string>(s => s.Contains(privilegedQuery) || s.Contains(privilegedSummary)),
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<long>()), Times.Never);
        _contextEventEmitter.Verify(e => e.ToolCallCompleted(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.Is<string?>(s => s != null && (s.Contains(privilegedQuery) || s.Contains(privilegedSummary))),
            It.IsAny<string>(),
            It.IsAny<long>()), Times.Never);
        _contextEventEmitter.Verify(e => e.ToolCallCompleted(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.Is<string>(s => s.Contains(privilegedQuery) || s.Contains(privilegedSummary)),
            It.IsAny<long>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // requireCitations: true with zero citations → Error = "NO_CITATIONS_AVAILABLE"
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_RequireCitationsTrue_ZeroCitations_ReturnsNoCitationsAvailableError()
    {
        var chatSessionId = Guid.NewGuid();
        var session = BuildSessionWithFile(
            chatSessionId,
            // searchDocIdsCsv with a value that won't match the RagService result IDs — so the
            // file-scoped filter removes ALL chunks and produces zero citations.
            searchDocIdsCsv: "non-matching-search-doc-id");
        var argsJson = BuildArgsJson(
            DefaultFileId,
            RecallSessionFileHandler.PurposeAnswerQuestion,
            "what does it say?",
            scope: RecallSessionFileHandler.ScopeRelevantSections,
            requireCitations: true);
        var ctx = BuildContext(argsJson, chatSessionId: chatSessionId);

        _ragService
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<RagSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(resultCount: 1)); // result has Id=DefaultSearchDocId; CSV doesn't include it

        var handler = CreateHandler(session);
        var result = await handler.ExecuteChatAsync(ctx, BuildRecallTool(), CancellationToken.None);

        result.Success.Should().BeFalse(
            because: "architecture §8.3 trust frame: requireCitations=true + zero citations + scope!=summary " +
                     "returns a structured tool error so the orchestrator can retry");
        result.ErrorCode.Should().Be(RecallSessionFileHandler.ErrorNoCitationsAvailable);
    }
}
