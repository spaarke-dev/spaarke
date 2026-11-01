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

    /// <summary>One write of the row. An invoice or line goes UPDATE-ONLY (If-Match: *, #2081); any other table keeps the upsert.</summary>
    private void WrittenOnce(string table, Guid id)
    {
        var sent = Sent().Where(w => w.Table == table && w.Id == id).ToList();
        sent.Should().ContainSingle();
        sent[0].Method.Should().Be(InvoiceFamilyCoreRecordGuard.IsUpdateOnlyTarget(table)
            ? nameof(IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync)
            : nameof(IFieldMappingDataverseService.UpdateRecordFieldsAsync));
        Sent().Should().ContainSingle("only that one write");
    }

    // ── The output orchestrator: DataverseUpdateHandler ────────────────────────────────────────────────────────────

    private DataverseUpdateHandler Handler(IGenericEntityService app) =>
        new(_writes.Object, app, new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());

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
        SourceField = "sprk_project", TargetField = "sprk_project", SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
    };

    [Fact]
    public async Task FieldMappingPush_AnInvoiceGivenASecondCoreRecord_IsRefusedByCodeOnly_AndNothingIsWritten()
    {
        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
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
            SourceField = "sprk_regardingmatter", TargetField = "sprk_regardingmatter",
            SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
        };

        var (updated, failed, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object, world.Restamper, [rule], new Dictionary<string, object?> { ["sprk_regardingmatter"] = Matter },
            "sprk_event", [Event], Caller, NullLogger.Instance, CancellationToken.None,
            appOnlyEntities: new Mock<IGenericEntityService>(MockBehavior.Strict).Object);

        updated.Should().Be(1);
        failed.Should().Be(0);
        WrittenOnce("sprk_event", Event);
    }
}
