using Microsoft.AspNetCore.Http;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// <see cref="IDocumentTextSource"/> implementation that composes
/// <see cref="AnalysisDocumentLoader"/> (Dataverse row + SPE extraction) and
/// <see cref="ITextExtractor"/> (raw file extraction).
/// </summary>
public sealed class DocumentTextSource : IDocumentTextSource
{
    private readonly AnalysisDocumentLoader _documentLoader;
    private readonly ITextExtractor _textExtractor;
    private readonly ILogger<DocumentTextSource> _logger;

    public DocumentTextSource(
        AnalysisDocumentLoader documentLoader,
        ITextExtractor textExtractor,
        ILogger<DocumentTextSource> logger)
    {
        _documentLoader = documentLoader;
        _textExtractor = textExtractor;
        _logger = logger;
    }

    public async Task<DocumentText> ExtractFromDocumentIdAsync(
        Guid documentId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var document = await _documentLoader.GetDocumentAsync(documentId.ToString(), cancellationToken)
            ?? throw new InvalidOperationException($"Document {documentId} not found in Dataverse.");

        var extractedText = await _documentLoader.ExtractDocumentTextAsync(document, httpContext, cancellationToken);

        _logger.LogInformation(
            "Extracted document text: DocumentId={DocumentId}, FileName={FileName}, TextLength={TextLength}",
            documentId, document.FileName ?? document.Name, extractedText?.Length ?? 0);

        return new DocumentText
        {
            DocumentId = documentId,
            FileName = document.FileName ?? document.Name ?? "Unknown",
            ExtractedText = extractedText ?? string.Empty,
            GraphDriveId = document.GraphDriveId,
            GraphItemId = document.GraphItemId,
        };
    }

    public async Task<DocumentText> ExtractFromFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("Uploaded file is empty.", nameof(file));
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (!_textExtractor.IsSupported(extension))
        {
            throw new InvalidOperationException(
                $"File type '{extension}' is not supported for text extraction.");
        }

        using var stream = file.OpenReadStream();
        var result = await _textExtractor.ExtractAsync(stream, file.FileName, cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Text extraction failed for '{file.FileName}': {result.ErrorMessage}");
        }

        _logger.LogInformation(
            "Extracted file text: FileName={FileName}, TextLength={TextLength}, Method={Method}",
            file.FileName, result.Text?.Length ?? 0, result.Method);

        return new DocumentText
        {
            DocumentId = null,
            FileName = file.FileName,
            ExtractedText = result.Text ?? string.Empty,
        };
    }
}
