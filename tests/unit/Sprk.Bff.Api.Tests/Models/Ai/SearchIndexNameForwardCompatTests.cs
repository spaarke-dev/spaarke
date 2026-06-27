using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// Forward-compat regression tests for the <c>SearchIndexName</c> property added to the
/// three BFF search request DTOs (FR-BFF-05, NFR-02 of the
/// <c>spaarke-multi-container-multi-index-r1</c> project).
///
/// Invariant under test:
/// <list type="bullet">
/// <item>Existing JSON payloads that DO NOT carry <c>searchIndexName</c> MUST continue
/// to deserialize with the property set to <c>null</c> (existing callers keep working —
/// NFR-02).</item>
/// <item>New JSON payloads that DO carry <c>searchIndexName</c> MUST deserialize with
/// the property populated (FR-BFF-05).</item>
/// <item>Serialization is round-trippable — <c>null</c> stays <c>null</c>;
/// a value stays the same value.</item>
/// </list>
///
/// JSON options mirror ASP.NET Core's <see cref="JsonSerializerDefaults.Web"/> — the
/// implicit default Minimal API uses when <c>Program.cs</c> registers no override (verified
/// 2026-06-07: no <c>ConfigureHttpJsonOptions</c> / <c>JsonSerializerOptions</c> in
/// <c>Program.cs</c>). camelCase, case-insensitive.
/// </summary>
public class SearchIndexNameForwardCompatTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    // ====== SemanticSearchRequest ======

    [Fact]
    public void SemanticSearchRequest_JsonWithoutSearchIndexName_DeserializesWithNull()
    {
        // Arrange — exact shape of a pre-FR-BFF-05 PCF / code-page caller.
        const string json = """
            {
              "query": "contract termination",
              "scope": "all"
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<SemanticSearchRequest>(json, WebOptions);

        // Assert — forward-compat: missing field → null; existing properties survive.
        dto.Should().NotBeNull();
        dto!.SearchIndexName.Should().BeNull("requests without searchIndexName must continue to work (NFR-02)");
        dto.Query.Should().Be("contract termination");
        dto.Scope.Should().Be(SearchScope.All);
    }

    [Fact]
    public void SemanticSearchRequest_JsonWithSearchIndexName_DeserializesProperty()
    {
        // Arrange — new caller threads an explicit index name.
        const string json = """
            {
              "query": "force majeure",
              "scope": "entity",
              "entityType": "matter",
              "entityId": "00000000-0000-0000-0000-000000000123",
              "searchIndexName": "spaarke-file-index"
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<SemanticSearchRequest>(json, WebOptions);

        // Assert
        dto.Should().NotBeNull();
        dto!.SearchIndexName.Should().Be("spaarke-file-index", "FR-BFF-05 — explicit index threaded through DTO");
        dto.Query.Should().Be("force majeure");
        dto.Scope.Should().Be("entity");
        dto.EntityType.Should().Be("matter");
    }

    [Fact]
    public void SemanticSearchRequest_RoundTripsSearchIndexNameValue()
    {
        // Arrange
        var original = new SemanticSearchRequest
        {
            Query = "round-trip",
            Scope = SearchScope.All,
            SearchIndexName = "discovery-index"
        };

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, WebOptions);
        var roundTripped = JsonSerializer.Deserialize<SemanticSearchRequest>(bytes, WebOptions);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.SearchIndexName.Should().Be("discovery-index");
        roundTripped.Query.Should().Be("round-trip");
    }

    [Fact]
    public void SemanticSearchRequest_RoundTripsNullSearchIndexName()
    {
        // Arrange
        var original = new SemanticSearchRequest
        {
            Query = "no-index",
            Scope = SearchScope.All
            // SearchIndexName intentionally omitted — default null
        };

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, WebOptions);
        var roundTripped = JsonSerializer.Deserialize<SemanticSearchRequest>(bytes, WebOptions);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.SearchIndexName.Should().BeNull();
    }

    // ====== RecordSearchRequest ======

    [Fact]
    public void RecordSearchRequest_JsonWithoutSearchIndexName_DeserializesWithNull()
    {
        // Arrange — pre-FR-BFF-05 record-search caller.
        const string json = """
            {
              "query": "Acme Corp",
              "recordTypes": ["sprk_matter"]
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<RecordSearchRequest>(json, WebOptions);

        // Assert
        dto.Should().NotBeNull();
        dto!.SearchIndexName.Should().BeNull("requests without searchIndexName must continue to work (NFR-02)");
        dto.Query.Should().Be("Acme Corp");
        dto.RecordTypes.Should().ContainSingle().Which.Should().Be("sprk_matter");
    }

    [Fact]
    public void RecordSearchRequest_JsonWithSearchIndexName_DeserializesProperty()
    {
        // Arrange
        const string json = """
            {
              "query": "Acme Corp",
              "recordTypes": ["sprk_matter", "sprk_project"],
              "searchIndexName": "spaarke-records-index-tenant-b"
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<RecordSearchRequest>(json, WebOptions);

        // Assert
        dto.Should().NotBeNull();
        dto!.SearchIndexName.Should().Be("spaarke-records-index-tenant-b", "FR-BFF-05");
        dto.Query.Should().Be("Acme Corp");
        dto.RecordTypes.Should().HaveCount(2);
    }

    [Fact]
    public void RecordSearchRequest_RoundTripsSearchIndexNameValue()
    {
        // Arrange
        var original = new RecordSearchRequest
        {
            Query = "matter X",
            RecordTypes = new[] { "sprk_matter" },
            SearchIndexName = "spaarke-records-index-tenant-c"
        };

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, WebOptions);
        var roundTripped = JsonSerializer.Deserialize<RecordSearchRequest>(bytes, WebOptions);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.SearchIndexName.Should().Be("spaarke-records-index-tenant-c");
    }

    // ====== RagSearchRequest ======

    [Fact]
    public void RagSearchRequest_JsonWithoutSearchIndexName_DeserializesWithNull()
    {
        // Arrange — pre-FR-BFF-05 RAG caller.
        const string json = """
            {
              "query": "what is the warranty period",
              "options": {
                "tenantId": "tenant-1",
                "topK": 10
              }
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<RagSearchRequest>(json, WebOptions);

        // Assert
        dto.Should().NotBeNull();
        dto!.SearchIndexName.Should().BeNull("requests without searchIndexName must continue to work (NFR-02)");
        dto.Query.Should().Be("what is the warranty period");
        dto.Options.Should().NotBeNull();
        dto.Options.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void RagSearchRequest_JsonWithSearchIndexName_DeserializesProperty()
    {
        // Arrange
        const string json = """
            {
              "query": "indemnification clauses",
              "options": {
                "tenantId": "tenant-1",
                "topK": 5
              },
              "searchIndexName": "spaarke-rag-references"
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<RagSearchRequest>(json, WebOptions);

        // Assert
        dto.Should().NotBeNull();
        dto!.SearchIndexName.Should().Be("spaarke-rag-references", "FR-BFF-05");
        dto.Query.Should().Be("indemnification clauses");
    }

    [Fact]
    public void RagSearchRequest_RoundTripsSearchIndexNameValue()
    {
        // Arrange
        var original = new RagSearchRequest
        {
            Query = "indemnification",
            Options = new RagSearchOptions { TenantId = "tenant-1", TopK = 5 },
            SearchIndexName = "spaarke-knowledge-index-v2"
        };

        // Act
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original, WebOptions);
        var roundTripped = JsonSerializer.Deserialize<RagSearchRequest>(bytes, WebOptions);

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped!.SearchIndexName.Should().Be("spaarke-knowledge-index-v2");
    }
}
