// -----------------------------------------------------------------------------
// IRequiredApplicationsInstaller.cs
//
// HANDLER-07 (Wave 2 pre-dispatch remediation 2026-08-27) — F13 verbatim
// absorption. Fresh Production-tier Dataverse environments do NOT include
// Power BI Extensions by default. SpaarkeMaster environment variable
// carries a spurious dependency on `powerbimashupparameter` → solution
// import fails 5 min in with 1 unresolved MissingDependency. This seam
// gates H6 on a pre-install of required Power Platform applications
// (canonical list: msft_PowerBI_Anchor for R1) BEFORE
// CanonicalSolutionCatalog resolves.
//
// PRODUCTION IMPL:
//   <see cref="PacRequiredApplicationsInstaller"/> shells out to
//   <c>pac application install</c> per required app + polls until the
//   Dataverse install-status reports "installed" (typical wall-clock
//   ~6 min per PowerBI Anchor). For Wave 2 the production impl ships as
//   a scaffold that returns Success unconditionally with a "not-yet-live"
//   diagnostic in the logs; the interface + wiring + tests + manifest
//   land here so a future incremental change can drop in the real shell-
//   out without touching H6.
//
// MANIFEST:
//   Canonical list of required-application names ships in
//   <c>scripts/canonical-solutions/required-applications.yaml</c>. Read
//   by <see cref="IRequiredApplicationsManifest"/> at process start.
//   Adding a new required application = one manifest line (no code change).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls from day 1: PacRequiredApplicationsInstaller (production
//   scaffold) + test fakes injected per unit test.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Installs the canonical Power Platform applications (Power BI Anchor,
/// etc.) H6 depends on BEFORE any solution import fires. Fresh
/// Production-tier envs lack these by default; missing them = spurious
/// MissingDependency failures 5 min into solution import.
/// </summary>
public interface IRequiredApplicationsInstaller
{
    /// <summary>
    /// Ensures every application named in
    /// <paramref name="requiredApplicationNames"/> is installed on the
    /// target Dataverse env. Idempotent — no-op if all apps are already
    /// installed. Domain outcomes never throw; only unanticipated
    /// infrastructure faults propagate.
    /// </summary>
    Task<RequiredApplicationsInstallOutcome> EnsureInstalledAsync(
        RequiredApplicationsInstallRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Input for <see cref="IRequiredApplicationsInstaller.EnsureInstalledAsync"/>.</summary>
/// <param name="TenantId">Entra tenant id (§4D I1 — must be explicit).</param>
/// <param name="ClientId">BFF app-reg id for pac auth.</param>
/// <param name="ClientSecret">BFF app-reg secret for pac auth (may be null on secret-free envs — impl selects credential per SolutionImportOptions.Credentials).</param>
/// <param name="TargetDataverseUrl">Customer Dataverse env URL (H5 output).</param>
/// <param name="RequiredApplicationNames">Canonical app-name list from the manifest (e.g. ["msft_PowerBI_Anchor"]).</param>
public sealed record RequiredApplicationsInstallRequest(
    string TenantId,
    string ClientId,
    string? ClientSecret,
    string TargetDataverseUrl,
    IReadOnlyList<string> RequiredApplicationNames);

/// <summary>Result of <see cref="IRequiredApplicationsInstaller.EnsureInstalledAsync"/>.</summary>
public abstract record RequiredApplicationsInstallOutcome
{
    /// <summary>All applications were installed (either freshly or already-present).</summary>
    public sealed record Success(IReadOnlyList<string> InstalledOrAlreadyPresent) : RequiredApplicationsInstallOutcome;

    /// <summary>
    /// One or more applications could not be installed within the timeout.
    /// Diagnostic cites which app(s) failed + the observed error.
    /// </summary>
    public sealed record Failure(string Diagnostic) : RequiredApplicationsInstallOutcome;
}

/// <summary>
/// Manifest reader for the canonical required-applications list. Ships
/// under <c>scripts/canonical-solutions/required-applications.yaml</c>
/// so operators / makers can extend without a code change.
/// </summary>
public interface IRequiredApplicationsManifest
{
    /// <summary>Canonical list of required application names for the current release.</summary>
    IReadOnlyList<string> RequiredApplicationNames { get; }
}
