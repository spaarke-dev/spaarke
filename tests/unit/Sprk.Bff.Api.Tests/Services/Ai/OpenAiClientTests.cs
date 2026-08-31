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
    public void Constructor_WithEmptyEndpoint_ThrowsException()
    {
        var options = Options.Create(new DocumentIntelligenceOptions
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
        var options = Options.Create(new DocumentIntelligenceOptions
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


    private static IOptions<DocumentIntelligenceOptions> CreateValidOptions()
    {
        return Options.Create(new DocumentIntelligenceOptions
        {
            OpenAiEndpoint = "https://test-resource.openai.azure.com/",
            OpenAiKey = "test-api-key-that-is-invalid"
        });
    }
}

// `OpenAiClientConfigurationTests` was deleted here by task CICD-094 (issue #864) as the B16
// migration that let the B16 guard arm green. Its three [Theory] methods (12 cases) each assigned
// ONE auto-property on DocumentIntelligenceOptions and asserted that same property read back:
//
//     var options = new DocumentIntelligenceOptions { MaxOutputTokens = maxTokens };
//     options.MaxOutputTokens.Should().Be(maxTokens);
//
// That is ADR-038 §7 B16 verbatim — the C# language guarantees the round-trip, and `{ get; set; }`
// has no behavior to protect. The names promised more than the bodies delivered:
// `MaxOutputTokens_AcceptsValidRange` asserted no range (none is enforced on the options type) and
// `SummarizeModel_AcceptsAnyDeploymentName` asserted no deployment-name rule. Nothing regressed
// when they were removed because they constrained nothing.
//
// If validation is ever added to these options, test THAT — the throw, the clamp, the default —
// under `tests/unit/domain/**`. See Adr038TestBanGuardTests.B16_NoAutoPropertyRoundTripTests.
