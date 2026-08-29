# Current Task State — ci-cd-unit-test-remediation-r1

> **Last Updated**: 2026-08-28 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. Full narrative + traps: `C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke\memory\session-resume-2026-08-27-ci-remediation.md`
> **Synopsis** (objectives / scope / end state / how CI runs after): <https://claude.ai/code/artifact/7ca88795-5790-43f3-8432-8d84bd3fdec0>

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active task** | none — 094 done, PR **#890** open |
| **Project state** | 47 tasks · 34 complete · 1 open · 7 closed · 2 partial |
| **Status** | in-progress — **all defect tasks closed; only the cutover chain remains** |
| **Next Action** | **1.** Merge PR **#890** when green. **2.** Then WAIT — the cutover chain is gated on the shadow window, and the window is now gated on **calendar days**, not throughput. Nothing else is startable. |

### Critical context

**The shadow window is now gated on CALENDAR DAYS, not PRs.** It moved 7 → **15/20 agreeing** in a
single session, but the day span is **1 of 5**. The 20-PR bar will be met well before the 5-day bar.
Check with `pwsh scripts/ci/shadow-window-status.ps1`. **0 false greens throughout.**

**Task 094 is the only substantive work left, and it is FREEZE-EXPOSED** — arming more ADR-038 bans
means editing `ci-tier1-blocking.yml`. PR #865 set the precedent: bans at **zero live instances** are
verdict-neutral and were armed under the banner's own "guard present but not armed" carve-out. Shape
for 094: measure all 15 remaining bans with the hardened classifier, arm the zero-count ones under
that precedent, document the rest with live counts.

**Do not `--delete-branch` a PR that is another PR's base** — it auto-closes the stacked PR
irrecoverably. That killed #856 earlier (recovered as #857).

---

## Shipped this session

| PR | Task | Result |
|---|---|---|
| #843 #847 #865 #866 #867 | (queue from prior session) | all merged; ArchTests now **136/136** on master |
| **#884** | 091 timing tests | 4/5 fixed; 5th escalated (below). Scheduling suite 5m14s → 5s |
| **#885** | 093 link validator | corpus 100,220 → 922 files; broken 1,212 → **267** (all verified real) |
| **#886** | 095 client jest | new `client-tests.yml`; 730 test files / 40 packages now run at all |
| **#888** | 092 prettier | root cause `endOfLine: crlf`; CI flagged 1,907 of 1,911 on line endings alone |
| **#889** | test-signal hygiene | **OPEN at handoff** — registry exit rule + un-skip 4 |

---

## Open work

### 1. ~~Task 094~~ — DONE (PR #890)

All 17 bans accounted for: **5 armed** (B1/B4 prior + **B3/B12/B16**), 12 documented-unenforceable
with live counts. Migration cost 4 test methods in 2 files. ArchTests **136 → 139**.
Census: `notes/094-adr038-ban-census.md`; annex added to ADR-038 §7.

**The one thing worth carrying forward**: **B8 is the next arming pass** — 7 call sites in 5 files
invoking private production methods by reflection. It is the only unarmed ban with both a tight
signature and a bounded count; it stayed unarmed because the migration is a per-call-site
production-visibility decision, not a mechanical sweep.

Also confirmed, so nobody re-derives it: **B2 and B17 name types this repo does not contain** —
there is no `IServiceClient` (3 grep hits, all prose) and AutoMapper is not a dependency (0 refs).
Arming either would be guard-theater.

### 2. The cutover chain — gated on the window

`071 cutover → 075 soak (7d) → 077 retire sdap-ci.yml → 076 (30d) → 090 wrapup`
(076 depends on 071, not 077.)

### 3. Escalated, deliberately not fixed

- **091's 5th failure** — `ReAnalysisFlowTests` is NOT a timing test. Reproduces deterministically;
  fails on HttpClient's 100s timeout while making a **live Azure Search call**. It surfaces in a job
  named *Full Unit Tests* because tier2 pass 1 is bare `dotnet test` with **no project filter**.
  Fixing it is a tier-file edit (frozen) **and** an owner call, since filtering integration tests out
  of Tier 2 means they run nowhere on a PR. Details: `notes/091-realclock-findings.md`.
- **092's job-output criterion** — printing the repro command needs a tier-file edit. The command
  lives in `docs/procedures/testing-and-code-quality.md` instead. One-liner, deferred past cutover.

---

## Test-suite improvement review (owner-approved subset)

Assessment: `notes/test-suite-improvement-assessment.md`. Ground-truth pass resolved every
`<CONFIRM>` in `notes/how-to-improve-the-Spaarke-test-suite.md`.

**Implemented in #889** — A (registry exit rule + 3 stale entries removed), B (un-skip 4 of 10),
C (`/test-diet` touch-radius + registry lookup), D (test-scope clause in `task-create`), plus
governance in `constraints/testing.md` §2a/§2b.

**Deferred by owner decision — do not re-propose without new information**: mutation testing
(conflicts with ADR-038 "coverage is observation, never a gate"), fixed-quota rotation + test-debt
ledger (standing overhead vs north star #4), auto-quarantine (would grow a graveyard that already
has **143** `Skip=` + **137** `repaired` residents and no drain).

**Continuing backlog**: **6** skipped tests remain in `Spaarke.Scheduling.Tests`. The virtual-clock
conversion pattern is proven — see `AdvanceUntilAsync` / `VirtualClockOptions` in
`ScheduledJobHostTests.cs`.

---

## Decisions made this session (do not re-litigate)

1. **092's fix is `endOfLine: "auto"`, NOT a `.gitattributes` rule.** The `*.cs text eol=crlf`
   precedent exists to BACK an already-declared `.editorconfig` policy; nothing declares one for
   TypeScript, so a gitattributes rule would invent a policy and force CRLF working trees on CI and
   every non-Windows contributor. **Do not "fix" `endOfLine` back to `crlf` or `lf`** — either value
   re-breaks one of the two platforms.
2. **095 does NOT run on `pull_request`, deliberately** — 40 packages × `npm install` would contend
   with the very PRs the shadow window needs. Adding it is Phase 2 promotion, not an oversight.
3. **093 kept `knowledge/**` in the validator corpus** despite 67 findings — actively curated
   (`REFRESH-PROCEDURE.md`), and root CLAUDE.md §15 has the researcher consult it first.
4. **Registry membership is a statement about a test as it exists today**, not a permanent label
   (`constraints/testing.md` §2a).

---

## Traps worth carrying forward

- **`TickAsync` refreshes BEFORE the due-check** and recomputes `NextFireUtc` from `now`
  **exclusive**. Under jitter-free virtual time, a refresh interval that divides the cron period
  starves dispatch **forever**. `VirtualClockOptions` uses 30s;
  `RefreshTick_PicksUpDefinitionAddedAtRuntime` needs a short-but-**non-divisor** 700ms because the
  refresh tick is the thing it tests.
- **`TriggerNowAsync` passes `CancellationToken.None`** while its own comment claims the host
  stopping token — manual-trigger jobs cannot observe shutdown. Latent NFR-07 gap, no coverage.
  Reported, not fixed (out of scope).
- **`--delete-branch` has a SECOND failure mode.** Beyond auto-closing a stacked PR (which killed
  #856), deleting the branch while any job is still **queued** fails that job at **Checkout** — it
  fetches a ref that no longer exists. Hit on #890: legacy sdap-ci "Code Quality" started 24s after
  the merge and went red with no quality check having run. Wait for every check to reach a TERMINAL
  state, not merely for the fail count to read 0 while others pend.
- **That red cannot contaminate the shadow window**, by construction: `shadow-window-status.ps1`
  compares `$pr.mergeCommit.oid` — POST-MERGE master runs only — so PR-branch infra failures are
  never consulted. Worth knowing precisely because "sdap-ci red + tiers green" is the exact shape of
  a disqualifying false green.
- **A trivial fixture can falsely refute a true hypothesis.** The 092 line-ending theory looked
  disproven by a two-line file; inverting the setting against the real 1,911-file corpus proved it.
  Prefer inverting a setting over constructing a minimal repro when the corpus is the variable.
