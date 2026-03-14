using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Service that orchestrates matter AI pre-fill: stores uploaded files temporarily via SpeFileStore,
/// extracts text, invokes the AI Playbook platform for structured matter field extraction, and
/// returns a PreFillResponse.
/// </summary>
/// <remarks>
/// Follows ADR-007: File uploads routed through SpeFileStore facade — no direct SPE access.
/// Follows ADR-013: AI analysis via IPlaybookOrchestrationService — no hardcoded prompts or
/// direct IOpenAiClient calls. Extraction prompts are configured as playbook Skills in Dataverse.
///
/// File storage lifecycle:
/// Files uploaded here are stored under a per-request staging prefix (ai-prefill/{requestId}/...).
/// They are available for later association when the matter record is created.
/// Cleanup of orphaned staging files is a separate concern (background job).
/// </remarks>
public class MatterPreFillService
{
    private readonly SpeFileStore _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly IPlaybookOrchestrationService _playbookService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatterPreFillService> _logger;

    // Playbook configuration key — overridable via appsettings
    private const string PlaybookIdConfigKey = "Workspace:PreFillPlaybookId";

    // Default: "Create New Matter Pre-Fill" playbook (Extract Matter Fields — ACT-008, gpt-4o)
    private static readonly Guid DefaultPreFillPlaybookId =
        Guid.Parse("2d660cad-d418-f111-8343-7ced8d1dc988");

    // Supported MIME types for the pre-fill endpoint
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".xlsx" };

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    // JSON options for parsing the AI structured output
    private static readonly JsonSerializerOptions AiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MatterPreFillService(
        SpeFileStore speFileStore,
        ITextExtractor textExtractor,
        IPlaybookOrchestrationService playbookService,
        IConfiguration configuration,
        ILogger<MatterPreFillService> logger)
    {
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates the uploaded files against size and type constraints.
    /// Returns a list of validation errors (one per offending file). Empty list means all files are valid.
    /// </summary>
    public static List<string> ValidateFiles(IFormFileCollection files)
    {
        var errors = new List<string>();

        if (files == null || files.Count == 0)
        {
            errors.Add("At least one file must be uploaded.");
            return errors;
        }

        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                errors.Add($"File '{file.FileName}' is empty.");
                continue;
            }

            if (file.Length > MaxFileSizeBytes)
            {
                errors.Add($"File '{file.FileName}' exceeds the maximum allowed size of 10 MB " +
                            $"({file.Length / 1024 / 1024:F1} MB uploaded).");
            }

            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
            if (!AllowedExtensions.Contains(extension))
            {
                errors.Add($"File '{file.FileName}' has unsupported type '{extension}'. " +
                            "Only PDF, DOCX, and XLSX files are accepted.");
                continue;
            }

            // Also validate by Content-Type header (defence-in-depth)
            if (!string.IsNullOrEmpty(file.ContentType) &&
                !AllowedContentTypes.Contains(file.ContentType))
            {
                errors.Add($"File '{file.FileName}' has unsupported Content-Type '{file.ContentType}'.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Stores uploaded files temporarily via SpeFileStore (when staging container is configured),
    /// extracts text from the documents, invokes the AI Playbook platform for structured matter
    /// field extraction, and returns a PreFillResponse.
    ///
    /// On AI timeout or service unavailability, returns an empty PreFillResponse with confidence=0
    /// rather than propagating an error (graceful degradation as per spec).
    /// </summary>
    public async Task<PreFillResponse> AnalyzeFilesAsync(
        IFormFileCollection files,
        string userId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();

        _logger.LogInformation(
            "Matter AI pre-fill started. UserId={UserId}, FileCount={FileCount}, RequestId={RequestId}",
            userId, files.Count, requestId);

        // --- Step 1: Store files temporarily via SpeFileStore and extract text ---
        var combinedText = await ExtractTextFromFilesAsync(files, requestId, httpContext, cancellationToken);

        if (string.IsNullOrWhiteSpace(combinedText))
        {
            _logger.LogWarning(
                "No text could be extracted from any uploaded files. Returning empty pre-fill. " +
                "RequestId={RequestId}",
                requestId);
            return PreFillResponse.Empty("NO_TEXT: could not extract text from uploaded files");
        }

        _logger.LogInformation(
            "Text extraction complete. TotalChars={TotalChars}. RequestId={RequestId}",
            combinedText.Length, requestId);

        // --- Step 2: Invoke playbook for structured extraction ---
        return await ExtractFieldsViaPlaybookAsync(combinedText, requestId, httpContext, cancellationToken);
    }

    /// <summary>
    /// Extracts text from uploaded files, optionally staging them in SPE.
    /// </summary>
    private async Task<string> ExtractTextFromFilesAsync(
        IFormFileCollection files,
        Guid requestId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var allExtractedText = new StringBuilder();
        var stagingContainerId = _configuration["SharePointEmbedded:StagingContainerId"];
        var filesExtracted = 0;
        var filesFailed = 0;
        var filesSkipped = 0;

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file.FileName);
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            using var fileStream = file.OpenReadStream();

            if (!_textExtractor.IsSupported(extension))
            {
                filesSkipped++;
                continue;
            }

            TextExtractionResult extractionResult;
            if (!string.IsNullOrEmpty(stagingContainerId))
            {
                var stagingPath = $"ai-prefill/{requestId}/{fileName}";

                try
                {
                    using var buffer = new MemoryStream();
                    await fileStream.CopyToAsync(buffer, cancellationToken);
                    buffer.Position = 0;

                    var uploadResult = await _speFileStore.UploadSmallAsUserAsync(
                        httpContext,
                        stagingContainerId,
                        stagingPath,
                        buffer,
                        cancellationToken);

                    // Staging result tracked in batch summary below

                    buffer.Position = 0;
                    extractionResult = await _textExtractor.ExtractAsync(buffer, fileName, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to stage file '{FileName}' to SPE. Falling back to in-memory extraction. " +
                        "RequestId={RequestId}",
                        fileName, requestId);

                    using var fallbackBuffer = new MemoryStream();
                    await file.OpenReadStream().CopyToAsync(fallbackBuffer, cancellationToken);
                    fallbackBuffer.Position = 0;
                    extractionResult = await _textExtractor.ExtractAsync(fallbackBuffer, fileName, cancellationToken);
                }
            }
            else
            {
                _logger.LogDebug(
                    "No staging container configured. Extracting text from '{FileName}' in-memory. " +
                    "RequestId={RequestId}",
                    fileName, requestId);
                extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);
            }

            if (extractionResult.Success && !string.IsNullOrWhiteSpace(extractionResult.Text))
            {
                allExtractedText.AppendLine($"===== Document: {fileName} =====");
                allExtractedText.AppendLine(extractionResult.Text);
                allExtractedText.AppendLine();
                filesExtracted++;
            }
            else
            {
                filesFailed++;
            }
        }

        _logger.LogDebug(
            "Text extraction batch complete: {Extracted} succeeded, {Failed} failed, {Skipped} unsupported out of {Total} files. RequestId={RequestId}",
            filesExtracted, filesFailed, filesSkipped, files.Count, requestId);

        return allExtractedText.ToString();
    }

    /// <summary>
    /// Invokes the AI Playbook platform for structured matter field extraction.
    /// The playbook's extraction node (configured with a Skill prompt in Dataverse) handles
    /// the AI prompt and JSON schema — no hardcoded prompts in this code.
    ///
    /// Extracted text is passed via DocumentContext.ExtractedText (required by AiAnalysisNodeExecutor)
    /// and also in UserContext. Files haven't been registered as sprk_document records yet
    /// (pre-fill happens before matter creation), so DocumentId uses the requestId.
    /// </summary>
    private async Task<PreFillResponse> ExtractFieldsViaPlaybookAsync(
        string documentText,
        Guid requestId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Truncate to ~80KB to avoid excessive token usage (roughly 20K tokens)
        const int maxTextChars = 80_000;
        if (documentText.Length > maxTextChars)
        {
            _logger.LogDebug(
                "Truncating combined text from {Original} to {Truncated} chars. RequestId={RequestId}",
                documentText.Length, maxTextChars, requestId);
            documentText = documentText[..maxTextChars] + "\n\n[... content truncated ...]";
        }

        // Resolve playbook ID from configuration (allows per-environment override)
        var playbookIdStr = _configuration[PlaybookIdConfigKey];
        var playbookId = !string.IsNullOrEmpty(playbookIdStr) && Guid.TryParse(playbookIdStr, out var parsed)
            ? parsed
            : DefaultPreFillPlaybookId;

        _logger.LogInformation(
            "Invoking playbook for matter field extraction. PlaybookId={PlaybookId}, " +
            "TextLength={TextLength}, RequestId={RequestId}",
            playbookId, documentText.Length, requestId);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));

            var request = new PlaybookRunRequest
            {
                PlaybookId = playbookId,
                DocumentIds = [],
                UserContext = documentText,
                Document = new DocumentContext
                {
                    DocumentId = requestId,
                    Name = "Pre-fill upload",
                    ExtractedText = documentText,
                },
                Parameters = new Dictionary<string, string>
                {
                    ["entity_type"] = "matter",
                    ["extraction_mode"] = "pre-fill"
                }
            };

            // Consume the playbook SSE stream and collect the final node outputs
            string? preFillJson = null;
            double confidence = 0;

            await foreach (var evt in _playbookService.ExecuteAsync(request, httpContext, timeoutCts.Token))
            {
                // Look for the completed node output that contains pre-fill data
                if (evt.Type == PlaybookEventType.NodeCompleted && evt.NodeOutput != null)
                {
                    // Check if this node's output contains structured pre-fill data
                    if (evt.NodeOutput.StructuredData.HasValue)
                    {
                        preFillJson = evt.NodeOutput.StructuredData.Value.GetRawText();
                        confidence = evt.NodeOutput.Confidence ?? 0;

                        _logger.LogDebug(
                            "Received pre-fill data from node '{NodeName}'. Confidence={Confidence}. " +
                            "RequestId={RequestId}",
                            evt.NodeName, confidence, requestId);
                    }
                    // Fall back to text content if no structured data
                    else if (!string.IsNullOrWhiteSpace(evt.NodeOutput.TextContent))
                    {
                        preFillJson = evt.NodeOutput.TextContent;
                        confidence = evt.NodeOutput.Confidence ?? 0;
                    }
                }

                if (evt.Type == PlaybookEventType.RunFailed)
                {
                    _logger.LogWarning(
                        "Playbook execution failed for pre-fill. Error={Error}. RequestId={RequestId}",
                        evt.Error, requestId);
                    return PreFillResponse.Empty($"PLAYBOOK_FAILED: {evt.Error}");
                }
            }

            if (string.IsNullOrWhiteSpace(preFillJson))
            {
                _logger.LogWarning(
                    "Playbook completed but no pre-fill data was produced. RequestId={RequestId}",
                    requestId);
                return PreFillResponse.Empty("PLAYBOOK_NO_OUTPUT: completed but no structured/text data");
            }

            _logger.LogInformation(
                "Playbook extraction complete. ResponseLength={Length}, Confidence={Confidence}. " +
                "RequestId={RequestId}",
                preFillJson.Length, confidence, requestId);

            return ParseAiResponse(preFillJson, confidence, requestId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Playbook pre-fill request timed out after 45s. Returning empty response. RequestId={RequestId}",
                requestId);
            return PreFillResponse.Empty("TIMEOUT: playbook timed out after 45s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Playbook pre-fill call failed. Returning empty response. RequestId={RequestId}",
                requestId);
            return PreFillResponse.Empty($"EXCEPTION: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the AI JSON response into a PreFillResponse.
    /// Handles multiple response formats from the AI pipeline:
    /// 1. Direct pre-fill schema: {"matterName":"...","matterDescription":"...",...}
    /// 2. Entity extraction: {"entities":[{"name":"Matter Name","value":"..."},...]}
    /// 3. rawResponse wrapper: {"rawResponse":"{...inner JSON...}"}
    /// </summary>
    private PreFillResponse ParseAiResponse(string aiResponse, double overallConfidence, Guid requestId)
    {
        var json = StripMarkdownCodeFences(aiResponse.Trim());

        // Unwrap rawResponse envelope if present
        json = UnwrapRawResponse(json);

        // Try direct pre-fill schema first (preferred format from JPS)
        AiPreFillResult? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<AiPreFillResult>(json, AiJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Direct JSON deserialization failed (may be truncated). Trying entity extraction. RequestId={RequestId}",
                requestId);
        }

        // If direct parse produced no useful fields, try entity extraction format
        if (parsed == null || !HasAnyField(parsed))
        {
            _logger.LogInformation(
                "Direct schema parse found no fields; trying entity extraction format. RequestId={RequestId}",
                requestId);
            parsed = TryParseEntityExtractionFormat(json, overallConfidence, requestId);
        }

        // If strict JSON parsing failed (truncated response), try regex-based extraction
        if (parsed == null)
        {
            _logger.LogInformation(
                "Entity extraction parse also failed; trying regex-based extraction from partial JSON. RequestId={RequestId}",
                requestId);
            parsed = TryExtractFromPartialJson(json, overallConfidence, requestId);
        }

        if (parsed == null)
        {
            _logger.LogWarning(
                "AI response could not be parsed by any method. First 500 chars: '{Response}'. RequestId={RequestId}",
                json.Length > 500 ? json[..500] : json, requestId);
            return PreFillResponse.Empty($"PARSE_FAILED: {(json.Length > 500 ? json[..500] : json)}");
        }

        var result = BuildPreFillResponse(parsed, overallConfidence, requestId);
        // Attach raw AI response for debugging
        return result with { DebugRawAiResponse = $"OK: {(json.Length > 500 ? json[..500] : json)}" };
    }

    /// <summary>
    /// Unwraps a {"rawResponse": "..."} envelope if present (GenericAnalysisHandler wraps output this way).
    /// </summary>
    private static string UnwrapRawResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("rawResponse", out var inner) && inner.ValueKind == JsonValueKind.String)
            {
                return inner.GetString() ?? json;
            }
        }
        catch (JsonException) { }
        return json;
    }

    /// <summary>
    /// Returns true if at least one pre-fill field has a non-null value.
    /// </summary>
    private static bool HasAnyField(AiPreFillResult result) =>
        !string.IsNullOrWhiteSpace(result.MatterTypeName) ||
        !string.IsNullOrWhiteSpace(result.PracticeAreaName) ||
        !string.IsNullOrWhiteSpace(result.MatterName) ||
        !string.IsNullOrWhiteSpace(result.MatterDescription);

    /// <summary>
    /// Parses entity extraction format into AiPreFillResult by mapping entity names to pre-fill fields.
    /// Handles: {"entities":[{"name":"Matter Name","value":"..."},...], "keyValuePairs":[...]}
    /// </summary>
    private AiPreFillResult? TryParseEntityExtractionFormat(string json, double confidence, Guid requestId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Extract from "entities" array
            if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var entity in entities.EnumerateArray())
                {
                    var name = entity.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var value = entity.TryGetProperty("value", out var v) ? v.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                        fields.TryAdd(name, value);
                }
            }

            // Extract from "keyValuePairs" array
            if (root.TryGetProperty("keyValuePairs", out var kvPairs) && kvPairs.ValueKind == JsonValueKind.Array)
            {
                foreach (var kv in kvPairs.EnumerateArray())
                {
                    var key = kv.TryGetProperty("key", out var k) ? k.GetString() : null;
                    var value = kv.TryGetProperty("value", out var v) ? v.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                        fields.TryAdd(key, value);
                }
            }

            if (fields.Count == 0)
                return null;

            // Try to extract documentType for matter type inference
            var docType = root.TryGetProperty("documentType", out var dt) ? dt.GetString() : null;

            _logger.LogInformation(
                "Parsed entity extraction format: {FieldCount} fields, documentType='{DocType}'. RequestId={RequestId}",
                fields.Count, docType ?? "(none)", requestId);

            return new AiPreFillResult
            {
                MatterTypeName = MatchField(fields, "Matter Type", "matterType", "Type", "Category"),
                PracticeAreaName = MatchField(fields, "Practice Area", "practiceArea", "Area", "Practice"),
                MatterName = MatchField(fields, "Matter Name", "matterName", "Title", "Name", "Patent Title"),
                MatterDescription = MatchField(fields, "Description", "Summary", "Abstract", "matterDescription",
                    "Matter Description", "Document Summary"),
                AssignedAttorneyName = MatchField(fields, "Assigned Attorney", "Attorney", "assignedAttorney",
                    "Lead Attorney", "Responsible Attorney"),
                AssignedParalegalName = MatchField(fields, "Assigned Paralegal", "Paralegal", "assignedParalegal"),
                AssignedOutsideCounselName = MatchField(fields, "Outside Counsel", "outsideCounsel",
                    "assignedOutsideCounsel", "External Counsel"),
                Confidence = confidence
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Last-resort extraction from truncated/malformed JSON using regex.
    /// Extracts entity name/value pairs from the raw text even if JSON is incomplete.
    /// </summary>
    private AiPreFillResult? TryExtractFromPartialJson(string json, double confidence, Guid requestId)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Extract "name": "...", "value": "..." pairs from entity objects using regex
        var entityPattern = new Regex(
            @"""name""\s*:\s*""([^""]+)""\s*,\s*""(?:type|category)""\s*:\s*""[^""]*""\s*,\s*""value""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase);

        foreach (Match match in entityPattern.Matches(json))
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                fields.TryAdd(name, value);
        }

        // Also try simple "key": "value" patterns for top-level fields
        var kvPattern = new Regex(
            @"""(documentType|matterTypeName|practiceAreaName|matterName|matterDescription)""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase);

        foreach (Match match in kvPattern.Matches(json))
        {
            fields.TryAdd(match.Groups[1].Value, match.Groups[2].Value);
        }

        if (fields.Count == 0)
            return null;

        _logger.LogInformation(
            "Regex extraction from partial JSON found {FieldCount} fields. RequestId={RequestId}",
            fields.Count, requestId);

        return new AiPreFillResult
        {
            MatterTypeName = MatchField(fields, "Matter Type", "matterType", "matterTypeName", "Type", "Category", "documentType"),
            PracticeAreaName = MatchField(fields, "Practice Area", "practiceArea", "practiceAreaName", "Area", "Practice"),
            MatterName = MatchField(fields, "Matter Name", "matterName", "Title", "Name", "Patent Title", "Subject"),
            MatterDescription = MatchField(fields, "Description", "Summary", "Abstract", "matterDescription",
                "Matter Description", "Document Summary"),
            AssignedAttorneyName = MatchField(fields, "Assigned Attorney", "Attorney", "assignedAttorney",
                "assignedAttorneyName", "Lead Attorney", "Responsible Attorney"),
            AssignedParalegalName = MatchField(fields, "Assigned Paralegal", "Paralegal", "assignedParalegal",
                "assignedParalegalName"),
            AssignedOutsideCounselName = MatchField(fields, "Outside Counsel", "outsideCounsel",
                "assignedOutsideCounselName", "External Counsel"),
            Confidence = confidence
        };
    }

    /// <summary>
    /// Matches a field by trying multiple possible key names against the field map.
    /// </summary>
    private static string? MatchField(Dictionary<string, string> fields, params string[] possibleKeys)
    {
        foreach (var key in possibleKeys)
        {
            if (fields.TryGetValue(key, out var value))
                return value;
        }
        return null;
    }

    /// <summary>
    /// Builds the PreFillResponse from a successfully parsed AiPreFillResult.
    /// </summary>
    private PreFillResponse BuildPreFillResponse(AiPreFillResult parsed, double overallConfidence, Guid requestId)
    {
        var preFilledFields = new List<string>();
        if (!string.IsNullOrWhiteSpace(parsed.MatterTypeName)) preFilledFields.Add("matterTypeName");
        if (!string.IsNullOrWhiteSpace(parsed.PracticeAreaName)) preFilledFields.Add("practiceAreaName");
        if (!string.IsNullOrWhiteSpace(parsed.MatterName)) preFilledFields.Add("matterName");
        if (!string.IsNullOrWhiteSpace(parsed.MatterDescription)) preFilledFields.Add("summary");
        if (!string.IsNullOrWhiteSpace(parsed.AssignedAttorneyName)) preFilledFields.Add("assignedAttorneyName");
        if (!string.IsNullOrWhiteSpace(parsed.AssignedParalegalName)) preFilledFields.Add("assignedParalegalName");
        if (!string.IsNullOrWhiteSpace(parsed.AssignedOutsideCounselName)) preFilledFields.Add("assignedOutsideCounselName");

        var confidence = Math.Clamp(parsed.Confidence ?? overallConfidence, 0.0, 1.0);

        _logger.LogInformation(
            "Pre-fill result: FieldCount={Fields}, Confidence={Confidence}. RequestId={RequestId}",
            preFilledFields.Count, confidence, requestId);

        return new PreFillResponse(
            MatterTypeName: NormalizeNullableString(parsed.MatterTypeName),
            PracticeAreaName: NormalizeNullableString(parsed.PracticeAreaName),
            MatterName: NormalizeNullableString(parsed.MatterName),
            Summary: NormalizeNullableString(parsed.MatterDescription),
            AssignedAttorneyName: NormalizeNullableString(parsed.AssignedAttorneyName),
            AssignedParalegalName: NormalizeNullableString(parsed.AssignedParalegalName),
            AssignedOutsideCounselName: NormalizeNullableString(parsed.AssignedOutsideCounselName),
            Confidence: confidence,
            PreFilledFields: [.. preFilledFields]);
    }

    private static string? NormalizeNullableString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string StripMarkdownCodeFences(string text)
    {
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text[7..].TrimStart();
        }
        else if (text.StartsWith("```"))
        {
            text = text[3..].TrimStart();
        }

        if (text.EndsWith("```"))
        {
            text = text[..^3].TrimEnd();
        }

        return text;
    }

    // ─── Internal AI response model ───────────────────────────────────────────

    /// <summary>
    /// Internal DTO for deserializing the AI's structured JSON extraction response.
    /// Field names match the front-end IAiPrefillFields interface.
    /// </summary>
    private sealed class AiPreFillResult
    {
        [JsonPropertyName("matterTypeName")]
        public string? MatterTypeName { get; init; }

        [JsonPropertyName("practiceAreaName")]
        public string? PracticeAreaName { get; init; }

        [JsonPropertyName("matterName")]
        public string? MatterName { get; init; }

        [JsonPropertyName("matterDescription")]
        public string? MatterDescription { get; init; }

        [JsonPropertyName("assignedAttorneyName")]
        public string? AssignedAttorneyName { get; init; }

        [JsonPropertyName("assignedParalegalName")]
        public string? AssignedParalegalName { get; init; }

        [JsonPropertyName("assignedOutsideCounselName")]
        public string? AssignedOutsideCounselName { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }
    }
}
