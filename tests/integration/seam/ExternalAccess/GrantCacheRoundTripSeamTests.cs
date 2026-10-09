// unified-access-control-r2 Task 131 (#1055) — defect C3: the external grant cache dropped
// DirectAccessLevel, so a DIRECT grant on a SECURE root composed correctly on a cache MISS and to
// AccessRights.None on every cache HIT.
//
// ADR-038 "vertical-slice-seam" KEEP path. What makes this suite different from every other test of the
// grant set is the ONE thing it does not double: GetGrantSetAsync. Every existing double
// (UnifiedEvaluatorSeamTests.ParticipationWorld, StandingGrantRuntimeUnionSeamTests,
// AccessibleRecordSetServiceTests, MembershipPagingCharacterizationTests, ExternalAccessContractTests)
// overrides GetGrantSetAsync wholesale — which is exactly the method that owns the cache read, the miss
// fallback and the cache write. So the cache was bypassed in every test in the repo, and C3 (the THIRD
// instance of this bug class — task 032 fixed the same split for AccessLevel) shipped green.
//
// Here the double overrides only the Dataverse read underneath (QueryGrantSetAsync, internal virtual) plus
// the veto-flag / org reads, and the cache is the PRODUCTION TenantCache over MemoryDistributedCache, so the
// production JSON serializer runs. The REAL AccessibleRecordSetService composes once on the miss and once on
// the hit, on BOTH workforce planes.
//
// Determinism (tests/CLAUDE.md — no sleeps, no delays): the production cache write is fire-and-forget
// (`_ = CacheGrantSetAsync(...)`). Over MemoryDistributedCache every await in that write completes
// synchronously, so the entry exists by the time GetGrantSetAsync returns. The suite does not RELY on that
// silently: every miss→hit pair reads the v{CacheVersion} entry back through the same TenantCache BEFORE the
// second call and asserts the Dataverse read ran exactly once across both — an unwritten entry fails loudly
// instead of the "hit" quietly re-running the miss.

using System.Collections;
using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.ExternalAccess;

public sealed class GrantCacheRoundTripSeamTests
{
    private const string ProjectEntity = "sprk_project";
    private const string MatterEntity = "sprk_matter";
    private const string WorkAssignmentEntity = "sprk_workassignment";

    private static readonly Guid SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Tenant = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static readonly Guid RecordId = Guid.Parse("13100000-0000-0000-0000-000000000001");

    private const AccessRights ReadCreateWrite = AccessRights.Read | AccessRights.Create | AccessRights.Write;

    /// <summary>The two workforce planes that read <c>DirectAccessLevel</c> through the cached grant set.</summary>
    public enum Plane
    {
        /// <summary><c>ComposeForContactAsync</c> — a contact-only Teams/SPA user (session 26's C3 scenario).</summary>
        ContactOnly,

        /// <summary><c>ComposeForSystemUserAsync</c> — the grant term via the systemuser's linked contact,
        /// with NO membership on the record, so the membership term cannot mask the grant term.</summary>
        SystemUserViaLinkedContact,
    }

    public static TheoryData<string> RootEntityTypes => new() { ProjectEntity, MatterEntity, WorkAssignmentEntity };

    /// <summary>Matter + work assignment only: the root types whose grant level is nullable (task 032).</summary>
    public static TheoryData<string> NullableLevelRootEntityTypes => new() { MatterEntity, WorkAssignmentEntity };

    public static TheoryData<string, Plane> RootEntityTypesOnBothPlanes
    {
        get
        {
            var data = new TheoryData<string, Plane>();
            foreach (var entityType in new[] { ProjectEntity, MatterEntity, WorkAssignmentEntity })
            {
                data.Add(entityType, Plane.ContactOnly);
                data.Add(entityType, Plane.SystemUserViaLinkedContact);
            }

            return data;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 1 — the C3 scenario: a DIRECT Collaborate grant on a secure root survives the hit.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypesOnBothPlanes))]
    public async Task ComposeAsync_DirectCollaborateGrantOnSecureRoot_ComposesReadCreateWriteOnMissAndIdenticallyOnHit(
        string entityType, Plane plane)
    {
        var world = new CacheWorld(
            Grant(entityType, RecordId, level: ExternalAccessLevel.Collaborate, direct: ExternalAccessLevel.Collaborate),
            secureRecordIds: RecordId);

        var (miss, hit) = await ComposeOnMissThenHitAsync(world, plane, entityType);

        miss.RightsFor(RecordId).Should().Be(ReadCreateWrite,
            $"on the MISS a direct Collaborate grant on a secure {entityType} confers Read|Create|Write ({plane})");
        hit.RightsFor(RecordId).Should().Be(ReadCreateWrite,
            $"C3: on the HIT the same direct grant must still confer Read|Create|Write — before task 131 the " +
            $"cached shape dropped DirectAccessLevel and this was None ({plane})");
        hit.Rights.Should().BeEquivalentTo(miss.Rights,
            "the composed rights map must be identical on a miss and on the hit it populated");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 2 — NO OVER-GRANT: an org-inherited-only grant on a secure root is ABSENT on both.
    //
    // Task 136 (C2): a record whose rights compose to None leaves the answer — RightsFor == None alone is not
    // enough, because a None-rights KEY is what every presence-gated read used to admit. Assert absence.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task ComposeAsync_OrgInheritedOnlyGrantOnSecureRoot_IsAbsentOnMissAndOnHit(string entityType)
    {
        var world = new CacheWorld(
            Grant(entityType, RecordId, level: ExternalAccessLevel.Collaborate, direct: null),
            secureRecordIds: RecordId);

        var (miss, hit) = await ComposeOnMissThenHitAsync(world, Plane.ContactOnly, entityType);

        miss.RightsFor(RecordId).Should().Be(AccessRights.None,
            $"Secure suppression removes the org-inherited contribution on a secure {entityType}");
        miss.Contains(RecordId).Should().BeFalse("task 136: a record with no rights is not in the answer");
        miss.Rights.Should().NotContainKey(RecordId, "absent, not present with no rights");
        hit.RightsFor(RecordId).Should().Be(AccessRights.None,
            "a null DirectAccessLevel must restore from the cache AS null — defaulting it to AccessLevel " +
            "would grant Read|Create|Write on a secure record reached only through an organization");
        hit.Contains(RecordId).Should().BeFalse();
        hit.Rights.Should().NotContainKey(RecordId,
            "task 136: the hit must leave the Secure-suppressed record out of the answer exactly as the miss does");
        hit.Rights.Should().BeEquivalentTo(miss.Rights);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 3 — MIXED provenance: the two levels survive the round trip independently.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task ComposeAsync_ViewOnlyDirectPlusCollaborateOrgOnSecureRoot_ComposesReadOnlyOnMissAndOnHit(string entityType)
    {
        // The deduped shape QueryGrantSetAsync produces for one direct ViewOnly row + one org Collaborate row:
        // effective = max over all rows, direct = max over the contact's own rows.
        var world = new CacheWorld(
            Grant(entityType, RecordId, level: ExternalAccessLevel.Collaborate, direct: ExternalAccessLevel.ViewOnly),
            secureRecordIds: RecordId);

        var (miss, hit) = await ComposeOnMissThenHitAsync(world, Plane.ContactOnly, entityType);

        miss.RightsFor(RecordId).Should().Be(AccessRights.Read);
        hit.RightsFor(RecordId).Should().Be(AccessRights.Read,
            "the direct ViewOnly level must come back as ViewOnly — not dropped (None) and not collapsed " +
            "into the effective Collaborate level (Read|Create|Write)");
        hit.Rights.Should().BeEquivalentTo(miss.Rights);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 4 — NON-SECURE root: the AccessLevel path is unaffected.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task ComposeAsync_OrgInheritedCollaborateGrantOnNonSecureRoot_ComposesReadCreateWriteOnMissAndOnHit(string entityType)
    {
        var world = new CacheWorld(
            Grant(entityType, RecordId, level: ExternalAccessLevel.Collaborate, direct: null));

        var (miss, hit) = await ComposeOnMissThenHitAsync(world, Plane.ContactOnly, entityType);

        miss.RightsFor(RecordId).Should().Be(ReadCreateWrite);
        hit.RightsFor(RecordId).Should().Be(ReadCreateWrite,
            $"on a non-secure {entityType} the effective AccessLevel is read, so an org grant still confers its level");
        hit.Rights.Should().BeEquivalentTo(miss.Rights);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 5 — NULL-LEVEL matter/WA row: confers NOTHING, on a miss and on a hit.
    //
    // Task 136 (defect C2) reversed task 032's rule here. 032 kept a level-less row's id as a None-rights key
    // ("no silent revocation"); every presence-gated read then admitted it. Owner 2026-09-30: no level = not
    // granted, so the record is ABSENT. What this criterion protects for the cache is unchanged: a null level
    // must restore as null. A cache that invented a level would put the record back with rights on the hit.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(NullableLevelRootEntityTypes))]
    public async Task ComposeAsync_NullLevelGrant_ConfersNothingOnMissAndOnHit(string entityType)
    {
        var world = new CacheWorld(Grant(entityType, RecordId, level: null, direct: null));

        var (miss, hit) = await ComposeOnMissThenHitAsync(world, Plane.ContactOnly, entityType);

        miss.Contains(RecordId).Should().BeFalse("task 136: a level-less matter/WA row grants nothing (owner: no level = not granted)");
        miss.Rights.Should().NotContainKey(RecordId, "absent, not present with no rights");
        hit.Contains(RecordId).Should().BeFalse(
            "a null level must restore as null — a cache that invented a level would admit the record here");
        hit.Rights.Should().NotContainKey(RecordId);
        hit.Rights.Should().BeEquivalentTo(miss.Rights);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 6 — VERSIONING: an entry under the previous version's key is never served.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetGrantSetAsync_EntryCachedUnderThePreviousVersionKey_IsNotServedAndDataverseIsRequeried()
    {
        ExternalParticipationService.CacheVersion.Should().Be(5,
            "task 131 moved the cached grant shape to v5 (DirectAccessLevel); a further shape change must " +
            "bump it again and update this pin deliberately");

        var staleRecordId = Guid.Parse("13100000-0000-0000-0000-0000000000f4");
        // The exact v4 shape: (id, level) with no direct level. Readable as a v5 entry — which is precisely
        // why the version in the KEY, not the payload, is what must keep it from being served.
        var v4Payload = new
        {
            projects = new[] { new { projectId = staleRecordId, accessLevel = (int)ExternalAccessLevel.FullAccess } },
            matterGrants = Array.Empty<object>(),
            workAssignmentGrants = Array.Empty<object>(),
        };

        // Control: the same payload under the CURRENT version IS served, so the negative below cannot pass
        // merely because the seed failed to deserialize.
        var control = new CacheWorld(Grant(ProjectEntity, RecordId, ExternalAccessLevel.ViewOnly, ExternalAccessLevel.ViewOnly));
        await control.Cache.SetAsync(Tenant, ExternalParticipationService.ExternalAccessResource, ContactId.ToString(),
            ExternalParticipationService.CacheVersion, v4Payload, TimeSpan.FromSeconds(60));
        var served = await control.Participations.GetGrantSetAsync(ContactId);
        control.Participations.QueryCount.Should().Be(0, "control: an entry under the current key is a hit");
        served.Projects.Select(p => p.ProjectId).Should().Equal(new[] { staleRecordId }, "control: the seeded payload is readable");

        var world = new CacheWorld(Grant(ProjectEntity, RecordId, ExternalAccessLevel.ViewOnly, ExternalAccessLevel.ViewOnly));
        await world.Cache.SetAsync(Tenant, ExternalParticipationService.ExternalAccessResource, ContactId.ToString(),
            ExternalParticipationService.CacheVersion - 1, v4Payload, TimeSpan.FromSeconds(60));

        var set = await world.Participations.GetGrantSetAsync(ContactId);

        world.Participations.QueryCount.Should().Be(1, "a previous-version entry is a MISS — Dataverse is re-queried");
        set.Projects.Select(p => p.ProjectId).Should().Equal(new[] { RecordId },
            "the v4 entry (no DirectAccessLevel) must never be served under v5");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 7 — SHAPE-PARITY GUARD: every public settable property of the three grant types
    // survives a round trip through the production write/read path.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Properties deliberately NOT carried by the cache, each with its reason. Anything public that is not
    /// settable must be named here too, so a new get-only property is a conscious classification rather
    /// than a silent skip.
    /// </summary>
    private static readonly IReadOnlyDictionary<(Type Type, string Property), string> NotCarriedByDesign =
        new Dictionary<(Type, string), string>
        {
            // Derived get-only id views over MatterGrants / WorkAssignmentGrants. They hold no state of their
            // own, so they round-trip exactly when their source lists do.
            [(typeof(ExternalGrantSet), nameof(ExternalGrantSet.Matters))] = "derived view over MatterGrants",
            [(typeof(ExternalGrantSet), nameof(ExternalGrantSet.WorkAssignments))] = "derived view over WorkAssignmentGrants",
            // Task 132 (C12): a faulted set is never written, so the flag is always false on a hit — pinned by
            // GetGrantSetAsync_ACacheHit_NeverReportsFaulted below.
            [(typeof(ExternalGrantSet), nameof(ExternalGrantSet.Faulted))] =
                "a faulted set is never written, so the flag is always false on a hit",
        };

    /// <summary>
    /// Task 132 (C12) — the other half of the <see cref="ExternalGrantSet.Faulted"/> exclusion above: whatever was
    /// cached, a HIT reports a complete answer. The flag is not part of the cached shape, so a hit can only ever restore
    /// it as false — which is the truth, because the only write path skips a faulted set.
    /// </summary>
    [Fact]
    public async Task GetGrantSetAsync_ACacheHit_NeverReportsFaulted()
    {
        var world = new CacheWorld(Grant(ProjectEntity, RecordId, ExternalAccessLevel.ViewOnly, ExternalAccessLevel.ViewOnly));

        var miss = await world.Participations.GetGrantSetAsync(ContactId);
        (await world.ReadCachedEntryAsync()).Should().NotBeNull("precondition: a clean miss writes the entry");
        var hit = await world.Participations.GetGrantSetAsync(ContactId);

        world.Participations.QueryCount.Should().Be(1, "the second read is a cache HIT");
        miss.Faulted.Should().BeFalse("precondition: the read that was cached was complete");
        hit.Faulted.Should().BeFalse("a hit is always a complete answer — a faulted set is never written");
        hit.Projects.Select(p => p.ProjectId).Should().Equal(new[] { RecordId });
    }

    [Fact]
    public async Task GetGrantSetAsync_EveryPublicSettableGrantProperty_SurvivesTheRoundTripThroughTheProductionCache()
    {
        var probe = (ExternalGrantSet)Populate(typeof(ExternalGrantSet));
        var world = new CacheWorld(probe);

        await world.Participations.GetGrantSetAsync(ContactId);
        (await world.ReadCachedEntryAsync()).Should().NotBeNull(
            "the miss must have written the cache entry before the hit is read");
        var hit = await world.Participations.GetGrantSetAsync(ContactId);
        world.Participations.QueryCount.Should().Be(1, "the second read must be a cache HIT");

        var losses = new List<string>();
        CollectLosses(typeof(ExternalGrantSet), probe, hit, nameof(ExternalGrantSet), losses);

        losses.Should().BeEmpty(
            "every public settable property of ExternalGrantSet / ExternalParticipation / ExternalRootGrant " +
            "must survive the grant cache. A property added to a grant type must be carried by " +
            "ExternalParticipationService's cached shape (and CacheVersion bumped) — or named in " +
            "NotCarriedByDesign with its reason. This is the gap that let task 032's AccessLevel and " +
            "task 037's DirectAccessLevel each read correctly on a miss and wrongly on a hit");
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Miss, then (after proving the entry was written) hit. Asserts the Dataverse read ran exactly once
    /// across both, so the second composition is provably served from the cache.
    /// </summary>
    private static async Task<(AccessibleRecordSet Miss, AccessibleRecordSet Hit)> ComposeOnMissThenHitAsync(
        CacheWorld world, Plane plane, string entityType)
    {
        var principal = plane == Plane.ContactOnly ? ContactPrincipal() : SystemUserPrincipal();

        var miss = await world.Composer.ComposeAsync(principal, entityType, CancellationToken.None);
        world.Participations.QueryCount.Should().Be(1, "the first composition is a cache MISS that reads Dataverse");

        (await world.ReadCachedEntryAsync()).Should().NotBeNull(
            $"the miss must have written the v{ExternalParticipationService.CacheVersion} entry before the second " +
            "composition — otherwise the 'hit' would silently re-run the miss and prove nothing");

        var hit = await world.Composer.ComposeAsync(principal, entityType, CancellationToken.None);
        world.Participations.QueryCount.Should().Be(1, "the second composition is a cache HIT — no second Dataverse read");

        return (miss, hit);
    }

    private static WorkforcePrincipal ContactPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = ContactId,
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    private static WorkforcePrincipal SystemUserPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = SystemUserId,
        ContactId = ContactId, // the linked contact (sprk_primarycontact) whose grants form the grant term
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    private static ExternalGrantSet Grant(
        string entityType, Guid recordId, ExternalAccessLevel? level, ExternalAccessLevel? direct)
        => entityType switch
        {
            ProjectEntity => new ExternalGrantSet
            {
                Projects = new[]
                {
                    new ExternalParticipation
                    {
                        ProjectId = recordId,
                        AccessLevel = level ?? throw new ArgumentNullException(nameof(level), "a project grant always carries a level"),
                        DirectAccessLevel = direct,
                    },
                },
                MatterGrants = Array.Empty<ExternalRootGrant>(),
                WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
            },
            MatterEntity => new ExternalGrantSet
            {
                Projects = Array.Empty<ExternalParticipation>(),
                MatterGrants = new[] { new ExternalRootGrant { RecordId = recordId, AccessLevel = level, DirectAccessLevel = direct } },
                WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
            },
            WorkAssignmentEntity => new ExternalGrantSet
            {
                Projects = Array.Empty<ExternalParticipation>(),
                MatterGrants = Array.Empty<ExternalRootGrant>(),
                WorkAssignmentGrants = new[] { new ExternalRootGrant { RecordId = recordId, AccessLevel = level, DirectAccessLevel = direct } },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown root entity type."),
        };

    // ── shape-parity reflection helpers (PUBLIC members only — ADR-038 B8 bans non-public reflection) ──

    private static readonly ExternalAccessLevel[] DistinctLevels =
    {
        ExternalAccessLevel.FullAccess, ExternalAccessLevel.ViewOnly, ExternalAccessLevel.Collaborate,
    };

    /// <summary>The public properties the cache must carry for <paramref name="type"/>.</summary>
    private static IReadOnlyList<PropertyInfo> CarriedProperties(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var unclassified = properties
            .Where(p => p.SetMethod is not { IsPublic: true } && !NotCarriedByDesign.ContainsKey((type, p.Name)))
            .Select(p => $"{type.Name}.{p.Name}")
            .ToList();
        unclassified.Should().BeEmpty(
            "a public property without a public setter is neither populated nor compared by this guard, so it " +
            "must be named in NotCarriedByDesign with the reason it needs no cache slot");

        return properties
            .Where(p => p.SetMethod is { IsPublic: true } && !NotCarriedByDesign.ContainsKey((type, p.Name)))
            .ToList();
    }

    /// <summary>Builds an instance with a non-default value in every carried property.</summary>
    private static object Populate(Type type)
    {
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Could not construct {type.Name}.");

        // Distinct levels per instance, so a slot restored from its SIBLING (e.g. DirectAccessLevel
        // defaulted to AccessLevel) reads back as a different value and is caught.
        var levelOrdinal = 0;
        foreach (var property in CarriedProperties(type))
        {
            var t = property.PropertyType;
            object value;
            if (t == typeof(Guid))
            {
                value = Guid.NewGuid();
            }
            else if (t == typeof(ExternalAccessLevel) || t == typeof(ExternalAccessLevel?))
            {
                value = DistinctLevels[levelOrdinal++ % DistinctLevels.Length];
            }
            else if (t == typeof(IReadOnlyList<ExternalParticipation>))
            {
                value = new List<ExternalParticipation> { (ExternalParticipation)Populate(typeof(ExternalParticipation)) };
            }
            else if (t == typeof(IReadOnlyList<ExternalRootGrant>))
            {
                value = new List<ExternalRootGrant> { (ExternalRootGrant)Populate(typeof(ExternalRootGrant)) };
            }
            else
            {
                throw new InvalidOperationException(
                    $"{type.Name}.{property.Name} has type {t.Name}, which this shape-parity guard cannot populate. " +
                    "Teach Populate a non-default value for it AND carry the property through " +
                    "ExternalParticipationService's cached shape (bumping CacheVersion) — or name it in " +
                    "NotCarriedByDesign with its reason.");
            }

            property.SetValue(instance, value);
        }

        return instance;
    }

    /// <summary>Records every carried property whose value did not survive, recursing into the grant lists.</summary>
    private static void CollectLosses(Type type, object written, object readBack, string path, List<string> losses)
    {
        foreach (var property in CarriedProperties(type))
        {
            var at = $"{path}.{property.Name}";
            var expected = property.GetValue(written);
            var actual = property.GetValue(readBack);

            if (expected is IEnumerable expectedItems)
            {
                var expectedList = expectedItems.Cast<object>().ToList();
                var actualList = (actual as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                if (expectedList.Count != actualList.Count)
                {
                    losses.Add($"{at}: wrote {expectedList.Count} item(s), read back {actualList.Count}");
                    continue;
                }

                var elementType = property.PropertyType.GetGenericArguments().Single();
                for (var i = 0; i < expectedList.Count; i++)
                {
                    CollectLosses(elementType, expectedList[i], actualList[i], $"{at}[{i}]", losses);
                }
            }
            else if (!Equals(expected, actual))
            {
                losses.Add($"{at}: wrote {expected ?? "null"}, read back {actual ?? "null"}");
            }
        }
    }

    /// <summary>
    /// One composition world: the PRODUCTION TenantCache over MemoryDistributedCache, a request carrying a
    /// <c>tid</c> claim (the cache is skipped without one), the production-service double below, and the
    /// REAL AccessibleRecordSetService. Its other boundaries contribute nothing: no standing grant, no
    /// membership, no deny-list entries, no organizations.
    /// </summary>
    private sealed class CacheWorld
    {
        public CacheWorld(ExternalGrantSet dataverseGrants, params Guid[] secureRecordIds)
        {
            Cache = new TenantCache(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                NullLogger<TenantCache>.Instance);

            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", Tenant) }, "test")),
            });

            Participations = new CacheBackedParticipationService(Cache, accessor.Object, dataverseGrants, secureRecordIds);

            // Strict: the contact plane must never walk membership here (no standing grant, no orgs); the
            // systemuser plane walks it once per composition and finds NOTHING — so the grant term alone
            // decides the record's rights and the membership term cannot mask the result.
            var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
            membership
                .Setup(m => m.ResolveAsync(SystemUserId, It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string entityType, MembershipResolveOptions? _, CancellationToken _) => new MembershipResponse(
                    entityType,
                    new PersonIdentity(SystemUserId, ContactId: ContactId),
                    Array.Empty<Guid>(),
                    new Dictionary<string, IReadOnlyList<Guid>>(),
                    0,
                    DateTimeOffset.Parse("2026-09-30T00:00:00Z")));

            var standing = new Mock<ISubjectStandingGrantReader>(MockBehavior.Strict);
            standing.Setup(s => s.ReadForContactAsync(ContactId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StandingGrantState.NotHeld);

            var denyList = new Mock<INoAccessListReader>();
            denyList
                .Setup(d => d.GetDeniedRecordsAsync(
                    It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(NoAccessListResult.Empty);
            // Task 143: the systemuser plane asks through the three-subject overload; nobody is walled here either.
            denyList
                .Setup(d => d.GetDeniedRecordsAsync(
                    It.IsAny<NoAccessSubjects>(),
                    It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(NoAccessListResult.Empty);

            Composer = new AccessibleRecordSetService(
                membership.Object, Participations, standing.Object, denyList.Object,
                Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
                Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.InternalSystemUsers(),
                Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities(),
                NullLogger<AccessibleRecordSetService>.Instance);
        }

        public TenantCache Cache { get; }

        public CacheBackedParticipationService Participations { get; }

        public AccessibleRecordSetService Composer { get; }

        /// <summary>The raw entry under the CURRENT version's key, read through the same TenantCache.</summary>
        public Task<string?> ReadCachedEntryAsync() => Cache.GetStringAsync(
            Tenant, ExternalParticipationService.ExternalAccessResource, ContactId.ToString(),
            ExternalParticipationService.CacheVersion);
    }

    /// <summary>
    /// A subclass of the PRODUCTION <see cref="ExternalParticipationService"/> that overrides only its
    /// data reads — the Dataverse grant query (<c>QueryGrantSetAsync</c>), the veto flags and the
    /// organization reads. <see cref="ExternalParticipationService.GetGrantSetAsync"/> is deliberately NOT
    /// overridden: its cache read, miss fallback and cache write are the subject under test.
    /// </summary>
    private sealed class CacheBackedParticipationService : ExternalParticipationService
    {
        private readonly ExternalGrantSet _dataverseGrants;
        private readonly HashSet<Guid> _secureRecordIds;

        public CacheBackedParticipationService(
            ITenantCache cache, IHttpContextAccessor accessor, ExternalGrantSet dataverseGrants, IEnumerable<Guid> secureRecordIds)
            : base(new HttpClient(), cache, configuration: null!, credential: null!, accessor,
                   NullLogger<ExternalParticipationService>.Instance,
                   filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities())
        {
            _dataverseGrants = dataverseGrants;
            _secureRecordIds = secureRecordIds.ToHashSet();
        }

        /// <summary>How many times the Dataverse grant read ran — 1 across a miss→hit pair proves the hit.</summary>
        public int QueryCount { get; private set; }

        internal override Task<ExternalGrantSet> QueryGrantSetAsync(Guid contactId, CancellationToken ct)
        {
            QueryCount++;
            return Task.FromResult(_dataverseGrants);
        }

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            IReadOnlyDictionary<Guid, RootRecordFlags> flags = recordIds.Distinct().ToDictionary(
                id => id,
                id => _secureRecordIds.Contains(id)
                    ? new RootRecordFlags(IsSecure: true, IsRestricted: false)
                    : RootRecordFlags.None);
            return Task.FromResult(flags);
        }

        // Task 137: the contact's live state. Active, so this double's grants compose exactly as before;
        // the inactive-contact guard itself is pinned by UnifiedEvaluatorSeamTests (task 137 section).
        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(ContactRecordState.Active);

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(ActiveOrgMemberships.None);

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            IReadOnlyDictionary<Guid, ReferencedOrganizations> result =
                recordIds.Distinct().ToDictionary(id => id, _ => ReferencedOrganizations.None);
            return Task.FromResult(result);
        }
    }
}
