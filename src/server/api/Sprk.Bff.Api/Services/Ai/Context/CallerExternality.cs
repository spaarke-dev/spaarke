using System.Security.Claims;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// The ONE answer to "is this request's caller external?" (procedure-and-file-model section 5.3, "step 0"): the caller's
/// systemuser from the token's <c>oid</c> (<see cref="ICallerSystemUserResolver"/>; no active row → external), then
/// <see cref="ISystemUserIdentityResolver.IsExternalAsync"/> (<c>sprk_isexternal</c> true → external; false or blank →
/// internal; a missing row → external), and a fault anywhere THROWS — a transient fault never decides who the caller is.
/// </summary>
/// <remarks>
/// Moved here unchanged in behaviour from <c>ExternalCallerCommunicationGuard.IsExternalCallerAsync</c> (ebilling-r1 task 099)
/// so the Finance authorization filter can reuse it: non-AI code may not depend on <c>Services.Ai.Handlers.Dataverse</c>
/// (ADR013_AiBoundaryTests, D-66), while <see cref="ICallerSystemUserResolver"/> beside it is already consumed by non-AI
/// filters. The assistant's guard delegates here, so there is still one resolution.
/// </remarks>
public static class CallerExternality
{
    /// <summary>
    /// <see cref="CallerSystemUserResolution.UnresolvedReason"/> for a lookup fault the resolver swallowed (the value
    /// <see cref="CallerSystemUserResolver"/> returns).
    /// </summary>
    public const string LookupFailedReason = "lookup-failed";

    /// <summary>
    /// True when <paramref name="principal"/> is external: flagged <c>sprk_isexternal</c>, or not resolvable to an active
    /// systemuser (no principal, no oid, no matching active row). A lookup that FAILED (<see cref="LookupFailedReason"/>: a
    /// Dataverse or cache fault the resolver swallowed) THROWS, as does a failing externality read.
    /// </summary>
    public static async Task<bool> IsExternalCallerAsync(
        ClaimsPrincipal? principal,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        CancellationToken ct) =>
        (await ClassifyCallerAsync(principal, callerResolver, identityResolver, ct).ConfigureAwait(false)).IsExternal;

    /// <summary>
    /// <see cref="IsExternalCallerAsync"/> with the caller's <c>systemuserid</c> as well, so a caller that needs both (the
    /// Finance file download's audit event, ebilling-r1 task 090) asks once. Same rules, same throws: this IS the definition,
    /// and <see cref="IsExternalCallerAsync"/> is this method's verdict.
    /// </summary>
    /// <returns>
    /// The verdict; <see cref="CallerExternalityVerdict.SystemUserId"/> is the resolved row when one resolved (also for a
    /// flagged-external caller), <c>null</c> when none did.
    /// </returns>
    public static async Task<CallerExternalityVerdict> ClassifyCallerAsync(
        ClaimsPrincipal? principal,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        CancellationToken ct)
    {
        var resolution = await callerResolver.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!resolution.IsResolved && string.Equals(resolution.UnresolvedReason, LookupFailedReason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The caller's systemuser lookup failed; externality cannot be decided (fail closed: no data).");
        }

        if (!resolution.IsResolved || !Guid.TryParse(resolution.SystemUserId, out var systemUserId))
        {
            return new CallerExternalityVerdict(IsExternal: true, SystemUserId: null);
        }

        var external = await identityResolver.IsExternalAsync(systemUserId, ct).ConfigureAwait(false);
        return new CallerExternalityVerdict(external, systemUserId);
    }
}

/// <summary>The answer of <see cref="CallerExternality.ClassifyCallerAsync"/>.</summary>
/// <param name="IsExternal">True when the caller is external (flagged, or no active systemuser row).</param>
/// <param name="SystemUserId">The caller's resolved <c>systemuserid</c>, or <c>null</c> when none resolved.</param>
public sealed record CallerExternalityVerdict(bool IsExternal, Guid? SystemUserId);
