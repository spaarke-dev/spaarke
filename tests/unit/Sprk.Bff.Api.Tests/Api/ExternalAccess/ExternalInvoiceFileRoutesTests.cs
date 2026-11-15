using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
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
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Tests.Finance.Files;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 093: <c>GET /api/v1/external/invoices/{id}/files</c> and <c>…/files/{fileId}/content</c> over the REAL mapping, the
/// REAL caller-principal group filter, the REAL <see cref="ExternalInvoiceSubmissionAuthorizationFilter"/> InvoiceFiles gate, the REAL
/// record gate (<see cref="ExternalMessageEndpoints.GetRecordRightsDecisionAsync"/> with the production module registry), the REAL kill
/// switch and the REAL <see cref="StoredFileService"/> (over <see cref="StoredFileRig"/>'s fakes), substituted only at Dataverse, the blob
/// store, the caller's memberships, the systemuser flag and the (a') seam.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the firm, record and file checks are the boundary of what a firm sees of an invoice. Every refusal is asserted
/// to be the same bytes as an unknown invoice's (or an unknown file's), with no storage read; every fault a 503.
/// </remarks>
public sealed class ExternalInvoiceFileRoutesTests
{
    private static readonly Guid Invoice = Guid.Parse("09300000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherInvoice = Guid.Parse("09300000-0000-4000-8000-0000000000a2");
    private static readonly Guid Matter = Guid.Parse("09300000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherMatter = Guid.Parse("09300000-0000-4000-8000-0000000000b2");
    private static readonly Guid SecureProject = Guid.Parse("09300000-0000-4000-8000-0000000000b3");
    private static readonly Guid Firm = Guid.Parse("09300000-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherFirm = Guid.Parse("09300000-0000-4000-8000-0000000000c2");
    private static readonly Guid Contact = Guid.Parse("09300000-0000-4000-8000-0000000000d1");
    private static readonly Guid OtherFirmContact = Guid.Parse("09300000-0000-4000-8000-0000000000d2");
    private static readonly Guid FlaggedSystemUser = Guid.Parse("09300000-0000-4000-8000-0000000000e1");
    private static readonly Guid InternalSystemUser = Guid.Parse("09300000-0000-4000-8000-0000000000e2");
    private static readonly byte[] Pdf = "%PDF-1.7 firm invoice"u8.ToArray();

    private static StoredFileParent InvoiceParent(Guid invoice) => new(StoredFileParentKind.Invoice, invoice);

    private static string ListUrl(Guid invoice) => $"/api/v1/external/invoices/{invoice}/files";

    private static string ContentUrl(Guid invoice, Guid file) => $"/api/v1/external/invoices/{invoice}/files/{file}/content";

    // =============================================================================================================================
    // Must not fire: the firm's own file, for each caller kind
    // =============================================================================================================================

    public static TheoryData<string> AdmittedCallers => new() { "contact", "flagged-systemuser", "internal-systemuser" };

    [Theory(DisplayName = "Must-not-fire: a firm contact, a flagged-external systemuser of the firm and an internal systemuser list the firm file and download it")]
    [MemberData(nameof(AdmittedCallers))]
    public async Task AdmittedCaller_ListsAndDownloads(string kind)
    {
        await using var host = await Host.StartAsync();
        host.CallerIs(kind);
        var file = host.SeedFirmFile();

        var list = await host.GetAsync(ListUrl(Invoice));
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        (await Ids(list)).Should().Equal(file);

        var content = await host.GetAsync(ContentUrl(Invoice, file));
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
        content.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        content.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        content.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact(DisplayName = "Must-not-fire: an internal systemuser sees an invoice whatever its firm, and no firm is read for it")]
    public async Task InternalSystemUser_IsNotFirmScoped()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("internal-systemuser");
        host.Data.Invoices[Invoice] = (Matter, OtherFirm);
        var file = host.SeedFirmFile();

        (await Ids(await host.GetAsync(ListUrl(Invoice)))).Should().Equal(file);
        host.Participations.MembershipReads.Should().Be(0);
    }

    [Fact(DisplayName = "The list row: id, fileName, contentType, sizeBytes, createdOn, scanStatus Clean; nothing else (no path, container, URL or source)")]
    public async Task ListRow_Shape()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var file = host.SeedFirmFile(name: "INV-1.pdf");

        var response = await host.GetAsync(ListUrl(Invoice));
        var body = await response.Content.ReadAsStringAsync();

        var row = JsonNode.Parse(body)!["value"]!.AsArray().Single()!.AsObject();
        row.Select(p => p.Key).Should().BeEquivalentTo("id", "fileName", "contentType", "sizeBytes", "createdOn", "scanStatus");
        row["id"]!.GetValue<Guid>().Should().Be(file);
        row["fileName"]!.GetValue<string>().Should().Be("INV-1.pdf");
        row["contentType"]!.GetValue<string>().Should().Be("application/pdf");
        row["sizeBytes"]!.GetValue<int>().Should().Be(Pdf.Length);
        row["createdOn"]!.GetValue<string>().Should().EndWith("Z");
        row["scanStatus"]!.GetValue<string>().Should().Be("Clean");
        var path = host.Rig.Dataverse.Rows[file].GetAttributeValue<string>(StoredFileSchema.BlobPath);
        body.Should().NotContain(path).And.NotContain("invoice-files").And.NotContain("http").And.NotContain("sig=");
    }

    [Fact(DisplayName = "Content: one audit event (the firm route, the caller's contact) before the first byte")]
    public async Task Content_AuditsOnceBeforeTheFirstByte()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var file = host.SeedFirmFile();

        (await host.GetAsync(ContentUrl(Invoice, file))).StatusCode.Should().Be(HttpStatusCode.OK);

        var audit = host.AuditEvents.Should().ContainSingle().Subject;
        audit.Message.Should().Contain(file.ToString()).And.Contain("sprk_invoice").And.Contain(Invoice.ToString())
            .And.Contain("contact").And.Contain(Contact.ToString()).And.Contain("route firm");
        host.Timeline.Should().Equal("audit", "first-byte");
    }

    [Fact(DisplayName = "The (a') predicate is asked once per request, for the route invoice only")]
    public async Task ThePredicate_IsAskedOnce()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        host.SeedFirmFile();

        await host.GetAsync(ListUrl(Invoice));

        host.Hiding.Calls.Should().ContainSingle().Which.Should().Equal(Invoice);
    }

    // =============================================================================================================================
    // Must fire: a caller who may not see the invoice gets the unknown invoice's 404, on both routes, and nothing is read from the store
    // =============================================================================================================================

    public static TheoryData<string> RefusedCases => new()
    {
        "other-firm-invoice",
        "contact-without-read",
        "contact-with-no-grant",
        "contact-with-no-firm",
        "flagged-systemuser-without-read",
        "flagged-systemuser-no-rights-on-the-root",
        "flagged-systemuser-contact-of-another-firm",
        "flagged-systemuser-with-no-contact",
        "internal-systemuser-without-read",
        "flagged-systemuser-with-root-read-without-invoice-read",
        "internal-systemuser-with-root-read-without-invoice-read",
        "hidden-by-the-predicate",
        "also-under-a-secure-root-the-caller-cannot-read",
    };

    [Theory(DisplayName = "Must-fire: each refusal is the unknown invoice's 404 (byte for byte) on the list and the content route, with no file read")]
    [MemberData(nameof(RefusedCases))]
    public async Task Refused_IsTheUniform404(string kind)
    {
        await using var host = await Host.StartAsync();
        var file = host.SeedFirmFile();
        Arrange(host, kind);

        var unknownList = await Shape(await host.GetAsync(ListUrl(Guid.NewGuid())));
        var unknownContent = await Shape(await host.GetAsync(ContentUrl(Guid.NewGuid(), file)));
        host.Rig.Dataverse.Calls.Clear();

        var list = await host.GetAsync(ListUrl(Invoice));
        var content = await host.GetAsync(ContentUrl(Invoice, file));

        list.StatusCode.Should().Be(HttpStatusCode.NotFound, "never an empty 200");
        (await Shape(list)).Should().Be(unknownList);
        (await Shape(content)).Should().Be(unknownContent);
        host.Rig.Dataverse.Calls.Should().BeEmpty("the file rows are read only for an invoice the caller may see");
        host.Rig.Gateway.StorageCalls.Should().Be(0);
        host.AuditEvents.Should().BeEmpty();
    }

    private static void Arrange(Host host, string kind)
    {
        switch (kind)
        {
            case "other-firm-invoice":
                host.CallerIs("contact");
                host.Data.Invoices[Invoice] = (Matter, OtherFirm);
                break;
            case "contact-without-read":
                host.Resolver.Principal = ContactPrincipal(Contact, new() { [OtherMatter] = AccessRights.Read });
                break;
            case "contact-with-no-grant":
                host.Resolver.Principal = ContactPrincipal(Contact, new());
                break;
            case "contact-with-no-firm":
                host.CallerIs("contact");
                host.Participations.FirmsOf.Remove(Contact);
                break;
            case "flagged-systemuser-without-read":
                host.Resolver.Principal = SystemUserPrincipal(FlaggedSystemUser, Contact, new() { [OtherMatter] = AccessRights.Read });
                break;
            case "flagged-systemuser-no-rights-on-the-root":
                // The evaluator's post-veto answer for a root the caller is barred from: listed, with no rights.
                host.Resolver.Principal = SystemUserPrincipal(FlaggedSystemUser, Contact, new() { [Matter] = AccessRights.None });
                break;
            case "flagged-systemuser-contact-of-another-firm":
                host.Resolver.Principal = SystemUserPrincipal(FlaggedSystemUser, OtherFirmContact, new() { [Matter] = AccessRights.Read });
                break;
            case "flagged-systemuser-with-no-contact":
                host.Resolver.Principal = SystemUserPrincipal(FlaggedSystemUser, Guid.Empty, new() { [Matter] = AccessRights.Read });
                break;
            case "internal-systemuser-without-read":
                host.Resolver.Principal = SystemUserPrincipal(InternalSystemUser, Guid.Empty, new() { [OtherMatter] = AccessRights.Read });
                break;
            case "flagged-systemuser-with-root-read-without-invoice-read":
                // Fix round 1 (A): Read on the invoice's matter, none on the invoice row itself.
                host.CallerIs("flagged-systemuser");
                host.Probe.AnswerAppOnly(FlaggedSystemUser, StoredFileSchema.InvoiceEntitySet, Invoice, AccessRights.None);
                break;
            case "internal-systemuser-with-root-read-without-invoice-read":
                host.CallerIs("internal-systemuser");
                host.Probe.AnswerAppOnly(InternalSystemUser, StoredFileSchema.InvoiceEntitySet, Invoice, AccessRights.None);
                break;
            case "hidden-by-the-predicate":
                host.CallerIs("contact");
                host.Hiding = InvoiceHidingStub.Hiding(Invoice);
                break;
            case "also-under-a-secure-root-the-caller-cannot-read":
                host.CallerIs("contact");
                host.Data.InvoiceProjects[Invoice] = SecureProject;
                host.Data.RootIsSecure[(ExternalGrantRootType.Project, SecureProject)] = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown case (test).");
        }
    }

    // =============================================================================================================================
    // Must fire: only the invoice's own firm-visible, sourceless Invoice PDF is listed and served
    // =============================================================================================================================

    public static TheoryData<string> HiddenFiles => new()
    {
        "not-firm-visible", "supporting-file", "copy-of-a-document", "copy-of-an-email", "not-yet-servable", "removed", "malicious",
        "another-invoices-file",
    };

    [Theory(DisplayName = "Must-fire: a file the firm may not see is not listed, and its content is the unknown file's 404")]
    [MemberData(nameof(HiddenFiles))]
    public async Task HiddenFile_IsNotListed_AndIsTheUniform404(string kind)
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var visible = host.SeedFirmFile();
        var hidden = kind switch
        {
            "not-firm-visible" => host.SeedFirmFile(firmVisible: false),
            "supporting-file" => host.SeedFirmFile(purpose: StoredFilePurpose.InvoiceSupportingFile),
            "copy-of-a-document" => host.SeedFirmFile(sourceDocument: Guid.NewGuid()),
            "copy-of-an-email" => host.SeedFirmFile(sourceCommunication: Guid.NewGuid()),
            "not-yet-servable" => host.SeedFirmFile(status: StoredFileStatus.NotYetServable),
            "removed" => host.SeedFirmFile(status: StoredFileStatus.Removed),
            "malicious" => host.SeedFirmFile(tags: new Dictionary<string, string>
            {
                [StoredFileBlobs.ScanResultTagKey] = StoredFileBlobs.ScanMaliciousValue,
                [StoredFileBlobs.ScanTimeTagKey] = host.Rig.Gateway.Now.AddMinutes(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            }),
            "another-invoices-file" => host.SeedFirmFile(invoice: OtherInvoice),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown case (test)."),
        };

        (await Ids(await host.GetAsync(ListUrl(Invoice)))).Should().Equal(visible);

        var unknown = await Shape(await host.GetAsync(ContentUrl(Invoice, Guid.NewGuid())));
        (await Shape(await host.GetAsync(ContentUrl(Invoice, hidden)))).Should().Be(unknown);
        host.AuditEvents.Should().BeEmpty();
    }

    [Fact(DisplayName = "A file whose scan has no verdict is listed as Checking, and its content is 409 file_scan_pending with no bytes")]
    public async Task Pending_IsChecking_And409()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var file = host.SeedFirmFile(tags: new Dictionary<string, string>());

        var row = JsonNode.Parse(await (await host.GetAsync(ListUrl(Invoice))).Content.ReadAsStringAsync())!["value"]![0]!;
        row["scanStatus"]!.GetValue<string>().Should().Be("Checking");

        var content = await host.GetAsync(ContentUrl(Invoice, file));
        content.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(content)).Should().Be(StoredFileEndpoints.ScanPendingErrorCode);
        host.AuditEvents.Should().BeEmpty();
    }

    [Fact(DisplayName = "With malware scanning off, an untagged file is listed as NotScanned and served")]
    public async Task ScanningOff_NotScanned_Served()
    {
        await using var host = await Host.StartAsync(scanning: false);
        host.CallerIs("contact");
        var file = host.SeedFirmFile(tags: new Dictionary<string, string>());

        var row = JsonNode.Parse(await (await host.GetAsync(ListUrl(Invoice))).Content.ReadAsStringAsync())!["value"]![0]!;
        row["scanStatus"]!.GetValue<string>().Should().Be("NotScanned");
        (await host.GetAsync(ContentUrl(Invoice, file))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "An invoice with no firm file lists an empty set (the invoice is visible)")]
    public async Task NoFiles_EmptyList()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");

        var response = await host.GetAsync(ListUrl(Invoice));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Ids(response)).Should().BeEmpty();
    }

    // =============================================================================================================================
    // Faults: a decision that could not be made is a 503, never a 404 and never a partial list
    // =============================================================================================================================

    public static TheoryData<string> Faults => new()
    {
        "firm-read", "systemuser-flag", "invoice-read", "predicate", "secure-flag-read", "file-rows", "storage", "too-many-files",
    };

    [Theory(DisplayName = "Fault: each read that fails is the 503 file_store_unavailable on the list and the content route")]
    [MemberData(nameof(Faults))]
    public async Task Fault_Is503(string kind)
    {
        await using var host = await Host.StartAsync();
        host.CallerIs(kind == "systemuser-flag" ? "flagged-systemuser" : "contact");
        var file = host.SeedFirmFile();
        switch (kind)
        {
            case "firm-read":
                host.Participations.Unreadable = true;
                break;
            case "systemuser-flag":
                host.Identity.Setup(i => i.IsExternalAsync(FlaggedSystemUser, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("Dataverse answered 503."));
                break;
            case "invoice-read":
                host.Data.InvoiceFault = new ExternalDataService.DataverseUnavailableException("Dataverse answered 429.");
                break;
            case "predicate":
                host.Hiding = InvoiceHidingStub.Faulting();
                break;
            case "secure-flag-read":
                host.Data.InvoiceProjects[Invoice] = SecureProject;
                host.Data.SecureFlagFault = new ExternalDataService.DataverseUnavailableException("Dataverse answered 429.");
                break;
            case "file-rows":
                host.Rig.Dataverse.QueryFault = new HttpRequestException("Dataverse answered 503.");
                break;
            case "storage":
                host.Rig.Gateway.FailEverything = new IOException("connection reset");
                break;
            case "too-many-files":
                for (var i = 0; i < StoredFileEndpoints.MaxListedFiles; i++)
                    host.SeedFirmFile();
                break;
        }

        foreach (var url in new[] { ListUrl(Invoice), ContentUrl(Invoice, file) })
        {
            var response = await host.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, url);
            (await ErrorCode(response)).Should().Be(StoredFileEndpoints.StoreUnavailableErrorCode);
        }

        host.AuditEvents.Should().BeEmpty();
    }

    // =============================================================================================================================
    // Fix round 1 (A, uac-r2 ruling): a systemuser holds Read on the invoice row itself AND on its root; a contact is decided as before
    // =============================================================================================================================

    public static TheoryData<string> SystemUsers => new() { "flagged-systemuser", "internal-systemuser" };

    [Theory(DisplayName = "Fix 1 A (must not fire): a systemuser with Read on the invoice and its root lists and downloads; Dataverse is asked about that systemuser and that invoice")]
    [MemberData(nameof(SystemUsers))]
    public async Task SystemUser_WithReadOnTheInvoiceAndItsRoot_IsServed(string kind)
    {
        await using var host = await Host.StartAsync();
        host.CallerIs(kind);
        var file = host.SeedFirmFile();
        var systemUser = kind == "flagged-systemuser" ? FlaggedSystemUser : InternalSystemUser;

        (await Ids(await host.GetAsync(ListUrl(Invoice)))).Should().Equal(file);
        var content = await host.GetAsync(ContentUrl(Invoice, file));

        content.StatusCode.Should().Be(HttpStatusCode.OK);
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
        host.Probe.AppOnlyCalls.Should().Equal(
            (systemUser, StoredFileSchema.InvoiceEntitySet, Invoice), (systemUser, StoredFileSchema.InvoiceEntitySet, Invoice));
    }

    [Fact(DisplayName = "Fix 1 A (must not fire): a contact with the same grant on the invoice's root is served, and Dataverse is not asked about the invoice row")]
    public async Task Contact_WithTheSameRootGrant_IsServed_WithoutTheRowQuestion()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var file = host.SeedFirmFile();

        (await Ids(await host.GetAsync(ListUrl(Invoice)))).Should().Equal(file);
        (await host.GetAsync(ContentUrl(Invoice, file))).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Probe.AppOnlyCalls.Should().BeEmpty();
    }

    public static TheoryData<string, string> SystemUserRowQuestionFaults => new()
    {
        { "flagged-systemuser", "unanswered" },
        { "flagged-systemuser", "throws" },
        { "internal-systemuser", "unanswered" },
        { "internal-systemuser", "throws" },
    };

    [Theory(DisplayName = "Fix 1 A (fault): Dataverse gives no answer for a systemuser's Read on the invoice row: 503 on both routes, no file row or byte read, never the root's answer alone")]
    [MemberData(nameof(SystemUserRowQuestionFaults))]
    public async Task SystemUser_RowQuestionUnanswered_Is503_AndNothingIsServed(string kind, string fault)
    {
        await using var host = await Host.StartAsync();
        host.CallerIs(kind);
        var file = host.SeedFirmFile();
        var systemUser = kind == "flagged-systemuser" ? FlaggedSystemUser : InternalSystemUser;
        if (fault == "unanswered")
            host.Probe.AnswerAppOnly(systemUser, StoredFileSchema.InvoiceEntitySet, Invoice, null);
        else
            host.Probe.AppOnlyFault = new HttpRequestException("Dataverse answered 503.");
        host.Rig.Dataverse.Calls.Clear();

        foreach (var url in new[] { ListUrl(Invoice), ContentUrl(Invoice, file) })
        {
            var response = await host.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, url);
            (await ErrorCode(response)).Should().Be(StoredFileEndpoints.StoreUnavailableErrorCode);
        }

        host.Rig.Dataverse.Calls.Should().BeEmpty("no file row is read without the caller's Read on the invoice");
        host.Rig.Gateway.StorageCalls.Should().Be(0);
        host.AuditEvents.Should().BeEmpty();
    }

    // =============================================================================================================================
    // Fix round 1 (B): the firm's files are read filtered by the store, so internal files of the invoice neither crowd them out nor
    // count against the cap; and the content route judges only the file it names
    // =============================================================================================================================

    [Fact(DisplayName = "Fix 1 B: an invoice with 60 internal files and one firm PDF lists and serves the PDF")]
    public async Task ManyInternalFiles_DoNotHideTheFirmPdf()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        for (var i = 0; i < 30; i++)
        {
            host.SeedFirmFile(firmVisible: false);
            host.SeedFirmFile(purpose: StoredFilePurpose.InvoiceSupportingFile, firmVisible: false);
        }

        var file = host.SeedFirmFile();

        var list = await host.GetAsync(ListUrl(Invoice));
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        (await Ids(list)).Should().Equal(file);
        var content = await host.GetAsync(ContentUrl(Invoice, file));
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
    }

    [Fact(DisplayName = "Fix 1 B: the firm route's Stored File query filters on the invoice, Invoice PDF, firm-visible, Servable and no source")]
    public async Task TheStoredFileQuery_IsFilteredByTheStore()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        host.SeedFirmFile();

        await host.GetAsync(ListUrl(Invoice));

        var query = host.Rig.Dataverse.Mock.Invocations
            .Where(i => i.Method.Name == nameof(Spaarke.Dataverse.IGenericEntityService.RetrieveMultipleAsync))
            .Select(i => i.Arguments[0]).OfType<QueryExpression>()
            .Should().ContainSingle(q => q.EntityName == StoredFileSchema.Entity).Subject;
        var conditions = query.Criteria.Conditions
            .Select(c => $"{c.AttributeName} {c.Operator} {string.Join(",", c.Values)}")
            .ToList();
        conditions.Should().BeEquivalentTo(
            $"{StoredFileSchema.InvoiceTable} {ConditionOperator.Equal} {Invoice}",
            $"{StoredFileSchema.Purpose} {ConditionOperator.Equal} {(int)StoredFilePurpose.InvoicePdf}",
            $"{StoredFileSchema.FirmVisible} {ConditionOperator.Equal} {true}",
            $"{StoredFileSchema.Status} {ConditionOperator.Equal} {(int)StoredFileStatus.Servable}",
            $"{StoredFileSchema.SourceDocument} {ConditionOperator.Null} ",
            $"{StoredFileSchema.SourceCommunication} {ConditionOperator.Null} ");
    }

    [Fact(DisplayName = "Fix 1 (K3): another firm file's storage fault does not refuse a healthy file's content; the list, which judges every file, is the 503")]
    public async Task SiblingStorageFault_DoesNotRefuseAHealthyDownload()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var healthy = host.SeedFirmFile();
        var broken = host.SeedFirmFile();
        host.Rig.Gateway.BeforeOp = (_, name) =>
        {
            if (name.Contains(broken.ToString("D"), StringComparison.OrdinalIgnoreCase))
                throw new IOException("connection reset");
        };

        var content = await host.GetAsync(ContentUrl(Invoice, healthy));
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);

        var list = await host.GetAsync(ListUrl(Invoice));
        list.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(list)).Should().Be(StoredFileEndpoints.StoreUnavailableErrorCode);
    }

    // =============================================================================================================================
    // The kill switch, authentication and the absent routes
    // =============================================================================================================================

    [Fact(DisplayName = "Flag off: 503 ebilling.disabled for a caller who may see the invoice; a caller who may not still gets the 404 (the flag runs after authorization)")]
    public async Task FlagOff()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var file = host.SeedFirmFile();
        host.Harness.Options.Enabled = false;

        var list = await host.GetAsync(ListUrl(Invoice));
        list.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(list)).Should().Be(EbillingEnabledFilter.ErrorCode);
        (await host.GetAsync(ContentUrl(Invoice, file))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.AuditEvents.Should().BeEmpty();

        host.Data.Invoices[Invoice] = (Matter, OtherFirm);
        (await host.GetAsync(ListUrl(Invoice))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "No token: 401 on both routes")]
    public async Task Unauthenticated_401()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var file = host.SeedFirmFile();

        (await host.GetAsync(ListUrl(Invoice), authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.GetAsync(ContentUrl(Invoice, file), authenticated: false)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "No upload and no versions route under the firm file routes")]
    public async Task NoUploadOrVersionsRoute()
    {
        await using var host = await Host.StartAsync();
        host.CallerIs("contact");
        var file = host.SeedFirmFile();

        (await host.SendAsync(HttpMethod.Post, ListUrl(Invoice))).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await host.GetAsync($"/api/v1/external/invoices/{Invoice}/files/{file}/versions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =============================================================================================================================
    // Helpers
    // =============================================================================================================================

    private static CallerPrincipal ContactPrincipal(Guid contact, Dictionary<Guid, AccessRights> matters) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = contact,
        Email = "firm.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = matters,
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static CallerPrincipal SystemUserPrincipal(Guid systemUser, Guid contact, Dictionary<Guid, AccessRights> matters) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = contact,
        SystemUserId = systemUser,
        Email = "staff@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = matters,
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static async Task<Guid[]> Ids(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["value"]!.AsArray().Select(r => r!["id"]!.GetValue<Guid>()).ToArray();

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.TryGetProperty("errorCode", out var code)
            ? code.GetString()
            : null;

    /// <summary>A refusal's comparable shape: status, content type and the body without its per-request correlation fields.</summary>
    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType}|{node.ToJsonString()}";
    }

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public StoredFileRig Rig { get; private set; } = null!;
        public InvoiceSubmissionServiceTests.Harness Harness { get; } = new();
        public FirmParticipations Participations { get; } = new();
        public StubResolver Resolver { get; } = new();
        public InvoiceData Data { get; } = new();
        public Mock<ISystemUserIdentityResolver> Identity { get; } = new();
        public InvoiceHidingStub Hiding { get; set; } = InvoiceHidingStub.NoneHidden();

        /// <summary>Fix round 1 (A): Dataverse's answer for a systemuser's own rights on a row (app-only, named systemuser). Unseeded is None.</summary>
        public CallerRecordAccessProbeDouble Probe { get; } = new();
        public ConcurrentQueue<string> Timeline { get; } = new();
        public StoredFileEndpointsTests.CapturingProvider Logs { get; private set; } = null!;

        public IReadOnlyList<(EventId EventId, string Message)> AuditEvents =>
            Logs.Entries.Where(e => e.EventId.Id == StoredFileAudit.Downloaded.Id).ToList();

        public static async Task<Host> StartAsync(bool scanning = true)
        {
            var host = new Host { Rig = new StoredFileRig(malwareScanningEnabled: scanning) };
            host.Logs = new StoredFileEndpointsTests.CapturingProvider(host.Timeline);
            host.Rig.Gateway.OnFirstRead = () => host.Timeline.Enqueue("first-byte");
            host.Harness.Participations = host.Participations;
            host.Participations.FirmsOf[Contact] = new[] { Firm };
            host.Participations.FirmsOf[OtherFirmContact] = new[] { OtherFirm };
            host.Data.Invoices[Invoice] = (Matter, Firm);
            host.Data.RootIsSecure[(ExternalGrantRootType.Matter, Matter)] = false;
            host.Identity.Setup(i => i.IsExternalAsync(FlaggedSystemUser, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            host.Identity.Setup(i => i.IsExternalAsync(InternalSystemUser, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            // Both systemusers hold Read on the invoice row itself (a test that refuses it overrides the answer).
            host.Probe.AnswerAppOnly(FlaggedSystemUser, StoredFileSchema.InvoiceEntitySet, Invoice, AccessRights.Read);
            host.Probe.AnswerAppOnly(InternalSystemUser, StoredFileSchema.InvoiceEntitySet, Invoice, AccessRights.Read);

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
            builder.Services.AddSingleton(host.Harness.Monitor);
            builder.Services.AddSingleton(host.Harness.Build());
            builder.Services.AddSingleton<ExternalParticipationService>(host.Participations);
            builder.Services.AddSingleton<ICallerPrincipalResolver>(host.Resolver);
            builder.Services.AddSingleton<ExternalDataService>(host.Data);
            builder.Services.AddSingleton(ProductionRegistry());
            builder.Services.AddSingleton(host.Rig.Service);
            builder.Services.AddSingleton(host.Identity.Object);
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
            builder.Services.AddScoped<ExternalInvoiceHiding>(_ => (caller, ids, ct) => host.Hiding.Seam(caller, ids, ct));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            // Stands in for ExternalAccessEndpoints' group: authenticated, then the caller-principal filter.
            host._app.MapGroup("/api/v1/external").RequireAuthorization().AddCallerPrincipalAuthorizationFilter()
                .MapExternalInvoiceSubmissionEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void CallerIs(string kind) => Resolver.Principal = kind switch
        {
            "contact" => ContactPrincipal(Contact, new() { [Matter] = AccessRights.Read }),
            "flagged-systemuser" => SystemUserPrincipal(FlaggedSystemUser, Contact, new() { [Matter] = AccessRights.Read }),
            "internal-systemuser" => SystemUserPrincipal(InternalSystemUser, Guid.Empty, new() { [Matter] = AccessRights.Read }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown caller (test)."),
        };

        /// <summary>Seeds a Stored File row and blob (by default the firm's own Servable, Clean, firm-visible Invoice PDF of <see cref="Invoice"/>).</summary>
        public Guid SeedFirmFile(
            Guid? invoice = null, bool firmVisible = true, StoredFilePurpose purpose = StoredFilePurpose.InvoicePdf,
            StoredFileStatus status = StoredFileStatus.Servable, Guid? sourceDocument = null, Guid? sourceCommunication = null,
            IDictionary<string, string>? tags = null, string name = "invoice.pdf")
        {
            var file = Guid.NewGuid();
            var row = Rig.SeedFile(file, InvoiceParent(invoice ?? Invoice), Pdf, status, purpose, sourceDocument, tags);
            row[StoredFileSchema.FirmVisible] = firmVisible;
            row[StoredFileSchema.Name] = name;
            row[StoredFileSchema.SizeBytes] = Pdf.Length;
            if (sourceCommunication is { } communication)
                row[StoredFileSchema.SourceCommunication] = new EntityReference(StoredFileSchema.CommunicationTable, communication);
            return file;
        }

        public Task<HttpResponseMessage> GetAsync(string url, bool authenticated = true) => SendAsync(HttpMethod.Get, url, authenticated);

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, bool authenticated = true)
        {
            var request = new HttpRequestMessage(method, url);
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
                request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
            }

            return _client.SendAsync(request);
        }

        private static ExternalModuleRegistry ProductionRegistry()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
            services.AddExternalAccess();
            return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
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

    private sealed class StubResolver : ICallerPrincipalResolver
    {
        public CallerPrincipal? Principal { get; set; }

        public Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct) =>
            Task.FromResult(Principal is null
                ? CallerPrincipalResolution.Denied(Results.Problem(statusCode: 403, detail: "No external principal (test stub)"))
                : CallerPrincipalResolution.Resolved(Principal));
    }

    /// <summary>Each contact's conferring firms (a contact not listed belongs to none); every contact is Active.</summary>
    private sealed class FirmParticipations : ExternalParticipationService
    {
        public FirmParticipations()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: Mock.Of<TokenCredential>(),
                httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance, filing: null)
        {
        }

        public Dictionary<Guid, Guid[]> FirmsOf { get; } = new();

        public bool Unreadable { get; set; }

        public int MembershipReads { get; private set; }

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct) =>
            Task.FromResult(ContactRecordState.Active);

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
        {
            MembershipReads++;
            var firms = FirmsOf.TryGetValue(contactId, out var f) ? f : Array.Empty<Guid>();
            return Task.FromResult(Unreadable ? ActiveOrgMemberships.Failed : new ActiveOrgMemberships(firms, firms, false));
        }
    }

    /// <summary>The app-only invoice reads the record gate makes: the invoice's lookups (its matter, project and firm), the secure flags
    /// and the live parents.</summary>
    private sealed class InvoiceData : ExternalDataService
    {
        public InvoiceData()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), Mock.Of<TokenCredential>(), NullLogger<ExternalDataService>.Instance)
        {
        }

        /// <summary>Invoice id → its matter and its vendor firm. Absent = the invoice does not exist.</summary>
        public ConcurrentDictionary<Guid, (Guid Matter, Guid Firm)> Invoices { get; } = new();

        /// <summary>Invoice id → a project it is also filed under.</summary>
        public ConcurrentDictionary<Guid, Guid> InvoiceProjects { get; } = new();

        /// <summary>A root's effective secure state. Absent = unreadable (counts as secure).</summary>
        public ConcurrentDictionary<(ExternalGrantRootType Root, Guid Id), bool> RootIsSecure { get; } = new();

        public Exception? InvoiceFault { get; set; }

        public Exception? SecureFlagFault { get; set; }

        public override Task<IReadOnlyDictionary<string, object?>> GetInvoiceLookupsAsync(
            Guid invoiceId, IReadOnlyCollection<string> lookups, CancellationToken ct = default)
        {
            if (InvoiceFault is { } fault)
                throw fault;
            if (!Invoices.TryGetValue(invoiceId, out var invoice))
                throw new RecordNotFoundException("sprk_invoice", invoiceId);

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (lookups.Contains("sprk_matter")) row["sprk_matter"] = invoice.Matter;
            if (lookups.Contains("sprk_vendororg")) row["sprk_vendororg"] = invoice.Firm;
            if (InvoiceProjects.TryGetValue(invoiceId, out var project) && lookups.Contains("sprk_project")) row["sprk_project"] = project;
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(row);
        }

        public override Task<NonSecureRootsRead> GetNonSecureRootsAsync(
            IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)> roots, CancellationToken ct = default)
        {
            if (SecureFlagFault is { } fault)
                throw fault;
            return Task.FromResult(new NonSecureRootsRead(roots.Where(r => RootIsSecure.TryGetValue(r, out var secure) && !secure).ToHashSet()));
        }

        public override Task<LiveParentRootsRead> GetLiveParentRootsAsync(
            IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct = default) =>
            Task.FromResult(new LiveParentRootsRead(
                new Dictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>>(), false));
    }
}
