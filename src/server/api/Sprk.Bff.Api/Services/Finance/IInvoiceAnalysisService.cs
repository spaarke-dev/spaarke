using Sprk.Bff.Api.Services.Finance.Models;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// AI-powered invoice analysis service using OpenAI structured output.
/// Provides attachment classification (Playbook A) and invoice fact extraction (Playbook B).
/// </summary>
/// <remarks>
/// Per ADR-013: Extends BFF API with AI analysis capability.
/// Per ADR-014: Prompts are loaded from Dataverse via IPlaybookService, not hardcoded.
/// Per ADR-015: NEVER log document content, extracted text, or prompts. Only IDs, sizes, timings.
/// </remarks>
public interface IInvoiceAnalysisService
{
    /// <summary>
    /// Classify an attachment as invoice candidate or non-invoice using AI (Playbook A).
    /// Uses gpt-4o-mini for speed and cost efficiency.
    /// </summary>
    /// <param name="documentText">The extracted text content of the document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Classification result with confidence score and optional invoice hints.</returns>
    Task<ClassificationResult> ClassifyAttachmentAsync(string documentText, CancellationToken ct = default);

    /// <summary>
    /// Extract structured invoice facts from document text using AI (Playbook B).
    /// Uses gpt-4o for higher accuracy on complex extraction tasks.
    /// </summary>
    /// <param name="documentText">The extracted text content of the document.</param>
    /// <param name="reviewerHints">Optional hints from classification to guide extraction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extraction result with invoice header, line items, and confidence score.</returns>
    Task<ExtractionResult> ExtractInvoiceFactsAsync(string documentText, InvoiceHints? reviewerHints = null, CancellationToken ct = default);
}
