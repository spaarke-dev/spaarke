// -----------------------------------------------------------------------------
// H10Propagation.cs
//
// T186 audit (H10): H10 creates or changes Dataverse and Entra objects and re-reads them seconds later. Both platforms
// are eventually consistent — Graph can list an appRoleAssignment late, Dataverse copies roles into a new business unit
// asynchronously, and either can answer 429 / 5xx / time out under load. The reference for the pattern is H3's
// GraphAppRegistrationProvisioner.RetryWhileReplicatingAsync (bounded retries keyed on a narrow predicate, delays from
// options) and SecretClientKvWriter.ExecuteEntryWithRbacRetryAsync.
//
// This file holds the two shared pieces H10's raw-HTTP collaborators need:
//   - H10HttpException: the status-carrying form of the "METHOD uri failed: 503 ..." InvalidOperationException they
//     already threw (same type hierarchy and message, so existing catch blocks and assertions are unchanged);
//   - IsTransient / DelayAsync: the narrow "try again" predicate and the growing, option-driven delay.
// Only transient faults and not-visible-yet answers are waited out; a 401 / 403 / 400 is a real answer and is never retried.
// -----------------------------------------------------------------------------

using System.Net;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>A non-success response from Microsoft Graph or the Dataverse Web API, with its status code.</summary>
internal sealed class H10HttpException : InvalidOperationException
{
    public H10HttpException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

internal static class H10Propagation
{
    /// <summary>
    /// True for a fault that says nothing about the request itself and is worth waiting out: HTTP 429, any 5xx, a
    /// network-level <see cref="HttpRequestException"/>, or a timeout the CALLER did not ask for (the HttpClient timeout
    /// surfaces as a <see cref="TaskCanceledException"/> while <paramref name="callerToken"/> is still live). Anything else —
    /// 400, 401, 403, an unreadable body — is a real answer and not transient.
    /// </summary>
    public static bool IsTransient(Exception ex, CancellationToken callerToken) => ex switch
    {
        H10HttpException http => IsTransientStatus(http.StatusCode),
        HttpRequestException => true,
        OperationCanceledException => !callerToken.IsCancellationRequested,
        _ => false,
    };

    /// <summary>429 or any 5xx.</summary>
    public static bool IsTransientStatus(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>Waits <paramref name="baseDelay"/> × <paramref name="attempt"/> (a growing delay; zero skips the wait).</summary>
    public static Task DelayAsync(TimeSpan baseDelay, int attempt, CancellationToken ct)
        => baseDelay > TimeSpan.Zero ? Task.Delay(baseDelay * attempt, ct) : Task.CompletedTask;
}
