using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The codes the from-email route adds (ebilling-r1 task 095; wire contract <c>notes/095-from-email-contract.md</c> sections 6 and 9,
/// agreed with word-add-in-r1). Kept apart from <see cref="InvoiceSubmissionErrors"/>, which the intake owns; the codes it shares
/// (<c>firm_invalid</c>, <c>firm_not_linked</c>, <c>firm_invoice_number_required</c>, <c>document_already_submitted</c>,
/// <c>submission_already_ended</c>, <c>document_core_record_mismatch</c>, <c>record_unavailable</c>, <c>failed</c>) are referenced there.
/// </summary>
public static class FromEmailSubmissionErrors
{
    /// <summary>400: the marks break a rule; <c>reason</c> is one of <see cref="FromEmailMarksReasons"/>, <c>fileName</c> when one file.</summary>
    public const string MarksInvalid = "ebilling.submission.marks_invalid";

    /// <summary>422: an Invoice or Supporting mark has no entry, or its entry has no usable <c>contentBase64</c>; <c>fileName</c>, <c>mark</c>.</summary>
    public const string MarkedFileMissing = "ebilling.submission.marked_file_missing";

    /// <summary>422, permanent: the email cannot be identified; <c>reason</c> is one of <see cref="FromEmailUnidentifiableReasons"/>.</summary>
    public const string EmailUnidentifiable = "ebilling.submission.email_unidentifiable";

    /// <summary>
    /// 503, retryable: Graph timed out, throttled or failed. Pinned by the contract; this route has NO producer today: with the RFC
    /// Message-ID required (OP-2) and every marked file's bytes required (step 0), the save makes no Graph call that decides anything
    /// (the sent-time read is non-fatal). Recorded in notes/095-submit-invoice-from-email.md, "Contract items".
    /// </summary>
    public const string EmailUnavailable = "ebilling.submission.email_unavailable";

    /// <summary>409: a Correction is refused; <c>reason</c> is one of <see cref="FromEmailCorrectionReasons"/>.</summary>
    public const string CorrectionNotAllowed = "ebilling.submission.correction_not_allowed";

    /// <summary>Outcome code (contract section 9): the <c>.eml</c> save did not finish, so the submission ended Failed.</summary>
    public const string EmailSaveFailed = "ebilling.submission.email_save_failed";

    /// <summary>
    /// 422, permanent (095 review F1, added 2026-10-28): the <c>.eml</c> as built does not carry every marked file byte-for-byte, so the
    /// save was refused after the build and before the upload; nothing was saved and nothing was submitted.
    /// </summary>
    public const string AttachmentNotPreserved = "ebilling.submission.attachment_not_preserved";
}

/// <summary>The <c>reason</c> values of 400 <see cref="FromEmailSubmissionErrors.MarksInvalid"/> (contract section 6).</summary>
public static class FromEmailMarksReasons
{
    public const string InvoiceCount = "invoice_count";
    public const string UnknownMark = "unknown_mark";
    public const string InlineAttachment = "inline_attachment";
    public const string UnmarkedAttachment = "unmarked_attachment";
    public const string DuplicateMark = "duplicate_mark";
    public const string AmbiguousAttachment = "ambiguous_attachment";
    public const string InvoiceNotPdf = "invoice_not_pdf";
}

/// <summary>The <c>reason</c> values of 422 <see cref="FromEmailSubmissionErrors.EmailUnidentifiable"/> (contract section 6).</summary>
public static class FromEmailUnidentifiableReasons
{
    public const string NoMessageId = "no_message_id";
    public const string EmailNotFound = "email_not_found";
    public const string EarlierSaveUnlinked = "earlier_save_unlinked";
    public const string NoCommunication = "no_communication";
    public const string EmlAmbiguous = "eml_ambiguous";
}

/// <summary>The <c>reason</c> values of 409 <see cref="FromEmailSubmissionErrors.CorrectionNotAllowed"/>; <see cref="NotAvailable"/> until task 097 (OP-7).</summary>
public static class FromEmailCorrectionReasons
{
    public const string NotAvailable = "not_available";
    public const string InvoiceStatus = "invoice_status";
    public const string DifferentFirm = "different_firm";
    public const string DifferentCoreRecord = "different_core_record";
}

/// <summary>The mark values (exact case, contract section 2).</summary>
public static class FromEmailMarks
{
    public const string Invoice = "Invoice";
    public const string Supporting = "Supporting";
    public const string DontSave = "DontSave";

    internal static bool IsKnown(string? mark) => mark is Invoice or Supporting or DontSave;

    internal static bool IsSaved(string mark) => mark is Invoice or Supporting;
}

/// <summary>The mode values (exact case, contract section 2).</summary>
public static class FromEmailModes
{
    public const string New = "New";
    public const string Correction = "Correction";
}

/// <summary>One mark as sent: the attachment's identity (<c>fileName</c>, <c>size</c>, <c>contentType</c>, OP-3) and its mark.</summary>
public sealed record FromEmailMark
{
    public string? FileName { get; init; }
    public long? Size { get; init; }
    public string? ContentType { get; init; }
    public string? Mark { get; init; }
}

/// <summary>The from-email request after the body-shape check (every required field present; the target normalized).</summary>
internal sealed record FromEmailSubmissionInput
{
    public required InvoiceCoreRecord CoreRecord { get; init; }

    /// <summary>The target's friendly entity type for the in-process save (<c>Matter</c>, <c>Project</c>, <c>WorkAssignment</c>).</summary>
    public required string SaveEntityType { get; init; }

    public required EmailMetadata Email { get; init; }
    public required IReadOnlyList<FromEmailMark> Marks { get; init; }
    public required Guid FirmId { get; init; }
    public string? FirmInvoiceNumber { get; init; }
    public string? PoNumber { get; init; }
    public required string Mode { get; init; }
    public Guid? InvoiceId { get; init; }

    /// <summary>The RFC Message-ID (OP-2: required and RFC-shaped by the body check).</summary>
    public string MessageId => Sprk.Bff.Api.Services.Office.OfficeEmailEnricher.ResolveRfcMessageId(Email)!;
}

/// <summary>A refusal of the marks (400) or of the bytes (422), with the fields the contract puts beside the code.</summary>
internal sealed record FromEmailMarksRefusal(int Status, string ErrorCode, string Reason, string? FileName, string? Mark, string Detail);

/// <summary>A marked attachment the marks rules accepted, with its entry and, for Invoice and Supporting, its decoded bytes' SHA-256.</summary>
internal sealed record FromEmailResolvedMark(FromEmailMark Mark, AttachmentReference Entry, int EntryIndex, string Sha256);

/// <summary>
/// The marks rules (model §9.1 "Marks", contract section 6), pure. <see cref="Check"/> runs the 400 rules then the 422 bytes rule,
/// in the contract's order, and returns either the refusal or the Invoice and Supporting marks resolved to their entries.
/// </summary>
internal static class FromEmailMarkRules
{
    /// <summary>
    /// The 400 rules, in this order: <c>unknown_mark</c>, <c>ambiguous_attachment</c>, <c>inline_attachment</c>, <c>duplicate_mark</c>,
    /// <c>unmarked_attachment</c>, <c>invoice_count</c>, <c>invoice_not_pdf</c>; then the 422 <c>marked_file_missing</c>. A mark matches the
    /// non-inline entry with the same <c>fileName</c> (ordinal), <c>size</c> and <c>contentType</c> (ignore case). A Don't-save mark that
    /// matches no entry is accepted.
    /// </summary>
    public static (FromEmailMarksRefusal? Refusal, IReadOnlyList<FromEmailResolvedMark> Saved) Check(
        IReadOnlyList<AttachmentReference> attachments, IReadOnlyList<FromEmailMark> marks)
    {
        var none = Array.Empty<FromEmailResolvedMark>();

        foreach (var mark in marks)
        {
            if (!FromEmailMarks.IsKnown(mark.Mark))
            {
                return (Invalid(FromEmailMarksReasons.UnknownMark, mark.FileName,
                    "Each attachment is marked Invoice, Supporting or DontSave."), none);
            }
        }

        var entries = attachments.Select((a, i) => (Entry: a, Index: i)).ToList();
        var nonInline = entries.Where(e => !e.Entry.IsInline).ToList();

        // OP-3: two non-inline attachments equal on all three identity fields cannot be told apart.
        if (nonInline.GroupBy(e => (e.Entry.FileName, e.Entry.Size, (e.Entry.ContentType ?? string.Empty).ToUpperInvariant()))
                .FirstOrDefault(g => g.Count() > 1) is { } twins)
        {
            return (Invalid(FromEmailMarksReasons.AmbiguousAttachment, twins.Key.FileName,
                "Two attachments have the same name, size and type, so a mark cannot tell them apart."), none);
        }

        var markedEntry = new Dictionary<int, FromEmailMark>();
        var matched = new List<(FromEmailMark Mark, (AttachmentReference Entry, int Index)? Entry)>();
        foreach (var mark in marks)
        {
            var hit = nonInline.Where(e => Matches(mark, e.Entry)).ToList();
            if (hit.Count == 0)
            {
                if (entries.Any(e => e.Entry.IsInline && Matches(mark, e.Entry)))
                {
                    return (Invalid(FromEmailMarksReasons.InlineAttachment, mark.FileName,
                        "An inline picture in the email body cannot be marked."), none);
                }

                matched.Add((mark, null));
                continue;
            }

            var entry = hit[0];
            if (markedEntry.ContainsKey(entry.Index))
            {
                return (Invalid(FromEmailMarksReasons.DuplicateMark, mark.FileName, "An attachment is marked more than once."), none);
            }

            markedEntry[entry.Index] = mark;
            matched.Add((mark, entry));
        }

        if (nonInline.FirstOrDefault(e => !markedEntry.ContainsKey(e.Index)) is { Entry: not null } unmarked)
        {
            return (Invalid(FromEmailMarksReasons.UnmarkedAttachment, unmarked.Entry.FileName,
                "Every attachment is marked Invoice, Supporting or DontSave."), none);
        }

        var invoices = marks.Where(m => m.Mark == FromEmailMarks.Invoice).ToList();
        if (invoices.Count != 1)
        {
            return (Invalid(FromEmailMarksReasons.InvoiceCount, null, "Exactly one attachment is marked Invoice."), none);
        }

        var invoice = invoices[0];
        if (!IsPdf(invoice))
        {
            return (Invalid(FromEmailMarksReasons.InvoiceNotPdf, invoice.FileName, "The attachment marked Invoice must be a PDF."), none);
        }

        // Step 0 (422): every Invoice and Supporting attachment is present with bytes the .eml can carry.
        var saved = new List<FromEmailResolvedMark>();
        foreach (var (mark, entry) in matched.Where(m => FromEmailMarks.IsSaved(m.Mark.Mark!)))
        {
            if (entry is not { } found || !EmlMimeParts.TryCarriedBytes(found.Entry.ContentBase64, found.Entry.ContentType, out var bytes))
            {
                return (new FromEmailMarksRefusal(StatusCodes.Status422UnprocessableEntity, FromEmailSubmissionErrors.MarkedFileMissing,
                    Reason: string.Empty, mark.FileName, mark.Mark,
                    "A file marked to be saved was not sent with its content, so nothing was saved."), none);
            }

            saved.Add(new FromEmailResolvedMark(mark, found.Entry, found.Index, EmlMimeParts.Sha256Hex(bytes)));
        }

        // The Invoice first, then the Supporting files in attachment order.
        return (null, saved.OrderBy(s => s.Mark.Mark == FromEmailMarks.Invoice ? 0 : 1).ThenBy(s => s.EntryIndex).ToList());
    }

    /// <summary>Contract section 6 <c>invoice_not_pdf</c>: a PDF unless the content type lacks "pdf" AND the name does not end in ".pdf".</summary>
    internal static bool IsPdf(FromEmailMark invoice) =>
        (invoice.ContentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) ?? false)
        || (invoice.FileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool Matches(FromEmailMark mark, AttachmentReference entry) =>
        string.Equals(mark.FileName, entry.FileName, StringComparison.Ordinal)
        && mark.Size == entry.Size
        && string.Equals(mark.ContentType, entry.ContentType, StringComparison.OrdinalIgnoreCase);

    private static FromEmailMarksRefusal Invalid(string reason, string? fileName, string detail) =>
        new(StatusCodes.Status400BadRequest, FromEmailSubmissionErrors.MarksInvalid, reason, fileName, null, detail);
}

/// <summary>
/// The idempotency key K (model §9.1 "Idempotency key", contract section 4) and the ids it derives. K has no caller (OP-5).
/// </summary>
internal static class FromEmailSubmissionKey
{
    /// <summary>The highest generation a K may reach (EB-D-160 re-attempts after Failed); past it the route answers 409 submission_already_ended.</summary>
    internal const int MaxGeneration = 50;

    /// <summary>
    /// K: a canonical JSON array, version 2: the RFC Message-ID; the core record (kind, id); the mode; the invoice id (empty for New); and
    /// every mark as [fileName, size, contentType, mark], sorted ordinal by fileName, then size, contentType, mark (OP-3). The content type
    /// is compared ignoring case (contract section 2), so it is upper-cased in K.
    /// <para>The firm and the firm invoice number are NOT in K (095 review F2, contract section 5 "other details differ"): the same email,
    /// record and marking with another firm or number is the SAME submission, answered 409 document_already_submitted while its latest
    /// attempt is not Failed, and a new attempt with the new details after Failed. Version 1 carried them, which made a second invoice
    /// for one email. They stay in the response-cache binding (<see cref="CacheBinding"/>).</para>
    /// </summary>
    public static string Of(FromEmailSubmissionInput input)
    {
        var marks = input.Marks
            .Select(m => (FileName: m.FileName ?? string.Empty, Size: m.Size ?? -1, ContentType: (m.ContentType ?? string.Empty).ToUpperInvariant(),
                Mark: m.Mark ?? string.Empty))
            .OrderBy(m => m.FileName, StringComparer.Ordinal)
            .ThenBy(m => m.Size)
            .ThenBy(m => m.ContentType, StringComparer.Ordinal)
            .ThenBy(m => m.Mark, StringComparer.Ordinal)
            .ToList();

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            json.WriteStringValue("v2");
            json.WriteStringValue(input.MessageId);
            json.WriteStringValue(input.CoreRecord.Kind.ToString());
            json.WriteStringValue(input.CoreRecord.RecordId.ToString("D"));
            json.WriteStringValue(input.Mode);
            json.WriteStringValue(input.InvoiceId is { } invoiceId ? invoiceId.ToString("D") : string.Empty);
            json.WriteStartArray();
            foreach (var mark in marks)
            {
                json.WriteStartArray();
                json.WriteStringValue(mark.FileName);
                json.WriteNumberValue(mark.Size);
                json.WriteStringValue(mark.ContentType);
                json.WriteStringValue(mark.Mark);
                json.WriteEndArray();
            }

            json.WriteEndArray();
            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The submission id of attempt <paramref name="generation"/>: generation 1 is the contract's derivation, later ones append it.</summary>
    public static Guid SubmissionIdFor(string k, int generation) =>
        InvoiceSubmissionPersister.IdOf("ebilling-from-email-submission-" + k + GenerationSuffix(generation));

    /// <summary>The job id of attempt <paramref name="generation"/>.</summary>
    public static Guid JobIdFor(string k, int generation) =>
        InvoiceSubmissionPersister.IdOf("ebilling-from-email-job-" + k + GenerationSuffix(generation));

    /// <summary>
    /// The value the <c>X-Idempotency-Key</c> response cache is bound to (the filter hashes it): K plus the firm and the trimmed firm invoice
    /// number, so a header reused on a request with another firm or number is never answered with the first request's cached 202; it
    /// reaches the handler, which answers it from K (contract section 5).
    /// </summary>
    public static string CacheBinding(FromEmailSubmissionInput input) =>
        Of(input) + "|" + input.FirmId.ToString("D") + "|" + (input.FirmInvoiceNumber?.Trim() ?? string.Empty);

    /// <summary>Whether an attempt row recorded exactly this firm and trimmed firm invoice number (the details K does not carry).</summary>
    public static bool SameFirmDetails(Guid? rowFirm, string? rowNumber, FromEmailSubmissionInput input) =>
        rowFirm == input.FirmId
        && string.Equals(rowNumber?.Trim(), input.FirmInvoiceNumber?.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// The Stored File a marked part of attempt <paramref name="submissionId"/> is staged under (092's file-store intake), so a retried
    /// stage meets the blob the first one left. The Invoice part is <see cref="Files.StoredFileIds.ForUpload"/>, the id 092's upload stages
    /// its one PDF under. A Supporting part is <see cref="Files.StoredFileIds.ForAttachment"/>, keyed by its ordinal in the <c>.eml</c>
    /// (<c>eml-part-{ordinal}</c>).
    /// </summary>
    public static Guid StoredFileIdFor(Guid submissionId, FromEmailMarkedPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return part.Mark == FromEmailMarks.Invoice
            ? Files.StoredFileIds.ForUpload(submissionId)
            : Files.StoredFileIds.ForAttachment(submissionId,
                "eml-part-" + part.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string GenerationSuffix(int generation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);
        return generation == 1 ? string.Empty : "|gen" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>The 202 body of the from-email route: <see cref="Api.Finance.InvoiceSubmissionAccepted"/>'s four fields and <c>emlDocumentId</c> (contract section 3).</summary>
public sealed record InvoiceSubmissionFromEmailAccepted(Guid SubmissionId, Guid JobId, string TrackingNumber, string StatusUrl, Guid EmlDocumentId);

/// <summary>One firm of a core record (contract section 8).</summary>
public sealed record CoreRecordFirm(Guid FirmId, string Name);
