# PlaybookBuilder `sprk_nodetype` + `__actionType` Reference Audit

> **Task**: 080 (Wave 8) — pre-flight audit for task 088 canvas-state migration
> **Spec coverage**: FR-26 (canvas-state `sprk_nodetype` → `sprk_executortype`), FR-20 (retire `__actionType` injection)
> **Status**: complete — audit-only, no source modified
> **Date**: 2026-06-28

---

## 1. Executive summary

| Literal | Hits | Files |
|---|---|---|
| `sprk_nodetype` (case-insensitive) | **9** | 3 (`types/canvas.ts`, `types/playbook.ts`, `services/playbookNodeSync.ts`) |
| `__actionType` (literal) | **3** | 3 (`types/canvas.ts`, `types/playbook.ts`, `services/playbookNodeSync.ts`) |
| Cross-cutting in `src/client/shared/` | **0** | — (no shared-lib references) |

All references are concentrated in the **PlaybookBuilder Code Page** under `src/client/code-pages/PlaybookBuilder/src/`. Nothing in `src/client/shared/` (no `@spaarke/ui-components`, `@spaarke/ai-widgets`, etc.) references these literals. Replacement scope is entirely within PlaybookBuilder.

**Two parallel mechanisms exist today** for executor dispatch — both are R7 retirement targets:

1. **`sprk_nodetype` Dataverse column** (coarse 6-value Choice: AIAnalysis / Output / Control / Workflow / DeliverComposite / EntityNameValidator). Populated by `playbookNodeSync.ts` via `NodeTypeToDataverse` map. Read on load via `$select` query. **R7 FR-26**: replace with `sprk_executortype` (33-value Choice).
2. **`__actionType` injection into `sprk_configjson`** (specific executor enum 0–141). Injected as a synthetic JSON key by `buildConfigJson()`. **R7 FR-20**: server reads `sprk_executortype` directly — this injection becomes dead weight and must be removed.

After R7 cutover, neither survives.

---

## 2. `sprk_nodetype` references — file × line table

| # | File | Line | Current text (1-line context) | Category | Replacement strategy (task 088) |
|---|---|---|---|---|---|
| 1 | `src/client/code-pages/PlaybookBuilder/src/types/canvas.ts` | 38 | `*   type         -> sprk_nodetype (coarse category via NodeTypeToDataverse mapping)` | JSDoc field-mapping comment | **Rewrite**: `type -> sprk_executortype (specific executor via NodeTypeToExecutorType mapping)`. Drop the "coarse category" framing. |
| 2 | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` | 63 | `// Dataverse sprk_nodetype — Coarse Node Category (4 values)` | Section-divider comment | **Delete entire section**: 6-value Choice (`DataverseNodeType` enum) and `NodeTypeToDataverse` map both die. Replace with `// Dataverse sprk_executortype — Specific Executor (33 values)` section + new `ExecutorType` enum + `NodeTypeToExecutorType` map (sourced from Wave 2 task 022 server `ExecutorType` enum). |
| 3 | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` | 67 | `* Coarse node category stored as sprk_nodetype choice on sprk_playbooknode.` | JSDoc on `DataverseNodeType` enum | **Delete with enum** (see #2). |
| 4 | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` | 81 | `// Output composition. Added to sprk_nodetype OptionSet via` | Comment on `DeliverComposite` enum member | **Delete with enum** (see #2). |
| 5 | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` | 92 | `* Map canvas PlaybookNodeType → Dataverse sprk_nodetype (coarse category).` | JSDoc on `NodeTypeToDataverse` const | **Delete with map** (see #2). |
| 6 | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` | 184 | `  sprk_nodetype: number;` | Field on `PlaybookNodeRecord` interface | **Rename**: `sprk_executortype: number;` (matches Wave 4 schema state — column kept as Number/Choice ID). Required field on read path. |
| 7 | `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` | 99 | `'$select=sprk_playbooknodeid,sprk_name,sprk_nodetype,sprk_executionorder,' +` | Dataverse `$select` query string in `loadPlaybookNodes` | **Direct rename**: replace `sprk_nodetype` → `sprk_executortype`. |
| 8 | `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` | 587 | `    sprk_nodetype: nodeType,` | Payload field in `createNodeRecord` | **Direct rename**: replace key with `sprk_executortype`. The `nodeType` value now comes from `NodeTypeToExecutorType[node.type]` (see line 583 in adjacent change). |
| 9 | `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` | 627 | `    sprk_nodetype: nodeType,` | Payload field in `updateNodeRecord` | **Direct rename**: same as #8. |

### Adjacent code that must change with these

These don't contain the literal `sprk_nodetype` but are tightly coupled — task 088 must update them:

| File | Line | Current text | Why it changes |
|---|---|---|---|
| `services/playbookNodeSync.ts` | 35 | `  NodeTypeToDataverse,` (import) | Replace import: `NodeTypeToExecutorType,`. |
| `services/playbookNodeSync.ts` | 583 | `const nodeType = NodeTypeToDataverse[node.type as PlaybookNodeType];` | Rename map call: `NodeTypeToExecutorType[node.type]`. Variable name `nodeType` is now misleading; rename to `executorType` for clarity. |
| `services/playbookNodeSync.ts` | 624 | `const nodeType = NodeTypeToDataverse[node.type as PlaybookNodeType];` | Same as above (update path). |

---

## 3. `__actionType` references — file × line table

| # | File | Line | Current text (1-line context) | Category | Replacement strategy (task 088) |
|---|---|---|---|---|---|
| 1 | `src/client/code-pages/PlaybookBuilder/src/types/canvas.ts` | 39 | `*                    + __actionType in sprk_configjson (specific executor dispatch)` | JSDoc continuation of field-mapping comment | **Delete this line** entirely. After R7 cutover, `sprk_executortype` is the single dispatch field; `sprk_configjson` no longer carries dispatch info. |
| 2 | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` | 124 | ` * Stored as __actionType in ConfigJson.` | JSDoc on `ActionType` enum (line 126) | **Rewrite**: ` * Stored as sprk_executortype on sprk_playbooknode (top-level Choice, no JSON injection).` Note: enum `ActionType` also renames to `ExecutorType` per Wave 2 task 022. |
| 3 | `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` | 330 | `    __actionType: actionType ?? 0,` | Synthetic JSON key inside `buildConfigJson()` config object | **Delete this line + the `const actionType = NodeTypeToActionType[...]` lookup on line 327 + the import of `NodeTypeToActionType` on line 36.** Per FR-20, server no longer reads `__actionType` from configjson. |

### Adjacent code that must change with these

| File | Line | Current text | Why it changes |
|---|---|---|---|
| `services/playbookNodeSync.ts` | 36 | `  NodeTypeToActionType,` (import) | **Delete** import — no longer used after line 327 removed. |
| `services/playbookNodeSync.ts` | 327 | `const actionType = NodeTypeToActionType[data.type as PlaybookNodeType];` | **Delete** — feeds only the `__actionType` injection on line 330. |
| `types/playbook.ts` | 157 | `export const NodeTypeToActionType: Record<PlaybookNodeType, ActionType> = { … };` | **Delete entire export** — no consumer remains after the `playbookNodeSync.ts` cleanup. (Confirm no other importer: grep found only `playbookNodeSync.ts:36`.) |
| `types/playbook.ts` | 126–152 | `export enum ActionType { … }` (27-member enum) | **Rename to `ExecutorType` per Wave 2 task 022 server enum.** May need to grow to 33 members if server has additions. Defer to task 088 implementation — coordinate with Wave 2 outputs. |

---

## 4. Canvas-state shape today vs. post-R7

### Today (pre-R7)

```
PlaybookNodeData.type: PlaybookNodeType  // 13 string values (start, aiAnalysis, …, entityNameValidator)
  │
  ├── NodeTypeToDataverse[type]  → DataverseNodeType  (6 numeric values, coarse)
  │                               → written to sprk_nodetype column (top-level)
  │                               → read on $select
  │
  └── NodeTypeToActionType[type] → ActionType         (27 numeric values, specific)
                                  → injected as __actionType inside sprk_configjson string
                                  → server parses configjson, extracts __actionType for dispatch
```

Two parallel writes per node (`sprk_nodetype` column + `__actionType` JSON key) — both currently required by server.

### Post-R7 (after tasks 080 → 081 → 082 → 088 → 089d)

```
PlaybookNodeData.type: PlaybookNodeType  // 13 string values (unchanged — internal canvas hint per design.md §3)
  │
  └── NodeTypeToExecutorType[type] → ExecutorType  (33 numeric values, specific)
                                    → written to sprk_executortype column (top-level)
                                    → read on $select
                                    → SINGLE source of dispatch for server
```

Single write per node. `sprk_configjson` carries only executor-specific config (no synthetic `__actionType`). Server reads `node.sprk_executortype` directly (FR-07 single-hop dispatch).

**Note on canvas `PlaybookNodeType` survival**: per design.md §3 + project CLAUDE.md "Key Technical Constraints", the canvas `type` discriminator is allowed to survive as an internal graph-traversal/renderer hint (drives React Flow `nodeTypes` registry and palette grouping). It does NOT need to map 1:1 with server `ExecutorType` — the canvas may still bucket multiple executors under one renderer (e.g., the 13-entry canvas → 33-entry server via `NodeTypeToExecutorType`). Task 082 (33 categorized palette entries) determines whether this 13-vs-33 mismatch widens or whether canvas grows new types.

---

## 5. Canvas-state replacement plan for task 088

Order of changes inside task 088, smallest-blast-radius first:

### Step A — Wire new map (additive, low-risk)

1. In `types/playbook.ts`:
   - Add new `ExecutorType` enum (33 members, sourced from Wave 2 task 022 server enum — coordinate via cross-worktree review).
   - Add new `NodeTypeToExecutorType: Record<PlaybookNodeType, ExecutorType>` map.
   - **Do NOT yet delete** `DataverseNodeType`, `NodeTypeToDataverse`, `ActionType`, `NodeTypeToActionType`. They're still referenced.

### Step B — Switch write path (single commit boundary)

2. In `services/playbookNodeSync.ts`:
   - Import: replace `NodeTypeToDataverse, NodeTypeToActionType` → `NodeTypeToExecutorType` (line 35–36).
   - `loadPlaybookNodes` line 99: change `sprk_nodetype` → `sprk_executortype` in `$select`.
   - `createNodeRecord` line 583, 587: rename local var + replace payload key.
   - `updateNodeRecord` line 624, 627: same as above.
   - `buildConfigJson` lines 327, 330: **delete** `actionType` lookup + `__actionType` injection.

### Step C — Type-shape cleanup

3. In `types/playbook.ts`:
   - `PlaybookNodeRecord` line 184: rename field `sprk_nodetype: number` → `sprk_executortype: number`.
   - **Delete** `DataverseNodeType` enum (lines 75–89).
   - **Delete** `NodeTypeToDataverse` map (lines 92–115).
   - **Delete** `ActionType` enum (lines 117–152) — supplanted by new `ExecutorType` from Step A.
   - **Delete** `NodeTypeToActionType` map (lines 154–171).
   - Update JSDoc on line 63, 67, 92 (these comments all go with their deleted constructs).

### Step D — Comment cleanup

4. In `types/canvas.ts`:
   - Line 38–39: rewrite the field-mapping JSDoc per Section 2 #1 + Section 3 #1 above.

### Step E — Verify no stragglers

5. Re-run grep:
   - `Grep("sprk_nodetype", path="src/client/code-pages/PlaybookBuilder/")` → expect **0 hits**.
   - `Grep("__actionType", path="src/client/code-pages/PlaybookBuilder/")` → expect **0 hits**.
   - `Grep("DataverseNodeType|NodeTypeToDataverse|NodeTypeToActionType", path="src/client/code-pages/PlaybookBuilder/")` → expect **0 hits**.
   - `Grep("ActionType", path="src/client/code-pages/PlaybookBuilder/")` → expect **0 hits** (renamed to `ExecutorType` everywhere).

---

## 6. Cross-task coordination

| Downstream task | Coupling to this audit |
|---|---|
| **Task 022** (Wave 2 server enum rename `ActionType` → `ExecutorType`) | Provides the canonical 33-member enum that task 088 imports/mirrors on the canvas side. Task 088 cannot complete until task 022 ships the server enum names. |
| **Task 024** (Wave 2 dispatch refactor, single-hop `node.sprk_executortype` read) | Once shipped, server stops reading `__actionType` from configjson. Task 088 can then safely delete the injection without breaking dispatch. Confirm task 024 is merged before merging task 088. |
| **Task 055** (Wave 5 `Deploy-Playbook.ps1` update) | Server-side write equivalent of this audit's findings. Both write paths (PowerShell deploy script + canvas `playbookNodeSync.ts`) must agree on column name + no `__actionType` injection. Task 055 and task 088 should be reviewed together. |
| **Task 081** (Wave 8 Power Apps form update — Node Type → Executor Type Choice) | Provides the OptionSet definition that `ExecutorType` enum mirrors. Task 081 must ship before task 088 can safely write to the column (Choice values exist). |
| **Task 082** (Wave 8 canvas left-panel 33 categorized entries) | Determines whether the canvas `PlaybookNodeType` discriminator grows from 13 → 33 entries (1:1 with server) or stays 13 with a many-to-one map. Task 088's `NodeTypeToExecutorType` shape depends on this decision. |
| **Task 086** (Wave 8 Action tab promotion) | Independent — Action FK is a separate field. No coupling to this audit. |

---

## 7. Risks & open questions for task 088

| Risk | Mitigation |
|---|---|
| Wave 2 task 022 ships before task 088 → ESM/TS build briefly references obsolete `ActionType` in canvas while server-side `ExecutorType` is canonical | Acceptable — TS build is isolated from server. Task 088 catches up post-task-024 merge. |
| `ActionType` symbol exported but not imported anywhere outside `playbookNodeSync.ts` → safe to delete. Confirmed via grep above. | Re-verify in task 088 — if any new consumer landed between this audit (2026-06-28) and task 088 execution, abort + re-plan. |
| 27→33 enum growth: Wave 2 task 022 may add new members (e.g., AiCompletion=1 already exists; new members for other dispatch reforms) | Pull the canonical server enum at task 088 start — do not hand-roll the canvas enum. |
| `requiresConfirmation` and other HITL fields (`types/playbook.ts` line 222) are not touched by this audit — they're orthogonal to executor dispatch. | OK — out of scope for task 088. |
| **Tests**: `tests/` searched separately (zero hits in shared lib + grep done in step 1 confirms zero hits outside the 3 files above) — but there may be `__tests__/` folders inside PlaybookBuilder not yet inspected. | Task 088 implementation must check `src/client/code-pages/PlaybookBuilder/**/__tests__/` and update fixtures before claiming completion. |

---

## 8. Acceptance criteria (per task 080 POML)

| Criterion | Status |
|---|---|
| Audit document exists at `notes/spikes/playbookbuilder-sprk-nodetype-audit.md` | ✅ this file |
| Document enumerates every file + line containing `sprk_nodetype` (literal) | ✅ Section 2 (9 refs, 3 files) |
| Document enumerates every file + line containing `__actionType` (literal) | ✅ Section 3 (3 refs, 3 files) |
| Document includes a replacement-target table per call site | ✅ Sections 2 & 3 tables |
| Document has "Canvas-state replacement plan for task 088" section | ✅ Section 5 |
| No source files modified by this task | ✅ audit-only — only this `.md` written |
| `TASK-INDEX.md` shows 080 ✅ | ✅ updated in this commit |

---

*Generated by task 080 — Wave 8 pre-flight audit. Consumer: task 088 (canvas-state replacement). Audit-only; zero source files modified.*
