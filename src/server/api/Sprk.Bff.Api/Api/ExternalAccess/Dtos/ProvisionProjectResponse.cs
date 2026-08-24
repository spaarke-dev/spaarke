namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response from POST /api/v1/external-access/provision-project.
///
/// Contains the IDs of all provisioned (or reused) infrastructure resources
/// so the caller (Create Project wizard) can store them on the project record
/// and display confirmation to the user.
/// </summary>
/// <param name="BusinessUnitId">
/// The Dataverse Business Unit ID created (or reused from UmbrellaBuId).
/// Intended to be stored on the project as the <c>sprk_securitybu</c> lookup. NOTE: the stamping
/// PATCH currently writes the nonexistent <c>sprk_securitybuid</c> and fails silently — task 021.
/// </param>
/// <param name="BusinessUnitName">Display name of the Business Unit.</param>
/// <param name="SpeContainerId">
/// The SPE FileStorageContainer ID provisioned for this project.
/// Intended to be stored on the project as <c>sprk_containerid</c>. NOTE: the stamping PATCH currently
/// writes the nonexistent <c>sprk_specontainerid</c> (a column of <c>sprk_container</c>) — task 021.
/// </param>
/// <param name="AccountId">
/// The Dataverse Account GUID created (or resolved from umbrella BU) to represent
/// this project's external organisation. Intended to be stored as the <c>sprk_externalaccount</c>
/// lookup; the PATCH currently writes the nonexistent <c>sprk_externalaccountid</c> — task 021.
/// </param>
/// <param name="AccountName">Display name of the Account.</param>
/// <param name="WasUmbrellaBu">
/// True when an existing umbrella BU was reused; false when a new BU was created.
/// </param>
public record ProvisionProjectResponse(
    Guid BusinessUnitId,
    string BusinessUnitName,
    string SpeContainerId,
    Guid AccountId,
    string AccountName,
    bool WasUmbrellaBu);
