using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — result of STRUCTURED layout extraction
/// (<c>ITextExtractor.ExtractLayoutAsync</c>, Azure Document Intelligence <c>prebuilt-layout</c>).
/// Sibling of <see cref="TextExtractionResult"/> (the flat-text contract existing callers read —
/// byte-unchanged by this addition): where that contract flattens to <c>Text</c>, this one preserves
/// the structural facts (paragraph roles, tables, document order) as a
/// <see cref="DocumentLayout"/> for consumers that project structure, not prose.
/// </summary>
public sealed record LayoutExtractionResult
{
    /// <summary>Whether layout extraction succeeded. False → <see cref="Layout"/> is null and
    /// <see cref="ErrorMessage"/> explains why.</summary>
    public bool Success { get; init; }

    /// <summary>Error message when extraction failed. Null on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The structured layout. Null when extraction failed.</summary>
    public DocumentLayout? Layout { get; init; }

    public static LayoutExtractionResult Succeeded(DocumentLayout layout) => new()
    {
        Success = true,
        Layout = layout,
    };

    public static LayoutExtractionResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
    };
}
