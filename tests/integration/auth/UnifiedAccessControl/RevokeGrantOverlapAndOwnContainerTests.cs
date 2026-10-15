using Sprk.Bff.Api.Tests.TestInfrastructure;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 166 r1 — two corrections to the container cleanup that close-project and the
/// single-grant revoke perform, both found by the task-166 verifier.
/// </summary>
/// <remarks>
/// <para><b>1. OWN container only.</b> Task 166 derived the container from the authorized record with
/// <see cref="RecordContainerResolver.ResolveForRecordAsync(string, Guid, CancellationToken)"/> — the CONTENT
/// question. Since task 155 that answers a NON-secure project or work assignment filed under a SECURE matter with the
/// MATTER's container, so closing the project (or revoking a grant on it) stripped the revoked grantees from the
/// matter's container, where a grant on the matter itself may still entitle them. The helper now asks
/// <see cref="RecordContainerResolver.ResolveOwnContainerAsync"/>: a record that is not itself secure owns no
/// isolated container and its step is skipped.</para>
/// <para><b>2. Overlap.</b> Container permissions are keyed by EMAIL, so one person can be entitled to a root twice —
/// a direct contact grant and membership of an organization granted the same root. Revoking one must not strip a
/// permission the other still justifies. The root's REMAINING active, unexpired grants are read after the revoked
/// grant is deactivated, and every contact they still entitle is kept; if that cannot be established the removal
/// proceeds (fail closed).</para>
/// <para>Seams: the virtual members of <see cref="DataverseWebApiClient"/> and
/// <see cref="SpeContainerMembershipService"/>, and the REAL <see cref="RecordContainerResolver"/> over substituted
/// registry and entity service (<see cref="TestRecordContainerResolver"/>). No <c>Mock&lt;HttpMessageHandler&gt;</c>,
/// no DI-registration or constructor null-check test (ADR-038).</para>
/// </remarks>
public class RevokeGrantOverlapAndOwnContainerTests
{
    private const string GrantEntitySet = "sprk_externalrecordaccesses";
    private const string TenantId = "00000000-0000-0000-0000-0000000001aa";
    private const string ProjectContainer = "b!secure-project-own-container-166r1";

    private static readonly Guid ProjectId = Guid.Parse("22222222-1660-4000-8000-000000000001");
    private static readonly Guid WorkAssignmentId = Guid.Parse("22222222-1660-4000-8000-000000000002");
    private static readonly Guid SecureMatterId = Guid.Parse("22222222-1660-4000-8000-000000000003");
    private static readonly Guid ContactA = Guid.Parse("11111111-1660-4000-8000-00000000000a");
    private static readonly Guid ContactB = Guid.Parse("11111111-1660-4000-8000-00000000000b");
    private static readonly Guid OrganizationId = Guid.Parse("33333333-1660-4000-8000-000000000001");
    private static readonly Guid OtherOrganizationId = Guid.Parse("33333333-1660-4000-8000-000000000002");

    private static readonly DateOnly Today = new(2026, 10, 4);
    private static readonly DateOnly NextMonth = Today.AddDays(30);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    private static readonly IReadOnlyDictionary<Guid, string> Emails = new Dictionary<Guid, string>
    {
        [ContactA] = "a.counsel@clientfirm.example",
        [ContactB] = "b.counsel@clientfirm.example",
    };

    // =============================================================================================
    // 1. OWN container only — never a secure ancestor's.
    // =============================================================================================

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task ContentResolution_OfANonSecureRecordUnderASecureMatter_IsTheMattersContainer_WhichIsTheHazard(string entity)
    {
        var recordId = entity == "sprk_project" ? ProjectId : WorkAssignmentId;
        var resolver = TestRecordContainerResolver.ForNonSecureRecordUnderSecureMatter(entity, recordId, SecureMatterId);

        var decision = await resolver.ResolveForRecordAsync(entity, recordId, CancellationToken.None);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure,
            "task 155: a child of a secure root stores its CONTENT in the root's container");
        decision.ContainerId.Should().Be(TestRecordContainerResolver.SecureMatterContainer);
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task DeriveRecordOwnContainer_OfANonSecureRecordUnderASecureMatter_IsNoContainer_NotTheMatters(string entity)
    {
        var recordId = entity == "sprk_project" ? ProjectId : WorkAssignmentId;
        var resolver = TestRecordContainerResolver.ForNonSecureRecordUnderSecureMatter(entity, recordId, SecureMatterId);

        var (containerId, decided) = await ProjectClosureEndpoint.DeriveRecordOwnContainerAsync(
            resolver, entity, recordId, NullLogger.Instance, CancellationToken.None);

        containerId.Should().BeNull("the record is not itself secure, so it owns no isolated container to clean");
        decided.Should().BeTrue("a non-secure record is a decided 'nothing to clean', not an unknown");
    }

    [Fact]
    public async Task DeriveRecordOwnContainer_OfASecureRecord_IsItsOwnContainer()
    {
        var resolver = TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, ProjectContainer);

        var (containerId, decided) = await ProjectClosureEndpoint.DeriveRecordOwnContainerAsync(
            resolver, "sprk_project", ProjectId, NullLogger.Instance, CancellationToken.None);

        containerId.Should().Be(ProjectContainer);
        decided.Should().BeTrue();
    }

    [Fact]
    public async Task DeriveRecordOwnContainer_WhenTheRecordsSecureFlagIsAbsent_IsUndecided_NeverItsStaleStamp()
    {
        var resolver = TestRecordContainerResolver.ForRecordWithNoSecureFlag("sprk_project", ProjectId);

        var (containerId, decided) = await ProjectClosureEndpoint.DeriveRecordOwnContainerAsync(
            resolver, "sprk_project", ProjectId, NullLogger.Instance, CancellationToken.None);

        containerId.Should().BeNull();
        decided.Should().BeFalse("an unknown flag is never read as 'not secure' — the step reports not cleared");
    }

    [Fact]
    public async Task Revoke_OfAGrantOnAWorkAssignmentUnderASecureMatter_TouchesNoContainer()
    {
        var world = new GrantWorld(ExternalGrantRootType.WorkAssignment, WorkAssignmentId);
        var target = world.ContactGrant(ContactA, NextMonth);
        var spe = new SpeCalls();

        var result = await Revoke(world, spe, target, ContactA,
            TestRecordContainerResolver.ForNonSecureRecordUnderSecureMatter("sprk_workassignment", WorkAssignmentId, SecureMatterId));

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted);
        spe.Containers.Should().BeEmpty("the secure matter's container is the matter's, not the work assignment's");
        world.Deactivated.Should().Contain(target, "the Dataverse revoke itself is unchanged");
    }

    // =============================================================================================
    // 2. Overlap — another live grant on the same root keeps the permission it still justifies.
    // =============================================================================================

    [Fact]
    public async Task Revoke_AContactGrant_WhoseContactIsAlsoInAnOrganizationStillGrantedTheRoot_KeepsTheirPermission()
    {
        var world = new GrantWorld(ExternalGrantRootType.Project, ProjectId);
        var target = world.ContactGrant(ContactA, NextMonth);
        world.OrganizationGrant(OrganizationId, NextMonth);
        world.Members[OrganizationId] = [ContactA];
        var spe = new SpeCalls();

        var result = await Revoke(world, spe, target, ContactA);

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted,
            "the organization grant still entitles them, so their email-keyed permission is still justified");
        spe.Emails.Should().BeEmpty();
        world.Deactivated.Should().Equal(target);
    }

    [Fact]
    public async Task Revoke_AnOrganizationGrant_KeepsTheMemberWithTheirOwnLiveGrant_AndRemovesTheRest()
    {
        var world = new GrantWorld(ExternalGrantRootType.Project, ProjectId);
        var target = world.OrganizationGrant(OrganizationId, NextMonth);
        world.ContactGrant(ContactA, NextMonth);
        world.Members[OrganizationId] = [ContactA, ContactB];
        var spe = new SpeCalls();

        var result = await Revoke(world, spe, target, Guid.Empty);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved);
        body.SpeOrgMemberCleanup!.RetainedByOtherGrant.Should().Be(1);
        body.SpeOrgMemberCleanup.PermissionsRemoved.Should().Be(1);
        spe.Emails.Should().Equal(Emails[ContactB]);
        spe.Containers.Should().OnlyContain(c => c == ProjectContainer);
    }

    [Fact]
    public async Task Revoke_AnOrganizationGrant_WhenEveryMemberIsStillEntitled_AttemptsNothing()
    {
        var world = new GrantWorld(ExternalGrantRootType.Project, ProjectId);
        var target = world.OrganizationGrant(OrganizationId, NextMonth);
        world.OrganizationGrant(OtherOrganizationId, NextMonth);
        world.Members[OrganizationId] = [ContactA];
        world.Members[OtherOrganizationId] = [ContactA];
        var spe = new SpeCalls();

        var result = await Revoke(world, spe, target, Guid.Empty);

        var body = Body(result);
        body.SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.NotAttempted,
            "nobody's permission was looked up, so 'nobody held one' would be a claim this revoke never checked");
        body.SpeOrgMemberCleanup!.RetainedByOtherGrant.Should().Be(1);
        spe.Emails.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_WhenTheOtherGrantHasExpired_ItKeepsNobodysPermission()
    {
        var world = new GrantWorld(ExternalGrantRootType.Project, ProjectId);
        var target = world.ContactGrant(ContactA, NextMonth);
        world.OrganizationGrant(OrganizationId, Yesterday);
        world.Members[OrganizationId] = [ContactA];
        var spe = new SpeCalls();

        var result = await Revoke(world, spe, target, ContactA);

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved,
            "a lapsed grant confers nothing (ExternalParticipationService.ConfersAccessOn), so it justifies nothing");
        spe.Emails.Should().Equal(Emails[ContactA]);
    }

    [Fact]
    public async Task Revoke_WhenTheRemainingGrantsCannotBeRead_RemovesAnyway_FailClosed()
    {
        var world = new GrantWorld(ExternalGrantRootType.Project, ProjectId) { RootQueryFails = true };
        var target = world.ContactGrant(ContactA, NextMonth);
        world.OrganizationGrant(OrganizationId, NextMonth);
        world.Members[OrganizationId] = [ContactA];
        var spe = new SpeCalls();

        var result = await Revoke(world, spe, target, ContactA);

        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved,
            "an entitlement that cannot be verified is not honoured (ADR-003)");
        spe.Emails.Should().Equal(Emails[ContactA]);
    }

    [Fact]
    public async Task Revoke_TheRevokedGrantsOwnRowsNeverCountAsAnotherGrant()
    {
        var world = new GrantWorld(ExternalGrantRootType.Project, ProjectId);
        var target = world.ContactGrant(ContactA, NextMonth);
        var duplicate = world.ContactGrant(ContactA, NextMonth); // a duplicate row of the SAME logical grant
        var spe = new SpeCalls();

        var result = await Revoke(world, spe, target, ContactA);

        world.Deactivated.Should().BeEquivalentTo(new[] { target, duplicate });
        Body(result).SpeContainerOutcome.Should().Be(SpeContainerRevokeOutcome.PermissionRemoved,
            "the overlap read runs after the sweep, so the grant being revoked cannot keep itself");
        spe.Emails.Should().Equal(Emails[ContactA]);
    }

    // =============================================================================================
    // Harness
    // =============================================================================================

    private static async Task<IResult> Revoke(
        GrantWorld world, SpeCalls spe, Guid accessRecordId, Guid contactId, RecordContainerResolver? resolver = null)
        => await RevokeExternalAccessEndpoint.RevokeAccessAsync(
            new RevokeAccessRequest(accessRecordId, contactId, ProjectId),
            world.Build().Object,
            spe.Build().Object,
            new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None),
            AssignedAccessTestDoubles.InertMaterializer(),
            resolver ?? TestRecordContainerResolver.ForSecureRecord(
                ExternalGrantRoot.LogicalNameFor(world.RootType), world.RootId, ProjectContainer),
            AuthenticatedContext(),
            NullLogger<Program>.Instance,
            CancellationToken.None,
            new FakeTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)));

    private static RevokeAccessResponse Body(IResult result) =>
        result.Should().BeOfType<Ok<RevokeAccessResponse>>().Subject.Value!;

    private static HttpContext AuthenticatedContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", TenantId) }, authenticationType: "Test")),
    };

    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000001ab",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = TenantId,
        }).Build();

    /// <summary>
    /// The grant table for ONE root, the organization junction and the contacts' emails, behind the virtual
    /// <see cref="DataverseWebApiClient"/> seam. It INTERPRETS the production <c>$filter</c>s (the logical-grant key
    /// filter, the root filter, the junction filter) and models deactivation, so the order "sweep, then read what
    /// remains" is what the tests actually exercise.
    /// </summary>
    private sealed class GrantWorld(ExternalGrantRootType rootType, Guid rootId)
    {
        private readonly List<ExternalGrantRow> _rows = new();

        public ExternalGrantRootType RootType { get; } = rootType;
        public Guid RootId { get; } = rootId;
        public Dictionary<Guid, Guid[]> Members { get; } = new();
        public List<Guid> Deactivated { get; } = new();
        public bool RootQueryFails { get; init; }

        public Guid ContactGrant(Guid contactId, DateOnly expires) => Add(contactId, organizationId: null, expires);

        public Guid OrganizationGrant(Guid organizationId, DateOnly expires) => Add(contactId: null, organizationId, expires);

        private Guid Add(Guid? contactId, Guid? organizationId, DateOnly expires)
        {
            var row = new ExternalGrantRow
            {
                Id = Guid.NewGuid(),
                StateCode = 0,
                ExpiresDate = expires,
                ContactId = contactId,
                OrganizationId = organizationId,
                ProjectId = RootType == ExternalGrantRootType.Project ? RootId : null,
                WorkAssignmentId = RootType == ExternalGrantRootType.WorkAssignment ? RootId : null,
            };
            _rows.Add(row);
            return row.Id;
        }

        private IEnumerable<ExternalGrantRow> Active => _rows.Where(r => !Deactivated.Contains(r.Id));

        public Mock<DataverseWebApiClient> Build()
        {
            var mock = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance, null!, null!);

            mock.Setup(c => c.RetrieveAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string _, CancellationToken _) => _rows.SingleOrDefault(r => r.Id == id));

            mock.Setup(c => c.QueryAsync<ExternalGrantRow>(
                    GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) => Match(filter!));

            mock.Setup(c => c.UpdateAsync(
                    GrantEntitySet, It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid id, object _, CancellationToken _) =>
                {
                    Deactivated.Add(id);
                    return Task.CompletedTask;
                });

            mock.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                    ExternalOrganizationMembership.EntitySet, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) =>
                {
                    var org = Guid.Parse(Regex.Match(filter!, @"_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
                    return (Members.TryGetValue(org, out var ids) ? ids : [])
                        .Select(id => new ExternalOrganizationMembership.ContactOrganizationRow { ContactId = id })
                        .ToList();
                });

            mock.Setup(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
                    "contacts", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string _, CancellationToken _) =>
                    new RevokeExternalAccessEndpoint.ContactEmailRow { emailaddress1 = Emails.TryGetValue(id, out var e) ? e : null });

            return mock;
        }

        private List<ExternalGrantRow> Match(string filter)
        {
            var root = ExternalGrantLifecycle.ActiveRowsForRootFilter(RootType, RootId);
            if (filter == root)
            {
                if (RootQueryFails)
                {
                    throw new InvalidOperationException("Dataverse unavailable");
                }

                return Active.ToList();
            }

            if (filter.Contains("_sprk_contact_value eq null", StringComparison.Ordinal))
            {
                var org = Guid.Parse(Regex.Match(filter, @"_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
                return Active.Where(r => r.ContactId is null && r.OrganizationId == org).ToList();
            }

            var contact = Regex.Match(filter, @"_sprk_contact_value eq ([0-9a-fA-F-]{36})");
            if (contact.Success)
            {
                var id = Guid.Parse(contact.Groups[1].Value);
                return Active.Where(r => r.ContactId == id).ToList();
            }

            throw new InvalidOperationException($"Unmodelled grant $filter: '{filter}'.");
        }
    }

    /// <summary>The membership seam: every container and email the revoke asked to remove, answering "removed".</summary>
    private sealed class SpeCalls
    {
        public List<string> Emails { get; } = new();
        public List<string> Containers { get; } = new();

        public Mock<SpeContainerMembershipService> Build()
        {
            var mock = new Mock<SpeContainerMembershipService>(
                TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);

            mock.Setup(s => s.RevokeMembershipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, string email, CancellationToken _) =>
                {
                    Containers.Add(containerId);
                    Emails.Add(email);
                    return new SpeContainerMembershipResult(true, "permission-166r1", null);
                });

            mock.Setup(s => s.RemoveMembershipsAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string containerId, IReadOnlyCollection<string> emails, CancellationToken _) =>
                {
                    var results = new Dictionary<string, SpeContainerMembershipResult>(StringComparer.OrdinalIgnoreCase);
                    foreach (var email in emails)
                    {
                        Containers.Add(containerId);
                        Emails.Add(email);
                        results[email] = new SpeContainerMembershipResult(true, "permission-166r1", null);
                    }

                    return (IReadOnlyDictionary<string, SpeContainerMembershipResult>)results;
                });

            return mock;
        }
    }
}
