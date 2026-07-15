using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// A single association a rung proposes: ONE typed regarding lookup write, with the confidence and
/// provenance behind it. A rung may return several matches (e.g. participant correlation can match a
/// contact AND an organization AND an account). The engine applies the matches of the first rung that
/// yields any (deterministic cascade), writing each <see cref="RegardingFieldName"/> = <see cref="Target"/>.
/// </summary>
/// <remarks>
/// Per-attribute confidence + provenance (FR-09): confidence drives task 015's confidence→status +
/// auto-file (≥0.85 for deterministic rungs); provenance is a human-readable trace of how the match
/// was made. Neither is persisted by task 011 — the engine keeps the existing binary
/// Resolved/PendingReview status. Task 015 consumes <see cref="Confidence"/>.
/// </remarks>
public sealed record RungMatch
{
    /// <summary>
    /// The <c>sprk_communication</c> regarding lookup field to write (e.g. <c>sprk_regardingmatter</c>).
    /// Null for a <b>metadata-only</b> match — a structural signal (rung 3) that carries
    /// <see cref="Category"/>/<see cref="Obligations"/> without resolving a specific regarding entity.
    /// </summary>
    public string? RegardingFieldName { get; init; }

    /// <summary>
    /// The typed lookup target (correct <c>LogicalName</c> + id) written to <see cref="RegardingFieldName"/>.
    /// Null for a metadata-only match. The engine writes a regarding lookup only when BOTH this and
    /// <see cref="RegardingFieldName"/> are non-null.
    /// </summary>
    public EntityReference? Target { get; init; }

    /// <summary>Match confidence in [0,1].</summary>
    public required double Confidence { get; init; }

    /// <summary>Human-readable provenance of the match (e.g. <c>thread:in-reply-to→parent</c>, <c>sender:domain→organization</c>).</summary>
    public required string Provenance { get; init; }

    /// <summary>The rung that produced this match.</summary>
    public RungKind Rung { get; init; }

    /// <summary>
    /// Optional content category resolved by a structural detector (rung 3) — e.g. <c>event</c>,
    /// <c>invoice</c>, <c>esign-completion</c>, <c>court-notice</c>. Metadata for task 015 / W5
    /// Responsive Intelligence; not persisted by the deterministic rungs.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional obligations resolved by a structural detector (rung 3) — e.g. <c>executed-document</c>,
    /// <c>deadline-response</c>. Feed W5 Responsive Intelligence (FR-19); not persisted here.
    /// </summary>
    public IReadOnlyList<string> Obligations { get; init; } = Array.Empty<string>();
}
