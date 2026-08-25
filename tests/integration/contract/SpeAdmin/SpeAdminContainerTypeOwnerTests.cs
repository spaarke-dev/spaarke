using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the container-type OWNER surface — the request shapes and the mapping (spec FR-C09, task 027).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> This surface has no typed SDK builder (Graph 6.5.0 models v1.0, and
/// <c>fileStorageContainerType.permissions</c> is beta-only), so every request is hand-built. Nothing
/// but a test can catch a wrong path segment, a wrong verb, or a payload Graph will ignore — the
/// compiler cannot, which is the opposite of the position task 023 engineered for settings.
/// </para>
/// <para>
/// The path assertion is the load-bearing one. <c>/permissions</c> on a container type is a DIFFERENT
/// Graph resource from the <c>applicationPermissions</c> the BFF also exposes under a route called
/// "permissions"; getting the URL subtly wrong would return a plausible-looking payload for the wrong
/// concept. That conflation is exactly the error task 027's own POML made.
/// </para>
/// </remarks>
public class SpeAdminContainerTypeOwnerTests
{
    private const string TypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06";
    private static string PermissionsPath => $"/storage/fileStorage/containerTypes/{TypeId}/permissions";

    // ─────────────────────────────────────────────────────────────────────────
    // Request shapes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListOwners_TargetsTheContainerTypePermissionsCollection()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(PermissionsPath, """{"value":[]}""");

        await CreateSut().ListContainerTypeOwnersAsync(graph.CreateGraphClient(), TypeId);

        graph.RequestsFor(PermissionsPath).Should().ContainSingle(
            "owners live on the container type's own permissions collection — NOT on " +
            "containerTypeRegistrations, and not on the applicationPermissions surface the BFF also " +
            "calls 'permissions'");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task AddOwner_ResolvesAUpnToAnObjectId_BecauseGraphAcceptsOnlyTheId()
    {
        /*
         * 🔴 Regression guard. The first implementation sent `userPrincipalName` for an
         * email-shaped identifier. Graph's Create-permission reference is explicit: "Only the
         * **user** property with the user's **id** is supported". Live, that returned
         * 400 invalidRequest — the same message that names nothing as the etag defect did.
         *
         * An administrator types an email, so the UPN must be resolved to an object id first.
         */
        using var graph = new GraphWireMockFixture();
        graph.StubGet("/users/", """{"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""");
        graph.StubPost(PermissionsPath, """{"id":"perm-1","roles":["owner"]}""");

        await CreateSut().AddContainerTypeOwnerAsync(
            graph.CreateGraphClient(), TypeId, "ada@contoso.com");

        graph.RequestsFor("/users/").Should().ContainSingle(
            "a UPN has to be resolved before it can be granted ownership");

        var request = graph.RequestsFor(PermissionsPath).Should().ContainSingle().Subject;
        request.Method.Should().Be("POST");
        request.Body.Should().Contain("owner", "the grant must name the role it confers");
        request.Body.Should().Contain("3f2504e0-4f89-11d3-9a0c-0305e82c3301",
            "the RESOLVED object id is what Graph accepts");
        request.Body.Should().NotContain("ada@contoso.com",
            "Graph rejects a userPrincipalName here with a 400 that names no cause");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task AddOwner_WhenTheUpnResolvesToNobody_SaysSo_RatherThanSendingADoomedGrant()
    {
        // "No such user" and "400 invalidRequest" read identically to an admin and mean completely
        // different things. Sending the grant anyway would surface the wrong one.
        using var graph = new GraphWireMockFixture();
        graph.StubGet("/users/", """{"error":{"code":"Request_ResourceNotFound"}}""", statusCode: 404);
        graph.StubPost(PermissionsPath, """{"id":"perm-1"}""");

        var act = async () => await CreateSut().AddContainerTypeOwnerAsync(
            graph.CreateGraphClient(), TypeId, "nobody@contoso.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("nobody@contoso.com"));

        graph.RequestsFor(PermissionsPath).Should().BeEmpty(
            "a grant that cannot succeed should not be sent");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task AddOwner_WhenGivenAnObjectId_SendsItDirectly_WithoutADirectoryLookup()
    {
        // An id needs no resolution, and a needless /users call would be a second way to fail.
        using var graph = new GraphWireMockFixture();
        graph.StubPost(PermissionsPath, """{"id":"perm-1"}""");

        await CreateSut().AddContainerTypeOwnerAsync(
            graph.CreateGraphClient(), TypeId, "3f2504e0-4f89-11d3-9a0c-0305e82c3301");

        graph.RequestsFor("/users/").Should().BeEmpty();
        var body = graph.RequestsFor(PermissionsPath).Single().Body;
        body.Should().Contain("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        body.Should().NotContain("userPrincipalName");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task RemoveOwner_DeletesTheNamedGrant()
    {
        var grantPath = $"{PermissionsPath}/perm-1";
        using var graph = new GraphWireMockFixture();
        graph.StubDelete(grantPath, string.Empty, statusCode: 204);

        var removed = await CreateSut().RemoveContainerTypeOwnerAsync(
            graph.CreateGraphClient(), TypeId, "perm-1");

        removed.Should().BeTrue();
        graph.RequestsFor(grantPath).Should().ContainSingle().Which.Method.Should().Be("DELETE");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mapping — absence must stay absence
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListOwners_MapsIdentityFromGrantedToV2()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(PermissionsPath, """
            {"value":[{
              "id":"perm-1",
              "roles":["owner"],
              "grantedToV2":{"user":{"id":"user-1","displayName":"Ada Lovelace","email":"ada@contoso.com"}}
            }]}
            """);

        var owners = await CreateSut().ListContainerTypeOwnersAsync(graph.CreateGraphClient(), TypeId);

        var owner = owners.Should().ContainSingle().Subject;
        owner.PermissionId.Should().Be("perm-1");
        owner.DisplayName.Should().Be("Ada Lovelace");
        owner.Email.Should().Be("ada@contoso.com");
        owner.UserId.Should().Be("user-1");
        owner.Roles.Should().ContainSingle().Which.Should().Be("owner");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListOwners_FallsBackToLegacyGrantedTo()
    {
        // Both shapes are in the beta schema and which one a response carries is not ours to assume.
        // Reading only grantedToV2 is how a populated owner list would render as a list of blanks.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(PermissionsPath, """
            {"value":[{
              "id":"perm-2",
              "grantedTo":{"user":{"displayName":"Grace Hopper"}}
            }]}
            """);

        var owners = await CreateSut().ListContainerTypeOwnersAsync(graph.CreateGraphClient(), TypeId);

        owners.Should().ContainSingle().Which.DisplayName.Should().Be("Grace Hopper");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListOwners_WhenGraphReportsNoIdentity_LeavesFieldsNull_NotEmptyStrings()
    {
        // "" renders as a blank row that reads like a corrupt record. Null lets the UI say
        // "Unknown user" and explain why (NFR-06).
        using var graph = new GraphWireMockFixture();
        graph.StubGet(PermissionsPath, """{"value":[{"id":"perm-3"}]}""");

        var owner = (await CreateSut().ListContainerTypeOwnersAsync(graph.CreateGraphClient(), TypeId))
            .Should().ContainSingle().Subject;

        owner.DisplayName.Should().BeNull();
        owner.Email.Should().BeNull();
        owner.UserId.Should().BeNull();
        owner.Roles.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListOwners_WhenGraphReturnsNoOwners_ReturnsEmpty_NotNull()
    {
        // Empty and null mean different things to the caller: "Graph reported none" vs "not found".
        // Collapsing them would make a missing container type look like one with no owners.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(PermissionsPath, """{"value":[]}""");

        var owners = await CreateSut().ListContainerTypeOwnersAsync(graph.CreateGraphClient(), TypeId);

        owners.Should().NotBeNull().And.BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test plumbing
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
            dataverseClient: new DataverseWebApiClient(configuration, NullLogger<DataverseWebApiClient>.Instance),
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
