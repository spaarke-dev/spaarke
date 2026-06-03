# B-2 Decision Record: `@spaarke/ai-widgets` tsc cross-rootDir fix

> **Task**: 061 (B-2)
> **Date**: 2026-05-26
> **Author**: Claude (executed via task-execute, STANDARD rigor)
> **Status**: Decided + applied

---

## Problem

`src/client/shared/Spaarke.AI.Widgets/tsconfig.json` declared `rootDir: "./src"` plus
`paths` entries that mapped sibling packages to **source files** (`.ts`):

```jsonc
"paths": {
  "@spaarke/ai-outputs":     ["../Spaarke.AI.Outputs/src/index.ts"],
  "@spaarke/ui-components":  ["../Spaarke.UI.Components/dist/index.d.ts"],   // ← already correct
  "@spaarke/legal-workspace":["../../../solutions/LegalWorkspace/src/index.ts"]
}
```

Pointing tsc at sibling `*.ts` source forces it to compile those files **as members of the
Widgets project**, which violates `rootDir: "./src"` (the sibling files are outside it). The
cascade also surfaces ~70 secondary errors (missing `react`, `@fluentui/*` types) because the
sibling packages have their own `node_modules` topology that this project's compiler context
doesn't share.

Baseline reproduction (before fix):
```
TS6059: File '...Spaarke.AI.Outputs/src/...' is not under 'rootDir' '...Spaarke.AI.Widgets/src'
TS2307: Cannot find module 'react' ... (in Outputs source)
TS2875: This JSX tag requires the module path 'react/jsx-runtime' to exist ...
~70+ errors in total
```

---

## Options considered

| # | Option | Verdict |
|---|--------|---------|
| A | `composite: true` TS project references against siblings | **REJECTED** — requires `composite: true` + consistent `declaration` emit + `outDir` discipline across 3+ sibling tsconfigs; breaks if any sibling has emergent errors; heavy churn for a single-package fix |
| B | Widen `rootDir` (or remove it) and broaden `include` to cover siblings | **REJECTED** — surfaces sibling React/Fluent type errors because Widgets' compiler context lacks sibling `node_modules`; makes the problem WORSE, not better |
| **C** | **Adjust `paths` to point at sibling `.d.ts` declarations (not source)** | **CHOSEN** — smallest change; sibling already has `src/index.d.ts`; matches the existing `@spaarke/ui-components` pattern (already declarations-only); each package remains independent |
| D | Remove `paths` entirely; rely on `node_modules` symlinks + `package.json` `types` field | Equivalent to C but depends on `dist/` existing — currently `Spaarke.AI.Outputs/dist/` is absent. Less robust than pointing at `src/*.d.ts` which we know exists. |

---

## Decision

**Option C: Redirect `paths` at sibling `dist/index.d.ts`** (or `src/index.d.ts` where no
dist is available), mirroring the existing `@spaarke/ui-components` pattern.

```jsonc
"paths": {
  "@spaarke/ai-outputs":      ["../Spaarke.AI.Outputs/dist/index.d.ts"],
  "@spaarke/ui-components":   ["../Spaarke.UI.Components/dist/index.d.ts"],  // unchanged
  "@spaarke/legal-workspace": ["../../../solutions/LegalWorkspace/src/index.d.ts"]
}
```

### Why `dist/` (not `src/index.d.ts`) for Spaarke.AI.Outputs

Initial attempt pointed at `../Spaarke.AI.Outputs/src/index.d.ts` (which exists committed).
That FAILED: although the entry-point file is a `.d.ts`, its `export * from './types'`
re-exports cause tsc to resolve `./types` relative to `Spaarke.AI.Outputs/src/types/`,
where BOTH `index.d.ts` and `index.ts` exist. TS module resolution prefers `.ts` over
`.d.ts` when both exist, so tsc walks into `.ts` source — back to the rootDir cascade.

`dist/` (post-build) contains ONLY `.d.ts` + `.js` files (no `.ts` source neighbors), so
re-export resolution stays in declaration-only land. This is identical to how
`@spaarke/ui-components` already resolves successfully.

**CI implication**: the `Spaarke.AI.Widgets` typecheck depends on `Spaarke.AI.Outputs` being
built first. Captured as a sibling build step in the CI workflow (see below).

### Rationale

1. **Minimal blast radius** — only `Spaarke.AI.Widgets/tsconfig.json` changes. No churn in
   sibling packages, no `composite` flag cascade.
2. **Treats siblings as opaque** — tsc reads only declarations; sibling implementation files
   are never pulled into Widgets' compilation graph; rootDir constraint is naturally satisfied.
3. **Scales to future shared packages** — same pattern for each new `@spaarke/*` entry.
4. **Matches existing convention** — `@spaarke/ui-components` already uses this approach;
   the fix makes the three path entries consistent.
5. **Vite/runtime unaffected** — Vite resolves via `node_modules/@spaarke/*` symlinks and the
   package's `main` field at runtime. The `paths` mapping is only consulted by tsc for
   typecheck. Vite consumer bundles do not change.
6. **No `@ts-ignore` / `@ts-nocheck`** — error addressed by tsconfig topology, satisfying
   project constraints.

### Tradeoffs accepted

- **Declarations must stay up-to-date.** Siblings need to keep their `src/*.d.ts` (or
  `dist/*.d.ts`) committed/built so consumers see fresh type information. This is already
  the de facto state — declarations are committed in the repo.
- **Source-level Go-to-Definition jumps to `.d.ts`, not `.ts`.** Acceptable; sibling source
  is still readable via filesystem navigation.
- **NOT a true monorepo build graph.** If we want CI to build siblings first then verify
  Widgets compiles against their *fresh* declarations, we'd need Option A (composite refs).
  Defer that until we have a unified workspace build tool (Nx, Turbo, pnpm workspaces) —
  not in R4 scope per backlog DEFER items.

---

## CI gate

Added to `.github/workflows/sdap-ci.yml` in the existing `client-quality` job — four
distinct steps so each failure mode is visible in the CI log:

```yaml
- name: Install Spaarke.AI.Outputs deps
  working-directory: src/client/shared/Spaarke.AI.Outputs
  run: npm install --legacy-peer-deps --no-audit --no-fund

- name: Build Spaarke.AI.Outputs (declarations required by Widgets typecheck)
  working-directory: src/client/shared/Spaarke.AI.Outputs
  run: npx tsc

- name: Install Spaarke.AI.Widgets deps
  working-directory: src/client/shared/Spaarke.AI.Widgets
  run: npm install --legacy-peer-deps --no-audit --no-fund

- name: Spaarke.AI.Widgets — tsc --noEmit
  working-directory: src/client/shared/Spaarke.AI.Widgets
  run: npx tsc --noEmit
```

The final step is named distinctly so a tsc-only failure is distinguishable from
sibling-build, install, or lint failures in CI logs. Satisfies project constraint #5.

---

## Verification

After applying the fix (with `Spaarke.AI.Outputs/dist/` built):

```bash
$ npx tsc --noEmit -p src/client/shared/Spaarke.AI.Widgets/tsconfig.json
# rootDir errors (TS6059): 0  ✅
# remaining errors: 13 unrelated type errors (R-5 carry-over, see below)
```

**Baseline before fix**: ~70 errors (all rootDir cascade in sibling source).
**After fix**: 0 cross-rootDir errors. **NFR-05 acceptance criterion #1 satisfied.**

---

## Risk R-5 actual exposure

Per `plan.original.md §8`, R-5 warned this fix might surface previously-hidden type errors
across multiple packages and inflate effort ~3h → ~6h.

**Actual exposure**: 13 type errors revealed in `Spaarke.AI.Widgets/src/*` (NOT sibling
source — all in this package's own code). Classification:

| File | Error count | Class | Disposition |
|------|-------------|-------|-------------|
| `components/FeedbackButtons.tsx` | 1 | TS2307 — missing `@spaarke/auth` | FOLLOW-UP TASK (B-11) — needs `@spaarke/auth` in `paths` or `node_modules` |
| `hooks/useWorkspaceLayouts.ts` | 1 | TS2307 — missing `@spaarke/auth` | FOLLOW-UP TASK (B-11) — same as above |
| `providers/AiSessionProvider.tsx` | 3 | TS2307 (`@spaarke/auth`, `@spaarke/ai-context`) + TS7006 (implicit `any`) | FOLLOW-UP TASK (B-11) |
| `index.ts` | 2 | TS2322 — `ContextWidgetComponent` type variance (generic widget type mismatch) | FOLLOW-UP TASK (B-11) — fix at the widget-type definition layer |
| `registry/register-context-widgets.ts` | 3 | TS2322 — same `ContextWidgetComponent` variance | FOLLOW-UP TASK (B-11) — same root cause |
| `widgets/context/PlaybookGalleryWidget.tsx` | 1 | TS2322 — invalid `"neutral"` Badge literal | FOLLOW-UP TASK (B-11) |
| `widgets/workspace/WorkspaceLayoutWidget.tsx` | 2 | TS2305 — missing exports `getDefaultWorkspaceRenderer`, `WorkspaceRenderer` from `@spaarke/ui-components` | FOLLOW-UP TASK (B-11) — coordinate with task 057 (C-3) or task 048 (W-3) artifacts |

**Total**: 13 errors / 7 files. Per task constraint #6 ("If type errors surface beyond
cross-rootDir, fix each in its own commit — do NOT bundle with the rootDir fix commit")
AND per task notes ("if scope balloons (e.g., 10+ new errors), record carry-overs to
B-11 (task 067)"), these are carried over to **B-11 (task 067)** for batch resolution.

**This task (061 / B-2) completes when**:
- ✅ tsconfig rootDir cascade fixed (DONE)
- ✅ CI gate added (DONE)
- ✅ Decision record committed (this file)
- ✅ Carry-over errors documented (above table)

**Effort budget actual**: ~1.5 h (well under R-5's ~6h pessimistic ceiling; the 13 surfaced
errors are deferred per the contingency plan rather than fixed inline).

**CI gate will fail until B-11 lands.** Two options:
1. **Land this task with CI gate disabled (`continue-on-error: true`)** until B-11 fixes the
   13 errors. Documented as a temporary CI exception.
2. **Land this task atomically with B-11** so CI gate succeeds from day one.

**Recommendation**: Defer CI gate activation to merge time. Operator should decide based on
whether B-11 lands in the same PR train. If gated CI is set to fail on these errors before
B-11 lands, master will be red. For now, the CI step is added as configured above; if it
blocks the merge, set `continue-on-error: true` until B-11 ships.

---

## Files modified

- `src/client/shared/Spaarke.AI.Widgets/tsconfig.json` — `paths` re-targeted to `dist/index.d.ts` for ai-outputs (and confirmed `src/index.d.ts` for legal-workspace)
- `.github/workflows/sdap-ci.yml` — added 4-step typecheck gate in `client-quality` job
- `projects/spaarke-ai-platform-unification-r4/notes/b2-tsc-rootdir-decision.md` — this file
