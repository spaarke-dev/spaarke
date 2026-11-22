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
/// production defaults (<see cref="MembershipOptionsDefaults"/>). The transitive discovery
/// (<see cref="MembershipFieldDiscoveryService.DiscoverLookupsTargetingAsync"/>) never returns them on <c>sprk_communication</c>
/// either, so the <c>includeRelated</c> expansion never reads messages by the reference.
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

    // ── DiscoverLookupsTargetingAsync: the lookups the includeRelated expansion reads by (app-only) ─────────────────────────

    /// <summary>
    /// The transitive expansion (<c>includeRelated=sprk_communication</c>) reads communication rows by every lookup this returns. A
    /// reference column targets the same core record as the filing lookup, so without the exclusion it would be returned and a
    /// private message would be selected by a record-scoped read. The regarding lookup is still returned (must-not-fire control).
    /// </summary>
    [Theory]
    [InlineData("sprk_matter", "sprk_regardingmatter")]
    [InlineData("sprk_project", "sprk_regardingproject")]
    [InlineData("sprk_workassignment", "sprk_regardingworkassignment")]
    public async Task DiscoverLookupsTargetingAsync_OnCommunication_ReturnsTheFilingLookupAndNeverTheReference(string coreTable, string regardingColumn)
    {
        var sut = PerTable(new()
        {
            ["sprk_communication"] = new[]
            {
                Lookup(regardingColumn, coreTable),
                Lookup(PrivateContextColumns.ColumnByTable[coreTable], coreTable),
                Lookup("sprk_sentby", "systemuser"),
            },
        });

        var result = await sut.DiscoverLookupsTargetingAsync("sprk_communication", coreTable, CancellationToken.None);

        result.Should().Equal(regardingColumn);
    }

    /// <summary>The source name is normalised before the rule applies, as for every other lookup.</summary>
    [Fact]
    public async Task DiscoverLookupsTargetingAsync_OnCommunicationInAnyCase_ExcludesTheReference()
    {
        var sut = PerTable(new()
        {
            ["sprk_communication"] = new[]
            {
                Lookup("sprk_regardingmatter", "sprk_matter"),
                Lookup(PrivateContextColumns.Matter.ToUpperInvariant(), "sprk_matter"),
            },
        });

        var result = await sut.DiscoverLookupsTargetingAsync(" SPRK_Communication ", "sprk_matter", CancellationToken.None);

        result.Should().Equal("sprk_regardingmatter");
    }

    /// <summary>
    /// Must-not-fire control: the rule is scoped to <c>sprk_communication</c>. Another table's lookups that target the same core
    /// record are returned unchanged, including a column that happens to share a reference column's name.
    /// </summary>
    [Fact]
    public async Task DiscoverLookupsTargetingAsync_OnAnotherTable_IsUnaffected()
    {
        var sut = PerTable(new()
        {
            ["sprk_document"] = new[]
            {
                Lookup("sprk_matter", "sprk_matter"),
                Lookup(PrivateContextColumns.Matter, "sprk_matter"),
                Lookup("sprk_project", "sprk_project"),
            },
        });

        var result = await sut.DiscoverLookupsTargetingAsync("sprk_document", "sprk_matter", CancellationToken.None);

        result.Should().Equal("sprk_matter", PrivateContextColumns.Matter);
    }

    private static MembershipFieldDiscoveryService.LookupAttributeRow Lookup(string column, string target) => new(column, new[] { target });

    private static TableDiscovery PerTable(Dictionary<string, MembershipFieldDiscoveryService.LookupAttributeRow[]> byTable)
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(Options.DefaultName, options);
        return new TableDiscovery(options, byTable);
    }

    /// <summary>The service with its metadata read answered per table from <paramref name="byTable"/>; an unknown table has none.</summary>
    private sealed class TableDiscovery(
        MembershipOptions options, IReadOnlyDictionary<string, MembershipFieldDiscoveryService.LookupAttributeRow[]> byTable)
        : MembershipFieldDiscoveryService(
            Mock.Of<IDataverseService>(), new NoCache(), Options.Create(options), NullLogger<MembershipFieldDiscoveryService>.Instance)
    {
        protected override Task<IReadOnlyList<LookupAttributeRow>> FetchLookupAttributesAsync(string entityLogicalName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LookupAttributeRow>>(
                byTable.TryGetValue(entityLogicalName, out var rows) ? rows : Array.Empty<LookupAttributeRow>());
    }

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
