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
        // DocxAnnotationWriter RETIRED (task 036, §6.5 Path B): the text-anchored push-annotations WRITE
        // surface it byte-authored was retired entirely — the LAST text-search byte-author is gone, so I-5
        // (one byte-author = ComposeShadowPatchEngine) + I-7 (no write-path text-search) now hold literally.
        // Grep shows zero remaining call sites. The read-direction DocxAnnotationReader (unregistered, used
        // directly by the pull/reanchor endpoints) is unaffected.
        services.AddSingleton<ParaIdPreParser>();                               // FR-08 (task 010, E2) — pure OOXML w14:paraId collect/mint pre-parse; thread-safe stateless singleton (ADR-010). Consumed by ComposeService.LoadAsync; registered UNCONDITIONALLY (same lifetime as its consumer — symmetric per bff-extensions.md §F.1, no feature gate)
        // ComposeParagraphRedlineSynthesizer RETIRED (task 032, FR-06 write-path cutover): the paragraph-diff
        // save path is superseded by the ID-anchored ComposeShadowPatchEngine below. Its DI registration is
        // removed with the class; grep shows zero remaining call sites.
        services.AddSingleton<ComposeDocumentRenderer>();                       // FR-01a/FR-27 (task 026, E1 born-in-editor) — pure from-scratch high-fidelity OOXML AUTHORING engine (ComposeContentModel -> byte[]): real styles + style-linked multi-level numbering (no direct-numId double-numbering) + native tables + minted w14:paraId. The server-side replacement for the removed client docx.js exporter (task 027) — makes the server the single authority for all .docx authoring. Deterministic OOXML authoring, NOT an AI dispatch (ADR-039 complied — design §11). No external NuGet (DocumentFormat.OpenXml already referenced). Consumed by ComposeService.SaveAsync create-path; thread-safe stateless singleton (ADR-010); registered UNCONDITIONALLY (symmetric per bff-extensions.md §F.1)
        services.AddSingleton<ComposeShadowPatchEngine>();                      // FR-04/FR-11/D5 (task 030) — the SINGLE unified byte-author of the Shadow Document Architecture: applies an ID-anchored ComposeOperationLog (paraId+runIndex+run-local-offset) surgically onto retained OOXML (byte[]->byte[]), emitting native w:ins/w:del/w:comment with ZERO write-path text-search (I-7). Migrates the EDGE-1..4 wisdom from DocxAnnotationWriter (comment-before-trackchange ordering; w:delText; monotonic revision-id seeding). Consolidates the retired ComposeParagraphRedlineSynthesizer + the save-path use of DocxAnnotationWriter — text/mark/comment ops + core resolve-split-emit + structural paragraph ops (task 031). ROUTED through ComposeService.SaveAsync (task 032, FR-06 cutover); DocxAnnotationWriter stays only for the push-annotations surface (task 036). No external NuGet (DocumentFormat.OpenXml already referenced; task-005 A/B rejected Docxodus). Pure — no AI internals (ADR-013 Tier-1), no Microsoft.Graph (ADR-007); thread-safe stateless singleton (ADR-010); registered UNCONDITIONALLY (symmetric per bff-extensions.md §F.1)
        services.AddSingleton<ComposeDocxProjectionBuilder>();                  // Phase-1 mammoth removal (design notes/design-server-side-docx-html-conversion.md) — pure single-walk DOCX->editor projection (byte[]->paraId-tagged HTML + ordered paraId map + status/warnings): the ONE server engine that assigns w14:paraId and emits the editor block from the same paragraph instance, eliminating the mammoth-vs-OOXML two-engine drift. No external NuGet (DocumentFormat.OpenXml already referenced). Consumed by ComposeService.LoadAsync (supersedes ParaIdPreParser on the Load path); thread-safe stateless singleton (ADR-010); registered UNCONDITIONALLY (symmetric per bff-extensions.md §F.1)

        // R2 W1 SPE change-detection (FR-26, task 052) — subscription state machine +
        // BackgroundService renewal (ADR-001 hosted service; ADR-007 Graph stays behind
        // the SpeFileStore facade; ADR-009 Redis state). Orchestrator is Scoped (injects
        // scoped ISpeFileOperations); the hosted service resolves it via CreateScope.
        services.AddScoped<SpeSyncOrchestrator>();
        services.AddHostedService<SpeWebhookRenewalHostedService>();
        return services;
    }
}
