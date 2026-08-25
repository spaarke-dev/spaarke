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
/// Characterization + negative suite for <see cref="CachedAccessDataSource"/>.
///
/// ✅ A-19 FLIPPED BY TASK 014 (FR-13, unified-access-control-r2 spec NFR-07). The resource cache key
/// was <c>sdap:auth:access:{userId}:{resourceId}</c> (pre-fix CachedAccessDataSource.cs:65) and omitted
/// the <c>userAccessToken</c> the method itself accepts. Two callers share this single decorator —
/// <c>Spaarke.Core.Auth.AuthorizationService</c> always passes null (app-only/"sp" mode) and
/// <c>AiAuthorizationService</c> passes the caller bearer ("obo" mode) — so for up to 60 seconds an
/// app-only snapshot could be served to an OBO caller, defeating the one genuinely caller-scoped check
/// in the system. Task 014 added an <c>{authMode}</c> ("sp"/"obo") segment to the key so the two modes
/// can never share an entry; see the class doc comment on <see cref="CachedAccessDataSource"/> for the
/// full rationale (including why a boolean flag — not a token-identity hash — is sufficient given
/// <c>userId</c> is already the caller's stable 'oid' claim).
///
/// Tests seed the cache directly rather than racing the production write, which is deliberately
/// fire-and-forget (<c>_ = CacheSnapshotAsync(...)</c>, line 105). Seeding keeps the assertion
/// deterministic with no Stopwatch/Task.Delay (tests/CLAUDE.md TimeProvider rule).
/// </summary>
public class AccessCacheCharacterizationTests
{
    private const string UserId = "caller-oid-1";
    private const string ResourceId = "document-1";

    /// <summary>The exact key production computes for SP (app-only) mode — CachedAccessDataSource.cs
    /// resourceCacheKey when userAccessToken is null/empty.</summary>
    private const string ProductionCacheKey = "sdap:auth:access:sp:caller-oid-1:document-1";

    /// <summary>The exact key production computes for OBO mode, same (user, resource) — resourceCacheKey
    /// when userAccessToken is non-empty. Added by task 014 alongside the authMode key segment.</summary>
    private const string OboProductionCacheKey = "sdap:auth:access:obo:caller-oid-1:document-1";

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
    // A-19 — Flipped by task 014 (FR-13).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED BY TASK 014 (FR-13) — was <c>Characterization_GetUserAccessAsync_ServesAppOnlySnapshotToOboCaller</c>,
    /// pinning A-19: an entry written under app-only (service-principal) mode was served to a caller
    /// presenting an OBO bearer token, because the key carried no auth-mode discriminator. The inner
    /// data source was never consulted, so the OBO caller silently inherited an answer computed from
    /// *application* visibility.
    ///
    /// Now that the key includes the <c>{authMode}</c> segment, the app-only entry lives under the "sp"
    /// key while the OBO call looks under the "obo" key — a guaranteed MISS — so the request reaches
    /// the inner source and gets the true, independently-computed OBO answer.
    /// </summary>
    [Fact]
    public async Task GetUserAccessAsync_OboCallerAfterAppOnlySnapshotCached_MissesCacheAndConsultsInnerSource()
    {
        // Arrange — an app-only-mode snapshot already in cache under the "sp" key (app can see the doc → Read).
        var cache = NewCache();
        await SeedCacheAsync(cache, ProductionCacheKey, AccessRights.Read);

        // The inner source answers None for this caller — the OBO truth is "no access", the opposite
        // of the cached SP-mode answer. If the fix were absent, the stale Read would leak through.
        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };

        // Act — an OBO caller asks the same (user, resource) question.
        var snapshot = await Decorator(inner, cache)
            .GetUserAccessAsync(UserId, ResourceId, userAccessToken: "obo-bearer-token");

        // Assert — the OBO caller gets the caller-scoped truth (None), not the poisoned SP snapshot
        // (Read). Anti-vacuity: also prove the inner source was actually reached, WITH the OBO token —
        // a "None" result reached by some other path (e.g. an unrelated bug) would not prove the fix.
        snapshot.AccessRights.Should().Be(AccessRights.None,
            "A-19 is fixed: the OBO call must MISS the SP-mode cache entry and get the real OBO answer");
        inner.TokensReceived.Should().ContainSingle().Which.Should().Be("obo-bearer-token",
            "the cache MISS must forward the caller's own bearer token to the inner source");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 014 (FR-13) — was <c>Characterization_GetUserAccessAsync_ServesOboSnapshotToAppOnlyCaller</c>,
    /// the mirror direction: an OBO-mode entry was likewise served to an app-only caller, proving the
    /// key was symmetric-blind, not merely missing one case. Same fix, same guarantee in reverse.
    /// </summary>
    [Fact]
    public async Task GetUserAccessAsync_AppOnlyCallerAfterOboSnapshotCached_MissesCacheAndConsultsInnerSource()
    {
        // Arrange — cache entry seeded under the "obo" key (an OBO evaluation's answer).
        var cache = NewCache();
        await SeedCacheAsync(cache, OboProductionCacheKey, AccessRights.Read | AccessRights.Write);

        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };

        // Act — an app-only caller (userAccessToken: null) asks the same question.
        var snapshot = await Decorator(inner, cache)
            .GetUserAccessAsync(UserId, ResourceId, userAccessToken: null);

        // Assert — the app-only caller gets its own (independently computed) answer, not the OBO
        // caller's cached Read|Write. Anti-vacuity: prove the inner source was reached with a null token.
        snapshot.AccessRights.Should().Be(AccessRights.None,
            "A-19 is fixed: the app-only call must MISS the OBO-mode cache entry and get the real app-only answer");
        inner.TokensReceived.Should().ContainSingle().Which.Should().BeNull(
            "the cache MISS must reach the inner source in app-only mode (null token), never inheriting the OBO entry");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 014 (FR-13) — was <c>Characterization_CacheKey_DoesNotVaryWithAuthMode</c>,
    /// stating the defect directly: production's cache key did not vary with auth mode, so a null
    /// token and a bearer token hit the SAME seeded entry. This now asserts the opposite property
    /// directly: for the SAME (userId, resourceId), the two auth modes resolve to two DISTINCT cache
    /// entries, so a hit for one mode can never satisfy a request in the other mode.
    /// </summary>
    [Fact]
    public async Task GetUserAccessAsync_SameUserAndResourceDifferentAuthMode_ProducesDistinctCacheEntries()
    {
        // Arrange — seed ONLY the SP-mode key; the OBO-mode key for the same (user, resource) is empty.
        var cache = NewCache();
        await SeedCacheAsync(cache, ProductionCacheKey, AccessRights.Read);

        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };
        var sut = Decorator(inner, cache);

        // Act — both auth modes, same (user, resource).
        var appOnly = await sut.GetUserAccessAsync(UserId, ResourceId, userAccessToken: null);
        var obo = await sut.GetUserAccessAsync(UserId, ResourceId, userAccessToken: "obo-bearer-token");

        // Assert — the SP-mode call still hits its own cached entry; the OBO-mode call, having no
        // entry under its own key, misses and gets the inner source's independent answer. Anti-vacuity:
        // exactly ONE inner call (the OBO one) proves the SP call stayed a genuine cache hit rather than
        // both calls coincidentally reaching the inner source.
        appOnly.AccessRights.Should().Be(AccessRights.Read,
            "the SP-mode call still hits its own seeded entry — the fix must not break same-mode caching");
        obo.AccessRights.Should().Be(AccessRights.None,
            "the OBO-mode call has no entry under the obo key, so it must miss and reach the inner source");
        inner.TokensReceived.Should().ContainSingle().Which.Should().Be("obo-bearer-token",
            "exactly one inner call (the OBO miss) — the SP call must remain a pure cache hit");
    }

    /// <summary>
    /// New negative test (task 014) enforcing the project constraint that the raw user access token
    /// MUST NOT appear in, or as, the cache key. Proof: a key shaped as if the raw token occupied the
    /// auth-mode position is seeded with a distinctive rights value; if production actually built the
    /// key that way, a request presenting that exact token would read it back. It does not — the real
    /// key uses the "sp"/"obo" mode flag, so this seeded entry is unreachable and the call falls
    /// through to the inner source instead.
    /// </summary>
    [Fact]
    public async Task GetUserAccessAsync_CacheKey_DoesNotEmbedRawUserAccessToken()
    {
        // Arrange — a key that WOULD be correct if the implementation embedded the raw token (instead
        // of the "sp"/"obo" mode flag) at the auth-mode position.
        const string token = "obo-bearer-token";
        var tokenShapedKey = $"sdap:auth:access:{token}:{UserId}:{ResourceId}";

        var cache = NewCache();
        await SeedCacheAsync(cache, tokenShapedKey, AccessRights.Write);

        var inner = new RecordingInnerSource { RightsToReturn = AccessRights.None };

        // Act — call with that exact token.
        var snapshot = await Decorator(inner, cache)
            .GetUserAccessAsync(UserId, ResourceId, userAccessToken: token);

        // Assert — the real key is "sdap:auth:access:obo:...", not the token-shaped one, so this MUST
        // miss the seeded Write entry and reach the inner source instead.
        snapshot.AccessRights.Should().Be(AccessRights.None,
            "the cache key uses a mode flag, never the raw token — a token-shaped key must not be readable");
        inner.TokensReceived.Should().ContainSingle().Which.Should().Be(token,
            "the miss must still forward the real token to the inner source (only the KEY excludes it)");
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
