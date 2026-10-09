// -----------------------------------------------------------------------------
// DataverseWebApiAppUserCreatorTests.cs
//
// T259 (ISS-010 / #1486, owner decision 2026-10-09 — INCOMING-145 §6 T1/T3). The Web API shapes of H10's
// DataverseWebApiAppUserCreator over a real HttpClient wrapping a hand-written HttpMessageHandler (never
// Mock<HttpMessageHandler>, ADR-038), with the credential injected through the internal test constructor.
//
// COVERAGE:
//   EnsureCustomerBusinessUnitAsync
//     - none of that name → POST businessunits under the ROOT (parentbusinessunitid@odata.bind), id from OData-EntityId;
//     - one under the root → reused, nothing written;
//     - one under another unit, or the root itself carrying the name → WrongParent, nothing written;
//     - two of that name → Ambiguous, nothing written ($top=2 makes the second visible);
//     - not exactly one root → Failure, nothing written; a quote in the name is doubled in the OData literal.
//   EnsureAppUserAsync
//     - a new App User is POSTed with businessunitid = the customer unit (and the UAMI's explicit
//       azureactivedirectoryobjectid, auth-v4 §10.4) — never the root;
//     - the role is the copy IN the customer unit (filter by its id), associated once; a held role writes nothing;
//     - an existing App User in another unit → InForeignBusinessUnit, nothing written (never moved);
//     - a role missing from the unit, or ambiguous, is a Failure (no name-only fallback that could pick another unit's copy).
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiAppUserCreatorTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string EnvUrl = "https://spaarke-acme.crm.dynamics.com/";
    private const string RootBu = "10000000-0000-0000-0000-000000000001";
    private const string CustomerBu = "20000000-0000-0000-0000-000000000002";
    private const string OtherBu = "30000000-0000-0000-0000-000000000003";
    private const string AppId = "40000000-0000-0000-0000-000000000004";
    private const string UamiObjectId = "50000000-0000-0000-0000-000000000005";
    private const string SystemUser = "60000000-0000-0000-0000-000000000006";
    private const string RoleCopy = "70000000-0000-0000-0000-000000000007";

    // ---------------- EnsureCustomerBusinessUnitAsync ----------------

    [Fact]
    public async Task CustomerUnit_Absent_IsCreatedDirectlyUnderTheRoot()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Post
            ? Created($"{EnvUrl}api/data/v9.2/businessunits({CustomerBu})")
            : req.RequestUri!.AbsoluteUri.Contains("parentbusinessunitid%20eq%20null")
                ? Json($$"""{"value":[{"businessunitid":"{{RootBu}}"}]}""")
                : Json("""{"value":[]}"""));

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.Success(Guid.Parse(CustomerBu), Created: true));
        http.Requests[1].Uri.Should().Contain("name%20eq%20").And.Contain("Acme%20Corporation").And.Contain("$top=2");
        var post = http.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post).Subject;
        post.Uri.Should().EndWith("/api/data/v9.2/businessunits");
        post.Body.Should().Contain("\"name\":\"Acme Corporation\"")
            .And.Contain($"\"parentbusinessunitid@odata.bind\":\"/businessunits({RootBu})\"");
    }

    [Fact]
    public async Task CustomerUnit_UnderTheRoot_IsReused_NothingWritten()
    {
        var http = UnitFake($$"""[{"businessunitid":"{{CustomerBu}}","_parentbusinessunitid_value":"{{RootBu}}"}]""");

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.Success(Guid.Parse(CustomerBu), Created: false));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Theory]
    [InlineData(OtherBu)]
    [InlineData(null)]   // the ROOT itself carries the name
    public async Task CustomerUnit_NotADirectChildOfTheRoot_IsWrongParent_NothingWritten(string? parent)
    {
        var parentJson = parent is null ? "null" : $"\"{parent}\"";
        var http = UnitFake($$"""[{"businessunitid":"{{CustomerBu}}","_parentbusinessunitid_value":{{parentJson}}}]""");

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.WrongParent(
            Guid.Parse(CustomerBu), parent is null ? null : Guid.Parse(parent), Guid.Parse(RootBu)));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CustomerUnit_TwoOfThatName_IsAmbiguous_NothingWritten()
    {
        var http = UnitFake($$"""[{"businessunitid":"{{CustomerBu}}","_parentbusinessunitid_value":"{{RootBu}}"},{"businessunitid":"{{OtherBu}}","_parentbusinessunitid_value":"{{RootBu}}"}]""");

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.Ambiguous(2));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CustomerUnit_NoSingleRoot_IsAFailure_NothingWritten()
    {
        var http = new FakeHttp(_ => Json("""{"value":[]}"""));

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", CancellationToken.None);

        outcome.Should().BeOfType<CustomerBusinessUnitOutcome.Failure>().Which.Diagnostic.Should().Contain("0 root business units");
        http.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task CustomerUnit_AQuoteInTheName_IsDoubledInTheODataLiteral()
    {
        var http = UnitFake($$"""[{"businessunitid":"{{CustomerBu}}","_parentbusinessunitid_value":"{{RootBu}}"}]""");

        await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "O'Brien & Co", CancellationToken.None);

        http.Requests[1].Uri.Should().Contain("O%27%27Brien%20%26%20Co");
    }

    // ---------------- EnsureAppUserAsync ----------------

    [Fact]
    public async Task AppUser_New_IsCreatedInTheCustomerUnit_WithTheUnitsRoleCopy()
    {
        var http = AppUserFake(existingUnit: null, roleRows: 1, roleHeld: false);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(UamiObjectId), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        var create = http.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.EndsWith("/api/data/v9.2/systemusers"));
        create.Body.Should().Contain($"\"businessunitid@odata.bind\":\"/businessunits({CustomerBu})\"")
            .And.Contain($"\"applicationid\":\"{AppId}\"")
            .And.Contain($"\"azureactivedirectoryobjectid\":\"{UamiObjectId}\"", "auth-v4 §10.4: the principalId, in the same POST")
            .And.NotContain(RootBu);
        http.Requests.Single(r => r.Uri.Contains("/roles?")).Uri
            .Should().Contain("System%20Administrator").And.Contain($"_businessunitid_value%20eq%20{CustomerBu}");
        var associate = http.Requests.Single(r => r.Uri.EndsWith("/systemuserroles_association/$ref"));
        associate.Uri.Should().Contain($"/systemusers({SystemUser})/");
        associate.Body.Should().Contain($"https://spaarke-acme.crm.dynamics.com/api/data/v9.2/roles({RoleCopy})");
    }

    [Fact]
    public async Task AppUser_ExistingInTheCustomerUnit_WithTheRole_WritesNothing()
    {
        var http = AppUserFake(existingUnit: CustomerBu, roleRows: 1, roleHeld: true);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get, "a second run writes nothing");
        http.Requests[0].Uri.Should().Contain($"applicationid%20eq%20{AppId}").And.Contain("_businessunitid_value");
    }

    [Theory]
    [InlineData(RootBu)]
    [InlineData(OtherBu)]
    public async Task AppUser_ExistingInAnotherUnit_IsInForeignBusinessUnit_NeverMoved(string unit)
    {
        var http = AppUserFake(existingUnit: unit, roleRows: 1, roleHeld: false);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.InForeignBusinessUnit(SystemUser, Guid.Parse(unit)));
        http.Requests.Should().ContainSingle("nothing after the read: no role, no PATCH");
    }

    [Theory]
    [InlineData(0, "not found in business unit")]
    [InlineData(2, "More than one role")]
    public async Task AppUser_RoleMissingOrAmbiguousInTheUnit_IsAFailure_WithoutAssociation(int rows, string expected)
    {
        var http = AppUserFake(existingUnit: CustomerBu, roleRows: rows, roleHeld: false);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseAppUserCreationOutcome.Failure>().Which.Diagnostic.Should().Contain(expected);
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
        http.Requests.Should().NotContain(r => r.Uri.Contains("GetRootBusinessUnitId"), "no name-only or root fallback");
    }

    // ---------------- helpers ----------------

    private static DataverseAppUserCreationRequest Request(string? objectId = null)
        => new(EnvUrl, TenantId, AppId, "System Administrator", Guid.Parse(CustomerBu), objectId);

    private static FakeHttp UnitFake(string namedRows)
        => new(req => req.RequestUri!.AbsoluteUri.Contains("parentbusinessunitid%20eq%20null")
            ? Json($$"""{"value":[{"businessunitid":"{{RootBu}}"}]}""")
            : Json($$"""{"value":{{namedRows}}}"""));

    private static FakeHttp AppUserFake(string? existingUnit, int roleRows, bool roleHeld)
        => new(req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (req.Method == HttpMethod.Post)
            {
                return uri.EndsWith("/api/data/v9.2/systemusers", StringComparison.Ordinal)
                    ? Created($"{EnvUrl}api/data/v9.2/systemusers({SystemUser})")
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (uri.Contains("/systemusers?"))
            {
                return Json(existingUnit is null
                    ? """{"value":[]}"""
                    : $$"""{"value":[{"systemuserid":"{{SystemUser}}","_businessunitid_value":"{{existingUnit}}"}]}""");
            }
            if (uri.Contains("/roles?"))
            {
                return Json($$"""{"value":[{{string.Join(",", Enumerable.Repeat($$"""{"roleid":"{{RoleCopy}}"}""", roleRows))}}]}""");
            }
            if (uri.Contains("/systemuserroles_association?"))
            {
                return Json(roleHeld ? $$"""{"value":[{"roleid":"{{RoleCopy}}"}]}""" : """{"value":[]}""");
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"unexpected {req.Method} {uri}") };
        });

    private static DataverseWebApiAppUserCreator Creator(FakeHttp http)
        => new(new HttpClient(http), Options.Create(new H10DataverseAppUserGraphParityOptions()),
            NullLogger<DataverseWebApiAppUserCreator>.Instance, _ => new FakeCredential());

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Created(string entityId)
    {
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.Add("OData-EntityId", entityId);
        return response;
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Hand-rolled fake handler (NOT Mock&lt;HttpMessageHandler&gt;) recording each request.</summary>
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, body));
            request.Headers.Authorization!.Parameter.Should().Be("fake-token");
            return respond(request);
        }
    }
}
