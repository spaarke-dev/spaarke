using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 081 (ontology review, 2026-10-22): <see cref="SignalClosureService"/> is not sealed only so the Procedure retire tests
/// can run its real selection and observe its close (<c>SelectOpenSignalsOfPolicyAsync</c> and <c>ClosePolicySignalsRetiredAsync</c> are
/// virtual). No production type may derive from it: an override would let one caller select or close a different Signal set from the one
/// the sweep, the admin retire close and the Procedure retire preview share ("preview = sweep").
/// </summary>
/// <remarks>
/// <para><b>Enforced</b> (reflection over the <c>Sprk.Bff.Api</c> assembly only, so the test-only subclasses and mocks do not count): no
/// type in it is a subclass of <see cref="SignalClosureService"/>, directly or through another subclass.</para>
/// <para><b>Not enforced:</b> other assemblies (a test project may derive), and a runtime proxy (Moq) built outside the BFF.</para>
/// <para><b>Controls.</b> <see cref="Detector_FiresOnASubclass"/> (must-fire) runs the detector over this assembly, which holds a direct and an
/// indirect subclass fixture; <see cref="Detector_DoesNotFireOnTheBaseOrAnUnrelatedType"/> (must-not-fire) shows the base type itself and
/// unrelated types are not reported, so a detector that stops matching fails instead of passing vacuously.</para>
/// </remarks>
public sealed class SignalClosureServiceDerivationGuardTests
{
    [Fact(DisplayName = "081: no Sprk.Bff.Api type derives from SignalClosureService")]
    public void NoBffTypeDerivesFromSignalClosureService()
    {
        var bffTypes = typeof(SignalClosureService).Assembly.GetTypes();
        Assert.Contains(typeof(SignalClosureService), bffTypes); // non-vacuity: the scan reads the assembly that declares it

        var offenders = Subclasses(bffTypes).Select(t => t.FullName).ToList();

        Assert.True(offenders.Count == 0,
            $"A Sprk.Bff.Api type derives from SignalClosureService: {string.Join(", ", offenders)}. It is unsealed for the Procedure retire tests " +
            "only; an override would let one caller select or close a different Signal set from the others (task 081, preview = sweep).");
    }

    [Fact(DisplayName = "081 must-fire: the detector reports a direct and an indirect subclass")]
    public void Detector_FiresOnASubclass()
    {
        var found = Subclasses(typeof(SignalClosureServiceDerivationGuardTests).Assembly.GetTypes());

        Assert.Contains(typeof(DirectSubclassControl), found);
        Assert.Contains(typeof(IndirectSubclassControl), found);
    }

    [Fact(DisplayName = "081 must-not-fire: the base type and unrelated types are not reported")]
    public void Detector_DoesNotFireOnTheBaseOrAnUnrelatedType()
    {
        var found = Subclasses(new[] { typeof(SignalClosureService), typeof(SignalClosureOutcome), typeof(UnrelatedControl), typeof(object) });

        Assert.Empty(found);
    }

    // ── Detector ─────────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<Type> Subclasses(IEnumerable<Type> types) =>
        types.Where(t => t.IsSubclassOf(typeof(SignalClosureService))).ToList();

    // ── Control fixtures (never instantiated) ────────────────────────────────────────────────────────

    private class DirectSubclassControl : SignalClosureService
    {
        public DirectSubclassControl() : base(null!, null!, null!, null!) { }
    }

    private sealed class IndirectSubclassControl : DirectSubclassControl
    {
    }

    private sealed class UnrelatedControl
    {
    }
}
