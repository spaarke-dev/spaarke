using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the per-container storage quota surface and the settings write-verification (spec FR-E02,
/// task 051).
/// </summary>
/// <remarks>
/// <para>
/// <b>The finding these defend.</b> FR-E02 asked for a per-container storage ceiling. Graph has none:
/// <c>maxStoragePerContainerInBytes</c> exists only on <c>fileStorageContainerTypeSettings</c> (the
/// container TYPE) and is absent from <c>fileStorageContainerSettings</c> on both API versions.
/// Measured live 2026-08-27 on a throwaway container, <c>PATCH /containers/{id}</c> carrying that
/// property — both nested under <c>settings</c> and top-level — returns <b>200 OK and silently
/// discards it</b>; the read-back has no such property.
/// </para>
/// <para>
/// So the shipped shape is: the ceiling is authored on the TYPE (one value for every container of
/// that type), and each container exposes a READ-ONLY quota from its drive. These tests pin both
/// halves, plus the verification that stops a silently-dropped settings write from being reported as
/// a success. Full evidence: notes/task-051-findings.md.
/// </para>
/// </remarks>
public class SpeAdminContainerQuotaContractTests
{
    private const string ContainerId = "b!DcvTfUkibESq94RyGJFs-UhqWZU646tBrEagKKMKiOc";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string ContainerTypesPath = "/storage/fileStorage/containerTypes";
    private const string ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06";

    /// <summary>
    /// A real quota facet. Values copied from a live response on Spaarke Dev so the numbers are
    /// internally consistent — note <c>remaining</c> is NOT <c>total - used</c>, because
    /// <c>deleted</c> still counts against the quota.
    /// </summary>
    private const string ContainerWithQuota = """
        {
          "id": "b!DcvTfUkibESq94RyGJFs-UhqWZU646tBrEagKKMKiOc",
          "displayName": "Spaarke Inc",
          "status": "active",
          "drive": {
            "webUrl": "https://contoso.sharepoint.com/contentstorage/CSP_x/Documents",
            "quota": {
              "total": 27487790694400,
              "used": 868906006,
              "remaining": 27486921785062,
              "deleted": 3332,
              "state": "normal"
            }
          }
        }
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // The request
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_ExpandsTheDriveQuota_BecauseTheContainerHasNoStorageProperty()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", ContainerWithQuota);

        await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        var query = Uri.UnescapeDataString(
            graph.RequestsFor($"{ContainersPath}/{ContainerId}").Single().RawQuery);

        query.Should().Contain("quota",
            "fileStorageContainer exposes no storage property in either API version — the quota lives " +
            "on the drive navigation property, so dropping it from the expand silently empties the " +
            "whole storage surface rather than failing");

        query.Should().Contain("webUrl",
            "the drive expand serves FR-C10 as well — narrowing it to quota alone would silently " +
            "remove the container URL (task 028)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The mapping
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_MapsEveryQuotaField_FromTheExpandedDrive()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", ContainerWithQuota);

        var container = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        container!.Quota.Should().NotBeNull();
        container.Quota!.Total.Should().Be(27_487_790_694_400L);
        container.Quota.Used.Should().Be(868_906_006L);
        container.Quota.Remaining.Should().Be(27_486_921_785_062L);
        container.Quota.Deleted.Should().Be(3_332L);
        container.Quota.State.Should().Be("normal");
    }

    /// <summary>
    /// <c>remaining</c> comes from Graph, not from arithmetic.
    /// </summary>
    /// <remarks>
    /// Deleted items still count against the quota, so <c>total - used</c> disagrees with Graph
    /// whenever a recycle bin is non-empty — and a locally-computed figure would look just as
    /// authoritative while being wrong. This asserts the gap explicitly so nobody "simplifies" the
    /// mapper into a subtraction.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_TakesRemainingFromGraph_NotFromTotalMinusUsed()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", ContainerWithQuota);

        var quota = (await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId))!.Quota!;

        (quota.Total!.Value - quota.Used!.Value).Should().NotBe(quota.Remaining!.Value,
            "this fixture carries deleted bytes, so the arithmetic and Graph's own figure differ — " +
            "which is the point");
        quota.Remaining.Should().Be(27_486_921_785_062L);
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_WhenGraphOmitsTheDrive_LeavesQuotaNull_RatherThanZeroed()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {"id":"{{ContainerId}}","displayName":"No drive returned","status":"active"}
            """);

        var container = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        // A record of nulls would be indistinguishable from "quota reported, all values unknown".
        // Null for the whole facet says "not reported" once, clearly (spec NFR-06).
        container!.Quota.Should().BeNull();
    }

    /// <summary>
    /// The quota is the ONLY consumption figure a single-container fetch can carry.
    /// </summary>
    /// <remarks>
    /// <c>storageUsedInBytes</c> is beta-only AND list-only — absent from GET-single even with an
    /// explicit <c>$select</c> (tasks 020/024). This fixture reproduces that: no
    /// <c>storageUsedInBytes</c>, but a populated quota. If the quota expand is ever dropped, the
    /// detail view loses consumption entirely, and this test says so.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_QuotaUsed_IsAvailableWhereStorageUsedInBytesIsNot()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", ContainerWithQuota);

        var container = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        container!.StorageUsedInBytes.Should().BeNull(
            "Graph does not return storageUsedInBytes on a single-container GET (tasks 020/024)");
        container.Quota!.Used.Should().Be(868_906_006L,
            "so the quota facet is the detail view's only source of consumption");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Write verification — the regression guard for the whole finding
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 A success response that does not reflect the write MUST NOT be reported as success.
    /// </summary>
    /// <remarks>
    /// This reproduces the exact shape measured live on the container-scope quota PATCH: Graph answers
    /// 200, returns a populated settings object carrying the OTHER fields, and simply omits the one
    /// that was set. Neither "was it 2xx?" nor "did settings come back?" catches that — only comparing
    /// what was asked against what was reported does.
    ///
    /// The container-TYPE path exercised here does persist correctly in production (task 023 proved it
    /// live). The guard exists so that if that ever stops being true, an administrator sees an error
    /// instead of a confirmation for a storage cap that was never applied.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UpdateSettings_WhenGraphReturnsSuccessButDropsTheCeiling_Throws_RatherThanReportingSuccess()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, $$"""
            {"id":"{{ContainerTypeId}}","name":"Legal Documents","etag":"MC4wLjAuMA=="}
            """);
        // 200, settings present, other fields echoed, maxStoragePerContainerInBytes silently absent.
        graph.StubPatch(ContainerTypesPath, $$"""
            {
              "id":"{{ContainerTypeId}}","name":"Legal Documents",
              "settings":{"itemMajorVersionLimit":25,"isItemVersioningEnabled":true}
            }
            """);

        var act = () => CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: 10_737_418_240L);

        var ex = await act.Should()
            .ThrowAsync<SpeAdminGraphService.SettingsNotPersistedException>(
                "Graph accepted the write and did not apply it — reporting success would leave an " +
                "administrator believing a storage cap exists when none does");

        ex.Which.UnwrittenFields.Should().ContainSingle()
            .Which.Should().Contain("maxStoragePerContainerInBytes");
    }

    /// <summary>
    /// Positive control — a write Graph DOES reflect must pass cleanly.
    /// </summary>
    /// <remarks>
    /// Without this, "always throw" would satisfy the test above and break every settings save. The
    /// pair is what makes the detector meaningful.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UpdateSettings_WhenGraphReflectsTheCeiling_Succeeds()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, $$"""
            {"id":"{{ContainerTypeId}}","name":"Legal Documents","etag":"MC4wLjAuMA=="}
            """);
        graph.StubPatch(ContainerTypesPath, $$"""
            {
              "id":"{{ContainerTypeId}}","name":"Legal Documents",
              "settings":{"itemMajorVersionLimit":25,"maxStoragePerContainerInBytes":10737418240}
            }
            """);

        var result = await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: null,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: 10_737_418_240L);

        result!.Settings!.MaxStoragePerContainerInBytes.Should().Be(10_737_418_240L);
    }

    /// <summary>
    /// Second negative control: the guard must not fire on a field the caller did not set.
    /// </summary>
    /// <remarks>
    /// A settings save that only changes the version limit must not be rejected because the response's
    /// ceiling differs from `null`. Over-firing here would make every partial save fail — the kind of
    /// false positive that gets a guard deleted rather than fixed.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UpdateSettings_DoesNotVerifyFieldsTheCallerDidNotSet()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, $$"""
            {"id":"{{ContainerTypeId}}","name":"Legal Documents","etag":"MC4wLjAuMA=="}
            """);
        // The ceiling comes back at a value we never asked about — that is not our business.
        graph.StubPatch(ContainerTypesPath, $$"""
            {
              "id":"{{ContainerTypeId}}","name":"Legal Documents",
              "settings":{"itemMajorVersionLimit":5,"maxStoragePerContainerInBytes":27487790694400}
            }
            """);

        var result = await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: null,
            itemMajorVersionLimit: 5, maxStoragePerContainerInBytes: null);

        result.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUT
    // ─────────────────────────────────────────────────────────────────────────

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
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: null);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(
            $"A method under test requested the '{name}' HttpClient. These tests supply the Graph " +
            "client directly, so building one means the code took an unexpected path.");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Key Vault must not be reached from a contract test.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Key Vault must not be reached from a contract test.");
    }
}
