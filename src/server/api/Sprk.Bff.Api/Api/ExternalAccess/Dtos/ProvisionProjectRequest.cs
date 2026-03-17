namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for POST /api/v1/external-access/provision-project.
///
/// Provisions all infrastructure required for a Secure Project:
///   - A child Business Unit scoped to this project
///   - An SPE container for isolated document storage
///   - An External Access Account record owned by the BU
///
/// Supports the "umbrella BU" scenario where organizations with existing secure
/// projects can reuse an existing BU and Account instead of creating new ones.
/// </summary>
/// <param name="ProjectId">
/// The Dataverse sprk_project GUID — must already exist with sprk_issecure = true.
/// </param>
/// <param name="ProjectRef">
/// The project's short reference code (e.g. "P-2024-0042"). Used to name the BU as
/// "SP-{ProjectRef}". Required unless UmbrellaBuId is provided.
/// </param>
/// <param name="UmbrellaBuId">
/// Optional. If provided, skips BU and Account creation and links to this existing
/// Business Unit. The BU's associated Account is resolved from the BU record.
/// Used for multi-project organisations that share a single BU/Account.
/// </param>
public record ProvisionProjectRequest(
    Guid ProjectId,
    string? ProjectRef,
    Guid? UmbrellaBuId);
