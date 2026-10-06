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
    /// The logical names of every entity carrying the <c>sprk_issecure</c> attribute, lower-cased — except an entity
    /// whose flag the owner ruled is not a security input (<c>sprk_invoice</c>: an invoice follows its matter; task 150,
    /// <see cref="SecurableEntityRegistry.FlagIsNotASecurityInput"/>), which classifies as
    /// <see cref="EntitySecurability.NotSecurable"/>.
    /// </summary>
    /// <exception cref="Exception">
    /// Propagates any metadata-retrieval failure. Callers MUST NOT catch-and-default to "not securable".
    /// </exception>
    Task<IReadOnlySet<string>> GetSecurableEntitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Classify <paramref name="entityLogicalName"/> in ONE catalog lookup: not an entity at all, a real
    /// entity that cannot be secure, or a real entity carrying <c>sprk_issecure</c>. Case-insensitive and
    /// trimmed. An entity SET name (<c>sprk_projects</c>), a friendly alias (<c>project</c>) or a misspelling
    /// is NOT a logical name and answers <see cref="EntitySecurability.NotAnEntity"/>.
    /// </summary>
    /// <remarks>
    /// <para>unified-access-control-r2 task 151 (#1038). The three-way answer is what lets a caller tell "a
    /// real entity that cannot be secure" apart from "not an entity at all". A yes/no securability question
    /// cannot — both read as "no" — and treating the second as the first is how an Office save to a secure
    /// project, named by its friendly alias, landed in a shared container.</para>
    ///
    /// <para><b>One question, deliberately.</b> This replaced an <c>IsSecurableAsync</c> + <c>IsKnownEntityAsync</c>
    /// pair that each fetched the catalog, so a non-securable name cost TWO full-org metadata round trips on a
    /// cache miss. Implementations MUST answer from a single catalog lookup.</para>
    /// </remarks>
    /// <exception cref="Exception">
    /// Propagates metadata-retrieval failures, and THROWS when metadata reports no entities at all (an answer
    /// indistinguishable from a failed query) — never answers <see cref="EntitySecurability.NotAnEntity"/> or
    /// <see cref="EntitySecurability.NotSecurable"/> for a question it could not determine. See the interface
    /// remarks.
    /// </exception>
    Task<EntitySecurability> ClassifyEntityAsync(string entityLogicalName, CancellationToken ct = default);
}
