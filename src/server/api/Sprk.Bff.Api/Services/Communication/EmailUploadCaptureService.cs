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
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;
    private readonly ILogger<EmailUploadCaptureService> _logger;

    public EmailUploadCaptureService(
        ICommunicationDataverseService communicationService,
        IncomingAssociationResolver associationResolver,
        ICommunicationEnrichmentService enrichmentService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        ILogger<EmailUploadCaptureService> logger)
    {
        _communicationService = communicationService;
        _associationResolver = associationResolver;
        _enrichmentService = enrichmentService;
        // unified-access-control-r2 task 146 (the word-add-in-r1 note 080 §6.6 hand-off): the captured communication
        // is owned by the records the association files it to (the named Secure team for a secure one) from its first
        // write; an unfiled capture keeps its creator (E1).
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger;
    }

    /// <summary>
    /// Captures a user-saved EMAIL as a canonical <c>sprk_communication</c> and runs shared association +
    /// enrichment. No-op (returns null) for non-email saves. Best-effort/non-fatal throughout (NFR-04): any
    /// failure is swallowed + logged so the caller's save always completes. Returns the canonical communication
    /// id (new or reconciled) when a record was created/matched; null otherwise.
    /// </summary>
    public async Task<Guid?> CaptureAsync(SaveRequest request, string? userId, CancellationToken ct) =>
        (await CaptureWithOutcomeAsync(request, userId, ct))?.CommunicationId;

    /// <summary>
    /// <see cref="CaptureAsync"/>, also saying whether the email reconciled to a communication that ALREADY existed, with
    /// the association decision evaluated for this save (spaarkeai-word-add-in-r1 task 121). A reconciled row keeps its
    /// association here; the Office save decides whether that row may now be filed to the save's record
    /// (<see cref="Office.ReconciledEmailFiling"/>, owner decision 2026-10-09).
    /// </summary>
    public async Task<EmailCaptureOutcome?> CaptureWithOutcomeAsync(SaveRequest request, string? userId, CancellationToken ct)
    {
        // Only emails become intelligence-bearing communications; attachment/document saves stay archive-only.
        if (request.ContentType != SaveContentType.Email || request.Email is null)
            return null;

        try
        {
            var email = request.Email;
            var envelope = BuildEnvelope(email);
            var context = BuildContext(request.TargetEntity);

            // ── Association EVALUATED BEFORE the create (task 146): rung 0 (ExplicitReferenceRung) treats the
            //    add-in save-pane selection as the authoritative regarding (CallerSuppliedRegarding). The decision's
            //    records — and the FR-26 core-ancestor stamps derived from them — decide the OWNER, so a capture filed to
            //    a secure record is the named Secure team's from its first write.
            //
            //    r1 (verifier item 4): a failed evaluation or stamp derivation means the records — and so whether one is
            //    secure — cannot be determined. It used to capture the email UNFILED and creator-owned; now the capture is
            //    SKIPPED, this best-effort writer's refusal shape (the save proceeds as an archive, whose own owner the
            //    Office save already resolved). ──
            AssociationDecision decision;
            Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext ownershipContext;
            try
            {
                decision = await _associationResolver.EvaluateAsync(envelope, context, ct);
                ownershipContext = await _associationResolver.OwnershipContextForNewRecordAsync(decision, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Upload email capture SKIPPED for message {MessageId}: its records could not be determined, so it is "
                    + "not captured unfiled ({Code}, task 146).",
                    request.Email?.InternetMessageId, Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.ParentUndetermined);
                return null;
            }

            // Task 146 c1-r1 (owner round 13 item 9): the Office user who saved the email asked for the capture the
            // application creates.
            var owner = await _ownership.ResolveOwnerAsync(
                ownershipContext with { RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.OfObjectId(userId) },
                ct);
            if (owner.IsRefused)
            {
                // Best-effort writer (NFR-04): a refusal is a SKIPPED capture — nothing is written, the save proceeds
                // as an archive (whose own owner the Office save already resolved).
                _logger.LogWarning(
                    "Upload email capture SKIPPED for message {MessageId}: no owner — {Reason} ({Code}) (task 146).",
                    email.InternetMessageId, owner.Reason, owner.RefusalCode);
                return null;
            }

            // Task 146 r2 (verifier item 9): a capture OWNED from its filing is created WITH that filing — written
            // separately and non-fatally, a failure left a secure team's email filed under nothing, which nobody can
            // see. A failure to build the filing skips the capture (this writer's refusal shape).
            Dictionary<string, object>? filingFields = null;
            if (owner.IsOwned)
            {
                try
                {
                    filingFields = await _associationResolver.BuildNewRecordFieldsAsync(decision, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex,
                        "Upload email capture SKIPPED for message {MessageId}: its filing could not be built, so it is not "
                        + "captured owned by its records' team and filed under nothing ({Code}, task 146).",
                        email.InternetMessageId, Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.ParentUndetermined);
                    return null;
                }
            }

            // Message-level dedup (FR-C1 / NFR-02): the race-proof create keys on the UNIQUE
            // sprk_internetmessageid alternate key. A same-email save (already captured from a mailbox, or
            // saved by another user) reconciles to the canonical row (WasDuplicate=true) instead of inserting
            // a duplicate — the SINGLE dedup authority. A null/blank internet-message-id creates unguarded.
            var communication = BuildCommunicationEntity(email, envelope);
            foreach (var (field, value) in filingFields ?? new Dictionary<string, object>())
            {
                communication[field] = value;
            }

            if (owner.IsOwned)
            {
                communication["ownerid"] = new EntityReference("team", owner.OwningTeamId!.Value);
            }

            owner.StampCreatorOn(communication); // task 146 c1-r1 — the person who saved it

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
                return new EmailCaptureOutcome(communicationId, ReconciledToExisting: true, decision);
            }

            // ── Association: apply the decision evaluated above to the row just created with its owner — only when it
            //    kept its creator (E1); one owned from its filing was created with it. Non-fatal — the record exists. ──
            if (filingFields is null)
            {
                try
                {
                    await _associationResolver.ApplyToNewRecordAsync(communicationId, decision, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Upload-capture association failed (non-fatal) | CommunicationId: {CommunicationId}",
                        communicationId);
                }
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

            return new EmailCaptureOutcome(communicationId, ReconciledToExisting: false, decision);
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

/// <summary>
/// What <see cref="EmailUploadCaptureService.CaptureWithOutcomeAsync"/> produced (spaarkeai-word-add-in-r1 task 121): the
/// canonical communication, whether it ALREADY existed (the save reconciled to it on the Message-ID alternate key, so this
/// capture wrote nothing to it), and the association decision evaluated for this save — the save's own record as the
/// caller-supplied regarding (rung 0).
/// </summary>
public sealed record EmailCaptureOutcome(Guid CommunicationId, bool ReconciledToExisting, AssociationDecision Decision);
