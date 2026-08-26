// -----------------------------------------------------------------------------
// LoadTestFixtures.cs
//
// L2 CONTROL-PLANE load-test shared harness (task 062, Wave C5 Batch 4E).
//
// PURPOSE:
//   Reusable seams for the three load-test scenarios:
//     - L2LoadTestFactory: WebApplicationFactory<Program> with in-memory
//       repository + enqueuer + guard seams (parity with RunsEndpointsTests's
//       L2WebApplicationFactory, minus the audit-log sink that isn't needed
//       here).
//     - InMemoryHandlerEnqueuer / DedupingHandlerEnqueuer: two enqueuer
//       shapes — a simple recording enqueuer for scenarios 1/2 (verifies
//       *that* an enqueue happened) + a MessageId-deduping recorder for
//       scenario 3 (verifies dedup contract).
//     - InMemoryProvisioningRunRepository: identical shape to the sibling
//       fixture in Sprk.Provisioning.ControlPlane.Tests.Api.RunsEndpointsTests
//       — replicated here (not shared via InternalsVisibleTo) so the
//       LoadTests project stays free of a test-project-to-test-project
//       reference (which is a fragile pattern the codebase avoids).
//     - AllowAllCustomerRunGuard: a permissive guard (always Success) so
//       the load scenarios can post N=50 concurrent runs against the SAME
//       customerId without tripping the I5 per-customer serialization guard.
//       This is intentional: the enqueue-latency scenario measures the
//       endpoint's OWN work; the I5 guard is exercised by task 059 tests.
//
// MESSAGE-ID DEDUP REPRODUCTION (why not InternalsVisibleTo):
//   ServiceBusHandlerEnqueuer.ComputeMessageId is `internal static` and
//   visible to Sprk.Provisioning.ControlPlane.Tests via InternalsVisibleTo
//   in the SUT csproj. Adding a second InternalsVisibleTo for LoadTests
//   would modify src/server/**, which is forbidden by the task 062 POML
//   'What NOT to touch' rule. Instead this file reproduces the SHA256
//   formula (5 lines, documented in ServiceBusHandlerEnqueuer.cs file
//   header) inline. If the production formula changes, the reproduced
//   ComputeMessageIdParity method here must be updated in the same PR —
//   there is no CI drift guard for this yet (a follow-on task could add
//   one under Sprk.Provisioning.ControlPlane.Tests).
//
// ADR-038 alignment:
//   - No Mock<HttpMessageHandler>. The Cosmos + Service Bus SDKs are not
//     invoked at all — the seams above them route to in-memory doubles.
//   - Seams are interface types, not SDK internals.
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.LoadTests;

/// <summary>
/// WebApplicationFactory that composes the real L2 Program.cs but replaces
/// the three transport seams (repository / enqueuer / customer-run-guard)
/// with in-memory doubles + swaps JwtBearer auth for a permissive header-
/// driven test scheme. The Cosmos + Service Bus modules still LOAD (they
/// need config to satisfy their fail-fast validators) but their clients
/// are never invoked because the seams above them route to in-memory impls.
/// </summary>
public sealed class L2LoadTestFactory : WebApplicationFactory<Program>
{
    /// <summary>In-memory repository — records CreateRun / ReadRun / ReplaceRun.</summary>
    public InMemoryProvisioningRunRepository Repository { get; } = new();

    /// <summary>In-memory enqueuer — records every EnqueueAsync call.</summary>
    public RecordingHandlerEnqueuer Enqueuer { get; } = new();

    /// <summary>Permissive I5 guard — always returns Success so N-concurrent same-customer runs don't 409.</summary>
    public AllowAllCustomerRunGuard Guard { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Satisfy fail-fast validators in AddCosmosModule + AddServiceBusModule
        // + AddTelemetryModule without a live endpoint. The seams above these
        // clients route to the in-memory doubles so the SDKs never fire.
        builder.UseSetting("Cosmos:AccountEndpoint", "https://l2-loadtest.documents.azure.com:443/");
        builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "l2-loadtest.servicebus.windows.net");
        // Reconciler is disabled by default so the endpoint scenarios don't
        // race against a background poller. The dedicated
        // ReconcilerConcurrencyScenario constructs StateReconcilerService
        // directly with its own composition.
        builder.UseSetting("Reconciler:Enabled", "false");

        // Testing environment — TelemetryModule's AzureMonitorGuard skips
        // exporter wiring silently on non-Development/Production envs.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            ReplaceSingleton<IProvisioningRunRepository>(services, Repository);
            ReplaceSingleton<IHandlerEnqueuer>(services, Enqueuer);
            ReplaceSingleton<ICustomerRunGuard>(services, Guard);

            // Test-only auth — same pattern as RunsEndpointsTests.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, LoadTestAuthenticationHandler>(
                    LoadTestAuthenticationHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = LoadTestAuthenticationHandler.SchemeName;
                o.DefaultChallengeScheme = LoadTestAuthenticationHandler.SchemeName;
                o.DefaultForbidScheme = LoadTestAuthenticationHandler.SchemeName;
            });
        });
    }

    private static void ReplaceSingleton<TService>(IServiceCollection services, TService instance)
        where TService : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TService))
            {
                services.RemoveAt(i);
            }
        }
        services.AddSingleton(instance);
    }
}

/// <summary>
/// Thread-safe in-memory ProvisioningRun repository. Enforces the same
/// partition-key contract as the real Cosmos repository: reads REQUIRE
/// (customerId, runId); a read with a wrong customerId returns null.
/// </summary>
public sealed class InMemoryProvisioningRunRepository : IProvisioningRunRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<(string CustomerId, string RunId), (ProvisioningRun Run, string ETag)> _store = new();
    private int _createdCount;
    private int _readCount;

    public int CreatedCount => Volatile.Read(ref _createdCount);
    public int ReadCount => Volatile.Read(ref _readCount);

    public IReadOnlyList<ProvisioningRun> AllRuns
    {
        get { lock (_lock) return _store.Values.Select(v => v.Run).ToArray(); }
    }

    /// <summary>Pre-seeds a run for GET-heavy scenarios.</summary>
    public void Seed(ProvisioningRun run)
    {
        lock (_lock)
        {
            _store[(run.CustomerId, run.RunId)] = (run, "\"seed-etag\"");
        }
    }

    /// <summary>Mutates a run's Status atomically — used by LongHandlerScenario to simulate handler completion.</summary>
    public bool TryUpdateStatus(string customerId, string runId, RunStatus newStatus)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue((customerId, runId), out var stored))
            {
                return false;
            }
            stored.Run.Status = newStatus;
            if (newStatus is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
            {
                stored.Run.CompletedOn = DateTimeOffset.UtcNow;
            }
            return true;
        }
    }

    public Task<ProvisioningRunReadResult?> ReadRunAsync(
        string customerId, string runId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _readCount);
        lock (_lock)
        {
            if (_store.TryGetValue((customerId, runId), out var stored))
            {
                return Task.FromResult<ProvisioningRunReadResult?>(
                    new ProvisioningRunReadResult(stored.Run, stored.ETag));
            }
            return Task.FromResult<ProvisioningRunReadResult?>(null);
        }
    }

    public Task<ProvisioningRunReadResult> CreateRunAsync(
        ProvisioningRun run, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createdCount);
        lock (_lock)
        {
            var key = (run.CustomerId, run.RunId);
            if (_store.ContainsKey(key))
            {
                throw new InvalidOperationException($"Run '{run.RunId}' already exists.");
            }
            var etag = "\"created-" + Guid.NewGuid().ToString("N") + "\"";
            _store[key] = (run, etag);
            return Task.FromResult(new ProvisioningRunReadResult(run, etag));
        }
    }

    public Task<ReplaceRunResult> ReplaceRunAsync(
        ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var key = (run.CustomerId, run.RunId);
            if (!_store.TryGetValue(key, out var stored))
            {
                return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.NotFound());
            }
            if (stored.ETag != ifMatchEtag)
            {
                return Task.FromResult<ReplaceRunResult>(
                    new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(stored.Run, stored.ETag)));
            }
            var newEtag = "\"replaced-" + Guid.NewGuid().ToString("N") + "\"";
            _store[key] = (run, newEtag);
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, newEtag));
        }
    }
}

/// <summary>
/// Records every EnqueueAsync call. Not deduping — used by scenarios that
/// verify call *count* (not dedup contract).
/// </summary>
public sealed class RecordingHandlerEnqueuer : IHandlerEnqueuer
{
    private readonly List<HandlerEnvelope> _enqueued = new();
    private readonly object _lock = new();

    public IReadOnlyList<HandlerEnvelope> Enqueued
    {
        get { lock (_lock) return _enqueued.ToArray(); }
    }

    public int Count
    {
        get { lock (_lock) return _enqueued.Count; }
    }

    public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        lock (_lock) _enqueued.Add(envelope);
        return Task.CompletedTask;
    }
}

/// <summary>
/// MessageId-deduping enqueuer for the reconciler concurrency scenario.
/// Replicates the production Service Bus wire-level dedup contract
/// (level-1 idempotency per FR-22): two calls with an identical envelope
/// produce an identical deterministic MessageId; the queue retains ONE.
/// TotalCalls counts every call attempt; DistinctMessageIds retains one
/// entry per unique MessageId.
/// </summary>
public sealed class DedupingHandlerEnqueuer : IHandlerEnqueuer
{
    private readonly object _lock = new();
    private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
    private readonly List<HandlerEnvelope> _distinct = new();
    private int _totalCalls;

    public int TotalCalls => Volatile.Read(ref _totalCalls);

    public IReadOnlyList<HandlerEnvelope> DistinctEnvelopes
    {
        get { lock (_lock) return _distinct.ToArray(); }
    }

    public IReadOnlyList<string> DistinctMessageIds
    {
        get { lock (_lock) return _seenIds.ToArray(); }
    }

    public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalCalls);
        var messageId = ComputeMessageIdParity(envelope);
        lock (_lock)
        {
            if (_seenIds.Add(messageId))
            {
                _distinct.Add(envelope);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reproduces <c>ServiceBusHandlerEnqueuer.ComputeMessageId</c>
    /// (internal in the SUT). The formula is documented in the SUT file
    /// header (`{HandlerId}|{RunId}|{CustomerId}|paramHash` -> SHA256 hex).
    /// See the file header for why this is reproduced rather than accessed
    /// via InternalsVisibleTo.
    /// </summary>
    public static string ComputeMessageIdParity(HandlerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var paramHash = Sha256Hex(envelope.ParametersJson);
        var composite = $"{envelope.HandlerId}|{envelope.RunId}|{envelope.CustomerId}|{paramHash}";
        return Sha256Hex(composite);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// Permissive I5 guard — always returns Success so scenarios can post
/// N-concurrent same-customer runs without tripping FR-23 serialization.
/// The I5 guard's own contract is exercised by the task 059 tests; this
/// harness is measuring the endpoint's OWN latency, not the guard.
/// </summary>
public sealed class AllowAllCustomerRunGuard : ICustomerRunGuard
{
    public Task<AcquireResult> TryAcquireAsync(string customerId, string runId, CancellationToken cancellationToken)
        => Task.FromResult<AcquireResult>(new AcquireResult.Success(customerId, runId));

    public Task<ReleaseResult> ReleaseAsync(string customerId, string runId, CancellationToken cancellationToken)
        => Task.FromResult<ReleaseResult>(new ReleaseResult.Released(customerId, runId));
}

/// <summary>
/// Header-driven test authentication handler. Any request with an
/// Authorization header authenticates as an Operator by default (roles
/// controllable via X-Test-Roles). Mirrors the pattern in
/// Sprk.Provisioning.ControlPlane.Tests.Api.TestAuthenticationHandler
/// but tuned for load-test defaults (Operator by default so the load
/// scenarios don't need per-request role attachment).
/// </summary>
public sealed class LoadTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestBearer";
    public const string RolesHeader = "X-Test-Roles";
    private const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    private const string TestObjectId = "22222222-2222-2222-2222-222222222222";

    public LoadTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out _))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var rolesRaw = Request.Headers.TryGetValue(RolesHeader, out var vs)
            ? vs.ToString()
            : "Operator"; // Default = Operator so load scenarios don't need to set the header per-request.

        var claims = new List<Claim>
        {
            new("http://schemas.microsoft.com/identity/claims/tenantid", TestTenantId),
            new("http://schemas.microsoft.com/identity/claims/objectidentifier", TestObjectId),
        };
        foreach (var role in rolesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Local <see cref="TimeProvider"/> double for ReconcilerConcurrencyScenario.
/// Mirrors the pattern in Sprk.Provisioning.ControlPlane.Tests.Reconciler
/// (StateReconcilerServiceTests.TestTimeProvider) to avoid adding
/// Microsoft.Extensions.TimeProvider.Testing as a package dependency.
/// </summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset initial)
    {
        _now = initial;
    }

    public void Set(DateTimeOffset next) => _now = next;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>
/// Small helper for percentile computation on a Stopwatch-derived latency
/// sample set. Sorts a copy of the sample array in place then indexes with
/// nearest-rank at the given percentile. Sufficient for the 3-scenario
/// reporting purpose; not intended as a general-purpose percentiles impl.
/// </summary>
public static class LatencyStatistics
{
    /// <summary>
    /// Returns the sample at the requested percentile (0..100) using the
    /// nearest-rank method. Percentile 100 returns Max; percentile 0
    /// returns Min. Input array must be non-empty.
    /// </summary>
    public static long Percentile(long[] samplesMs, double percentile)
    {
        ArgumentNullException.ThrowIfNull(samplesMs);
        if (samplesMs.Length == 0)
        {
            throw new ArgumentException("samples must be non-empty", nameof(samplesMs));
        }
        if (percentile < 0 || percentile > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), "percentile must be 0..100");
        }

        var sorted = (long[])samplesMs.Clone();
        Array.Sort(sorted);
        // Nearest-rank: ceil(p/100 * N) - 1, clamped.
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        if (index < 0) index = 0;
        if (index >= sorted.Length) index = sorted.Length - 1;
        return sorted[index];
    }

    /// <summary>Simple arithmetic mean of the samples.</summary>
    public static double Mean(long[] samplesMs)
    {
        ArgumentNullException.ThrowIfNull(samplesMs);
        if (samplesMs.Length == 0) return 0d;
        double total = 0;
        for (var i = 0; i < samplesMs.Length; i++) total += samplesMs[i];
        return total / samplesMs.Length;
    }
}
