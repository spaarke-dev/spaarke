using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.ContentUnderstanding;

/// <summary>
/// Maps the <c>sprk_invoice_vN</c> analyzer's document content(s) to <see cref="ExtractedInvoice"/> (ebilling-r1 task
/// 043; FR-21, FR-04). Pure: no I/O, no logging. Field names are the analyzer's, as actually returned on both API
/// versions (task 004 / 005; <c>infrastructure/content-understanding/sprk-invoice-v2*.json</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Tolerant by design.</b> A missing field, a value of an unexpected type or an unparsable source becomes an empty
/// value (null), never an exception: "missing optional field" is normal for invoices (an expense table with no unit
/// cost, a row with no title).
/// </para>
/// <para>
/// <b>Source.</b> <c>source</c> is <c>D(page,x1,y1,…,x4,y4)</c> (a polygon) or <c>D(page,x,y,w,h)</c>, several regions
/// separated by <c>;</c>, in the content's unit (inch for PDF). The page is the first region's (original-file numbering,
/// also for page-range reads); the box is the axis-aligned union of the regions on that page, divided by that page's
/// width and height from <c>pages[]</c> and clamped to 0-1. No page size → page kept, box null.
/// </para>
/// <para>
/// <b>Several page ranges.</b> A long document read in ranges yields one content per range. Header fields take the
/// value with the highest confidence across ranges (ties: the earliest range); rows are concatenated in range order.
/// </para>
/// </remarks>
internal static partial class InvoiceAnalyzerResultMapper
{
    public static ExtractedInvoice ToInvoice(ContentUnderstandingAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var ranges = analysis.Contents.Select(c => new RangeContent(c)).ToList();

        var header = new ExtractedInvoiceHeader
        {
            FirmName = BestHeader(ranges, "FirmName", ReadString),
            FirmInvoiceNumber = BestHeader(ranges, "FirmInvoiceNumber", ReadString),
            InvoiceDate = BestHeader(ranges, "InvoiceDate", ReadDate),
            BillingPeriodStart = BestHeader(ranges, "BillingPeriodStart", ReadDate),
            BillingPeriodEnd = BestHeader(ranges, "BillingPeriodEnd", ReadDate),
            ClientName = BestHeader(ranges, "ClientName", ReadString),
            MatterName = BestHeader(ranges, "MatterName", ReadString),
            FirmMatterReference = BestHeader(ranges, "FirmMatterReference", ReadString),
            PurchaseOrderAsQuoted = BestHeader(ranges, "PurchaseOrderAsQuoted", ReadString),
            FeesTotal = BestHeader(ranges, "FeesTotal", ReadNumber),
            ExpensesTotal = BestHeader(ranges, "ExpensesTotal", ReadNumber),
            AdjustmentsTotal = BestHeader(ranges, "AdjustmentsTotal", ReadNumber),
            TotalDue = BestHeader(ranges, "TotalDue", ReadNumber),
            Currency = BestHeader(ranges, "Currency", ReadString),
        };

        var fees = new List<ExtractedInvoiceLine>();
        var expenses = new List<ExtractedInvoiceLine>();
        var summary = new List<ExtractedTimekeeperSummaryRow>();
        foreach (var range in ranges)
        {
            foreach (var row in range.Rows("TimeEntries"))
            {
                fees.Add(WithRowBox(new ExtractedInvoiceLine
                {
                    LineType = InvoiceLineType.Fee,
                    RowIndex = fees.Count,
                    Date = ReadDate(row, "Date", range),
                    TimekeeperAsBilled = ReadString(row, "TimekeeperAsBilled", range),
                    TitleAsBilled = ReadString(row, "TitleAsBilled", range),
                    Hours = ReadNumber(row, "Hours", range),
                    Rate = ReadNumber(row, "Rate", range),
                    Quantity = Empty<decimal?>(),
                    UnitCost = Empty<decimal?>(),
                    Amount = ReadNumber(row, "Amount", range),
                    Narrative = ReadString(row, "Narrative", range),
                    TaskCodeAsBilled = ReadString(row, "TaskCodeAsBilled", range),
                    ActivityCodeAsBilled = ReadString(row, "ActivityCodeAsBilled", range),
                    ExpenseCodeAsBilled = Empty<string?>(),
                }));
            }

            foreach (var row in range.Rows("Expenses"))
            {
                expenses.Add(WithRowBox(new ExtractedInvoiceLine
                {
                    LineType = InvoiceLineType.Expense,
                    RowIndex = expenses.Count,
                    Date = ReadDate(row, "Date", range),
                    TimekeeperAsBilled = Empty<string?>(),
                    TitleAsBilled = Empty<string?>(),
                    Hours = Empty<decimal?>(),
                    Rate = Empty<decimal?>(),
                    Quantity = ReadNumber(row, "Quantity", range),
                    UnitCost = ReadNumber(row, "UnitCost", range),
                    Amount = ReadNumber(row, "Amount", range),
                    Narrative = ReadString(row, "Description", range),
                    TaskCodeAsBilled = Empty<string?>(),
                    ActivityCodeAsBilled = Empty<string?>(),
                    ExpenseCodeAsBilled = ReadString(row, "ExpenseCodeAsBilled", range),
                }));
            }

            foreach (var row in range.Rows("TimekeeperSummary"))
            {
                summary.Add(new ExtractedTimekeeperSummaryRow
                {
                    Name = ReadString(row, "Name", range),
                    Title = ReadString(row, "Title", range),
                    Hours = ReadNumber(row, "Hours", range),
                    Rate = ReadNumber(row, "Rate", range),
                    Amount = ReadNumber(row, "Amount", range),
                });
            }
        }

        return new ExtractedInvoice
        {
            Header = header,
            Lines = fees.Concat(expenses).ToList(),
            TimekeeperSummary = summary,
            PageCount = analysis.PageCount,
            AnalyzerId = analysis.AnalyzerId,
            ApiVersion = analysis.ApiVersion,
        };
    }

    // ── header merge ──────────────────────────────────────────────────────────────────────────────────────

    private static ExtractedInvoiceField<T> BestHeader<T>(
        IReadOnlyList<RangeContent> ranges,
        string name,
        Func<JsonElement, string, RangeContent, ExtractedInvoiceField<T>> read)
    {
        ExtractedInvoiceField<T>? best = null;
        ExtractedInvoiceField<T>? firstEmpty = null;
        foreach (var range in ranges)
        {
            var field = read(range.Fields, name, range);
            if (field.Value is null)
            {
                firstEmpty ??= field;
                continue;
            }

            if (best is null || (field.Confidence ?? -1m) > (best.Confidence ?? -1m))
            {
                best = field;
            }
        }

        return best ?? firstEmpty ?? Empty<T>();
    }

    // ── value readers ─────────────────────────────────────────────────────────────────────────────────────

    private static ExtractedInvoiceField<string?> ReadString(JsonElement parent, string name, RangeContent range)
    {
        if (!TryField(parent, name, out var field))
        {
            return Empty<string?>();
        }

        string? value = null;
        if (field.TryGetProperty("valueString", out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString();
        }

        return Build<string?>(field, string.IsNullOrWhiteSpace(value) ? null : value, range);
    }

    private static ExtractedInvoiceField<DateOnly?> ReadDate(JsonElement parent, string name, RangeContent range)
    {
        if (!TryField(parent, name, out var field))
        {
            return Empty<DateOnly?>();
        }

        DateOnly? value = null;
        var raw = field.TryGetProperty("valueDate", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (raw is not null
            && DateOnly.TryParseExact(raw.Length >= 10 ? raw[..10] : raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            value = d;
        }

        return Build<DateOnly?>(field, value, range);
    }

    private static ExtractedInvoiceField<decimal?> ReadNumber(JsonElement parent, string name, RangeContent range)
    {
        if (!TryField(parent, name, out var field))
        {
            return Empty<decimal?>();
        }

        decimal? value = null;
        foreach (var property in new[] { "valueNumber", "valueInteger" })
        {
            if (field.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n))
            {
                value = n;
                break;
            }
        }

        return Build<decimal?>(field, value, range);
    }

    private static ExtractedInvoiceField<T> Build<T>(JsonElement field, T? value, RangeContent range)
    {
        decimal? confidence = null;
        if (field.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var conf)
            && conf >= 0m && conf <= 1m)
        {
            confidence = conf;
        }

        int? page = null;
        ExtractedBoundingBox? box = null;
        if (value is not null && field.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String)
        {
            (page, box) = ParseSource(s.GetString(), range);
        }

        return new ExtractedInvoiceField<T>
        {
            Value = value,
            ReturnedValue = value,
            Confidence = confidence,
            Page = page,
            BoundingBox = box,
        };
    }

    private static ExtractedInvoiceField<T> Empty<T>() => new();

    private static bool TryField(JsonElement parent, string name, out JsonElement field)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out field) && field.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        field = default;
        return false;
    }

    // ── source → page and box ─────────────────────────────────────────────────────────────────────────────

    internal static (int? Page, ExtractedBoundingBox? Box) ParseSource(string? source, RangeContent range)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return (null, null);
        }

        int? page = null;
        decimal minX = decimal.MaxValue, minY = decimal.MaxValue, maxX = decimal.MinValue, maxY = decimal.MinValue;
        foreach (Match match in SourceRegion().Matches(source))
        {
            var numbers = new List<decimal>();
            foreach (var part in match.Groups[1].Value.Split(','))
            {
                if (!decimal.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                {
                    numbers.Clear();
                    break;
                }

                numbers.Add(n);
            }

            // page + 4 numbers (x, y, w, h) or page + an even number >= 6 of polygon coordinates.
            if (numbers.Count < 5 || numbers[0] < 1 || numbers[0] != decimal.Truncate(numbers[0]))
            {
                continue;
            }

            var regionPage = (int)numbers[0];
            page ??= regionPage;
            if (regionPage != page)
            {
                continue;
            }

            var coords = numbers.Skip(1).ToList();
            if (coords.Count == 4)
            {
                minX = Math.Min(minX, coords[0]);
                minY = Math.Min(minY, coords[1]);
                maxX = Math.Max(maxX, coords[0] + coords[2]);
                maxY = Math.Max(maxY, coords[1] + coords[3]);
            }
            else if (coords.Count >= 6 && coords.Count % 2 == 0)
            {
                for (var i = 0; i < coords.Count; i += 2)
                {
                    minX = Math.Min(minX, coords[i]);
                    maxX = Math.Max(maxX, coords[i]);
                    minY = Math.Min(minY, coords[i + 1]);
                    maxY = Math.Max(maxY, coords[i + 1]);
                }
            }
        }

        if (page is null || minX == decimal.MaxValue || !range.TryGetPageSize(page.Value, out var width, out var height))
        {
            return (page, null);
        }

        var x = Normalise(minX, width);
        var y = Normalise(minY, height);
        return (page, new ExtractedBoundingBox(
            page.Value, x, y, Normalise(maxX, width) - x, Normalise(maxY, height) - y));
    }

    private static decimal Normalise(decimal value, decimal extent) =>
        Math.Round(Math.Clamp(value / extent, 0m, 1m), 4, MidpointRounding.AwayFromZero);

    private static ExtractedInvoiceLine WithRowBox(ExtractedInvoiceLine line)
    {
        var boxes = new[]
        {
            line.Date.BoundingBox, line.TimekeeperAsBilled.BoundingBox, line.TitleAsBilled.BoundingBox, line.Hours.BoundingBox,
            line.Rate.BoundingBox, line.Quantity.BoundingBox, line.UnitCost.BoundingBox, line.Amount.BoundingBox,
            line.Narrative.BoundingBox, line.TaskCodeAsBilled.BoundingBox, line.ActivityCodeAsBilled.BoundingBox,
            line.ExpenseCodeAsBilled.BoundingBox,
        }.Where(b => b is not null).Select(b => b!).ToList();
        if (boxes.Count == 0)
        {
            return line;
        }

        var page = boxes[0].Page;
        var onPage = boxes.Where(b => b.Page == page).ToList();
        var x = onPage.Min(b => b.X);
        var y = onPage.Min(b => b.Y);
        var right = onPage.Max(b => b.X + b.W);
        var bottom = onPage.Max(b => b.Y + b.H);
        return line with { RowBoundingBox = new ExtractedBoundingBox(page, x, y, right - x, bottom - y) };
    }

    [GeneratedRegex(@"D\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex SourceRegion();

    /// <summary>One range's document content: its fields and its page sizes.</summary>
    internal sealed class RangeContent
    {
        private readonly Dictionary<int, (decimal Width, decimal Height)> _pageSizes = new();

        public RangeContent(JsonElement content)
        {
            Fields = content.ValueKind == JsonValueKind.Object
                     && content.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object
                ? fields
                : default;

            if (content.ValueKind == JsonValueKind.Object
                && content.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pages.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.Object
                        && p.TryGetProperty("pageNumber", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var number)
                        && p.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetDecimal(out var width) && width > 0
                        && p.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetDecimal(out var height) && height > 0)
                    {
                        _pageSizes[number] = (width, height);
                    }
                }
            }
        }

        /// <summary>The content's <c>fields</c> object (default when absent).</summary>
        public JsonElement Fields { get; }

        public bool TryGetPageSize(int page, out decimal width, out decimal height)
        {
            if (_pageSizes.TryGetValue(page, out var size))
            {
                (width, height) = size;
                return true;
            }

            width = height = 0m;
            return false;
        }

        /// <summary>The <c>valueObject</c> of each item of the named array field.</summary>
        public IEnumerable<JsonElement> Rows(string arrayField)
        {
            if (!TryField(Fields, arrayField, out var field)
                || !field.TryGetProperty("valueArray", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("valueObject", out var row) && row.ValueKind == JsonValueKind.Object)
                {
                    yield return row;
                }
            }
        }
    }
}
