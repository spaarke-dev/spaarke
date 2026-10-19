using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using static Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 186 (owner round 101, option (a)) at the routes: a workforce-contact caller is allowed
/// an operation only when the Workforce role holds the privilege (any depth) AND UAC's record scope allows it. Driven
/// through the REAL routes and handlers on the existing fixtures; the role read is the fixture's
/// <see cref="WorkforceCeilingDouble"/>. Systemusers and CIAM partners are not checked.
/// </summary>
internal static class CeilingRouteTestData
{
    public static CallerPrincipal WorkforceContact(Guid contact) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = contact,
        Email = "staff@customer.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    /// <summary>The owner's prerequisite with <paramref name="table"/> losing <paramref name="removed"/>.</summary>
    public static WorkforceRoleCeiling Without(string table, AccessRights removed)
    {
        var map = WorkforceCeilingDouble.OwnerPrerequisite.ToDictionary(kv => kv.Key, kv => kv.Value);
        map[table] &= ~removed;
        return WorkforceRoleCeiling.Available(map);
    }

    public static async Task<string?> ReasonCode(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }

    public static async Task ShouldBeCeilingDenial(HttpResponseMessage response, string reasonCode)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ReasonCode(response)).Should().Be(reasonCode);
    }
}

// ── POST /servicerequests ────────────────────────────────────────────────────────────────────────────────────────────

[Trait("category", "external-access")]
public sealed class WorkforceCeilingIntakeRouteTests : IClassFixture<ExternalIntakeEndpointsTests.SwitchOffFixture>
{
    private readonly ExternalIntakeEndpointsTests.SwitchOffFixture _fixture;

    public WorkforceCeilingIntakeRouteTests(ExternalIntakeEndpointsTests.SwitchOffFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    private Task<HttpResponseMessage> Submit()
    {
        var client = _fixture.CreateAuthenticatedClient();
        return client.PostAsync(ExternalIntakeEndpointsTests.Route,
            IntakeTestData.Body(ExternalIntakeEndpointsTests.NdaAssessment, "NDA", "{}"));
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleLacksCreate_Is403_AndNothingIsCreated()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(WorkforceRoleCeilingGate.ServiceRequestTable, AccessRights.Create);

        using var response = await Submit();

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleCannotBeRead_Is403_BaselineUnavailable_AndNothingIsCreated()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("read_failed");

        using var response = await Submit();

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleHoldsCreate_Submits()
    {
        _fixture.Principal = IntakeTestData.WorkforceContact();

        using var response = await Submit();

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.Created.Should().ContainSingle();
        _fixture.WorkforceCeiling.Reads.Should().Be(1);
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleHoldsCreate_IsStillRefused_WhenUacSaysNo()
    {
        // The ceiling never widens: the module entitlement still has to allow the submit.
        _fixture.Principal = IntakeTestData.WorkforceContact();
        _fixture.Entitlements.Override = new[] { "policy-library" };

        using var response = await Submit();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CeilingRouteTestData.ReasonCode(response)).Should().Be(ExternalIntakeEndpoints.DenyModuleNotEntitled);
        _fixture.Data.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ASystemuser_IsNotChecked_EvenWhenTheRoleCannotBeRead()
    {
        _fixture.Principal = IntakeTestData.SystemUser();
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        using var response = await Submit();

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.WorkforceCeiling.Reads.Should().Be(0);
    }
}

[Trait("category", "external-access")]
public sealed class WorkforceCeilingIntakePartnerRouteTests
    : IClassFixture<ExternalIntakeEndpointsPartnerSwitchOnTests.SwitchOnFixture>
{
    private readonly ExternalIntakeEndpointsPartnerSwitchOnTests.SwitchOnFixture _fixture;

    public WorkforceCeilingIntakePartnerRouteTests(ExternalIntakeEndpointsPartnerSwitchOnTests.SwitchOnFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    [Fact]
    public async Task ACiamPartner_IsNotChecked_EvenWhenTheRoleCannotBeRead()
    {
        _fixture.Principal = IntakeTestData.Ciam();
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsync(ExternalIntakeEndpointsTests.Route,
            IntakeTestData.Body(ExternalIntakeEndpointsTests.NdaAssessment, "NDA", "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.WorkforceCeiling.Reads.Should().Be(0);
    }
}

// ── DELETE /servicerequests/{id} ─────────────────────────────────────────────────────────────────────────────────────

[Trait("category", "external-access")]
public sealed class WorkforceCeilingDeleteRouteTests : IClassFixture<ExternalDeleteOwnTests.Fixture>
{
    private static readonly Guid Me = Guid.Parse("99999999-9999-9999-9999-999999999186");
    private static readonly Guid SomeoneElse = Guid.Parse("88888888-8888-8888-8888-888888888186");

    private readonly ExternalDeleteOwnTests.Fixture _fixture;

    public WorkforceCeilingDeleteRouteTests(ExternalDeleteOwnTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    private Guid SeedRequest(Guid requester)
    {
        var id = Guid.NewGuid();
        _fixture.Data.Requesters[id] = requester;
        return id;
    }

    private Task<HttpResponseMessage> Delete(Guid id) =>
        _fixture.CreateAuthenticatedClient().DeleteAsync($"/api/v1/external/servicerequests/{id}");

    [Fact]
    public async Task TheRequester_WhoseRoleLacksDelete_Is403_BeforeTheRowIsRead_AndNothingIsDeleted()
    {
        var id = SeedRequest(Me);
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(WorkforceRoleCeilingGate.ServiceRequestTable, AccessRights.Delete);

        using var response = await Delete(id);

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        _fixture.Data.RequesterAndStateReads.Should().Be(0, "the ceiling is decided before any record read");
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
        _fixture.Data.Requesters.ContainsKey(id).Should().BeTrue();
    }

    [Fact]
    public async Task TheRequester_WhoseRoleCannotBeRead_Is403_BaselineUnavailable()
    {
        var id = SeedRequest(Me);
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("read_failed");

        using var response = await Delete(id);

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
    }

    [Fact]
    public async Task NotTheRequester_WhoseRoleHoldsDelete_IsStillTheUniformDenial()
    {
        var id = SeedRequest(SomeoneElse);
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CeilingRouteTestData.ReasonCode(response)).Should().Be("sdap.access.deny.delete_own");
        _fixture.Data.ServiceRequestDeletes.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRequester_WhoseRoleHoldsDelete_Deletes()
    {
        var id = SeedRequest(Me);
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        _fixture.Data.ServiceRequestDeletes.Should().Equal(id);
    }

    [Fact]
    public async Task ASystemuserCaller_IsNotChecked()
    {
        var id = SeedRequest(SomeoneElse);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = SomeoneElse,
            SystemUserId = Guid.NewGuid(),
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        using var response = await Delete(id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        _fixture.WorkforceCeiling.Reads.Should().Be(0);
    }
}

// ── /servicerequests/{id}/documents (list, content, versions, upload) ───────────────────────────────────────────────

[Trait("category", "external-access")]
public sealed class WorkforceCeilingDocumentRouteTests : IClassFixture<ExternalRecordDocumentRoutesTests.Fixture>
{
    private static readonly Guid Me = Guid.Parse("99999999-9999-9999-9999-999999990186");
    private static readonly Guid SomeoneElse = Guid.Parse("88888888-8888-8888-8888-888888880186");
    private static readonly Guid Request = Guid.Parse("a1860000-0000-4000-8000-00000000000a");

    private readonly ExternalRecordDocumentRoutesTests.Fixture _fixture;

    public WorkforceCeilingDocumentRouteTests(ExternalRecordDocumentRoutesTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    private static MultipartFormDataContent FileForm() => new()
    {
        { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "brief.pdf" },
    };

    private static string BasePath => $"/api/v1/external/servicerequests/{Request}/documents";

    private Guid SeedMyRequestWithADocument()
    {
        _fixture.Data.Requesters[Request] = Me;
        var document = _fixture.Data.Seed(DocumentParentKind.ServiceRequest, Request, "sr.pdf", "b!c", "item-sr");
        _fixture.Data.DocumentReads.Clear();
        return document;
    }

    [Fact]
    public async Task TheRequester_WhoseRoleLacksDocumentRead_Is403_OnListContentAndVersions_AndNoDocumentIsRead()
    {
        var document = SeedMyRequestWithADocument();
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(WorkforceRoleCeilingGate.DocumentTable, AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        foreach (var path in new[] { BasePath, $"{BasePath}/{document}/content", $"{BasePath}/{document}/versions" })
        {
            using var response = await client.GetAsync(path);
            await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        }

        _fixture.Data.DocumentReads.Should().BeEmpty();
        _fixture.Downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRequester_WhoseRoleLacksServiceRequestRead_Is403_OnTheList()
    {
        SeedMyRequestWithADocument();
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(WorkforceRoleCeilingGate.ServiceRequestTable, AccessRights.Read);

        using var response = await _fixture.CreateAuthenticatedClient().GetAsync(BasePath);

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
    }

    [Theory]
    [InlineData(WorkforceRoleCeilingGate.DocumentTable, AccessRights.Create)]
    [InlineData(WorkforceRoleCeilingGate.ServiceRequestTable, AccessRights.AppendTo)]
    public async Task TheRequester_WhoseRoleLacksAnUploadPrivilege_Is403_AndNothingIsStored(string table, AccessRights removed)
    {
        SeedMyRequestWithADocument();
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(table, removed);

        using var response = await _fixture.CreateAuthenticatedClient().PostAsync(BasePath, FileForm());

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().ContainSingle("only the seeded document exists");
    }

    [Fact]
    public async Task TheRequester_WhoseRoleCannotBeRead_Is403_BaselineUnavailable_OnListAndUpload()
    {
        SeedMyRequestWithADocument();
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_ambiguous");

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync(BasePath);
        using var upload = await client.PostAsync(BasePath, FileForm());

        await CeilingRouteTestData.ShouldBeCeilingDenial(list, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        await CeilingRouteTestData.ShouldBeCeilingDenial(upload, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task NotTheRequester_WhoseRoleHoldsEveryPrivilege_IsStillRefused_ByTheRequesterScope()
    {
        _fixture.Data.Requesters[Request] = SomeoneElse;
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync(BasePath);
        using var upload = await client.PostAsync(BasePath, FileForm());

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CeilingRouteTestData.ReasonCode(list)).Should()
            .NotBe(WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing, "the role holds the privileges; the scope refused");
        _fixture.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRequester_WhoseRoleHoldsEveryPrivilege_ListsAndUploads()
    {
        SeedMyRequestWithADocument();
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync(BasePath);
        using var upload = await client.PostAsync(BasePath, FileForm());

        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
    }
}

// ── The module-data seam: reading own service requests ───────────────────────────────────────────────────────────────

[Trait("category", "external-access")]
public sealed class WorkforceCeilingModuleDataRouteTests : IClassFixture<ExternalDeleteOwnTests.Fixture>
{
    private const string Base = "/api/v1/external/api/dataverse";

    private const string ServiceRequestFetch =
        "<fetch><entity name='sprk_servicerequest'><attribute name='sprk_servicerequestid' /></entity></fetch>";

    private readonly ExternalDeleteOwnTests.Fixture _fixture;

    public WorkforceCeilingModuleDataRouteTests(ExternalDeleteOwnTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    private Task<HttpResponseMessage> Fetch(string entity, string fetchXml) =>
        _fixture.CreateAuthenticatedClient().PostAsync($"{Base}/fetch",
            JsonContent.Create(new FetchRequestDto(entity, fetchXml, null)));

    [Fact]
    public async Task AWorkforceContact_WhoseRoleLacksServiceRequestRead_Is403_OnFetchAndRecord()
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Guid.NewGuid());
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(WorkforceRoleCeilingGate.ServiceRequestTable, AccessRights.Read);

        using var fetch = await Fetch("sprk_servicerequest", ServiceRequestFetch);
        using var record = await _fixture.CreateAuthenticatedClient()
            .GetAsync($"{Base}/record/sprk_servicerequest/{Guid.NewGuid()}");

        await CeilingRouteTestData.ShouldBeCeilingDenial(fetch, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        await CeilingRouteTestData.ShouldBeCeilingDenial(record, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleCannotBeRead_Is403_BaselineUnavailable()
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Guid.NewGuid());
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("read_failed");

        using var fetch = await Fetch("sprk_servicerequest", ServiceRequestFetch);

        await CeilingRouteTestData.ShouldBeCeilingDenial(fetch, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleHoldsRead_PassesTheCeiling_AndTheTier2ScopeStillDecides()
    {
        // Must-not-fire: with no contact the Service Requests scope is empty, so the read answers 0 rows without a query.
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Guid.Empty);

        using var fetch = await Fetch("sprk_servicerequest", ServiceRequestFetch);

        fetch.StatusCode.Should().Be(HttpStatusCode.OK, await fetch.Content.ReadAsStringAsync());
        var body = await fetch.Content.ReadFromJsonAsync<FetchResponseDto>();
        body!.Entities.Should().BeEmpty();
        _fixture.WorkforceCeiling.Reads.Should().Be(1);
    }

    [Fact]
    public async Task AWorkforceContact_WhoseRoleHoldsRead_IsStillRefused_ARequestOutsideItsScope()
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Guid.NewGuid());

        using var record = await _fixture.CreateAuthenticatedClient()
            .GetAsync($"{Base}/record/sprk_servicerequest/{Guid.NewGuid()}");

        record.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CeilingRouteTestData.ReasonCode(record)).Should().Be(ExternalModuleDataEndpoints.DenyRecordNotAccessible);
    }

    [Fact]
    public async Task AnotherModule_IsCheckedToo_AndAnUnreadableRole_Is403_BaselineUnavailable()
    {
        // Review round 1 F1 (inverted from round 0): every module is held to the ceiling, not only service requests.
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Guid.NewGuid());
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        using var record = await _fixture.CreateAuthenticatedClient()
            .GetAsync($"{Base}/record/sprk_project/{Guid.NewGuid()}");

        await CeilingRouteTestData.ShouldBeCeilingDenial(record, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.WorkforceCeiling.Reads.Should().Be(1);
    }

    [Theory]
    [InlineData("sprk_project", "<fetch><entity name='sprk_project'><attribute name='sprk_projectid' /></entity></fetch>")]
    [InlineData("sprk_matter", "<fetch><entity name='sprk_matter'><attribute name='sprk_matterid' /></entity></fetch>")]
    [InlineData("sprk_workassignment", "<fetch><entity name='sprk_workassignment'><attribute name='sprk_workassignmentid' /></entity></fetch>")]
    [InlineData("sprk_document", "<fetch><entity name='sprk_document'><attribute name='sprk_documentid' /></entity></fetch>")]
    [InlineData("sprk_invoice", "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid' /></entity></fetch>")]
    [InlineData("sprk_gridconfiguration", "<fetch><entity name='sprk_gridconfiguration'><attribute name='sprk_gridconfigurationid' /></entity></fetch>")]
    public async Task EveryModule_WhoseTableTheRoleCannotRead_Is403_OnFetch(string table, string fetchXml)
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Guid.NewGuid());
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(table, AccessRights.Read);

        using var fetch = await Fetch(table, fetchXml);

        await CeilingRouteTestData.ShouldBeCeilingDenial(fetch, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
    }

    [Fact]
    public async Task AnotherModule_WhoseTableTheRoleCanRead_PassesTheCeiling_AndTheTier2ScopeStillDecides()
    {
        // Must-not-fire: Read on sprk_project held, so the scope (no grant) refuses the record, not the ceiling.
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Guid.NewGuid());

        using var record = await _fixture.CreateAuthenticatedClient()
            .GetAsync($"{Base}/record/sprk_project/{Guid.NewGuid()}");

        (await CeilingRouteTestData.ReasonCode(record)).Should().Be(ExternalModuleDataEndpoints.DenyRecordNotAccessible);
        _fixture.WorkforceCeiling.Reads.Should().Be(1);
    }
}
