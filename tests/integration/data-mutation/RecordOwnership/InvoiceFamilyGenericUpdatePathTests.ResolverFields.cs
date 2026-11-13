using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045d (owner ruling Q8 (a); owner, 2026-11-12, option (b)): on the three app-only update
/// paths — the playbook update, the output orchestrator and the field-mapping push — a write that sets or changes an
/// invoice-family row's own core lookup carries, in the same write, the resolver fields of the record the row will name, read
/// app-only: the type, the id and the URL always, the name and the number when the record has them. A derive that cannot be
/// done is answered as each path answers the invoice-family guard (its code only); a write that moves no core lookup reads
/// nothing for it. Driven through the real paths (<see cref="RunAsync"/>).
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private static readonly string[] ResolverColumns =
    [
        "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
    ];

    /// <summary>The one payload sent for the row (the update-only write; the upsert on a path that still uses it).</summary>
    private Dictionary<string, object?> SentPayload(string table, Guid id) =>
        (Dictionary<string, object?>)_writes.Invocations
            .Single(i => i.Method.Name is nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync)
                             or nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync)
                         && (string)i.Arguments[0] == table && (Guid)i.Arguments[1] == id)
            .Arguments[2];

    private static void ShouldCarryTheResolverFieldsOf(
        Dictionary<string, object?> payload, string navigation, string core, Guid id, string? name, string? number)
    {
        var lowerId = id.ToString("D").ToLowerInvariant();
        payload[$"{navigation}@odata.bind"].Should().Be($"/sprk_recordtype_refs({CatalogRowOf(core):D})");
        payload["sprk_regardingrecordid"].Should().Be(lowerId);
        payload["sprk_regardingrecordurl"].Should().Be($"/main.aspx?pagetype=entityrecord&etn={core}&id={lowerId}");
        payload["sprk_regardingrecordname"].Should().Be(name);
        payload["sprk_regardingrecordnumber"].Should().Be(number);
    }

    private static string RecordName(Guid id) => $"Record {id.ToString("N")[..6]}";

    private static string RecordNumber(Guid id) => $"N-{id.ToString("N")[..6]}";

    // ── Must fire: the core lookup moves, the resolver fields follow it in the same write ───────────────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnInvoiceMovedToAnotherMatter_IsWrittenWithThatMattersResolverFields(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoice", Invoice), "sprk_regardingrecordtype", "sprk_matter", OtherMatter,
            RecordName(OtherMatter), RecordNumber(OtherMatter));
        _deriveReads.Should().Equal("catalog:sprk_matter", "record:sprk_matter");
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AnInvoiceWithNoLines_MovedFromItsMatterToAProject_IsWrittenWithTheProjectsResolverFields(GenericPath path)
    {
        // A core-table switch (the push cannot clear the matter, so it is not run on it).
        var outcome = await RunAsync(path, "sprk_invoice", Invoice,
            Lookups(("sprk_matter", null), ("sprk_project", new EntityReference("sprk_project", Project))), NoPlain,
            InvoiceUnderAMatter().Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoice", Invoice), "sprk_regardingrecordtype", "sprk_project", Project,
            RecordName(Project), RecordNumber(Project));
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "no name")]
    [InlineData(GenericPath.OutputOrchestrator, "no number")]
    [InlineData(GenericPath.FieldMappingPush, "no name")]
    public async Task AnInvoiceMovedToAMatterWithABlankNameOrNumber_IsWritten_WithTheTypeIdAndUrl_AndTheBlankOneCleared(GenericPath path, string gap)
    {
        // Owner, 2026-11-12, option (b): the row takes what the record has; a blank is never a refusal on the generic path.
        _recordAnswer = (table, id, columns) => new Entity(table, id)
        {
            [columns[0]] = gap == "no name" ? null : "Acme v Beta",
            [columns[1]] = gap == "no number" ? null : "MAT-000077",
        };

        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoice", Invoice), "sprk_regardingrecordtype", "sprk_matter", OtherMatter,
            gap == "no name" ? null : "Acme v Beta", gap == "no number" ? null : "MAT-000077");
    }

    // ── Fail closed: a derive that cannot be done is the path's existing invoice-family refusal ─────────────────────

    public static TheoryData<GenericPath, string> FailureCases()
    {
        var data = new TheoryData<GenericPath, string>();
        foreach (var path in Enum.GetValues<GenericPath>())
        {
            data.Add(path, "no catalog row");
            data.Add(path, "catalog read fault");
            data.Add(path, "matter gone");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task ADeriveThatCannotBeDone_IsRefusedWithTheGuardsCodeOnly_AndNothingIsWritten(GenericPath path, string failure)
    {
        switch (failure)
        {
            case "no catalog row":
                _catalogAnswer = _ => null;
                break;
            case "catalog read fault":
                _catalogAnswer = _ => throw new TimeoutException("catalog timed out");
                break;
            default:
                _recordAnswer = (_, _, _) => null;
                break;
        }

        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().Be(failure switch
        {
            "no catalog row" => InvoiceFamilyCoreRecordGuard.ResolverFieldsIncompleteCode,
            "catalog read fault" => InvoiceFamilyCoreRecordGuard.ReadFailedCode,
            _ => InvoiceFamilyCoreRecordGuard.RecordNotFoundCode,
        });
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AMatterGoneByTheDerive_OnAWriteRunAsAPerson_IsReadFailed_AsTheGuardAnswersThere(GenericPath path)
    {
        _recordAnswer = (_, _, _) => null;

        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines(OtherMatter).Object, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "a run as a person never tells found from not-found");
        NothingWritten();
    }

    // ── Must not fire: a write that moves no core lookup reads nothing for the derive ───────────────────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnInvoiceRenamedOnly_ReadsNothingForTheDerive_AndWritesNoneOfTheResolverFields(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoice", Invoice, NoLookups, RenameOnly, WorldWithLines().Object);

        outcome.Code.Should().BeNull(outcome.Message);
        _deriveReads.Should().BeEmpty();
        var payload = SentPayload("sprk_invoice", Invoice);
        payload.Keys.Should().NotContain(ResolverColumns).And.NotContain("sprk_regardingrecordtype@odata.bind");
    }
}
