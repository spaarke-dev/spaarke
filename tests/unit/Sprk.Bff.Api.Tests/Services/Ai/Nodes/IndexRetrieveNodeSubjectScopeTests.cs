using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for Wave D6 (task 035) hybrid dual-read filter generation in
/// <see cref="IndexRetrieveNode"/>. Covers design-a6 §4.5 OR-filter logic for matter subjects
/// (NFR-08 backward-compat) + canonical narrow-filter for project/invoice subjects.
/// </summary>
/// <remarks>
/// Targets the internal helpers <see cref="IndexRetrieveNode.BuildSubjectScopeFilter"/> +
/// <see cref="IndexRetrieveNode.BuildFilter"/> — the deterministic filter-composition surface
/// that drives the actual SearchClient query.
/// </remarks>
public sealed class IndexRetrieveNodeSubjectScopeTests
{
    // ─── BuildSubjectScopeFilter — matter (NFR-08 dual-read OR-filter) ────────

    [Fact]
    public void BuildSubjectScopeFilter_MatterSubject_EmitsHybridDualReadOrFilter()
    {
        // Per design-a6 §4.5: matter subjects MUST emit OR-filter that covers both:
        //   (a) Phase 1 Observations carrying scope.matterId only, AND
        //   (b) Phase 1.5 Observations carrying scope.entityType="matter" + scope.entityId
        var filter = IndexRetrieveNode.BuildSubjectScopeFilter("matter:M-2024-0341");

        filter.Should().NotBeNull();
        filter.Should().Contain("scope/matterId eq 'M-2024-0341'");
        filter.Should().Contain("scope/entityType eq 'matter'");
        filter.Should().Contain("scope/entityId eq 'M-2024-0341'");
        filter.Should().Contain(" or ", because: "NFR-08 dual-read OR-filter per design-a6 §4.5");
    }

    [Fact]
    public void BuildSubjectScopeFilter_MatterSubject_CaseInsensitiveScheme()
    {
        // Scheme should normalize to lowercase before emission.
        var filter = IndexRetrieveNode.BuildSubjectScopeFilter("MATTER:M-1234");

        filter.Should().Contain("scope/matterId eq 'M-1234'");
        filter.Should().Contain("scope/entityType eq 'matter'");
    }

    // ─── BuildSubjectScopeFilter — project / invoice (canonical-only) ─────────

    [Fact]
    public void BuildSubjectScopeFilter_ProjectSubject_EmitsCanonicalOnly()
    {
        var filter = IndexRetrieveNode.BuildSubjectScopeFilter("project:p-abc-123");

        filter.Should().NotBeNull();
        filter.Should().Contain("scope/entityType eq 'project'");
        filter.Should().Contain("scope/entityId eq 'p-abc-123'");
        filter.Should().NotContain("scope/matterId", because: "non-matter schemes do NOT use scope.matterId");
        filter.Should().NotContain(" or ", because: "non-matter schemes use a single AND-clause");
    }

    [Fact]
    public void BuildSubjectScopeFilter_InvoiceSubject_EmitsCanonicalOnly()
    {
        var filter = IndexRetrieveNode.BuildSubjectScopeFilter("invoice:i-xyz-456");

        filter.Should().NotBeNull();
        filter.Should().Contain("scope/entityType eq 'invoice'");
        filter.Should().Contain("scope/entityId eq 'i-xyz-456'");
        filter.Should().NotContain("scope/matterId");
    }

    [Fact]
    public void BuildSubjectScopeFilter_UnknownScheme_EmitsCanonicalOnly()
    {
        // Future schemes (client:, contract:, document:) should work with the canonical
        // shape without code changes — design-a6 §2.3 (catalog-driven schemes).
        var filter = IndexRetrieveNode.BuildSubjectScopeFilter("client:c-acme");

        filter.Should().NotBeNull();
        filter.Should().Contain("scope/entityType eq 'client'");
        filter.Should().Contain("scope/entityId eq 'c-acme'");
        filter.Should().NotContain(" or ");
    }

    // ─── BuildSubjectScopeFilter — malformed (defense) ────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("no-colon")]
    [InlineData(":no-scheme")]
    [InlineData("no-id:")]
    [InlineData("   ")]
    public void BuildSubjectScopeFilter_Malformed_ReturnsNull(string subjectScope)
    {
        var filter = IndexRetrieveNode.BuildSubjectScopeFilter(subjectScope);
        filter.Should().BeNull();
    }

    [Fact]
    public void BuildSubjectScopeFilter_SingleQuoteInEntityId_EscapedODataSafely()
    {
        // OData escape: ' → '' (double single-quote).
        var filter = IndexRetrieveNode.BuildSubjectScopeFilter("matter:O'Brien");

        filter.Should().NotBeNull();
        filter.Should().Contain("scope/matterId eq 'O''Brien'");
        filter.Should().NotContain("'O'Brien'", because: "unescaped single quote would break the OData filter");
    }

    // ─── BuildFilter — composition with other narrowing dimensions ────────────

    [Fact]
    public void BuildFilter_TenantOnlyConfig_EmitsTenantGuardOnly()
    {
        var config = new IndexRetrieveNodeConfig();
        var filter = IndexRetrieveNode.BuildFilter("tenant-acme", config);

        filter.Should().Be("tenantId eq 'tenant-acme'");
    }

    [Fact]
    public void BuildFilter_WithSubjectScope_AppendsHybridFilter()
    {
        var config = new IndexRetrieveNodeConfig
        {
            ArtifactType = "observation",
            Predicate = "outcomeCategory",
            SubjectScope = "matter:M-2024-0341"
        };
        var filter = IndexRetrieveNode.BuildFilter("tenant-acme", config);

        filter.Should().Contain("tenantId eq 'tenant-acme'");
        filter.Should().Contain("artifactType eq 'observation'");
        filter.Should().Contain("predicate eq 'outcomeCategory'");
        filter.Should().Contain("scope/matterId eq 'M-2024-0341'");
        filter.Should().Contain("scope/entityType eq 'matter'");

        // Should be joined by ' and '
        filter.Split(" and ").Should().HaveCountGreaterOrEqualTo(4, because: "tenant + artifactType + predicate + scope clauses joined by AND");
    }

    [Fact]
    public void BuildFilter_WithSubjectScopeAndConfigFilter_BothApplied()
    {
        // Playbook authors can still supply ad-hoc OData via config.filter alongside subjectScope.
        var config = new IndexRetrieveNodeConfig
        {
            SubjectScope = "project:p-1",
            Filter = "asOf ge 2026-01-01T00:00:00Z"
        };
        var filter = IndexRetrieveNode.BuildFilter("tenant-acme", config);

        filter.Should().Contain("tenantId eq 'tenant-acme'");
        filter.Should().Contain("scope/entityType eq 'project'");
        filter.Should().Contain("scope/entityId eq 'p-1'");
        filter.Should().Contain("(asOf ge 2026-01-01T00:00:00Z)", because: "config.filter is wrapped in parens");
    }

    [Fact]
    public void BuildFilter_MalformedSubjectScope_DoesNotEmitScopeClause()
    {
        // When subjectScope is malformed and other narrowing dimensions exist, the filter
        // should still build (without the bad scope clause) rather than crash.
        var config = new IndexRetrieveNodeConfig
        {
            SubjectScope = "malformed-no-colon",
            ArtifactType = "observation"
        };
        var filter = IndexRetrieveNode.BuildFilter("tenant-acme", config);

        filter.Should().Contain("tenantId eq 'tenant-acme'");
        filter.Should().Contain("artifactType eq 'observation'");
        filter.Should().NotContain("scope/matterId");
        filter.Should().NotContain("scope/entityType");
    }

    // ─── Validate — subjectScope counts as a narrowing dimension ──────────────

    [Fact]
    public void Validate_WithSubjectScopeOnly_ReturnsSuccess()
    {
        var searchClient = new Moq.Mock<Azure.Search.Documents.Indexes.SearchIndexClient>();
        var openAi = new Moq.Mock<IOpenAiClient>();
        var node = new IndexRetrieveNode(
            searchClient.Object,
            openAi.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexRetrieveNode>.Instance);

        var context = InsightsNodeTestHelpers.CreateContext(
            ExecutorType.IndexRetrieve,
            """{ "subjectScope": "matter:M-2024-0341" }""");

        var result = node.Validate(context);

        result.IsValid.Should().BeTrue(
            because: "subjectScope is a narrowing dimension on its own per Wave D6 task 035");
    }
}
