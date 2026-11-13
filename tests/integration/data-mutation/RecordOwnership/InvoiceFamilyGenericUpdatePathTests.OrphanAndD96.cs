using System.ServiceModel;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1, #2081 follow-up (split out by unified-access-control-r2 from task 026's review). The three generic update
/// paths PATCH with no If-Match, which is an UPSERT. For <c>sprk_invoice</c> and <c>sprk_billingevent</c> the guard now reads the
/// STORED row (app-only, by id) for every write and refuses a missing row, so a stale or deleted id can never create an orphan
/// row (no core record, owned by the app). Also here, through each path: an invoice's core record fixed at create and a line
/// that keeps its invoice (task 045g rules 1 and 3), D-96's line side, the "no <see cref="IGenericEntityService"/>" case, and
/// the return-and-resubmit hint on those refusals.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    public enum GenericPath
    {
        /// <summary>The playbook update action: <see cref="UpdateRecordActionCore"/>.</summary>
        PlaybookUpdate,

        /// <summary>The output orchestrator: <see cref="DataverseUpdateHandler"/>.</summary>
        OutputOrchestrator,

        /// <summary>The field-mapping push: <c>ApplyMappingsToChildRecordsAsync</c>.</summary>
        FieldMappingPush,
    }

    private static readonly Guid MissingRow = Guid.Parse("02200000-0000-4000-8000-0000000000b1");

    private static FaultException<OrganizationServiceFault> ObjectDoesNotExist() =>
        new(new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist"));

    /// <summary>What a path did with a write: the refusal it surfaced (null = the write went ahead) and its text.</summary>
    private sealed record PathOutcome(string? Code, string? Message);

    private static readonly Regex GuardCode = new("invoice_family_[a-z_]+");

    /// <summary>
    /// ONE write — lookups (column → record, or null to clear) and plain columns — expressed in each path's own input, run
    /// through the real path, and read back as a refusal code and text. The push can set a lookup or a text column but never
    /// clear one (a null source value is skipped), so a clearing write is not run on it.
    /// </summary>
    private async Task<PathOutcome> RunAsync(
        GenericPath path, string table, Guid id,
        IReadOnlyDictionary<string, EntityReference?> lookups, IReadOnlyDictionary<string, object?> plain,
        IGenericEntityService? app, Guid? playbookActsAs = null, CancellationToken ct = default)
    {
        switch (path)
        {
            case GenericPath.OutputOrchestrator:
                {
                    var fields = new Dictionary<string, object?>(plain);
                    foreach (var (column, record) in lookups)
                        fields[column] = record;
                    try
                    {
                        // Master merge (#2257): a person's run writes its output mapping AS that person (playbookActsAs).
                        await Handler(app!).UpdateAsync(
                            table, id, fields, Sprk.Bff.Api.Services.Ai.ConcurrencyMode.None, maxRetries: 1, ct, playbookActsAs);
                        return new PathOutcome(null, null);
                    }
                    catch (RecordOwnerUnresolvedException refused)
                    {
                        return new PathOutcome(refused.RefusalCode, refused.Message);
                    }
                }

            case GenericPath.PlaybookUpdate:
                {
                    var legacy = plain.ToDictionary(p => p.Key, p => (string?)p.Value?.ToString());
                    var rendered = new List<RenderedLookup>();
                    foreach (var (column, record) in lookups)
                    {
                        if (record is null)
                            legacy[$"{column}@odata.bind"] = null; // a null value clears the lookup
                        else
                            rendered.Add(new RenderedLookup(column, record.LogicalName, record.Id.ToString()));
                    }

                    try
                    {
                        await CoreOver(app).UpdateAsync(
                            new UpdateRecordActionInput(table, id, FieldMappings: null, LegacyFields: legacy, Lookups: rendered,
                                ImpersonateSystemUserId: playbookActsAs),
                            ct);
                        return new PathOutcome(null, null);
                    }
                    catch (RecordOwnerUnresolvedException refused)
                    {
                        return new PathOutcome(refused.RefusalCode, refused.Message);
                    }
                }

            default:
                {
                    lookups.Values.Should().NotContain((EntityReference?)null, "the push cannot clear a lookup");
                    var rules = new List<FieldMappingRuleDto>();
                    var values = new Dictionary<string, object?>();
                    var binds = new Dictionary<FieldMappingRuleDto, LookupBindPlan>();
                    foreach (var (column, record) in lookups)
                    {
                        var rule = new FieldMappingRuleDto
                        {
                            SourceField = column,
                            TargetField = column,
                            SourceFieldType = "Lookup",
                            TargetFieldType = "Lookup",
                            Priority = 1,
                        };
                        rules.Add(rule);
                        values[column] = record!.Id;
                        binds[rule] = new LookupBindPlan($"{column}@odata.bind", $"/{record.LogicalName}s({record.Id:D})", null);
                    }

                    foreach (var (column, value) in plain)
                    {
                        rules.Add(new FieldMappingRuleDto
                        {
                            SourceField = column,
                            TargetField = column,
                            SourceFieldType = "Text",
                            TargetFieldType = "Text",
                            Priority = 2,
                        });
                        values[column] = value;
                    }

                    if (app is not null)
                    {
                        AnswerDeriveReads(app);
                        // Task 045d fix round 3: the push reads the derived name and number as its caller (CallerDisplayReader),
                        // which names the record's entity set from metadata.
                        if (app is IMocked<IGenericEntityService> mocked)
                        {
                            mocked.Mock.Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                                .ReturnsAsync((string t, CancellationToken _) => EntitySetNameFixtures.SetOf(t));
                        }
                    }

                    // Task 045g fix round 1: the push writes as its caller, who reads each row's lookups as the app does unless
                    // a test says otherwise (_personRows).
                    _personReadsAsTheApp = app;
                    var (_, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
                Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
                        _writes.Object, new StampWorld().Restamper, rules.ToArray(), values, table, [id], Caller,
                        NullLogger.Instance, ct, scopes: PushScopes(), lookupBinds: binds, appOnlyEntities: app,
                        callerQuery: CallerQuery());
                    if (failed == 0)
                        return new PathOutcome(null, null);

                    var message = errors.Should().ContainSingle().Subject.Error ?? string.Empty;
                    return new PathOutcome(GuardCode.Match(message).Value, message);
                }
        }
    }

    /// <summary>The playbook core over a scope that holds <paramref name="app"/>, or holds no entity service at all.</summary>
    private UpdateRecordActionCore CoreOver(IGenericEntityService? app)
    {
        var services = new ServiceCollection()
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble());
        if (app is not null)
        {
            // uac-r2 #2091 round 4 (master): the core reads a lookup's entity set from metadata through this same service.
            Mock.Get(app)
                .Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string table, CancellationToken _) => EntitySetNameFixtures.SetOf(table));
            AnswerDeriveReads(app);
            services.AddSingleton(app);
        }

        // Task 045d fix round 4: the person the run acts as reads the derived name and number (MovedDisplayReader).
        if (CallerQuery() is { } personQuery)
            services.AddSingleton(personQuery);

        return new UpdateRecordActionCore(_writes.Object, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance);
    }

    /// <summary>The ownership resolver the push's re-file asks (it records each reparent and then runs the write).</summary>
    private readonly RecordOwnershipResolverDouble _pushOwnership = new();

    /// <summary>
    /// The scope the push re-files through (uac-r2 #2091, merged 2026-10-18): a lookup move of an invoice or line is a re-file,
    /// which needs the ownership resolver and the reconciler, or the child is refused before any write.
    /// </summary>
    private IServiceScopeFactory PushScopes() =>
        new ServiceCollection()
            .AddSingleton<IRecordOwnershipResolver>(_pushOwnership)
            .AddScoped(_ => SecureChildReconcilerFixtures.OverEmptyWorld())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static IReadOnlyDictionary<string, EntityReference?> Lookups(params (string Column, EntityReference? Record)[] values) =>
        values.ToDictionary(v => v.Column, v => v.Record);

    private static readonly IReadOnlyDictionary<string, EntityReference?> NoLookups = new Dictionary<string, EntityReference?>();

    private static readonly IReadOnlyDictionary<string, object?> RenameOnly =
        new Dictionary<string, object?> { ["sprk_name"] = "Renamed" };

    private static readonly IReadOnlyDictionary<string, object?> NoPlain = new Dictionary<string, object?>();

    private static EntityReference MatterRef(Guid id) => new("sprk_matter", id);

    private static void NoGuid(PathOutcome outcome) =>
        AnyGuid.IsMatch(outcome.Message ?? string.Empty).Should().BeFalse($"no id read as the app may reach the caller: \"{outcome.Message}\"");

    // ── #2081: an upsert through a generic path can never create an orphan invoice-family row ─────────────────────────────

    public static TheoryData<GenericPath, string, bool, bool> OrphanCases()
    {
        var data = new TheoryData<GenericPath, string, bool, bool>();
        foreach (var path in Enum.GetValues<GenericPath>())
            foreach (var table in new[] { "sprk_invoice", "sprk_billingevent" })
                foreach (var answersNull in new[] { true, false })   // the reader answers null / throws object-does-not-exist
                    foreach (var touchesLookup in new[] { true, false })
                        data.Add(path, table, answersNull, touchesLookup);
        return data;
    }

    [Theory]
    [MemberData(nameof(OrphanCases))]
    public async Task AnUpsertToARowThatDoesNotExist_IsRefusedRecordNotFound_AndNothingIsWritten(
        GenericPath path, string table, bool answersNull, bool touchesLookup)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var read = app.Setup(a => a.RetrieveAsync(table, MissingRow, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (answersNull) read.ReturnsAsync((Entity)null!); else read.ThrowsAsync(ObjectDoesNotExist());

        // The write that used to CREATE the row: a rename (the old guard let it through unread), or a lookup it would have checked.
        var outcome = await RunAsync(path, table, MissingRow,
            touchesLookup ? Lookups(("sprk_matter", MatterRef(Matter))) : NoLookups, touchesLookup ? NoPlain : RenameOnly, app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        outcome.Message.Should().Contain(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_invoice")]
    [InlineData(GenericPath.PlaybookUpdate, "sprk_billingevent")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_invoice")]
    [InlineData(GenericPath.OutputOrchestrator, "sprk_billingevent")]
    [InlineData(GenericPath.FieldMappingPush, "sprk_invoice")]
    [InlineData(GenericPath.FieldMappingPush, "sprk_billingevent")]
    public async Task AnUpsertWhoseStoredRowCannotBeRead_IsRefusedReadFailed_AndNothingIsWritten(GenericPath path, string table)
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveAsync(table, Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("socket 10.0.0.7 timed out"));

        var outcome = await RunAsync(path, table, Invoice, NoLookups, RenameOnly, app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "a fault is never \"the row exists\"");
        outcome.Message.Should().NotContain("10.0.0.7");
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task AnExistingInvoice_FollowsTheExistingRules_ARenameIsWritten_AndASecondCoreRecordIsRefusedCoreRecordFixed(GenericPath path)
    {
        var renamed = await RunAsync(path, "sprk_invoice", Invoice, NoLookups, RenameOnly, InvoiceUnderAMatter().Object);
        renamed.Code.Should().BeNull();
        WrittenOnce("sprk_invoice", Invoice);

        _writes.Invocations.Clear();
        var second = await RunAsync(path, "sprk_invoice", Invoice,
            Lookups(("sprk_project", new EntityReference("sprk_project", Project))), NoPlain, InvoiceUnderAMatter().Object);
        second.Code.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode);
        second.Message.Should().Contain(ReturnAndResubmitHint);
        NothingWritten();
    }

    /// <summary>Task 045g rule 1: an update that would leave an invoice under no core record changes its core record.</summary>
    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task AnInvoiceWhoseCoreLookupIsCleared_IsRefusedCoreRecordFixed_AndNothingIsWritten(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", null)), NoPlain, InvoiceUnderAMatter().Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode);
        outcome.Message.Should().Contain(ReturnAndResubmitHint);
        NothingWritten();
    }

    // ── Task 4: no IGenericEntityService is registered or available — fail closed with a clear code, not a null reference ──

    [Theory]
    [InlineData("sprk_invoice", true)]
    [InlineData("sprk_invoice", false)]
    [InlineData("sprk_billingevent", true)]
    [InlineData("sprk_billingevent", false)]
    public async Task UpdateRecordActionCore_WithNoEntityServiceRegistered_FailsClosedReadFailed_AndNothingIsWritten(
        string table, bool touchesLookup)
    {
        if (touchesLookup)
        {
            // Merged with uac-r2 #2091 round 4 (2026-10-18): a lookup's entity set is read from metadata before the guard runs,
            // so with no entity service the write fails closed THERE (still nothing written), before the guard's read.
            var act = () => RunAsync(GenericPath.PlaybookUpdate, table, Invoice, Lookups(("sprk_matter", MatterRef(Matter))), NoPlain, app: null);
            (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which.Message.Should().Contain("was not written");
            NothingWritten();
            return;
        }

        var outcome = await RunAsync(GenericPath.PlaybookUpdate, table, Invoice, NoLookups, RenameOnly, app: null);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        outcome.Message.Should().Be(
            $"The invoice-family core-record rule refused this write ({InvoiceFamilyCoreRecordGuard.ReadFailedCode}); nothing was written.");
        NothingWritten();
    }

    [Fact]
    public async Task UpdateRecordActionCore_WithNoEntityServiceRegistered_StillWritesATableOutsideTheInvoiceFamily()
    {
        var outcome = await RunAsync(GenericPath.PlaybookUpdate, "sprk_todo", Invoice, NoLookups, RenameOnly, app: null);

        outcome.Code.Should().BeNull("the guard covers sprk_invoice and sprk_billingevent only");
        WrittenOnce("sprk_todo", Invoice);
    }

    [Fact]
    public async Task FieldMappingPush_WithNoEntityService_FailsClosedReadFailed_ForAnInvoiceRename()
    {
        var outcome = await RunAsync(GenericPath.FieldMappingPush, "sprk_invoice", Invoice, NoLookups, RenameOnly, app: null);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        NothingWritten();
    }

    // ── An invoice's core record is fixed at create (task 045g rule 1), a line keeps its invoice (rule 3), and a line still
    // regards its invoice's core record (D-96's line side), through each generic path ───────────────────────────────────────

    private static readonly Guid StrayLine = Guid.Parse("02200000-0000-4000-8000-0000000000c1");

    private const string ReturnAndResubmitHint =
        "A wrongly filed invoice is not moved: it is returned to the firm and resubmitted on the right matter, project or work assignment.";

    /// <summary>
    /// The app-only world: the invoice and a line, both filed under <see cref="Matter"/>, and the lines a query would find
    /// (<paramref name="lineCores"/>: the core record each of the invoice's lines names), evaluated like Dataverse would.
    /// </summary>
    private static Mock<IGenericEntityService> WorldWithLines(params Guid[] lineCores)
    {
        var lines = lineCores.Select(core => new Entity("sprk_billingevent", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", Invoice),
            ["sprk_matter"] = MatterRef(core),
        }).ToList();
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = MatterRef(Matter) });
        app.Setup(a => a.RetrieveAsync("sprk_billingevent", Line, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_billingevent", Line)
            {
                ["sprk_matter"] = MatterRef(Matter),
                ["sprk_invoice"] = new EntityReference("sprk_invoice", Invoice),
            });
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                new EntityCollection(Sprk.Bff.Api.Tests.Services.Dataverse.QueryEvaluator.Run(q, lines).ToList()));
        return app;
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, false)]
    [InlineData(GenericPath.PlaybookUpdate, true)]
    [InlineData(GenericPath.OutputOrchestrator, false)]
    [InlineData(GenericPath.OutputOrchestrator, true)]
    [InlineData(GenericPath.FieldMappingPush, false)]
    [InlineData(GenericPath.FieldMappingPush, true)]
    public async Task AnInvoiceMovedToAnotherMatter_IsRefusedCoreRecordFixed_WithTheHint_WhereverItsLinesAre(GenericPath path, bool linesFollow)
    {
        // Task 045g rule 1: decided on the stored row before any other read, so the lines are never counted.
        var app = linesFollow ? WorldWithLines(OtherMatter) : WorldWithLines(OtherMatter, Matter);

        var outcome = await RunAsync(path, "sprk_invoice", Invoice, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain, app.Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.InvoiceCoreRecordFixedCode);
        outcome.Message.Should().Contain(ReturnAndResubmitHint);
        NoGuid(outcome);
        NothingWritten();
        app.Verify(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never,
            "the lines and the catalog are never read for a refused move");
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    [InlineData(GenericPath.FieldMappingPush)]
    public async Task D96_ALineMovedToAnotherMatter_WhileStillAttachedToItsInvoice_IsRefusedLineMismatch_WithTheHint(GenericPath path)
    {
        var outcome = await RunAsync(path, "sprk_billingevent", Line, Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain,
            WorldWithLines().Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        outcome.Message.Should().Contain(ReturnAndResubmitHint);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate, false)]
    [InlineData(GenericPath.PlaybookUpdate, true)]
    [InlineData(GenericPath.OutputOrchestrator, false)]
    [InlineData(GenericPath.OutputOrchestrator, true)]
    public async Task ALineWhoseInvoiceIsCleared_IsRefusedLineInvoiceCleared_AndNothingIsWritten(GenericPath path, bool movedToo)
    {
        // Task 045g rule 3: a line keeps an invoice once it has one, whether or not the same write moves it.
        var lookups = movedToo
            ? Lookups(("sprk_invoice", null), ("sprk_matter", MatterRef(OtherMatter)))
            : Lookups(("sprk_invoice", null));

        var outcome = await RunAsync(path, "sprk_billingevent", Line, lookups, NoPlain, WorldWithLines().Object);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceClearedCode);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory(DisplayName = "164a-1 (must not fire): a line's update naming its own invoice on the same matter is written; only the line's generic CREATE is refused")]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task D96_ALineReattachedToItsInvoice_OnTheSameMatter_IsWritten_TheCreateOnlyRefusalDoesNotFire(GenericPath path)
    {
        // ebilling-r1 task 164a-1 (uac-r2 Q6): the line's generic create is refused (GenericCreateRefused), its update is not.
        var rule = Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.Get("sprk_billingevent");
        rule.GenericCreateRefused.Should().BeTrue();
        Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.RefuseWriterOnlyTableUpdate("sprk_billingevent").Should().BeNull();

        // The line, on its invoice's matter, names its invoice again.
        var outcome = await RunAsync(path, "sprk_billingevent", Line,
            Lookups(("sprk_invoice", new EntityReference("sprk_invoice", Invoice)), ("sprk_matter", MatterRef(Matter))), NoPlain,
            WorldWithLines().Object);

        outcome.Code.Should().BeNull(outcome.Message);
        WrittenOnce("sprk_billingevent", Line);
    }

    [Fact]
    public async Task TheCoreRecordFixedRefusalOnTheThrowingPaths_PinsTheExactMessage()
    {
        var outcome = await RunAsync(GenericPath.OutputOrchestrator, "sprk_invoice", Invoice,
            Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain, WorldWithLines(Matter).Object);

        outcome.Message.Should().Be(
            "The invoice-family core-record rule refused this write (invoice_family_invoice_core_record_fixed); nothing was written. "
            + ReturnAndResubmitHint);
    }

    [Fact]
    public async Task TheMismatchRefusalOnTheThrowingPaths_PinsTheExactMessage()
    {
        var outcome = await RunAsync(GenericPath.OutputOrchestrator, "sprk_billingevent", Line,
            Lookups(("sprk_matter", MatterRef(OtherMatter))), NoPlain, WorldWithLines().Object);

        outcome.Message.Should().Be(
            "The invoice-family core-record rule refused this write (invoice_family_line_invoice_core_record_mismatch); nothing was written. "
            + ReturnAndResubmitHint);
    }
}
