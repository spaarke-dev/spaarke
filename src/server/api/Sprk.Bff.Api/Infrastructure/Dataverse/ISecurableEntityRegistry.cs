namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// unified-access-control-r2 task 075 — which Dataverse entities can be marked secure, derived from LIVE
/// METADATA rather than a hard-coded list.
///
/// <para><b>Why not a constant.</b> The list happens to be <c>sprk_project</c>, <c>sprk_matter</c> and
/// <c>sprk_workassignment</c> today. A hard-coded list is wrong the moment a fourth entity gains
/// <c>sprk_issecure</c>, and the failure would be silent in the worst possible direction: the new securable
/// entity would resolve through the non-secure fallback and its content would land in a shared container,
/// which SPE's additive-only permission model makes irreversible. Deriving the list means a new securable
/// entity is picked up without a code change.</para>
///
/// <para><b>Fail-closed contract.</b> Implementations MUST throw rather than return an empty or partial set
/// when the answer cannot be determined. "I could not find out whether this entity is securable" read as
/// "it is not securable" is the same isolation failure as a wrong answer — see
/// <see cref="SecureContainerDecision"/>.</para>
/// </summary>
public interface ISecurableEntityRegistry
{
    /// <summary>
    /// The logical names of every entity carrying the <c>sprk_issecure</c> attribute, lower-cased.
    /// </summary>
    /// <exception cref="Exception">
    /// Propagates any metadata-retrieval failure. Callers MUST NOT catch-and-default to "not securable".
    /// </exception>
    Task<IReadOnlySet<string>> GetSecurableEntitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="entityLogicalName"/> can be marked secure. Case-insensitive.
    /// </summary>
    /// <exception cref="Exception">Propagates metadata-retrieval failures — see the interface remarks.</exception>
    Task<bool> IsSecurableAsync(string entityLogicalName, CancellationToken ct = default);
}
