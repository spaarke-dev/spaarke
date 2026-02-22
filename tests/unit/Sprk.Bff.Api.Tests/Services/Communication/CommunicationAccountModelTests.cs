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
