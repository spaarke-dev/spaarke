# Task 041 — Playwright NFR suite (FR-17 / NFR-01·02·03)

> **Status**: Authored + syntactically valid + typechecks + wired into `npm run test:e2e`. **NOT run green** — no live Power Apps environment reachable in the authoring sandbox → **escalation trigger fired** (per POML `<escalation>` + negative acceptance criterion). Real-environment run required before merge.
> **Date**: 2026-08-17

## What shipped (4 new files + 1 env doc)
- `tests/e2e/pages/smart-todo/SmartTodoPage.ts` — Code Page page object. Does **not** extend `BasePCFPage` (SmartTodo is a Code Page web resource, not a PCF control — POML "adapt, do not literally extend"). Ready-state = toolbar (`aria-label="Smart To Do toolbar"`) + first kanban column visible.
- `tests/e2e/specs/smart-todo/performance.spec.ts` — NFR-02: `Date.now()`-bracketed load time to ready-state, `expect(elapsed).toBeLessThan(3000)` + `test.info().annotations` (mirrors `spe-file-viewer/performance.spec.ts`), plus a P50/P95 pass.
- `tests/e2e/specs/smart-todo/accessibility.spec.ts` — NFR-01: `AxeBuilder().withTags(['wcag2a','wcag2aa','wcag21aa']).analyze()` run **light + dark** (the dark-on-yellow contrast requirement), `expect(violations).toEqual([])`; plus a keyboard-nav test (bounded Tab walk asserting reachability + visible focus + Enter/Space activation + no trap).
- `tests/e2e/specs/smart-todo/orientation.spec.ts` — NFR-03: select card → assert selected → overflow "Layout" flip → assert selection persists → `assertNoLayoutGlitch()` (boundingBox: no zero-size/overlapping columns) → post-flip cross-column drag-drop → assert column membership changed.
- `tests/e2e/config/.env.example` — added `SMART_TODO_URL` (the deployed `sprk_smarttodo` route the specs navigate to).

## Verification performed (no live env needed)
- `npx playwright test smart-todo --list` → **exit 0**, all 3 specs compile/transpile and are discovered. Full suite now **279 tests in 16 files** (was 13).
- **Zero new npm dependencies** — `@playwright/test ^1.55.1` + `@axe-core/playwright ^4.10.2` already present. **Zero new/competing Playwright config** — specs auto-discovered via the existing `testDir` (no `package.json` script change, no second config).
- Assertions are DOM/semantic/geometry (axe violations, `toBeFocused`, `boundingBox`, column membership) — **no pixel-diff snapshots** (ADR-021 constraint met).

## ⚠️ Two selector assumptions to confirm on the real-env run
Both are implemented defensively and flagged in-code:
1. **`SmartTodoPage.selectCard()`** — assumes a card **click** sets `aria-selected="true"` / `[data-selected]`. If the deployed kanban uses an explicit checkbox instead, switch to clicking the card's checkbox.
2. **`SmartTodoPage.setColorScheme()`** — uses `page.emulateMedia({colorScheme})` (works if the app honors `prefers-color-scheme`) **and** sets a best-effort `localStorage['spaarke.theme.preference']` key. Confirm which mechanism the deployed page actually reads (themeStorage key name/value), then keep the one that works.

## Escalation (CLAUDE.md §6 / POML trigger)
🔔 **Human input / follow-up required** — the suite is authored, valid, and wired, but has **not** been executed against a live SmartTodo Code Page (no reachable Power Apps env + interactive auth in this execution context). Before merge/close, run it against a real environment:

```
# set SMART_TODO_URL (+ POWER_APPS_URL/auth) in tests/e2e/config/.env, then:
npx playwright install            # browsers, once
npm run test:e2e -- smart-todo    # or: npx playwright test smart-todo --project=edge
```

Confirm the two selector assumptions above, then the four NFR criteria assert green. This mirrors spec FR-20/NFR-06 (real-DV verification, not mock-only confidence) — do not mark the task green on the authored-only state.
