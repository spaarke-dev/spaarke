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
/// Contract tests for <c>POST /api/ai/analysis/promote</c> — the two-tier session model's
/// explicit-promotion path (ai-advanced-capabilities-analysis-hub-r1 task 023, spec FR-07).
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b> (mirrors <see cref="AnalysisForkEndpointContractTests"/>, ADR-038): a
/// minimal in-process <see cref="WebApplication"/> mapping the REAL <c>MapAnalysisEndpoints</c>. The
/// promote handler runs against a REAL <see cref="ChatSessionManager"/> over an
/// <see cref="InMemoryTenantCache"/> and the SAME capturing <see cref="CapturingChatDataverseRepository"/>
/// double the fork tests use (extended with <c>BindSessionToAnalysisAsync</c> capture/fail-injection),
/// so the fetch→create→bind composition is exercised through the production endpoint.
/// </para>
/// <para>
/// <b>Coverage</b>: happy-path 201 binding an EXISTING loose session in place (no new session, no
/// archive — contrast with <c>/fork</c>); 401 unauthenticated; 404 session-not-found; 400
/// double-promote guard (already Analysis-owned); 400 no-document (neither request nor session
/// supplies one); the compensation/no-orphan path (bind fails after Analysis create → the Analysis is
/// rolled back, 500); and the task's NEGATIVE acceptance criterion — a loose session created and used
/// normally (no promotion call) NEVER triggers <c>CreateAnalysisAsync</c>.
/// </para>
/// </remarks>
public class AnalysisPromoteEndpointContractTests : IClassFixture<AnalysisPromoteEndpointTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly AnalysisPromoteEndpointTestFixture _fx;

    public AnalysisPromoteEndpointContractTests(AnalysisPromoteEndpointTestFixture fx)
    {
        _fx = fx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — 201 binding the EXISTING session in place (no mint, no archive)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_LooseSessionWithDocument_Returns201AndBindsSessionInPlace()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();

        // Arrange — a real, loose (no HostContext) session, minted via the SAME ChatSessionManager
        // the endpoint uses.
        var loose = await _fx.Sessions.CreateSessionAsync(
            AnalysisPromoteEndpointTestFixture.TenantId, TestSessionOwner.Oid, documentId.ToString(), playbookId: null, hostContext: null);
        _fx.ChatRepo.Created.Clear();

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId = loose.SessionId,
            name = "Promoted Analysis",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<AnalysisPromoteResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.AnalysisId.Should().Be(AnalysisPromoteEndpointTestFixture.AnalysisId);
        body.SessionId.Should().Be(loose.SessionId, "promotion binds the SAME session id — no new session is minted");

        // No new sprk_aichatsummary row was created — this is a BIND, not a mint.
        _fx.ChatRepo.Created.Should().BeEmpty("promotion must not create a second sprk_aichatsummary row");

        // The FK bind seam was invoked against the EXISTING session/analysis pair.
        _fx.ChatRepo.Bound.Should().ContainSingle().Which.Should().Be(
            (AnalysisPromoteEndpointTestFixture.TenantId, loose.SessionId, AnalysisPromoteEndpointTestFixture.AnalysisId));

        // The session is now Analysis-owned in place (task-020 HostContext convention), same
        // session id, retrievable via the SAME ChatSessionManager (Redis re-read after promotion).
        var afterPromote = await _fx.Sessions.GetSessionAsync(
            AnalysisPromoteEndpointTestFixture.TenantId, loose.SessionId);
        afterPromote.Should().NotBeNull();
        afterPromote!.HostContext.Should().NotBeNull();
        afterPromote.HostContext!.EntityType.Should().Be("sprk_analysisoutput");
        afterPromote.HostContext.EntityId.Should().Be(AnalysisPromoteEndpointTestFixture.AnalysisId.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH — 401 unauthenticated (before any write)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_Unauthenticated_Returns401AndWritesNothing()
    {
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId = Guid.NewGuid().ToString("N"),
            name = "Should Not Run",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fx.ChatRepo.Bound.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NOT FOUND — 404 session-not-found leaves no orphan (Analysis never created)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_SessionNotFound_Returns404AndCreatesNoAnalysis()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId = Guid.NewGuid().ToString("N"), // never seeded → not found
            name = "No Such Session",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DOUBLE-PROMOTE GUARD — 400 when the session is already Analysis-owned
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_SessionAlreadyAnalysisOwned_Returns400AndCreatesNoSecondAnalysis()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();
        var existingAnalysisId = Guid.NewGuid();
        var ownedSessionId = Guid.NewGuid().ToString("N");

        // Seed an ALREADY Analysis-owned session directly on the cold-tier double (bypasses
        // CreateSessionAsync's own FK-write to isolate the promote guard).
        _fx.ChatRepo.SessionsById[ownedSessionId] = new ChatSession(
            SessionId: ownedSessionId,
            TenantId: AnalysisPromoteEndpointTestFixture.TenantId,
            DocumentId: documentId.ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: new ChatHostContext(
                EntityType: "sprk_analysisoutput",
                EntityId: existingAnalysisId.ToString()));

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId = ownedSessionId,
            name = "Second Analysis Attempt",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an already-promoted session must not be re-parented to a second Analysis");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NO DOCUMENT — 400 when neither the request nor the session supplies one
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_SessionWithNoDocumentAndNoRequestDocumentId_Returns400()
    {
        _fx.Reset();
        var sessionId = Guid.NewGuid().ToString("N");

        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: AnalysisPromoteEndpointTestFixture.TenantId,
            DocumentId: null, // knowledge-only chat — no attached document
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null) { OwnerOid = TestSessionOwner.Oid };

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId,
            name = "No Document Session",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // COMPENSATION / NO-ORPHAN — bind fails after Analysis create → Analysis rolled back
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_BindFailsAfterAnalysisCreate_RollsBackAnalysisAndReturns500()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");

        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: AnalysisPromoteEndpointTestFixture.TenantId,
            DocumentId: documentId.ToString(),
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null) { OwnerOid = TestSessionOwner.Oid };
        _fx.ChatRepo.FailOnBind = true;

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId,
            name = "Compensating Promote",
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(documentId, "Compensating Promote", It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _fx.EntityServiceMock.Verify(
            e => e.DeleteAsync("sprk_analysis", AnalysisPromoteEndpointTestFixture.AnalysisId, It.IsAny<CancellationToken>()),
            Times.Once,
            "a mid-sequence bind failure MUST roll back the just-created Analysis (no orphan)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NEGATIVE ACCEPTANCE CRITERION — a loose session, used normally, NEVER creates an Analysis
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAndUseLooseSession_WithoutExplicitPromotion_NeverCreatesAnalysis()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();

        // A casual/ad-hoc chat session: create + a normal "message activity" write-through cycle —
        // NO call to /api/ai/analysis/promote or /fork. This exercises the SAME ChatSessionManager
        // surface a real chat turn uses (create → re-fetch → cache update), never touching
        // IAnalysisDataverseService at all.
        var loose = await _fx.Sessions.CreateSessionAsync(
            AnalysisPromoteEndpointTestFixture.TenantId, TestSessionOwner.Oid, documentId.ToString(), playbookId: null, hostContext: null);
        var fetched = await _fx.Sessions.GetSessionAsync(AnalysisPromoteEndpointTestFixture.TenantId, loose.SessionId);

        fetched.Should().NotBeNull();
        fetched!.HostContext.Should().BeNull("a casual chat has no Analysis-owned HostContext by default");

        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a loose/ad-hoc chat session must NEVER auto-create an sprk_analysis record");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-D9 "Set related record" — regarding (matter) association + document-less relaxation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_WithRegardingMatterAndNoDocument_Returns201AndPassesRegardingTargetDocumentless()
    {
        _fx.Reset();
        var sessionId = Guid.NewGuid().ToString("N");
        var matterId = Guid.NewGuid();

        // A document-less loose session (knowledge-only chat) — historically un-promotable (400). With a
        // regarding target (FR-D9), it becomes promotable: the analysis is anchored by the matter, not a doc.
        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: AnalysisPromoteEndpointTestFixture.TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null) { OwnerOid = TestSessionOwner.Oid };

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId,
            name = "Filed to Matter",
            regardingEntityType = "sprk_matter",
            regardingEntityId = matterId,
            regardingEntityName = "Smith v. Jones",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // The endpoint passed the regarding target through to CreateAnalysisAsync with a NULL document
        // (document-anchor relaxation) — the ADR-024 5-field write itself lives in the Dataverse impl.
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(
                It.Is<Guid?>(d => d == null),
                "Filed to Matter",
                It.IsAny<Guid?>(),
                It.Is<AnalysisRegardingTarget?>(t => t != null && t.EntityLogicalName == "sprk_matter" && t.RecordId == matterId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a regarding-anchored promotion must forward the target and allow a document-less analysis");

        // The session is bound to the new Analysis in place (same session id, no mint).
        _fx.ChatRepo.Bound.Should().ContainSingle().Which.Should().Be(
            (AnalysisPromoteEndpointTestFixture.TenantId, sessionId, AnalysisPromoteEndpointTestFixture.AnalysisId));
    }

    [Fact]
    public async Task Promote_WithUnsupportedRegardingEntityType_Returns400AndCreatesNoAnalysis()
    {
        _fx.Reset();
        var sessionId = Guid.NewGuid().ToString("N");

        _fx.ChatRepo.SessionsById[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: AnalysisPromoteEndpointTestFixture.TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null) { OwnerOid = TestSessionOwner.Oid };

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId,
            name = "Bad Regarding",
            regardingEntityType = "account", // not in the closed matter|project set
            regardingEntityId = Guid.NewGuid(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fx.AnalysisServiceMock.Verify(
            s => s.CreateAnalysisAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Test fixture hosting a minimal <see cref="WebApplication"/> with <c>MapAnalysisEndpoints</c> and
/// the promote handler's dependency graph. Reuses the SAME <see cref="CapturingChatDataverseRepository"/>
/// double (extended for <c>BindSessionToAnalysisAsync</c>) and fake-auth scheme the fork endpoint
/// fixture uses (<see cref="AnalysisForkEndpointTestFixture"/> / <see cref="SummarizeFakeAuthHandler"/>).
/// </summary>
public sealed class AnalysisPromoteEndpointTestFixture : IAsyncLifetime, IDisposable
{
    // MUST match the "tid" claim SummarizeFakeAuthHandler always emits (shared fake-auth scheme
    // with AnalysisForkEndpointContractTests) — the endpoint's ExtractTenantId reads this claim.
    public const string TenantId = "00000000-0000-0000-0000-000000000abc";
    public static readonly Guid AnalysisId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

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

        builder.Services.AddSingleton(Mock.Of<IAiAuthorizationService>());

        // ── Promote handler dependency graph ─────────────────────────────────────────
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

        // ── Sibling endpoints in the same MapAnalysisEndpoints group — service types must be
        //    registered so minimal-API parameter inference treats them as services at map time.
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
        ChatRepo.Bound.Clear();
        ChatRepo.FailOnCreate = false;
        ChatRepo.FailOnBind = false;
        ConfigureDefaults();
    }

    private void ConfigureDefaults()
    {
        AnalysisServiceMock
            .Setup(s => s.CreateAnalysisAsync(
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(), It.IsAny<CancellationToken>()))
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
