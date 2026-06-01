using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Sprk.Bff.Api.Services.Ai.Sessions;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Platform Foundation services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Baseline DI count before this module: 89 (per ADR-010 tracking comment in CLAUDE.md).
/// This module adds non-framework singleton/scoped registrations (ADR-010: ≤15 unconditional):
///
/// UNCONDITIONAL (always registered) — 15 total:
///   1. AddKeyedSingleton&lt;IChatClient&gt;("raw")            — ADR-010 (task 071) — Raw Azure OpenAI client (pre-function-invocation) for compound intent detection
///   2. AddChatClient&lt;IChatClient&gt;                       — ADR-010 (AIPL-050) — Azure OpenAI IChatClient bridge (UseFunctionInvocation pipeline)
///   3. AddSingleton&lt;LlamaParseClient&gt;                  — ADR-010 (AIPL-012)
///   4. AddSingleton&lt;DocumentIntelligenceService&gt;        — ADR-010 (AIPL-012)
///   5. AddSingleton&lt;DocumentParserRouter&gt;               — ADR-010 (AIPL-012)
///   6. AddSingleton&lt;SemanticDocumentChunker&gt;            — ADR-010 (AIPL-011)
///   7. AddSingleton&lt;RagQueryBuilder&gt;                    — ADR-010 (AIPL-010)
///   8. AddSingleton&lt;SprkChatAgentFactory&gt;               — ADR-010 (AIPL-051) — Agent factory (singleton: IChatClient is thread-safe)
///   9. AddScoped&lt;IChatContextProvider, PlaybookChatContextProvider&gt; — ADR-010 (AIPL-051) — Scoped: resolves Dataverse context per request
///  10. AddScoped&lt;IChatDataverseRepository, ChatDataverseRepository&gt; — ADR-010 (AIPL-052) — Scoped: Dataverse persistence for sessions + messages
///  11. AddScoped&lt;ChatSessionManager&gt;                    — ADR-010 (AIPL-052) — Scoped: session lifecycle (Redis + Dataverse)
///  12. AddScoped&lt;ChatHistoryManager&gt;                    — ADR-010 (AIPL-052) — Scoped: message history + summarisation
///  13. AddScoped&lt;ChatContextMappingService&gt;             — ADR-010 (AIPL-053) — Scoped: context mapping resolution (Redis + Dataverse)
///  14. AddScoped&lt;AnalysisChatContextResolver&gt;          — ADR-010 (task 020) — Scoped: analysis context resolution (Redis + Dataverse)
///  15. AddScoped&lt;PendingPlanManager&gt;                    — ADR-010 (task 071) — Scoped: pending plan Redis storage (30-min TTL, plan:pending key)
///
/// CONDITIONAL (DocumentIntelligence:Enabled = true) — 4 additional feature-gated registrations:
///  16. AddSingleton&lt;RagIndexingPipeline&gt;               — ADR-010 (AIPL-013) — conditional: requires SearchIndexClient + IOpenAiClient
///  17. AddSingleton&lt;ReferenceIndexingService&gt;          — ADR-010 (AIRA-011) — conditional: golden reference knowledge indexing
///  18. AddSingleton&lt;ReferenceRetrievalService&gt;         — ADR-010 (AIRA-013) — conditional: reference knowledge retrieval
///  19. AddHostedService&lt;PlaybookIndexingBackgroundService&gt; — ADR-001 (no Azure Functions) — conditional: hosted service
///
/// Plus 1 framework registration: AddHttpClient&lt;LlamaParseClient&gt; (not counted per ADR-010)
///
/// Phase 2 services (AgentServiceClient, AgentServiceNodeExecutor, CodeInterpreterBridge, options)
/// are registered in AnalysisServicesModule and ConfigurationModule per the feature module pattern
/// (ADR-010) — they are NOT duplicated here (AIPU-075 audit: 2026-05-16).
///
/// DI count: 15 unconditional / 15 limit (ADR-010 compliant). See bottom of file for full registration list.
///
/// Prerequisites (must already be registered before calling AddAiModule):
/// - <c>ITextExtractor</c> — registered in Program.cs when <c>DocumentIntelligence:Enabled = true</c>
/// - <c>IConfiguration</c> — registered by the host
/// - <c>ILogger&lt;T&gt;</c> — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
/// - <c>IOptions&lt;LlamaParseOptions&gt;</c> — registered via <c>Configure&lt;LlamaParseOptions&gt;</c>
///   in Program.cs (added by task AIPL-004)
/// - <c>IOptions&lt;AiSearchOptions&gt;</c> — registered via <c>Configure&lt;AiSearchOptions&gt;</c>
///   in Program.cs (added by task AIPL-004)
/// - <c>SearchIndexClient</c> — registered in Program.cs when DocumentIntelligence is enabled
/// - <c>IRagService</c> — registered in Program.cs when DocumentIntelligence is enabled
/// - <c>IOpenAiClient</c> — registered in Program.cs when DocumentIntelligence is enabled
/// - <c>ITextChunkingService</c> — registered in Program.cs
///
/// Usage in Program.cs:
/// <code>
/// builder.Services.AddAiModule(builder.Configuration);
/// </code>
/// </remarks>
public static class AiModule
{
    /// <summary>
    /// Registers AI Platform Foundation services with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (used to read LlamaParse:BaseUrl and AzureOpenAI settings).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAiModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // IChatClient — Agent Framework bridge to Azure OpenAI (AIPL-050, ADR-013, ADR-010).
        // Uses Microsoft.Extensions.AI.OpenAI adapter (OpenAIChatClient) over Azure.AI.OpenAI SDK.
        // Configuration keys: AzureOpenAI:Endpoint (required), AzureOpenAI:ChatModelName (required).
        // Authentication: DefaultAzureCredential (Managed Identity in Azure, dev credentials locally).
        // The IChatClient is consumed by SprkChatAgentFactory (AIPL-051) via chatClient.AsAIAgent().
        // Registered as singleton: AzureOpenAIClient is thread-safe; ChatClient is lightweight.
        var azureOpenAiEndpoint = configuration["AzureOpenAI:Endpoint"];
        var azureOpenAiChatModel = configuration["AzureOpenAI:ChatModelName"];
        if (!string.IsNullOrEmpty(azureOpenAiEndpoint) && !string.IsNullOrEmpty(azureOpenAiChatModel))
        {
            // Local helper that constructs the inner IChatClient.
            //
            // Auth mode is chosen by configuration:
            //  - If AzureOpenAI:ApiKey is set (typically a Key Vault reference), use API key auth.
            //    This is the documented ADR-028 exception (2026-05-28) for AIServices-kind
            //    accounts where MI auth returned persistent 401 PermissionDenied despite full
            //    Cognitive Services User wildcard + Cognitive Services OpenAI User grants.
            //    Documented Microsoft escape hatch (see learn.microsoft.com Q&A 2168038).
            //  - Otherwise (and as the long-term target), use the DI-injected TokenCredential
            //    (UAMI-pinned via ManagedIdentityCredentialFactory). Restore-to-MI is a single
            //    config change (clear the AzureOpenAI:ApiKey setting).
            //
            // Both code paths remain live so per-env choice is a config toggle, not a redeploy.
            static IChatClient BuildInnerClient(IServiceProvider sp, string endpoint, string model)
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var apiKey = config["AzureOpenAI:ApiKey"];
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Sprk.Bff.Api.AiModule");

                AzureOpenAIClient client;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    logger.LogInformation("AzureOpenAI auth: KV-backed ApiKey (ADR-028 scoped exception for AIServices-kind MI 401).");
                    client = new AzureOpenAIClient(new Uri(endpoint), new System.ClientModel.ApiKeyCredential(apiKey));
                }
                else
                {
                    logger.LogInformation("AzureOpenAI auth: Managed Identity (canonical, ADR-028).");
                    var credential = sp.GetRequiredService<TokenCredential>();
                    client = new AzureOpenAIClient(new Uri(endpoint), credential);
                }

                return client.GetChatClient(model).AsIChatClient();
            }

            // Register the raw (pre-function-invocation) client under a keyed name.
            // Used by SprkChatAgentFactory for compound intent detection (task 071):
            // the factory uses this client to inspect what tools the LLM wants to call
            // BEFORE function invocation executes them, enabling plan_preview gating.
            // Key: "raw" — resolved via IServiceProvider.GetKeyedService<IChatClient>("raw").
            services.AddKeyedSingleton<IChatClient>("raw", (sp, _) =>
                BuildInnerClient(sp, azureOpenAiEndpoint, azureOpenAiChatModel));

            // UseFunctionInvocation enables automatic tool-call execution:
            // when the LLM requests a tool call, the pipeline executes the AIFunction,
            // feeds the result back into the conversation, and continues until the LLM
            // produces a text response.  Without this, tool calls go unexecuted and
            // the streaming response contains only FunctionCallContent (no text tokens).
            services.AddChatClient(sp => BuildInnerClient(sp, azureOpenAiEndpoint, azureOpenAiChatModel))
                .UseFunctionInvocation();
        }

        // LlamaParseClient — registered via IHttpClientFactory (ADR-010).
        // The base address is read from LlamaParseOptions.BaseUrl at registration time.
        // The API key is resolved at runtime from IConfiguration (Key Vault reference).
        var baseUrl = configuration["LlamaParse:BaseUrl"] ?? "https://api.cloud.llamaindex.ai";
        services.AddHttpClient(LlamaParseClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            // Timeout is handled internally by the polling loop (ParseTimeoutSeconds);
            // the HttpClient timeout is set generously to avoid double-timeout conflicts.
            client.Timeout = TimeSpan.FromSeconds(300);
        });

        // Register LlamaParseClient so it can be resolved from DI.
        // Uses the named HttpClient above via IHttpClientFactory.
        services.AddSingleton<LlamaParseClient>();

        // DocumentIntelligenceService — thin wrapper around ITextExtractor.
        // Singleton: ITextExtractor is singleton; all state is injected.
        services.AddSingleton<DocumentIntelligenceService>();

        // DocumentParserRouter — concrete singleton per ADR-010.
        // Routes between DocumentIntelligenceService and LlamaParseClient based on
        // document characteristics and LlamaParseOptions.Enabled feature flag.
        services.AddSingleton<DocumentParserRouter>();

        // SemanticDocumentChunker — concrete singleton per ADR-010 (AIPL-011).
        // Clause-aware chunker for the RAG indexing pipeline; operates on AnalyzeResult
        // from the Layout model.  Stateless and thread-safe.  No interface registered —
        // single implementation (ADR-010: no unnecessary interfaces).
        services.AddSingleton<SemanticDocumentChunker>();

        // RagQueryBuilder — concrete singleton per ADR-010 (AIPL-010).
        // Builds metadata-aware RAG queries from DocumentAnalysisResult (entities,
        // key phrases, document type, summary).  Replaces the first-500-chars approach
        // in AnalysisOrchestrationService.  Stateless and thread-safe.
        services.AddSingleton<RagQueryBuilder>();

        // RagIndexingPipeline, ReferenceIndexingService, ReferenceRetrievalService all depend
        // on IOpenAiClient + SearchIndexClient — both gated on DocumentIntelligence:Enabled.
        // Register conditionally so DI does not fail when AI is disabled.
        var documentIntelligenceEnabled = configuration.GetValue<bool>("DocumentIntelligence:Enabled");
        if (documentIntelligenceEnabled)
        {
            // RagIndexingPipeline — concrete singleton per ADR-010 (AIPL-013).
            // Orchestrates the full indexing pipeline: chunk → embed → index into both
            // the knowledge index (512-token) and discovery index (1024-token).
            // Called by RagIndexingJobHandler (task AIPL-014) via Service Bus.
            // Requires: ITextChunkingService, IRagService, SearchIndexClient,
            //           IOpenAiClient, IOptions<AiSearchOptions>.
            services.AddSingleton<RagIndexingPipeline>();

            // ReferenceIndexingService — concrete singleton per ADR-010 (AIRA-011).
            // Indexes golden reference knowledge sources into spaarke-rag-references index.
            // 512-token chunks, 100-token overlap, 3072-dim embeddings.
            // Called by AdminKnowledgeEndpoints (admin-only, not Service Bus).
            // Requires: ITextChunkingService, SearchIndexClient, IOpenAiClient,
            //           IScopeResolverService, IOptions<AiSearchOptions>.
            services.AddSingleton<ReferenceIndexingService>();

            // ReferenceRetrievalService — concrete singleton per ADR-010 (AIRA-013).
            // Queries spaarke-rag-references index for golden reference knowledge using
            // hybrid search (keyword + vector + semantic reranking).
            // Parallel retrieval path to RagService (which queries customer documents).
            // Requires: SearchIndexClient, IOpenAiClient, IEmbeddingCache, IOptions<AiSearchOptions>.
            services.AddSingleton<ReferenceRetrievalService>();
        }

        // SprkChatAgentFactory — singleton per ADR-010 (AIPL-051).
        // Creates SprkChatAgent instances per session.  Singleton is safe because
        // IChatClient is thread-safe and IChatContextProvider is resolved from a
        // new DI scope inside CreateAgentAsync (avoids captive dependency).
        // Consumed by ChatSessionManager (AIPL-052) and ChatEndpoints (AIPL-054).
        services.AddSingleton<SprkChatAgentFactory>();

        // IChatContextProvider — scoped per ADR-010 (AIPL-051).
        // Seam required: production impl calls Dataverse/ScopeResolverService;
        // tests inject a stub.  Scoped lifetime matches request lifetime so that
        // per-request state (auth credentials, cancellation) is naturally bounded.
        services.AddScoped<IChatContextProvider, PlaybookChatContextProvider>();

        // IChatDataverseRepository — scoped per ADR-010 (AIPL-052).
        // Seam required: production impl calls IDataverseService (sprk_aichatsummary /
        // sprk_aichatmessage entities).  Unit tests inject an in-memory stub.
        // Scoped lifetime matches request lifetime (IDataverseService is singleton; scoping
        // the repository limits its visibility to a single request).
        services.AddScoped<IChatDataverseRepository, ChatDataverseRepository>();

        // ChatSessionManager — scoped per ADR-010 (AIPL-052, AIPU2-064).
        // Manages session lifecycle (create / get / delete) with Redis hot path,
        // optional Cosmos DB warm path (D-06 write-through), and Dataverse cold path.
        // ISessionPersistenceService is resolved optionally — when AiPersistenceModule is
        // registered (production) it is injected; when absent (environments without Cosmos)
        // it resolves to null and the manager falls back to Redis + Dataverse only.
        services.AddScoped<ChatSessionManager>(sp => new ChatSessionManager(
            cache: sp.GetRequiredService<IDistributedCache>(),
            dataverseRepository: sp.GetRequiredService<IChatDataverseRepository>(),
            logger: sp.GetRequiredService<ILogger<ChatSessionManager>>(),
            persistence: sp.GetService<ISessionPersistenceService>()));   // optional — null when Cosmos not configured

        // ChatHistoryManager — scoped per ADR-010 (AIPL-052).
        // Manages message addition, history retrieval, summarisation (15 messages), and
        // archiving (50 messages).  Scoped: depends on ChatSessionManager (scoped).
        services.AddScoped<ChatHistoryManager>();

        // ChatContextMappingService — scoped per ADR-010 (AIPL-053).
        // Resolves which playbook(s) to show for a given entityType + pageType via
        // sprk_aichatcontextmapping entity. Redis-first with 30-min sliding TTL (ADR-009).
        // Scoped: depends on IGenericEntityService (singleton); scoping limits per-request visibility.
        services.AddScoped<ChatContextMappingService>();

        // AnalysisChatContextResolver — scoped per ADR-010 (Phase 2C, task 020).
        // Resolves analysis-scoped SprkChat context (playbooks, inline actions, knowledge sources,
        // commands, search guidance, scope metadata) for a given analysisId.
        // Redis-first with 30-min absolute TTL (ADR-009).
        // Cache key pattern: "chat-context:{tenantId}:{analysisId}" (ADR-014 — tenant-scoped).
        // Scoped: depends on IGenericEntityService (singleton), IDistributedCache (singleton).
        services.AddScoped<AnalysisChatContextResolver>();

        // StandaloneChatContextProvider — scoped (AI Platform Unification R1).
        // Resolves standalone SprkChat context for sprk_spaarkeai Code Page.
        // Redis-first with 30-min absolute TTL (ADR-009).
        services.AddScoped<StandaloneChatContextProvider>();

        // PendingPlanManager — scoped per ADR-010 (task 071, Phase 2F).
        // Stores pending plans in Redis at "plan:pending:{tenantId}:{sessionId}" with 30-min TTL.
        // Used by CompoundIntentDetector flow in ChatEndpoints.SendMessageAsync to gate
        // multi-tool chains and write-back operations behind user approval.
        // ADR-009: Redis via IDistributedCache; no in-memory fallback.
        // ADR-010: Concrete type, no interface (single implementation).
        services.AddScoped<PendingPlanManager>();

        // PlaybookIndexingBackgroundService — hosted service (ADR-001 mandate, no Azure Functions).
        // Processes playbook embedding indexing requests from a bounded Channel<string>.
        // Factory-instantiates PlaybookIndexingService internally (ADR-010: no new DI registration
        // for the service itself). Exposes a static Instance accessor so the trigger endpoint
        // (POST /api/ai/playbooks/{playbookId}/index) can enqueue without DI.
        // Requires: IPlaybookService, SearchIndexClient, IOpenAiClient.
        // Conditional: SearchIndexClient + IOpenAiClient only registered when DocumentIntelligence:Enabled=true.
        if (documentIntelligenceEnabled)
        {
            services.AddHostedService<PlaybookIndexingBackgroundService>();
        }

        return services;
    }
}

// =============================================================================
// DI REGISTRATION COUNT AUDIT — AiModule.cs (AIPU-075, 2026-05-16)
// ADR-010 Limit: 15 non-framework registrations per module
// =============================================================================
// UNCONDITIONAL REGISTRATIONS — 15 / 15
// -----------------------------------------------------------------------------
//  1. AddKeyedSingleton<IChatClient>("raw")                — raw OpenAI client (task 071)
//  2. AddChatClient<IChatClient>                           — OpenAI pipeline client (AIPL-050)
//  3. AddSingleton<LlamaParseClient>                       — document parser client (AIPL-012)
//  4. AddSingleton<DocumentIntelligenceService>            — Doc Intel wrapper (AIPL-012)
//  5. AddSingleton<DocumentParserRouter>                   — parser router (AIPL-012)
//  6. AddSingleton<SemanticDocumentChunker>                — clause-aware chunker (AIPL-011)
//  7. AddSingleton<RagQueryBuilder>                        — metadata-aware RAG query builder (AIPL-010)
//  8. AddSingleton<SprkChatAgentFactory>                   — chat agent factory (AIPL-051)
//  9. AddScoped<IChatContextProvider, PlaybookChatContextProvider> — playbook context (AIPL-051)
// 10. AddScoped<IChatDataverseRepository, ChatDataverseRepository> — chat persistence (AIPL-052)
// 11. AddScoped<ChatSessionManager>                        — session lifecycle (AIPL-052)
// 12. AddScoped<ChatHistoryManager>                        — message history (AIPL-052)
// 13. AddScoped<ChatContextMappingService>                 — context mapping (AIPL-053)
// 14. AddScoped<AnalysisChatContextResolver>               — analysis context (task 020)
// 15. AddScoped<PendingPlanManager>                        — pending plan Redis storage (task 071)
// -----------------------------------------------------------------------------
// CONDITIONAL (DocumentIntelligence:Enabled=true) — feature-gated, excluded from ADR-010 limit
// -----------------------------------------------------------------------------
// 16. AddSingleton<RagIndexingPipeline>                    — RAG indexing pipeline (AIPL-013)
// 17. AddSingleton<ReferenceIndexingService>               — reference knowledge indexing (AIRA-011)
// 18. AddSingleton<ReferenceRetrievalService>              — reference knowledge retrieval (AIRA-013)
// 19. AddHostedService<PlaybookIndexingBackgroundService>  — hosted indexing worker (ADR-001)
// -----------------------------------------------------------------------------
// PHASE 2 SERVICES — registered in appropriate feature modules, not here (AIPU-075 audit)
// -----------------------------------------------------------------------------
// AgentServiceClient          → AnalysisServicesModule.AddNodeExecutors (AIPU-061)
// AgentServiceNodeExecutor    → AnalysisServicesModule.AddNodeExecutors as INodeExecutor (AIPU-061)
// CodeInterpreterBridge       → AnalysisServicesModule.AddNodeExecutors (AIPU-070)
// AgentServiceOptions         → ConfigurationModule (AIPU-061, deferred validation — kill-switch option)
// CodeInterpreterOptions      → ConfigurationModule (AIPU-070, deferred validation — kill-switch option)
// BingGroundingOptions        → ConfigurationModule (AIPU-071, deferred validation — kill-switch option)
// AgentServiceRoutingMiddleware → SprkChatAgentFactory.WrapWithMiddleware, factory-instantiated (AIPU-072)
// =============================================================================
