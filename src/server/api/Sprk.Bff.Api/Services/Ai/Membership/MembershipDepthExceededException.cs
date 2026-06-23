// R3 Part 1D — User-Record Membership Resolution (transitive depth enforcement)
// Task 054 (2026-06-21): Sentinel exception thrown by the membership pipeline
// when an `includeRelated` request would exceed the 1-hop max enforced by
// spec.md FR-1D.2 (per owner clarification Q3, 2026-06-20).
//
// The membership endpoint (MembershipEndpoints.cs) catches this exception and
// converts it to a 400 BadRequest ProblemDetails response so callers get a
// structured rejection rather than a 500 InternalServerError.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1D.2;
//            owner clarification table (Q3) 2026-06-20.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Thrown by <see cref="IMembershipResolverService"/> when an
/// <c>includeRelated</c> request would resolve to more than one hop from the
/// primary entity (e.g., the operator-requested entity is not a direct Lookup
/// target of the primary entity, OR the requested value contains explicit
/// chain syntax such as <c>documents.events</c>). Per spec.md FR-1D.2 the
/// max chain depth is 1 hop; deeper requests are surfaced as 400 BadRequest
/// at the endpoint layer.
/// </summary>
/// <remarks>
/// The <see cref="OffendingEntry"/> property captures the specific token from
/// the comma-separated <c>includeRelated</c> query parameter that triggered
/// the rejection — included in the endpoint's ProblemDetails response so
/// callers can correct their request without having to enumerate which entry
/// was invalid.
/// </remarks>
public sealed class MembershipDepthExceededException : Exception
{
    /// <summary>
    /// The specific <c>includeRelated</c> entry that triggered the rejection
    /// (e.g., <c>"documents.events"</c> or <c>"sprk_unrelated"</c>). Always
    /// non-null and non-empty; the constructor trims+normalizes the input.
    /// </summary>
    public string OffendingEntry { get; }

    /// <summary>
    /// Optional short reason tag for telemetry/log correlation. One of:
    /// <c>"explicit-chain-syntax"</c> (dot syntax in the entry),
    /// <c>"not-a-direct-lookup-target"</c> (no 1-hop Lookup from the related
    /// entity back to the primary entity), or <c>"unknown-entity"</c>
    /// (the related entity's metadata could not be resolved).
    /// </summary>
    public string ReasonTag { get; }

    public MembershipDepthExceededException(string offendingEntry, string reasonTag, string message)
        : base(message)
    {
        OffendingEntry = offendingEntry ?? string.Empty;
        ReasonTag = reasonTag ?? "unknown";
    }

    public MembershipDepthExceededException(
        string offendingEntry,
        string reasonTag,
        string message,
        Exception inner)
        : base(message, inner)
    {
        OffendingEntry = offendingEntry ?? string.Empty;
        ReasonTag = reasonTag ?? "unknown";
    }
}
