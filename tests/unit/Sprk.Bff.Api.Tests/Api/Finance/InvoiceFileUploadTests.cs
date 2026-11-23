using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.InvoiceFileUploadSeed;
using Host = Sprk.Bff.Api.Tests.Api.Finance.StoredFileEndpointsTests.Host;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// <c>POST /api/finance/invoices/{invoiceId}/files</c> (ebilling-r1 task 123, EB-D-161) through the REAL <c>MapStoredFileEndpoints</c>, the
/// Finance authorization filter (<c>FinanceDenial.UniformNotFoundForRecord</c>), step 0, the kill switch, <see cref="StoredFileService"/>
/// and <see cref="StoredFileBlobs"/>, on the same hosts and fakes as <see cref="StoredFileEndpointsTests"/>. What the store writes and what
/// every refusal leaves behind; the wire shapes are pinned by <c>InvoiceFileUploadContractTests</c>.
/// </summary>
public sealed class InvoiceFileUploadTests
{
    private static readonly byte[] Csv = Encoding.UTF8.GetBytes("date,hours\n2026-10-01,7.5\n");

    public static IEnumerable<object[]> RefusedKinds => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    public static IEnumerable<object[]> InactiveStatuses =>
    [
        [InvoiceLifecycleStatus.Paid], [InvoiceLifecycleStatus.Rejected], [InvoiceLifecycleStatus.ReturnedToFirm],
        [InvoiceLifecycleStatus.Cancelled], [InvoiceLifecycleStatus.Superseded],
    ];

    public static IEnumerable<object[]> ActiveStatuses =>
        [[InvoiceLifecycleStatus.InReview], [InvoiceLifecycleStatus.Approved], [InvoiceLifecycleStatus.SentToAp]];

    // =============================================================================================================================
    // 201: what is stored
    // =============================================================================================================================

    [Fact(DisplayName = "Write on an Active invoice → 201: a Servable Stored File, purpose Invoice supporting file, Firm-visible No, parent the invoice, name, type and bytes as given; no sprk_document")]
    public async Task Created_StoredAsSupportingFile()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile(Csv, "timesheet.csv", "text/csv"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var fileId = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["fileId"]!.GetValue<Guid>();
        var row = host.Rig.Dataverse.Rows[fileId];
        row.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose).Value.Should().Be((int)StoredFilePurpose.InvoiceSupportingFile);
        row.GetAttributeValue<bool>(StoredFileSchema.FirmVisible).Should().BeFalse();
        row.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value.Should().Be((int)StoredFileStatus.Servable);
        row.GetAttributeValue<EntityReference>(StoredFileSchema.InvoiceTable).Id.Should().Be(InvoiceId);
        row.GetAttributeValue<EntityReference>(StoredFileSchema.ProcedureVersionTable).Should().BeNull();
        row.GetAttributeValue<EntityReference>(StoredFileSchema.SourceDocument).Should().BeNull();
        row.GetAttributeValue<string>(StoredFileSchema.Name).Should().Be("timesheet.csv");
        row.GetAttributeValue<string>(StoredFileSchema.ContentType).Should().Be("text/csv");
        row.GetAttributeValue<int>(StoredFileSchema.SizeBytes).Should().Be(Csv.Length);

        var blob = host.Rig.Gateway.Get(StoredFileSchema.ContainerOf(StoredFilePurpose.InvoiceSupportingFile),
            StoredFileBlobName.Build(StoredFileRig.Tenant, new StoredFileParent(StoredFileParentKind.Invoice, InvoiceId), fileId));
        blob.Should().NotBeNull();
        blob!.Content.Should().Equal(Csv);

        host.Rig.Dataverse.Calls.Where(c => c.StartsWith("create", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().StartWith($"create {StoredFileSchema.Entity} ", "one Stored File row and never a sprk_document");
    }

    [Fact(DisplayName = "A file part with no content type is stored as application/octet-stream")]
    public async Task NoContentType_StoredAsOctetStream()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile(Csv, "notes.bin", contentType: null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var fileId = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["fileId"]!.GetValue<Guid>();
        host.Rig.Dataverse.Rows[fileId].GetAttributeValue<string>(StoredFileSchema.ContentType).Should().Be("application/octet-stream");
    }

    [Theory(DisplayName = "Every Active status (In Review, Approved, Sent to AP) accepts a file")]
    [MemberData(nameof(ActiveStatuses))]
    public async Task ActiveStatus_Accepted(InvoiceLifecycleStatus status)
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host, status);
        GrantWrite(host);

        (await host.PostAsync(InvoiceId, OneFile(Csv))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "The caller's Write is asked as the caller (its own token), by the filter and again by the upload's backstop")]
    public async Task Write_AskedAsTheCaller()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);

        (await host.PostAsync(InvoiceId, OneFile(Csv))).StatusCode.Should().Be(HttpStatusCode.Created);

        host.Rig.Probe.Calls.Should().ContainSingle(c => c.Set == "sprk_invoices" && c.Id == InvoiceId)
            .Which.Token.Should().Be("caller-token", "the backstop asks the gate variant with the caller's own token, never app-only");
    }

    // =============================================================================================================================
    // Access refusals: nothing read beyond the rights, nothing staged
    // =============================================================================================================================

    [Fact(DisplayName = "No Write (Read and Append are not Write) → 404 before the invoice or the body is read; nothing staged, no row")]
    public async Task NoWrite_404_NothingStaged()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        host.Rig.Probe.Grant("sprk_invoices", InvoiceId, AccessRights.Read | AccessRights.Append | AccessRights.AppendTo);

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        AssertNothingStored(host);
        host.Rig.Dataverse.Calls.Should().BeEmpty("the filter refuses before the handler reads the invoice");
    }

    [Fact(DisplayName = "An unknown invoice → the same 404; nothing staged")]
    public async Task UnknownInvoice_404_NothingStaged()
    {
        await using var host = await Host.StartAsync();

        var response = await host.PostAsync(Guid.NewGuid(), OneFile(Csv));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        AssertNothingStored(host);
    }

    [Fact(DisplayName = "Rights Dataverse did not answer (a faulted answer without Write) → 403 system_failure, not the 404; nothing staged")]
    public async Task UnknownRights_403SystemFailure()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        host.Rig.Probe.Unknown.Add(("sprk_invoices", InvoiceId));

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.error.system_failure");
        AssertNothingStored(host);
    }

    [Fact(DisplayName = "A faulted answer that still carries Write is held (the degraded-answer rule): 201")]
    public async Task FaultedButHoldsWrite_Created()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);
        host.Rig.Probe.Unknown.Add(("sprk_invoices", InvoiceId));

        (await host.PostAsync(InvoiceId, OneFile(Csv))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory(DisplayName = "Step 0 refuses a flagged-external, row-less or unreadable caller with 403 before any record read, even with Write")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_Refuses_403(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);
        host.StepZero.Kind = kind;

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.AssertNothingRead();
        host.Rig.Dataverse.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "Unauthenticated → 401, nothing read or staged")]
    public async Task Unauthenticated_401()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile(Csv), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.AssertNothingRead();
    }

    [Fact(DisplayName = "eBilling off: an authorized caller gets 503 ebilling.disabled and nothing is staged; a caller with no Write still gets the 404")]
    public async Task FlagOff()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        SeedInvoice(host);

        (await host.PostAsync(InvoiceId, OneFile(Csv))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        GrantWrite(host);
        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        AssertNothingStored(host);
    }

    // =============================================================================================================================
    // Inactive invoices
    // =============================================================================================================================

    [Theory(DisplayName = "Each Inactive status (Paid, Rejected, Returned to Firm, Cancelled, Superseded) → 409 ebilling.invoice.inactive; nothing staged")]
    [MemberData(nameof(InactiveStatuses))]
    public async Task InactiveInvoice_409_NothingStaged(InvoiceLifecycleStatus status)
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host, status);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.invoice.inactive");
        AssertNothingStored(host);
    }

    [Fact(DisplayName = "An invoice whose state cannot be read, or that has no state → 503 (fail closed, never read as Active); nothing staged")]
    public async Task InvoiceStateUnreadable_503()
    {
        await using var host = await Host.StartAsync();
        GrantWrite(host);
        host.Rig.Dataverse.AddOther(StoredFileSchema.InvoiceTable, InvoiceId);

        var noState = await host.PostAsync(InvoiceId, OneFile(Csv));
        noState.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        host.Rig.Dataverse.RetrieveFault = new TimeoutException("Dataverse slow");
        var fault = await host.PostAsync(InvoiceId, OneFile(Csv));
        fault.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await fault.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_store_unavailable");

        AssertNothingStored(host);
    }

    [Fact(DisplayName = "An invoice gone between the rights check and the state read → the same uniform 404")]
    public async Task InvoiceGoneAfterRightsCheck_404()
    {
        await using var host = await Host.StartAsync();
        GrantWrite(host); // rights answered, but no invoice row to read

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        AssertNothingStored(host);
    }

    // =============================================================================================================================
    // Request shape: refused before anything is staged
    // =============================================================================================================================

    public static IEnumerable<object[]> BadShapes => [["no-file-field"], ["two-files"], ["other-name"], ["not-multipart"], ["no-body"]];

    [Theory(DisplayName = "A missing file field, two file parts, a part not named file, a body that is not multipart, no body → 400 ebilling.files.file_required; nothing staged")]
    [MemberData(nameof(BadShapes))]
    public async Task BadShape_400_FileRequired(string shape)
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);

        HttpContent? content = shape switch
        {
            "no-file-field" => new MultipartFormDataContent { { new StringContent("hello"), "note" } },
            "two-files" => TwoFiles(),
            "other-name" => OneFile(Csv, field: "attachment"),
            "not-multipart" => new StringContent("{\"file\":\"x\"}", Encoding.UTF8, "application/json"),
            _ => null,
        };

        var response = await host.PostAsync(InvoiceId, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, shape);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.files.file_required");
        AssertNothingStored(host);
    }

    [Fact(DisplayName = "An empty file → 400 ebilling.files.file_empty; nothing staged")]
    public async Task EmptyFile_400()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile([]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.files.file_empty");
        AssertNothingStored(host);
    }

    [Fact(DisplayName = "Over Ebilling:MaxSubmissionFileSizeMb by one byte → 413 ebilling.files.file_too_large; exactly the limit is accepted")]
    public async Task OverLimit_413_AtLimit_Created()
    {
        await using var host = await Host.StartAsync(maxSubmissionFileSizeMb: 1);
        SeedInvoice(host);
        GrantWrite(host);
        const int limit = 1024 * 1024;

        var over = await host.PostAsync(InvoiceId, OneFile(new byte[limit + 1]));

        over.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        JsonNode.Parse(await over.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.files.file_too_large");
        AssertNothingStored(host);

        (await host.PostAsync(InvoiceId, OneFile(new byte[limit]))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "A Content-Length far over the limit → 413 before the body is parsed; nothing staged")]
    public async Task ContentLengthOverLimit_413()
    {
        await using var host = await Host.StartAsync(maxSubmissionFileSizeMb: 1);
        SeedInvoice(host);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile(new byte[3 * 1024 * 1024]));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        AssertNothingStored(host);
    }

    [Fact(DisplayName = "A content type longer than the column → 400 file_required (the store's own name and type rule, decided before any write)")]
    public async Task ContentTypeTooLong_400()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile(Csv, contentType: "text/" + new string('x', 300)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AssertNothingStored(host);
    }

    // =============================================================================================================================
    // Store failures: 503 with Retry-After, nothing Servable left
    // =============================================================================================================================

    [Fact(DisplayName = "Staging faults → 503 file_store_unavailable with Retry-After; no row")]
    public async Task StagingFault_503()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);
        host.Rig.Gateway.FailOp["create"] = new Azure.RequestFailedException(500, "storage fault");

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        await AssertStoreUnavailableAsync(response);
        host.Rig.Dataverse.Rows.Should().BeEmpty();
    }

    [Fact(DisplayName = "The row write faults → 503 with Retry-After; the staged blob is deleted, no row")]
    public async Task RowWriteFault_503_BlobDeleted()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);
        host.Rig.Dataverse.CreateFault = new InvalidOperationException("Dataverse refused the create");

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        await AssertStoreUnavailableAsync(response);
        host.Rig.Dataverse.Rows.Should().BeEmpty();
        host.Rig.Gateway.Names.Should().BeEmpty("the bytes this attempt staged are deleted");
    }

    [Fact(DisplayName = "The flip is refused (the row changed) → 503 with Retry-After; the row stays Not yet servable, never Servable")]
    public async Task FlipRefused_503_RowNotServable()
    {
        await using var host = await Host.StartAsync();
        SeedInvoice(host);
        GrantWrite(host);
        host.Rig.Dataverse.BumpVersionBeforeNextUpdate = true;

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        await AssertStoreUnavailableAsync(response);
        host.Rig.Dataverse.Rows.Values.Should().OnlyContain(r =>
            r.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value != (int)StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "Store not configured → 503 file_store_unavailable before anything is written")]
    public async Task NotConfigured_503()
    {
        await using var host = await Host.StartAsync(configured: false);
        SeedInvoice(host);
        GrantWrite(host);

        var response = await host.PostAsync(InvoiceId, OneFile(Csv));

        await AssertStoreUnavailableAsync(response);
        host.Rig.Dataverse.Writes.Should().Be(0);
    }

    // =============================================================================================================================

    private static async Task AssertStoreUnavailableAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(StoredFileEndpoints.UploadRetryAfterSeconds));
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_store_unavailable");
    }
}
