// ebilling-r1 082c-2 fix round 3 (uac-r2 U1, 2026-11-05): the external invoice read returns sprk_invoice.sprk_reviewdetail only while the
// invoice is In Review.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing - ExternalModuleDescriptor.ReadableColumns and StripToReadableColumns (a value-blind column allow-list), and the invoice
//       predicate (HideInvoiceRowsAsync: hides whole rows). Nothing shapes one column's value by another's.
//   (2) Extension - an allow-list cannot say "this column, only while statuscode is 1". The projection is a pure function over a row, beside
//       the invoice-only steps the fetch and record routes already take (InvoiceFamilyKeyAttributes, HideInvoiceRowsAsync); no descriptor
//       member, so no other module's behaviour changes.
//   (3) Cost of doing nothing - the invariant "non-blank only while In Review" held only for status moves made through the BFF. A status change
//       outside it (the native form, a field mapping, a playbook Update Record writing statuscode) leaves the label set, and the partner's
//       read returned it raw: "waiting for the firm" on an approved or paid invoice.
//
// Placement (bff-extensions.md): BFF, Infrastructure/ExternalAccess beside ExternalInvoiceVisibility. No DI registration, no endpoint, no package.

using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// The server-side form of the review detail's invariant for the external invoice reads (the grid fetch and the single-record read): the column
/// comes back <b>null</b> unless the row's <c>statuscode</c> is <see cref="InvoiceStatusCodes.InReview"/>.
/// </summary>
/// <remarks>
/// <para><b>Fail closed.</b> A row that carries the column but no readable <c>statuscode</c> (absent, null, or a shape that is not a number) has
/// it nulled: the invariant cannot be shown to hold. The routes read <c>statuscode</c> server-side so a caller who asked for the detail but not the
/// status is still judged on the real status (the fetch route's <c>serverProjected</c> mechanism; the record route adds it to the read), and the
/// status is removed from the response when the caller did not ask for it.</para>
/// <para>The display label travels in the row's <c>@formattedValues</c> too; it is removed with the value.</para>
/// <para>Only <c>sprk_invoice</c> modules are affected (<see cref="Applies"/>); a row that does not carry the column is returned as it is.</para>
/// </remarks>
internal static class ExternalInvoiceReviewDetail
{
    /// <summary>The status column the projection reads.</summary>
    internal const string StatusColumn = "statuscode";

    private const string FormattedValuesKey = "@formattedValues";

    /// <summary>Whether <paramref name="recordEntity"/> is the invoice table (the only one this applies to).</summary>
    internal static bool Applies(string? recordEntity) =>
        string.Equals(recordEntity, ExternalInvoiceVisibility.InvoiceTable, StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> DetailOnly =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { InvoiceReviewDetail.Column };

    /// <summary>
    /// 082c-2 fix round 4 (R4-1): the columns a caller may read but not filter or sort on, for <paramref name="recordEntity"/>. The review detail
    /// is returned only while In Review, so a condition (<c>attribute</c> or <c>valueof</c>) or an order on it would reveal the value on the rows
    /// that null it. Empty (null) for every other entity: no other module changes behaviour.
    /// </summary>
    internal static IReadOnlySet<string>? UnfilterableColumns(string? recordEntity) => Applies(recordEntity) ? DetailOnly : null;

    /// <summary>The attributes the server must read for the projection to judge a row: the status, for an invoice module.</summary>
    internal static IEnumerable<string> RequiredAttributes(string? recordEntity) =>
        Applies(recordEntity) ? new[] { StatusColumn } : Array.Empty<string>();

    /// <summary>
    /// Whether a single-record <c>$select</c> asks for the review detail but not the status, so the read must add the status to judge it on (and
    /// the response must not return it).
    /// </summary>
    internal static bool NeedsStatusRead(string[]? select) =>
        select is not null
        && select.Contains(InvoiceReviewDetail.Column, StringComparer.OrdinalIgnoreCase)
        && !select.Contains(StatusColumn, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="row"/> with the review detail (value and display label) nulled unless the row's status is In Review. A row that does not
    /// carry the column is returned as it is. Pure.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> Project(IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var carriesDetail = false;
        foreach (var key in row.Keys)
        {
            if (IsDetail(key))
            {
                carriesDetail = true;
            }
        }

        if (row.TryGetValue(FormattedValuesKey, out var formattedValue) && formattedValue is IEnumerable<KeyValuePair<string, string>> labels)
        {
            foreach (var label in labels)
            {
                if (IsDetail(label.Key))
                {
                    carriesDetail = true;
                }
            }
        }

        if (!carriesDetail || IsInReview(row))
        {
            return row;
        }

        var projected = new Dictionary<string, object?>(row.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in row)
        {
            if (IsDetail(key))
            {
                projected[key] = null;
            }
            else if (string.Equals(key, FormattedValuesKey, StringComparison.Ordinal) && value is IEnumerable<KeyValuePair<string, string>> map)
            {
                var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var label in map)
                {
                    if (!IsDetail(label.Key))
                    {
                        kept[label.Key] = label.Value;
                    }
                }

                projected[key] = kept;
            }
            else
            {
                projected[key] = value;
            }
        }

        return projected;
    }

    /// <summary>Whether <paramref name="name"/> is the review detail column (the one place this projection names it).</summary>
    private static bool IsDetail(string name) => string.Equals(name, InvoiceReviewDetail.Column, StringComparison.OrdinalIgnoreCase);

    private static bool IsInReview(IReadOnlyDictionary<string, object?> row)
    {
        string? key = null;
        foreach (var candidate in row.Keys)
        {
            if (string.Equals(candidate, StatusColumn, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
            }
        }

        return key is not null && row[key] switch
        {
            int value => value == InvoiceStatusCodes.InReview,
            long value => value == InvoiceStatusCodes.InReview,
            short value => value == InvoiceStatusCodes.InReview,
            Microsoft.Xrm.Sdk.OptionSetValue option => option.Value == InvoiceStatusCodes.InReview,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } json => json.TryGetInt32(out var number) && number == InvoiceStatusCodes.InReview,
            _ => false, // absent, null or another shape: not shown to be In Review (fail closed)
        };
    }
}
