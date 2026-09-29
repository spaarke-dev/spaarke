# Task 091 — findings (#848 Tier 2 real-clock failures)

> **Date**: 2026-08-28 · **Branch**: `ci/task-091-deterministic-timing` · **Status**: 4 of 5 fixed, 1 escalated

Issue #848 grouped five Tier 2 failures as "real-clock timing tests". **Four were. One was not.**
It also asserted that `FakeTimeProvider` "is referenced nowhere" (via #795) — that is stale: it is
already used in 16 files, including one of the very files #848 lists.

---

## 1. What was actually wrong (4 fixed)

| Test | Real root cause | Fix |
|---|---|---|
| `StorageRetryPolicyTests.ExecuteAsync_CancellationDuringRetry_StopsRetrying` | A 500ms `CancelAfter` timer raced the policy's 2s backoff. When the runner was slow the timer landed **after** attempt #2 returned `"success"`, so `ExecuteAsync` completed normally and `ThrowAsync<OperationCanceledException>` failed. | Cancel **synchronously** inside attempt #1. No clock at all. Assertion tightened `BeLessThan(4)` → `Be(1)`. **No production change.** |
| `RetryAndIdempotencyTests.CancellationDuringRetryLoop_StopsImmediately_…` | Waited on the real clock for an every-second cron to dispatch, then asserted a `Stopwatch` ceiling. | `FakeTimeProvider`; the 5s retry sleep is virtual time never granted. |
| `ScheduledJobHostTests.StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07` | Same — `startedTcs.Task.WaitAsync(5s)` against a real cron tick. | `FakeTimeProvider` + behavioural assertions. |
| `ScheduledJobHostTests.InvalidCronExpression_LoggedAndJobSkipped_HostKeepsRunning` | Same. | `FakeTimeProvider`. |

**The production side was already done.** `ScheduledJobHost` has taken an optional `TimeProvider`
and routed *every* sleep through it for some time — line 488 even says "the host's sleeps are ALL
virtualizable, so a FakeTimeProvider test can prove that a wake-up came from cancellation rather
than from elapsed wall-clock time". The tests simply never adopted it. No production code was
changed by this task.

**Result**: 6 consecutive local runs, 46 passed / 0 failed, stable **5s** (was 24s locally and
**5m14s** in CI).

### The retry fix is stronger, not weaker

Dropping the `Stopwatch` ceiling reads like a relaxation. It is the opposite. The retry sleep is
now 5s of *virtual* time that the test never advances, so if the sleep ignored its cancellation
token `StopAsync` could not return at all. Completing is therefore **proof** the wake-up came from
the token — the exact NFR-07 property the wall-clock ceiling could only approximate.

---

## 2. Virtual-clock hazard found in `TickAsync` (documented, not changed)

`TickAsync` refreshes definitions *before* the due-check, and a refresh recomputes `NextFireUtc`
from `now` **exclusive**. So with `RefreshInterval` (200ms) shorter than the cron period (1s) and a
perfectly periodic advance, the refresh lands on every tick that would have dispatched and pushes
the job to the next occurrence — **dispatch starves forever**. Real time escapes this only because
sleeps overshoot and the alignment drifts.

This is latent in production for any job whose cron period exceeds `RefreshInterval`, but it needs
jitter-free timing to bite, which real deployments do not have. **Not changed** — tests now state
`RefreshInterval > cron period` explicitly (`VirtualClockOptions`) instead of relying on the
accident. Worth a look if the scheduler is ever moved onto a virtualized or deterministic clock.

---

## 3. `TriggerNowAsync` ignores host shutdown (found, not fixed — out of scope)

`ScheduledJobHost.TriggerNowAsync` comments say *"We use the host's stoppingToken … so admin client
cancellation doesn't kill an in-flight run"* — and then passes **`CancellationToken.None`** to
`RunManualTriggerAsync`. A manually-triggered job therefore cannot observe host shutdown at all:
`StopAsync` waits out the full `ShutdownDrainTimeout` and logs the NFR-07 warning.

Nothing currently fails because of this, and no test covers it. Flagged for the scheduling surface
owner; deliberately **not** fixed here (production behaviour change, unrelated to #848).

---

## 4. The fifth test is a different defect — ESCALATED

`ReAnalysisFlowTests.ReAnalysis_HappyPath_EmitsProgressThenDocumentReplaceThenDone` is **not a
real-clock timing test**. It contains no `Stopwatch`, no `DateTime.UtcNow`, no timing assertion —
its assertions are `body.Should().Contain("data: ")` and `Contain("\"type\":")`.

It **reproduces deterministically on a developer machine** (so it is not a load flake), failing with:

```
TaskCanceledException: The operation was canceled.
---- HttpRequestException : Error while copying content to a stream.
-------- IOException : The client aborted the request.
```

That is `HttpClient`'s **100-second default timeout**. The SSE path attempts a **live Azure Search
call** (the test's own comment documents this, from the 2026-06-01 RB-T028 repair), which cannot
succeed in a test environment.

### Why it surfaced in a job called "Full Unit Tests"

`ci-tier2-advisory.yml` runs pass 1 as bare `dotnet test` with **no project or category filter** —
"Pass 1 runs everything". So the whole solution runs, including `Spe.Integration.Tests`. A job
named *Full Unit Tests* is executing integration tests that require live Azure services.

**That is a defect in this project's own deliverable**, and it is the real subject of this fifth
failure.

### Why it was not fixed here

`scripts/ci/shadow-window-status.ps1` prints, and the spec binds:

> Do NOT edit `ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml` while it runs:
> changing the configuration invalidates what was observed.

The window was at **6/20 PRs, 0.8/5 days** at the time of this task. Scoping the Tier 2 test
invocation is a tier-file edit, so doing it now would reset the window and delay `sdap-ci.yml`
retirement — the project's critical path. It also is not a free call: excluding integration tests
from Tier 2 means they run nowhere on a PR, which is an owner decision, not an implementer's.

**Recommendation** (for after cutover, task 090 or a follow-on):

1. Split the Tier 2 job into `unit` and `integration` legs, or filter pass 1 by project.
2. Either give `ReAnalysisFlowTests` a doubled Search boundary, or move it to a nightly leg where
   live Azure credentials exist.
3. Do **not** simply raise the `HttpClient` timeout — that trades a 2-minute failure for a
   longer one and still cannot make a live Search call succeed in CI.

Until then `ReAnalysis_HappyPath_…` remains red in Tier 2, which is **advisory and non-blocking**,
so it does not gate any PR.

---

## Acceptance criteria status

| Criterion | Status |
|---|---|
| Tier 2 `Full Unit Tests (Debug)` reports 0 failures | ⚠️ **Partial** — 4 of 5 fixed; the 5th is a different defect, escalated above |
| No `Stopwatch` / `Task.Delay` / `DateTime.UtcNow` remains in the touched tests | ✅ for the four repaired tests. `WaitUntilAsync` is retained for the `TriggerNowAsync` tests — a completion wait, not a timing assertion, and never flaky (nothing scheduled). 10 `[Fact(Skip)]` tests in these files still reference it; un-skipping them is follow-on work. |
| Tier 2 p95 within the NFR-02 8-minute budget | ✅ Improved — `Spaarke.Scheduling.Tests` 5m14s → seconds; nothing added |
