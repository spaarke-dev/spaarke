using System.Threading;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior tests for <see cref="CommunicationDraftEndpoints.DraftAsync"/> — the email composer's AI
/// "sparkle" draft handler. Verifies request validation, the mapping of the <see cref="IEmailDraftAi"/>
/// facade result to HTTP results, and the null→503 degradation (AI unavailable). The AI call itself is
/// mocked at the <see cref="IEmailDraftAi"/> facade boundary (allowed per ADR-038 — a module boundary,
/// not an HttpMessageHandler mock).
/// </summary>
public class CommunicationDraftEndpointTests
{
    private readonly Mock<IEmailDraftAi> _emailDraftAiMock = new();
    private readonly Mock<ILogger<CommunicationDraftResponse>> _loggerMock = new();

    private Task<IResult> InvokeAsync(CommunicationDraftRequest request) =>
        CommunicationDraftEndpoints.DraftAsync(
            request,
            _emailDraftAiMock.Object,
            _loggerMock.Object,
            CancellationToken.None);

    [Fact]
    public async Task Draft_WhenFacadeReturnsText_ReturnsOkWithTextAndEchoedIsHtml()
    {
        // Arrange
        _emailDraftAiMock
            .Setup(s => s.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>Dear Acme, thank you for your email.</p>");

        // Act
        var result = await InvokeAsync(new CommunicationDraftRequest
        {
            Intent = "reply",
            CurrentBody = "<p>original</p>",
            IsHtml = true,
        });

        // Assert
        var ok = result.Should().BeOfType<Ok<CommunicationDraftResponse>>().Subject;
        ok.Value!.Text.Should().Contain("thank you");
        ok.Value.IsHtml.Should().BeTrue("the response echoes the request body format");
    }

    [Fact]
    public async Task Draft_ForwardsIntentAndContextToFacade()
    {
        // Arrange
        EmailDraftAiRequest? captured = null;
        _emailDraftAiMock
            .Setup(s => s.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<EmailDraftAiRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync("drafted");

        // Act
        await InvokeAsync(new CommunicationDraftRequest
        {
            Intent = "custom",
            UserInstruction = "Make it sound urgent",
            CurrentBody = "hello",
            IsHtml = false,
            Subject = "Re: Filing deadline",
        });

        // Assert
        captured.Should().NotBeNull();
        captured!.Intent.Should().Be("custom");
        captured.UserInstruction.Should().Be("Make it sound urgent");
        captured.CurrentBody.Should().Be("hello");
        captured.IsHtml.Should().BeFalse();
        captured.Subject.Should().Be("Re: Filing deadline");
    }

    [Fact]
    public async Task Draft_WhenFacadeReturnsNull_ReturnsProblem503()
    {
        // Arrange — the Null-Object facade (AI off) or a completion failure returns null.
        _emailDraftAiMock
            .Setup(s => s.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await InvokeAsync(new CommunicationDraftRequest { Intent = "concise", CurrentBody = "x" });

        // Assert
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
        problem.ProblemDetails.Title.Should().Be("AI Drafting Unavailable");
    }

    [Fact]
    public async Task Draft_WhenIntentMissing_ThrowsValidationProblem()
    {
        // Act
        var act = () => InvokeAsync(new CommunicationDraftRequest { Intent = "", CurrentBody = "x" });

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Code.Should().Be("VALIDATION_ERROR");
        _emailDraftAiMock.Verify(s => s.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Draft_WhenCustomIntentMissingInstruction_ThrowsValidationProblem()
    {
        // Act
        var act = () => InvokeAsync(new CommunicationDraftRequest { Intent = "custom", CurrentBody = "x" });

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Detail.Should().Contain("userInstruction");
        _emailDraftAiMock.Verify(s => s.DraftAsync(It.IsAny<EmailDraftAiRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
