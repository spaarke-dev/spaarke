// -----------------------------------------------------------------------------
// HandlerEnqueuerUsageGuardTests.cs
//
// Structural guard (T186 L19, 2026-10-15): handler bodies must not enqueue
// other handlers.
//
// WHY:
//   Handler-to-handler chaining is owned by the state reconciler
//   (Reconciler/StateReconcilerService.cs + DagAdvancer.HandlerDependencies);
//   Enqueue/IHandlerEnqueuer.cs "NOT CONSUMED BY" forbids handler bodies from
//   re-enqueuing. Four wave-C4 handlers (H0, H1, H12b, H12c) kept a "temporary
//   bridge" that enqueued their successor themselves. Live in the T186 ACME
//   run H0 enqueued H0.5 with an empty payload (H0.5 needs a consent payload)
//   and failed every run; H1/H12c duplicated the reconciler's dispatch under a
//   different MessageId (defeating the dispatcher's Level-2 lock); H12b
//   dispatched H12c before its dependency H12a had completed.
//
// WHAT IT CHECKS:
//   No type under Sprk.Provisioning.ControlPlane.Handlers (handlers and their
//   sub-handlers/helpers) takes IHandlerEnqueuer as a constructor parameter or
//   holds one in a field, EXCEPT H05ConsentCaptureHandler: H0.5 and H0 are
//   DagAdvancer.EntryPointHandlers the reconciler never dispatches, so H0.5 ->
//   H0 is the designed Model 2 entry chain.
//
// Controls: the exemption must still hold an enqueuer (so the scan is not
// vacuous) and the scan must see the full handler set (so a namespace move
// cannot silently empty it).
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class HandlerEnqueuerUsageGuardTests
{
    private const string HandlersNamespacePrefix = "Sprk.Provisioning.ControlPlane.Handlers";

    private static readonly BindingFlags AllInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void OnlyTheConsentCaptureHandler_DependsOnIHandlerEnqueuer()
    {
        var offenders = HandlerSideTypes()
            .Where(t => t != typeof(H05ConsentCaptureHandler))
            .Where(HoldsEnqueuer)
            .Select(t => t.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "handler bodies must not re-enqueue (IHandlerEnqueuer 'NOT CONSUMED BY'); chaining is the " +
            "reconciler's (DagAdvancer.HandlerDependencies). Only H0.5 -> H0 (both entry points the " +
            "reconciler never dispatches) is exempt. Offenders: {0}", string.Join(", ", offenders));
    }

    [Fact]
    public void Control_ConsentCaptureHandler_StillHoldsTheEnqueuer_SoTheScanIsNotVacuous()
    {
        HoldsEnqueuer(typeof(H05ConsentCaptureHandler)).Should().BeTrue(
            "H0.5 chains into H0 via IHandlerEnqueuer; if that changes, retire the exemption in this guard.");
    }

    [Fact]
    public void Control_ScanCoversEveryDispatchableHandler()
    {
        var handlerTypes = typeof(IProvisioningHandler).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IProvisioningHandler).IsAssignableFrom(t))
            .ToList();

        handlerTypes.Should().HaveCountGreaterThanOrEqualTo(
            HandlerIds.Dispatchable.Count,
            "the scan must see every IProvisioningHandler implementation");
        handlerTypes.Should().OnlyContain(
            t => t.Namespace != null && t.Namespace.StartsWith(HandlersNamespacePrefix, StringComparison.Ordinal),
            "the scan covers the Handlers namespace only; a handler outside it would escape the guard");
        HandlerSideTypes().Should().Contain(handlerTypes);
    }

    private static IEnumerable<Type> HandlerSideTypes()
        => typeof(IProvisioningHandler).Assembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith(HandlersNamespacePrefix, StringComparison.Ordinal));

    private static bool HoldsEnqueuer(Type type)
    {
        var viaCtor = type.GetConstructors(AllInstance)
            .SelectMany(c => c.GetParameters())
            .Any(p => IsEnqueuer(p.ParameterType));
        var viaField = type.GetFields(AllInstance).Any(f => IsEnqueuer(f.FieldType));
        return viaCtor || viaField;
    }

    private static bool IsEnqueuer(Type t) => typeof(IHandlerEnqueuer).IsAssignableFrom(t);
}
