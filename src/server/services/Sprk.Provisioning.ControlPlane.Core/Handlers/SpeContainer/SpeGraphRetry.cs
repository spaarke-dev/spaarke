// -----------------------------------------------------------------------------
// SpeGraphRetry.cs
//
// T186 audit of H8 — bounded retries for the two kinds of Graph transient H8 meets:
//   (1) a container created moments ago that the replica serving the next call has
//       not seen yet (404 / itemNotFound) — the business-unit stamp PATCH and its
//       read-back, and /activate; and
//   (2) a transient service answer on the verifier's GET (429, 5xx, a client-side
//       timeout, a dropped connection) — never a reason to quarantine.
//
// PATTERN PARITY: the same shape as Handlers/EntraAppReg/GraphAppRegistrationProvisioner
// .RetryWhileReplicatingAsync (T186 L26/L27) — bounded attempts, a narrow transient
// predicate, delay and attempts from options. Only the predicate differs: it takes any
// Exception because the verifier's transients are not all ODataErrors. An error the
// predicate rejects, and the last error when attempts run out, propagate unchanged.
// -----------------------------------------------------------------------------

using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

internal static class SpeGraphRetry
{
    /// <summary>
    /// Runs <paramref name="call"/> up to <paramref name="attempts"/> times, waiting <paramref name="delay"/> between
    /// attempts, while <paramref name="isTransient"/> says the failure is transient. The caller's cancellation is never
    /// retried. Each attempt applies its own timeout (the call does — see <c>LinkedTimeout</c>).
    /// </summary>
    internal static async Task<T> RetryWhileAsync<T>(
        Func<CancellationToken, Task<T>> call,
        Func<Exception, bool> isTransient,
        int attempts,
        TimeSpan delay,
        ILogger logger,
        string operation,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested && isTransient(ex))
            {
                logger.LogInformation(
                    "H8 {Operation} attempt {Attempt}/{Max} failed transiently ({Error}); retrying in {Delay}.",
                    operation, attempt, attempts, Describe(ex), delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Graph's answer for a container created moments ago that has not replicated to the replica serving the request:
    /// 404 (<c>itemNotFound</c>). Nothing else — a 403, 400 or 5xx is a real answer and is not waited out here.
    /// </summary>
    internal static bool IsNotYetReplicated(Exception ex) => ex is ODataError { ResponseStatusCode: 404 };

    /// <summary>
    /// A failure that says nothing about the container itself: throttling (429), a 5xx, a dropped connection, or the
    /// per-attempt timeout (an <see cref="OperationCanceledException"/> while the caller has NOT cancelled).
    /// </summary>
    internal static bool IsTransientServiceFault(Exception ex, CancellationToken callerToken) => ex switch
    {
        ODataError odata => odata.ResponseStatusCode is 429 or >= 500,
        HttpRequestException => true,
        TimeoutException => true,
        OperationCanceledException => !callerToken.IsCancellationRequested,
        _ => false,
    };

    internal static string Describe(Exception ex) => ex is ODataError odata
        ? $"Graph {odata.ResponseStatusCode} {odata.Error?.Code}"
        : $"{ex.GetType().Name}: {ex.Message}";
}
