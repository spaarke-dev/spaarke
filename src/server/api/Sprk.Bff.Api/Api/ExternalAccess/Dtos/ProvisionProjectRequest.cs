namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for POST /api/v1/external-access/provision-project.
///
/// Provisioning assigns the project to the canonical Secure Project business unit's default owner
/// team and creates the project's own SPE container. It creates no business unit and no account.
/// </summary>
/// <remarks>
/// <para><b><c>UmbrellaBuId</c> was removed (task 021, 2026-08-25).</b> It selected between "reuse
/// this business unit" and "create a new one per project", and neither branch survives: design.md
/// §5.1 specifies ONE canonical <c>Secure Project</c> business unit, resolved by name from
/// configuration, so there is no longer a business unit for a caller to choose. A request still
/// sending the field is unaffected — unknown JSON properties are ignored.</para>
/// </remarks>
/// <param name="ProjectId">
/// The Dataverse sprk_project GUID — must already exist with sprk_issecure = true.
/// </param>
/// <param name="ProjectRef">
/// Optional. The project's short reference code (e.g. "P-2024-0042"), used only as a fallback for the
/// SPE container's display name when the project record has no <c>sprk_projectname</c>. It no longer
/// names a business unit, so it is no longer required — the previous
/// "required unless UmbrellaBuId is provided" rule went with the umbrella branch.
/// </param>
/// <param name="SharePrincipalIds">
/// Optional. Additional Dataverse <c>systemuser</c> ids to share the project with at provisioning
/// time, alongside the creator (who is always shared to, and is identified from the caller's own
/// token — never from this list).
/// </param>
/// <remarks>
/// <para><b><c>SharePrincipalIds</c> added by task 061 (2026-09-08).</b> A secure project is owned by
/// a memberless team, so <b>nobody</b> can see it until an explicit share is issued (design.md §5.1:
/// <i>"All human access is by explicit Dataverse share, including the creating attorney's"</i>).
/// Provisioning always shares to the creator; this list is for the colleagues a wizard already knows
/// about at creation time, so the common case does not need a second round trip to the FR-29
/// "+ User" endpoint.</para>
/// </remarks>
public record ProvisionProjectRequest(
    Guid ProjectId,
    string? ProjectRef,
    IReadOnlyList<Guid>? SharePrincipalIds = null);
