namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Association target types.
/// </summary>
/// <remarks>
/// These are the valid entity types that documents can be associated with
/// in the Spaarke DMS. A document MUST be associated to exactly one of these.
/// </remarks>
public enum AssociationType
{
    // ⚠️ Values are EXPLICIT and must stay pinned. `Account` was removed 2026-09-04 (owner decision —
    // sprk_document has no account lookup in either column family, so such a save could only ever land
    // unassociated). Ordinal 3 is left BURNED rather than reused: these members were previously
    // implicit, so deleting Account from the middle would have silently renumbered Contact,
    // WorkAssignment and Event under any int-valued serialization. Do not reclaim 3, and do not
    // re-add Account — Spaarke's organization analogue is `sprk_organization`.

    /// <summary>
    /// Matter (sprk_matter).
    /// </summary>
    Matter = 0,

    /// <summary>
    /// Project (sprk_project).
    /// </summary>
    Project = 1,

    /// <summary>
    /// Invoice (sprk_invoice).
    /// </summary>
    Invoice = 2,

    // 3 = Account (removed 2026-09-04 — see the note above). Deliberately not reused.

    /// <summary>
    /// Contact (standard Dataverse contact). Files to <c>sprk_relatedcontact</c> — a real column, so
    /// unlike the removed <c>Account</c> this type became genuinely functional on 2026-09-04 rather
    /// than being dropped.
    /// </summary>
    Contact = 4,

    /// <summary>
    /// Work assignment (sprk_workassignment). Added 2026-09-03 with the Q4 widening — the save
    /// endpoint now accepts this association type, so the response shape has to be able to name it.
    /// </summary>
    WorkAssignment = 5,

    /// <summary>
    /// Event. Added 2026-09-03, same reason as <see cref="WorkAssignment"/>. Files to
    /// <c>sprk_relatedevent</c> — there is no <c>sprk_event</c> column on <c>sprk_document</c>.
    /// </summary>
    Event = 6,

    /// <summary>
    /// To-do (sprk_todo), via <c>sprk_relatedtodo</c>. Added 2026-09-04, correcting the record that
    /// held a document to be unmappable to a to-do — the column existed the whole time.
    /// </summary>
    Todo = 7
}
