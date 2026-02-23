using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests the Phase 2 approved sender merge logic in ApprovedSenderValidator.ResolveAsync.
/// Covers: cache hits, cache misses, Dataverse queries, merge precedence, and failure fallback.
/// </summary>
public class ApprovedSenderMergeTests
{
    #region Test Infrastructure

    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<IDistributedCache> _accountCacheMock;
    private readonly Mock<ILogger<ApprovedSenderValidator>> _loggerMock;

    public ApprovedSenderMergeTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _cacheMock = new Mock<IDistributedCache>();
        _accountCacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<ApprovedSenderValidator>>();

        // Always cache miss for CommunicationAccountService's cache
        _accountCacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
    }

    private ApprovedSenderValidator CreateValidator(
        ApprovedSenderConfig[]? senders = null,
        string? defaultMailbox = null)
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = senders ?? Array.Empty<ApprovedSenderConfig>(),
            DefaultMailbox = defaultMailbox
        };

        var accountService = new CommunicationAccountService(
            _dataverseServiceMock.Object,
            _accountCacheMock.Object,
            Mock.Of<ILogger<CommunicationAccountService>>());

        return new ApprovedSenderValidator(
            Options.Create(options),
            accountService,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    private static ApprovedSenderConfig CreateSender(
        string email,
        string displayName = "Test Sender",
        bool isDefault = false) => new()
    {
        Email = email,
        DisplayName = displayName,
        IsDefault = isDefault
    };

    private static DataverseEntity CreateCommunicationAccountEntity(
        string email,
        string name,
        bool isDefault = false)
    {
        var entity = new DataverseEntity("sprk_communicationaccount");
        entity.Id = Guid.NewGuid();
        entity["sprk_emailaddress"] = email;
        entity["sprk_name"] = name;
        entity["sprk_displayname"] = name;
        entity["sprk_isdefaultsender"] = isDefault;
        entity["sprk_sendenableds"] = true;
        entity["sprk_accounttype"] = new OptionSetValue(100000000); // SharedAccount
        return entity;
    }

    /// <summary>
    /// Sets up the distributed cache mock to return a cached value via GetAsync.
    /// The GetStringAsync extension method calls GetAsync internally.
    /// </summary>
    private void SetupCacheHit(ApprovedSenderConfig[] cachedSenders)
    {
        var json = JsonSerializer.Serialize(cachedSenders);
        var bytes = Encoding.UTF8.GetBytes(json);

        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);
    }

    /// <summary>
    /// Sets up the distributed cache mock to return null (cache miss).
    /// </summary>
    private void SetupCacheMiss()
    {
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    /// <summary>
    /// Sets up the distributed cache mock to throw (Redis unavailable).
    /// </summary>
    private void SetupCacheFailure()
    {
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection refused"));
    }

    #endregion

    #region Cache Hit - Skips Dataverse Query

    [Fact]
    public async Task ResolveAsync_WhenCacheHit_ReturnsCachedSenders()
    {
        // Arrange
        var cachedSenders = new[]
        {
            CreateSender("cached@contoso.com", "Cached Sender", isDefault: true)
        };
        SetupCacheHit(cachedSenders);

        var validator = CreateValidator(
            senders: new[] { CreateSender("config@contoso.com", "Config Sender", isDefault: true) });

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("cached@contoso.com", "cached senders should be returned, not config senders");
    }

    [Fact]
    public async Task ResolveAsync_WhenCacheHit_DoesNotQueryDataverse()
    {
        // Arrange
        var cachedSenders = new[]
        {
            CreateSender("cached@contoso.com", "Cached Sender", isDefault: true)
        };
        SetupCacheHit(cachedSenders);

        var validator = CreateValidator(
            senders: new[] { CreateSender("config@contoso.com", "Config Sender", isDefault: true) });

        // Act
        await validator.ResolveAsync(fromMailbox: null);

        // Assert
        _dataverseServiceMock.Verify(
            ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Dataverse should not be queried when cache hit occurs");
    }

    #endregion

    #region Cache Miss - Queries Dataverse and Caches

    [Fact]
    public async Task ResolveAsync_WhenCacheMiss_QueriesDataverse()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("dv-sender@contoso.com", "DV Sender", isDefault: true)
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("config@contoso.com", "Config Sender") });

        // Act
        var result = await validator.ResolveAsync(fromMailbox: "dv-sender@contoso.com");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("dv-sender@contoso.com");

        _dataverseServiceMock.Verify(
            ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Dataverse should be queried on cache miss");
    }

    [Fact]
    public async Task ResolveAsync_WhenCacheMiss_CachesMergedResults()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("dv@contoso.com", "DV Sender")
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("config@contoso.com", "Config Sender", isDefault: true) });

        // Act
        await validator.ResolveAsync(fromMailbox: null);

        // Assert
        _cacheMock.Verify(
            c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "merged senders should be cached after Dataverse query");
    }

    #endregion

    #region Merge Precedence: Dataverse Wins on Email Match

    [Fact]
    public async Task ResolveAsync_WhenDataverseHasMatchingEmail_DataverseWins()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("shared@contoso.com", "DV Display Name Override", isDefault: true)
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("shared@contoso.com", "Config Display Name", isDefault: false) });

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("shared@contoso.com");
        result.DisplayName.Should().Be("DV Display Name Override",
            "Dataverse sender should override config sender on email match");
    }

    [Fact]
    public async Task ResolveAsync_WhenDataverseEmailMatchesCaseInsensitive_DataverseWins()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("SHARED@CONTOSO.COM", "DV Override Upper", isDefault: true)
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("shared@contoso.com", "Config Lower") });

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.DisplayName.Should().Be("DV Override Upper",
            "Dataverse wins on case-insensitive email match");
    }

    #endregion

    #region Dataverse-Only Senders (Not in Config)

    [Fact]
    public async Task ResolveAsync_WhenDataverseHasNewSender_AddsToMergedList()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("new-dv@contoso.com", "New DV Sender")
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("config@contoso.com", "Config Sender", isDefault: true) });

        // Act - request the new Dataverse sender explicitly
        var result = await validator.ResolveAsync(fromMailbox: "new-dv@contoso.com");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("new-dv@contoso.com",
            "Dataverse-only senders should be added to the merged list");
        result.DisplayName.Should().Be("New DV Sender");
    }

    #endregion

    #region Config-Only Fallback (Dataverse Unavailable)

    [Fact]
    public async Task ResolveAsync_WhenDataverseThrows_FallsBackToConfigOnly()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse connection failed"));

        var validator = CreateValidator(
            senders: new[]
            {
                CreateSender("config-sender@contoso.com", "Config Fallback", isDefault: true)
            });

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("config-sender@contoso.com",
            "should fall back to config-only senders when Dataverse fails");
        result.DisplayName.Should().Be("Config Fallback");
    }

    [Fact]
    public async Task ResolveAsync_WhenDataverseThrows_DoesNotThrow()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse timeout"));

        var validator = CreateValidator(
            senders: new[]
            {
                CreateSender("safe@contoso.com", "Safe Sender", isDefault: true)
            });

        // Act
        var act = () => validator.ResolveAsync(fromMailbox: null);

        // Assert
        await act.Should().NotThrowAsync("Dataverse failure should be caught and fall back to config");
    }

    #endregion

    #region Both Cache and Dataverse Fail

    [Fact]
    public async Task ResolveAsync_WhenBothCacheAndDataverseFail_FallsBackToConfig()
    {
        // Arrange - cache throws, then Dataverse also throws
        SetupCacheFailure();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse down"));

        var validator = CreateValidator(
            senders: new[]
            {
                CreateSender("emergency@contoso.com", "Emergency Fallback", isDefault: true)
            });

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("emergency@contoso.com",
            "when both cache and Dataverse fail, config senders are the final fallback");
    }

    #endregion

    #region Dataverse Entity Mapping

    [Fact]
    public async Task ResolveAsync_MapsDataverseEntityFields_Correctly()
    {
        // Arrange
        SetupCacheMiss();

        var dvEntity = new DataverseEntity("sprk_communicationaccount");
        dvEntity.Id = Guid.NewGuid();
        dvEntity["sprk_emailaddress"] = "mapped@contoso.com";
        dvEntity["sprk_name"] = "Mapped Display Name";
        dvEntity["sprk_displayname"] = "Mapped Display Name";
        dvEntity["sprk_isdefaultsender"] = true;
        dvEntity["sprk_sendenableds"] = true;
        dvEntity["sprk_accounttype"] = new OptionSetValue(100000000);

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { dvEntity });

        var validator = CreateValidator();

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("mapped@contoso.com");
        result.DisplayName.Should().Be("Mapped Display Name");
    }

    [Fact]
    public async Task ResolveAsync_SkipsDataverseEntities_WithEmptyEmail()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("", "Empty Email Sender"),
                CreateCommunicationAccountEntity("valid@contoso.com", "Valid Sender", isDefault: true)
            });

        var validator = CreateValidator();

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("valid@contoso.com",
            "senders with empty email should be filtered out");
    }

    #endregion

    #region Merge with Multiple Senders

    [Fact]
    public async Task ResolveAsync_MergesMultipleSenders_Correctly()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("config@contoso.com", "DV Override", isDefault: false),
                CreateCommunicationAccountEntity("dv-only@contoso.com", "DV Only", isDefault: false)
            });

        var validator = CreateValidator(
            senders: new[]
            {
                CreateSender("config@contoso.com", "Config Original", isDefault: true),
                CreateSender("config-only@contoso.com", "Config Only", isDefault: false)
            });

        // Act - request each one
        var configOverridden = await validator.ResolveAsync(fromMailbox: "config@contoso.com");
        var configOnly = await validator.ResolveAsync(fromMailbox: "config-only@contoso.com");
        var dvOnly = await validator.ResolveAsync(fromMailbox: "dv-only@contoso.com");

        // Assert
        configOverridden.IsValid.Should().BeTrue();
        configOverridden.DisplayName.Should().Be("DV Override",
            "Dataverse should override config for matching email");

        configOnly.IsValid.Should().BeTrue();
        configOnly.DisplayName.Should().Be("Config Only",
            "config-only senders should still be available");

        dvOnly.IsValid.Should().BeTrue();
        dvOnly.DisplayName.Should().Be("DV Only",
            "Dataverse-only senders should be added to merged list");
    }

    #endregion

    #region Default Sender Resolution via ResolveAsync

    [Fact]
    public async Task ResolveAsync_WithNullFromMailbox_ReturnsDefaultFromMergedList()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("dv-default@contoso.com", "DV Default Sender", isDefault: true)
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("config@contoso.com", "Config Sender") });

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("dv-default@contoso.com",
            "Dataverse default sender should be returned when fromMailbox is null");
    }

    #endregion

    #region Cache Write Failure (Non-Fatal)

    [Fact]
    public async Task ResolveAsync_WhenCacheWriteFails_StillReturnsMergedSenders()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("sender@contoso.com", "Test Sender", isDefault: true)
            });

        // Cache write (SetAsync) throws
        _cacheMock
            .Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis write failed"));

        var validator = CreateValidator();

        // Act
        var result = await validator.ResolveAsync(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("sender@contoso.com",
            "cache write failure is non-fatal; merged senders should still be returned");
    }

    #endregion

    #region Explicit Sender Validation via ResolveAsync

    [Fact]
    public async Task ResolveAsync_WithExplicitSender_ValidatesAgainstMergedList()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("approved-dv@contoso.com", "DV Approved")
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("approved-config@contoso.com", "Config Approved", isDefault: true) });

        // Act - try an unapproved sender
        var result = await validator.ResolveAsync(fromMailbox: "unauthorized@evil.com");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_SENDER");
    }

    [Fact]
    public async Task ResolveAsync_WithExplicitDataverseSender_Succeeds()
    {
        // Arrange
        SetupCacheMiss();

        _dataverseServiceMock
            .Setup(ds => ds.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateCommunicationAccountEntity("dv-approved@contoso.com", "DV Approved Sender")
            });

        var validator = CreateValidator(
            senders: new[] { CreateSender("config@contoso.com", "Config Sender", isDefault: true) });

        // Act - request sender that only exists in Dataverse
        var result = await validator.ResolveAsync(fromMailbox: "dv-approved@contoso.com");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("dv-approved@contoso.com");
        result.DisplayName.Should().Be("DV Approved Sender");
    }

    #endregion
}
