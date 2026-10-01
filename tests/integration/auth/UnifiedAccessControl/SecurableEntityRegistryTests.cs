using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk.Metadata;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 151 (#1038) — the securable-entity registry's KNOWN-ENTITY answer, which is
/// what lets <see cref="RecordContainerResolver"/> refuse a name that is not an entity instead of reading it as
/// "a real entity that cannot be secure".
///
/// <para><b>What is real and what is substituted.</b> Only the metadata round trip is substituted (through the
/// registry's internal seam — a <c>ServiceClient</c> cannot be stood up in a test host). The catalog building,
/// the empty-never-cached rule, and the cache key + format all run as production code over a real
/// <see cref="MemoryDistributedCache"/>. What this CANNOT prove is the Dataverse premise that the unfiltered
/// entity query returns every entity — that is the task's manual live gate.</para>
/// </summary>
public class SecurableEntityRegistryTests
{
    /// <summary>The key the previous build wrote a bare securable-names array to.</summary>
    private const string PreviousBuildCacheKey = "sdap:dv:dv-securable-entities";

    // ============================================================================================
    // The known-entity set comes from the SAME query, and includes every entity it returned
    // ============================================================================================

    [Fact(DisplayName = "Task 151: every entity the metadata query returned is KNOWN — including those without sprk_issecure")]
    public async Task KnownEntities_IncludeEveryReturnedEntity_NotOnlyTheSecurableOnes()
    {
        var harness = new Harness(
            Meta("sprk_project", secure: true),
            Meta("sprk_matter", secure: true),
            Meta("sprk_invoice", secure: false),
            Meta("contact", secure: false));

        (await harness.Registry.IsKnownEntityAsync("sprk_invoice")).Should().BeTrue(
            "a real non-securable entity must stay resolvable — dropping it would refuse every ordinary upload");
        (await harness.Registry.IsKnownEntityAsync("Contact ")).Should().BeTrue("case-insensitive and trimmed");
        (await harness.Registry.IsKnownEntityAsync("sprk_project")).Should().BeTrue();

        (await harness.Registry.IsKnownEntityAsync("sprk_projects")).Should().BeFalse("an entity SET name is not an entity");
        (await harness.Registry.IsKnownEntityAsync("project")).Should().BeFalse("an alias is not a logical name");
        (await harness.Registry.IsKnownEntityAsync("sprk_projectt")).Should().BeFalse();

        // The securable answer is unchanged by the second set.
        (await harness.Registry.GetSecurableEntitiesAsync())
            .Should().BeEquivalentTo(["sprk_project", "sprk_matter"]);
        (await harness.Registry.IsSecurableAsync("sprk_invoice")).Should().BeFalse();

        harness.FetchCount.Should().Be(1, "both answers come from ONE metadata round trip");
    }

    // ============================================================================================
    // Fail closed: failures propagate, empty answers are never cached
    // ============================================================================================

    [Fact(DisplayName = "Task 151: a metadata failure PROPAGATES from IsKnownEntityAsync and nothing is cached")]
    public async Task MetadataFailure_Propagates_AndIsNotCached()
    {
        var harness = new Harness(throws: new TimeoutException("Dataverse metadata timed out"));

        var act = async () => await harness.Registry.IsKnownEntityAsync("sprk_invoice");

        await act.Should().ThrowAsync<TimeoutException>(
            "'could not find out' must never be answered as 'not an entity' or 'not securable'");
        (await harness.Cache.GetAsync(SecurableEntityRegistry.CacheKey)).Should().BeNull();
    }

    [Fact(DisplayName = "Task 151: an EMPTY metadata answer is not cached and is not read as 'unknown' — it throws")]
    public async Task EmptyMetadataAnswer_IsNotCached_AndThrowsRatherThanAnsweringUnknown()
    {
        var harness = new Harness();

        var act = async () => await harness.Registry.IsKnownEntityAsync("sprk_invoice");

        // Every org has entities, so "none" is a failed query wearing an answer's clothes. Answering false
        // would surface a metadata fault as a 400 the caller cannot fix.
        await act.Should().ThrowAsync<InvalidOperationException>();
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await harness.Cache.GetAsync(SecurableEntityRegistry.CacheKey)).Should().BeNull("an empty answer is never cached");
        harness.FetchCount.Should().Be(2, "not cached, so the next call re-queries");
    }

    [Fact(DisplayName = "Task 151: entities with NO securable among them are still not cached (pre-existing rule preserved)")]
    public async Task NoSecurableEntities_IsNotCached()
    {
        var harness = new Harness(Meta("sprk_invoice", secure: false), Meta("contact", secure: false));

        (await harness.Registry.GetSecurableEntitiesAsync()).Should().BeEmpty();
        (await harness.Registry.GetSecurableEntitiesAsync()).Should().BeEmpty();

        (await harness.Cache.GetAsync(SecurableEntityRegistry.CacheKey)).Should().BeNull();
        harness.FetchCount.Should().Be(2);
    }

    [Fact(DisplayName = "Task 151: a valid answer IS cached and served without a second metadata query")]
    public async Task ValidAnswer_IsCached_AndServedFromCache()
    {
        // Positive control for the four tests around it: without it, a registry that never cached at all would
        // pass every "is not cached" assertion.
        var harness = new Harness(Meta("sprk_project", secure: true), Meta("sprk_invoice", secure: false));

        (await harness.Registry.IsKnownEntityAsync("sprk_invoice")).Should().BeTrue();
        (await harness.Registry.IsKnownEntityAsync("sprk_invoice")).Should().BeTrue();

        harness.FetchCount.Should().Be(1);
        (await harness.Cache.GetAsync(SecurableEntityRegistry.CacheKey)).Should().NotBeNull();
    }

    // ============================================================================================
    // A value the PREVIOUS build cached must never be read as the known-entity set
    // ============================================================================================

    [Fact(DisplayName = "Task 151: the previous build's bare securable-names array is never read as the known-entity set")]
    public async Task PreviousBuildFormat_AtTheCurrentKey_IsIgnored_AndReQueried()
    {
        // Read as the known set, this value would make sprk_invoice "unknown" and refuse every ordinary upload
        // for the TTL; it must be treated as a miss instead.
        var harness = new Harness(Meta("sprk_project", secure: true), Meta("sprk_invoice", secure: false));
        await harness.Cache.SetAsync(
            SecurableEntityRegistry.CacheKey,
            Encoding.UTF8.GetBytes("[\"sprk_project\",\"sprk_matter\",\"sprk_workassignment\"]"));

        (await harness.Registry.IsKnownEntityAsync("sprk_invoice")).Should().BeTrue();
        harness.FetchCount.Should().Be(1, "the old-format value was a miss, so metadata was queried");
    }

    [Fact(DisplayName = "Task 151: the previous build's (unversioned) key is never consulted, whatever it holds")]
    public async Task PreviousBuildKey_IsNeverConsulted()
    {
        // Seeded with a WELL-FORMED current-format catalog that is WRONG (no sprk_invoice), so the only way this
        // passes is if the registry never reads the unversioned key at all.
        var harness = new Harness(Meta("sprk_project", secure: true), Meta("sprk_invoice", secure: false));
        await harness.Cache.SetAsync(
            PreviousBuildCacheKey,
            SecurableEntityRegistry.SerializeCacheEntry(["sprk_project"], ["sprk_project"]));

        (await harness.Registry.IsKnownEntityAsync("sprk_invoice")).Should().BeTrue();
        harness.FetchCount.Should().Be(1);
    }

    [Theory(DisplayName = "Task 151: a cached value that is not exactly a valid current-format catalog is a miss")]
    [InlineData("{\"v\":1,\"known\":[\"sprk_project\"],\"securable\":[\"sprk_project\"]}")]   // older format version
    [InlineData("{\"v\":2,\"known\":[\"sprk_project\"],\"securable\":[\"sprk_matter\"]}")]    // securable ⊄ known
    [InlineData("{\"v\":2,\"known\":[],\"securable\":[\"sprk_project\"]}")]                    // empty known set
    [InlineData("{\"v\":2,\"known\":[\"sprk_project\"],\"securable\":[]}")]                    // empty securable set
    [InlineData("{\"v\":2,\"known\":[],\"securable\":[]}")]                                    // both empty
    [InlineData("{\"v\":2,\"securable\":[\"sprk_project\"]}")]                                // known set missing
    public async Task MalformedOrForeignCacheValue_IsAMiss(string cachedJson)
    {
        var harness = new Harness(Meta("sprk_project", secure: true), Meta("sprk_invoice", secure: false));
        await harness.Cache.SetAsync(SecurableEntityRegistry.CacheKey, Encoding.UTF8.GetBytes(cachedJson));

        (await harness.Registry.IsKnownEntityAsync("sprk_invoice")).Should().BeTrue();
        harness.FetchCount.Should().Be(1);
    }

    // ============================================================================================
    // Machinery
    // ============================================================================================

    private static EntityMetadata Meta(string logicalName, bool secure)
    {
        var entity = new EntityMetadata { LogicalName = logicalName };

        // The attribute query is filtered to sprk_issecure, so an entity without it comes back with an EMPTY
        // attribute collection — the shape that must still count as KNOWN. `Attributes` has a non-public
        // setter in the SDK (it is normally populated by deserialization).
        AttributeMetadata[] attributes = secure
            ? [new BooleanAttributeMetadata { LogicalName = SecurableEntityRegistry.SecureFlagAttribute }]
            : [];
        typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(entity, attributes);

        return entity;
    }

    private sealed class Harness
    {
        public MemoryDistributedCache Cache { get; } =
            new(Options.Create(new MemoryDistributedCacheOptions()));

        public SecurableEntityRegistry Registry { get; }

        public int FetchCount { get; private set; }

        public Harness(params EntityMetadata[] metadata)
            : this(throws: null, metadata)
        {
        }

        public Harness(Exception? throws, params EntityMetadata[] metadata)
        {
            Registry = new SecurableEntityRegistry(
                _ =>
                {
                    FetchCount++;
                    return throws is null
                        ? Task.FromResult<IReadOnlyCollection<EntityMetadata>>(metadata)
                        : Task.FromException<IReadOnlyCollection<EntityMetadata>>(throws);
                },
                Cache,
                NullLogger<SecurableEntityRegistry>.Instance);
        }
    }
}
