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
    /// <summary>The <c>sprk_communication</c> regarding lookup field to write (e.g. <c>sprk_regardingmatter</c>).</summary>
    public required string RegardingFieldName { get; init; }

    /// <summary>The typed lookup target (correct <c>LogicalName</c> + id) written to <see cref="RegardingFieldName"/>.</summary>
    public required EntityReference Target { get; init; }

    /// <summary>Match confidence in [0,1]. Deterministic exact matches (rungs 0–3) are 1.0 in task 011.</summary>
    public required double Confidence { get; init; }

    /// <summary>Human-readable provenance of the match (e.g. <c>thread:in-reply-to→parent</c>, <c>sender:domain→organization</c>).</summary>
    public required string Provenance { get; init; }

    /// <summary>The rung that produced this match.</summary>
    public RungKind Rung { get; init; }
}
