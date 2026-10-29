// -----------------------------------------------------------------------------
// H11ReplicationRetry.cs
//
// T186 audit (H11). Entra and Dataverse answer a request about an object created or changed moments ago with a
// transient "not there yet" — a just-invited guest or just-created user 404s (or 400 "does not reference a valid
// object") until the directory replicates, and Dataverse 404s the alternate-key read of a user whose group membership
// has not propagated. H11's raw-HTTP seams (GraphRestUserProvisioner, GraphRestEnvironmentSecurityGroupClient,
// DataverseWebApiGuestUserWriter) wait that out with this bounded retry instead of failing the run. Same idea as H3's
// GraphAppRegistrationProvisioner.RetryWhileReplicatingAsync, on HTTP status + body (these seams use HttpClient, not the
// Graph SDK); deliberately not coupled to the EntraAppReg class.
//
// Only the narrow predicates below are retried: any other reply (403, a genuine licence error, ...) is returned at once,
// and the last reply is returned when the attempts run out — so the caller's existing failure handling is unchanged.
// -----------------------------------------------------------------------------

using System.Net;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>Bounded retry for H11's raw-HTTP calls about objects that may not have replicated yet.</summary>
internal static class H11ReplicationRetry
{
    /// <summary>One HTTP reply: status and body text.</summary>
    internal readonly record struct Reply(HttpStatusCode Status, string Body)
    {
        public bool IsSuccess => (int)Status is >= 200 and < 300;

        public bool BodyContains(string text) => Body.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Graph's answer for a directory object it has not replicated yet: 404 (Request_ResourceNotFound), or 400 naming
    /// an object that is not a valid reference / does not exist. "One or more added object references already exist"
    /// is a different, non-matching message.
    /// </summary>
    internal static bool IsGraphObjectNotYetReplicated(Reply reply) =>
        reply.Status == HttpStatusCode.NotFound
        || (reply.Status == HttpStatusCode.BadRequest
            && (reply.BodyContains("does not reference a valid")
                || reply.BodyContains("does not exist")
                || reply.BodyContains("Request_ResourceNotFound")));

    /// <summary>
    /// As <see cref="IsGraphObjectNotYetReplicated"/>, for <c>assignLicense</c> on a just-created user — which also
    /// fails with a 400 about the user's usage location while the replica serving the call has not seen the one set at
    /// creation. A genuine licence error (no available licences, an unknown SKU, an unassignable service plan) is
    /// never transient.
    /// </summary>
    internal static bool IsLicenseTargetNotYetReplicated(Reply reply)
    {
        if (reply.Status == HttpStatusCode.NotFound)
        {
            return true;
        }
        if (reply.Status != HttpStatusCode.BadRequest)
        {
            return false;
        }
        if (reply.BodyContains("usage location"))
        {
            return true;
        }
        if (reply.BodyContains("available licenses") || reply.BodyContains("sku") || reply.BodyContains("service plan")
            || reply.BodyContains("subscribed"))
        {
            return false;
        }
        return reply.BodyContains("does not reference a valid")
            || reply.BodyContains("does not exist")
            || reply.BodyContains("Request_ResourceNotFound");
    }

    /// <summary>
    /// Dataverse's answer to the alternate-key read of a guest whose security-group membership has not propagated: 404
    /// (it adds the user on this read only once it sees the membership).
    /// </summary>
    internal static bool IsDataverseUserNotYetVisible(Reply reply) => reply.Status == HttpStatusCode.NotFound;

    /// <summary>
    /// Sends via <paramref name="send"/>, repeating up to <paramref name="attempts"/> times (waiting
    /// <paramref name="delay"/> between) while the reply is <paramref name="isTransient"/>. Returns the first success,
    /// the first non-transient reply, or the last reply when the attempts run out. An exception propagates at once.
    /// </summary>
    internal static async Task<Reply> RunAsync(
        Func<CancellationToken, Task<Reply>> send,
        Func<Reply, bool> isTransient,
        int attempts,
        TimeSpan delay,
        ILogger? logger,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var reply = await send(cancellationToken).ConfigureAwait(false);
            if (reply.IsSuccess || attempt >= attempts || !isTransient(reply))
            {
                return reply;
            }

            logger?.LogInformation(
                "H11 {Operation} attempt {Attempt}/{Max} got {Status} — the new object has not replicated yet; retrying in {Delay}.",
                operation, attempt, attempts, (int)reply.Status, delay);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
