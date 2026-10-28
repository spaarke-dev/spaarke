namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>Request body for <c>POST /api/v1/external-access/remove-person</c> and <c>/reinstate-person</c>.</summary>
/// <param name="ContactId">The contact whose external access an administrator removes or reinstates.</param>
public record ExternalPersonRequest(Guid ContactId);

/// <summary>One step of a removal or a reinstatement, and whether it completed.</summary>
/// <param name="Step">The step's stable name (<c>removed_marker</c>, <c>grants_revoked</c>, <c>caches_evicted</c>,
/// <c>invite_state_cleared</c>, <c>standing_grant_cleared</c>, <c>removed_marker_cleared</c>).</param>
/// <param name="Completed">The step finished; false means a retry is needed (the call is idempotent).</param>
/// <param name="Detail">A staff-readable sentence when the step did not complete, or what it found; never an email.</param>
public record ExternalPersonStepResult(string Step, bool Completed, string? Detail = null);

/// <summary>
/// The outcome of <c>/remove-person</c>: 200 when every step completed. A partial removal is a 500 ProblemDetails carrying
/// the same fields as extensions; the person stays refused (the marker is written first).
/// </summary>
/// <param name="ContactId">The contact.</param>
/// <param name="Steps">Every step attempted, in order.</param>
/// <param name="GrantsRevoked">Logical grants whose revoke completed on this call.</param>
/// <param name="GrantsNotCompleted">Access record ids of the grants whose revoke did not complete; retry the removal.</param>
public record RemovePersonResponse(
    Guid ContactId,
    IReadOnlyList<ExternalPersonStepResult> Steps,
    int GrantsRevoked,
    IReadOnlyList<Guid> GrantsNotCompleted);

/// <summary>The outcome of <c>/reinstate-person</c>.</summary>
/// <param name="ContactId">The contact.</param>
/// <param name="MarkerCleared">The removed marker was cleared on this call; false when the contact carried no marker
/// (a no-op).</param>
public record ReinstatePersonResponse(Guid ContactId, bool MarkerCleared);
