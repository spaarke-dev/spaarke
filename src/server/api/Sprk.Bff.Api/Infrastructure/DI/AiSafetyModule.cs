namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Safety services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Registers the safety perimeter services introduced in Spaarke AI Platform Unification R2.
///
/// UNCONDITIONAL registrations (planned — registered by future AIPU2 tasks):
///   1. ContentSafetyService   — Azure AI Content Safety integration (prompt + completion screening)
///   2. PromptShieldService    — Prompt injection / jailbreak detection
///   3. GroundednessService    — Retroactive groundedness annotation (citation verification)
///
/// Prerequisites (must already be registered before calling AddAiSafetyModule):
///   - <c>IConfiguration</c>  — registered by the host
///   - <c>ILogger&lt;T&gt;</c> — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
///
/// Usage in Program.cs:
/// <code>
/// builder.Services.AddAiSafetyModule(builder.Configuration);
/// </code>
/// </remarks>
public static class AiSafetyModule
{
    /// <summary>
    /// Registers AI Safety services with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAiSafetyModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // TODO AIPU2-xxx: Register ContentSafetyService, PromptShieldService, GroundednessService

        return services;
    }
}
