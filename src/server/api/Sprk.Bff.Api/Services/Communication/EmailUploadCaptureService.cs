using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// FR-B3 (email-communication-intelligence-r2 task 043): the <b>user-upload (Outlook "Save to Spaarke")</b>
/// capture entry point — the email sibling of <see cref="Channels.MessagingIngestor"/> and the Graph-webhook
/// <see cref="IncomingCommunicationProcessor"/>. It routes a hand-filed email through the SAME Association
/// Engine as mailbox intake, so a saved email is <b>associated + triaged + provenance-stamped</b> and becomes
/// an intelligence-bearing <c>sprk_communication</c> — not merely a <c>sprk_document</c> archive. This resolves
/// the spec's "capture-vs-upload split" structural gap.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-045 extension, not a fork.</b> The engine's real entry contract is the channel-neutral
/// <see cref="NormalizedMessage"/> envelope (<see cref="IncomingAssociationResolver.ResolveAsync"/> operates
/// over it, NEVER over <c>Microsoft.Graph.Message</c>). <see cref="IncomingCommunicationProcessor.ProcessAsync"/>
/// is only the Graph-webhook adapter; <see cref="Channels.MessagingIngestor"/> is the proven non-Graph peer
/// (<see cref="Channels.ICommunicationChannelIngestor"/>). This service is a third peer entry: it maps the
/// upload DTO (<see cref="SaveRequest.Email"/>) → envelope ONCE at the boundary (exactly as
/// <see cref="GraphMessageNormalizer"/> / <c>AcsEventNormalizer</c> do for their channels) and REUSES the same
/// shared seams — it re-implements neither the engine nor dedup.
/// </para>
/// <para>
/// <b>Dedup is structural (FR-C1 / NFR-02).</b> The <c>sprk_communication</c> is created via
/// <see cref="ICommunicationDataverseService.CreateCommunicationRaceProofAsync"/>, whose UNIQUE
/// <c>sprk_internetmessageid</c> alternate key reconciles a same-email save (already captured from a mailbox,
/// or saved by another user) to the single canonical row instead of inserting a duplicate — one dedup
/// authority, no app-level check-then-insert. The FR-C2 saver-stamp + FR-C4 archive-document→communication link
/// are handled by <see cref="Office.OfficeDocumentPersistence"/> when it creates the archive document (the
/// communication this service produces is what that path then resolves + links to).
/// </para>
/// <para>
/// <b>Best-effort / non-fatal (NFR-04).</b> Every step is guarded — a capture, association, or enrichment
/// failure MUST NOT fail the user's save. Registered as a concrete singleton in
/// <c>AddCommunicationModule()</c> per ADR-010; all its dependencies are singletons.
/// </para>
/// </remarks>
public sealed class EmailUploadCaptureService
{
    private readonly ICommunicationDataverseService _communicationService;
    private readonly IncomingAssociationResolver _associationResolver;
    private readonly ICommunicationEnrichmentService _enrichmentService;
    private readonly ILogger<EmailUploadCaptureService> _logger;

    public EmailUploadCaptureService(
        ICommunicationDataverseService communicationService,
        IncomingAssociationResolver associationResolver,
        ICommunicationEnrichmentService enrichmentService,
        ILogger<EmailUploadCaptureService> logger)
    {
        _communicationService = communicationService;
        _associationResolver = associationResolver;
        _enrichmentService = enrichmentService;
        _logger = logger;
    }

    /// <summary>
    /// Captures a user-saved EMAIL as a canonical <c>sprk_communication</c> and runs shared association +
    /// enrichment. No-op (returns null) for non-email saves. Best-effort/non-fatal throughout (NFR-04): any
    /// failure is swallowed + logged so the caller's save always completes. Returns the canonical communication
    /// id (new or reconciled) when a record was created/matched; null otherwise.
    /// </summary>
    public async Task<Guid?> CaptureAsync(SaveRequest request, string? userId, CancellationToken ct)
    {
        // Only emails become intelligence-bearing communications; attachment/document saves stay archive-only.
        if (request.ContentType != SaveContentType.Email || request.Email is null)
            return null;

        try
        {
            var email = request.Email;
            var envelope = BuildEnvelope(email);
            var context = BuildContext(request.TargetEntity);

            // Message-level dedup (FR-C1 / NFR-02): the race-proof create keys on the UNIQUE
            // sprk_internetmessageid alternate key. A same-email save (already captured from a mailbox, or
            // saved by another user) reconciles to the canonical row (WasDuplicate=true) instead of inserting
            // a duplicate — the SINGLE dedup authority. A null/blank internet-message-id creates unguarded.
            var communication = BuildCommunicationEntity(email, envelope);
            var (communicationId, wasDuplicate) = await _communicationService
                .CreateCommunicationRaceProofAsync(communication, email.InternetMessageId, ct);

            if (wasDuplicate)
            {
                // The canonical already carries association + triage + provenance from its original capture/save.
                // Re-running them would duplicate work + could clobber the first association — short-circuit
                // exactly like the inbound path's dedup early-return (FR-C1 / NFR-02).
                _logger.LogInformation(
                    "Upload email reconciled to existing canonical communication {CommunicationId} " +
                    "(internet-message-id match); skipping re-association (single dedup authority).",
                    communicationId);
                return communicationId;
            }

            // ── Association: rung 0 (ExplicitReferenceRung) treats the add-in save-pane selection as the
            //    authoritative regarding (CallerSuppliedRegarding). Non-fatal — the record already exists. ──
            try
            {
                await _associationResolver.ResolveAsync(communicationId, envelope, context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Upload-capture association failed (non-fatal) | CommunicationId: {CommunicationId}",
                    communicationId);
            }

            // ── Triage/enrichment: the SAME entry point the inbound + outbound paths invoke, so upload capture
            //    is not forked. archivedDocumentId=null: the .eml + attachments are RAG-indexed / AI-analyzed by
            //    the Office finalization job (OfficeJobQueue.QueueUploadFinalizationAsync), so EnrichAsync's
            //    RAG/analysis steps intentionally no-op here — identical to the inbound path. Non-fatal. ──
            try
            {
                await _enrichmentService.EnrichAsync(
                    communicationId, CommunicationDirection.Incoming, envelope, archivedDocumentId: null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Upload-capture enrichment failed (non-fatal) | CommunicationId: {CommunicationId}",
                    communicationId);
            }

            _logger.LogInformation(
                "Captured user-saved email as communication {CommunicationId} (association + triage ran) | " +
                "InternetMessageId: {InternetMessageId}, User: {UserId}",
                communicationId, email.InternetMessageId, userId);

            return communicationId;
        }
        catch (Exception ex)
        {
            // NFR-04: the whole capture is best-effort — a failure MUST NOT fail the user's save.
            _logger.LogWarning(
                ex,
                "Upload email capture failed (non-fatal) for message {MessageId}; the save proceeds as an archive.",
                request.Email?.InternetMessageId);
            return null;
        }
    }

    /// <summary>
    /// Maps the upload DTO (<see cref="EmailMetadata"/>) to the channel-neutral <see cref="NormalizedMessage"/>
    /// envelope — the SINGLE upload→envelope boundary (mirrors <see cref="GraphMessageNormalizer.Normalize"/>).
    /// Direction=Incoming (a received email being filed).
    /// </summary>
    private static NormalizedMessage BuildEnvelope(EmailMetadata email)
    {
        var isHtml = email.IsBodyHtml;
        var body = email.Body;

        return new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = email.SenderEmail,
            To = AddressesOfType(email.Recipients, RecipientType.To),
            Cc = AddressesOfType(email.Recipients, RecipientType.Cc),
            Bcc = AddressesOfType(email.Recipients, RecipientType.Bcc),
            Subject = email.Subject,
            // Reduce HTML to plain text for the text-consuming rungs (classifier / semantic match), reusing the
            // SAME lightweight reducer the Graph boundary uses (no new dependency, §10 BFF hygiene).
            BodyText = isHtml ? GraphMessageNormalizer.HtmlToPlainText(body) : body,
            BodyHtml = isHtml ? body : null,
            InternetMessageId = email.InternetMessageId,
            ConversationId = email.ConversationId,
            SentAt = email.SentDate ?? email.ReceivedDate,
            Attachments = MapAttachments(email.Attachments),
        };
    }

    /// <summary>
    /// Builds the ambient <see cref="AssociationContext"/>. The add-in save-pane selection
    /// (<see cref="SaveRequest.TargetEntity"/>) becomes the caller-supplied regarding — exactly what
    /// <see cref="AssociationContext.CallerSuppliedRegarding"/> is for (rung 0, confidence 1.0). Empty when no
    /// selection or the entity type is not a mapped regarding target (the rung then skips it gracefully).
    /// </summary>
    private static AssociationContext BuildContext(SaveEntityReference? target)
    {
        if (target is null)
            return new AssociationContext();

        var logicalName = ToLogicalName(target.EntityType);
        if (logicalName is null)
            return new AssociationContext();

        return new AssociationContext
        {
            CallerSuppliedRegarding = new[]
            {
                new CommunicationAssociation
                {
                    EntityType = logicalName,
                    EntityId = target.EntityId,
                    EntityName = target.DisplayName,
                },
            },
        };
    }

    /// <summary>
    /// Creates the <c>sprk_communication</c> entity to persist. Mirrors
    /// <c>IncomingCommunicationProcessor.CreateCommunicationRecordAsync</c>'s field mapping so a saved email is
    /// stored identically to a captured one (regarding fields are set later by the association resolver).
    /// </summary>
    private static Entity BuildCommunicationEntity(EmailMetadata email, NormalizedMessage envelope)
    {
        var communication = new Entity("sprk_communication")
        {
            ["sprk_name"] = $"Email: {TruncateTo(email.Subject ?? "(No Subject)", 200)}",
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),  // 100000000
            ["statuscode"] = new OptionSetValue((int)CommunicationStatus.Delivered),        // 659490003
            ["statecode"] = new OptionSetValue(0),                                          // Active
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Incoming),  // 100000000
            ["sprk_bodyformat"] = new OptionSetValue(
                email.IsBodyHtml ? (int)BodyFormat.HTML : (int)BodyFormat.PlainText),
            ["sprk_from"] = email.SenderEmail ?? "unknown",
            ["sprk_to"] = envelope.To.Count > 0 ? string.Join("; ", envelope.To) : string.Empty,
            ["sprk_subject"] = email.Subject ?? "(No Subject)",
            ["sprk_body"] = email.Body ?? string.Empty,
            ["sprk_sentat"] = (email.SentDate ?? email.ReceivedDate)?.UtcDateTime ?? DateTime.UtcNow,
            ["sprk_receiveddate"] = (email.ReceivedDate ?? email.SentDate)?.UtcDateTime ?? DateTime.UtcNow,
        };

        // Stamp the internet-message-id when present (it is ALSO the race-proof create's dedup key). A
        // null/blank id leaves the attribute unset — the alternate key excludes nulls (unguarded create).
        if (!string.IsNullOrWhiteSpace(email.InternetMessageId))
            communication["sprk_internetmessageid"] = email.InternetMessageId;

        if (envelope.Cc.Count > 0)
            communication["sprk_cc"] = string.Join("; ", envelope.Cc);

        if (envelope.Attachments.Count > 0)
        {
            communication["sprk_hasattachments"] = true;
            communication["sprk_attachmentcount"] = envelope.Attachments.Count;
        }

        return communication;
    }

    private static IReadOnlyList<string> AddressesOfType(List<Recipient>? recipients, RecipientType type) =>
        recipients?
            .Where(r => r.Type == type && !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => r.Email)
            .ToArray() ?? Array.Empty<string>();

    private static IReadOnlyList<NormalizedAttachment> MapAttachments(List<AttachmentReference>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return Array.Empty<NormalizedAttachment>();

        return attachments
            .Select(a => new NormalizedAttachment
            {
                Name = a.FileName,
                ContentType = a.ContentType,
                SizeBytes = a.Size,
                IsInline = a.IsInline,
            })
            .ToArray();
    }

    /// <summary>
    /// Normalizes the add-in's target entity type (the picker sends enum-form names such as "Matter", or a
    /// Dataverse logical name) to a Dataverse logical name recognized by <see cref="RegardingFieldMap"/>.
    /// Returns null for an unmapped type (the rung then skips it rather than writing an unknown lookup).
    /// </summary>
    private static string? ToLogicalName(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return null;

        // Already a mapped Dataverse logical name (case-insensitive)? Pass through.
        if (RegardingFieldMap.FieldFor(entityType) is not null)
            return entityType;

        // Map the add-in picker's short/enum forms to logical names.
        return entityType.Trim().ToLowerInvariant() switch
        {
            "matter" => "sprk_matter",
            "project" => "sprk_project",
            "invoice" => "sprk_invoice",
            "account" => "account",
            "contact" => "contact",
            "organization" => "sprk_organization",
            "servicerequest" => "sprk_servicerequest",
            "workassignment" => "sprk_workassignment",
            "event" => "sprk_event",
            "budget" => "sprk_budget",
            "analysis" => "sprk_analysis",
            _ => null,
        };
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
