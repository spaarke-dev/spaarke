using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance.Models;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// AI-powered invoice analysis using OpenAI structured output with playbook-driven prompts.
/// Implements Playbook A (classification) and Playbook B (extraction) for the Finance Intelligence Module.
/// </summary>
/// <remarks>
/// Per ADR-013: Extends BFF API with AI analysis capability.
/// Per ADR-014: Prompts loaded from Dataverse via IPlaybookService, not hardcoded strings.
/// Per ADR-015: NEVER log document content, extracted text, or prompts. Only log IDs, sizes, timings.
///
/// Classification uses gpt-4o-mini (fast, cost-effective) for binary invoice/not-invoice decisions.
/// Extraction uses gpt-4o (accurate) for detailed structured data extraction from invoice text.
/// </remarks>
public class InvoiceAnalysisService : IInvoiceAnalysisService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly IPlaybookService _playbookService;
    private readonly FinanceOptions _options;
    private readonly ILogger<InvoiceAnalysisService> _logger;

    /// <summary>
    /// Playbook name for attachment classification (Playbook A).
    /// Loaded from Dataverse via IPlaybookService.GetByNameAsync.
    /// </summary>
    internal const string ClassificationPlaybookName = "FinanceClassification";

    /// <summary>
    /// Playbook name for invoice fact extraction (Playbook B).
    /// Loaded from Dataverse via IPlaybookService.GetByNameAsync.
    /// </summary>
    internal const string ExtractionPlaybookName = "FinanceExtraction";

    public InvoiceAnalysisService(
        IOpenAiClient openAiClient,
        IPlaybookService playbookService,
        IOptions<FinanceOptions> options,
        ILogger<InvoiceAnalysisService> logger)
    {
        _openAiClient = openAiClient;
        _playbookService = playbookService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ClassificationResult> ClassifyAttachmentAsync(
        string documentText,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting attachment classification. DocumentTextLength={TextLength}",
            documentText.Length);

        // 1. Load classification playbook prompt from Dataverse (ADR-014)
        var playbook = await _playbookService.GetByNameAsync(ClassificationPlaybookName, ct);
        var systemPrompt = playbook.Description
            ?? throw new InvalidOperationException(
                $"Playbook '{ClassificationPlaybookName}' has no description (system prompt).");

        _logger.LogDebug(
            "Loaded classification playbook. PlaybookId={PlaybookId}, PromptLength={PromptLength}",
            playbook.Id, systemPrompt.Length);

        // 2. Build ChatMessages: system prompt + user prompt with document text
        var userPrompt = $"""
            Analyze the following document text and classify whether it is an invoice candidate or not.
            Extract any invoice hints you can identify.

            <document>
            {documentText}
            </document>
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        // 3. Call structured completion with classification schema
        var result = await _openAiClient.GetStructuredCompletionAsync<ClassificationResult>(
            messages,
            FinanceJsonSchemas.ClassificationResultSchema,
            nameof(ClassificationResult),
            _options.ClassificationDeploymentName,
            ct);

        sw.Stop();

        // ADR-015: Only log IDs, sizes, timings — never document content or prompts
        _logger.LogInformation(
            "Attachment classification completed. Classification={Classification}, Confidence={Confidence}, ElapsedMs={ElapsedMs}",
            result.Classification, result.Confidence, sw.ElapsedMilliseconds);

        return result;
    }

    /// <inheritdoc />
    public async Task<ExtractionResult> ExtractInvoiceFactsAsync(
        string documentText,
        InvoiceHints? reviewerHints = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting invoice fact extraction. DocumentTextLength={TextLength}, HasReviewerHints={HasHints}",
            documentText.Length, reviewerHints is not null);

        // 1. Load extraction playbook prompt from Dataverse (ADR-014)
        var playbook = await _playbookService.GetByNameAsync(ExtractionPlaybookName, ct);
        var systemPrompt = playbook.Description
            ?? throw new InvalidOperationException(
                $"Playbook '{ExtractionPlaybookName}' has no description (system prompt).");

        _logger.LogDebug(
            "Loaded extraction playbook. PlaybookId={PlaybookId}, PromptLength={PromptLength}",
            playbook.Id, systemPrompt.Length);

        // 2. Build ChatMessages with document text and optional reviewer hints
        var hintsSection = reviewerHints is not null
            ? $"""

              <reviewer_hints>
              Vendor: {reviewerHints.VendorName ?? "unknown"}
              Invoice Number: {reviewerHints.InvoiceNumber ?? "unknown"}
              Invoice Date: {reviewerHints.InvoiceDate ?? "unknown"}
              Total Amount: {(reviewerHints.TotalAmount.HasValue ? reviewerHints.TotalAmount.Value.ToString("F2") : "unknown")}
              Currency: {reviewerHints.Currency ?? "unknown"}
              Matter Reference: {reviewerHints.MatterReference ?? "unknown"}
              </reviewer_hints>
              """
            : string.Empty;

        var userPrompt = $"""
            Extract all invoice facts from the following document text.
            Return the invoice header, line items, and your confidence in the extraction.
            {hintsSection}
            <document>
            {documentText}
            </document>
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        // 3. Call structured completion with extraction schema
        var result = await _openAiClient.GetStructuredCompletionAsync<ExtractionResult>(
            messages,
            FinanceJsonSchemas.ExtractionResultSchema,
            nameof(ExtractionResult),
            _options.ExtractionDeploymentName,
            ct);

        sw.Stop();

        // ADR-015: Only log IDs, sizes, timings — never document content or prompts
        _logger.LogInformation(
            "Invoice fact extraction completed. LineItemCount={LineItemCount}, Confidence={Confidence}, ElapsedMs={ElapsedMs}",
            result.LineItems.Length, result.ExtractionConfidence, sw.ElapsedMilliseconds);

        return result;
    }
}
