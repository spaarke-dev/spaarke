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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class ExternalParticipationServiceInvalidationTests
{
    // Must match the private consts in ExternalParticipationService (the documented cache-key contract).
    private const string ExternalAccessResource = "external-access-grant";
    private const int CacheVersion = 1;

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
            NullLogger<ExternalParticipationService>.Instance);
    }
}
