using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The per-record authorization of the eight finance + scorecard routes, exercised through the REAL
/// <c>MapFinanceEndpoints</c>, <c>MapFinanceRollupEndpoints</c> and <c>MapScorecardCalculatorEndpoints</c>
/// (unified-access-control-r2 task 130, defect C8).
/// </summary>
/// <remarks>
/// <para><b>What is real and what is substituted.</b> The endpoint mappers, <c>FinanceAuthorizationFilter</c>,
/// <see cref="AuthorizationService"/>, <see cref="OperationAccessRule"/>, <see cref="OperationAccessPolicy"/>,
/// <see cref="FinanceRollupService"/> and <see cref="ScorecardCalculatorService"/> are the production types.
/// The substitution is at the <see cref="IAccessDataSource"/> boundary (a recording fake that answers "what may
/// this caller do to record X of set S" and logs every question asked) and at the Dataverse service seams the
/// handlers sit on. No HTTP handler is mocked (ADR-038).</para>
/// <para><b>Why the finance recalculate 200 is not asserted here.</b> <see cref="FinanceRollupService"/> reads
/// through a concrete Dataverse <c>ServiceClient</c> unwrapped from <see cref="IDataverseService"/>, which no
/// in-process double can stand in for. The authorized finance route is therefore shown to REACH its handler
/// (which then fails on the unwrappable double, a 500 that a denial can never produce); its 200 is proven by
/// the task's manual live gate (b). The scorecard routes, whose reads go through an interface, are asserted
/// 200 end to end.</para>
/// </remarks>
public class FinanceEndpointsAuthorizationContractTests
{
    private const string Matters = "sprk_matters";             // live EntityDefinitions, 2026-09-30
    private const string Projects = "sprk_projects";           // live EntityDefinitions, 2026-09-30
    private const string Documents = "sprk_documents";         // live EntityDefinitions, 2026-09-30
    private const string VendorOrganizations = "sprk_organizations"; // live EntityDefinitions(LogicalName='sprk_organization'), 2026-09-30
    private const string CreateInvoicePrivilege = "prvCreatesprk_Invoice"; // live privileges(name), 2026-10-01

    // =========================================================================================
    // Filter reach — every one of the eight routes denies a caller the seam reports as None
    // =========================================================================================

    public static TheoryData<string> AllEightRoutes => new()
    {
        "summary", "search", "confirm", "reject",
        "finance-matter-recalculate", "finance-project-recalculate",
        "scorecard-matter-recalculate", "scorecard-project-recalculate",
    };

    [Theory]
    [MemberData(nameof(AllEightRoutes))]
    public async Task EveryRoute_CallerWithNoRights_IsDeniedBeforeAnyServiceRuns(string route)
    {
        await using var host = await FinanceAuthHost.StartAsync();

        var response = await host.SendAsync(BuildRequest(route, Guid.NewGuid()));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        host.Access.Calls.Should().NotBeEmpty("the route's own filter must have asked the access seam");
        host.VerifyNoServiceWasInvoked();
    }

    // =========================================================================================
    // Recalculate (finance + scorecard)
    // =========================================================================================

    public static TheoryData<string, string> RecalculateRoutes => new()
    {
        { "finance-matter-recalculate", Matters },
        { "finance-project-recalculate", Projects },
        { "scorecard-matter-recalculate", Matters },
        { "scorecard-project-recalculate", Projects },
    };

    [Theory]
    [MemberData(nameof(RecalculateRoutes))]
    public async Task Recalculate_CallerWithNoRights_Returns404AndTouchesNoDataverseSeam(string route, string expectedSet)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var parentId = Guid.NewGuid();

        var response = await host.SendAsync(BuildRequest(route, parentId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new AccessCall(AccessPath.Record, expectedSet, parentId, HasToken: true));
        host.VerifyNoServiceWasInvoked();
    }

    [Theory]
    [MemberData(nameof(RecalculateRoutes))]
    public async Task Recalculate_AbsentAndUnreadable_AreByteIdenticalAndNeverEchoTheId(string route, string expectedSet)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var absentId = Guid.NewGuid();
        var unreadableId = Guid.NewGuid();

        // Absent: Dataverse resolves no rights at all. Unreadable: the record exists and the caller holds
        // OTHER rights on it, just not Read. Different seam answers; the caller must not be able to tell.
        host.Access.Grant(expectedSet, unreadableId, AccessRights.AppendTo | AccessRights.Append);

        var absent = await host.SendAsync(BuildRequest(route, absentId));
        var unreadable = await host.SendAsync(BuildRequest(route, unreadableId));

        absent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unreadable.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var absentBody = await absent.Content.ReadAsStringAsync();
        var unreadableBody = await unreadable.Content.ReadAsStringAsync();

        Normalize(absentBody).Should().Be(Normalize(unreadableBody));
        absentBody.Should().NotContain(absentId.ToString()).And.NotContainEquivalentOf(absentId.ToString("N"));
        unreadableBody.Should().NotContain(unreadableId.ToString()).And.NotContainEquivalentOf(unreadableId.ToString("N"));

        var json = JsonNode.Parse(absentBody)!.AsObject();
        json["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        json["title"]!.GetValue<string>().Should().Be("Not Found");
    }

    [Theory]
    [InlineData("scorecard-matter-recalculate", Matters)]
    [InlineData("scorecard-project-recalculate", Projects)]
    public async Task Recalculate_RecordDeletedAfterTheCheck_GetsTheSameUniform404(string route, string expectedSet)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var deletedId = Guid.NewGuid();
        var absentId = Guid.NewGuid();
        host.Access.Grant(expectedSet, deletedId, AccessRights.Read);
        host.SetupScorecardReads();
        host.ScorecardWrites
            .Setup(w => w.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), deletedId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("record was not found."));

        var deleted = await host.SendAsync(BuildRequest(route, deletedId));
        var absent = await host.SendAsync(BuildRequest(route, absentId));

        deleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var deletedBody = await deleted.Content.ReadAsStringAsync();
        Normalize(deletedBody).Should().Be(Normalize(await absent.Content.ReadAsStringAsync()));
        deletedBody.Should().NotContain(deletedId.ToString());
    }

    [Theory]
    [InlineData("finance-matter-recalculate", Matters)]
    [InlineData("finance-project-recalculate", Projects)]
    public async Task Recalculate_FinanceRecordDeletedAfterTheCheck_GetsTheSameUniform404(string route, string expectedSet)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var deletedId = Guid.NewGuid();
        var absentId = Guid.NewGuid();
        host.Access.Grant(expectedSet, deletedId, AccessRights.Read);
        // The update-only rollup write refuses a parent deleted between the check and the write.
        host.Rollup.DeletedAfterCheck.Add(deletedId);

        var deleted = await host.SendAsync(BuildRequest(route, deletedId));
        var absent = await host.SendAsync(BuildRequest(route, absentId));

        deleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Rollup.Calls.Should().Equal(deletedId); // the check passed and the handler ran; absentId never got there
        var deletedBody = await deleted.Content.ReadAsStringAsync();
        Normalize(deletedBody).Should().Be(Normalize(await absent.Content.ReadAsStringAsync()));
        deletedBody.Should().NotContain(deletedId.ToString()).And.NotContainEquivalentOf(deletedId.ToString("N"));
    }

    [Theory]
    [InlineData("scorecard-matter-recalculate", Matters, "sprk_matter")]
    [InlineData("scorecard-project-recalculate", Projects, "sprk_project")]
    public async Task Recalculate_ScorecardReader_Gets200WithTheRecalculatedPayload(string route, string expectedSet, string logicalName)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var parentId = Guid.NewGuid();
        host.Access.Grant(expectedSet, parentId, AccessRights.Read);
        host.SetupScorecardReads();
        host.ScorecardWrites
            .Setup(w => w.UpdateExistingRecordFieldsAsync(
                logicalName, parentId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await host.SendAsync(BuildRequest(route, parentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json.Should().ContainKey("guidelineCurrent").And.ContainKey("budgetTrend");
        host.ScorecardWrites.Verify(w => w.UpdateExistingRecordFieldsAsync(
            logicalName, parentId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("finance-matter-recalculate", Matters)]
    [InlineData("finance-project-recalculate", Projects)]
    public async Task Recalculate_FinanceReader_ReachesTheRollupHandler(string route, string expectedSet)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var parentId = Guid.NewGuid();
        host.Access.Grant(expectedSet, parentId, AccessRights.Read);

        var response = await host.SendAsync(BuildRequest(route, parentId));

        // The filter let the request through: the handler ran FinanceRollupService, which cannot unwrap a
        // ServiceClient from the in-process double and fails with the handler's own 500. A denial is a 404.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("An error occurred while recalculating financial fields");
    }

    [Theory]
    [MemberData(nameof(RecalculateRoutes))]
    public async Task Recalculate_AuthenticatedButNoBearerToken_Returns404WithoutAskingDataverse(string route, string expectedSet)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var parentId = Guid.NewGuid();
        host.Access.Grant(expectedSet, parentId, AccessRights.Read); // would be allowed WITH a token

        var request = BuildRequest(route, parentId);
        request.Headers.Authorization = null; // authenticated by the test scheme, but no bearer token to forward

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Access.Calls.Should().BeEmpty("with no caller token the check fails closed without any app-only query");
        host.VerifyNoServiceWasInvoked();
    }

    [Theory]
    [MemberData(nameof(RecalculateRoutes))]
    public async Task Recalculate_AccessSeamThrows_Returns404(string route, string expectedSet)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        host.Access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess unavailable");

        var response = await host.SendAsync(BuildRequest(route, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Access.Calls.Should().ContainSingle().Which.Set.Should().Be(expectedSet);
        host.VerifyNoServiceWasInvoked();
    }

    // =========================================================================================
    // Summary
    // =========================================================================================

    [Fact]
    public async Task Summary_Reader_Gets200_AndTheRightsQueryTargetsSprkMatters()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var matterId = Guid.NewGuid();
        host.Access.Grant(Matters, matterId, AccessRights.Read);
        host.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceSummaryDto { MatterId = matterId, CurrentSpend = 10m });

        var response = await host.SendAsync(BuildRequest("summary", matterId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new AccessCall(AccessPath.Record, Matters, matterId, HasToken: true));
        host.Access.Calls.Should().NotContain(c => c.Set == Documents, "a matter id is never a document id");
    }

    [Fact]
    public async Task Summary_ReaderWithNoFinancialData_GetsTheDocumented404()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var matterId = Guid.NewGuid();
        host.Access.Grant(Matters, matterId, AccessRights.Read);
        host.Summary.Setup(s => s.GetSummaryAsync(matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinanceSummaryDto?)null);

        var response = await host.SendAsync(BuildRequest("summary", matterId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Financial Data Not Found");
    }

    [Fact]
    public async Task Summary_NonReader_Gets403WithReasonCode_AndTheServiceIsNotInvoked()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var matterId = Guid.NewGuid();
        host.Access.Grant(Matters, matterId, AccessRights.AppendTo); // rights, but not Read

        var response = await host.SendAsync(BuildRequest("summary", matterId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, "sdap.access.deny.insufficient_rights");
        host.Access.Calls.Should().ContainSingle().Which.Set.Should().Be(Matters);
        host.VerifyNoServiceWasInvoked();
    }

    // =========================================================================================
    // Search
    // =========================================================================================

    [Theory]
    [InlineData("/api/finance/invoices/search?query=fees")]
    [InlineData("/api/finance/invoices/search?query=fees&documentId=11111111-1111-1111-1111-111111111111")]
    [InlineData("/api/finance/invoices/search?query=fees&invoiceId=11111111-1111-1111-1111-111111111111")]
    public async Task Search_WithoutMatterId_Returns400_AndNoOtherIdAuthorizesAnything(string url)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        host.Access.Grant(Documents, Guid.Parse("11111111-1111-1111-1111-111111111111"), AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(Authenticated(HttpMethod.Get, url));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("matterId");
        host.Access.Calls.Should().BeEmpty();
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Search_MatterTheCallerCannotRead_Returns403()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var matterId = Guid.NewGuid();

        var response = await host.SendAsync(BuildRequest("search", matterId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new AccessCall(AccessPath.Record, Matters, matterId, HasToken: true));
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Search_ReadableMatter_RunsScopedToThatMatter()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var matterId = Guid.NewGuid();
        host.Access.Grant(Matters, matterId, AccessRights.Read);
        host.Search.Setup(s => s.SearchAsync("fees", matterId, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceSearchResponse());

        var response = await host.SendAsync(BuildRequest("search", matterId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Search.Verify(s => s.SearchAsync("fees", matterId, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================================
    // Confirm
    // =========================================================================================

    [Fact]
    public async Task Confirm_DocumentNotWritable_Returns403_AndTheReviewServiceIsNotInvoked()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.Access.Grant(Documents, body.DocumentId, AccessRights.Read | AccessRights.AppendTo);
        host.Access.Grant(Matters, body.MatterId, AccessRights.AppendTo);
        host.Access.Grant(VendorOrganizations, body.VendorOrgId, AccessRights.AppendTo);

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, "sdap.access.deny.insufficient_rights");
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new AccessCall(AccessPath.Document, Documents, body.DocumentId, HasToken: true));
        host.VerifyNoServiceWasInvoked();
    }

    public static TheoryData<string> ConfirmLinkTargets => new() { "document", "matter", "vendor" };

    /// <summary>
    /// The document HOLDS the invoice lookup (live <c>sprk_document.sprk_invoice</c>), so it needs Append; the
    /// matter and vendor are pointed AT by the invoice's own lookups, so they need AppendTo (owner G5).
    /// </summary>
    [Theory]
    [MemberData(nameof(ConfirmLinkTargets))]
    public async Task Confirm_MissingTheLinkRightOnOneLinkedRecord_Returns403_NamingThatIdAndSet(string missing)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.Probe.Hold(CreateInvoicePrivilege);

        var docRights = AccessRights.Read | AccessRights.Write | AccessRights.Append;
        var matterRights = AccessRights.Read | AccessRights.AppendTo;
        var vendorRights = AccessRights.Read | AccessRights.AppendTo;
        (string Set, Guid Id) expected;
        switch (missing)
        {
            case "document": docRights &= ~AccessRights.Append; expected = (Documents, body.DocumentId); break;
            case "matter": matterRights &= ~AccessRights.AppendTo; expected = (Matters, body.MatterId); break;
            default: vendorRights &= ~AccessRights.AppendTo; expected = (VendorOrganizations, body.VendorOrgId); break;
        }

        host.Access.Grant(Documents, body.DocumentId, docRights);
        host.Access.Grant(Matters, body.MatterId, matterRights);
        host.Access.Grant(VendorOrganizations, body.VendorOrgId, vendorRights);

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(new AccessCall(AccessPath.Record, expected.Set, expected.Id, HasToken: true));
        host.Access.Calls.Last().Should().Be(
            new AccessCall(AccessPath.Record, expected.Set, expected.Id, HasToken: true),
            "the denying question is the last one asked — the filter stops at the first deny");
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Confirm_DocumentWithAppendToButNotAppend_IsDenied_TheOldRightNoLongerSuffices()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);
        // AppendTo was the right asked of the document before the live schema showed the lookup is ON it.
        host.Access.Grant(Documents, body.DocumentId, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, "sdap.access.deny.insufficient_rights");
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Confirm_CallerWithoutCreateOnSprkInvoice_Returns403_AndNothingIsCreated()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);
        host.Probe.Release(CreateInvoicePrivilege); // every record right, but not the table privilege

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, "sdap.access.deny.insufficient_privilege");
        host.Probe.Calls.Should().ContainSingle()
            .Which.Should().Be(new PrivilegeCall(CreateInvoicePrivilege, HasToken: true),
                "the privilege is asked AS THE CALLER (their token forwarded), by its exact live name");
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Confirm_PrivilegeCheckThrows_IsDenied()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);
        host.Probe.ThrowOnEveryCall = new HttpRequestException("RetrieveUserSetOfPrivilegesByNames unavailable");

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, "sdap.access.error.system_failure");
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Confirm_NoPrivilegeProbeRegistered_IsDenied_NeverAllowed()
    {
        // A host whose container has no CallerRecordAccessProbe: the Privilege-path check cannot be asked, and a
        // check that cannot be asked must deny (fail closed) — every record right is granted, so only that guard
        // stands between this caller and an app-only invoice create.
        await using var host = await FinanceAuthHost.StartAsync(registerProbe: false);
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldCarryReasonCode(response, "sdap.access.error.system_failure");
        host.Access.Calls.Should().HaveCount(4, "every record check ran and passed; the privilege check denied");
        host.Probe.Calls.Should().BeEmpty("the probe is not registered, so it cannot have been asked");
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Confirm_DocumentLinkedToAnotherMattersInvoice_Is409_AndNeverNamesThatInvoice()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);
        var otherInvoice = Guid.NewGuid();
        // Even an exception that carried the other invoice (in its message and as InvoiceId) must not leak it.
        host.Review.Setup(r => r.ConfirmInvoiceAsync(It.IsAny<InvoiceReviewConfirmRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceReviewException(
                InvoiceReviewFailure.DocumentLinkedToAnotherInvoice, $"linked to invoice {otherInvoice}", otherInvoice));

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(otherInvoice.ToString()).And.NotContainEquivalentOf(otherInvoice.ToString("N"));
        var json = JsonNode.Parse(text)!.AsObject();
        json.Should().NotContainKey("invoiceId");
        json["reasonCode"]!.GetValue<string>().Should().Be("sdap.finance.invoice_review.document_linked_elsewhere");
    }

    public static TheoryData<InvoiceReviewFailure, HttpStatusCode, bool> ConfirmFailures => new()
    {
        { InvoiceReviewFailure.DocumentNotFound, HttpStatusCode.NotFound, false },
        { InvoiceReviewFailure.DocumentChangedConcurrently, HttpStatusCode.Conflict, false },
        { InvoiceReviewFailure.ReviewDecisionChanged, HttpStatusCode.Conflict, false },
        { InvoiceReviewFailure.OwnerTeamUnresolved, HttpStatusCode.Forbidden, false },
        { InvoiceReviewFailure.LinkFailed, HttpStatusCode.InternalServerError, false },
        { InvoiceReviewFailure.LinkFailedInvoiceNotRemoved, HttpStatusCode.InternalServerError, true },
        { InvoiceReviewFailure.CreateFailedInvoiceMayRemain, HttpStatusCode.InternalServerError, true },
        { InvoiceReviewFailure.StatusNotUpdated, HttpStatusCode.InternalServerError, true },
        { InvoiceReviewFailure.ExtractionNotQueued, HttpStatusCode.InternalServerError, true },
    };

    [Theory]
    [MemberData(nameof(ConfirmFailures))]
    public async Task Confirm_ServiceFailure_IsRenderedWithItsMessage_AndNamesTheInvoiceWhenOneExists(
        InvoiceReviewFailure failure, HttpStatusCode expectedStatus, bool namesInvoice)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);
        var invoiceId = Guid.NewGuid();
        host.Review.Setup(r => r.ConfirmInvoiceAsync(It.IsAny<InvoiceReviewConfirmRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceReviewException(failure, $"what happened (invoice {invoiceId})", namesInvoice ? invoiceId : null));

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(expectedStatus);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["detail"]!.GetValue<string>().Should().Contain("what happened");
        json["reasonCode"]!.GetValue<string>().Should().StartWith("sdap.finance.invoice_review.");
        if (namesInvoice)
        {
            json["invoiceId"]!.GetValue<string>().Should().Be(invoiceId.ToString());
        }
        else
        {
            json.Should().NotContainKey("invoiceId");
        }
    }

    [Fact]
    public async Task Confirm_AuthorizedBodyOnlyRequest_Returns202_AndAuthorizesEveryBodyId()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);
        host.Review.Setup(r => r.ConfirmInvoiceAsync(
                It.Is<InvoiceReviewConfirmRequest>(q => q.DocumentId == body.DocumentId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceReviewResult { InvoiceId = Guid.NewGuid(), JobId = Guid.NewGuid(), StatusUrl = "/x" });

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        host.Access.Calls.Should().Equal(
            new AccessCall(AccessPath.Document, Documents, body.DocumentId, HasToken: true),
            new AccessCall(AccessPath.Record, Documents, body.DocumentId, HasToken: true),
            new AccessCall(AccessPath.Record, Matters, body.MatterId, HasToken: true),
            new AccessCall(AccessPath.Record, VendorOrganizations, body.VendorOrgId, HasToken: true));
        host.Probe.Calls.Should().Equal(new PrivilegeCall(CreateInvoicePrivilege, HasToken: true));
    }

    [Fact]
    public async Task Confirm_LostTheRaceToAConcurrentConfirm_Is202_WithNoJobId_AndExtractionAlreadyQueued()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        host.GrantFullConfirm(body);
        var winnersInvoice = Guid.NewGuid();
        host.Review.Setup(r => r.ConfirmInvoiceAsync(
                It.Is<InvoiceReviewConfirmRequest>(q => q.DocumentId == body.DocumentId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceReviewResult { InvoiceId = winnersInvoice, ExtractionAlreadyQueued = true });

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().BeNull("no job of this request's exists to poll");
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["invoiceId"]!.GetValue<string>().Should().Be(winnersInvoice.ToString());
        json["jobId"].Should().BeNull("a job id is never fabricated");
        json["statusUrl"].Should().BeNull();
        json["extractionAlreadyQueued"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_QueryStringDocumentId_HasNoEffect_TheBodyIdIsAuthorized()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        var decoy = Guid.NewGuid();
        // The caller can write the DECOY, not the body's document. Before task 130, ?documentId=<decoy>
        // satisfied the filter while the handler confirmed the body's document.
        host.Access.Grant(Documents, decoy, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);

        var request = Confirm(body);
        request.RequestUri = new Uri($"/api/finance/invoice-review/confirm?documentId={decoy}", UriKind.Relative);

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().NotContain(c => c.Id == decoy);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(body.DocumentId);
        host.VerifyNoServiceWasInvoked();
    }

    public static TheoryData<string> ConfirmEmptyFields => new() { "documentId", "matterId", "vendorOrgId" };

    [Theory]
    [MemberData(nameof(ConfirmEmptyFields))]
    public async Task Confirm_EmptyBodyId_IsTheFieldLevel400_WithNoRightsQuery(string emptyField)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var body = ConfirmBody.New();
        body = emptyField switch
        {
            "documentId" => body with { DocumentId = Guid.Empty },
            "matterId" => body with { MatterId = Guid.Empty },
            _ => body with { VendorOrgId = Guid.Empty },
        };

        var response = await host.SendAsync(Confirm(body));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
        errors.Should().ContainKey(emptyField);
        host.Access.Calls.Should().BeEmpty("an empty id must not spend a Dataverse rights query");
        host.VerifyNoServiceWasInvoked();
    }

    // =========================================================================================
    // Reject
    // =========================================================================================

    [Fact]
    public async Task Reject_BodyDocumentIsAuthorizedAtWrite_AndAQueryIdCannotSubstitute()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        var decoy = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read); // body doc: Read only
        host.Access.Grant(Documents, decoy, AccessRights.Read | AccessRights.Write);

        var request = Authenticated(HttpMethod.Post, $"/api/finance/invoice-review/reject?documentId={decoy}");
        request.Content = JsonContent.Create(new { documentId });

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new AccessCall(AccessPath.Document, Documents, documentId, HasToken: true));
        host.VerifyNoServiceWasInvoked();
    }

    [Fact]
    public async Task Reject_WriterOfTheBodyDocument_IsAllowed()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);
        host.Review.Setup(r => r.RejectInvoiceAsync(
                It.Is<InvoiceReviewRejectRequest>(q => q.DocumentId == documentId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceReviewRejectResult { DocumentId = documentId });

        var request = Authenticated(HttpMethod.Post, "/api/finance/invoice-review/reject");
        request.Content = JsonContent.Create(new { documentId });

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reject_DocumentDeletedAfterTheCheck_Returns404WithReasonCode()
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);
        // The update-only status write refuses a document deleted between the check and the write.
        host.Review.Setup(r => r.RejectInvoiceAsync(
                It.Is<InvoiceReviewRejectRequest>(q => q.DocumentId == documentId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("sprk_document record was not found."));

        var request = Authenticated(HttpMethod.Post, "/api/finance/invoice-review/reject");
        request.Content = JsonContent.Create(new { documentId });

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["reasonCode"]!.GetValue<string>().Should().Be("sdap.finance.invoice_review.document_not_found");
        json["title"]!.GetValue<string>().Should().Be("Document Not Found");
    }

    public static TheoryData<InvoiceReviewFailure, string> RejectRefusals => new()
    {
        { InvoiceReviewFailure.DocumentLinkedToInvoice, "sdap.finance.invoice_review.document_linked_to_invoice" },
        { InvoiceReviewFailure.DocumentChangedConcurrently, "sdap.finance.invoice_review.document_changed" },
    };

    [Theory]
    [MemberData(nameof(RejectRefusals))]
    public async Task Reject_Refused_Is409_WithItsReasonCode_AndNoInvoiceId(InvoiceReviewFailure failure, string reasonCode)
    {
        await using var host = await FinanceAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);
        host.Review.Setup(r => r.RejectInvoiceAsync(
                It.Is<InvoiceReviewRejectRequest>(q => q.DocumentId == documentId), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceReviewException(failure, "refused; nothing was saved"));

        var request = Authenticated(HttpMethod.Post, "/api/finance/invoice-review/reject");
        request.Content = JsonContent.Create(new { documentId });

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["reasonCode"]!.GetValue<string>().Should().Be(reasonCode);
        json.Should().NotContainKey("invoiceId");
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static HttpRequestMessage BuildRequest(string route, Guid id) => route switch
    {
        "summary" => Authenticated(HttpMethod.Get, $"/api/finance/matters/{id}/summary"),
        "search" => Authenticated(HttpMethod.Get, $"/api/finance/invoices/search?query=fees&matterId={id}"),
        "confirm" => Confirm(new ConfirmBody(id, Guid.NewGuid(), Guid.NewGuid())),
        "reject" => WithJson(Authenticated(HttpMethod.Post, "/api/finance/invoice-review/reject"), new { documentId = id }),
        "finance-matter-recalculate" => Authenticated(HttpMethod.Post, $"/api/finance/matters/{id}/recalculate"),
        "finance-project-recalculate" => Authenticated(HttpMethod.Post, $"/api/finance/projects/{id}/recalculate"),
        "scorecard-matter-recalculate" => Authenticated(HttpMethod.Post, $"/api/matters/{id}/recalculate-grades"),
        "scorecard-project-recalculate" => Authenticated(HttpMethod.Post, $"/api/projects/{id}/recalculate-grades"),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
    };

    private static HttpRequestMessage Authenticated(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        return request;
    }

    private static HttpRequestMessage WithJson(HttpRequestMessage request, object body)
    {
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage Confirm(ConfirmBody body) =>
        WithJson(Authenticated(HttpMethod.Post, "/api/finance/invoice-review/confirm"),
            new { documentId = body.DocumentId, matterId = body.MatterId, vendorOrgId = body.VendorOrgId });

    private static async Task ShouldCarryReasonCode(HttpResponseMessage response, string reasonCode)
    {
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["reasonCode"]!.GetValue<string>().Should().Be(reasonCode);
    }

    /// <summary>The body with its correlation/trace identifiers removed — the only fields allowed to differ.</summary>
    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return node.ToJsonString();
    }

    internal sealed record ConfirmBody(Guid DocumentId, Guid MatterId, Guid VendorOrgId)
    {
        public static ConfirmBody New() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    internal enum AccessPath { Document, Record }

    internal sealed record AccessCall(AccessPath Path, string Set, Guid Id, bool HasToken);

    internal sealed record PrivilegeCall(string Privilege, bool HasToken);

    /// <summary>
    /// <see cref="CallerRecordAccessProbe"/> at its virtual privilege seam: answers from a held-privilege set and
    /// records every question. Like the real probe, a missing caller token answers "not held".
    /// </summary>
    internal sealed class RecordingPrivilegeProbe : CallerRecordAccessProbe
    {
        private readonly HashSet<string> _held = new(StringComparer.Ordinal);

        public RecordingPrivilegeProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public List<PrivilegeCall> Calls { get; } = new();

        public Exception? ThrowOnEveryCall { get; set; }

        public void Hold(string privilege) => _held.Add(privilege);

        public void Release(string privilege) => _held.Remove(privilege);

        public override Task<bool> CallerHoldsPrivilegeAsync(
            string? callerBearerToken, string privilegeName, CancellationToken ct = default)
        {
            Calls.Add(new PrivilegeCall(privilegeName, !string.IsNullOrEmpty(callerBearerToken)));
            if (ThrowOnEveryCall is not null)
            {
                return Task.FromException<bool>(ThrowOnEveryCall);
            }

            return Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && _held.Contains(privilegeName));
        }

        /// <summary>
        /// What WhoAmI on the caller's OBO token answers (task 162 f1, the personal-analysis creator match). Null — the
        /// default, and the base class's answer without OBO configuration — means "the caller's systemuserid could not be
        /// established".
        /// </summary>
        public Guid? CallerSystemUserId { get; set; }

        /// <summary>How many times the caller's systemuserid was asked for.</summary>
        public int SystemUserIdCalls { get; private set; }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
        {
            SystemUserIdCalls++;
            if (ThrowOnEveryCall is not null)
            {
                return Task.FromException<Guid?>(ThrowOnEveryCall);
            }

            return Task.FromResult(string.IsNullOrEmpty(callerBearerToken) ? null : CallerSystemUserId);
        }
    }

    /// <summary>
    /// <see cref="FinanceRollupService"/> at its virtual seam. By default it runs the REAL service (which fails on
    /// the unwrappable in-process double — see the class remarks); an id in <see cref="DeletedAfterCheck"/> instead
    /// throws the <see cref="KeyNotFoundException"/> the update-only write raises for a parent deleted after the check.
    /// </summary>
    internal sealed class RollupSeam : FinanceRollupService
    {
        public RollupSeam(IDataverseService reads, IFieldMappingDataverseService writes)
            : base(reads, writes, NullLogger<FinanceRollupService>.Instance)
        {
        }

        public HashSet<Guid> DeletedAfterCheck { get; } = new();

        public List<Guid> Calls { get; } = new();

        public override Task<RecalculateFinanceResponse> RecalculateMatterAsync(Guid matterId, CancellationToken ct = default)
        {
            Calls.Add(matterId);
            return DeletedAfterCheck.Contains(matterId)
                ? Task.FromException<RecalculateFinanceResponse>(new KeyNotFoundException("sprk_matter record was not found."))
                : base.RecalculateMatterAsync(matterId, ct);
        }

        public override Task<RecalculateFinanceResponse> RecalculateProjectAsync(Guid projectId, CancellationToken ct = default)
        {
            Calls.Add(projectId);
            return DeletedAfterCheck.Contains(projectId)
                ? Task.FromException<RecalculateFinanceResponse>(new KeyNotFoundException("sprk_project record was not found."))
                : base.RecalculateProjectAsync(projectId, ct);
        }
    }

    /// <summary>
    /// Hand-written <see cref="IAccessDataSource"/>: answers from a rights table and records every question.
    /// The document path (<see cref="IAccessDataSource.GetUserAccessAsync"/>) is the one whose Dataverse target
    /// is hard-coded to sprk_documents, so it is recorded as such.
    /// </summary>
    internal sealed class RecordingAccessDataSource : IAccessDataSource
    {
        private readonly Dictionary<(string Set, Guid Id), AccessRights> _rights = new();

        public List<AccessCall> Calls { get; } = new();

        public Exception? ThrowOnEveryCall { get; set; }

        public void Grant(string set, Guid id, AccessRights rights) => _rights[(set, id)] = rights;

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
        {
            var id = Guid.Parse(resourceId);
            Calls.Add(new AccessCall(AccessPath.Document, Documents, id, !string.IsNullOrEmpty(userAccessToken)));
            return Answer(userId, Documents, id);
        }

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
        {
            Calls.Add(new AccessCall(AccessPath.Record, entitySetName, recordId, !string.IsNullOrEmpty(userAccessToken)));
            return Answer(userId, entitySetName, recordId);
        }

        private Task<AccessSnapshot> Answer(string userId, string set, Guid id)
        {
            if (ThrowOnEveryCall is not null)
            {
                return Task.FromException<AccessSnapshot>(ThrowOnEveryCall);
            }

            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = id.ToString(),
                AccessRights = _rights.TryGetValue((set, id), out var rights) ? rights : AccessRights.None,
            });
        }
    }

    /// <summary>A minimal host over the three REAL endpoint mappers.</summary>
    internal sealed class FinanceAuthHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public RecordingAccessDataSource Access { get; } = new();
        public RecordingPrivilegeProbe Probe { get; } = new();
        public Mock<IInvoiceReviewService> Review { get; } = new(MockBehavior.Strict);
        public Mock<IInvoiceSearchService> Search { get; } = new(MockBehavior.Strict);
        public Mock<IFinanceSummaryService> Summary { get; } = new(MockBehavior.Strict);
        public Mock<IKpiDataverseService> Kpi { get; } = new(MockBehavior.Strict);
        public Mock<IFieldMappingDataverseService> ScorecardWrites { get; } = new(MockBehavior.Strict);
        public Mock<IDataverseService> RollupReads { get; } = new(MockBehavior.Strict);
        public Mock<IFieldMappingDataverseService> RollupWrites { get; } = new(MockBehavior.Strict);
        public RollupSeam Rollup { get; private set; } = null!;

        /// <param name="registerProbe">False builds a host whose container has NO <see cref="CallerRecordAccessProbe"/>
        /// — the configuration fault the Privilege path must deny on.</param>
        public static async Task<FinanceAuthHost> StartAsync(bool registerProbe = true)
        {
            var host = new FinanceAuthHost();
            await host.InitializeAsync(registerProbe);
            return host;
        }

        private async Task InitializeAsync(bool registerProbe)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();

            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(
                    FinanceAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

            // The REAL authorization stack, substituted only at the access-data boundary.
            builder.Services.AddSingleton<IAccessDataSource>(Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            if (registerProbe)
            {
                builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);
            }

            builder.Services.AddSingleton(Review.Object);
            builder.Services.AddSingleton(Search.Object);
            builder.Services.AddSingleton(Summary.Object);
            builder.Services.AddSingleton(new ScorecardCalculatorService(
                Kpi.Object, ScorecardWrites.Object, NullLogger<ScorecardCalculatorService>.Instance));
            Rollup = new RollupSeam(RollupReads.Object, RollupWrites.Object);
            builder.Services.AddSingleton<FinanceRollupService>(Rollup);

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();

            _app.MapFinanceEndpoints();
            _app.MapFinanceRollupEndpoints();
            _app.MapScorecardCalculatorEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client!.SendAsync(request);

        public void SetupScorecardReads() =>
            Kpi.Setup(k => k.BatchQueryKpiAssessmentsAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<int, KpiAssessmentRecord[]>());

        public void GrantFullConfirm(ConfirmBody body)
        {
            Access.Grant(Documents, body.DocumentId, AccessRights.Read | AccessRights.Write | AccessRights.Append);
            Access.Grant(Matters, body.MatterId, AccessRights.Read | AccessRights.AppendTo);
            Access.Grant(VendorOrganizations, body.VendorOrgId, AccessRights.Read | AccessRights.AppendTo);
            Probe.Hold(CreateInvoicePrivilege);
        }

        /// <summary>Every service and Dataverse seam behind the eight handlers was left untouched.</summary>
        public void VerifyNoServiceWasInvoked()
        {
            Review.VerifyNoOtherCalls();
            Search.VerifyNoOtherCalls();
            Summary.VerifyNoOtherCalls();
            Kpi.VerifyNoOtherCalls();
            ScorecardWrites.VerifyNoOtherCalls();
            RollupReads.VerifyNoOtherCalls();
            RollupWrites.VerifyNoOtherCalls();
            Rollup.Calls.Should().BeEmpty("the finance rollup handler must not have been reached");
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

/// <summary>
/// Authenticates a request carrying <see cref="CallerHeader"/> as a caller with an Entra <c>oid</c>, independent
/// of the Authorization header — so a test can present an authenticated principal whose bearer token is absent,
/// which is the "token unreadable" fail-closed case.
/// </summary>
public sealed class FinanceAuthzTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FinanceAuthzTest";
    public const string CallerHeader = "X-Test-Caller";
    public const string CallerObjectId = "6f0c1a52-0000-4000-8000-000000000130";

    public FinanceAuthzTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(CallerHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(new[] { new Claim("oid", CallerObjectId) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
