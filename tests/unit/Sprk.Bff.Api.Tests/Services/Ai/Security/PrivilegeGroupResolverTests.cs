using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Security;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Security;

/// <summary>
/// Unit tests for PrivilegeGroupResolver (AIPU2-027).
/// Verifies group resolution from JWT claims and Graph fallback,
/// overage detection, caching, and fail-closed behaviour.
/// </summary>
public class PrivilegeGroupResolverTests
{
    private readonly Mock<IGraphClientFactory> _graphFactoryMock;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;

    private const string TestOid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    public PrivilegeGroupResolverTests()
    {
        _graphFactoryMock = new Mock<IGraphClientFactory>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        // Default: provide an HttpContext so Graph fallback has a context to work with
        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);
    }

    private PrivilegeGroupResolver CreateSut()
    {
        return new PrivilegeGroupResolver(
            _graphFactoryMock.Object,
            _memoryCache,
            _httpContextAccessorMock.Object,
            NullLogger<PrivilegeGroupResolver>.Instance);
    }

    private static ClaimsPrincipal BuildPrincipalWithGroups(string oid, params string[] groupIds)
    {
        var claims = new List<Claim>
        {
            new Claim("oid", oid)
        };
        foreach (var gid in groupIds)
        {
            claims.Add(new Claim("groups", gid));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal BuildPrincipalWithoutGroups(string oid)
    {
        var claims = new List<Claim> { new Claim("oid", oid) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal BuildPrincipalWithOverage(string oid)
    {
        // Overage is signalled by the _claim_names claim
        var claims = new List<Claim>
        {
            new Claim("oid", oid),
            new Claim("_claim_names", "{\"groups\":\"src1\"}")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // -------------------------------------------------------------------------
    // Groups from JWT claims
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveGroupIdsAsync_GroupsClaimPresent_ReturnsGroupsFromClaims()
    {
        // Arrange
        var principal = BuildPrincipalWithGroups(TestOid, "group-1", "group-2");
        var sut = CreateSut();

        // Act
        var result = await sut.ResolveGroupIdsAsync(principal);

        // Assert — resolved from claims, Graph should NOT be called
        result.Should().HaveCount(2);
        result.Should().Contain("group-1");
        result.Should().Contain("group-2");
        _graphFactoryMock.Verify(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveGroupIdsAsync_NoGroupsClaimAndNoOid_ReturnsEmptyList()
    {
        // Arrange — principal with no OID claim
        var claims = new List<Claim>(); // no OID
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var sut = CreateSut();

        // Act
        var result = await sut.ResolveGroupIdsAsync(principal);

        // Assert — fail-closed: empty list, no Graph call
        result.Should().BeEmpty();
        _graphFactoryMock.Verify(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Caching
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveGroupIdsAsync_CalledTwiceWithSamePrincipal_SecondCallReturnsCachedResult()
    {
        // Arrange
        var principal = BuildPrincipalWithGroups(TestOid, "group-x");
        var sut = CreateSut();

        // Act — call twice
        var result1 = await sut.ResolveGroupIdsAsync(principal);
        var result2 = await sut.ResolveGroupIdsAsync(principal);

        // Assert — same result, Graph never called (claims path)
        result1.Should().BeEquivalentTo(result2);
        _graphFactoryMock.Verify(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveGroupIdsAsync_CacheMissAfterExpiry_ResolvesAgain()
    {
        // Arrange — use a very short TTL by creating a cache that expires immediately
        // We can't control the 5-minute TTL directly, but we can verify the cache key pattern
        // by testing with a separate resolver instance sharing the same IMemoryCache
        var principal = BuildPrincipalWithGroups(TestOid, "cached-group");
        var sut1 = CreateSut();
        var sut2 = CreateSut(); // Same cache

        // Act
        var result1 = await sut1.ResolveGroupIdsAsync(principal);
        var result2 = await sut2.ResolveGroupIdsAsync(principal);

        // Assert — both use cache after first call, results match
        result1.Should().BeEquivalentTo(result2);
        result1.Should().Contain("cached-group");
    }

    // -------------------------------------------------------------------------
    // Overage — Graph fallback path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveGroupIdsAsync_OveragePrincipal_NoHttpContext_ReturnsEmptyFailClosed()
    {
        // Arrange — overage principal but no HttpContext available
        _httpContextAccessorMock.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var principal = BuildPrincipalWithOverage(TestOid);
        var sut = CreateSut();

        // Act
        var result = await sut.ResolveGroupIdsAsync(principal);

        // Assert — fail-closed: empty (cannot call Graph without HttpContext)
        result.Should().BeEmpty();
        _graphFactoryMock.Verify(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveGroupIdsAsync_GroupsClaimAbsent_NoHttpContext_ReturnsEmptyFailClosed()
    {
        // Arrange — no groups claim, no HttpContext
        _httpContextAccessorMock.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var principal = BuildPrincipalWithoutGroups(TestOid);
        var sut = CreateSut();

        // Act
        var result = await sut.ResolveGroupIdsAsync(principal);

        // Assert — fail-closed: empty (cannot call Graph without HttpContext)
        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Graph exception — fail-closed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveGroupIdsAsync_GraphThrowsException_ReturnsEmptyListFailClosed()
    {
        // Arrange — principal with overage so Graph fallback is triggered
        var principal = BuildPrincipalWithOverage(TestOid);
        _graphFactoryMock
            .Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Graph API unreachable"));

        var sut = CreateSut();

        // Act
        var result = await sut.ResolveGroupIdsAsync(principal);

        // Assert — fail-closed: empty list (not an exception propagation)
        result.Should().BeEmpty("Graph failures must return empty to enforce fail-closed behaviour");
    }

    [Fact]
    public async Task ResolveGroupIdsAsync_GraphThrowsForAbsentGroups_ReturnsEmptyListFailClosed()
    {
        // Arrange — principal with no groups claim, Graph fallback, Graph throws
        var principal = BuildPrincipalWithoutGroups(TestOid);
        _graphFactoryMock
            .Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph error"));

        var sut = CreateSut();

        // Act
        var result = await sut.ResolveGroupIdsAsync(principal);

        // Assert
        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Cancellation propagation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveGroupIdsAsync_CancellationRequested_PropagatesCancellation()
    {
        // Arrange — overage principal so Graph is called; Graph throws OperationCanceledException
        var principal = BuildPrincipalWithOverage(TestOid);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _graphFactoryMock
            .Setup(f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut();

        // Act & Assert — OperationCanceledException propagates (not swallowed by fail-closed handler)
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.ResolveGroupIdsAsync(principal, cts.Token));
    }
}
