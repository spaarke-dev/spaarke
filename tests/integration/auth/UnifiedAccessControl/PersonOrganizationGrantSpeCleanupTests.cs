using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice C2c: a removed person keeps no container permission that their organization's grants could account for.
/// <see cref="ExternalGrantRevocationService.RemovePersonOrganizationGrantPermissionsAsync"/> removes THAT person's permission
/// from the own container of every secure root an organization they belong to is granted; the organization grants and the
/// other members are untouched, and anything not established is reported not completed. Seams: the virtual members of
/// <see cref="DataverseWebApiClient"/> and <see cref="SpeContainerMembershipService"/>, and the real
/// <see cref="RecordContainerResolver"/> over <see cref="TestRecordContainerResolver"/>. KEEP path:
/// <c>tests/integration/auth/**</c>.
/// </summary>
public class PersonOrganizationGrantSpeCleanupTests
{
    private const string GrantEntitySet = "sprk_externalrecordaccesses";
    private const string ProjectContainer = "b!secure-project-c2c";
    private const string Email = "member@clientfirm.example";

    private static readonly Guid ProjectId = Guid.Parse("22222222-c2c0-4000-8000-000000000001");
    private static readonly Guid Contact = Guid.Parse("11111111-c2c0-4000-8000-00000000000a");
    private static readonly Guid Organization = Guid.Parse("33333333-c2c0-4000-8000-000000000001");

    private readonly List<(Guid Contact, Guid Organization, int State)> _memberships = new();
    private readonly List<ExternalGrantRow> _grants = new();
    private readonly List<Guid> _updates = new();
    private bool _membershipReadFails;

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AMemberOfAnOrganizationGrantedASecureRoot_LosesTheirOwnPermissionOnItsContainer_AndNothingElse()
    {
        _memberships.Add((Contact, Organization, 0));
        OrganizationGrant(Organization, ProjectId);
        var spe = Spe(Removed());

        var cleanup = await Run(spe, Secure());

        cleanup.Completed.Should().BeTrue();
        cleanup.PermissionsRemoved.Should().Be(1);
        spe.Verify(s => s.RevokeMembershipAsync(ProjectContainer, It.Is<SpeMembershipKey>(k => k.Email == Email),
            It.IsAny<DataverseWebApiClient>(), It.IsAny<CancellationToken>()), Times.Once);
        spe.Verify(s => s.RemoveMembershipsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SpeMembershipKey>>(),
            It.IsAny<DataverseWebApiClient>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never,
            "no other member's permission is swept");
        _updates.Should().BeEmpty("the organization grant belongs to the organization and stays active");
    }

    [Fact]
    public async Task AnAmbiguousMatch_RemovesNothing_AndIsNotCompleted()
    {
        _memberships.Add((Contact, Organization, 0));
        OrganizationGrant(Organization, ProjectId);
        var spe = Spe(new SpeContainerMembershipResult(false, null, SpeContainerMembershipService.AmbiguousMatchError + ": two"));

        var cleanup = await Run(spe, Secure());

        cleanup.Completed.Should().BeFalse();
        cleanup.Ambiguous.Should().Be(1);
    }

    [Fact]
    public async Task AMembershipReadFault_IsNotCompleted_AndChecksNoContainer()
    {
        _membershipReadFails = true;
        var spe = Spe(Removed());

        var cleanup = await Run(spe, Secure());

        cleanup.Completed.Should().BeFalse();
        cleanup.Organizations.Should().BeNull();
        spe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnOrganizationWithMoreGrantsThanTheBound_IsNotCompleted()
    {
        _memberships.Add((Contact, Organization, 0));
        for (var i = 0; i <= ExternalGrantRevocationService.MaxOrganizationGrantsPerPersonCleanup; i++)
        {
            OrganizationGrant(Organization, ProjectId);
        }

        var cleanup = await Run(Spe(Removed()), Secure());

        cleanup.Completed.Should().BeFalse();
        cleanup.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task AContainerThatCannotBeDecided_IsNotCompleted()
    {
        _memberships.Add((Contact, Organization, 0));
        OrganizationGrant(Organization, ProjectId);
        var spe = Spe(Removed());

        var cleanup = await Run(spe, TestRecordContainerResolver.ForRecordWithNoSecureFlag("sprk_project", ProjectId));

        cleanup.Completed.Should().BeFalse();
        spe.VerifyNoOtherCalls();
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnInactiveMembership_ChecksNoContainer()
    {
        _memberships.Add((Contact, Organization, 1));
        OrganizationGrant(Organization, ProjectId);
        var spe = Spe(Removed());

        var cleanup = await Run(spe, Secure());

        cleanup.Completed.Should().BeTrue();
        cleanup.Organizations.Should().Be(0);
        spe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnOrganizationGrantOnANonSecureRoot_ChecksNoContainer()
    {
        _memberships.Add((Contact, Organization, 0));
        OrganizationGrant(Organization, ProjectId);
        var spe = Spe(Removed());

        var cleanup = await Run(spe, TestRecordContainerResolver.ForNonSecureRecord("sprk_project", ProjectId));

        cleanup.Completed.Should().BeTrue();
        cleanup.Containers.Should().Be(0);
        spe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task APersonWithNoPermissionThere_IsCompleted()
    {
        _memberships.Add((Contact, Organization, 0));
        OrganizationGrant(Organization, ProjectId);
        var spe = Spe(new SpeContainerMembershipResult(false, null, SpeContainerMembershipService.NoPermissionFoundError));

        var cleanup = await Run(spe, Secure());

        cleanup.Completed.Should().BeTrue();
        cleanup.PermissionsRemoved.Should().Be(0);
    }

    // ── World ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static SpeContainerMembershipResult Removed() => new(true, "perm-c2c", null);

    private static RecordContainerResolver Secure()
        => TestRecordContainerResolver.ForSecureRecord("sprk_project", ProjectId, ProjectContainer);

    private void OrganizationGrant(Guid organizationId, Guid projectId) => _grants.Add(new ExternalGrantRow
    {
        Id = Guid.NewGuid(),
        StateCode = 0,
        OrganizationId = organizationId,
        ProjectId = projectId,
    });

    private static Mock<SpeContainerMembershipService> Spe(SpeContainerMembershipResult answer)
    {
        var mock = new Mock<SpeContainerMembershipService>(
            TestSpeOwnership.AllowAll(Mock.Of<IGraphClientFactory>()), NullLogger<SpeContainerMembershipService>.Instance);
        mock.Setup(s => s.RevokeMembershipAsync(
                It.IsAny<string>(), It.IsAny<SpeMembershipKey>(), It.IsAny<DataverseWebApiClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer);
        return mock;
    }

    private Task<PersonOrganizationGrantSpeCleanup> Run(Mock<SpeContainerMembershipService> spe, RecordContainerResolver resolver)
        => new ExternalGrantRevocationService(
                Dataverse().Object,
                spe.Object,
                new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None),
                AssignedAccessTestDoubles.InertMaterializer(),
                resolver,
                new FakeTimeProvider(),
                identityStore: null,
                NullLogger.Instance)
            .RemovePersonOrganizationGrantPermissionsAsync(Contact, CancellationToken.None);

    private Mock<DataverseWebApiClient> Dataverse()
    {
        var mock = new Mock<DataverseWebApiClient>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Graph:ManagedIdentity:Enabled"] = "true",
                ["API_APP_ID"] = "00000000-0000-0000-0000-0000000001ab",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["TENANT_ID"] = "00000000-0000-0000-0000-0000000001aa",
            }).Build(),
            NullLogger<DataverseWebApiClient>.Instance, null!, null!);

        mock.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                ExternalOrganizationMembership.EntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? filter, string? _, int? _, int? _, CancellationToken _) =>
            {
                if (_membershipReadFails)
                {
                    throw new HttpRequestException("Dataverse unavailable");
                }

                filter.Should().Be(ExternalOrganizationMembership.ActiveOrganizationsOfContactFilter(Contact));
                return _memberships
                    .Where(m => m.Contact == Contact && m.State == 0)
                    .Select(m => new ExternalOrganizationMembership.ContactOrganizationRow { ContactId = m.Contact, OrganizationId = m.Organization })
                    .ToList();
            });

        mock.Setup(c => c.QueryAsync<ExternalGrantRow>(
                GrantEntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? filter, string? _, int? top, int? _, CancellationToken _) =>
            {
                var organization = Guid.Parse(Regex.Match(filter!, @"_sprk_organization_value eq ([0-9a-fA-F-]{36})").Groups[1].Value);
                filter.Should().Be(ExternalGrantLifecycle.ActiveOrganizationGrantsFilter(organization));
                return _grants.Where(g => g.OrganizationId == organization && g.ContactId is null && g.StateCode == 0)
                    .Take(top ?? int.MaxValue).ToList();
            });

        mock.Setup(c => c.UpdateAsync(GrantEntitySet, It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, object _, CancellationToken _) =>
            {
                _updates.Add(id);
                return Task.CompletedTask;
            });

        mock.Setup(c => c.RetrieveAsync<RevokeExternalAccessEndpoint.ContactEmailRow>(
                "contacts", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevokeExternalAccessEndpoint.ContactEmailRow { emailaddress1 = Email });

        return mock;
    }
}
