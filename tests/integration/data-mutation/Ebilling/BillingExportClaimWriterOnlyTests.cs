using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 task 165d (EB-D-207; unified-access-control-r2 U1-U4, approved 2026-11-07): the AP export claim
/// (<c>sprk_invoice.sprk_billingexport</c>, <see cref="ApExportService.BatchLookup"/>) is set and cleared only by AP export. Every generic
/// writer that asks <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> is refused a payload naming it, for EVERY <c>WriteIdentity</c> (U1):
/// the Update Record node (<see cref="UpdateRecordActionCore"/>), a playbook's output mapping (<see cref="DataverseUpdateHandler"/>) and
/// Repair run app-only, the field-mapping push runs as its caller. The refusal extends the <c>changed_only_by_its_writer</c> marker and names
/// the column (U2). Must fire: each writer, in the plain, case-variant, <c>@odata.bind</c> and <c>_{column}_value</c> forms, set and cleared.
/// Must not fire: the same writer onto an ordinary invoice column, the same name on other tables, a neighbouring name. AP export's own claim
/// and release run untouched next to the refusal (U4; the claim and release tests in <c>ApExportBatchTests</c> are unmodified).
/// </summary>
public class BillingExportClaimWriterOnlyTests
{
    private const string Invoice = "sprk_invoice";
    private const string Claim = ApExportService.BatchLookup;
    private static readonly string MarkerFamily = RecordTableRules.ChangedOnlyByItsWriterReasonCode;
    private static readonly string Code = RecordTableRules.ChangedOnlyByItsWriterColumnReasonCode;

    private static readonly Guid Row = Guid.Parse("165d0000-0000-0000-0000-00000000e401");
    private static readonly Guid Caller = Guid.Parse("165d0000-0000-0000-0000-00000000e4ca");
    private static readonly Guid Batch = Guid.Parse("165d0000-0000-0000-0000-00000000ba7c");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>The column in every form a writer sends it.</summary>
    public static TheoryData<string> KeyForms() => new()
    {
        Claim,
        $" {Claim.ToUpperInvariant()} ",
        "sprk_BillingExport",
        $"{Claim}@odata.bind",
        $"sprk_BillingExport@odata.bind",
        $"_{Claim}_value",
    };

    private static string? CodeOf(string? table, string key, InheritedAccessPermission.WriteIdentity identity) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, [key], identity)?.RefusalCode;

    // ── The refusal itself (U1: both identities; U2: the marker family and the column named) ─────────────────────

    [Theory(DisplayName = "165d U1/U2: the AP export claim is refused for the app-only AND the caller identity, in every key form, with the marker family and the column named")]
    [MemberData(nameof(KeyForms))]
    public void TheClaim_IsRefused_ForEveryIdentity(string key)
    {
        foreach (var identity in new[] { InheritedAccessPermission.WriteIdentity.AppOnly, InheritedAccessPermission.WriteIdentity.Caller })
        {
            var refused = InheritedAccessPermission.RefuseAppOnlyWrite(Invoice, [key], identity);

            refused.Should().NotBeNull($"{key} as {identity}");
            refused!.RefusalCode.Should().Be(Code, $"{key} as {identity}");
            refused.RefusalCode.Should().StartWith(MarkerFamily, "the gate query counts the changed_only_by_its_writer marker");
            refused.Reason.Should().StartWith($"{Claim} of a {Invoice} ").And.Contain("nothing was written").And.Contain(MarkerFamily);
        }

        // The table is matched trimmed and in any case.
        CodeOf($"  {Invoice.ToUpperInvariant()}\t", key, InheritedAccessPermission.WriteIdentity.Caller).Should().Be(Code);
    }

    [Fact(DisplayName = "165d U1: a payload that names the claim among ordinary columns is refused whole, and the refusal names the claim")]
    public void TheClaim_AmongOrdinaryColumns_IsRefused()
    {
        InheritedAccessPermission.RefuseAppOnlyWrite(Invoice, ["sprk_description", $"{Claim}@odata.bind", "sprk_invoicenumber"])!
            .Reason.Should().StartWith($"{Claim} of a {Invoice} ");
    }

    [Fact(DisplayName = "165d must-not-fire: an ordinary invoice column, the same name on another table, a neighbouring name and a null table are not refused")]
    public void OtherWrites_AreNotRefused()
    {
        foreach (var identity in new[] { InheritedAccessPermission.WriteIdentity.AppOnly, InheritedAccessPermission.WriteIdentity.Caller })
        {
            CodeOf(Invoice, "sprk_description", identity).Should().BeNull("an ordinary invoice column");
            CodeOf(Invoice, "sprk_invoicenumber", identity).Should().BeNull("an ordinary invoice column");
            CodeOf(Invoice, Claim + "s", identity).Should().BeNull("a neighbouring name");
            CodeOf(Invoice, "x" + Claim, identity).Should().BeNull("a neighbouring name");
            CodeOf("sprk_billingevent", Claim, identity).Should().BeNull("a line carries no export claim");
            CodeOf("sprk_invoicesubmission", Claim, identity).Should().BeNull("the submission carries no export claim");
            CodeOf("sprk_billingexport", "sprk_name", identity).Should().BeNull("the batch row's own columns are ordinary");
            CodeOf("sprk_matter", Claim, identity).Should().BeNull("only the invoice carries the claim");
        }
    }

    [Fact(DisplayName = "165d: the registry declares the claim as a writer-only column of the invoice alone, and as mapping-protected")]
    public void TheRegistry_DeclaresTheClaim()
    {
        RecordTableRules.Get(Invoice).WriterOnlyColumns.Should().BeEquivalentTo([Claim]);
        RecordTableRules.Get(Invoice).MappingProtectedColumns.Should().Contain(Claim);
        RecordTableRules.All.Where(r => r.WriterOnlyColumns.Count > 0).Select(r => r.Table).Should().Equal(Invoice);

        RecordTableRules.RefuseWriterOnlyColumns(Invoice, ["sprk_description"]).Should().BeNull();
        RecordTableRules.RefuseWriterOnlyColumns(null, [Claim]).Should().BeNull();
        RecordTableRules.RefuseWriterOnlyColumns("sprk_unregisteredfixture", [Claim]).Should().BeNull();
    }

    // ── The Update Record node (UpdateRecordActionCore, app-only) ────────────────────────────────────────────────

    [Theory(DisplayName = "165d: the Update Record node is refused a write of the claim (set, cleared and as a lookup), and nothing is written")]
    [MemberData(nameof(KeyForms))]
    public async Task TheUpdateRecordNode_IsRefused(string key)
    {
        var set = () => Core().UpdateAsync(
            new UpdateRecordActionInput(Invoice, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [key] = Batch.ToString("D") }, Lookups: null),
            CancellationToken.None);
        (await set.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(Code, key);

        var cleared = () => Core().UpdateAsync(
            new UpdateRecordActionInput(Invoice, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [key] = "" }, Lookups: null),
            CancellationToken.None);
        (await cleared.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(Code, key);

        var lookup = () => Core().UpdateAsync(
            new UpdateRecordActionInput(Invoice, Row, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup(key, "sprk_billingexport", Batch.ToString("D"))]),
            CancellationToken.None);
        (await lookup.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(Code, key);

        _writes.Invocations.Should().BeEmpty(key);
    }

    [Fact(DisplayName = "165d must-not-fire: the Update Record node still writes an ordinary invoice column, and the claim's name on another table")]
    public async Task TheUpdateRecordNode_StillWritesOtherColumns()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(Invoice, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_description"] = "reviewed" }, Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Invoice, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            null, It.IsAny<CancellationToken>()), Times.Once);

        // The same column name on a table that does not declare it as writer-only is not this refusal's to make.
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_billingevent", [Claim]).Should().BeNull();
    }

    // ── A playbook's output mapping (DataverseUpdateHandler, app-only) ───────────────────────────────────────────

    [Theory(DisplayName = "165d: a playbook's output mapping is refused a write of the claim (set and cleared), and nothing is written")]
    [MemberData(nameof(KeyForms))]
    public async Task TheOutputMapping_IsRefused(string key)
    {
        foreach (var value in new object?[] { null, new EntityReference("sprk_billingexport", Batch), Batch.ToString("D") })
        {
            _writes.Invocations.Clear();
            var mapping = () => Handler().UpdateAsync(
                Invoice, Row, new Dictionary<string, object?> { [key] = value },
                ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

            (await mapping.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(Code, key);
            _writes.Invocations.Should().BeEmpty(key);
        }
    }

    [Fact(DisplayName = "165d must-not-fire: a playbook's output mapping still writes an ordinary invoice column")]
    public async Task TheOutputMapping_StillWritesOtherColumns()
    {
        await Handler().UpdateAsync(
            Invoice, Row, new Dictionary<string, object?> { ["sprk_description"] = "a summary" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Invoice, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The field-mapping push (runs as its caller: WriteIdentity.Caller) ────────────────────────────────────────

    [Theory(DisplayName = "165d U1: a field-mapping push rule aimed at the claim is skipped (the caller identity is refused too), the rest of the push applies, and the claim is never sent")]
    [MemberData(nameof(KeyForms))]
    public async Task TheFieldMappingPush_SkipsTheRule(string key)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            Invoice, Rule("sprk_name", "sprk_description"), Rule("sprk_name", key));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        results.Should().ContainSingle(r => r.Status == FieldMappingStatus.Skipped)
            .Which.ErrorMessage.Should().Contain(Claim).And.Contain(MarkerFamily);
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Invoice, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "165d must-not-fire: a push onto only ordinary invoice columns is sent as the caller with every column")]
    public async Task TheFieldMappingPush_StillSendsOtherColumns()
    {
        var (updated, failed, skipped, errors, results) = await Push(
            Invoice, Rule("sprk_name", "sprk_description"), Rule("sprk_name", "sprk_invoicenumber"));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        results.Should().NotContain(r => r.Status == FieldMappingStatus.Skipped);
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Invoice, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 2 && f.ContainsKey("sprk_description") && f.ContainsKey("sprk_invoicenumber")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Repair (RecordCreationService.RepairAsync, app-only) ─────────────────────────────────────────────────────

    [Fact(DisplayName = "165d: Repair never maps the claim (its per-rule ask reports the code), and nothing it writes names the claim; an ordinary rule is not refused")]
    public async Task Repair_NeverMapsTheClaim()
    {
        var world = new RepairWorld();
        world.MatterLinksTo();
        world.Map("sprk_matter", Invoice,
            RepairWorld.DefaultText(Claim, Batch.ToString("D"), 1),
            RepairWorld.DefaultText("sprk_description", "from the matter", 2));
        var id = world.InvoiceUnderTheMatter();

        var outcome = await world.RepairAsync(Invoice, id);

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Mapping && f.Column == Claim)
            .Which.ReasonCode.Should().Be(Code);
        outcome.Fields.Where(f => f.Column == "sprk_description").Should().NotContain(f => f.ReasonCode == Code, "an ordinary rule is not refused (must not fire)");
        world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(k => k.Contains(Claim, StringComparison.OrdinalIgnoreCase));
    }

    // ── AP export's own claim and release (U4): untouched, next to the refusal ──────────────────────────────────

    [Fact(DisplayName = "165d U4: AP export still claims an invoice for its batch, while a generic writer naming the same column is refused for both identities")]
    public async Task ApExportClaim_IsWritten_WhileTheGenericWriterIsRefused()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A", approved: 900m);
        world.GrantEverything();

        var result = await world.ExportAsync(a);

        result.Outcome.Should().Be(ApExportOutcome.Created, result.Detail);
        var batch = world.Batches().Should().ContainSingle().Subject;
        world.BatchOfInvoice(a.Id).Should().Be(batch.Id, "AP export wrote the claim through its own seam");

        CodeOf(Invoice, Claim, InheritedAccessPermission.WriteIdentity.AppOnly).Should().Be(Code);
        CodeOf(Invoice, Claim, InheritedAccessPermission.WriteIdentity.Caller).Should().Be(Code);
    }

    [Fact(DisplayName = "165d U4: AP export still RELEASES a claim (a status change after the claim), while a generic writer naming the same column is refused")]
    public async Task ApExportRelease_IsWritten_WhileTheGenericWriterIsRefused()
    {
        var world = new ApExportWorld();
        var a = world.AddInvoice("INV-A");
        var b = world.AddInvoice("INV-B");
        world.GrantEverything();
        var last = a.Id.CompareTo(b.Id) < 0 ? b : a;
        world.Dataverse.BeforeConditionalUpdate = (table, id) =>
        {
            if (table == "sprk_invoice" && id == last.Id)
            {
                world.Dataverse.BeforeConditionalUpdate = null;
                var first = ReferenceEquals(last, a) ? b : a;
                world.Dataverse.Touch("sprk_invoice", first.Id, new Dictionary<string, object> { ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Cancelled) });
            }
        };

        var result = await world.ExportAsync(a, b);

        result.Outcome.Should().Be(ApExportOutcome.Conflict);
        world.BatchOfInvoice(a.Id).Should().BeNull("the release cleared the claim through AP export's own seam");
        world.BatchOfInvoice(b.Id).Should().BeNull();

        CodeOf(Invoice, Claim, InheritedAccessPermission.WriteIdentity.AppOnly).Should().Be(Code);
        CodeOf(Invoice, Claim, InheritedAccessPermission.WriteIdentity.Caller).Should().Be(Code);
    }

    // ── Harness (as AccessTableAppOnlyWriterTests) ───────────────────────────────────────────────────────────────

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Invoice 165d" },
            table,
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None,
            appOnlyEntities: InvoiceUnderAMatter());

    /// <summary>The app-only reader the invoice-family guard reads: a stored invoice filed under a matter, with no lines.</summary>
    private static IGenericEntityService InvoiceUnderAMatter()
    {
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveAsync(Invoice, Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(Invoice, Row) { ["sprk_matter"] = new EntityReference("sprk_matter", Guid.Parse("165d0000-0000-0000-0000-00000000a001")) });
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        app.Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, CancellationToken _) => table == "sprk_billingexport" ? "sprk_billingexports" : EntitySetNameFixtures.SetOf(table));
        return app.Object;
    }

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection()
            .AddSingleton(InvoiceUnderAMatter())
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private DataverseUpdateHandler Handler() =>
        new(_writes.Object, InvoiceUnderAMatter(), new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
}
