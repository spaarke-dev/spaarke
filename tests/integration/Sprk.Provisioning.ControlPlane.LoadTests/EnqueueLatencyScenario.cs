// -----------------------------------------------------------------------------
// EnqueueLatencyScenario.cs
//
// L2 CONTROL-PLANE load-test scenario 1 (task 062, Wave C5 Batch 4E).
//
// ACCEPTANCE (task 062 POML criterion 1 / spec.md FR-22 / R20):
//   "N=50 concurrent POST /api/runs -> p95 latency <100ms; 100% responses
//   are 202 Accepted with Location header."
//
// WHAT IT MEASURES:
//   The endpoint's OWN work — the time from HTTP send-line to 202-received
//   over an in-process TestServer. Excludes real Cosmos + Service Bus
//   network round-trips (those are exercised by CosmosSmokeTests +
//   ServiceBusSmokeTests + the NightlyTests project against dev
//   infrastructure). The FR-22 <100ms budget is a PRODUCTION target on the
//   deployed L2 App Service; in a CI/local test host with in-memory seams
//   the endpoint completes in ~1-5ms typically — so a p95 breach here
//   would signal a regression in the endpoint's own hot path (accidental
//   synchronous I/O, unbounded serialization, DI resolution regression).
//
// CI HEADROOM:
//   Assertion ceiling is p95 <= 250ms (matching the RunsEndpointsTests
//   Latency spot-check headroom). The actual production budget is <100ms
//   (spec.md FR-22); the CI slack accounts for shared-runner variance.
//
// REPORTING:
//   Emits p50/p95/p99/mean/max/min to xUnit ITestOutputHelper so
//   `dotnet test -v n` writes them to the console; the numbers are
//   copy-pasted into notes/l2-load-test-2026-08-18.md by the task-062
//   author (single-shot; not automated — the report is a point-in-time
//   artifact per the POML deliverable spec).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Provisioning.ControlPlane.LoadTests;

public sealed class EnqueueLatencyScenario : IClassFixture<L2LoadTestFactory>
{
    private const int ConcurrencyN = 50;
    private const int WarmupN = 3;

    /// <summary>
    /// CI ceiling — see file header. Production budget is <100ms per FR-22;
    /// unit-test env is expected in the low single-digit ms with in-memory seams.
    /// </summary>
    private const long P95CeilingMs = 250;

    private readonly L2LoadTestFactory _factory;
    private readonly ITestOutputHelper _output;

    public EnqueueLatencyScenario(L2LoadTestFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task PostRuns_N50Concurrent_AllReturn202_p95Under250ms()
    {
        var client = _factory.CreateClient();

        // Warm-up so JIT + DI compilation is out of the timed window.
        for (var i = 0; i < WarmupN; i++)
        {
            using var warmup = BuildRequest($"warmup-{i}");
            using var warmupResponse = await client.SendAsync(warmup);
            warmupResponse.EnsureSuccessStatusCode();
        }

        // Snapshot the shared-fixture repo + enqueuer counters AFTER warm-up
        // so we assert on the DELTA induced by the 50 timed requests. The
        // factory is a class-fixture (IClassFixture) so warm-up + prior test
        // runs contribute to the running totals — the delta is the semantic
        // invariant.
        var preCreated = _factory.Repository.CreatedCount;
        var preEnqueued = _factory.Enqueuer.Count;

        // Fire N concurrent requests and record per-request latency.
        var samples = new long[ConcurrencyN];
        var statuses = new HttpStatusCode[ConcurrencyN];
        string?[] locations = new string?[ConcurrencyN];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, ConcurrencyN),
            new ParallelOptions { MaxDegreeOfParallelism = ConcurrencyN },
            async (i, ct) =>
            {
                var sw = Stopwatch.StartNew();
                using var request = BuildRequest($"customer-load-{i}");
                using var response = await client.SendAsync(request, ct);
                sw.Stop();
                samples[i] = sw.ElapsedMilliseconds;
                statuses[i] = response.StatusCode;
                locations[i] = response.Headers.Location?.OriginalString;
            });

        // ------------------------------------------------------------------
        // AC 1 — 100% responses are 202 Accepted with a Location header.
        // ------------------------------------------------------------------
        statuses.Should().OnlyContain(s => s == HttpStatusCode.Accepted,
            "spec FR-22 acceptance: enqueue-and-return path MUST NOT fail under N-concurrent load.");
        locations.Should().OnlyContain(loc => loc != null && loc.StartsWith("/api/runs/"),
            "spec FR-22 acceptance: every 202 carries a Location header pointing at GET /api/runs/{id}.");

        // ------------------------------------------------------------------
        // AC 2 — p95 latency under the CI ceiling (production budget is
        // <100ms per FR-22; CI ceiling is <=250ms to absorb shared-runner
        // variance — see file header).
        // ------------------------------------------------------------------
        var p50 = LatencyStatistics.Percentile(samples, 50);
        var p95 = LatencyStatistics.Percentile(samples, 95);
        var p99 = LatencyStatistics.Percentile(samples, 99);
        var mean = LatencyStatistics.Mean(samples);
        var max = samples.Max();
        var min = samples.Min();

        _output.WriteLine("EnqueueLatencyScenario N=50 concurrent POST /api/runs:");
        _output.WriteLine($"  min       = {min} ms");
        _output.WriteLine($"  p50       = {p50} ms");
        _output.WriteLine($"  mean      = {mean:F2} ms");
        _output.WriteLine($"  p95       = {p95} ms   (CI ceiling {P95CeilingMs} ms; production budget <100 ms per FR-22)");
        _output.WriteLine($"  p99       = {p99} ms");
        _output.WriteLine($"  max       = {max} ms");
        _output.WriteLine($"  200-count = {statuses.Count(s => s == HttpStatusCode.Accepted)}/{ConcurrencyN}");

        p95.Should().BeLessOrEqualTo(P95CeilingMs,
            $"POST /api/runs p95 SHOULD be <= {P95CeilingMs} ms in CI ({p95} ms measured); production target is <100 ms per FR-22.");

        // ------------------------------------------------------------------
        // AC 3 — every 202 corresponds to a Cosmos row create + Service Bus
        // enqueue. Delta on the shared-fixture repo/enqueuer counters is
        // ConcurrencyN (the 50 timed requests); warm-up requests already
        // committed to the counters before the snapshot above.
        // ------------------------------------------------------------------
        var deltaCreated = _factory.Repository.CreatedCount - preCreated;
        var deltaEnqueued = _factory.Enqueuer.Count - preEnqueued;

        deltaCreated.Should().Be(ConcurrencyN,
            "each 202 Accepted must land ONE ProvisioningRun row in Cosmos (in-memory repo verifies).");
        deltaEnqueued.Should().Be(ConcurrencyN,
            "each 202 Accepted must fire exactly ONE Service Bus enqueue (H0 preflight per design.md §4.1).");
        _factory.Enqueuer.Enqueued.Should().OnlyContain(e => e.HandlerId == "H0",
            "the initial-dispatch per design.md §4.1 DAG is H0 preflight (task 057 endpoint invariant).");
    }

    private static HttpRequestMessage BuildRequest(string customerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs")
        {
            Content = JsonContent.Create(new
            {
                customerId,
                environmentId = "env-1",
                tenancyModel = "Model1Shared",
                profile = "spaarke-hosted-model1-trial",
                nonSecretParameters = new Dictionary<string, string>
                {
                    ["display-name"] = $"LoadTest-{customerId}",
                },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "load-test-token");
        return request;
    }
}
