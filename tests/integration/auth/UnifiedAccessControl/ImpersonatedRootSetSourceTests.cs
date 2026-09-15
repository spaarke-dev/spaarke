using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// FR-20 / task 035 — <see cref="ImpersonatedRootSetSource"/> must ask Dataverse which root records a
/// user can read, and must FAIL CLOSED in every direction that could silently widen access.
///
/// <para><b>What "fail closed" means on this specific path, and why it is not the obvious thing.</b>
/// App-only is the silent default of the whole Dataverse client. So the dangerous failure here is not
/// an exception — it is (a) quietly issuing an app-only query, which returns EVERY record in the org,
/// or (b) swallowing a fault into an empty set, which reads as "this user can see nothing". Both are
/// silent, and they are wrong in opposite directions. The tests below therefore assert on
/// PROPAGATION — that faults and empty-caller-ids escape rather than being absorbed.</para>
///
/// <para><b>Why the query seam is substituted rather than HTTP.</b> ADR-038 §7 B1 bans
/// <c>Mock&lt;HttpMessageHandler&gt;</c>. <see cref="IImpersonatedCommunicationQuery"/> is the module
/// boundary, and substituting it lets these tests assert the exact OData that was emitted — which is
/// the only place the id-only $select and the row cap are observable.</para>
///
/// Placement: <c>tests/integration/auth/**</c> — the ADR-038 §2 security-auth KEEP path.
/// </summary>
public sealed class ImpersonatedRootSetSourceTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ImpersonatedRootSetSource Build(
        StubImpersonatedQuery query, ITenantCache? cache = null) =>
        new(query, cache ?? new StubTenantCache(), NullLogger<ImpersonatedRootSetSource>.Instance);

    // =====================================================================
    // Positive — one impersonated, id-only query per root type
    // =====================================================================

    [Theory]
    [InlineData("sprk_project", "sprk_projects", "sprk_projectid")]
    [InlineData("sprk_matter", "sprk_matters", "sprk_matterid")]
    [InlineData("sprk_workassignment", "sprk_workassignments", "sprk_workassignmentid")]
    public async Task GetAsync_ForEachRootType_IssuesOneImpersonatedIdOnlyQueryAndReturnsTheIds(
        string entityType, string expectedEntitySet, string expectedIdColumn)
    {
        var id = Guid.NewGuid();
        var query = new StubImpersonatedQuery();
        query.Rows = new[] { Row(expectedIdColumn, id) };

        var result = await Build(query).GetAsync(User, entityType);

        result.Ids.Should().BeEquivalentTo(new[] { id });
        result.Truncated.Should().BeFalse();

        query.CallCount.Should().Be(1, "exactly one round trip per uncached entity type (NFR-02)");
        query.LastEntitySet.Should().Be(expectedEntitySet);
        query.LastCallerId.Should().Be(User, "the read must be impersonated AS the caller");
        // The id-only $select is the whole point — a $select-less query returns every column.
        query.LastODataQuery.Should().Contain($"$select={expectedIdColumn}");
        query.LastODataQuery.Should().Contain("$top=");
    }

    [Fact]
    public async Task GetAsync_NeverIssuesAQueryWithoutACallerId()
    {
        // The single most dangerous regression on this path: an impersonation parameter that goes
        // missing turns the read into an app-only query returning the WHOLE ORG. Asserting the caller
        // id reached the seam is the only way to see that from here.
        var query = new StubImpersonatedQuery();

        await Build(query).GetAsync(User, "sprk_matter");

        query.LastCallerId.Should().NotBe(Guid.Empty);
        query.LastCallerId.Should().Be(User);
    }

    // =====================================================================
    // Negative — the fail-closed contract
    // =====================================================================

    [Fact]
    public async Task GetAsync_WhenCallerIdIsEmpty_PropagatesTheRefusalAndNeverFallsBackToAppOnly()
    {
        // The primitive refuses Guid.Empty. This asserts the refusal ESCAPES: nothing here catches it
        // into an empty set, and no app-only query is issued in its place.
        var query = new StubImpersonatedQuery { ThrowOnEmptyCaller = true };

        var act = () => Build(query).GetAsync(Guid.Empty, "sprk_matter");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_WhenDataverseFaults_PropagatesAndNeverReturnsAPartialOrEmptySet()
    {
        // An empty set is a VALID answer ("you can read nothing"), which is precisely why a fault must
        // never be converted into one — the caller cannot tell the two apart, and the wrong one of the
        // two silently denies a user everything.
        var query = new StubImpersonatedQuery { Fault = new HttpRequestException("Dataverse 503") };

        var act = () => Build(query).GetAsync(User, "sprk_project");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("sprk_servicerequest")]
    [InlineData("sprk_document")]
    [InlineData("")]
    public async Task GetAsync_ForAnUnsupportedEntityType_Throws(string entityType)
    {
        // sprk_servicerequest is included deliberately: it IS a core type, but it is never externally
        // grantable (owner decision 2026-09-09, task 028), so it must not silently resolve here.
        var act = () => Build(new StubImpersonatedQuery()).GetAsync(User, entityType);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // =====================================================================
    // Truncation — never silent (NFR-03 groundwork)
    // =====================================================================

    [Fact]
    public async Task GetAsync_WhenTheRowCapIsHit_FlagsTruncated()
    {
        var query = new StubImpersonatedQuery
        {
            Rows = Enumerable.Range(0, ImpersonatedRootSetSource.RowCap)
                .Select(_ => Row("sprk_matterid", Guid.NewGuid())).ToArray()
        };

        var result = await Build(query).GetAsync(User, "sprk_matter");

        result.Truncated.Should().BeTrue(
            "a capped read is MISSING records the user can see; a caller that treats it as complete "
            + "under-grants invisibly");
        result.Ids.Should().HaveCount(ImpersonatedRootSetSource.RowCap);
    }

    [Fact]
    public async Task GetAsync_WhenTheCapIsHitByDuplicateIds_StillFlagsTruncated()
    {
        // Truncation is measured on ROWS RETURNED, not distinct ids. If it were measured on the id set,
        // a capped page containing duplicates would collapse below the cap and report itself complete.
        var duplicate = Guid.NewGuid();
        var query = new StubImpersonatedQuery
        {
            Rows = Enumerable.Range(0, ImpersonatedRootSetSource.RowCap)
                .Select(_ => Row("sprk_matterid", duplicate)).ToArray()
        };

        var result = await Build(query).GetAsync(User, "sprk_matter");

        result.Ids.Should().HaveCount(1);
        result.Truncated.Should().BeTrue();
    }

    // =====================================================================
    // Cache — keyed per user; a cache fault degrades the CACHE, never the ANSWER
    // =====================================================================

    [Fact]
    public async Task GetAsync_SecondCallWithinTtl_IssuesZeroDataverseQueries()
    {
        var query = new StubImpersonatedQuery { Rows = new[] { Row("sprk_matterid", Guid.NewGuid()) } };
        var cache = new StubTenantCache();
        var source = Build(query, cache);

        await source.GetAsync(User, "sprk_matter");
        await source.GetAsync(User, "sprk_matter");

        query.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_ForADifferentUser_NeverServesTheFirstUsersSet()
    {
        // Investigation 08 §3a-3: a cache key omitting systemUserId serves one user's accessible set to
        // another. That is a cross-user disclosure produced entirely by a cache key.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var query = new StubImpersonatedQuery { Rows = new[] { Row("sprk_matterid", mine) } };
        var cache = new StubTenantCache();
        var source = Build(query, cache);

        var first = await source.GetAsync(User, "sprk_matter");

        query.Rows = new[] { Row("sprk_matterid", theirs) };
        var second = await source.GetAsync(OtherUser, "sprk_matter");

        first.Ids.Should().BeEquivalentTo(new[] { mine });
        second.Ids.Should().BeEquivalentTo(new[] { theirs });
        query.CallCount.Should().Be(2, "a second user must cause a second query, not a cache hit");
    }

    [Fact]
    public async Task GetAsync_ForADifferentEntityType_DoesNotServeTheOtherTypesSet()
    {
        var project = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var query = new StubImpersonatedQuery { Rows = new[] { Row("sprk_projectid", project) } };
        var source = Build(query, new StubTenantCache());

        var p = await source.GetAsync(User, "sprk_project");
        query.Rows = new[] { Row("sprk_matterid", matter) };
        var m = await source.GetAsync(User, "sprk_matter");

        p.Ids.Should().BeEquivalentTo(new[] { project });
        m.Ids.Should().BeEquivalentTo(new[] { matter });
    }

    [Fact]
    public async Task GetAsync_WhenTheCacheReadFaults_FallsThroughToALiveQuery()
    {
        // Fail-open on the CACHE only. A Redis outage must degrade to a live impersonated query, never
        // to a failed authorization read and never to an empty set.
        var id = Guid.NewGuid();
        var query = new StubImpersonatedQuery { Rows = new[] { Row("sprk_matterid", id) } };
        var cache = new StubTenantCache { FaultOnGet = true };

        var result = await Build(query, cache).GetAsync(User, "sprk_matter");

        result.Ids.Should().BeEquivalentTo(new[] { id });
        query.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WhenTheCacheWriteFaults_StillReturnsTheLiveAnswer()
    {
        var id = Guid.NewGuid();
        var query = new StubImpersonatedQuery { Rows = new[] { Row("sprk_projectid", id) } };
        var cache = new StubTenantCache { FaultOnSet = true };

        var result = await Build(query, cache).GetAsync(User, "sprk_project");

        result.Ids.Should().BeEquivalentTo(new[] { id });
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static Dictionary<string, JsonElement> Row(string idColumn, Guid id)
    {
        using var doc = JsonDocument.Parse($"{{\"{idColumn}\":\"{id}\"}}");
        return new Dictionary<string, JsonElement>
        {
            [idColumn] = doc.RootElement.GetProperty(idColumn).Clone()
        };
    }

    private sealed class StubImpersonatedQuery : IImpersonatedCommunicationQuery
    {
        public IReadOnlyList<Dictionary<string, JsonElement>> Rows { get; set; }
            = Array.Empty<Dictionary<string, JsonElement>>();

        public Exception? Fault { get; set; }
        public bool ThrowOnEmptyCaller { get; set; }

        public int CallCount { get; private set; }
        public string? LastEntitySet { get; private set; }
        public string? LastODataQuery { get; private set; }
        public Guid LastCallerId { get; private set; }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            // Mirrors the real primitive's guard so the propagation test exercises the true contract
            // rather than a shape invented for the test.
            if (ThrowOnEmptyCaller && callerSystemUserId == Guid.Empty)
                throw new ArgumentException(
                    "An impersonated read requires a non-empty caller systemuserid.", nameof(callerSystemUserId));

            CallCount++;
            LastEntitySet = entitySetName;
            LastODataQuery = odataQuery;
            LastCallerId = callerSystemUserId;

            if (Fault is not null) throw Fault;

            return Task.FromResult(Rows);
        }
    }

    /// <summary>In-memory <see cref="ITenantCache"/> that can be made to fault on read or write.</summary>
    private sealed class StubTenantCache : ITenantCache
    {
        private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

        public bool FaultOnGet { get; set; }
        public bool FaultOnSet { get; set; }

        private static string Key(string tenantId, string resource, string id, int version)
            => $"{tenantId}:{resource}:{id}:v{version}";

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            if (FaultOnGet) throw new InvalidOperationException("simulated cache read fault");
            return Task.FromResult(_store.TryGetValue(Key(tenantId, resource, id, version), out var v)
                ? (T?)v
                : default);
        }

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
        {
            if (FaultOnSet) throw new InvalidOperationException("simulated cache write fault");
            _store[Key(tenantId, resource, id, version)] = value;
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version,
            Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            var hit = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
            if (hit is not null) return hit;
            var made = await factory(ct);
            await SetAsync(tenantId, resource, id, version, made, ttl, cacheInstance, ct);
            return made;
        }

        public Task<string?> GetStringAsync(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetStringAsync(string tenantId, string resource, string id, int version, string value,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
