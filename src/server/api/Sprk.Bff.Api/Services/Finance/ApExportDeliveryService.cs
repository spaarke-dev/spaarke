using System.Globalization;
using System.Net.Mail;
using System.Text;
using Azure;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Finance.Files;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Body of <c>POST /api/finance/export-batches/{batchId}/email</c>: up to <see cref="ApExportDeliveryService.MaxCc"/> CC addresses and an optional message.</summary>
public sealed record ApExportEmailRequest(IReadOnlyList<string>? Cc, string? Message);

/// <summary>
/// 200 body of Email to AP. <see cref="SenttoapDateSet"/> is true when THIS call set the batch's Sent to AP Date; false when the date was already
/// set (<see cref="SentToApDate"/> is then the earlier value, unchanged) or when the date could not be written after the send (it is then null: the
/// email WAS sent, so a retry must not send again unless the user asks).
/// </summary>
public sealed record ApExportEmailResponse(
    Guid? CommunicationId, string SentTo, IReadOnlyList<string> Cc, DateTimeOffset? SentToApDate, bool SenttoapDateSet);

/// <summary>200 body of Mark Sent to AP: the batch's Sent to AP Date, and whether it was already set (then it is unchanged).</summary>
public sealed record ApExportMarkSentResponse(DateTimeOffset? SentToApDate, bool AlreadySet);

/// <summary>What the delivery routes answer (the endpoint maps each to one HTTP status and one stable code, ADR-019).</summary>
public enum ApExportDeliveryOutcome
{
    /// <summary>200.</summary>
    Done,

    /// <summary>400 <c>ebilling.export.invalid_request</c>: a bad CC address, too many, or a message that is too long. Nothing was read or sent.</summary>
    InvalidRequest,

    /// <summary>The uniform 404: the batch does not exist.</summary>
    NotFound,

    /// <summary>409 <c>ebilling.export.ap_email_not_configured</c>.</summary>
    ApEmailNotConfigured,

    /// <summary>409 <c>ebilling.export.file_not_available</c>: the batch did not complete, or a file is missing, unscanned or unservable.</summary>
    FileNotAvailable,

    /// <summary>409 <c>ebilling.export.batch_not_completed</c> (Mark Sent to AP only).</summary>
    BatchNotCompleted,

    /// <summary>409 <c>ebilling.export.attachments_too_large</c>: the files do not fit the send path's attachment limits. Nothing was sent.</summary>
    AttachmentsTooLarge,

    /// <summary>503 <c>ebilling.export.email_send_failed</c>: the send faulted or was refused. The date was not set.</summary>
    SendFailed,

    /// <summary>503 <c>ebilling.export.unavailable</c>: a read or write other than the send faulted.</summary>
    Unavailable,
}

/// <summary>The delivery routes' answer: the outcome with its stable code and detail, or the body.</summary>
public sealed record ApExportDeliveryResult(
    ApExportDeliveryOutcome Outcome, string? Code = null, string? Detail = null, ApExportEmailResponse? Email = null, ApExportMarkSentResponse? MarkSent = null)
{
    internal static ApExportDeliveryResult Refused(ApExportDeliveryOutcome outcome, string code, string detail) => new(outcome, code, detail);
}

/// <summary>
/// The test seam over the communication send (<see cref="CommunicationService.SendAsync"/>, which is sealed): the one place Email to AP
/// reaches the mail path. Concrete (ADR-010), <c>virtual</c> only so a test can stand in for the Graph boundary, the
/// <c>InquiryEmailSender</c> precedent.
/// </summary>
public class ApExportMailer(CommunicationService communication)
{
    /// <summary>Sends as the signed-in user (the request's <see cref="SendCommunicationRequest.SendMode"/> is User). Throws on a refusal or a fault.</summary>
    /// <returns>The <c>sprk_communication</c> id when the core tracked one.</returns>
    public virtual async Task<Guid?> SendAsync(SendCommunicationRequest request, HttpContext httpContext, CancellationToken ct)
    {
        var response = await communication.SendAsync(request, httpContext, ct).ConfigureAwait(false);
        return response.CommunicationId;
    }
}

/// <summary>
/// Email to AP and Mark Sent to AP (ebilling-r1 task 151c-1a; EB-D-181, EB-D-182): sends a Completed batch's stored CSV and JSON to the AP address in the
/// eBilling settings as the signed-in user, and records on the batch (<c>sprk_senttoapdate</c>) when it was first sent to AP.
/// </summary>
/// <remarks>
/// <para><b>The files.</b> Read from the batch's Stored Files exactly as the download route reads them (the batch's file list, the row checked against the
/// batch and purpose, <see cref="StoredFileService.JudgeServableAsync"/>, the download audit event), never regenerated, and handed to the mail path as
/// in-memory attachments (<see cref="ServerSendOptions"/>). They are never copied into a document, a communication attachment row or an archive.</para>
/// <para><b>The record.</b> The sent <c>sprk_communication</c> is owned as a content row of the batch (the ownership resolver, never the default owner),
/// is internal-only, is not enriched, is not archived, and carries no regarding or lookup to the batch: linking it to the batch is task 151c-2.</para>
/// <para><b>The date.</b> Set only when empty, by a version-conditional write, and never overwritten. After the email went, a failure to write it is logged
/// at Error and the route still answers 200 (<see cref="ApExportEmailResponse.SenttoapDateSet"/> false), because a retry would send the email again.</para>
/// <para><b>The recipient</b> is read from the settings row with its own one-column query, not through <see cref="IEbillingSettingsReader"/>: that reader's
/// query names every settings column and serves every submission, so a column the schema script has not yet added must not be able to fail it.</para>
/// </remarks>
public sealed class ApExportDeliveryService
{
    /// <summary>The most CC addresses; the longest message.</summary>
    public const int MaxCc = 10, MaxMessageLength = 2000;

    /// <summary>The column this task adds to the settings table.</summary>
    internal const string ApEmailColumn = "sprk_apexportemail";

    /// <summary>The column this task adds to the batch table.</summary>
    internal const string SentToApColumn = "sprk_senttoapdate";

    private readonly ApExportService _batches;
    private readonly IGenericEntityService _entities;
    private readonly StoredFileService _files;
    private readonly ApExportMailer _mailer;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApExportDeliveryService> _logger;

    public ApExportDeliveryService(
        ApExportService batches,
        IGenericEntityService entities,
        StoredFileService files,
        ApExportMailer mailer,
        ILogger<ApExportDeliveryService> logger,
        TimeProvider? clock = null)
    {
        _batches = batches ?? throw new ArgumentNullException(nameof(batches));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _mailer = mailer ?? throw new ArgumentNullException(nameof(mailer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? TimeProvider.System;
    }

    // =============================================================================================================================
    // Email to AP
    // =============================================================================================================================

    /// <summary>Emails the batch's files to AP as the caller (see the type remarks for the order and the answers).</summary>
    public async Task<ApExportDeliveryResult> EmailAsync(
        Guid batchId, ApExportEmailRequest? request, Guid callerSystemUserId, HttpContext httpContext, CancellationToken ct)
    {
        // (1) The body, before anything is read: a bad request names nothing about the batch.
        var cc = ValidateRequest(request, out var problem);
        if (problem is not null)
        {
            return ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.InvalidRequest, ApExportCodes.InvalidRequest, problem);
        }

        try
        {
            // (2) The batch, then the recipient.
            var batch = await _batches.GetBatchAsync(batchId, ct).ConfigureAwait(false);
            if (batch is null)
            {
                return new ApExportDeliveryResult(ApExportDeliveryOutcome.NotFound);
            }

            var to = await ReadApAddressAsync(ct).ConfigureAwait(false);
            if (to is null)
            {
                return ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.ApEmailNotConfigured, ApExportCodes.ApEmailNotConfigured,
                    "No AP export email address is set in the eBilling settings, so nothing was sent. Ask an administrator to set it.");
            }

            // (3) A Completed batch with every file it produced, servable.
            if (batch.Status != "Completed" || batch.Formats.Count == 0)
            {
                return FileNotAvailable("This batch did not complete, so it has no files to send.");
            }

            var listed = new List<ApExportFileDto>(batch.Formats.Count);
            foreach (var format in batch.Formats.Distinct())
            {
                var file = batch.Files.FirstOrDefault(f => f.Format == format);
                if (file is null)
                {
                    return FileNotAvailable($"This batch has no {format} file to send.");
                }

                listed.Add(file);
            }

            // (4) The bytes, under the send path's total-size limit (checked as they are read, so an oversize file is never fully loaded).
            var attachments = new List<ServerAttachment>(listed.Count);
            long total = 0;
            foreach (var file in listed.OrderBy(f => f.Format, StringComparer.Ordinal))
            {
                var read = await ReadFileAsync(batchId, file, callerSystemUserId, CommunicationService.MaxTotalAttachmentSizeBytes - total, ct).ConfigureAwait(false);
                if (read.Refusal is not null)
                {
                    return read.Refusal;
                }

                total += read.Bytes!.Length;
                attachments.Add(new ServerAttachment(
                    $"ap-export-{batchId:D}.{file.Format}", file.Format == "csv" ? "text/csv" : "application/json", read.Bytes));
            }


            // (5) The send.
            var currencies = await ReadCurrenciesAsync(batchId, ct).ConfigureAwait(false);
            var send = BuildRequest(batch, to, cc, request?.Message, currencies, attachments, batchId);
            Guid? communicationId;
            try
            {
                communicationId = await _mailer.SendAsync(send, httpContext, ct).ConfigureAwait(false);
            }
            catch (SdapProblemException ex) when (ex.Code == "ATTACHMENT_LIMIT_EXCEEDED")
            {
                _logger.LogWarning("Email to AP: batch {BatchId} attachments exceed the send path's limits (nothing sent)", batchId);
                return TooLarge();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Nothing the caller can fix by changing the request; the date is NOT set, so the user can try again.
                _logger.LogError(ex, "Email to AP: sending batch {BatchId} faulted or was refused; the Sent to AP Date was not set", batchId);
                return ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.SendFailed, ApExportCodes.EmailSendFailed,
                    "The email could not be sent. Nothing was recorded; try again.");
            }

            // (6) The email went. The date is best effort from here: a failure is logged and the answer is still 200.
            var dateSet = false;
            DateTimeOffset? sentToAp = null;
            try
            {
                (sentToAp, dateSet) = await SetSentToApDateIfEmptyAsync(batchId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(
                    ex, "Email to AP: batch {BatchId} WAS emailed (communication {CommunicationId}) but its Sent to AP Date could not be written; do not send it again",
                    batchId, communicationId);
            }

            return new ApExportDeliveryResult(
                ApExportDeliveryOutcome.Done, Email: new ApExportEmailResponse(communicationId, to, cc, sentToAp, dateSet));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Email to AP: reading batch {BatchId}, the settings or its files faulted; nothing was sent", batchId);
            return ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.Unavailable, ApExportCodes.Unavailable,
                "The batch could not be read, so nothing was sent. Try again.");
        }
    }

    private static ApExportDeliveryResult FileNotAvailable(string detail) =>
        ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.FileNotAvailable, ApExportCodes.FileNotAvailable, detail);

    private static ApExportDeliveryResult TooLarge() =>
        ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.AttachmentsTooLarge, ApExportCodes.AttachmentsTooLarge,
            $"The batch's files are larger than an email can carry ({CommunicationService.MaxTotalAttachmentSizeBytes / (1024 * 1024)} MB). Nothing was sent. Download the files and deliver them another way, then use Mark Sent to AP.");

    /// <summary>The CC addresses, trimmed and de-duplicated; <paramref name="problem"/> says what is wrong with the request, if anything.</summary>
    internal static List<string> ValidateRequest(ApExportEmailRequest? request, out string? problem)
    {
        problem = null;
        var cc = new List<string>();
        if (request?.Message is { } message && message.Length > MaxMessageLength)
        {
            problem = $"The message can be at most {MaxMessageLength} characters.";
            return cc;
        }

        if (request?.Cc is not { } list)
        {
            return cc;
        }

        if (list.Count > MaxCc)
        {
            problem = $"At most {MaxCc} CC addresses are allowed.";
            return cc;
        }

        foreach (var raw in list)
        {
            var address = raw?.Trim();
            if (!IsValidAddress(address))
            {
                problem = "Every CC must be a valid email address.";
                return [];
            }

            if (!cc.Contains(address!, StringComparer.OrdinalIgnoreCase))
            {
                cc.Add(address!);
            }
        }

        return cc;
    }

    /// <summary>A bare address (<c>name@domain</c>): no display name, whitespace or control characters, at most 320 characters.</summary>
    internal static bool IsValidAddress(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= 320
        && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>' or ',' or ';' or '"')
        && MailAddress.TryCreate(value, out var parsed)
        && string.Equals(parsed.Address, value, StringComparison.Ordinal);

    private async Task<string?> ReadApAddressAsync(CancellationToken ct)
    {
        var query = new QueryExpression(EbillingSettingsReader.EntityName)
        {
            ColumnSet = new ColumnSet(ApEmailColumn),
            TopCount = 2,
            Criteria = { Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) } },
        };
        query.AddOrder("createdon", OrderType.Ascending);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var address = rows.Entities.FirstOrDefault()?.GetAttributeValue<string>(ApEmailColumn)?.Trim();
        return IsValidAddress(address) ? address : null;
    }

    private async Task<List<string>> ReadCurrenciesAsync(Guid batchId, CancellationToken ct)
    {
        var query = new QueryExpression(ApExportService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet("sprk_currency"),
            TopCount = 5000,
            Criteria = { Conditions = { new ConditionExpression(ApExportService.BatchLookup, ConditionOperator.Equal, batchId) } },
        };
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return rows.Entities
            .Select(r => r.GetAttributeValue<string>("sprk_currency")?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<(byte[]? Bytes, ApExportDeliveryResult? Refusal)> ReadFileAsync(
        Guid batchId, ApExportFileDto listed, Guid callerSystemUserId, long budget, CancellationToken ct)
    {
        var row = await _files.ReadRowAsync(listed.FileId, ct).ConfigureAwait(false);
        if (row is null || row.Purpose != StoredFilePurpose.ExportFile || row.Parent != new StoredFileParent(StoredFileParentKind.BillingExport, batchId))
        {
            return (null, FileNotAvailable($"This batch has no {listed.Format} file to send."));
        }

        var decision = await _files.JudgeServableAsync(row, callerSystemUserId, ct).ConfigureAwait(false);
        switch (decision.Outcome)
        {
            case StoredFileDownloadOutcome.Serve:
                break;
            case StoredFileDownloadOutcome.ScanPending:
                return (null, FileNotAvailable($"The {listed.Format} file has not finished its security scan yet. Try again shortly."));
            case StoredFileDownloadOutcome.Unavailable:
                return (null, ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.Unavailable, ApExportCodes.Unavailable,
                    "The file store is not available, so nothing was sent. Try again."));
            default:
                return (null, FileNotAvailable($"This batch has no {listed.Format} file to send."));
        }

        Stream? stream;
        try
        {
            stream = await _files.OpenServableAsync(decision.File!, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status412PreconditionFailed)
        {
            // Changed since its verdict was read: the new content has none yet.
            return (null, FileNotAvailable($"The {listed.Format} file has not finished its security scan yet. Try again shortly."));
        }

        if (stream is null)
        {
            return (null, FileNotAvailable($"This batch has no {listed.Format} file to send."));
        }

        // The same audit event the download route writes: the files are read on the caller's behalf.
        StoredFileAudit.LogDownloaded(
            _logger, decision.File!.FileId, decision.File.Parent.Table, decision.File.Parent.Id,
            StoredFileAudit.CallerSystemUser, decision.File.CallerSystemUserId, StoredFileAudit.RouteInternal);

        await using (stream)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > budget)
                {
                    return (null, TooLarge());
                }

                buffer.Write(chunk, 0, read);
            }

            return (buffer.ToArray(), null);
        }
    }

    /// <summary>
    /// The send request: to the AP address, the caller's CCs, the summary in the subject and body, both files as server-only attachments. No
    /// association (so no regarding or lookup to the batch; 151c-2 adds a plain batch reference), internal-only, not archived.
    /// </summary>
    internal static SendCommunicationRequest BuildRequest(
        ApExportBatchDto batch, string to, IReadOnlyList<string> cc, string? message, IReadOnlyList<string> currencies,
        IReadOnlyList<ServerAttachment> attachments, Guid batchId)
    {
        var currency = currencies.Count switch
        {
            0 => "no currency recorded",
            1 => currencies[0],
            _ => "mixed currencies: " + string.Join(", ", currencies),
        };
        var total = batch.TotalAmount.ToString("N2", CultureInfo.InvariantCulture);
        var invoices = batch.InvoiceCount == 1 ? "1 invoice" : $"{batch.InvoiceCount.ToString(CultureInfo.InvariantCulture)} invoices";

        var body = new StringBuilder();
        body.Append("AP export batch ").Append(batch.Name).Append('\n');
        body.Append("Invoices: ").Append(batch.InvoiceCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        body.Append("Total: ").Append(total).Append(' ').Append(currency).Append('\n');
        body.Append("Files attached: ").Append(string.Join(", ", attachments.Select(a => a.Name))).Append('\n');
        if (!string.IsNullOrWhiteSpace(message))
        {
            body.Append('\n').Append(message.Trim()).Append('\n');
        }

        return new SendCommunicationRequest
        {
            To = [to],
            Cc = cc.Count == 0 ? null : cc.ToArray(),
            Subject = $"AP export {batch.Name}: {invoices}, {total} {currency}",
            Body = body.ToString(),
            BodyFormat = BodyFormat.PlainText,
            CommunicationType = CommunicationType.Email,
            SendMode = SendMode.User,
            ArchiveToSpe = false,
            IsInternalOnly = true,
            ServerSend = new ServerSendOptions
            {
                Attachments = attachments,
                OwnerFollows = new ServerOwnerRecord(ApExportService.BatchEntity, batchId),
                SkipEnrichment = true,
            },
        };
    }

    // =============================================================================================================================
    // Mark Sent to AP
    // =============================================================================================================================

    /// <summary>Records that the batch was delivered another way: sets the date when it is empty; otherwise leaves it (idempotent).</summary>
    public async Task<ApExportDeliveryResult> MarkSentAsync(Guid batchId, CancellationToken ct)
    {
        try
        {
            Entity batch;
            try
            {
                batch = await _entities.RetrieveAsync(ApExportService.BatchEntity, batchId, ["sprk_exportstatus"], ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (StoredFileNotFound.Is(ex))
            {
                return new ApExportDeliveryResult(ApExportDeliveryOutcome.NotFound);
            }

            if (batch.GetAttributeValue<OptionSetValue>("sprk_exportstatus")?.Value != ApExportService.StatusCompleted)
            {
                return ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.BatchNotCompleted, ApExportCodes.BatchNotCompleted,
                    "Only a Completed batch can be marked as sent to AP.");
            }

            var (date, set) = await SetSentToApDateIfEmptyAsync(batchId, ct).ConfigureAwait(false);
            return new ApExportDeliveryResult(ApExportDeliveryOutcome.Done, MarkSent: new ApExportMarkSentResponse(date, AlreadySet: !set));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Mark Sent to AP: batch {BatchId} faulted; its Sent to AP Date may not be set", batchId);
            return ApExportDeliveryResult.Refused(ApExportDeliveryOutcome.Unavailable, ApExportCodes.Unavailable,
                "The batch could not be updated. Try again.");
        }
    }

    /// <summary>
    /// Sets <c>sprk_senttoapdate</c> to now when it is empty, with a write conditional on the version read, so two callers cannot both set it and a
    /// value is never overwritten. When a rival wins, the read is repeated once and its value is returned (not set by this call).
    /// </summary>
    private async Task<(DateTimeOffset? Date, bool Set)> SetSentToApDateIfEmptyAsync(Guid batchId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var batch = await _entities.RetrieveAsync(ApExportService.BatchEntity, batchId, [SentToApColumn, "versionnumber"], ct).ConfigureAwait(false);
            if (batch.GetAttributeValue<DateTime?>(SentToApColumn) is { } existing)
            {
                return (ApExportService.InstantOf(existing), false);
            }

            var version = batch.RowVersion
                ?? (batch.GetAttributeValue<long?>("versionnumber") is { } number ? number.ToString(CultureInfo.InvariantCulture) : null);
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException("The batch carries no row version, so its Sent to AP Date cannot be written conditionally.");
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            try
            {
                await _entities.UpdateIfRowVersionMatchesAsync(
                    ApExportService.BatchEntity, batchId, version, new Dictionary<string, object> { [SentToApColumn] = now }, ct).ConfigureAwait(false);
                return (new DateTimeOffset(now, TimeSpan.Zero), true);
            }
            catch (System.Data.DBConcurrencyException) when (attempt == 0)
            {
                // The batch changed since it was read (possibly by a rival that set the date): read it again.
            }
        }
    }
}
