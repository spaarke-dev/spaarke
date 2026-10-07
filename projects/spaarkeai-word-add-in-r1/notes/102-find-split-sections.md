# Task 102 — Find: two sections, own scroll, draggable divider (UAT round 6 item 4)

## What changed
- `components/FindSplitPane.tsx` (new, local — no shared vertical splitter exists to alias; none needed in webpack/jest/tsconfig). Ratio-based (default 0.5), `role="separator"` `aria-orientation="horizontal"` `aria-valuenow/min/max`, pointer capture drag, ArrowUp/Down 5 %, Home/End, 96 px minimum per region (ratio clamped, plus CSS min-height, re-clamped on ResizeObserver), per-viewer localStorage (try/catch).
- `FindResultsList.tsx`: heading renamed "Similar Documents"; each section = fixed heading + own scroller (`find-documents-scroll`, `find-records-scroll`); hub row stays inside the documents scroller; without `records` no divider.
- Progressive loads unchanged: documents `useLazyResults` and records `useFindRecordMatches` sentinels are each at the end of their own scroller, so each fires on its own list's scroll.

## Label check (note §3) — SERVER placeholders, client now masks them
- `VisualizationService.CreateParentHubNode`: `Label = sourceDoc.MatterName ?? "Matter"` (also Project/Invoice/Email) — when the parent name is unavailable the server sends the TYPE word as the name -> "Matter: Matter". Root cause server-side (MatterName null on the source doc's index row); client now renders "Matter (name unavailable)" when label equals the type word.
- `VisualizationService.CreateNode`: `DocumentType = document.DocumentType ?? "Unknown"` — index rows lacking a type get "Unknown". Client now omits "Unknown" from the meta line. Real fix (populate DocumentType / MatterName in the index) is a server/indexing matter — REPORTED, not edited.

## Tests
FindResultsList (updated + new), FindSplitPane (new, 7), FindView (rename only). New suite to gate: `shared/taskpane/components/__tests__/FindSplitPane.test.tsx`.
