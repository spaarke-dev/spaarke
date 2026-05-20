using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// Unit tests for <see cref="InternalIndexProvider"/>.
///
/// Test matrix:
///   1. High-score result (&gt;= 0.85) → IsVerified = true
///   2. Low-score result  (&lt;  0.85) → IsVerified = false, ConfidenceScore = 0
///   3. No search results            → IsVerified = false, ConfidenceScore = 0
///   4. RequestFailedException        → IsVerified = false (never propagated)
///   5. ProviderName, SupportedTypes, CanVerify contract
///   6. SearchAsync returns up to 5 citations
///   7. SearchAsync on RequestFailedException returns empty list (never propagates)
///   8. GetFullTextAsync returns content of matching document
///   9. GetFullTextAsync returns null when no match
///  10. GetFullTextAsync returns null on RequestFailedException (never propagates)
/// </summary>
public class InternalIndexProviderTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static readonly ILogger<InternalIndexProvider> NullLogger =
        NullLogger<InternalIndexProvider>.Instance;

    /// <summary>
    /// Builds a real <see cref="SearchDocument"/> with the given field values.
    /// </summary>
    private static SearchDocument BuildDoc(
        string id      = "doc-1",
        string content = "The court held that …",
        string domain  = "caselaw",
        string ks      = "Legal Corpus")
    {
        var doc = new SearchDocument();
        doc["id"]                 = id;
        doc["content"]            = content;
        doc["domain"]             = domain;
        doc["knowledgeSourceName"] = ks;
        return doc;
    }

    /// <summary>
    /// Creates a mock <see cref="SearchClient"/> that returns the supplied results for
    /// any <c>SearchAsync</c> call.  Because <see cref="SearchClient"/> is not an interface,
    /// we use a real client pointed at a non-existent endpoint and rely on Moq's
    /// virtual-method interception.
    ///
    /// Azure SDK's <see cref="SearchClient"/> exposes a virtual
    /// <c>SearchAsync&lt;T&gt;</c> overload, so Moq can intercept it.
    /// </summary>
    private static Mock<SearchClient> BuildMockClient(
        IReadOnlyList<(SearchDocument doc, double score)> results)
    {
        var mockClient = new Mock<SearchClient>();

        // Build the page of results using the Azure SDK's model factories.
        // SearchModelFactory.SearchResult<T>(document, score?, highlights?) — 3-arg overload.
        var searchResultItems = results
            .Select(r => SearchModelFactory.SearchResult<SearchDocument>(
                document:   r.doc,
                score:      r.score,
                highlights: null))
            .ToList();

        var searchResults = SearchModelFactory.SearchResults<SearchDocument>(
            values:      searchResultItems,
            totalCount:  results.Count,
            facets:      null,
            coverage:    null,
            rawResponse: null!);

        mockClient
            .Setup(c => c.SearchAsync<SearchDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(searchResults, Mock.Of<Response>()));

        return mockClient;
    }

    /// <summary>
    /// Creates a mock <see cref="SearchClient"/> that throws a
    /// <see cref="RequestFailedException"/> on any <c>SearchAsync</c> call.
    /// </summary>
    private static Mock<SearchClient> BuildThrowingClient(int statusCode = 503)
    {
        var mockClient = new Mock<SearchClient>();

        mockClient
            .Setup(c => c.SearchAsync<SearchDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(statusCode, "Simulated search failure"));

        return mockClient;
    }

    private static Citation MakeCitation(
        string rawText      = "Smith v. Jones, 542 U.S. 296 (2004)",
        CitationType type   = CitationType.CaseLaw,
        string normalizedKey = "542 U.S. 296")
        => new(rawText, type, normalizedKey);

    // =========================================================================
    // 1. High-score result → verified
    // =========================================================================

    [Fact]
    public async Task VerifyAsync_HighScoreResult_ReturnsVerified()
    {
        // Score of 3.4 / 4.0 normalises to 0.85 — right at the threshold.
        // Use 3.6 to be clearly above.
        var doc     = BuildDoc();
        var client  = BuildMockClient([(doc, score: 1.0)]);

        // Score = 1.0 (BM25). Since no semantic score, effective = 1.0 >= 0.85 → verified.
        var sut     = new InternalIndexProvider(client.Object, NullLogger);
        var citation = MakeCitation();

        var result = await sut.VerifyAsync(citation, CancellationToken.None);

        result.IsVerified.Should().BeTrue();
        result.VerificationProvider.Should().Be("InternalIndex");
        result.ConfidenceScore.Should().BeGreaterThan(0f);
        result.VerifiedText.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // 2. Low-score result → not verified
    // =========================================================================

    [Fact]
    public async Task VerifyAsync_LowScoreResult_ReturnsNotVerified()
    {
        var doc    = BuildDoc();
        var client = BuildMockClient([(doc, score: 0.5)]);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        var result = await sut.VerifyAsync(MakeCitation(), CancellationToken.None);

        result.IsVerified.Should().BeFalse();
        result.ConfidenceScore.Should().Be(0f);
        result.VerificationProvider.Should().Be("InternalIndex");
    }

    // =========================================================================
    // 3. No search results → not verified
    // =========================================================================

    [Fact]
    public async Task VerifyAsync_NoResults_ReturnsNotVerified()
    {
        var client = BuildMockClient([]);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        var result = await sut.VerifyAsync(MakeCitation(), CancellationToken.None);

        result.IsVerified.Should().BeFalse();
        result.ConfidenceScore.Should().Be(0f);
        result.SourceUrl.Should().BeNull();
        result.VerifiedText.Should().BeNull();
    }

    // =========================================================================
    // 4. RequestFailedException → unverified (never propagated)
    // =========================================================================

    [Fact]
    public async Task VerifyAsync_SearchRequestFailed_ReturnsUnverifiedWithoutThrowing()
    {
        var client = BuildThrowingClient(503);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        Func<Task> act = () => sut.VerifyAsync(MakeCitation(), CancellationToken.None);

        // Must NOT throw.
        await act.Should().NotThrowAsync();

        var result = await sut.VerifyAsync(MakeCitation(), CancellationToken.None);
        result.IsVerified.Should().BeFalse();
        result.ConfidenceScore.Should().Be(0f);
    }

    // =========================================================================
    // 5. ProviderName, SupportedTypes, CanVerify
    // =========================================================================

    [Fact]
    public void ProviderName_IsCaseSensitiveExpectedValue()
    {
        var sut = new InternalIndexProvider(new Mock<SearchClient>().Object, NullLogger);
        sut.ProviderName.Should().Be("InternalIndex");
    }

    [Theory]
    [InlineData(CitationType.CaseLaw,   true)]
    [InlineData(CitationType.Statute,   true)]
    [InlineData(CitationType.Regulation, true)]
    [InlineData(CitationType.Patent,    false)]
    [InlineData(CitationType.SecFiling, false)]
    [InlineData(CitationType.Unknown,   false)]
    public void CanVerify_ReturnsCorrectValue(CitationType type, bool expected)
    {
        var sut = new InternalIndexProvider(new Mock<SearchClient>().Object, NullLogger);
        sut.CanVerify(type).Should().Be(expected);
    }

    [Fact]
    public void SupportedTypes_ContainsExactlyThreeTypes()
    {
        var sut = new InternalIndexProvider(new Mock<SearchClient>().Object, NullLogger);
        sut.SupportedTypes.Should().BeEquivalentTo(
        [
            CitationType.CaseLaw,
            CitationType.Statute,
            CitationType.Regulation,
        ]);
    }

    // =========================================================================
    // 6. SearchAsync returns up to 5 citations
    // =========================================================================

    [Fact]
    public async Task SearchAsync_MultipleResults_ReturnsCitationPerResult()
    {
        var docs = Enumerable.Range(1, 3).Select(i =>
            (BuildDoc(id: $"doc-{i}", content: $"Content {i}", domain: "statute"),
             score: 0.9)).ToList();

        var client = BuildMockClient(docs);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        var citations = await sut.SearchAsync("tax law", CancellationToken.None);

        citations.Should().HaveCount(3);
        citations.Should().AllSatisfy(c => c.CitationType.Should().Be(CitationType.Statute));
    }

    [Fact]
    public async Task SearchAsync_EmptyResults_ReturnsEmptyList()
    {
        var client = BuildMockClient([]);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        var citations = await sut.SearchAsync("obscure term", CancellationToken.None);

        citations.Should().BeEmpty();
    }

    // =========================================================================
    // 7. SearchAsync on RequestFailedException returns empty list
    // =========================================================================

    [Fact]
    public async Task SearchAsync_SearchRequestFailed_ReturnsEmptyListWithoutThrowing()
    {
        var client = BuildThrowingClient(503);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        Func<Task> act = () => sut.SearchAsync("query", CancellationToken.None);
        await act.Should().NotThrowAsync();

        var result = await sut.SearchAsync("query", CancellationToken.None);
        result.Should().BeEmpty();
    }

    // =========================================================================
    // 8. GetFullTextAsync returns content of matching document
    // =========================================================================

    [Fact]
    public async Task GetFullTextAsync_MatchFound_ReturnsContent()
    {
        const string expectedContent = "The full text of the statute.";
        var doc    = BuildDoc(content: expectedContent);
        var client = BuildMockClient([(doc, score: 1.0)]);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        var text = await sut.GetFullTextAsync(
            new Citation("35 U.S.C. § 101", CitationType.Statute, "35 U.S.C. § 101"),
            CancellationToken.None);

        text.Should().Be(expectedContent);
    }

    // =========================================================================
    // 9. GetFullTextAsync returns null when no match
    // =========================================================================

    [Fact]
    public async Task GetFullTextAsync_NoResults_ReturnsNull()
    {
        var client = BuildMockClient([]);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        var text = await sut.GetFullTextAsync(
            new Citation("47 C.F.R. § 73.3999", CitationType.Regulation, "47 C.F.R. § 73.3999"),
            CancellationToken.None);

        text.Should().BeNull();
    }

    // =========================================================================
    // 10. GetFullTextAsync returns null on RequestFailedException
    // =========================================================================

    [Fact]
    public async Task GetFullTextAsync_SearchRequestFailed_ReturnsNullWithoutThrowing()
    {
        var client = BuildThrowingClient(503);
        var sut    = new InternalIndexProvider(client.Object, NullLogger);

        Func<Task> act = () => sut.GetFullTextAsync(
            new Citation("35 U.S.C. § 101", CitationType.Statute, "35 U.S.C. § 101"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();

        var result = await sut.GetFullTextAsync(
            new Citation("35 U.S.C. § 101", CitationType.Statute, "35 U.S.C. § 101"),
            CancellationToken.None);
        result.Should().BeNull();
    }
}
