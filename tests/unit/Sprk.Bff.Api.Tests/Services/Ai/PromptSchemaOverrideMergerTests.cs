using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class PromptSchemaOverrideMergerTests
{
    // -- Null / identity merge tests --

    [Fact]
    public void NullOverride_ReturnsBaseUnchanged()
    {
        // Arrange
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = "You are a legal analyst",
                Task = "Analyze the contract"
            }
        };

        // Act
        var result = PromptSchemaOverrideMerger.Merge(baseSchema, null);

        // Assert — exact same reference returned
        result.Should().BeSameAs(baseSchema);
    }

    // -- Scalar field replacement tests --

    [Fact]
    public void ReplacesScalarFields_WhenPresent()
    {
        // Arrange
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = "You are a general analyst",
                Task = "Summarize the document",
                Context = "Base context"
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = "You are a financial analyst",
                Task = "Extract financial obligations"
            }
        };

        // Act
        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        // Assert — role and task replaced, context preserved from base
        result.Instruction.Role.Should().Be("You are a financial analyst");
        result.Instruction.Task.Should().Be("Extract financial obligations");
        result.Instruction.Context.Should().Be("Base context");
    }

    // -- Constraint merge tests --

    [Fact]
    public void ConcatenatesConstraints_ByDefault()
    {
        // Arrange — base has 2 constraints, override has 1
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Task = "Analyze",
                Constraints = new[] { "Be precise", "Use ISO 8601 format" }
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Task = "",
                Constraints = new[] { "Focus on financial clauses" }
            }
        };

        // Act
        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        // Assert — merged has 3 constraints (base + override concatenated)
        result.Instruction.Constraints.Should().HaveCount(3);
        result.Instruction.Constraints.Should().ContainInOrder(
            "Be precise", "Use ISO 8601 format", "Focus on financial clauses");
    }

    [Fact]
    public void ReplacesConstraints_WithDirective()
    {
        // Arrange — override constraints include "__replace" marker
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Task = "Analyze",
                Constraints = new[] { "Be precise", "Use ISO 8601 format" }
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Task = "",
                Constraints = new[] { "__replace", "Only output JSON", "No commentary" }
            }
        };

        // Act
        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        // Assert — base constraints fully replaced; __replace marker stripped
        result.Instruction.Constraints.Should().HaveCount(2);
        result.Instruction.Constraints.Should().ContainInOrder("Only output JSON", "No commentary");
        result.Instruction.Constraints.Should().NotContain("Be precise");
        result.Instruction.Constraints.Should().NotContain("__replace");
    }

    // -- Output field merge tests --

    [Fact]
    public void ConcatenatesOutputFields_ByDefault()
    {
        // Arrange — base has 2 fields, override has 1
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "summary", Type = "string" },
                    new OutputFieldDefinition { Name = "score", Type = "number" }
                }
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "category", Type = "string" }
                }
            }
        };

        // Act
        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        // Assert — merged has 3 fields
        result.Output!.Fields.Should().HaveCount(3);
        result.Output.Fields.Select(f => f.Name).Should()
            .ContainInOrder("summary", "score", "category");
    }

    [Fact]
    public void ReplacesOutputFields_WithDirective()
    {
        // Arrange — override has field named "__replace" to trigger full replacement
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Classify" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "summary", Type = "string" },
                    new OutputFieldDefinition { Name = "score", Type = "number" }
                }
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "" },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "__replace", Type = "string" },
                    new OutputFieldDefinition { Name = "newField", Type = "boolean" }
                }
            }
        };

        // Act
        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        // Assert — base fields fully replaced; __replace marker field stripped
        result.Output!.Fields.Should().HaveCount(1);
        result.Output.Fields[0].Name.Should().Be("newField");
        result.Output.Fields.Select(f => f.Name).Should().NotContain("summary");
        result.Output.Fields.Select(f => f.Name).Should().NotContain("__replace");
    }

    // -- Null preservation tests --

    [Fact]
    public void KeepsBaseScalars_WhenOverrideNull()
    {
        // Arrange — override has null role → base role should be preserved
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = "You are a contract specialist",
                Task = "Analyze",
                Context = "Original context"
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = null,
                Task = "",
                Context = null
            }
        };

        // Act
        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        // Assert — base values preserved when override has null
        result.Instruction.Role.Should().Be("You are a contract specialist");
        result.Instruction.Task.Should().Be("Analyze");
        result.Instruction.Context.Should().Be("Original context");
    }

    // -- ExtractOverride tests --

    [Fact]
    public void ExtractOverride_ValidConfigJson()
    {
        // Arrange — configJson with promptSchemaOverride
        var configJson = JsonSerializer.Serialize(new
        {
            promptSchemaOverride = new
            {
                instruction = new
                {
                    task = "Override task",
                    constraints = new[] { "Override constraint" }
                }
            },
            otherConfig = "value"
        });

        // Act
        var result = PromptSchemaOverrideMerger.ExtractOverride(configJson);

        // Assert — override extracted with instruction content
        result.Should().NotBeNull();
        result!.Instruction.Task.Should().Be("Override task");
        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints![0].Should().Be("Override constraint");
    }

    [Fact]
    public void ExtractOverride_NullConfigJson_ReturnsNull()
    {
        // Act
        var result = PromptSchemaOverrideMerger.ExtractOverride(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractOverride_NoOverrideKey_ReturnsNull()
    {
        // Arrange — configJson without promptSchemaOverride
        var configJson = JsonSerializer.Serialize(new
        {
            someOtherSetting = "value",
            fieldMappings = new[] { new { field = "test", type = "string" } }
        });

        // Act
        var result = PromptSchemaOverrideMerger.ExtractOverride(configJson);

        // Assert
        result.Should().BeNull();
    }
}
