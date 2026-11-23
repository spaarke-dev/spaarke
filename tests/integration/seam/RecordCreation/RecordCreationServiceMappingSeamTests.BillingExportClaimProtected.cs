using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// ebilling-r1 task 165d (EB-D-207): the AP export claim (<c>sprk_invoice.sprk_billingexport</c>, <see cref="ApExportService.BatchLookup"/>) is a
/// mapping-protected column (<see cref="RecordTableRule.MappingProtectedColumns"/>) and a writer-only one
/// (<see cref="RecordTableRule.WriterOnlyColumns"/>): only AP export sets and clears it. Same seams as the invoice amounts: a post-mapping default
/// is checked against the protected set alone (must fire if the entry is removed), the set is per table (must not fire), and a create-time rule
/// aimed at the column is skipped with the existing warning while an unprotected target still maps.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    [Fact(DisplayName = "165d: a post-mapping default aimed at the invoice's AP export claim is an argument error (mapping never writes it)")]
    public async Task PostMappingDefault_TheApExportClaim_IsAnArgumentError()
    {
        var act = () => Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            },
                mapping: true) with
            { PostMappingDefaults = new Dictionary<string, object?> { [ApExportService.BatchLookup] = 1 } },
            AsCaller(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message
            .Should().Contain(ApExportService.BatchLookup).And.Contain("field mapping never writes it");
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "165d: only the invoice protects the AP export claim from mapping, and its other columns stay mappable")]
    public void ProtectedFromMapping_TheApExportClaim_IsOnTheInvoiceOnly()
    {
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().Contain(ApExportService.BatchLookup);
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().NotContain("sprk_description", "an ordinary column stays mappable");
        RecordCreationService.ProtectedFromMapping("sprk_billingevent", RecordTableRules.Get("sprk_billingevent"), null)
            .Should().NotContain(ApExportService.BatchLookup, "only the invoice carries the claim");
    }

    [Fact(DisplayName = "165d: a create-time rule aimed at the AP export claim is skipped with the warning, and an unprotected target of the same profile still maps")]
    public async Task CreateAsync_ARuleTargetingTheApExportClaim_IsSkippedWithTheWarning_AndAnOrdinaryRuleIsStillFilled()
    {
        _user.Columns["sprk_invoice"][ApExportService.BatchLookup] = ("String", true);
        Profile("sprk_matter", "sprk_invoice",
            Rule(null, $"  {ApExportService.BatchLookup.ToUpperInvariant()} ", Text, Default, "x"),
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-165D",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: true),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => Normalized(k).StartsWith(Normalized(ApExportService.BatchLookup), StringComparison.Ordinal),
            "the claim is AP export's alone (must fire)");
        StringOf(fields, "sprk_description").Should().Be("From the matter", "an unprotected target of the same profile still maps (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains(ApExportService.BatchLookup));
    }

    // ── The create path (165d fix round, EB-D-207): an invoice must not be created already claimed ──────────────────

    [Theory(DisplayName = "165d fix round: a CALLER's create naming the AP export claim is refused (blocked column, the writer-only code, nothing created), in every key form")]
    [InlineData("sprk_BillingExport@odata.bind")]
    [InlineData("sprk_billingexport@odata.bind")]
    [InlineData("sprk_billingexport")]
    [InlineData("_sprk_billingexport_value")]
    public async Task CreateAsync_ACallersInvoiceNamingTheApExportClaim_IsRefused(string key)
    {
        var outcome = await Service().CreateAsync(InvoiceNaming(key), AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull("the claim is AP export's alone (must fire)");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Code.Should().Be(RecordTableRules.ChangedOnlyByItsWriterColumnReasonCode);
        outcome.Refusal.Detail.Should().Contain(ApExportService.BatchLookup).And.Contain("changed only by Spaarke itself");
        Problem(outcome, "sprk_invoice").Status.Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden);
        _creates.Should().BeEmpty();
    }

    [Theory(DisplayName = "165d fix round: a SYSTEM create naming the AP export claim is refused too (the caller's column policy is skipped, this column's is not)")]
    [InlineData("sprk_billingexport")]
    [InlineData("sprk_BillingExport@odata.bind")]
    public async Task CreateAsync_ASystemInvoiceNamingTheApExportClaim_IsRefused(string key)
    {
        var outcome = await Service().CreateAsync(
            InvoiceNaming(key), RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Code.Should().Be(RecordTableRules.ChangedOnlyByItsWriterColumnReasonCode);
        _creates.Should().BeEmpty();
    }

    /// <summary>An invoice create whose (already mapped) item names <paramref name="key"/> beside an ordinary column.</summary>
    private static RecordCreationRequest InvoiceNaming(string key) =>
        new("sprk_invoice", new RecordCreationPayload.Mapped(new DataverseWriteItemMapper.MappedItem(
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-165D",
                [key] = "/sprk_billingexports(165d0000-0000-0000-0000-00000000ba7c)",
            }),
            ["sprk_invoicenumber", key])))
        {
            CorrelationId = FixedCorrelation,
        };

    [Fact(DisplayName = "165d fix round must-not-fire: the same invoice create without the claim, or with an unprotected column, is not blocked")]
    public async Task CreateAsync_AnInvoiceNamingNoClaim_IsNotBlocked()
    {
        RecordCreationService.CallerBlockedColumnRefusal(RecordTableRules.Get("sprk_invoice"), "invoice", ["sprk_invoicenumber", "sprk_description"])
            .Should().BeNull("an unprotected column creates");
        RecordCreationService.CallerBlockedColumnRefusal(RecordTableRules.Get("sprk_billingevent"), "line", [ApExportService.BatchLookup])
            .Should().BeNull("only the invoice carries the claim");
        RecordCreationService.CallerBlockedColumnRefusal(RecordTableRules.Get("sprk_invoice"), "invoice", ["sprk_BillingExport@odata.bind"])
            .Should().NotBeNull();

        var outcome = await Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-165D",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: false),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle();
    }

    // ── K1 (165d fix round): the invoices can also be associated FROM the batch row, through the collection-valued navigation ──────────

    [Fact(DisplayName = "165d fix round K1: no generic writer creates a batch row, so the batch-side navigation (sprk_invoice_billingexport_billingexport) cannot associate invoices on create")]
    public async Task CreateAsync_ABatchRowNamingTheInvoiceNavigation_IsNotCreatedHere()
    {
        // The batch table is in no registry and on no create list: AP export is its only BFF creator. A collection-valued @odata.bind is accepted by
        // Dataverse on a POST only, so the navigation needs no refusal of its own while this holds; if the table is ever registered or listed, this fails
        // and the navigation must be added to a WriterOnlyColumns set first.
        RecordTableRules.Find("sprk_billingexport").Should().BeNull();
        Sprk.Bff.Api.Api.ChildRecordEndpoints.CreateTables.Should().NotContain("sprk_billingexport");

        var outcome = await Service().CreateAsync(
            Request("sprk_billingexport", new Dictionary<string, object?>
            {
                ["sprk_name"] = "BATCH-165D",
                ["sprk_invoice_billingexport_billingexport@odata.bind"] = new[] { $"/sprk_invoices({Guid.NewGuid():D})" },
            }, mapping: false),
            AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.TableUnsupportedCode);
        _creates.Should().BeEmpty();
    }
}
