using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// R7 Wave 11 T116 debug — focused tests exercising the EXACT helper invocations that
/// the deployed DAILY-BRIEFING-NARRATE allowList template uses, so we can see what the
/// engine actually produces without a deploy cycle.
/// </summary>
public class TemplateEngine_Wave11Debug
{
    private readonly TemplateEngine _engine;
    private readonly ITestOutputHelper _output;

    public TemplateEngine_Wave11Debug(ITestOutputHelper output)
    {
        _engine = new TemplateEngine(NullLogger<TemplateEngine>.Instance);
        _output = output;
    }

    [Fact]
    public void Debug_JsonOnDirectList_ProducesValidJsonArray()
    {
        var ctx = new Dictionary<string, object?>
        {
            ["names"] = new List<object?> { "Alice", "Bob", "Charlie" }
        };
        var rendered = _engine.Render("{{json names}}", ctx);
        _output.WriteLine($"OUTPUT: |{rendered}|");
        // Direct json over a list — should produce JSON array text
        rendered.Should().StartWith("[").And.EndWith("]");
        rendered.Should().Contain("\"Alice\"").And.Contain("\"Bob\"").And.Contain("\"Charlie\"");
    }

    [Fact]
    public void Debug_JsonOverDistinct_ProducesValidJsonArray()
    {
        var ctx = new Dictionary<string, object?>
        {
            ["names"] = new List<object?> { "Alice", "Bob", "Alice" }
        };
        var rendered = _engine.Render("{{json (distinct names)}}", ctx);
        _output.WriteLine($"OUTPUT: |{rendered}|");
        rendered.Should().StartWith("[").And.EndWith("]");
        rendered.Should().Contain("\"Alice\"").And.Contain("\"Bob\"");
    }

    [Fact]
    public void Debug_JsonOverDistinctConcatMap_ProducesValidJsonArray()
    {
        // Mirrors the simplified shape of DAILY-BRIEFING-NARRATE allowList
        var ctx = new Dictionary<string, object?>
        {
            ["priorityItems"] = new List<object?>
            {
                new Dictionary<string, object?> { ["regardingName"] = "Smith Matter 2026" },
                new Dictionary<string, object?> { ["regardingName"] = "Acme Litigation" }
            },
            ["start"] = new Dictionary<string, object?>
            {
                ["categories"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["items"] = new List<object?>
                        {
                            new Dictionary<string, object?> { ["regardingName"] = "Beta Corp" }
                        }
                    }
                }
            }
        };
        var template = "{{json (distinct (concat (map priorityItems 'regardingName') (flatMap start.categories 'items.regardingName')))}}";
        var rendered = _engine.Render(template, ctx);
        _output.WriteLine($"OUTPUT: |{rendered}|");
        rendered.Should().StartWith("[").And.EndWith("]");
        rendered.Should().Contain("Smith Matter 2026");
        rendered.Should().Contain("Acme Litigation");
        rendered.Should().Contain("Beta Corp");
    }

    [Fact]
    public void Debug_PromptSchemaRenderer_FullBriefNarrateTldrPrompt()
    {
        // Use the actual BRIEF-NARRATE-TLDR JPS body shape + a sample runtimeInput
        // to inspect what the LLM would actually see.
        var jpsBody = """
{
  "$schema": "https://spaarke.com/schemas/prompt/v1",
  "$version": 1,
  "instruction": {
    "role": "notification summarizer",
    "task": "Generate a TL;DR summary of the user's daily notification briefing. Read the structured input payload (categories, priorityItems, channels, totalNotificationCount) and produce: a one-sentence overall summary, 2-4 short key-takeaway bullets, one suggested top action, plus the integer counts.",
    "constraints": [
      "Use ONLY entity names present in the provided input.",
      "Keep summary to a single sentence (max 30 words)."
    ]
  },
  "output": {
    "fields": [
      { "name": "summary", "type": "string" },
      { "name": "keyTakeaways", "type": "array" }
    ]
  }
}
""";

        var renderer = new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance);
        using var runtimeInputDoc = System.Text.Json.JsonDocument.Parse("""
{
  "briefing": {
    "categories": [{"name":"Tasks Due Soon","count":3}],
    "priorityItems": [{"category":"Tasks","title":"Smith Filing"}],
    "totalNotificationCount": 5
  }
}
""");
        var rendered = renderer.Render(
            rawPrompt: jpsBody,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: null,
            downstreamNodes: null,
            runtimeInput: runtimeInputDoc.RootElement);

        _output.WriteLine($"=== RENDERED PROMPT ({rendered.PromptText.Length} chars) ===");
        _output.WriteLine(rendered.PromptText);
        _output.WriteLine("=== END ===");

        rendered.PromptText.Should().Contain("notification summarizer");
        rendered.PromptText.Should().Contain("Generate a TL;DR summary");
        rendered.PromptText.Should().Contain("## Input");
        rendered.PromptText.Should().Contain("Smith Filing");
    }

    [Fact]
    public void Debug_JsonOnDictionary_ProducesValidJsonObject()
    {
        var ctx = new Dictionary<string, object?>
        {
            ["tldrResult"] = new Dictionary<string, object?>
            {
                ["summary"] = "5 notifications today",
                ["keyTakeaways"] = new List<object?> { "takeaway one", "takeaway two" }
            }
        };
        var rendered = _engine.Render("{{json tldrResult}}", ctx);
        _output.WriteLine($"OUTPUT: |{rendered}|");
        rendered.Should().StartWith("{").And.EndWith("}");
        rendered.Should().Contain("\"summary\"").And.Contain("5 notifications today");
    }
}
