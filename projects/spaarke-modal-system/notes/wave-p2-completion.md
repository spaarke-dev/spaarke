# Wave P2 Completion — Tasks 040 + 041 + 042 + Main-Session Consolidation (2026-08-02)

**Result: ✅ all three tasks complete** (3 parallel Sonnet agents, FULL rigor each) **+ a main-session preset-consolidation pass** closing the cross-agent findings before commit.

## Task outcomes
- **040 (confirms re-base)**: `ComposeConflictDialog`, `PinnedMemoryDeleteConfirmation`, both `CloseProjectDialog` copies re-based onto standard confirm chrome (`dismiss="alert"`, danger token class, Cancel-left). CloseProjectDialog's inline button-color anti-pattern REMOVED (design §3.3/§6.5). Deliberate standardizations: Cancel repositioned left; title icons folded to string titles. Zero escalations.
- **041 (ChoiceDialog re-base)**: now a thin adapter over `ChoiceModal` (48 ins / 134 del); ADR-023 2–4 rich-choice contract preserved with per-behavior evidence; public prop contract intact; 12 new adapter tests. Sole production consumer: SpaarkeAi `FileAttachSessionPrompt` (unchanged). **ChoiceDialog is NOT in pcf-safe.ts — Code-Page-only.**
- **042 (overlay retirement)**: hand-rolled `position:absolute` `ActionConfirmationDialog` DELETED; sole consumer (`SprkChat.tsx` HITL flow) re-routed onto `ConfirmModal`; anchor investigation cleared the escalation trigger (accidental overflow-clipping, not a deliberate in-tree anchor); grep proof: zero overlay patterns remain in `SprkChat/`. 5 new integration tests. Spec success-criterion 5: 1 of 3 overlays retired (ConversationModal → P5, DocumentOperations → P7).

## Main-session consolidation (the orchestrator downstream-filter pass)
The three agents' reports converged on preset gaps that produced 3 verbatim copies of ConfirmModal's danger class + a lost busy-state + a dead `cancelText` prop. Consolidated before commit:
1. **`ConfirmModal.busy`** — disables both buttons + tiny Spinner on Confirm (restores the retired overlay's in-flight parity). Adopted by `SprkChat` (`busy={isConfirmingAction}`, guards kept as defense-in-depth) and `PinnedMemoryDeleteConfirmation`.
2. **`useDangerButtonClassName` export** — single-source danger class for dialogs whose footer shape legitimately exceeds `ConfirmModalProps` (3-way, phase-dependent). Both `CloseProjectDialog` copies now import it; verbatim copies deleted.
3. **`PinnedMemoryDeleteConfirmation` re-pointed onto the literal `ConfirmModal`** (busy made it expressible) — its SprkModal-direct composition + danger copy removed; tests moved to accessible-name queries.
4. **`ChoiceModal.cancelLabel`** (mirrors ConfirmModal) — `ChoiceDialog.cancelText` now actually renders; the 041 no-op test flipped accordingly.
5. **`SprkModal` aria-labelledby** — custom header now wires `id` on the title + `aria-labelledby` on `DialogSurface` (040's reported pre-existing P0 a11y gap; React-16-safe id via module counter, NOT `useId`).

Still legitimately SprkModal-direct (documented in-file): `ComposeConflictDialog` (3-way choice footer), both `CloseProjectDialog` copies (phase-dependent 0–2-button footers) — now with imported (not copied) danger styling.

## Verification (main session, post-consolidation — final)
- UI.Components `tsc` dist build: PASS. Full jest (199 suites / 2506 tests): failures = **the exact pre-existing baseline, 11 suites / 22 tests** — zero P2 regressions. All P2-touched suites green: SprkChat **357/357** (incl. the new 5-test integration suite), SprkModal family + ChoiceDialog scoped 121/121 + 12 new adapter tests + new `busy`/`cancelLabel` preset tests.
- AI.Widgets build + memory suites: PASS (×2, incl. post-re-point). SpaarkeAi full build (React 19 Code-Page proof): PASS. SemanticSearchControl `build:prod` (React 16/17 PCF proof): PASS. LW scoped tsc: 0 errors on `CloseProjectDialog`.
- ADR-021 diff gate: CLEAN (sole hit = a comment describing the ban itself).
- Behavior notes: blocking confirms ignore ESC/backdrop (`alert`); ChoiceDialog gains explicit-dismiss (settled task-005 design decision, strengthens "conscious choice"); interim P1 maximize removed on confirms per preset contracts (the tension flagged in wave-p1 notes — resolved as predicted).
- Baseline correction: `ConversationView.forward` + `.emailInFlow` ARE part of the pre-existing failing set (stash-A/B-proven on HEAD) — earlier tallies that counted 10 were under-counting.

## Test-determinism hardening (post-wave, agent-042 follow-up — the one flake found)
The new `actionConfirmationIntegration.test.tsx` passed standalone but flaked under full-suite parallel-worker load. Two independent root causes, both test-env-only (component code untouched): (1) **jest's default 5s whole-test timeout** — Fluent Dialog portal + full SprkChat tree + userEvent typing exceeded 5s under contention (fix: explicit 30s per-test budgets + 2-char typed message); (2) **React 19 concurrent-scheduler starvation on jsdom's `setTimeout` fallback** (no MessageChannel/setImmediate in this env) — when the mock SSE chain's microtasks landed outside an act block, the `pendingActionEvent` dispatch effect was dropped UNRECOVERABLY (repeated act-flushing empirically disproven, FAIL/PASS/FAIL). Fix is **prevention**: the fetch router parks `/messages` + gate-resolve responses on pending promises released only INSIDE an open `act()` block (`flushPendingWork`), so the entire response→reader→state-batch→effect→dialog cascade flushes deterministically; poll-with-flush helpers retained as the wait mechanism. Verified: **3 consecutive full-suite runs green** (9.7s/9.0s/10.5s), each at the exact baseline. Full evidence trail (disproven alternatives, probe data): `notes/task-042-completion.md` follow-up section. Real browsers have MessageChannel — this scheduler behavior is a jsdom-only test concern.

Per-task detail: `task-040-completion.md` · `task-041-completion.md` · `task-042-completion.md`.
