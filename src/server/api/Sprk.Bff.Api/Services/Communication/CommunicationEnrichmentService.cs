using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
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
    private readonly ILogger<CommunicationEnrichmentService> _logger;

    public CommunicationEnrichmentService(
        IPostUploadIndexingEnqueuer postUploadIndexingEnqueuer,
        IGenericEntityService genericEntityService,
        IConfiguration configuration,
        ILogger<CommunicationEnrichmentService> logger)
    {
        _postUploadIndexingEnqueuer = postUploadIndexingEnqueuer;
        _genericEntityService = genericEntityService;
        _configuration = configuration;
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

        await RunStepAsync("assessment-event", communicationId,
            () => RunAssessmentEmissionAsync(communicationId, direction, message, ct));

        _logger.LogInformation(
            "Enrichment complete | CommunicationId: {CommunicationId}, Direction: {Direction}",
            communicationId, direction);
    }

    // ── Step 1: Association ──────────────────────────────────────────────────────
    /// <summary>
    /// SEAM (task 011). The existing <see cref="IncomingAssociationResolver.ResolveAsync"/> requires
    /// a <c>Microsoft.Graph.Message</c> + mailbox/graphMessageId, which ADR-045 forbids the engine from
    /// consuming (the engine MUST operate over <see cref="NormalizedMessage"/> only). It is therefore
    /// NOT signature-compatible with this entry point, so 010 does not delegate to it here.
    /// <para>
    /// Current association coverage is UNCHANGED by 010: inbound is resolved inline by
    /// <see cref="IncomingCommunicationProcessor"/> (via the resolver) at capture time; outbound is
    /// mapped from client-supplied associations at record-creation time
    /// (<c>CommunicationService.MapAssociationFields</c>). Task 011 refactors the resolver into the
    /// Association Engine over the envelope and plugs it in HERE, making association direction-symmetric.
    /// </para>
    /// </summary>
    private Task RunAssociationAsync(
        Guid communicationId, CommunicationDirection direction, NormalizedMessage message, CancellationToken ct)
    {
        _logger.LogDebug(
            "Enrichment[association] seam (task 011) | CommunicationId: {CommunicationId}, Direction: {Direction}. " +
            "Association is currently handled at the capture/creation site; Association Engine over the envelope lands in task 011.",
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

    // ── Step 5: Responsive-Intelligence trigger (assessment event) ───────────────
    /// <summary>
    /// EMIT-ONLY (task 010). Emits a <c>communication_assessed</c> assessment signal. The consumer wiring
    /// lands in task 052 (W5), gated by the 050 coordination gate with
    /// <c>spaarke-ai-architecture-redesign-r2</c>.
    /// <para>
    /// <b>Why a structured log and not <c>IEventRulesService.FireAsync</c> (ESCALATION E5):</b> the existing
    /// <c>IEventRulesService</c> seam is chat-session/SSE-shaped (<c>SurfaceEventRequest{SessionId,UserOid,
    /// FileIds}</c> → <c>IAsyncEnumerable&lt;ChatSseEvent&gt;</c>, token vocabulary <c>document_uploaded</c>
    /// only). It does NOT fit a fire-and-forget <c>communication_assessed</c> emission and is owned by r2.
    /// Task 052 must design/consume the correct non-SSE publish seam under <c>Services/Ai/PublicContracts/</c>.
    /// 010 emits a durable, greppable signal so nothing is silently dropped in the interim.
    /// </para>
    /// </summary>
    private Task RunAssessmentEmissionAsync(
        Guid communicationId, CommunicationDirection direction, NormalizedMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "communication_assessed | CommunicationId: {CommunicationId}, Direction: {Direction}, Subject: '{Subject}', From: {From}, RecipientCount: {RecipientCount}. " +
            "Emit-only (task 010); consumer wiring is task 052 (W5) — ESCALATION E5.",
            communicationId, direction, message.Subject, message.From, message.To.Count);
        return Task.CompletedTask;
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
