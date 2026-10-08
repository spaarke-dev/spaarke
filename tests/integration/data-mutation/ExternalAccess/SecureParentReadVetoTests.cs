using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// GitHub #1410 — round 61 item 1 (class a, security) on the Teams/SPA READ path: a No Access entry on a secure matter or
/// project reaches every secure work assignment and project filed below it, at any depth, in the systemuser-plane veto —
/// as it already does at share time (<see cref="SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync(string, Guid, Guid, SecureWallRecordScope, CancellationToken)"/>)
/// and in the enforcer. Before the fix the veto built each candidate from its OWN id and organizations only, so a person
/// walled off a secure matter who still reached a secure work assignment under it through a team saw it in Teams/SPA.
/// </summary>
/// <remarks>Driven through the REAL <see cref="AccessibleRecordSetService"/>, the REAL parent walk
/// (<see cref="SecureRootInheritance"/>) over task 148's in-memory Dataverse world, and the REAL deny-list matching
/// (only the reader's wire seam is substituted). A work assignment is filed under a matter or project by its typed lookup;
/// a project under a matter or project by the polymorphic pair.</remarks>
public class SecureParentReadVetoTests
{
    private const string Matter = "sprk_matter";
    private const string Project = "sprk_project";
    private const string WorkAssignment = "sprk_workassignment";

    private static readonly Guid Walled = Guid.Parse("14101410-1410-1410-1410-1410141014a1");
    private static readonly Guid SecureMatter = Guid.Parse("14101410-1410-1410-1410-1410141014b1");
    private static readonly Guid OtherSecureMatter = Guid.Parse("14101410-1410-1410-1410-1410141014b2");
    private static readonly Guid OpenMatter = Guid.Parse("14101410-1410-1410-1410-1410141014b3");
    private static readonly Guid MiddleProject = Guid.Parse("14101410-1410-1410-1410-1410141014c1");
    private static readonly Guid ChildWa = Guid.Parse("14101410-1410-1410-1410-1410141014d1");
    private static readonly Guid ControlWa = Guid.Parse("14101410-1410-1410-1410-1410141014d2");
    private static readonly Guid OpenWa = Guid.Parse("14101410-1410-1410-1410-1410141014d3");
    private static readonly Guid Organization = Guid.Parse("14101410-1410-1410-1410-1410141014e1");
    private static readonly Guid MatterType = Guid.Parse("14101410-1410-1410-1410-1410141014f1");
    private static readonly Guid ProjectType = Guid.Parse("14101410-1410-1410-1410-1410141014f2");

    private static readonly RootRecordFlags SecureFlags = new(IsSecure: true, IsRestricted: false);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("sprk_recordtype_ref", MatterType, ("sprk_recordlogicalname", Matter))
        .Add("sprk_recordtype_ref", ProjectType, ("sprk_recordlogicalname", Project));

    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _participations =
        new(defaultFlags: RootRecordFlags.None);

    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _denyList = new();

    /// <summary>When set, any query whose id condition (Equal or In) names this row throws — a fault on that row alone.</summary>
    private (string Table, Guid Id)? _failingRow;

    /// <summary>How many queries the failing row refused (they never reach the world's own count).</summary>
    private int _refusedReads;

    /// <summary>The principal's derived contact (the contact axis), when a test gives it one.</summary>
    private Guid? _principalContact;

    /// <summary>The link store the veto reads; the unlinked default unless a test records the reads.</summary>
    private IContactIdentityStore? _identities;

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────────────────────────

    private void SecureMatterRow(Guid id) => _world.SecureRoot(Matter, id);

    /// <summary>A secure work assignment filed under <paramref name="parentTable"/> by its typed lookup (the candidate's
    /// flag comes from the participation read, as in production).</summary>
    private void SecureWaUnder(Guid wa, string parentTable, Guid parentId)
    {
        _world.Add(WorkAssignment, wa,
            ("owningteam", new EntityReference("team", SecureChildShareWorld.SecureTeam)),
            ("sprk_issecure", true),
            (parentTable == Matter ? "sprk_regardingmatter" : "sprk_regardingproject", new EntityReference(parentTable, parentId)));
        _participations.Flags[wa] = SecureFlags;
    }

    /// <summary>A work assignment filed under <paramref name="parentTable"/> by its typed lookup that is NOT secure (yet):
    /// inheritance pending, or ended Refused or Failed (owner round 82: the parent's list governs it all the same).</summary>
    private void OpenWaUnder(Guid wa, string parentTable, Guid parentId)
    {
        _world.Add(WorkAssignment, wa,
            ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
            ("sprk_issecure", false),
            (parentTable == Matter ? "sprk_regardingmatter" : "sprk_regardingproject", new EntityReference(parentTable, parentId)));
        _participations.Flags[wa] = RootRecordFlags.None;
    }

    /// <summary>A project filed under <paramref name="parentTable"/> <paramref name="parentId"/> by the pair.</summary>
    private void ProjectUnder(Guid project, string parentTable, Guid parentId, bool secure = true)
    {
        _world.Add(Project, project,
            ("owningteam", new EntityReference("team", SecureChildShareWorld.SecureTeam)),
            ("sprk_issecure", secure),
            ("sprk_regardingrecordid", parentId.ToString("D")),
            ("sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", parentTable == Matter ? MatterType : ProjectType)));
        _participations.Flags[project] = secure ? SecureFlags : RootRecordFlags.None;
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
                    _refusedReads++;
                    throw new InvalidOperationException($"Test: this {query.EntityName} row cannot be read.");
                }
                return Task.FromResult(_world.Answer(query));
            });
        return entities.Object;
    }

    /// <summary>Composes <paramref name="entityType"/> for <see cref="Walled"/>, who reaches <paramref name="reached"/>
    /// through Dataverse (a team, a role, the business unit — the membership term).</summary>
    private Task<AccessibleRecordSet> ComposeAsync(string entityType, params Guid[] reached)
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(Walled, entityType, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipResponse(
                entityType, new PersonIdentity(Guid.Empty, ContactId: null), reached,
                new Dictionary<string, IReadOnlyList<Guid>>(), reached.Length, DateTimeOffset.UtcNow.AddMinutes(5)));

        var sut = new AccessibleRecordSetService(
            membership.Object, _participations, Mock.Of<ISubjectStandingGrantReader>(), _denyList,
            _identities ?? UnlinkedIdentityStore(), InternalSystemUsers(), Entities(), NullLogger<AccessibleRecordSetService>.Instance);

        return sut.ComposeAsync(new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = Walled,
            ContactId = _principalContact,
            Oid = Guid.NewGuid().ToString("D"),
            TenantId = "14101410-0000-0000-0000-000000000000",
        }, entityType, CancellationToken.None);
    }

    // ── The rule ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1410: a matter's entry hides a secure work assignment filed under it on Teams/SPA; a sibling under another matter stays")]
    public async Task AMattersEntry_HidesTheSecureWorkAssignmentFiledUnderIt()
    {
        SecureMatterRow(SecureMatter);
        SecureMatterRow(OtherSecureMatter);
        SecureWaUnder(ChildWa, Matter, SecureMatter);
        SecureWaUnder(ControlWa, Matter, OtherSecureMatter);
        _denyList.DenySystemUserOnRecord(Walled, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, ChildWa, ControlWa);

        set.Rights.Should().NotContainKey(ChildWa, "the wall on the secure matter reaches every secure record filed below it");
        set.Rights.Should().ContainKey(ControlWa, "a record filed under a matter the person is not walled off is untouched");
    }

    [Fact(DisplayName = "#1410: a matter's entry hides a work assignment two levels below it, and the project between")]
    public async Task AMattersEntry_HidesTheGrandchild_AndTheProjectBetween()
    {
        SecureMatterRow(SecureMatter);
        ProjectUnder(MiddleProject, Matter, SecureMatter);
        SecureWaUnder(ChildWa, Project, MiddleProject);
        _denyList.DenySystemUserOnRecord(Walled, SecureMatter);

        var workAssignments = await ComposeAsync(WorkAssignment, ChildWa);
        var projects = await ComposeAsync(Project, MiddleProject);

        workAssignments.Rights.Should().NotContainKey(ChildWa, "the wall reaches every level below the matter (round 61 item 1)");
        projects.Rights.Should().NotContainKey(MiddleProject);
    }

    [Fact(DisplayName = "#1410: an entry naming an organization the secure PARENT references hides the child (the parent's own list)")]
    public async Task AnEntryOnAnOrganizationTheParentReferences_HidesTheChild()
    {
        SecureMatterRow(SecureMatter);
        SecureWaUnder(ChildWa, Matter, SecureMatter);
        _participations.RecordOrganizations[SecureMatter] = new[] { Organization }; // the work assignment references none
        _denyList.DenySystemUserOnOrganization(Walled, Organization);

        var set = await ComposeAsync(WorkAssignment, ChildWa);

        set.Rights.Should().NotContainKey(ChildWa,
            "a secure parent's list is every entry naming it or an organization it references, as the share-time guard asks it");
    }

    [Fact(DisplayName = "#1410: a NON-secure parent's entry does not reach the record filed under it")]
    public async Task ANonSecureParentsEntry_DoesNotReachTheChild()
    {
        _world.OrdinaryRoot(Matter, OpenMatter);
        SecureWaUnder(ChildWa, Matter, OpenMatter);
        _denyList.DenySystemUserOnRecord(Walled, OpenMatter);

        var set = await ComposeAsync(WorkAssignment, ChildWa);

        set.Rights.Should().ContainKey(ChildWa, "only a SECURE matter's or project's list reaches what is filed below it");
    }

    [Fact(DisplayName = "#1410: the climb goes THROUGH a non-secure project to the secure matter above it")]
    public async Task TheClimbGoesThroughANonSecureProject()
    {
        SecureMatterRow(SecureMatter);
        ProjectUnder(MiddleProject, Matter, SecureMatter, secure: false);
        SecureWaUnder(ChildWa, Project, MiddleProject);
        _denyList.DenySystemUserOnRecord(Walled, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, ChildWa);

        set.Rights.Should().NotContainKey(ChildWa, "a filing is a filing, secure or not: the secure matter above still applies");
    }

    [Fact(DisplayName = "Round 82: a NOT-yet-secure work assignment under a walled secure matter is removed (the parent permissions control)")]
    public async Task ANonSecureCandidate_UnderAWalledSecureParent_IsRemoved()
    {
        SecureMatterRow(SecureMatter);
        OpenWaUnder(OpenWa, Matter, SecureMatter);
        _denyList.DenySystemUserOnRecord(Walled, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, OpenWa);

        set.Rights.Should().NotContainKey(OpenWa, "a record filed under a secure parent is governed by its list whatever its own flag reads");
    }

    [Theory(DisplayName = "Round 82: a contact- or organization-subject entry on the secure parent removes a not-yet-secure child WHOLE")]
    [InlineData("contact")]
    [InlineData("organization")]
    public async Task AContactOrOrganizationWallOnTheParent_RemovesTheNonSecureChildWhole(string subject)
    {
        var contact = Guid.Parse("14101410-1410-1410-1410-1410141014a2");
        _principalContact = contact;
        _participations.GrantSets[contact] = new ExternalGrantSet
        {
            Projects = Array.Empty<ExternalParticipation>(),
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        };
        _participations.ContactOrganizations[contact] = new[] { Organization };
        SecureMatterRow(SecureMatter);
        OpenWaUnder(OpenWa, Matter, SecureMatter);
        if (subject == "contact")
            _denyList.DenyContactOnRecord(contact, SecureMatter);
        else
            _denyList.DenyOrganizationOnRecord(Organization, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, OpenWa);

        set.Rights.Should().NotContainKey(OpenWa,
            "the parent's wall removes the record whole — membership term included — not only the contact's contribution (N3 is the child's OWN list)");
    }

    [Fact(DisplayName = "Round 82: a not-yet-secure child reached THROUGH a non-secure project to a walled secure matter is removed")]
    public async Task ANonSecureChild_ThroughANonSecureProject_IsRemoved()
    {
        SecureMatterRow(SecureMatter);
        ProjectUnder(MiddleProject, Matter, SecureMatter, secure: false);
        OpenWaUnder(OpenWa, Project, MiddleProject);
        _denyList.DenySystemUserOnRecord(Walled, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, OpenWa);

        set.Rights.Should().NotContainKey(OpenWa);
    }

    [Fact(DisplayName = "Round 82: a non-secure child under a NON-secure matter whose wall would match stays")]
    public async Task ANonSecureChild_UnderANonSecureMatter_Stays()
    {
        _world.OrdinaryRoot(Matter, OpenMatter);
        OpenWaUnder(OpenWa, Matter, OpenMatter);
        _denyList.DenySystemUserOnRecord(Walled, OpenMatter);

        var set = await ComposeAsync(WorkAssignment, OpenWa);

        set.Rights.Should().ContainKey(OpenWa, "only a SECURE parent's list reaches what is filed below it");
    }

    [Fact(DisplayName = "Round 82 / Q4: a systemuser entry on the non-secure child ITSELF (no secure parent) removes nothing")]
    public async Task ASystemUserEntryOnTheNonSecureChildItself_RemovesNothing()
    {
        _world.OrdinaryRoot(Matter, OpenMatter);
        OpenWaUnder(OpenWa, Matter, OpenMatter);
        _denyList.DenySystemUserOnRecord(Walled, OpenWa);

        var set = await ComposeAsync(WorkAssignment, OpenWa);

        set.Rights.Should().ContainKey(OpenWa, "the internal wall binds secure records; the child's own list is unchanged (Q4)");
    }

    [Fact(DisplayName = "Round 82: a not-yet-secure child whose parent cannot be read is removed; an unfiled sibling stays")]
    public async Task ANonSecureChildsFilingFault_RemovesIt_AndLeavesAnUnaffectedSibling()
    {
        SecureMatterRow(SecureMatter);
        OpenWaUnder(OpenWa, Matter, SecureMatter);
        _world.Add(WorkAssignment, ControlWa, ("sprk_issecure", false)); // filed under nothing
        _failingRow = (Matter, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, OpenWa, ControlWa);

        set.Rights.Should().NotContainKey(OpenWa, "whose list governs it is unknown: fail closed (ADR-003)");
        set.Rights.Should().ContainKey(ControlWa);
    }

    [Fact(DisplayName = "Round 82: a secure and a not-yet-secure child under the walled matter are both removed, in ONE walk")]
    public async Task AMixedBatch_UnderAWalledMatter_IsRemovedInOneWalk()
    {
        SecureMatterRow(SecureMatter);
        SecureWaUnder(ChildWa, Matter, SecureMatter);
        OpenWaUnder(OpenWa, Matter, SecureMatter);
        _denyList.DenySystemUserOnRecord(Walled, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, ChildWa, OpenWa);

        set.Rights.Should().NotContainKey(ChildWa);
        set.Rights.Should().NotContainKey(OpenWa);
        _world.QueriedTables.Count(t => t == WorkAssignment).Should().Be(1, "both rows are read in one chunk");
        _world.QueriedTables.Count(t => t == Matter).Should().Be(1, "the matter's flag is read once");
    }

    [Fact(DisplayName = "#1410: a project composition where an ancestor is ALSO a candidate — both removed, the ancestor asked once")]
    public async Task AProjectComposition_WhereAnAncestorIsAlsoACandidate()
    {
        SecureMatterRow(SecureMatter);
        ProjectUnder(MiddleProject, Matter, SecureMatter);
        var lower = Guid.Parse("14101410-1410-1410-1410-1410141014c3");
        ProjectUnder(lower, Project, MiddleProject);
        _denyList.DenySystemUserOnRecord(Walled, MiddleProject);

        var set = await ComposeAsync(Project, MiddleProject, lower);

        set.Rights.Should().NotContainKey(MiddleProject, "its own entry");
        set.Rights.Should().NotContainKey(lower, "the project above it is walled");
    }

    [Fact(DisplayName = "#1410: a matter composition reads no filing (a matter files under nothing) — unchanged")]
    public async Task AMatterComposition_ReadsNoFiling()
    {
        SecureMatterRow(SecureMatter);
        _participations.Flags[SecureMatter] = SecureFlags;

        var set = await ComposeAsync(Matter, SecureMatter);

        set.Rights.Should().ContainKey(SecureMatter);
        _world.QueriedTables.Should().BeEmpty();
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#1410: the parent's flag cannot be read -> the secure child is removed; an unfiled secure sibling stays")]
    public async Task AParentReadFault_RemovesTheChild()
    {
        SecureMatterRow(SecureMatter);
        SecureWaUnder(ChildWa, Matter, SecureMatter);
        _world.Add(WorkAssignment, ControlWa, ("sprk_issecure", true)); // secure, filed under nothing
        _participations.Flags[ControlWa] = SecureFlags;
        _failingRow = (Matter, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, ChildWa, ControlWa);

        set.Rights.Should().NotContainKey(ChildWa, "whose list applies is unknown: the veto fails closed (NFR-01)");
        set.Rights.Should().ContainKey(ControlWa, "a record filed under nothing needs no parent read");
    }

    [Fact(DisplayName = "#1410: the candidate's own filing cannot be read -> removed")]
    public async Task ACandidateFilingFault_RemovesIt()
    {
        SecureMatterRow(SecureMatter);
        SecureWaUnder(ChildWa, Matter, SecureMatter);
        _failingRow = (WorkAssignment, ChildWa);

        var set = await ComposeAsync(WorkAssignment, ChildWa);

        set.Rights.Should().NotContainKey(ChildWa);
    }

    [Fact(DisplayName = "#1410: a batched filing read that faults removes every secure candidate it covered")]
    public async Task ABatchedFilingFault_RemovesEveryCandidateInTheChunk()
    {
        SecureMatterRow(SecureMatter);
        SecureWaUnder(ChildWa, Matter, SecureMatter);
        _world.Add(WorkAssignment, ControlWa, ("sprk_issecure", true));
        _participations.Flags[ControlWa] = SecureFlags;
        _failingRow = (WorkAssignment, ControlWa); // one id in a two-id chunk: the whole chunk read faults

        var set = await ComposeAsync(WorkAssignment, ChildWa, ControlWa);

        set.Rights.Should().NotContainKey(ChildWa);
        set.Rights.Should().NotContainKey(ControlWa);
    }

    [Fact(DisplayName = "#1410: a parent whose secure flag is EMPTY -> the child is removed (empty is never 'not secure')")]
    public async Task AnEmptyParentFlag_RemovesTheChild()
    {
        _world.Add(Matter, SecureMatter, ("owningteam", new EntityReference("team", SecureChildShareWorld.SecureTeam)));
        SecureWaUnder(ChildWa, Matter, SecureMatter);

        var set = await ComposeAsync(WorkAssignment, ChildWa);

        set.Rights.Should().NotContainKey(ChildWa);
    }

    [Fact(DisplayName = "#1410: the secure parent's referenced organizations cannot be read -> the child is removed")]
    public async Task AnUnreadableAncestorOrganizationSet_RemovesTheChild()
    {
        SecureMatterRow(SecureMatter);
        SecureWaUnder(ChildWa, Matter, SecureMatter);
        _participations.UnreadableReferencedOrganizations[SecureMatter] = true;

        var set = await ComposeAsync(WorkAssignment, ChildWa);

        set.Rights.Should().NotContainKey(ChildWa);
    }

    [Fact(DisplayName = "#1410: a filing chain longer than the bound -> removed (never 'no more parents')")]
    public async Task AChainPastTheBound_RemovesTheCandidate()
    {
        // WA -> P1 -> P2 -> P3 -> P4 -> P5 -> matter: the climb stops at MaxFilingDepth with a row still filed.
        SecureMatterRow(SecureMatter);
        var chain = Enumerable.Range(1, SecureRootInheritance.MaxFilingDepth + 1)
            .Select(i => Guid.Parse($"14101410-1410-1410-1410-14101410{i:D4}")).ToArray();
        ProjectUnder(chain[^1], Matter, SecureMatter);
        for (var i = chain.Length - 2; i >= 0; i--)
            ProjectUnder(chain[i], Project, chain[i + 1]);
        SecureWaUnder(ChildWa, Project, chain[0]);

        var set = await ComposeAsync(WorkAssignment, ChildWa);

        set.Rights.Should().NotContainKey(ChildWa, "a chain past the bound is unverifiable, and the veto fails closed");
    }

    [Fact(DisplayName = "#1410: a filing cycle ends the climb; an entry on a secure project in the cycle still applies")]
    public async Task ACycle_EndsTheClimb_AndTheWallApplies()
    {
        var other = Guid.Parse("14101410-1410-1410-1410-1410141014c2");
        ProjectUnder(MiddleProject, Project, other);
        ProjectUnder(other, Project, MiddleProject);
        _denyList.DenySystemUserOnRecord(Walled, other);

        var set = await ComposeAsync(Project, MiddleProject);

        set.Rights.Should().NotContainKey(MiddleProject);
    }

    [Fact(DisplayName = "#1410: one chunk's filing fault removes that chunk only; the other chunk is decided")]
    public async Task OneChunksFault_LeavesTheOtherChunkAlone()
    {
        var candidates = new List<Guid>();
        for (var i = 0; i < 250; i++)
        {
            var wa = Guid.Parse($"14101410-0000-0000-0003-{i:D12}");
            _world.Add(WorkAssignment, wa, ("sprk_issecure", true)); // secure, filed under nothing
            _participations.Flags[wa] = SecureFlags;
            candidates.Add(wa);
        }

        _failingRow = (WorkAssignment, candidates[210]); // one row of one 200/50 chunk

        var set = await ComposeAsync(WorkAssignment, candidates.ToArray());

        set.Rights.Should().HaveCount(200, "the other chunk is decided; the faulted chunk of 50 fails closed");
        set.Rights.Should().NotContainKey(candidates[210]);
    }

    [Fact(DisplayName = "#1410 F3: rows naming the same pair type whose read faults cost ONE type read, and are all removed")]
    public async Task AFaultingPairType_IsReadOnce_ForEveryRowNamingIt()
    {
        var parent = Guid.Parse("14101410-1410-1410-1410-1410141014c9");
        var projects = Enumerable.Range(0, 5).Select(i => Guid.Parse($"14101410-0000-0000-0006-{i:D12}")).ToArray();
        foreach (var project in projects)
            ProjectUnder(project, Project, parent); // all name ProjectType by the pair
        _failingRow = ("sprk_recordtype_ref", ProjectType);

        var set = await ComposeAsync(Project, projects);

        set.Rights.Should().BeEmpty("whose list governs them is unknown: fail closed");
        _refusedReads.Should().Be(1, "a type whose read faulted is not read again for every row naming it");
    }

    [Fact(DisplayName = "Round 82: a not-yet-secure child under a secure matter that is NOT walled stays")]
    public async Task ANonSecureChild_UnderAnUnwalledSecureMatter_Stays()
    {
        SecureMatterRow(SecureMatter);
        SecureMatterRow(OtherSecureMatter);
        OpenWaUnder(OpenWa, Matter, SecureMatter);
        _denyList.DenySystemUserOnRecord(Walled, OtherSecureMatter); // a wall elsewhere

        var set = await ComposeAsync(WorkAssignment, OpenWa);

        set.Rights.Should().ContainKey(OpenWa);
    }

    [Fact(DisplayName = "Round 82: the link read fails -> a not-yet-secure child below a secure parent is removed; an unfiled one stays")]
    public async Task ALinkReadFault_RemovesANonSecureChildBelowASecureParent()
    {
        var links = new Mock<IContactIdentityStore>(MockBehavior.Strict);
        links.Setup(l => l.GetSystemUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemUserLookup(LookupStatus.Failed, null));
        _identities = links.Object;
        SecureMatterRow(SecureMatter);
        OpenWaUnder(OpenWa, Matter, SecureMatter);
        _world.Add(WorkAssignment, ControlWa, ("sprk_issecure", false)); // filed under nothing

        var set = await ComposeAsync(WorkAssignment, OpenWa, ControlWa);

        set.Rights.Should().NotContainKey(OpenWa, "whose wall subjects could not be read: fail closed");
        set.Rights.Should().ContainKey(ControlWa, "no secure parent: the subjects are not its question");
    }

    [Fact(DisplayName = "Round 82: the deny-list reader fails closed -> a not-yet-secure child below a secure parent is removed")]
    public async Task ADenyListFault_RemovesANonSecureChildBelowASecureParent()
    {
        SecureMatterRow(SecureMatter);
        OpenWaUnder(OpenWa, Matter, SecureMatter);
        _world.Add(WorkAssignment, ControlWa, ("sprk_issecure", false)); // filed under nothing
        _denyList.Faults = true;

        var set = await ComposeAsync(WorkAssignment, OpenWa, ControlWa);

        set.Rights.Should().NotContainKey(OpenWa);
        set.Rights.Should().ContainKey(ControlWa, "it was never asked: no secure parent, no contact axis");
    }

    // ── Cost (NFR-02) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Round 82 cost pin: 250 non-secure work assignments under non-secure matters — 2 row reads, 2 flag reads, nothing else")]
    public async Task ManyNonSecureCandidates_UnderNonSecureMatters_CostOnlyTheBatchedWalk()
    {
        var links = new Mock<IContactIdentityStore>(MockBehavior.Strict); // any link read fails the test
        _identities = links.Object;
        var candidates = new List<Guid>();
        for (var i = 0; i < 250; i++)
        {
            var matter = Guid.Parse($"14101410-0000-0000-0004-{i:D12}");
            var wa = Guid.Parse($"14101410-0000-0000-0005-{i:D12}");
            _world.OrdinaryRoot(Matter, matter);
            OpenWaUnder(wa, Matter, matter);
            candidates.Add(wa);
        }

        var set = await ComposeAsync(WorkAssignment, candidates.ToArray());

        set.Rights.Should().HaveCount(250);
        _world.QueriedTables.Count(t => t == WorkAssignment).Should().Be(2, "250 rows in chunks of 200");
        _world.QueriedTables.Count(t => t == Matter).Should().Be(2, "250 parent flags in chunks of 200");
        _world.QueriedTables.Should().HaveCount(4, "no other read");
        _denyList.Queries.Should().Be(0, "no secure candidate, no secure parent, no contact axis: no deny-list query");
    }

    [Fact(DisplayName = "#1410: 250 secure work assignments are walked in batched reads, never one per record")]
    public async Task ManyCandidates_AreWalkedInBatches()
    {
        var candidates = new List<Guid>();
        for (var i = 0; i < 250; i++)
        {
            var matter = Guid.Parse($"14101410-0000-0000-0001-{i:D12}");
            var wa = Guid.Parse($"14101410-0000-0000-0002-{i:D12}");
            SecureMatterRow(matter);
            SecureWaUnder(wa, Matter, matter);
            candidates.Add(wa);
        }

        var walledMatter = Guid.Parse($"14101410-0000-0000-0001-{249:D12}");
        _denyList.DenySystemUserOnRecord(Walled, walledMatter);

        var set = await ComposeAsync(WorkAssignment, candidates.ToArray());

        set.Rights.Should().NotContainKey(candidates[249]);
        set.Rights.Should().HaveCount(249);
        _world.QueriedTables.Count(t => t == WorkAssignment).Should().Be(2, "250 rows in chunks of 200");
        _world.QueriedTables.Count(t => t == Matter).Should().Be(2, "250 parent flags in chunks of 200");
    }
}
