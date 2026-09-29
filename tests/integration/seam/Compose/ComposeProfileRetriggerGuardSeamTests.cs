// Task 070 cluster 5a (#781 adjacent) — the G10 profiling STORM GUARD.
//
// WHY THIS FILE EXISTS. The seam map's coverage pass measured cluster 5a at 64.3% branch — the
// weakest code in `ComposeService` — and said to extract it last or give it tests first. Before the
// extraction, a mutation was seeded to confirm that judgement: invert the eTag comparison in
// `MaybeRetriggerProfileOnLoadAsync`, so an UNCHANGED reopen re-dispatches the profile and a
// genuinely CHANGED document never does. The entire 1,814-test Compose suite stayed GREEN.
//
// Both halves of that inversion are real production harm:
//   - unchanged reopen re-profiling  = a profiling storm on every document open, which is precisely
//     what G10's stamp exists to prevent;
//   - changed document NOT profiling = a document silently keeps a stale profile forever.
//
// Neither direction had a test. These do, and they assert the guard from BOTH sides — a test that
// only covered the "changed" path would still pass under the inversion, because the inversion swaps
// the branches rather than disabling one.
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038).

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeProfileRetriggerGuardSeamTests
{
    private const string SpeId = "drive-item-profile-guard";

    /// <summary>
    /// A REAL <see cref="IDistributedCache"/> (in-memory), not a mock. The guard's whole behaviour is
    /// "what did I stamp last time, and does it match now" — a mock would let the test assert the
    /// stamp was WRITTEN without ever proving it is READ BACK on the next open, which is the half the
    /// storm guard actually depends on.
    /// </summary>
    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(new OptionsWrapperShim());

    private static ComposeProfileRetriggerGuard NewGuard(IDistributedCache? cache) =>
        new(cache, NewDispatcher(), NullLogger.Instance);

    // The dispatcher is constructed with every collaborator null: on this path it is never invoked
    // with a live scope, and a null-service dispatch is a documented no-op (ADR-032 kill-switch
    // shape). What these tests observe is the STAMP, which is the guard's own decision record.
    private static ComposeProfileDispatcher NewDispatcher() =>
        new(null!, null, null!, NullLogger.Instance);

    private static string StampKey(string speId) =>
        ComposeProfileRetriggerGuard.ProfiledETagKeyPrefix + speId;

    [Fact]
    public async Task MaybeRetriggerProfileOnLoad_WhenLiveETagMatchesTheStamp_LeavesTheStampUntouched()
    {
        // Arrange: the document was profiled at "etag-v1" and has NOT changed since.
        var cache = NewCache();
        await cache.SetStringAsync(StampKey(SpeId), "etag-v1");
        var guard = NewGuard(cache);

        // Act: a reopen at the SAME eTag.
        await guard.MaybeRetriggerProfileOnLoadAsync(
            Guid.NewGuid(), SpeId, "etag-v1", httpContext: null!, CancellationToken.None);

        // Assert: the guard took the SKIP branch — the stamp is untouched, no re-profile.
        (await cache.GetStringAsync(StampKey(SpeId))).Should().Be("etag-v1",
            "an unchanged reopen must skip: re-profiling here is the storm G10 exists to prevent");
    }

    [Fact]
    public async Task MaybeRetriggerProfileOnLoad_WhenLiveETagDiffersFromTheStamp_RestampsToTheLiveETag()
    {
        // Arrange: profiled at "etag-v1"; Word has since saved the document externally.
        var cache = NewCache();
        await cache.SetStringAsync(StampKey(SpeId), "etag-v1");
        var guard = NewGuard(cache);

        // Act: a reopen at a DIFFERENT eTag.
        await guard.MaybeRetriggerProfileOnLoadAsync(
            Guid.NewGuid(), SpeId, "etag-v2", httpContext: null!, CancellationToken.None);

        // Assert: the guard took the RE-TRIGGER branch and closed the loop by re-stamping, so the
        // NEXT unchanged reopen skips. Restamping is the observable proof the branch ran.
        (await cache.GetStringAsync(StampKey(SpeId))).Should().Be("etag-v2",
            "a changed document must re-profile AND re-stamp, or it would re-trigger on every future open");
    }

    [Fact]
    public async Task MaybeRetriggerProfileOnLoad_WhenNeverProfiled_StampsTheLiveETag()
    {
        // A document Compose has never profiled has no stamp at all — null != live, so it profiles once.
        var cache = NewCache();
        var guard = NewGuard(cache);

        await guard.MaybeRetriggerProfileOnLoadAsync(
            Guid.NewGuid(), SpeId, "etag-first", httpContext: null!, CancellationToken.None);

        (await cache.GetStringAsync(StampKey(SpeId))).Should().Be("etag-first",
            "a never-profiled document profiles once, then stamps so it does not storm");
    }

    [Fact]
    public async Task MaybeRetriggerProfileOnLoad_TwiceAtTheSameETag_StampsOnceAndThenSkips()
    {
        // The closed loop, end to end: first open profiles + stamps; the second open at the same
        // eTag must SKIP. This is the test that fails under the seeded inversion in BOTH directions,
        // because the inversion makes the second call re-trigger.
        var cache = NewCache();
        var guard = NewGuard(cache);

        await guard.MaybeRetriggerProfileOnLoadAsync(
            Guid.NewGuid(), SpeId, "etag-stable", httpContext: null!, CancellationToken.None);
        var afterFirst = await cache.GetStringAsync(StampKey(SpeId));

        await guard.MaybeRetriggerProfileOnLoadAsync(
            Guid.NewGuid(), SpeId, "etag-stable", httpContext: null!, CancellationToken.None);
        var afterSecond = await cache.GetStringAsync(StampKey(SpeId));

        afterFirst.Should().Be("etag-stable");
        afterSecond.Should().Be("etag-stable",
            "repeated reopens at one eTag converge on a single profile — the storm guard's whole point");
    }

    [Fact]
    public async Task MaybeRetriggerProfileOnLoad_WhenCacheIsNull_DoesNotThrow()
    {
        // ADR-032 kill-switch shape: no cache configured (a bare test host) must no-op, never fail a
        // Load. Profiling is enrichment, not a precondition for opening a document.
        var guard = NewGuard(cache: null);

        var act = async () => await guard.MaybeRetriggerProfileOnLoadAsync(
            Guid.NewGuid(), SpeId, "etag-v1", httpContext: null!, CancellationToken.None);

        await act.Should().NotThrowAsync("a missing cache degrades to no-op; Load is never blocked by profiling");
    }

    [Fact]
    public async Task SetProfiledETag_WithAnEmptyETag_WritesNoStamp()
    {
        // An empty eTag is not a fact about the document, and stamping it would make the NEXT open
        // compare against "" and re-trigger. The guard declines to write it.
        var cache = NewCache();
        var guard = NewGuard(cache);

        await guard.SetProfiledETagAsync(SpeId, string.Empty, CancellationToken.None);

        (await cache.GetStringAsync(StampKey(SpeId))).Should().BeNull(
            "an empty eTag carries no information — stamping it would guarantee a re-trigger next open");
    }

    /// <summary>Minimal <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> shim so the test
    /// can construct a real <see cref="MemoryDistributedCache"/> without pulling in DI.</summary>
    private sealed class OptionsWrapperShim : Microsoft.Extensions.Options.IOptions<MemoryDistributedCacheOptions>
    {
        public MemoryDistributedCacheOptions Value { get; } = new();
    }
}
