using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Dependency injection module for the Compose drafting workspace (spaarkeai-compose-r1).
///
/// Post-cleanup (retirement of Compose AI dispatch): only two registrations remain:
/// - <see cref="IComposeService"/> — Load/Save/Promote DOCX lifecycle orchestration.
///   Injects <c>ISpeFileOperations</c> + <c>ChatSessionManager</c> + <c>IGenericEntityService</c>.
/// - <see cref="StaleCheckoutSweeperHostedService"/> — releases SPE checkouts whose
///   client-side heartbeat has gone stale (Spike #3 §4.3).
///
/// Retired: <c>IComposeDocumentService</c> (Load/Save now flows through <c>SpeFileStore</c>),
/// <c>ComposeSessionService</c> (rebind logic inlined into <see cref="ComposeService"/>),
/// <c>IDocxTextExtractor</c> (R7 <c>ITextExtractor</c>/<c>TextExtractorService</c> covers DOCX
/// via Document Intelligence).
/// </summary>
public static class ComposeModule
{
    public static IServiceCollection AddComposeModule(this IServiceCollection services)
    {
        services.AddScoped<IComposeService, ComposeService>();
        services.AddHostedService<StaleCheckoutSweeperHostedService>();

        // R2 W1 edit/annotation services — pure deterministic text logic (ADR-013:
        // NO AI-internal injection; stateless concretes registered per ADR-010).
        services.AddSingleton<IComposeEditValidator, ComposeEditValidator>();   // FR-19 (task 020)
        services.AddSingleton<SemanticAppendixGenerator>();                     // FR-22 (task 023)
        services.AddSingleton<CriticMarkupRenderer>();                          // FR-22 (task 023)

        // R2 W1 SPE change-detection (FR-26, task 052) — subscription state machine +
        // BackgroundService renewal (ADR-001 hosted service; ADR-007 Graph stays behind
        // the SpeFileStore facade; ADR-009 Redis state). Orchestrator is Scoped (injects
        // scoped ISpeFileOperations); the hosted service resolves it via CreateScope.
        services.AddScoped<SpeSyncOrchestrator>();
        services.AddHostedService<SpeWebhookRenewalHostedService>();
        return services;
    }
}
