using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Contract tests for SPE Admin search, pinning the three defects task 004 fixed.
/// </summary>
/// <remarks>
/// <para>
/// What breaks if these are deleted: the Search and Items screens go back to returning
/// <c>400 BadRequest "The call failed, please try again."</c> for every query, which is what they did
/// from the day they were written. Each defect below was reproduced against the live Spaarke Dev
/// tenant and each fix was verified there — see <c>notes/search-root-cause.md</c>.
/// </para>
/// <para>
/// The failures were invisible to the existing 359-test SpeAdmin suite because none of it makes an
/// HTTP call. These assert the wire request, which is where all three defects lived.
/// </para>
/// </remarks>
[Trait("Category", "SpeAdminGraphContract")]
public class SpeAdminSearchContractTests
{
    private const string ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string SearchQueryPath = "/search/query";

    // ─────────────────────────────────────────────────────────────────────────
    // D1 — container search must not use /search/query
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchContainers_DoesNotCallSearchQuery_BecauseGraphRejectsThatEntityType()
    {
        // THE regression guard. Graph does not expose `fileStorageContainer` to /search/query: a
        // request for it is indistinguishable from a request for a nonexistent entity type, on beta
        // and v1.0, with and without region. Routing container search back through /search/query
        // returns 400 for every query, forever.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");
        graph.StubPost(SearchQueryPath, """{"value":[]}""");

        await CreateSut().SearchContainersAsync(graph.CreateGraphClient(), ContainerTypeId, "Test", 25, null);

        graph.RequestsFor(SearchQueryPath).Should().BeEmpty("`fileStorageContainer` is not a searchable entity type");
        graph.RequestsFor(ContainersPath).Should().ContainSingle();
    }

    [Fact]
    public async Task SearchContainers_FiltersOnBothNameAndDescription_WithAnUnquotedContainerTypeGuid()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        await CreateSut().SearchContainersAsync(graph.CreateGraphClient(), ContainerTypeId, "Test", 25, null);

        var filter = Uri.UnescapeDataString(graph.RequestsFor(ContainersPath).Single().RawQuery);
        filter.Should().Contain($"containerTypeId eq {ContainerTypeId}");
        filter.Should().NotContain($"'{ContainerTypeId}'", "containerTypeId is Edm.Guid — a quoted literal 400s");
        filter.Should().Contain("contains(displayName,'Test')");
        filter.Should().Contain("contains(description,'Test')");
    }

    [Fact]
    public async Task SearchContainers_MapsContainersOntoResults()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, $$"""
        {"value":[{"id":"b!abc","displayName":"API Test","description":"d","containerTypeId":"{{ContainerTypeId}}"}]}
        """);

        var page = await CreateSut().SearchContainersAsync(graph.CreateGraphClient(), ContainerTypeId, "Test", 25, null);

        var hit = page.Items.Should().ContainSingle().Subject;
        hit.Id.Should().Be("b!abc");
        hit.DisplayName.Should().Be("API Test");
        hit.Description.Should().Be("d");
    }

    [Fact]
    public async Task SearchContainers_WhenNothingMatches_ReturnsEmptyRatherThanFailing()
    {
        // Acceptance criterion: a legitimate no-match must be distinguishable from a failure.
        // Verified live — Graph answers 200 {"value":[]}, so this is a real empty, not a swallowed error.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        var page = await CreateSut().SearchContainersAsync(graph.CreateGraphClient(), ContainerTypeId, "zzznomatch", 25, null);

        page.Items.Should().BeEmpty();
        page.NextSkipToken.Should().BeNull();
    }

    [Fact]
    public async Task SearchContainers_WhenGraphReportsNoNextLink_ReportsNoNextPage()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[{"id":"b!a","displayName":"A"}]}""");

        var page = await CreateSut().SearchContainersAsync(graph.CreateGraphClient(), ContainerTypeId, "A", 25, null);

        page.NextSkipToken.Should().BeNull();
        page.TotalCount.Should().BeNull(
            "the containers endpoint reports no total; inventing one would make the last page look complete");
    }

    [Fact]
    public async Task SearchContainers_WhenGraphReturnsANextLink_SurfacesOnlyTheOpaqueSkipToken()
    {
        // The token must not be the whole nextLink — that would hand the browser a fully-formed Graph
        // URL including host and filter.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """
        {"value":[{"id":"b!a","displayName":"A"}],
         "@odata.nextLink":"https://graph.microsoft.com/beta/storage/fileStorage/containers?$filter=x&$top=25&$skiptoken=OPAQUE123"}
        """);

        var page = await CreateSut().SearchContainersAsync(graph.CreateGraphClient(), ContainerTypeId, "A", 25, null);

        page.NextSkipToken.Should().Be("OPAQUE123");
        page.NextSkipToken.Should().NotContain("graph.microsoft.com");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OData literal escaping
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("O'Brien", "O''Brien")]
    [InlineData("a') or (1 eq 1", "a'') or (1 eq 1")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void EscapeODataStringLiteral_DoublesQuotes(string input, string expected)
    {
        // The search term is interpolated into a single-quoted OData literal. Unescaped, an
        // apostrophe closes the literal early — so a container named "O'Brien Matter" would 400 the
        // whole screen, and a crafted term could append clauses to the filter.
        SpeAdminGraphService.EscapeODataStringLiteral(input).Should().Be(expected);
    }

    [Fact]
    public async Task SearchContainers_WhenTermContainsAnApostrophe_EscapesItInTheFilter()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        await CreateSut().SearchContainersAsync(graph.CreateGraphClient(), ContainerTypeId, "O'Brien", 25, null);

        var filter = Uri.UnescapeDataString(graph.RequestsFor(ContainersPath).Single().RawQuery);
        filter.Should().Contain("contains(displayName,'O''Brien')");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // D2 / D3 — item search
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchItems_SendsRegion_WhichAppOnlyGraphRequires()
    {
        // Without it: 400 "Region is required when request with application permission."
        using var graph = new GraphWireMockFixture();
        graph.StubPost(SearchQueryPath, EmptyHits);

        await CreateSut().SearchItemsAsync(graph.CreateGraphClient(), "a", null, null, 25, null);

        var body = graph.RequestsFor(SearchQueryPath).Single().BodyAsJson();
        body.GetProperty("requests")[0].GetProperty("region").GetString()
            .Should().Be(SpeAdminGraphService.DefaultSearchRegion);
    }

    [Fact]
    public async Task SearchItems_DoesNotSendContentSources_WhichGraphAllowsOnlyForExternalItem()
    {
        // Setting it produced 400 "Content Source is required only for ExternalItem" — so this fired
        // even for a container-scoped search that had region set correctly.
        using var graph = new GraphWireMockFixture();
        graph.StubGet("/storage/fileStorage/containers/", """{"id":"drive-1"}""");
        graph.StubPost(SearchQueryPath, EmptyHits);

        await CreateSut().SearchItemsAsync(graph.CreateGraphClient(), "a", "b!container", null, 25, null);

        var request = graph.RequestsFor(SearchQueryPath).Single().BodyAsJson().GetProperty("requests")[0];
        request.TryGetProperty("contentSources", out _).Should().BeFalse(
            "contentSources is valid only for externalItem; Graph 400s a driveItem request that carries it");
    }

    [Fact]
    public async Task SearchItems_SearchesDriveItemsWithTheDocumentedFieldSet()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(SearchQueryPath, EmptyHits);

        await CreateSut().SearchItemsAsync(graph.CreateGraphClient(), "a", null, null, 25, null);

        var request = graph.RequestsFor(SearchQueryPath).Single().BodyAsJson().GetProperty("requests")[0];
        request.GetProperty("entityTypes").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("driveItem");
        request.GetProperty("fields").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("id", "name", "size", "lastModifiedDateTime", "webUrl", "file", "parentReference");
    }

    [Fact]
    public async Task SearchItems_WhenScopedToAContainer_DropsHitsFromOtherDrives()
    {
        // Graph cannot scope a driveItem search server-side, so hits are filtered by driveId here.
        // Without this the admin is shown files from containers they did not ask about.
        using var graph = new GraphWireMockFixture();
        graph.StubGet("/storage/fileStorage/containers/", """{"id":"drive-wanted"}""");
        graph.StubPost(SearchQueryPath, """
        {"value":[{"hitsContainers":[{"total":2,"hits":[
          {"hitId":"1","resource":{"@odata.type":"#microsoft.graph.driveItem","id":"i1","name":"keep.txt","parentReference":{"driveId":"drive-wanted"}}},
          {"hitId":"2","resource":{"@odata.type":"#microsoft.graph.driveItem","id":"i2","name":"other.txt","parentReference":{"driveId":"drive-other"}}}
        ]}]}]}
        """);

        var page = await CreateSut().SearchItemsAsync(graph.CreateGraphClient(), "a", "b!container", null, 25, null);

        page.Items.Should().ContainSingle().Which.Name.Should().Be("keep.txt");
    }

    [Fact]
    public async Task SearchItems_WhenNotScopedToAContainer_KeepsHitsFromEveryDrive()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(SearchQueryPath, """
        {"value":[{"hitsContainers":[{"total":2,"hits":[
          {"hitId":"1","resource":{"@odata.type":"#microsoft.graph.driveItem","id":"i1","name":"a.txt","parentReference":{"driveId":"drive-a"}}},
          {"hitId":"2","resource":{"@odata.type":"#microsoft.graph.driveItem","id":"i2","name":"b.txt","parentReference":{"driveId":"drive-b"}}}
        ]}]}]}
        """);

        var page = await CreateSut().SearchItemsAsync(graph.CreateGraphClient(), "a", null, null, 25, null);

        page.Items.Select(i => i.Name).Should().BeEquivalentTo("a.txt", "b.txt");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private const string EmptyHits = """{"value":[{"hitsContainers":[{"total":0,"hits":[]}]}]}""";

    private static SpeAdminGraphService CreateSut()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://unused.invalid",
            })
            .Build();

        return new SpeAdminGraphService(
            httpClientFactory: new UnusedHttpClientFactory(),
            secretClient: new SecretClient(new Uri("https://unused.invalid/"), new UnusableCredential()),
            dataverseClient: new DataverseWebApiClient(configuration, NullLogger<DataverseWebApiClient>.Instance),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: null);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(
            $"A method under test requested the '{name}' HttpClient — the call is leaving the fixture.");
    }

    private sealed class UnusableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("These tests must never authenticate against a real service.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("These tests must never authenticate against a real service.");
    }
}
