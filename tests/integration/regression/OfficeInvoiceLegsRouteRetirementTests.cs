using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — ebilling-r1 task 096 (owner L, FR-17 / FR-20; ADR-038 Amendment A3).
///
/// Task 096 DELETED the Office quick-create's invoice-only leg (<c>OfficeService.QuickCreateAsync</c>'s generic Dataverse create of a
/// <c>sprk_invoice</c> with its name, description and Assigned To) and RETIRED the save route's Invoice target (<c>/api/office/save</c>
/// with a <c>TargetEntity</c> of invoice). An invoice is created and filed onto only by the submission operation (Submit Invoice),
/// whose own routes carry the finance filters. The Office add-in dropped both surfaces first (word-add-in-r1 slice C, live 2026-11-21);
/// the server legs followed one add-in release later (#2999, live 2026-11-23).
///
/// WHAT WOULD BREAK IF THIS TEST WERE DELETED: someone "restores" the invoice quick-create or the invoice save target for a cached
/// client, and an invoice is created, or filed onto, outside submission — with none of the submission's checks (the firm gate, the
/// totals owner, the lifecycle, the audit). Nothing else in the suite would notice a leg that simply started answering again.
///
/// THE TESTS DELETED WITH THE LEG (A3: the code they exercised is deleted, the behaviour does not continue elsewhere; this file is
/// their retirement test): <c>Issue1079_InvoiceQuickCreateNameTests</c> (the invoice's name attribute), the two
/// <c>Post_Invoice_*</c> tests in <c>OfficeQuickCreateFieldsContractTests</c>, the two <c>QuickCreateInvoice_*</c> tests in
/// <c>OfficeRecordOwnershipTests</c> and the invoice rows of <c>OfficeQuickCreateContractTests</c>' three privilege theories.
/// The refusal's full contract (every spelling, nothing read or written at any boundary) is
/// <c>OfficeInvoiceTargetRefusalContractTests</c> under <c>tests/integration/contract/Api/Office/</c>.
///
/// The route stays (Matter and Project quick-create, Matter/Project/Work Assignment saves), so this is not an absent-route test: it
/// pins that the invoice request is answered, and answered without a write, and that no Office source creates a <c>sprk_invoice</c>.
/// </summary>
[Trait("status", "repaired")]
public class OfficeInvoiceLegsRouteRetirementTests
{
    [Fact]
    public async Task OfficeQuickCreate_OfAnInvoice_IsRefused_AndNoSprkInvoiceIsCreated()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/invoice", new QuickCreateRequest { Name = "INV-2026-0042" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the invoice-only quick-create leg was retired");
        (await response.Content.ReadAsStringAsync()).Should().Contain("OFFICE_023");
        factory.Entities.Invocations.Should().BeEmpty("no generic Dataverse create — the leg that wrote sprk_name / sprk_description / sprk_assignedto1 is gone");
        factory.FieldMappings.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task OfficeSave_ToAnInvoiceTarget_IsRefused_AndTheJobIsNeverQueued()
    {
        using var factory = new OfficeTestWebAppFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata { Subject = "Invoice", SenderEmail = "billing@firm.test", SenderName = "Firm Billing" },
            TargetEntity = new SaveEntityReference { EntityType = "Invoice", EntityId = Guid.NewGuid() },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an invoice is not a save target any more");
        (await response.Content.ReadAsStringAsync()).Should().Contain("OFFICE_023");
    }

    /// <summary>
    /// No Office source (route, service, model) creates a <c>sprk_invoice</c>: no <c>new Entity("sprk_invoice")</c>, and no resolving
    /// of the Invoice quick-create enum value to its logical name. Search (<c>OfficeSearchService</c>) and the To Do "Related to" column
    /// (<c>OfficeService</c>'s <c>sprk_regardinginvoice</c>) name the invoice table to READ or LINK it; they are not matched.
    /// </summary>
    [Fact]
    public void OfficeSources_NeverCreateASprkInvoice()
    {
        var violations = Violations(OfficeSourceFiles());

        violations.Should().BeEmpty(
            "an invoice is created only by submission (FR-17, FR-20); Office reads, searches and links to one, and never creates one:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void CreateDetector_MustFire_OnASeededInvoiceCreate_AndMustNotFire_OnTheSanctionedShapes()
    {
        // Must fire: each shape the retired leg used, and its enum route.
        Violations("var entity = new Microsoft.Xrm.Sdk.Entity(\"sprk_invoice\");").Should().HaveCount(1);
        Violations("var e = new Entity( \"sprk_invoice\" ) { [\"sprk_name\"] = n };").Should().HaveCount(1);
        Violations("var logicalName = QuickCreateFieldRequirements.GetLogicalName(QuickCreateEntityType.Invoice);").Should().HaveCount(1);
        Violations("QuickCreateEntityType.Invoice => \"sprk_invoice\",").Should().HaveCount(1);

        // Must not fire: reading, searching and linking to an invoice, and the Matter / Project create.
        Violations("[AssociationEntityType.Invoice] = new(\"sprk_invoices\", \"sprk_invoiceid\", \"sprk_name\");").Should().BeEmpty();
        Violations("[\"Invoice\"] = (\"sprk_regardinginvoice\", \"sprk_invoice\"),").Should().BeEmpty();
        Violations("AssociationEntityType.Invoice => \"sprk_invoice\",").Should().BeEmpty();
        Violations("var table = QuickCreateFieldRequirements.GetLogicalName(entityType); // Matter or Project").Should().BeEmpty();
        Violations("if (parsedEntityType == QuickCreateEntityType.Invoice) return ProblemDetailsHelper.OfficeInvoiceTargetRetired(traceId);").Should().BeEmpty();
    }

    // ── The detector ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly Regex InvoiceEntityCreate = new(@"new\s+(?:[A-Za-z_.]+\.)?Entity\s*\(\s*""sprk_invoice""", RegexOptions.CultureInvariant);

    private static readonly Regex InvoiceEnumResolved = new(@"GetLogicalName\s*\(\s*QuickCreateEntityType\.Invoice|QuickCreateEntityType\.Invoice\s*=>\s*""sprk_invoice""", RegexOptions.CultureInvariant);

    private static List<string> Violations(string source) =>
        source.Split('\n')
            .Select((line, index) => (line, number: index + 1))
            .Where(l => InvoiceEntityCreate.IsMatch(l.line) || InvoiceEnumResolved.IsMatch(l.line))
            .Select(l => $"line {l.number}: {l.line.Trim()}")
            .ToList();

    private static List<string> Violations(IEnumerable<string> files) =>
        files.SelectMany(file => Violations(File.ReadAllText(file)).Select(v => $"{Path.GetRelativePath(RepoRoot(), file)} {v}")).ToList();

    private static IEnumerable<string> OfficeSourceFiles()
    {
        var bff = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api");
        var folders = new[] { Path.Combine(bff, "Api", "Office"), Path.Combine(bff, "Services", "Office"), Path.Combine(bff, "Models", "Office") };

        var files = folders.SelectMany(f => Directory.EnumerateFiles(f, "*.cs", SearchOption.AllDirectories)).ToList();
        files.Should().NotBeEmpty("an empty scan would make 'no Office source creates an invoice' trivially true");
        files.Should().Contain(f => f.EndsWith("OfficeService.cs", StringComparison.Ordinal), "the scan must include the service that held the leg");
        return files;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
