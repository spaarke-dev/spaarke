using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.ServiceModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 095g: <c>GET /api/finance/firms</c> and <c>POST /api/finance/core-records/{entityType}/{entityId}/firms</c> through the
/// REAL <c>MapCoreRecordFirmEndpoints</c>, <see cref="FinanceAuthorizationFilter"/> (fail-closed mode), <see cref="AuthorizationService"/>,
/// the kill switch, the REAL <see cref="CoreRecordFirmAssignmentService"/> and the REAL <see cref="AssignedAccessMaterializer"/> (over the
/// assigned-access harness), substituted only at the Dataverse seams (<see cref="FirmWorld"/>).
/// </summary>
/// <remarks>
/// Every refusal asserts its status, its code, that no slot write landed and that no access pass ran (notes 095g section 6). The case ids
/// (N1..N18) are the design's.
/// </remarks>
public sealed class CoreRecordFirmEndpointContractTests
{
    private const string MatterSet = FinanceAuthorizationFilter.MatterEntitySet;
    private const string ProjectSet = FinanceAuthorizationFilter.ProjectEntitySet;
    private const string WorkAssignmentSet = FinanceAuthorizationFilter.WorkAssignmentEntitySet;
    private const string FirmSet = FinanceAuthorizationFilter.VendorOrganizationEntitySet;
    private const string Uniform404 = "sdap.access.deny.record_unavailable";
    private const string Unavailable = "ebilling.submission.record_unavailable";

    internal static readonly Guid MatterId = Guid.Parse("095f0000-0000-4000-8000-000000000001");
    internal static readonly Guid ParentMatterId = Guid.Parse("095f0000-0000-4000-8000-000000000002");
    internal static readonly Guid ProjectId = Guid.Parse("095f0000-0000-4000-8000-000000000003");
    internal static readonly Guid WorkAssignmentId = Guid.Parse("095f0000-0000-4000-8000-000000000004");
    internal static readonly Guid FirmA = Guid.Parse("095f0000-0000-4000-8000-0000000000a1");
    internal static readonly Guid FirmB = Guid.Parse("095f0000-0000-4000-8000-0000000000b2");
    internal static readonly Guid FirmC = Guid.Parse("095f0000-0000-4000-8000-0000000000c3");
    internal static readonly Guid InactiveFirm = Guid.Parse("095f0000-0000-4000-8000-0000000000d4");
    internal static readonly Guid UntypedOrganization = Guid.Parse("095f0000-0000-4000-8000-0000000000e5");
    internal static readonly Guid OtherTypeOrganization = Guid.Parse("095f0000-0000-4000-8000-0000000000f6");
    internal static readonly Guid LawFirmType = Guid.Parse("095f0000-0000-4000-8000-000000000101");
    internal static readonly Guid OtherType = Guid.Parse("095f0000-0000-4000-8000-000000000102");
    internal static readonly Guid CallerSystemUser = Guid.Parse("095f0000-0000-4000-8000-000000000201");

    private static readonly Guid CallerOid = Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId);

    // ═══════════════════════════════════════════════════════════════════════════
    // Authentication, step 0, input, kill switch
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095g: unauthenticated is 401 on both routes and reads nothing")]
    public async Task Unauthenticated_Is401()
    {
        await using var host = await Host.StartAsync();

        (await host.AssignAsync(new { add = FirmA }, authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.ListAsync("", authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory(DisplayName = "095g N1/N2: a flagged-external or row-less caller holding every right is the uniform 404 on both routes, with no rights call and nothing read (must fire)")]
    [InlineData(StepZeroCallerKind.FlagTrue)]
    [InlineData(StepZeroCallerKind.NoSystemUserRow)]
    public async Task StepZero_RefusesAsTheUniform404_HoldingEveryRight(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.StepZero.Kind = kind;

        var assign = await host.AssignAsync(new { add = FirmA });
        var list = await host.ListAsync("");

        await host.StepZero.AssertRefusedAsync(assign, kind, uniformNotFound: true);
        await host.StepZero.AssertRefusedAsync(list, kind, uniformNotFound: true);
        host.Access.Calls.Should().BeEmpty();
        host.Probe.Calls.Should().BeEmpty();
        host.ShouldHaveDoneNothing();
    }

    [Theory(DisplayName = "095g fault: a step-0 read fault is 503 on both routes, never the uniform 404, and nothing is read")]
    [InlineData(StepZeroCallerKind.FlagReadFault)]
    [InlineData(StepZeroCallerKind.LookupFault)]
    public async Task StepZeroFault_Is503(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.StepZero.Kind = kind;

        foreach (var response in new[] { await host.AssignAsync(new { add = FirmA }), await host.ListAsync("") })
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await ErrorCode(response)).Should().Be(Unavailable);
        }

        host.Access.Calls.Should().BeEmpty();
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g must-not-fire: the two internal kinds pass step 0 and the add is applied")]
    public async Task StepZero_AdmitsTheInternalKinds()
    {
        foreach (var kind in StatedCaller.InternalKinds)
        {
            await using var host = await Host.StartAsync();
            host.GrantEverything();
            host.StepZero.Kind = kind;

            var response = await host.AssignAsync(new { add = FirmA });

            response.StatusCode.Should().Be(HttpStatusCode.OK, kind.ToString());
        }
    }

    [Theory(DisplayName = "095g N18: a bad body or entity type is 400 after step 0 and before any rights call")]
    [InlineData("Matter", "{}")]
    [InlineData("Matter", """{"add":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("Matter", """{"remove":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("Matter", """{"add":"095f0000-0000-4000-8000-0000000000a1","remove":"095f0000-0000-4000-8000-0000000000a1"}""")]
    [InlineData("Matter", """{"add":"095f0000-0000-4000-8000-0000000000a1","confirmRevoke":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("Invoice", """{"add":"095f0000-0000-4000-8000-0000000000a1"}""")]
    public async Task BadInput_Is400(string entityType, string body)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();

        var response = await host.AssignRawAsync(entityType, MatterId, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().BeEmpty();
        host.ShouldHaveDoneNothing();
    }

    [Theory(DisplayName = "095g list: a bad search, page or pageSize is 400 before any rights call")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("page=0")]
    [InlineData("page=x")]
    public async Task List_BadQuery_Is400(string query)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();

        var response = await host.ListRawAsync(query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Probe.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095g list (uac-r2 review): page above the bound is 400 with no privilege call and no Dataverse request; the bound itself is accepted")]
    public async Task List_PageAboveTheBound_Is400()
    {
        CoreRecordFirmAssignmentService.PageMax.Should().Be(200);

        await using var over = await Host.StartAsync();
        over.GrantEverything();
        var refused = await over.ListRawAsync("page=201");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("page must be between 1 and 200");
        over.Probe.Calls.Should().BeEmpty();
        over.World.Entities.Invocations.Should().BeEmpty("no Dataverse request is sent");

        await using var at = await Host.StartAsync();
        at.GrantEverything();
        var accepted = await at.ListRawAsync("page=200");

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(accepted);
        json["page"]!.GetValue<int>().Should().Be(200);
        json["items"]!.AsArray().Should().BeEmpty();
    }

    [Fact(DisplayName = "095g list (uac-r2 review): the page bound runs after step 0, as the other query 400s do - an external caller is the uniform 404")]
    public async Task List_PageAboveTheBound_AfterStepZero()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.StepZero.Kind = StepZeroCallerKind.FlagTrue;

        var response = await host.ListRawAsync("page=201");

        await host.StepZero.AssertRefusedAsync(response, StepZeroCallerKind.FlagTrue, uniformNotFound: true);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095g list: a search over 100 characters is 400")]
    public async Task List_LongSearch_Is400()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();

        var response = await host.ListAsync(new string('a', CoreRecordFirmAssignmentService.SearchMaxLength + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095g (Q10): the kill switch off is 503 ebilling.disabled on both routes, after the filter, and nothing is read")]
    public async Task KillSwitchOff_Is503()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantEverything();

        foreach (var response in new[] { await host.AssignAsync(new { add = FirmA }), await host.ListAsync("") })
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await ErrorCode(response)).Should().Be(EbillingEnabledFilter.ErrorCode);
        }

        host.ShouldHaveDoneNothing();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Record rights (step 0 of the sequence), uac-r2 R1
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "095g N4 / uac-r2 R1: an add without Write AND Append on the record is the uniform 404 before anything is read (must fire)")]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.Read)]
    [InlineData(AccessRights.Read | AccessRights.Write)]
    [InlineData(AccessRights.Read | AccessRights.Append)]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo)]
    public async Task Add_WithoutWriteAndAppend_IsUniform404(AccessRights rights)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.Access.Grant(MatterSet, MatterId, rights);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCode(response)).Should().Be(Uniform404);
        host.Access.Calls.Should().ContainSingle("ONE snapshot answers Write and Append")
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, MatterSet, MatterId, HasToken: true));
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g uac-r2 R1 control: the same add with Read, Write and Append is applied (must not fire)")]
    public async Task Add_WithWriteAndAppend_IsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.Access.Grant(MatterSet, MatterId, AccessRights.Read | AccessRights.Write | AccessRights.Append);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.Writes.Should().ContainSingle();
    }

    [Fact(DisplayName = "095g uac-r2 R1: a pure clear needs Read and Write only (Append is not asked of a clear)")]
    public async Task Clear_NeedsWriteOnly()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA);
        host.Access.Grant(MatterSet, MatterId, AccessRights.Read | AccessRights.Write);

        var response = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((null, null));
    }

    public static IEnumerable<object[]> WithoutReadBodies() => new[]
    {
        // Without Read every answer would describe the record: its firms by name, and (for a clear) whether the named firm is on it.
        new object[] { "add, Write and Append", AccessRights.Write | AccessRights.Append | AccessRights.AppendTo, "add" },
        new object[] { "replace, Write and Append", AccessRights.Write | AccessRights.Append | AccessRights.AppendTo, "replace" },
        new object[] { "clear, Write", AccessRights.Write | AccessRights.Append | AccessRights.AppendTo, "clear-on" },
        new object[] { "clear of a firm not on the record, Write", AccessRights.Write | AccessRights.Append | AccessRights.AppendTo, "clear-off" },
    };

    [Theory(DisplayName = "095g uac-r2 review: without Read on the record an add, replace or clear is the uniform 404, byte-identical to an unknown record, nothing written (must fire)")]
    [MemberData(nameof(WithoutReadBodies))]
    public async Task WithoutRead_IsUniform404_LikeAnUnknownRecord(string name, AccessRights rights, string body)
    {
        object Body() => body switch
        {
            "add" => new { add = FirmC },
            "replace" => new { add = FirmC, remove = FirmA, confirmRevoke = FirmA },
            "clear-on" => new { remove = FirmA, confirmRevoke = FirmA },
            _ => new { remove = FirmB, confirmRevoke = FirmB },
        };

        await using var refused = await Host.StartAsync();
        refused.GrantEverything();
        refused.World.Matter(MatterId, slot1: FirmA);
        refused.Access.Grant(MatterSet, MatterId, rights);
        var denied = await refused.AssignAsync(Body());

        await using var unknown = await Host.StartAsync();
        unknown.GrantEverything();
        var absent = await unknown.AssignAsync(Body(), recordId: Guid.Parse("095f0000-0000-4000-8000-0000000009fe"));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound, name);
        (await ReasonCode(denied)).Should().Be(Uniform404);
        absent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StableBody(denied)).Should().Be(await StableBody(absent), "a caller without Read learns nothing the unknown record does not tell");
        refused.ShouldHaveDoneNothing();
        refused.World.SlotsOf(MatterId).Should().Be((FirmA, null));
    }

    [Fact(DisplayName = "095g uac-r2 R1 (review 1 F4): a replace {add, remove, confirmRevoke} with Write only is the uniform 404 - the add in it needs Append (must fire)")]
    public async Task Replace_WithWriteOnly_IsUniform404()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA);
        host.Access.Grant(MatterSet, MatterId, AccessRights.Read | AccessRights.Write);

        var response = await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCode(response)).Should().Be(Uniform404);
        host.ShouldHaveDoneNothing();
        host.World.SlotsOf(MatterId).Should().Be((FirmA, null));
    }

    [Fact(DisplayName = "095g uac-r2 R1 (review 1 F4) control: the same replace with Write and Append is applied (must not fire)")]
    public async Task Replace_WithWriteAndAppend_IsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA);
        host.Access.Grant(MatterSet, MatterId, AccessRights.Read | AccessRights.Write | AccessRights.Append);

        var response = await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((FirmC, null));
    }

    [Fact(DisplayName = "095g: a clear without Write is the uniform 404 (must fire)")]
    public async Task Clear_WithoutWrite_IsUniform404()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA);
        host.Access.Grant(MatterSet, MatterId, AccessRights.Read | AccessRights.Append | AccessRights.AppendTo);

        var response = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCode(response)).Should().Be(Uniform404);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g design 3e: a record-rights answer Dataverse did not complete is 503, never the uniform 404, and nothing is read")]
    public async Task RecordRightsFault_Is503()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.Access.Fault(MatterSet, MatterId);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be(Unavailable);
        host.ShouldHaveDoneNothing();
    }

    public static IEnumerable<object[]> RecordRightsHelperCases() => new[]
    {
        // Held wins, faulted or not: a degraded Read still answers a Read operation (the request gets the rights it always did).
        new object[] { "Read, complete, Read op", AccessRights.Read, false, "finance.read", FinanceCheckVerdict.Allowed },
        new object[] { "Read, FAULTED (degraded), Read op", AccessRights.Read, true, "finance.read", FinanceCheckVerdict.Allowed },
        new object[] { "all rights, complete, add op", AccessRights.Read | AccessRights.Write | AccessRights.Append, false, "ebilling.record.assign_firm", FinanceCheckVerdict.Allowed },

        // Not held and faulted: Faulted (a degraded Read never satisfies Write and Append).
        new object[] { "Read, FAULTED (degraded), add op", AccessRights.Read, true, "ebilling.record.assign_firm", FinanceCheckVerdict.Faulted },
        new object[] { "no Read, FAULTED (None), Read op", AccessRights.AppendTo, true, "finance.read", FinanceCheckVerdict.Faulted },

        // Not held and complete: Denied.
        new object[] { "Read, complete, add op", AccessRights.Read, false, "ebilling.record.assign_firm", FinanceCheckVerdict.Denied },
        new object[] { "None, complete, Read op", AccessRights.None, false, "finance.read", FinanceCheckVerdict.Denied },
    };

    [Theory(DisplayName = "095g review 2: the one record-rights helper - held wins whatever Faulted says; not held and faulted is Faulted; otherwise Denied")]
    [MemberData(nameof(RecordRightsHelperCases))]
    public async Task RecordRightsHelper_HeldWins_ThenFaulted_ThenDenied(
        string name, AccessRights granted, bool faulted, string operation, FinanceCheckVerdict expected)
    {
        var source = new FinAuth.RecordingAccessDataSource();
        var authorization = new AuthorizationService(source, Array.Empty<IAuthorizationRule>(), NullLogger<AuthorizationService>.Instance);
        source.Grant(MatterSet, MatterId, granted);
        if (faulted)
        {
            source.Fault(MatterSet, MatterId); // the production shapes: Read when readable, else None
        }

        var verdict = await FinanceAuthorizationFilter.CallerRecordVerdictAsync(
            authorization, "user", MatterSet, MatterId, operation, "token", CancellationToken.None);
        var holds = await FinanceAuthorizationFilter.CallerHoldsRightsAsync(
            authorization, "user", MatterSet, MatterId, operation, "token", CancellationToken.None);

        verdict.Should().Be(expected, name);
        holds.Should().Be(expected == FinanceCheckVerdict.Allowed, "the handlers' check and the filter's check give one answer: " + name);
    }

    [Fact(DisplayName = "095g review 2: an add given a degraded Read on a faulted answer is 503, never the uniform 404, and nothing is read")]
    public async Task Add_GivenADegradedReadOnAFault_Is503()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.Access.Grant(MatterSet, MatterId, AccessRights.Read); // readable, so the fault answers a degraded Read
        host.Access.Fault(MatterSet, MatterId);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be(Unavailable);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g review 2: the firm list asks no record rights, so a faulted record answer does not reach it; its table-privilege check decides (fail-closed mode)")]
    public async Task List_IsNotDecidedByARecordFault()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.Access.Fault(MatterSet, MatterId);
        host.Access.Fault(FirmSet, FirmA);

        var response = await host.ListAsync("");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Access.Calls.Should().NotContain(c => c.Path == FinAuth.AccessPath.Record, "the list declares a table-privilege check only");
        host.Probe.Calls.Should().ContainSingle().Which.Privilege.Should().Be(CoreRecordFirmEndpoints.ReadOrganizationPrivilege);
    }

    [Fact(DisplayName = "095g N1-N4, N11, N12: every uniform 404 has the same status and the same bytes")]
    public async Task Uniform404_IsByteIdentical()
    {
        var unknownRecord = Guid.Parse("095f0000-0000-4000-8000-0000000009ff");
        var unknownFirm = Guid.Parse("095f0000-0000-4000-8000-0000000008ff");
        var cases = new (string Name, Func<Host, Task<HttpResponseMessage>> Send)[]
        {
            ("N1 flagged external", async h => { h.StepZero.Kind = StepZeroCallerKind.FlagTrue; return await h.AssignAsync(new { add = FirmA }); }),
            ("N2 no systemuser", async h => { h.StepZero.Kind = StepZeroCallerKind.NoSystemUserRow; return await h.AssignAsync(new { add = FirmA }); }),
            ("N3 unknown record", h => h.AssignAsync(new { add = FirmA }, recordId: unknownRecord)),
            ("N4 Read only", async h => { h.Access.Grant(MatterSet, MatterId, AccessRights.Read); return await h.AssignAsync(new { add = FirmA }); }),
            ("N11 unknown firm", h => h.AssignAsync(new { add = unknownFirm })),
            ("N12 no AppendTo on the firm", async h => { h.Access.Grant(FirmSet, FirmA, AccessRights.Read); return await h.AssignAsync(new { add = FirmA }); }),
            ("N12b no Read on the firm", async h => { h.Access.Grant(FirmSet, FirmA, AccessRights.AppendTo); return await h.AssignAsync(new { add = FirmA }); }),
            ("record gone after the check", async h => { h.World.Records.Remove(("sprk_matter", MatterId)); return await h.AssignAsync(new { add = FirmA }); }),
            ("firm row gone after the check", async h => { h.World.Firms.Remove(FirmA); return await h.AssignAsync(new { add = FirmA }); }),
        };

        var seen = new List<(string Name, HttpStatusCode Status, string Body)>();
        foreach (var (name, send) in cases)
        {
            await using var host = await Host.StartAsync();
            host.GrantEverything();

            var response = await send(host);

            seen.Add((name, response.StatusCode, await StableBody(response)));
            host.ShouldHaveWrittenNothing(name);
        }

        seen.Select(s => s.Status).Distinct().Should().ContainSingle().Which.Should().Be(HttpStatusCode.NotFound);
        seen.Select(s => s.Body).Distinct().Should().ContainSingle("one body for all: " + string.Join(" | ", seen.Select(s => s.Name + "=" + s.Body)));
        seen[0].Body.Should().Contain(Uniform404);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 2: eligibility (one rule with the materializer)
    // ═══════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> FlagRefusals() => new[]
    {
        new object[] { "N5 Restricted", new RootRecordFlags(IsSecure: false, IsRestricted: true), CoreRecordFirmCodes.RecordRestricted, false },
        new object[] { "N6 Secure", new RootRecordFlags(IsSecure: true, IsRestricted: false), CoreRecordFirmCodes.RecordSecure, false },
        new object[] { "N9 Limited", new RootRecordFlags(IsSecure: false, IsRestricted: false, IsLimited: true), CoreRecordFirmCodes.RecordLimited, false },
        new object[] { "Secure and Restricted", new RootRecordFlags(IsSecure: true, IsRestricted: true), CoreRecordFirmCodes.RecordRestricted, false },
        new object[] { "Q2 inactive Secure", new RootRecordFlags(IsSecure: true, IsRestricted: false, IsInactive: true), CoreRecordFirmCodes.RecordSecure, false },
        new object[] { "Q2 inactive Restricted", new RootRecordFlags(IsSecure: false, IsRestricted: true, IsInactive: true), CoreRecordFirmCodes.RecordRestricted, false },

        // uac-r2 review: a REPLACE adds a firm too, so it is refused the same way (the old firm stays, nothing is revoked).
        new object[] { "N6 Secure, replace", new RootRecordFlags(IsSecure: true, IsRestricted: false), CoreRecordFirmCodes.RecordSecure, true },
    };

    [Theory(DisplayName = "095g N5, N6, N9 and Q2: a Restricted, Secure or Limited record refuses an add (or a replace) with 409 and its code, active or inactive (must fire)")]
    [MemberData(nameof(FlagRefusals))]
    public async Task Add_OnAFlaggedRecord_Is409(string name, RootRecordFlags flags, string code, bool replace)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: replace ? FirmA : null, flags: flags, active: !flags.IsInactive);

        var response = replace
            ? await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmA })
            : await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, name);
        (await ErrorCode(response)).Should().Be(code);
        host.ShouldHaveDoneNothing();
        host.World.SlotsOf(MatterId).Should().Be((replace ? FirmA : (Guid?)null, (Guid?)null));
    }

    [Fact(DisplayName = "095g N5-N9 control: the same add on a Standard record is applied (must not fire)")]
    public async Task Add_OnAStandardRecord_IsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "095g N7: a work assignment under a SECURE matter, its own flag clear, refuses an add (record_secure)")]
    public async Task N7_WorkAssignmentUnderASecureMatter_Is409Secure()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.FiledMatter(ParentMatterId, secure: true);
        host.World.WorkAssignment(WorkAssignmentId, parentMatter: ParentMatterId);
        host.Access.Grant(WorkAssignmentSet, WorkAssignmentId, AccessRights.Read | AccessRights.Write | AccessRights.Append);

        var response = await host.AssignAsync(new { add = FirmA }, entityType: "WorkAssignment", recordId: WorkAssignmentId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.RecordSecure);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g N7 control: the same work assignment under a Standard matter is applied")]
    public async Task N7Control_WorkAssignmentUnderAStandardMatter_IsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.FiledMatter(ParentMatterId, secure: false);
        host.World.WorkAssignment(WorkAssignmentId, parentMatter: ParentMatterId);
        host.Access.Grant(WorkAssignmentSet, WorkAssignmentId, AccessRights.Read | AccessRights.Write | AccessRights.Append);

        var response = await host.AssignAsync(new { add = FirmA }, entityType: "WorkAssignment", recordId: WorkAssignmentId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(WorkAssignmentId).Should().Be((FirmA, null));
    }

    [Fact(DisplayName = "095g N8: a project under a RESTRICTED matter, its own flag clear, refuses an add (record_restricted)")]
    public async Task N8_ProjectUnderARestrictedMatter_Is409Restricted()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.FiledMatter(ParentMatterId, secure: false, permission: ExternalParticipationService.AccessPermissionRestricted);
        host.World.Project(ProjectId, parentMatter: ParentMatterId);
        host.Access.Grant(ProjectSet, ProjectId, AccessRights.Read | AccessRights.Write | AccessRights.Append);

        var response = await host.AssignAsync(new { add = FirmA }, entityType: "Project", recordId: ProjectId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.RecordRestricted);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g N8 control: the same project under a Standard matter is applied")]
    public async Task N8Control_ProjectUnderAStandardMatter_IsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.FiledMatter(ParentMatterId, secure: false);
        host.World.Project(ProjectId, parentMatter: ParentMatterId);
        host.Access.Grant(ProjectSet, ProjectId, AccessRights.Read | AccessRights.Write | AccessRights.Append);

        var response = await host.AssignAsync(new { add = FirmA }, entityType: "Project", recordId: ProjectId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "095g: a clear on a Secure (or Restricted) record is allowed - a revoke is never blocked by flags")]
    public async Task Clear_OnASecureRecord_IsAllowed()
    {
        foreach (var flags in new[] { new RootRecordFlags(true, false), new RootRecordFlags(false, true), new RootRecordFlags(false, false, IsLimited: true) })
        {
            await using var host = await Host.StartAsync();
            host.GrantEverything();
            host.World.Matter(MatterId, slot1: FirmA, flags: flags);

            var response = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmA });

            response.StatusCode.Should().Be(HttpStatusCode.OK, flags.ToString());
            host.World.SlotsOf(MatterId).Should().Be((null, null));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Q2 (EB-D-233): an inactive record is allowed, and promises nothing now
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095g Q2: an add to an INACTIVE Standard record writes the slot, grants nothing, answers applied with granted [] and the when-active notice")]
    public async Task Q2_InactiveStandardRecord_WritesTheSlot_GrantsNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, flags: new RootRecordFlags(false, false, IsInactive: true), active: false);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(response);
        json["changed"]!.GetValue<bool>().Should().BeTrue();
        json["recordActive"]!.GetValue<bool>().Should().BeFalse();
        json["access"]!["state"]!.GetValue<string>().Should().Be("applied");
        json["access"]!["granted"]!.AsArray().Should().BeEmpty();
        json["notice"]!.GetValue<string>().Should().Be("Firm A's people will get access to this record when it is active again.");
        host.World.SlotsOf(MatterId).Should().Be((FirmA, null));
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty("the materializer skips an inactive record");
        host.World.Harness.Store.RowsOf(MatterId, FirmA).Should().ContainSingle().Which.Reason.Should().Be(AssignedAccessReason.RootInactive);
    }

    [Fact(DisplayName = "095g Q2: after the inactive add, reactivating the record lets the access pass grant the firm (materializer level)")]
    public async Task Q2_Reactivation_Grants()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, flags: new RootRecordFlags(false, false, IsInactive: true), active: false);
        (await host.AssignAsync(new { add = FirmA })).StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty();

        host.World.Harness.Participations.Flags[MatterId] = RootRecordFlags.None; // the record is active again
        var outcome = await host.World.Harness.SyncAsync(ExternalGrantRootType.Matter, MatterId);

        outcome.Complete.Should().BeTrue();
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The 32-combination theory (uac-r2 condition 2): step 2 equals the materializer's skip
    // ═══════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> AllFlagCombinations() =>
        Enumerable.Range(0, 32).Select(bits => new object[]
        {
            (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0,
        });

    [Theory(DisplayName = "095g theory: for all 32 RootRecordFlags combinations the route's step 2 and the materializer's organization-grant decision agree")]
    [MemberData(nameof(AllFlagCombinations))]
    public async Task StepTwo_AgreesWithTheMaterializer(bool secure, bool restricted, bool limited, bool unreadable, bool inactive)
    {
        var flags = new RootRecordFlags(secure, restricted, limited, unreadable, inactive);
        var world = new FirmWorld();
        world.Matter(MatterId, flags: flags, active: !inactive);

        var result = await world.Service().AssignFirmAsync(
            new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, MatterId), FirmA, null, null, CallerOid.ToString("D"),
            (_, _) => Task.FromResult<bool?>(true), CancellationToken.None);

        if (unreadable)
        {
            result.Status.Should().Be(StatusCodes.Status503ServiceUnavailable, "unreadable flags cannot be decided");
            world.Writes.Should().BeEmpty();
            world.Harness.Store.RootReads.Should().Be(0);

            // The materializer never grants on flags it cannot read either.
            world.Harness.Store.Assign(ExternalGrantRootType.Matter, MatterId, CoreRecordFirmAssignmentService.Slot1Column, FirmA);
            await world.Harness.SyncAsync(ExternalGrantRootType.Matter, MatterId);
            world.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty();
            return;
        }

        if (result.Status == StatusCodes.Status200OK)
        {
            // The route admitted the add and ran the pass: the materializer granted, or (inactive only) skipped for the record's state.
            world.Writes.Should().ContainSingle();
            var row = world.Harness.Store.RowsOf(MatterId, FirmA).Should().ContainSingle().Subject;
            if (inactive)
            {
                row.Reason.Should().Be(AssignedAccessReason.RootInactive);
                world.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty();

                // Review 1: the inactive skip must not hide a flag refusal. The same firm on the same flags with the record ACTIVE is granted.
                var activeCopy = new FirmWorld();
                activeCopy.Matter(MatterId, slot1: FirmA, flags: flags with { IsInactive = false });
                await activeCopy.Harness.SyncAsync(ExternalGrantRootType.Matter, MatterId);
                activeCopy.Harness.Store.RowsOf(MatterId, FirmA).Should().ContainSingle()
                    .Which.State.Should().Be(AssignedAccessState.Granted, "the route admitted the add, so the flags alone must allow the grant");
            }
            else
            {
                row.State.Should().Be(AssignedAccessState.Granted);
                world.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().ContainSingle();
            }

            return;
        }

        // The route refused: the materializer, given the same firm in the slot on the same flags, never grants.
        result.Status.Should().Be(StatusCodes.Status409Conflict);
        world.Writes.Should().BeEmpty();
        world.Harness.Store.RootReads.Should().Be(0);
        world.Harness.Store.Assign(ExternalGrantRootType.Matter, MatterId, CoreRecordFirmAssignmentService.Slot1Column, FirmA);
        await world.Harness.SyncAsync(ExternalGrantRootType.Matter, MatterId);
        world.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty();

        // And the code the route gave is the reason the materializer skips for on the same flags with the record ACTIVE (Q2 condition 2:
        // the record's state is not part of the rule, so an inactive record is refused exactly as its active counterpart).
        var active = new FirmWorld();
        active.Matter(MatterId, slot1: FirmA, flags: flags with { IsInactive = false });
        await active.Harness.SyncAsync(ExternalGrantRootType.Matter, MatterId);
        var activeRow = active.Harness.Store.RowsOf(MatterId, FirmA).Should().ContainSingle().Subject;
        var reason = activeRow.State == AssignedAccessState.Granted ? null : activeRow.Reason;
        CodeFor(reason).Should().Be(result.Code, $"route {result.Code} vs materializer {reason ?? "granted"}");
    }

    private static string? CodeFor(string? reason) => reason switch
    {
        AssignedAccessReason.Restricted => CoreRecordFirmCodes.RecordRestricted,
        AssignedAccessReason.OrganizationOnSecure => CoreRecordFirmCodes.RecordSecure,
        AssignedAccessReason.OrganizationOnLimited => CoreRecordFirmCodes.RecordLimited,
        _ => "unmapped:" + reason,
    };

    [Fact(DisplayName = "095g design 3f: OrganizationGrantSkipReason never reads the record's state (Q2 condition 2)")]
    public void OrganizationGrantSkipReason_IgnoresTheRecordState()
    {
        foreach (var bits in Enumerable.Range(0, 16))
        {
            var active = new RootRecordFlags((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, IsInactive: false);
            var inactive = active with { IsInactive = true };
            AssignedAccessMaterializer.OrganizationGrantSkipReason(inactive).Should().Be(AssignedAccessMaterializer.OrganizationGrantSkipReason(active));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 3-5: the firm
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095g N10: an inactive firm is 422 firm_inactive")]
    public async Task N10_InactiveFirm_Is422()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();

        var response = await host.AssignAsync(new { add = InactiveFirm });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.FirmInactive);
        host.ShouldHaveDoneNothing();
    }

    [Theory(DisplayName = "095g Q3: an organization that is not of the Law Firm type is 422 not_a_law_firm on add (must fire)")]
    [InlineData("untyped")]
    [InlineData("another type")]
    public async Task Q3_NotALawFirm_Is422(string which)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        var organization = which == "untyped" ? UntypedOrganization : OtherTypeOrganization;

        var response = await host.AssignAsync(new { add = organization });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.NotALawFirm);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g Q3: a clear removes any firm, a non-law-firm included (the type is not checked on a clear)")]
    public async Task Q3_ClearOfANonLawFirm_Works()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: UntypedOrganization, slot2: OtherTypeOrganization);
        host.World.Types.Clear(); // even with no Law Firm type code on the environment

        var first = await host.AssignAsync(new { remove = UntypedOrganization, confirmRevoke = UntypedOrganization });
        var second = await host.AssignAsync(new { remove = OtherTypeOrganization, confirmRevoke = OtherTypeOrganization });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((null, null));
    }

    [Theory(DisplayName = "095g Q3: no (or more than one) active type row carrying the Law Firm code is 503 on add, never 'every organization counts'")]
    [InlineData("none")]
    [InlineData("two")]
    [InlineData("inactive")]
    public async Task Q3_MissingTypeCode_Add_Is503(string shape)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.ShapeTypes(shape);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be(Unavailable);
        host.ShouldHaveDoneNothing();
    }

    [Theory(DisplayName = "095g Q3: no (or more than one) active type row carrying the Law Firm code is 503 on the list, never []")]
    [InlineData("none")]
    [InlineData("two")]
    [InlineData("inactive")]
    public async Task Q3_MissingTypeCode_List_Is503(string shape)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.ShapeTypes(shape);

        var response = await host.ListAsync("");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be(Unavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("items");
    }

    [Fact(DisplayName = "095g N13: a firm on the record's No Access list is 409 firm_no_access")]
    public async Task N13_FirmOnTheNoAccessList_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Harness.DenyList.DenyOrganizationOnRecord(FirmA, MatterId);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.FirmNoAccess);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g N13 control: another firm on the same record is applied")]
    public async Task N13Control_AnotherFirm_IsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Harness.DenyList.DenyOrganizationOnRecord(FirmB, MatterId);

        (await host.AssignAsync(new { add = FirmA })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Faults: each 503, nothing written, no access pass
    // ═══════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> Faults() => new[]
    {
        new object[] { "record row read" },
        new object[] { "slot firm names read" },
        new object[] { "own flags read" },
        new object[] { "own flags absent" },
        new object[] { "ancestry walk" },
        new object[] { "firm rights" },
        new object[] { "firm row read" },
        new object[] { "type row read" },
        new object[] { "No Access unverifiable" },
        new object[] { "No Access throws" },
        new object[] { "write throws" },
    };

    [Theory(DisplayName = "095g faults: every read or write fault is 503 ebilling.submission.record_unavailable with nothing written and no access pass")]
    [MemberData(nameof(Faults))]
    public async Task EveryFault_Is503(string fault)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        var entityType = "Matter";
        var recordId = MatterId;
        var world = host.World;
        world.Matter(MatterId, slot1: FirmB);
        switch (fault)
        {
            case "record row read": world.FaultRowReadsOf.Add("sprk_matter"); break;
            case "slot firm names read": world.FaultQueriesOf.Add("sprk_organization"); break;
            case "own flags read": world.Harness.Participations.ThrowOnRead = true; break;
            case "own flags absent": world.Harness.Participations.Absent[MatterId] = true; break;
            case "ancestry walk":
                world.FiledMatter(ParentMatterId, secure: false);
                world.WorkAssignment(WorkAssignmentId, parentMatter: ParentMatterId);
                world.Filing.FailingQueriesOf("sprk_matter");
                host.Access.Grant(WorkAssignmentSet, WorkAssignmentId, AccessRights.Read | AccessRights.Write | AccessRights.Append);
                entityType = "WorkAssignment";
                recordId = WorkAssignmentId;
                break;
            case "firm rights": host.Access.Fault(FirmSet, FirmA); break;
            case "firm row read": world.FaultRowReadsOf.Add("sprk_organization"); break;
            case "type row read": world.FaultQueriesOf.Add(LawFirmOrganizationType.TypeEntity); break;
            case "No Access unverifiable": world.Harness.DenyList.Faults = true; break;
            case "No Access throws": world.Harness.DenyList.Throws = new HttpRequestException("deny list down"); break;
            case "write throws": world.FaultWrites = true; break;
        }

        var response = await host.AssignAsync(new { add = FirmA }, entityType: entityType, recordId: recordId);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, fault);
        (await ErrorCode(response)).Should().Be(Unavailable);
        world.Writes.Should().BeEmpty("no write landed");
        world.Harness.Store.RootReads.Should().Be(0, "no access pass ran");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("deny list down");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Slots
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095g positive: an add fills slot 1, a second fills slot 2, each grants the firm with the caller as grantor (sprk_grantedby) and states the R2 notice")]
    public async Task Add_FillsSlot1ThenSlot2_AndGrants()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();

        var first = await host.AssignAsync(new { add = FirmA });
        var second = await host.AssignAsync(new { add = FirmB });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((FirmA, FirmB));
        var json = await Json(second);
        json["changed"]!.GetValue<bool>().Should().BeTrue();
        json["recordActive"]!.GetValue<bool>().Should().BeTrue();
        json["access"]!["state"]!.GetValue<string>().Should().Be("applied");
        json["access"]!["granted"]!.AsArray().Select(g => g!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmB);
        json["access"]!["revoked"]!.AsArray().Should().BeEmpty();
        json["notice"]!.GetValue<string>().Should().Be("Firm B's people will get access to this record.");
        json["assigned"]!.AsArray().Select(a => (a!["slot"]!.GetValue<int>(), a["firmId"]!.GetValue<Guid>(), a["name"]!.GetValue<string>(), a["active"]!.GetValue<bool>()))
            .Should().Equal((1, FirmA, "Firm A", true), (2, FirmB, "Firm B", true));
        json["firms"]!.AsArray().Select(f => f!["name"]!.GetValue<string>()).Should().Equal("Firm A", "Firm B");

        foreach (var firm in new[] { FirmA, FirmB })
        {
            host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: firm).Should().ContainSingle()
                .Which.GrantedBySystemUserId.Should().Be(CallerSystemUser, "the grantor is the caller, from the token's oid");
        }

        host.World.Writes.Select(w => string.Join(",", w.Fields.Keys)).Should().Equal(
            CoreRecordFirmAssignmentService.Slot1Column, CoreRecordFirmAssignmentService.Slot2Column);
        host.World.Writes.Should().OnlyContain(w => w.Fields.Count == 1, "only the slot column, nothing else");
    }

    [Fact(DisplayName = "095g positive: a repeat add is a no-op (changed false, no write) and the access pass still runs")]
    public async Task RepeatAdd_IsANoOp_AndThePassRuns()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        (await host.AssignAsync(new { add = FirmA })).StatusCode.Should().Be(HttpStatusCode.OK);
        var passes = host.World.Harness.Store.RootReads;

        var repeat = await host.AssignAsync(new { add = FirmA });

        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(repeat))["changed"]!.GetValue<bool>().Should().BeFalse();
        host.World.Writes.Should().ContainSingle("the repeat wrote nothing");
        host.World.Harness.Store.RootReads.Should().BeGreaterThan(passes, "L1 runs after a no-op too");
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().ContainSingle("the pass is idempotent");
    }

    [Fact(DisplayName = "095g N14: an add with both slots full is 409 slots_full with the assigned firms")]
    public async Task N14_SlotsFull_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA, slot2: FirmB);

        var response = await host.AssignAsync(new { add = FirmC });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await Json(response);
        json["errorCode"]!.GetValue<string>().Should().Be(CoreRecordFirmCodes.SlotsFull);
        json["detail"]!.GetValue<string>().Should().Be("Two firms are already assigned. Replace one of them.");
        json["assigned"]!.AsArray().Select(a => a!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmA, FirmB);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g N15: a replace without a confirmation is 409 revoke_confirmation_required naming the firm, and writes nothing")]
    public async Task N15_ReplaceWithoutConfirmation_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA, slot2: FirmB);

        var response = await host.AssignAsync(new { add = FirmC, remove = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await Json(response);
        json["errorCode"]!.GetValue<string>().Should().Be(CoreRecordFirmCodes.RevokeConfirmationRequired);
        json["revokes"]!.AsArray().Select(r => (r!["firmId"]!.GetValue<Guid>(), r["name"]!.GetValue<string>())).Should().Equal((FirmA, "Firm A"));
        json["assigned"]!.AsArray().Should().HaveCount(2);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g N16: a confirmation naming another firm is 409 revoke_confirmation_required (a stale confirmation cannot revoke a different firm)")]
    public async Task N16_ConfirmationNamingAnotherFirm_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA, slot2: FirmB);

        var replace = await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmB });
        var clear = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmB });

        foreach (var response in new[] { replace, clear })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.RevokeConfirmationRequired);
        }

        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g N17: a replace onto the firm in the other slot is 409 firm_already_assigned")]
    public async Task N17_ReplaceOntoTheOtherSlotsFirm_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA, slot2: FirmB);

        var response = await host.AssignAsync(new { add = FirmB, remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.FirmAlreadyAssigned);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g: a replace whose old firm is not on the record (and the new one is not either) is 409 record_changed with the assigned firms")]
    public async Task ReplaceOfAnAbsentFirm_Is409RecordChanged()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmB);

        var response = await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await Json(response);
        json["errorCode"]!.GetValue<string>().Should().Be(CoreRecordFirmCodes.RecordChanged);
        json["assigned"]!.AsArray().Select(a => a!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmB);
        host.ShouldHaveDoneNothing();
    }

    [Fact(DisplayName = "095g positive (Q8): a confirmed replace keeps its slot, revokes the old firm's automatic grant and grants the new firm")]
    public async Task Replace_KeepsItsSlot_RevokesAndGrants()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        (await host.AssignAsync(new { add = FirmA })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.AssignAsync(new { add = FirmB })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((FirmC, FirmB), "the new firm takes the replaced firm's slot; the other is not moved");
        host.World.Writes.Last().Fields.Keys.Should().Equal(CoreRecordFirmAssignmentService.Slot1Column);
        var json = await Json(response);
        json["access"]!["granted"]!.AsArray().Select(g => g!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmC);
        json["access"]!["revoked"]!.AsArray().Select(g => g!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmA);
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty("the replaced firm's unmodified grant is gone");
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmB).Should().ContainSingle();
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmC).Should().ContainSingle();
    }

    [Fact(DisplayName = "095g materializer integration: a replace keeps a grant an operator adopted (revoked is empty)")]
    public async Task Replace_KeepsAnAdoptedGrant()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        (await host.AssignAsync(new { add = FirmA })).StatusCode.Should().Be(HttpStatusCode.OK);
        var grant = host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Single();
        await host.World.Harness.Materializer.MarkGrantAdoptedAsync(ExternalGrantRootType.Matter, MatterId, null, FirmA, grant.Id, CancellationToken.None);

        var response = await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().ContainSingle("an adopted grant is kept");
        (await Json(response))["access"]!["revoked"]!.AsArray().Should().BeEmpty();
    }

    [Fact(DisplayName = "095g positive (Q8): a clear empties its slot and never moves the other firm up; a repeat clear is a no-op")]
    public async Task Clear_EmptiesItsSlot_AndRepeatIsANoOp()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        (await host.AssignAsync(new { add = FirmA })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.AssignAsync(new { add = FirmB })).StatusCode.Should().Be(HttpStatusCode.OK);

        var clear = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmA });
        var writes = host.World.Writes.Count;
        var repeat = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmA });

        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((null, FirmB));
        var json = await Json(clear);
        json["access"]!["revoked"]!.AsArray().Select(g => g!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmA);
        json["notice"].Should().BeNull("only an add carries the notice");
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty();
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(repeat))["changed"]!.GetValue<bool>().Should().BeFalse();
        host.World.Writes.Count.Should().Be(writes);
    }

    [Fact(DisplayName = "095g (review 1 F4): a replace of a firm held in BOTH slots leaves (new, empty), revokes the old firm once and grants the new one")]
    public async Task Replace_FirmInBothSlots_LeavesNewAndEmpty()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA, slot2: FirmA);
        (await host.World.Harness.SyncAsync(ExternalGrantRootType.Matter, MatterId)).Complete.Should().BeTrue();
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().ContainSingle("one grant for the firm, however many slots hold it");

        var response = await host.AssignAsync(new { add = FirmC, remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((FirmC, null));
        var json = await Json(response);
        json["access"]!["revoked"]!.AsArray().Select(g => g!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmA);
        json["access"]!["granted"]!.AsArray().Select(g => g!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmC);
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty();
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmC).Should().ContainSingle();
    }

    [Fact(DisplayName = "095g (review 1 F4): a clear of a firm held in BOTH slots empties both and revokes the firm once")]
    public async Task Clear_FirmInBothSlots_EmptiesBoth()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA, slot2: FirmA);
        (await host.World.Harness.SyncAsync(ExternalGrantRootType.Matter, MatterId)).Complete.Should().BeTrue();
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().ContainSingle();

        var response = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.SlotsOf(MatterId).Should().Be((null, null));
        (await Json(response))["access"]!["revoked"]!.AsArray().Select(g => g!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmA);
        host.World.Harness.Grants.ActiveRowsOf(MatterId, organizationId: FirmA).Should().BeEmpty();
    }

    [Fact(DisplayName = "095g positive: an incomplete inline access pass answers pending; the slot write stands and the notice is still given")]
    public async Task IncompletePass_IsPending()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Harness.Grants.FailQueries = true;

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(response);
        json["access"]!["state"]!.GetValue<string>().Should().Be("pending");
        json["notice"]!.GetValue<string>().Should().Be("Firm A's people will get access to this record.");
        host.World.SlotsOf(MatterId).Should().Be((FirmA, null));
    }

    [Fact(DisplayName = "095g: a fault reading the firm list after a successful write answers 200 with firms null (the write stands)")]
    public async Task FirmListFaultAfterTheWrite_IsFirmsNull()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        // The record's organization references become unreadable during the write (after the No Access check passed).
        host.World.BeforeWrite = _ => host.World.Harness.Participations.UnreadableReferencedOrganizations[MatterId] = true;

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["firms"].Should().BeNull();
        host.World.SlotsOf(MatterId).Should().Be((FirmA, null));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Concurrency (design 3c)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095g concurrency: a version mismatch whose re-plan is identical writes again and is applied")]
    public async Task Mismatch_IdenticalReplan_Writes()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.BeforeWrite = attempt => { if (attempt == 1) host.World.BumpVersion(MatterId); };

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.WriteAttempts.Should().Be(2);
        host.World.Writes.Should().ContainSingle();
        host.World.SlotsOf(MatterId).Should().Be((FirmA, null));
    }

    [Fact(DisplayName = "095g concurrency: a version mismatch whose re-plan differs is 409 record_changed and writes nothing")]
    public async Task Mismatch_DifferentReplan_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.BeforeWrite = attempt => { if (attempt == 1) host.World.SetSlots(MatterId, FirmB, null); };

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await Json(response);
        json["errorCode"]!.GetValue<string>().Should().Be(CoreRecordFirmCodes.RecordChanged);
        json["assigned"]!.AsArray().Select(a => a!["firmId"]!.GetValue<Guid>()).Should().Equal(FirmB);
        host.World.Writes.Should().BeEmpty();
        host.World.Harness.Store.RootReads.Should().Be(0);
    }

    [Fact(DisplayName = "095g concurrency: a second mismatch is 409 record_changed")]
    public async Task SecondMismatch_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.BeforeWrite = _ => host.World.BumpVersion(MatterId);

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.RecordChanged);
        host.World.WriteAttempts.Should().Be(2);
        host.World.Writes.Should().BeEmpty();
        host.World.Harness.Store.RootReads.Should().Be(0, "no access pass runs on a refusal");
    }

    [Fact(DisplayName = "095g concurrency: a concurrent writer that already added the same firm makes the re-plan a no-op (200, changed false)")]
    public async Task Mismatch_ReplanIsANoOp_IsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.BeforeWrite = attempt => { if (attempt == 1) host.World.SetSlots(MatterId, FirmA, null); };

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["changed"]!.GetValue<bool>().Should().BeFalse();
        host.World.WriteAttempts.Should().Be(1);
    }

    [Fact(DisplayName = "095g concurrency (review 1 F1): a record made Restricted between the check and the write refuses the add on the re-plan (409 record_restricted, nothing written)")]
    public async Task Mismatch_RecordBecameRestricted_Is409()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.BeforeWrite = attempt =>
        {
            if (attempt == 1)
            {
                // Another writer restricts the record: its flags change and its version moves, its slots do not.
                host.World.Harness.Participations.Flags[MatterId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
                host.World.BumpVersion(MatterId);
            }
        };

        var response = await host.AssignAsync(new { add = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be(CoreRecordFirmCodes.RecordRestricted);
        host.World.WriteAttempts.Should().Be(1, "the second write is never attempted");
        host.World.Writes.Should().BeEmpty();
        host.World.SlotsOf(MatterId).Should().Be((null, null));
        host.World.Harness.Store.RootReads.Should().Be(0, "no access pass runs");
    }

    [Fact(DisplayName = "095g concurrency (review 1 F1) control: a clear on a record made Restricted between the check and the write is still applied (a revoke is never blocked by flags)")]
    public async Task Mismatch_RecordBecameRestricted_ClearIsApplied()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Matter(MatterId, slot1: FirmA);
        host.World.BeforeWrite = attempt =>
        {
            if (attempt == 1)
            {
                host.World.Harness.Participations.Flags[MatterId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
                host.World.BumpVersion(MatterId);
            }
        };

        var response = await host.AssignAsync(new { remove = FirmA, confirmRevoke = FirmA });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.WriteAttempts.Should().Be(2);
        host.World.SlotsOf(MatterId).Should().Be((null, null));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /api/finance/firms
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095g list: the active law firms only, sorted by name then id, with paging")]
    public async Task List_ActiveLawFirms_Sorted_Paged()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();

        var first = await Json(await host.ListRawAsync("pageSize=2"));
        var second = await Json(await host.ListRawAsync("pageSize=2&page=2"));

        Names(first).Should().Equal("Firm A", "Firm B");
        first["page"]!.GetValue<int>().Should().Be(1);
        first["hasMore"]!.GetValue<bool>().Should().BeTrue();
        Names(second).Should().Equal("Firm C");
        second["hasMore"]!.GetValue<bool>().Should().BeFalse();
        var all = Names(await Json(await host.ListAsync("")));
        all.Should().NotContain("Firm D (inactive)").And.NotContain("Untyped Org").And.NotContain("Other Type Org");
        host.Probe.Calls.Should().HaveCount(3).And.OnlyContain(c => c.Privilege == CoreRecordFirmEndpoints.ReadOrganizationPrivilege && c.HasToken);
    }

    [Fact(DisplayName = "095g list (review 1 F2): the list keeps the server's order; it is never re-sorted with another collation")]
    public async Task List_KeepsTheServersOrder()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.ServerCollation = StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, ignoreCase: true);
        host.World.Firm(Guid.Parse("095f0000-0000-4000-8000-000000000a01"), "Étude Law");
        host.World.Firm(Guid.Parse("095f0000-0000-4000-8000-000000000a02"), "Zeta Law");

        var all = Names(await Json(await host.ListAsync("")));

        // A culture-aware collation puts the accented E with the other E's; an ordinal one puts it after Z.
        all.Should().Equal("Étude Law", "Firm A", "Firm B", "Firm C", "Zeta Law");
    }

    [Fact(DisplayName = "095g list: the search escapes %, _ and [ so they match literally")]
    public async Task List_SearchEscapesWildcards()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.Firm(Guid.NewGuid(), "50% Partners");
        host.World.Firm(Guid.NewGuid(), "500 Partners");
        host.World.Firm(Guid.NewGuid(), "A_B Law");
        host.World.Firm(Guid.NewGuid(), "AxB Law");
        host.World.Firm(Guid.NewGuid(), "[Bracket] LLP");
        host.World.Firm(Guid.NewGuid(), "B LLP");

        Names(await Json(await host.ListAsync("50%"))).Should().Equal("50% Partners");
        Names(await Json(await host.ListAsync("A_B"))).Should().Equal("A_B Law");
        Names(await Json(await host.ListAsync("[B"))).Should().Equal("[Bracket] LLP");
    }

    [Theory(DisplayName = "095g list: N1, N2 and no table Read give the same uniform 404")]
    [InlineData("N1")]
    [InlineData("N2")]
    [InlineData("no Read")]
    public async Task List_Refusals_AreTheUniform404(string which)
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        switch (which)
        {
            case "N1": host.StepZero.Kind = StepZeroCallerKind.FlagTrue; break;
            case "N2": host.StepZero.Kind = StepZeroCallerKind.NoSystemUserRow; break;
            default: host.Probe.Release(CoreRecordFirmEndpoints.ReadOrganizationPrivilege); break;
        }

        var response = await host.ListAsync("");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StableBody(response)).Should().Be(await UniformBodyAsync());
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095g list: a privilege-read fault is 503, never the uniform 404 and never []")]
    public async Task List_PrivilegeFault_Is503()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.Probe.FaultFor.Add(CoreRecordFirmEndpoints.ReadOrganizationPrivilege);

        var response = await host.ListAsync("");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be(Unavailable);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095g list: a read fault is 503, never []")]
    public async Task List_ReadFault_Is503()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.World.FaultQueriesOf.Add("sprk_organization");

        var response = await host.ListAsync("");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be(Unavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("items");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Pins
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "095g design 3e: the 095a by-email route still declares Privilege checks only, so the Record fault branch does not change it")]
    public void ByEmailRoute_DeclaresPrivilegeChecksOnly()
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?internetMessageId=" + Uri.EscapeDataString("<abc.123@mail.example.com>"));

        var targets = InvoiceSubmissionEndpoints.ResolveByEmailTargets(new DefaultEndpointFilterInvocationContext(http));

        targets.Rejection.Should().BeNull();
        targets.Checks.Should().NotBeEmpty().And.OnlyContain(c => c.Path == FinanceCheckPath.Privilege);
    }

    [Fact(DisplayName = "095g (EB-D-234): the seeding script's type code equals LawFirmOrganizationType.Code")]
    public void SeedingScript_UsesTheServersCode()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingLawFirmTypeCode.ps1"));

        var match = Regex.Match(script, @"^\$LawFirmTypeCode = '(?<code>[^']+)'\s*$", RegexOptions.Multiline);

        match.Success.Should().BeTrue("the script states the code on one line");
        match.Groups["code"].Value.Should().Be(LawFirmOrganizationType.Code);
    }

    [Fact(DisplayName = "095g review 1 (F1, script): before it writes, the seeding script looks for an INACTIVE type row holding the code and stops with an escalation")]
    public void SeedingScript_EscalatesOnAnInactiveRowHoldingTheCode()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingLawFirmTypeCode.ps1"));

        // The code is unique across all type rows, so an inactive holder would fail the write on the duplicate key.
        var lookup = Regex.Match(
            script,
            @"^\s*\$(?<var>\w+) = Get-TypeRows ""\$CodeColumn eq '\$\(Format-OData \$LawFirmTypeCode\)' and statecode ne 0""\s*$",
            RegexOptions.Multiline);
        lookup.Success.Should().BeTrue("the script reads the inactive type rows that carry the code");

        var escalation = Regex.Match(
            script.Substring(lookup.Index),
            @"if \(\$" + lookup.Groups["var"].Value + @"\.Count -gt 0\) \{[\s\S]{0,400}?throw \[EscalationException\]");
        escalation.Success.Should().BeTrue("finding one stops the script with an escalation (exit 2, nothing written)");

        var patch = script.IndexOf("-Method PATCH", StringComparison.Ordinal);
        patch.Should().BeGreaterThan(lookup.Index + escalation.Index, "the check comes before the write");
    }

    [Fact(DisplayName = "095g: the two operation keys - an add needs Read, Write and Append, a clear Read and Write")]
    public void OperationKeys_StateR1()
    {
        OperationAccessPolicy.HasRequiredRights(AccessRights.Write, "ebilling.record.assign_firm").Should().BeFalse();
        OperationAccessPolicy.HasRequiredRights(AccessRights.Write | AccessRights.Append, "ebilling.record.assign_firm").Should().BeFalse("Read is asked too");
        OperationAccessPolicy.HasRequiredRights(AccessRights.Read | AccessRights.Write | AccessRights.Append, "ebilling.record.assign_firm").Should().BeTrue();
        OperationAccessPolicy.HasRequiredRights(AccessRights.Write, "ebilling.record.remove_firm").Should().BeFalse("Read is asked too");
        OperationAccessPolicy.HasRequiredRights(AccessRights.Read | AccessRights.Write, "ebilling.record.remove_firm").Should().BeTrue();
        OperationAccessPolicy.HasRequiredRights(AccessRights.Read | AccessRights.Append, "ebilling.record.remove_firm").Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private static string[] Names(JsonNode page) => page["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()).ToArray();

    private static async Task<JsonNode> Json(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>();

    private static async Task<string?> ReasonCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]?.GetValue<string>();

    /// <summary>The body without the per-request ids (correlation and trace), so two 404s compare byte for byte.</summary>
    private static async Task<string> StableBody(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        body.Remove("correlationId");
        return body.ToJsonString();
    }

    private static async Task<string> UniformBodyAsync()
    {
        await using var host = await Host.StartAsync();
        host.GrantEverything();
        host.StepZero.Kind = StepZeroCallerKind.FlagTrue;
        return await StableBody(await host.AssignAsync(new { add = FirmA }));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root (.git) not found above " + AppContext.BaseDirectory);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The world: core records, organizations and type rows behind IGenericEntityService, over the assigned-access harness
    // ═══════════════════════════════════════════════════════════════════════════

    internal sealed class FirmWorld
    {
        private const string MatterTable = "sprk_matter";
        private const string ProjectTable = "sprk_project";
        private const string WorkAssignmentTable = "sprk_workassignment";
        private static readonly Guid MatterRecordType = Guid.Parse("095f0000-0000-4000-8000-000000000301");

        public FirmWorld()
        {
            Harness = new AssignedAccessTestDoubles.Harness();
            Filing = SecureChildShareWorld.Standard()
                .Add("sprk_recordtype_ref", MatterRecordType, ("sprk_recordlogicalname", MatterTable));
            Harness.Participations = new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None, FilingEntities());
            Harness.Grants.SystemUserIdsByOid[CallerOid] = CallerSystemUser;

            Types.Add(TypeRow(LawFirmType, LawFirmOrganizationType.Code, 0));
            Types.Add(TypeRow(OtherType, "VENDOR", 0));
            Firm(FirmA, "Firm A");
            Firm(FirmB, "Firm B");
            Firm(FirmC, "Firm C");
            Firm(InactiveFirm, "Firm D (inactive)", state: 1);
            Firm(UntypedOrganization, "Untyped Org", untyped: true);
            Firm(OtherTypeOrganization, "Other Type Org", type: OtherType);
            Matter(MatterId);

            Entities
                .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string table, Guid id, string[] _, CancellationToken _) => Retrieve(table, id));
            Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) => Query(query));
            Entities
                .Setup(e => e.UpdateIfRowVersionMatchesAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) => Update(table, id, version, fields));
        }

        public AssignedAccessTestDoubles.Harness Harness { get; }

        /// <summary>What the effective-flag walk reads (parents of a project or work assignment).</summary>
        public SecureChildShareWorld Filing { get; }

        public Mock<IGenericEntityService> Entities { get; } = new();

        public Dictionary<(string Table, Guid Id), Entity> Records { get; } = new();

        public Dictionary<Guid, Entity> Firms { get; } = new();

        public List<Entity> Types { get; } = new();

        /// <summary>Every slot write that landed.</summary>
        public List<(string Table, Guid Id, string Version, Dictionary<string, object> Fields)> Writes { get; } = new();

        /// <summary>Every write attempted (a mismatch included).</summary>
        public int WriteAttempts { get; private set; }

        /// <summary>Runs before each write attempt with its 1-based ordinal (a concurrent writer).</summary>
        public Action<int>? BeforeWrite { get; set; }

        public bool FaultWrites { get; set; }

        public HashSet<string> FaultRowReadsOf { get; } = new();

        public HashSet<string> FaultQueriesOf { get; } = new();

        /// <summary>The collation the server orders names with (Dataverse's is culture-aware, not ordinal).</summary>
        public StringComparer ServerCollation { get; set; } = StringComparer.OrdinalIgnoreCase;

        public CoreRecordFirmAssignmentService Service() => new(
            Entities.Object, Harness.Participations, Harness.AccessibleRecords, Harness.Materializer,
            NullLogger<CoreRecordFirmAssignmentService>.Instance);

        public void Firm(Guid id, string name, int state = 0, Guid? type = null, bool untyped = false)
        {
            var row = new Entity(CoreRecordFirmAssignmentService.OrganizationEntity, id)
            {
                [CoreRecordFirmAssignmentService.OrganizationIdColumn] = id,
                [CoreRecordFirmAssignmentService.OrganizationNameColumn] = name,
                ["statecode"] = new OptionSetValue(state),
            };
            Guid? typeId = untyped ? null : type ?? LawFirmType;
            if (typeId is { } t)
            {
                row[LawFirmOrganizationType.OrganizationTypeColumn] = new EntityReference(LawFirmOrganizationType.TypeEntity, t);
            }

            Firms[id] = row;
        }

        /// <summary>none: no type row carries the code; two: two active rows do; inactive: the one row carrying it is inactive.</summary>
        public void ShapeTypes(string shape)
        {
            Types.Clear();
            switch (shape)
            {
                case "two":
                    Types.Add(TypeRow(LawFirmType, LawFirmOrganizationType.Code, 0));
                    Types.Add(TypeRow(OtherType, LawFirmOrganizationType.Code, 0));
                    break;
                case "inactive":
                    Types.Add(TypeRow(LawFirmType, LawFirmOrganizationType.Code, 1));
                    break;
                default:
                    Types.Add(TypeRow(LawFirmType, null, 0));
                    break;
            }
        }

        public void Matter(Guid id, Guid? slot1 = null, Guid? slot2 = null, RootRecordFlags? flags = null, bool active = true)
        {
            Record(MatterTable, ExternalGrantRootType.Matter, id, slot1, slot2, flags ?? RootRecordFlags.None, active);
            Filing.Add(MatterTable, id, ("sprk_issecure", (flags ?? RootRecordFlags.None).IsSecure));
        }

        /// <summary>A matter that is a parent only (its flags in the filing world and the flag reads).</summary>
        public void FiledMatter(Guid id, bool secure, int? permission = null)
        {
            var columns = new List<(string, object)> { ("sprk_issecure", secure), ("sprk_mattername", "Parent") };
            if (permission is { } p)
            {
                columns.Add(("sprk_accesspermission", new OptionSetValue(p)));
            }

            Filing.Add(MatterTable, id, columns.ToArray());
            Harness.Participations.Flags[id] = new RootRecordFlags(
                secure, permission == ExternalParticipationService.AccessPermissionRestricted, permission == ExternalParticipationService.AccessPermissionLimited);
        }

        public void WorkAssignment(Guid id, Guid parentMatter)
        {
            Record(WorkAssignmentTable, ExternalGrantRootType.WorkAssignment, id, null, null, RootRecordFlags.None, active: true);
            Filing.Add(WorkAssignmentTable, id, ("sprk_issecure", false), ("sprk_regardingmatter", new EntityReference(MatterTable, parentMatter)));
        }

        public void Project(Guid id, Guid parentMatter)
        {
            Record(ProjectTable, ExternalGrantRootType.Project, id, null, null, RootRecordFlags.None, active: true);
            Filing.Add(ProjectTable, id,
                ("sprk_issecure", false),
                ("sprk_projectname", "Middle"),
                ("sprk_regardingrecordid", parentMatter.ToString("D")),
                ("sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", MatterRecordType)));
        }

        public (Guid? Slot1, Guid? Slot2) SlotsOf(Guid id)
        {
            var row = Records.Single(r => r.Key.Id == id).Value;
            return (row.GetAttributeValue<EntityReference>(CoreRecordFirmAssignmentService.Slot1Column)?.Id,
                row.GetAttributeValue<EntityReference>(CoreRecordFirmAssignmentService.Slot2Column)?.Id);
        }

        /// <summary>Another writer changed a column this route does not write: the version moves, the slots do not.</summary>
        public void BumpVersion(Guid id)
        {
            var row = Records.Single(r => r.Key.Id == id).Value;
            row[CoreRecordFirmAssignmentService.VersionColumn] = row.GetAttributeValue<long>(CoreRecordFirmAssignmentService.VersionColumn) + 1;
        }

        /// <summary>Another writer set the slots.</summary>
        public void SetSlots(Guid id, Guid? slot1, Guid? slot2)
        {
            var (key, row) = Records.Single(r => r.Key.Id == id);
            Apply(key.Table, id, row, new Dictionary<string, object>
            {
                [CoreRecordFirmAssignmentService.Slot1Column] = slot1 is { } a ? new EntityReference(CoreRecordFirmAssignmentService.OrganizationEntity, a) : DBNull.Value,
                [CoreRecordFirmAssignmentService.Slot2Column] = slot2 is { } b ? new EntityReference(CoreRecordFirmAssignmentService.OrganizationEntity, b) : DBNull.Value,
            });
            BumpVersion(id);
        }

        private void Record(string table, ExternalGrantRootType rootType, Guid id, Guid? slot1, Guid? slot2, RootRecordFlags flags, bool active)
        {
            var row = new Entity(table, id)
            {
                [CoreRecordFirmAssignmentService.VersionColumn] = 1000L,
                ["statecode"] = new OptionSetValue(active ? 0 : 1),
            };
            Records[(table, id)] = row;
            Harness.Store.Root(rootType, id);
            Harness.Participations.Flags[id] = flags;
            Apply(table, id, row, new Dictionary<string, object>
            {
                [CoreRecordFirmAssignmentService.Slot1Column] = slot1 is { } a ? new EntityReference(CoreRecordFirmAssignmentService.OrganizationEntity, a) : DBNull.Value,
                [CoreRecordFirmAssignmentService.Slot2Column] = slot2 is { } b ? new EntityReference(CoreRecordFirmAssignmentService.OrganizationEntity, b) : DBNull.Value,
            });
        }

        /// <summary>Sets the slots on the row and mirrors them where the access pass and the firm list read them.</summary>
        private void Apply(string table, Guid id, Entity row, Dictionary<string, object> fields)
        {
            var rootType = table switch
            {
                MatterTable => ExternalGrantRootType.Matter,
                ProjectTable => ExternalGrantRootType.Project,
                _ => ExternalGrantRootType.WorkAssignment,
            };
            foreach (var (column, value) in fields)
            {
                var firm = value is EntityReference reference ? reference.Id : (Guid?)null;
                if (firm is null)
                {
                    row.Attributes.Remove(column);
                }
                else
                {
                    row[column] = value;
                }

                Harness.Store.Assign(rootType, id, column, firm);
            }

            Harness.Participations.RecordOrganizations[id] = new[]
                {
                    row.GetAttributeValue<EntityReference>(CoreRecordFirmAssignmentService.Slot1Column)?.Id,
                    row.GetAttributeValue<EntityReference>(CoreRecordFirmAssignmentService.Slot2Column)?.Id,
                }
                .OfType<Guid>().Distinct().ToArray();
        }

        private static Entity TypeRow(Guid id, string? code, int state)
        {
            var row = new Entity(LawFirmOrganizationType.TypeEntity, id) { ["statecode"] = new OptionSetValue(state) };
            if (code is not null)
            {
                row[LawFirmOrganizationType.TypeCodeColumn] = code;
            }

            return row;
        }

        private IGenericEntityService FilingEntities()
        {
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(Filing.Answer(query)));
            return entities.Object;
        }

        private Task<Entity> Retrieve(string table, Guid id)
        {
            if (FaultRowReadsOf.Contains(table))
            {
                return Task.FromException<Entity>(new HttpRequestException($"{table} row read failed"));
            }

            Entity? row = table == CoreRecordFirmAssignmentService.OrganizationEntity
                ? Firms.GetValueOrDefault(id)
                : Records.GetValueOrDefault((table, id));
            if (row is null)
            {
                return Task.FromException<Entity>(new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = -2147220969, Message = $"{table} With Id = {id} Does Not Exist" }));
            }

            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var attribute in row.Attributes)
            {
                copy[attribute.Key] = attribute.Value;
            }

            return Task.FromResult(copy);
        }

        private Task<EntityCollection> Query(QueryExpression query)
        {
            if (FaultQueriesOf.Contains(query.EntityName))
            {
                return Task.FromException<EntityCollection>(new HttpRequestException($"{query.EntityName} query failed"));
            }

            IEnumerable<Entity> rows = query.EntityName switch
            {
                LawFirmOrganizationType.TypeEntity => Types,
                CoreRecordFirmAssignmentService.OrganizationEntity => Firms.Values,
                _ => throw new InvalidOperationException($"unexpected query of {query.EntityName}"),
            };

            var matched = rows.Where(r => query.Criteria.Conditions.All(c => Matches(r, c))).ToList();
            if (query.Orders.Count > 0)
            {
                matched = matched
                    .OrderBy(r => r.GetAttributeValue<string>(CoreRecordFirmAssignmentService.OrganizationNameColumn), ServerCollation)
                    .ThenBy(r => r.Id)
                    .ToList();
            }

            if (query.TopCount is { } top)
            {
                matched = matched.Take(top).ToList();
            }

            var more = false;
            if (query.PageInfo is { Count: > 0 } paging)
            {
                var page = Math.Max(1, paging.PageNumber);
                more = matched.Count > page * paging.Count;
                matched = matched.Skip((page - 1) * paging.Count).Take(paging.Count).ToList();
            }

            return Task.FromResult(new EntityCollection(matched) { MoreRecords = more });
        }

        private static bool Matches(Entity row, ConditionExpression condition)
        {
            var value = row.Attributes.TryGetValue(condition.AttributeName, out var raw) ? raw : null;
            if (condition.AttributeName == row.LogicalName + "id")
            {
                value = row.Id;
            }

            object? Plain(object? v) => v switch
            {
                EntityReference r => r.Id,
                OptionSetValue o => o.Value,
                _ => v,
            };

            var actual = Plain(value);
            return condition.Operator switch
            {
                ConditionOperator.Equal => Equals(actual, condition.Values[0]),
                ConditionOperator.In => condition.Values.Any(v => Equals(actual, v)),
                ConditionOperator.Like => actual is string s && LikeMatches(s, (string)condition.Values[0]),
                _ => throw new InvalidOperationException($"unexpected operator {condition.Operator}"),
            };
        }

        /// <summary>T-SQL LIKE, case-insensitive: % any run, _ one character, [x] the literal x.</summary>
        private static bool LikeMatches(string text, string pattern)
        {
            var regex = new System.Text.StringBuilder("^");
            for (var i = 0; i < pattern.Length; i++)
            {
                var ch = pattern[i];
                if (ch == '%')
                {
                    regex.Append(".*");
                }
                else if (ch == '_')
                {
                    regex.Append('.');
                }
                else if (ch == '[')
                {
                    var close = pattern.IndexOf(']', i + 2);
                    regex.Append('[').Append(Regex.Escape(pattern.Substring(i + 1, close - i - 1))).Append(']');
                    i = close;
                }
                else
                {
                    regex.Append(Regex.Escape(ch.ToString()));
                }
            }

            return Regex.IsMatch(text, regex.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private Task Update(string table, Guid id, string version, Dictionary<string, object> fields)
        {
            WriteAttempts++;
            BeforeWrite?.Invoke(WriteAttempts);
            if (FaultWrites)
            {
                return Task.FromException(new HttpRequestException("write failed"));
            }

            var row = Records[(table, id)];
            var current = row.GetAttributeValue<long>(CoreRecordFirmAssignmentService.VersionColumn).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (current != version)
            {
                return Task.FromException(new DBConcurrencyException("The row version does not match."));
            }

            Apply(table, id, row, fields);
            BumpVersion(id);
            Writes.Add((table, id, version, new Dictionary<string, object>(fields)));
            return Task.CompletedTask;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Host
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public StatedCaller StepZero { get; private set; } = null!;
        public FirmWorld World { get; } = new();
        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public FinAuth.RecordingPrivilegeProbe Probe { get; } = new();

        public static async Task<Host> StartAsync(bool ebillingEnabled = true)
        {
            var host = new Host();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(FinanceAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(o => o.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("test")));
            host.StepZero = StatedCaller.Register(builder.Services);
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);

            // The REAL authorization stack, substituted only at the access-data boundary.
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);

            // The REAL service over the world.
            builder.Services.AddScoped(_ => host.World.Service());

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseRateLimiter();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapCoreRecordFirmEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        /// <summary>The caller holds every right the routes ask: Write and Append on the matter, Read and AppendTo on every firm, Read on organizations.</summary>
        public void GrantEverything()
        {
            Access.Grant(MatterSet, MatterId, AccessRights.Read | AccessRights.Write | AccessRights.Append);
            foreach (var firm in World.Firms.Keys)
            {
                Access.Grant(FirmSet, firm, AccessRights.Read | AccessRights.AppendTo);
            }

            Probe.Hold(CoreRecordFirmEndpoints.ReadOrganizationPrivilege);
        }

        public void ShouldHaveWrittenNothing(string because = "")
        {
            World.Writes.Should().BeEmpty(because);
            World.Harness.Store.RootReads.Should().Be(0, "no access pass ran " + because);
            World.Harness.Grants.WriteCount.Should().Be(0, because);
        }

        /// <summary>A refusal: no slot write, no access pass, no grant write.</summary>
        public void ShouldHaveDoneNothing() => ShouldHaveWrittenNothing();

        public Task<HttpResponseMessage> AssignAsync(object body, bool authenticated = true, string entityType = "Matter", Guid? recordId = null) =>
            Send(HttpMethod.Post, $"/api/finance/core-records/{entityType}/{recordId ?? MatterId}/firms", JsonContent.Create(body), authenticated);

        public Task<HttpResponseMessage> AssignRawAsync(string entityType, Guid recordId, string body) =>
            Send(HttpMethod.Post, $"/api/finance/core-records/{entityType}/{recordId}/firms",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"), authenticated: true);

        public Task<HttpResponseMessage> ListAsync(string search, bool authenticated = true) =>
            Send(HttpMethod.Get, "/api/finance/firms?search=" + Uri.EscapeDataString(search), null, authenticated);

        public Task<HttpResponseMessage> ListRawAsync(string query) => Send(HttpMethod.Get, "/api/finance/firms?" + query, null, authenticated: true);

        private Task<HttpResponseMessage> Send(HttpMethod method, string url, HttpContent? content, bool authenticated)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
                request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
            }

            return _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
