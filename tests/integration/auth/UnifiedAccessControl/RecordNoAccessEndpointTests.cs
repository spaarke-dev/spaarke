using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.NoAccessEnforcementTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 064 (owner round 59 item 3) — <c>GET /api/v1/records/{type}/{recordId}/no-access</c>
/// through the REAL pipeline: authentication, then <c>RecordRouteAccessAuthorizationFilter</c> asking the caller's rights
/// (the probe double answers per record, as Dataverse does), then the handler over the PRODUCTION
/// <see cref="NoAccessShareEnforcer.ReadCoverageAsync"/> and store, with only their module boundaries substituted.
/// </summary>
/// <remarks>Placement: <c>tests/integration/auth/**</c> (the security-auth KEEP path).</remarks>
public class RecordNoAccessEndpointTests : IClassFixture<RecordNoAccessTestFixture>
{
    private const string Project = "sprk_project";
    private const string Matter = "sprk_matter";
    private const string WorkAssignment = "sprk_workassignment";

    private static readonly Guid Record = Guid.Parse("06406406-0000-4000-8000-0000000000a1");
    private static readonly Guid Org = Guid.Parse("06406406-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherOrg = Guid.Parse("06406406-0000-4000-8000-0000000000c2");
    private static readonly Guid WalledUser = Guid.Parse("06406406-0000-4000-8000-0000000000b1");
    private static readonly Guid WalledContact = Guid.Parse("06406406-0000-4000-8000-0000000000b2");
    private static readonly Guid Author = Guid.Parse("06406406-0000-4000-8000-0000000000b3");
    private static readonly Guid SecureMatter = Guid.Parse("06406406-0000-4000-8000-0000000000d1");
    private static readonly Guid FiledWorkAssignment = Guid.Parse("06406406-0000-4000-8000-0000000000d2");

    private readonly RecordNoAccessTestFixture _fixture;
    private Harness H => _fixture.Harness;

    public RecordNoAccessEndpointTests(RecordNoAccessTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        H.Participations.Flags[Record] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        H.Participations.RecordOrganizations[Record] = new[] { Org };
    }

    private static string Route(string type, Guid id) => $"/api/v1/records/{type}/{id}/no-access";

    private void CallerHolds(string entitySet, Guid id, AccessRights rights) => _fixture.Rights[(entitySet, id)] = rights;

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> GetOk(string type, Guid id)
    {
        var response = await _fixture.CreateAuthenticatedClient().GetAsync(Route(type, id));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await BodyOf(response);
    }

    /// <summary>A direct entry naming the record and a wall over an organization it references.</summary>
    private (Guid Direct, Guid Wall) DirectEntryAndOrganizationWall()
    {
        var direct = H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Project, Record), modifiedBy: Author);
        var wall = H.Store.AddEntry(subjectContact: WalledContact, objectOrganization: Org, modifiedBy: Author);
        H.Store.Entries[direct] = H.Store.Entries[direct] with
        {
            Display = new NoAccessEntryDisplay("Walled user on the project", "Walled User", null, "Author Person",
                new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)),
        };
        H.Store.Entries[wall] = H.Store.Entries[wall] with
        {
            Display = new NoAccessEntryDisplay("Contact walled from Org", "Walled Contact", "Org Ltd", "Author Person", null),
        };
        return (direct, wall);
    }

    // ── (a) the two entries, each with its object path — Write tier ─────────────────────────────────────────────

    [Fact]
    public async Task WriteCaller_ADirectEntryAndAWallOverAReferencedOrganization_ReturnsExactlyThoseTwo_EachWithItsPath()
    {
        var (direct, wall) = DirectEntryAndOrganizationWall();
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("recordType").GetString().Should().Be(Project);
        body.GetProperty("recordId").GetGuid().Should().Be(Record);
        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Applies);
        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies);
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.Complete);
        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        entries.Should().HaveCount(2);

        var d = entries.Single(e => e.GetProperty("entryId").GetGuid() == direct);
        d.GetProperty("objectKind").GetString().Should().Be("record");
        d.GetProperty("subjectKind").GetString().Should().Be("systemuser");
        d.GetProperty("subjectId").GetGuid().Should().Be(WalledUser);
        d.GetProperty("subjectName").GetString().Should().Be("Walled User");
        d.GetProperty("name").GetString().Should().Be("Walled user on the project");
        d.GetProperty("coveredRecordId").GetGuid().Should().Be(Record);
        d.GetProperty("viaSecureParent").GetBoolean().Should().BeFalse();
        d.GetProperty("malformed").GetBoolean().Should().BeFalse();
        d.GetProperty("inForce").GetBoolean().Should().BeTrue("a user wall binds a SECURE record");
        d.GetProperty("notInForceReason").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("modifiedById").GetGuid().Should().Be(Author);
        d.GetProperty("modifiedByName").GetString().Should().Be("Author Person");

        var w = entries.Single(e => e.GetProperty("entryId").GetGuid() == wall);
        w.GetProperty("objectKind").GetString().Should().Be("organization");
        w.GetProperty("objectOrganizationId").GetGuid().Should().Be(Org);
        w.GetProperty("objectOrganizationName").GetString().Should().Be("Org Ltd");
        w.GetProperty("subjectKind").GetString().Should().Be("contact");
        w.GetProperty("subjectId").GetGuid().Should().Be(WalledContact);

        entries.SelectMany(e => e.EnumerateObject().Select(p => p.Name)).Should()
            .NotContain(new[] { "reason", "sprk_reason" }, "an entry's Reason is never returned (task 143 / O2)");
    }

    // ── (e) Read tier: the signals only ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadOnlyCaller_GetsTheSignals_ButNoEntriesAndNoSubjectIdentities()
    {
        DirectEntryAndOrganizationWall();
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var response = await _fixture.CreateAuthenticatedClient().GetAsync(Route(Project, Record));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        var body = await BodyOf(response);
        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Applies);
        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies);
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.NotShown);
        body.GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Null);
        text.Should().NotContain(WalledUser.ToString()).And.NotContain(WalledContact.ToString())
            .And.NotContain("Walled").And.NotContainEquivalentOf("reason");
    }

    [Theory]
    [InlineData(Project, "sprk_projects")]
    [InlineData(Matter, "sprk_matters")]
    [InlineData(WorkAssignment, "sprk_workassignments")]
    public async Task EachRootType_IsGatedOnItsOwnEntitySet_AndAnswers(string type, string entitySet)
    {
        H.Participations.Flags[Record] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        CallerHolds(entitySet, Record, AccessRights.Read);

        var body = await GetOk(type, Record);

        body.GetProperty("recordType").GetString().Should().Be(type);
        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.DoesNotApply);
        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.DoesNotApply);
        _fixture.ProbedTargets.Should().Contain((entitySet, Record), "the caller's rights were asked on THIS record's own table");
    }

    // ── (b) an entry over an organization the record does not reference ──────────────────────────────────────────

    [Fact]
    public async Task AWallOverAnOrganizationTheRecordDoesNotReference_IsAbsent_AndDoesNotApply()
    {
        H.Store.AddEntry(subjectContact: WalledContact, objectOrganization: OtherOrg, modifiedBy: Author);
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.DoesNotApply);
        body.GetProperty("entries").EnumerateArray().Should().BeEmpty();
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.Complete);
    }

    // ── (c) inactive, and malformed ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnInactiveEntry_IsNotCounted_AndNotListed()
    {
        H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Project, Record), modifiedBy: Author, stateCode: 1);
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.DoesNotApply);
        body.GetProperty("entries").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task AnEntryDeactivatedAfterTheCoveringQuery_IsNotCounted()
    {
        // The covering query found it active; by the time it is read, it is not (the store double returns the row as stored).
        var entry = H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Project, Record), modifiedBy: Author);
        _fixture.AfterCoveringQuery = () => H.Store.Entries[entry] = H.Store.Entries[entry] with { StateCode = 1 };
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.DoesNotApply);
        body.GetProperty("entries").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task AMalformedEntry_WallsNobody_SoDoesNotApply_ButIsListedFlaggedToAWriteCaller()
    {
        var malformed = H.Store.AddEntry(subjectUser: WalledUser, subjectContact: WalledContact,
            objectRecord: (Project, Record), modifiedBy: Author);
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.DoesNotApply,
            "a malformed entry denies nothing at read time, so it is not a restriction (the reader's rule)");
        var row = body.GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("entryId").GetGuid().Should().Be(malformed);
        row.GetProperty("malformed").GetBoolean().Should().BeTrue();
        row.GetProperty("inForce").GetBoolean().Should().BeFalse();
        row.GetProperty("notInForceReason").GetString().Should().Be(NoAccessEntryNotInForceReason.Malformed);
        row.GetProperty("subjectKind").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("objectKind").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── user walls bind only SECURE records (owner Q4; verifier finding F4) ────────────────────────────────────

    [Fact]
    public async Task AUserWall_OnANonSecureRecord_DoesNotApply_AndIsListedNotInForce()
    {
        H.Participations.Flags[Record] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        var entry = H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Project, Record), modifiedBy: Author);
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.DoesNotApply);
        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.DoesNotApply,
            "a systemuser-subject entry removes nothing on a non-secure record (AccessibleRecordSetService, the enforcer)");
        var row = body.GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("entryId").GetGuid().Should().Be(entry);
        row.GetProperty("inForce").GetBoolean().Should().BeFalse();
        row.GetProperty("notInForceReason").GetString().Should().Be(NoAccessEntryNotInForceReason.UserWallOnNonSecureRecord);
        row.GetProperty("malformed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AUserWall_OnASecureRecord_Applies()
    {
        H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Project, Record), modifiedBy: Author);
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies);
    }

    [Fact]
    public async Task AUserWall_WhenTheSecureFlagIsUnknown_IsUnknown_NeverDoesNotApply()
    {
        H.Participations.Flags[Record] = RootRecordFlags.Unreadable;
        H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Project, Record), modifiedBy: Author);
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Unknown);
        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Unknown);
        var row = body.GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("inForce").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("notInForceReason").GetString().Should().Be(NoAccessEntryNotInForceReason.SecureStateUnknown);
    }

    [Fact]
    public async Task AUserWall_WhenTheSecureFlagIsUnknown_ButAnotherEntryIsInForce_Applies()
    {
        H.Participations.Flags[Record] = RootRecordFlags.Unreadable;
        H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Project, Record), modifiedBy: Author);
        H.Store.AddEntry(subjectContact: WalledContact, objectOrganization: Org, modifiedBy: Author);
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies);
    }

    [Fact]
    public async Task AContactWall_OnANonSecureRecord_StillApplies()
    {
        H.Participations.Flags[Record] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        H.Store.AddEntry(subjectContact: WalledContact, objectOrganization: Org, modifiedBy: Author);
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies,
            "a contact or organization wall binds any record (the contact plane; owner N3)");
    }

    // ── secure parents (round 61) ─────────────────────────────────────────────────────────────────────────────────

    private void SecureMatterWithAFiledWorkAssignment()
    {
        H.Participations.Flags[FiledWorkAssignment] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        H.Participations.Flags[SecureMatter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        H.ChildWorld = SecureChildShareWorld.Standard()
            .SecureRoot(Matter, SecureMatter)
            .Add(WorkAssignment, FiledWorkAssignment,
                ("owningteam", new Microsoft.Xrm.Sdk.EntityReference("team", SecureChildShareWorld.SecureTeam)),
                ("sprk_issecure", true),
                ("sprk_regardingmatter", new Microsoft.Xrm.Sdk.EntityReference(Matter, SecureMatter)));
    }

    /// <summary>
    /// Task 174 (owner round 84; verifier F2 and F1-d): the route reports the EFFECTIVE values of a work assignment whose own
    /// flags are Standard and not secure, filed under a non-flagged project under a secure, Restricted matter — and, to a
    /// Read-only caller, names only the DIRECT parent (the project, on the record's own lookup), never the matter above it.
    /// </summary>
    [Fact]
    public async Task AReadOnlyCaller_OnAChildGovernedByAGrandparent_GetsTheEffectiveValues_AndOnlyTheDirectParent()
    {
        var middleProject = Guid.Parse("06406406-0000-4000-8000-0000000000d3");
        var matterType = Guid.Parse("06406406-0000-4000-8000-0000000000d4");
        H.Participations.Flags[FiledWorkAssignment] = RootRecordFlags.None;
        H.ChildWorld = SecureChildShareWorld.Standard()
            .Add("sprk_recordtype_ref", matterType, ("sprk_recordlogicalname", Matter))
            .Add(Matter, SecureMatter, ("sprk_issecure", true), ("sprk_mattername", "Hidden Matter"),
                ("sprk_accesspermission", new Microsoft.Xrm.Sdk.OptionSetValue(ExternalParticipationService.AccessPermissionRestricted)))
            .Add(Project, middleProject, ("sprk_issecure", false), ("sprk_projectname", "Middle Project"),
                ("sprk_regardingrecordid", SecureMatter.ToString("D")),
                ("sprk_regardingrecordtype", new Microsoft.Xrm.Sdk.EntityReference("sprk_recordtype_ref", matterType)))
            .Add(WorkAssignment, FiledWorkAssignment, ("sprk_issecure", false),
                ("sprk_regardingproject", new Microsoft.Xrm.Sdk.EntityReference(Project, middleProject)));
        CallerHolds("sprk_workassignments", FiledWorkAssignment, AccessRights.Read);

        var body = await GetOk(WorkAssignment, FiledWorkAssignment);

        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Applies, "secure through the matter");
        body.GetProperty("accessPermission").GetString().Should().Be(EffectiveAccessPermission.Restricted);
        var from = body.GetProperty("inheritedFrom");
        from.GetProperty("recordId").GetGuid().Should().Be(middleProject);
        from.GetProperty("name").GetString().Should().Be("Middle Project");
        body.GetRawText().Should().NotContain(SecureMatter.ToString("D")).And.NotContain("Hidden Matter",
            "a record above the direct parent is not disclosed");
    }

    [Fact]
    public async Task AnEntryOnTheSecureMatterAWorkAssignmentIsFiledUnder_CoversIt_ViaTheSecureParent()
    {
        SecureMatterWithAFiledWorkAssignment();
        var onMatter = H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Matter, SecureMatter), modifiedBy: Author);
        CallerHolds("sprk_workassignments", FiledWorkAssignment, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(WorkAssignment, FiledWorkAssignment);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies,
            "a secure parent's list reaches every secure record filed below it (round 61), as the enforcer applies it");
        var row = body.GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("entryId").GetGuid().Should().Be(onMatter);
        row.GetProperty("viaSecureParent").GetBoolean().Should().BeTrue();
        row.GetProperty("coveredRecordType").GetString().Should().Be(Matter);
        row.GetProperty("coveredRecordId").GetGuid().Should().Be(SecureMatter);
    }

    [Fact]
    public async Task AWriteCaller_SeesAnOrganizationWallOverWhatTheSecureParentReferences_WithItsShape()
    {
        SecureMatterWithAFiledWorkAssignment();
        H.Participations.RecordOrganizations[SecureMatter] = new[] { OtherOrg };
        var wall = H.Store.AddEntry(subjectContact: WalledContact, objectOrganization: OtherOrg, modifiedBy: Author);
        H.Store.Entries[wall] = H.Store.Entries[wall] with
        {
            Display = new NoAccessEntryDisplay("Contact walled from Other Org", "Walled Contact", "Other Org", null, null),
        };
        CallerHolds("sprk_workassignments", FiledWorkAssignment, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(WorkAssignment, FiledWorkAssignment);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies);
        var row = body.GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("entryId").GetGuid().Should().Be(wall);
        row.GetProperty("objectKind").GetString().Should().Be("organization");
        row.GetProperty("objectOrganizationId").GetGuid().Should().Be(OtherOrg);
        row.GetProperty("objectOrganizationName").GetString().Should().Be("Other Org");
        row.GetProperty("subjectKind").GetString().Should().Be("contact");
        row.GetProperty("subjectId").GetGuid().Should().Be(WalledContact);
        row.GetProperty("viaSecureParent").GetBoolean().Should().BeTrue();
        row.GetProperty("coveredRecordType").GetString().Should().Be(Matter);
        row.GetProperty("coveredRecordId").GetGuid().Should().Be(SecureMatter);
        row.GetProperty("inForce").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AUserWallOnTheSecureParent_StaysInForce_WhateverTheFiledRecordsOwnFlagReads()
    {
        SecureMatterWithAFiledWorkAssignment();
        H.Participations.Flags[FiledWorkAssignment] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        H.Store.AddEntry(subjectUser: WalledUser, objectRecord: (Matter, SecureMatter), modifiedBy: Author);
        CallerHolds("sprk_workassignments", FiledWorkAssignment, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(WorkAssignment, FiledWorkAssignment);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies, "the parent is secure (round 61)");
        body.GetProperty("entries").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("inForce").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Verifier pass 2: the record AND its secure parent both reference the walled organization, and the filed record is
    /// not flagged secure yet (the inheritance window, or a refused / failed inheritance). The user wall reaches it through
    /// the parent, so it is in force, as the share-time guard says; finding it on the direct path too must not hide that.
    /// </summary>
    [Fact]
    public async Task AUserWallOverAnOrganizationTheRecordAndItsSecureParentBothReference_IsInForce_WhateverTheRecordsOwnFlag()
    {
        SecureMatterWithAFiledWorkAssignment();
        H.Participations.Flags[FiledWorkAssignment] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        H.Participations.RecordOrganizations[SecureMatter] = new[] { OtherOrg };
        H.Participations.RecordOrganizations[FiledWorkAssignment] = new[] { OtherOrg };
        var wall = H.Store.AddEntry(subjectUser: WalledUser, objectOrganization: OtherOrg, modifiedBy: Author);
        CallerHolds("sprk_workassignments", FiledWorkAssignment, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(WorkAssignment, FiledWorkAssignment);

        // Task 174 (owner round 84): secure is the EFFECTIVE flag — the record is secure through its parent.
        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Applies);
        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies,
            "the wall reaches the record through its secure parent (round 61), whatever its own flag reads");
        var row = body.GetProperty("entries").EnumerateArray().Should().ContainSingle("listed once").Subject;
        row.GetProperty("entryId").GetGuid().Should().Be(wall);
        row.GetProperty("viaSecureParent").GetBoolean().Should().BeFalse("its first path is the record's own");
        row.GetProperty("alsoViaSecureParent").GetBoolean().Should().BeTrue();
        row.GetProperty("coveredRecordId").GetGuid().Should().Be(FiledWorkAssignment);
        row.GetProperty("inForce").GetBoolean().Should().BeTrue();
        row.GetProperty("notInForceReason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Task 174 (owner rounds 82/84 — Q4's "secure" is the record or any filing ancestor): a user wall over an organization
    /// only the not-yet-flagged child references is IN FORCE, because the child is secure through its parent; it is still
    /// not marked as reaching it via the parent. (Before task 174 the child's own false flag made it not in force.)
    /// </summary>
    [Fact]
    public async Task AUserWallOverAnOrganizationOnlyTheNotYetFlaggedChildReferences_IsInForce_AndNotMarkedViaTheParent()
    {
        SecureMatterWithAFiledWorkAssignment();
        H.Participations.Flags[FiledWorkAssignment] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        H.Participations.RecordOrganizations[FiledWorkAssignment] = new[] { OtherOrg };
        H.Store.AddEntry(subjectUser: WalledUser, objectOrganization: OtherOrg, modifiedBy: Author);
        CallerHolds("sprk_workassignments", FiledWorkAssignment, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(WorkAssignment, FiledWorkAssignment);

        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Applies);
        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies);
        var row = body.GetProperty("entries").EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("alsoViaSecureParent").GetBoolean().Should().BeFalse();
        row.GetProperty("inForce").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WhatTheRecordIsFiledUnder_Unreadable_IsUnknown_AndTheEntriesUnavailable()
    {
        SecureMatterWithAFiledWorkAssignment();
        H.ChildWorld.FailingRowReadsOf(WorkAssignment, FiledWorkAssignment);
        CallerHolds("sprk_workassignments", FiledWorkAssignment, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(WorkAssignment, FiledWorkAssignment);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Unknown);
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.Unavailable);
        body.GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── (d) read faults: unknown, never a negative signal or an empty list ───────────────────────────────────────

    [Fact]
    public async Task UnreadableReferencedOrganizations_IsUnknown_NeverDoesNotApply_AndTheEntriesUnavailable()
    {
        H.Participations.UnreadableReferencedOrganizations[Record] = true;
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Unknown);
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.Unavailable);
        body.GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Applies, "the other signal is still read");
    }

    [Fact]
    public async Task UnreadableReferencedOrganizations_ForAReadOnlyCaller_IsUnknown()
    {
        H.Participations.UnreadableReferencedOrganizations[Record] = true;
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Unknown);
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.NotShown);
    }

    [Fact]
    public async Task TheCoveringQueryFaulting_IsUnknown()
    {
        _fixture.FailCoveringQuery = true;
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Unknown);
    }

    [Fact]
    public async Task AnEntryReadFaulting_IsUnknown_AndTheEntriesUnavailable()
    {
        DirectEntryAndOrganizationWall();
        H.Store.FailEntryRead = true;
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Unknown);
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.Unavailable);
        body.GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnUnreadableOrMaskedSecureFlag_IsUnknown_NeverNotSecure()
    {
        H.Participations.Flags[Record] = RootRecordFlags.Unreadable;
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Unknown);
    }

    [Fact]
    public async Task ASecureFlagTheReadDidNotReturn_IsUnknown()
    {
        H.Participations.Absent[Record] = true;
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Unknown);
    }

    [Fact]
    public async Task TheSecureFlagReadThrowing_IsUnknown()
    {
        H.Participations.ThrowOnRead = true;
        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("secure").GetString().Should().Be(AccessSignalState.Unknown);
    }

    // ── truncation: never a silent prefix ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MoreEntriesThanOneReadLists_AreTruncated_AndSaySo()
    {
        for (var i = 0; i <= NoAccessShareEnforcer.MaxEntriesPerRecord; i++)
        {
            H.Store.AddEntry(subjectUser: Guid.NewGuid(), objectRecord: (Project, Record), modifiedBy: Author);
        }

        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Applies);
        body.GetProperty("entriesState").GetString().Should().Be(NoAccessEntriesState.Truncated);
        body.GetProperty("entries").GetArrayLength().Should().Be(NoAccessShareEnforcer.MaxEntriesPerRecord);
    }

    [Fact]
    public async Task ATruncatedPrefixWithNothingInForce_IsUnknown_NotDoesNotApply()
    {
        for (var i = 0; i <= NoAccessShareEnforcer.MaxEntriesPerRecord; i++)
        {
            H.Store.AddEntry(subjectUser: Guid.NewGuid(), subjectContact: Guid.NewGuid(), objectRecord: (Project, Record),
                modifiedBy: Author);
        }

        CallerHolds("sprk_projects", Record, AccessRights.Read);

        var body = await GetOk(Project, Record);

        body.GetProperty("noAccess").GetString().Should().Be(AccessSignalState.Unknown);
    }

    // ── (e) authorization ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_Is401_AndNothingIsRead()
    {
        DirectEntryAndOrganizationWall();

        var response = await _fixture.CreateUnauthenticatedClient().GetAsync(Route(Project, Record));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        H.Store.EntryReads.Should().BeEmpty();
    }

    [Fact]
    public async Task ACallerWithoutRead_AndAnUnknownRecord_GetTheSameUniform404_AndNothingIsRead()
    {
        DirectEntryAndOrganizationWall();
        CallerHolds("sprk_projects", Record, AccessRights.None);
        var client = _fixture.CreateAuthenticatedClient();

        var denied = await client.GetAsync(Route(Project, Record));
        var unknown = await client.GetAsync(Route(Project, Guid.NewGuid()));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var d = await BodyOf(denied);
        var u = await BodyOf(unknown);
        d.GetProperty("reasonCode").GetString().Should().Be(u.GetProperty("reasonCode").GetString());
        d.GetProperty("detail").GetString().Should().Be(u.GetProperty("detail").GetString(),
            "a record the caller cannot read is indistinguishable from one that does not exist (NFR-01)");
        H.Store.EntryReads.Should().BeEmpty();
        H.Participations.Reads.Should().BeEmpty("nothing is read app-only for a caller the gate refused");
    }

    [Fact]
    public async Task ACallerWithAppendButNotRead_Is404()
    {
        CallerHolds("sprk_projects", Record, AccessRights.Append | AccessRights.AppendTo);

        var response = await _fixture.CreateAuthenticatedClient().GetAsync(Route(Project, Record));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ARequestWithNoBearerToken_IsDenied_NeverAnsweredAppOnly()
    {
        DirectEntryAndOrganizationWall();
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);
        var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "not-a-bearer-token");

        var response = await client.GetAsync(Route(Project, Record));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fixture.ProbedTokens.Should().ContainSingle().Which.Should().BeNull("the probe got no caller token to ask with");
        H.Store.EntryReads.Should().BeEmpty();
        H.Participations.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task TheProbeThrowing_IsTheUniform404()
    {
        CallerHolds("sprk_projects", Record, AccessRights.Read | AccessRights.Write);
        _fixture.ProbeThrows = true;

        var response = await _fixture.CreateAuthenticatedClient().GetAsync(Route(Project, Record));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        H.Store.EntryReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("sprk_organization")]
    [InlineData("sprk_invoice")]
    public async Task AnyOtherRecordType_IsNotMapped(string type)
    {
        _fixture.Rights[("contacts", Record)] = AccessRights.Read | AccessRights.Write;

        var response = await _fixture.CreateAuthenticatedClient().GetAsync(Route(type, Record));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fixture.ProbedTargets.Should().BeEmpty("no route answers for that type, so nothing is probed");
    }

    [Fact]
    public async Task TheHandler_WithNoGateDecisionForTheRecord_RefusesWithTheUniform404()
    {
        // Defence in depth: the handler never answers on its own; a decision for ANOTHER record does not count either.
        var context = new DefaultHttpContext();
        context.Items[Sprk.Bff.Api.Api.Filters.RecordRouteAccessAuthorizationFilter.AuthorizedRightsItemKey] =
            new Sprk.Bff.Api.Api.Filters.RecordRouteAccessAuthorizationFilter.AuthorizedRouteRecord(
                "sprk_projects", Guid.NewGuid(), AccessRights.Read | AccessRights.Write);

        var result = await RecordNoAccessEndpoint.HandleAsync(Project, "sprk_projects", Record, H.Enforcer, H.Store,
            H.Participations, context, NullLogger.Instance, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        H.Store.EntryReads.Should().BeEmpty();
    }
}

/// <summary>
/// Test host for the per-record No Access read: the real pipeline, a caller-rights probe that answers per (entity set,
/// record) as Dataverse does (none for anything unseeded, and none without a bearer token), and the production enforcer and
/// handler over <see cref="NoAccessEnforcementTestDoubles.Harness"/>.
/// </summary>
public sealed class RecordNoAccessTestFixture : WorkspaceTestFixture
{
    internal Harness Harness { get; private set; } = new();

    public ConcurrentDictionary<(string EntitySet, Guid RecordId), AccessRights> Rights { get; } = new();

    public ConcurrentBag<(string EntitySet, Guid RecordId)> ProbedTargets { get; } = new();

    public ConcurrentBag<string?> ProbedTokens { get; } = new();

    public bool ProbeThrows { get; set; }

    /// <summary>The covering-entry query throws (a Dataverse fault).</summary>
    public bool FailCoveringQuery { get; set; }

    /// <summary>Runs once the covering-entry query has answered (a change between the query and the entry reads).</summary>
    public Action? AfterCoveringQuery { get; set; }

    public void Reset()
    {
        Harness = new Harness();
        Harness.Store.CoveringHook = CoveringHook;
        Rights.Clear();
        ProbedTargets.Clear();
        ProbedTokens.Clear();
        ProbeThrows = false;
        FailCoveringQuery = false;
        AfterCoveringQuery = null;
    }

    private void CoveringHook()
    {
        if (FailCoveringQuery)
            throw new HttpRequestException("Simulated covering-entry query failure.");

        AfterCoveringQuery?.Invoke();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new RightsProbe(this));

            var client = new Mock<DataverseWebApiClient>(ClientConfig(), NullLogger<DataverseWebApiClient>.Instance, null!, null!)
            { CallBase = false };
            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton(client.Object);

            // The enforcer and the handler are the PRODUCTION registrations; only their module boundaries are substituted,
            // each resolved per request so Reset's new harness takes effect.
            services.RemoveAll<NoAccessEnforcementStore>();
            services.AddScoped<NoAccessEnforcementStore>(_ => Harness.Store);
            services.RemoveAll<ExternalParticipationService>();
            services.AddScoped<ExternalParticipationService>(_ => Harness.Participations);
            services.RemoveAll<IContactIdentityStore>();
            services.AddScoped<IContactIdentityStore>(_ => Harness.Identities);
            services.RemoveAll<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>();
            services.AddScoped<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>(_ => Harness.Shares);
            services.RemoveAll<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
            services.AddScoped(_ => Harness.Cache.Mock.Object);
            services.RemoveAll<IGenericEntityService>();
            services.AddScoped<IGenericEntityService>(_ => Harness.Entities());
        });
    }

    public new HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return client;
    }

    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

    private sealed class RightsProbe : CallerRecordAccessProbe
    {
        private readonly RecordNoAccessTestFixture _fixture;

        public RightsProbe(RecordNoAccessTestFixture fixture)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _fixture = fixture;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            _fixture.ProbedTargets.Add((entitySet, recordId));
            _fixture.ProbedTokens.Add(callerBearerToken);
            if (_fixture.ProbeThrows)
                throw new HttpRequestException("Simulated probe failure.");

            // As the production probe: no caller token, no answer.
            if (string.IsNullOrWhiteSpace(callerBearerToken))
                return Task.FromResult(AccessRights.None);

            return Task.FromResult(_fixture.Rights.TryGetValue((entitySet, recordId), out var r) ? r : AccessRights.None);
        }
    }
}
