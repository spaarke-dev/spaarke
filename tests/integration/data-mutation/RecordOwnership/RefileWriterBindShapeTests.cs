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

    private const int CollaborateMask = 1 | 2 | 4 | 16;
    private const string MatterBind = "sprk_RegardingMatter@odata.bind";

    private static readonly DataversePrincipalRef Secure = DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam);
    private static readonly DataversePrincipalRef General = DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam);

    private SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly Mock<IThreadResolver> _threads = new();
    private readonly Mock<IFieldMappingDataverseService> _write = new(MockBehavior.Strict);
    private readonly List<Dictionary<string, object?>> _writes = new();

    public RefileWriterBindShapeTests()
    {
        _write
            .Setup(w => w.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>()))
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
                LegacyFields: new Dictionary<string, string?> { [MatterBind] = $"/sprk_matters({SecureMatter})" }, Lookups: null),
            CancellationToken.None);

        AssertMovedIntoSecure();
    }

    [Fact(DisplayName = "#2091 r3: the output orchestrator's update binding a communication's matter to a secure matter re-owns it, and the to-do below is isolated")]
    public async Task DataverseUpdateHandler_ABindIntoASecureMatter_ReownsTheRow_AndIsolatesWhatIsFiledUnderIt()
    {
        AnOrdinaryCommunication();

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
                LegacyFields: new Dictionary<string, string?> { [MatterBind] = bind }, Lookups: null),
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
        _threads.Verify(t => t.ReconcileThreadForFilingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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

    /// <summary>An isolated communication under the secure matter, an isolated to-do under it, both with the sharee's mirror.</summary>
    private void ASecureCommunication(bool alsoUnderOrdinaryProject = false)
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

    private static CoreAncestorRestamper Restamper() =>
        new StampWorld().Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]).Restamper;

    /// <summary>The confirming user holds Full Access on every record (F3 permits the move out of Secure).</summary>
    private static IDataverseRecordShareService FullAccessRights()
    {
        var rights = new Mock<IDataverseRecordShareService>();
        rights
            .Setup(r => r.GetPrincipalRightsAsync(ConfirmingUser, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.Write | AccessRights.Delete);
        return rights.Object;
    }

    private IServiceScopeFactory Scopes() =>
        new ServiceCollection()
            .AddSingleton(Restamper())
            .AddSingleton<IRecordOwnershipResolver>(Resolver())
            .AddSingleton(_threads.Object)
            .AddSingleton(FullAccessRights())
            .AddScoped(_ => Reconciler())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
            .GetRequiredService<IServiceScopeFactory>();

    private UpdateRecordActionCore Core() => new(_write.Object, Scopes(), NullLogger.Instance);

    private DataverseUpdateHandler Handler() =>
        new(
            _write.Object, new Mock<IGenericEntityService>().Object, Restamper(), Resolver(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance, Reconciler());

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string bindKey, string bindValue, IServiceScopeFactory? scopes)
    {
        var rule = new FieldMappingRuleDto
        {
            SourceField = "sprk_lookupsource", TargetField = SecureRootInheritance.NormalizeColumn(bindKey),
            SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
        };
        return FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _write.Object, Restamper(), [rule],
            new Dictionary<string, object?> { ["sprk_lookupsource"] = Guid.NewGuid() },
            "sprk_communication", [Communication], ConfirmingUser, NullLogger.Instance, CancellationToken.None,
            scopes, rootFiling: null,
            lookupBinds: new Dictionary<FieldMappingRuleDto, LookupBindPlan> { [rule] = new(bindKey, bindValue, null) });
    }
}
