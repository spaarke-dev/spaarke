// teams-app-r1 Task 051 — ExternalParticipationService.InvalidateAsync tests (design §5 / auth.md).
//
// When a contact's subject-level standing grant (contact.sprk_standinggrant) toggles, the contact's
// accessible set widens/narrows. InvalidateAsync drops the per-Contact participation DATA cache entry
// (tenant:{tid}:external-access-grant:{contactId}:v1) so the change reflects on the NEXT accessible-set
// evaluation instead of waiting out the 60s TTL. Contract being protected (spec FR-06 promptness +
// auth.md "MUST NOT cache authorization decisions"): this invalidates DATA only — the yes/no decision
// is never cached, so there is nothing decision-shaped to invalidate here.
//
// Module-boundary substitutes only (ITenantCache — real InMemoryTenantCache and a Moq verify double).

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class ExternalParticipationServiceInvalidationTests
{
    // Reference the SINGLE SOURCE OF TRUTH consts on ExternalParticipationService directly (they are
    // public by design — see the service's const comment) so a version bump propagates here automatically
    // instead of drifting. (Previously hardcoded CacheVersion = 2, which went stale when task 073 #7
    // bumped the stored/invalidated key to v3 for the org-grant shape — the exact drift the shared const
    // exists to prevent.)
    private const string ExternalAccessResource = ExternalParticipationService.ExternalAccessResource;
    private const int CacheVersion = ExternalParticipationService.CacheVersion;

    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Tenant = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    [Fact]
    public async Task InvalidateAsync_WithExplicitTenant_RemovesTheExactPerContactKey()
    {
        var cache = new Mock<ITenantCache>();
        var sut = CreateSut(cache.Object, httpContext: null);

        await sut.InvalidateAsync(ContactId, Tenant, CancellationToken.None);

        cache.Verify(c => c.RemoveAsync(
            Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateAsync_EndToEnd_SubsequentReadNoLongerSeesCachedEntry()
    {
        // Prove promptness against a real cache: seed the exact key, confirm it is present, invalidate,
        // confirm it is gone (a subsequent evaluation would re-query rather than serve stale TTL data).
        var cache = new InMemoryTenantCache();
        await cache.SetAsync(
            Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion,
            new List<int> { 1, 2, 3 }, TimeSpan.FromSeconds(60));

        (await cache.GetAsync<List<int>>(Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion))
            .Should().NotBeNull("precondition: the per-contact entry is cached");

        var sut = CreateSut(cache, httpContext: null);
        await sut.InvalidateAsync(ContactId, Tenant, CancellationToken.None);

        (await cache.GetAsync<List<int>>(Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion))
            .Should().BeNull("after invalidation the stale entry is gone before the 60s TTL lapses");
    }

    [Fact]
    public async Task InvalidateAsync_WithNoExplicitTenant_FallsBackToTidClaim()
    {
        var cache = new Mock<ITenantCache>();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", Tenant) }))
        };
        var sut = CreateSut(cache.Object, context);

        await sut.InvalidateAsync(ContactId, tenantId: null, CancellationToken.None);

        cache.Verify(c => c.RemoveAsync(
            Tenant, ExternalAccessResource, ContactId.ToString(), CacheVersion,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateAsync_WithNoTenantAvailable_IsNoOp()
    {
        // No explicit tenant and no tid claim → the tenant-scoped key cannot be built → logged no-op,
        // never a mis-scoped removal against a wrong/empty tenant.
        var cache = new Mock<ITenantCache>(MockBehavior.Strict);
        var sut = CreateSut(cache.Object, new DefaultHttpContext()); // no User claims

        await sut.InvalidateAsync(ContactId, tenantId: null, CancellationToken.None);

        cache.Verify(c => c.RemoveAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Task 137 (#1060, defect C5): the ONE invalidation routine ─────────────────────────────────────

    /// <summary>
    /// Every tenant a grant set can be cached under: the request's tid, the CIAM tenant, the app's own tenant and every
    /// configured customer workforce tenant (Model 1: these differ). A GUID configured in upper case is ALSO removed in
    /// the canonical lower-case "D" form the tid claim carries; duplicates collapse.
    /// </summary>
    [Fact]
    public void GrantCacheTenantIds_CoversTheRequestTheCiamTenantAndEveryConfiguredWorkforceTenant()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ciam:TenantId"] = "C1C1C1C1-0000-0000-0000-00000000000C",
                ["AzureAd:TenantId"] = Tenant,
                ["WorkforceIdentity:CustomerTenantIds:0"] = "11111111-1111-1111-1111-111111111111",
                ["WorkforceIdentity:CustomerTenantIds:1"] = Tenant,
            })
            .Build();
        var sut = new Sprk.Bff.Api.Tests.AccessControl.GrantPolicyTestDoubles.MemberPagingParticipationService(new Mock<ITenantCache>().Object, TidContext(), configuration);

        sut.GrantCacheTenantIds().Should().BeEquivalentTo(new[]
        {
            Tenant,
            "C1C1C1C1-0000-0000-0000-00000000000C",
            "c1c1c1c1-0000-0000-0000-00000000000c",
            "11111111-1111-1111-1111-111111111111",
        });
    }

    /// <summary>
    /// A member page that faults mid-walk: the members already read ARE invalidated, the organization is reported as not
    /// fully expanded, and the routine does not throw (the write it serves is unaffected; the rest expire on the TTL).
    /// </summary>
    [Fact]
    public async Task InvalidateGrantSetsAsync_AMemberPageFaults_InvalidatesWhatWasRead_ReportsTheGap_NeverThrows()
    {
        var organizationId = Guid.Parse("0a0a0a0a-0000-0000-0000-000000000137");
        var members = Enumerable.Range(1, 5).Select(i => Guid.Parse($"eeeeeeee-0000-0000-0000-{i:D12}")).ToArray();
        var cache = new InMemoryTenantCache();
        foreach (var member in members)
        {
            await cache.SetAsync(Tenant, ExternalAccessResource, member.ToString(), CacheVersion, new List<int> { 1 });
        }

        var sut = new Sprk.Bff.Api.Tests.AccessControl.GrantPolicyTestDoubles.MemberPagingParticipationService(cache, TidContext(), configuration: null) { PageSize = 2, FailPage = 1 };
        sut.Members[organizationId] = members;

        var outcome = await sut.InvalidateGrantSetsAsync(Array.Empty<Guid>(), new[] { organizationId });

        outcome.OrganizationsNotFullyExpanded.Should().Equal(organizationId);
        (await cache.GetAsync<List<int>>(Tenant, ExternalAccessResource, members[0].ToString(), CacheVersion))
            .Should().BeNull("page 0 was read, so its members were invalidated");
        (await cache.GetAsync<List<int>>(Tenant, ExternalAccessResource, members[4].ToString(), CacheVersion))
            .Should().NotBeNull("a member past the failed page was never read; its entry expires on the TTL");
    }

    /// <summary>The live contact-state read maps only statecode 0 to Active; NULL is unreadable (fail closed).</summary>
    [Theory]
    [InlineData(0, "Active")]
    [InlineData(1, "Inactive")]
    [InlineData(2, "Inactive")]
    [InlineData(null, "Unreadable")]
    public void ContactStateFrom_OnlyStateCodeZeroIsActive(int? stateCode, string expected)
        => ExternalParticipationService.ContactStateFrom(stateCode).ToString().Should().Be(expected);

    /// <summary>
    /// The contact state is read live ONCE per request (remembered in HttpContext.Items), read AGAIN by the next
    /// request, and a fault is never remembered — so the next composition retries rather than inheriting a deny.
    /// </summary>
    [Fact]
    public async Task ReadContactStateAsync_OncePerRequest_AgainNextRequest_AndAFaultIsNotRemembered()
    {
        var sut = new CountingState(TidContext());

        (await sut.ReadContactStateAsync(ContactId, CancellationToken.None)).Should().Be(ContactRecordState.Active);
        (await sut.ReadContactStateAsync(ContactId, CancellationToken.None)).Should().Be(ContactRecordState.Active);
        sut.Reads.Should().Be(1, "the same request reads the row once");

        sut.Context = TidContext();
        sut.Next = ContactRecordState.Inactive;
        (await sut.ReadContactStateAsync(ContactId, CancellationToken.None)).Should().Be(ContactRecordState.Inactive);
        sut.Reads.Should().Be(2, "a new request reads live again — a deactivation is seen at once");

        sut.Context = TidContext();
        sut.Throw = true;
        (await sut.ReadContactStateAsync(ContactId, CancellationToken.None)).Should().Be(ContactRecordState.Unreadable);
        sut.Throw = false;
        sut.Next = ContactRecordState.Active;
        (await sut.ReadContactStateAsync(ContactId, CancellationToken.None)).Should().Be(ContactRecordState.Active,
            "a fault is not remembered");
    }

    /// <summary>
    /// A RETURNED <see cref="ContactRecordState.Unreadable"/> (a non-2xx answer other than 404, or a row with no
    /// <c>statecode</c>) is not remembered either: within the SAME request, the next read goes back to Dataverse. The
    /// thrown-fault case above returns from the catch before the memo is written, so only this case exercises the
    /// memo's own <c>!= Unreadable</c> guard (task 137 r3, verifier finding 2).
    /// </summary>
    [Fact]
    public async Task ReadContactStateAsync_AReturnedUnreadable_IsNotRemembered_WithinTheSameRequest()
    {
        var sut = new CountingState(TidContext()) { Next = ContactRecordState.Unreadable };

        (await sut.ReadContactStateAsync(ContactId, CancellationToken.None)).Should().Be(ContactRecordState.Unreadable);

        sut.Next = ContactRecordState.Active;
        (await sut.ReadContactStateAsync(ContactId, CancellationToken.None)).Should().Be(ContactRecordState.Active,
            "an unreadable answer is retried on the next read of the same request, never memoised");
        sut.Reads.Should().Be(2, "both reads reached Dataverse");
    }

    private static DefaultHttpContext TidContext() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", Tenant) })),
    };

    private sealed class CountingState : ExternalParticipationService
    {
        private readonly HttpContextAccessor _accessor;

        public CountingState(HttpContext context)
            : this(new HttpContextAccessor { HttpContext = context })
        {
        }

        private CountingState(HttpContextAccessor accessor)
            : base(new HttpClient(), Mock.Of<ITenantCache>(), configuration: null!, credential: null!, accessor,
                   NullLogger<ExternalParticipationService>.Instance,
                   filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities())
            => _accessor = accessor;

        public HttpContext? Context { set => _accessor.HttpContext = value; }

        public ContactRecordState Next { get; set; } = ContactRecordState.Active;

        public bool Throw { get; set; }

        public int Reads { get; private set; }

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
        {
            Reads++;
            return Throw
                ? Task.FromException<ContactRecordState>(new HttpRequestException("simulated"))
                : Task.FromResult(Next);
        }
    }

    private static ExternalParticipationService CreateSut(ITenantCache cache, HttpContext? httpContext)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        // InvalidateAsync uses only _cache, _httpContextAccessor, _logger — configuration/credential are
        // never touched on this path, so null! is safe (and keeps the test at the invalidation boundary).
        return new ExternalParticipationService(
            new HttpClient(),
            cache,
            configuration: null!,
            credential: null!,
            accessor.Object,
            NullLogger<ExternalParticipationService>.Instance,
            filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities());
    }
}
