namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Identifies a Spaarke messaging participant by its Dataverse identity record — either a
/// <c>systemuser</c> (internal Entra user) or a <c>contact</c> (external party). Both entities
/// carry the <c>sprk_communicationuserid</c> column (task 005) that maps the identity to its ACS
/// <c>communicationUserId</c> (MRI). Used by <see cref="Acs.IAcsIdentityService.EnsureIdentityAsync"/>
/// as the durable key for the <c>communicationUserId ↔ Dataverse</c> mapping (FR-03).
/// </summary>
public sealed record ParticipantReference
{
    /// <summary>Dataverse logical name of the identity entity — <c>systemuser</c> or <c>contact</c>.</summary>
    public required string EntityLogicalName { get; init; }

    /// <summary>Row id of the identity record.</summary>
    public required Guid RecordId { get; init; }

    /// <summary>An internal Spaarke user (Entra-backed <c>systemuser</c>).</summary>
    public static ParticipantReference SystemUser(Guid id) =>
        new() { EntityLogicalName = "systemuser", RecordId = id };

    /// <summary>An external party (<c>contact</c>).</summary>
    public static ParticipantReference Contact(Guid id) =>
        new() { EntityLogicalName = "contact", RecordId = id };
}
