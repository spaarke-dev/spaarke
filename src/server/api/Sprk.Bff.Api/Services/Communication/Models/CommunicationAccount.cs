namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Model representing a Dataverse sprk_communicationaccount record.
/// Field names match the actual Dataverse schema exactly.
/// </summary>
public sealed class CommunicationAccount
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string EmailAddress { get; init; }
    public string? DisplayName { get; init; }
    public AccountType AccountType { get; init; }

    /// <summary>Note: actual Dataverse field is sprk_sendenableds (trailing 's').</summary>
    public bool SendEnabled { get; init; }

    public bool IsDefaultSender { get; init; }
    public bool ReceiveEnabled { get; init; }
    public string? MonitorFolder { get; init; }
    public bool AutoCreateRecords { get; init; }

    /// <summary>Graph subscription ID. Null means no subscription configured.</summary>
    public string? SubscriptionId { get; init; }

    public DateTimeOffset? SubscriptionExpiry { get; init; }
    public string? SecurityGroupId { get; init; }
    public string? SecurityGroupName { get; init; }
    public VerificationStatus? VerificationStatus { get; init; }
    public DateTimeOffset? LastVerified { get; init; }

    /// <summary>
    /// Derives auth method from account type.
    /// Shared/Service → AppOnly, User → OnBehalfOf.
    /// </summary>
    public AuthMethod DeriveAuthMethod() => AccountType switch
    {
        AccountType.SharedAccount => AuthMethod.AppOnly,
        AccountType.ServiceAccount => AuthMethod.AppOnly,
        AccountType.UserAccount => AuthMethod.OnBehalfOf,
        _ => AuthMethod.AppOnly
    };

    /// <summary>
    /// Derives subscription status from SubscriptionId presence and expiry.
    /// </summary>
    public SubscriptionStatus DeriveSubscriptionStatus()
    {
        if (string.IsNullOrEmpty(SubscriptionId))
            return Models.SubscriptionStatus.NotConfigured;

        if (SubscriptionExpiry.HasValue && SubscriptionExpiry.Value < DateTimeOffset.UtcNow)
            return Models.SubscriptionStatus.Expired;

        return Models.SubscriptionStatus.Active;
    }
}
