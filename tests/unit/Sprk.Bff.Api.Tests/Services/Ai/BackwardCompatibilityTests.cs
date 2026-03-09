using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Backward compatibility tests ensuring flat-text (legacy, non-JPS) prompts
/// continue to work unchanged through the PromptSchemaRenderer pipeline.
/// </summary>
/// <remarks>
/// Before JPS, analysis actions stored plain text in sprk_systemprompt.
/// The renderer uses IsJpsFormat() to detect JPS; anything that fails that
/// check must flow through the legacy flat-text path unmodified.
/// </remarks>
public class BackwardCompatibilityTests
{
    private readonly PromptSchemaRenderer _sut;

    public BackwardCompatibilityTests()
    {
        _sut = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());
    }

    // ---------------------------------------------------------------
    // 1. Plain text prompt -> IsJpsFormat returns false (FlatText format)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("You are a helpful assistant. Summarize the document.")]
    [InlineData("Analyze this contract and identify key obligations.")]
    [InlineData("Extract all dates from the following legal document.")]
    public void PlainTextPrompt_DetectedAsFlatText(string prompt)
    {
        var result = _sut.Render(prompt, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
    }

    [Theory]
    [InlineData("You are a helpful assistant. Summarize the document.")]
    [InlineData("Analyze this contract and identify key obligations.")]
    [InlineData("Extract all dates from the following legal document.")]
    public void PlainTextPrompt_ReturnedUnmodified(string prompt)
    {
        var result = _sut.Render(prompt, null, null, null, null, null);

        result.PromptText.Should().Be(prompt);
    }

    // ---------------------------------------------------------------
    // 2. Plain text prompt -> Render() uses legacy path, no JsonSchema
    // ---------------------------------------------------------------

    [Fact]
    public void PlainTextPrompt_HasNoJsonSchemaOrSchemaName()
    {
        const string prompt = "Classify this document as either commercial or residential.";

        var result = _sut.Render(prompt, null, null, null, null, null);

        result.JsonSchema.Should().BeNull();
        result.SchemaName.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // 3. HTML content -> not detected as JPS
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("<html><body><p>Analyze this document.</p></body></html>")]
    [InlineData("<h1>Contract Review</h1><p>Check all clauses for compliance.</p>")]
    [InlineData("<!DOCTYPE html><html><head><title>Prompt</title></head><body>Summarize.</body></html>")]
    public void HtmlContent_TreatedAsFlatText(string htmlPrompt)
    {
        var result = _sut.Render(htmlPrompt, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(htmlPrompt);
    }

    // ---------------------------------------------------------------
    // 4. JSON that's NOT JPS (no $schema) -> not detected as JPS
    // ---------------------------------------------------------------

    [Fact]
    public void JsonWithoutSchemaKey_TreatedAsFlatText()
    {
        const string jsonNoSchema = """{ "instruction": { "task": "Summarize the document" }, "version": 1 }""";

        var result = _sut.Render(jsonNoSchema, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(jsonNoSchema);
    }

    [Fact]
    public void JsonArrayWithoutSchemaKey_TreatedAsFlatText()
    {
        const string jsonArray = """[{ "role": "system", "content": "You are a helpful assistant." }]""";

        var result = _sut.Render(jsonArray, null, null, null, null, null);

        // Starts with '[', not '{', so cannot be JPS
        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(jsonArray);
    }

    [Fact]
    public void JsonWithDifferentSchemaProperty_TreatedAsFlatText()
    {
        // Has a "schema" key but not "$schema" — should not trigger JPS
        const string jsonWrongKey = """{ "schema": "https://example.com/some-schema", "task": "Summarize" }""";

        var result = _sut.Render(jsonWrongKey, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(jsonWrongKey);
    }

    // ---------------------------------------------------------------
    // 5. Empty/null prompt -> handled gracefully
    // ---------------------------------------------------------------

    [Fact]
    public void NullPrompt_ReturnsEmptyFlatText()
    {
        var result = _sut.Render(null, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().BeEmpty();
        result.JsonSchema.Should().BeNull();
        result.SchemaName.Should().BeNull();
    }

    [Fact]
    public void EmptyStringPrompt_ReturnsEmptyFlatText()
    {
        var result = _sut.Render(string.Empty, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // 6. Whitespace-padded text -> not detected as JPS
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("   Summarize the document.   ")]
    [InlineData("\t\tAnalyze this contract.\t\t")]
    [InlineData("\n\nClassify the content.\n\n")]
    public void WhitespacePaddedPlainText_TreatedAsFlatText(string paddedPrompt)
    {
        var result = _sut.Render(paddedPrompt, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(paddedPrompt);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("  \r\n  ")]
    public void WhitespaceOnlyPrompt_ReturnsEmptyFlatText(string whitespace)
    {
        var result = _sut.Render(whitespace, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // 7. Legacy prompt with handler-specific context -> context ignored
    //    by renderer (stays in handler's BuildExecutionPrompt)
    // ---------------------------------------------------------------

    [Fact]
    public void LegacyPromptWithSkillContext_RendererReturnsPromptOnly()
    {
        // The renderer's flat-text path returns rawPrompt as-is.
        // Skill and knowledge context injection is the handler's responsibility
        // (via BuildExecutionPrompt) for legacy prompts.
        const string legacyPrompt = "Analyze the document and extract key terms.";
        const string skillContext = "Focus on indemnification clauses and liability caps.";
        const string knowledgeContext = "Standard contract terms reference material.";

        var result = _sut.Render(legacyPrompt, skillContext, knowledgeContext, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        // Legacy path returns the raw prompt text only; handler appends context later
        result.PromptText.Should().Be(legacyPrompt);
    }

    [Fact]
    public void LegacyPromptWithDocumentText_RendererReturnsPromptOnly()
    {
        const string legacyPrompt = "Review the following contract:\n\n{document}";
        const string documentText = "This Software License Agreement is entered into...";

        var result = _sut.Render(legacyPrompt, null, null, documentText, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        // Legacy path does NOT substitute {document} — that happens in handler
        result.PromptText.Should().Be(legacyPrompt);
        result.PromptText.Should().Contain("{document}");
    }

    [Fact]
    public void LegacyPromptWithTemplateParameters_RendererReturnsPromptOnly()
    {
        const string legacyPrompt = "Analyze obligations for {parameters} in this contract.";
        var templateParams = new Dictionary<string, object?>
        {
            { "jurisdiction", "California" },
            { "partyName", "Acme Corp" }
        };

        var result = _sut.Render(legacyPrompt, null, null, null, templateParams, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        // Template parameters are not substituted by the renderer for legacy prompts
        result.PromptText.Should().Be(legacyPrompt);
    }

    [Fact]
    public void LegacyPromptWithAllContextParameters_RendererReturnsPromptOnly()
    {
        // Comprehensive test: all optional parameters populated, still flat-text path
        const string legacyPrompt = "You are a legal analyst. Identify all clause types in {document}.";
        const string skillContext = "Skill fragment: focus on termination clauses.";
        const string knowledgeContext = "Knowledge: standard NDA terms.";
        const string documentText = "This Non-Disclosure Agreement...";
        var templateParams = new Dictionary<string, object?> { { "contractType", "NDA" } };

        var result = _sut.Render(
            legacyPrompt,
            skillContext,
            knowledgeContext,
            documentText,
            templateParams,
            downstreamNodes: null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(legacyPrompt);
        result.JsonSchema.Should().BeNull();
        result.SchemaName.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // Edge cases: prompts that look "JSON-ish" but are not JPS
    // ---------------------------------------------------------------

    [Fact]
    public void TextContainingSchemaWord_NotDetectedAsJps()
    {
        // Contains the word "$schema" but does not start with '{'
        const string prompt = """The "$schema" property is used in JSON Schema to identify the schema version.""";

        var result = _sut.Render(prompt, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(prompt);
    }

    [Fact]
    public void MultilinePromptWithCurlyBraces_NotDetectedAsJps()
    {
        // Starts with plain text, has curly braces later (template placeholders)
        const string prompt = """
            You are a contract analyst.
            Extract the following fields: {document_type}, {effective_date}, {parties}.
            Return results as JSON.
            """;

        var result = _sut.Render(prompt, null, null, null, null, null);

        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(prompt);
    }

    [Fact]
    public void PromptWithEmbeddedJsonExample_NotDetectedAsJps()
    {
        // Plain text prompt that contains a JSON example mid-text
        const string prompt = """
            Analyze the document and return results.
            Format your response as: { "summary": "...", "confidence": 0.9 }
            Do not include any other text.
            """;

        var result = _sut.Render(prompt, null, null, null, null, null);

        // Does not start with '{' so should not be detected as JPS
        result.Format.Should().Be(PromptFormat.FlatText);
        result.PromptText.Should().Be(prompt);
    }
}
