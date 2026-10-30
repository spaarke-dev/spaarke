using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Finance;

// ebilling-r1 task 150 (FR-41, D-56, D-21): the AP export mapping and the two file writers. Pure: no I/O, so the columns, the order, the
// quoting and the formats are asserted directly (ApExportMappingTests).

/// <summary>One mapped column: its output <see cref="Name"/>, the dotted <see cref="Source"/> path it reads and an optional <see cref="Format"/>.</summary>
public sealed record ApExportColumn(string Name, string Source, string? Format);

/// <summary>The parsed mapping: <see cref="Columns"/> is the CSV row and the JSON invoice object, <see cref="LineColumns"/> the JSON <c>lines[]</c>.</summary>
public sealed record ApExportMappingModel(IReadOnlyList<ApExportColumn> Columns, IReadOnlyList<ApExportColumn> LineColumns);

/// <summary>The answer of <see cref="ApExportMappingParser.Parse"/>: the model, or why the mapping cannot be used (nothing is exported then).</summary>
public sealed record ApExportMappingResult(ApExportMappingModel? Model, string? Error)
{
    public bool IsValid => Model is not null;
}

/// <summary>
/// The source-path vocabulary (the first shape 025 seeded; task 150 owns it). A column whose source is NOT in the vocabulary makes the
/// mapping invalid (a typo must not silently write an empty AP column). A source that IS in the vocabulary but has no value for an
/// invoice is written empty: <see cref="AlwaysEmptyInvoiceSources"/> lists those the R1 schema has no field for.
/// </summary>
public static class ApExportSources
{
    public static readonly IReadOnlySet<string> InvoiceSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "batch.id", "invoice.id", "firm.name", "firm.apVendorId", "firm.taxId", "invoice.firmInvoiceNumber", "invoice.trackingNumber",
        "invoice.invoiceDate", "invoice.dueDate", "invoice.billingPeriodStart", "invoice.billingPeriodEnd", "invoice.currency",
        "invoice.billedTotal", "invoice.lineAdjustmentsTotal", "invoice.invoiceAdjustmentsTotal", "invoice.approvedTotal",
        "record.type", "record.number", "record.name", "record.lawDepartmentMatterNumber", "record.firmClientMatterNumber",
        "invoice.poNumber", "approval.approvedBy", "approval.approvedOn", "invoice.documentUrl",
    };

    public static readonly IReadOnlySet<string> LineSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "line.date", "line.type", "line.timekeeper", "line.hours", "line.rate", "line.billed", "line.approved",
        "line.taskCode", "line.activityCode", "line.expenseCode",
    };

    /// <summary>
    /// D-56 columns the R1 schema has no source field for (no vendor AP id or tax id on the firm, no PO number, no firm client-matter
    /// number, no stable document URL). They stay in the default mapping and are written empty; the owner decides whether to add a source.
    /// </summary>
    public static readonly IReadOnlySet<string> AlwaysEmptyInvoiceSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "firm.apVendorId", "firm.taxId", "record.firmClientMatterNumber", "invoice.poNumber", "invoice.documentUrl",
    };
}

/// <summary>The D-56 default mapping (used when the settings column is blank) and its canonical JSON text, which is also what the batch snapshots.</summary>
public static class ApExportDefaultMapping
{
    private static readonly (string Name, string Source, string? Format)[] DefaultColumns =
    [
        ("BatchId", "batch.id", null), ("SpaarkeInvoiceId", "invoice.id", null), ("VendorName", "firm.name", null),
        ("VendorApId", "firm.apVendorId", null), ("VendorTaxId", "firm.taxId", null), ("InvoiceNumber", "invoice.firmInvoiceNumber", null),
        ("TrackingNumber", "invoice.trackingNumber", null), ("InvoiceDate", "invoice.invoiceDate", "date"), ("DueDate", "invoice.dueDate", "date"),
        ("BillingPeriodStart", "invoice.billingPeriodStart", "date"), ("BillingPeriodEnd", "invoice.billingPeriodEnd", "date"),
        ("Currency", "invoice.currency", null), ("BilledTotal", "invoice.billedTotal", "decimal"),
        ("LineAdjustmentsTotal", "invoice.lineAdjustmentsTotal", "decimal"), ("InvoiceAdjustmentsTotal", "invoice.invoiceAdjustmentsTotal", "decimal"),
        ("ApprovedTotal", "invoice.approvedTotal", "decimal"), ("CoreRecordType", "record.type", null), ("CoreRecordNumber", "record.number", null),
        ("CoreRecordName", "record.name", null), ("LawDepartmentMatterNumber", "record.lawDepartmentMatterNumber", null),
        ("FirmClientMatterNumber", "record.firmClientMatterNumber", null), ("PONumber", "invoice.poNumber", null),
        ("ApprovedBy", "approval.approvedBy", null), ("ApprovedOn", "approval.approvedOn", "date"), ("DocumentUrl", "invoice.documentUrl", null),
    ];

    private static readonly (string Name, string Source, string? Format)[] DefaultLineColumns =
    [
        ("date", "line.date", "date"), ("type", "line.type", null), ("timekeeper", "line.timekeeper", null), ("hours", "line.hours", "decimal"),
        ("rate", "line.rate", "decimal"), ("billed", "line.billed", "decimal"), ("approved", "line.approved", "decimal"),
        ("taskCode", "line.taskCode", null), ("activityCode", "line.activityCode", null), ("expenseCode", "line.expenseCode", null),
    ];

    public static ApExportMappingModel Model { get; } = new(
        DefaultColumns.Select(c => new ApExportColumn(c.Name, c.Source, c.Format)).ToArray(),
        DefaultLineColumns.Select(c => new ApExportColumn(c.Name, c.Source, c.Format)).ToArray());

    /// <summary>The default as JSON in the shape 025 seeds (version 1, <c>columns</c>, <c>lineColumns</c>).</summary>
    public static string Json { get; } = JsonSerializer.Serialize(new
    {
        version = 1,
        columns = DefaultColumns.Select(c => new { name = c.Name, source = c.Source, format = c.Format }),
        lineColumns = DefaultLineColumns.Select(c => new { name = c.Name, source = c.Source, format = c.Format }),
    }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
}

/// <summary>Reads the settings column (JSON, version 1) into an <see cref="ApExportMappingModel"/>; a blank column is the D-56 default.</summary>
public static class ApExportMappingParser
{
    public const int SupportedVersion = 1;

    public static ApExportMappingResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ApExportMappingResult(ApExportDefaultMapping.Model, null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("the mapping is not a JSON object");
            }

            if (!TryGet(root, "version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != SupportedVersion)
            {
                return Invalid($"the mapping version must be {SupportedVersion}");
            }

            if (!TryGet(root, "columns", out var columns))
            {
                return Invalid("the mapping has no columns");
            }

            if (ReadColumns(columns, ApExportSources.InvoiceSources, out var invoiceColumns) is { } columnError)
            {
                return Invalid($"columns: {columnError}");
            }

            IReadOnlyList<ApExportColumn> lines = [];
            if (TryGet(root, "lineColumns", out var lineColumns) && lineColumns.ValueKind != JsonValueKind.Null
                && ReadColumns(lineColumns, ApExportSources.LineSources, out lines) is { } lineError)
            {
                return Invalid($"lineColumns: {lineError}");
            }

            return new ApExportMappingResult(new ApExportMappingModel(invoiceColumns, lines), null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Invalid("the mapping is not valid JSON");
        }
    }

    private static ApExportMappingResult Invalid(string error) => new(null, error);

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Null when the list is valid; otherwise why not. At least one column, unique non-blank names, a known source, a known format.</summary>
    private static string? ReadColumns(JsonElement array, IReadOnlySet<string> vocabulary, out IReadOnlyList<ApExportColumn> columns)
    {
        columns = [];
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
        {
            return "must be a non-empty array";
        }

        var list = new List<ApExportColumn>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return "an entry is not an object";
            }

            var name = TryGet(entry, "name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var source = TryGet(entry, "source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var format = TryGet(entry, "format", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return "an entry has no name";
            }

            if (!names.Add(name))
            {
                return $"the name '{name}' is used twice";
            }

            if (source is null || !vocabulary.Contains(source))
            {
                return $"'{name}' reads the unknown source '{source}'";
            }

            if (format is not (null or "date" or "decimal"))
            {
                return $"'{name}' has the unknown format '{format}'";
            }

            list.Add(new ApExportColumn(name, source, format));
        }

        columns = list;
        return null;
    }
}

/// <summary>One invoice's values for the export: the invoice-level values by source path and its lines (line values by source path).</summary>
public sealed record ApExportInvoiceValues(
    string SortKey, Guid InvoiceId, IReadOnlyDictionary<string, object?> Values, IReadOnlyList<IReadOnlyDictionary<string, object?>> Lines);

/// <summary>Formats mapped values and writes the CSV and JSON bodies. Deterministic: same input, same bytes.</summary>
public static class ApExportWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>The invoices in file order: by firm invoice number (the sort key), then id, ordinal.</summary>
    public static IReadOnlyList<ApExportInvoiceValues> InFileOrder(IEnumerable<ApExportInvoiceValues> invoices) =>
        invoices.OrderBy(i => i.SortKey, StringComparer.Ordinal).ThenBy(i => i.InvoiceId).ToList();

    /// <summary>RFC 4180: a header row and one row per invoice, CRLF line ends, no BOM; a field with a comma, quote or line break is quoted.</summary>
    public static byte[] WriteCsv(ApExportMappingModel mapping, IEnumerable<ApExportInvoiceValues> invoices)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var builder = new StringBuilder();
        builder.Append(string.Join(',', mapping.Columns.Select(c => Quote(c.Name)))).Append("\r\n");
        foreach (var invoice in InFileOrder(invoices))
        {
            builder.Append(string.Join(',', mapping.Columns.Select(c => Quote(CsvText(c, invoice.Values))))).Append("\r\n");
        }

        return Utf8NoBom.GetBytes(builder.ToString());
    }

    /// <summary>
    /// <c>{ batchId, invoiceCount, invoices: [ { mapped columns..., lines: [ { mapped line columns } ] } ] }</c>. Dates are ISO strings, decimals
    /// are JSON numbers, an absent value is null.
    /// </summary>
    public static byte[] WriteJson(string batchId, ApExportMappingModel mapping, IEnumerable<ApExportInvoiceValues> invoices)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var ordered = InFileOrder(invoices);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("batchId", batchId);
            json.WriteNumber("invoiceCount", ordered.Count);
            json.WriteStartArray("invoices");
            foreach (var invoice in ordered)
            {
                json.WriteStartObject();
                foreach (var column in mapping.Columns)
                {
                    WriteJsonValue(json, column, invoice.Values);
                }

                json.WriteStartArray("lines");
                foreach (var line in invoice.Lines)
                {
                    json.WriteStartObject();
                    foreach (var column in mapping.LineColumns)
                    {
                        WriteJsonValue(json, column, line);
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>A mapped value as text (CSV): ISO date, invariant decimal with at least two places, otherwise the value's invariant text.</summary>
    internal static string? FormatText(ApExportColumn column, IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue(column.Source, out var raw) || raw is null)
        {
            return null;
        }

        return (column.Format, raw) switch
        {
            ("date", DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ("date", DateTime dt) => DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ("decimal", decimal m) => m.ToString("0.00##", CultureInfo.InvariantCulture),
            (_, decimal m) => m.ToString("0.00##", CultureInfo.InvariantCulture),
            (_, DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (_, DateTime dt) => DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (_, Guid g) => g.ToString("D"),
            (_, IFormattable f) => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };
    }

    /// <summary>
    /// The CSV text of a value. A TEXT value (not a number or date) that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return is
    /// prefixed with an apostrophe so a spreadsheet that opens the file never runs it as a formula (CSV injection); a number or date is left as is.
    /// </summary>
    private static string CsvText(ApExportColumn column, IReadOnlyDictionary<string, object?> values)
    {
        var text = FormatText(column, values) ?? string.Empty;
        var isTextValue = values.TryGetValue(column.Source, out var raw) && raw is string;
        return isTextValue && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    private static void WriteJsonValue(Utf8JsonWriter json, ApExportColumn column, IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue(column.Source, out var raw) || raw is null)
        {
            json.WriteNull(column.Name);
            return;
        }

        switch (raw)
        {
            case decimal m:
                json.WriteNumber(column.Name, m);
                break;
            case int i:
                json.WriteNumber(column.Name, i);
                break;
            default:
                json.WriteString(column.Name, FormatText(column, values));
                break;
        }
    }
}
