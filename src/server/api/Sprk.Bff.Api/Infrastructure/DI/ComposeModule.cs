using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Dependency injection module for the Compose drafting workspace (spaarkeai-compose-r1).
/// </summary>
/// <remarks>
/// <para>
/// Registers the three Compose orchestration services that back the <c>/api/compose/*</c>
/// endpoint surface (<see cref="Sprk.Bff.Api.Api.ComposeEndpoints"/>):
/// <list type="bullet">
///   <item><see cref="IComposeService"/> → <see cref="ComposeService"/> — load/save/promote orchestration (FR-04/05/06).</item>
///   <item><see cref="IComposeDocumentService"/> → <see cref="ComposeDocumentService"/> — SPE drive-item DOCX read/write plumbing (FR-04/05).</item>
///   <item><see cref="ComposeSessionService"/> — ChatSession DocumentId binding facade (FR-07). Concrete registration (ADR-010 strict — interface collapsed 2026-06-29 cleanup).</item>
/// </list>
/// </para>
/// <para>
/// <b>UNCONDITIONAL registration (binding per <c>.claude/constraints/bff-extensions.md</c> §F.1)</b>:
/// these three services are registered without any feature-flag wrapping. The endpoint
/// mapping in <see cref="Sprk.Bff.Api.Api.ComposeEndpoints.MapComposeEndpoints"/> is also
/// unconditional, so the DI side MUST match — otherwise we reproduce the RB-T028-03/04/05/06
/// asymmetric-registration anti-pattern (services missing while endpoints map → 500 on first
/// request). Compose R1 has no feature gates per project CLAUDE.md.
/// </para>
/// <para>
/// <b>Scoped lifetime (per ADR-010)</b>: matches the existing BFF convention for orchestration
/// services that participate in per-request auth scope (OBO HttpContext, IGenericEntityService).
/// Mirrors the lifetime of <see cref="ChatSessionManager"/> which <see cref="ComposeSessionService"/>
/// wraps.
/// </para>
/// <para>
/// <b>ADR-013 facade boundary (refined 2026-05-20)</b>: the three Compose services are CRUD
/// orchestration code. Their constructors inject only <c>IGraphClientFactory</c>,
/// <c>IGenericEntityService</c>, <c>ChatSessionManager</c>, and the two sibling Compose
/// interfaces — no <c>IOpenAiClient</c> / <c>IPlaybookService</c> / other AI-internal types.
/// The Compose AI-dispatch path (<c>POST /api/compose/action/{consumerType}</c>) takes its
/// AI dependencies (<c>IConsumerRoutingService</c> + <c>IInvokePlaybookAi</c>) directly at
/// the endpoint composition root from <c>Services/Ai/PublicContracts/</c>, not via these
/// services. Verified by static scan in task 025.
/// </para>
/// </remarks>
public static class ComposeModule
{
    /// <summary>
    /// Adds the Compose drafting workspace services to the DI container. Idempotent — safe
    /// to call once during <see cref="WebApplication"/> startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddComposeModule(this IServiceCollection services)
    {
        // ============================================================================
        // Compose orchestration services — UNCONDITIONAL registration per bff-extensions.md §F.1.
        // ============================================================================
        // Endpoint mapping in ComposeEndpoints.MapComposeEndpoints is unconditional; the
        // service registrations below MUST be unconditional to match. NO feature flag
        // wrapping. Compose R1 has no feature gates per project CLAUDE.md.
        services.AddScoped<IComposeDocumentService, ComposeDocumentService>();
        services.AddScoped<ComposeSessionService>(); // Concrete (ADR-010 strict; interface collapsed 2026-06-29 cleanup)
        services.AddScoped<IComposeService, ComposeService>();

        // ============================================================================
        // Stale checkout sweeper (Spaarke Compose R1 — Spike #3 §4.3, task 052)
        // ============================================================================
        // Background service that releases SPE checkouts whose client-side heartbeat
        // has gone stale (15-min threshold, 2-min scan interval, ≤17-min orphan ceiling).
        //
        // ADR-001 BackgroundService pattern. UNCONDITIONAL registration matching the
        // heartbeat endpoint at POST /api/compose/document/{id}/heartbeat (which is also
        // unconditional in ComposeEndpoints). The companion DocumentCheckoutService is
        // already registered in DocumentsModule.AddDocumentsModule via AddHttpClient<>;
        // the sweeper resolves it per iteration through a scoped IServiceProvider.
        services.AddHostedService<StaleCheckoutSweeperHostedService>();

        return services;
    }
}
