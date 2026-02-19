using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Service that orchestrates matter AI pre-fill: stores uploaded files temporarily via SpeFileStore,
/// extracts text, calls the AI for structured matter field extraction, and returns a PreFillResponse.
/// </summary>
/// <remarks>
/// Follows ADR-007: File uploads routed through SpeFileStore facade — no direct SPE access.
/// Follows ADR-013: AI document analysis via IOpenAiClient (PlaybookService integration
/// is not suitable for ad-hoc structured JSON extraction; direct prompt-based approach used instead).
///
/// File storage lifecycle:
/// Files uploaded here are stored under a per-request staging prefix (ai-prefill/{requestId}/...).
/// They are available for later association when the matter record is created (task 024).
/// Cleanup of orphaned staging files is a separate concern (background job).
/// </remarks>
public class MatterPreFillService
{
    // Staging container is configured via appsettings (SharePointEmbedded:StagingContainerId).
    // If not configured, files are processed in-memory only (text extracted from IFormFile stream).
    private readonly SpeFileStore _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly IOpenAiClient _openAiClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatterPreFillService> _logger;

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
        IOpenAiClient openAiClient,
        IConfiguration configuration,
        ILogger<MatterPreFillService> logger)
    {
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
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
    /// extracts text from the documents, calls the AI for matter field extraction, and returns
    /// a structured PreFillResponse.
    ///
    /// On AI timeout or service unavailability, returns an empty PreFillResponse with confidence=0
    /// rather than propagating an error (graceful degradation as per spec).
    /// </summary>
    /// <param name="files">The validated uploaded files.</param>
    /// <param name="userId">The authenticated user ID (for staging path scoping and logging).</param>
    /// <param name="httpContext">The HTTP context (for SpeFileStore OBO calls).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>PreFillResponse with extracted field values, or empty response on AI failure.</returns>
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
        var allExtractedText = new StringBuilder();
        var stagingContainerId = _configuration["SharePointEmbedded:StagingContainerId"];
        var filesStagedInSpe = new List<string>();

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file.FileName);
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            // Open stream for text extraction (always done in-memory from the uploaded stream)
            using var fileStream = file.OpenReadStream();

            // Check if TextExtractor supports this extension
            if (!_textExtractor.IsSupported(extension))
            {
                _logger.LogWarning(
                    "Text extractor does not support extension '{Extension}' for file '{FileName}'. " +
                    "This file will be skipped. RequestId={RequestId}",
                    extension, fileName, requestId);
                continue;
            }

            TextExtractionResult extractionResult;
            if (!string.IsNullOrEmpty(stagingContainerId))
            {
                // Store in SPE staging area via SpeFileStore facade (ADR-007)
                var stagingPath = $"ai-prefill/{requestId}/{fileName}";

                try
                {
                    // Buffer the stream so we can re-read it after SPE upload
                    using var buffer = new MemoryStream();
                    await fileStream.CopyToAsync(buffer, cancellationToken);
                    buffer.Position = 0;

                    var uploadResult = await _speFileStore.UploadSmallAsUserAsync(
                        httpContext,
                        stagingContainerId,
                        stagingPath,
                        buffer,
                        cancellationToken);

                    if (uploadResult != null)
                    {
                        filesStagedInSpe.Add(stagingPath);
                        _logger.LogDebug(
                            "Staged file '{FileName}' to SPE path '{StagingPath}'. RequestId={RequestId}",
                            fileName, stagingPath, requestId);
                    }

                    // Re-read the buffer for text extraction
                    buffer.Position = 0;
                    extractionResult = await _textExtractor.ExtractAsync(buffer, fileName, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to stage file '{FileName}' to SPE. Falling back to in-memory extraction. " +
                        "RequestId={RequestId}",
                        fileName, requestId);

                    // Fall back to in-memory extraction if SPE staging fails
                    using var fallbackBuffer = new MemoryStream();
                    await file.OpenReadStream().CopyToAsync(fallbackBuffer, cancellationToken);
                    fallbackBuffer.Position = 0;
                    extractionResult = await _textExtractor.ExtractAsync(fallbackBuffer, fileName, cancellationToken);
                }
            }
            else
            {
                // No staging container configured — extract directly from in-memory stream
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

                _logger.LogDebug(
                    "Extracted {CharCount} characters from '{FileName}'. RequestId={RequestId}",
                    extractionResult.CharacterCount, fileName, requestId);
            }
            else
            {
                _logger.LogWarning(
                    "Text extraction failed for '{FileName}': {Error}. RequestId={RequestId}",
                    fileName, extractionResult.ErrorMessage, requestId);
            }
        }

        var combinedText = allExtractedText.ToString();

        if (string.IsNullOrWhiteSpace(combinedText))
        {
            _logger.LogWarning(
                "No text could be extracted from any uploaded files. Returning empty pre-fill. " +
                "RequestId={RequestId}",
                requestId);
            return PreFillResponse.Empty();
        }

        _logger.LogInformation(
            "Text extraction complete. TotalChars={TotalChars}, FilesStagedInSpe={StagedCount}. " +
            "RequestId={RequestId}",
            combinedText.Length, filesStagedInSpe.Count, requestId);

        // --- Step 2: Call AI for structured matter field extraction ---
        return await ExtractMatterFieldsWithAiAsync(combinedText, requestId, cancellationToken);
    }

    /// <summary>
    /// Calls the AI (via IOpenAiClient) with a structured extraction prompt and parses the response
    /// into a PreFillResponse. Returns an empty response on timeout or service failure.
    /// </summary>
    private async Task<PreFillResponse> ExtractMatterFieldsWithAiAsync(
        string documentText,
        Guid requestId,
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

        var prompt = BuildExtractionPrompt(documentText);

        _logger.LogInformation(
            "Calling AI for matter field extraction. PromptLength={PromptLength}, RequestId={RequestId}",
            prompt.Length, requestId);

        try
        {
            // Use a shorter timeout for pre-fill to keep the UX snappy
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            var aiResponse = await _openAiClient.GetCompletionAsync(
                prompt,
                model: null, // Use default configured model
                timeoutCts.Token);

            _logger.LogInformation(
                "AI response received for matter field extraction. ResponseLength={Length}, RequestId={RequestId}",
                aiResponse?.Length ?? 0, requestId);

            return ParseAiResponse(aiResponse, requestId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout (not client disconnect) — return graceful empty response per spec
            _logger.LogWarning(
                "AI pre-fill request timed out after 30s. Returning empty response. RequestId={RequestId}",
                requestId);
            return PreFillResponse.Empty();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AI pre-fill call failed. Returning empty response. RequestId={RequestId}",
                requestId);
            return PreFillResponse.Empty();
        }
    }

    /// <summary>
    /// Builds the structured extraction prompt instructing the AI to respond in JSON.
    /// The JSON schema matches the AiPreFillResult internal class for reliable parsing.
    /// </summary>
    private static string BuildExtractionPrompt(string documentText)
    {
        // Note: Using string concatenation instead of raw interpolated string to avoid
        // conflict between {{ JSON braces }} and $"" interpolation syntax in the schema template.
        const string schema =
            """
            {
              "matterType": "string or null - one of: Litigation, Transactional, Advisory, Regulatory, Compliance, Other",
              "practiceArea": "string or null - one of: Corporate, Employment Law, Real Estate, Intellectual Property, Litigation, Tax, Privacy & Data, Finance, Other",
              "matterName": "string or null - a concise descriptive name for this matter (max 100 chars)",
              "organization": "string or null - primary client or counterparty organization name",
              "estimatedBudget": "number or null - estimated budget amount in USD (e.g., 250000 for $250,000). Parse formats like 1M, one million, $1500000",
              "keyParties": "string or null - comma-separated list of key individuals and organizations",
              "summary": "string or null - 2-3 sentence summary of the matter context and key issues",
              "confidence": "number between 0.0 and 1.0 - your overall confidence in the extracted data"
            }
            """;

        return "You are a legal operations AI assistant. Analyze the following legal documents and extract matter information.\n\n" +
               "Return ONLY a valid JSON object matching this schema (no markdown, no explanation):\n" +
               schema + "\n" +
               "If you cannot extract a field with reasonable confidence, set it to null.\n" +
               "Do not guess or hallucinate values. Only extract what is clearly stated in the documents.\n\n" +
               "Documents to analyze:\n" +
               "---\n" +
               documentText + "\n" +
               "---";
    }

    /// <summary>
    /// Parses the AI JSON response into a PreFillResponse.
    /// Handles partial JSON, malformed responses, and builds the PreFilledFields array.
    /// </summary>
    private PreFillResponse ParseAiResponse(string? aiResponse, Guid requestId)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
        {
            _logger.LogWarning("AI returned empty response. RequestId={RequestId}", requestId);
            return PreFillResponse.Empty();
        }

        // Strip markdown code fences if the model included them
        var json = StripMarkdownCodeFences(aiResponse.Trim());

        try
        {
            var parsed = JsonSerializer.Deserialize<AiPreFillResult>(json, AiJsonOptions);
            if (parsed == null)
            {
                _logger.LogWarning(
                    "AI response deserialized to null. RequestId={RequestId}", requestId);
                return PreFillResponse.Empty();
            }

            // Build the list of successfully extracted (non-null) field names
            var preFilledFields = new List<string>();
            if (!string.IsNullOrWhiteSpace(parsed.MatterType)) preFilledFields.Add("matterType");
            if (!string.IsNullOrWhiteSpace(parsed.PracticeArea)) preFilledFields.Add("practiceArea");
            if (!string.IsNullOrWhiteSpace(parsed.MatterName)) preFilledFields.Add("matterName");
            if (!string.IsNullOrWhiteSpace(parsed.Organization)) preFilledFields.Add("organization");
            if (parsed.EstimatedBudget.HasValue) preFilledFields.Add("estimatedBudget");
            if (!string.IsNullOrWhiteSpace(parsed.KeyParties)) preFilledFields.Add("keyParties");
            if (!string.IsNullOrWhiteSpace(parsed.Summary)) preFilledFields.Add("summary");

            var confidence = Math.Clamp(parsed.Confidence ?? 0.0, 0.0, 1.0);

            _logger.LogInformation(
                "AI extraction complete. FieldsExtracted={Fields}, Confidence={Confidence}. RequestId={RequestId}",
                preFilledFields.Count, confidence, requestId);

            return new PreFillResponse(
                MatterType: NormalizeNullableString(parsed.MatterType),
                PracticeArea: NormalizeNullableString(parsed.PracticeArea),
                MatterName: NormalizeNullableString(parsed.MatterName),
                Organization: NormalizeNullableString(parsed.Organization),
                EstimatedBudget: parsed.EstimatedBudget,
                KeyParties: NormalizeNullableString(parsed.KeyParties),
                Summary: NormalizeNullableString(parsed.Summary),
                Confidence: confidence,
                PreFilledFields: [.. preFilledFields]);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse AI JSON response. Raw response (first 500 chars): '{Response}'. RequestId={RequestId}",
                aiResponse.Length > 500 ? aiResponse[..500] : aiResponse,
                requestId);
            return PreFillResponse.Empty();
        }
    }

    private static string? NormalizeNullableString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string StripMarkdownCodeFences(string text)
    {
        // Remove ```json ... ``` or ``` ... ``` wrappers
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
    /// </summary>
    private sealed class AiPreFillResult
    {
        [JsonPropertyName("matterType")]
        public string? MatterType { get; init; }

        [JsonPropertyName("practiceArea")]
        public string? PracticeArea { get; init; }

        [JsonPropertyName("matterName")]
        public string? MatterName { get; init; }

        [JsonPropertyName("organization")]
        public string? Organization { get; init; }

        [JsonPropertyName("estimatedBudget")]
        public decimal? EstimatedBudget { get; init; }

        [JsonPropertyName("keyParties")]
        public string? KeyParties { get; init; }

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }
    }
}
