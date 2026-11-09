using Sprk.Bff.Api.Services.Dataverse.FieldMapping;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b fix round 1 (F1): THE root redaction set, the one list of every column form that can carry the invoice link of
/// a ROOT row (<see cref="InvoiceLinks.IsRootWithInvoiceLink"/>; today the work assignment). A root is never hidden by its invoice link: it
/// is decided by its grant, and its invoice link is shown only when that invoice is visible to the caller. Every plane that returns a root
/// row, or answers a predicate on one, takes its columns from here: the assistant tools blank them
/// (<see cref="ExternalInvoiceVisibility.BlankRootInvoiceLink"/>) and refuse a filter or sort on them; the external routes use the same set.
/// </summary>
/// <remarks>
/// <para><b>Two parts, both derived.</b> (1) The invoice link: every invoice link column of the table (<see cref="InvoiceLinks.LinkColumnsOf"/>,
/// from the edge registries). (2) The regarding pair and its resolver columns (<see cref="CreateTimeFieldMapping.RegardingPairAndResolverColumns"/>:
/// id, record type, name, URL, number), which hold the invoice when the regarding target IS the linked invoice. Each column is listed in every
/// form a tool can carry it: the logical name, the Web API value form (<c>_col_value</c>) and the lookup name forms (<c>colname</c>,
/// <c>colyominame</c>). An annotation (<c>key@…</c>) is matched by its column. A form a table does not have matches nothing.</para>
/// <para><b>Guarded.</b> <c>RootInvoiceLinkRedactionTests</c> fails the build when a root with an invoice link declares a regarding column
/// (in the solution source) that is not in this set.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="ExternalInvoiceVisibility"/> held a hand-written list of three
/// regarding columns; <see cref="CreateTimeFieldMapping.RegardingPairAndResolverColumns"/> lists the regarding columns for the write path only.
/// (2) Extension — the set composes both registries instead of copying them; it lives in its own file because <see cref="InvoiceLinks"/> is
/// shared with a parallel branch and the external routes need the same set without the tools' predicate. (3) Cost of doing nothing — the
/// regarding number and record type of a root show, and a filter on them answers, that the root is linked to an invoice the caller may not see.</para>
/// </remarks>
internal static class RootInvoiceLinkRedaction
{
    /// <summary>The regarding pair and resolver columns (logical names), from <see cref="CreateTimeFieldMapping.RegardingPairAndResolverColumns"/>.</summary>
    internal static IReadOnlySet<string> RegardingColumns => CreateTimeFieldMapping.RegardingPairAndResolverColumns;

    /// <summary>Every form a tool can carry <paramref name="column"/> in: the logical name, <c>_col_value</c>, <c>colname</c>, <c>colyominame</c>.</summary>
    internal static IEnumerable<string> FormsOf(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        yield return column;
        yield return $"_{column}_value";
        yield return column + "name";
        yield return column + "yominame";
    }

    /// <summary>Every form of the regarding pair and resolver columns.</summary>
    internal static IReadOnlySet<string> RegardingForms { get; } =
        RegardingColumns.SelectMany(FormsOf).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every form of the invoice link columns of <paramref name="table"/>; empty when it is not a root with an invoice link.</summary>
    internal static IReadOnlySet<string> LinkFormsOf(string? table) =>
        InvoiceLinks.IsRootWithInvoiceLink(table)
            ? InvoiceLinks.LinkColumnsOf(table).SelectMany(FormsOf).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The root redaction set of <paramref name="table"/>: every form of its invoice link columns and of the regarding pair and resolver
    /// columns. Empty when <paramref name="table"/> is not a root with an invoice link (nothing is redacted on it).
    /// </summary>
    internal static IReadOnlySet<string> ColumnsOf(string? table) =>
        table is not null && ByRoot.TryGetValue(table, out var set) ? set : Empty;

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The redaction set of each root with an invoice link, built once from the registries.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByRoot =
        InvoiceLinks.Tables.Keys.Where(InvoiceLinks.IsRootWithInvoiceLink).ToDictionary(
            table => table,
            table => (IReadOnlySet<string>)LinkFormsOf(table).Concat(RegardingForms).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The column a key names: a key without its annotation (<c>col@OData.Community.Display.V1.FormattedValue</c> → <c>col</c>).</summary>
    internal static string ColumnOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Split('@', 2)[0];
    }

    /// <summary>True when <paramref name="key"/> (a column, any of its forms, or an annotation on one) is in the redaction set of <paramref name="table"/>.</summary>
    internal static bool IsRedacted(string? table, string key) => ColumnsOf(table).Contains(ColumnOf(key));
}
