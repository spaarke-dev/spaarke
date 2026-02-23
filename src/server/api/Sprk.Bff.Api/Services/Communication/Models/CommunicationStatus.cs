namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Communication lifecycle status. Maps to Dataverse standard statuscode field.
/// </summary>
public enum CommunicationStatus
{
    Draft = 1,
    Deleted = 2,
    Queued = 659490001,
    Send = 659490002,
    Delivered = 659490003,
    Failed = 659490004,
    Bounded = 659490005,
    Recalled = 659490006
}
