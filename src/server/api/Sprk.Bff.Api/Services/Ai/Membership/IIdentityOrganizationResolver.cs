// R3 Part 1 — User-Record Membership Resolution (organization mapping seam)
// Task 031 (2026-06-21): Coordination seam between task 031 (IdentityNormalizationService)
// and task 032 (organization-mapping-mechanism). Defined here so the
// normalization service compiles + ships independently of 032; 032 provides
// the implementation (mapping table, configurable lookup, junction entity, etc.
// per Q4 — design TBD in task 032).
//
// Multiple registrations are supported (services.AddSingleton<IIdentityOrganizationResolver, X>()
// + .AddSingleton<..., Y>()) — the normalization service injects
// IEnumerable<IIdentityOrganizationResolver> and merges their results. This
// keeps task 031 self-contained (works with ZERO registered resolvers — returns
// empty OrganizationIds[]) while letting task 032 plug in 1+ implementations
// without modifying this file.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md Q4 (sprk_organization
//            mapping mechanism); design.md Part 1 § Identity normalization contract
//            ("Lookup → sprk_organization | Configured user-organization mapping").

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Resolves the <c>sprk_organizationid</c> values associated with a given
/// systemuser / contact. Implementations are supplied by task 032
/// (organization-mapping-mechanism); the contract is defined here so
/// <see cref="IdentityNormalizationService"/> compiles + ships independently.
/// </summary>
/// <remarks>
/// <para>
/// Implementations MUST be registered as singletons via
/// <c>services.AddSingleton&lt;IIdentityOrganizationResolver, ConcreteResolver&gt;()</c>.
/// Multiple registrations are supported — <see cref="IdentityNormalizationService"/>
/// injects <see cref="IEnumerable{IIdentityOrganizationResolver}"/> and merges
/// the distinct results.
/// </para>
/// <para>
/// Failure isolation: an implementation that throws is logged + skipped; the
/// other resolvers' results are still merged. Implementations SHOULD therefore
/// internally guard against null / missing-data cases and return an empty list
/// rather than throwing in normal "no match" scenarios.
/// </para>
/// </remarks>
public interface IIdentityOrganizationResolver
{
    /// <summary>
    /// Returns the distinct <c>sprk_organizationid</c> values associated with
    /// the given user. Either parameter MAY be <c>null</c> when the upstream
    /// lookup has not yet resolved (e.g., the user has no contact row).
    /// Implementations MUST tolerate both being null and return an empty list.
    /// </summary>
    /// <param name="systemUserId">
    /// The Dataverse <c>systemuserid</c>. Always populated when invoked from
    /// <see cref="IdentityNormalizationService"/>.
    /// </param>
    /// <param name="contactId">
    /// The matching <c>contactid</c> if a contact exists for this user, or
    /// <c>null</c> if no contact was found by the upstream cross-reference.
    /// Some mapping mechanisms key off contact rather than systemuser.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A list of distinct <c>sprk_organizationid</c> Guids. Empty (NOT null)
    /// when no mapping exists.
    /// </returns>
    Task<IReadOnlyList<Guid>> ResolveOrganizationsAsync(
        Guid systemUserId,
        Guid? contactId,
        CancellationToken ct);
}
