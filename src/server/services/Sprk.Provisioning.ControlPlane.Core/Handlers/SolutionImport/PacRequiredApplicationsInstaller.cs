// -----------------------------------------------------------------------------
// PacRequiredApplicationsInstaller.cs
//
// HANDLER-07 (Wave 2 pre-dispatch remediation 2026-08-27) — F13 verbatim.
// Production <see cref="IRequiredApplicationsInstaller"/> impl. Wave 2
// scaffold: logs the intended install list + returns Success
// unconditionally (the LIVE `pac application install` shell-out lands in
// a follow-on incremental change; the seam + rejection code + wiring +
// H6 gate are the actual pain-point remediations F13 requires — a real
// operator can pre-install msft_PowerBI_Anchor via the Power Platform
// admin center once and the manifest's post-import verifier catches any
// missed pre-req).
//
// FUTURE INCREMENTAL CHANGE (documented forward reference):
//   Replace this body with a real shell-out to `pac application install
//   --name {app} --environment {url}` per application + poll ADmin API
//   until state == "installed" (typical 6 min per app). Add a timeout
//   knob to the request record; on timeout return Failure with a
//   MissingRequiredApplication diagnostic. The interface + all callers
//   stay unchanged.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Production <see cref="IRequiredApplicationsInstaller"/> impl. Wave 2
/// scaffold — see file header for the incremental-change trajectory to
/// a real `pac application install` shell-out.
/// </summary>
public sealed class PacRequiredApplicationsInstaller : IRequiredApplicationsInstaller
{
    private readonly ILogger<PacRequiredApplicationsInstaller> _logger;

    /// <summary>Constructs the installer.</summary>
    public PacRequiredApplicationsInstaller(ILogger<PacRequiredApplicationsInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<RequiredApplicationsInstallOutcome> EnsureInstalledAsync(
        RequiredApplicationsInstallRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Wave 2 scaffold: log the intent + return Success. Operator must
        // pre-install the listed apps via the Power Platform admin center
        // until the incremental change lands. Post-import solution
        // verifier will catch a missed pre-req.
        _logger.LogInformation(
            "HANDLER-07 scaffold: required-applications ensure requested for {AppCount} app(s) on '{DataverseUrl}': " +
            "{Apps}. Wave 2 scaffold returns Success — operator must pre-install via Power Platform admin center " +
            "until the `pac application install` incremental change lands (see PacRequiredApplicationsInstaller.cs header).",
            request.RequiredApplicationNames.Count,
            request.TargetDataverseUrl,
            string.Join(", ", request.RequiredApplicationNames));

        return Task.FromResult<RequiredApplicationsInstallOutcome>(
            new RequiredApplicationsInstallOutcome.Success(request.RequiredApplicationNames));
    }
}
