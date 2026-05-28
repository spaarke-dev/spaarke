using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Phase 1 stub implementation of <see cref="ILiveFactResolver"/>. Always throws
/// <see cref="LiveFactNotSupportedException"/> so any production call surfaces a clear
/// "not implemented in Phase 1" message in App Insights.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a stub?</b> The full <c>DataverseLiveFactResolver</c> (which queries
/// <c>sprk_matter</c> + related rows for predicates like <c>totalSpend</c>,
/// <c>matterType</c>, <c>matterDurationDays</c>) lands with D-P7 (task 040 — universal
/// ingest playbook) per SPEC §3.1. Wiring the interface + a stub in task 022 lets
/// <see cref="Sprk.Bff.Api.Services.Ai.Nodes.LiveFactNode"/> register cleanly in DI and
/// lets task 060 (D-P14 predict-matter-cost playbook) author against the real surface
/// — when task 040 swaps the implementation, no LiveFactNode-consumer wiring changes.
/// </para>
/// <para>
/// <b>Tests</b> inject their own <see cref="ILiveFactResolver"/> directly into
/// <c>LiveFactNode</c>; they do not consume this stub.
/// </para>
/// <para>
/// <b>Zone B</b> per SPEC §3.5 — lives under <c>Services/Insights/LiveFacts/</c>.
/// </para>
/// </remarks>
internal sealed class StubLiveFactResolver : ILiveFactResolver
{
    public Task<FactArtifact?> ResolveAsync(
        string subject,
        string predicate,
        string tenantId,
        CancellationToken cancellationToken)
    {
        throw new LiveFactNotSupportedException(
            $"StubLiveFactResolver (Phase 1): subject='{subject}' predicate='{predicate}'. " +
            "The real DataverseLiveFactResolver lands with D-P7 task 040.");
    }
}
