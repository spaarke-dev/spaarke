# Handoff — 2026-05-29 pre-compact

> **Purpose**: Continue the spaarke-matter-ui-enhancement-r1 project from this point in a fresh session. This document covers session 2 (v1.1.50 → v1.1.69) of the UAT polish loop. The prior session handoff is [`handoff-2026-05-28-pre-compact.md`](./handoff-2026-05-28-pre-compact.md) (covers v1.1.45 → v1.1.50).

---

## Project status at handoff

**Overall**: Core project work (31/34 tasks) merged to master. UAT polish loop on **SemanticSearchControl PCF** continues. The project remains in a tight iterate-on-screenshot-feedback rhythm; no fundamental architecture changes pending.

**Tasks**: 31 of 34 task POMLs ✅ in `tasks/TASK-INDEX.md`. Remaining 3 (Phase 6 form XML, Phase 7 task 074 UAT, Phase 8 task 090 wrap-up) are user-owned.

**Branch**: `work/spaarke-matter-ui-enhancement-r1` at `d5231f8a` (v1.1.69 + docs commit). v1.1.45..v1.1.49 ALL in master via merge commit `b451bbe1`. v1.1.50..v1.1.69 pushed to origin but NOT yet merged to master.

**Worktree**: `c:/code_files/spaarke-wt-spaarke-matter-ui-enhancement-r1` (worktree of `C:/code_files/spaarke/.git`).

**Latest packaged artifact**: `src/client/pcf/SemanticSearchControl/Solution/bin/SpaarkeSemanticSearch_v1.1.69.zip` (212 KB) — awaiting user UAT.

---

## SemanticSearchControl PCF version history (this session: v1.1.50 → v1.1.69)

| Version | Commit | Scope |
|---|---|---|
| v1.1.50 | `b921bc63` | 8 items round 5 (from session 1) — ListView lazy-load, column refactor, AI sparkle, preview unification |
| **v1.1.51** | `53ae96ef` | 8 items — Clear filters button; `relationship: 'both'` tag fix for dupes-across-paths; Document col 400→260; AI summary hidden in preview menu; bottom Relationship pill + similarity %; date inline with title; lighter Badge tint; outlined Search button |
| **v1.1.52** | `054f1651` | 3 items — Card layout revert (date back on own row); CommandBar nowrap on 1920×1080; SendEmailDialog widened to 720px via new `maxWidth?` prop |
| **v1.1.53** | `c75490a3` | 5 items — Card pill order swap; "100%" instead of blank for associated; card title icon removed; grid Type col icon-only (matches card hero classification); email modal 720→1200px |
| **v1.1.54** | `ccb0f143` | 6 items — Top % pill removed; "100%" in green; pill row bottom-anchored; grid mirrors green chip; **email preview-hide pattern** (close preview while email open); menu standardization (hide AI Summary/Toggle workspace/Rename; show Preview/Open File/Find Similar/Download/Copy link/Email/Open Record/Pin to top/Delete) |
| **v1.1.55** | `ff6e8094` | 4 items — Email modal 640→1280px; bulk action bar gap XS→M; leading divider between count and icons |
| **v1.1.56** | `82133964` | SendEmailDialog width fix (`width: '90vw'` → `'100%'`) + new `height?` prop default 'auto'; SemanticSearchControl passes height="85vh" |
| **v1.1.57** | `fbe91c3d` | SendEmailDialog inline `style={{ minHeight: height }}` added — Fluent's `block-size: fit-content` was suppressing inline `height`; `min-height` semantics force growth |
| **v1.1.58** | `ccb0f143` | SendEmailDialog complete flex hierarchy (DialogBody + DialogContent + DialogActions classes + flex grow); Textarea wrapper className `& > textarea` selector targeted inner element (didn't fully work — see v1.1.59) |
| **v1.1.59** | `6abab84e` | 4 items — Textarea fix via Fluent slot prop `textarea={{ className }}` (descendant selector failed); hide X in SendEmailDialog title; ListView widths trim + first localStorage prefix bump `.v2`; sticky-right on menu cell |
| **v1.1.60** | `4c4e8310` | SendEmailDialog `title?: string` prop added (default `'Email Document'`) — fully cross-domain reusable |
| **v1.1.61** | `2db4cd09` | **MAJOR TECH-DEBT CLEANUP**: removed SendEmailDialog from PCF entirely; single-doc Email now uses DocumentEmailWizard (same as bulk Email); `singleDocForWizard` state; +52 / −159 lines; zero orphan refs verified; historical narrative comments scrubbed |
| **v1.1.62** | `9d24ee9e` | Pure cache-override version bump (no code changes) per pcf-deploy protocol |
| **v1.1.63** | `31375c01` | 5 items — Row menu auto-margin anchor (Fluent v9 DataGrid rows are flexbox, NOT grid — verified by reading source); preview stays open behind wizard for BOTH single-doc paths; wizard `maxWidth?`/`height?` props plumbed through WizardShell + DocumentEmailWizard; bulk Email loads selected docs only via `emailWizardItemsSelected`; fileSize warning deferred (BFF SearchResult lacks `fileSize`) |
| **v1.1.64** | `e0e91d71` | Document col DEFAULT 480→240 to eliminate horizontal scroll. Auto-margin from v1.1.63 still anchors menu to right |
| **v1.1.65** | `d83a6683` | `overflowX: 'auto'` → `'hidden'` on container (phantom scrollbar from sub-pixel rounding). Sticky-right defense still works under overflow:hidden |
| **v1.1.66** | `1049faf1` | Document col 240→320; `MAX_WIDTHS` cap map; header wrapped in `TableCellLayout` for body-matching padding (partially fixed) |
| **v1.1.67** | `b76156eb` | localStorage prefix `.v2` → `.v3` to invalidate over-large persisted widths |
| **v1.1.68** | `45ae20d7` | **Root-cause fix after reading Fluent v9 source** (`@fluentui/react-table`): `useTableColumnResizeState` re-dispatches on prop change BUT `useTableColumnResizeMouseHandler` pumps raw mouse delta with zero max enforcement anywhere in Fluent. Option D = A + C: **C** heal-on-read in `useDocumentListPrefs` clamps stale localStorage and re-persists; **A** `resizeRemountKey` counter on DataGrid forces remount when drag exceeds cap, re-initializing Fluent's reducer state from clamped options |
| **v1.1.69** | `3401b4c6` | 2 items from DevTools-confirmed UAT: (1) **Header/body padding mismatch fix** — apply same cell-specific classes (`selectCell`/`pinCell`/`menuCell`) to header cells via mergeClasses; Pin header was 52×57 vs body 36×69 (16px mismatch); (2) **Responsive MAX_WIDTHS** via ResizeObserver — `containerWidth − 240 = maxDocument` produces HD 480 / 2K 880 / 4K 1780 |

**Currently installed version (per user)**: v1.1.69 awaiting UAT.

---

## v1.1.70+ follow-up queue (carried forward from prior + new this session)

### Primary (deferred, larger efforts)

**FilePreview promotion to `@spaarke/ui-components` (Option A, 3-5 hours)** — STILL deferred (carried from session 1 handoff). Move the PCF-local `FilePreviewDialog.tsx` (with all v1.1.45-v1.1.50+ enhancements) into the shared lib. Multi-paragraph rationale preserved in `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx` lines 1-39.

### Secondary (smaller, BFF-touching)

- **`fileSize` field on BFF `SearchResult`** (~30 min + BFF redeploy) — currently `fileSizeBytes` is always undefined in the wizard, so the 25 MB attachment warning never triggers. Wizard logic exists at `DocumentEmailWizard.tsx:545-550`. Plumb through `SemanticSearchService.cs` → `SearchResult.cs` projection → PCF passthrough in `emailWizardItemsAll`. Then the warning fires automatically.

### Tertiary (smaller cleanup)

- **Widen shared `SendEmailDialog` default maxWidth** (or document as deferred) — current default `'520px'` for back-compat. LegalWorkspace + SpeDocumentViewer consume it at default size. If we want them to grow too, decide breakpoint and ship.
- **Cleanup unused styles** — `summaryTldr`/`summaryBody` styles + `IFilePreviewDialogSummary` interface refs in `FilePreviewDialog.tsx` (from when AI summary section lived inside the preview).
- **Refactor** — Extract `mergeForRelationship(prior, fresh)` helper from `SemanticSearchApiService.searchUnion` for unit-testability.
- **Type column "—" fallback** when both `documentType` and `fileType` empty.

---

## Major architectural changes this session

### 1. Email modal evolution → unified on DocumentEmailWizard (v1.1.61)

**Why**: SendEmailDialog (simple) and DocumentEmailWizard (3-step with Summary + Attach toggles + sprk_communication tracking) coexisted — single-doc Email used the dialog, bulk Email used the wizard. UX inconsistent.

**v1.1.61 unification**: Single-doc Email now routes through the wizard. New state `singleDocForWizard: IDocumentEmailWizardItem | null` distinguishes flows. Wizard's `selectedDocuments` reads `singleDocForWizard ? [it] : emailWizardItemsSelected`. Removed from `SemanticSearchControl.tsx`:
- Imports: `SendEmailDialog`, `ISendEmailPayload`, `ILookupItem`
- State: `emailDialogResult`, `previewedBeforeEmail`
- Handlers: `handleEmailDialogClose`, `handleSearchUsers`, `handleSendEmail`
- Computed: `emailDefaultSubject`, `emailDefaultBody`
- JSX: the entire `<SendEmailDialog>` block

Net: +52 / −159 lines in `SemanticSearchControl.tsx`.

**Shared `@spaarke/ui-components/SendEmailDialog` still ships** for other consumers (LegalWorkspace, SpeDocumentViewer). v1.1.60 made it fully reusable (`title?: string` prop = default 'Email Document', plus `maxWidth?` and `height?` from earlier rounds). No project-specific bindings remain in the shared component.

### 2. Preview-hide vs preview-stay-open (v1.1.54 → v1.1.63)

**v1.1.54 (preview-hide pattern)**: Close preview when email opens, re-open after — solved modal-stacking issue with the *old* SendEmailDialog approach (the simpler modal didn't visually occlude the preview).

**v1.1.63 (preview-stay-open)**: With the wizard now in use (richer modal that visually dominates), removed `setHostPreviewDocId(null)` from `handleEmailDocument`. Wizard renders as modal-over-modal. After wizard closes, user returns to preview at the same docId.

### 3. Wizard sized to match FilePreview (v1.1.63)

Added backward-compatible `maxWidth?: string` and `height?: string` props on BOTH `WizardShell` (which owns the `DialogSurface`) AND `DocumentEmailWizard` (passes through). Defaults preserve `WizardShell`'s existing `'95vw'` / `'70vh'` so DocumentRelationshipViewer (the other consumer) doesn't change behavior. Host passes `maxWidth="1280px" height="85vh"` — same footprint as FilePreviewDialog.

Same inline-style-bypass pattern as SendEmailDialog v1.1.56+: `style={{ maxWidth, height, minHeight: height }}` to bypass Fluent's content-sizing rule.

### 4. Column width / scroll / alignment saga (v1.1.59 → v1.1.69)

This consumed the largest fraction of session-2 effort. Several pivots:

**The Fluent v9 source diagnosis (v1.1.68)**: Read `node_modules/@fluentui/react-table/lib/`. Found `useTableColumnResizeState` re-dispatches `COLUMN_SIZING_OPTIONS_UPDATED` on prop change (`useTableColumnResizeState.js:78-85`), BUT `useTableColumnResizeMouseHandler` pumps raw mouse delta into `SET_COLUMN_WIDTH` every tick (`useTableColumnResizeMouseHandler.js:12-24`) and the `setColumnWidth` action clamps only to `minWidth` — **Fluent has NO max-width concept anywhere in the resize state machine**. The COLUMN_SIZING_OPTIONS_UPDATED re-merge SHOULD win on next render but in practice the race is unreliable.

**Fix that finally worked (v1.1.68 = Option D = A + C)**:
- **C** Heal-on-read in `useDocumentListPrefs`: clamps stale localStorage to MAX_WIDTHS and re-persists healed values on every read (initial mount + matter switch). No manual clear required — works even when iframe-origin mismatch defeats console scripts.
- **A** `resizeRemountKey` counter on `<DataGrid>` — increments when `handleColumnResize` sees a drag exceed the cap, forcing React to unmount + remount the DataGrid so Fluent's `useReducer` re-initializes from clamped `columnSizingOptions`.

**v1.1.69 added on top**: ResizeObserver-based **responsive** MAX_WIDTHS so the cap scales with form section width (HD 480 / 2K 880 / 4K 1780). Same plumbing as the static MAX_WIDTHS — `dynamicMaxWidths` memo overrides the constant.

**UX trade-off**: drag visually stretches column with cursor, then snaps back to cap on release. True real-time-during-drag clamping would require patching Fluent or refactoring to lower-level `useTableFeatures` primitives.

### 5. Header/body cell alignment (v1.1.66 partial, v1.1.69 complete)

User used DevTools to measure exact dimensions and pinpoint that Pin header (52×57) and body (36×69) had a 16px horizontal padding mismatch. Root cause: body render applied `pinCell { paddingLeft: 0; paddingRight: 0 }` and `selectCell { paddingLeft: XS; paddingRight: XS }` via `mergeClasses`, but the header render only applied `menuCell`. Other header cells fell back to TableCellLayout's default 8px.

**v1.1.69 fix**: header render now applies the SAME cell-specific classes the body does:
```tsx
className={mergeClasses(
  styles.headerCell, styles.gridCell,
  isSelectHeader && styles.selectCell,
  isPinHeader && styles.pinCell,
  isMenuHeader && styles.menuCell,
)}
```

---

## Other state at handoff

### Form XML (task 060)

**Owner**: The user. Still not started per the original deploy planning.

Chart def lookups for the 5 Visual Host instances (carried from session 1):
- Matter Health Composite: `a8b8df8b-f359-f111-a825-3833c5d9bcab`
- Matter Budget: `7bf5b79e-f359-f111-a825-3833c5d9bcab`
- Matter Tasks: `c4feb098-f359-f111-a825-3833c5d9bcab`
- Matter Next Date: `154bd4a4-f359-f111-a825-3833c5d9bcab`
- Matter Activity: `1a4bd4a4-f359-f111-a825-3833c5d9bcab`

Each Visual Host instance: `showTitle = true` (opts into CardChrome). Layout: 2-col 66/34, left = Matter Info + Documents PCF, right = 5 stacked Visual Hosts.

### BFF deploy

**Status**: Unchanged from session 1. `spaarke-bff-dev` is the deployed target. No BFF redeploy was done this session — none of the v1.1.51..v1.1.69 changes required it.

The pending `fileSize` field on `SearchResult` (for the wizard's 25 MB warning) would require a BFF redeploy when implemented. Deploy procedure unchanged:
- Script: `scripts/Deploy-BffApi.ps1`
- Run with `pwsh` (NOT `powershell.exe`)
- Pre-flight: `az webapp show -g rg-spaarke-dev -n spaarke-bff-dev --query state -o tsv`

---

## Resuming in a new session

Pick up by reading (in this order):

1. **This handoff** (full state for v1.1.50 → v1.1.69)
2. **`projects/spaarke-matter-ui-enhancement-r1/current-task.md`** — Quick Recovery row points at this handoff
3. **`projects/spaarke-matter-ui-enhancement-r1/notes/handoffs/handoff-2026-05-28-pre-compact.md`** — only if you need pre-v1.1.50 archaeology
4. **Latest commit on `work/spaarke-matter-ui-enhancement-r1`**: `d5231f8a` (v1.1.69 + docs)

### Next probable user requests

- **UAT feedback on v1.1.69**: most likely path — bundle that with whatever the user finds into a v1.1.70 polish round
- **"merge to master"**: when UAT signs off on a version, merge `work/spaarke-matter-ui-enhancement-r1` into master:
  ```
  cd C:/code_files/spaarke
  git fetch origin
  git merge --no-ff origin/work/spaarke-matter-ui-enhancement-r1 -m "Merge: matter-ui-r1 UAT polish v1.1.50..v1.1.69"
  git push origin master
  ```
- **Form XML help** (task 060): if the user starts placing the chart def lookups + 2-col layout and hits issues
- **Task 074 UAT**: cross-cutting validation (axe / dark mode / NFR-05 regression / App Insights events)
- **Task 090 wrap-up**: README → Complete, lessons-learned.md, `/repo-cleanup`, final merge
- **The deferred FilePreview promotion** (Option A, 3-5h) — user has not yet asked but it's still in the queue

### Patterns established this session

- **Heal-on-read + remount counter** for Fluent v9 DataGrid column width clamping (v1.1.68). This is now the canonical pattern for any DataGrid where the consumer wants max-width caps Fluent itself doesn't enforce. The hook layer heals stale persisted state; the component layer enforces the responsive cap; the remount key handles the in-session drag-exceeds-cap case.
- **Inline style + minHeight + className flex hierarchy** for shared modal sizing (SendEmailDialog v1.1.56→v1.1.58 + WizardShell v1.1.63). The `style={{ maxWidth, height, minHeight: height }}` pattern is the only reliable way to override Fluent v9 DialogSurface's `block-size: fit-content` content-sizing rule.
- **Cell-specific className mergeClasses on header AND body** (v1.1.69). Don't trust TableCellLayout's defaults to match across the two layers — if you customize padding on body cells, you MUST apply the same class to header cells or the columns drift apart by 2× the padding value.
- **ResizeObserver + 1px hysteresis** for responsive width caps (v1.1.69). Cheap to recompute the memo per resize event; the 1px floor avoids fractional pixel thrash.
- **Bump localStorage prefix `.v1 → .v2 → .v3`** when DEFAULT or MAX widths change in a way persisted state would violate. Three bumps to date (v1.1.59, v1.1.67, and the constant in v1.1.68 — though the latter kept the prefix and added heal-on-read instead).
- **Sub-agent dispatch for multi-item polish rounds**: one agent per round, agent does code + bump + build + copy + pack; main session does git. Main session also typically scrubs over-narrative comments the agent leaves behind ("v1.1.50–v1.1.60 history" comments) per the "no tech debt confusion in future" mandate.

### Coding constraints (still binding)

- Per ADR-022: PCF uses React 16/17 platform-provided. No React 18 APIs.
- Per ADR-021: tokens-only colors. Zero hex/rgb. Dark mode parity required.
- Per ADR-012: shared components consumed via deep-path imports (NOT barrel — Lexical/React 16 boundary).
- Per ADR-028: BFF calls via `authenticatedFetch` from `@spaarke/auth`. Never `PublicClientApplication`, never raw bearer headers, never `accessToken` typed props.
- Spec FR-DOC-06: **AssociatedOnly auto-search behavior MUST be preserved verbatim** across every polish round. Tested verbatim every round.
- Per pcf-deploy skill: `npm run build:prod` (NOT `npm run build`). Bundle size sanity: 700-800 KB is current normal; if >1 MB, build:prod is misconfigured.

### Deploy notes (still binding)

- PCF imports use `pac solution delete` + `pac solution import` to clear Dataverse control cache, then user hard-refreshes browser (Ctrl+Shift+R).
- All 5 version-bump locations MUST be updated each round (the pcf-deploy protocol). Verify with grep before build:
  1. `SemanticSearchControl/ControlManifest.Input.xml` — version + `(v1.1.XX)` in description-key
  2. `SemanticSearchControl/SemanticSearchControl.tsx` — footer string
  3. `Solution/solution.xml` — `<Version>`
  4. `Solution/Controls/sprk_Sprk.SemanticSearchControl/ControlManifest.xml` — version + description
  5. `Solution/pack.ps1` — `$version`
- ZIP verification via `Expand-Archive` to `$env:TEMP\pcf-verify-1.1.XX` after pack — check solution.xml, ControlManifest.xml, bundle.js footer string.

---

## Files this session frequently modified

### PCF source
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` (host integration, email handlers, wizard wiring, footer)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/ListView.tsx` (column widths, alignment, header rendering, responsive cap) — **the most-edited file in session 2**
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/ResultCard.tsx` (card layout, pills, icon removal)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/CommandBar.tsx` (Clear button, nowrap, scope toggle)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/FilePreviewDialog.tsx` (menu actions, X removal)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/BulkActionBar.tsx` (gap + leading divider)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/components/SearchInput.tsx` (button appearance tone-down)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/SemanticSearchApiService.ts` (searchUnion 'both' tag)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/hooks/useDocumentListPrefs.ts` (localStorage prefix bumps, heal-on-read)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/types/search.ts` (`relationship` union extended to `'both'`)

### Shared library (`@spaarke/ui-components`)
- `src/client/shared/Spaarke.UI.Components/src/components/SendEmailDialog/SendEmailDialog.tsx` (maxWidth, height, title props + flex hierarchy)
- `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx` (maxWidth, height props)
- `src/client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes.ts` (prop types)
- `src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx` (maxWidth, height props pass-through)

### Plus the 5 version-bump files per round + `Solution/Controls/.../bundle.js` (rebuilt artifact)

---

## Session-2 deep-dives worth remembering

### Why localStorage clear scripts can fail in PCF context

PCFs render inside iframes. The localStorage we write to belongs to the PCF iframe's origin. When the user runs `localStorage.removeItem(...)` in the main Power Apps page's DevTools console, they're operating on the OUTER frame's storage — wrong origin. Symptom: scripts return clean but the PCF still reads the old values.

Workaround:
- Right-click directly in the PCF surface → Inspect → in the new DevTools, top of Console has a frame selector dropdown — switch to the PCF iframe before running scripts
- Or use the v1.1.68 heal-on-read pattern which runs INSIDE the PCF context and doesn't need manual intervention

### Why three rounds of column-width fixes failed before v1.1.68

| Attempt | What it did | Why it failed |
|---|---|---|
| v1.1.66 `columnSizingOptions` clamp | `idealWidth: Math.min(persisted, max)` on every render | Fluent's reducer doesn't reliably re-merge on prop change during in-session interactions |
| v1.1.66 `handleColumnResize` clamp | `Math.min(data.width, max)` at persist time | Fluent's mouse handler fires `SET_COLUMN_WIDTH` BEFORE the callback — internal state already has unclamped value |
| v1.1.67 prefix bump `.v2 → .v3` | Invalidates persisted widths | Works for users with empty localStorage but if user resizes again in session, Fluent's internal state takes the new value with no cap enforcement |
| **v1.1.68 Option D (A + C)** | Heal-on-read in hook + remount counter | Heal-on-read normalizes stored values before they enter the system; remount forces Fluent to re-init from clamped options |

---

*This handoff captures everything needed to continue the project in a fresh session. Read it first; then read `current-task.md`; then pick up wherever the user directs.*
