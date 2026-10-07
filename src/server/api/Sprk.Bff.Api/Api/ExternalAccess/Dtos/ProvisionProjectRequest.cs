namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for POST /api/v1/external-access/provision-project.
///
/// Provisioning assigns a secure record — a project, matter or work assignment — to the canonical Secure Record
/// business unit's NAMED owner team (task 144) and creates the record's own SPE container. It creates no business
/// unit and no account.
/// </summary>
/// <remarks>
/// <para><b><c>UmbrellaBuId</c> was removed (task 021, 2026-08-25).</b> It selected between "reuse
/// this business unit" and "create a new one per project", and neither branch survives: design.md
/// §5.1 specifies ONE canonical Secure Record business unit, resolved by name from configuration, so there is no
/// longer a business unit for a caller to choose. A request still sending the field is unaffected — unknown JSON
/// properties are ignored.</para>
///
/// <para><b><c>SharePrincipalIds</c> added by task 061 (2026-09-08).</b> A secure record is owned by a memberless
/// team, so <b>nobody</b> can see it until an explicit share is issued (design.md §5.1: <i>"All human access is by
/// explicit Dataverse share, including the creating attorney's"</i>). Provisioning always shares to the creator;
/// this list is for the colleagues a wizard already knows about at creation time.</para>
///
/// <para><b><c>RecordType</c> / <c>RecordId</c> added by task 144 (2026-10-01).</b> All three roots carry
/// <c>sprk_issecure</c>, but only projects could be provisioned, so a secure matter or work assignment stayed owned by
/// its creator in an ordinary business unit — not isolated at all — and every upload to it 409'd. The shape is the
/// grant routes' (<see cref="GrantAccessRequest"/>): an explicit <c>RecordType</c> + <c>RecordId</c> wins, the
/// legacy <c>ProjectId</c> keeps working for the shipped Create Project wizard, and the delegation filter resolves the
/// SAME root through the same function (<c>SecureRecordRoot.ResolveTarget</c>).</para>
/// </remarks>
/// <param name="ProjectId">
/// Legacy shorthand for a project root. Ignored when <paramref name="RecordType"/> is supplied.
/// </param>
/// <param name="ProjectRef">
/// Optional. The record's short reference code, used only as a fallback for the SPE container's display name when the
/// record has no name.
/// </param>
/// <param name="SharePrincipalIds">
/// Optional. Additional Dataverse <c>systemuser</c> ids to share the record with at provisioning time, alongside the
/// creator (who is always shared to, and is identified from the caller's own token — never from this list).
/// </param>
/// <param name="RecordType">
/// <c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive; the grant routes' vocabulary). When
/// supplied, <paramref name="RecordId"/> is required.
/// </param>
/// <param name="RecordId">The GUID of the record named by <paramref name="RecordType"/>.</param>
/// <param name="Transition">
/// Task 150 (round 33 item 1): which surface asks. Omitted — the wizards' create-then-secure path, where an UNFLAGGED
/// record is secured only for its creator (owner round 10 item 10). <c>make-secure</c>
/// (<c>ProvisionProjectEndpoint.TransitionMakeSecure</c>) — the form's Make Secure command, securing an EXISTING record (task
/// 148's surface): held to the route's Write gate only (owner R3b), and the record's creator is shared to as well, so the
/// confirmation copy's "the person who created this record … will keep access" holds (owner round 27). The caller is
/// shared at the creator's level — never lower than a level they already hold — on the forward path AND when the call
/// finishes an earlier run that stopped after the owner move (round 40 items 1 and 2), so the same command finishes a
/// Make Secure that failed after its first write. It names no colleagues: with <paramref name="SharePrincipalIds"/> it is
/// refused 400 (people are added through Manage Access). The value is matched exactly; any other value, including an
/// empty string or another spelling, is refused 400 — an unrecognised surface never falls back to either rule.
/// </param>
public record ProvisionProjectRequest(
    Guid ProjectId,
    string? ProjectRef,
    IReadOnlyList<Guid>? SharePrincipalIds = null,
    string? RecordType = null,
    Guid? RecordId = null,
    string? Transition = null);
