using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

// Disambiguate — ResolvedKnowledgeRef exists in both Ai and Ai.Schemas namespaces.
using ResolvedKnowledgeRef = Sprk.Bff.Api.Services.Ai.ResolvedKnowledgeRef;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Integration tests proving that JPS $ref entries (knowledge and skill scopes)
/// resolve correctly and appear in the rendered prompt output.
/// Uses ACT-001.json as the reference JPS document which declares:
///   $knowledge: ["knowledge:standard-contract-clauses", "knowledge:commercial-risk-factors"]
/// </summary>
public class JpsRefResolutionIntegrationTests
{
    private readonly PromptSchemaRenderer _sut;

    /// <summary>
    /// ACT-001.json inlined — the contract analysis JPS prompt with two $knowledge refs.
    /// </summary>
    private const string Act001Jps = """
        {
          "$schema": "https://spaarke.com/schemas/prompt/v1",
          "$version": 1,
          "instruction": {
            "role": "You are a senior commercial contracts attorney.",
            "task": "Perform a comprehensive review of the provided contract document.",
            "constraints": [
              "Cite the relevant contract section for each finding",
              "Flag items requiring attention with [ACTION REQUIRED] or [REVIEW RECOMMENDED]"
            ],
            "context": "The user is a legal or business professional reviewing commercial contracts."
          },
          "input": {
            "document": {
              "required": true,
              "maxLength": 50000,
              "placeholder": "{{document.extractedText}}"
            }
          },
          "output": {
            "fields": [
              {
                "name": "executiveSummary",
                "type": "string",
                "description": "One-paragraph executive summary"
              },
              {
                "name": "riskLevel",
                "type": "string",
                "enum": ["low", "medium", "high", "critical"],
                "description": "Overall risk assessment"
              }
            ],
            "structuredOutput": true
          },
          "scopes": {
            "$knowledge": [
              { "$ref": "knowledge:standard-contract-clauses", "as": "reference" },
              { "$ref": "knowledge:commercial-risk-factors", "as": "definitions" }
            ]
          },
          "metadata": {
            "author": "migration",
            "description": "Contract analysis prompt"
          }
        }
        """;

    /// <summary>
    /// A minimal JPS prompt with a $skills scope section for skill ref tests.
    /// </summary>
    private const string JpsWithSkillRefs = """
        {
          "$schema": "https://spaarke.com/schemas/prompt/v1",
          "$version": 1,
          "instruction": {
            "role": "You are an AI assistant.",
            "task": "Analyze the document using specialized skills."
          },
          "scopes": {
            "$skills": [
              { "$ref": "skill:clause-comparison" }
            ]
          },
          "metadata": {
            "author": "test",
            "description": "Skill ref test prompt"
          }
        }
        """;

    public JpsRefResolutionIntegrationTests()
    {
        _sut = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
    }

    // ---------------------------------------------------------------
    // Knowledge $ref resolution
    // ---------------------------------------------------------------

    [Fact]
    public void Render_WithResolvedKnowledgeRefs_IncludesContentInPrompt()
    {
        // Arrange
        var resolvedKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("standard-contract-clauses", "Force Majeure: An unforeseeable event that prevents a party from fulfilling contractual obligations.", "reference"),
            new("commercial-risk-factors", "Key risk factors include counterparty default, currency fluctuation, and regulatory changes.", "definitions")
        };

        // Act
        var rendered = _sut.Render(
            Act001Jps,
            skillContext: null,
            knowledgeContext: null,
            documentText: "Sample contract document text for testing.",
            templateParameters: null,
            downstreamNodes: null,
            additionalKnowledge: resolvedKnowledge,
            additionalSkills: null);

        // Assert — resolved knowledge content must appear in the assembled prompt
        rendered.PromptText.Should().Contain("Force Majeure");
        rendered.PromptText.Should().Contain("counterparty default");
        rendered.Format.Should().Be(PromptFormat.JsonPromptSchema);
    }

    [Fact]
    public void Render_WithResolvedKnowledgeRefs_GroupsByLabel()
    {
        // Arrange — "reference" → "Reference Knowledge" heading, "definitions" → "Definitions" heading
        var resolvedKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("standard-contract-clauses", "Force Majeure clause content here.", "reference"),
            new("commercial-risk-factors", "Counterparty default risk content here.", "definitions")
        };

        // Act
        var rendered = _sut.Render(
            Act001Jps,
            skillContext: null,
            knowledgeContext: null,
            documentText: "Sample contract text.",
            templateParameters: null,
            downstreamNodes: null,
            additionalKnowledge: resolvedKnowledge,
            additionalSkills: null);

        // Assert — both label-based headings should appear
        rendered.PromptText.Should().Contain("## Reference Knowledge");
        rendered.PromptText.Should().Contain("## Definitions");
        rendered.PromptText.Should().Contain("Force Majeure clause content here.");
        rendered.PromptText.Should().Contain("Counterparty default risk content here.");
    }

    [Fact]
    public void Render_WithNoKnowledgeRefs_DoesNotCrashAndOmitsKnowledgeSection()
    {
        // Arrange — no additional knowledge, and the JPS has $ref entries that will go unresolved
        // Act
        var rendered = _sut.Render(
            Act001Jps,
            skillContext: null,
            knowledgeContext: null,
            documentText: "Sample contract text.",
            templateParameters: null,
            downstreamNodes: null,
            additionalKnowledge: null,
            additionalSkills: null);

        // Assert — should not crash and should still produce valid output
        rendered.PromptText.Should().NotBeNullOrEmpty();
        rendered.Format.Should().Be(PromptFormat.JsonPromptSchema);
        // Without resolved refs, the knowledge content should not appear
        rendered.PromptText.Should().NotContain("Force Majeure");
        rendered.PromptText.Should().NotContain("counterparty default");
    }

    [Fact]
    public void Render_WithEmptyKnowledgeRefList_DoesNotCrash()
    {
        // Arrange — empty list (not null)
        var emptyKnowledge = new List<ResolvedKnowledgeRef>();

        // Act
        var rendered = _sut.Render(
            Act001Jps,
            skillContext: null,
            knowledgeContext: null,
            documentText: "Sample contract text.",
            templateParameters: null,
            downstreamNodes: null,
            additionalKnowledge: emptyKnowledge,
            additionalSkills: null);

        // Assert — should not crash
        rendered.PromptText.Should().NotBeNullOrEmpty();
        rendered.Format.Should().Be(PromptFormat.JsonPromptSchema);
    }

    // ---------------------------------------------------------------
    // Skill $ref resolution
    // ---------------------------------------------------------------

    [Fact]
    public void Render_WithResolvedSkillRefs_IncludesPromptFragmentInOutput()
    {
        // Arrange
        var resolvedSkills = new List<ResolvedSkillRef>
        {
            new("clause-comparison", "When comparing clauses, identify semantic differences and flag material deviations from standard language.")
        };

        // Act
        var rendered = _sut.Render(
            JpsWithSkillRefs,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            additionalKnowledge: null,
            additionalSkills: resolvedSkills);

        // Assert — skill prompt fragment should appear in the rendered output
        rendered.PromptText.Should().Contain("semantic differences");
        rendered.PromptText.Should().Contain("material deviations");
        rendered.Format.Should().Be(PromptFormat.JsonPromptSchema);
    }

    [Fact]
    public void Render_WithNoSkillRefs_DoesNotIncludeSkillSection()
    {
        // Arrange — JPS has skill scopes, but no resolved skills provided

        // Act
        var rendered = _sut.Render(
            JpsWithSkillRefs,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            additionalKnowledge: null,
            additionalSkills: null);

        // Assert — should not crash, skill fragment should not appear
        rendered.PromptText.Should().NotBeNullOrEmpty();
        rendered.Format.Should().Be(PromptFormat.JsonPromptSchema);
        rendered.PromptText.Should().NotContain("semantic differences");
    }

    // ---------------------------------------------------------------
    // Combined: knowledge + skill refs together
    // ---------------------------------------------------------------

    [Fact]
    public void Render_WithBothKnowledgeAndSkillRefs_IncludesBothInOutput()
    {
        // Arrange — use a JPS that has both $knowledge and $skills
        const string jpsWithBoth = """
            {
              "$schema": "https://spaarke.com/schemas/prompt/v1",
              "$version": 1,
              "instruction": {
                "role": "You are a contract analyst.",
                "task": "Analyze the document."
              },
              "scopes": {
                "$knowledge": [
                  { "$ref": "knowledge:standard-contract-clauses", "as": "reference" }
                ],
                "$skills": [
                  { "$ref": "skill:clause-comparison" }
                ]
              },
              "metadata": { "author": "test" }
            }
            """;

        var resolvedKnowledge = new List<ResolvedKnowledgeRef>
        {
            new("standard-contract-clauses", "Indemnification: A contractual obligation to compensate for losses.", "reference")
        };

        var resolvedSkills = new List<ResolvedSkillRef>
        {
            new("clause-comparison", "Compare clauses against industry-standard templates.")
        };

        // Act
        var rendered = _sut.Render(
            jpsWithBoth,
            skillContext: null,
            knowledgeContext: null,
            documentText: "Contract text here.",
            templateParameters: null,
            downstreamNodes: null,
            additionalKnowledge: resolvedKnowledge,
            additionalSkills: resolvedSkills);

        // Assert — both knowledge and skill content must appear
        rendered.PromptText.Should().Contain("Indemnification");
        rendered.PromptText.Should().Contain("industry-standard templates");
        rendered.Format.Should().Be(PromptFormat.JsonPromptSchema);
    }
}
