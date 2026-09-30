using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// HTTP contract of <c>GET /api/office/search/matter-types</c> (spaarkeai-word-add-in-r1 task 038): a small,
/// load-once list of active <c>sprk_mattertype_ref</c> rows for the pane's required Matter Type field.
/// </summary>
/// <remarks>
/// <para>
/// Per this project's boundary rules, this is a NEW file (not an addition to
/// <c>OfficeEndpointsContractTests.cs</c>), and it configures its OWN local double for
/// <see cref="DataverseWebApiClient"/> via <c>WithWebHostBuilder</c> rather than modifying the shared
/// <see cref="OfficeTestWebAppFactory"/> — the same "configure locally, don't touch the shared fixture"
/// approach <c>OfficeVersionSaveContractTests</c> uses for its own world.
/// </para>
/// <para>
/// <see cref="DataverseWebApiClient"/> is a concrete class (registered as itself, ADR-010), so it is
/// mocked the same way the shared factory already mocks <c>SpeFileStore</c>: construct
/// <see cref="Mock{T}"/> with the real constructor's arguments and override the one virtual member the
/// code path calls (<c>QueryAsync</c>).
/// </para>
/// </remarks>
[Trait("status", "new")]
public class OfficeMatterTypeLookupContractTests : IClassFixture<OfficeTestWebAppFactory>
{
    private readonly OfficeTestWebAppFactory _factory;

    public OfficeMatterTypeLookupContractTests(OfficeTestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_OfficeMatterTypes_WhenUnauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var response = await client.GetAsync("/api/office/search/matter-types");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_OfficeMatterTypes_ReturnsActiveRows_OrderedByName_NoQueryRequired()
    {
        // Arrange — a local DataverseWebApiClient double returning three unordered rows, one of which
        // has no name (must be dropped, never surfaced — same rule as MapSearchRow).
        var rows = new List<Dictionary<string, JsonElement>>
        {
            Row("""{ "sprk_mattertype_refid": "6cedd99b-30da-f011-8406-7ced8d1dc988", "sprk_mattertypename": "Trademark", "sprk_mattertypecode": "TMRK" }"""),
            Row("""{ "sprk_mattertype_refid": "11aed095-30da-f011-8406-7ced8d1dc988", "sprk_mattertypename": "Litigation", "sprk_mattertypecode": "LITG" }"""),
            Row("""{ "sprk_mattertype_refid": "46c35aa2-30da-f011-8406-7ced8d1dc988", "sprk_mattertypename": "" }"""),
        };

        var dataverseClientMock = new Mock<DataverseWebApiClient>(
            MockBehavior.Loose,
            Mock.Of<IConfiguration>(c => c["Dataverse:ServiceUrl"] == "https://test.crm.dynamics.com"),
            Mock.Of<ILogger<DataverseWebApiClient>>(),
            new NoOpTokenCredential(),
            (IConfidentialClientProvider)null!);
        dataverseClientMock
            .Setup(c => c.QueryAsync<Dictionary<string, JsonElement>>(
                "sprk_mattertype_refs",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DataverseWebApiClient>();
                services.AddSingleton(dataverseClientMock.Object);
            });
        });

        // Act
        var response = await scopedFactory.CreateClient().GetAsync("/api/office/search/matter-types");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<MatterTypeListResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().HaveCount(2, "the unnamed row must be dropped");
        result.Results.Select(r => r.Name).Should().ContainInOrder("Litigation", "Trademark");
        result.Results.Should().Contain(r => r.Name == "Litigation" && r.Code == "LITG");

        dataverseClientMock.Verify(
            c => c.QueryAsync<Dictionary<string, JsonElement>>(
                "sprk_mattertype_refs",
                "statecode eq 0",
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "only active (statecode eq 0) rows are requested");
    }

    private static Dictionary<string, JsonElement> Row(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    /// <summary>A <see cref="TokenCredential"/> that answers instantly and never touches the network —
    /// local to this file per the "configure locally" boundary rule (mirrors the shared, but
    /// inaccessible, <c>TestTokenCredential.StubTokenCredential</c>).</summary>
    private sealed class NoOpTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("stub-token-not-a-real-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
