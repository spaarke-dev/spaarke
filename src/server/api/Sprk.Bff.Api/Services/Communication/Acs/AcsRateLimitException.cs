namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// Signals that an ACS thread/membership mutation was throttled (HTTP 429) and is safe to RETRY after a
/// back-off (design §8.4 — ACS enforces 10 mutations/10s + 30/min per thread, 3000/min per resource).
/// The membership-reconcile job (task 041) catches this to distinguish a transient rate-limit (back off +
/// retry the remaining batches) from a permanent failure. Kept inside the Communication boundary; it is a
/// Spaarke exception, NOT an <c>Azure.Communication.*</c> type (ADR-045).
/// </summary>
public sealed class AcsRateLimitException : Exception
{
    /// <summary>Suggested wait before retrying, when ACS supplied a <c>Retry-After</c>; otherwise null.</summary>
    public TimeSpan? RetryAfter { get; }

    public AcsRateLimitException(string message, TimeSpan? retryAfter, Exception? innerException)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }
}
