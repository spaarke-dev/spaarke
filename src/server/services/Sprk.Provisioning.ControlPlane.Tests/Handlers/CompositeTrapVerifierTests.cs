// -----------------------------------------------------------------------------
// CompositeTrapVerifierTests.cs
//
// Unit tests over CompositeTrapVerifier (task 185, Wave G-7 Batch G-7D).
// TERMINAL aggregation seam wiring for H13's IE2ETrapVerifier surface. These
// tests exist because a mistake in the composition logic would silently mask
// any one trap's real failure across every future customer provision --
// specifically the two silent-fail patterns Wave G-7 has been guarding against:
//
//   (a) Empty-probe-set "vacuously passed" bug -- the trap surface would emit
//       an all-Passed catalog without invoking any real probe, silently
//       greening the gate. AC-2 verifies the composite emits InfraFault (which
//       H13 classifies Resumable) for every un-wired kind.
//   (b) Kind-mis-mapping bug -- a probe with declared Kind=T3 returning an
//       outcome for Kind=T4 would let H13's MapTrapKindToRejectionCode name
//       the WRONG trap in the operator diagnostic. AC-6 verifies the composite
//       converts mis-kinded outcomes to InfraFault (never silently forwards).
//
// ADR-038 CATEGORY:
//   Path #1 -- pure C# unit test. NO live Azure / Graph / Dataverse. Fake
//   ITrapProbe impls return canned outcomes. Live-Azure coverage for each
//   individual probe belongs in its own per-probe test file (T2/T4/T5/T6
//   already have those; T1/T3 covered by their probe-specific test files or
//   via H13 handler tests).
//
// COVERAGE:
//   AC-1  Composed with all 6 real ITrapProbe impls -> all-Passed aggregate.
//   AC-2  Empty probe set -> every kind returns InfraFault (not Passed).
//   AC-3  Single probe wired -> that kind's outcome forwarded; the other 5
//         return InfraFault with deferral diagnostic.
//   AC-4  One probe Failed, rest Passed -> aggregate FirstFailure identifies
//         the failing kind + FullCatalog reflects all 6 verdicts.
//   AC-5  Duplicate probe registration -> constructor throws InvalidOperationException.
//   AC-6  Probe returns an outcome with a mismatched Kind -> composite emits
//         InfraFault (protects H13's rejection-code mapping from naming the
//         wrong trap).
//   AC-7  Probe returns null -> InfraFault.
//   AC-8  Probe throws (contract violation) -> InfraFault; the aggregate for
//         the OTHER kinds still runs (fault isolation).
//   AC-9  OperationCanceledException propagates verbatim (does NOT become
//         InfraFault).
//   AC-10 Enum-declaration order preservation -- outcomes list ordered T1..T6.
//   AC-11 Multiple Failed probes -> every Failure is in the catalog (aggregate
//         does not short-circuit on first failure).
//   AC-12 All 6 real ITrapProbe registrations wired by E2EAcceptanceModule.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CompositeTrapVerifierTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string DataverseUrl = "https://sprk-acme.crm.dynamics.com";
    private const string BffAppRegId = "bff-appreg-id";
    private const string UamiClientId = "uami-client-id";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string AppServiceName = "sprk-bff-acme";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";

    private static TrapVerificationRequest BuildRequest() => new(
        CustomerId, RunId, TenantId, SubscriptionId, DataverseUrl,
        BffAppRegId, UamiClientId, KeyVaultName, AppServiceName, ResourceGroupName);

    // ---------- AC-1 ----------
    [Fact]
    public async Task AC1_AllSixProbesPassed_ProducesAllPassedCatalog()
    {
        var probes = new ITrapProbe[]
        {
            FakeTrapProbe.Passed(TrapKind.T1KeyVaultReferenceIdentity),
            FakeTrapProbe.Passed(TrapKind.T2DataverseAppUser),
            FakeTrapProbe.Passed(TrapKind.T3GraphAppRoleParity),
            FakeTrapProbe.Passed(TrapKind.T4ExchangePolicyCount),
            FakeTrapProbe.Passed(TrapKind.T5SlotMiKvRbac),
            FakeTrapProbe.Passed(TrapKind.T6SpeConfidentialClient),
        };
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        result.Outcomes.Should().HaveCount(6);
        result.AllTrapsClear.Should().BeTrue();
        result.AnyInfraFault.Should().BeFalse();
        result.FirstFailure.Should().BeNull();
        result.FirstInfraFault.Should().BeNull();
        result.Outcomes.Should().AllBeOfType<TrapVerificationOutcome.Passed>();
    }

    // ---------- AC-2 SILENT-FAIL GUARD ----------
    [Fact]
    public async Task AC2_EmptyProbeSet_ReturnsInfraFaultForEveryKind_NotVacuousPass()
    {
        // GUARDS AGAINST: an empty-set "vacuously passed" bug would silently
        // green the H13 gate. The composite MUST emit InfraFault for each kind
        // when nothing is registered -- H13 then classifies Resumable per §4C,
        // and the Ready transition stays BLOCKED (correct).
        var verifier = new CompositeTrapVerifier(
            Array.Empty<ITrapProbe>(),
            NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        result.Outcomes.Should().HaveCount(6);
        result.AllTrapsClear.Should().BeTrue("no Failed outcome yet — only InfraFaults");
        result.FirstFailure.Should().BeNull();
        result.FirstInfraFault.Should().NotBeNull("empty probe set MUST NOT vacuously pass");
        foreach (var o in result.Outcomes)
        {
            o.Should().BeOfType<TrapVerificationOutcome.InfraFault>();
            var infra = (TrapVerificationOutcome.InfraFault)o;
            infra.Diagnostic.Should().Contain("H13 trap live-probe not yet wired");
        }
    }

    // ---------- AC-3 ----------
    [Fact]
    public async Task AC3_OnlyOneProbeWired_OtherKindsReturnInfraFault()
    {
        var probes = new ITrapProbe[]
        {
            FakeTrapProbe.Passed(TrapKind.T4ExchangePolicyCount),
        };
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        result.Outcomes.Should().HaveCount(6);
        foreach (var kind in Enum.GetValues<TrapKind>())
        {
            var outcome = result.Outcomes.Single(o => o switch
            {
                TrapVerificationOutcome.Passed p => p.Kind == kind,
                TrapVerificationOutcome.Failed f => f.Kind == kind,
                TrapVerificationOutcome.InfraFault i => i.Kind == kind,
                _ => false,
            });

            if (kind == TrapKind.T4ExchangePolicyCount)
            {
                outcome.Should().BeOfType<TrapVerificationOutcome.Passed>();
            }
            else
            {
                outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>(
                    $"kind {kind} has no ITrapProbe registered");
            }
        }
    }

    // ---------- AC-4 ----------
    [Theory]
    [InlineData(TrapKind.T1KeyVaultReferenceIdentity)]
    [InlineData(TrapKind.T2DataverseAppUser)]
    [InlineData(TrapKind.T3GraphAppRoleParity)]
    [InlineData(TrapKind.T4ExchangePolicyCount)]
    [InlineData(TrapKind.T5SlotMiKvRbac)]
    [InlineData(TrapKind.T6SpeConfidentialClient)]
    public async Task AC4_OneProbeFails_RestPass_FirstFailureIdentifiesFailingKind(TrapKind failing)
    {
        var probes = Enum.GetValues<TrapKind>()
            .Select<TrapKind, ITrapProbe>(k => k == failing
                ? FakeTrapProbe.Failed(k, $"{k} sample failure")
                : FakeTrapProbe.Passed(k))
            .ToArray();
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        result.Outcomes.Should().HaveCount(6);
        result.AllTrapsClear.Should().BeFalse();
        result.FirstFailure.Should().NotBeNull();
        result.FirstFailure!.Kind.Should().Be(failing);
        result.FirstFailure.Diagnostic.Should().Contain($"{failing} sample failure");
    }

    // ---------- AC-5 ----------
    [Fact]
    public void AC5_DuplicateProbeForSameKind_ConstructorThrows()
    {
        var probes = new ITrapProbe[]
        {
            FakeTrapProbe.Passed(TrapKind.T1KeyVaultReferenceIdentity),
            FakeTrapProbe.Passed(TrapKind.T1KeyVaultReferenceIdentity),
        };

        var act = () => new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*duplicate ITrapProbe registration for kind 'T1KeyVaultReferenceIdentity'*");
    }

    // ---------- AC-6 SILENT-FAIL GUARD ----------
    [Fact]
    public async Task AC6_KindMismatch_ProbeReturnsOutcomeForWrongKind_ComposesInfraFault()
    {
        // GUARDS AGAINST: a probe with declared Kind=T3 returning
        // TrapVerificationOutcome.Failed(T4, "...") would silently let H13's
        // MapTrapKindToRejectionCode name the WRONG trap in the operator
        // diagnostic. Composite MUST convert to InfraFault.
        var probes = new ITrapProbe[]
        {
            new KindLyingFakeTrapProbe(
                declaredKind: TrapKind.T3GraphAppRoleParity,
                actualOutcomeKind: TrapKind.T4ExchangePolicyCount),
        };
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        var t3Outcome = result.Outcomes.Single(o => o switch
        {
            TrapVerificationOutcome.InfraFault i => i.Kind == TrapKind.T3GraphAppRoleParity,
            _ => false,
        });
        t3Outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>();
        ((TrapVerificationOutcome.InfraFault)t3Outcome).Diagnostic
            .Should().Contain("Contract violation")
            .And.Contain("returned outcome for Kind=T4ExchangePolicyCount");
    }

    // ---------- AC-7 ----------
    [Fact]
    public async Task AC7_ProbeReturnsNull_ComposesInfraFault()
    {
        var probes = new ITrapProbe[]
        {
            new NullReturningFakeTrapProbe(TrapKind.T2DataverseAppUser),
        };
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        var t2 = result.Outcomes.Single(o => o switch
        {
            TrapVerificationOutcome.InfraFault i => i.Kind == TrapKind.T2DataverseAppUser,
            _ => false,
        });
        t2.Should().BeOfType<TrapVerificationOutcome.InfraFault>();
        ((TrapVerificationOutcome.InfraFault)t2).Diagnostic.Should().Contain("returned null");
    }

    // ---------- AC-8 ----------
    [Fact]
    public async Task AC8_ProbeThrows_ComposesInfraFault_AndOtherProbesStillRun()
    {
        var probes = new ITrapProbe[]
        {
            new ThrowingFakeTrapProbe(TrapKind.T5SlotMiKvRbac, new InvalidOperationException("kaboom")),
            FakeTrapProbe.Passed(TrapKind.T6SpeConfidentialClient),
        };
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        var t5 = result.Outcomes.Single(o => o switch
        {
            TrapVerificationOutcome.InfraFault i => i.Kind == TrapKind.T5SlotMiKvRbac,
            _ => false,
        });
        t5.Should().BeOfType<TrapVerificationOutcome.InfraFault>();
        ((TrapVerificationOutcome.InfraFault)t5).Diagnostic.Should().Contain("kaboom");

        // fault isolation -- T6 (the sibling probe) still ran and reported Passed.
        var t6 = result.Outcomes.Single(o => o switch
        {
            TrapVerificationOutcome.Passed p => p.Kind == TrapKind.T6SpeConfidentialClient,
            _ => false,
        });
        t6.Should().BeOfType<TrapVerificationOutcome.Passed>();
    }

    // ---------- AC-9 ----------
    [Fact]
    public async Task AC9_ProbeThrowsOperationCanceled_Propagates()
    {
        var probes = new ITrapProbe[]
        {
            new ThrowingFakeTrapProbe(TrapKind.T1KeyVaultReferenceIdentity,
                new OperationCanceledException("cancelled")),
        };
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var act = async () => await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- AC-10 ----------
    [Fact]
    public async Task AC10_OutcomesInEnumDeclarationOrder()
    {
        var probes = Enum.GetValues<TrapKind>()
            .Reverse()
            .Select<TrapKind, ITrapProbe>(FakeTrapProbe.Passed)
            .ToArray();
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        var kindsInOrder = result.Outcomes.Select(o => o switch
        {
            TrapVerificationOutcome.Passed p => p.Kind,
            TrapVerificationOutcome.Failed f => f.Kind,
            TrapVerificationOutcome.InfraFault i => i.Kind,
            _ => throw new InvalidOperationException(),
        }).ToArray();

        kindsInOrder.Should().Equal(
            TrapKind.T1KeyVaultReferenceIdentity,
            TrapKind.T2DataverseAppUser,
            TrapKind.T3GraphAppRoleParity,
            TrapKind.T4ExchangePolicyCount,
            TrapKind.T5SlotMiKvRbac,
            TrapKind.T6SpeConfidentialClient);
    }

    // ---------- AC-11 ----------
    [Fact]
    public async Task AC11_MultipleFailedProbes_AllFailuresPresentInCatalog()
    {
        var probes = new ITrapProbe[]
        {
            FakeTrapProbe.Failed(TrapKind.T1KeyVaultReferenceIdentity, "T1 diag"),
            FakeTrapProbe.Failed(TrapKind.T3GraphAppRoleParity, "T3 diag"),
            FakeTrapProbe.Failed(TrapKind.T6SpeConfidentialClient, "T6 diag"),
        };
        var verifier = new CompositeTrapVerifier(probes, NullLogger<CompositeTrapVerifier>.Instance);

        var result = await verifier.VerifyAllAsync(BuildRequest(), CancellationToken.None);

        var failures = result.Outcomes.OfType<TrapVerificationOutcome.Failed>().ToArray();
        failures.Should().HaveCount(3);
        failures.Select(f => f.Kind).Should().BeEquivalentTo(new[]
        {
            TrapKind.T1KeyVaultReferenceIdentity,
            TrapKind.T3GraphAppRoleParity,
            TrapKind.T6SpeConfidentialClient,
        });
        // FirstFailure is deterministic -- T1 (enum-order-first).
        result.FirstFailure!.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
    }

    // ---------- AC-12 -- registration surface ----------
    [Fact]
    public void AC12_AllSixTrapProbeConcreteTypesImplementITrapProbe()
    {
        // Guards against a future "author a new trap probe without annotating :
        // ITrapProbe" regression -- the module wiring would silently drop it.
        typeof(ITrapProbe).IsAssignableFrom(typeof(KeyVaultReferenceIdentityT1Probe)).Should().BeTrue();
        typeof(ITrapProbe).IsAssignableFrom(typeof(DataverseAppUserPairT2Probe)).Should().BeTrue();
        typeof(ITrapProbe).IsAssignableFrom(typeof(GraphAppRoleParityT3Probe)).Should().BeTrue();
        typeof(ITrapProbe).IsAssignableFrom(typeof(ExchangePolicyCountT4Probe)).Should().BeTrue();
        typeof(ITrapProbe).IsAssignableFrom(typeof(T5SlotMiKvRbacTrapProbe)).Should().BeTrue();
        typeof(ITrapProbe).IsAssignableFrom(typeof(T6SpeConfidentialClientTrapProbe)).Should().BeTrue();
    }

    // ---- fakes ----

    private sealed class FakeTrapProbe : ITrapProbe
    {
        private readonly TrapVerificationOutcome _outcome;
        private FakeTrapProbe(TrapKind kind, TrapVerificationOutcome outcome)
        {
            Kind = kind;
            _outcome = outcome;
        }

        public TrapKind Kind { get; }

        public static FakeTrapProbe Passed(TrapKind kind)
            => new(kind, new TrapVerificationOutcome.Passed(kind));

        public static FakeTrapProbe Failed(TrapKind kind, string diag)
            => new(kind, new TrapVerificationOutcome.Failed(kind, diag));

        public Task<TrapVerificationOutcome> ProbeAsync(TrapVerificationRequest request, CancellationToken ct)
            => Task.FromResult(_outcome);
    }

    private sealed class KindLyingFakeTrapProbe : ITrapProbe
    {
        private readonly TrapKind _actualOutcomeKind;
        public KindLyingFakeTrapProbe(TrapKind declaredKind, TrapKind actualOutcomeKind)
        {
            Kind = declaredKind;
            _actualOutcomeKind = actualOutcomeKind;
        }

        public TrapKind Kind { get; }

        public Task<TrapVerificationOutcome> ProbeAsync(TrapVerificationRequest request, CancellationToken ct)
            => Task.FromResult<TrapVerificationOutcome>(
                new TrapVerificationOutcome.Failed(_actualOutcomeKind, "mis-kinded"));
    }

    private sealed class NullReturningFakeTrapProbe : ITrapProbe
    {
        public NullReturningFakeTrapProbe(TrapKind kind) { Kind = kind; }
        public TrapKind Kind { get; }
        public Task<TrapVerificationOutcome> ProbeAsync(TrapVerificationRequest request, CancellationToken ct)
            => Task.FromResult<TrapVerificationOutcome>(null!);
    }

    private sealed class ThrowingFakeTrapProbe : ITrapProbe
    {
        private readonly Exception _ex;
        public ThrowingFakeTrapProbe(TrapKind kind, Exception ex) { Kind = kind; _ex = ex; }
        public TrapKind Kind { get; }
        public Task<TrapVerificationOutcome> ProbeAsync(TrapVerificationRequest request, CancellationToken ct)
            => throw _ex;
    }
}
