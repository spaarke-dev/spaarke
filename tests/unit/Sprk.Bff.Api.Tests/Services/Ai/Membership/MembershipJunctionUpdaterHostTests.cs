// R3 Part 1 Phase 2 — Task 084 (2026-06-22)
// Host-level tests for MembershipJunctionUpdaterHost +
// NullMembershipJunctionUpdaterHost. Locks:
//   - NFR-07: StopAsync drain completes within 30 seconds.
//   - ADR-032 Null peer: ExecuteAsync logs + returns; no Service Bus
//     interaction; no exception when stopped.
//
// Notes on host-level testing scope:
//   • Full message-pump dispatch (deserialize → handler → CompleteMessageAsync)
//     would require either a real Service Bus emulator OR mocking the
//     SDK's ProcessMessageEventArgs internals — both heavy. The
//     end-to-end smoke is task 073's "Bicep deploy + topic/subscription
//     smoke test" + the integration tests downstream of operator deploy.
//   • Here we lock the two host-level contracts that we own:
//     drain semantics + Null peer correctness. Handler correctness is
//     fully covered by `MembershipJunctionUpdaterTests`.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "new")]
public class MembershipJunctionUpdaterHostTests
{
    // ─── ADR-032 Null peer ────────────────────────────────────────────────

    [Fact]
    public async Task NullHost_ExecuteAsync_LogsAndReturnsImmediately()
    {
        // ADR-032 hosted-service Null-peer contract:
        //   - StartAsync (which calls ExecuteAsync) completes without
        //     throwing.
        //   - No Service Bus client created. (Null host has no Service
        //     Bus deps to begin with.)
        //   - StopAsync is a no-op.
        var sut = new NullMembershipJunctionUpdaterHost(
            NullLogger<NullMembershipJunctionUpdaterHost>.Instance);

        using var cts = new CancellationTokenSource();

        await sut.StartAsync(cts.Token);
        await sut.StopAsync(CancellationToken.None);

        // No exception ⇒ contract met. ExecuteAsync returns
        // Task.CompletedTask after a single LogInformation.
    }

    // ─── NFR-07 30s drain ──────────────────────────────────────────────────

    [Fact]
    public async Task RealHost_StopAsync_DoesNotDeadlock_WhenNeverStarted()
    {
        // Calling StopAsync before StartAsync (e.g., host shut down during
        // app-startup failure) must not deadlock and must respect the
        // 30s drain cap. The real host's processor field is null in this
        // state — exercising the null-guard branch.
        var options = Options.Create(new MembershipJunctionUpdaterOptions
        {
            Enabled = true,
            TopicName = "test-topic",
            SubscriptionName = "test-subscription",
            ServiceBusNamespace = "test-ns.servicebus.windows.net",
            MaxConcurrentCalls = 5,
        });

        var scopeFactory = new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var sut = new MembershipJunctionUpdaterHost(
            scopeFactory,
            options,
            NullLogger<MembershipJunctionUpdaterHost>.Instance);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sut.StopAsync(CancellationToken.None);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(MembershipJunctionUpdaterHost.DrainTimeout,
            "StopAsync on never-started host must short-circuit, not wait for full drain timeout");
    }

    [Fact]
    public void DrainTimeout_Is30Seconds()
    {
        // Lock the binding NFR-07 value at the type-system level.
        MembershipJunctionUpdaterHost.DrainTimeout
            .Should().Be(TimeSpan.FromSeconds(30),
                "NFR-07 binds the drain cap at 30 seconds");
    }
}
