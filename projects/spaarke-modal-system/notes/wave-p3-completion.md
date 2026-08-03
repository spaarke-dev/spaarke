# Wave P3 Completion — Task 050 ✅ + Task 051 ⏸️ Deferred (2026-08-02)

**Result: 050 complete; 051 properly escalated → deferred with interim mitigation.** 2 parallel Sonnet agents (FULL rigor); main-session preset pre-extension + post-wave consolidation.

## Pre-wave consolidation (main session, applying the P2 lesson proactively)
`FormModal` was extended BEFORE dispatch so real forms fit the literal preset: `submitDisabled` (validation-gated Save), `busy` (both disabled + Spinner on Save, mirrors ConfirmModal), `cancelLabel`, `dismiss?: 'explicit'|'alert'` (default explicit; alert for active-compose per FR-14/FR-05). +1 combined preset test (8/8).

## 050 (forms → FormModal/md) — ✅
- `NewThreadModal` (UI.Components): literal FormModal, `busy={submitting}`, uiScale passthrough prop.
- `PinnedMemoryEditDialog` (AI.Widgets): literal FormModal; `busy={isSubmitting}` wired by main session after the dist rebuild (the agent was correctly blocked by the stale dist type boundary); guards kept as defense-in-depth.
- `QuickStartModal` (SpaarkeAi): literal FormModal + `useUiScale()` threaded (the task-020 seam's first real consumer); primary relabeled "Close" (card picker — no submit concept; documented in-file).
- ADR-028 function-passing preserved (paths cited in task notes); `md` fit all three — the owner-locked-size escalation never fired.
- Collateral fix: 2 widget-suite testid queries (P2 re-point fallout) moved to accessible-name queries.

## 051 (EmailComposer re-base + legacy retirement) — ⏸️ DEFERRED (Issue #713 / DEF-002)
Agent escalated per the POML trigger with ZERO source changes — the correct outcome:
1. **Re-base blocked**: `EmailComposer mount="dialog"` is SELF-CHROMED (own header incl. ModalWindowControls + own `ComposerActionBar` footer; no suppression prop; `inline` nulls the action bar; forking forbidden). Any SprkModal-based wrap double-renders chrome.
2. **Retirement blocked**: two live legacy consumers — `pcf-safe.ts:27-28` deep-export (grep: zero actual PCF imports; dead surface) and `DailyBriefingApp.tsx` on the legacy `onSend` API with two bespoke send flows that don't map to the canonical engine.

**Orchestrator disposition** (autonomous, documented): migrating DailyBriefing / adding chrome-suppression = scope beyond this project on surfaces owned by adjacent active worktrees → two-write deferral (Issue #713 + DEF-002). **Interim shipped**: `maxHeight: 720px` on the wrapper → numerically identical to `md` (it already had `modalType="alert"` + ModalWindowControls) — **FR-14 satisfied in substance**; literal preset composition rides the follow-on. The P1 "v1.1.59 no-X" escalation on the legacy dialog remains OPEN (tracked in defer-issues.md Decision-pending).

## Verification (main session)
- ADR-021 diff gate: CLEAN (414 added lines).
- UI.Components: dist rebuilt (tsc clean ×2); full suite at the pre-existing baseline (figures in commit message).
- AI.Widgets: build + full suite (busy wiring + testid fix verified).
- SpaarkeAi: full build green + scoped tests (agent-verified during 050; unchanged since).
- LegalWorkspace untouched this wave.

Per-task detail: `task-050-completion.md` · `task-051-completion.md`.
