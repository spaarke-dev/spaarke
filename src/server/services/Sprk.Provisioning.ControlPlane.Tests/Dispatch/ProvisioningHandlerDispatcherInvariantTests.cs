// -----------------------------------------------------------------------------
// ProvisioningHandlerDispatcherInvariantTests.cs
//
// L2 CONTROL-PLANE dispatcher-invariants gate (task 102, Phase C'' Wave G-1).
//
// PURPOSE:
//   Freeze tests over correctness invariants of ProvisioningHandlerDispatcher
//   that MUST NOT drift silently:
//
//     1. MaxConcurrentCallsPerSession == 1 (DS-2b R3 forcing function).
//        Session serialization is the whole reason the dispatcher is
//        session-aware. Widening this value (via config, code edit,
//        refactor accident) silently reintroduces the systematic
//        branch-join ETag-conflict class every alternative dispatch
//        strategy has to survive -- see DS-2b Sec.0-1. Kept as a
//        HARD-CODED const inside ProvisioningHandlerDispatcher rather
//        than a DispatcherOptions knob; this test asserts on the
//        BuildSessionProcessorOptions output (an internal helper exposed
//        via InternalsVisibleTo("Sprk.Provisioning.ControlPlane.Tests")
//        on the .Core csproj -- but the helper lives on the dispatcher
//        which is in .Worker; see below for why the extern alias is
//        needed).
//
//     2. Zero compile references to the Sprk.Bff.Api assembly from either
//        the .Core or .Worker assemblies (parent-task contract from Phase C''
//        Wave G-1 task 102 instructions -- L2 is a PEER service, not a BFF
//        extension; ADR-010 + project MUST rule). Detected by reflection on
//        the actual runtime AssemblyReferences of both DLLs -- the RIGHT
//        surface, because a compile-time reference to Sprk.Bff.Api's
//        IJobHandler type would REQUIRE a project reference that shows up
//        here. Prose comments that reference the type by name in doc-string
//        prose (as context for readers explaining L2's peer relationship to
//        BFF) do NOT constitute compile references and are not flagged.
//
// ADR-038 alignment:
//   - KEEP category: unit test (in-process; no external resource; no
//     Mock<HttpMessageHandler>). The dispatcher's static
//     BuildSessionProcessorOptions is a pure function; the contract test
//     scans committed source files -- both are stateless + fast.
//   - Not a DI-registration-only ctor-null-check test -- these assert
//     LOAD-BEARING correctness properties, exactly the type of freeze
//     test ADR-038 §7 KEEP category #6 (integrity guard) sanctions.
//
// EXTERN ALIAS:
//   ProvisioningHandlerDispatcher lives in Sprk.Provisioning.ControlPlane.Worker
//   which is aliased as `WorkerHost` in the test csproj (see
//   Sprk.Provisioning.ControlPlane.Tests.csproj comment on the Worker
//   ProjectReference -- both .Api and .Worker generate an implicit top-
//   level-statement Program class in the global namespace, so an
//   unaliased Worker reference makes every WebApplicationFactory<Program>
//   usage project-wide ambiguous). This test uses `WorkerHost::` to reach
//   into the aliased assembly.
// -----------------------------------------------------------------------------

extern alias WorkerHost;

using System.Linq;
using System.Reflection;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Dispatch;
using Sprk.Provisioning.ControlPlane.Handlers;
using Xunit;
using DispatcherType = WorkerHost::Sprk.Provisioning.ControlPlane.Worker.Dispatch.ProvisioningHandlerDispatcher;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

/// <summary>
/// Freeze-test suite over <c>ProvisioningHandlerDispatcher</c>'s correctness
/// invariants: MaxConcurrentCallsPerSession = 1 (DS-2b R3) and zero
/// compile references to Sprk.Bff.Api.IJobHandler across .Core + .Worker.
/// </summary>
public sealed class ProvisioningHandlerDispatcherInvariantTests
{
    // -------------------------------------------------------------------------
    // Freeze test 1 -- MaxConcurrentCallsPerSession is HARD-CODED to 1
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildSessionProcessorOptions_HardCodesMaxConcurrentCallsPerSessionToOne()
    {
        // Arrange -- ANY valid DispatcherOptions must yield
        // MaxConcurrentCallsPerSession == 1. Vary the tunable knobs to
        // prove no combination of config values can widen the invariant.
        var options = new DispatcherOptions
        {
            Enabled = true,
            MaxConcurrentCustomers = 8,             // Non-default; proves this ISN'T the knob that controls per-session.
            MaxHandlerDuration = TimeSpan.FromMinutes(90),
            SessionIdleTimeout = TimeSpan.FromSeconds(45),
            MaxDeliveryCount = 3,
        };

        // Act -- the exact code path ExecuteAsync uses to construct the
        // processor options. BuildSessionProcessorOptions is internal-visible
        // to this test project via InternalsVisibleTo on the Worker's Core
        // reference chain.
        ServiceBusSessionProcessorOptions built = DispatcherType.BuildSessionProcessorOptions(options);

        // Assert -- DS-2b Sec.9 R3 forcing function.
        built.MaxConcurrentCallsPerSession.Should().Be(
            1,
            "MaxConcurrentCallsPerSession is a HARD-CODED correctness invariant per DS-2b Sec.9 R3 -- " +
            "widening it silently reintroduces the systematic branch-join ETag-conflict class that " +
            "session serialization was chosen to eliminate. If this test fails, someone made the " +
            "value config-driven or hand-edited it -- revert; do NOT weaken the guard.");

        // Also assert the const surface itself so a refactor that renames /
        // moves the constant still trips this test.
        DispatcherType.MaxConcurrentCallsPerSessionInvariant.Should().Be(
            1,
            "The internal MaxConcurrentCallsPerSessionInvariant const is the single source of truth " +
            "for the DS-2b R3 rule -- BuildSessionProcessorOptions reads from it.");
    }

    [Fact]
    public void BuildSessionProcessorOptions_MirrorsDispatcherOptionsForTunableFields()
    {
        // Arrange
        var options = new DispatcherOptions
        {
            Enabled = true,
            MaxConcurrentCustomers = 6,
            MaxHandlerDuration = TimeSpan.FromMinutes(45),
            SessionIdleTimeout = TimeSpan.FromSeconds(15),
            MaxDeliveryCount = 7,
        };

        // Act
        ServiceBusSessionProcessorOptions built = DispatcherType.BuildSessionProcessorOptions(options);

        // Assert -- the TUNABLE fields flow through untouched; only
        // MaxConcurrentCallsPerSession is fixed. This test guards against
        // the opposite drift: someone hard-coding MaxConcurrentSessions
        // or MaxHandlerDuration would defeat operator control of fleet
        // concurrency + long-handler headroom.
        built.MaxConcurrentSessions.Should().Be(options.MaxConcurrentCustomers);
        built.MaxAutoLockRenewalDuration.Should().Be(options.MaxHandlerDuration);
        built.SessionIdleTimeout.Should().Be(options.SessionIdleTimeout);
        built.AutoCompleteMessages.Should().BeFalse(
            "Wire-level idempotency requires explicit Complete/DeadLetter/Abandon per DS-2 §1.5.");
        built.PrefetchCount.Should().Be(
            0,
            "Long handlers (H6 30-60 min, H9 15-30 min) must never prefetch -- a prefetched message " +
            "whose lock renewal fails is a redelivery for no reason per DS-2 §1.5.");
        built.ReceiveMode.Should().Be(
            ServiceBusReceiveMode.PeekLock,
            "PeekLock is required for the dispatcher to own settle decisions -- ReceiveAndDelete would " +
            "delete the message before Cosmos outcome-apply lands.");
    }

    // -------------------------------------------------------------------------
    // Freeze test 2 -- Zero assembly reference from .Core + .Worker to Sprk.Bff.Api
    // -------------------------------------------------------------------------

    [Fact]
    public void CoreAssembly_HasZeroReferenceToSprkBffApi()
    {
        // ARRANGE -- pick any type known to live in .Core to grab the
        // assembly. IProvisioningHandler is stable + present in every L2 build.
        var coreAssembly = typeof(IProvisioningHandler).Assembly;
        coreAssembly.GetName().Name.Should().Be(
            "Sprk.Provisioning.ControlPlane.Core",
            "expected IProvisioningHandler to live in the .Core assembly -- if this fails, " +
            "the .Core split has been renamed and this contract test needs re-anchoring.");

        AssertAssemblyHasNoBffApiReference(coreAssembly);
    }

    [Fact]
    public void WorkerAssembly_HasZeroReferenceToSprkBffApi()
    {
        // ARRANGE -- pick a Worker-only type via the extern alias.
        var workerAssembly = typeof(DispatcherType).Assembly;
        workerAssembly.GetName().Name.Should().Be(
            "Sprk.Provisioning.ControlPlane.Worker",
            "expected ProvisioningHandlerDispatcher to live in the .Worker assembly -- if this " +
            "fails, the .Worker split has been renamed and this contract test needs re-anchoring.");

        AssertAssemblyHasNoBffApiReference(workerAssembly);
    }

    private static void AssertAssemblyHasNoBffApiReference(Assembly assembly)
    {
        // ACT -- enumerate the runtime AssemblyReferences of the target
        // assembly. A compile-time reference to any Sprk.Bff.Api type
        // (including IJobHandler) would REQUIRE a project reference from
        // this project to Sprk.Bff.Api, which in turn produces exactly ONE
        // entry named "Sprk.Bff.Api" in this list. Absence of that entry
        // is a stronger, structurally-grounded proof than any source-file
        // grep could give -- and immune to false positives from doc-comment
        // prose that references the type by name for context.
        var bffApiReferences = assembly
            .GetReferencedAssemblies()
            .Where(name => name.Name?.Equals("Sprk.Bff.Api", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        // ASSERT -- parent-task contract: zero compile references. Contract
        // test per task 102 parent instructions #7 + spec.md MUST rule (L2
        // cannot reference the BFF assembly per ADR-010 + project MUST rule).
        // L2's dispatcher uses its OWN IProvisioningHandler contract; the
        // BFF's IJobHandler is a BFF-internal type unreachable from L2 as
        // long as this test passes.
        bffApiReferences.Should().BeEmpty(
            $"{assembly.GetName().Name} MUST NOT compile-reference Sprk.Bff.Api (project MUST rule + " +
            "CLAUDE.md §10 + ADR-010). L2 is a PEER service, not a BFF extension. Any need to " +
            "share types must go through duplication (as with HandlerEnvelope / IProvisioningHandler " +
            "mirroring BFF shape without a project reference) OR promotion of the shared type into " +
            "a neutral shared library -- NEVER a BFF -> L2 reference.");
    }
}
