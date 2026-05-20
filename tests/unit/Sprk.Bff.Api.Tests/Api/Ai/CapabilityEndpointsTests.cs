using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="CapabilityEndpoints"/>.
///
/// Tests the webhook-secret authentication logic via direct reflection of the
/// private HandleWebhookRefreshAsync handler, isolating it from the full ASP.NET
/// pipeline. This follows the pattern used by other endpoint tests in this project.
/// </summary>
public sealed class CapabilityEndpointsTests
{
    private const string WebhookSecretHeader = "X-Webhook-Secret";
    private const string ValidSecret = "my-test-secret";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the private HandleWebhookRefreshAsync handler via reflection,
    /// mirroring how ASP.NET minimal API routes invoke static handlers.
    /// </summary>
    private static IResult InvokeHandler(
        HttpContext context,
        IManifestRefreshTrigger trigger,
        IOptions<ManifestRefreshOptions> options)
    {
        // Use reflection to call the private static handler.
        var method = typeof(CapabilityEndpoints)
            .GetMethod("HandleWebhookRefreshAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("Handler method not found via reflection");

        var result = method.Invoke(null, new object[]
        {
            context,
            trigger,
            options,
            NullLoggerFactory.Instance
        });

        return result as IResult
            ?? throw new InvalidOperationException("Handler did not return IResult");
    }

    private static HttpContext MakeContext(string? secretHeaderValue = null)
    {
        var context = new DefaultHttpContext();
        if (secretHeaderValue is not null)
            context.Request.Headers[WebhookSecretHeader] = secretHeaderValue;
        return context;
    }

    private static IOptions<ManifestRefreshOptions> MakeOptions(string? secret)
        => Options.Create(new ManifestRefreshOptions
        {
            RefreshIntervalMinutes = 15,
            WebhookSecret = secret
        });

    // ── Tests: 401 Unauthorized ───────────────────────────────────────────────

    [Fact]
    public void WhenSecretNotConfigured_Returns401()
    {
        // Arrange
        var context = MakeContext(secretHeaderValue: "any-value");
        var trigger = new Mock<IManifestRefreshTrigger>();
        var options = MakeOptions(secret: null); // no secret configured

        // Act
        var result = InvokeHandler(context, trigger.Object, options);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);

        trigger.Verify(t => t.TriggerRefresh(), Times.Never,
            "TriggerRefresh must not be called when the server secret is not configured");
    }

    [Fact]
    public void WhenSecretHeaderIsMissing_Returns401()
    {
        // Arrange
        var context = MakeContext(secretHeaderValue: null); // no header
        var trigger = new Mock<IManifestRefreshTrigger>();
        var options = MakeOptions(ValidSecret);

        // Act
        var result = InvokeHandler(context, trigger.Object, options);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);

        trigger.Verify(t => t.TriggerRefresh(), Times.Never);
    }

    [Fact]
    public void WhenSecretHeaderIsWrong_Returns401()
    {
        // Arrange
        var context = MakeContext(secretHeaderValue: "wrong-secret");
        var trigger = new Mock<IManifestRefreshTrigger>();
        var options = MakeOptions(ValidSecret);

        // Act
        var result = InvokeHandler(context, trigger.Object, options);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);

        trigger.Verify(t => t.TriggerRefresh(), Times.Never);
    }

    [Fact]
    public void WhenSecretIsCaseSensitivelyDifferent_Returns401()
    {
        // Arrange: correct value but wrong case — secrets are compared ordinally
        var context = MakeContext(secretHeaderValue: ValidSecret.ToUpperInvariant());
        var trigger = new Mock<IManifestRefreshTrigger>();
        var options = MakeOptions(ValidSecret);

        // Act
        var result = InvokeHandler(context, trigger.Object, options);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized,
            "shared-secret comparison is case-sensitive (Ordinal)");

        trigger.Verify(t => t.TriggerRefresh(), Times.Never);
    }

    // ── Tests: 204 No Content ─────────────────────────────────────────────────

    [Fact]
    public void WhenSecretMatches_Returns204AndTriggersRefresh()
    {
        // Arrange
        var context = MakeContext(secretHeaderValue: ValidSecret);
        var trigger = new Mock<IManifestRefreshTrigger>();
        var options = MakeOptions(ValidSecret);

        // Act
        var result = InvokeHandler(context, trigger.Object, options);

        // Assert: 204 No Content
        result.Should().BeOfType<NoContent>();

        // TriggerRefresh must be called exactly once
        trigger.Verify(t => t.TriggerRefresh(), Times.Once,
            "a valid webhook call must trigger an immediate manifest refresh");
    }
}
