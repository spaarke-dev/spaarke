using FluentAssertions;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class CommunicationAccountModelTests
{
    #region AccountType Enum Values

    [Fact]
    public void AccountType_SharedAccount_HasCorrectValue()
    {
        ((int)AccountType.SharedAccount).Should().Be(100000000);
    }

    [Fact]
    public void AccountType_ServiceAccount_HasCorrectValue()
    {
        ((int)AccountType.ServiceAccount).Should().Be(100000001);
    }

    [Fact]
    public void AccountType_UserAccount_HasCorrectValue()
    {
        ((int)AccountType.UserAccount).Should().Be(100000002);
    }

    #endregion

    #region DeriveAuthMethod

    [Fact]
    public void DeriveAuthMethod_SharedAccount_ReturnsAppOnly()
    {
        // Arrange
        var account = new CommunicationAccount
        {
            Name = "Shared",
            EmailAddress = "shared@contoso.com",
            AccountType = AccountType.SharedAccount
        };

        // Act
        var result = account.DeriveAuthMethod();

        // Assert
        result.Should().Be(AuthMethod.AppOnly);
    }

    [Fact]
    public void DeriveAuthMethod_ServiceAccount_ReturnsAppOnly()
    {
        // Arrange
        var account = new CommunicationAccount
        {
            Name = "Service",
            EmailAddress = "service@contoso.com",
            AccountType = AccountType.ServiceAccount
        };

        // Act
        var result = account.DeriveAuthMethod();

        // Assert
        result.Should().Be(AuthMethod.AppOnly);
    }

    [Fact]
    public void DeriveAuthMethod_UserAccount_ReturnsOnBehalfOf()
    {
        // Arrange
        var account = new CommunicationAccount
        {
            Name = "User",
            EmailAddress = "user@contoso.com",
            AccountType = AccountType.UserAccount
        };

        // Act
        var result = account.DeriveAuthMethod();

        // Assert
        result.Should().Be(AuthMethod.OnBehalfOf);
    }

    #endregion

    #region DeriveSubscriptionStatus

    [Fact]
    public void DeriveSubscriptionStatus_NullSubscriptionId_ReturnsNotConfigured()
    {
        // Arrange
        var account = new CommunicationAccount
        {
            Name = "Test",
            EmailAddress = "test@contoso.com",
            SubscriptionId = null
        };

        // Act
        var result = account.DeriveSubscriptionStatus();

        // Assert
        result.Should().Be(SubscriptionStatus.NotConfigured);
    }

    [Fact]
    public void DeriveSubscriptionStatus_EmptySubscriptionId_ReturnsNotConfigured()
    {
        // Arrange
        var account = new CommunicationAccount
        {
            Name = "Test",
            EmailAddress = "test@contoso.com",
            SubscriptionId = ""
        };

        // Act
        var result = account.DeriveSubscriptionStatus();

        // Assert
        result.Should().Be(SubscriptionStatus.NotConfigured);
    }

    [Fact]
    public void DeriveSubscriptionStatus_ValidSubscriptionWithFutureExpiry_ReturnsActive()
    {
        // Arrange
        var account = new CommunicationAccount
        {
            Name = "Test",
            EmailAddress = "test@contoso.com",
            SubscriptionId = "sub-abc-123",
            SubscriptionExpiry = DateTimeOffset.UtcNow.AddDays(7)
        };

        // Act
        var result = account.DeriveSubscriptionStatus();

        // Assert
        result.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void DeriveSubscriptionStatus_ValidSubscriptionWithPastExpiry_ReturnsExpired()
    {
        // Arrange
        var account = new CommunicationAccount
        {
            Name = "Test",
            EmailAddress = "test@contoso.com",
            SubscriptionId = "sub-abc-123",
            SubscriptionExpiry = DateTimeOffset.UtcNow.AddDays(-1)
        };

        // Act
        var result = account.DeriveSubscriptionStatus();

        // Assert
        result.Should().Be(SubscriptionStatus.Expired);
    }

    [Fact]
    public void DeriveSubscriptionStatus_ValidSubscriptionWithNullExpiry_ReturnsActive()
    {
        // Arrange — no expiry means subscription is considered active
        var account = new CommunicationAccount
        {
            Name = "Test",
            EmailAddress = "test@contoso.com",
            SubscriptionId = "sub-abc-123",
            SubscriptionExpiry = null
        };

        // Act
        var result = account.DeriveSubscriptionStatus();

        // Assert
        result.Should().Be(SubscriptionStatus.Active);
    }

    #endregion

    #region ShouldArchiveIncoming (FR-17 default-on, forward-only)

    // These pin the FR-17 contract: incoming mail to a MONITORED (receive-enabled) account is
    // archived as a full-body .eml by DEFAULT (default-on), unless the account EXPLICITLY opts out.
    // The predicate is the single source of truth for the archive gate in
    // IncomingCommunicationProcessor Step 6. If someone were to flip default-on to opt-IN
    // (e.g. `== true`), these tests break loudly — which is their whole purpose.

    private static CommunicationAccount CreateMonitoredAccount(bool? archiveIncomingOptIn) =>
        new()
        {
            Name = "Monitored",
            EmailAddress = "shared@contoso.com",
            AccountType = AccountType.SharedAccount,
            ReceiveEnabled = true,          // "monitored"
            MonitorFolder = "Inbox",
            ArchiveIncomingOptIn = archiveIncomingOptIn
        };

    [Fact]
    public void ShouldArchiveIncoming_WhenFlagUnset_ReturnsTrue()
    {
        // Monitored account, sprk_archiveincomingoptin unset (null) → default-on.
        var account = CreateMonitoredAccount(archiveIncomingOptIn: null);

        account.ShouldArchiveIncoming().Should().BeTrue(
            "an unset opt-in flag is the intended default-on for monitored accounts (FR-17)");
    }

    [Fact]
    public void ShouldArchiveIncoming_WhenFlagExplicitlyTrue_ReturnsTrue()
    {
        var account = CreateMonitoredAccount(archiveIncomingOptIn: true);

        account.ShouldArchiveIncoming().Should().BeTrue(
            "an explicit opt-in archives");
    }

    [Fact]
    public void ShouldArchiveIncoming_WhenExplicitlyOptedOut_ReturnsFalse()
    {
        // Negative: explicit opt-out (false) MUST be honored — default-on does NOT override it.
        var account = CreateMonitoredAccount(archiveIncomingOptIn: false);

        account.ShouldArchiveIncoming().Should().BeFalse(
            "an explicit opt-out (sprk_archiveincomingoptin = false) must be honored, not force-archived");
    }

    #endregion

    #region VerificationStatus Enum Values

    [Fact]
    public void VerificationStatus_Verified_HasCorrectValue()
    {
        ((int)VerificationStatus.Verified).Should().Be(100000000);
    }

    [Fact]
    public void VerificationStatus_Failed_HasCorrectValue()
    {
        ((int)VerificationStatus.Failed).Should().Be(100000001);
    }

    [Fact]
    public void VerificationStatus_Pending_HasCorrectValue()
    {
        ((int)VerificationStatus.Pending).Should().Be(100000002);
    }

    #endregion
}
