using Sprk.Bff.Api.Services.Ai.Insights.Extraction;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Insights Engine Zone A extraction pipeline post-processing primitives
/// (D-P10 today; D-P9 / D-P12 nodes layered on top in later Wave-3 tasks).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone</b>: A per <c>SPEC §3.5</c> — lives under <c>Services/Ai/Insights/Extraction/</c>
/// and is permitted to import AI internals freely. Zone B code (CRUD, endpoints, dispatch
/// boundaries) MUST NOT take a direct dependency on these registrations — go through
/// <c>Services/Ai/PublicContracts/IInsightsAi</c> (added in Wave 5 task 042).
/// </para>
/// <para>
/// <b>ADR-010 compliance</b>:
/// <list type="bullet">
///   <item>New feature module rather than extending <c>AiModule</c> (which is at the 15/15
///   unconditional cap per its inline audit). Per ADR-010 feature-module pattern.</item>
///   <item>Single interface seam: <see cref="IObservationEmitter"/>. Justified per ADR-010
///   §Exceptions: three concrete consumers planned (D-P7 universal ingest playbook orchestrator,
///   D-P14 synthesis playbook integration tests, future D-62 targeted re-extraction tooling),
///   and the test seam is load-bearing for Wave-3 unit testing without an LLM dependency.</item>
///   <item>Options pattern via <see cref="ConfidenceThresholdOptions"/> with
///   <c>IOptionsMonitor</c> binding — admin-tunable per D-63 without a BFF restart.</item>
/// </list>
/// </para>
/// <para>
/// <b>Configuration section</b>: <c>Insights:Extraction:ConfidenceThresholds</c> (see
/// <see cref="ConfidenceThresholdOptions.SectionName"/>). Defaults match the Phase 1 starter
/// thresholds in <c>SPEC-phase-1-minimum.md §3.4</c>; admins override per-field at deploy time
/// or live via Key Vault / appsettings reload.
/// </para>
/// </remarks>
public static class InsightsExtractionModule
{
    /// <summary>
    /// Registers the Insights Engine Zone A extraction primitives. Call from
    /// <c>Program.cs</c> alongside the other AI feature modules (typically immediately after
    /// <c>AddAnalysisServicesModule</c>).
    /// </summary>
    public static IServiceCollection AddInsightsExtractionModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // ConfidenceThresholdOptions — admin-tunable per-field thresholds per D-63.
        // IOptionsMonitor binding means appsettings reloads / Key Vault refresh propagate
        // without a BFF restart; ObservationEmitter reads the snapshot per call.
        // Validation deferred (no ValidateOnStart): the option has data-annotation on
        // DefaultThreshold but is functional with all defaults — no missing config can break
        // startup. The validation runs on first read via ValidateDataAnnotations().
        services
            .AddOptions<ConfidenceThresholdOptions>()
            .Bind(configuration.GetSection(ConfidenceThresholdOptions.SectionName))
            .ValidateDataAnnotations();

        // IObservationEmitter — D-P10 third mechanical gate (per-field threshold gating +
        // per-field Observation emission per SPEC-phase-1-minimum.md §3.4).
        // Singleton: stateless; IOptionsMonitor + ILogger are both thread-safe.
        services.AddSingleton<IObservationEmitter, ObservationEmitter>();

        return services;
    }
}
