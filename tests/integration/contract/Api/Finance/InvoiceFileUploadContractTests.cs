using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;
using Host = Sprk.Bff.Api.Tests.Api.Finance.StoredFileEndpointsTests.Host;
using U = Sprk.Bff.Api.Tests.Api.Finance.InvoiceFileUploadSeed;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>The invoice, the caller's Write and the request bodies the task 123 upload tests (contract and route) share.</summary>
internal static class InvoiceFileUploadSeed
{
    internal static readonly Guid InvoiceId = Guid.Parse("1a000000-0000-4000-8000-0000000000f0");

    private static readonly byte[] Small = Encoding.UTF8.GetBytes("date,hours\n2026-10-01,7.5\n");

    /// <summary>The invoice row the upload reads its state from, in <paramref name="status"/> (its statecode and statuscode pair).</summary>
    internal static void SeedInvoice(Host host, InvoiceLifecycleStatus status = InvoiceLifecycleStatus.InReview, Guid? invoiceId = null) =>
        host.Rig.Dataverse.AddOther(StoredFileSchema.InvoiceTable, invoiceId ?? InvoiceId,
            ("statecode", new OptionSetValue(InvoiceStatusCodes.StateCodeOf(status))),
            ("statuscode", new OptionSetValue(InvoiceStatusCodes.StatusCodeOf(status))));

    /// <summary>The caller's own Read and Write on the invoice (the filter's check and the upload's backstop read the same grant).</summary>
    internal static void GrantWrite(Host host, Guid? invoiceId = null) =>
        host.Rig.Probe.Grant("sprk_invoices", invoiceId ?? InvoiceId, AccessRights.Read | AccessRights.Write);

    /// <summary>A multipart body with one file part (<paramref name="field"/>, default <c>file</c>).</summary>
    internal static MultipartFormDataContent OneFile(byte[] bytes, string name = "timesheet.csv", string? contentType = "text/csv", string field = "file")
    {
        var part = new ByteArrayContent(bytes);
        if (contentType is not null)
        {
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return new MultipartFormDataContent { { part, field, name } };
    }

    /// <summary>A multipart body with two file parts, both named <c>file</c>.</summary>
    internal static MultipartFormDataContent TwoFiles()
    {
        var form = OneFile(Small, "a.csv");
        form.Add(new ByteArrayContent(Small) { Headers = { ContentType = MediaTypeHeaderValue.Parse("text/csv") } }, "file", "b.csv");
        return form;
    }

    /// <summary>Nothing reached storage and nothing was written to Dataverse.</summary>
    internal static void AssertNothingStored(Host host)
    {
        host.Rig.Gateway.StorageCalls.Should().Be(0, "no byte is staged");
        host.Rig.Dataverse.Writes.Should().Be(0, "no row is written");
    }
}

/// <summary>
/// The wire contract of <c>POST /api/finance/invoices/{invoiceId}/files</c> (ebilling-r1 task 123; <c>projects/ebilling-r1/notes/123-upload-contract.md</c>,
/// pinned for word-add-in-r1): the route, the 201 shape and <c>Location</c>, and every row of the error table with its status, its code field
/// (<c>errorCode</c> for the route's own refusals, <c>reasonCode</c> for the filter's; a client reads <c>errorCode ?? reasonCode</c>) and its
/// <c>Retry-After</c>. The REAL route, filters in their order (step 0, the Finance filter, the kill switch) and the REAL file store; doubles
/// only at Dataverse, the blob gateway and the caller-rights answer (<see cref="StoredFileEndpointsTests.Host"/>).
/// </summary>
/// <remarks>Maintain-class (ADR-038): every field name, status, code and header here is a client contract (word-add-in-r1).</remarks>
public sealed class InvoiceFileUploadContractTests
{
    private static readonly byte[] Bytes = new byte[40211];
    private static readonly Guid InvoiceId = Guid.Parse("1a000000-0000-4000-8000-0000000000f0");

    [Fact(DisplayName = "Contract 201: Location /api/finance/files/{fileId}/content and exactly { fileId, fileName, sizeBytes }")]
    public async Task Created_Shape()
    {
        await using var host = await Host.StartAsync();
        U.SeedInvoice(host);
        U.GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, U.OneFile(Bytes, "timesheet.csv", "text/csv"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Select(p => p.Key).Should().BeEquivalentTo(["fileId", "fileName", "sizeBytes"]);
        var fileId = body["fileId"]!.GetValue<Guid>();
        fileId.Should().NotBeEmpty();
        body["fileName"]!.GetValue<string>().Should().Be("timesheet.csv");
        body["sizeBytes"]!.GetValue<long>().Should().Be(40211);
        response.Headers.Location!.OriginalString.Should().Be($"/api/finance/files/{fileId}/content");
    }

    public static IEnumerable<object[]> RouteRefusals =>
    [
        ["no file part", 400, "ebilling.files.file_required", false],
        ["two file parts", 400, "ebilling.files.file_required", false],
        ["empty file", 400, "ebilling.files.file_empty", false],
        ["over the limit", 413, "ebilling.files.file_too_large", false],
        ["inactive invoice", 409, "ebilling.invoice.inactive", false],
        ["submission files pending", 409, "ebilling.invoice.files_pending", true],
        ["file limit reached", 409, "ebilling.files.limit_reached", false],
        ["store failed", 503, "file_store_unavailable", true],
        ["eBilling off", 503, "ebilling.disabled", false],
    ];

    [Theory(DisplayName = "Contract: the route's own refusals carry errorCode (and Retry-After only on the store's 503 and files_pending)")]
    [MemberData(nameof(RouteRefusals))]
    public async Task RouteRefusal_Shape(string when, int status, string errorCode, bool retryAfter)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: when != "eBilling off", maxSubmissionFileSizeMb: 1);
        U.SeedInvoice(host, when == "inactive invoice" ? InvoiceLifecycleStatus.Rejected : InvoiceLifecycleStatus.InReview);
        U.GrantWrite(host);
        var invoice = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceId);
        if (when == "store failed")
        {
            host.Rig.Gateway.FailOp["create"] = new Azure.RequestFailedException(500, "storage fault");
        }
        else if (when == "submission files pending")
        {
            host.Rig.SeedFile(Guid.NewGuid(), invoice, "%PDF-1.7"u8.ToArray(), StoredFileStatus.NotYetServable, StoredFilePurpose.InvoicePdf);
        }
        else if (when == "file limit reached")
        {
            while (host.Rig.Dataverse.Rows.Count < Sprk.Bff.Api.Api.Finance.StoredFileEndpoints.MaxListedFiles)
            {
                host.Rig.SeedFile(Guid.NewGuid(), invoice, Bytes, StoredFileStatus.Servable, StoredFilePurpose.InvoiceSupportingFile);
            }
        }

        HttpContent content = when switch
        {
            "no file part" => new MultipartFormDataContent { { new StringContent("x"), "note" } },
            "two file parts" => U.TwoFiles(),
            "empty file" => U.OneFile([]),
            "over the limit" => U.OneFile(new byte[(1024 * 1024) + 1]),
            _ => U.OneFile(Bytes),
        };

        var response = await host.PostAsync(InvoiceId, content);

        ((int)response.StatusCode).Should().Be(status, when);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["errorCode"]!.GetValue<string>().Should().Be(errorCode, when);
        (response.Headers.RetryAfter is not null).Should().Be(retryAfter, when);
    }

    public static IEnumerable<object[]> OwnerRefusals =>
    [
        [RecordOwnerRefusal.NoOwnerSource, 409],
        [RecordOwnerRefusal.DirectTeamUnavailable, 503],
    ];

    [Theory(DisplayName = "Contract (fix round 1): no owner resolves for the file → the platform's owner refusal, reasonCode the refusal code: 409, or 503 for its one transient code; nothing stored")]
    [MemberData(nameof(OwnerRefusals))]
    public async Task OwnerRefusal_Shape(string refusalCode, int status)
    {
        await using var host = await Host.StartAsync();
        U.SeedInvoice(host);
        U.GrantWrite(host);
        host.Rig.OwnerAnswer = RecordOwnerResolution.Refused(refusalCode, "no owner");

        var response = await host.PostAsync(InvoiceId, U.OneFile(Bytes));

        ((int)response.StatusCode).Should().Be(status, refusalCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["reasonCode"]!.GetValue<string>().Should().Be(refusalCode);
        body["errorCode"].Should().BeNull();
        host.Rig.Dataverse.Writes.Should().Be(0, "no row");
    }

    [Fact(DisplayName = "Contract (fix round 1): the filter found Write but the upload's own rights check got no answer → 403 reasonCode system_failure (retry), never the 404")]
    public async Task BackstopRefusal_Shape()
    {
        await using var host = await Host.StartAsync();
        U.SeedInvoice(host);
        U.GrantWrite(host);
        host.Rig.Probe.SingleQuestionAnswersNone.Add(("sprk_invoices", InvoiceId));

        var response = await host.PostAsync(InvoiceId, U.OneFile(Bytes));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.error.system_failure");
        body["errorCode"].Should().BeNull();
        U.AssertNothingStored(host);
    }

    public static IEnumerable<object[]> FilterRefusals =>
    [
        ["flagged external", 403, "sdap.access.deny.internal_callers_only"],
        ["step 0 fault", 403, "sdap.access.error.system_failure"],
        ["rights not answered", 403, "sdap.access.error.system_failure"],
        ["no Write", 404, "sdap.access.deny.record_unavailable"],
        ["unknown invoice", 404, "sdap.access.deny.record_unavailable"],
    ];

    [Theory(DisplayName = "Contract: the filter's refusals carry reasonCode")]
    [MemberData(nameof(FilterRefusals))]
    public async Task FilterRefusal_Shape(string when, int status, string reasonCode)
    {
        await using var host = await Host.StartAsync();
        U.SeedInvoice(host);
        var target = InvoiceId;
        switch (when)
        {
            case "flagged external":
                U.GrantWrite(host);
                host.StepZero.Kind = StepZeroCallerKind.FlagTrue;
                break;
            case "step 0 fault":
                U.GrantWrite(host);
                host.StepZero.Kind = StepZeroCallerKind.FlagReadFault;
                break;
            case "rights not answered":
                host.Rig.Probe.Unknown.Add(("sprk_invoices", InvoiceId));
                break;
            case "no Write":
                host.Rig.Probe.Grant("sprk_invoices", InvoiceId, AccessRights.Read);
                break;
            default:
                target = Guid.NewGuid();
                break;
        }

        var response = await host.PostAsync(target, U.OneFile(Bytes));

        ((int)response.StatusCode).Should().Be(status, when);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["reasonCode"]!.GetValue<string>().Should().Be(reasonCode, when);
        body["errorCode"].Should().BeNull(when);
        U.AssertNothingStored(host);
    }

    [Fact(DisplayName = "Contract: no Write and an unknown invoice answer the SAME 404 (status, content type and body but the per-request ids)")]
    public async Task NoWrite_And_Unknown_ByteIdentical()
    {
        await using var host = await Host.StartAsync();
        U.SeedInvoice(host);
        host.Rig.Probe.Grant("sprk_invoices", InvoiceId, AccessRights.Read);

        var noWrite = await host.PostAsync(InvoiceId, U.OneFile(Bytes));
        var unknown = await host.PostAsync(Guid.NewGuid(), U.OneFile(Bytes));

        noWrite.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Shape(noWrite)).Should().Be(await Shape(unknown));
    }

    [Fact(DisplayName = "Contract 401: unauthenticated")]
    public async Task Unauthenticated_401()
    {
        await using var host = await Host.StartAsync();

        (await host.PostAsync(InvoiceId, U.OneFile(Bytes), authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A 404's comparable shape: status, content type and the body without its per-request correlation fields.</summary>
    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType}|{node.ToJsonString()}";
    }
}
