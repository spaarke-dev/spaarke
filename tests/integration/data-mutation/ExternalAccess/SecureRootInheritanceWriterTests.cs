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
                table.Should().Be("sprk_workassignment");
                _fixture.SeedWorkAssignment(id, owningTeamId: BoundId(fields, "ownerid@odata.bind"),
                    isSecure: fields.TryGetValue("sprk_issecure", out var flag) && flag is true,
                    createdBy: AppUser, createdByPerson: BoundId(fields, "sprk_CreatedByPerson@odata.bind"));
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
    /// matter is asked AS THE CALLER before the isolated create; without it, denied, nothing created.
    /// </summary>
    [Fact]
    public async Task ChatCreate_UnderASecureMatterByThePair_WithoutAppendToOnTheMatter_IsDenied_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        var user = new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator);
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
                tablename = "sprk_workassignment",
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
    /// Owner round 31 item 1, chat create: filed under a SECURE matter (typed) AND, by the pair, under a matter whose flag
    /// reads EMPTY — secure-if-any makes the record secure, but the caller cannot be checked against the unreadable matter's
    /// No Access list, so the create is refused (<c>creator_no_access_unverifiable</c>) with nothing written.
    /// </summary>
    [Fact]
    public async Task ChatCreate_UnderASecureMatterAndOneWhoseFlagCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        var (secure, other) = Matters();
        FlagUnreadable(other);

        var result = await ChatCreate(AppCreatesIntoTheWorld().Object).ExecuteChatAsync(
            BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
            {
                tablename = "sprk_workassignment",
                item = new Dictionary<string, object>
                {
                    ["sprk_name"] = "Review the lease",
                    ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = secure },
                    ["sprk_regardingrecordid"] = other.ToString("D"),
                    ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
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
            new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator), CreateLogger<DataverseCreateRecordHandler>(),
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

    // ── (1) CREATE: the Office quick-create of a project (filed through the Field Mapping Framework's pair) ──────────

    private readonly List<Entity> _officeCreates = new();

    /// <summary>Task 158 r1c-v1: what changes right after the Office create — between its plan and its provisioning.</summary>
    private Action<Guid>? _afterOfficeCreate;

    private RecordCreationService OfficeCreator(
        Guid sourceMatter, Guid typeRef, FieldMappingRuleEntity? extraRule = null, bool withBearerToken = true)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity row, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                _officeCreates.Add(row);
                _fixture.SeedProject(id, owningTeamId: row.GetAttributeValue<EntityReference>("ownerid").Id,
                    isSecure: row.GetAttributeValue<bool?>("sprk_issecure") == true,
                    createdBy: AppUser, createdByPerson: row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id);
                ApplyFiling("sprk_project", id, row.Attributes.ToDictionary(a => a.Key, a => (object?)a.Value));
                _afterOfficeCreate?.Invoke(id);
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
                    .. (extraRule is null ? Array.Empty<FieldMappingRuleEntity>() : new[] { extraRule }),
                ],
            });

        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SecureChildShareWorld.GeneralTeam);
        return new RecordCreationService(entities.Object, fieldMappings.Object, ownership.Object,
            IdentityNormalizationFixtures.NoLinkedContact(), _gate,
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(), NullLogger<RecordCreationService>.Instance,
            _officeProbe.Object, new HttpContextAccessor { HttpContext = withBearerToken ? CallerRequest() : new DefaultHttpContext() });
    }

    /// <summary>
    /// G5 for the Office create (owner round 31 item 2): the OBO probe's designated virtual seam — the caller's Create on
    /// <c>sprk_project</c> and their rights on each secure parent. Default: both held.
    /// </summary>
    private readonly Mock<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe> _officeProbe = OfficeProbe();

    private static Mock<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe> OfficeProbe()
    {
        var probe = new Mock<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>(
            MockBehavior.Strict, new HttpClient(), new ConfigurationBuilder().Build(),
            NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>.Instance, null!);
        probe.Setup(p => p.CallerHoldsPrivilegeAsync("caller-token", RecordCreationService.ProjectCreatePrivilege, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        probe.Setup(p => p.GetCallerRightsAsync("caller-token", It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo);
        return probe;
    }

    private static HttpContext CallerRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer caller-token";
        return context;
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

    /// <summary>
    /// AC 1 + owner round 31 item 2, Office quick-create: a project the mapping files under a SECURE matter is created INTO
    /// isolation (the named team and the flag in the create itself, the maker as <c>sprk_createdbyperson</c>), after the
    /// caller's own rights were checked (G5), and comes out secure.
    /// </summary>
    [Fact]
    public async Task OfficeCreate_AProjectFiledUnderASecureMatter_IsCreatedIntoIsolation_AndComesOutSecure()
    {
        var (secure, _) = Matters();

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeTrue(result.Failure?.Detail);
        result.Warnings.Should().NotContain(w => w.Contains("secur"), "securing completed in the same call");
        var row = _officeCreates.Should().ContainSingle().Subject;
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam, "never the caller's unit first");
        row.GetAttributeValue<bool?>("sprk_issecure").Should().BeTrue();
        _fixture.Updates.Should().NotContain(u => u.RecordId == result.RecordId && u.Payload.ContainsKey("ownerid@odata.bind"));
        ShouldBeSecure(result.RecordId, "filed under a secure matter by the mapping's pair");
        _officeProbe.Verify(p => p.GetCallerRightsAsync("caller-token", "sprk_matters", secure, It.IsAny<CancellationToken>()),
            Times.Once, "AppendTo on the secure matter is asked AS THE CALLER");
    }

    /// <summary>Owner round 31 item 2, Office: the maker cannot be shared — the project is removed again; nothing created.</summary>
    [Fact]
    public async Task OfficeCreate_WhenTheMakerCannotBeShared_TheProjectIsRemoved_AndTheCreateRefused()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Kind.Should().Be(RecordCreationFailureKind.SecureFilingFailed);
        result.Failure.Detail.Should().Contain("removed again").And.Contain("Try again in a few minutes", "a fault: a retry can succeed");
        World.Deletes.Should().ContainSingle();
        _fixture.IsSecureOf(World.Deletes.Single().Id).Should().BeNull("read back gone");
    }

    /// <summary>
    /// Owner round 31 item 2, Office: the maker could not be shared AND the project could not be removed again — the warning
    /// says exactly that (never "shared to you"); the project stays secure and team-owned, and the job shares it later.
    /// </summary>
    [Fact]
    public async Task OfficeCreate_WhenTheMakerCannotBeSharedNorTheProjectRemoved_WarnsSo()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;
        World.DeletesFail = true;

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeTrue(result.Failure?.Detail);
        result.Warnings.Should().ContainSingle(w => w.Contains("could not be shared to you and could not be removed")
            && w.Contains("shared to you automatically once that step succeeds"), "a fault: the job's retry does share it");
        result.Warnings.Should().NotContain(w => w.Contains("as a secure record shared to you"));
        _fixture.IsSecureOf(result.RecordId).Should().BeTrue();
        _fixture.OwningTeamOf(result.RecordId).Should().Be(SecureTeam);
        _fixture.ShareMaskOf(result.RecordId, Creator).Should().Be(0);
    }

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

    /// <summary>Verifier item 2, Office: the delete answers success but the project survives — warned as existing, never refused as removed.</summary>
    [Fact]
    public async Task OfficeCreate_WhenTheCompensationDeleteAnswersSuccessButTheProjectSurvives_WarnsSo_NeverRefusedAsRemoved()
    {
        var (secure, _) = Matters();
        _fixture.FailShareForPrincipal = Creator;
        World.DeletesIgnored = true;

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeTrue("the project exists: the delete did not remove it");
        result.Warnings.Should().ContainSingle(w => w.Contains("could not be shared to you and could not be removed"));
        World.Deletes.Should().ContainSingle(d => d.Id == result.RecordId);
        World.Has("sprk_project", result.RecordId).Should().BeTrue();
        _fixture.IsSecureOf(result.RecordId).Should().BeTrue();
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

    /// <summary>Verifier item 7, Office: a maker walled off after the plan, the project not removable — no self-heal promised.</summary>
    [Fact]
    public async Task OfficeCreate_WhenTheMakerIsWalledOffAfterThePlan_AndTheProjectCannotBeRemoved_PromisesNoSelfHeal()
    {
        var (secure, _) = Matters();
        World.DeletesFail = true;
        _afterOfficeCreate = _ => _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeTrue(result.Failure?.Detail);
        result.Warnings.Should().ContainSingle(w => w.Contains("could not be shared to you and could not be removed")
            && w.Contains("will not be shared to you automatically"));
        result.Warnings.Should().NotContain(w => w.Contains("automatically once"));
    }

    /// <summary>
    /// Verifier item 7, Office, the removed case: a maker walled off after the plan — the project is removed again, and the
    /// refusal is named instead of "try again in a few minutes" (a retry is refused the same way).
    /// </summary>
    [Fact]
    public async Task OfficeCreate_WhenTheMakerIsWalledOffAfterThePlan_TheProjectIsRemoved_AndNoRetryIsSuggested()
    {
        var (secure, _) = Matters();
        _afterOfficeCreate = _ => _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Kind.Should().Be(RecordCreationFailureKind.SecureFilingFailed);
        result.Failure.Detail.Should().Contain("removed again").And.NotContain("Try again");
        result.Failure.Code.Should().StartWith("sdap.provision.").And.Contain("no_access");
        // Task 158 r1c-v2 (round 47 item 4): one sentence of its own — never provisioning's ProblemDetails text appended
        // (a doubled period, and its "Nothing was changed." beside "Nothing was created.").
        result.Failure.Detail.Should().NotContain("..").And.NotContain("Nothing was changed")
            .And.EndWith("an administrator needs to review your access to the secure record it would be filed under.");
    }

    /// <summary>G5, Office: the caller lacks AppendTo on the secure matter — refused (403 kind), nothing created.</summary>
    [Fact]
    public async Task OfficeCreate_WhenTheCallerCannotFileUnderTheSecureMatter_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _officeProbe.Setup(p => p.GetCallerRightsAsync("caller-token", "sprk_matters", secure, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Kind.Should().Be(RecordCreationFailureKind.SecureFilingRefused);
        result.Failure.Code.Should().Be("caller_cannot_file_under_parent");
        _officeCreates.Should().BeEmpty();
    }

    /// <summary>
    /// G5, Office: the request carries no bearer token, so the caller's own rights cannot be asked — refused (500 kind,
    /// <c>caller_rights_unverifiable</c>), nothing created (ADR-003: never "allowed" because it could not be checked).
    /// </summary>
    [Fact]
    public async Task OfficeCreate_WhenTheCallersRightsCannotBeChecked_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();

        var result = await OfficeCreator(secure, RecordTypeRef(_fixture, "sprk_matter"), withBearerToken: false)
            .CreateAsync(new RecordCreationRequest
            {
                EntityType = QuickCreateEntityType.Project, Name = "Lease review", CallerUserId = "oid",
                OwnerSystemUserId = Creator.ToString("D"), SourceEntityLogicalName = "sprk_matter", SourceRecordId = secure,
            });

        result.Succeeded.Should().BeFalse();
        result.Failure!.Kind.Should().Be(RecordCreationFailureKind.SecureFilingFailed);
        result.Failure.Code.Should().Be(RecordCreationService.CallerRightsUnverifiable);
        _officeCreates.Should().BeEmpty();
    }

    /// <summary>G5, Office: the caller holds no Create on projects — refused (403 kind), nothing created.</summary>
    [Fact]
    public async Task OfficeCreate_WhenTheCallerCannotCreateProjects_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _officeProbe.Setup(p => p.CallerHoldsPrivilegeAsync("caller-token", RecordCreationService.ProjectCreatePrivilege, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Kind.Should().Be(RecordCreationFailureKind.SecureFilingRefused);
        result.Failure.Code.Should().Be(RecordCreationService.CallerCannotCreate);
        _officeCreates.Should().BeEmpty();
    }

    /// <summary>
    /// Task 158 r1: <c>sprk_issecure</c> is the BFF's own column (task 150) — a mapping rule that would copy it onto a project
    /// created under an ORDINARY matter is skipped; the project is created unflagged and ordinary.
    /// </summary>
    [Fact]
    public async Task OfficeCreate_AMappingRuleTargetingTheSecureFlag_IsSkipped()
    {
        var (_, ordinary) = Matters();
        var typeRef = RecordTypeRef(_fixture, "sprk_matter");
        var creator = OfficeCreator(ordinary, typeRef, extraRule: new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "flag", SourceField = "", SourceFieldType = 0, TargetField = "sprk_issecure",
            TargetFieldType = 0, MappingType = 1, ExecutionOrder = 3, DefaultValue = "true", IsActive = true,
        });

        var result = await creator.CreateAsync(new RecordCreationRequest
        {
            EntityType = QuickCreateEntityType.Project, Name = "Lease review", CallerUserId = "oid",
            OwnerSystemUserId = Creator.ToString("D"), SourceEntityLogicalName = "sprk_matter", SourceRecordId = ordinary,
        });

        result.Succeeded.Should().BeTrue(result.Failure?.Detail);
        _officeCreates.Should().ContainSingle().Which.Contains("sprk_issecure").Should().BeFalse("never a value a mapping copies");
        _fixture.IsSecureOf(result.RecordId).Should().BeFalse();
    }

    /// <summary>Owner round 31 item 1, Office: the maker is on the secure matter's No Access list — refused, nothing created.</summary>
    [Fact]
    public async Task OfficeCreate_WhenTheMakerIsOnTheSecureMattersNoAccessList_IsRefused_AndNothingIsCreated()
    {
        var (secure, _) = Matters();
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, secure);

        var result = await OfficeCreateProjectFrom(secure);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Code.Should().Be(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        result.Failure.Kind.Should().Be(RecordCreationFailureKind.SecureFilingRefused);
        result.Failure.Detail.Should().Contain("the No Access list of the secure matter it would be filed under");
        _officeCreates.Should().BeEmpty();
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

    /// <param name="afterPatch">Task 158 r1c-v1: what changes right after the caller's PATCH — between the re-file's pre-check
    /// and the securing that follows it.</param>
    private Task<ToolResult> ChatRefile(Guid workAssignment, Guid matter, Action? afterPatch = null)
    {
        var user = new PatchingUserClient(new SecureChildOwnershipAiToolTests.ScriptedUserClient(Creator), (path, body) =>
        {
            using var doc = JsonDocument.Parse(body);
            ApplyFiling("sprk_workassignment", workAssignment,
                doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()));
            afterPatch?.Invoke();
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
    /// Owner round 31 item 1, chat update: the re-file files the record under a SECURE matter while its pair names a matter
    /// whose flag reads EMPTY — it would be secured (secure-if-any), but its creator cannot be checked against the unreadable
    /// matter's No Access list: refused before the PATCH (<c>creator_no_access_unverifiable</c>).
    /// </summary>
    [Fact]
    public async Task ChatUpdate_UnderASecureMatterWhileItsPairNamesAMatterWhoseFlagCannotBeRead_IsRefusedBeforeThePatch()
    {
        var (secure, ordinary) = Matters();
        var unreadable = Guid.NewGuid();
        _fixture.SeedMatter(unreadable, isSecure: false);
        FlagUnreadable(unreadable);
        var workAssignment = Guid.NewGuid();
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", ordinary);
        FilePair(_fixture, "sprk_workassignment", workAssignment, unreadable, RecordTypeRef(_fixture, "sprk_matter"));

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

    // ── (2) RE-FILE: the playbook output orchestrator's DataverseUpdateHandler ─────────────────────────────────────────

    private Task RefileThroughTheUpdateHandler(Guid workAssignment, Guid matter) =>
        new DataverseUpdateHandler(
                FieldMappingWritingToTheWorld().Object, Mock.Of<IGenericEntityService>(), new StampWorld().Restamper,
                new RecordOwnershipResolverDouble(), _gate, NullLogger<DataverseUpdateHandler>.Instance)
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
            new RecordOwnershipResolverDouble(), _gate, NullLogger<DataverseUpdateHandler>.Instance);

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
            new RecordOwnershipResolverDouble(), SecureRootFilingGateFixtures.Unregistered(), NullLogger<DataverseUpdateHandler>.Instance);
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
}
