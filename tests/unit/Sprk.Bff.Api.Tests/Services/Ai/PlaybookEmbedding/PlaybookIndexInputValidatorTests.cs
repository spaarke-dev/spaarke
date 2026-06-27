using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PlaybookEmbedding;

/// <summary>
/// FR-12 (chat-routing-redesign-r1 task 036) — <see cref="PlaybookIndexInputValidator"/>
/// MUST report missing required fields (<c>description</c>, <c>documentTypes</c>,
/// <c>destinationHint</c>) so the playbook embedding trigger endpoint can return
/// 400 ProblemDetails with the <c>missingFields</c> extension.
/// </summary>
public class PlaybookIndexInputValidatorTests
{
    private const string FullValidJson = """
    {
      "documentTypes": ["NDA", "Contract"],
      "intents": ["summarize"],
      "triggerPhrases": ["summarize this NDA"],
      "outputDestination": "chat"
    }
    """;

    private static PlaybookResponse BuildPlaybook(
        string? description = "Test playbook description",
        string? jpsJson = FullValidJson) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Test Playbook",
            Description = description,
            JpsMatchingMetadata = jpsJson,
        };

    [Fact]
    public void Validate_ReturnsEmptyMissingFields_WhenAllRequiredFieldsPresent()
    {
        // Arrange
        var sut = new PlaybookIndexInputValidator();
        var playbook = BuildPlaybook();

        // Act
        var missing = sut.Validate(playbook);

        // Assert
        missing.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReportsDescription_WhenDescriptionIsNullOrWhitespace()
    {
        // Arrange
        var sut = new PlaybookIndexInputValidator();
        var playbook = BuildPlaybook(description: "   ");

        // Act
        var missing = sut.Validate(playbook);

        // Assert — only description missing; other two satisfied by FullValidJson
        missing.Should().ContainSingle().Which.Should().Be("description");
    }

    [Fact]
    public void Validate_ReportsDocumentTypes_WhenJpsMetadataMissingDocumentTypes()
    {
        // Arrange — well-formed JSON but documentTypes is omitted
        const string jsonMissingDocumentTypes = """
        {
          "intents": ["summarize"],
          "outputDestination": "chat"
        }
        """;
        var sut = new PlaybookIndexInputValidator();
        var playbook = BuildPlaybook(jpsJson: jsonMissingDocumentTypes);

        // Act
        var missing = sut.Validate(playbook);

        // Assert
        missing.Should().ContainSingle().Which.Should().Be("documentTypes");
    }

    [Fact]
    public void Validate_ReportsDestinationHint_WhenJpsMetadataMissingOutputDestination()
    {
        // Arrange — well-formed JSON with documentTypes but no outputDestination
        const string jsonMissingOutputDestination = """
        {
          "documentTypes": ["NDA"],
          "intents": ["summarize"]
        }
        """;
        var sut = new PlaybookIndexInputValidator();
        var playbook = BuildPlaybook(jpsJson: jsonMissingOutputDestination);

        // Act
        var missing = sut.Validate(playbook);

        // Assert
        missing.Should().ContainSingle().Which.Should().Be("destinationHint");
    }

    [Fact]
    public void Validate_ReportsAllThree_WhenAllAreMissingOrJsonIsMalformed()
    {
        // Arrange — description blank + JSON is malformed (treated as missing per FR-12)
        const string malformedJson = "{ this is not valid json";
        var sut = new PlaybookIndexInputValidator();
        var playbook = BuildPlaybook(description: null, jpsJson: malformedJson);

        // Act
        var missing = sut.Validate(playbook);

        // Assert — stable order: description, documentTypes, destinationHint
        missing.Should().Equal(new[] { "description", "documentTypes", "destinationHint" });
    }
}
