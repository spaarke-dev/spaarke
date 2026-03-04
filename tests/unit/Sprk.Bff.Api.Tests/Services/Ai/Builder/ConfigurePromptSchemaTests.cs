using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Builder;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Builder;

/// <summary>
/// Unit tests for the configure_prompt_schema tool execution in BuilderToolExecutor.
/// Tests JPS generation, metadata, structured output, error handling, and field type mapping.
/// </summary>
public class ConfigurePromptSchemaTests
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly BuilderToolExecutor _executor;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<ILogger<BuilderToolExecutor>> _loggerMock;

    public ConfigurePromptSchemaTests()
    {
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _loggerMock = new Mock<ILogger<BuilderToolExecutor>>();
        _executor = new BuilderToolExecutor(
            _scopeResolverMock.Object,
            _loggerMock.Object);
    }

    #region Helper Methods

    private static BuilderToolCall CreateToolCall(object arguments)
    {
        var json = JsonSerializer.Serialize(arguments, CamelCaseOptions);
        return new BuilderToolCall
        {
            Id = "call_test_" + Guid.NewGuid().ToString("N")[..8],
            ToolName = "configure_prompt_schema",
            Arguments = JsonDocument.Parse(json),
            RawArguments = json
        };
    }

    private static CanvasState CreateCanvasWithNode(string nodeId, string nodeType = "aiAnalysis", string? label = null)
    {
        return new CanvasState
        {
            Nodes = new[]
            {
                new CanvasNode
                {
                    Id = nodeId,
                    Type = nodeType,
                    Label = label ?? "Test Node"
                }
            },
            Edges = Array.Empty<CanvasEdge>()
        };
    }

    /// <summary>
    /// Extracts the PromptSchema from the tool result's canvas operations.
    /// The schema is stored in the canvas patch config under "sprk_systemprompt".
    /// </summary>
    private static PromptSchema? ExtractPromptSchemaFromResult(BuilderToolResult result)
    {
        result.CanvasOperations.Should().NotBeNull().And.HaveCountGreaterThan(0);
        var canvasOp = result.CanvasOperations![0];
        var payload = canvasOp.Payload.RootElement;

        // The schema JSON is stored as a string in the config's sprk_systemprompt field
        var configProp = payload.GetProperty("config");
        var schemaJsonString = configProp.GetProperty("sprk_systemprompt").GetString();
        schemaJsonString.Should().NotBeNullOrEmpty();

        return JsonSerializer.Deserialize<PromptSchema>(schemaJsonString!, CamelCaseOptions);
    }

    /// <summary>
    /// Extracts the ConfigurePromptSchemaResult from the tool result's Result JSON.
    /// </summary>
    private static ConfigurePromptSchemaResult? ExtractResultData(BuilderToolResult result)
    {
        result.Result.Should().NotBeNull();
        return result.Result!.Deserialize<ConfigurePromptSchemaResult>(CamelCaseOptions);
    }

    #endregion

    #region GeneratesValidJpsFromArguments

    [Fact]
    public async Task GeneratesValidJpsFromArguments()
    {
        // Arrange
        var nodeId = "node-analysis-1";
        var canvasState = CreateCanvasWithNode(nodeId, label: "Document Classifier");
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Classify the document type based on its content",
            role = "You are a document classification specialist",
            outputFields = new[]
            {
                new { name = "documentType", type = "string", description = "The classified document type" },
                new { name = "confidence", type = "number", description = "Classification confidence score" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert - Tool result is successful
        result.Success.Should().BeTrue();
        result.ToolName.Should().Be("configure_prompt_schema");

        // Assert - Result data has correct field count
        var resultData = ExtractResultData(result);
        resultData.Should().NotBeNull();
        resultData!.NodeId.Should().Be(nodeId);
        resultData.FieldCount.Should().Be(2);
        resultData.Success.Should().BeTrue();

        // Assert - Generated JPS schema is valid
        var schema = ExtractPromptSchemaFromResult(result);
        schema.Should().NotBeNull();
        schema!.Schema.Should().Be("https://spaarke.com/schemas/prompt/v1");
        schema.Version.Should().Be(1);

        // Assert - Instruction section
        schema.Instruction.Task.Should().Be("Classify the document type based on its content");
        schema.Instruction.Role.Should().Be("You are a document classification specialist");

        // Assert - Output fields
        schema.Output.Should().NotBeNull();
        schema.Output!.Fields.Should().HaveCount(2);
        schema.Output.Fields[0].Name.Should().Be("documentType");
        schema.Output.Fields[0].Type.Should().Be("string");
        schema.Output.Fields[0].Description.Should().Be("The classified document type");
        schema.Output.Fields[1].Name.Should().Be("confidence");
        schema.Output.Fields[1].Type.Should().Be("number");
        schema.Output.Fields[1].Description.Should().Be("Classification confidence score");
    }

    #endregion

    #region SetsMetadataCorrectly

    [Fact]
    public async Task SetsMetadataCorrectly()
    {
        // Arrange
        var nodeId = "node-meta-1";
        var canvasState = CreateCanvasWithNode(nodeId);
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Summarize the document",
            outputFields = new[]
            {
                new { name = "summary", type = "string" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var schema = ExtractPromptSchemaFromResult(result);
        schema.Should().NotBeNull();
        schema!.Metadata.Should().NotBeNull();
        schema.Metadata!.AuthorLevel.Should().Be(3);
        schema.Metadata.Author.Should().Be("builder-agent");
        schema.Metadata.CreatedAt.Should().NotBeNullOrEmpty("created timestamp should be set");
    }

    #endregion

    #region SetsStructuredOutputFlag

    [Fact]
    public async Task SetsStructuredOutputFlag()
    {
        // Arrange
        var nodeId = "node-structured-1";
        var canvasState = CreateCanvasWithNode(nodeId);
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Extract key dates from the contract",
            outputFields = new[]
            {
                new { name = "dates", type = "array", description = "Extracted dates" }
            },
            useStructuredOutput = true,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var schema = ExtractPromptSchemaFromResult(result);
        schema.Should().NotBeNull();
        schema!.Output.Should().NotBeNull();
        schema.Output!.StructuredOutput.Should().BeTrue();

        // Also verify the result data reports structured output
        var resultData = ExtractResultData(result);
        resultData.Should().NotBeNull();
        resultData!.StructuredOutput.Should().BeTrue();

        // Verify the canvas patch config also has the structured output flag
        var canvasOp = result.CanvasOperations![0];
        var payload = canvasOp.Payload.RootElement;
        var config = payload.GetProperty("config");
        config.GetProperty("structuredOutput").GetBoolean().Should().BeTrue();
        config.GetProperty("promptSchemaVersion").GetInt32().Should().Be(1);
    }

    #endregion

    #region HandlesMissingNodeId_ReturnsError

    [Fact]
    public async Task HandlesMissingNodeId_ReturnsError()
    {
        // Arrange - Use a nodeId that does not exist in the canvas
        var canvasState = CreateCanvasWithNode("existing-node-id");
        var toolCall = CreateToolCall(new
        {
            nodeId = "nonexistent-node-id",
            task = "Some task",
            outputFields = new[]
            {
                new { name = "result", type = "string" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Node not found");
        result.Error.Should().Contain("nonexistent-node-id");
    }

    #endregion

    #region HandlesMissingTask_ReturnsError

    [Fact]
    public async Task HandlesMissingTask_ReturnsError()
    {
        // Arrange - Task is required on ConfigurePromptSchemaArguments.
        // Sending a JSON object without "task" should cause a deserialization failure
        // or a JsonException, which the executor handles as an error.
        var nodeId = "node-notask-1";
        var canvasState = CreateCanvasWithNode(nodeId);

        // Create raw JSON without the "task" property to trigger parse error
        var rawJson = JsonSerializer.Serialize(new
        {
            nodeId,
            outputFields = new[]
            {
                new { name = "result", type = "string" }
            }
        }, CamelCaseOptions);

        var toolCall = new BuilderToolCall
        {
            Id = "call_test_notask",
            ToolName = "configure_prompt_schema",
            Arguments = JsonDocument.Parse(rawJson),
            RawArguments = rawJson
        };

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert - Should fail: "task" is a required field on ConfigurePromptSchemaArguments.
        // The method deserializes arguments and the JsonSerializer will return null or throw
        // for missing required properties, triggering CreateErrorResult.
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region MapsOutputFieldTypesCorrectly

    [Fact]
    public async Task MapsOutputFieldTypesCorrectly()
    {
        // Arrange - Create outputFields covering all 5 types
        var nodeId = "node-types-1";
        var canvasState = CreateCanvasWithNode(nodeId);
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Analyze the document and extract structured data",
            role = "You are a data extraction specialist",
            outputFields = new object[]
            {
                new { name = "title", type = "string", description = "Document title" },
                new { name = "pageCount", type = "number", description = "Number of pages" },
                new { name = "isConfidential", type = "boolean", description = "Whether document is confidential" },
                new { name = "keyPhrases", type = "array", description = "Key phrases found in the document" },
                new { name = "metadata", type = "object", description = "Additional document metadata" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var schema = ExtractPromptSchemaFromResult(result);
        schema.Should().NotBeNull();
        schema!.Output.Should().NotBeNull();
        schema.Output!.Fields.Should().HaveCount(5);

        // Verify each field type is mapped correctly
        var fields = schema.Output.Fields;

        fields[0].Name.Should().Be("title");
        fields[0].Type.Should().Be("string");
        fields[0].Description.Should().Be("Document title");

        fields[1].Name.Should().Be("pageCount");
        fields[1].Type.Should().Be("number");
        fields[1].Description.Should().Be("Number of pages");

        fields[2].Name.Should().Be("isConfidential");
        fields[2].Type.Should().Be("boolean");
        fields[2].Description.Should().Be("Whether document is confidential");

        fields[3].Name.Should().Be("keyPhrases");
        fields[3].Type.Should().Be("array");
        fields[3].Description.Should().Be("Key phrases found in the document");

        fields[4].Name.Should().Be("metadata");
        fields[4].Type.Should().Be("object");
        fields[4].Description.Should().Be("Additional document metadata");

        // Verify result data
        var resultData = ExtractResultData(result);
        resultData!.FieldCount.Should().Be(5);
    }

    #endregion

    #region Additional Coverage

    [Fact]
    public async Task CanvasOperation_HasUpdateNodeType()
    {
        // Arrange
        var nodeId = "node-optype-1";
        var canvasState = CreateCanvasWithNode(nodeId);
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Simple analysis task",
            outputFields = new[]
            {
                new { name = "result", type = "string" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert - Canvas operation should be an UpdateNode
        result.CanvasOperations.Should().NotBeNull().And.HaveCount(1);
        result.CanvasOperations![0].Type.Should().Be(CanvasOperationType.UpdateNode);
    }

    [Fact]
    public async Task ResultMessage_IncludesNodeLabelAndFieldCount()
    {
        // Arrange
        var nodeId = "node-msg-1";
        var canvasState = CreateCanvasWithNode(nodeId, label: "Risk Analysis");
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Assess risk factors in the document",
            outputFields = new[]
            {
                new { name = "riskLevel", type = "string" },
                new { name = "factors", type = "array" },
                new { name = "score", type = "number" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        var resultData = ExtractResultData(result);
        resultData.Should().NotBeNull();
        resultData!.Message.Should().Contain("Risk Analysis");
        resultData.Message.Should().Contain("3 output fields");
    }

    [Fact]
    public async Task StructuredOutputFlag_IncludedInResultMessage()
    {
        // Arrange
        var nodeId = "node-msg-so-1";
        var canvasState = CreateCanvasWithNode(nodeId, label: "Classifier");
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Classify the document",
            outputFields = new[]
            {
                new { name = "type", type = "string" }
            },
            useStructuredOutput = true,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        var resultData = ExtractResultData(result);
        resultData!.Message.Should().Contain("structured output enabled");
    }

    [Fact]
    public async Task Constraints_MappedToInstructionSection()
    {
        // Arrange
        var nodeId = "node-constraints-1";
        var canvasState = CreateCanvasWithNode(nodeId);
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Analyze the contract",
            constraints = new[] { "Be concise", "Use formal language", "Cite specific clauses" },
            outputFields = new[]
            {
                new { name = "analysis", type = "string" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var schema = ExtractPromptSchemaFromResult(result);
        schema.Should().NotBeNull();
        schema!.Instruction.Constraints.Should().NotBeNull();
        schema.Instruction.Constraints.Should().HaveCount(3);
        schema.Instruction.Constraints.Should().Contain("Be concise");
        schema.Instruction.Constraints.Should().Contain("Use formal language");
        schema.Instruction.Constraints.Should().Contain("Cite specific clauses");
    }

    [Fact]
    public async Task OutputFieldsWithEnum_MappedCorrectly()
    {
        // Arrange
        var nodeId = "node-enum-1";
        var canvasState = CreateCanvasWithNode(nodeId);
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Classify the document priority",
            outputFields = new[]
            {
                new
                {
                    name = "priority",
                    type = "string",
                    description = "Document priority level",
                    @enum = new[] { "low", "medium", "high", "critical" }
                }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var schema = ExtractPromptSchemaFromResult(result);
        schema.Should().NotBeNull();
        schema!.Output!.Fields.Should().HaveCount(1);

        var priorityField = schema.Output.Fields[0];
        priorityField.Name.Should().Be("priority");
        priorityField.Enum.Should().NotBeNull();
        priorityField.Enum.Should().HaveCount(4);
        priorityField.Enum.Should().ContainInOrder("low", "medium", "high", "critical");
    }

    [Fact]
    public async Task SchemaVersion_AlwaysSetToOne()
    {
        // Arrange
        var nodeId = "node-version-1";
        var canvasState = CreateCanvasWithNode(nodeId);
        var toolCall = CreateToolCall(new
        {
            nodeId,
            task = "Quick analysis",
            outputFields = new[]
            {
                new { name = "result", type = "string" }
            },
            useStructuredOutput = false,
            autoWireChoices = false
        });

        // Act
        var result = await _executor.ExecuteAsync(toolCall, canvasState, CancellationToken.None);

        // Assert
        var resultData = ExtractResultData(result);
        resultData!.SchemaVersion.Should().Be(1);

        var schema = ExtractPromptSchemaFromResult(result);
        schema!.Version.Should().Be(1);
    }

    #endregion
}
