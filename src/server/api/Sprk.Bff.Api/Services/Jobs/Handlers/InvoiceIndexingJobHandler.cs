using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler that indexes one invoice into <c>spaarke-invoices-index</c> from its structured records (ebilling-r1 task 152, FR-43, D-44):
/// the <c>sprk_invoice</c> row and its <c>sprk_billingevent</c> lines (firm, core record, timekeepers, UTBMS codes, amounts, status,
/// narratives). It reads no document text and never touches a submission (<see cref="InvoiceIndexDocument"/> builds the document).
/// </summary>
/// <remarks>
/// <para><b>One job, one invoice, the invoice as it is when the job runs.</b> A job names an invoice and the change that caused it; the handler
/// reads the current rows, so jobs from two changes that run out of order still converge on the latest state, and an at-least-once
/// re-delivery rewrites the same document (<c>MergeOrUpload</c> keyed by the invoice id; ADR-004).</para>
/// <para><b>Enqueue keys carry the change.</b> <see cref="BuildJob"/> puts the change in the idempotency key, which
/// <see cref="JobSubmissionService"/> uses as the Service Bus <c>MessageId</c> for duplicate detection. A fixed per-invoice key would drop
/// every re-index after the first.</para>
/// <para><b>Registered unconditionally</b> (FinanceModule flag-off rule), so a queued message never dead-letters <c>NoHandler</c>. It degrades by
/// completing: <c>Ebilling:Enabled</c> off does nothing; no <see cref="SearchIndexClient"/> (search not configured) logs one warning; no
/// <see cref="IInvoiceAi"/> embedding indexes the document without a vector.</para>
/// <para>ADR-013: embeddings only through <see cref="IInvoiceAi"/>. ADR-015: logs carry ids, counts and timings, never narrative text.
/// ADR-014: the document's <c>tenantId</c> is the BFF's <c>AzureAd:TenantId</c>, the value <see cref="InvoiceSearchService"/> filters on; with none
/// configured the job is poisoned rather than written without one.</para>
/// </remarks>
public sealed class InvoiceIndexingJobHandler : IJobHandler
{
    private readonly SearchIndexClient? _searchIndexClient;
    private readonly IGenericEntityService _entities;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly IInvoiceAi? _invoiceAi;
    private readonly TimeProvider _time;
    private readonly ILogger<InvoiceIndexingJobHandler> _logger;

    /// <summary>Job type constant - must match the JobType used when enqueuing.</summary>
    public const string JobTypeName = "InvoiceIndexing";

    /// <summary>The change key of an invoice's acceptance (the first index of its document).</summary>
    public const string AcceptedChange = "accepted";

    /// <summary>
    /// Canonical index name: spaarke-invoices-index (per AI-SEARCH-INDEX-CATALOG.md; FR-10 + NFR-03).
    /// MVP uses a single index; per-tenant fan-out is a future extension.
    /// </summary>
    internal const string IndexNamePattern = "spaarke-invoices-index";

    internal const string TenantSettingKey = "AzureAd:TenantId";

    public InvoiceIndexingJobHandler(
        IGenericEntityService entities,
        IConfiguration configuration,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<InvoiceIndexingJobHandler> logger,
        SearchIndexClient? searchIndexClient = null,
        IInvoiceAi? invoiceAi = null,
        TimeProvider? time = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _searchIndexClient = searchIndexClient; // Nullable: SearchIndexClient is registered only when DocumentIntelligence:Enabled; without it the job completes with a warning (never dead-letters).
        _invoiceAi = invoiceAi; // Nullable: when AI is disabled the embedding step is skipped and the document is indexed without a new vector.
        _time = time ?? TimeProvider.System;
    }

    public string JobType => JobTypeName;

    /// <summary>
    /// The indexing job of <paramref name="invoiceId"/> for one <paramref name="change"/>. The idempotency key is
    /// <c>invoice-index-{invoiceId:N}-{change}</c>: two changes never share a key, one change always does.
    /// </summary>
    /// <param name="invoiceId">The <c>sprk_invoice</c> to index.</param>
    /// <param name="change">What changed, unique per change of the invoice: <see cref="AcceptedChange"/>, or the row version the change
    /// replaced (<c>v{versionnumber}</c>; a conditional write moves the version, so each change has its own).</param>
    /// <param name="correlationId">The caller's correlation id; a new one when null.</param>
    public static JobContract BuildJob(Guid invoiceId, string change, string? correlationId)
    {
        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("An invoice id is required.", nameof(invoiceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(change);

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = JobTypeName,
            SubjectId = invoiceId.ToString(),
            CorrelationId = correlationId ?? Activity.Current?.Id ?? Guid.NewGuid().ToString(),
            IdempotencyKey = $"invoice-index-{invoiceId:N}-{change}",
            Attempt = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(new { InvoiceId = invoiceId, Change = change })),
        };
    }

    /// <summary>
    /// The one best-effort producer of an invoice's re-index, shared by every service that changes what the document holds (the lifecycle
    /// move, a line decision, an adjustment, a correction's follow-through, an override). The change is already committed when this runs, so
    /// a missing submitter (a test seam) does nothing and a failed enqueue is logged (ids only, ADR-015) and never fails or undoes the change:
    /// the index catches up at the next change. It ignores the caller's cancellation for the same reason (a caller that cancels now must not
    /// leave the index stale).
    /// </summary>
    /// <param name="jobs">The job submitter; null does nothing.</param>
    /// <param name="logger">The producer's logger.</param>
    /// <param name="invoiceId">The invoice to re-index.</param>
    /// <param name="change">Unique per change (see <see cref="BuildJob"/>): never a constant per invoice.</param>
    internal static async Task TryEnqueueAsync(JobSubmissionService? jobs, ILogger logger, Guid invoiceId, string change)
    {
        if (jobs is null)
        {
            return;
        }

        try
        {
            await jobs.SubmitJobAsync(BuildJob(invoiceId, change, correlationId: null), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Invoice {InvoiceId}: the re-index could not be queued after change {Change} ({ExceptionType}); the index catches up at the next change",
                invoiceId, change, ex.GetType().Name);
        }
    }

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing invoice indexing job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // ADR-018: the same flag as the endpoints. Off: complete with nothing done (the handler is registered unconditionally, FinanceModule flag rule).
            if (!_ebilling.CurrentValue.Enabled)
            {
                _logger.LogInformation("Invoice indexing job {JobId}: eBilling is disabled; nothing indexed", job.JobId);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Search is not configured (SearchIndexClient is registered only with DocumentIntelligence:Enabled): nothing can be indexed here, and a
            // retry or a dead letter would not change that. One warning, then complete.
            if (_searchIndexClient is null)
            {
                _logger.LogWarning(
                    "Invoice indexing job {JobId}: Azure AI Search is not configured (DocumentIntelligence:Enabled is off); subject {SubjectId} is not indexed",
                    job.JobId, job.SubjectId);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.InvoiceId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for invoice indexing job {JobId}", job.JobId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var invoiceId = payload.InvoiceId;

            // ADR-014: a document without a tenant is never written (the search filters on it; a misconfigured tenant would otherwise fail open).
            var tenantId = _configuration[TenantSettingKey];
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogError("Invoice indexing job {JobId}: {Setting} is not configured; invoice {InvoiceId} is not indexed", job.JobId, TenantSettingKey, invoiceId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Tenant id is not configured", job.Attempt, stopwatch.Elapsed);
            }

            // Step 1: the invoice and its lines, as they are now.
            var invoiceRows = await _entities.RetrieveMultipleAsync(InvoiceIndexDocument.InvoiceQuery(invoiceId), ct);
            var invoice = invoiceRows.Entities.FirstOrDefault();
            if (invoice == null)
            {
                _logger.LogError("Invoice {InvoiceId} not found in Dataverse", invoiceId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invoice record not found", job.Attempt, stopwatch.Elapsed);
            }

            var lines = await InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceIndexDocument.LineQuery(invoiceId), ct);

            // Step 2: the document's facts and text.
            var model = InvoiceIndexDocument.Read(invoice, lines);

            // Step 3: the embedding. A failure (AI off, throttled, model error) indexes the document without a new vector: keyword and filter
            // search keep working, and the vector an earlier run wrote stays (the document omits the field).
            ReadOnlyMemory<float> embedding = ReadOnlyMemory<float>.Empty;
            if (!string.IsNullOrWhiteSpace(model.Content))
            {
                try
                {
                    embedding = await RequireAi().GenerateEmbeddingAsync(
                        InvoiceIndexDocument.EmbeddingInput(model),
                        model: "text-embedding-3-large",
                        dimensions: 3072,
                        cancellationToken: ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "No embedding for invoice {InvoiceId} ({ExceptionType}); indexing without a new vector",
                        invoiceId, ex.GetType().Name);
                    embedding = ReadOnlyMemory<float>.Empty;
                }
            }

            // Step 4: MergeOrUpload keyed by the invoice id.
            var searchDocument = InvoiceIndexDocument.ToSearchDocument(model, tenantId, embedding, _time.GetUtcNow());
            var searchClient = _searchIndexClient.GetSearchClient(IndexNamePattern);

            try
            {
                var batch = new[] { new IndexDocumentsAction<SearchDocument>(IndexActionType.MergeOrUpload, searchDocument) };

                var response = await searchClient.IndexDocumentsAsync(
                    IndexDocumentsBatch.Create(batch),
                    new IndexDocumentsOptions { ThrowOnAnyError = false },
                    cancellationToken: ct);

                var result = response.Value.Results.FirstOrDefault();
                if (result?.Succeeded != true)
                {
                    _logger.LogError(
                        "Failed to index invoice {InvoiceId}: status {Status}, {Error}",
                        invoiceId, result?.Status, result?.ErrorMessage ?? "Unknown error");

                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        result?.ErrorMessage ?? "Indexing failed",
                        job.Attempt, stopwatch.Elapsed);
                }

                _logger.LogInformation(
                    "Indexed invoice {InvoiceId} ({LineCount} lines, status {Status}, vector {HasVector}) into Azure AI Search in {Duration}ms",
                    invoiceId, lines.Count, model.Status, embedding.Length > 0, stopwatch.ElapsedMilliseconds);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure AI Search request failed for invoice {InvoiceId}: status {Status}",
                    invoiceId, ex.Status);

                // Throttling and service-unavailable retry; anything else is permanent.
                if (ex.Status is 429 or 503)
                {
                    return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
                }

                return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice indexing job {JobId} failed ({ExceptionType})", job.JobId, ex.GetType().Name);

            return IsRetryableException(ex)
                ? JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed)
                : JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Returns the AI facade or throws if AI features are disabled. The throw is caught at the call site: the document is then indexed without
    /// a new vector.
    /// </summary>
    private IInvoiceAi RequireAi() =>
        _invoiceAi ?? throw new InvalidOperationException(
            "Invoice embeddings require AI features. Set 'Analysis:Enabled=true' AND 'DocumentIntelligence:Enabled=true' to enable.");

    private InvoiceIndexingPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<InvoiceIndexingPayload>(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse invoice indexing job payload");
            return null;
        }
    }

    /// <summary>
    /// Determines if an exception represents a transient failure that should be retried.
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        // HTTP 429 (throttling), 503 (service unavailable), timeouts
        if (ex is HttpRequestException)
        {
            return true;
        }

        // RequestFailedException with retryable status codes
        if (ex is RequestFailedException rfe)
        {
            return rfe.Status is 429 or 503;
        }

        // Check for known transient exception types
        var exceptionName = ex.GetType().Name;
        return exceptionName.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Transient", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Payload structure for invoice indexing jobs.
/// </summary>
public class InvoiceIndexingPayload
{
    /// <summary>The Dataverse invoice ID to index.</summary>
    public Guid InvoiceId { get; set; }

    /// <summary>
    /// Legacy: the document the retired document-centric path sent (InvoiceExtractionJobHandler, retired by 050b). Accepted and ignored; the
    /// document id of the index row comes from the invoice's own <c>sprk_document</c> lookup.
    /// </summary>
    public Guid? DocumentId { get; set; }

    /// <summary>What changed (<see cref="InvoiceIndexingJobHandler.BuildJob"/>); informational, the handler always reads the current rows.</summary>
    public string? Change { get; set; }
}
