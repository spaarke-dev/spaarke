using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="InvoiceSearchService"/> returns only the invoices whose source document AND invoice record the caller can
/// Read, through the one retrieval access trim (<see cref="IRetrievalAccessTrim"/>), and fails closed when the trim
/// cannot evaluate. The index is a <see cref="SearchClient"/> test double (SDK model factory); the trim is a fake that
/// answers from a set of readable records, so these tests pin how the service applies the seam, not the seam itself
/// (covered by its own suite).
/// </summary>
public class InvoiceSearchServiceAccessTrimTests
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000197";
    private const string IndexName = "spaarke-invoices-index";

    private readonly Mock<SearchIndexClient> _indexClient = new();
    private readonly Mock<SearchClient> _searchClient = new();
    private readonly Mock<IInvoiceAi> _invoiceAi = new();

    public InvoiceSearchServiceAccessTrimTests()
    {
        _indexClient.Setup(c => c.GetSearchClient(IndexName)).Returns(_searchClient.Object);
        _invoiceAi
            .Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));
    }

    [Fact]
    public async Task SearchAsync_AHitWhoseDocumentTheCallerCannotRead_IsDropped_AReadableOneIsKept()
    {
        var readable = Hit();
        var unreadableDocument = Hit();
        IndexReturns(readable, unreadableDocument);
        var trim = new FakeTrim();
        trim.Readable(readable.DocumentId, readable.InvoiceId);
        trim.Readable(null, unreadableDocument.InvoiceId);

        var response = await CreateService(trim).SearchAsync("fees", CallerOid, Guid.NewGuid(), top: 10);

        response.Results.Select(r => r.InvoiceId).Should().Equal(Guid.Parse(readable.InvoiceId));
        response.TotalCount.Should().Be(1, "the count is of results the caller may see, never the index count");
        trim.CallerOids.Should().OnlyContain(oid => oid == CallerOid);
    }

    [Fact]
    public async Task SearchAsync_AHitWhoseInvoiceRecordTheCallerCannotRead_IsDropped()
    {
        var readable = Hit();
        var unreadableInvoice = Hit();
        IndexReturns(readable, unreadableInvoice);
        var trim = new FakeTrim();
        trim.Readable(readable.DocumentId, readable.InvoiceId);
        trim.Readable(unreadableInvoice.DocumentId, null);

        var response = await CreateService(trim).SearchAsync("fees", CallerOid, Guid.NewGuid(), top: 10);

        response.Results.Select(r => r.InvoiceId).Should().Equal(Guid.Parse(readable.InvoiceId));
    }

    [Theory]
    [InlineData(RetrievalTrimOutcome.NoVerifiedCaller)]
    [InlineData(RetrievalTrimOutcome.CheckFailed)]
    public async Task SearchAsync_WhenTheTrimCannotEvaluate_ReturnsNothing(RetrievalTrimOutcome outcome)
    {
        var hit = Hit();
        IndexReturns(hit);
        var trim = new FakeTrim { Outcome = outcome };
        trim.Readable(hit.DocumentId, hit.InvoiceId);

        var response = await CreateService(trim).SearchAsync("fees", CallerOid, Guid.NewGuid(), top: 10);

        response.Results.Should().BeEmpty("the trim fails closed");
        response.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithNoTrimServiceAvailable_ReturnsNothing()
    {
        IndexReturns(Hit());

        var response = await CreateService(trim: null).SearchAsync("fees", CallerOid, Guid.NewGuid(), top: 10);

        response.Results.Should().BeEmpty("results are never returned unchecked");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private InvoiceSearchService CreateService(IRetrievalAccessTrim? trim) => new(
        _indexClient.Object,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = "tenant-r197",
        }).Build(),
        NullLogger<InvoiceSearchService>.Instance,
        _invoiceAi.Object,
        trim);

    private static InvoiceSearchDocument Hit() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Content = "content",
        InvoiceId = Guid.NewGuid().ToString("D"),
        DocumentId = Guid.NewGuid().ToString("D"),
        InvoiceNumber = "INV",
        VendorName = "Vendor",
    };

    private void IndexReturns(params InvoiceSearchDocument[] hits)
    {
        var results = SearchModelFactory.SearchResults<InvoiceSearchDocument>(
            values: hits.Select((h, i) => SearchModelFactory.SearchResult(h, score: 4.0 - i * 0.1, highlights: null)).ToList(),
            totalCount: 100,
            facets: null,
            coverage: null,
            rawResponse: null!);
        _searchClient
            .Setup(c => c.SearchAsync<InvoiceSearchDocument>(It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(results, null!));
    }

    /// <summary>Keeps a row when its record key is in the readable set; reports <see cref="Outcome"/>.</summary>
    private sealed class FakeTrim : IRetrievalAccessTrim
    {
        private readonly HashSet<(string Set, string Value)> _readable = new();

        public RetrievalTrimOutcome Outcome { get; init; } = RetrievalTrimOutcome.Evaluated;

        public List<string?> CallerOids { get; } = new();

        public void Readable(string? documentId, string? invoiceId)
        {
            if (RetrievalRecordKey.Document(documentId) is { } d) _readable.Add((d.EntitySetName, d.Value));
            if (RetrievalRecordKey.ParentRecord("invoice", invoiceId) is { } r) _readable.Add((r.EntitySetName, r.Value));
        }

        public bool CanEvaluate(string? callerObjectId) => Outcome == RetrievalTrimOutcome.Evaluated;

        public Task<RetrievalTrimResult<T>> TrimAsync<T>(
            IReadOnlyList<T> rows, Func<T, string?> documentIdOf, string? callerObjectId, CancellationToken cancellationToken = default)
            => TrimByRecordAsync(rows, r => RetrievalRecordKey.Document(documentIdOf(r)), callerObjectId, cancellationToken);

        public Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
            IReadOnlyList<T> rows, Func<T, RetrievalRecordKey?> recordOf, string? callerObjectId, CancellationToken cancellationToken = default)
        {
            CallerOids.Add(callerObjectId);
            if (Outcome != RetrievalTrimOutcome.Evaluated)
            {
                return Task.FromResult(new RetrievalTrimResult<T>(Array.Empty<T>(), Outcome, rows.Count));
            }

            var kept = rows.Where(r => recordOf(r) is { } key && _readable.Contains((key.EntitySetName, key.Value))).ToList();
            return Task.FromResult(new RetrievalTrimResult<T>(kept, RetrievalTrimOutcome.Evaluated, rows.Count - kept.Count));
        }
    }
}
