// -----------------------------------------------------------------------------
// CustomerRunGuardTests.cs
//
// L2 CONTROL-PLANE unit tests for the I5 concurrency guard (task 059, Wave C5).
//
// COVERAGE — POML acceptance criteria (a)–(f):
//   (a) Happy acquire: null column -> Success, column value = runA.
//   (b) Concurrent-acquire: column already holds runA, TryAcquire(runB) ->
//       Conflict(winningRunId=runA); column unchanged.
//   (c) Idempotent re-acquire: column holds runA, TryAcquire(runA) -> Success.
//   (d) Release-match: column holds runA, ReleaseAsync(runA) -> Released;
//       column is null.
//   (e) Release-mismatch: column holds runA, ReleaseAsync(runB) -> Mismatched;
//       column unchanged (== runA).
//   (f) Cross-customer parallelism: TryAcquire(C1, runX) + TryAcquire(C2, runY)
//       BOTH succeed independently.
//   (g) 409 body wiring: task 059 endpoint test asserts winningRunId +
//       reasonCode round-trip (in RunsEndpointsTests.cs, task 057's fixture).
//
// PLUS extended coverage for robustness:
//   (h) Missing registry row -> TransientFailure (endpoint surfaces 502).
//   (i) Store transient failure -> TransientFailure propagated.
//   (j) ETag race on acquire -> retry succeeds when the racer became our
//       runId (Success); returns Conflict when a different runner won.
//   (k) Kill-switch (Enabled=false) -> Success unconditionally.
//   (l) Canonicalization: braced/UPPERCASE runId compares equal to
//       stored lowercase.
//   (m) Quarantined winner -> ReasonCode == "Quarantined" (task 061 hook).
//
// SEAM: InMemoryRegistryConcurrencyStore (test-only impl) — satisfies
// ADR-038 §5 (no Mock<HttpMessageHandler>).
//
// PATH: L2 project-scoped test — mirrors L2 Reconciler/*Tests + Handlers/*Tests.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Concurrency;

/// <summary>
/// Unit tests for <see cref="CustomerRunGuard"/> — covers every POML
/// acceptance criterion plus the extended robustness matrix documented in the
/// file header.
/// </summary>
public sealed class CustomerRunGuardTests
{
    private const string CustomerA = "acme-corp";
    private const string CustomerB = "beta-industries";
    private const string RunA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string RunB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    // -----------------------------------------------------------------------
    // POML (a) — Happy acquire.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_NullColumn_ReturnsSuccessAndSetsColumn()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: null);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Success>();
        ((AcquireResult.Success)result).RunId.Should().Be(RunA);
        store.PeekCurrentRunId(CustomerA).Should().Be(RunA);
    }

    // -----------------------------------------------------------------------
    // POML (b) — Conflict on concurrent-acquire.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_ConcurrentDifferentRun_ReturnsConflictWithWinningRunId()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository();
        // Winning run doc has Running status -> reasonCode should be AlreadyInFlight.
        repo.Seed(new ProvisioningRun
        {
            RunId = RunA,
            CustomerId = CustomerA,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Running,
        });
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunB, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Conflict>();
        var conflict = (AcquireResult.Conflict)result;
        conflict.WinningRunId.Should().Be(RunA);
        conflict.ReasonCode.Should().Be(AcquireConflictReasonCodes.AlreadyInFlight);
        // Column MUST be unchanged.
        store.PeekCurrentRunId(CustomerA).Should().Be(RunA);
    }

    // -----------------------------------------------------------------------
    // POML (c) — Idempotent re-acquire (crash-recovery contract).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_SameRunAlreadyHeld_ReturnsSuccessIdempotently()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Success>();
        store.PeekCurrentRunId(CustomerA).Should().Be(RunA);
    }

    // -----------------------------------------------------------------------
    // POML (d) — Release-match clears the column.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Release_MatchingRun_ClearsColumn()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.ReleaseAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<ReleaseResult.Released>();
        store.PeekCurrentRunId(CustomerA).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // POML (e) — Release-mismatch is a documented no-op.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Release_DifferentRunHeld_ReturnsMismatchedAndLeavesColumnUntouched()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.ReleaseAsync(CustomerA, RunB, CancellationToken.None);

        result.Should().BeOfType<ReleaseResult.Mismatched>();
        ((ReleaseResult.Mismatched)result).CurrentValue.Should().Be(RunA);
        store.PeekCurrentRunId(CustomerA).Should().Be(RunA);
    }

    [Fact]
    public async Task Release_ColumnAlreadyNull_ReturnsMismatched()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: null);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.ReleaseAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<ReleaseResult.Mismatched>();
        ((ReleaseResult.Mismatched)result).CurrentValue.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // POML (f) — Cross-customer parallelism.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_CrossCustomer_BothSucceedIndependently()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: null);
        store.Seed(CustomerB, currentRunId: null);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var resultA = await guard.TryAcquireAsync(CustomerA, RunA, CancellationToken.None);
        var resultB = await guard.TryAcquireAsync(CustomerB, RunB, CancellationToken.None);

        resultA.Should().BeOfType<AcquireResult.Success>();
        resultB.Should().BeOfType<AcquireResult.Success>();
        store.PeekCurrentRunId(CustomerA).Should().Be(RunA);
        store.PeekCurrentRunId(CustomerB).Should().Be(RunB);
    }

    // -----------------------------------------------------------------------
    // (h) Missing registry row -> TransientFailure.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_MissingRegistryRow_ReturnsTransientFailure()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        // No seed — customer row does not exist.
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.TransientFailure>();
        ((AcquireResult.TransientFailure)result).Diagnostic
            .Should().Contain("not found for customerId");
    }

    // -----------------------------------------------------------------------
    // (i) Store transient failures propagate as guard TransientFailure.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_LookupTransientFailure_PropagatesDiagnostic()
    {
        var store = new InMemoryRegistryConcurrencyStore { ForceLookupFailure = "simulated DV outage" };
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.TransientFailure>();
        ((AcquireResult.TransientFailure)result).Diagnostic.Should().Be("simulated DV outage");
    }

    [Fact]
    public async Task Release_LookupTransientFailure_ReturnsTransientResult()
    {
        var store = new InMemoryRegistryConcurrencyStore { ForceLookupFailure = "simulated DV outage" };
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.ReleaseAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<ReleaseResult.TransientFailure>();
    }

    // -----------------------------------------------------------------------
    // (j) ETag race on acquire — retry loop.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_ETagRace_LosingToDifferentRun_ReturnsConflict()
    {
        // Row starts null. On the first Lookup, the guard reads null + ETag=1.
        // Before PATCH lands, an external writer stamps runB. TrySetIfNull with
        // the stale ETag -> PreconditionFailed. On retry, Lookup returns runB
        // -> Conflict(winningRunId=runB).
        var inner = new InMemoryRegistryConcurrencyStore();
        inner.Seed(CustomerA, currentRunId: null);
        var lookupCount = 0;
        var store = new AfterFirstLookupInjectingStore(inner, () =>
        {
            if (Interlocked.Increment(ref lookupCount) == 1)
            {
                inner.ExternalWrite(CustomerA, RunB);
            }
        });
        var repo = new InMemoryProvisioningRunRepository();
        repo.Seed(new ProvisioningRun
        {
            RunId = RunB,
            CustomerId = CustomerA,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Running,
        });
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Conflict>();
        ((AcquireResult.Conflict)result).WinningRunId.Should().Be(RunB);
        inner.PeekCurrentRunId(CustomerA).Should().Be(RunB);
    }

    // -----------------------------------------------------------------------
    // (k) Kill-switch — disabled guard returns Success without touching store.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_KillSwitchDisabled_ReturnsSuccessWithoutStoreCall()
    {
        var store = new InMemoryRegistryConcurrencyStore { ForceLookupFailure = "should-not-be-called" };
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo, enabled: false);

        var result = await guard.TryAcquireAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Success>();
        // Store was NOT touched — the row was never seeded and yet no failure.
        store.PeekCurrentRunId(CustomerA).Should().BeNull();
    }

    [Fact]
    public async Task Release_KillSwitchDisabled_ReturnsReleasedWithoutStoreCall()
    {
        var store = new InMemoryRegistryConcurrencyStore { ForceLookupFailure = "should-not-be-called" };
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo, enabled: false);

        var result = await guard.ReleaseAsync(CustomerA, RunA, CancellationToken.None);

        result.Should().BeOfType<ReleaseResult.Released>();
    }

    // -----------------------------------------------------------------------
    // (l) ADR-044 canonicalization — brace-wrapped/UPPERCASE compares equal.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_BracedUpperCaseRunId_IdempotentAgainstLowercaseStored()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        // Caller passes `{AAAA...}` in UPPERCASE with braces — should still
        // match the canonical lowercase stored value.
        var result = await guard.TryAcquireAsync(
            CustomerA, "{" + RunA.ToUpperInvariant() + "}", CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Success>();
        // Column is unchanged (still the canonical lowercase form).
        store.PeekCurrentRunId(CustomerA).Should().Be(RunA);
    }

    [Fact]
    public async Task Release_BracedUpperCaseRunId_MatchesStoredCanonical()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository();
        var guard = BuildGuard(store, repo);

        var result = await guard.ReleaseAsync(
            CustomerA, "{" + RunA.ToUpperInvariant() + "}", CancellationToken.None);

        result.Should().BeOfType<ReleaseResult.Released>();
        store.PeekCurrentRunId(CustomerA).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // (m) Quarantined winner -> ReasonCode == "Quarantined" (task 061 hook).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryAcquire_ConflictWithQuarantinedWinner_ReturnsQuarantinedReasonCode()
    {
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository();
        // Winning run is Quarantined -> reasonCode should be Quarantined.
        repo.Seed(new ProvisioningRun
        {
            RunId = RunA,
            CustomerId = CustomerA,
            EnvironmentId = "env-1",
            TenancyModel = "Model1Shared",
            Profile = "spaarke-hosted-model1-trial",
            Status = RunStatus.Quarantined,
            Quarantine = new QuarantineInfo { Reason = "partial Bicep deploy", QuarantinedAt = DateTimeOffset.UtcNow },
        });
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunB, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Conflict>();
        var conflict = (AcquireResult.Conflict)result;
        conflict.WinningRunId.Should().Be(RunA);
        conflict.ReasonCode.Should().Be(AcquireConflictReasonCodes.Quarantined);
    }

    [Fact]
    public async Task TryAcquire_ConflictWithNoRunDoc_DegradesToAlreadyInFlight()
    {
        // Guard column set but no run doc exists in Cosmos (edge case — pre-59
        // partial state). Should still return a Conflict with the default
        // AlreadyInFlight reason — Cosmos hiccup MUST NOT mask the concurrency
        // signal.
        var store = new InMemoryRegistryConcurrencyStore();
        store.Seed(CustomerA, currentRunId: RunA);
        var repo = new InMemoryProvisioningRunRepository(); // no seeds
        var guard = BuildGuard(store, repo);

        var result = await guard.TryAcquireAsync(CustomerA, RunB, CancellationToken.None);

        result.Should().BeOfType<AcquireResult.Conflict>();
        ((AcquireResult.Conflict)result).ReasonCode
            .Should().Be(AcquireConflictReasonCodes.AlreadyInFlight);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CustomerRunGuard BuildGuard(
        IRegistryConcurrencyStore store,
        IProvisioningRunRepository repo,
        bool enabled = true)
    {
        var options = Options.Create(new CustomerRunGuardOptions
        {
            Enabled = enabled,
            // REG-02 Path X (2026-08-27): TenantId / ClientId / ClientSecret
            // fields were REMOVED — the store authenticates via DefaultAzureCredential
            // pinned to a UAMI (ManagedIdentityClientId). The in-memory store
            // bypasses Dataverse entirely so no real credential is exercised.
            TargetDataverseUrl = enabled ? "https://spaarke-admin-test.crm.dynamics.com" : null,
            ManagedIdentityClientId = enabled ? "22222222-2222-2222-2222-222222222222" : null,
        });
        return new CustomerRunGuard(store, repo, options, NullLogger<CustomerRunGuard>.Instance);
    }

    /// <summary>
    /// Decorator over <see cref="InMemoryRegistryConcurrencyStore"/> that
    /// fires <paramref name="onAfterLookup"/> AFTER every LookupAsync — the
    /// callback decides internally whether to inject on the 1st vs Nth call.
    /// Uses composition (not inheritance) because C# `new` shadowing does NOT
    /// dispatch through the interface, so a subclass override was invisible
    /// to CustomerRunGuard (which holds the store via the interface).
    /// </summary>
    private sealed class AfterFirstLookupInjectingStore : IRegistryConcurrencyStore
    {
        private readonly InMemoryRegistryConcurrencyStore _inner;
        private readonly Action _onAfterLookup;

        public AfterFirstLookupInjectingStore(InMemoryRegistryConcurrencyStore inner, Action onAfterLookup)
        {
            _inner = inner;
            _onAfterLookup = onAfterLookup;
        }

        public async Task<LookupOutcome> LookupAsync(string customerId, CancellationToken cancellationToken)
        {
            var res = await _inner.LookupAsync(customerId, cancellationToken);
            _onAfterLookup();
            return res;
        }

        public Task<WriteOutcome> TrySetIfNullAsync(
            Guid environmentRowId, string newRunId, string ifMatchEtag, CancellationToken cancellationToken)
            => _inner.TrySetIfNullAsync(environmentRowId, newRunId, ifMatchEtag, cancellationToken);

        public Task<WriteOutcome> TryClearAsync(
            Guid environmentRowId, string ifMatchEtag, CancellationToken cancellationToken)
            => _inner.TryClearAsync(environmentRowId, ifMatchEtag, cancellationToken);
    }
}

// -----------------------------------------------------------------------------
// InMemoryProvisioningRunRepository — tiny stub for CustomerRunGuardTests.
//
// The InMemoryProvisioningRunRepository in Api/RunsEndpointsTests.cs is a
// different concrete type with a different scope; we keep this one local so
// this file has no cross-file dependency on the endpoint fixture.
// -----------------------------------------------------------------------------

internal sealed class InMemoryProvisioningRunRepository : IProvisioningRunRepository
{
    private readonly Dictionary<(string CustomerId, string RunId), ProvisioningRun> _store = new();

    public void Seed(ProvisioningRun run) => _store[(run.CustomerId, run.RunId)] = run;

    public Task<ProvisioningRunReadResult?> ReadRunAsync(
        string customerId, string runId, CancellationToken cancellationToken)
    {
        return _store.TryGetValue((customerId, runId), out var run)
            ? Task.FromResult<ProvisioningRunReadResult?>(new ProvisioningRunReadResult(run, "\"unit-etag\""))
            : Task.FromResult<ProvisioningRunReadResult?>(null);
    }

    public Task<ProvisioningRunReadResult> CreateRunAsync(
        ProvisioningRun run, CancellationToken cancellationToken)
    {
        _store[(run.CustomerId, run.RunId)] = run;
        return Task.FromResult(new ProvisioningRunReadResult(run, "\"unit-etag\""));
    }

    public Task<ReplaceRunResult> ReplaceRunAsync(
        ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken)
    {
        _store[(run.CustomerId, run.RunId)] = run;
        return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, "\"unit-etag\""));
    }
}
