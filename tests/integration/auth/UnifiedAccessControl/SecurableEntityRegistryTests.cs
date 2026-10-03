using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 151 (#1038) — the securable-entity registry's CLASSIFICATION answer ("not an
/// entity" / "not securable" / "securable"), which is what lets <see cref="RecordContainerResolver"/> refuse a
/// name that is not an entity instead of reading it as "a real entity that cannot be secure".
///
/// <para><b>What is real and what is substituted.</b> Only the metadata round trip is substituted (through the
/// registry's internal seam — a <c>ServiceClient</c> cannot be stood up in a test host). The catalog building,
/// the empty-never-cached rule, and the cache key + format all run as production code over a real
/// <see cref="MemoryDistributedCache"/> (or, for the cost tests, a cache that is DOWN). What this CANNOT prove is
/// the Dataverse premise that the unfiltered entity query returns every entity — that is the task's manual
/// live gate.</para>
/// </summary>
public class SecurableEntityRegistryTests
{
    /// <summary>The key the previous build wrote a bare securable-names array to.</summary>
    private const string PreviousBuildCacheKey = "sdap:dv:dv-securable-entities";

    private static readonly Guid RecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ============================================================================================
    // The known-entity set comes from the SAME query, and includes every entity it returned
    // ============================================================================================

    [Fact(DisplayName = "Task 151: every entity the metadata query returned is KNOWN — including those without sprk_issecure")]
    public async Task Classify_KnowsEveryReturnedEntity_NotOnlyTheSecurableOnes()
    {
        var harness = new Harness(
            Meta("sprk_project", secure: true),
            Meta("sprk_matter", secure: true),
            Meta("sprk_invoice", secure: false),
            Meta("contact", secure: false));

        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable,
            "a real non-securable entity must stay resolvable — dropping it would refuse every ordinary upload");
        (await harness.Registry.ClassifyEntityAsync("Contact ")).Should().Be(
            EntitySecurability.NotSecurable, "case-insensitive and trimmed");
        (await harness.Registry.ClassifyEntityAsync("sprk_project")).Should().Be(EntitySecurability.Securable);
        (await harness.Registry.ClassifyEntityAsync(" SPRK_Matter")).Should().Be(EntitySecurability.Securable);

        (await harness.Registry.ClassifyEntityAsync("sprk_projects")).Should().Be(
            EntitySecurability.NotAnEntity, "an entity SET name is not an entity");
        (await harness.Registry.ClassifyEntityAsync("project")).Should().Be(
            EntitySecurability.NotAnEntity, "an alias is not a logical name");
        (await harness.Registry.ClassifyEntityAsync("sprk_projectt")).Should().Be(EntitySecurability.NotAnEntity);

        // The securable answer is unchanged by the second set.
        (await harness.Registry.GetSecurableEntitiesAsync())
            .Should().BeEquivalentTo(["sprk_project", "sprk_matter"]);

        harness.FetchCount.Should().Be(1, "every answer comes from ONE metadata round trip, then the cache");
    }

    // ============================================================================================
    // Cost: ONE metadata round trip per resolve, even with the cache DOWN (task 151 review)
    // ============================================================================================

    [Theory(DisplayName = "Task 151: with the cache DOWN, one resolve costs exactly ONE metadata round trip — whatever the name")]
    [InlineData("sprk_invoice")]   // real, non-securable — the ordinary upload; the case that used to cost TWO
    [InlineData("contact")]        // real, non-securable, an identity alias
    [InlineData("sprk_projectt")]  // not an entity — refused; used to cost TWO before the refusal
    [InlineData("sprk_project")]   // securable
    [InlineData("project")]        // alias of a securable root
    public async Task OneResolve_WithTheCacheDown_FetchesMetadataExactlyOnce(string name)
    {
        // Redis down means EVERY catalog lookup is a miss and becomes a full-org metadata query. Before the
        // review fix the resolver asked IsSecurableAsync and then IsKnownEntityAsync, each fetching the catalog,
        // so a non-securable (or unknown) name cost two of those per upload. The REAL registry and the REAL
        // resolver are composed here; only the metadata call and the record read are substituted.
        var harness = new Harness(
            new DownDistributedCache(),
            throws: null,
            Meta("sprk_project", secure: true),
            Meta("sprk_invoice", secure: false),
            Meta("contact", secure: false));

        var records = Substitute.For<IGenericEntityService>();
        records.RetrieveAsync("sprk_project", RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_project", RecordId)
            {
                ["sprk_issecure"] = true,
                ["sprk_containerid"] = "b!own-container-00000000000000000"
            }));
        // Task 155: an invoice is a CHILD record, so its row is now read for its root links. A row with none —
        // the resolve completes on the fallback; what is under test is still only the metadata cost.
        records.RetrieveAsync("sprk_invoice", RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_invoice", RecordId)));
        // Task 155 f3: the same for a contact — its own sprk_invoice lookup (live sweep) means its row is read too.
        records.RetrieveAsync("contact", RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("contact", RecordId)));

        var resolver = new RecordContainerResolver(
            harness.Registry, records, NullLogger<RecordContainerResolver>.Instance);

        try
        {
            await resolver.ResolveForRecordAsync(name, RecordId, nonSecureFallbackContainerId: "b!shared-0000000000000");
        }
        catch (SdapProblemException ex) when (ex.Code == "container_entity_unknown")
        {
            // The refusal is the expected outcome for a non-entity; what is under test is its COST.
        }

        harness.FetchCount.Should().Be(1,
            "one resolve asks the registry ONE question; a second catalog lookup doubles the full-org metadata "
            + "cost of every upload whenever the cache is unavailable");
    }

    [Fact(DisplayName = "Task 155: a CHILD under a secure root asks the registry TWICE in one resolve, yet costs ONE metadata round trip with the cache DOWN")]
    public async Task ChildUnderASecureRoot_WithTheCacheDown_FetchesMetadataExactlyOnce()
    {
        // The child's own classification AND its root's ("can a project be secure?") are both catalog lookups.
        // The registry is Scoped, and it memoizes the catalog for its scope, so the second question is free —
        // without that memo it is a second full-org metadata query on every child upload while Redis is down,
        // the cost the task 151 review removed.
        var harness = new Harness(
            new DownDistributedCache(),
            throws: null,
            Meta("sprk_project", secure: true),
            Meta("sprk_todo", secure: false),
            Meta("businessunit", secure: false));

        var projectId = Guid.Parse("45454545-4545-4545-4545-454545454545");
        var records = Substitute.For<IGenericEntityService>();
        records.RetrieveAsync("sprk_todo", RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_todo", RecordId)
            {
                ["sprk_regardingproject"] = new EntityReference("sprk_project", projectId)
            }));
        records.RetrieveAsync("sprk_project", projectId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_project", projectId)
            {
                ["sprk_issecure"] = true,
                ["sprk_containerid"] = "b!secure-project-container-00000000"
            }));

        var resolver = new RecordContainerResolver(
            harness.Registry, records, NullLogger<RecordContainerResolver>.Instance);

        var decision = await resolver.ResolveForRecordAsync("sprk_todo", RecordId);

        decision.ContainerId.Should().Be("b!secure-project-container-00000000");
        harness.FetchCount.Should().Be(1, "both questions are answered from the scope's one catalog");
    }

    // ============================================================================================
    // Fail closed: failures propagate, empty answers are never cached
    // ============================================================================================

    [Fact(DisplayName = "Task 151: a metadata failure PROPAGATES from ClassifyEntityAsync and nothing is cached")]
    public async Task MetadataFailure_Propagates_AndIsNotCached()
    {
        var harness = new Harness(throws: new TimeoutException("Dataverse metadata timed out"));

        var act = async () => await harness.Registry.ClassifyEntityAsync("sprk_invoice");

        await act.Should().ThrowAsync<TimeoutException>(
            "'could not find out' must never be answered as 'not an entity' or 'not securable'");
        (await harness.Cache.GetAsync(SecurableEntityRegistry.CacheKey)).Should().BeNull();
    }

    [Fact(DisplayName = "Task 151: an EMPTY metadata answer is not cached and is not read as 'not an entity' — it throws")]
    public async Task EmptyMetadataAnswer_IsNotCached_AndThrowsRatherThanAnsweringNotAnEntity()
    {
        var harness = new Harness();

        var act = async () => await harness.Registry.ClassifyEntityAsync("sprk_invoice");

        // Every org has entities, so "none" is a failed query wearing an answer's clothes. Answering
        // NotAnEntity would surface a metadata fault as a 400 the caller cannot fix.
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

        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable);
        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable);

        harness.FetchCount.Should().Be(1);
        (await harness.Cache.GetAsync(SecurableEntityRegistry.CacheKey)).Should().NotBeNull();
    }

    // ============================================================================================
    // A value the PREVIOUS build cached must never be read as the known-entity set
    // ============================================================================================

    [Fact(DisplayName = "Task 151: the previous build's bare securable-names array is never read as the known-entity set")]
    public async Task PreviousBuildFormat_AtTheCurrentKey_IsIgnored_AndReQueried()
    {
        // Read as the known set, this value would make sprk_invoice "not an entity" and refuse every ordinary
        // upload for the TTL; it must be treated as a miss instead.
        var harness = new Harness(Meta("sprk_project", secure: true), Meta("sprk_invoice", secure: false));
        await harness.Cache.SetAsync(
            SecurableEntityRegistry.CacheKey,
            Encoding.UTF8.GetBytes("[\"sprk_project\",\"sprk_matter\",\"sprk_workassignment\"]"));

        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable);
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

        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable);
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

        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable);
        harness.FetchCount.Should().Be(1);
    }

    // ============================================================================================
    // Task 150 (owner round 10 item 11): invoices follow their matter — the invoice's own flag is not a security input
    // ============================================================================================

    private static readonly Guid MatterId = Guid.Parse("15015015-0000-0000-0000-00000000a771");
    private const string SharedFallback = "b!shared-fallback-000000000000000";
    private const string SecureMatterContainer = "b!secure-matter-container-0000000";

    /// <summary>Live dev's shape: the invoice table CARRIES sprk_issecure (task 151 live gate).</summary>
    private static Harness LiveShapedHarness() => new(
        Meta("sprk_project", secure: true),
        Meta("sprk_matter", secure: true),
        Meta("sprk_workassignment", secure: true),
        Meta("sprk_invoice", secure: true),
        Meta("businessunit", secure: false));

    [Fact(DisplayName = "Task 150: sprk_invoice carries sprk_issecure, yet it is NOT securable — and it stays a known entity")]
    public async Task Invoice_CarryingTheFlag_IsNotSecurable_AndStaysKnown()
    {
        var harness = LiveShapedHarness();

        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable,
            "owner round 10 item 11: an invoice follows its matter, so its own flag decides nothing — and it must stay KNOWN, "
            + "or every invoice upload would be refused as 'not an entity'");
        (await harness.Registry.GetSecurableEntitiesAsync())
            .Should().BeEquivalentTo(["sprk_project", "sprk_matter", "sprk_workassignment"]);
    }

    [Fact(DisplayName = "Task 150: a catalog cached BEFORE the invoice rule (invoice listed securable) is read without the invoice")]
    public async Task ACatalogCachedBeforeTheInvoiceRule_IsReadWithoutTheInvoice()
    {
        var harness = LiveShapedHarness();
        await harness.Cache.SetAsync(
            SecurableEntityRegistry.CacheKey,
            SecurableEntityRegistry.SerializeCacheEntry(
                ["sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice", "businessunit"],
                ["sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice"]));

        (await harness.Registry.ClassifyEntityAsync("sprk_invoice")).Should().Be(EntitySecurability.NotSecurable,
            "a value an earlier build cached (6h TTL) must not keep the invoice's flag a security input after deploy");
        (await harness.Registry.ClassifyEntityAsync("sprk_matter")).Should().Be(EntitySecurability.Securable);
        harness.FetchCount.Should().Be(0, "the cached value was valid and was served — the rule applied to it on read");
    }

    [Fact(DisplayName = "Task 150: an invoice FLAGGED secure under an ORDINARY matter is not secure — its matter decides, and its flag is never read")]
    public async Task AnInvoiceFlaggedTrue_UnderAnOrdinaryMatter_IsNotTreatedAsSecure()
    {
        var harness = LiveShapedHarness();
        var records = Substitute.For<IGenericEntityService>();
        records.RetrieveAsync("sprk_invoice", RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_invoice", RecordId)
            {
                ["sprk_issecure"] = true,   // what a user could set before the lock; no container of its own
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterId)
            }));
        records.RetrieveAsync("sprk_matter", MatterId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_matter", MatterId) { ["sprk_issecure"] = false }));

        var resolver = new RecordContainerResolver(harness.Registry, records, NullLogger<RecordContainerResolver>.Instance);

        var decision = await resolver.ResolveForRecordAsync("sprk_invoice", RecordId, nonSecureFallbackContainerId: SharedFallback);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback,
            "before task 150 the invoice's own true flag made it a secure record with no container — every upload refused");
        decision.ContainerId.Should().Be(SharedFallback);
        await records.Received(1).RetrieveAsync("sprk_invoice", RecordId,
            Arg.Is<string[]>(columns => !columns.Contains(SecurableEntityRegistry.SecureFlagAttribute)),
            Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "Task 150: an invoice under a SECURE matter IS secure — whatever its own flag says")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnInvoiceUnderASecureMatter_IsSecure_WhateverItsOwnFlag(bool invoiceFlag)
    {
        var harness = LiveShapedHarness();
        var records = Substitute.For<IGenericEntityService>();
        records.RetrieveAsync("sprk_invoice", RecordId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_invoice", RecordId)
            {
                ["sprk_issecure"] = invoiceFlag,
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterId)
            }));
        records.RetrieveAsync("sprk_matter", MatterId, Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Entity("sprk_matter", MatterId)
            {
                ["sprk_issecure"] = true,
                ["sprk_containerid"] = SecureMatterContainer
            }));

        var resolver = new RecordContainerResolver(harness.Registry, records, NullLogger<RecordContainerResolver>.Instance);

        var decision = await resolver.ResolveForRecordAsync("sprk_invoice", RecordId, nonSecureFallbackContainerId: SharedFallback);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);
        decision.ContainerId.Should().Be(SecureMatterContainer, "the invoice follows its secure matter (owner C10 part 2)");
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

    /// <summary>
    /// A distributed cache that is UNAVAILABLE — every operation throws, as a down Redis does. The registry
    /// treats cache failures as misses (graceful, per ADR-009), so with this cache every catalog lookup becomes
    /// a live metadata query and the fetch counter measures exactly how many lookups a caller made.
    /// </summary>
    private sealed class DownDistributedCache : IDistributedCache
    {
        private static Exception Down() => new InvalidOperationException("Redis unavailable (test)");

        public byte[]? Get(string key) => throw Down();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromException<byte[]?>(Down());
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => Task.FromException(Down());
        public void Refresh(string key) => throw Down();
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(Down());
        public void Remove(string key) => throw Down();
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(Down());
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
            : this(cache: null, throws, metadata)
        {
        }

        /// <param name="cache">The registry's cache; <see langword="null"/> uses <see cref="Cache"/>.</param>
        /// <param name="throws">When set, every metadata round trip fails with it.</param>
        /// <param name="metadata">What the metadata round trip returns.</param>
        public Harness(IDistributedCache? cache, Exception? throws, params EntityMetadata[] metadata)
        {
            Registry = new SecurableEntityRegistry(
                _ =>
                {
                    FetchCount++;
                    return throws is null
                        ? Task.FromResult<IReadOnlyCollection<EntityMetadata>>(metadata)
                        : Task.FromException<IReadOnlyCollection<EntityMetadata>>(throws);
                },
                cache ?? Cache,
                NullLogger<SecurableEntityRegistry>.Instance);
        }
    }
}
