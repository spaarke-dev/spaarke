using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// <c>POST /api/finance/export-batches</c> and the owner of the export files (ebilling-r1, dev incident 2026-11-03, uac-r2): what the route answers
/// when the acting user's business unit cannot own the files. The REAL route, authorization stack, export service and Stored File service run over the
/// in-memory world (<see cref="ApExportRoutesHost"/>); the service-level proof of the writes is <c>ApExportOwnerUnitTests</c>.
/// </summary>
public sealed class ApExportOwnerUnitRouteTests
{
    private static readonly Regex AnyGuid = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static Task<HttpResponseMessage> PostAsync(ApExportRoutesHost host, params Entity[] invoices)
    {
        host.World.GrantEverything();
        return host.SendAsync(HttpMethod.Post, "/api/finance/export-batches", body: new { invoiceIds = invoices.Select(i => i.Id).ToArray() });
    }

    [Fact(DisplayName = "ebilling-r1 AP export: a caller in the ROOT business unit gets 409 ebilling.export.owner_unit_not_allowed (not a retryable 503), naming no id, with nothing written")]
    public async Task RootUnitCaller_Is409_NotA503_AndNothingIsWritten()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        host.World.CallerInRootBusinessUnit = true;
        var a = host.World.AddInvoice("INV-A");

        var response = await PostAsync(host, a);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(text)!;
        body["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.OwnerUnitNotAllowed).And.Be("ebilling.export.owner_unit_not_allowed");
        var detail = body["detail"]!.GetValue<string>();
        detail.Should().Contain("business unit").And.Contain("administrator").And.Contain("will not help").And.NotContain("Try again");
        AnyGuid.IsMatch(text).Should().BeFalse("the answer names no internal id (no batch, no user, no unit)");
        body["batchId"].Should().BeNull();
        host.World.Dataverse.Writes.Should().Be(0);
        host.World.Batches().Should().BeEmpty();
        host.World.StoredFiles().Should().BeEmpty();
        host.World.BatchOfInvoice(a.Id).Should().BeNull();
        host.World.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "ebilling-r1 AP export: a fault in the owner pre-check is 503 ebilling.export.unavailable (retry) with nothing written - never the 409")]
    public async Task PreCheckFault_Is503_AndNothingIsWritten()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        host.World.OwnerAnswer = (_, asked) => asked == 1 ? throw new InvalidOperationException("Dataverse unavailable") : null;

        var response = await PostAsync(host, a);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.Unavailable);
        host.World.Dataverse.Writes.Should().Be(0);
        host.World.Batches().Should().BeEmpty();
    }

    [Fact(DisplayName = "ebilling-r1 AP export: a transient owner fault in the file's own check is still 503 ebilling.export.file_failed through the route, compensated")]
    public async Task TransientFileOwnerFault_Is503FileFailed()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        host.World.OwnerAnswer = (_, asked) => asked == 3 ? throw new InvalidOperationException("Dataverse unavailable") : null;

        var response = await PostAsync(host, a);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.FileFailed);
        host.World.Batches().Should().ContainSingle().Which.GetAttributeValue<OptionSetValue>("sprk_exportstatus").Value.Should().Be(ApExportService.StatusFailed);
        host.World.BatchOfInvoice(a.Id).Should().BeNull();
        host.World.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.Approved);
    }

    [Fact(DisplayName = "ebilling-r1 AP export: a caller in a customer business unit (BU1) passes the pre-check and the export completes - 201, unchanged")]
    public async Task Bu1Caller_PassesThePreCheck_AndExportCompletes()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var a = host.World.AddInvoice("INV-A");

        var response = await PostAsync(host, a);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["status"]!.GetValue<string>().Should().Be("Completed");
        body["files"]!.AsArray().Should().HaveCount(2);
        host.World.StatusOfInvoice(a.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }
}
