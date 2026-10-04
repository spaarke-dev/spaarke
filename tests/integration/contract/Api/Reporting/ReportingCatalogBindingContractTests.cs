// Reporting catalog-row contract — unified-access-control-r2 task 166 r1 (sweep F14/F15/F16 + amendment f; owner
// round 21 item 2, option A).
//
// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`
//   - Path:     `tests/integration/contract/Api/Reporting/**`
//   - Justification: every /api/reporting route used to act, as the Power BI service principal, on a workspace id and
//     report id the request named. The contract is now: the ONLY report id on the wire is the sprk_report catalog row
//     id; the row is read AS THE CALLER before any Power BI call; the Power BI ids are derived from the row; absent and
//     unreadable are one 404; the RLS identity is the caller's business unit read server-side. Only the REAL route
//     group (with its real ReportingAuthorizationFilter) proves which id each handler consumes.
//
// Doubles are module boundaries only (ADR-038 §4): IDataverseUserClient (the caller's OBO Dataverse client),
// ReportingEmbedService at its permitted virtual seam (ADR-010 — unsealed for this), CallerRecordAccessProbe at its
// virtual privilege question, and IGenericEntityService (the app-only "does another catalog row reference this
// report?" read, task 166 r2). No Mock<HttpMessageHandler>, no DI-registration assertion, no constructor null-check.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Reporting;

public sealed class ReportingCatalogBindingContractTests
{
    private static readonly Guid CatalogRowId = Guid.Parse("a1660000-0000-4000-8000-0000000000a1");
    private static readonly Guid PbiReportId = Guid.Parse("b1660000-0000-4000-8000-0000000000b1");
    private static readonly Guid WorkspaceId = Guid.Parse("c1660000-0000-4000-8000-0000000000c1");
    private static readonly Guid DatasetId = Guid.Parse("d1660000-0000-4000-8000-0000000000d1");
    private static readonly Guid CallerBusinessUnit = Guid.Parse("e1660000-0000-4000-8000-0000000000e1");
    private static readonly Guid ForeignWorkspace = Guid.Parse("f1660000-0000-4000-8000-0000000000f1");

    private const string Viewer = "sprk_ReportingAccess";
    private const string Author = "sprk_ReportingAccess,sprk_ReportingAuthor";
    private const string Admin = "sprk_ReportingAccess,sprk_ReportingAdmin";

    // =============================================================================================
    // Absent and unreadable are ONE answer, and Power BI is never asked (F14 / F15 / F16).
    // =============================================================================================

    // "row-of-another-id" (task 166 r2): the read answers 200 with a row whose sprk_reportid is NOT the id asked for —
    // the binding never adopts a row other than the one the request named.
    public static TheoryData<string> UnboundRowShapes => new()
    {
        "not-found", "access-denied", "obo-failed", "read-throws", "row-without-power-bi-ids", "row-of-another-id",
    };

    [Theory]
    [MemberData(nameof(UnboundRowShapes))]
    public async Task EmbedToken_ForARowTheCallerCannotBindTo_IsTheUniform404_AndPowerBiIsNeverAsked(string shape)
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow(shape);

        var response = await host.Send(HttpMethod.Get, $"/api/reporting/embed-token?reportId={CatalogRowId}", Viewer);

        await AssertNotInCatalogAsync(host, response);
    }

    [Theory]
    [MemberData(nameof(UnboundRowShapes))]
    public async Task Export_ForARowTheCallerCannotBindTo_IsTheUniform404_AndPowerBiIsNeverAsked(string shape)
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow(shape);

        var response = await host.Send(HttpMethod.Post, "/api/reporting/export", Viewer,
            new { reportId = CatalogRowId, format = "PDF" });

        await AssertNotInCatalogAsync(host, response);
    }

    [Theory]
    [MemberData(nameof(UnboundRowShapes))]
    public async Task GetReport_ForARowTheCallerCannotBindTo_IsTheUniform404(string shape)
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow(shape);

        var response = await host.Send(HttpMethod.Get, $"/api/reporting/reports/{CatalogRowId}", Viewer);

        await AssertNotInCatalogAsync(host, response);
    }

    [Fact]
    public async Task UnreadableAndAbsent_AreByteIdentical()
    {
        await using var denied = await Host.StartAsync();
        denied.ArrangeCatalogRow("access-denied");
        await using var absent = await Host.StartAsync();
        absent.ArrangeCatalogRow("not-found");

        var a = await denied.Send(HttpMethod.Get, $"/api/reporting/embed-token?reportId={CatalogRowId}", Viewer);
        var b = await absent.Send(HttpMethod.Get, $"/api/reporting/embed-token?reportId={CatalogRowId}", Viewer);

        Normalize(await a.Content.ReadAsStringAsync()).Should().Be(Normalize(await b.Content.ReadAsStringAsync()));
    }

    // =============================================================================================
    // The bound path — ids derived from the row, the caller's business unit as the RLS identity.
    // =============================================================================================

    [Fact]
    public async Task EmbedToken_ForAReadableRow_UsesTheRowsPowerBiIds_AndTheCallersBusinessUnitAsTheRlsIdentity()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        host.ArrangeBusinessUnit(CallerBusinessUnit);
        host.Embed
            .Setup(e => e.GetEmbedConfigAsync(WorkspaceId, PbiReportId, It.IsAny<string?>(), It.IsAny<IList<string>?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbedConfig("embed-token", "https://app.powerbi.com/embed", PbiReportId,
                DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddMinutes(48)));

        // A workspaceId the client still sends (old contract) is not read by anything.
        var response = await host.Send(HttpMethod.Get,
            $"/api/reporting/embed-token?reportId={CatalogRowId}&workspaceId={ForeignWorkspace}", Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.CatalogPathsRead.Should().Contain(ReportingEndpoints.CatalogRowPath(CatalogRowId),
            "the catalog row is read through the CALLER's OBO client");
        host.Embed.Verify(e => e.GetEmbedConfigAsync(
            WorkspaceId, PbiReportId,
            CallerBusinessUnit.ToString("D"),
            It.Is<IList<string>?>(roles => roles != null && roles.SequenceEqual(new[] { "BusinessUnitFilter" })),
            null, It.IsAny<CancellationToken>()), Times.Once,
            "the token's RLS identity is the caller's business unit, read server-side — never a token claim");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["workspaceId"]!.GetValue<Guid>().Should().Be(WorkspaceId, "the derived workspace rides back for Save As");
        body["reportId"]!.GetValue<Guid>().Should().Be(PbiReportId);
    }

    [Fact]
    public async Task EmbedToken_WhenTheCallersBusinessUnitCannotBeRead_IssuesNoToken()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        host.ArrangeBusinessUnit(null);

        var response = await host.Send(HttpMethod.Get, $"/api/reporting/embed-token?reportId={CatalogRowId}", Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "a token without the business-unit RLS identity would show the whole dataset");
        host.VerifyPowerBiNeverAsked();
    }

    [Fact]
    public async Task EmbedToken_WithoutAReportId_KeepsItsValidation400_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Send(HttpMethod.Get, "/api/reporting/embed-token", Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.CatalogPathsRead.Should().BeEmpty();
        host.VerifyPowerBiNeverAsked();
    }

    [Fact]
    public async Task Export_ForAReadableRow_ExportsTheDerivedReport_UnderTheCallersBusinessUnitRlsIdentity()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        host.ArrangeBusinessUnit(CallerBusinessUnit);
        host.Embed
            .Setup(e => e.ExportReportAsync(WorkspaceId, PbiReportId, ExportFormat.PDF, It.IsAny<string>(),
                It.IsAny<IList<string>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream("%PDF-1.7"u8.ToArray()));

        var response = await host.Send(HttpMethod.Post, "/api/reporting/export", Viewer,
            new { reportId = CatalogRowId, format = "PDF", workspaceId = ForeignWorkspace });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        host.Embed.Verify(e => e.ExportReportAsync(
                WorkspaceId, PbiReportId, ExportFormat.PDF,
                CallerBusinessUnit.ToString("D"),
                It.Is<IList<string>>(roles => roles != null && roles.SequenceEqual(new[] { "BusinessUnitFilter" })),
                null, It.IsAny<CancellationToken>()),
            Times.Once,
            "the report and workspace come from the catalog row, and the export runs under the SAME server-computed "
            + "business-unit RLS identity as an embed token (task 166 r2) — never unfiltered");
    }

    [Fact]
    public async Task Export_WhenTheCallersBusinessUnitCannotBeRead_ExportsNothing()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        host.ArrangeBusinessUnit(null);

        var response = await host.Send(HttpMethod.Post, "/api/reporting/export", Viewer,
            new { reportId = CatalogRowId, format = "PDF" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "an export without the business-unit RLS identity would contain every business unit's rows");
        host.VerifyPowerBiNeverAsked();
    }

    // The export request Power BI receives carries the identity (task 166 r2): the service cannot be asked to export
    // without one, and the request it builds names the username, the role and the report's dataset.
    [Fact]
    public void ExportRequest_CarriesTheEffectiveIdentity_OnTheReportsDataset()
    {
        var request = ReportingEmbedService.BuildExportRequest(
            ExportFormat.PPTX, DatasetId.ToString(), CallerBusinessUnit.ToString("D"), ["BusinessUnitFilter"]);

        request.Format.Should().Be(Microsoft.PowerBI.Api.Models.FileFormat.PPTX);
        var identity = request.PowerBIReportConfiguration.Identities.Should().ContainSingle().Subject;
        identity.Username.Should().Be(CallerBusinessUnit.ToString("D"));
        identity.Roles.Should().Equal("BusinessUnitFilter");
        identity.Datasets.Should().Equal(DatasetId.ToString());
    }

    [Theory]
    [InlineData("", "BusinessUnitFilter", "dataset")]
    [InlineData("bu", null, "dataset")]
    [InlineData("bu", "BusinessUnitFilter", null)]
    public void ExportRequest_WithoutACompleteIdentity_IsNeverBuilt(string username, string? role, string? datasetId)
    {
        var roles = role is null ? new List<string>() : new List<string> { role };

        var build = () => ReportingEmbedService.BuildExportRequest(ExportFormat.PDF, datasetId, username, roles);

        build.Should().Throw<Exception>("an export without a complete RLS identity would run unfiltered");
    }

    [Fact]
    public async Task GetReport_ForAReadableRow_ReturnsTheCatalogEntry()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");

        var response = await host.Send(HttpMethod.Get, $"/api/reporting/reports/{CatalogRowId}", Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        item["id"]!.GetValue<Guid>().Should().Be(CatalogRowId);
        item["category"]!.GetValue<string>().Should().Be("Financial");
        host.VerifyPowerBiNeverAsked();
    }

    [Fact]
    public async Task GetReports_ListsOnlyWhatTheCallersOwnReadReturns()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogList(RowJson(CatalogRowId), RowJson(Guid.NewGuid(), withPowerBiIds: false));

        var response = await host.Send(HttpMethod.Get, "/api/reporting/reports", Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.CatalogPathsRead.Should().Equal(ReportingEndpoints.CatalogListPath);
        var items = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        items.Should().ContainSingle("a row without usable Power BI ids is not offered")
            .Which!["id"]!.GetValue<Guid>().Should().Be(CatalogRowId);
    }

    [Fact]
    public async Task GetReports_WhenTheCatalogCannotBeRead_IsAnError_NeverAnEmptyList()
    {
        await using var host = await Host.StartAsync();
        host.DataverseUser
            .Setup(d => d.GetAsync(ReportingEndpoints.CatalogListPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "unavailable"));

        var response = await host.Send(HttpMethod.Get, "/api/reporting/reports", Viewer);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    // =============================================================================================
    // Amendment (f) — catalog CRUD binds to rows the caller can read, and writes them AS the caller.
    // =============================================================================================

    [Fact]
    public async Task Create_FromAReadableSource_ClonesInTheSourcesWorkspace_AndRegistersTheRowAsTheCaller()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        var cloneId = Guid.NewGuid();
        var newRowId = Guid.NewGuid();
        host.Embed
            .Setup(e => e.CreateReportAsync(WorkspaceId, "Q4 copy", DatasetId, PbiReportId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PowerBiReport(cloneId, "Q4 copy", "https://app.powerbi.com/clone", DatasetId));
        host.ArrangeCatalogCreate(newRowId);

        var response = await host.Send(HttpMethod.Post, "/api/reporting/reports", Author,
            new { name = "Q4 copy", sourceReportId = CatalogRowId, workspaceId = ForeignWorkspace });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reportId"]!.GetValue<Guid>().Should().Be(newRowId);
        var created = JsonNode.Parse(host.CreatedRowPayloads.Should().ContainSingle().Subject)!;
        created["sprk_workspaceid"]!.GetValue<string>().Should().Be(WorkspaceId.ToString("D"),
            "the workspace is the SOURCE row's — a client workspace is never written");
        created["sprk_pbi_reportid"]!.GetValue<string>().Should().Be(cloneId.ToString("D"));
    }

    [Fact]
    public async Task Create_FromAnUnreadableSource_IsTheUniform404_AndCreatesNothing()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("access-denied");

        var response = await host.Send(HttpMethod.Post, "/api/reporting/reports", Author,
            new { name = "Copy", sourceReportId = CatalogRowId });

        await AssertNotInCatalogAsync(host, response);
        host.CreatedRowPayloads.Should().BeEmpty();
    }

    // Owner round 23 item 2 (task 166 r2): the client-named "Save As" registration path is gone. A body that still
    // names a Power BI report gets a CLONE of the source row's report; the named report is never looked up, never
    // registered, so a caller cannot alias an existing (uncatalogued or unreadable) report into a row they own.
    [Fact]
    public async Task Create_WithAClientNamedPowerBiReport_ClonesTheSource_AndNeverRegistersTheNamedReport()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        var named = Guid.NewGuid();
        var cloneId = Guid.NewGuid();
        host.Embed
            .Setup(e => e.CreateReportAsync(WorkspaceId, "Saved as", DatasetId, PbiReportId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PowerBiReport(cloneId, "Saved as", "https://app.powerbi.com/clone", DatasetId));
        host.ArrangeCatalogCreate(Guid.NewGuid());

        var response = await host.Send(HttpMethod.Post, "/api/reporting/reports", Author,
            new { name = "Saved as", sourceReportId = CatalogRowId, pbiReportId = named });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Embed.Verify(e => e.GetReportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never, "a client-named Power BI report is never looked up");
        var created = JsonNode.Parse(host.CreatedRowPayloads.Should().ContainSingle().Subject)!;
        created["sprk_pbi_reportid"]!.GetValue<string>().Should().Be(cloneId.ToString("D"),
            "the new row points at the server's clone, never at the report the client named");
        created.ToJsonString().Should().NotContain(named.ToString("D"));
    }

    // Owner round 9 write pattern (task 166 r2): the caller's own Create on sprk_report is asked AS THE CALLER before
    // the app-only Power BI clone — a caller who may not create the row causes no clone at all.
    [Theory]
    [InlineData("denied")]
    [InlineData("throws")]
    public async Task Create_WithoutTheCallersCreatePrivilege_IsRefused_BeforeAnyReadOrClone(string answer)
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        var privilege = host.AccessProbe.Setup(p => p.CallerHoldsPrivilegeAsync(
            It.IsAny<string?>(), ReportingEndpoints.CreateReportPrivilege, It.IsAny<CancellationToken>()));
        if (answer == "denied")
        {
            privilege.ReturnsAsync(false);
        }
        else
        {
            privilege.ThrowsAsync(new HttpRequestException("Dataverse unavailable"));
        }

        var response = await host.Send(HttpMethod.Post, "/api/reporting/reports", Author,
            new { name = "Copy", sourceReportId = CatalogRowId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.AccessProbe.Verify(p => p.CallerHoldsPrivilegeAsync(
            "caller-token", ReportingEndpoints.CreateReportPrivilege, It.IsAny<CancellationToken>()), Times.Once,
            "the question is asked with the CALLER's own token");
        host.CatalogPathsRead.Should().BeEmpty();
        host.CreatedRowPayloads.Should().BeEmpty();
        host.VerifyPowerBiNeverAsked();
    }

    [Fact]
    public void CreateReportPrivilege_IsTheLiveVerifiedName()
    {
        // Read-only on spaarkedev1, 2026-10-04: privilegeid 4ea28bbd…, name prvCreatesprk_Report, accessright 32 (Create).
        ReportingEndpoints.CreateReportPrivilege.Should().Be("prvCreatesprk_Report");
    }

    [Fact]
    public async Task Create_WhenTheCallerMayNotCreateTheCatalogRow_TheCloneIsRemoved_AndTheAnswerIs403()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        var cloneId = Guid.NewGuid();
        host.Embed
            .Setup(e => e.CreateReportAsync(WorkspaceId, It.IsAny<string>(), DatasetId, PbiReportId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PowerBiReport(cloneId, "Copy", "https://app.powerbi.com/clone", DatasetId));
        host.DataverseUser
            .Setup(d => d.PostAsync(ReportingEndpoints.CatalogCollectionApiPath, It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "no create"));

        var response = await host.Send(HttpMethod.Post, "/api/reporting/reports", Author,
            new { name = "Copy", sourceReportId = CatalogRowId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Embed.Verify(e => e.DeleteReportAsync(WorkspaceId, cloneId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once,
            "an uncatalogued clone is not left behind");
    }

    [Fact]
    public async Task Create_ByAViewer_IsRefused_BeforeAnyRead()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Send(HttpMethod.Post, "/api/reporting/reports", Viewer,
            new { name = "Copy", sourceReportId = CatalogRowId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.CatalogPathsRead.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_IsPatch_TheVerbTheClientSends_AndWritesTheRowAsTheCaller()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        host.DataverseUser
            .Setup(d => d.PatchAsync($"sprk_reports({CatalogRowId:D})", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(204, null));

        var patch = await host.Send(HttpMethod.Patch, $"/api/reporting/reports/{CatalogRowId}", Author, new { name = "Renamed" });
        var put = await host.Send(HttpMethod.Put, $"/api/reporting/reports/{CatalogRowId}", Author, new { name = "Renamed" });

        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        put.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "the route maps PATCH only");
        host.DataverseUser.Verify(d => d.PatchAsync(
            $"sprk_reports({CatalogRowId:D})", It.Is<string>(json => json.Contains("Renamed")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_OfAnUnreadableRow_IsTheUniform404_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("not-found");

        var response = await host.Send(HttpMethod.Patch, $"/api/reporting/reports/{CatalogRowId}", Author, new { name = "x" });

        await AssertNotInCatalogAsync(host, response);
        host.DataverseUser.Verify(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_OfACustomRowNoOtherRowReferences_DeletesTheRowAsTheCallerFirst_ThenTheDerivedReport()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable-custom");
        host.ArrangeOtherReferences(0);
        var order = new List<string>();
        host.DataverseUser
            .Setup(d => d.DeleteAsync($"sprk_reports({CatalogRowId:D})", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("row"))
            .ReturnsAsync(DataverseUserResponse.Ok(204, null));
        host.Embed
            .Setup(e => e.DeleteReportAsync(WorkspaceId, PbiReportId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("report"))
            .Returns(Task.CompletedTask);

        var response = await host.Send(HttpMethod.Delete, $"/api/reporting/reports/{CatalogRowId}", Admin);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        order.Should().Equal("row", "report");
        var query = host.ReferenceQueries.Should().ContainSingle(
            "whether another row references the report is read APP-ONLY — a caller-scoped read would miss rows the caller cannot see").Subject;
        query.EntityName.Should().Be("sprk_report");
        query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "sprk_pbi_reportid"
            && c.Operator == ConditionOperator.Equal && (string)c.Values[0] == PbiReportId.ToString("D"));
        query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "sprk_reportid"
            && c.Operator == ConditionOperator.NotEqual && (Guid)c.Values[0] == CatalogRowId);
    }

    // Owner round 23 item 2 (task 166 r2): DELETE removes the Power BI report ONLY for a custom row no other catalog row
    // references. A standard report, a report another row (even one the caller cannot see) still points at, and an
    // unanswerable reference question all KEEP the report; the caller's catalog row is still deleted.
    [Theory]
    [InlineData("standard-row")]
    [InlineData("referenced-by-another-row")]
    [InlineData("reference-check-throws")]
    public async Task Delete_KeepsThePowerBiReport_UnlessItIsACustomRowsUnreferencedReport(string shape)
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow(shape == "standard-row" ? "readable" : "readable-custom");
        switch (shape)
        {
            case "referenced-by-another-row":
                host.ArrangeOtherReferences(1);
                break;
            case "reference-check-throws":
                host.EntityService
                    .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("Dataverse unavailable"));
                break;
        }

        host.DataverseUser
            .Setup(d => d.DeleteAsync($"sprk_reports({CatalogRowId:D})", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(204, null));

        var response = await host.Send(HttpMethod.Delete, $"/api/reporting/reports/{CatalogRowId}", Admin);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the caller's catalog row was deleted");
        host.DataverseUser.Verify(d => d.DeleteAsync($"sprk_reports({CatalogRowId:D})", It.IsAny<CancellationToken>()), Times.Once);
        host.Embed.Verify(e => e.DeleteReportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never, "a shared or still-referenced Power BI report is never destroyed by deleting one catalog row");
    }

    [Fact]
    public async Task Delete_WhenTheCallerMayNotDeleteTheRow_TheReportIsNeverDeleted()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("readable");
        host.DataverseUser
            .Setup(d => d.DeleteAsync($"sprk_reports({CatalogRowId:D})", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "no delete"));

        var response = await host.Send(HttpMethod.Delete, $"/api/reporting/reports/{CatalogRowId}", Admin);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyPowerBiNeverAsked();
    }

    [Fact]
    public async Task Delete_OfAnUnreadableRow_IsTheUniform404_AndDeletesNothing()
    {
        await using var host = await Host.StartAsync();
        host.ArrangeCatalogRow("access-denied");

        var response = await host.Send(HttpMethod.Delete, $"/api/reporting/reports/{CatalogRowId}", Admin);

        await AssertNotInCatalogAsync(host, response);
        host.DataverseUser.Verify(d => d.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =============================================================================================
    // Harness
    // =============================================================================================

    private static async Task AssertNotInCatalogAsync(Host host, HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["title"]!.GetValue<string>().Should().Be("Report Not Found");
        problem["reasonCode"]!.GetValue<string>().Should().Be(ReportingEndpoints.ReportNotInCatalogReasonCode);
        host.VerifyPowerBiNeverAsked();
    }

    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }

    private static JsonElement RowJson(Guid rowId, bool withPowerBiIds = true, bool isCustom = false)
    {
        var row = new Dictionary<string, object?>
        {
            ["sprk_reportid"] = rowId.ToString("D"),
            ["sprk_name"] = "Q4 Financials",
            ["sprk_pbi_reportid"] = withPowerBiIds ? (rowId == CatalogRowId ? PbiReportId : Guid.NewGuid()).ToString("D") : null,
            ["sprk_workspaceid"] = WorkspaceId.ToString("D"),
            ["sprk_datasetid"] = DatasetId.ToString("D"),
            ["sprk_embedurl"] = "https://app.powerbi.com/reportEmbed",
            ["sprk_category"] = 100000000,
            ["sprk_iscustom"] = isCustom,
        };
        return JsonSerializer.SerializeToElement(row);
    }

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public Mock<IDataverseUserClient> DataverseUser { get; } = new(MockBehavior.Loose);
        public Mock<ReportingEmbedService> Embed { get; }
        public List<string> CatalogPathsRead { get; } = new();
        public List<string> CreatedRowPayloads { get; } = new();

        /// <summary>The caller's privilege question (task 166 r2). Defaults to "holds prvCreatesprk_Report".</summary>
        public Mock<CallerRecordAccessProbe> AccessProbe { get; } = new(
            MockBehavior.Loose,
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<CallerRecordAccessProbe>.Instance,
            null!);

        /// <summary>The app-only reference read DELETE makes (task 166 r2). Defaults to "no other row".</summary>
        public Mock<IGenericEntityService> EntityService { get; } = new(MockBehavior.Loose);
        public List<QueryExpression> ReferenceQueries { get; } = new();

        private Host()
        {
            AccessProbe
                .Setup(p => p.CallerHoldsPrivilegeAsync(It.IsAny<string?>(), ReportingEndpoints.CreateReportPrivilege, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            ArrangeOtherReferences(0);

            Embed = new Mock<ReportingEmbedService>(
                MockBehavior.Loose,
                Options.Create(new PowerBiOptions
                {
                    TenantId = "00000000-0000-0000-0000-000000000166",
                    ClientId = "00000000-0000-0000-0000-0000000001a6",
                    ClientSecret = "test-secret",
                }),
                Mock.Of<ITenantCache>(),
                new HttpContextAccessor(),
                NullLogger<ReportingEmbedService>.Instance);
        }

        public static async Task<Host> StartAsync()
        {
            var host = new Host();
            await host.InitializeAsync();
            return host;
        }

        public void ArrangeCatalogRow(string shape)
        {
            var path = ReportingEndpoints.CatalogRowPath(CatalogRowId);
            var setup = DataverseUser.Setup(d => d.GetAsync(path, It.IsAny<CancellationToken>()));
            var record = setup.Callback((string p, CancellationToken _) => CatalogPathsRead.Add(p));
            switch (shape)
            {
                case "readable":
                    record.ReturnsAsync(DataverseUserResponse.Ok(200, RowJson(CatalogRowId)));
                    break;
                case "readable-custom":
                    record.ReturnsAsync(DataverseUserResponse.Ok(200, RowJson(CatalogRowId, isCustom: true)));
                    break;
                case "row-of-another-id":
                    // A well-formed row with usable Power BI ids — but not the row the request named.
                    record.ReturnsAsync(DataverseUserResponse.Ok(200, RowJson(Guid.NewGuid())));
                    break;
                case "not-found":
                    record.ReturnsAsync(DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "not found"));
                    break;
                case "access-denied":
                    record.ReturnsAsync(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied"));
                    break;
                case "obo-failed":
                    record.ReturnsAsync(DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.OboExchangeFailed, "obo"));
                    break;
                case "read-throws":
                    record.ThrowsAsync(new HttpRequestException("Dataverse unavailable"));
                    break;
                case "row-without-power-bi-ids":
                    var bare = new Dictionary<string, object?> { ["sprk_reportid"] = CatalogRowId.ToString("D"), ["sprk_name"] = "x" };
                    record.ReturnsAsync(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(bare)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
            }
        }

        public void ArrangeCatalogList(params JsonElement[] rows)
            => DataverseUser
                .Setup(d => d.GetAsync(ReportingEndpoints.CatalogListPath, It.IsAny<CancellationToken>()))
                .Callback((string p, CancellationToken _) => CatalogPathsRead.Add(p))
                .ReturnsAsync(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows })));

        public void ArrangeBusinessUnit(Guid? businessUnitId)
            => DataverseUser
                .Setup(d => d.GetAsync("WhoAmI", It.IsAny<CancellationToken>()))
                .ReturnsAsync(businessUnitId is { } bu
                    ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { UserId = Guid.NewGuid(), BusinessUnitId = bu }))
                    : DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.OboExchangeFailed, "obo"));

        /// <summary>The app-only reference read returns <paramref name="others"/> OTHER catalog rows.</summary>
        public void ArrangeOtherReferences(int others)
            => EntityService
                .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Callback((QueryExpression q, CancellationToken _) => ReferenceQueries.Add(q))
                .ReturnsAsync(() =>
                {
                    var rows = new EntityCollection();
                    for (var i = 0; i < others; i++)
                    {
                        rows.Entities.Add(new Entity("sprk_report", Guid.NewGuid()));
                    }

                    return rows;
                });

        public void ArrangeCatalogCreate(Guid newRowId)
            => DataverseUser
                .Setup(d => d.PostAsync(ReportingEndpoints.CatalogCollectionApiPath, It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                .Callback((string _, string json, bool _, CancellationToken _) => CreatedRowPayloads.Add(json))
                .ReturnsAsync(DataverseUserResponse.Ok(201, JsonSerializer.SerializeToElement(new { sprk_reportid = newRowId.ToString("D") })));

        public void VerifyPowerBiNeverAsked()
        {
            Embed.Verify(e => e.GetEmbedConfigAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<IList<string>?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Embed.Verify(e => e.ExportReportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ExportFormat>(), It.IsAny<string>(), It.IsAny<IList<string>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Embed.Verify(e => e.GetReportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Embed.Verify(e => e.CreateReportAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Embed.Verify(e => e.DeleteReportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public Task<HttpResponseMessage> Send(HttpMethod method, string url, string roles, object? body = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(ReportingContractAuthHandler.RolesHeader, roles);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return _client.SendAsync(request);
        }

        private async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Reporting:ModuleEnabled"] = "true" });
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = ReportingContractAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = ReportingContractAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, ReportingContractAuthHandler>(ReportingContractAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(DataverseUser.Object);
            builder.Services.AddSingleton(Embed.Object);
            builder.Services.AddSingleton(AccessProbe.Object);
            builder.Services.AddSingleton(EntityService.Object);
            builder.WebHost.UseTestServer();

            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.MapReportingEndpointGroup();
            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>Authenticates a bearer request as a caller with the module roles named in <see cref="RolesHeader"/>.</summary>
    private sealed class ReportingContractAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ReportingContract";
        public const string RolesHeader = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("oid", "6f0c1a52-0000-4000-8000-000000001660"), new("tid", "tenant-166") };
            foreach (var role in Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                claims.Add(new Claim("roles", role));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
