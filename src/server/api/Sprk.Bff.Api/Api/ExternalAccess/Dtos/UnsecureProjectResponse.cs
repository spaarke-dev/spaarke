namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response from POST /api/v1/external-access/unsecure-project.
/// </summary>
/// <param name="ProjectId">The project whose secure designation was removed.</param>
/// <param name="NewOwnerSystemUserId">
/// The <c>systemuser</c> that now owns the record. Ownership moving off the Secure Project business
/// unit's owner team is what actually ends the isolation — the <c>sprk_issecure</c> flag is a label,
/// the ownership is the mechanism.
/// </param>
/// <param name="SharesRevoked">
/// How many POA shares were removed from the record. A secure project's access came entirely from
/// explicit shares; once normal ownership and business-unit access apply, leaving them behind would
/// keep a second, invisible access path alive.
/// </param>
/// <param name="AlreadyUnsecure">
/// <c>true</c> when the project was not secure to begin with. The call is idempotent: a repeat is a
/// 200 that changed nothing, not a 409.
/// </param>
/// <param name="SweepComplete">
/// Whether <paramref name="SharesRevoked"/> is the WHOLE story. Three states, and the distinction
/// between the last two is the point:
/// <list type="bullet">
/// <item><c>true</c> — every share on the record was enumerated and removed.</item>
/// <item><c>false</c> — a sweep RAN but could not account for every row; one or more shares may survive.</item>
/// <item><c>null</c> — NO sweep was attempted (the idempotent already-unsecure path). This response
/// makes no claim either way, and the record may still carry POA rows from another surface.</item>
/// </list>
/// A caller must treat anything other than <c>true</c> as "not vouched for".
/// </param>
/// <remarks>
/// <para><b>Why this exists</b> (ISS-018 / #995, task 108): <c>SharesRevoked = 0</c> was ambiguous in
/// the worst direction — EITHER "no shares" OR "the shares could not be read", because the soft POA
/// read answers an empty list when it fails. A count is only meaningful next to a statement about
/// whether the count is complete. The full argument lives on
/// <c>UnsecureProjectEndpoint.RevokeAllSharesAsync</c>; this summary points there rather than
/// restating it, so the two cannot drift apart.</para>
///
/// <para><b>No default, deliberately.</b> An earlier draft defaulted this to <c>true</c>, and review
/// caught the consequence: the idempotent early-return omitted the argument and so silently claimed a
/// complete sweep for a sweep it never ran — reinstating ISS-018 on the retry that a
/// <c>sweepComplete: false</c> response invites. A completeness flag that defaults to the optimistic
/// answer is fail-OPEN. With no default the compiler forces every construction site to say what it
/// actually knows. The JSON contract is still purely additive; only in-assembly source is affected,
/// and both call sites live in the endpoint.</para>
/// </remarks>
public record UnsecureProjectResponse(
    Guid ProjectId,
    Guid NewOwnerSystemUserId,
    int SharesRevoked,
    bool AlreadyUnsecure,
    bool? SweepComplete);
