using System.Data;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Stable codes of the AP export routes (ADR-019).</summary>
public static class ApExportCodes
{
    public const string InvalidRequest = "ebilling.export.invalid_request";
    public const string SelectionRefused = "ebilling.export.selection_refused";
    public const string NotPermitted = "ebilling.export.not_permitted";
    public const string CallerUnresolved = "ebilling.export.caller_unresolved";
    public const string AuthorizationUnavailable = "ebilling.export.authorization_unavailable";
    public const string MappingInvalid = "ebilling.export.mapping_invalid";
    public const string Unavailable = "ebilling.export.unavailable";
    public const string InvoiceConflict = "ebilling.export.invoice_conflict";
    public const string FileFailed = "ebilling.export.file_failed";
    public const string FileTooLarge = "ebilling.export.file_too_large";
    public const string TransitionFailed = "ebilling.export.transition_failed";
    public const string TransitionIncomplete = "ebilling.export.transition_incomplete";
    public const string CompensationIncomplete = "ebilling.export.compensation_incomplete";
    public const string CompletionIncomplete = "ebilling.export.completion_incomplete";
    public const string BatchTotalOutOfRange = "ebilling.export.batch_total_out_of_range";

    /// <summary>Per-invoice reasons of <see cref="SelectionRefused"/>.</summary>
    public const string ReasonNotApproved = "invoice_not_approved";
    public const string ReasonAlreadyExported = "invoice_already_exported";
    public const string ReasonNotFound = "invoice_not_found";
}

/// <summary>Body of <c>POST /api/finance/export-batches</c>: the invoices to export and the formats (csv, json; both when omitted).</summary>
public sealed record ApExportRequest(IReadOnlyList<Guid>? InvoiceIds, IReadOnlyList<string>? Formats);

public sealed record ApExportFileDto(Guid FileId, string Format, string Name, string ContentType);

public sealed record ApExportBatchDto(
    Guid BatchId, string Name, string Status, int InvoiceCount, decimal TotalAmount, IReadOnlyList<string> Formats,
    IReadOnlyList<ApExportFileDto> Files, IReadOnlyList<Guid> InvoiceIds, string? FailureReason);

public sealed record ApExportCandidateDto(
    Guid InvoiceId, string? FirmInvoiceNumber, string? TrackingNumber, DateOnly? InvoiceDate, decimal? ApprovedTotal, string? Currency,
    string? CoreRecordName);

public sealed record ApExportCandidatesDto(IReadOnlyList<ApExportCandidateDto> Candidates, bool Truncated);

public sealed record ApExportInvoiceRefusal(Guid InvoiceId, string Reason);

public enum ApExportOutcome
{
    Created,
    InvalidRequest,
    SelectionRefused,
    NotPermitted,
    AuthorizationUnavailable,
    MappingInvalid,
    Unavailable,
    Conflict,
    Failed,
    Incomplete,

    /// <summary>The batch total is outside what <c>sprk_billingexport.sprk_totalamount</c> can hold (400, nothing claimed or written).</summary>
    TotalOutOfRange,
}

/// <summary>The answer of <see cref="ApExportService.CreateBatchAsync"/>: a created batch, or one outcome with its stable code.</summary>
public sealed record ApExportResult(
    ApExportOutcome Outcome, string? Code, string? Detail, ApExportBatchDto? Batch, IReadOnlyList<ApExportInvoiceRefusal>? Invoices, Guid? BatchId)
{
    internal static ApExportResult Created(ApExportBatchDto batch) => new(ApExportOutcome.Created, null, null, batch, null, batch.BatchId);

    internal static ApExportResult Refused(ApExportOutcome outcome, string code, string detail, Guid? batchId = null) =>
        new(outcome, code, detail, null, null, batchId);
}

/// <summary>
/// The AP export batch (ebilling-r1 task 150; FR-41, FR-06, D-21, D-56, D-85, EB-D-148, EB-D-178). Finance users list the Approved,
/// not-yet-exported invoices and export a selection: the server claims each invoice for exactly one <c>sprk_billingexport</c> batch, snapshots the
/// mapping, writes a CSV and a JSON as Stored Files (purpose Export file, parent the batch, never firm-visible), and moves the invoices to Sent to
/// AP through the lifecycle service. The only writer of <c>sprk_invoice.sprk_billingexport</c> (ADR-002).
/// </summary>
/// <remarks>
/// <para><b>Order.</b> (1) validate the request; (2) <see cref="ApExportAuthorization.AuthorizeExportAsync"/> (EB-D-178; the whole batch or nothing, before any read
/// of the invoices' state); (3) read the mapping and check the file store (before any claim: a fault here writes nothing); (4) read and check the
/// selection (each invoice Approved with an empty batch lookup, else 400 with per-invoice reasons, nothing claimed; and the batch total fits the batch total column, else 400 <c>batch_total_out_of_range</c>, nothing claimed); (5) create the batch, status
/// Generating; (6) claim every invoice in id order with <c>IfRowVersionMatches</c> on the version read in (4): a conflict releases this batch's claims,
/// marks the batch Failed and answers 409 (ordered claims make exactly one of two overlapping batches win); (7) re-check that every claimed invoice is
/// still Approved; (8) read the values once, write the CSV and JSON; (9) move each invoice to Sent to AP; (10) mark the batch Completed.</para>
/// <para><b>Compensation (a Failed batch blocks nothing).</b> A failure in 6 to 8 withdraws the files already written (through 090) and releases every
/// claim; the batch is marked Failed with a reason. The lifecycle has no way back from Sent to AP, so (9) is the one irreversible step and comes
/// after every file is written. A failure on the FIRST invoice of (9) leaves nothing sent and takes the same path. A failure after at least one invoice
/// moved (a fault or a concurrent change between (7) and (9), a narrow window) cannot un-send those: the files are withdrawn (so no payment file
/// exists for the Failed batch), the claims of the invoices NOT sent are released, the sent invoices keep their claim, and the answer is 500
/// <c>ebilling.export.transition_incomplete</c> naming the batch for the operator. Known limit, recorded in the task notes.</para>
/// <para><b>Not guaranteed here.</b> A process crash between (5) and (10) leaves a Generating batch holding claims; nothing sweeps it in R1 (the claim
/// is the double-payment guard, so it fails safe: the invoices cannot be exported again until an operator clears the batch).</para>
/// </remarks>
public sealed class ApExportService
{
    public const string BatchEntity = "sprk_billingexport";
    public const string InvoiceEntity = "sprk_invoice";
    public const string BatchLookup = "sprk_billingexport";

    /// <summary><c>sprk_exportstatus</c> (023).</summary>
    public const int StatusGenerating = 100000000, StatusCompleted = 100000001, StatusFailed = 100000002;

    /// <summary><c>sprk_formats</c> (023): CSV, JSON. XML (100000002) is reserved and never written.</summary>
    public const int FormatCsv = 100000000, FormatJson = 100000001;

    /// <summary>The most invoices one batch may hold; and the most the candidates list shows.</summary>
    public const int MaxInvoicesPerBatch = 200, CandidateCap = 500;

    /// <summary>
    /// The range of <c>sprk_billingexport.sprk_totalamount</c> as configured (docs/data-model/sprk_billingexport.md): a batch whose total is outside it
    /// is refused before any claim, because the write that completes the batch would fail AFTER the invoices were sent to AP. The minimum is the one
    /// <c>scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1</c> sets (-922337203685477, D-129 (4); run on dev 2026-10-30, and a deploy prerequisite
    /// of every other environment). This is the only place the range is spelled.
    /// </summary>
    public const decimal BatchTotalMinimum = -922_337_203_685_477m, BatchTotalMaximum = 1_000_000_000m;

    private const int PortionApproved = 100000001;

    /// <summary>The page size of every multi-row read here. Dataverse caps <c>TopCount</c> and a page at 5,000: a larger number fails the call.</summary>
    private const int ReadLimit = 5000;
    private static readonly string[] SelectionColumns =
    [
        "statuscode", BatchLookup, "versionnumber", "sprk_invoicenumber", "sprk_trackingnumber", "sprk_invoicedate", "sprk_invoiceduedate",
        "sprk_billingperiodstart", "sprk_billingperiodend", "sprk_currency", "sprk_totalamount", "sprk_lineadjustmentstotal",
        "sprk_invoiceadjustmentstotal", "sprk_approvedtotal", "sprk_vendororg", "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
        "sprk_regardingagreement", "sprk_regardingrecordnumber", "sprk_regardingrecordname",
    ];

    private readonly IGenericEntityService _entities;
    private readonly IInvoiceLifecycleService _lifecycle;
    private readonly IEbillingSettingsReader _settings;
    private readonly ApExportAuthorization _authorization;
    private readonly StoredFileService _files;
    private readonly StoredFileBlobs _blobs;
    private readonly IRecordOwnershipResolver _owners;
    private readonly ILogger<ApExportService> _logger;

    public ApExportService(
        IGenericEntityService entities,
        IInvoiceLifecycleService lifecycle,
        IEbillingSettingsReader settings,
        ApExportAuthorization authorization,
        StoredFileService files,
        StoredFileBlobs blobs,
        IRecordOwnershipResolver owners,
        ILogger<ApExportService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _owners = owners ?? throw new ArgumentNullException(nameof(owners));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =============================================================================================================================
    // Create a batch
    // =============================================================================================================================

    /// <summary>Exports <paramref name="request"/>'s invoices as one batch for <paramref name="caller"/> (see the type remarks for the order).</summary>
    public async Task<ApExportResult> CreateBatchAsync(
        ApExportRequest request, InvoiceContextService.InvoiceCaller caller, Guid callerSystemUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        // (1) The request.
        if (Validate(request) is not { } valid)
        {
            return ApExportResult.Refused(ApExportOutcome.InvalidRequest, ApExportCodes.InvalidRequest,
                $"Select between 1 and {MaxInvoicesPerBatch} distinct invoices and the formats csv and/or json.");
        }

        var (invoiceIds, formats) = valid;

        // (2) Who may export these invoices (EB-D-178): all of them or the whole request is refused, before anything is read or written.
        switch (await _authorization.AuthorizeExportAsync(caller, invoiceIds, ct).ConfigureAwait(false))
        {
            case ApExportAuthorizationOutcome.NotPermitted:
                return ApExportResult.Refused(ApExportOutcome.NotPermitted, ApExportCodes.NotPermitted,
                    "You are not allowed to export this selection. Nothing was exported.");
            case ApExportAuthorizationOutcome.Undetermined:
                return ApExportResult.Refused(ApExportOutcome.AuthorizationUnavailable, ApExportCodes.AuthorizationUnavailable,
                    "Your rights on the selected invoices could not be confirmed. Nothing was exported. Try again.");
        }

        // (3) Before any claim: the mapping and the file store. A fault here writes nothing.
        ApExportMappingResult mapping;
        string snapshot;
        try
        {
            var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
            mapping = ApExportMappingParser.Parse(settings.ApExportMappingJson);
            snapshot = string.IsNullOrWhiteSpace(settings.ApExportMappingJson) ? ApExportDefaultMapping.Json : settings.ApExportMappingJson;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AP export: the settings could not be read; nothing was claimed");
            return ApExportResult.Refused(ApExportOutcome.Unavailable, ApExportCodes.Unavailable, "The export settings could not be read. Nothing was exported. Try again.");
        }

        if (!mapping.IsValid)
        {
            _logger.LogError("AP export: the settings mapping is invalid ({Error}); nothing was claimed", mapping.Error);
            return ApExportResult.Refused(ApExportOutcome.MappingInvalid, ApExportCodes.MappingInvalid,
                $"The AP export mapping in the eBilling settings is not valid ({mapping.Error}). Nothing was exported.");
        }

        if (!_blobs.IsConfigured)
        {
            return ApExportResult.Refused(ApExportOutcome.Unavailable, ApExportCodes.Unavailable, "The file store is not available. Nothing was exported.");
        }

        // (4) The selection: every invoice Approved with an empty batch lookup, else the whole request is refused with the reasons.
        var selected = new List<Entity>();
        var refusals = new List<ApExportInvoiceRefusal>();
        try
        {
            foreach (var id in invoiceIds.OrderBy(i => i))
            {
                var row = await TryReadInvoiceAsync(id, ct).ConfigureAwait(false);
                var reason = row is null ? ApExportCodes.ReasonNotFound : SelectionReason(row);
                if (reason is not null)
                {
                    refusals.Add(new ApExportInvoiceRefusal(id, reason));
                }
                else
                {
                    selected.Add(row!);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AP export: the selected invoices could not be read; nothing was claimed");
            return ApExportResult.Refused(ApExportOutcome.Unavailable, ApExportCodes.Unavailable, "The selected invoices could not be read. Nothing was exported. Try again.");
        }

        if (refusals.Count > 0)
        {
            return new ApExportResult(ApExportOutcome.SelectionRefused, ApExportCodes.SelectionRefused,
                "Only Approved invoices that are not yet in an export batch can be exported. Nothing was exported.", null, refusals, null);
        }

        // The batch total, computed once here: it is written to the batch at the very end, after the invoices were sent to AP, so a total the column
        // cannot hold must be refused NOW, before any claim (a failing completion write there would leave a Generating batch of sent invoices).
        var total = selected.Sum(r => Dec(r, "sprk_approvedtotal") ?? 0m);
        if (total < BatchTotalMinimum || total > BatchTotalMaximum)
        {
            _logger.LogWarning("AP export: the batch total is outside {Minimum}..{Maximum}; nothing was claimed", BatchTotalMinimum, BatchTotalMaximum);
            return ApExportResult.Refused(ApExportOutcome.TotalOutOfRange, ApExportCodes.BatchTotalOutOfRange,
                "The total of the selected invoices is outside the range an export batch can record. Select different invoices. Nothing was exported.");
        }

        // (5) The batch, Generating. Owned like any user-owned row the caller creates (the caller's business-unit team), so the people who can see
        // the invoices can see the batch (D-90).
        var batchId = Guid.NewGuid();
        var batchName = batchId.ToString("D");
        RecordOwnerResolution owner;
        try
        {
            owner = await _owners.ResolveOwnerAsync(
                new RecordOwnershipContext { CallerSystemUserId = callerSystemUserId, RequestedBy = RecordRequester.Of(systemUserId: callerSystemUserId) }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AP export: the batch owner could not be resolved; nothing was created");
            return ApExportResult.Refused(ApExportOutcome.Unavailable, ApExportCodes.Unavailable, "The export batch could not be created. Nothing was exported. Try again.");
        }

        if (owner.IsRefused)
        {
            _logger.LogWarning("AP export: no owner for the batch ({Code}); nothing was created", owner.RefusalCode);
            return ApExportResult.Refused(ApExportOutcome.Unavailable, ApExportCodes.Unavailable, "The export batch could not be created. Nothing was exported.");
        }

        var batch = new Entity(BatchEntity, batchId)
        {
            ["sprk_name"] = batchName,
            ["sprk_exportstatus"] = new OptionSetValue(StatusGenerating),
            ["sprk_formats"] = new OptionSetValueCollection(formats.Select(f => new OptionSetValue(f == "csv" ? FormatCsv : FormatJson)).ToList()),
            ["sprk_mappingsnapshot"] = snapshot,
        };
        owner.ApplyTo(batch);
        try
        {
            await _entities.CreateAsync(batch, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AP export: the batch {BatchId} could not be created; nothing was claimed", batchId);
            return ApExportResult.Refused(ApExportOutcome.Unavailable, ApExportCodes.Unavailable, "The export batch could not be created. Nothing was exported. Try again.");
        }

        var progress = new Progress();
        try
        {
            // (6) Claim, in id order, each under the version read in (4).
            foreach (var row in selected)
            {
                // Recorded BEFORE the write: a write that commits and then faults (a lost response) must still be released. The release
                // re-reads the row and does nothing when the claim is not this batch's.
                progress.Claimed.Add(row.Id);
                try
                {
                    await _entities.UpdateIfRowVersionMatchesAsync(
                        InvoiceEntity, row.Id, VersionOf(row)!,
                        new Dictionary<string, object> { [BatchLookup] = new EntityReference(BatchEntity, batchId) }, ct).ConfigureAwait(false);
                }
                catch (DBConcurrencyException)
                {
                    throw new Stopped(ApExportOutcome.Conflict, ApExportCodes.InvoiceConflict,
                        "An invoice in the selection was changed or exported by someone else. Nothing was exported. Reload and try again.");
                }
            }

            // (7) Still Approved after the claim (a status change does not look at the batch lookup).
            foreach (var row in selected)
            {
                var fresh = await _entities.RetrieveAsync(InvoiceEntity, row.Id, ["statuscode", BatchLookup], ct).ConfigureAwait(false);
                if (StatusOf(fresh) != InvoiceStatusCodes.Approved || BatchOf(fresh) != batchId)
                {
                    throw new Stopped(ApExportOutcome.Conflict, ApExportCodes.InvoiceConflict,
                        "An invoice in the selection changed while the export was being prepared. Nothing was exported. Reload and try again.");
                }
            }

            // (8) The values, read once; the files, written once.
            var values = new List<ApExportInvoiceValues>();
            var reader = new ValueReader(_entities, batchName);
            foreach (var row in selected)
            {
                values.Add(await reader.ReadAsync(row, ct).ConfigureAwait(false));
            }

            var model = mapping.Model!;
            var written = new List<ApExportFileDto>();
            foreach (var format in formats)
            {
                var bytes = format == "csv" ? ApExportWriter.WriteCsv(model, values) : ApExportWriter.WriteJson(batchName, model, values);
                if (!StoredFileSize.TryToColumnValue(bytes.LongLength, out _))
                {
                    throw new Stopped(ApExportOutcome.Failed, ApExportCodes.FileTooLarge, "The export file is too large to store. Nothing was exported. Select fewer invoices.");
                }

                var contentType = format == "csv" ? "text/csv" : "application/json";
                var name = $"ap-export-{batchName}.{format}";
                var spec = new StoredFileRowSpec
                {
                    FileId = StoredFileIds.New(),
                    Purpose = StoredFilePurpose.ExportFile,
                    Parent = new StoredFileParent(StoredFileParentKind.BillingExport, batchId),
                    Name = name,
                    ContentType = contentType,
                    FirmVisible = false,
                };
                progress.Files.Add(spec.FileId);
                StoredFileCreateResult created;
                try
                {
                    // requestedBy is mandatory: the batch files nothing, so the acting user decides the file's owner (194b); without it the write fails closed.
                    created = await _files.CreateForAppAsync(spec, new MemoryStream(bytes, writable: false), callerSystemUserId, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogError(ex, "AP export: writing the {Format} file of batch {BatchId} failed", format, batchId);
                    throw new Stopped(ApExportOutcome.Failed, ApExportCodes.FileFailed, "The export file could not be stored. Nothing was exported. Try again.");
                }

                if (!created.IsCreated)
                {
                    _logger.LogWarning("AP export: the file store refused the {Format} file of batch {BatchId} ({Code})", format, batchId, created.RefusalCode);
                    throw created.RefusalCode == StoredFileRefusal.SizeOutOfRange
                        ? new Stopped(ApExportOutcome.Failed, ApExportCodes.FileTooLarge, "The export file is too large to store. Nothing was exported. Select fewer invoices.")
                        : new Stopped(ApExportOutcome.Failed, ApExportCodes.FileFailed, "The export file could not be stored. Nothing was exported. Try again.");
                }

                written.Add(new ApExportFileDto(spec.FileId, format, name, contentType));
            }

            // (9) The one irreversible step: every invoice to Sent to AP, through the lifecycle service.
            // From here on the request's cancellation token is not honoured: the claims are held, the work is seconds, and a client that
            // disconnects half way must not turn a healthy batch into a partly sent one.
            foreach (var row in selected)
            {
                if (!await TrySendAsync(row.Id).ConfigureAwait(false))
                {
                    // A move that committed and then faulted (a lost response) is sent: it is counted, so its claim is kept and the answer is honest.
                    if (await MaybeSentAsync(row.Id).ConfigureAwait(false))
                    {
                        progress.Sent.Add(row.Id);
                    }

                    throw progress.Sent.Count == 0
                        ? new Stopped(ApExportOutcome.Failed, ApExportCodes.TransitionFailed, "An invoice could not be moved to Sent to AP. Nothing was exported. Try again.")
                        : new Stopped(ApExportOutcome.Incomplete, ApExportCodes.TransitionIncomplete,
                            $"{progress.Sent.Count} of {selected.Count} invoices were moved to Sent to AP before one failed. The batch is Failed and has no files; an operator must resolve it.");
                }

                progress.Sent.Add(row.Id);
            }

            // (10) Completed, with the count and the total.
            await CompleteAsync(batchId, selected.Count, total, ct).ConfigureAwait(false);
            _logger.LogInformation("AP export batch {BatchId} completed: {Count} invoices, {Files} files", batchId, selected.Count, written.Count);
            return ApExportResult.Created(new ApExportBatchDto(
                batchId, batchName, "Completed", selected.Count, total, formats, written, selected.Select(r => r.Id).ToList(), null));
        }
        catch (Stopped stopped)
        {
            return await CompensateAsync(batchId, progress, stopped).ConfigureAwait(false);
        }
        catch (CompletionException)
        {
            // Every invoice is Sent to AP and the files exist, so nothing is withdrawn or released: the batch stays Generating for an operator.
            return ApExportResult.Refused(ApExportOutcome.Incomplete, ApExportCodes.CompletionIncomplete,
                "The invoices were exported and the files written, but the batch could not be marked Completed. An operator must check the batch.", batchId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CompensateAsync(batchId, progress, new Stopped(ApExportOutcome.Failed, ApExportCodes.Unavailable, "The request was cancelled.")).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AP export batch {BatchId} failed unexpectedly", batchId);
            return await CompensateAsync(batchId, progress, new Stopped(ApExportOutcome.Failed, ApExportCodes.Unavailable,
                "The export could not be completed. Nothing was exported. Try again.")).ConfigureAwait(false);
        }
    }

    // =============================================================================================================================
    // Read
    // =============================================================================================================================

    /// <summary>
    /// The Approved invoices with no batch that the caller may export (EB-D-178), oldest invoice date first, at most <see cref="CandidateCap"/> examined;
    /// <c>null</c> when the caller's rights could not be determined (nothing is shown).
    /// </summary>
    public async Task<ApExportCandidatesDto?> ListCandidatesAsync(InvoiceContextService.InvoiceCaller caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var query = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = new ColumnSet(SelectionColumns),
            TopCount = CandidateCap + 1,
            Criteria = { Conditions = { new ConditionExpression("statuscode", ConditionOperator.Equal, InvoiceStatusCodes.Approved), new ConditionExpression(BatchLookup, ConditionOperator.Null) } },
        };
        query.AddOrder("sprk_invoicedate", OrderType.Ascending);
        var rows = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.ToList();
        var truncated = rows.Count > CandidateCap;
        rows = rows.Take(CandidateCap).ToList();

        var allowed = await _authorization.FilterExportableAsync(caller, rows.Select(r => r.Id).ToList(), ct).ConfigureAwait(false);
        if (allowed is null)
        {
            return null;
        }

        var allowedSet = allowed.ToHashSet();
        var candidates = rows.Where(r => allowedSet.Contains(r.Id)).Select(r => new ApExportCandidateDto(
            r.Id, r.GetAttributeValue<string>("sprk_invoicenumber"), r.GetAttributeValue<string>("sprk_trackingnumber"),
            DateOf(r, "sprk_invoicedate"), Dec(r, "sprk_approvedtotal"), r.GetAttributeValue<string>("sprk_currency"),
            r.GetAttributeValue<string>("sprk_regardingrecordname"))).ToList();
        return new ApExportCandidatesDto(candidates, truncated);
    }

    /// <summary>One batch with its servable files and invoices; <c>null</c> when it does not exist. The route authorizes Read on the batch.</summary>
    public async Task<ApExportBatchDto?> GetBatchAsync(Guid batchId, CancellationToken ct)
    {
        Entity batch;
        try
        {
            batch = await _entities.RetrieveAsync(BatchEntity, batchId,
                ["sprk_name", "sprk_exportstatus", "sprk_invoicecount", "sprk_totalamount", "sprk_formats", "sprk_failurereason"], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (StoredFileNotFound.Is(ex))
        {
            return null;
        }

        var fileQuery = new QueryExpression(StoredFileSchema.Entity)
        {
            ColumnSet = new ColumnSet(StoredFileSchema.IdColumn, StoredFileSchema.Name, StoredFileSchema.ContentType, StoredFileSchema.Status, StoredFileSchema.Purpose),
            TopCount = 10,
            Criteria = { Conditions = { new ConditionExpression(StoredFileSchema.BillingExportTable, ConditionOperator.Equal, batchId) } },
        };
        var isCompleted = batch.GetAttributeValue<OptionSetValue>("sprk_exportstatus")?.Value == StatusCompleted;
        var files = (await _entities.RetrieveMultipleAsync(fileQuery, ct).ConfigureAwait(false)).Entities
            .Where(f => isCompleted // a file of a batch that did not complete is never advertised, even if its withdrawal failed
                        && f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status)?.Value == (int)StoredFileStatus.Servable
                        && f.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose)?.Value == (int)StoredFilePurpose.ExportFile)
            .Select(f =>
            {
                var contentType = f.GetAttributeValue<string>(StoredFileSchema.ContentType) ?? string.Empty;
                return new ApExportFileDto(f.Id, contentType == "text/csv" ? "csv" : "json", f.GetAttributeValue<string>(StoredFileSchema.Name) ?? string.Empty, contentType);
            }).OrderBy(f => f.Format, StringComparer.Ordinal).ToList();

        var invoiceQuery = new QueryExpression(InvoiceEntity)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = ReadLimit,
            Criteria = { Conditions = { new ConditionExpression(BatchLookup, ConditionOperator.Equal, batchId) } },
        };
        var invoiceIds = (await _entities.RetrieveMultipleAsync(invoiceQuery, ct).ConfigureAwait(false)).Entities.Select(e => e.Id).OrderBy(i => i).ToList();

        var status = batch.GetAttributeValue<OptionSetValue>("sprk_exportstatus")?.Value switch
        {
            StatusCompleted => "Completed",
            StatusFailed => "Failed",
            _ => "Generating",
        };
        var formats = (batch.GetAttributeValue<OptionSetValueCollection>("sprk_formats") ?? []).Select(o => o.Value == FormatCsv ? "csv" : "json").ToList();
        return new ApExportBatchDto(
            batchId, batch.GetAttributeValue<string>("sprk_name") ?? batchId.ToString("D"), status,
            batch.GetAttributeValue<int?>("sprk_invoicecount") ?? invoiceIds.Count, Dec(batch, "sprk_totalamount") ?? 0m, formats, files,
            invoiceIds, batch.GetAttributeValue<string>("sprk_failurereason"));
    }

    // =============================================================================================================================
    // Steps and compensation
    // =============================================================================================================================

    private sealed class Progress
    {
        public List<Guid> Claimed { get; } = [];
        public List<Guid> Files { get; } = [];
        public List<Guid> Sent { get; } = [];
    }

    private sealed class Stopped(ApExportOutcome outcome, string code, string detail) : Exception(detail)
    {
        public ApExportOutcome Outcome { get; } = outcome;
        public string Code { get; } = code;
    }

    /// <summary>
    /// Undoes what the batch did, as far as the lifecycle allows: withdraws every file written (through 090), releases the claims of the invoices not
    /// sent, marks the batch Failed. Runs with no cancellation token (it must finish when the caller went away). A step that fails is logged and
    /// reported (<see cref="ApExportCodes.CompensationIncomplete"/>); the other steps still run.
    /// </summary>
    private async Task<ApExportResult> CompensateAsync(Guid batchId, Progress progress, Stopped stopped)
    {
        var clean = true;
        foreach (var fileId in progress.Files)
        {
            try
            {
                await _files.WithdrawExportFileAsync(fileId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                clean = false;
                _logger.LogError(ex, "AP export batch {BatchId}: withdrawing file {FileId} failed; the file store reconcile job will not serve it but an operator should check", batchId, fileId);
            }
        }

        foreach (var invoiceId in progress.Claimed.Where(c => !progress.Sent.Contains(c)))
        {
            try
            {
                await ReleaseClaimAsync(invoiceId, batchId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                clean = false;
                _logger.LogError(ex, "AP export batch {BatchId}: releasing the claim on invoice {InvoiceId} failed; the invoice stays blocked until an operator clears it", batchId, invoiceId);
            }
        }

        try
        {
            await _entities.UpdateAsync(BatchEntity, batchId, new Dictionary<string, object>
            {
                ["sprk_exportstatus"] = new OptionSetValue(StatusFailed),
                ["sprk_failurereason"] = Truncate(stopped.Code + (progress.Sent.Count > 0 ? $" (sent {progress.Sent.Count} of {progress.Claimed.Count})" : string.Empty), 200),
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            clean = false;
            _logger.LogError(ex, "AP export batch {BatchId}: marking it Failed failed", batchId);
        }

        _logger.LogWarning("AP export batch {BatchId} failed with {Code}; compensation clean={Clean}", batchId, stopped.Code, clean);
        return clean
            ? ApExportResult.Refused(stopped.Outcome, stopped.Code, stopped.Message, batchId)
            : ApExportResult.Refused(ApExportOutcome.Incomplete, ApExportCodes.CompensationIncomplete,
                "The export failed and could not be fully undone. An operator must check the batch.", batchId);
    }

    /// <summary>Clears this batch's claim on one invoice, under the version just read; two attempts, because another writer may touch the row.</summary>
    private async Task ReleaseClaimAsync(Guid invoiceId, Guid batchId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var row = await _entities.RetrieveAsync(InvoiceEntity, invoiceId, [BatchLookup, "versionnumber"], CancellationToken.None).ConfigureAwait(false);
            if (BatchOf(row) != batchId)
            {
                return; // not (or no longer) ours: nothing to release
            }

            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(
                    InvoiceEntity, invoiceId, VersionOf(row)!, new Dictionary<string, object> { [BatchLookup] = DBNull.Value }, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (DBConcurrencyException) when (attempt == 0)
            {
                // re-read and retry once
            }
        }

        throw new InvalidOperationException($"Invoice {invoiceId} kept changing; its claim by batch {batchId} could not be released.");
    }

    /// <summary>
    /// Whether the invoice is (or, when its status cannot be read, may be) sent: Sent to AP or Paid. Any other status (still Approved, or moved elsewhere
    /// by someone else) is NOT sent by this batch, so its claim is released. Unknown counts as sent: its claim is kept (fail safe).
    /// </summary>
    private async Task<bool> MaybeSentAsync(Guid invoiceId)
    {
        try
        {
            var row = await _entities.RetrieveAsync(InvoiceEntity, invoiceId, ["statuscode"], CancellationToken.None).ConfigureAwait(false);
            return StatusOf(row) is InvoiceStatusCodes.SentToAp or InvoiceStatusCodes.Paid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AP export: the status of invoice {InvoiceId} could not be read after a failed move; its claim is kept", invoiceId);
            return true;
        }
    }

    /// <summary>True only when THIS call moved the invoice (<see cref="InvoiceTransitionOutcome.Transitioned"/>): an invoice that was already Sent to AP was moved by someone else.</summary>
    private async Task<bool> TrySendAsync(Guid invoiceId)
    {
        var ct = CancellationToken.None;
        try
        {
            var result = await _lifecycle.TransitionAsync(invoiceId, InvoiceLifecycleStatus.SentToAp, ct).ConfigureAwait(false);
            if (result.Outcome != InvoiceTransitionOutcome.Transitioned)
            {
                _logger.LogError("AP export: invoice {InvoiceId} was not moved to Sent to AP by this batch ({Outcome}, {Reason})", invoiceId, result.Outcome, result.Reason);
            }

            return result.Outcome == InvoiceTransitionOutcome.Transitioned;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AP export: the move of invoice {InvoiceId} to Sent to AP faulted", invoiceId);
            return false;
        }
    }

    private async Task CompleteAsync(Guid batchId, int count, decimal total, CancellationToken ct)
    {
        var fields = new Dictionary<string, object>
        {
            ["sprk_exportstatus"] = new OptionSetValue(StatusCompleted),
            ["sprk_invoicecount"] = count,
            ["sprk_totalamount"] = new Money(total),
        };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _entities.UpdateAsync(BatchEntity, batchId, fields, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt == 0)
            {
                _logger.LogWarning(ex, "AP export batch {BatchId}: marking it Completed failed; retrying once", batchId);
            }
            catch (Exception ex)
            {
                // Every invoice is already Sent to AP and the files exist: this must not look like a clean failure.
                _logger.LogError(ex, "AP export batch {BatchId}: the invoices were sent and the files written, but the batch could not be marked Completed", batchId);
                throw new CompletionException(batchId, ex);
            }
        }
    }

    private sealed class CompletionException(Guid batchId, Exception inner) : Exception($"Batch {batchId} could not be marked Completed.", inner);

    // =============================================================================================================================
    // Helpers
    // =============================================================================================================================

    private static (IReadOnlyList<Guid> Ids, IReadOnlyList<string> Formats)? Validate(ApExportRequest request)
    {
        if (request.InvoiceIds is not { Count: > 0 and <= MaxInvoicesPerBatch } ids || ids.Any(i => i == Guid.Empty) || ids.Distinct().Count() != ids.Count)
        {
            return null;
        }

        List<string?> formats = request.Formats is { Count: > 0 } requested
            ? requested.Select(f => f?.Trim().ToLowerInvariant()).ToList()
            : ["csv", "json"];
        if (formats.Any(f => f is not ("csv" or "json")))
        {
            return null;
        }

        return (ids, formats.Distinct().Order(StringComparer.Ordinal).Select(f => f!).ToList());
    }

    private async Task<Entity?> TryReadInvoiceAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveAsync(InvoiceEntity, id, SelectionColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (StoredFileNotFound.Is(ex))
        {
            return null;
        }
    }

    private static string? SelectionReason(Entity row) =>
        BatchOf(row) is not null ? ApExportCodes.ReasonAlreadyExported
        : StatusOf(row) != InvoiceStatusCodes.Approved ? ApExportCodes.ReasonNotApproved
        : VersionOf(row) is null ? ApExportCodes.ReasonNotFound
        : null;

    private static int? StatusOf(Entity row) => row.GetAttributeValue<OptionSetValue>("statuscode")?.Value;

    private static Guid? BatchOf(Entity row) => row.GetAttributeValue<EntityReference>(BatchLookup)?.Id;

    private static string? VersionOf(Entity row) =>
        !string.IsNullOrEmpty(row.RowVersion) ? row.RowVersion : row.Contains("versionnumber") ? Convert.ToString(row["versionnumber"], System.Globalization.CultureInfo.InvariantCulture) : null;

    internal static decimal? Dec(Entity row, string column) =>
        row.Contains(column)
            ? row[column] switch { Money m => m.Value, decimal d => d, int i => i, double db => (decimal)db, long l => l, _ => null }
            : null;

    internal static DateOnly? DateOf(Entity row, string column) =>
        row.GetAttributeValue<DateTime?>(column) is { } dt ? DateOnly.FromDateTime(dt) : null;

    /// <summary>A Dataverse date-time as a UTC instant: Dataverse returns UTC (kind Utc or Unspecified); a Local kind is converted.</summary>
    internal static DateTimeOffset InstantOf(DateTime value) =>
        new(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // =============================================================================================================================
    // The values of one invoice (read once, app-only: the caller's rights on the invoice were proven by the authorization seam)
    // =============================================================================================================================

    private sealed class ValueReader(IGenericEntityService entities, string batchName)
    {
        private readonly Dictionary<Guid, string?> _firmNames = [];
        private readonly Dictionary<Guid, string?> _codes = [];

        public async Task<ApExportInvoiceValues> ReadAsync(Entity invoice, CancellationToken ct)
        {
            var (coreType, coreLookup) = CoreOf(invoice);
            var values = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["batch.id"] = batchName,
                ["invoice.id"] = invoice.Id,
                ["firm.name"] = invoice.GetAttributeValue<EntityReference>("sprk_vendororg") is { } firm ? await FirmNameAsync(firm.Id, ct).ConfigureAwait(false) : null,
                ["invoice.firmInvoiceNumber"] = invoice.GetAttributeValue<string>("sprk_invoicenumber"),
                ["invoice.trackingNumber"] = invoice.GetAttributeValue<string>("sprk_trackingnumber"),
                ["invoice.invoiceDate"] = DateOf(invoice, "sprk_invoicedate"),
                ["invoice.dueDate"] = DateOf(invoice, "sprk_invoiceduedate"),
                ["invoice.billingPeriodStart"] = DateOf(invoice, "sprk_billingperiodstart"),
                ["invoice.billingPeriodEnd"] = DateOf(invoice, "sprk_billingperiodend"),
                ["invoice.currency"] = invoice.GetAttributeValue<string>("sprk_currency"),
                ["invoice.billedTotal"] = Dec(invoice, "sprk_totalamount"),
                ["invoice.lineAdjustmentsTotal"] = Dec(invoice, "sprk_lineadjustmentstotal"),
                ["invoice.invoiceAdjustmentsTotal"] = Dec(invoice, "sprk_invoiceadjustmentstotal"),
                ["invoice.approvedTotal"] = Dec(invoice, "sprk_approvedtotal"),
                ["record.type"] = coreLookup is null ? null : coreType,
                ["record.number"] = invoice.GetAttributeValue<string>("sprk_regardingrecordnumber"),
                ["record.name"] = invoice.GetAttributeValue<string>("sprk_regardingrecordname"),
                // The Law Department's own number for the core record is the record number; there is no separate field.
                ["record.lawDepartmentMatterNumber"] = invoice.GetAttributeValue<string>("sprk_regardingrecordnumber"),
            };
            foreach (var empty in ApExportSources.AlwaysEmptyInvoiceSources)
            {
                values[empty] = null;
            }

            await AddApprovalAsync(invoice.Id, values, ct).ConfigureAwait(false);
            return new ApExportInvoiceValues(invoice.GetAttributeValue<string>("sprk_invoicenumber") ?? string.Empty, invoice.Id, values, await ReadLinesAsync(invoice.Id, ct).ConfigureAwait(false));
        }

        private static (string Type, Guid? Id) CoreOf(Entity invoice)
        {
            foreach (var (column, label) in new[] { ("sprk_matter", "Matter"), ("sprk_project", "Project"), ("sprk_regardingworkassignment", "Work Assignment"), ("sprk_regardingagreement", "Agreement") })
            {
                if (invoice.GetAttributeValue<EntityReference>(column) is { } reference)
                {
                    return (label, reference.Id);
                }
            }

            return (string.Empty, null);
        }

        private async Task<string?> FirmNameAsync(Guid firmId, CancellationToken ct)
        {
            if (_firmNames.TryGetValue(firmId, out var cached))
            {
                return cached;
            }

            var firm = await entities.RetrieveAsync("sprk_organization", firmId, ["sprk_organizationname"], ct).ConfigureAwait(false);
            return _firmNames[firmId] = firm.GetAttributeValue<string>("sprk_organizationname");
        }

        private async Task AddApprovalAsync(Guid invoiceId, Dictionary<string, object?> values, CancellationToken ct)
        {
            var query = new QueryExpression("sprk_invoiceapproval")
            {
                ColumnSet = new ColumnSet("sprk_decidedby", "sprk_decidedon"),
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("sprk_invoice", ConditionOperator.Equal, invoiceId),
                        new ConditionExpression("sprk_approvalstatus", ConditionOperator.Equal, PortionApproved),
                    },
                },
            };
            query.AddOrder("sprk_decidedon", OrderType.Descending);
            var approval = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            string? approvedBy = null;
            if (approval?.GetAttributeValue<EntityReference>("sprk_decidedby") is { } contact)
            {
                approvedBy = (await entities.RetrieveAsync("contact", contact.Id, ["fullname"], ct).ConfigureAwait(false)).GetAttributeValue<string>("fullname");
            }

            values["approval.approvedBy"] = approvedBy;
            // The full instant, not a date: the stored value is a moment (Dataverse hands it back in UTC), and its UTC date can be a day off from the
            // approver's local date. ApExportWriter writes a DateTimeOffset as ISO 8601 UTC (2026-10-20T15:00:00Z), whatever the column's format says.
            values["approval.approvedOn"] = approval?.GetAttributeValue<DateTime?>("sprk_decidedon") is { } decidedOn ? InstantOf(decidedOn) : null;
        }

        /// <summary>
        /// The invoice's lines, read the way every other reader reads them: a removed (deactivated) Adjustment line is excluded by the query
        /// (<see cref="InvoiceTotalsService.ExcludeInactiveAdjustments"/>) and again here (<see cref="InvoiceTotalsService.IsCounted"/>), so the file never
        /// lists a line the invoice's totals do not count. One page of <see cref="ReadLimit"/> rows (Dataverse's cap); more than that fails the batch.
        /// </summary>
        private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadLinesAsync(Guid invoiceId, CancellationToken ct)
        {
            var query = new QueryExpression("sprk_billingevent")
            {
                ColumnSet = new ColumnSet(
                    "sprk_eventdate", "sprk_costtype", "sprk_timekeeper", "sprk_quantity", "sprk_rate", "sprk_amount", "sprk_approvedamount", "sprk_linestatus",
                    "sprk_linesequence", "sprk_taskcode", "sprk_activitycode", "sprk_expensecode", "sprk_taskcodeasbilled", "sprk_activitycodeasbilled",
                    "sprk_expensecodeasbilled", InvoiceTotalsService.LineState),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { PageNumber = 1, Count = ReadLimit },
            };
            query.Criteria.AddCondition("sprk_invoice", ConditionOperator.Equal, invoiceId);
            InvoiceTotalsService.ExcludeInactiveAdjustments(query);
            query.AddOrder("sprk_linesequence", OrderType.Ascending);
            query.AddOrder("sprk_billingeventid", OrderType.Ascending);
            var page = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            if (page.MoreRecords)
            {
                throw new InvalidOperationException($"Invoice {invoiceId} has more than {ReadLimit} lines; the export would silently drop some.");
            }

            var lines = page.Entities.Where(InvoiceTotalsService.IsCounted).ToList();
            var result = new List<IReadOnlyDictionary<string, object?>>();
            foreach (var line in lines.OrderBy(l => l.GetAttributeValue<int?>("sprk_linesequence") ?? int.MaxValue))
            {
                var costType = line.GetAttributeValue<OptionSetValue>("sprk_costtype")?.Value;
                var amount = Dec(line, "sprk_amount");
                decimal? billed, approved;
                if (costType == (int)InvoiceLineCostType.Adjustment)
                {
                    // An Adjustment line's sprk_amount is a positive deduction. Emitted as its SIGNED contribution to the totals, the way
                    // InvoiceTotalsCalculator subtracts it: to the approved total it contributes -amount (a deduction is negative); to the billed total
                    // it contributes nothing (the firm billed nothing for it; BilledTotal is fees + expenses). So the file's lines[].billed add up to the
                    // invoice's BilledTotal and lines[].approved add up to its ApprovedTotal, and approved - billed is the adjustment on every line type.
                    // Its own status is never read (only Fee and Expense lines are decided). 0 - x, never -x: a zero amount stays +0.
                    billed = 0m;
                    approved = amount is { } deduction ? 0m - deduction : null;
                }
                else
                {
                    billed = amount;
                    approved = line.GetAttributeValue<OptionSetValue>("sprk_linestatus")?.Value switch
                    {
                        (int)InvoiceLineStatus.Disallowed => 0m,
                        (int)InvoiceLineStatus.Reduced => Dec(line, "sprk_approvedamount") ?? billed,
                        _ => billed,
                    };
                }

                result.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["line.date"] = DateOf(line, "sprk_eventdate"),
                    ["line.type"] = costType switch
                    {
                        (int)InvoiceLineCostType.Fee => "Fee",
                        (int)InvoiceLineCostType.Expense => "Expense",
                        (int)InvoiceLineCostType.Adjustment => "Adjustment",
                        _ => null,
                    },
                    ["line.timekeeper"] = line.GetAttributeValue<string>("sprk_timekeeper"),
                    ["line.hours"] = Dec(line, "sprk_quantity"),
                    ["line.rate"] = Dec(line, "sprk_rate"),
                    ["line.billed"] = billed,
                    ["line.approved"] = approved,
                    ["line.taskCode"] = await CodeAsync(line, "sprk_taskcode", "sprk_taskcodeasbilled", ct).ConfigureAwait(false),
                    ["line.activityCode"] = await CodeAsync(line, "sprk_activitycode", "sprk_activitycodeasbilled", ct).ConfigureAwait(false),
                    ["line.expenseCode"] = await CodeAsync(line, "sprk_expensecode", "sprk_expensecodeasbilled", ct).ConfigureAwait(false),
                });
            }

            return result;
        }

        /// <summary>The resolved code's text; the as-billed text when the line has no resolved code.</summary>
        private async Task<string?> CodeAsync(Entity line, string lookup, string asBilled, CancellationToken ct)
        {
            if (line.GetAttributeValue<EntityReference>(lookup) is not { } reference)
            {
                return line.GetAttributeValue<string>(asBilled);
            }

            if (!_codes.TryGetValue(reference.Id, out var code))
            {
                var row = await entities.RetrieveAsync("sprk_billingcode", reference.Id, ["sprk_code"], ct).ConfigureAwait(false);
                _codes[reference.Id] = code = row.GetAttributeValue<string>("sprk_code");
            }

            return code ?? line.GetAttributeValue<string>(asBilled);
        }
    }
}
