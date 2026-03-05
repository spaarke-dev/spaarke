using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>
/// Integration tests for the full JPS pipeline: JPS JSON → format detection →
/// rendering → structured output. Exercises PromptSchemaRenderer as the core
/// pipeline component with realistic JPS payloads.
/// </summary>
public class JpsPipelineIntegrationTests
{
    private readonly PromptSchemaRenderer _sut;

    public JpsPipelineIntegrationTests()
    {
        _sut = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
    }

    // ===================================================================
    // Simple JPS → renders correct prompt text with role, task, context, constraints
    // ===================================================================

    [Fact]
    public void SimpleJps_RendersRoleTaskContextConstraints()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "role": "You are a senior commercial contracts attorney.",
                "task": "Review the provided contract and identify key risks.",
                "constraints": [
                    "Cite the relevant contract section for each finding",
                    "Only use information present in the document"
                ],
                "context": "The user is a legal professional reviewing commercial contracts."
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("You are a senior commercial contracts attorney.");
        result.PromptText.Should().Contain("Review the provided contract and identify key risks.");
        result.PromptText.Should().Contain("## Constraints");
        result.PromptText.Should().Contain("1. Cite the relevant contract section for each finding");
        result.PromptText.Should().Contain("2. Only use information present in the document");
        result.PromptText.Should().Contain("The user is a legal professional reviewing commercial contracts.");
        result.JsonSchema.Should().BeNull("no output section was specified");
    }

    [Fact]
    public void SimpleJps_RolePrecedesTask_TaskPrecedesConstraints()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "role": "You are an analyst.",
                "task": "Summarize the document.",
                "constraints": ["Be concise"]
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert — verify ordering: role comes before task, task before constraints
        var text = result.PromptText;
        text.IndexOf("You are an analyst.").Should().BeLessThan(text.IndexOf("Summarize the document."));
        text.IndexOf("Summarize the document.").Should().BeLessThan(text.IndexOf("## Constraints"));
    }

    // ===================================================================
    // JPS with template parameters → {{param}} substituted in rendered output
    // ===================================================================

    [Fact]
    public void JpsWithTemplateParameters_SubstitutesInRole()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "role": "You are an expert in {{jurisdiction}} law.",
                "task": "Analyze the contract."
            }
        }
        """;
        var parameters = new Dictionary<string, object?>
        {
            { "jurisdiction", "New York" }
        };

        // Act
        var result = _sut.Render(jps, null, null, null, parameters, null);

        // Assert
        result.PromptText.Should().Contain("You are an expert in New York law.");
        result.PromptText.Should().NotContain("{{jurisdiction}}");
    }

    [Fact]
    public void JpsWithTemplateParameters_SubstitutesInTaskAndContext()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Analyze the {{documentType}} document.",
                "context": "This document is from {{clientName}}."
            }
        }
        """;
        var parameters = new Dictionary<string, object?>
        {
            { "documentType", "NDA" },
            { "clientName", "Acme Corp" }
        };

        // Act
        var result = _sut.Render(jps, null, null, null, parameters, null);

        // Assert
        result.PromptText.Should().Contain("Analyze the NDA document.");
        result.PromptText.Should().Contain("This document is from Acme Corp.");
    }

    [Fact]
    public void JpsWithTemplateParameters_SubstitutesInConstraints()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Review the contract.",
                "constraints": [
                    "Apply {{jurisdiction}} legal standards",
                    "Limit response to {{maxWords}} words"
                ]
            }
        }
        """;
        var parameters = new Dictionary<string, object?>
        {
            { "jurisdiction", "Delaware" },
            { "maxWords", "500" }
        };

        // Act
        var result = _sut.Render(jps, null, null, null, parameters, null);

        // Assert
        result.PromptText.Should().Contain("Apply Delaware legal standards");
        result.PromptText.Should().Contain("Limit response to 500 words");
    }

    // ===================================================================
    // JPS with structuredOutput → JSON Schema generated
    // ===================================================================

    [Fact]
    public void JpsWithStructuredOutput_GeneratesJsonSchema()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Classify the document."
            },
            "output": {
                "fields": [
                    { "name": "documentType", "type": "string", "enum": ["contract", "invoice", "letter"] },
                    { "name": "confidence", "type": "number", "minimum": 0, "maximum": 1 }
                ],
                "structuredOutput": true
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.JsonSchema.Should().NotBeNull("structuredOutput is true");
        result.SchemaName.Should().Be("prompt_response");

        // Verify schema structure
        var schema = result.JsonSchema!;
        schema["type"]!.GetValue<string>().Should().Be("object");
        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();

        // Verify properties
        var props = schema["properties"]!.AsObject();
        props.Should().ContainKey("documentType");
        props.Should().ContainKey("confidence");

        // Verify enum values on documentType
        var docTypeProp = props["documentType"]!.AsObject();
        docTypeProp["type"]!.GetValue<string>().Should().Be("string");
        docTypeProp["enum"]!.AsArray().Should().HaveCount(3);

        // Verify numeric constraints on confidence
        var confProp = props["confidence"]!.AsObject();
        confProp["type"]!.GetValue<string>().Should().Be("number");
        confProp["minimum"]!.GetValue<double>().Should().Be(0);
        confProp["maximum"]!.GetValue<double>().Should().Be(1);

        // Verify required array
        var required = schema["required"]!.AsArray();
        required.Should().HaveCount(2);
    }

    [Fact]
    public void JpsWithStructuredOutputFalse_DoesNotGenerateJsonSchema()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Classify the document."
            },
            "output": {
                "fields": [
                    { "name": "documentType", "type": "string" }
                ],
                "structuredOutput": false
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.JsonSchema.Should().BeNull("structuredOutput is false");
        result.SchemaName.Should().BeNull();
        result.PromptText.Should().Contain("## Output Format");
        result.PromptText.Should().Contain("documentType (string)");
    }

    // ===================================================================
    // JPS with scopes → knowledge/skills rendered into context
    // ===================================================================

    [Fact]
    public void JpsWithResolvedKnowledge_InjectsKnowledgeIntoPrompt()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Analyze the contract."
            },
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:standard-clauses", "as": "reference" }
                ]
            }
        }
        """;

        var additionalKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("standard-clauses", "Standard clause definitions: indemnification, limitation of liability, force majeure.", "reference")
        };

        // Act
        var result = _sut.Render(jps, null, null, null, null, null, additionalKnowledge);

        // Assert
        result.PromptText.Should().Contain("Standard clause definitions");
        result.PromptText.Should().Contain("indemnification");
    }

    [Fact]
    public void JpsWithInlineKnowledge_RendersInlineContent()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Assess document risk."
            },
            "scopes": {
                "$knowledge": [
                    { "inline": "Risk levels: Low (routine), Medium (requires review), High (escalate immediately).", "as": "definitions" }
                ]
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("Risk levels: Low (routine)");
    }

    [Fact]
    public void JpsWithNnScopeKnowledge_RendersKnowledgeContext()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Analyze the contract."
            }
        }
        """;
        var knowledgeContext = "N:N scope knowledge: This is pre-resolved knowledge from scope relationships.";

        // Act
        var result = _sut.Render(jps, null, knowledgeContext, null, null, null);

        // Assert
        result.PromptText.Should().Contain("N:N scope knowledge");
    }

    [Fact]
    public void JpsWithResolvedSkills_InjectsSkillFragmentsIntoPrompt()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Perform clause comparison."
            },
            "scopes": {
                "$skills": [
                    { "$ref": "skill:clause-comparison" }
                ]
            }
        }
        """;

        var additionalSkills = new List<ResolvedSkillRef>
        {
            new("clause-comparison", "When comparing clauses, highlight deviations from standard language and rate severity.")
        };

        // Act
        var result = _sut.Render(jps, null, null, null, null, null, additionalSkills: additionalSkills);

        // Assert
        result.PromptText.Should().Contain("comparing clauses");
        result.PromptText.Should().Contain("highlight deviations");
    }

    // ===================================================================
    // Malformed JPS → graceful error handling (fallback to flat text)
    // ===================================================================

    [Fact]
    public void MalformedJps_FallsBackToFlatText()
    {
        // Arrange — starts with { and contains "$schema" but is not valid JSON
        var malformedJps = """{ "$schema": "https://spaarke.com/schemas/prompt/v1", broken!!! }""";

        // Act
        var result = _sut.Render(malformedJps, null, null, null, null, null);

        // Assert
        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(malformedJps);
        result.JsonSchema.Should().BeNull();
    }

    [Fact]
    public void JpsWithMissingRequiredInstructionField_FallsBackToFlatText()
    {
        // Arrange — valid JSON but missing required "instruction" field
        var invalidJps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "output": {
                "fields": [{ "name": "summary", "type": "string" }]
            }
        }
        """;

        // Act
        var result = _sut.Render(invalidJps, null, null, null, null, null);

        // Assert — should fall back to flat text since deserialization will fail
        // (instruction.task is required)
        result.Format.Should().Be(PromptFormat.FlatText);
    }

    // ===================================================================
    // Legacy flat text (non-JPS) → format detection returns false, legacy path used
    // ===================================================================

    [Fact]
    public void LegacyFlatText_DetectedAsFlatText_ReturnedAsIs()
    {
        // Arrange
        var legacyPrompt = "You are a document analyst. Summarize the following document and extract key entities.";

        // Act
        var result = _sut.Render(legacyPrompt, null, null, null, null, null);

        // Assert
        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(legacyPrompt);
        result.JsonSchema.Should().BeNull();
        result.SchemaName.Should().BeNull();
    }

    [Fact]
    public void JsonWithoutSchemaProperty_TreatedAsFlatText()
    {
        // Arrange — valid JSON object but no "$schema" property → not detected as JPS
        var jsonWithoutSchema = """{ "instruction": { "task": "Do something" } }""";

        // Act
        var result = _sut.Render(jsonWithoutSchema, null, null, null, null, null);

        // Assert
        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(jsonWithoutSchema);
    }

    // ===================================================================
    // JPS with output.fields → output format section generated
    // ===================================================================

    [Fact]
    public void JpsWithOutputFields_NonStructured_RendersOutputFormatSection()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Profile the document."
            },
            "output": {
                "fields": [
                    { "name": "summary", "type": "string", "description": "A brief summary", "maxLength": 500 },
                    { "name": "riskLevel", "type": "string", "enum": ["low", "medium", "high"] },
                    { "name": "confidence", "type": "number", "minimum": 0, "maximum": 1 }
                ],
                "structuredOutput": false
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("## Output Format");
        result.PromptText.Should().Contain("summary (string): A brief summary");
        result.PromptText.Should().Contain("(max 500 chars)");
        result.PromptText.Should().Contain("riskLevel (string)");
        result.PromptText.Should().Contain("one of: low, medium, high");
        result.PromptText.Should().Contain("confidence (number)");
    }

    [Fact]
    public void JpsWithOutputFields_Structured_RendersSchemaInstruction()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Classify the document."
            },
            "output": {
                "fields": [
                    { "name": "documentType", "type": "string" }
                ],
                "structuredOutput": true
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.PromptText.Should().Contain("Return valid JSON matching the provided schema.");
        result.PromptText.Should().NotContain("## Output Format");
    }

    // ===================================================================
    // Full pipeline: realistic JPS → complete rendered prompt
    // ===================================================================

    [Fact]
    public void FullPipeline_RealisticJps_RendersCompletePromptWithAllSections()
    {
        // Arrange — a realistic JPS similar to document-profiler.json
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "role": "You are an expert document analyst specializing in rapid document profiling.",
                "task": "Profile the provided document by generating a summary, keywords, and document type classification.",
                "constraints": [
                    "Generate ALL output fields",
                    "Summary must be 2-4 paragraphs",
                    "Only extract information explicitly present in the document"
                ],
                "context": "This is an automated document profiling system for {{clientName}}."
            },
            "input": {
                "document": {
                    "required": true,
                    "maxLength": 100000,
                    "placeholder": "{{document.extractedText}}"
                }
            },
            "output": {
                "fields": [
                    { "name": "summary", "type": "string", "description": "Comprehensive summary", "maxLength": 5000 },
                    { "name": "keywords", "type": "string", "description": "Comma-separated keywords" },
                    { "name": "documentType", "type": "string", "enum": ["contract", "invoice", "letter", "nda", "other"] }
                ],
                "structuredOutput": true
            },
            "scopes": {
                "$knowledge": [
                    { "inline": "Document types: contract, invoice, letter, nda, other.", "as": "definitions" }
                ]
            },
            "examples": [
                {
                    "input": "AGREEMENT between Acme Corp and Beta LLC.",
                    "output": { "summary": "An agreement between two companies.", "keywords": "agreement, Acme Corp, Beta LLC", "documentType": "contract" }
                }
            ],
            "metadata": {
                "author": "migration",
                "tags": ["profiling", "classification"]
            }
        }
        """;

        var parameters = new Dictionary<string, object?>
        {
            { "clientName", "Contoso Legal" }
        };

        var documentText = "MASTER SERVICES AGREEMENT between Alpha Inc. and Beta Corp.";

        // Act
        var result = _sut.Render(jps, null, null, documentText, parameters, null);

        // Assert — format
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);

        // Assert — role rendered first
        result.PromptText.Should().Contain("You are an expert document analyst");

        // Assert — task rendered
        result.PromptText.Should().Contain("Profile the provided document");

        // Assert — constraints as numbered list
        result.PromptText.Should().Contain("## Constraints");
        result.PromptText.Should().Contain("1. Generate ALL output fields");
        result.PromptText.Should().Contain("2. Summary must be 2-4 paragraphs");
        result.PromptText.Should().Contain("3. Only extract information explicitly present");

        // Assert — context with template parameter substituted
        result.PromptText.Should().Contain("automated document profiling system for Contoso Legal");
        result.PromptText.Should().NotContain("{{clientName}}");

        // Assert — document injected
        result.PromptText.Should().Contain("## Document");
        result.PromptText.Should().Contain("MASTER SERVICES AGREEMENT between Alpha Inc. and Beta Corp.");

        // Assert — inline knowledge rendered
        result.PromptText.Should().Contain("Document types: contract, invoice, letter, nda, other.");

        // Assert — examples rendered
        result.PromptText.Should().Contain("## Examples");
        result.PromptText.Should().Contain("AGREEMENT between Acme Corp and Beta LLC.");

        // Assert — structured output: JSON Schema generated
        result.JsonSchema.Should().NotBeNull();
        result.SchemaName.Should().Be("prompt_response");

        var schema = result.JsonSchema!;
        var props = schema["properties"]!.AsObject();
        props.Should().ContainKey("summary");
        props.Should().ContainKey("keywords");
        props.Should().ContainKey("documentType");

        // Verify enum on documentType in JSON Schema
        var docTypeProp = props["documentType"]!.AsObject();
        docTypeProp["enum"]!.AsArray().Should().HaveCount(5);

        // Verify maxLength on summary in JSON Schema
        var summaryProp = props["summary"]!.AsObject();
        summaryProp["maxLength"]!.GetValue<int>().Should().Be(5000);
    }

    [Fact]
    public void FullPipeline_JpsWithDocumentAndSkillsAndKnowledge_AllSectionsPresent()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "role": "You are a risk assessment specialist.",
                "task": "Identify and rate risks in the contract."
            },
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:risk-definitions", "as": "definitions" }
                ],
                "$skills": [
                    { "$ref": "skill:risk-scoring" }
                ]
            }
        }
        """;

        var skillContext = "N:N skill context: You have access to risk assessment tools.";
        var knowledgeContext = "N:N knowledge context: Standard risk categories.";
        var documentText = "This contract contains an unlimited liability clause.";

        var additionalKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("risk-definitions", "Risk levels: Critical (immediate action required), High (review within 24h), Medium (routine review), Low (informational).", "definitions")
        };

        var additionalSkills = new List<ResolvedSkillRef>
        {
            new("risk-scoring", "Score each risk on a 1-10 scale based on likelihood and impact.")
        };

        // Act
        var result = _sut.Render(jps, skillContext, knowledgeContext, documentText, null, null, additionalKnowledge, additionalSkills);

        // Assert
        result.Format.Should().Be(PromptFormat.JsonPromptSchema);
        result.PromptText.Should().Contain("risk assessment specialist");
        result.PromptText.Should().Contain("Identify and rate risks");
        result.PromptText.Should().Contain("## Document");
        result.PromptText.Should().Contain("unlimited liability clause");
        result.PromptText.Should().Contain("Risk levels: Critical");
        result.PromptText.Should().Contain("Score each risk on a 1-10 scale");
    }

    // ===================================================================
    // JPS with array output fields → items schema in JSON Schema
    // ===================================================================

    [Fact]
    public void JpsWithArrayField_GeneratesItemsSchemaInJsonSchema()
    {
        // Arrange
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "instruction": {
                "task": "Extract key clauses."
            },
            "output": {
                "fields": [
                    { "name": "clauses", "type": "array", "description": "List of extracted clauses" }
                ],
                "structuredOutput": true
            }
        }
        """;

        // Act
        var result = _sut.Render(jps, null, null, null, null, null);

        // Assert
        result.JsonSchema.Should().NotBeNull();
        var clausesProp = result.JsonSchema!["properties"]!["clauses"]!.AsObject();
        clausesProp["type"]!.GetValue<string>().Should().Be("array");
        // Default items schema is string when not specified
        clausesProp["items"]!["type"]!.GetValue<string>().Should().Be("string");
    }
}
