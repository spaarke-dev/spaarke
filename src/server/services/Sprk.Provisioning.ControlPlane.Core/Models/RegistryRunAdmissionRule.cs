// -----------------------------------------------------------------------------
// RegistryRunAdmissionRule.cs
//
// ISS-031 (#2926, 2026-11-23). Which registry rows POST /api/runs (REG-07) admits a new run against, in ONE place.
//
// Before: only an InProgress row was admitted. H13 leaves every completed stamp Ready and nothing moves it back, so
// every run against an existing stamp failed with 400 until an operator reset the row by hand (ACME, 2026-11-20).
//
// Now: a row the registry records as provisioned (sprk_provisionedon set) is an existing stamp. A run against it must
// carry intake `upgradeOfProvisionedOn` equal to that value: the operator's acknowledgement that the run re-runs THIS
// stamp. With it, a Ready (or InProgress) row is admitted AS IT IS. The row is not moved to InProgress: H13 sets Ready
// whatever the row says, and a live stamp left InProgress by a failed or cancelled run would stop the BFF's identity-link
// reconciliation (it skips non-Ready rows) with nothing to move it back. Without the acknowledgement the run is refused,
// naming the value to send. A row never provisioned refuses the acknowledgement.
//
// The acknowledgement is deliberately NOT `provisionedOn`: that key switches H0/H2a/H4/H4b to upgrade mode, which needs
// version inputs the skill cannot supply yet (H0 upgrade-compat-missing-versions; ISS-038).
//
// §11 justification — existing: the REG-07 block in RunsEndpoints.CreateRun. Extension: this IS that check, moved
// into a pure rule so every case is unit-tested and the endpoint keeps the I/O. Cost of doing nothing: every run
// against an existing stamp needs a hand-edited registry row.
// -----------------------------------------------------------------------------

using System.Globalization;

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>Decides whether POST /api/runs admits a run against a registry row (REG-07 + ISS-031).</summary>
public static class RegistryRunAdmissionRule
{
    /// <summary>The registry status a stamp being provisioned holds (sprk_setupstatus display name).</summary>
    public const string InProgressStatus = "InProgress";

    /// <summary>The registry status H13 leaves a completed stamp in.</summary>
    public const string ReadyStatus = "Ready";

    /// <summary>
    /// Decides admission.
    /// </summary>
    /// <param name="setupStatus">The row's <c>sprk_setupstatus</c> display name.</param>
    /// <param name="rowProvisionedOn">The row's <c>sprk_provisionedon</c>; null when never provisioned.</param>
    /// <param name="acknowledgedProvisionedOn">Intake <c>upgradeOfProvisionedOn</c> as sent; null or empty when absent.</param>
    public static RegistryRunAdmission Evaluate(string setupStatus, DateTimeOffset? rowProvisionedOn, string? acknowledgedProvisionedOn)
    {
        var acknowledged = !string.IsNullOrWhiteSpace(acknowledgedProvisionedOn);
        var isInProgress = string.Equals(setupStatus, InProgressStatus, StringComparison.OrdinalIgnoreCase);

        if (rowProvisionedOn is not { } provisioned)
        {
            if (!isInProgress)
            {
                return new RegistryRunAdmission.NotInProgress(
                    $"has setupStatus='{setupStatus}' (expected 'InProgress') and has never been provisioned. Row is in a " +
                    "rollback or unknown state; a new run cannot overwrite it. Use clear-quarantine or an operator-side " +
                    "registry reset before retrying.");
            }

            return acknowledged
                ? new RegistryRunAdmission.UpgradeMismatch(
                    $"has never been provisioned (sprk_provisionedon is empty), but the intake sends upgradeOfProvisionedOn=" +
                    $"'{acknowledgedProvisionedOn}'. A run against an existing stamp needs one: omit the key for a first " +
                    "provisioning, or check the environmentId.")
                : new RegistryRunAdmission.Admit();
        }

        var expected = provisioned.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        if (!acknowledged)
        {
            return new RegistryRunAdmission.UpgradeNotAcknowledged(
                $"is an existing stamp (sprk_provisionedon={expected}), so this run re-runs it. Send " +
                $"nonSecretParameters['upgradeOfProvisionedOn']='{expected}' to confirm it (/provision-environment Step 1a " +
                "sends it after the operator confirms the upgrade).");
        }

        if (!TryParseUtc(acknowledgedProvisionedOn!, out var claimed) || !SameSecond(claimed, provisioned))
        {
            return new RegistryRunAdmission.UpgradeMismatch(
                $"was provisioned at {expected}, but the intake sends upgradeOfProvisionedOn='{acknowledgedProvisionedOn}'. " +
                "Send the row's current value (read it from the registry again: every completed run rewrites it).");
        }

        if (isInProgress || string.Equals(setupStatus, ReadyStatus, StringComparison.OrdinalIgnoreCase))
        {
            return new RegistryRunAdmission.Admit();
        }

        return new RegistryRunAdmission.NotInProgress(
            $"has setupStatus='{setupStatus}' (expected 'Ready' or 'InProgress' for a run against an existing stamp). " +
            "Resolve the row's state (clear-quarantine or an operator-side registry reset) before retrying.");
    }

    private static bool TryParseUtc(string value, out DateTimeOffset parsed)
        => DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);

    // Dataverse stores Date and Time to the second; a client may render more or less precision.
    private static bool SameSecond(DateTimeOffset a, DateTimeOffset b)
        => a.ToUniversalTime().ToUnixTimeSeconds() == b.ToUniversalTime().ToUnixTimeSeconds();
}

/// <summary>Outcome of <see cref="RegistryRunAdmissionRule.Evaluate"/>.</summary>
public abstract record RegistryRunAdmission
{
    private RegistryRunAdmission() { }

    /// <summary>Admit the run; the row is left as it is.</summary>
    public sealed record Admit : RegistryRunAdmission;

    /// <summary>The row is in a state no run may start from.</summary>
    public sealed record NotInProgress(string Detail) : RegistryRunAdmission;

    /// <summary>An existing stamp, but the intake did not send <c>upgradeOfProvisionedOn</c>.</summary>
    public sealed record UpgradeNotAcknowledged(string Detail) : RegistryRunAdmission;

    /// <summary>The intake's <c>upgradeOfProvisionedOn</c> does not match the row (or the row was never provisioned).</summary>
    public sealed record UpgradeMismatch(string Detail) : RegistryRunAdmission;
}
