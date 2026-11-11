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
//     - T266: an existing App User whose azureactivedirectoryobjectid is not the requested principal id (empty, or the
//       client id) is a Failure, nothing written (never rewritten); a matching one is adopted;
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
    private const string SecureBu = "80000000-0000-0000-0000-000000000008";
    private const string SecureName = "Secure Record";
    private static readonly string[] Reserved = [SecureName];

    // ---------------- EnsureCustomerBusinessUnitAsync ----------------

    [Fact]
    public async Task CustomerUnit_Absent_IsCreatedDirectlyUnderTheRoot()
    {
        var http = new FakeHttp(req => req.Method == HttpMethod.Post
            ? Created($"{EnvUrl}api/data/v9.2/businessunits({CustomerBu})")
            : req.RequestUri!.AbsoluteUri.Contains("parentbusinessunitid%20eq%20null")
                ? Json($$"""{"value":[{"businessunitid":"{{RootBu}}"}]}""")
                : Json("""{"value":[]}"""));

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

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

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

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

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.WrongParent(
            Guid.Parse(CustomerBu), parent is null ? null : Guid.Parse(parent), Guid.Parse(RootBu)));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CustomerUnit_TwoOfThatName_IsAmbiguous_NothingWritten()
    {
        var http = UnitFake($$"""[{"businessunitid":"{{CustomerBu}}","_parentbusinessunitid_value":"{{RootBu}}"},{"businessunitid":"{{OtherBu}}","_parentbusinessunitid_value":"{{RootBu}}"}]""");

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.Ambiguous(2));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CustomerUnit_NoSingleRoot_IsAFailure_NothingWritten()
    {
        var http = new FakeHttp(_ => Json("""{"value":[]}"""));

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

        outcome.Should().BeOfType<CustomerBusinessUnitOutcome.Failure>().Which.Diagnostic.Should().Contain("0 root business units");
        http.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task CustomerUnit_AQuoteInTheName_IsDoubledInTheODataLiteral()
    {
        var http = UnitFake($$"""[{"businessunitid":"{{CustomerBu}}","_parentbusinessunitid_value":"{{RootBu}}"}]""");

        await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "O'Brien & Co", Reserved, CancellationToken.None);

        http.Requests[1].Uri.Should().Contain("O%27%27Brien%20%26%20Co");
    }

    // ---- the customer's unit is never a reserved unit (compared by ID, as Dataverse resolves both names) ----

    [Theory]
    [InlineData("Secure Record")]   // exact
    [InlineData("SECURE RECORD")]   // casing
    [InlineData("Sécure Record")]   // accent — OrdinalIgnoreCase in code does not see this one
    [InlineData("Secure Récord")]
    public async Task CustomerName_DataverseResolvesToTheSecureUnit_IsReservedUnit_NothingWritten(string customerName)
    {
        // Must fire: the Secure Record unit exists; Dataverse finds it for the customer's name.
        var http = UnitsFake([(SecureBu, SecureName)]);

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, customerName, Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.IsReservedUnit(Guid.Parse(SecureBu), SecureName));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get, "refused before any write: no create, no adopt");
    }

    [Theory]
    [InlineData("Sécure Record")]
    [InlineData("SECURE RECORD")]
    public async Task FreshStamp_CustomerNameCollidingWithAReservedName_IsRefusedRightAfterTheCreate_NoUserPlaced(string customerName)
    {
        // Must fire on the NORMAL path: no Secure Record unit exists yet (H7b creates it after H10). H10 creates the
        // customer's unit, then the reserved-name lookup resolves to that very unit → refused; only an empty unit was written.
        var units = new List<(string Id, string Name)>();
        var http = UnitsFake(units, onCreate: name => units.Add((CustomerBu, name)));

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, customerName, Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.IsReservedUnit(Guid.Parse(CustomerBu), SecureName, CreatedByThisCall: true));
        http.Requests.Where(r => r.Method != HttpMethod.Get).Should().ContainSingle("only the unit itself was created")
            .Which.Uri.Should().EndWith("/api/data/v9.2/businessunits");
    }

    [Fact]
    public async Task TwoUnitsForTheCustomersName_OneTheSecureUnit_IsReservedUnit_NotAmbiguous()
    {
        var http = UnitsFake([(SecureBu, SecureName), (CustomerBu, "Sécure Record")]);

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Sécure Record", Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.IsReservedUnit(Guid.Parse(SecureBu), SecureName));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task ThreeUnitsAnswerTheSecureName_TheCustomersLast_IsStillFound()
    {
        // Must fire: a $top=2 read of the reserved name would miss the customer's unit in third place. (Scripted per query:
        // the customer-name lookup answers one unit, the reserved-name lookup answers three with the customer's last.)
        var http = new FakeHttp(req =>
        {
            var uri = Uri.UnescapeDataString(req.RequestUri!.AbsoluteUri);
            if (uri.Contains("parentbusinessunitid eq null"))
            {
                return Json($$"""{"value":[{"businessunitid":"{{RootBu}}"}]}""");
            }
            if (uri.Contains($"name eq '{SecureName}'"))
            {
                var take = int.Parse(System.Text.RegularExpressions.Regex.Match(uri, @"\$top=(\d+)").Groups[1].Value);
                var rows = new[] { "90000000-0000-0000-0000-000000000009", SecureBu, CustomerBu }
                    .Take(take).Select(id => $$"""{"businessunitid":"{{id}}"}""");
                return Json($$"""{"value":[{{string.Join(",", rows)}}]}""");
            }
            return Json($$"""{"value":[{"businessunitid":"{{CustomerBu}}","_parentbusinessunitid_value":"{{RootBu}}"}]}""");
        });

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Sécure Record", Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.IsReservedUnit(Guid.Parse(CustomerBu), SecureName));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task ReservedNameAnsweredByTheCapOrMore_IsAFailure_NeverAPass()
    {
        var units = Enumerable.Range(1, DataverseWebApiAppUserCreator.ReservedLookupCap)
            .Select(i => ($"a0000000-0000-0000-0000-{i:D12}", SecureName))
            .Append((CustomerBu, "Acme Corporation"));
        var http = UnitsFake(units);

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

        outcome.Should().BeOfType<CustomerBusinessUnitOutcome.Failure>().Which.Diagnostic.Should().Contain("cannot tell");
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CustomerName_Distinct_WithTheSecureUnitPresent_ReusesTheCustomersUnit()
    {
        // Must not fire: distinct names proceed.
        var http = UnitsFake([(SecureBu, SecureName), (CustomerBu, "Acme Corporation")]);

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.Success(Guid.Parse(CustomerBu), Created: false));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CustomerName_Distinct_Absent_IsCreated_EvenWithTheSecureUnitPresent()
    {
        var http = UnitsFake([(SecureBu, SecureName)]);

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

        outcome.Should().Be(new CustomerBusinessUnitOutcome.Success(Guid.Parse(CustomerBu), Created: true));
    }

    [Fact]
    public async Task ReservedUnitLookup_Fault_IsAFailure_NothingWritten()
    {
        // A fault resolving the reserved unit is never "not found, so adopt".
        var http = UnitsFake([(CustomerBu, "Acme Corporation")], faultOnName: SecureName);

        var outcome = await Creator(http).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", Reserved, CancellationToken.None);

        outcome.Should().BeOfType<CustomerBusinessUnitOutcome.Failure>().Which.Diagnostic.Should().Contain("503");
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task ReservedUnitNames_Empty_IsACallerBug()
    {
        var act = () => Creator(UnitsFake([])).EnsureCustomerBusinessUnitAsync(EnvUrl, TenantId, "Acme Corporation", [], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
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

    [Theory]
    [InlineData(null)]
    [InlineData(CustomerBu)]
    public async Task AppUser_NoRoleRequested_CreatesOrAdoptsTheUserOnly_AndTouchesNoRole(string? existingUnit)
    {
        // T266 (#2605): the ontology writer's App User — H10 associates no role; H7b S24 owns its one role.
        var http = AppUserFake(existingUnit: existingUnit, roleRows: 1, roleHeld: false);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(UamiObjectId) with { SecurityRoleName = null }, CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        http.Requests.Should().NotContain(r => r.Uri.Contains("/roles?"), "no role is looked up");
        http.Requests.Should().NotContain(r => r.Uri.Contains("systemuserroles_association"), "no role is read or associated");
        http.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(existingUnit is null ? 1 : 0);
    }

    [Fact]
    public async Task AppUser_ABlankRoleName_IsACallerBug_NotNoRole()
    {
        var http = AppUserFake(existingUnit: CustomerBu, roleRows: 1, roleHeld: false);

        var act = () => Creator(http).EnsureAppUserAsync(Request() with { SecurityRoleName = " " }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        http.Requests.Should().BeEmpty();
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

    [Fact]
    public async Task AppUser_ExistingWithTheRequestedObjectId_IsAdopted()
    {
        var http = AppUserFake(existingUnit: CustomerBu, roleRows: 1, roleHeld: true, existingObjectId: UamiObjectId.ToUpperInvariant());

        var outcome = await Creator(http).EnsureAppUserAsync(Request(UamiObjectId), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        http.Requests[0].Uri.Should().Contain("azureactivedirectoryobjectid");
    }

    [Theory]
    [InlineData(AppId)]   // the trap: the client id where the principal id belongs
    [InlineData(null)]    // never set
    public async Task AppUser_ExistingWithAnotherObjectId_IsAFailure_NothingWritten(string? heldObjectId)
    {
        // auth-v4 §10.4 on adoption (T266 review): such a row passes every existence check yet fails every app-only call.
        var http = AppUserFake(existingUnit: CustomerBu, roleRows: 1, roleHeld: false, existingObjectId: heldObjectId);

        var outcome = await Creator(http).EnsureAppUserAsync(Request(UamiObjectId) with { SecurityRoleName = null }, CancellationToken.None);

        outcome.Should().BeOfType<DataverseAppUserCreationOutcome.Failure>().Which.Diagnostic
            .Should().Contain(UamiObjectId).And.Contain(heldObjectId ?? "(empty)");
        http.Requests.Should().ContainSingle("nothing after the read: no PATCH, no role");
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

    // ---------------- task 267 (#2618): the one move — the ontology writer out of the customer's unit ----------------

    private const string WriterBu = "90000000-0000-0000-0000-000000000009";

    private static DataverseAppUserCreationRequest WriterMoveRequest()
        => new(EnvUrl, TenantId, AppId, SecurityRoleName: null, Guid.Parse(WriterBu), UamiObjectId,
            MoveFromBusinessUnitId: Guid.Parse(CustomerBu));

    [Fact]
    public async Task Move_TheWritersRowInTheMoveFromUnit_IsPatchedIntoItsUnit_ReadBack_AndReported()
    {
        var http = MoveFake(existingUnit: CustomerBu, existingObjectId: UamiObjectId, readBackUnit: WriterBu);

        var outcome = await Creator(http).EnsureAppUserAsync(WriterMoveRequest(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser, MovedFromBusinessUnitId: Guid.Parse(CustomerBu)));
        var patch = http.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Patch).Subject;
        patch.Uri.Should().EndWith($"/api/data/v9.2/systemusers({SystemUser})");
        patch.Body.Should().Contain($"\"businessunitid@odata.bind\":\"/businessunits({WriterBu})\"");
        http.Requests.Last().Uri.Should().Contain($"systemusers({SystemUser})").And.Contain("_businessunitid_value",
            "the move is read back after the PATCH");
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Post, "nothing is created and no role is associated");
        http.Requests.Should().NotContain(r => r.Uri.Contains("/roles?") || r.Uri.Contains("systemuserroles_association"));
    }

    [Fact]
    public async Task Move_TheWriterAlreadyInItsUnit_WritesNothing()
    {
        var http = MoveFake(existingUnit: WriterBu, existingObjectId: UamiObjectId, readBackUnit: WriterBu);

        var outcome = await Creator(http).EnsureAppUserAsync(WriterMoveRequest(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        http.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    [Theory]
    [InlineData(RootBu)]
    [InlineData(OtherBu)]
    public async Task Move_FromAnyOtherUnit_IsInForeignBusinessUnit_NeverMoved(string unit)
    {
        var http = MoveFake(existingUnit: unit, existingObjectId: UamiObjectId, readBackUnit: WriterBu);

        var outcome = await Creator(http).EnsureAppUserAsync(WriterMoveRequest(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.InForeignBusinessUnit(SystemUser, Guid.Parse(unit)));
        http.Requests.Should().ContainSingle("nothing after the read: no PATCH");
    }

    [Theory]
    [InlineData(AppId)]
    [InlineData(null)]
    public async Task Move_ARowHoldingAnotherObjectId_IsNeverMoved_AndNamesTheReason(string? heldObjectId)
    {
        var http = MoveFake(existingUnit: CustomerBu, existingObjectId: heldObjectId, readBackUnit: WriterBu);

        var outcome = await Creator(http).EnsureAppUserAsync(WriterMoveRequest(), CancellationToken.None);

        var foreign = outcome.Should().BeOfType<DataverseAppUserCreationOutcome.InForeignBusinessUnit>().Subject;
        foreign.BusinessUnitId.Should().Be(Guid.Parse(CustomerBu));
        foreign.Reason.Should().Contain("azureactivedirectoryobjectid").And.Contain(UamiObjectId);
        http.Requests.Should().ContainSingle("the object id is checked BEFORE any write: no PATCH");
    }

    [Fact]
    public async Task Move_ThatDoesNotReadBack_IsAFailure()
    {
        var http = MoveFake(existingUnit: CustomerBu, existingObjectId: UamiObjectId, readBackUnit: CustomerBu);

        var outcome = await Creator(http).EnsureAppUserAsync(WriterMoveRequest(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseAppUserCreationOutcome.Failure>().Which.Diagnostic
            .Should().Contain(WriterBu).And.Contain(CustomerBu);
    }

    [Fact]
    public async Task Move_ThePatchRefused_IsAFailure()
    {
        var http = MoveFake(existingUnit: CustomerBu, existingObjectId: UamiObjectId, readBackUnit: WriterBu, patchStatus: HttpStatusCode.BadRequest);

        var outcome = await Creator(http).EnsureAppUserAsync(WriterMoveRequest(), CancellationToken.None);

        outcome.Should().BeOfType<DataverseAppUserCreationOutcome.Failure>();
    }

    [Fact]
    public async Task Move_ANewWriter_IsCreatedInItsUnit_NotMoved()
    {
        var http = MoveFake(existingUnit: null, existingObjectId: null, readBackUnit: WriterBu);

        var outcome = await Creator(http).EnsureAppUserAsync(WriterMoveRequest(), CancellationToken.None);

        outcome.Should().Be(new DataverseAppUserCreationOutcome.Success(SystemUser));
        http.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);
        http.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post).Which.Body
            .Should().Contain($"/businessunits({WriterBu})");
    }

    [Fact]
    public async Task Move_WithARoleRequested_IsACallerBug()
    {
        var http = MoveFake(existingUnit: CustomerBu, existingObjectId: UamiObjectId, readBackUnit: WriterBu);

        var act = () => Creator(http).EnsureAppUserAsync(WriterMoveRequest() with { SecurityRoleName = "System Administrator" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        http.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Move_WithoutAnObjectId_OrFromTheTargetUnit_OrFromEmpty_IsACallerBug(bool fromTarget, bool fromEmpty)
    {
        var http = MoveFake(existingUnit: CustomerBu, existingObjectId: UamiObjectId, readBackUnit: WriterBu);
        var request = fromTarget
            ? WriterMoveRequest() with { MoveFromBusinessUnitId = Guid.Parse(WriterBu) }
            : fromEmpty
                ? WriterMoveRequest() with { MoveFromBusinessUnitId = Guid.Empty }
                : WriterMoveRequest() with { SystemUserAzureActiveDirectoryObjectId = null };

        var act = () => Creator(http).EnsureAppUserAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        http.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData(" ")]
    public async Task Move_WithAnObjectIdThatIsNotAGuid_IsACallerBug_BeforeAnyRequest(string objectId)
    {
        // MoveAsync compares the row's object id with Guid.Parse: the guard makes that parse unable to throw.
        var http = MoveFake(existingUnit: CustomerBu, existingObjectId: UamiObjectId, readBackUnit: WriterBu);

        var act = () => Creator(http).EnsureAppUserAsync(
            WriterMoveRequest() with { SystemUserAzureActiveDirectoryObjectId = objectId }, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("as a GUID");
        http.Requests.Should().BeEmpty();
    }

    private static FakeHttp MoveFake(string? existingUnit, string? existingObjectId, string readBackUnit,
        HttpStatusCode patchStatus = HttpStatusCode.NoContent)
        => new(req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (req.Method == HttpMethod.Patch)
            {
                return new HttpResponseMessage(patchStatus) { Content = new StringContent(patchStatus == HttpStatusCode.NoContent ? "" : "refused") };
            }
            if (req.Method == HttpMethod.Post && uri.EndsWith("/api/data/v9.2/systemusers", StringComparison.Ordinal))
            {
                return Created($"{EnvUrl}api/data/v9.2/systemusers({SystemUser})");
            }
            if (uri.Contains("/systemusers?"))
            {
                return Json(existingUnit is null
                    ? """{"value":[]}"""
                    : $$"""{"value":[{"systemuserid":"{{SystemUser}}","_businessunitid_value":"{{existingUnit}}","azureactivedirectoryobjectid":{{(existingObjectId is null ? "null" : $"\"{existingObjectId}\"")}}}]}""");
            }
            if (uri.Contains($"/systemusers({SystemUser})?"))
            {
                return Json($$"""{"systemuserid":"{{SystemUser}}","_businessunitid_value":"{{readBackUnit}}"}""");
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"unexpected {req.Method} {uri}") };
        });

    // ---------------- helpers ----------------

    private static DataverseAppUserCreationRequest Request(string? objectId = null)
        => new(EnvUrl, TenantId, AppId, "System Administrator", Guid.Parse(CustomerBu), objectId);

    /// <summary>The customer-name lookup answers <paramref name="namedRows"/>; the reserved-name (Secure Record) lookup finds nothing.</summary>
    private static FakeHttp UnitFake(string namedRows)
        => new(req => req.RequestUri!.AbsoluteUri.Contains("parentbusinessunitid%20eq%20null")
            ? Json($$"""{"value":[{"businessunitid":"{{RootBu}}"}]}""")
            : req.RequestUri!.AbsoluteUri.Contains("Secure%20Record")
                ? Json("""{"value":[]}""")
                : Json($$"""{"value":{{namedRows}}}"""));

    /// <summary>
    /// A small environment of business units (all under the root) that answers <c>name eq</c> the way Dataverse does —
    /// ignoring case AND accents — and creates a unit on POST. <paramref name="faultOnName"/>: that name's lookup → 503.
    /// </summary>
    private static FakeHttp UnitsFake(
        IEnumerable<(string Id, string Name)> units, string? faultOnName = null, Action<string>? onCreate = null)
    {
        var all = units as List<(string Id, string Name)> ?? units.ToList();
        return new FakeHttp(req =>
        {
            var uri = Uri.UnescapeDataString(req.RequestUri!.AbsoluteUri);
            if (req.Method == HttpMethod.Post)
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var created = System.Text.RegularExpressions.Regex.Match(body, "\"name\":\"([^\"]*)\"").Groups[1].Value;
                onCreate?.Invoke(System.Text.RegularExpressions.Regex.Unescape(created));
                return Created($"{EnvUrl}api/data/v9.2/businessunits({CustomerBu})");
            }
            if (uri.Contains("parentbusinessunitid eq null"))
            {
                return Json($$"""{"value":[{"businessunitid":"{{RootBu}}"}]}""");
            }
            var match = System.Text.RegularExpressions.Regex.Match(uri, @"name eq '((?:[^']|'')*)'");
            var name = match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
            if (faultOnName is not null && Fold(name) == Fold(faultOnName))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") };
            }
            var rows = all.Where(u => Fold(u.Name) == Fold(name))
                .Select(u => $$"""{"businessunitid":"{{u.Id}}","_parentbusinessunitid_value":"{{RootBu}}"}""");
            return Json($$"""{"value":[{{string.Join(",", rows)}}]}""");
        });

        static string Fold(string value)
            => new string(value.Normalize(NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray()).ToUpperInvariant();
    }

    private static FakeHttp AppUserFake(string? existingUnit, int roleRows, bool roleHeld, string? existingObjectId = UamiObjectId)
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
                    : $$"""{"value":[{"systemuserid":"{{SystemUser}}","_businessunitid_value":"{{existingUnit}}","azureactivedirectoryobjectid":{{(existingObjectId is null ? "null" : $"\"{existingObjectId}\"")}}}]}""");
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
        // T186 audit: the role-copy lookup waits while Dataverse copies roles into a new unit — inject a tiny delay.
        => new(new HttpClient(http), Options.Create(new H10DataverseAppUserGraphParityOptions { PropagationRetryDelay = TimeSpan.FromMilliseconds(1) }),
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
