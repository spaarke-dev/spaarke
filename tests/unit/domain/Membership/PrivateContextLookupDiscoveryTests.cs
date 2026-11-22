using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Membership;

/// <summary>
/// field-mapping-server-write-path-r1 task 056b, condition C5 (design note §3.4 rule 3). Runtime metadata discovery of membership
/// lookups (<see cref="MembershipFieldDiscoveryService.DiscoverAsync"/>) keeps only lookups that target an identity table. A private
/// message's reference columns target core records (matter, project, work assignment), so discovery on <c>sprk_communication</c>
/// never returns them as membership fields: the reference never makes anyone a member of a message. The identity tables are the
/// production defaults (<see cref="MembershipOptionsDefaults"/>).
/// </summary>
public class PrivateContextLookupDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_OnCommunication_DoesNotReturnTheCoreRecordTargetedReferenceLookups()
    {
        var sut = Discovery(
            Lookup(PrivateContextColumns.Matter, "sprk_matter"),
            Lookup(PrivateContextColumns.Project, "sprk_project"),
            Lookup(PrivateContextColumns.WorkAssignment, "sprk_workassignment"),
            Lookup("sprk_sentby", "systemuser"));

        var result = await sut.DiscoverAsync("sprk_communication", CancellationToken.None);

        result.DiscoveredFields.Select(d => d.Field).Should().NotContain(PrivateContextColumns.All);
        result.IgnoredFields.Select(i => i.Field).Should().Contain(PrivateContextColumns.All,
            "a lookup to a core record is not an identity lookup");
    }

    /// <summary>Control: an identity-targeted lookup on the same table is returned, so the check above is not vacuous.</summary>
    [Fact]
    public async Task DiscoverAsync_OnCommunication_ReturnsAnIdentityTargetedLookup()
    {
        var sut = Discovery(
            Lookup(PrivateContextColumns.Matter, "sprk_matter"),
            Lookup("sprk_sentby", "systemuser"));

        var result = await sut.DiscoverAsync("sprk_communication", CancellationToken.None);

        result.DiscoveredFields.Should().ContainSingle(d => d.Field == "sprk_sentby")
            .Which.IdentityType.Should().Be("SystemUser");
    }

    private static MembershipFieldDiscoveryService.LookupAttributeRow Lookup(string column, string target) => new(column, new[] { target });

    private static CannedDiscovery Discovery(params MembershipFieldDiscoveryService.LookupAttributeRow[] lookups)
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(Options.DefaultName, options); return new CannedDiscovery(options, lookups);
    }

    /// <summary>The service with its metadata read answered from <paramref name="lookups"/> (the seam the service exposes for this).</summary>
    private sealed class CannedDiscovery(MembershipOptions options, IReadOnlyList<MembershipFieldDiscoveryService.LookupAttributeRow> lookups)
        : MembershipFieldDiscoveryService(
            Mock.Of<IDataverseService>(), new NoCache(), Options.Create(options), NullLogger<MembershipFieldDiscoveryService>.Instance)
    {
        protected override Task<IReadOnlyList<LookupAttributeRow>> FetchLookupAttributesAsync(string entityLogicalName, CancellationToken ct) =>
            Task.FromResult(lookups);
    }

    private sealed class NoCache : ITenantCache
    {
        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            Task.FromResult<T?>(default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default) =>
            factory(ct);
    }
}
