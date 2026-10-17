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
public class InvoiceFamilyGenericUpdatePathTests
{
    private static readonly Guid Invoice = Guid.Parse("02200000-0000-4000-8000-0000000000a1");
    private static readonly Guid Matter = Guid.Parse("02200000-0000-4000-8000-0000000000a2");
    private static readonly Guid Project = Guid.Parse("02200000-0000-4000-8000-0000000000a3");
    private static readonly Guid Event = Guid.Parse("02200000-0000-4000-8000-0000000000a4");
    private static readonly Guid Caller = Guid.Parse("02200000-0000-4000-8000-0000000000aa");

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

    private void NothingWritten() =>
        _writes.Verify(w => w.UpdateRecordFieldsAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never);

    private void WrittenOnce(string table, Guid id) =>
        _writes.Verify(w => w.UpdateRecordFieldsAsync(table, id,
            It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Once);

    // ── The output orchestrator: DataverseUpdateHandler ────────────────────────────────────────────────────────────

    private DataverseUpdateHandler Handler(IGenericEntityService app) =>
        new(_writes.Object, app, new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance);

    [Fact]
    public async Task DataverseUpdateHandler_AnInvoiceGivenASecondCoreRecord_IsRefusedWithTheGuardsCode_AndNothingIsWritten()
    {
        var act = () => Handler(InvoiceUnderAMatter().Object).UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["sprk_project"] = new EntityReference("sprk_project", Project) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        NothingWritten();
    }

    [Fact]
    public async Task DataverseUpdateHandler_AnInvoiceUpdateTouchingNoCoreLookup_IsWritten_AndReadsNothingForTheGuard()
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict); // any read would throw

        await Handler(app.Object).UpdateAsync(
            "sprk_invoice", Invoice, new Dictionary<string, object?> { ["sprk_name"] = "Renamed" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        WrittenOnce("sprk_invoice", Invoice);
    }

    // ── The playbook update: UpdateRecordActionCore ────────────────────────────────────────────────────────────────

    private UpdateRecordActionCore Core(IGenericEntityService app) =>
        new(_writes.Object,
            new ServiceCollection()
                .AddSingleton(app)
                .AddSingleton(new StampWorld().Restamper)
                .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance);

    [Fact]
    public async Task UpdateRecordActionCore_AnInvoiceGivenASecondCoreRecord_IsRefusedWithTheGuardsCode_AndNothingIsWritten()
    {
        var act = () => Core(InvoiceUnderAMatter().Object).UpdateAsync(
            new UpdateRecordActionInput("sprk_invoice", Invoice, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_project", "sprk_project", Project.ToString())]),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        NothingWritten();
    }

    [Fact]
    public async Task UpdateRecordActionCore_AnInvoiceUpdateTouchingNoCoreLookup_IsWritten_AndReadsNothingForTheGuard()
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);

        await Core(app.Object).UpdateAsync(
            new UpdateRecordActionInput("sprk_invoice", Invoice, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "Renamed" }, Lookups: null),
            CancellationToken.None);

        WrittenOnce("sprk_invoice", Invoice);
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
        errors.Should().ContainSingle().Which.Error.Should()
            .Contain(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode)
            .And.NotContain(Matter.ToString("D"), "the reason can name records the caller has not read: code only");
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
