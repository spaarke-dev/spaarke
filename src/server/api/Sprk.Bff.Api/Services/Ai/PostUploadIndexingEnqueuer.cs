using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Default implementation of <see cref="IPostUploadIndexingEnqueuer"/>.
/// Extracts the canonical enqueue pattern previously duplicated across
/// <c>UploadFinalizationWorker</c>, <c>IncomingCommunicationProcessor</c>,
/// <c>AnalysisResultPersistence</c>, <c>DeliverToIndexNodeExecutor</c>,
/// <c>KnowledgeBaseEndpoints</c>, and <c>RagEndpoints.IndexFile</c>.
/// </summary>
/// <remarks>
/// <para>
/// Lifetime: <b>Scoped</b>. Matches <see cref="JobSubmissionService"/> +
/// downstream consumer expectations (per-request DI graph).
/// </para>
/// <para>
/// See <c>projects/spaarke-multi-container-multi-index-r1/notes/upload-indexing-centralization-design.md</c>
/// §3 + §4 for the full architecture + 11 fail-protection features.
/// </para>
/// </remarks>
public sealed class PostUploadIndexingEnqueuer : IPostUploadIndexingEnqueuer
{
    // Content-type prefixes that have no extractable text (skip enqueue).
    private static readonly string[] SkipPrefixes =
    {
        "video/",
        "audio/",
    };

    // Exact content types that are non-indexable archives (skip enqueue).
    private static readonly HashSet<string> SkipExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/zip",
        "application/x-zip-compressed",
        "application/x-rar-compressed",
        "application/vnd.rar",
        "application/x-7z-compressed",
        "application/x-tar",
        "application/gzip",
    };

    // Extensions to skip when content-type is octet-stream or missing (binaries that
    // can't be text-indexed). Note: .msg / .eml are NOT in this list — they may carry
    // extractable text. .pdf is NOT here — OCR may still work for scanned PDFs.
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bin", ".iso", ".dmg", ".img", ".so", ".dylib",
    };

    private readonly JobSubmissionService _jobSubmissionService;
    private readonly IOptions<PostUploadIndexingOptions> _options;
    private readonly ILogger<PostUploadIndexingEnqueuer> _logger;

    public PostUploadIndexingEnqueuer(
        JobSubmissionService jobSubmissionService,
        IOptions<PostUploadIndexingOptions> options,
        ILogger<PostUploadIndexingEnqueuer> logger)
    {
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PostUploadIndexingResult> EnqueueIfApplicableAsync(
        PostUploadIndexingRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var opts = _options.Value;

        // 1. Feature flag — emergency disable (§4.10)
        if (!opts.PostUploadEnqueueEnabled)
        {
            _logger.LogInformation(
                "Skipping RAG enqueue: feature flag Indexing:PostUploadEnqueueEnabled is off " +
                "(File={FileName} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("FeatureFlagDisabled");
        }

        // 2. Tenant context required (§4.7) — missing indicates misconfigured upload path
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogError(
                "Skipping RAG enqueue: TenantId is missing — likely misconfigured upload path " +
                "(File={FileName} DriveId={DriveId} ItemId={ItemId} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.DriveId, request.ItemId, request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("MissingTenantId");
        }

        // 3. Required SPE identifiers
        if (string.IsNullOrWhiteSpace(request.DriveId) || string.IsNullOrWhiteSpace(request.ItemId))
        {
            _logger.LogWarning(
                "Skipping RAG enqueue: DriveId or ItemId is empty " +
                "(File={FileName} DriveId={DriveId} ItemId={ItemId} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.DriveId, request.ItemId, request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("MissingSpeIdentifiers");
        }

        // 4-5. Size-based skips only run when caller knows the size (§4.4, §4.5).
        if (request.FileSizeBytes.HasValue)
        {
            if (request.FileSizeBytes.Value == 0)
            {
                _logger.LogInformation(
                    "Skipping RAG enqueue: file is empty " +
                    "(File={FileName} Source={Source} CorrelationId={CorrelationId})",
                    request.FileName, request.Source, request.CorrelationId);
                return PostUploadIndexingResult.Skipped("EmptyFile");
            }

            if (request.FileSizeBytes.Value > opts.MaxIndexableBytes)
            {
                _logger.LogInformation(
                    "Skipping RAG enqueue: file exceeds MaxIndexableBytes " +
                    "(File={FileName} SizeBytes={SizeBytes} MaxBytes={MaxBytes} Source={Source} CorrelationId={CorrelationId})",
                    request.FileName, request.FileSizeBytes.Value, opts.MaxIndexableBytes, request.Source, request.CorrelationId);
                return PostUploadIndexingResult.Skipped("FileTooLarge");
            }
        }

        // 6. Content-type / extension skip-list (§4.3)
        if (IsNonIndexableContent(request.ContentType, request.FileName))
        {
            _logger.LogInformation(
                "Skipping RAG enqueue: content type / extension is non-indexable " +
                "(File={FileName} ContentType={ContentType} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.ContentType ?? "(null)", request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("NonIndexableContentType");
        }

        // 7. Build + submit the job. Non-fatal try/catch — RAG indexing must NEVER fail the upload (§4.1)
        try
        {
            var jobPayload = new RagIndexingJobPayload
            {
                TenantId = request.TenantId,
                DriveId = request.DriveId,
                ItemId = request.ItemId,
                FileName = request.FileName,
                DocumentId = request.DocumentId,
                ParentEntity = request.ParentEntity,
                SearchIndexName = request.SearchIndexName, // null is OK — handler runs ISearchIndexNameResolver chain (§4.6)
                Source = request.Source,
                EnqueuedAt = DateTimeOffset.UtcNow,
            };

            var job = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = request.DocumentId ?? request.ItemId,
                CorrelationId = request.CorrelationId,
                IdempotencyKey = $"rag-index-{request.DriveId}-{request.ItemId}", // §4.2
                Attempt = 1,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(jobPayload)),
            };

            await _jobSubmissionService.SubmitJobAsync(job, ct);

            // §4.9 observability — single structured log line per successful enqueue
            _logger.LogInformation(
                "[PostUploadIndexingEnqueuer] Enqueued RAG indexing job {JobId} for {FileName} " +
                "(DriveId={DriveId} ItemId={ItemId} DocumentId={DocumentId} " +
                "SearchIndexName={SearchIndexName} Source={Source} TenantId={TenantId} CorrelationId={CorrelationId})",
                job.JobId,
                request.FileName,
                request.DriveId,
                request.ItemId,
                request.DocumentId ?? "(none)",
                request.SearchIndexName ?? "(resolver-pending)",
                request.Source,
                request.TenantId,
                request.CorrelationId);

            return PostUploadIndexingResult.Submitted(job.JobId);
        }
        catch (Exception ex)
        {
            // §4.1 non-fatal: log + swallow. The SPE upload already succeeded; indexing is best-effort.
            // Operator can re-trigger manually via POST /api/ai/rag/index-file (§4.11).
            _logger.LogWarning(ex,
                "[PostUploadIndexingEnqueuer] Failed to enqueue RAG indexing for {FileName} " +
                "(DriveId={DriveId} ItemId={ItemId} Source={Source} CorrelationId={CorrelationId}): {Error}",
                request.FileName,
                request.DriveId,
                request.ItemId,
                request.Source,
                request.CorrelationId,
                ex.Message);
            return PostUploadIndexingResult.Failed(ex.GetType().Name);
        }
    }

    /// <summary>
    /// Returns true if the content type / file extension indicates the file has no
    /// extractable text and should not be enqueued for indexing.
    /// </summary>
    /// <remarks>
    /// <para>Conservative: when in doubt, returns false (let the handler attempt extraction).</para>
    /// <para>.msg / .eml / .pdf are intentionally NOT skipped — see §8.5.2 of the design doc.</para>
    /// </remarks>
    private static bool IsNonIndexableContent(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            foreach (var prefix in SkipPrefixes)
            {
                if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (SkipExact.Contains(contentType))
            {
                return true;
            }
        }

        // If content-type is missing or octet-stream, fall back to extension skip-list for known binaries.
        var isOctetStreamOrMissing = string.IsNullOrWhiteSpace(contentType) ||
            string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase);

        if (isOctetStreamOrMissing && !string.IsNullOrWhiteSpace(fileName))
        {
            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext) && SkipExtensions.Contains(ext))
            {
                return true;
            }
        }

        return false;
    }
}
