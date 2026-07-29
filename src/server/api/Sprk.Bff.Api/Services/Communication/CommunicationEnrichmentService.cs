using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Default <see cref="ICommunicationEnrichmentService"/> — the single direction-agnostic
/// enrichment orchestrator (ADR-045, FR-08). See the interface docs for the staged-delivery
/// notes; each step below documents exactly what task 010 delivers vs. what is deferred to
/// tasks 011 / 052.
/// </summary>
/// <remarks>
/// <para>
/// <b>Auth (ADR-028):</b> this service injects no credential and no AI-internal type. It reaches
/// RAG indexing ONLY through the existing <see cref="IPostUploadIndexingEnqueuer"/> seam (already
/// consumed by <see cref="IncomingCommunicationProcessor"/>), honoring ADR-013 (no direct
/// <c>IOpenAiClient</c>/<c>IPlaybookService</c> injection into Communication-layer code).
/// </para>
/// <para>
/// <b>Best-effort (NFR-06):</b> every step is wrapped in try/catch → log warning → continue.
/// A step failure never propagates to the send or inbound-capture path.
/// </para>
/// </remarks>
public sealed class CommunicationEnrichmentService : ICommunicationEnrichmentService
{
    private readonly IPostUploadIndexingEnqueuer _postUploadIndexingEnqueuer;
    private readonly IGenericEntityService _genericEntityService;
    private readonly IConfiguration _configuration;
    private readonly ICommunicationAssessedProducer _assessedProducer;
    private readonly ICommunicationTriageAi _triageAi;
    private readonly ILogger<CommunicationEnrichmentService> _logger;

    /// <summary>Regarding-matter lookup field (ADR-024) — used ONLY to scope the FR-06 prior-correspondence
    /// grounding for email triage; read-only reference to the shared field map (Engine/RegardingFieldMap.cs
    /// is not modified by this task).</summary>
    private static readonly string MatterRegardingField = RegardingFieldMap.FieldFor("sprk_matter")!;

    public CommunicationEnrichmentService(
        IPostUploadIndexingEnqueuer postUploadIndexingEnqueuer,
        IGenericEntityService genericEntityService,
        IConfiguration configuration,
        ICommunicationAssessedProducer assessedProducer,
        ICommunicationTriageAi triageAi,
        ILogger<CommunicationEnrichmentService> logger)
    {
        _postUploadIndexingEnqueuer = postUploadIndexingEnqueuer;
        _genericEntityService = genericEntityService;
        _configuration = configuration;
        _assessedProducer = assessedProducer ?? throw new ArgumentNullException(nameof(assessedProducer));
        _triageAi = triageAi ?? throw new ArgumentNullException(nameof(triageAi));
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task EnrichAsync(
        Guid communicationId,
        CommunicationDirection direction,
        NormalizedMessage message,
        Guid? archivedDocumentId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Enrichment starting | CommunicationId: {CommunicationId}, Direction: {Direction}, ArchivedDocumentId: {ArchivedDocumentId}",
            communicationId, direction, archivedDocumentId);

        // Order is fixed (FR-08): association → categorization → AI analysis → RAG indexing → assessment.
        await RunStepAsync("association", communicationId,
            () => RunAssociationAsync(communicationId, direction, message, ct));

        await RunStepAsync("categorization", communicationId,
            () => RunCategorizationAsync(communicationId, direction, message, ct));

        await RunStepAsync("ai-analysis", communicationId,
            () => RunAiAnalysisAsync(communicationId, direction, archivedDocumentId, ct));

        await RunStepAsync("rag-indexing", communicationId,
            () => RunRagIndexingAsync(communicationId, direction, message, archivedDocumentId, ct));

        await RunStepAsync("email-triage", communicationId,
            () => RunEmailTriageAsync(communicationId, message, ct));

        await RunStepAsync("assessment-event", communicationId,
            () => RunAssessmentEmissionAsync(communicationId, direction, message, ct));

        _logger.LogInformation(
            "Enrichment complete | CommunicationId: {CommunicationId}, Direction: {Direction}",
            communicationId, direction);
    }

    // ── Step 1: Association ──────────────────────────────────────────────────────
    /// <summary>
    /// SEAM. As of task 011 the <see cref="IncomingAssociationResolver"/> IS the Association Engine over
    /// <see cref="NormalizedMessage"/> (envelope-only, ADR-045 / FR-09). For INBOUND, the processor
    /// invokes the engine at the capture boundary (right after normalizing the Graph message), so this
    /// step is intentionally a no-op — re-running it here would double-resolve. For OUTBOUND, associations
    /// come from the client-supplied set at record-creation (<c>CommunicationService.MapAssociationFields</c>);
    /// running the engine over the outbound envelope requires direction-aware rung content and would
    /// overwrite the client's associations, so it is deferred to the direction-symmetry work
    /// (tasks 012/013/015/017), NOT wired in 011.
    /// </summary>
    private Task RunAssociationAsync(
        Guid communicationId, CommunicationDirection direction, NormalizedMessage message, CancellationToken ct)
    {
        _logger.LogDebug(
            "Enrichment[association] no-op | CommunicationId: {CommunicationId}, Direction: {Direction}. " +
            "Inbound is resolved by the Association Engine at the capture boundary; outbound uses client-supplied " +
            "associations. Direction-symmetric engine invocation via this seam is deferred to tasks 012/013/015/017.",
            communicationId, direction);
        return Task.CompletedTask;
    }

    // ── Step 2: Categorization ───────────────────────────────────────────────────
    /// <summary>
    /// SEAM (schema gap — see task-010 ESCALATION E1). "Categorization (content-class + urgency)" has
    /// NO persistence target: FR-01's schema pass adds no content-class/urgency columns to
    /// <c>sprk_communication</c>. This step logs only; it does NOT write invented fields. The owner must
    /// decide whether categorization gets dedicated schema (W0 amendment) or is subsumed by the FR-15 AI
    /// rung (which already outputs category + urgency). Until then this is a documented no-op.
    /// </summary>
    private Task RunCategorizationAsync(
        Guid communicationId, CommunicationDirection direction, NormalizedMessage message, CancellationToken ct)
    {
        _logger.LogDebug(
            "Enrichment[categorization] seam (no schema target — ESCALATION E1) | CommunicationId: {CommunicationId}, Direction: {Direction}.",
            communicationId, direction);
        return Task.CompletedTask;
    }

    // ── Step 3: AI analysis ──────────────────────────────────────────────────────
    /// <summary>
    /// SEAM (documented no-op for 010). App-only document analysis for the archived <c>.eml</c> is
    /// ALREADY enqueued at the archival site in BOTH paths
    /// (<c>CommunicationService.EnqueueDocumentAnalysisAsync</c> and
    /// <c>IncomingCommunicationProcessor.EnqueueDocumentAnalysisAsync</c>). Re-enqueuing here would be
    /// redundant (idempotency key <c>DocAnalysis:{documentId}</c> would dedup it, but it adds noise).
    /// Centralizing analysis into this service is deferred to task 011 alongside the archival-site
    /// consolidation. For 010 this step is intentionally empty.
    /// </summary>
    private Task RunAiAnalysisAsync(
        Guid communicationId, CommunicationDirection direction, Guid? archivedDocumentId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Enrichment[ai-analysis] seam (already enqueued at archival site) | CommunicationId: {CommunicationId}, Direction: {Direction}, ArchivedDocumentId: {ArchivedDocumentId}.",
            communicationId, direction, archivedDocumentId);
        return Task.CompletedTask;
    }

    // ── Step 4: RAG indexing ─────────────────────────────────────────────────────
    /// <summary>
    /// The substantive NEW capability delivered by task 010: RAG-index the archived outbound <c>.eml</c>
    /// (the previously-missing OUTBOUND half). Resolves the SPE drive/item + file name from the
    /// <c>sprk_document</c> (the enqueuer needs DriveId/ItemId/FileName/TenantId — not a Dataverse GUID;
    /// see ESCALATION E4), then dispatches via the app-only seam (MI-written file, per the writer-identity
    /// rule) exactly as the inbound path does.
    /// <para>
    /// <b>Direction gate (staged symmetry — ESCALATION E3):</b> inbound <c>.eml</c> + attachments are
    /// ALREADY indexed inline by <see cref="IncomingCommunicationProcessor"/> at upload time (with SPE ids
    /// in scope, per-attachment). Re-indexing inbound here would be redundant, and this entry point only
    /// receives the <c>.eml</c> document id (not the per-attachment SPE ids), so it cannot replicate the
    /// inline per-attachment indexing anyway. Task 011 removes the inbound inline indexing and routes BOTH
    /// directions through this step. Until then, 010 indexes OUTBOUND here and leaves inbound as-is.
    /// </para>
    /// </summary>
    private async Task RunRagIndexingAsync(
        Guid communicationId, CommunicationDirection direction, NormalizedMessage message,
        Guid? archivedDocumentId, CancellationToken ct)
    {
        if (direction != CommunicationDirection.Outgoing)
        {
            _logger.LogDebug(
                "Enrichment[rag-indexing] skipped for inbound (already indexed inline; ESCALATION E3) | CommunicationId: {CommunicationId}.",
                communicationId);
            return;
        }

        if (!archivedDocumentId.HasValue)
        {
            _logger.LogDebug(
                "Enrichment[rag-indexing] skipped: no archived document (nothing archived to index) | CommunicationId: {CommunicationId}.",
                communicationId);
            return;
        }

        // Resolve SPE identifiers from the sprk_document (ESCALATION E4: enqueuer needs SPE ids, not a GUID).
        var document = await _genericEntityService.RetrieveAsync(
            "sprk_document",
            archivedDocumentId.Value,
            ["sprk_graphdriveid", "sprk_graphitemid", "sprk_filename"],
            ct);

        var driveId = document.GetAttributeValue<string>("sprk_graphdriveid");
        var itemId = document.GetAttributeValue<string>("sprk_graphitemid");
        var fileName = document.GetAttributeValue<string>("sprk_filename");

        if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId))
        {
            _logger.LogWarning(
                "Enrichment[rag-indexing] skipped: archived document {DocumentId} is missing SPE drive/item ids | CommunicationId: {CommunicationId}.",
                archivedDocumentId.Value, communicationId);
            return;
        }

        var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"] ?? "";

        var request = new PostUploadIndexingRequest(
            TenantId: tenantId,
            DriveId: driveId,
            ItemId: itemId,
            FileName: string.IsNullOrWhiteSpace(fileName) ? "outbound-email.eml" : fileName,
            FileSizeBytes: null,
            ContentType: null,
            DocumentId: archivedDocumentId.Value.ToString(),
            ParentEntity: null,
            SearchIndexName: null, // handler runs the ISearchIndexNameResolver chain
            Source: "OutboundEmail",
            CorrelationId: communicationId.ToString("N"));

        // App-only path: the outbound .eml was written to SPE by the BFF's Managed Identity, so MI can
        // read its own write (writer-identity rule per sdap-auth-patterns.md Pattern 4). Non-fatal.
        await _postUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync(request, ct);

        _logger.LogInformation(
            "Enrichment[rag-indexing] enqueued outbound RAG indexing (previously-missing half closed) | CommunicationId: {CommunicationId}, DocumentId: {DocumentId}.",
            communicationId, archivedDocumentId.Value);
    }

    // ── Step 4.5: Email triage (FR-05) ───────────────────────────────────────────
    /// <summary>
    /// email-communication-intelligence-r1 task 023 (FR-05). Triggers the catalog-authored TRIAGE-EMAIL
    /// Action via the <see cref="ICommunicationTriageAi"/> facade (ADR-013 — no AI internals injected into
    /// this Communication-layer class), reusing the classification signal
    /// <see cref="Engine.Rungs.AiClassificationRung"/> ALREADY produced during association resolution — no
    /// second full LLM pass. The signal is reconstructed from the persisted
    /// <c>sprk_communication.sprk_associationprovenance</c> JSON (see
    /// <see cref="PersistedClassificationSignalReader"/>) rather than re-derived, since rung 5 runs at a
    /// different call site (the Association Engine, via <c>IncomingAssociationResolver</c>) than this
    /// enrichment step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Best-effort (NFR-04).</b> No persisted classification signal (e.g. outbound messages today, or
    /// an inbound message where rung 5 found nothing useful) → this step no-ops (logged at Debug). A
    /// facade failure (Action not routed, completion failure) is ALSO absorbed by
    /// <see cref="ICommunicationTriageAi"/> itself (returns null rather than throwing); this method's own
    /// try/catch plus <see cref="RunStepAsync"/>'s outer guard are defense-in-depth. A triage failure never
    /// fails capture or send.
    /// </para>
    /// <para>
    /// <b>Persistence is task 025's job.</b> This step logs the produced
    /// <see cref="CommunicationTriageResult"/> (closed-set label fields only — summary/obligations are
    /// free text and are NOT logged, per ADR-015) but does NOT write any <c>sprk_communication</c> triage
    /// field. Task 025 extends this method (immediately after the <c>_triageAi.TriageAsync</c> call below)
    /// to add the field writes, mapping the five result fields to the as-built columns documented in
    /// <c>notes/022-triage-email-action.md</c> §2.
    /// </para>
    /// </remarks>
    private async Task RunEmailTriageAsync(Guid communicationId, NormalizedMessage message, CancellationToken ct)
    {
        var record = await _genericEntityService.RetrieveAsync(
            "sprk_communication",
            communicationId,
            ["sprk_associationprovenance", MatterRegardingField],
            ct);

        if (record is null)
        {
            _logger.LogDebug(
                "Enrichment[email-triage] skipped: communication record could not be retrieved | CommunicationId: {CommunicationId}.",
                communicationId);
            return;
        }

        var provenanceJson = record.GetAttributeValue<string>("sprk_associationprovenance");
        var classification = PersistedClassificationSignalReader.TryReadFromProvenanceJson(provenanceJson);

        if (classification is null)
        {
            _logger.LogDebug(
                "Enrichment[email-triage] skipped: no persisted AI-classify signal for this communication yet | CommunicationId: {CommunicationId}.",
                communicationId);
            return;
        }

        var matterRef = record.GetAttributeValue<EntityReference>(MatterRegardingField);
        var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"] ?? "";

        var request = new CommunicationTriageRequest
        {
            Classification = classification,
            Subject = message.Subject ?? string.Empty,
            BodyText = message.BodyText ?? string.Empty,
            MatterId = matterRef?.Id,
            TenantId = tenantId,
        };

        var result = await _triageAi.TriageAsync(request, ct);

        if (result is null)
        {
            _logger.LogInformation(
                "Enrichment[email-triage] produced no result (Action not routed/disabled, or completion failed — non-fatal) | CommunicationId: {CommunicationId}.",
                communicationId);
            return;
        }

        // ADR-015: identifiers/closed-set labels only — summary + obligations carry free text, never logged.
        _logger.LogInformation(
            "Enrichment[email-triage] produced triage result | CommunicationId: {CommunicationId}, Category: {Category}, Priority: {Priority}, ReviewOutcome: {ReviewOutcome}, ObligationCount: {ObligationCount}, MatterGrounded: {MatterGrounded}.",
            communicationId, result.Category, result.Priority, result.ReviewOutcome, result.Obligations.Count, matterRef is not null);

        // Task 025 persists `result` to sprk_communication's triage fields here.
    }

    // ── Step 5: Responsive-Intelligence trigger (assessment event) ───────────────
    /// <summary>
    /// Emits the <c>communication_assessed</c> signal (spec FR-11) via the
    /// <see cref="ICommunicationAssessedProducer"/> seam. The interim registered default is log-only
    /// (<see cref="LoggingCommunicationAssessedProducer"/>); task 041 registers the real comms-policy-gate
    /// consumer behind the same seam, and task 042 (downstream of that gate) writes the
    /// <c>kind=communication-assessed</c> outbox row + <c>appnotification</c> mirror — this method emits the
    /// input signal only and MUST NOT write the outbox or call <c>IEventRulesService.FireAsync</c>.
    /// <para>
    /// <b>Fire-and-forget, non-fatal (NFR-05):</b> the producer call is wrapped so a producer exception is
    /// caught + logged (distinct from the success path, correlatable by CommunicationId) and swallowed — the
    /// enrichment step always completes. <c>RunStepAsync</c>'s outer guard is defense-in-depth.
    /// </para>
    /// <para>
    /// <b>Why the seam is NOT <c>IEventRulesService.FireAsync</c>:</b> that seam is chat-session/SSE-shaped
    /// (<c>SurfaceEventRequest{SessionId,UserOid,FileIds}</c> → <c>IAsyncEnumerable&lt;ChatSseEvent&gt;</c>)
    /// and does not fit a fire-and-forget assessment emission — the original rationale still holds.
    /// </para>
    /// </summary>
    private async Task RunAssessmentEmissionAsync(
        Guid communicationId, CommunicationDirection direction, NormalizedMessage message, CancellationToken ct)
    {
        var signal = new CommunicationAssessedSignal(
            communicationId, direction, message.Subject, message.From, message.To.Count);

        try
        {
            await _assessedProducer.PublishAsync(signal, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // NFR-05: the communication_assessed producer is fire-and-forget — a producer failure MUST NOT
            // fail enrichment. Distinct log from the producer's own success path + RunStepAsync's generic guard.
            _logger.LogWarning(
                ex,
                "communication_assessed producer failed (non-fatal) | CommunicationId: {CommunicationId}",
                communicationId);
        }
    }

    // ── Best-effort wrapper (NFR-06) ─────────────────────────────────────────────
    private async Task RunStepAsync(string stepName, Guid communicationId, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            // NFR-06: never fail the send or inbound-capture path. Log and continue.
            _logger.LogWarning(
                ex,
                "Enrichment step '{Step}' failed (non-fatal) | CommunicationId: {CommunicationId}",
                stepName, communicationId);
        }
    }
}
