// -----------------------------------------------------------------------------
// IFailureClassifier.cs
//
// L2 CONTROL-PLANE policy seam that classifies a handler outcome (either a
// <see cref="Sprk.Provisioning.ControlPlane.Handlers.HandlerResult.Failure"/>
// or an escaped <see cref="Exception"/>) into a
// <see cref="Sprk.Provisioning.ControlPlane.Handlers.FailureClass"/> per
// design.md §4C.
//
// PURPOSE:
//   Provides a single authoritative mapping — new failure categories are added
//   in ONE place, not scattered across every consumer. The reconciler (task
//   058) resolves this seam per-tick + delegates classification here rather
//   than pattern-matching exception types inline.
//
// DESIGN REF:
//   - spec.md FR-24 + design.md §4C.
//   - CLAUDE.md §11 component justification: this seam avoids duplicating the
//     exception-to-class ternary across 17 handlers + reconciler + tests.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <summary>
/// Domain policy that maps handler outcomes / escaped exceptions to a
/// <see cref="FailureClass"/> per the design.md §4C rollback taxonomy.
/// </summary>
public interface IFailureClassifier
{
    /// <summary>
    /// Classifies a returned <see cref="HandlerResult.Failure"/> — pass-through
    /// of the failure's own declared <see cref="FailureClass"/> (handlers own
    /// the domain classification per <see cref="IProvisioningHandler"/>
    /// contract). Provided as a method rather than a raw <c>failure.Class</c>
    /// access so the reconciler dispatch path always routes through the seam
    /// (allows future centralized override / audit hooks without breaking
    /// callers).
    /// </summary>
    /// <param name="failure">Handler-declared failure outcome. Non-null.</param>
    /// <returns>The failure's own §4C classification.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure"/> is null.</exception>
    FailureClass Classify(HandlerResult.Failure failure);

    /// <summary>
    /// Classifies an ESCAPED (thrown) exception the handler failed to convert
    /// into a <see cref="HandlerResult.Failure"/>. Per the
    /// <see cref="IProvisioningHandler"/> contract handlers SHOULD NOT throw
    /// on domain failures — but transient infrastructure failures
    /// (HTTP transport, cancellation, timeout) may escape. This method maps
    /// them per §4C:
    /// <list type="bullet">
    ///   <item><see cref="OperationCanceledException"/> re-thrown — cancellation is not a domain failure.</item>
    ///   <item><see cref="TimeoutException"/> / <see cref="HttpRequestException"/> / <see cref="System.Net.Sockets.SocketException"/> -> <see cref="FailureClass.Resumable"/> (infrastructure hiccup).</item>
    ///   <item>Any other exception -> <see cref="FailureClass.Resumable"/> — the SAFE default per POML step 2 guidance ("resolve external precondition"). NEVER auto-quarantines on unknown exception (silent quarantine would strand runs).</item>
    /// </list>
    /// </summary>
    /// <param name="exception">Escaped exception from the handler. Non-null.</param>
    /// <returns>§4C classification.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Re-thrown as-is — cancellation propagates.</exception>
    FailureClass ClassifyException(Exception exception);
}
