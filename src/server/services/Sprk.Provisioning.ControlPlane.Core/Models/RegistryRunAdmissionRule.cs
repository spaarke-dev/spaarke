// -----------------------------------------------------------------------------
// RegistryRunAdmissionRule.cs
//
// ISS-031 (#2926, 2026-11-23). Which registry rows POST /api/runs (REG-07) admits a new run against, in ONE place.
//
// Before: only an InProgress row was admitted. H13 leaves every completed stamp Ready, and nothing moved it back, so
// every upgrade run failed with 400 until an operator reset the row by hand (ACME, 2026-11-20). Worse, the skill never
// sent `provisionedOn`, so the ACME upgrade ran H0/H2a/H4/H9 in fresh-provision mode — H2a's upgrade drift check
// (§14A.5) never ran.
//
// Now: a row the registry records as provisioned (sprk_provisionedon set) is an existing stamp. A run against it must
// carry intake `provisionedOn` equal to that value — the operator's acknowledgement that this is an upgrade of THIS
// stamp, and the value H0/H2a/H4/H9 read to run in upgrade mode. With it, a Ready row is admitted and the endpoint
// moves it to InProgress itself (the one owner of that transition). Without it, the run is refused with the value to
// send. A row never provisioned refuses an upgrade claim.
//
// §11 justification — existing: the REG-07 block in RunsEndpoints.CreateRun. Extension: this IS that check, moved
// into a pure rule so every case is unit-tested and the endpoint keeps the I/O. Cost of doing nothing: every upgrade
// needs a hand-edited registry row and runs without upgrade-mode safety.
// -----------------------------------------------------------------------------

using System.Globalization;

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>Decides whether POST /api/runs admits a run against a registry row (REG-07 + ISS-031).</summary>
public static class RegistryRunAdmissionRule
{
    /// <summary>The registry status a running stamp holds (sprk_setupstatus display name).</summary>
    public const string InProgressStatus = "InProgress";

    /// <summary>The registry status H13 leaves a completed stamp in.</summary>
    public const string ReadyStatus = "Ready";

    /// <summary>
    /// Decides admission.
    /// </summary>
    /// <param name="setupStatus">The row's <c>sprk_setupstatus</c> display name.</param>
    /// <param name="rowProvisionedOn">The row's <c>sprk_provisionedon</c>; null when never provisioned.</param>
    /// <param name="intakeProvisionedOn">Intake <c>provisionedOn</c> as sent; null or empty when absent.</param>
    public static RegistryRunAdmission Evaluate(string setupStatus, DateTimeOffset? rowProvisionedOn, string? intakeProvisionedOn)
    {
        var claimsUpgrade = !string.IsNullOrWhiteSpace(intakeProvisionedOn);
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

            return claimsUpgrade
                ? new RegistryRunAdmission.UpgradeMismatch(
                    $"has never been provisioned (sprk_provisionedon is empty), but the intake sends provisionedOn=" +
                    $"'{intakeProvisionedOn}'. An upgrade run needs an existing stamp: omit provisionedOn for a first " +
                    "provisioning, or check the environmentId.")
                : new RegistryRunAdmission.Admit(MoveToInProgress: false);
        }

        var expected = provisioned.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        if (!claimsUpgrade)
        {
            return new RegistryRunAdmission.UpgradeNotAcknowledged(
                $"is an existing stamp (sprk_provisionedon={expected}), so this run is an UPGRADE. Send " +
                $"nonSecretParameters['provisionedOn']='{expected}' to confirm it (/provision-environment Step 1a sends " +
                "it after the operator confirms the upgrade); H0, H2a, H4 and H9 read it to run in upgrade mode.");
        }

        if (!TryParseUtc(intakeProvisionedOn!, out var claimed) || !SameSecond(claimed, provisioned))
        {
            return new RegistryRunAdmission.UpgradeMismatch(
                $"was provisioned at {expected}, but the intake sends provisionedOn='{intakeProvisionedOn}'. Send the " +
                "row's current value (read it from the registry again: every completed run rewrites it).");
        }

        if (isInProgress)
        {
            // An upgrade already admitted (or a row reset by hand): resume/retry without another transition.
            return new RegistryRunAdmission.Admit(MoveToInProgress: false);
        }

        if (string.Equals(setupStatus, ReadyStatus, StringComparison.OrdinalIgnoreCase))
        {
            return new RegistryRunAdmission.Admit(MoveToInProgress: true);
        }

        return new RegistryRunAdmission.NotInProgress(
            $"has setupStatus='{setupStatus}' (expected 'Ready' or 'InProgress' for an upgrade). Resolve the row's state " +
            "(clear-quarantine or an operator-side registry reset) before retrying.");
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

    /// <summary>Admit the run; when <paramref name="MoveToInProgress"/> the endpoint first moves the Ready row to InProgress.</summary>
    public sealed record Admit(bool MoveToInProgress) : RegistryRunAdmission;

    /// <summary>The row is in a state no run may start from.</summary>
    public sealed record NotInProgress(string Detail) : RegistryRunAdmission;

    /// <summary>An existing stamp, but the intake did not send <c>provisionedOn</c>.</summary>
    public sealed record UpgradeNotAcknowledged(string Detail) : RegistryRunAdmission;

    /// <summary>The intake's <c>provisionedOn</c> does not match the row (or the row was never provisioned).</summary>
    public sealed record UpgradeMismatch(string Detail) : RegistryRunAdmission;
}
