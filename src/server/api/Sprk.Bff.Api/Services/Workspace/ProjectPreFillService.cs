using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Service that orchestrates project AI pre-fill: stores uploaded files temporarily via SpeFileStore,
/// extracts text, invokes the AI Playbook platform for structured project field extraction, and
/// returns a ProjectPreFillResponse.
/// </summary>
/// <remarks>
/// Follows the same pattern as MatterPreFillService but uses a project-specific playbook
/// and returns ProjectPreFillResponse with project field names.
/// </remarks>
public class ProjectPreFillService
{
    private readonly SpeFileStore _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly IPlaybookOrchestrationService _playbookService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProjectPreFillService> _logger;

    // Playbook configuration key — overridable via appsettings
    private const string PlaybookIdConfigKey = "Workspace:ProjectPreFillPlaybookId";

    // Default: "Create New Project Pre-Fill" playbook (Extract Project Fields — ACT-008, gpt-4o)
    private static readonly Guid DefaultPreFillPlaybookId =
        Guid.Parse("54cf6bb4-c018-f111-8343-7ced8d1dc988");

    // Reuse same file constraints as MatterPreFillService
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

    private static readonly JsonSerializerOptions AiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProjectPreFillService(
        SpeFileStore speFileStore,
        ITextExtractor textExtractor,
        IPlaybookOrchestrationService playbookService,
        IConfiguration configuration,
        ILogger<ProjectPreFillService> logger)
    {
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates the uploaded files against size and type constraints.
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

            if (!string.IsNullOrEmpty(file.ContentType) &&
                !AllowedContentTypes.Contains(file.ContentType))
            {
                errors.Add($"File '{file.FileName}' has unsupported Content-Type '{file.ContentType}'.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Extracts text from uploaded files, invokes the AI Playbook for structured project
    /// field extraction, and returns a ProjectPreFillResponse.
    /// </summary>
    public async Task<ProjectPreFillResponse> AnalyzeFilesAsync(
        IFormFileCollection files,
        string userId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();

        _logger.LogInformation(
            "Project AI pre-fill started. UserId={UserId}, FileCount={FileCount}, RequestId={RequestId}",
            userId, files.Count, requestId);

        var combinedText = await ExtractTextFromFilesAsync(files, requestId, httpContext, cancellationToken);

        if (string.IsNullOrWhiteSpace(combinedText))
        {
            _logger.LogWarning(
                "No text could be extracted from any uploaded files. Returning empty pre-fill. " +
                "RequestId={RequestId}",
                requestId);
            return ProjectPreFillResponse.Empty();
        }

        _logger.LogInformation(
            "Text extraction complete. TotalChars={TotalChars}. RequestId={RequestId}",
            combinedText.Length, requestId);

        return await ExtractFieldsViaPlaybookAsync(combinedText, requestId, httpContext, cancellationToken);
    }

    private async Task<string> ExtractTextFromFilesAsync(
        IFormFileCollection files,
        Guid requestId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var allExtractedText = new StringBuilder();
        var stagingContainerId = _configuration["SharePointEmbedded:StagingContainerId"];

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file.FileName);
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            using var fileStream = file.OpenReadStream();

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

                    if (uploadResult != null)
                    {
                        _logger.LogDebug(
                            "Staged file '{FileName}' to SPE path '{StagingPath}'. RequestId={RequestId}",
                            fileName, stagingPath, requestId);
                    }

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
                extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);
            }

            if (extractionResult.Success && !string.IsNullOrWhiteSpace(extractionResult.Text))
            {
                allExtractedText.AppendLine($"===== Document: {fileName} =====");
                allExtractedText.AppendLine(extractionResult.Text);
                allExtractedText.AppendLine();
            }
            else
            {
                _logger.LogWarning(
                    "Text extraction failed for '{FileName}': {Error}. RequestId={RequestId}",
                    fileName, extractionResult.ErrorMessage, requestId);
            }
        }

        return allExtractedText.ToString();
    }

    private async Task<ProjectPreFillResponse> ExtractFieldsViaPlaybookAsync(
        string documentText,
        Guid requestId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        const int maxTextChars = 80_000;
        if (documentText.Length > maxTextChars)
        {
            documentText = documentText[..maxTextChars] + "\n\n[... content truncated ...]";
        }

        var playbookIdStr = _configuration[PlaybookIdConfigKey];
        var playbookId = !string.IsNullOrEmpty(playbookIdStr) && Guid.TryParse(playbookIdStr, out var parsed)
            ? parsed
            : DefaultPreFillPlaybookId;

        _logger.LogInformation(
            "Invoking playbook for project field extraction. PlaybookId={PlaybookId}, " +
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
                    ["entity_type"] = "project",
                    ["extraction_mode"] = "pre-fill"
                }
            };

            string? preFillJson = null;
            double confidence = 0;

            await foreach (var evt in _playbookService.ExecuteAsync(request, httpContext, timeoutCts.Token))
            {
                if (evt.Type == PlaybookEventType.NodeCompleted && evt.NodeOutput != null)
                {
                    if (evt.NodeOutput.StructuredData.HasValue)
                    {
                        preFillJson = evt.NodeOutput.StructuredData.Value.GetRawText();
                        confidence = evt.NodeOutput.Confidence ?? 0;
                    }
                    else if (!string.IsNullOrWhiteSpace(evt.NodeOutput.TextContent))
                    {
                        preFillJson = evt.NodeOutput.TextContent;
                        confidence = evt.NodeOutput.Confidence ?? 0;
                    }
                }

                if (evt.Type == PlaybookEventType.RunFailed)
                {
                    _logger.LogWarning(
                        "Playbook execution failed for project pre-fill. Error={Error}. RequestId={RequestId}",
                        evt.Error, requestId);
                    return ProjectPreFillResponse.Empty();
                }
            }

            if (string.IsNullOrWhiteSpace(preFillJson))
            {
                _logger.LogWarning(
                    "Playbook completed but no pre-fill data was produced. RequestId={RequestId}",
                    requestId);
                return ProjectPreFillResponse.Empty();
            }

            return ParseAiResponse(preFillJson, confidence, requestId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Playbook pre-fill request timed out after 45s. Returning empty response. RequestId={RequestId}",
                requestId);
            return ProjectPreFillResponse.Empty();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Playbook pre-fill call failed. Returning empty response. RequestId={RequestId}",
                requestId);
            return ProjectPreFillResponse.Empty();
        }
    }

    private ProjectPreFillResponse ParseAiResponse(string aiResponse, double overallConfidence, Guid requestId)
    {
        var json = StripMarkdownCodeFences(aiResponse.Trim());

        try
        {
            var parsed = JsonSerializer.Deserialize<AiProjectPreFillResult>(json, AiJsonOptions);
            if (parsed == null)
            {
                _logger.LogWarning(
                    "AI response deserialized to null. RequestId={RequestId}", requestId);
                return ProjectPreFillResponse.Empty();
            }

            // Coalesce: playbook may emit "projectDescription" or "description"
            var description = parsed.ProjectDescription ?? parsed.Description;

            var preFilledFields = new List<string>();
            if (!string.IsNullOrWhiteSpace(parsed.ProjectTypeName)) preFilledFields.Add("projectTypeName");
            if (!string.IsNullOrWhiteSpace(parsed.PracticeAreaName)) preFilledFields.Add("practiceAreaName");
            if (!string.IsNullOrWhiteSpace(parsed.ProjectName)) preFilledFields.Add("projectName");
            if (!string.IsNullOrWhiteSpace(description)) preFilledFields.Add("description");
            if (!string.IsNullOrWhiteSpace(parsed.AssignedAttorneyName)) preFilledFields.Add("assignedAttorneyName");
            if (!string.IsNullOrWhiteSpace(parsed.AssignedParalegalName)) preFilledFields.Add("assignedParalegalName");
            if (!string.IsNullOrWhiteSpace(parsed.AssignedOutsideCounselName)) preFilledFields.Add("assignedOutsideCounselName");

            var confidence = Math.Clamp(parsed.Confidence ?? overallConfidence, 0.0, 1.0);

            return new ProjectPreFillResponse(
                ProjectTypeName: NormalizeNullableString(parsed.ProjectTypeName),
                PracticeAreaName: NormalizeNullableString(parsed.PracticeAreaName),
                ProjectName: NormalizeNullableString(parsed.ProjectName),
                Description: NormalizeNullableString(description),
                AssignedAttorneyName: NormalizeNullableString(parsed.AssignedAttorneyName),
                AssignedParalegalName: NormalizeNullableString(parsed.AssignedParalegalName),
                AssignedOutsideCounselName: NormalizeNullableString(parsed.AssignedOutsideCounselName),
                Confidence: confidence,
                PreFilledFields: [.. preFilledFields]);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse AI JSON response. Raw response (first 500 chars): '{Response}'. RequestId={RequestId}",
                aiResponse.Length > 500 ? aiResponse[..500] : aiResponse,
                requestId);
            return ProjectPreFillResponse.Empty();
        }
    }

    private static string? NormalizeNullableString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string StripMarkdownCodeFences(string text)
    {
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            text = text[7..].TrimStart();
        else if (text.StartsWith("```"))
            text = text[3..].TrimStart();

        if (text.EndsWith("```"))
            text = text[..^3].TrimEnd();

        return text;
    }

    private sealed class AiProjectPreFillResult
    {
        [JsonPropertyName("projectTypeName")]
        public string? ProjectTypeName { get; init; }

        [JsonPropertyName("practiceAreaName")]
        public string? PracticeAreaName { get; init; }

        [JsonPropertyName("projectName")]
        public string? ProjectName { get; init; }

        [JsonPropertyName("projectDescription")]
        public string? ProjectDescription { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

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
