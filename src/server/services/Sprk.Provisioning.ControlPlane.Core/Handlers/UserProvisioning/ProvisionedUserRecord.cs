// -----------------------------------------------------------------------------
// ProvisionedUserRecord.cs
//
// POCO record written to ProvisioningRun.InterStepState.ProvisionedUsers by
// H11 (task 054) after a successful (or WaitingOnGate-pending) user
// provisioning pass. Satisfies POML goal item (d): "writes provisioned
// userIds to Cosmos interStepState". Mirrors the SolutionImport.
// ImportedSolutionRecord + task 050/053 InterStepState controlled-extension
// precedent — a deliberate type extension, not an ad-hoc dictionary insert.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// One entry in the H11-authored user manifest persisted to
/// <see cref="Models.InterStepState.ProvisionedUsers"/>.
/// </summary>
/// <param name="UserId">
/// Entra ID object id (Graph <c>user.id</c>) — for NativeAccount the created
/// user's id, for B2BGuest the invited guest's <c>invitedUser.id</c>.
/// </param>
/// <param name="UpnOrEmail">
/// NativeAccount: the generated UPN. B2BGuest: the invited email address
/// (the guest's UPN in the Spaarke tenant is Graph-generated and not
/// surfaced by the invitation response — the invited email is the stable
/// operator-facing identifier).
/// </param>
/// <param name="IdentityPreset">
/// The D6 preset that produced this entry — <c>"B2BGuest"</c> or
/// <c>"NativeAccount"</c>.
/// </param>
public sealed record ProvisionedUserRecord(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("upnOrEmail")] string UpnOrEmail,
    [property: JsonPropertyName("identityPreset")] string IdentityPreset);
