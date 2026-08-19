# Task 001 — Completion: Absorb PR #508 boundary fix on `Spaarke.SmartTodo.Components`

> **Date**: 2026-08-15 · **FR**: FR-01 (prerequisite) · **Tier**: opus/xhigh · **Outcome**: COMPLETE, build-green.

## What changed (import-path + build-config only, no runtime change)

Rewrote all 4 private-source reach-ins into `../Spaarke.UI.Components/src/...` to the `@spaarke/ui-components` **package barrel**, and wired the package/tsconfig boundary (mirroring the verified `Spaarke.DailyBriefing.Components` reference):

| File | Change |
|---|---|
| `src/types/kanban.ts` | `export type { IKanbanColumn, KanbanOrientation }` now from `@spaarke/ui-components` (+ updated stale rationale comment). |
| `src/components/SmartTodoKanban/SmartTodoKanban.tsx` | `import { KanbanBoard }` now from `@spaarke/ui-components`; fixed a `@see src/client/shared/Spaarke.UI.Components/src/...` doc-comment that would trip the grep gate. |
| `src/widgets/SmartTodoWidget/SmartTodoWidget.tsx` | Merged `OrientationToggle` + `type Orientation` + `MicrosoftToDoIcon` into one `@spaarke/ui-components` barrel import. |
| `package.json` | Added `dependencies: { "@spaarke/ui-components": "file:../Spaarke.UI.Components" }` + `peerDependencies: { "@spaarke/ui-components": "*" }`. |
| `tsconfig.json` | Added `baseUrl: "."` + `paths` mapping `@spaarke/ui-components` / `@spaarke/ui-components/*` → `../Spaarke.UI.Components/dist/...`. |

## Deviation from POML (documented per step 11)

The POML step 2 suggested rewriting to deep subpaths (e.g. `@spaarke/ui-components/components/Kanban/types`) "or the equivalent subpath/barrel the package actually exposes." **Chose the barrel `@spaarke/ui-components` for all 6 symbols** because: (a) all 6 are cleanly re-exported from the barrel via `export *` (`./icons`, `./components/Kanban`, `./components/OrientationToggle`); (b) the verified reference consumer `Spaarke.DailyBriefing.Components/src/components/SubRowTodo.tsx` imports `MicrosoftToDoIcon` from the **barrel** — so barrel is the sanctioned precedent; (c) barrel avoids fragile deep dist-subpath coupling. Escalation trigger did NOT fire (the DailyBriefing/AI.Widgets `paths→dist` precedent applies exactly; `dist` is a gitignored build artifact, confirmed built).

## Verification (acceptance criteria — all met)

1. ✅ `tsc --noEmit` in `Spaarke.SmartTodo.Components` → **exit 0** (after building `Spaarke.UI.Components` `dist` first). No private-source errors from `Spaarke.UI.Components`. (`skipLibCheck: true` in SmartTodo tsconfig is pre-existing.)
2. ✅ Repo grep for `Spaarke.UI.Components/src` scoped to `Spaarke.SmartTodo.Components/src/` → **0 matches**.
3. ✅ `package.json` carries `@spaarke/ui-components` under both `dependencies` + `peerDependencies`.
4. ✅ `tsconfig.json` carries the `@spaarke/ui-components` `paths` mapping.
5. ✅ Negative: only import-statement lines + the 2 config files changed; no prop/export/render change.
6. ✅ Build green.

## Quality gates (Step 9.5 — FULL rigor)

- **code-review**: Critical 0 / Warning 0 / Suggestion 0. Boundary restored; barrel specifiers correct; no behavioral drift; no circular dep.
- **adr-check**: ADR-012 ✅ (this change enforces it); ADR-021 ✅ (no v8/color change). 0 violations.

## Conflict-check

Only open PR on these files is #508 itself (the stale one being superseded). No other active worktree PR overlaps. Landing as its own small shared-lib commit/PR per the 19-worktree contention note. **#508 to be closed as superseded at wrap-up (task 090).**

## Next

Task 002 (13-file Kanban hoist) depends on this clean boundary — it will add NEW `@spaarke/ui-components` barrel imports for the hoisted components, never the relative bypass.
