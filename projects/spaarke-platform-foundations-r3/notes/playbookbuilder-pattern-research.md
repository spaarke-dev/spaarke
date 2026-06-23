# PlaybookBuilder Pattern Research (Task R3-090)

> **Purpose**: Survey existing PlaybookBuilder componentry to identify reuse seams for H2 affordances (tasks 091, 092, 093). Per spec **Q5** owner directive — **do NOT invent new patterns**; extend the existing per-ActionType form / validation / dialog architecture.
>
> **Authored**: 2026-06-21 · **Task**: `090-playbookbuilder-pattern-research.poml` · **Workstream**: P9 (Part 3 H2 — Builder validation + UI affordances) · **Inputs to**: 091, 092, 093
>
> **All file paths relative to** `src/client/code-pages/PlaybookBuilder/src/`.

---

## Section 1 — Methodology

Files surveyed in full (Read tool, full content):

| File | Lines |
|---|---|
| `components/properties/index.ts` | barrel — 27 lines |
| `components/properties/NodePropertiesForm.tsx` | 462 lines — accordion-based properties panel |
| `components/properties/NodePropertiesDialog.tsx` | 517 lines — fixed-size modal variant (tabs) |
| `components/properties/LookupUserMembershipForm.tsx` | 242 lines — task 043 canonical per-ActionType form |
| `components/properties/ConditionEditor.tsx` | 247 lines — condition expression builder |
| `components/properties/VariableReferencePanel.tsx` | 286 lines — upstream output enumeration + clipboard copy |
| `components/properties/NodeValidationBadge.tsx` | 218 lines — error/warning popover badge |
| `components/edges/ConditionEdge.tsx` | 162 lines — `TrueBranchEdge` + `FalseBranchEdge` |
| `components/edges/index.ts` | 34 lines — `edgeTypes` registry + `EDGE_TYPES` constants |
| `components/nodes/BaseNode.tsx` | 215 lines — shared node chrome + handles |
| `components/nodes/ConditionNode.tsx` | 142 lines — two-output condition node (custom handles `id="true"` / `id="false"`) |
| `services/canvasValidation.ts` | 707 lines — rule-based validation engine |
| `services/playbookNodeSync.ts` | (relevant slices) — validation invocation @ save |
| `stores/canvasStore.ts` | (relevant slices) — `onConnect` edge-type dispatch |
| `types/canvas.ts` | 113 lines — `PlaybookNodeData`, `ConditionEdgeData`, `PlaybookEdge`, `CanvasJson` |
| `types/forms.ts` | 60 lines — `NodeFormProps`, `NodeReference`, `VariableEntry`, `NodeValidationResult` |
| `projects/spaarke-platform-foundations-r3/spec.md` | §H2 + Q5 + FR-3H2.1/2/3 (lines 159-170) |

Survey depth: **full read** of every PlaybookBuilder file referenced by tasks 091/092/093 + structural context (BaseNode, ConditionNode, types). **No code modifications made.**

---

## Section 2 — Property-forms pattern (canonical shape)

**Canonical per-ActionType form contract** (defined in `types/forms.ts:16-23`):

```ts
export interface NodeFormProps {
  nodeId: string;
  configJson: string;
  onConfigChange: (json: string) => void;
}
```

**Reference implementation**: `components/properties/LookupUserMembershipForm.tsx` (task 043, the most recently-added form — explicitly designed per Q5 as a pattern-conforming template).

**Pattern shape** (matches every per-ActionType form):

1. **Imports**: Fluent UI v9 (`makeStyles`, `tokens`, `Input`, `Label`, `Switch`, `Text`) from `@fluentui/react-components` — ADR-021 (no hex colors, no v8).
2. **Local config interface** typed to the server executor's contract (e.g. `LookupUserMembershipConfig` mirrors `LookupUserMembershipNodeExecutor`).
3. **`DEFAULT_CONFIG`** constant.
4. **`parseConfig(json: string)`** helper — defensive `JSON.parse` with field-by-field type guards; falls back to `DEFAULT_CONFIG` on any failure (graceful).
5. **`serializeConfig(config)`** helper — `JSON.stringify`.
6. **`memo`-wrapped component** named `<TypeName>Form` (suffix `Form`).
7. **`useMemo`** to compute `config` from `configJson` prop.
8. **`useCallback`** `update(patch: Partial<Config>)` that calls `onConfigChange(serializeConfig({ ...config, ...patch }))`.
9. **Per-field `useCallback` handlers** that call `update({ field: newValue })`.
10. **Styles**: `useStyles` via `makeStyles({ form, field, fieldHint, ... })` using `tokens.spacingVertical*` / `tokens.colorNeutralForeground3` — no inline styles for color.
11. **Field hints** use `<Text className={styles.fieldHint}>` for help text below inputs.
12. **`required` Labels** for required fields (server-validated contract).

**Form registration** is two-fold:
- Export in `components/properties/index.ts` (barrel).
- Wire into BOTH `NodePropertiesForm.tsx` (lines 282-343, `hasTypeForm` allow-list at lines 139-149) AND `NodePropertiesDialog.tsx` (lines 388-446, `hasTypeForm` at lines 164-173). The two panels are intentional dual-presentation surfaces (accordion vs tabbed dialog).

**Canonical fields** lived on `node.data` directly (NOT in configJson) per `types/canvas.ts:42-77`:
- `label`, `outputVariable`, `actionId`, `configJson`, `isConfigured`, `validationErrors`, `timeoutSeconds`, `retryCount`, `conditionJson`, `skillIds`, `knowledgeIds`, `toolIds`, `modelDeploymentId`, `promptSchema`.

All other configuration lives inside `configJson` (per-ActionType opaque blob).

---

## Section 3 — Edges + branch metadata

**Edge type registry** (`components/edges/index.ts:21-33`):

```ts
export const edgeTypes: EdgeTypes = {
  trueBranch: TrueBranchEdge,
  falseBranch: FalseBranchEdge,
};

export const EDGE_TYPES = {
  TRUE_BRANCH: 'trueBranch',
  FALSE_BRANCH: 'falseBranch',
  DEFAULT: 'smoothstep',
} as const;
```

Defined at module scope per `@xyflow/react` v12 rules (avoid re-renders).

**Edge components** (`components/edges/ConditionEdge.tsx`):
- `TrueBranchEdge` — green stroke (`tokens.colorPaletteGreenForeground1`), labeled "True", uses `BaseEdge` + `getBezierPath` + `foreignObject` for the label badge.
- `FalseBranchEdge` — red stroke, labeled "False", same structure.

**Branch metadata persistence** (`types/canvas.ts:83-90`):

```ts
export interface ConditionEdgeData {
  branch: 'true' | 'false';
  conditionLabel?: string;
  [key: string]: unknown;  // react-flow v12 index signature
}
export type PlaybookEdge = Edge<ConditionEdgeData>;
```

**Edge creation seam** — `stores/canvasStore.ts:149-182` (`onConnect`). This is the SINGLE chokepoint where edge type is assigned at connect-time, based on the source node's type and `connection.sourceHandle`:

```ts
onConnect: connection => set(state => {
  let edgeType = 'smoothstep';
  let animated = true;
  let edgeData: PlaybookEdge['data'] | undefined;

  const sourceNode = state.nodes.find(n => n.id === connection.source);
  if (sourceNode?.data.type === 'condition' && connection.sourceHandle) {
    if (connection.sourceHandle === 'true')  { edgeType = 'trueBranch';  edgeData = { branch: 'true'  }; animated = false; }
    if (connection.sourceHandle === 'false') { edgeType = 'falseBranch'; edgeData = { branch: 'false' }; animated = false; }
  }
  return { edges: addEdge({ ...connection, type: edgeType, animated, data: edgeData }, state.edges), isDirty: true };
})
```

**Two-handle ConditionNode** (`components/nodes/ConditionNode.tsx:138-139`) — does NOT use `BaseNode`; declares two source `Handle`s with explicit `id="true"` and `id="false"`. Branch labels render inside the node body (`branches` div).

**ConditionEditor properties form** (`components/properties/ConditionEditor.tsx`) — manages `conditionJson` (NOT `configJson` — condition nodes have their own dedicated field on `PlaybookNodeData.conditionJson`). Schema is:

```jsonc
{ "condition": { "operator": "eq" | "ne" | "gt" | …, "left": "…", "right"?: "…" },
  "trueBranch": "True",   // human-readable branch label
  "falseBranch": "False"
}
```

`trueBranch` / `falseBranch` are **string labels** — they are NOT edge IDs. The actual wiring lives in the canvas edges array; the editor just lets the user rename the human-readable labels shown on the two outputs.

---

## Section 4 — Variable-reference scanning (current state)

Two distinct scanners exist:

**(A) Build-time enumeration** (`VariableReferencePanel.tsx`): given current `nodeId` + all `nodes`, produces a `Map<groupLabel, VariableEntry[]>` of *available* upstream output expressions (`{{nodeName.output.fieldName}}`). Per-type field catalog is hard-coded in `OUTPUT_FIELDS_BY_TYPE` (lines 106-134, e.g. `aiAnalysis` has `result`, `summary`, `entities`, `confidence`). Used to *populate* the clipboard-copyable variable picker — does NOT scan for existing references in other nodes.

**(B) Reference scanning** (`services/canvasValidation.ts:97`):

```ts
const TEMPLATE_REF_RE = /\{\{(output_\w+)\.output\.(\w+)\}\}/g;
```

Used by `extractTemplateRefs(value: string)` (lines 676-693). The regex is **the seam for finding references** — it scans any string for `{{output_<var>.output.<field>}}`.

**Where scanning currently runs** (`canvasValidation.ts` `parseDownstreamNode` at lines 597-671):
- Parses each downstream node's `configJson` — extracts `fieldMappings[].value`, `fields{}` dict values, and known template fields (`template`, `body`, `subject`, `description`).
- Also scans `node.data.template`, `node.data.emailBody`, `node.data.emailSubject` directly.
- Output: `DownstreamNodeInfo[]` containing all `TemplateRef`s found.

**Limitation for task 091**: current scanning is *AI-node-centric* and only walks **outgoing edges 1-hop** from an AI node (`collectDownstreamInfo` at lines 572-591). A rename guard needs to scan **all nodes in the canvas** (not just direct AI-descendants) for any reference to a given `outputVariable`. The scanning *primitives* (`extractTemplateRefs` regex + `parseDownstreamNode` field enumeration) are reusable; the *traversal* is the new piece.

---

## Section 5 — Validation infrastructure

**Public API** (`services/canvasValidation.ts:127-224`):

```ts
export function validatePromptSchemaNodes(nodes: Node<PlaybookNodeData>[], edges: Edge[]): PromptSchemaValidation[]
export function hasValidationErrors(results: PromptSchemaValidation[]): boolean
export function groupValidationsByNode(results): Map<nodeId, { errors: string[]; warnings: string[] }>
```

**Result shape** (lines 27-46):

```ts
interface PromptSchemaValidation {
  nodeId: string;
  severity: 'error' | 'warning';
  rule: 'missing-task' | 'unresolvable-choices' | 'output-coverage'
      | 'choice-consistency' | 'type-compatibility'
      | 'lookup-user-membership-missing-entity-type'
      | 'lookup-user-membership-missing-output-variable';
  message: string;
}
```

**Rule-registration pattern**: each rule is a top-level function `function validate<RuleName>(nodeId, ...): PromptSchemaValidation[]` and is invoked from `validatePromptSchemaNodes` body (lines 134-194). Adding a new rule = (a) extend the `rule` discriminated-union literal type (lines 33-43), (b) write a `validate<X>(…)` function, (c) push its results into the main loop. **Two distinct loops** in `validatePromptSchemaNodes`:

1. **AI-node loop** (lines 152-175) — only iterates nodes whose type is in `AI_NODE_TYPES = {'aiAnalysis', 'aiCompletion'}`; passes the prompt schema + downstream info to the AI-only rules.
2. **Per-type config-shape loop** (lines 180-184, added by task 043) — iterates ALL nodes and dispatches per-type:

```ts
for (const node of nodes) {
  if (node.data.type === 'lookupUserMembership') {
    results.push(...validateLookupUserMembershipNode(node.id, node));
  }
}
```

This is the **canonical extension point** for new per-node rules that don't need downstream/AI context.

**Validation invocation seam** (`services/playbookNodeSync.ts:274-303`): `validateAndSyncNodes(playbookId, nodes, edges)` runs `validatePromptSchemaNodes` on every save. If `hasValidationErrors(...)` → sync is blocked. Warnings flow through but don't block.

**NodeValidationBadge surfacing** (`components/properties/NodeValidationBadge.tsx`):
- Props: `validationErrors: string[]` + optional `warnings: string[]`.
- Status: `'error' | 'warning' | 'valid'` (errors win over warnings; warnings win over valid).
- Icon: `ErrorCircle20Filled` (red) / `Warning20Filled` (yellow) / `CheckmarkCircle20Filled` (green).
- Renders an inline `Badge` with `Tooltip` (summary); clicking opens a `Popover` with full lists ("Errors (N)" / "Warnings (N)").
- Consumed by `NodePropertiesForm.tsx:193`: `<NodeValidationBadge validationErrors={node.data.validationErrors ?? []} />` — currently reads from `node.data.validationErrors` (NOT from canvasValidation's grouped results — see gap below).

**Gap noted** (relevant to all three H2 tasks): there is no built-in connector wiring `groupValidationsByNode(...)` results into `node.data.validationErrors` / `node.data.warnings`. The badge today reads `node.data.validationErrors` directly; canvasValidation runs only at save. Tasks 091/092/093 may need to either (a) pipe `groupValidationsByNode` results into `node.data` after each canvas mutation, OR (b) pass a `warnings` prop into the badge from a higher-level consumer of canvasValidation. Either approach is preferred over a third invent. **Recommend (a)** — extend `canvasStore` to call `validatePromptSchemaNodes` on every change and patch `node.data.validationErrors` + `node.data.warnings`. Coordinate with task 091/092/093 owners.

---

## Section 6 — H2 affordance → extension seam mapping

### Task 091 — `OutputVariable` rename guard

**Spec** (FR-3H2.1): when user edits a node's `OutputVariable` in `NodePropertiesForm.tsx` / `NodePropertiesDialog.tsx`, scan all other nodes for `{{<oldName>.output*}}` references; present dialog with auto-rename / keep / continue options.

**Extension seams**:

| Seam | File:Line | Mechanism |
|---|---|---|
| **Input change** | `NodePropertiesForm.tsx:217-227` and `NodePropertiesDialog.tsx:281-289` | Both `<Input>` calls `handleUpdate('outputVariable', value)`. Intercept here — wrap with rename-guard logic before calling `updateNodeData`. |
| **Reference discovery** | `services/canvasValidation.ts:97` (`TEMPLATE_REF_RE`) + lines 676-693 (`extractTemplateRefs`) + lines 597-671 (`parseDownstreamNode`) | Reuse `extractTemplateRefs(value)` regex. Extend `parseDownstreamNode` (or factor out a shared scanner) to walk ALL nodes (not just 1-hop AI descendants) — same field set: `configJson.fieldMappings[].value`, `configJson.fields{}`, `configJson.{template, body, subject, description}`, `node.data.{template, emailBody, emailSubject}`. **Also scan `node.data.conditionJson`** (NEW — not currently scanned) for `condition.left` / `condition.right` template refs. |
| **Auto-rename application** | `stores/canvasStore.ts:140-141` (`setEdges`) + existing `updateNodeData` | Add a new store action `renameOutputVariable(oldName, newName)` that mutates each affected node's serialized fields via the same write paths the forms use (parse JSON → patch → serialize). |
| **Validation rule** (audit follow-up) | `services/canvasValidation.ts:180-184` (per-type loop) | Add rule `outputvar-collision`: if two nodes share the same non-empty `outputVariable`, surface as error. Plugs in via the existing per-type loop pattern (task 043 precedent). |
| **Confirmation dialog** | Reuse Fluent UI v9 `Dialog` (per `NodePropertiesDialog.tsx` pattern) — author a small `RenameGuardDialog` component in `components/properties/` exporting via `properties/index.ts`. |

**Pattern alignment**: scanner reuses the existing `TEMPLATE_REF_RE` regex; rule registration follows the task-043 per-type loop precedent; dialog reuses Fluent UI v9 `Dialog` + Spaarke tokens.

---

### Task 092 — Branch wiring auto-generation

**Spec** (FR-3H2.2): when an edge connects a Condition node to a downstream node, prompt for branch (`true` / `false` / `both`); persist in `DependsOn` branch metadata; visualize edges differently per branch.

**Extension seams**:

| Seam | File:Line | Mechanism |
|---|---|---|
| **Edge-type dispatch (current)** | `stores/canvasStore.ts:149-182` (`onConnect`) | Today already auto-assigns `trueBranch` or `falseBranch` IFF `connection.sourceHandle === 'true' / 'false'`. Extension point: when source is `condition` but `sourceHandle` is undefined OR user-prompt mode is configured, intercept and open a branch-picker dialog before calling `addEdge`. |
| **Branch metadata** | `types/canvas.ts:83-90` (`ConditionEdgeData`) | Already has `branch: 'true' \| 'false'`. For the `both` option: either (a) create TWO edges (one trueBranch + one falseBranch — recommended, mirrors the existing two-handle UI), OR (b) extend the union with `'both'` (NOT recommended — would require new edge renderer). **Pick (a).** |
| **Edge renderer** | `components/edges/ConditionEdge.tsx` + `components/edges/index.ts:21-24` (`edgeTypes` registry) | Already complete for `trueBranch` / `falseBranch`. No new edge types needed if 092 picks option (a) above. |
| **ConditionEditor coordination** | `components/properties/ConditionEditor.tsx:152-159` (`update`) | The editor's `trueBranch` / `falseBranch` STRING LABELS could be surfaced in the new branch-picker dialog ("Wire to: True (\"Approved\") / False (\"Rejected\") / Both"). Read-only consumer — no editor changes required. |
| **Branch-picker dialog** | New `BranchPickerDialog` component in `components/properties/`, exported via `properties/index.ts` barrel; reuses Fluent UI v9 `Dialog` per `NodePropertiesDialog.tsx` precedent. |
| **Persistence** | Already handled by `canvasStore.onConnect` writing to `state.edges`; persisted via existing `playbookNodeSync.ts` (canvas serialization). **No new persistence code.** |

**Pattern alignment**: edge-type dispatch already exists at `onConnect`; "both" reuses two existing edge types instead of inventing a third; dialog reuses Fluent UI v9.

---

### Task 093 — Edge perf hint advisory

**Spec** (FR-3H2.3): when an edge connects two nodes whose configs don't reference each other's `OutputVariable`, show non-blocking warning: "This edge forces sequential execution. Confirm or remove?" Advisory only.

**Extension seams**:

| Seam | File:Line | Mechanism |
|---|---|---|
| **Rule registration** | `services/canvasValidation.ts:180-184` (per-type loop) — extend to **per-edge loop** | Add NEW iteration: `for (const edge of edges) { results.push(...validateEdgePerfHint(edge, nodeById, ...)); }`. Currently no per-edge rules exist; this is the **only NEW iteration pattern** introduced, but it follows the same shape as the per-type loop. |
| **Rule discriminator** | `services/canvasValidation.ts:33-43` (`rule` union) | Add `'edge-no-data-dependency'` literal. |
| **Reference scanning** | `services/canvasValidation.ts:676-693` (`extractTemplateRefs`) + 597-671 (`parseDownstreamNode`) | Reuse to check whether the target node's serialized config contains any `{{<sourceOutputVar>.output.*}}` refs. If not → emit warning. |
| **Warning surface** | `components/properties/NodeValidationBadge.tsx:90-95` (`warnings` prop already exists, line 103) | Badge already accepts `warnings: string[]`. **NEW seam needed**: NodeValidationBadge today is consumed only by `NodePropertiesForm.tsx:193` and `NodePropertiesDialog.tsx` doesn't render the badge at all. For per-edge warnings, either (a) attach to source node's badge via `node.data.warnings`, OR (b) render badge inline on the edge itself (would require modifying `TrueBranchEdge` / `FalseBranchEdge` + a new generic edge renderer). **Recommend (a)** — keep the badge on the source node so users see it in the properties panel they're already using; less new surface area. |
| **Validation invocation** | `services/playbookNodeSync.ts:287-290` already runs `validatePromptSchemaNodes`. Warnings flow through; only errors block save. **No invocation changes needed.** |
| **Bridging `groupValidationsByNode` → `node.data.warnings`** | NEW — see "Gap noted" in §5 | Either (a) extend `canvasStore` to call `validatePromptSchemaNodes` on every change and patch `node.data.warnings`, OR (b) pass results through a top-level consumer. Recommend (a) and coordinate across 091/092/093. |

**Pattern alignment**: rule registration extends the existing per-type loop with a sibling per-edge loop; warning surface reuses existing `warnings` prop on `NodeValidationBadge` (no new component); regex + node scanner reused verbatim.

---

## Section 7 — Anti-patterns to avoid (per Q5)

| Anti-pattern (DO NOT) | Existing pattern to use instead |
|---|---|
| ❌ Create a new validation framework / new rule registry / new severity types | ✅ Use `services/canvasValidation.ts` — add rules as `validate<X>` functions invoked from `validatePromptSchemaNodes`; extend the `rule` union literal type; use existing `'error' / 'warning'` severity |
| ❌ Author a new template-reference regex / new scanner | ✅ Reuse `TEMPLATE_REF_RE` (canvasValidation.ts:97) + `extractTemplateRefs` helper |
| ❌ Invent a new edge type for the "both" option in branch wiring | ✅ Create two edges (one `trueBranch` + one `falseBranch`) — existing renderers handle them |
| ❌ Add a new modal component framework / new dialog system | ✅ Reuse Fluent UI v9 `Dialog` per `NodePropertiesDialog.tsx` precedent |
| ❌ Create a new badge / warning indicator component | ✅ Reuse `NodeValidationBadge` — its `warnings: string[]` prop already exists |
| ❌ Persist branch metadata in a parallel structure (e.g. node.data.branchWiring) | ✅ Use existing `ConditionEdgeData.branch: 'true' \| 'false'` on the edge itself |
| ❌ Build a separate sidebar for variable picking / reference search | ✅ Extend `VariableReferencePanel.tsx` OR reuse its `OUTPUT_FIELDS_BY_TYPE` catalog + `buildVariableEntries` helper |
| ❌ Add a new form-props contract (e.g. `NodeFormV2Props`) | ✅ Use existing `NodeFormProps` (`types/forms.ts:16-23`) — `(nodeId, configJson, onConfigChange)` |
| ❌ Add a new accordion / tabbed-panel system in properties | ✅ Properties panel uses Fluent UI v9 `Accordion` (NodePropertiesForm) + `TabList` (NodePropertiesDialog); reuse both via existing `hasTypeForm` allow-list pattern |
| ❌ Add hex color values for new affordances | ✅ Always use `tokens.colorPalette*` / `tokens.colorNeutral*` per ADR-021 (LookupUserMembershipForm + ConditionEdge are precedents) |

---

## Section 8 — Acceptance

Satisfies POML acceptance criteria:

- ✅ **Research doc exists** at `projects/spaarke-platform-foundations-r3/notes/playbookbuilder-pattern-research.md` (this file).
- ✅ **Each H2 affordance has an identified extension seam** (Section 6 — file:line refs + mechanism for each of 091/092/093).

**Cross-task coordination notes for 091/092/093** (parallel-group **T** per TASK-INDEX line 120):

- `services/canvasValidation.ts` is shared by all three tasks. Suggested region split (top-of-file comment blocks):
  - **091 region**: new rule `outputvar-collision` + (if scoped here) reference-scanning refactor.
  - **092 region**: NO `canvasValidation.ts` changes expected (lives in `canvasStore.ts` + new `BranchPickerDialog`).
  - **093 region**: new rule `edge-no-data-dependency` + the new per-edge iteration loop.
- `stores/canvasStore.ts` is touched by 091 (rename action) + 092 (onConnect intercept). Coordinate.
- `components/properties/index.ts` barrel is touched by 091 (RenameGuardDialog) + 092 (BranchPickerDialog). Trivial conflict — alphabetize.
- **Recommended shared follow-up** (see §5 gap): add a `canvasStore` subscription that runs `validatePromptSchemaNodes` + `groupValidationsByNode` after every mutation and patches each node's `data.validationErrors` + `data.warnings`. Pick up in task 094 (component tests) or factor out as a new sub-task before 094.

**Ready for tasks 091, 092, 093.**

---

**Maintained for**: tasks 091 (`OutputVariable` rename guard), 092 (Branch wiring auto-generation), 093 (Edge perf hint advisory). Cite this doc in each task's POML `<knowledge>` section and in code review.
