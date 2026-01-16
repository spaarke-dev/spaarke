using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for SendEmailNodeExecutor.
/// Tests validation, template substitution, and email configuration.
/// </summary>
public class SendEmailNodeExecutorTests
{
    private readonly Mock<ITemplateEngine> _templateEngineMock;
    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ILogger<SendEmailNodeExecutor>> _loggerMock;
    private readonly SendEmailNodeExecutor _executor;

    public SendEmailNodeExecutorTests()
    {
        _templateEngineMock = new Mock<ITemplateEngine>();
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<SendEmailNodeExecutor>>();
        _executor = new SendEmailNodeExecutor(
            _templateEngineMock.Object,
            _graphClientFactoryMock.Object,
            _httpContextAccessorMock.Object,
            _loggerMock.Object);
    }

    #region SupportedActionTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsSendEmail()
    {
        // Assert
        _executor.SupportedActionTypes.Should().Contain(ActionType.SendEmail);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var config = @"{
            ""to"":[""user@example.com""],
            ""subject"":""Test Subject"",
            ""body"":""Test body content""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithNoConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(null);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public void Validate_WithEmptyConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("{invalid json}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid"));
    }

    [Fact]
    public void Validate_WithMissingTo_ReturnsFailure()
    {
        // Arrange
        var config = @"{""subject"":""Subject"",""body"":""Body""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("recipient") || e.Contains("To"));
    }

    [Fact]
    public void Validate_WithEmptyTo_ReturnsFailure()
    {
        // Arrange
        var config = @"{""to"":[],""subject"":""Subject"",""body"":""Body""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("recipient") || e.Contains("To"));
    }

    [Fact]
    public void Validate_WithMissingSubject_ReturnsFailure()
    {
        // Arrange
        var config = @"{""to"":[""user@example.com""],""body"":""Body""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("subject"));
    }

    [Fact]
    public void Validate_WithMissingBody_ReturnsFailure()
    {
        // Arrange
        var config = @"{""to"":[""user@example.com""],""subject"":""Subject""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("body"));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithNoHttpContext_ReturnsErrorOutput()
    {
        // Arrange
        var config = @"{
            ""to"":[""user@example.com""],
            ""subject"":""Test"",
            ""body"":""Body""
        }";
        var context = CreateValidContext(config);

        _httpContextAccessorMock.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("HttpContext");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidationFailure_ReturnsErrorOutput()
    {
        // Arrange
        var context = CreateValidContext("{}"); // Invalid config

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public void Validate_WithMultipleRecipients_ReturnsSuccess()
    {
        // Arrange
        var config = @"{
            ""to"":[""user1@example.com"",""user2@example.com""],
            ""cc"":[""cc@example.com""],
            ""subject"":""Test"",
            ""body"":""Body"",
            ""isHtml"":true
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithTemplateVariablesInRecipients_ReturnsSuccess()
    {
        // Arrange
        var config = @"{
            ""to"":[""{{analysis.output.recipientEmail}}""],
            ""subject"":""Analysis Complete: {{analysis.output.docName}}"",
            ""body"":""{{analysis.output.summary}}""
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private void SetupMockPassThrough()
    {
        // Mock both overloads - compiler chooses generic version for Dictionary<string, object?>
        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns((string template, IDictionary<string, object?> _) => template);

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns((string template, Dictionary<string, object?> _) => template);
    }

    private static NodeExecutionContext CreateValidContext(string? configJson)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Send Email Node",
                ExecutionOrder = 1,
                OutputVariable = "emailResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Send Email"
            },
            ActionType = ActionType.SendEmail,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    #endregion
}
