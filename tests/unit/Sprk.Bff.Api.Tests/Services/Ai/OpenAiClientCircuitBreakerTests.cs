using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Tests for OpenAiClient circuit breaker functionality (Task 072).
/// </summary>
public class OpenAiClientCircuitBreakerTests
{
    private readonly Mock<ILogger<OpenAiClient>> _loggerMock;
    private readonly AiOptions _options;

    public OpenAiClientCircuitBreakerTests()
    {
        _loggerMock = new Mock<ILogger<OpenAiClient>>();
        _options = new AiOptions
        {
            Enabled = true,
            OpenAiEndpoint = "https://test.openai.azure.com",
            OpenAiKey = "test-key",
            SummarizeModel = "gpt-4o-mini",
            MaxOutputTokens = 1000,
            Temperature = 0.3f
        };
    }

    [Fact]
    public void OpenAiCircuitBrokenException_ContainsRetryAfter()
    {
        // Arrange
        var retryAfter = TimeSpan.FromSeconds(30);

        // Act
        var exception = new OpenAiCircuitBrokenException(retryAfter);

        // Assert
        Assert.Equal(retryAfter, exception.RetryAfter);
        Assert.Contains("30", exception.Message);
    }

    [Fact]
    public void OpenAiCircuitBrokenException_CustomMessage_ContainsRetryAfter()
    {
        // Arrange
        var retryAfter = TimeSpan.FromSeconds(60);
        var message = "Custom error message";

        // Act
        var exception = new OpenAiCircuitBrokenException(message, retryAfter);

        // Assert
        Assert.Equal(retryAfter, exception.RetryAfter);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void OpenAiClient_InitializesWithCircuitBreaker()
    {
        // Act - Creating the client should not throw
        var client = new OpenAiClient(Options.Create(_options), _loggerMock.Object);

        // Assert - Client was created successfully
        Assert.NotNull(client);
    }
}
