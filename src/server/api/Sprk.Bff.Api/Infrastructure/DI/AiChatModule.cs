using Sprk.Bff.Api.Services.Ai.Chat;
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
///   1. AddSingleton&lt;ISprkAgent, DirectOpenAiAgent&gt;  — AIPU2-008: R2 provider-agnostic agent boundary (FR-701/FR-702)
///   2. AddSingleton&lt;AiLatencyTelemetry&gt;            — AIPU2-066: AI latency telemetry meter
///   3. AddScoped&lt;AiLatencyTracker&gt;                  — AIPU2-066: per-request latency stopwatch
///   4. AddSingleton&lt;IPlaybookCandidateSelector, PlaybookCandidateSelector&gt; — chat-routing-redesign-r1 task 113R / FR-47 + FR-48 top-N selector
///
/// Planned registrations (future AIPU2 tasks):
///   - SprkChatAgentFactory          — Extended factory (replaces AiModule registration in R2)
///   - ChatOrchestrationService      — Three-pane experience orchestration (router + event bus)
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
        // AIPU2-008 / AIPU2-060: Register the R2 provider-agnostic agent boundary (FR-701/FR-702).
        // DirectOpenAiAgent is the Phase 2 full implementation that streams directly from Azure OpenAI.
        // Constructor deps resolved from DI:
        //   - IChatClient                (singleton, registered in AiModule via AddChatClient().UseFunctionInvocation())
        //   - IOrchestratorPromptBuilder (singleton, registered in AiCapabilitiesModule)
        //   - ILogger<DirectOpenAiAgent> (framework, always available)
        // Phase 3 will introduce FoundryAgent and a MultiAgentOrchestrator to replace this registration.
        services.AddSingleton<ISprkAgent, DirectOpenAiAgent>();

        // AIPU2-066: AI Latency telemetry services.
        // AiLatencyTelemetry — singleton: Meter instances are thread-safe and long-lived.
        // ADR-010: concrete singleton, no interface (single implementation, no test seam required).
        services.AddSingleton<AiLatencyTelemetry>();

        // AiLatencyTracker — scoped: one stopwatch per HTTP request.
        // Wraps AiLatencyTelemetry with per-request state (model, routing layer, elapsed times).
        // Injected into ChatEndpoints streaming path to record TTFT / TBT / TTLT / token counts.
        services.AddScoped<AiLatencyTracker>();

        // chat-routing-redesign-r1 task 113R (FR-47 + FR-48):
        // Top-N file-aware playbook candidate selector. Pure in-memory aggregator
        // over PlaybookDispatcher.RunPhaseBVectorMatchAsync output (task 112). FR-48
        // invariant: NEVER auto-executes — always returns candidates for downstream
        // `playbook_options` SSE rendering (task 117a). The interface justification
        // (per ADR-010): there is a sole DI injection target plus multiple test
        // mocks; concrete + interface keeps the test seam clean. Singleton lifetime
        // is correct — the selector holds no per-request state and depends only on
        // IOptions + ILogger.
        services.AddSingleton<IPlaybookCandidateSelector, PlaybookCandidateSelector>();

        return services;
    }
}
