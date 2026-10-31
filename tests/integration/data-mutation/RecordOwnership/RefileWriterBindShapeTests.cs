using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// GitHub #2091 round 3 — the Web API can set a lookup ONLY as <c>"sprk_RegardingMatter@odata.bind": "/sprk_matters(&lt;id&gt;)"</c>,
/// so that is the shape a re-file arrives in on the playbook update node (<see cref="UpdateRecordActionCore"/>), the output
/// orchestrator's generic update (<see cref="DataverseUpdateHandler"/>) and the field-mapping push. Each must re-derive the
/// row's owner (<see cref="IRecordOwnershipResolver.ReparentAsync"/>) and run the step after the re-file.
/// </summary>
/// <remarks>
/// Everything real over one in-memory Dataverse (<see cref="SecureChildShareWorld"/>): the REAL ownership resolver for the
/// writer's reparent, the REAL reconciler for the step after it. The write itself only moves the lookup, as Dataverse does;
/// the owner moves only because the writer's reparent assigned it, and the to-do filed under the communication is isolated or
/// released only because the step ran over the re-filed row. Before round 3 a bind was not a parent change: the row was
/// written with no reparent, kept its old owner, and the to-do below it never followed.
/// </remarks>
public sealed class RefileWriterBindShapeTests
{
    private static readonly Guid SecureMatter = Guid.Parse("20910003-0000-4000-8000-000000000001");
    private static readonly Guid OrdinaryMatter = Guid.Parse("20910003-0000-4000-8000-000000000002");
    private static readonly Guid OrdinaryProject = Guid.Parse("20910003-0000-4000-8000-000000000003");
    private static readonly Guid Communication = Guid.Parse("20910003-0000-4000-8000-0000000000c1");
    private static readonly Guid TodoUnderCommunication = Guid.Parse("20910003-0000-4000-8000-0000000000d1");
    private static readonly Guid Sharee = Guid.Parse("20910003-0000-4000-8000-0000000000b1");
    private static readonly Guid ConfirmingUser = Guid.Parse("20910003-0000-4000-8000-0000000000e1");
    private static readonly Guid ThreadA = Guid.Parse("21100003-0000-4000-8000-0000000000a1");
    private static readonly Guid ThreadB = Guid.Parse("21100003-0000-4000-8000-0000000000a2");

    private const int CollaborateMask = 1 | 2 | 4 | 16;
    private const string MatterBind = "sprk_RegardingMatter@odata.bind";
    private const string ProjectBind = "sprk_RegardingProject@odata.bind";
    private const string ThreadBind = "sprk_CommunicationThread@odata.bind";

    private static readonly DataversePrincipalRef Secure = DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam);
    private static readonly DataversePrincipalRef General = DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam);

    private SecureChildShareWorld _world = SecureChildShareWorld.Standard();

    /// <summary>The core-ancestor re-stamp's own Dataverse: the communication, and a to-do filed under it that carries no
    /// copy of its matter yet — so a re-stamp after a filing write (whether or not it moved a parent) patches the to-do.</summary>
    private readonly StampWorld _stamps = new StampWorld()
        .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)])
        .Row("sprk_todo", TodoUnderCommunication, [("sprk_regardingcommunication", "sprk_communication", Communication)]);
    private readonly FakeRecordShareTable _shares = new();
    private readonly Mock<IThreadResolver> _threads = new();
    private readonly Mock<IFieldMappingDataverseService> _write = new(MockBehavior.Strict);
    private readonly List<Dictionary<string, object?>> _writes = new();
    private readonly ITestOutputHelper _output;

    /// <summary>Who every write must be sent AS (MSCRMCallerID): the node and the push act for the confirming user; the
    /// output orchestrator's update acts for nobody. The strict write mock refuses any other, so a writer that dropped its
    /// impersonation fails.</summary>
    private Guid? _writesAs = ConfirmingUser;

    public RefileWriterBindShapeTests(ITestOutputHelper output)
    {
        _output = output;
        _write
            .Setup(w => w.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.Is<Guid?>(g => g == _writesAs)))
            .Returns((string table, Guid id, Dictionary<string, object?> fields, CancellationToken _, Guid? _) =>
            {
                _writes.Add(fields);
                ApplyBinds(table, id, fields); // the lookup moves; the owner is the writer's reparent's to move
                return Task.CompletedTask;
            });
    }

    // ── move IN to Secure ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2091 r3: an UpdateRecord node binding a communication's matter to a secure matter re-owns it, and the to-do below is isolated")]
    public async Task UpdateRecordActionCore_ABindIntoASecureMatter_ReownsTheRow_AndIsolatesWhatIsFiledUnderIt()
    {
        AnOrdinaryCommunication();

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [MatterBind] = $"/sprk_matters({SecureMatter})" }, Lookups: null,
                ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);

        AssertMovedIntoSecure();
    }

    [Fact(DisplayName = "#2091 r3: the output orchestrator's update binding a communication's matter to a secure matter re-owns it, and the to-do below is isolated")]
    public async Task DataverseUpdateHandler_ABindIntoASecureMatter_ReownsTheRow_AndIsolatesWhatIsFiledUnderIt()
    {
        AnOrdinaryCommunication();

        _writesAs = null;
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = $"/sprk_matters({SecureMatter})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        AssertMovedIntoSecure();
    }

    [Fact(DisplayName = "#2091 r3: a field-mapping push binding a child's matter to a secure matter re-owns it, and the to-do below is isolated")]
    public async Task FieldMappingPush_ABindIntoASecureMatter_ReownsTheChild_AndIsolatesWhatIsFiledUnderIt()
    {
        AnOrdinaryCommunication();

        var (updated, failed, _, errors, _) = await Push(MatterBind, $"/sprk_matters({SecureMatter})", Scopes());

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0));
        AssertMovedIntoSecure();
    }

    // ── move OUT of Secure ────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2091 r3: an UpdateRecord node (acting for a Full Access user) binding a communication's matter out of Secure re-owns it, and the to-do below is released")]
    public async Task UpdateRecordActionCore_ABindOutOfASecureMatter_ReownsTheRow_AndReleasesWhatIsFiledUnderIt()
    {
        ASecureCommunication();

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [MatterBind] = $"/sprk_matters({OrdinaryMatter})" }, Lookups: null,
                ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);

        _writes.Should().ContainSingle();
        _world.OwnerOf("sprk_communication", Communication).Should().Be(General, "the node's reparent re-derived the owner from the ordinary matter");
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(General,
            "the step after the re-file released the to-do isolated only through the communication");
        _shares.MaskOf("sprk_communication", Communication, DataversePrincipalRef.User(Sharee)).Should().BeNull();
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().BeNull();
    }

    [Fact(DisplayName = "#2091 r3: the output orchestrator's update (acting for no person) binding a communication's matter out of Secure is refused and writes nothing")]
    public async Task DataverseUpdateHandler_ABindOutOfASecureMatter_IsRefused_ForAWriterActingForNoPerson()
    {
        ASecureCommunication();

        _writesAs = null;
        var act = () => Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = $"/sprk_matters({OrdinaryMatter})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        await act.Should().ThrowAsync<RecordOwnerUnresolvedException>("leaving a secure record is an un-secure, and no person asked for it");
        AssertNothingMoved();
    }

    // ── a bind CLEAR ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2091 r3: the output orchestrator's update clearing a communication's secure matter through its bind key is a parent change (refused: no person)")]
    public async Task DataverseUpdateHandler_ABindClearOutOfASecureMatter_IsAParentChange()
    {
        ASecureCommunication();

        _writesAs = null;
        var act = () => Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        await act.Should().ThrowAsync<RecordOwnerUnresolvedException>(
            "the clear is entered under the column it clears, so the reparent sees the row leave its secure matter");
        AssertNothingMoved();
    }

    [Fact(DisplayName = "#2091 r3: an UpdateRecord node clearing a communication's secure matter through its bind key re-owns it from what remains, and releases the to-do")]
    public async Task UpdateRecordActionCore_ABindClearOutOfASecureMatter_ReownsTheRow_AndReleasesWhatIsFiledUnderIt()
    {
        ASecureCommunication(alsoUnderOrdinaryProject: true);

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [MatterBind] = null }, Lookups: null,
                ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);

        _world.OwnerOf("sprk_communication", Communication).Should().Be(General);
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(General);
    }

    // ── a bind that names no row of the lookup's target: refused, nothing read or written ──────────────────────────

    [Theory(DisplayName = "#2091 r3: a filing lookup bound to anything but /<target set>(<id>) is refused by both generic writers, before any read or write")]
    [InlineData("/sprk_projects(20910003-0000-4000-8000-000000000003)")] // another table's set
    [InlineData("/sprk_matters(not-a-guid)")]
    [InlineData("/sprk_matters(00000000-0000-0000-0000-000000000000)")]
    [InlineData("sprk_matters")]
    [InlineData("")]
    public async Task BothWriters_AMalformedOrMismatchedFilingBind_IsRefused(string bind)
    {
        AnOrdinaryCommunication();

        var node = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [MatterBind] = bind }, Lookups: null,
                ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);
        var handler = () => Handler().UpdateAsync(
            "sprk_communication", Communication, new Dictionary<string, object?> { [MatterBind] = bind },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await node.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        (await handler.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
        _writes.Should().BeEmpty();
        _world.QueriedTables.Should().BeEmpty("the bind is refused before anything is read");
    }

    // ── the push: per-row failure semantics, and a push that files nothing ─────────────────────────────────────────

    [Fact(DisplayName = "#2091 r3: a field-mapping push that writes no filing lookup runs no reparent and no step after a re-file")]
    public async Task FieldMappingPush_ANonFilingBind_RunsNoReparentAndNoStep()
    {
        AnOrdinaryCommunication();

        // A communication's contact is a relationship, not a filing parent (SecureChildLineage).
        var (updated, failed, _, _, _) = await Push("sprk_Contact@odata.bind", "/contacts(20910003-0000-4000-8000-0000000000f1)", Scopes());

        (updated, failed).Should().Be((1, 0));
        _world.QueriedTables.Should().BeEmpty("no isolation read, no reparent, no step");
        _world.OwnerWrites.Should().BeEmpty();
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "#2091 r3: a field-mapping push that re-files a child on a host that cannot re-own it fails THAT child and writes nothing")]
    public async Task FieldMappingPush_ARefileWithNoReowningServices_FailsTheChild_WritesNothing()
    {
        AnOrdinaryCommunication();

        var (updated, failed, _, errors, _) = await Push(MatterBind, $"/sprk_matters({SecureMatter})", scopes: null);

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle().Which.RecordId.Should().Be(Communication);
        _writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2091 r3: a field-mapping push whose filing bind names another table's set fails THAT child and writes nothing")]
    public async Task FieldMappingPush_AMismatchedFilingBind_FailsTheChild_WritesNothing()
    {
        AnOrdinaryCommunication();

        var (updated, failed, _, errors, _) = await Push(MatterBind, $"/sprk_projects({OrdinaryProject})", Scopes());

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle().Which.Error.Should().Contain(RecordOwnerRefusal.ParentUnresolved);
        _writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "#2143 r2: a field-mapping push whose re-filed child's owner cannot be settled fails THAT child with a fixed message, never the exception's")]
    public async Task FieldMappingPush_AnUnsettledOwner_FailsTheChildWithAFixedMessage_AndRunsNoStepAfter()
    {
        AnOrdinaryCommunication();
        var unsettled = new RecordOwnerUnsettledException(
            "sprk_communication", Communication,
            $"The owner of sprk_communication {Communication:D} was not settled: held in the Secure Record business unit (#2143).");
        var ownership = new RecordOwnershipResolverDouble { FaultAfterChange = unsettled };
        var scopes = new ServiceCollection()
            .AddSingleton(Restamper())
            .AddSingleton<IRecordOwnershipResolver>(ownership)
            .AddSingleton(_threads.Object)
            .AddSingleton(FullAccessRights())
            .AddSingleton(EntitySetNameFixtures.Service())
            .AddScoped(_ => Reconciler())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
            .GetRequiredService<IServiceScopeFactory>();

        var (updated, failed, _, errors, _) = await Push(MatterBind, $"/sprk_matters({SecureMatter})", scopes);

        (updated, failed).Should().Be((0, 1));
        var error = errors.Should().ContainSingle().Which;
        error.RecordId.Should().Be(Communication);
        error.Error.Should().NotContain(Communication.ToString("D"))
            .And.NotContain("Secure Record").And.NotContain("#2143").And.NotContain("sprk_communication");
        error.Error.Should().Contain("Reload it and push again");
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never,
            "no step after the re-file runs for a row whose owner was not settled");
    }

    // ── #2091 round 4: the push's move OUT of Secure (F3, as the caller), and a push that moves nothing ─────────────

    [Fact(DisplayName = "#2091 r4: a field-mapping push (caller with Full Access) binding a child's matter out of Secure re-owns it, releases the to-do and removes the mirrors")]
    public async Task FieldMappingPush_ABindOutOfASecureMatter_WithFullAccess_ReownsTheChild_AndReleasesWhatIsFiledUnderIt()
    {
        ASecureCommunication();

        var (updated, failed, _, errors, _) = await Push(MatterBind, $"/sprk_matters({OrdinaryMatter})", Scopes(FullAccessRights()));

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0));
        _writes.Should().ContainSingle("written as the caller (the strict mock accepts no other)");
        _world.OwnerOf("sprk_communication", Communication).Should().Be(General);
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(General);
        _shares.MaskOf("sprk_communication", Communication, DataversePrincipalRef.User(Sharee)).Should().BeNull();
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().BeNull();
    }

    [Fact(DisplayName = "#2091 r4: a field-mapping push (caller with no rights) binding a child's matter out of Secure fails THAT child with the F3 code and writes nothing")]
    public async Task FieldMappingPush_ABindOutOfASecureMatter_WithoutRights_FailsTheChild_OnF3()
    {
        ASecureCommunication();

        var (updated, failed, _, errors, _) = await Push(MatterBind, $"/sprk_matters({OrdinaryMatter})", Scopes(NoRights()));

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle().Which.Error.Should().Contain(SecureDesignationRemoval.NotPermittedReasonCode);
        AssertNothingMoved();
    }

    [Fact(DisplayName = "#2091 r4: a field-mapping push that re-binds the parent the child already holds writes it and runs no step after a re-file")]
    public async Task FieldMappingPush_ThatRebindsTheParentTheChildHolds_RunsNoStep()
    {
        ASecureCommunication();

        var (updated, failed, _, errors, _) = await Push(MatterBind, $"/sprk_matters({SecureMatter})", Scopes());

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0));
        _world.OwnerWrites.Should().BeEmpty("nothing moved, so nothing is re-owned");
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never,
            "the step after a re-file did not run (it reconciles a communication's thread)");
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    [Fact(DisplayName = "#2091 r4: a 500-child no-op push from a secure matter costs a few Dataverse calls per child, never a descendant pass")]
    public async Task FieldMappingPush_A500ChildNoOpPush_CostsAFewCallsPerChild()
    {
        const int count = 500;
        _world = SecureChildShareWorld.Standard().SecureRoot("sprk_matter", SecureMatter);
        var children = Enumerable.Range(1, count).Select(i => Guid.Parse($"20910004-0000-4000-8000-{i:D12}")).ToArray();
        foreach (var child in children)
        {
            _world.SecureChild("sprk_communication", child, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
            _world.SecureChild("sprk_todo", Guid.NewGuid(), ("sprk_regardingcommunication", "sprk_communication", child));
        }

        // The re-file's own cost: the core-ancestor re-stamp a filing write triggers (task 156, every push writing a filing
        // lookup, before and after #2091) is measured apart, over an empty stamp world.
        var (updated, failed, _, _, _) = await Push(
            MatterBind, $"/sprk_matters({SecureMatter})", Scopes(), children: children);

        (updated, failed).Should().Be((count, 0));
        var queries = _world.QueriedTables.Count;
        var rowReads = _world.RowReads;
        var total = queries + rowReads + _writes.Count + _world.OwnerWrites.Count + _world.StampWrites.Count;
        _output.WriteLine(
            $"500-child no-op push: {total} Dataverse calls ({(double)total / count:0.##} per child) = {queries} queries "
            + $"[{string.Join(", ", _world.QueriedTables.GroupBy(t => t).Select(g => $"{g.Key} {g.Count()}"))}] + {rowReads} row reads + "
            + $"{_writes.Count} PATCHes + {_world.OwnerWrites.Count} owner writes + {_world.StampWrites.Count} stamp writes");
        _world.OwnerWrites.Should().BeEmpty();
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _world.QueriedTables.Should().NotContain("sprk_todo", "no descendant pass ran over what is filed under any child");
        ((double)total / count).Should().BeLessThanOrEqualTo(5, "a no-op child costs its isolation read, its reparent's row read and its write — not a pass");
    }

    [Fact(DisplayName = "#2091 r4: a field-mapping push rule whose planned bind writes a different filing lookup than the rule names is refused, and nothing is written")]
    public async Task FieldMappingPush_ARuleWhoseBindIsAnotherLookup_IsRefused()
    {
        AnOrdinaryCommunication();

        var (updated, failed, skipped, _, fieldResults) = await Push(
            MatterBind, $"/sprk_matters({SecureMatter})", Scopes(), targetField: "sprk_regardingproject");

        (updated, failed, skipped).Should().Be((0, 0, 1));
        fieldResults.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Error);
        _writes.Should().BeEmpty();
        _world.OwnerOf("sprk_communication", Communication).Should().Be(General);
    }

    // ── #2091 round 5: a re-bind of the held parent at the node and the handler; a mix; a clear of an empty lookup ─────

    [Fact(DisplayName = "#2091 r5: an UpdateRecord node that re-binds the parent the communication already holds runs no step after a re-file, and still re-stamps")]
    public async Task UpdateRecordActionCore_ThatRebindsTheParentTheRowHolds_RunsNoStep_AndStillReStamps()
    {
        ASecureCommunication();

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { [MatterBind] = $"/sprk_matters({SecureMatter})" }, Lookups: null,
                ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);

        AssertNoStepButReStamped();
    }

    [Fact(DisplayName = "#2091 r5: the output orchestrator's update that re-binds the parent the communication already holds runs no step after a re-file, and still re-stamps")]
    public async Task DataverseUpdateHandler_ThatRebindsTheParentTheRowHolds_RunsNoStep_AndStillReStamps()
    {
        ASecureCommunication();

        _writesAs = null;
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = $"/sprk_matters({SecureMatter})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        AssertNoStepButReStamped();
    }

    [Fact(DisplayName = "#2091 r5: an UpdateRecord node that re-binds the held matter AND sets a project is a re-file: the step runs")]
    public async Task UpdateRecordActionCore_AnUnchangedAndAChangedFilingLookup_IsARefile_TheStepRuns()
    {
        ASecureCommunication();

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?>
                {
                    [MatterBind] = $"/sprk_matters({SecureMatter})",
                    ["sprk_RegardingProject@odata.bind"] = $"/sprk_projects({OrdinaryProject})",
                },
                Lookups: null,
                ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);

        _writes.Should().ContainSingle();
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(Communication, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.Is<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(c => c != null), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Once,
            "one column moved, so the step after a re-file ran (it reconciles a communication's thread), acting for the person the node impersonates (#2143 r7)");
        _world.OwnerOf("sprk_communication", Communication).Should().Be(Secure, "the secure matter is still held");
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(Secure);
    }

    [Fact(DisplayName = "#2091 r5: the output orchestrator's update clearing a filing lookup the communication does not hold runs no step after a re-file")]
    public async Task DataverseUpdateHandler_AClearOfAnEmptyFilingLookup_RunsNoStep()
    {
        ASecureCommunication();

        _writesAs = null;
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_RegardingProject@odata.bind"] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        _world.OwnerWrites.Should().BeEmpty("nothing moved, so nothing is re-owned");
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    // ── #2091 round 5: two rules equal by value (a record) are two rules, never a 500 ───────────────────────────────

    [Fact(DisplayName = "#2091 r5: a field-mapping push with two lookup rules equal by value applies both and fails nothing")]
    public async Task FieldMappingPush_TwoValueEqualLookupRules_AreTwoRules()
    {
        AnOrdinaryCommunication();
        var first = new FieldMappingRuleDto
        {
            SourceField = "sprk_lookupsource", TargetField = "sprk_regardingmatter",
            SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
        };
        var second = first with { };
        var plans = new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
        {
            [first] = new(MatterBind, $"/sprk_matters({SecureMatter})", null),
            [second] = new(MatterBind, $"/sprk_matters({SecureMatter})", null),
        };

        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _write.Object, Restamper(), [first, second],
            new Dictionary<string, object?> { ["sprk_lookupsource"] = Guid.NewGuid() },
            "sprk_communication", [Communication], ConfirmingUser, NullLogger.Instance, CancellationToken.None,
            Scopes(), rootFiling: null, lookupBinds: plans);

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0));
        AssertMovedIntoSecure();
    }

    // ── #2110 (ontology K3): a message's thread is a LINEAGE parent (not an ownership parent): moving it is a change ─────

    [Fact(DisplayName = "#2110 (ontology K3): the output orchestrator's update re-binding the held matter AND moving the communication's thread runs the step once")]
    public async Task DataverseUpdateHandler_TheHeldMatterAndAnotherThread_RunsTheStepOnce_2110()
    {
        ASecureCommunication(inThread: ThreadA);

        _writesAs = null;
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = $"/sprk_matters({SecureMatter})", [ThreadBind] = $"/sprk_communicationthreads({ThreadB})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        AssertTheThreadMoveRanTheStepOnce();
    }

    [Fact(DisplayName = "#2110 (ontology K3): the output orchestrator's update re-binding the held matter AND the held thread runs no step")]
    public async Task DataverseUpdateHandler_TheHeldMatterAndTheHeldThread_RunsNoStep_2110()
    {
        ASecureCommunication(inThread: ThreadA);

        _writesAs = null;
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = $"/sprk_matters({SecureMatter})", [ThreadBind] = $"/sprk_communicationthreads({ThreadA})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        AssertNoStepButReStamped();
    }

    [Fact(DisplayName = "#2110 (ontology K3): an UpdateRecord node re-binding the held matter AND moving the communication's thread runs the step once")]
    public async Task UpdateRecordActionCore_TheHeldMatterAndAnotherThread_RunsTheStepOnce_2110()
    {
        ASecureCommunication(inThread: ThreadA);

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?>
                {
                    [MatterBind] = $"/sprk_matters({SecureMatter})", [ThreadBind] = $"/sprk_communicationthreads({ThreadB})",
                },
                Lookups: null, ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);

        AssertTheThreadMoveRanTheStepOnce();
    }

    [Fact(DisplayName = "#2110 (ontology K3): an UpdateRecord node re-binding the held matter AND the held thread runs no step")]
    public async Task UpdateRecordActionCore_TheHeldMatterAndTheHeldThread_RunsNoStep_2110()
    {
        ASecureCommunication(inThread: ThreadA);

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?>
                {
                    [MatterBind] = $"/sprk_matters({SecureMatter})", [ThreadBind] = $"/sprk_communicationthreads({ThreadA})",
                },
                Lookups: null, ImpersonateSystemUserId: ConfirmingUser),
            CancellationToken.None);

        AssertNoStepButReStamped();
    }

    [Fact(DisplayName = "#2110 (ontology K3): the output orchestrator's update re-binding the held matter AND clearing the held thread runs the step once")]
    public async Task DataverseUpdateHandler_TheHeldMatterAndTheThreadCleared_RunsTheStepOnce_2110()
    {
        ASecureCommunication(inThread: ThreadA);

        _writesAs = null;
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = $"/sprk_matters({SecureMatter})", [ThreadBind] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        _world.ValueOf<EntityReference>("sprk_communication", Communication, "sprk_communicationthread").Should().BeNull();
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(Communication, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Once,
            "the held thread was cleared, so the step after a re-file ran once (it reconciles a communication's thread)");
        _world.OwnerOf("sprk_communication", Communication).Should().Be(Secure, "the thread decides no owner; the secure matter is still held");
    }

    [Theory(DisplayName = "#2110 (ontology K3): a field-mapping push re-binding the held matter runs the step once only when it also MOVES the thread")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FieldMappingPush_TheHeldMatterAndAThread_RunsTheStepOnlyWhenTheThreadMoves_2110(bool movesTheThread)
    {
        ASecureCommunication(inThread: ThreadA);
        var matterRule = new FieldMappingRuleDto
        {
            SourceField = "sprk_mattersource", TargetField = "sprk_regardingmatter",
            SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
        };
        var threadRule = new FieldMappingRuleDto
        {
            SourceField = "sprk_threadsource", TargetField = "sprk_communicationthread",
            SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 2,
        };
        var plans = new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance)
        {
            [matterRule] = new(MatterBind, $"/sprk_matters({SecureMatter})", null),
            [threadRule] = new(ThreadBind, $"/sprk_communicationthreads({(movesTheThread ? ThreadB : ThreadA)})", null),
        };

        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _write.Object, Restamper(), [matterRule, threadRule],
            new Dictionary<string, object?> { ["sprk_mattersource"] = Guid.NewGuid(), ["sprk_threadsource"] = Guid.NewGuid() },
            "sprk_communication", [Communication], ConfirmingUser, NullLogger.Instance, CancellationToken.None,
            Scopes(), rootFiling: null, lookupBinds: plans);

        errors.Should().BeEmpty();
        (updated, failed).Should().Be((1, 0));
        if (movesTheThread)
        {
            AssertTheThreadMoveRanTheStepOnce();
            // #2143 round 7 (F1): the push hands the step the person it acts as.
            _threads.Verify(t => t.ReconcileThreadForFilingAsync(
                Communication, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.Is<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(c => c != null), It.IsAny<Func<CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            AssertNoStepButReStamped();
        }
    }

    // ── #2110 (ontology K1): the same matter with a parent REMOVED is a change ───────────────────────────────────────

    [Fact(DisplayName = "#2110 (ontology K1): the output orchestrator's update re-binding the held matter AND removing the held project runs the step once")]
    public async Task DataverseUpdateHandler_TheHeldMatterWithTheProjectRemoved_RunsTheStepOnce_2110()
    {
        ASecureCommunication(alsoUnderOrdinaryProject: true);

        _writesAs = null;
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { [MatterBind] = $"/sprk_matters({SecureMatter})", [ProjectBind] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        _world.ValueOf<EntityReference>("sprk_communication", Communication, "sprk_regardingproject").Should().BeNull();
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(Communication, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Once,
            "a parent was removed, so the step after a re-file ran once (it reconciles a communication's thread)");
        _world.OwnerOf("sprk_communication", Communication).Should().Be(Secure, "the secure matter is still held");
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(Secure);
    }

    private void AssertTheThreadMoveRanTheStepOnce()
    {
        _writes.Should().ContainSingle();
        _world.ValueOf<EntityReference>("sprk_communication", Communication, "sprk_communicationthread")!.Id.Should().Be(ThreadB);
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(Communication, It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Once,
            "the thread moved, so the step after a re-file ran once (it reconciles a communication's thread)");
        _world.OwnerOf("sprk_communication", Communication).Should().Be(Secure, "the thread decides no owner; the secure matter is still held");
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(Secure);
        _shares.MaskOf("sprk_communication", Communication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    private void AssertNoStepButReStamped()
    {
        _writes.Should().ContainSingle();
        _world.OwnerWrites.Should().BeEmpty("nothing moved, so nothing is re-owned");
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<Sprk.Bff.Api.Services.Communication.FilingReconcileHost>(), It.IsAny<Sprk.Bff.Api.Services.Access.SecureRemovalCaller?>(), It.IsAny<Func<CancellationToken, Task>?>(), It.IsAny<CancellationToken>()), Times.Never,
            "the step after a re-file did not run (it reconciles a communication's thread)");
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        _stamps.PatchesTo("sprk_todo", TodoUnderCommunication).Should().NotBeEmpty(
            "the core-ancestor re-stamp after a filing write is not the step, and still ran");
    }

    // ── worlds ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An ordinary communication under the ordinary matter, an ordinary to-do under it; the secure matter's sharee.</summary>
    private void AnOrdinaryCommunication()
    {
        _world = SecureChildShareWorld.Standard()
            .SecureRoot("sprk_matter", SecureMatter)
            .OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .OrdinaryRoot("sprk_project", OrdinaryProject)
            .OrdinaryChild("sprk_communication", Communication, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter))
            .OrdinaryChild("sprk_todo", TodoUnderCommunication, ("sprk_regardingcommunication", "sprk_communication", Communication));
        _shares.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);
    }

    /// <summary>An isolated communication under the secure matter, an isolated to-do under it, both with the sharee's mirror —
    /// and, with <paramref name="inThread"/>, in that thread of the secure matter (#2110; a second one beside it).</summary>
    private void ASecureCommunication(bool alsoUnderOrdinaryProject = false, Guid? inThread = null)
    {
        var lookups = new List<(string, string, Guid)> { ("sprk_regardingmatter", "sprk_matter", SecureMatter) };
        if (alsoUnderOrdinaryProject)
            lookups.Add(("sprk_regardingproject", "sprk_project", OrdinaryProject));
        _world = SecureChildShareWorld.Standard()
            .SecureRoot("sprk_matter", SecureMatter)
            .OrdinaryRoot("sprk_matter", OrdinaryMatter)
            .OrdinaryRoot("sprk_project", OrdinaryProject)
            .SecureChild("sprk_communication", Communication, lookups.ToArray())
            .SecureChild("sprk_todo", TodoUnderCommunication, ("sprk_regardingcommunication", "sprk_communication", Communication));
        _shares.Seed("sprk_matter", SecureMatter, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _shares.Seed("sprk_communication", Communication, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _shares.Seed("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee), CollaborateMask);
        if (inThread is { } thread)
        {
            _world
                .SecureChild("sprk_communicationthread", ThreadA, ("sprk_regardingmatter", "sprk_matter", SecureMatter))
                .SecureChild("sprk_communicationthread", ThreadB, ("sprk_regardingmatter", "sprk_matter", SecureMatter))
                .Set("sprk_communication", Communication, "sprk_communicationthread", new EntityReference("sprk_communicationthread", thread));
            // #2143 r7d: record threads.
            _world.Set("sprk_communicationthread", ThreadA, "sprk_threadtype", new OptionSetValue(100000000));
            _world.Set("sprk_communicationthread", ThreadB, "sprk_threadtype", new OptionSetValue(100000000));
        }
    }

    private void AssertMovedIntoSecure()
    {
        _writes.Should().ContainSingle();
        _world.ValueOf<EntityReference>("sprk_communication", Communication, "sprk_regardingmatter")!.Id.Should().Be(SecureMatter);
        _world.OwnerOf("sprk_communication", Communication).Should().Be(Secure,
            "the writer's reparent re-derived the owner from the secure matter the bind names");
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(Secure,
            "the step after the re-file moved the to-do filed under the now-isolated communication into isolation");
    }

    private void AssertNothingMoved()
    {
        _writes.Should().BeEmpty();
        _world.OwnerOf("sprk_communication", Communication).Should().Be(Secure);
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(Secure);
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
    }

    /// <summary>Dataverse applying a PATCH's binds: the lookup moves (or clears). Nothing else is modelled.</summary>
    private void ApplyBinds(string table, Guid id, Dictionary<string, object?> fields)
    {
        foreach (var (key, value) in fields)
        {
            if (!key.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase))
                continue;

            var column = SecureRootInheritance.NormalizeColumn(key);
            if (value is not string path)
            {
                _world.Set(table, id, column, null);
                continue;
            }

            var open = path.IndexOf('(');
            var target = path[1..open] switch
            {
                "sprk_matters" => "sprk_matter",
                "sprk_projects" => "sprk_project",
                "sprk_communicationthreads" => "sprk_communicationthread",
                _ => null,
            };
            if (target is not null)
                _world.Set(table, id, column, new EntityReference(target, Guid.Parse(path[(open + 1)..^1])));
        }
    }

    // ── composition ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The REAL ownership resolver over the world (its row reads included), as the host registers it.</summary>
    private RecordOwnershipResolver Resolver()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) => Task.FromResult(_world.Retrieve(table, id, columns)));
        return new RecordOwnershipResolver(entities.Object, SecureChildShareWorld.Configuration(), NullLogger<RecordOwnershipResolver>.Instance);
    }

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, _shares, null!, threads: _threads.Object);

    private CoreAncestorRestamper Restamper() => _stamps.Restamper;

    /// <summary>The confirming user holds Full Access on every record (F3 permits the move out of Secure).</summary>
    private static IDataverseRecordShareService FullAccessRights()
    {
        var rights = new Mock<IDataverseRecordShareService>();
        rights
            .Setup(r => r.GetPrincipalRightsAsync(ConfirmingUser, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.Write | AccessRights.Delete);
        return rights.Object;
    }

    /// <summary>The confirming user holds no right on any record (F3 refuses the move out of Secure).</summary>
    private static IDataverseRecordShareService NoRights()
    {
        var rights = new Mock<IDataverseRecordShareService>();
        rights
            .Setup(r => r.GetPrincipalRightsAsync(ConfirmingUser, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.None);
        return rights.Object;
    }

    private IServiceScopeFactory Scopes(IDataverseRecordShareService? rights = null) =>
        new ServiceCollection()
            .AddSingleton(Restamper())
            .AddSingleton<IRecordOwnershipResolver>(Resolver())
            .AddSingleton(_threads.Object)
            .AddSingleton(rights ?? FullAccessRights())
            .AddSingleton(EntitySetNameFixtures.Service())
            .AddScoped(_ => Reconciler())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
            .GetRequiredService<IServiceScopeFactory>();

    private UpdateRecordActionCore Core() => new(_write.Object, Scopes(), NullLogger.Instance);

    private DataverseUpdateHandler Handler() =>
        new(
            _write.Object, new Mock<IGenericEntityService>().Object, Restamper(), Resolver(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance, Reconciler());

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string bindKey, string bindValue, IServiceScopeFactory? scopes, string? targetField = null, Guid[]? children = null,
        CoreAncestorRestamper? restamper = null)
    {
        var rule = new FieldMappingRuleDto
        {
            SourceField = "sprk_lookupsource", TargetField = targetField ?? SecureRootInheritance.NormalizeColumn(bindKey),
            SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
        };
        return FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _write.Object, restamper ?? Restamper(), [rule],
            new Dictionary<string, object?> { ["sprk_lookupsource"] = Guid.NewGuid() },
            "sprk_communication", children ?? [Communication], ConfirmingUser, NullLogger.Instance, CancellationToken.None,
            scopes, rootFiling: null,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan> { [rule] = new(bindKey, bindValue, null) });
    }
}
