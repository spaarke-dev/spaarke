// -----------------------------------------------------------------------------
// IAppServiceSettingsWriter.cs
//
// Task 253 (G38) — the seam H4b writes the BFF's app settings through. It
// replaced the pwsh run of the generated Configure-AppServiceSettings script:
// the L2 Worker host (App Service DOTNETCORE|10.0) has no PowerShell and its
// publish has no scripts/ folder.
//
// CONTRACT (the generated script's, kept):
//   - The settings land on the production site AND the `staging` slot.
//   - MERGE, never replace: every setting already on a slot that the request
//     does not name stays as it is (e.g. an operator-set
//     AiSpendLimit__MonthlyLimitUsd, T254). A named setting takes the
//     requested value.
//   - One write per slot, and none when the slot already holds every
//     requested value — so a re-run does not restart the BFF.
//   - EXCEPTION to merge (task 255): an "exclusive list" key names a .NET
//     configuration list H4b owns whole (WorkforceIdentity__CustomerTenantIds).
//     Every existing setting under it ({key}__*, or the bare {key}; matched
//     case-insensitively, ':' read as '__', as .NET configuration binds it)
//     that the request does not name is REMOVED — a tenant dropped from the
//     list must not keep admitting its employees from a stale index.
//
// Production impl: ArmAppServiceSettingsWriter (Azure.ResourceManager.AppService).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Merges a set of app settings into an App Service's production site and its
/// <c>staging</c> slot. Production impl: <see cref="ArmAppServiceSettingsWriter"/>.
/// </summary>
public interface IAppServiceSettingsWriter
{
    /// <summary>
    /// Merges <see cref="AppServiceSettingsWriteRequest.Settings"/> into both slots.
    /// ARM refusals (4xx/5xx, a missing slot) do NOT throw — they are a
    /// <see cref="AppServiceSettingsWriteResult.Failure"/>. Cancellation throws.
    /// </summary>
    Task<AppServiceSettingsWriteResult> MergeAsync(AppServiceSettingsWriteRequest request, CancellationToken cancellationToken);
}

/// <summary>Inputs to one H4b app-settings merge.</summary>
/// <param name="SubscriptionId">The customer's subscription (intake <c>subscriptionId</c>).</param>
/// <param name="ResourceGroupName">The stamp's resource group (H2a output).</param>
/// <param name="AppServiceName">The stamp's BFF App Service (H2a output).</param>
/// <param name="Settings">The settings to set — name → value (ordinal names, as App Service compares them).</param>
/// <param name="ExclusiveListKeys">Task 255: base names of .NET configuration lists the request owns whole — every
/// existing <c>{key}__*</c> (or bare <c>{key}</c>) setting not in <paramref name="Settings"/> is removed. Null = none.</param>
public sealed record AppServiceSettingsWriteRequest(
    string SubscriptionId,
    string ResourceGroupName,
    string AppServiceName,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string>? ExclusiveListKeys = null);

/// <summary>Discriminated result of <see cref="IAppServiceSettingsWriter.MergeAsync"/>.</summary>
public abstract record AppServiceSettingsWriteResult
{
    private AppServiceSettingsWriteResult() { }

    /// <summary>
    /// Both slots hold every requested value. <paramref name="SlotsWritten"/> names the slots this call had to
    /// write (<c>production</c> / <c>staging</c>); empty = everything was already in place, nothing written.
    /// </summary>
    public sealed record Success(IReadOnlyList<string> SlotsWritten) : AppServiceSettingsWriteResult;

    /// <summary>
    /// ARM refused a read or a write. The production slot may already carry the new values when the staging slot
    /// failed; a re-run converges (the merge is idempotent). The diagnostic names settings, never their values.
    /// </summary>
    public sealed record Failure(string Diagnostic) : AppServiceSettingsWriteResult;
}
