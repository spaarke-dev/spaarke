// unified-access-control-r2 task 152 (ADR-034 Amendment A3), verifier round 1 item 4 — the COMPLETE people-targeted
// set of one entity, or an explicit "incomplete" answer. Never a silently shrunk set.
//
// The defect this closes: consumers called ResolveAsync(..., MembershipResolveOptions.People) at the default 500-row
// page and ignored response.ContinuationToken. The resolver pages ordered by primary id, so a person with more than
// 500 people-targeted rows of an entity (human Created By covers every to-do and document they ever made) got an
// arbitrary GUID-ordered subset — and every channel built on it shrank with nothing saying so. AccessibleRecordSetService
// fixed the same class of defect for the authorization plane (task 015, "WalkMembershipPagesAsync").

using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Reads the people-targeting surface (<see cref="MembershipResolveOptions.People"/>) for one entity to completion,
/// up to <see cref="MembershipResolveOptions.MaxLimit"/> ids, and says whether the answer is complete.
/// </summary>
/// <remarks>
/// <para>
/// One page of <see cref="MembershipResolveOptions.MaxLimit"/> ids (the resolver's hard ceiling, which is also
/// Dataverse's FetchXML page maximum). When the resolver returns a continuation token, ONE bounded confirmation read
/// decides between "complete at the ceiling" (the resolver emits a token whenever a page comes back full, so a set of
/// exactly <see cref="MembershipResolveOptions.MaxLimit"/> rows also carries one) and "genuinely larger than the
/// ceiling". A larger set is reported <see cref="Result.Complete"/> = <see langword="false"/>; consumers treat it as
/// a FAILED read (ADR-003 fail closed: "could not be loaded", never a quietly truncated list).
/// </para>
/// <para>
/// Every read carries identical options apart from the continuation token, so both pages share the resolver's
/// options-hash cache key family (the same rule <c>AccessibleRecordSetService.WalkMembershipPagesAsync</c> follows).
/// Resolver exceptions propagate unchanged — the consumer decides how a failure is reported.
/// </para>
/// </remarks>
internal static class PeopleTargetedSet
{
    /// <summary>
    /// The options every people-targeted candidate read uses: the people surface at the resolver's hard ceiling.
    /// </summary>
    internal static MembershipResolveOptions CandidateOptions { get; } =
        MembershipResolveOptions.People with { Limit = MembershipResolveOptions.MaxLimit };

    /// <summary>The people-targeted ids, and whether they are the whole set.</summary>
    /// <param name="Ids">Every id read (never more than <see cref="MembershipResolveOptions.MaxLimit"/>).</param>
    /// <param name="Complete"><see langword="false"/> when more rows exist than the ceiling allows.</param>
    internal sealed record Result(IReadOnlyList<Guid> Ids, bool Complete);

    /// <summary>
    /// Resolves the records of <paramref name="entityType"/> FOR <paramref name="systemUserId"/> through the people
    /// surface, to completion or to the ceiling.
    /// </summary>
    public static async Task<Result> ResolveAsync(
        IMembershipResolverService resolver,
        Guid systemUserId,
        string entityType,
        ILogger logger,
        CancellationToken ct)
    {
        var first = await resolver
            .ResolveAsync(systemUserId, entityType, CandidateOptions, ct)
            .ConfigureAwait(false);

        if (first.ContinuationToken is null)
        {
            return new Result(first.Ids, Complete: true);
        }

        // The resolver emits a token whenever a page comes back full; ask once whether anything is actually left.
        var confirmation = await resolver
            .ResolveAsync(systemUserId, entityType, CandidateOptions with { ContinuationToken = first.ContinuationToken }, ct)
            .ConfigureAwait(false);

        var known = new HashSet<Guid>(first.Ids);
        if (confirmation.ContinuationToken is null && confirmation.Ids.All(known.Contains))
        {
            return new Result(first.Ids, Complete: true);
        }

        logger.LogWarning(
            "people_targeting_set_incomplete: entity={EntityType} systemUserId={SystemUserId} ceiling={Ceiling} — more "
            + "records are FOR this person than one read can carry; the consumer reports the read as FAILED rather than "
            + "showing an arbitrary subset",
            entityType, systemUserId, MembershipResolveOptions.MaxLimit);
        return new Result(first.Ids, Complete: false);
    }
}
