using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// Unit tests for the POLYMORPHIC external grant-WRITE path (task 070) — the write-side companion to
/// task 028's polymorphic reads. Grants can now be held at a Project, Matter, OR Work Assignment root.
///
/// These assert the two pieces of pure branching logic whose failure would silently break grants:
///   - <see cref="ExternalGrantRoot"/> — recordType parsing + the @odata.bind navigation-property map.
///   - <see cref="GrantExternalAccessEndpoint.ResolveGrantRoot"/> — root selection + fail-closed rejects.
///   - <see cref="GrantExternalAccessEndpoint.BuildGrantPayload"/> — the exact typed-lookup bind written
///     to Dataverse (a wrong key is a silent grant failure — behavior a caller would notice).
///   - <see cref="ProjectClosureEndpoint.BuildActiveProjectGrantsFilter"/> — regression guard for the
///     _sprk_project_value cascade-revoke fix.
///
/// Testing internal members via InternalsVisibleTo("Sprk.Bff.Api.Tests") — no reflection (ADR-038 §7).
/// </summary>
[Trait("category", "external-access")]
public class PolymorphicGrantWriteTests
{
    // =========================================================================
    // ExternalGrantRoot.BindFor — @odata.bind navigation-property map
    // =========================================================================

    [Theory]
    [InlineData(ExternalGrantRootType.Project, "sprk_Project", "sprk_projects")]
    [InlineData(ExternalGrantRootType.Matter, "sprk_Matter", "sprk_matters")]
    [InlineData(ExternalGrantRootType.WorkAssignment, "sprk_WorkAssignment", "sprk_workassignments")]
    public void BindFor_ForEachRoot_ReturnsTypedLookupNavigationAndEntitySet(
        ExternalGrantRootType type, string expectedNavProperty, string expectedEntitySet)
    {
        var (navProperty, entitySet) = ExternalGrantRoot.BindFor(type);

        navProperty.Should().Be(expectedNavProperty,
            "the grant write binds the typed lookup nav-property sprk_Xid (verified live on sprk_externalrecordaccess)");
        entitySet.Should().Be(expectedEntitySet);
    }

    // =========================================================================
    // ExternalGrantRoot.TryParse — wire recordType token → enum (case/spacing tolerant)
    // =========================================================================

    [Theory]
    [InlineData("project", ExternalGrantRootType.Project)]
    [InlineData("Project", ExternalGrantRootType.Project)]
    [InlineData("MATTER", ExternalGrantRootType.Matter)]
    [InlineData("matter", ExternalGrantRootType.Matter)]
    [InlineData("workassignment", ExternalGrantRootType.WorkAssignment)]
    [InlineData("WorkAssignment", ExternalGrantRootType.WorkAssignment)]
    [InlineData("work-assignment", ExternalGrantRootType.WorkAssignment)]
    [InlineData("work_assignment", ExternalGrantRootType.WorkAssignment)]
    public void TryParse_KnownToken_ReturnsTrueAndCorrectType(string raw, ExternalGrantRootType expected)
    {
        ExternalGrantRoot.TryParse(raw, out var type).Should().BeTrue();
        type.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invoice")]
    [InlineData("sprk_project")]
    [InlineData("document")]
    public void TryParse_UnknownOrEmptyToken_ReturnsFalse(string? raw)
    {
        ExternalGrantRoot.TryParse(raw, out _).Should().BeFalse(
            "an unrecognized recordType must not silently resolve to a default root (fail-closed)");
    }

    // =========================================================================
    // ResolveGrantRoot — root selection + fail-closed rejection (NFR-08)
    // =========================================================================

    [Fact]
    public void ResolveGrantRoot_ExplicitMatter_ResolvesMatterRoot()
    {
        var matterId = Guid.NewGuid();
        var request = MakeGrant(projectId: Guid.Empty, recordType: "matter", recordId: matterId);

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeTrue();
        result.Type.Should().Be(ExternalGrantRootType.Matter);
        result.Id.Should().Be(matterId);
    }

    [Fact]
    public void ResolveGrantRoot_ExplicitWorkAssignment_ResolvesWorkAssignmentRoot()
    {
        var waId = Guid.NewGuid();
        var request = MakeGrant(projectId: Guid.Empty, recordType: "workassignment", recordId: waId);

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeTrue();
        result.Type.Should().Be(ExternalGrantRootType.WorkAssignment);
        result.Id.Should().Be(waId);
    }

    [Fact]
    public void ResolveGrantRoot_LegacyProjectIdOnly_ResolvesProjectRoot()
    {
        // Back-compat: a pre-071 client that sends only ProjectId still creates a project grant.
        var projectId = Guid.NewGuid();
        var request = MakeGrant(projectId: projectId, recordType: null, recordId: null);

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeTrue();
        result.Type.Should().Be(ExternalGrantRootType.Project);
        result.Id.Should().Be(projectId);
    }

    [Fact]
    public void ResolveGrantRoot_ExplicitTypeWithLegacyProjectId_PrefersExplicitType()
    {
        // If a client sends BOTH, the explicit polymorphic root wins; ProjectId is ignored.
        var projectId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var request = MakeGrant(projectId: projectId, recordType: "matter", recordId: matterId);

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeTrue();
        result.Type.Should().Be(ExternalGrantRootType.Matter);
        result.Id.Should().Be(matterId);
    }

    [Fact]
    public void ResolveGrantRoot_UnknownRecordType_FailsClosed()
    {
        var request = MakeGrant(projectId: Guid.NewGuid(), recordType: "invoice", recordId: Guid.NewGuid());

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeFalse("an unknown recordType must be rejected, never defaulted to a project grant");
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResolveGrantRoot_ExplicitRecordTypeWithEmptyRecordId_FailsClosed()
    {
        var request = MakeGrant(projectId: Guid.NewGuid(), recordType: "matter", recordId: Guid.Empty);

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeFalse("an explicit recordType with no recordId is an error, not a fallback to ProjectId");
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResolveGrantRoot_NoRootAtAll_FailsClosed()
    {
        var request = MakeGrant(projectId: Guid.Empty, recordType: null, recordId: null);

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeFalse("a grant with no root must be rejected (no unscoped/project-defaulted row)");
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResolveGrantRoot_RecordIdWithoutRecordType_FailsClosed()
    {
        // A bare RecordId with no RecordType is ambiguous — reject rather than guess.
        var request = MakeGrant(projectId: Guid.Empty, recordType: null, recordId: Guid.NewGuid());

        var result = GrantExternalAccessEndpoint.ResolveGrantRoot(request);

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    // =========================================================================
    // BuildGrantPayload — exact typed-lookup bind written to Dataverse
    // =========================================================================

    [Fact]
    public void BuildGrantPayload_ProjectRoot_BindsProjectLookup_ByteIdenticalToLegacy()
    {
        var projectId = Guid.NewGuid();
        var request = MakeGrant(projectId: projectId, recordType: null, recordId: null);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Project, projectId, grantedBySystemUserId: null));

        payload.Should().ContainKey("sprk_Project@odata.bind");
        payload["sprk_Project@odata.bind"].Should().Be($"/sprk_projects({projectId})");
        payload["sprk_Contact@odata.bind"].Should().Be($"/contacts({request.ContactId})");
        // Back-compat: no matter/WA lookups on a project grant.
        payload.Should().NotContainKey("sprk_Matter@odata.bind");
        payload.Should().NotContainKey("sprk_WorkAssignment@odata.bind");
    }

    [Fact]
    public void BuildGrantPayload_MatterRoot_BindsMatterLookupOnly()
    {
        var matterId = Guid.NewGuid();
        var request = MakeGrant(projectId: Guid.Empty, recordType: "matter", recordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload["sprk_Matter@odata.bind"].Should().Be($"/sprk_matters({matterId})");
        payload.Should().NotContainKey("sprk_Project@odata.bind");
        payload.Should().NotContainKey("sprk_WorkAssignment@odata.bind");
    }

    [Fact]
    public void BuildGrantPayload_WorkAssignmentRoot_BindsWorkAssignmentLookupOnly()
    {
        var waId = Guid.NewGuid();
        var request = MakeGrant(projectId: Guid.Empty, recordType: "workassignment", recordId: waId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.WorkAssignment, waId, grantedBySystemUserId: null));

        payload["sprk_WorkAssignment@odata.bind"].Should().Be($"/sprk_workassignments({waId})");
        payload.Should().NotContainKey("sprk_Project@odata.bind");
        payload.Should().NotContainKey("sprk_Matter@odata.bind");
    }

    [Theory]
    [InlineData(ExternalGrantRootType.Project)]
    [InlineData(ExternalGrantRootType.Matter)]
    [InlineData(ExternalGrantRootType.WorkAssignment)]
    public void BuildGrantPayload_AnyRoot_BindsExactlyOneRootLookup(ExternalGrantRootType type)
    {
        var rootId = Guid.NewGuid();
        var request = MakeGrant(projectId: rootId, recordType: null, recordId: null);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, type, rootId, grantedBySystemUserId: null));

        var rootKeys = new[] { "sprk_Project@odata.bind", "sprk_Matter@odata.bind", "sprk_WorkAssignment@odata.bind" };
        payload.Keys.Count(k => rootKeys.Contains(k)).Should().Be(1,
            "a grant row must bind exactly ONE root lookup (never two, never zero)");
    }

    [Fact]
    public void BuildGrantPayload_AnyRoot_AlwaysBindsContactAndAccessLevel()
    {
        var matterId = Guid.NewGuid();
        var request = MakeGrant(projectId: Guid.Empty, recordType: "matter", recordId: matterId,
            accessLevel: ExternalAccessLevel.Collaborate);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload.Should().ContainKey("sprk_Contact@odata.bind");
        payload["sprk_accesslevel"].Should().Be((int)ExternalAccessLevel.Collaborate);
        payload.Should().ContainKey("sprk_granteddate");
    }

    [Fact]
    public void BuildGrantPayload_WithResolvedSystemUser_BindsGrantedBy()
    {
        var matterId = Guid.NewGuid();
        var systemUserId = Guid.NewGuid(); // a resolved Dataverse systemuserid (NOT an AAD oid)
        var request = MakeGrant(projectId: Guid.Empty, recordType: "matter", recordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: systemUserId.ToString()));

        payload["sprk_GrantedBy@odata.bind"].Should().Be($"/systemusers({systemUserId})");
    }

    [Fact]
    public void BuildGrantPayload_NoResolvedSystemUser_OmitsGrantedBy()
    {
        // Regression (task 070): grantedby is audit metadata resolved from the caller's oid → systemuserid.
        // When it can't be resolved (null), it MUST be omitted — an audit field must never 400 the grant.
        var matterId = Guid.NewGuid();
        var request = MakeGrant(projectId: Guid.Empty, recordType: "matter", recordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload.Should().NotContainKey("sprk_GrantedBy@odata.bind");
    }

    [Fact]
    public void BuildGrantPayload_WithExpiry_BindsExpiresDateField_NotExpiryDate()
    {
        // Regression (task 070): the grant table's expiry field is sprk_expiresdate, NOT sprk_expirydate
        // (verified live) — the prior name would 400 any grant carrying an expiry.
        var matterId = Guid.NewGuid();
        var request = new GrantAccessRequest(
            ContactId: Guid.NewGuid(), ProjectId: Guid.Empty, AccessLevel: ExternalAccessLevel.ViewOnly,
            ExpiryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), OrganizationId: null,
            RecordType: "matter", RecordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload.Should().ContainKey("sprk_expiresdate");
        payload.Should().NotContainKey("sprk_expirydate");
    }

    [Fact]
    public void BuildGrantPayload_WithOrganization_BindsSprkOrganization_NotAccount()
    {
        // Firm/org association is sprk_organization (nav property sprk_Organization), NOT the OOB account.
        var matterId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var request = new GrantAccessRequest(
            ContactId: Guid.NewGuid(), ProjectId: Guid.Empty, AccessLevel: ExternalAccessLevel.ViewOnly,
            ExpiryDate: null, OrganizationId: orgId, RecordType: "matter", RecordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload["sprk_Organization@odata.bind"].Should().Be($"/sprk_organizations({orgId})");
        payload.Should().NotContainKey("sprk_accountid@odata.bind");
    }

    [Fact]
    public void BuildGrantPayload_NoOrganization_OmitsOrganizationBind()
    {
        var matterId = Guid.NewGuid();
        var request = MakeGrant(projectId: Guid.Empty, recordType: "matter", recordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload.Should().NotContainKey("sprk_Organization@odata.bind");
    }

    [Fact]
    public void BuildGrantPayload_OrganizationGrant_OmitsContactBind_KeepsOrganizationAndRoot()
    {
        // task 073 #7: an ORGANIZATION grant is a row with an EMPTY ContactId + an OrganizationId. The
        // contact bind MUST be omitted — a contact-empty row is the load-bearing marker the access-check
        // read path keys on to treat the row as an org grant (every active firm member then inherits).
        // A regression that re-adds the contact bind here silently converts an org grant into a broken
        // per-contact row, so this contract is caller-observable.
        var matterId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var request = new GrantAccessRequest(
            ContactId: Guid.Empty, ProjectId: Guid.Empty, AccessLevel: ExternalAccessLevel.FullAccess,
            ExpiryDate: null, OrganizationId: orgId, RecordType: "matter", RecordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload.Should().NotContainKey("sprk_Contact@odata.bind",
            "an org grant has no contact grantee — the contact-empty row IS the org-grant marker");
        payload["sprk_Organization@odata.bind"].Should().Be($"/sprk_organizations({orgId})");
        payload["sprk_Matter@odata.bind"].Should().Be($"/sprk_matters({matterId})");
        payload["sprk_accesslevel"].Should().Be((int)ExternalAccessLevel.FullAccess);
    }

    // =========================================================================
    // ProjectClosureEndpoint — cascade-revoke filter regression (task 070 fix)
    // =========================================================================

    [Fact]
    public void BuildActiveProjectGrantsFilter_UsesProjectValueField_NotProjectIdValue()
    {
        var projectId = Guid.NewGuid();

        var filter = ProjectClosureEndpoint.BuildActiveProjectGrantsFilter(projectId);

        filter.Should().Contain("_sprk_project_value eq",
            "the grant table's project lookup value field is _sprk_project_value (task 070 fix)");
        filter.Should().NotContain("_sprk_projectid_value",
            "the prior _sprk_projectid_value field name is invalid and matched zero rows");
        filter.Should().Contain("statecode eq 0", "cascade-revoke only targets ACTIVE grants");
        filter.Should().Contain(projectId.ToString());
    }

    // =========================================================================
    // Task 138 — the ONE write-time grant-policy decision
    // =========================================================================

    private static readonly Guid PolicyRoot = Guid.Parse("13800000-0000-0000-0000-000000000138");

    private static IReadOnlyDictionary<Guid, RootRecordFlags> FlagsOf(RootRecordFlags flags)
        => new Dictionary<Guid, RootRecordFlags> { [PolicyRoot] = flags };

    /// <summary>The grantee kind is internal, so theory data carries it as a bool (a public method cannot take it).</summary>
    private static GrantGranteeKind KindOf(bool organizationGrant)
        => organizationGrant ? GrantGranteeKind.Organization : GrantGranteeKind.Contact;

    /// <summary>
    /// The owner's matrix (round 2, 2026-09-30): Standard admits both grantee kinds; Secure and Limited admit only
    /// named contacts; Restricted admits neither, and wins over Secure/Limited.
    /// </summary>
    [Theory]
    //          secure restricted limited organization-grant  expected reason (null = allowed)
    [InlineData(false, false, false, false, null)]
    [InlineData(false, false, false, true, null)]
    [InlineData(true, false, false, false, null)]
    [InlineData(true, false, false, true, ExternalGrantLifecycle.OrgGrantDirectOnlyReasonCode)]
    [InlineData(false, false, true, false, null)]
    [InlineData(false, false, true, true, ExternalGrantLifecycle.OrgGrantDirectOnlyReasonCode)]
    [InlineData(true, false, true, true, ExternalGrantLifecycle.OrgGrantDirectOnlyReasonCode)]
    [InlineData(false, true, false, false, ExternalGrantLifecycle.RecordRestrictedReasonCode)]
    [InlineData(false, true, false, true, ExternalGrantLifecycle.RecordRestrictedReasonCode)]
    [InlineData(true, true, false, false, ExternalGrantLifecycle.RecordRestrictedReasonCode)]
    [InlineData(true, true, true, true, ExternalGrantLifecycle.RecordRestrictedReasonCode)]
    public void DecideGrantPolicy_FollowsTheOwnersMatrix(
        bool secure, bool restricted, bool limited, bool organizationGrant, string? expectedReason)
    {
        var decision = ExternalGrantLifecycle.DecideGrantPolicy(
            FlagsOf(new RootRecordFlags(secure, restricted, limited)), PolicyRoot, KindOf(organizationGrant));

        decision.IsAllowed.Should().Be(expectedReason is null);
        decision.ReasonCode.Should().Be(expectedReason);
        if (expectedReason is not null)
        {
            decision.StatusCode.Should().Be(422, "a policy refusal is a 422, never a 500");
            decision.Detail.Should().Contain("Nothing was granted");
        }
    }

    /// <summary>
    /// A fault is reported as itself. <see cref="RootRecordFlags.Unreadable"/> also carries IsRestricted, so the
    /// unreadable marker must be checked FIRST — otherwise a fault reads as "the record is Restricted".
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecideGrantPolicy_UnreadableFlags_ArePolicyUnreadable_NotRecordRestricted(bool organizationGrant)
    {
        var decision = ExternalGrantLifecycle.DecideGrantPolicy(
            FlagsOf(RootRecordFlags.Unreadable), PolicyRoot, KindOf(organizationGrant));

        decision.ReasonCode.Should().Be(ExternalGrantLifecycle.PolicyUnreadableReasonCode);
        decision.StatusCode.Should().Be(503);
    }

    /// <summary>
    /// ABSENT KEY = UNREADABLE at write time — the opposite of the read path's "no veto". The flag reader returns an
    /// empty map for a non-flag-bearing entity type, so a wrong name must not silently allow everything.
    /// </summary>
    [Fact]
    public void DecideGrantPolicy_RootIdAbsentFromTheFlagMap_IsPolicyUnreadable()
    {
        var decision = ExternalGrantLifecycle.DecideGrantPolicy(
            new Dictionary<Guid, RootRecordFlags>(), PolicyRoot, GrantGranteeKind.Contact);

        decision.IsAllowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(ExternalGrantLifecycle.PolicyUnreadableReasonCode);
    }

    /// <summary>
    /// The table the absent-key rule depends on: every grant root type's LOGICAL name is a key of the flag
    /// sources. If a key is renamed, or <see cref="ExternalGrantRoot.LogicalNameFor"/> drifts to an entity-set name,
    /// this fails instead of every grant on that root type being refused as unreadable (or, under a copy of the
    /// read path's TryGetValue shape, silently allowed).
    /// </summary>
    [Theory]
    [InlineData(ExternalGrantRootType.Project, "sprk_project")]
    [InlineData(ExternalGrantRootType.Matter, "sprk_matter")]
    [InlineData(ExternalGrantRootType.WorkAssignment, "sprk_workassignment")]
    public void EveryGrantRootType_MapsThroughItsLogicalName_ToAFlagBearingEntityType(
        ExternalGrantRootType rootType, string expectedLogicalName)
    {
        var logicalName = ExternalGrantRoot.LogicalNameFor(rootType);

        logicalName.Should().Be(expectedLogicalName);
        ExternalParticipationService.IsFlagBearingRootType(logicalName).Should().BeTrue();
        ExternalParticipationService.IsFlagBearingRootType(ExternalGrantRoot.BindFor(rootType).EntitySet)
            .Should().BeFalse("an entity-set name is NOT a flag-source key — passing one reads nothing");
    }

    /// <summary>
    /// Every value of the enum is covered by the table above — a fourth root type cannot be added without a flag
    /// source (or without this failing).
    /// </summary>
    [Fact]
    public void EveryGrantRootType_IsFlagBearing()
    {
        foreach (var rootType in Enum.GetValues<ExternalGrantRootType>())
        {
            ExternalParticipationService.IsFlagBearingRootType(ExternalGrantRoot.LogicalNameFor(rootType))
                .Should().BeTrue("{0} grants are policy-checked through the flag reader", rootType);
        }
    }

    /// <summary>
    /// The column → flag mapping of one READ row whose <c>sprk_issecure</c> holds a value. A null
    /// sprk_accesspermission is Standard (today's behaviour). An EMPTY sprk_issecure is pinned in EmptySecureFlagFailsClosedTests.
    /// </summary>
    [Theory]
    //          issecure  accesspermission  secure restricted limited
    [InlineData(false, null, false, false, false)]
    [InlineData(false, 100000000, false, false, false)]
    [InlineData(false, 100000001, false, false, true)]
    [InlineData(false, 100000002, false, true, false)]
    [InlineData(true, null, true, false, false)]
    [InlineData(true, 100000002, true, true, false)]
    public void FlagsFrom_MapsTheRootColumns(
        bool? isSecure, int? accessPermission, bool secure, bool restricted, bool limited)
    {
        // An ACTIVE row (statecode 0) — the state column is pinned on its own below (task 137).
        var flags = ExternalParticipationService.FlagsFrom(isSecure, accessPermission, stateCode: 0);

        flags.Should().Be(new RootRecordFlags(secure, restricted, limited));
        flags.IsUnreadable.Should().BeFalse();
        flags.IsDirectOnly.Should().Be(secure || limited);
    }

    /// <summary>
    /// Task 137 · C5: the root's own <c>statecode</c>. Only 0 is active; Inactive (1) and a NULL state (a row whose
    /// state was not read — Dataverse never writes one) are INACTIVE, which removes every contact-sourced
    /// contribution. The policy flags are untouched by it, and the flag read selects the column.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(null, true)]
    public void FlagsFrom_TheRootsOwnStateCode_InactiveOrUnknownRemovesContactAccess(int? stateCode, bool inactive)
    {
        var flags = ExternalParticipationService.FlagsFrom(isSecure: false, accessPermission: 100000000, stateCode);

        flags.IsInactive.Should().Be(inactive);
        flags.RemovesContactSourcedAccess.Should().Be(inactive);
        flags.IsRestricted.Should().BeFalse();
        flags.IsUnreadable.Should().BeFalse("the row WAS read");
        ExternalParticipationService.RootFlagColumns.Split(',').Should().Contain("statecode",
            "the inactive-root rule rides the existing batched flag read — no second round trip");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static GrantAccessRequest MakeGrant(
        Guid projectId,
        string? recordType,
        Guid? recordId,
        ExternalAccessLevel accessLevel = ExternalAccessLevel.ViewOnly)
        => new(
            ContactId: Guid.NewGuid(),
            ProjectId: projectId,
            AccessLevel: accessLevel,
            ExpiryDate: null,
            OrganizationId: null,
            RecordType: recordType,
            RecordId: recordId);

    private static IReadOnlyDictionary<string, object?> ToDict(object payload)
        => (IReadOnlyDictionary<string, object?>)(Dictionary<string, object?>)payload;
}
