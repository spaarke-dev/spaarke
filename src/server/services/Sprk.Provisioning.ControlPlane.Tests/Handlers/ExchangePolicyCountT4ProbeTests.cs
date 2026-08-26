// -----------------------------------------------------------------------------
// ExchangePolicyCountT4ProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for ExchangePolicyCountT4Probe (task 180,
// Wave G-7 -- pipelined with H14a / task 114 + task 161). ADR-038 path #1 --
// pure C# unit tests with a fake IExchangePolicyReadClient (the same
// substitute-a-seam pattern task 177's T2 sibling probe uses for
// IDataverseAppUserVerifier).
//
// COVERAGE (per POML acceptance criteria + T4 verdict classifier):
//   V1  PASSED (exact parity)                -- both expected AppIds present, no extras.
//   V2  PASSED (with extras)                 -- both present + additional AppIds; still Passed.
//   V3  FAILED (missing both, empty tenant)  -- observed set entirely empty.
//   V4  FAILED (missing both, others present) -- observed has non-expected AppIds only.
//   V5  FAILED (missing BFF only).
//   V6  FAILED (missing UAMI only).
//   V7  Case-insensitive AppId membership (ARM/Graph sometimes upper-cases).
//   V8  InfraFault -- missing TenantId (I1 guard).
//   V9  InfraFault -- missing BffAppRegId (guard).
//   V10 InfraFault -- missing UamiClientId (guard).
//   V11 InfraFault -- read client returns Failure (pass-through diagnostic).
//   V12 InfraFault -- read client throws (contract violation, defensively caught).
//   V13 Cancellation propagates.
//   V14 Kind is TrapKind.T4ExchangePolicyCount.
//   V15 Classifier -- pinned unit tests for T4Verdict shapes.
//
// SILENT-FAIL AUDIT (parity with sibling T-probe self-audits):
//   * A false Pass on any Failed shape ships a broken tenant to Ready --
//     the ENTIRE point of H13 R7 "assert EFFECTS not intentions" is to catch
//     the case where H14a self-reports success but the write silently didn't
//     land. V3/V4/V5/V6 pin every distinct Failed shape's diagnostic to make
//     silent regression impossible.
//   * Never surface a partial-truth Success -- V11's read-client-Failure
//     path MUST NOT return Passed just because "we saw two AppIds" (we
//     didn't; the read failed).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ExchangePolicyCountT4ProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string DataverseUrl = "https://sprk-acme.crm.dynamics.com";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string ExtraAppId1 = "99999999-8888-7777-6666-555555555555";
    private const string ExtraAppId2 = "88888888-7777-6666-5555-444444444444";
    private const string PolicyScopeGroupId = "77777777-8888-9999-0000-111111111111";

    private static TrapVerificationRequest BuildRequest(
        string? tenantId = null,
        string? bffAppRegId = null,
        string? uamiClientId = null) => new(
        CustomerId: CustomerId,
        RunId: RunId,
        TenantId: tenantId ?? TenantId,
        SubscriptionId: SubscriptionId,
        DataverseUrl: DataverseUrl,
        BffAppRegId: bffAppRegId ?? BffAppRegId,
        UamiClientId: uamiClientId ?? UamiClientId,
        KeyVaultName: "sprk-acme-prod-kv",
        AppServiceName: "sprk-acme-prod-bff",
        ResourceGroupName: "rg-spaarke-acme-prod");

    private static ExchangePolicyCountT4Probe BuildProbe(IExchangePolicyReadClient client)
        => new(client, NullLogger<ExchangePolicyCountT4Probe>.Instance);

    private static ExchangePolicyReadOutcome.Success SuccessWith(params string[] appIds)
        => new(
            ObservedAppIds: appIds,
            Policies: appIds.Select(a => new ExchangePolicyEntry(
                AppId: a,
                Description: $"Spaarke-Provisioning-AppAccessPolicy-{a}",
                PolicyScopeGroupId: PolicyScopeGroupId)).ToArray());

    // ---------- V1 PASSED (exact parity) ----------

    [Fact]
    public async Task V1_BothExpectedPresent_NoExtras_ReturnsPassed()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(BffAppRegId, UamiClientId) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var passed = outcome.Should().BeOfType<TrapVerificationOutcome.Passed>().Subject;
        passed.Kind.Should().Be(TrapKind.T4ExchangePolicyCount);
        fake.CallCount.Should().Be(1);
        fake.LastRequest!.TenantId.Should().Be(TenantId);
        fake.LastRequest.CorrelationId.Should().Be(RunId);
    }

    // ---------- V2 PASSED with extras ----------

    [Fact]
    public async Task V2_BothExpectedPresent_WithExtras_ReturnsPassed()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(BffAppRegId, UamiClientId, ExtraAppId1, ExtraAppId2) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T4ExchangePolicyCount);
    }

    // ---------- V3 FAILED (missing both, empty tenant) ----------

    [Fact]
    public async Task V3_MissingBoth_EmptyObservedSet_ReturnsFailed_WithMissingBothDiagnostic()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith() };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T4ExchangePolicyCount);
        failed.Diagnostic.Should().Contain("MISSING BOTH")
            .And.Contain(TenantId)
            .And.Contain(BffAppRegId)
            .And.Contain(UamiClientId)
            .And.Contain("403");
    }

    // ---------- V4 FAILED (missing both, others present) ----------

    [Fact]
    public async Task V4_MissingBoth_OthersPresent_ReturnsFailed_WithNoExpectedButOthersDiagnostic()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(ExtraAppId1, ExtraAppId2) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("NO EXPECTED (BUT OTHERS PRESENT)")
            .And.Contain("2 OTHER AppId");
    }

    // ---------- V5 FAILED (missing BFF only) ----------

    [Fact]
    public async Task V5_MissingBffOnly_ReturnsFailed_WithMissingOneBffDiagnostic()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(UamiClientId) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("MISSING ONE (BFF)")
            .And.Contain("BFF app-registration is unprotected");
    }

    // ---------- V6 FAILED (missing UAMI only) ----------

    [Fact]
    public async Task V6_MissingUamiOnly_ReturnsFailed_WithMissingOneUamiDiagnostic()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(BffAppRegId) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should().Contain("MISSING ONE (UAMI)")
            .And.Contain("UAMI client is unprotected");
    }

    // ---------- V7 Case-insensitive AppId membership ----------

    [Fact]
    public async Task V7_ObservedAppIdsUpperCased_StillMatchesExpected_CaseInsensitive()
    {
        var fake = new FakeReadClient
        {
            Outcome = SuccessWith(BffAppRegId.ToUpperInvariant(), UamiClientId.ToUpperInvariant()),
        };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
    }

    // ---------- V8/V9/V10 InfraFault -- missing required input ----------

    [Fact]
    public async Task V8_MissingTenantId_ReturnsInfraFault_I1Guard_NoReadClientCall()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(BffAppRegId, UamiClientId) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(tenantId: ""), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should().Contain("TenantId").And.Contain("I1");
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task V9_MissingBffAppRegId_ReturnsInfraFault_NoReadClientCall()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(BffAppRegId, UamiClientId) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(bffAppRegId: ""), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should().Contain("BffAppRegId");
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task V10_MissingUamiClientId_ReturnsInfraFault_NoReadClientCall()
    {
        var fake = new FakeReadClient { Outcome = SuccessWith(BffAppRegId, UamiClientId) };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(uamiClientId: ""), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should().Contain("UamiClientId");
        fake.CallCount.Should().Be(0);
    }

    // ---------- V11 InfraFault -- read client returns Failure ----------

    [Fact]
    public async Task V11_ReadClientReturnsFailure_ReturnsInfraFault_PassingThroughDiagnostic()
    {
        var fake = new FakeReadClient
        {
            Outcome = new ExchangePolicyReadOutcome.Failure(
                "Sidecar POST /policies transport failure: Connection refused."),
        };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T4ExchangePolicyCount);
        infra.Diagnostic.Should().Contain("verdict deferred").And.Contain("Connection refused");
    }

    // ---------- V12 InfraFault -- read client throws (contract violation) ----------

    [Fact]
    public async Task V12_ReadClientThrows_ReturnsInfraFault_DefensivelyCaught()
    {
        var fake = new FakeReadClient
        {
            ThrowOnCall = new InvalidOperationException("test-contract-violation"),
        };
        var probe = BuildProbe(fake);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T4ExchangePolicyCount);
        infra.Diagnostic.Should().Contain("InvalidOperationException").And.Contain("test-contract-violation");
    }

    // ---------- V13 Cancellation ----------

    [Fact]
    public async Task V13_Cancellation_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fake = new FakeReadClient
        {
            ThrowOnCall = new OperationCanceledException(cts.Token),
        };
        var probe = BuildProbe(fake);

        var act = () => probe.ProbeAsync(BuildRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- V14 Kind ----------

    [Fact]
    public void V14_Kind_IsT4ExchangePolicyCount()
    {
        var probe = BuildProbe(new FakeReadClient { Outcome = SuccessWith() });
        probe.Kind.Should().Be(TrapKind.T4ExchangePolicyCount);
    }

    // ---------- V15 Classifier pins ----------

    [Fact]
    public void V15a_Classifier_BothPresent_NoExtras_ReturnsPassed()
    {
        var v = ExchangePolicyCountT4Probe.ClassifyOutcome(BffAppRegId, UamiClientId,
            new[] { BffAppRegId, UamiClientId });
        v.Kind.Should().Be(ExchangePolicyCountT4Probe.T4VerdictKind.Passed);
        v.Extras.Should().BeEmpty();
    }

    [Fact]
    public void V15b_Classifier_BothPresent_WithExtras_ReturnsPassedWithExtras()
    {
        var v = ExchangePolicyCountT4Probe.ClassifyOutcome(BffAppRegId, UamiClientId,
            new[] { BffAppRegId, UamiClientId, ExtraAppId1 });
        v.Kind.Should().Be(ExchangePolicyCountT4Probe.T4VerdictKind.PassedWithExtras);
        v.Extras.Should().ContainSingle().Which.Should().Be(ExtraAppId1);
    }

    [Fact]
    public void V15c_Classifier_MissingBoth_EmptySet_ReturnsFailedMissingBoth()
    {
        var v = ExchangePolicyCountT4Probe.ClassifyOutcome(BffAppRegId, UamiClientId, Array.Empty<string>());
        v.Kind.Should().Be(ExchangePolicyCountT4Probe.T4VerdictKind.FailedMissingBoth);
        v.MissingBff.Should().BeTrue();
        v.MissingUami.Should().BeTrue();
    }

    [Fact]
    public void V15d_Classifier_MissingBoth_OthersPresent_ReturnsFailedNoExpectedButOthersPresent()
    {
        var v = ExchangePolicyCountT4Probe.ClassifyOutcome(BffAppRegId, UamiClientId, new[] { ExtraAppId1 });
        v.Kind.Should().Be(ExchangePolicyCountT4Probe.T4VerdictKind.FailedNoExpectedButOthersPresent);
    }

    [Fact]
    public void V15e_Classifier_MissingBff_ReturnsFailedMissingOne_MissingBffTrue()
    {
        var v = ExchangePolicyCountT4Probe.ClassifyOutcome(BffAppRegId, UamiClientId, new[] { UamiClientId });
        v.Kind.Should().Be(ExchangePolicyCountT4Probe.T4VerdictKind.FailedMissingOne);
        v.MissingBff.Should().BeTrue();
        v.MissingUami.Should().BeFalse();
    }

    [Fact]
    public void V15f_Classifier_MissingUami_ReturnsFailedMissingOne_MissingUamiTrue()
    {
        var v = ExchangePolicyCountT4Probe.ClassifyOutcome(BffAppRegId, UamiClientId, new[] { BffAppRegId });
        v.Kind.Should().Be(ExchangePolicyCountT4Probe.T4VerdictKind.FailedMissingOne);
        v.MissingBff.Should().BeFalse();
        v.MissingUami.Should().BeTrue();
    }

    // ---------- Fake ----------

    private sealed class FakeReadClient : IExchangePolicyReadClient
    {
        public ExchangePolicyReadOutcome? Outcome { get; set; }
        public Exception? ThrowOnCall { get; set; }
        public int CallCount { get; private set; }
        public ExchangePolicyReadRequest? LastRequest { get; private set; }

        public Task<ExchangePolicyReadOutcome> ReadAsync(
            ExchangePolicyReadRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (ThrowOnCall is not null)
            {
                throw ThrowOnCall;
            }
            return Task.FromResult(Outcome ?? throw new InvalidOperationException("test setup: FakeReadClient.Outcome not set"));
        }
    }
}
