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
            ExpiryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), AccountId: null,
            RecordType: "matter", RecordId: matterId);

        var payload = ToDict(GrantExternalAccessEndpoint.BuildGrantPayload(
            request, ExternalGrantRootType.Matter, matterId, grantedBySystemUserId: null));

        payload.Should().ContainKey("sprk_expiresdate");
        payload.Should().NotContainKey("sprk_expirydate");
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
            AccountId: null,
            RecordType: recordType,
            RecordId: recordId);

    private static IReadOnlyDictionary<string, object?> ToDict(object payload)
        => (IReadOnlyDictionary<string, object?>)(Dictionary<string, object?>)payload;
}
