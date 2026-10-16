using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Identity;

/// <summary>
/// Issue #1576 — both identity directions of <see cref="SystemUserIdentityResolver"/> exclude disabled systemusers: the
/// query each one sends to Dataverse carries <c>isdisabled = false</c> alongside its key condition.
/// </summary>
/// <remarks>The query is captured at the <see cref="IDataverseService"/> module boundary (ADR-038); the cache is a real
/// in-memory <see cref="IDistributedCache"/>, empty, so every call reaches the live read.</remarks>
public sealed class SystemUserIdentityResolverDisabledFilterTests
{
    private static readonly Guid Oid = Guid.Parse("15760000-0000-0000-0000-00000000cc01");
    private static readonly Guid SystemUserId = Guid.Parse("15760000-0000-0000-0000-00000000dd01");

    [Fact]
    public async Task Oid_to_systemuser_filters_out_disabled_users()
    {
        var (resolver, queries) = Build(new Entity("systemuser", SystemUserId));

        var resolved = await resolver.ResolveSystemUserIdAsync(Oid.ToString("D"));

        resolved.Should().Be(SystemUserId);
        Conditions(queries.Single()).Should().Contain(("azureactivedirectoryobjectid", Oid))
            .And.Contain(("isdisabled", false));
    }

    [Fact]
    public async Task Systemuser_to_oid_filters_out_disabled_users()
    {
        var row = new Entity("systemuser", SystemUserId) { ["azureactivedirectoryobjectid"] = Oid };
        var (resolver, queries) = Build(row);

        var resolved = await resolver.ResolveOidAsync(SystemUserId);

        resolved.Should().Be(Oid.ToString("D"));
        Conditions(queries.Single()).Should().Contain(("systemuserid", SystemUserId))
            .And.Contain(("isdisabled", false),
                "a disabled systemuser must not resolve to an oid that a notification or ping is then targeted at");
    }

    private static (SystemUserIdentityResolver Resolver, List<QueryExpression> Queries) Build(Entity answer)
    {
        var queries = new List<QueryExpression>();
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => queries.Add(q))
            .ReturnsAsync(new EntityCollection(new List<Entity> { answer }));

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return (new SystemUserIdentityResolver(dataverse.Object, cache, NullLogger<SystemUserIdentityResolver>.Instance), queries);
    }

    private static IEnumerable<(string Attribute, object Value)> Conditions(QueryExpression query)
        => query.Criteria.Conditions
            .Where(c => c.Operator == ConditionOperator.Equal && c.Values.Count == 1)
            .Select(c => (c.AttributeName, c.Values[0]));
}
