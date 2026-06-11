# G — Drill-through URL spike: `sprk_smarttodo` modal-style render

> **Task**: R4-003 (Phase 0 / Workstream G)
> **Date**: 2026-06-10
> **Status**: SPIKE-PLAN COMPLETE — live-env verification deferred to task-execute live run (assignee at task 080 / 081-084 time)
> **Risk**: LOW — abundant production precedent confirms each leg of the contract independently
> **Probability of fallback needed**: LOW (modal-style + `data`-envelope param propagation are both proven patterns)

---

## TL;DR (one-paragraph summary)

`Xrm.Navigation.navigateTo({pageType: "webresource", webresourceName: "sprk_smarttodo", data: "regardingType=<entity>&regardingId=<id>"}, {target: 2, position: 1, width: {value: 80, unit: "%"}, height: {value: 80, unit: "%"}, title: "Upcoming To Dos"})` **is the recommended payload**. Microsoft Learn confirms `target: 2` opens the web resource in a dialog (modal), with width/height/position controllable via `navigationOptions`. The `data` string is surfaced to the iframe as `?data=<urlencoded>` per the in-repo `parseDataParams` utility — which already handles both the envelope form AND raw `?key=val` form. **However, the existing R3 `useLaunchContext` hook reads `window.location.search` raw and does NOT decode the `data` envelope** — so the R4 implementation MUST either (a) refactor `useLaunchContext` to use `parseDataParams`, or (b) emit a raw query string from VisualHost's drill-through. Option (a) is recommended — it makes the SmartTodo Code Page launch-context handling unified across Outlook ribbon (R3 task 070) AND Visual Host drill-through (R4 G).

---

## 1. `Xrm.Navigation.navigateTo` for `pageType: "webresource"` — reference summary

**Source**: [Microsoft Learn — `Xrm.Navigation.navigateTo`](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto) (page captured 2026-04-10).

### `pageInput` for HTML web resource

| Field | Type | Required | Notes |
|---|---|---|---|
| `pageType` | String | yes | `"webresource"` |
| `webresourceName` | String | yes | e.g. `"sprk_smarttodo"` |
| `data` | **String** (NOT object — see "Generative" for object form) | optional | "The data to pass to the web resource." |

**Critical**: For `pageType: "webresource"`, `data` is typed as a **String**, not an object. This is unique to web resources — for `pageType: "entityrecord"` and `pageType: "generative"`, `data` is an Object. Spaarke convention (see [`parseDataParams.ts`](#21-parsedataparamsts---spaarke-standard-envelope-decoder)) is to pass the data as `key1=val1&key2=val2` (no leading `?`) and let the Code Page decode it.

### `navigationOptions`

| Field | Type | Required | Notes |
|---|---|---|---|
| `target` | Number | optional (default `1` = inline) | **`2` = open in a dialog**. The MS Learn docs explicitly state: "You can open entity records, web resources, and generative pages either inline or in a dialog." |
| `width` | Number OR `{value: N, unit: "%" \| "px"}` | optional | Only valid when `target: 2`. |
| `height` | Number OR `{value: N, unit: "%" \| "px"}` | optional | Only valid when `target: 2`. |
| `position` | Number | optional (default `1` = center) | `1` = center, `2` = far side. |
| `title` | String | optional | Dialog title. |

### Modal-style render — CONFIRMED by docs

Per MS Learn Example 4 (HTML web resource dialog):

```javascript
var pageInput = {
    pageType: "webresource",
    webresourceName: "new_sample_webresource.htm"
};
var navigationOptions = {
    target: 2,
    width: 500,
    height: 400,
    position: 1
};
Xrm.Navigation.navigateTo(pageInput, navigationOptions);
```

This is exactly the shape FR-34 needs. Render is in an iframe inside a dialog overlay — confirmed by production behavior of every `target: 2` + `pageType: "webresource"` call in the repo (Document Upload Wizard, Create Matter Wizard, Find Similar, Playbook Library, etc. — see §2 inventory).

### `data` propagation — IMPLICIT in docs, EXPLICIT in repo precedent

Microsoft Learn does NOT explicitly state HOW the `data` string is surfaced to the web resource at runtime — but the Spaarke production-validated mechanism is:

> Dataverse appends the data string as a single URL-encoded `?data=<urlencoded>` query-string parameter to the web resource iframe's URL.

This is documented and handled in `src/client/shared/Spaarke.UI.Components/src/utils/parseDataParams.ts`, which is used by every Code Page wrapper today (Document Upload Wizard, Semantic Search, Document Relationship Viewer, etc.).

---

## 2. Existing repo precedent — `Xrm.Navigation.navigateTo` for `pageType: "webresource"`

Comprehensive grep across `src/` yielded 30+ callers. The canonical pattern is fully proven:

| Caller | Surface | `target` | `width` × `height` | `data` shape | `position` | Notes |
|---|---|---|---|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/xrmNavigationServiceAdapter.ts` `openDialog()` | Shared adapter | `2` | from caller | passes through caller's `data` string | (omitted) | **The canonical shared abstraction**. Used by AssociateToStep, RichFilePreview, etc. |
| `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts` (7 launchers) | LegalWorkspace + SpaarkeAi Get Started cards | `2` | `60%` × `70%` | `"bffBaseUrl=<encoded>"` + call-specific | (omitted) | Frame-walking `Xrm` resolver (handles nested iframes). |
| `src/client/pcf/SemanticSearchControl/.../NavigationService.ts` `openSemanticSearchPage()` | PCF dialog launch | `2` | `80%` × `80%` (configurable) | `query=<...>&theme=<...>&scope=<...>&entityId=<...>` | (omitted) | Wraps the string in `encodeURIComponent(dataString)` at call site — see comment below. |
| `src/client/pcf/SemanticSearchControl/.../NavigationService.ts` `openAddDocument()` | PCF dialog launch | `2` | `60%` × `70%` | `parentEntityType=...&parentEntityId=...&parentEntityName=...&containerId=...&theme=...` | (omitted) | Production validated by Document Upload Wizard. |
| **`src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` `handleExpandClick`** | **VisualHost drill-through (THE caller for R4 G)** | `2` | `90%` × `85%` | `URLSearchParams` → `params.toString()` (NOT pre-encoded) | `1` (center) | **Currently auto-injects `entityName`, `filterField`, `filterValue`, `viewId`, `mode=dialog` keys** — see §3 for what R4 must adapt. |
| `src/client/code-pages/SemanticSearch/src/components/EntityRecordDialog.ts` | Code Page → entity record dialog | `2` | `80%` × `80%` | n/a (`pageType: entityrecord`) | (omitted) | Not a web resource but same `target: 2` pattern. |

### 2.1 `parseDataParams.ts` — Spaarke-standard envelope decoder

`src/client/shared/Spaarke.UI.Components/src/utils/parseDataParams.ts` handles **both** propagation forms transparently:

- **Xrm `data` envelope**: URL is `?data=key1%3Dval1%26key2%3Dval2` → decoded into `{key1: "val1", key2: "val2"}`.
- **Raw URL params**: URL is `?key1=val1&key2=val2` → returned as-is.
- **Mixed**: `?data=mode%3Dedit&theme=dark` → both merged (envelope wins on conflict).

This is the production utility every Spaarke Code Page uses on cold load.

### 2.2 `useLaunchContext.ts` — SmartTodo-specific parser (R3 only handles RAW form)

`src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` reads `window.location.search` directly via `URLSearchParams`. It does **NOT** decode the Xrm `data` envelope:

```typescript
const action = params.get(LAUNCH_PARAM_KEYS.ACTION);
const entityType = params.get(LAUNCH_PARAM_KEYS.REGARDING_TYPE);
const recordId = params.get(LAUNCH_PARAM_KEYS.REGARDING_ID);
```

So it only works when the URL is `?action=createTodo&regardingType=...&regardingId=...&regardingName=...` (raw form).

This is the SmartTodo "task 070b" Outlook-ribbon-launcher path — and the Outlook ribbon builds a raw URL via `window.open` (not `Xrm.Navigation.navigateTo`), so it sees raw params.

**R4 G implication**: When VisualHost drills through to `sprk_smarttodo` via `navigateTo`, the params arrive as `?data=<urlencoded>` — NOT as raw `?regardingType=...` keys. `useLaunchContext` will see `params.get('action') === null` and return `undefined`, so the Kanban will load with no pre-filter. This is the ONE meaningful contract gap surfaced by the spike.

---

## 3. VisualHost drill-through consumption (existing implementation, v1.4.13)

**Reference**: `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` lines 374–530 (`handleExpandClick`).

### How `sprk_drillthroughtarget` is consumed today

The field accepts two formats (decided at runtime by file extension):

| Format | Detection | `navigateTo` shape |
|---|---|---|
| Web resource name ending in `.html` / `.htm` (e.g., `sprk_eventspage.html`) | `.toLowerCase().endsWith('.html')` | `{pageType: 'webresource', webresourceName: <target>, data: <params>}` with `{target: 2, position: 1, width: 90%, height: 85%}` |
| Entity logical name (no extension, e.g., `sprk_event`) | else-branch | `{pageType: 'entitylist', entityName: <target>}` with `{target: 2, position: 1, width: 90%, height: 85%}` |

### Auto-injected `data` keys (the contract for R4 G)

When the target is a web resource, VisualHost builds the `data` string itself — the chart def author does NOT control individual keys, only the target name. The keys auto-injected are:

```typescript
const params = new URLSearchParams();
if (entityName) params.set('entityName', entityName);      // → "sprk_todo"
if (filterField) params.set('filterField', filterField);   // → "sprk_regardingmatter"
if (filterValue) params.set('filterValue', filterValue);   // → parent record ID (no braces)
if (viewId) params.set('viewId', viewId.replace(/[{}]/g, ''));
params.set('mode', 'dialog');
```

So when VisualHost drills through to `sprk_smarttodo.html`, the SmartTodo Code Page sees:

```
?data=entityName%3Dsprk_todo%26filterField%3Dsprk_regardingmatter%26filterValue%3D<guid>%26mode%3Ddialog
```

**Critical**: these keys (`entityName`, `filterField`, `filterValue`, `mode`) are NOT the keys SmartTodo's `useLaunchContext` expects (`action`, `regardingType`, `regardingId`, `regardingName`).

### Two reconciliation paths

**Path A (RECOMMENDED) — Refactor `useLaunchContext` to read VisualHost's contract**:

Refactor `useLaunchContext` (or add a new sibling hook `useDrillThroughContext`) to:
1. Use `parseDataParams()` from `@spaarke/ui-components` (handles both envelope + raw forms).
2. Recognize VisualHost's auto-injected keys: `entityName` → regardingType, `filterField` + `filterValue` → regarding lookup field + parent ID.
3. Translate to the same `IRegardingFilter` SmartTodo's Kanban uses for filtering.

This path keeps VisualHost generic (no R4-specific changes to VisualHost), and concentrates the launch-context logic in one place inside SmartTodo. It also means the Outlook ribbon path (R3 task 070) and the Visual Host drill-through path (R4 G) BOTH flow through the same `useLaunchContext` hook.

**Path B — Make VisualHost emit R4-G-specific keys via an opt-in chart-def field**:

Add an optional `sprk_drillthroughdatatemplate` field on `sprk_chartdefinition` that lets the chart def override the auto-injected keys. The new chart defs for R4 G would set it to:

```
action=$mode&regardingType=$entityName&regardingId=$filterValue&regardingName=
```

VisualHost substitutes `$entityName`, `$filterValue`, etc. before passing to `navigateTo`. Backward compat preserved (chart defs without the field continue to use auto-inject).

**Trade-off**:

| Aspect | Path A | Path B |
|---|---|---|
| Maintenance | One hook update | Schema change + VisualHost code path + chart def authoring complexity |
| Reuse | `useLaunchContext` unified | Per-chart-def template strings |
| Migration risk | Low (only SmartTodo touched) | Medium (VisualHost is shared by Matter / Project / Invoice / WorkAssignment forms — every chart def affected) |
| Scope creep | None | Extends VisualHost contract beyond R4 G |

**Recommendation**: **Path A**. Concentrate the launch-context contract in SmartTodo where it belongs; treat VisualHost's auto-inject keys as the wire format. Document the translation in `useLaunchContext.ts`'s header.

---

## 4. Existing UPCOMING TASKS chart def (`154bd4a4-f359-f111-a825-3833c5d9bcab`) drill payload

**Source**: Live record in spaarkedev1 — repo grep found the GUID referenced only in project artifacts, NOT in `infrastructure/dataverse/charts/` JSON files (the chart def was created directly in the maker portal; no source-of-truth file in the repo).

**Runtime inspection deferred**: Task 080 will fetch the live record via `Check-ChartDefinitionEntity.ps1` and capture the actual `sprk_drillthroughtarget` payload at that time. For the spike outcome:

| Field | Expected value | Source of expectation |
|---|---|---|
| `sprk_drillthroughtarget` | Either `sprk_event` (entity name → opens entity list — the R3 behavior) OR a web resource like `sprk_eventspage.html` | VisualHost code supports both formats; R3 visual was an Events list on Matter form, so likely entity-name form. |
| `sprk_contextfieldname` | `sprk_regardingmatter` | Matter form is the only place it's mounted today. |

**R4 implication**: Even if the existing `154bd4a4-...` def uses the entity-name form (opens entity list view), R4 G EXPLICITLY mandates the web-resource form per FR-34. We are NOT cloning the drill-through target verbatim — we're cloning the visual shape (Due Date Card List = 100000009) and entity-targeting pattern. Drill-through is a deliberate override.

---

## 5. Recommended drill-through payload for R4 G chart defs (FR-34)

### 5.1 What goes in `sprk_chartdefinition.sprk_drillthroughtarget`

```
sprk_smarttodo.html
```

The `.html` suffix is the VisualHost contract marker that triggers the web-resource code path (vs. the entity-list code path). The `sprk_smarttodo` web resource name matches the actual deployed Code Page resource name.

### 5.2 What `navigateTo` call VisualHost will produce (no code change needed)

```javascript
Xrm.Navigation.navigateTo(
  {
    pageType: "webresource",
    webresourceName: "sprk_smarttodo.html",
    data: "entityName=sprk_todo&filterField=sprk_regardingmatter&filterValue=<parent-record-guid>&mode=dialog"
  },
  {
    target: 2,
    position: 1,
    width: { value: 90, unit: "%" },
    height: { value: 85, unit: "%" }
  }
);
```

(Same shape for Project / Invoice / WorkAssignment — only `filterField` and `filterValue` differ.)

### 5.3 What SmartTodo Code Page sees in iframe `window.location.search`

```
?data=entityName%3Dsprk_todo%26filterField%3Dsprk_regardingmatter%26filterValue%3D<guid>%26mode%3Ddialog
```

After `parseDataParams()` decode:

```typescript
{
  entityName: "sprk_todo",
  filterField: "sprk_regardingmatter",
  filterValue: "<parent-record-guid>",
  mode: "dialog"
}
```

### 5.4 What `useLaunchContext` (refactored per Path A) returns

```typescript
{
  action: "drillThrough",
  initialRegarding: {
    entityType: "sprk_matter",        // derived from filterField "sprk_regardingmatter" — strip "sprk_regarding" prefix
    recordId: "<parent-record-guid>", // from filterValue
    recordName: ""                    // not provided by VisualHost; lookup display name via Xrm.WebApi if needed
  }
}
```

The Kanban consumer then filters by `regarding<X>` lookup = `recordId` and the orientation/header behave as if the user picked that parent from the resolver.

---

## 6. Live spike test plan (for task-execute live execution in spaarkedev1)

> **Execution context**: spaarkedev1, signed in as a user with access to a Matter record. Browser DevTools console open.

### Test 1 — Modal render (HARD-CONFIRM `target: 2` + `pageType: "webresource"`)

**Steps**:
1. Open spaarkedev1 (Power Apps maker → published Spaarke app).
2. Open any Matter record main form.
3. Open browser DevTools → Console.
4. Paste and execute:
   ```javascript
   Xrm.Navigation.navigateTo(
     {
       pageType: "webresource",
       webresourceName: "sprk_smarttodo.html",
       data: "entityName=sprk_todo&filterField=sprk_regardingmatter&filterValue=" +
             Xrm.Page.data.entity.getId().replace(/[{}]/g, "") +
             "&mode=dialog"
     },
     {
       target: 2,
       position: 1,
       width: { value: 90, unit: "%" },
       height: { value: 85, unit: "%" },
       title: "Upcoming To Dos"
     }
   );
   ```
5. **Expected**: SmartTodo Code Page opens in a **modal dialog overlay** (NOT full-window navigation). Title bar reads "Upcoming To Dos". Dialog is centered, ~90% × 85% of viewport.

### Test 2 — Param propagation (HARD-CONFIRM `data` surfaces to iframe)

**Steps** (continued from Test 1, with modal still open):
1. In DevTools, switch to the iframe context (in Chrome: dropdown next to "Console" → choose the `sprk_smarttodo.html` frame).
2. Execute:
   ```javascript
   console.log("location.search =", window.location.search);
   console.log("decoded data =", new URLSearchParams(window.location.search).get("data"));
   ```
3. **Expected**:
   - `location.search` contains `?data=entityName%3Dsprk_todo%26filterField%3Dsprk_regardingmatter%26filterValue%3D<guid>%26mode%3Ddialog`
   - `decoded data` = `entityName=sprk_todo&filterField=sprk_regardingmatter&filterValue=<guid>&mode=dialog`
4. Then run:
   ```javascript
   // Simulate parseDataParams decoding
   const urlParams = new URLSearchParams(window.location.search);
   const dataValue = urlParams.get('data');
   const result = {};
   if (dataValue) {
     const parsed = new URLSearchParams(decodeURIComponent(dataValue));
     parsed.forEach((value, key) => { result[key.trim()] = value.trim(); });
   }
   console.log("parsed =", result);
   ```
5. **Expected**: `result` = `{entityName: "sprk_todo", filterField: "sprk_regardingmatter", filterValue: "<guid>", mode: "dialog"}`.

### Test 3 — Fallback if Test 2 fails (DEFENSE IN DEPTH)

**If `data` param does NOT surface to the iframe** (unexpected — would contradict all Spaarke production precedent):

**Strategy 1: sessionStorage handoff** (HIGH confidence — works regardless of `data` propagation):
1. Before `navigateTo`, write to `sessionStorage`:
   ```javascript
   sessionStorage.setItem("sprk_smarttodo_launchContext", JSON.stringify({
     regardingType: "sprk_matter",
     regardingId: matterId,
     regardingName: matterName
   }));
   ```
2. Inside the SmartTodo Code Page, on cold load, read and clear:
   ```javascript
   const ctx = sessionStorage.getItem("sprk_smarttodo_launchContext");
   if (ctx) { sessionStorage.removeItem("sprk_smarttodo_launchContext"); /* parse + apply */ }
   ```
3. Same-tab same-origin → sessionStorage is shared between MDA outer frame and Code Page iframe.
4. **Caveat**: Requires drill-through to go through a JS-controlled launcher (not direct chart-def-driven `navigateTo`). Would require Path B variant where VisualHost emits a pre-handler.

**Strategy 2: `Xrm.WebApi` parent-record lookup** (MEDIUM confidence — slower, but no contract changes):
1. The Code Page reads `Xrm.Page.context.getQueryStringParameters()` or polls `window.parent.Xrm` (frame-walk pattern from `wizardLaunchers.ts`).
2. If VisualHost can be coaxed to set a single recognizable param (e.g., `?launchContext=visualHostDrillThrough`), the Code Page reads the active form context from `parent.Xrm.Page` and resolves entity/ID that way.
3. **Drawback**: Couples SmartTodo Code Page tightly to `Xrm.Page` parent context — violates the multi-environment portability spirit of NFR-03 (Code Page should be self-sufficient given launch params).

**Strategy 3: Direct `window.open` + custom modal CSS** (LAST RESORT — rejected by FR-34):
- `window.open` doesn't render as a Dataverse modal — opens a real browser window.
- FR-34 explicitly mandates "modal" not "new browser window".
- Listed only for completeness.

### Test 4 — End-to-end via real VisualHost chart def (after task 080 deploys chart defs)

**Steps**:
1. Open a Matter record with the new "Upcoming To Dos" Visual Host card mounted (task 081 outcome).
2. Click the expand icon on the card.
3. **Expected**: SmartTodo Code Page modal opens, Kanban renders filtered to only `sprk_todo` records where `sprk_regardingmatter = <current matter ID>`.

---

## 7. Risk and assumption summary

| Risk | Likelihood | Mitigation |
|---|---|---|
| `data` param doesn't propagate to iframe | **VERY LOW** — `parseDataParams` is production-used by Document Upload Wizard, Semantic Search, Document Relationship Viewer, Create Matter Wizard, all 7 LegalWorkspace wizard launchers | Fallback Strategy 1 (sessionStorage) ready if needed |
| Modal-style render doesn't activate | **VERY LOW** — explicitly documented and used by ~20+ in-repo `target: 2` callers | None — would be a Dataverse platform bug; would require escalation |
| `useLaunchContext` param-key mismatch | **CERTAIN** (this IS the gap) | Path A refactor: extend `useLaunchContext` to recognize VisualHost's auto-inject keys via `parseDataParams` |
| VisualHost's auto-inject `filterField=<lookup>` doesn't translate cleanly to a regarding type | **LOW** — the `sprk_regardingmatter`-style field names are deterministic; strip `sprk_regarding` prefix to get the target entity logical name (`sprk_matter`) | Document mapping in `useLaunchContext.ts` header; unit-test the translation |
| Modal close doesn't return control to Matter form | **LOW** — promise resolves on close per docs; same pattern proven by every existing dialog launcher | N/A |
| User opens modal multiple times → stale launch context | **LOW** | `useLaunchContext` already clears params via `history.replaceState` on mount (R3 task 070b mechanism); applies equally to Path A refactor |

### Assumptions

1. The `sprk_smarttodo` web resource name in spaarkedev1 follows the `.html` convention (i.e., the deployed Code Page registers as either `sprk_smarttodo` or `sprk_smarttodo.html`). **Task 080 will verify** during chart def authoring; if the deployed name is bare `sprk_smarttodo`, the VisualHost branch detection breaks and a tiny VisualHost change is needed to also check for known Code Page names. Mitigation: deploy the Code Page with `.html` suffix per `ADR-026` convention (matches `sprk_eventspage.html`).
2. The Code Page's `parseDataParams` utility is consumable from `src/solutions/SmartTodo/` — confirmed by `package.json` workspace alias `@spaarke/ui-components: workspace:*`.
3. VisualHost's existing `position: 1, width: 90%, height: 85%` defaults are acceptable for the SmartTodo modal — spec doesn't mandate otherwise; matches FR-34's implicit "modal-style" requirement.

---

## 8. Acceptance criteria checklist (from task 003 POML)

- [x] Working `Xrm.Navigation.navigateTo` payload documented (full payload in §5; confirmed against MS Learn + 20+ in-repo callers)
- [ ] Test executed in spaarkedev1 with captured evidence (screenshot or console log) — **DEFERRED to live execution at task 080 / 081-084 time**
- [x] Query-string param propagation contract documented — confirmed via Spaarke `parseDataParams` precedent (§2.1)
- [x] Modal-style render contract documented — confirmed via MS Learn Example 4 + production precedent (§1)
- [x] Tasks 080–084 have a clear navigation contract to implement against — `sprk_drillthroughtarget = "sprk_smarttodo.html"`, `sprk_contextfieldname = "sprk_regarding<X>"`, plus Path A `useLaunchContext` refactor as the SmartTodo-side change
- [x] Fallback strategy documented for the one identified gap (`useLaunchContext` doesn't decode `data` envelope or recognize VisualHost's wire-format keys) — Path A refactor recommended (§3); fallback Strategy 1 (sessionStorage) as defense-in-depth (§6 Test 3)

---

## 9. Hand-off to live execution

> **At task 080 / 081-084 execution time, the assignee must**:
>
> 1. Execute Test 1 (modal render) in spaarkedev1. Capture a screenshot of the modal overlay rendering. Append to this doc as §10 "Live evidence".
> 2. Execute Test 2 (param propagation). Capture the console output proving `data` envelope decodes to the expected keys.
> 3. If Test 1 or Test 2 fails, switch to fallback Strategy 1 (sessionStorage) per §6 Test 3 and notify the project owner before proceeding.
> 4. Update §5 with any adjustments measured at runtime (e.g., if the deployed web resource name is bare `sprk_smarttodo` not `sprk_smarttodo.html` — adjust accordingly).
> 5. After Tasks 080–084 land, flip task 003's status to "completed" in `TASK-INDEX.md` and `current-task.md`.

---

## 10. Live evidence (TO BE FILLED IN at task-execute live run)

> _This section is intentionally blank. To be populated by the assignee executing Tests 1–4 in spaarkedev1 against the real environment._

| Test | Status | Evidence |
|---|---|---|
| Test 1 — Modal render | (pending) | (paste screenshot path) |
| Test 2 — Param propagation | (pending) | (paste console output) |
| Test 3 — Fallback if needed | (n/a or pending) | (n/a if Tests 1+2 pass) |
| Test 4 — E2E via real chart def | (pending — gated on task 080) | (paste screenshot) |

---

## Appendix A — Full inventory of `Xrm.Navigation.navigateTo` callers in `src/` (74 files)

Grep yielded 74 files; representative subset shown in §2. Full list cached at session start; no significant pattern divergence from the canonical `{target: 2, width: <obj>, height: <obj>}` shape. Notable exceptions:

- `src/client/code-pages/AnalysisWorkspace/src/services/hostContext.ts` and `src/solutions/SmartTodo/src/components/TodoDetailPanel.tsx` use `pageType: "entityrecord"` with `target: 2` for record dialogs — same `navigateTo` API, different `pageInput` shape. Irrelevant to R4 G (we want a web resource, not an entity record).
- `src/solutions/SmartTodo/src/utils/navigation.ts` `openRecordDialog()` and `navigateToEntityList()` — both use `target: 2, width: 80%, height: 80%`. Confirms the pattern is already adopted inside `src/solutions/SmartTodo/`.

## Appendix B — `parseDataParams.ts` example coverage matrix

Verified by reading the JSDoc + implementation:

| Input URL form | Returns |
|---|---|
| `?data=documentId%3Dabc-123%26matterId%3Ddef-456` | `{documentId: "abc-123", matterId: "def-456"}` |
| `?documentId=abc-123&matterId=def-456` | `{documentId: "abc-123", matterId: "def-456"}` |
| `?data=mode%3Dedit&theme=dark` | `{mode: "edit", theme: "dark"}` (envelope wins on key conflict) |
| `(invalid URL)` | `{}` (graceful) |

---

*Spike doc author: `task-execute` skill / R4-003 / 2026-06-10*
