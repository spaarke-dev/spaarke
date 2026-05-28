using Sprk.Bff.Api.Services.Ai.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Spaarke Insights Engine public facade (Zone A, the only Zone-A
/// surface Zone B code is permitted to import per <c>SPEC §3.5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>: registers <see cref="IInsightsAi"/> → <see cref="InsightsOrchestrator"/>
/// (task 042). The orchestrator wires <see cref="IPlaybookExecutionEngine"/> +
/// <see cref="IInsightsPlaybookExecutionCache"/> (D-P13, task 023) +
/// <see cref="IOpenAiClient"/> behind the facade so Zone B callers
/// (D-P8 SPE-upload consumer, D-P15 <c>/api/insights/ask</c> endpoint, D-P4 Precedent
/// projection sync) never import any of those internal types.
/// </para>
/// <para>
/// <b>ADR-010 compliance</b>:
/// <list type="bullet">
///   <item>New feature module rather than extending <see cref="AiModule"/> (which is at
///   the 15/15 unconditional cap per its inline audit). Per ADR-010 feature-module pattern.</item>
///   <item>Single interface seam (<see cref="IInsightsAi"/>) justified per ADR-010
///   §Exceptions: this IS the §3.5 facade boundary — Zone B callers cannot import
///   the concrete <see cref="InsightsOrchestrator"/> directly (it lives in Zone A and
///   imports AI internals), so the interface is structural-not-optional.</item>
///   <item>Singleton lifetime — <see cref="InsightsOrchestrator"/> is stateless; all
///   dependencies are thread-safe (engine resolves per-request HttpContext internally;
///   cache + IOpenAiClient are themselves singletons or thread-safe scoped).</item>
/// </list>
/// </para>
/// <para>
/// <b>Why a separate module from <see cref="InsightsExtractionModule"/></b>: that module
/// registers Zone A extraction post-processing primitives (D-P10 ObservationEmitter,
/// D-P5 Layer1ClassificationEmitter). This module registers the public facade. Keeping
/// them separate makes the §3.5 boundary visible in DI composition and lets each
/// module evolve independently (extraction grows as new Layer N's land; facade stays
/// stable at 3 methods through Phase 1).
/// </para>
/// <para>
/// <b>Prerequisites</b> (must already be registered when <see cref="AddInsightsFacadeModule"/>
/// is called):
/// <list type="bullet">
///   <item><see cref="IPlaybookExecutionEngine"/> — registered by AnalysisServicesModule</item>
///   <item><see cref="IInsightsPlaybookExecutionCache"/> — registered by task 023 in
///   AnalysisServicesModule</item>
///   <item><see cref="IOpenAiClient"/> — registered in Program.cs when
///   <c>DocumentIntelligence:Enabled = true</c></item>
/// </list>
/// </para>
/// </remarks>
public static class InsightsFacadeModule
{
    /// <summary>
    /// Registers the Spaarke Insights Engine public facade. Call from
    /// <c>Program.cs</c> after <c>AddAnalysisServicesModule</c> (which registers
    /// <see cref="IPlaybookExecutionEngine"/> + <see cref="IInsightsPlaybookExecutionCache"/>).
    /// </summary>
    public static IServiceCollection AddInsightsFacadeModule(this IServiceCollection services)
    {
        // IInsightsAi — the only Zone-A surface Zone B code may import per SPEC §3.5.
        // Singleton: stateless wrapper over thread-safe dependencies (engine, cache,
        // IOpenAiClient). Per ADR-010 §Exceptions the interface seam is justified —
        // this IS the §3.5 boundary, not an over-abstraction.
        services.AddSingleton<IInsightsAi, InsightsOrchestrator>();

        return services;
    }
}
