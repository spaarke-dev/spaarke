namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Derived Graph subscription status.
/// Not stored in Dataverse — derived from sprk_subscriptionid + sprk_subscriptionexpiry.
/// </summary>
public enum SubscriptionStatus
{
    NotConfigured,
    Active,
    Expired
}
