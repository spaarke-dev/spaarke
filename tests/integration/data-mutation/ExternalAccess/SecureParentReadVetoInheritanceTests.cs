using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Owner round 82 (2026-10-08, binding): "the parent permissions control" — a work assignment filed under a secure matter is
/// governed by the matter's No Access list on Teams/SPA whatever its own flag reads, including when the REAL inheritance
/// (<see cref="SecureRootInheritance.SecureIfFiledUnderSecureAsync"/>) left it unsecured because it ended Refused or Failed.
/// </summary>
/// <remarks>The inheritance runs for real over the provisioning fixture; the composition is the REAL
/// <see cref="AccessibleRecordSetService"/> over the same Dataverse world, flag reads and deny list.</remarks>
[Trait("status", "issue-1410-uac-r2")]
public class SecureParentReadVetoInheritanceTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string WorkAssignment = "sprk_workassignment";

    private readonly ProvisionProjectTestFixture _fixture;

    public SecureParentReadVetoInheritanceTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _fixture.SystemUsers[Outsider] = (false, false);
        _fixture.SystemUsers[AppUser] = (false, true);
    }

    private async Task<SecureRootInheritResult> InheritAsync(Guid workAssignment)
    {
        using var scope = _fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SecureRootInheritance>()
            .SecureIfFiledUnderSecureAsync(WorkAssignment, workAssignment, "t1410", CancellationToken.None);
    }

    /// <summary>The REAL composition for <paramref name="user"/>, who reaches <paramref name="workAssignment"/> through
    /// Dataverse (a team, a role, the business unit), with the record's flag as the fixture's world holds it.</summary>
    private Task<AccessibleRecordSet> ComposeAsync(Guid user, Guid workAssignment)
    {
        _fixture.NoAccessReads.Flags[workAssignment] =
            new RootRecordFlags(IsSecure: _fixture.IsSecureOf(workAssignment) == true, IsRestricted: false);
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(user, WorkAssignment, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipResponse(
                WorkAssignment, new PersonIdentity(Guid.Empty, ContactId: null), new[] { workAssignment },
                new Dictionary<string, IReadOnlyList<Guid>>(), 1, DateTimeOffset.UtcNow.AddMinutes(5)));

        var sut = new AccessibleRecordSetService(
            membership.Object, _fixture.NoAccessReads, Mock.Of<ISubjectStandingGrantReader>(), _fixture.NoAccessList,
            UnlinkedIdentityStore(), InternalSystemUsers(),
            SecureChildShareWorld.EntitiesOver(() => _fixture.ChildWorld).Object,
            NullLogger<AccessibleRecordSetService>.Instance);

        return sut.ComposeAsync(new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = user,
            ContactId = null,
            Oid = Guid.NewGuid().ToString("D"),
            TenantId = "00000000-0000-0000-0000-0000000000cc",
        }, WorkAssignment, CancellationToken.None);
    }

    [Fact(DisplayName = "Round 82: inheritance REFUSED (a walled creator) leaves the record unsecured; the matter's list still hides it")]
    public async Task ARefusedInheritance_LeavesItUnsecured_AndTheMattersWallStillHidesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        _fixture.NoAccessReads.Flags[workAssignment] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, matter); // the creator is walled off the matter

        var result = await InheritAsync(workAssignment);

        result.Outcome.Should().Be(SecureRootInheritOutcome.Refused);
        _fixture.IsSecureOf(workAssignment).Should().NotBe(true, "a refused inheritance writes nothing");
        var set = await ComposeAsync(Creator, workAssignment);
        set.Rights.Should().NotContainKey(workAssignment, "the parent permissions control, whatever the record's own flag");
    }

    [Fact(DisplayName = "Round 82: inheritance FAILED (the flag write faults) leaves the record unsecured; the matter's list still hides it")]
    public async Task AFailedInheritance_LeavesItUnsecured_AndTheMattersWallStillHidesIt()
    {
        var (matter, workAssignment) = (Guid.NewGuid(), Guid.NewGuid());
        SecureMatter(_fixture, matter);
        FiledWorkAssignment(_fixture, workAssignment, "sprk_regardingmatter", "sprk_matter", matter);
        _fixture.SecureFlagWriteFails = true;

        var result = await InheritAsync(workAssignment);

        result.Outcome.Should().Be(SecureRootInheritOutcome.Failed);
        _fixture.IsSecureOf(workAssignment).Should().NotBe(true, "the flag write failed");
        _fixture.NoAccessList.DenySystemUserOnRecord(Colleague, matter); // a colleague walled off the matter, reaching it by a team
        var set = await ComposeAsync(Colleague, workAssignment);
        set.Rights.Should().NotContainKey(workAssignment, "the parent permissions control, whatever the record's own flag");
    }
}
