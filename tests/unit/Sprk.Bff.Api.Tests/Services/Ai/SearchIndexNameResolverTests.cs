using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the shared index-name resolver. Since field-mapping-server-write-path-r1 task 041 (FR-40) it names only the
/// canonical default index: the per-record chain (document → parent → business unit) and its tests are gone.
/// </summary>
public sealed class SearchIndexNameResolverTests
{
    private const string DefaultKnowledgeIndex = "spaarke-files-index";

    private static SearchIndexNameResolver CreateResolver(string knowledgeIndexName = DefaultKnowledgeIndex) =>
        new(Options.Create(new AiSearchOptions { KnowledgeIndexName = knowledgeIndexName }));

    // ─────────────────────────────────────────────────────────────────────────
    // FR-26 / FAILURE-MODES G-9 — index-config split-brain consolidation.
    // The resolver is the single source of the tenant-default index name for BOTH
    // reads and writes; GetDefaultIndexName() returns AiSearchOptions.KnowledgeIndexName.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetDefaultIndexName_ReturnsCanonicalKnowledgeIndexName()
    {
        // Arrange — the single canonical read/write setting is AiSearch:KnowledgeIndexName.
        var resolver = CreateResolver(knowledgeIndexName: "spaarke-file-index");

        // Act + Assert
        resolver.GetDefaultIndexName().Should().Be("spaarke-file-index");
    }

    [Fact]
    public void GetDefaultIndexName_ReadAndWriteDefault_ResolveToSameIndex()
    {
        // FR-26 / G-9: the READ default (RagService reads AiSearchOptions.KnowledgeIndexName) and the
        // WRITE default (indexing job handler, RAG endpoints, KnowledgeDeploymentService now call
        // GetDefaultIndexName()) derive from the SAME single setting — flipping it moves both, so
        // reads and writes can no longer silently target different indexes.
        const string canonical = "spaarke-consolidated-index";
        var options = new AiSearchOptions { KnowledgeIndexName = canonical };
        var resolver = new SearchIndexNameResolver(Options.Create(options));

        // write-side default (what every write/stamp path now calls)
        var writeDefault = resolver.GetDefaultIndexName();
        // read-side default (the same field RagService reads for searches)
        var readDefault = options.KnowledgeIndexName;

        writeDefault.Should().Be(readDefault);
        writeDefault.Should().Be(canonical);
    }
}
