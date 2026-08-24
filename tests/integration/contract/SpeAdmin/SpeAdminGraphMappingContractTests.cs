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
/// Contract tests for the SpeAdmin ↔ Microsoft Graph HTTP exchange, run against
/// <see cref="GraphWireMockFixture"/>.
/// </summary>
/// <remarks>
/// <para>
/// What breaks if these are deleted: every request SpeAdmin sends to Graph goes back to having no
/// automated coverage of its wire shape. The defect class this defends against (spec §3.2) is a
/// property name in a request that does not exist on the API — <c>majorVersionLimit</c> for
/// <c>itemMajorVersionLimit</c>, <c>storageUsedInBytes</c> for <c>maxStoragePerContainerInBytes</c>.
/// Those cost nothing to write, fail silently at runtime, and no test in the 359-test SpeAdmin suite
/// could see them, because not one of those tests makes an HTTP call.
/// </para>
/// <para>
/// These run fully offline: WireMock binds a loopback port and the Graph client authenticates
/// anonymously, so there is no tenant, no credential, and no network egress.
/// </para>
/// </remarks>
[Trait("Category", "SpeAdminGraphContract")]
public class SpeAdminGraphMappingContractTests
{
    private const string ContainerTypeId = "3c2f1e9a-7b64-4a1d-9f0e-2d8c5b41a6e7";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string DeletedContainersPath = "/storage/fileStorage/deletedContainers";

    // ─────────────────────────────────────────────────────────────────────────
    // Container list — request shape
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListContainers_RequestsTheDocumentedSelectFieldSet()
    {
        // Arrange
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        // Act
        await CreateSut().ListContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        // Assert — the exact field set, as a set. A rename or a dropped field fails here.
        graph.SelectFieldsFor(ContainersPath).Should().BeEquivalentTo(
            "id", "displayName", "description", "containerTypeId",
            "createdDateTime", "storageUsedInBytes", "status");
    }

    [Fact]
    public async Task ListContainers_FiltersOnContainerTypeIdWithAnUnquotedGuid()
    {
        // Arrange — containerTypeId is Edm.Guid in Graph; a quoted literal is Edm.String and 400s.
        // This is the same class of defect that made the Audit Log screen fail against Dataverse.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        // Act
        await CreateSut().ListContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        // Assert
        var query = Uri.UnescapeDataString(graph.RequestsFor(ContainersPath).Single().RawQuery);
        query.Should().Contain($"containerTypeId eq {ContainerTypeId}");
        query.Should().NotContain($"'{ContainerTypeId}'", "a quoted GUID is Edm.String and Graph rejects it");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Container list — response mapping
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListContainers_MapsDocumentedGraphPropertiesOntoTheDomainModel()
    {
        // Arrange — property names here are Graph's, not ours. That is the point: if production
        // reads a different name, the mapped value comes back empty and this fails.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, $$"""
        {
          "value": [
            {
              "id": "b!container-one",
              "displayName": "Matter Files",
              "description": "Working documents",
              "containerTypeId": "{{ContainerTypeId}}",
              "createdDateTime": "2026-03-04T09:15:00Z",
              "status": "active"
            }
          ]
        }
        """);

        // Act
        var result = await CreateSut().ListContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        // Assert
        var container = result.Should().ContainSingle().Subject;
        container.Id.Should().Be("b!container-one");
        container.DisplayName.Should().Be("Matter Files");
        container.Description.Should().Be("Working documents");
        container.ContainerTypeId.Should().Be(ContainerTypeId);
        container.CreatedDateTime.Should().Be(DateTimeOffset.Parse("2026-03-04T09:15:00Z"));
        container.Status.Should().Be("active");
    }

    [Fact]
    public async Task ListContainers_WhenGraphOmitsStatus_DefaultsToActive()
    {
        // Arrange — status arrives via AdditionalData, so its absence is a real branch, not a
        // language guarantee.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[{"id":"b!c","displayName":"No status"}]}""");

        // Act
        var result = await CreateSut().ListContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        // Assert
        result.Should().ContainSingle().Which.Status.Should().Be("active");
    }

    [Fact]
    public async Task ListContainers_AlwaysReportsStorageUsedAsNull_PinningTheKnownDefect()
    {
        // ⚠️ CHARACTERIZATION TEST — this pins a DEFECT, not desired behavior.
        //
        // SpeAdminGraphService.cs:645 hardcodes `StorageUsedInBytes: null` even though the $select
        // asks for the field, so the Storage tile is silently blank for every container (spec §3.2).
        // The field name is wrong too: `storageUsedInBytes` is a quota CEILING on the container type
        // (`maxStoragePerContainerInBytes` in v1.0), not consumption.
        //
        // Task 024 decides whether Graph exposes consumption at all and either implements it or
        // removes the tile. WHEN THAT LANDS, THIS TEST MUST FAIL AND BE UPDATED — that failure is
        // the point. Deleting it instead would restore the silence this project exists to end.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """
        {"value":[{"id":"b!c","displayName":"Has quota","storageUsedInBytes":1099511627776}]}
        """);

        // Act
        var result = await CreateSut().ListContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        // Assert
        result.Should().ContainSingle().Which.StorageUsedInBytes.Should().BeNull(
            "SpeAdminGraphService.cs:645 discards the value — task 024 owns the fix");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deleted containers (recycle bin) — the deletedDateTime site
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeletedContainers_SendsNoSelect_SoTheProjectionCannotDriftFromTheCode()
    {
        // UPDATED by task 022 (2026-08-24). This previously pinned a hand-maintained $select of
        // "id", "displayName", "containerTypeId", "deletedDateTime".
        //
        // The $select was removed rather than corrected, for the same reason as the container-type
        // list: a hand-maintained list of property names is a standing liability. A wrong or
        // version-absent name is a hard 400 that breaks the whole view (as `storageUsedInBytes` does
        // on v1.0), and a name the list simply forgot silently withholds the property from every
        // caller (as `owningAppId` was on the container-type surface). The default projection cannot
        // drift out of sync with itself.
        //
        // The filter still has to survive — it is what scopes the view to one container type.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(DeletedContainersPath, """{"value":[]}""");

        await CreateSut().ListDeletedContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        var query = Uri.UnescapeDataString(graph.RequestsFor(DeletedContainersPath).Single().RawQuery);

        query.Should().NotContain("$select",
            "the projection is the default one — see the comment in ListDeletedContainersAsync");
        query.Should().Contain(ContainerTypeId,
            "the view must stay scoped to the requested container type");
        query.Should().NotContain($"'{ContainerTypeId}'",
            "containerTypeId is Edm.Guid — quoting it makes Graph reject the filter (ADR-044)");
    }

    [Fact]
    public async Task ListDeletedContainers_MapsTheDeletionTimestamp_NotJustIdAndDisplayName()
    {
        // 🔴 REGRESSION GUARD — this was a characterization test pinning a live defect, inverted by
        // task 022 (2026-08-24) when the defect was fixed. Task 040 wrote it saying "WHEN 022 FIXES
        // THIS, THIS TEST MUST FAIL AND BE UPDATED", and it did.
        //
        // The defect: deletedDateTime is not typed on FileStorageContainer, so production reads it
        // from AdditionalData — correctly — but guarded the read with `rawDeletedAt is string`.
        // Kiota stores a System.DateTime there (pinned separately below). The guard could never be
        // true, so DeletedDateTime was null for EVERY row: the recycle bin could not sort by deletion
        // date or age anything out, and "deleted at an unknown time" was indistinguishable from
        // "deleted just now".
        //
        // Asserting the exact value, not merely non-null: a fix that produced DateTime.UtcNow would
        // also satisfy "not null" while being just as wrong.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(DeletedContainersPath, $$"""
        {
          "value": [
            {
              "id": "b!deleted-one",
              "displayName": "Closed Matter",
              "containerTypeId": "{{ContainerTypeId}}",
              "deletedDateTime": "2026-08-01T17:45:00Z"
            }
          ]
        }
        """);

        var result = await CreateSut().ListDeletedContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        var deleted = result.Should().ContainSingle().Subject;
        deleted.Id.Should().Be("b!deleted-one");
        deleted.DisplayName.Should().Be("Closed Matter");
        deleted.ContainerTypeId.Should().Be(ContainerTypeId);
        deleted.DeletedDateTime.Should().Be(
            new DateTimeOffset(2026, 8, 1, 17, 45, 0, TimeSpan.Zero),
            "Graph sent this value and Kiota parsed it — dropping it on a type check is the defect");
    }

    [Fact]
    public async Task ListDeletedContainers_WhenGraphOmitsTheTimestamp_LeavesItNull()
    {
        // The other half of the fix. Unknown must stay unknown: substituting "now" for a missing
        // deletion date makes an aged-out container look freshly deleted, which is how a retention
        // sweep skips the very rows it exists to catch.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(DeletedContainersPath, $$"""
        {"value":[{"id":"b!no-date","displayName":"Undated","containerTypeId":"{{ContainerTypeId}}"}]}
        """);

        var result = await CreateSut().ListDeletedContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        result.Should().ContainSingle().Which.DeletedDateTime.Should().BeNull();
    }

    [Fact]
    public async Task DeletedContainerPayload_StoresTheTimestampAsDateTimeNotString()
    {
        // The root cause behind the characterization test above, pinned separately so a fix is
        // aimed at the right line. The value IS delivered — Graph sent it, Kiota parsed it, it is
        // sitting in AdditionalData. Production drops it on a type check, not on a missing field.
        // Asserting the runtime type here means that if a future Kiota upgrade changes the
        // representation, whoever fixes 022 finds out from this test rather than from production.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(DeletedContainersPath, """
        {"value":[{"id":"b!d","displayName":"X","deletedDateTime":"2026-08-01T17:45:00Z"}]}
        """);

        // Act — the raw SDK call, deliberately not through production mapping
        var page = await graph.CreateGraphClient().Storage.FileStorage.DeletedContainers.GetAsync();

        // Assert
        var additionalData = page!.Value!.Single().AdditionalData;
        additionalData.Should().ContainKey("deletedDateTime");
        additionalData["deletedDateTime"].Should().BeOfType<DateTime>(
            "production tests `raw is string`, so anything else silently yields a null timestamp");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The demonstrator — proof the detector actually fires (spec success criterion 18)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WrongPropertyNameInSelect_FailsTheSuite()
    {
        // A mechanism that cannot be shown to fail is not a mechanism. This runs the same assertion
        // the tests above use, against the field set a developer WOULD have written under the §3.2
        // defect (`majorVersionLimit` in place of the real name), and proves it throws.
        //
        // Asserting the failure rather than leaving a red test is what keeps CI honest: the
        // demonstrator lives in the green suite and still proves the detector works.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");
        await CreateSut().ListContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        var assertWithADefectiveFieldSet = () =>
            graph.SelectFieldsFor(ContainersPath).Should().BeEquivalentTo(
                "id", "displayName", "description", "containerTypeId",
                "createdDateTime", "majorVersionLimit", "status");

        assertWithADefectiveFieldSet.Should().Throw<Exception>(
            "a $select field name that production does not send must fail the suite");
    }

    [Fact]
    public async Task RequestToAnUnexercisedPath_FailsLoudlyRatherThanSilentlyPassing()
    {
        // Guards the fixture itself. If a production method stopped calling Graph, an assertion over
        // "the requests we saw" would otherwise vacuously pass over an empty list — the exact
        // absent-reads-as-success shape this project keeps finding.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");
        await CreateSut().ListContainersAsync(graph.CreateGraphClient(), ContainerTypeId);

        var readingAPathNobodyCalled = () => graph.SelectFieldsFor("/storage/fileStorage/nothingHere");

        readingAPathNobodyCalled.Should().Throw<InvalidOperationException>()
            .WithMessage("*No request was made*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUT construction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SpeAdminGraphService"/> for the 47 methods that take a
    /// <c>GraphServiceClient</c> parameter.
    /// </summary>
    /// <remarks>
    /// The Key Vault and Dataverse dependencies are constructor-required but unreachable from these
    /// methods — they serve <c>GetClientForConfigAsync</c>, which the tests bypass by supplying the
    /// client directly. Both are wired to credentials that throw if anything ever calls them, so a
    /// future change that makes one of these paths reach outward fails loudly instead of quietly
    /// trying to authenticate.
    /// </remarks>
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

    /// <summary>Fails loudly if a code path under test ever tries to build its own HTTP client.</summary>
    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(
            $"A method under test requested the '{name}' HttpClient. These tests supply the " +
            "GraphServiceClient directly; reaching the factory means the call is leaving the fixture.");
    }

    /// <summary>Fails loudly if a code path under test ever tries to acquire a real token.</summary>
    private sealed class UnusableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("These tests must never authenticate against a real service.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("These tests must never authenticate against a real service.");
    }
}
