# Current Task — Spaarke Compose Legal Fidelity R4.5

> Tracks only the **active** task. History is in `tasks/TASK-INDEX.md` and per-task `.poml` files.

## Active Task

**Status**: in-progress (autonomous parallel execution)
**Mode**: autonomous, parallel-where-possible; `/conflict-check` before every BFF PR.

### Completed (11/21)
- Phase 0: **001** fixtures ✅ · **002** harness ✅
- WS-5: **050** ✅ · **051** ✅ · **052** ✅ (DEFER pagination to fast-follow; 2 licensing sign-offs pending @ fast-follow)
- **WS-1 CODE COMPLETE**: **010** upload→projection ✅ · **011** browse `/project` endpoint ✅ · **012** transient (FR-02 already met; guards) ✅ · **013** delete mammoth ✅
- **WS-1 boundary gate ✅ PASS** (0 Critical; DEF-01 pre-existing test → 031; DEF-02 payload-size hardening — see `notes/defer-issues.md`).

### Deploys queued for HUMAN
- **014** (WS-1 deploy+UAT) + **034** (WS-3 deploy+UAT) — batched; need Compose deploy to shared `sprk_spaarkeai` + manual UAT. Code proceeds without them (020 deps 013, not 014).

### Completed (18/22 incl. added 035)
- Phase 0: 001, 002 ✅ · WS-5: 050, 051, 052 ✅ (DEFER)
- WS-1: 010–013 ✅ (gate PASS) · WS-2: 020–022 ✅ (gate PASS; 5 silent drops fixed)
- **WS-3: 030, 031, 032, 033, 035 ✅ (gate PASS)** — flagship NFR-02 green (24/24 golden = Word); **DEF-03** (numId counter bug caught by round-trip) fixed via task 035; full Compose suite **694 pass / 0 skip / 0 fail**.

### In progress / next
- **040** WS-4 projection reference fields (computedNumber already present from 031; add numberingLevel/listPath/headingLevel) — 🔄
- Then 041 (persist paraId→number in payload + session ledger), **042** (opus citation resolver — single/sub-item/range; has the citation-CONTRACT unresolved-question → may escalate), wrap-up 090.

### STOP-for-human
- Queued deploys 014 + 034 (batched). · 042 citation-contract question (spec Unresolved Q). · 052 licensing sign-offs (fast-follow only).

### Open defer-issues (notes/defer-issues.md)
- DEF-01 advisoryComments (pre-existing; WS-3 031 domain — still red, revisit). · DEF-02 payload-size hardening. · DEF-03 ✅ RESOLVED (task 035).

## Project state

- Pipeline complete: README, spec, plan, CLAUDE.md, tasks/, TASK-INDEX.md generated 2026-07-28.
- Branch `work/spaarkeai-compose-fidelity-r4.5` — synced with origin, 0 behind master (R4 merged).
- Baseline BFF build: **green** (0 errors, 23 pre-existing warnings).

## Steps completed
_(none — no active task)_

## Files modified this task
_(none)_

## Decisions / deviations
_(none)_

## Reminders
- **`parallel-safe: false` on all `Services/Compose/` tasks** — run `/conflict-check` before every BFF PR.
- Consume `Services/Ai/PublicContracts/` — no fork of `Services/Ai/`.
- Publish size ≤60 MB; WS-1..WS-4 ~0 delta; WS-5 sidecar out-of-publish.
- Line numbers cited in spec/design may have shifted after today's master merge — re-grep before editing.
