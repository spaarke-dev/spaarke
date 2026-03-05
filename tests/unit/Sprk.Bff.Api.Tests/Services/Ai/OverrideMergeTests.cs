using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// End-to-end tests for the PromptSchemaOverrideMerger, exercising the full
/// ConfigJson.promptSchemaOverride → ExtractOverride → Merge pipeline.
/// </summary>
public class OverrideMergeTests
{
    /// <summary>
    /// Helper that simulates the executor flow: extract override from raw ConfigJson,
    /// then merge it into a base PromptSchema.
    /// </summary>
    private static PromptSchema ApplyOverride(PromptSchema baseSchema, string? configJson)
    {
        var schemaOverride = PromptSchemaOverrideMerger.ExtractOverride(configJson);
        return PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);
    }

    // ========================================================================
    // No-override scenarios: base JPS returned unchanged
    // ========================================================================

    [Fact]
    public void NoOverride_NullConfigJson_ReturnsBaseUnchanged()
    {
        var baseSchema = MakeBaseSchema();

        var result = ApplyOverride(baseSchema, null);

        result.Should().BeSameAs(baseSchema);
    }

    [Fact]
    public void NoOverride_EmptyConfigJson_ReturnsBaseUnchanged()
    {
        var baseSchema = MakeBaseSchema();

        var result = ApplyOverride(baseSchema, "");

        result.Should().BeSameAs(baseSchema);
    }

    [Fact]
    public void NoOverride_ConfigJsonWithoutOverrideKey_ReturnsBaseUnchanged()
    {
        var baseSchema = MakeBaseSchema();
        var configJson = JsonSerializer.Serialize(new { temperature = 0.7, maxTokens = 2000 });

        var result = ApplyOverride(baseSchema, configJson);

        result.Should().BeSameAs(baseSchema);
    }

    [Fact]
    public void NoOverride_InvalidJsonConfigJson_ReturnsBaseUnchanged()
    {
        var baseSchema = MakeBaseSchema();

        var result = ApplyOverride(baseSchema, "not valid json {{{");

        result.Should().BeSameAs(baseSchema);
    }

    [Fact]
    public void NoOverride_OverrideKeyIsNotObject_ReturnsBaseUnchanged()
    {
        var baseSchema = MakeBaseSchema();
        var configJson = JsonSerializer.Serialize(new { promptSchemaOverride = "not an object" });

        var result = ApplyOverride(baseSchema, configJson);

        result.Should().BeSameAs(baseSchema);
    }

    // ========================================================================
    // Instruction scalar field overrides
    // ========================================================================

    [Fact]
    public void OverrideRole_ReplacesRole_PreservesOthers()
    {
        var baseSchema = MakeBaseSchema(role: "General analyst", task: "Summarize", context: "Base context");
        var configJson = MakeConfigJson(new { instruction = new { role = "Financial specialist" } });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Role.Should().Be("Financial specialist");
        result.Instruction.Task.Should().Be("Summarize");
        result.Instruction.Context.Should().Be("Base context");
    }

    [Fact]
    public void OverrideTask_ReplacesTask_PreservesOthers()
    {
        var baseSchema = MakeBaseSchema(role: "Analyst", task: "Summarize the document");
        var configJson = MakeConfigJson(new { instruction = new { task = "Extract key obligations" } });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Task.Should().Be("Extract key obligations");
        result.Instruction.Role.Should().Be("Analyst");
    }

    [Fact]
    public void OverrideContext_ReplacesContext_PreservesOthers()
    {
        var baseSchema = MakeBaseSchema(role: "Analyst", task: "Analyze", context: "Original context");
        var configJson = MakeConfigJson(new { instruction = new { context = "New context for this node" } });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Context.Should().Be("New context for this node");
        result.Instruction.Role.Should().Be("Analyst");
        result.Instruction.Task.Should().Be("Analyze");
    }

    [Fact]
    public void OverrideEmptyTask_PreservesBaseTask()
    {
        // Empty task in override is treated as "not provided" by the merger
        var baseSchema = MakeBaseSchema(task: "Original task");
        var configJson = MakeConfigJson(new { instruction = new { task = "", role = "New role" } });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Task.Should().Be("Original task");
        result.Instruction.Role.Should().Be("New role");
    }

    // ========================================================================
    // Constraint merge behavior
    // ========================================================================

    [Fact]
    public void OverrideConstraints_Concatenated_ByDefault()
    {
        var baseSchema = MakeBaseSchema(
            task: "Analyze",
            constraints: new[] { "Be precise", "Use ISO dates" });
        var configJson = MakeConfigJson(new
        {
            instruction = new { constraints = new[] { "Focus on financial terms" } }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Constraints.Should().HaveCount(3);
        result.Instruction.Constraints.Should().ContainInOrder(
            "Be precise", "Use ISO dates", "Focus on financial terms");
    }

    [Fact]
    public void OverrideConstraints_WithReplaceDirective_ReplacesAll()
    {
        var baseSchema = MakeBaseSchema(
            task: "Analyze",
            constraints: new[] { "Be precise", "Use ISO dates" });
        var configJson = MakeConfigJson(new
        {
            instruction = new { constraints = new[] { "__replace", "Only JSON output" } }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints.Should().Contain("Only JSON output");
        result.Instruction.Constraints.Should().NotContain("__replace");
        result.Instruction.Constraints.Should().NotContain("Be precise");
    }

    [Fact]
    public void OverrideConstraints_EmptyArray_PreservesBase()
    {
        var baseSchema = MakeBaseSchema(
            task: "Analyze",
            constraints: new[] { "Be precise" });
        var configJson = MakeConfigJson(new
        {
            instruction = new { constraints = Array.Empty<string>() }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints.Should().Contain("Be precise");
    }

    [Fact]
    public void OverrideConstraints_BaseHasNone_OverrideUsed()
    {
        var baseSchema = MakeBaseSchema(task: "Analyze");
        var configJson = MakeConfigJson(new
        {
            instruction = new { constraints = new[] { "New constraint" } }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints.Should().Contain("New constraint");
    }

    // ========================================================================
    // Output field merge behavior
    // ========================================================================

    [Fact]
    public void OverrideOutputFields_Concatenated_ByDefault()
    {
        var baseSchema = MakeBaseSchemaWithOutput(
            new OutputFieldDefinition { Name = "summary", Type = "string" },
            new OutputFieldDefinition { Name = "score", Type = "number" });
        var configJson = MakeConfigJson(new
        {
            output = new
            {
                fields = new[] { new { name = "category", type = "string" } }
            }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Output!.Fields.Should().HaveCount(3);
        result.Output.Fields.Select(f => f.Name).Should()
            .ContainInOrder("summary", "score", "category");
    }

    [Fact]
    public void OverrideOutputFields_WithReplaceDirective_ReplacesAll()
    {
        var baseSchema = MakeBaseSchemaWithOutput(
            new OutputFieldDefinition { Name = "summary", Type = "string" },
            new OutputFieldDefinition { Name = "score", Type = "number" });
        var configJson = MakeConfigJson(new
        {
            output = new
            {
                fields = new[]
                {
                    new { name = "__replace", type = "string" },
                    new { name = "newField", type = "boolean" }
                }
            }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Output!.Fields.Should().HaveCount(1);
        result.Output.Fields[0].Name.Should().Be("newField");
        result.Output.Fields.Select(f => f.Name).Should().NotContain("summary");
        result.Output.Fields.Select(f => f.Name).Should().NotContain("__replace");
    }

    [Fact]
    public void OverrideOutput_BaseHasNoOutput_OverrideUsed()
    {
        var baseSchema = MakeBaseSchema(task: "Analyze");
        baseSchema.Output.Should().BeNull(); // sanity check
        var configJson = MakeConfigJson(new
        {
            output = new
            {
                fields = new[] { new { name = "result", type = "string" } }
            }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Output.Should().NotBeNull();
        result.Output!.Fields.Should().HaveCount(1);
        result.Output.Fields[0].Name.Should().Be("result");
    }

    [Fact]
    public void OverrideStructuredOutput_MergesWithOr()
    {
        // structuredOutput uses OR: if either base or override is true, result is true
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Output = new OutputSection
            {
                Fields = new[] { new OutputFieldDefinition { Name = "summary", Type = "string" } },
                StructuredOutput = false
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "" },
            Output = new OutputSection
            {
                Fields = Array.Empty<OutputFieldDefinition>(),
                StructuredOutput = true
            }
        };

        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        result.Output!.StructuredOutput.Should().BeTrue();
    }

    // ========================================================================
    // Input, Scopes, Metadata — override replaces if present
    // ========================================================================

    [Fact]
    public void OverrideInput_ReplacesEntireSection()
    {
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Input = new InputSection
            {
                Document = new DocumentInput { Required = true, MaxLength = 5000 }
            }
        };
        var configJson = MakeConfigJson(new
        {
            input = new
            {
                document = new { required = false, maxLength = 10000 }
            }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Input.Should().NotBeNull();
        result.Input!.Document!.Required.Should().BeFalse();
        result.Input.Document.MaxLength.Should().Be(10000);
    }

    [Fact]
    public void OverrideInput_Null_PreservesBaseInput()
    {
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Input = new InputSection
            {
                Document = new DocumentInput { Required = true }
            }
        };
        // Override with no input section
        var configJson = MakeConfigJson(new { instruction = new { role = "New role" } });

        var result = ApplyOverride(baseSchema, configJson);

        result.Input.Should().NotBeNull();
        result.Input!.Document!.Required.Should().BeTrue();
    }

    [Fact]
    public void OverrideMetadata_ReplacesEntireSection()
    {
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Metadata = new MetadataSection { Author = "original-author", Description = "Original" }
        };
        var configJson = MakeConfigJson(new
        {
            metadata = new { author = "override-author", description = "Overridden description" }
        });

        var result = ApplyOverride(baseSchema, configJson);

        result.Metadata.Should().NotBeNull();
        result.Metadata!.Author.Should().Be("override-author");
        result.Metadata.Description.Should().Be("Overridden description");
    }

    // ========================================================================
    // Examples merge behavior (concatenation, no __replace support)
    // ========================================================================

    [Fact]
    public void OverrideExamples_Concatenated()
    {
        var baseExampleOutput = JsonSerializer.SerializeToElement(new { summary = "base" });
        var overrideExampleOutput = JsonSerializer.SerializeToElement(new { summary = "override" });

        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Summarize" },
            Examples = new[]
            {
                new ExampleEntry { Input = "Base input", Output = baseExampleOutput }
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "" },
            Examples = new[]
            {
                new ExampleEntry { Input = "Override input", Output = overrideExampleOutput }
            }
        };

        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        result.Examples.Should().HaveCount(2);
        result.Examples![0].Input.Should().Be("Base input");
        result.Examples[1].Input.Should().Be("Override input");
    }

    [Fact]
    public void OverrideExamples_EmptyOverride_PreservesBase()
    {
        var baseExampleOutput = JsonSerializer.SerializeToElement(new { summary = "base" });
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Summarize" },
            Examples = new[]
            {
                new ExampleEntry { Input = "Base input", Output = baseExampleOutput }
            }
        };
        var schemaOverride = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "" },
            Examples = Array.Empty<ExampleEntry>()
        };

        var result = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

        result.Examples.Should().HaveCount(1);
        result.Examples![0].Input.Should().Be("Base input");
    }

    // ========================================================================
    // Cross-section isolation: override one section, others unchanged
    // ========================================================================

    [Fact]
    public void OverrideInstruction_DoesNotAffectOutput()
    {
        var baseSchema = MakeBaseSchemaWithOutput(
            new OutputFieldDefinition { Name = "summary", Type = "string" });
        baseSchema = baseSchema with
        {
            Instruction = baseSchema.Instruction with
            {
                Role = "Original role",
                Task = "Original task",
                Constraints = new[] { "Original constraint" }
            }
        };
        var configJson = MakeConfigJson(new
        {
            instruction = new
            {
                role = "New role",
                task = "New task",
                constraints = new[] { "__replace", "New constraint" }
            }
        });

        var result = ApplyOverride(baseSchema, configJson);

        // Instruction changed
        result.Instruction.Role.Should().Be("New role");
        result.Instruction.Task.Should().Be("New task");
        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints.Should().Contain("New constraint");

        // Output unchanged
        result.Output!.Fields.Should().HaveCount(1);
        result.Output.Fields[0].Name.Should().Be("summary");
    }

    [Fact]
    public void OverrideOutput_DoesNotAffectInstruction()
    {
        var baseSchema = MakeBaseSchemaWithOutput(
            new OutputFieldDefinition { Name = "summary", Type = "string" });
        baseSchema = baseSchema with
        {
            Instruction = baseSchema.Instruction with
            {
                Role = "Original role",
                Task = "Original task",
                Constraints = new[] { "Keep this" }
            }
        };
        var configJson = MakeConfigJson(new
        {
            output = new
            {
                fields = new[]
                {
                    new { name = "__replace", type = "string" },
                    new { name = "newField", type = "boolean" }
                }
            }
        });

        var result = ApplyOverride(baseSchema, configJson);

        // Instruction unchanged
        result.Instruction.Role.Should().Be("Original role");
        result.Instruction.Task.Should().Be("Original task");
        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints.Should().Contain("Keep this");

        // Output replaced
        result.Output!.Fields.Should().HaveCount(1);
        result.Output.Fields[0].Name.Should().Be("newField");
    }

    // ========================================================================
    // ExtractOverride edge cases via full pipeline
    // ========================================================================

    [Fact]
    public void ExtractOverride_PartialInstruction_OnlyConstraints_NormalizesTask()
    {
        // Override has only constraints, no task — NormalizeOverrideJson adds empty task
        var configJson = JsonSerializer.Serialize(new
        {
            promptSchemaOverride = new
            {
                instruction = new
                {
                    constraints = new[] { "Extra constraint" }
                }
            }
        });

        var result = PromptSchemaOverrideMerger.ExtractOverride(configJson);

        result.Should().NotBeNull();
        // Task is empty string (sentinel), treated as "not provided"
        result!.Instruction.Task.Should().BeEmpty();
        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints![0].Should().Be("Extra constraint");
    }

    [Fact]
    public void ExtractOverride_NoInstructionSection_NormalizesWithMinimal()
    {
        // Override with only output, no instruction at all
        var configJson = JsonSerializer.Serialize(new
        {
            promptSchemaOverride = new
            {
                output = new
                {
                    fields = new[] { new { name = "result", type = "string" } }
                }
            }
        });

        var result = PromptSchemaOverrideMerger.ExtractOverride(configJson);

        result.Should().NotBeNull();
        result!.Instruction.Should().NotBeNull();
        result.Instruction.Task.Should().BeEmpty();
        result.Output.Should().NotBeNull();
        result.Output!.Fields.Should().HaveCount(1);
    }

    [Fact]
    public void ExtractOverride_WhitespaceOnlyConfigJson_ReturnsNull()
    {
        var result = PromptSchemaOverrideMerger.ExtractOverride("   ");

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractOverride_OverrideKeyIsArray_ReturnsNull()
    {
        var configJson = JsonSerializer.Serialize(new
        {
            promptSchemaOverride = new[] { "not", "an", "object" }
        });

        var result = PromptSchemaOverrideMerger.ExtractOverride(configJson);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractOverride_OverrideKeyIsNumber_ReturnsNull()
    {
        var configJson = JsonSerializer.Serialize(new { promptSchemaOverride = 42 });

        var result = PromptSchemaOverrideMerger.ExtractOverride(configJson);

        result.Should().BeNull();
    }

    // ========================================================================
    // Full end-to-end: ConfigJson → extract → merge → verify
    // ========================================================================

    [Fact]
    public void EndToEnd_MultiSectionOverride_AllMergedCorrectly()
    {
        // Base schema with all sections populated
        var baseExampleOutput = JsonSerializer.SerializeToElement(new { summary = "base example" });
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = "Legal analyst",
                Task = "Review the contract for compliance",
                Context = "Corporate law context",
                Constraints = new[] { "Be thorough", "Cite clauses" }
            },
            Input = new InputSection
            {
                Document = new DocumentInput { Required = true, MaxLength = 5000 }
            },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "summary", Type = "string" },
                    new OutputFieldDefinition { Name = "riskScore", Type = "number" }
                },
                StructuredOutput = true
            },
            Examples = new[]
            {
                new ExampleEntry { Input = "Base example input", Output = baseExampleOutput }
            },
            Metadata = new MetadataSection { Author = "system", Description = "Base template" }
        };

        // Override: change role, add constraints, add output field
        var configJson = JsonSerializer.Serialize(new
        {
            promptSchemaOverride = new
            {
                instruction = new
                {
                    role = "Financial compliance specialist",
                    constraints = new[] { "Focus on financial regulations" }
                },
                output = new
                {
                    fields = new[]
                    {
                        new { name = "complianceStatus", type = "string" }
                    }
                }
            },
            otherSetting = "ignored"
        });

        var result = ApplyOverride(baseSchema, configJson);

        // Role replaced
        result.Instruction.Role.Should().Be("Financial compliance specialist");
        // Task preserved from base (override task was empty/sentinel)
        result.Instruction.Task.Should().Be("Review the contract for compliance");
        // Context preserved
        result.Instruction.Context.Should().Be("Corporate law context");
        // Constraints concatenated (2 base + 1 override)
        result.Instruction.Constraints.Should().HaveCount(3);
        result.Instruction.Constraints.Should().ContainInOrder(
            "Be thorough", "Cite clauses", "Focus on financial regulations");
        // Output fields concatenated (2 base + 1 override)
        result.Output!.Fields.Should().HaveCount(3);
        result.Output.Fields.Select(f => f.Name).Should()
            .ContainInOrder("summary", "riskScore", "complianceStatus");
        // structuredOutput still true
        result.Output.StructuredOutput.Should().BeTrue();
        // Input unchanged (override didn't have input)
        result.Input.Should().NotBeNull();
        result.Input!.Document!.Required.Should().BeTrue();
        // Examples unchanged
        result.Examples.Should().HaveCount(1);
        // Metadata unchanged
        result.Metadata!.Author.Should().Be("system");
    }

    [Fact]
    public void EndToEnd_ReplaceDirectives_BothConstraintsAndFields()
    {
        var baseSchema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Task = "Classify document",
                Constraints = new[] { "Old constraint 1", "Old constraint 2" }
            },
            Output = new OutputSection
            {
                Fields = new[]
                {
                    new OutputFieldDefinition { Name = "oldField1", Type = "string" },
                    new OutputFieldDefinition { Name = "oldField2", Type = "number" }
                }
            }
        };
        var configJson = JsonSerializer.Serialize(new
        {
            promptSchemaOverride = new
            {
                instruction = new
                {
                    constraints = new[] { "__replace", "New constraint only" }
                },
                output = new
                {
                    fields = new object[]
                    {
                        new { name = "__replace", type = "string" },
                        new { name = "newField1", type = "boolean" },
                        new { name = "newField2", type = "string" }
                    }
                }
            }
        });

        var result = ApplyOverride(baseSchema, configJson);

        // Constraints fully replaced
        result.Instruction.Constraints.Should().HaveCount(1);
        result.Instruction.Constraints.Should().Contain("New constraint only");
        result.Instruction.Constraints.Should().NotContain("__replace");
        // Output fields fully replaced
        result.Output!.Fields.Should().HaveCount(2);
        result.Output.Fields.Select(f => f.Name).Should()
            .ContainInOrder("newField1", "newField2");
        result.Output.Fields.Select(f => f.Name).Should().NotContain("__replace");
        result.Output.Fields.Select(f => f.Name).Should().NotContain("oldField1");
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static PromptSchema MakeBaseSchema(
        string? role = null,
        string task = "Analyze the document",
        string? context = null,
        string[]? constraints = null)
    {
        return new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection
            {
                Role = role,
                Task = task,
                Context = context,
                Constraints = constraints
            }
        };
    }

    private static PromptSchema MakeBaseSchemaWithOutput(params OutputFieldDefinition[] fields)
    {
        return new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Analyze" },
            Output = new OutputSection { Fields = fields }
        };
    }

    /// <summary>
    /// Wraps an anonymous object as the promptSchemaOverride inside a ConfigJson envelope.
    /// </summary>
    private static string MakeConfigJson(object overrideContent)
    {
        return JsonSerializer.Serialize(new { promptSchemaOverride = overrideContent });
    }
}
