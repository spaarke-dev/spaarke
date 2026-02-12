using System.Diagnostics;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.RecordMatching;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for classifying email attachments as invoice candidates.
/// Downloads attachment from SPE, extracts text, then runs AI classification (Playbook A).
///
/// Follows ADR-004 for job contract patterns and idempotency requirements.
/// Follows ADR-013 for AI via BFF extension.
/// Follows ADR-014 for playbook-based prompts.
/// Follows ADR-015: NEVER log document content, extracted text, or AI prompts.
/// </summary>
public class AttachmentClassificationJobHandler : IJobHandler
{
    private readonly IInvoiceAnalysisService _invoiceAnalysisService;
    private readonly ISpeFileOperations _speFileOperations;
    private readonly TextExtractorService _textExtractorService;
    private readonly IDataverseService _dataverseService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IRecordMatchService _recordMatchService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<AttachmentClassificationJobHandler> _logger;

    /// <summary>
    /// Job type constant - must match the JobType used when enqueuing classification jobs.
    /// </summary>
    public const string JobTypeName = "AttachmentClassification";

    // Invoice review status choice values (sprk_invoicereviewstatus)
    private const int ReviewStatusToReview = 100000000;

    public AttachmentClassificationJobHandler(
        IInvoiceAnalysisService invoiceAnalysisService,
        ISpeFileOperations speFileOperations,
        TextExtractorService textExtractorService,
        IDataverseService dataverseService,
        IIdempotencyService idempotencyService,
        IRecordMatchService recordMatchService,
        FinanceTelemetry telemetry,
        ILogger<AttachmentClassificationJobHandler> logger)
    {
        _invoiceAnalysisService = invoiceAnalysisService ?? throw new ArgumentNullException(nameof(invoiceAnalysisService));
        _speFileOperations = speFileOperations ?? throw new ArgumentNullException(nameof(speFileOperations));
        _textExtractorService = textExtractorService ?? throw new ArgumentNullException(nameof(textExtractorService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _recordMatchService = recordMatchService ?? throw new ArgumentNullException(nameof(recordMatchService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        using var activity = _telemetry.StartActivity(
            "AttachmentClassification.ProcessJob",
            correlationId: job.CorrelationId);

        var documentId = string.Empty;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing attachment classification job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Step 1: Parse payload to get documentId, driveId, itemId
            var payload = ParsePayload(job.Payload);
            if (payload == null)
            {
                _logger.LogError("Invalid payload for attachment classification job {JobId}", job.JobId);
                _telemetry.RecordClassificationFailure(stopwatch, job.SubjectId, "invalid_payload");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            documentId = payload.DocumentId;
            var driveId = payload.DriveId;
            var itemId = payload.ItemId;

            if (string.IsNullOrEmpty(documentId) || string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId))
            {
                _logger.LogError(
                    "Missing required payload fields for attachment classification job {JobId}: DocumentId={HasDocumentId}, DriveId={HasDriveId}, ItemId={HasItemId}",
                    job.JobId, !string.IsNullOrEmpty(documentId), !string.IsNullOrEmpty(driveId), !string.IsNullOrEmpty(itemId));
                _telemetry.RecordClassificationFailure(stopwatch, documentId ?? "unknown", "missing_payload_fields");
                return JobOutcome.Poisoned(job.JobId, JobType, "Missing required payload fields", job.Attempt, stopwatch.Elapsed);
            }

            // Step 2: Check idempotency - prevent duplicate classification
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"classify-{documentId}-attachment";
            }

            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "Document {DocumentId} already classified (idempotency key: {IdempotencyKey})",
                    documentId, idempotencyKey);

                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(5), ct))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for document {DocumentId} (idempotency key: {IdempotencyKey})",
                    documentId, idempotencyKey);

                // Return success to prevent retry - another instance is processing
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                var classificationStopwatch = _telemetry.RecordClassificationStart(documentId);

                // Step 3: Get file metadata to determine file name for text extraction
                var metadata = await _speFileOperations.GetFileMetadataAsync(driveId, itemId, ct);
                if (metadata == null)
                {
                    _logger.LogError(
                        "Could not retrieve file metadata for document {DocumentId} (DriveId={DriveId}, ItemId={ItemId})",
                        documentId, driveId, itemId);
                    _telemetry.RecordClassificationFailure(classificationStopwatch, documentId, "metadata_not_found");
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        "Could not retrieve file metadata from SPE",
                        job.Attempt, stopwatch.Elapsed);
                }

                var fileName = metadata.Name;

                // Step 4: Download attachment from SPE
                var fileStream = await _speFileOperations.DownloadFileAsync(driveId, itemId, ct);
                if (fileStream == null)
                {
                    _logger.LogError(
                        "Could not download file for document {DocumentId} (DriveId={DriveId}, ItemId={ItemId})",
                        documentId, driveId, itemId);
                    _telemetry.RecordClassificationFailure(classificationStopwatch, documentId, "download_failed");
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        "Could not download file from SPE",
                        job.Attempt, stopwatch.Elapsed);
                }

                // Step 5: Extract text via TextExtractorService
                // ADR-015: Do NOT log extracted text content
                string? extractedText;
                await using (fileStream)
                {
                    var extractionResult = await _textExtractorService.ExtractAsync(fileStream, fileName, ct);

                    if (!extractionResult.Success || string.IsNullOrWhiteSpace(extractionResult.Text))
                    {
                        // Text extraction failed - classify as Unknown with confidence=0
                        _logger.LogWarning(
                            "Text extraction failed for document {DocumentId} (file: {FileName}). Classifying as Unknown.",
                            documentId, fileName);

                        await WriteClassificationToDataverseAsync(
                            documentId,
                            DocumentClassification.Unknown,
                            confidence: 0m,
                            hintsJson: null,
                            matterSuggestions: [],
                            ct);

                        await _idempotencyService.MarkEventAsProcessedAsync(
                            idempotencyKey, TimeSpan.FromDays(7), ct);

                        _telemetry.RecordClassificationSuccess(classificationStopwatch, documentId, "Unknown");

                        _logger.LogInformation(
                            "Attachment classification job {JobId} completed (Unknown - text extraction failed) for document {DocumentId} in {Duration}ms",
                            job.JobId, documentId, stopwatch.ElapsedMilliseconds);

                        return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
                    }

                    extractedText = extractionResult.Text;
                }

                _logger.LogDebug(
                    "Text extraction succeeded for document {DocumentId}: {CharCount} characters",
                    documentId, extractedText.Length);

                // Step 6: Call AI classification via IInvoiceAnalysisService
                // ADR-015: Do NOT log the extracted text or AI prompts
                var classificationResult = await _invoiceAnalysisService.ClassifyAttachmentAsync(extractedText, ct);

                _logger.LogInformation(
                    "Classification result for document {DocumentId}: {Classification} (confidence: {Confidence})",
                    documentId, classificationResult.Classification, classificationResult.Confidence);

                // Step 6a: Entity matching - run AFTER AI classification
                // Aggregate signals from hints, parent context, and keyword matching
                var matterSuggestions = await PerformEntityMatchingAsync(
                    documentId,
                    classificationResult.Hints,
                    ct);

                _logger.LogInformation(
                    "Entity matching found {Count} matter suggestions for document {DocumentId}",
                    matterSuggestions.Count, documentId);

                // Step 7: Serialize hints to JSON for Dataverse storage
                string? hintsJson = null;
                if (classificationResult.Hints != null)
                {
                    hintsJson = JsonSerializer.Serialize(classificationResult.Hints, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
                }

                // Step 8: Write classification results and matter suggestions to Dataverse
                await WriteClassificationToDataverseAsync(
                    documentId,
                    classificationResult.Classification,
                    classificationResult.Confidence,
                    hintsJson,
                    matterSuggestions,
                    ct);

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey, TimeSpan.FromDays(7), ct);

                _telemetry.RecordClassificationSuccess(
                    classificationStopwatch, documentId, classificationResult.Classification.ToString());

                _logger.LogInformation(
                    "Attachment classification job {JobId} completed for document {DocumentId} in {Duration}ms. Classification={Classification}, Confidence={Confidence}",
                    job.JobId, documentId, stopwatch.ElapsedMilliseconds,
                    classificationResult.Classification, classificationResult.Confidence);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }
            finally
            {
                // Always release the lock
                await _idempotencyService.ReleaseProcessingLockAsync(idempotencyKey, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attachment classification job {JobId} failed: {Error}", job.JobId, ex.Message);

            var isRetryable = IsRetryableException(ex);
            _telemetry.RecordClassificationFailure(stopwatch, documentId ?? "unknown", isRetryable ? "transient_error" : "permanent_error");

            if (isRetryable && job.Attempt < job.MaxAttempts)
            {
                _logger.LogWarning(
                    "Document {DocumentId} classification failed (attempt {Attempt}/{MaxAttempts}), will retry: {Error}",
                    documentId, job.Attempt, job.MaxAttempts, ex.Message);
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Perform entity matching to find matter candidates based on classification hints,
    /// parent email context, and keyword overlap.
    /// Aggregates signals per matter and returns ranked list by confidence.
    /// </summary>
    private async Task<List<MatterSuggestion>> PerformEntityMatchingAsync(
        string documentId,
        InvoiceHints? hints,
        CancellationToken ct)
    {
        var matterCandidates = new Dictionary<string, MatterCandidate>();

        // Signal 1: Reference number match (confidence: 0.95)
        if (hints?.MatterReference != null && !string.IsNullOrWhiteSpace(hints.MatterReference))
        {
            var refRequest = new RecordMatchRequest
            {
                ReferenceNumbers = [hints.MatterReference],
                RecordTypeFilter = "sprk_matter",
                MaxResults = 10
            };

            var refMatches = await _recordMatchService.MatchAsync(refRequest, ct);

            foreach (var match in refMatches.Suggestions)
            {
                AddOrUpdateCandidate(
                    matterCandidates,
                    match.RecordId,
                    0.95,
                    "ReferenceNumberMatch");

                _logger.LogDebug(
                    "Reference match for document {DocumentId}: Matter {MatterId} via '{Signal}'",
                    documentId, match.RecordId, "ReferenceNumberMatch");
            }
        }

        // Signal 2: Vendor organization match (confidence: 0.60-0.85 depending on match quality)
        if (hints?.VendorName != null && !string.IsNullOrWhiteSpace(hints.VendorName))
        {
            var vendorRequest = new RecordMatchRequest
            {
                Organizations = [hints.VendorName],
                RecordTypeFilter = "sprk_matter",
                MaxResults = 10
            };

            var vendorMatches = await _recordMatchService.MatchAsync(vendorRequest, ct);

            foreach (var match in vendorMatches.Suggestions)
            {
                // Use the RecordMatchService confidence score (0.60-0.85 range)
                var confidence = match.ConfidenceScore;
                AddOrUpdateCandidate(
                    matterCandidates,
                    match.RecordId,
                    confidence,
                    "VendorOrgMatch");

                _logger.LogDebug(
                    "Vendor match for document {DocumentId}: Matter {MatterId} via '{Signal}' (confidence: {Confidence})",
                    documentId, match.RecordId, "VendorOrgMatch", confidence);
            }
        }

        // Signal 3: Parent email context (confidence: 0.90)
        // Load parent document to get its matterId
        var parentMatterId = await GetParentDocumentMatterIdAsync(documentId, ct);
        if (parentMatterId != null)
        {
            AddOrUpdateCandidate(
                matterCandidates,
                parentMatterId,
                0.90,
                "ParentEmailContext");

            _logger.LogDebug(
                "Parent context match for document {DocumentId}: Matter {MatterId} via '{Signal}'",
                documentId, parentMatterId, "ParentEmailContext");
        }

        // Signal 4: Keyword overlap matching (confidence: 0.40-0.60)
        // Extract keywords from hints for keyword matching
        var keywords = new List<string>();
        if (hints?.VendorName != null)
            keywords.Add(hints.VendorName);
        if (hints?.InvoiceNumber != null)
            keywords.Add(hints.InvoiceNumber);

        if (keywords.Count > 0)
        {
            var keywordRequest = new RecordMatchRequest
            {
                Keywords = keywords,
                RecordTypeFilter = "sprk_matter",
                MaxResults = 10
            };

            var keywordMatches = await _recordMatchService.MatchAsync(keywordRequest, ct);

            foreach (var match in keywordMatches.Suggestions)
            {
                // Keyword matching has lower confidence (0.40-0.60)
                var confidence = Math.Min(0.60, match.ConfidenceScore);
                AddOrUpdateCandidate(
                    matterCandidates,
                    match.RecordId,
                    confidence,
                    "KeywordOverlap");

                _logger.LogDebug(
                    "Keyword match for document {DocumentId}: Matter {MatterId} via '{Signal}' (confidence: {Confidence})",
                    documentId, match.RecordId, "KeywordOverlap", confidence);
            }
        }

        // Aggregate and rank candidates
        var rankedSuggestions = matterCandidates.Values
            .OrderByDescending(c => c.MaxConfidence)
            .Select(c => new MatterSuggestion
            {
                MatterId = c.MatterId,
                Confidence = Math.Round(c.MaxConfidence, 2),
                Signal = c.BestSignal
            })
            .ToList();

        return rankedSuggestions;
    }

    /// <summary>
    /// Add or update a matter candidate with a new signal.
    /// Takes the max confidence per matter across all signals.
    /// </summary>
    private static void AddOrUpdateCandidate(
        Dictionary<string, MatterCandidate> candidates,
        string matterId,
        double confidence,
        string signal)
    {
        if (!candidates.TryGetValue(matterId, out var candidate))
        {
            candidate = new MatterCandidate { MatterId = matterId };
            candidates[matterId] = candidate;
        }

        if (confidence > candidate.MaxConfidence)
        {
            candidate.MaxConfidence = confidence;
            candidate.BestSignal = signal;
        }
    }

    /// <summary>
    /// Get the Matter ID from the parent document (if this is an email attachment).
    /// Returns null if no parent document or parent has no matter association.
    /// </summary>
    private async Task<string?> GetParentDocumentMatterIdAsync(string documentId, CancellationToken ct)
    {
        try
        {
            // Load current document to get parent lookup
            var currentDoc = await _dataverseService.GetDocumentAsync(documentId, ct);
            if (currentDoc?.ParentDocumentId == null)
            {
                return null;
            }

            // Load parent document to get its matter association
            var parentDoc = await _dataverseService.GetDocumentAsync(currentDoc.ParentDocumentId, ct);
            return parentDoc?.MatterId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load parent document matter for document {DocumentId}: {Error}",
                documentId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Write classification results to Dataverse sprk_document fields.
    /// Fields: sprk_classification, sprk_classificationconfidence, sprk_invoicehintsjson,
    /// sprk_mattersuggestionjson, sprk_mattersuggestedref.
    /// Sets sprk_invoicereviewstatus=ToReview for InvoiceCandidate and Unknown classifications.
    /// NEVER creates Invoice or BillingEvent records.
    /// </summary>
    private async Task WriteClassificationToDataverseAsync(
        string documentId,
        DocumentClassification classification,
        decimal confidence,
        string? hintsJson,
        List<MatterSuggestion> matterSuggestions,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, object?>
        {
            ["sprk_classification"] = classification.ToString(),
            ["sprk_classificationconfidence"] = confidence,
            ["sprk_invoicehintsjson"] = hintsJson
        };

        // Serialize matter suggestions to JSON array (top 5)
        if (matterSuggestions.Count > 0)
        {
            var suggestionsJson = JsonSerializer.Serialize(
                matterSuggestions.Take(5).ToList(),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

            fields["sprk_mattersuggestionjson"] = suggestionsJson;

            // Store best match reference text (for display)
            var bestMatch = matterSuggestions.First();
            fields["sprk_mattersuggestedref"] = bestMatch.Signal;

            // ADR-015: Log only IDs and confidence scores, never content
            _logger.LogInformation(
                "Stored {Count} matter suggestions for document {DocumentId}: BestMatch={MatterId} (confidence: {Confidence})",
                matterSuggestions.Count, documentId, bestMatch.MatterId, bestMatch.Confidence);
        }

        // Set review status for classifications that need human review
        if (classification is DocumentClassification.InvoiceCandidate or DocumentClassification.Unknown)
        {
            fields["sprk_invoicereviewstatus"] = ReviewStatusToReview;
        }

        _logger.LogDebug(
            "Writing classification to Dataverse for document {DocumentId}: Classification={Classification}, FieldCount={FieldCount}",
            documentId, classification, fields.Count);

        await _dataverseService.UpdateDocumentFieldsAsync(documentId, fields, ct);

        _logger.LogInformation(
            "Classification written to Dataverse for document {DocumentId}: {Classification} (confidence: {Confidence})",
            documentId, classification, confidence);
    }

    private AttachmentClassificationPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<AttachmentClassificationPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse attachment classification job payload");
            return null;
        }
    }

    private static bool IsRetryableException(Exception ex)
    {
        if (ex is HttpRequestException)
            return true;

        var exceptionName = ex.GetType().Name;
        return exceptionName.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Timeout", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Payload structure for attachment classification jobs.
/// </summary>
public class AttachmentClassificationPayload
{
    /// <summary>
    /// The Dataverse document ID to classify.
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// The SPE drive ID where the file is stored.
    /// </summary>
    public string? DriveId { get; set; }

    /// <summary>
    /// The SPE item ID of the file.
    /// </summary>
    public string? ItemId { get; set; }
}

/// <summary>
/// Matter suggestion record for JSON serialization to sprk_mattersuggestionjson.
/// </summary>
internal record MatterSuggestion
{
    public required string MatterId { get; init; }
    public double Confidence { get; init; }
    public required string Signal { get; init; }
}

/// <summary>
/// Internal candidate tracking for matter aggregation.
/// </summary>
internal class MatterCandidate
{
    public required string MatterId { get; init; }
    public double MaxConfidence { get; set; }
    public string BestSignal { get; set; } = string.Empty;
}
