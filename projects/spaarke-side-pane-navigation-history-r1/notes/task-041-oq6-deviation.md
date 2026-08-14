# Task 041 — OQ-6 escalation-trigger deviation + tsconfig/vite gap fix

> **Task**: 041 — Recent (Viewed) tab
> **Date**: 2026-08-13

## 1. OQ-6 escalation trigger — not fired (per explicit task dispatch instruction)

The task POML's `<escalation>` says: "If the capture service (030) row shape
cannot supply a clean label/target for a pagetype (e.g., custom-page OQ-6),
STOP and escalate the labeling gap (root §6.5) rather than silently rendering
a misleading or empty chip."

The **task-041 dispatch instructions** (given at execution time, superseding
the raw POML trigger for this narrow case) explicitly directed: *"for Recent,
treat an unresolvable custom page as a generic 'Page' chip using its
available label, and note it."*

**What was implemented**: `RecentTab.tsx`'s `chipForRow()` maps
`sprk_pagetype=Custom` to a generic `"Page"` chip (Fluent `Badge`, `subtle`
color), using the row's stored `sprk_displayname` as the row label — never an
empty or misleading entity-specific chip. `navigateToRow()` does not attempt
navigation for `Custom` rows (no generic safe target exists from a stored
history row) — the row still renders, just isn't clickable-to-navigate.

**Why this is not a hard STOP**: the capture engine (`navigatorCaptureService.ts`,
task 030) **only ever writes `sprk_pagetype=EntityRecord` history rows**
today (see that file's module docblock, "Capture scope" — entitylist/
dashboard/custom pages are explicitly skipped at capture time, no malformed
row is ever written). So in production, the Recent (History) tab will never
actually render a `Custom`-pagetype row from real capture data. The
`EntityList`/`Custom`/`WebLink` chip branches in `RecentTab.tsx` exist for
robustness/future-proofing (the `<ui-tests>` explicitly ask for all five chip
types to be exercised) and because task 050/051 (pins/bookmarks) may write
those pagetypes to the SAME `sprk_navitem` entity later. Full custom-page
label resolution (parsing `getPageContext()` for a human-readable custom-page
name) remains deferred to task 051, unchanged from the OQ-6 investigation
note in spec.md.

**No path A/B/C ADR conflict here** — this is a UI-labeling scope decision
within task 041's own constraints, not an ADR rule.

## 2. NavigatorPane tsconfig/vite gap — deep shared-lib subpath import

`RecentTab.tsx` is NavigatorPane's first file to deep-import a shared-lib
subpath (`@spaarke/ui-components/services/navigator/navItemRepository`,
mirroring the `useSprkMemoRepository.ts` / DEF-10 tree-shaking convention).
NavigatorPane's `vite.config.ts` and `tsconfig.json` had no alias/paths entry
for this (its original task-040 design note assumed it would "only consume
the pre-built `@spaarke/ui-components` dist/" via plain bare-specifier
resolution, which is sufficient for the top-level barrel but not for
subpaths — there is no `exports` map on the shared-lib `package.json`).

**Fix applied** (both required for the import to resolve at every layer):
- `vite.config.ts` — added `resolve.alias["@spaarke/ui-components/services"]`
  → the shared-lib's **compiled `dist/services`** directory (NOT source,
  unlike Notepad's heavier `resolveSharedLibDeps` source-aliasing approach —
  this keeps NavigatorPane's original "consume pre-built dist" design intact).
- `tsconfig.json` — added a matching `paths` entry for
  `@spaarke/ui-components/services/*` → the same `dist/services/*` target, so
  `tsc --noEmit` resolves the import instead of reporting `TS2307`.
- `tsconfig.json` — added `"types": ["jest", "@testing-library/jest-dom"]`.
  This is unrelated to the subpath-import fix: it closes a **latent, pre-existing
  gap** — `RecentTab.test.tsx` is the first NavigatorPane test file placed
  under `src/` (task 040's `NavigatorBody.test.tsx` lives in the sibling
  top-level `__tests__/` folder, outside this tsconfig's `"include": ["src"]`
  scope, so the missing jest-dom type augmentation was never exercised by
  `tsc --noEmit` before now).

Verified: `npx tsc --noEmit` clean, `npx jest` 13/13 green, and a real
`npm run build` (Vite production bundle, cache-cleared first) succeeds and the
built `dist/index.html` contains the RecentTab strings — confirming the
runtime alias (not just the jest moduleNameMapper) actually resolves the
import.

## 3. Shared-lib edit (flagged per task instructions)

`src/client/shared/Spaarke.UI.Components/src/services/navigator/navItemRepository.ts`
gained three new exports: `listHistoryItems`, `listPinItems`,
`createPinItem` (+ the `CreatePinItemInput` type). All three mirror the
existing file's style (JSDoc, `NavItemRepositoryError` wrapping,
`requireWebApi()`/`getXrm()` re-acquire-per-call pattern) and are additive —
no existing export's signature or behavior changed. `listPinItems` and
`createPinItem` are intentionally MINIMAL (no un-pin/toggle/duplicate-guard)
per the task's explicit instruction that task 050 owns the full pin gesture
and should extend these rather than introduce a second read/write path.
