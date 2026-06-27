using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PlaybookEmbedding;

/// <summary>
/// FR-10 (chat-routing-redesign-r1 task 032) — `PlaybookEmbeddingService.ComposeContentText`
/// must include `documentTypes + intents + triggerPhrases` from `sprk_jps_matching_metadata`
/// when present and well-formed, and tolerantly fall back to the baseline 4-source
/// composition on null / missing / malformed JSON.
/// </summary>
public class PlaybookEmbeddingServiceComposeContentTextTests
{
    private static PlaybookEmbeddingDocument BaselineDocument(string? jpsJson = null) => new()
    {
        Id = "pb-1",
        PlaybookName = "Test Playbook",
        Description = "Test playbook description.",
        TriggerPhrases = new List<string> { "trigger one", "trigger two" },
        Tags = new List<string> { "tag-a", "tag-b" },
        JpsMatchingMetadata = jpsJson
    };

    [Fact]
    public void ComposeContentText_ReturnsBaselineComposition_WhenJpsMetadataIsNull()
    {
        // Arrange
        var document = BaselineDocument(jpsJson: null);

        // Act
        var content = PlaybookEmbeddingService.ComposeContentText(document);

        // Assert
        content.Should().Contain("Test Playbook");
        content.Should().Contain("Test playbook description.");
        content.Should().Contain("trigger one | trigger two");
        content.Should().Contain("tag-a, tag-b");
        content.Should().NotContain("nda"); // No JPS additions
        content.Split('\n').Should().HaveCount(4, "baseline composition has 4 sections");
    }

    [Fact]
    public void ComposeContentText_AppendsJpsSections_WhenJpsMetadataIsFullyPopulated()
    {
        // Arrange
        const string fullJson = """
        {
          "documentTypes": ["NDA", "Contract"],
          "intents": ["summarize", "review"],
          "triggerPhrases": ["summarize this NDA", "review confidentiality terms"]
        }
        """;
        var document = BaselineDocument(jpsJson: fullJson);

        // Act
        var content = PlaybookEmbeddingService.ComposeContentText(document);

        // Assert — all JPS values surface in the embed input
        content.Should().Contain("NDA, Contract");
        content.Should().Contain("summarize, review");
        content.Should().Contain("summarize this NDA | review confidentiality terms");
        content.Split('\n').Should().HaveCount(7, "4 baseline sections + 3 JPS sections");

        // Deterministic ordering: documentTypes → intents → triggerPhrases (per FR-10)
        var docTypesIdx = content.IndexOf("NDA, Contract", StringComparison.Ordinal);
        var intentsIdx = content.IndexOf("summarize, review", StringComparison.Ordinal);
        var jpsPhrasesIdx = content.IndexOf("summarize this NDA", StringComparison.Ordinal);
        docTypesIdx.Should().BeLessThan(intentsIdx);
        intentsIdx.Should().BeLessThan(jpsPhrasesIdx);
    }

    [Fact]
    public void ComposeContentText_FallsBackToBaseline_WhenJpsMetadataIsMalformedJson()
    {
        // Arrange — malformed JSON (truncated object)
        const string malformed = """{ "documentTypes": [ "NDA" """;
        var document = BaselineDocument(jpsJson: malformed);

        // Act
        var content = PlaybookEmbeddingService.ComposeContentText(document);

        // Assert — baseline composition only, NO exception, NO JPS values appended
        content.Should().Contain("Test Playbook");
        content.Should().Contain("trigger one | trigger two");
        content.Should().Contain("tag-a, tag-b");
        content.Should().NotContain("NDA", "malformed JSON must NOT leak its content into embed input");
        content.Split('\n').Should().HaveCount(4, "fallback to 4-section baseline composition");
    }

    [Fact]
    public void ComposeContentText_AppendsOnlyPresentSections_WhenJpsMetadataIsPartial()
    {
        // Arrange — only documentTypes populated; intents + triggerPhrases absent
        const string partialJson = """
        {
          "documentTypes": ["NDA"]
        }
        """;
        var document = BaselineDocument(jpsJson: partialJson);

        // Act
        var content = PlaybookEmbeddingService.ComposeContentText(document);

        // Assert — exactly one extra section (documentTypes); no blank-line padding for absent sections
        content.Should().Contain("NDA");
        content.Split('\n').Should().HaveCount(5, "4 baseline sections + 1 JPS section (documentTypes only)");
    }
}
