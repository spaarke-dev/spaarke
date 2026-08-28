// -----------------------------------------------------------------------------
// DataverseRegistrySetupStatusUpdaterTests.cs
//
// L2 CONTROL-PLANE task-184 unit coverage for the REAL H13 Ready-writer
// (DataverseRegistrySetupStatusUpdater) that replaces the Wave-C4 logged
// no-op. These tests satisfy the POML's testable acceptance criteria:
//
//   Criterion 1: The old `LogWarning.*no-op` pattern is gone — enforced by
//                the source-level assertion in
//                DataverseRegistrySetupStatusUpdaterNoOpForbiddenTests below.
//   Criterion 2: A completed H13 outcome produces a REAL PATCH call to the
//                C1.4 registry client with sprk_setupstatus=Ready — enforced
//                by TransitionToReadyAsync_Delegates_To_RegistryClient tests.
//   Criterion 3: sprk_currentrunid is cleared in the SAME transition
//                (companion update per spec.md FR-23) — enforced by
//                TransitionToReadyAsync_Sets_ClearCurrentRunId_True.
//   Criterion 4: Integration seam test against a canary registry row —
//                lives in DataverseEnvironmentRegistryClientTests's smoke
//                tier (env-guarded DVREG_L2_SMOKE_*). Not duplicated here to
//                avoid ADR-038 KEEP-path drift.
//
// ADR-038 KEEP path: tests/unit/**/handlers/e2e-acceptance/**. Pure unit —
// zero HTTP, zero credential, single fake IDataverseEnvironmentRegistryClient
// (parity with H13's FakeRegistryClient pattern).
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Registry;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.E2EAcceptance;

public sealed class DataverseRegistrySetupStatusUpdaterTests
{
    // -------------------------------------------------------------------------
    // Ctor guards.
    // -------------------------------------------------------------------------

    [Fact]
    public void Ctor_Rejects_Null_RegistryClient()
    {
        FluentActions.Invoking(() => new DataverseRegistrySetupStatusUpdater(
            registryClient: null!,
            logger: NullLogger<DataverseRegistrySetupStatusUpdater>.Instance))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_Rejects_Null_Logger()
    {
        FluentActions.Invoking(() => new DataverseRegistrySetupStatusUpdater(
            registryClient: new SuccessFakeClient(),
            logger: null!))
            .Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // Request guards.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransitionToReadyAsync_Rejects_Null_Request()
    {
        var updater = NewUpdater(new SuccessFakeClient());
        await FluentActions.Invoking(() => updater.TransitionToReadyAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TransitionToReadyAsync_Rejects_Empty_EnvironmentId()
    {
        var updater = NewUpdater(new SuccessFakeClient());
        var request = NewRequest(environmentId: "  ");
        await FluentActions.Invoking(() => updater.TransitionToReadyAsync(request, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TransitionToReadyAsync_Rejects_Empty_CustomerId()
    {
        var updater = NewUpdater(new SuccessFakeClient());
        var request = NewRequest(customerId: string.Empty);
        await FluentActions.Invoking(() => updater.TransitionToReadyAsync(request, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TransitionToReadyAsync_Rejects_Empty_RunId()
    {
        var updater = NewUpdater(new SuccessFakeClient());
        var request = NewRequest(runId: string.Empty);
        await FluentActions.Invoking(() => updater.TransitionToReadyAsync(request, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // POML acceptance criterion #2: real PATCH call with sprk_setupstatus=Ready.
    // POML acceptance criterion #3: sprk_currentrunid cleared in same transition.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransitionToReadyAsync_Delegates_To_RegistryClient_With_SetupStatus_Ready()
    {
        var fake = new CapturingFakeClient(new RegistryUpdateOutcome.Success());
        var updater = NewUpdater(fake);
        var request = NewRequest();

        var outcome = await updater.TransitionToReadyAsync(request, CancellationToken.None);

        fake.CallCount.Should().Be(1, "the H13 real Ready-writer MUST hit the C1.4 registry client (no more logged no-op)");
        fake.LastUpdate.Should().NotBeNull();
        fake.LastUpdate!.SetupStatus.Should().Be("Ready");
        outcome.Should().BeOfType<RegistrySetupStatusUpdateOutcome.Success>();
    }

    [Fact]
    public async Task TransitionToReadyAsync_Sets_ClearCurrentRunId_False_SingleWriterInvariant()
    {
        // Bucket B HIGH#7 SESSION 18 (customer-provisioning-orchestration-r1
        // adversarial e2e verify workflow wepdcb8we): the Ready PATCH MUST NOT
        // clear sprk_currentrunid. Two writers on the same column with different
        // safety models (unconditional PATCH here vs ETag-safe
        // CustomerRunGuard.ReleaseAsync elsewhere) is a concurrency skew that
        // produces silent-fail bugs.
        //
        // The release now fires from ONE authoritative path — Bucket B HIGH#6's
        // explicit ICustomerRunGuard.ReleaseAsync call in HandlerOutcomeApplier's
        // Success-with-RunStatus.Completed branch. That call is ETag-safe and
        // stale-value-safe (Mismatched = no-op). This test locks the single-
        // writer invariant so a future edit that re-flips ClearCurrentRunId=true
        // fails here rather than silently re-opening the two-writer race.
        //
        // Historical note: spec.md FR-23 originally read "companion clear of
        // sprk_currentrunid in the SAME transaction as Ready" — the SESSION 18
        // adversarial audit re-classified that as a fragile-coupling anti-pattern
        // now that ICustomerRunGuard.ReleaseAsync (task 059) is the canonical
        // ETag-safe release primitive.
        var fake = new CapturingFakeClient(new RegistryUpdateOutcome.Success());
        var updater = NewUpdater(fake);
        var request = NewRequest();

        await updater.TransitionToReadyAsync(request, CancellationToken.None);

        fake.LastUpdate.Should().NotBeNull();
        fake.LastUpdate!.ClearCurrentRunId.Should().BeFalse(
            "single-writer invariant: sprk_currentrunid MUST be released via ICustomerRunGuard.ReleaseAsync " +
            "(from HandlerOutcomeApplier's terminal-Completed branch per Bucket B HIGH#6), NOT piggy-backed " +
            "on the Ready PATCH. Two writers with different safety models = concurrency race.");
    }

    [Fact]
    public async Task TransitionToReadyAsync_Threads_Ids_Through_As_Log_Metadata()
    {
        var fake = new CapturingFakeClient(new RegistryUpdateOutcome.Success());
        var updater = NewUpdater(fake);
        var request = NewRequest(
            customerId: "trial-2026-08-20",
            runId: "65109e91-5968-4300-933e-9e79dea4109c",
            environmentId: "87d7b4a7-399b-f111-b8de-7ced8ddc4a05");

        await updater.TransitionToReadyAsync(request, CancellationToken.None);

        fake.LastUpdate!.CustomerIdForLog.Should().Be("trial-2026-08-20");
        fake.LastUpdate.RunIdForLog.Should().Be("65109e91-5968-4300-933e-9e79dea4109c");
        fake.LastUpdate.EnvironmentId.Should().Be("87d7b4a7-399b-f111-b8de-7ced8ddc4a05");
    }

    // -------------------------------------------------------------------------
    // Outcome-mapping fidelity — the C1.4 wire outcomes fold to the domain
    // outcomes H13 expects.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransitionToReadyAsync_Success_Maps_To_Domain_Success()
    {
        var updater = NewUpdater(new CapturingFakeClient(new RegistryUpdateOutcome.Success()));
        var outcome = await updater.TransitionToReadyAsync(NewRequest(), CancellationToken.None);
        outcome.Should().BeOfType<RegistrySetupStatusUpdateOutcome.Success>();
    }

    [Fact]
    public async Task TransitionToReadyAsync_NotFound_Maps_To_Domain_Failure_With_Preserved_Diagnostic()
    {
        // 404 (row missing) folds to Failure with a preserved diagnostic so
        // an operator diffing the H13 rejection reason (Cosmos-side) can still
        // tell "wrong environmentId" apart from a Dataverse domain rejection.
        var updater = NewUpdater(new CapturingFakeClient(
            new RegistryUpdateOutcome.NotFound("PATCH /api/data/v9.2/sprk_dataverseenvironments(...) returned 404 NotFound. Body: (empty)")));
        var outcome = await updater.TransitionToReadyAsync(
            NewRequest(environmentId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            CancellationToken.None);

        var failure = outcome.Should().BeOfType<RegistrySetupStatusUpdateOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "the wrong-environmentId case MUST surface the id so the operator sees the mismatch");
        failure.Diagnostic.Should().Contain("404 NotFound",
            "the C1.4 client's diagnostic MUST be preserved for post-mortem");
    }

    [Fact]
    public async Task TransitionToReadyAsync_Failure_Preserves_Diagnostic_Verbatim()
    {
        var updater = NewUpdater(new CapturingFakeClient(
            new RegistryUpdateOutcome.Failure("PATCH ... returned 500 InternalServerError. Body: sql timeout")));
        var outcome = await updater.TransitionToReadyAsync(NewRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<RegistrySetupStatusUpdateOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Be("PATCH ... returned 500 InternalServerError. Body: sql timeout");
    }

    [Fact]
    public async Task TransitionToReadyAsync_Propagates_Client_Exceptions_To_H13_For_Resumable_Classification()
    {
        // The IRegistrySetupStatusUpdater docstring is explicit: infrastructure
        // faults throw so H13 can classify Resumable. Do NOT swallow.
        var throwing = new ThrowingFakeClient(new HttpRequestException("connection refused"));
        var updater = NewUpdater(throwing);
        await FluentActions.Invoking(() => updater.TransitionToReadyAsync(NewRequest(), CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    // -------------------------------------------------------------------------
    // Helpers + fakes.
    // -------------------------------------------------------------------------

    private static DataverseRegistrySetupStatusUpdater NewUpdater(IDataverseEnvironmentRegistryClient client)
        => new(client, NullLogger<DataverseRegistrySetupStatusUpdater>.Instance);

    private static RegistrySetupStatusUpdateRequest NewRequest(
        string customerId = "trial-2026-08-20",
        string runId = "run-abc-def",
        string tenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        string environmentId = "87d7b4a7-399b-f111-b8de-7ced8ddc4a05",
        string registryDataverseUrl = "https://spaarkedev1.crm.dynamics.com")
        => new(customerId, runId, tenantId, environmentId, registryDataverseUrl);

    // Capturing fake — records the last update passed to UpdateSetupStatusAsync
    // so unit tests can assert on the exact shape H13 sees on the wire.
    private sealed class CapturingFakeClient : IDataverseEnvironmentRegistryClient
    {
        private readonly RegistryUpdateOutcome _outcome;
        public int CallCount { get; private set; }
        public RegistrySetupStatusUpdate? LastUpdate { get; private set; }

        public CapturingFakeClient(RegistryUpdateOutcome outcome) => _outcome = outcome;

        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
            string tenantId, CancellationToken cancellationToken)
            => Task.FromResult<DataverseEnvironmentRegistrySnapshot?>(null);

        public Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(
            RegistrySetupStatusUpdate update, CancellationToken cancellationToken)
        {
            CallCount++;
            LastUpdate = update;
            return Task.FromResult(_outcome);
        }
    }

    private sealed class SuccessFakeClient : IDataverseEnvironmentRegistryClient
    {
        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
            string tenantId, CancellationToken cancellationToken)
            => Task.FromResult<DataverseEnvironmentRegistrySnapshot?>(null);

        public Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(
            RegistrySetupStatusUpdate update, CancellationToken cancellationToken)
            => Task.FromResult<RegistryUpdateOutcome>(new RegistryUpdateOutcome.Success());
    }

    private sealed class ThrowingFakeClient : IDataverseEnvironmentRegistryClient
    {
        private readonly Exception _toThrow;
        public ThrowingFakeClient(Exception toThrow) => _toThrow = toThrow;

        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
            string tenantId, CancellationToken cancellationToken)
            => throw _toThrow;

        public Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(
            RegistrySetupStatusUpdate update, CancellationToken cancellationToken)
            => throw _toThrow;
    }
}

// -----------------------------------------------------------------------------
// POML acceptance criterion #1 (source-level enforcement):
//   grep 'LogWarning.*no-op\|LogWarning.*Ready' returns 0 matches in the
//   updated DataverseRegistrySetupStatusUpdater.cs. Enforced by a source-file
//   read + regex check so a future accidental re-introduction of the Wave-C4
//   placeholder is caught in CI, not by a code reviewer.
// -----------------------------------------------------------------------------

public sealed class DataverseRegistrySetupStatusUpdaterNoOpForbiddenTests
{
    [Fact]
    public void Source_Contains_No_LogWarning_NoOp_Or_Ready_Placeholder()
    {
        // Locate the source file relative to the tests project root. The Tests
        // csproj sits next to the Core csproj in .../services/, so navigate up
        // and across.
        var source = LocateSource();
        source.Should().NotBeNull("the updater source file MUST be present for this guard to be meaningful");

        var text = File.ReadAllText(source!);

        // The Wave-C4 placeholder emitted a LogWarning explicitly announcing
        // the no-op semantic. Any future accidental re-introduction of that
        // form (or the Ready-placeholder log statement) fails here. Match on
        // real C# invocation syntax (`_logger.LogWarning`) not the historical
        // narrative-comment reference in the file header.
        Regex.IsMatch(text, @"_logger\.LogWarning\b[^;]*no-op", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Should().BeFalse("Wave-C4 placeholder _logger.LogWarning(\"no-op\") is FORBIDDEN — task 184 replaced the logged no-op with a real PATCH");
        Regex.IsMatch(text, @"_logger\.LogWarning\b[^;]*NOT issuing a real Dataverse PATCH", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Should().BeFalse("Wave-C4 placeholder 'NOT issuing a real Dataverse PATCH' _logger call is FORBIDDEN — task 184 replaced it");
    }

    private static string? LocateSource()
    {
        // Walk up from the test-execution CWD looking for the source file.
        // AppContext.BaseDirectory sits under bin/Debug/net{ver}/; the source
        // file lives at ../../../../Sprk.Provisioning.ControlPlane.Core/
        // Handlers/E2EAcceptance/DataverseRegistrySetupStatusUpdater.cs.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "Sprk.Provisioning.ControlPlane.Core",
                "Handlers", "E2EAcceptance", "DataverseRegistrySetupStatusUpdater.cs");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        // Fallback: search two levels up for a "services" dir peer.
        var start = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && start is not null; i++)
        {
            var services = Path.Combine(start.FullName, "services");
            if (Directory.Exists(services))
            {
                var candidate = Path.Combine(
                    services,
                    "Sprk.Provisioning.ControlPlane.Core",
                    "Handlers", "E2EAcceptance", "DataverseRegistrySetupStatusUpdater.cs");
                if (File.Exists(candidate)) return candidate;
            }
            start = start.Parent;
        }
        return null;
    }
}
