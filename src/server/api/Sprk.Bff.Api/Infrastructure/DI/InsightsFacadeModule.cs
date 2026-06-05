namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Spaarke Insights Engine public facade (Zone A, the only Zone-A
/// surface Zone B code is permitted to import per <c>SPEC §3.5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>2026-06-04 audit Migration PR #1 — registrations RELOCATED</b>: per
/// <c>bff-ai-architecture-audit-r1</c> W4 §4.5 + DR-003 (LATENT BUG #1 Option A
/// remediation), the unconditional <c>IInsightsAi → InsightsOrchestrator</c> and
/// <c>IPlaybookExecutionEngine → PlaybookExecutionEngine</c> registrations that used
/// to live here have been MOVED into <c>AnalysisServicesModule.AddPublicContractsFacade</c>,
/// which only runs when the compound AI gate is ON
/// (<c>Analysis:Enabled=true AND DocumentIntelligence:Enabled=true</c>). Symmetric Null
/// peers (<c>NullInsightsAi</c>, <c>NullInvoiceAi</c>, <c>NullWorkspacePrefillAi</c>,
/// <c>NullRecordMatchingAi</c>) are registered by
/// <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c> when the compound gate
/// is OFF.
/// </para>
/// <para>
/// <b>Why this matters</b>: the prior asymmetric registration (facade unconditional, but
/// transitive ctor deps — <c>IPlaybookOrchestrationService</c>, <c>IOpenAiClient</c>,
/// <c>IInsightsPlaybookExecutionCache</c> — conditional behind the compound gate) violated
/// the Endpoint↔DI Registration Conditionality Symmetry Rule (audit W4 §4.1). Under
/// compound-AI-OFF, resolving <c>IInsightsAi</c> threw <see cref="System.InvalidOperationException"/>
/// instead of the contract-specified 503 <c>FeatureDisabledException</c> — surfacing as a
/// 500 to <c>/api/insights/ask</c> / <c>/api/insights/search</c> /
/// <c>/api/insights/assistant/query</c> callers.
/// </para>
/// <para>
/// <b>Why the extension method survives empty</b>: <c>Program.cs</c> calls
/// <see cref="AddInsightsFacadeModule"/> unconditionally; keeping the no-op stub avoids
/// touching <c>Program.cs</c> in PR #1 (which is intentionally surgical). A follow-up
/// cleanup PR may delete this file + the <c>Program.cs</c> call together.
/// </para>
/// </remarks>
public static class InsightsFacadeModule
{
    /// <summary>
    /// Stub. All prior registrations relocated to
    /// <c>AnalysisServicesModule.AddPublicContractsFacade</c> /
    /// <c>AddNullObjectsForCompoundOff</c> per the LATENT BUG #1 remediation. See the
    /// type-level remarks for context.
    /// </summary>
    public static IServiceCollection AddInsightsFacadeModule(this IServiceCollection services)
    {
        return services;
    }
}
