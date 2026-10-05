// -----------------------------------------------------------------------------
// T6SpeConfidentialClientTrapProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for T6SpeConfidentialClientTrapProbe (task 175;
// rewritten by task 248 — the owning app signs in through the Worker UAMI's
// federated credential, so the Key Vault certificate half is gone and the Graph
// half lists the run's container).
//
// ADR-038 CATEGORY:
//   Path #1 -- pure C# unit test. NO live Graph / KV. The Graph half is substituted
//   with FakeT6GraphAppOnlyProbe returning canned results; the real
//   GraphContainersListAppOnlyProbe has its own tests (GraphContainersListAppOnlyProbeTests).
//
// COVERAGE: pass (container listed); refused (trap phrase → Failed; other refusal,
// 404 replication window, seam throw → InfraFault); container absent → Failed;
// guards (no owner entry, no container id → InfraFault without a Graph call).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class T6SpeConfidentialClientTrapProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string BffAppRegId = "99999999-0000-0000-0000-00000000bf00";   // the customer BFF app — NOT the SPE owner
    private const string OwnerAppId = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";    // the container type's owning app
    private const string ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string ContainerId = "b!acme-container-0000000000000000000000000000000000000000000000";

    // ---------- PASS -- container listed ----------

    [Fact]
    public async Task ProbeAsync_ContainerListedAppOnlyAsTheOwner_ReturnsPassedT6()
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded);

        var outcome = await BuildProbe(graphProbe).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        graphProbe.CallCount.Should().Be(1);
        graphProbe.LastTenantId.Should().Be(TenantId);
        graphProbe.LastOwnerAppId.Should().Be(OwnerAppId,
            "T6 lists as the container type's owning app, never the customer BFF app (task 245b)");
        graphProbe.LastContainerTypeId.Should().Be(ContainerTypeId);
        graphProbe.LastContainerId.Should().Be(ContainerId, "the run's own container (H8 output) is what T6 looks for");
    }

    // ---------- FAIL -- container absent ----------

    [Fact]
    public async Task ProbeAsync_ListingSucceedsWithoutTheRunsContainer_ReturnsFailedT6()
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(
            new T6GraphAppOnlyProbeResult.ContainerAbsentResult(2, "listing has 2 other containers"));

        var outcome = await BuildProbe(graphProbe).ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        failed.Diagnostic.Should().Contain(ContainerId).And.Contain(OwnerAppId).And.Contain("cannot see it");
    }

    // ---------- REFUSED -- delegated-token trap phrase (T6 manifested) ----------

    [Fact]
    public async Task ProbeAsync_GraphReturnsDelegatedTokenTrap_ReturnsFailedT6()
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(
            new T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult(
                StatusCode: 403,
                Diagnostic: "Graph error code=InvalidClientToken message='Public client not allowed for this resource.'"));

        var outcome = await BuildProbe(graphProbe).ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        failed.Diagnostic.Should().Contain("T6 silent-fail trap MANIFESTED").And.Contain("403").And.Contain("FR-33");
    }

    // ---------- REFUSED / NO VERDICT -- InfraFault (Resumable in H13) ----------

    [Fact]
    public async Task ProbeAsync_GraphRefusesWithoutTrapPhrase_ReturnsInfraFaultT6()
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(
            new T6GraphAppOnlyProbeResult.InfraFaultResult(
                "Graph containers listing ODataError status=403: accessDenied Access denied."));

        var outcome = await BuildProbe(graphProbe).ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("verdict deferred").And.Contain("403");
    }

    [Fact]
    public async Task ProbeAsync_GraphReturnsReplicationPending_ReturnsInfraFaultT6_NotFailed()
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(
            new T6GraphAppOnlyProbeResult.ReplicationPendingResult(
                "Graph containers listing returned 404 Not Found (ResourceNotFound not found)."));

        var outcome = await BuildProbe(graphProbe).ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("replication window").And.Contain("NOT a T6 trap");
    }

    [Fact]
    public async Task ProbeAsync_GraphSeamThrows_ReturnsInfraFaultT6_NoLeak()
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithThrower(new InvalidCastException("rogue impl thrown"));

        var outcome = await BuildProbe(graphProbe).ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T6SpeConfidentialClient);
        infra.Diagnostic.Should().Contain("InvalidCastException").And.Contain("verdict deferred");
    }

    // ---------- GUARDS ----------

    [Theory]
    [InlineData("")]
    [InlineData("dddddddd-0000-0000-0000-000000000009")]
    public async Task ProbeAsync_NoOwnerForTheContainerType_ReturnsInfraFault_NoGraphCall(string containerTypeId)
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded);

        var outcome = await BuildProbe(graphProbe).ProbeAsync(
            BuildRequest() with { ContainerTypeId = containerTypeId }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>();
        graphProbe.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_NoContainerIdOnTheRun_ReturnsInfraFault_NoGraphCall()
    {
        var graphProbe = FakeT6GraphAppOnlyProbe.WithResult(T6GraphAppOnlyProbeResults.Succeeded);

        var outcome = await BuildProbe(graphProbe).ProbeAsync(
            BuildRequest() with { SpeContainerId = " " }, CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("SpeContainerId");
        graphProbe.CallCount.Should().Be(0);
    }

    // ---------- helpers ----------

    private static TrapVerificationRequest BuildRequest() => new(
        CustomerId: CustomerId,
        RunId: RunId,
        TenantId: TenantId,
        SubscriptionId: "sub-cus-acme-prod",
        DataverseUrl: "https://sprk-acme.crm.dynamics.com",
        BffAppRegId: BffAppRegId,
        UamiClientId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        KeyVaultName: "sprk-acme-prod-kv",
        AppServiceName: "sprk-bff-acme",
        ResourceGroupName: "rg-spaarke-acme-prod",
        ContainerTypeId: ContainerTypeId,
        SpeContainerId: ContainerId);

    private static T6SpeConfidentialClientTrapProbe BuildProbe(IT6GraphAppOnlyProbe graphProbe) => new(
        graphProbe,
        Options.Create(new SpeContainerOptions
        {
            ContainerTypeOwners = [new SpeContainerTypeOwner { ContainerTypeId = ContainerTypeId, OwnerAppId = OwnerAppId }],
        }),
        NullLogger<T6SpeConfidentialClientTrapProbe>.Instance);

    private sealed class FakeT6GraphAppOnlyProbe : IT6GraphAppOnlyProbe
    {
        private readonly T6GraphAppOnlyProbeResult? _result;
        private readonly Exception? _thrower;

        private FakeT6GraphAppOnlyProbe(T6GraphAppOnlyProbeResult? result, Exception? thrower)
        {
            _result = result;
            _thrower = thrower;
        }

        public static FakeT6GraphAppOnlyProbe WithResult(T6GraphAppOnlyProbeResult result) => new(result, thrower: null);

        public static FakeT6GraphAppOnlyProbe WithThrower(Exception ex) => new(result: null, thrower: ex);

        public int CallCount { get; private set; }

        public string? LastTenantId { get; private set; }

        public string? LastOwnerAppId { get; private set; }

        public string? LastContainerTypeId { get; private set; }

        public string? LastContainerId { get; private set; }

        public Task<T6GraphAppOnlyProbeResult> ProbeAsync(
            string tenantId, string ownerAppId, string containerTypeId, string containerId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastTenantId = tenantId;
            LastOwnerAppId = ownerAppId;
            LastContainerTypeId = containerTypeId;
            LastContainerId = containerId;
            if (_thrower is not null)
            {
                throw _thrower;
            }
            return Task.FromResult(_result!);
        }
    }
}
