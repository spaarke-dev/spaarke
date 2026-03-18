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
/// Stored on the project as sprk_securitybuid.
/// </param>
/// <param name="BusinessUnitName">Display name of the Business Unit.</param>
/// <param name="SpeContainerId">
/// The SPE FileStorageContainer ID provisioned for this project.
/// Stored on the project as sprk_specontainerid.
/// </param>
/// <param name="AccountId">
/// The Dataverse Account GUID created (or resolved from umbrella BU) to represent
/// this project's external organisation. Stored on the project as sprk_externalaccountid.
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
