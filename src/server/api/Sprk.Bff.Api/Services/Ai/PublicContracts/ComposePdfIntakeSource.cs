namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — the PublicContracts facade by which the Compose PDF intake
/// path obtains the STRUCTURED layout of a PDF (NDA/agreement): Azure Document Intelligence
/// <c>prebuilt-layout</c> via the EXISTING <c>DocumentParserRouter</c> →
/// <c>DocumentIntelligenceService</c> → <c>ITextExtractor</c> stack — reuse, no parallel PDF subsystem
/// (spec §11 MUST rule). Parse ONLY — the projection into the Compose canonical content model is
/// <c>ComposePdfModelProjector</c> (<c>Services/Compose</c>), which consumes the neutral
/// <see cref="DocumentLayout"/> this facade returns, keeping <c>Services/Compose</c> free of AI-internal
/// types (ADR-013).
/// </summary>
/// <remarks>
/// Placement (ADR-013 / root §10 bullet 3): the implementation lives in <c>Services/Ai</c> beside
/// <c>DocumentIntelligenceService</c> (the intake path it composes). This is a document-PARSE facade,
/// not AI dispatch — no engine, no routing, no model call (ADR-039 non-conflict). Sibling precedent:
/// <see cref="IComposeTemplateSource"/> (task 031).
/// </remarks>
public interface IComposePdfIntakeSource
{
    /// <summary>
    /// Parses a PDF into its structured layout (paragraphs-with-roles + tables in document order) via
    /// the existing Azure Document Intelligence intake path. Returns null when the layout cannot be
    /// extracted (unconfigured/disabled service, corrupt file, service failure) — the failure is
    /// logged loudly here; the caller surfaces a clear "PDF could not be opened" outcome, never a
    /// silent empty document.
    /// </summary>
    /// <param name="pdfBytes">Raw PDF bytes (ADR-007: supplied by the caller from the SpeFileStore
    /// facade; this facade never fetches storage).</param>
    /// <param name="fileName">File name including extension (drives extraction method + logging).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<DocumentLayout?> ParseAsync(
        byte[] pdfBytes,
        string fileName,
        CancellationToken cancellationToken);
}
