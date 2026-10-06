// -----------------------------------------------------------------------------
// HttpHealthzProbeTests.cs
//
// Task 205c / punch row A39 — unit tests for HttpHealthzProbe. Task 201
// shipped the class without a dedicated test file; this task adds one because
// the probe's backoff-poll is the CHOSEN mechanism (option (b), H4b boot-retry
// allowance) for tolerating the auth-v4 §11 invariant 2 measured ~130s
// AADSTS70025 FIC-propagation-flap window once Graph__Credentials__Order__0 /
// RequireSecretFreeIdentity (task 205c / A39) are applied.
//
// ADR-038 CATEGORY: tests/unit/domain -- pure C# unit test. A hand-rolled
// HttpMessageHandler subclass (NOT Mock<HttpMessageHandler> -- ADR-038 §"MUST
// NOT" rule 1 bans mocking the transport abstraction directly) stands in for
// the network boundary, matching the constraint doc's own recommended
// alternative ("a fake HttpClient via test-double + integration boundary").
//
// COVERAGE:
//   T1  Transient failures (simulating the flap) followed by a 200 within the
//       schedule -> Success. Demonstrates the FIC-flap tolerance mechanism
//       concretely: H4b's healthz-poll absorbs a flap that resolves before
//       the schedule is exhausted.
//   T2  All attempts fail -> Timeout after exhausting the FULL schedule (the
//       fail-fast backstop once the window is genuinely exceeded).
//   T3  DefaultBackoffSchedule total budget comfortably exceeds the measured
//       ~130s flap window -- the regression guard for the "rely on the
//       existing backoff, no new BFF-side retry code" design choice.
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class HttpHealthzProbeTests
{
    private static readonly Uri HealthzUrl = new("https://sprk-prod-api.azurewebsites.net/healthz");

    // Zero-delay schedule so the transient-failure/recover tests run
    // instantly -- the PRODUCTION schedule's timing is asserted separately
    // (T3) as a static budget guard, never exercised via a real Task.Delay
    // in a test (per testing.md "MUST NOT use Thread.Sleep or arbitrary
    // delays").
    private static readonly TimeSpan[] FastTestSchedule =
        [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    [Fact]
    public async Task ProbeWithBackoffAsync_TransientFailuresThenSuccess_ReturnsSuccessWithinBudget()
    {
        // Simulates the FIC-propagation-flap window: the first 2 probes see a
        // transient failure (as a broken/flapping credential would surface as
        // a non-200 from a BFF that has not yet finished booting), the 3rd
        // succeeds once the flap has settled.
        var handler = new SequencedResponseHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        var probe = new HttpHealthzProbe(new HttpClient(handler), NullLogger<HttpHealthzProbe>.Instance, FastTestSchedule);

        var result = await probe.ProbeWithBackoffAsync(HealthzUrl, CancellationToken.None);

        var success = result.Should().BeOfType<HealthzResult.Success>().Subject;
        success.LastStatusCode.Should().Be(200);
        handler.CallCount.Should().Be(3, "the probe must stop retrying as soon as it observes 200");
    }

    [Fact]
    public async Task ProbeWithBackoffAsync_AllAttemptsFail_ReturnsTimeoutAfterExhaustingSchedule()
    {
        var handler = new SequencedResponseHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable);
        var probe = new HttpHealthzProbe(new HttpClient(handler), NullLogger<HttpHealthzProbe>.Instance, FastTestSchedule);

        var result = await probe.ProbeWithBackoffAsync(HealthzUrl, CancellationToken.None);

        var timeout = result.Should().BeOfType<HealthzResult.Timeout>().Subject;
        timeout.AttemptsMade.Should().Be(FastTestSchedule.Length);
        handler.CallCount.Should().Be(FastTestSchedule.Length,
            "a genuinely broken credential (beyond the flap window) must exhaust the full budget " +
            "before the handler classifies it QuarantineRequired");
    }

    [Fact]
    public void DefaultBackoffSchedule_TotalBudget_ExceedsMeasuredFicFlapWindowWithMargin()
    {
        // Auth-v4 §11 invariant 2 measured value: ~130s (~8 failures,
        // AADSTS70025) after a FIC is created/changed. Task 205c / A39 relies
        // on this EXISTING budget (not new BFF-side retry code) as the FIC-
        // flap tolerance mechanism -- see manifest.yaml + H4bBulkAppSettingsHandler.cs.
        var totalBudget = HttpHealthzProbe.DefaultBackoffSchedule
            .Aggregate(TimeSpan.Zero, (sum, delay) => sum + delay);

        totalBudget.TotalSeconds.Should().BeGreaterThanOrEqualTo(300,
            "the shipped 30+60+90+120+180=480s schedule must stay well above the measured " +
            "~130s flap window; shrinking it below ~2x margin would reopen the boot-loop risk");
    }

    /// <summary>
    /// Hand-rolled HttpMessageHandler test double (NOT a Mock&lt;HttpMessageHandler&gt;)
    /// returning a fixed sequence of status codes, one per call, holding on the
    /// last entry once exhausted.
    /// </summary>
    private sealed class SequencedResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _sequence;
        public int CallCount { get; private set; }

        public SequencedResponseHandler(params HttpStatusCode[] sequence) => _sequence = sequence;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Math.Min(CallCount, _sequence.Length - 1);
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_sequence[index]));
        }
    }
}
