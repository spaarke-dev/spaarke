using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the container archival contract (spec FR-E01, task 050): the request shapes of the
/// <c>archive</c> / <c>unarchive</c> beta actions, the mapping of <c>archivalDetails</c>, and — the
/// load-bearing part — that a container type which has not opted into archival is diagnosed as such
/// rather than reported as a permissions failure.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is genuinely at risk here.</b> Archival could not be verified end-to-end against a live
/// tenant when it was written: the Spaarke Dev container type has not opted in, and the opt-in is an
/// operator action on shared infrastructure (notes/task-050-findings.md §7). So these tests carry more
/// weight than usual — they are the only automated statement of what the code sends and how it reads
/// what comes back. They were authored from Graph's <b>CSDL</b> and from <b>measured live responses</b>,
/// never from the shape our own code happens to produce.
/// </para>
/// <para>
/// <b>Live-measured facts encoded below</b> (2026-08-27, app-only against Spaarke Dev):
/// <list type="bullet">
/// <item><c>archive</c> and <c>unarchive</c> exist on <b>beta only</b> — the v1.0 CSDL has neither.</item>
/// <item>A not-opted-in container type answers
/// <c>403 notAllowed — "Archival operation cannot proceed because this application does not currently
/// support archiving."</c> The 403 is semantic: the route exists, the capability is off.</item>
/// <item><c>status</c> is <b>never</b> returned on a containers LIST, even when <c>$select</c> asks
/// for it.</item>
/// </list>
/// </para>
/// <para>
/// Per <c>tests/CLAUDE.md</c>, these live under <c>tests/integration/contract/**</c> — a KEEP path.
/// The task POML nominated <c>tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/</c>, which task 042
/// established is NOT a KEEP path; writing them there would have scheduled them for deletion at the
/// <c>/test-diet</c> gate. Deviation recorded in notes/task-050-findings.md §6.
/// </para>
/// </remarks>
public class SpeAdminContainerArchivalContractTests
{
    private const string ContainerId = "b!DcvTfUkibESq94RyGJFs-UhqWZU646tBrEagKKMKiOc";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private static string ArchivePath => $"{ContainersPath}/{ContainerId}/archive";
    private static string UnarchivePath => $"{ContainersPath}/{ContainerId}/unarchive";

    /// <summary>
    /// Graph's real refusal when the container type has not opted into archival. Copied verbatim from
    /// a live 403 so the detector is tested against the actual payload, not a paraphrase of it.
    /// </summary>
    private const string NotOptedInBody = """
        {"error":{"code":"notAllowed","message":"Archival operation cannot proceed because this application does not currently support archiving."}}
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // The request
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ArchiveContainer_PostsToTheArchiveAction_OnTheContainerItself()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ArchivePath, "", statusCode: 202);

        var accepted = await CreateSut().ArchiveContainerAsync(graph.CreateGraphClient(), ContainerId);

        accepted.Should().BeTrue();
        graph.RequestsFor(ArchivePath).Should().ContainSingle(
            "archival is a bound action on the container — POST {id}/archive, not a PATCH of a status " +
            "field. There is no writable archive property to PATCH on either API version.");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UnarchiveContainer_PostsToUnarchive_NotToTheRestoreAction()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(UnarchivePath, "", statusCode: 202);

        await CreateSut().UnarchiveContainerAsync(graph.CreateGraphClient(), ContainerId);

        graph.RequestsFor(UnarchivePath).Should().ContainSingle();

        // The distinction this test exists for. `restore` is a REAL and DIFFERENT action —
        // it recovers a soft-DELETED container from /deletedContainers, and this codebase already
        // implements it as RestoreContainerAsync. Sending `restore` here would be accepted by Graph
        // as a well-formed call to the wrong operation, and against a live tenant would either 404
        // (the container is not deleted) or, worse, succeed against something unintended.
        graph.RequestsFor($"{ContainersPath}/{ContainerId}/restore").Should().BeEmpty(
            "unarchive reverses ARCHIVAL; restore recovers a DELETED container. Two distinct Graph " +
            "actions that both sound like 'bring it back'.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The not-opted-in diagnosis — the reason this task exists
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ArchiveContainer_WhenContainerTypeHasNotOptedIn_ThrowsArchivalNotEnabled_NotAGenericForbidden()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ArchivePath, NotOptedInBody, statusCode: 403);

        var act = () => CreateSut().ArchiveContainerAsync(graph.CreateGraphClient(), ContainerId);

        // If this ever regresses to a bare ODataError/403, the endpoint returns "Forbidden" and an
        // administrator spends their afternoon auditing Graph permissions that are already correct.
        // Nothing about the caller is wrong; the capability is switched off on the container type.
        await act.Should().ThrowAsync<SpeAdminGraphService.ArchivalNotEnabledException>(
            "a 403 whose code is notAllowed and whose message is about archiving is a capability " +
            "problem, not an authorization problem, and the two need different remediation");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UnarchiveContainer_WhenContainerTypeHasNotOptedIn_ThrowsArchivalNotEnabled()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(UnarchivePath, NotOptedInBody, statusCode: 403);

        var act = () => CreateSut().UnarchiveContainerAsync(graph.CreateGraphClient(), ContainerId);

        await act.Should().ThrowAsync<SpeAdminGraphService.ArchivalNotEnabledException>();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ArchiveContainer_WhenNotOptedIn_PreservesGraphsOwnMessage()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ArchivePath, NotOptedInBody, statusCode: 403);

        var ex = await Assert.ThrowsAsync<SpeAdminGraphService.ArchivalNotEnabledException>(
            () => CreateSut().ArchiveContainerAsync(graph.CreateGraphClient(), ContainerId));

        // Graph's own words reach the ProblemDetails payload. If Microsoft's message ever stops
        // matching our diagnosis, an operator can see that for themselves instead of having to
        // trust our interpretation of a failure we translated.
        ex.GraphMessage.Should().Contain("does not currently support archiving");
        ex.ContainerId.Should().Be(ContainerId);
    }

    /// <summary>
    /// Negative control for the detector above — a plain authorization failure must NOT be
    /// re-labelled as an archival opt-in problem.
    /// </summary>
    /// <remarks>
    /// Without this, widening <c>IsArchivalNotEnabled</c> to "any 403" would still pass every test
    /// above, and every genuine permissions error would then tell the administrator to go and run
    /// PowerShell that changes nothing. A detector that over-fires produces confident wrong advice,
    /// which is worse than the generic message it replaced.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ArchiveContainer_WhenGenuinelyForbidden_DoesNotClaimArchivalIsDisabled()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ArchivePath, """
            {"error":{"code":"accessDenied","message":"Either scp or roles claim need to be present in the token."}}
            """, statusCode: 403);

        var act = () => CreateSut().ArchiveContainerAsync(graph.CreateGraphClient(), ContainerId);

        (await act.Should().ThrowAsync<Exception>())
            .Which.Should().NotBeOfType<SpeAdminGraphService.ArchivalNotEnabledException>(
                "accessDenied is a real permissions failure — telling the operator to enable archival " +
                "would send them to fix something that is not broken");
    }

    /// <summary>
    /// Second negative control: right status, right code, unrelated subject.
    /// </summary>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ArchiveContainer_WhenNotAllowedForAnUnrelatedReason_DoesNotClaimArchivalIsDisabled()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ArchivePath, """
            {"error":{"code":"notAllowed","message":"The container is locked and cannot be modified."}}
            """, statusCode: 403);

        var act = () => CreateSut().ArchiveContainerAsync(graph.CreateGraphClient(), ContainerId);

        (await act.Should().ThrowAsync<Exception>())
            .Which.Should().NotBeOfType<SpeAdminGraphService.ArchivalNotEnabledException>(
                "`notAllowed` is a general-purpose Graph code — the diagnosis needs the subject to " +
                "be archiving, not merely the code to match");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ArchiveContainer_WhenContainerDoesNotExist_ReturnsFalse_RatherThanThrowing()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ArchivePath, """
            {"error":{"code":"itemNotFound","message":"Item not found"}}
            """, statusCode: 404);

        var result = await CreateSut().ArchiveContainerAsync(graph.CreateGraphClient(), ContainerId);

        result.Should().BeFalse("the endpoint maps a false return to 404 ProblemDetails");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reading archive state back
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_MapsArchiveStatus_FromArchivalDetails()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {
              "id":"{{ContainerId}}",
              "displayName":"Closed Matter 2019-114",
              "status":"active",
              "archivalDetails":{"archiveStatus":"fullyArchived"}
            }
            """);

        var container = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        container!.ArchiveStatus.Should().Be("fullyArchived");

        // The two are independent dimensions, and this row is the proof: Graph reports the container
        // as `active` AND `fullyArchived` simultaneously. Collapsing them into one value would have
        // to discard one of two true facts.
        container.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_WhenNotArchived_LeavesArchiveStatusNull()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {"id":"{{ContainerId}}","displayName":"Active Matter","status":"active"}
            """);

        var container = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        // Graph has no `notArchived` enum member on either version — a non-archived container simply
        // omits `archivalDetails`. Null is therefore the ONLY representation available, and callers
        // must not read it as a positive claim that content is online.
        container!.ArchiveStatus.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_AsksForArchivalDetails_SoTheFieldArrivesIfGraphServesIt()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {"id":"{{ContainerId}}","displayName":"Matter"}
            """);

        await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        // Verified live 2026-08-27 that adding archivalDetails to the $select does NOT cause Graph to
        // drop the other selected fields (storageUsedInBytes still came back) — checked precisely
        // because task 028 found $expand=drive being silently dropped from list rows.
        Uri.UnescapeDataString(graph.RequestsFor($"{ContainersPath}/{ContainerId}").Single().RawQuery)
            .Should().Contain("archivalDetails");
    }

    /// <summary>
    /// The transitional states are preserved verbatim — the UI depends on telling them apart.
    /// </summary>
    /// <remarks>
    /// <c>siteArchiveStatus</c> is <c>{recentlyArchived, fullyArchived, reactivating}</c>. Two of the
    /// three mean "still working". Normalising them to a boolean would erase the distinction between
    /// "archived" and "archiving", which is exactly the acceptance-is-not-completion claim this
    /// feature must not make.
    /// </remarks>
    [Theory]
    [InlineData("recentlyArchived")]
    [InlineData("fullyArchived")]
    [InlineData("reactivating")]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_PreservesEveryArchiveStatusValue_IncludingTheInFlightOnes(string archiveStatus)
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {
              "id":"{{ContainerId}}",
              "displayName":"Matter",
              "archivalDetails":{"archiveStatus":"{{archiveStatus}}"}
            }
            """);

        var container = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        container!.ArchiveStatus.Should().Be(archiveStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The status regression this task uncovered
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A container LIST must never invent a status.
    /// </summary>
    /// <remarks>
    /// <b>This is a regression guard for a defect that shipped.</b> Until 2026-08-27 all four
    /// container mapping sites read status out of <c>AdditionalData</c> and fell back to the literal
    /// <c>"active"</c>. <c>status</c> is in the v1.0 schema, so the Graph SDK models it as a
    /// <i>typed</i> property and Kiota never places it in <c>AdditionalData</c> — the lookup could not
    /// match, and the fallback fired for every row on every path. The Containers grid asserted
    /// "Active" for every container in the tenant.
    ///
    /// Graph does not return <c>status</c> on a LIST at all (measured live), so the honest list value
    /// is null. If this test fails with "active", the fallback has been reintroduced.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListContainers_WhenGraphOmitsStatus_ReportsNull_NotAFabricatedActive()
    {
        using var graph = new GraphWireMockFixture();

        // Exactly what the live LIST returns: no `status` member on the row, whatever $select asks.
        graph.StubGet(ContainersPath, $$"""
            {"value":[{"id":"{{ContainerId}}","displayName":"Matter","containerTypeId":"8a6ce34c-6055-4681-8f87-2f4f9f921c06"}]}
            """);

        var containers = await CreateSut().ListContainersAsync(
            graph.CreateGraphClient(), "8a6ce34c-6055-4681-8f87-2f4f9f921c06");

        containers.Single().Status.Should().BeNull(
            "Graph does not report container status on a collection — null means NOT REPORTED, and " +
            "'active' would be a fabrication indistinguishable from a real reading (spec NFR-06)");
    }

    /// <summary>
    /// Positive control for the same reader: when Graph DOES report a status, it must survive.
    /// </summary>
    /// <remarks>
    /// Without this, "return null" would satisfy the test above and silently destroy the detail
    /// view — trading a fabricated value for a discarded one. Both are the same class of defect.
    /// The value below is <c>inactive</c> deliberately: it is what Graph really returns for a
    /// freshly created container, and it is the value the old hardcoded "active" was overwriting.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_WhenGraphReportsStatus_PreservesIt_RatherThanOverwritingWithActive()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {"id":"{{ContainerId}}","displayName":"Just Created","status":"inactive"}
            """);

        var container = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        container!.Status.Should().Be("inactive",
            "a newly created container is inactive until activated — reporting it as active is the " +
            "exact lie the old hardcoded fallback told");
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
