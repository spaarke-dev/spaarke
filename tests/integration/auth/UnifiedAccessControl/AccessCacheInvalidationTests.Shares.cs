using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// <item><b>Share-only changes evict like owner changes — and the eviction is a property of the WRITE</b> (main-session
/// round 55). A POA grant, rights change or revoke changes who can read a record, so <see cref="DataverseWebApiService"/>
/// notifies its <see cref="IRecordShareWriteObserver"/> after every share write it makes — returned, refused or cancelled,
/// on <see cref="CancellationToken.None"/>, never failing the write — and the BFF's observer is the
/// <see cref="IMembershipCacheInvalidator"/>, whose default member runs
/// <see cref="IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync"/>. The cases below run the PRODUCTION client and
/// the PRODUCTION invalidator: every write, every principal kind and each outcome; a write reached WITHOUT the seam — by a
/// late binder choosing the method from a run-time name, and by a method resolved from its metadata token and run by a
/// LINQ provider's compiled tree (the verifier's seeds V and M); exactly one eviction per write, after the write, through
/// the seam or not; and an observer that throws.</item>
/// <item><b>No eviction for a type no cache holds.</b> The children an Assign cascade re-owns
/// (<c>sharepointdocumentlocation</c>, <c>sharepointdocument</c>) are cached by no access cache, so the owner-change hook
/// builds no pattern for them and touches Redis not at all — while a root's eviction is unchanged.</item>
/// </list>
/// </summary>
/// <remarks>
/// Same instruments as the rest of this class: the production readers write the entries into one
/// <see cref="InMemoryRedisKeyspace"/>; the production invalidator scans it. The client runs against an in-memory server
/// standing in for the Dataverse Web API (ADR-038 §7's named replacement for B1 — no transport double; the request
/// builder, the auth header and the status handling are the production code).
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
    // Residual 1 — the CLIENT notifies after every share write: returned, refused, or cancelled by the caller
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The input dimensions a write path could branch on, which the cases must therefore cover: each write, for EVERY
    /// principal kind the client is parameterized by (a team share is what PlaybookSharingService writes; task 132 verifier
    /// seed N1 was a team-only path with no eviction, which user-only cases could not see), and — where the outcome
    /// matters — Dataverse accepting or refusing it. Derived from the enum, so a new kind gets its cases.
    /// </summary>
    public static TheoryData<string, DataversePrincipalKind, bool> EveryWriteEveryPrincipalEveryOutcome()
    {
        var data = new TheoryData<string, DataversePrincipalKind, bool>();
        foreach (var write in ShareWrites)
        {
            foreach (var kind in Enum.GetValues<DataversePrincipalKind>())
            {
                data.Add(write, kind, false);
                data.Add(write, kind, true);
            }
        }

        return data;
    }

    /// <inheritdoc cref="EveryWriteEveryPrincipalEveryOutcome"/>
    public static TheoryData<string, DataversePrincipalKind> EveryWriteEveryPrincipal()
    {
        var data = new TheoryData<string, DataversePrincipalKind>();
        foreach (var write in ShareWrites)
        {
            foreach (var kind in Enum.GetValues<DataversePrincipalKind>())
            {
                data.Add(write, kind);
            }
        }

        return data;
    }

    private static readonly string[] ShareWrites = { "grant", "modify", "revoke" };

    /// <summary>
    /// Every share write of the PRODUCTION client — called directly, with no seam in front of it — evicts that record: a
    /// grant (InternalShareEndpoints share, provisioning's share-first creator grant, the resume error paths' unconfirmed
    /// grant, the child fan-out's grant, a playbook's team share), a rights change (a level change, the creator-share
    /// restore to its old mask) and a revoke (unshare, the restore that removes an issued share, the fan-out's removal, a
    /// playbook team's unshare) — for a user and for a team principal. The write's own outcome is what the caller sees: a
    /// refused write still throws, AFTER the eviction, because a write that reports failure can have committed.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryWriteEveryPrincipalEveryOutcome))]
    public async Task PoaClient_EveryShareWrite_EvictsThatRecord_AndTheWritesOwnOutcomeStands(
        string write, DataversePrincipalKind principal, bool dataverseRefuses)
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        var expectedGone = await WarmTheRecordAsync(world, record);
        var before = world.Keyspace.Keys.ToHashSet();

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: dataverseRefuses);
        var client = dataverse.Client(world.Keyspace.Invalidator());

        var act = () => RunShareWriteAsync(client, write, principal, record, CancellationToken.None);

        if (dataverseRefuses)
        {
            await act.Should().ThrowAsync<HttpRequestException>("the write's own failure is what the caller sees");
        }
        else
        {
            await act.Should().NotThrowAsync();
        }

        dataverse.Writes.Should().ContainSingle("precondition: the POA action reached Dataverse")
            .Which.Should().Match<(string Request, string Body)>(w =>
                w.Request.EndsWith("/" + ActionOf(write), StringComparison.Ordinal)
                && w.Body.Contains($"{principal.ToEntitySet()}(", StringComparison.Ordinal)
                && w.Body.Contains($"sprk_projects({record})", StringComparison.Ordinal),
                $"precondition: the {write} for a {principal} addressed the record");
        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(expectedGone),
            $"the {write} for a {principal} (refused={dataverseRefuses}) may have changed who can read the record; its root sets and snapshots are gone");
    }

    /// <summary>
    /// A caller that goes away mid-write still leaves the caches clean — for every write and principal kind: the
    /// notification is not bound to the request's token (CancellationToken.None), and the caller's cancellation still
    /// propagates.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryWriteEveryPrincipal))]
    public async Task PoaClient_WhenTheCallerCancels_TheEvictionStillRuns_AndTheCancellationPropagates(
        string write, DataversePrincipalKind principal)
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        await world.WarmSnapshotAsync(OidV, "sprk_projects", record, TenantA);
        world.Keyspace.Keys.Should().ContainSingle("precondition");

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: false);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => RunShareWriteAsync(dataverse.Client(world.Keyspace.Invalidator()), write, principal, record, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        world.Keyspace.Keys.Should().BeEmpty($"the eviction ran with CancellationToken.None after the cancelled {write} for a {principal}");
    }

    /// <summary>
    /// The CLIENT's own guard: an observer that throws (the invalidator's contract is never to, so this is the defect case
    /// the client's catch exists for) never changes the write's outcome — for every write and principal kind. A write that
    /// succeeded completes normally, a write Dataverse refused still surfaces ITS failure (not the observer's), the observer
    /// was told exactly once, and the client logs the failure it swallowed. A client that stopped catching fails this test
    /// with the double's exception.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryWriteEveryPrincipalEveryOutcome))]
    public async Task PoaClient_WhenTheObserverThrows_TheClientCatchesAndLogs_AndTheWritesOwnOutcomeStands(
        string write, DataversePrincipalKind principal, bool dataverseRefuses)
    {
        var logs = new ProvisionProjectTestFixture.LogCapture();
        using var loggers = new LoggerFactory(new[] { logs });
        var observer = new ThrowingInvalidator();

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: dataverseRefuses);
        var client = dataverse.Client(observer, loggers.CreateLogger<DataverseWebApiService>());

        var act = () => RunShareWriteAsync(client, write, principal, AccessCacheWorld.Project, CancellationToken.None);

        if (dataverseRefuses)
        {
            (await act.Should().ThrowAsync<HttpRequestException>(
                    "the write's own failure is what the caller sees, not the observer's"))
                .Which.Should().NotBeSameAs(ThrowingInvalidator.Fault);
        }
        else
        {
            await act.Should().NotThrowAsync("the share has already been written; the TTL is the backstop");
        }

        dataverse.Writes.Should().ContainSingle("precondition: the POA action reached Dataverse");
        observer.ShareChangeCalls.Should().Be(1, "the client told the observer once, after the write");
        logs.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning
            && e.Message.Contains("[ACCESS-EVICT] The share-write observer threw after the", StringComparison.Ordinal)
            && e.Message.Contains($"after the {WriteOf(write)} on sprk_projects", StringComparison.Ordinal),
            "the client's catch logs the observer failure it swallowed");
    }

    /// <summary>
    /// The production invalidator's own resilience under the client: when Redis fails mid-scan the invalidator logs and
    /// returns (its "never throws" contract), so the share write completes normally and the client has nothing to catch.
    /// </summary>
    [Fact]
    public async Task PoaClient_WhenRedisFails_TheInvalidatorLogsAndReturns_AndTheShareWriteSucceeds()
    {
        var world = new AccessCacheWorld();
        await world.WarmSnapshotAsync(OidV, "sprk_projects", AccessCacheWorld.Project, TenantA);
        world.Keyspace.FailScans = true;
        var logs = new ProvisionProjectTestFixture.LogCapture();
        using var loggers = new LoggerFactory(new[] { logs });

        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: false);
        var client = dataverse.Client(
            world.Keyspace.Invalidator(logger: loggers.CreateLogger<MembershipCacheInvalidator>()),
            loggers.CreateLogger<DataverseWebApiService>());

        var act = () => RunShareWriteAsync(client, "grant", DataversePrincipalKind.SystemUser, AccessCacheWorld.Project, CancellationToken.None);

        await act.Should().NotThrowAsync("the share has already been written; the TTL is the backstop");
        dataverse.Writes.Should().ContainSingle();
        world.Keyspace.ScannedPatterns.Should().NotBeEmpty("precondition: the eviction was attempted");
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("[ACCESS-EVICT] Eviction of pattern", StringComparison.Ordinal),
            "the invalidator logged the Redis failure itself");
        logs.Entries.Should().NotContain(e => e.Message.Contains("[ACCESS-EVICT] The share-write observer threw", StringComparison.Ordinal),
            "the invalidator did not throw, so the client's catch had nothing to log");
    }

    /// <summary>The two routes a share write takes in the BFF: through the seam every writer injects, or the client itself.</summary>
    public static TheoryData<string, string> EveryWriteEveryRoute()
    {
        var data = new TheoryData<string, string>();
        foreach (var write in ShareWrites)
        {
            data.Add(write, "seam");
            data.Add(write, "client");
        }

        return data;
    }

    /// <summary>
    /// Exactly ONE eviction per write, made AFTER the write reached Dataverse (main-session round 55): the seam no longer
    /// evicts on its own, so a write through it is evicted once — by the client — exactly like a write straight to the
    /// client, with the record the write addressed and the write's name as its correlation id. A seam that still evicted
    /// would make it two; an eviction before the write would let a read in between re-cache the old access.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryWriteEveryRoute))]
    public async Task ShareWrite_ThroughTheSeamOrStraightToTheClient_IsEvictedExactlyOnce_AfterTheWrite(string write, string route)
    {
        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: false);
        var observer = new CountingInvalidator(writesSoFar: () => dataverse.Writes.Count);
        var client = dataverse.Client(observer);
        var record = AccessCacheWorld.Project;

        await (route == "seam"
            ? RunShareWriteAsync(new DataverseRecordShareService(client), write, DataversePrincipalKind.SystemUser, record, CancellationToken.None)
            : RunShareWriteAsync(client, write, DataversePrincipalKind.SystemUser, record, CancellationToken.None));

        dataverse.Writes.Should().ContainSingle("precondition: one POA action reached Dataverse");
        observer.ShareChanges.Should().Equal(new (string, Guid, string?, int)[] { ("sprk_projects", record, $"share:{write}", 1) },
            $"one {write} through the {route} is one eviction of that record, made once the write had reached Dataverse");
    }

    /// <summary>
    /// The BFF's OWN composition wires it: the <see cref="DataverseWebApiService"/> the BFF's container builds notifies the
    /// container's <see cref="IMembershipCacheInvalidator"/> after a share write. A registration that dropped the observer,
    /// or put anything else in its place, fails here. The caller's token is already cancelled, so the write stops before a
    /// token or a request — and the notification still runs (it is not bound to the caller's token).
    /// </summary>
    [Fact]
    public async Task TheBffsComposedClient_NotifiesTheContainersInvalidator_AfterAShareWrite()
    {
        var registered = new CountingInvalidator(writesSoFar: () => 0);
        using var host = _fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(registered);
        }));
        var client = host.Services.GetRequiredService<DataverseWebApiService>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => client.RevokeAccessAsync("sprk_projects", AccessCacheWorld.Project, DataversePrincipalRef.User(UserU), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("precondition: the cancelled write sent nothing");
        registered.ShareChanges.Should().Equal(
            new (string, Guid, string?, int)[] { ("sprk_projects", AccessCacheWorld.Project, "share:revoke", 0) },
            "the composed client's observer is the invalidator the container holds");
    }

    /// <summary>
    /// Task 132 verifier seed V, as a behaviour test: the client's revoke chosen and invoked by the Visual Basic LATE BINDER
    /// from a name built at run time — no seam, no method token, no constant. The write still evicts that record, because
    /// the eviction is the write's own.
    /// </summary>
    [Fact]
    public async Task ShareWrite_InvokedByALateBinderFromARunTimeName_StillEvictsThatRecord()
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        var expectedGone = await WarmTheRecordAsync(world, record);
        var before = world.Keyspace.Keys.ToHashSet();
        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: false);
        var client = dataverse.Client(world.Keyspace.Invalidator());

        var write = (Task)Microsoft.VisualBasic.Interaction.CallByName(
            client,
            string.Concat("Revoke", "AccessAsync"),
            Microsoft.VisualBasic.CallType.Method,
            "sprk_projects",
            record,
            DataversePrincipalRef.User(UserU),
            CancellationToken.None)!;
        await write;

        dataverse.Writes.Should().ContainSingle("precondition: the late-bound call reached Dataverse")
            .Which.Request.Should().EndWith("/RevokeAccess");
        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(expectedGone),
            "a share write the late binder chose at run time evicts exactly like a direct one");
    }

    /// <summary>
    /// Task 132 verifier seed M, as a behaviour test: the client's revoke resolved from its METADATA TOKEN
    /// (<c>ModuleHandle.ResolveMethodHandle</c> + <c>MethodBase.GetMethodFromHandle</c>), put into a hand-built
    /// <c>Expression.Call</c>, and run by a LINQ provider (<c>EnumerableQuery</c> compiles and invokes the tree internally) —
    /// no seam, no name. The write still evicts that record.
    /// </summary>
    [Fact]
    public async Task ShareWrite_ResolvedFromItsMetadataTokenAndRunByALinqProvider_StillEvictsThatRecord()
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        var expectedGone = await WarmTheRecordAsync(world, record);
        var before = world.Keyspace.Keys.ToHashSet();
        await using var dataverse = await PoaDataverse.StartAsync(refuseWrites: false);
        DataverseWebApiService client = dataverse.Client(world.Keyspace.Invalidator());

        var token = ((Func<string, Guid, DataversePrincipalRef, CancellationToken, Task>)client.RevokeAccessAsync).Method.MetadataToken;
        var method = (MethodInfo)MethodBase.GetMethodFromHandle(
            typeof(DataverseWebApiService).Module.ModuleHandle.ResolveMethodHandle(token))!;
        var receiver = Expression.Parameter(typeof(DataverseWebApiService), "client");
        var call = Expression.Call(
            receiver,
            method,
            Expression.Constant("sprk_projects"),
            Expression.Constant(record),
            Expression.Constant(DataversePrincipalRef.User(UserU)),
            Expression.Constant(CancellationToken.None));
        var lambda = Expression.Lambda<Func<DataverseWebApiService, Task>>(call, receiver);

        await new[] { client }.AsQueryable().Select(lambda).First();

        dataverse.Writes.Should().ContainSingle("precondition: the token-resolved call reached Dataverse")
            .Which.Request.Should().EndWith("/RevokeAccess");
        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(expectedGone),
            "a share write resolved from its token and run by a LINQ provider evicts exactly like a direct one");
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

    private static readonly Guid TeamT = Guid.Parse("13200000-0000-0000-0000-0000000009a1");

    /// <summary>
    /// Caches what a share write on <paramref name="record"/> must evict — a colleague's project root set (tenant A) and two
    /// users' snapshots of the record (tenants A and B) — and returns exactly those keys; everything else warmed (the
    /// colleague's identity and membership) must stay.
    /// </summary>
    private static async Task<HashSet<string>> WarmTheRecordAsync(AccessCacheWorld world, Guid record)
    {
        await world.WarmUserAsync(UserV, TenantA);
        await world.WarmSnapshotAsync(OidV, "sprk_projects", record, TenantA);
        await world.WarmSnapshotAsync(OidU, "sprk_projects", record, TenantB);
        var expectedGone = world.Keyspace.Keys.Where(k =>
                (k.Contains($":{ImpersonatedRootSetSource.CacheResource}:", StringComparison.Ordinal) && k.Contains(":sprk_project:", StringComparison.Ordinal))
                || k.Contains($":{CachedAccessDataSource.RecordAccessResource}:", StringComparison.Ordinal))
            .ToHashSet();
        expectedGone.Should().HaveCount(1 + 2, "precondition: the colleague's project root set and two snapshots of the record");
        return expectedGone;
    }

    private static string ActionOf(string write) => write switch
    {
        "grant" => "GrantAccess",
        "modify" => "ModifyAccess",
        "revoke" => "RevokeAccess",
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown share write."),
    };

    private static RecordShareWrite WriteOf(string write) => write switch
    {
        "grant" => RecordShareWrite.Grant,
        "modify" => RecordShareWrite.Modify,
        "revoke" => RecordShareWrite.Revoke,
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown share write."),
    };

    private static DataversePrincipalRef PrincipalOf(DataversePrincipalKind kind) => kind switch
    {
        DataversePrincipalKind.SystemUser => DataversePrincipalRef.User(UserU),
        DataversePrincipalKind.Team => DataversePrincipalRef.Team(TeamT),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Add a principal for the new kind."),
    };

    /// <summary>One share write straight to the production client.</summary>
    private static Task RunShareWriteAsync(
        DataverseWebApiService client, string write, DataversePrincipalKind kind, Guid record, CancellationToken ct) =>
        write switch
        {
            "grant" => client.GrantAccessAsync("sprk_projects", record, PrincipalOf(kind), "ReadAccess", ct),
            "modify" => client.ModifyAccessAsync("sprk_projects", record, PrincipalOf(kind), "ReadAccess,WriteAccess", ct),
            "revoke" => client.RevokeAccessAsync("sprk_projects", record, PrincipalOf(kind), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown share write."),
        };

    /// <summary>One share write through the seam the BFF's writers inject.</summary>
    private static Task RunShareWriteAsync(
        IDataverseRecordShareService seam, string write, DataversePrincipalKind kind, Guid record, CancellationToken ct) =>
        write switch
        {
            "grant" => seam.GrantAccessAsync("sprk_projects", record, PrincipalOf(kind), "ReadAccess", ct),
            "modify" => seam.ModifyAccessAsync("sprk_projects", record, PrincipalOf(kind), "ReadAccess,WriteAccess", ct),
            "revoke" => seam.RevokeAccessAsync("sprk_projects", record, PrincipalOf(kind), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown share write."),
        };

    /// <summary>
    /// An invalidator with a defect: every hook throws. The invalidator's contract is never to throw; this stands in for
    /// the defect the client's own catch guards against. (An <see cref="IMembershipCacheInvalidator"/>, so — like the
    /// production one — it is the client's observer through the interface's default member.)
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
    /// An invalidator that records each share-change eviction it is asked for — with how many writes had reached Dataverse
    /// at that moment — and evicts nothing.
    /// </summary>
    private sealed class CountingInvalidator(Func<int> writesSoFar) : IMembershipCacheInvalidator
    {
        private readonly ConcurrentQueue<(string EntitySet, Guid Record, string? CorrelationId, int WritesSoFar)> _shareChanges = new();

        public IReadOnlyList<(string EntitySet, Guid Record, string? CorrelationId, int WritesSoFar)> ShareChanges => _shareChanges.ToArray();

        public Task PublishInvalidationAsync(Guid personId, string entityLogicalName, string? correlationId, CancellationToken ct)
            => Task.CompletedTask;

        public Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct) => Task.CompletedTask;

        public Task InvalidateRecordOwnerChangeAsync(
            string entityLogicalName, string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
            => Task.CompletedTask;

        public Task InvalidateRecordShareChangeAsync(string entitySetName, Guid recordId, string? correlationId, CancellationToken ct)
        {
            _shareChanges.Enqueue((entitySetName, recordId, correlationId, writesSoFar()));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The Dataverse Web API over an in-memory server: 204 for every POST, or 500 for every POST when told to refuse. The
    /// PRODUCTION <see cref="DataverseWebApiService"/> is pointed at it.
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

        /// <summary>Each request that reached the server: its method and path, and its body (the payload names the principal).</summary>
        public ConcurrentQueue<(string Request, string Body)> Writes { get; } = new();

        public static async Task<PoaDataverse> StartAsync(bool refuseWrites)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new PoaDataverse(app, refuseWrites);
            app.Map("/api/data/v9.2/{**rest}", async (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync(context.RequestAborted);
                fake.Writes.Enqueue(($"{context.Request.Method} {context.Request.Path}", body));
                context.Response.StatusCode = fake._refuseWrites
                    ? StatusCodes.Status500InternalServerError
                    : StatusCodes.Status204NoContent;
            });
            await app.StartAsync();
            return fake;
        }

        /// <summary>The production client, notifying <paramref name="observer"/> after each share write.</summary>
        public DataverseWebApiService Client(IRecordShareWriteObserver observer, ILogger<DataverseWebApiService>? logger = null)
            => new ServerBackedDataverse(
                new HttpClient(_app.GetTestServer().CreateHandler()),
                observer,
                logger ?? NullLogger<DataverseWebApiService>.Instance);

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        /// <summary>The production client through its test constructor (a static credential; task 104's seam).</summary>
        private sealed class ServerBackedDataverse(
            HttpClient client, IRecordShareWriteObserver observer, ILogger<DataverseWebApiService> logger) : DataverseWebApiService(
            client,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = ServiceUrl,
            }).Build(),
            logger,
            observer,
            confidentialClients: null,
            credential: new StaticTokenCredential());
    }
}
