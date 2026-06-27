// R3 Part 1D — Task 056 (2026-06-21): Canary perf integration test for the
// transitive memberships surface (`GET /api/users/me/memberships/{entityType}?includeRelated=...`).
//
// Spec authority:
//   - NFR-04: p95 ≤300ms for the membership endpoint, MEASURED VIA APPLICATION
//     INSIGHTS server-side request telemetry (decision 2026-06-20, AC-1A.5).
//   - AC-1D.2: transitive query maintains p95 ≤300ms or documents the limit
//     explicitly.
//
// Scope of this test (per task 056 brief + owner clarification carried from
// task 054 completion notes):
//
//   This is a CANARY load test for catching in-process pipeline regressions
//   before deploy — it is NOT the production NFR-04 measurement (which is
//   done server-side via App Insights per AC-1A.5). The mocked
//   IMembershipResolverService returns instantly, so the measured latency
//   isolates the BFF pipeline overhead (auth, routing, JSON serialization at
//   transitive scale, ProblemDetails handling) at a fixture scale that
//   matches the NFR-04 envelope (500 memberships, ~2500 transitive IDs).
//
// Fixture scale:
//   - 500 seeded "matter" memberships (matches NFR-04 envelope).
//   - 5 documents per matter → ~2500 transitive document IDs in one response.
//   - Single representative role bucket per primary + per related.
//
// Measurement protocol:
//   - 10 warmup iterations  → timings discarded (let WAF JIT/cache settle).
//   - 100 measured iterations → per-call elapsed-ms via Stopwatch.
//   - Compute p50, p95, p99, max, mean from the 100-sample array.
//
// Gates:
//   - HARD GATE (asserted, stable across runners): p95 < 3000ms — matches
//     task 054's gross-regression ceiling. CI never flakes here.
//   - SOFT TARGET (logged ONLY, never asserted):
//       * p95 < 300ms   → log "NFR-04 target met in-process".
//       * 300ms ≤ p95   → log "NFR-04 target exceeded in-process; production
//                          p95 measured via App Insights per AC-1A.5".
//     Soft target is diagnostic — perf tests are environment-sensitive,
//     so we do not flake CI on a tighter target. Documented limit (if any)
//     is recorded in the POML completion-notes and spec.md AC-1D.2.
//
// Coordination with sibling tests:
//   - tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/
//     TransitiveMembershipTests.cs::GetMembership_PerformanceWithinBudget
//     (task 054) is a single-request gross-regression check; this test
//     adds the 100-iteration distributional measurement at scale.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md NFR-04,
//            AC-1D.2, AC-1A.5; design.md Part 1 § Endpoint contract;
//            task 054 completion notes; bff-extensions.md §A.

using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.IntegrationTests.Membership;

/// <summary>
/// Canary perf integration test for R3 Part 1D / NFR-04. Runs 100 measured
/// iterations of the transitive memberships endpoint at NFR-04 envelope scale
/// (500 memberships, ~2500 transitive IDs) and asserts a stable
/// gross-regression gate (p95 &lt; 3000ms). Diagnostic soft-target (300ms)
/// reporting is logged via <see cref="ITestOutputHelper"/> and is NOT
/// asserted — production NFR-04 measurement is server-side via App Insights
/// per AC-1A.5.
/// </summary>
public sealed class TransitiveMembershipPerfTests : IClassFixture<TransitiveMembershipIntegrationFixture>
{
    /// <summary>NFR-04 envelope: ≤500 memberships on a 50K-row entity.</summary>
    private const int SeededPrimaryCount = 500;

    /// <summary>Five documents per matter → ~2500 transitive document IDs total.</summary>
    private const int DocumentsPerPrimary = 5;

    /// <summary>Discarded — let WAF JIT, route table, JSON metadata cache, etc. settle.</summary>
    private const int WarmupIterations = 10;

    /// <summary>Sample size for percentile measurement (p50/p95/p99/max/mean).</summary>
    private const int MeasuredIterations = 100;

    /// <summary>Hard gross-regression gate (asserted). Stable across runners; does NOT flake.</summary>
    private const int HardCeilingMs = 3000;

    /// <summary>Soft NFR-04 target (logged only; never asserted — perf tests are env-sensitive).</summary>
    private const int SoftTargetMs = 300;

    private readonly TransitiveMembershipIntegrationFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TransitiveMembershipPerfTests(
        TransitiveMembershipIntegrationFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task GetMembership_WithTransitive_PerformanceCanary_500Memberships()
    {
        // Arrange: seed AAD oid → systemuser lookup and a resolver response at
        // NFR-04 envelope scale (500 primary + ~2500 transitive document IDs).
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        var primaryIds = Enumerable.Range(0, SeededPrimaryCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var transitiveDocIds = Enumerable.Range(0, SeededPrimaryCount * DocumentsPerPrimary)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var resolverResponse = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(systemUserId),
            Ids: primaryIds,
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["owner"] = primaryIds,
            },
            Count: primaryIds.Length,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            ContinuationToken: null,
            RelatedByRole: new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>
            {
                ["sprk_document"] = new Dictionary<string, IReadOnlyList<Guid>>
                {
                    ["matter"] = transitiveDocIds,
                },
            });

        _fixture.ResolverMock.Reset();
        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(
                systemUserId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolverResponse);

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        const string url = "/api/users/me/memberships/sprk_matter?includeRelated=sprk_document";

        // Warmup — discarded timings. Lets WAF JIT, ASP.NET route table, JSON
        // metadata cache, ProblemDetails pipeline, and auth handler settle.
        for (var i = 0; i < WarmupIterations; i++)
        {
            using var warmupResponse = await client.GetAsync(url);
            warmupResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "warmup iterations must succeed before measurement");
        }

        // Act — 100 measured iterations.
        var samples = new long[MeasuredIterations];
        for (var i = 0; i < MeasuredIterations; i++)
        {
            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(url);
            sw.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                $"measured iteration {i} must succeed (perf measurement only valid for successful requests)");

            samples[i] = sw.ElapsedMilliseconds;
        }

        // Compute distributional statistics.
        var p50 = Percentile(samples, 50);
        var p95 = Percentile(samples, 95);
        var p99 = Percentile(samples, 99);
        var max = samples.Max();
        var mean = samples.Average();

        _output.WriteLine("=== Transitive Membership Perf Canary (Task 056) ===");
        _output.WriteLine($"Fixture: {SeededPrimaryCount} primary memberships, " +
            $"{transitiveDocIds.Length} transitive document IDs (~{DocumentsPerPrimary} docs/matter)");
        _output.WriteLine($"Warmup: {WarmupIterations} iterations (discarded)");
        _output.WriteLine($"Measured: {MeasuredIterations} iterations");
        _output.WriteLine($"  p50  = {p50,6} ms");
        _output.WriteLine($"  p95  = {p95,6} ms");
        _output.WriteLine($"  p99  = {p99,6} ms");
        _output.WriteLine($"  max  = {max,6} ms");
        _output.WriteLine($"  mean = {mean,6:F1} ms");

        // Soft NFR-04 target (logged only — never asserted; perf tests are env-sensitive).
        if (p95 < SoftTargetMs)
        {
            _output.WriteLine($"NFR-04 target met in-process (p95 {p95}ms < {SoftTargetMs}ms).");
        }
        else if (p95 < HardCeilingMs)
        {
            _output.WriteLine(
                $"NFR-04 target exceeded in-process (p95 {p95}ms >= {SoftTargetMs}ms); " +
                $"production p95 measured via App Insights per AC-1A.5. " +
                $"In-process measurement is a canary-only signal; CI runner variance " +
                $"and mocked resolver overhead are not representative of production.");
        }

        // HARD GATE — gross-regression ceiling. Stable across runners; CI does not flake here.
        // If this fails, investigate pipeline regression (auth, routing, JSON serialization
        // at transitive scale, ProblemDetails handling) — production p95 is measured
        // separately via App Insights per AC-1A.5.
        p95.Should().BeLessThan(HardCeilingMs,
            $"in-process pipeline p95 ({p95}ms) regressed beyond the {HardCeilingMs}ms gross-regression ceiling. " +
            "Investigate auth, routing, JSON serialization at transitive scale, or ProblemDetails handling. " +
            "Production NFR-04 p95 is measured server-side via App Insights per AC-1A.5.");
    }

    /// <summary>
    /// Nearest-rank percentile (R-1 / "lower" estimate) over a long-sample array.
    /// Suitable for small fixed-N samples; avoids the floating-point interpolation
    /// of <c>Percentile.Linear</c> which is overkill here and would obscure the
    /// integer-ms granularity of <see cref="Stopwatch.ElapsedMilliseconds"/>.
    /// </summary>
    private static long Percentile(long[] samples, int percentile)
    {
        if (samples is null || samples.Length == 0)
        {
            throw new ArgumentException("Samples must be non-empty.", nameof(samples));
        }
        if (percentile is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), "Must be 0–100.");
        }

        var sorted = (long[])samples.Clone();
        Array.Sort(sorted);

        // Nearest-rank (R-1): rank = ceil(p/100 * N), 1-based index.
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
        if (rank < 1)
        {
            rank = 1;
        }
        if (rank > sorted.Length)
        {
            rank = sorted.Length;
        }
        return sorted[rank - 1];
    }
}
