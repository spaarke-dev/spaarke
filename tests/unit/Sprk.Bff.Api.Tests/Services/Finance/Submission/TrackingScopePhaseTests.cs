using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095c: the tracking-scope phase the allocator acts on. <see cref="TrackingScopePhaseProbe.PhaseOf"/> decides P0 / P1 / P2
/// from metadata; <see cref="TrackingScopePhaseCache"/> only moves forward, re-reads at most every five minutes, and never after P2.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): a phase read too early (P2 before the scope key is Active) numbers a no-firm submission that nothing guards; one
/// that moves backward un-writes the scope the backfill relies on. The metadata transport is not mocked (ADR-038 B1): the probe's decision is a
/// pure function over the names and statuses the <c>RetrieveEntityRequest</c> returns.
/// </remarks>
public sealed class TrackingScopePhaseTests
{
    private const string Scope = "sprk_trackingscope";
    private const string Tracking = "sprk_trackingnumber";
    private const string Key = "sprk_invoicesubmission_scope_trackingnumber";

    private static readonly string[] WithScope = ["sprk_name", Tracking, "sprk_vendororg", Scope];
    private static readonly string[] WithoutScope = ["sprk_name", Tracking, "sprk_vendororg"];

    private static (string?, IReadOnlyCollection<string>?, string?) KeyOf(string name, string status, params string[] columns) =>
        (name, columns, status);

    // ── The probe ──

    [Fact]
    public void NoScopeColumn_IsP0_WhateverTheKeys()
    {
        TrackingScopePhaseProbe.PhaseOf(WithoutScope, [KeyOf(Key, "Active", Tracking, Scope)]).Should().Be(TrackingScopePhase.P0NoScopeColumn);
        TrackingScopePhaseProbe.PhaseOf(null, null).Should().Be(TrackingScopePhase.P0NoScopeColumn, "an empty read is not a move forward");
    }

    [Fact]
    public void TheScopeColumnWithoutTheScopeKey_IsP1()
    {
        TrackingScopePhaseProbe.PhaseOf(WithScope, null).Should().Be(TrackingScopePhase.P1ScopeColumn);
        TrackingScopePhaseProbe.PhaseOf(WithScope, [KeyOf("sprk_invoicesubmission_firm_trackingnumber", "Active", Tracking, "sprk_vendororg")])
            .Should().Be(TrackingScopePhase.P1ScopeColumn, "the firm key does not guard a no-firm row");
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("InProgress")]
    [InlineData("Failed")]
    public void TheScopeKeyNotYetActive_IsP1(string status)
    {
        TrackingScopePhaseProbe.PhaseOf(WithScope, [KeyOf(Key, status, Tracking, Scope)]).Should().Be(TrackingScopePhase.P1ScopeColumn);
    }

    [Fact]
    public void AKeyOfTheScopeKeysNameOverOtherColumns_IsNotReliedOn()
    {
        TrackingScopePhaseProbe.PhaseOf(WithScope, [KeyOf(Key, "Active", Scope)]).Should().Be(TrackingScopePhase.P1ScopeColumn);
        TrackingScopePhaseProbe.PhaseOf(WithScope, [KeyOf(Key, "Active", Tracking, Scope, "sprk_vendororg")]).Should().Be(TrackingScopePhase.P1ScopeColumn);
    }

    [Fact]
    public void TheScopeKeyActiveOnTrackingNumberAndScope_IsP2_InAnyCaseAndColumnOrder()
    {
        TrackingScopePhaseProbe.PhaseOf(WithScope, [KeyOf(Key, "Active", Scope, Tracking)]).Should().Be(TrackingScopePhase.P2ScopeKeyActive);
        TrackingScopePhaseProbe.PhaseOf(["SPRK_TrackingScope"], [KeyOf(Key.ToUpperInvariant(), "active", Tracking, Scope)])
            .Should().Be(TrackingScopePhase.P2ScopeKeyActive);
    }

    // ── The cache ──

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Queue<TrackingScopePhase?> _answers = new();
    private int _reads;

    private Task<TrackingScopePhase?> Read(CancellationToken _)
    {
        _reads++;
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
    }

    [Fact]
    public async Task APhaseBelowP2_IsReadOnce_ThenCachedForFiveMinutes_ThenReadAgain()
    {
        var cache = new TrackingScopePhaseCache(_clock);
        _answers.Enqueue(TrackingScopePhase.P1ScopeColumn);
        _answers.Enqueue(TrackingScopePhase.P1ScopeColumn);

        (await cache.GetAsync(Read, force: false, default)).Should().Be(TrackingScopePhase.P1ScopeColumn);
        _clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        (await cache.GetAsync(Read, force: false, default)).Should().Be(TrackingScopePhase.P1ScopeColumn);
        _reads.Should().Be(1, "inside the five minutes the cached phase is used");

        _clock.Advance(TimeSpan.FromSeconds(1));
        await cache.GetAsync(Read, force: false, default);
        _reads.Should().Be(2, "at five minutes the phase is read again");
    }

    [Fact]
    public async Task ThePhaseOnlyMovesForward_AndAnUnreadablePhaseKeepsTheCachedOne()
    {
        var cache = new TrackingScopePhaseCache(_clock);
        _answers.Enqueue(TrackingScopePhase.P1ScopeColumn);
        _answers.Enqueue(TrackingScopePhase.P0NoScopeColumn);
        _answers.Enqueue(null);

        (await cache.GetAsync(Read, force: false, default)).Should().Be(TrackingScopePhase.P1ScopeColumn);
        (await cache.GetAsync(Read, force: true, default)).Should().Be(TrackingScopePhase.P1ScopeColumn, "a lower read never moves it back");
        (await cache.GetAsync(Read, force: true, default)).Should().Be(TrackingScopePhase.P1ScopeColumn, "an unreadable read is not a move");
        _reads.Should().Be(3, "a forced read is made even inside the five minutes");
    }

    [Fact]
    public async Task OnceP2_ThePhaseIsNeverReadAgain_ForcedOrNot()
    {
        var cache = new TrackingScopePhaseCache(_clock);
        _answers.Enqueue(TrackingScopePhase.P2ScopeKeyActive);

        (await cache.GetAsync(Read, force: false, default)).Should().Be(TrackingScopePhase.P2ScopeKeyActive);
        _clock.Advance(TimeSpan.FromHours(1));
        (await cache.GetAsync(Read, force: false, default)).Should().Be(TrackingScopePhase.P2ScopeKeyActive);
        (await cache.GetAsync(Read, force: true, default)).Should().Be(TrackingScopePhase.P2ScopeKeyActive);
        _reads.Should().Be(1);
    }

    [Fact]
    public async Task TwoForcedReReads_QueuedBehindEachOther_DoOneMetadataRead_WhenBothWereRefusedBeforeItStarted()
    {
        var cache = new TrackingScopePhaseCache(_clock);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<TrackingScopePhase?> SlowRead(CancellationToken _)
        {
            _reads++;
            started.TrySetResult();
            return release.Task.ContinueWith(_ => (TrackingScopePhase?)TrackingScopePhase.P1ScopeColumn, TaskScheduler.Default);
        }

        // Two callers, both refused by the platform before any read started, both ask for a forced re-read.
        var refusedAt = cache.ReadsStarted;
        var first = cache.GetAsync(SlowRead, force: true, default, refusedAt);
        await started.Task; // the first read is in flight and holds the gate
        var second = cache.GetAsync(SlowRead, force: true, default, refusedAt);
        release.SetResult();

        (await first).Should().Be(TrackingScopePhase.P1ScopeColumn);
        (await second).Should().Be(TrackingScopePhase.P1ScopeColumn, "the second shares the first's read: it started after the second's refusal");
        _reads.Should().Be(1, "one metadata read for two queued forced re-reads");
    }

    [Fact]
    public async Task AForcedReRead_ForARefusalThatCameAfterTheLastReadStarted_IsStillMade()
    {
        // Must-not-fire control: a read that started before this caller's refusal says nothing of what the refusal showed.
        var cache = new TrackingScopePhaseCache(_clock);
        _answers.Enqueue(TrackingScopePhase.P0NoScopeColumn);
        _answers.Enqueue(TrackingScopePhase.P1ScopeColumn);

        await cache.GetAsync(Read, force: false, default); // read 1, before the refusal
        var refusedAt = cache.ReadsStarted;

        (await cache.GetAsync(Read, force: true, default, refusedAt)).Should().Be(TrackingScopePhase.P1ScopeColumn);
        _reads.Should().Be(2);
    }

    [Fact]
    public async Task BeforeAnyRead_ThePhaseIsP0_AndAnUnreadableFirstReadStaysP0()
    {
        var cache = new TrackingScopePhaseCache(_clock);

        (await cache.GetAsync(Read, force: false, default)).Should().Be(TrackingScopePhase.P0NoScopeColumn, "today's behaviour until a read says otherwise");
        _reads.Should().Be(1);
    }
}
