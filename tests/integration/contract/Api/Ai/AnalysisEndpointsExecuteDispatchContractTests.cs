using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
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
/// endpoint-absorbed document-profile pipeline branch (task 044) when the client-submitted legacy
/// <c>PlaybookId</c> equals the Binding row's <c>sprk_playbook</c>.
/// </para>
/// <para>
/// <b>Hosting approach</b>: minimal in-process <see cref="WebApplication"/> mapping
/// <c>MapAnalysisEndpoints</c>. Module-boundary doubles (ADR-038):
/// <see cref="IConsumerRoutingService"/>, <see cref="IPlaybookOrchestrationService"/>,
/// <see cref="IActionResolver"/> and friends. The document-profile pipeline,
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
            "the Linear document-profile branch (FR-P3-01 replaces the config reverse-lookup)");
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

    // =========================================================================================
    // Terminal error event (ADR-019): a run that fails after the stream has started
    // =========================================================================================

    [Fact]
    public async Task Post_EngineRunFailsAfterTheFirstEvent_EndsWithExactlyOneSafeTerminalErrorChunk()
    {
        _fx.Reset();
        const string secret = "SENTINEL-upstream-provider-internal tools[3].function.parameters";
        _fx.PlaybookOrchestratorMock
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(FailsAfterFirstEvent(new InvalidOperationException(secret, new TimeoutException("SENTINEL-inner"))));

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { Guid.NewGuid() },
            playbookId = Guid.NewGuid(),
        });
        var body = await response.Content.ReadAsStringAsync();

        // The stream was already committed, so the failure surfaces as an SSE frame on a 200.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var chunks = ParseChunks(body);
        chunks.Select(c => c.GetProperty("type").GetString()).Should().Equal("metadata", "error");

        var error = chunks.Last();
        error.GetProperty("done").GetBoolean().Should().BeTrue();
        error.GetProperty("error").GetString().Should().Be(PlaybookStreamEvent.RunFailedMessage);
        error.GetProperty("errorCode").GetString().Should().Be("playbook.run-failed");
        error.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace(
            "ADR-019: the user can quote the correlation id so support finds the server log entry");

        // Nothing from the exception reaches the wire, and nothing follows the error.
        body.Should().NotContain("SENTINEL", "the exception and inner-exception messages must never reach the wire");
        body.Should().NotContain(nameof(InvalidOperationException), "the exception type must never reach the wire");
        body.Should().NotContain(nameof(TimeoutException), "the inner exception type must never reach the wire");
        body.Should().NotContain(" at Sprk.", "no stack frame may reach the wire");
        body.Should().NotContain("[DONE]", "the error chunk is the terminal event");
    }

    [Fact]
    public async Task Post_EngineRunEndsInItsOwnRunFailedEvent_ShowsTheError_AndSendsNoCompletionNotification()
    {
        _fx.Reset();
        var playbookId = Guid.NewGuid();
        _fx.PlaybookOrchestratorMock
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(EndsInRunFailed(playbookId));
        var notificationLookup = _fx.WatchForNotificationLookup();

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { Guid.NewGuid() },
            playbookId,
        });
        var body = await response.Content.ReadAsStringAsync();

        var chunks = ParseChunks(body);
        chunks.Select(c => c.GetProperty("type").GetString()).Should().Equal("metadata", "error");
        chunks.Last().GetProperty("error").GetString().Should().Be(PlaybookStreamEvent.RunFailedMessage);

        // The "AI analysis complete" notification is fire-and-forget (no handle to await), so give it a generous
        // window to start: a pre-fix run sends it within milliseconds of the response ending.
        var completed = await Task.WhenAny(notificationLookup, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().NotBeSameAs(notificationLookup,
            "a failed run must not tell the user their analysis is complete");
    }

    // =========================================================================================
    // Document Profile branch: failure must not read as success (ADR-019)
    // =========================================================================================

    [Fact]
    public async Task Post_DocumentProfileRunFails_ShowsTheError_AndSendsNoCompletionNotification()
    {
        // Default fixture: the Action cannot be resolved, so the pipeline yields an error chunk and ends (a failed
        // profile run - the same shape as "Document has no extractable text").
        _fx.Reset();
        var notificationLookup = _fx.WatchForNotificationLookup();

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { Guid.NewGuid() },
            playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId,
        });
        var chunks = ParseChunks(await response.Content.ReadAsStringAsync());

        chunks.Select(c => c.GetProperty("type").GetString()).Should().EndWith("error");
        chunks.Count(c => c.GetProperty("type").GetString() == "error").Should().Be(1);

        // The notification is fire-and-forget: give a pre-fix run (it sends within milliseconds) a generous window.
        var completed = await Task.WhenAny(notificationLookup, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().NotBeSameAs(notificationLookup,
            "a failed profile run must not tell the user their analysis is complete");
    }

    [Fact]
    public async Task Post_DocumentProfileUpstreamTimeout_EndsWithExactlyOneSafeTerminalErrorChunk()
    {
        // A timeout is a TaskCanceledException while the CLIENT is still connected: the pipeline rethrows it, and it
        // used to be swallowed as a "client disconnected", closing the stream with no error and no [DONE].
        _fx.Reset();
        _fx.ActionResolverMock
            .Setup(a => a.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("SENTINEL-HttpClient.Timeout elapsed"));
        var notificationLookup = _fx.WatchForNotificationLookup();

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { Guid.NewGuid() },
            playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId,
        });
        var body = await response.Content.ReadAsStringAsync();

        var chunks = ParseChunks(body);
        chunks.Count(c => c.GetProperty("type").GetString() == "error").Should().Be(1);
        var error = chunks.Last();
        error.GetProperty("type").GetString().Should().Be("error");
        error.GetProperty("error").GetString().Should().Be(PlaybookStreamEvent.RunFailedMessage);
        error.GetProperty("errorCode").GetString().Should().Be(PlaybookStreamEvent.RunFailedErrorCode);
        body.Should().NotContain("SENTINEL").And.NotContain(nameof(TaskCanceledException));
        body.Should().NotContain("[DONE]", "the error chunk is the terminal event");

        var completed = await Task.WhenAny(notificationLookup, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.Should().NotBeSameAs(notificationLookup, "a timed-out profile run is not a completed one");
    }

    [Fact]
    public async Task Post_DocumentProfileClientAborts_WritesNoErrorChunk()
    {
        // Control: when the CLIENT goes away the close stays silent - there is nobody to tell.
        _fx.Reset();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fx.ActionResolverMock
            .Setup(a => a.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (_, ct) =>
            {
                reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return default(AnalysisAction)!;
            });

        using var cts = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/analysis/execute")
        {
            Content = JsonContent.Create(new
            {
                documentIds = new[] { Guid.NewGuid() },
                playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId,
            }),
        };
        // The first chunk (the branch marker) is flushed before the Action is resolved, so the headers arrive first.
        var response = await _fx.CreateAuthenticatedClient()
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(10));

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();
        response.Dispose(); // dropping the connection is what aborts the server-side request

        await _fx.RequestHandled.WaitAsync(TimeSpan.FromSeconds(10));
        ParseChunks(_fx.ServerWire).Select(c => c.GetProperty("type").GetString()).Should().NotContain("error");
        _fx.ServerWire.Should().NotContain("[DONE]");
    }

    [Theory]
    [InlineData("{\"summary\":\"A summary\",\"tldr\":\"tldr\",\"documentType\":\"contract\"}", false)] // misconfigured schema
    [InlineData("{\"sprk_filetldr\":\"tldr\"}", true)] // control: one profile column is a profile
    public async Task Post_DocumentProfileWithNoMappableField_EndsInOneError_WritesNothing_AndSendsNoCompletionNotification(
        string aiOutput, bool mapsAProfileColumn)
    {
        // #2239: an AI output that maps no profile column used to write an empty update (just the computed file type
        // before that) and report Completed. It is a failed profile, as the app-only and Compose consumers treat it.
        _fx.Reset();
        var documentId = Guid.NewGuid();
        _fx.ActionResolverMock
            .Setup(a => a.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = Guid.NewGuid(), Name = "Document Profiler" });
        _fx.DocumentTextSourceMock
            .Setup(t => t.ExtractFromDocumentIdAsync(documentId, It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentText { DocumentId = documentId, FileName = "letter.pdf", ExtractedText = "Some text." });
        _fx.ActionRunnerMock
            .Setup(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<DocumentText>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonDocument.Parse(aiOutput).RootElement.Clone());
        var notificationLookup = _fx.WatchForNotificationLookup();

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { documentId },
            playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId,
        });
        var chunks = ParseChunks(await response.Content.ReadAsStringAsync());
        var types = chunks.Select(c => c.GetProperty("type").GetString()).ToList();

        if (mapsAProfileColumn)
        {
            types.Should().NotContain("error");
            _fx.DocumentServiceMock.Verify(d => d.UpdateDocumentFieldsAsync(
                documentId.ToString(),
                It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_filetldr") && f.ContainsKey("sprk_filetype")),
                It.IsAny<CancellationToken>()), Times.Once);
            return;
        }

        types.Should().EndWith("error");
        types.Count(t => t == "error").Should().Be(1);
        types.Should().NotContain("done");
        _fx.DocumentServiceMock.Verify(d => d.UpdateDocumentFieldsAsync(
            It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
        var completed = await Task.WhenAny(notificationLookup, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().NotBeSameAs(notificationLookup, "a profile that mapped nothing is not a completed analysis");
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("engine")]
    public async Task Post_AFailedRunWhoseTerminatorWriteThrows_StillEndsWithExactlyOneErrorChunk(string branch)
    {
        // The run already put its own error on the wire; the flush after [DONE] then fails (not a client abort). The handler's
        // catch-all must see the error is already there and not append a second terminal error.
        _fx.Reset();
        Guid playbookId;
        if (branch == "profile")
        {
            playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId;
        }
        else
        {
            playbookId = Guid.NewGuid();
            _fx.PlaybookOrchestratorMock
                .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Returns(EndsInRunFailed(playbookId));
        }

        _fx.ThrowWhenTheTerminatorIsWritten = true;
        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { Guid.NewGuid() },
            playbookId,
        });
        await response.Content.ReadAsStringAsync();

        ParseChunks(_fx.ServerWire).Count(c => c.GetProperty("type").GetString() == "error").Should().Be(1,
            "ADR-019: a stream ends in exactly one terminal error");
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("engine-run-failed")]
    [InlineData("engine-node-failed")]
    public async Task Post_AFailureWhoseTextCarriesASecret_NeverPutsTheSecretOnTheWire(string scenario)
    {
        const string leaky = "Upstream rejected the request: client_secret=SUPERSECRET";
        _fx.Reset();
        var playbookId = AnalysisExecuteDispatchTestFixture.DocumentProfilePlaybookId;
        if (scenario == "profile")
        {
            _fx.ActionResolverMock
                .Setup(a => a.ResolveAsync(ConsumerTypes.DocumentProfile, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException(leaky));
        }
        else
        {
            playbookId = Guid.NewGuid();
            _fx.PlaybookOrchestratorMock
                .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Returns(scenario == "engine-run-failed" ? FailsWith(playbookId, leaky) : NodeFailsWith(playbookId, leaky));
        }

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/execute", new
        {
            documentIds = new[] { Guid.NewGuid() },
            playbookId,
        });
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain("SUPERSECRET", "a credential value must never reach the SSE stream");
        body.Should().Contain("client_secret=[redacted]", "the secret NAME stays so the failure is diagnosable");
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> FailsWith(Guid playbookId, string error)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        await Task.Yield();
        yield return PlaybookStreamEvent.RunFailed(runId, playbookId, error);
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> NodeFailsWith(Guid playbookId, string error)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        await Task.Yield();
        yield return PlaybookStreamEvent.NodeFailed(runId, playbookId, Guid.NewGuid(), "Step", error);
    }

    private static List<JsonElement> ParseChunks(string body)
    {
        var chunks = new List<JsonElement>();
        foreach (var frame in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            frame.Should().StartWith("data: ");
            var payload = frame["data: ".Length..];
            if (payload == "[DONE]")
            {
                continue;
            }

            chunks.Add(JsonDocument.Parse(payload).RootElement.Clone());
        }

        return chunks;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> FailsAfterFirstEvent(Exception toThrow)
    {
        yield return PlaybookStreamEvent.RunStarted(Guid.NewGuid(), Guid.NewGuid(), 1);
        await Task.Yield();
        throw toThrow;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EndsInRunFailed(Guid playbookId)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        await Task.Yield();
        yield return PlaybookStreamEvent.RunFailed(runId, playbookId, "The playbook run failed. See the server log for details.");
    }
}

/// <summary>
/// Test fixture hosting a minimal <see cref="WebApplication"/> with
/// <c>MapAnalysisEndpoints</c> and the /execute handler's dependency graph.
/// Reuses the sibling summarize fixture's fake auth handler.
/// </summary>
public sealed class AnalysisExecuteDispatchTestFixture : IAsyncLifetime, IDisposable
{
    /// <summary>Task 146: the owner resolver double (every create resolves its owner).</summary>
    public Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble Ownership { get; } = new();

    /// <summary>Mirrors the seeded spaarkedev1 document-profile Binding row's sprk_playbook.</summary>
    public static readonly Guid DocumentProfilePlaybookId =
        Guid.Parse("18cf3cc8-02ec-f011-8406-7c1e520aa4df");

    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();
    public Mock<IPlaybookOrchestrationService> PlaybookOrchestratorMock { get; } = new();
    public Mock<IActionResolver> ActionResolverMock { get; } = new();

    /// <summary>#2239: the profile pipeline's text, run and write seams (loose; the default returns nothing).</summary>
    public Mock<IDocumentTextSource> DocumentTextSourceMock { get; } = new();
    public Mock<IActionRunner> ActionRunnerMock { get; } = new();
    public Mock<IDocumentDataverseService> DocumentServiceMock { get; } = new();

    /// <summary>The Dataverse boundary of the fire-and-forget "analysis complete" notification.</summary>
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new();

    /// <summary>
    /// Completes when the notification path first looks up the caller's systemuser - the first thing it does. Used to
    /// observe whether a run sent the completion notification.
    /// </summary>
    public Task WatchForNotificationLookup()
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EntityServiceMock.Reset();
        EntityServiceMock
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback(() => seen.TrySetResult())
            .ReturnsAsync(new EntityCollection());
        return seen.Task;
    }

    /// <summary>Every byte the handler wrote to the response body, as the server wrote it (survives a client abort).</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _serverWrites = new();

    public string ServerWire => string.Concat(_serverWrites);

    /// <summary>When set, the flush after the SSE <c>[DONE]</c> terminator throws (a non-abort I/O failure after the body began).</summary>
    public bool ThrowWhenTheTerminatorIsWritten { get; set; }

    private TaskCompletionSource _requestHandled = NewSignal();

    /// <summary>Completes when the handler of the current test's request has returned.</summary>
    public Task RequestHandled => _requestHandled.Task;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        // Task 162: /execute chains the run filter (Write on the documents for the document-profile branch or a
        // playbook that can write; the playbook-use decision otherwise). These tests pin the DISPATCH decision, so
        // every seam answers an explicit ALLOW; the deny cases are AnalysisEndpointsAuthorizationContractTests.
        builder.Services.AddAllowAllAnalysisAuthorization();

        // /execute handler dependency graph — module-boundary doubles + real concretes.
        builder.Services.AddSingleton(ConsumerRoutingMock.Object);
        builder.Services.AddSingleton(PlaybookOrchestratorMock.Object);
        builder.Services.AddSingleton(Options.Create(new AnalysisOptions { Enabled = true }));

        // Real AnalysisDocumentLoader over mocked boundaries (engine-path pre-load is
        // fail-soft: null document → orchestrator still invoked).
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(Mock.Of<IAnalysisDataverseService>());
        builder.Services.AddSingleton(DocumentServiceMock.Object);
        builder.Services.AddSingleton(Mock.Of<ISpeFileOperations>());
        builder.Services.AddSingleton(Mock.Of<ITextExtractor>());
        builder.Services.AddSingleton<ITenantCache>(
            new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache());
        // Task 171: the loader reads document bytes app-only after the document-pointer check.
        builder.Services.AddSingleton(TestRecordContainerResolver.ForBusinessUnitContainers(c => true));
        builder.Services.AddSingleton<AnalysisDocumentLoader>();

        // FR-P3-05 (task 044): the endpoint composes the executor primitives directly
        // (the wrapper class was absorbed) — mocked Linear primitives; the Linear-branch
        // marker chunk ("document-profile:{id}") is emitted before any primitive is used.
        builder.Services.AddSingleton(ActionResolverMock.Object);
        builder.Services.AddSingleton(DocumentTextSourceMock.Object);
        builder.Services.AddSingleton(ActionRunnerMock.Object);
        builder.Services.AddSingleton(Mock.Of<IPostUploadIndexingEnqueuer>());

        // Real NotificationService (fire-and-forget completion notification path).
        builder.Services.AddSingleton(EntityServiceMock.Object);
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(Ownership); // task 146 — the owner resolver at its module boundary
        builder.Services.AddSingleton<NotificationService>();

        // Sibling endpoints in the same MapAnalysisEndpoints group (save/export/get) —
        // parameter inference at map time requires the service type to be registered even
        // though these tests never invoke those routes. (continue/resume retired —
        // ai-advanced-capabilities-analysis-hub-r1 task 062, spec §13.5/FR-20, 2026-07-29.)
        builder.Services.AddSingleton(Mock.Of<IAnalysisOrchestrationService>());

        // Fork/Promote sibling endpoints take the concrete ChatSessionManager plus
        // IChatDataverseRepository. Any complex param (concrete OR interface) that is NOT a
        // registered service is mis-inferred as a second request body → "Failure to infer ..."
        // at MapAnalysisEndpoints time. These tests never invoke Fork/Promote, so doubles that
        // are never resolved at request time suffice to satisfy map-time inference.
        builder.Services.AddSingleton(Mock.Of<IChatDataverseRepository>());
        builder.Services.AddSingleton<ChatSessionManager>(sp => new ChatSessionManager(
            sp.GetRequiredService<ITenantCache>(),
            sp.GetRequiredService<IChatDataverseRepository>(),
            sp.GetRequiredService<ILogger<ChatSessionManager>>()));

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        // Records what the handler writes, observes when it has returned, and can make the [DONE] write fail.
        _app.Use(async (ctx, next) =>
        {
            var inner = ctx.Response.Body;
            ctx.Response.Body = new RecordingBodyStream(inner, this);
            try
            {
                await next(ctx);
            }
            finally
            {
                ctx.Response.Body = inner;
                _requestHandled.TrySetResult();
            }
        });

        _app.MapAnalysisEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void Reset()
    {
        _serverWrites.Clear();
        ThrowWhenTheTerminatorIsWritten = false;
        _requestHandled = NewSignal();
        ConsumerRoutingMock.Reset();
        PlaybookOrchestratorMock.Reset();
        ActionResolverMock.Reset();
        DocumentTextSourceMock.Reset();
        ActionRunnerMock.Reset();
        DocumentServiceMock.Reset();
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
        // the document-profile pipeline surfaces as an error chunk AFTER the branch-marker metadata
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

    /// <summary>Delegating write-only stream that records each write and can fail the SSE terminator write.</summary>
    private sealed class RecordingBodyStream(Stream inner, AnalysisExecuteDispatchTestFixture owner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            // Fails the flush that follows the [DONE] terminator (a stream-level I/O failure, not a client abort). Failing
            // the terminator write itself would fault the response pipe and hide a second error write.
            if (owner.ThrowWhenTheTerminatorIsWritten && owner.ServerWire.EndsWith("[DONE]\n\n", StringComparison.Ordinal))
            {
                throw new IOException("simulated failure flushing the SSE terminator");
            }

            return inner.FlushAsync(cancellationToken);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var text = System.Text.Encoding.UTF8.GetString(buffer.Span);
            owner._serverWrites.Enqueue(text);
            await inner.WriteAsync(buffer, cancellationToken);
        }
    }
}
