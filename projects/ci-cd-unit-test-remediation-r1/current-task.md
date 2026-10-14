# Current Task State — ci-cd-unit-test-remediation-r1

> **Last Updated**: 2026-10-14 (tasks 075 + 077 complete, worktree `C:\wtci`, branch `work/ci-retire-sdap-ci`)
> **Synopsis** (objectives / scope / end state): <https://claude.ai/code/artifact/7ca88795-5790-43f3-8432-8d84bd3fdec0>

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active task** | none — **076** (30-day SC measurements) is next, gated at cutover+30d = **2026-10-29** |
| **Status** | 075 complete (owner ruling, soak passed). 077 complete: `sdap-ci.yml` retired on branch `work/ci-retire-sdap-ci` (PR "ci: retire legacy sdap-ci.yml (CI remediation tasks 075 + 077)"). The PR must merge **after #1837** (it uses `.github/actions/quality-alert`). |
| **Next Action** | Merge master into the branch once #1837 lands and confirm `CI Standalone Checks` goes green; apply the `.claude/` edits listed in the PR body (main session). On/after 2026-10-29 run 076. |

### Critical context

- **Retirement record**: `notes/sdap-ci-retirement.md` (job mapping, alert wiring, replay table, deploy-promote note).
- **New workflows**: `ci-standalone-checks.yml` (dependency audit, Release build + `deployment-packages`, Compose
  client gate, client typecheck; not required) and `master-ci-alert.yml` (`router-master`,
  `tier2-master/full-unit-tests` quality alerts; decision logic `scripts/ci/master-ci-verdict.ps1`).
- **deploy-promote.yml** now downloads from `ci-standalone-checks.yml` (separate commit; spec MUST "untouched"
  flagged to the owner). Recommended follow-up: `branch: master` on its three download steps.
- **Required checks**: `Router` AND `Client lint (type-aware ratchet)` (ruleset 21824191).
- **Flakes**: Tier 1 `Arch Tests` IMDS flake (#1672, #1784, OPEN) will now raise `router-master` alerts when it hits master.
- **Owner rules still standing**: never edit `ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml`
  (they keep stale sdap-ci comments); `.claude/` edits go to the main session as a list.

### Remaining chain

`076 (30d gate 2026-10-29)` → `090 wrapup`

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
