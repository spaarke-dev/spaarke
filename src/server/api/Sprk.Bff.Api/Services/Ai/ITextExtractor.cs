using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Interface for text extraction from documents.
/// Enables unit testing of services that depend on text extraction.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Extract text from a file stream.
    /// </summary>
    Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a file extension is supported for extraction.
    /// </summary>
    bool IsSupported(string extension);

    /// <summary>
    /// Get the extraction method for a file extension.
    /// </summary>
    ExtractionMethod? GetMethod(string extension);
}
