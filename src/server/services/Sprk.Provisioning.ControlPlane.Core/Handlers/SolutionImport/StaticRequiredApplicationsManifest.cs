// -----------------------------------------------------------------------------
// StaticRequiredApplicationsManifest.cs
//
// HANDLER-07 (Wave 2 pre-dispatch remediation 2026-08-27) — F13 verbatim.
// Default <see cref="IRequiredApplicationsManifest"/> implementation
// serving a hard-coded canonical list. Upgrades to YAML-file-backed via
// FileRequiredApplicationsManifest (future incremental change) without
// touching consumers; the static ships the R1 known-required set so H6
// works on day 1 without an additional deployment file.
//
// CANONICAL SET (R1):
//   - msft_PowerBI_Anchor — F13 verbatim: fresh Production-tier Dataverse
//     envs lack this by default; SpaarkeMaster env-var carries a
//     dependency on `powerbimashupparameter` → import fails
//     MissingDependency 5 min in.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Static <see cref="IRequiredApplicationsManifest"/> serving the R1
/// canonical required-applications list. Replace with a
/// file-backed impl when the list grows / diverges per environment.
/// </summary>
public sealed class StaticRequiredApplicationsManifest : IRequiredApplicationsManifest
{
    /// <summary>
    /// R1 canonical list. F13 verbatim: msft_PowerBI_Anchor is required by
    /// SpaarkeMaster's env-var dep on powerbimashupparameter.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultRequiredApplicationNames =
        new[] { "msft_PowerBI_Anchor" };

    /// <inheritdoc/>
    public IReadOnlyList<string> RequiredApplicationNames => DefaultRequiredApplicationNames;
}
