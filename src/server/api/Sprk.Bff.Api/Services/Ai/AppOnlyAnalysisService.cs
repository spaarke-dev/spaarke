using System.Text;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Provides AI analysis for documents using app-only authentication.
/// Unlike AnalysisOrchestrationService, this service does not require HttpContext or OBO auth,
/// making it suitable for background job processing (e.g., email-to-document automation).
///
/// Per ADR-013: Extends BFF API pattern with app-only AI analysis capability.
/// </summary>
/// <remarks>
/// Document Profile analysis flow:
/// 1. Get DocumentEntity from Dataverse (metadata with GraphDriveId, GraphItemId)
/// 2. Download file from SPE via ISpeFileOperations (app-only auth)
/// 3. Extract text via ITextExtractor
/// 4. Load playbook by name (e.g., "Document Profile") and execute tools
/// 5. Update Document Profile fields in Dataverse
/// </remarks>
public class AppOnlyAnalysisService : IAppOnlyAnalysisService
{
    private readonly IDataverseService _dataverseService;
    private readonly ISpeFileOperations _speFileOperations;
    private readonly ITextExtractor _textExtractor;
    private readonly IPlaybookService _playbookService;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IToolHandlerRegistry _toolHandlerRegistry;
    private readonly ILogger<AppOnlyAnalysisService> _logger;

    // Summary status values (Dataverse OptionSet)
    private const int SummaryStatusPending = 100000001;
    private const int SummaryStatusCompleted = 100000002;
    private const int SummaryStatusFailed = 100000004;
    private const int SummaryStatusNotSupported = 100000005;

    /// <summary>
    /// Default playbook name for Document Profile analysis.
    /// This playbook is loaded by name rather than hardcoded ID for flexibility.
    /// </summary>
    public const string DefaultPlaybookName = "Document Profile";

    public AppOnlyAnalysisService(
        IDataverseService dataverseService,
        ISpeFileOperations speFileOperations,
        ITextExtractor textExtractor,
        IPlaybookService playbookService,
        IScopeResolverService scopeResolver,
        IToolHandlerRegistry toolHandlerRegistry,
        ILogger<AppOnlyAnalysisService> logger)
    {
        _dataverseService = dataverseService;
        _speFileOperations = speFileOperations;
        _textExtractor = textExtractor;
        _playbookService = playbookService;
        _scopeResolver = scopeResolver;
        _toolHandlerRegistry = toolHandlerRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Analyze a document and update its Document Profile fields in Dataverse.
    /// Uses app-only authentication for all operations.
    /// Loads the playbook by name to get tools and prompts from Dataverse configuration.
    /// </summary>
    /// <param name="documentId">The Dataverse Document ID to analyze.</param>
    /// <param name="playbookName">Optional playbook name override (default: "Document Profile").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with success status and any generated profile data.</returns>
    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(
        Guid documentId,
        string? playbookName = null,
        CancellationToken cancellationToken = default)
    {
        var effectivePlaybookName = playbookName ?? DefaultPlaybookName;
        _logger.LogInformation(
            "Starting app-only document analysis for {DocumentId} using playbook '{PlaybookName}'",
            documentId, effectivePlaybookName);

        try
        {
            // 1. Get document metadata from Dataverse
            var document = await _dataverseService.GetDocumentAsync(documentId.ToString(), cancellationToken);
            if (document == null)
            {
                _logger.LogWarning("Document {DocumentId} not found in Dataverse", documentId);
                return DocumentAnalysisResult.Failed(documentId, "Document not found");
            }

            _logger.LogDebug(
                "Document retrieved: Id={DocumentId}, Name={Name}, GraphDriveId={DriveId}, GraphItemId={ItemId}",
                documentId, document.Name, document.GraphDriveId, document.GraphItemId);

            // 2. Validate document has SPE file reference
            if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
            {
                _logger.LogWarning("Document {DocumentId} has no SPE file reference", documentId);
                await UpdateSummaryStatusAsync(documentId, SummaryStatusNotSupported, cancellationToken);
                return DocumentAnalysisResult.Failed(documentId, "Document has no file reference");
            }

            // 3. Check if file type is supported for extraction
            var fileName = document.FileName ?? document.Name ?? "unknown";
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            if (!_textExtractor.IsSupported(extension))
            {
                _logger.LogWarning(
                    "File type {Extension} not supported for text extraction (Document: {DocumentId})",
                    extension, documentId);
                await UpdateSummaryStatusAsync(documentId, SummaryStatusNotSupported, cancellationToken);
                return DocumentAnalysisResult.Failed(documentId, $"File type '{extension}' not supported");
            }

            // Mark as pending before processing
            await UpdateSummaryStatusAsync(documentId, SummaryStatusPending, cancellationToken);

            // 4. Download file from SPE using app-only auth
            _logger.LogInformation(
                "Downloading document {DocumentId} from SPE (Drive={DriveId}, Item={ItemId})",
                documentId, document.GraphDriveId, document.GraphItemId);

            using var fileStream = await _speFileOperations.DownloadFileAsync(
                document.GraphDriveId,
                document.GraphItemId,
                cancellationToken);

            if (fileStream == null)
            {
                _logger.LogWarning("Failed to download document {DocumentId} from SPE", documentId);
                await UpdateSummaryStatusAsync(documentId, SummaryStatusFailed, cancellationToken);
                return DocumentAnalysisResult.Failed(documentId, "Failed to download file");
            }

            // 5. Extract text from document
            var extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);

            if (!extractionResult.Success || string.IsNullOrWhiteSpace(extractionResult.Text))
            {
                _logger.LogWarning(
                    "Text extraction failed for document {DocumentId}: {Error}",
                    documentId, extractionResult.ErrorMessage);
                await UpdateSummaryStatusAsync(documentId, SummaryStatusFailed, cancellationToken);
                return DocumentAnalysisResult.Failed(documentId, extractionResult.ErrorMessage ?? "Text extraction failed");
            }

            _logger.LogInformation(
                "Extracted {CharCount} characters from document {DocumentId}",
                extractionResult.Text.Length, documentId);

            // 6. Execute playbook-based analysis
            var analysisResult = await ExecutePlaybookAnalysisAsync(
                documentId,
                document.Name ?? fileName,
                fileName,
                extractionResult.Text,
                effectivePlaybookName,
                cancellationToken);

            if (!analysisResult.IsSuccess)
            {
                await UpdateSummaryStatusAsync(documentId, SummaryStatusFailed, cancellationToken);
                return analysisResult;
            }

            // Mark as completed
            if (analysisResult.ProfileUpdate != null)
            {
                analysisResult.ProfileUpdate.SummaryStatus = SummaryStatusCompleted;
                await _dataverseService.UpdateDocumentAsync(documentId.ToString(), analysisResult.ProfileUpdate, cancellationToken);
            }
            else
            {
                await UpdateSummaryStatusAsync(documentId, SummaryStatusCompleted, cancellationToken);
            }

            _logger.LogInformation(
                "Document Profile updated for {DocumentId} via playbook '{PlaybookName}'",
                documentId, effectivePlaybookName);

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing document {DocumentId}", documentId);

            // Try to mark as failed
            try
            {
                await UpdateSummaryStatusAsync(documentId, SummaryStatusFailed, cancellationToken);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning(updateEx, "Failed to update summary status for document {DocumentId}", documentId);
            }

            return DocumentAnalysisResult.Failed(documentId, ex.Message);
        }
    }

    /// <summary>
    /// Analyze a document from an existing stream (e.g., from email attachment).
    /// Useful when the file is already in memory and doesn't need to be downloaded from SPE.
    /// </summary>
    /// <param name="documentId">The Dataverse Document ID to update.</param>
    /// <param name="fileName">The file name for extension detection.</param>
    /// <param name="fileStream">The file content stream.</param>
    /// <param name="playbookName">Optional playbook name override (default: "Document Profile").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with success status and any generated profile data.</returns>
    public async Task<DocumentAnalysisResult> AnalyzeDocumentFromStreamAsync(
        Guid documentId,
        string fileName,
        Stream fileStream,
        string? playbookName = null,
        CancellationToken cancellationToken = default)
    {
        var effectivePlaybookName = playbookName ?? DefaultPlaybookName;
        _logger.LogInformation(
            "Starting stream-based document analysis for {DocumentId} using playbook '{PlaybookName}'",
            documentId, effectivePlaybookName);

        try
        {
            // Check if file type is supported
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            if (!_textExtractor.IsSupported(extension))
            {
                _logger.LogWarning(
                    "File type {Extension} not supported for text extraction (Document: {DocumentId})",
                    extension, documentId);
                return DocumentAnalysisResult.Failed(documentId, $"File type '{extension}' not supported");
            }

            // Extract text
            var extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);

            if (!extractionResult.Success || string.IsNullOrWhiteSpace(extractionResult.Text))
            {
                _logger.LogWarning(
                    "Text extraction failed for document {DocumentId}: {Error}",
                    documentId, extractionResult.ErrorMessage);
                return DocumentAnalysisResult.Failed(documentId, extractionResult.ErrorMessage ?? "Text extraction failed");
            }

            _logger.LogInformation(
                "Extracted {CharCount} characters from document {DocumentId}",
                extractionResult.Text.Length, documentId);

            // Execute playbook-based analysis
            var analysisResult = await ExecutePlaybookAnalysisAsync(
                documentId,
                fileName,
                fileName,
                extractionResult.Text,
                effectivePlaybookName,
                cancellationToken);

            if (!analysisResult.IsSuccess)
            {
                return analysisResult;
            }

            // Update Document Profile
            if (analysisResult.ProfileUpdate != null)
            {
                analysisResult.ProfileUpdate.SummaryStatus = SummaryStatusCompleted;
                await _dataverseService.UpdateDocumentAsync(documentId.ToString(), analysisResult.ProfileUpdate, cancellationToken);
            }

            _logger.LogInformation(
                "Document Profile updated for {DocumentId} from stream analysis via playbook '{PlaybookName}'",
                documentId, effectivePlaybookName);

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing document {DocumentId} from stream", documentId);
            return DocumentAnalysisResult.Failed(documentId, ex.Message);
        }
    }

    /// <summary>
    /// Execute playbook-based analysis using tools from the specified playbook.
    /// </summary>
    private async Task<DocumentAnalysisResult> ExecutePlaybookAnalysisAsync(
        Guid documentId,
        string documentName,
        string fileName,
        string documentText,
        string playbookName,
        CancellationToken cancellationToken)
    {
        // 1. Load playbook by name
        Models.Ai.PlaybookResponse playbook;
        try
        {
            playbook = await _playbookService.GetByNameAsync(playbookName, cancellationToken);
            _logger.LogDebug(
                "Loaded playbook '{PlaybookName}' (Id={PlaybookId}) with {ToolCount} tools",
                playbook.Name, playbook.Id, playbook.ToolIds?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playbook '{PlaybookName}'", playbookName);
            return DocumentAnalysisResult.Failed(documentId, $"Playbook '{playbookName}' not found");
        }

        // 2. Resolve playbook scopes (Skills, Knowledge, Tools)
        var scopes = await _scopeResolver.ResolvePlaybookScopesAsync(playbook.Id, cancellationToken);
        _logger.LogDebug(
            "Resolved scopes: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
            scopes.Skills.Length, scopes.Knowledge.Length, scopes.Tools.Length);

        if (scopes.Tools.Length == 0)
        {
            _logger.LogWarning("Playbook '{PlaybookName}' has no tools configured", playbookName);
            return DocumentAnalysisResult.Failed(documentId, $"Playbook '{playbookName}' has no tools configured");
        }

        // 3. Build execution context
        var analysisId = Guid.NewGuid();
        var executionContext = new ToolExecutionContext
        {
            AnalysisId = analysisId,
            TenantId = "app-only", // App-only context doesn't have tenant claims
            Document = new DocumentContext
            {
                DocumentId = documentId,
                Name = documentName,
                FileName = fileName,
                ExtractedText = documentText,
                Metadata = new Dictionary<string, object?>
                {
                    ["PlaybookId"] = playbook.Id,
                    ["PlaybookName"] = playbook.Name
                }
            }
        };

        // 4. Execute tools and collect results
        var toolResults = new List<ToolResult>();
        var structuredOutputs = new Dictionary<string, string?>();

        foreach (var tool in scopes.Tools)
        {
            _logger.LogDebug("Executing tool '{ToolName}' (Type={ToolType})", tool.Name, tool.Type);

            // Get handler for this tool type
            var handlers = _toolHandlerRegistry.GetHandlersByType(tool.Type);
            var handler = handlers.FirstOrDefault();

            if (handler == null)
            {
                _logger.LogWarning("No handler found for tool type {ToolType}", tool.Type);
                continue;
            }

            // Validate
            var validation = handler.Validate(executionContext, tool);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "Tool validation failed for '{ToolName}': {Errors}",
                    tool.Name, string.Join(", ", validation.Errors));
                continue;
            }

            // Execute
            try
            {
                var toolResult = await handler.ExecuteAsync(executionContext, tool, cancellationToken);
                if (toolResult.Success)
                {
                    toolResults.Add(toolResult);
                    ExtractStructuredOutputs(toolResult, structuredOutputs);

                    _logger.LogDebug(
                        "Tool '{ToolName}' completed successfully: {Summary}",
                        tool.Name, toolResult.Summary?[..Math.Min(100, toolResult.Summary?.Length ?? 0)]);
                }
                else
                {
                    _logger.LogWarning(
                        "Tool '{ToolName}' execution failed: {Error}",
                        tool.Name, toolResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool '{ToolName}'", tool.Name);
            }
        }

        if (toolResults.Count == 0)
        {
            _logger.LogWarning("No tools executed successfully for document {DocumentId}", documentId);
            return DocumentAnalysisResult.Failed(documentId, "No tools executed successfully");
        }

        // 5. Build profile update from structured outputs
        var profileUpdate = BuildProfileUpdateFromOutputs(structuredOutputs);

        _logger.LogInformation(
            "Playbook analysis completed for {DocumentId}: {ToolCount} tools executed, {OutputCount} outputs extracted",
            documentId, toolResults.Count, structuredOutputs.Count);

        return DocumentAnalysisResult.Success(documentId, profileUpdate);
    }

    /// <summary>
    /// Extract structured outputs from tool results for Document Profile storage.
    /// </summary>
    private void ExtractStructuredOutputs(ToolResult toolResult, Dictionary<string, string?> outputs)
    {
        if (toolResult.Data is null)
        {
            return;
        }

        try
        {
            using var dataDoc = JsonDocument.Parse(toolResult.Data.Value.GetRawText());
            var root = dataDoc.RootElement;

            // Map tool result structures to output type names based on handler ID
            if (toolResult.HandlerId.Equals("EntityExtractorHandler", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("entities", out var entitiesValue) ||
                    root.TryGetProperty("Entities", out entitiesValue))
                {
                    outputs["Entities"] = JsonSerializer.Serialize(entitiesValue);
                }
            }
            else if (toolResult.HandlerId.Equals("SummaryHandler", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("fullText", out var fullTextValue) ||
                    root.TryGetProperty("FullText", out fullTextValue))
                {
                    var summaryText = fullTextValue.GetString();
                    if (!string.IsNullOrWhiteSpace(summaryText))
                    {
                        outputs["Summary"] = summaryText;
                        outputs["TL;DR"] = ExtractTldr(summaryText);
                    }
                }

                if (root.TryGetProperty("keywords", out var keywordsValue) ||
                    root.TryGetProperty("Keywords", out keywordsValue))
                {
                    outputs["Keywords"] = keywordsValue.GetString();
                }
            }
            else if (toolResult.HandlerId.Equals("DocumentClassifierHandler", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("documentType", out var docTypeValue) ||
                    root.TryGetProperty("DocumentType", out docTypeValue) ||
                    root.TryGetProperty("classification", out docTypeValue))
                {
                    outputs["Document Type"] = docTypeValue.ValueKind == JsonValueKind.String
                        ? docTypeValue.GetString()
                        : docTypeValue.ToString();
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse tool data for {ToolName}", toolResult.ToolName);
        }
    }

    /// <summary>
    /// Extract TL;DR from full summary text.
    /// </summary>
    private static string ExtractTldr(string summaryText)
    {
        if (string.IsNullOrWhiteSpace(summaryText))
            return string.Empty;

        // Take first 1-2 sentences (up to 200 chars)
        var sentences = System.Text.RegularExpressions.Regex.Split(summaryText, @"(?<=[.!?])\s+");
        var tldr = new StringBuilder();
        var charCount = 0;

        foreach (var sentence in sentences.Take(2))
        {
            if (charCount + sentence.Length > 200 && tldr.Length > 0)
                break;

            tldr.Append(sentence);
            if (!sentence.EndsWith(" "))
                tldr.Append(" ");
            charCount += sentence.Length;
        }

        return tldr.ToString().Trim();
    }

    /// <summary>
    /// Build UpdateDocumentRequest from extracted structured outputs.
    /// </summary>
    private static UpdateDocumentRequest BuildProfileUpdateFromOutputs(Dictionary<string, string?> outputs)
    {
        var request = new UpdateDocumentRequest();

        if (outputs.TryGetValue("TL;DR", out var tldr) && !string.IsNullOrWhiteSpace(tldr))
        {
            request.TlDr = tldr;
        }

        if (outputs.TryGetValue("Summary", out var summary) && !string.IsNullOrWhiteSpace(summary))
        {
            request.Summary = summary;
        }

        if (outputs.TryGetValue("Keywords", out var keywords) && !string.IsNullOrWhiteSpace(keywords))
        {
            request.Keywords = keywords;
        }

        if (outputs.TryGetValue("Document Type", out var docType) && !string.IsNullOrWhiteSpace(docType))
        {
            request.ExtractDocumentType = docType;
        }

        if (outputs.TryGetValue("Entities", out var entities) && !string.IsNullOrWhiteSpace(entities))
        {
            // Parse entities JSON and populate individual fields
            ParseEntitiesJson(entities, request);
        }

        return request;
    }

    /// <summary>
    /// Parse entities JSON from EntityExtractorHandler and populate request fields.
    /// </summary>
    private static void ParseEntitiesJson(string entitiesJson, UpdateDocumentRequest request)
    {
        try
        {
            using var doc = JsonDocument.Parse(entitiesJson);
            var root = doc.RootElement;

            // Handle array of entities
            if (root.ValueKind == JsonValueKind.Array)
            {
                var organizations = new List<string>();
                var people = new List<string>();
                var dates = new List<string>();
                var amounts = new List<string>();
                var references = new List<string>();

                foreach (var entity in root.EnumerateArray())
                {
                    var type = entity.TryGetProperty("type", out var typeValue)
                        ? typeValue.GetString()?.ToLowerInvariant()
                        : null;
                    var name = entity.TryGetProperty("name", out var nameValue)
                        ? nameValue.GetString()
                        : entity.TryGetProperty("value", out var valueVal)
                            ? valueVal.GetString()
                            : null;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    switch (type)
                    {
                        case "organization":
                        case "company":
                        case "org":
                            organizations.Add(name);
                            break;
                        case "person":
                        case "people":
                        case "individual":
                            people.Add(name);
                            break;
                        case "date":
                        case "datetime":
                            dates.Add(name);
                            break;
                        case "money":
                        case "amount":
                        case "currency":
                            amounts.Add(name);
                            break;
                        case "reference":
                        case "id":
                        case "number":
                            references.Add(name);
                            break;
                    }
                }

                if (organizations.Count > 0)
                    request.ExtractOrganization = string.Join(", ", organizations.Distinct());
                if (people.Count > 0)
                    request.ExtractPeople = string.Join(", ", people.Distinct());
                if (dates.Count > 0)
                    request.ExtractDates = string.Join(", ", dates.Distinct());
                if (amounts.Count > 0)
                    request.ExtractFees = string.Join(", ", amounts.Distinct());
                if (references.Count > 0)
                    request.ExtractReference = string.Join(", ", references.Distinct());
            }
        }
        catch (JsonException)
        {
            // If parsing fails, store as raw string
        }
    }

    /// <summary>
    /// Update only the summary status field.
    /// </summary>
    private async Task UpdateSummaryStatusAsync(
        Guid documentId,
        int status,
        CancellationToken cancellationToken)
    {
        var update = new UpdateDocumentRequest { SummaryStatus = status };
        await _dataverseService.UpdateDocumentAsync(documentId.ToString(), update, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Email Analysis (FR-11, FR-12)
    // Combines email metadata + body + attachments for comprehensive AI analysis
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Default playbook name for Email Analysis.
    /// </summary>
    public const string EmailAnalysisPlaybookName = "Email Analysis";

    /// <summary>
    /// Maximum combined context size in characters (100KB per FR-12).
    /// </summary>
    private const int MaxEmailContextChars = 100_000;

    /// <summary>
    /// Analyze an email and its attachments as a combined context.
    /// Combines email metadata + body + attachment text and executes the "Email Analysis" playbook.
    /// Results are stored on the main .eml Document record.
    /// </summary>
    /// <param name="emailId">The Dataverse email activity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with success status. Results stored on the main .eml Document.</returns>
    public async Task<EmailAnalysisResult> AnalyzeEmailAsync(
        Guid emailId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting email analysis for email activity {EmailId} using playbook '{PlaybookName}'",
            emailId, EmailAnalysisPlaybookName);

        try
        {
            // 1. Find the main .eml Document record by email lookup
            var mainDocument = await FindEmailDocumentAsync(emailId, cancellationToken);
            if (mainDocument == null)
            {
                _logger.LogWarning(
                    "No .eml Document found for email activity {EmailId}. Email may not have been converted yet.",
                    emailId);
                return EmailAnalysisResult.Failed(emailId, Guid.Empty, "No .eml document found for this email");
            }

            var mainDocumentId = Guid.Parse(mainDocument.Id);
            _logger.LogDebug(
                "Found main .eml document {DocumentId} for email {EmailId}",
                mainDocumentId, emailId);

            // Mark as pending before processing
            await UpdateSummaryStatusAsync(mainDocumentId, SummaryStatusPending, cancellationToken);

            // 2. Extract email metadata from the document record
            var emailMetadata = ExtractEmailMetadataFromDocument(mainDocument);

            // 3. Extract text from main .eml document
            var mainDocText = await ExtractDocumentTextAsync(mainDocument, cancellationToken);
            if (string.IsNullOrWhiteSpace(mainDocText))
            {
                _logger.LogWarning(
                    "No text extracted from main .eml document {DocumentId}",
                    mainDocumentId);
                mainDocText = emailMetadata.Body ?? string.Empty;
            }

            // 4. Find and extract text from child documents (attachments)
            var attachmentTexts = await ExtractAttachmentTextsAsync(mainDocumentId, cancellationToken);
            _logger.LogInformation(
                "Extracted text from {AttachmentCount} attachments for email {EmailId}",
                attachmentTexts.Count, emailId);

            // 5. Build combined context with size management
            var combinedContext = BuildEmailContext(emailMetadata, mainDocText, attachmentTexts);
            _logger.LogInformation(
                "Built combined context for email {EmailId}: {CharCount} characters (max: {MaxChars})",
                emailId, combinedContext.Length, MaxEmailContextChars);

            // 6. Execute Email Analysis playbook
            var analysisResult = await ExecuteEmailPlaybookAnalysisAsync(
                mainDocumentId,
                mainDocument.Name ?? "Email",
                combinedContext,
                cancellationToken);

            if (!analysisResult.IsSuccess)
            {
                await UpdateSummaryStatusAsync(mainDocumentId, SummaryStatusFailed, cancellationToken);
                return EmailAnalysisResult.Failed(emailId, mainDocumentId, analysisResult.ErrorMessage ?? "Playbook execution failed");
            }

            // 7. Update main document with AI results
            if (analysisResult.ProfileUpdate != null)
            {
                analysisResult.ProfileUpdate.SummaryStatus = SummaryStatusCompleted;
                await _dataverseService.UpdateDocumentAsync(mainDocumentId.ToString(), analysisResult.ProfileUpdate, cancellationToken);
            }
            else
            {
                await UpdateSummaryStatusAsync(mainDocumentId, SummaryStatusCompleted, cancellationToken);
            }

            _logger.LogInformation(
                "Email analysis completed for email {EmailId} -> Document {DocumentId}",
                emailId, mainDocumentId);

            return EmailAnalysisResult.Success(emailId, mainDocumentId, attachmentTexts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing email {EmailId}", emailId);
            return EmailAnalysisResult.Failed(emailId, Guid.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Find the main .eml Document record associated with an email activity.
    /// Uses the IDataverseService.GetDocumentByEmailLookupAsync method.
    /// </summary>
    private async Task<DocumentEntity?> FindEmailDocumentAsync(Guid emailId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Searching for .eml document with email lookup {EmailId}", emailId);
        return await _dataverseService.GetDocumentByEmailLookupAsync(emailId, cancellationToken);
    }

    /// <summary>
    /// Extract email metadata from the document record's email fields.
    /// Uses fields populated by EmailToDocumentJobHandler.
    /// </summary>
    private static EmailMetadataForAnalysis ExtractEmailMetadataFromDocument(DocumentEntity document)
    {
        return new EmailMetadataForAnalysis
        {
            Subject = document.EmailSubject ?? document.Name ?? "No Subject",
            From = document.EmailFrom ?? string.Empty,
            To = document.EmailTo ?? string.Empty,
            Cc = document.EmailCc ?? string.Empty,
            Date = document.EmailDate ?? document.CreatedOn,
            Body = document.EmailBody ?? document.Description ?? string.Empty
        };
    }

    /// <summary>
    /// Extract text content from a document.
    /// </summary>
    private async Task<string> ExtractDocumentTextAsync(DocumentEntity document, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            _logger.LogWarning("Document {DocumentId} has no SPE file reference", document.Id);
            return string.Empty;
        }

        var fileName = document.FileName ?? document.Name ?? "unknown";
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

        if (!_textExtractor.IsSupported(extension))
        {
            _logger.LogWarning("File type {Extension} not supported for document {DocumentId}", extension, document.Id);
            return string.Empty;
        }

        try
        {
            using var fileStream = await _speFileOperations.DownloadFileAsync(
                document.GraphDriveId,
                document.GraphItemId,
                cancellationToken);

            if (fileStream == null)
            {
                _logger.LogWarning("Failed to download document {DocumentId}", document.Id);
                return string.Empty;
            }

            var result = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);
            return result.Success ? result.Text ?? string.Empty : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from document {DocumentId}", document.Id);
            return string.Empty;
        }
    }

    /// <summary>
    /// Extract text from all child documents (attachments) of the main .eml document.
    /// </summary>
    private async Task<List<AttachmentTextInfo>> ExtractAttachmentTextsAsync(
        Guid parentDocumentId,
        CancellationToken cancellationToken)
    {
        var attachmentTexts = new List<AttachmentTextInfo>();

        // Query child documents by ParentDocumentLookup
        _logger.LogDebug("Querying child documents for parent {ParentDocumentId}", parentDocumentId);

        IEnumerable<DocumentEntity> childDocuments;
        try
        {
            childDocuments = await _dataverseService.GetDocumentsByParentAsync(parentDocumentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query child documents for {ParentDocumentId}", parentDocumentId);
            return attachmentTexts;
        }

        var documentList = childDocuments.ToList();
        _logger.LogDebug("Found {Count} child documents (attachments) for parent {ParentDocumentId}",
            documentList.Count, parentDocumentId);

        // Extract text from each attachment in parallel with limited concurrency
        var extractionTasks = documentList.Select(async document =>
        {
            try
            {
                var extractedText = await ExtractDocumentTextAsync(document, cancellationToken);
                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    return new AttachmentTextInfo
                    {
                        DocumentId = document.Id ?? string.Empty,
                        FileName = document.FileName ?? document.Name ?? "unknown",
                        ExtractedText = extractedText,
                        ExtractedAt = DateTime.UtcNow
                    };
                }
                _logger.LogDebug("No text extracted from attachment {FileName}", document.FileName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract text from attachment {DocumentId}", document.Id);
                return null;
            }
        });

        var results = await Task.WhenAll(extractionTasks);
        attachmentTexts.AddRange(results.Where(r => r != null)!);

        _logger.LogInformation("Extracted text from {Count} of {Total} attachments",
            attachmentTexts.Count, documentList.Count);

        return attachmentTexts;
    }

    /// <summary>
    /// Build combined email context for AI analysis.
    /// Implements FR-12 context size management with truncation.
    /// </summary>
    private string BuildEmailContext(
        EmailMetadataForAnalysis metadata,
        string emailBodyText,
        List<AttachmentTextInfo> attachmentTexts)
    {
        var sb = new StringBuilder();

        // Email metadata section
        sb.AppendLine("===== EMAIL METADATA =====");
        sb.AppendLine($"Subject: {metadata.Subject}");
        if (!string.IsNullOrEmpty(metadata.From))
            sb.AppendLine($"From: {metadata.From}");
        if (!string.IsNullOrEmpty(metadata.To))
            sb.AppendLine($"To: {metadata.To}");
        if (!string.IsNullOrEmpty(metadata.Cc))
            sb.AppendLine($"CC: {metadata.Cc}");
        sb.AppendLine($"Date: {metadata.Date:u}");
        sb.AppendLine();

        // Email body section
        sb.AppendLine("===== EMAIL BODY =====");
        sb.AppendLine(emailBodyText);
        sb.AppendLine();

        // Calculate remaining budget for attachments
        var headerAndBodyLength = sb.Length;
        var remainingBudget = MaxEmailContextChars - headerAndBodyLength;

        // Attachment sections (prioritize by size, most recent first)
        foreach (var attachment in attachmentTexts.OrderByDescending(a => a.ExtractedAt))
        {
            var sectionHeader = $"===== ATTACHMENT: {attachment.FileName} =====\n";
            var sectionFooter = "\n\n";
            var availableForContent = remainingBudget - sectionHeader.Length - sectionFooter.Length;

            if (availableForContent <= 100)
            {
                // Not enough space for meaningful content
                _logger.LogDebug(
                    "Skipping attachment {FileName} due to context budget constraints",
                    attachment.FileName);
                continue;
            }

            var attachmentContent = attachment.ExtractedText;
            if (attachmentContent.Length > availableForContent)
            {
                // Truncate attachment content
                attachmentContent = attachmentContent[..availableForContent] + "\n[... truncated ...]";
                _logger.LogDebug(
                    "Truncated attachment {FileName} from {Original} to {Truncated} chars",
                    attachment.FileName, attachment.ExtractedText.Length, availableForContent);
            }

            sb.Append(sectionHeader);
            sb.Append(attachmentContent);
            sb.Append(sectionFooter);

            remainingBudget -= (sectionHeader.Length + attachmentContent.Length + sectionFooter.Length);
        }

        var result = sb.ToString();

        // Final truncation if still over budget (shouldn't happen with proper calculations)
        if (result.Length > MaxEmailContextChars)
        {
            _logger.LogWarning(
                "Combined context exceeded max ({Length} > {Max}), truncating",
                result.Length, MaxEmailContextChars);
            result = result[..MaxEmailContextChars];
        }

        return result;
    }

    /// <summary>
    /// Execute Email Analysis playbook on the combined context.
    /// </summary>
    private async Task<DocumentAnalysisResult> ExecuteEmailPlaybookAnalysisAsync(
        Guid documentId,
        string documentName,
        string combinedContext,
        CancellationToken cancellationToken)
    {
        return await ExecutePlaybookAnalysisAsync(
            documentId,
            documentName,
            "email-combined.eml",
            combinedContext,
            EmailAnalysisPlaybookName,
            cancellationToken);
    }
}

/// <summary>
/// Result of an email analysis operation.
/// </summary>
public class EmailAnalysisResult
{
    public Guid EmailId { get; init; }
    public Guid DocumentId { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public int AttachmentsAnalyzed { get; init; }

    public static EmailAnalysisResult Success(Guid emailId, Guid documentId, int attachmentsAnalyzed)
        => new()
        {
            EmailId = emailId,
            DocumentId = documentId,
            IsSuccess = true,
            AttachmentsAnalyzed = attachmentsAnalyzed
        };

    public static EmailAnalysisResult Failed(Guid emailId, Guid documentId, string errorMessage)
        => new()
        {
            EmailId = emailId,
            DocumentId = documentId,
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// Email metadata extracted from document record for analysis context.
/// </summary>
internal class EmailMetadataForAnalysis
{
    public string Subject { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Cc { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Attachment text extraction info for context building.
/// </summary>
internal class AttachmentTextInfo
{
    public string DocumentId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ExtractedText { get; init; } = string.Empty;
    public DateTime ExtractedAt { get; init; }
}

/// <summary>
/// Result of a document analysis operation.
/// </summary>
public class DocumentAnalysisResult
{
    public Guid DocumentId { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public UpdateDocumentRequest? ProfileUpdate { get; init; }

    public static DocumentAnalysisResult Success(Guid documentId, UpdateDocumentRequest profileUpdate)
        => new()
        {
            DocumentId = documentId,
            IsSuccess = true,
            ProfileUpdate = profileUpdate
        };

    public static DocumentAnalysisResult Failed(Guid documentId, string errorMessage)
        => new()
        {
            DocumentId = documentId,
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
}
