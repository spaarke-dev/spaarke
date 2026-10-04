using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 132 (defect C12) — the BFF's own team, business-unit and owner writes evict the
/// caches those changes make stale, through the ONE hook (<see cref="IMembershipCacheInvalidator"/>), under every tenant
/// segment, with no HttpContext required — and every eviction pattern reaches the key the production reader wrote.
/// </summary>
/// <remarks>
/// <para><b>Why cache STATE is asserted, not timing.</b> The TTLs are the backstop either way; what the eviction buys is
/// that the entry is GONE when the write returns. Each test warms the caches through the production readers
/// (<see cref="IdentityNormalizationService"/>, <see cref="MembershipResolverService"/>,
/// <see cref="ImpersonatedRootSetSource"/>, <see cref="CachedAccessDataSource"/>) into one
/// <see cref="InMemoryRedisKeyspace"/> — the PRODUCTION <see cref="TenantCache"/> key format with the configured
/// <c>InstanceName</c> applied the way <c>StackExchangeRedisCache</c> applies it — runs the write, and compares the key
/// space before and after: exactly the affected keys removed, every neighbour (another user, another entity type,
/// another record) kept. An eviction that silently matched nothing cannot pass.</para>
/// <para><b>Instruments.</b> The registration writes run the real <see cref="RegistrationDataverseService"/> against an
/// in-memory server standing in for the Dataverse Web API (ADR-038 §7's named replacement for B1). The re-own writes run
/// the real endpoints in the <see cref="ProvisionProjectTestFixture"/> host with only the invalidator registration
/// replaced by the production invalidator over the key space.</para>
/// </remarks>
public sealed partial class AccessCacheInvalidationTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string TenantA = "13200000-aaaa-4aaa-8aaa-00000000000a";
    private const string TenantB = "13200000-bbbb-4bbb-8bbb-00000000000b";
    private const string OwnEnvironment = "https://own.crm.dynamics.com";
    private const string OtherEnvironment = "https://demo.crm.dynamics.com";

    private static readonly Guid UserU = Guid.Parse("13200000-0000-0000-0000-0000000001a1");
    private static readonly Guid UserV = Guid.Parse("13200000-0000-0000-0000-0000000001a2");
    private const string OidU = "13200000-0000-0000-0000-0000000002a1";
    private const string OidV = "13200000-0000-0000-0000-0000000002a2";

    private readonly ProvisionProjectTestFixture _fixture;

    public AccessCacheInvalidationTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 23 — every eviction pattern reaches the key the production reader wrote, and no neighbour
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UserEviction_RemovesExactlyThatUsersIdentityMembershipAndRootSetEntries_UnderEveryTenant()
    {
        var world = new AccessCacheWorld();
        await world.WarmUserAsync(UserU, TenantA);
        await world.WarmUserAsync(UserU, TenantB); // the same user cached under a second tenant segment
        await world.WarmUserAsync(UserV, TenantA); // the neighbour
        var before = world.Keyspace.Keys.ToHashSet();
        var userKeys = before.Where(k => k.Contains(UserU.ToString("D"), StringComparison.Ordinal)).ToHashSet();

        var patterns = MembershipCacheInvalidator.UserAccessPatterns(InMemoryRedisKeyspace.InstanceName, UserU);
        foreach (var pattern in patterns)
        {
            userKeys.Should().Contain(k => InMemoryRedisKeyspace.Matches(pattern, k),
                $"pattern '{pattern}' must reach a key a production reader wrote — a pattern that matches nothing evicts nothing");
        }

        userKeys.Should().HaveCount(2 * (1 + 2 + 2), "precondition: identity + two membership + two root-set entries, in each of two tenants");
        await world.Keyspace.Invalidator().InvalidateUserAccessAsync(UserU, "test", CancellationToken.None);

        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(userKeys),
            "exactly the user's identity, membership and root-set entries go — under BOTH tenants; the neighbour's stay");
    }

    [Fact]
    public async Task OwnerChangeEviction_RemovesEveryUsersEntriesForThatEntityType_AndEverySnapshotOfThatRecord_Only()
    {
        var world = new AccessCacheWorld();
        var record = AccessCacheWorld.Project;
        var otherRecord = Guid.Parse("13200000-0000-0000-0000-0000000003b2");
        await world.WarmUserAsync(UserU, TenantA);
        await world.WarmUserAsync(UserV, TenantB);
        await world.WarmSnapshotAsync(OidU, "sprk_projects", record, TenantA);
        await world.WarmSnapshotAsync(OidV, "sprk_projects", record, TenantB);
        await world.WarmSnapshotAsync(OidU, "sprk_projects", otherRecord, TenantA); // another record
        await world.WarmSnapshotAsync(OidU, "sprk_matters", record, TenantA);       // another entity set, same id
        var before = world.Keyspace.Keys.ToHashSet();

        var expectedGone = before.Where(k =>
                (k.Contains($":{MembershipResolverService.CacheResource}:", StringComparison.Ordinal) && k.Contains(":sprk_project:", StringComparison.Ordinal))
                || (k.Contains($":{ImpersonatedRootSetSource.CacheResource}:", StringComparison.Ordinal) && k.Contains(":sprk_project:", StringComparison.Ordinal))
                || (k.Contains($":{CachedAccessDataSource.RecordAccessResource}:sprk_projects:", StringComparison.Ordinal) && k.Contains(record.ToString("D"), StringComparison.Ordinal)))
            .ToHashSet();
        expectedGone.Should().HaveCount(2 + 2 + 2, "precondition: each user's project membership + root set, and two snapshots of the record");

        foreach (var pattern in MembershipCacheInvalidator.RecordOwnerChangePatterns(
                     InMemoryRedisKeyspace.InstanceName, "sprk_project", "sprk_projects", record))
        {
            expectedGone.Should().Contain(k => InMemoryRedisKeyspace.Matches(pattern, k),
                $"pattern '{pattern}' must reach a key a production reader wrote");
        }

        await world.Keyspace.Invalidator().InvalidateRecordOwnerChangeAsync(
            "sprk_project", "sprk_projects", record, "test", CancellationToken.None);

        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(expectedGone),
            "matter entries, identity entries, the other record's snapshot and the matter-set snapshot of the same id all stay");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 13 — the BFF's team and business-unit writes evict, with no HttpContext, only for their own environment
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After RegistrationDataverseService adds or removes a team member, or binds a business unit, against THIS BFF's
    /// own environment, that user's identity, membership and root-set entries are gone — though the write ran with no
    /// HttpContext (the DemoExpirationService path) and the entries were cached under a real tenant id.
    /// </summary>
    [Theory]
    [InlineData("team-add")]
    [InlineData("team-remove")]
    [InlineData("business-unit-bind")]
    public async Task RegistrationWrite_OnThisBffsEnvironment_EvictsThatUsersEntries_WithNoHttpContext(string write)
    {
        var world = new AccessCacheWorld();
        await world.WarmUserAsync(UserU, TenantA);
        await world.WarmUserAsync(UserV, TenantA);
        var before = world.Keyspace.Keys.ToHashSet();
        var userKeys = before.Where(k => k.Contains(UserU.ToString("D"), StringComparison.Ordinal)).ToHashSet();
        userKeys.Should().NotBeEmpty("precondition");

        await using var dataverse = await RegistrationDataverse.StartAsync(createdUserId: UserU);
        var service = dataverse.Service(world.Keyspace.Invalidator());

        await RunAsync(service, write, targetDataverseUrl: null);

        world.Keyspace.Keys.Should().BeEquivalentTo(before.Except(userKeys),
            $"the {write} changed this user's teams or business unit; the neighbour's entries are untouched");
    }

    /// <summary>A write to ANOTHER environment evicts nothing (D-13: this BFF never caches that environment's users) and does not fail.</summary>
    [Fact]
    public async Task RegistrationWrite_ToAnotherEnvironment_EvictsNothing_AndDoesNotFail()
    {
        var world = new AccessCacheWorld();
        await world.WarmUserAsync(UserU, TenantA);
        var before = world.Keyspace.Keys.ToHashSet();

        await using var dataverse = await RegistrationDataverse.StartAsync(createdUserId: UserU);
        var service = dataverse.Service(world.Keyspace.Invalidator());

        var act = () => RunAsync(service, "team-add", OtherEnvironment);

        await act.Should().NotThrowAsync();
        dataverse.Writes.Should().ContainSingle("precondition: the team write itself ran");
        world.Keyspace.ScannedPatterns.Should().BeEmpty("no eviction is attempted for another environment's user");
        world.Keyspace.Keys.Should().BeEquivalentTo(before);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 14 — the re-own paths evict the colleague's membership, root set and every snapshot of the record
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A BU colleague C held project P through OWNERSHIP (team-owned), cached in membership, in the impersonated root
    /// set and in a record snapshot. Provisioning re-owns P to the memberless secure team; on return C's next resolve
    /// re-reads instead of being served the cached P, and every user's snapshot of P is gone. The same for the unsecure
    /// re-own. Neighbours (C's matter entries, a snapshot of another project) stay.
    /// </summary>
    [Theory]
    [InlineData("provision")]
    [InlineData("unsecure")]
    public async Task ReOwn_EvictsTheColleaguesCachedOwnershipAccess_AndEverySnapshotOfTheRecord(string path)
    {
        var world = new AccessCacheWorld();
        var project = AccessCacheWorld.Project;
        var otherProject = Guid.Parse("13200000-0000-0000-0000-0000000003b9");
        if (path == "provision")
        {
            _fixture.SeedProject(project);
        }
        else
        {
            _fixture.SeedProject(project, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        }

        var colleague = await world.WarmUserAsync(UserV, TenantA);
        colleague.ProjectIds.Should().Contain(project, "precondition: the colleague's cached membership holds the project via ownership");
        await world.WarmSnapshotAsync(OidV, "sprk_projects", project, TenantA);
        await world.WarmSnapshotAsync(OidU, "sprk_projects", project, TenantB);
        await world.WarmSnapshotAsync(OidV, "sprk_projects", otherProject, TenantA);
        var queriesBefore = world.MembershipQueries;
        var rootReadsBefore = world.RootSetReads;

        using var host = HostWith(_fixture, world.Keyspace);
        var response = await PostAsync(host, path, project);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var readers = world.Readers(TenantA);
        await readers.Membership.ResolveAsync(UserV, AccessCacheWorld.ProjectEntity, AccessCacheWorld.AuthorizationOptions, CancellationToken.None);
        world.MembershipQueries.Should().Be(queriesBefore + 1, "the colleague's cached project membership is gone — the next resolve re-reads");
        await readers.RootSets.GetAsync(UserV, AccessCacheWorld.ProjectEntity);
        world.RootSetReads.Should().Be(rootReadsBefore + 1, "the colleague's cached impersonated root set for projects is gone");

        world.Keyspace.Keys.Should().NotContain(k => k.Contains(project.ToString("D"), StringComparison.Ordinal)
            && k.Contains(CachedAccessDataSource.RecordAccessResource, StringComparison.Ordinal),
            "every user's snapshot of the re-owned record is gone, under every tenant");
        world.Keyspace.Keys.Should().Contain(k => k.Contains(otherProject.ToString("D"), StringComparison.Ordinal),
            "another project's snapshot stays");
        world.Keyspace.Keys.Should().Contain(k => k.Contains(":sprk_matter:", StringComparison.Ordinal),
            "the colleague's matter entries stay");
    }

    /// <summary>
    /// An AMBIGUOUS re-own — the ownership PATCH timed out after Dataverse committed it, or was accepted but could not be
    /// read back — may have changed the owner, so BOTH endpoints evict before reporting the failure (verifier r1 item 8:
    /// provisioning did not, unsecuring did on one of the two). The endpoint's existing outcome (500, nothing further
    /// done) is unchanged; only what is left in the caches changes.
    /// </summary>
    [Theory]
    [InlineData("provision", "patch-timed-out-after-commit")]
    [InlineData("provision", "read-back-failed")]
    [InlineData("unsecure", "patch-timed-out-after-commit")]
    [InlineData("unsecure", "read-back-failed")]
    public async Task ReOwn_AnAmbiguousOutcome_StillEvicts_AndTheFailureIsReportedAsBefore(string path, string ambiguity)
    {
        var world = new AccessCacheWorld();
        var project = AccessCacheWorld.Project;
        if (path == "provision")
        {
            _fixture.SeedProject(project);
        }
        else
        {
            _fixture.SeedProject(project, owningTeamId: ProvisionProjectTestFixture.SecureOwnerTeamId);
        }

        await world.WarmUserAsync(UserV, TenantA);
        await world.WarmSnapshotAsync(OidV, "sprk_projects", project, TenantA);
        var queriesBefore = world.MembershipQueries;
        var rootReadsBefore = world.RootSetReads;
        _fixture.OwnershipPatchTimesOutAfterApplying = ambiguity == "patch-timed-out-after-commit";
        _fixture.OwnerReadBackFails = ambiguity == "read-back-failed";

        using var host = HostWith(_fixture, world.Keyspace);
        var response = await PostAsync(host, path, project);

        // Batch 4 integration (task 133): provisioning reads the owner back even when the PATCH threw, so a PATCH that
        // timed out AFTER Dataverse committed it reads back as moved and the run completes (200) — the read decides, not
        // the PATCH's error. Every other ambiguous case is reported as a failure, exactly as before task 132.
        var completesOnReadBack = path == "provision" && ambiguity == "patch-timed-out-after-commit";
        response.StatusCode.Should().Be(
            completesOnReadBack ? HttpStatusCode.OK : HttpStatusCode.InternalServerError,
            await response.Content.ReadAsStringAsync());
        _fixture.Updates.Should().Contain(u => u.Payload.ContainsKey("ownerid@odata.bind"),
            "precondition: the ownership PATCH was sent, so the record may have been re-owned");

        var readers = world.Readers(TenantA);
        await readers.Membership.ResolveAsync(UserV, AccessCacheWorld.ProjectEntity, AccessCacheWorld.AuthorizationOptions, CancellationToken.None);
        world.MembershipQueries.Should().Be(queriesBefore + 1, "the colleague's cached project membership was evicted");
        await readers.RootSets.GetAsync(UserV, AccessCacheWorld.ProjectEntity);
        world.RootSetReads.Should().Be(rootReadsBefore + 1, "the colleague's cached impersonated root set for projects was evicted");
        world.Keyspace.Keys.Should().NotContain(k => k.Contains(project.ToString("D"), StringComparison.Ordinal)
            && k.Contains(CachedAccessDataSource.RecordAccessResource, StringComparison.Ordinal),
            "every snapshot of the possibly re-owned record was evicted");
    }

    /// <summary>A failed eviction does not fail the re-own: the endpoint's outcome is unchanged and the failure is logged.</summary>
    [Fact]
    public async Task ReOwn_WhenEvictionFails_TheProvisioningOutcomeIsUnchanged_AndTheFailureIsLogged()
    {
        var world = new AccessCacheWorld();
        var project = AccessCacheWorld.Project;
        _fixture.SeedProject(project);
        await world.WarmUserAsync(UserV, TenantA);
        world.Keyspace.FailScans = true;

        using var host = HostWith(_fixture, world.Keyspace);
        var response = await PostAsync(host, "provision", project);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the re-own already happened; the TTL is the backstop");
        _fixture.OwningTeamOf(project).Should().Be(ProvisionProjectTestFixture.SecureOwnerTeamId);
        world.Keyspace.ScannedPatterns.Should().NotBeEmpty("precondition: the eviction was attempted");
        _fixture.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("[ACCESS-EVICT] Eviction of pattern", StringComparison.Ordinal));
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────

    private static Task RunAsync(RegistrationDataverseService service, string write, string? targetDataverseUrl) => write switch
    {
        "team-add" => service.AddUserToTeamAsync(RegistrationDataverse.TeamName, UserU, CancellationToken.None, targetDataverseUrl),
        "team-remove" => service.RemoveUserFromTeamAsync(RegistrationDataverse.TeamName, UserU, CancellationToken.None, targetDataverseUrl),
        // "not-a-guid" skips task 141's contact link (an unusable oid), so only the business-unit bind is exercised.
        "business-unit-bind" => service.CreateSystemUserAsync(
            "not-a-guid", "Ann", "Lee", "ann@customer.example", RegistrationDataverse.BusinessUnitName,
            CancellationToken.None, targetDataverseUrl),
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown registration write."),
    };

    private static WebApplicationFactory<Program> HostWith(ProvisionProjectTestFixture fixture, InMemoryRedisKeyspace keyspace)
        => fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMembershipCacheInvalidator>();
            services.AddSingleton<IMembershipCacheInvalidator>(sp => new MembershipCacheInvalidator(
                keyspace.Multiplexer(),
                Options.Create(new MembershipCacheInvalidatorOptions()),
                Options.Create(new RedisOptions { InstanceName = InMemoryRedisKeyspace.InstanceName }),
                TimeProvider.System,
                sp.GetRequiredService<ILogger<MembershipCacheInvalidator>>()));
        }));

    private static Task<HttpResponseMessage> PostAsync(WebApplicationFactory<Program> host, string path, Guid project)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "provision-test-token");
        return path == "provision"
            ? client.PostAsJsonAsync("/api/v1/external-access/provision-project", new { recordType = "project", recordId = project })
            : client.PostAsJsonAsync("/api/v1/external-access/unsecure-project", new { recordType = "project", recordId = project });
    }

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }

    /// <summary>
    /// The production readers over one key space: they WRITE the entries the evictions must reach. Dataverse is
    /// substituted at its module boundaries; the colleague's project membership arrives through team ownership.
    /// </summary>
    private sealed class AccessCacheWorld
    {
        public const string ProjectEntity = "sprk_project";
        public const string MatterEntity = "sprk_matter";
        public const string CascadeChildEntity = "sharepointdocumentlocation";
        public static readonly Guid Project = Guid.Parse("13200000-0000-0000-0000-0000000003b1");
        private static readonly Guid BusinessUnitTeam = Guid.Parse("13200000-0000-0000-0000-0000000004c1");

        public static readonly MembershipResolveOptions AuthorizationOptions = new(AccessConferringOnly: true);

        private readonly Mock<IDataverseService> _dataverse = new();
        private readonly Mock<IMembershipFieldDiscoveryService> _discovery = new();
        private int _membershipQueries;
        private int _rootSetReads;
        private int _snapshotReads;

        public AccessCacheWorld()
        {
            _dataverse
                .Setup(d => d.RetrieveAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => new Entity("systemuser", id)
                {
                    ["internalemailaddress"] = $"{id:N}@customer.example",
                    ["businessunitid"] = new EntityReference("businessunit", Guid.Parse("13200000-0000-0000-0000-0000000004c0")),
                });
            _dataverse
                .Setup(d => d.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "teammembership"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var rows = new EntityCollection();
                    rows.Entities.Add(new Entity("teammembership") { ["teamid"] = BusinessUnitTeam });
                    return rows;
                });
            _dataverse
                .Setup(d => d.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FetchExpression fetch, CancellationToken _) =>
                {
                    Interlocked.Increment(ref _membershipQueries);
                    var rows = new EntityCollection();
                    if (fetch.Query.Contains($"name='{ProjectEntity}'", StringComparison.Ordinal))
                    {
                        // Team-owned by the BU's team — the colleague holds the project through OWNERSHIP.
                        rows.Entities.Add(new Entity(ProjectEntity, Project) { ["owningteam"] = new EntityReference("team", BusinessUnitTeam) });
                    }

                    return rows;
                });

            // Task 132 batch 4 residual: a table an Assign cascade re-owns is discoverable like any other — the membership
            // resolver must still decline to cache it.
            foreach (var entity in new[] { ProjectEntity, MatterEntity, CascadeChildEntity })
            {
                _discovery
                    .Setup(d => d.DiscoverAsync(entity, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DiscoveryResult(entity, DateTimeOffset.UtcNow, new[]
                    {
                        new MembershipDescriptor("ownerid", "owner", "SystemUser", "systemuser", "test"),
                        new MembershipDescriptor("owningteam", "owningTeam", "Team", "team", "test"),
                    }, Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));
            }
        }

        public InMemoryRedisKeyspace Keyspace { get; } = new();

        public int MembershipQueries => Volatile.Read(ref _membershipQueries);

        public int RootSetReads => Volatile.Read(ref _rootSetReads);

        /// <summary>Calls that reached the inner (Dataverse) access source — a cache miss or an uncached read.</summary>
        public int SnapshotReads => Volatile.Read(ref _snapshotReads);

        /// <summary>The four production readers, as a request in <paramref name="tenant"/> sees them.</summary>
        public ReaderSet Readers(string tenant)
        {
            var accessor = new FixedHttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", tenant) }, "test")),
                },
            };
            var cache = Keyspace.TenantCache();
            var identity = new IdentityNormalizationService(
                _dataverse.Object, cache, Array.Empty<IIdentityOrganizationResolver>(),
                Options.Create(new MembershipOptions()), NullLogger<IdentityNormalizationService>.Instance, accessor);
            var membership = new MembershipResolverService(
                _discovery.Object, identity, _dataverse.Object, cache, Options.Create(new MembershipOptions()),
                NullLogger<MembershipResolverService>.Instance, accessor);
            var rootSets = new ImpersonatedRootSetSource(
                new RootQuery(this), cache, NullLogger<ImpersonatedRootSetSource>.Instance, accessor);
            var snapshots = new CachedAccessDataSource(
                new AnsweringInner(this), cache, accessor, NullLogger<CachedAccessDataSource>.Instance);
            return new ReaderSet(identity, membership, rootSets, snapshots);
        }

        /// <summary>Caches the user's identity, project + matter membership and project + matter root sets, under <paramref name="tenant"/>.</summary>
        public async Task<(IReadOnlyList<Guid> ProjectIds, IReadOnlyList<Guid> MatterIds)> WarmUserAsync(Guid user, string tenant)
        {
            var readers = Readers(tenant);
            await readers.Identity.ResolveAsync(user, CancellationToken.None);
            var projects = await readers.Membership.ResolveAsync(user, ProjectEntity, AuthorizationOptions, CancellationToken.None);
            var matters = await readers.Membership.ResolveAsync(user, MatterEntity, AuthorizationOptions, CancellationToken.None);
            await readers.RootSets.GetAsync(user, ProjectEntity);
            await readers.RootSets.GetAsync(user, MatterEntity);
            return (projects.Ids, matters.Ids);
        }

        /// <summary>Caches one record-access snapshot through the production decorator.</summary>
        public Task WarmSnapshotAsync(string oid, string entitySet, Guid recordId, string tenant)
            => Readers(tenant).Snapshots.GetRecordAccessAsync(oid, entitySet, recordId, "user-token");

        /// <summary>
        /// Caches one DOCUMENT-access snapshot through the production decorator, with the document id spelled as the
        /// request spelled it (route text: any casing, braces).
        /// </summary>
        public Task WarmDocumentSnapshotAsync(string oid, string documentIdAsSent, string tenant)
            => Readers(tenant).Snapshots.GetUserAccessAsync(oid, documentIdAsSent, "user-token");

        internal void CountRootSetRead() => Interlocked.Increment(ref _rootSetReads);

        internal void CountSnapshotRead() => Interlocked.Increment(ref _snapshotReads);

        public sealed record ReaderSet(
            IdentityNormalizationService Identity,
            MembershipResolverService Membership,
            ImpersonatedRootSetSource RootSets,
            CachedAccessDataSource Snapshots);

        private sealed class RootQuery(AccessCacheWorld world) : IImpersonatedCommunicationQuery
        {
            public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
                string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
            {
                world.CountRootSetRead();
                IReadOnlyList<Dictionary<string, JsonElement>> rows = entitySetName == "sprk_projects"
                    ? new[] { new Dictionary<string, JsonElement> { ["sprk_projectid"] = JsonSerializer.SerializeToElement(Project.ToString("D")) } }
                    : Array.Empty<Dictionary<string, JsonElement>>();
                return Task.FromResult(rows);
            }
        }

        private sealed class AnsweringInner(AccessCacheWorld world) : IAccessDataSource
        {
            public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
            {
                world.CountSnapshotRead();
                return Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = resourceId, AccessRights = AccessRights.Read });
            }

            public Task<AccessSnapshot> GetRecordAccessAsync(string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
            {
                world.CountSnapshotRead();
                return Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = recordId.ToString(), AccessRights = AccessRights.Read | AccessRights.Write });
            }
        }
    }

    /// <summary>
    /// The registration service's Dataverse surface over an in-memory server: business-unit and team lookups by name,
    /// the team-membership association writes, and the systemuser create (answering with the created id).
    /// </summary>
    private sealed class RegistrationDataverse : IAsyncDisposable
    {
        public const string TeamName = "Demo Team";
        public const string BusinessUnitName = "Demo Users";
        private static readonly Guid BusinessUnitId = Guid.Parse("13200000-0000-0000-0000-0000000005d1");
        private static readonly Guid TeamId = Guid.Parse("13200000-0000-0000-0000-0000000005d2");

        private readonly WebApplication _app;
        private readonly Guid _createdUserId;

        private RegistrationDataverse(WebApplication app, Guid createdUserId)
        {
            _app = app;
            _createdUserId = createdUserId;
        }

        public System.Collections.Concurrent.ConcurrentQueue<string> Writes { get; } = new();

        public static async Task<RegistrationDataverse> StartAsync(Guid createdUserId)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            var app = builder.Build();
            var fake = new RegistrationDataverse(app, createdUserId);
            app.Map("/api/data/v9.2/{**rest}", (HttpContext context) => fake.HandleAsync(context));
            await app.StartAsync();
            return fake;
        }

        public RegistrationDataverseService Service(IMembershipCacheInvalidator invalidator)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATAVERSE_URL"] = OwnEnvironment,
                ["Dataverse:ServiceUrl"] = OwnEnvironment,
                ["SecureRecord:BusinessUnitName"] = "Secure Record",
            }).Build();
            var factory = new ServerClientFactory(_app.GetTestServer());

            return new RegistrationDataverseService(
                configuration,
                new TrackingIdGenerator(),
                new StaticTokenCredential(),
                factory,
                NullLogger<RegistrationDataverseService>.Instance,
                new ContactIdentityBinderFactory(
                    factory, NullLoggerFactory.Instance,
                    IdentityBinding.IdentityBindingTestKit.Tenants(IdentityBinding.IdentityBindingTestKit.CustomerTenant),
                    TimeProvider.System),
                invalidator);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var filter = Uri.UnescapeDataString(context.Request.QueryString.Value ?? string.Empty);

            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
            {
                Writes.Enqueue($"{context.Request.Method} {path}");
                if (path.EndsWith("/systemusers", StringComparison.Ordinal))
                {
                    context.Response.Headers["OData-EntityId"] = $"{OwnEnvironment}/api/data/v9.2/systemusers({_createdUserId})";
                }

                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            context.Response.ContentType = "application/json";
            if (path.EndsWith("/businessunits", StringComparison.Ordinal))
            {
                // The Secure Record lookup (task 144) finds none; the configured business unit resolves by name.
                var rows = filter.Contains($"name eq '{BusinessUnitName}'", StringComparison.Ordinal)
                    ? $"[{{\"businessunitid\":\"{BusinessUnitId}\"}}]"
                    : "[]";
                await context.Response.WriteAsync($"{{\"value\":{rows}}}");
                return;
            }

            if (path.EndsWith("/teams", StringComparison.Ordinal))
            {
                await context.Response.WriteAsync(
                    $"{{\"value\":[{{\"teamid\":\"{TeamId}\",\"_businessunitid_value\":\"{BusinessUnitId}\"}}]}}");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }

        private sealed class ServerClientFactory(TestServer server) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(server.CreateHandler(), disposeHandler: false);
        }
    }
}
