using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for R5 task 032 — <c>ChatDocumentEndpoints.UploadDocumentAsync</c> wiring
/// into the session-files RAG indexing pipeline + <see cref="ChatSession.UploadedFiles"/>
/// manifest persistence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b>: builds a minimal in-process <see cref="WebApplication"/> that
/// maps ONLY <c>MapChatDocumentEndpoints()</c> with test-double dependencies. Mirrors the
/// pattern in <see cref="SummarizeSessionEndpointTests"/>.
/// </para>
/// <para>
/// <b>Coverage</b>:
/// <list type="number">
///   <item>Successful upload → <see cref="RagIndexingPipeline.IndexSessionFileAsync"/>
///         is invoked with the expected (tenantId, sessionId, documentId, fileName)
///         arguments (observed via the SearchIndexClient mock surfaces).</item>
///   <item>Successful upload → <see cref="ChatSessionManager.UpdateSessionCacheAsync"/>
///         is invoked with a session whose <see cref="ChatSession.UploadedFiles"/> has
///         one additional entry.</item>
///   <item>21st upload to a session already at the 20-file cap → 400 ProblemDetails
///         with stable <c>summarize.too-many-files</c> errorCode and NO mutation
///         (defense-in-depth per NFR-02; cap-check happens BEFORE indexing).</item>
///   <item>Pre-existing Redis writes (<c>doc-upload:</c>, <c>doc-binary:</c>,
///         <c>doc-upload-meta:</c>) still happen on successful upload — back-compat
///         for any consumer that still reads those keys directly.</item>
/// </list>
/// </para>
/// <para>
/// <b>Live-patched tid-claim fix preservation</b> (operator-applied 2026-06-04, lines
/// 151-153 + 443-445 of <c>ChatDocumentEndpoints.cs</c>): the auth handler in this
/// fixture emits the short <c>tid</c> claim form, which matches the canonical path. The
/// schema-URL fallback at lines 152 + 444 is exercised only when the JWT pipeline strips
/// the short form — out of scope here (covered separately when the follow-up PR adds a
/// fixture variant for that scenario).
/// </para>
/// </remarks>
public class ChatDocumentEndpointsTests : IClassFixture<ChatDocumentEndpointsTestFixture>
{
    private readonly ChatDocumentEndpointsTestFixture _fx;

    private const string TestTenantId = "00000000-0000-0000-0000-000000000abc";
    private const string TestSessionId = "11111111-2222-3333-4444-555555555555";

    public ChatDocumentEndpointsTests(ChatDocumentEndpointsTestFixture fx)
    {
        _fx = fx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1 — Successful upload invokes IndexSessionFileAsync with expected args
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_Success_CallsIndexSessionFileAsync_WithExpectedArgs()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, uploadedFiles: null);

        var client = _fx.CreateAuthenticatedClient();
        using var form = BuildMultipartForm(filename: "contract.pdf",
            content: "This is a contract document for summarization testing.");

        var response = await client.PostAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/documents", form);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The session-files SearchClient mock receives an Upload call (via the
        // RagIndexingPipeline pipeline). The IndexSessionFileAsync call is
        // surfaced via SearchIndexClient.GetSearchClient(SessionFilesIndexName)
        // → MergeOrUploadDocumentsAsync(KnowledgeDocument[]).
        _fx.SessionFilesSearchClientMock.Verify(
            c => c.MergeOrUploadDocumentsAsync(
                It.Is<IEnumerable<KnowledgeDocument>>(docs =>
                    docs.Any(d => d.TenantId == TestTenantId
                               && d.SessionId == TestSessionId
                               && d.FileName == "contract.pdf")),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "IndexSessionFileAsync must propagate tenantId + sessionId + fileName to AI Search (ADR-014 + R5 FR-09)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2 — Successful upload invokes UpdateSessionCacheAsync with updated manifest
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_Success_PersistsUpdatedSession_WithNewUploadedFileEntry()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, uploadedFiles: null);

        var client = _fx.CreateAuthenticatedClient();
        using var form = BuildMultipartForm(filename: "contract.pdf",
            content: "Contract body text for indexing.");

        var response = await client.PostAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/documents", form);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        _fx.Sessions.PersistedSession.Should().NotBeNull(
            "endpoint must call UpdateSessionCacheAsync after a successful upload (decision D-06)");
        _fx.Sessions.PersistedSession!.UploadedFiles.Should().NotBeNull();
        _fx.Sessions.PersistedSession.UploadedFiles!.Should().HaveCount(1,
            "the new ChatSessionFile must be appended to the session's UploadedFiles");

        var newEntry = _fx.Sessions.PersistedSession.UploadedFiles![0];
        newEntry.FileName.Should().Be("contract.pdf");
        newEntry.ContentType.Should().Be("application/pdf");
        newEntry.SizeBytes.Should().BeGreaterThan(0);
        newEntry.SearchDocumentIdsCsv.Should().NotBeNullOrEmpty(
            "SearchDocumentIdsCsv carries the chunk IDs needed by the cleanup job (task 007)");
        newEntry.FileId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Upload_Success_AppendsToExistingUploadedFiles()
    {
        _fx.Reset();
        // Session already has 2 files — new upload should produce a manifest of 3.
        var existing = new List<ChatSessionFile>
        {
            new("file-existing-1", "first.pdf", "application/pdf", 1024, "doc-1_s_0", DateTimeOffset.UtcNow),
            new("file-existing-2", "second.pdf", "application/pdf", 2048, "doc-2_s_0", DateTimeOffset.UtcNow)
        };
        _fx.Sessions.Session = BuildSession(TestSessionId, uploadedFiles: existing);

        var client = _fx.CreateAuthenticatedClient();
        using var form = BuildMultipartForm(filename: "third.pdf",
            content: "Third document content.");

        var response = await client.PostAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/documents", form);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _fx.Sessions.PersistedSession!.UploadedFiles!.Should().HaveCount(3,
            "endpoint must APPEND the new file (not replace existing UploadedFiles)");
        _fx.Sessions.PersistedSession.UploadedFiles![2].FileName.Should().Be("third.pdf");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3 — 21st upload (cap reached) → 400 with `summarize.too-many-files`, no mutation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_AtCap_Returns400_TooManyFiles_NoMutation()
    {
        _fx.Reset();
        // Session already has 20 files — at the NFR-02 hard cap.
        var atCap = Enumerable.Range(0, ChatSession.MaxUploadedFiles)
            .Select(i => new ChatSessionFile(
                FileId: $"file-{i:000}",
                FileName: $"file-{i:000}.pdf",
                ContentType: "application/pdf",
                SizeBytes: 1024,
                SearchDocumentIdsCsv: $"file-{i:000}_s_0",
                UploadedAt: DateTimeOffset.UtcNow))
            .ToList();
        _fx.Sessions.Session = BuildSession(TestSessionId, uploadedFiles: atCap);

        var client = _fx.CreateAuthenticatedClient();
        using var form = BuildMultipartForm(filename: "twenty-first.pdf",
            content: "This 21st upload must be rejected.");

        var response = await client.PostAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/documents", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "21st upload must be rejected with 400 ProblemDetails (NFR-02 defense-in-depth)");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("summarize.too-many-files",
            "ADR-019: stable errorCode contract must match SummarizeSessionEndpoint's constant");

        // Defense-in-depth: cap-check happens BEFORE any indexing or cache writes.
        _fx.SessionFilesSearchClientMock.Verify(
            c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "cap-check must reject BEFORE invoking IndexSessionFileAsync (no AI Search writes)");
        _fx.Sessions.PersistedSession.Should().BeNull(
            "cap-check must reject BEFORE calling UpdateSessionCacheAsync (no manifest mutation)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4 — Pre-existing Redis writes still happen on success (back-compat)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_Success_StillWritesLegacyRedisKeys_ForBackCompat()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(TestSessionId, uploadedFiles: null);

        var client = _fx.CreateAuthenticatedClient();
        using var form = BuildMultipartForm(filename: "contract.pdf",
            content: "Document content for Redis back-compat verification.");

        var response = await client.PostAsync(
            $"/api/ai/chat/sessions/{TestSessionId}/documents", form);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // R3 legacy paths still populated (no consumer left behind).
        var docUploadKeys = _fx.CacheCalls.Where(k => k.StartsWith("doc-upload:")).ToList();
        var docBinaryKeys = _fx.CacheCalls.Where(k => k.StartsWith("doc-binary:")).ToList();
        var docMetaKeys = _fx.CacheCalls.Where(k => k.StartsWith("doc-upload-meta:")).ToList();

        docUploadKeys.Should().NotBeEmpty(
            "back-compat: extracted text Redis write (doc-upload:) must still happen");
        docBinaryKeys.Should().NotBeEmpty(
            "back-compat: original binary Redis write (doc-binary:) must still happen");
        docMetaKeys.Should().NotBeEmpty(
            "back-compat: metadata Redis write (doc-upload-meta:) must still happen");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static ChatSession BuildSession(string sessionId, IReadOnlyList<ChatSessionFile>? uploadedFiles)
        => new(
            SessionId: sessionId,
            TenantId: TestTenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: uploadedFiles);

    private static MultipartFormDataContent BuildMultipartForm(string filename, string content)
    {
        // Build a multipart payload with a single "file" field. ContentType: application/pdf
        // so the existing extension/MIME validators in the endpoint accept it.
        var form = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(content);
        var byteContent = new ByteArrayContent(bytes);
        byteContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(byteContent, "file", filename);
        return form;
    }
}

/// <summary>
/// Test fixture for <see cref="ChatDocumentEndpointsTests"/>. Hosts a minimal
/// <see cref="WebApplication"/> mapping only <c>MapChatDocumentEndpoints()</c> with
/// test-double dependencies — no production-config matrix wired in.
/// </summary>
public sealed class ChatDocumentEndpointsTestFixture : IAsyncLifetime, IDisposable
{
    public TestableChatSessionManagerForUpload Sessions { get; } = new();
    public Mock<ITextExtractor> TextExtractorMock { get; } = new();
    public Mock<IAiAuthorizationService> AuthMock { get; } = new();
    public Mock<SearchIndexClient> SearchIndexClientMock { get; } = new();
    public Mock<SearchClient> SessionFilesSearchClientMock { get; } = new();
    public Mock<IRagService> RagServiceMock { get; } = new();
    public Mock<ITextChunkingService> ChunkingServiceMock { get; } = new();
    public Mock<IOpenAiClient> OpenAiClientMock { get; } = new();
    public List<string> CacheCalls { get; } = new();

    private WebApplication? _app;
    private RecordingDistributedCache _recordingCache = null!;

    public async Task InitializeAsync()
    {
        ConfigureDefaults();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.Logging.ClearProviders();

        // Auth — single scheme that succeeds on Authorization: Bearer ... with tid claim.
        builder.Services
            .AddSingleton(new UploadFakeAuthOptions(includeTid: true))
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = UploadFakeAuthHandler.SchemeName;
                o.DefaultChallengeScheme = UploadFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, UploadFakeAuthHandler>(
                UploadFakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        // Rate-limit policy used by AddAiAuthorizationFilter consumers — register no-op.
        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-upload", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-upload-test"));
            opt.AddPolicy("ai-persist", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-persist-test"));
        });

        builder.Services.AddProblemDetails();

        // Test-double dependencies for the endpoint handler.
        _recordingCache = new RecordingDistributedCache(CacheCalls);
        builder.Services.AddSingleton<IDistributedCache>(_recordingCache);
        builder.Services.AddSingleton<ChatSessionManager>(Sessions);
        builder.Services.AddSingleton<ITextExtractor>(TextExtractorMock.Object);
        builder.Services.AddSingleton(AuthMock.Object);

        // RagIndexingPipeline — sealed concrete; build a real instance with mocked
        // dependencies. The pipeline writes through SearchIndexClient.GetSearchClient(...)
        // and the configured options below.
        SearchIndexClientMock
            .Setup(x => x.GetSearchClient(It.IsAny<string>()))
            .Returns(SessionFilesSearchClientMock.Object);
        builder.Services.AddSingleton(SearchIndexClientMock.Object);
        builder.Services.AddSingleton(RagServiceMock.Object);
        builder.Services.AddSingleton(ChunkingServiceMock.Object);
        builder.Services.AddSingleton(OpenAiClientMock.Object);
        builder.Services.AddSingleton<IOptions<AiSearchOptions>>(
            Options.Create(new AiSearchOptions
            {
                DiscoveryIndexName = "test-discovery",
                KnowledgeIndexName = "test-knowledge",
                SessionFilesIndexName = "test-session-files"
            }));
        builder.Services.AddSingleton<RagIndexingPipeline>();

        // SpeFileStore is needed by PersistDocumentAsync but not by UploadDocumentAsync;
        // the endpoint mapping still resolves the type at startup so register a stub.
        builder.Services.AddSingleton<Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore>(_ =>
            null!); // not exercised by upload tests

        // IPostUploadIndexingEnqueuer is needed by PersistDocumentAsync (Phase 3a — sync OBO
        // post-upload indexing). Same registration pattern as SpeFileStore above — endpoint
        // parameter binding requires the type to resolve at startup; not exercised by upload tests.
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.IPostUploadIndexingEnqueuer>(_ =>
            Moq.Mock.Of<Sprk.Bff.Api.Services.Ai.IPostUploadIndexingEnqueuer>());

        builder.WebHost.UseTestServer();
        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapChatDocumentEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose()
    {
        _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Reset()
    {
        Sessions.Session = null;
        Sessions.PersistedSession = null;
        TextExtractorMock.Reset();
        AuthMock.Reset();
        SessionFilesSearchClientMock.Reset();
        RagServiceMock.Reset();
        ChunkingServiceMock.Reset();
        OpenAiClientMock.Reset();
        CacheCalls.Clear();
        ConfigureDefaults();
    }

    private void ConfigureDefaults()
    {
        // Default text extraction — returns a small valid string for any input.
        TextExtractorMock
            .Setup(t => t.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded(
                "Stub extracted text content for testing.",
                TextExtractionMethod.DocumentIntelligence));

        // Default auth filter — the filter passes through when no document IDs are
        // present in the request args (the upload endpoint takes sessionId + IFormFile,
        // neither is a Guid). Default mock still returns Authorized for completeness.
        AuthMock
            .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Authorized(Array.Empty<Guid>()));

        // Default chunking — one small chunk so embedding/upload paths succeed.
        ChunkingServiceMock
            .Setup(c => c.ChunkTextAsync(It.IsAny<string>(),
                It.IsAny<ChunkingOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>
            {
                new() { Content = "chunk-content-0", Index = 0, StartPosition = 0, EndPosition = 100 }
            });

        // Default embedding — small 3072-dim vector.
        var embedding = new float[3072];
        OpenAiClientMock
            .Setup(o => o.GenerateEmbeddingAsync(It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(embedding));

        // Default session-files search (delete-stale step) — empty result.
        var emptyResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);
        SessionFilesSearchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(emptyResults, null!));

        // Default upload — returns a successful single-document IndexDocumentsResult.
        var successResult = SearchModelFactory.IndexingResult("doc-0_s_0", null, true, 201);
        var indexResults = SearchModelFactory.IndexDocumentsResult(new[] { successResult });
        SessionFilesSearchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(indexResults, null!));
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "fake-upload-token");
        return client;
    }
}

/// <summary>
/// Subclass of <see cref="ChatSessionManager"/> that overrides
/// <see cref="ChatSessionManager.GetSessionAsync(string, string, CancellationToken)"/> and
/// <see cref="ChatSessionManager.UpdateSessionCacheAsync(ChatSession, CancellationToken)"/>
/// for in-process testing.
/// </summary>
public sealed class TestableChatSessionManagerForUpload : ChatSessionManager
{
    public TestableChatSessionManagerForUpload() : base(
        cache: Mock.Of<IDistributedCache>(),
        dataverseRepository: Mock.Of<IChatDataverseRepository>(),
        logger: Mock.Of<ILogger<ChatSessionManager>>(),
        persistence: null,
        cleanupSignal: null)
    {
    }

    public ChatSession? Session { get; set; }
    public ChatSession? PersistedSession { get; set; }

    public override Task<ChatSession?> GetSessionAsync(
        string tenantId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(Session);

    internal override Task UpdateSessionCacheAsync(
        ChatSession session, CancellationToken ct = default)
    {
        PersistedSession = session;
        return Task.CompletedTask;
    }
}

/// <summary>Records every cache key SET so back-compat tests can assert the legacy Redis writes still happen.</summary>
public sealed class RecordingDistributedCache : IDistributedCache
{
    private readonly List<string> _calls;
    private readonly Dictionary<string, byte[]> _store = new();

    public RecordingDistributedCache(List<string> calls) => _calls = calls;

    public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        => Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        _calls.Add(key);
        _store[key] = value;
    }
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>Singleton holder for "include tid claim?" — mutated per-test via fixture.</summary>
public sealed class UploadFakeAuthOptions
{
    public bool IncludeTid { get; set; }
    public UploadFakeAuthOptions(bool includeTid) => IncludeTid = includeTid;
}

/// <summary>Bearer-presence auth handler for the in-process WebApplication test fixture.</summary>
public sealed class UploadFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "UploadFakeAuth";
    private readonly UploadFakeAuthOptions _opts;

    public UploadFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        UploadFakeAuthOptions opts)
        : base(options, logger, encoder)
    {
        _opts = opts;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));
        }

        var claims = new List<Claim>
        {
            new("oid", "00000000-0000-0000-0000-000000000aaa"),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000aaa"),
            new(System.Security.Claims.ClaimTypes.Name, "Upload Endpoint Test User"),
        };

        if (_opts.IncludeTid)
        {
            claims.Add(new Claim("tid", "00000000-0000-0000-0000-000000000abc"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
