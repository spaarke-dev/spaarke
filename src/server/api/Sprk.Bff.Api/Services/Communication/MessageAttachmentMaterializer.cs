using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Materializes a messaging (ACS Chat) file attachment into Spaarke's channel-agnostic storage model
/// (design §6.4, FR-14):
/// <c>ACS/file → SPE (SpeFileStore) → sprk_document → sprk_document.sprk_communication lookup +
/// sprk_communicationattachment intersection</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the messaging channel's own materialization STEP — the net-new surface of task 070. The
/// storage SCHEMA is unchanged: it reuses the exact <c>sprk_document</c> + <c>sprk_communicationattachment</c>
/// shape the email inbound path already writes (see
/// <see cref="IncomingCommunicationProcessor"/>.<c>ProcessIncomingAttachmentsAsync</c>), and it uploads
/// through the <b>same</b> <c>SpeFileStore</c> facade (ADR-007) — no second storage path is introduced.
/// Email's materialization is email-specific and is NOT behind the archive seam, so a new channel writes
/// its own step feeding the SAME shape rather than trying to share the Graph-typed email code.
/// </para>
/// <para>
/// <b>Reference, not binary (FR-14):</b> the binary lives in SPE (the store). The materializer returns a
/// <see cref="MessageAttachmentReference"/> — the ACS message carries only this reference (rendered later
/// as a file card in the timeline, task 060). No <c>Azure.Communication.*</c> type is referenced here and
/// no binary is ever written back onto an ACS message (NFR-04: server-side only).
/// </para>
/// <para>
/// <b>Policy enforcement (CHAT-ATTACHMENT-POLICY.md — binding):</b> the policy gate runs FIRST, before any
/// SPE upload or Dataverse write. Oversize (&gt; 25 MB binary) and disallowed MIME types are rejected with
/// RFC 7807 <see cref="ProblemDetails"/> (ADR-019); no <c>sprk_document</c> is created and nothing is
/// uploaded. The caps/allow-list are the policy's — not re-invented here.
/// </para>
/// <para>
/// <b>DI:</b> registered Scoped in <c>CommunicationModule</c> to match the Scoped <see cref="ISpeFileOperations"/>
/// facade lifetime (the Singleton <see cref="IGenericEntityService"/> composes safely into a Scoped consumer);
/// registration is unconditional (ADR-032 — no feature gate).
/// </para>
/// </remarks>
public sealed class MessageAttachmentMaterializer
{
    // ===== CHAT-ATTACHMENT-POLICY.md (binding; 2026-05-26) — single source of truth =====
    // Binary cap: 25 MB per file. The chat policy states the binary cap is enforced client-side (the chat
    // SERVER gate operates on extracted-text envelopes); THIS materializer is the binary-into-SPE analog the
    // policy calls out for attachment ingestion — so it enforces the SAME 25 MB cap + the SAME 4-type MIME
    // allow-list the chat server gate (ChatEndpoints.AllowedAttachmentContentTypes) accepts. Reused — NOT
    // re-invented; no parallel policy surface.
    internal const long MaxAttachmentSizeBytes = 25L * 1024 * 1024;

    internal static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text/plain",
            "text/markdown",
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // DOCX
        };

    // sprk_communicationattachment.sprk_attachmenttype = File — pre-existing intersection schema
    // (same value the email inbound path writes). No new option-set member is introduced.
    private const int AttachmentTypeFile = 100000000;

    private readonly ISpeFileOperations _speFileStore;
    private readonly IGenericEntityService _genericEntityService;
    private readonly CommunicationOptions _options;
    private readonly ILogger<MessageAttachmentMaterializer> _logger;

    /// <summary>
    /// The record-aware container decision (unified-access-control-r2 task 076). Injected directly —
    /// both this type and the resolver are registered Scoped (<c>CommunicationModule</c>), so no
    /// scope-factory dance is needed here (unlike <c>CommunicationService</c>, which is not Scoped).
    ///
    /// <para>OPTIONAL so existing test constructions stay compilable, but the registration is NOT
    /// feature-gated. A null resolver means the container decision cannot be made, and
    /// <see cref="MaterializeAsync"/> therefore REFUSES rather than falling back to the shared archive
    /// container — the CLAUDE.md §10 F.1 asymmetric-registration trap is that an absent isolation seam
    /// silently becomes "use the shared container", which is the exact defect this routing removes.</para>
    /// </summary>
    private readonly Engine.CommunicationContainerResolver? _containerResolver;

    public MessageAttachmentMaterializer(
        ISpeFileOperations speFileStore,
        IGenericEntityService genericEntityService,
        IOptions<CommunicationOptions> options,
        ILogger<MessageAttachmentMaterializer> logger,
        Engine.CommunicationContainerResolver? containerResolver = null)
    {
        _speFileStore = speFileStore;
        _genericEntityService = genericEntityService;
        _options = options.Value;
        _logger = logger;
        _containerResolver = containerResolver;
    }

    /// <summary>
    /// Materializes one chat attachment. Enforces <c>CHAT-ATTACHMENT-POLICY.md</c> FIRST; on pass, uploads the
    /// binary to SPE and creates the governed <c>sprk_document</c> + <c>sprk_communicationattachment</c> links,
    /// returning the reference the ACS message carries. On a policy violation returns a rejected result carrying
    /// RFC 7807 <see cref="ProblemDetails"/> (ADR-019) — nothing is uploaded and no record is created.
    /// </summary>
    public async Task<MaterializeAttachmentResult> MaterializeAsync(
        MaterializeAttachmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var effectiveSize = EffectiveSize(request);

        // ── POLICY GATE FIRST — before any SPE upload or Dataverse write (load-bearing correctness) ──
        var policyProblem = EvaluatePolicy(request, effectiveSize);
        if (policyProblem is not null)
        {
            _logger.LogWarning(
                "Rejected message attachment '{FileName}' ({ContentType}, {SizeBytes} bytes) for communication " +
                "{CommunicationId} | ErrorCode: {ErrorCode} | CorrelationId: {CorrelationId}",
                request.FileName, request.ContentType, effectiveSize, request.CommunicationId,
                policyProblem.Extensions["errorCode"], request.CorrelationId);
            return MaterializeAttachmentResult.Rejected(policyProblem);
        }

        // ── Resolve the SPE drive/container — RECORD-AWARE as of task 076 ───────────────────────
        //
        // This used to be `request.DriveId ?? _options.ArchiveContainerId`, so a chat attachment on a
        // message regarding a SECURE matter landed in the shared archive container. SPE permissions
        // are additive-only, so that placement cannot be retracted by any later permission change.
        // Task 075 fixed the email inbound-attachment twin; this is the messaging-channel one.
        //
        // ⚠️ NOTE THE ORDER. `request.DriveId` is now the FALLBACK, not the winner. If the resolver
        // ran only when `request.DriveId` was absent, the isolation fix would be caller-bypassable —
        // any caller supplying a drive id would silently reinstate the defect. A secure regarding's
        // own container must beat every caller-supplied value; a NON-SECURE message keeps exactly its
        // previous behaviour, because `request.DriveId ?? ArchiveContainerId` is what gets passed in
        // as INV-7's tier-3 default.
        if (_containerResolver is null)
        {
            // Refuse rather than fall back. An absent isolation seam that degrades to "use the shared
            // container" is the CLAUDE.md §10 F.1 anti-pattern in its most damaging form.
            _logger.LogError(
                "Cannot materialize attachment '{FileName}' for communication {CommunicationId}: the " +
                "record-aware container resolver is unavailable, so it cannot be determined whether the " +
                "message regards a secure record. Refusing rather than using the shared archive container. " +
                "CorrelationId: {CorrelationId}",
                request.FileName, request.CommunicationId, request.CorrelationId);

            return MaterializeAttachmentResult.Rejected(Problem(
                status: 500,
                errorCode: "ATTACHMENT_STORE_NOT_CONFIGURED",
                title: "Attachment store not configured",
                detail: "The storage container for this message could not be determined, so the "
                        + "attachment was not stored.",
                correlationId: request.CorrelationId));
        }

        var fallbackDriveId = !string.IsNullOrWhiteSpace(request.DriveId)
            ? request.DriveId!
            : _options.ArchiveContainerId;

        // Throws SdapProblemException for a secure regarding with no container of its own
        // (secure_record_container_missing, 409) or ambiguous ownership — fail closed by design, and
        // deliberately NOT caught here: swallowing it into a "not configured" rejection would turn a
        // correct refusal into what reads like a deployment problem.
        var driveId = await _containerResolver
            .ResolveContainerAsync(request.CommunicationId, fallbackDriveId, cancellationToken);

        if (string.IsNullOrWhiteSpace(driveId))
        {
            return MaterializeAttachmentResult.Rejected(Problem(
                status: 500,
                errorCode: "ATTACHMENT_STORE_NOT_CONFIGURED",
                title: "Attachment store not configured",
                detail: "No SPE drive is configured for message attachments (Communication:ArchiveContainerId).",
                correlationId: request.CorrelationId));
        }

        // ── Upload the binary to SPE via the SpeFileStore facade (ADR-007 — the sole SPE access path) ──
        if (request.Content.CanSeek)
            request.Content.Position = 0;

        var spePath = $"/communications/{request.CommunicationId:N}/attachments/{request.FileName}";
        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, spePath, request.Content, cancellationToken);
        if (fileHandle?.Id is null)
        {
            return MaterializeAttachmentResult.Rejected(Problem(
                status: 502,
                errorCode: "ATTACHMENT_UPLOAD_FAILED",
                title: "Attachment upload failed",
                detail: $"SharePoint Embedded returned no drive-item id for attachment '{request.FileName}'.",
                correlationId: request.CorrelationId));
        }

        // ── Governed sprk_document (message → doc lookup: sprk_document.sprk_communication) ──
        // Mirrors the email inbound attachment shape; storage schema is unchanged.
        var document = new DataverseEntity("sprk_document")
        {
            ["sprk_documentname"] = TruncateTo(request.FileName, 200),
            ["sprk_filename"] = request.FileName, // AI analyzer reads this for file-type detection
            ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", request.CommunicationId),
            ["sprk_graphitemid"] = fileHandle.Id,
            ["sprk_graphdriveid"] = driveId,
        };
        var documentId = await _genericEntityService.CreateAsync(document, cancellationToken);

        // ── sprk_communicationattachment intersection (pre-existing schema; links message → doc) ──
        var attachment = new DataverseEntity("sprk_communicationattachment")
        {
            ["sprk_name"] = TruncateTo(request.FileName, 200),
            ["sprk_communication"] = new EntityReference("sprk_communication", request.CommunicationId),
            ["sprk_attachmenttype"] = new OptionSetValue(AttachmentTypeFile), // File
            ["sprk_document"] = new EntityReference("sprk_document", documentId),
            ["sprk_graphitemid"] = fileHandle.Id,
            ["sprk_graphdriveid"] = driveId,
        };
        var attachmentId = await _genericEntityService.CreateAsync(attachment, cancellationToken);

        _logger.LogInformation(
            "Materialized message attachment '{FileName}' | CommunicationId: {CommunicationId}, DocumentId: " +
            "{DocumentId}, CommunicationAttachmentId: {AttachmentId}, GraphItemId: {GraphItemId}, CorrelationId: {CorrelationId}",
            request.FileName, request.CommunicationId, documentId, attachmentId, fileHandle.Id, request.CorrelationId);

        // The reference the ACS message carries — SPE is the store; the binary is NEVER placed on the ACS message.
        var reference = new MessageAttachmentReference
        {
            DocumentId = documentId,
            CommunicationAttachmentId = attachmentId,
            GraphItemId = fileHandle.Id,
            GraphDriveId = driveId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = fileHandle.Size ?? effectiveSize,
        };
        return MaterializeAttachmentResult.Ok(reference);
    }

    /// <summary>
    /// Evaluates <c>CHAT-ATTACHMENT-POLICY.md</c> for the attachment. Returns <c>null</c> when the attachment is
    /// allowed, or an RFC 7807 <see cref="ProblemDetails"/> describing the violation (ADR-019). Pure — performs no
    /// I/O — so the gate can be asserted directly and runs before any upload.
    /// </summary>
    public ProblemDetails? EvaluatePolicy(MaterializeAttachmentRequest request, long effectiveSize)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Problem(
                status: 400,
                errorCode: "ATTACHMENT_INVALID",
                title: "Invalid attachment",
                detail: "The attachment has no file name.",
                correlationId: request.CorrelationId);
        }

        // Oversize — reject BEFORE upload (25 MB binary cap, CHAT-ATTACHMENT-POLICY.md).
        if (effectiveSize > MaxAttachmentSizeBytes)
        {
            return Problem(
                status: 413,
                errorCode: "ATTACHMENT_TOO_LARGE",
                title: "Attachment too large",
                detail: $"Attachment '{request.FileName}' is {effectiveSize} bytes, exceeding the " +
                        $"{MaxAttachmentSizeBytes}-byte ({MaxAttachmentSizeBytes / (1024 * 1024)} MB) limit.",
                correlationId: request.CorrelationId);
        }

        // Disallowed MIME — reject BEFORE upload (4-type allow-list, CHAT-ATTACHMENT-POLICY.md).
        if (request.ContentType is null || !AllowedContentTypes.Contains(request.ContentType))
        {
            return Problem(
                status: 415,
                errorCode: "ATTACHMENT_TYPE_NOT_ALLOWED",
                title: "Attachment type not allowed",
                detail: $"Attachment '{request.FileName}' has unsupported content type " +
                        $"'{request.ContentType ?? "(none)"}'. Allowed types: {string.Join(", ", AllowedContentTypes)}.",
                correlationId: request.CorrelationId);
        }

        return null;
    }

    private static long EffectiveSize(MaterializeAttachmentRequest request)
        => request.Content.CanSeek ? request.Content.Length : (request.SizeBytes ?? 0);

    private static ProblemDetails Problem(int status, string errorCode, string title, string detail, string? correlationId)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };
        problem.Extensions["errorCode"] = errorCode;
        if (!string.IsNullOrWhiteSpace(correlationId))
            problem.Extensions["correlationId"] = correlationId;
        return problem;
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Channel-neutral input to <see cref="MessageAttachmentMaterializer.MaterializeAsync"/>. Carries the chat file
/// (binary + MIME + filename) plus the message context (the <c>sprk_communication</c> id). No provider type
/// crosses this boundary (NFR-04).
/// </summary>
public sealed record MaterializeAttachmentRequest
{
    /// <summary>The parent <c>sprk_communication</c> (message) record id the attachment belongs to.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>File name including extension.</summary>
    public required string FileName { get; init; }

    /// <summary>MIME content type. Validated against the CHAT-ATTACHMENT-POLICY allow-list before upload.</summary>
    public required string ContentType { get; init; }

    /// <summary>The attachment binary. Uploaded to SPE via the <see cref="ISpeFileOperations"/> facade.</summary>
    public required Stream Content { get; init; }

    /// <summary>Size in bytes when the content stream is non-seekable; ignored when <c>Content.CanSeek</c>.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// Optional SPE drive/container override. When null, the materializer uses
    /// <c>Communication:ArchiveContainerId</c> — the same store the email archive path uses.
    /// </summary>
    public string? DriveId { get; init; }

    /// <summary>Correlation id for tracing / ProblemDetails.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// The reference the ACS message carries after a successful materialization — SPE is the store, so this
/// points at the governed <c>sprk_document</c> + its SPE drive-item, never the binary itself.
/// </summary>
public sealed record MessageAttachmentReference
{
    /// <summary>The governed <c>sprk_document</c> created for the attachment.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>The <c>sprk_communicationattachment</c> intersection row linking message → document.</summary>
    public required Guid CommunicationAttachmentId { get; init; }

    /// <summary>The SPE drive-item id of the uploaded binary.</summary>
    public required string GraphItemId { get; init; }

    /// <summary>The SPE drive id the binary was uploaded to.</summary>
    public required string GraphDriveId { get; init; }

    /// <summary>Original file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Original MIME content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>Size in bytes.</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>
/// Result of a materialization: either a <see cref="MessageAttachmentReference"/> (success) or an RFC 7807
/// <see cref="ProblemDetails"/> (policy rejection / upload failure). Mirrors the DTO discipline of the other
/// Communication seam results.
/// </summary>
public sealed record MaterializeAttachmentResult
{
    /// <summary>The reference the ACS message carries; non-null on success.</summary>
    public MessageAttachmentReference? Reference { get; init; }

    /// <summary>The RFC 7807 problem describing a rejection; non-null on failure.</summary>
    public ProblemDetails? Problem { get; init; }

    /// <summary>True when the attachment materialized successfully.</summary>
    public bool Succeeded => Problem is null && Reference is not null;

    public static MaterializeAttachmentResult Ok(MessageAttachmentReference reference)
        => new() { Reference = reference };

    public static MaterializeAttachmentResult Rejected(ProblemDetails problem)
        => new() { Problem = problem };
}
