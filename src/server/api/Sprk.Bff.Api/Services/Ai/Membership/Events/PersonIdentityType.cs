// R3 Part 1 Phase 2 — Membership change event contract
// Task 072 (2026-06-22): Enumerates the Dataverse identity-type of the
// `personId` carried in a `MembershipChangedEvent`. Mirrors — but
// intentionally does NOT reuse — the string labels used by the sibling
// discovery pipeline (`MembershipDescriptor.IdentityType`,
// `MembershipOptions.IdentityTableConfig.IdentityType`). Rationale:
//
//   1. The discovery surface uses string labels because the configured set
//      is open-ended (operators may add identity tables via
//      MembershipOptions). The EVENT surface is a stable, versioned wire
//      contract — a closed enum gives downstream subscribers compile-time
//      safety + JSON schema stability across schemaVersion bumps.
//   2. Per spec FR-2P2.2 + Q4 owner clarification (2026-06-20), the event's
//      identity-type set is closed at the four targets that membership
//      mutations actually emit against: User (systemuser), Contact (contact),
//      Team (team), Organization (sprk_organization). BusinessUnit + Account
//      do NOT appear in mutation events — they are derived in the resolver
//      pipeline, not mutated directly on records.
//   3. The closed-enum-vs-open-string asymmetry is the same boundary
//      pattern the codebase uses for `MembershipMutationType` (sibling
//      file): event payloads use closed enums; service-internal
//      configuration uses open strings.
//
// Explicit numeric values are pinned for the same reason as
// `MembershipMutationType` — stability under any future binary capture.
// Default JSON serialization uses the string form (see
// `MembershipChangedEvent.SerializerOptions`).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.2;
//            projects/spaarke-platform-foundations-r3/spec.md Owner Clarification Q4
//            (2026-06-20); src/server/api/Sprk.Bff.Api/Services/Ai/Membership/
//            Models/MembershipDescriptor.cs (sibling open-string surface).

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// The Dataverse identity-type label for the <c>personId</c> carried in
/// <see cref="MembershipChangedEvent"/>. Closed enum (NOT extensible by
/// configuration) — distinct from the open-string label used by the
/// service-internal discovery pipeline. Serialized as a string by the
/// canonical event JsonSerializerOptions.
/// </summary>
public enum PersonIdentityType
{
    /// <summary>
    /// A Dataverse <c>systemuser</c> record.
    /// </summary>
    User = 1,

    /// <summary>
    /// A Dataverse <c>contact</c> record.
    /// </summary>
    Contact = 2,

    /// <summary>
    /// A Dataverse <c>team</c> record.
    /// </summary>
    Team = 3,

    /// <summary>
    /// A Dataverse <c>sprk_organization</c> record (per Q4 owner
    /// clarification 2026-06-20: lawfirm Lookups target <c>sprk_organization</c>,
    /// NOT <c>contact</c>).
    /// </summary>
    Organization = 4,
}
