using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 20 item 1 — BOTH BFF container-creation paths stamp the new container
/// with its owning business unit before returning, with the request shape Graph accepts, read it back, and remove a
/// container whose stamp did not land.
/// </summary>
/// <remarks>
/// <para>
/// The two paths: <see cref="SpeAdminGraphService.CreateContainerAsync"/> (the SPE admin plane) and
/// <see cref="ContainerOperations.CreateContainerAsync"/> (secure-record provisioning through <c>SpeFileStore</c>). The
/// stamp is <c>PATCH /storage/fileStorage/containers/{id}/customProperties</c> with the property map as the BODY ROOT —
/// the shape proven live on 2026-08-28 (a <c>{"customProperties":…}</c> wrapper on the container is a 400).
/// </para>
/// <para>Real services against the WireMock Graph (<see cref="GraphWireMockFixture"/>); ADR-038 contract KEEP path.</para>
/// </remarks>
public sealed class ContainerCreationStampContractTests : IDisposable
{
    private const string ContainersPath = "/storage/fileStorage/containers";
    private static readonly Guid Unit = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid TypeId = Guid.Parse("77777777-0000-0000-0000-000000000077");

    private readonly GraphWireMockFixture _graph = new();

    public void Dispose() => _graph.Dispose();

    [Fact]
    public async Task AdminPlaneCreate_StampsTheUnit_AsTheBodyRootOfACustomPropertiesPatch_AndReadsItBack()
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        created.Id.Should().Be("c-new");
        AssertStamped(Unit);
        var readBack = _graph.RequestsFor($"{ContainersPath}/c-new").Single(r => r.Method == "GET");
        GraphWireMockFixture.ParseSelect(readBack.RawQuery).Should().BeEquivalentTo("id", "containerTypeId", "customProperties");
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(200, false)]
    public async Task AdminPlaneCreate_WhoseStampDidNotLand_SoftDeletesTheContainer_AndThrows(int patchStatus, bool readBackStamped)
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}", patchStatus);
        _graph.StubGet($"{ContainersPath}/c-new", Container(readBackStamped ? Unit : null));
        _graph.StubDelete($"{ContainersPath}/c-new");

        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<SpeAdminGraphService.ContainerBindingException>()).Which;
        ex.ContainerId.Should().Be("c-new");
        ex.Removed.Should().BeTrue();
        _graph.RequestsFor($"{ContainersPath}/c-new").Should().Contain(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task AdminPlaneCreate_RefusesToCreateWithoutAnOwningUnit()
    {
        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Guid.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _graph.AllRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisioningCreate_StampsTheUnit_AndReadsItBack()
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        created!.Id.Should().Be("c-new");
        AssertStamped(Unit);
    }

    [Fact]
    public async Task ProvisioningCreate_WhoseStampDidNotLand_DeletesTheContainer_AndThrows()
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}", 500);
        _graph.StubDelete($"{ContainersPath}/c-new");

        var act = () => ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _graph.RequestsFor($"{ContainersPath}/c-new").Should().Contain(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task TheBindingOfADeletedContainer_IsReadFromTheRecycleBin()
    {
        _graph.StubGet("/storage/fileStorage/deletedContainers/c-gone", Container(Unit, "c-gone"));

        var read = await AdminGraph().GetContainerBindingAsync(_graph.CreateGraphClient(), "c-gone", deleted: true, CancellationToken.None);

        read!.Binding.Should().Be(SpeContainerBinding.BoundTo(Unit));
        _graph.SelectFieldsFor("/storage/fileStorage/deletedContainers/c-gone")
            .Should().BeEquivalentTo("id", "containerTypeId", "customProperties");
    }

    [Theory]
    [InlineData(404, false)]
    [InlineData(500, true)]
    public async Task ABindingRead_IsNullWhenAbsent_AndThrowsWhenGraphFails(int status, bool throws)
    {
        _graph.StubGet($"{ContainersPath}/c-x", """{"error":{"code":"x","message":"x"}}""", status);

        var act = () => AdminGraph().GetContainerBindingAsync(_graph.CreateGraphClient(), "c-x", deleted: false, CancellationToken.None);

        if (throws)
        {
            await act.Should().ThrowAsync<Exception>("a failed read must never be read as 'unbound'");
        }
        else
        {
            (await act()).Should().BeNull();
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void StubCreated() =>
        _graph.StubPostExact(ContainersPath,
            "{\"id\":\"c-new\",\"displayName\":\"New\",\"containerTypeId\":\"" + TypeId + "\",\"status\":\"inactive\"}", 201);

    private static string Container(Guid? unit, string id = "c-new") =>
        "{\"id\":\"" + id + "\",\"containerTypeId\":\"" + TypeId + "\",\"customProperties\":" +
        (unit is null ? "{}" : "{\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + unit + "\",\"isSearchable\":false}}") + "}";

    private void AssertStamped(Guid unit)
    {
        var patch = _graph.PatchRequestsFor($"{ContainersPath}/c-new").Should().ContainSingle().Subject;
        patch.Path.Should().EndWith("/customProperties", "customProperties is its own sub-resource");
        using var body = JsonDocument.Parse(patch.Body!);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(new[] { SpeContainerBusinessUnitStamp.PropertyName },
            "the stamp is the ONLY property written, as the body root — merge semantics leave every other property alone");
        var stamp = body.RootElement.GetProperty(SpeContainerBusinessUnitStamp.PropertyName);
        stamp.GetProperty("value").GetString().Should().Be(unit.ToString("D"));
        stamp.GetProperty("isSearchable").GetBoolean().Should().BeFalse();
    }

    private ContainerOperations ProvisioningOperations() =>
        new(new WireMockGraphFactory(_graph), NullLogger<ContainerOperations>.Instance);

    private static SpeAdminGraphService AdminGraph()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://unused.invalid" })
            .Build();

        return new SpeAdminGraphService(
            dataverseClient: new DataverseWebApiClient(configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance);
    }

    /// <summary>The app-only client ContainerOperations asks for is the WireMock one.</summary>
    private sealed class WireMockGraphFactory(GraphWireMockFixture graph) : IGraphClientFactory
    {
        public GraphServiceClient ForApp() => graph.CreateGraphClient();

        public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default) =>
            throw new InvalidOperationException("Container creation is app-only.");

        public Task<GraphServiceClient> ForUserBetaAsync(HttpContext ctx, CancellationToken ct = default) =>
            throw new InvalidOperationException("Container creation is app-only.");
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Not used: the client is passed in.");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c) =>
            throw new InvalidOperationException("No credential may be used by these tests.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c) =>
            throw new InvalidOperationException("No credential may be used by these tests.");
    }
}
