using Sprk.Bff.Api.Services.Ai.Chat;

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
///
/// Planned registrations (future AIPU2 tasks):
///   2. SprkChatAgentFactory         — Extended factory (replaces AiModule registration in R2)
///   3. ChatOrchestrationService     — Three-pane experience orchestration (router + event bus)
///
/// DI count: 1 unconditional (ADR-010 compliant, well within ≤15 limit).
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

        return services;
    }
}
