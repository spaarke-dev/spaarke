using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class PromptSchemaTests
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = null, // JSON property names come from [JsonPropertyName]
        WriteIndented = false
    };

    [Fact]
    public void SerializesPromptSchemaToJson()
    {
        // Arrange
        var schema = CreateFullSchema();

        // Act
        var json = JsonSerializer.Serialize(schema, SerializeOptions);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("$schema").GetString()
            .Should().Be("https://spaarke.com/schemas/prompt/v1");
        root.GetProperty("$version").GetInt32()
            .Should().Be(1);

        root.GetProperty("instruction").GetProperty("role").GetString()
            .Should().Be("You are a contract analysis specialist");
        root.GetProperty("instruction").GetProperty("task").GetString()
            .Should().Be("Classify the document type");
        root.GetProperty("instruction").GetProperty("constraints").GetArrayLength()
            .Should().Be(2);

        root.GetProperty("output").GetProperty("structuredOutput").GetBoolean()
            .Should().BeFalse();
        root.GetProperty("output").GetProperty("fields").GetArrayLength()
            .Should().Be(2);

        root.GetProperty("examples").GetArrayLength()
            .Should().Be(1);

        root.GetProperty("metadata").GetProperty("author").GetString()
            .Should().Be("test-user");
    }

    [Fact]
    public void DeserializesJsonToPromptSchema()
    {
        // Arrange
        var json = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "role": "You are a legal analyst",
                "task": "Extract key dates from the document",
                "constraints": ["Be precise", "Use ISO 8601 format"],
                "context": "Focus on contract effective dates"
            },
            "output": {
                "fields": [
                    { "name": "dates", "type": "array", "description": "Extracted dates" }
                ],
                "structuredOutput": true
            },
            "metadata": {
                "author": "builder-agent",
                "authorLevel": 3,
                "tags": ["legal", "dates"]
            }
        }
        """;

        // Act
        var schema = JsonSerializer.Deserialize<PromptSchema>(json);

        // Assert
        schema.Should().NotBeNull();
        schema!.Schema.Should().Be("https://spaarke.com/schemas/prompt/v1");
        schema.Version.Should().Be(1);

        schema.Instruction.Role.Should().Be("You are a legal analyst");
        schema.Instruction.Task.Should().Be("Extract key dates from the document");
        schema.Instruction.Constraints.Should().HaveCount(2);
        schema.Instruction.Constraints![0].Should().Be("Be precise");
        schema.Instruction.Constraints[1].Should().Be("Use ISO 8601 format");
        schema.Instruction.Context.Should().Be("Focus on contract effective dates");

        schema.Output.Should().NotBeNull();
        schema.Output!.StructuredOutput.Should().BeTrue();
        schema.Output.Fields.Should().HaveCount(1);
        schema.Output.Fields[0].Name.Should().Be("dates");
        schema.Output.Fields[0].Type.Should().Be("array");

        schema.Metadata.Should().NotBeNull();
        schema.Metadata!.Author.Should().Be("builder-agent");
        schema.Metadata.AuthorLevel.Should().Be(3);
        schema.Metadata.Tags.Should().BeEquivalentTo(new[] { "legal", "dates" });
    }

    [Fact]
    public void RoundTripsPromptSchema()
    {
        // Arrange
        var original = CreateFullSchema();

        // Act
        var json = JsonSerializer.Serialize(original, SerializeOptions);
        var deserialized = JsonSerializer.Deserialize<PromptSchema>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Schema.Should().Be(original.Schema);
        deserialized.Version.Should().Be(original.Version);
        deserialized.Instruction.Role.Should().Be(original.Instruction.Role);
        deserialized.Instruction.Task.Should().Be(original.Instruction.Task);
        deserialized.Instruction.Constraints.Should().BeEquivalentTo(original.Instruction.Constraints);
        deserialized.Instruction.Context.Should().Be(original.Instruction.Context);

        deserialized.Output.Should().NotBeNull();
        deserialized.Output!.StructuredOutput.Should().Be(original.Output!.StructuredOutput);
        deserialized.Output.Fields.Should().HaveCount(original.Output.Fields.Count);
        deserialized.Output.Fields[0].Name.Should().Be(original.Output.Fields[0].Name);
        deserialized.Output.Fields[0].Type.Should().Be(original.Output.Fields[0].Type);

        deserialized.Examples.Should().HaveCount(original.Examples!.Count);
        deserialized.Examples![0].Input.Should().Be(original.Examples[0].Input);

        deserialized.Metadata.Should().NotBeNull();
        deserialized.Metadata!.Author.Should().Be(original.Metadata!.Author);
        deserialized.Metadata.Tags.Should().BeEquivalentTo(original.Metadata.Tags);
    }

    [Fact]
    public void HandlesMinimalSchema()
    {
        // Arrange — only required field is instruction.task
        var schema = new PromptSchema
        {
            Instruction = new InstructionSection { Task = "Summarize the document" }
        };

        // Act
        var json = JsonSerializer.Serialize(schema, SerializeOptions);
        var deserialized = JsonSerializer.Deserialize<PromptSchema>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Instruction.Task.Should().Be("Summarize the document");
        deserialized.Instruction.Role.Should().BeNull();
        deserialized.Instruction.Constraints.Should().BeNull();
        deserialized.Instruction.Context.Should().BeNull();
        deserialized.Schema.Should().BeNull();
        deserialized.Version.Should().Be(1); // default
        deserialized.Input.Should().BeNull();
        deserialized.Output.Should().BeNull();
        deserialized.Scopes.Should().BeNull();
        deserialized.Examples.Should().BeNull();
        deserialized.Metadata.Should().BeNull();
    }

    [Fact]
    public void HandlesNullOptionalFields()
    {
        // Arrange — all optional fields explicitly null/absent
        var schema = new PromptSchema
        {
            Schema = null,
            Instruction = new InstructionSection
            {
                Role = null,
                Task = "Do something",
                Constraints = null,
                Context = null
            },
            Input = null,
            Output = null,
            Scopes = null,
            Examples = null,
            Metadata = null
        };

        // Act
        var json = JsonSerializer.Serialize(schema, SerializeOptions);
        var deserialized = JsonSerializer.Deserialize<PromptSchema>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Instruction.Task.Should().Be("Do something");

        // All optional fields should survive round-trip as null
        deserialized.Schema.Should().BeNull();
        deserialized.Instruction.Role.Should().BeNull();
        deserialized.Instruction.Constraints.Should().BeNull();
        deserialized.Instruction.Context.Should().BeNull();
        deserialized.Input.Should().BeNull();
        deserialized.Output.Should().BeNull();
        deserialized.Scopes.Should().BeNull();
        deserialized.Examples.Should().BeNull();
        deserialized.Metadata.Should().BeNull();

        // Verify JSON does not contain unexpected keys for nulls
        // (System.Text.Json default: includes null properties)
        json.Should().Contain("\"instruction\"");
    }

    // -- Helper --

    private static PromptSchema CreateFullSchema()
    {
        return new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Version = 1,
            Instruction = new InstructionSection
            {
                Role = "You are a contract analysis specialist",
                Task = "Classify the document type",
                Constraints = new[] { "Be concise", "Use only provided categories" },
                Context = "The user is reviewing legal contracts"
            },
            Input = new InputSection
            {
                Document = new DocumentInput
                {
                    Required = true,
                    MaxLength = 50_000,
                    Placeholder = "{{document.extractedText}}"
                },
                PriorOutputs = new[]
                {
                    new PriorOutputReference
                    {
                        Variable = "classify",
                        Fields = new[] { "output.documentType", "output.confidence" },
                        Description = "Classification result from upstream node"
                    }
                }
            },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition
                    {
                        Name = "documentType",
                        Type = "string",
                        Description = "The classified document type",
                        Enum = new[] { "contract", "amendment", "nda" }
                    },
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
            },
            Examples = new[]
            {
                new ExampleEntry
                {
                    Input = "This agreement is entered into...",
                    Output = JsonSerializer.SerializeToElement(new
                    {
                        documentType = "contract",
                        confidence = 0.95
                    })
                }
            },
            Metadata = new MetadataSection
            {
                Author = "test-user",
                AuthorLevel = 2,
                CreatedAt = "2026-01-15T10:00:00Z",
                Description = "Document classification prompt",
                Tags = new[] { "legal", "classification" }
            }
        };
    }
}
