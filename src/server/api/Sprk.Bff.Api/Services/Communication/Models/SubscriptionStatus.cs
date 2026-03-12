namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Derived Graph subscription status.
/// Not stored in Dataverse — derived from sprk_graphsubscriptionid + sprk_graphsubscriptionexpiry.
/// </summary>
public enum SubscriptionStatus
{
    NotConfigured,
    Active,
    Expired
}
