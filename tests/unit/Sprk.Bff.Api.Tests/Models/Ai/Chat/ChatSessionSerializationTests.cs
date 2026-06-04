using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="ChatSession"/> + <see cref="ChatSessionFile"/> covering the
/// R5 manifest extension (task 004 / D1-04). The manifest must round-trip cleanly through
/// the existing triple-tier persistence:
///   - Redis hot tier (<see cref="JsonSerializer"/> via
///     <c>ChatSessionManager.CacheSessionAsync</c>).
///   - Cosmos warm tier (<c>ISessionPersistenceService</c> write-through; mapping
///     intentionally cherry-picks fields — verified separately by integration tests).
///   - Dataverse cold-tier audit (<c>IChatDataverseRepository</c>; cherry-picks audit
///     columns — verified separately).
/// These tests prove the Redis JSON path (which is the LOAD-bearing tier per ADR-009)
/// preserves the manifest faithfully and that backward compatibility holds.
/// </summary>
public class ChatSessionSerializationTests
{
    private static readonly JsonSerializerOptions DefaultOptions = new();

    // === Backward compatibility: pre-R5 sessions ===

    [Fact]
    public void ChatSession_WithoutUploadedFiles_RoundTripsAsNullManifest()
    {
        // Arrange — exactly how a pre-R5 caller constructs ChatSession (no UploadedFiles arg).
        var session = new ChatSession(
            SessionId: "session-abc",
            TenantId: "tenant-1",
            DocumentId: null,
            PlaybookId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>());

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session, DefaultOptions);
        var roundTripped = JsonSerializer.Deserialize<ChatSession>(bytes, DefaultOptions);

        // Assert — default null preserved; existing properties survive untouched.
        roundTripped.Should().NotBeNull();
        roundTripped!.UploadedFiles.Should().BeNull("pre-R5 default is null manifest");
        roundTripped.SessionId.Should().Be(session.SessionId);
        roundTripped.TenantId.Should().Be(session.TenantId);
        roundTripped.PlaybookId.Should().Be(session.PlaybookId);
        roundTripped.AdditionalDocumentIds.Should().BeNull();
    }

    [Fact]
    public void ChatSession_WithEmptyUploadedFiles_RoundTripsAsEmptyManifest()
    {
        // Arrange
        var session = new ChatSession(
            SessionId: "session-empty",
            TenantId: "tenant-1",
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: Array.Empty<ChatSessionFile>());

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session, DefaultOptions);
        var roundTripped = JsonSerializer.Deserialize<ChatSession>(bytes, DefaultOptions);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.UploadedFiles.Should().NotBeNull();
        roundTripped.UploadedFiles!.Should().BeEmpty();
    }

    // === Full manifest at the 20-file cap ===

    [Fact]
    public void ChatSession_WithMaxUploadedFiles_RoundTripsAllSixFields()
    {
        // Arrange — populate the manifest at the hard cap (20 entries).
        var files = Enumerable.Range(0, ChatSession.MaxUploadedFiles)
            .Select(i => new ChatSessionFile(
                FileId: $"file-{i:D2}",
                FileName: $"contract-{i:D2}.pdf",
                ContentType: "application/pdf",
                SizeBytes: 1024L * (i + 1),
                // Multi-ID CSV for non-trivial entries; single ID (no comma) for the first
                // — represents the <500-token single-chunk skip-chunking path per NFR-02.
                SearchDocumentIdsCsv: i == 0
                    ? $"doc-{i:D2}-chunk-0"
                    : string.Join(',', Enumerable.Range(0, 3).Select(c => $"doc-{i:D2}-chunk-{c}")),
                UploadedAt: DateTimeOffset.UtcNow.AddMinutes(-i)))
            .ToList();

        var session = new ChatSession(
            SessionId: "session-full",
            TenantId: "tenant-1",
            DocumentId: "doc-anchor",
            PlaybookId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: new[] { "pinned-1", "pinned-2" },
            UploadedFiles: files);

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session, DefaultOptions);
        var roundTripped = JsonSerializer.Deserialize<ChatSession>(bytes, DefaultOptions);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.UploadedFiles.Should().NotBeNull();
        roundTripped.UploadedFiles!.Should().HaveCount(ChatSession.MaxUploadedFiles);

        // All six fields preserved on every entry (binding shape per design.md §4.4).
        var rtFiles = roundTripped.UploadedFiles!;
        for (var i = 0; i < ChatSession.MaxUploadedFiles; i++)
        {
            var actual = rtFiles[i];
            var expected = files[i];

            actual.FileId.Should().Be(expected.FileId);
            actual.FileName.Should().Be(expected.FileName);
            actual.ContentType.Should().Be(expected.ContentType);
            actual.SizeBytes.Should().Be(expected.SizeBytes);
            actual.SearchDocumentIdsCsv.Should().Be(expected.SearchDocumentIdsCsv);
            actual.UploadedAt.Should().Be(expected.UploadedAt);
        }

        // The first entry's CSV has no comma (single-chunk skip-chunking path).
        rtFiles[0].SearchDocumentIdsCsv.Should().NotContain(",");
        // Other entries' CSVs contain commas (multi-chunk path).
        rtFiles[5].SearchDocumentIdsCsv.Should().Contain(",");

        // Sibling property (AdditionalDocumentIds) also survives — both coexist (design.md §4.4 vs R3).
        roundTripped.AdditionalDocumentIds.Should().BeEquivalentTo(new[] { "pinned-1", "pinned-2" });
    }

    // === Const + regression guards ===

    [Fact]
    public void MaxUploadedFiles_IsTwenty_PerSpecNFR02()
    {
        // Spec NFR-02 + project CLAUDE.md §3.8 — hard cap is 20.
        // This test guards against drift from the binding source of truth.
        ChatSession.MaxUploadedFiles.Should().Be(20);
    }

    [Fact]
    public void ChatSessionFile_HasExactlySixFields_PerDesignMdSection44()
    {
        // Reflection-based regression guard: design.md §4.4 binds the shape to exactly
        // six fields. If somebody adds a seventh field (or removes one), this test breaks
        // — forcing them to update design.md §4.4 and spec NFR-02 first.
        var properties = typeof(ChatSessionFile).GetProperties();
        properties.Should().HaveCount(6, "design.md §4.4 binds ChatSessionFile to six fields");

        var names = properties.Select(p => p.Name).ToHashSet();
        names.Should().Contain(new[]
        {
            nameof(ChatSessionFile.FileId),
            nameof(ChatSessionFile.FileName),
            nameof(ChatSessionFile.ContentType),
            nameof(ChatSessionFile.SizeBytes),
            nameof(ChatSessionFile.SearchDocumentIdsCsv),
            nameof(ChatSessionFile.UploadedAt),
        });
    }

    // === Wire-compat: pre-R5 JSON deserializes ===

    [Fact]
    public void ChatSession_DeserializesPreR5Json_WithoutUploadedFilesField()
    {
        // Arrange — simulate a JSON payload written by a pre-R5 BFF (no UploadedFiles
        // field on the wire). This is exactly what's in Redis for already-running
        // production sessions at the moment R5 deploys.
        var preR5Json = """
            {
              "SessionId": "legacy-session",
              "TenantId": "tenant-legacy",
              "DocumentId": null,
              "PlaybookId": null,
              "CreatedAt": "2026-06-01T00:00:00+00:00",
              "LastActivity": "2026-06-01T01:00:00+00:00",
              "Messages": [],
              "HostContext": null,
              "AdditionalDocumentIds": null
            }
            """;

        // Act
        var session = JsonSerializer.Deserialize<ChatSession>(preR5Json, DefaultOptions);

        // Assert — manifest defaults to null; existing properties survive.
        session.Should().NotBeNull();
        session!.SessionId.Should().Be("legacy-session");
        session.UploadedFiles.Should().BeNull("absent field on the wire is semantically 'no files uploaded'");
    }
}
