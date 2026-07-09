using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Chat / Agent services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Registers the AI chat extensions and agent implementations introduced in Spaarke AI Platform Unification R2.
/// Supplements the baseline chat services already registered in AiModule (SprkChatAgentFactory,
/// ChatSessionManager, ChatHistoryManager, etc.) with R2-specific agent implementations.
///
/// UNCONDITIONAL registrations:
///   1. AddSingleton&lt;AiLatencyTelemetry&gt;            — AIPU2-066: AI latency telemetry meter
///   2. AddScoped&lt;AiLatencyTracker&gt;                  — AIPU2-066: per-request latency stopwatch
///   3. AddSingleton&lt;OrchestratorPromptBuilder&gt; (and as IOrchestratorPromptBuilder)
///      — chat-routing-redesign-r1 task 141 / FR-22: orchestrator-side prompt builder.
///   4. AddSingleton&lt;IUiActionAckCoordinator&gt; — D-F3 UI-action truthfulness (FR-A1-08 /
///      task AIR2-037): client-ack coordination for UI-affecting tool results. Singleton so a
///      pending wait registered by the tool-call's scoped request survives to be resolved by
///      the ack endpoint's LATER, separate scoped request.
///
/// (The AIPU2-008 provider-agnostic agent boundary registration was removed by
/// spaarke-ai-architecture-redesign-r1 Track-B batch 1 — the implementation was
/// registered but never consumed. The FR-46/FR-47/FR-49 classifier-stack registrations
/// — top-N candidate selector, hybrid LLM intent reranker, and the options SSE payload
/// builder — were DELETED by task 035 / FR-P2-06 with the dispatcher stack, ADR-039.)
///
/// DI count: 4 unconditional (ADR-010 compliant, well within ≤15 limit).
///
/// Prerequisites (must already be registered before calling AddAiChatModule):
///   - <c>IConfiguration</c>   — registered by the host
///   - <c>ILogger&lt;T&gt;</c>  — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
///   - <c>IChatClient</c>      — registered in AiModule (requires AddAnalysisServicesModule first)
///   - Redis / <c>IDistributedCache</c> — registered in CacheModule
///
/// Usage in Program.cs:
/// <code>
/// builder.Services.AddAiChatModule(builder.Configuration);
/// </code>
/// </remarks>
public static class AiChatModule
{
    /// <summary>
    /// Registers AI Chat and Agent extension services with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAiChatModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // chat-routing-redesign-r1 task 141 / FR-22: OrchestratorPromptBuilder.
        // Singleton — holds an in-process MemoryCache for the stable prefix (keyed by active
        // playbook name). Singleton lifetime is required so the cache persists across requests
        // (ADR-009 exception: in-process structural metadata, not business data).
        services.AddSingleton<OrchestratorPromptBuilder>();
        services.AddSingleton<IOrchestratorPromptBuilder>(sp =>
            sp.GetRequiredService<OrchestratorPromptBuilder>());

        // AIPU2-066: AI Latency telemetry services.
        // AiLatencyTelemetry — singleton: Meter instances are thread-safe and long-lived.
        // ADR-010: concrete singleton, no interface (single implementation, no test seam required).
        services.AddSingleton<AiLatencyTelemetry>();

        // AiLatencyTracker — scoped: one stopwatch per HTTP request.
        // Wraps AiLatencyTelemetry with per-request state (model, routing layer, elapsed times).
        // Injected into ChatEndpoints streaming path to record TTFT / TBT / TTLT / token counts.
        services.AddScoped<AiLatencyTracker>();

        // FR-P2-06 (task 035): the FR-46/FR-47/FR-49 classifier-stack registrations
        // (the top-N candidate selector, the hybrid LLM reranker, and the options SSE builder)
        // were DELETED with the dispatcher stack — the agent-turn loop is the ONE dispatch
        // protocol (ADR-039); nothing emits or consumes their SSE projection anymore.

        // D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037): the ONE ack-gating
        // mechanism for UI-affecting tool results (SendWorkspaceArtifactHandler today;
        // any future UI-claiming tool reuses this SAME coordinator — no second ack
        // mechanism per CLAUDE.md §11). Singleton: an in-process ConcurrentDictionary of
        // pending waits keyed by (sessionId, frameId); see UiActionAckCoordinator remarks
        // for why in-process (not Redis/Cosmos) is the right scope for a sub-10s wait.
        services.AddSingleton<IUiActionAckCoordinator, UiActionAckCoordinator>();

        return services;
    }
}
