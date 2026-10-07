// -----------------------------------------------------------------------------
// ImportedSolutionSet.cs
//
// Task 245b (G25). The value H13 writes to the registry column
// sprk_solutionversion, derived from H6's InterStepState.ImportedSolutions.
// The /provision-environment Step 6a registry fallback recomputes it in
// PowerShell — keep the two in step (H13BuildPromotedColumnsTests pins a vector).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Task 245b: the version of an imported solution SET — what H13 writes to the registry column
/// <c>sprk_solutionversion</c> (String 50) from H6's <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.ImportedSolutions"/>.
/// </summary>
/// <remarks>
/// The set has no release tag of its own yet: the solution artifact manifest H6 imports from
/// (<c>dataverse-solutions-latest.json</c>) carries per-solution versions only, and the
/// version-compatibility matrix's <c>S&lt;YYYY&gt;.&lt;MM&gt;</c> tag has no producer. The value is
/// therefore a content fingerprint of (unique name, version) pairs — the first 32 hex digits of
/// <see cref="ArtifactVersion"/> over the sorted pairs. Identical sets give identical values in every
/// environment (solution ids are deliberately excluded — they differ per environment); any version
/// change gives a new value.
/// </remarks>
public static class ImportedSolutionSet
{
    /// <summary>Hex digits kept from the SHA-256 — fits <c>sprk_solutionversion</c> (50 chars) with room to spare.</summary>
    public const int FingerprintLength = 32;

    /// <summary>The set version, or <c>null</c> when nothing was imported.</summary>
    public static string? ComputeVersion(IEnumerable<ImportedSolutionRecord>? solutions)
    {
        var pairs = (solutions ?? Array.Empty<ImportedSolutionRecord>())
            .Where(s => !string.IsNullOrWhiteSpace(s.SolutionUniqueName))
            .Select(s => $"{s.SolutionUniqueName.Trim()}={s.Version?.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        return pairs.Count == 0 ? null : ArtifactVersion.Of(string.Join('\n', pairs))[..FingerprintLength];
    }
}
