using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for TemplateEngine.
/// Tests variable substitution, nested access, and edge cases.
/// </summary>
public class TemplateEngineTests
{
    private readonly TemplateEngine _engine;
    private readonly Mock<ILogger<TemplateEngine>> _loggerMock;

    public TemplateEngineTests()
    {
        _loggerMock = new Mock<ILogger<TemplateEngine>>();
        _engine = new TemplateEngine(_loggerMock.Object);
    }

    #region Simple Variable Substitution

    [Fact]
    public void Render_SimpleVariable_SubstitutesCorrectly()
    {
        // Arrange
        var template = "Hello {{name}}!";
        var context = new Dictionary<string, object?> { ["name"] = "World" };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Hello World!");
    }

    [Fact]
    public void Render_MultipleVariables_SubstitutesAll()
    {
        // Arrange
        var template = "{{greeting}} {{name}}, you have {{count}} messages.";
        var context = new Dictionary<string, object?>
        {
            ["greeting"] = "Hello",
            ["name"] = "John",
            ["count"] = 5
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Hello John, you have 5 messages.");
    }

    [Fact]
    public void Render_SameVariableMultipleTimes_SubstitutesAll()
    {
        // Arrange
        var template = "{{name}} said hello. {{name}} then left.";
        var context = new Dictionary<string, object?> { ["name"] = "Alice" };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Alice said hello. Alice then left.");
    }

    #endregion

    #region Nested Object Access

    [Fact]
    public void Render_NestedObject_AccessesPropertyCorrectly()
    {
        // Arrange
        var template = "The document has {{analysis.parties}} parties.";
        var context = new Dictionary<string, object?>
        {
            ["analysis"] = new { parties = 3 }
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("The document has 3 parties.");
    }

    [Fact]
    public void Render_DeeplyNestedObject_AccessesCorrectly()
    {
        // Arrange
        var template = "Value: {{node.output.data.value}}";
        var context = new Dictionary<string, object?>
        {
            ["node"] = new
            {
                output = new
                {
                    data = new { value = "deep" }
                }
            }
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Value: deep");
    }

    [Fact]
    public void Render_NestedDictionary_AccessesCorrectly()
    {
        // Arrange
        var template = "Result: {{analysis.summary}}";
        var context = new Dictionary<string, object?>
        {
            ["analysis"] = new Dictionary<string, object>
            {
                ["summary"] = "Document analyzed successfully"
            }
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Result: Document analyzed successfully");
    }

    #endregion

    #region Missing Variables (Graceful Handling)

    [Fact]
    public void Render_MissingVariable_RendersEmptyString()
    {
        // Arrange
        var template = "Hello {{name}}!";
        var context = new Dictionary<string, object?>();

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Hello !");
    }

    [Fact]
    public void Render_MissingNestedProperty_RendersEmptyString()
    {
        // Arrange
        var template = "Value: {{node.output.missing}}";
        var context = new Dictionary<string, object?>
        {
            ["node"] = new { output = new { exists = true } }
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Value: ");
    }

    [Fact]
    public void Render_NullValue_RendersEmptyString()
    {
        // Arrange
        var template = "Value: {{value}}";
        var context = new Dictionary<string, object?> { ["value"] = null };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Value: ");
    }

    [Fact]
    public void Render_NullContext_RendersTemplateWithEmptyVariables()
    {
        // Arrange
        var template = "Hello {{name}}!";

        // Act
        var result = _engine.Render(template, (IDictionary<string, object?>)null!);

        // Assert
        result.Should().Be("Hello !");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Render_EmptyTemplate_ReturnsEmpty()
    {
        // Arrange
        var context = new Dictionary<string, object?> { ["name"] = "test" };

        // Act
        var result = _engine.Render(string.Empty, context);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Render_NullTemplate_ReturnsEmpty()
    {
        // Arrange
        var context = new Dictionary<string, object?> { ["name"] = "test" };

        // Act
        var result = _engine.Render(null!, context);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Render_NoVariables_ReturnsOriginalTemplate()
    {
        // Arrange
        var template = "Plain text without variables.";
        var context = new Dictionary<string, object?> { ["unused"] = "value" };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Plain text without variables.");
    }

    [Fact]
    public void Render_SpecialCharacters_PreservesContent()
    {
        // Arrange
        var template = "Special: {{content}}";
        var context = new Dictionary<string, object?>
        {
            ["content"] = "<script>alert('xss')</script>"
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert - NoEscape is set, so special chars are preserved
        result.Should().Be("Special: <script>alert('xss')</script>");
    }

    #endregion

    #region Generic Render<T>

    [Fact]
    public void RenderGeneric_WithObject_UsesProperties()
    {
        // Arrange
        var template = "Name: {{Name}}, Age: {{Age}}";
        var context = new { Name = "John", Age = 30 };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Name: John, Age: 30");
    }

    [Fact]
    public void RenderGeneric_NullContext_RendersEmptyVariables()
    {
        // Arrange
        var template = "Hello {{Name}}!";

        // Act
        var result = _engine.Render<object>(template, null!);

        // Assert
        result.Should().Be("Hello !");
    }

    #endregion

    #region HasVariables

    [Fact]
    public void HasVariables_WithVariables_ReturnsTrue()
    {
        // Arrange
        var template = "Hello {{name}}!";

        // Act
        var result = _engine.HasVariables(template);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasVariables_WithoutVariables_ReturnsFalse()
    {
        // Arrange
        var template = "Plain text.";

        // Act
        var result = _engine.HasVariables(template);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasVariables_EmptyTemplate_ReturnsFalse()
    {
        // Act
        var result = _engine.HasVariables(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasVariables_NullTemplate_ReturnsFalse()
    {
        // Act
        var result = _engine.HasVariables(null!);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetVariableNames

    [Fact]
    public void GetVariableNames_SingleVariable_ReturnsName()
    {
        // Arrange
        var template = "Hello {{name}}!";

        // Act
        var result = _engine.GetVariableNames(template);

        // Assert
        result.Should().ContainSingle().Which.Should().Be("name");
    }

    [Fact]
    public void GetVariableNames_MultipleVariables_ReturnsAll()
    {
        // Arrange
        var template = "{{greeting}} {{name}}, count: {{count}}";

        // Act
        var result = _engine.GetVariableNames(template);

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("greeting");
        result.Should().Contain("name");
        result.Should().Contain("count");
    }

    [Fact]
    public void GetVariableNames_NestedVariable_ReturnsRootName()
    {
        // Arrange
        var template = "Value: {{node.output.data}}";

        // Act
        var result = _engine.GetVariableNames(template);

        // Assert
        result.Should().ContainSingle().Which.Should().Be("node");
    }

    [Fact]
    public void GetVariableNames_DuplicateVariable_ReturnsUnique()
    {
        // Arrange
        var template = "{{name}} and {{name}} again";

        // Act
        var result = _engine.GetVariableNames(template);

        // Assert
        result.Should().ContainSingle().Which.Should().Be("name");
    }

    [Fact]
    public void GetVariableNames_NoVariables_ReturnsEmpty()
    {
        // Arrange
        var template = "Plain text.";

        // Act
        var result = _engine.GetVariableNames(template);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetVariableNames_EmptyTemplate_ReturnsEmpty()
    {
        // Act
        var result = _engine.GetVariableNames(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Real-World Playbook Scenarios

    [Fact]
    public void Render_EmailTemplate_SubstitutesPlaybookData()
    {
        // Arrange - Real playbook scenario: email with analysis results
        var template = @"Dear {{recipient.name}},

Your document analysis is complete.

Summary: {{analysis.summary}}
Key parties identified: {{analysis.parties}}

Regards,
{{sender.name}}";

        var context = new Dictionary<string, object?>
        {
            ["recipient"] = new { name = "John Smith" },
            ["analysis"] = new
            {
                summary = "Contract analyzed successfully",
                parties = "Acme Corp, Widget Inc"
            },
            ["sender"] = new { name = "AI Assistant" }
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Contain("Dear John Smith,");
        result.Should().Contain("Summary: Contract analyzed successfully");
        result.Should().Contain("Key parties identified: Acme Corp, Widget Inc");
        result.Should().Contain("Regards,");
        result.Should().Contain("AI Assistant");
    }

    [Fact]
    public void Render_TaskDescription_SubstitutesNodeOutputs()
    {
        // Arrange - Real playbook scenario: create task with node outputs
        var template = "Review document: {{extract_entities.output.documentName}} - {{summarize.output.summary}}";

        var context = new Dictionary<string, object?>
        {
            ["extract_entities"] = new
            {
                output = new { documentName = "Contract_2024.pdf" }
            },
            ["summarize"] = new
            {
                output = new { summary = "5-year service agreement" }
            }
        };

        // Act
        var result = _engine.Render(template, context);

        // Assert
        result.Should().Be("Review document: Contract_2024.pdf - 5-year service agreement");
    }

    #endregion
}
