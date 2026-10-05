using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 132 — the two batch 4 integration residuals.
/// <list type="number">
/// <item><b>Share-only changes evict like owner changes.</b> A POA grant, rights change or revoke changes who can read a
/// record, so the ONE POA seam (<see cref="DataverseRecordShareService"/>) calls
/// <see cref="IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync"/> after every write — returned or thrown, not
/// bound to the caller's token, never failing the write. Every share writer (InternalShareEndpoints share/unshare,
/// provisioning's creator share and its restore, the resume error paths, SecureChildShareSynchronizer's fan-out, …)
/// reaches Dataverse only through that seam; <c>PoaShareClientSingletonGuardTests</c> pins that per writer, and by an IL
/// scan of the BFF's assemblies for any compiled route around the seam. That guard pins the seam's writes to its three
/// methods; the cases below are what prove each of the three evicts.</item>
/// <item><b>No eviction for a type no cache holds.</b> The children an Assign cascade re-owns
/// (<c>sharepointdocumentlocation</c>, <c>sharepointdocument</c>) are cached by no access cache, so the owner-change hook
/// builds no pattern for them and touches Redis not at all — while a root's eviction is unchanged.</item>
/// </list>
/// </summary>
/// <remarks>
/// Same instruments as the rest of this class: the production readers write the entries into one
/// <see cref="InMemoryRedisKeyspace"/>; the production invalidator scans it. The seam runs over the production
/// <see cref="DataverseWebApiService"/> against an in-memory server standing in for the Dataverse Web API (ADR-038 §7's
/// named replacement for B1 — no transport double; the request builder, the auth header and the status handling are the
/// production code).
/// </remarks>
public sealed partial class AccessCacheInvalidationTests
{
    private static readonly Guid Document = Guid.Parse("13200000-0000-0000-0000-0000000006d1");
    private static readonly Guid OtherDocument = Guid.Parse("13200000-0000-0000-0000-0000000006d2");

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Residual 1 — share-only changes: the patterns reach the real keys, and only them (criterion 23 for the new hook)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A share change on a project removes every user's impersonated root set for projects (the set is answered by an
    /// impersonated query, which sees shares; a team share changes every member's) and every user's snapshot of THAT
    /// project, under every tenant — and leaves membership and identity (a share is not a membership term), the matter
    /// root sets, another project's snapshot and the same id under another entity set.
    /// </summary>
    [Fact]
    public async Task ShareChangeEviction_RemovesEveryUsersRootSetForTheType_AndEverySnapshotOfTheRecord_Only()
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        var otherRecord = Guid.Parse("13200000-0000-0000-0000-0000000003c2");
        await world.WarmUserAsync(UserU, TenantA);
        await world.WarmUserAsync(UserV, TenantB);
        await world.WarmSnapshotAsync(OidU, "sprk_projects", record, TenantA);
        await world.WarmSnapshotAsync(OidV, "sprk_projects", record, TenantB);
        await world.WarmSnapshotAsync(OidU, "sprk_projects", otherRecord, TenantA);
        await world.WarmSnapshotAsync(OidU, "sprk_matters", record, TenantA);
        var before = world.Keyspace.Keys.ToHashSet();

        var expectedGone = before.Where(k =>
                (k.Contains($":{ImpersonatedRootSetSource.CacheResource}:", StringComparison.Ordinal) && k.Contains(":sprk_project:", StringComparison.Ordinal))
                || (k.Contains($":{CachedAccessDataSource.RecordAccessResource}:sprk_projects:", StringComparison.Ordinal) && k.Contains(record.ToString("D"), StringComparison.Ordinal)))
            .ToHashSet();
        expectedGone.Should().HaveCount(2 + 2, "precondition: each user's project root set, and two snapshots of the record");

        foreach (var pattern in MembershipCacheInvalidator.RecordShareChangePatterns(InMemoryRedisKeyspace.InstanceName, "sprk_projects", record))
        {
            expectedGone.Should().Contain(k => InMemoryRedisKeyspace.Matches(pattern, k),
                $"pattern '{pattern}' must reach a key a production reader wrote — a pattern that matches nothing evicts nothing");
        }

        await world.Keyspace.Invalidator().InvalidateRecordShareChangeAsync("sprk_projects", record, "test", CancellationToken.None);

        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(expectedGone),
            "identity, membership, matter root sets, the other project's snapshot and the matter-set snapshot all stay");
    }

    /// <summary>
    /// A share change on a DOCUMENT (task 149's child fan-out shares documents) also removes the document-scoped snapshot,
    /// whose key carries no entity set — however the request that cached it spelled the id (upper case, braces) — and
    /// builds no root-set pattern (a document is not a root).
    /// </summary>
    [Fact]
    public async Task ShareChangeEviction_OnADocument_RemovesItsDocumentAndRecordSnapshots_HoweverTheIdWasSpelled()
    {
        var world = new AccessCacheWorld();
        await world.WarmUserAsync(UserU, TenantA);
        await world.WarmDocumentSnapshotAsync(OidU, Document.ToString("D").ToUpperInvariant(), TenantA);
        await world.WarmDocumentSnapshotAsync(OidV, Document.ToString("B"), TenantB);
        await world.WarmDocumentSnapshotAsync(OidU, OtherDocument.ToString("D"), TenantA);
        await world.WarmSnapshotAsync(OidV, DataverseAccessDataSource.DocumentEntitySetName, Document, TenantA);
        var before = world.Keyspace.Keys.ToHashSet();

        var expectedGone = before.Where(k => k.Contains(Document.ToString("D"), StringComparison.Ordinal)).ToHashSet();
        expectedGone.Should().HaveCount(3, "precondition: two document snapshots (both spellings land on ONE id format) and one record snapshot");

        await world.Keyspace.Invalidator().InvalidateRecordShareChangeAsync(
            DataverseAccessDataSource.DocumentEntitySetName, Document, "test", CancellationToken.None);

        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(expectedGone),
            "every snapshot of the document goes; the other document's snapshot and the user's root sets stay");
        world.Keyspace.ScannedPatterns.Should().NotContain(p => p.Contains(ImpersonatedRootSetSource.CacheResource, StringComparison.Ordinal),
            "a document is not a root type: no root-set pattern is built, so none is scanned");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Residual 1 — the POA seam evicts after every write: returned, refused, or cancelled by the caller
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every write of the PRODUCTION POA seam evicts that record — a grant (InternalShareEndpoints share, provisioning's
    /// share-first creator grant, the resume error paths' unconfirmed grant, the child fan-out's grant), a rights change
    /// (a level change, the creator-share restore to its old mask) and a revoke (unshare, the restore that removes an
    /// issued share, the fan-out's removal) — and the write's own outcome is what the caller sees: a refused write still
    /// throws, AFTER the eviction, because a write that reports failure can have committed.
    /// </summary>
    [Theory]
    [InlineData("grant", false)]
    [InlineData("modify", false)]
    [InlineData("revoke", false)]
    [InlineData("grant", true)]
    [InlineData("modify", true)]
    [InlineData("revoke", true)]
    public async Task PoaSeam_EveryShareWrite_EvictsThatRecord_AndTheWritesOwnOutcomeStands(string write, bool dataverseRefuses)
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        await world.WarmUserAsync(UserV, TenantA);
        await world.WarmSnapshotAsync(OidV, "sprk_projects", record, TenantA);
        await world.WarmSnapshotAsync(OidU, "sprk_projects", record, TenantB);
        var before = world.Keyspace.Keys.ToHashSet();
        var expectedGone = before.Where(k =>
                (k.Contains($":{ImpersonatedRootSetSource.CacheResource}:", StringComparison.Ordinal) && k.Contains(":sprk_project:", StringComparison.Ordinal))
                || k.Contains($":{CachedAccessDataSource.RecordAccessResource}:", StringComparison.Ordinal))
            .ToHashSet();
        expectedGone.Should().HaveCount(1 + 2, "precondition: the colleague's project root set and two snapshots of the record");

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: dataverseRefuses);
        var seam = dataverse.Seam(world.Keyspace.Invalidator());

        var act = () => RunShareWriteAsync(seam, write, record, CancellationToken.None);

        if (dataverseRefuses)
        {
            await act.Should().ThrowAsync<HttpRequestException>("the write's own failure is what the caller sees");
        }
        else
        {
            await act.Should().NotThrowAsync();
        }

        dataverse.Writes.Should().ContainSingle("precondition: the POA action reached Dataverse");
        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(expectedGone),
            $"the {write} (refused={dataverseRefuses}) may have changed who can read the record; its root sets and snapshots are gone");
    }

    /// <summary>
    /// A caller that goes away mid-write still leaves the caches clean: the eviction is not bound to the request's token
    /// (CancellationToken.None), and the caller's cancellation still propagates.
    /// </summary>
    [Fact]
    public async Task PoaSeam_WhenTheCallerCancels_TheEvictionStillRuns_AndTheCancellationPropagates()
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        await world.WarmSnapshotAsync(OidV, "sprk_projects", record, TenantA);
        world.Keyspace.Keys.Should().ContainSingle("precondition");

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: false);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => RunShareWriteAsync(dataverse.Seam(world.Keyspace.Invalidator()), "revoke", record, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        world.Keyspace.Keys.Should().BeEmpty("the eviction ran with CancellationToken.None after the cancelled write");
    }

    /// <summary>
    /// The SEAM's own guard: an invalidator that throws (the hook's contract is never to, so this is the defect case the
    /// seam's catch exists for) never changes the write's outcome. A write that succeeded completes normally, a write
    /// Dataverse refused still surfaces ITS failure (not the eviction's), and the seam logs the eviction failure it
    /// swallowed. A seam that stopped catching fails this test with the double's exception.
    /// </summary>
    [Theory]
    [InlineData("grant", false)]
    [InlineData("modify", false)]
    [InlineData("revoke", false)]
    [InlineData("grant", true)]
    [InlineData("modify", true)]
    [InlineData("revoke", true)]
    public async Task PoaSeam_WhenEvictionThrows_TheSeamCatchesAndLogs_AndTheWritesOwnOutcomeStands(string write, bool dataverseRefuses)
    {
        var logs = new ProvisionProjectTestFixture.LogCapture();
        using var loggers = new LoggerFactory(new[] { logs });
        var invalidator = new ThrowingInvalidator();

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: dataverseRefuses);
        var seam = dataverse.Seam(invalidator, loggers.CreateLogger<DataverseRecordShareService>());

        var act = () => RunShareWriteAsync(seam, write, AccessCacheWorld.Project, CancellationToken.None);

        if (dataverseRefuses)
        {
            (await act.Should().ThrowAsync<HttpRequestException>(
                    "the write's own failure is what the caller sees, not the eviction's"))
                .Which.Should().NotBeSameAs(ThrowingInvalidator.Fault);
        }
        else
        {
            await act.Should().NotThrowAsync("the share has already been written; the TTL is the backstop");
        }

        dataverse.Writes.Should().ContainSingle("precondition: the POA action reached Dataverse");
        invalidator.ShareChangeCalls.Should().Be(1, "precondition: the seam attempted the eviction once, after the write");
        logs.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning
            && e.Message.Contains("[ACCESS-EVICT] The share-change eviction for", StringComparison.Ordinal)
            && e.Message.Contains($"({write})", StringComparison.Ordinal),
            "the seam's catch logs the eviction failure it swallowed");
    }

    /// <summary>
    /// The production invalidator's own resilience under the seam: when Redis fails mid-scan the invalidator logs and
    /// returns (its "never throws" contract), so the share write completes normally and the seam has nothing to catch.
    /// </summary>
    [Fact]
    public async Task PoaSeam_WhenRedisFails_TheInvalidatorLogsAndReturns_AndTheShareWriteSucceeds()
    {
        var world = new AccessCacheWorld();
        await world.WarmSnapshotAsync(OidV, "sprk_projects", AccessCacheWorld.Project, TenantA);
        world.Keyspace.FailScans = true;
        var logs = new ProvisionProjectTestFixture.LogCapture();
        using var loggers = new LoggerFactory(new[] { logs });

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: false);
        var seam = dataverse.Seam(
            world.Keyspace.Invalidator(logger: loggers.CreateLogger<MembershipCacheInvalidator>()),
            loggers.CreateLogger<DataverseRecordShareService>());

        var act = () => RunShareWriteAsync(seam, "grant", AccessCacheWorld.Project, CancellationToken.None);

        await act.Should().NotThrowAsync("the share has already been written; the TTL is the backstop");
        dataverse.Writes.Should().ContainSingle();
        world.Keyspace.ScannedPatterns.Should().NotBeEmpty("precondition: the eviction was attempted");
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("[ACCESS-EVICT] Eviction of pattern", StringComparison.Ordinal),
            "the invalidator logged the Redis failure itself");
        logs.Entries.Should().NotContain(e => e.Message.Contains("[ACCESS-EVICT] The share-change eviction for", StringComparison.Ordinal),
            "the invalidator did not throw, so the seam's catch had nothing to log");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Residual 2 — no eviction for a type no cache holds; root evictions unchanged
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provisioning's compensation puts each cascaded child back on its own owner and calls the owner-change hook for it.
    /// For <c>sharepointdocumentlocation</c> and <c>sharepointdocument</c> no cache can hold an entry, so the hook builds
    /// no pattern and scans nothing (it used to scan the whole key space three times per child). A ROOT's owner change
    /// still builds all three patterns.
    /// </summary>
    [Theory]
    [InlineData("sharepointdocumentlocation", "sharepointdocumentlocations")]
    [InlineData("sharepointdocument", "sharepointdocuments")]
    public async Task CascadeChildOwnerChange_BuildsNoPattern_AndScansNothing_WhileARootStillScans(string child, string childSet)
    {
        var world = new AccessCacheWorld();
        await world.WarmUserAsync(UserU, TenantA);
        var before = world.Keyspace.Keys.ToHashSet();
        var childId = Guid.Parse("13200000-0000-0000-0000-0000000007e1");

        MembershipCacheInvalidator.RecordOwnerChangePatterns(InMemoryRedisKeyspace.InstanceName, child, childSet, childId)
            .Should().BeEmpty($"no access cache holds a {child} entry");
        MembershipCacheInvalidator.RecordShareChangePatterns(InMemoryRedisKeyspace.InstanceName, childSet, childId)
            .Should().BeEmpty($"no access cache holds a {childSet} snapshot either");

        var invalidator = world.Keyspace.Invalidator();
        await invalidator.InvalidateRecordOwnerChangeAsync(child, childSet, childId, "test", CancellationToken.None);
        await invalidator.InvalidateRecordShareChangeAsync(childSet, childId, "test", CancellationToken.None);

        world.Keyspace.ScannedPatterns.Should().BeEmpty("a child the cascade re-owns touches Redis not at all");
        world.Keyspace.Keys.Should().BeEquivalentTo(before);

        await invalidator.InvalidateRecordOwnerChangeAsync(
            AccessCacheWorld.ProjectEntity, "sprk_projects", AccessCacheWorld.Project, "test", CancellationToken.None);
        world.Keyspace.ScannedPatterns.Should().HaveCount(3, "the root's eviction is unchanged: membership, root set, snapshot");
    }

    /// <summary>
    /// WHY the child patterns can be dropped: the readers never cache a cascade-child type — so the forward Assign (which
    /// re-owns them with no eviction at all) can never leave one stale either. Both membership planes and the
    /// record-access snapshot read such a type live, every time; a root type is still cached (control).
    /// </summary>
    [Fact]
    public async Task CascadeChildTables_AreNeverCached_SoNeitherTheCascadeNorItsRestoreCanLeaveOneStale()
    {
        var world = new AccessCacheWorld();
        var readers = world.Readers(TenantA);
        var contact = Guid.Parse("13200000-0000-0000-0000-0000000008f1");
        var location = Guid.Parse("13200000-0000-0000-0000-0000000007e2");

        await readers.Membership.ResolveAsync(UserU, AccessCacheWorld.CascadeChildEntity, AccessCacheWorld.AuthorizationOptions, CancellationToken.None);
        await readers.Membership.ResolveByContactAsync(contact, AccessCacheWorld.CascadeChildEntity, AccessCacheWorld.AuthorizationOptions, CancellationToken.None);
        await readers.Snapshots.GetRecordAccessAsync(OidU, "sharepointdocumentlocations", location, "user-token");
        await readers.Snapshots.GetRecordAccessAsync(OidU, "sharepointdocumentlocations", location, "user-token");

        world.Keyspace.Keys.Should().NotContain(k => k.Contains(AccessCacheWorld.CascadeChildEntity, StringComparison.Ordinal),
            "no membership entry (either plane) and no snapshot is written for a table the Assign cascade re-owns");
        world.SnapshotReads.Should().Be(2, "the second snapshot read went to Dataverse again — nothing was cached");

        var queriesBefore = world.MembershipQueries;
        await readers.Membership.ResolveAsync(UserU, AccessCacheWorld.CascadeChildEntity, AccessCacheWorld.AuthorizationOptions, CancellationToken.None);
        world.MembershipQueries.Should().Be(queriesBefore + 1, "the systemuser-plane resolve re-read: nothing was cached");

        await world.WarmSnapshotAsync(OidU, "sprk_projects", AccessCacheWorld.Project, TenantA);
        world.Keyspace.Keys.Should().Contain(k => k.Contains(CachedAccessDataSource.RecordAccessResource, StringComparison.Ordinal),
            "control: a root's snapshot is still cached");
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────

    private static Task RunShareWriteAsync(IDataverseRecordShareService seam, string write, Guid record, CancellationToken ct)
    {
        var principal = DataversePrincipalRef.User(UserU);
        return write switch
        {
            "grant" => seam.GrantAccessAsync("sprk_projects", record, principal, "ReadAccess", ct),
            "modify" => seam.ModifyAccessAsync("sprk_projects", record, principal, "ReadAccess,WriteAccess", ct),
            "revoke" => seam.RevokeAccessAsync("sprk_projects", record, principal, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown share write."),
        };
    }

    /// <summary>
    /// An invalidator with a defect: every hook throws. The hook's contract is never to throw; this stands in for the
    /// defect the seam's own catch guards against.
    /// </summary>
    private sealed class ThrowingInvalidator : IMembershipCacheInvalidator
    {
        public static readonly InvalidOperationException Fault = new("invalidator defect (test double)");

        private int _shareChangeCalls;

        public int ShareChangeCalls => Volatile.Read(ref _shareChangeCalls);

        public Task PublishInvalidationAsync(Guid personId, string entityLogicalName, string? correlationId, CancellationToken ct)
            => throw Fault;

        public Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct) => throw Fault;

        public Task InvalidateRecordOwnerChangeAsync(
            string entityLogicalName, string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
            => throw Fault;

        public Task InvalidateRecordShareChangeAsync(string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
        {
            Interlocked.Increment(ref _shareChangeCalls);
            throw Fault;
        }
    }

    /// <summary>
    /// The Dataverse Web API's three POA actions over an in-memory server: 204 for each, or 500 when told to refuse. The
    /// PRODUCTION seam runs over the PRODUCTION <see cref="DataverseWebApiService"/> pointed at it.
    /// </summary>
    private sealed class PoaDataverse : IAsyncDisposable
    {
        private const string ServiceUrl = "https://own.crm.dynamics.com";
        private readonly WebApplication _app;
        private readonly bool _refuseWrites;

        private PoaDataverse(WebApplication app, bool refuseWrites)
        {
            _app = app;
            _refuseWrites = refuseWrites;
        }

        public ConcurrentQueue<string> Writes { get; } = new();

        public static async Task<PoaDataverse> StartAsync(bool refuseWrites)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new PoaDataverse(app, refuseWrites);
            app.Map("/api/data/v9.2/{**rest}", (HttpContext context) =>
            {
                fake.Writes.Enqueue($"{context.Request.Method} {context.Request.Path}");
                context.Response.StatusCode = fake._refuseWrites
                    ? StatusCodes.Status500InternalServerError
                    : StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });
            await app.StartAsync();
            return fake;
        }

        /// <summary>The production POA seam over the production client, evicting through <paramref name="invalidator"/>.</summary>
        public IDataverseRecordShareService Seam(
            IMembershipCacheInvalidator invalidator, ILogger<DataverseRecordShareService>? logger = null)
            => new DataverseRecordShareService(
                new ServerBackedDataverse(new HttpClient(_app.GetTestServer().CreateHandler())),
                invalidator,
                logger ?? NullLogger<DataverseRecordShareService>.Instance);

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        /// <summary>The production client through its test constructor (a static credential; task 104's seam).</summary>
        private sealed class ServerBackedDataverse(HttpClient client) : DataverseWebApiService(
            client,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = ServiceUrl,
            }).Build(),
            NullLogger<DataverseWebApiService>.Instance,
            confidentialClients: null,
            credential: new StaticTokenCredential());
    }
}
