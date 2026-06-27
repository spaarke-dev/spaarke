// R3 Part 1 — User-Record Membership Resolution (discovery descriptor)
// Task 030 (2026-06-21): Runtime descriptor emitted by
// MembershipFieldDiscoveryService for each Lookup field on an entity that
// targets a configured identity table. Carries everything downstream services
// (MembershipResolverService — task 033, MembershipResponse DTO — task 034,
// LookupUserMembershipNodeExecutor — task 041) need to issue per-role queries
// against the entity:
//
//   Field         — Dataverse logical lookup attribute name (e.g., "ownerid",
//                   "sprk_assignedattorney1"). Used in FetchXML criteria.
//   Role          — public-facing camelCase role name (e.g., "owner",
//                   "assignedAttorney"). Returned in MembershipResponse.byRole.
//   IdentityType  — the logical identity-type label (SystemUser, Contact, Team,
//                   BusinessUnit, Account, Organization). Drives client-side
//                   role-binding logic + cache key partitioning.
//   TargetTable   — the Dataverse logical table the lookup points at (e.g.,
//                   "contact", "sprk_organization"). MUST be one of the
//                   configured identity tables.
//   Source        — "auto" when the field was discovered via metadata scan,
//                   "override" when the role-name or include came from
//                   per-entity overrides in MembershipOptions.EntityOverrides.
//
// Q4 sharpening (per spec.md owner clarification 2026-06-20): the
// sprk_assignedlawfirm1/2 lookups target sprk_organization (NOT contact, as
// the design.md discovery-report example had originally suggested). The
// IdentityType for those fields must therefore be "Organization", derived by
// looking up sprk_organization in IncludedIdentityTables.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.1, FR-1A.4;
//            projects/spaarke-platform-foundations-r3/design.md Part 1 §
//            "Discovery algorithm" + "Discovery Report endpoint".

namespace Sprk.Bff.Api.Services.Ai.Membership.Models;

/// <summary>
/// Runtime descriptor for a single membership-bearing Lookup field on an
/// entity. Emitted by <c>MembershipFieldDiscoveryService</c> (task 030) and
/// consumed by the membership resolver pipeline (task 033 onward).
/// </summary>
/// <param name="Field">
/// Dataverse logical attribute name of the Lookup column (e.g.,
/// <c>ownerid</c>, <c>sprk_assignedattorney1</c>). Always lowercase per
/// Dataverse convention.
/// </param>
/// <param name="Role">
/// Public-facing role name. Derived from <paramref name="Field"/> via the
/// configured role-name strategy (currently <c>CamelCase</c>: strip
/// <c>sprk_</c> prefix + strip trailing digits + camelCase) unless a
/// per-entity <c>FieldRoleOverrides</c> entry supersedes the auto-derived
/// value. Returned in <c>MembershipResponse.byRole</c> (task 034).
/// </param>
/// <param name="IdentityType">
/// Logical identity-type label for the lookup target (one of
/// <c>SystemUser</c>, <c>Contact</c>, <c>Team</c>, <c>BusinessUnit</c>,
/// <c>Account</c>, <c>Organization</c>). Sourced from
/// <c>MembershipOptions.IncludedIdentityTables</c> by matching
/// <paramref name="TargetTable"/>.
/// </param>
/// <param name="TargetTable">
/// Dataverse logical table the lookup points at (e.g., <c>contact</c>,
/// <c>sprk_organization</c>). Guaranteed to be present in
/// <c>MembershipOptions.IncludedIdentityTables</c> — fields whose targets are
/// not in that list are emitted as <c>IgnoredField</c> instead of descriptors.
/// </param>
/// <param name="Source">
/// Provenance of this descriptor. <c>"auto"</c> when discovered via metadata
/// scan and not modified by overrides. <c>"override"</c> when either the
/// role-name came from <c>FieldRoleOverrides</c> OR the field was force-included
/// via <c>IncludedFields</c> (i.e., it would have been globally excluded but
/// the per-entity override resurrected it).
/// </param>
public sealed record MembershipDescriptor(
    string Field,
    string Role,
    string IdentityType,
    string TargetTable,
    string Source);
