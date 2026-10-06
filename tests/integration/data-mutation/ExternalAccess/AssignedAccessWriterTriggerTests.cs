using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 142, criterion 13 — every BFF writer in the census (notes §2) calls the Assigned-To
/// invariant owner AFTER its own write committed, with the root it created or updated, and a materializer fault never
/// fails the writer. The REAL materializer runs over <see cref="Harness"/>; each test asserts the access that resulted for
/// the written root (not merely that a mock was called), and a twin shows a write that touched no registry column
/// materializes nothing. KEEP path: <c>tests/integration/data-mutation/**</c> (writes and their side effects).
/// </summary>
public sealed class AssignedAccessWriterTriggerTests : TypedToolHandlerTestFixture
{
    private const string Attorney1 = "sprk_assignedattorney1";

    private readonly Harness _h = new();

    /// <summary>A scope factory whose scope resolves the harness's materializer — what the host's DI provides.</summary>
    private IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _h.Materializer);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // RecordCreationService (Office quick-create: matter, project) — incl. owner A7: the maker is assigned
    // ─────────────────────────────────────────────────────────────────────────────

    private RecordCreationService OfficeCreator(Guid createdId, Guid? makersContact, AssignedAccessMaterializer? materializer = null)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) =>
            {
                // What Dataverse stores: the created row's Assigned column, as the materializer will read it back.
                if (e.Attributes.TryGetValue("sprk_assignedtointernal", out var v) && v is EntityReference r)
                    _h.Store.Assign(e.LogicalName == "sprk_matter" ? ExternalGrantRootType.Matter : ExternalGrantRootType.Project,
                        createdId, "sprk_assignedtointernal", r.Id);
                else
                    _h.Store.Root(e.LogicalName == "sprk_matter" ? ExternalGrantRootType.Matter : ExternalGrantRootType.Project, createdId);
            })
            .ReturnsAsync(createdId);

        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return new RecordCreationService(
            entities.Object, new Mock<IFieldMappingDataverseService>().Object, ownership.Object,
            IdentityNormalizationFixtures.WithContact(makersContact).Object, Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), materializer ?? _h.Materializer,
            NullLogger<RecordCreationService>.Instance);
    }

    [Theory]
    [InlineData(QuickCreateEntityType.Matter, "sprk_matter")]
    [InlineData(QuickCreateEntityType.Project, "sprk_project")]
    public async Task OfficeQuickCreate_SharesTheRecordWithItsAssignedMaker_BeforeItReturns(
        QuickCreateEntityType type, string table)
    {
        var createdId = Guid.NewGuid();
        var (makersContact, makerUser) = _h.LinkedContact();

        var result = await OfficeCreator(createdId, makersContact).CreateAsync(new RecordCreationRequest
        {
            EntityType = type, Name = "Quick", CallerUserId = "maker-oid", OwnerSystemUserId = makerUser.ToString("D"),
        });

        result.Succeeded.Should().BeTrue();
        result.RecordId.Should().Be(createdId);
        _h.Shares.MaskOf(table, createdId, DataversePrincipalRef.User(makerUser)).Should().NotBeNull(
            "owner A7: the maker is assigned, and the Assigned-To share is issued inline (R3 'immediate')");
        _h.Store.RowsOf(createdId, makersContact).Should().ContainSingle().Which.State.Should().Be(AssignedAccessState.Shared);
    }

    [Fact]
    public async Task OfficeQuickCreate_AMaterializerFault_NeverFailsTheCreate()
    {
        var createdId = Guid.NewGuid();
        var (makersContact, makerUser) = _h.LinkedContact();
        _h.Store.FailLedgerRead = true;

        var result = await OfficeCreator(createdId, makersContact).CreateAsync(new RecordCreationRequest
        {
            EntityType = QuickCreateEntityType.Matter, Name = "Quick", CallerUserId = "maker-oid",
            OwnerSystemUserId = makerUser.ToString("D"),
        });

        result.Succeeded.Should().BeTrue("the job repairs it within minutes");
        _h.Shares.Writes.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // UpdateRecordActionCore (AI playbook UpdateRecord node + the Job B ActionSeam)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecordActionCore_ThatWritesAnAssignedLookup_MaterializesTheRoot()
    {
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        var fieldMappings = new Mock<IFieldMappingDataverseService>();
        var core = new UpdateRecordActionCore(fieldMappings.Object, Scopes(), NullLogger.Instance);

        await core.UpdateAsync(new UpdateRecordActionInput(
            "sprk_matter", matter, null, null,
            new[] { new RenderedLookup(Attorney1, "contact", contact.ToString("D")) }), CancellationToken.None);

        fieldMappings.Verify(f => f.UpdateRecordFieldsAsync("sprk_matter", matter, It.IsAny<Dictionary<string, object?>>(),
            It.IsAny<CancellationToken>(), null), Times.Once);
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateRecordActionCore_ThatWritesNoRegistryColumn_MaterializesNothing()
    {
        var matter = Guid.NewGuid();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, _h.Contact());
        var core = new UpdateRecordActionCore(new Mock<IFieldMappingDataverseService>().Object, Scopes(), NullLogger.Instance);

        await core.UpdateAsync(new UpdateRecordActionInput(
            "sprk_matter", matter, null, new Dictionary<string, string?> { ["sprk_mattername"] = "Renamed" }, null),
            CancellationToken.None);

        _h.TotalWrites.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The AI chat tools (dataverse.update_record / dataverse.create_record)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatUpdateRecord_OfAnAssignedColumn_MaterializesTheRoot_AfterThePatchCommitted()
    {
        var matter = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, contact);
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_matter')")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_matters", "PrimaryIdAttribute": "sprk_matterid" }""")));
        dataverse.Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(204, body: null));
        var handler = new DataverseUpdateRecordHandler(dataverse.Object, new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp, CreateLogger<DataverseUpdateRecordHandler>(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$$"""{"tablename":"sprk_matter","recordId":"{{{matter:D}}}","item":{"{{{Attorney1}}}":"{{{contact:D}}}"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseUpdateRecordHandler), name: "SYS-Dataverse Update Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(matter, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task ChatUpdateRecord_ThatTheUserIsRefused_MaterializesNothing()
    {
        var matter = Guid.NewGuid();
        _h.Store.Assign(ExternalGrantRootType.Matter, matter, Attorney1, _h.Contact());
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_matters", "PrimaryIdAttribute": "sprk_matterid" }""")));
        dataverse.Setup(d => d.PatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Fail(403, "0x80040220", "Principal user is missing prvWritesprk_matter"));
        var handler = new DataverseUpdateRecordHandler(dataverse.Object, new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp, CreateLogger<DataverseUpdateRecordHandler>(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: $$$"""{"tablename":"sprk_matter","recordId":"{{{matter:D}}}","item":{"{{{Attorney1}}}":"x"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseUpdateRecordHandler), name: "SYS-Dataverse Update Record"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        _h.TotalWrites.Should().Be(0, "nothing committed, so nothing is materialized");
    }

    [Fact]
    public async Task ChatCreateRecord_OfARoot_MaterializesTheCreatedRecord()
    {
        var createdId = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Project, createdId, Attorney1, contact);
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_projects", "PrimaryIdAttribute": "sprk_projectid" }""")));
        dataverse.Setup(d => d.PostAsync("/api/data/v9.2/sprk_projects", It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, Json($$"""{ "sprk_projectid": "{{createdId:D}}" }""")));
        var handler = new DataverseCreateRecordHandler(
            dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), new Mock<IFieldMappingDataverseService>().Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"sprk_project","item":{"sprk_projectname":"New"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.Grants.ActiveRowsOf(createdId, contact).Should().ContainSingle();
    }

    [Fact]
    public async Task ChatCreateRecord_OfAChildTable_MaterializesNothing()
    {
        var createdId = Guid.NewGuid();
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "EntitySetName": "sprk_events", "PrimaryIdAttribute": "sprk_eventid" }""")));
        dataverse.Setup(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(201, Json($$"""{ "sprk_eventid": "{{createdId:D}}" }""")));
        var handler = new DataverseCreateRecordHandler(
            dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), new Mock<IFieldMappingDataverseService>().Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"sprk_event","item":{"sprk_eventname":"Hearing"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _h.TotalWrites.Should().Be(0, "owner A6: a child entity's assignee gets no root grant");
    }

    /// <summary>
    /// Batch 4 integration (task 142 x task 146): since owner round 7 item 3 a root created from chat is created by the
    /// APPLICATION, owned by the resolver's team (<c>OwnedChildWrite</c>) — that path returns before the run-as-user tail,
    /// so it runs the Assigned-To materializer itself, after the create committed.
    /// </summary>
    [Fact]
    public async Task ChatCreateRecord_OfARoot_OnTheOwnedPath_MaterializesTheCreatedRecord()
    {
        var contact = _h.Contact();
        Guid? createdId = null;
        var dataverse = new Mock<IDataverseUserClient>();
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')?$select=EntitySetName")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json(
                """{ "EntitySetName": "sprk_projects", "PrimaryIdAttribute": "sprk_projectid", "OwnershipType": "UserOwned" }""")));
        dataverse.Setup(d => d.GetAsync("WhoAmI()", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json($$"""{ "UserId": "{{Guid.NewGuid():D}}" }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')?$select=LogicalName,Privileges")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json(
                """{ "LogicalName": "sprk_project", "Privileges": [ { "PrivilegeType": "Create", "Name": "prvCreatesprk_project" } ] }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.Contains("RetrieveUserSetOfPrivilegesByNames")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "RolePrivileges": [ { "PrivilegeName": "prvCreatesprk_project" } ] }""")));
        dataverse.Setup(d => d.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_project')/Attributes")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, Json("""{ "value": [] }""")));
        var appOnly = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        appOnly.Setup(a => a.UpdateRecordFieldsAsync(
                "sprk_project", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>())) // task 166 (S-67): the push now writes AS the caller
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, id, _, _, _) =>
            {
                // The created row names the contact in an "Assigned *" column (what the materializer reads back).
                createdId = id;
                _h.Store.Assign(ExternalGrantRootType.Project, id, Attorney1, contact);
            })
            .Returns(Task.CompletedTask);
        var handler = new DataverseCreateRecordHandler(
            dataverse.Object, CreateLogger<DataverseCreateRecordHandler>(),
            new Sprk.Bff.Api.Api.Agent.HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), appOnly.Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact(), Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), Scopes());

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: """{"tablename":"sprk_project","item":{"sprk_projectname":"New"}}"""),
            BuildAnalysisTool(handlerClass: nameof(DataverseCreateRecordHandler), name: "SYS-Dataverse Create Record"),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        createdId.Should().NotBeNull("the application created the root (the owned path)");
        dataverse.Verify(d => d.PostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "a run-as-user POST would mean the owned path was not taken");
        _h.Grants.ActiveRowsOf(createdId!.Value, contact).Should().ContainSingle();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Field Mapping push (a parent's mapped values pushed to its children)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FieldMappingPush_ThatWritesAnAssignedColumnOfARootChild_MaterializesIt()
    {
        var project = Guid.NewGuid();
        var contact = _h.Contact();
        _h.Store.Assign(ExternalGrantRootType.Project, project, Attorney1, contact);
        var dataverse = new Mock<IFieldMappingDataverseService>();

        var (updated, failed, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            dataverse.Object,
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper,
            new[] { new FieldMappingRuleDto { SourceField = Attorney1, TargetField = Attorney1, SourceFieldType = "Text", TargetFieldType = "Text" } },
            new Dictionary<string, object?> { [Attorney1] = contact.ToString("D") },
            "sprk_project",
            new[] { project },
            // Task 166 (S-67): the push writes each child AS the caller (MSCRMCallerID).
            Guid.Parse("0000c166-0000-0000-0000-00000000ca11"),
            NullLogger.Instance,
            CancellationToken.None,
            Scopes());

        updated.Should().Be(1);
        failed.Should().Be(0);
        _h.Grants.ActiveRowsOf(project, contact).Should().ContainSingle();
    }
}
