using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PlaybookEmbedding;

/// <summary>
/// FR-13 (chat-routing-redesign-r1 task 034) — <see cref="PlaybookEmbeddingHashCalculator"/>
/// MUST produce a deterministic, content-derived hash that matches the indexer's
/// composition. These tests pin the contract so the drift-detection job can never
/// disagree with the indexer about what the canonical embed-input is.
/// </summary>
public class PlaybookEmbeddingHashCalculatorTests
{
    private static PlaybookEmbeddingDocument BaselineDocument(string? jpsJson = null) => new()
    {
        Id = "pb-1",
        PlaybookId = "pb-1",
        PlaybookName = "Test Playbook",
        Description = "Test playbook description.",
        TriggerPhrases = new List<string> { "trigger one", "trigger two" },
        Tags = new List<string> { "tag-a", "tag-b" },
        JpsMatchingMetadata = jpsJson
    };

    [Fact]
    public void ComputeHash_ReturnsDeterministicValue_ForIdenticalInput()
    {
        // Arrange
        var calculator = new PlaybookEmbeddingHashCalculator();
        var doc1 = BaselineDocument();
        var doc2 = BaselineDocument();

        // Act
        var hash1 = calculator.ComputeHash(doc1);
        var hash2 = calculator.ComputeHash(doc2);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsDifferentValue_WhenDescriptionChanges()
    {
        // Arrange
        var calculator = new PlaybookEmbeddingHashCalculator();
        var doc1 = BaselineDocument();
        var doc2 = BaselineDocument();
        doc2.Description = "Different description.";

        // Act
        var hash1 = calculator.ComputeHash(doc1);
        var hash2 = calculator.ComputeHash(doc2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsDifferentValue_WhenJpsMetadataChanges()
    {
        // Arrange — FR-10 invariant: changes to sprk_jps_matching_metadata MUST be
        // visible to the hash so drift detection catches them.
        var calculator = new PlaybookEmbeddingHashCalculator();
        var doc1 = BaselineDocument(jpsJson: null);
        var doc2 = BaselineDocument(jpsJson: """{"documentTypes":["NDA"]}""");

        // Act
        var hash1 = calculator.ComputeHash(doc1);
        var hash2 = calculator.ComputeHash(doc2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHex_WithExpectedLength()
    {
        // Arrange
        var calculator = new PlaybookEmbeddingHashCalculator();
        var doc = BaselineDocument();

        // Act
        var hash = calculator.ComputeHash(doc);

        // Assert — SHA-256 hex is 64 chars (fits Dataverse sprk_indexhash NVARCHAR(100)).
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_Throws_WhenDocumentIsNull()
    {
        // Arrange
        var calculator = new PlaybookEmbeddingHashCalculator();

        // Act
        var act = () => calculator.ComputeHash(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeHash_IsIgnorantOf_VectorAndIdentifierFields()
    {
        // Arrange — only content fields drive the hash. Vector + id changes MUST NOT
        // alter the hash (otherwise the drift job would flip every row on every
        // re-embed even when content is unchanged).
        var calculator = new PlaybookEmbeddingHashCalculator();
        var doc1 = BaselineDocument();
        var doc2 = BaselineDocument();
        doc2.Id = "pb-different-id";
        doc2.PlaybookId = "pb-different-id";
        doc2.ContentVector3072 = new ReadOnlyMemory<float>(new float[] { 1.0f, 2.0f, 3.0f });

        // Act
        var hash1 = calculator.ComputeHash(doc1);
        var hash2 = calculator.ComputeHash(doc2);

        // Assert
        hash1.Should().Be(hash2);
    }
}
