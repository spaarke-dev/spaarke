using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Resolves a deterministic <b>Live Fact</b> about a Dataverse subject by reading the system of
/// record directly (no projection writing). Per SPEC §3.1 D-P12 and design.md §2.1 — Live Facts
/// are computed on read, never stored in <c>spaarke-insights-index</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary</b>: Zone B per SPEC §3.5 — this interface lives under
/// <c>Services/Insights/LiveFacts/</c> and consumes only <c>IDataverseService</c>. Zone A code
/// (<c>Services/Ai/Nodes/LiveFactNode</c>) injects this interface for evidence-bearing playbooks.
/// </para>
/// <para>
/// <b>Phase 1 vs Phase 1.5</b>: per the Q5 audit + SPEC §3.1 D-P12 (LiveFactNode subset),
/// Phase 1 ships the interface + a minimal <c>DataverseLiveFactResolver</c> that supports the
/// predicates needed by <c>predict-matter-cost</c> (D-P14, task 060) — e.g.,
/// <c>matter:M-1234.totalSpend</c>, <c>matter:M-1234.matterType</c>. Additional predicates
/// land as the synthesis playbook portfolio grows. The interface signature is intentionally
/// generic (predicate + subject + value JSON) so new predicates do NOT require interface changes.
/// </para>
/// <para>
/// <b>Confidence</b>: returned <see cref="FactArtifact"/> always carries
/// <see cref="FactArtifact.Confidence"/> = 1.0 per design.md §2.1.
/// </para>
/// </remarks>
public interface ILiveFactResolver
{
    /// <summary>
    /// Resolves a Live Fact for the given subject + predicate.
    /// </summary>
    /// <param name="subject">
    /// Scheme-prefixed subject identifier per SPEC §3.4 worked examples (e.g.,
    /// <c>matter:M-1234</c>, <c>document:abc</c>). The scheme determines which Dataverse entity
    /// the resolver queries.
    /// </param>
    /// <param name="predicate">
    /// The claim name (e.g., <c>totalSpend</c>, <c>matterType</c>, <c>matterDurationDays</c>).
    /// Must be one of the predicates the implementation supports; unsupported predicates throw
    /// <see cref="LiveFactNotSupportedException"/>.
    /// </param>
    /// <param name="tenantId">Tenant identifier (propagated into the returned <see cref="FactArtifact.TenantId"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="FactArtifact"/> with <see cref="FactArtifact.Confidence"/> = 1.0, populated
    /// <see cref="InsightArtifact.Value"/> + <see cref="InsightArtifact.Evidence"/>
    /// (<c>fact-source</c> evidence pointing at the Dataverse record + field) per SPEC §3.4.1.
    /// Returns <c>null</c> when the subject does not exist in Dataverse.
    /// </returns>
    /// <exception cref="LiveFactNotSupportedException">
    /// The (subject scheme, predicate) pair is not supported by this resolver.
    /// </exception>
    Task<FactArtifact?> ResolveAsync(
        string subject,
        string predicate,
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when an <see cref="ILiveFactResolver"/> is asked for a (subject-scheme, predicate)
/// pair it does not implement. Per D-P12 LiveFactNode, this surfaces as a node-level
/// validation failure so playbook authors see misconfigured predicates immediately.
/// </summary>
public sealed class LiveFactNotSupportedException : Exception
{
    public LiveFactNotSupportedException(string message) : base(message) { }

    public LiveFactNotSupportedException(string subject, string predicate)
        : base($"LiveFactResolver does not support predicate '{predicate}' on subject '{subject}'.")
    {
        Subject = subject;
        Predicate = predicate;
    }

    public string? Subject { get; }
    public string? Predicate { get; }
}
