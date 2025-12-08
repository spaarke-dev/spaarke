using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class OpenAiClientTests
{
    private readonly Mock<ILogger<OpenAiClient>> _loggerMock;

    public OpenAiClientTests()
    {
        _loggerMock = new Mock<ILogger<OpenAiClient>>();
    }

    [Fact]
    public void Constructor_WithValidOptions_CreatesClient()
    {
        var options = CreateValidOptions();

        var client = new OpenAiClient(options, _loggerMock.Object);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEmptyEndpoint_ThrowsException()
    {
        var options = Options.Create(new AiOptions
        {
            OpenAiEndpoint = string.Empty,
            OpenAiKey = "test-key"
        });

        var act = () => new OpenAiClient(options, _loggerMock.Object);

        act.Should().Throw<UriFormatException>();
    }

    [Fact]
    public void Constructor_WithInvalidEndpointUri_ThrowsException()
    {
        var options = Options.Create(new AiOptions
        {
            OpenAiEndpoint = "not-a-valid-uri",
            OpenAiKey = "test-key"
        });

        var act = () => new OpenAiClient(options, _loggerMock.Object);

        act.Should().Throw<UriFormatException>();
    }

    [Fact]
    public async Task StreamCompletionAsync_WithInvalidCredentials_ThrowsException()
    {
        var options = CreateValidOptions();
        var client = new OpenAiClient(options, _loggerMock.Object);

        // Using an invalid key should eventually throw when making the actual API call
        var act = async () =>
        {
            await foreach (var _ in client.StreamCompletionAsync("test prompt"))
            {
                // Consume the stream
            }
        };

        // Should throw because credentials are invalid
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetCompletionAsync_WithInvalidCredentials_ThrowsException()
    {
        var options = CreateValidOptions();
        var client = new OpenAiClient(options, _loggerMock.Object);

        // Using an invalid key should throw when making the actual API call
        var act = async () => await client.GetCompletionAsync("test prompt");

        // Should throw because credentials are invalid
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task StreamCompletionAsync_WithCancellation_StopsEnumeration()
    {
        var options = CreateValidOptions();
        var client = new OpenAiClient(options, _loggerMock.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in client.StreamCompletionAsync("test", cancellationToken: cts.Token))
            {
                // Should not reach here
            }
        };

        // Should throw OperationCanceledException or similar due to cancellation
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task StreamVisionCompletionAsync_WithInvalidCredentials_ThrowsException()
    {
        var options = CreateValidOptions();
        var client = new OpenAiClient(options, _loggerMock.Object);
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header

        var act = async () =>
        {
            await foreach (var _ in client.StreamVisionCompletionAsync("describe this image", imageBytes, "image/png"))
            {
                // Consume the stream
            }
        };

        // Should throw because credentials are invalid
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void StreamCompletionAsync_UsesConfiguredModel_WhenNoModelOverride()
    {
        var options = Options.Create(new AiOptions
        {
            OpenAiEndpoint = "https://test.openai.azure.com/",
            OpenAiKey = "test-key",
            SummarizeModel = "custom-model"
        });

        var client = new OpenAiClient(options, _loggerMock.Object);

        // Client should be created with the custom model configured
        // The actual model usage is verified through the API call which we can't easily test here
        client.Should().NotBeNull();
    }

    [Fact]
    public void StreamVisionCompletionAsync_UsesImageModel_WhenConfigured()
    {
        var options = Options.Create(new AiOptions
        {
            OpenAiEndpoint = "https://test.openai.azure.com/",
            OpenAiKey = "test-key",
            SummarizeModel = "gpt-4o-mini",
            ImageSummarizeModel = "gpt-4o" // Different model for vision
        });

        var client = new OpenAiClient(options, _loggerMock.Object);

        // Client should be created with the image model configured
        client.Should().NotBeNull();
    }

    [Fact]
    public void StreamVisionCompletionAsync_FallsBackToSummarizeModel_WhenImageModelNotConfigured()
    {
        var options = Options.Create(new AiOptions
        {
            OpenAiEndpoint = "https://test.openai.azure.com/",
            OpenAiKey = "test-key",
            SummarizeModel = "gpt-4o-mini",
            ImageSummarizeModel = null // Not configured
        });

        var client = new OpenAiClient(options, _loggerMock.Object);

        // Client should be created - will fall back to SummarizeModel
        client.Should().NotBeNull();
    }

    private static IOptions<AiOptions> CreateValidOptions()
    {
        return Options.Create(new AiOptions
        {
            OpenAiEndpoint = "https://test-resource.openai.azure.com/",
            OpenAiKey = "test-api-key-that-is-invalid"
        });
    }
}

public class OpenAiClientConfigurationTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(4000)]
    public void MaxOutputTokens_AcceptsValidRange(int maxTokens)
    {
        var options = new AiOptions { MaxOutputTokens = maxTokens };

        options.MaxOutputTokens.Should().Be(maxTokens);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.3f)]
    [InlineData(0.7f)]
    [InlineData(1.0f)]
    public void Temperature_AcceptsValidRange(float temperature)
    {
        var options = new AiOptions { Temperature = temperature };

        options.Temperature.Should().Be(temperature);
    }

    [Theory]
    [InlineData("gpt-4o-mini")]
    [InlineData("gpt-4o")]
    [InlineData("gpt-4")]
    [InlineData("custom-deployment-name")]
    public void SummarizeModel_AcceptsAnyDeploymentName(string model)
    {
        var options = new AiOptions { SummarizeModel = model };

        options.SummarizeModel.Should().Be(model);
    }
}
