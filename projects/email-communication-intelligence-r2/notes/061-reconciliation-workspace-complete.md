# Task 061 — ReconciliationWorkspace composition + shell A4/A5/A6 — COMPLETE (2026-08-10)

**Rigor**: FULL · sonnet·high · directional. **Escalation**: not triggered (all contracts composed).

## What shipped
- **`ReconciliationWorkspace.tsx`** (new, `@spaarke/communication-components`) — composes the shipped `ReconciliationGrid` → `ReconciliationBrowseShell` (via its `renderTabs` slot) → Related-to (`RelatedToCell`/052) · Fields (`FieldUpdateReconcileTab`/055) · Tasks (`TaskReconcileTab`/056) TabList. Owns only orchestration state (open row, selected tab, active citation, confirm handshake). Context-agnostic (`IDataverseClient` + `authenticatedFetch` via props; no Xrm — ADR-012). NFR-10 gate (Fields/Tasks disabled until Related-to confirmed; re-scope on override) + NFR-11 citation lift (tab citation → shell `activeCitation` → the one reader).
- **`ReconciliationBrowseShell.tsx`** (modified) — **A4** SprkModal `lg`→`xl` (92vw×88vh); **A6** fixed `1fr minmax()` grid → flex + reused `PanelSplitter` + local `useSplitRatio` hook (50/50 default, 0.25–0.75 clamp, keyboard ±2%, double-click reset); **A5** thin scrollbar on the tabs pane (Fluent tokens only). Reader/nav/citation/`renderTabs` contract + all `data-testid`s untouched.
- **`ReconciliationGrid.tsx`** (modified) — forwarded the existing `<DataGrid onRecordsLoaded>` seam (pass-through only) so the workspace builds the "N of M" browse queue from the grid's loaded rows.
- **`components/index.ts`** — export `ReconciliationWorkspace`.

## Deviations (all reuse-preserving)
- Added `onRecordsLoaded` pass-through to ReconciliationGrid (not in the POML) — forwards an existing DataGrid seam; production analog of the prototype's local `emails` array; needed for the queue.
- No `theme` prop — ReconciliationGrid has none; theme comes from the host `FluentProvider` (ADR-021).
- `useThreadPaneLayout` NOT reused — it models a fixed-px collapsible sidebar (+localStorage) and isn't exported; a symmetric ratio split needed a small local hook. Reused the presentational `PanelSplitter` grip.
- NFR-10 scope: confirmed regarding resolved by the host from the (refreshed) row via `resolveRegarding` (mirrors the `RelatedToGridBinding.onConfirmed` no-arg + host-refresh pattern; `EmailConnectionsReview.onAssociationsChanged` is parameterless).

## Verification
- Package build (tsc): **0 errors**. Jest: **229/229** (32 suites). New `ReconciliationWorkspace.test.tsx` = 6 (grid render · row-open opens shell + separator + no navigateTo · NFR-10 gate · Fields Accept→apply{overrideValue} · NFR-11 citation→reader · RelatedToCell reuse). `ReconciliationBrowseShell.test.tsx` +1 (A6 splitter).
- Step 9.5: code-review PASS (inline; no critical/warning) · adr-check PASS (021/012/050/022/045) · /conflict-check CLEAN (0 open-PR overlap, no master divergence on these files).

## Next
**062** — dual host (code page `sprk_communicationreconciliation` + SpaarkeAi widget) mounts this one component. Then **059** (seed gridconfig + dual deploy, gated).
