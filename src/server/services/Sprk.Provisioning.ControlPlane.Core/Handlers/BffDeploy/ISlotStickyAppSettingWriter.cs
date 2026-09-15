// -----------------------------------------------------------------------------
// ISlotStickyAppSettingWriter.cs
//
// L2 abstraction over "set one app setting on a deployment slot AND make it
// slot-sticky" — the ARM equivalent of
//   az webapp config appsettings set --slot <slot> --slot-settings NAME=VALUE
// H9 uses it for the ADR-036 A1 rule 2 scheduled-jobs slot guard
// (Scheduling__RunScheduledJobs=false) BEFORE its Kudu zip-deploy to the
// staging slot (GitHub #987) — parity with scripts/Deploy-BffApi.ps1
// -UseSlotDeploy and .github/workflows/deploy-bff-api.yml, which run that az
// command before their own staging deploys.
//
// SEMANTICS (binding for every implementation):
//   - MERGE, NEVER REPLACE. The slot's app settings are read and written back
//     with this one key set. The parent site's slotConfigNames are read and
//     the name is appended only if absent — every other sticky app-setting
//     name, the connection-string sticky list and the storage sticky list are
//     written back unchanged. (Both ARM writes are whole-object PUTs, so a
//     write that did not start from a read would wipe the rest.)
//   - STICKY FIRST. The name is made slot-sticky on the parent site BEFORE the
//     value is written to the slot, so at no intermediate point can a swap
//     carry the value into production: a sticky name with no value moves
//     nothing, whereas a value that is not yet sticky travels with the swap
//     (for the scheduled-jobs guard, that would silently stop every scheduled
//     job in production). az CLI writes in the opposite order; the end state
//     is identical.
//   - IDEMPOTENT. Nothing already in the desired state is written — a re-run
//     makes no ARM write at all (and so causes no slot restart).
//   - Domain failures (ARM 4xx/5xx) do NOT throw — carried in
//     SlotStickyAppSettingResult.Failure. Infrastructure faults (timeout,
//     transport) MAY throw.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: ArmSlotStickyAppSettingWriter (Azure.ResourceManager.AppService).
//     - Test: FakeSlotGuard in H9BffDeployHandlerTests.
//   Component justification (CLAUDE.md §11): see ArmSlotStickyAppSettingWriter.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Sets one app setting on an App Service deployment slot and marks it
/// slot-sticky on the parent site — merge, never replace; sticky name first;
/// idempotent. Production impl: <see cref="ArmSlotStickyAppSettingWriter"/>.
/// </summary>
public interface ISlotStickyAppSettingWriter
{
    /// <summary>
    /// Ensures <see cref="SlotStickyAppSettingRequest.SettingName"/> =
    /// <see cref="SlotStickyAppSettingRequest.SettingValue"/> on the slot and
    /// that the name is listed in the site's sticky app-setting names. Domain
    /// failures (ARM 4xx/5xx) do NOT throw — carried in
    /// <see cref="SlotStickyAppSettingResult.Failure"/>. Infrastructure faults
    /// MAY throw.
    /// </summary>
    Task<SlotStickyAppSettingResult> EnsureAsync(SlotStickyAppSettingRequest request, CancellationToken cancellationToken);
}

/// <summary>Inputs to a single slot-sticky app-setting write.</summary>
/// <param name="SubscriptionId">Subscription hosting the App Service (ADR-027 D4).</param>
/// <param name="ResourceGroupName">Resource group hosting the App Service.</param>
/// <param name="AppServiceName">App Service (parent site) name — owns the sticky-name list.</param>
/// <param name="SlotName">Deployment slot that receives the value (never <c>production</c>).</param>
/// <param name="SettingName">App setting name, in App Service form (e.g. <c>Scheduling__RunScheduledJobs</c>).</param>
/// <param name="SettingValue">App setting value.</param>
public sealed record SlotStickyAppSettingRequest(
    string SubscriptionId,
    string ResourceGroupName,
    string AppServiceName,
    string SlotName,
    string SettingName,
    string SettingValue);

/// <summary>Discriminated result of <see cref="ISlotStickyAppSettingWriter.EnsureAsync"/>.</summary>
public abstract record SlotStickyAppSettingResult
{
    private SlotStickyAppSettingResult() { }

    /// <summary>
    /// The slot holds the value and the name is slot-sticky. The flags say what
    /// this call had to write; both <c>false</c> = already in place, no ARM write.
    /// </summary>
    public sealed record Success(bool StickyNameAdded, bool SettingWritten) : SlotStickyAppSettingResult;

    /// <summary>
    /// ARM rejected a read or write. The value is NOT confirmed on the slot; the
    /// name may already be sticky (harmless — a sticky name with no value moves
    /// nothing on a swap).
    /// </summary>
    public sealed record Failure(string Diagnostic) : SlotStickyAppSettingResult;
}
