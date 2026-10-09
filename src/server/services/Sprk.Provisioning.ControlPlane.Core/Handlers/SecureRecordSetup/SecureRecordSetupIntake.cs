// -----------------------------------------------------------------------------
// SecureRecordSetupIntake.cs
//
// T256 (H7b) — the one rule for the H7b dry-run intake value, applied by POST /api/runs AND by the handler (the
// run-context contract: an intake value a handler has rules for is validated at intake with the same code, and
// refused with the handler's own rejection code — provisioning.md "Run-context contract").
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <summary>Intake rule for <see cref="IntakeParameterCatalog.SecureRecordSetupDryRun"/>.</summary>
public static class SecureRecordSetupIntake
{
    /// <summary>
    /// Reads the dry-run flag from the run's intake values: absent → false; <c>true</c> / <c>false</c> (exact, lower
    /// case) → that value; anything else → invalid (a near-miss such as <c>yes</c> must not silently mean "apply").
    /// </summary>
    /// <returns>False when the value is present but not <c>true</c> or <c>false</c>.</returns>
    public static bool TryReadDryRun(IDictionary<string, string> nonSecretParameters, out bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(nonSecretParameters);
        dryRun = false;
        if (!nonSecretParameters.TryGetValue(IntakeParameterCatalog.SecureRecordSetupDryRun, out var value))
        {
            return true;
        }
        switch (value)
        {
            case "true":
                dryRun = true;
                return true;
            case "false":
                return true;
            default:
                return false;
        }
    }
}
