using Sprk.Bff.Api.Infrastructure.Sse;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Safety;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Safety services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Registers the safety perimeter services introduced in Spaarke AI Platform Unification R2.
///
/// UNCONDITIONAL registrations:
///   1. PromptShieldService          — Prompt injection / jailbreak detection (scoped)   [AIPU2-020]
///   2. PromptShieldTelemetry        — OTEL metrics for Prompt Shield (singleton)         [AIPU2-020]
///   3. Named HttpClient "ContentSafety" — pre-configured for Content Safety REST API    [AIPU2-020]
///   4. IGroundednessCheckService    — Post-LLM groundedness annotation (scoped)         [AIPU2-021]
///
/// Planned (registered by future AIPU2 tasks):
///   5. ContentSafetyService   — Azure AI Content Safety integration (prompt + completion screening)
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
        // Named HttpClient "ContentSafety" — base address from AiSafety:ContentSafety:Endpoint.
        // The API key is read at call time by PromptShieldService (supports Key Vault rotation).
        // Timeout is set to 120ms (generous outer limit; PromptShieldService enforces 100ms internally).
        var endpoint = configuration["AiSafety:ContentSafety:Endpoint"]
            ?? "https://spaarke-contentsafety-dev.cognitiveservices.azure.com/";

        services.AddHttpClient(PromptShieldService.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            // Outer timeout is generous; PromptShieldService applies its own 100ms CancellationToken.
            client.Timeout = TimeSpan.FromMilliseconds(120);
        });

        // PromptShieldTelemetry — singleton: Meter instances are thread-safe and long-lived.
        // ADR-010: no interface needed (single implementation, no seam required for testing).
        services.AddSingleton<PromptShieldTelemetry>();

        // IPromptShieldService / PromptShieldService — scoped: one instance per HTTP request.
        // Scoped lifetime ensures that per-request cancellation tokens and logging context
        // are correctly propagated from the outer request pipeline.
        services.AddScoped<IPromptShieldService, PromptShieldService>();

        // GroundednessCheckTelemetry — singleton: OTEL Meter instances are thread-safe.
        // ADR-010: no interface needed (single implementation, no seam required for testing).
        services.AddSingleton<GroundednessCheckTelemetry>();

        // IGroundednessCheckService / GroundednessCheckService — AIPU2-021
        // Scoped: one instance per HTTP request. Resolves named HttpClient "ContentSafety" via
        // IHttpClientFactory. Interface registered for testability (ADR-010: seam required —
        // unit tests inject a stub without hitting the real Content Safety API).
        services.AddScoped<IGroundednessCheckService>(sp =>
        {
            var factory    = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(PromptShieldService.HttpClientName);
            var logger     = sp.GetRequiredService<ILogger<GroundednessCheckService>>();
            var telemetry  = sp.GetRequiredService<GroundednessCheckTelemetry>();
            return new GroundednessCheckService(httpClient, logger, telemetry);
        });

        // ICitationVerificationService / CitationVerificationService — AIPU2-022
        // Singleton: stateless orchestrator; all IVerificationProvider implementations are also singletons.
        // ADR-010: interface registered for testability (unit tests inject stub providers).
        services.AddSingleton<ICitationVerificationService, CitationVerificationService>();

        // InternalIndexProvider — AIPU2-023
        // Singleton: stateless; SearchClient is thread-safe and long-lived.
        // Handles CaseLaw, Statute, and Regulation types against spaarke-rag-references index.
        // Registered as IVerificationProvider so CitationVerificationService receives it via
        // IEnumerable<IVerificationProvider> constructor injection.
        services.AddSingleton<IVerificationProvider>(sp =>
            new InternalIndexProvider(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<InternalIndexProvider>>()));


        // ISseEventValidator / SseEventValidator -- AIPU2-026
        services.AddSingleton<ISseEventValidator, SseEventValidator>();

        // SseValidationTelemetry -- AIPU2-026
        services.AddSingleton<SseValidationTelemetry>();

        // SseOutputGuard -- AIPU2-026
        // Scoped: one instance per HTTP request. Validates SSE tool output payloads.
        services.AddScoped<SseOutputGuard>();

        return services;
    }
}
