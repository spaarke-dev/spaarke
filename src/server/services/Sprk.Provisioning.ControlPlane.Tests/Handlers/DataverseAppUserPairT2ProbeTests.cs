// -----------------------------------------------------------------------------
// DataverseAppUserPairT2ProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for DataverseAppUserPairT2Probe (task 177,
// Wave G-7 Batch G-7A1). ADR-038 path #1 — pure C# unit tests over a hand-
// rolled fake IDataverseAppUserVerifier (the seam H10 also uses; NOT Mock<T>
// per testing.md's ban). No live Dataverse / HttpClient / DefaultAzureCredential —
// the real DataverseWebApiAppUserVerifier's HttpClient half is exercised by
// H10SeamsSmokeTests.cs's env-gated live tests (task 143), not this file.
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  Passing real-world condition (BOTH halves resolved) → Passed(T2).
//   T2  BFF-half missing (count=0) → Failed(T2) with pair-mismatch diagnostic.
//   T3  UAMI-half missing (count=0) → Failed(T2) with pair-mismatch diagnostic.
//   T4  BOTH halves missing → Failed(T2) mentions both.
//   T5  UAMI half returned duplicate (count=3) → Failed(T2).
//   T6a Missing tenantId → InfraFault(T2). Verifier never called.
//   T6b Missing dataverseUrl → InfraFault(T2). Verifier never called.
//   T6c Missing bffAppRegId → InfraFault(T2). Verifier never called.
//   T6d Missing uamiClientId → InfraFault(T2). Verifier never called.
//   T7  Verifier THROWS on BFF-half → InfraFault(T2) mentions exception type.
//   T8  Verifier THROWS on UAMI-half → InfraFault(T2) mentions exception type.
//   T9  Kind property is TrapKind.T2DataverseAppUser (constant contract).
//   T10 Cancellation (OperationCanceledException) propagates — never
//       swallowed as InfraFault.
//   T11 Both halves are independently invoked with the correct applicationId
//       (regression guard: probe MUST not confuse BFF vs UAMI id).
//   T12 auth-v4 §10.4 byte-equality extension (task 205d / punch row A41,
//       2026-08-26): byte-equality match -> Passed + "byte-equality verified"
//       log; byte-equality mismatch (observed == UAMI CLIENT ID, the exact
//       trap) -> Failed naming the trap; observed null -> Failed; UamiObjectId
//       not supplied -> degrades to pre-A41 count-only Passed + "DEGRADED" log.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseAppUserPairT2ProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string DataverseUrl = "https://sprk-acme.crm.dynamics.com";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string UamiObjectId = "66666666-7777-8888-9999-000000000000";

    // ---------- T1 happy path ----------

    [Fact]
    public async Task ProbeAsync_BothHalvesResolved_ReturnsPassed()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.Verified("sys-uami-1"));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T2DataverseAppUser);
        verifier.CallCount.Should().Be(2);
    }

    // ---------- T2 BFF-half missing ----------

    [Fact]
    public async Task ProbeAsync_BffHalfCountZero_ReturnsFailedWithPairDiagnostic()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.CountMismatch(0),
            uami: new DataverseAppUserVerificationResult.Verified("sys-uami-1"));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T2DataverseAppUser);
        failed.Diagnostic.Should().Contain("VIOLATED")
            .And.Contain(BffAppRegId).And.Contain("COUNT=0")
            .And.Contain(UamiClientId).And.Contain("OK (count=1)")
            .And.Contain(DataverseUrl).And.Contain(TenantId);
        verifier.CallCount.Should().Be(2);
    }

    // ---------- T3 UAMI-half missing ----------

    [Fact]
    public async Task ProbeAsync_UamiHalfCountZero_ReturnsFailedWithPairDiagnostic()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.CountMismatch(0));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T2DataverseAppUser);
        failed.Diagnostic.Should().Contain("VIOLATED")
            .And.Contain(BffAppRegId).And.Contain("OK (count=1)")
            .And.Contain(UamiClientId).And.Contain("COUNT=0");
    }

    // ---------- T4 both halves missing ----------

    [Fact]
    public async Task ProbeAsync_BothHalvesCountZero_ReturnsFailedMentioningBoth()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.CountMismatch(0),
            uami: new DataverseAppUserVerificationResult.CountMismatch(0));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain(BffAppRegId).And.Contain("COUNT=0")
            .And.Contain(UamiClientId);
    }

    // ---------- T5 UAMI duplicate rows ----------

    [Fact]
    public async Task ProbeAsync_UamiHalfDuplicateRows_ReturnsFailedMentioningDuplicateCount()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.CountMismatch(3));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("COUNT=3");
    }

    // ---------- T6 input guards ----------

    [Fact]
    public async Task ProbeAsync_MissingTenantId_ReturnsInfraFaultAndDoesNotCallVerifier()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("x"),
            uami: new DataverseAppUserVerificationResult.Verified("y"));
        var probe = BuildProbe(verifier);
        var request = BuildRequest() with { TenantId = "" };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T2DataverseAppUser);
        infra.Diagnostic.Should().Contain("TenantId").And.Contain("I1");
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_MissingDataverseUrl_ReturnsInfraFaultAndDoesNotCallVerifier()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("x"),
            uami: new DataverseAppUserVerificationResult.Verified("y"));
        var probe = BuildProbe(verifier);
        var request = BuildRequest() with { DataverseUrl = "  " };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("DataverseUrl").And.Contain("H5/H6");
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_MissingBffAppRegId_ReturnsInfraFaultAndDoesNotCallVerifier()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("x"),
            uami: new DataverseAppUserVerificationResult.Verified("y"));
        var probe = BuildProbe(verifier);
        var request = BuildRequest() with { BffAppRegId = "" };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("BffAppRegId").And.Contain("H3");
        verifier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_MissingUamiClientId_ReturnsInfraFaultAndDoesNotCallVerifier()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("x"),
            uami: new DataverseAppUserVerificationResult.Verified("y"));
        var probe = BuildProbe(verifier);
        var request = BuildRequest() with { UamiClientId = "" };

        var outcome = await probe.ProbeAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("UamiClientId").And.Contain("H2a");
        verifier.CallCount.Should().Be(0);
    }

    // ---------- T7-T8 verifier throws ----------

    [Fact]
    public async Task ProbeAsync_VerifierThrowsOnBffHalf_ReturnsInfraFault()
    {
        var verifier = new FakeVerifier(throwOnAppId: BffAppRegId,
            ex: new InvalidOperationException("simulated network fault"));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should().Contain("BFF-half").And.Contain("InvalidOperationException")
            .And.Contain("simulated network fault");
    }

    [Fact]
    public async Task ProbeAsync_VerifierThrowsOnUamiHalf_ReturnsInfraFault()
    {
        var verifier = new FakeVerifier(throwOnAppId: UamiClientId,
            ex: new InvalidOperationException("uami re-query blew up"));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should().Contain("UAMI-half").And.Contain("InvalidOperationException")
            .And.Contain("uami re-query blew up");
    }

    // ---------- T9 kind constant ----------

    [Fact]
    public void Kind_IsT2DataverseAppUser()
    {
        var probe = BuildProbe(new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("x"),
            uami: new DataverseAppUserVerificationResult.Verified("y")));

        probe.Kind.Should().Be(TrapKind.T2DataverseAppUser);
    }

    // ---------- T10 cancellation ----------

    [Fact]
    public async Task ProbeAsync_Cancelled_PropagatesOperationCanceled()
    {
        var verifier = new FakeVerifier(throwOnAppId: BffAppRegId,
            ex: new OperationCanceledException("caller cancelled"));
        var probe = BuildProbe(verifier);

        var act = async () => await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- T11 correct-id-per-half ----------

    [Fact]
    public async Task ProbeAsync_InvokesVerifierWithCorrectAppIdPerHalf()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.Verified("sys-uami-1"));
        var probe = BuildProbe(verifier);

        await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        verifier.InvokedAppIds.Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
    }

    // ---------- T12 byte-equality extension (task 205d / punch row A41, auth-v4 §10.4) ----------

    [Fact]
    public async Task ProbeAsync_ByteEqualityMatches_ReturnsPassed_LogsByteEqualityVerified()
    {
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.Verified("sys-uami-1", UamiObjectId));
        var logger = new CapturingLogger();
        var probe = new DataverseAppUserPairT2Probe(verifier, logger);

        var outcome = await probe.ProbeAsync(BuildRequestWithUamiObjectId(UamiObjectId), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T2DataverseAppUser);
        logger.Entries.Should().Contain(e => e.Message.Contains("byte-equality verified"));
    }

    [Fact]
    public async Task ProbeAsync_ByteEqualityMismatch_ReturnsFailed_NamesTheTrap()
    {
        // The exact §10.4 trap: azureactivedirectoryobjectid holds the UAMI's
        // CLIENT ID instead of its principalId. Both are valid-shaped GUIDs;
        // the count check alone (T1-T11 above) would report this as PASSED.
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.Verified("sys-uami-1", UamiClientId));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequestWithUamiObjectId(UamiObjectId), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T2DataverseAppUser);
        failed.Diagnostic.Should().Contain("§10.4")
            .And.Contain("byte-equal")
            .And.Contain(UamiObjectId)
            .And.Contain(UamiClientId)
            .And.Contain("single most-missed item",
                "the diagnostic MUST name the exact trap shape so an operator recognizes it immediately");
    }

    [Fact]
    public async Task ProbeAsync_ByteEqualityObservedNull_ReturnsFailed()
    {
        // Verifier returned no azureactivedirectoryobjectid at all (e.g. a
        // pre-A41 row that was never explicitly set) — also a mismatch, not a
        // pass, because the app-only call would still 401 for the same reason.
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.Verified("sys-uami-1", AzureActiveDirectoryObjectId: null));
        var probe = BuildProbe(verifier);

        var outcome = await probe.ProbeAsync(BuildRequestWithUamiObjectId(UamiObjectId), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("(null/empty)");
    }

    [Fact]
    public async Task ProbeAsync_UamiObjectIdNotSupplied_DegradesToCountOnly_LogsDegraded()
    {
        // Backward compatibility: a caller that has not been upgraded to pass
        // UamiObjectId gets the pre-A41 count-only verdict, not a failure —
        // but the log MUST distinguish this from a genuine byte-equality pass.
        var verifier = new FakeVerifier(
            bff: new DataverseAppUserVerificationResult.Verified("sys-bff-1"),
            uami: new DataverseAppUserVerificationResult.Verified("sys-uami-1", UamiClientId)); // "wrong" value, irrelevant here
        var logger = new CapturingLogger();
        var probe = new DataverseAppUserPairT2Probe(verifier, logger);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None); // BuildRequest() has UamiObjectId = ""

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
        logger.Entries.Should().Contain(e => e.Message.Contains("DEGRADED"));
    }

    // ---------- helpers ----------

    private static DataverseAppUserPairT2Probe BuildProbe(IDataverseAppUserVerifier verifier) =>
        new(verifier, NullLogger<DataverseAppUserPairT2Probe>.Instance);

    private static TrapVerificationRequest BuildRequest() => new(
        CustomerId: CustomerId,
        RunId: RunId,
        TenantId: TenantId,
        SubscriptionId: SubscriptionId,
        DataverseUrl: DataverseUrl,
        BffAppRegId: BffAppRegId,
        UamiClientId: UamiClientId,
        KeyVaultName: string.Empty,
        AppServiceName: string.Empty,
        ResourceGroupName: string.Empty);

    private static TrapVerificationRequest BuildRequestWithUamiObjectId(string uamiObjectId) =>
        BuildRequest() with { UamiObjectId = uamiObjectId };

    /// <summary>
    /// Minimal <see cref="ILogger{T}"/> capture — parity with H10SeamsSmokeTests.cs's
    /// CapturingLogger pattern (plain in-memory recorder over the production
    /// ILogger contract, not a mocking-framework double per ADR-038).
    /// </summary>
    private sealed class CapturingLogger : ILogger<DataverseAppUserPairT2Probe>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// Fake <see cref="IDataverseAppUserVerifier"/> — no HttpClient / no
    /// DefaultAzureCredential. Returns canned outcomes per applicationId,
    /// throws for a specified id, and records every invocation so the probe's
    /// two-half query semantics can be asserted directly (parity with
    /// DataverseWebApiSolutionVerifierTests.cs's FakeHandler pattern but at
    /// the seam-interface level, since IDataverseAppUserVerifier IS the seam
    /// the probe injects — no HttpClient layer to fake).
    /// </summary>
    private sealed class FakeVerifier : IDataverseAppUserVerifier
    {
        private readonly DataverseAppUserVerificationResult? _bff;
        private readonly DataverseAppUserVerificationResult? _uami;
        private readonly string? _throwOnAppId;
        private readonly Exception? _ex;

        public int CallCount { get; private set; }
        public List<string> InvokedAppIds { get; } = new();

        public FakeVerifier(
            DataverseAppUserVerificationResult bff,
            DataverseAppUserVerificationResult uami)
        {
            _bff = bff;
            _uami = uami;
        }

        public FakeVerifier(string throwOnAppId, Exception ex)
        {
            _throwOnAppId = throwOnAppId;
            _ex = ex;
        }

        public Task<DataverseAppUserVerificationResult> VerifyAsync(
            string environmentUrl, string tenantId, string applicationId, CancellationToken cancellationToken)
        {
            CallCount++;
            InvokedAppIds.Add(applicationId);

            if (_throwOnAppId is not null && string.Equals(_throwOnAppId, applicationId, StringComparison.Ordinal))
            {
                throw _ex!;
            }

            // If a throw is configured on the OTHER half only, return a
            // successful outcome for this half so the probe proceeds to the
            // half where it will throw.
            if (_throwOnAppId is not null)
            {
                return Task.FromResult<DataverseAppUserVerificationResult>(
                    new DataverseAppUserVerificationResult.Verified("sys-passthrough-1"));
            }

            // Canonical happy path — pick the right canned outcome per id.
            var result = string.Equals(applicationId, BffAppRegId, StringComparison.Ordinal)
                ? _bff
                : _uami;
            return Task.FromResult(result!);
        }
    }
}
