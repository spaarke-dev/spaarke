using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 ISS-050 (#3059): the per-invoice line-decision lock (2 minutes) is renewed while a long call is still writing, and a call whose
/// renewal answers false or throws writes nothing more and answers the existing "incomplete, repeat to complete" outcome (503
/// <c>ebilling.decision.incomplete</c>; 503 <c>ebilling.decision.unavailable</c> before the first write). Time is a <see cref="FakeTimeProvider"/>
/// moved by the writes themselves, and the lock is <see cref="LeaseLocks"/>, which expires and renews like the real one.
/// </summary>
public sealed partial class InvoiceLineDecisionServiceTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);

    /// <summary>
    /// ISS-050: a processing lock with owners and an expiry on a fake clock, as <c>IdempotencyService</c> behaves: an acquire is refused while an
    /// unexpired lock is held; a renewal answers true only for the holder while its lock is unexpired, and extends it. A test can force the answer
    /// of the n-th renewal (0-based), make it throw, and look at the world at that moment (<see cref="OnRenew"/>).
    /// </summary>
    internal sealed class LeaseLocks(TimeProvider clock) : IIdempotencyService
    {
        private readonly Dictionary<string, (string Owner, DateTimeOffset ExpiresAt)> _held = new();

        public readonly List<(string Key, string Owner, TimeSpan Duration, DateTimeOffset At)> Acquires = [];
        public readonly List<(string Key, string Owner, TimeSpan Duration, DateTimeOffset At)> Renewals = [];
        public readonly List<(string Key, string Owner, DateTimeOffset At)> Releases = [];

        /// <summary>The forced answer of renewal n; null = the real answer.</summary>
        public Func<int, bool?> RenewAnswer { get; set; } = _ => null;

        /// <summary>The fault renewal n throws; null = none.</summary>
        public Func<int, Exception?> RenewFault { get; set; } = _ => null;

        /// <summary>Called with n before renewal n is answered.</summary>
        public Action<int>? OnRenew { get; set; }

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) =>
            TryAcquireProcessingLockAsync(eventId, eventId, lockDuration, cancellationToken);

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, string ownerId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            lock (_held)
            {
                var now = clock.GetUtcNow();
                if (_held.TryGetValue(eventId, out var held) && held.ExpiresAt > now)
                {
                    return Task.FromResult(false);
                }

                var duration = lockDuration ?? TimeSpan.FromMinutes(5);
                _held[eventId] = (ownerId, now + duration);
                Acquires.Add((eventId, ownerId, duration, now));
                return Task.FromResult(true);
            }
        }

        public Task<bool> RenewProcessingLockAsync(string eventId, string ownerId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
        {
            int n;
            lock (_held)
            {
                n = Renewals.Count;
                Renewals.Add((eventId, ownerId, lockDuration, clock.GetUtcNow()));
            }

            OnRenew?.Invoke(n);
            if (RenewFault(n) is { } fault)
            {
                return Task.FromException<bool>(fault);
            }

            lock (_held)
            {
                var now = clock.GetUtcNow();
                var real = _held.TryGetValue(eventId, out var held) && held.Owner == ownerId && held.ExpiresAt > now;
                var answer = RenewAnswer(n) ?? real;
                if (answer)
                {
                    _held[eventId] = (ownerId, now + lockDuration);
                }

                return Task.FromResult(answer);
            }
        }

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default) =>
            ReleaseProcessingLockAsync(eventId, eventId, cancellationToken);

        public Task ReleaseProcessingLockAsync(string eventId, string ownerId, CancellationToken cancellationToken = default)
        {
            lock (_held)
            {
                if (_held.TryGetValue(eventId, out var held) && held.Owner == ownerId)
                {
                    _held.Remove(eventId);
                }

                Releases.Add((eventId, ownerId, clock.GetUtcNow()));
            }

            return Task.CompletedTask;
        }

        /// <summary>The intervals between the times <paramref name="owner"/> took, renewed and released <paramref name="key"/>, in order. The lock
        /// never lapsed while the call ran when every interval is shorter than the duration it was taken or renewed for.</summary>
        public IReadOnlyList<TimeSpan> Gaps(string key, string owner)
        {
            var touches = Acquires.Where(a => a.Key == key && a.Owner == owner).Select(a => a.At)
                .Concat(Renewals.Where(r => r.Key == key && r.Owner == owner).Select(r => r.At))
                .Concat(Releases.Where(r => r.Key == key && r.Owner == owner).Select(r => r.At))
                .Order()
                .ToList();
            return touches.Zip(touches.Skip(1), (a, b) => b - a).ToList();
        }
    }

    /// <summary>The write counts: lines, totals, correction rows, Decision Records, Signal closes.</summary>
    private static (int Lines, int Totals, int Corrections, int Records, int Closes) Snapshot(World w) =>
        (w.LineWrites.Count, w.TotalsWrites.Count, w.Corrections.Count, w.H.Created.Count, w.Closes.Count);

    /// <summary>A world on a fake clock with the expiring lock, <paramref name="lines"/> Clean lines with AI fields, one open Signal each.</summary>
    private static (World W, FakeTimeProvider Clock, LeaseLocks Locks, List<Entity> Lines) LongWorld(int lines)
    {
        var w = new World(billedTotal: 100m * lines);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));
        var locks = new LeaseLocks(clock);
        w.Clock = clock;
        w.LockService = locks;
        var rows = new List<Entity>();
        for (var i = 0; i < lines; i++)
        {
            var line = w.AddLine(100m, InvoiceLineStatus.Clean, extraction: Extraction);
            w.AddSignal(line.Id);
            rows.Add(line);
        }

        return (w, clock, locks, rows);
    }

    /// <summary>Moves the clock inside the record step (the recorder resolves the decider's identity once per line record).</summary>
    private static void OnRecord(World w, Action tick) =>
        w.Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) =>
            {
                tick();
                return new PersonIdentity(user, ContactId: Guid.Parse("01000000-0000-4000-8000-0000000000e1"));
            });

    // ── (1) A call that outlives one lock duration renews and completes ──

    [Fact(DisplayName = "ISS-050 (1): Accept all Clean over 10 lines taking 200 s (past the 2-minute lock) renews the lock as this owner at every checkpoint past 30 s, never lets it lapse, and completes")]
    public async Task LongCall_RenewsTheLock_AndCompletes()
    {
        var (w, clock, locks, lines) = LongWorld(10);
        var start = clock.GetUtcNow();
        OnRecord(w, () => clock.Advance(TimeSpan.FromSeconds(20)));

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None, CallerToken);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        (clock.GetUtcNow() - start).Should().BeGreaterThan(TwoMinutes, "the call outlived one lock duration");
        var key = InvoiceLineDecisionService.LockKeyFor(w.InvoiceId);
        var owner = locks.Acquires.Should().ContainSingle().Subject.Owner;
        locks.Renewals.Should().HaveCountGreaterThanOrEqualTo(4)
            .And.OnlyContain(r => r.Key == key && r.Owner == owner && r.Duration == TwoMinutes, "this call's lock, renewed for its own duration");
        locks.Gaps(key, owner).Should().OnlyContain(gap => gap < TwoMinutes, "the lock never lapsed while the call wrote");
        locks.Releases.Should().ContainSingle().Which.Owner.Should().Be(owner, "released owner-checked");
        w.H.Created.Should().HaveCount(10, "one Decision Record per line");
        w.ClosedBy.Should().HaveCount(10, "every line's Signal closed");
        lines.Should().OnlyContain(l => Status(w, l) == InvoiceLineStatus.Accepted);
    }

    // ── (4) A short call completes as before, with no renewal ──

    [Fact(DisplayName = "ISS-050 (4): a short call makes no renewal and completes exactly as before; its lock is released owner-checked")]
    public async Task ShortCall_MakesNoRenewal_AndCompletesAsBefore()
    {
        var (w, _, locks, lines) = LongWorld(2);

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, Guid.NewGuid(), Decider, CancellationToken.None, CallerToken);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        locks.Renewals.Should().BeEmpty("no checkpoint was 30 s past the acquire");
        var owner = locks.Acquires.Should().ContainSingle().Subject.Owner;
        locks.Releases.Should().ContainSingle().Which.Owner.Should().Be(owner);
        w.H.Created.Should().HaveCount(2);
        w.ClosedBy.Should().HaveCount(2);
        lines.Should().OnlyContain(l => Status(w, l) == InvoiceLineStatus.Accepted);
    }

    // ── (2) / (3) A renewal that answers false or throws: nothing more is written; the same-content repeat finishes ──

    [Theory(DisplayName = "ISS-050 (2)(3): a renewal that answers false or throws stops the call before its next write and answers the existing outcome; the same decision repeated completes it")]
    [InlineData("ownership", "false", null)]
    [InlineData("ownership", "throws", null)]
    [InlineData("lines", "false", "totals")]
    [InlineData("lines", "throws", "totals")]
    [InlineData("totals", "false", "corrections")]
    [InlineData("totals", "throws", "corrections")]
    [InlineData("record", "false", "record")]
    [InlineData("record", "throws", "record")]
    [InlineData("close", "false", "record")]
    [InlineData("close", "throws", "record")]
    public async Task LostLock_StopsBeforeTheNextWrite_AndTheRepeatCompletes(string slowStep, string renewal, string? failedStep)
    {
        var (w, clock, locks, lines) = LongWorld(3);
        var ticking = true;
        void Tick(string step)
        {
            if (ticking && step == slowStep)
            {
                clock.Advance(TimeSpan.FromSeconds(40));
            }
        }

        w.OnWrite = Tick;
        OnRecord(w, () => Tick("record"));
        w.Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Tick("ownership");
                return w.Owner;
            });

        (int, int, int, int, int)? atLoss = null;
        locks.OnRenew = n => atLoss ??= Snapshot(w);
        locks.RenewAnswer = n => n == 0 && renewal == "false" ? false : null;
        locks.RenewFault = n => n == 0 && renewal == "throws" ? new TimeoutException("the lock store timed out") : null;
        var decisionId = Guid.NewGuid();

        var result = await w.Service().AcceptCleanAsync(w.InvoiceId, decisionId, Decider, CancellationToken.None, CallerToken);

        locks.Renewals.Should().ContainSingle("the call stopped at its first failed renewal");
        atLoss.Should().NotBeNull();
        Snapshot(w).Should().Be(atLoss!.Value, "nothing at all was written after the renewal failed");
        var owner = locks.Acquires.Should().ContainSingle().Subject.Owner;
        locks.Renewals[0].Owner.Should().Be(owner);
        locks.Releases.Should().ContainSingle().Which.Owner.Should().Be(owner, "still released owner-checked");
        w.Log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("lock-lost"));
        if (failedStep is null)
        {
            result.Status.Should().Be(LineDecisionStatus.Unavailable, "nothing was written yet: 503, never 409");
            result.Code.Should().Be(LineDecisionCodes.Unavailable);
            NothingWritten(w);
        }
        else
        {
            result.Status.Should().Be(LineDecisionStatus.Incomplete, "503 incomplete, never 409, never continued unlocked");
            result.Code.Should().Be(LineDecisionCodes.Incomplete);
            result.FailedStep.Should().Be(failedStep);
            result.CommittedLineIds.Should().BeEquivalentTo(lines.Select(l => l.Id));
            w.H.Created.Count.Should().BeLessThan(lines.Count, "the call stopped before every record was written");
        }

        // The same decision repeated (the documented repeat: the same decision id over the committed lines) finishes the work under a new lock.
        ticking = false;
        var again = failedStep is null
            ? await w.Service().AcceptCleanAsync(w.InvoiceId, decisionId, Decider, CancellationToken.None, CallerToken)
            : await w.Service().DecideAsync(
                Command(w, LineDecisionAction.Accept, result.CommittedLineIds!.Select(id => new LineDecisionLine(id)), decisionId: decisionId, bulk: true),
                CancellationToken.None);

        again.Status.Should().Be(LineDecisionStatus.Applied);
        w.H.Created.Should().HaveCount(lines.Count, "one record per line, each written once");
        w.ClosedBy.Should().HaveCount(lines.Count, "every line's Signal closed");
        lines.Should().OnlyContain(l => Status(w, l) == InvoiceLineStatus.Accepted);
        w.ApprovedTotal.Should().Be(100m * lines.Count);
    }
}
