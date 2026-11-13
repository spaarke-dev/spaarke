using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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

    /// <summary>
    /// A name or number as a path writes it for a run with no person: the push reads it as its caller (here, one who may read
    /// it); the two handlers clear it, as the 045b planner clears a moved pair's display with no person (never derived app-only).
    /// </summary>
    private static string? ShownOn(GenericPath path, string? value) => path == GenericPath.FieldMappingPush ? value : null;

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
            ShownOn(path, RecordName(OtherMatter)), ShownOn(path, RecordNumber(OtherMatter)));
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
            ShownOn(path, RecordName(Project)), ShownOn(path, RecordNumber(Project)));
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "no name")]
    [InlineData(GenericPath.PlaybookUpdate, "no number")]
    [InlineData(GenericPath.OutputOrchestrator, "no name")]
    [InlineData(GenericPath.OutputOrchestrator, "no number")]
    [InlineData(GenericPath.FieldMappingPush, "no name")]
    [InlineData(GenericPath.FieldMappingPush, "no number")]
    public async Task AnInvoiceMovedToAMatterWithABlankNameOrNumber_IsWritten_WithTheTypeIdAndUrl_AndTheBlankOneCleared(GenericPath path, string gap)
    {
        // Owner, 2026-11-12, option (b): the row takes what the record has; a blank is never a refusal on the generic path.
        _recordAnswer = (table, id, columns) => new Entity(table, id)
        {
            [columns[0]] = gap == "no name" ? null : "Acme v Beta",
            [columns[1]] = gap == "no number" ? null : "MAT-000077",
        };

        // Fix round 5: the handlers run as a person who may read the matter, so their row tests the blank one alone (with no
        // person both would be cleared whatever the matter holds); the push reads as its caller either way.
        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines(OtherMatter).Object, playbookActsAs: Caller);

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

    // ── Fix round 3 ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnThePush_AMatterTheCallerMayNotRead_IsWrittenWithTheTypeIdAndUrl_AndItsNameAndNumberCleared()
    {
        // The push reads display values as its caller (as the 045b planner does there): a record the caller cannot read shows
        // them nothing, so its name and number are cleared (option (b)); the type, the id and the URL come from the catalog and
        // the id.
        _callerMayNotRead = true;

        var outcome = await RunAsync(GenericPath.FieldMappingPush, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))),
            NoPlain, WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoice", Invoice), "sprk_regardingrecordtype", "sprk_matter", OtherMatter,
            name: null, number: null);
    }

    // ── Fix round 4: on the two handlers the name and number are read as the person the run acts as ──────────────────

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task ARunAsAPersonWhoMayNotReadTheNewMatter_WritesItsTypeIdAndUrl_AndClearsItsNameAndNumber(GenericPath path)
    {
        _callerMayNotRead = true;

        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow,
            Lookups(("sprk_regardingmatter", MatterRef(OtherMatter))), NoPlain, FamilyRowUnderAMatter("sprk_invoiceapproval").Object,
            playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoiceapproval", FamilyRow), "sprk_RegardingRecordType", "sprk_matter",
            OtherMatter, name: null, number: null);
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task ARunAsAPersonWhoMayReadTheNewMatter_WritesTheNameAndNumberThePersonReads(GenericPath path)
    {
        _personAnswer = (table, id, columns) => new Entity(table, id) { [columns[0]] = "As the person sees it", [columns[1]] = "P-1" };

        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow,
            Lookups(("sprk_regardingmatter", MatterRef(OtherMatter))), NoPlain, FamilyRowUnderAMatter("sprk_invoiceapproval").Object,
            playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoiceapproval", FamilyRow), "sprk_RegardingRecordType", "sprk_matter",
            OtherMatter, "As the person sees it", "P-1");
    }

    // ── Fix round 5: a write that re-names the record the row already names derives nothing ────────────────────────────

    public static TheoryData<GenericPath, string> ReNamingRuns() => new()
    {
        { GenericPath.PlaybookUpdate, "no person" },
        { GenericPath.OutputOrchestrator, "no person" },
        { GenericPath.PlaybookUpdate, "a person" },
        { GenericPath.OutputOrchestrator, "a person" },
        { GenericPath.FieldMappingPush, "a caller who may not read it" },
        { GenericPath.FieldMappingPush, "a caller who may read it" },
    };

    [Theory]
    [MemberData(nameof(ReNamingRuns))]
    public async Task AWriteThatReNamesTheMatterTheRowAlreadyNames_LeavesItsNameAndNumberUntouched(GenericPath path, string runAs)
    {
        // The pair planner keeps a pair that does not move (045b rule 4); so does the derive. Before, a run that could not read
        // the matter (no person, or a caller with append but no read) wrote its name and number null on every re-run.
        _callerMayNotRead = runAs == "a caller who may not read it";

        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(Matter))), NoPlain,
            InvoiceUnderAMatter().Object, playbookActsAs: runAs == "a person" ? Caller : null);

        outcome.Code.Should().BeNull(outcome.Message);
        var payload = SentPayload("sprk_invoice", Invoice);
        payload.Keys.Should().NotContain(["sprk_regardingrecordname", "sprk_regardingrecordnumber"], "the row keeps the name and number it has");
        payload.Keys.Should().NotContain(ResolverColumns);
        _deriveReads.Should().BeEmpty("nothing is derived for a row whose record does not change");
    }

    // ── Fix round 5: a row that changes between the guard and the derive is refused, never written with stale fields ──

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task ARowGivenASecondCoreRecordAfterTheGuard_IsRefusedByTheDerive_AndNothingIsWritten(GenericPath path)
    {
        // The guard reads the stored invoice under one matter and admits the move; by the derive's own read (the core lookups
        // only) it is also filed under a project, which the derive's Check refuses. The path answers that code; it does not
        // move the core with stale resolver fields.
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        app.Setup(a => a.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                var row = new Entity(table, id) { ["sprk_matter"] = MatterRef(Matter) };
                if (columns.Length == 3 && columns.Contains("sprk_project") && !columns.Contains(InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn))
                    row["sprk_project"] = new EntityReference("sprk_project", Project); // the derive's read, after the change
                return Task.FromResult(row);
            });

        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain, app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        NoGuid(outcome);
        NothingWritten();
    }

    // ── Fix round 4: the push's caller read, when it fails ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("caller read times out")]
    [InlineData("no caller query")]
    public async Task OnThePush_ACallerReadThatCannotBeDone_IsRefusedReadFailed_AndNothingIsWritten(string failure)
    {
        if (failure == "no caller query")
            _noCallerQuery = true;
        else
            _callerFault = new TimeoutException("socket 10.0.0.8 timed out");

        var outcome = await RunAsync(GenericPath.FieldMappingPush, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))),
            NoPlain, WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        outcome.Message.Should().NotContain("10.0.0.8");
        NothingWritten();
    }

    [Fact]
    public async Task OnThePush_A403OnAMatterThatDoesNotExist_IsRefusedRecordNotFound_AndNothingIsWritten()
    {
        _callerMayNotRead = true;
        _recordAnswer = (_, _, _) => null; // the app's exists-check finds no matter

        var outcome = await RunAsync(GenericPath.FieldMappingPush, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))),
            NoPlain, WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnApprovalMovedToAnotherMatter_IsWrittenWithThatMattersResolverFields(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoiceapproval", FamilyRow,
            Lookups(("sprk_regardingmatter", MatterRef(OtherMatter))), NoPlain, FamilyRowUnderAMatter("sprk_invoiceapproval").Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoiceapproval", FamilyRow), "sprk_RegardingRecordType", "sprk_matter",
            OtherMatter, ShownOn(path, RecordName(OtherMatter)), ShownOn(path, RecordNumber(OtherMatter)));
    }

    [Fact]
    public async Task AnExternalIdentifierMovedToInvoiceOnly_IsWrittenWithItsInvoicesResolverFields()
    {
        // D-68: the core lookup cleared, the invoice kept; the row now regards the invoice.
        var app = FamilyRowUnderAMatter("sprk_externalidentifier",
            (InvoiceFamilyCoreRecordGuard.ExternalIdentifierInvoiceColumn, new EntityReference("sprk_invoice", Invoice)));

        var outcome = await RunAsync(GenericPath.OutputOrchestrator, "sprk_externalidentifier", FamilyRow,
            Lookups(("sprk_regardingmatter", null)), NoPlain, app.Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_externalidentifier", FamilyRow), "sprk_RegardingRecordType", "sprk_invoice",
            Invoice, name: null, number: null);
    }

    [Fact]
    public async Task APushThatSendsThePair_AndMovesTheCoreLookup_IsWrittenWithTheDerivedPair_NotThePushedOne()
    {
        var pushed = new Dictionary<string, object?>
        {
            ["sprk_regardingrecordid"] = Matter.ToString("D"),
            ["sprk_regardingrecordname"] = "Pushed name",
            ["sprk_regardingrecordnumber"] = "PUSHED-1",
        };

        var outcome = await RunAsync(GenericPath.FieldMappingPush, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))),
            pushed, WorldWithLines(OtherMatter).Object);

        outcome.Code.Should().BeNull(outcome.Message);
        ShouldCarryTheResolverFieldsOf(SentPayload("sprk_invoice", Invoice), "sprk_regardingrecordtype", "sprk_matter", OtherMatter,
            RecordName(OtherMatter), RecordNumber(OtherMatter));
    }

    [Fact]
    public async Task ACancellationDuringTheDerive_IsRethrown_AndNothingIsWritten()
    {
        using var cancel = new CancellationTokenSource();
        _catalogAnswer = _ =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        };

        var act = () => Handler(WorldWithLines(OtherMatter).Object).UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["sprk_matter"] = MatterRef(OtherMatter) },
            Sprk.Bff.Api.Services.Ai.ConcurrencyMode.None, maxRetries: 1, cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        NothingWritten();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AStoredRowThatDoesNotExist_DerivesNothing_AndIsLeftToThePath(bool answersNotFoundFault, bool writeIsImpersonated)
    {
        // The derive reads the stored row only to derive from it: a row that does not exist gets no derive code of its own
        // (the guard before it, or the update-only write after it, answers it as any missing row).
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var read = app.Setup(a => a.RetrieveAsync("sprk_invoiceapproval", MissingRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (answersNotFoundFault) read.ThrowsAsync(ObjectDoesNotExist()); else read.ReturnsAsync((Entity)null!);

        var derived = await InvoiceFamilyCoreRecordGuard.DeriveForGenericUpdateAsync("sprk_invoiceapproval", MissingRow,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = MatterRef(OtherMatter) }, app.Object,
            InvoiceFamilyCoreRecordGuard.AppOnlyRecords(app.Object), CancellationToken.None, writeIsImpersonated);

        derived.Should().BeNull();
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
