# Assessment — "How to Improve the Spaarke Test Suite"

> **Reviewed**: 2026-08-28 · **Source**: `notes/how-to-improve-the-Spaarke-test-suite.md`
> The document asks for a ground-truth pass before implementation. This is it.

**Verdict**: the diagnosis is good and the four blind spots it identifies in `/test-diet` are real.
But **roughly half of §5 already exists**, one of its warnings has already come true, and several
proposals carry standing maintenance cost that collides with owner north star #4 — *no full-time
CI/CD manager*. Recommendation below is a much smaller subset than the document proposes, ordered
by value-per-unit-of-standing-cost.

---

## 1. Ground truth (every `<CONFIRM>` resolved)

| Doc's assumption | Reality |
|---|---|
| CI platform `<CONFIRM>` | **GitHub Actions**, `.github/workflows/` |
| `/test-diet`, `/task-create`, `/task-execute`, `/project-pipeline` exist | ✅ all five confirmed |
| "~11,000 existing tests" | **9,841** server test attributes + **730** client test files. Same order; the figure is ~12% high |
| §5.1 rerun-on-failure `<CONFIRM current CI retry behavior>` | **ALREADY BUILT.** `ci-tier2-advisory.yml` runs two passes; `scripts/ci/classify-and-retry.ps1` emits the retry filter; only a test failing **both** counts |
| §5.2 flake registry — `<CONFIRM whether existing tooling already has a place for this>` | **PARTIALLY BUILT.** `tests/.reliability-registry.json` exists — 11 `TimingSensitive` + 3 `ConcurrencySensitive` entries |
| Stryker.NET present or evaluated | **No.** Zero references anywhere in the repo. Genuinely greenfield |
| Shared-fixture registry | **No.** Confirmed gap |
| §5.3 auto-quarantine | No formal trait — but see the finding below |

### The doc's own warning has already come true

§6.4 warns that quarantine must not "become a graveyard." **It already is one:**

- **143** `Skip=` markers
- **137** `[Trait("status", "repaired")]` markers

Nothing today prompts anyone to revisit either. Task 091 hit this directly: **10 skipped tests in
two files** all cite *"needs TimeProvider refactor"* — a refactor that now exists, shipped in #884.
The skip reasons went stale the moment the blocker was removed, and no mechanism noticed.

**This is the highest-value finding in the review.** The debt the document proposes elaborate new
machinery to prevent is already sitting in the repo, already enumerated, and already fixable.

---

## 2. Immediate action — three registry entries are now WRONG

Task 091 made these deterministic (virtual clock / synchronous cancellation). All three are still
listed in `tests/.reliability-registry.json`:

| Entry | Bucket | Status after #884 |
|---|---|---|
| `ScheduledJobHostTests.StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07` | TimingSensitive | now deterministic |
| `RetryAndIdempotencyTests.CancellationDuringRetryLoop_StopsImmediately_DoesNotSleepThroughToken` | ConcurrencySensitive | now deterministic |
| `StorageRetryPolicyTests.ExecuteAsync_CancellationDuringRetry_StopsRetrying` | ConcurrencySensitive | now deterministic |

Leaving them in is not cosmetic — **it actively weakens the suite**. A registry entry buys a test a
free pass-2 retry. A genuine future regression in `StopAsync_CancelsInFlightJobWithinDrainTimeout_NFR07`
— a test that *names an NFR* — would now be retried and could pass on the second attempt, exactly
the masking this project exists to remove.

**This is the real §5.2 gap, and it is not "we lack a registry".** The registry is hand-curated with
no lifecycle: entries go in when a test flakes and nothing ever takes them out. That asymmetry, not
absence, is the defect.

---

## 3. What fits Spaarke

Judged against the four owner north stars, especially **#4 — no full-time CI/CD manager**.

### Adopt — high value, low or zero standing cost

| # | Proposal | Why it fits |
|---|---|---|
| **A** | **Registry-exit rule**: removing a test's flake source REQUIRES removing its registry entry in the same PR | Fixes a live correctness defect (§2). Zero standing cost — it is a code-review line, and `/test-diet` can check it mechanically |
| **B** | **Skip-debt paydown**: work the existing 143 `Skip=` + 137 `repaired` markers | The backlog is already enumerated. 10 are already unblocked by #884. This is §6.3's "guaranteed forward motion" without inventing a quota system |
| **C** | §6.1 **touch-radius expansion** in `/test-diet` | Genuinely near-zero marginal cost — the file is already open. Best value-per-effort in the document |
| **D** | §3 **test-scope clause** in POML acceptance criteria | One template line in `task-create`. Prevention beats cleanup, and this project's own POMLs already do it informally |

### Adopt cautiously — real value, real cost

| # | Proposal | Caveat |
|---|---|---|
| **E** | §2.1 **I1–I4 isolation checks** | The diagnosis is right: coupling is invisible to B1–B17. But I1/I2 are *hard* to detect statically and will produce false positives. Recommend starting with **I4 only** (test appears in the reliability registry) — it is a lookup, needs no heuristics, and is immediately correct |
| **F** | §4.2 **fixture registry** | Real gap. But it is a hand-maintained file that rots exactly like the reliability registry already does. Only worth it if paired with rule A's lifecycle discipline |

### Do not adopt now

| # | Proposal | Why not |
|---|---|---|
| **G** | §2.2 / §5.4 **mutation testing (Stryker.NET)** | Two problems. **Cost**: mutation analysis on a 9,841-test suite is very expensive, and the doc concedes the runtime is unmeasured. **Direction**: ADR-038 makes coverage *observation, never a gate*, precisely because metrics drive test-count inflation. Mutation score is the same class of metric. This project just spent months arguing tests should be **fewer and better**; introducing a thoroughness score risks re-arming the ratchet it removed. Revisit only after the suite is stable, and if adopted, as **observation only** |
| **H** | §6.3 **fixed-quota rotation + test-debt ledger** | Adds a mandatory chore to *every* project's wrap-up plus a ledger to maintain — precisely the standing overhead north star #4 forbids. Item **B** gets the same forward motion from a backlog that already exists, with no new ceremony |
| **I** | §5.3 **auto-quarantine** | The repo already auto-accumulates quarantine informally and cannot drain it. Adding a mechanism that quarantines *faster* before the drain works would make the graveyard grow faster. Sequence B first; revisit only if the skip count is falling |

---

## 4. Where the document is most right

Two points deserve to survive into governance regardless of what else is adopted:

1. **§1's blind-spot analysis is correct.** `/test-diet` classifies test *shape*; coupling,
   redundancy, and spec alignment are orthogonal axes it structurally cannot see. That framing is
   worth keeping even if the proposed detectors are not all built.
2. **§0 and §8's insistence that "a change not reflected in governed files is not reliably
   honored"** matches this repo's actual behavior. Anything adopted must land in ADR-038 /
   `.claude/constraints/testing.md` / `tests/CLAUDE.md`, not only in a skill file.

## 5. Where it needs correction before implementation

- It proposes building §5.1/§5.2 that **already exist**. Its own instruction — *"extend, don't
  rebuild"* — applies to itself here.
- It treats quarantine as a future risk. It is a **present condition** with 143 residents.
- Its rollout sequence starts with new machinery. Ground truth says start with **draining what is
  already accumulated** (B) and **fixing the registry lifecycle** (A) — both of which make the later
  items smaller.

## 6. Recommended sequence

1. **A** — registry-exit rule + remove the three now-wrong entries *(do immediately; correctness bug)*
2. **B** — skip-debt paydown, starting with the 10 unblocked by #884
3. **C** + **D** — touch-radius expansion, test-scope clause *(cheap, preventive)*
4. **E (I4 only)** — registry-lookup isolation check
5. **F** — fixture registry, only with A's lifecycle discipline attached
6. Re-evaluate **G/H/I** once the skip count is measurably falling

Items 1–2 are the ones that pay for themselves immediately. Everything below 3 should wait until
after cutover (071) — none of it is on the critical path, and the shadow window is the live gate.
