using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.HealthChecks;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// CONTRACT — <c>GET /healthz/catalog</c> memoizes its checks' results (unified-access-control-r2 task 167, main-session
/// round 34 item 7).
///
/// <para><b>The contract.</b> The route is anonymous, and each of its checks reads Dataverse as the BFF's own identity.
/// So each check's <c>HealthCheckResult</c> is computed at most once per 30 seconds per instance and SHARED by every
/// caller in that window — concurrent callers included (one in-flight evaluation); a FAULT is shared the same way and for
/// no longer than the same 30 seconds; a caller that hangs up does not cancel the shared evaluation; and an evaluation
/// that HANGS ends at <see cref="CatalogHealthChecks.EvaluationTimeout"/> as a shared timeout fault, re-evaluated after the
/// window (main-session round 43 item 2) — never a permanently stuck memo. This is in addition to the <c>health-probe</c>
/// rate limit, not instead of it.</para>
///
/// <para><b>How.</b> The window is measured on <see cref="TimeProvider"/>, so every test drives a
/// <see cref="FakeTimeProvider"/> — no sleeps, no wall clock. The memo itself is exercised directly (a counting check
/// behind the production <see cref="MemoizedHealthCheck"/>), and the route over HTTP in the real app (the production
/// <see cref="ComposeIdentityKeyHealthCheck"/>, its Dataverse fetch seam counted — the subclass seam its own contract
/// tests use; no transport is mocked, ADR-038 B1).</para>
/// </summary>
public class CatalogHealthMemoContractTests : IClassFixture<CustomWebAppFactory>
{
    private static readonly TimeSpan Window = CatalogHealthChecks.MemoTtl;
    private readonly CustomWebAppFactory _factory;

    public CatalogHealthMemoContractTests(CustomWebAppFactory factory) => _factory = factory;

    /// <summary>An inner check that counts its evaluations and answers whatever <see cref="Next"/> says.</summary>
    private sealed class CountingCheck : IHealthCheck
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Completes when the first evaluation has entered the check (the evaluation runs on a worker).</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The token the most recent evaluation was handed.</summary>
        public CancellationToken LastToken { get; private set; }

        public Func<int, Task<HealthCheckResult>> Next { get; set; } = call => Task.FromResult(HealthCheckResult.Healthy($"evaluation {call}"));

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            var call = Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            return Next(call);
        }
    }

    /// <summary>A real-time safety net for awaiting a signal the test itself controls — never a timing assumption.</summary>
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Bound = CatalogHealthChecks.EvaluationTimeout;

    /// <summary>The production decorator over <paramref name="inner"/>, one NEW instance per probe sharing one state —
    /// exactly how the health-check service builds it from the registration's factory.</summary>
    private static Func<CancellationToken, Task<HealthCheckResult>> Probe(CountingCheck inner, TimeProvider time)
    {
        var state = new MemoizedHealthCheck.State(Window, Bound);
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("catalog-under-test", inner, HealthStatus.Unhealthy, new[] { CatalogHealthChecks.Tag }),
        };
        return ct => new MemoizedHealthCheck(state, scopes, _ => inner, time).CheckHealthAsync(context, ct);
    }

    [Fact]
    public void TheWindowIsThirtySeconds()
    {
        Window.Should().Be(TimeSpan.FromSeconds(30), "main-session round 34 item 7 fixes the memo at 30 seconds");
        Bound.Should().BePositive().And.BeLessThanOrEqualTo(Window,
            "main-session round 43 item 2: a hung evaluation ends as a cached fault, and the bound is no longer than the window");
    }

    [Fact]
    public async Task EveryCallerWithinTheWindow_SharesOneEvaluation_AndTheWindowEndsAtThirtySeconds()
    {
        var clock = new FakeTimeProvider();
        var inner = new CountingCheck();
        var probe = Probe(inner, clock);

        (await probe(CancellationToken.None)).Description.Should().Be("evaluation 1");
        for (var i = 0; i < 9; i++)
        {
            (await probe(CancellationToken.None)).Description.Should().Be("evaluation 1", "every caller in the window gets THE result");
        }

        clock.Advance(Window - TimeSpan.FromTicks(1));
        (await probe(CancellationToken.None)).Description.Should().Be("evaluation 1", "one tick short of 30 s is still inside the window");
        inner.Calls.Should().Be(1, "eleven callers inside 30 s cost ONE evaluation");

        clock.Advance(TimeSpan.FromTicks(1));
        (await probe(CancellationToken.None)).Description.Should().Be("evaluation 2", "at 30 s the memo has expired");
        inner.Calls.Should().Be(2);

        // The new window is measured from the new result.
        clock.Advance(Window - TimeSpan.FromSeconds(1));
        (await probe(CancellationToken.None)).Description.Should().Be("evaluation 2");
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneInFlightEvaluation()
    {
        var clock = new FakeTimeProvider();
        var release = new TaskCompletionSource<HealthCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingCheck { Next = _ => release.Task };
        var probe = Probe(inner, clock);

        // 25 callers from parallel threads. Each passes the memo's gate synchronously before it awaits, so once
        // Parallel.For returns every one of them has either started the evaluation or joined it — nothing is timed.
        var callers = new Task<HealthCheckResult>[25];
        Parallel.For(0, callers.Length, i => callers[i] = probe(CancellationToken.None));
        await inner.Entered.Task.WaitAsync(Safety);   // the one evaluation runs on a worker (round 43 item 2)
        inner.Calls.Should().Be(1, "callers that arrive while an evaluation is running wait for it instead of starting their own");
        callers.Should().OnlyContain(c => !c.IsCompleted, "the one evaluation is still running");

        var result = HealthCheckResult.Degraded("slow but done");
        release.SetResult(result);
        var results = await Task.WhenAll(callers);

        results.Should().OnlyContain(r => r.Status == HealthStatus.Degraded && r.Description == "slow but done");
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AFault_IsSharedForTheWindow_AndNoLonger()
    {
        var clock = new FakeTimeProvider();
        var inner = new CountingCheck
        {
            Next = call => call == 1
                ? Task.FromException<HealthCheckResult>(new InvalidOperationException("Dataverse throttled the read"))
                : Task.FromResult(HealthCheckResult.Healthy("recovered")),
        };
        var probe = Probe(inner, clock);

        var fault = await probe(CancellationToken.None);
        fault.Status.Should().Be(HealthStatus.Unhealthy, "a thrown check reports the registration's failure status");
        fault.Exception.Should().BeOfType<InvalidOperationException>();

        clock.Advance(Window - TimeSpan.FromTicks(1));
        var shared = await probe(CancellationToken.None);
        shared.Exception.Should().BeSameAs(fault.Exception, "a fault is shared like any result — provoking faults cannot defeat the memo");
        shared.Status.Should().Be(HealthStatus.Unhealthy);
        inner.Calls.Should().Be(1);

        clock.Advance(TimeSpan.FromTicks(1));
        var recovered = await probe(CancellationToken.None);
        recovered.Status.Should().Be(HealthStatus.Healthy, "a fault is NOT cached beyond the same 30 s");
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task AnUnhealthyResult_IsSharedForTheWindow_AndNoLonger()
    {
        var clock = new FakeTimeProvider();
        var inner = new CountingCheck
        {
            Next = call => Task.FromResult(call == 1 ? HealthCheckResult.Unhealthy("catalog drift") : HealthCheckResult.Healthy("seeded")),
        };
        var probe = Probe(inner, clock);

        (await probe(CancellationToken.None)).Status.Should().Be(HealthStatus.Unhealthy);
        clock.Advance(TimeSpan.FromSeconds(29));
        (await probe(CancellationToken.None)).Status.Should().Be(HealthStatus.Unhealthy);
        clock.Advance(TimeSpan.FromSeconds(1));
        (await probe(CancellationToken.None)).Status.Should().Be(HealthStatus.Healthy);
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ACallerThatHangsUp_DoesNotCancelTheSharedEvaluation()
    {
        var clock = new FakeTimeProvider();
        var release = new TaskCompletionSource<HealthCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingCheck { Next = _ => release.Task };
        var probe = Probe(inner, clock);

        using var hangUp = new CancellationTokenSource();
        var abandoned = probe(hangUp.Token);
        hangUp.Cancel();
        await FluentActions.Awaiting(() => abandoned).Should().ThrowAsync<OperationCanceledException>("that caller alone stops waiting");

        var waiting = probe(CancellationToken.None);
        release.SetResult(HealthCheckResult.Healthy("finished anyway"));
        (await waiting).Description.Should().Be("finished anyway");
        inner.Calls.Should().Be(1, "the evaluation the first caller started ran to completion and is shared");
    }

    // =============================================================================================
    // Every evaluation ends — a hang included (main-session round 43 item 2)
    // =============================================================================================

    [Fact]
    public async Task AHungEvaluation_EndsAtTheBound_AsASharedTimeoutFault_AndIsReEvaluatedAfterTheWindow()
    {
        var clock = new FakeTimeProvider();
        var never = new TaskCompletionSource<HealthCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingCheck { Next = call => call == 1 ? never.Task : Task.FromResult(HealthCheckResult.Healthy("recovered")) };
        var probe = Probe(inner, clock);

        var first = probe(CancellationToken.None);
        await inner.Entered.Task.WaitAsync(Safety);
        clock.Advance(Bound - TimeSpan.FromTicks(1));
        first.IsCompleted.Should().BeFalse("one tick short of the bound the evaluation is still allowed to run");

        clock.Advance(TimeSpan.FromTicks(1));
        var fault = await first.WaitAsync(Safety);
        fault.Status.Should().Be(HealthStatus.Unhealthy, "a hung check reports the registration's failure status at the bound");
        fault.Description.Should().Contain($"did not complete within {Bound.TotalSeconds:0.#} s");
        inner.LastToken.IsCancellationRequested.Should().BeTrue("the hung evaluation's own token is cancelled at the bound");

        clock.Advance(Window - TimeSpan.FromTicks(1));
        var shared = await probe(CancellationToken.None).WaitAsync(Safety);
        shared.Exception.Should().BeSameAs(fault.Exception, "the timeout is shared like any fault — never a stuck memo");
        inner.Calls.Should().Be(1);

        clock.Advance(TimeSpan.FromTicks(1));
        (await probe(CancellationToken.None).WaitAsync(Safety)).Status.Should().Be(HealthStatus.Healthy,
            "a timeout is NOT cached beyond the window: the next probe evaluates again");
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ACheckThatBlocksSynchronously_IsBoundedToo()
    {
        var clock = new FakeTimeProvider();
        using var gate = new ManualResetEventSlim(false);
        var inner = new CountingCheck
        {
            Next = _ =>
            {
                gate.Wait();   // blocks its thread before returning any task — the evaluation runs on a worker, so it is bounded
                return Task.FromResult(HealthCheckResult.Healthy("late"));
            },
        };
        var probe = Probe(inner, clock);

        try
        {
            var caller = probe(CancellationToken.None);
            await inner.Entered.Task.WaitAsync(Safety);
            caller.IsCompleted.Should().BeFalse();
            clock.Advance(Bound);
            (await caller.WaitAsync(Safety)).Description.Should().Contain("did not complete within");
        }
        finally
        {
            gate.Set();   // release the blocked worker, whatever the outcome
        }
    }

    // =============================================================================================
    // The real app — the route, and every check on it
    // =============================================================================================

    /// <summary>The production identity-key check with its Dataverse fetch counted (its own contract tests' seam).</summary>
    private sealed class CountingKeyProbe : ComposeIdentityKeyHealthCheck
    {
        private int _fetches;

        public CountingKeyProbe(IServiceProvider sp)
            : base(sp, NullLogger<ComposeIdentityKeyHealthCheck>.Instance)
        {
        }

        public int Fetches => Volatile.Read(ref _fetches);

        protected override Task<KeyProbe> FetchKeyStatusAsync(IDataverseService dataverse, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _fetches);
            return Task.FromResult(new KeyProbe("Active", null));
        }
    }

    [Fact]
    public async Task HealthzCatalog_RequestsWithinTheWindow_ReadDataverseOnce_AndARequestAfterItReadsAgain()
    {
        var clock = new FakeTimeProvider();
        var probe = new CountingKeyProbe(new ServiceCollection().AddSingleton(new Mock<IDataverseService>().Object).BuildServiceProvider());
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<ComposeIdentityKeyHealthCheck>();
            services.AddSingleton<ComposeIdentityKeyHealthCheck>(probe);
        }));
        using var client = factory.CreateClient();

        var before = probe.Fetches;   // the startup surface (IHostedService.StartAsync) reads once, unmemoized by design
        for (var i = 0; i < 5; i++)
        {
            (await client.GetAsync("/healthz/catalog")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        (probe.Fetches - before).Should().Be(1, "five anonymous probes inside 30 s cost ONE Dataverse read of the key");

        clock.Advance(Window);
        await client.GetAsync("/healthz/catalog");
        (probe.Fetches - before).Should().Be(2, "after the window the next probe reads again");
    }

    [Fact]
    public void EveryCheckOnHealthzCatalog_IsRegisteredMemoized()
    {
        var registrations = _factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var catalog = registrations.Where(r => r.Tags.Contains(CatalogHealthChecks.Tag)).ToList();

        catalog.Select(r => r.Name).Should().Contain(new[] { "ai-catalog-reconciliation", "compose-identity-key" },
            "the two Dataverse-reading checks /healthz/catalog runs today");
        using var scope = _factory.Services.CreateScope();
        foreach (var registration in catalog)
        {
            registration.Factory(scope.ServiceProvider).Should().BeOfType<MemoizedHealthCheck>(
                $"'{registration.Name}' is on the anonymous /healthz/catalog route, so it must be registered with "
                + "CatalogHealthChecks.AddCatalogCheck (memoized), not AddCheck");
            registration.Timeout.Should().Be(CatalogHealthChecks.EvaluationTimeout,
                $"'{registration.Name}': the health-check service stops each caller's wait at the same bound (round 43 item 2)");
        }

        // CONTROL: the predicate above is not vacuous — a catalog check registered the plain way is NOT memoized, and
        // AddCatalogCheck adds the catalog tag itself.
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddCheck<CountingCheck>("plain", tags: new[] { CatalogHealthChecks.Tag })
            .AddCatalogCheck<CountingCheck>("memoized", HealthStatus.Unhealthy, new[] { "x" });
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ToDictionary(r => r.Name);
        options["plain"].Factory(provider).Should().NotBeOfType<MemoizedHealthCheck>();
        options["memoized"].Factory(provider).Should().BeOfType<MemoizedHealthCheck>();
        options["plain"].Timeout.Should().Be(Timeout.InfiniteTimeSpan, "a plain AddCheck has no bound — the control for the pin above");
        options["memoized"].Tags.Should().BeEquivalentTo(new[] { "x", CatalogHealthChecks.Tag });
    }
}
