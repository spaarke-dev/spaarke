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
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for <c>POST /api/ai/analysis/fork</c> — fork-on-analysis
/// (ai-advanced-capabilities-analysis-hub-r1 task 021 / UQ-1 Option B / §6.5 Path A).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b> (mirrors <see cref="SummarizeSessionEndpointTestFixture"/> +
/// <see cref="AnalysisExecuteDispatchTestFixture"/>, ADR-038): a minimal in-process
/// <see cref="WebApplication"/> mapping the REAL <c>MapAnalysisEndpoints</c>. The fork handler runs
/// against a REAL <see cref="ChatSessionManager"/> over an <see cref="InMemoryTenantCache"/> and a
/// capturing <see cref="IChatDataverseRepository"/> double, so the session-mint + FK-bind + archive
/// composition is exercised through the production endpoint. <see cref="IAnalysisDataverseService"/>
/// and <see cref="IGenericEntityService"/> are module-boundary mocks (Analysis create + compensation
/// delete are the observable side effects).
/// </para>
/// <para>
/// <b>Coverage</b>: happy-path 201 with the { analysisId, newSessionId, archivedSessionId } triple +
/// the new session bound to the Analysis FK (task 020 convention) + prior transcript still
/// retrievable (ADR-040 store-of-record preserved); 401 unauthenticated (no write); 404
/// prior-not-found (no Analysis created — no orphan); and the compensation/no-orphan path (mint
/// fails after Analysis create → the Analysis is rolled back, 500, prior NOT archived).
/// </para>
/// </remarks>
public class AnalysisForkEndpointContractTests : IClassFixture<AnalysisForkEndpointTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly AnalysisForkEndpointTestFixture _fx;

    public AnalysisForkEndpointContractTests(AnalysisForkEndpointTestFixture fx)
    {
        _fx = fx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — 201 with the triple; new session FK-bound; prior preserved
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fork_AuthenticatedWithLivePriorSession_Returns201WithTripleAndBindsNewSessionToAnalysis()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();

        // Arrange — mint a real prior (loose) session via the same ChatSessionManager the endpoint uses.
        var prior = await _fx.Sessions.CreateSessionAsync(
            AnalysisForkEndpointTestFixture.TenantId, documentId.ToString(), playbookId: null, hostContext: null);
        _fx.ChatRepo.Created.Clear(); // ignore the prior's create; we assert only the fork's new-session create

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/fork", new
        {
            priorSessionId = prior.SessionId,
            documentId,
            name = "Forked Analysis",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<AnalysisForkResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.AnalysisId.Should().Be(AnalysisForkEndpointTestFixture.AnalysisId);
        body.NewSessionId.Should().NotBeNullOrEmpty();
        body.NewSessionId.Should().NotBe(prior.SessionId, "the fork mints a brand-new session");
        body.ArchivedSessionId.Should().Be(prior.SessionId);

        // The new session was created BOUND to the new Analysis (task-020 FK convention:
        // HostContext.EntityType == 'sprk_analysisoutput' + EntityId == analysisId).
        var newCreated = _fx.ChatRepo.Created.Should().ContainSingle().Subject;
        newCreated.SessionId.Should().Be(body.NewSessionId);
        newCreated.HostContext.Should().NotBeNull();
        newCreated.HostContext!.EntityType.Should().Be("sprk_analysisoutput");
        newCreated.HostContext.EntityId.Should().Be(AnalysisForkEndpointTestFixture.AnalysisId.ToString());

        // The prior session was archived (archive-marker seam called).
        _fx.ChatRepo.Archived.Should().Contain((AnalysisForkEndpointTestFixture.TenantId, prior.SessionId));

        // ADR-040: the prior transcript is preserved and still retrievable (archived, switchable back).
        var priorAfter = await _fx.Sessions.GetSessionAsync(
            AnalysisForkEndpointTestFixture.TenantId, prior.SessionId);
        priorAfter.Should().NotBeNull("the fork must NOT hard-delete the prior transcript (ADR-040 store-of-record)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH — 401 unauthenticated (before any Dataverse write)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fork_Unauthenticated_Returns401AndWritesNothing()
    {
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/analysis/fork", new
        {
            priorSessionId = Guid.NewGuid().ToString("N"),
            documentId = Guid.NewGuid(),
            name = "Should Not Run",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // No Dataverse writes occurred (401 fires at the auth boundary, before the handler).
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fx.ChatRepo.Created.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NOT FOUND — 404 prior-not-found leaves no orphan (Analysis never created)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fork_PriorSessionNotFound_Returns404AndCreatesNoAnalysis()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/analysis/fork", new
        {
            priorSessionId = Guid.NewGuid().ToString("N"), // never seeded → not found
            documentId = Guid.NewGuid(),
            name = "No Prior",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The existence check runs BEFORE any write, so nothing is orphaned.
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fx.ChatRepo.Created.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // COMPENSATION / NO-ORPHAN — mint fails after Analysis create → Analysis rolled back
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fork_NewSessionMintFailsAfterAnalysisCreate_RollsBackAnalysisAndReturns500()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();
        var priorId = Guid.NewGuid().ToString("N");

        // Seed a retrievable prior (repo cold path) WITHOUT going through CreateSessionAsync,
        // so the mint-failure flag below only trips the NEW session's create.
        _fx.ChatRepo.SessionsById[priorId] = BuildChatSession(priorId, documentId);
        _fx.ChatRepo.FailOnCreate = true; // simulate a catastrophic hot-path failure during the new-session mint

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/fork", new
        {
            priorSessionId = priorId,
            documentId,
            name = "Compensating Fork",
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // The Analysis was created then COMPENSATED (deleted) — no dangling anchor.
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(documentId, "Compensating Fork", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _fx.EntityServiceMock.Verify(
            e => e.DeleteAsync("sprk_analysis", AnalysisForkEndpointTestFixture.AnalysisId, It.IsAny<CancellationToken>()),
            Times.Once,
            "a mid-sequence mint failure MUST roll back the just-created Analysis (no orphan)");

        // The prior session was NOT archived — the fork aborted before the archive step.
        _fx.ChatRepo.Archived.Should().BeEmpty("archive must not run when the fork fails mid-sequence");
    }

    private static ChatSession BuildChatSession(string sessionId, Guid documentId) => new(
        SessionId: sessionId,
        TenantId: AnalysisForkEndpointTestFixture.TenantId,
        DocumentId: documentId.ToString(),
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>(),
        HostContext: null);
}

/// <summary>
/// Test fixture hosting a minimal <see cref="WebApplication"/> with <c>MapAnalysisEndpoints</c>
/// and the fork handler's dependency graph. Reuses the sibling summarize fixture's fake auth handler
/// (<see cref="SummarizeFakeAuthHandler"/> / <see cref="SummarizeFakeAuthOptions"/>).
/// </summary>
public sealed class AnalysisForkEndpointTestFixture : IAsyncLifetime, IDisposable
{
    public const string TenantId = "00000000-0000-0000-0000-000000000abc";
    public static readonly Guid AnalysisId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    public Mock<IAnalysisDataverseService> AnalysisServiceMock { get; } = new();
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new();
    public CapturingChatDataverseRepository ChatRepo { get; } = new();
    public ChatSessionManager Sessions { get; private set; } = null!;

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
            opt.AddPolicy("ai-stream", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-stream-test"));
        });

        // AddAiAuthorizationFilter dependency — pass-through (fork request carries no documentId
        // argument the filter would gate, so AuthorizeAsync is never invoked).
        builder.Services.AddSingleton(Mock.Of<IAiAuthorizationService>());

        // ── Fork handler dependency graph ────────────────────────────────────────────────
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
        builder.Services.AddSingleton(EntityServiceMock.Object);

        // ── Sibling endpoints in the same MapAnalysisEndpoints group (create/execute/continue/
        //    save/export/get/resume) — service types must be registered so minimal-API parameter
        //    inference treats them as services at map time even though these tests never invoke them.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(Options.Create(new AnalysisOptions { Enabled = true }));
        builder.Services.AddSingleton(Mock.Of<IPlaybookOrchestrationService>());
        builder.Services.AddSingleton(Mock.Of<IConsumerRoutingService>());
        builder.Services.AddSingleton(Mock.Of<IActionResolver>());
        builder.Services.AddSingleton(Mock.Of<IDocumentTextSource>());
        builder.Services.AddSingleton(Mock.Of<IActionRunner>());
        builder.Services.AddSingleton(Mock.Of<IPostUploadIndexingEnqueuer>());
        builder.Services.AddSingleton(Mock.Of<IAnalysisOrchestrationService>());
        builder.Services.AddSingleton(Mock.Of<IDocumentDataverseService>());
        builder.Services.AddSingleton(Mock.Of<ISpeFileOperations>());
        builder.Services.AddSingleton(Mock.Of<ITextExtractor>());
        builder.Services.AddSingleton<AnalysisDocumentLoader>();
        builder.Services.AddSingleton<NotificationService>();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapAnalysisEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void Reset()
    {
        AnalysisServiceMock.Reset();
        EntityServiceMock.Reset();
        ChatRepo.Created.Clear();
        ChatRepo.Archived.Clear();
        ChatRepo.SessionsById.Clear();
        ChatRepo.FailOnCreate = false;
        ConfigureDefaults();
    }

    private void ConfigureDefaults()
    {
        AnalysisServiceMock
            .Setup(s => s.CreateAnalysisAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalysisId);
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

/// <summary>
/// Capturing <see cref="IChatDataverseRepository"/> double for the fork endpoint tests. Records the
/// sessions created (to assert the new session's FK-binding HostContext) and the sessions archived,
/// and serves seeded sessions from the cold (repo) read path. <see cref="FailOnCreate"/> simulates a
/// catastrophic create failure so the compensation path can be exercised.
/// </summary>
public sealed class CapturingChatDataverseRepository : IChatDataverseRepository
{
    public List<ChatSession> Created { get; } = new();
    public List<(string TenantId, string SessionId)> Archived { get; } = new();
    public Dictionary<string, ChatSession> SessionsById { get; } = new(StringComparer.Ordinal);
    public bool FailOnCreate { get; set; }

    public Task CreateSessionAsync(ChatSession session, CancellationToken ct = default)
    {
        if (FailOnCreate)
        {
            // Non-InvalidOperationException so ChatSessionManager.CreateSessionAsync does NOT swallow
            // it (that catch is reserved for the tolerated Dataverse-write failure) — it propagates,
            // driving the fork handler's compensation path.
            throw new InvalidTimeZoneException("simulated hot-path create failure (fork compensation test)");
        }
        Created.Add(session);
        return Task.CompletedTask;
    }

    public Task<ChatSession?> GetSessionAsync(string tenantId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(SessionsById.TryGetValue(sessionId, out var s) ? s : null);

    public Task ArchiveSessionAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        Archived.Add((tenantId, sessionId));
        return Task.CompletedTask;
    }

    public Task AddMessageAsync(ChatMessage message, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, int maxMessages, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());

    public Task<int> GetMessageCountAsync(string tenantId, string sessionId, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task UpdateSessionActivityAsync(string tenantId, string sessionId, int messageCount, DateTimeOffset lastActivity, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UpdateSessionSummaryAsync(string tenantId, string sessionId, string summary, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AnalysisSessionSummary>> GetSessionsByAnalysisAsync(string tenantId, Guid analysisId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AnalysisSessionSummary>>(Array.Empty<AnalysisSessionSummary>());
}
