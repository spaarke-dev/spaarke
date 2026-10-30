using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.DataMutation.Ebilling;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// Who may export invoices to AP (ebilling-r1 task 150; owner decision EB-D-178, ADR-008, ADR-019): Create on <c>sprk_billingexport</c> AND
/// <c>ebilling.invoice.decide</c> (Write) AND Read (D-85) on EVERY invoice of the batch. Any failure refuses the whole batch before anything is written, names no
/// other invoice, and a rights read that cannot be answered refuses too (fail closed).
/// </summary>
/// <remarks>
/// <para>Part 1 drives the seam (<see cref="ApExportAuthorization.AuthorizeExportAsync"/>) and the service over the in-memory world, so each refusal is
/// shown to leave the stored state untouched. Part 2 drives the REAL <c>MapApExportEndpoints</c>, <c>FinanceAuthorizationFilter</c>,
/// <c>AuthorizationService</c> and eBilling kill switch over the same world.</para>
/// <para><b>Seeding proof (task 150).</b> <c>MissingDecideOnOneInvoice_RefusesTheWholeBatch_NothingWritten</c> and
/// <c>ReadAloneIsNotTheDecideRight</c> fail when the per-invoice scan in <c>AuthorizeExportAsync</c> is removed; <c>NoCreatePrivilege_Is_Refused_WithoutAskingForInvoiceRights</c>
/// and the route 403 fail when the Create check is removed. Recorded in <c>projects/ebilling-r1/notes/150-ap-export-batch.md</c>.</para>
/// </remarks>
public sealed class ApExportAuthorizationTests
{
    private const string InvoiceSet = "sprk_invoices";

    // =========================================================================================
    // The seam and the service
    // =========================================================================================

    [Fact(DisplayName = "150 EB-D-178: a caller with Create on the batch table and decide on every invoice is allowed, and Dataverse is asked as the caller about each invoice")]
    public async Task Authorized_IsAllowed()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();

        var outcome = await world.Authorization.AuthorizeExportAsync(world.Caller, [a.Id, b.Id], CancellationToken.None);

        outcome.Should().Be(ApExportAuthorizationOutcome.Allowed);
        world.Probe.Calls.Should().ContainSingle().Which.Should().Be(new FinAuth.PrivilegeCall(ApExportAuthorization.CreateBatchPrivilege, HasToken: true));
        // Two rights per invoice (the decide right = Write, then Read), each asked of Dataverse as the caller.
        world.Access.Calls.Should().BeEquivalentTo([
            new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, a.Id, HasToken: true),
            new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, b.Id, HasToken: true),
            new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, a.Id, HasToken: true),
            new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, b.Id, HasToken: true)]);
    }

    [Fact(DisplayName = "150 D-85 review SEEDED: Write without Read on an invoice refuses the WHOLE batch (the files would carry values the caller cannot read), nothing is written, no invoice is named")]
    public async Task WriteWithoutRead_RefusesTheWholeBatch_NothingWritten()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        world.Access.Grant(InvoiceSet, b.Id, AccessRights.Write); // can approve it, cannot read it

        var outcome = await world.Authorization.AuthorizeExportAsync(world.Caller, [a.Id, b.Id], CancellationToken.None);
        var result = await world.ExportAsync(a, b);

        outcome.Should().Be(ApExportAuthorizationOutcome.NotPermitted);
        result.Outcome.Should().Be(ApExportOutcome.NotPermitted);
        result.Code.Should().Be(ApExportCodes.NotPermitted);
        result.Invoices.Should().BeNull();
        $"{result.Detail} {result.Code}".Should().NotContain(a.Id.ToString()).And.NotContain(b.Id.ToString());
        world.Dataverse.Log.Should().BeEmpty("refused before anything is read or written");
        world.Batches().Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        world.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.Approved);
        world.StatusOfInvoice(b.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "150 D-85 review: the candidates list leaves out an invoice the caller can write but not read")]
    public async Task WriteWithoutRead_IsNotACandidate()
    {
        var world = new ApExportWorld();
        var ok = world.AddInvoice("INV-OK");
        var writeOnly = world.AddInvoice("INV-WRITE-ONLY");
        world.GrantEverything();
        world.Access.Grant(InvoiceSet, writeOnly.Id, AccessRights.Write);

        var candidates = await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);

        candidates!.Candidates.Select(c => c.InvoiceId).Should().BeEquivalentTo([ok.Id]);
    }

    [Fact(DisplayName = "150 D-85 review: a Read answer that cannot be given (Write held, the lookup faulted) is 'undetermined' - not allowed - and the list shows nothing")]
    public async Task ReadAnswerFault_FailsClosed()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Access.Grant(InvoiceSet, a.Id, AccessRights.Write); // the decide right holds; Read does not, and Dataverse gave no answer for it
        world.Access.Fault(InvoiceSet, a.Id);

        var outcome = await world.Authorization.AuthorizeExportAsync(world.Caller, [a.Id], CancellationToken.None);
        var result = await world.ExportAsync(a);
        var candidates = await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);

        outcome.Should().Be(ApExportAuthorizationOutcome.Undetermined);
        result.Outcome.Should().Be(ApExportOutcome.AuthorizationUnavailable);
        candidates.Should().BeNull();
        world.Batches().Should().BeEmpty();
    }

    [Fact(DisplayName = "150 EB-D-178: no Create on the batch table is refused before any invoice right is asked, and nothing is written")]
    public async Task NoCreatePrivilege_Is_Refused_WithoutAskingForInvoiceRights()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Probe.Release(ApExportAuthorization.CreateBatchPrivilege);

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.NotPermitted);
        result.Code.Should().Be(ApExportCodes.NotPermitted);
        world.Access.Calls.Should().BeEmpty("the table right failed first; no invoice was asked about");
        world.Dataverse.Log.Should().BeEmpty("nothing was read or written for a caller who may not export");
        world.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "150 EB-D-178 SEEDED: missing decide on ONE of several invoices refuses the WHOLE batch with nothing written, and the answer names no invoice")]
    public async Task MissingDecideOnOneInvoice_RefusesTheWholeBatch_NothingWritten()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        var c = world.AddInvoice("INV-C");
        world.GrantEverything();
        world.Access.Grant(InvoiceSet, b.Id, AccessRights.Read); // can see it, cannot approve it

        var result = await world.ExportAsync(a, b, c);

        result.Outcome.Should().Be(ApExportOutcome.NotPermitted);
        result.Code.Should().Be(ApExportCodes.NotPermitted);
        world.Dataverse.Log.Should().BeEmpty("refused before anything is read or written");
        world.Batches().Should().BeEmpty();
        world.StoredFiles().Should().BeEmpty();
        foreach (var invoice in new[] { a, b, c })
        {
            world.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Approved);
            world.BatchOfInvoice(invoice.Id).Should().BeNull();
        }

        var answer = $"{result.Detail} {result.Code} {result.Invoices?.Count}";
        foreach (var invoice in new[] { a, b, c })
        {
            answer.Should().NotContain(invoice.Id.ToString());
        }

        result.Invoices.Should().BeNull("a refusal on rights carries no per-invoice list");
    }

    [Fact(DisplayName = "150 EB-D-178 SEEDED: Read alone is not the decide right (Write is)")]
    public async Task ReadAloneIsNotTheDecideRight()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Access.Grant(InvoiceSet, a.Id, AccessRights.Read);

        var outcome = await world.Authorization.AuthorizeExportAsync(world.Caller, [a.Id], CancellationToken.None);

        outcome.Should().Be(ApExportAuthorizationOutcome.NotPermitted);
    }

    [Fact(DisplayName = "150 EB-D-178: the decide right on a different invoice does not authorize this one")]
    public async Task RightOnAnotherInvoice_IsNotRightOnThisOne()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A", grant: false);
        world.Probe.Hold(ApExportAuthorization.CreateBatchPrivilege);
        world.Access.Grant(InvoiceSet, Guid.NewGuid(), AccessRights.Read | AccessRights.Write);

        var outcome = await world.Authorization.AuthorizeExportAsync(world.Caller, [a.Id], CancellationToken.None);

        outcome.Should().Be(ApExportAuthorizationOutcome.NotPermitted);
    }

    [Fact(DisplayName = "150 EB-D-178: a rights read that cannot be answered for one invoice is 'undetermined' - not allowed - and the service writes nothing (503)")]
    public async Task RightsReadFault_FailsClosed()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        world.Access.Grant(InvoiceSet, b.Id, AccessRights.None);
        world.Access.Fault(InvoiceSet, b.Id); // Dataverse gave no answer for b

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.AuthorizationUnavailable);
        result.Code.Should().Be(ApExportCodes.AuthorizationUnavailable);
        world.Dataverse.Log.Should().BeEmpty();
        world.Batches().Should().BeEmpty();
        world.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "150 EB-D-178: every rights read throwing is 'undetermined' (fail closed), never an allow")]
    public async Task EveryRightsReadThrowing_FailsClosed()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var outcome = await world.Authorization.AuthorizeExportAsync(world.Caller, [a.Id], CancellationToken.None);
        var result = await world.ExportAsync(a);

        outcome.Should().Be(ApExportAuthorizationOutcome.Undetermined);
        result.Outcome.Should().Be(ApExportOutcome.AuthorizationUnavailable);
        world.Dataverse.Log.Should().BeEmpty();
    }

    [Fact(DisplayName = "150 EB-D-178: a failed table-privilege question is 'undetermined' (fail closed), and no invoice right is asked")]
    public async Task PrivilegeProbeThrowing_FailsClosed()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Probe.ThrowOnEveryCall = new InvalidOperationException("OBO exchange failed");

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.AuthorizationUnavailable);
        world.Access.Calls.Should().BeEmpty();
        world.Dataverse.Log.Should().BeEmpty();
    }

    [Fact(DisplayName = "150 EB-D-178: a caller with no access token is refused (the rights are the caller's own, asked on-behalf-of)")]
    public async Task NoAccessToken_IsRefused()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        world.GrantEverything();

        var outcome = await world.Authorization.AuthorizeExportAsync(
            new InvoiceContextService.InvoiceCaller("caller-oid", null), [a.Id], CancellationToken.None);

        outcome.Should().Be(ApExportAuthorizationOutcome.NotPermitted);
        world.Access.Calls.Should().BeEmpty();
        world.Probe.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "150 EB-D-178: the candidates list shows nothing without Create on the batch table")]
    public async Task Candidates_NeedCreateOnTheBatchTable()
    {
        var world = new ApExportWorld();
        world.AddInvoice("INV-A");
        world.GrantEverything();
        world.Probe.Release(ApExportAuthorization.CreateBatchPrivilege);

        var candidates = await world.Service.ListCandidatesAsync(world.Caller, CancellationToken.None);

        candidates!.Candidates.Should().BeEmpty();
    }

    [Fact(DisplayName = "150 EB-D-178: the Create privilege this seam asks for is the one the Core User role grants (a rename of the table would silently refuse everyone)")]
    public void CreatePrivilege_IsGrantedByTheCoreUserRole()
    {
        var role = File.ReadAllText(Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Roles", "Spaarke Core User.xml"));

        role.Should().Contain($"name=\"{ApExportAuthorization.CreateBatchPrivilege}\"");
        ApExportAuthorization.CreateBatchPrivilege.Should().Be("prv" + "Create" + "sprk_billingexport");
        ApExportAuthorization.BatchEntitySet.Should().Be("sprk_billingexports");
        ApExportAuthorization.InvoiceDecideOperation.Should().Be("ebilling.invoice.decide");
    }

    // =========================================================================================
    // The routes
    // =========================================================================================

    [Theory(DisplayName = "150: both POST and candidates are 401 unauthenticated and touch nothing")]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task Unauthenticated_Is401(string method)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(method, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.World.Dataverse.Log.Should().BeEmpty();
        host.World.Access.Calls.Should().BeEmpty();
    }

    [Theory(DisplayName = "150: without Create on the batch table, POST and candidates are 403 before the handler runs; nothing is read or written")]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task WithoutCreate_Is403(string method)
    {
        await using var host = await Host.StartAsync(holdCreate: false);

        var response = await host.SendAsync(method);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.Dataverse.Log.Should().BeEmpty();
        host.World.Access.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "150: with Create but missing decide on one invoice, POST is 403 ebilling.export.not_permitted and nothing is written")]
    public async Task MissingDecide_Is403_NamingNoInvoice()
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        var b = host.World.AddInvoice("INV-B");
        host.World.GrantEverything();
        host.World.Access.Grant(InvoiceSet, b.Id, AccessRights.Read);

        var response = await host.SendAsync("POST", ids: [a.Id, b.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ApExportCodes.NotPermitted).And.NotContain(a.Id.ToString()).And.NotContain(b.Id.ToString());
        host.World.Dataverse.Log.Should().BeEmpty();
    }

    [Fact(DisplayName = "150 D-85 review: with Create and Write but no Read on one invoice, POST is 403 ebilling.export.not_permitted, names no invoice, and nothing is written")]
    public async Task WriteWithoutRead_Is403_NamingNoInvoice()
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        var b = host.World.AddInvoice("INV-B");
        host.World.GrantEverything();
        host.World.Access.Grant(InvoiceSet, b.Id, AccessRights.Write);

        var response = await host.SendAsync("POST", ids: [a.Id, b.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ApExportCodes.NotPermitted).And.NotContain(a.Id.ToString()).And.NotContain(b.Id.ToString());
        host.World.Dataverse.Log.Should().BeEmpty();
        host.World.Batches().Should().BeEmpty();
    }

    [Fact(DisplayName = "150: a rights read fault on POST is 503 ebilling.export.authorization_unavailable and nothing is written")]
    public async Task RightsFault_Is503()
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        host.World.GrantEverything();
        host.World.Access.ThrowOnEveryCall = new InvalidOperationException("down");

        var response = await host.SendAsync("POST", ids: [a.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>().Should().Be(ApExportCodes.AuthorizationUnavailable);
        host.World.Dataverse.Log.Should().BeEmpty();
        host.World.Batches().Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 099 must-fire: a flagged-external, row-less or unreadable caller is refused before any rights call, even holding every right")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_RefusesBeforeAnyRightsCall(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        host.World.GrantEverything();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync("POST", ids: [a.Id]);

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.World.Access.Calls.Should().BeEmpty();
        host.World.Probe.Calls.Should().BeEmpty();
        host.World.Dataverse.Log.Should().BeEmpty();
    }

    public static IEnumerable<object[]> RefusedKinds() => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "150 ADR-018: the eBilling kill switch is 503 ebilling.disabled for an authorized caller on POST and candidates, and reads nothing")]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task FlagOff_AuthorizedCaller_Gets503(string method)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        var a = host.World.AddInvoice("INV-A");
        host.World.GrantEverything();

        var response = await host.SendAsync(method, ids: [a.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.World.Dataverse.Log.Should().BeEmpty();
    }

    [Fact(DisplayName = "150 ADR-018: the kill switch never replaces authorization - an unauthorized caller still gets 403 when the module is off")]
    public async Task FlagOff_UnauthorizedCaller_StillGets403()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, holdCreate: false);

        var response = await host.SendAsync("POST");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "150: an authorized caller exports through the route - 201 with the batch and a Location, both files, invoices Sent to AP")]
    public async Task Authorized_Post_Is201()
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        var b = host.World.AddInvoice("INV-B");
        host.World.GrantEverything();

        var response = await host.SendAsync("POST", ids: [a.Id, b.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["status"]!.GetValue<string>().Should().Be("Completed");
        json["invoiceCount"]!.GetValue<int>().Should().Be(2);
        json["files"]!.AsArray().Should().HaveCount(2);
        response.Headers.Location!.ToString().Should().EndWith(json["batchId"]!.GetValue<string>());
        host.World.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.SentToAp);
        host.World.OwnerQuestions.Should().OnlyContain(q => q.RequestedBy != null && q.RequestedBy.SystemUserId == ApExportWorld.CallerSystemUser,
            "the file rows are owned through the acting user, resolved from the caller's token");
    }

    [Fact(DisplayName = "150 review F4: a batch whose total is below the batch total column's minimum is 400 ebilling.export.batch_total_out_of_range through the route, with nothing claimed or written")]
    public async Task BatchTotalOutOfRange_Is400_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        var credit = host.World.AddInvoice("INV-CREDIT", approved: -922_337_203_685_477.01m);
        host.World.GrantEverything();
        var writesBefore = host.World.Dataverse.Writes;

        var response = await host.SendAsync("POST", ids: [credit.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.BatchTotalOutOfRange);
        host.World.Dataverse.Writes.Should().Be(writesBefore);
        host.World.Batches().Should().BeEmpty();
        host.World.StatusOfInvoice(credit.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "150: a caller whose systemuser cannot be established is 403 ebilling.export.caller_unresolved and nothing is written")]
    public async Task UnresolvedCaller_Is403()
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        host.World.GrantEverything();
        host.World.Probe.CallerSystemUserId = null;

        var response = await host.SendAsync("POST", ids: [a.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>().Should().Be(ApExportCodes.CallerUnresolved);
        host.World.Dataverse.Log.Should().BeEmpty();
    }

    [Fact(DisplayName = "150: a selection that includes an invoice that is not Approved is 400 selection_refused with a reason per invoice")]
    public async Task NotApproved_Is400_WithReasons()
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        var b = host.World.AddInvoice("INV-B", status: InvoiceStatusCodes.InReview);
        host.World.GrantEverything();

        var response = await host.SendAsync("POST", ids: [a.Id, b.Id]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.SelectionRefused);
        var refusals = json["invoices"]!.AsArray();
        refusals.Should().ContainSingle();
        refusals[0]!["invoiceId"]!.GetValue<Guid>().Should().Be(b.Id);
        refusals[0]!["reason"]!.GetValue<string>().Should().Be(ApExportCodes.ReasonNotApproved);
        host.World.Dataverse.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "150: a batch read is 404 without Read on the batch (uniform), and 200 with it")]
    public async Task BatchRead_NeedsReadOnTheBatch()
    {
        await using var host = await Host.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        host.World.GrantEverything();
        var created = JsonNode.Parse(await (await host.SendAsync("POST", ids: [a.Id])).Content.ReadAsStringAsync())!;
        var batchId = created["batchId"]!.GetValue<Guid>();

        var denied = await host.SendAsync("GET", path: $"/api/finance/export-batches/{batchId}");
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Read);
        var allowed = await host.SendAsync("GET", path: $"/api/finance/export-batches/{batchId}");

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await allowed.Content.ReadAsStringAsync())!["files"]!.AsArray().Should().HaveCount(2);
    }

    [Fact(DisplayName = "150: the candidates route lists Approved, un-batched invoices the caller may export, as JSON")]
    public async Task Candidates_Lists()
    {
        await using var host = await Host.StartAsync();
        host.World.AddInvoice("INV-A");
        host.World.AddInvoice("INV-REVIEW", status: InvoiceStatusCodes.InReview);
        host.World.GrantEverything();

        var response = await host.SendAsync("GET");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["candidates"]!.AsArray().Should().ContainSingle()
            .Which!["firmInvoiceNumber"]!.GetValue<string>().Should().Be("INV-A");
    }

    // =========================================================================================
    // Host
    // =========================================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public StatedCaller StepZero { get; private set; } = null!;
        public ApExportWorld World { get; } = new();

        public static async Task<Host> StartAsync(bool ebillingEnabled = true, bool holdCreate = true)
        {
            var host = new Host();
            host.World.Probe.CallerSystemUserId = ApExportWorld.CallerSystemUser;
            if (holdCreate)
            {
                host.World.Probe.Hold(ApExportAuthorization.CreateBatchPrivilege);
            }

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
            host.StepZero = StatedCaller.Register(builder.Services);
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);

            // The REAL authorization stack, substituted only at the access-data boundary and the privilege probe (the same fakes the world uses).
            builder.Services.AddSingleton<IAccessDataSource>(host.World.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.World.Probe);

            // The REAL export service over the in-memory Dataverse and file store.
            builder.Services.AddScoped(_ => host.World.Service);

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapApExportEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public Task<HttpResponseMessage> SendAsync(string method, bool authenticated = true, IReadOnlyList<Guid>? ids = null, string? path = null)
        {
            var isPost = method == "POST";
            var url = path ?? (isPost ? "/api/finance/export-batches" : "/api/finance/export-batches/candidates");
            var request = new HttpRequestMessage(isPost ? HttpMethod.Post : HttpMethod.Get, url);
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
                request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
            }

            if (isPost)
            {
                var body = new JsonObject { ["invoiceIds"] = new JsonArray((ids ?? [Guid.NewGuid()]).Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()) };
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
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
