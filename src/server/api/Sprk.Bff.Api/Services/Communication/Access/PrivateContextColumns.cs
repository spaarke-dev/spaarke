namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// fm task 056 — the non-filing context reference of a private message (design:
/// <c>projects/field-mapping-server-write-path-r1/notes/task-056-private-message-design.md</c>).
/// <para>
/// A private message is a Direct message (the pair's Direct thread, the Direct-team owner, the stamped pair
/// <see cref="DirectMessageRecipient.SenderColumn"/> + <see cref="DirectMessageRecipient.RecipientColumn"/>) that also
/// names ONE core record it is about. The reference decides where the message is SHOWN to its two participants; it never
/// decides who READS it. It is therefore never a filing lookup: it is in none of <c>RegardingFieldMap</c>,
/// <c>SecureChildLineage.Children</c>, <c>RecordFamilyEdges</c>, <c>InvoiceLinks</c>, the external scope registries or the
/// core-ancestor map, so it never feeds ownership, inheritance, grants or a record-scoped read. The BFF writes it once, at
/// create; the columns are field-secured with the other BFF-owned communication columns. Guarded by an ArchTest that
/// lists the only files allowed to name these columns.
/// </para>
/// </summary>
internal static class PrivateContextColumns
{
    /// <summary>The reference to a matter.</summary>
    internal const string Matter = "sprk_privatecontextmatter";

    /// <summary>The reference to a project.</summary>
    internal const string Project = "sprk_privatecontextproject";

    /// <summary>The reference to a work assignment.</summary>
    internal const string WorkAssignment = "sprk_privatecontextworkassignment";

    /// <summary>The referenced table → its reference column. Core records only (MR-1 rule 1).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ColumnByTable =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = Matter,
            ["sprk_project"] = Project,
            ["sprk_workassignment"] = WorkAssignment,
        };

    /// <summary>The referenced table → its Web API entity set name.</summary>
    internal static readonly IReadOnlyDictionary<string, string> EntitySetByTable =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_matters",
            ["sprk_project"] = "sprk_projects",
            ["sprk_workassignment"] = "sprk_workassignments",
        };

    /// <summary>All three reference columns.</summary>
    internal static readonly IReadOnlyList<string> All = new[] { Matter, Project, WorkAssignment };

    /// <summary>
    /// Parses an <c>about</c> query value, <c>{table}:{guid}</c> (for example <c>sprk_matter:7f…</c>). Returns false for
    /// anything else: an unknown table, a malformed or empty id. The caller answers a false with a 400.
    /// </summary>
    internal static bool TryParseAbout(string? value, out string table, out Guid id)
    {
        table = string.Empty;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
            return false;

        var candidateTable = value[..separator].Trim();
        if (!ColumnByTable.ContainsKey(candidateTable)
            || !Guid.TryParse(value[(separator + 1)..].Trim(), out var candidateId)
            || candidateId == Guid.Empty)
        {
            return false;
        }

        table = candidateTable.ToLowerInvariant();
        id = candidateId;
        return true;
    }
}
