// -----------------------------------------------------------------------------
// FailureClassifier.cs
//
// Default <see cref="IFailureClassifier"/> impl — pass-through for handler-
// declared failures + SAFE-default mapping for escaped exceptions per §4C.
//
// SAFETY POSTURE:
//   Unknown exceptions default to <see cref="FailureClass.Resumable"/> — the
//   SAFE default. Auto-quarantining on any unhandled exception would strand
//   runs on transient bugs; per spec.md FR-24 QuarantineRequired is reserved
//   for un-recoverable partial external state that the handler INTENTIONALLY
//   classifies (via <see cref="Handlers.HandlerResult.Failure"/>).
//
//   OperationCanceledException is RE-THROWN, not classified — cancellation
//   propagates through the async chain per .NET convention (shutdown / caller-
//   scoped CTS). This matches StateReconcilerService.RunTickAsync's own
//   catch-except-OCE pattern.
// -----------------------------------------------------------------------------

using System.Net.Http;
using System.Net.Sockets;
using Sprk.Provisioning.ControlPlane.Handlers;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <summary>
/// Production <see cref="IFailureClassifier"/> implementation.
/// </summary>
public sealed class FailureClassifier : IFailureClassifier
{
    /// <inheritdoc/>
    public FailureClass Classify(HandlerResult.Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Class;
    }

    /// <inheritdoc/>
    public FailureClass ClassifyException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Cancellation propagates — never classified (per BackgroundService
        // convention + StateReconcilerService.RunTickAsync's own catch shape).
        if (exception is OperationCanceledException)
        {
            throw exception;
        }

        // Transient infrastructure exceptions -> Resumable (external precondition
        // — the retry-after-external-fix class, not the auto-retry class). The
        // reconciler consults RollbackTransitions.ShouldReEnqueue to decide
        // whether to auto-retry (currently false for Resumable — operator resumes).
        //
        // If a future project introduces a well-known infrastructure exception
        // that is ALWAYS safe to auto-retry (e.g. an Azure SDK's typed
        // RequestFailedException with status 503), add a branch that returns
        // RetryableWithCleanup — but keep the SAFE-default at Resumable.
        return exception switch
        {
            TimeoutException => FailureClass.Resumable,
            HttpRequestException => FailureClass.Resumable,
            SocketException => FailureClass.Resumable,
            _ => FailureClass.Resumable,
        };
    }
}
