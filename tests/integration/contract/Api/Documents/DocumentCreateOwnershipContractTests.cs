using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.Services.Compose;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

/// <summary>
/// Contract tests for <c>POST /api/v1/documents</c>: who may create (unified-access-control-r2 task 166) and who owns
/// what is created (spaarkeai-word-add-in-r1 task 080, invariant I-6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership.</b> The owner of the created <c>sprk_document</c> is decided on the SERVER — the caller's business-unit
/// default owner team — and never by the request body. <see cref="CreateDocumentRequest.OwningTeamId"/> and
/// <see cref="CreateDocumentRequest.Id"/> are <c>[JsonIgnore]</c>, and the endpoint resolves the owner itself. The create
/// is app-only: with no explicit owner, Dataverse would make the BFF application user the owner, in the ROOT business
/// unit, where the caller could not read what they had just created.
/// </para>
/// <para>
/// <b>Authorization (task 166).</b> Because the row is written app-only, the caller's own rights are checked first, as
/// the caller: Create on <c>sprk_document</c> (through <see cref="CallerRecordAccessProbe.CallerHoldsPrivilegeAsync"/>),
/// and the body's container must be the caller's business-unit container, derived server-side by the REAL
/// <c>RecordContainerResolver</c> over the arranged Dataverse boundary (<see cref="TestActingUserBusinessUnit"/>). A
/// privilege not held and a container that is not the caller's are one 403; a fault while checking is a 403 too.
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

        var response = await factory.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = TestActingUserBusinessUnit.ContainerId,
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

        var response = await factory.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = TestActingUserBusinessUnit.ContainerId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"errorCode\":\"OFFICE_022\"",
            "one code for 'no owner team' across every create surface");
        factory.CreatedRequests.Should().BeEmpty("an app-owned row in ROOT is the defect, not a fallback");
    }

    [Fact]
    public async Task PostDocument_ACallerWithCreateNamingTheirOwnBusinessUnitContainer_Creates_AfterAskingCreateAsTheCaller()
    {
        using var factory = new DocumentCreateTestWebAppFactory();

        var response = await factory.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = TestActingUserBusinessUnit.ContainerId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.CreatedRequests.Should().ContainSingle().Which.ContainerId.Should().Be(TestActingUserBusinessUnit.ContainerId);
        factory.PrivilegesAsked.Should().Equal(new[] { "prvCreatesprk_Document" },
            "the app-only create is allowed only for a caller who could create a document themselves");
        factory.Ownership.Requests.Should().ContainSingle("the owner team is still resolved exactly as before");
    }

    [Fact]
    public async Task PostDocument_ACallerWithoutCreateOnDocuments_Is403_AndWritesNothing()
    {
        using var factory = new DocumentCreateTestWebAppFactory { HoldsCreate = false };

        var response = await factory.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = TestActingUserBusinessUnit.ContainerId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await ReadProblemAsync(response);
        body.GetProperty("reasonCode").GetString().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        body.GetProperty("detail").GetString().Should().Be(DataverseDocumentsEndpoints.DocumentCreateDeniedDetail);
        factory.CreatedRequests.Should().BeEmpty("a caller without Create on sprk_document creates nothing");
        factory.Ownership.Requests.Should().BeEmpty("the refusal comes before the owner is resolved");
    }

    [Fact]
    public async Task PostDocument_AContainerThatIsNotTheCallersBusinessUnitContainer_IsTheSame403_AndWritesNothing()
    {
        using var otherContainer = new DocumentCreateTestWebAppFactory();
        using var noPrivilege = new DocumentCreateTestWebAppFactory { HoldsCreate = false };

        var mismatch = await otherContainer.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = "b!some-other-business-unit-container",
        });
        var denied = await noPrivilege.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = TestActingUserBusinessUnit.ContainerId,
        });

        mismatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        otherContainer.CreatedRequests.Should().BeEmpty("the body's container is refused, never written");
        otherContainer.Ownership.Requests.Should().BeEmpty();

        var mismatchBody = WithoutTraceId(await ReadProblemAsync(mismatch));
        var deniedBody = WithoutTraceId(await ReadProblemAsync(denied));
        mismatchBody.Should().Be(deniedBody,
            "a container the caller may not use and a missing privilege are one response, so it names no container");
    }

    [Fact]
    public async Task PostDocument_WhenTheCallersBusinessUnitHasNoContainer_IsRefused_AndWritesNothing()
    {
        using var factory = new DocumentCreateTestWebAppFactory();
        TestActingUserBusinessUnit.ArrangeWithNoContainer(factory.Dataverse);

        var response = await factory.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = TestActingUserBusinessUnit.ContainerId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(response)).GetProperty("detail").GetString().Should()
            .Contain("Your business unit has no document container configured");
        factory.CreatedRequests.Should().BeEmpty("with no container to verify against, nothing is created");
    }

    [Fact]
    public async Task PostDocument_WhenThePrivilegeCheckThrows_Is403SystemFailure_AndWritesNothing()
    {
        using var factory = new DocumentCreateTestWebAppFactory { HoldsCreate = null };

        var response = await factory.CallerClient().PostAsJsonAsync(Route, new
        {
            name = "Summary input.pdf",
            containerId = TestActingUserBusinessUnit.ContainerId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(response)).GetProperty("reasonCode").GetString().Should()
            .Be(FinanceAuthorizationFilter.SystemFailureReasonCode, "a check that could not be answered denies (ADR-003)");
        factory.CreatedRequests.Should().BeEmpty();
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>The problem body as text with its per-request <c>traceId</c> removed, for comparing two refusals.</summary>
    private static string WithoutTraceId(JsonElement problem)
        => JsonSerializer.Serialize(problem.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .ToDictionary(p => p.Name, p => p.Value.ToString()));

    /// <summary>
    /// The Office test host, with a Dataverse double that records every document create and answers the caller's
    /// business-unit container reads, and a privilege probe whose answer each test sets.
    /// </summary>
    private sealed class DocumentCreateTestWebAppFactory : OfficeTestWebAppFactory
    {
        public DocumentCreateTestWebAppFactory()
        {
            Dataverse.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            Dataverse
                .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback<CreateDocumentRequest, CancellationToken>((request, _) => CreatedRequests.Add(request))
                .ReturnsAsync(() => Guid.NewGuid().ToString());
            TestActingUserBusinessUnit.Arrange(Dataverse);
        }

        public List<CreateDocumentRequest> CreatedRequests { get; } = new();

        /// <summary>The Dataverse boundary every service reads and writes through (GraphModule forwards to it).</summary>
        public Mock<IDataverseService> Dataverse { get; } = new();

        /// <summary>The caller's answer for a table privilege: held, not held, or <c>null</c> = the check throws.</summary>
        public bool? HoldsCreate { get; init; } = true;

        /// <summary>Every table privilege the endpoint asked about, in order.</summary>
        public List<string> PrivilegesAsked { get; } = new();

        /// <summary>A client signed in as <see cref="TestSessionOwner.Oid"/>, the caller the Dataverse double knows.</summary>
        public HttpClient CallerClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Oid", TestSessionOwner.Oid);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataverseService>();
                services.AddSingleton(Dataverse.Object);

                services.RemoveAll<CallerRecordAccessProbe>();
                services.AddScoped(_ =>
                {
                    var probe = ComposeServiceCollaborators.Probe();
                    probe.Setup(p => p.CallerHoldsPrivilegeAsync(
                            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns((string? _, string privilege, CancellationToken _) =>
                        {
                            PrivilegesAsked.Add(privilege);
                            return HoldsCreate is { } holds
                                ? Task.FromResult(holds)
                                : throw new InvalidOperationException("privilege check unavailable");
                        });
                    return probe.Object;
                });
            });
        }
    }
}
