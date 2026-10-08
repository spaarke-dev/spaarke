using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Task 174 (owner round 84: "Child access should always follow parent"; closes #1442 and #1425). A work assignment or
/// project is enforced with its EFFECTIVE flags — the most restrictive Secure / Access Permission of itself and every record
/// it is filed under — and a contact walled on a secure parent is denied what is filed below it, at read time and at grant
/// time. The children here are NOT flagged themselves (inheritance pending, Refused or Failed; task 175's cascade not run).
/// </summary>
/// <remarks>Driven through the REAL <see cref="AccessibleRecordSetService"/> (the CIAM contact plane), the REAL filing walk
/// over task 148's in-memory Dataverse world, the REAL deny-list matching (only the reader's wire seam is substituted) and
/// the REAL write-time grant policy. A work assignment is filed by its typed lookup; a project by the polymorphic pair.</remarks>
public class EffectiveAccessFollowsParentTests
{
    private const string Matter = "sprk_matter";
    private const string Project = "sprk_project";
    private const string WorkAssignment = "sprk_workassignment";
    private const int Limited = ExternalParticipationService.AccessPermissionLimited;
    private const int Restricted = ExternalParticipationService.AccessPermissionRestricted;

    private static readonly Guid Contact = Guid.Parse("17417417-0000-0000-0000-0000000000a1");
    private static readonly Guid ParentMatter = Guid.Parse("17417417-0000-0000-0000-0000000000b1");
    private static readonly Guid OtherMatter = Guid.Parse("17417417-0000-0000-0000-0000000000b2");
    private static readonly Guid MiddleProject = Guid.Parse("17417417-0000-0000-0000-0000000000c1");
    private static readonly Guid ByOrgGrant = Guid.Parse("17417417-0000-0000-0000-0000000000d1");
    private static readonly Guid ByDirectGrant = Guid.Parse("17417417-0000-0000-0000-0000000000d2");
    private static readonly Guid Sibling = Guid.Parse("17417417-0000-0000-0000-0000000000d3");
    private static readonly Guid Firm = Guid.Parse("17417417-0000-0000-0000-0000000000e1");
    private static readonly Guid ProjectType = Guid.Parse("17417417-0000-0000-0000-0000000000f1");
    private static readonly Guid MatterType = Guid.Parse("17417417-0000-0000-0000-0000000000f2");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("sprk_recordtype_ref", MatterType, ("sprk_recordlogicalname", Matter))
        .Add("sprk_recordtype_ref", ProjectType, ("sprk_recordlogicalname", Project));

    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyList = new();
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _participations;

    /// <summary>When set, any query whose id condition names this row throws (a fault on that row alone).</summary>
    private (string Table, Guid Id)? _failingRow;

    public EffectiveAccessFollowsParentTests()
    {
        _participations = new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None, Entities());
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────────────────────────

    private void MatterRow(Guid id, bool secure, int? permission = null)
    {
        var columns = new List<(string, object)> { ("sprk_issecure", secure), ("sprk_mattername", "Matter " + id.ToString("N")[..4]) };
        if (permission is { } p)
            columns.Add(("sprk_accesspermission", new OptionSetValue(p)));
        _world.Add(Matter, id, columns.ToArray());
        _participations.Flags[id] = new RootRecordFlags(secure, permission == Restricted, permission == Limited);
    }

    /// <summary>A work assignment filed under a matter or project, NOT secure and Standard itself (its own row says so).</summary>
    private void OpenWaUnder(Guid wa, string parentTable, Guid parentId)
    {
        _world.Add(WorkAssignment, wa,
            ("sprk_issecure", false),
            (parentTable == Matter ? "sprk_regardingmatter" : "sprk_regardingproject", new EntityReference(parentTable, parentId)));
        _participations.Flags[wa] = RootRecordFlags.None;
    }

    /// <summary>A work assignment filed under nothing, not secure, Standard.</summary>
    private void ParentlessWa(Guid wa)
    {
        _world.Add(WorkAssignment, wa, ("sprk_issecure", false));
        _participations.Flags[wa] = RootRecordFlags.None;
    }

    /// <summary>A non-secure, Standard project filed under a matter by the pair.</summary>
    private void OpenProjectUnderMatter(Guid project, Guid matter)
    {
        _world.Add(Project, project,
            ("sprk_issecure", false),
            ("sprk_projectname", "Middle"),
            ("sprk_regardingrecordid", matter.ToString("D")),
            ("sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", MatterType)));
        _participations.Flags[project] = RootRecordFlags.None;
    }

    /// <summary>The contact holds an ORGANIZATION-inherited Collaborate grant on <see cref="ByOrgGrant"/> (no direct level)
    /// and a DIRECT, named View Only grant on <see cref="ByDirectGrant"/>, plus <paramref name="more"/> direct grants.</summary>
    private void ContactGrants(params Guid[] more)
    {
        var grants = new List<ExternalRootGrant>
        {
            new() { RecordId = ByOrgGrant, AccessLevel = ExternalAccessLevel.Collaborate, DirectAccessLevel = null },
            new() { RecordId = ByDirectGrant, AccessLevel = ExternalAccessLevel.ViewOnly, DirectAccessLevel = ExternalAccessLevel.ViewOnly },
        };
        grants.AddRange(more.Select(id => new ExternalRootGrant
        {
            RecordId = id, AccessLevel = ExternalAccessLevel.ViewOnly, DirectAccessLevel = ExternalAccessLevel.ViewOnly,
        }));
        _participations.GrantSets[Contact] = new ExternalGrantSet
        {
            Projects = Array.Empty<ExternalParticipation>(),
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = grants,
        };
    }

    private IGenericEntityService Entities()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (_failingRow is { } failing && query.EntityName == failing.Table && query.Criteria.Conditions.Any(c =>
                        c.AttributeName == query.EntityName + "id" && c.Values.OfType<Guid>().Contains(failing.Id)))
                {
                    throw new InvalidOperationException($"Test: this {query.EntityName} row cannot be read.");
                }

                return Task.FromResult(_world.Answer(query));
            });
        return entities.Object;
    }

    private AccessibleRecordSetService Service() => new(
        Mock.Of<IMembershipResolverService>(), _participations, Mock.Of<ISubjectStandingGrantReader>(), _denyList,
        UnlinkedIdentityStore(), InternalSystemUsers(), Entities(), NullLogger<AccessibleRecordSetService>.Instance);

    /// <summary>The external SPA's composition for <see cref="Contact"/> (the CIAM contact plane).</summary>
    private Task<AccessibleRecordSet> ComposeAsync(string entityType = WorkAssignment) =>
        Service().ComposeForCiamContactAsync(Contact, entityType, CancellationToken.None);

    // ── Read path: cancellation follows the parent (#1442) ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "174: a non-secure Standard work assignment under a SECURE matter admits only the named direct grant")]
    public async Task ReadPath_ChildOfASecureMatter_AdmitsOnlyNamedDirectGrants()
    {
        MatterRow(ParentMatter, secure: true);
        OpenWaUnder(ByOrgGrant, Matter, ParentMatter);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        ContactGrants();

        var set = await ComposeAsync();

        set.Rights.Should().NotContainKey(ByOrgGrant, "the parent is secure, so an organization-wide grant confers nothing on the child");
        set.Rights.Should().ContainKey(ByDirectGrant, "a named, direct grant still counts on a secure record");
    }

    [Fact(DisplayName = "174: the same children under a non-secure Standard matter are unchanged (both grants confer)")]
    public async Task ReadPath_ChildOfAStandardMatter_IsUnchanged()
    {
        MatterRow(ParentMatter, secure: false);
        OpenWaUnder(ByOrgGrant, Matter, ParentMatter);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        ContactGrants();

        var set = await ComposeAsync();

        set.Rights.Should().ContainKey(ByOrgGrant);
        set.Rights.Should().ContainKey(ByDirectGrant);
    }

    [Fact(DisplayName = "174: a child under a RESTRICTED matter confers no contact-sourced access, direct grants included")]
    public async Task ReadPath_ChildOfARestrictedMatter_ConfersNothingContactSourced()
    {
        MatterRow(ParentMatter, secure: false, permission: Restricted);
        OpenWaUnder(ByOrgGrant, Matter, ParentMatter);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        ContactGrants();

        var set = await ComposeAsync();

        set.Rights.Should().BeEmpty("Restricted removes every contact-sourced contribution, as on the parent");
    }

    [Fact(DisplayName = "174: a child filed through a NON-secure project under a secure matter is treated as Secure")]
    public async Task ReadPath_ChildThroughANonSecureProject_IsTreatedAsSecure()
    {
        MatterRow(ParentMatter, secure: true);
        OpenProjectUnderMatter(MiddleProject, ParentMatter);
        OpenWaUnder(ByOrgGrant, Project, MiddleProject);
        OpenWaUnder(ByDirectGrant, Project, MiddleProject);
        ContactGrants();

        var set = await ComposeAsync();

        set.Rights.Should().NotContainKey(ByOrgGrant, "secure-if-any, at any depth of filing");
        set.Rights.Should().ContainKey(ByDirectGrant);
    }

    // ── Contact plane: the secure parent's No Access list (#1425) ──────────────────────────────────────────────────

    [Fact(DisplayName = "174: a contact walled on secure matter M is denied a work assignment filed under M; a wall elsewhere does nothing")]
    public async Task ReadPath_ContactWalledOnTheSecureParent_IsDeniedTheChild()
    {
        MatterRow(ParentMatter, secure: true);
        MatterRow(OtherMatter, secure: true);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        OpenWaUnder(Sibling, Matter, OtherMatter);
        ContactGrants(Sibling);
        _denyList.DenyContactOnRecord(Contact, ParentMatter);

        var set = await ComposeAsync();

        set.Rights.Should().NotContainKey(ByDirectGrant, "the parent's list binds what is filed below it (round 83 item 10)");
        set.Rights.Should().ContainKey(Sibling, "a wall on an unrelated record does not reach this one");
    }

    [Fact(DisplayName = "174: an organization wall on the secure parent reaches its members through membership")]
    public async Task ReadPath_OrganizationWalledOnTheSecureParent_DeniesItsMember()
    {
        MatterRow(ParentMatter, secure: true);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        ContactGrants();
        _participations.ContactOrganizations[Contact] = new[] { Firm };
        _denyList.DenyOrganizationOnRecord(Firm, ParentMatter);

        var set = await ComposeAsync();

        set.Rights.Should().NotContainKey(ByDirectGrant);
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "174: an unreadable filing chain or ancestor flag removes the child; an unrelated record stays")]
    [InlineData(WorkAssignment)] // the child's own filing row
    [InlineData(Matter)]         // the parent's flag row
    public async Task ReadPath_UnreadableChain_RemovesTheChild(string failingTable)
    {
        MatterRow(ParentMatter, secure: false);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        ParentlessWa(Sibling);
        ContactGrants(Sibling);
        _failingRow = failingTable == Matter ? (Matter, ParentMatter) : (WorkAssignment, ByDirectGrant);

        var set = await ComposeAsync();

        set.Rights.Should().NotContainKey(ByDirectGrant, "what governs it is unknown: secure AND Restricted (ADR-003)");
        if (failingTable == Matter)
            set.Rights.Should().ContainKey(Sibling, "a record filed under nothing needs no parent read");
    }

    [Fact(DisplayName = "174: a secure parent's referenced organizations cannot be read -> the child is removed; an unfiled one stays")]
    public async Task ReadPath_UnreadableAncestorOrganizations_RemovesTheChild()
    {
        MatterRow(ParentMatter, secure: true);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        ParentlessWa(Sibling);
        ContactGrants(Sibling);
        _participations.UnreadableReferencedOrganizations[ParentMatter] = true;

        var set = await ComposeAsync();

        set.Rights.Should().NotContainKey(ByDirectGrant);
        set.Rights.Should().ContainKey(Sibling);
    }

    [Fact(DisplayName = "174: a deny-list fault removes the child below a secure parent (the existing all-candidates posture)")]
    public async Task ReadPath_DenyListFault_RemovesTheChild()
    {
        MatterRow(ParentMatter, secure: true);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        ContactGrants();
        _denyList.Faults = true;

        var set = await ComposeAsync();

        set.Rights.Should().NotContainKey(ByDirectGrant);
    }

    // ── Cost pin (goal 6) ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "174 cost pin: a matter composition reads no filing")]
    public async Task Cost_MatterComposition_ReadsNoFiling()
    {
        MatterRow(ParentMatter, secure: false);
        _participations.GrantSets[Contact] = new ExternalGrantSet
        {
            Projects = Array.Empty<ExternalParticipation>(),
            MatterGrants = new[]
            {
                new ExternalRootGrant { RecordId = ParentMatter, AccessLevel = ExternalAccessLevel.ViewOnly, DirectAccessLevel = ExternalAccessLevel.ViewOnly },
            },
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        };

        var set = await ComposeAsync(Matter);

        set.Rights.Should().ContainKey(ParentMatter);
        _world.QueriedTables.Should().BeEmpty("a matter files under nothing");
        _denyList.Queries.Should().Be(1, "the candidate's own list only, as before");
    }

    [Fact(DisplayName = "174 cost pin: parentless work assignments cost one batched row read, and nothing else changes")]
    public async Task Cost_ParentlessChildren_CostOneRowRead()
    {
        ParentlessWa(ByOrgGrant);
        ParentlessWa(ByDirectGrant);
        ParentlessWa(Sibling);
        ContactGrants(Sibling);

        var set = await ComposeAsync();

        set.Rights.Keys.Should().BeEquivalentTo(new[] { ByOrgGrant, ByDirectGrant, Sibling }, "identical to before: nothing is above them");
        _world.QueriedTables.Should().Equal(new[] { WorkAssignment }, "the walk's one batched row read; no parent or organization read");
        _denyList.Queries.Should().Be(1, "the same one deny-list query as before");
    }

    // ── Grant time ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "174: a grant to a contact walled on the secure parent is refused; a wall elsewhere allows it")]
    public async Task GrantTime_ContactWalledOnTheSecureParent_IsDenied()
    {
        MatterRow(ParentMatter, secure: true);
        MatterRow(OtherMatter, secure: true);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        OpenWaUnder(Sibling, Matter, OtherMatter);
        _denyList.DenyContactOnRecord(Contact, ParentMatter);

        var walled = await Service().CheckGranteeNoAccessAsync(WorkAssignment, ByDirectGrant, Contact, Array.Empty<Guid>(), CancellationToken.None);
        var elsewhere = await Service().CheckGranteeNoAccessAsync(WorkAssignment, Sibling, Contact, Array.Empty<Guid>(), CancellationToken.None);

        walled.Should().Be(NoAccessCheckAnswer.Denied);
        elsewhere.Should().Be(NoAccessCheckAnswer.Allowed);
    }

    [Fact(DisplayName = "174: the grantee check cannot read what the record is filed under -> Unverifiable (refused as a fault)")]
    public async Task GrantTime_UnreadableChain_IsUnverifiable()
    {
        MatterRow(ParentMatter, secure: true);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        _failingRow = (Matter, ParentMatter);

        var answer = await Service().CheckGranteeNoAccessAsync(WorkAssignment, ByDirectGrant, Contact, Array.Empty<Guid>(), CancellationToken.None);

        answer.Should().Be(NoAccessCheckAnswer.Unverifiable);
    }

    [Fact(DisplayName = "174: a contact grant on a child of a Restricted matter is refused with the Restricted refusal")]
    public async Task GrantTime_ChildOfARestrictedMatter_RefusesTheContactGrant()
    {
        MatterRow(ParentMatter, secure: false, permission: Restricted);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);

        var decision = await ExternalGrantLifecycle.EvaluateGrantPolicyAsync(
            _participations, ExternalGrantRootType.WorkAssignment, ByDirectGrant, GrantGranteeKind.Contact,
            NullLogger.Instance, CancellationToken.None);

        decision.Should().Be(GrantPolicyDecision.Restricted);
    }

    [Theory(DisplayName = "174: an organization-wide grant on a child of a secure or Limited matter is refused; Standard allows it")]
    [InlineData(true, null, false)]
    [InlineData(false, Limited, false)]
    [InlineData(false, null, true)]
    public async Task GrantTime_OrganizationGrant_FollowsTheParent(bool parentSecure, int? parentPermission, bool allowed)
    {
        MatterRow(ParentMatter, parentSecure, parentPermission);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);

        var decision = await ExternalGrantLifecycle.EvaluateGrantPolicyAsync(
            _participations, ExternalGrantRootType.WorkAssignment, ByDirectGrant, GrantGranteeKind.Organization,
            NullLogger.Instance, CancellationToken.None);

        decision.IsAllowed.Should().Be(allowed);
        if (!allowed)
            decision.Should().Be(GrantPolicyDecision.OrgGrantOnDirectOnly(parentSecure));
    }

    [Fact(DisplayName = "174: the grant policy cannot read the child's filing -> Unreadable (503), never 'Restricted'")]
    public async Task GrantTime_UnreadableChain_IsUnreadable()
    {
        MatterRow(ParentMatter, secure: false);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);
        _failingRow = (WorkAssignment, ByDirectGrant);

        var decision = await ExternalGrantLifecycle.EvaluateGrantPolicyAsync(
            _participations, ExternalGrantRootType.WorkAssignment, ByDirectGrant, GrantGranteeKind.Contact,
            NullLogger.Instance, CancellationToken.None);

        decision.Should().Be(GrantPolicyDecision.Unreadable);
    }

    // ── Display contract (goal 5) ──────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "174: the effective access read reports the parent's Restricted and secure state, and the parent it follows")]
    public async Task Display_EffectiveAccess_NamesTheGoverningParent()
    {
        MatterRow(ParentMatter, secure: true, permission: Restricted);
        OpenWaUnder(ByDirectGrant, Matter, ParentMatter);

        var access = await _participations.GetEffectiveRootAccessAsync(WorkAssignment, ByDirectGrant, CancellationToken.None);

        access.Flags.IsSecure.Should().BeTrue();
        access.Flags.IsRestricted.Should().BeTrue();
        access.InheritedFrom.Should().NotBeNull();
        access.InheritedFrom!.Table.Should().Be(Matter);
        access.InheritedFrom.Id.Should().Be(ParentMatter);
    }
}
