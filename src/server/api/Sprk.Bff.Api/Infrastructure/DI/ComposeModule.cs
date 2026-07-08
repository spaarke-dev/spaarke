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
        return services;
    }
}
