namespace Spaarke.Dataverse;

/// <summary>Which <c>principalobjectaccess</c> (POA) share write <see cref="DataverseWebApiService"/> made.</summary>
public enum RecordShareWrite
{
    /// <summary>A share created (<see cref="DataverseWebApiService.GrantAccessAsync"/>).</summary>
    Grant,

    /// <summary>A share's rights replaced (<see cref="DataverseWebApiService.ModifyAccessAsync"/>).</summary>
    Modify,

    /// <summary>A share removed (<see cref="DataverseWebApiService.RevokeAccessAsync"/>).</summary>
    Revoke,
}

/// <summary>
/// Told about every POA share write <see cref="DataverseWebApiService"/> makes, after the write — so a host that caches
/// who can read a record can drop what the write made stale.
/// </summary>
/// <remarks>
/// <para><b>Why it exists (unified-access-control-r2 task 132, main-session round 55).</b> A grant, a rights change or a
/// revoke changes who can read the record, exactly as an owner change does, so the BFF's access caches must be evicted
/// after it. That eviction used to live in a caller-side seam, and every guard that tried to force all callers through
/// the seam could be routed around by one more mechanism (an <c>[UnsafeAccessor]</c>, reflection, a late binder, a
/// method resolved from a metadata token and run by a LINQ provider). Making the notification part of the write itself
/// ends that: a share write that goes through <see cref="DataverseWebApiService"/> notifies, whoever called it and
/// however.</para>
///
/// <para><b>The contract the client keeps</b>: it calls <see cref="OnRecordShareWrittenAsync"/> once per share write,
/// after the write — whether the write returned, threw or was cancelled (a write that reports failure can have
/// committed) — with the entity set and record the write addressed, on <see cref="CancellationToken.None"/> (a caller
/// that went away must not leave the clean-up undone). An observer that throws is logged by the client and never
/// changes the write's own outcome.</para>
///
/// <para><b>Hosts.</b> This library cannot reference the BFF, so the hook points outward (dependency inversion). The BFF
/// registers its access-cache invalidator as the observer, unconditionally — the real one where Redis is the cache, the
/// Null peer (which evicts nothing and says so once at startup) with the in-memory cache (ADR-032, symmetric). The client
/// takes the observer as a required constructor argument, so no host gets a client that silently tells nobody: a host
/// with no access cache passes an observer that does nothing, on purpose.</para>
/// </remarks>
public interface IRecordShareWriteObserver
{
    /// <summary>
    /// Called once after a POA share write on the record <paramref name="entitySetName"/>(<paramref name="recordId"/>),
    /// whatever the write's outcome.
    /// </summary>
    /// <param name="entitySetName">The record's entity SET name, as the write addressed it (<c>sprk_projects</c>).</param>
    /// <param name="recordId">The record the write addressed.</param>
    /// <param name="write">Which share write it was.</param>
    /// <param name="ct">Always <see cref="CancellationToken.None"/> from the client; see the remarks.</param>
    Task OnRecordShareWrittenAsync(string entitySetName, Guid recordId, RecordShareWrite write, CancellationToken ct);
}
