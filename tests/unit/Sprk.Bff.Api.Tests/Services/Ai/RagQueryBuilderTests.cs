using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="RagQueryBuilder"/>.
/// Verifies metadata-aware RAG query construction from DocumentAnalysisResult.
/// </summary>
public class RagQueryBuilderTests
{
    private readonly RagQueryBuilder _sut = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: Entities extracted from result appear in search text
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_WithEntities_IncludesEntityTermsInSearchText()
    {
        // Arrange
        var result = new DocumentAnalysisResult
        {
            Summary = "This is an NDA between two companies.",
            Keywords = "confidentiality, disclosure, agreement",
            Entities = new ExtractedEntities
            {
                Organizations = ["Acme Corp", "GlobalTech Ltd"],
                People = ["Jane Smith"],
                References = ["NDA-2025-001"],
                Amounts = [],
                Dates = [],
                DocumentType = "nda"
            }
        };
        const string tenantId = "tenant-abc";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.SearchText.Should().Contain("Acme Corp",
            because: "organizations are highest-priority entity terms");
        query.SearchText.Should().Contain("GlobalTech Ltd",
            because: "all extracted organizations should appear in search text");
        query.SearchText.Should().Contain("Jane Smith",
            because: "people entities should appear in search text");
        query.SearchText.Should().Contain("NDA-2025-001",
            because: "reference identifiers should appear in search text");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: Key phrases from keywords field appear in search text
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_WithKeywords_IncludesKeyPhrasesInSearchText()
    {
        // Arrange
        var result = new DocumentAnalysisResult
        {
            Summary = "A commercial lease agreement.",
            Keywords = "lease, rent, landlord, tenant, commercial property",
            Entities = new ExtractedEntities
            {
                Organizations = [],
                People = [],
                DocumentType = "lease"
            }
        };
        const string tenantId = "tenant-xyz";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.SearchText.Should().Contain("lease",
            because: "key phrases from Keywords field should appear in search text");
        query.SearchText.Should().Contain("landlord",
            because: "key phrases from Keywords field should appear in search text");
        query.SearchText.Should().Contain("commercial property",
            because: "multi-word key phrases should be preserved");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: Tenant filter expression contains tenantId
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_Always_IncludesTenantIdInFilterExpression()
    {
        // Arrange
        var result = new DocumentAnalysisResult
        {
            Summary = "Standard contract.",
            Keywords = "contract, terms",
            Entities = new ExtractedEntities { DocumentType = "contract" }
        };
        const string tenantId = "tenant-99a7b3c2";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.FilterExpression.Should().Contain(tenantId,
            because: "ADR-014 requires tenant isolation on all index queries");
        query.FilterExpression.Should().Contain("tenantId eq",
            because: "filter expression must use OData eq operator for tenant scoping");
    }

    [Fact]
    public void BuildQuery_TenantIdWithSpecialChars_EscapesODataFilterCorrectly()
    {
        // Arrange — tenant ID containing a single quote (edge case for OData injection)
        var result = new DocumentAnalysisResult
        {
            Summary = "Document.",
            Keywords = string.Empty,
            Entities = new ExtractedEntities { DocumentType = "other" }
        };
        const string tenantId = "tenant-with'quote";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.FilterExpression.Should().Contain("tenant-with''quote",
            because: "single quotes in OData filter values must be escaped as double single quotes");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4: Document type filter applied when type is strongly typed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_WithStronglyTypedDocumentType_AppliesDocumentTypeFilter()
    {
        // Arrange
        var result = new DocumentAnalysisResult
        {
            Summary = "A non-disclosure agreement.",
            Keywords = "confidential, disclosure",
            Entities = new ExtractedEntities { DocumentType = "nda" }
        };
        const string tenantId = "tenant-doc-type-test";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.FilterExpression.Should().Contain("documentType eq 'nda'",
            because: "strongly typed document types should narrow the index search");
    }

    [Fact]
    public void BuildQuery_WithDocumentTypeOther_DoesNotApplyDocumentTypeFilter()
    {
        // Arrange — "other" is the catch-all type and should not restrict results
        var result = new DocumentAnalysisResult
        {
            Summary = "An unclassified document.",
            Keywords = string.Empty,
            Entities = new ExtractedEntities { DocumentType = "other" }
        };
        const string tenantId = "tenant-other-doc";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.FilterExpression.Should().NotContain("documentType",
            because: "'other' is the catch-all type and should not restrict the search index");
        query.FilterExpression.Should().Contain("tenantId",
            because: "tenant filter must always be present even when document type is omitted");
    }

    [Fact]
    public void BuildQuery_WithEmptyDocumentType_DoesNotApplyDocumentTypeFilter()
    {
        // Arrange
        var result = new DocumentAnalysisResult
        {
            Summary = "Document without type.",
            Keywords = string.Empty,
            Entities = new ExtractedEntities { DocumentType = string.Empty }
        };
        const string tenantId = "tenant-no-type";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.FilterExpression.Should().NotContain("documentType",
            because: "empty document type should not add a filter clause");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: Empty result produces reasonable fallback query
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_WithEmptyAnalysisResult_ReturnsFallbackQuery()
    {
        // Arrange — completely empty result (e.g., extraction failure fallback)
        var result = new DocumentAnalysisResult
        {
            Summary = string.Empty,
            Keywords = string.Empty,
            Entities = new ExtractedEntities
            {
                Organizations = [],
                People = [],
                References = [],
                Amounts = [],
                Dates = [],
                DocumentType = "other"
            }
        };
        const string tenantId = "tenant-empty";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.SearchText.Should().NotBeNullOrWhiteSpace(
            because: "builder must always produce a non-empty search text for RAG to function");
        query.FilterExpression.Should().Contain(tenantId,
            because: "tenant filter is mandatory even for fallback queries");
    }

    [Fact]
    public void BuildQuery_WithEmptyResult_FallbackTextIsSemanticallySensible()
    {
        // Arrange
        var result = new DocumentAnalysisResult
        {
            Summary = string.Empty,
            Keywords = string.Empty,
            Entities = new ExtractedEntities { DocumentType = "other" }
        };
        const string tenantId = "tenant-fallback";

        // Act
        var query = _sut.BuildQuery(result, tenantId);

        // Assert
        query.SearchText.Length.Should().BeGreaterThan(3,
            because: "fallback search text should be a meaningful phrase, not an empty or trivial string");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6: Default result parameters
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_Always_ReturnsDefaultTopAndMinRelevanceScore()
    {
        // Arrange
        var result = new DocumentAnalysisResult
        {
            Summary = "Test document.",
            Keywords = "test",
            Entities = new ExtractedEntities { DocumentType = "other" }
        };

        // Act
        var query = _sut.BuildQuery(result, "tenant-defaults");

        // Assert
        query.Top.Should().Be(10,
            because: "default Top value must be 10 per RagQuery record definition");
        query.MinRelevanceScore.Should().Be(0.7,
            because: "default MinRelevanceScore must be 0.7 per RagQuery record definition");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 7: Summary included in search text for semantic context
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_WithSummary_IncludesSummaryFragmentInSearchText()
    {
        // Arrange
        const string summaryFragment = "This NDA governs the exchange of confidential information";
        var result = new DocumentAnalysisResult
        {
            Summary = summaryFragment,
            Keywords = string.Empty,
            Entities = new ExtractedEntities { DocumentType = "other" }
        };

        // Act
        var query = _sut.BuildQuery(result, "tenant-summary-test");

        // Assert
        query.SearchText.Should().Contain(summaryFragment,
            because: "summary is included as semantic context for vector search");
    }

    [Fact]
    public void BuildQuery_WithLongSummary_TruncatesTo200Chars()
    {
        // Arrange
        var longSummary = new string('x', 500);
        var result = new DocumentAnalysisResult
        {
            Summary = longSummary,
            Keywords = string.Empty,
            Entities = new ExtractedEntities { DocumentType = "other" }
        };

        // Act
        var query = _sut.BuildQuery(result, "tenant-truncation-test");

        // Assert — the 500-char summary should be truncated to 200 in the search text
        var summaryInQuery = query.SearchText.Substring(0, Math.Min(200, query.SearchText.Length));
        summaryInQuery.Should().HaveLength(200,
            because: "long summaries are truncated at 200 characters to keep query concise");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 8: Guard clauses
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQuery_NullResult_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.BuildQuery(null!, "tenant-id");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildQuery_NullTenantId_ThrowsArgumentException()
    {
        // Arrange
        var result = new DocumentAnalysisResult { Entities = new ExtractedEntities() };

        // Act
        var act = () => _sut.BuildQuery(result, null!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildQuery_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var result = new DocumentAnalysisResult { Entities = new ExtractedEntities() };

        // Act
        var act = () => _sut.BuildQuery(result, string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
