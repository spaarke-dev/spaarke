using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Export;

/// <summary>
/// Service interface for exporting analysis results to various document formats.
/// Implements ADR-001 (BFF API pattern) - export services run in the same process.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Gets the export format this service handles.
    /// </summary>
    ExportFormat Format { get; }

    /// <summary>
    /// Exports analysis content to the target format.
    /// </summary>
    /// <param name="context">Export context containing analysis data and options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Export result with file bytes and metadata.</returns>
    Task<ExportFileResult> ExportAsync(ExportContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Validates the export request before execution.
    /// </summary>
    /// <param name="context">Export context to validate.</param>
    /// <returns>Validation result with any errors.</returns>
    ExportValidationResult Validate(ExportContext context);
}

/// <summary>
/// Context for export operations containing analysis data and configuration.
/// </summary>
public record ExportContext
{
    /// <summary>
    /// Analysis ID being exported.
    /// </summary>
    public required Guid AnalysisId { get; init; }

    /// <summary>
    /// Analysis title/name for the document.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Working document content (HTML/Markdown format).
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Optional summary extracted from analysis.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Source document name.
    /// </summary>
    public string? SourceDocumentName { get; init; }

    /// <summary>
    /// Source document ID for linking.
    /// </summary>
    public Guid? SourceDocumentId { get; init; }

    /// <summary>
    /// Analysis creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// User who created the analysis.
    /// </summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// Extracted entities from the analysis.
    /// </summary>
    public AnalysisEntities? Entities { get; init; }

    /// <summary>
    /// Extracted clauses from contract analysis.
    /// </summary>
    public AnalysisClauses? Clauses { get; init; }

    /// <summary>
    /// Export options from the request.
    /// </summary>
    public ExportOptions? Options { get; init; }
}

/// <summary>
/// Entities extracted from document analysis.
/// </summary>
public record AnalysisEntities
{
    /// <summary>Organizations mentioned in the document.</summary>
    public List<string> Organizations { get; init; } = [];

    /// <summary>People mentioned in the document.</summary>
    public List<string> People { get; init; } = [];

    /// <summary>Dates found in the document.</summary>
    public List<string> Dates { get; init; } = [];

    /// <summary>Monetary amounts found in the document.</summary>
    public List<string> Amounts { get; init; } = [];

    /// <summary>Reference numbers found in the document.</summary>
    public List<string> References { get; init; } = [];
}

/// <summary>
/// Clauses extracted from contract analysis.
/// </summary>
public record AnalysisClauses
{
    /// <summary>List of identified clauses with risk assessment.</summary>
    public List<ClauseInfo> Clauses { get; init; } = [];
}

/// <summary>
/// Information about an extracted clause.
/// </summary>
public record ClauseInfo
{
    /// <summary>Clause type (e.g., "Termination", "Indemnity").</summary>
    public required string Type { get; init; }

    /// <summary>Brief description of the clause.</summary>
    public string? Description { get; init; }

    /// <summary>Risk level (Low, Medium, High).</summary>
    public string? RiskLevel { get; init; }

    /// <summary>Extracted text of the clause.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// Result of an export operation.
/// </summary>
public record ExportFileResult
{
    /// <summary>
    /// Whether the export succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// File content bytes (null for action-based exports like Email).
    /// </summary>
    public byte[]? FileBytes { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Suggested filename.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Format-specific metadata (e.g., EmailMessageId for email exports).
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful file result.
    /// </summary>
    public static ExportFileResult Ok(byte[] bytes, string contentType, string fileName) => new()
    {
        Success = true,
        FileBytes = bytes,
        ContentType = contentType,
        FileName = fileName
    };

    /// <summary>
    /// Creates a successful action result (no file, but action completed).
    /// </summary>
    public static ExportFileResult OkAction(Dictionary<string, object?> metadata) => new()
    {
        Success = true,
        Metadata = metadata
    };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static ExportFileResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// Result of export validation.
/// </summary>
public record ExportValidationResult
{
    /// <summary>
    /// Whether validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors if any.
    /// </summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    public static ExportValidationResult Valid() => new() { IsValid = true };

    /// <summary>
    /// Creates an invalid result with errors.
    /// </summary>
    public static ExportValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = [.. errors]
    };
}
