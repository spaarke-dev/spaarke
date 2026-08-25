namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response from POST /api/v1/external-access/provision-project.
///
/// Reports what provisioning actually did, so the caller can display confirmation and an operator can
/// reconcile if a later step failed.
/// </summary>
/// <remarks>
/// <para><b>Reshaped by task 021 (2026-08-25).</b> Three members were removed because the things they
/// described are no longer created:</para>
/// <list type="bullet">
///   <item><c>AccountId</c> / <c>AccountName</c> — provisioning created a synthetic
///   "External Access — {project}" <c>account</c> per project. Nothing in the external-access model
///   reads an account (firms are <c>sprk_organization</c>), and the column it was aimed at,
///   <c>sprk_externalaccount</c>, is the project's CLIENT lookup. Both the creation and the write are
///   gone.</item>
///   <item><c>WasUmbrellaBu</c> — there is one canonical Secure Project business unit, so there is no
///   longer a create-vs-reuse distinction to report.</item>
/// </list>
/// <para>Two members were added: the owner team that now holds the record, which is the thing that
/// actually makes the project secure (design.md §5.1a).</para>
/// </remarks>
/// <param name="BusinessUnitId">
/// The canonical Secure Project business unit this project now belongs to, RESOLVED by name — not
/// created. Reported for operator confirmation; it is not written to the project record. The retired
/// <c>sprk_securitybu</c> stamp existed to record a per-project business unit, and there is no longer
/// a per-project business unit to record.
/// </param>
/// <param name="BusinessUnitName">
/// The configured name that resolved (<c>SecureProject:BusinessUnitName</c>, default
/// <c>Secure Project</c>).
/// </param>
/// <param name="OwnerTeamId">
/// The business unit's default owner team, which now owns the project. This is the security-relevant
/// outcome: the record sits in the Secure Project business unit because a team there owns it, and no
/// human holds access through that ownership.
/// </param>
/// <param name="OwnerTeamName">Display name of the owner team.</param>
/// <param name="SpeContainerId">
/// The SPE FileStorageContainer provisioned for this project, recorded on the project as
/// <c>sprk_containerid</c>. A response containing this id means the write succeeded — if it could not
/// be written the endpoint returns a non-2xx that carries the id instead (ADR-003).
/// </param>
public record ProvisionProjectResponse(
    Guid BusinessUnitId,
    string BusinessUnitName,
    Guid OwnerTeamId,
    string OwnerTeamName,
    string SpeContainerId);
