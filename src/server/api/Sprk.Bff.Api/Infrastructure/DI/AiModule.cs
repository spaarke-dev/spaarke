using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Platform Foundation services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Baseline DI count before this module: 89 (per ADR-010 tracking comment in CLAUDE.md).
/// This module adds 11 non-framework singleton/scoped registrations:
///   1. AddChatClient&lt;IChatClient&gt;                       — ADR-010 (AIPL-050) — Azure OpenAI IChatClient bridge
///   2. AddSingleton&lt;LlamaParseClient&gt;                  — ADR-010 (AIPL-012)
///   3. AddSingleton&lt;DocumentIntelligenceService&gt;        — ADR-010 (AIPL-012)
///   4. AddSingleton&lt;DocumentParserRouter&gt;               — ADR-010 (AIPL-012)
///   5. AddSingleton&lt;SemanticDocumentChunker&gt;            — ADR-010 (AIPL-011)
///   6. AddSingleton&lt;RagQueryBuilder&gt;                    — ADR-010 (AIPL-010)
///   7. AddSingleton&lt;RagIndexingPipeline&gt;               — ADR-010 (AIPL-013)
///   8. AddSingleton&lt;SprkChatAgentFactory&gt;               — ADR-010 (AIPL-051) — Agent factory (singleton: IChatClient is thread-safe)
///   9. AddScoped&lt;IChatContextProvider, PlaybookChatContextProvider&gt; — ADR-010 (AIPL-051) — Scoped: resolves Dataverse context per request
///  10. AddScoped&lt;IChatDataverseRepository, ChatDataverseRepository&gt; — ADR-010 (AIPL-052) — Scoped: Dataverse persistence for sessions + messages
///  11. AddScoped&lt;ChatSessionManager&gt;                    — ADR-010 (AIPL-052) — Scoped: session lifecycle (Redis + Dataverse)
///  12. AddScoped&lt;ChatHistoryManager&gt;                    — ADR-010 (AIPL-052) — Scoped: message history + summarisation
/// Plus 1 framework registration: AddHttpClient&lt;LlamaParseClient&gt; (not counted per ADR-010)
///
/// DI count after: 99 (2 new scoped services added in AIPL-052; see ADR-010 and NFR-10).
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
            var innerClient = new AzureOpenAIClient(
                    new Uri(azureOpenAiEndpoint), new DefaultAzureCredential())
                .GetChatClient(azureOpenAiChatModel)
                .AsIChatClient();

            // UseFunctionInvocation enables automatic tool-call execution:
            // when the LLM requests a tool call, the pipeline executes the AIFunction,
            // feeds the result back into the conversation, and continues until the LLM
            // produces a text response.  Without this, tool calls go unexecuted and
            // the streaming response contains only FunctionCallContent (no text tokens).
            services.AddChatClient(innerClient)
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

        // RagIndexingPipeline — concrete singleton per ADR-010 (AIPL-013).
        // Orchestrates the full indexing pipeline: chunk → embed → index into both
        // the knowledge index (512-token) and discovery index (1024-token).
        // Called by RagIndexingJobHandler (task AIPL-014) via Service Bus.
        // Requires: ITextChunkingService, IRagService, SearchIndexClient,
        //           IOpenAiClient, IOptions<AiSearchOptions>.
        services.AddSingleton<RagIndexingPipeline>();

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

        // ChatSessionManager — scoped per ADR-010 (AIPL-052).
        // Manages session lifecycle (create / get / delete) with Redis hot path and
        // Dataverse cold path.  Scoped: IDistributedCache is a singleton; scoping the manager
        // ensures clear per-request state and natural cancellation token boundaries.
        services.AddScoped<ChatSessionManager>();

        // ChatHistoryManager — scoped per ADR-010 (AIPL-052).
        // Manages message addition, history retrieval, summarisation (15 messages), and
        // archiving (50 messages).  Scoped: depends on ChatSessionManager (scoped).
        services.AddScoped<ChatHistoryManager>();

        return services;
    }
}
