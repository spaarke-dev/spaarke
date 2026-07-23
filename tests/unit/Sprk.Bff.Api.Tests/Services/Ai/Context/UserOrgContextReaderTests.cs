using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarkeai-assistant-enhancements-r1 FR-E5 BU/team (un-defer D-032-01): <see cref="UserOrgContextReader"/>
/// reuses <see cref="IIdentityNormalizationService"/> (the SAME resolved systemuserid — no second identity
/// mechanism) for the caller's Redis-cached BU/team IDs, resolves the business-unit + team NAMES, sorts the
/// team names Ordinal, and Redis-caches the result per-systemuserid (NFR-03). Soft-fails to null (ADR-032 P2
/// quiet no-op) on an invalid id or any read error. Pinned against a mocked identity + Dataverse module
/// boundary + the process-local <see cref="InMemoryTenantCache"/>.
/// </summary>
public sealed class UserOrgContextReaderTests
{
    private const string SystemUserId = "9b0e6a1e-0000-4000-8000-00000000abcd";
    private static readonly Guid BusinessUnitId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeamA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeamB = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static UserOrgContextReader BuildReader(
        IIdentityNormalizationService identity, IDataverseService dataverse) =>
        new(identity, dataverse, new InMemoryTenantCache(), Mock.Of<ILogger<UserOrgContextReader>>());

    private static Mock<IIdentityNormalizationService> IdentityReturning(
        Guid? businessUnitId, params Guid[] teamIds)
    {
        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PersonIdentity(
                SystemUserId: id, BusinessUnitId: businessUnitId, TeamIds: teamIds));
        return identity;
    }

    private static Mock<IDataverseService> DataverseWith(
        string? businessUnitName, params (Guid id, string name)[] teams)
    {
        var mock = new Mock<IDataverseService>();
        mock.Setup(d => d.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] __, CancellationToken ___) =>
            {
                var e = new Entity("businessunit", id);
                if (businessUnitName is not null) e["name"] = businessUnitName;
                return e;
            });
        mock.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                if (q.EntityName != "team") return new EntityCollection();
                var rows = teams.Select(t => new Entity("team", t.id) { ["name"] = t.name }).ToList();
                return new EntityCollection(rows);
            });
        return mock;
    }

    [Fact]
    public async Task ReadAsync_UserWithBuAndTeams_ResolvesNamesAndSortsTeamsOrdinal()
    {
        var identity = IdentityReturning(BusinessUnitId, TeamA, TeamB);
        // Deliberately out of order to pin the reader's Ordinal sort.
        var dataverse = DataverseWith("Litigation Group", (TeamA, "Litigation"), (TeamB, "Corporate"));

        var context = await BuildReader(identity.Object, dataverse.Object).ReadAsync(SystemUserId, CancellationToken.None);

        context.Should().NotBeNull();
        context!.BusinessUnitName.Should().Be("Litigation Group");
        context.TeamNames.Should().Equal("Corporate", "Litigation");
    }

    [Fact]
    public async Task ReadAsync_InvalidSystemUserId_ReturnsNullWithoutResolving()
    {
        var identity = new Mock<IIdentityNormalizationService>();
        var dataverse = new Mock<IDataverseService>();

        var context = await BuildReader(identity.Object, dataverse.Object).ReadAsync("not-a-guid", CancellationToken.None);

        context.Should().BeNull();
        identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "an unparseable systemuserid short-circuits before any identity/Dataverse read");
    }

    [Fact]
    public async Task ReadAsync_NoBusinessUnitAndNoTeams_ReturnsNull()
    {
        var identity = IdentityReturning(businessUnitId: null);
        var dataverse = DataverseWith(businessUnitName: null);

        var context = await BuildReader(identity.Object, dataverse.Object).ReadAsync(SystemUserId, CancellationToken.None);

        context.Should().BeNull("a user with no BU and no teams contributes no org block");
    }

    [Fact]
    public async Task ReadAsync_IdentityThrows_SoftFailsToNull()
    {
        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse outage"));

        var context = await BuildReader(identity.Object, new Mock<IDataverseService>().Object)
            .ReadAsync(SystemUserId, CancellationToken.None);

        context.Should().BeNull("any identity/name read error soft-fails to null (ADR-032 P2 quiet no-op)");
    }

    [Fact]
    public async Task ReadAsync_SecondCall_ServesFromCacheWithoutReResolving()
    {
        // NFR-03: the resolved names are Redis-cached per-systemuserid — a second bind within the TTL must
        // NOT re-resolve the identity (nor re-read the names). Same cache instance across both calls.
        var identity = IdentityReturning(BusinessUnitId, TeamA);
        var dataverse = DataverseWith("Litigation Group", (TeamA, "Litigation"));
        var reader = BuildReader(identity.Object, dataverse.Object);

        var first = await reader.ReadAsync(SystemUserId, CancellationToken.None);
        var second = await reader.ReadAsync(SystemUserId, CancellationToken.None);

        first!.BusinessUnitName.Should().Be("Litigation Group");
        second!.BusinessUnitName.Should().Be("Litigation Group");
        second.TeamNames.Should().Equal("Litigation");
        identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once,
            "the second call is served from the per-systemuserid cache — no second identity resolution (NFR-03)");
    }
}
