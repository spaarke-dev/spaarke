using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the Graph API version used for SPE container operations, and pins pagination to the base
/// address of the client that actually issues the request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> Task 020 (spec FR-C01) set out to migrate <c>/beta</c> → <c>/v1.0</c>. Live
/// probing against Spaarke Dev on 2026-08-23 showed that for <c>/storage/fileStorage/containers</c> the
/// migration would <b>delete a feature</b>: <c>storageUsedInBytes</c> is not defined in the v1.0 schema
/// at all — an explicit <c>$select</c> returns
/// <c>400 "Could not find a property named 'storageUsedInBytes'"</c>, while the identical call on beta
/// returns 200 with the value. <c>ownershipType</c> is likewise beta-only. Both feed the storage surface
/// (spec FR-C06 / task 024).
/// </para>
/// <para>
/// So beta here is a <b>measured decision</b>, not drift — and a decision with no test is just a comment
/// waiting to be "cleaned up" by the next person reading FR-C01's title. These tests fail loudly if the
/// base address is flipped, and point at the evidence.
/// </para>
/// <para>
/// Evidence: <c>projects/sdap-SPE-admin-app-r2/notes/beta-vs-v1-surface-verification.md</c>.
/// </para>
/// </remarks>
public class SpeAdminGraphVersionContractTests
{
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06";

    // ─────────────────────────────────────────────────────────────────────────
    // The version decision itself
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void ContainerOperations_UseBeta_BecauseV1DoesNotDefineStorageUsedInBytes()
    {
        // If this fails, someone migrated containers to v1.0. That silently removes
        // storageUsedInBytes and ownershipType — re-run the probe in the note before changing it.
        SpeAdminGraphService.SpeContainerGraphBaseUrl
            .Should().Be("https://graph.microsoft.com/beta",
                "v1.0 returns 400 'Could not find a property named storageUsedInBytes' — verified live " +
                "2026-08-23; see notes/beta-vs-v1-surface-verification.md");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pagination must follow the client, not a literal
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void ResolveGraphBaseUrl_ReturnsTheClientsOwnBaseAddress()
    {
        using var graph = new GraphWireMockFixture();

        SpeAdminGraphService.ResolveGraphBaseUrl(graph.CreateGraphClient())
            .Should().Be(graph.BaseUrl.TrimEnd('/'),
                "a synthetic nextLink must resolve against the client that will issue it");
    }

    /// <summary>The load-bearing one.</summary>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PagingToPageTwo_IssuesTheRequestAgainstTheSameClient_NotAHardcodedGraphHost()
    {
        // The fixture's client points at loopback. A nextLink hardcoded to graph.microsoft.com would
        // send page 2 to the real Graph host — the request would never reach the fixture, and the
        // failure mode is "no more results" rather than an error. That is precisely the silent
        // pagination break FR-C01's constraint names.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        await CreateSut().ListContainersPageAsync(
            graph.CreateGraphClient(), ContainerTypeId, top: 25, skipToken: "OPAQUE_TOKEN_XYZ");

        graph.RequestsFor(ContainersPath).Should().ContainSingle(
            "page 2 must be issued against the same client that fetched page 1");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task PagingToPageTwo_ForwardsTheSkipTokenAndFilterUnchanged()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        await CreateSut().ListContainersPageAsync(
            graph.CreateGraphClient(), ContainerTypeId, top: 25, skipToken: "OPAQUE_TOKEN_XYZ");

        var query = Uri.UnescapeDataString(graph.RequestsFor(ContainersPath).Single().RawQuery);

        query.Should().Contain("OPAQUE_TOKEN_XYZ", "the skip token is opaque and must survive round-tripping");
        query.Should().Contain(ContainerTypeId, "the filter must be carried onto later pages");
        query.Should().NotContain($"'{ContainerTypeId}'",
            "containerTypeId is Edm.Guid — quoting it makes Graph reject the filter (ADR-044)");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task FirstPage_AndNextPage_TargetTheSameHost()
    {
        // Cross-check: whatever host page 1 used, page 2 uses too. Stated as a relationship rather
        // than a literal so it keeps holding if the base address legitimately changes one day.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainersPath, """{"value":[]}""");

        var sut = CreateSut();
        await sut.ListContainersPageAsync(graph.CreateGraphClient(), ContainerTypeId, top: 25, skipToken: null);
        await sut.ListContainersPageAsync(graph.CreateGraphClient(), ContainerTypeId, top: 25, skipToken: "TOK");

        graph.RequestsFor(ContainersPath).Should().HaveCount(2,
            "both pages must reach the fixture — if only one does, the other went to a hardcoded host");
    }

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
            // auth-v4 (merged from master 2026-08-25) made DataverseWebApiClient select a credential in
            // its ctor: with Managed Identity disabled it now REQUIRES TENANT_ID + API_APP_ID + an
            // IConfidentialClientProvider, and threw before any test body ran. Passing the credential
            // explicitly takes the "selection bypassed" branch — and UnusableCredential throws if
            // anything ever actually asks it for a token, so a test that starts reaching Dataverse
            // fails loudly instead of quietly acquiring one. These contract tests supply the Graph
            // client directly and never touch Dataverse; this dependency exists only to construct.
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
