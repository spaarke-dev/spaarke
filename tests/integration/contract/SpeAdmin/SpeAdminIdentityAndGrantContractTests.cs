using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
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

    // ─────────────────────────────────────────────────────────────────────────
    // Identity — the BFF's own app-only client, never an owning-app secret
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForConfig_ReturnsTheBffAppOnlyClient_EvenWhenTheConfigHasNoSecretName()
    {
        using var graph = new GraphWireMockFixture();
        var bffAppClient = graph.CreateGraphClient();
        var sut = CreateSut(new StubGraphClientFactory(bffAppClient));

        // No usable Key Vault secret name — the exact Model 1 shape that used to break every operation.
        var client = await sut.GetClientForConfigAsync(Config(BffTenant, secretName: "null"));

        client.Should().BeSameAs(bffAppClient,
            because: "container work runs as the BFF's own identity; no credential is read from the config");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForConfig_RefusesAConfigFromAnotherTenant()
    {
        using var graph = new GraphWireMockFixture();
        var sut = CreateSut(new StubGraphClientFactory(graph.CreateGraphClient()));

        var act = () => sut.GetClientForConfigAsync(Config(OtherTenant));

        // The BFF identity can only act in its own tenant. Serving this config would list THIS tenant's
        // containers under another tenant's configuration — silently wrong, so it must refuse.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{OtherTenant}*{BffTenant}*");
    }

    [Theory]
    [Trait("Category", "SpeAdminGraphContract")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetClientForConfig_RefusesAConfigWhoseTenantIsUnknown(string tenant)
    {
        using var graph = new GraphWireMockFixture();
        var sut = CreateSut(new StubGraphClientFactory(graph.CreateGraphClient()));

        var act = () => sut.GetClientForConfigAsync(Config(tenant));

        // Fail closed (WP-6): an unestablished tenant cannot be confirmed to be the BFF's.
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*has no tenant*");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForConfig_RefusesWhenTheBffTenantIsNotConfigured()
    {
        using var graph = new GraphWireMockFixture();
        var sut = CreateSut(new StubGraphClientFactory(graph.CreateGraphClient()), bffTenant: null);

        var act = () => sut.GetClientForConfigAsync(Config(BffTenant));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*TENANT_ID*");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetClientForConfig_WithoutAGraphClientFactory_NamesTheMissingDependency()
    {
        var sut = CreateSut(graphClientFactory: null);

        var act = () => sut.GetClientForConfigAsync(Config(BffTenant));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IGraphClientFactory*");
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
            graphClientFactory: graphClientFactory);
    }

    /// <summary>Hands back one pre-built client as the BFF's app-only client; the delegated paths are
    /// not under test here and throw if reached.</summary>
    private sealed class StubGraphClientFactory(GraphServiceClient appOnly) : IGraphClientFactory
    {
        public GraphServiceClient ForApp() => appOnly;

        public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default) =>
            throw new InvalidOperationException("Delegated Graph is not under test here.");

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
