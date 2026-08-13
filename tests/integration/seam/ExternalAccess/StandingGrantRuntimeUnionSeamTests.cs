// teams-app-r1 Task 051 — Standing-grant runtime-union vertical-slice SEAM test (ADR-038 §2 path #6).
//
// DEFINITION OF DONE for the standing-grant runtime union (design §5 / spec FR-06 / FR-12). A router- or
// composition-unit test in isolation is NOT sufficient (ADR-043 seam-category governance): this drives
// the REAL slice end-to-end — the production ContactStandingGrantReader (flag gate) composed by the
// production AccessibleRecordSetService (union + record∈set enforcement) — and asserts that toggling
// contact.sprk_standinggrant ENABLE→grants LIVE access and DISABLE→REVOKES it, with NO
// sprk_externalrecordaccess row ever materialized, plus the cache-invalidation that makes a toggle
// reflect promptly.
//
// Only the external boundaries are doubled (per tests/CLAUDE.md seam rules):
//   • IDataverseService     — the FLS-secured flag source (flippable between phases)
//   • IMembershipResolverService — the allowlist-filtered contact-anchored record source (task 021)
//   • the grant loader      — a thin ExternalParticipationService subclass (real compose surface)
// The reader + composition + enforcement decision are all PRODUCTION types.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.ExternalAccess;

public sealed class StandingGrantRuntimeUnionSeamTests
{
    private const string ProjectEntity = "sprk_project";
    private const string ContactEntity = "contact";
    private const string StandingGrantAttribute = "sprk_standinggrant";
    private const string ExternalAccessResource = "external-access-grant";
    // Mirrors ExternalParticipationService.CacheVersion — bumped 1→2 by task 028 (grant-set cache shape).
    private const int CacheVersion = 2;

    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Tenant = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    // A record reachable ONLY via the contact-anchored (allowlisted-role) membership resolver — i.e. only
    // the standing-grant term can admit it. It is NEVER a materialized grant.
    private static readonly Guid StandingOnlyProject = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    // A record the contact holds by an explicit materialized grant, independent of the standing flag.
    private static readonly Guid GrantedProject = Guid.Parse("b0000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task StandingGrant_Enable_GrantsLiveUnionAccess_Disable_Revokes_WithoutMaterializingRows()
    {
        // ── external boundaries ──────────────────────────────────────────────────────────────────
        var flagHeld = false; // the live toggle; flipped between phases below

        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        dataverse
            .Setup(d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Contact(flagHeld)); // re-evaluated each call → reflects the current toggle

        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MembershipOf(StandingOnlyProject)); // allowlist-filtered result (NFR-05, task 021)

        // ── production slice: real reader + real composer over the real compose surface ───────────
        var reader = new ContactStandingGrantReader(dataverse.Object, NullLogger<ContactStandingGrantReader>.Instance);
        var participations = new FakeParticipationService(new[] { GrantedProject });
        var sut = new AccessibleRecordSetService(
            membership.Object, participations, reader, NullLogger<AccessibleRecordSetService>.Instance);

        var principal = ContactPrincipal();

        // ── Phase 1 — DISABLED: only the explicit grant is accessible; the standing-only record is denied.
        flagHeld = false;
        var disabled = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);
        disabled.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject });
        disabled.Sources.StandingGrantMembership.Should().BeFalse();
        (await sut.IsRecordAccessibleAsync(principal, ProjectEntity, StandingOnlyProject, CancellationToken.None))
            .Should().BeFalse("with the standing grant OFF, a membership-only record is NOT accessible");

        // The load-bearing negative: with the flag off, the membership resolver is never even consulted.
        membership.Verify(
            m => m.ResolveByContactAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // ── Phase 2 — ENABLE: the same membership-only record becomes LIVE-accessible via the union.
        flagHeld = true;
        var enabled = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);
        enabled.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject, StandingOnlyProject },
            "enabling the standing grant unions the contact-anchored membership into the accessible set");
        enabled.Sources.StandingGrantMembership.Should().BeTrue();
        (await sut.IsRecordAccessibleAsync(principal, ProjectEntity, StandingOnlyProject, CancellationToken.None))
            .Should().BeTrue("enabling the standing grant confers live access — no per-record row required");

        // ── Phase 3 — REVOKE: toggling off immediately narrows the set back (revoke, not merely 'stop new').
        flagHeld = false;
        var revoked = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);
        revoked.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject });
        (await sut.IsRecordAccessibleAsync(principal, ProjectEntity, StandingOnlyProject, CancellationToken.None))
            .Should().BeFalse("disabling the standing grant revokes the previously-unioned access");

        // ── NO-MATERIALIZATION PROOF ─────────────────────────────────────────────────────────────
        // Across all three phases the only Dataverse call the slice ever made was the app-only flag READ.
        // A Strict mock with ONLY RetrieveAsync configured means any create/update/delete/upsert — i.e.
        // any attempt to materialize a sprk_externalrecordaccess row — would have thrown. It never did.
        dataverse.Verify(
            d => d.RetrieveAsync(ContactEntity, ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(6)); // 3 ComposeAsync + 3 IsRecordAccessibleAsync
        dataverse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StandingGrant_Toggle_InvalidatesPerContactDataCache_SoNextEvaluationIsNotStale()
    {
        // The cache-invalidation limb of the seam: a real ExternalParticipationService over a real cache.
        // Seed the per-contact DATA entry, invalidate (as a standing-grant toggle would), assert it is gone
        // so the next accessible-set evaluation re-reads instead of serving up to 60s of stale TTL.
        var cache = new InMemoryTenantCache();
        await cache.SetAsync(
            Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion,
            new List<int> { 42 }, TimeSpan.FromSeconds(60));

        var svc = new ExternalParticipationService(
            new HttpClient(), cache, configuration: null!, credential: null!,
            httpContextAccessor: new NoopHttpContextAccessor(),
            logger: NullLogger<ExternalParticipationService>.Instance);

        (await cache.GetAsync<List<int>>(Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion))
            .Should().NotBeNull("precondition: the contact's participation data is cached");

        await svc.InvalidateAsync(ContactId, Tenant, CancellationToken.None);

        (await cache.GetAsync<List<int>>(Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion))
            .Should().BeNull("a standing-grant toggle invalidates the DATA cache so the change reflects promptly");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static Entity Contact(bool held)
    {
        var e = new Entity(ContactEntity, ContactId);
        e[StandingGrantAttribute] = held;
        return e;
    }

    private static MembershipResponse MembershipOf(params Guid[] ids) => new(
        ProjectEntity,
        new PersonIdentity(Guid.Empty, ContactId: ContactId),
        ids,
        new Dictionary<string, IReadOnlyList<Guid>>(),
        ids.Length,
        DateTimeOffset.UtcNow.AddMinutes(5));

    private static WorkforcePrincipal ContactPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = ContactId,
        Oid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        TenantId = Tenant,
    };

    /// <summary>Thin real-surface double: overrides only the virtual grant loader
    /// (<see cref="ExternalParticipationService.GetGrantSetAsync"/>, task 028 — base
    /// <c>GetParticipationsAsync</c> delegates to it) (design §5 reuse).</summary>
    private sealed class FakeParticipationService : ExternalParticipationService
    {
        private readonly ExternalGrantSet _grantSet;

        public FakeParticipationService(IEnumerable<Guid> projectIds)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
            => _grantSet = new ExternalGrantSet
            {
                Projects = projectIds
                    .Select(id => new ExternalParticipation { ProjectId = id, AccessLevel = ExternalAccessLevel.ViewOnly })
                    .ToList(),
                Matters = new HashSet<Guid>(),
                WorkAssignments = new HashSet<Guid>(),
            };

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(_grantSet);
    }

    private sealed class NoopHttpContextAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
    }
}
