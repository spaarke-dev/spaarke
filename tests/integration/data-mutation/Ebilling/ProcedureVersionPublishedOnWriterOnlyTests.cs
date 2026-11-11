using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 DEF-061 (owner-approved schema change, 2026-11-09): <c>sprk_procedureversion.sprk_publishedon</c>
/// (<see cref="ProcedureEntity.VersionPublishedOnColumn"/>) is written only by <c>ProcedureService.PublishVersionAsync</c>. It is a writer-only
/// column of the table (<see cref="RecordTableRule.WriterOnlyColumns"/>, the AP export claim's mechanism, 165d) and mapping-protected: every
/// generic writer that asks <c>InheritedAccessPermission.RefuseAppOnlyWrite</c> is refused a payload naming it, for EVERY <c>WriteIdentity</c>.
/// Must fire: the Update Record node, a playbook's output mapping and the field-mapping push, in the plain, case-variant, <c>@odata.bind</c> and
/// <c>_{column}_value</c> forms. Must not fire: the same writers onto an ordinary column of the table, the same name on other tables, a neighbouring
/// name. Registering the table does not open it to a generic create, and Repair still does not touch it.
/// </summary>
public class ProcedureVersionPublishedOnWriterOnlyTests
{
    private const string Version = "sprk_procedureversion";
    private const string Stamp = ProcedureEntity.VersionPublishedOnColumn;
    private static readonly string Code = RecordTableRules.ChangedOnlyByItsWriterColumnReasonCode;
    private static readonly string MarkerFamily = RecordTableRules.ChangedOnlyByItsWriterReasonCode;

    private static readonly Guid Row = Guid.Parse("def06100-0000-0000-0000-00000000e401");
    private static readonly Guid Caller = Guid.Parse("def06100-0000-0000-0000-00000000e4ca");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>The column in every form a writer sends it.</summary>
    public static TheoryData<string> KeyForms() => new()
    {
        Stamp,
        $" {Stamp.ToUpperInvariant()} ",
        "sprk_PublishedOn",
        $"{Stamp}@odata.bind",
        "sprk_PublishedOn@odata.bind",
        $"_{Stamp}_value",
    };

    private static string? CodeOf(string? table, string key, InheritedAccessPermission.WriteIdentity identity) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, [key], identity)?.RefusalCode;

    // ── The refusal itself ─────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "DEF-061: the publish stamp is refused for the app-only AND the caller identity, in every key form, with the marker family and the column named")]
    [MemberData(nameof(KeyForms))]
    public void TheStamp_IsRefused_ForEveryIdentity(string key)
    {
        foreach (var identity in new[] { InheritedAccessPermission.WriteIdentity.AppOnly, InheritedAccessPermission.WriteIdentity.Caller })
        {
            var refused = InheritedAccessPermission.RefuseAppOnlyWrite(Version, [key], identity);

            refused.Should().NotBeNull($"{key} as {identity}");
            refused!.RefusalCode.Should().Be(Code, $"{key} as {identity}");
            refused.RefusalCode.Should().StartWith(MarkerFamily);
            refused.Reason.Should().StartWith($"{Stamp} of a {Version} ");
        }

        CodeOf($"  {Version.ToUpperInvariant()}\t", key, InheritedAccessPermission.WriteIdentity.Caller).Should().Be(Code);
    }

    [Fact(DisplayName = "DEF-061 must-not-fire: an ordinary column of the table, a neighbouring name, the same name on another table and a null table are not refused")]
    public void OtherWrites_AreNotRefused()
    {
        foreach (var identity in new[] { InheritedAccessPermission.WriteIdentity.AppOnly, InheritedAccessPermission.WriteIdentity.Caller })
        {
            CodeOf(Version, "sprk_name", identity).Should().BeNull("an ordinary column");
            CodeOf(Version, ProcedureEntity.VersionEffectiveFromColumn, identity).Should().BeNull("the dates are not this refusal's (081 owns them)");
            CodeOf(Version, Stamp + "s", identity).Should().BeNull("a neighbouring name");
            CodeOf(Version, "x" + Stamp, identity).Should().BeNull("a neighbouring name");
            CodeOf("sprk_procedure", Stamp, identity).Should().BeNull("only the version carries the stamp");
            CodeOf("sprk_policyversion", Stamp, identity).Should().BeNull();
            CodeOf("sprk_invoice", Stamp, identity).Should().BeNull();
        }

        RecordTableRules.RefuseWriterOnlyColumns(Version, ["sprk_name"]).Should().BeNull();
        RecordTableRules.RefuseWriterOnlyColumns(null, [Stamp]).Should().BeNull();
        RecordTableRules.RefuseWriterOnlyColumns("sprk_unregisteredfixture", [Stamp]).Should().BeNull();
    }

    [Fact(DisplayName = "DEF-061: the registry declares the stamp writer-only and mapping-protected; the table stays generic-create-refused and Repair-skipped, and the AP export claim is untouched")]
    public void TheRegistry_DeclaresTheStamp_AndOpensNothingElse()
    {
        var rule = RecordTableRules.Get(Version);

        rule.WriterOnlyColumns.Should().BeEquivalentTo([Stamp]);
        rule.MappingProtectedColumns.Should().BeEquivalentTo([Stamp]);
        rule.GenericCreateRefused.Should().BeTrue("a Procedure Version is created by ProcedureService alone; registering the stamp must not open a generic create");
        rule.CreatedOnlyByItsWriter.Should().BeFalse("its other columns are as they were: only the stamp is refused");
        DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter(Version).Should().BeTrue("the chat create tool refuses it too");
        DataverseCreateRecordHandler.CreatedOnlyByItsWriterCreateBlockedMessage(Version)
            .Should().Contain("Procedure versions are created and published only from the Procedure's versions page")
            .And.NotContain("signal").And.NotContain("decision");
        rule.TableServerOwnedColumns.Should().BeEmpty();
        RecordTableRules.All.Where(r => r.WriterOnlyColumns.Count > 0).Select(r => r.Table).Should().BeEquivalentTo(["sprk_invoice", Version]);
        RecordTableRules.Get("sprk_invoice").WriterOnlyColumns.Should().BeEquivalentTo([ApExportService.BatchLookup], "the AP claim is still the invoice's only");
    }

    // ── The Update Record node (UpdateRecordActionCore, app-only) ──────────────────────────────────────────────

    [Theory(DisplayName = "DEF-061: the Update Record node is refused a write of the stamp (set, cleared and as a lookup), and nothing is written")]
    [MemberData(nameof(KeyForms))]
    public async Task TheUpdateRecordNode_IsRefused(string key)
    {
        var set = () => Core().UpdateAsync(
            new UpdateRecordActionInput(Version, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [key] = "2027-03-01T00:00:00Z" }, Lookups: null),
            CancellationToken.None);
        var refusal = (await set.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which;
        refusal.RefusalCode.Should().Be(Code, key);
        refusal.Message.Should().StartWith("The update was NOT written: ").And.Contain(Stamp);

        var cleared = () => Core().UpdateAsync(
            new UpdateRecordActionInput(Version, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [key] = "" }, Lookups: null),
            CancellationToken.None);
        (await cleared.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(Code, key);

        _writes.Invocations.Should().BeEmpty(key);
    }

    [Fact(DisplayName = "DEF-061 must-not-fire: the Update Record node still writes an ordinary column of a Procedure Version")]
    public async Task TheUpdateRecordNode_AnOrdinaryColumn_IsWritten()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(Version, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "renamed" }, Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Version, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_name")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── A playbook's output mapping (DataverseUpdateHandler, app-only) ─────────────────────────────────────────

    [Theory(DisplayName = "DEF-061: a playbook's output mapping is refused a write of the stamp (set and cleared), and nothing is written")]
    [MemberData(nameof(KeyForms))]
    public async Task TheOutputMapping_IsRefused(string key)
    {
        foreach (var value in new object?[] { null, new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), "2027-03-01T00:00:00Z" })
        {
            _writes.Invocations.Clear();
            var mapping = () => Handler().UpdateAsync(
                Version, Row, new Dictionary<string, object?> { [key] = value }, ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

            (await mapping.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(Code, key);
            _writes.Invocations.Should().BeEmpty(key);
        }
    }

    [Fact(DisplayName = "DEF-061 must-not-fire: a playbook's output mapping still writes an ordinary column of a Procedure Version")]
    public async Task TheOutputMapping_StillWritesOtherColumns()
    {
        await Handler().UpdateAsync(
            Version, Row, new Dictionary<string, object?> { ["sprk_name"] = "renamed" }, ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Version, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_name")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The field-mapping push (runs as its caller: WriteIdentity.Caller) ──────────────────────────────────────

    [Theory(DisplayName = "DEF-061: a field-mapping push rule aimed at the stamp is skipped (the caller identity is refused too), the rest of the push applies, and the stamp is never sent")]
    [MemberData(nameof(KeyForms))]
    public async Task TheFieldMappingPush_SkipsTheRule(string key)
    {
        var (updated, failed, skipped, errors, results) = await Push(Rule("sprk_name", "sprk_name"), Rule("sprk_name", key));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        results.Should().ContainSingle(r => r.Status == FieldMappingStatus.Skipped)
            .Which.ErrorMessage.Should().Contain(Stamp).And.Contain(MarkerFamily);
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Version, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_name")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "DEF-061 must-not-fire: a push onto only ordinary columns of a Procedure Version is sent as the caller with every column")]
    public async Task TheFieldMappingPush_StillSendsOtherColumns()
    {
        var (updated, failed, skipped, errors, results) = await Push(Rule("sprk_name", "sprk_name"), Rule("sprk_name", "sprk_versionnumber"));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        results.Should().NotContain(r => r.Status == FieldMappingStatus.Skipped);
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            Version, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 2 && f.ContainsKey("sprk_name") && f.ContainsKey("sprk_versionnumber")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Repair (RecordCreationService.RepairAsync, app-only) ───────────────────────────────────────────────────

    [Fact(DisplayName = "DEF-061: Repair never reads, maps into or re-owns a Procedure Version, though the table is now in the registry")]
    public async Task Repair_DoesNotTouchAProcedureVersion()
    {
        var world = new RepairWorld();
        world.MatterLinksTo();
        world.Map("sprk_matter", Version, RepairWorld.DefaultText(Stamp, "2027-03-01T00:00:00Z", 1));
        var id = world.RowOf(Version, OwnershipDirectory.ChildBu, new Dictionary<string, object>
        {
            ["sprk_name"] = "Version 1",
        });

        var outcome = await world.RepairAsync(Version, id);

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.TableUnsupported, "the neutral reason it had before it was registered; it is not append-only");
        outcome.ReasonCode.Should().NotBe(RecordRepairReason.TableAppendOnly);
        world.UpdateCalls.Should().Be(0);
        world.Directory.RecordQueries.Should().BeEmpty("the row is not even read");
        world.Directory.Row(Version, id).Attributes.Should().NotContainKey(Stamp);
    }

    // ── Harness (as BillingExportClaimWriterOnlyTests) ─────────────────────────────────────────────────────────

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Version 1" },
            Version,
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None,
            appOnlyEntities: TheVersionRow());

    /// <summary>The app-only reader: the stored version row, with no parents.</summary>
    private static IGenericEntityService TheVersionRow()
    {
        var app = new Mock<IGenericEntityService>();
        app.Setup(a => a.RetrieveAsync(Version, Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(Version, Row));
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        app.Setup(a => a.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, CancellationToken _) => EntitySetNameFixtures.SetOf(table));
        return app.Object;
    }

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection()
            .AddSingleton(TheVersionRow())
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private DataverseUpdateHandler Handler() =>
        new(_writes.Object, TheVersionRow(), new StampWorld().Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
}
