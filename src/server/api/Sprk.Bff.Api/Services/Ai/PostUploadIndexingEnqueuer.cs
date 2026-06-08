using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Default implementation of <see cref="IPostUploadIndexingEnqueuer"/>.
/// Dispatches RAG indexing synchronously in the OBO request scope via
/// <see cref="IFileIndexingService.IndexFileAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// **History**: Originally (Phase 1 — commit fd9dda7d) this helper enqueued
/// a Service Bus job via <c>JobSubmissionService.SubmitJobAsync</c> consumed by
/// <c>RagIndexingJobHandler</c> running under MI auth. UAT (2026-06-08) revealed
/// the MI handler 403'd on user-OBO-uploaded files — SPE container ACLs include
/// the writer's identity, and Spaarke's MI is intentionally NOT registered on
/// the container type as a guest app (per <c>sdap-auth-patterns.md</c> Pattern 4
/// + <c>managed-identity-resource-rbac.md</c>).
/// </para>
/// <para>
/// **Fix**: dispatch synchronously inline using <see cref="IFileIndexingService.IndexFileAsync"/>
/// which uses OBO (user's token, threaded through <see cref="HttpContext"/>). This is the same
/// path that the production <c>/api/ai/rag/index-file</c> and <c>/api/ai/rag/send-to-index</c>
/// endpoints have always used — and that DocumentUploadWizard's <c>uploadOrchestrator.triggerRagIndexing</c>
/// already calls. The 4 Create* wizards (Matter / Project / WorkAssignment / Event) now converge on
/// this same proven OBO indexing path through this helper.
/// </para>
/// <para>
/// **Trade-off**: upload request now waits ~5-10 s for indexing to complete (chunk + embed + write
/// to AI Search). The "fast response + async indexing" goal of Phase 1's design is impossible to
/// honor with the existing SPE permission model — async would require either MI guest-app
/// registration on the container type (architectural deviation from Pattern 4) or OBO token
/// persistence (complex; tokens expire).
/// </para>
/// <para>
/// **Lifetime**: <b>Scoped</b> (changed from Singleton in Phase 1). Required because
/// <see cref="IFileIndexingService"/> is scoped — it depends on per-request services.
/// </para>
/// </remarks>
public sealed class PostUploadIndexingEnqueuer : IPostUploadIndexingEnqueuer
{
    // Content-type prefixes that have no extractable text (skip indexing).
    private static readonly string[] SkipPrefixes =
    {
        "video/",
        "audio/",
    };

    // Exact content types that are non-indexable archives.
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

    // Extensions to skip when content-type is octet-stream or missing.
    // NOTE: .msg / .eml / .pdf are intentionally NOT in this list.
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bin", ".iso", ".dmg", ".img", ".so", ".dylib",
    };

    private readonly IFileIndexingService _fileIndexingService;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly IOptions<PostUploadIndexingOptions> _options;
    private readonly ILogger<PostUploadIndexingEnqueuer> _logger;

    public PostUploadIndexingEnqueuer(
        IFileIndexingService fileIndexingService,
        JobSubmissionService jobSubmissionService,
        IOptions<PostUploadIndexingOptions> options,
        ILogger<PostUploadIndexingEnqueuer> logger)
    {
        _fileIndexingService = fileIndexingService ?? throw new ArgumentNullException(nameof(fileIndexingService));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PostUploadIndexingResult> EnqueueIfApplicableAsync(
        PostUploadIndexingRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpContext);

        var opts = _options.Value;

        // 1. Feature flag — emergency disable
        if (!opts.PostUploadEnqueueEnabled)
        {
            _logger.LogInformation(
                "Skipping RAG indexing: feature flag Indexing:PostUploadEnqueueEnabled is off " +
                "(File={FileName} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("FeatureFlagDisabled");
        }

        // 2. Tenant context required — missing indicates misconfigured upload path
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogError(
                "Skipping RAG indexing: TenantId is missing — likely misconfigured upload path " +
                "(File={FileName} DriveId={DriveId} ItemId={ItemId} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.DriveId, request.ItemId, request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("MissingTenantId");
        }

        // 3. Required SPE identifiers
        if (string.IsNullOrWhiteSpace(request.DriveId) || string.IsNullOrWhiteSpace(request.ItemId))
        {
            _logger.LogWarning(
                "Skipping RAG indexing: DriveId or ItemId is empty " +
                "(File={FileName} DriveId={DriveId} ItemId={ItemId} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.DriveId, request.ItemId, request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("MissingSpeIdentifiers");
        }

        // 4-5. Size-based skips only run when caller knows the size.
        if (request.FileSizeBytes.HasValue)
        {
            if (request.FileSizeBytes.Value == 0)
            {
                _logger.LogInformation(
                    "Skipping RAG indexing: file is empty " +
                    "(File={FileName} Source={Source} CorrelationId={CorrelationId})",
                    request.FileName, request.Source, request.CorrelationId);
                return PostUploadIndexingResult.Skipped("EmptyFile");
            }

            if (request.FileSizeBytes.Value > opts.MaxIndexableBytes)
            {
                _logger.LogInformation(
                    "Skipping RAG indexing: file exceeds MaxIndexableBytes " +
                    "(File={FileName} SizeBytes={SizeBytes} MaxBytes={MaxBytes} Source={Source} CorrelationId={CorrelationId})",
                    request.FileName, request.FileSizeBytes.Value, opts.MaxIndexableBytes, request.Source, request.CorrelationId);
                return PostUploadIndexingResult.Skipped("FileTooLarge");
            }
        }

        // 6. Content-type / extension skip-list
        if (IsNonIndexableContent(request.ContentType, request.FileName))
        {
            _logger.LogInformation(
                "Skipping RAG indexing: content type / extension is non-indexable " +
                "(File={FileName} ContentType={ContentType} Source={Source} CorrelationId={CorrelationId})",
                request.FileName, request.ContentType ?? "(null)", request.Source, request.CorrelationId);
            return PostUploadIndexingResult.Skipped("NonIndexableContentType");
        }

        // 7. Dispatch — synchronous OBO indexing via IFileIndexingService. Non-fatal try/catch:
        //    RAG indexing must NEVER fail the upload contract with the caller.
        try
        {
            var fileIndexRequest = new FileIndexRequest
            {
                TenantId = request.TenantId,
                DriveId = request.DriveId,
                ItemId = request.ItemId,
                FileName = request.FileName,
                DocumentId = request.DocumentId,
                ParentEntity = request.ParentEntity,
                SearchIndexName = request.SearchIndexName,
            };

            _logger.LogInformation(
                "[PostUploadIndexingEnqueuer] Starting sync OBO indexing for {FileName} " +
                "(DriveId={DriveId} ItemId={ItemId} DocumentId={DocumentId} " +
                "SearchIndexName={SearchIndexName} Source={Source} TenantId={TenantId} CorrelationId={CorrelationId})",
                request.FileName,
                request.DriveId,
                request.ItemId,
                request.DocumentId ?? "(none)",
                request.SearchIndexName ?? "(resolver-pending)",
                request.Source,
                request.TenantId,
                request.CorrelationId);

            var result = await _fileIndexingService.IndexFileAsync(fileIndexRequest, httpContext, ct);

            if (result.Success)
            {
                _logger.LogInformation(
                    "[PostUploadIndexingEnqueuer] OBO indexing succeeded for {FileName} — {ChunksIndexed} chunks in {DurationMs} ms " +
                    "(DocumentId={DocumentId} Source={Source} CorrelationId={CorrelationId})",
                    request.FileName,
                    result.ChunksIndexed,
                    (int)result.Duration.TotalMilliseconds,
                    request.DocumentId ?? "(none)",
                    request.Source,
                    request.CorrelationId);
                return PostUploadIndexingResult.Submitted(Guid.NewGuid());
            }

            _logger.LogWarning(
                "[PostUploadIndexingEnqueuer] OBO indexing returned failure for {FileName}: {Error} " +
                "(Source={Source} CorrelationId={CorrelationId})",
                request.FileName,
                result.ErrorMessage ?? "(no message)",
                request.Source,
                request.CorrelationId);
            return PostUploadIndexingResult.Failed(result.ErrorMessage ?? "IndexingFailed");
        }
        catch (Exception ex)
        {
            // Non-fatal: log + swallow. The SPE upload already succeeded; indexing is best-effort.
            // Operator can re-trigger manually via POST /api/ai/rag/send-to-index (the ribbon command).
            _logger.LogWarning(ex,
                "[PostUploadIndexingEnqueuer] OBO indexing threw for {FileName} " +
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

    /// <inheritdoc/>
    public async Task<PostUploadIndexingResult> EnqueueAppOnlyIfApplicableAsync(
        PostUploadIndexingRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var opts = _options.Value;

        // Same applicability checks as the OBO path
        if (!opts.PostUploadEnqueueEnabled)
        {
            _logger.LogInformation(
                "Skipping app-only RAG enqueue: feature flag disabled (File={FileName} Source={Source})",
                request.FileName, request.Source);
            return PostUploadIndexingResult.Skipped("FeatureFlagDisabled");
        }
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogError(
                "Skipping app-only RAG enqueue: TenantId missing (File={FileName} Source={Source})",
                request.FileName, request.Source);
            return PostUploadIndexingResult.Skipped("MissingTenantId");
        }
        if (string.IsNullOrWhiteSpace(request.DriveId) || string.IsNullOrWhiteSpace(request.ItemId))
        {
            _logger.LogWarning(
                "Skipping app-only RAG enqueue: missing SPE identifiers (File={FileName} Source={Source})",
                request.FileName, request.Source);
            return PostUploadIndexingResult.Skipped("MissingSpeIdentifiers");
        }
        if (request.FileSizeBytes.HasValue)
        {
            if (request.FileSizeBytes.Value == 0)
            {
                return PostUploadIndexingResult.Skipped("EmptyFile");
            }
            if (request.FileSizeBytes.Value > opts.MaxIndexableBytes)
            {
                return PostUploadIndexingResult.Skipped("FileTooLarge");
            }
        }
        if (IsNonIndexableContent(request.ContentType, request.FileName))
        {
            return PostUploadIndexingResult.Skipped("NonIndexableContentType");
        }

        // Dispatch via Service Bus — non-fatal try/catch.
        // This is the Phase 1 pattern. MUST only be called for MI-written files
        // (Office Add-in finalize, Email-to-Document, post-analysis re-index).
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
                SearchIndexName = request.SearchIndexName,
                Source = request.Source,
                EnqueuedAt = DateTimeOffset.UtcNow,
            };

            var job = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = request.DocumentId ?? request.ItemId,
                CorrelationId = request.CorrelationId,
                IdempotencyKey = $"rag-index-{request.DriveId}-{request.ItemId}",
                Attempt = 1,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(jobPayload)),
            };

            await _jobSubmissionService.SubmitJobAsync(job, ct);

            _logger.LogInformation(
                "[PostUploadIndexingEnqueuer] Enqueued app-only RAG indexing job {JobId} for {FileName} " +
                "(DriveId={DriveId} ItemId={ItemId} DocumentId={DocumentId} Source={Source})",
                job.JobId, request.FileName, request.DriveId, request.ItemId,
                request.DocumentId ?? "(none)", request.Source);

            return PostUploadIndexingResult.Submitted(job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[PostUploadIndexingEnqueuer] Failed to enqueue app-only RAG indexing for {FileName}: {Error}",
                request.FileName, ex.Message);
            return PostUploadIndexingResult.Failed(ex.GetType().Name);
        }
    }

    /// <summary>
    /// Returns true if the content type / file extension indicates the file has no
    /// extractable text and should not be indexed.
    /// </summary>
    /// <remarks>
    /// .msg / .eml / .pdf are intentionally NOT skipped.
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
