# Task 022 — Deviations

> **Created**: 2026-06-01 by task 022

---

## Deviations from original POML prompt

### 1. UQ-05 (Mark Paid) — deferred to R2

- **Original POML**: "commandBar includes custom 'Mark Paid' handler + Export"
- **Actual**: `commandBar` contains only standard defaults (`newRecord`, `refresh`, `exportExcel`); no custom Mark Paid handler
- **Rationale**: User explicitly delegated decisions to defaults earlier in the conversation. R1 scope is restricted to defaults; custom Mark Paid command + handler registration deferred to R2.
- **Recorded in**: `022-mark-paid-decision.md`
- **Impact on task 023**: Custom Page host (task 023) does NOT need to register a Mark Paid command handler. Simplifies host bootstrap.

### 2. `source.type` — `savedquery` (single) not `savedquery-set` (auto-discover)

- **Original POML**: "configjson v1.0: `source.type = 'savedquery-set'` (auto-discover all sprk_invoice savedqueries) per design.md Appendix"
- **Actual**: `source.type = "savedquery"` with explicit `savedQueryId` reference to task 020's created savedquery (`b9f6d045-9a5e-f111-ab0c-7c1e521545d7`)
- **Rationale**: Parent task prompt directed this final shape, matching task 021's pattern. A single matter-context savedquery is sufficient for R1; auto-discovery via `savedquery-set` is unnecessary complexity for this drill-through.
- **Impact**: None on downstream tasks; framework's IDataverseClient resolves a single savedQueryId identically to one element of a savedquery-set.

### 3. SecondaryAction visibility conditions — unconditional (no state-machine filtering)

- **Original POML**: implied conditional visibility based on Invoice status
- **Actual**: Both secondary actions (`ask-sprkchat-invoice`, `review-playbook`) are unconditional (no `visibleWhen` clause)
- **Rationale**: `sprk_invoice` does not currently expose a state-machine optionset (`sprk_invoicestatus`) suitable for visibility filtering. Refinement deferred to post-UAT once status semantics are confirmed.
- **Impact**: Buttons render whenever a single row is selected, regardless of invoice state.

---

## No deviation on these items

- Schema version (`_version: "1.0"`) — matches
- `display`, `filterChips`, `rowOpen`, `behavior` shape — matches `DataGridConfiguration` v1.0 interface
- `parentContextFilter` overlay — matches framework convention introduced in commit `fe4f675d` / task 020 D-020-02
- Acceptance criteria 1-3 — all PASS (see task report)
