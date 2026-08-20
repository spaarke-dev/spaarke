using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Services.Ai.ReviewMemo;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for <c>POST /api/ai/chat/sessions/{sessionId}/review-memo</c> — FR-13 Review
/// Summary Memo assembly + persistence (ai-advanced-capabilities-agreements-r1 task 050).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b> (mirrors <see cref="AnalysisPromoteEndpointContractTests"/> /
/// <see cref="AnalysisForkEndpointContractTests"/>, ADR-038): a minimal in-process
/// <see cref="WebApplication"/> mapping the REAL <c>MapReviewMemoEndpoints</c>. The handler runs
/// against a REAL <see cref="ChatSessionManager"/> over an <see cref="InMemoryTenantCache"/> and the
/// SAME <see cref="CapturingChatDataverseRepository"/> double the fork/promote tests use, and a REAL
/// <see cref="AnalysisResultPersistence"/> wrapping a <see cref="Mock{IAnalysisDataverseService}"/> so
/// the write is observed at the KEEP-list persistence seam (<c>CreateAnalysisOutputAsync</c>).
/// </para>
/// <para>
/// <b>Coverage</b>: happy-path 201 persisting a memo with correct AnalysisId + JSON body that parses
/// back to the section array (persistence round-trip); sentinel-aware analysisId resolution (a session
/// bound to a DIFFERENT sentinel/entity never gets treated as Analysis-owned); 400 "no completed
/// review" (empty sections) with NOTHING persisted; 404 session-not-found; 400 session-not-bound (a
/// loose/casual chat session) with NOTHING persisted; 401 unauthenticated.
/// </para>
/// </remarks>
public class ReviewMemoEndpointContractTests : IClassFixture<ReviewMemoEndpointTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ReviewMemoEndpointTestFixture _fx;

    public ReviewMemoEndpointContractTests(ReviewMemoEndpointTestFixture fx)
    {
        _fx = fx;
    }

    private static object BuildValidBody() => new
    {
        overallRisk = "High",
        sections = new[]
        {
            new
            {
                sectionRef = "Section 4.2, para 2 (p. 3)",
                quotedText = "Confidential Information means information marked Confidential in writing.",
                afterText = (string?)"Confidential Information means any information disclosed, whether or not marked.",
                assessment = "Materially narrower than the standard.",
                standardRef = "B5 - Use & disclosure obligations",
                flaggedClause = "The clause defines Confidential Information only as information marked in writing.",
                riskLevel = "High",
            },
            new
            {
                sectionRef = "Section 6.1 (p. 4)",
                quotedText = "This Agreement shall be governed by the laws of Delaware.",
                afterText = (string?)null,
                assessment = "Standard governing-law clause; no deviation found worth negotiating.",
                standardRef = "B9 - Governing law",
                flaggedClause = "The clause selects Delaware law.",
                riskLevel = "Low",
            },
        },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — 201; persists under the RIGHT Analysis; JSON round-trips
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateMemo_SessionBoundToAnalysisWithSections_Returns201AndPersistsUnderCorrectAnalysis()
    {
        _fx.Reset();
        var analysisId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");

        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: ReviewMemoEndpointTestFixture.TenantId,
            DocumentId: Guid.NewGuid().ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: new ChatHostContext(
                EntityType: ChatSessionManager.AnalysisHostContextEntityType,
                EntityId: analysisId.ToString()));

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/review-memo", BuildValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<GenerateReviewMemoResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.AnalysisId.Should().Be(analysisId, "analysisId is resolved from the session's HostContext.EntityId, not the output row id");
        body.SectionCount.Should().Be(2);

        // Persistence round-trip: verify the EXACT AnalysisOutputEntity handed to the KEEP-list
        // persistence seam — correct AnalysisId FK, and the JSON Value parses back to the section array.
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisOutputAsync(
                It.Is<AnalysisOutputEntity>(o => o.AnalysisId == analysisId && o.Value != null),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var captured = _fx.CapturedOutputs.Should().ContainSingle().Subject;
        captured.AnalysisId.Should().Be(analysisId);

        var memo = JsonSerializer.Deserialize<ReviewMemoDocument>(captured.Value!, JsonOptions);
        memo.Should().NotBeNull();
        memo!.OverallRisk.Should().Be("High");
        memo.SectionCount.Should().Be(2);
        memo.Sections[0].Location.Should().Be("Section 4.2, para 2 (p. 3)");
        memo.Sections[0].After.Should().NotBe(memo.Sections[0].Before, "the first section supplied an AfterText — an accepted edit");
        memo.Sections[1].After.Should().Be(memo.Sections[1].Before, "the second section supplied no AfterText — rejected/untouched converge on before==after");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NEGATIVE — no completed review (empty sections) → 400, nothing persisted
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateMemo_NoSections_Returns400AndPersistsNothing()
    {
        _fx.Reset();
        var analysisId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");

        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: ReviewMemoEndpointTestFixture.TenantId,
            DocumentId: Guid.NewGuid().ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: new ChatHostContext(
                EntityType: ChatSessionManager.AnalysisHostContextEntityType,
                EntityId: analysisId.ToString()));

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/review-memo",
            new { overallRisk = "Low", sections = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisOutputAsync(It.IsAny<AnalysisOutputEntity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a request with no flagged sections is 'no completed review' — nothing is ever persisted");

        // agreements-r1 UAT round-1 #2 — this is the "no completed review" condition, DISTINCT from
        // "session not bound": the session IS bound here (Analysis HostContext), so the message must
        // carry the `no-completed-review` code and must NOT claim the session is unbound.
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("\"code\":\"no-completed-review\"");
        problem.Should().NotContain("not bound to an Analysis");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NOT FOUND — 404 session-not-found, nothing persisted
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateMemo_SessionNotFound_Returns404AndPersistsNothing()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{Guid.NewGuid():N}/review-memo", BuildValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisOutputAsync(It.IsAny<AnalysisOutputEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SENTINEL-AWARE — a loose (non-Analysis-owned) session → 400, nothing persisted
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateMemo_SessionNotBoundToAnalysis_Returns400AndPersistsNothing()
    {
        _fx.Reset();
        var sessionId = Guid.NewGuid().ToString("N");

        // A casual/ad-hoc chat session — no HostContext at all (never fork'd or promoted).
        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: ReviewMemoEndpointTestFixture.TenantId,
            DocumentId: Guid.NewGuid().ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null);

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/review-memo", BuildValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisOutputAsync(It.IsAny<AnalysisOutputEntity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an unbound session has nowhere to persist the memo");

        // agreements-r1 UAT round-1 #2 — the message must be SPLIT, not conflated: the not-bound
        // condition must NOT claim "there is no completed review" (a review may well have completed;
        // the memo is unusable only because there is nowhere durable to save it). It must carry the
        // machine-detectable `code` and guide the user to the existing promote affordance.
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("\"code\":\"session-not-bound\"");
        problem.Should().Contain("save the document first"); // UAT 2026-08-18: save-driven, History-free (was "Promote to Analysis")
        problem.Should().NotContain("there is no completed review",
            "the old conflated message wrongly claimed no review had completed — that claim is removed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SENTINEL FOOTGUN — HostContext.EntityType matching a DIFFERENT entity sentinel never resolves
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateMemo_SessionHostContextIsNonAnalysisEntity_Returns400AndPersistsNothing()
    {
        _fx.Reset();
        var sessionId = Guid.NewGuid().ToString("N");

        // A session hosted against a DIFFERENT parent entity (e.g. a matter-embedded SprkChat) must
        // never be mistaken for an Analysis-owned session, even though it carries a HostContext.
        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: ReviewMemoEndpointTestFixture.TenantId,
            DocumentId: Guid.NewGuid().ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: new ChatHostContext(EntityType: "matter", EntityId: Guid.NewGuid().ToString()));

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{sessionId}/review-memo", BuildValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisOutputAsync(It.IsAny<AnalysisOutputEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH — 401 unauthenticated, nothing persisted
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateMemo_Unauthenticated_Returns401AndPersistsNothing()
    {
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{Guid.NewGuid():N}/review-memo", BuildValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisOutputAsync(It.IsAny<AnalysisOutputEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-14 (task 051) — READ path: GET .../review-memo (JSON) and .../review-memo/docx (file)
    // ─────────────────────────────────────────────────────────────────────────

    private static ReviewMemoDocument BuildPersistedMemo() => new(
        SchemaVersion: "review-memo-v1",
        OverallRisk: "High",
        SectionCount: 1,
        Sections: new[]
        {
            new ReviewMemoSection(
                Location: "Section 4.2, para 2 (p. 3)",
                Before: "Confidential Information means information marked Confidential in writing.",
                After: "Confidential Information means any information disclosed, whether or not marked.",
                Why: "Materially narrower than the standard.",
                FlaggedClause: "The clause defines Confidential Information only as information marked in writing.",
                StandardRef: "B5 - Use & disclosure obligations",
                RiskLevel: "High"),
        });

    private string BindSessionToAnalysis(Guid analysisId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: ReviewMemoEndpointTestFixture.TenantId,
            DocumentId: Guid.NewGuid().ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: new ChatHostContext(
                EntityType: ChatSessionManager.AnalysisHostContextEntityType,
                EntityId: analysisId.ToString()));
        return sessionId;
    }

    [Fact]
    public async Task GetReviewMemo_MemoPersisted_Returns200WithMemoAndAnalysisName()
    {
        _fx.Reset();
        var analysisId = Guid.NewGuid();
        var sessionId = BindSessionToAnalysis(analysisId);
        var memo = BuildPersistedMemo();

        _fx.AnalysisServiceMock
            .Setup(s => s.GetLatestAnalysisOutputByNameAsync(analysisId, "Review Summary Memo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisOutputEntity
            {
                Id = Guid.NewGuid(),
                Name = "Review Summary Memo",
                Value = JsonSerializer.Serialize(memo),
                AnalysisId = analysisId,
            });
        _fx.AnalysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisEntity { Id = analysisId, Name = "Acme NDA Review", DocumentId = Guid.Empty });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/ai/chat/sessions/{sessionId}/review-memo");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ReviewMemoReadResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.AnalysisId.Should().Be(analysisId);
        body.AnalysisName.Should().Be("Acme NDA Review");
        body.Memo.SectionCount.Should().Be(1);
        body.Memo.Sections[0].Location.Should().Be(memo.Sections[0].Location);
        body.Memo.Sections[0].Before.Should().Be(memo.Sections[0].Before);
        body.Memo.Sections[0].After.Should().Be(memo.Sections[0].After);
    }

    [Fact]
    public async Task GetReviewMemo_NoMemoPersistedYet_Returns404WithGenerateFirstMessage()
    {
        _fx.Reset();
        var analysisId = Guid.NewGuid();
        var sessionId = BindSessionToAnalysis(analysisId);

        _fx.AnalysisServiceMock
            .Setup(s => s.GetLatestAnalysisOutputByNameAsync(analysisId, "Review Summary Memo", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisOutputEntity?)null);

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/ai/chat/sessions/{sessionId}/review-memo");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("Generate the review memo first");
    }

    [Fact]
    public async Task GetReviewMemo_SessionNotBoundToAnalysis_Returns400WithPromoteGuidance()
    {
        // The EXACT UAT round-1 #2 repro: the "Create Summary Memo" toolbar (a READ) on a
        // direct-Compose session that was never promoted/bound to an Analysis. Must be an accurate,
        // actionable ProblemDetails (promote first) — NOT the old conflated "no completed review".
        _fx.Reset();
        var sessionId = Guid.NewGuid().ToString("N");
        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: ReviewMemoEndpointTestFixture.TenantId,
            DocumentId: Guid.NewGuid().ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null);

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/ai/chat/sessions/{sessionId}/review-memo");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("\"code\":\"session-not-bound\"");
        problem.Should().Contain("save the document first"); // UAT 2026-08-18: save-driven, History-free (was "Promote to Analysis")
        problem.Should().NotContain("there is no completed review");
    }

    [Fact]
    public async Task GetReviewMemo_SessionNotFound_Returns404()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/ai/chat/sessions/{Guid.NewGuid():N}/review-memo");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetReviewMemoDocx_MemoPersisted_Returns200WithWordprocessingContentType()
    {
        _fx.Reset();
        var analysisId = Guid.NewGuid();
        var sessionId = BindSessionToAnalysis(analysisId);
        var memo = BuildPersistedMemo();

        _fx.AnalysisServiceMock
            .Setup(s => s.GetLatestAnalysisOutputByNameAsync(analysisId, "Review Summary Memo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisOutputEntity
            {
                Id = Guid.NewGuid(),
                Name = "Review Summary Memo",
                Value = JsonSerializer.Serialize(memo),
                AnalysisId = analysisId,
            });
        _fx.AnalysisServiceMock
            .Setup(s => s.GetAnalysisAsync(analysisId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisEntity { Id = analysisId, Name = "Acme NDA Review", DocumentId = Guid.Empty });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/ai/chat/sessions/{sessionId}/review-memo/docx");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0, "the rendered .docx must have real content, not an empty download");
        // OOXML .docx packages are ZIP archives — verify the local-file-header magic bytes ("PK\x03\x04").
        bytes[0].Should().Be(0x50);
        bytes[1].Should().Be(0x4B);
    }

    [Fact]
    public async Task GetReviewMemoDocx_NoMemoPersistedYet_Returns404_NeverAnEmptyDownload()
    {
        _fx.Reset();
        var analysisId = Guid.NewGuid();
        var sessionId = BindSessionToAnalysis(analysisId);

        _fx.AnalysisServiceMock
            .Setup(s => s.GetLatestAnalysisOutputByNameAsync(analysisId, "Review Summary Memo", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisOutputEntity?)null);

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/ai/chat/sessions/{sessionId}/review-memo/docx");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }
}

/// <summary>
/// Test fixture hosting a minimal <see cref="WebApplication"/> with <c>MapReviewMemoEndpoints</c> and
/// the handler's dependency graph. Reuses <see cref="CapturingChatDataverseRepository"/> +
/// <see cref="SummarizeFakeAuthHandler"/>/<see cref="SummarizeFakeAuthOptions"/> from the sibling
/// fork/promote/summarize endpoint fixtures (same test assembly, same namespace).
/// </summary>
public sealed class ReviewMemoEndpointTestFixture : IAsyncLifetime, IDisposable
{
    public const string TenantId = "00000000-0000-0000-0000-000000000abc";

    public Mock<IAnalysisDataverseService> AnalysisServiceMock { get; } = new();
    public CapturingChatDataverseRepository ChatRepo { get; } = new();
    public ChatSessionManager Sessions { get; private set; } = null!;

    /// <summary>Captures every <see cref="AnalysisOutputEntity"/> handed to <c>CreateAnalysisOutputAsync</c>.</summary>
    public List<AnalysisOutputEntity> CapturedOutputs { get; } = new();

    private readonly InMemoryTenantCache _cache = new();
    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        ConfigureDefaults();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });

        builder.Logging.ClearProviders();

        builder.Services
            .AddSingleton(new SummarizeFakeAuthOptions(includeTid: true))
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = SummarizeFakeAuthHandler.SchemeName;
                o.DefaultChallengeScheme = SummarizeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SummarizeFakeAuthHandler>(
                SummarizeFakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-batch", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-batch-test"));
        });

        // AddAiAuthorizationFilter dependency — pass-through (the review-memo request carries no
        // documentId argument the filter would gate; only the oid-claim 401 check is exercised).
        builder.Services.AddSingleton(Mock.Of<IAiAuthorizationService>());

        // ── review-memo handler dependency graph ─────────────────────────────────────────
        builder.Services.AddSingleton<ITenantCache>(_cache);
        builder.Services.AddSingleton<IChatDataverseRepository>(ChatRepo);
        Sessions = new ChatSessionManager(
            cache: _cache,
            dataverseRepository: ChatRepo,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: null);
        builder.Services.AddSingleton(Sessions);
        builder.Services.AddSingleton(AnalysisServiceMock.Object);

        // AnalysisResultPersistence's other constructor deps are not exercised by this endpoint's
        // ONE new method (PersistReviewMemoAsync only touches _analysisService) — module-boundary
        // mocks / trivial concretes, matching the sibling fixtures' style.
        builder.Services.AddSingleton(Mock.Of<IDocumentDataverseService>());
        builder.Services.AddSingleton(Mock.Of<IWorkingDocumentService>());
        builder.Services.AddSingleton(Mock.Of<IStorageRetryPolicy>());
        builder.Services.AddSingleton(new ExportServiceRegistry(Array.Empty<IExportService>()));
        builder.Services.AddSingleton<AnalysisResultPersistence>();
        // FR-14 (task 051) — the docx READ handler's rendering engine (real; pure/stateless, no I/O).
        builder.Services.AddSingleton<ComposeDocumentRenderer>();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapReviewMemoEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void Reset()
    {
        AnalysisServiceMock.Reset();
        CapturedOutputs.Clear();
        ChatRepo.SessionsById.Clear();
        ConfigureDefaults();
    }

    private void ConfigureDefaults()
    {
        AnalysisServiceMock
            .Setup(s => s.CreateAnalysisOutputAsync(It.IsAny<AnalysisOutputEntity>(), It.IsAny<CancellationToken>()))
            .Callback<AnalysisOutputEntity, CancellationToken>((entity, _) => CapturedOutputs.Add(entity))
            .ReturnsAsync(Guid.NewGuid());
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var opts = _app!.Services.GetRequiredService<SummarizeFakeAuthOptions>();
        opts.IncludeTid = true;
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }

    public HttpClient CreateUnauthenticatedClient() => _app!.GetTestClient();
}
