namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Communication channel type. Maps to Dataverse sprk_communiationtype (note: typo is intentional — actual Dataverse logical name).
/// </summary>
public enum CommunicationType
{
    Email = 100000000,
    TeamsMessage = 100000001,
    SMS = 100000002,
    Notification = 100000003
}
