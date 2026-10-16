using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
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
        // ebilling-r1 task 028: the two eBilling container ids are format-checked only ([RegularExpression], b!…) —
        // empty passes, so no stamp fails to boot and IOptionsDriftTests demands no key.
        // The two container ids must also be distinct from each other and from the email/archive containers
        // (FinanceOptionsValidator; fails start-up naming both keys).
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

        // Invoice lifecycle (ebilling-r1 task 014, FR-06, I-6, ADR-002): the single server-side owner of sprk_invoice
        // statecode / statuscode. Scoped, unconditional (write-path owner; see the flag rule above). Callers: 103, 104, 150, 151.
        services.AddScoped<IInvoiceLifecycleService, InvoiceLifecycleService>();

        // Rate card and timekeeper import / publish (ebilling-r1 task 078, FR-29 / FR-08): template, all-or-nothing import into a Draft card,
        // overlap re-check and publish. Concrete (ADR-010: no interface, no second implementation). Scoped: holds the scoped Dataverse
        // clients and the SPE facade. Unconditional (flag rule above); the routes carry the kill switch.
        services.AddScoped<RateCardImportService>();

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
        // Scoped: confirms documents as invoices, creates sprk_invoice records,
        // enqueues InvoiceExtraction jobs via JobSubmissionService
        // Uses IDataverseService for record operations
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

        return services;
    }
}
