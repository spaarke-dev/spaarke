// -----------------------------------------------------------------------------
// SearchIndexClientProvisionerTests.cs
//
// L2 CONTROL-PLANE unit tests for SearchIndexClientProvisioner (task 124,
// Wave G-2). Same fake-transport philosophy as ArmSubscriptionReadinessProbeTests
// / KeyVaultCertBootstrapProbeTests: builds a REAL SearchIndexClient against a
// fake HttpClientTransport (reuses the shared FakeArmHttpMessageHandler from
// ArmSubscriptionReadinessProbeTests.cs — internal, same test assembly) so the
// SDK's own request construction, URL building, and pipeline auth-header
// injection all run unmodified; only the HTTP boundary is faked. This directly
// satisfies acceptance criterion #1 ("All 7 canonical indexes are created/
// verified via SDK calls in a test against a fake/emulated Search service").
//
// SCOPE: pure unit tests — no live AI Search, no live network. ADR-038 path #1.
// Never Mock&lt;HttpMessageHandler&gt;: the fake IS a hand-rolled HttpMessageHandler
// subclass wrapped by the SDK's own HttpClientTransport, not a mocking-library
// wrapper around the SDK client itself.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SearchIndexClientProvisionerTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string Endpoint = "https://sprk-acme-search.search.windows.net";
    private const string IndexVersion = "manifest-abc123";

    private static readonly System.Collections.Immutable.ImmutableArray<string> CanonicalSeven =
        System.Collections.Immutable.ImmutableArray.Create(
            "spaarke-files-index",
            "spaarke-discovery-index",
            "spaarke-records-index",
            "spaarke-rag-references",
            "spaarke-insights-index",
            "spaarke-session-files",
            "spaarke-invoices-index");

    // ---------- Admin-key absence (acceptance criterion — RBAC only) ----------

    [Fact]
    public void ProvisionerSourceContainsNoAdminKeyHandling()
    {
        // Structural proof mirroring RestApiAiSearchIndexVerifier's own design
        // intent: grep the compiled provisioner's constructor + request path
        // for any admin-key vocabulary. This is exercised as an assembly-level
        // reflection check (no ctor parameter, no field, named anything
        // resembling admin-key) rather than a source-text grep, since the
        // acceptance criterion's grep runs at Step 9.5/CI over the .cs file
        // directly; this test is the runtime-shape mirror of that check.
        var type = typeof(SearchIndexClientProvisioner);
        var members = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Select(f => f.Name)
            .Concat(type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name!));

        members.Should().NotContain(n =>
            n.Contains("adminkey", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("apikey", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Embedded schema loading + comment-stripping ----------

    [Theory]
    [InlineData("spaarke-files-index.json")]
    [InlineData("spaarke-discovery-index.json")]
    [InlineData("spaarke-records-index.json")]
    [InlineData("spaarke-rag-references.json")]
    [InlineData("spaarke-insights-index.json")]
    [InlineData("spaarke-session-files.json")]
    [InlineData("spaarke-invoices-index.json")]
    public void LoadAndStripSchema_AllSevenCanonicalFiles_ProduceValidJsonWithNoCommentKeys(string resourceFileName)
    {
        var json = SearchIndexClientProvisioner.LoadAndStripSchema(resourceFileName);

        json.Should().NotContain("\"//", "comment keys must be stripped before PUT (Azure AI Search rejects unknown properties)");
        json.Should().NotContain("\"_comment_\"", "the insights-index underscore-comment convention must also be stripped");

        // Must still be valid, parseable JSON after stripping (proves the
        // regex-based strip didn't corrupt array/object structure).
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("name", out _).Should().BeTrue("every canonical schema declares a top-level 'name'");
        doc.RootElement.TryGetProperty("fields", out var fields).Should().BeTrue();
        fields.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void LoadAndStripSchema_UnknownResource_Throws()
    {
        var act = () => SearchIndexClientProvisioner.LoadAndStripSchema("does-not-exist.json");
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    // ---------- ProvisionAsync via real SearchIndexClient + fake transport ----------

    [Fact]
    public async Task ProvisionAsync_AllSevenIndexes_PutsEachIndexOverGenuineSearchIndexClientPipeline()
    {
        var putRequests = new List<Uri>();
        var handler = new FakeArmHttpMessageHandler(request =>
        {
            putRequests.Add(request.RequestUri!);
            request.Method.Should().Be(HttpMethod.Put, "the provisioner must PUT (create-or-update), not POST/GET");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provisioner = BuildProvisioner(handler);
        var request = BuildRequest(CanonicalSeven);

        var outcome = await provisioner.ProvisionAsync(request, CancellationToken.None);

        var success = outcome.Should().BeOfType<AiSearchIndexProvisionOutcome.Success>().Subject;
        success.ProvisionedIndexNames.Should().Equal(CanonicalSeven);

        putRequests.Should().HaveCount(7, "one genuine HTTP PUT per canonical index — not a hard-coded Success");
        foreach (var name in CanonicalSeven)
        {
            putRequests.Should().ContainSingle(u => u.AbsolutePath == $"/indexes/{name}",
                $"index '{name}' must be PUT to the real AI Search indexes endpoint");
        }
        putRequests.Should().OnlyContain(u => u.Query.Contains("api-version="),
            "every PUT must carry the configured api-version query parameter");
    }

    [Fact]
    public async Task ProvisionAsync_SingleIndex_SendsStrippedJsonBody()
    {
        string? capturedBody = null;
        var handler = new FakeArmHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var provisioner = BuildProvisioner(handler);
        var request = BuildRequest(System.Collections.Immutable.ImmutableArray.Create("spaarke-files-index"));

        var outcome = await provisioner.ProvisionAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<AiSearchIndexProvisionOutcome.Success>();
        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody.Should().NotContain("\"//", "the PUT body must be the comment-stripped JSON, not the raw embedded resource");
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("name").GetString().Should().Be("spaarke-files-index");
    }

    [Fact]
    public async Task ProvisionAsync_ServerRejectsPut_ReturnsFailureWithStatusAndBody()
    {
        var handler = new FakeArmHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{ "error": { "code": "InvalidField", "message": "unknown field 'foo'" } }"""),
        });
        var provisioner = BuildProvisioner(handler);
        var request = BuildRequest(System.Collections.Immutable.ImmutableArray.Create("spaarke-files-index"));

        var outcome = await provisioner.ProvisionAsync(request, CancellationToken.None);

        var failure = outcome.Should().BeOfType<AiSearchIndexProvisionOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("400");
        failure.Diagnostic.Should().Contain("unknown field");
    }

    [Fact]
    public async Task ProvisionAsync_UnknownCanonicalName_ReturnsFailure_DoesNotPut()
    {
        var putCount = 0;
        var handler = new FakeArmHttpMessageHandler(_ => { putCount++; return new HttpResponseMessage(HttpStatusCode.OK); });
        var provisioner = BuildProvisioner(handler);
        var request = BuildRequest(System.Collections.Immutable.ImmutableArray.Create("spaarke-nonexistent-index"));

        var outcome = await provisioner.ProvisionAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<AiSearchIndexProvisionOutcome.Failure>();
        putCount.Should().Be(0, "an unregistered canonical name must fail BEFORE any HTTP call (two-place-edit forcing function)");
    }

    // ---------- helpers ----------

    private static SearchIndexClientProvisioner BuildProvisioner(FakeArmHttpMessageHandler handler)
        => new(
            new FakeCredential(),
            Options.Create(new AiSearchIndexOptions { SearchApiVersion = "2024-07-01" }),
            new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) },
            NullLogger<SearchIndexClientProvisioner>.Instance);

    private static AiSearchIndexProvisionRequest BuildRequest(System.Collections.Immutable.ImmutableArray<string> indexNames)
        => new(
            CustomerId: CustomerId,
            TenantId: TenantId,
            EnvironmentName: "dev",
            SearchEndpoint: Endpoint,
            RequestedIndexNames: indexNames,
            IndexVersion: IndexVersion);

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-search-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
