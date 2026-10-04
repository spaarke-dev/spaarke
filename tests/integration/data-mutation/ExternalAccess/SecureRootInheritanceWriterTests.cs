using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 158 (owner round 6) — (1) CREATE and (2) RE-FILE through every BFF writer of a work
/// assignment or project (the inventory, <c>notes/task-158-secure-inherit-filed-records.md</c> §2): each writer, driven
/// through its own entry point with its own write seam applying the write to the provisioning fixture's Dataverse, calls
/// the host's REAL <see cref="SecureRootFilingGate"/> — so a record it files under a secure matter comes out SECURE (flag,
/// named owner team, own container, creator share, read back), one it files under an ordinary matter is left as it is, and
/// one whose parent's flag cannot be read is REFUSED with nothing written.
/// </summary>
/// <remarks>
/// The writers: <c>dataverse.create_record</c> and <c>dataverse.update_record</c> (chat), the playbook output
/// orchestrator's <see cref="DataverseUpdateHandler"/>, the UpdateRecord node / ActionSeam core
/// (<see cref="UpdateRecordActionCore"/>), the field-mapping push, and the Office quick-create of a project
/// (<see cref="RecordCreationService"/>). <c>POST /api/v1/work-assignments</c> is not among them: task 166 deletes it
/// (owner round 10 item 1), and it cannot file a work assignment under a matter at all (it writes a column the table does
/// not have).
/// </remarks>
[Trait("status", "task-158-uac-r2")]
public class SecureRootInheritanceWriterTests : TypedToolHandlerTestFixture, IClassFixture<ProvisionProjectTestFixture>
{
    private readonly ProvisionProjectTestFixture _fixture;
    private readonly SecureRootFilingGate _gate;

    /// <summary>Every write a writer's own seam was asked to make (table, id, fields).</summary>
    private readonly List<(string Table, Guid Id, IReadOnlyDictionary<string, object?> Fields)> _writes = new();

    public SecureRootInheritanceWriterTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _fixture.SystemUsers[AppUser] = (false, true);
        _gate = _fixture.Services.GetRequiredService<SecureRootFilingGate>();
    }

    private SecureChildShareWorld World => _fixture.ChildWorld;

    private void ShouldBeSecure(Guid id, string because)
    {
        _fixture.IsSecureOf(id).Should().BeTrue($"{because}: sprk_issecure");
        _fixture.OwningTeamOf(id).Should().Be(SecureTeam, $"{because}: the named owner team");
        _fixture.ContainerIdOf(id).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId, $"{because}: its own container");
        _fixture.ShareMaskOf(id, Creator).Should().Be(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights),
            $"{because}: the person who created it is shared");
    }

    private void ShouldNotBeSecured(Guid id, string because)
    {
        _fixture.IsSecureOf(id).Should().BeFalse(because);
        _fixture.OwningTeamOf(id).Should().NotBe(SecureTeam, because);
        _fixture.Updates.Should().NotContain(u => u.RecordId == id, because);
        _fixture.SharesOn(id).Should().BeEmpty(because);
    }

    /// <summary>A secure matter (already provisioned) and an ordinary one; returns their ids.</summary>
    private (Guid Secure, Guid Ordinary) Matters()
    {
        var (secure, ordinary) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secure);
        _fixture.SeedMatter(ordinary, isSecure: false);
        return (secure, ordinary);
    }

    /// <summary>The matter's flag reads EMPTY (owner round 17 item 3) — "cannot be read", never "not secure".</summary>
    private void FlagUnreadable(Guid matter) => World.Set("sprk_matter", matter, "sprk_issecure", null);

    // ── The writers' own seams, applying each write to the fixture's Dataverse ──────────────────────────────────────

    /// <summary>
    /// Applies the filing columns of a write — as any writer spells them: <c>sprk_regardingmatter</c>,
    /// <c>sprk_RegardingMatter@odata.bind</c>; an <see cref="EntityReference"/>, a <see cref="Guid"/>, a bind path, a JSON
    /// string — to the row the inheritance reads.
    /// </summary>
    private void ApplyFiling(string table, Guid id, IReadOnlyDictionary<string, object?> fields)
    {
        _writes.Add((table, id, fields));
        foreach (var (key, raw) in fields)
        {
            var column = SecureRootInheritance.NormalizeColumn(key);
            if (!SecureRootInheritance.FilingColumnsOf(table).Contains(column))
                continue;

            var value = raw is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : raw;
            if (column == SecureRootInheritance.PairIdColumn)
            {
                World.Set(table, id, column, value?.ToString());
                continue;
            }

            Guid? target = value switch
            {
                EntityReference reference => reference.Id,
                Guid guid => guid,
                string text when Guid.TryParse(text.Trim().TrimEnd(')').Split('(').Last(), out var parsed) => parsed,
                _ => null,
            };
            World.Set(table, id, column, target is { } t
                ? new EntityReference(column == SecureRootInheritance.PairTypeColumn ? "sprk_recordtype_ref" : "parent", t)
                : null);
        }
    }

    private Mock<IFieldMappingDataverseService> FieldMappingWritingToTheWorld()
    {
        var service = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        service
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((t, id, f, _, _) => ApplyFiling(t, id, f))
            .Returns(Task.CompletedTask);
        return service;
    }

    private static Guid? BoundId(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var raw)
        && (raw is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : raw as string) is { } bind
        && Guid.TryParse(bind.TrimEnd(')').Split('(').Last(), out var id)
            ? id
            : null;

    // ── (1) CREATE: dataverse.create_record ────────────────────────────────────────────────────────────────────────

    private DataverseCreateRecordHandler ChatCreate(IFieldMappingDataverseService appOnly) =>
        new(new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator), CreateLogger<DataverseCreateRecordHandler>(),
            new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new RecordOwnershipResolver(
                SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                NullLogger<RecordOwnershipResolver>.Instance),
            appOnly, IdentityNormalizationFixtures.NoLinkedContact(), _gate);

    /// <summary>The app-only create seam: the row lands in the fixture as the application created it (createdby = app).</summary>
    private Mock<IFieldMappingDataverseService> AppCreatesIntoTheWorld()
    {
        var service = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        service
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                table.Should().Be("sprk_workassignment");
                _fixture.SeedWorkAssignment(id, owningTeamId: BoundId(fields, "ownerid@odata.bind"), isSecure: false,
                    createdBy: AppUser, createdByPerson: BoundId(fields, "sprk_CreatedByPerson@odata.bind"));
                ApplyFiling(table, id, fields);
            })
            .Returns(Task.CompletedTask);
        return service;
    }

    private Task<ToolResult> CreateWorkAssignmentUnder(Guid matter, IFieldMappingDataverseService appOnly) =>
        ChatCreate(appOnly).ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                tablename = "sprk_workassignment",
                item = new Dictionary<string, object>
                {
                    ["sprk_name"] = "Review the lease",
                    ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = matter },
                },
            })) with
            { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

    /// <summary>
    /// AC 1, chat create: a work assignment created under a SECURE matter is created by the application as an ordinary row of
    /// the caller's business unit, then — in the same call — made secure for the person who asked (its
    /// <c>sprk_createdbyperson</c>; createdby is the application): flag, named team, own container, creator share.
    /// </summary>
    [Fact]
    public async Task ChatCreate_AWorkAssignmentUnderASecureMatter_ComesOutSecure()
    {
        var (secure, _) = Matters();

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var created = _writes.Should().ContainSingle().Subject.Id;
        ShouldBeSecure(created, "created under a secure matter");
    }

    /// <summary>
    /// ADR-003, chat create: created under a secure matter, but its securing is refused (the person it would be secured for
    /// is disabled) — never reported as a plain success: the tool answers an error naming the record and the reason.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheNewRecordCannotBeSecured_ReportsItNotSecure()
    {
        var (secure, _) = Matters();
        _fixture.SystemUsers[Creator] = (true, false);

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        var created = _writes.Should().ContainSingle().Subject.Id;
        result.ErrorMessage.Should().Contain(created.ToString("D")).And.Contain("could not be made secure yet");
        _fixture.OwningTeamOf(created).Should().NotBe(SecureTeam, "never moved to the memberless team without a person to see it");
    }

    /// <summary>AC 1 control: under an ORDINARY matter the same create stays an ordinary row of the caller's unit.</summary>
    [Fact]
    public async Task ChatCreate_AWorkAssignmentUnderAnOrdinaryMatter_StaysOrdinary()
    {
        var (_, ordinary) = Matters();

        var result = await CreateWorkAssignmentUnder(ordinary, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ShouldNotBeSecured(_writes.Should().ContainSingle().Subject.Id, "filed under an ordinary matter");
    }

    /// <summary>AC negative, chat create: the matter's flag cannot be read — refused, nothing created.</summary>
    [Fact]
    public async Task ChatCreate_UnderAMatterWhoseFlagCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        FlagUnreadable(secure);

        var result = await CreateWorkAssignmentUnder(secure, new Mock<IFieldMappingDataverseService>(MockBehavior.Strict).Object);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _writes.Should().BeEmpty();
    }

    // ── (1) CREATE: the Office quick-create of a project (filed through the Field Mapping Framework's pair) ──────────

    private readonly List<Entity> _officeCreates = new();

    private RecordCreationService OfficeCreator(Guid sourceMatter, Guid typeRef)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity row, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                _officeCreates.Add(row);
                _fixture.SeedProject(id, owningTeamId: row.GetAttributeValue<EntityReference>("ownerid").Id, isSecure: false,
                    createdBy: AppUser, createdByPerson: row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id);
                ApplyFiling("sprk_project", id, row.Attributes.ToDictionary(a => a.Key, a => (object?)a.Value));
                return Task.FromResult(id);
            });
        entities
            .Setup(e => e.RetrieveAsync("sprk_matter", sourceMatter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", sourceMatter)
            {
                ["sprk_matterid"] = sourceMatter,
                ["sprk_recordtype"] = new EntityReference("sprk_recordtype_ref", typeRef),
            });

        // The maker's mapping profile files the new project under the matter it is created from (the polymorphic pair).
        var fieldMappings = new Mock<IFieldMappingDataverseService>();
        fieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_matter", "sprk_project", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "matter to project",
                SourceEntity = "sprk_matter",
                TargetEntity = "sprk_project",
                IsActive = true,
                Rules =
                [
                    new FieldMappingRuleEntity
                    {
                        Id = Guid.NewGuid(), Name = "regarding id", SourceField = "sprk_matterid", SourceFieldType = 0,
                        TargetField = "sprk_regardingrecordid", TargetFieldType = 0, MappingType = 0, ExecutionOrder = 1,
                        IsActive = true,
                    },
                    new FieldMappingRuleEntity
                    {
                        Id = Guid.NewGuid(), Name = "regarding type", SourceField = "sprk_recordtype", SourceFieldType = 1,
                        TargetField = "sprk_regardingrecordtype", TargetFieldType = 1, MappingType = 0, ExecutionOrder = 2,
                        IsActive = true,
                    },
                ],
            });

        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SecureChildShareWorld.GeneralTeam);
        return new RecordCreationService(entities.Object, fieldMappings.Object, ownership.Object,
            IdentityNormalizationFixtures.NoLinkedContact(), _gate, NullLogger<RecordCreationService>.Instance);
    }

    private Task<RecordCreationResult> OfficeCreateProjectFrom(Guid matter) =>
        OfficeCreator(matter, RecordTypeRef(_fixture, "sprk_matter")).CreateAsync(new RecordCreationRequest
        {
            EntityType = QuickCreateEntityType.Project,
            Name = "Lease review",
            CallerUserId = "oid",
            OwnerSystemUserId = Creator.ToString("D"),
            SourceEntityLogicalName = "sprk_matter",
            SourceRecordId = matter,
        });

    /// <summary>AC 1, Office quick-create: a project the mapping files under a SECURE matter comes out secure.</summary>
    [Fact]
    public async Task OfficeCreate_AProjectFiledUnderASecureMatter_ComesOutSecure()
    {
        var (secure, _) = Matters();

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeTrue(result.Failure?.Detail);
        result.Warnings.Should().NotContain(w => w.Contains("could not be made secure"));
        ShouldBeSecure(result.RecordId, "filed under a secure matter by the mapping's pair");
    }

    /// <summary>ADR-003, Office quick-create: a project created under a secure matter that cannot be secured is reported.</summary>
    [Fact]
    public async Task OfficeCreate_WhenTheNewProjectCannotBeSecured_WarnsThatItIsNotSecureYet()
    {
        var (secure, _) = Matters();
        _fixture.SystemUsers[Creator] = (true, false);

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeTrue("the project was created");
        result.Warnings.Should().Contain(w => w.Contains("could not be made secure yet"));
        _fixture.IsSecureOf(result.RecordId).Should().BeFalse();
    }

    /// <summary>AC negative, Office quick-create: the matter's flag cannot be read — refused, nothing created.</summary>
    [Fact]
    public async Task OfficeCreate_UnderAMatterWhoseFlagCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        FlagUnreadable(secure);

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Code.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _officeCreates.Should().BeEmpty();
    }

    // ── (2) RE-FILE: dataverse.update_record ────────────────────────────────────────────────────────────────────────

    /// <summary>The caller's Dataverse, scripted, whose PATCH lands in the fixture's Dataverse.</summary>
    private sealed class PatchingUserClient(IDataverseUserClient inner, Action<string, string> onPatch) : IDataverseUserClient
    {
        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) =>
            inner.GetAsync(relativePath, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(
            string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            onPatch(relativePath, jsonBody);
            return inner.PatchAsync(relativePath, jsonBody, cancellationToken);
        }

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            inner.DeleteAsync(relativePath, cancellationToken);
    }

    private Task<ToolResult> ChatRefile(Guid workAssignment, Guid matter)
    {
        var user = new PatchingUserClient(new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator), (path, body) =>
        {
            using var doc = JsonDocument.Parse(body);
            ApplyFiling("sprk_workassignment", workAssignment,
                doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()));
        });
        return new DataverseUpdateRecordHandler(
                user, new StampWorld().AfterWriteRestamp, CreateLogger<DataverseUpdateRecordHandler>(),
                new RecordOwnershipResolverDouble(), _gate)
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
                {
                    tablename = "sprk_workassignment",
                    recordId = workAssignment,
                    item = new Dictionary<string, object>
                    {
                        ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = matter },
                    },
                })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseUpdateRecordHandler)), CancellationToken.None);
    }

    /// <summary>AC 2, chat update: re-filing an ordinary work assignment under a SECURE matter secures it.</summary>
    [Fact]
    public async Task ChatUpdate_RefilingUnderASecureMatter_SecuresIt()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ShouldBeSecure(workAssignment, "re-filed under a secure matter");
    }

    /// <summary>ADR-003, chat update: the re-file stands but its securing is refused — reported as an error, not a success.</summary>
    [Fact]
    public async Task ChatUpdate_WhenTheRefiledRecordCannotBeSecured_ReportsItNotSecure()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.SystemUsers[Creator] = (true, false);

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("could not be made secure yet");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
    }

    /// <summary>AC negative, chat update: the matter's flag cannot be read — refused, the caller's PATCH never sent.</summary>
    [Fact]
    public async Task ChatUpdate_RefilingUnderAMatterWhoseFlagCannotBeRead_IsRefused_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        FlagUnreadable(secure);

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
    }

    // ── (2) RE-FILE: the playbook output orchestrator's DataverseUpdateHandler ─────────────────────────────────────────

    private Task RefileThroughTheUpdateHandler(Guid workAssignment, Guid matter) =>
        new DataverseUpdateHandler(
                FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new RecordOwnershipResolverDouble(),
                new StampWorld().Restamper, _gate, NullLogger<DataverseUpdateHandler>.Instance)
            .UpdateAsync(
                "sprk_workassignment", workAssignment,
                new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) },
                ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

    /// <summary>
    /// AC 2, the generic writer: re-filing under a SECURE matter secures the work assignment; re-filing another under an
    /// ORDINARY matter leaves it exactly as it was.
    /// </summary>
    [Fact]
    public async Task UpdateHandler_RefilingUnderASecureMatter_SecuresIt_AndUnderAnOrdinaryOne_LeavesItUnchanged()
    {
        var (secure, ordinary) = Matters();
        var (toSecure, toOrdinary) = (Guid.NewGuid(), Guid.NewGuid());
        FiledWorkAssignment(_fixture, toSecure, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.SeedWorkAssignment(toOrdinary, isSecure: false);

        await RefileThroughTheUpdateHandler(toSecure, secure);
        await RefileThroughTheUpdateHandler(toOrdinary, ordinary);

        ShouldBeSecure(toSecure, "re-filed under a secure matter");
        ShouldNotBeSecured(toOrdinary, "re-filed under an ordinary matter");
        _fixture.OwningUserOf(toOrdinary).Should().Be(Creator, "unchanged");
    }

    /// <summary>AC negative, the generic writer: the matter's flag cannot be read — it throws, nothing written.</summary>
    [Fact]
    public async Task UpdateHandler_RefilingUnderAMatterWhoseFlagCannotBeRead_Throws_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        FlagUnreadable(secure);

        var act = () => RefileThroughTheUpdateHandler(workAssignment, secure);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// Fail closed on a value the gate cannot interpret (neither a reference, an id nor a bind path): refused, nothing
    /// written — never read as "filed under nothing".
    /// </summary>
    [Fact]
    public async Task UpdateHandler_AFilingValueThatCannotBeInterpreted_IsRefused()
    {
        var workAssignment = Guid.NewGuid();
        _fixture.SeedWorkAssignment(workAssignment, isSecure: false);
        var handler = new DataverseUpdateHandler(
            FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new RecordOwnershipResolverDouble(),
            new StampWorld().Restamper, _gate, NullLogger<DataverseUpdateHandler>.Instance);

        var act = () => handler.UpdateAsync("sprk_workassignment", workAssignment,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = 42 }, ConcurrencyMode.None, 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// Fail closed in a composition WITHOUT the inheritance (§10 F.1's twin): the gate refuses a filing write of a work
    /// assignment, and the UpdateRecord core and the push refuse one when the gate itself is absent — nothing written. A
    /// write that files nothing is unaffected.
    /// </summary>
    [Fact]
    public async Task WithoutTheInheritance_EveryWriterRefusesAFilingWrite_ButNotAnOrdinaryOne()
    {
        var workAssignment = Guid.NewGuid();
        _fixture.SeedWorkAssignment(workAssignment, isSecure: false);
        var matter = Guid.NewGuid();
        var filing = new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) };

        var handler = new DataverseUpdateHandler(
            FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new RecordOwnershipResolverDouble(),
            new StampWorld().Restamper, SecureRootFilingGateFixtures.Unregistered(), NullLogger<DataverseUpdateHandler>.Instance);
        var viaHandler = () => handler.UpdateAsync("sprk_workassignment", workAssignment, filing, ConcurrencyMode.None, 1, CancellationToken.None);
        await viaHandler.Should().ThrowAsync<RecordOwnerUnresolvedException>();

        var noGate = new ServiceCollection()
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .BuildServiceProvider();
        var core = new UpdateRecordActionCore(
            FieldMappingWritingToTheWorld().Object, noGate.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);
        var viaCore = () => core.UpdateAsync(
            new UpdateRecordActionInput("sprk_workassignment", workAssignment, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", matter.ToString())]),
            CancellationToken.None);
        await viaCore.Should().ThrowAsync<RecordOwnerUnresolvedException>();

        var (updated, failed, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            FieldMappingWritingToTheWorld().Object, new StampWorld().Restamper,
            [
                new FieldMappingRuleDto
                {
                    SourceField = "sprk_regardingmatter", TargetField = "sprk_regardingmatter",
                    SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
                },
            ],
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = matter }, "sprk_workassignment", [workAssignment],
            NullLogger.Instance, CancellationToken.None, rootFiling: null);
        (updated, failed).Should().Be((0, 1));
        _writes.Should().BeEmpty("no writer wrote a filing it could not check");

        await handler.UpdateAsync("sprk_workassignment", workAssignment,
            new Dictionary<string, object?> { ["sprk_name"] = "Renamed" }, ConcurrencyMode.None, 1, CancellationToken.None);
        _writes.Should().ContainSingle("a write that files nothing needs no check");
    }

    // ── (2) RE-FILE: the UpdateRecord node / ActionSeam core ────────────────────────────────────────────────────────

    private Task RefileThroughTheActionCore(Guid workAssignment, Guid matter)
    {
        var services = new ServiceCollection()
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .AddSingleton(_gate)
            .BuildServiceProvider();
        return new UpdateRecordActionCore(
                FieldMappingWritingToTheWorld().Object, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance)
            .UpdateAsync(
                new UpdateRecordActionInput(
                    "sprk_workassignment", workAssignment, FieldMappings: null, LegacyFields: null,
                    Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", matter.ToString())]),
                CancellationToken.None);
    }

    /// <summary>AC 2, the UpdateRecord node: re-filing under a SECURE matter (a bind path) secures the work assignment.</summary>
    [Fact]
    public async Task ActionCore_RefilingUnderASecureMatter_SecuresIt()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);

        await RefileThroughTheActionCore(workAssignment, secure);

        ShouldBeSecure(workAssignment, "re-filed under a secure matter by an UpdateRecord node");
    }

    /// <summary>AC negative, the UpdateRecord node: the matter's flag cannot be read — it throws, nothing written.</summary>
    [Fact]
    public async Task ActionCore_RefilingUnderAMatterWhoseFlagCannotBeRead_Throws_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        FlagUnreadable(secure);

        var act = () => RefileThroughTheActionCore(workAssignment, secure);

        await act.Should().ThrowAsync<RecordOwnerUnresolvedException>();
        _writes.Should().BeEmpty();
    }

    // ── (2) RE-FILE: the field-mapping push ─────────────────────────────────────────────────────────────────────────

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)>
        PushMatterOnto(Guid workAssignment, Guid matter) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            FieldMappingWritingToTheWorld().Object,
            new StampWorld().Restamper,
            [
                new FieldMappingRuleDto
                {
                    SourceField = "sprk_regardingmatter", TargetField = "sprk_regardingmatter",
                    SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
                },
            ],
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = matter },
            "sprk_workassignment",
            [workAssignment],
            NullLogger.Instance,
            CancellationToken.None,
            _gate);

    /// <summary>AC 2, the field-mapping push: a push that files the work assignment under a SECURE matter secures it.</summary>
    [Fact]
    public async Task FieldMappingPush_FilingUnderASecureMatter_SecuresIt()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);

        var (updated, failed, _, _, _) = await PushMatterOnto(workAssignment, secure);

        (updated, failed).Should().Be((1, 0));
        ShouldBeSecure(workAssignment, "pushed under a secure matter");
    }

    /// <summary>AC negative, the push: the matter's flag cannot be read — that record fails, nothing written.</summary>
    [Fact]
    public async Task FieldMappingPush_UnderAMatterWhoseFlagCannotBeRead_FailsThatRecord_AndWritesNothing()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        FlagUnreadable(secure);

        var (updated, failed, _, errors, _) = await PushMatterOnto(workAssignment, secure);

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle(e => e.RecordId == workAssignment && e.Error.Contains(RecordOwnerRefusal.ParentUndetermined));
        _writes.Should().BeEmpty();
    }
}
