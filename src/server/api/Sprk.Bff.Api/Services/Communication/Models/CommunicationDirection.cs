namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Communication direction. Maps to Dataverse sprk_direction.
/// </summary>
public enum CommunicationDirection
{
    Incoming = 100000000,
    Outgoing = 100000001
}
