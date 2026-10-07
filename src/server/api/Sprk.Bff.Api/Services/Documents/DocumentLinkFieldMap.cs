using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// Compose-side POLICY over the <c>sprk_document</c> link vocabulary. It does NOT declare that
/// vocabulary — <see cref="DocumentLinkFields"/> in <c>Spaarke.Dataverse</c> does, and is the single
/// home for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>History, because the shape of this file is the point.</b> On 2026-09-04 this class WAS a second
/// enumeration of the columns, created here (BFF-local) to end two drifted copies. On 2026-09-05
/// <c>unified-access-control-r2</c> independently hoisted the same closed set into
/// <c>Spaarke.Dataverse.DocumentLinkFields</c> — a strictly better home: a shared library both the BFF
/// and client code can consume, carrying the case-SENSITIVE <c>SchemaName</c> this file never had. Two
/// projects solved one problem a day apart and neither could see the other.
/// </para>
/// <para>
/// Resolved at the 2026-09-29 master merge in the only way root <c>CLAUDE.md</c> §11 permits: ONE
/// vocabulary, in the shared library. What is left here is the part that has no equivalent there and is
/// genuinely Compose's own — the legacy-column knowledge and the copy-forward semantics. Everything
/// column-shaped is DERIVED from <see cref="DocumentLinkFields.All"/>, so this file can no longer drift
/// from it: there is nothing here to drift.
/// </para>
/// <para>
/// The merge also carried a correction the other direction: the hoist enumerated sixteen columns and the
/// table has seventeen (<c>sprk_email</c> was absent). That column is now in the shared list.
/// </para>
/// </remarks>
public static class DocumentLinkFieldMap
{
    /// <summary>
    /// The four unprefixed lookups superseded by a <c>sprk_related*</c> counterpart, mapped to it.
    /// </summary>
    /// <remarks>
    /// This is RECORDED, never ACTED ON during a copy — see <see cref="ProjectForCopy"/> for why
    /// redirecting a legacy column on write is a defect rather than a migration. It exists so that a
    /// deliberate, one-time data migration has its mapping in a verified place; it is the only
    /// superseded/current distinction anywhere in the codebase, which is why it survives the hoist.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> SupersededBy =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_relatedmatter",
            ["sprk_project"] = "sprk_relatedproject",
            ["sprk_invoice"] = "sprk_relatedinvoice",
            ["sprk_workassignment"] = "sprk_relatedworkassignment",
        };

    /// <summary>True when <paramref name="logicalName"/> is a legacy lookup with a <c>related</c> successor.</summary>
    public static bool IsLegacy(string logicalName) => SupersededBy.ContainsKey(logicalName);

    /// <summary>
    /// Projects every link the source record carries into the attributes to write on the COPY, keyed by
    /// the SAME column the value was read from. <paramref name="readLink"/> returns the value for one
    /// column, or <c>null</c> when the source has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Column-for-column, deliberately — including the legacy unprefixed lookups.</b> An earlier cut
    /// of this method redirected <c>sprk_matter</c> → <c>sprk_relatedmatter</c> on write, to migrate rows
    /// as they were touched. Two existing tests caught it and they were right: <b>a Dataverse subgrid
    /// binds to ONE relationship.</b> If the Matter form's Documents subgrid is bound to
    /// <c>sprk_matter</c> and the source PDF sits there, writing the copy to <c>sprk_relatedmatter</c>
    /// means the two do NOT appear together — silently defeating "files alongside the source", which is
    /// the entire point of the feature.
    /// </para>
    /// <para>
    /// Migrating legacy columns is a deliberate one-time data operation, never a side effect of saving a
    /// document. <see cref="SupersededBy"/> records the mapping for whenever that is done.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, T> ProjectForCopy<T>(Func<DocumentLinkField, T?> readLink)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(readLink);

        var projected = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in DocumentLinkFields.All)
        {
            var value = readLink(field);
            if (value is not null)
            {
                projected[field.LogicalName] = value;
            }
        }

        return projected;
    }
}
