using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Communication channel type. Maps to Dataverse field sprk_communicationtype.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommunicationType
{
    Email = 100000000,
    TeamsMessage = 100000001,
    SMS = 100000002,
    Notification = 100000003
}
