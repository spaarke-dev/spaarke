using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Request DTO for POST /api/communications/threads/direct (task 043 / FR-09) — start (or reuse) a 1:1
/// direct thread with another Spaarke user.
/// </summary>
public sealed record StartDirectThreadRequest
{
    /// <summary>
    /// The other participant's Dataverse <c>systemuserid</c> (R1 is internal-only — a <c>contact</c>
    /// counterpart is out of scope, per <c>notes/access-model-decision.md</c> config prerequisite #3).
    /// </summary>
    [Required]
    public required Guid OtherParticipantSystemUserId { get; init; }
}
