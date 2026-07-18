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
        services.AddSingleton<ComposeEditBatch>();                              // FR-20 (task 021)
        services.AddSingleton<ComposeEditTransaction>();                       // FR-21 (task 022) — snapshot/rollback wrapper; holds no per-operation instance state (see class remarks), safe as a singleton
        services.AddSingleton<SemanticAppendixGenerator>();                     // FR-22 (task 023)
        services.AddSingleton<CriticMarkupRenderer>();                          // FR-22 (task 023)
        services.AddSingleton<DocxAnnotationWriter>();                          // FR-24 (task 050) — pure OOXML annotation writer; thread-safe stateless singleton (ADR-010). Consumed by ComposeService.PushAnnotationsAsync (registered unconditionally, same as its consumer — no asymmetric-registration risk per bff-extensions.md §F.1)
        services.AddSingleton<ParaIdPreParser>();                               // FR-08 (task 010, E2) — pure OOXML w14:paraId collect/mint pre-parse; thread-safe stateless singleton (ADR-010). Consumed by ComposeService.LoadAsync; registered UNCONDITIONALLY (same lifetime as its consumer — symmetric per bff-extensions.md §F.1, no feature gate)
        services.AddSingleton<ComposeParagraphRedlineSynthesizer>();            // FR-01/FR-03/FR-07 (E1, Option C — design §4.2) — pure paraId-keyed retained-original redline synthesizer (byte[]->byte[]): word-diff each edited paragraph -> native w:ins/w:del in place, preserving paraId + tables + structure. REPLACES the retired Docxodus WmlComparer path (task 003 NFR-09 gate proved WmlComparer strips paraId + drops tables on real docs, in BOTH 6.4.0 and 7.1.0). No external diff dependency. Consumes ComposeParaIdSpliceMap (task 012, FR-12); consumed by task 022 SaveAsync. Thread-safe stateless singleton (ADR-010); registered UNCONDITIONALLY (symmetric per bff-extensions.md §F.1)

        // R2 W1 SPE change-detection (FR-26, task 052) — subscription state machine +
        // BackgroundService renewal (ADR-001 hosted service; ADR-007 Graph stays behind
        // the SpeFileStore facade; ADR-009 Redis state). Orchestrator is Scoped (injects
        // scoped ISpeFileOperations); the hosted service resolves it via CreateScope.
        services.AddScoped<SpeSyncOrchestrator>();
        services.AddHostedService<SpeWebhookRenewalHostedService>();
        return services;
    }
}
