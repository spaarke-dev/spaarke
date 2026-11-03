using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;
using Host = Sprk.Bff.Api.Tests.Api.Finance.StoredFileEndpointsTests.Host;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// <c>GET /api/finance/invoices/{invoiceId}/files</c> (ebilling-r1 task 151e) through the REAL <c>MapStoredFileEndpoints</c>, the Finance
/// authorization filter, <c>StoredFileAccessFilter.DecideRowAsync</c> (the content route's decision), <see cref="StoredFileService"/> and
/// <see cref="StoredFileBlobs"/>, on the same hosts and fakes as <see cref="StoredFileEndpointsTests"/>.
/// </summary>
public sealed class InvoiceFilesListTests
{
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 the invoice bytes");
    private static readonly Guid InvoiceId = Guid.Parse("1a000000-0000-4000-8000-0000000000f0");
    private static readonly StoredFileParent Invoice = new(StoredFileParentKind.Invoice, InvoiceId);
    private static readonly DateTime Day1 = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private static string ListUrl(Guid invoiceId) => $"/api/finance/invoices/{invoiceId}/files";

    private static string ContentUrl(Guid fileId) => $"/api/finance/files/{fileId}/content";

    public static IEnumerable<object[]> RefusedKinds => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    public static IEnumerable<object[]> InternalKinds => StatedCaller.InternalKinds.Select(k => new object[] { k });

    // =============================================================================================================================
    // 200: what is listed, in what order, with what fields
    // =============================================================================================================================

    [Fact(DisplayName = "Read on the invoice → 200: the Invoice PDF first, then Supporting files by added-on; every field, the source email with its name")]
    public async Task Listed_InOrder_WithFields()
    {
        await using var host = await Host.StartAsync();
        var email = Guid.NewGuid();
        host.Rig.Dataverse.AddOther(StoredFileSchema.DocumentTable, email, (StoredFileSchema.DocumentName, "Invoice 792345 from Acme.eml"));
        host.GrantParentRead();
        host.Rig.Probe.Grant("sprk_documents", email, AccessRights.Read);
        var supportingLater = Seed(host, "later.xlsx", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(2), contentType: "application/vnd.ms-excel", size: 300);
        var pdf = Seed(host, "INV-792345.pdf", StoredFilePurpose.InvoicePdf, Day1.AddHours(3), sourceDocument: email, size: 1234);
        var supportingEarlier = Seed(host, "earlier.txt", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(1), contentType: "text/plain", size: 200);

        var response = await host.SendAsync(ListUrl(InvoiceId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        items.Select(i => i!["fileId"]!.GetValue<Guid>()).Should().Equal(pdf, supportingEarlier, supportingLater);

        var first = items[0]!;
        first["name"]!.GetValue<string>().Should().Be("INV-792345.pdf");
        first["purpose"]!.GetValue<string>().Should().Be("InvoicePdf");
        first["sizeBytes"]!.GetValue<int>().Should().Be(1234);
        first["contentType"]!.GetValue<string>().Should().Be("application/pdf");
        first["addedOn"]!.GetValue<DateTime>().Should().Be(Day1.AddHours(3));
        first["scanState"]!.GetValue<string>().Should().Be("Ready");
        first["sourceEmail"]!["documentId"]!.GetValue<Guid>().Should().Be(email);
        first["sourceEmail"]!["name"]!.GetValue<string>().Should().Be("Invoice 792345 from Acme.eml");

        items[1]!["purpose"]!.GetValue<string>().Should().Be("Supporting");
        items[1]!["sourceEmail"].Should().BeNull("a file that is not a copy of an email has no source email");
        items.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Pending scan: listed with scanState Pending (Open waits); a clean one beside it is Ready")]
    public async Task PendingScan_ListedAsPending()
    {
        await using var host = await Host.StartAsync();
        host.GrantParentRead();
        var ready = Seed(host, "ready.pdf", StoredFilePurpose.InvoicePdf, Day1);
        var pending = Seed(host, "scanning.pdf", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(1), tags: new Dictionary<string, string>());

        var items = await ListAsync(host);

        items.Select(i => (i["fileId"]!.GetValue<Guid>(), i["scanState"]!.GetValue<string>()))
            .Should().Equal((ready, "Ready"), (pending, "Pending"));
    }

    [Fact(DisplayName = "An empty invoice → 200 with an empty list")]
    public async Task NoFiles_EmptyList()
    {
        await using var host = await Host.StartAsync();
        host.GrantParentRead();

        (await ListAsync(host)).Should().BeEmpty();
    }

    [Fact(DisplayName = "Source email: asked once per document (not once per file); the invoice's Read asked once")]
    public async Task Rights_AskedOncePerRecord()
    {
        await using var host = await Host.StartAsync();
        var email = Guid.NewGuid();
        host.Rig.Dataverse.AddOther(StoredFileSchema.DocumentTable, email, (StoredFileSchema.DocumentName, "mail.eml"));
        host.GrantParentRead();
        host.Rig.Probe.Grant("sprk_documents", email, AccessRights.Read);
        Seed(host, "a.pdf", StoredFilePurpose.InvoicePdf, Day1, sourceDocument: email);
        Seed(host, "b.txt", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(1), sourceDocument: email, contentType: "text/plain");

        (await ListAsync(host)).Should().HaveCount(2);

        host.Rig.Probe.Calls.Count(c => c.Set == "sprk_invoices" && c.Id == InvoiceId).Should().Be(1);
        host.Rig.Probe.Calls.Count(c => c.Set == "sprk_documents" && c.Id == email).Should().Be(1);
    }

    // =============================================================================================================================
    // List and content agree (the rule this task seeds)
    // =============================================================================================================================

    [Fact(DisplayName = "Seeded rule: every listed file is what the content route serves (Ready 200, Pending 409); every file the content route refuses is not listed")]
    public async Task List_AndContent_Agree()
    {
        await using var host = await Host.StartAsync();
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        host.Rig.Dataverse.AddOther(StoredFileSchema.DocumentTable, readable, (StoredFileSchema.DocumentName, "readable.eml"));
        host.Rig.Dataverse.AddOther(StoredFileSchema.DocumentTable, unreadable, (StoredFileSchema.DocumentName, "secret.eml"));
        host.GrantParentRead();
        host.Rig.Probe.Grant("sprk_documents", readable, AccessRights.Read);

        var all = new Dictionary<Guid, string>
        {
            [Seed(host, "pdf.pdf", StoredFilePurpose.InvoicePdf, Day1, sourceDocument: readable)] = "served",
            [Seed(host, "plain.txt", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(1), contentType: "text/plain")] = "served",
            [Seed(host, "pending.txt", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(2), tags: new Dictionary<string, string>())] = "pending",
            [Seed(host, "copy-of-unreadable-email.pdf", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(3), sourceDocument: unreadable)] = "refused",
            [Seed(host, "not-yet.pdf", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(4), status: StoredFileStatus.NotYetServable)] = "refused",
            [Seed(host, "removed.pdf", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(5), status: StoredFileStatus.Removed)] = "refused",
            [Seed(host, "malicious.pdf", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(6), tags: new Dictionary<string, string>
            {
                [StoredFileBlobs.ScanResultTagKey] = StoredFileBlobs.ScanMaliciousValue,
                [StoredFileBlobs.ScanTimeTagKey] = host.Rig.Gateway.Now.AddMinutes(1).ToString("O"),
            })] = "refused",
        };

        var items = await ListAsync(host);
        var listed = items.ToDictionary(i => i["fileId"]!.GetValue<Guid>(), i => i["scanState"]!.GetValue<string>());

        foreach (var (file, expected) in all)
        {
            var content = await host.SendAsync(ContentUrl(file));
            switch (expected)
            {
                case "served":
                    content.StatusCode.Should().Be(HttpStatusCode.OK, file.ToString());
                    listed.Should().ContainKey(file);
                    listed[file].Should().Be("Ready");
                    break;
                case "pending":
                    content.StatusCode.Should().Be(HttpStatusCode.Conflict, file.ToString());
                    listed.Should().ContainKey(file);
                    listed[file].Should().Be("Pending");
                    break;
                default:
                    content.StatusCode.Should().Be(HttpStatusCode.NotFound, file.ToString());
                    listed.Should().NotContainKey(file);
                    break;
            }
        }

        listed.Should().HaveCount(3);
        items.Should().NotContain(i => i["name"]!.GetValue<string>().Contains("secret"), "the unreadable email's name never leaves the BFF");
    }

    [Fact(DisplayName = "A file whose parent is another invoice is never listed under this one (even when the query returns it)")]
    public async Task OtherInvoicesFile_NotListed()
    {
        await using var host = await Host.StartAsync();
        host.GrantParentRead();
        var otherInvoice = new StoredFileParent(StoredFileParentKind.Invoice, Guid.NewGuid());
        host.Rig.Probe.Grant("sprk_invoices", otherInvoice.Id, AccessRights.Read);
        var other = Guid.NewGuid();
        host.Rig.SeedFile(other, otherInvoice, Pdf);
        var own = Seed(host, "own.pdf", StoredFilePurpose.InvoicePdf, Day1);

        var items = await ListAsync(host);

        items.Select(i => i["fileId"]!.GetValue<Guid>()).Should().Equal(own);
    }

    // =============================================================================================================================
    // Authorization: the same 404 for no Read, unknown invoice and a step-0 refusal; 401; the flag
    // =============================================================================================================================

    [Fact(DisplayName = "Unauthenticated → 401, nothing read")]
    public async Task Unauthenticated_401()
    {
        await using var host = await Host.StartAsync();
        Seed(host, "a.pdf", StoredFilePurpose.InvoicePdf, Day1);

        var response = await host.SendAsync(ListUrl(InvoiceId), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.AssertNothingRead();
    }

    [Fact(DisplayName = "No Read on the invoice (Write and Append are not Read), an unknown invoice and Read on another invoice → the SAME 404; nothing listed, no file row or storage read")]
    public async Task NoRead_UnknownInvoice_SameUniform404()
    {
        await using var host = await Host.StartAsync();
        Seed(host, "a.pdf", StoredFilePurpose.InvoicePdf, Day1);
        host.Rig.Probe.Grant("sprk_invoices", InvoiceId, AccessRights.Write | AccessRights.Append);
        host.Rig.Probe.Grant("sprk_invoices", Guid.NewGuid(), AccessRights.Read);

        var noRead = await host.SendAsync(ListUrl(InvoiceId));
        var unknown = await host.SendAsync(ListUrl(Guid.NewGuid()));

        noRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Shape(noRead)).Should().Be(await Shape(unknown));
        JsonNode.Parse(await noRead.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        host.Rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("query", StringComparison.Ordinal));
        host.Rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Step 0 refuses a flagged-external, row-less or unreadable caller with the same uniform 404 (not a 403), before any record read, even with Read")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_Refuses_Uniform404(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        Seed(host, "a.pdf", StoredFilePurpose.InvoicePdf, Day1);
        host.GrantParentRead();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(ListUrl(InvoiceId));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.Rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("query", StringComparison.Ordinal));
        host.Rig.Probe.Calls.Should().BeEmpty();
        host.Rig.Gateway.StorageCalls.Should().Be(0);
    }

    [Theory(DisplayName = "An internal caller (flag false; flag blank) with Read is listed")]
    [MemberData(nameof(InternalKinds))]
    public async Task InternalCaller_Listed(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;
        host.GrantParentRead();
        Seed(host, "a.pdf", StoredFilePurpose.InvoicePdf, Day1);

        (await ListAsync(host)).Should().HaveCount(1);
    }

    [Fact(DisplayName = "eBilling off: an authorized caller gets 503 ebilling.disabled; a caller with no Read still gets the 404 (the flag never replaces authorization)")]
    public async Task FlagOff()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        Seed(host, "a.pdf", StoredFilePurpose.InvoicePdf, Day1);

        (await host.SendAsync(ListUrl(InvoiceId))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        host.GrantParentRead();
        var response = await host.SendAsync(ListUrl(InvoiceId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Rig.Dataverse.Calls.Should().NotContain(c => c.StartsWith("query", StringComparison.Ordinal));
    }

    // =============================================================================================================================
    // Faults: 503, never a partial or empty list
    // =============================================================================================================================

    public static IEnumerable<object[]> FaultCases => [["query"], ["document"], ["props"], ["tags"], ["rights-email"], ["rights-invoice"]];

    [Theory(DisplayName = "A read fault (the file query, the source document's name, the blob's properties or scan tags, an UNKNOWN rights answer on the source email or on the invoice) → 503 file_store_unavailable, not a partial or empty list")]
    [MemberData(nameof(FaultCases))]
    public async Task ReadFault_503(string where)
    {
        await using var host = await Host.StartAsync();
        var email = Guid.NewGuid();
        host.Rig.Dataverse.AddOther(StoredFileSchema.DocumentTable, email, (StoredFileSchema.DocumentName, "mail.eml"));
        host.GrantParentRead();
        host.Rig.Probe.Grant("sprk_documents", email, AccessRights.Read);
        Seed(host, "ok.pdf", StoredFilePurpose.InvoicePdf, Day1, sourceDocument: email);
        Seed(host, "second.txt", StoredFilePurpose.InvoiceSupportingFile, Day1.AddHours(1), contentType: "text/plain");
        switch (where)
        {
            case "query":
                host.Rig.Dataverse.QueryFault = new TimeoutException("Dataverse slow");
                break;
            case "document":
                host.Rig.Dataverse.RetrieveFault = new TimeoutException("Dataverse slow");
                break;
            case "rights-email":
                // A 429/5xx/OBO/WhoAmI fault on the source email: the gate variant would read it as "cannot read" and drop the PDF (a partial list).
                host.Rig.Probe.Unknown.Add(("sprk_documents", email));
                break;
            case "rights-invoice":
                // The same on the invoice: every file would be dropped ("No files").
                host.Rig.Probe.Unknown.Add(("sprk_invoices", InvoiceId));
                break;
            default:
                host.Rig.Gateway.FailOp[where] = new Azure.RequestFailedException(500, "storage fault");
                break;
        }

        var response = await host.SendAsync(ListUrl(InvoiceId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, where);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_store_unavailable");
    }

    [Fact(DisplayName = "A step-0 refusal INSIDE the handler that is a system failure (not a refusal of the caller) → 503, not the uniform 404; a real refusal there stays 404")]
    public async Task StepZeroSystemFailureInHandler_503_RealRefusalStays404()
    {
        await using var host = await Host.StartAsync();
        host.GrantParentRead();
        Seed(host, "ok.pdf", StoredFilePurpose.InvoicePdf, Day1);

        // Baseline: how many lookups one full step 0 asks (the authorization filter runs it, then the handler runs it again).
        (await host.SendAsync(ListUrl(InvoiceId))).StatusCode.Should().Be(HttpStatusCode.OK);
        var perStepZero = host.StepZero.Queries.Count / 2;
        perStepZero.Should().BeGreaterThan(0);
        host.StepZero.Queries.Clear();

        // The filter's step 0 admits the caller; the handler's own step 0 (query perStepZero + 1 onward) finds the lookup faulted.
        var asked = 0;
        host.StepZero.KindSource = () => ++asked > perStepZero ? StepZeroCallerKind.LookupFault : StepZeroCallerKind.FlagFalse;
        var fault = await host.SendAsync(ListUrl(InvoiceId));
        fault.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await fault.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("file_store_unavailable");

        // Control: a real refusal at the handler's step 0 (the caller turns out flagged external) is still the uniform 404.
        asked = 0;
        host.StepZero.KindSource = () => ++asked > perStepZero ? StepZeroCallerKind.FlagTrue : StepZeroCallerKind.FlagFalse;
        (await host.SendAsync(ListUrl(InvoiceId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "More files than an invoice can have (51) → 503, never a truncated list")]
    public async Task TooManyFiles_503()
    {
        await using var host = await Host.StartAsync();
        host.GrantParentRead();
        for (var i = 0; i <= StoredFileEndpoints.MaxListedFiles; i++)
        {
            Seed(host, $"f{i}.txt", StoredFilePurpose.InvoiceSupportingFile, Day1.AddMinutes(i), contentType: "text/plain");
        }

        (await host.SendAsync(ListUrl(InvoiceId))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact(DisplayName = "Store not configured → 503 file_store_unavailable")]
    public async Task NotConfigured_503()
    {
        await using var host = await Host.StartAsync(configured: false);
        host.GrantParentRead();
        var file = Guid.NewGuid();
        host.Rig.Dataverse.AddRow(new Entity(StoredFileSchema.Entity, file)
        {
            [StoredFileSchema.Purpose] = new OptionSetValue((int)StoredFilePurpose.InvoicePdf),
            [StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Servable),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
            [StoredFileSchema.BlobPath] = StoredFileBlobName.Build(StoredFileRig.Tenant, Invoice, file),
        });

        (await host.SendAsync(ListUrl(InvoiceId))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // =============================================================================================================================
    // The form source: the Files section, the batch's Sent to AP Date, and no native Stored File subgrid
    // =============================================================================================================================

    private const string WebResourceClassId = "{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}";
    private const string QuickViewClassId = "{5C5600E0-1D6E-4205-A272-BE80DA87FD42}";

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "dataverse", "solutions", "SpaarkeMaster")))
        {
            dir = dir.Parent;
        }

        return Path.Combine([dir!.FullName, .. parts]);
    }

    private static System.Xml.Linq.XDocument LoadForm(params string[] parts) => System.Xml.Linq.XDocument.Load(RepoFile(parts));

    private static readonly string[] InvoiceMainForm =
        ["src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_Invoice", "FormXml", "main", "{93aa1c69-0406-f111-8406-7c1e525abd8b}.xml"];

    private static readonly string[] BatchQuickForm =
        ["src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_billingexport", "FormXml", "quick", "{2c568b46-e9dd-4774-bcfa-00c436cb4eba}.xml"];

    /// <summary>True when any control of <paramref name="form"/> is a subgrid whose target (or any text) is <c>sprk_storedfile</c>.</summary>
    private static bool HasStoredFileSubgrid(System.Xml.Linq.XDocument form) =>
        form.Descendants("control").Any(c =>
            string.Equals((string?)c.Attribute("indicationOfSubgrid"), "true", StringComparison.OrdinalIgnoreCase)
            && c.Descendants().Any(e => e.Value.Contains("sprk_storedfile", StringComparison.OrdinalIgnoreCase)));

    [Fact(DisplayName = "Invoice main form: a Files section hosts the sprk_invoicefiles page (record id passed), and there is no native sprk_storedfile subgrid (control: the scan fires on one)")]
    public void Form_HasFilesSection_AndNoStoredFileSubgrid()
    {
        var form = LoadForm(InvoiceMainForm);

        var section = form.Descendants("section").Single(s => (string?)s.Attribute("name") == "section_files");
        section.Descendants("label").Select(l => (string?)l.Attribute("description")).Should().Contain("Files");
        var control = section.Descendants("control").Single();
        ((string?)control.Attribute("classid")).Should().Be(WebResourceClassId);
        control.Element("parameters")!.Element("Url")!.Value.Should().Be("sprk_invoicefiles");
        control.Element("parameters")!.Element("PassParameters")!.Value.Should().Be("true", "the page reads the record id the form passes");

        HasStoredFileSubgrid(form).Should().BeFalse("no role grants Read on sprk_storedfile; the list goes through the BFF");
        File.ReadAllText(RepoFile(InvoiceMainForm)).Should().NotContain("sprk_storedfile");

        // Control: the scan fires on a subgrid of the table and not on the page's own control.
        var subgrid = System.Xml.Linq.XDocument.Parse(
            "<form><control indicationOfSubgrid=\"true\" id=\"Subgrid_files\"><parameters><TargetEntityType>sprk_storedfile</TargetEntityType></parameters></control></form>");
        HasStoredFileSubgrid(subgrid).Should().BeTrue();
    }

    [Fact(DisplayName = "Invoice main form shows the batch's Sent to AP Date read-only: a quick view on the Export Batch lookup, whose quick form holds the date (no new invoice column)")]
    public void Form_ShowsBatchSentToApDate()
    {
        var form = LoadForm(InvoiceMainForm);

        var quickView = form.Descendants("control").Single(c => (string?)c.Attribute("classid") == QuickViewClassId);
        ((string?)quickView.Attribute("datafieldname")).Should().Be("sprk_billingexport");

        // The platform's own shape (verified on spaarkedev1, the Account main form's primarycontactid Quick View, 2026-11-03): QuickForms
        // holds an ESCAPED STRING (text, not child elements) of <QuickFormIds><QuickFormId entityname="...">GUID</QuickFormId>, and
        // ControlMode sits beside it.
        var parameters = quickView.Element("parameters")!;
        var quickForms = parameters.Element("QuickForms")!;
        quickForms.HasElements.Should().BeFalse("the platform stores QuickForms as an escaped string, not as child elements");
        var quickFormIds = System.Xml.Linq.XDocument.Parse(quickForms.Value);
        var quickForm = quickFormIds.Root!.Elements("QuickFormId").Single();
        quickFormIds.Root.Name.LocalName.Should().Be("QuickFormIds");
        ((string?)quickForm.Attribute("entityname")).Should().Be("sprk_billingexport");
        parameters.Element("ControlMode")!.Value.Should().Be("Edit");

        // Control: the old literal-child-elements shape has child elements, so this check fires on it.
        var literalShape = System.Xml.Linq.XElement.Parse("<QuickForms><QuickFormIds><QuickFormId entityname=\"sprk_billingexport\">x</QuickFormId></QuickFormIds></QuickForms>");
        literalShape.HasElements.Should().BeTrue();

        var batch = LoadForm(BatchQuickForm);
        batch.Descendants("formid").Single().Value.Trim('{', '}').Should().BeEquivalentTo(quickForm.Value.Trim('{', '}'));
        var date = batch.Descendants("control").Single(c => (string?)c.Attribute("datafieldname") == "sprk_senttoapdate");
        ((string?)date.Attribute("disabled")).Should().Be("true");

        form.Descendants("control").Select(c => (string?)c.Attribute("datafieldname")).Should().NotContain("sprk_senttoapdate", "the date lives on the batch, not on the invoice");
    }

    // =============================================================================================================================

    private static Guid Seed(
        Host host, string name, StoredFilePurpose purpose, DateTime createdOn, Guid? sourceDocument = null, StoredFileStatus status = StoredFileStatus.Servable,
        string contentType = "application/pdf", int size = 100, IDictionary<string, string>? tags = null)
    {
        var file = Guid.NewGuid();
        var row = host.Rig.SeedFile(file, Invoice, Pdf, status, purpose, sourceDocument, tags, contentType: contentType);
        row[StoredFileSchema.Name] = name;
        row[StoredFileSchema.SizeBytes] = size;
        row[StoredFileSchema.CreatedOn] = createdOn;
        return file;
    }

    private static async Task<IReadOnlyList<JsonNode>> ListAsync(Host host)
    {
        var response = await host.SendAsync(ListUrl(InvoiceId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray().Select(i => i!).ToList();
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
