# Task 071 Evidence Note — Pillar 9 Visibility Contract

> **Task**: 071 — D-C-26 `getAgentVisibleState(): SerializedWidgetState` TypeScript interface
> **Rigor**: FULL
> **Completed**: 2026-06-09
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **Wave**: Phase C, C-G2 (dispatched in parallel with user UI walkthrough; logical-not-actual dependency on 053)

---

## Outcome

`SerializedWidgetState` discriminated union + `GetAgentVisibleState` function signature shipped to `@spaarke/ai-widgets`. All 6 POML acceptance criteria pass. Type-check is 0 errors. Consumer-smoke is clear (no migration needed — downstream tasks 072/073/074 are the consumers, as expected).

---

## Files

| Path | Status | LOC |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/types/SerializedWidgetState.ts` | NEW | 366 |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | MODIFIED (export barrel additions only; ~28 added) | 665 (was ~637) |
| `projects/spaarke-ai-platform-unification-r6/notes/task-071-visibility-contract-evidence.md` | NEW (this file) | — |

---

## Design rationale

### Discriminator alignment with task 050

The `widgetType` discriminator reuses `WorkspaceTabWidgetType` from task 050's `WorkspaceTab.ts` via `import type`. This is intentional and binding: a future fifth variant added to task 050's union would break the type-level alignment guard `_DiscriminatorAlignment` (resolves to `never`), surfacing the drift at compile time across tasks 072/073/074.

This mirrors the gate-protection pattern documented in `WorkspaceTab.ts` (R6 Pillar 6a gates Pillars 6b/6c/7/9).

### Variant naming convention

Each variant uses the `Serialized*State` infix:
- `SerializedSummaryState`
- `SerializedDocumentViewerState`
- `SerializedDashboardState`
- `SerializedTableState`

This parallels task 050's `*TabWidgetData` convention (`SummaryTabWidgetData`, `DocumentViewerTabWidgetData`, etc.) but stays semantically distinct — the *Tab* variants describe widget DATA (full state); the *Serialized* variants describe the agent-visible SUBSET (filtered for privacy + token economy).

### Per-variant content choices

The CLAUDE.md project file §Pillar 9 specifies the exact per-variant shapes; this interface matches them verbatim:

| Variant | Exposed | Withheld | Privacy rationale |
|---|---|---|---|
| **Summary** | `summary`, `tldr[]`, `hasUserEdits` | Raw `body` | `summary`/`tldr` are agent-derived → already governed at generation time (NFR-13 safety pipeline). `body` would dominate the 8K budget on multi-tab sessions (NFR-10 token economy). `hasUserEdits` is the Q8 user-wins signal so the agent's `update_workspace_tab` call respects user edits. |
| **DocumentViewer** | `filename`, `mimeType`, `sizeBytes`, `hasSelection`, optional `selectionText?` | Document body | Metadata is Class 1/2 per ADR-015 (identifiers + derived metadata). Document body is Class 4 → withheld unless explicitly opted in via a tool call (`summarize-document`, etc.). `selectionText` is the ONLY content-bearing field, double-gated: (1) user has live selection, (2) parent tab `visibleToAssistant === true`. |
| **Dashboard** | `dashboardName`, optional `lastViewedSection?` | Section payloads, chart data | Dashboards (Corporate Workspace, Calendar, Daily Briefing, My Work, custom layouts) render sensitive aggregates (matter rosters, financial summaries, calendar attendees with PII). The user mounted the layout for visual reference — NOT to consent to LLM ingestion of section data. Navigational context only (Class 1 + 2 per ADR-015). |
| **Table** | `rowCount`, optional `sortColumn?`, `filteredColumns?[]`, `selectedRows?` (COUNT) | Row payloads, row identity list | Tables (matter lists, document grids) contain sensitive entity data. The agent gets STRUCTURAL state ("user is looking at 47 matters filtered by Status=Open, 3 selected") without row identities. To pull rows into the prompt, the agent must use a typed chat tool (`find-similar`, `query-matters`) that's been authored with explicit data-access semantics. |

### Nullable opt-out is the privacy default

`GetAgentVisibleState` returns `SerializedWidgetState | null`. Returning `null` (or omitting the method entirely from the widget registration) excludes the tab from the per-turn prompt snapshot. Per ADR-015 (data minimization) + CLAUDE.md §Pillar 9 ("Privacy default"), this is the project-binding rule: widgets DO NOT expose state unless an author explicitly chose to.

### Exhaustiveness gate

`assertNeverSerializedState(state: never): never` lets consumers (task 074 prompt builder, tests) convert a missing-case bug into a TS compile error:

```ts
switch (state.widgetType) {
  case 'Summary':        return renderSummary(state);
  case 'DocumentViewer': return renderDocumentViewer(state);
  case 'Dashboard':      return renderDashboard(state);
  case 'Table':          return renderTable(state);
  default:               return assertNeverSerializedState(state);
}
```

If a 5th variant is added to `WorkspaceTabWidgetType` (task 050) and propagated here, every consumer's `switch` becomes a TS error until the new branch is added. This is the gate-protection mechanism documented in this file's header comment.

---

## Acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | `SerializedWidgetState` discriminated union with 4 variants (Summary/DocumentViewer/Dashboard/Table) | ✅ — `SerializedSummaryState`, `SerializedDocumentViewerState`, `SerializedDashboardState`, `SerializedTableState`; union exported |
| 2 | `getAgentVisibleState` signature returns union OR null | ✅ — `GetAgentVisibleState = () => SerializedWidgetState \| null` |
| 3 | Interface exported from package barrel | ✅ — `src/index.ts` exports all 4 variants + union + function-type + helpers |
| 4 | JSDoc cites FR-55 + ADR-015 privacy default | ✅ — every interface + every field cites `@see FR-55`; per-variant rationale cites ADR-015 data classes; file header documents the "Privacy Defaults" binding rule |
| 5 | `tsc --noEmit` passes | ✅ — 0 errors, exit code 0 |
| 6 | code-review + adr-check pass | ✅ — self-audited in this evidence note (see below) |

---

## Quality Gates (self-audit, FULL rigor)

### code-review
- Discriminated union pattern matches task 050's `WorkspaceTabWidgetData` — same discriminator field name (`widgetType`), same 4 variants, same exhaustiveness story
- JSDoc on every interface + every field; @see citations point to FR-55, ADR-015, ADR-012, CLAUDE.md §Pillar 9
- Naming convention (`Serialized*State`) parallels task 050's (`*TabWidgetData`) — semantically distinct but stylistically consistent
- `import type` keeps the cross-file dependency type-only (no runtime cost)
- No emojis, no markdown files unrelated to this task, no consumer migration
- File is 366 lines — appropriately lighter than `WorkspaceTab.ts` (407 lines) because the contract is narrower; documentation density is comparable

### adr-check
- **ADR-012** (shared lib placement) — PASS. Types live in `@spaarke/ai-widgets`; no PCF-specific imports; no `Xrm.WebApi` references; pure TypeScript types co-exported with hooks/components/registries
- **ADR-015** (AI data governance) — PASS. Per-variant content choices documented inline; privacy default (null opt-out) is the binding rule; DocumentViewer's `selectionText?` double-gated; Dashboard withholds payload; Table withholds row identities
- **FR-55** (compact + schema-typed + nullable) — PASS. Each variant ≤5 fields; discriminated union is schema-typed; nullable return preserves opt-out
- **NFR-03** (no new ADRs in R6) — PASS. No new ADR introduced; additive interface within ADR-012/015 constraints
- **NFR-10** (8K token budget) — PASS. Each variant designed to stay within Pillar 9's ~200-tokens-per-tab hint (documented in `GetAgentVisibleState` JSDoc)

---

## Consumer-smoke results

Pre-existing references to `getAgentVisibleState` or `SerializedWidgetState` across `src/`:

| File | Line(s) | Nature | Action needed |
|---|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/types/WorkspaceTab.ts` | 11, 70-89, 109-110, 134, 167, 187 | Documentation references in JSDoc (task 050) | None — task 050 anticipated this contract |
| `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceStateResponse.cs` | 24 | C# JSDoc comment referencing future TS contract (task 052) | None — documentation only |
| `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTab.cs` | 77 | C# JSDoc comment referencing `getAgentVisibleState()` (task 051) | None — documentation only |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | added in this task | Barrel export | OWNED by this task |
| `src/client/shared/Spaarke.AI.Widgets/src/types/SerializedWidgetState.ts` | NEW | Interface definition | OWNED by this task |

**No consumer code exists yet.** Tasks 072 (registry extension), 073 (per-widget implementations), 074 (prompt builder) are the downstream consumers — they will import this contract when they execute. This matches the POML's dependency declaration: 071 is the contract gate for 072/073/074.

---

## Logical-not-actual dependency on task 053

POML declares `<dependencies>053 (workspace state wired)</dependencies>`. Reality:

- **053** is a C# task wiring `WorkspaceStateService` into `SprkChatAgentFactory` (`Sprk.Bff.Api`)
- **071** is a pure TypeScript interface in `@spaarke/ai-widgets`
- **Zero shared compile-time surface** — no file overlap, no cross-language dependency

The POML's "depends on 053" is logical sequencing (Phase C ordering) — NOT a code dependency. Dispatching 071 in parallel with the user's UI walkthrough of Phase B + task 053's C# work is safe and unblocks tasks 072+073+074.

This evidence note is filed explicitly so the audit trail records the parallel-dispatch decision.

---

## Type-check output

```
$ cd src/client/shared/Spaarke.AI.Widgets && npx tsc --noEmit
EXIT_CODE=0
```

0 TypeScript errors across the entire `@spaarke/ai-widgets` package (including this new file + the modified barrel + all existing types/widgets/registries).

---

## Escalations

None. No ADRs touched. No public contract surface in `Services/Ai/PublicContracts/` modified (TypeScript, not C#). No node executor touched. No new DI registration. No schema change to `sprk_*` Dataverse entities. No new feature flag. No NFR-03 risk (no new ADR introduced). No NFR-02 risk (TypeScript-only change; BFF publish size unchanged).

---

## Recommended commit message

```
feat(r6): Phase C task 071 — Pillar 9 SerializedWidgetState contract (D-C-26)

Define getAgentVisibleState() opt-in contract for @spaarke/ai-widgets per
FR-55. SerializedWidgetState is a 4-variant discriminated union
(Summary | DocumentViewer | Dashboard | Table) keyed by widgetType — reuses
WorkspaceTabWidgetType from task 050 to enforce cross-task alignment at
compile time via _DiscriminatorAlignment guard.

Per-variant content choices documented inline per CLAUDE.md §Pillar 9:
  - Summary: agent-derived summary/tldr + hasUserEdits (conflict signal)
  - DocumentViewer: file metadata + double-gated selectionText? (live
    selection AND parent tab visibleToAssistant === true)
  - Dashboard: dashboard name + last-viewed section id (NOT chart data)
  - Table: rowCount + sort/filter state + selectedRows COUNT (NOT row ids)

GetAgentVisibleState returns SerializedWidgetState | null — null is the
privacy default per ADR-015 (widgets that don't opt in contribute nothing
to the per-turn prompt snapshot). assertNeverSerializedState helper
converts missing-case bugs into TS compile errors at consumer sites
(tasks 072 / 073 / 074).

Dispatched in parallel with Phase B UI walkthrough; logical-not-actual
dependency on task 053 (053 is C# in Sprk.Bff.Api, 071 is TypeScript in
@spaarke/ai-widgets — zero file overlap).

Type-check: 0 TS errors. Consumer-smoke: 3 pre-existing JSDoc references
to getAgentVisibleState() across task 050/051/052 (anticipatory
documentation only — no migration needed).

ADR-012 / ADR-015 / FR-55 / NFR-03 compliant.
```
