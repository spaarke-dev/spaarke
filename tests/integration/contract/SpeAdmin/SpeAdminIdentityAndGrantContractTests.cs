using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the two halves of the 2026-10-04 identity change: WHICH identity SPE Admin container work runs as,
/// and the SHAPE of the grant that gives that identity access to a container type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity.</b> SPE Admin used to authenticate app-only as each container type's OWNING app, with a
/// client secret from Key Vault. A config holding no usable secret (the Model 1 config held the literal
/// <c>null</c>) failed every container operation. It now uses the BFF's own app-only client —
/// <see cref="IGraphClientFactory.ForApp"/>, the managed identity on Azure — and refuses a config from
/// another tenant rather than serving it with a client pointed at the wrong one.
/// </para>
/// <para>
/// <b>Grant.</b> Access for that identity is an <c>applicationPermissionGrant</c> on the container type's
/// registration. The previous write POSTed to the collection with each permission list collapsed to its
/// first element as a bare string — task 041 measured it failing live (<c>400 apiNotFound</c>). Graph's
/// documented create is <c>PUT …/applicationPermissionGrants/{appId}</c> with both lists as arrays and no
/// <c>appId</c> in the body.
/// </para>
/// <para>Per <c>tests/CLAUDE.md</c> this lives under <c>tests/integration/contract/**</c> — a KEEP path.</para>
/// </remarks>
public class SpeAdminIdentityAndGrantContractTests
{
    private const string BffTenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string OtherTenant = "11111111-2222-3333-4444-555555555555";
    private const string RegistrationsPath = "/storage/fileStorage/containerTypeRegistrations";
    private const string ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06";
    private const string BffManagedIdentityAppId = "5967251e-171c-46fe-a6c2-ef843c90309d";
    private const string OwnedContainerId = "b!owned-container";

    // ─────────────────────────────────────────────────────────────────────────
    // Identity — the BFF's own app-only client, never an owning-app secret
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForContainer_ReturnsTheBffAppOnlyClient_EvenWhenTheConfigHasNoSecretName()
    {
        using var graph = new GraphWireMockFixture();
        var bffAppClient = graph.CreateGraphClient();
        var sut = CreateSut(new StubGraphClientFactory(bffAppClient));

        // No usable Key Vault secret name — the exact Model 1 shape that used to break every operation.
        var client = await sut.GetClientForContainerAsync(Config(BffTenant, secretName: "null"), OwnedContainerId);

        client.Should().BeSameAs(bffAppClient,
            because: "container work runs as the BFF's own identity; no credential is read from the config");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForContainer_RefusesAConfigFromAnotherTenant()
    {
        using var graph = new GraphWireMockFixture();
        var sut = CreateSut(new StubGraphClientFactory(graph.CreateGraphClient()));

        var act = () => sut.GetClientForContainerAsync(Config(OtherTenant), OwnedContainerId);

        // The BFF identity can only act in its own tenant. Serving this config would list THIS tenant's
        // containers under another tenant's configuration — silently wrong, so it must refuse.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{OtherTenant}*{BffTenant}*");
    }

    [Theory]
    [Trait("Category", "SpeAdminGraphContract")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetTypeWideClient_RefusesAConfigWhoseTenantIsUnknown(string tenant)
    {
        using var graph = new GraphWireMockFixture();
        var sut = CreateSut(new StubGraphClientFactory(graph.CreateGraphClient()));

        var act = () => sut.GetTypeWideClientForConfigAsync(Config(tenant));

        // Fail closed (WP-6): an unestablished tenant cannot be confirmed to be the BFF's.
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*has no tenant*");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForContainer_RefusesWhenTheBffTenantIsNotConfigured()
    {
        using var graph = new GraphWireMockFixture();
        var sut = CreateSut(new StubGraphClientFactory(graph.CreateGraphClient()), bffTenant: null);

        var act = () => sut.GetClientForContainerAsync(Config(BffTenant), OwnedContainerId);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*TENANT_ID*");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForContainer_WithoutAnOwnershipGuard_NamesTheMissingDependency()
    {
        var sut = CreateSut(graphClientFactory: null);

        var act = () => sut.GetClientForContainerAsync(Config(BffTenant), OwnedContainerId);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*SpeContainerOwnershipGuard*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Grant create — PUT to the appId, arrays, no appId in the body
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RegisterConsumingTenant_PutsToTheAppIdKeyedGrant_NotPostToTheCollection()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPut(RegistrationsPath, GrantResponse(["full"], ["full"]));

        await CreateSut().RegisterConsumingTenantAsync(
            graph.CreateGraphClient(), ContainerTypeId, BffManagedIdentityAppId, tenantId: null,
            delegatedPermissions: ["full"], applicationPermissions: ["full"]);

        var write = graph.RequestsFor(RegistrationsPath).Should().ContainSingle().Subject;

        // 🔴 The verb and the key ARE the contract. POST to the collection is what failed live.
        write.Method.Should().Be("PUT");
        write.Path.Should().EndWith(
            $"/containerTypeRegistrations/{ContainerTypeId}/applicationPermissionGrants/{BffManagedIdentityAppId}");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RegisterConsumingTenant_SendsEveryPermissionAsAnArray_AndNoAppIdInTheBody()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPut(RegistrationsPath, GrantResponse(["readContent", "writeContent"], ["full"]));

        await CreateSut().RegisterConsumingTenantAsync(
            graph.CreateGraphClient(), ContainerTypeId, BffManagedIdentityAppId, tenantId: null,
            delegatedPermissions: ["readContent", "writeContent"], applicationPermissions: ["full"]);

        var body = graph.RequestsFor(RegistrationsPath).Single().BodyAsJson();

        // The old code sent ONLY the first element, as a string — readContent+writeContent became readContent.
        body.GetProperty("delegatedPermissions").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("readContent", "writeContent");
        body.GetProperty("applicationPermissions").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("full");
        body.TryGetProperty("appId", out _).Should().BeFalse(
            because: "Graph's create contract: \"Don't include the appId in the body\" — it is the path key");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RegisterConsumingTenant_ReportsWhatGraphGranted_NotWhatWasRequested()
    {
        using var graph = new GraphWireMockFixture();
        // Graph granted less than was asked for — the result must say so.
        graph.StubPut(RegistrationsPath, GrantResponse(delegated: [], application: ["readContent"]));

        var result = await CreateSut().RegisterConsumingTenantAsync(
            graph.CreateGraphClient(), ContainerTypeId, BffManagedIdentityAppId, tenantId: null,
            delegatedPermissions: ["full"], applicationPermissions: ["full"]);

        result!.ApplicationPermissions.Should().Equal("readContent");
        result.DelegatedPermissions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RegisterConsumingTenant_WhenTheTypeIsNotRegisteredHere_ReturnsNull()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPut(RegistrationsPath, """{"error":{"code":"itemNotFound","message":"not found"}}""", statusCode: 404);

        var result = await CreateSut().RegisterConsumingTenantAsync(
            graph.CreateGraphClient(), ContainerTypeId, BffManagedIdentityAppId, tenantId: null,
            delegatedPermissions: [], applicationPermissions: ["full"]);

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Grant update — PATCH with both whole lists
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UpdateConsumingTenant_PatchesTheGrant_WithBothWholeLists()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPatch(RegistrationsPath, GrantResponse(["readContent", "writeContent"], ["manageContent", "full"]));

        await CreateSut().UpdateConsumingTenantAsync(
            graph.CreateGraphClient(), ContainerTypeId, BffManagedIdentityAppId,
            delegatedPermissions: ["readContent", "writeContent"], applicationPermissions: ["manageContent", "full"]);

        var write = graph.PatchRequestsFor(RegistrationsPath).Should().ContainSingle().Subject;
        write.Path.Should().EndWith($"/applicationPermissionGrants/{BffManagedIdentityAppId}");

        var body = write.BodyAsJson();
        body.GetProperty("delegatedPermissions").GetArrayLength().Should().Be(2);
        body.GetProperty("applicationPermissions").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("manageContent", "full");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reading the grants — delegated, never the app-only identity (UAT 2026-10-07)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainerTypePermissions_ReadsTheGrantsAsTheSignedInAdmin_NotTheAppOnlyIdentity()
    {
        // App-only reads of a registration are limited to the app that OWNS it. The BFF identity owns
        // none, so the Permissions tab returned 403 accessDenied while the Consuming Apps tab — the same
        // grants, read delegated — worked.
        using var appOnlyGraph = new GraphWireMockFixture();
        using var delegatedGraph = new GraphWireMockFixture();
        delegatedGraph.StubGet(RegistrationsPath, JsonSerializer.Serialize(new
        {
            value = new[] { new { appId = BffManagedIdentityAppId, delegatedPermissions = Array.Empty<string>(), applicationPermissions = new[] { "full" } } },
        }));
        var sut = CreateSut(new StubGraphClientFactory(appOnlyGraph.CreateGraphClient(), delegatedGraph.CreateGraphClient()));

        var grants = await sut.GetContainerTypePermissionsForUserAsync(new DefaultHttpContext(), ContainerTypeId);

        grants!.Should().ContainSingle().Which.AppId.Should().Be(BffManagedIdentityAppId);
        delegatedGraph.RequestsFor(RegistrationsPath).Should().ContainSingle()
            .Which.Path.Should().EndWith($"/containerTypeRegistrations/{ContainerTypeId}/applicationPermissionGrants");
        appOnlyGraph.AllRequests.Should().BeEmpty(because: "the app-only identity cannot read a registration it does not own");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reading one container type — no $select, so billingStatus arrives (task 029, UAT 2026-10-07)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainerType_SendsNoSelect_SoBillingStatusReachesTheDetailPanel()
    {
        // The list showed "Valid" while the detail panel showed "Billing: Unknown": the single GET asked
        // for four named fields and billingStatus was not one of them.
        using var graph = new GraphWireMockFixture();
        graph.StubGet("/storage/fileStorage/containerTypes", JsonSerializer.Serialize(new
        {
            id = ContainerTypeId,
            name = "Spaarke SPE Model 1 Owner",
            billingClassification = "standard",
            billingStatus = "valid",
        }));

        var type = await CreateSut().GetContainerTypeAsync(graph.CreateGraphClient(), ContainerTypeId);

        graph.SelectFieldsFor("/storage/fileStorage/containerTypes").Should().BeEmpty();
        type!.BillingStatus.Should().BeEquivalentTo("valid");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The register endpoint's legacy permission names → Graph's
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "SpeAdminGraphContract")]
    [InlineData(ContainerTypePermissions.ReadContent, "readContent")]
    [InlineData(ContainerTypePermissions.WriteContent, "writeContent")]
    [InlineData(ContainerTypePermissions.Create, "create")]
    [InlineData(ContainerTypePermissions.Delete, "delete")]
    [InlineData(ContainerTypePermissions.ManagePermissions, "managePermissions")]
    [InlineData(ContainerTypePermissions.AddAllPermissions, "full")]
    public void LegacyRegisterPermissionNames_MapOntoGraphValues(string legacy, string graphValue)
    {
        SpeAdminGraphService.ToGraphContainerTypePermission(legacy).Should().Be(graphValue);
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void EveryAcceptedLegacyPermissionName_HasAGraphMapping()
    {
        // The endpoint validates against ValidPermissions; a name accepted there but unmapped here would
        // pass validation and then fail the request. Keep the two sets locked together.
        foreach (var name in ContainerTypePermissions.ValidPermissions)
        {
            var act = () => SpeAdminGraphService.ToGraphContainerTypePermission(name);
            act.Should().NotThrow(because: $"'{name}' is accepted by the register endpoint");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────────

    private static string GrantResponse(string[] delegated, string[] application) =>
        JsonSerializer.Serialize(new
        {
            appId = BffManagedIdentityAppId,
            delegatedPermissions = delegated,
            applicationPermissions = application,
        });

    private static SpeAdminGraphService.ContainerTypeConfig Config(string tenant, string secretName = "") =>
        new(
            ConfigId: Guid.Parse("0f8c3c1e-0000-4000-8000-000000000001"),
            ContainerTypeId: ContainerTypeId,
            ClientId: "bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e",
            TenantId: tenant,
            SecretKeyVaultName: secretName);

    private static SpeAdminGraphService CreateSut(
        IGraphClientFactory? graphClientFactory = null, string? bffTenant = BffTenant)
    {
        var settings = new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://unused.invalid" };
        if (bffTenant is not null) settings["TENANT_ID"] = bffTenant;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new SpeAdminGraphService(
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            graphClientFactory: graphClientFactory,
            // Ownership is not what these tests pin (SpeAppOnlyContainerIsolationTests does): every container is owned.
            ownership: graphClientFactory is null ? null : TestSpeOwnership.AllowAll(graphClientFactory));
    }

    /// <summary>Hands back pre-built clients as the BFF's app-only and (optionally) delegated clients; a
    /// delegated path reached without a delegated client throws.</summary>
    private sealed class StubGraphClientFactory(GraphServiceClient appOnly, GraphServiceClient? delegated = null)
        : IGraphClientFactory
    {
        public GraphServiceClient ForApp() => appOnly;

        public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default) =>
            delegated is not null
                ? Task.FromResult(delegated)
                : throw new InvalidOperationException("Delegated Graph is not under test here.");

        public Task<GraphServiceClient> ForUserBetaAsync(HttpContext ctx, CancellationToken ct = default) =>
            throw new InvalidOperationException("Delegated Graph is not under test here.");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Dataverse must not be reached from a contract test.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Dataverse must not be reached from a contract test.");
    }
}
