using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

// Disambiguate — ResolvedKnowledgeRef exists in both Ai and Ai.Schemas namespaces.
// The Render() method takes the Ai namespace version.
using ResolvedKnowledgeRef = Sprk.Bff.Api.Services.Ai.ResolvedKnowledgeRef;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class PromptSchemaRendererTests
{
    private readonly PromptSchemaRenderer _sut;

    public PromptSchemaRendererTests()
    {
        _sut = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
    }

    // -- Format detection tests --

    [Fact]
    public void NullInput_ReturnsEmptyFlatText()
    {
        // Act
        var result = _sut.Render(null, null, null, null, null, null);

        // Assert
        result.PromptText.Should().BeEmpty();
        result.Format.Should().Be(PromptFormat.FlatText);
    }

    [Fact]
    public void EmptyInput_ReturnsEmptyFlatText()
    {
        // Act
        var result = _sut.Render(string.Empty, null, null, null, null, null);

        // Assert
        result.PromptText.Should().BeEmpty();
        result.Format.Should().Be(PromptFormat.FlatText);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void WhitespaceInput_ReturnsEmptyFlatText(string input)
    {
        // Act
        var result = _sut.Render(input, null, null, null, null, null);

        // Assert
        result.PromptText.Should().BeEmpty();
        result.Format.Should().Be(PromptFormat.FlatText);
    }

    [Fact]
    public void FlatText_ReturnsAsIs()
    {
        // Arrange
        var flatPrompt = "You are a helpful assistant. Summarize the document.";

        // Act
        var result = _sut.Render(flatPrompt, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Be(flatPrompt);
        result.Format.Should().Be(PromptFormat.FlatText);
    }

    [Fact]
    public void JpsJson_DetectedCorrectly()
    {
        // Arrange
        var jpsJson = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "task": "Classify the document"
            }
        }
        """;

        // Act
        var result = _sut.Render(jpsJson, null, null, null, null, null);

        // Assert
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("Classify the document");
    }

    [Fact]
    public void MalformedJson_FallsBackToFlatText()
    {
        // Arrange — starts with { and contains "$schema" but is not valid JSON
        var malformed = """{ "$schema": broken json here """;

        // Act
        var result = _sut.Render(malformed, null, null, null, null, null);

        // Assert
        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(malformed);
    }

    [Fact]
    public void JsonWithoutSchema_TreatedAsFlatText()
    {
        // Arrange — valid JSON but no "$schema" key
        var jsonNoSchema = """{ "instruction": { "task": "Summarize" } }""";

        // Act
        var result = _sut.Render(jsonNoSchema, null, null, null, null, null);

        // Assert
        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(jsonNoSchema);
    }

    // -- JPS rendering tests --

    [Fact]
    public void RendersRole()
    {
        // Arrange
        var jps = BuildJpsJson(role: "You are a contract analysis specialist", task: "Analyze");

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        var lines = result.PromptText.Split('\n', StringSplitOptions.None);
        lines[0].TrimEnd().Should().Be("You are a contract analysis specialist");
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
    }

    [Fact]
    public void RendersTask()
    {
        // Arrange
        var jps = BuildJpsJson(task: "Extract key dates from the document");

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("Extract key dates from the document");
    }

    [Fact]
    public void RendersConstraints()
    {
        // Arrange
        var jps = BuildJpsJson(
            task: "Classify",
            constraints: new[] { "Be precise", "Use ISO 8601 format", "No speculation" });

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("## Constraints");
        result.PromptText.Should().Contain("1. Be precise");
        result.PromptText.Should().Contain("2. Use ISO 8601 format");
        result.PromptText.Should().Contain("3. No speculation");
    }

    [Fact]
    public void RendersExamples()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Examples = new[]
            {
                new ExampleEntry
                {
                    Input = "This agreement is entered into...",
                    Output = JsonSerializer.SerializeToElement(new { documentType = "contract" })
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("## Examples");
        result.PromptText.Should().Contain("Input: \"This agreement is entered into...\"");
        result.PromptText.Should().Contain("Expected output:");
        result.PromptText.Should().Contain("\"documentType\"");
        result.PromptText.Should().Contain("\"contract\"");
    }

    [Fact]
    public void RendersOutputFieldsText()
    {
        // Arrange — structuredOutput = false, so fields rendered as text instructions
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "summary",
                        Type = "string",
                        Description = "A brief summary",
                        MaxLength = 500
                    },
                    new OutputFieldDefinition
                    {
                        Name = "riskLevel",
                        Type = "string",
                        Description = "Risk assessment",
                        Enum = new[] { "low", "medium", "high" }
                    },
                    new OutputFieldDefinition
                    {
                        Name = "score",
                        Type = "number",
                        Description = "Confidence score",
                        Minimum = 0,
                        Maximum = 1
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("## Output Format");
        result.PromptText.Should().Contain("Return valid JSON with the following fields:");
        result.PromptText.Should().Contain("- summary (string): A brief summary (max 500 chars)");
        result.PromptText.Should().Contain("- riskLevel (string): Risk assessment");
        result.PromptText.Should().Contain("one of: low, medium, high");
        result.PromptText.Should().Contain("- score (number): Confidence score (0-1)");
    }

    [Fact]
    public void RendersStructuredOutputInstruction()
    {
        // Arrange — structuredOutput = true, so constrained decoding instruction
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "type", Type = "string" }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("Return valid JSON matching the provided schema.");
        result.PromptText.Should().NotContain("## Output Format");
    }

    [Fact]
    public void MinimalSchema_RendersTaskOnly()
    {
        // Arrange — only the required task field
        var jps = BuildJpsJson(task: "Summarize the document");

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("Summarize the document");
        result.PromptText.Should().NotContain("## Constraints");
        result.PromptText.Should().NotContain("## Examples");
        result.PromptText.Should().NotContain("## Output Format");
        result.PromptText.Should().NotContain("## Document");
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
    }

    [Fact]
    public void RendersDocumentText()
    {
        // Arrange
        var jps = BuildJpsJson(task: "Analyze this contract");
        var documentText = "This is the full text of the contract...";

        // Act
        var result = _sut.Render(jps, null, null, documentText, null, null);

        // Assert
        result.PromptText.Should().Contain("## Document");
        result.PromptText.Should().Contain("This is the full text of the contract...");
    }

    [Fact]
    public void RendersSkillContext()
    {
        // Arrange
        var jps = BuildJpsJson(task: "Analyze");
        var skillContext = "Apply financial analysis methodology.";

        // Act
        var result = _sut.Render(jps, skillContext, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("## Additional Analysis Instructions");
        result.PromptText.Should().Contain("Apply financial analysis methodology.");
    }

    [Fact]
    public void RendersKnowledgeContext()
    {
        // Arrange
        var jps = BuildJpsJson(task: "Analyze");
        var knowledgeContext = "Key definitions: Force Majeure means...";

        // Act
        var result = _sut.Render(jps, null, knowledgeContext, null, null, null);

        // Assert
        result.PromptText.Should().Contain("## Reference Knowledge");
        result.PromptText.Should().Contain("Key definitions: Force Majeure means...");
    }

    [Fact]
    public void RendersContextField()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Task = "Analyze",
                Context = "The user is reviewing NDA contracts from 2025"
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("The user is reviewing NDA contracts from 2025");
    }

    [Fact]
    public void FullSchema_RendersAllSectionsInOrder()
    {
        // Arrange — schema with all sections populated
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = "You are a legal analyst",
                Task = "Extract obligations",
                Constraints = new[] { "Be thorough" },
                Context = "Focus on payment obligations"
            },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "obligations", Type = "array" }
                },
                StructuredOutput = false
            },
            Examples = new[]
            {
                new ExampleEntry
                {
                    Input = "Party A shall pay...",
                    Output = JsonSerializer.SerializeToElement(new { obligations = new[] { "payment" } })
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, "skill instructions", "knowledge base", "document text", null, null);

        // Assert — verify sections appear in order
        var text = result.PromptText;

        var roleIdx = text.IndexOf("You are a legal analyst", StringComparison.Ordinal);
        var taskIdx = text.IndexOf("Extract obligations", StringComparison.Ordinal);
        var constraintsIdx = text.IndexOf("## Constraints", StringComparison.Ordinal);
        var contextIdx = text.IndexOf("Focus on payment obligations", StringComparison.Ordinal);
        var documentIdx = text.IndexOf("## Document", StringComparison.Ordinal);
        var skillIdx = text.IndexOf("## Additional Analysis Instructions", StringComparison.Ordinal);
        var knowledgeIdx = text.IndexOf("## Reference Knowledge", StringComparison.Ordinal);
        var examplesIdx = text.IndexOf("## Examples", StringComparison.Ordinal);
        var outputIdx = text.IndexOf("## Output Format", StringComparison.Ordinal);

        roleIdx.Should().BeGreaterOrEqualTo(0);
        taskIdx.Should().BeGreaterThan(roleIdx);
        constraintsIdx.Should().BeGreaterThan(taskIdx);
        contextIdx.Should().BeGreaterThan(constraintsIdx);
        documentIdx.Should().BeGreaterThan(contextIdx);
        skillIdx.Should().BeGreaterThan(documentIdx);
        knowledgeIdx.Should().BeGreaterThan(skillIdx);
        examplesIdx.Should().BeGreaterThan(knowledgeIdx);
        outputIdx.Should().BeGreaterThan(examplesIdx);
    }

    // -- Section-level detail tests --

    [Fact]
    public void ConstraintsRendered_AsNumberedList()
    {
        // Arrange
        var jps = BuildJpsJson(
            task: "Analyze",
            constraints: new[] { "Be precise", "Use ISO 8601 format", "No speculation" });

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — each constraint is on its own line with sequential numbering
        var lines = result.PromptText.Split(Environment.NewLine);
        lines.Should().Contain("1. Be precise");
        lines.Should().Contain("2. Use ISO 8601 format");
        lines.Should().Contain("3. No speculation");
    }

    [Fact]
    public void ExamplesRendered_WithInputAndJsonOutput()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Examples = new[]
            {
                new ExampleEntry
                {
                    Input = "Contract between A and B",
                    Output = JsonSerializer.SerializeToElement(new { documentType = "contract", risk = "low" })
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — verify Input: "..." and Expected output: followed by indented JSON
        var text = result.PromptText;
        text.Should().Contain("Input: \"Contract between A and B\"");
        text.Should().Contain("Expected output:");
        text.Should().Contain("\"documentType\": \"contract\"");
        text.Should().Contain("\"risk\": \"low\"");
    }

    [Fact]
    public void OutputFieldsRendered_WithEnumValues()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "severity",
                        Type = "string",
                        Description = "Issue severity",
                        Enum = new[] { "low", "medium", "high" }
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — enum values appear after em-dash with "one of:" prefix
        result.PromptText.Should().Contain("- severity (string): Issue severity");
        result.PromptText.Should().Contain("one of: low, medium, high");
    }

    [Fact]
    public void OutputFieldsRendered_WithNumericRange()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Score" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "confidence",
                        Type = "number",
                        Description = "Confidence score",
                        Minimum = 0,
                        Maximum = 1
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — numeric range appears in parentheses
        result.PromptText.Should().Contain("- confidence (number): Confidence score (0-1)");
    }

    [Fact]
    public void OutputFieldsRendered_WithMaxLength()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Summarize" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "summary",
                        Type = "string",
                        Description = "Brief summary",
                        MaxLength = 250
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — max length shows in parentheses
        result.PromptText.Should().Contain("- summary (string): Brief summary (max 250 chars)");
    }

    [Fact]
    public void OutputFieldsRendered_WithArrayType()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Extract" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "parties",
                        Type = "array",
                        Description = "Named parties"
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — array type is rendered with parenthesized type and description
        result.PromptText.Should().Contain("- parties (array): Named parties");
    }

    [Fact]
    public void StructuredOutput_GeneratesJsonSchema()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "type", Type = "string" },
                    new OutputFieldDefinition { Name = "score", Type = "number" }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — JsonSchema should be populated
        result.JsonSchema.Should().NotBeNull();
        result.SchemaName.Should().Be("prompt_response");
    }

    [Fact]
    public void StructuredOutput_JsonSchemaHasCorrectProperties()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "documentType",
                        Type = "string",
                        Description = "Type of document",
                        Enum = new[] { "contract", "invoice", "memo" }
                    },
                    new OutputFieldDefinition
                    {
                        Name = "confidence",
                        Type = "number",
                        Minimum = 0,
                        Maximum = 1
                    },
                    new OutputFieldDefinition
                    {
                        Name = "isValid",
                        Type = "boolean"
                    }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — JSON Schema structure has correct top-level shape
        var jsonSchema = result.JsonSchema!;
        jsonSchema["type"]!.GetValue<string>().Should().Be("object");
        jsonSchema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();

        // Verify properties exist with correct types
        var props = jsonSchema["properties"]!.AsObject();
        props["documentType"]!["type"]!.GetValue<string>().Should().Be("string");
        props["confidence"]!["type"]!.GetValue<string>().Should().Be("number");
        props["isValid"]!["type"]!.GetValue<string>().Should().Be("boolean");

        // Verify enum constraint is in schema
        var enumArray = props["documentType"]!["enum"]!.AsArray();
        enumArray.Should().HaveCount(3);
        enumArray[0]!.GetValue<string>().Should().Be("contract");

        // Verify numeric constraints
        props["confidence"]!["minimum"]!.GetValue<double>().Should().Be(0);
        props["confidence"]!["maximum"]!.GetValue<double>().Should().Be(1);

        // Verify required array
        var required = jsonSchema["required"]!.AsArray();
        required.Should().HaveCount(3);
        required.Select(n => n!.GetValue<string>()).Should().Contain("documentType");
        required.Select(n => n!.GetValue<string>()).Should().Contain("confidence");
        required.Select(n => n!.GetValue<string>()).Should().Contain("isValid");
    }

    [Fact]
    public void NoStructuredOutput_JsonSchemaIsNull()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "type", Type = "string" }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — no JSON Schema when structuredOutput is false
        result.JsonSchema.Should().BeNull();
        result.SchemaName.Should().BeNull();
    }

    // -- Edge case tests --

    [Fact]
    public void EmptyConstraintsArray_NoConstraintsSection()
    {
        // Arrange — constraints is an empty array, not null
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Task = "Summarize",
                Constraints = Array.Empty<string>()
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — "## Constraints" heading should not appear
        result.PromptText.Should().NotContain("## Constraints");
    }

    [Fact]
    public void EmptyOutputFields_NoOutputSection()
    {
        // Arrange — output section exists but fields array is empty
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Output = new OutputSection
            {
                Fields = Array.Empty<OutputFieldDefinition>(),
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — no output instructions rendered
        result.PromptText.Should().NotContain("## Output Format");
        result.PromptText.Should().NotContain("Return valid JSON");
    }

    [Fact]
    public void NullOutput_NoOutputSection()
    {
        // Arrange — no output section at all
        var jps = BuildJpsJson(task: "Summarize the document");

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — no output instructions rendered
        result.PromptText.Should().NotContain("## Output Format");
        result.PromptText.Should().NotContain("Return valid JSON");
        result.PromptText.Should().NotContain("matching the provided schema");
    }

    [Fact]
    public void MultipleExamples_AllRendered()
    {
        // Arrange — three distinct examples
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Examples = new[]
            {
                new ExampleEntry
                {
                    Input = "Agreement for consulting services",
                    Output = JsonSerializer.SerializeToElement(new { type = "contract" })
                },
                new ExampleEntry
                {
                    Input = "Invoice #12345 for $5,000",
                    Output = JsonSerializer.SerializeToElement(new { type = "invoice" })
                },
                new ExampleEntry
                {
                    Input = "Dear HR team, I would like to request...",
                    Output = JsonSerializer.SerializeToElement(new { type = "letter" })
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — all three examples appear
        var text = result.PromptText;
        text.Should().Contain("## Examples");

        // Each example's input should appear
        text.Should().Contain("Input: \"Agreement for consulting services\"");
        text.Should().Contain("Input: \"Invoice #12345 for $5,000\"");
        text.Should().Contain("Input: \"Dear HR team, I would like to request...\"");

        // Each example's output should appear
        text.Should().Contain("\"contract\"");
        text.Should().Contain("\"invoice\"");
        text.Should().Contain("\"letter\"");

        // Expected output label should appear for each example
        var expectedOutputCount = text.Split("Expected output:").Length - 1;
        expectedOutputCount.Should().Be(3);
    }

    // -- $choices resolution tests --

    [Fact]
    public void ChoicesResolved_FromDownstreamNode()
    {
        // Arrange — JPS schema with a field referencing $choices from a downstream node
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify the document type" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "documentType",
                        Type = "string",
                        Description = "The type of document",
                        Choices = "downstream:update_doc.sprk_documenttype"
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        var downstreamNodes = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["invoice"] = 100000001,
                ["proposal"] = 100000002
            }))
        };

        // Act
        var result = _sut.Render(jps, null, null, null, null, downstreamNodes);

        // Assert — option keys should appear in the rendered output text
        result.PromptText.Should().Contain("contract");
        result.PromptText.Should().Contain("invoice");
        result.PromptText.Should().Contain("proposal");
        result.PromptText.Should().Contain("one of:");
    }

    [Fact]
    public void ChoicesResolved_InjectsEnumValues()
    {
        // Arrange — structuredOutput = true, so enum values should appear in JSON Schema
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify the document type" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "documentType",
                        Type = "string",
                        Description = "The type of document",
                        Choices = "downstream:update_doc.sprk_documenttype"
                    }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        var downstreamNodes = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["invoice"] = 100000001,
                ["proposal"] = 100000002
            }))
        };

        // Act
        var result = _sut.Render(jps, null, null, null, null, downstreamNodes);

        // Assert — JSON Schema should contain enum array with option keys
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();
        var docTypeSchema = props["documentType"]!;
        var enumArray = docTypeSchema["enum"]!.AsArray();
        enumArray.Should().HaveCount(3);

        var enumValues = enumArray.Select(n => n!.GetValue<string>()).ToList();
        enumValues.Should().Contain("contract");
        enumValues.Should().Contain("invoice");
        enumValues.Should().Contain("proposal");
    }

    [Fact]
    public void UnresolvableChoices_GracefulDegradation()
    {
        // Arrange — $choices references a downstream node that does not exist
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "documentType",
                        Type = "string",
                        Description = "The type of document",
                        Choices = "downstream:nonexistent_node.sprk_documenttype"
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Pass empty downstream nodes — the reference cannot be resolved
        var downstreamNodes = new List<DownstreamNodeInfo>();

        // Act — should not throw
        var act = () => _sut.Render(jps, null, null, null, null, downstreamNodes);

        // Assert — rendering succeeds, field renders without enum values
        var result = act.Should().NotThrow().Subject;
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("documentType");
        result.PromptText.Should().NotContain("one of:");
    }

    [Fact]
    public void ChoicesResolved_NoDownstreamNodes_GracefulFallback()
    {
        // Arrange — $choices references exist but downstreamNodes is null
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "documentType",
                        Type = "string",
                        Choices = "downstream:update_doc.sprk_documenttype"
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act — pass null for downstreamNodes
        var act = () => _sut.Render(jps, null, null, null, null, null);

        // Assert — rendering succeeds without exception
        var result = act.Should().NotThrow().Subject;
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("documentType");
    }

    [Fact]
    public void ChoicesResolved_MalformedConfigJson_GracefulFallback()
    {
        // Arrange — downstream node has invalid ConfigJson
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "documentType",
                        Type = "string",
                        Choices = "downstream:update_doc.sprk_documenttype"
                    }
                },
                StructuredOutput = false
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        var downstreamNodes = new List<DownstreamNodeInfo>
        {
            new("update_doc", "{ this is not valid JSON !!!")
        };

        // Act — should not throw despite malformed JSON
        var act = () => _sut.Render(jps, null, null, null, null, downstreamNodes);

        // Assert — rendering succeeds
        var result = act.Should().NotThrow().Subject;
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("documentType");
    }

    // -- JSON Schema generation tests --

    [Fact]
    public void JsonSchema_AllFieldTypes_MappedCorrectly()
    {
        // Arrange — schema with all supported field types
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Extract data" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "title", Type = "string" },
                    new OutputFieldDefinition { Name = "score", Type = "number" },
                    new OutputFieldDefinition { Name = "isValid", Type = "boolean" },
                    new OutputFieldDefinition { Name = "tags", Type = "array" },
                    new OutputFieldDefinition { Name = "metadata", Type = "object" }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — each field has the correct type in JSON Schema
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();

        props["title"]!["type"]!.GetValue<string>().Should().Be("string");
        props["score"]!["type"]!.GetValue<string>().Should().Be("number");
        props["isValid"]!["type"]!.GetValue<string>().Should().Be("boolean");
        props["tags"]!["type"]!.GetValue<string>().Should().Be("array");
        props["metadata"]!["type"]!.GetValue<string>().Should().Be("object");
    }

    [Fact]
    public void JsonSchema_RequiredArray_ContainsAllFields()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Extract" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "alpha", Type = "string" },
                    new OutputFieldDefinition { Name = "beta", Type = "number" },
                    new OutputFieldDefinition { Name = "gamma", Type = "boolean" },
                    new OutputFieldDefinition { Name = "delta", Type = "array" }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — required array contains all field names
        result.JsonSchema.Should().NotBeNull();
        var required = result.JsonSchema!["required"]!.AsArray();
        var requiredNames = required.Select(n => n!.GetValue<string>()).ToList();

        requiredNames.Should().HaveCount(4);
        requiredNames.Should().Contain("alpha");
        requiredNames.Should().Contain("beta");
        requiredNames.Should().Contain("gamma");
        requiredNames.Should().Contain("delta");
    }

    [Fact]
    public void JsonSchema_AdditionalPropertiesFalse()
    {
        // Arrange
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "category", Type = "string" }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — additionalProperties must be false (Azure OpenAI requirement)
        result.JsonSchema.Should().NotBeNull();
        result.JsonSchema!["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void JsonSchema_EnumFromChoices_Included()
    {
        // Arrange — $choices resolved enum values should appear in JSON Schema
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "status",
                        Type = "string",
                        Description = "Document status",
                        Choices = "downstream:update_doc.sprk_status"
                    }
                },
                StructuredOutput = true
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        var downstreamNodes = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_status", new Dictionary<string, int>
            {
                ["draft"] = 100000000,
                ["review"] = 100000001,
                ["approved"] = 100000002,
                ["rejected"] = 100000003
            }))
        };

        // Act
        var result = _sut.Render(jps, null, null, null, null, downstreamNodes);

        // Assert — enum values from $choices resolution appear in the JSON Schema
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();
        var statusSchema = props["status"]!;
        var enumArray = statusSchema["enum"]!.AsArray();
        var enumValues = enumArray.Select(n => n!.GetValue<string>()).ToList();

        enumValues.Should().HaveCount(4);
        enumValues.Should().Contain("draft");
        enumValues.Should().Contain("review");
        enumValues.Should().Contain("approved");
        enumValues.Should().Contain("rejected");
    }

    // -- $knowledge resolution tests --

    [Fact]
    public void KnowledgeRef_ResolvedAndRendered()
    {
        // Arrange — schema with scopes.$knowledge having a $ref
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze the contract" },
            Scopes = new ScopesSection
            {
                Knowledge = new[]
                {
                    new KnowledgeReference
                    {
                        Ref = "knowledge:standard-contract-clauses"
                    }
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        var additionalKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("standard-contract-clauses", "Force Majeure: An extraordinary event beyond control.", null)
        };

        // Act
        var result = _sut.Render(jps, null, null, null, null, null,
            additionalKnowledge: additionalKnowledge);

        // Assert — content appears under "Reference Knowledge" (default heading)
        result.PromptText.Should().Contain("## Reference Knowledge");
        result.PromptText.Should().Contain("Force Majeure: An extraordinary event beyond control.");
    }

    [Fact]
    public void KnowledgeRef_WithAsLabel_RenderedUnderCorrectHeading()
    {
        // Arrange — schema with $knowledge ref using "as": "definitions"
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Scopes = new ScopesSection
            {
                Knowledge = new[]
                {
                    new KnowledgeReference
                    {
                        Ref = "knowledge:legal-terms",
                        As = "definitions"
                    }
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        var additionalKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("legal-terms", "Indemnification: Obligation to compensate for loss.", null)
        };

        // Act
        var result = _sut.Render(jps, null, null, null, null, null,
            additionalKnowledge: additionalKnowledge);

        // Assert — content appears under "## Definitions" heading
        result.PromptText.Should().Contain("## Definitions");
        result.PromptText.Should().Contain("Indemnification: Obligation to compensate for loss.");
    }

    [Fact]
    public void KnowledgeInline_RenderedDirectly()
    {
        // Arrange — schema with inline $knowledge entry (no external resolution needed)
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Scopes = new ScopesSection
            {
                Knowledge = new[]
                {
                    new KnowledgeReference
                    {
                        Inline = "Document types include contracts, invoices, and memos."
                    }
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Act — no additionalKnowledge needed for inline entries
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — inline content is rendered
        result.PromptText.Should().Contain("Document types include contracts, invoices, and memos.");
        result.PromptText.Should().Contain("## Reference Knowledge");
    }

    [Fact]
    public void KnowledgeRef_Missing_GracefulDegradation()
    {
        // Arrange — schema references knowledge that is NOT in additionalKnowledge
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Scopes = new ScopesSection
            {
                Knowledge = new[]
                {
                    new KnowledgeReference
                    {
                        Ref = "knowledge:nonexistent-knowledge"
                    }
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        // Pass an empty list — the reference cannot be matched
        var additionalKnowledge = new List<ResolvedKnowledgeRef>();

        // Act — should not throw
        var act = () => _sut.Render(jps, null, null, null, null, null,
            additionalKnowledge: additionalKnowledge);

        // Assert — renders successfully without the missing knowledge content
        var result = act.Should().NotThrow().Subject;
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("Analyze");
        result.PromptText.Should().NotContain("## Reference Knowledge");
    }

    [Fact]
    public void KnowledgeRef_MergedWithNnScopes()
    {
        // Arrange — both N:N knowledgeContext AND additionalKnowledge provided
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Scopes = new ScopesSection
            {
                Knowledge = new[]
                {
                    new KnowledgeReference
                    {
                        Ref = "knowledge:extra-context"
                    }
                }
            }
        };
        var jps = JsonSerializer.Serialize(schema);

        var knowledgeContext = "N:N scope knowledge: baseline definitions here.";
        var additionalKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("extra-context", "Additional context from $ref resolution.", null)
        };

        // Act
        var result = _sut.Render(jps, null, knowledgeContext, null, null, null,
            additionalKnowledge: additionalKnowledge);

        // Assert — both N:N and $ref knowledge appear; N:N context appears first
        var text = result.PromptText;
        text.Should().Contain("## Reference Knowledge");
        text.Should().Contain("N:N scope knowledge: baseline definitions here.");
        text.Should().Contain("Additional context from $ref resolution.");

        // Verify N:N context appears before the $ref-resolved content
        var nnIdx = text.IndexOf("N:N scope knowledge:", StringComparison.Ordinal);
        var refIdx = text.IndexOf("Additional context from $ref resolution.", StringComparison.Ordinal);
        nnIdx.Should().BeLessThan(refIdx, "N:N scope knowledge should appear before $ref-resolved knowledge");
    }

    // -- $skill resolution tests --

    [Fact]
    public void SkillRef_ResolvedAndRendered()
    {
        // Arrange — schema with $skills ref
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze liability" },
            Scopes = new ScopesSection
            {
                Skills = new[]
                {
                    JsonSerializer.SerializeToElement(new { Ref = "skill:liability-analysis" })
                }
            }
        };

        // The schema serialization uses JsonElement for skills; build the JSON
        // manually to get the correct "$ref" property name
        var jpsJson = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "task": "Analyze liability"
            },
            "scopes": {
                "$skills": [
                    { "$ref": "skill:liability-analysis" }
                ]
            }
        }
        """;

        var additionalSkills = new List<ResolvedSkillRef>
        {
            new("liability-analysis", "When analyzing liability, consider indemnification clauses and limitation of liability.")
        };

        // Act
        var result = _sut.Render(jpsJson, null, null, null, null, null,
            additionalSkills: additionalSkills);

        // Assert — skill prompt fragment appears
        result.PromptText.Should().Contain("## Additional Analysis Instructions");
        result.PromptText.Should().Contain("When analyzing liability, consider indemnification clauses and limitation of liability.");
    }

    [Fact]
    public void SkillRef_MergedWithNnScopes()
    {
        // Arrange — both N:N skillContext AND additionalSkills provided
        var jpsJson = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "task": "Analyze the document"
            },
            "scopes": {
                "$skills": [
                    { "$ref": "skill:deep-analysis" }
                ]
            }
        }
        """;

        var skillContext = "N:N scope skill: Apply standard review methodology.";
        var additionalSkills = new List<ResolvedSkillRef>
        {
            new("deep-analysis", "Deep analysis: Examine every clause for hidden obligations.")
        };

        // Act
        var result = _sut.Render(jpsJson, skillContext, null, null, null, null,
            additionalSkills: additionalSkills);

        // Assert — both N:N and $ref skills appear; N:N appears first
        var text = result.PromptText;
        text.Should().Contain("## Additional Analysis Instructions");
        text.Should().Contain("N:N scope skill: Apply standard review methodology.");
        text.Should().Contain("Deep analysis: Examine every clause for hidden obligations.");

        // Verify N:N context appears before the $ref-resolved content
        var nnIdx = text.IndexOf("N:N scope skill:", StringComparison.Ordinal);
        var refIdx = text.IndexOf("Deep analysis:", StringComparison.Ordinal);
        nnIdx.Should().BeLessThan(refIdx, "N:N scope skill context should appear before $ref-resolved skill");
    }

    // -- Template parameter substitution tests --

    [Fact]
    public void Jps_TemplateParameter_SubstitutedInTaskField()
    {
        // Arrange — JPS with {{documentType}} in the task field
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "task": "Analyze this {{documentType}} for compliance issues."
            }
        }
        """;

        var parameters = new Dictionary<string, object?>
        {
            { "documentType", "contract" }
        };

        // Act
        var result = _sut.Render(jps, null, null, null, parameters, null);

        // Assert
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("Analyze this contract for compliance issues.");
        result.PromptText.Should().NotContain("{{documentType}}");
    }

    [Fact]
    public void Jps_TemplateParameter_NoParameters_PlaceholderLeftAsIs()
    {
        // Arrange — JPS with {{documentType}} but no parameters provided
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "task": "Analyze this {{documentType}} for compliance issues."
            }
        }
        """;

        // Act — null parameters
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — placeholder remains untouched
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("{{documentType}}");
    }

    [Fact]
    public void Jps_TemplateParameter_ParametersButNoPlaceholders_TextUnchanged()
    {
        // Arrange — JPS with no placeholders, parameters provided anyway
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "task": "Analyze this document for compliance issues."
            }
        }
        """;

        var parameters = new Dictionary<string, object?>
        {
            { "documentType", "contract" }
        };

        // Act
        var result = _sut.Render(jps, null, null, null, parameters, null);

        // Assert — text is unchanged
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("Analyze this document for compliance issues.");
    }

    [Fact]
    public void Jps_TemplateParameter_MultipleParameters_AllSubstituted()
    {
        // Arrange — JPS with multiple placeholders in role, task, context, and constraints
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "role": "You are a {{role}} specialist.",
                "task": "Review the {{documentType}} from {{clientName}}.",
                "context": "The client operates in the {{industry}} sector.",
                "constraints": [
                    "Focus on {{jurisdiction}} regulations.",
                    "Flag any {{riskLevel}} risk items."
                ]
            }
        }
        """;

        var parameters = new Dictionary<string, object?>
        {
            { "role", "legal" },
            { "documentType", "contract" },
            { "clientName", "Acme Corp" },
            { "industry", "healthcare" },
            { "jurisdiction", "EU" },
            { "riskLevel", "high" }
        };

        // Act
        var result = _sut.Render(jps, null, null, null, parameters, null);

        // Assert — all placeholders substituted across all instruction fields
        var text = result.PromptText;
        text.Should().Contain("You are a legal specialist.");
        text.Should().Contain("Review the contract from Acme Corp.");
        text.Should().Contain("The client operates in the healthcare sector.");
        text.Should().Contain("Focus on EU regulations.");
        text.Should().Contain("Flag any high risk items.");

        // No unresolved placeholders remain
        text.Should().NotContain("{{");
        text.Should().NotContain("}}");
    }

    [Fact]
    public void Jps_TemplateParameter_NullValue_ReplacedWithEmptyString()
    {
        // Arrange — parameter value is null
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "task": "Analyze the {{documentType}} document."
            }
        }
        """;

        var parameters = new Dictionary<string, object?>
        {
            { "documentType", null }
        };

        // Act
        var result = _sut.Render(jps, null, null, null, parameters, null);

        // Assert — null value becomes empty string
        result.PromptText.Should().Contain("Analyze the  document.");
        result.PromptText.Should().NotContain("{{documentType}}");
    }

    // -- Helpers --

    /// <summary>
    /// Builds a minimal JPS JSON string with the given optional sections.
    /// Always includes the "$schema" key so format detection triggers.
    /// </summary>
    private static string BuildJpsJson(
        string task,
        string? role = null,
        string[]? constraints = null)
    {
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = role,
                Task = task,
                Constraints = constraints
            }
        };

        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// Builds a ConfigJson string for a downstream node with a single field mapping
    /// containing the given options (used for $choices resolution tests).
    /// </summary>
    private static string BuildChoiceConfigJson(string fieldName, Dictionary<string, int> options)
    {
        var config = new
        {
            fieldMappings = new[]
            {
                new
                {
                    field = fieldName,
                    type = "choice",
                    options
                }
            }
        };
        return JsonSerializer.Serialize(config);
    }
}
