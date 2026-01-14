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
