namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// The durable <c>communicationUserId ↔ Dataverse</c> mapping for one participant (FR-03). Returned by
/// <see cref="Acs.IAcsIdentityService.EnsureIdentityAsync"/> after the participant is guaranteed to have
/// an ACS identity persisted on <c>sprk_communicationuserid</c>.
/// </summary>
/// <remarks>
/// Server-side model only. Per NFR-04 the ACS <c>communicationUserId</c> is transport addressing held by
/// the BFF; it is NOT an admin capability and NOT a token — it never grants ACS access on its own.
/// </remarks>
public sealed record AcsIdentityMapping
{
    /// <summary>The ACS <c>communicationUserId</c> (MRI, e.g. <c>8:acs:{resource}_{guid}</c>).</summary>
    public required string CommunicationUserId { get; init; }

    /// <summary>The Dataverse participant this identity maps to.</summary>
    public required ParticipantReference Participant { get; init; }

    /// <summary>
    /// <c>true</c> when this call created + persisted a new ACS identity; <c>false</c> when an existing
    /// mapping was found and reused (idempotent no-op).
    /// </summary>
    public required bool WasCreated { get; init; }
}
