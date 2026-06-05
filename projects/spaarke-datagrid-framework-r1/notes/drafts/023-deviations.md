# Task 023 — Deviations

> **Created**: 2026-06-01 by task 023
> **Task**: 023-drill-through-custom-pages — Custom Pages `sprk_kpiassessmentspage` + `sprk_invoicespage`

---

## Deviations from POML

### D-023-01 — Invoice main.tsx is 61 lines (spec target ≤60)

**What**: `sprk_invoicespage/src/main.tsx` has 61 lines vs the spec criterion "each main.tsx is ≤60 lines (excluding boilerplate)".

**Why**: One extra docstring line in the file header explaining that the Mark Paid command handler is deferred to R2 (per task 022's `022-mark-paid-decision.md`). The doc comment exists so the next reader doesn't waste effort hunting for a missing `registerCommandHandler("mark-paid", ...)` call.

**Impact**: Negligible. Spec language is "~50 lines" / "≤60 lines (excluding boilerplate)". 61 total lines (boilerplate included) is materially compliant with the intent ("Shell only — no business logic"); excluding the 16-line header docblock, the actual code body is ~45 lines.

**Decision**: Accept. Documentation comment > artificial line-trimming.

---

### D-023-02 — `@spaarke/ui-components` resolved via prebuilt `dist/` (not source-aliased)

**What**: Both new Custom Pages' `vite.config.ts` files mirror the **CalendarSidePane** pattern (simple), not the **EventsPage** pattern (source-aliases `@spaarke/events-components`, leaves `@spaarke/ui-components` on prebuilt dist).

**Why**: The DataGrid framework lives in `@spaarke/ui-components`. EventsPage already established the convention that `@spaarke/ui-components` is consumed from `node_modules/@spaarke/ui-components/dist/` (not source-aliased) — see EventsPage `vite.config.ts` line 100-103 comments: "@spaarke/ui-components is NOT aliased here ... Switching it to source-alias would tree-shake out ~90 KB gzip — that's a separate optimization decision outside this hoist task's scope."

We followed the same precedent. Both pages bundle 1.17 MB raw / 328 KB gzip — well within Spaarke Code Page budget (LegalWorkspace baseline ≈ 1.2 MB / 360 KB).

**Impact**: None. Consistent with established Spaarke Code Page convention.

**Decision**: Accept. Follow established precedent.

---

### D-023-03 — Mark Paid handler NOT registered (per task 022 decision)

**What**: Task 023 POML step 4 says "If UQ-05 was R1, additionally register Mark Paid handler via `registerCommandHandler('mark-paid', handler)`."

**Why**: Per task 022's `022-mark-paid-decision.md`, Mark Paid is deferred to R2 — the configjson `commandBar.commands` array does NOT include a `mark-paid` entry. No registration needed.

**Impact**: None — this matches task 022's design decision.

**Decision**: No deviation; task 023 step 4 conditional correctly resolved to "skip".

---

## Acceptance criteria — final status

| Criterion | Status | Evidence |
|---|---|---|
| `npm run build` produces single-file HTML | ✅ PASS | Both `dist/index.html` files; viteSingleFile plugin inlined all JS+CSS; sizes 1.17 MB / 328 KB gzip |
| DataGrid renders with Matter context filter | ✅ PASS (static) | Framework overlay wired via configjson `behavior.parentContextFilter` (commit fe4f675d); `parentContext={{matterId}}` passed from URL parse |
| `applyStylesToPortals={true}` on root FluentProvider | ✅ PASS | grep `applyStylesToPortals` returns 1 match per main.tsx (line 46 KPI, line 49 Invoice) |
| Line count ≤60 (excluding boilerplate) | ✅ PASS (KPI 58 / Invoice 61, code body ~45 each) | See D-023-01 above |

---

## Files created (8 per page = 16 total)

### sprk_kpiassessmentspage
- `src/solutions/sprk_kpiassessmentspage/package.json` (30 lines)
- `src/solutions/sprk_kpiassessmentspage/vite.config.ts` (35 lines)
- `src/solutions/sprk_kpiassessmentspage/tsconfig.json` (28 lines)
- `src/solutions/sprk_kpiassessmentspage/tsconfig.node.json` (10 lines)
- `src/solutions/sprk_kpiassessmentspage/index.html` (24 lines)
- `src/solutions/sprk_kpiassessmentspage/src/main.tsx` (58 lines)

### sprk_invoicespage
- `src/solutions/sprk_invoicespage/package.json` (30 lines)
- `src/solutions/sprk_invoicespage/vite.config.ts` (35 lines)
- `src/solutions/sprk_invoicespage/tsconfig.json` (28 lines)
- `src/solutions/sprk_invoicespage/tsconfig.node.json` (10 lines)
- `src/solutions/sprk_invoicespage/index.html` (24 lines)
- `src/solutions/sprk_invoicespage/src/main.tsx` (61 lines)

### Build outputs (single-file HTML web resources, gitignored)
- `src/solutions/sprk_kpiassessmentspage/dist/index.html` (1,172 KB / 328 KB gzip)
- `src/solutions/sprk_invoicespage/dist/index.html` (1,172 KB / 328 KB gzip)
