using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// #2251 (uac-r2): the tables whose rows ARE access (<see cref="InheritedAccessPermission.AccessTables"/>: grants,
/// organisation membership, ethical walls, the Assigned-To ledger, team membership) and a secure root's secure flag, access
/// columns and container (<see cref="InheritedAccessPermission.SecureRootColumns"/>) are written only by the BFF's typed
/// writers. An app-only generic writer never writes them (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>, reasons
/// <see cref="InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode"/> and
/// <see cref="InheritedAccessPermission.AppOnlySecureRootWriteReasonCode"/>): the Update Record playbook node and the action
/// seam acting for no person (<see cref="UpdateRecordActionCore"/>), a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>), and the field-mapping push (a rule naming one is skipped). The BFF app user is a
/// System Administrator, so before this a playbook could point a grant at another contact, move a contact into an
/// organisation, repoint an ethical wall, un-secure a matter or point it at another root's container. Must fire: every
/// access table, in any case and with whitespace, with any column; every secure-root column on every root, in its plain,
/// case-variant, <c>@odata.bind</c> and <c>_{column}_value</c> forms. Must not fire: ordinary columns on a root (the live
/// <c>sprk_matter.sprk_performancesummary</c> node), the same columns on other tables, neighbouring table names. Drift: every
/// table constant the access readers query is refused, and every root the registry and the grant model name is covered.
/// </summary>
public class AccessTableAppOnlyWriterTests
{
    private static readonly Guid Row = Guid.Parse("22510000-0000-0000-0000-00000000e401");
    private static readonly Guid Caller = Guid.Parse("22510000-0000-0000-0000-00000000e4ca");
    private static readonly Guid Forged = Guid.Parse("22510000-0000-0000-0000-00000000e4ff");

    private static readonly string[] Tables =
        ["sprk_externalrecordaccess", "sprk_contactorganization", "sprk_noaccessentry", "sprk_assignedaccess", "teammembership"];

    private static readonly string[] Roots = ["sprk_matter", "sprk_project", "sprk_workassignment"];

    private static readonly string[] RootColumns =
        ["sprk_issecure", "sprk_accesspermission", "sprk_accessinheritance", "sprk_securitybu", "sprk_containerid"];

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>Each access table in each spelling, with a column of each kind a configuration names.</summary>
    public static TheoryData<string, string> EveryAccessTableForm()
    {
        var data = new TheoryData<string, string>();
        foreach (var table in Tables)
        {
            foreach (var spelling in new[] { table, table.ToUpperInvariant(), $"  {table}\t", $" {char.ToUpperInvariant(table[0])}{table[1..]} " })
            {
                data.Add(spelling, "sprk_name");
                data.Add(spelling, "statecode");
                data.Add(spelling, "sprk_Contact@odata.bind");
                data.Add(spelling, "_sprk_organization_value");
            }
        }

        return data;
    }

    /// <summary>Each secure-root column in each form a payload key can take, on each root, with the column it names.</summary>
    public static TheoryData<string, string, string> EverySecureRootForm()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var root in Roots)
        {
            foreach (var column in RootColumns)
            {
                data.Add(root, column, column);
                data.Add($" {root.ToUpperInvariant()} ", $" {column.ToUpperInvariant()} ", column);
                data.Add(root, $"{column}@odata.bind", column);
                data.Add(root, $"_{column}_value", column);
            }

            // The navigation-property spelling a configuration writes for the business-unit lookup.
            data.Add(root, "sprk_SecurityBU@odata.bind", "sprk_securitybu");
        }

        return data;
    }

    /// <summary>A case of each rule, on the tables the end-to-end writers are driven over.</summary>
    public static TheoryData<string, string, string> WriterCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var table in Tables)
            data.Add(table, "sprk_name", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        data.Add(" SPRK_ExternalRecordAccess ", "sprk_accesslevel", InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        foreach (var root in Roots)
        {
            data.Add(root, "sprk_issecure", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
            data.Add(root, "sprk_containerid", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
            data.Add(root, "sprk_SecurityBU@odata.bind", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
            data.Add(root, "SPRK_AccessPermission", InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        }

        return data;
    }

    // ── The refusal itself ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryAccessTableForm))]
    public void AnAccessTable_InAnySpelling_IsRefusedWhole(string table, string key)
    {
        var refusal = InheritedAccessPermission.RefuseAppOnlyWrite(table, [key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        refusal.Reason.Should().StartWith(table.Trim().ToLowerInvariant() + " records ");
    }

    [Fact]
    public void AnAccessTable_IsRefused_EvenWhenThePayloadNamesNoColumn()
    {
        foreach (var table in Tables)
            Code(table).Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode, table);
    }

    [Theory]
    [MemberData(nameof(EverySecureRootForm))]
    public void ASecureRootColumn_InAnyForm_IsRefusedOnEveryRoot(string table, string key, string column)
    {
        var refusal = InheritedAccessPermission.RefuseAppOnlyWrite(table, ["sprk_description", key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        refusal.Reason.Should().StartWith(column + " of a secure root ");
    }

    [Theory]
    [InlineData("sprk_description")]
    [InlineData("sprk_performancesummary")]
    [InlineData("sprk_name")]
    [InlineData("sprk_issecurex")]
    [InlineData("sprk_issecurename")]
    [InlineData("sprk_securitybuname")]
    [InlineData("sprk_containeridx")]
    [InlineData("sprk_accesspermissionname")]
    [InlineData("sprk_accesspermissiongrant")]
    [InlineData("sprk_regardingmatter")]
    public void AnOrdinaryColumnOnARoot_IsNotRefused(string key)
    {
        foreach (var root in Roots)
            Code(root, key).Should().BeNull($"'{key}' is not refused on {root}");
    }

    [Theory]
    [InlineData("sprk_event", "sprk_issecure")]
    [InlineData("sprk_todo", "sprk_containerid")]
    [InlineData("sprk_invoice", "sprk_issecure")]
    [InlineData("sprk_servicerequest", "sprk_securitybu")]
    [InlineData("sprk_communication", "sprk_accesspermission")]
    [InlineData("contact", "sprk_containerid")]
    [InlineData("businessunit", "sprk_name")]
    public void TheSameColumnOnANonRoot_IsNotRefusedAsASecureRoot(string table, string key) =>
        Code(table, key).Should().BeNull($"'{key}' on {table} is no secure root's column");

    [Theory]
    [InlineData("sprk_externalrecordaccesses")]
    [InlineData("sprk_externalrecordaccessx")]
    [InlineData("sprk_contactorganizationrole")]
    [InlineData("sprk_noaccess")]
    [InlineData("sprk_assignedaccessrule")]
    [InlineData("team")]
    [InlineData("contact")]
    [InlineData("sprk_organization")]
    [InlineData("sprk_matterx")]
    [InlineData(null)]
    public void ANeighbouringTable_IsNotRefusedWhole(string? table)
    {
        Code(table, "sprk_name").Should().BeNull($"'{table}' is not an access table");
        Code(table, "sprk_issecure").Should().BeNull($"'{table}' is not a secure root");
    }

    [Fact]
    public void TheAccessTableSet_IsExactlyTheseTables() =>
        InheritedAccessPermission.AccessTables.Should().BeEquivalentTo(Tables);

    [Fact]
    public void TheSecureRoots_AreExactlyTheseTables_EachWithExactlyTheseColumns()
    {
        InheritedAccessPermission.SecureRootColumns.Keys.Should().BeEquivalentTo(Roots);
        foreach (var root in Roots)
            InheritedAccessPermission.SecureRootColumns[root].Should().BeEquivalentTo(RootColumns, root);
    }

    [Fact]
    public void TheExistingRefusals_AreUnchanged_AndTheirPrecedenceHolds()
    {
        Code("sprk_document", "sprk_accesspermission").Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        Code("sprk_document", "sprk_parentdocument").Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        Code("sprk_document", "sprk_graphitemidbound").Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        Code("sprk_event", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        Code("sprk_event", "sprk_createdbyperson").Should().Be(InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
        Code("sprk_document", "sprk_issecure", "sprk_containerid").Should().BeNull("a document is no secure root");

        // An access table comes first, whatever else the payload names; then a secure root's columns before the owner and
        // the creator.
        Code("sprk_externalrecordaccess", "ownerid", "sprk_createdbyperson")
            .Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        Code("sprk_matter", "sprk_createdbyperson", "ownerid", "sprk_issecure")
            .Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        Code("sprk_matter", "sprk_createdbyperson", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
    }

    // ── Drift ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The table constants the access readers and the grant writers name: every one must be refused whole.</summary>
    private static readonly string[] ReaderTables =
    [
        ExternalGrantLifecycle.EntityLogicalName,
        GrantExternalAccessEndpoint.EntityLogicalName,
        WorkforceRoleCeilingGate.GrantTable,
        SecureChildReconciler.GrantTable,
        ExternalOrganizationMembership.EntityLogicalName,
        NoAccessListReader.EntityLogicalName,
        AssignedAccessStore.EntityLogicalName,
    ];

    [Fact]
    public void EveryTableTheAccessReadersQuery_IsRefusedWhole() =>
        NotRefused(ReaderTables).Should().BeEmpty("every table the access readers decide access from is the BFF's alone");

    [Fact]
    public void TheDriftCheck_FiresOnAReaderTableTheRefusalDoesNotList() =>
        NotRefused([.. ReaderTables, "sprk_accessgrantx"]).Should().Equal("sprk_accessgrantx");

    [Fact]
    public void EachReadersCollection_IsItsRefusedTablesCollection()
    {
        // The readers query by collection; each collection is the refused logical name's, so a renamed reader drifts here.
        foreach (var (set, logical) in new[]
                 {
                     (ExternalGrantLifecycle.EntitySet, ExternalGrantLifecycle.EntityLogicalName),
                     (ExternalOrganizationMembership.EntitySet, ExternalOrganizationMembership.EntityLogicalName),
                     (NoAccessListReader.EntitySet, NoAccessListReader.EntityLogicalName),
                     (AssignedAccessStore.EntitySet, AssignedAccessStore.EntityLogicalName),
                 })
        {
            set.Should().BeOneOf(logical + "s", logical + "es", logical[..^1] + "ies");
            InheritedAccessPermission.AccessTables.Should().Contain(logical);
        }
    }

    [Fact]
    public void EveryRootTheRegistryAndTheGrantModelName_IsASecureRoot()
    {
        var roots = RecordTableRules.All.Where(r => r.Kind == RecordTableKind.Root).Select(r => r.Table)
            .Concat(Enum.GetValues<ExternalGrantRootType>().Select(ExternalGrantRoot.LogicalNameFor))
            .Concat([SecureRootInheritance.Matter, SecureRootInheritance.Project, SecureRootInheritance.WorkAssignment]);

        roots.Where(r => !InheritedAccessPermission.SecureRootColumns.ContainsKey(r)).Should().BeEmpty();
    }

    [Fact]
    public void EverySecureRootColumnTheCreateProtects_AndTheContainer_IsRefusedOnEveryRoot()
    {
        // One source of truth with #2220's create side, plus the readers' own constants.
        string[] columns =
        [
            .. RecordTableRules.SecureRootAccessColumns,
            RecordContainerResolver.ContainerColumn,
            SecurableEntityRegistry.SecureFlagAttribute,
            SecurableEntityRegistry.SecurityBusinessUnitAttribute,
            InheritedAccessPermission.Column,
            AccessInheritance.Column,
        ];
        foreach (var root in Roots)
        {
            foreach (var column in columns)
                Code(root, column).Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode, $"{column} on {root}");
        }
    }

    /// <summary>The tables of <paramref name="tables"/> the refusal does not refuse whole.</summary>
    private static IEnumerable<string> NotRefused(IEnumerable<string> tables) =>
        tables.Where(t => Code(t, "sprk_name") != InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode).ToList();

    private static string? Code(string? table, params string[] keys) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, keys)?.RefusalCode;

    // ── Update Record node / action seam (app-only) ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WriterCases))]
    public async Task APlaybookUpdate_OfAnAccessTableOrASecureRootColumn_IsRefused_AndNothingIsWritten(
        string table, string key, string code)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_description"] = "x", [key] = Forged.ToString("D") },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written");
    }

    [Theory]
    [InlineData("sprk_externalrecordaccess", "sprk_Contact", "contact")]
    [InlineData("sprk_contactorganization", "sprk_Organization", "sprk_organization")]
    [InlineData("sprk_noaccessentry", "sprk_SubjectContact", "contact")]
    [InlineData("sprk_matter", "sprk_SecurityBU", "team")] // the refusal reads the bind key; the target only needs a pinned set
    public async Task APlaybookLookup_IntoAnAccessTableOrOntoASecureRootColumn_IsRefused_AndNothingIsWritten(
        string table, string field, string target)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup(field, target, Forged.ToString("D"))]),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().BeOneOf(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode,
                InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written");
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_performancesummary")]
    [InlineData("sprk_matter", "sprk_description")]
    [InlineData("sprk_project", "sprk_description")]
    [InlineData("sprk_workassignment", "sprk_description")]
    public async Task APlaybookUpdate_OfAnOrdinaryColumnOnARoot_IsWritten(string table, string column)
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { [column] = "a summary" },
                Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey(column)),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Fact]
    public async Task AnImpersonatedUpdate_OfAnAccessTable_IsNotAskedTheAppOnlyRefusal()
    {
        // A user's own write runs as that user, so Dataverse's privileges decide it (the refusal is for app-only writers).
        var user = Guid.Parse("22510000-0000-0000-0000-00000000e4ee");
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_externalrecordaccess", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x" },
                Lookups: null, ImpersonateSystemUserId: user),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_externalrecordaccess", Row, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), user),
            Times.Once);
    }

    // ── Output mapping (DataverseUpdateHandler, app-only) ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WriterCases))]
    public async Task AnOutputMapping_OfAnAccessTableOrASecureRootColumn_IsRefused_AndNothingIsWritten(
        string table, string key, string code)
    {
        var act = () => Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { ["sprk_description"] = "x", [key] = Forged.ToString("D") },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("the refusal comes before anything is written");
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_performancesummary")]
    [InlineData("sprk_project", "sprk_description")]
    [InlineData("sprk_workassignment", "sprk_description")]
    public async Task AnOutputMapping_OfAnOrdinaryColumnOnARoot_IsWritten(string table, string column)
    {
        await Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { [column] = "a summary" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey(column)),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    // ── Field-mapping push (a rule naming one is skipped; the rest of the push applies) ──────────────────────────

    [Theory]
    [InlineData("sprk_matter", "sprk_issecure")]
    [InlineData("sprk_project", "sprk_containerid")]
    [InlineData("sprk_workassignment", "sprk_securitybu")]
    public async Task APushRule_OntoASecureRootColumn_IsSkipped_AndTheOtherColumnsArePushed(string table, string target)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            table, Rule("sprk_name", "sprk_description"), Rule("sprk_name", target));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            It.IsAny<CancellationToken>(), Caller), Times.Once);
        results.Should().ContainSingle(r => r.TargetField == target)
            .Which.Status.Should().Be(FieldMappingStatus.Skipped);
    }

    [Fact]
    public async Task APushIntoAnAccessTable_SkipsEveryRule_AndWritesNothing()
    {
        var (updated, failed, skipped, errors, results) = await Push(
            "sprk_externalrecordaccess", Rule("sprk_name", "sprk_description"), Rule("sprk_name", "sprk_accesslevel"));

        (updated, failed, skipped).Should().Be((0, 0, 1));
        errors.Should().BeEmpty();
        _writes.Invocations.Should().BeEmpty();
        results.Should().HaveCount(2).And.OnlyContain(r => r.Status == FieldMappingStatus.Skipped);
    }

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Matter 2251" },
            table,
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None);

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection().AddSingleton(EntitySetNameFixtures.Service()).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private DataverseUpdateHandler Handler()
    {
        var world = new StampWorld();
        return new DataverseUpdateHandler(
            _writes.Object, world.Service, world.Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
    }
}
