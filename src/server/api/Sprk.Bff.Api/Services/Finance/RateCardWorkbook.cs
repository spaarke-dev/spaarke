using System.Globalization;
using System.IO.Compression;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// One problem found in a rate card workbook (ebilling-r1 task 078, FR-29): where it is (sheet, row, column) and what is wrong. Row
/// numbers are the spreadsheet's own (row 1 is the header). Sheet / row / column are null for a problem that is not in a cell (a missing
/// sheet, an unreadable file, an unknown firm).
/// </summary>
public sealed record RateCardImportError(string? Sheet, int? Row, string? Column, string Message);

/// <summary>One data row read from a sheet: the spreadsheet row number and the cell text keyed by header name (blank cells absent).</summary>
internal sealed record WorkbookRow(string Sheet, int Row, IReadOnlyDictionary<string, string> Cells)
{
    public string? Get(string column) => Cells.TryGetValue(column, out var v) ? v : null;
}

/// <summary>The result of reading a workbook: its data rows, or the structural problems that stopped it.</summary>
internal sealed record WorkbookRead(
    IReadOnlyList<WorkbookRow> TimekeeperRows,
    IReadOnlyList<WorkbookRow> RateRows,
    IReadOnlyList<RateCardImportError> Errors);

/// <summary>
/// The LEDES timekeeper classifications of the global choice <c>sprk_timekeeperrole</c> (D-84: exactly these 20 options; label = LEDES
/// name, ExternalValue = LEDES code). The workbook accepts either the label or the code. The list is the data-model document
/// <c>docs/data-model/sprk_ratecard.md</c> "Classification"; a new option added to the choice must be added here too.
/// </summary>
internal static class TimekeeperClassifications
{
    internal static readonly IReadOnlyList<(int Value, string Code, string Label)> All = new (int, string, string)[]
    {
        (100000001, "PARTNR", "Partner"),
        (100000014, "OFCOUN", "Of Counsel"),
        (100000003, "ASSOC", "Associate"),
        (100000015, "TRANEE", "Associate Trainee"),
        (100000016, "STFATT", "Staff Attorney"),
        (100000017, "SECNDE", "Secondee"),
        (100000018, "LGLINT", "Legal Intern"),
        (100000004, "PARALG", "Paralegal"),
        (100000019, "LGLAST", "Legal Assistant"),
        (100000020, "CLKSEC", "Clerk or Secretary"),
        (100000006, "NBOTHR", "Other"),
        (100000021, "LIBRRN", "Librarian"),
        (100000022, "RSRCHR", "Research Specialist/Researcher"),
        (100000023, "ANALST", "Analyst"),
        (100000024, "HLNLPF", "High-Level Non-Lawyer Professional"),
        (100000025, "LGPRMG", "Legal Project Manager"),
        (100000026, "IPAGNT", "IP Agent"),
        (100000027, "CSTCNS", "Costs Counsel"),
        (100000028, "TEMPAT", "Contract Attorney"),
        (100000029, "TEMPOS", "Contract Other Staff"),
    };

    internal static bool TryParse(string? text, out int value)
    {
        value = 0;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            return false;
        }

        foreach (var (v, code, label) in All)
        {
            if (string.Equals(t, code, StringComparison.OrdinalIgnoreCase) || string.Equals(t, label, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }

        return false;
    }

    internal static string LabelOf(int value) => All.FirstOrDefault(c => c.Value == value).Label ?? value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The rate card import workbook (task 078): ONE source for its column set, used to generate the downloadable template and to read an
/// uploaded file. No binary template is committed; no package beyond the already-referenced DocumentFormat.OpenXml (NFR-12).
/// </summary>
/// <remarks>
/// Layout: sheet <c>Timekeepers</c> and sheet <c>Rates</c>, header in row 1, one data row per line from row 2, columns found BY HEADER NAME
/// (any order, extra columns ignored). A third sheet <c>Instructions</c> is for people and is never read. A row whose first filled cell
/// starts with <see cref="ExampleMarker"/> is the template's example row and is skipped. Dates are ISO text (<c>2026-01-31</c>) or real
/// Excel date cells; amounts are numbers or text.
/// </remarks>
internal static class RateCardWorkbook
{
    internal const string TimekeepersSheet = "Timekeepers";
    internal const string RatesSheet = "Rates";
    internal const string InstructionsSheet = "Instructions";
    internal const string ExampleMarker = "(example)";

    /// <summary>Largest number of data rows read from either sheet; more is refused rather than half-read.</summary>
    internal const int MaxRowsPerSheet = 5000;

    /// <summary>Largest total unpacked size (by the zip's declared entry sizes) a workbook may have; a few thousand rows are far below it.</summary>
    internal const long MaxUnpackedBytes = 64L * 1024 * 1024;

    internal const string TkName = "Name";
    internal const string TkClassification = "Classification";
    internal const string TkEffectiveFrom = "Effective From";
    internal const string TkEffectiveTo = "Effective To";
    internal const string TkContactEmail = "Contact Email";

    internal const string RateTimekeeper = "Timekeeper";
    internal const string RateClassification = "Classification";
    internal const string RateApproved = "Approved Rate";
    internal const string RateRack = "Rack Rate";
    internal const string RateCurrency = "Currency";
    internal const string RateEffectiveFrom = "Effective From";
    internal const string RateEffectiveTo = "Effective To";

    internal static readonly string[] TimekeeperColumns = { TkName, TkClassification, TkEffectiveFrom, TkEffectiveTo, TkContactEmail };

    internal static readonly string[] RateColumns =
    {
        RateTimekeeper, RateClassification, RateApproved, RateRack, RateCurrency, RateEffectiveFrom, RateEffectiveTo,
    };

    /// <summary>The downloadable template: both sheets with a header row and one example row, plus the instructions sheet.</summary>
    internal static byte[] BuildTemplate() => Build(
        new[] { new object?[] { ExampleMarker + " Jane Doe", "Partner", "2026-01-01", "2026-12-31", "jane.doe@examplefirm.com" } },
        new[] { new object?[] { ExampleMarker + " Jane Doe", null, "850.00", "1050.00", "USD", "2026-01-01", "2026-12-31" } },
        includeInstructions: true);

    /// <summary>
    /// Builds a workbook from raw row values (row 1 is the generated header). A <see cref="string"/> is written as text; a number as a
    /// number; a <see cref="DateTime"/> as an Excel date serial; null as an empty cell.
    /// </summary>
    internal static byte[] Build(
        IReadOnlyList<object?[]> timekeeperRows, IReadOnlyList<object?[]> rateRows, bool includeInstructions = false)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook(new Sheets());
            var sheets = workbookPart.Workbook.GetFirstChild<Sheets>()!;
            uint sheetId = 1;

            AddSheet(workbookPart, sheets, sheetId++, TimekeepersSheet, TimekeeperColumns, timekeeperRows);
            AddSheet(workbookPart, sheets, sheetId++, RatesSheet, RateColumns, rateRows);
            if (includeInstructions)
            {
                AddSheet(workbookPart, sheets, sheetId, InstructionsSheet, new[] { "Rate card import" }, InstructionLines.Select(l => new object?[] { l }).ToList());
            }

            workbookPart.Workbook.Save();
        }

        return ms.ToArray();
    }

    private static readonly string[] InstructionLines =
    {
        "Fill the Timekeepers and Rates sheets, one row per line, starting at row 2. Keep the header row.",
        "Delete the example rows (they start with \"(example)\"; they are ignored if left in).",
        "Dates: yyyy-mm-dd (an Excel date cell also works). Effective To may be left empty for an open-ended range.",
        "Classification: a LEDES classification name or code, for example Partner or PARTNR.",
        "Rates: fill Timekeeper (a name from the Timekeepers sheet or the firm's existing roster) OR Classification, not both.",
        "Every timekeeper on the Timekeepers sheet needs at least one rate row. Approved Rate may not exceed Rack Rate.",
        "Currency: an ISO code, for example USD. Ranges for the same timekeeper or classification may not overlap.",
        "Importing creates a Draft rate card and Pending timekeepers. Nothing is in force until the card is published.",
    };

    private static void AddSheet(
        WorkbookPart workbookPart, Sheets sheets, uint sheetId, string name, string[] header, IReadOnlyList<object?[]> rows)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        part.Worksheet = new Worksheet(data);

        data.Append(BuildRow(1, header.Cast<object?>().ToArray()));
        var rowIndex = 2u;
        foreach (var values in rows)
        {
            data.Append(BuildRow(rowIndex++, values));
        }

        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = sheetId, Name = name });
    }

    private static Row BuildRow(uint rowIndex, object?[] values)
    {
        var row = new Row { RowIndex = rowIndex };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value is null)
            {
                continue;
            }

            var reference = ColumnLetters(i + 1) + rowIndex.ToString(CultureInfo.InvariantCulture);
            row.Append(value switch
            {
                string s => new Cell { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(s)) },
                DateTime d => NumberCell(reference, d.ToOADate()),
                double n => NumberCell(reference, n),
                decimal m => NumberCell(reference, (double)m),
                int n => NumberCell(reference, n),
                _ => new Cell { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)) },
            });
        }

        return row;
    }

    private static Cell NumberCell(string reference, double value) => new()
    {
        CellReference = reference,
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture)),
    };

    private static string ColumnLetters(int index)
    {
        var letters = string.Empty;
        while (index > 0)
        {
            index--;
            letters = (char)('A' + (index % 26)) + letters;
            index /= 26;
        }

        return letters;
    }

    private static int ColumnIndex(string? cellReference)
    {
        var index = 0;
        foreach (var ch in cellReference ?? string.Empty)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            index = (index * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return index;
    }

    /// <summary>
    /// Reads both sheets. A file that is not a workbook, a missing sheet or header, or too many rows yields <see cref="WorkbookRead.Errors"/>
    /// and no rows; cell-level validation is the caller's.
    /// </summary>
    internal static WorkbookRead Read(Stream stream)
    {
        var errors = new List<RateCardImportError>();
        var notAWorkbook = new RateCardImportError(null, null, null, "The file is not a valid .xlsx workbook.");

        if (IsTooLargeUnpacked(stream))
        {
            errors.Add(new RateCardImportError(null, null, null, "The workbook is too large when unpacked."));
            return new WorkbookRead(Array.Empty<WorkbookRow>(), Array.Empty<WorkbookRow>(), errors);
        }

        try
        {
            using var doc = SpreadsheetDocument.Open(stream, false);
            var workbookPart = doc.WorkbookPart;
            if (workbookPart?.Workbook?.Sheets is null)
            {
                errors.Add(notAWorkbook);
                return new WorkbookRead(Array.Empty<WorkbookRow>(), Array.Empty<WorkbookRow>(), errors);
            }

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?
                .Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? new List<string>();

            var timekeepers = ReadSheet(workbookPart, sharedStrings, TimekeepersSheet, TimekeeperColumns, errors);
            var rates = ReadSheet(workbookPart, sharedStrings, RatesSheet, RateColumns, errors);
            return errors.Count > 0
                ? new WorkbookRead(Array.Empty<WorkbookRow>(), Array.Empty<WorkbookRow>(), errors)
                : new WorkbookRead(timekeepers, rates, errors);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or XmlException or InvalidDataException or FileFormatException
                                       or IOException or ArgumentException or InvalidOperationException)
        {
            // A zip that opens but whose parts are not valid workbook XML is the caller's bad file (400), not a server fault (500).
            return new WorkbookRead(Array.Empty<WorkbookRow>(), Array.Empty<WorkbookRow>(), new List<RateCardImportError> { notAWorkbook });
        }
    }

    private static bool IsTooLargeUnpacked(Stream stream)
    {
        var start = stream.Position;
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return zip.Entries.Sum(e => e.Length) > MaxUnpackedBytes;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return false; // not a zip: SpreadsheetDocument.Open reports it
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static List<WorkbookRow> ReadSheet(
        WorkbookPart workbookPart, List<string> sharedStrings, string sheetName, string[] columns, List<RateCardImportError> errors)
    {
        var result = new List<WorkbookRow>();
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .FirstOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        var relationshipId = sheet?.Id?.Value;
        if (relationshipId is null || workbookPart.GetPartById(relationshipId) is not WorksheetPart part)
        {
            errors.Add(new RateCardImportError(sheetName, null, null, $"The workbook has no sheet named '{sheetName}'."));
            return result;
        }

        var rows = (part.Worksheet?.Descendants<Row>() ?? Enumerable.Empty<Row>()).OrderBy(r => r.RowIndex?.Value ?? 0u).ToList();
        var headerRow = rows.FirstOrDefault(r => (r.RowIndex?.Value ?? 0u) == 1u);
        var headerByIndex = new Dictionary<int, string>();
        if (headerRow is not null)
        {
            foreach (var cell in headerRow.Elements<Cell>())
            {
                var text = CellText(cell, sharedStrings)?.Trim();
                var wanted = columns.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
                if (wanted is not null)
                {
                    headerByIndex[ColumnIndex(cell.CellReference?.Value)] = wanted;
                }
            }
        }

        var missing = columns.Where(c => !headerByIndex.ContainsValue(c)).ToList();
        foreach (var column in missing)
        {
            errors.Add(new RateCardImportError(sheetName, 1, column, $"Column '{column}' is missing from the header row."));
        }

        if (missing.Count > 0)
        {
            return result;
        }

        foreach (var row in rows)
        {
            var rowNumber = (int)(row.RowIndex?.Value ?? 0u);
            if (rowNumber <= 1)
            {
                continue;
            }

            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            string? firstFilled = null;
            foreach (var cell in row.Elements<Cell>().OrderBy(c => ColumnIndex(c.CellReference?.Value)))
            {
                if (!headerByIndex.TryGetValue(ColumnIndex(cell.CellReference?.Value), out var column))
                {
                    continue;
                }

                var text = CellText(cell, sharedStrings)?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                cells[column] = text;
                firstFilled ??= text;
            }

            if (cells.Count == 0 || (firstFilled is not null && firstFilled.StartsWith(ExampleMarker, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (result.Count >= MaxRowsPerSheet)
            {
                errors.Add(new RateCardImportError(sheetName, rowNumber, null, $"The sheet has more than {MaxRowsPerSheet} rows; split the import."));
                return new List<WorkbookRow>();
            }

            result.Add(new WorkbookRow(sheetName, rowNumber, cells));
        }

        return result;
    }

    private static string? CellText(Cell cell, List<string> sharedStrings)
    {
        var type = cell.DataType?.Value;
        if (type is { } t && t.Equals(CellValues.InlineString))
        {
            return cell.InlineString?.InnerText;
        }

        var raw = cell.CellValue?.Text;
        if (raw is null)
        {
            return null;
        }

        if (type is { } s && s.Equals(CellValues.SharedString))
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < sharedStrings.Count
                ? sharedStrings[i]
                : null;
        }

        if (type is { } b && b.Equals(CellValues.Boolean))
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        return raw;
    }
}
