using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 035 (FR-05 server half, FR-06 intake half):
/// <c>POST /api/v1/external/servicerequests</c> through the REAL <c>/api/v1/external</c> group, route and handler, with the
/// partner switch OFF (the default). The switch-ON cases are <see cref="ExternalIntakeEndpointsPartnerSwitchOnTests"/>.
/// </summary>
/// <remarks>
/// <para>Doubles sit at module boundaries only: the caller principal (<see cref="ICallerPrincipalResolver"/>), the Dataverse
/// create (<see cref="ExternalDataService.CreateServiceRequestAsync"/>, kept in memory and fed by the REAL
/// <see cref="ExternalDataService.BuildServiceRequestCreatePayload"/>), and the entitlement resolver's App-Role map read.
/// The entitlement RULE is the real <see cref="ModuleEntitlementResolver"/>, the partner switch is the host's bound
/// <see cref="ExternalAccessOptions"/>, and the "service-requests" descriptor is the host's own registry entry — the three
/// that review 011 K2 requires to agree.</para>
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalIntakeEndpointsTests : IClassFixture<ExternalIntakeEndpointsTests.SwitchOffFixture>
{
    internal const string Route = "/api/v1/external/servicerequests";
    internal const int NdaAssessment = 100000000;
    internal const int Inbound = 100000000;

    private readonly SwitchOffFixture _fixture;

    public ExternalIntakeEndpointsTests(SwitchOffFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // =====================================================================
    // AC 1: a workforce caller's valid NDA Assessment → 201 and the six columns (the five + the owner)
    // =====================================================================

    [Fact]
    public async Task WorkforceSystemUser_SubmitsNdaAssessment_Gets201_AndTheRowCarriesTheSixIntakeColumns()
    {
        var caller = IntakeTestData.SystemUser();
        _fixture.Principal = caller;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA with Contoso",
            """{"counterparty":"Contoso Ltd","mutual":true,"term":{"years":2}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<ExternalServiceRequestCreatedDto>();
        var row = _fixture.Data.Created.Should().ContainSingle().Subject;
        created!.SprkServicerequestid.Should().Be(row.Id.ToString("D"));

        row.Payload.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_name"] = "NDA with Contoso",
            ["sprk_requesttype"] = NdaAssessment,
            ["sprk_intakedata"] = row.Payload["sprk_intakedata"],
            ["sprk_direction"] = Inbound,
            ["sprk_RequestedBy@odata.bind"] = $"/contacts({caller.ContactId:D})",
            [ExternalDataService.OwnerBindKey] = $"/teams({RecordOwnershipResolverDouble.DefaultTeamId})",
        }, "the intake writes exactly these six columns - the five of the request and the owning team; no creator stamp, nothing from the client");
        JsonDocument.Parse((string)row.Payload["sprk_intakedata"]!).RootElement.GetProperty("counterparty").GetString()
            .Should().Be("Contoso Ltd", "sprk_intakedata stores the submitted answers as JSON");
    }

    [Theory]
    [InlineData(100000000)]
    [InlineData(100000001)]
    [InlineData(100000002)]
    [InlineData(100000003)]
    public async Task WorkforceContact_SubmitsEachRequestType_Gets201_WithThatTypeAndTheirContactAsRequester(int requestType)
    {
        var caller = IntakeTestData.WorkforceContact();
        _fixture.Principal = caller;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(requestType, "Request", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var row = _fixture.Data.Created.Should().ContainSingle().Subject;
        row.Payload["sprk_requesttype"].Should().Be(requestType);
        IntakeTestData.RequesterOf(row).Should().Be(caller.ContactId);
    }

    // =====================================================================
    // AC 2: the row is in the caller's Service Requests read and in no other caller's
    // =====================================================================

    [Fact]
    public async Task SubmittedRow_IsInTheCallersServiceRequestsRead_AndInNoOtherCallers()
    {
        var caller = IntakeTestData.WorkforceContact();
        _fixture.Principal = caller;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var readRow = IntakeTestData.AsRead(_fixture.Data.Created.Single());
        var module = _fixture.ServiceRequestsModule();

        module.ScopeRows(caller, new[] { readRow }).Should().ContainSingle("the caller's own request is in their tab");
        module.ScopeRows(IntakeTestData.WorkforceContact(), new[] { readRow }).Should().BeEmpty();
        module.ScopeRows(IntakeTestData.SystemUser(), new[] { readRow }).Should().BeEmpty();
        module.ScopeRows(IntakeTestData.Ciam(), new[] { readRow }).Should().BeEmpty();
    }

    // =====================================================================
    // AC 3 + K2: a CIAM caller is refused while the switch is off — the route, /me and the descriptor agree
    // =====================================================================

    [Fact]
    public async Task CiamCaller_SwitchOff_Gets403_NothingIsCreated_AndMeAndTheDescriptorAgree()
    {
        var caller = IntakeTestData.Ciam();
        _fixture.Principal = caller;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntakeTestData.ProblemCode(response, "reasonCode")).Should().Be(ExternalIntakeEndpoints.DenyModuleNotEntitled);
        _fixture.Data.Created.Should().BeEmpty();

        var me = await client.GetFromJsonAsync<MeEntitlementsResponse>("/api/v1/external/me/entitlements");
        me!.Entitlements.Should().NotContain("legal-front-door");
        _fixture.ServiceRequestsModule().AccessibleRecordIds!(caller).Should().BeEmpty();
    }

    // =====================================================================
    // AC 5: a caller not entitled to legal-front-door → 403 (the route asks the resolver, not the plane)
    // =====================================================================

    [Fact]
    public async Task WorkforceCaller_TheResolverDoesNotEntitleToLegalFrontDoor_Gets403_AndNothingIsCreated()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.Entitlements.Override = new[] { "policy-library" };

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntakeTestData.ProblemCode(response, "reasonCode")).Should().Be(ExternalIntakeEndpoints.DenyModuleNotEntitled);
        _fixture.Data.Created.Should().BeEmpty();
    }

    // =====================================================================
    // AC 6: unknown request type, invalid title, invalid or oversize answers → 400
    // =====================================================================

    [Theory]
    [InlineData("""{"title":"t","answers":{}}""")]
    [InlineData("""{"requestType":0,"title":"t","answers":{}}""")]
    [InlineData("""{"requestType":100000004,"title":"t","answers":{}}""")]
    [InlineData("""{"requestType":-1,"title":"t","answers":{}}""")]
    public async Task UnknownRequestType_Gets400_AndNothingIsCreated(string json)
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Json(json));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntakeTestData.ProblemCode(response, "errorCode")).Should().Be(ExternalIntakeEndpoints.ErrorInvalidRequestType);
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"requestType":100000000,"answers":{}}""")]
    [InlineData("""{"requestType":100000000,"title":"   ","answers":{}}""")]
    public async Task MissingOrBlankTitle_Gets400_AndNothingIsCreated(string json)
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Json(json));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntakeTestData.ProblemCode(response, "errorCode")).Should().Be(ExternalIntakeEndpoints.ErrorInvalidTitle);
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task TitleLongerThanSprkName_Gets400_AndNothingIsCreated()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route,
            IntakeTestData.Body(NdaAssessment, new string('t', ExternalIntakeEndpoints.MaxTitleLength + 1), "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntakeTestData.ProblemCode(response, "errorCode")).Should().Be(ExternalIntakeEndpoints.ErrorInvalidTitle);
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnswersOver100Kb_Gets400_AndNothingIsCreated()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route,
            IntakeTestData.Body(NdaAssessment, "NDA", IntakeTestData.AnswersOfBytes(ExternalIntakeEndpoints.MaxAnswersBytes + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntakeTestData.ProblemCode(response, "errorCode")).Should().Be(ExternalIntakeEndpoints.ErrorInvalidAnswers);
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnswersOfExactly100Kb_Gets201()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        var answers = IntakeTestData.AnswersOfBytes(ExternalIntakeEndpoints.MaxAnswersBytes);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", answers));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        ((string)_fixture.Data.Created.Single().Payload["sprk_intakedata"]!).Should().Be(answers);
    }

    [Theory]
    [InlineData("""{"requestType":100000000,"title":"t"}""")]
    [InlineData("""{"requestType":100000000,"title":"t","answers":null}""")]
    [InlineData("""{"requestType":100000000,"title":"t","answers":[{"q":"a"}]}""")]
    [InlineData("""{"requestType":100000000,"title":"t","answers":"{\"q\":\"a\"}"}""")]
    [InlineData("""{"requestType":100000000,"title":"t","answers":42}""")]
    public async Task AnswersThatAreNotAJsonObject_Get400_AndNothingIsCreated(string json)
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Json(json));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntakeTestData.ProblemCode(response, "errorCode")).Should().Be(ExternalIntakeEndpoints.ErrorInvalidAnswers);
        _fixture.Data.Created.Should().BeEmpty();
    }

    // =====================================================================
    // AC 7: a requester, owner or creator named in the body is ignored
    // =====================================================================

    [Fact]
    public async Task RequesterOwnerAndCreatorNamedInTheBody_AreIgnored_TheRequesterIsTheCaller()
    {
        var caller = IntakeTestData.WorkforceContact();
        var someoneElse = Guid.NewGuid();
        _fixture.Principal = caller;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Json($$"""
            {
              "requestType": 100000000,
              "title": "NDA",
              "answers": {},
              "requestedBy": "{{someoneElse}}",
              "sprk_requestedby": "{{someoneElse}}",
              "sprk_RequestedBy@odata.bind": "/contacts({{someoneElse}})",
              "ownerid@odata.bind": "/teams({{someoneElse}})",
              "sprk_createdbycontact": "{{someoneElse}}",
              "sprk_direction": 100000001
            }
            """));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var row = _fixture.Data.Created.Single();
        IntakeTestData.RequesterOf(row).Should().Be(caller.ContactId);
        row.Payload["sprk_direction"].Should().Be(Inbound);
        row.Payload.Keys.Should().NotContain(k => k.StartsWith("sprk_createdby", StringComparison.OrdinalIgnoreCase));
        row.Payload[ExternalDataService.OwnerBindKey].Should().Be($"/teams({RecordOwnershipResolverDouble.DefaultTeamId})",
            "the owner is the resolver's team for the caller's contact, never one named in the body");
        row.Payload.Values.OfType<string>().Should().NotContain(v => v.Contains(someoneElse.ToString()));
    }

    // =====================================================================
    // A caller with no contact → 403 with a clear reason
    // =====================================================================

    [Fact]
    public async Task SystemUserWithNoLinkedContact_Gets403_NoContact_AndNothingIsCreated()
    {
        _fixture.Principal = IntakeTestData.SystemUser(contactId: Guid.Empty);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("reasonCode").GetString().Should().Be(ExternalIntakeEndpoints.DenyNoContact);
        problem.RootElement.GetProperty("detail").GetString().Should().Contain("not linked to a contact");
        _fixture.Data.Created.Should().BeEmpty();
    }

    // =====================================================================
    // Review fix round (F1): a contact that could not be READ is a retryable 503, not "not linked"
    // =====================================================================

    [Fact]
    public async Task SystemUserWhoseContactCouldNotBeRead_Gets503_NotTheNoContact403_AndNothingIsCreated()
    {
        _fixture.Principal = IntakeTestData.SystemUser(contactId: Guid.Empty, contactUnreadable: true);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be(ExternalIntakeEndpoints.ErrorContactUnreadable);
        problem.RootElement.GetProperty("detail").GetString().Should().Contain("try again");
        problem.RootElement.TryGetProperty("reasonCode", out _).Should().BeFalse("it is not the final no_contact deny");
        _fixture.Data.Created.Should().BeEmpty();
    }

    // =====================================================================
    // Review fix round (F2): Dataverse failures on the create
    // =====================================================================

    [Fact]
    public async Task DataverseUnavailableOnTheCreate_Gets503_WithTheErrorCode_AndNoExceptionText()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.Data.ThrowOnCreate = new ExternalDataService.DataverseUnavailableException(
            "SECRET-HOST-org1234.crm.dynamics.com answered 429", new HttpRequestException("SECRET-INNER-TEXT"));

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, raw);
        using var problem = JsonDocument.Parse(raw);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be(ExternalIntakeEndpoints.ErrorDataverseUnavailable);
        problem.RootElement.GetProperty("detail").GetString().Should().Be("Nothing was submitted; try again.");
        raw.Should().NotContain("SECRET");
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AnyOtherFailureOnTheCreate_Gets500_WithTheInternalErrorCode_AndNoExceptionText()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.Data.ThrowOnCreate = new InvalidOperationException("SECRET-PAYLOAD-REJECTED sprk_intakedata");

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, raw);
        (await IntakeTestData.ProblemCode(response, "errorCode")).Should().Be("EXT_INTAKE_INTERNAL_ERROR");
        raw.Should().NotContain("SECRET");
    }

    // =====================================================================
    // Owner decision 2026-10-11 (I-6): owned by the default team of the BU that owns the requester's contact
    // =====================================================================

    [Fact]
    public async Task TheRow_IsOwnedByTheDefaultTeamOfTheBusinessUnitThatOwnsTheRequestersContact()
    {
        var caller = IntakeTestData.WorkforceContact();
        var contactBuTeam = Guid.Parse("0f0f0f0f-0035-4035-8035-000000000035");
        _fixture.Principal = caller;
        _fixture.Ownership.TeamId = contactBuTeam;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.Created.Single().Payload[ExternalDataService.OwnerBindKey].Should().Be($"/teams({contactBuTeam})");

        var asked = _fixture.Ownership.Requests.Should().ContainSingle().Subject;
        asked.TargetEntityLogicalName.Should().Be("contact", "the record whose business unit decides is the requester's contact");
        asked.TargetRecordId.Should().Be(caller.ContactId);
        asked.CallerSystemUserId.Should().BeNull("the acting user's business unit never decides - most callers have no systemuser");
    }

    [Fact]
    public async Task AnUnresolvedOwningTeam_IsRefused500_WithTheErrorCode_NothingIsCreated_AndNoReasonLeaks()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.Ownership.TeamId = null;
        _fixture.Ownership.RefusalCode = RecordOwnerRefusal.NoDefaultOwnerTeam;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, raw);
        using var problem = JsonDocument.Parse(raw);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be(ExternalIntakeEndpoints.ErrorOwnerUnresolved);
        raw.Should().NotContain(RecordOwnerRefusal.NoDefaultOwnerTeam, "the resolver's code and reason are logged, not returned");
        _fixture.Data.Created.Should().BeEmpty("an unresolved owner refuses the create - never an application-owned row");
        _fixture.Data.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task AResolverReadFault_Gets503_WithTheDataverseUnavailableCode_NothingIsCreated_AndNoExceptionText()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.Ownership.Fault = new InvalidOperationException("SECRET-HOST-org1234.crm.dynamics.com answered 429");

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, "NDA", "{}"));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, raw);
        using var problem = JsonDocument.Parse(raw);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be(ExternalIntakeEndpoints.ErrorDataverseUnavailable);
        problem.RootElement.GetProperty("detail").GetString().Should().Be("Nothing was submitted; try again.");
        raw.Should().NotContain("SECRET");
        _fixture.Data.Created.Should().BeEmpty();
        _fixture.Data.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnInvalidSubmission_NeverAsksTheResolver()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Json("""{"requestType":100000004,"title":"t","answers":{}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Ownership.Requests.Should().BeEmpty("no Dataverse read happens before the submission is valid");
    }

    // =====================================================================
    // Review fix round (F3): the per-route body cap
    // =====================================================================

    // The test host does not enforce RequestSizeLimitAttribute (TestServer's request has no IHttpMaxRequestBodySizeFeature, so a
    // 256 KB + 1 body came back as the handler's 400, not 413). Kestrel enforces it on the real host; here the metadata the
    // platform enforces is asserted on the endpoint, the same proof the Compose routes' contract test uses.
    [Fact]
    public void TheRoute_CarriesTheRequestSizeLimitMetadata()
    {
        // Independent of how the test host enforces the limit: the metadata the platform enforces is on the endpoint.
        var endpoint = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/external/servicerequests"
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize
            .Should().Be(ExternalIntakeEndpoints.MaxRequestBodyBytes);
    }

    // =====================================================================
    // Review fix round: the 850-character title boundary
    // =====================================================================

    [Fact]
    public async Task TitleOfExactly850Characters_Gets201()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        var title = new string('t', ExternalIntakeEndpoints.MaxTitleLength);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(Route, IntakeTestData.Body(NdaAssessment, title, "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.Created.Single().Payload["sprk_name"].Should().Be(title);
    }

    // =====================================================================
    // Fixture
    // =====================================================================

    /// <summary>The external collaboration host, partner switch OFF (the default).</summary>
    public sealed class SwitchOffFixture : IntakeFixture
    {
        public SwitchOffFixture() : base(partnerServiceRequestsEnabled: false) { }
    }
}

/// <summary>
/// Task 035 with <c>ExternalAccess:PartnerServiceRequestsEnabled</c> ON (FR-06): a CIAM partner submits, and the request
/// is visible only to that partner. Same host shape as <see cref="ExternalIntakeEndpointsTests"/>, switch bound on.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalIntakeEndpointsPartnerSwitchOnTests
    : IClassFixture<ExternalIntakeEndpointsPartnerSwitchOnTests.SwitchOnFixture>
{
    private readonly SwitchOnFixture _fixture;

    public ExternalIntakeEndpointsPartnerSwitchOnTests(SwitchOnFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task CiamCaller_SwitchOn_Gets201_AndTheRowIsVisibleOnlyToThatCaller_AndMeAgrees()
    {
        var caller = IntakeTestData.Ciam();
        _fixture.Principal = caller;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(ExternalIntakeEndpointsTests.Route,
            IntakeTestData.Body(100000003, "Trademark search: SPAARKE", """{"mark":"SPAARKE","classes":[9,42]}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var row = _fixture.Data.Created.Should().ContainSingle().Subject;
        IntakeTestData.RequesterOf(row).Should().Be(caller.ContactId);
        row.Payload["sprk_direction"].Should().Be(ExternalIntakeEndpointsTests.Inbound);

        var readRow = IntakeTestData.AsRead(row);
        var module = _fixture.ServiceRequestsModule();
        module.ScopeRows(caller, new[] { readRow }).Should().ContainSingle();
        module.ScopeRows(IntakeTestData.Ciam(), new[] { readRow }).Should().BeEmpty("another partner never sees it");
        module.ScopeRows(IntakeTestData.WorkforceContact(), new[] { readRow }).Should().BeEmpty();

        var me = await client.GetFromJsonAsync<MeEntitlementsResponse>("/api/v1/external/me/entitlements");
        me!.Entitlements.Should().Contain("legal-front-door");
    }

    [Fact]
    public async Task WorkforceCaller_SwitchOn_IsUnchanged_Gets201()
    {
        var caller = IntakeTestData.WorkforceContact();
        _fixture.Principal = caller;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(ExternalIntakeEndpointsTests.Route,
            IntakeTestData.Body(100000002, "Policy question", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        IntakeTestData.RequesterOf(_fixture.Data.Created.Single()).Should().Be(caller.ContactId);
    }

    /// <summary>The external collaboration host, partner switch ON.</summary>
    public sealed class SwitchOnFixture : IntakeFixture
    {
        public SwitchOnFixture() : base(partnerServiceRequestsEnabled: true) { }
    }
}

/// <summary>
/// The external collaboration host for the intake route: the stub principal, an in-memory service-request create, the
/// partner switch configured, and a <see cref="ModuleEntitlementResolver"/> whose App-Role map is an (empty) fixture.
/// </summary>
public abstract class IntakeFixture : ExternalCollaborationTestFixture
{
    private readonly bool _partnerServiceRequestsEnabled;
    private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();

    protected IntakeFixture(bool partnerServiceRequestsEnabled) =>
        _partnerServiceRequestsEnabled = partnerServiceRequestsEnabled;

    public InMemoryServiceRequestData Data { get; } = new();

    /// <summary>The owner resolver, at the module boundary: the team every contact's business unit answers with.</summary>
    public RecordOwnershipResolverDouble Ownership { get; } = new();

    public EntitlementOverride Entitlements { get; } = new();

    public CallerPrincipal? Principal
    {
        get => _principals.Principal;
        set => _principals.Principal = value;
    }

    public void Reset()
    {
        Data.Reset();
        Ownership.TeamId = RecordOwnershipResolverDouble.DefaultTeamId;
        Ownership.RefusalCode = RecordOwnerRefusal.SecureParentNotIsolated;
        Ownership.Fault = null;
        Ownership.Requests.Clear();
        Entitlements.Override = null;
        Principal = null;
    }

    /// <summary>The host's own registered "service-requests" descriptor (built from the host's bound switch).</summary>
    public ExternalModuleDescriptor ServiceRequestsModule() =>
        Services.GetRequiredService<ExternalModuleRegistry>().FindByName("service-requests")
        ?? throw new InvalidOperationException("The service-requests descriptor is not registered.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // The switch, bound as production binds it: the descriptor factory and the resolver both read this value.
            services.Configure<ExternalAccessOptions>(o => o.PartnerServiceRequestsEnabled = _partnerServiceRequestsEnabled);

            services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
            services.AddScoped(_ => (ExternalDataService)Data);
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton<IRecordOwnershipResolver>(Ownership);
            services.RemoveAll<ModuleEntitlementResolver>();
            services.AddScoped<ModuleEntitlementResolver>(sp => new FixtureEntitlementResolver(
                sp.GetRequiredService<IOptions<ExternalAccessOptions>>(), Entitlements));
        });
    }

    /// <summary>A test's answer in place of the resolver's own (null: the real rule decides).</summary>
    public sealed class EntitlementOverride
    {
        public IReadOnlyList<string>? Override { get; set; }
    }

    /// <summary>The REAL resolver with its App-Role map read replaced by an empty fixture (no HTTP).</summary>
    private sealed class FixtureEntitlementResolver : ModuleEntitlementResolver
    {
        private readonly EntitlementOverride _override;

        public FixtureEntitlementResolver(IOptions<ExternalAccessOptions> options, EntitlementOverride entitlementOverride)
            : base(new HttpClient(), new InMemoryTenantCache(), new ConfigurationBuilder().Build(), new FakeCredential(),
                   new HttpContextAccessor(), options, NullLogger<ModuleEntitlementResolver>.Instance)
        {
            _override = entitlementOverride;
        }

        public override Task<IReadOnlyList<string>> ResolveAsync(
            CallerPrincipal principal, ClaimsPrincipal user, CancellationToken ct = default) =>
            _override.Override is { } fixed_ ? Task.FromResult(fixed_) : base.ResolveAsync(principal, user, ct);

        public override Task<IReadOnlyList<AppRoleModuleMapping>> GetActiveMapAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AppRoleModuleMapping>>(Array.Empty<AppRoleModuleMapping>());
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("test-token", DateTimeOffset.MaxValue));
    }
}

/// <summary>One created service request: its id and the payload the REAL builder produced.</summary>
public sealed record CreatedServiceRequest(Guid Id, IReadOnlyDictionary<string, object?> Payload);

/// <summary>The service-request create in memory: each create records the REAL builder's payload.</summary>
public sealed class InMemoryServiceRequestData : ExternalDataService
{
    private readonly ConcurrentQueue<CreatedServiceRequest> _created = new();

    public InMemoryServiceRequestData()
        : base(new HttpClient(), new ConfigurationBuilder().Build(), new NoCredential(), NullLogger<ExternalDataService>.Instance)
    {
    }

    public IReadOnlyList<CreatedServiceRequest> Created => _created.ToList();

    /// <summary>When set, the next creates throw it (a Dataverse fault) instead of recording a row.</summary>
    public Exception? ThrowOnCreate { get; set; }

    /// <summary>How many times the create was reached - zero proves a refused submission never got near Dataverse.</summary>
    public int CreateCalls => Volatile.Read(ref _createCalls);

    private int _createCalls;

    public void Reset()
    {
        _created.Clear();
        ThrowOnCreate = null;
        Interlocked.Exchange(ref _createCalls, 0);
    }

    public override Task<Guid> CreateServiceRequestAsync(
        Guid requesterContactId, Guid owningTeamId, int requestType, string name, string intakeDataJson,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _createCalls);
        if (ThrowOnCreate is { } fault)
        {
            throw fault;
        }

        var payload = BuildServiceRequestCreatePayload(requesterContactId, owningTeamId, requestType, name, intakeDataJson);
        var id = Guid.NewGuid();
        _created.Enqueue(new CreatedServiceRequest(id, payload));
        return Task.FromResult(id);
    }

    private sealed class NoCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The in-memory store never acquires a token.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The in-memory store never acquires a token.");
    }
}

/// <summary>Principals, bodies and row projections shared by the intake tests.</summary>
internal static class IntakeTestData
{
    private static readonly Regex ContactBind = new(@"^/contacts\((?<id>[0-9a-fA-F-]{36})\)$", RegexOptions.Compiled);

    public static CallerPrincipal Ciam() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "partner@outside.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    public static CallerPrincipal WorkforceContact() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = null,
        Email = "staff@customer.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    /// <summary>A licensed workforce caller; <paramref name="contactId"/> <see cref="Guid.Empty"/> = no linked contact.</summary>
    public static CallerPrincipal SystemUser(Guid? contactId = null, bool contactUnreadable = false) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = contactId ?? Guid.NewGuid(),
        ContactUnreadable = contactUnreadable,
        SystemUserId = Guid.NewGuid(),
        Email = "lawyer@spaarke.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static StringContent Body(int requestType, string title, string answersJson) =>
        Json($$"""{"requestType":{{requestType}},"title":{{JsonSerializer.Serialize(title)}},"answers":{{answersJson}}}""");

    /// <summary>A JSON object whose raw text is exactly <paramref name="bytes"/> UTF-8 bytes.</summary>
    public static string AnswersOfBytes(int bytes)
    {
        const string prefix = "{\"text\":\"";
        const string suffix = "\"}";
        var answers = prefix + new string('a', bytes - prefix.Length - suffix.Length) + suffix;
        Encoding.UTF8.GetByteCount(answers).Should().Be(bytes);
        return answers;
    }

    /// <summary>The requester contact the row was created with (parsed from the bind the REAL builder wrote).</summary>
    public static Guid RequesterOf(CreatedServiceRequest row)
    {
        var bind = row.Payload[ExternalDataService.RequestedByBindKey] as string;
        var match = ContactBind.Match(bind ?? string.Empty);
        match.Success.Should().BeTrue($"sprk_requestedby must bind a contact, got '{bind}'");
        return Guid.Parse(match.Groups["id"].Value);
    }

    /// <summary>The created row as the module read returns it: <c>sprk_requestedby</c> as the SDK's EntityReference.</summary>
    public static IReadOnlyDictionary<string, object?> AsRead(CreatedServiceRequest row) => new Dictionary<string, object?>
    {
        ["@logicalName"] = "sprk_servicerequest",
        ["sprk_servicerequestid"] = row.Id,
        ["sprk_name"] = row.Payload["sprk_name"],
        ["sprk_requestedby"] = new EntityReference("contact", RequesterOf(row)),
    };

    public static async Task<string?> ProblemCode(HttpResponseMessage response, string property)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty(property, out var code) ? code.GetString() : null;
    }
}
