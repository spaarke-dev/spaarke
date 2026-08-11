using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Communication;
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
    private readonly ICommunicationProposeAi _proposeAi;
    private readonly ICommunicationCreateTaskAi _createTaskAi;
    private readonly IActionSeam _actionSeam;
    private readonly Engine.CategoryRoutingGate _routingGate;
    private readonly ILogger<CommunicationEnrichmentService> _logger;

    // ── Job B propose (task 030, FR-09) — sprk_emailreviewlog / sprk_emailupdatefield constants ──

    /// <summary>As-built <c>sprk_emailreviewlog.sprk_action</c> option-set integers (task 012 verification).</summary>
    private const int ReviewActionProposed = 100000001;
    private const int ReviewActionApproved = 100000002;
    private const int ReviewActionOverriden = 100000003; // as-built single-d spelling; code keys on the int
    private const int ReviewActionDismissed = 100000004;
    private const int ReviewActionApplied = 100000005;

    /// <summary>As-built <c>sprk_emailreviewlog.sprk_actortype</c> = Machine (task 012 verification).</summary>
    private const int ReviewActorTypeMachine = 100000000;

    /// <summary>
    /// Job C (task 040, FR-14) — the <c>sprk_emailreviewlog.sprk_targetfield</c> sentinel PREFIX used to mark
    /// a "create task" proposal row, distinguishing it from a Job B field-update proposal (which carries a
    /// real target field logical name). Suffixed per-candidate with a short content hash of the candidate's
    /// subject (<see cref="BuildCreateTaskSentinelField"/>) so two DIFFERENT implied tasks on the same email
    /// each get their own open/closed lifecycle — reusing task 030's exact "open Proposed row with no later
    /// terminal row" idempotency shape (<see cref="LoadOpenProposalFieldsAsync"/>) rather than forking a
    /// second pending-store mechanism. This sentinel can never collide with a real allow-listed field name
    /// (Job B's allow-list only ever contains real Dataverse field logical names).
    /// </summary>
    private const string CreateTaskSentinelFieldPrefix = "__create_task__:";

    /// <summary>
    /// Task 041 (FR-13) — the <c>sprk_emailreviewlog.sprk_targetfield</c> sentinel PREFIX for an
    /// attachment-grounded action-extraction proposal row. DISTINCT from
    /// <see cref="CreateTaskSentinelFieldPrefix"/> so an attachment-extracted action and a body-extracted Job C
    /// task on the same email each keep their own open/closed lifecycle under
    /// <see cref="LoadOpenProposalFieldsAsync"/>'s existing per-field walk. Suffixed with a stable hash of
    /// (attachment file name + candidate subject) so distinct actions across attachments do not collide.
    /// </summary>
    private const string AttachmentActionSentinelFieldPrefix = "__attach_action__:";

    /// <summary>
    /// Task 041 (FR-13) — the provenance label written to <c>sprk_emailreviewlog.sprk_actor</c> for
    /// attachment-grounded action rows, distinguishing them from Job C (<see cref="ConsumerTypes.EmailCreateTask"/>)
    /// rows in the audit trail. This is a plain provenance STRING, NOT a routed closed-catalog consumer type —
    /// the AI extraction itself reuses the shipped CREATE-TASK-FROM-EMAIL Action (via <c>ICommunicationCreateTaskAi</c>),
    /// so no new <see cref="ConsumerTypes"/> entry / Binding / eval family is introduced (§11 reuse).
    /// </summary>
    private const string AttachmentActionActor = "email-attachment-action";

    /// <summary>
    /// Task 042 (FR-12) — the <c>sprk_emailreviewlog.sprk_targetfield</c> sentinel PREFIX for a
    /// "regarding-vs-related intent" proposal row (the email PRESENTS a new record while REFERENCING an
    /// existing one → propose creating the new record). DISTINCT from the Job C / attachment-action prefixes so
    /// it keeps its own open/closed lifecycle under <see cref="LoadOpenProposalFieldsAsync"/>'s per-field walk.
    /// Suffixed with a stable hash of the trigger + referenced identifiers so re-enrichment is idempotent.
    /// </summary>
    private const string RegardingIntentSentinelFieldPrefix = "__regarding_intent__:";

    /// <summary>
    /// Task 042 (FR-12) — the provenance label written to <c>sprk_emailreviewlog.sprk_actor</c> for
    /// regarding-intent rows. A plain provenance STRING, NOT a routed closed-catalog consumer type: FR-12 adds
    /// no Action/Binding/eval family and no <see cref="ConsumerTypes"/> entry — intent detection is
    /// deterministic (<see cref="NewRecordIntentDetector"/>) and the "create new record" is proposal-only
    /// (there is no create-record write leg; human confirms in r5). §11 reuse.
    /// </summary>
    private const string RegardingIntentActor = "email-regarding-intent";

    /// <summary>
    /// Task 042 (FR-12) — marker written to <c>sprk_emailreviewlog.sprk_targetentity</c> for a regarding-intent
    /// proposal whose <see cref="NewRecordIntent.ProposedEntityHint"/> is unpinned (generic "new filing"). A
    /// non-entity sentinel so <see cref="LoadOpenProposalFieldsAsync"/>'s per-<c>targetentity</c> idempotency
    /// walk still scopes it; never collides with a real entity logical name.
    /// </summary>
    private const string RegardingIntentUnpinnedTargetEntity = "__new_record__";

    /// <summary>
    /// The 7 core record types whose <c>sprk_communication</c> regarding lookup Job B may propose updates to
    /// (task 020 / note 001 — matter, project, invoice, work assignment, budget, service request, report
    /// card). Reads the WRITE field from the code source of truth (<see cref="RegardingFieldMap"/>), never a
    /// catalog column. The allow-list (<c>sprk_emailupdatefield</c>) is still the SOLE gate on WHICH fields
    /// may be proposed — this list only determines WHICH associated record a proposal targets.
    /// </summary>
    private static readonly IReadOnlyList<(string Entity, string RegardingField)> CoreProposeTargets =
        new[]
        {
            "sprk_matter", "sprk_project", "sprk_invoice", "sprk_workassignment",
            "sprk_budget", "sprk_servicerequest", "sprk_reportcard",
        }
        .Select(e => (Entity: e, RegardingField: RegardingFieldMap.FieldFor(e)!))
        .Where(t => !string.IsNullOrWhiteSpace(t.RegardingField))
        .ToArray();

    /// <summary>Regarding-matter lookup field (ADR-024) — used ONLY to scope the FR-06 prior-correspondence
    /// grounding for email triage; read-only reference to the shared field map (Engine/RegardingFieldMap.cs
    /// is not modified by this task).</summary>
    private static readonly string MatterRegardingField = RegardingFieldMap.FieldFor("sprk_matter")!;

    /// <summary>
    /// Task 025 (FR-07) — as-built <c>sprk_triagepriority</c> CHOICE integer values on
    /// <c>sprk_communication</c>, per <c>notes/schema-to-create.md</c> ("AS-BUILT option-set values,
    /// confirmed by operator 2026-07-29 — AUTHORITATIVE for implementation") and re-confirmed by
    /// <c>notes/011-triage-fields-verified.md</c>. Maps the TRIAGE-EMAIL Action's raw <c>Priority</c> label
    /// (task 022/023's <c>$choices</c> contract) to the Dataverse option-set integer. An unrecognized or
    /// missing label is intentionally absent from this map — <see cref="PersistTriageResultAsync"/> leaves
    /// <c>sprk_triagepriority</c> unset rather than guessing (best-effort, no fabricated value).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> TriagePriorityOptionSetValues =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Urgent"] = 100000000,
            ["High"] = 100000001,
            ["Medium"] = 100000002,
            ["Low"] = 100000003,
        };

    /// <summary>
    /// Task 025 (FR-07) — as-built <c>sprk_reviewoutcome</c> CHOICE integer values on
    /// <c>sprk_communication</c> (D-05 "review outcome", never "disposition"), per
    /// <c>notes/schema-to-create.md</c>. Maps the TRIAGE-EMAIL Action's raw <c>ReviewOutcome</c> label to the
    /// Dataverse option-set integer; an unrecognized/missing label leaves <c>sprk_reviewoutcome</c> unset.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> ReviewOutcomeOptionSetValues =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["File"] = 100000000,
            ["Update"] = 100000001,
            ["Route"] = 100000002,
            ["Dismiss"] = 100000003,
            ["Pending"] = 100000004,
        };

    public CommunicationEnrichmentService(
        IPostUploadIndexingEnqueuer postUploadIndexingEnqueuer,
        IGenericEntityService genericEntityService,
        IConfiguration configuration,
        ICommunicationAssessedProducer assessedProducer,
        ICommunicationTriageAi triageAi,
        ICommunicationProposeAi proposeAi,
        ICommunicationCreateTaskAi createTaskAi,
        IActionSeam actionSeam,
        Engine.CategoryRoutingGate routingGate,
        ILogger<CommunicationEnrichmentService> logger)
    {
        _postUploadIndexingEnqueuer = postUploadIndexingEnqueuer;
        _genericEntityService = genericEntityService;
        _configuration = configuration;
        _assessedProducer = assessedProducer ?? throw new ArgumentNullException(nameof(assessedProducer));
        _triageAi = triageAi ?? throw new ArgumentNullException(nameof(triageAi));
        _proposeAi = proposeAi ?? throw new ArgumentNullException(nameof(proposeAi));
        _createTaskAi = createTaskAi ?? throw new ArgumentNullException(nameof(createTaskAi));
        _actionSeam = actionSeam ?? throw new ArgumentNullException(nameof(actionSeam));
        _routingGate = routingGate ?? throw new ArgumentNullException(nameof(routingGate));
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

        // Hoisted (not re-derived) for task 024's RI-confidence scorer: the SAME CommunicationTriageResult
        // produced by the email-triage step, captured here so the assessment-emission step below can prefer
        // its Priority field without a second triage call. Null when triage produced no result (Action not
        // routed/disabled, or no persisted classification signal yet) — the assessment step falls back to
        // the classification's own Urgency field in that case (see RunAssessmentEmissionAsync).
        CommunicationTriageResult? triageResult = null;

        await RunStepAsync("email-triage", communicationId,
            async () => { triageResult = await RunEmailTriageAsync(communicationId, message, ct); });

        // Job B propose (task 030, FR-09): best-effort, non-fatal (NFR-04). Runs AFTER email-triage so the
        // already-produced triage output can ground the targeted field-value extraction (no second
        // classification). Reuses the hoisted triageResult; no re-derivation.
        await RunStepAsync("email-propose", communicationId,
            () => RunEmailProposeAsync(communicationId, message, triageResult, ct));

        // Job C create-task (task 040, FR-14): best-effort, non-fatal (NFR-04). Runs alongside email-propose
        // (also grounded in the hoisted triageResult, no second classification) — a distinct capability
        // (implied follow-up work vs a field-update proposal), so it is its own step/method, independent of
        // email-propose's allow-list gating.
        await RunStepAsync("email-create-task", communicationId,
            () => RunEmailCreateTaskAsync(communicationId, message, triageResult, ct));

        // Attachment-grounded action extraction (task 041, FR-13): best-effort, non-fatal (NFR-04). Runs after
        // email-create-task; extracts an action stated ONLY in an attachment (grounded per-attachment on the
        // extracted attachment text, deterministically cost-gated to likely action-triggers), cited to the
        // attachment + a machine-verified page locator, and feeds the SAME Job C create leg (no forked create).
        await RunStepAsync("email-attachment-action", communicationId,
            () => RunEmailAttachmentActionAsync(communicationId, message, triageResult, ct));

        // Regarding-vs-related intent (task 042, FR-12): best-effort, non-fatal (NFR-04). Runs AFTER
        // email-triage so it can annotate the already-persisted sprk_triagesummary. Note the SUPPRESSION half
        // of FR-12 (don't auto-file onto a merely-referenced record) happens at CAPTURE in the identifier rung
        // (NewRecordIntentDetector-driven confidence cap) — this enrichment step owns only the human-readable
        // half: the triage-summary cross-reference note + the gated "create new record" proposal (proposal-only,
        // human-confirmed; ADR-024 amended 2026-07-30 — one relationship, no related field).
        await RunStepAsync("email-regarding-intent", communicationId,
            () => RunEmailRegardingIntentAsync(communicationId, message, ct));

        await RunStepAsync("assessment-event", communicationId,
            () => RunAssessmentEmissionAsync(communicationId, direction, message, triageResult, ct));

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

        // FR-D1 / FR-06: tag the resolved regarding as the RAG grounding key on the outbound half too,
        // so a matter-scoped query returns sent correspondence (was null). Best-effort/non-fatal (NFR-04).
        var parentEntity = await RegardingParentEntityMapper.ResolveAsync(
            _genericEntityService, communicationId, _logger, ct);

        var request = new PostUploadIndexingRequest(
            TenantId: tenantId,
            DriveId: driveId,
            ItemId: itemId,
            FileName: string.IsNullOrWhiteSpace(fileName) ? "outbound-email.eml" : fileName,
            FileSizeBytes: null,
            ContentType: null,
            DocumentId: archivedDocumentId.Value.ToString(),
            ParentEntity: parentEntity,
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
    /// <b>Persistence (task 025, FR-07).</b> This step logs the produced
    /// <see cref="CommunicationTriageResult"/> (closed-set label fields only — summary/obligations are
    /// free text and are NOT logged, per ADR-015) and then persists it to the <c>sprk_communication</c>
    /// triage fields via <see cref="PersistTriageResultAsync"/> — the SAME <see cref="IGenericEntityService"/>
    /// write path already used throughout this class (no new write mechanism). Persistence is itself
    /// best-effort (NFR-04): a write/resolve failure degrades + logs and never prevents the already-produced
    /// <see cref="CommunicationTriageResult"/> from being returned/hoisted for task 024's RI-confidence step.
    /// </para>
    /// <para>
    /// <b>Result is hoisted for task 024 (FR-04).</b> Returns the produced <see cref="CommunicationTriageResult"/>
    /// (or <c>null</c> when no triage ran/produced a result) so <see cref="EnrichAsync"/> can pass it, in the
    /// SAME orchestration call, into <see cref="RunAssessmentEmissionAsync"/> — the RI-confidence scorer
    /// prefers <see cref="CommunicationTriageResult.Priority"/> as its urgency source without a second
    /// triage call.
    /// </para>
    /// </remarks>
    private async Task<CommunicationTriageResult?> RunEmailTriageAsync(Guid communicationId, NormalizedMessage message, CancellationToken ct)
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
            return null;
        }

        var provenanceJson = record.GetAttributeValue<string>("sprk_associationprovenance");
        var classification = PersistedClassificationSignalReader.TryReadFromProvenanceJson(provenanceJson);

        if (classification is null)
        {
            _logger.LogDebug(
                "Enrichment[email-triage] skipped: no persisted AI-classify signal for this communication yet | CommunicationId: {CommunicationId}.",
                communicationId);
            return null;
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
            return null;
        }

        // ADR-015: identifiers/closed-set labels only — summary + obligations carry free text, never logged.
        _logger.LogInformation(
            "Enrichment[email-triage] produced triage result | CommunicationId: {CommunicationId}, Category: {Category}, Priority: {Priority}, ReviewOutcome: {ReviewOutcome}, ObligationCount: {ObligationCount}, MatterGrounded: {MatterGrounded}.",
            communicationId, result.Category, result.Priority, result.ReviewOutcome, result.Obligations.Count, matterRef is not null);

        // Task 025 (FR-07): persist the produced result to sprk_communication's triage fields. Best-effort
        // (NFR-04) — its own try/catch means a persistence failure never prevents `result` from being
        // returned/hoisted for task 024's RI-confidence step, and RunStepAsync's outer guard on the
        // "email-triage" step is defense-in-depth on top of that.
        await PersistTriageResultAsync(communicationId, result, classification, provenanceJson, ct).ConfigureAwait(false);

        return result;
    }

    // ── Task 025 (FR-07): persist the triage output to sprk_communication ───────
    /// <summary>
    /// Persists the TRIAGE-EMAIL Action's five-field output (task 022/023) — plus the FR-04 RI-confidence
    /// (task 024) — onto the <c>sprk_communication</c> triage fields (task 011), via the SAME
    /// <see cref="IGenericEntityService.UpdateAsync"/> write path already used throughout this class (no
    /// new write mechanism, no second resolver).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Category → lookup resolution (ADR-024).</b> <paramref name="result"/>'s <c>Category</c> is the
    /// Action's raw taxonomy-label string (task 013's seeded <c>sprk_triagecategory</c> rows, e.g.
    /// <c>"Court / Filing"</c>) — this method resolves the NAME to the taxonomy row's GUID via
    /// <see cref="ResolveTriageCategoryIdAsync"/> and writes a typed <see cref="EntityReference"/>, never
    /// free text. An unmatched category (no seeded row with that <c>sprk_name</c>) degrades gracefully:
    /// <c>sprk_triagecategory</c> is left unset and the miss is logged — this method never fabricates a
    /// taxonomy row.
    /// </para>
    /// <para>
    /// <b>RI-confidence parity with task 024.</b> Computes <c>sprk_riconfidence</c> via the SAME
    /// <see cref="RiConfidenceScorer.Compute"/> formula and the SAME inputs
    /// <see cref="ComputeRiConfidenceAsync"/> uses for the <c>communication_assessed</c> signal — urgency
    /// preferring <paramref name="result"/>.<c>Priority</c>, falling back to
    /// <paramref name="classification"/>.<c>Urgency</c> (the SAME reconstructed signal
    /// <see cref="RunEmailTriageAsync"/> already produced), and deterministic agreement from
    /// <see cref="Engine.AssociationDecisionTrace.TopDeterministicConfidence"/> reconstructed from the SAME
    /// <paramref name="provenanceJson"/> string this method's caller already read. Reusing identical inputs
    /// (not re-reading Dataverse a third time) guarantees the persisted value matches the emitted signal's
    /// confidence exactly.
    /// </para>
    /// <para>
    /// <b>Best-effort (NFR-04).</b> The ENTIRE method is wrapped in try/catch — any failure (category
    /// resolution query, the Dataverse update itself) degrades to a logged warning and returns without
    /// throwing. This is independent, inner defense-in-depth on top of <see cref="RunStepAsync"/>'s outer
    /// guard on the "email-triage" step; either layer alone is sufficient to keep a persistence failure from
    /// ever reaching the capture/send path.
    /// </para>
    /// <para>
    /// <b>Idempotent / additive-only.</b> The update dictionary carries ONLY the six triage fields — it
    /// never touches association/regarding fields or any field another enrichment step owns.
    /// </para>
    /// </remarks>
    private async Task PersistTriageResultAsync(
        Guid communicationId,
        CommunicationTriageResult result,
        CommunicationClassificationResult classification,
        string? provenanceJson,
        CancellationToken ct)
    {
        try
        {
            var fields = new Dictionary<string, object>();

            // category → sprk_triagecategory (LOOKUP → sprk_triagecategory taxonomy; ADR-024 — a resolved
            // reference, never free text). No match → leave unset (best-effort, never fabricate a row).
            if (!string.IsNullOrWhiteSpace(result.Category))
            {
                var categoryId = await ResolveTriageCategoryIdAsync(result.Category, ct).ConfigureAwait(false);
                if (categoryId.HasValue)
                {
                    fields["sprk_triagecategory"] = new EntityReference("sprk_triagecategory", categoryId.Value);
                }
                else
                {
                    _logger.LogInformation(
                        "Enrichment[email-triage] persist: no sprk_triagecategory taxonomy row matches category {Category} — leaving sprk_triagecategory unset | CommunicationId: {CommunicationId}.",
                        result.Category, communicationId);
                }
            }

            // priority → sprk_triagepriority (CHOICE; as-built integers). Unrecognized/missing → unset.
            if (TriagePriorityOptionSetValues.TryGetValue(result.Priority ?? string.Empty, out var priorityValue))
            {
                fields["sprk_triagepriority"] = new OptionSetValue(priorityValue);
            }

            // summary → sprk_triagesummary (multiline text).
            fields["sprk_triagesummary"] = result.Summary ?? string.Empty;

            // obligations[] → sprk_triageobligation (SINGULAR — as-built name; D-06 lean JSON string array).
            fields["sprk_triageobligation"] = JsonSerializer.Serialize(result.Obligations ?? Array.Empty<string>());

            // reviewOutcome → sprk_reviewoutcome (CHOICE; as-built integers). Unrecognized/missing → unset.
            if (ReviewOutcomeOptionSetValues.TryGetValue(result.ReviewOutcome ?? string.Empty, out var outcomeValue))
            {
                fields["sprk_reviewoutcome"] = new OptionSetValue(outcomeValue);
            }

            // RI-confidence → sprk_riconfidence: SAME formula + SAME inputs as ComputeRiConfidenceAsync
            // (task 024), so the persisted value matches the communication_assessed signal's confidence.
            var provenance = PersistedClassificationSignalReader.TryDeserializeProvenance(provenanceJson);
            var deterministicAgreement = provenance?.Decision.TopDeterministicConfidence ?? 0.0;
            var urgencyWeight = !string.IsNullOrWhiteSpace(result.Priority)
                ? RiConfidenceScorer.UrgencyWeightFromPriority(result.Priority)
                : RiConfidenceScorer.UrgencyWeightFromClassification(classification.Urgency);
            fields["sprk_riconfidence"] = RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement);

            // category → owning team (FR-E7 / task 057, ADR-018 CategoryRoutingGate): when routing is enabled
            // and the category is mapped, ASSIGN the communication to the mapped team by setting ownerid (an
            // ownership set on THIS same additive triage update — ADR-024, no second write path). Wrapped
            // independently so a routing failure (unknown team / query error) NEVER drops the triage fields
            // (NFR-04). An unmapped category / disabled routing leaves ownerid untouched (default view).
            await AssignOwningTeamAsync(result.Category, fields, communicationId, ct).ConfigureAwait(false);

            await _genericEntityService.UpdateAsync("sprk_communication", communicationId, fields, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Enrichment[email-triage] persisted triage output to sprk_communication | CommunicationId: {CommunicationId}, FieldCount: {FieldCount}.",
                communicationId, fields.Count);
        }
        catch (Exception ex)
        {
            // NFR-04: a persistence failure MUST NOT fail capture/enrichment — degrade + log, never throw.
            _logger.LogWarning(
                ex,
                "Enrichment[email-triage] failed to persist triage output to sprk_communication (non-fatal) | CommunicationId: {CommunicationId}.",
                communicationId);
        }
    }

    /// <summary>
    /// Resolves a TRIAGE-EMAIL Action <c>category</c> NAME (e.g. <c>"Court / Filing"</c>) to its task-013
    /// seeded <c>sprk_triagecategory</c> row id via the SAME <see cref="IGenericEntityService.RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>
    /// read path other services in this codebase already use for name lookups (no new query mechanism).
    /// Returns <c>null</c> when no row matches — the caller degrades gracefully rather than fabricating a
    /// taxonomy row (ADR-024).
    /// </summary>
    private async Task<Guid?> ResolveTriageCategoryIdAsync(string categoryName, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_triagecategory")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_name", ConditionOperator.Equal, categoryName);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Count > 0 ? result.Entities[0].Id : null;
    }

    /// <summary>
    /// FR-E7 (task 057): if the ADR-018 <see cref="Engine.CategoryRoutingGate"/> maps <paramref name="category"/>
    /// to an owning team, resolve that team's id and set <c>ownerid</c> in <paramref name="fields"/> (assigning
    /// the communication to the team on the SAME additive triage update). Wrapped in its own try/catch so a
    /// routing failure never drops the triage fields (NFR-04). An unmapped category, disabled routing, or an
    /// unknown team name is a no-op (the communication is left in the default/unassigned view — never a forced
    /// mis-assignment). Tenant-scoped resolution activates when a tenant key is plumbed; the global map applies
    /// today (single-org default, mirroring <see cref="AutoFileOptions"/>).
    /// </summary>
    private async Task AssignOwningTeamAsync(
        string? category, Dictionary<string, object> fields, Guid communicationId, CancellationToken ct)
    {
        try
        {
            var teamName = _routingGate.ResolveTeamName(category, tenantKey: null);
            if (string.IsNullOrWhiteSpace(teamName))
                return;

            var teamId = await ResolveTeamIdByNameAsync(teamName, ct).ConfigureAwait(false);
            if (teamId.HasValue)
            {
                fields["ownerid"] = new EntityReference("team", teamId.Value);
                _logger.LogInformation(
                    "Enrichment[email-triage] routing: category {Category} → team {Team} — assigning ownerid | CommunicationId: {CommunicationId}.",
                    category, teamName, communicationId);
            }
            else
            {
                _logger.LogInformation(
                    "Enrichment[email-triage] routing: category {Category} maps to team {Team} but no team row matches that name — leaving ownerid unset | CommunicationId: {CommunicationId}.",
                    category, teamName, communicationId);
            }
        }
        catch (Exception ex)
        {
            // Routing is additive — a failure here must NOT drop the triage fields or fail capture (NFR-04).
            _logger.LogWarning(
                ex,
                "Enrichment[email-triage] routing: category→team assignment failed (non-fatal; triage fields still persist) | Category: {Category}, CommunicationId: {CommunicationId}.",
                category, communicationId);
        }
    }

    /// <summary>Resolves a team NAME to its <c>teamid</c> via the SAME name-lookup read path
    /// <see cref="ResolveTriageCategoryIdAsync"/> uses (no new query mechanism). Null when no team matches.</summary>
    private async Task<Guid?> ResolveTeamIdByNameAsync(string teamName, CancellationToken ct)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, teamName);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Count > 0 ? result.Entities[0].Id : null;
    }

    // ── Step 4.6: Job B propose (task 030, FR-09) ────────────────────────────────
    /// <summary>
    /// email-communication-intelligence-r1 task 030 (FR-09). For a communication associated to one of the 7
    /// core records (task 020), proposes ALLOW-LISTED field updates on that record — each carrying old→new
    /// value, a VERIFIED citation (source + locator + quoted text), a plain-language reason, and a
    /// confidence — stored as PENDING <c>sprk_emailreviewlog</c> <c>Proposed</c> rows. This step NEVER writes
    /// the target record (application is task 031, human-confirmed).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Allow-list is the SOLE gate (FR-11 / C-4).</b> Reads the operator-owned <c>sprk_emailupdatefield</c>
    /// (READ-ONLY to r1) for the associated entity, taking ONLY <c>sprk_enabled = true</c> rows. A field with
    /// no enabled row is never proposed. Each candidate returned by the Action is re-checked against that
    /// enabled set (defense-in-depth even though the fields are supplied to the model).
    /// </para>
    /// <para>
    /// <b>Verify-cited-text (NFR-06).</b> Each surviving candidate's <c>quotedText</c> is re-located in the
    /// source communication/attachment text (<see cref="CitationVerifier"/>); a proposal whose citation
    /// cannot be located is DROPPED, never stored.
    /// </para>
    /// <para>
    /// <b>Reuse, no second pass (FR-05).</b> <paramref name="triageResult"/> — the already-produced triage
    /// output hoisted from the email-triage step — is passed to the propose Action as grounding only, never
    /// re-derived. The <see cref="ICommunicationProposeAi"/> facade is structurally incapable of a second
    /// classification.
    /// </para>
    /// <para>
    /// <b>Best-effort (NFR-04).</b> The ENTIRE method is wrapped in try/catch — any failure (allow-list read,
    /// facade completion, a Proposed-row write) degrades to a logged warning and returns without throwing,
    /// independent of <see cref="RunStepAsync"/>'s outer guard on the "email-propose" step. A propose failure
    /// never fails capture or send.
    /// </para>
    /// <para>
    /// <b>Append-only + idempotent.</b> A proposal is immutable once proposed (task 031 writes a NEW
    /// Approved/Applied row on resolution; this step never mutates a Proposed row). Re-enrichment is
    /// idempotent: an OPEN Proposed row for the same <c>(communication, targetentity, targetfield)</c> — one
    /// with no later terminal row — suppresses a duplicate.
    /// </para>
    /// </remarks>
    private async Task RunEmailProposeAsync(
        Guid communicationId, NormalizedMessage message, CommunicationTriageResult? triageResult, CancellationToken ct)
    {
        try
        {
            // Read the communication's core regarding lookups (the association from task 020) + privilege flag.
            var columns = CoreProposeTargets.Select(t => t.RegardingField)
                .Append("sprk_associationprovenance").ToArray();
            var record = await _genericEntityService.RetrieveAsync("sprk_communication", communicationId, columns, ct)
                .ConfigureAwait(false);
            if (record is null)
            {
                _logger.LogDebug(
                    "Enrichment[email-propose] skipped: communication record could not be retrieved | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            var associations = ResolveAssociatedCoreRecords(record);
            if (associations.Count == 0)
            {
                _logger.LogDebug(
                    "Enrichment[email-propose] skipped: no association to a core record (nothing to keep current) | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            // ADR-015: privilege is a FLAG carried into the proposal JSON, never a suppress/auto-act decision.
            var provenanceJson = record.GetAttributeValue<string>("sprk_associationprovenance");
            var privilegeFlagged =
                PersistedClassificationSignalReader.TryReadFromProvenanceJson(provenanceJson)?.PrivilegeFlagged ?? false;

            var sourceText = CitationVerifier.BuildSourceText(message.Subject, message.BodyText, message.AttachmentText);
            var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"] ?? "";

            foreach (var (entityLogicalName, recordId) in associations)
            {
                await ProposeForRecordAsync(
                    communicationId, entityLogicalName, recordId, message, triageResult,
                    sourceText, privilegeFlagged, tenantId, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // NFR-04: a propose failure MUST NOT fail capture/enrichment — degrade + log, never throw.
            _logger.LogWarning(
                ex,
                "Enrichment[email-propose] failed (non-fatal) | CommunicationId: {CommunicationId}.",
                communicationId);
        }
    }

    /// <summary>
    /// Proposes allow-listed updates for ONE associated record: reads the enabled allow-list, invokes the
    /// propose Action, then gates each candidate (allow-list re-check → coerce → old-value read →
    /// verify-cited-text → no-op skip → open-proposal dedup) and stores the survivors as <c>Proposed</c> rows.
    /// </summary>
    private async Task ProposeForRecordAsync(
        Guid communicationId,
        string entityLogicalName,
        Guid recordId,
        NormalizedMessage message,
        CommunicationTriageResult? triageResult,
        string sourceText,
        bool privilegeFlagged,
        string tenantId,
        CancellationToken ct)
    {
        var allowList = await LoadEnabledAllowListAsync(entityLogicalName, ct).ConfigureAwait(false);
        if (allowList.Count == 0)
        {
            _logger.LogDebug(
                "Enrichment[email-propose] no enabled sprk_emailupdatefield rows for {Entity} — no proposals | CommunicationId: {CommunicationId}.",
                entityLogicalName, communicationId);
            return;
        }

        var request = new CommunicationProposeRequest
        {
            EntityLogicalName = entityLogicalName,
            Fields = allowList.Values.Select(a => new ProposableField
            {
                FieldLogicalName = a.FieldLogicalName,
                FieldType = a.FieldType,
                ExtractionGuidance = a.ExtractionGuidance,
            }).ToArray(),
            Triage = triageResult,
            Subject = message.Subject ?? string.Empty,
            BodyText = message.BodyText ?? string.Empty,
            AttachmentText = message.AttachmentText,
            TenantId = tenantId,
        };

        var candidates = await _proposeAi.ProposeAsync(request, ct).ConfigureAwait(false);
        if (candidates is not { Count: > 0 })
        {
            _logger.LogDebug(
                "Enrichment[email-propose] produced no candidate proposals for {Entity} | CommunicationId: {CommunicationId}.",
                entityLogicalName, communicationId);
            return;
        }

        var openProposalFields = await LoadOpenProposalFieldsAsync(communicationId, entityLogicalName, ct).ConfigureAwait(false);
        var storedCount = 0;

        foreach (var candidate in candidates)
        {
            // (a) Allow-list re-check — a field with no enabled row is NEVER proposed (the SOLE gate).
            if (candidate.Field is null || !allowList.TryGetValue(candidate.Field, out var allowRow))
            {
                _logger.LogDebug(
                    "Enrichment[email-propose] dropped candidate for non-allow-listed field {Field} on {Entity} | CommunicationId: {CommunicationId}.",
                    candidate.Field, entityLogicalName, communicationId);
                continue;
            }

            // (b) Coerce the raw value per the allow-listed field type; drop if it cannot be shaped.
            if (!EmailUpdateFieldCoercion.TryCoerce(allowRow.FieldType, candidate.NewValue, out var coercedValue)
                || coercedValue is null)
            {
                _logger.LogDebug(
                    "Enrichment[email-propose] dropped candidate for {Entity}.{Field}: value not coercible to {FieldType} | CommunicationId: {CommunicationId}.",
                    entityLogicalName, candidate.Field, allowRow.FieldType, communicationId);
                continue;
            }

            // (d) Verify-cited-text (NFR-06) — drop any candidate whose quotedText is not in the source.
            if (!CitationVerifier.IsCitedTextPresent(sourceText, candidate.Citation?.QuotedText))
            {
                _logger.LogDebug(
                    "Enrichment[email-propose] dropped candidate for {Entity}.{Field}: cited text not located in source (NFR-06) | CommunicationId: {CommunicationId}.",
                    entityLogicalName, candidate.Field, communicationId);
                continue;
            }

            // Idempotency — skip if an OPEN Proposed row already exists for this (entity, field).
            if (openProposalFields.Contains(candidate.Field))
            {
                _logger.LogDebug(
                    "Enrichment[email-propose] skipped {Entity}.{Field}: an open Proposed row already exists (append-only, idempotent) | CommunicationId: {CommunicationId}.",
                    entityLogicalName, candidate.Field, communicationId);
                continue;
            }

            // (c) Read the record's CURRENT value = oldValue.
            var oldValue = await ReadCurrentFieldValueAsync(entityLogicalName, recordId, candidate.Field, ct)
                .ConfigureAwait(false);

            // (e) Skip a no-op (proposed value equals current value).
            if (string.Equals(oldValue?.Trim(), coercedValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Enrichment[email-propose] skipped {Entity}.{Field}: proposed value equals current value (no-op) | CommunicationId: {CommunicationId}.",
                    entityLogicalName, candidate.Field, communicationId);
                continue;
            }

            await StoreProposedRowAsync(
                communicationId, entityLogicalName, recordId, allowRow, candidate,
                oldValue, coercedValue, privilegeFlagged, ct).ConfigureAwait(false);
            storedCount++;
        }

        _logger.LogInformation(
            "Enrichment[email-propose] stored {StoredCount} pending proposal(s) of {CandidateCount} candidate(s) for {Entity} (allow-listed, cited, PENDING) | CommunicationId: {CommunicationId}.",
            storedCount, candidates.Count, entityLogicalName, communicationId);
    }

    /// <summary>An enabled <c>sprk_emailupdatefield</c> allow-list row (coercion hint + guidance + confirm).</summary>
    private sealed record AllowListRow(string FieldLogicalName, string FieldType, string? ExtractionGuidance, bool RequireConfirm);

    /// <summary>Resolves the associated core records from the communication's populated regarding lookups.</summary>
    private static IReadOnlyList<(string Entity, Guid RecordId)> ResolveAssociatedCoreRecords(Entity record)
    {
        var associations = new List<(string, Guid)>();
        foreach (var (entity, regardingField) in CoreProposeTargets)
        {
            var reference = record.GetAttributeValue<EntityReference>(regardingField);
            if (reference is not null && reference.Id != Guid.Empty)
            {
                associations.Add((entity, reference.Id));
            }
        }
        return associations;
    }

    /// <summary>
    /// Reads the ENABLED <c>sprk_emailupdatefield</c> rows for the associated entity, keyed by target field
    /// logical name (<c>sprk_targetfieldlogicalname</c> — NOT <c>sprk_targetfield</c>, per note 001). Resolves
    /// the entity's <c>sprk_recordtype_ref</c> id first, then filters the (org-owned) allow-list by that
    /// lookup + <c>sprk_enabled = true</c>. Rows with an unrecognized field type or a blank field name are
    /// skipped (never guessed). Empty when the operator enabled nothing for this entity.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, AllowListRow>> LoadEnabledAllowListAsync(
        string entityLogicalName, CancellationToken ct)
    {
        var recordTypeRefId = await ResolveRecordTypeRefIdAsync(entityLogicalName, ct).ConfigureAwait(false);
        if (recordTypeRefId is null)
        {
            return new Dictionary<string, AllowListRow>(StringComparer.OrdinalIgnoreCase);
        }

        var query = new QueryExpression("sprk_emailupdatefield")
        {
            ColumnSet = new ColumnSet(
                "sprk_targetfieldlogicalname", "sprk_fieldtype", "sprk_extractionguidance", "sprk_requireconfirm"),
        };
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("sprk_targetentity", ConditionOperator.Equal, recordTypeRefId.Value);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        var rows = new Dictionary<string, AllowListRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in result.Entities)
        {
            var field = row.GetAttributeValue<string>("sprk_targetfieldlogicalname")?.Trim();
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var fieldTypeValue = row.GetAttributeValue<OptionSetValue>("sprk_fieldtype")?.Value;
            var fieldType = fieldTypeValue.HasValue ? EmailUpdateFieldTypes.FromOptionSet(fieldTypeValue.Value) : null;
            if (fieldType is null)
            {
                continue; // unrecognized sprk_fieldtype ⇒ never guess a coercion
            }

            rows[field] = new AllowListRow(
                FieldLogicalName: field,
                FieldType: fieldType,
                ExtractionGuidance: row.GetAttributeValue<string>("sprk_extractionguidance"),
                RequireConfirm: row.GetAttributeValue<bool>("sprk_requireconfirm"));
        }

        return rows;
    }

    /// <summary>Resolves the <c>sprk_recordtype_ref</c> id for an entity logical name (via
    /// <c>sprk_recordlogicalname</c>). Null when no catalog row matches.</summary>
    private async Task<Guid?> ResolveRecordTypeRefIdAsync(string entityLogicalName, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_recordtype_ref")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, entityLogicalName);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result.Entities.Count > 0 ? result.Entities[0].Id : null;
    }

    /// <summary>
    /// Loads the set of target fields that already have an OPEN proposal for this (communication, entity) —
    /// a <c>Proposed</c> row whose most-recent state (walking <c>createdon</c> ascending) is still open (no
    /// later Approved/Applied/Dismissed/Overriden). This is the same "open/pending" definition task 032's feed
    /// and task 031's apply use; here it makes re-enrichment idempotent (no duplicate Proposed rows).
    /// </summary>
    private async Task<IReadOnlySet<string>> LoadOpenProposalFieldsAsync(
        Guid communicationId, string entityLogicalName, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_emailreviewlog")
        {
            ColumnSet = new ColumnSet("sprk_action", "sprk_targetfield", "sprk_targetentity", "createdon"),
        };
        query.Criteria.AddCondition("sprk_communication", ConditionOperator.Equal, communicationId);
        query.Criteria.AddCondition("sprk_targetentity", ConditionOperator.Equal, entityLogicalName);
        query.AddOrder("createdon", OrderType.Ascending);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        // Walk chronologically per field: a Proposed row opens; a terminal row closes.
        var state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in result.Entities)
        {
            var field = row.GetAttributeValue<string>("sprk_targetfield")?.Trim();
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var action = row.GetAttributeValue<OptionSetValue>("sprk_action")?.Value;
            switch (action)
            {
                case ReviewActionProposed:
                    state[field] = true;
                    break;
                case ReviewActionApproved:
                case ReviewActionApplied:
                case ReviewActionDismissed:
                case ReviewActionOverriden:
                    state[field] = false;
                    break;
            }
        }

        return state.Where(kv => kv.Value).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads the record's current value for a field and renders it to a comparable string (the
    /// proposal's <c>oldValue</c>). Best-effort — a read failure degrades to null (unknown current value),
    /// never throws into the propose batch.</summary>
    private async Task<string?> ReadCurrentFieldValueAsync(
        string entityLogicalName, Guid recordId, string field, CancellationToken ct)
    {
        try
        {
            var record = await _genericEntityService.RetrieveAsync(entityLogicalName, recordId, new[] { field }, ct)
                .ConfigureAwait(false);
            return StringifyDataverseValue(record?.GetAttributeValue<object>(field));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Enrichment[email-propose] could not read current value of {Entity}.{Field} (non-fatal; oldValue unknown).",
                entityLogicalName, field);
            return null;
        }
    }

    /// <summary>Renders a Dataverse attribute value to a stable comparison string.</summary>
    private static string? StringifyDataverseValue(object? value) => value switch
    {
        null => null,
        EntityReference er => er.Id.ToString(),
        OptionSetValue osv => osv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Money money => money.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>
    /// Stores ONE surviving proposal as a PENDING <c>sprk_emailreviewlog</c> <c>Proposed</c> row (Decision 1
    /// — the schema's designed proposal-lifecycle store; no new table). Carries the typed columns task 032's
    /// feed + task 031's apply read, plus the full old→new + citation JSON in <c>sprk_aisuggestion</c>. NEVER
    /// writes the target record (application is task 031).
    /// </summary>
    private async Task StoreProposedRowAsync(
        Guid communicationId,
        string entityLogicalName,
        Guid recordId,
        AllowListRow allowRow,
        ProposedFieldCandidate candidate,
        string? oldValue,
        string coercedValue,
        bool privilegeFlagged,
        CancellationToken ct)
    {
        var suggestion = new
        {
            field = allowRow.FieldLogicalName,
            fieldType = allowRow.FieldType,
            oldValue,
            newValue = coercedValue,
            citation = new
            {
                source = candidate.Citation?.Source,
                locator = candidate.Citation?.Locator,
                quotedText = candidate.Citation?.QuotedText,
            },
            reason = candidate.Reason,
            confidence = candidate.Confidence,
            requireConfirm = allowRow.RequireConfirm,
            privilegeFlagged, // ADR-015: carried as a flag, never a suppress/auto-act decision
        };
        var suggestionJson = JsonSerializer.Serialize(suggestion);

        var name = $"Proposed update: {entityLogicalName}.{allowRow.FieldLogicalName}";
        var entity = new Entity("sprk_emailreviewlog")
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeMachine),
            ["sprk_action"] = new OptionSetValue(ReviewActionProposed),
            ["sprk_actor"] = ConsumerTypes.EmailPropose,
            ["sprk_targetentity"] = Truncate(entityLogicalName, 100),
            ["sprk_targetrecordid"] = Truncate(recordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(allowRow.FieldLogicalName, 100),
            ["sprk_confidence"] = (decimal)candidate.Confidence,
            ["sprk_sourceref"] = Truncate(candidate.Citation?.Locator ?? candidate.Citation?.Source ?? string.Empty, 1000),
            ["sprk_aisuggestion"] = suggestionJson,
        };

        await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Enrichment[email-propose] stored Proposed row | CommunicationId: {CommunicationId}, TargetEntity: {Entity}, TargetField: {Field}, Confidence: {Confidence:F2}, PrivilegeFlagged: {PrivilegeFlagged}.",
            communicationId, entityLogicalName, allowRow.FieldLogicalName, candidate.Confidence, privilegeFlagged);
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    // ── Step 4.7: Job C create-task (task 040, FR-14) ────────────────────────────
    /// <summary>
    /// email-communication-intelligence-r1 task 040 (FR-14). For a communication associated to one of the 7
    /// core records (task 020), extracts candidate follow-up tasks/events implied by the email content —
    /// grounded in the already-produced triage output (hoisted, no second classification) — and, for each
    /// surviving candidate, either creates it immediately via the SHIPPED create-task write core
    /// (<see cref="IActionSeam.CreateTaskAsync"/> → <c>TaskActionCore</c>) when it carries NO concrete
    /// deadline, or stores it PENDING (an <c>sprk_emailreviewlog</c> <c>Proposed</c> row — reusing task 030's
    /// Decision-1 pending-store pattern, no new table) for HUMAN CONFIRM when it DOES carry a deadline
    /// (NFR-06 / ADR-015 — nothing deadline-bearing auto-finalizes). Every created/proposed item carries a
    /// citation to the source communication (email + locator).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Which associated record.</b> Reuses the SAME core-record association read as Job B
    /// (<see cref="CoreProposeTargets"/> / <see cref="ResolveAssociatedCoreRecords"/>) and targets the
    /// FIRST resolved association (deterministic order) — "the record the email is associated to" (task
    /// 020) — rather than invoking the extraction Action once per associated record, which would multiply
    /// LLM cost for the same email without a corresponding benefit.
    /// </para>
    /// <para>
    /// <b>Reuse, no fork (FR-14 / ADR-039).</b> The create is the SAME session-agnostic write core Job B's
    /// apply step (task 031) and the chat-loop CREATE-TASK@v1 capability's <c>dataverse.create_record</c>
    /// tool both ultimately reach (<c>TaskActionCore</c>) — reached here via the ADR-013
    /// <see cref="IActionSeam"/> facade (already registered unconditionally, not AI-model-gated). No second
    /// create mechanism is introduced.
    /// </para>
    /// <para>
    /// <b>Uniform audit trail.</b> EVERY surviving candidate is stored as a <c>Proposed</c> row FIRST
    /// (<see cref="StoreCreateTaskProposedRowAsync"/>) — this is the citation-bearing audit record task 041
    /// and any future review surface can read. A non-deadline-bearing candidate immediately gets a SECOND
    /// <c>Applied</c> row once the create succeeds (<see cref="WriteCreateTaskAppliedRowAsync"/>); a
    /// deadline-bearing candidate's <c>Proposed</c> row is left OPEN (no later terminal row) — the exact
    /// "open/pending" shape task 030/031/032 already established, reusing <see cref="LoadOpenProposalFieldsAsync"/>
    /// unmodified (it is generic over any <c>sprk_targetfield</c> string, sentinel or real) rather than
    /// forking a second idempotency query.
    /// </para>
    /// <para>
    /// <b>Best-effort (NFR-04).</b> The ENTIRE method is wrapped in try/catch — any failure (association
    /// read, facade completion, a Dataverse write) degrades to a logged warning and returns without
    /// throwing, independent of <see cref="RunStepAsync"/>'s outer guard on the "email-create-task" step. A
    /// Job C failure never fails capture or send.
    /// </para>
    /// </remarks>
    private async Task RunEmailCreateTaskAsync(
        Guid communicationId, NormalizedMessage message, CommunicationTriageResult? triageResult, CancellationToken ct)
    {
        try
        {
            var columns = CoreProposeTargets.Select(t => t.RegardingField).ToArray();
            var record = await _genericEntityService.RetrieveAsync("sprk_communication", communicationId, columns, ct)
                .ConfigureAwait(false);
            if (record is null)
            {
                _logger.LogDebug(
                    "Enrichment[email-create-task] skipped: communication record could not be retrieved | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            var associations = ResolveAssociatedCoreRecords(record);
            if (associations.Count == 0)
            {
                _logger.LogDebug(
                    "Enrichment[email-create-task] skipped: no association to a core record (nothing to attach a task to) | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            // "The record the email is associated to" (singular, task 020) — the first resolved association
            // in CoreProposeTargets' deterministic order. Multiple simultaneous associations do not multiply
            // the extraction call.
            var (entityLogicalName, recordId) = associations[0];

            var sourceText = CitationVerifier.BuildSourceText(message.Subject, message.BodyText, message.AttachmentText);
            var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"] ?? "";

            var request = new CommunicationCreateTaskRequest
            {
                EntityLogicalName = entityLogicalName,
                Triage = triageResult,
                Subject = message.Subject ?? string.Empty,
                BodyText = message.BodyText ?? string.Empty,
                AttachmentText = message.AttachmentText,
                TenantId = tenantId,
            };

            var candidates = await _createTaskAi.ExtractAsync(request, ct).ConfigureAwait(false);
            if (candidates is not { Count: > 0 })
            {
                _logger.LogDebug(
                    "Enrichment[email-create-task] produced no candidate follow-up tasks for {Entity} | CommunicationId: {CommunicationId}.",
                    entityLogicalName, communicationId);
                return;
            }

            var openSentinels = await LoadOpenProposalFieldsAsync(communicationId, entityLogicalName, ct).ConfigureAwait(false);
            var createdCount = 0;
            var pendingCount = 0;

            foreach (var candidate in candidates)
            {
                // Verify-cited-text (NFR-06) — drop any candidate whose quotedText is not in the source.
                if (!CitationVerifier.IsCitedTextPresent(sourceText, candidate.Citation?.QuotedText))
                {
                    _logger.LogDebug(
                        "Enrichment[email-create-task] dropped candidate '{Subject}': cited text not located in source (NFR-06) | CommunicationId: {CommunicationId}.",
                        candidate.Subject, communicationId);
                    continue;
                }

                var sentinelField = BuildCreateTaskSentinelField(candidate.Subject);

                // Idempotency — skip if an OPEN proposal already exists for this exact candidate (same
                // subject, same associated entity) on a prior enrichment run.
                if (openSentinels.Contains(sentinelField))
                {
                    _logger.LogDebug(
                        "Enrichment[email-create-task] skipped '{Subject}': an open Proposed row already exists (append-only, idempotent) | CommunicationId: {CommunicationId}.",
                        candidate.Subject, communicationId);
                    continue;
                }

                var proposedRowId = await StoreCreateTaskProposedRowAsync(
                    communicationId, entityLogicalName, recordId, sentinelField, candidate, ct).ConfigureAwait(false);

                if (candidate.DueDate.HasValue)
                {
                    // NFR-06 / ADR-015: deadline-bearing candidates NEVER auto-finalize. The Proposed row is
                    // left OPEN for human confirm; nothing is created here.
                    pendingCount++;
                    _logger.LogInformation(
                        "Enrichment[email-create-task] deadline-bearing candidate '{Subject}' stored PENDING for human confirm (DueDate: {DueDate:yyyy-MM-dd}) | CommunicationId: {CommunicationId}.",
                        candidate.Subject, candidate.DueDate, communicationId);
                    continue;
                }

                // Non-deadline-bearing: create immediately via the SHIPPED create-task write core.
                var description = BuildCreatedTaskDescription(candidate, communicationId);
                var createResult = await _actionSeam.CreateTaskAsync(
                    new CreateTaskRequest
                    {
                        Subject = Truncate(candidate.Subject, 200),
                        Description = description,
                        DueDate = null,
                        RegardingObjectId = recordId,
                        RegardingObjectType = entityLogicalName,
                    },
                    ct).ConfigureAwait(false);

                if (createResult.Success && createResult.TaskId != Guid.Empty)
                {
                    await WriteCreateTaskAppliedRowAsync(
                        communicationId, entityLogicalName, recordId, sentinelField, candidate, createResult.TaskId, ct)
                        .ConfigureAwait(false);
                    createdCount++;
                }
                else
                {
                    // Degraded/failed create — the Proposed row stays open (no Applied row), so a future
                    // re-enrichment can retry rather than silently losing the candidate.
                    _logger.LogWarning(
                        "Enrichment[email-create-task] create-task did not succeed for '{Subject}' ({Error}); Proposed row left open for retry | CommunicationId: {CommunicationId}.",
                        candidate.Subject, createResult.Error, communicationId);
                }
            }

            _logger.LogInformation(
                "Enrichment[email-create-task] processed {CandidateCount} candidate(s) for {Entity}: {CreatedCount} created, {PendingCount} pending human confirm | CommunicationId: {CommunicationId}.",
                candidates.Count, entityLogicalName, createdCount, pendingCount, communicationId);
        }
        catch (Exception ex)
        {
            // NFR-04: a Job C failure MUST NOT fail capture/enrichment — degrade + log, never throw.
            _logger.LogWarning(
                ex,
                "Enrichment[email-create-task] failed (non-fatal) | CommunicationId: {CommunicationId}.",
                communicationId);
        }
    }

    /// <summary>Builds the sentinel <c>sprk_targetfield</c> value for a Job C candidate — the
    /// <see cref="CreateTaskSentinelFieldPrefix"/> plus a short, STABLE (process-independent) hash of the
    /// candidate's subject, so distinct implied tasks on the same email each get their own open/closed
    /// lifecycle under <see cref="LoadOpenProposalFieldsAsync"/>'s existing per-field walk.</summary>
    private static string BuildCreateTaskSentinelField(string subject)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(subject.Trim().ToLowerInvariant()));
        return CreateTaskSentinelFieldPrefix + Convert.ToHexString(hash)[..8];
    }

    /// <summary>Appends the source-communication citation as a provenance line to a created task's
    /// description (FR-14 — every created/proposed item carries a citation to the source communication).</summary>
    private static string BuildCreatedTaskDescription(TaskCandidate candidate, Guid communicationId)
    {
        var locator = candidate.Citation?.Locator ?? candidate.Citation?.Source ?? "email";
        var quoted = candidate.Citation?.QuotedText ?? string.Empty;
        var provenance = $"Provenance: source communication {communicationId:D}; {locator}. \"{quoted}\"";
        return string.IsNullOrWhiteSpace(candidate.Description)
            ? provenance
            : $"{candidate.Description}\n\n{provenance}";
    }

    /// <summary>
    /// Stores ONE candidate as a PENDING <c>sprk_emailreviewlog</c> <c>Proposed</c> row — the SAME
    /// Decision-1 pending-store reuse as Job B (task 030), keyed by <paramref name="sentinelField"/> instead
    /// of a real field logical name. Returns the created row id (not currently consumed by the caller, kept
    /// for parity with a future apply/feed reader).
    /// </summary>
    private async Task<Guid> StoreCreateTaskProposedRowAsync(
        Guid communicationId,
        string entityLogicalName,
        Guid recordId,
        string sentinelField,
        TaskCandidate candidate,
        CancellationToken ct)
    {
        var suggestion = new
        {
            kind = "create-task",
            subject = candidate.Subject,
            description = candidate.Description,
            dueDate = candidate.DueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            regardingObjectType = entityLogicalName,
            regardingObjectId = recordId,
            citation = new
            {
                source = candidate.Citation?.Source,
                locator = candidate.Citation?.Locator,
                quotedText = candidate.Citation?.QuotedText,
            },
            reason = candidate.Reason,
            confidence = candidate.Confidence,
            requireConfirm = candidate.DueDate.HasValue, // NFR-06/ADR-015: deadline-bearing requires confirm
        };
        var suggestionJson = JsonSerializer.Serialize(suggestion);

        var name = $"Create task: {candidate.Subject}";
        var entity = new Entity("sprk_emailreviewlog")
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeMachine),
            ["sprk_action"] = new OptionSetValue(ReviewActionProposed),
            ["sprk_actor"] = ConsumerTypes.EmailCreateTask,
            ["sprk_targetentity"] = Truncate(entityLogicalName, 100),
            ["sprk_targetrecordid"] = Truncate(recordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(sentinelField, 100),
            ["sprk_confidence"] = (decimal)candidate.Confidence,
            ["sprk_sourceref"] = Truncate(candidate.Citation?.Locator ?? candidate.Citation?.Source ?? string.Empty, 1000),
            ["sprk_aisuggestion"] = suggestionJson,
        };

        var rowId = await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Enrichment[email-create-task] stored Proposed row | CommunicationId: {CommunicationId}, TargetEntity: {Entity}, Subject: {Subject}, DeadlineBearing: {DeadlineBearing}.",
            communicationId, entityLogicalName, candidate.Subject, candidate.DueDate.HasValue);

        return rowId;
    }

    /// <summary>
    /// Writes the resolution <c>Applied</c> row for a non-deadline-bearing candidate that was created
    /// immediately — mirroring Job B's append-only Proposed→terminal-row convention (task 030/031), except
    /// there is no separate human-confirm turn for a non-deadline candidate (it is system auto-finalized, so
    /// both rows carry <c>Machine</c> actor type). The <c>Proposed</c> row itself is NEVER mutated
    /// (append-only).
    /// </summary>
    private async Task WriteCreateTaskAppliedRowAsync(
        Guid communicationId,
        string entityLogicalName,
        Guid recordId,
        string sentinelField,
        TaskCandidate candidate,
        Guid createdTaskId,
        CancellationToken ct)
    {
        var suggestion = new
        {
            kind = "create-task",
            subject = candidate.Subject,
            createdTaskId,
            citation = new
            {
                source = candidate.Citation?.Source,
                locator = candidate.Citation?.Locator,
                quotedText = candidate.Citation?.QuotedText,
            },
            confidence = candidate.Confidence,
        };
        var suggestionJson = JsonSerializer.Serialize(suggestion);

        var name = $"Created task: {candidate.Subject}";
        var entity = new Entity("sprk_emailreviewlog")
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeMachine),
            ["sprk_action"] = new OptionSetValue(ReviewActionApplied),
            ["sprk_actor"] = ConsumerTypes.EmailCreateTask,
            ["sprk_targetentity"] = Truncate(entityLogicalName, 100),
            ["sprk_targetrecordid"] = Truncate(recordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(sentinelField, 100),
            ["sprk_confidence"] = (decimal)candidate.Confidence,
            ["sprk_sourceref"] = Truncate(candidate.Citation?.Locator ?? candidate.Citation?.Source ?? string.Empty, 1000),
            ["sprk_aisuggestion"] = suggestionJson,
        };

        await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Enrichment[email-create-task] created task {TaskId} and stored Applied row | CommunicationId: {CommunicationId}, Entity: {Entity}, Subject: {Subject}.",
            createdTaskId, communicationId, entityLogicalName, candidate.Subject);
    }

    // ── Task 042 (FR-12): regarding-vs-related intent ────────────────────────────
    /// <summary>
    /// Regarding-vs-related intent (FR-12) — the human-readable half. When the envelope PRESENTS a new record
    /// while REFERENCING an existing one ("new litigation matter related to LIT-123456"), this step (a) notes
    /// the cross-reference in <c>sprk_triagesummary</c> and (b) stores a gated "create new record"
    /// <c>Proposed</c> row for human confirmation. The misfile-critical SUPPRESSION (the referenced record does
    /// NOT auto-file) already happened at capture in <see cref="Rungs.IdentifierReverseLookupRung"/> via the
    /// same <see cref="NewRecordIntentDetector"/> — this step writes nothing to the association and creates no
    /// record (there is no create-record write leg; ADR-024 amended 2026-07-30: ONE relationship = regarding,
    /// no related field). Reuses the append-only <c>sprk_emailreviewlog</c> Proposed-row + per-field
    /// idempotency conventions — no new Action, facade, ConsumerType, or create mechanism (§11). Best-effort /
    /// non-fatal (NFR-04): any failure logs + degrades, never throws up the enrichment/capture path.
    /// </summary>
    private async Task RunEmailRegardingIntentAsync(
        Guid communicationId, NormalizedMessage message, CancellationToken ct)
    {
        try
        {
            var intent = NewRecordIntentDetector.Detect(message.Subject, message.BodyText);
            if (intent is null)
            {
                // The overwhelming common case — the email is not presenting a new record. No-op.
                return;
            }

            // Idempotency: scope the open-proposal walk by the proposed-entity marker (a real entity hint when
            // pinned, else the __new_record__ sentinel), then skip if this exact intent already has an open row.
            var targetEntityMarker = intent.ProposedEntityHint ?? RegardingIntentUnpinnedTargetEntity;
            var sentinelField = BuildRegardingIntentSentinelField(intent);
            var openSentinels = await LoadOpenProposalFieldsAsync(communicationId, targetEntityMarker, ct).ConfigureAwait(false);
            if (openSentinels.Contains(sentinelField))
            {
                _logger.LogDebug(
                    "Enrichment[email-regarding-intent] skipped: an open Proposed row already exists for this intent (append-only, idempotent) | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            await StoreRegardingIntentProposedRowAsync(communicationId, targetEntityMarker, sentinelField, intent, ct)
                .ConfigureAwait(false);

            // Note the cross-reference in the triage summary (best-effort; the reference is informational, not
            // a structural link — ADR-024). A read failure or a summary that already carries the note is a
            // no-op, so re-enrichment never duplicates it.
            await AnnotateTriageSummaryAsync(communicationId, intent, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Enrichment[email-regarding-intent] new-record intent proposed | CommunicationId: {CommunicationId}, ProposedType: {Type}, Referenced: [{Referenced}], Trigger: \"{Trigger}\".",
                communicationId, intent.ProposedTypeLabel, string.Join(",", intent.ReferencedIdentifiers), intent.TriggerPhrase);
        }
        catch (Exception ex)
        {
            // NFR-04: intent classification MUST NOT fail capture/enrichment — degrade + log, never throw.
            _logger.LogWarning(
                ex,
                "Enrichment[email-regarding-intent] failed (non-fatal) | CommunicationId: {CommunicationId}.",
                communicationId);
        }
    }

    /// <summary>
    /// Stable idempotency sentinel for a regarding-intent proposal: a hash of the trigger phrase + the sorted
    /// referenced identifiers, so re-enrichment of the same email produces the SAME sentinel (no duplicate
    /// proposal) while two genuinely different new-record framings each get their own lifecycle.
    /// </summary>
    private static string BuildRegardingIntentSentinelField(NewRecordIntent intent)
    {
        var basis = intent.ProposedTypeLabel.Trim().ToLowerInvariant() + "|" +
            string.Join(",", intent.ReferencedIdentifiers.Select(v => v.Trim().ToLowerInvariant()).OrderBy(v => v, StringComparer.Ordinal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return RegardingIntentSentinelFieldPrefix + Convert.ToHexString(hash)[..8];
    }

    /// <summary>
    /// Stores the gated "create new record" proposal as a PENDING <c>sprk_emailreviewlog</c> <c>Proposed</c>
    /// row (the SAME append-only reuse as Job B/C, keyed by <paramref name="sentinelField"/>). NOTHING is
    /// created here (NFR-06 / ADR-015 — there is no create-record write leg; the human confirms in r5); the row
    /// is left OPEN as the reviewer's "create new / file-onto / dismiss" choice payload.
    /// </summary>
    private async Task StoreRegardingIntentProposedRowAsync(
        Guid communicationId,
        string targetEntityMarker,
        string sentinelField,
        NewRecordIntent intent,
        CancellationToken ct)
    {
        var suggestion = new
        {
            kind = "regarding-intent",
            intent = "new-record",
            proposedType = intent.ProposedTypeLabel,
            proposedEntity = intent.ProposedEntityHint, // null = unpinned (generic new record)
            referencedIdentifiers = intent.ReferencedIdentifiers,
            trigger = intent.TriggerPhrase,
            // The reviewer's choices (r5 renders); r1 supplies the data (C-3). Nothing auto-finalizes.
            options = new[] { "create-new-record", "file-onto-referenced", "dismiss" },
            requireConfirm = true, // ADR-015: never auto-finalized
        };
        var suggestionJson = JsonSerializer.Serialize(suggestion);

        var referenced = string.Join(", ", intent.ReferencedIdentifiers);
        var name = $"New {intent.ProposedTypeLabel} proposed (references {referenced})";
        var entity = new Entity("sprk_emailreviewlog")
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeMachine),
            ["sprk_action"] = new OptionSetValue(ReviewActionProposed),
            ["sprk_actor"] = RegardingIntentActor,
            ["sprk_targetentity"] = Truncate(targetEntityMarker, 100),
            ["sprk_targetrecordid"] = string.Empty, // proposing a NEW record — no existing target id
            ["sprk_targetfield"] = Truncate(sentinelField, 100),
            ["sprk_sourceref"] = Truncate($"new-record-intent; references {referenced}; trigger \"{intent.TriggerPhrase}\"", 1000),
            ["sprk_aisuggestion"] = suggestionJson,
        };

        await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Enrichment[email-regarding-intent] stored Proposed row | CommunicationId: {CommunicationId}, ProposedType: {Type}, Referenced: [{Referenced}].",
            communicationId, intent.ProposedTypeLabel, referenced);
    }

    /// <summary>
    /// Appends a human-readable cross-reference note to <c>sprk_triagesummary</c> (ADR-024: the reference is
    /// noted, not persisted as a link). Best-effort: a read failure degrades to no write; an already-annotated
    /// summary is left unchanged so re-enrichment never duplicates the note.
    /// </summary>
    private async Task AnnotateTriageSummaryAsync(Guid communicationId, NewRecordIntent intent, CancellationToken ct)
    {
        const string marker = "[Regarding intent]";

        var record = await _genericEntityService
            .RetrieveAsync("sprk_communication", communicationId, new[] { "sprk_triagesummary" }, ct)
            .ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        var current = record.GetAttributeValue<string>("sprk_triagesummary") ?? string.Empty;
        if (current.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            return; // already annotated — idempotent
        }

        var referenced = string.Join(", ", intent.ReferencedIdentifiers);
        var note =
            $"{marker} Appears to present a NEW {intent.ProposedTypeLabel} while referencing {referenced}. " +
            $"The referenced record was NOT auto-filed; a \"create new record\" proposal is pending review.";
        var updated = string.IsNullOrWhiteSpace(current) ? note : $"{current}\n\n{note}";

        var fields = new Dictionary<string, object> { ["sprk_triagesummary"] = updated };
        await _genericEntityService.UpdateAsync("sprk_communication", communicationId, fields, ct).ConfigureAwait(false);
    }

    // ── Task 041 (FR-13): attachment-grounded action extraction ──────────────────
    /// <summary>
    /// Attachment-grounded action extraction (FR-13). For each attachment whose extracted text a
    /// DETERMINISTIC pre-filter flags as a likely action-trigger (cost gate, NFR-08), grounds the shipped
    /// CREATE-TASK-FROM-EMAIL Action (reused via <see cref="ICommunicationCreateTaskAi"/>) on ONLY that
    /// attachment's text (empty subject/body — so the model can structurally only cite the attachment,
    /// capturing an action stated ONLY in an attachment), then MACHINE-VERIFIES the cited span exists in that
    /// attachment and CODE-DERIVES the page number of the span before feeding the SAME Job C create leg
    /// (<see cref="IActionSeam.CreateTaskAsync"/>) — deadline-bearing candidates are stored PENDING for human
    /// confirm (NFR-06 / ADR-015), never auto-finalized. Best-effort / non-fatal (NFR-04): any failure logs +
    /// degrades, never throws up the enrichment/capture path. Reuses the extraction Action, the create leg,
    /// the append-only audit convention, and the open-proposal idempotency walk — no new Action, facade,
    /// create mechanism, or closed-catalog consumer type (§11).
    /// </summary>
    private async Task RunEmailAttachmentActionAsync(
        Guid communicationId, NormalizedMessage message, CommunicationTriageResult? triageResult, CancellationToken ct)
    {
        try
        {
            var attachments = message.AttachmentTexts;
            if (attachments is not { Count: > 0 })
            {
                _logger.LogDebug(
                    "Enrichment[email-attachment-action] skipped: no per-attachment extracted text (no attachments, or a non-inbound path that does not re-extract) | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            // Cost gate (NFR-08): only attachments whose text carries action-trigger signals get an LLM pass.
            var flagged = attachments.Where(a => AttachmentActionGate.IsLikelyActionTrigger(a.FullText)).ToList();
            _logger.LogInformation(
                "Enrichment[email-attachment-action] cost gate | CommunicationId: {CommunicationId}, Attachments: {Total}, Flagged: {Flagged}, Skipped: {Skipped}.",
                communicationId, attachments.Count, flagged.Count, attachments.Count - flagged.Count);
            if (flagged.Count == 0)
            {
                return;
            }

            var columns = CoreProposeTargets.Select(t => t.RegardingField).ToArray();
            var record = await _genericEntityService.RetrieveAsync("sprk_communication", communicationId, columns, ct)
                .ConfigureAwait(false);
            if (record is null)
            {
                _logger.LogDebug(
                    "Enrichment[email-attachment-action] skipped: communication record could not be retrieved | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            var associations = ResolveAssociatedCoreRecords(record);
            if (associations.Count == 0)
            {
                _logger.LogDebug(
                    "Enrichment[email-attachment-action] skipped: no association to a core record (nothing to attach a task to) | CommunicationId: {CommunicationId}.",
                    communicationId);
                return;
            }

            var (entityLogicalName, recordId) = associations[0];
            var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"] ?? "";
            var openSentinels = await LoadOpenProposalFieldsAsync(communicationId, entityLogicalName, ct).ConfigureAwait(false);
            var createdCount = 0;
            var pendingCount = 0;

            foreach (var attachment in flagged)
            {
                // Ground the extraction on ONLY this attachment's text (subject/body empty) — the model can
                // structurally only cite the attachment, which is exactly FR-13's "action stated only in an
                // attachment" case. The reused Action's citation.source will be "attachment".
                var request = new CommunicationCreateTaskRequest
                {
                    EntityLogicalName = entityLogicalName,
                    Triage = triageResult,
                    Subject = string.Empty,
                    BodyText = string.Empty,
                    AttachmentText = attachment.FullText,
                    TenantId = tenantId,
                };

                var candidates = await _createTaskAi.ExtractAsync(request, ct).ConfigureAwait(false);
                if (candidates is not { Count: > 0 })
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    // Machine-verified locator gate (NFR-06, CODE-DERIVED — stronger than the shipped
                    // merged-blob check): the verbatim cited span MUST be present in THIS attachment's text,
                    // and the page is derived by locating it — never asserted by the model.
                    var (present, page) = AttachmentActionGate.VerifyAgainstAttachment(attachment, candidate.Citation?.QuotedText);
                    if (!present)
                    {
                        _logger.LogDebug(
                            "Enrichment[email-attachment-action] dropped candidate '{Subject}': cited text not located in attachment '{FileName}' (NFR-06) | CommunicationId: {CommunicationId}.",
                            candidate.Subject, attachment.FileName, communicationId);
                        continue;
                    }

                    var sentinelField = BuildAttachmentActionSentinelField(attachment.FileName, candidate.Subject);
                    if (openSentinels.Contains(sentinelField))
                    {
                        _logger.LogDebug(
                            "Enrichment[email-attachment-action] skipped '{Subject}' from '{FileName}': an open Proposed row already exists (append-only, idempotent) | CommunicationId: {CommunicationId}.",
                            candidate.Subject, attachment.FileName, communicationId);
                        continue;
                    }

                    var locator = BuildAttachmentLocator(attachment.FileName, page);
                    await StoreAttachmentActionProposedRowAsync(
                        communicationId, entityLogicalName, recordId, sentinelField, attachment, page, locator, candidate, ct)
                        .ConfigureAwait(false);

                    if (candidate.DueDate.HasValue)
                    {
                        // NFR-06 / ADR-015: deadline-bearing candidates NEVER auto-finalize.
                        pendingCount++;
                        _logger.LogInformation(
                            "Enrichment[email-attachment-action] deadline-bearing candidate '{Subject}' from '{Locator}' stored PENDING for human confirm (DueDate: {DueDate:yyyy-MM-dd}) | CommunicationId: {CommunicationId}.",
                            candidate.Subject, locator, candidate.DueDate, communicationId);
                        continue;
                    }

                    var description = BuildAttachmentActionTaskDescription(candidate, communicationId, locator);
                    var createResult = await _actionSeam.CreateTaskAsync(
                        new CreateTaskRequest
                        {
                            Subject = Truncate(candidate.Subject, 200),
                            Description = description,
                            DueDate = null,
                            RegardingObjectId = recordId,
                            RegardingObjectType = entityLogicalName,
                        },
                        ct).ConfigureAwait(false);

                    if (createResult.Success && createResult.TaskId != Guid.Empty)
                    {
                        await WriteAttachmentActionAppliedRowAsync(
                            communicationId, entityLogicalName, recordId, sentinelField, attachment, page, locator, candidate, createResult.TaskId, ct)
                            .ConfigureAwait(false);
                        createdCount++;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Enrichment[email-attachment-action] create-task did not succeed for '{Subject}' ({Error}); Proposed row left open for retry | CommunicationId: {CommunicationId}.",
                            candidate.Subject, createResult.Error, communicationId);
                    }
                }
            }

            _logger.LogInformation(
                "Enrichment[email-attachment-action] processed {FlaggedCount} flagged attachment(s) for {Entity}: {CreatedCount} created, {PendingCount} pending human confirm | CommunicationId: {CommunicationId}.",
                flagged.Count, entityLogicalName, createdCount, pendingCount, communicationId);
        }
        catch (Exception ex)
        {
            // NFR-04: an attachment-action failure MUST NOT fail capture/enrichment — degrade + log, never throw.
            _logger.LogWarning(
                ex,
                "Enrichment[email-attachment-action] failed (non-fatal) | CommunicationId: {CommunicationId}.",
                communicationId);
        }
    }

    /// <summary>Builds the attachment-action sentinel <c>sprk_targetfield</c> value: the
    /// <see cref="AttachmentActionSentinelFieldPrefix"/> plus a stable hash of (attachment file name +
    /// candidate subject), so distinct actions across attachments each get their own open/closed lifecycle
    /// under <see cref="LoadOpenProposalFieldsAsync"/>.</summary>
    private static string BuildAttachmentActionSentinelField(string fileName, string subject)
    {
        var material = (fileName ?? string.Empty).Trim().ToLowerInvariant() + "|" + (subject ?? string.Empty).Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return AttachmentActionSentinelFieldPrefix + Convert.ToHexString(hash)[..8];
    }

    /// <summary>The machine-verified human-readable locator for a cited attachment span: the file name plus
    /// the derived page (<c>"contract.pdf p.3"</c>) or just the file name when the page is not resolvable.</summary>
    private static string BuildAttachmentLocator(string fileName, int? page) =>
        page.HasValue ? $"{fileName} p.{page.Value}" : fileName;

    /// <summary>Provenance line for a created attachment-action task — cites the source communication + the
    /// machine-verified attachment locator + the verbatim span.</summary>
    private static string BuildAttachmentActionTaskDescription(TaskCandidate candidate, Guid communicationId, string locator)
    {
        var quoted = candidate.Citation?.QuotedText ?? string.Empty;
        var provenance = $"Provenance: source communication {communicationId:D}; attachment {locator}. \"{quoted}\"";
        return string.IsNullOrWhiteSpace(candidate.Description)
            ? provenance
            : $"{candidate.Description}\n\n{provenance}";
    }

    /// <summary>Stores ONE attachment-action candidate as a PENDING <c>Proposed</c> <c>sprk_emailreviewlog</c>
    /// row — same append-only convention as Job B/C, with <c>kind="attachment-action"</c>, the
    /// <see cref="AttachmentActionActor"/> provenance label, and the MACHINE-VERIFIED attachment + page in the
    /// citation.</summary>
    private async Task StoreAttachmentActionProposedRowAsync(
        Guid communicationId,
        string entityLogicalName,
        Guid recordId,
        string sentinelField,
        AttachmentExtractedText attachment,
        int? page,
        string locator,
        TaskCandidate candidate,
        CancellationToken ct)
    {
        var suggestion = new
        {
            kind = "attachment-action",
            subject = candidate.Subject,
            description = candidate.Description,
            dueDate = candidate.DueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            regardingObjectType = entityLogicalName,
            regardingObjectId = recordId,
            citation = new
            {
                source = "attachment",
                attachmentFileName = attachment.FileName,
                attachmentDocumentId = attachment.DocumentId,
                page,
                locator,
                quotedText = candidate.Citation?.QuotedText,
            },
            reason = candidate.Reason,
            confidence = candidate.Confidence,
            requireConfirm = candidate.DueDate.HasValue, // NFR-06/ADR-015: deadline-bearing requires confirm
        };
        var suggestionJson = JsonSerializer.Serialize(suggestion);

        var name = $"Attachment action: {candidate.Subject}";
        var entity = new Entity("sprk_emailreviewlog")
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeMachine),
            ["sprk_action"] = new OptionSetValue(ReviewActionProposed),
            ["sprk_actor"] = AttachmentActionActor,
            ["sprk_targetentity"] = Truncate(entityLogicalName, 100),
            ["sprk_targetrecordid"] = Truncate(recordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(sentinelField, 100),
            ["sprk_confidence"] = (decimal)candidate.Confidence,
            ["sprk_sourceref"] = Truncate(locator, 1000),
            ["sprk_aisuggestion"] = suggestionJson,
        };

        await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Enrichment[email-attachment-action] stored Proposed row | CommunicationId: {CommunicationId}, TargetEntity: {Entity}, Subject: {Subject}, Locator: {Locator}, DeadlineBearing: {DeadlineBearing}.",
            communicationId, entityLogicalName, candidate.Subject, locator, candidate.DueDate.HasValue);
    }

    /// <summary>Writes the terminal <c>Applied</c> row for an attachment-action candidate created immediately
    /// (non-deadline-bearing) — mirrors Job C's append-only convention; the <c>Proposed</c> row is never
    /// mutated.</summary>
    private async Task WriteAttachmentActionAppliedRowAsync(
        Guid communicationId,
        string entityLogicalName,
        Guid recordId,
        string sentinelField,
        AttachmentExtractedText attachment,
        int? page,
        string locator,
        TaskCandidate candidate,
        Guid createdTaskId,
        CancellationToken ct)
    {
        var suggestion = new
        {
            kind = "attachment-action",
            subject = candidate.Subject,
            createdTaskId,
            citation = new
            {
                source = "attachment",
                attachmentFileName = attachment.FileName,
                attachmentDocumentId = attachment.DocumentId,
                page,
                locator,
                quotedText = candidate.Citation?.QuotedText,
            },
            confidence = candidate.Confidence,
        };
        var suggestionJson = JsonSerializer.Serialize(suggestion);

        var name = $"Created attachment-action task: {candidate.Subject}";
        var entity = new Entity("sprk_emailreviewlog")
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeMachine),
            ["sprk_action"] = new OptionSetValue(ReviewActionApplied),
            ["sprk_actor"] = AttachmentActionActor,
            ["sprk_targetentity"] = Truncate(entityLogicalName, 100),
            ["sprk_targetrecordid"] = Truncate(recordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(sentinelField, 100),
            ["sprk_confidence"] = (decimal)candidate.Confidence,
            ["sprk_sourceref"] = Truncate(locator, 1000),
            ["sprk_aisuggestion"] = suggestionJson,
        };

        await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Enrichment[email-attachment-action] created task {TaskId} and stored Applied row | CommunicationId: {CommunicationId}, Entity: {Entity}, Subject: {Subject}, Locator: {Locator}.",
            createdTaskId, communicationId, entityLogicalName, candidate.Subject, locator);
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
    /// <b>FR-04 (email-communication-intelligence-r1 task 024):</b> <see cref="CommunicationAssessedSignal.Confidence"/>
    /// now carries the computed email-specific RI-confidence (<see cref="ComputeRiConfidenceAsync"/>) instead
    /// of the previous hardcoded <c>0</c> — this is what lights up <see cref="CommunicationRuleGate"/> /
    /// <see cref="CommunicationRiActionService"/> for a genuinely urgent, well-associated communication.
    /// </para>
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
        Guid communicationId,
        CommunicationDirection direction,
        NormalizedMessage message,
        CommunicationTriageResult? triageResult,
        CancellationToken ct)
    {
        var confidence = await ComputeRiConfidenceAsync(communicationId, triageResult, ct).ConfigureAwait(false);

        var signal = new CommunicationAssessedSignal(
            communicationId, direction, message.Subject, message.From, message.To.Count, confidence);

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

    // ── FR-04: RI-confidence computation ─────────────────────────────────────────
    /// <summary>
    /// Computes the FR-04 email-specific RI-confidence — <c>urgencyWeight × deterministicAgreement</c>, via
    /// the pure <see cref="RiConfidenceScorer"/> — for the <c>communication_assessed</c> signal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Urgency source (preference order).</b> Prefers <paramref name="triageResult"/>'s
    /// <see cref="CommunicationTriageResult.Priority"/> — the SAME result <see cref="RunEmailTriageAsync"/>
    /// just produced in this call to <see cref="EnrichAsync"/>, hoisted rather than re-derived. When triage
    /// produced no result (Action not routed/disabled, or no persisted classification signal yet — e.g.
    /// outbound messages today), falls back to the classification's own <c>Urgency</c> field, reconstructed
    /// from the SAME persisted <c>sprk_associationprovenance</c> JSON via
    /// <see cref="PersistedClassificationSignalReader"/> (task 023's reuse pattern — no re-derivation, no
    /// second classification call, per ADR-013/NFR-03).
    /// </para>
    /// <para>
    /// <b>Deterministic-rung agreement source.</b> <see cref="Engine.AssociationDecisionTrace.TopDeterministicConfidence"/>
    /// from that SAME provenance document — the reinforced confidence of the strongest deterministic-rung
    /// (0–3) winner that task 021's <see cref="Engine.AssociationStatusMapper"/> already computed and
    /// persisted. No provenance yet (e.g. an outbound message, or an inbound message whose association
    /// hasn't resolved) ⇒ <c>0</c>, which — combined with any urgency weight — keeps the overall score at
    /// <c>0</c>: the same conservative outcome the pre-024 hardcoded value produced for an unassessed
    /// communication (no noise introduced for communications with no association signal at all).
    /// </para>
    /// <para>
    /// <b>Best-effort (NFR-04).</b> ANY failure (the Dataverse read, JSON parse) is caught here and degrades
    /// to confidence <c>0</c> — the same safe/conservative default the hardcoded value used — rather than
    /// throwing into the assessment-emission step. Never propagates to capture/enrichment.
    /// </para>
    /// </remarks>
    private async Task<double> ComputeRiConfidenceAsync(
        Guid communicationId, CommunicationTriageResult? triageResult, CancellationToken ct)
    {
        try
        {
            var record = await _genericEntityService.RetrieveAsync(
                "sprk_communication",
                communicationId,
                ["sprk_associationprovenance"],
                ct);

            var provenanceJson = record?.GetAttributeValue<string>("sprk_associationprovenance");
            var provenance = PersistedClassificationSignalReader.TryDeserializeProvenance(provenanceJson);

            var deterministicAgreement = provenance?.Decision.TopDeterministicConfidence ?? 0.0;

            double urgencyWeight;
            if (!string.IsNullOrWhiteSpace(triageResult?.Priority))
            {
                urgencyWeight = RiConfidenceScorer.UrgencyWeightFromPriority(triageResult!.Priority);
            }
            else
            {
                var classification = PersistedClassificationSignalReader.TryReconstruct(provenance);
                urgencyWeight = RiConfidenceScorer.UrgencyWeightFromClassification(classification?.Urgency);
            }

            var score = RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement);

            _logger.LogDebug(
                "Enrichment[assessment-event] RI-confidence computed | CommunicationId: {CommunicationId}, UrgencyWeight: {UrgencyWeight:F2}, DeterministicAgreement: {DeterministicAgreement:F2}, Score: {Score:F2}",
                communicationId, urgencyWeight, deterministicAgreement, score);

            return score;
        }
        catch (Exception ex)
        {
            // NFR-04: a scoring failure degrades to the safe/conservative default (0 — matches the pre-024
            // hardcoded value) rather than throwing into the assessment-emission step.
            _logger.LogWarning(
                ex,
                "Enrichment[assessment-event] RI-confidence scoring failed (non-fatal); defaulting to 0 | CommunicationId: {CommunicationId}",
                communicationId);
            return 0.0;
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
