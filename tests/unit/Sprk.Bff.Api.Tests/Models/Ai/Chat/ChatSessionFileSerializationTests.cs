using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai.Chat;

/// <summary>
/// Unit tests for the additive enrichment of <see cref="ChatSessionFile"/> introduced by
/// chat-routing-redesign-r1 task 071 (stateful-chat-architecture.md §11.2). The 6-Tier
/// Memory Subsystem upload pipeline writes the 8 enriched fields onto every entry of
/// <see cref="ChatSession.UploadedFiles"/>. Cosmos warm-tier + Redis hot-tier round-trip
/// these via <see cref="JsonSerializer"/>; older documents (pre-task-071) lack the
/// enriched fields and MUST deserialize cleanly with sensible defaults.
///
/// These tests are intentionally scoped to <see cref="ChatSessionFile"/> serialization
/// only — broader <see cref="ChatSession"/> round-trip coverage lives in
/// <see cref="ChatSessionSerializationTests"/>.
/// </summary>
public class ChatSessionFileSerializationTests
{
    // Mirrors SessionPersistenceService's serialization options: default System.Text.Json
    // (the production write paths in SessionPersistenceService.WriteToRedisAsync and
    // UpsertToCosmosAsync also use defaults — no custom JsonSerializerOptions instance
    // is wired through ADR-015 D-06 today). If those paths ever adopt custom options
    // (e.g., camelCase or custom converters) they MUST be applied here too.
    private static readonly JsonSerializerOptions DefaultOptions = new();

    // =========================================================================
    // Backward compatibility — pre-task-071 (no enriched fields)
    // =========================================================================

    [Fact]
    public void Deserialize_OldShape_PopulatesNewFieldsWithDefaults()
    {
        // Arrange — exactly the R5 wire shape: 6 positional fields, no enriched fields.
        // This is what's in Cosmos/Redis for sessions that pre-date task 071.
        var oldShapeJson = """
            {
              "FileId": "file-abc",
              "FileName": "contract.pdf",
              "ContentType": "application/pdf",
              "SizeBytes": 12345,
              "SearchDocumentIdsCsv": "doc-1,doc-2",
              "UploadedAt": "2026-06-01T00:00:00+00:00"
            }
            """;

        // Act
        var file = JsonSerializer.Deserialize<ChatSessionFile>(oldShapeJson, DefaultOptions);

        // Assert — original six fields preserved.
        file.Should().NotBeNull();
        file!.FileId.Should().Be("file-abc");
        file.FileName.Should().Be("contract.pdf");
        file.ContentType.Should().Be("application/pdf");
        file.SizeBytes.Should().Be(12345L);
        file.SearchDocumentIdsCsv.Should().Be("doc-1,doc-2");
        file.UploadedAt.Should().Be(DateTimeOffset.Parse("2026-06-01T00:00:00+00:00"));

        // Assert — all 8 enriched fields take their defaults (null for scalars,
        // empty list for collections). NEVER throw NullReferenceException on absent fields.
        file.SummaryText.Should().BeNull("absent enrichment field defaults to null");
        file.ClassifiedDocType.Should().BeNull("absent enrichment field defaults to null");
        file.ClassifiedConfidence.Should().BeNull("absent enrichment field defaults to null");
        file.Sections.Should().NotBeNull("collection default is empty, never null");
        file.Sections.Should().BeEmpty();
        file.TableMetadata.Should().NotBeNull("collection default is empty, never null");
        file.TableMetadata.Should().BeEmpty();
        file.Citations.Should().NotBeNull("collection default is empty, never null");
        file.Citations.Should().BeEmpty();
        file.PageCount.Should().BeNull("absent enrichment field defaults to null");
        // Language intentionally defaults to null (NOT "en") — see ChatSessionFile.Language
        // XML doc: explicit "not yet detected" is distinguishable from confident-en.
        file.Language.Should().BeNull("Language defaults to null per architecture §11.2");
    }

    [Fact]
    public void Deserialize_PartialOldShape_PopulatesAbsentFieldsWithDefaults()
    {
        // Arrange — wire shape missing SOME enriched fields (e.g., classifier ran but
        // summarizer + manifest extractor did not, or the document was upgraded incrementally
        // across deploys). Tests that partial absence is handled cleanly per field.
        var partialJson = """
            {
              "FileId": "file-partial",
              "FileName": "memo.txt",
              "ContentType": "text/plain",
              "SizeBytes": 500,
              "SearchDocumentIdsCsv": "doc-1",
              "UploadedAt": "2026-06-15T00:00:00+00:00",
              "ClassifiedDocType": "memo",
              "ClassifiedConfidence": 0.92
            }
            """;

        // Act
        var file = JsonSerializer.Deserialize<ChatSessionFile>(partialJson, DefaultOptions);

        // Assert — classifier-populated fields survive.
        file.Should().NotBeNull();
        file!.ClassifiedDocType.Should().Be("memo");
        file.ClassifiedConfidence.Should().Be(0.92);

        // Other enrichment fields take defaults.
        file.SummaryText.Should().BeNull("summarizer had not yet run for this document");
        file.Sections.Should().BeEmpty("manifest extractor had not yet run");
        file.TableMetadata.Should().BeEmpty("manifest extractor had not yet run");
        file.Citations.Should().BeEmpty("no recall tool had yet attributed citations");
        file.PageCount.Should().BeNull("manifest extractor had not yet run");
        file.Language.Should().BeNull("language detector had not yet run");
    }

    // =========================================================================
    // Forward compatibility — full enrichment round-trip
    // =========================================================================

    [Fact]
    public void Roundtrip_NewShape_PreservesAllFields()
    {
        // Arrange — fully enriched ChatSessionFile (post-task-071 upload-pipeline output).
        var original = new ChatSessionFile(
            FileId: "file-full",
            FileName: "complex-contract.pdf",
            ContentType: "application/pdf",
            SizeBytes: 250_000L,
            SearchDocumentIdsCsv: "doc-1,doc-2,doc-3,doc-4",
            UploadedAt: DateTimeOffset.Parse("2026-06-22T00:00:00+00:00"))
        {
            SummaryText = "Mutual NDA between Acme Corp and Beta LLC dated 2026-06-01.",
            ClassifiedDocType = "NDA",
            ClassifiedConfidence = 0.97,
            Sections = new[]
            {
                new SectionInfo(
                    Name: "1. Definitions",
                    StartCharOffset: 0,
                    EndCharOffset: 1234,
                    StartPage: 1,
                    EndPage: 2),
                new SectionInfo(
                    Name: "2. Confidential Information",
                    StartCharOffset: 1234,
                    EndCharOffset: 5678,
                    StartPage: 2,
                    EndPage: 5),
                // Section without page metadata — represents non-paginated source path.
                new SectionInfo(
                    Name: "3. Term",
                    StartCharOffset: 5678,
                    EndCharOffset: 6789),
            },
            TableMetadata = new[]
            {
                new TableInfo(
                    Name: "Schedule A — Permitted Recipients",
                    StartCharOffset: 7000,
                    Page: 7),
                // Table without page metadata.
                new TableInfo(
                    Name: "Schedule B",
                    StartCharOffset: 8500),
            },
            Citations = new[]
            {
                new CitationReference(
                    SourceId: "doc-1",
                    Quote: "The Receiving Party shall not disclose...",
                    Page: 3),
                // Structural citation without inline quote.
                new CitationReference(
                    SourceId: "doc-2",
                    Page: 5),
                // Citation with neither quote nor page (purely symbolic).
                new CitationReference(SourceId: "section-4"),
            },
            PageCount = 12,
            Language = "en",
        };

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, DefaultOptions);
        var roundTripped = JsonSerializer.Deserialize<ChatSessionFile>(bytes, DefaultOptions);

        // Assert — original six fields preserved verbatim.
        roundTripped.Should().NotBeNull();
        roundTripped!.FileId.Should().Be(original.FileId);
        roundTripped.FileName.Should().Be(original.FileName);
        roundTripped.ContentType.Should().Be(original.ContentType);
        roundTripped.SizeBytes.Should().Be(original.SizeBytes);
        roundTripped.SearchDocumentIdsCsv.Should().Be(original.SearchDocumentIdsCsv);
        roundTripped.UploadedAt.Should().Be(original.UploadedAt);

        // Assert — scalar enriched fields preserved verbatim.
        roundTripped.SummaryText.Should().Be(original.SummaryText);
        roundTripped.ClassifiedDocType.Should().Be(original.ClassifiedDocType);
        roundTripped.ClassifiedConfidence.Should().Be(original.ClassifiedConfidence);
        roundTripped.PageCount.Should().Be(original.PageCount);
        roundTripped.Language.Should().Be(original.Language);

        // Assert — collection enriched fields preserved in order with all sub-fields.
        roundTripped.Sections.Should().BeEquivalentTo(original.Sections, options => options.WithStrictOrdering());
        roundTripped.TableMetadata.Should().BeEquivalentTo(original.TableMetadata, options => options.WithStrictOrdering());
        roundTripped.Citations.Should().BeEquivalentTo(original.Citations, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Roundtrip_NewShape_WithNullScalarsAndEmptyCollections_PreservesDefaults()
    {
        // Arrange — a "freshly uploaded, not yet enriched" file matches the wire shape
        // that SessionFileEnrichmentService writes BEFORE any enrichment step completes.
        // Should round-trip with all enrichment fields at defaults.
        var fresh = new ChatSessionFile(
            FileId: "file-fresh",
            FileName: "fresh.pdf",
            ContentType: "application/pdf",
            SizeBytes: 1L,
            SearchDocumentIdsCsv: "doc-1",
            UploadedAt: DateTimeOffset.UtcNow);

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(fresh, DefaultOptions);
        var roundTripped = JsonSerializer.Deserialize<ChatSessionFile>(bytes, DefaultOptions);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.SummaryText.Should().BeNull();
        roundTripped.ClassifiedDocType.Should().BeNull();
        roundTripped.ClassifiedConfidence.Should().BeNull();
        roundTripped.Sections.Should().BeEmpty();
        roundTripped.TableMetadata.Should().BeEmpty();
        roundTripped.Citations.Should().BeEmpty();
        roundTripped.PageCount.Should().BeNull();
        roundTripped.Language.Should().BeNull();
    }
}
