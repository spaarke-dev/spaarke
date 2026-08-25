using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins how a container's SharePoint URL is requested, mapped, and — deliberately — NOT emitted on
/// list rows (spec FR-C10, task 028).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> <c>fileStorageContainer</c> has <b>no URL property</b> in either Graph API
/// version. The value lives on the <c>drive</c> navigation property and must be pulled in with
/// <c>$expand</c>. An expand is far easier to delete by accident than a <c>$select</c> field — it
/// looks like a performance optimisation — and deleting it does not fail: the response still returns
/// 200 and the URL simply becomes null, which the UI would then render as "this container has no
/// URL".
/// </para>
/// <para>
/// <b>The measured behaviour these guard against</b> (live, 2026-08-24, both API versions —
/// notes/task-028-findings.md §1): on the containers <b>collection</b>, Graph <i>accepts</i>
/// <c>$expand=drive($select=webUrl)</c>, answers <b>200</b>, echoes <c>drive(webUrl)</c> back in
/// <c>@odata.context</c>, and returns <b>no <c>drive</c> member on any row</b>. Every expand shape
/// behaves the same way. So the natural implementation — put the expand on the list — produces a
/// confident, well-formed, entirely empty answer. That is this project's signature defect arriving
/// from the platform itself, and the reason the list contract omits the field rather than sending
/// <c>"webUrl": null</c>.
/// </para>
/// <para>
/// WireMock cannot reproduce Graph's silent drop (a fake returns whatever it is told to). These tests
/// therefore pin the two things that ARE ours to keep correct: that the GET-single request still asks
/// for the drive, and that the list DTO never carries the key.
/// </para>
/// </remarks>
public class SpeAdminContainerUrlMappingTests
{
    private const string ContainerId = "b!DcvTfUkibESq94RyGJFs-UhqWZU646tBrEagKKMKiOc";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string WebUrl =
        "https://contoso.sharepoint.com/contentstorage/CSP_7dd3cb0d-2249-446c-aaf7-847218916cf9/Document%20Library";

    // ─────────────────────────────────────────────────────────────────────────
    // The request
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The load-bearing one — without the expand there is no URL, and no error either.</summary>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_ExpandsTheDrive_BecauseTheContainerItselfHasNoUrlProperty()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {"id":"{{ContainerId}}","displayName":"Legal Matters"}
            """);

        await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        var query = Uri.UnescapeDataString(
            graph.RequestsFor($"{ContainersPath}/{ContainerId}").Single().RawQuery);

        query.Should().Contain("$expand=drive",
            "the container entity exposes no URL property in either API version — the URL is only " +
            "reachable through the drive navigation property, so removing this expand silently " +
            "empties the field rather than failing");
        query.Should().Contain("webUrl",
            "the nested $select keeps the expand cheap; without it Graph returns the entire drive");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The mapping
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_MapsTheWebUrl_FromTheExpandedDrive()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {
              "id":"{{ContainerId}}",
              "displayName":"Legal Matters",
              "drive":{"webUrl":"{{WebUrl}}"}
            }
            """);

        var result = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        result!.WebUrl.Should().Be(WebUrl,
            "this is the value an administrator pastes into Purview to scope an eDiscovery search");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Absence must stay absence
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_WhenGraphReturnsNoDrive_LeavesWebUrlNull_NotEmptyString()
    {
        // A container that is still provisioning genuinely has no drive yet. "" renders as a blank
        // cell, which reads as "this container has no URL"; null is the only value that lets the UI
        // say "not reported" (NFR-06).
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {"id":"{{ContainerId}}","displayName":"Still Provisioning"}
            """);

        var result = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        result!.WebUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetContainer_WhenDrivePresentButUrlBlank_LeavesWebUrlNull()
    {
        // Graph returning an empty string is not Graph reporting a URL. Passing "" through would
        // produce a copy button that copies nothing.
        using var graph = new GraphWireMockFixture();
        graph.StubGet($"{ContainersPath}/{ContainerId}", $$"""
            {
              "id":"{{ContainerId}}",
              "displayName":"Blank Url",
              "drive":{"webUrl":""}
            }
            """);

        var result = await CreateSut().GetContainerAsync(graph.CreateGraphClient(), ContainerId);

        result!.WebUrl.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The list contract — the deliberate omission
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Guards the <c>JsonIgnore(WhenWritingNull)</c> on <c>ContainerDto.WebUrl</c>.
    /// </summary>
    /// <remarks>
    /// Not a JSON round-trip test (ADR-038 B12): it pins a deliberate wire-contract decision whose
    /// removal has a named consequence. Drop the attribute and every list row starts carrying
    /// <c>"webUrl": null</c>, which invites exactly one reading — "these containers have no URL" —
    /// when the truth is that Graph was never asked, because on the collection it cannot answer.
    /// </remarks>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void ListRows_DoNotCarryAWebUrlKey_BecauseGraphCannotSupplyItOnACollection()
    {
        var listRow = ContainerEndpoints.ContainerDto.FromSummary(
            new SpeAdminGraphService.SpeContainerSummary(
                Id: ContainerId,
                DisplayName: "Legal Matters",
                Description: null,
                ContainerTypeId: "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
                CreatedDateTime: null,
                StorageUsedInBytes: null,
                Status: "active"));

        var json = JsonSerializer.Serialize(listRow, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContain("webUrl",
            "emitting webUrl:null on list rows would assert that these containers have no URL; the " +
            "truth is that the containers collection cannot return one, so the key is omitted and " +
            "the client resolves it per container on demand");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void DetailRow_DoesCarryTheWebUrl_WhenGraphReportedOne()
    {
        // The other half of the same contract: omission must be driven by absence, not by the DTO
        // having quietly stopped carrying the field at all.
        var detail = ContainerEndpoints.ContainerDto.FromSummary(
            new SpeAdminGraphService.SpeContainerSummary(
                Id: ContainerId,
                DisplayName: "Legal Matters",
                Description: null,
                ContainerTypeId: "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
                CreatedDateTime: null,
                StorageUsedInBytes: null,
                Status: "active",
                WebUrl: WebUrl));

        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("webUrl").And.Contain(WebUrl);
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
