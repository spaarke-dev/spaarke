using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class ApprovedSenderValidatorTests
{
    #region Test Data Builders

    private static ApprovedSenderConfig CreateSender(
        string email = "sender@contoso.com",
        string displayName = "Test Sender",
        bool isDefault = false) => new()
    {
        Email = email,
        DisplayName = displayName,
        IsDefault = isDefault
    };

    private static CommunicationOptions CreateOptions(
        ApprovedSenderConfig[]? senders = null,
        string? defaultMailbox = null) => new()
    {
        ApprovedSenders = senders ?? Array.Empty<ApprovedSenderConfig>(),
        DefaultMailbox = defaultMailbox
    };

    private static ApprovedSenderValidator CreateValidator(CommunicationOptions options)
        => new(
            Options.Create(options),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

    #endregion

    #region Resolve_WithNullFromMailbox (Default Resolution)

    [Fact]
    public void Resolve_WithNullFromMailbox_ReturnsDefaultSender()
    {
        // Arrange
        var options = CreateOptions(senders: new[]
        {
            CreateSender("regular@contoso.com", "Regular Sender"),
            CreateSender("default@contoso.com", "Default Sender", isDefault: true),
            CreateSender("another@contoso.com", "Another Sender")
        });
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("default@contoso.com");
        result.DisplayName.Should().Be("Default Sender");
        result.ErrorCode.Should().BeNull();
        result.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithNullFromMailbox_FallsBackToDefaultMailbox()
    {
        // Arrange — no sender has IsDefault=true, but DefaultMailbox matches a sender
        var options = CreateOptions(
            senders: new[]
            {
                CreateSender("first@contoso.com", "First Sender"),
                CreateSender("fallback@contoso.com", "Fallback Sender")
            },
            defaultMailbox: "fallback@contoso.com");
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("fallback@contoso.com");
        result.DisplayName.Should().Be("Fallback Sender");
    }

    [Fact]
    public void Resolve_WithNullFromMailbox_FallsBackToFirstSender()
    {
        // Arrange — no IsDefault, no DefaultMailbox; falls back to first sender
        var options = CreateOptions(senders: new[]
        {
            CreateSender("first@contoso.com", "First Sender"),
            CreateSender("second@contoso.com", "Second Sender")
        });
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("first@contoso.com");
        result.DisplayName.Should().Be("First Sender");
    }

    [Fact]
    public void Resolve_WithNullFromMailbox_NoSendersConfigured_ReturnsNoDefaultSenderError()
    {
        // Arrange — empty approved senders list
        var options = CreateOptions(senders: Array.Empty<ApprovedSenderConfig>());
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: null);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_DEFAULT_SENDER");
        result.ErrorDetail.Should().Contain("No default sender is configured");
        result.Email.Should().BeNull();
        result.DisplayName.Should().BeNull();
    }

    #endregion

    #region Resolve_WithExplicitFromMailbox

    [Fact]
    public void Resolve_WithValidSender_ReturnsSuccess()
    {
        // Arrange
        var options = CreateOptions(senders: new[]
        {
            CreateSender("noreply@contoso.com", "No Reply"),
            CreateSender("support@contoso.com", "Support Team")
        });
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: "support@contoso.com");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("support@contoso.com");
        result.DisplayName.Should().Be("Support Team");
    }

    [Fact]
    public void Resolve_WithValidSender_CaseInsensitive_ReturnsSuccess()
    {
        // Arrange — sender configured in lowercase, request in uppercase
        var options = CreateOptions(senders: new[]
        {
            CreateSender("support@contoso.com", "Support Team")
        });
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: "SUPPORT@CONTOSO.COM");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("support@contoso.com");
        result.DisplayName.Should().Be("Support Team");
    }

    [Fact]
    public void Resolve_WithInvalidSender_ReturnsInvalidSenderError()
    {
        // Arrange
        var options = CreateOptions(senders: new[]
        {
            CreateSender("approved@contoso.com", "Approved Sender")
        });
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: "unauthorized@evil.com");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_SENDER");
        result.ErrorDetail.Should().Contain("unauthorized@evil.com");
        result.ErrorDetail.Should().Contain("not in the approved senders list");
        result.Email.Should().BeNull();
        result.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithEmptyApprovedList_ReturnsInvalidSenderError()
    {
        // Arrange — no senders at all, explicit mailbox requested
        var options = CreateOptions(senders: Array.Empty<ApprovedSenderConfig>());
        var validator = CreateValidator(options);

        // Act
        var result = validator.Resolve(fromMailbox: "anyone@contoso.com");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_SENDER");
        result.ErrorDetail.Should().Contain("anyone@contoso.com");
    }

    #endregion
}
