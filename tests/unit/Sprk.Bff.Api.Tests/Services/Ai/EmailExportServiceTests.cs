using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Me.SendMail;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for EmailExportService - email sending via Microsoft Graph API.
/// Tests validation, email building, and Graph API interactions.
/// </summary>
public class EmailExportServiceTests
{
    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ILogger<EmailExportService>> _loggerMock;

    public EmailExportServiceTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<EmailExportService>>();
    }

    private EmailExportService CreateService(AnalysisOptions? options = null, IEnumerable<IExportService>? exportServices = null)
    {
        options ??= new AnalysisOptions { EnableEmailExport = true };

        return new EmailExportService(
            _graphClientFactoryMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object,
            Options.Create(options),
            exportServices ?? []);
    }

    private static ExportContext CreateValidContext(
        string? title = null,
        string? content = null,
        string[]? emailTo = null)
    {
        return new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = title ?? "Test Analysis Report",
            Content = content ?? "This is the main analysis content.",
            Summary = "Brief summary.",
            SourceDocumentName = "test-document.pdf",
            SourceDocumentId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "Test User",
            Options = new ExportOptions
            {
                EmailTo = emailTo ?? ["test@example.com"]
            }
        };
    }

    #region Format Property Tests

    [Fact]
    public void Format_ShouldReturnEmail()
    {
        // Arrange
        var service = CreateService();

        // Act
        var format = service.Format;

        // Assert
        format.Should().Be(ExportFormat.Email);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidContext_ShouldReturnIsValid()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithEmptyContent_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(content: string.Empty);

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("content"));
    }

    [Fact]
    public void Validate_WithEmptyTitle_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = string.Empty,
            Content = "Valid content",
            Options = new ExportOptions { EmailTo = ["test@example.com"] }
        };

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("title"));
    }

    [Fact]
    public void Validate_WhenEmailExportDisabled_ShouldReturnError()
    {
        // Arrange
        var options = new AnalysisOptions { EnableEmailExport = false };
        var service = CreateService(options);
        var context = CreateValidContext();

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not enabled"));
    }

    [Fact]
    public void Validate_WithNoRecipients_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = "Test Report",
            Content = "Valid content",
            Options = new ExportOptions { EmailTo = null }
        };

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("recipient"));
    }

    [Fact]
    public void Validate_WithEmptyRecipients_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = "Test Report",
            Content = "Valid content",
            Options = new ExportOptions { EmailTo = [] }
        };

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("recipient"));
    }

    [Fact]
    public void Validate_WithInvalidEmailAddress_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(emailTo: ["not-an-email"]);

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid email"));
    }

    [Fact]
    public void Validate_WithMultipleValidEmails_ShouldReturnValid()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(emailTo: ["user1@example.com", "user2@test.org"]);

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMixedValidAndInvalidEmails_ShouldReturnErrors()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(emailTo: ["valid@example.com", "invalid", "another-bad"]);

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Contains("invalid"));
        result.Errors.Should().Contain(e => e.Contains("another-bad"));
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WhenEmailExportDisabled_ShouldFail()
    {
        // Arrange
        var options = new AnalysisOptions { EnableEmailExport = false };
        var service = CreateService(options);
        var context = CreateValidContext();

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not enabled");
    }

    [Fact]
    public async Task ExportAsync_WithNoRecipients_ShouldFail()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = "Test Report",
            Content = "Valid content",
            Options = null
        };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("recipients");
    }

    [Fact]
    public async Task ExportAsync_WhenHttpContextMissing_ShouldFail()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("user context");
    }

    [Fact]
    public async Task ExportAsync_WithNoOptions_ShouldFail()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = "Test",
            Content = "Content",
            Options = new ExportOptions { EmailTo = null }
        };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("recipients");
    }

    #endregion

    #region Email Content Tests

    [Fact]
    public void Validate_ContentWithHtmlTags_ShouldBeValid()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(content: "<p>Some <b>bold</b> text</p>");

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ContentWithSpecialCharacters_ShouldBeValid()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(content: "Contract value: $100,000 & more <test>");

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Validate_WithWhitespaceOnlyContent_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(content: "   \t\n   ");

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("content"));
    }

    [Fact]
    public void Validate_WithWhitespaceOnlyTitle_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = "   ",
            Content = "Valid content",
            Options = new ExportOptions { EmailTo = ["test@example.com"] }
        };

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("title"));
    }

    [Fact]
    public void Validate_WithWhitespaceEmail_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(emailTo: ["   "]);

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid email"));
    }

    #endregion
}
