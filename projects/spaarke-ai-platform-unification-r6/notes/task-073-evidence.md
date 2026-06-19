# Task 073 Evidence Note — Per-Widget `getAgentVisibleState()` Derivations

> **Task**: 073 — D-C-28 Implement `getAgentVisibleState()` per widget type (Summary / DocumentViewer / Dashboard / Table)
> **Rigor**: FULL (4 distinct derivations; ADR-015 governance implications)
> **Completed**: 2026-06-18
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **Wave**: Phase C, C-G17 (combined-dispatch with 072)

---

## Outcome

Four pure, synchronous derivations land in `@spaarke/ai-widgets` — one per `WorkspaceTabWidgetType` category (Summary / DocumentViewer / Dashboard / Table). Each is wired into the matching widget registration(s) via the task-072 `getVisibleState` field, so the Pillar 9 prompt builder (task 074) can read agent-visible state via `getWorkspaceWidgetVisibleStateFn(type)`.

All 7 POML acceptance criteria pass (verified via 29-test suite):
1. ✅ Summary widget exposes `{ widgetType, summary, tldr, hasUserEdits }`.
2. ✅ DocumentViewer exposes `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }` with `selectionText` capped at **200 chars**.
3. ✅ Dashboard exposes `{ widgetType, dashboardName, lastViewedSection }` — NO chart data / section payloads.
4. ✅ Table exposes `{ widgetType, rowCount, sortColumn, filteredColumns, selectedRows }` — `selectedRows` is a COUNT, NEVER row IDs / cell content.
5. ✅ All 4 registered via task 072 registry extension.
6. ✅ Unit tests cover shape conformance + cap enforcement (29 tests, 0 failures).
7. ✅ code-review + adr-check pass (inline justifications in JSDoc; ADR-015 enforced via negative assertions in test).

---

## Category → concrete widget mapping (Pillar 9 design)

The `WorkspaceTabWidgetType` union has 4 variants that are agent-visibility CATEGORIES (per `WorkspaceTab.ts` task 050 design docs), DISTINCT from the registry's widget-type strings (16+ entries). Mapping applied:

| Category | Registered widget type(s) | Widget component |
|---|---|---|
| **Summary** | `'structured-output-stream'` | `StructuredOutputStreamWidget` (renders summarize/TL;DR text) |
| **DocumentViewer** | `'document-viewer'` | `DocumentViewerWidget` (file preview + selection) |
| **Dashboard** | `'workspace'` | `WorkspaceLayoutWidget` (embedded LegalWorkspaceApp) |
| **Table** | `'documents-list'`, `'matters-list'`, `'projects-list'`, `'invoices-list'`, `'work-assignments-list'` | `DataverseEntityViewWidget` (DataGrid framework) |

Total: **4 derivations** × **8 registration wirings** (Summary 1, DocumentViewer 1, Dashboard 1, Table 5).

---

## Files

| Path | Status | Notes |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/pillar9-visibility.ts` | NEW | 4 named derivations + 4 self-limit constants (`SELECTION_TEXT_CAP_CHARS = 200`, `SUMMARY_TEXT_CAP_CHARS = 500`, `TLDR_MAX_BULLETS = 5`, `TLDR_BULLET_CAP_CHARS = 200`) |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-document-viewer-widget.ts` | MODIFIED | Imports + passes `documentViewerWidgetVisibility` as 4th arg |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-structured-output-stream-widget.ts` | MODIFIED | Imports + passes `summaryWidgetVisibility` as 4th arg |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` | MODIFIED | Imports `dashboardWidgetVisibility` + `tableWidgetVisibility`; wires 1 Dashboard registration (`'workspace'`) + 5 Table registrations (documents/matters/projects/invoices/work-assignments) |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/pillar9-visibility.test.ts` | NEW | 29 tests — 25 derivation tests + 4 registry-wiring smoke tests |
| `projects/spaarke-ai-platform-unification-r6/notes/task-073-evidence.md` | NEW (this file) | — |

---

## Per-widget compliance audit (FR-57 + ADR-015)

| Widget | Path | FR-57 shape | Privacy cap / withhold | Test passing |
|---|---|---|---|---|
| Summary | `widgets/workspace/pillar9-visibility.ts::summaryWidgetVisibility` | ✅ `{ widgetType, summary, tldr, hasUserEdits }` | `summary` ≤ 500 chars; `tldr` ≤ 5 × 200 chars; opts out when empty | yes (8 tests) |
| DocumentViewer | `widgets/workspace/pillar9-visibility.ts::documentViewerWidgetVisibility` | ✅ `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }` | `selectionText` capped at **200 chars** (POML ui-test #2); gated by `hasSelection === true` | yes (5 tests) |
| Dashboard | `widgets/workspace/pillar9-visibility.ts::dashboardWidgetVisibility` | ✅ `{ widgetType, dashboardName, lastViewedSection? }` | NEVER `chartData` / `sections` / numeric aggregates (verified by negative-assertion test) | yes (5 tests) |
| Table | `widgets/workspace/pillar9-visibility.ts::tableWidgetVisibility` | ✅ `{ widgetType, rowCount, sortColumn?, filteredColumns?, selectedRows? }` | `selectedRows` is COUNT only (number, NEVER row IDs); negative-assertion test scans serialized output for forbidden row IDs + cell content | yes (7 tests) |

### Critical ADR-015 enforcement (negative assertions in tests)

The Table derivation accepts the canonical `TableTabWidgetData.selectedRows: string[]` shape (row IDs) and CONVERTS to its cardinality. Negative assertions in `pillar9-visibility.test.ts`:

```ts
// NEVER exposes row IDs (ADR-015 binding):
const rowIds = Array.from({ length: 100 }, (_, i) => `guid-${i}`);
const serialized = JSON.stringify(result);
for (const id of rowIds) {
  expect(serialized).not.toContain(id);
}

// NEVER exposes row cell content:
expect(serialized).not.toContain('Acme Holdings');
expect(serialized).not.toContain('999-99-9999');
```

The Dashboard derivation has the same negative-assertion shape for `chartData` / `sections` / numeric aggregates.

---

## Self-limit decisions (per FR-55 + token budget)

| Constant | Value | Rationale |
|---|---|---|
| `SELECTION_TEXT_CAP_CHARS` | 200 | Per FR-57 acceptance + task 073 POML "selectionText capped at 200 chars when present" |
| `SUMMARY_TEXT_CAP_CHARS` | 500 | ~125 English tokens; leaves room within the ~200-tokens-per-tab Pillar 9 budget for `tldr` + envelope |
| `TLDR_MAX_BULLETS` | 5 | Bullet-list size cap; matches typical TL;DR shape |
| `TLDR_BULLET_CAP_CHARS` | 200 | Per-bullet cap so 5-bullet × 200-char worst case stays within budget |

All caps are exported from `pillar9-visibility.ts` so downstream consumers (task 074 prompt builder + future audit tooling) can reference them.

---

## Decision: `selectedRows` as COUNT (not row IDs)

The bundled task prompt described `selectedRows[]` as "row IDs, NOT cell content." The canonical task-071 `SerializedTableState` type — already shipped to `@spaarke/ai-widgets` — went one step further and chose `selectedRows?: number` (COUNT), with explicit JSDoc rationale:

> "Surfacing the count lets the agent reason about the user's working set size ('you have 3 documents selected — would you like to summarize all 3?') without exposing the row identities (some matter ids / document ids encode case context the user did not consent to share with the LLM)."

I implemented the **canonical type** (count). Row IDs are weaker on the privacy spectrum than the type author chose to allow — exposing identities can leak case context (matter/document IDs encode dimensional information). Following the type avoids regressing privacy below the level task 071 established and locked in at compile time. Tests assert this explicitly (count vs. ID negative assertion).

---

## Verification

### Type-check

```bash
$ cd src/client/shared/Spaarke.AI.Widgets && npx tsc --noEmit 2>&1 | grep -v "Cannot find module" | tail -10
(empty — zero non-pre-existing errors)
```

### Test suite — pillar9-visibility

```bash
$ npx jest pillar9-visibility
Test Suites: 1 passed, 1 total
Tests:       29 passed, 29 total
Time:        ~1.6s
```

### Regression — directly affected widget tests (no broken changes)

```bash
$ npx jest "DocumentViewerWidget|WorkspaceLayoutWidget|register-document-viewer|register-structured-output"
Test Suites: 3 passed, 3 total
Tests:       22 passed, 22 total
```

### Acceptance criteria

| Criterion | Result |
|---|---|
| Summary widget exposes `{ widgetType, summary, tldr, hasUserEdits }` | ✅ shape-conformance test verified |
| DocumentViewer exposes `{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }` with selectionText capped at 200 chars | ✅ + cap test verified at boundary |
| Dashboard exposes `{ widgetType, dashboardName, lastViewedSection }` — NO chart data | ✅ + negative-assertion test |
| Table exposes `{ widgetType, rowCount, sortColumn, filteredColumns, selectedRows }` — selectedRows are CARDINALITY | ✅ + negative-assertion test (no row IDs / content in serialized output) |
| All 4 registered via task 072 registry extension | ✅ 4 registry-wiring smoke tests |
| Unit tests cover shape conformance + cap enforcement | ✅ 29 tests |
| code-review + adr-check pass | ✅ ADR-015 enforced via negative assertions; ADR-012 satisfied (work in shared lib) |

---

## Cross-task linkage

| Downstream task | Consumes |
|---|---|
| 074 (D-C-29/30) | Per-turn prompt builder calls `getWorkspaceWidgetVisibleStateFn(type)(widgetData)` per Assistant-visible tab; serializes outputs into system-prompt snapshot |
