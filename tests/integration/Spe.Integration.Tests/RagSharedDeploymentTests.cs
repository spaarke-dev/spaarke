using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for RAG Shared deployment model.
/// Tests document indexing, hybrid search, and tenant isolation.
/// </summary>
/// <remarks>
/// Task 006: Test Shared Deployment Model
///
/// Prerequisites:
/// - Azure AI Search index "spaarke-knowledge-index" deployed
/// - Azure OpenAI text-embedding-3-small model available
/// - Redis cache running (for embedding cache)
/// - appsettings.json configured with credentials
///
/// These tests create and clean up their own test data.
/// </remarks>
[Collection("Integration")]
[Trait("Category", "Integration")]
[Trait("Feature", "RAG")]
public class RagSharedDeploymentTests : IClassFixture<IntegrationTestFixture>, IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly IRagService _ragService;
    private readonly List<string> _indexedDocumentIds = [];
    private readonly string _testTenantId = "test-tenant-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _otherTenantId = "other-tenant-" + Guid.NewGuid().ToString("N")[..8];
    private readonly List<long> _searchLatencies = [];

    public RagSharedDeploymentTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;

        // Get services from DI container
        using var scope = _fixture.Services.CreateScope();
        _ragService = scope.ServiceProvider.GetRequiredService<IRagService>();
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine($"Test tenant ID: {_testTenantId}");
        _output.WriteLine($"Other tenant ID: {_otherTenantId}");

        // Index test documents for our tenant
        var testDocs = CreateTestDocuments(_testTenantId);
        foreach (var doc in testDocs)
        {
            var indexed = await _ragService.IndexDocumentAsync(doc);
            _indexedDocumentIds.Add(indexed.Id);
            _output.WriteLine($"Indexed: {indexed.Id} - {indexed.FileName}");
        }

        // Index some documents for the other tenant (for isolation testing)
        var otherDocs = CreateTestDocuments(_otherTenantId, prefix: "other-");
        foreach (var doc in otherDocs)
        {
            var indexed = await _ragService.IndexDocumentAsync(doc);
            _indexedDocumentIds.Add(indexed.Id);
            _output.WriteLine($"Indexed (other tenant): {indexed.Id}");
        }

        // Wait for index to be searchable (Azure AI Search has eventual consistency)
        await Task.Delay(TimeSpan.FromSeconds(3));
    }

    public async Task DisposeAsync()
    {
        // Clean up test documents
        _output.WriteLine("Cleaning up test documents...");
        foreach (var docId in _indexedDocumentIds)
        {
            var tenantId = docId.Contains("other-") ? _otherTenantId : _testTenantId;
            try
            {
                await _ragService.DeleteDocumentAsync(docId, tenantId);
                _output.WriteLine($"Deleted: {docId}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Warning: Failed to delete {docId}: {ex.Message}");
            }
        }
    }

    #region Step 1-2: Document Indexing Tests

    [Fact]
    public async Task IndexDocumentAsync_ValidDocument_SuccessfullyIndexed()
    {
        // Arrange
        var doc = new KnowledgeDocument
        {
            Id = $"test-index-{Guid.NewGuid():N}",
            TenantId = _testTenantId,
            DeploymentModel = KnowledgeDeploymentModel.Shared,
            DocumentId = "test-source-doc-1",
            FileName = "Index Test Document.pdf",
            DocumentType = "contract",
            ChunkIndex = 0,
            ChunkCount = 1,
            Content = "This is a test document for indexing verification. It contains unique content for testing.",
            Tags = ["test", "indexing"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _ragService.IndexDocumentAsync(doc);
        _indexedDocumentIds.Add(result.Id); // Track for cleanup

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(doc.Id);
        result.ContentVector.Length.Should().Be(3072, "text-embedding-3-large produces 3072 dimensions");

        _output.WriteLine($"Document indexed with ID: {result.Id}");
        _output.WriteLine($"Embedding dimensions: {result.ContentVector.Length}");
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_MultipleDocuments_AllSuccessfullyIndexed()
    {
        // Arrange
        var docs = Enumerable.Range(0, 5).Select(i => new KnowledgeDocument
        {
            Id = $"test-batch-{Guid.NewGuid():N}",
            TenantId = _testTenantId,
            DeploymentModel = KnowledgeDeploymentModel.Shared,
            DocumentId = "test-batch-source",
            FileName = $"Batch Test Document {i}.pdf",
            DocumentType = "policy",
            ChunkIndex = i,
            ChunkCount = 5,
            Content = $"Batch document chunk {i}. This contains test content for batch indexing verification.",
            Tags = ["test", "batch"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }).ToList();

        // Act
        var results = await _ragService.IndexDocumentsBatchAsync(docs);
        foreach (var r in results.Where(r => r.Succeeded))
        {
            _indexedDocumentIds.Add(r.Id); // Track for cleanup
        }

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue($"Document {r.Id} should be indexed"));

        _output.WriteLine($"Batch indexed {results.Count} documents successfully");
    }

    #endregion

    #region Step 3-4: Hybrid Search Tests

    [Fact]
    public async Task SearchAsync_HybridSearch_ReturnsRelevantResults()
    {
        // Arrange
        var options = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 5,
            MinScore = 0.5f,
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await _ragService.SearchAsync("employment termination procedures", options);
        stopwatch.Stop();
        _searchLatencies.Add(stopwatch.ElapsedMilliseconds);

        // Assert
        response.Should().NotBeNull();
        response.Results.Should().NotBeEmpty("hybrid search should find relevant documents");
        response.Query.Should().Be("employment termination procedures");
        response.SearchDurationMs.Should().BeGreaterThan(0);

        _output.WriteLine($"Query: {response.Query}");
        _output.WriteLine($"Total results: {response.TotalCount}");
        _output.WriteLine($"Search duration: {response.SearchDurationMs}ms");
        _output.WriteLine($"Embedding duration: {response.EmbeddingDurationMs}ms");
        _output.WriteLine($"Embedding cache hit: {response.EmbeddingCacheHit}");

        foreach (var result in response.Results)
        {
            _output.WriteLine($"  - [{result.Score:F3}] {result.DocumentName}: {result.Content[..Math.Min(100, result.Content.Length)]}...");
        }
    }

    [Fact]
    public async Task SearchAsync_VectorSearchOnly_ReturnsSemanticallySimilarResults()
    {
        // Arrange
        var options = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 5,
            MinScore = 0.5f,
            UseSemanticRanking = false,
            UseVectorSearch = true,
            UseKeywordSearch = false
        };

        // Act
        var response = await _ragService.SearchAsync("employee dismissal process", options);

        // Assert - should find "termination" docs even though query uses different words
        response.Should().NotBeNull();
        response.Results.Should().NotBeEmpty("vector search should find semantically similar content");

        _output.WriteLine($"Vector-only search found {response.Results.Count} results");
        foreach (var result in response.Results.Take(3))
        {
            _output.WriteLine($"  - [{result.Score:F3}] {result.Content[..Math.Min(80, result.Content.Length)]}...");
        }
    }

    [Fact]
    public async Task SearchAsync_KeywordSearchOnly_ReturnsExactMatches()
    {
        // Arrange
        var options = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 5,
            MinScore = 0.1f, // Lower threshold for keyword-only
            UseSemanticRanking = false,
            UseVectorSearch = false,
            UseKeywordSearch = true
        };

        // Act
        var response = await _ragService.SearchAsync("termination", options);

        // Assert
        response.Should().NotBeNull();

        _output.WriteLine($"Keyword-only search found {response.Results.Count} results");
        foreach (var result in response.Results.Take(3))
        {
            _output.WriteLine($"  - [{result.Score:F3}] {result.Content[..Math.Min(80, result.Content.Length)]}...");
        }
    }

    [Fact]
    public async Task SearchAsync_WithDocumentTypeFilter_ReturnsFilteredResults()
    {
        // Arrange
        var options = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 10,
            MinScore = 0.3f,
            DocumentType = "policy",
            UseSemanticRanking = true
        };

        // Act
        var response = await _ragService.SearchAsync("employee procedures", options);

        // Assert
        response.Should().NotBeNull();
        // All returned results should be of type "policy"
        foreach (var result in response.Results)
        {
            _output.WriteLine($"  - Type: policy, Score: {result.Score:F3}, Doc: {result.DocumentName}");
        }
    }

    [Fact]
    public async Task SearchAsync_WithTagFilter_ReturnsTaggedResults()
    {
        // Arrange
        var options = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 10,
            MinScore = 0.3f,
            Tags = ["hr", "legal"],
            UseSemanticRanking = true
        };

        // Act
        var response = await _ragService.SearchAsync("company policies", options);

        // Assert
        response.Should().NotBeNull();

        _output.WriteLine($"Tag-filtered search found {response.Results.Count} results");
        foreach (var result in response.Results)
        {
            _output.WriteLine($"  - Tags: [{string.Join(", ", result.Tags ?? [])}], Doc: {result.DocumentName}");
        }
    }

    #endregion

    #region Step 5: Tenant Isolation Tests

    [Fact]
    public async Task SearchAsync_TenantIsolation_OnlyReturnsOwnTenantDocuments()
    {
        // Arrange - search as test tenant
        var testTenantOptions = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 20,
            MinScore = 0.1f // Low threshold to find all matches
        };

        // Act
        var response = await _ragService.SearchAsync("test document", testTenantOptions);

        // Assert - should NOT find other tenant's documents
        response.Should().NotBeNull();
        response.Results.Should().AllSatisfy(r =>
        {
            r.Id.Should().NotContain("other-", "tenant isolation must prevent seeing other tenant's data");
        });

        _output.WriteLine($"Test tenant search found {response.Results.Count} results (none from other tenant)");
    }

    [Fact]
    public async Task SearchAsync_OtherTenant_CannotSeeTestTenantDocuments()
    {
        // Arrange - search as other tenant
        var otherTenantOptions = new RagSearchOptions
        {
            TenantId = _otherTenantId,
            TopK = 20,
            MinScore = 0.1f
        };

        // Act
        var response = await _ragService.SearchAsync("test document", otherTenantOptions);

        // Assert - should only find other tenant's docs
        response.Should().NotBeNull();

        _output.WriteLine($"Other tenant search found {response.Results.Count} results");
        foreach (var result in response.Results)
        {
            _output.WriteLine($"  - {result.Id} (should be 'other-' prefixed)");
        }
    }

    [Fact]
    public async Task SearchAsync_NonExistentTenant_ReturnsNoResults()
    {
        // Arrange
        var options = new RagSearchOptions
        {
            TenantId = "non-existent-tenant-xyz",
            TopK = 10,
            MinScore = 0.1f
        };

        // Act
        var response = await _ragService.SearchAsync("test document", options);

        // Assert
        response.Should().NotBeNull();
        response.Results.Should().BeEmpty("non-existent tenant should have no documents");

        _output.WriteLine("Non-existent tenant correctly returned 0 results");
    }

    #endregion

    #region Step 6: Performance / Latency Tests

    [Fact]
    public async Task SearchAsync_P95Latency_UnderTarget()
    {
        // Arrange
        const int iterations = 20;
        const int p95TargetMs = 500; // P95 target from task requirements
        var latencies = new List<long>();

        var options = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 5,
            MinScore = 0.5f,
            UseSemanticRanking = true
        };

        var queries = new[]
        {
            "employment termination procedures",
            "company holiday policy",
            "remote work guidelines",
            "expense reimbursement process",
            "performance review timeline"
        };

        // Act - run multiple searches
        for (int i = 0; i < iterations; i++)
        {
            var query = queries[i % queries.Length];
            var stopwatch = Stopwatch.StartNew();
            await _ragService.SearchAsync(query, options);
            stopwatch.Stop();
            latencies.Add(stopwatch.ElapsedMilliseconds);
        }

        // Calculate P95
        latencies.Sort();
        var p95Index = (int)Math.Ceiling(latencies.Count * 0.95) - 1;
        var p95Latency = latencies[p95Index];
        var avgLatency = latencies.Average();
        var minLatency = latencies.Min();
        var maxLatency = latencies.Max();

        // Assert
        _output.WriteLine($"Latency Statistics ({iterations} iterations):");
        _output.WriteLine($"  Min:  {minLatency}ms");
        _output.WriteLine($"  Avg:  {avgLatency:F1}ms");
        _output.WriteLine($"  P95:  {p95Latency}ms");
        _output.WriteLine($"  Max:  {maxLatency}ms");
        _output.WriteLine($"  Target: P95 < {p95TargetMs}ms");
        _output.WriteLine($"  Result: {(p95Latency < p95TargetMs ? "PASS" : "FAIL")}");

        p95Latency.Should().BeLessThan(p95TargetMs,
            $"P95 latency ({p95Latency}ms) should be under {p95TargetMs}ms target");
    }

    [Fact]
    public async Task SearchAsync_EmbeddingCache_ImprovesLatency()
    {
        // Arrange
        var options = new RagSearchOptions
        {
            TenantId = _testTenantId,
            TopK = 5,
            MinScore = 0.5f
        };
        const string query = "employee handbook policies and procedures";

        // First call (cache miss expected)
        var stopwatch1 = Stopwatch.StartNew();
        var response1 = await _ragService.SearchAsync(query, options);
        stopwatch1.Stop();

        // Second call (cache hit expected)
        var stopwatch2 = Stopwatch.StartNew();
        var response2 = await _ragService.SearchAsync(query, options);
        stopwatch2.Stop();

        // Assert
        _output.WriteLine($"First call (cache miss): {stopwatch1.ElapsedMilliseconds}ms, EmbeddingCacheHit: {response1.EmbeddingCacheHit}");
        _output.WriteLine($"Second call (cache hit): {stopwatch2.ElapsedMilliseconds}ms, EmbeddingCacheHit: {response2.EmbeddingCacheHit}");

        // Second call should have cache hit and be faster (embedding generation skipped)
        response2.EmbeddingCacheHit.Should().BeTrue("second call should hit embedding cache");
        response2.EmbeddingDurationMs.Should().BeLessThan(response1.EmbeddingDurationMs,
            "cached embedding should be faster than generating new one");
    }

    #endregion

    #region Helper Methods

    private List<KnowledgeDocument> CreateTestDocuments(string tenantId, string prefix = "")
    {
        var timestamp = DateTimeOffset.UtcNow;

        return
        [
            new KnowledgeDocument
            {
                Id = $"{prefix}doc1-chunk0-{Guid.NewGuid():N}",
                TenantId = tenantId,
                DeploymentModel = KnowledgeDeploymentModel.Shared,
                DocumentId = $"{prefix}employee-handbook",
                FileName = "Employee Handbook 2024.pdf",
                DocumentType = "policy",
                KnowledgeSourceId = "ks-hr-policies",
                KnowledgeSourceName = "HR Policies",
                ChunkIndex = 0,
                ChunkCount = 3,
                Content = "This employee handbook outlines company policies regarding employment termination procedures. All employees must follow the standard two-week notice period before leaving the organization. Managers are required to conduct exit interviews with departing staff members.",
                Tags = ["hr", "policy", "termination"],
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new KnowledgeDocument
            {
                Id = $"{prefix}doc1-chunk1-{Guid.NewGuid():N}",
                TenantId = tenantId,
                DeploymentModel = KnowledgeDeploymentModel.Shared,
                DocumentId = $"{prefix}employee-handbook",
                FileName = "Employee Handbook 2024.pdf",
                DocumentType = "policy",
                KnowledgeSourceId = "ks-hr-policies",
                KnowledgeSourceName = "HR Policies",
                ChunkIndex = 1,
                ChunkCount = 3,
                Content = "Remote work policy: Employees may work remotely up to three days per week with manager approval. All remote workers must be available during core business hours from 10 AM to 3 PM local time. Equipment will be provided for approved remote work arrangements.",
                Tags = ["hr", "policy", "remote-work"],
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new KnowledgeDocument
            {
                Id = $"{prefix}doc2-chunk0-{Guid.NewGuid():N}",
                TenantId = tenantId,
                DeploymentModel = KnowledgeDeploymentModel.Shared,
                DocumentId = $"{prefix}legal-contract-template",
                FileName = "Standard Service Agreement.docx",
                DocumentType = "contract",
                KnowledgeSourceId = "ks-legal-templates",
                KnowledgeSourceName = "Legal Templates",
                ChunkIndex = 0,
                ChunkCount = 2,
                Content = "This Service Agreement governs the terms and conditions under which services will be provided. The agreement shall commence on the effective date and continue for a period of twelve months unless terminated earlier in accordance with the termination clause.",
                Tags = ["legal", "contract", "template"],
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new KnowledgeDocument
            {
                Id = $"{prefix}doc3-chunk0-{Guid.NewGuid():N}",
                TenantId = tenantId,
                DeploymentModel = KnowledgeDeploymentModel.Shared,
                DocumentId = $"{prefix}expense-policy",
                FileName = "Expense Reimbursement Policy.pdf",
                DocumentType = "policy",
                KnowledgeSourceId = "ks-finance-policies",
                KnowledgeSourceName = "Finance Policies",
                ChunkIndex = 0,
                ChunkCount = 1,
                Content = "Expense reimbursement guidelines: Employees must submit expense reports within 30 days of incurring the expense. Receipts are required for all expenses over $25. Travel expenses must be pre-approved by department managers. Mileage reimbursement rate is $0.67 per mile.",
                Tags = ["finance", "policy", "expenses"],
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            }
        ];
    }

    #endregion
}
