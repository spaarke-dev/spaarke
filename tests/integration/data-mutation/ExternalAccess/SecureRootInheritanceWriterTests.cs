using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 158 (owner rounds 6 and 31) — (1) CREATE and (2) RE-FILE through every BFF writer of a work
/// assignment or project (the inventory, <c>notes/task-158-secure-inherit-filed-records.md</c> §2): each writer, driven
/// through its own entry point with its own write seam applying the write to the provisioning fixture's Dataverse, calls
/// the host's REAL <see cref="SecureRootFilingGate"/> — so a record it CREATES under a secure matter is created INTO
/// isolation (named team and flag in the create itself — no business-unit-visible window) and comes out SECURE (flag, named
/// owner team, own container, creator share, read back); one it files under an ordinary matter is left as it is; one whose
/// parent's flag cannot be read, or whose creator is walled off the record or the secure parent, is REFUSED with nothing
/// written; and a created row whose creator cannot be shared is removed again.
/// </summary>
/// <remarks>
/// The writers: <c>dataverse.create_record</c> and <c>dataverse.update_record</c> (chat), the playbook output
/// orchestrator's <see cref="DataverseUpdateHandler"/>, the UpdateRecord node / ActionSeam core
/// (<see cref="UpdateRecordActionCore"/>) and the field-mapping push. The Office quick-create of a project is the unified
/// create since field mapping task 018 (018c deleted its own creation core), pinned over its real route in
/// <c>SecureRootCreateRouteTests</c>. <c>POST /api/v1/work-assignments</c> is not among them: task 166 deletes it
/// (owner round 10 item 1), and it cannot file a work assignment under a matter at all (it writes a column the table does
/// not have).
/// </remarks>
[Trait("status", "task-158-uac-r2")]
public partial class SecureRootInheritanceWriterTests : TypedToolHandlerTestFixture, IClassFixture<ProvisionProjectTestFixture>
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

            var value = raw switch
            {
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
                JsonElement { ValueKind: JsonValueKind.Null } => null,
                _ => raw,
            };
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

    /// <summary>The caller's scripted Dataverse, reading the ADR-024 record-type catalog from this fixture's own rows.</summary>
    private SecureChildOwnershipAiToolTests.ScriptedUserClient Scripted() =>
        new(Creator) { RecordTypeOf = id => World.ValueOf<string>("sprk_recordtype_ref", id, "sprk_recordlogicalname") };

    private DataverseCreateRecordHandler ChatCreate(IFieldMappingDataverseService appOnly) =>
        new(Scripted(), CreateLogger<DataverseCreateRecordHandler>(),
            new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new RecordOwnershipResolver(
                SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                NullLogger<RecordOwnershipResolver>.Instance),
            appOnly, IdentityNormalizationFixtures.NoLinkedContact(), _gate);

    /// <summary>
    /// The app-only create seam: the row lands in the fixture as the application created it (createdby = app).
    /// <paramref name="afterCreate"/> (task 158 r1c-v1): what changes right after the create — between the plan and the
    /// provisioning that completes it (a race the plan's own checks cannot close).
    /// </summary>
    private Mock<IFieldMappingDataverseService> AppCreatesIntoTheWorld(Action<Guid>? afterCreate = null)
    {
        var service = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        service
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                table.Should().BeOneOf("sprk_workassignment", "sprk_project");
                var isSecure = fields.TryGetValue("sprk_issecure", out var flag) && flag is true;
                if (table == "sprk_project")
                {
                    _fixture.SeedProject(id, owningTeamId: BoundId(fields, "ownerid@odata.bind"), isSecure: isSecure,
                        createdBy: AppUser, createdByPerson: BoundId(fields, "sprk_CreatedByPerson@odata.bind"));
                }
                else
                {
                    _fixture.SeedWorkAssignment(id, owningTeamId: BoundId(fields, "ownerid@odata.bind"), isSecure: isSecure,
                        createdBy: AppUser, createdByPerson: BoundId(fields, "sprk_CreatedByPerson@odata.bind"));
                }
                ApplyFiling(table, id, fields);
                afterCreate?.Invoke(id);
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
    /// AC 1 + owner round 31 item 2, chat create: a work assignment created under a SECURE matter is created INTO isolation —
    /// the application's create itself names the Secure Record Owners team and carries <c>sprk_issecure = true</c>, with the
    /// caller as <c>sprk_createdbyperson</c> (createdby is the application) — so there is never a business-unit-visible row
    /// and no owner MOVE afterwards; then it comes out secure: flag, named team, own container, creator share.
    /// </summary>
    [Fact]
    public async Task ChatCreate_AWorkAssignmentUnderASecureMatter_IsCreatedIntoIsolation_AndComesOutSecure()
    {
        var (secure, _) = Matters();

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam, "created owned by the named team — never the caller's unit first");
        fields["sprk_issecure"].Should().Be(true, "flagged in the create itself");
        BoundId(fields, "sprk_CreatedByPerson@odata.bind").Should().Be(Creator);
        _fixture.Updates.Should().NotContain(u => u.RecordId == created && u.Payload.ContainsKey("ownerid@odata.bind"),
            "no owner move: there was never a business-unit-visible window to close");
        ShouldBeSecure(created, "created under a secure matter");
    }

    /// <summary>
    /// Owner round 31 item 2: the creator's share is added and READ BACK before anything else; when it cannot be made the
    /// just-created row is DELETED (read back gone) and the create is refused — never a row nobody can open, never a
    /// business-unit-visible one.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheCreatorCannotBeShared_TheIsolatedRowIsRemoved_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("NOT created").And.Contain("removed again");
        var created = _writes.Should().ContainSingle().Subject.Id;
        World.Has("sprk_workassignment", created).Should().BeFalse("the row was deleted again");
        _fixture.IsSecureOf(created).Should().BeNull("read back gone");
        World.Deletes.Should().ContainSingle(d => d.Id == created);
    }

    /// <summary>
    /// Owner round 31 item 2, the compensation's own failure: the creator could not be shared AND the row could not be
    /// deleted again — the tool says exactly that (never "shared to you"), the row stays secure and team-owned (never a
    /// business-unit-visible row), and the job's re-entry shares it to its creator and completes it.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheCreatorCannotBeSharedNorTheRowRemoved_SaysSo_AndTheJobSharesItToTheCreator()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;
        World.DeletesFail = true;

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("could not be shared to you and could not be removed").And.NotContain("as a secure record shared to you")
            .And.Contain("shared to you automatically once that step succeeds", "a fault: the job's retry does share it (verifier item 7)");
        var created = _writes.Should().ContainSingle().Subject.Id;
        World.Has("sprk_workassignment", created).Should().BeTrue();
        _fixture.IsSecureOf(created).Should().BeTrue("still secure — never a business-unit-visible row");
        _fixture.OwningTeamOf(created).Should().Be(SecureTeam);
        _fixture.ShareMaskOf(created, Creator).Should().Be(0);

        _fixture.FailShareForPrincipal = null;
        World.DeletesFail = false;
        (await new SecureRootInheritanceJobRunner(_fixture).RunAsync()).Success.Should().BeTrue();
        ShouldBeSecure(created, "the job's re-entry shares it to the person who created it");
    }

    /// <summary>
    /// Owner round 31 item 2: a creator share that WAS made, with a later step that did not complete (here the container),
    /// leaves a PROVISIONED-but-incomplete record — secure, shared to its creator, never removed — which the existing
    /// re-entry branch (the job) completes.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheContainerCannotBeCreated_TheRecordStaysSecureForItsCreator_AndTheJobCompletesIt()
    {
        var (secure, _) = Matters();
        _fixture.SpeContainerCreationSucceeds = false;

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse("never reported as a plain success");
        result.ErrorMessage.Should().Contain("shared to you").And.Contain("completed automatically");
        var created = _writes.Should().ContainSingle().Subject.Id;
        _fixture.IsSecureOf(created).Should().BeTrue();
        _fixture.OwningTeamOf(created).Should().Be(SecureTeam);
        _fixture.ShareMaskOf(created, Creator).Should().Be(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights));
        World.Deletes.Should().BeEmpty("a record its creator can open is completed, never removed");

        _fixture.SpeContainerCreationSucceeds = true;
        (await new SecureRootInheritanceJobRunner(_fixture).RunAsync()).Success.Should().BeTrue();
        ShouldBeSecure(created, "the job completes it through the re-entry branch");
    }

    /// <summary>
    /// Owner round 31 item 1, chat create: the caller is on the SECURE MATTER's No Access list — refused before any write
    /// (<c>sdap.provision.creator_no_access</c>), nothing created.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheCallerIsOnTheSecureMattersNoAccessList_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        result.ErrorMessage.Should().Contain("NOT created")
            .And.Contain("the No Access list of the secure matter it would be filed under", "whose list refused (r1c-v2)");
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 31 item 1, chat create: the record's OWN No Access list, before it exists — the caller is walled off an
    /// organization the new work assignment would reference — refused before any write, nothing created.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheCallerIsWalledOffAnOrganizationTheRecordWouldReference_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        var lawFirm = Guid.NewGuid();
        _fixture.NoAccessList.DenySystemUserOnOrganization(Creator, lawFirm);

        var result = await ChatCreate(AppCreatesIntoTheWorld().Object).ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                tablename = "sprk_workassignment",
                item = new Dictionary<string, object>
                {
                    ["sprk_name"] = "Review the lease",
                    ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = secure },
                    ["sprk_assignedlawfirm1"] = new { relatedTable = "sprk_organization", recordId = lawFirm },
                },
            })) with
            { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// G5 for a parent named by the POLYMORPHIC PAIR (text — no lookup the mapper checks): the caller's AppendTo on the secure
    /// matter is asked AS THE CALLER before the isolated create; without it, denied, nothing created. #1616: on a PROJECT —
    /// the table whose pair files it on its own (a work assignment's pair-only filing is refused as invalid, below).
    /// </summary>
    [Fact]
    public async Task ChatCreate_AProjectUnderASecureMatterByThePair_WithoutAppendToOnTheMatter_IsDenied_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        var user = Scripted();
        user.NoAppendTo.Add(secure);
        var handler = new DataverseCreateRecordHandler(user, CreateLogger<DataverseCreateRecordHandler>(),
            new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
            new RecordOwnershipResolver(
                SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                NullLogger<RecordOwnershipResolver>.Instance),
            AppCreatesIntoTheWorld().Object, IdentityNormalizationFixtures.NoLinkedContact(), _gate);

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                tablename = "sprk_project",
                item = new Dictionary<string, object>
                {
                    ["sprk_name"] = "Review the lease",
                    ["sprk_regardingrecordid"] = secure.ToString("D"),
                    ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
                },
            })) with
            { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _writes.Should().BeEmpty();
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

    /// <summary>
    /// Owner round 31 item 2: the named Secure Record Owners team cannot be resolved in this environment (no Secure Record
    /// business unit answers) — a create that would be made INTO isolation is refused (<c>secure_owner_team_unresolved</c>),
    /// nothing created: never an isolated row owned by nobody, never an ordinary row instead.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheSecureOwnerTeamCannotBeResolved_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _fixture.SecureBuMatchCount = 0;

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(RecordOwnerRefusal.SecureOwnerTeamUnresolved);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// The plan never creates INTO isolation for nobody: asked for a create under a secure matter with no person to secure
    /// it for, it refuses (<c>creator_unresolved</c>) — the gate every writer calls, asked directly.
    /// </summary>
    [Fact]
    public async Task ThePlan_ForACreateUnderASecureMatterForNobody_Refuses()
    {
        var (secure, _) = Matters();

        var plan = await _gate.PlanCreateAsync("sprk_workassignment",
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", secure) },
            Guid.Empty, CancellationToken.None);

        plan.Isolated.Should().BeFalse();
        plan.Refusal.Should().NotBeNull();
        plan.Refusal!.RefusalCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorUnresolved);
    }

    /// <summary>
    /// Owner round 31 item 1, chat create: whether the caller is on the secure matter's No Access list cannot be checked (the
    /// list cannot be read) — refused (<c>creator_no_access_unverifiable</c>), nothing created (ADR-003).
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheNoAccessListCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _fixture.NoAccessList.Faults = true;

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 31 item 1, chat create: filed under a SECURE matter AND under a project whose flag reads EMPTY (both typed
    /// lookups — since #1616 a work assignment's pair names a parent only by repeating one) — secure-if-any makes the record
    /// secure, but the caller cannot be checked against the unreadable project's No Access list, so the create is refused
    /// (<c>creator_no_access_unverifiable</c>) with nothing written.
    /// </summary>
    [Fact]
    public async Task ChatCreate_UnderASecureMatterAndAProjectWhoseFlagCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        var other = Guid.NewGuid();
        _fixture.SeedProject(other, isSecure: false);
        World.Set("sprk_project", other, "sprk_issecure", null);

        var result = await ChatCreate(AppCreatesIntoTheWorld().Object).ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                tablename = "sprk_workassignment",
                item = new Dictionary<string, object>
                {
                    ["sprk_name"] = "Review the lease",
                    ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = secure },
                    ["sprk_regardingproject"] = new { relatedTable = "sprk_project", recordId = other },
                },
            })) with
            { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 31 item 2, the two decisions disagreeing: the ownership resolver names the Secure Record team for a work
    /// assignment the plan found filed under NO secure record (what it names changed between the two reads) — nothing is
    /// created (fail closed): never an ordinary row re-owned to the caller's unit, never a team-owned row nobody provisions.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheResolverNamesTheSecureTeamButThePlanFoundNoSecureParent_NothingIsCreated()
    {
        var (_, ordinary) = Matters();
        var resolver = new Mock<IRecordOwnershipResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Owned(SecureTeam) with { IsSecureOwner = true });
        var handler = new DataverseCreateRecordHandler(
            Scripted(), CreateLogger<DataverseCreateRecordHandler>(),
            new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"), resolver.Object,
            AppCreatesIntoTheWorld().Object, IdentityNormalizationFixtures.NoLinkedContact(), _gate);

        var result = await handler.ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                tablename = "sprk_workassignment",
                item = new Dictionary<string, object>
                {
                    ["sprk_name"] = "Review the lease",
                    ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = ordinary },
                },
            })) with
            { UserId = Guid.NewGuid().ToString() },
            BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("NOT created");
        _writes.Should().BeEmpty();
    }

    // ── (1) CREATE: the Office quick-create of a project ───────────────────────────────────────────────────────────
    //
    // Field mapping task 018c deleted the Office creation core these tests drove. The Office quick-create is the unified
    // RecordCreationService behind OfficeService's adapter, and its secure-root behaviour is pinned over the real route in
    // SecureRootCreateRouteTests (and its OfficeSecureOwner and OfficeFiling parts), OfficeQuickCreateAdapterContractTests
    // and the RecordCreation seam tests. The per-test disposition is in
    // projects/field-mapping-server-write-path-r1/notes/task-018-notes.md, section "018c".

    // ══ Task 158 r1c-v1 — the verifier's items 2 and 7 ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifier item 2 (round 31 item 2: "deletes the just-created row (READ BACK)"): the compensation delete ANSWERS success
    /// but the row survives. The read-back finds it, so the create is never reported as removed / not created: the tool says
    /// the row exists and could not be shared or removed, it stays secure and team-owned, and the job shares it later.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheCompensationDeleteAnswersSuccessButTheRowSurvives_SaysSo_NeverNotCreated()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;
        World.DeletesIgnored = true;

        var result = await CreateWorkAssignmentUnder(secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("could not be shared to you and could not be removed").And.NotContain("NOT created");
        var created = _writes.Should().ContainSingle().Subject.Id;
        World.Deletes.Should().ContainSingle(d => d.Id == created, "the delete was asked, and answered success");
        World.Has("sprk_workassignment", created).Should().BeTrue("yet the row survived — only the read-back tells");
        _fixture.IsSecureOf(created).Should().BeTrue("still secure and team-owned: never a business-unit-visible row");
        _fixture.OwningTeamOf(created).Should().Be(SecureTeam);

        _fixture.FailShareForPrincipal = null;
        World.DeletesIgnored = false;
        (await new SecureRootInheritanceJobRunner(_fixture).RunAsync()).Success.Should().BeTrue();
        ShouldBeSecure(created, "the job's re-entry shares it to the person who created it");
    }

    /// <summary>
    /// Verifier item 7: the creator is walled off the secure matter BETWEEN the create's plan and its provisioning (a race the
    /// plan's own check cannot close). Provisioning refuses the creator and the row cannot be removed. Every job run refuses it
    /// again — so the tool must NOT promise that it is shared automatically: it says an administrator must act.
    /// </summary>
    [Fact]
    public async Task ChatCreate_WhenTheCreatorIsWalledOffAfterThePlan_AndTheRowCannotBeRemoved_PromisesNoSelfHeal()
    {
        var (secure, _) = Matters();
        World.DeletesFail = true;

        var result = await CreateWorkAssignmentUnder(secure,
            AppCreatesIntoTheWorld(afterCreate: _ => _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure)).Object);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("could not be shared to you and could not be removed")
            .And.Contain("will not be shared to you automatically").And.Contain("administrator")
            .And.NotContain("automatically once");
        var created = _writes.Should().ContainSingle().Subject.Id;
        _fixture.ShareMaskOf(created, Creator).Should().Be(0);

        World.DeletesFail = false;
        var run = await new SecureRootInheritanceJobRunner(_fixture).RunAsync();
        run.Success.Should().BeFalse("the job refuses the walled creator on every run: no self-heal");
        _fixture.ShareMaskOf(created, Creator).Should().Be(0);
    }

    // ── (2) RE-FILE: dataverse.update_record ────────────────────────────────────────────────────────────────────────

    /// <summary>The caller's Dataverse, scripted, whose PATCH lands in the fixture's Dataverse.</summary>
    private sealed class PatchingUserClient(
        IDataverseUserClient inner, Action<string, string> onPatch, Action<string>? afterGet = null) : IDataverseUserClient
    {
        public async Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            var response = await inner.GetAsync(relativePath, cancellationToken);
            afterGet?.Invoke(relativePath);
            return response;
        }

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

    /// <param name="afterPatch">Task 158 r1c-v1: what changes right after the caller's PATCH — between the re-file's pre-check
    /// and the securing that follows it.</param>
    private Task<ToolResult> ChatRefile(Guid workAssignment, Guid matter, Action? afterPatch = null) =>
        ChatRefileWith(workAssignment, Scripted(),
            new Dictionary<string, object> { ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = matter } },
            afterPatch);

    private Task<ToolResult> ChatRefileWith(
        Guid workAssignment, SecureChildOwnershipAiToolTests.ScriptedUserClient scripted, Dictionary<string, object> item,
        Action? afterPatch = null) =>
        ChatUpdateWith("sprk_workassignment", workAssignment, scripted, item, afterPatch);

    /// <param name="appOnly">#1616: the application's update-only writer the update tool resolves from its scope (the
    /// BFF-managed pair of a project); <c>null</c> builds the tool without a scope, as before.</param>
    private Task<ToolResult> ChatUpdateWith(
        string table, Guid recordId, SecureChildOwnershipAiToolTests.ScriptedUserClient scripted, Dictionary<string, object> item,
        Action? afterPatch = null, IFieldMappingDataverseService? appOnly = null, Action<string>? afterGet = null)
    {
        var user = new PatchingUserClient(scripted, (path, body) =>
        {
            using var doc = JsonDocument.Parse(body);
            ApplyFiling(table, recordId,
                doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()));
            afterPatch?.Invoke();
        }, afterGet);
        var scopes = appOnly is null
            ? null
            : new ServiceCollection().AddSingleton(appOnly).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new DataverseUpdateRecordHandler(
                user, new StampWorld().AfterWriteRestamp, CreateLogger<DataverseUpdateRecordHandler>(),
                new RecordOwnershipResolverDouble(), _gate, scopes)
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
                {
                    tablename = table,
                    recordId,
                    item,
                })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseUpdateRecordHandler)), CancellationToken.None);
    }

    /// <summary>
    /// Verifier item 7, the re-file: securing a re-filed work assignment did not finish because of a FAULT (its container
    /// could not be created) — the job retries it, and the tool says so.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_WhenSecuringTheReFiledRecordFaults_SaysItIsRetriedAutomatically()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.SpeContainerCreationSucceeds = false;

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("could not be made secure yet").And.Contain("retried automatically");
    }

    /// <summary>
    /// Verifier item 7, the re-file: the creator is walled off the secure matter BETWEEN the re-file's pre-check and the
    /// securing (a race the pre-check cannot close). Provisioning refuses on every run, so the tool must not promise a retry
    /// that cannot succeed: an administrator needs to review it.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_WhenTheCreatorIsWalledOffAfterThePreCheck_PromisesNoAutomaticRetry()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);

        var result = await ChatRefile(workAssignment, secure,
            afterPatch: () => _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("could not be made secure yet").And.Contain("administrator")
            .And.NotContain("retried automatically");
        _fixture.ShareMaskOf(workAssignment, Creator).Should().Be(0);
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

    /// <summary>
    /// Owner round 31 item 1, chat update: the person the re-filed record would be secured for cannot be named (its creator's
    /// user is disabled) — refused BEFORE the caller's PATCH, never a re-filed record left unsecured.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_WhenTheRecordsCreatorCannotBeNamed_IsRefusedBeforeThePatch()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.SystemUsers[Creator] = (true, false);

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonResumeCreatorUnavailable);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
    }

    /// <summary>
    /// Owner round 31 item 1, chat update: the record's creator is on the No Access list of the SECURE MATTER it would be
    /// filed under — refused before the PATCH (<c>creator_no_access</c>), nothing written.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_WhenTheCreatorIsOnTheSecureMattersNoAccessList_IsRefusedBeforeThePatch()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        result.ErrorMessage.Should().Contain("the No Access list of the secure matter it would be filed under");
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
        _fixture.SharesOn(workAssignment).Should().BeEmpty();
    }

    /// <summary>
    /// Owner round 31 item 1, chat update: the record's creator is on the WORK ASSIGNMENT's own No Access list — it reads
    /// unflagged, but it is asked about as the secure record it would become — refused before the PATCH, nothing written.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_WhenTheCreatorIsOnTheRecordsOwnNoAccessList_IsRefusedBeforeThePatch()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.NoAccessReads.Flags[workAssignment] = new Sprk.Bff.Api.Infrastructure.ExternalAccess.RootRecordFlags(IsSecure: false, IsRestricted: false);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, workAssignment);

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
    }

    /// <summary>
    /// Owner round 31 item 1, chat update: the re-file files the record under a SECURE matter while its typed PROJECT is a
    /// record whose flag reads EMPTY — it would be secured (secure-if-any), but its creator cannot be checked against the
    /// unreadable project's No Access list: refused before the PATCH (<c>creator_no_access_unverifiable</c>). (#1616 fix
    /// round: on a typed filing — a pair no typed lookup repeats names nothing the walls are asked about, see
    /// <see cref="ChatUpdate_ReFilingByTheTypedLookupWhileThePairStillNamesTheOldMatter_StillHonoursTheSecureMattersNoAccessList"/>.)
    /// </summary>
    [Fact]
    public async Task ChatUpdate_UnderASecureMatterWhileItsTypedProjectsFlagCannotBeRead_IsRefusedBeforeThePatch()
    {
        var (secure, ordinary) = Matters();
        var unreadable = Guid.NewGuid();
        _fixture.SeedProject(unreadable, isSecure: false);
        World.Set("sprk_project", unreadable, "sprk_issecure", null);
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        World.Set("sprk_workassignment", workAssignment, "sprk_regardingproject", new EntityReference("sprk_project", unreadable));

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
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

    // ── The ADR-024 pair is AppendTo-checked AS THE CALLER like any lookup (ontology-platform-r1 task 046's review) ───────

    /// <summary>A <paramref name="table"/> row filed ONLY by the pair (no typed regarding lookup) under <paramref name="matter"/>.</summary>
    private Task<ToolResult> CreateByThePair(
        string table, SecureChildOwnershipAiToolTests.ScriptedUserClient user, Guid matter, IFieldMappingDataverseService appOnly) =>
        new DataverseCreateRecordHandler(user, CreateLogger<DataverseCreateRecordHandler>(),
                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
                new RecordOwnershipResolver(
                    SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                    NullLogger<RecordOwnershipResolver>.Instance),
                appOnly, IdentityNormalizationFixtures.NoLinkedContact(), _gate)
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
                {
                    tablename = table,
                    item = new Dictionary<string, object>
                    {
                        ["sprk_name"] = "Review the lease",
                        ["sprk_regardingrecordid"] = matter.ToString("D"),
                        ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
                    },
                })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

    /// <summary>
    /// THE ORACLE. Before the fix the pair's matter was AppendTo-checked only when it was SECURE (the plan's guard): a missing
    /// matter or an ordinary one the caller cannot see was CREATED under, and only a secure one was refused — so the refusal
    /// said "this id exists and is secure". Now all three answer the same denial, and nothing is created. (#1616: a project —
    /// the table whose pair files it on its own.)
    /// </summary>
    [Fact]
    public async Task ChatCreate_AProjectFiledByThePair_UnderAMissingAnUnreadableOrASecureUnreadableMatter_AnswersIdentically()
    {
        var (secure, ordinary) = Matters();
        var absent = Guid.NewGuid();
        var user = Scripted();
        user.Missing.Add(absent);
        user.NoAppendTo.Add(ordinary);
        user.NoAppendTo.Add(secure);

        var missing = await CreateByThePair("sprk_project", user, absent, AppCreatesIntoTheWorld().Object);
        var unreadable = await CreateByThePair("sprk_project", user, ordinary, AppCreatesIntoTheWorld().Object);
        var secureUnreadable = await CreateByThePair("sprk_project", user, secure, AppCreatesIntoTheWorld().Object);

        missing.Success.Should().BeFalse("a project is never filed under a record that does not exist");
        unreadable.Success.Should().BeFalse("nor under an ordinary record the caller may not append to");
        (unreadable.ErrorCode, unreadable.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage));
        (secureUnreadable.ErrorCode, secureUnreadable.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage),
            "a secure record the caller cannot see answers exactly as a missing one");
        missing.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// The control, and #1616's legitimate BFF path: a project filed by the pair under a secure matter the caller MAY append
    /// to is created into isolation — also once the pair columns are field-secured (<paramref name="pairFieldSecured"/>, the
    /// state <c>scripts/Set-RegardingPairFieldSecurity.ps1</c> applies): the create's "no field-secured column" rule asks
    /// about every column but the BFF-managed pair, which only this server writes, after the AppendTo check AS THE CALLER.
    /// Before the fix the secured pair was refused as a field-secured column.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatCreate_AProjectFiledByThePair_UnderAnAppendableSecureMatter_IsCreatedSecure(bool pairFieldSecured)
    {
        var (secure, _) = Matters();
        var user = Scripted();
        if (pairFieldSecured)
        {
            user.SecuredColumns.Add(SecureRootInheritance.PairIdColumn);
            user.SecuredColumns.Add(SecureRootInheritance.PairTypeColumn);
        }

        var result = await CreateByThePair("sprk_project", user, secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ShouldBeSecure(_writes.Should().ContainSingle().Subject.Id, "filed by the pair under a secure matter");
    }

    /// <summary>
    /// #1616, the field-security control: any OTHER field-secured column on the project is still refused (the exemption is
    /// the BFF-managed pair, nothing else) — nothing is created.
    /// </summary>
    [Fact]
    public async Task ChatCreate_AProjectWithAnotherFieldSecuredColumn_IsStillDenied()
    {
        var (secure, _) = Matters();
        var user = Scripted();
        user.SecuredColumns.Add(SecureRootInheritance.PairIdColumn);
        user.SecuredColumns.Add(SecureRootInheritance.PairTypeColumn);
        user.SecuredColumns.Add("sprk_name");

        var result = await CreateByThePair("sprk_project", user, secure, AppCreatesIntoTheWorld().Object);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("sprk_name");
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// #1616: a WORK ASSIGNMENT filed only by the pair — under a missing, an ordinary, a secure or an appendable secure
    /// matter — is refused as invalid with ONE answer (it never reads the record the pair names, so it tells nothing about
    /// it), and nothing is created: its pair names a matter only by repeating its typed lookup, which Dataverse checks.
    /// Before the fix it was created (and, under a secure matter, isolated) on the pair alone.
    /// </summary>
    [Fact]
    public async Task ChatCreate_AWorkAssignmentFiledOnlyByThePair_IsRefusedAsInvalid_WithOneAnswer_AndNothingIsCreated()
    {
        var (secure, ordinary) = Matters();
        var absent = Guid.NewGuid();
        var user = Scripted();
        user.Missing.Add(absent);
        user.NoAppendTo.Add(ordinary);

        var answers = new[]
        {
            await CreateByThePair("sprk_workassignment", user, absent, AppCreatesIntoTheWorld().Object),
            await CreateByThePair("sprk_workassignment", user, ordinary, AppCreatesIntoTheWorld().Object),
            await CreateByThePair("sprk_workassignment", user, secure, AppCreatesIntoTheWorld().Object),
            await CreateByThePair("sprk_workassignment", Scripted(), secure, AppCreatesIntoTheWorld().Object),
        };

        foreach (var answer in answers)
        {
            answer.Success.Should().BeFalse();
            answer.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
            answer.ErrorMessage.Should().Be(answers[0].ErrorMessage);
        }

        answers[0].ErrorMessage.Should().Contain("sprk_regardingmatter");
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// The update tool asks the caller's AppendTo on the record a row is moved under BEFORE the secure-root gate: the gate
    /// reads app-only, and its own refusal (the record's creator walled off the secure matter) would otherwise say "this
    /// matter exists and is secure" to a caller who may not see it. Typed lookup (a work assignment) and the pair (a project,
    /// whose pair files it on its own — #1616) alike; nothing written.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChatUpdate_ReFilingUnderASecureMatterTheCallerCannotAppendTo_IsTheSameDenialAsAMissingMatter(bool byThePair)
    {
        var (secure, ordinary) = Matters();
        var absent = Guid.NewGuid();
        var record = Guid.NewGuid();
        var table = byThePair ? "sprk_project" : "sprk_workassignment";
        var typeRef = RecordTypeRef(_fixture, "sprk_matter");
        if (byThePair)
        {
            _fixture.SeedProject(record, isSecure: false);
            FilePair(_fixture, "sprk_project", record, ordinary, typeRef);
        }
        else
        {
            FiledWorkAssignment(_fixture, record, "sprk_regardingmatter", "sprk_matter", ordinary);
        }
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure); // the gate alone would refuse with creator_no_access
        var user = Scripted();
        user.Missing.Add(absent);
        user.NoAppendTo.Add(secure);

        Dictionary<string, object> Under(Guid matter) => byThePair
            ? new()
            {
                ["sprk_regardingrecordid"] = matter.ToString("D"),
                ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = typeRef },
            }
            : new() { ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = matter } };

        var toSecure = await ChatUpdateWith(table, record, user, Under(secure), appOnly: AppUpdatesTheWorld().Object);
        var toMissing = await ChatUpdateWith(table, record, user, Under(absent), appOnly: AppUpdatesTheWorld().Object);

        toSecure.Success.Should().BeFalse();
        toSecure.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        (toSecure.ErrorCode, toSecure.ErrorMessage).Should().Be((toMissing.ErrorCode, toMissing.ErrorMessage));
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
        _fixture.IsSecureOf(record).Should().BeFalse();
    }

    /// <summary>
    /// #1616: the application's update-only writer the update tool uses for a project's BFF-managed pair — applying the write
    /// to the fixture's Dataverse.
    /// </summary>
    private Mock<IFieldMappingDataverseService> AppUpdatesTheWorld()
    {
        var service = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        service
            .Setup(s => s.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((t, id, f, _) => ApplyFiling(t, id, f))
            .Returns(Task.CompletedTask);
        return service;
    }

    /// <summary>
    /// The control, and #1616's update path: a project re-filed by the pair under a secure matter the caller may append to
    /// is secured after the write. The pair columns are field-secured, so the caller's own PATCH of them is refused (403) —
    /// the tool sends the caller's PATCH of every OTHER column and the application writes the pair, after the AppendTo check
    /// AS THE CALLER. Before the fix the caller's PATCH carried the pair and was refused.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_AProjectReFiledByThePairUnderAnAppendableSecureMatter_WritesThePairAsTheApplication_AndSecuresIt()
    {
        var (secure, ordinary) = Matters();
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, ordinary, RecordTypeRef(_fixture, "sprk_matter"));
        var user = Scripted();
        user.SecuredColumns.Add(SecureRootInheritance.PairIdColumn);
        user.SecuredColumns.Add(SecureRootInheritance.PairTypeColumn);
        var appOnly = AppUpdatesTheWorld();

        var result = await ChatUpdateWith("sprk_project", project, user, new()
        {
            ["sprk_name"] = "Renamed",
            ["sprk_regardingrecordid"] = secure.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        }, appOnly: appOnly.Object);

        result.Success.Should().BeTrue(result.ErrorMessage);
        user.Patches.Should().ContainSingle("the caller's own PATCH carries the other columns")
            .Which.Body.Should().Contain("sprk_name").And.NotContain(SecureRootInheritance.PairIdColumn);
        appOnly.Verify(s => s.UpdateExistingRecordFieldsAsync("sprk_project", project,
            It.Is<Dictionary<string, object?>>(f => f.Count == 2 && f.ContainsKey(SecureRootInheritance.PairIdColumn)),
            It.IsAny<CancellationToken>()), Times.Once);
        ShouldBeSecure(project, "re-filed by the pair under a secure matter");
    }

    /// <summary>
    /// #1616, fail closed: with no application writer to hand (the tool built without its scope), a project's pair is NOT
    /// sent as the caller's PATCH (field security refuses it; before the lock the readers would honour it unchecked by this
    /// path's rule); nothing is written.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_AProjectsPair_WithNoApplicationWriter_IsRefused_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, ordinary, RecordTypeRef(_fixture, "sprk_matter"));
        var user = Scripted();

        var result = await ChatUpdateWith("sprk_project", project, user, new()
        {
            ["sprk_regardingrecordid"] = secure.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        });

        result.Success.Should().BeFalse();
        user.Patches.Should().BeEmpty();
        _writes.Should().BeEmpty();
        _fixture.IsSecureOf(project).Should().BeFalse();
    }

    /// <summary>
    /// #1616, the update tool's own rights check before the application writes a project's pair: without Append on the
    /// project (which setting the type lookup costs a run-as-user PATCH), refused, nothing written.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_AProjectsPair_WithoutAppendOnTheProject_IsDenied_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var project = Guid.NewGuid();
        _fixture.SeedProject(project, isSecure: false);
        FilePair(_fixture, "sprk_project", project, ordinary, RecordTypeRef(_fixture, "sprk_matter"));
        var user = Scripted();
        user.NoAppendTo.Add(project); // the scripted caller then holds ReadAccess,WriteAccess on it only
        var appOnly = AppUpdatesTheWorld();

        var result = await ChatUpdateWith("sprk_project", project, user, new()
        {
            ["sprk_regardingrecordid"] = secure.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        }, appOnly: appOnly.Object);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        appOnly.VerifyNoOtherCalls();
        user.Patches.Should().BeEmpty();
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// #1616: a work assignment re-filed by the pair ALONE (no typed lookup repeating it) is refused as invalid — even under
    /// a secure matter the caller may append to; nothing written. Before the fix it was re-filed and secured on the pair.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_AWorkAssignmentReFiledByThePairAlone_IsRefusedAsInvalid_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);

        var result = await ChatRefileWith(workAssignment, Scripted(), new()
        {
            ["sprk_regardingrecordid"] = secure.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
    }

    /// <summary>The legitimate update: the typed matter and the same pair, under a secure matter — re-filed and secured.</summary>
    [Fact]
    public async Task ChatUpdate_AWorkAssignmentReFiledByItsTypedMatterAndTheSamePair_UnderAnAppendableSecureMatter_SecuresIt()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);

        var result = await ChatRefileWith(workAssignment, Scripted(), new()
        {
            ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = secure },
            ["sprk_regardingrecordid"] = secure.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        ShouldBeSecure(workAssignment, "re-filed by its typed matter and the same pair under a secure matter");
    }

    // ── #1616 fix round ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A chat create of <paramref name="table"/> with <paramref name="item"/> as the scripted caller.</summary>
    private Task<ToolResult> ChatCreateWith(
        string table, Dictionary<string, object> item, SecureChildOwnershipAiToolTests.ScriptedUserClient? user = null) =>
        new DataverseCreateRecordHandler(user ?? Scripted(), CreateLogger<DataverseCreateRecordHandler>(),
                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
                new RecordOwnershipResolver(
                    SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                    NullLogger<RecordOwnershipResolver>.Instance),
                AppCreatesIntoTheWorld().Object, IdentityNormalizationFixtures.NoLinkedContact(), _gate)
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new { tablename = table, item })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

    /// <summary>
    /// The work assignment's row as the scripted caller reads it: its pair and its typed filing lookups, from the world.
    /// </summary>
    private void CallerReadsTheRow(SecureChildOwnershipAiToolTests.ScriptedUserClient user, Guid workAssignment)
    {
        static string? IdOf(object? value) => value is EntityReference r ? r.Id.ToString("D") : value?.ToString();
        var values = new Dictionary<string, object?>
        {
            [SecureRootInheritance.PairIdColumn] = World.ValueOf<object>("sprk_workassignment", workAssignment, SecureRootInheritance.PairIdColumn)?.ToString(),
            [$"_{SecureRootInheritance.PairTypeColumn}_value"] = IdOf(World.ValueOf<object>("sprk_workassignment", workAssignment, SecureRootInheritance.PairTypeColumn)),
        };
        foreach (var column in new[] { "sprk_regardingmatter", "sprk_regardingproject" })
            values[$"_{column}_value"] = IdOf(World.ValueOf<object>("sprk_workassignment", workAssignment, column));
        user.RowValues[workAssignment] = values;
    }

    /// <summary>
    /// Fix round 2, F-1: a wizard-made work assignment (typed matter A AND the same pair) re-filed by the BFF through the
    /// typed lookup ONLY, to a secure matter B, has its pair moved to B in the same caller PATCH — so it is secured with
    /// B's sharees and nothing is held (no <c>parent_unverifiable</c>). Before the fix the pair kept naming A: the record was
    /// secured but stuck (sharees held, flags unreadable, every loosening refused) until a repair.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_ATypedOnlyReFile_MovesTheInStepPairWithIt_AndSecuresItWithItsSharees()
    {
        var (secure, ordinary) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secure, null, Colleague);
        _fixture.SeedMatter(ordinary, isSecure: false);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        SecureRootInheritanceTests.FilePair(_fixture, "sprk_workassignment", workAssignment, ordinary, matterType);
        var user = Scripted();
        CallerReadsTheRow(user, workAssignment);

        var result = await ChatRefileWith(workAssignment, user,
            new() { ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = secure } });

        result.Success.Should().BeTrue(result.ErrorMessage);
        World.ValueOf<object>("sprk_workassignment", workAssignment, SecureRootInheritance.PairIdColumn)?.ToString()
            .Should().Be(secure.ToString("D"), "the pair moves with the typed lookup it repeated");
        World.ValueOf<EntityReference>("sprk_workassignment", workAssignment, SecureRootInheritance.PairTypeColumn)!.Id
            .Should().Be(matterType);
        ShouldBeSecure(workAssignment, "re-filed under a secure matter");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(
            RecordShareLevels.ChildMirrorMask(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CollaboratorAccessRights)),
            "the secure matter's sharee is mirrored — nothing is held");
    }

    /// <summary>Fix round 2, F-1: clearing the typed lookup the pair was in step with clears the pair (and its display
    /// columns) in the same caller PATCH. Before the fix the pair kept naming the old matter.</summary>
    [Fact]
    public async Task ChatUpdate_ClearingTheTypedLookup_ClearsTheInStepPair()
    {
        var (_, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        SecureRootInheritanceTests.FilePair(_fixture, "sprk_workassignment", workAssignment, ordinary, RecordTypeRef(_fixture, "sprk_matter"));
        var user = Scripted();
        CallerReadsTheRow(user, workAssignment);

        var result = await ChatRefileWith(workAssignment, user, new() { ["sprk_regardingmatter"] = null! });

        result.Success.Should().BeTrue(result.ErrorMessage);
        World.ValueOf<object>("sprk_workassignment", workAssignment, SecureRootInheritance.PairIdColumn).Should().BeNull();
        World.ValueOf<object>("sprk_workassignment", workAssignment, SecureRootInheritance.PairTypeColumn).Should().BeNull();
        user.Patches.Should().ContainSingle().Which.Body.Should().Contain("sprk_regardingrecordname");
    }

    /// <summary>Fix round 2, F-1 guard: a pair that was NOT in step with the moved lookup (it names another matter) is left
    /// alone — this server moves only a pair it can tell repeated that lookup.</summary>
    [Fact]
    public async Task ChatUpdate_ATypedReFile_LeavesAPairThatWasNotInStepAlone()
    {
        var (secure, ordinary) = Matters();
        var elsewhere = Guid.NewGuid();
        _fixture.SeedMatter(elsewhere, isSecure: false);
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        SecureRootInheritanceTests.FilePair(_fixture, "sprk_workassignment", workAssignment, elsewhere, RecordTypeRef(_fixture, "sprk_matter"));
        var user = Scripted();
        CallerReadsTheRow(user, workAssignment);

        await ChatRefileWith(workAssignment, user,
            new() { ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = secure } });

        user.Patches.Should().ContainSingle().Which.Body.Should().NotContain(SecureRootInheritance.PairIdColumn);
        World.ValueOf<object>("sprk_workassignment", workAssignment, SecureRootInheritance.PairIdColumn)?.ToString()
            .Should().Be(elsewhere.ToString("D"));
    }

    /// <summary>F-C, the walls still decide: a re-file under a secure matter while an unrepeated pair names the old matter,
    /// with the creator walled off the secure matter, is <c>creator_no_access</c> (never "unverifiable"), before the PATCH.</summary>
    [Fact]
    public async Task ChatUpdate_ReFilingByTheTypedLookupWhileThePairStillNamesTheOldMatter_StillHonoursTheSecureMattersNoAccessList()
    {
        var (secure, ordinary) = Matters();
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        SecureRootInheritanceTests.FilePair(_fixture, "sprk_workassignment", workAssignment, ordinary, RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await ChatRefile(workAssignment, secure);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
    }

    /// <summary>
    /// F-A: a work assignment created with a pair naming ANOTHER work assignment (no typed lookup can repeat it) is refused
    /// as invalid, nothing created — the container resolver would refuse it anyway. Before the fix round it was created, and
    /// the resolver then stored its children in that other work assignment's container.
    /// </summary>
    [Fact]
    public async Task ChatCreate_AWorkAssignmentWhosePairNamesAnotherWorkAssignment_IsRefusedAsInvalid_AndNothingIsCreated()
    {
        var other = Guid.NewGuid();
        _fixture.SeedWorkAssignment(other, isSecure: true);

        var result = await ChatCreateWith("sprk_workassignment", new()
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_regardingrecordid"] = other.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_workassignment") },
        });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        _writes.Should().BeEmpty("nothing is created");
    }

    /// <summary>
    /// F-A (coordinator constraint): the Create Work Assignment wizard's other shapes — a work assignment filed under an
    /// INVOICE or an EVENT by its typed lookup AND the same pair — are created exactly as before (ordinary, nothing refused).
    /// </summary>
    [Theory]
    [InlineData("sprk_regardinginvoice", "sprk_invoice")]
    [InlineData("sprk_regardingevent", "sprk_event")]
    public async Task ChatCreate_AWorkAssignmentFiledUnderAnInvoiceOrAnEvent_WithTheSamePair_IsCreated(string column, string table)
    {
        var intermediate = Guid.NewGuid();
        _fixture.ChildWorld.Add(table, intermediate);
        _fixture.ChildWorld.MoveOwner(table, intermediate, DataversePrincipalRef.User(Creator)); // an owner (and its business unit)

        var result = await ChatCreateWith("sprk_workassignment", new()
        {
            ["sprk_name"] = "Review the lease",
            [column] = new { relatedTable = table, recordId = intermediate },
            ["sprk_regardingrecordid"] = intermediate.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, table) },
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        _fixture.IsSecureOf(_writes.Should().ContainSingle().Subject.Id).Should().BeFalse("an invoice or event is not a secure root");
    }

    /// <summary>
    /// K-b: an update that moves only a project's pair ID (its type kept) has the application write the FULL pair it checked
    /// — the id AND the row's type — never the id alone beside whatever the type holds by then.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_AProjectsPairIdAlone_TheApplicationWritesTheFullCheckedPair()
    {
        var (secure, ordinary) = Matters();
        var project = Guid.NewGuid();
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.SeedProject(project, isSecure: false);
        SecureRootInheritanceTests.FilePair(_fixture, "sprk_project", project, ordinary, matterType);
        var user = Scripted();
        user.SecuredColumns.Add(SecureRootInheritance.PairIdColumn);
        user.SecuredColumns.Add(SecureRootInheritance.PairTypeColumn);
        user.RowValues[project] = new() // the row as the caller reads it (every user reads the pair)
        {
            [SecureRootInheritance.PairIdColumn] = ordinary.ToString("D"),
            [$"_{SecureRootInheritance.PairTypeColumn}_value"] = matterType.ToString("D"),
        };
        var appOnly = AppUpdatesTheWorld();

        var result = await ChatUpdateWith("sprk_project", project, user, new()
        {
            ["sprk_regardingrecordid"] = secure.ToString("D"),
        }, appOnly: appOnly.Object);

        result.Success.Should().BeTrue(result.ErrorMessage);
        appOnly.Verify(s => s.UpdateExistingRecordFieldsAsync("sprk_project", project,
            It.Is<Dictionary<string, object?>>(f => f.Count == 2
                && f.ContainsKey(SecureRootInheritance.PairIdColumn)
                && f.Keys.Any(k => SecureRootInheritance.NormalizeColumn(k) == SecureRootInheritance.PairTypeColumn
                                   && f[k]!.ToString()!.Contains(matterType.ToString("D")))),
            It.IsAny<CancellationToken>()), Times.Once);
        ShouldBeSecure(project, "re-filed by its pair id under a secure matter, its type kept");
    }

    /// <summary>
    /// Fix round 2, K-2: the type the application writes is the one the pair rule CHECKED — taken from the row once, before
    /// any check — never one a later read returns. Here the row's type changes after its first read; the written pair still
    /// carries the first. Before the fix the write took the type from a second read.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_AProjectsPairIdAlone_WritesTheTypeThatWasChecked_NotALaterRead()
    {
        var (secure, ordinary) = Matters();
        var project = Guid.NewGuid();
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        var laterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.SeedProject(project, isSecure: false);
        SecureRootInheritanceTests.FilePair(_fixture, "sprk_project", project, ordinary, matterType);
        var user = Scripted();
        user.SecuredColumns.Add(SecureRootInheritance.PairIdColumn);
        user.SecuredColumns.Add(SecureRootInheritance.PairTypeColumn);
        user.RowValues[project] = new()
        {
            [SecureRootInheritance.PairIdColumn] = ordinary.ToString("D"),
            [$"_{SecureRootInheritance.PairTypeColumn}_value"] = matterType.ToString("D"),
        };
        var appOnly = AppUpdatesTheWorld();

        var result = await ChatUpdateWith("sprk_project", project, user, new()
        {
            ["sprk_regardingrecordid"] = secure.ToString("D"),
        }, appOnly: appOnly.Object, afterGet: path =>
        {
            if (path.StartsWith($"sprk_projects({project:D})", StringComparison.OrdinalIgnoreCase))
                user.RowValues[project][$"_{SecureRootInheritance.PairTypeColumn}_value"] = laterType.ToString("D");
        });

        result.Success.Should().BeTrue(result.ErrorMessage);
        appOnly.Verify(s => s.UpdateExistingRecordFieldsAsync("sprk_project", project,
            It.Is<Dictionary<string, object?>>(f => f.Keys.Any(k =>
                SecureRootInheritance.NormalizeColumn(k) == SecureRootInheritance.PairTypeColumn
                && f[k]!.ToString()!.Contains(matterType.ToString("D")))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Verifier round on #1612 (F1/F2): a lookup with the same id in ANOTHER table never stands in for the pair's record ──

    /// <summary>
    /// A <paramref name="table"/> row assigned to a contact whose id the caller chose equal to <paramref name="matter"/>'s (a
    /// client may pick a new row's id), and filed by the pair under that matter. Before the fix the pair's AppendTo check —
    /// and the plan's own guard — were skipped because the pair's id equalled SOME lookup's.
    /// </summary>
    private Task<ToolResult> CreateAssignedToAContactWithTheMattersId(
        SecureChildOwnershipAiToolTests.ScriptedUserClient user, string table, string contactColumn, Guid matter,
        IFieldMappingDataverseService appOnly) =>
        new DataverseCreateRecordHandler(user, CreateLogger<DataverseCreateRecordHandler>(),
                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
                new RecordOwnershipResolver(
                    SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                    NullLogger<RecordOwnershipResolver>.Instance),
                appOnly, IdentityNormalizationFixtures.NoLinkedContact(), _gate)
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
                {
                    tablename = table,
                    item = new Dictionary<string, object>
                    {
                        ["sprk_name"] = "Review the lease",
                        [contactColumn] = new { relatedTable = "contact", recordId = matter },
                        ["sprk_regardingrecordid"] = matter.ToString("D"),
                        ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
                    },
                })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

    /// <param name="expectedCode">#1616: a work assignment filed by the pair alone is refused as invalid before any record is
    /// asked about — still one answer for both; a project's pair (its only filing) is the AppendTo denial.</param>
    [Theory]
    [InlineData("sprk_workassignment", "sprk_assignedto", ToolErrorCodes.ValidationFailed)]
    [InlineData("sprk_project", "sprk_assignedtointernal", DataverseUserClientErrorCodes.AccessDenied)]
    public async Task ChatCreate_AssignedToAContactWithASecureMattersId_AndFiledByThePairUnderThatMatter_IsTheSameDenialAsAMissingMatter(
        string table, string contactColumn, string expectedCode)
    {
        var (secure, _) = Matters();
        var absent = Guid.NewGuid();
        var user = Scripted();
        user.NoAppendToIn.Add(("sprk_matters", secure)); // the caller may append to the CONTACT with that id, not the matter
        user.MissingIn.Add(("sprk_matters", absent));

        var toSecure = await CreateAssignedToAContactWithTheMattersId(user, table, contactColumn, secure, AppCreatesIntoTheWorld().Object);
        var toMissing = await CreateAssignedToAContactWithTheMattersId(user, table, contactColumn, absent, AppCreatesIntoTheWorld().Object);

        toSecure.Success.Should().BeFalse("a row is never filed under a secure matter the caller may not append to");
        toSecure.ErrorCode.Should().Be(expectedCode);
        (toSecure.ErrorCode, toSecure.ErrorMessage).Should().Be((toMissing.ErrorCode, toMissing.ErrorMessage),
            "the matter's existence and security are not told by a contact sharing its id");
        _writes.Should().BeEmpty("nothing is created");
    }

    /// <summary>The legitimate shape still works: the typed filing lookup and the same pair, under a secure matter.</summary>
    [Fact]
    public async Task ChatCreate_AWorkAssignmentWithItsTypedMatterAndTheSamePair_UnderAnAppendableSecureMatter_IsCreatedSecure()
    {
        var (secure, _) = Matters();

        var result = await new DataverseCreateRecordHandler(Scripted(), CreateLogger<DataverseCreateRecordHandler>(),
                new HandoffUrlBuilder("https://spaarkedev1.crm.dynamics.com"),
                new RecordOwnershipResolver(
                    SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object, SecureChildShareWorld.Configuration(),
                    NullLogger<RecordOwnershipResolver>.Instance),
                AppCreatesIntoTheWorld().Object, IdentityNormalizationFixtures.NoLinkedContact(), _gate)
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
                {
                    tablename = "sprk_workassignment",
                    item = new Dictionary<string, object>
                    {
                        ["sprk_name"] = "Review the lease",
                        ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = secure },
                        ["sprk_regardingrecordid"] = secure.ToString("D"),
                        ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
                    },
                })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseCreateRecordHandler)), CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        ShouldBeSecure(_writes.Should().ContainSingle().Subject.Id, "filed by its typed matter and the same pair");
    }

    /// <summary>
    /// The update tool's oracle (verifier F1/F2): an update that assigns a contact whose id equals matter X's and moves the
    /// pair onto X used to skip the pair's AppendTo check, so the app-only secure-root gate then read X and answered with its
    /// OWN refusals — the Access Permission floor ("filed under {name}, cannot be lower than Restricted") or the creator's
    /// wall (<c>creator_no_access</c>) — telling the caller that X exists, its name and its permission level. Now AppendTo on
    /// X is asked as the caller FIRST: a missing matter, an ordinary one they cannot see, a secure one they cannot see and a
    /// Restricted one they cannot see answer identically, and nothing is written.
    /// </summary>
    /// <param name="expectedCode">#1616: on a work assignment the pair moved alone is refused as invalid before any record is
    /// asked about; on a project (its pair is its only filing) the AppendTo denial. One answer for all four either way.</param>
    [Theory]
    [InlineData("sprk_workassignment", "sprk_assignedto", ToolErrorCodes.ValidationFailed)]
    [InlineData("sprk_project", "sprk_assignedtointernal", DataverseUserClientErrorCodes.AccessDenied)]
    public async Task ChatUpdate_AssigningAContactWithAMattersId_AndMovingThePairOntoIt_AnswersIdentically_ForAMissingAnOrdinaryASecureAndARestrictedMatter(
        string table, string contactColumn, string expectedCode)
    {
        var (secure, ordinary) = Matters();
        var restricted = Guid.NewGuid();
        _fixture.SeedMatter(restricted, isSecure: false);
        World.Set("sprk_matter", restricted, "sprk_accesspermission",
            new OptionSetValue(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService.AccessPermissionRestricted));
        var absent = Guid.NewGuid();
        var (filedUnder, _) = Matters();
        var record = Guid.NewGuid();
        var typeRef = RecordTypeRef(_fixture, "sprk_matter");
        if (table == "sprk_project")
        {
            _fixture.SeedProject(record, isSecure: false);
            FilePair(_fixture, "sprk_project", record, filedUnder, typeRef);
        }
        else
        {
            FiledWorkAssignment(_fixture, record, "sprk_regardingmatter", "sprk_matter", filedUnder);
        }
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure); // the gate alone would refuse with creator_no_access
        var user = Scripted();
        user.MissingIn.Add(("sprk_matters", absent));
        foreach (var matter in new[] { ordinary, secure, restricted })
            user.NoAppendToIn.Add(("sprk_matters", matter)); // the contacts with these ids are the caller's to append to

        Task<ToolResult> Move(Guid matter) => ChatUpdateWith(table, record, user, new()
        {
            [contactColumn] = new { relatedTable = "contact", recordId = matter },
            ["sprk_regardingrecordid"] = matter.ToString("D"),
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = typeRef },
            ["sprk_accesspermission"] = 100000000, // Standard: below a Restricted parent's floor
        }, appOnly: AppUpdatesTheWorld().Object);

        var missing = await Move(absent);
        var answers = new[] { await Move(ordinary), await Move(secure), await Move(restricted) };

        missing.Success.Should().BeFalse();
        missing.ErrorCode.Should().Be(expectedCode);
        foreach (var answer in answers)
            (answer.ErrorCode, answer.ErrorMessage).Should().Be((missing.ErrorCode, missing.ErrorMessage));
        missing.ErrorMessage.Should().NotContain("creator_no_access")
            .And.NotContain("Access Permission").And.NotContain("Restricted");
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
    }

    // ── #1630 (owner Option A, 2026-10-11): filed under an INVOICE or EVENT of a secure matter or project ─────────────────

    /// <summary>The Create Work Assignment wizard's shape for an intermediate: its typed lookup AND the same pair.</summary>
    private Dictionary<string, object> UnderIntermediate(string column, string table, Guid intermediate) => new()
    {
        ["sprk_name"] = "Review the invoice",
        [column] = new { relatedTable = table, recordId = intermediate },
        ["sprk_regardingrecordid"] = intermediate.ToString("D"),
        ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, table) },
    };

    /// <summary>
    /// #1630: the wizard's shapes under an INVOICE of a secure matter and an EVENT of a secure project are created INTO
    /// isolation (named team and flag in the create itself) and come out secure with the parent's sharee — exactly as if
    /// filed directly under it. Before #1630 they were created ordinary in the caller's business unit.
    /// </summary>
    [Theory]
    [InlineData("sprk_regardinginvoice", "sprk_invoice", "sprk_matter")]
    [InlineData("sprk_regardingevent", "sprk_event", "sprk_project")]
    public async Task ChatCreate_AWorkAssignmentUnderAnInvoiceOrEventOfASecureRecord_IsCreatedIntoIsolation_AndComesOutSecure(
        string column, string table, string parentTable)
    {
        var parent = Guid.NewGuid();
        if (parentTable == "sprk_matter")
            SecureMatter(_fixture, parent, null, Colleague);
        else
            SecureProject(_fixture, parent, Colleague);
        var intermediate = table == "sprk_invoice"
            ? SecureRootInheritanceTests.InvoiceOf(_fixture, parent)
            : SecureRootInheritanceTests.EventOf(_fixture, parentTable, parent);

        var result = await ChatCreateWith("sprk_workassignment", UnderIntermediate(column, table, intermediate));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var (_, created, fields) = _writes.Should().ContainSingle().Subject;
        BoundId(fields, "ownerid@odata.bind").Should().Be(SecureTeam, "created owned by the named team");
        fields["sprk_issecure"].Should().Be(true, "flagged in the create itself");
        ShouldBeSecure(created, $"created under a {table} of a secure {parentTable}");
        _fixture.ShareMaskOf(created, Colleague).Should().Be(
            RecordShareLevels.ChildMirrorMask(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CollaboratorAccessRights)),
            "the secure parent's sharee, as if filed directly under it");
    }

    /// <summary>
    /// #1630, never broaden reach: AppendTo on the INVOICE does not let a caller file a work assignment under the secure
    /// matter it belongs to — the plan asks AppendTo on that matter as the caller (create parity), nothing is created.
    /// </summary>
    [Fact]
    public async Task ChatCreate_UnderAnInvoiceOfASecureMatterTheCallerCannotAppendTo_IsDenied_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        var invoice = SecureRootInheritanceTests.InvoiceOf(_fixture, secure);
        var user = Scripted();
        user.NoAppendTo.Add(secure);

        var result = await ChatCreateWith("sprk_workassignment", UnderIntermediate("sprk_regardinginvoice", "sprk_invoice", invoice), user);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _writes.Should().BeEmpty("nothing is created");
    }

    /// <summary>#1630: the creator's No Access check runs against the secure matter the invoice belongs to.</summary>
    [Fact]
    public async Task ChatCreate_UnderAnInvoiceOfASecureMatter_WhenTheCallerIsOnTheMattersNoAccessList_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        var invoice = SecureRootInheritanceTests.InvoiceOf(_fixture, secure);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await ChatCreateWith("sprk_workassignment", UnderIntermediate("sprk_regardinginvoice", "sprk_invoice", invoice));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty("nothing is created");
    }

    /// <summary>#1630, fail closed: an invoice whose row cannot be read refuses the create — never "filed under nothing".</summary>
    [Fact]
    public async Task ChatCreate_UnderAnInvoiceThatCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        var invoice = SecureRootInheritanceTests.InvoiceOf(_fixture, secure);
        World.FailingRowReadsOf("sprk_invoice", invoice);

        var result = await ChatCreateWith("sprk_workassignment", UnderIntermediate("sprk_regardinginvoice", "sprk_invoice", invoice));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("sprk_invoice");
        _writes.Should().BeEmpty("nothing is created");
    }

    /// <summary>#1630, the ordinary case is unchanged: under an invoice of an ORDINARY matter the work assignment is created
    /// ordinary.</summary>
    [Fact]
    public async Task ChatCreate_UnderAnInvoiceOfAnOrdinaryMatter_StaysOrdinary()
    {
        var (_, ordinary) = Matters();
        var invoice = SecureRootInheritanceTests.InvoiceOf(_fixture, ordinary);
        _fixture.ChildWorld.MoveOwner("sprk_invoice", invoice, DataversePrincipalRef.User(Creator));

        var result = await ChatCreateWith("sprk_workassignment", UnderIntermediate("sprk_regardinginvoice", "sprk_invoice", invoice));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var created = _writes.Should().ContainSingle().Subject.Id;
        _fixture.IsSecureOf(created).Should().BeFalse("its invoice belongs to an ordinary matter");
        _fixture.OwningTeamOf(created).Should().NotBe(SecureTeam);
    }

    /// <summary>The scripted caller reads the work assignment's pair and its invoice lookup from the world.</summary>
    private void CallerReadsTheInvoiceFiling(SecureChildOwnershipAiToolTests.ScriptedUserClient user, Guid workAssignment)
    {
        CallerReadsTheRow(user, workAssignment);
        user.RowValues[workAssignment]["_sprk_regardinginvoice_value"] =
            World.ValueOf<EntityReference>("sprk_workassignment", workAssignment, "sprk_regardinginvoice")?.Id.ToString("D");
    }

    /// <summary>
    /// #1630, re-file: a wizard-made work assignment under an invoice of an ORDINARY matter (typed invoice AND the same pair),
    /// re-filed by the update tool to an invoice of a SECURE matter through the typed lookup only, has its pair moved with it
    /// and is secured with the matter's sharee.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_ReFilingToAnInvoiceOfASecureMatter_MovesThePair_AndSecuresItWithTheSharee()
    {
        var (secure, ordinary) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, secure, null, Colleague);
        _fixture.SeedMatter(ordinary, isSecure: false);
        var (ordinaryInvoice, secureInvoice) = (SecureRootInheritanceTests.InvoiceOf(_fixture, ordinary),
            SecureRootInheritanceTests.InvoiceOf(_fixture, secure));
        var invoiceType = RecordTypeRef(_fixture, "sprk_invoice");
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardinginvoice", "sprk_invoice", ordinaryInvoice);
        FilePair(_fixture, "sprk_workassignment", workAssignment, ordinaryInvoice, invoiceType);
        var user = Scripted();
        CallerReadsTheInvoiceFiling(user, workAssignment);

        var result = await ChatRefileWith(workAssignment, user,
            new() { ["sprk_regardinginvoice"] = new { relatedTable = "sprk_invoice", recordId = secureInvoice } });

        result.Success.Should().BeTrue(result.ErrorMessage);
        World.ValueOf<object>("sprk_workassignment", workAssignment, SecureRootInheritance.PairIdColumn)?.ToString()
            .Should().Be(secureInvoice.ToString("D"), "the pair moves with the invoice lookup it repeated");
        ShouldBeSecure(workAssignment, "re-filed under an invoice of a secure matter");
        _fixture.ShareMaskOf(workAssignment, Colleague).Should().Be(
            RecordShareLevels.ChildMirrorMask(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CollaboratorAccessRights)));
    }

    /// <summary>
    /// #1630, never broaden reach on a re-file: AppendTo on the invoice is not enough to move a work assignment under the
    /// secure matter it belongs to — the update tool asks AppendTo on that matter as the caller before the gate says anything
    /// about it (the creator's No Access wall would otherwise answer), and nothing is written.
    /// </summary>
    [Fact]
    public async Task ChatUpdate_ReFilingToAnInvoiceOfASecureMatterTheCallerCannotAppendTo_IsDenied_AndNothingIsWritten()
    {
        var (secure, ordinary) = Matters();
        var invoice = SecureRootInheritanceTests.InvoiceOf(_fixture, secure);
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure); // the gate alone would refuse with creator_no_access
        var user = Scripted();
        user.NoAppendTo.Add(secure);

        var result = await ChatRefileWith(workAssignment, user,
            new() { ["sprk_regardinginvoice"] = new { relatedTable = "sprk_invoice", recordId = invoice } });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
        _fixture.IsSecureOf(workAssignment).Should().BeFalse();
    }

    /// <summary>#1630: the re-file's creator wall checks the secure matter the invoice belongs to, before the PATCH.</summary>
    [Fact]
    public async Task ChatUpdate_ReFilingToAnInvoiceOfASecureMatter_WhenTheCreatorIsOnTheMattersNoAccessList_IsRefusedBeforeThePatch()
    {
        var (secure, ordinary) = Matters();
        var invoice = SecureRootInheritanceTests.InvoiceOf(_fixture, secure);
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await ChatRefileWith(workAssignment, Scripted(),
            new() { ["sprk_regardinginvoice"] = new { relatedTable = "sprk_invoice", recordId = invoice } });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _writes.Should().BeEmpty("the caller's PATCH is never sent");
    }

    // ── (2) RE-FILE: the playbook output orchestrator's DataverseUpdateHandler ─────────────────────────────────────────

    private Task RefileThroughTheUpdateHandler(Guid workAssignment, Guid matter) =>
        new DataverseUpdateHandler(
                FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new StampWorld().Restamper,
                new RecordOwnershipResolverDouble(), _gate, NullLogger<DataverseUpdateHandler>.Instance,
                SecureChildReconcilerFixtures.OverEmptyWorld())
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
            FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new StampWorld().Restamper,
            new RecordOwnershipResolverDouble(), _gate, NullLogger<DataverseUpdateHandler>.Instance,
                SecureChildReconcilerFixtures.OverEmptyWorld());

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
            FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new StampWorld().Restamper,
            new RecordOwnershipResolverDouble(), SecureRootFilingGateFixtures.Unregistered(), NullLogger<DataverseUpdateHandler>.Instance,
                SecureChildReconcilerFixtures.OverEmptyWorld());
        var viaHandler = () => handler.UpdateAsync("sprk_workassignment", workAssignment, filing, ConcurrencyMode.None, 1, CancellationToken.None);
        await viaHandler.Should().ThrowAsync<RecordOwnerUnresolvedException>();

        var noGate = new ServiceCollection()
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.EntitySetNameFixtures.Service())
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
            Guid.Parse("0000c158-0000-0000-0000-00000000ca11"), NullLogger.Instance, CancellationToken.None, rootFiling: null);
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
            .AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.EntitySetNameFixtures.Service())
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
            // Batch-4 integration (task 166 S-67): the push writes each child AS the caller.
            Guid.Parse("0000c158-0000-0000-0000-00000000ca11"),
            NullLogger.Instance,
            CancellationToken.None,
            rootFiling: _gate);

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

    // ── Task 175 fix round 2 (K3): sprk_issecure is never a generic writer's to set ─────────────────────────────────────

    /// <summary>
    /// K3, the output orchestrator's update handler: a payload that sets <c>sprk_issecure</c> on a work assignment (true or
    /// false) is refused <c>secure_flag_transition_only</c> and nothing is written — only Make Secure / Remove Secure (F3,
    /// with their isolation steps) and the cascade set it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateHandler_ASecureFlagWrite_IsRefused_AndNothingIsWritten(bool value)
    {
        var workAssignment = Guid.NewGuid();
        _fixture.SeedWorkAssignment(workAssignment, isSecure: !value);
        var handler = new DataverseUpdateHandler(
            FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new StampWorld().Restamper,
            new RecordOwnershipResolverDouble(), _gate, NullLogger<DataverseUpdateHandler>.Instance,
                SecureChildReconcilerFixtures.OverEmptyWorld());

        var act = () => handler.UpdateAsync("sprk_workassignment", workAssignment,
            new Dictionary<string, object?> { ["sprk_issecure"] = value }, ConcurrencyMode.None, 1, CancellationToken.None);

        // #2251: the app-only refusal (a secure root's secure flag) answers before the filing gate's K3 refusal.
        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        _writes.Should().BeEmpty();
    }

    /// <summary>K3, the UpdateRecord node / ActionSeam core (playbooks): the same refusal, nothing written.</summary>
    [Fact]
    public async Task ActionCore_ASecureFlagWrite_IsRefused_AndNothingIsWritten()
    {
        var workAssignment = Guid.NewGuid();
        _fixture.SeedWorkAssignment(workAssignment, isSecure: true);
        var services = new ServiceCollection()
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.EntitySetNameFixtures.Service())
            .AddSingleton(_gate)
            .BuildServiceProvider();
        var core = new UpdateRecordActionCore(
            FieldMappingWritingToTheWorld().Object, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);

        var act = () => core.UpdateAsync(
            new UpdateRecordActionInput("sprk_project", workAssignment, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_issecure"] = "false" }, Lookups: null),
            CancellationToken.None);

        // #2251: the app-only refusal (a secure root's secure flag) answers before the filing gate's K3 refusal.
        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// #2253 fix round 1 (K4): an update that impersonates its user for a proposal that user confirmed (#2257: only
    /// <c>UpdateRecordPayloadSource.ConfirmedProposal</c>) skips the app-only refusal, so the filing gate's K3
    /// refusal still answers a <c>sprk_issecure</c> write on a project or a work assignment there, and nothing is written. The
    /// gate's refusal stays pinned at the writer level, not only behind the app-only one.
    /// </summary>
    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task ActionCore_AnImpersonatedSecureFlagWrite_IsRefusedByTheFilingGate_AndNothingIsWritten(string table)
    {
        var record = Guid.NewGuid();
        _fixture.SeedWorkAssignment(record, isSecure: true);
        var services = new ServiceCollection()
            .AddSingleton(new StampWorld().Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
            .AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.EntitySetNameFixtures.Service())
            .AddSingleton(_gate)
            .BuildServiceProvider();
        var core = new UpdateRecordActionCore(
            FieldMappingWritingToTheWorld().Object, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);

        var act = () => core.UpdateAsync(
            new UpdateRecordActionInput(table, record, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_issecure"] = "false" }, Lookups: null,
                ImpersonateSystemUserId: Guid.Parse("0000c158-0000-0000-0000-0000000e2253"),
                PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(AccessFollowsParent.SecureFlagReasonCode);
        _writes.Should().BeEmpty();
    }

    /// <summary>
    /// K3, the field-mapping push: a rule onto <c>sprk_issecure</c> fails that record, nothing written. #2253 fix round 1
    /// (K2): the push runs as its caller and asks the refusal as <c>WriteIdentity.Caller</c>, which leaves a secure root's
    /// columns alone, so the filing gate's K3 refusal answers it, as before #2251.
    /// </summary>
    [Fact]
    public async Task FieldMappingPush_OntoTheSecureFlag_FailsThatRecord_AndWritesNothing()
    {
        var workAssignment = Guid.NewGuid();
        _fixture.SeedWorkAssignment(workAssignment, isSecure: false);

        var (updated, failed, _, errors, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            FieldMappingWritingToTheWorld().Object,
            new StampWorld().Restamper,
            [
                new FieldMappingRuleDto
                {
                    SourceField = "sprk_issecure", TargetField = "sprk_issecure",
                    SourceFieldType = "Boolean", TargetFieldType = "Boolean", Priority = 1,
                },
            ],
            new Dictionary<string, object?> { ["sprk_issecure"] = true },
            "sprk_workassignment",
            [workAssignment],
            Guid.Parse("0000c158-0000-0000-0000-00000000ca11"),
            NullLogger.Instance,
            CancellationToken.None,
            rootFiling: _gate);

        (updated, failed).Should().Be((0, 1));
        errors.Should().ContainSingle(e => e.RecordId == workAssignment && e.Error.Contains(AccessFollowsParent.SecureFlagReasonCode));
        _writes.Should().BeEmpty();
    }
}
