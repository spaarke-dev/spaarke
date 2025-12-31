using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Sprk.Bff.Api.Services.Ai.Export;

/// <summary>
/// QuestPDF document composer for analysis exports.
/// Generates professionally styled PDF documents with branding support.
/// Runs in-process following ADR-001 (no Azure Functions for core processing).
/// </summary>
public class AnalysisPdfDocument : IDocument
{
    private readonly ExportContext _context;
    private readonly string _primaryColor;
    private readonly string _lightColor;

    // Color constants
    private const string DefaultPrimaryColor = "#0078D4";
    private const string HighRiskColor = "#D13438";
    private const string MediumRiskColor = "#FF8C00";
    private const string LowRiskColor = "#107C10";

    public AnalysisPdfDocument(ExportContext context)
    {
        _context = context;
        _primaryColor = DefaultPrimaryColor;
        _lightColor = LightenColor(_primaryColor);
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public DocumentSettings GetSettings() => new()
    {
        CompressDocument = true,
        ImageCompressionQuality = ImageCompressionQuality.Medium
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            // Title bar with branding
            column.Item()
                .Background(Color.FromHex(_primaryColor))
                .Padding(15)
                .Row(row =>
                {
                    row.RelativeItem().Column(titleColumn =>
                    {
                        titleColumn.Item()
                            .Text(_context.Title)
                            .FontSize(20)
                            .Bold()
                            .FontColor(Colors.White);

                        if (!string.IsNullOrEmpty(_context.SourceDocumentName))
                        {
                            titleColumn.Item()
                                .Text($"Source: {_context.SourceDocumentName}")
                                .FontSize(10)
                                .FontColor(Colors.White)
                                .Italic();
                        }
                    });
                });

            // Metadata bar
            column.Item()
                .Background(Color.FromHex(_lightColor))
                .Padding(10)
                .Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Generated: ").SemiBold();
                        text.Span(DateTime.UtcNow.ToString("MMMM d, yyyy"));
                    });

                    if (!string.IsNullOrEmpty(_context.CreatedBy))
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Author: ").SemiBold();
                            text.Span(_context.CreatedBy);
                        });
                    }

                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Analysis ID: ").SemiBold();
                        text.Span(_context.AnalysisId.ToString()[..8] + "...");
                    });
                });

            column.Item().Height(15);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            // Executive Summary
            if (!string.IsNullOrWhiteSpace(_context.Summary))
            {
                column.Item().Element(c => ComposeSummarySection(c, _context.Summary));
            }

            // Main Content
            column.Item().Element(c => ComposeMainContentSection(c, _context.Content));

            // Entities Table
            if (_context.Entities != null && HasEntities(_context.Entities))
            {
                column.Item().Element(c => ComposeEntitiesSection(c, _context.Entities));
            }

            // Clauses Table
            if (_context.Clauses?.Clauses.Count > 0)
            {
                column.Item().Element(c => ComposeClausesSection(c, _context.Clauses));
            }
        });
    }

    private void ComposeSummarySection(IContainer container, string summary)
    {
        container.Column(column =>
        {
            column.Item().Text("Executive Summary")
                .FontSize(14)
                .Bold()
                .FontColor(Color.FromHex(_primaryColor));

            column.Item().Height(8);

            column.Item()
                .Background(Color.FromHex("#F5F5F5"))
                .Padding(12)
                .Text(summary)
                .FontSize(11)
                .LineHeight(1.4f);

            column.Item().Height(20);
        });
    }

    private void ComposeMainContentSection(IContainer container, string content)
    {
        container.Column(column =>
        {
            column.Item().Text("Analysis Content")
                .FontSize(14)
                .Bold()
                .FontColor(Color.FromHex(_primaryColor));

            column.Item().Height(8);

            // Strip HTML tags if present
            var cleanContent = StripHtmlTags(content);

            // Split into paragraphs
            var paragraphs = cleanContent.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

            foreach (var paragraph in paragraphs)
            {
                if (!string.IsNullOrWhiteSpace(paragraph))
                {
                    column.Item()
                        .Text(paragraph.Trim())
                        .FontSize(11)
                        .LineHeight(1.5f);

                    column.Item().Height(8);
                }
            }

            column.Item().Height(12);
        });
    }

    private void ComposeEntitiesSection(IContainer container, AnalysisEntities entities)
    {
        container.Column(column =>
        {
            column.Item().Text("Extracted Entities")
                .FontSize(14)
                .Bold()
                .FontColor(Color.FromHex(_primaryColor));

            column.Item().Height(8);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(3);
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Background(Color.FromHex(_primaryColor)).Padding(8)
                        .Text("Type").Bold().FontColor(Colors.White);
                    header.Cell().Background(Color.FromHex(_primaryColor)).Padding(8)
                        .Text("Values").Bold().FontColor(Colors.White);
                });

                // Entity rows
                AddEntityRow(table, "Organizations", entities.Organizations);
                AddEntityRow(table, "People", entities.People);
                AddEntityRow(table, "Dates", entities.Dates);
                AddEntityRow(table, "Amounts", entities.Amounts);
                AddEntityRow(table, "References", entities.References);
            });

            column.Item().Height(20);
        });
    }

    private static void AddEntityRow(TableDescriptor table, string type, List<string> values)
    {
        if (values.Count == 0) return;

        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(8)
            .Text(type).SemiBold();
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(8)
            .Text(string.Join(", ", values));
    }

    private void ComposeClausesSection(IContainer container, AnalysisClauses clauses)
    {
        container.Column(column =>
        {
            column.Item().Text("Contract Clause Analysis")
                .FontSize(14)
                .Bold()
                .FontColor(Color.FromHex(_primaryColor));

            column.Item().Height(8);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(80);
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Background(Color.FromHex(_primaryColor)).Padding(8)
                        .Text("Clause Type").Bold().FontColor(Colors.White);
                    header.Cell().Background(Color.FromHex(_primaryColor)).Padding(8)
                        .Text("Description").Bold().FontColor(Colors.White);
                    header.Cell().Background(Color.FromHex(_primaryColor)).Padding(8)
                        .Text("Risk Level").Bold().FontColor(Colors.White);
                });

                // Clause rows
                foreach (var clause in clauses.Clauses)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(8)
                        .Text(clause.Type).SemiBold();

                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(8)
                        .Text(clause.Description ?? "—");

                    var riskColor = GetRiskColor(clause.RiskLevel);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(8)
                        .Text(clause.RiskLevel ?? "—")
                        .FontColor(Color.FromHex(riskColor))
                        .SemiBold();
                }
            });

            column.Item().Height(20);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item()
                .BorderTop(1)
                .BorderColor(Colors.Grey.Lighten2)
                .PaddingTop(10)
                .Row(row =>
                {
                    row.RelativeItem().Text("Generated by Spaarke AI")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);

                    row.RelativeItem().AlignRight()
                        .Text(text =>
                        {
                            text.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken1));
                            text.Span("Page ");
                            text.CurrentPageNumber();
                            text.Span(" of ");
                            text.TotalPages();
                        });
                });
        });
    }

    private static bool HasEntities(AnalysisEntities entities)
    {
        return entities.Organizations.Count > 0 ||
               entities.People.Count > 0 ||
               entities.Dates.Count > 0 ||
               entities.Amounts.Count > 0 ||
               entities.References.Count > 0;
    }

    private static string GetRiskColor(string? riskLevel) => riskLevel?.ToUpperInvariant() switch
    {
        "HIGH" or "CRITICAL" => HighRiskColor,
        "MEDIUM" => MediumRiskColor,
        "LOW" => LowRiskColor,
        _ => "#666666"
    };

    private static string LightenColor(string hexColor)
    {
        return hexColor switch
        {
            "#0078D4" => "#E6F2FB", // Microsoft blue lightened
            "#D13438" => "#FCECED", // Red lightened
            "#107C10" => "#E7F4E7", // Green lightened
            _ => "#F5F5F5"          // Default light gray
        };
    }

    private static string StripHtmlTags(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Simple HTML tag removal
        var result = System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", "");
        // Decode common HTML entities
        result = result.Replace("&nbsp;", " ")
                       .Replace("&amp;", "&")
                       .Replace("&lt;", "<")
                       .Replace("&gt;", ">")
                       .Replace("&quot;", "\"");
        return result;
    }
}
