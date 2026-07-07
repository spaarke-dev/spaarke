using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for the FR-P3-01 Linear-dispatch detection inside
/// <c>POST /api/ai/analysis/execute</c> (spaarke-ai-architecture-redesign-r1 task 040).
/// </summary>
/// <remarks>
/// <para>
/// The legacy <c>LinearConsumers:PlaybookIds</c> config reverse-lookup was deleted. The
/// endpoint now resolves the document-profile playbook through
/// <see cref="IConsumerRoutingService.ResolveAsync"/> (the <c>sprk_playbookconsumer</c>
/// Binding table — single routing surface per ADR-039) and takes the Linear
/// <see cref="DocumentProfileService"/> branch when the client-submitted legacy
/// <c>PlaybookId</c> equals the Binding row's <c>sprk_playbook</c>.
/// </para>
/// <para>
/// <b>Hosting approach</b>: minimal in-process <see cref="WebApplication"/> mapping
/// <c>MapAnalysisEndpoints</c>. Module-boundary doubles (ADR-038):
/// <see cref="IConsumerRoutingService"/>, <see cref="IPlaybookOrchestrationService"/>,
/// <see cref="IActionResolver"/> and friends. The <see cref="DocumentProfileService"/>,
/// <see cref="AnalysisDocumentLoader"/> and <see cref="NotificationService"/> concretes are
/// REAL instances over those doubles, so the dispatch decision is exercised through the
/// production handler.
/// </para>
/// </remarks>
public class AnalysisEndpointsExecuteDispatchContractTests
    : IClassFixture<AnalysisExecuteDispatchTestFixture>
{
    private readonly AnalysisExecuteDispatchTestFixture _fx;

    public AnalysisEndpointsExecuteDispatchContractTests(AnalysisExecuteDispatchTestFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task Post_PlaybookIdMatchesDocumentProfileBinding_DispatchesLinearBranch()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { documentId },
            playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"document-profile:{documentId}",
            "a PlaybookId equal to the Binding-routed document-profile playbook MUST dispatch " +
            "the Linear DocumentProfileService branch (FR-P3-01 replaces the config reverse-lookup)");
        body.Should().Contain("data: [DONE]",
            "the Linear branch preserves the client SSE terminator contract");

        _fx.PlaybookOrchestratorMock.Verify(
            o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the Linear branch MUST NOT fall through to the Playbook Engine");
    }

    [Fact]
    public async Task Post_PlaybookIdDoesNotMatchBinding_FallsThroughToEnginePath()
    {
        _fx.Reset();
        var documentId = Guid.NewGuid();
        var otherPlaybookId = Guid.NewGuid(); // NOT the Binding-routed document-profile playbook

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { documentId },
            playbookId = otherPlaybookId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("document-profile:",
            "a non-matching PlaybookId MUST NOT dispatch the Linear Document Profile branch");
        body.Should().Contain("data: [DONE]");

        _fx.PlaybookOrchestratorMock.Verify(
            o => o.ExecuteAsync(
                It.Is<PlaybookRunRequest>(r => r.PlaybookId == otherPlaybookId),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "non-Linear consumers keep the canonical Playbook Engine dispatch");
    }

    [Fact]
    public async Task Post_NoDocumentProfileBindingRow_FallsThroughToEnginePath()
    {
        // FR-P3-01 hard cutover behavior at the endpoint: when NO Binding row resolves
        // 'document-profile' (ResolveAsync → null), nothing can match — every request
        // falls through to the engine path (no config fallback exists anymore).
        _fx.Reset();
        _fx.ConsumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.DocumentProfile,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var documentId = Guid.NewGuid();
        var client = _fx.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { documentId },
            playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("document-profile:");

        _fx.PlaybookOrchestratorMock.Verify(
            o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

/// <summary>
/// Test fixture hosting a minimal <see cref="WebApplication"/> with
/// <c>MapAnalysisEndpoints</c> and the /execute handler's dependency graph.
/// Reuses the sibling summarize fixture's fake auth handler.
/// </summary>
public sealed class AnalysisExecuteDispatchTestFixture : IAsyncLifetime, IDisposable
{
    /// <summary>Mirrors the seeded spaarkedev1 document-profile Binding row's sprk_playbook.</summary>
    public static readonly Guid DocumentProfilePlaybookId =
        Guid.Parse("18cf3cc8-02ec-f011-8406-7c1e520aa4df");

    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();
    public Mock<IPlaybookOrchestrationService> PlaybookOrchestratorMock { get; } = new();
    public Mock<IActionResolver> ActionResolverMock { get; } = new();

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
            opt.AddPolicy("ai-stream", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-stream-test"));
            opt.AddPolicy("ai-batch", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-batch-test"));
        });

        // Endpoint auth filter dependency — pass-through authorization.
        var authMock = new Mock<IAiAuthorizationService>();
        authMock
            .Setup(a => a.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Authorized(Array.Empty<Guid>()));
        builder.Services.AddSingleton(authMock.Object);

        // /execute handler dependency graph — module-boundary doubles + real concretes.
        builder.Services.AddSingleton(ConsumerRoutingMock.Object);
        builder.Services.AddSingleton(PlaybookOrchestratorMock.Object);
        builder.Services.AddSingleton(Options.Create(new AnalysisOptions { Enabled = true }));

        // Real AnalysisDocumentLoader over mocked boundaries (engine-path pre-load is
        // fail-soft: null document → orchestrator still invoked).
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(Mock.Of<IAnalysisDataverseService>());
        builder.Services.AddSingleton(Mock.Of<IDocumentDataverseService>());
        builder.Services.AddSingleton(Mock.Of<ISpeFileOperations>());
        builder.Services.AddSingleton(Mock.Of<ITextExtractor>());
        builder.Services.AddSingleton<ITenantCache>(
            new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache());
        builder.Services.AddSingleton<AnalysisDocumentLoader>();

        // Real DocumentProfileService over mocked Linear primitives — the Linear-branch
        // marker chunk ("document-profile:{id}") is emitted before any primitive is used.
        builder.Services.AddSingleton(ActionResolverMock.Object);
        builder.Services.AddSingleton(Mock.Of<IDocumentTextSource>());
        builder.Services.AddSingleton(Mock.Of<IActionRunner>());
        builder.Services.AddSingleton(Mock.Of<IPostUploadIndexingEnqueuer>());
        builder.Services.AddSingleton<DocumentProfileService>();

        // Real NotificationService (fire-and-forget completion notification path).
        builder.Services.AddSingleton(Mock.Of<IGenericEntityService>());
        builder.Services.AddSingleton<NotificationService>();

        // Sibling endpoints in the same MapAnalysisEndpoints group (continue/save/export/
        // get/resume) — parameter inference at map time requires the service type to be
        // registered even though these tests never invoke those routes.
        builder.Services.AddSingleton(Mock.Of<IAnalysisOrchestrationService>());

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
        ConsumerRoutingMock.Reset();
        PlaybookOrchestratorMock.Reset();
        ActionResolverMock.Reset();
        ConfigureDefaults();
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }

    private void ConfigureDefaults()
    {
        // Default: the document-profile Binding row resolves to the legacy playbook GUID —
        // mirrors the seeded spaarkedev1 row (sprk_playbook populated).
        ConsumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.DocumentProfile,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentProfilePlaybookId);

        // Linear branch: keep the pipeline short — action resolution fails fast, which the
        // DocumentProfileService surfaces as an error chunk AFTER the branch-marker metadata
        // chunk (the dispatch decision is what these tests pin).
        ActionResolverMock
            .Setup(a => a.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test fixture: no Action configured"));

        // Engine branch: empty event stream — the endpoint writes [DONE] after it.
        PlaybookOrchestratorMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyEventsAsync());
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EmptyEventsAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}
