using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Azure;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// <c>GET /api/finance/files/{fileId}/content</c> through the REAL <c>MapStoredFileEndpoints</c>, <see cref="StoredFileAccessFilter"/>,
/// step 0 (099's definition over the shared Dataverse seam), <see cref="StoredFileService"/>, <see cref="StoredFileBlobs"/> and
/// <c>EbillingEnabledFilter</c> (ebilling-r1 task 090; procedure-and-file-model 4.3, 5.3; uac-r2 Q5, F1-3, F1-e, F4-2). Substituted only
/// at the blob gateway, <see cref="Sprk.Bff.Api.Services.Dataverse.IGenericEntityService"/> and the caller-rights answer.
/// </summary>
public sealed class StoredFileEndpointsTests
{
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 the invoice bytes");
    private static readonly Guid InvoiceId = Guid.Parse("1a000000-0000-4000-8000-0000000000f0");
    private static readonly StoredFileParent Invoice = new(StoredFileParentKind.Invoice, InvoiceId);

    private static string Url(Guid id) => $"/api/finance/files/{id}/content";

    public static IEnumerable<object[]> RefusedKinds => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    public static IEnumerable<object[]> InternalKinds => StatedCaller.InternalKinds.Select(k => new object[] { k });

    // =============================================================================================================================
    // Authentication and step 0: (a)-(c), before any row read
    // =============================================================================================================================

    [Fact(DisplayName = "Unauthenticated → 401, nothing read")]
    public async Task Unauthenticated_401()
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();

        var response = await host.SendAsync(Url(file), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.AssertNothingRead();
    }

    [Theory(DisplayName = "Must-fire (a)-(c): flagged-external, no systemuser row, flag-read fault (and lookup fault) → 403 before any row read or rights call, even with Read")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_Refuses_BeforeAnyRead(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();
        host.GrantParentRead();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Url(file));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.AssertNothingRead();
    }

    [Fact(DisplayName = "eBilling off: a step-0-refused caller still gets 403 (the flag never replaces authorization)")]
    public async Task FlagOff_RefusedCaller_403()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        var file = host.SeedServable();
        host.StepZero.Kind = StepZeroCallerKind.FlagTrue;

        (await host.SendAsync(Url(file))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "eBilling off: an authorized caller gets 503 ebilling.disabled and no bytes")]
    public async Task FlagOff_AuthorizedCaller_503()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        var file = host.SeedServable();
        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Rig.Gateway.Calls.Should().NotContain(c => c.StartsWith("open", StringComparison.Ordinal));
        host.AuditEvents.Should().BeEmpty();
    }

    // =============================================================================================================================
    // (d)-(f): every 404 is the unknown id's 404
    // =============================================================================================================================

    [Fact(DisplayName = "Unknown id → the uniform 404 (no id in the body), after step 0, with no rights call and no storage call")]
    public async Task UnknownId_Uniform404()
    {
        await using var host = await Host.StartAsync();
        var unknown = Guid.NewGuid();

        var response = await host.SendAsync(Url(unknown));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(unknown.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        host.Rig.Probe.Calls.Should().BeEmpty();
        host.Rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Must-fire (d): no Read on the parent (Write and Append are not Read) → 404 identical to an unknown id's; no storage call")]
    public async Task NoParentRead_404()
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();
        host.Rig.Probe.Grant("sprk_invoices", InvoiceId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync(Url(file));

        (await Shape(response)).Should().Be(await host.UnknownShapeAsync());
        host.Rig.Probe.Calls.Should().ContainSingle().Which.Should().Be(("sprk_invoices", InvoiceId, "caller-token"));
        host.Rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Must-fire (d): Read on ANOTHER invoice is not Read on this file's parent")]
    public async Task ReadOnAnotherParent_404()
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();
        host.Rig.Probe.Grant("sprk_invoices", Guid.NewGuid(), AccessRights.Read);

        (await Shape(await host.SendAsync(Url(file)))).Should().Be(await host.UnknownShapeAsync());
    }

    [Fact(DisplayName = "Must-fire (e): a copy (source document set) the caller cannot Read at its source → 404 identical to an unknown id's")]
    public async Task Copy_NoSourceRead_404()
    {
        await using var host = await Host.StartAsync();
        var document = Guid.NewGuid();
        var file = host.SeedServable(sourceDocument: document);
        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        (await Shape(response)).Should().Be(await host.UnknownShapeAsync());
        host.Rig.Probe.Calls.Should().Contain(("sprk_documents", document, "caller-token"));
        host.Rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Must-not-fire (e): a copy whose source the caller CAN Read is served")]
    public async Task Copy_WithSourceRead_200()
    {
        await using var host = await Host.StartAsync();
        var document = Guid.NewGuid();
        var file = host.SeedServable(sourceDocument: document);
        host.GrantParentRead();
        host.Rig.Probe.Grant("sprk_documents", document, AccessRights.Read);

        (await host.SendAsync(Url(file))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public static IEnumerable<object[]> NotServableCases =>
    [
        ["not-yet-servable"], ["removed"], ["malicious"], ["path-other-parent"], ["path-other-file"], ["path-other-tenant"], ["blob-missing"],
    ];

    [Theory(DisplayName = "Must-fire (f): Not yet servable, Removed, Malicious, a path that is not the row's own, or a missing blob → 404 identical to an unknown id's")]
    [MemberData(nameof(NotServableCases))]
    public async Task NotServable_404(string kind)
    {
        await using var host = await Host.StartAsync();
        var file = Guid.NewGuid();
        var otherTenant = Guid.Parse("0ff1e000-0000-4000-8000-000000000999");
        switch (kind)
        {
            case "not-yet-servable":
                host.Rig.SeedFile(file, Invoice, Pdf, StoredFileStatus.NotYetServable);
                break;
            case "removed":
                host.Rig.SeedFile(file, Invoice, Pdf, StoredFileStatus.Removed);
                break;
            case "malicious":
                host.Rig.SeedFile(file, Invoice, Pdf, tags: new Dictionary<string, string>
                {
                    [StoredFileBlobs.ScanResultTagKey] = StoredFileBlobs.ScanMaliciousValue,
                    [StoredFileBlobs.ScanTimeTagKey] = host.Rig.Gateway.Now.AddMinutes(1).ToString("O"),
                });
                break;
            case "path-other-parent":
                host.Rig.SeedFile(file, Invoice, Pdf, blobPathOverride: StoredFileBlobName.Build(StoredFileRig.Tenant, new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid()), file));
                break;
            case "path-other-file":
                var other = host.SeedServable();
                host.Rig.SeedFile(file, Invoice, Pdf, blobPathOverride: StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, other));
                break;
            case "path-other-tenant":
                host.Rig.SeedFile(file, Invoice, Pdf, blobPathOverride: StoredFileBlobName.Build(otherTenant, Invoice, file));
                break;
            case "blob-missing":
                host.Rig.SeedFile(file, Invoice, Pdf);
                await host.Rig.Gateway.DeleteIfExistsAsync("invoice-files", StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file), ifMatch: null, CancellationToken.None);
                break;
        }

        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        (await Shape(response)).Should().Be(await host.UnknownShapeAsync(), kind);
        host.Rig.Gateway.Calls.Should().NotContain(c => c.StartsWith("open", StringComparison.Ordinal));
        host.AuditEvents.Should().BeEmpty();
    }

    // =============================================================================================================================
    // (g) scan pending, (h) faults
    // =============================================================================================================================

    public static IEnumerable<object[]> PendingCases => [["absent"], ["error"], ["no-time"], ["scan-before-last-modified"]];

    [Theory(DisplayName = "Must-fire (g): tag absent, another result, no scan time, or a scan older than Last-Modified → 409 file_scan_pending")]
    [MemberData(nameof(PendingCases))]
    public async Task ScanPending_409(string kind)
    {
        await using var host = await Host.StartAsync();
        var file = Guid.NewGuid();
        var now = host.Rig.Gateway.Now;
        var tags = kind switch
        {
            "absent" => new Dictionary<string, string>(),
            "error" => new Dictionary<string, string> { [StoredFileBlobs.ScanResultTagKey] = "Error", [StoredFileBlobs.ScanTimeTagKey] = now.AddMinutes(1).ToString("O") },
            "no-time" => new Dictionary<string, string> { [StoredFileBlobs.ScanResultTagKey] = StoredFileBlobs.ScanCleanValue },
            _ => StoredFileRig.CleanTags(now.AddMinutes(1)),
        };
        host.Rig.SeedFile(file, Invoice, Pdf, tags: tags);
        if (kind == "scan-before-last-modified")
        {
            // Tagged clean, then written later (the BFF keeps blob write): the result tag is stale.
            host.Rig.Gateway.Overwrite("invoice-files", StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file), Pdf, now.AddMinutes(2));
        }

        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_scan_pending");
        host.AuditEvents.Should().BeEmpty();
    }

    [Fact(DisplayName = "Must-fire (g): content written between the verdict and the open (ETag changed) → 409, no bytes, no audit event")]
    public async Task ChangedAfterVerdict_409()
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();
        host.GrantParentRead();
        var path = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file);
        host.Rig.Gateway.BeforeOp = (op, _) =>
        {
            if (op == "open")
            {
                host.Rig.Gateway.Overwrite("invoice-files", path, Encoding.UTF8.GetBytes("swapped"), host.Rig.Gateway.Now);
            }
        };

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("swapped");
        host.AuditEvents.Should().BeEmpty();
    }

    public static IEnumerable<object[]> FaultCases => [["row"], ["props"], ["tags"], ["open"]];

    [Theory(DisplayName = "Must-fire (h): a row read or storage read fault → 503 file_store_unavailable, no bytes, no audit event")]
    [MemberData(nameof(FaultCases))]
    public async Task ReadFault_503(string where)
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();
        host.GrantParentRead();
        if (where == "row")
        {
            host.Rig.Dataverse.RetrieveFault = new TimeoutException("Dataverse slow");
        }
        else
        {
            host.Rig.Gateway.FailOp[where] = new RequestFailedException(500, "storage fault");
        }

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_store_unavailable");
        host.AuditEvents.Should().BeEmpty();
    }

    [Fact(DisplayName = "Store not configured → 503 file_store_unavailable after the caller checks")]
    public async Task NotConfigured_503()
    {
        await using var host = await Host.StartAsync(configured: false);
        var file = Guid.NewGuid();
        host.Rig.Dataverse.AddRow(new Microsoft.Xrm.Sdk.Entity(StoredFileSchema.Entity, file)
        {
            [StoredFileSchema.Purpose] = new Microsoft.Xrm.Sdk.OptionSetValue((int)StoredFilePurpose.InvoicePdf),
            [StoredFileSchema.Status] = new Microsoft.Xrm.Sdk.OptionSetValue((int)StoredFileStatus.Servable),
            ["sprk_invoice"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_invoice", InvoiceId),
            [StoredFileSchema.BlobPath] = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file),
        });
        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_store_unavailable");
        host.Rig.Probe.Calls.Should().ContainSingle("the caller checks run first");
        host.AuditEvents.Should().BeEmpty();
    }

    // =============================================================================================================================
    // Must-not-fire: 200
    // =============================================================================================================================

    [Theory(DisplayName = "Must-not-fire: an internal caller (flag false; flag blank) with Read, Servable, Clean → 200, the row's content type and bytes, one audit event before the first byte")]
    [MemberData(nameof(InternalKinds))]
    public async Task Internal_200(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;
        var file = host.SeedServable(contentType: "application/pdf");
        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("sandbox");

        var audit = host.AuditEvents.Should().ContainSingle().Subject;
        audit.Message.Should().Contain(file.ToString()).And.Contain("sprk_invoice").And.Contain(InvoiceId.ToString())
            .And.Contain("systemuser").And.Contain(StatedCaller.SystemUserId.ToString()).And.Contain("route internal");
        host.Timeline.Should().Equal("audit", "first-byte");
    }

    [Fact(DisplayName = "Must-not-fire: with MalwareScanningEnabled explicitly false an untagged file is served")]
    public async Task ScanningOff_UntaggedServed()
    {
        await using var host = await Host.StartAsync(malwareScanningEnabled: false);
        var file = Guid.NewGuid();
        host.Rig.SeedFile(file, Invoice, Pdf, tags: new Dictionary<string, string>());
        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
    }

    [Fact(DisplayName = "MalwareScanningEnabled absent (not false) → an untagged file is NOT served (409)")]
    public async Task ScanningAbsent_UntaggedRefused()
    {
        await using var host = await Host.StartAsync(malwareScanningEnabled: null);
        var file = Guid.NewGuid();
        host.Rig.SeedFile(file, Invoice, Pdf, tags: new Dictionary<string, string>());
        host.GrantParentRead();

        (await host.SendAsync(Url(file))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory(DisplayName = "Content type (review pass 1): allow-listed types are served as themselves; HTML, SVG, XML, script or a malformed value as an octet-stream attachment")]
    [InlineData("application/pdf", "application/pdf", false)]
    [InlineData("text/plain; charset=utf-8", "text/plain", false)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    [InlineData("IMAGE/PNG", "image/png", false)]
    [InlineData("text/html; charset=utf-8", "application/octet-stream", true)]
    [InlineData("image/svg+xml", "application/octet-stream", true)]
    [InlineData("application/xhtml+xml", "application/octet-stream", true)]
    [InlineData("application/xml", "application/octet-stream", true)]
    [InlineData("text/javascript", "application/octet-stream", true)]
    [InlineData("not a type", "application/octet-stream", true)]
    [InlineData("", "application/octet-stream", true)]
    public async Task ContentType_Safe(string stored, string served, bool attachment)
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable(contentType: stored);
        host.GrantParentRead();

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(served);
        (response.Content.Headers.ContentDisposition?.DispositionType == "attachment").Should().Be(attachment);
    }

    [Fact(DisplayName = "Must-fire (review pass 1, copy rule): a source communication without a source document → 404 identical to an unknown id's")]
    public async Task CommunicationOnlyCopy_404()
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();
        host.Rig.Dataverse.Rows[file][StoredFileSchema.SourceCommunication] =
            new Microsoft.Xrm.Sdk.EntityReference(StoredFileSchema.CommunicationTable, Guid.NewGuid());
        host.GrantParentRead();

        (await Shape(await host.SendAsync(Url(file)))).Should().Be(await host.UnknownShapeAsync());
        host.Rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Must-fire (h, review pass 1): a non-storage fault opening the blob (I/O, transport) → 503, not 500")]
    public async Task OpenOtherFault_503()
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedServable();
        host.GrantParentRead();
        host.Rig.Gateway.FailOp["open"] = new IOException("connection reset");

        var response = await host.SendAsync(Url(file));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.AuditEvents.Should().BeEmpty();
    }

    [Fact(DisplayName = "Review pass 1: the download filter asks rights ONLY through the timing-equalized gate variant (source scan, with controls)")]
    public void RightsAskedOnlyThroughTheGateVariant()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api")))
        {
            dir = dir.Parent;
        }

        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "server", "api", "Sprk.Bff.Api", "Api", "Finance", "StoredFileEndpoints.cs"));
        var direct = new System.Text.RegularExpressions.Regex(@"\.GetCallerRights(Async|ForRecordsAsync)\s*\(");
        var gate = new System.Text.RegularExpressions.Regex(@"\.GetCallerRightsForAuthorizationGateAsync\s*\(");

        direct.IsMatch(source).Should().BeFalse("a direct GetCallerRightsAsync call would answer no-access faster than not-found");
        gate.Matches(source).Count.Should().Be(2, "the parent Read and the copy rule");

        // Controls: the patterns see what they must, and not each other.
        direct.IsMatch("await probe.GetCallerRightsAsync(token, set, id, ct)").Should().BeTrue();
        direct.IsMatch("await probe.GetCallerRightsForAuthorizationGateAsync(token, set, id, ct)").Should().BeFalse();
    }

    // =============================================================================================================================

    /// <summary>A 404's comparable shape: status, content type and the body without its per-request correlation fields.</summary>
    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType}|{node.ToJsonString()}";
    }

    internal sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public StoredFileRig Rig { get; private set; } = null!;
        public StatedCaller StepZero { get; private set; } = null!;
        public ConcurrentQueue<string> Timeline { get; } = new();
        public CapturingProvider Logs { get; private set; } = null!;

        public IReadOnlyList<(EventId EventId, string Message)> AuditEvents =>
            Logs.Entries.Where(e => e.EventId.Id == StoredFileAudit.Downloaded.Id).ToList();

        public static async Task<Host> StartAsync(
            bool ebillingEnabled = true, bool? malwareScanningEnabled = true, bool configured = true, StoredFileRig? rig = null)
        {
            // ebilling-r1 task 094: a caller may pass its own rig (the copy-rule end-to-end test serves the copy Submit Invoice committed).
            var host = new Host { Rig = rig ?? new StoredFileRig(malwareScanningEnabled, configured) };
            host.Logs = new CapturingProvider(host.Timeline);
            host.Rig.Gateway.OnFirstRead = () => host.Timeline.Enqueue("first-byte");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(host.Logs);
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
            builder.Services.AddSingleton(host.Rig.Service);
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Rig.Probe);

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapStoredFileEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public Guid SeedServable(Guid? sourceDocument = null, string contentType = "application/pdf")
        {
            var file = Guid.NewGuid();
            Rig.SeedFile(file, Invoice, Pdf, sourceDocument: sourceDocument, contentType: contentType);
            return file;
        }

        public void GrantParentRead() => Rig.Probe.Grant("sprk_invoices", InvoiceId, AccessRights.Read);

        public async Task<string> UnknownShapeAsync()
        {
            var response = await SendAsync(Url(Guid.NewGuid()));
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            return await Shape(response);
        }

        public void AssertNothingRead()
        {
            Rig.Dataverse.Calls.Should().BeEmpty("no record read before step 0 admits the caller");
            Rig.Probe.Calls.Should().BeEmpty();
            Rig.Gateway.StorageCalls.Should().Be(0);
        }

        public Task<HttpResponseMessage> SendAsync(string url, bool authenticated = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
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

    internal sealed class CapturingProvider(ConcurrentQueue<string> timeline) : ILoggerProvider
    {
        public ConcurrentQueue<(EventId EventId, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Entries.Enqueue((eventId, formatter(state, exception)));
                if (eventId.Id == StoredFileAudit.Downloaded.Id)
                {
                    owner._timeline.Enqueue("audit");
                }
            }
        }

        private ConcurrentQueue<string> _timeline => timeline;
    }
}
