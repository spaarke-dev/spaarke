using System.Security.Claims;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Read model for <c>GET /api/communications/{id}/attachments/text</c>
/// (email-communication-intelligence-r2, reconciliation reader parity Batch 2 / B2.1).
///
/// <para>
/// The reconciliation browse reader folds each file attachment's extracted text into the same normalized
/// surface as the email body (NFR-11 — one reader). That text is a transient pipeline artifact never persisted,
/// so this service RE-EXTRACTS on demand: it lists the communication's <c>sprk_communicationattachment</c> rows,
/// resolves each linked <c>sprk_document</c>'s SPE pointer, downloads the binary, and extracts readable text.
/// </para>
///
/// <para>
/// <b>Access model (NFR-06 no-leak — security-sensitive).</b> BOTH Dataverse reads are IMPERSONATED
/// (<c>MSCRMCallerID</c> = the caller's <c>systemuserid</c>) via <see cref="IImpersonatedCommunicationQuery"/>,
/// exactly like <c>CommunicationThreadReadService</c> — Dataverse applies row-level security natively, so a
/// caller who cannot see the communication (or a given document) simply gets zero rows: no filename / document-id
/// / count is disclosed cross-user. Fail-closed: an unresolvable caller is refused (403), never an app-only Dataverse
/// fallback that would widen access. This deliberately does NOT use the app-only <c>IGenericEntityService</c> path
/// (code-review 2026-08-14).
/// </para>
///
/// <para>
/// <b>The SPE bytes are read APP-ONLY</b> (unified-access-control-r2 task 171, owner round 69 — broker-only), after
/// the impersonated Dataverse read decided which documents the caller may see AND the document-pointer check
/// (<c>RecordContainerResolver.IsDocumentPointerContainerAllowedAsync</c>) verified each row's pointer. They used to
/// be read OBO, which SharePoint Embedded answers only for a caller holding a role on the container: an attachment
/// filed on a secure record (no members by design) was always "not available as text".
/// </para>
///
/// <para>
/// <b>Reuse (§11, ADR-007/ADR-009).</b> The download + extraction + Redis caching are the SAME shared primitives
/// the analysis pipeline uses — <see cref="ISpeFileOperations.DownloadFileAsync"/> and the cache-aware
/// <see cref="ITextExtractor.ExtractAsync(System.IO.Stream, string, string?, string?, string?, System.Threading.CancellationToken)"/>
/// overload (its own 24h Redis cache). The impersonated query seam is REUSED from the messaging read path. This
/// service adds only the attachment→document join + the rich per-attachment result mapping the reader needs (the
/// analysis loader collapses failures into a lossy placeholder string, which cannot signal "not available as text").
/// </para>
///
/// <para>
/// Non-fatal per attachment (NFR-04): a single attachment that fails to download/extract, or whose document the
/// caller cannot see, is reported as <c>Extractable=false</c> (the reader shows its "Content not available as
/// text" note) and never fails the whole response. Cost-capped at <see cref="MaxAttachments"/>.
/// </para>
/// </summary>
public sealed class CommunicationAttachmentTextService
{
    private readonly IImpersonatedCommunicationQuery _query;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly CommunicationThreadReadService _threadReadService;
    private readonly ISpeFileOperations _speFileStore;
    private readonly Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver _containerResolver;
    private readonly ITextExtractor _textExtractor;
    private readonly ILogger<CommunicationAttachmentTextService> _logger;

    /// <summary>Cost cap on attachments extracted per request (NFR-08). A single email rarely exceeds this.</summary>
    private const int MaxAttachments = 25;

    // Web API entity sets + fields for the impersonated OData reads.
    private const string AttachmentSet = "sprk_communicationattachments";
    private const string DocumentSet = "sprk_documents";
    private const string AttachmentPkField = "sprk_communicationattachmentid";
    private const string AttachmentNameField = "sprk_name";
    private const string AttachmentDocumentValue = "_sprk_document_value";
    private const string AttachmentCommunicationValue = "_sprk_communication_value";
    private const string DocumentPkField = "sprk_documentid";
    private const string DriveIdField = "sprk_graphdriveid";
    private const string ItemIdField = "sprk_graphitemid";
    private const string DocumentFileNameField = "sprk_filename";

    public CommunicationAttachmentTextService(
        IImpersonatedCommunicationQuery query,
        ICallerSystemUserResolver callerResolver,
        CommunicationThreadReadService threadReadService,
        ISpeFileOperations speFileStore,
        Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
        ITextExtractor textExtractor,
        ILogger<CommunicationAttachmentTextService> logger)
    {
        _query = query;
        _callerResolver = callerResolver;
        _threadReadService = threadReadService ?? throw new ArgumentNullException(nameof(threadReadService));
        _speFileStore = speFileStore;
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _textExtractor = textExtractor;
        _logger = logger;
    }

    /// <summary>
    /// Resolve every file attachment of <paramref name="communicationId"/> that <paramref name="caller"/> may see
    /// to its re-extracted, normalized text. Returns an empty list when the caller can see no document-backed
    /// attachments. Throws 403 when the caller cannot be resolved to a Dataverse user (fail-closed).
    /// </summary>
    public async Task<CommunicationAttachmentTextResponse> GetAttachmentTextAsync(
        Guid communicationId,
        ClaimsPrincipal? caller,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, cancellationToken).ConfigureAwait(false);

        // 0) Task 065 fix round 6 (F1): the communication itself must be visible to this caller before any of its
        //    attachments are listed. ReadVisibleCommunicationAsync is the one visibility decision (the shared flag rule,
        //    and for an external caller D7: an internal-only email, or one whose thread is not explicitly Open, is
        //    invisible). A row the caller may not see answers exactly like one with no attachments, so nothing leaks.
        if (await _threadReadService.ReadVisibleCommunicationAsync(communicationId, caller, null, cancellationToken).ConfigureAwait(false) is null)
            return Empty();

        // 1) IMPERSONATED attachment read — Dataverse returns only attachments this caller may see (NFR-06).
        var attachments = await QueryAttachmentsAsync(communicationId, callerSystemUserId, cancellationToken).ConfigureAwait(false);
        if (attachments.Count == 0)
            return Empty();

        // 2) IMPERSONATED document read for the referenced docs — only docs the caller may see resolve to an SPE
        //    pointer, so an attachment whose document is invisible to the caller degrades to not-extractable.
        var pointers = await QueryDocumentPointersAsync(attachments, callerSystemUserId, cancellationToken).ConfigureAwait(false);

        // 2') uac-r2 task 188 round 4b (F1-4): THE external document narrowing over the documents the caller's own read
        //     returned. For a caller who is not a resolved internal systemuser, a document it hides (own Restricted, a
        //     Restricted ancestor, an internal-only communication) is dropped, so its attachment reads as
        //     Extractable=false exactly like one whose document the caller cannot see; a narrowing fault drops them all
        //     (fail closed). An internal systemuser: no narrowing call and no app-only read.
        pointers = await NarrowForCallerAsync(pointers, httpContext).ConfigureAwait(false);

        // 3) Download + extract per attachment (app-only after the pointer check; the extractor's own Redis cache makes
        //    repeats cheap).
        var items = new List<CommunicationAttachmentTextItem>(attachments.Count);
        foreach (var att in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(await ExtractOneAsync(att, pointers, httpContext, cancellationToken).ConfigureAwait(false));
        }
        return new CommunicationAttachmentTextResponse { Attachments = items };
    }

    private static CommunicationAttachmentTextResponse Empty() =>
        new() { Attachments = Array.Empty<CommunicationAttachmentTextItem>() };

    /// <summary>Resolve the caller's Dataverse <c>systemuserid</c> or refuse the read (403) — fail-closed, no app-only fallback.</summary>
    private async Task<Guid> ResolveCallerOrThrowAsync(ClaimsPrincipal? caller, CancellationToken ct)
    {
        var resolution = await _callerResolver.ResolveAsync(caller, ct).ConfigureAwait(false);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var systemUserId)
            || systemUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "[ATTACHMENT-TEXT] caller has no resolvable Dataverse systemuserid ({Reason}) — refusing the read (fail closed).",
                resolution.UnresolvedReason ?? "unresolved");
            throw new SdapProblemException(
                code: "ATTACHMENT_TEXT_FORBIDDEN",
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user, so attachment text cannot be read.",
                statusCode: 403);
        }
        return systemUserId;
    }

    /// <summary>
    /// Impersonated read of the communication's attachments (only those the caller may see). Non-fatal (NFR-04):
    /// any query failure ⇒ empty.
    /// </summary>
    private async Task<IReadOnlyList<AttachmentRow>> QueryAttachmentsAsync(
        Guid communicationId, Guid callerSystemUserId, CancellationToken ct)
    {
        try
        {
            var select = $"{AttachmentPkField},{AttachmentDocumentValue},{AttachmentNameField}";
            var odata = $"$select={select}&$filter={AttachmentCommunicationValue} eq {communicationId}&$top={MaxAttachments}";
            var rows = await _query.QueryAsync(AttachmentSet, odata, callerSystemUserId, ct).ConfigureAwait(false);

            var result = new List<AttachmentRow>(rows.Count);
            foreach (var row in rows)
            {
                var attachmentId = TryGuid(row, AttachmentPkField);
                if (attachmentId is null)
                    continue;
                result.Add(new AttachmentRow(
                    AttachmentId: attachmentId.Value,
                    DocumentId: TryGuid(row, AttachmentDocumentValue),
                    FileName: TryString(row, AttachmentNameField) ?? "Attachment"));
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Impersonated attachment read failed for communication {CommunicationId}; returning no attachments", communicationId);
            return Array.Empty<AttachmentRow>();
        }
    }

    /// <summary>
    /// Impersonated read of the referenced documents' SPE pointers (only those the caller may see), keyed by
    /// document id. Non-fatal (NFR-04): any query failure ⇒ empty map (all attachments degrade to not-extractable).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, DocPointer>> QueryDocumentPointersAsync(
        IReadOnlyList<AttachmentRow> attachments, Guid callerSystemUserId, CancellationToken ct)
    {
        var docIds = attachments
            .Where(a => a.DocumentId is not null)
            .Select(a => a.DocumentId!.Value)
            .Distinct()
            .ToList();
        if (docIds.Count == 0)
            return EmptyPointers;

        try
        {
            var select = $"{DocumentPkField},{DriveIdField},{ItemIdField},{DocumentFileNameField}";
            var orClause = string.Join(" or ", docIds.Select(id => $"{DocumentPkField} eq {id}"));
            var odata = $"$select={select}&$filter={orClause}";
            var rows = await _query.QueryAsync(DocumentSet, odata, callerSystemUserId, ct).ConfigureAwait(false);

            var map = new Dictionary<Guid, DocPointer>(rows.Count);
            foreach (var row in rows)
            {
                var docId = TryGuid(row, DocumentPkField);
                if (docId is null)
                    continue;
                map[docId.Value] = new DocPointer(
                    DriveId: TryString(row, DriveIdField),
                    ItemId: TryString(row, ItemIdField),
                    FileName: TryString(row, DocumentFileNameField));
            }
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Impersonated document read failed while resolving {Count} attachment document(s); degrading to not-extractable", docIds.Count);
            return EmptyPointers;
        }
    }

    /// <summary>
    /// Task 188 round 4b (F1-4): the pointers whose documents THE external document narrowing shows the caller
    /// (<see cref="Sprk.Bff.Api.Api.Filters.InternalPlaneDocumentNarrowing.ForCallerAsync"/>: one call; an internal
    /// systemuser is not narrowed and nothing is read; a fault shows none). Logs counts only.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, DocPointer>> NarrowForCallerAsync(
        IReadOnlyDictionary<Guid, DocPointer> pointers, HttpContext httpContext)
    {
        if (pointers.Count == 0)
            return pointers;

        var visibility = await Sprk.Bff.Api.Api.Filters.InternalPlaneDocumentNarrowing.ForCallerAsync(
            httpContext, pointers.Keys.Select(id => (string?)id.ToString()), "communication-attachment-text", _logger)
            .ConfigureAwait(false);
        if (!visibility.Narrowed)
            return pointers;

        var shown = pointers.Where(p => visibility.IsShown(p.Key.ToString())).ToDictionary(p => p.Key, p => p.Value);
        if (shown.Count != pointers.Count)
        {
            _logger.LogInformation(
                "[ATTACHMENT-TEXT] {Hidden} of {Count} attachment document(s) withheld by the external document narrowing",
                pointers.Count - shown.Count, pointers.Count);
        }

        return shown;
    }

    /// <summary>
    /// Download + extract ONE attachment's text (OBO download so SPE enforces the caller's access; the extractor's
    /// own Redis cache makes repeats cheap). Every non-text outcome — invisible/missing document, missing SPE
    /// reference, unsupported type, image/vision-required, download miss, extraction failure, exception — maps to
    /// the same non-fatal <c>Extractable=false</c> result the reader renders as "not available as text".
    /// </summary>
    private async Task<CommunicationAttachmentTextItem> ExtractOneAsync(
        AttachmentRow att,
        IReadOnlyDictionary<Guid, DocPointer> pointers,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DocPointer? pointer = att.DocumentId is { } docId && pointers.TryGetValue(docId, out var p) ? p : null;
        // Prefer the document's own filename (carries the true extension); else the attachment name.
        var fileName = !string.IsNullOrEmpty(pointer?.FileName) ? pointer!.FileName! : att.FileName;

        var unavailable = new CommunicationAttachmentTextItem
        {
            AttachmentId = att.AttachmentId,
            DocumentId = att.DocumentId,
            FileName = fileName,
            Extractable = false,
        };

        if (pointer is null || string.IsNullOrEmpty(pointer.DriveId) || string.IsNullOrEmpty(pointer.ItemId))
            return unavailable;

        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
        if (!_textExtractor.IsSupported(extension))
            return unavailable;

        try
        {
            // Task 171: the bytes are read AS THE APPLICATION, so the row's pointer must name a container this document
            // may use (the check SPE's own ACL made under OBO). Unverifiable → not available as text, never the bytes.
            if (att.DocumentId is not { } pointerDocumentId
                || !await _containerResolver.IsDocumentPointerContainerAllowedAsync(
                    pointerDocumentId, pointer.DriveId, pointer.ItemId, cancellationToken).ConfigureAwait(false))
            {
                return unavailable;
            }

            // ETag for the extractor's cache key (best-effort; extraction still runs without it).
            string? etag = null;
            try
            {
                var metadata = await _speFileStore
                    .GetFileMetadataAsync(pointer.DriveId!, pointer.ItemId!, cancellationToken)
                    .ConfigureAwait(false);
                etag = metadata?.ETag;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "ETag lookup failed for attachment {AttachmentId} (proceeding uncached)", att.AttachmentId);
            }

            using var stream = await _speFileStore
                .DownloadFileAsync(pointer.DriveId!, pointer.ItemId!, cancellationToken)
                .ConfigureAwait(false);
            if (stream is null)
                return unavailable;

            var extraction = await _textExtractor
                .ExtractAsync(stream, fileName, pointer.DriveId, pointer.ItemId, etag, cancellationToken)
                .ConfigureAwait(false);

            if (!extraction.Success || string.IsNullOrEmpty(extraction.Text))
                return unavailable;

            return new CommunicationAttachmentTextItem
            {
                AttachmentId = att.AttachmentId,
                DocumentId = att.DocumentId,
                FileName = fileName,
                Text = extraction.Text,
                Extractable = true,
                Method = extraction.Method.ToString(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Attachment-text extraction failed for attachment {AttachmentId} (document {DocumentId})",
                att.AttachmentId, att.DocumentId);
            return unavailable;
        }
    }

    private static readonly IReadOnlyDictionary<Guid, DocPointer> EmptyPointers = new Dictionary<Guid, DocPointer>();

    private static string? TryString(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Guid? TryGuid(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;

    /// <summary>Projected attachment row: the junction id + its document lookup + the attachment's own name.</summary>
    private sealed record AttachmentRow(Guid AttachmentId, Guid? DocumentId, string FileName);

    /// <summary>A visible document's SPE pointer (drive/item) + its filename.</summary>
    private sealed record DocPointer(string? DriveId, string? ItemId, string? FileName);
}
