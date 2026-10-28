using System.Globalization;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Finance.Files;

// ebilling-r1 task 090 (procedure-and-file-model sections 4.1-4.3; EB-D-148). The contracts of the Finance file store: the
// sprk_storedfile schema names and option values (088, verified live on dev 2026-10-22), the deterministic ids, the ONE canonical
// source-version string, the checked size conversion and the blob-name rule. Pure: no I/O, so every rule is asserted directly.

/// <summary><c>sprk_storedfile.sprk_purpose</c> (088).</summary>
public enum StoredFilePurpose
{
    /// <summary>A Procedure Version's source file. Container <c>procedures</c>; parent <c>sprk_procedureversion</c>.</summary>
    ProcedureSource = 100000000,

    /// <summary>An invoice PDF. Container <c>invoice-files</c>; parent <c>sprk_invoice</c>.</summary>
    InvoicePdf = 100000001,

    /// <summary>An invoice supporting file. Container <c>invoice-files</c>; parent <c>sprk_invoice</c>.</summary>
    InvoiceSupportingFile = 100000002,

    /// <summary>An export file. Container <c>billing-exports</c>; parent <c>sprk_billingexport</c>.</summary>
    ExportFile = 100000003,
}

/// <summary><c>sprk_storedfile.sprk_filestatus</c> (088).</summary>
public enum StoredFileStatus
{
    /// <summary>The row exists but its bytes are not yet verified; never served.</summary>
    NotYetServable = 100000000,

    /// <summary>The blob exists and its SHA-256 matched <c>sprk_sha256</c> at the flip.</summary>
    Servable = 100000001,

    /// <summary>Withdrawn; never served.</summary>
    Removed = 100000002,
}

/// <summary>The table a Stored File hangs under. Exactly one parent lookup is set on a row.</summary>
public enum StoredFileParentKind
{
    /// <summary><c>sprk_procedureversion</c>.</summary>
    ProcedureVersion,

    /// <summary><c>sprk_invoice</c>.</summary>
    Invoice,

    /// <summary><c>sprk_billingexport</c>.</summary>
    BillingExport,
}

/// <summary>A Stored File's parent: its table and id.</summary>
public readonly record struct StoredFileParent(StoredFileParentKind Kind, Guid Id)
{
    /// <summary>The parent's logical name, which is also the lookup column's name on <c>sprk_storedfile</c>.</summary>
    public string Table => StoredFileSchema.TableOf(Kind);

    /// <summary>The parent's entity SET name (read live, never pluralized).</summary>
    public string EntitySet => StoredFileSchema.EntitySetOf(Kind);
}

/// <summary>Names and fixed values of <c>sprk_storedfile</c> and its neighbours.</summary>
public static class StoredFileSchema
{
    public const string Entity = "sprk_storedfile";

    /// <summary>Entity set, read live (describe tables/sprk_storedfile, dev 2026-10-22).</summary>
    public const string EntitySet = "sprk_storedfiles";

    public const string IdColumn = "sprk_storedfileid";
    public const string Name = "sprk_name";
    public const string Purpose = "sprk_purpose";
    public const string FirmVisible = "sprk_firmvisible";
    public const string Status = "sprk_filestatus";
    public const string SourceDocument = "sprk_sourcedocument";
    public const string SourceCommunication = "sprk_sourcecommunication";
    public const string SourceAttachmentIdentity = "sprk_sourceattachmentidentity";
    public const string SourceVersion = "sprk_sourceversion";
    public const string SourceSha256 = "sprk_sourcesha256";
    public const string Sha256 = "sprk_sha256";
    public const string ContentType = "sprk_contenttype";
    public const string SizeBytes = "sprk_sizebytes";
    public const string BlobPath = "sprk_blobpath";
    public const string VersionNumber = "versionnumber";
    public const string CreatedOn = "createdon";

    /// <summary>Length limits of the text columns (088).</summary>
    public const int NameMaxLength = 400, ContentTypeMaxLength = 255, AttachmentIdentityMaxLength = 1000, BlobPathMaxLength = 1024;

    public const string ProcedureVersionTable = "sprk_procedureversion";
    public const string InvoiceTable = "sprk_invoice";
    public const string BillingExportTable = "sprk_billingexport";

    /// <summary>Entity set names, read live on dev 2026-10-22 (describe) and pinned by a test. Never derived by pluralizing.</summary>
    public const string ProcedureVersionEntitySet = "sprk_procedureversions";
    public const string InvoiceEntitySet = "sprk_invoices";
    public const string BillingExportEntitySet = "sprk_billingexports";

    /// <summary>The source document's table and set (the copy rule reads it).</summary>
    public const string DocumentTable = "sprk_document";
    public const string DocumentEntitySet = "sprk_documents";
    public const string CommunicationTable = "sprk_communication";

    /// <summary>Every parent lookup column, in a fixed order.</summary>
    public static readonly IReadOnlyList<string> ParentLookups = [ProcedureVersionTable, InvoiceTable, BillingExportTable];

    /// <summary>The columns a read of a row asks for (every column the store's rules look at).</summary>
    public static readonly string[] ReadColumns =
    [
        IdColumn, Purpose, FirmVisible, Status, SourceDocument, SourceCommunication, Sha256, ContentType, SizeBytes, BlobPath,
        ProcedureVersionTable, InvoiceTable, BillingExportTable, VersionNumber, CreatedOn,
    ];

    public static string TableOf(StoredFileParentKind kind) => kind switch
    {
        StoredFileParentKind.ProcedureVersion => ProcedureVersionTable,
        StoredFileParentKind.Invoice => InvoiceTable,
        StoredFileParentKind.BillingExport => BillingExportTable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Stored File parent."),
    };

    public static string EntitySetOf(StoredFileParentKind kind) => kind switch
    {
        StoredFileParentKind.ProcedureVersion => ProcedureVersionEntitySet,
        StoredFileParentKind.Invoice => InvoiceEntitySet,
        StoredFileParentKind.BillingExport => BillingExportEntitySet,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Stored File parent."),
    };

    /// <summary>The parent kind of a table name; exact, case-sensitive (blob names are written in lower case only).</summary>
    public static bool TryKindOfTable(string? table, out StoredFileParentKind kind)
    {
        switch (table)
        {
            case ProcedureVersionTable: kind = StoredFileParentKind.ProcedureVersion; return true;
            case InvoiceTable: kind = StoredFileParentKind.Invoice; return true;
            case BillingExportTable: kind = StoredFileParentKind.BillingExport; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>The container of a purpose (section 4.2).</summary>
    public static string ContainerOf(StoredFilePurpose purpose) => purpose switch
    {
        StoredFilePurpose.ProcedureSource => "procedures",
        StoredFilePurpose.InvoicePdf or StoredFilePurpose.InvoiceSupportingFile => "invoice-files",
        StoredFilePurpose.ExportFile => "billing-exports",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown Stored File purpose."),
    };

    /// <summary>The three containers, each once.</summary>
    public static readonly IReadOnlyList<string> Containers = ["procedures", "invoice-files", "billing-exports"];

    /// <summary>The only parent kind a purpose may hang under.</summary>
    public static StoredFileParentKind ParentKindOf(StoredFilePurpose purpose) => purpose switch
    {
        StoredFilePurpose.ProcedureSource => StoredFileParentKind.ProcedureVersion,
        StoredFilePurpose.InvoicePdf or StoredFilePurpose.InvoiceSupportingFile => StoredFileParentKind.Invoice,
        StoredFilePurpose.ExportFile => StoredFileParentKind.BillingExport,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown Stored File purpose."),
    };

    /// <summary>True when <paramref name="value"/> is a defined purpose.</summary>
    public static bool IsKnownPurpose(int value) => Enum.IsDefined(typeof(StoredFilePurpose), value);
}

/// <summary>
/// Deterministic Stored File ids (section 4.1, 9.1 step 2; uac-r2 F4-4, N6): a retried submission derives the same id, so its
/// re-stage and its re-create meet the row and blob the first attempt left. Each kind has its own key prefix, so the three kinds of
/// one submission never collide. <see cref="New"/> is for a caller's upload, which has no submission to derive from.
/// </summary>
public static class StoredFileIds
{
    /// <summary>The id of the copy of a source document at row version <paramref name="version"/>.</summary>
    /// <remarks>The version enters the key through <see cref="StoredFileSourceVersion.ToCanonical"/>, the SAME string written to
    /// <c>sprk_sourceversion</c> (088 retype, owner 2026-10-22).</remarks>
    public static Guid ForSourceVersion(Guid submissionId, long version) =>
        InvoiceSubmissionPersister.IdOf($"ebilling-storedfile-version-{RequireSubmission(submissionId):D}-{StoredFileSourceVersion.ToCanonical(version)}");

    /// <summary>The id of a flagged mailbox attachment's copy, keyed by its attachment identity.</summary>
    public static Guid ForAttachment(Guid submissionId, string attachmentIdentity)
    {
        if (string.IsNullOrWhiteSpace(attachmentIdentity))
        {
            throw new ArgumentException("An attachment identity is required.", nameof(attachmentIdentity));
        }

        if (attachmentIdentity.Length > StoredFileSchema.AttachmentIdentityMaxLength)
        {
            throw new ArgumentException(
                $"An attachment identity is at most {StoredFileSchema.AttachmentIdentityMaxLength} characters.", nameof(attachmentIdentity));
        }

        return InvoiceSubmissionPersister.IdOf($"ebilling-storedfile-attachment-{RequireSubmission(submissionId):D}-{attachmentIdentity}");
    }

    /// <summary>The id of the file a firm uploaded with a submission (the fixed ordinal 0: one PDF per submission).</summary>
    public static Guid ForUpload(Guid submissionId) =>
        InvoiceSubmissionPersister.IdOf($"ebilling-storedfile-upload-{RequireSubmission(submissionId):D}-0");

    /// <summary>A fresh id for a caller's upload.</summary>
    public static Guid New() => Guid.NewGuid();

    private static Guid RequireSubmission(Guid submissionId) =>
        submissionId == Guid.Empty ? throw new ArgumentException("A submission id is required.", nameof(submissionId)) : submissionId;
}

/// <summary>
/// The ONE canonical string of a source row version (088 retype, owner 2026-10-22): invariant culture, no padding, no separators.
/// <see cref="StoredFileIds.ForSourceVersion"/> and the <c>sprk_sourceversion</c> column both take it from <see cref="ToCanonical"/>,
/// so the stored value and the hashed value are always the same string, whatever the current culture.
/// </summary>
public static class StoredFileSourceVersion
{
    /// <summary>The column's length (088: String, max length 20).</summary>
    public const int MaxLength = 20;

    /// <summary>The canonical string of <paramref name="version"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A negative version (a row version is never negative).</exception>
    public static string ToCanonical(long version)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "A row version is never negative.");
        }

        // long.MaxValue has 19 digits, so the string always fits; the check keeps the column's contract explicit.
        var text = version.ToString(CultureInfo.InvariantCulture);
        return text.Length <= MaxLength
            ? text
            : throw new ArgumentOutOfRangeException(nameof(version), version, $"A source version is at most {MaxLength} characters.");
    }

    /// <summary>
    /// Reads a row version as Dataverse reports it (<c>Entity.RowVersion</c>, digits only) into its number. Refuses a value that is
    /// empty, signed, has separators, white space or other characters, is longer than <see cref="MaxLength"/>, or does not fit a
    /// <see cref="long"/>; the canonical string is then <see cref="ToCanonical"/> of the result (leading zeros are dropped).
    /// </summary>
    public static bool TryParseRowVersion(string? rowVersion, out long version)
    {
        version = 0;
        return !string.IsNullOrEmpty(rowVersion)
               && rowVersion.Length <= MaxLength
               && long.TryParse(rowVersion, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }
}

/// <summary>
/// The checked conversion of a file's length to <c>sprk_sizebytes</c> (088 retype: Integer 0 to 2,147,483,647; owner 2026-10-22).
/// A length outside that range is REFUSED, never cast or clamped: a file over 2 GiB cannot be stored (known limit).
/// </summary>
public static class StoredFileSize
{
    /// <summary>Largest storable length in bytes.</summary>
    public const long MaxBytes = int.MaxValue;

    /// <summary>True and the column value when <paramref name="length"/> fits; false otherwise (no value to write).</summary>
    public static bool TryToColumnValue(long length, out int value)
    {
        if (length < 0 || length > MaxBytes)
        {
            value = 0;
            return false;
        }

        value = checked((int)length);
        return true;
    }
}

/// <summary>
/// The blob-name rule (section 4.2): <c>{tenantId}/{parent table}/{parent id}/{file id}</c>, every id a lower-case "D" GUID, the
/// table one of the three parent tables. Built only from typed values, and every name read back (a row's <c>sprk_blobpath</c>, a
/// listing) is parsed strictly before any storage call: exactly four segments, no empty segment, no separator or <c>..</c> inside a
/// segment, canonical GUIDs, a known table, and the configured tenant.
/// </summary>
public static class StoredFileBlobName
{
    /// <summary>The name of <paramref name="fileId"/> under <paramref name="parent"/> in <paramref name="tenantId"/>.</summary>
    public static string Build(Guid tenantId, StoredFileParent parent, Guid fileId)
    {
        if (tenantId == Guid.Empty || parent.Id == Guid.Empty || fileId == Guid.Empty)
        {
            throw new ArgumentException("A blob name needs a tenant, a parent id and a file id.");
        }

        return $"{tenantId:D}/{parent.Table}/{parent.Id:D}/{fileId:D}";
    }

    /// <summary>Parses <paramref name="name"/> strictly; false for anything the rule does not produce.</summary>
    public static bool TryParse(string? name, out Guid tenantId, out StoredFileParent parent, out Guid fileId)
    {
        tenantId = Guid.Empty;
        parent = default;
        fileId = Guid.Empty;
        if (string.IsNullOrEmpty(name) || name.Length > StoredFileSchema.BlobPathMaxLength)
        {
            return false;
        }

        var segments = name.Split('/');
        if (segments.Length != 4
            || !TryCanonicalGuid(segments[0], out var tenant)
            || !StoredFileSchema.TryKindOfTable(segments[1], out var kind)
            || !TryCanonicalGuid(segments[2], out var parentId)
            || !TryCanonicalGuid(segments[3], out var file))
        {
            return false;
        }

        tenantId = tenant;
        parent = new StoredFileParent(kind, parentId);
        fileId = file;
        return true;
    }

    /// <summary>
    /// Parses <paramref name="name"/> and checks it belongs to <paramref name="expectedTenant"/>; false for a malformed name or
    /// another tenant's.
    /// </summary>
    public static bool TryParseForTenant(string? name, Guid expectedTenant, out StoredFileParent parent, out Guid fileId) =>
        TryParse(name, out var tenant, out parent, out fileId) && tenant == expectedTenant && expectedTenant != Guid.Empty;

    private static bool TryCanonicalGuid(string segment, out Guid value) =>
        Guid.TryParseExact(segment, "D", out value)
        && value != Guid.Empty
        && string.Equals(value.ToString("D"), segment, StringComparison.Ordinal);
}

/// <summary>Stable refusal codes of the file store (returned, never thrown; logged with ids only).</summary>
/// <summary>
/// Dataverse "record does not exist" (0x80040217) at ANY depth of the exception chain: the platform client wraps some faults
/// (<c>DataverseServiceClientImpl.DeleteAsync</c> throws <see cref="InvalidOperationException"/> around the fault), and
/// <c>RecordContainerResolver.IsRecordNotFound</c> reads only the exception it is given. Same rule as the submission job handler's.
/// </summary>
internal static class StoredFileNotFound
{
    internal static bool Is(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }
}

public static class StoredFileRefusal
{
    /// <summary>No <c>Finance:FileStore:BlobEndpoint</c> is configured.</summary>
    public const string NotConfigured = "stored_file.store_not_configured";

    /// <summary>The length is negative or above <see cref="StoredFileSize.MaxBytes"/>; nothing was written.</summary>
    public const string SizeOutOfRange = "stored_file.size_out_of_range";

    /// <summary>A blob with different bytes already exists under the id; it was left unchanged.</summary>
    public const string ContentConflict = "stored_file.content_conflict";

    /// <summary>The purpose does not belong under the parent, or the request names an invalid combination (e.g. a firm-visible copy).</summary>
    public const string InvalidRequest = "stored_file.invalid_request";

    /// <summary>A step-0 refusal: the caller is flagged external, has no systemuser row, or the flag could not be read.</summary>
    public const string CallerNotInternal = "stored_file.caller_not_internal";

    /// <summary>The caller lacks Write on the parent (or the answer could not be had).</summary>
    public const string NoWriteOnParent = "stored_file.no_write_on_parent";

    /// <summary>No owner could be resolved for the row.</summary>
    public const string OwnerUnresolved = "stored_file.owner_unresolved";

    /// <summary>The blob is missing at the flip.</summary>
    public const string BlobMissing = "stored_file.blob_missing";

    /// <summary>The blob's SHA-256 differs from the row's at the flip.</summary>
    public const string HashMismatch = "stored_file.hash_mismatch";

    /// <summary>The row is not in a state the operation accepts (no row, Removed, a path that is not its own).</summary>
    public const string RowNotFlippable = "stored_file.row_not_flippable";

    /// <summary>The row changed since it was read; nothing was written.</summary>
    public const string Concurrency = "stored_file.concurrency";
}

/// <summary>What a stored blob's malware scan says, as the download rule reads it (section 4.2; uac-r2 condition 3).</summary>
public enum StoredFileScanState
{
    /// <summary>Tagged "No threats found" with a scan time no earlier than the blob's Last-Modified.</summary>
    Clean,

    /// <summary>Scanning is explicitly off for this store (<c>MalwareScanningEnabled = false</c>); served.</summary>
    NotScanned,

    /// <summary>No usable verdict yet: tag absent, unreadable, another value, conflicting, or older than the content.</summary>
    Pending,

    /// <summary>Tagged "Malicious"; never served.</summary>
    Malicious,
}

/// <summary>The spec of a Stored File row (everything except the blob facts, which come from the stage).</summary>
public sealed record StoredFileRowSpec
{
    /// <summary>The row id, which is also the blob's file id.</summary>
    public required Guid FileId { get; init; }

    public required StoredFilePurpose Purpose { get; init; }

    public required StoredFileParent Parent { get; init; }

    /// <summary>The display name (<c>sprk_name</c>, at most 400 characters; a longer value is cut at that length).</summary>
    public required string Name { get; init; }

    /// <summary>The content type served with the bytes.</summary>
    public required string ContentType { get; init; }

    /// <summary>Firm-visible: only an Invoice PDF the firm uploaded through the SPA, never a copy with a source (section 4.1).</summary>
    public bool FirmVisible { get; init; }

    public Guid? SourceDocumentId { get; init; }

    public Guid? SourceCommunicationId { get; init; }

    public string? SourceAttachmentIdentity { get; init; }

    /// <summary>The source row version (written through <see cref="StoredFileSourceVersion.ToCanonical"/>).</summary>
    public long? SourceVersion { get; init; }

    public string? SourceSha256 { get; init; }

    /// <summary>The submission that staged the file (blob metadata only).</summary>
    public Guid? SubmissionId { get; init; }

    /// <summary>Null when the spec is valid; otherwise why not.</summary>
    public string? Invalid()
    {
        if (FileId == Guid.Empty || Parent.Id == Guid.Empty)
            return "file id and parent id are required";
        if (!Enum.IsDefined(Purpose))
            return "unknown purpose";
        if (StoredFileSchema.ParentKindOf(Purpose) != Parent.Kind)
            return "the purpose does not belong under this parent";
        if (string.IsNullOrWhiteSpace(Name))
            return "a name is required";
        if (string.IsNullOrWhiteSpace(ContentType) || ContentType.Length > StoredFileSchema.ContentTypeMaxLength)
            return "a content type of at most 255 characters is required";
        var hasSource = SourceDocumentId is not null || SourceCommunicationId is not null || SourceAttachmentIdentity is not null;
        if (FirmVisible && (Purpose != StoredFilePurpose.InvoicePdf || hasSource))
            return "only a firm-uploaded Invoice PDF with no source may be firm-visible";
        if (SourceAttachmentIdentity is { Length: > StoredFileSchema.AttachmentIdentityMaxLength })
            return "the attachment identity is too long";
        if (SourceVersion is < 0)
            return "a source version is never negative";
        if (SourceSha256 is not null && !IsSha256Hex(SourceSha256))
            return "the source SHA-256 must be 64 lower-case hex characters";
        return null;
    }

    internal static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>The facts of a staged blob.</summary>
/// <param name="Container">The purpose's container.</param>
/// <param name="BlobPath">The blob name (also <c>sprk_blobpath</c>).</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the bytes.</param>
/// <param name="SizeBytes">The length in bytes (already checked to fit the column).</param>
/// <param name="Reused">True when the same bytes were already stored under the id (a retry).</param>
public sealed record StagedBlob(string Container, string BlobPath, string Sha256, long SizeBytes, bool Reused);

/// <summary>The answer of a stage: the blob, or a refusal code (nothing written).</summary>
public sealed record StageResult(StagedBlob? Blob, string? RefusalCode)
{
    public bool IsStaged => Blob is not null;

    public static StageResult Staged(StagedBlob blob) => new(blob, null);

    public static StageResult Refused(string code) => new(null, code);
}

/// <summary>The answer of <c>FlipToServableAsync</c>.</summary>
public enum StoredFileFlipOutcome
{
    /// <summary>Updated to Servable.</summary>
    Flipped,

    /// <summary>Already Servable (a retry); nothing written.</summary>
    AlreadyServable,

    /// <summary>A definitive failure (blob missing, hash mismatch, row not flippable); nothing written.</summary>
    Definitive,

    /// <summary>The row changed since it was read; nothing written.</summary>
    Concurrency,
}

/// <summary>The answer of a flip with its refusal code on <see cref="StoredFileFlipOutcome.Definitive"/>.</summary>
public sealed record StoredFileFlipResult(StoredFileFlipOutcome Outcome, string? RefusalCode = null);

/// <summary>What <see cref="StoredFileService.DeleteForUndoAsync"/> did (ebilling-r1 task 094, the submission undo).</summary>
public enum StoredFileCompensation
{
    /// <summary>The blob (when its path was the row's own) and then the row were deleted.</summary>
    Deleted,

    /// <summary>No row with that id: nothing to delete (a row is deleted after its blob, so its blob is gone too).</summary>
    AlreadyGone,
}

/// <summary>The answer of a create (caller upload or app create): the Servable file, or a refusal.</summary>
public sealed record StoredFileCreateResult(Guid? FileId, string? RefusalCode)
{
    public bool IsCreated => FileId is not null;

    public static StoredFileCreateResult Created(Guid fileId) => new(fileId, null);

    public static StoredFileCreateResult Refused(string code) => new(null, code);
}

/// <summary>What <c>OpenForProcessingAsync</c> found.</summary>
public enum StoredFileProcessingOutcome
{
    /// <summary>The bytes, scan Clean (or NotScanned when scanning is off).</summary>
    Ok,

    /// <summary>No usable scan verdict yet: retry later.</summary>
    Pending,

    /// <summary>The scan says Malicious: never processed.</summary>
    Malicious,

    /// <summary>No row, a Removed row, a path that is not the row's, or no blob.</summary>
    Missing,
}

/// <summary>A job's read of a Stored File's bytes.</summary>
public sealed record StoredFileProcessingRead(StoredFileProcessingOutcome Outcome, BinaryData? Content = null, string? ContentType = null);
