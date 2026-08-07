# Task 051 — Triage as grid columns — COMPLETE (2026-08-07)

**Rigor**: FULL (override up from authored STANDARD — modifies `.tsx` on contended `Spaarke.Communication.Components`) · sonnet·high · directional. **Model**: Opus 4.8.
**Result**: shared-lib code + tests green (tsc 0; jest 26 suites / 189 tests, +6 for 051). Step 9.5 clean. Conflict-check clean (email-communication-solution-r5 no overlap).

## What shipped (additive renderers on the existing grid — no new data path, ADR-045)

- **`components/ReconciliationGrid/triageColumnRenderers.tsx`** (NEW): five presentational renderers keyed by `sprk_communication` field logical name, plugged into the task-050 `DataGridOverrides.columnRenderers[field] = (value, record) => node` seam —
  - `sprk_triagecategory` (LOOKUP → taxonomy) → subtle chip of the formatted name;
  - `sprk_triagepriority` (CHOICE Urgent=100000000 … Low=100000003) → status Badge (Urgent=danger · High=severe · Medium=warning · Low=informative);
  - `sprk_triagesummary` (text) → truncated cell (120 chars) + full text on hover;
  - `sprk_riconfidence` (double 0..1) → toned "NN%" Badge (≥0.8 success · ≥0.5 informative · else warning);
  - `sprk_reviewoutcome` (CHOICE File=100000000 … Pending=100000004) → status Badge.
  - Every renderer degrades a missing/null (or unrecognized choice) value to a neutral em-dash placeholder — no crash, no literal "undefined".
- **`ReconciliationGrid.tsx`** (MODIFIED): merged `TRIAGE_COLUMN_RENDERERS` into `DEFAULT_COLUMN_RENDERERS` (caller-supplied renderers still win per field).
- **`needs-review.gridconfiguration.json`** (MODIFIED): added the five triage attributes + cells + column labels; **default sort is now `sprk_triagepriority` ascending (Urgent-first), `sprk_receiveddate` descending secondary** — the framework `<order>`, NOT a bespoke client re-sort (project constraint).

## Field values (as-built, from `CommunicationEnrichmentService`)
- `TriagePriorityOptionSetValues`: Urgent=100000000, High=100000001, Medium=100000002, Low=100000003.
- `ReviewOutcomeOptionSetValues`: File=100000000, Update=100000001, Route=100000002, Dismiss=100000003, Pending=100000004.
- `sprk_triagecategory` is a **lookup** (EntityReference → taxonomy); the cell renders the formatted name. `sprk_riconfidence` is a double 0..1 (task 024/025 RI-confidence scorer).

## Notes
- The `ReconciliationGrid.test.tsx` (050) inline fixture is unaffected — it decouples from the JSON deploy artifact by design; the triage renderers are covered by the new `triageColumnRenderers.test.tsx`.
- Live visual dark-mode contrast is jsdom-verified only; worth a browser pass at Pillar E deploy (059).
- `NEEDS_REVIEW_CONFIG_ID` placeholder GUID still pending real seeded id (task 059) — unchanged by 051.
