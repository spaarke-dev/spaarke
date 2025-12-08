using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for extracting text from documents.
/// Handles native text formats directly; PDF/DOCX require Document Intelligence (Task 060).
/// </summary>
public class TextExtractorService : ITextExtractor
{
    private readonly AiOptions _options;
    private readonly ILogger<TextExtractorService> _logger;

    public TextExtractorService(IOptions<AiOptions> options, ILogger<TextExtractorService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Extract text from a file stream.
    /// </summary>
    /// <param name="fileStream">The file content stream.</param>
    /// <param name="fileName">The file name (used to determine extraction method).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result with text or error message.</returns>
    public async Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

        _logger.LogDebug("Extracting text from file {FileName} (extension: {Extension})", fileName, extension);

        // Check if file type is supported
        if (!_options.SupportedFileTypes.TryGetValue(extension, out var fileTypeConfig))
        {
            _logger.LogWarning("File type {Extension} is not supported", extension);
            return TextExtractionResult.NotSupported(extension);
        }

        // Check if file type is enabled
        if (!fileTypeConfig.Enabled)
        {
            _logger.LogWarning("File type {Extension} is disabled", extension);
            return TextExtractionResult.Disabled(extension);
        }

        // Route to appropriate extraction method
        return fileTypeConfig.Method switch
        {
            ExtractionMethod.Native => await ExtractNativeAsync(fileStream, fileName, cancellationToken),
            ExtractionMethod.DocumentIntelligence => await ExtractViaDocIntelAsync(fileStream, fileName, cancellationToken),
            ExtractionMethod.VisionOcr => HandleVisionOcrFile(fileName),
            _ => TextExtractionResult.NotSupported(extension)
        };
    }

    /// <summary>
    /// Handle image files that require vision model processing.
    /// Returns a special result indicating the file should be processed directly by vision model.
    /// </summary>
    private TextExtractionResult HandleVisionOcrFile(string fileName)
    {
        // Check if vision model is configured
        if (string.IsNullOrEmpty(_options.ImageSummarizeModel))
        {
            _logger.LogWarning(
                "Vision model not configured. Cannot process image file {FileName}. " +
                "Set Ai:ImageSummarizeModel in configuration.",
                fileName);
            return TextExtractionResult.Failed(
                "Vision model is not configured. Image summarization is unavailable.",
                TextExtractionMethod.VisionOcr);
        }

        _logger.LogDebug(
            "Image file {FileName} will be processed by vision model {Model}",
            fileName, _options.ImageSummarizeModel);

        // Return special result indicating vision processing is required
        return TextExtractionResult.RequiresVision();
    }

    /// <summary>
    /// Extract text from native text files (TXT, MD, JSON, CSV, XML, HTML).
    /// Uses encoding detection to handle UTF-8, UTF-16 with BOM.
    /// </summary>
    private async Task<TextExtractionResult> ExtractNativeAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check file size
            if (fileStream.CanSeek && fileStream.Length > _options.MaxFileSizeBytes)
            {
                var sizeMb = fileStream.Length / (1024.0 * 1024.0);
                var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
                return TextExtractionResult.Failed(
                    $"File size ({sizeMb:F1}MB) exceeds maximum allowed ({maxMb:F1}MB).",
                    TextExtractionMethod.Native);
            }

            // Read with encoding detection (handles BOM for UTF-8, UTF-16 LE/BE)
            using var reader = new StreamReader(
                fileStream,
                encoding: Encoding.UTF8, // Default fallback
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);

            var text = await reader.ReadToEndAsync(cancellationToken);

            // Check for empty content
            if (string.IsNullOrWhiteSpace(text))
            {
                return TextExtractionResult.Failed(
                    "File is empty or contains only whitespace.",
                    TextExtractionMethod.Native);
            }

            // Check estimated token count against limit
            var estimatedTokens = text.Length / 4;
            if (estimatedTokens > _options.MaxInputTokens)
            {
                _logger.LogWarning(
                    "File {FileName} has ~{EstimatedTokens} tokens, exceeding limit of {MaxTokens}. Text will be truncated.",
                    fileName, estimatedTokens, _options.MaxInputTokens);

                // Truncate to approximately MaxInputTokens
                var maxChars = _options.MaxInputTokens * 4;
                text = text[..Math.Min(text.Length, maxChars)];
                text += "\n\n[Content truncated due to size limits]";
            }

            _logger.LogDebug(
                "Successfully extracted {CharCount} characters from {FileName}",
                text.Length, fileName);

            return TextExtractionResult.Succeeded(text, TextExtractionMethod.Native);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from {FileName}", fileName);
            return TextExtractionResult.Failed(
                $"Failed to extract text: {ex.Message}",
                TextExtractionMethod.Native);
        }
    }

    /// <summary>
    /// Extract text from PDF/DOCX files using Azure Document Intelligence.
    /// Uses the prebuilt-read model for general document text extraction.
    /// </summary>
    private async Task<TextExtractionResult> ExtractViaDocIntelAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Check if Document Intelligence is configured
        if (string.IsNullOrEmpty(_options.DocIntelEndpoint) || string.IsNullOrEmpty(_options.DocIntelKey))
        {
            _logger.LogWarning(
                "Document Intelligence not configured. Cannot extract text from {FileName}. " +
                "Set Ai:DocIntelEndpoint and Ai:DocIntelKey in configuration.",
                fileName);
            return TextExtractionResult.Failed(
                "Document Intelligence is not configured. PDF/DOCX extraction is unavailable.",
                TextExtractionMethod.DocumentIntelligence);
        }

        try
        {
            // Check file size before processing
            if (fileStream.CanSeek && fileStream.Length > _options.MaxFileSizeBytes)
            {
                var sizeMb = fileStream.Length / (1024.0 * 1024.0);
                var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
                return TextExtractionResult.Failed(
                    $"File size ({sizeMb:F1}MB) exceeds maximum allowed ({maxMb:F1}MB).",
                    TextExtractionMethod.DocumentIntelligence);
            }

            _logger.LogDebug("Starting Document Intelligence extraction for {FileName}", fileName);

            // Create client
            var credential = new AzureKeyCredential(_options.DocIntelKey);
            var client = new DocumentIntelligenceClient(new Uri(_options.DocIntelEndpoint), credential);

            // Read stream to BinaryData (required by SDK)
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            var binaryData = BinaryData.FromBytes(memoryStream.ToArray());

            // Analyze document using prebuilt-read model
            var operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                binaryData,
                cancellationToken: cancellationToken);

            var result = operation.Value;

            // Extract text from all pages
            var textBuilder = new StringBuilder();
            foreach (var page in result.Pages)
            {
                foreach (var line in page.Lines)
                {
                    textBuilder.AppendLine(line.Content);
                }
                // Add page break between pages
                if (result.Pages.Count > 1)
                {
                    textBuilder.AppendLine();
                }
            }

            var text = textBuilder.ToString().Trim();

            // Check for empty content
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Document Intelligence returned no text for {FileName}", fileName);
                return TextExtractionResult.Failed(
                    "No text could be extracted from the document. The file may be empty, image-only, or corrupted.",
                    TextExtractionMethod.DocumentIntelligence);
            }

            // Check estimated token count against limit
            var estimatedTokens = text.Length / 4;
            if (estimatedTokens > _options.MaxInputTokens)
            {
                _logger.LogWarning(
                    "File {FileName} has ~{EstimatedTokens} tokens, exceeding limit of {MaxTokens}. Text will be truncated.",
                    fileName, estimatedTokens, _options.MaxInputTokens);

                var maxChars = _options.MaxInputTokens * 4;
                text = text[..Math.Min(text.Length, maxChars)];
                text += "\n\n[Content truncated due to size limits]";
            }

            _logger.LogInformation(
                "Successfully extracted {CharCount} characters from {FileName} using Document Intelligence ({PageCount} pages)",
                text.Length, fileName, result.Pages.Count);

            return TextExtractionResult.Succeeded(text, TextExtractionMethod.DocumentIntelligence);
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            _logger.LogWarning(ex, "Document Intelligence could not process {FileName} - invalid or unsupported format", fileName);
            return TextExtractionResult.Failed(
                "The document format is invalid or unsupported by Document Intelligence.",
                TextExtractionMethod.DocumentIntelligence);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Document Intelligence API error for {FileName}: {Status} {Code}",
                fileName, ex.Status, ex.ErrorCode);
            return TextExtractionResult.Failed(
                $"Document Intelligence service error: {ex.Message}",
                TextExtractionMethod.DocumentIntelligence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from {FileName} using Document Intelligence", fileName);
            return TextExtractionResult.Failed(
                $"Failed to extract text: {ex.Message}",
                TextExtractionMethod.DocumentIntelligence);
        }
    }

    /// <summary>
    /// Check if a file extension is supported for extraction.
    /// </summary>
    public bool IsSupported(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : $".{extension}";
        return _options.IsFileTypeSupported(ext.ToLowerInvariant());
    }

    /// <summary>
    /// Get the extraction method for a file extension.
    /// </summary>
    public ExtractionMethod? GetMethod(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : $".{extension}";
        return _options.GetExtractionMethod(ext.ToLowerInvariant());
    }
}
