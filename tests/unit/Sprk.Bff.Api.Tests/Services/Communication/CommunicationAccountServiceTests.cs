using System.Text;
using System.Text.Json;
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

public class CommunicationAccountServiceTests
{
    #region Test Infrastructure

    private readonly Mock<IDataverseService> _dataverseMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<CommunicationAccountService>> _loggerMock;
    private readonly CommunicationAccountService _sut;

    public CommunicationAccountServiceTests()
    {
        _dataverseMock = new Mock<IDataverseService>();
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<CommunicationAccountService>>();

        // Default: cache miss (returns null bytes)
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Default: Dataverse returns empty array
        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataverseEntity>());

        _sut = new CommunicationAccountService(
            _dataverseMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    #endregion

    #region Test Data Builders

    private static DataverseEntity CreateAccountEntity(
        string email,
        string name,
        string? displayName = null,
        bool sendEnabled = true,
        bool isDefault = false,
        bool receiveEnabled = false,
        int accountType = 100000000,
        string? monitorFolder = null,
        bool autoCreateRecords = false,
        string? subscriptionId = null,
        DateTime? subscriptionExpiry = null,
        string? securityGroupId = null,
        string? securityGroupName = null,
        int? verificationStatus = null,
        DateTime? lastVerified = null)
    {
        var entity = new DataverseEntity("sprk_communicationaccount");
        entity.Id = Guid.NewGuid();
        entity["sprk_emailaddress"] = email;
        entity["sprk_name"] = name;
        if (displayName != null) entity["sprk_displayname"] = displayName;
        entity["sprk_sendenableds"] = sendEnabled;
        entity["sprk_isdefaultsender"] = isDefault;
        entity["sprk_receiveenabled"] = receiveEnabled;
        entity["sprk_accounttype"] = new OptionSetValue(accountType);
        entity["sprk_autocreaterecords"] = autoCreateRecords;
        if (monitorFolder != null) entity["sprk_monitorfolder"] = monitorFolder;
        if (subscriptionId != null) entity["sprk_subscriptionid"] = subscriptionId;
        if (subscriptionExpiry.HasValue) entity["sprk_subscriptionexpiry"] = subscriptionExpiry.Value;
        if (securityGroupId != null) entity["sprk_securitygroupid"] = securityGroupId;
        if (securityGroupName != null) entity["sprk_securitygroupname"] = securityGroupName;
        if (verificationStatus.HasValue) entity["sprk_verificationstatus"] = new OptionSetValue(verificationStatus.Value);
        if (lastVerified.HasValue) entity["sprk_lastverified"] = lastVerified.Value;
        return entity;
    }

    private void SetupCacheHit(CommunicationAccount[] accounts, string cacheKey = "comm:accounts:send-enabled")
    {
        var json = JsonSerializer.Serialize(accounts);
        var bytes = Encoding.UTF8.GetBytes(json);
        _cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
    }

    private void SetupCacheMiss(string? cacheKey = null)
    {
        if (cacheKey is null)
        {
            _cacheMock
                .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);
        }
        else
        {
            _cacheMock
                .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);
        }
    }

    private void SetupDataverseReturns(params DataverseEntity[] entities)
    {
        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entities);
    }

    private static CommunicationAccount CreateAccount(
        string email = "test@contoso.com",
        string name = "Test Account",
        bool sendEnabled = true,
        bool isDefault = false,
        AccountType accountType = AccountType.SharedAccount) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        EmailAddress = email,
        AccountType = accountType,
        SendEnabled = sendEnabled,
        IsDefaultSender = isDefault
    };

    #endregion

    #region QuerySendEnabledAccountsAsync — Cache Miss

    [Fact]
    public async Task QuerySendEnabledAccountsAsync_WhenCacheMiss_QueriesDataverse()
    {
        // Arrange
        SetupCacheMiss();
        var entity = CreateAccountEntity("noreply@contoso.com", "Contoso Noreply", sendEnabled: true);
        SetupDataverseReturns(entity);

        // Act
        var result = await _sut.QuerySendEnabledAccountsAsync();

        // Assert
        _dataverseMock.Verify(d => d.QueryCommunicationAccountsAsync(
            It.Is<string>(f => f.Contains("sprk_sendenableds eq true") && f.Contains("statecode eq 0")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().HaveCount(1);
    }

    #endregion

    #region QuerySendEnabledAccountsAsync — Cache Hit

    [Fact]
    public async Task QuerySendEnabledAccountsAsync_WhenCacheHit_ReturnsCachedAccounts()
    {
        // Arrange
        var cached = new[]
        {
            CreateAccount("cached@contoso.com", "Cached Account")
        };
        SetupCacheHit(cached, "comm:accounts:send-enabled");

        // Act
        var result = await _sut.QuerySendEnabledAccountsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].EmailAddress.Should().Be("cached@contoso.com");

        // Dataverse should NOT be queried when cache hits
        _dataverseMock.Verify(d => d.QueryCommunicationAccountsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region QuerySendEnabledAccountsAsync — Cache Failure

    [Fact]
    public async Task QuerySendEnabledAccountsAsync_WhenCacheFails_QueriesDataverse()
    {
        // Arrange — cache throws an exception
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection failed"));

        var entity = CreateAccountEntity("fallback@contoso.com", "Fallback Account");
        SetupDataverseReturns(entity);

        // Act
        var result = await _sut.QuerySendEnabledAccountsAsync();

        // Assert — should fall through to Dataverse
        result.Should().HaveCount(1);
        result[0].EmailAddress.Should().Be("fallback@contoso.com");
        _dataverseMock.Verify(d => d.QueryCommunicationAccountsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region QuerySendEnabledAccountsAsync — Dataverse Failure (Graceful Degradation)

    [Fact]
    public async Task QuerySendEnabledAccountsAsync_WhenDataverseFails_ReturnsEmptyArray()
    {
        // Arrange — cache miss and Dataverse throws
        SetupCacheMiss();
        _dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        // Act
        var result = await _sut.QuerySendEnabledAccountsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region QuerySendEnabledAccountsAsync — Entity Mapping

    [Fact]
    public async Task QuerySendEnabledAccountsAsync_MapsEntityCorrectly()
    {
        // Arrange
        SetupCacheMiss();
        var entity = CreateAccountEntity(
            email: "shared@contoso.com",
            name: "Shared Mailbox",
            displayName: "Contoso Shared",
            sendEnabled: true,
            isDefault: true,
            receiveEnabled: true,
            accountType: 100000001, // ServiceAccount
            monitorFolder: "Inbox",
            autoCreateRecords: true,
            subscriptionId: "sub-123",
            subscriptionExpiry: new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            securityGroupId: "sg-abc",
            securityGroupName: "Legal Team",
            verificationStatus: 100000000, // Verified
            lastVerified: new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc));
        SetupDataverseReturns(entity);

        // Act
        var result = await _sut.QuerySendEnabledAccountsAsync();

        // Assert
        result.Should().HaveCount(1);
        var account = result[0];
        account.Id.Should().Be(entity.Id);
        account.Name.Should().Be("Shared Mailbox");
        account.EmailAddress.Should().Be("shared@contoso.com");
        account.DisplayName.Should().Be("Contoso Shared");
        account.AccountType.Should().Be(AccountType.ServiceAccount);
        account.SendEnabled.Should().BeTrue();
        account.IsDefaultSender.Should().BeTrue();
        account.ReceiveEnabled.Should().BeTrue();
        account.MonitorFolder.Should().Be("Inbox");
        account.AutoCreateRecords.Should().BeTrue();
        account.SubscriptionId.Should().Be("sub-123");
        account.SubscriptionExpiry.Should().NotBeNull();
        account.SecurityGroupId.Should().Be("sg-abc");
        account.SecurityGroupName.Should().Be("Legal Team");
        account.VerificationStatus.Should().Be(Sprk.Bff.Api.Services.Communication.Models.VerificationStatus.Verified);
        account.LastVerified.Should().NotBeNull();
    }

    [Fact]
    public async Task QuerySendEnabledAccountsAsync_MapsOptionSetValues_ForAccountType()
    {
        // Arrange — verify each AccountType maps correctly
        SetupCacheMiss();
        var sharedEntity = CreateAccountEntity("shared@contoso.com", "Shared", accountType: 100000000);
        var serviceEntity = CreateAccountEntity("service@contoso.com", "Service", accountType: 100000001);
        var userEntity = CreateAccountEntity("user@contoso.com", "User", accountType: 100000002);
        SetupDataverseReturns(sharedEntity, serviceEntity, userEntity);

        // Act
        var result = await _sut.QuerySendEnabledAccountsAsync();

        // Assert
        result.Should().HaveCount(3);
        result[0].AccountType.Should().Be(AccountType.SharedAccount);
        result[1].AccountType.Should().Be(AccountType.ServiceAccount);
        result[2].AccountType.Should().Be(AccountType.UserAccount);
    }

    #endregion

    #region QuerySendEnabledAccountsAsync — Caches Result After Dataverse Query

    [Fact]
    public async Task QuerySendEnabledAccountsAsync_WhenCacheMiss_CachesResult()
    {
        // Arrange
        SetupCacheMiss();
        var entity = CreateAccountEntity("cache-me@contoso.com", "Cache Me");
        SetupDataverseReturns(entity);

        // Act
        await _sut.QuerySendEnabledAccountsAsync();

        // Assert — SetAsync should be called to cache the result
        _cacheMock.Verify(c => c.SetAsync(
            "comm:accounts:send-enabled",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(5)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetDefaultSendAccountAsync

    [Fact]
    public async Task GetDefaultSendAccountAsync_ReturnsDefaultSender()
    {
        // Arrange
        SetupCacheMiss();
        var regularEntity = CreateAccountEntity("regular@contoso.com", "Regular", isDefault: false);
        var defaultEntity = CreateAccountEntity("default@contoso.com", "Default Sender", isDefault: true);
        var anotherEntity = CreateAccountEntity("another@contoso.com", "Another", isDefault: false);
        SetupDataverseReturns(regularEntity, defaultEntity, anotherEntity);

        // Act
        var result = await _sut.GetDefaultSendAccountAsync();

        // Assert
        result.Should().NotBeNull();
        result!.EmailAddress.Should().Be("default@contoso.com");
        result.IsDefaultSender.Should().BeTrue();
    }

    [Fact]
    public async Task GetDefaultSendAccountAsync_WhenNoDefault_ReturnsFirst()
    {
        // Arrange — no account has IsDefaultSender=true
        SetupCacheMiss();
        var firstEntity = CreateAccountEntity("first@contoso.com", "First Account", isDefault: false);
        var secondEntity = CreateAccountEntity("second@contoso.com", "Second Account", isDefault: false);
        SetupDataverseReturns(firstEntity, secondEntity);

        // Act
        var result = await _sut.GetDefaultSendAccountAsync();

        // Assert
        result.Should().NotBeNull();
        result!.EmailAddress.Should().Be("first@contoso.com");
    }

    [Fact]
    public async Task GetDefaultSendAccountAsync_WhenEmpty_ReturnsNull()
    {
        // Arrange — no accounts at all
        SetupCacheMiss();
        SetupDataverseReturns(); // empty

        // Act
        var result = await _sut.GetDefaultSendAccountAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetSendAccountByEmailAsync

    [Fact]
    public async Task GetSendAccountByEmailAsync_FindsMatchCaseInsensitive()
    {
        // Arrange
        SetupCacheMiss();
        var entity = CreateAccountEntity("NoReply@Contoso.COM", "Contoso NoReply");
        SetupDataverseReturns(entity);

        // Act — search with different casing
        var result = await _sut.GetSendAccountByEmailAsync("noreply@contoso.com");

        // Assert
        result.Should().NotBeNull();
        result!.EmailAddress.Should().Be("NoReply@Contoso.COM");
    }

    [Fact]
    public async Task GetSendAccountByEmailAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        SetupCacheMiss();
        var entity = CreateAccountEntity("existing@contoso.com", "Existing");
        SetupDataverseReturns(entity);

        // Act
        var result = await _sut.GetSendAccountByEmailAsync("nonexistent@contoso.com");

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
