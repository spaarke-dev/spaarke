using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class JpsRefResolverTests
{
    // ===================================================================
    // ExtractKnowledgeRefs
    // ===================================================================

    [Fact]
    public void ExtractKnowledgeRefs_NullInput_ReturnsEmptyList()
    {
        // Arrange
        string? input = null;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractKnowledgeRefs_EmptyString_ReturnsEmptyList()
    {
        // Arrange
        var input = string.Empty;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("This is plain text, not JSON")]
    [InlineData("SELECT * FROM table")]
    [InlineData("[1, 2, 3]")]
    public void ExtractKnowledgeRefs_NonJsonText_ReturnsEmptyList(string input)
    {
        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractKnowledgeRefs_ValidJpsWithTwoRefs_ReturnsBothWithNamesAndLabels()
    {
        // Arrange
        var jps = """
        {
            "instruction": { "role": "test" },
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:standard-contract-clauses", "as": "reference" },
                    { "$ref": "knowledge:commercial-risk-factors", "as": "definitions" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("standard-contract-clauses");
        result[0].Label.Should().Be("reference");
        result[1].Name.Should().Be("commercial-risk-factors");
        result[1].Label.Should().Be("definitions");
    }

    [Fact]
    public void ExtractKnowledgeRefs_JpsWithoutScopesSection_ReturnsEmptyList()
    {
        // Arrange
        var jps = """
        {
            "instruction": { "role": "analyst" },
            "input": { "document": { "required": true } }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractKnowledgeRefs_EmptyKnowledgeArray_ReturnsEmptyList()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$knowledge": []
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractKnowledgeRefs_KnowledgeRefWithoutLabel_ReturnsNullLabel()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:my-source" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("my-source");
        result[0].Label.Should().BeNull();
    }

    [Fact]
    public void ExtractKnowledgeRefs_RefWithWrongPrefix_IsIgnored()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$knowledge": [
                    { "$ref": "skill:wrong-prefix", "as": "oops" },
                    { "$ref": "knowledge:correct-one", "as": "ok" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("correct-one");
        result[0].Label.Should().Be("ok");
    }

    [Fact]
    public void ExtractKnowledgeRefs_RefWithEmptyNameAfterPrefix_IsIgnored()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractKnowledgeRefs_MalformedJson_ReturnsEmptyList()
    {
        // Arrange
        var input = "{ invalid json {{";

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractKnowledgeRefs_RealAct001Content_ExtractsCorrectRefs()
    {
        // Arrange — real JPS content from ACT-001.json (contract analysis tool)
        var jps = """
        {
            "$schema": "https://spaarke.com/schemas/prompt/v1",
            "$version": 1,
            "instruction": {
                "role": "You are a senior commercial contracts attorney with over 20 years of experience.",
                "task": "Perform a comprehensive review of the provided contract document.",
                "constraints": [
                    "Cite the relevant contract section for each finding",
                    "Only use information present in the document"
                ],
                "context": "The user is a legal or business professional reviewing commercial contracts."
            },
            "input": {
                "document": { "required": true, "maxLength": 50000, "placeholder": "{{document.extractedText}}" }
            },
            "output": {
                "fields": [
                    { "name": "executiveSummary", "type": "string" },
                    { "name": "riskLevel", "type": "string", "enum": ["low","medium","high","critical"] }
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
                "tags": ["contract", "legal", "risk-assessment"]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("standard-contract-clauses");
        result[0].Label.Should().Be("reference");
        result[1].Name.Should().Be("commercial-risk-factors");
        result[1].Label.Should().Be("definitions");
    }

    // ===================================================================
    // ExtractSkillRefs
    // ===================================================================

    [Fact]
    public void ExtractSkillRefs_NullInput_ReturnsEmptyList()
    {
        // Arrange
        string? input = null;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkillRefs_EmptyString_ReturnsEmptyList()
    {
        // Arrange
        var input = string.Empty;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkillRefs_ValidJpsWithObjectRefs_ReturnsSkillNames()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$skills": [
                    { "$ref": "skill:clause-comparison" },
                    { "$ref": "skill:risk-scoring" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be("clause-comparison");
        result[1].Should().Be("risk-scoring");
    }

    [Fact]
    public void ExtractSkillRefs_ValidJpsWithPlainStringRefs_ReturnsSkillNames()
    {
        // Arrange — skills array can contain plain strings per the implementation
        var jps = """
        {
            "scopes": {
                "$skills": [
                    "skill:search-documents",
                    "skill:summarize"
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be("search-documents");
        result[1].Should().Be("summarize");
    }

    [Fact]
    public void ExtractSkillRefs_MixedObjectAndStringRefs_ReturnsAll()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$skills": [
                    { "$ref": "skill:clause-comparison" },
                    "skill:summarize"
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be("clause-comparison");
        result[1].Should().Be("summarize");
    }

    [Fact]
    public void ExtractSkillRefs_JpsWithoutSkillsProperty_ReturnsEmptyList()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:some-source", "as": "data" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkillRefs_JpsWithoutScopesSection_ReturnsEmptyList()
    {
        // Arrange
        var jps = """
        {
            "instruction": { "role": "analyst" },
            "output": { "fields": [] }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkillRefs_EmptySkillsArray_ReturnsEmptyList()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$skills": []
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkillRefs_RefWithWrongPrefix_IsIgnored()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$skills": [
                    { "$ref": "knowledge:wrong-prefix" },
                    { "$ref": "skill:correct-one" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("correct-one");
    }

    [Fact]
    public void ExtractSkillRefs_MalformedJson_ReturnsEmptyList()
    {
        // Arrange
        var input = "{ broken }}}";

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(input);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkillRefs_RefWithEmptyNameAfterPrefix_IsIgnored()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$skills": [
                    { "$ref": "skill:" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().BeEmpty();
    }

    // ===================================================================
    // Cross-cutting: scopes with both $knowledge and $skills
    // ===================================================================

    [Fact]
    public void ExtractKnowledgeRefs_ScopesWithBothArrays_OnlyReturnsKnowledge()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:data-source", "as": "context" }
                ],
                "$skills": [
                    { "$ref": "skill:search" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractKnowledgeRefs(jps);

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("data-source");
        result[0].Label.Should().Be("context");
    }

    [Fact]
    public void ExtractSkillRefs_ScopesWithBothArrays_OnlyReturnsSkills()
    {
        // Arrange
        var jps = """
        {
            "scopes": {
                "$knowledge": [
                    { "$ref": "knowledge:data-source", "as": "context" }
                ],
                "$skills": [
                    { "$ref": "skill:search" }
                ]
            }
        }
        """;

        // Act
        var result = JpsRefResolver.ExtractSkillRefs(jps);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("search");
    }
}
