// -----------------------------------------------------------------------------
// DataverseWebApiSecureRecordSetupTests.cs
//
// T256 (H7b) — the Web API shapes of SecureRecordSetupWebApi, the HTTP half of the production seam, against a
// hand-written HttpMessageHandler (never Mock<HttpMessageHandler>, ADR-038). Pinned here because they are the easy
// ones to get wrong and were verified live by unified-access-control-r2 (SECURE-PROJECT-ENVIRONMENT-SETUP.md §5.3–§5.5):
//   W1 RemovePrivilegeRole takes an ENTITY REFERENCE named Privilege (a GUID named PrivilegeId is an OData error).
//   W2 AddPrivilegesRole sends RolePrivilege entries at Depth Basic with the unit.
//   W3 RetrieveRolePrivilegesRole: depth by name, or by enum value.
//   W4 the sprk_noaccessentry probe selects exactly the BFF reader's columns; 400/404 = missing; 401/429 = faults.
//   W5 a create returns the id from OData-EntityId; none is a fault.
//   W6 an OData string literal doubles its quotes and is URL-escaped.
//   W7 list reads follow @odata.nextLink.
//   W8 a table's Read privileges: 404 = none; only PrivilegeType Read.
//   W9 a transport fault or a timeout is the seam's exception; the caller's cancellation propagates.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;

public sealed class DataverseWebApiSecureRecordSetupTests
{
    private static readonly Uri EnvUri = new("https://spaarke-acme.crm.dynamics.com/");
    private static readonly Guid Role = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Unit = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid Privilege = Guid.Parse("33333333-0000-0000-0000-000000000003");

    [Fact]
    public async Task W1_RemovePrivilegeRole_SendsThePrivilegeEntityReference()
    {
        var http = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Api(http).RemovePrivilegeAsync(Role, Privilege, CancellationToken.None);

        var request = http.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().EndWith($"/api/data/v9.2/roles({Role})/Microsoft.Dynamics.CRM.RemovePrivilegeRole");
        var body = JsonDocument.Parse(request.Body!).RootElement;
        body.TryGetProperty("PrivilegeId", out _).Should().BeFalse();
        var reference = body.GetProperty("Privilege");
        reference.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.privilege");
        reference.GetProperty("privilegeid").GetGuid().Should().Be(Privilege);
    }

    [Fact]
    public async Task W2_AddPrivilegesRole_SendsBasicRolePrivilegesWithTheUnit()
    {
        var http = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Api(http).AddBasicPrivilegesAsync(Role, Unit, [new SecureSetupPrivilege(Privilege, "prvReadsprk_Project")], CancellationToken.None);

        var request = http.Requests.Single();
        request.Uri.Should().EndWith($"/roles({Role})/Microsoft.Dynamics.CRM.AddPrivilegesRole");
        var entry = JsonDocument.Parse(request.Body!).RootElement.GetProperty("Privileges")[0];
        entry.GetProperty("@odata.type").GetString().Should().Be("Microsoft.Dynamics.CRM.RolePrivilege");
        entry.GetProperty("Depth").GetString().Should().Be("Basic");
        entry.GetProperty("PrivilegeId").GetGuid().Should().Be(Privilege);
        entry.GetProperty("PrivilegeName").GetString().Should().Be("prvReadsprk_Project");
        entry.GetProperty("BusinessUnitId").GetGuid().Should().Be(Unit);
        http.Requests.Should().NotContain(r => r.Uri.Contains("ReplacePrivilegesRole", StringComparison.Ordinal),
            "ReplacePrivilegesRole leaves the SharePoint four (guide §5.4)");
    }

    [Fact]
    public async Task W3_RetrieveRolePrivilegesRole_ReadsDepthByNameOrEnumValue()
    {
        var other = Guid.NewGuid();
        var http = new Recorder().Respond(_ => Json(new
        {
            RolePrivileges = new object[]
            {
                new { Depth = "Basic", PrivilegeId = Privilege, BusinessUnitId = Unit, PrivilegeName = "prvReadsprk_Project" },
                new { Depth = 3, PrivilegeId = other, BusinessUnitId = Unit, PrivilegeName = "prvReadSharePointData" },
            },
        }));

        var held = await Api(http).GetRolePrivilegesAsync(Role, CancellationToken.None);

        http.Requests.Single().Uri.Should().EndWith($"/RetrieveRolePrivilegesRole(RoleId=@p)?@p={Role}");
        held.Should().BeEquivalentTo(new[]
        {
            new SecureSetupHeldPrivilege(Privilege, "prvReadsprk_Project", "Basic"),
            new SecureSetupHeldPrivilege(other, "prvReadSharePointData", "Global"),
        });
    }

    [Fact]
    public async Task W4_NoAccessEntryProbe_SelectsTheBffReadersColumns_AndClassifiesTheAnswer()
    {
        var ok = new Recorder().Respond(_ => Json(new { value = Array.Empty<object>() }));
        (await Api(ok).ProbeNoAccessEntryAsync(CancellationToken.None)).Present.Should().BeTrue();
        Uri.UnescapeDataString(ok.Requests.Single().Uri).Should()
            .Contain($"/sprk_noaccessentries?$select={SecureRecordSetupWebApi.NoAccessEntryRowSelect}&$top=1");

        var missingColumn = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Could not find a property named 'sprk_subjectsystemuser'\"}}"),
        });
        var probe = await Api(missingColumn).ProbeNoAccessEntryAsync(CancellationToken.None);
        probe.Present.Should().BeFalse();
        probe.Detail.Should().Contain("sprk_subjectsystemuser");

        var missingTable = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
        (await Api(missingTable).ProbeNoAccessEntryAsync(CancellationToken.None)).Present.Should().BeFalse();

        var unauthorized = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        (await FluentActions.Awaiting(() => Api(unauthorized).ProbeNoAccessEntryAsync(CancellationToken.None))
            .Should().ThrowAsync<SecureRecordSetupDataverseException>()).Which.Kind.Should().Be(SecureRecordSetupFaultKind.Auth);

        var throttled = new Recorder().Respond(_ => new HttpResponseMessage((HttpStatusCode)429));
        (await FluentActions.Awaiting(() => Api(throttled).ProbeNoAccessEntryAsync(CancellationToken.None))
            .Should().ThrowAsync<SecureRecordSetupDataverseException>()).Which.Kind.Should().Be(SecureRecordSetupFaultKind.RateLimited);
    }

    [Fact]
    public async Task W5_Create_ReturnsTheIdFromODataEntityId_AndCreatesTheRoleInTheUnit()
    {
        var created = Guid.NewGuid();
        var http = new Recorder().Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.Add("OData-EntityId", $"{EnvUri}api/data/v9.2/roles({created})");
            return response;
        });

        var id = await Api(http).CreateRoleAsync(Unit, "Secure Record Owner", "d", CancellationToken.None);

        id.Should().Be(created);
        var body = JsonDocument.Parse(http.Requests.Single().Body!).RootElement;
        body.GetProperty("businessunitid@odata.bind").GetString().Should().Be($"/businessunits({Unit})",
            "the role is created IN the Secure Record unit (guide §5.2)");

        var noHeader = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await FluentActions.Awaiting(() => Api(noHeader).CreateOwnerTeamAsync(Unit, "Secure Record Owners", "d", CancellationToken.None))
            .Should().ThrowAsync<SecureRecordSetupDataverseException>();
    }

    [Fact]
    public async Task W5b_CreateOwnerTeam_IsAnOwnerTeamInTheUnit()
    {
        var http = new Recorder().Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.Add("OData-EntityId", $"{EnvUri}api/data/v9.2/teams({Guid.NewGuid()})");
            return response;
        });

        await Api(http).CreateOwnerTeamAsync(Unit, "Secure Record Owners", "d", CancellationToken.None);

        var body = JsonDocument.Parse(http.Requests.Single().Body!).RootElement;
        body.GetProperty("teamtype").GetInt32().Should().Be(0);
        body.GetProperty("businessunitid@odata.bind").GetString().Should().Be($"/businessunits({Unit})");
    }

    [Fact]
    public async Task W6_NameFilters_DoubleQuotes_AndEscape()
    {
        var http = new Recorder().Respond(_ => Json(new { value = Array.Empty<object>() }));

        await Api(http).FindBusinessUnitsByNameAsync("O'Brien & Co", CancellationToken.None);

        var uri = http.Requests.Single().Uri;
        uri.Should().Contain("%26", "a literal '&' must not split the query string").And.Contain("$top=2");
        Uri.UnescapeDataString(uri).Should().Contain("name eq 'O''Brien & Co'", "an OData literal doubles its quotes");
    }

    [Fact]
    public async Task W7_ListReads_FollowNextLink()
    {
        var second = $"{EnvUri}api/data/v9.2/teams?$skiptoken=2";
        var http = new Recorder().Respond(request => request.RequestUri!.ToString().Contains("skiptoken", StringComparison.Ordinal)
            ? Json(new { value = new[] { new { teamid = Guid.NewGuid(), name = "b", _businessunitid_value = Unit, isdefault = true } } })
            : Json(new Dictionary<string, object>
            {
                ["value"] = new[] { new { teamid = Guid.NewGuid(), name = "a", _businessunitid_value = Unit, isdefault = true } },
                ["@odata.nextLink"] = second,
            }));

        var teams = await Api(http).FindDefaultTeamsAsync(null, CancellationToken.None);

        teams.Select(t => t.Name).Should().Equal("a", "b");
        http.Requests.Should().HaveCount(2);
        Uri.UnescapeDataString(http.Requests[0].Uri).Should().Contain("$filter=isdefault eq true");
    }

    [Fact]
    public async Task W8_ReadPrivileges_AbsentTableIsEmpty_AndOnlyReadIsReturned()
    {
        var absent = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        (await Api(absent).GetReadPrivilegesAsync("sprk_nope", CancellationToken.None)).Should().BeEmpty();

        var http = new Recorder().Respond(_ => Json(new
        {
            value = new object[]
            {
                new { PrivilegeId = Privilege, Name = "prvReadsprk_Project", PrivilegeType = "Read" },
                new { PrivilegeId = Guid.NewGuid(), Name = "prvWritesprk_Project", PrivilegeType = "Write" },
            },
        }));
        (await Api(http).GetReadPrivilegesAsync("sprk_project", CancellationToken.None))
            .Should().Equal(new SecureSetupPrivilege(Privilege, "prvReadsprk_Project"));
        http.Requests.Single().Uri.Should().EndWith("/EntityDefinitions(LogicalName='sprk_project')/Privileges");
    }

    [Fact]
    public async Task W9_TransportFaultAndTimeout_AreSeamFaults_CancellationPropagates()
    {
        var broken = new Recorder().Respond(_ => throw new HttpRequestException("reset"));
        (await FluentActions.Awaiting(() => Api(broken).ReadShareToPreviousOwnerOnAssignAsync(CancellationToken.None))
            .Should().ThrowAsync<SecureRecordSetupDataverseException>()).Which.Kind.Should().Be(SecureRecordSetupFaultKind.Other);

        var timeout = new Recorder().Respond(_ => throw new TaskCanceledException("timeout"));
        await FluentActions.Awaiting(() => Api(timeout).ReadShareToPreviousOwnerOnAssignAsync(CancellationToken.None))
            .Should().ThrowAsync<SecureRecordSetupDataverseException>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new Recorder().Respond(_ => throw new TaskCanceledException("cancelled"));
        await FluentActions.Awaiting(() => Api(cancelled).ReadShareToPreviousOwnerOnAssignAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SetColumnFalse_PatchesWithIfMatchAny()
    {
        var http = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var row = Guid.NewGuid();

        await Api(http).SetColumnFalseAsync(new SecureSetupTableIdentity("sprk_project", "sprk_projects", "sprk_projectid"), row,
            "sprk_issecure", CancellationToken.None);

        var request = http.Requests.Single();
        request.Method.Should().Be(HttpMethod.Patch);
        request.Uri.Should().EndWith($"/sprk_projects({row})");
        request.IfMatch.Should().Be("*", "never an upsert");
        JsonDocument.Parse(request.Body!).RootElement.GetProperty("sprk_issecure").GetBoolean().Should().BeFalse();
    }

    // ------------------------------------------------------------ T259: §6 T1/T3 reads

    [Fact]
    public async Task W10_GetBusinessUnit_ReadsItsParent_And404IsNone()
    {
        var parent = Guid.NewGuid();
        var http = new Recorder().Respond(_ => Json(new Dictionary<string, object>
        {
            ["businessunitid"] = Unit.ToString(), ["name"] = "Acme Corporation", ["_parentbusinessunitid_value"] = parent.ToString(),
        }));

        var unit = await Api(http).GetBusinessUnitAsync(Unit, CancellationToken.None);

        unit.Should().Be(new SecureSetupBusinessUnit(Unit, "Acme Corporation", parent));
        http.Requests.Single().Uri.Should().EndWith(
            $"/api/data/v9.2/businessunits({Unit})?$select=businessunitid,name,_parentbusinessunitid_value");

        var missing = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        (await Api(missing).GetBusinessUnitAsync(Unit, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task W11_GetUserBusinessUnit_ReadsTheLookup_404IsNone_AndAFaultIsTheSeamException()
    {
        var user = Guid.NewGuid();
        var http = new Recorder().Respond(_ => Json(new Dictionary<string, object> { ["_businessunitid_value"] = Unit.ToString() }));

        (await Api(http).GetUserBusinessUnitAsync(user, CancellationToken.None)).Should().Be(Unit);
        http.Requests.Single().Uri.Should().EndWith($"/api/data/v9.2/systemusers({user})?$select=_businessunitid_value");

        var missing = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        (await Api(missing).GetUserBusinessUnitAsync(user, CancellationToken.None)).Should().BeNull();

        var denied = new Recorder().Respond(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var act = () => Api(denied).GetUserBusinessUnitAsync(user, CancellationToken.None);
        (await act.Should().ThrowAsync<SecureRecordSetupDataverseException>()).Which.Kind.Should().Be(SecureRecordSetupFaultKind.Auth);
    }

    // ------------------------------------------------------------ helpers

    private static SecureRecordSetupWebApi Api(Recorder http)
        => new(new HttpClient(http), EnvUri, _ => ValueTask.FromResult("token"));

    private static HttpResponseMessage Json(object payload)
        => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    private sealed record Recorded(HttpMethod Method, string Uri, string? Body, string? IfMatch);

    private sealed class Recorder : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<Recorded> Requests { get; } = [];

        public Recorder Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.Method, request.RequestUri!.AbsoluteUri, body,
                request.Headers.TryGetValues("If-Match", out var values) ? values.Single() : null));
            request.Headers.Authorization!.Parameter.Should().Be("token");
            return _respond(request);
        }
    }
}
