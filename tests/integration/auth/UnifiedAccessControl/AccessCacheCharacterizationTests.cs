using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Caching;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Characterization suite for <see cref="CachedAccessDataSource"/>.
///
/// Pins finding A-19 (unified-access-control-r2 spec NFR-07): the resource cache key is
/// <c>sdap:auth:access:{userId}:{resourceId}</c> (CachedAccessDataSource.cs:65) and omits the
/// <c>userAccessToken</c> the method itself accepts. Two callers share this single decorator —
/// <c>AuthorizationService</c> always passes null (app-only mode) and <c>AiAuthorizationService</c>
/// passes the caller bearer (OBO mode) — so for 60 seconds an app-only snapshot can be served to an
/// OBO caller, defeating the one genuinely caller-scoped check in the system.
///
/// Tests seed the cache directly rather than racing the production write, which is deliberately
/// fire-and-forget (<c>_ = CacheSnapshotAsync(...)</c>, line 105). Seeding keeps the assertion
/// deterministic with no Stopwatch/Task.Delay (tests/CLAUDE.md TimeProvider rule).
/// </summary>
public class AccessCacheCharacterizationTests
{
    private const string UserId = "caller-oid-1";
    private const string ResourceId = "document-1";

    /// <summary>The exact key production computes at CachedAccessDataSource.cs:65.</summary>
    private const string ProductionCacheKey = "sdap:auth:access:caller-oid-1:document-1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class RecordingInnerSource : IAccessDataSource
    {
        public List<string?> TokensReceived { get; } = new();

        public AccessRights RightsToReturn { get; set; } = AccessRights.None;

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId,
            string resourceId,
            string? userAccessToken = null,
            CancellationToken ct = default)
        {
            TokensReceived.Add(userAccessToken);

            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = RightsToReturn
            });
        }
    }

    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    /// <summary>
    /// Writes an entry in the exact shape <c>CachedAccessDataSource</c> reads back (camelCase,
    /// <c>accessRightsValue</c> as the int flags value).
    /// </summary>
    private static async Task SeedCacheAsync(IDistributedCache cache, string key, AccessRights rights)
    {
        var payload = new
        {
            userId = UserId,
            resourceId = ResourceId,
            accessRightsValue = (int)rights,
            teamMemberships = Array.Empty<string>(),
            roles = Array.Empty<string>(),
            cachedAt = DateTimeOffset.UtcNow
        };

        await cache.SetStringAsync(key, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static CachedAccessDataSource Decorator(IAccessDataSource inner, IDistributedCache cache) =>
        new(inner, cache, NullLogger<CachedAccessDataSource>.Instance);

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-19. Flipped by task 014 (FR-13).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-19 — CURRENT (BROKEN) BEHAVIOR. An entry written under app-only (service-principal) mode is
    /// served to a caller presenting an OBO bearer token, because the key contains no auth-mode
    /// discriminator. The inner data source is never consulted, so the OBO caller silently inherits
    /// an answer computed from *application* visibility.
    ///
    /// FLIPPED BY: task 014 (FR-13) — once the auth mode is part of the key, the OBO call MUST miss
    /// and reach the inner source, so this asserts TokensReceived contains the OBO token.
    /// </summary>
    [Fact]
    public async Task Characterization_GetUserAccessAsync_ServesAppOnlySnapshotToOboCaller()
    {
        // Arrange — an app-only-mode snapshot already in cache (app can see the doc → Read).
        var cache = NewCache();
        await SeedCacheAsync(cache, ProductionCacheKey, AccessRights.Read);

        // The inner source would answer None for this caller — i.e. the OBO truth is "no access".
        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };

        // Act — an OBO caller asks the same (user, resource) question.
        var snapshot = await Decorator(inner, cache)
            .GetUserAccessAsync(UserId, ResourceId, userAccessToken: "obo-bearer-token");

        // Assert — CURRENT behavior: cache HIT, app-only answer returned, OBO truth never consulted.
        snapshot.AccessRights.Should().Be(AccessRights.Read,
            "A-19 pins the CURRENT broken state: the app-only snapshot is served to the OBO caller");
        inner.TokensReceived.Should().BeEmpty(
            "the cache hit short-circuits the inner source, so the caller-scoped OBO check never runs. " +
            "Task 014 adds auth mode to the key; this then becomes a MISS and the token reaches the inner source.");
    }

    /// <summary>
    /// A-19 — the mirror direction: an OBO-mode entry is likewise served to an app-only caller. Pins
    /// that the key is symmetric-blind, not merely missing one case.
    ///
    /// FLIPPED BY: task 014 (FR-13).
    /// </summary>
    [Fact]
    public async Task Characterization_GetUserAccessAsync_ServesOboSnapshotToAppOnlyCaller()
    {
        // Arrange — cache entry that (conceptually) came from an OBO evaluation.
        var cache = NewCache();
        await SeedCacheAsync(cache, ProductionCacheKey, AccessRights.Read | AccessRights.Write);

        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };

        // Act — an app-only caller (userAccessToken: null) asks the same question.
        var snapshot = await Decorator(inner, cache)
            .GetUserAccessAsync(UserId, ResourceId, userAccessToken: null);

        // Assert — CURRENT behavior: same entry, no discrimination.
        snapshot.AccessRights.Should().Be(AccessRights.Read | AccessRights.Write);
        inner.TokensReceived.Should().BeEmpty();
    }

    /// <summary>
    /// A-19 — states the defect directly: the cache key production computes does not vary with the
    /// auth mode. Both a null token and a bearer token hit the SAME seeded key.
    ///
    /// FLIPPED BY: task 014 (FR-13) — after the fix at most one of these two calls may hit.
    /// </summary>
    [Fact]
    public async Task Characterization_CacheKey_DoesNotVaryWithAuthMode()
    {
        // Arrange — one seeded entry under the single key production uses for both modes.
        var cache = NewCache();
        await SeedCacheAsync(cache, ProductionCacheKey, AccessRights.Read);

        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };
        var sut = Decorator(inner, cache);

        // Act — both auth modes, same (user, resource).
        var appOnly = await sut.GetUserAccessAsync(UserId, ResourceId, userAccessToken: null);
        var obo = await sut.GetUserAccessAsync(UserId, ResourceId, userAccessToken: "obo-bearer-token");

        // Assert — one cached entry satisfied both, so neither reached the inner source.
        appOnly.AccessRights.Should().Be(AccessRights.Read);
        obo.AccessRights.Should().Be(AccessRights.Read);
        inner.TokensReceived.Should().BeEmpty(
            "a single cache entry served both auth modes — the key carries no auth-mode discriminator");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — must already hold. Task 014 MUST NOT break these while re-keying.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserAccessAsync_OnCacheMiss_DelegatesToInnerSourceAndForwardsToken()
    {
        // Arrange — empty cache.
        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.Read };

        // Act
        var snapshot = await Decorator(inner, NewCache())
            .GetUserAccessAsync(UserId, ResourceId, userAccessToken: "obo-bearer-token");

        // Assert — the token IS forwarded to the inner source on a miss (only the KEY ignores it).
        snapshot.AccessRights.Should().Be(AccessRights.Read);
        inner.TokensReceived.Should().ContainSingle().Which.Should().Be("obo-bearer-token");
    }

    [Fact]
    public async Task GetUserAccessAsync_ForDifferentResources_DoesNotShareCacheEntry()
    {
        // Arrange — seed only resource "document-1".
        var cache = NewCache();
        await SeedCacheAsync(cache, ProductionCacheKey, AccessRights.Read);

        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };

        // Act — ask about a DIFFERENT resource.
        var snapshot = await Decorator(inner, cache)
            .GetUserAccessAsync(UserId, "document-2", userAccessToken: null);

        // Assert — resource IS part of the key, so this misses and reaches the inner source.
        snapshot.AccessRights.Should().Be(AccessRights.None);
        inner.TokensReceived.Should().ContainSingle();
    }

    [Fact]
    public async Task GetUserAccessAsync_ForDifferentUsers_DoesNotShareCacheEntry()
    {
        // Arrange — seed only user "caller-oid-1".
        var cache = NewCache();
        await SeedCacheAsync(cache, ProductionCacheKey, AccessRights.Read);

        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };

        // Act — ask as a DIFFERENT user.
        var snapshot = await Decorator(inner, cache)
            .GetUserAccessAsync("caller-oid-2", ResourceId, userAccessToken: null);

        // Assert — user IS part of the key.
        snapshot.AccessRights.Should().Be(AccessRights.None);
        inner.TokensReceived.Should().ContainSingle();
    }
}
