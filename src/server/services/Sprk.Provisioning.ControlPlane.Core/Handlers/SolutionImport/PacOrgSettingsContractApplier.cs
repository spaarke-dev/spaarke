// -----------------------------------------------------------------------------
// PacOrgSettingsContractApplier.cs
//
// HANDLER-08 (Wave 2 pre-dispatch remediation 2026-08-27) — F14 verbatim.
// Production <see cref="IOrgSettingsContractApplier"/> impl. Wave 2
// scaffold: logs the intended contract application + returns Success
// unconditionally (the LIVE `pac org update-settings` shell-out lands in
// a follow-on incremental change; the seam + rejection code + wiring +
// H6 gate + tests + canonical manifest are the actual pain-point
// remediations F14 requires).
//
// See sibling <see cref="PacRequiredApplicationsInstaller"/> file header
// for the same scaffold-to-production trajectory rationale.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>Production <see cref="IOrgSettingsContractApplier"/> — Wave 2 scaffold.</summary>
public sealed class PacOrgSettingsContractApplier : IOrgSettingsContractApplier
{
    private readonly ILogger<PacOrgSettingsContractApplier> _logger;

    /// <summary>Constructs the applier.</summary>
    public PacOrgSettingsContractApplier(ILogger<PacOrgSettingsContractApplier> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<OrgSettingsContractOutcome> ApplyAsync(
        OrgSettingsContractApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "HANDLER-08 scaffold: org-settings-contract apply requested for {SettingCount} setting(s) on '{DataverseUrl}': " +
            "{Settings}. Wave 2 scaffold returns Success — operator must apply via `pac org update-settings` " +
            "until the incremental change lands.",
            request.OrgSettings.Count,
            request.TargetDataverseUrl,
            string.Join(", ", request.OrgSettings.Select(kv => $"{kv.Key}={kv.Value}")));

        return Task.FromResult<OrgSettingsContractOutcome>(
            new OrgSettingsContractOutcome.Success(request.OrgSettings));
    }
}
