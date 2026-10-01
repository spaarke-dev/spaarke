using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract tests for <c>POST /api/v1/documents</c> ownership (spaarkeai-word-add-in-r1 task 080, invariant I-6):
/// the owner of the created <c>sprk_document</c> is decided on the SERVER — the caller's business-unit default owner
/// team — and never by the request body.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect these pin.</b> The endpoint binds the request body straight into
/// <see cref="CreateDocumentRequest"/>. Task 080 had added <see cref="CreateDocumentRequest.OwningTeamId"/> and task 014
/// <see cref="CreateDocumentRequest.Id"/> as SERVER-side inputs, but neither was excluded from binding — so any caller
/// could name the team (and so the business unit, a secure one included) that owns the document it creates, and choose
/// its primary key. Both are now <c>[JsonIgnore]</c>, and the endpoint resolves the owner itself.
/// </para>
/// <para>
/// The create is app-only: with no explicit owner, Dataverse would make the BFF application user the owner, in the ROOT
/// business unit, where the caller could not read what they had just created.
/// </para>
/// </remarks>
[Trait("status", "new")]
public class DocumentCreateOwnershipContractTests
{
    private const string Route = "/api/v1/documents";

    [Fact]
    public async Task PostDocument_WhenTheBodyNamesAnOwnerTeamAndAnId_OwnsItByTheCallersTeam_AndNeverTakesTheId()
    {
        using var factory = new DocumentCreateTestWebAppFactory();
        var attackerChosenTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000002");

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = "b!test-container",
            owningTeamId = attackerChosenTeam,
            id = Guid.Parse("00000000-0000-0000-0000-00000000beef"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = factory.CreatedRequests.Should().ContainSingle().Subject;
        created.OwningTeamId.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId)
            .And.NotBe(attackerChosenTeam, "the owner is a server decision, never a wire field");
        created.Id.Should().BeNull("only the Office save path sets a primary key, in server code");
        // NOTE: the OwningTeamId assertion above holds even without [JsonIgnore] on that property, because the endpoint
        // overwrites it with the resolved team — the attribute is defence-in-depth for any future binder of the type.
        // The Id assertion is the one that pins [JsonIgnore]: removing it from Id turns this test red.
        factory.Ownership.Requests.Should().ContainSingle().Which.HasTarget.Should().BeFalse(
            "the body names no record, so the caller's business unit decides");
    }

    [Fact]
    public async Task PostDocument_WhenNoOwnerTeamResolves_Returns403_AndCreatesNothing()
    {
        using var factory = new DocumentCreateTestWebAppFactory();
        factory.Ownership.TeamId = null;

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = "b!test-container",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"errorCode\":\"OFFICE_022\"",
            "one code for 'no owner team' across every create surface");
        factory.CreatedRequests.Should().BeEmpty("an app-owned row in ROOT is the defect, not a fallback");
    }

    /// <summary>The Office test host, with a Dataverse double that records every document create.</summary>
    private sealed class DocumentCreateTestWebAppFactory : OfficeTestWebAppFactory
    {
        public List<CreateDocumentRequest> CreatedRequests { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                var dataverse = new Mock<IDataverseService>();
                dataverse.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
                dataverse
                    .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<CreateDocumentRequest, CancellationToken>((request, _) => CreatedRequests.Add(request))
                    .ReturnsAsync(() => Guid.NewGuid().ToString());
                services.RemoveAll<IDataverseService>();
                services.AddSingleton(dataverse.Object);
            });
        }
    }
}
