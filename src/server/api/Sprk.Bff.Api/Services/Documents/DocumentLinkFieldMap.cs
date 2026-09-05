namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// One record-link lookup on <c>sprk_document</c> — the "what business record is this document filed
/// under" vocabulary.
/// </summary>
/// <param name="Attribute">The column's logical name on <c>sprk_document</c>.</param>
/// <param name="TargetEntity">Logical name of the parent entity the lookup points at.</param>
/// <param name="SupersededBy">
/// When non-null, this column is LEGACY and <see cref="WriteAttribute"/> resolves to the modern column
/// that replaces it. Legacy columns are still READ (existing rows may only carry the old one) but are
/// never written.
/// </param>
public sealed record DocumentLinkField(string Attribute, string TargetEntity, string? SupersededBy = null)
{
    /// <summary>True when a newer <c>Related*</c> column supersedes this one for NEW associations.</summary>
    public bool IsLegacy => SupersededBy is not null;
}

/// <summary>
/// Single source of truth for the <c>sprk_document</c> record-link vocabulary (ADR-024 family).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> This vocabulary was previously declared TWICE — in
/// <c>AttachmentDocumentAssociationRung.DocumentLinkFields</c> and
/// <c>ComposeService.DocumentAssociationLookupAttributes</c> — and both copies had drifted far behind the
/// schema. Against a live <c>describe('tables/sprk_document')</c> (2026-09-04) the table carries
/// <b>17</b> link lookups; the two declarations knew <b>6</b>. Ten were invisible to both consumers,
/// including Agreement, Service Request, To Do, Event, Contact, Organization and Vendor Org.
/// </para>
/// <para>
/// That was a live defect, not untidiness: Compose create-on-save copies a source document's links onto
/// the new Word document so the two file together, and it copied only the six it knew. A PDF filed under
/// an Agreement produced a Word document that <b>silently lost that filing</b> — no error, and the user
/// simply finds the document is not where they filed the original. Every host in this codebase declares
/// its regarding/link family exactly once (<c>RegardingFieldMap</c> for <c>sprk_communication</c>,
/// <c>TaskActionCore.RegardingFieldByEntity</c> for <c>sprk_event</c>); Document is now consistent with
/// that, and adding a link means editing ONE list.
/// </para>
/// <para>
/// <b>Naming.</b> <c>sprk_document</c> uses the <c>Related*</c> convention (owner decision, 2026-09-04),
/// deliberately unlike Event/Communication which use <c>Regarding*</c>. That is normal here — the field
/// map is the contract, not a naming convention, which is why the two <c>Regarding*</c> hosts already
/// disagree with each other (<c>contact</c> → <c>sprk_regardingperson</c> on communication vs
/// <c>sprk_regardingcontact</c> on event). Do NOT "harmonise" these names.
/// </para>
/// <para>
/// <b>Legacy columns.</b> Four unprefixed lookups (<c>sprk_matter</c>, <c>sprk_project</c>,
/// <c>sprk_invoice</c>, <c>sprk_workassignment</c>) predate the convention and duplicate a
/// <c>Related*</c> sibling. They cannot be deleted (solution dependencies) but are superseded: read them
/// so existing rows keep their filing, and write only the modern column, so a legacy value migrates the
/// first time a document is touched.
/// </para>
/// <para>
/// <b>Two lookups can share a target.</b> <c>sprk_relatedorganization</c> and
/// <c>sprk_relatedvendororg</c> both point at <c>sprk_organization</c> in different ROLES. Never key this
/// vocabulary by target entity.
/// </para>
/// <para>
/// <b>Maintenance.</b> Verified against the live schema on 2026-09-04. When a link column is added to
/// <c>sprk_document</c>, add it here — the consumers pick it up automatically. Consumers that need a
/// SUBSET must state their exclusions explicitly with a reason (see
/// <see cref="AssociationCandidateFields"/>); an exclusion expressed as silent omission is
/// indistinguishable from the oversight this class exists to prevent.
/// </para>
/// </remarks>
public static class DocumentLinkFieldMap
{
    /// <summary>
    /// Every record-link lookup on <c>sprk_document</c>, verified against
    /// <c>describe('tables/sprk_document')</c> on 2026-09-04.
    /// </summary>
    public static readonly IReadOnlyList<DocumentLinkField> All =
    [
        // ── Current vocabulary (the Related* convention) ──────────────────────────────
        new("sprk_relatedagreement", "sprk_agreement"),
        new("sprk_relatedcommunication", "sprk_communication"),
        new("sprk_relatedcontact", "contact"),
        new("sprk_relatedevent", "sprk_event"),
        new("sprk_relatedinvoice", "sprk_invoice"),
        new("sprk_relatedmatter", "sprk_matter"),
        new("sprk_relatedorganization", "sprk_organization"),
        new("sprk_relatedproject", "sprk_project"),
        new("sprk_relatedservicerequest", "sprk_servicerequest"),
        new("sprk_relatedtodo", "sprk_todo"),
        // Same target as sprk_relatedorganization, different ROLE (vendor). Both are legitimate.
        new("sprk_relatedvendororg", "sprk_organization"),
        new("sprk_relatedworkassignment", "sprk_workassignment"),
        // The OOB activity entity, not a sprk_ table.
        new("sprk_email", "email"),

        // ── Legacy: read-only, superseded by the Related* sibling ─────────────────────
        new("sprk_matter", "sprk_matter", SupersededBy: "sprk_relatedmatter"),
        new("sprk_project", "sprk_project", SupersededBy: "sprk_relatedproject"),
        new("sprk_invoice", "sprk_invoice", SupersededBy: "sprk_relatedinvoice"),
        new("sprk_workassignment", "sprk_workassignment", SupersededBy: "sprk_relatedworkassignment"),
    ];

    /// <summary>Every link column name — the column set to retrieve when reading a document's links.</summary>
    public static readonly IReadOnlyList<string> AllAttributes =
        All.Select(f => f.Attribute).ToArray();

    /// <summary>The non-legacy vocabulary — the only columns anything should WRITE.</summary>
    public static readonly IReadOnlyList<DocumentLinkField> Current =
        All.Where(f => !f.IsLegacy).ToArray();

    /// <summary>
    /// The subset the email→document association engine scans for candidate parents
    /// (<c>AttachmentDocumentAssociationRung</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Exclusions, stated rather than omitted:</b></para>
    /// <list type="bullet">
    ///   <item><c>sprk_relatedcommunication</c> and <c>sprk_email</c> — the inbound communication IS what
    ///   the rung is matching FROM. Surfacing it back as a candidate association is circular.</item>
    /// </list>
    /// <para>
    /// Legacy columns ARE included: a document filed only under <c>sprk_matter</c> is still genuinely
    /// filed under that matter, and the rung reads rather than writes.
    /// </para>
    /// <para>
    /// ⚠️ This set was WIDENED on 2026-09-04 from the 6 the rung previously hard-coded. The extra targets
    /// (agreement, contact, event, organization, vendor org, service request, to-do, and the related
    /// invoice/work-assignment forms) were always valid document links; the rung simply could not see
    /// them. Matches remain suggest-band, surface-only candidates a reviewer confirms — never written as
    /// filed — so a wider scan changes what can be SUGGESTED, not what is committed.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<DocumentLinkField> AssociationCandidateFields =
        All.Where(f => f.Attribute is not ("sprk_relatedcommunication" or "sprk_email")).ToArray();

    /// <summary>
    /// Copies a source document's non-empty links onto the column set a NEW document is created with —
    /// <b>column for column</b>, never redirected.
    /// </summary>
    /// <param name="readLink">
    /// Reads one link off the source document; returns <c>null</c> when the column is empty.
    /// </param>
    /// <returns>Column name → the value to set, ready to apply to the new record.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why legacy columns are NOT redirected here, despite being superseded.</b> A first cut of this
    /// method rewrote <c>sprk_matter</c> → <c>sprk_relatedmatter</c> on the way out, to migrate rows
    /// forward as they were touched. That is wrong on this path, and two existing tests caught it.
    /// </para>
    /// <para>
    /// The purpose of the copy is that the new Word document files <b>alongside</b> its source. A
    /// Dataverse subgrid binds to <b>one</b> relationship — so if the Matter form's Documents subgrid is
    /// bound to <c>sprk_matter</c> and the source PDF sits there, writing the new document to
    /// <c>sprk_relatedmatter</c> means the two do <b>not</b> appear together. The redirect would have
    /// defeated the exact guarantee the feature exists to provide, silently.
    /// </para>
    /// <para>
    /// Migrating legacy columns is a deliberate, one-time data operation — not a side effect of saving a
    /// document. <see cref="Current"/> is the vocabulary for NEW associations; a copy is not a new
    /// association, it is a reproduction of where the source already lives.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, T> ProjectForCopy<T>(Func<DocumentLinkField, T?> readLink)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(readLink);

        var projected = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in All)
        {
            if (readLink(field) is { } value)
            {
                projected[field.Attribute] = value;
            }
        }

        return projected;
    }
}
