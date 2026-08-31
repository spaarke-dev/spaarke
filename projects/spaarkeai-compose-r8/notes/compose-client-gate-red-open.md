# OPEN DEFECT — `compose-client-gate` is red on master, and my fix did NOT fix it

> **Status**: **OPEN** · Recorded 2026-08-31 · Not introduced by 071/072 (those are server-side C#)

## State

`ComposeWorkspace.redline-from-ledger.test.tsx` fails ~19-20 of its 24 tests in CI with
`Unable to find role="textbox"` — the TipTap editor has not mounted when the wait expires.

**Red on every master commit for at least the last 14**, predating this project's Track D work. The gate
is **not a required check** (only `Router` is), which is why PRs keep merging over it — #915 did, and so
did #916.

## What I tried, and why it failed — recorded so the next person does not repeat it

**PR #916 (merged `41b77dfb5`) DID NOT FIX THIS.** It raised RTL's `asyncUtilTimeout` to 15s and removed
14 explicit `{ timeout: 5000 }` overrides. Result: failures went 20 → 19 and the suite went **105s → 294s**.
The arithmetic is the tell — 19 failures × 15s ≈ 285s — so the waits consumed their full budget and still
lost. It bought one test and tripled the runtime. **Consider reverting or re-tuning it** as part of the
real fix; it is not load-bearing on its own.

### Two hypotheses I proved WRONG (do not re-run these)

1. **"It is the RTL vs jest timeout confusion."** Real distinction (jest `testTimeout` does not govern
   `findBy*`), and #908 had indeed only fixed the outer one — but raising the inner one did not fix it.
   My "positive control" (`asyncUtilTimeout: 1` reproduces the failure) proved the knob was **wired**,
   not that it was the **cause**. I over-read it.
2. **"`--coverage` instrumentation is the cost."** Looked decisive at 10s vs 102s — but that comparison
   was **warm cache vs cold cache**, not coverage. Measured properly with `jest --clearCache` before each
   run: **20s without coverage, 26s with**. ~1.3x, not 10x. Not the cause.

Also ruled out: stale `@spaarke/ui-components` dist (rebuilt, still passes locally); surviving
`virtual: true` resolver pollution (none remain — the flag was removed deliberately, see jest.config.js).

## What the evidence actually says

| Fact | Value |
|---|---|
| Local `npm run test:ci` (identical command to CI) | **PASSES** — 104 suites / 1,336 tests |
| Local cores | **32** · CI runner | **4** |
| Suite wall-clock, this file, locally | ~20s cold · in CI | **105s → 294s** |
| Failure mode | editor never mounts within the budget, whatever the budget |

The command is identical (`jest --ci --coverage --maxWorkers=2`); coverage is ~1.3x; the dist is fine.
What is left is **CPU starvation on a 4-core runner** — 104 suites, 2 workers, every one of them mounting
a real ProseMirror editor, alongside whatever else shares that runner.

## Recommended next steps (in preference order)

1. **Stop mounting the whole editor for assertions that do not need one.** `jest.config.js`'s own note
   already says this: *"If a test needs MORE than 30s, that is a defect in the test… split it, or stop
   mounting the whole editor for an assertion that does not need one."* This suite mounts
   `ComposeWorkspace → ComposeEditor → real TipTap` 24 times to assert on ledger materialisation. That is
   the actual defect.
2. **Isolate the heavy suite** — run it in its own job, or with `--maxWorkers=1`, so it is not competing
   with a second worker for 4 cores.
3. **Drop `--coverage` from this gate.** Small win (~1.3x) but free: coverage here is explicitly NOT
   gated (jest.config.js header; ADR-038 "coverage = observation, never a gate"), so the gate computes a
   number nobody consumes. ⚠️ Lives in `.github/workflows/sdap-ci.yml`, a **hot path owned by
   `ci-cd-unit-test-remediation-r1`** — coordinate, do not edit unilaterally.
4. Only then consider a larger budget, and treat it as a band-aid.

## Meta-lesson

This gate has now defeated two "obvious" fixes (#908's `testTimeout`, #916's `asyncUtilTimeout`). Both
looked right, both were reasoned from a real mechanism, and neither moved the symptom. **A local pass is
not evidence here** — the machine is 8x the runner. Any candidate fix must be validated by watching the
gate on an actual CI run, not by a green local suite.

---

# UPDATE 2026-08-31 (2) — third hypothesis also WRONG, and the timing story collapses

**`--maxWorkers=1` (PR #917) changed nothing.** Serial: 19 failed / 288s. Parallel: 19 failed / 294s.
CPU contention between workers is **not** the cause. Both #916 and #917 are now **REVERTED** — neither
fixed anything and both cost CI time.

## The number that reframes this

19 failures × 15s budget ≈ **285s**, and the whole suite took **288s**. So essentially *all* the runtime
is failing waits, and the **5 passing tests mount the editor almost instantly**.

That is not a slow machine. If it were slowness, all 24 would be slow and near the budget. Instead the
split is binary: **5 mount immediately, 19 never mount at all**, no matter how long they wait (5s → 15s
moved exactly one test).

**So this was never a timing problem, and three timing-shaped fixes were all treating the symptom:**
#908 (`testTimeout`), #916 (`asyncUtilTimeout`), #917 (`maxWorkers`).

## Where to look next (untested — do NOT assume, validate on the gate)

Something makes the editor mount for 5 tests and never for the other 19, in CI but not locally. That
shape points at **conditional state, not resources**:

- Test-order / shared-state pollution *within the file* — locally all 24 pass, so ordering or a
  module-level mock may differ under `--ci`. Compare which 5 pass in CI against local ordering.
- A module-resolution difference that only bites some code paths (jest.config.js's dist-vs-src note is
  the known trap here, and `--ci` disables the watch-mode cache).
- Something environment-gated inside `ComposeEditor` mount (a feature flag, `matchMedia`, a timer, an
  observer stub) that is absent or behaves differently on the runner.

**First diagnostic** (cheap, high information): get the CI log to print WHICH 5 tests pass. If they are
the first N in file order, it is state pollution after test N. If they are scattered, it is per-test
input.

## Meta

Three fixes, three wrong causes, all of them plausible and all validated only on a 32-core dev box where
the suite passes unconditionally. **A local pass proves nothing about this gate.** The next attempt
should start by making the CI log say *which* tests pass, not by changing a number.
