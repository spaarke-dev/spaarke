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
/// ebilling-r1 DEF-022 (#2056; task 026 fix-round-2): the three generic update paths that do not write through
/// <c>OwnedChildWrite</c> — the playbook update (<see cref="UpdateRecordActionCore"/>), the output orchestrator
/// (<see cref="DataverseUpdateHandler"/>) and the field-mapping push — call <see cref="InvoiceFamilyCoreRecordGuard"/> before
/// their write when it targets <c>sprk_invoice</c> / <c>sprk_billingevent</c> and touches a guarded lookup, and refuse with
/// its stable code. A write that touches no guarded lookup, or another table, is unaffected and reads nothing for it.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private static readonly Guid Invoice = Guid.Parse("02200000-0000-4000-8000-0000000000a1");
    private static readonly Guid Matter = Guid.Parse("02200000-0000-4000-8000-0000000000a2");
    private static readonly Guid Project = Guid.Parse("02200000-0000-4000-8000-0000000000a3");
    private static readonly Guid Event = Guid.Parse("02200000-0000-4000-8000-0000000000a4");
    private static readonly Guid Caller = Guid.Parse("02200000-0000-4000-8000-0000000000aa");
    private static readonly Guid OtherInvoice = Guid.Parse("02200000-0000-4000-8000-0000000000a5");
    private static readonly Guid OtherMatter = Guid.Parse("02200000-0000-4000-8000-0000000000a6");
    private static readonly Guid Line = Guid.Parse("02200000-0000-4000-8000-0000000000a7");

    private static readonly Regex AnyGuid = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    /// <summary>
    /// Task 026 fix-round-2: the app-only world of the two mismatch cases — the invoice and a line under the matter, and
    /// another invoice under ANOTHER matter (whose id only the app has read).
    /// </summary>
    private static Mock<IGenericEntityService> MismatchWorld()
    {
        var app = InvoiceUnderAMatter();
        app.Setup(a => a.RetrieveAsync("sprk_invoice", OtherInvoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", OtherInvoice) { ["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter) });
        app.Setup(a => a.RetrieveAsync("sprk_billingevent", Line, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_billingevent", Line) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) });
        return app;
    }

    /// <summary>(table, record, patch, expected code) for the superseded-invoice and line-invoice mismatch cases.</summary>
    public static TheoryData<string, string> MismatchCases() => new()
    {
        { "sprk_invoice", InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode },
        { "sprk_billingevent", InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode },
    };

    private static (Guid Id, Dictionary<string, object?> Patch) MismatchWrite(string table) => table == "sprk_invoice"
        ? (Invoice, new Dictionary<string, object?> { ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({OtherInvoice:D})" })
        : (Line, new Dictionary<string, object?> { ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({OtherInvoice:D})" });

    /// <summary>The refusal names no record (no GUID at all), carries the code, and is not worded as an owner failure.</summary>
    private static void CodeOnly(RecordOwnerUnresolvedException refused, string code)
    {
        refused.RefusalCode.Should().Be(code);
        AnyGuid.IsMatch(refused.Message).Should().BeFalse($"no id read as the app may reach the caller: \"{refused.Message}\"");
        AnyGuid.IsMatch(refused.Reason ?? string.Empty).Should().BeFalse();
        refused.Message.Should().Contain(code).And.NotStartWith("No owner could be resolved");
    }

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>
    /// Task 045d: the reads the app-only resolver-field derive makes, answered for every world (registered when a path runs,
    /// after the world's own setups; each matches only the derive's own query): the core table's active
    /// <c>sprk_recordtype_ref</c> row (<see cref="CatalogRowOf"/>, naming <c>{table}number</c>), and a core record read for its
    /// name and number (<c>Record {id}</c>, <c>N-{id}</c>). A test scripts another answer with <see cref="_catalogAnswer"/> or
    /// <see cref="_recordAnswer"/>; <see cref="_deriveReads"/> journals each derive read.
    /// </summary>
    private void AnswerDeriveReads(IGenericEntityService app)
    {
        if (app is not IMocked<IGenericEntityService> mocked)
            return;
        var mock = mocked.Mock;
        mock.Setup(a => a.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_recordtype_ref"), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                var table = (string)q.Criteria.Conditions.Single(c => c.AttributeName == "sprk_recordlogicalname").Values[0];
                _deriveReads.Add("catalog:" + table);
                return Task.FromResult(new EntityCollection(_catalogAnswer(table) is { } row ? [row] : []));
            });
        mock.Setup(a => a.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.Is<string[]>(c => c.Length == 2 && c[1].EndsWith("number", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                _deriveReads.Add($"record:{table}");
                return Task.FromResult(_recordAnswer(table, id, columns)!);
            });
    }

    private readonly List<string> _deriveReads = [];

    private Func<string, Entity?> _catalogAnswer = table =>
        new Entity("sprk_recordtype_ref", CatalogRowOf(table)) { [RegardingRecordType.RecordNumberFieldColumn] = table + "number" };

    private Func<string, Guid, string[], Entity?> _recordAnswer = (table, id, columns) => new Entity(table, id)
    {
        [columns[0]] = $"Record {id.ToString("N")[..6]}",
        [columns[1]] = $"N-{id.ToString("N")[..6]}",
    };

    /// <summary>
    /// Task 045d fix rounds 3-4: the person's query — the push's caller, or the person a playbook run acts as on the two
    /// handlers (their display read of the record the row will name). It answers the selected columns of
    /// <see cref="_recordAnswer"/> (journalled as that record's read), no row when that answers null, 403 when
    /// <see cref="_callerMayNotRead"/>, or <see cref="_callerFault"/>. Null when <see cref="_noCallerQuery"/>.
    /// </summary>
    private Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery? CallerQuery()
    {
        if (_noCallerQuery)
            return null;
        var query = new Mock<Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery>(MockBehavior.Strict);
        query.Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string? odata, Guid _, CancellationToken _) =>
            {
                var select = Regex.Match(odata!, @"\$select=([^&]+)").Groups[1].Value.Split(',');
                var filter = Regex.Match(odata!, @"\$filter=(\w+)id eq ([0-9a-fA-F-]{36})");
                var table = filter.Groups[1].Value;
                if (_personRows is not null && select.All(c => c.StartsWith('_') && c.EndsWith("_value", StringComparison.Ordinal)))
                    return Task.FromResult(PersonRowAnswer(table, Guid.Parse(filter.Groups[2].Value), select));
                _deriveReads.Add($"record:{table}");
                if (_callerFault is { } fault)
                    throw fault;
                if (_callerMayNotRead)
                    throw new HttpRequestException("denied", null, System.Net.HttpStatusCode.Forbidden);
                IReadOnlyList<Dictionary<string, System.Text.Json.JsonElement>> rows =
                    (_personAnswer ?? _recordAnswer)(table, Guid.Parse(filter.Groups[2].Value), select) is { } row
                        ? [select.ToDictionary(c => c, c => System.Text.Json.JsonSerializer.SerializeToElement(row.GetAttributeValue<string>(c)))]
                        : [];
                return Task.FromResult(rows);
            });
        return query.Object;
    }

    private bool _callerMayNotRead;

    private Exception? _callerFault;

    private bool _noCallerQuery;

    /// <summary>What the person's query answers, when it differs from the app's read (<see cref="_recordAnswer"/>).</summary>
    private Func<string, Guid, string[], Entity?>? _personAnswer;

    /// <summary>
    /// Task 045g: a row's lookups as the person reads them (the guard's re-decision on a person's run): the row, or null when
    /// the person cannot read it. A throw is the read's fault. Unset, a lookup read is answered as any other person read.
    /// </summary>
    private Func<string, Guid, Entity?>? _personRows;

    /// <summary>The person's lookup read: each selected <c>_{column}_value</c> as the row's reference id (null when unset).</summary>
    private IReadOnlyList<Dictionary<string, System.Text.Json.JsonElement>> PersonRowAnswer(string table, Guid id, string[] select)
    {
        _personRowReads.Add($"{table}:{id:D}");
        if (_personRows!(table, id) is not { } row)
            return [];
        return
        [
            select.ToDictionary(c => c, c =>
            {
                var column = c[1..^"_value".Length];
                return System.Text.Json.JsonSerializer.SerializeToElement(
                    row.Attributes.TryGetValue(column, out var value) && value is EntityReference reference ? reference.Id.ToString("D") : null);
            }),
        ];
    }

    /// <summary>Each row the person's lookup read asked for (<c>table:id</c>).</summary>
    private readonly List<string> _personRowReads = [];

    /// <summary>Task 045d: the harness catalog's fixed row id for a core table.</summary>
    private static Guid CatalogRowOf(string table) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("catalog:" + table)));

    /// <summary>The app-only reader: a stored invoice filed under a matter (with no lines).</summary>
    private static Mock<IGenericEntityService> InvoiceUnderAMatter()
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) });
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        return app;
    }

    private IEnumerable<(string Method, string Table, Guid Id, Guid? As)> Sent() =>
        _writes.Invocations
            .Where(i => i.Method.Name is nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync)
                or nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync))
            .Select(i => (i.Method.Name, (string)i.Arguments[0], (Guid)i.Arguments[1],
                i.Method.Name == nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync)
                    ? (Guid?)i.Arguments[4]
                    : (Guid?)i.Arguments[3]));

    /// <summary>Neither the upsert nor the update-only write was sent: nothing was created or changed.</summary>
    private void NothingWritten() => Sent().Should().BeEmpty();

    /// <summary>
    /// One write of the row, UPDATE-ONLY (If-Match: *), never the upsert. The guard's update-only branch (a family table,
    /// <c>InvoiceFamilyCoreRecordGuard.UpdateExistingAsync</c>) and the generic one send the same call; they differ in how a row
    /// gone by then is answered, which <c>AFamilyRowDeletedAfterTheGuard_IsRefusedByTheUpdateOnlyWrite_WithTheGuardsCode</c> pins.
    /// </summary>
    private void WrittenOnce(string table, Guid id)
    {
        var sent = Sent().Where(w => w.Table == table && w.Id == id).ToList();
        sent.Should().ContainSingle();
        sent[0].Method.Should().Be(nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync));
        Sent().Should().ContainSingle("only that one write");
    }

    // ── The output orchestrator: DataverseUpdateHandler ────────────────────────────────────────────────────────────

    private DataverseUpdateHandler Handler(IGenericEntityService app)
    {
        AnswerDeriveReads(app);
        // Fix round 5: a run as a person reads the derived name and number with the person's query, which names the record's
        // entity set from metadata through this same service.
        if (app is IMocked<IGenericEntityService> mocked)
        {
            mocked.Mock.Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string t, CancellationToken _) => EntitySetNameFixtures.SetOf(t));
        }
        return new(_writes.Object, app, new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld(), readAsPerson: CallerQuery());
    }

    [Fact]
    public async Task DataverseUpdateHandler_AnInvoiceGivenASecondCoreRecord_IsRefusedWithTheGuardsCode_AndNothingIsWritten()
    {
        var act = () => Handler(InvoiceUnderAMatter().Object).UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["sprk_project"] = new EntityReference("sprk_project", Project) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        CodeOnly((await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which,
            InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(MismatchCases))]
    public async Task DataverseUpdateHandler_ASupersededOrLineInvoiceMismatch_IsRefusedCodeOnly_NamingNoRecord(string table, string code)
    {
        var (id, patch) = MismatchWrite(table);

        var act = () => Handler(MismatchWorld().Object).UpdateAsync(table, id, patch, ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        CodeOnly((await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which, code);
        NothingWritten();
    }

    [Fact]
    public async Task DataverseUpdateHandler_AnInvoiceUpdateTouchingNoCoreLookup_IsWritten_AfterTheGuardConfirmsTheRowExists()
    {
        var app = InvoiceUnderAMatter(); // strict: the one stored-row read (#2081); a line query would throw

        await Handler(app.Object).UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["sprk_name"] = "Renamed" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        WrittenOnce("sprk_invoice", Invoice);
        app.Verify(a => a.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
        app.Verify(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The playbook update: UpdateRecordActionCore ────────────────────────────────────────────────────────────────

    private UpdateRecordActionCore Core(IGenericEntityService app)
    {
        // uac-r2 #2091 round 4 (master, merged 2026-10-18): the core reads a lookup's entity set from metadata through this
        // same service.
        Mock.Get(app)
            .Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, CancellationToken _) => EntitySetNameFixtures.SetOf(table));
        AnswerDeriveReads(app);
        return new(_writes.Object,
            new ServiceCollection()
                .AddSingleton(app)
                .AddSingleton(new StampWorld().Restamper)
                .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance);
    }

    [Fact]
    public async Task UpdateRecordActionCore_AnInvoiceGivenASecondCoreRecord_IsRefusedWithTheGuardsCode_AndNothingIsWritten()
    {
        var act = () => Core(InvoiceUnderAMatter().Object).UpdateAsync(
            new UpdateRecordActionInput("sprk_invoice", Invoice, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_project", "sprk_project", Project.ToString())]),
            CancellationToken.None);

        CodeOnly((await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which,
            InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(MismatchCases))]
    public async Task UpdateRecordActionCore_ASupersededOrLineInvoiceMismatch_IsRefusedCodeOnly_NamingNoRecord(string table, string code)
    {
        var (id, _) = MismatchWrite(table);
        var lookup = table == "sprk_invoice"
            ? new RenderedLookup("sprk_supersedesinvoice", "sprk_invoice", OtherInvoice.ToString())
            : new RenderedLookup("sprk_invoice", "sprk_invoice", OtherInvoice.ToString());

        var act = () => Core(MismatchWorld().Object).UpdateAsync(
            new UpdateRecordActionInput(table, id, FieldMappings: null, LegacyFields: null, Lookups: [lookup]), CancellationToken.None);

        CodeOnly((await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which, code);
        NothingWritten();
    }

    [Fact]
    public async Task UpdateRecordActionCore_AnInvoiceUpdateTouchingNoCoreLookup_IsWritten_AfterTheGuardConfirmsTheRowExists()
    {
        var app = InvoiceUnderAMatter();

        await Core(app.Object).UpdateAsync(
            new UpdateRecordActionInput("sprk_invoice", Invoice, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "Renamed" }, Lookups: null),
            CancellationToken.None);

        WrittenOnce("sprk_invoice", Invoice);
        app.Verify(a => a.RetrieveAsync("sprk_invoice", Invoice, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
        app.Verify(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The field-mapping push: ApplyMappingsToChildRecordsAsync ─────────────────────────────────────────────────

    private static readonly FieldMappingRuleDto ProjectRule = new()
    {
        SourceField = "sprk_project",
        TargetField = "sprk_project",
        SourceFieldType = "Lookup",
        TargetFieldType = "Lookup",
        Priority = 1,
    };

    [Fact]
    public async Task FieldMappingPush_AnInvoiceGivenASecondCoreRecord_IsRefusedByCodeOnly_AndNothingIsWritten()
    {
        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object, new StampWorld().Restamper, [ProjectRule],
            new Dictionary<string, object?> { ["sprk_project"] = Project },
            "sprk_invoice", [Invoice], Caller, NullLogger.Instance, CancellationToken.None,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan>
            {
                [ProjectRule] = new("sprk_Project@odata.bind", $"/sprk_projects({Project:D})", null),
            },
            appOnlyEntities: InvoiceUnderAMatter().Object);

        updated.Should().Be(0);
        failed.Should().Be(1);
        var error = errors.Should().ContainSingle().Subject.Error;
        error.Should().Contain(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        AnyGuid.IsMatch(error).Should().BeFalse("the reason can name records the caller has not read: code only");
        NothingWritten();
    }

    [Fact]
    public async Task FieldMappingPush_AnotherTablesCoreLookup_IsWritten_AndReadsNothingForTheGuard()
    {
        var world = new StampWorld().Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", Matter)]);
        var rule = new FieldMappingRuleDto
        {
            SourceField = "sprk_regardingmatter",
            TargetField = "sprk_regardingmatter",
            SourceFieldType = "Lookup",
            TargetFieldType = "Lookup",
            Priority = 1,
        };

        var (updated, failed, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object, world.Restamper, [rule], new Dictionary<string, object?> { ["sprk_regardingmatter"] = Matter },
            "sprk_event", [Event], Caller, NullLogger.Instance, CancellationToken.None,
            appOnlyEntities: new Mock<IGenericEntityService>(MockBehavior.Strict).Object);

        updated.Should().Be(1);
        failed.Should().Be(0);
        WrittenOnce("sprk_event", Event);
    }
}
