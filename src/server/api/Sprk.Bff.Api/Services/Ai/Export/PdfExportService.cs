using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Export;

/// <summary>
/// PDF export service using QuestPDF for in-process PDF generation.
/// Follows ADR-001 - no Azure Functions for core processing.
/// </summary>
public partial class PdfExportService : IExportService
{
    private readonly ILogger<PdfExportService> _logger;
    private readonly AnalysisOptions _options;

    private const string ContentType = "application/pdf";

    public PdfExportService(
        ILogger<PdfExportService> logger,
        IOptions<AnalysisOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Pdf;

    /// <inheritdoc />
    public async Task<ExportFileResult> ExportAsync(ExportContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Exporting analysis {AnalysisId} to PDF", context.AnalysisId);

        // Check if PDF export is enabled
        if (!_options.EnablePdfExport)
        {
            _logger.LogWarning("PDF export is disabled in configuration");
            return ExportFileResult.Fail("PDF export is not enabled");
        }

        try
        {
            // Generate PDF using QuestPDF
            var document = new AnalysisPdfDocument(context);
            var pdfBytes = document.GeneratePdf();

            // Generate filename from title
            var fileName = GenerateFileName(context);

            _logger.LogInformation(
                "PDF export complete for {AnalysisId}: {Size} bytes",
                context.AnalysisId, pdfBytes.Length);

            return await Task.FromResult(ExportFileResult.Ok(pdfBytes, ContentType, fileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for {AnalysisId}", context.AnalysisId);
            return ExportFileResult.Fail($"PDF generation failed: {ex.Message}");
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

        if (!_options.EnablePdfExport)
        {
            errors.Add("PDF export is not enabled");
        }

        return errors.Count > 0
            ? ExportValidationResult.Invalid([.. errors])
            : ExportValidationResult.Valid();
    }

    private static string GenerateFileName(ExportContext context)
    {
        var safeName = InvalidFileNameCharsRegex().Replace(context.Title, "_");
        if (safeName.Length > 50)
        {
            safeName = safeName[..50];
        }
        return $"{safeName}_{context.CreatedAt:yyyyMMdd}.pdf";
    }

    [GeneratedRegex(@"[<>:""/\\|?*]")]
    private static partial Regex InvalidFileNameCharsRegex();
}
