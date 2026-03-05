using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Focused tests for <c>$choices</c> resolution in the JPS prompt schema renderer.
/// Validates how output field enum values are dynamically resolved from downstream
/// node field mappings via the <c>"downstream:{outputVariable}.{fieldName}"</c> syntax.
/// </summary>
public class ChoicesResolutionTests
{
    private readonly PromptSchemaRenderer _sut;

    public ChoicesResolutionTests()
    {
        _sut = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
    }

    // -----------------------------------------------------------------------
    // Successful resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Choices_Resolved_TextOutput_InjectsEnumAsOneOf()
    {
        // Arrange -- text output (structuredOutput = false) with $choices reference
        var schema = BuildSchema(
            task: "Classify the document",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "category",
                    Type = "string",
                    Description = "Document category",
                    Choices = "downstream:update_record.sprk_category"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_record", BuildChoiceConfigJson("sprk_category", new Dictionary<string, int>
            {
                ["legal"] = 100000000,
                ["financial"] = 100000001,
                ["hr"] = 100000002
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- resolved values appear in text prompt as "one of:" constraint
        result.PromptText.Should().Contain("legal");
        result.PromptText.Should().Contain("financial");
        result.PromptText.Should().Contain("hr");
        result.PromptText.Should().Contain("one of:");
    }

    [Fact]
    public void Choices_Resolved_StructuredOutput_InjectsEnumInJsonSchema()
    {
        // Arrange -- structured output with $choices reference
        var schema = BuildSchema(
            task: "Classify the document",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "priority",
                    Type = "string",
                    Description = "Priority level",
                    Choices = "downstream:update_task.sprk_priority"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_task", BuildChoiceConfigJson("sprk_priority", new Dictionary<string, int>
            {
                ["low"] = 100000000,
                ["medium"] = 100000001,
                ["high"] = 100000002,
                ["critical"] = 100000003
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- JSON Schema contains enum array with option keys
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();
        var enumArray = props["priority"]!["enum"]!.AsArray();
        var enumValues = enumArray.Select(n => n!.GetValue<string>()).ToList();

        enumValues.Should().HaveCount(4);
        enumValues.Should().BeEquivalentTo("low", "medium", "high", "critical");
    }

    [Fact]
    public void StaticEnum_NoChoices_UsedAsIs()
    {
        // Arrange -- field has static enum values, no $choices reference
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "status",
                    Type = "string",
                    Description = "Document status",
                    Enum = new[] { "active", "inactive", "archived" }
                }
            },
            structuredOutput: false);

        // Act -- no downstream nodes needed
        var result = _sut.Render(schema, null, null, null, null, null);

        // Assert -- static enum values appear in prompt text
        result.PromptText.Should().Contain("active");
        result.PromptText.Should().Contain("inactive");
        result.PromptText.Should().Contain("archived");
        result.PromptText.Should().Contain("one of:");
    }

    [Fact]
    public void StaticEnum_NoChoices_StructuredOutput_EnumInJsonSchema()
    {
        // Arrange -- field has static enum, structured output
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "status",
                    Type = "string",
                    Description = "Status",
                    Enum = new[] { "open", "closed" }
                }
            },
            structuredOutput: true);

        // Act
        var result = _sut.Render(schema, null, null, null, null, null);

        // Assert -- JSON Schema enum reflects static values
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();
        var enumArray = props["status"]!["enum"]!.AsArray();
        var enumValues = enumArray.Select(n => n!.GetValue<string>()).ToList();
        enumValues.Should().BeEquivalentTo("open", "closed");
    }

    [Fact]
    public void Choices_OverridesExistingEnum()
    {
        // Arrange -- field has both static enum AND $choices; $choices takes precedence
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Description = "Document type",
                    Enum = new[] { "old_value_1", "old_value_2" },
                    Choices = "downstream:update_doc.sprk_doctype"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_doctype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["memo"] = 100000001
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- $choices values replace the static enum
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();
        var enumArray = props["docType"]!["enum"]!.AsArray();
        var enumValues = enumArray.Select(n => n!.GetValue<string>()).ToList();

        enumValues.Should().BeEquivalentTo("contract", "memo");
        enumValues.Should().NotContain("old_value_1");
        enumValues.Should().NotContain("old_value_2");
    }

    // -----------------------------------------------------------------------
    // Multiple output fields with different $choices sources
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleFields_DifferentChoicesSources_AllResolved()
    {
        // Arrange -- two fields referencing different downstream nodes
        var schema = BuildSchema(
            task: "Classify and tag document",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Description = "Document type",
                    Choices = "downstream:update_doc.sprk_documenttype"
                },
                new OutputFieldDefinition
                {
                    Name = "priority",
                    Type = "string",
                    Description = "Priority",
                    Choices = "downstream:update_task.sprk_priority"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["invoice"] = 100000001
            })),
            new("update_task", BuildChoiceConfigJson("sprk_priority", new Dictionary<string, int>
            {
                ["low"] = 100000000,
                ["high"] = 100000001
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- both fields have their respective enums
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();

        var docTypeEnum = props["docType"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        docTypeEnum.Should().BeEquivalentTo("contract", "invoice");

        var priorityEnum = props["priority"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        priorityEnum.Should().BeEquivalentTo("low", "high");
    }

    [Fact]
    public void MultipleFields_SameDownstreamNode_DifferentFields()
    {
        // Arrange -- two output fields referencing the same downstream node but different fields
        var configJson = JsonSerializer.Serialize(new
        {
            fieldMappings = new object[]
            {
                new
                {
                    field = "sprk_documenttype",
                    type = "choice",
                    options = new Dictionary<string, int>
                    {
                        ["contract"] = 100000000,
                        ["invoice"] = 100000001
                    }
                },
                new
                {
                    field = "sprk_status",
                    type = "choice",
                    options = new Dictionary<string, int>
                    {
                        ["draft"] = 100000000,
                        ["final"] = 100000001,
                        ["archived"] = 100000002
                    }
                }
            }
        });

        var schema = BuildSchema(
            task: "Classify document",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                },
                new OutputFieldDefinition
                {
                    Name = "status",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_status"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", configJson)
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();

        var docTypeEnum = props["docType"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        docTypeEnum.Should().BeEquivalentTo("contract", "invoice");

        var statusEnum = props["status"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        statusEnum.Should().BeEquivalentTo("draft", "final", "archived");
    }

    [Fact]
    public void MixedFields_SomeWithChoices_SomeWithStaticEnum_SomeWithNeither()
    {
        // Arrange -- three fields: one $choices, one static enum, one plain string
        var schema = BuildSchema(
            task: "Analyze document",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Description = "Type",
                    Choices = "downstream:update_doc.sprk_documenttype"
                },
                new OutputFieldDefinition
                {
                    Name = "confidence",
                    Type = "string",
                    Description = "Confidence level",
                    Enum = new[] { "low", "medium", "high" }
                },
                new OutputFieldDefinition
                {
                    Name = "summary",
                    Type = "string",
                    Description = "Brief summary"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["invoice"] = 100000001
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();

        // $choices field resolved
        var docTypeEnum = props["docType"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        docTypeEnum.Should().BeEquivalentTo("contract", "invoice");

        // Static enum preserved
        var confEnum = props["confidence"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        confEnum.Should().BeEquivalentTo("low", "medium", "high");

        // Plain string has no enum
        props["summary"]!["type"]!.GetValue<string>().Should().Be("string");
        props["summary"]!.AsObject().Should().NotContainKey("enum");
    }

    // -----------------------------------------------------------------------
    // Graceful fallback / degradation scenarios
    // -----------------------------------------------------------------------

    [Fact]
    public void Choices_NullDownstreamNodes_DoesNotThrow()
    {
        // Arrange -- $choices reference but null downstream nodes
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                }
            },
            structuredOutput: false);

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, null);

        // Assert -- no exception, field still renders (without enum values)
        var result = act.Should().NotThrow().Subject;
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("docType");
        result.PromptText.Should().NotContain("one of:");
    }

    [Fact]
    public void Choices_EmptyDownstreamNodes_DoesNotThrow()
    {
        // Arrange -- $choices reference but empty downstream list
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                }
            },
            structuredOutput: false);

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, new List<DownstreamNodeInfo>());

        // Assert
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
        result.PromptText.Should().NotContain("one of:");
    }

    [Fact]
    public void Choices_NonexistentDownstreamNode_GracefulFallback()
    {
        // Arrange -- $choices references a node that does not exist in the list
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:nonexistent_node.sprk_documenttype"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("different_node", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000
            }))
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- rendering succeeds, field present but no enum constraint
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
        result.PromptText.Should().NotContain("one of:");
    }

    [Fact]
    public void Choices_MalformedConfigJson_GracefulFallback()
    {
        // Arrange -- downstream node ConfigJson is not valid JSON
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", "<<< not valid json >>>")
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
    }

    [Fact]
    public void Choices_NullConfigJson_GracefulFallback()
    {
        // Arrange -- downstream node has null ConfigJson
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", null)
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
    }

    [Fact]
    public void Choices_ConfigJson_MissingFieldMappings_GracefulFallback()
    {
        // Arrange -- ConfigJson is valid JSON but has no "fieldMappings" array
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", """{"entityName": "sprk_document"}""")
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
    }

    [Fact]
    public void Choices_ConfigJson_FieldNotFound_GracefulFallback()
    {
        // Arrange -- fieldMappings exists but the target field is not in the array
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_status", new Dictionary<string, int>
            {
                ["active"] = 100000000
            }))
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- field present but no enum since target field not found
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
        result.PromptText.Should().NotContain("one of:");
    }

    [Fact]
    public void Choices_ConfigJson_EmptyOptions_GracefulFallback()
    {
        // Arrange -- fieldMapping found but options object is empty
        var configJson = JsonSerializer.Serialize(new
        {
            fieldMappings = new[]
            {
                new
                {
                    field = "sprk_documenttype",
                    type = "choice",
                    options = new Dictionary<string, int>()
                }
            }
        });

        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.sprk_documenttype"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", configJson)
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
    }

    // -----------------------------------------------------------------------
    // Invalid $choices reference format
    // -----------------------------------------------------------------------

    [Fact]
    public void Choices_InvalidPrefix_NotDownstream_GracefulFallback()
    {
        // Arrange -- $choices value does not start with "downstream:"
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "upstream:some_node.sprk_field"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("some_node", BuildChoiceConfigJson("sprk_field", new Dictionary<string, int>
            {
                ["val"] = 100000000
            }))
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- unrecognized prefix is skipped gracefully
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
    }

    [Fact]
    public void Choices_MissingDotSeparator_GracefulFallback()
    {
        // Arrange -- "downstream:update_doc" with no ".fieldName"
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000
            }))
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- malformed reference handled gracefully
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
    }

    [Fact]
    public void Choices_DotAtEnd_GracefulFallback()
    {
        // Arrange -- "downstream:update_doc." with trailing dot (empty field name)
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc."
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000
            }))
        };

        // Act
        var act = () => _sut.Render(schema, null, null, null, null, downstream);

        // Assert
        var result = act.Should().NotThrow().Subject;
        result.PromptText.Should().Contain("docType");
    }

    // -----------------------------------------------------------------------
    // Case-insensitive matching
    // -----------------------------------------------------------------------

    [Fact]
    public void Choices_CaseInsensitive_OutputVariableMatching()
    {
        // Arrange -- output variable has different casing
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:UPDATE_DOC.sprk_documenttype"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["invoice"] = 100000001
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- case-insensitive match succeeds
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();
        var enumArray = props["docType"]!["enum"]!.AsArray();
        enumArray.Should().HaveCount(2);
    }

    [Fact]
    public void Choices_CaseInsensitive_FieldNameMatching()
    {
        // Arrange -- target field name casing differs
        var schema = BuildSchema(
            task: "Classify",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Choices = "downstream:update_doc.SPRK_DOCUMENTTYPE"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["invoice"] = 100000001
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- case-insensitive field matching works
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();
        var enumArray = props["docType"]!["enum"]!.AsArray();
        enumArray.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Partial resolution (some resolve, some fail)
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleFields_OneResolves_OneFailsGracefully()
    {
        // Arrange -- two $choices fields, only one downstream node available
        var schema = BuildSchema(
            task: "Classify and tag",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Description = "Type",
                    Choices = "downstream:update_doc.sprk_documenttype"
                },
                new OutputFieldDefinition
                {
                    Name = "priority",
                    Type = "string",
                    Description = "Priority",
                    Choices = "downstream:missing_node.sprk_priority"
                }
            },
            structuredOutput: true);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["invoice"] = 100000001
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- docType resolved, priority falls back (no enum)
        result.JsonSchema.Should().NotBeNull();
        var props = result.JsonSchema!["properties"]!.AsObject();

        var docTypeEnum = props["docType"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        docTypeEnum.Should().BeEquivalentTo("contract", "invoice");

        // priority should have type but no enum (graceful fallback)
        props["priority"]!["type"]!.GetValue<string>().Should().Be("string");
        props["priority"]!.AsObject().Should().NotContainKey("enum");
    }

    // -----------------------------------------------------------------------
    // Text output format integration
    // -----------------------------------------------------------------------

    [Fact]
    public void Choices_TextOutput_MultipleFieldsRendered()
    {
        // Arrange -- text output mode with multiple $choices fields
        var schema = BuildSchema(
            task: "Analyze the document",
            fields: new[]
            {
                new OutputFieldDefinition
                {
                    Name = "docType",
                    Type = "string",
                    Description = "Document type",
                    Choices = "downstream:update_doc.sprk_documenttype"
                },
                new OutputFieldDefinition
                {
                    Name = "summary",
                    Type = "string",
                    Description = "Brief summary"
                }
            },
            structuredOutput: false);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000,
                ["memo"] = 100000001
            }))
        };

        // Act
        var result = _sut.Render(schema, null, null, null, null, downstream);

        // Assert -- choices field has "one of:", plain field does not
        result.PromptText.Should().Contain("contract");
        result.PromptText.Should().Contain("memo");
        result.PromptText.Should().Contain("one of:");
        result.PromptText.Should().Contain("summary");
    }

    // -----------------------------------------------------------------------
    // No fields / empty fields edge case
    // -----------------------------------------------------------------------

    [Fact]
    public void NoOutputFields_NoChoicesResolution_Succeeds()
    {
        // Arrange -- schema with no output section
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = "Just analyze" }
        };
        var jps = JsonSerializer.Serialize(schema);

        var downstream = new List<DownstreamNodeInfo>
        {
            new("update_doc", BuildChoiceConfigJson("sprk_documenttype", new Dictionary<string, int>
            {
                ["contract"] = 100000000
            }))
        };

        // Act
        var act = () => _sut.Render(jps, null, null, null, null, downstream);

        // Assert -- no crash
        var result = act.Should().NotThrow().Subject;
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a serialized JPS schema string from the given parts.
    /// </summary>
    private static string BuildSchema(
        string task,
        OutputFieldDefinition[] fields,
        bool structuredOutput)
    {
        var schema = new PromptSchema
        {
            Schema = "https://spaarke.com/schemas/prompt/v1",
            Instruction = new InstructionSection { Task = task },
            Output = new OutputSection
            {
                Fields = fields,
                StructuredOutput = structuredOutput
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
