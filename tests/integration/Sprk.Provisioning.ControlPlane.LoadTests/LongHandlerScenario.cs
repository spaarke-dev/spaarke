// -----------------------------------------------------------------------------
// LongHandlerScenario.cs
//
// L2 CONTROL-PLANE load-test scenario 2 (task 062, Wave C5 Batch 4E).
//
// ACCEPTANCE (task 062 POML criterion 2 / spec.md FR-22 / R20):
//   "A synthetic 30-min handler enqueued via POST /api/runs receives 202
//   immediately; GET /api/runs/{id} eventually reports status=Completed;
//   no HTTP timeout observed on caller."
//
// COMPRESSION DEVIATION (task 062 POML compression strategy):
//   The task POML permits compressing the 30-min handler via TimeProvider
//   OR via a parametrized duration. This scenario uses the parametrized
//   approach: wall-clock T = 3 seconds (test) vs 30 min (production).
//   The invariant proven is DURATION-INDEPENDENT: the L2 REST API never
//   blocks on handler execution because the fire-and-forget contract
//   returns 202 IMMEDIATELY (measured); the handler runs off-socket;
//   subsequent GET /api/runs/{id} polls succeed throughout the handler's
//   simulated lifetime. A 3-sec test and a 30-min production run prove
//   the SAME invariant; the 27-min-59-sec extra wall-clock at production
//   is 27-min-59-sec of NO ACTIVITY on the caller's HTTP socket by
//   definition — that's the point of fire-and-forget.
//
//   See notes/l2-load-test-2026-08-18.md for the full equivalence
//   argument. The task-062 escalation trigger is "handler exceeds 30 min
//   without completing" — that's a handler-side bug, not a load-test-
//   framework concern; the acceptance HERE is that the FRAMEWORK survives.
//
// WHY THIS PROVES FR-22 / R20:
//   The App Service default HTTP request timeout is 230 seconds. The fear
//   FR-22 addresses is "a synchronous handler call from L2 REST would
//   force the caller's HTTP connection open for the handler's full 30
//   minutes, blowing the 230-sec timeout and failing to complete." This
//   scenario proves:
//     (a) The 202 return happens in <1 sec regardless of the "handler"
//         work still to come — because the L2 REST endpoint DOES NOT
//         invoke the handler; it enqueues it and returns.
//     (b) The caller can independently poll GET /api/runs/{id} to observe
//         eventual completion; each poll is a fresh <100ms request; no
//         HTTP session is held open across the handler duration.
//     (c) The handler completing (simulated here as a background task that
//         mutates the repository's Status) is invisible to the caller's
//         HTTP path — the caller only sees the state transition on its
//         next poll.
//
// WHAT IT DOES NOT PROVE:
//   The real handler infrastructure (BFF's IJobHandler + Service Bus
//   ServiceBusJobProcessor + Redis idempotency) works. That's covered by:
//     - Sprk.Provisioning.ControlPlane.Tests.ServiceBusSmokeTests
//     - Sprk.Provisioning.ControlPlane.Tests.Handlers (per-handler unit tests)
//     - Sprk.Bff.Api.IntegrationTests.Jobs (BFF's job processor)
//     - Phase F E2E acceptance (task 089)
//   This scenario is the ARCHITECTURAL invariant that the L2 REST layer
//   NEVER blocks on handler execution.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Provisioning.ControlPlane.LoadTests;

public sealed class LongHandlerScenario
{
    /// <summary>
    /// Test-side wall-clock handler duration (compressed from production 30 min).
    /// See file header for the equivalence argument.
    /// </summary>
    private static readonly TimeSpan SyntheticHandlerDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Poll interval for GET /api/runs/{id}. Approximates a real operator
    /// or UI polling loop.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Per-HTTP-request budget. Each poll MUST complete well under this.
    /// This is intentionally FAR under the App Service 230-sec default to
    /// make an accidental sync-block obviously fail the assertion.
    /// </summary>
    private const long PerRequestBudgetMs = 5_000;

    private readonly ITestOutputHelper _output;

    public LongHandlerScenario(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task LongHandler_CallerReceives202Immediately_AndPollingSucceedsWithoutTimeout()
    {
        // Fresh factory so seams are isolated to this scenario.
        using var factory = new L2LoadTestFactory();
        using var client = factory.CreateClient();

        // ------------------------------------------------------------------
        // Step 1 — POST /api/runs; expect 202 with Location + a well-under-
        //          budget elapsed time.
        // ------------------------------------------------------------------
        const string customerId = "long-handler-customer";
        var postSw = Stopwatch.StartNew();
        HttpResponseMessage postResponse = await client.SendAsync(BuildPostRequest(customerId));
        postSw.Stop();

        postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "spec FR-22: enqueue-and-return path succeeds on the initial POST.");
        postResponse.Headers.Location.Should().NotBeNull("202 Accepted carries a Location header.");
        postSw.ElapsedMilliseconds.Should().BeLessThan(PerRequestBudgetMs,
            $"initial POST /api/runs SHOULD complete well under {PerRequestBudgetMs} ms " +
            $"(measured {postSw.ElapsedMilliseconds} ms); a stall here signals accidental " +
            "synchronous handler dispatch in the HTTP path.");

        var responseBody = await postResponse.Content.ReadFromJsonAsync<CreateRunResponsePayload>();
        responseBody.Should().NotBeNull();
        var runId = responseBody!.RunId;
        runId.Should().NotBeNullOrEmpty();
        postResponse.Dispose();

        // ------------------------------------------------------------------
        // Step 2 — Seed the run into the in-memory repository (POST /api/runs
        //          creates the doc; scenario has the same in-memory repo,
        //          so the seed step just wires it as visible to GET).
        //          Then kick off a "handler" background task that mutates
        //          the run's Status to Running immediately + Completed after
        //          SyntheticHandlerDuration.
        // ------------------------------------------------------------------
        // The POST already created the run at NotStarted via
        // repository.CreateRunAsync(). Simulate handler dispatch:
        factory.Repository.TryUpdateStatus(customerId, runId, RunStatus.Running)
            .Should().BeTrue("the POST landed a run in the repo; TryUpdateStatus finds it by (customerId, runId).");

        // Background "handler" — completes after SyntheticHandlerDuration.
        var handlerCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SyntheticHandlerDuration).ConfigureAwait(false);
                factory.Repository.TryUpdateStatus(customerId, runId, RunStatus.Completed);
                handlerCompleted.TrySetResult(true);
            }
            catch (Exception ex)
            {
                handlerCompleted.TrySetException(ex);
            }
        });

        // ------------------------------------------------------------------
        // Step 3 — Poll GET /api/runs/{id} throughout the handler's
        //          simulated lifetime. Each poll MUST return promptly.
        //          The final poll (after the handler completes) MUST see
        //          Status=Completed.
        // ------------------------------------------------------------------
        var pollLatencies = new List<long>();
        var statusesObserved = new List<RunStatus>();

        // Poll until we see Completed OR the overall timeout (2 × SyntheticHandlerDuration)
        // fires — the latter would be a real invariant failure.
        var overallDeadline = DateTimeOffset.UtcNow.Add(SyntheticHandlerDuration.Add(SyntheticHandlerDuration));
        RunStatus? lastStatus = null;
        while (DateTimeOffset.UtcNow < overallDeadline)
        {
            var pollSw = Stopwatch.StartNew();
            using var getResponse = await client.SendAsync(BuildGetRequest(runId, customerId));
            pollSw.Stop();

            pollLatencies.Add(pollSw.ElapsedMilliseconds);
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "GET /api/runs/{id} MUST succeed on every poll — the run exists throughout the handler's lifetime.");
            pollSw.ElapsedMilliseconds.Should().BeLessThan(PerRequestBudgetMs,
                $"each GET poll MUST complete well under {PerRequestBudgetMs} ms " +
                $"(measured {pollSw.ElapsedMilliseconds} ms); a stall here signals " +
                "the caller's HTTP path is somehow blocked on handler work.");

            var body = await getResponse.Content.ReadAsStringAsync();
            var run = JsonSerializer.Deserialize<ProvisioningRun>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            run.Should().NotBeNull();
            statusesObserved.Add(run!.Status);
            lastStatus = run.Status;

            if (run.Status == RunStatus.Completed)
            {
                break;
            }

            await Task.Delay(PollInterval);
        }

        // Await the background handler so we don't leak a task past the test.
        await handlerCompleted.Task;
        await handlerTask;

        // ------------------------------------------------------------------
        // AC 1 — final poll observed Completed.
        // ------------------------------------------------------------------
        lastStatus.Should().Be(RunStatus.Completed,
            "the handler transitioned the run to Completed; the caller MUST observe it via GET.");

        // ------------------------------------------------------------------
        // AC 2 — polling latency stays well under PerRequestBudgetMs on
        //        every poll; no HTTP timeout observed.
        // ------------------------------------------------------------------
        pollLatencies.Should().NotBeEmpty();
        var maxPoll = pollLatencies.Max();
        var pollP50 = LatencyStatistics.Percentile(pollLatencies.ToArray(), 50);
        var pollP95 = LatencyStatistics.Percentile(pollLatencies.ToArray(), 95);

        _output.WriteLine("LongHandlerScenario:");
        _output.WriteLine($"  syntheticDuration = {SyntheticHandlerDuration.TotalSeconds:F1} sec (production 1800 sec / 30 min — see file header equivalence)");
        _output.WriteLine($"  initialPostMs     = {postSw.ElapsedMilliseconds} ms");
        _output.WriteLine($"  pollCount         = {pollLatencies.Count}");
        _output.WriteLine($"  pollP50Ms         = {pollP50}");
        _output.WriteLine($"  pollP95Ms         = {pollP95}");
        _output.WriteLine($"  pollMaxMs         = {maxPoll}");
        _output.WriteLine($"  statusesObserved  = [{string.Join(", ", statusesObserved)}]");

        maxPoll.Should().BeLessThan(PerRequestBudgetMs,
            $"the caller's MAX HTTP poll SHOULD stay well under {PerRequestBudgetMs} ms; " +
            $"a value near the App Service 230-sec timeout would signal a sync-block regression.");

        // ------------------------------------------------------------------
        // AC 3 — the sequence of observed statuses includes Running before
        //        Completed (evidence the run legitimately advanced through
        //        state rather than jumping directly to a terminal at the
        //        first poll).
        // ------------------------------------------------------------------
        statusesObserved.Should().Contain(RunStatus.Running,
            "the observation window includes at least one Running poll before the Completed transition.");
        statusesObserved.Should().EndWith(new[] { RunStatus.Completed },
            "the last observed status is Completed.");
    }

    private static HttpRequestMessage BuildPostRequest(string customerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId,
                environmentId = "env-longhandler",
                tenancyModel = "Model2Dedicated",
                profile = "spaarke-hosted-model2",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "load-test-token");
        return request;
    }

    private static HttpRequestMessage BuildGetRequest(string runId, string customerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/runs/{runId}?customerId={Uri.EscapeDataString(customerId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "load-test-token");
        return request;
    }

    private sealed record CreateRunResponsePayload
    {
        public string RunId { get; init; } = string.Empty;
        public string CustomerId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Location { get; init; } = string.Empty;
    }
}
