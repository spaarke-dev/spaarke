using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class DailySendCountTests
{
    #region Test Infrastructure

    private readonly Mock<IDataverseService> _dataverseMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly CommunicationAccountService _accountService;

    public DailySendCountTests()
    {
        _dataverseMock = new Mock<IDataverseService>();
        _cacheMock = new Mock<IDistributedCache>();

        // Default: cache miss
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _accountService = new CommunicationAccountService(
            _dataverseMock.Object,
            _dataverseMock.Object,
            _cacheMock.Object,
            Mock.Of<ILogger<CommunicationAccountService>>());
    }

    #endregion

    #region IncrementSendCountAsync Tests

    [Fact]
    public async Task IncrementSendCountAsync_IncrementsCountByOne()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var entity = new DataverseEntity("sprk_communicationaccount") { Id = accountId };
        entity["sprk_sendstoday"] = 5;

        _dataverseMock
            .Setup(d => d.RetrieveAsync("sprk_communicationaccount", accountId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _dataverseMock
            .Setup(d => d.UpdateAsync("sprk_communicationaccount", accountId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _accountService.IncrementSendCountAsync(accountId);

        // Assert
        _dataverseMock.Verify(d => d.UpdateAsync(
            "sprk_communicationaccount",
            accountId,
            It.Is<Dictionary<string, object>>(dict => (int)dict["sprk_sendstoday"] == 6),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncrementSendCountAsync_FromZero_SetsToOne()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var entity = new DataverseEntity("sprk_communicationaccount") { Id = accountId };
        entity["sprk_sendstoday"] = 0;

        _dataverseMock
            .Setup(d => d.RetrieveAsync("sprk_communicationaccount", accountId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _dataverseMock
            .Setup(d => d.UpdateAsync("sprk_communicationaccount", accountId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _accountService.IncrementSendCountAsync(accountId);

        // Assert
        _dataverseMock.Verify(d => d.UpdateAsync(
            "sprk_communicationaccount",
            accountId,
            It.Is<Dictionary<string, object>>(dict => (int)dict["sprk_sendstoday"] == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncrementSendCountAsync_InvalidatesSendEnabledCache()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var entity = new DataverseEntity("sprk_communicationaccount") { Id = accountId };
        entity["sprk_sendstoday"] = 0;

        _dataverseMock
            .Setup(d => d.RetrieveAsync("sprk_communicationaccount", accountId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _dataverseMock
            .Setup(d => d.UpdateAsync("sprk_communicationaccount", accountId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _accountService.IncrementSendCountAsync(accountId);

        // Assert — cache key "comm:accounts:send-enabled" should be removed
        _cacheMock.Verify(c => c.RemoveAsync("comm:accounts:send-enabled", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ResetAllSendCountsAsync Tests

    [Fact]
    public async Task ResetAllSendCountsAsync_ResetsNonZeroAccounts()
    {
        // Arrange
        var account1 = new DataverseEntity("sprk_communicationaccount") { Id = Guid.NewGuid() };
        account1["sprk_sendstoday"] = 10;
        var account2 = new DataverseEntity("sprk_communicationaccount") { Id = Guid.NewGuid() };
        account2["sprk_sendstoday"] = 3;

        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.Is<string>(f => f.Contains("sprk_sendstoday gt 0")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { account1, account2 });

        _dataverseMock
            .Setup(d => d.BulkUpdateAsync(
                "sprk_communicationaccount",
                It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _accountService.ResetAllSendCountsAsync();

        // Assert
        _dataverseMock.Verify(d => d.BulkUpdateAsync(
            "sprk_communicationaccount",
            It.Is<List<(Guid id, Dictionary<string, object> fields)>>(updates =>
                updates.Count == 2 &&
                updates.All(u => (int)u.fields["sprk_sendstoday"] == 0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetAllSendCountsAsync_NoNonZeroAccounts_SkipsBulkUpdate()
    {
        // Arrange
        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataverseEntity>());

        // Act
        await _accountService.ResetAllSendCountsAsync();

        // Assert — BulkUpdateAsync should NOT be called
        _dataverseMock.Verify(d => d.BulkUpdateAsync(
            It.IsAny<string>(),
            It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetAllSendCountsAsync_InvalidatesCache()
    {
        // Arrange
        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DataverseEntity("sprk_communicationaccount") { Id = Guid.NewGuid(), ["sprk_sendstoday"] = 1 }
            });

        _dataverseMock
            .Setup(d => d.BulkUpdateAsync(
                It.IsAny<string>(),
                It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _accountService.ResetAllSendCountsAsync();

        // Assert
        _cacheMock.Verify(c => c.RemoveAsync("comm:accounts:send-enabled", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Daily Limit Check Tests (Model-Level)

    [Theory]
    [InlineData(0, 100, false)]   // Under limit
    [InlineData(99, 100, false)]  // One under limit
    [InlineData(100, 100, true)]  // At limit
    [InlineData(101, 100, true)]  // Over limit
    [InlineData(50, null, false)] // No limit set
    public void DailyLimitExceeded_ReturnsExpected(int sendsToday, int? dailyLimit, bool expectExceeded)
    {
        var account = new CommunicationAccount
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            EmailAddress = "test@contoso.com",
            SendsToday = sendsToday,
            DailySendLimit = dailyLimit
        };

        var exceeded = account.DailySendLimit.HasValue && account.DailySendLimit > 0
            && account.SendsToday >= account.DailySendLimit;

        exceeded.Should().Be(expectExceeded);
    }

    [Fact]
    public void SendsToday_DefaultsToZero()
    {
        var account = new CommunicationAccount
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            EmailAddress = "test@contoso.com"
        };

        account.SendsToday.Should().Be(0);
        account.DailySendLimit.Should().BeNull();
    }

    #endregion

    #region DailySendCountResetService Tests

    [Fact]
    public void CalculateDelayUntilMidnightUtc_ReturnsPositiveDelay()
    {
        var delay = DailySendCountResetService.CalculateDelayUntilMidnightUtc();

        delay.Should().BeGreaterThan(TimeSpan.Zero);
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromDays(1));
    }

    [Fact]
    public void CalculateDelayUntilMidnightUtc_ReturnsAtLeastOneSecond()
    {
        // The method has a safety floor of 1 second
        var delay = DailySendCountResetService.CalculateDelayUntilMidnightUtc();

        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
    }

    #endregion

    #region CommunicationAccount Mapping Tests

    [Fact]
    public async Task MapToCommunicationAccount_IncludesSendCountFields()
    {
        // Arrange: set up a Dataverse entity with send count fields
        var entity = new DataverseEntity("sprk_communicationaccount") { Id = Guid.NewGuid() };
        entity["sprk_emailaddress"] = "sender@contoso.com";
        entity["sprk_name"] = "Sender Account";
        entity["sprk_sendenabled"] = true;
        entity["sprk_isdefaultsender"] = true;
        entity["sprk_accounttype"] = new OptionSetValue(100000000);
        entity["sprk_sendstoday"] = 42;
        entity["sprk_dailysendlimit"] = 100;

        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entity });

        // Act
        var accounts = await _accountService.QuerySendEnabledAccountsAsync();

        // Assert
        accounts.Should().HaveCount(1);
        accounts[0].SendsToday.Should().Be(42);
        accounts[0].DailySendLimit.Should().Be(100);
    }

    [Fact]
    public async Task MapToCommunicationAccount_NullDailySendLimit_MapsToNull()
    {
        // Arrange: entity without sprk_dailysendlimit field
        var entity = new DataverseEntity("sprk_communicationaccount") { Id = Guid.NewGuid() };
        entity["sprk_emailaddress"] = "sender@contoso.com";
        entity["sprk_name"] = "Sender Account";
        entity["sprk_sendenabled"] = true;
        entity["sprk_isdefaultsender"] = false;
        entity["sprk_accounttype"] = new OptionSetValue(100000000);
        entity["sprk_sendstoday"] = 0;
        // Note: sprk_dailysendlimit is NOT set

        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { entity });

        // Act
        var accounts = await _accountService.QuerySendEnabledAccountsAsync();

        // Assert
        accounts.Should().HaveCount(1);
        accounts[0].SendsToday.Should().Be(0);
        accounts[0].DailySendLimit.Should().BeNull();
    }

    #endregion
}
