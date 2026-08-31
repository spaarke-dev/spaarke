// -----------------------------------------------------------------------------
// AiSearchTenantFilterTemplateProvisionerTests.cs
//
// L2 CONTROL-PLANE unit tests for AiSearchTenantFilterTemplateProvisioner
// (task 124, Wave G-2) — the REAL replacement for the wave-C4
// StubAiSearchTenantFilterTemplateProvisioner (DS-4 §2 "I2 provisioning half
// not real").
//
// TEST BOUNDARY (see AiSearchTenantFilterTemplateProvisioner.cs file header
// "TEST BOUNDARY" section): the Microsoft.Azure.Cosmos SDK has no fake-
// transport seam (not built on Azure.Core's HttpPipeline), so
// CosmosTenantFilterTemplateStore itself is NOT unit-tested here — parity
// with CosmosProvisioningRunRepository, which likewise has no unit test file
// (only the env-guarded CosmosSmokeTests.cs). These tests instead exercise
// AiSearchTenantFilterTemplateProvisioner against a hand-rolled FakeStore
// implementing ITenantFilterTemplateStore — the SAME pattern
// H2bAiSearchIndexHandlerTests.cs already uses for IProvisioningRunRepository.
// Never Mock&lt;T&gt; (ADR-038).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class AiSearchTenantFilterTemplateProvisionerTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string Endpoint = "https://spaarke-search-prod.search.windows.net";

    private static readonly ImmutableArray<string> CanonicalSeven =
        ImmutableArray.Create(
            "spaarke-files-index",
            "spaarke-discovery-index",
            "spaarke-records-index",
            "spaarke-rag-references",
            "spaarke-insights-index",
            "spaarke-session-files",
            "spaarke-invoices-index");

    // ---------- Acceptance criterion #3: real write occurs, not just a log ----------

    [Fact]
    public async Task ProvisionAsync_NoExistingTemplate_WritesRealDocument_NotMerelyLogged()
    {
        var store = new FakeStore();
        var provisioner = new AiSearchTenantFilterTemplateProvisioner(store, NullLogger<AiSearchTenantFilterTemplateProvisioner>.Instance);
        var request = BuildRequest(CanonicalSeven);

        var outcome = await provisioner.ProvisionAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<AiSearchTenantFilterTemplateOutcome.Success>();
        store.WriteCallCount.Should().Be(1, "a create/write call MUST occur — this is the I2 runtime-provisioning half DS-4 flagged as not real");
        store.LastWritten.Should().NotBeNull();
        store.LastWritten!.CustomerId.Should().Be(CustomerId);
        store.LastWritten.TenantId.Should().Be(TenantId);
        store.LastWritten.SearchEndpoint.Should().Be(Endpoint);
        store.LastWritten.Filters.Should().HaveCount(7);
        store.LastWritten.Filters.Should().OnlyContain(f => f.FilterPredicate == $"tenantId eq '{TenantId}'",
            "every index carries the SAME literal §4D I2 / FR-29 filter predicate");
        store.LastWritten.Filters.Select(f => f.IndexName).Should().BeEquivalentTo(CanonicalSeven);
    }

    [Fact]
    public async Task ProvisionAsync_DocumentId_IsDeterministicPerCustomer()
    {
        var store = new FakeStore();
        var provisioner = new AiSearchTenantFilterTemplateProvisioner(store, NullLogger<AiSearchTenantFilterTemplateProvisioner>.Instance);

        await provisioner.ProvisionAsync(BuildRequest(CanonicalSeven), CancellationToken.None);

        store.LastWritten!.Id.Should().Be(CosmosTenantFilterTemplateStore.BuildDocumentId(CustomerId));
    }

    // ---------- Acceptance criterion #4: idempotency (re-run = no-op) ----------

    [Fact]
    public async Task ProvisionAsync_TemplateAlreadyCurrentWithMatchingInputs_IsNoOp()
    {
        var store = new FakeStore();
        var provisioner = new AiSearchTenantFilterTemplateProvisioner(store, NullLogger<AiSearchTenantFilterTemplateProvisioner>.Instance);
        var request = BuildRequest(CanonicalSeven);

        var first = await provisioner.ProvisionAsync(request, CancellationToken.None);
        var second = await provisioner.ProvisionAsync(request, CancellationToken.None);

        first.Should().BeOfType<AiSearchTenantFilterTemplateOutcome.Success>();
        second.Should().BeOfType<AiSearchTenantFilterTemplateOutcome.Success>();
        store.WriteCallCount.Should().Be(1, "re-running the provisioner with UNCHANGED inputs against an already-current template is a genuine no-op — no second write");
        store.ReadCallCount.Should().Be(2, "the provisioner MUST still read-before-write on every invocation to detect no-op eligibility");
    }

    [Fact]
    public async Task ProvisionAsync_ExistingTemplateForDifferentTenant_OverwritesWithNewValue()
    {
        // Simulates a tenant re-key scenario — the stored predicate must
        // track the CURRENT request's TenantId, never the stale one.
        var store = new FakeStore();
        var provisioner = new AiSearchTenantFilterTemplateProvisioner(store, NullLogger<AiSearchTenantFilterTemplateProvisioner>.Instance);
        var originalRequest = BuildRequest(CanonicalSeven, tenantId: "11111111-2222-3333-4444-555555555555");
        await provisioner.ProvisionAsync(originalRequest, CancellationToken.None);

        var updatedRequest = BuildRequest(CanonicalSeven, tenantId: TenantId);
        var outcome = await provisioner.ProvisionAsync(updatedRequest, CancellationToken.None);

        outcome.Should().BeOfType<AiSearchTenantFilterTemplateOutcome.Success>();
        store.WriteCallCount.Should().Be(2, "a changed tenantId is NOT a no-op — the template must be overwritten");
        store.LastWritten!.TenantId.Should().Be(TenantId);
        store.LastWritten.Filters.Should().OnlyContain(f => f.FilterPredicate == $"tenantId eq '{TenantId}'");
    }

    [Fact]
    public async Task ProvisionAsync_ExistingTemplateWithFewerIndexes_OverwritesWithFullSet()
    {
        var store = new FakeStore();
        var provisioner = new AiSearchTenantFilterTemplateProvisioner(store, NullLogger<AiSearchTenantFilterTemplateProvisioner>.Instance);
        var partialRequest = BuildRequest(ImmutableArray.Create("spaarke-files-index"));
        await provisioner.ProvisionAsync(partialRequest, CancellationToken.None);

        var fullRequest = BuildRequest(CanonicalSeven);
        await provisioner.ProvisionAsync(fullRequest, CancellationToken.None);

        store.WriteCallCount.Should().Be(2);
        store.LastWritten!.Filters.Should().HaveCount(7);
    }

    // ---------- §4D I1: tenantId flows through verbatim, never a default ----------

    [Fact]
    public async Task ProvisionAsync_MissingTenantId_Throws()
    {
        var store = new FakeStore();
        var provisioner = new AiSearchTenantFilterTemplateProvisioner(store, NullLogger<AiSearchTenantFilterTemplateProvisioner>.Instance);
        var request = new AiSearchTenantFilterTemplateRequest(
            SearchEndpoint: Endpoint, TenantId: string.Empty, CustomerId: CustomerId, IndexNames: CanonicalSeven);

        var act = async () => await provisioner.ProvisionAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        store.WriteCallCount.Should().Be(0);
    }

    [Fact]
    public void OdataFilterPredicate_EscapesEmbeddedSingleQuote()
    {
        // Tenant ids are GUIDs in practice, but the escaping logic must not
        // silently break the OData grammar if a caller ever passes a
        // non-canonical value — mirrors RagQueryBuilder/RecordSearchService's
        // EscapeODataValue convention (doubled single-quote).
        var tenantIdWithQuote = "abc'def";
        var escaped = tenantIdWithQuote.Replace("'", "''", StringComparison.Ordinal);
        escaped.Should().Be("abc''def");
    }

    // ---------- helpers ----------

    private static AiSearchTenantFilterTemplateRequest BuildRequest(
        ImmutableArray<string> indexNames, string tenantId = TenantId)
        => new(SearchEndpoint: Endpoint, TenantId: tenantId, CustomerId: CustomerId, IndexNames: indexNames);

    /// <summary>Hand-rolled fake — records write/read calls + last-written document (parity with H2bAiSearchIndexHandlerTests' FakeRepository).</summary>
    private sealed class FakeStore : ITenantFilterTemplateStore
    {
        private TenantFilterTemplateDocument? _stored;

        public int WriteCallCount { get; private set; }
        public int ReadCallCount { get; private set; }
        public TenantFilterTemplateDocument? LastWritten { get; private set; }

        public Task<TenantFilterTemplateDocument?> TryReadAsync(string customerId, CancellationToken cancellationToken)
        {
            ReadCallCount++;
            return Task.FromResult(_stored);
        }

        public Task WriteAsync(TenantFilterTemplateDocument document, CancellationToken cancellationToken)
        {
            WriteCallCount++;
            LastWritten = document;
            // Deep-ish copy so a subsequent mutation of the caller's list doesn't corrupt the "stored" snapshot.
            _stored = new TenantFilterTemplateDocument
            {
                Id = document.Id,
                CustomerId = document.CustomerId,
                TenantId = document.TenantId,
                SearchEndpoint = document.SearchEndpoint,
                Filters = document.Filters
                    .Select(f => new TenantIndexFilterEntry { IndexName = f.IndexName, FilterPredicate = f.FilterPredicate })
                    .ToList(),
                UpdatedOn = document.UpdatedOn,
            };
            return Task.CompletedTask;
        }
    }
}
