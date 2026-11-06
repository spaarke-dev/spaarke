using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// <c>GET /api/finance/export-batches/{batchId}/files/{format}</c> (ebilling-r1 task 151a; EB-D-181, FR-41, ADR-008, ADR-019): the stored CSV or JSON of
/// an AP export batch, byte for byte as written at export. The REAL route, filter, authorization stack, export service, Stored File service (scan
/// verdict, own path) and kill switch run over the in-memory world; the batch is created through the real POST route.
/// </summary>
public sealed class ApExportDownloadTests
{
    private const string Csv = "text/csv";
    private const string Json = "application/json";

    private static string Url(Guid batchId, string format) => $"/api/finance/export-batches/{batchId}/files/{format}";

    /// <summary>Exports two Approved invoices, scans the files clean and (unless told not to) grants the caller Read on the batch.</summary>
    private static async Task<(Guid BatchId, ApExportRoutesHost Host)> ExportedAsync(
        string[]? formats = null, bool grantRead = true, bool ebillingEnabled = true)
    {
        var host = await ApExportRoutesHost.StartAsync();
        var a = host.World.AddInvoice("INV-A");
        var b = host.World.AddInvoice("INV-B");
        var batchId = await host.ExportAsync([a, b], formats);
        host.MarkExportedFilesScannedClean();
        host.EbillingEnabled = ebillingEnabled;
        if (grantRead)
        {
            host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Read);
        }

        return (batchId, host);
    }

    [Theory(DisplayName = "151a: a caller with Read on a Completed batch downloads each file - the stored bytes, its content type and an attachment filename")]
    [InlineData("csv", Csv, "text/csv; charset=utf-8")]
    [InlineData("json", Json, "application/json; charset=utf-8")]
    public async Task ReadOnTheBatch_Downloads_TheStoredFile(string format, string storedType, string servedType)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var stored = host.World.TextOf(host.World.FileOf(batchId, storedType));

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, format));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Be(servedType);
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Be($"ap-export-{batchId:D}.{format}");
        (await response.Content.ReadAsStringAsync()).Should().Be(stored).And.NotBeEmpty();
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact(DisplayName = "151a: the download serves the file stored at export - a later change to the invoice does not change the bytes (never regenerated)")]
    public async Task Download_IsTheStoredFile_NeverRegenerated()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var stored = host.World.TextOf(host.World.FileOf(batchId, Csv));
        foreach (var invoice in host.World.Dataverse.RowsOf("sprk_invoice"))
        {
            host.World.Dataverse.Touch("sprk_invoice", invoice.Id, new Dictionary<string, object>
            {
                ["sprk_approvedtotal"] = new Money(123456.78m),
                ["sprk_invoicenumber"] = "CHANGED-AFTER-EXPORT",
            });
        }

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"));

        var served = await response.Content.ReadAsStringAsync();
        served.Should().Be(stored);
        served.Should().NotContain("CHANGED-AFTER-EXPORT").And.NotContain("123456.78");
    }

    [Fact(DisplayName = "151a: a format other than csv or json is 400 ebilling.export.invalid_format")]
    public async Task OtherFormat_Is400()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, "xml"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.InvalidFormat);
    }

    [Fact(DisplayName = "151a: a batch exported as JSON only has no CSV - 404 ebilling.export.file_not_available - and still serves its JSON")]
    public async Task FormatNotRequested_Is404_FileNotAvailable()
    {
        var (batchId, host) = await ExportedAsync(formats: ["json"]);
        await using var _ = host;

        var csv = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"));
        var json = await host.SendAsync(HttpMethod.Get, Url(batchId, "json"));

        csv.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await csv.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.FileNotAvailable);
        json.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "151a FR-41: a batch that is not Completed advertises no file - 404 ebilling.export.file_not_available, no bytes")]
    public async Task BatchNotCompleted_Is404_FileNotAvailable()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        host.World.Dataverse.Touch("sprk_billingexport", batchId, new Dictionary<string, object>
        {
            ["sprk_exportstatus"] = new OptionSetValue(ApExportService.StatusFailed),
        });
        var storageCalls = host.World.Gateway.StorageCalls;

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be(ApExportCodes.FileNotAvailable);
        host.World.Gateway.StorageCalls.Should().Be(storageCalls, "no blob is opened for a batch that did not complete");
    }

    [Fact(DisplayName = "151a: a file with no scan verdict yet is 409 file_scan_pending and no bytes")]
    public async Task ScanPending_Is409()
    {
        var host = await ApExportRoutesHost.StartAsync();
        await using var _ = host;
        var batchId = await host.ExportAsync([host.World.AddInvoice("INV-A")]);
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Read); // the blobs carry no scan tags: no verdict

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_scan_pending");
    }

    // =========================================================================================
    // Authorization (Read on the batch; uniform 404)
    // =========================================================================================

    [Fact(DisplayName = "151a: unauthenticated is 401 and touches nothing")]
    public async Task Unauthenticated_Is401()
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var storageCalls = host.World.Gateway.StorageCalls;

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
    }

    [Fact(DisplayName = "151a SEEDED: no Read on the batch (Write alone, or nothing) is the uniform 404 and no bytes")]
    public async Task NoReadOnTheBatch_Is404_AndNoBytes()
    {
        var (batchId, host) = await ExportedAsync(grantRead: false);
        await using var _ = host;
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, batchId, AccessRights.Write | AccessRights.Append);
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, Guid.NewGuid(), AccessRights.Read);
        var storageCalls = host.World.Gateway.StorageCalls;

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
    }

    [Fact(DisplayName = "151a: an unknown batch the caller holds rights on is the same uniform 404 as no rights")]
    public async Task UnknownBatch_IsTheUniform404()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var unknown = Guid.NewGuid();
        host.World.Access.Grant(ApExportAuthorization.BatchEntitySet, unknown, AccessRights.Read);

        var response = await host.SendAsync(HttpMethod.Get, Url(unknown, "csv"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
    }

    public static IEnumerable<object[]> RefusedKinds() => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "151a task 099 must-fire: a flagged-external, row-less or unreadable caller is the uniform 404 before any rights call, even holding Read on the batch")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_RefusesBeforeAnyRead(StepZeroCallerKind kind)
    {
        var (batchId, host) = await ExportedAsync();
        await using var _ = host;
        var storageCalls = host.World.Gateway.StorageCalls;
        var accessCalls = host.World.Access.Calls.Count;
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.World.Access.Calls.Count.Should().Be(accessCalls);
        host.World.Gateway.StorageCalls.Should().Be(storageCalls);
    }

    [Fact(DisplayName = "151a ADR-018: the kill switch is 503 ebilling.disabled for an authorized caller; an unauthorized caller still gets the 404")]
    public async Task FlagOff_AuthorizedGets503_UnauthorizedStillGets404()
    {
        var (batchId, host) = await ExportedAsync(ebillingEnabled: false);
        await using var _ = host;
        var unreadable = Guid.NewGuid();

        var authorized = await host.SendAsync(HttpMethod.Get, Url(batchId, "csv"));
        var unauthorized = await host.SendAsync(HttpMethod.Get, Url(unreadable, "csv"));

        authorized.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await authorized.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
