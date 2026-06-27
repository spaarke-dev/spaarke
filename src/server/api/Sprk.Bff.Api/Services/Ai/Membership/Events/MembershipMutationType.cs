// R3 Part 1 Phase 2 — Membership change event contract
// Task 072 (2026-06-22): Enumerates the mutation classification for a
// `MembershipChangedEvent`. Carried in the event payload published to the
// Service Bus topic `sprk-membership-changes` (spec FR-2P2.3) and consumed
// by downstream subscriptions (e.g., `recon-junction-updater` per
// FR-2P2.4) to apply the correct write semantics:
//
//   Added    — a Lookup field that previously did NOT include this person
//              now does (new row to upsert in `sprk_userentityassociation`).
//   Removed  — a Lookup field that previously included this person no longer
//              does (junction row to delete).
//   Updated  — a non-add/non-remove mutation that still requires the
//              consumer to re-evaluate state for this {personId,
//              entityRecordId, sourceField} tuple (e.g., role-name override
//              changed; identity reclassification). The handler is keyed
//              idempotently per FR-2P2.4 so an Updated event safely re-runs
//              the upsert.
//
// Explicit numeric values are pinned so the underlying integer is stable in
// any future binary-serialized context (Application Insights custom
// dimensions, on-the-wire byte capture, etc.). The default JSON
// serialization, however, uses enum-as-string (see `MembershipChangedEvent`)
// for human readability and version-stability across schemaVersion bumps.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.2;
//            projects/spaarke-platform-foundations-r3/design.md Part 1 Phase 2.

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// Classification of a membership-affecting mutation as carried in
/// <see cref="MembershipChangedEvent"/>. Serialized as a string by the
/// canonical event JsonSerializerOptions for stability across
/// <c>schemaVersion</c> revisions.
/// </summary>
public enum MembershipMutationType
{
    /// <summary>
    /// A new association — the configured Lookup field now points at the
    /// person where it previously did not. Consumers upsert a junction row.
    /// </summary>
    Added = 1,

    /// <summary>
    /// The association was removed — the configured Lookup field no longer
    /// points at the person where it previously did. Consumers delete the
    /// corresponding junction row.
    /// </summary>
    Removed = 2,

    /// <summary>
    /// The association still exists but a non-add/non-remove attribute
    /// changed that affects the junction-row representation (e.g., role
    /// override). Consumers re-upsert (handler is idempotent per FR-2P2.4).
    /// </summary>
    Updated = 3,
}
