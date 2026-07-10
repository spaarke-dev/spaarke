namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Read-only INBOUND Organizational-scope provider seam (spaarke-ai-architecture-redesign-r2 task
/// AIR2-060, spec FR-B-11). Spaarke RECEIVES organizational context through this interface — it never
/// pushes context out. Work IQ is the named candidate provider; NO concrete runtime provider ships with
/// this interface (owner ruling 2026-07-08 defers the runtime provider + researcher spike). Outbound
/// Spaarke-as-MCP-server is explicitly OUT of scope for this seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — nothing on the platform resolves
/// inbound organizational context; the <see cref="OrganizationalSlice"/> on
/// <see cref="ContextEnvelope"/> (task 015) is hardcoded present-but-empty by
/// <see cref="ContextEnvelopeReferenceProducer"/>. (2) <i>Extension</i> — this is new inbound surface with
/// no existing facade to extend; the Context Binder (task 053, <see cref="Context.ContextBinder"/>) is the
/// natural single consumer, so the interface lives beside the other Binder-facing contracts in
/// <c>PublicContracts</c> rather than introducing a new folder. (3) <i>Cost-of-doing-nothing</i> — without
/// this seam, a future Work IQ integration would have to reopen <see cref="Context.ContextBinder"/> to add
/// the read path from scratch; this interface freezes the shape so that integration is a DI swap, not a
/// Binder rewrite.
/// </para>
/// <para>
/// <b>ADR-032 Null-Object</b>: r2 ships ONLY <see cref="NullOrganizationalContextProvider"/> — a P2 quiet
/// no-op (absence of a registered provider legitimately means "no organizational context this turn", not
/// an error state). No <c>FeatureDisabledException</c> / 503 semantics apply here: this is not a feature
/// kill-switch, it is the every-day default until a real provider is wired.
/// </para>
/// <para>
/// <b>Inbound-only, by construction</b>: the interface exposes exactly one method, a read
/// (<see cref="GetContextAsync"/>). There is no push/send/publish/notify method on this surface — adding
/// one would turn this into an outbound Spaarke-as-MCP-server seam, which is explicitly out of scope
/// (see the interface-shape test asserting inbound-only in
/// <c>tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PublicContracts/NullOrganizationalContextProviderTests.cs</c>).
/// </para>
/// </remarks>
public interface IOrganizationalContextProvider
{
    /// <summary>
    /// Resolve inbound organizational context for the given <paramref name="request"/>. Returns
    /// <see cref="OrganizationalContextResult.Empty"/> (or an equivalent empty result) when no
    /// organizational context is available for the caller — that is a normal outcome, never a thrown
    /// failure. The Null-Object default (<see cref="NullOrganizationalContextProvider"/>) always returns
    /// the empty result.
    /// </summary>
    Task<OrganizationalContextResult> GetContextAsync(
        OrganizationalContextRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs for an <see cref="IOrganizationalContextProvider"/> read. Minimal anchor for r2 — a future
/// Work IQ provider resolves organizational context (org chart, team, reporting line, etc.) from the
/// caller's identity. Additive-only evolution: new optional fields may be added later without breaking
/// the Null-Object default.
/// </summary>
public sealed record OrganizationalContextRequest
{
    /// <summary>Server-resolved caller contact id (claims→contact) — the identity anchor a real provider resolves organizational context from.</summary>
    public string? CallerContactId { get; init; }
}

/// <summary>
/// Result of an <see cref="IOrganizationalContextProvider"/> read. Counts-only in r2 (mirrors the
/// <see cref="ContextEnvelope"/> NFR-07 counts-only posture) — a real provider's richer content shape is
/// intentionally left for the provider that actually ships (Work IQ integration is deferred), so this
/// contract does not guess at a shape nobody has validated yet.
/// </summary>
public sealed record OrganizationalContextResult
{
    /// <summary>Whether this result came from a real registered provider (Work IQ) rather than the Null-Object default.</summary>
    public bool ProviderImplemented { get; init; }

    /// <summary>Count of organizational facts/records this result carries (counts only — NFR-07 posture).</summary>
    public int ReferenceCount { get; init; }

    /// <summary>The empty result: no provider implemented, zero references. Returned by <see cref="NullOrganizationalContextProvider"/> on every call.</summary>
    public static readonly OrganizationalContextResult Empty = new()
    {
        ProviderImplemented = false,
        ReferenceCount = 0,
    };
}

/// <summary>
/// Null-Object default for <see cref="IOrganizationalContextProvider"/> (ADR-032 P2 quiet no-op). Returned
/// by <see cref="Context.ContextBinder"/> internally when no real provider is supplied/registered, and
/// registered explicitly in DI (<c>AnalysisServicesModule</c>) so a future Work IQ provider is a one-line
/// swap. Absence of organizational context is the everyday default, not a failure — this type never
/// throws.
/// </summary>
public sealed class NullOrganizationalContextProvider : IOrganizationalContextProvider
{
    /// <inheritdoc />
    public Task<OrganizationalContextResult> GetContextAsync(
        OrganizationalContextRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(OrganizationalContextResult.Empty);
}
