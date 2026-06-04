using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Insights;

/// <summary>
/// Reference to a piece of evidence supporting an <see cref="InsightArtifact"/>.
/// Per SPEC §3.4.1 worked example, evidence refs come in several flavors distinguished by <see cref="RefType"/>:
/// <list type="bullet">
///   <item><c>fact-source</c> — pointer to authoritative data (e.g., <c>dataverse://sprk_matter/M-1234#totalSpend</c>)</item>
///   <item><c>document</c> — pointer to source document (e.g., <c>spe://drive/abc/item/xyz</c>); typically carries a <see cref="Quote"/></item>
///   <item><c>comparable-matter</c> — pointer to another matter (e.g., <c>matter://M-0567</c>)</item>
///   <item><c>supporting-matter</c> — Precedent supporting-matter reference (e.g., <c>matter://M-2024-0341</c>)</item>
///   <item><c>playbook-run</c> — pointer to the playbook execution that produced the artifact (e.g., <c>playbook://outcome-extraction@v1/run-2026-04-12T08:30:00Z</c>)</item>
/// </list>
/// Zone B POCO per SPEC §3.5 — pure record, no AI internals imports.
/// </summary>
public sealed record EvidenceRef
{
    /// <summary>The kind of reference. Free-form string; see remarks for canonical values.</summary>
    [JsonPropertyName("refType")]
    public required string RefType { get; init; }

    /// <summary>The reference URI/identifier (scheme-prefixed, e.g., <c>spe://...</c>, <c>matter://...</c>).</summary>
    [JsonPropertyName("ref")]
    public required string Ref { get; init; }

    /// <summary>
    /// Optional verbatim quote from the referenced document. Populated for <c>document</c> refs
    /// emitted by Layer 2 outcome extraction (D-P6) so <c>GroundingVerifier</c> (D-P9) can verify
    /// the quote exists in the source via substring/sliding-window match.
    /// </summary>
    [JsonPropertyName("quote")]
    public string? Quote { get; init; }
}
