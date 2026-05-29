using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Test-only stub implementation of <see cref="ILiveFactResolver"/>. Always throws
/// <see cref="LiveFactNotSupportedException"/> so any accidental production wiring
/// surfaces a clear "not implemented" message immediately.
/// </summary>
/// <remarks>
/// <para>
/// <b>Production uses <see cref="DataverseLiveFactResolver"/></b> (task 071, Wave 8.5
/// pre-deploy gap fix, 2026-05-29) — see
/// <c>Infrastructure/DI/InsightsModule.cs</c>. This stub is retained for two reasons:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Documented fallback</b>: if a future test composition root explicitly opts
///     out of the real Dataverse-backed resolver (e.g., a unit test that needs to
///     verify <see cref="Sprk.Bff.Api.Services.Ai.Nodes.LiveFactNode"/> handles the
///     unsupported-predicate error path), it can register this stub via
///     <c>services.AddSingleton&lt;ILiveFactResolver, StubLiveFactResolver&gt;()</c>
///     in test setup.
///   </item>
///   <item>
///     <b>Safety net</b>: a hard-fail implementation behind the interface guarantees
///     that an accidental composition error (e.g., a misordered <c>AddInsightsModule</c>)
///     surfaces as a clear LiveFactNotSupportedException rather than a silent default.
///   </item>
/// </list>
/// <para>
/// <b>Standard test pattern</b>: most tests inject their own <see cref="ILiveFactResolver"/>
/// mock directly into <see cref="Sprk.Bff.Api.Services.Ai.Nodes.LiveFactNode"/> rather
/// than using this stub.
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
            $"StubLiveFactResolver (test-only): subject='{subject}' predicate='{predicate}'. " +
            "Production uses DataverseLiveFactResolver (task 071, Wave 8.5). " +
            "If you see this in production, InsightsModule DI registration is misconfigured.");
    }
}
