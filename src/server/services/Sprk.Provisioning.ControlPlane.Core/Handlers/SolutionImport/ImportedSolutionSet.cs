// -----------------------------------------------------------------------------
// ImportedSolutionSet.cs
//
// Task 245b (G25); T218b. The value H13 writes to the registry column
// sprk_solutionversion, derived from H6's InterStepState.ImportedSolutions.
// The /provision-environment Step 6a registry fallback recomputes it in
// PowerShell — keep the two in step (H13BuildPromotedColumnsTests pins a vector).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// The installed package as the registry records it — what H13 writes to <c>sprk_solutionversion</c> (String 50) from
/// H6's <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.ImportedSolutions"/>.
/// </summary>
/// <remarks>
/// T218b (ADR-027 §3, amended 2026-10-07): the package is one solution, so its own version is the release tag and the
/// value is readable: <c>SpaarkeMaster 1.2.0.0 (managed)</c>. It also records the package type on the registry row
/// (owner D8) without a new column. Task 245b's 32-hex fingerprint served a multi-solution set with no release tag;
/// that set no longer exists.
/// </remarks>
public static class ImportedSolutionSet
{
    /// <summary>
    /// <c>{SpaarkeMaster} {version} ({managed|unmanaged})</c>, or <c>null</c> when SpaarkeMaster is not among the records
    /// (nothing imported, or a run recorded before T218b).
    /// </summary>
    public static string? ComputeVersion(IEnumerable<ImportedSolutionRecord>? solutions)
    {
        var package = (solutions ?? Array.Empty<ImportedSolutionRecord>())
            .FirstOrDefault(s => string.Equals(s.SolutionUniqueName?.Trim(), SpaarkePackage.SolutionUniqueName,
                StringComparison.OrdinalIgnoreCase));
        if (package is null || string.IsNullOrWhiteSpace(package.Version))
        {
            return null;
        }
        return $"{SpaarkePackage.SolutionUniqueName} {package.Version.Trim()} ({(package.IsManaged ? "managed" : "unmanaged")})";
    }
}
