// -----------------------------------------------------------------------------
// SpaarkePackage.cs
//
// T218b (ADR-027 §3-§4, amended 2026-10-07). The Spaarke Dataverse package is
// ONE solution, SpaarkeMaster — its scope is defined by rule in
// docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md. Replaces the 9-entry
// CanonicalSolutionCatalog (6 of whose solutions existed nowhere).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>The one Dataverse package H6 imports into every customer environment.</summary>
public static class SpaarkePackage
{
    /// <summary>Dataverse solution unique name of the package (publisher <c>Spaarke</c>, prefix <c>sprk</c>).</summary>
    public const string SolutionUniqueName = "SpaarkeMaster";

    /// <summary>
    /// Compares two Dataverse solution versions (<c>major.minor.build.revision</c>). Returns <c>null</c> when either is
    /// not a version — callers refuse rather than guess. Missing parts count as 0 (<c>1.2</c> equals <c>1.2.0.0</c>).
    /// </summary>
    public static int? CompareVersions(string? left, string? right)
        => Version.TryParse(left?.Trim(), out var l) && Version.TryParse(right?.Trim(), out var r)
            ? Normalize(l).CompareTo(Normalize(r))
            : null;

    private static Version Normalize(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
