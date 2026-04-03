using System.Net;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Export;

/// <summary>
/// DOCX export service using OpenXML SDK.
/// Implements ADR-001 (BFF pattern) - runs in-process, no Azure Functions.
/// Handles large documents (100+ pages) efficiently via streaming generation.
/// </summary>
public partial class DocxExportService : IExportService
{
    private readonly ILogger<DocxExportService> _logger;
    private readonly AnalysisOptions _options;

    // OpenXML constants
    private const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // Style IDs for consistent formatting
    private const string TitleStyleId = "Title";
    private const string Heading1StyleId = "Heading1";
    private const string Heading2StyleId = "Heading2";
    private const string Heading3StyleId = "Heading3";
    private const string NormalStyleId = "Normal";
    private const string TableStyleId = "TableGrid";

    public DocxExportService(
        ILogger<DocxExportService> logger,
        IOptions<AnalysisOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Docx;

    /// <inheritdoc />
    public async Task<ExportFileResult> ExportAsync(ExportContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Exporting analysis {AnalysisId} to DOCX", context.AnalysisId);

        try
        {
            using var stream = new MemoryStream();

            // Create document
            using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
            {
                // Add main document part
                var mainPart = document.AddMainDocumentPart();
                mainPart.Document = new Document();
                var body = mainPart.Document.AppendChild(new Body());

                // Add styles
                AddStyleDefinitions(mainPart);

                // Add document properties
                AddDocumentProperties(document, context);

                // Generate content sections
                AddTitleSection(body, context);
                AddMetadataSection(body, context);

                if (!string.IsNullOrWhiteSpace(context.Summary))
                {
                    AddSummarySection(body, context.Summary);
                }

                AddMainContent(body, context.Content);

                if (context.Entities != null && HasEntities(context.Entities))
                {
                    AddEntitiesSection(body, context.Entities);
                }

                if (context.Clauses?.Clauses.Count > 0)
                {
                    AddClausesSection(body, context.Clauses);
                }

                AddFooter(body, context);

                // Save and close
                mainPart.Document.Save();
            }

            // Get bytes
            var bytes = stream.ToArray();
            var fileName = GenerateFileName(context);

            _logger.LogInformation(
                "DOCX export complete for {AnalysisId}: {Size} bytes",
                context.AnalysisId, bytes.Length);

            return await Task.FromResult(ExportFileResult.Ok(bytes, ContentType, fileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export analysis {AnalysisId} to DOCX", context.AnalysisId);
            return ExportFileResult.Fail($"DOCX export failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public ExportValidationResult Validate(ExportContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Content))
        {
            errors.Add("Analysis content is required for export");
        }

        if (string.IsNullOrWhiteSpace(context.Title))
        {
            errors.Add("Analysis title is required for export");
        }

        return errors.Count > 0
            ? ExportValidationResult.Invalid([.. errors])
            : ExportValidationResult.Valid();
    }

    #region Document Structure

    private static void AddStyleDefinitions(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        // Title style
        styles.Append(CreateStyle(TitleStyleId, "Title", new RunProperties(
            new Bold(),
            new FontSize { Val = "56" }, // 28pt
            new Color { Val = "2E74B5" })));

        // Heading 1
        styles.Append(CreateStyle(Heading1StyleId, "Heading 1", new RunProperties(
            new Bold(),
            new FontSize { Val = "36" }, // 18pt
            new Color { Val = "2E74B5" })));

        // Heading 2
        styles.Append(CreateStyle(Heading2StyleId, "Heading 2", new RunProperties(
            new Bold(),
            new FontSize { Val = "28" }, // 14pt
            new Color { Val = "404040" })));

        // Heading 3
        styles.Append(CreateStyle(Heading3StyleId, "Heading 3", new RunProperties(
            new Bold(),
            new FontSize { Val = "24" }, // 12pt
            new Color { Val = "404040" })));

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static Style CreateStyle(string styleId, string styleName, RunProperties runProps)
    {
        return new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = styleId,
            StyleName = new StyleName { Val = styleName },
            StyleRunProperties = new StyleRunProperties(runProps.CloneNode(true))
        };
    }

    private static void AddDocumentProperties(WordprocessingDocument document, ExportContext context)
    {
        var props = document.AddCoreFilePropertiesPart();
        using var writer = new StreamWriter(props.GetStream());
        writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties""
                   xmlns:dc=""http://purl.org/dc/elements/1.1/""
                   xmlns:dcterms=""http://purl.org/dc/terms/"">
    <dc:title>{EscapeXml(context.Title)}</dc:title>
    <dc:creator>{EscapeXml(context.CreatedBy ?? "Spaarke AI")}</dc:creator>
    <dc:description>AI-generated analysis export</dc:description>
    <dcterms:created>{context.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>
</cp:coreProperties>");
    }

    #endregion

    #region Content Sections

    private static void AddTitleSection(Body body, ExportContext context)
    {
        // Main title
        var titlePara = CreateParagraph(context.Title, TitleStyleId);
        body.Append(titlePara);

        // Subtitle with source document
        if (!string.IsNullOrWhiteSpace(context.SourceDocumentName))
        {
            var subtitlePara = CreateParagraph($"Analysis of: {context.SourceDocumentName}", NormalStyleId);
            subtitlePara.ParagraphProperties = new ParagraphProperties(
                new SpacingBetweenLines { After = "400" });
            body.Append(subtitlePara);
        }

        // Horizontal line
        body.Append(CreateHorizontalRule());
    }

    private static void AddMetadataSection(Body body, ExportContext context)
    {
        var table = new Table();

        // Table properties - no borders, compact
        var tableProps = new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None }));
        table.Append(tableProps);

        // Metadata rows
        table.Append(CreateMetadataRow("Created:", context.CreatedAt.ToString("MMMM d, yyyy 'at' h:mm tt")));
        table.Append(CreateMetadataRow("Created By:", context.CreatedBy ?? "—"));

        if (context.SourceDocumentId.HasValue)
        {
            table.Append(CreateMetadataRow("Document ID:", context.SourceDocumentId.Value.ToString()));
        }

        body.Append(table);
        body.Append(new Paragraph()); // Spacer
    }

    private static void AddSummarySection(Body body, string summary)
    {
        body.Append(CreateParagraph("Executive Summary", Heading1StyleId));

        // Summary in a shaded box
        var summaryPara = CreateParagraph(summary, NormalStyleId);
        summaryPara.ParagraphProperties = new ParagraphProperties(
            new Shading { Val = ShadingPatternValues.Clear, Fill = "F5F5F5" },
            new ParagraphBorders(
                new LeftBorder { Val = BorderValues.Single, Size = 24, Color = "2E74B5" }),
            new Indentation { Left = "400", Right = "400" },
            new SpacingBetweenLines { Before = "200", After = "200" });

        body.Append(summaryPara);
        body.Append(new Paragraph()); // Spacer
    }

    private static void AddMainContent(Body body, string content)
    {
        body.Append(CreateParagraph("Analysis Details", Heading1StyleId));

        // Parse content (HTML/Markdown) into paragraphs
        var paragraphs = ParseContentToParagraphs(content);

        foreach (var para in paragraphs)
        {
            body.Append(para);
        }

        body.Append(new Paragraph()); // Spacer
    }

    private static void AddEntitiesSection(Body body, AnalysisEntities entities)
    {
        body.Append(CreateParagraph("Extracted Entities", Heading1StyleId));

        // Create entities table
        var table = CreateStyledTable();

        // Header row
        table.Append(CreateTableRow(
            CreateTableCell("Category", true),
            CreateTableCell("Values", true)));

        // Data rows
        if (entities.Organizations.Count > 0)
        {
            table.Append(CreateTableRow(
                CreateTableCell("Organizations"),
                CreateTableCell(string.Join(", ", entities.Organizations))));
        }

        if (entities.People.Count > 0)
        {
            table.Append(CreateTableRow(
                CreateTableCell("People"),
                CreateTableCell(string.Join(", ", entities.People))));
        }

        if (entities.Dates.Count > 0)
        {
            table.Append(CreateTableRow(
                CreateTableCell("Dates"),
                CreateTableCell(string.Join(", ", entities.Dates))));
        }

        if (entities.Amounts.Count > 0)
        {
            table.Append(CreateTableRow(
                CreateTableCell("Amounts"),
                CreateTableCell(string.Join(", ", entities.Amounts))));
        }

        if (entities.References.Count > 0)
        {
            table.Append(CreateTableRow(
                CreateTableCell("References"),
                CreateTableCell(string.Join(", ", entities.References))));
        }

        body.Append(table);
        body.Append(new Paragraph()); // Spacer
    }

    private static void AddClausesSection(Body body, AnalysisClauses clauses)
    {
        body.Append(CreateParagraph("Contract Clause Analysis", Heading1StyleId));

        // Create clauses table
        var table = CreateStyledTable();

        // Header row
        table.Append(CreateTableRow(
            CreateTableCell("Clause Type", true),
            CreateTableCell("Description", true),
            CreateTableCell("Risk Level", true)));

        // Data rows
        foreach (var clause in clauses.Clauses)
        {
            table.Append(CreateTableRow(
                CreateTableCell(clause.Type),
                CreateTableCell(clause.Description ?? "—"),
                CreateRiskLevelCell(clause.RiskLevel)));
        }

        body.Append(table);
        body.Append(new Paragraph()); // Spacer
    }

    private static void AddFooter(Body body, ExportContext context)
    {
        body.Append(CreateHorizontalRule());

        var footerPara = new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { Before = "200" }),
            new Run(
                new RunProperties(
                    new FontSize { Val = "18" },
                    new Color { Val = "808080" }),
                new Text($"Generated by Spaarke AI • {DateTime.UtcNow:MMMM d, yyyy}")));

        body.Append(footerPara);
    }

    #endregion

    #region Helper Methods

    private static Paragraph CreateParagraph(string text, string styleId)
    {
        var para = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
            new Run(new Text(SanitizeText(text)) { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    private static Paragraph CreateHorizontalRule()
    {
        return new Paragraph(
            new ParagraphProperties(
                new ParagraphBorders(
                    new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "CCCCCC" }),
                new SpacingBetweenLines { After = "200" }));
    }

    private static TableRow CreateMetadataRow(string label, string value)
    {
        var row = new TableRow();

        var labelCell = new TableCell(
            new TableCellProperties(new TableCellWidth { Width = "2000", Type = TableWidthUnitValues.Dxa }),
            new Paragraph(new Run(
                new RunProperties(new Bold(), new FontSize { Val = "20" }),
                new Text(label))));

        var valueCell = new TableCell(
            new Paragraph(new Run(
                new RunProperties(new FontSize { Val = "20" }),
                new Text(value))));

        row.Append(labelCell, valueCell);
        return row;
    }

    private static Table CreateStyledTable()
    {
        var table = new Table();

        var tableProps = new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" }),
            new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = true, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true });

        table.Append(tableProps);
        return table;
    }

    private static TableRow CreateTableRow(params TableCell[] cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
        {
            row.Append(cell);
        }
        return row;
    }

    private static TableCell CreateTableCell(string text, bool isHeader = false)
    {
        var cellProps = new TableCellProperties(
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        if (isHeader)
        {
            cellProps.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "2E74B5" });
        }

        var run = new Run(new Text(text));
        if (isHeader)
        {
            run.RunProperties = new RunProperties(
                new Bold(),
                new Color { Val = "FFFFFF" });
        }

        return new TableCell(
            cellProps,
            new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "40", After = "40" }),
                run));
    }

    private static TableCell CreateRiskLevelCell(string? riskLevel)
    {
        var color = riskLevel?.ToUpperInvariant() switch
        {
            "HIGH" => "C00000",
            "MEDIUM" => "ED7D31",
            "LOW" => "70AD47",
            _ => "808080"
        };

        var cellProps = new TableCellProperties(
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        return new TableCell(
            cellProps,
            new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "40", After = "40" }),
                new Run(
                    new RunProperties(new Bold(), new Color { Val = color }),
                    new Text(riskLevel ?? "—"))));
    }

    private static List<Paragraph> ParseContentToParagraphs(string content)
    {
        var paragraphs = new List<Paragraph>();

        // Strip HTML tags, then decode entities and sanitize for XML
        var plainText = SanitizeText(HtmlTagRegex().Replace(content, " "));

        // Split into paragraphs
        var lines = plainText.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Check for heading patterns
            if (trimmed.StartsWith("## "))
            {
                paragraphs.Add(CreateParagraph(trimmed[3..], Heading2StyleId));
            }
            else if (trimmed.StartsWith("# "))
            {
                paragraphs.Add(CreateParagraph(trimmed[2..], Heading1StyleId));
            }
            else
            {
                paragraphs.Add(CreateParagraph(trimmed, NormalStyleId));
            }
        }

        return paragraphs;
    }

    private static bool HasEntities(AnalysisEntities entities)
    {
        return entities.Organizations.Count > 0 ||
               entities.People.Count > 0 ||
               entities.Dates.Count > 0 ||
               entities.Amounts.Count > 0 ||
               entities.References.Count > 0;
    }

    private static string GenerateFileName(ExportContext context)
    {
        var safeName = InvalidFileNameCharsRegex().Replace(context.Title, "_");
        if (safeName.Length > 50)
        {
            safeName = safeName[..50];
        }
        return $"{safeName}_{context.CreatedAt:yyyyMMdd}.docx";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// Sanitizes a string for safe embedding in OpenXML content:
    /// 1. Decodes HTML entities (e.g. &amp;amp; → &amp;, &amp;nbsp; → space)
    /// 2. Removes XML-illegal control characters (U+0000–U+0008, U+000B–U+000C, U+000E–U+001F)
    ///    which cause "unreadable content" warnings when Word opens the file.
    /// Tabs (U+0009), LF (U+000A), and CR (U+000D) are preserved as they are valid in XML.
    /// </summary>
    private static string SanitizeText(string value)
    {
        // Decode HTML entities first (&amp; → &, &nbsp; → space, etc.)
        var decoded = WebUtility.HtmlDecode(value) ?? value;
        // Strip XML-illegal control characters
        return XmlInvalidCharRegex().Replace(decoded, string.Empty);
    }

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex XmlInvalidCharRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[<>:""/\\|?*]")]
    private static partial Regex InvalidFileNameCharsRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldMarkdownRegex();

    #endregion

    #region Markdown-to-DOCX Export (FR-15: Open in Word)

    /// <summary>
    /// Generates a DOCX document from markdown content for the "Open in Word" feature (FR-15).
    /// Handles common AI-generated markdown patterns: headings (#/##/###), bold (**text**),
    /// bullet lists (- item), numbered lists (1. item), and plain paragraphs.
    /// </summary>
    /// <param name="markdown">Markdown content to convert (typically AI-generated analysis output).</param>
    /// <param name="title">Document title for the title page and metadata.</param>
    /// <param name="metadata">Optional metadata dictionary (e.g. matterName, analysisDate) for document properties.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Byte array containing the generated .docx file.</returns>
    public Task<byte[]> GenerateFromMarkdownAsync(
        string markdown,
        string title,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating DOCX from markdown: Title={Title}, ContentLength={Length}",
            title, markdown.Length);

        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Add styles (reuse existing style definitions)
            AddStyleDefinitions(mainPart);

            // Add document properties
            var props = document.AddCoreFilePropertiesPart();
            using (var writer = new StreamWriter(props.GetStream()))
            {
                var creator = metadata?.GetValueOrDefault("createdBy") ?? "Spaarke AI";
                writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties""
                   xmlns:dc=""http://purl.org/dc/elements/1.1/""
                   xmlns:dcterms=""http://purl.org/dc/terms/"">
    <dc:title>{EscapeXml(title)}</dc:title>
    <dc:creator>{EscapeXml(creator)}</dc:creator>
    <dc:description>AI-generated analysis — exported from SprkChat</dc:description>
    <dcterms:created>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>
</cp:coreProperties>");
            }

            // Title
            body.Append(CreateParagraph(title, TitleStyleId));

            // Metadata subtitle (matter name, date)
            if (metadata is not null)
            {
                var metadataLine = string.Join(" | ", metadata
                    .Where(kv => !string.Equals(kv.Key, "createdBy", StringComparison.OrdinalIgnoreCase))
                    .Select(kv => $"{kv.Key}: {kv.Value}"));
                if (!string.IsNullOrWhiteSpace(metadataLine))
                {
                    var subtitlePara = CreateParagraph(metadataLine, NormalStyleId);
                    subtitlePara.ParagraphProperties = new ParagraphProperties(
                        new SpacingBetweenLines { After = "200" });
                    body.Append(subtitlePara);
                }
            }

            body.Append(CreateHorizontalRule());

            // Parse and append markdown content
            var paragraphs = ParseMarkdownToParagraphs(markdown);
            foreach (var para in paragraphs)
            {
                body.Append(para);
            }

            // Footer
            body.Append(CreateHorizontalRule());
            var footerPara = new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = "200" }),
                new Run(
                    new RunProperties(
                        new FontSize { Val = "18" },
                        new Color { Val = "808080" }),
                    new Text($"Generated by Spaarke AI \u2022 {DateTime.UtcNow:MMMM d, yyyy}")));
            body.Append(footerPara);

            mainPart.Document.Save();
        }

        var bytes = stream.ToArray();

        _logger.LogInformation("DOCX generated from markdown: Title={Title}, Size={Size} bytes",
            title, bytes.Length);

        return Task.FromResult(bytes);
    }

    /// <summary>
    /// Parses markdown text into OpenXML paragraphs.
    /// Handles: # Heading1, ## Heading2, ### Heading3, **bold**, - bullets, 1. numbered lists,
    /// and plain paragraphs. Designed for AI-generated analysis output patterns.
    /// </summary>
    private static List<OpenXmlElement> ParseMarkdownToParagraphs(string markdown)
    {
        var elements = new List<OpenXmlElement>();
        var lines = markdown.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Skip empty lines (they just add spacing)
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();

            // Headings (### before ## before # to match correctly)
            if (trimmed.StartsWith("### "))
            {
                elements.Add(CreateParagraph(trimmed[4..], Heading3StyleId));
            }
            else if (trimmed.StartsWith("## "))
            {
                elements.Add(CreateParagraph(trimmed[3..], Heading2StyleId));
            }
            else if (trimmed.StartsWith("# "))
            {
                elements.Add(CreateParagraph(trimmed[2..], Heading1StyleId));
            }
            // Bullet list items
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var bulletText = trimmed[2..];
                elements.Add(CreateBulletParagraph(bulletText));
            }
            // Numbered list items
            else if (NumberedListRegex().IsMatch(trimmed))
            {
                var match = NumberedListRegex().Match(trimmed);
                var listText = match.Groups[1].Value;
                elements.Add(CreateNumberedParagraph(listText, match.Value[..match.Value.IndexOf('.')]));
            }
            // Normal paragraph (may contain **bold** inline formatting)
            else
            {
                elements.Add(CreateFormattedParagraph(trimmed, NormalStyleId));
            }
        }

        return elements;
    }

    /// <summary>
    /// Creates a paragraph with inline bold formatting support (**text**).
    /// </summary>
    private static Paragraph CreateFormattedParagraph(string text, string styleId)
    {
        var para = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = styleId }));

        // Split text by **bold** markers and create runs.
        // Regex.Split with a capturing group interleaves captured groups at odd indices.
        var parts = BoldMarkdownRegex().Split(text);

        for (var i = 0; i < parts.Length; i++)
        {
            if (!string.IsNullOrEmpty(parts[i]))
            {
                // Check if this part is a bold match (captured group)
                // Regex.Split interleaves captured groups between splits
                if (i % 2 == 1)
                {
                    // Odd indices are captured bold groups
                    var boldRun = new Run(
                        new RunProperties(new Bold()),
                        new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
                    para.Append(boldRun);
                }
                else
                {
                    var normalRun = new Run(
                        new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
                    para.Append(normalRun);
                }
            }
        }

        return para;
    }

    /// <summary>
    /// Creates a bullet list paragraph with a Unicode bullet character prefix.
    /// </summary>
    private static Paragraph CreateBulletParagraph(string text)
    {
        var para = new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = NormalStyleId },
                new Indentation { Left = "720", Hanging = "360" },
                new SpacingBetweenLines { Before = "40", After = "40" }));

        // Add bullet character
        para.Append(new Run(
            new Text("\u2022  ") { Space = SpaceProcessingModeValues.Preserve }));

        // Add formatted text (may contain bold)
        AppendFormattedRuns(para, text);

        return para;
    }

    /// <summary>
    /// Creates a numbered list paragraph with the number prefix.
    /// </summary>
    private static Paragraph CreateNumberedParagraph(string text, string number)
    {
        var para = new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = NormalStyleId },
                new Indentation { Left = "720", Hanging = "360" },
                new SpacingBetweenLines { Before = "40", After = "40" }));

        // Add number prefix
        para.Append(new Run(
            new Text($"{number}.  ") { Space = SpaceProcessingModeValues.Preserve }));

        // Add formatted text (may contain bold)
        AppendFormattedRuns(para, text);

        return para;
    }

    /// <summary>
    /// Appends runs to a paragraph, handling **bold** inline formatting.
    /// </summary>
    private static void AppendFormattedRuns(Paragraph para, string text)
    {
        var parts = BoldMarkdownRegex().Split(text);

        for (var i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;

            if (i % 2 == 1)
            {
                para.Append(new Run(
                    new RunProperties(new Bold()),
                    new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve }));
            }
            else
            {
                para.Append(new Run(
                    new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve }));
            }
        }
    }

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex NumberedListRegex();

    #endregion
}
