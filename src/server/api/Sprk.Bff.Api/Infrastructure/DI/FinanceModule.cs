using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
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
        //           ClassificationDeploymentName, ExtractionDeploymentName
        services.Configure<FinanceOptions>(configuration.GetSection(FinanceOptions.SectionName));

        // ============================================================================
        // Invoice Analysis Service (ADR-013: AI via BFF, ADR-014: Playbook prompts)
        // ============================================================================
        // Scoped: loads playbook prompts per-request, calls OpenAI structured output
        // Uses IPlaybookService for prompt loading (FinanceClassification, FinanceExtraction)
        // Uses IOpenAiClient.GetStructuredCompletionAsync for constrained JSON output
        services.AddScoped<IInvoiceAnalysisService, InvoiceAnalysisService>();

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
        // Uses SearchIndexClient for accessing spaarke-invoices-dev index
        services.AddScoped<IInvoiceSearchService, InvoiceSearchService>();

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
        // Output Orchestrator Service (playbook-driven Dataverse updates)
        // ============================================================================
        // Scoped: reads outputMapping from playbooks and applies field updates to Dataverse
        // Enables business analysts to configure field mappings via Playbook Builder without code deployment
        // Variable resolution: ${context.invoiceId}, ${extraction.aiSummary}, etc.
        // Type conversions: Money, EntityReference, DateTime
        // Delegates to DataverseUpdateHandler for optimistic concurrency and retry logic
        services.AddScoped<IOutputOrchestratorService, OutputOrchestratorService>();

        // ============================================================================
        // Playbook Lookup Service (cached alternate key lookups for SaaS portability)
        // ============================================================================
        // Scoped: retrieves playbooks by portable code (e.g., "PB-013") instead of environment-specific GUIDs
        // Enables multi-environment deployments (DEV/QA/PROD) without hardcoded GUIDs or config changes
        // Caching: IMemoryCache with 1-hour TTL to minimize Dataverse queries (critical for high-volume scenarios)
        // Uses RetrieveByAlternateKeyAsync for indexed, fast lookups via sprk_playbookcode alternate key
        // Example: GetByCodeAsync("PB-013") returns same playbook across all environments (different GUIDs)
        services.AddScoped<IPlaybookLookupService, PlaybookLookupService>();

        // ============================================================================
        // Action Lookup Service (cached alternate key lookups for SaaS portability)
        // ============================================================================
        // Scoped: retrieves AI actions by portable code (e.g., "ACT-001") instead of environment-specific GUIDs
        // Enables multi-environment deployments (DEV/QA/PROD) without hardcoded action GUIDs
        // Caching: IMemoryCache with 1-hour TTL to minimize Dataverse queries
        // Uses RetrieveByAlternateKeyAsync for indexed, fast lookups via sprk_actioncode alternate key
        services.AddScoped<IActionLookupService, ActionLookupService>();

        // ============================================================================
        // Skill Lookup Service (cached alternate key lookups for SaaS portability)
        // ============================================================================
        // Scoped: retrieves AI skills by portable code (e.g., "SKILL-001") instead of environment-specific GUIDs
        // Enables multi-environment deployments (DEV/QA/PROD) without hardcoded skill GUIDs
        // Caching: IMemoryCache with 1-hour TTL to minimize Dataverse queries
        // Uses RetrieveByAlternateKeyAsync for indexed, fast lookups via sprk_skillcode alternate key
        services.AddScoped<ISkillLookupService, SkillLookupService>();

        // ============================================================================
        // Tool Lookup Service (cached alternate key lookups for SaaS portability)
        // ============================================================================
        // Scoped: retrieves AI tools by portable code (e.g., "TL-001") instead of environment-specific GUIDs
        // Enables multi-environment deployments (DEV/QA/PROD) without hardcoded tool GUIDs
        // Caching: IMemoryCache with 1-hour TTL to minimize Dataverse queries
        // Uses RetrieveByAlternateKeyAsync for indexed, fast lookups via sprk_toolcode alternate key
        services.AddScoped<IToolLookupService, ToolLookupService>();

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
        services.AddScoped<IAiToolHandler, FinancialCalculationToolHandler>();

        // DataverseUpdateToolHandler - generic entity record update from playbook context
        // Allows playbooks to update arbitrary entity fields without custom handlers
        // Handles type conversion for Money, EntityReference, and other Dataverse types
        services.AddScoped<IAiToolHandler, DataverseUpdateToolHandler>();

        // InvoiceExtractionToolHandler - wraps InvoiceAnalysisService for playbook workflows
        // Extracts invoice facts, generates AI summary, serializes to JSON for storage
        // Returns: aiSummary (5000 char limit), extractedJson (20000 char limit), facts object
        services.AddScoped<IAiToolHandler, InvoiceExtractionToolHandler>();

        // ============================================================================
        // Attachment Classification Job Handler (ADR-013: AI via BFF, ADR-015: no content logging)
        // ============================================================================
        // Scoped: classifies email attachments as invoice candidates via AI (Playbook A)
        // IdempotencyKey: classify-{documentId}-attachment
        // Writes sprk_classification, sprk_classificationconfidence, sprk_invoicehintsjson
        // Sets sprk_invoicereviewstatus=ToReview for InvoiceCandidate and Unknown
        services.AddScoped<IJobHandler, AttachmentClassificationJobHandler>();

        // ============================================================================
        // Invoice Extraction Job Handler (ADR-013: AI via BFF, ADR-015: no content logging)
        // ============================================================================
        // Scoped: extracts invoice facts via AI (Playbook B), creates BillingEvent records
        // IdempotencyKey: extract-{invoiceId}
        // VisibilityState ALWAYS "Invoiced" (deterministic, never from LLM)
        // Enqueues SpendSnapshotGeneration and InvoiceIndexing jobs on success
        services.AddScoped<IJobHandler, InvoiceExtractionJobHandler>();

        // ============================================================================
        // Invoice Indexing Job Handler (indexes invoices into Azure AI Search)
        // ============================================================================
        // Scoped: generates embeddings and indexes invoice documents for semantic search
        // IdempotencyKey: invoice-index-{invoiceId}
        // Index: spaarke-invoices-dev (per-tenant in production: spaarke-invoices-{tenantId})
        // Embeddings: text-embedding-3-large (3072 dimensions)
        services.AddScoped<IJobHandler, InvoiceIndexingJobHandler>();

        // ============================================================================
        // Spend Snapshot Generation Job Handler (deterministic aggregation, no AI)
        // ============================================================================
        // Scoped: aggregates BillingEvents into SpendSnapshots, then evaluates signals
        // Calls: SpendSnapshotService (create/update snapshots) + SignalEvaluationService (threshold detection)
        // IdempotencyKey: snapshots-{matterId}
        // Idempotent: Services use upsert via alternate keys (SpendSnapshot) and deterministic IDs (SpendSignal)
        // No downstream job enqueues - end of analytics chain
        services.AddScoped<IJobHandler, SpendSnapshotGenerationJobHandler>();

        return services;
    }
}
