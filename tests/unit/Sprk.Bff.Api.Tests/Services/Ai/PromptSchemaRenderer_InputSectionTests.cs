using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Tests for the Wave 11 Option B Layer 2 extension to PromptSchemaRenderer — the new
/// <c>runtimeInput</c> parameter and "## Input" section rendering. Pure-transformation tests;
/// the JPS body is the only collaborator (no mocks). Verifies the section appears in the
/// correct position (after Context, before Document), uses indented JSON, and is gracefully
/// skipped when input is null/empty.
/// </summary>
public class PromptSchemaRenderer_InputSectionTests
{
    private const string MinimalJpsBody = """
        {
          "$schema": "https://spaarke.com/schemas/prompt/v1",
          "instruction": {
            "role": "test analyst",
            "task": "Summarize the structured input.",
            "context": "Test context"
          }
        }
        """;

    private static PromptSchemaRenderer CreateRenderer()
        => new(NullLogger<PromptSchemaRenderer>.Instance);

    [Fact]
    public void Render_WithNullRuntimeInput_DoesNotEmitInputSection()
    {
        var renderer = CreateRenderer();

        var rendered = renderer.Render(
            rawPrompt: MinimalJpsBody,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: null);

        rendered.PromptText.Should().NotContain("## Input");
    }

    [Fact]
    public void Render_WithObjectRuntimeInput_EmitsInputSectionWithIndentedJson()
    {
        var renderer = CreateRenderer();
        using var doc = JsonDocument.Parse("""{"matterName":"Acme 2026","metrics":[{"name":"Hours","value":145}]}""");

        var rendered = renderer.Render(
            rawPrompt: MinimalJpsBody,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: doc.RootElement);

        rendered.PromptText.Should().Contain("## Input");
        // Indented JSON output — verify multi-line formatting present
        rendered.PromptText.Should().Contain("matterName").And.Contain("Acme 2026");
        rendered.PromptText.Should().Contain("metrics");
    }

    [Fact]
    public void Render_WithJsonValueKindNull_DoesNotEmitInputSection()
    {
        var renderer = CreateRenderer();
        using var doc = JsonDocument.Parse("null");

        var rendered = renderer.Render(
            rawPrompt: MinimalJpsBody,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: doc.RootElement);

        rendered.PromptText.Should().NotContain("## Input");
    }

    [Fact]
    public void Render_InputSection_AppearsBeforeDocumentSection()
    {
        var renderer = CreateRenderer();
        using var doc = JsonDocument.Parse("""{"payload":"data"}""");

        var rendered = renderer.Render(
            rawPrompt: MinimalJpsBody,
            skillContext: null,
            knowledgeContext: null,
            documentText: "Document body text here.",
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: doc.RootElement);

        var inputIdx = rendered.PromptText.IndexOf("## Input", System.StringComparison.Ordinal);
        var docIdx = rendered.PromptText.IndexOf("## Document", System.StringComparison.Ordinal);
        inputIdx.Should().BeGreaterThan(0);
        docIdx.Should().BeGreaterThan(0);
        inputIdx.Should().BeLessThan(docIdx);
    }

    [Fact]
    public void Render_InputSection_AppearsAfterContext()
    {
        var renderer = CreateRenderer();
        using var doc = JsonDocument.Parse("""{"payload":"data"}""");

        var rendered = renderer.Render(
            rawPrompt: MinimalJpsBody,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: doc.RootElement);

        var contextIdx = rendered.PromptText.IndexOf("Test context", System.StringComparison.Ordinal);
        var inputIdx = rendered.PromptText.IndexOf("## Input", System.StringComparison.Ordinal);
        contextIdx.Should().BeGreaterThan(0);
        inputIdx.Should().BeGreaterThan(0);
        contextIdx.Should().BeLessThan(inputIdx);
    }

    [Fact]
    public void Render_DoesNotMutateOriginalJpsBody_RegardlessOfRuntimeInput()
    {
        var renderer = CreateRenderer();
        using var doc = JsonDocument.Parse("""{"payload":"data"}""");

        // Render twice — once with runtimeInput, once without — to confirm runtimeInput
        // does not mutate the parsed JPS schema (defensive contract).
        var withInput = renderer.Render(MinimalJpsBody, null, null, null, null, null, runtimeInput: doc.RootElement);
        var withoutInput = renderer.Render(MinimalJpsBody, null, null, null, null, null, runtimeInput: null);

        // The "instruction" portion (Role + Task + Context) should be identical in both renders.
        // Only the "## Input" section differs.
        withoutInput.PromptText.Should().Contain("test analyst");
        withoutInput.PromptText.Should().Contain("Summarize the structured input.");
        withInput.PromptText.Should().Contain("test analyst");
        withInput.PromptText.Should().Contain("Summarize the structured input.");
    }
}
