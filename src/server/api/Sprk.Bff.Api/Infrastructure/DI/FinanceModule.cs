using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Finance.Tools;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.DI;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        // ============================================================================
        // Finance Intelligence Module Configuration (ADR-013: Extend BFF)
        // ============================================================================
        // Bind FinanceOptions from "Finance" configuration section
        // Provides: ClassificationConfidenceThreshold, BudgetWarningPercentage,
        //           VelocitySpikePct, FinanceSummaryCacheTtlMinutes,
        //           ExtractionDeploymentName
        // task 061 fail-fast sweep: canonical AddOptions chain with ValidateOnStart. No member is required (all have
        // safe defaults; InvoiceExtractionPlaybookId is intentionally empty in lower envs, so it is NOT [Required]).
        // ebilling-r1 task 028: the eBilling container id (Procedure Library) is format-checked only ([RegularExpression], b!…) —
        // empty passes, so no stamp fails to boot and IOptionsDriftTests demands no key. It must also be distinct from the
        // email/archive containers (FinanceOptionsValidator; fails start-up naming both keys). Task 092 retired the second key
        // (Finance:EbillingOutputContainerId): export files are Stored Files in Finance:FileStore.
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<FinanceOptions>, FinanceOptionsValidator>();
        services.AddOptions<FinanceOptions>()
            .Bind(configuration.GetSection(FinanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ============================================================================
        // eBilling flag (ebilling-r1 task 040, FR-45, ADR-018 rule 2, ADR-032)
        // ============================================================================
        // ONE flag for the whole capability: Ebilling:Enabled (default false). No per-service flags.
        // FLAG-OFF REGISTRATION RULE (every later eBilling task follows this):
        //   * eBilling services register UNCONDITIONALLY - no `if (ebillingEnabled)` registration gates.
        //   * The kill switch is the endpoint filter (chain RequireEbillingEnabled() AFTER each eBilling
        //     route's authorization filter; it returns 503 `ebilling.disabled`) plus the job-handler check
        //     (an eBilling IJobHandler reads EbillingOptions.Enabled itself - ADR-018 "same flag in
        //     endpoints and async handlers").
        //   * A Null-Object peer (ADR-032) is needed ONLY where an eBilling service depends on something
        //     that is itself conditionally registered - e.g. the AI facade IInvoiceAi, which already has
        //     NullInvoiceAi. Do not add a Null peer for a service just because eBilling is off.
        //   * The flag never bypasses or replaces authorization.
        services.AddOptions<EbillingOptions>()
            .Bind(configuration.GetSection(EbillingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // eBilling settings (ebilling-r1 task 025, FR-48, D-59): the one audited sprk_ebillingsetting row, read with a
        // 30 s IMemoryCache copy (ADR-009 exception documented on the class). Singleton: it takes a scope per load for the
        // scoped IGenericEntityService, so background jobs and requests share one cache. Unconditional (see the flag rule above).
        services.AddSingleton<IEbillingSettingsReader, EbillingSettingsReader>();

        // Invoice totals (ebilling-r1 task 016, FR-07, I-1, D-42): the one server-side owner of an invoice's five totals and its
        // Adjustment-line amounts. Scoped: stateless, holds only the (singleton) Dataverse clients. Unconditional (flag rule above).
        services.AddScoped<IInvoiceTotalsService, InvoiceTotalsService>();

        // Invoice resolution (ebilling-r1 task 046, FR-23, D-47, D-69): Processing Pipeline step 2 - arithmetic checks, timekeeper
        // match against the firm roster (exact, normalized, AI-assisted through IInvoiceAi.GenerateEmbeddingAsync only), rate
        // lookup in the Published rate card, and the combined confidence per field. Scoped: IInvoiceAi and IGenericEntityService
        // are scoped, and TimekeeperMatcher keeps its embedding memo per call, not per instance. Unconditional (flag rule above):
        // IInvoiceAi already has its Null peer, so the AI-assisted name step degrades to "unmatched" when AI is off.
        // Caller: task 049 (pipeline orchestration). IInvoiceResolutionService is the seam 049's tests fake.
        services.AddScoped<TimekeeperMatcher>();
        services.AddScoped<IInvoiceResolutionService, InvoiceResolutionService>();

        // Billing compliance evaluation (ebilling-r1 task 072, FR-25, D-23 in-memory strategy): BillingFactLoader reads the published
        // billing rules for the client default and the firm, the core record, the firm's priors and their secure roots (D-112) once
        // per submission; InvoiceComplianceEvaluator selects the versions in force per line (firm over client default, D-39), gates
        // them (FR-24) and runs the 071 handlers. Writes nothing. Concrete (ADR-010: no interface, one implementation each). Scoped:
        // the loader holds the scoped SecureChildShareSynchronizer (ExternalAccessModule, unconditional); the gate PolicyVersionValidator
        // is a singleton (SignalsModule). Unconditional (flag rule above). Caller: task 049 (pipeline orchestration).
        services.AddScoped<Sprk.Bff.Api.Services.Signals.Billing.BillingFactLoader>();
        services.AddScoped<Sprk.Bff.Api.Services.Signals.Billing.InvoiceComplianceEvaluator>();

        // Submission rejection rule (ebilling-r1 task 073, FR-26, I-5, NFR-06; Processing Pipeline step 5): decides Accepted / Rejected from
        // the evaluator's findings (consequence Reject, every read field at or above the threshold, reject-eligible type). Concrete (ADR-010:
        // no interface, one implementation). Singleton: its only dependency is the singleton settings reader and it holds no state; the
        // threshold is read on every decision, so a change applies at once. Unconditional (flag rule above). Caller: task 049.
        services.AddSingleton<SubmissionRejectionRule>();

        // Invoice lifecycle (ebilling-r1 task 014, FR-06, I-6, ADR-002): the single server-side owner of sprk_invoice
        // statecode / statuscode. Scoped, unconditional (write-path owner; see the flag rule above). Callers: 103, 104, 150, 151.
        services.AddScoped<IInvoiceLifecycleService, InvoiceLifecycleService>();

        // Notification seam of the submission pipeline (ebilling-r1 task 049 part A, Processing Pipeline step 9): the processor reports every
        // Accepted / Rejected submission here after its decision commits. The no-op is the default; task 132 replaces THIS registration with its
        // job-enqueuing sink, so the interface is a real seam (a second implementation is planned), not a 1:1 convenience (ADR-010).
        // Scoped like its callers (the pipeline's scoped processor). Unconditional (flag rule above): the processor always calls it.
        services.AddScoped<IEbillingNotificationSink, NoOpEbillingNotificationSink>();

        // Rate card and timekeeper import / publish (ebilling-r1 task 078, FR-29 / FR-08): template, all-or-nothing import into a Draft card,
        // overlap re-check and publish. Concrete (ADR-010: no interface, no second implementation). Scoped: holds the scoped Dataverse
        // clients and the SPE facade. Unconditional (flag rule above); the routes carry the kill switch.
        services.AddScoped<RateCardImportService>();
        // Billing rule sets (ebilling-r1 task 076, FR-28): create from a source document, review proposals, edit / accept / reject, publish
        // (PolicyVersionValidator, all or nothing, immutable versions), assign, visibility, and the one firm-facing clause-visibility decision.
        // Concrete (ADR-010: no interface, no second implementation). Scoped: holds the scoped SPE facade and ownership resolver; it uses the
        // singleton PolicyVersionValidator and RuleBodyDescriber (SignalsModule). Unconditional (flag rule above); the routes carry the kill switch.
        services.AddScoped<RuleSetService>();
        // Procedures and Procedure Versions (ebilling-r1 task 081, EB-D-148): create, version (copy-forward), publish in one transaction,
        // retire, and the caller-scoped retire preview. Concrete (ADR-010: no interface, no second implementation). Scoped: it holds the
        // scoped RuleSetService and ownership resolver; it uses the singleton SignalClosureService (SignalsModule) for the one selection and
        // close. AffectedInvoiceCounter is the one caller-scoped "open invoices you can see" count (081 and 089 share it); scoped over the
        // per-request CallerRecordAccessProbe (ExternalAccessModule). Unconditional (flag rule above); the routes carry the kill switch.
        services.AddScoped<AffectedInvoiceCounter>();
        services.AddScoped<ProcedureService>();

        // Rule drafting job (ebilling-r1 task 075, FR-27): ADR-004 IJobHandler, the same shape as the other Finance handlers. Reads the rule
        // set's source document, asks IInvoiceAi.DraftRulesAsync (which has its Null peer, so AI-off is a clear Poisoned outcome, not a
        // missing registration), and stores validated proposals through RuleSetService. §11: no new interface (ADR-010).
        services.AddScoped<IJobHandler, BillingRuleDraftJobHandler>();
        // Invoice Review context panel (ebilling-r1 task 106, FR-33): read-only, deterministic assembly of five independent sections for
        // one invoice (core record, budget rollups, other invoices, disallow history, rate history). Concrete (ADR-010: no interface, no second
        // implementation). Scoped: per-request, holds the (singleton) generic Dataverse client. Unconditional (flag rule above); the route carries the kill switch.
        services.AddScoped<InvoiceContextService>();
        // Invoice Review related work product (ebilling-r1 task 107, FR-34): candidate documents of one fee line from Dataverse (core record,
        // author, window), each checked for Read as the caller, ranked by ISemanticSearchService in documentIds scope. Concrete (ADR-010).
        // Scoped (holds the scoped AuthorizationService and search). Unconditional (flag rule above); ISemanticSearchService resolves to its
        // Null-Object (AnalysisServicesModule, ADR-032 P3) when AI is off, so the answer is "unavailable", never a silent empty.
        services.AddScoped<InvoiceRelatedWorkService>();
        // Invoice Review read model and line decisions (ebilling-r1 task 100, FR-31, D-64, D-75). Two concrete services (ADR-010: no interface, one
        // implementation each). Scoped: per request, they hold the scoped AuthorizationService (read) and the singleton generic Dataverse client, 016 totals,
        // 074 recorder and processing lock (decisions). Unconditional (flag rule above); the routes carry the kill switch. The read model also takes
        // the caller's IDataverseUserClient and EarlierInvoiceReadability (SignalsModule, unconditional) for the D-176 Duplicate check (task 119).
        services.AddScoped<InvoiceReviewReadService>();
        services.AddScoped<InvoiceLineDecisionService>();
        // Invoice-level adjustments (ebilling-r1 task 102, FR-32, D-74): add / remove an Adjustment line and the rule list; 016 owns the amounts and totals.
        services.AddScoped<InvoiceAdjustmentService>();
        // Value and code corrections (ebilling-r1 task 101 part B1, FR-31, FR-22, D-64): the D-64 correction rows and the corrected value. Concrete (ADR-010).
        // Scoped: it holds the scoped CodeSetResolver and the scoped EligibleApproverResolver. Unconditional (flag rule above); the routes carry the kill switch.
        services.AddScoped<InvoiceCorrectionService>();
        // B2b-1: the follow-through of a saved correction (re-evaluation diff, findings patch, line status), the service's onValueWritten seam. Concrete
        // (ADR-010), scoped: it holds the scoped BillingFactLoader and InvoiceComplianceEvaluator. Unconditional (flag rule above).
        services.AddScoped<InvoiceCorrectionReevaluation>();
        // UTBMS line coding (ebilling-r1 task 047, FR-22, D-46): resolves an invoice's code set (CodeSetResolver), codes its
        // lines through IInvoiceAi.CodeLinesAsync in chunks, and refuses any code outside the set (ILineCodingService).
        // Scoped (they hold the scoped IInvoiceAi and the scoped suggestion provider; IGenericEntityService is a singleton);
        // unconditional (flag rule above - IInvoiceAi is always registered, as NullInvoiceAi when AI is off).
        // ICodeSetSuggestionProvider is the FR-22 step 4 seam. Task 048: CodeSetProposalService is the AI provider (it replaces
        // NoCodeSetSuggestionProvider here) and also serves the code-set-mapping routes (proposals, confirm). One scoped instance plays
        // both roles, so its per-scope code-set embeddings are loaded once. Unconditional (flag rule above): IInvoiceAi has its Null
        // peer, which throws the feature-disabled exception rather than guessing.
        services.AddScoped<CodeSetProposalService>();
        services.AddScoped<ICodeSetSuggestionProvider>(sp => sp.GetRequiredService<CodeSetProposalService>());
        services.AddScoped<CodeSetResolver>();
        services.AddScoped<ILineCodingService, LineCodingService>();
        // Tracking numbers (ebilling-r1 task 045, FR-10, D-37, D-87, ADR-002): the single server-side owner of a submission's
        // {firm invoice number}.{n} and sprk_submissionsequence, and the creator of the submission row that carries them; made unique
        // by the (firm, tracking number) alternate key with a bounded retry. Scoped (takes the scoped IGenericEntityService),
        // unconditional (flag rule above). Caller: 044 (a resubmission, D-37, is a new submission through the same route).
        services.AddScoped<ITrackingNumberAllocator, TrackingNumberAllocator>();
        // Invoice submission (ebilling-r1 task 044, FR-17): the one intake both the SPA and the internal route call (validate, store the
        // PDF, create the document and the submission through the allocator, enqueue processing). Concrete (ADR-010: one implementation).
        // Scoped like its scoped dependencies (SpeFileStore, RecordContainerResolver, the allocator); unconditional (flag rule above).
        services.AddScoped<InvoiceSubmissionService>();
        // 050a review round 3 (uac-r2 F1-a / F1-b): the isolation rule of a confirmed existing document, decided at confirm (the service above)
        // and again at commit (the persister below), over uac-r2's SecureChildShareSynchronizer.SecureRootsAboveAsync. Concrete (ADR-010: one
        // implementation). Scoped like the synchronizer it composes (ExternalAccessModule); every dependency is registered unconditionally.
        services.AddScoped<ExistingDocumentIsolationCheck>();
        // Submit Invoice from an email (ebilling-r1 task 095, model §9.1): the from-email route's orchestration (marks, firm, replay, the
        // in-process Office save, the .eml's resolution), scoped like its scoped dependencies (the Office save, InvoiceSubmissionService).
        // The seam onto Submit Invoice is task 094's operation (it replaced 095's not-yet-available stand-in): step 1, the intake's checks,
        // step A, the marked parts read out of the .eml and staged with their source, the submission and its job. Scoped like its scoped
        // dependencies (InvoiceSubmissionService, the allocator, StoredFileService). The .eml's byte read (EmlMarkedPartReader, which the
        // operation calls at staging) is scoped like IGenericEntityService and SpeFileStore. All unconditional (flag rule above).
        services.AddScoped<FromEmailSubmissionService>();
        services.AddScoped<ISubmitInvoiceFromEmail, SubmitInvoiceFromEmailOperation>();
        services.AddScoped<EmlMarkedPartReader>();
        // Billing Decision Records (ebilling-r1 task 074, FR-30, D-75, D-100, D-101): the one Finance-side owner of a billing decision's record,
        // over the writer's additive WriteBillingDecisionAsync. Concrete (ADR-010: no interface, one implementation). Singleton: it holds no
        // state and its dependencies are singletons (DecisionRecordWriter, SignalsModule; IIdentityNormalizationService, MembershipModule, for
        // the deciding user's contact, K3). Unconditional (flag rule above).
        // Callers: 049, 100, 103, 104, 105.
        services.AddSingleton<BillingDecisionRecorder>();

        // Override / Return of a blocked internal submission (ebilling-r1 task 105, FR-19, FR-37, D-24, D-73, D-76). Concrete (ADR-010: no
        // interface, one implementation). Scoped like its scoped dependencies (IGenericEntityService, IInvoiceLifecycleService). The privilege
        // check is a static class over the existing CallerRecordAccessProbe, so it adds no registration. Unconditional (flag rule above). The
        // blocked view also takes the caller's IDataverseUserClient and EarlierInvoiceReadability (SignalsModule) for the D-176 Duplicate check
        // (task 119 round 2, DuplicateDetailGate, shared with InvoiceReviewReadService).
        services.AddScoped<SubmissionOverrideService>();

        // Approval (ebilling-r1 task 103, FR-35, FR-11, D-40, D-72, D-78): the eligible-approver resolver (live at approve, recheck on GET,
        // 049's acceptance step, the refresh job) and the approve service. Concrete (ADR-010). Scoped like their scoped dependencies
        // (InvoiceLineDecisionService, IInvoiceLifecycleService). Unconditional (flag rule above); the routes and the job read the kill switch.
        // The refresh is an ADR-036 scheduled job (no BackgroundService), every 15 minutes.
        services.AddScoped<EligibleApproverResolver>();
        services.AddScoped<InvoiceApprovalService>();
        services.AddScheduledJob<EligibleApproverRefreshJob>(EligibleApproverRefreshJob.DefaultCronSchedule);

        // Invoice submission processing (ebilling-r1 task 049 parts B-E, FR-20 / FR-21, D-101; Processing Pipeline steps 1-9). The pipeline is
        // three scoped components behind the seams the handler tests fake, plus the ADR-004 job handler that composes them:
        //   * IInvoiceSubmissionProcessor  - read (043), resolve (046), code (047), evaluate (072), decide (073); writes nothing.
        //   * IInvoiceSubmissionPersister  - the one all-or-nothing transaction (invoice, its Stored File row, lines, approval, the source
        //     guard, submission link), then the commit gate.
        //   * IInvoiceSubmissionAfterSteps - Signals, the automatic rejection's Decision Record, notification, terminal status last.
        //   * InvoiceSubmissionProcessingJobHandler - payload, the eBilling flag (read by the handler itself), the per-submission lock, the
        //     step-0 re-read, the document download, and the mapping of every result to a JobOutcome.
        // Scoped like their scoped dependencies (IGenericEntityService, IInvoiceAi, the evaluator, the lifecycle and totals services). Every
        // dependency is registered unconditionally (the flag rule above; IInvoiceAi has its Null peer, so AI-off is a Failed submission with
        // ai.invoice.disabled, not a missing registration). The queue consumer is the handler's job type, InvoiceSubmissionService.JobType.
        services.AddScoped<IInvoiceSubmissionProcessor, InvoiceSubmissionProcessor>();
        services.AddScoped<IInvoiceSubmissionPersister, InvoiceSubmissionPersister>();
        services.AddScoped<IInvoiceSubmissionAfterSteps, InvoiceSubmissionAfterSteps>();
        //   * SubmissionCommitGate (ebilling-r1 task 094; procedure-and-file-model 9.1 steps 5-6) - the post-commit gate of every channel,
        //     shared by the persister (after its commit), the after-steps' resume (first) and the handler (a held undo): the source re-run,
        //     the flip of every file, and the undo. Concrete (ADR-010: one implementation). Scoped like the isolation check and the share
        //     synchronizer it composes. It replaces 050a's InvoiceDocumentRefile (retired: a Submit Invoice source is copied, never re-filed).
        services.AddScoped<SubmissionCommitGate>();
        services.AddScoped<IJobHandler, InvoiceSubmissionProcessingJobHandler>();

        // ============================================================================
        // Invoice Analysis Service (ADR-013: AI via BFF, ADR-014: Playbook prompts)
        // ============================================================================
        // Scoped: loads the extraction playbook prompt per-request, calls OpenAI structured output
        // (classification no longer goes through it: task 042 moved it to IInvoiceAi.ClassifyAsync / INVOICE-CLASSIFY@v1)
        // Uses IPlaybookService for prompt loading (FinanceExtraction)
        // Uses IOpenAiClient.GetStructuredCompletionAsync for constrained JSON output
        //
        // Depends on IOpenAiClient which is only registered when DocumentIntelligence:Enabled=true
        // (see AnalysisServicesModule). Register conditionally so DI does not fail when AI is disabled.
        var documentIntelligenceEnabled = configuration.GetValue<bool>("DocumentIntelligence:Enabled");
        if (documentIntelligenceEnabled)
        {
            services.AddScoped<IInvoiceAnalysisService, InvoiceAnalysisService>();
        }

        // ============================================================================
        // Signal Evaluation Service (threshold-based signal detection, no AI)
        // ============================================================================
        // Scoped: evaluates spend snapshots against configurable threshold rules
        // Rules: BudgetExceeded (100%), BudgetWarning (configurable), VelocitySpike (configurable)
        // Upserts sprk_spendsignal records via IDataverseService
        services.AddScoped<ISignalEvaluationService, SignalEvaluationService>();

        // ============================================================================
        // Invoice Review Service (human-in-the-loop confirmation workflow)
        // ============================================================================
        // Scoped: confirms a classified document as an invoice by creating its Internal submission through InvoiceSubmissionService
        // (050a, D-134: no confirm path creates an invoice; the submission job creates it), and rejects documents. Scoped like
        // InvoiceSubmissionService.
        services.AddScoped<IInvoiceReviewService, InvoiceReviewService>();

        // ============================================================================
        // Invoice Search Service (semantic search for invoices)
        // ============================================================================
        // Scoped: performs semantic search across invoices using Azure AI Search
        // Generates embeddings via IOpenAiClient (text-embedding-3-large)
        // Hybrid search: vector + semantic reranking for best results
        // Uses SearchIndexClient for accessing spaarke-invoices-index
        //
        // Depends on IOpenAiClient + SearchIndexClient — both gated on DocumentIntelligence:Enabled.
        if (documentIntelligenceEnabled)
        {
            services.AddScoped<IInvoiceSearchService, InvoiceSearchService>();
        }
        else
        {
            // L2 — NullInvoiceSearchService (P3 Fail-Fast). Task 011 Phase 1b Tier 2, D-09 §2 L2.
            // FinanceEndpoints.SearchInvoices consumes IInvoiceSearchService unconditionally;
            // registering Null-Object here keeps DI param-inference green when
            // DocumentIntelligence:Enabled=false. Endpoint catch converts to 503 ProblemDetails.
            services.AddScoped<IInvoiceSearchService, NullInvoiceSearchService>();
        }

        // ============================================================================
        // Spend Snapshot Service (deterministic financial aggregation, no AI)
        // ============================================================================
        // Scoped: aggregates BillingEvents into SpendSnapshots per matter
        // Computes: Monthly + ToDate periods, budget variance, MoM velocity
        // Upserts sprk_spendsnapshot records via 5-field alternate key
        services.AddScoped<ISpendSnapshotService, SpendSnapshotService>();

        // ============================================================================
        // Finance Summary Service (aggregates snapshots, signals, and invoices with caching)
        // ============================================================================
        // Scoped: composes financial summary for a matter from multiple sources
        // Sources: latest ToDate snapshot, active signals (last 30 days), recent invoices (last 5)
        // Caching: Redis-backed with TTL from FinanceOptions (default: 5 minutes)
        // Invalidation: explicit via InvalidateSummaryAsync after snapshot updates
        services.AddScoped<IFinanceSummaryService, FinanceSummaryService>();

        // ============================================================================
        // Playbook Lookup + Output Orchestrator — MOVED to AnalysisServicesModule
        // ============================================================================
        // ai-architecture-redesign-r1 task 006 (FR-P0-05, 2026-07-05): IPlaybookLookupService
        // and IOutputOrchestratorService are AI playbook services that landed here during
        // earlier Finance accretion. Real registrations now live in
        // AnalysisServicesModule.AddPlaybookServices (compound Analysis:Enabled &&
        // DocumentIntelligence:Enabled gate); ADR-032 P3 Null-Object peers are registered in
        // AnalysisServicesModule.AddNullObjectsForCompoundOff so unconditional consumers
        // (ChatEndpoints, WorkspaceFileEndpoints, Workspace prefill services,
        // InvoiceExtractionJobHandler) keep resolving and surface the 503 kill-switch
        // pattern when the compound AI gate is OFF.

        // ============================================================================
        // Dataverse Update Handler (low-level update operations with concurrency control)
        // ============================================================================
        // Scoped: handles Dataverse entity updates with optimistic concurrency and retry logic
        // NOT a tool handler - called by OutputOrchestrator for record updates
        // Features: row version checking, exponential backoff, concurrency conflict detection
        // Used by playbooks to update invoice and matter records
        services.AddScoped<IDataverseUpdateHandler, DataverseUpdateHandler>();

        // ============================================================================
        // Telemetry: Finance metrics and distributed tracing (OpenTelemetry-compatible)
        // ============================================================================
        // Singleton: stateless, tracks metrics across all requests
        // Meter name: "Sprk.Bff.Api.Finance" for OpenTelemetry configuration
        services.AddSingleton<FinanceTelemetry>();

        // ============================================================================
        // AI Tool Handlers (IAiToolHandler) - called by playbooks for workflow orchestration
        // ============================================================================
        // Tool handlers execute specific actions during playbook-driven workflows
        // Registered as IAiToolHandler for playbook tool discovery and execution

        // FinancialCalculationToolHandler - calculates and updates matter/project financial totals
        // Operations: "recalculate" (from all invoices) or "increment" (add single invoice)
        // Uses optimistic concurrency with row version checks and exponential backoff retry
        // Dual registration: concrete type needed by InvoiceExtractionJobHandler (direct injection),
        // interface forwarding needed by playbook tool discovery (GetServices<IAiToolHandler>).
        services.AddScoped<FinancialCalculationToolHandler>();
        services.AddScoped<IAiToolHandler>(sp => sp.GetRequiredService<FinancialCalculationToolHandler>());

        // ============================================================================
        // Attachment Classification Job Handler (ADR-013: AI via BFF, ADR-015: no content logging)
        // ============================================================================
        // Scoped: classifies email attachments as invoice candidates through IInvoiceAi.ClassifyAsync
        // (Action INVOICE-CLASSIFY@v1 via its Binding; ebilling-r1 task 042). IInvoiceAi is registered
        // unconditionally (real or Null facade), so the handler no longer depends on DocumentIntelligence:Enabled for it.
        // IdempotencyKey: classify-{documentId}-attachment
        // Writes sprk_classification, sprk_classificationconfidence, sprk_invoicehintsjson
        // Sets sprk_invoicereviewstatus=ToReview for InvoiceCandidate and Unknown
        //
        // Depends on IRecordMatchService which is only registered when
        // DocumentIntelligence:RecordMatchingEnabled=true (see AnalysisServicesModule.AddRecordMatchingServices).
        // Register conditionally so IJobHandler enumeration does not throw when record matching is disabled.
        if (configuration.GetValue<bool>("DocumentIntelligence:RecordMatchingEnabled"))
        {
            services.AddScoped<IJobHandler, AttachmentClassificationJobHandler>();
        }

        // ============================================================================
        // Invoice Extraction Job Handler (ADR-013: AI via BFF, ADR-015: no content logging)
        // ============================================================================
        // Scoped: extracts invoice facts via AI (Playbook B), creates BillingEvent records
        // IdempotencyKey: extract-{invoiceId}
        // VisibilityState ALWAYS "Invoiced" (deterministic, never from LLM)
        // Enqueues InvoiceIndexing on success (InvoiceExtractionJobHandler.cs:344).
        // NOTE (verified 2026-08-20): it does NOT enqueue SpendSnapshotGeneration — no code
        // anywhere submits that job type. The snapshots → signals → rollup analytics chain has
        // no automatic trigger; FinanceRollupService runs ONLY via the manual
        // POST /api/finance/{matters|projects}/{id}/recalculate endpoints. Whether the missing
        // enqueue is a wiring gap or intended is an owner decision — do not wire it without
        // validating the side effects (same class as the TodoGenerationService trap in
        // docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md).
        services.AddScoped<IJobHandler, InvoiceExtractionJobHandler>();

        // ============================================================================
        // Invoice Indexing Job Handler (indexes invoices into Azure AI Search)
        // ============================================================================
        // Scoped: generates embeddings and indexes invoice documents for semantic search
        // IdempotencyKey: invoice-index-{invoiceId}
        // Index: spaarke-invoices-index (MVP single index; per-tenant fan-out is a future extension)
        // Embeddings: text-embedding-3-large (3072 dimensions)
        //
        // Depends on IOpenAiClient + SearchIndexClient — both gated on DocumentIntelligence:Enabled.
        if (documentIntelligenceEnabled)
        {
            services.AddScoped<IJobHandler, InvoiceIndexingJobHandler>();
        }

        // ============================================================================
        // Spend Snapshot Generation Job Handler (deterministic aggregation, no AI)
        // ============================================================================
        // Scoped: aggregates BillingEvents into SpendSnapshots, then evaluates signals
        // Calls: SpendSnapshotService (create/update snapshots) + SignalEvaluationService (threshold detection)
        // IdempotencyKey: snapshots-{matterId}
        // Idempotent: Services use upsert via alternate keys (SpendSnapshot) and deterministic IDs (SpendSignal)
        // No downstream job enqueues - end of analytics chain
        services.AddScoped<IJobHandler, SpendSnapshotGenerationJobHandler>();

        // ============================================================================
        // Finance Rollup Service (denormalized field write-back to Matter/Project)
        // ============================================================================
        // Scoped: recalculates 9 financial fields on parent Matter/Project records
        // Called by: FinanceRollupEndpoints (subgrid parent rollup web resource)
        //            SpendSnapshotGenerationJobHandler (after background invoice processing)
        // Fields: TotalSpendToDate, InvoiceCount, MonthlySpendCurrent, TotalBudget,
        //         RemainingBudget, BudgetUtilizationPercent, MonthOverMonthVelocity,
        //         AverageInvoiceAmount, MonthlySpendTimeline
        services.AddScoped<FinanceRollupService>();

        // ============================================================================
        // Finance file store (ebilling-r1 task 090; procedure-and-file-model section 4; EB-D-148)
        // ============================================================================
        // Placement (ADR-052 B2, B3): in the BFF - request-scoped authorization over the caller's token, short app-only writes and
        // an hourly sweep; no new process, no new package (Azure.Storage.Blobs is already referenced by the session store).
        //   * StoredFileBlobs - singleton: one managed-identity BlobGateway (Infrastructure/Storage, shared with SessionFileBlobStore)
        //     over the shared TokenCredential. An empty Finance:FileStore:BlobEndpoint is a runtime state ("not configured"), not
        //     a DI state, so the registration is unconditional (ADR-032 P1; the flag rule above).
        //   * StoredFileService - scoped: per request, like its CallerRecordAccessProbe (a transient typed HttpClient) and request
        //     IServiceProvider; IGenericEntityService is a singleton (GraphModule). Concrete (ADR-010: one implementation).
        //   * StoredFileReconcileJob - the ONE scheduled job of the store (ADR-036 A1 rule 6: AddScheduledJob, no BackgroundService);
        //     it reads Ebilling:Enabled itself (ADR-018).
        services.AddSingleton<StoredFileBlobs>();
        services.AddScoped<StoredFileService>();
        services.AddScheduledJob<StoredFileReconcileJob>(StoredFileReconcileJob.DefaultCronSchedule);

        return services;
    }
}
