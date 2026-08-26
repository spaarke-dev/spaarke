// -----------------------------------------------------------------------------
// E2EAcceptanceCompositionRootTests.cs
//
// L2 CONTROL-PLANE Phase C'' Wave G-7 Batch G-7E TERMINAL acceptance test --
// framework-level proof that the 15 constituent checks of the H13 E2E
// acceptance gate are wired to REAL implementations in the REAL Worker
// composition root (no PlaceholderTrapVerifier, no PlaceholderInvariantVerifier,
// no logged-no-op Ready writer, no shell-out CostEnvelopeChecker/Validation
// Runner/NamingConformanceChecker remaining registered).
//
// WHY THIS TEST EXISTS (task 186):
//   Task 186's charter is to demonstrate for the FIRST TIME (per the
//   r1-gap-analysis + POML "task 089 for real this time" framing) that r1's
//   stated E2E goal (spec.md FR-18 / SC #5: fresh customer environment reaches
//   sprk_dataverseenvironment.sprk_setupstatus = Ready via the new pipeline)
//   is achievable via actual code execution -- not a claimed/simulated pass.
//   Live execution is deferred to owner ceremony (per task 162 precedent -- L2
//   Worker App Service does not yet exist on Azure). What CAN be proven
//   pre-ceremony is the FRAMEWORK-LEVEL claim: the aggregation surface H13's
//   Ready-transition depends on is wired to real code, not placeholders. That
//   is exactly what this test asserts.
//
// STRATEGY -- the REAL Worker DI container (parity with
// HandlerRegistrationCompletenessTests):
//   Uses WorkerTestFactory (introduced by task 103 for the same purpose) so the
//   exact production composition root (Sprk.Provisioning.ControlPlane.Worker/
//   Program.cs -> E2EAcceptanceModule.AddH13E2EAcceptanceGateHandler) is
//   exercised. Duplicating the module's DI registrations in a hand-rolled
//   ServiceCollection would test a fictional container that silently drifts
//   from production wiring -- exactly the failure mode this test exists to
//   prevent.
//
// WHY NO NETWORK CALLS / NO HANDLER EXECUTION:
//   Only constructor-graph resolution is exercised (GetService / GetKeyedService
//   never invokes HandleAsync). WorkerTestFactory strips every IHostedService
//   registration before the host builds, so the reconciler + crash-recovery
//   background services never touch Cosmos. Every collaborator seam is
//   RESOLVED (constructor runs) but never USED -- the seams' actual behavior
//   is covered by their own per-seam unit tests (tasks 170/171/172/173/174/
//   175/176/177/178/179/180/181/182/183/184/185).
//
// COVERAGE (task 186 acceptance criteria mapped to test cases):
//   CR1  H13 handler class resolves from real Worker DI.
//   CR2  IE2ETrapVerifier == CompositeTrapVerifier (task 185; PlaceholderTrap
//        Verifier not registered).
//   CR3  IE2EInvariantVerifier == CompositeInvariantVerifier (task 174;
//        PlaceholderInvariantVerifier not registered).
//   CR4  IRegistrySetupStatusUpdater == DataverseRegistrySetupStatusUpdater
//        (task 184 -- the acceptance-target Ready-transition writer;
//        Wave-C4 logged-no-op not registered).
//   CR5  ICostEnvelopeChecker == ArmCostEnvelopeChecker (task 183; retired
//        AzCliCostEnvelopeChecker shell-out not registered).
//   CR6  INamingConformanceChecker == NamingConformanceChecker (task 182;
//        retired NamingConformanceScriptRunner shell-out not registered).
//   CR7  IE2EValidationRunner == E2EValidationRunner (task 181; retired
//        ValidateDeployedEnvironmentScriptRunner shell-out not registered).
//   CR8  All 6 ITrapProbe kinds are registered exactly once (T1-T6, tasks
//        171/177/178/180/172/175) with the expected concrete types.
//   CR9  All 5 IInvariantProbe kinds are registered exactly once (I1-I5,
//        tasks 170/173/174/176/179) with the expected concrete types.
//   CR10 The composite verifiers accept the resolved probe collections
//        without construction-time exceptions (composition-time contract
//        held: no duplicate registration).
//
// ADR-038 alignment:
//   - KEEP category: integration seam (unit-tier). REAL Worker composition
//     root, zero network, zero credentials, zero Cosmos.
//   - Not a DI-registration-only ctor-null-check test -- this asserts the
//     CONCRETE resolved TYPES (which impl is behind each abstract seam), a
//     regression-catching shape no other test in the suite covers for the
//     E2E acceptance surface end-to-end. The complementary AC-1 happy-path
//     test in H13E2EAcceptanceGateHandlerTests proves the handler's own
//     orchestration; this test proves the DI graph the handler runs against
//     is the REAL graph, not a placeholder-backed simulation.
// -----------------------------------------------------------------------------

// extern alias required -- see Sprk.Provisioning.ControlPlane.Tests.csproj
// comment on the aliased Worker ProjectReference (parity with
// HandlerRegistrationCompletenessTests): both .Api and .Worker's top-level-
// statement Program class land in the GLOBAL namespace, so an unaliased Worker
// reference would make WebApplicationFactory<Program> ambiguous project-wide.
extern alias WorkerHost;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Registry;
using Sprk.Provisioning.ControlPlane.Tests.Dispatch;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.E2EAcceptance;

/// <summary>
/// Wave G-7 Batch G-7E terminal composition-root gate: asserts every H13
/// collaborator seam is wired to its REAL Wave-G-7 implementation in the
/// production Worker DI container -- no PlaceholderTrapVerifier, no
/// PlaceholderInvariantVerifier, no logged-no-op Ready writer, no shell-out
/// runners. This test is r1's framework-level proof that spec.md FR-18 / SC #5
/// (the acceptance-target Ready transition) is achievable via code execution;
/// live-Azure demonstration is deferred to owner ceremony per task 162's
/// live-verification precedent.
/// </summary>
public sealed class E2EAcceptanceCompositionRootTests
    : IClassFixture<WorkerTestFactory>
{
    private readonly WorkerTestFactory _factory;

    public E2EAcceptanceCompositionRootTests(WorkerTestFactory factory)
    {
        _factory = factory;
    }

    // ---------- CR1 -- H13 handler resolves ----------

    [Fact]
    public void CR1_H13Handler_ResolvesFromRealWorkerDI()
    {
        using var scope = _factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetService<H13E2EAcceptanceGateHandler>();

        handler.Should().NotBeNull(
            "the H13 acceptance gate handler is the sole authority for the " +
            "sprk_setupstatus = Ready transition (spec.md FR-18 / SC #5). If " +
            "E2EAcceptanceModule.AddH13E2EAcceptanceGateHandler stops being " +
            "called from Worker/Program.cs, every downstream provisioning run " +
            "silently loses its terminal acceptance gate.");
    }

    // ---------- CR2 -- Composite trap verifier ----------

    [Fact]
    public void CR2_TrapVerifier_IsCompositeTrapVerifier_NotPlaceholder()
    {
        using var scope = _factory.Services.CreateScope();

        var verifier = scope.ServiceProvider.GetRequiredService<IE2ETrapVerifier>();

        verifier.Should().BeOfType<CompositeTrapVerifier>(
            "task 185 (Wave G-7 Batch G-7D) swaps IE2ETrapVerifier from the " +
            "Wave-C4 PlaceholderTrapVerifier (which returned InfraFault for " +
            "every trap kind) to CompositeTrapVerifier composing the 6 real " +
            "per-trap probes. A regression here would silently return the H13 " +
            "trap surface to Resumable-forever, blocking every Ready transition.");
    }

    // ---------- CR3 -- Composite invariant verifier ----------

    [Fact]
    public void CR3_InvariantVerifier_IsCompositeInvariantVerifier_NotPlaceholder()
    {
        using var scope = _factory.Services.CreateScope();

        var verifier = scope.ServiceProvider.GetRequiredService<IE2EInvariantVerifier>();

        verifier.Should().BeOfType<CompositeInvariantVerifier>(
            "task 174 (Wave G-7 Batch G-7A1) swaps IE2EInvariantVerifier from " +
            "PlaceholderInvariantVerifier to CompositeInvariantVerifier " +
            "composing the 5 real per-invariant probes (I1-I5, tasks 170/173/" +
            "174/176/179). A regression here would silently return the H13 " +
            "invariant surface to Resumable-forever.");
    }

    // ---------- CR4 -- Real Ready writer (THE acceptance-target transition) ----------

    [Fact]
    public void CR4_RegistrySetupStatusUpdater_IsRealDataverseWriter_NotWaveC4Placeholder()
    {
        using var scope = _factory.Services.CreateScope();

        var updater = scope.ServiceProvider.GetRequiredService<IRegistrySetupStatusUpdater>();

        updater.Should().BeOfType<DataverseRegistrySetupStatusUpdater>(
            "task 184 (Wave G-7 Batch G-7C) is THE acceptance-target: it " +
            "swaps IRegistrySetupStatusUpdater from the Wave-C4 logged-no-op " +
            "to a real Web API PATCH via the C1.4 IDataverseEnvironmentRegistry" +
            "Client. A regression here would silently return the sprk_setupstatus " +
            "= Ready write to a no-op, permanently voiding spec.md FR-18 / SC #5 " +
            "regardless of every other handler's correctness. This is the exact " +
            "DS-4 §6 overstatement (Wave-C4 logged-no-op claimed as acceptance " +
            "delivery) that the Phase C'' build exists to correct.");
    }

    // ---------- CR5 -- ARM SDK cost checker ----------

    [Fact]
    public void CR5_CostEnvelopeChecker_IsArmSdkPort_NotAzCliShellOut()
    {
        using var scope = _factory.Services.CreateScope();

        var checker = scope.ServiceProvider.GetRequiredService<ICostEnvelopeChecker>();

        checker.Should().BeOfType<ArmCostEnvelopeChecker>(
            "task 183 (Wave G-7 Batch G-7A2.2) replaces AzCliCostEnvelopeChecker " +
            "(az costmanagement query shell-out) with an Azure.ResourceManager." +
            "CostManagement SDK port per DS-4 section 6. The retired " +
            "AzCliCostEnvelopeChecker remains on disk unregistered per the " +
            "project retirement convention; regressing to it would silently " +
            "reintroduce the shell-out failure modes DS-4 audited.");
    }

    // ---------- CR6 -- Pure-C# naming-conformance checker ----------

    [Fact]
    public void CR6_NamingConformanceChecker_IsPureCsharpPort_NotScriptRunner()
    {
        using var scope = _factory.Services.CreateScope();

        var checker = scope.ServiceProvider.GetRequiredService<INamingConformanceChecker>();

        checker.Should().BeOfType<NamingConformanceChecker>(
            "task 182 (Wave G-7 Batch G-7A1) replaces NamingConformanceScriptRunner " +
            "(pwsh shell-out) with a pure-C# port per DS-4 section 6 (this " +
            "script has 0 az/REST calls; the port is a trivial mechanical " +
            "translation). The retired script runner remains on disk " +
            "unregistered per the project retirement convention.");
    }

    // ---------- CR7 -- Pure-C# E2E validation runner ----------

    [Fact]
    public void CR7_E2EValidationRunner_IsPureCsharpPort_NotScriptRunner()
    {
        using var scope = _factory.Services.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<IE2EValidationRunner>();

        runner.Should().BeOfType<E2EValidationRunner>(
            "task 181 (Wave G-7 Batch G-7B) replaces ValidateDeployedEnvironment" +
            "ScriptRunner (pwsh shell-out) with a pure-C# HttpClient port per " +
            "DS-4 section 6. Zero ProcessStartInfo / pwsh dependency in the " +
            "shipped runner; the retired script runner remains on disk " +
            "unregistered per the project retirement convention.");
    }

    // ---------- CR8 -- All 6 ITrapProbe kinds wired with expected concrete types ----------

    public static TheoryData<TrapKind, Type> ExpectedTrapProbes()
    {
        // Task 185 module wires exactly these 6 concrete types (see
        // E2EAcceptanceModule.AddH13E2EAcceptanceGateHandler); each row here
        // matches a Wave-G-7 probe task. Duplicating the mapping in the test is
        // deliberate -- the test acts as an anti-drift gate for the module.
        return new TheoryData<TrapKind, Type>
        {
            { TrapKind.T1KeyVaultReferenceIdentity, typeof(KeyVaultReferenceIdentityT1Probe) },
            { TrapKind.T2DataverseAppUser,          typeof(DataverseAppUserPairT2Probe) },
            { TrapKind.T3GraphAppRoleParity,        typeof(GraphAppRoleParityT3Probe) },
            { TrapKind.T4ExchangePolicyCount,       typeof(ExchangePolicyCountT4Probe) },
            { TrapKind.T5SlotMiKvRbac,              typeof(T5SlotMiKvRbacTrapProbe) },
            { TrapKind.T6SpeConfidentialClient,     typeof(T6SpeConfidentialClientTrapProbe) },
        };
    }

    [Theory]
    [MemberData(nameof(ExpectedTrapProbes))]
    public void CR8_TrapProbe_ForEachKind_ResolvesToExpectedConcreteType(
        TrapKind kind, Type expectedType)
    {
        using var scope = _factory.Services.CreateScope();

        var probes = scope.ServiceProvider.GetServices<ITrapProbe>().ToList();
        var matches = probes.Where(p => p.Kind == kind).ToList();

        matches.Should().ContainSingle(
            $"exactly one ITrapProbe MUST be registered for {kind} (Wave G-7 " +
            "composite semantics: duplicate registrations throw at composition " +
            "time, no registrations return InfraFault forever). Observed probes: " +
            $"[{string.Join(", ", probes.Select(p => $"{p.Kind}={p.GetType().Name}"))}].");
        matches[0].Should().BeOfType(expectedType,
            $"the {kind} probe MUST be the Wave-G-7 real implementation, not a " +
            "test fake or Wave-C4 placeholder. Swapping implementations without " +
            "updating this test is exactly the silent-fail drift this gate " +
            "exists to catch.");
    }

    [Fact]
    public void CR8_TrapProbes_TotalRegistrationCount_Equals6()
    {
        using var scope = _factory.Services.CreateScope();

        var probes = scope.ServiceProvider.GetServices<ITrapProbe>().ToList();

        probes.Should().HaveCount(6,
            "exactly 6 ITrapProbe registrations MUST exist post-Wave-G-7 (one " +
            "per TrapKind T1-T6). Under-registration re-inerts H13's trap " +
            "surface; over-registration would fail CompositeTrapVerifier's " +
            "duplicate-registration guard at construction time. Observed: " +
            $"[{string.Join(", ", probes.Select(p => $"{p.Kind}={p.GetType().Name}"))}].");
    }

    // ---------- CR9 -- All 5 IInvariantProbe kinds wired with expected concrete types ----------

    public static TheoryData<InvariantKind, Type> ExpectedInvariantProbes()
    {
        // Task 174 + module wire exactly these 5 concrete types; each row
        // matches a Wave-G-7 probe task. Anti-drift gate (see CR8 rationale).
        return new TheoryData<InvariantKind, Type>
        {
            { InvariantKind.I1NoHardcodedTenant,    typeof(PackagedScriptTenantLiteralInvariantProbe) },
            { InvariantKind.I2AiSearchTenantFilter, typeof(AiSearchTenantFilterInvariantProbe) },
            { InvariantKind.I3CosmosPartitionKey,   typeof(CosmosPartitionKeyInvariantProbe) },
            { InvariantKind.I4SpeContainerResolver, typeof(SpeContainerTenantDerivationInvariantProbe) },   // task 204c B07 SESSION 12 2026-08-26: swap from task-176 SpeContainerResolverInvariantProbe (BFF-diagnostic) to independent ARM app-settings re-verification per 204c dispatch principle
            { InvariantKind.I5GraphTokenTenant,     typeof(I5GraphTokenTenantScopeProbe) },
        };
    }

    [Theory]
    [MemberData(nameof(ExpectedInvariantProbes))]
    public void CR9_InvariantProbe_ForEachKind_ResolvesToExpectedConcreteType(
        InvariantKind kind, Type expectedType)
    {
        using var scope = _factory.Services.CreateScope();

        var probes = scope.ServiceProvider.GetServices<IInvariantProbe>().ToList();
        var matches = probes.Where(p => p.Kind == kind).ToList();

        matches.Should().ContainSingle(
            $"exactly one IInvariantProbe MUST be registered for {kind}. " +
            $"Observed probes: [{string.Join(", ", probes.Select(p => $"{p.Kind}={p.GetType().Name}"))}].");
        matches[0].Should().BeOfType(expectedType,
            $"the {kind} probe MUST be the Wave-G-7 real implementation, not a " +
            "placeholder or shell-out.");
    }

    [Fact]
    public void CR9_InvariantProbes_TotalRegistrationCount_Equals5()
    {
        using var scope = _factory.Services.CreateScope();

        var probes = scope.ServiceProvider.GetServices<IInvariantProbe>().ToList();

        probes.Should().HaveCount(5,
            "exactly 5 IInvariantProbe registrations MUST exist post-Wave-G-7 " +
            $"(one per InvariantKind I1-I5). Observed: [{string.Join(", ", probes.Select(p => $"{p.Kind}={p.GetType().Name}"))}].");
    }

    // ---------- CR10 -- Composite verifiers construct cleanly against real probe graph ----------

    [Fact]
    public void CR10_CompositeVerifiers_ResolveWithoutDuplicateRegistrationException()
    {
        // Resolving each composite forces its constructor to run against the
        // real probe collection -- if any TrapKind or InvariantKind had TWO
        // registrations, the CompositeTrapVerifier / CompositeInvariantVerifier
        // ctor throws InvalidOperationException at composition (silent-fail
        // guard per CompositeTrapVerifier's file header). Reaching a non-null
        // resolution proves the composition-time contract held.
        using var scope = _factory.Services.CreateScope();

        var trap = scope.ServiceProvider.GetRequiredService<IE2ETrapVerifier>();
        var invariant = scope.ServiceProvider.GetRequiredService<IE2EInvariantVerifier>();

        trap.Should().NotBeNull();
        invariant.Should().NotBeNull();
    }
}
