// Through-the-wire contract tests for the terminal error event of the playbook run SSE streams
// (KEEP path: endpoint-contract, ADR-038 section 2; ADR-019 "emit a terminal error event for SSE failures").
//
// WHAT THEY PIN
//   A run stream that fails AFTER it has started (status and content type are already on the wire, so it can no
//   longer become an HTTP error) ends with EXACTLY ONE safe terminal event: fixed message + stable errorCode +
//   correlationId, no exception type / message / stack, and nothing after it. The client going away stays silent.
//   Mirrors ChatMessageStreamingContractTests "Error mid-stream".
//
// WHAT IS REAL AND WHAT IS SUBSTITUTED
//   The route mappers, endpoint filters, authorization stack and SSE writers are the production types
//   (PlaybookAuthHost, shared with PlaybookRouteAuthorizationContractTests). The substitution is the playbook
//   orchestration service interface: its event stream is scripted, which is where a run fails in production.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

public class PlaybookRunStreamTerminalErrorContractTests
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000164";
    private const string Secret = "SENTINEL-upstream-provider-internal tools[3].function.parameters";

    /// <summary>Upper bound for waits on server-side signals - a deadlock guard, never asserted as a latency.</summary>
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private static readonly int RunStartedType = (int)PlaybookEventType.RunStarted;
    private static readonly int RunFailedType = (int)PlaybookEventType.RunFailed;
    private static readonly int RunCompletedType = (int)PlaybookEventType.RunCompleted;

    // =========================================================================================
    // POST /api/ai/playbooks/{id}/execute
    // =========================================================================================

    [Fact]
    public async Task Execute_RunFailsAfterTheFirstEvent_EndsWithExactlyOneSafeTerminalErrorEvent()
    {
        await using var host = await PlaybookRouteAuthorizationContractTests.PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var runId = Guid.NewGuid();
        host.Orchestration
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(FailsAfterFirstEvent(runId, playbookId, new InvalidOperationException(Secret)));

        var response = await host.SendAsync(Execute(playbookId, documentId));
        var body = await response.Content.ReadAsStringAsync();

        // The stream was already committed, so the failure surfaces as an SSE frame on a 200 - not a torn connection.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var frames = ParseFrames(body);
        frames.Select(f => f.Type).Should().Equal(RunStartedType, RunFailedType);

        // Terminal, safe and correlated.
        var error = frames.Last().Json;
        error.GetProperty("done").GetBoolean().Should().BeTrue();
        error.GetProperty("error").GetString().Should().Be(PlaybookStreamEvent.RunFailedMessage);
        error.GetProperty("errorCode").GetString().Should().Be("playbook.run-failed");
        error.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace(
            "ADR-019: the user can quote the correlation id so support finds the server log entry");
        error.GetProperty("runId").GetGuid().Should().Be(runId, "the error names the run the stream had already started");
        error.GetProperty("playbookId").GetGuid().Should().Be(playbookId);

        // ADR-019: nothing from the exception reaches the wire.
        body.Should().NotContain("SENTINEL", "the exception message must never reach the wire");
        body.Should().NotContain(nameof(InvalidOperationException), "the exception type must never reach the wire");
        body.Should().NotContain("   at Sprk.", "no stack frame may reach the wire");
        body.Should().NotContain("   at System.", "no stack frame may reach the wire");
    }

    [Fact]
    public async Task Execute_RunIsCancelledByAnUpstreamTimeoutNotTheClient_EndsWithTheTerminalErrorEvent()
    {
        // A TaskCanceledException from an upstream HttpClient timeout is a FAILURE: the client did not leave, so the
        // stream must not close silently.
        await using var host = await PlaybookRouteAuthorizationContractTests.PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        host.Orchestration
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(FailsAfterFirstEvent(Guid.NewGuid(), playbookId, new TaskCanceledException(Secret)));

        var response = await host.SendAsync(Execute(playbookId, documentId));
        var body = await response.Content.ReadAsStringAsync();

        var frames = ParseFrames(body);
        frames.Select(f => f.Type).Should().Equal(RunStartedType, RunFailedType);
        frames.Last().Json.GetProperty("errorCode").GetString().Should().Be("playbook.run-failed");
        body.Should().NotContain("SENTINEL");
    }

    [Fact]
    public async Task Execute_RunAlreadyEndedWithItsOwnTerminalEventThenThrows_AddsNoSecondTerminalEvent()
    {
        await using var host = await PlaybookRouteAuthorizationContractTests.PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var runId = Guid.NewGuid();
        host.Orchestration
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(EndsThenThrows(runId, playbookId, new InvalidOperationException(Secret)));

        var response = await host.SendAsync(Execute(playbookId, documentId));
        var body = await response.Content.ReadAsStringAsync();

        ParseFrames(body).Select(f => f.Type).Should().Equal(RunStartedType, RunCompletedType);
        body.Should().NotContain("SENTINEL");
    }

    [Fact]
    public async Task Execute_ClientAbortsMidStream_WritesNoErrorEvent_AndTheCancellationReachesTheRun()
    {
        var logs = new RecordingLoggerProvider();
        await using var host = await PlaybookRouteAuthorizationContractTests.PlaybookAuthHost.StartAsync(
            services => services.AddSingleton<ILoggerProvider>(logs));
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var script = new CancellationScript();
        host.Orchestration
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns((PlaybookRunRequest r, HttpContext _, CancellationToken ct) => script.RunAsync(r.PlaybookId, ct));

        using var abort = new CancellationTokenSource();
        using var response = await host.SendAsync(Execute(playbookId, documentId), HttpCompletionOption.ResponseHeadersRead, abort.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The run is parked after its first event; the client goes away.
        await script.FirstEventYielded.WaitAsync(HangGuard);
        abort.Cancel();
        response.Dispose();

        (await script.CancellationObserved.WaitAsync(HangGuard)).Should().BeTrue(
            "the request-abort token must flow through the endpoint into the run");
        await logs.WaitForAsync("Client disconnected during playbook execution", HangGuard);
        logs.Messages.Should().NotContain(m => m.Contains("Error during playbook execution", StringComparison.Ordinal),
            "a client abort is not a failure: no error path, so no error event");
    }

    // =========================================================================================
    // GET /api/ai/playbooks/runs/{runId}/stream
    // =========================================================================================

    [Fact]
    public async Task StreamRun_FailsAfterTheStreamHasStarted_EndsWithExactlyOneSafeTerminalErrorEvent()
    {
        // The reconnect stream commits to SSE before it writes anything; a failure from then on cannot become an
        // HTTP error. The logger of the "Client reconnecting" line is the scripted failure point (the first thing
        // that runs once the stream is committed), and its exception text is the secret that must not leak.
        await using var host = await PlaybookRouteAuthorizationContractTests.PlaybookAuthHost.StartAsync(
            services => services.AddSingleton<ILoggerProvider>(new ThrowingLoggerProvider("Client reconnecting", Secret)));
        var runId = Guid.NewGuid();
        host.RunStatus(runId, startedByOid: CallerOid);

        var response = await host.SendAsync(Get($"/api/ai/playbooks/runs/{runId}/stream"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var frames = ParseFrames(body);
        frames.Select(f => f.Type).Should().Equal(RunFailedType);
        var error = frames.Single().Json;
        error.GetProperty("done").GetBoolean().Should().BeTrue();
        error.GetProperty("error").GetString().Should().Be(PlaybookStreamEvent.RunFailedMessage);
        error.GetProperty("errorCode").GetString().Should().Be("playbook.run-failed");
        error.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        body.Should().NotContain("SENTINEL");
        body.Should().NotContain("   at ");
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static HttpRequestMessage Execute(Guid playbookId, Guid documentId)
    {
        var request = Authenticated(HttpMethod.Post, $"/api/ai/playbooks/{playbookId}/execute");
        request.Content = JsonContent.Create(new { documentIds = new[] { documentId } });
        return request;
    }

    private static HttpRequestMessage Get(string path) => Authenticated(HttpMethod.Get, path);

    private static HttpRequestMessage Authenticated(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, CallerOid);
        return request;
    }

    private sealed record SseFrame(string RawJson, JsonElement Json)
    {
        /// <summary>The numeric <see cref="PlaybookEventType"/> (the stream serializes the enum as a number).</summary>
        public int Type => Json.GetProperty("type").GetInt32();
    }

    private static List<SseFrame> ParseFrames(string body)
    {
        body.Should().EndWith("\n\n", "every SSE frame is terminated by a blank line");
        var frames = new List<SseFrame>();
        foreach (var chunk in body[..^2].Split("\n\n"))
        {
            chunk.Should().StartWith("data: ", "the run stream uses unnamed `data:` frames only");
            var json = chunk["data: ".Length..];
            frames.Add(new SseFrame(json, JsonDocument.Parse(json).RootElement.Clone()));
        }

        return frames;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> FailsAfterFirstEvent(
        Guid runId, Guid playbookId, Exception toThrow)
    {
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        await Task.Yield();
        throw toThrow;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EndsThenThrows(
        Guid runId, Guid playbookId, Exception toThrow)
    {
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        await Task.Yield();
        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
        await Task.Yield();
        throw toThrow;
    }

    /// <summary>Yields one event, then parks until the caller's token is cancelled, recording that it was.</summary>
    private sealed class CancellationScript
    {
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstEventYielded => _first.Task;

        public Task<bool> CancellationObserved => _observed.Task;

        public async IAsyncEnumerable<PlaybookStreamEvent> RunAsync(
            Guid playbookId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return PlaybookStreamEvent.RunStarted(Guid.NewGuid(), playbookId, 1);
            _first.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                _observed.TrySetResult(true);
                throw;
            }
        }
    }

    /// <summary>Keeps every log message of the host so a test can assert which handler path ran.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(_messages);

        public async Task WaitForAsync(string fragment, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (!Messages.Any(m => m.Contains(fragment, StringComparison.Ordinal)))
            {
                await Task.Delay(10, cts.Token);
            }
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(System.Collections.Concurrent.ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => sink.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>Throws from the logger when a message contains the fragment: the scripted failure point.</summary>
    private sealed class ThrowingLoggerProvider(string fragment, string exceptionText) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ThrowingLogger(fragment, exceptionText);

        public void Dispose()
        {
        }

        private sealed class ThrowingLogger(string fragment, string exceptionText) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter(state, exception).Contains(fragment, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(exceptionText);
                }
            }
        }
    }
}
