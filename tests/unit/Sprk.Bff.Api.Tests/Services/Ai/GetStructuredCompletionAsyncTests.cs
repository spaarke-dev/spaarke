using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenAI.Chat;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="OpenAiClient.GetStructuredCompletionAsync{T}"/>.
/// Tests verify configuration, deserialization, error handling, and circuit breaker behavior.
/// </summary>
public class GetStructuredCompletionAsyncTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly OpenAiClient _sut;

    private const string DeploymentName = "gpt-4o-mini";
    private const string SchemaName = "TestResult";

    private static readonly BinaryData s_testSchema = BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "value": { "type": "integer" },
                "isValid": { "type": "boolean" }
            },
            "required": ["name", "value", "isValid"],
            "additionalProperties": false
        }
        """);

    private record TestResult
    {
        public string Name { get; init; } = null!;
        public int Value { get; init; }
        public bool IsValid { get; init; }
    }

    public GetStructuredCompletionAsyncTests()
    {
        _mockServer = WireMockServer.Start();

        var options = Options.Create(new DocumentIntelligenceOptions
        {
            OpenAiEndpoint = _mockServer.Urls[0],
            OpenAiKey = "test-api-key",
            SummarizeModel = DeploymentName,
            MaxOutputTokens = 1000,
            Temperature = 0.3f
        });

        _sut = new OpenAiClient(options, new Mock<ILogger<OpenAiClient>>().Object);
    }

    public void Dispose()
    {
        _mockServer.Stop();
        _mockServer.Dispose();
    }

    #region Schema Configuration Tests

    [Fact]
    public void ChatResponseFormat_CreateJsonSchemaFormat_Configures_Strict_Schema()
    {
        // Arrange & Act — verify CreateJsonSchemaFormat produces a non-null format
        var format = ChatResponseFormat.CreateJsonSchemaFormat(
            SchemaName,
            s_testSchema,
            jsonSchemaIsStrict: true);

        // Assert
        format.Should().NotBeNull("structured output requires a JSON schema response format");
    }

    [Fact]
    public void JsonSchema_Has_AdditionalProperties_False_For_Strict_Mode()
    {
        // Verify the test schema follows strict schema requirements
        using var doc = JsonDocument.Parse(s_testSchema.ToString());
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            "strict schema mode requires additionalProperties: false");
        root.GetProperty("required").GetArrayLength().Should().Be(3);
    }

    #endregion

    #region Deserialization Tests

    [Fact]
    public void Deserialization_With_Web_Defaults_Handles_CamelCase()
    {
        // Verify camelCase JSON deserializes to PascalCase properties (JsonSerializerDefaults.Web)
        var json = """{"name":"TestDocument","value":42,"isValid":true}""";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var result = JsonSerializer.Deserialize<TestResult>(json, options);

        result.Should().NotBeNull();
        result!.Name.Should().Be("TestDocument");
        result.Value.Should().Be(42);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Deserialization_Of_Null_Json_Returns_Null()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var result = JsonSerializer.Deserialize<TestResult>("null", options);

        result.Should().BeNull("JSON 'null' should deserialize to null for reference types");
    }

    [Fact]
    public void Deserialization_Of_Invalid_Json_Throws_JsonException()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var act = () => JsonSerializer.Deserialize<TestResult>("this is not valid json {{{", options);

        act.Should().Throw<JsonException>("malformed JSON should cause a deserialization error");
    }

    [Fact]
    public void Deserialization_Of_Empty_String_Throws_JsonException()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var act = () => JsonSerializer.Deserialize<TestResult>("", options);

        act.Should().Throw<JsonException>("empty string is not valid JSON");
    }

    #endregion

    #region API Error Tests

    [Fact]
    public async Task GetStructuredCompletionAsync_Propagates_Exception_On_Server_Error()
    {
        // Arrange
        SetupServerError();

        var messages = new List<ChatMessage>
        {
            new UserChatMessage("test")
        };

        // Act
        var act = async () => await _sut.GetStructuredCompletionAsync<TestResult>(
            messages, s_testSchema, SchemaName, DeploymentName);

        // Assert — Should throw due to 500 error
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region Circuit Breaker Tests

    [Fact]
    public async Task GetStructuredCompletionAsync_Throws_OpenAiCircuitBrokenException_When_Circuit_Open()
    {
        // Arrange — 500 errors will trip the circuit breaker
        SetupServerError();

        var messages = new List<ChatMessage>
        {
            new UserChatMessage("test")
        };

        // Act — Make enough calls to trip the circuit breaker (MinimumThroughput=5, FailureRatio=0.5)
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await _sut.GetStructuredCompletionAsync<TestResult>(
                    messages, s_testSchema, SchemaName, DeploymentName);
            }
            catch (OpenAiCircuitBrokenException)
            {
                // Circuit is now open — this is the expected behavior
                return;
            }
            catch
            {
                // Expected server errors — these failures will trip the circuit
            }
        }

        // If we get here, make one more call that should hit the open circuit
        var act = async () => await _sut.GetStructuredCompletionAsync<TestResult>(
            messages, s_testSchema, SchemaName, DeploymentName);

        await act.Should().ThrowAsync<OpenAiCircuitBrokenException>();
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task GetStructuredCompletionAsync_Respects_CancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var messages = new List<ChatMessage>
        {
            new UserChatMessage("test")
        };

        // Act
        var act = async () => await _sut.GetStructuredCompletionAsync<TestResult>(
            messages, s_testSchema, SchemaName, DeploymentName, cts.Token);

        // Assert — Should throw OperationCanceledException or TaskCanceledException
        await act.Should().ThrowAsync<OperationCanceledException>(
            "a pre-cancelled token should prevent the API call");
    }

    #endregion

    #region Helper Methods

    private void SetupServerError()
    {
        _mockServer.Reset();
        _mockServer
            .Given(Request.Create()
                .WithPath(new WildcardMatcher("/openai/deployments/*/chat/completions", true))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                        "error": {
                            "message": "Internal server error",
                            "type": "server_error",
                            "code": "500"
                        }
                    }
                    """));
    }

    #endregion
}
