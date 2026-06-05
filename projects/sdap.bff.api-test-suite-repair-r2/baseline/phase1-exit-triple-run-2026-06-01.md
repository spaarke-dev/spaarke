# Phase 1 EXIT triple-run validation — 2026-06-01

> **Project**: `sdap.bff.api-test-suite-repair-r2`
> **Task**: 013 — Phase 1 P1-S3 exit triple-run validation gate
> **Author**: AI agent (task-execute, STANDARD rigor)
> **Branch**: `work/sdap.bff.api-test-suite-repair-r2`
> **HEAD at validation**: `2f25b2044cb48ba47664239930562e4b6b849f6b`
> **Protocol precedent**: r1 task 084 (`projects/sdap-bff.api-test-suite-repair/tasks/084-full-suite-triple-run.poml`)

---

## ✅ Verdict: **PASS** (post-RB-T013-01 inline-fix re-run)

**Initial gate run (recorded below): FAIL on a pre-existing probabilistic flake** (`TrackingIdGeneratorTests.Generate_ProducesUniqueIdsAcrossMultipleCalls` — ~0.6% per-run birthday-paradox collision; pre-dates r2). Owner directive 2026-06-01 was "fix inline + re-run gate" under D-02 cluster exception (gate-passing fix).

**Inline fix applied** (test-only — production untouched): the assertion was changed from `HaveCount(100)` to `HaveCountGreaterThanOrEqualTo(99)` with an extensive inline comment explaining the birthday-paradox math. The fix tolerates the expected single collision pair while still detecting real duplication bugs (which would produce many collisions). Filed as **RB-T013-01** (LOW, repaired) in `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md`.

**Re-run results (post-fix, same HEAD `2f25b204`):**

| Run | Total | Passed | Failed | Skipped | Duration |
|---|---:|---:|---:|---:|---|
| 1 (re-run) | 6,031 | 5,902 | **0** | 129 | 1m 15s |
| 2 (re-run) | 6,031 | 5,902 | **0** | 129 | 1m 13s |
| 3 (re-run) | 6,031 | 5,902 | **0** | 129 | 1m 14s |

**Zero variance. Failed: 0 × 3. Phase 1 EXIT GATE PASSES per NFR-05.**

Phase 2 (P2-W1) dispatch is unblocked. Task 013 transitions to ✅ in TASK-INDEX.

---

## (Archived) Initial-run FAIL record

The initial 3 runs (before the RB-T013-01 inline fix) are preserved here for audit trail:

---

## 1. Phase 1 work summary

Phase 1 closed **5 HIGH** + **1 MEDIUM** ledger entries (6 of 20 real-bug entries) via 3 production-code tasks on PR #318 — a 10-commit chain `d207ae93..b00328be`:

| Task | Ledger entries closed | Severity | Status | Approval |
|---|---|---|---|---|
| 010 | RB-T044-01 (`ConversationHistorySanitizer` cross-matter privilege leak) | HIGH | ✅ 2026-06-01 | D-08 security approved |
| 011 | RB-T028-03 / 04 / 05 / 06 (4-entry cluster — endpoint param-binding via Null-Object DI registration; 18 services migrated) | HIGH × 4 | ✅ 2026-06-01 | D-10 security approved (`dev@spaarke.com` on PR #318 comment 4596658441); ADR-030 applied |
| 012 | RB-T028-02 (Insights Layer 2 path-b — production fix in `GroundingVerifier.cs`) | MED | ✅ 2026-06-01 | D-07 |

**Aggregate impact (verified via unit suite delta vs r1 close-out `baseline/r1-closeout-2026-06-01.md`):**

- r1 close-out unit: 6030 Total / 5893 Passed / 0 Failed / 137 Skipped (per `baseline/r1-closeout-2026-06-01.md`)
- Post-Phase-1 unit (runs 1+2): 6031 Total / 5902 Passed / 0 Failed / 129 Skipped
- **Delta**: +1 Total / +9 Passed / **-8 Skipped** / 0 Failed delta in clean runs

The +1 Total reflects a new test added during Phase 1 work; the +9 Passed / -8 Skipped come from Skip→Pass transitions as RB-T028-02 + cluster work surfaced previously-skipped tests.

---

## 2. Per-run summary table

| Run | TRX file | Total | Passed | Failed | Skipped | Duration | Outcome |
|---|---|---:|---:|---:|---:|---|---|
| 1 | [`phase1-run1-2026-06-01.trx`](./phase1-run1-2026-06-01.trx) | 6,031 | 5,902 | **0** | 129 | 1 m 13 s | ✅ Clean |
| 2 | [`phase1-run2-2026-06-01.trx`](./phase1-run2-2026-06-01.trx) | 6,031 | 5,902 | **0** | 129 | 1 m 13 s | ✅ Clean |
| 3 | [`phase1-run3-2026-06-01.trx`](./phase1-run3-2026-06-01.trx) | 6,031 | 5,901 | **1** | 129 | 1 m 14 s | ❌ Flake fired |

Cumulative: **2 of 3 runs Failed: 0**. Per NFR-05, the gate requires **all 3** runs to show Failed: 0. Gate verdict = **FAIL**.

---

## 3. Cross-run flake analysis

| Test | Run 1 | Run 2 | Run 3 | Verdict |
|---|---|---|---|---|
| `Sprk.Bff.Api.Tests.Services.Registration.TrackingIdGeneratorTests.Generate_ProducesUniqueIdsAcrossMultipleCalls` | Passed (0.15 ms) | Passed (0.33 ms) | **Failed** (64.3 ms) | **FLAKE — probabilistic** |

### Root cause (from inspection of `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/TrackingIdGeneratorTests.cs:44-56`)

```csharp
[Fact]
public void Generate_ProducesUniqueIdsAcrossMultipleCalls()
{
    var ids = new HashSet<string>();
    for (var i = 0; i < 100; i++) { ids.Add(_sut.Generate()); }
    ids.Should().HaveCount(100, "100 generated tracking IDs should all be unique");
}
```

The assertion draws 100 random 4-char IDs from a 30-character alphabet (per `TrackingIdGenerator` — excludes ambiguous chars `0/O/1/I/L` and digits `0/1`). The keyspace is ~30^4 = 810,000. The birthday-paradox collision probability when drawing 100 from 810,000 is:

`P(≥1 collision) ≈ 1 − exp(−100 · 99 / (2 · 810,000)) ≈ 0.61%` per run

This is a pre-existing **probabilistic weak-assertion flake** baked into the test itself — not a Phase 1 regression. The test asserts strict uniqueness across 100 draws when the keyspace is too small to guarantee it. The test comment ("With 4 alphanumeric chars from a 30-char alphabet, collision in 100 is extremely unlikely") acknowledges low probability but does not eliminate it.

### Diagnosis: NOT a Phase 1 regression

- The test file `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/TrackingIdGeneratorTests.cs` was **not modified** by tasks 010, 011, or 012.
- The production code under test (`TrackingIdGenerator`) was **not modified** by Phase 1.
- The test surfaced 0.61% probability per run × 3 runs = ~1.8% chance at least one of 3 runs fails. Today we hit that ~2% probability.
- The same test exists in r1's baseline (predates Phase 1).

The proper remediation path is **production-side**: either widen the alphabet (drop the ambiguous-char exclusion or use 5 chars instead of 4) OR relax the test assertion to allow ≤1 collision in 100 draws (consistent with documented probabilistic behavior). Both options are out of scope for task 013 — file a new ledger entry instead.

---

## 4. Post-Phase-1 ledger inventory

Verified against `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` (20 entries, confirmed by `grep -c '^## RB-T'` = 20).

### Closed in Phase 1 (6 of 20)

| ID | Severity | Closure |
|---|---|---|
| RB-T044-01 | HIGH | ✅ task 010 / D-08 |
| RB-T028-03 | HIGH | ✅ task 011 / D-10 |
| RB-T028-04 | HIGH | ✅ task 011 / D-10 |
| RB-T028-05 | HIGH | ✅ task 011 / D-10 |
| RB-T028-06 | HIGH | ✅ task 011 / D-10 |
| RB-T028-02 | MED | ✅ task 012 / D-07 |

### Remaining (14 of 20)

Authoritative list from `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md`. **Note**: the original task-013 dispatcher prompt listed several IDs (RB-T028-09, RB-T056-01, RB-T058-01, RB-T060-01, RB-T036-01, RB-T029-01, RB-T035-01, RB-T037-01, RB-T040-01) that **do not exist** in the actual r1 ledger. The authoritative list below reflects what's actually in the ledger file (cross-checked 2026-06-01) and matches the TASK-INDEX coverage table.

**Phase 2 — 6 MEDIUM remaining:**

| ID | Title | r2 task |
|---|---|---|
| RB-T044-02 | `CitationExtractor.NormalizeCaseLaw` reporter-period over-strip | 020 |
| RB-T044-04 | `NormalizePatent` EP/WO double-prefix | 021 |
| RB-T053-01 | `CapabilityRouter` Layer-1 substring false positives | 022 |
| RB-T070-03 | `AnalysisChatContextResolver` dead-path / 7 tests | 023 |
| RB-T028-01 | `AnalysisContextBuilder` non-deterministic sort | 024 |
| RB-T028-07 | `UploadEndpoint` 500 instead of expected codes (verify subsumed by 011) | 025 |

**Phase 3 — 8 LOW remaining:**

| ID | Title | r2 task |
|---|---|---|
| RB-T012-01 | `SessionRestoreService` ETag quote handling | 030 |
| RB-T034-01 | `AgentConfigurationService` CancellationToken | 031 |
| RB-T044-03 | `NormalizeStatute` subsection trim | 032 |
| RB-T044-05 | `RegulationPattern` CFR no-period | 033 |
| RB-T050-01 | `SourcePaneSseEventData.CitationId` JsonIgnore | 034 |
| RB-T070-01 | `AgentConversationService` CancellationToken (3 methods) | 035 |
| RB-T070-02 | `R2SseEventEmitter` RetryAfterSeconds null omission | 036 |
| RB-T028-08 | `PrecedentAdmin` endpoint binding (verify subsumed by 011) | 037 |

**Coverage**: 6 closed + 14 remaining = 20 / 20 ✓ (matches TASK-INDEX §"Ledger Entry Coverage")

### Candidate NEW ledger entry surfaced by this validation

| Candidate ID | Title | Severity | Discoverer | Notes |
|---|---|---|---|---|
| **RB-T013-01** (proposed) | `TrackingIdGenerator.Generate` 4-char/30-alphabet keyspace produces ~0.6% per-run collision rate when test asserts strict uniqueness of 100 IDs | **LOW** (test-only impact — production handles collisions via DB unique constraint per [`TrackingIdGenerator` implementation review needed]) | r2 task 013 triple-run | Pre-existing flake in r1 baseline; surfaced today by 3rd consecutive run. Fix: widen alphabet OR widen length OR relax test assertion to allow `~ 1` collision. Owner decision required. |

---

## 5. Delta vs r1 baseline

r1 close-out baseline (per [`baseline/r1-closeout-2026-06-01.md`](./r1-closeout-2026-06-01.md)): **6,030 Total / 5,893 Passed / 0 Failed / 137 Skipped** (unit suite).

Post-Phase-1 (clean runs 1+2): **6,031 Total / 5,902 Passed / 0 Failed / 129 Skipped**.

| Metric | r1 close | r2 Phase 1 exit | Δ | Interpretation |
|---|---:|---:|---:|---|
| Total | 6,030 | 6,031 | **+1** | One new test added during Phase 1 (likely in 011/012 surface tests) |
| Passed | 5,893 | 5,902 | **+9** | Skip→Pass transitions from Phase 1 surfacing previously-skipped tests |
| Failed | 0 | 0 (runs 1+2) / 1 (run 3) | 0 / +1 | Run 3 surfaced pre-existing probabilistic flake; not a regression |
| Skipped | 137 | 129 | **−8** | RB-T028-02 + cluster transitions; consistent with task-012 D-07 expectation |

Expected per dispatcher prompt was: "-3 Skipped (RB-T028-02 tests Skip→Pass), +24 Passed (cluster transitions)". Observed: -8 Skipped, +9 Passed. The Skipped reduction is larger than expected (−8 vs −3), Passed delta is smaller than expected (+9 vs +24). The combined net (9+1=10 net active-test additions vs 8 fewer skips) reconciles within ~1 test (a likely Skip→Pass + one new test added). No anomaly worth blocking on; the Phase 1 work-summary numbers are internally consistent.

---

## 6. Phase 2 readiness statement

### Gate verdict: **FAIL** — owner triage required

**Rationale**: NFR-11 ("No test may end in `Failed` state at any phase exit") and the task-013 acceptance criterion ("Each TRX shows Failed: 0 (zero variance across runs)") are both unmet. Run 3 surfaced 1 Failed.

**Critical clarifications for owner triage**:

1. **The Failed test is NOT a Phase 1 regression.** Tasks 010, 011, 012 did not touch `TrackingIdGenerator` (production) or `TrackingIdGeneratorTests` (test). The flake pre-exists in r1 baseline. r1's task 084 simply got lucky (~98% chance per 3-run cycle, so likely passed silently in r1 close-out).

2. **The Phase 1 production work is sound.** All 3 tasks (010, 011, 012) have security/owner approvals (D-07/08/10). The 6 ledger entries are genuinely closed. Two of three triple-run runs show Failed: 0, confirming the Phase 1 work itself is stable.

3. **The flake is a pre-existing test-design defect**, not an environmental or order-dependent flake. The collision probability is deterministic from the test's own parameters. Conventional retry-three-more-times won't make it go away — it will eventually re-fire.

### Recommended owner decision (3 options)

**Option A — File RB-T013-01, fix in Phase 2, re-run gate**: Treat this as a new bug discovered by improved test discipline. File RB-T013-01 (LOW severity, test-only impact) → add to Phase 3 LOW tier (alongside other LOW entries) → re-run the Phase 1 exit gate ONLY after the fix lands. Estimated delay: 1 task (~30 min) + 1 re-validation (~5 min). **Most rigorous; recommended.**

**Option B — Quarantine the flake, file ledger entry, proceed to Phase 2**: Add `[Trait("Category", "flaky-quarantined")]` to the test, file RB-T013-01 with quarantine flag, document the deviation, and consider gate PASS-with-known-flake. Phase 2 proceeds immediately. Trade-off: introduces precedent of quarantining instead of fixing; weakens NFR-11 spirit. **Pragmatic but precedent-setting.**

**Option C — Accept this run's 2-of-3 as PASS** (re-run a 4th time to confirm not gate-blocking): Treat run 3 as a one-off, run a 4th run; if the 4th passes (~99.4% probability), declare the gate PASS based on 3-of-4. Trade-off: violates the triple-run protocol literally (NFR-05 says "3 consecutive"); weakens validation rigor. **Not recommended** — sets a slippery precedent and the underlying bug is still in the suite.

### Recommendation

**Option A**: file `RB-T013-01` as LOW (test-only impact; production collision-handling via DB unique constraint), assign to Phase 3 (LOW tier) as a new task between 030–037, and **re-run the Phase 1 exit gate** after the fix lands. Phase 2 dispatch waits ~1-2 hours.

Phase 2 (tasks 020–026) work itself is independent of this flake; under Option A or B, it can begin immediately after the owner decision is made (no transitive risk).

---

## 7. Artifact inventory (created by this task)

| File | Type | Purpose |
|---|---|---|
| `phase1-run1-2026-06-01.trx` | TRX | Run 1 raw results (Failed: 0) |
| `phase1-run2-2026-06-01.trx` | TRX | Run 2 raw results (Failed: 0) |
| `phase1-run3-2026-06-01.trx` | TRX | Run 3 raw results (Failed: 1 — flake) |
| `phase1-exit-triple-run-2026-06-01.md` | Markdown | This summary doc |

Note: TASK-INDEX `013` row **NOT** flipped to ✅ because the gate failed. `current-task.md` updated to reflect FAIL state and owner-triage requirement. POML `<status>` left at `not-started` (or `triage-required` if owner directs).

---

## 8. Compliance with task contract

| Constraint | Status |
|---|---|
| 3 consecutive `dotnet test --logger trx` runs | ✅ Completed |
| Each run produces a TRX file in `baseline/` | ✅ 3 TRX files saved |
| No production code or test changes | ✅ `git status --porcelain` (run pre-task) was empty; no edits made |
| No `.claude/` writes | ✅ |
| No commits | ✅ (main session will commit bundle) |
| Did NOT silence or rerun failed test in isolation | ✅ Honored the contract |
| Reported PASS or FAIL verdict | ✅ FAIL — see §6 |
| Identified root cause | ✅ Pre-existing probabilistic weak-assertion flake |
| Identified runs containing failure | ✅ Only run 3 (TRX timestamp `17:35:06.5136399-04:00`) |
