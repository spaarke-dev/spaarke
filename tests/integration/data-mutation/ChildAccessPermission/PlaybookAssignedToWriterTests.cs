using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// #2250 (uac-r2, owner decision: option 2): a playbook never writes the Assigned-To columns of a project, matter or work
/// assignment. Those columns drive the Assigned-To grants (<c>AssignedAccessMaterializer</c>, which grants Collaborate to the
/// assignee), and a playbook write runs as the BFF's System Administrator or for a configuration's author, so the grant would
/// have no grantor whom anyone checked. The Update Record node (<see cref="UpdateRecordActionCore"/>, a
/// <see cref="UpdateRecordPayloadSource.Configuration"/> payload) and a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>) ask <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> as
/// <see cref="InheritedAccessPermission.WriteIdentity.Playbook"/>, which refuses them with
/// <see cref="InheritedAccessPermission.PlaybookAssignedToWriteReasonCode"/>; as every refusal there, the whole write is
/// refused and nothing of the payload is written. Must fire: every registry column of every root, in its plain, case-variant,
/// <c>@odata.bind</c> and <c>_{column}_value</c> forms, through both writers. Must not fire: the user-driven writers (a
/// confirmed proposal written AS its user, the field-mapping push, the create-time mapping and Repair, which ask as
/// <see cref="InheritedAccessPermission.WriteIdentity.AppOnly"/> or <see cref="InheritedAccessPermission.WriteIdentity.Caller"/>),
/// the same columns on a table that confers no Assigned-To grant, and ordinary columns on a root.
/// </summary>
public class PlaybookAssignedToWriterTests
{
    private static readonly Guid Row = Guid.Parse("22500000-0000-0000-0000-00000000e401");
    private static readonly Guid Caller = Guid.Parse("22500000-0000-0000-0000-00000000e4ca");
    private static readonly Guid Assignee = Guid.Parse("22500000-0000-0000-0000-00000000e4ff");

    private static readonly string[] Roots = ["sprk_project", "sprk_matter", "sprk_workassignment"];

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>Every registry column of every root, in each form a payload key can take.</summary>
    public static TheoryData<string, string> EveryRegistryColumnInEveryForm()
    {
        var data = new TheoryData<string, string>();
        foreach (var root in Roots)
        {
            foreach (var column in MembershipOptionsDefaults.CanonicalAccessConferringRegistry[root].Select(c => c.Field))
            {
                data.Add(root, column);
                data.Add(root, column.ToUpperInvariant());
                data.Add(root, $"{column}@odata.bind");
                data.Add(root, $"_{column}_value");
            }
        }

        return data;
    }

    // ── The deny-list ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryRegistryColumnInEveryForm))]
    public void APlaybookWrite_OfARootsAssignedToColumn_IsRefused(string root, string key)
    {
        InheritedAccessPermission.RefuseAppOnlyWrite(root, ["sprk_name", key], InheritedAccessPermission.WriteIdentity.Playbook)
            .Should().Match<RecordOwnerResolution>(r => r.RefusalCode == InheritedAccessPermission.PlaybookAssignedToWriteReasonCode);
    }

    [Fact]
    public void EveryColumnTheMaterializerGrantsFrom_IsRefusedToAPlaybook()
    {
        // Drift: the grant code reads the bound registry for the three roots, seeded from the canonical one; every column it
        // names is refused here, so a registry column added without the sprk_assigned prefix fails this test.
        foreach (var root in Roots)
        {
            foreach (var column in MembershipOptionsDefaults.CanonicalAccessConferringRegistry[root])
            {
                PlaybookCode(root, column.Field)
                    .Should().Be(InheritedAccessPermission.PlaybookAssignedToWriteReasonCode, $"{column.Field} on {root}");
            }
        }
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_assignedattorney1")]
    [InlineData("sprk_project", "sprk_assignedlawfirm1@odata.bind")]
    [InlineData("sprk_workassignment", "sprk_assignedto")]
    public void ANonPlaybookWriter_OfARootsAssignedToColumn_IsNotRefused(string root, string key)
    {
        // The create-time mapping and Repair ask as AppOnly, the field-mapping push as Caller: they keep mapping Assigned To.
        InheritedAccessPermission.RefuseAppOnlyWrite(root, [key], InheritedAccessPermission.WriteIdentity.AppOnly).Should().BeNull();
        InheritedAccessPermission.RefuseAppOnlyWrite(root, [key]).Should().BeNull("AppOnly is the default");
        InheritedAccessPermission.RefuseAppOnlyWrite(root, [key], InheritedAccessPermission.WriteIdentity.Caller).Should().BeNull();
    }

    [Theory]
    [InlineData("sprk_event", "sprk_assignedto")]
    [InlineData("sprk_todo", "sprk_assignedto")]
    [InlineData("sprk_invoice", "sprk_assignedto1")]
    [InlineData("sprk_analysis", "sprk_assignedattorney1")]
    [InlineData("sprk_matter", "sprk_mattername")]
    [InlineData("sprk_matter", "sprk_assignee")]
    public void APlaybookWrite_ThatGrantsNoAssignedToAccess_IsNotRefused(string table, string key)
    {
        // Only the three roots are materialized (owner answer A6); a child's Assigned To and a root's ordinary column are data.
        InheritedAccessPermission.RefuseAppOnlyWrite(table, [key], InheritedAccessPermission.WriteIdentity.Playbook).Should().BeNull();
    }

    [Fact]
    public void APlaybookWrite_IsStillAskedEveryAppOnlyRefusal()
    {
        PlaybookCode("sprk_matter", "sprk_containerid").Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        PlaybookCode("sprk_assignedaccess", "sprk_state").Should().Be(InheritedAccessPermission.AppOnlyAccessTableWriteReasonCode);
        PlaybookCode("sprk_event", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        PlaybookCode("sprk_communication", "sprk_isinternalonly").Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
    }

    private static string? PlaybookCode(string table, params string[] keys) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, keys, InheritedAccessPermission.WriteIdentity.Playbook)?.RefusalCode;

    // ── The Update Record node (UpdateRecordActionCore, a Configuration payload) ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryRegistryColumnInEveryForm))]
    public async Task APlaybookNode_ThatWritesAnAssignedToColumn_IsRefused_AndNothingIsWritten(string root, string key)
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var act = () => new UpdateRecordActionCore(_writes.Object, scopes.Object, NullLogger.Instance).UpdateAsync(
            new UpdateRecordActionInput(
                root, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x", [key] = $"/contacts({Assignee:D})" },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.PlaybookAssignedToWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("the whole write is refused: no field of the payload is written");
        scopes.Invocations.Should().BeEmpty("nothing was written, so the Assigned-To materializer is never reached");
    }

    [Fact]
    public async Task APlaybookNode_ThatBindsAnAssignedToLookup_IsRefused_AndNothingIsWritten()
    {
        // The node's typed lookup config: the core builds "{field}@odata.bind" itself, then asks the refusal over it.
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_matter", Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x" },
                Lookups: [new RenderedLookup("sprk_AssignedAttorney1", "contact", Assignee.ToString("D"))]),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.PlaybookAssignedToWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    public static TheoryData<Guid?, string> UnconfirmedProposals() => new()
    {
        { null, "sprk_assignedattorney1" },
        { null, "sprk_assignedattorney1@odata.bind" },
        { Guid.Empty, "sprk_assignedattorney1" },
        { Guid.Empty, "sprk_assignedattorney1@odata.bind" },
    };

    [Theory]
    [MemberData(nameof(UnconfirmedProposals))]
    public async Task AProposalNoPersonConfirmed_ThatWritesAnAssignedToColumn_IsRefused_AndNothingIsWritten(Guid? user, string key)
    {
        // #2250 review: a ConfirmedProposal with no confirming person is a model-built payload nobody confirmed, written
        // app-only, so it gets the playbook rule.
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var act = () => new UpdateRecordActionCore(_writes.Object, scopes.Object, NullLogger.Instance).UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_matter", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x", [key] = $"/contacts({Assignee:D})" },
                Lookups: null, ImpersonateSystemUserId: user, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.PlaybookAssignedToWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("the whole write is refused: no field of the payload is written");
        scopes.Invocations.Should().BeEmpty("nothing was written, so the Assigned-To materializer is never reached");
    }

    [Fact]
    public async Task APersonsConfirmedProposal_OfAnAssignedToColumn_IsWrittenAsThatPerson()
    {
        // Must not fire: the action seam's Job B apply is the person's own edit, decided by their own Dataverse privileges.
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_matter", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_assignedattorney1@odata.bind"] = $"/contacts({Assignee:D})" },
                Lookups: null, ImpersonateSystemUserId: Caller, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_matter", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_assignedattorney1@odata.bind")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task APlaybookNode_OfAnOrdinaryRootColumn_IsWritten()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_matter", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_performancesummary"] = "x" }, Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_matter", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_performancesummary")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── A playbook's output mapping (DataverseUpdateHandler) ─────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryRegistryColumnInEveryForm))]
    public async Task AnOutputMapping_ThatWritesAnAssignedToColumn_IsRefused_AndNothingIsWritten(string root, string key)
    {
        var act = () => Handler().UpdateAsync(
            root, Row, new Dictionary<string, object?> { ["sprk_name"] = "x", [key] = $"/contacts({Assignee:D})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.PlaybookAssignedToWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("the whole write is refused: no field of the payload is written");
    }

    [Fact]
    public async Task APersonsOutputMapping_ThatWritesAnAssignedToColumn_IsRefusedToo()
    {
        // A person's run writes AS that person (#2257), but the mapping is the playbook's configuration, not theirs.
        var act = () => Handler().UpdateAsync(
            "sprk_workassignment", Row, new Dictionary<string, object?> { ["sprk_assignedto"] = Assignee },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None, Caller);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode
            .Should().Be(InheritedAccessPermission.PlaybookAssignedToWriteReasonCode);
        _writes.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOutputMapping_OfAChildsAssignedTo_IsWritten()
    {
        await Handler().UpdateAsync(
            "sprk_analysis", Row, new Dictionary<string, object?> { ["sprk_assignedattorney1"] = Assignee },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_analysis", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_assignedattorney1")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The field-mapping push (user-driven, as its caller): Assigned To is still pushed ─────────────────────────────

    [Fact]
    public async Task APushRule_TargetingARootsAssignedTo_IsStillPushed()
    {
        var (updated, failed, skipped, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object,
            new StampWorld().Restamper,
            [new FieldMappingRuleDto
            {
                SourceField = "sprk_assignedattorney1", TargetField = "sprk_assignedattorney1",
                SourceFieldType = "Text", TargetFieldType = "Text",
            }],
            new Dictionary<string, object?> { ["sprk_assignedattorney1"] = Assignee.ToString("D") },
            "sprk_workassignment",
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None);

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            "sprk_workassignment", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_assignedattorney1")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

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
