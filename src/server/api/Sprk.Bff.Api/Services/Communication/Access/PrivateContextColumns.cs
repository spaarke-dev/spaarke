using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

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

    // ── A private message is never filed (owner, 2026-11-22; UAC-r2: fail closed wherever a filing is read) ─────────────────────

    /// <summary>The table the reference columns are on.</summary>
    private const string CommunicationTable = "sprk_communication";

    /// <summary>
    /// Whether <paramref name="row"/> (a <c>sprk_communication</c> read with the reference columns) is a PRIVATE message: any
    /// reference column holds a record. A private message holds no filing anywhere, so every reader of a filing that meets one
    /// treats it as filed under nothing. A <c>null</c> or an <see cref="EntityReference"/> to <see cref="Guid.Empty"/> is no
    /// reference; any other value cannot be read as "no reference", so it counts as one (fail closed). A column the row was not
    /// read with is no reference: the caller reads the columns that exist (<see cref="PresentColumnsOf"/>).
    /// </summary>
    internal static bool IsPrivate(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        foreach (var column in All)
        {
            if (!row.Attributes.TryGetValue(column, out var value) || value is null)
                continue;
            if (value is not EntityReference reference || reference.Id != Guid.Empty)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The reference columns a read of <paramref name="table"/> adds so <see cref="IsPrivate"/> can answer: on
    /// <c>sprk_communication</c>, those of <paramref name="hostColumns"/> (the table's columns in this environment, from the
    /// caller's own metadata read); none on any other table. A column absent from metadata holds no reference.
    /// </summary>
    internal static IEnumerable<string> PresentColumnsOf(string table, IReadOnlySet<string> hostColumns)
    {
        ArgumentNullException.ThrowIfNull(hostColumns);
        return string.Equals(table, CommunicationTable, StringComparison.OrdinalIgnoreCase)
            ? All.Where(hostColumns.Contains)
            : Enumerable.Empty<string>();
    }

    /// <summary>
    /// The OData <c>$filter</c> clause that keeps every private message out of a <c>sprk_communication</c> read:
    /// <c>_{column}_value eq null</c> for each reference column that exists on the table (published metadata, through the
    /// send gate's cached column-presence check, <see cref="PrivateMessageSendGate.CommunicationColumnsPresentAsync"/>), joined
    /// with <c>and</c> and parenthesised; <c>null</c> when none exists (no private message can exist, so the read is unchanged).
    /// A metadata read that faults propagates, and one that gives no answer throws: either is a fault of the caller's read.
    /// </summary>
    internal static async Task<string?> NotPrivateODataFilterAsync(
        IGenericEntityService entities, CancellationToken ct, bool rememberMissing = false)
    {
        var present = await PresentColumnsAsync(entities, ct, rememberMissing).ConfigureAwait(false);
        return present.Count == 0 ? null : "(" + string.Join(" and ", present.Select(c => $"_{c}_value eq null")) + ")";
    }

    /// <summary>
    /// The reference columns that exist on <c>sprk_communication</c> (published metadata, through the send gate's cached
    /// column-presence check, <see cref="PrivateMessageSendGate.CommunicationColumnsPresentAsync"/>), so a read of the table can
    /// select exactly those for <see cref="IsPrivate"/>. Empty when none exists (no private message can exist). A metadata read that
    /// faults propagates, and one that gives no answer throws: either is a fault of the caller's read.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> PresentColumnsAsync(
        IGenericEntityService entities, CancellationToken ct, bool rememberMissing = false)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var present = new List<string>(All.Count);
        foreach (var column in All)
        {
            switch (await PrivateMessageSendGate.CommunicationColumnsPresentAsync(
                    entities, new[] { column }, ct, rememberMissing: rememberMissing).ConfigureAwait(false))
            {
                case true:
                    present.Add(column);
                    break;
                case false:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Whether {CommunicationTable}.{column} exists could not be read; the read does not run without that answer.");
            }
        }

        return present;
    }

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
