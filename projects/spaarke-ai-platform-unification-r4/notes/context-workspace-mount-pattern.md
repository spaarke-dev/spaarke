# Context → Workspace mount pattern (R4 task 043 / W-5)

> **Status**: Implemented (code only — deploy deferred to Phase 7)
> **Sibling task**: 042 (W-4) — Assistant → Workspace mount source
> **FR coverage**: FR-03 (Context-pane mount source for the Workspace pane)
> **Last updated**: 2026-05-26

---

## TL;DR

Task 043 establishes the canonical pattern for **Context-pane wizards/tools to promote their result into a workspace tab** via PaneEventBus `widget_load` dispatch on the `workspace` channel. The receiving widget resolves via `WorkspaceWidgetRegistry` and renders as a new tab — same mechanism as task 042 (W-4), with the dispatch site in the Context pane instead of the Assistant pane.

**Pattern is opt-in.** Each wizard/tool that adopts the pattern adds an "Also add to Workspace" (or equivalent) control whose default is OFF. Production behavior (modal launch, inline result, etc.) remains unchanged when the option is OFF.

---

## Wizard chosen

**`SemanticSearchCriteriaTool`** (`src/solutions/SpaarkeAi/src/components/context/SemanticSearchCriteriaTool.tsx`).

**Rationale for not picking Create Project** (the task's recommended candidate):
- `CreateProjectWizard` (`sprk_createprojectwizard`) runs in a **separate web-resource iframe** launched via `Xrm.Navigation.navigateTo({ target: 2 })`. The wizard's React tree is NOT inside the SpaarkeAi `<PaneEventBusProvider>`, so `useDispatchPaneEvent()` from the wizard component would silently no-op (no provider context).
- Cross-iframe dispatch would require a new mechanism (`postMessage` + a SpaarkeAi-side bridge subscriber) which is OUT OF SCOPE per the task spec ("Wiring all Context wizards is OUT OF SCOPE — pattern-establishment, not coverage") and would invalidate the 042-mirroring promise.
- `SemanticSearchCriteriaTool` is **in-process** (rendered inside SpaarkeAi's PaneEventBusProvider) so the 042 dispatch pattern applies verbatim.

The task spec explicitly permits this pivot: *"if Create Project doesn't have a clean completion hook, alternatives are fine"*.

---

## Files modified / created

| Path | Role |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/SearchCriteriaResultWidget.tsx` | NEW — receiving widget; renders captured criteria summary |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-search-criteria-result-widget.ts` | NEW — registers widget under `'search-criteria-result'` |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | Side-effect import + barrel export of the new widget |
| `src/solutions/SpaarkeAi/src/components/context/SemanticSearchCriteriaTool.tsx` | Added "Also add to Workspace" Checkbox + dispatch logic |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/SearchCriteriaResultWidget.test.tsx` | NEW — 13 component tests |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/register-search-criteria-result-widget.test.ts` | NEW — 4 registry tests |

**PaneEventTypes.ts**: NOT modified. The `WorkspaceWidgetLoadEvent` interface (added by 042) is reused verbatim — exactly the parallel-group E coordination outcome the POML notes anticipated.

**Bundle delta**: +1.67 KB gzip on SpaarkeAi (921.23 → 922.90 KB) — well under NFR-08 budget.

---

## The pattern (3 ingredients)

### 1. A receiving widget in `@spaarke/ai-widgets`

```typescript
// SearchCriteriaResultWidget.tsx
export interface SearchCriteriaResultWidgetData {
  query: string;
  domain: string;
  // ... (typed, no `any`)
}
const SearchCriteriaResultWidget: React.FC<
  WorkspaceWidgetProps<SearchCriteriaResultWidgetData>
> = ({ data, widgetType, isLoading, error }) => { ... };
export default SearchCriteriaResultWidget;
```

Standards:
- Lives in `@spaarke/ai-widgets` per ADR-012 (shared lib).
- Fluent v9 tokens only per ADR-021.
- React 19 functional component per ADR-022.
- `data` is the typed payload from the dispatcher; defensive narrowing via a runtime type guard inside the component handles wrong-shaped payloads safely.
- No BFF calls in v1 (the criteria are pure user input — ADR-028 trivially satisfied).

### 2. A one-file registration shim

```typescript
// register-search-criteria-result-widget.ts
export const SEARCH_CRITERIA_RESULT_WIDGET_TYPE = 'search-criteria-result' as const;
registerWorkspaceWidget(
  SEARCH_CRITERIA_RESULT_WIDGET_TYPE,
  { displayName: 'Search Criteria', category: 'analysis', icon: 'SearchRegular', allowMultiple: true, defaultOrder: 160 },
  () => import('./SearchCriteriaResultWidget') as Promise<{ default: WorkspaceWidgetComponent }>
);
```

The widget is lazy-loaded by the registry factory — no initial-bundle hit. The `index.ts` barrel imports the registration as a side effect so the widget is available before any shell mounts.

### 3. The dispatch site (the wizard/tool's "Add result to Workspace" affordance)

```typescript
// SemanticSearchCriteriaTool.tsx
import { useDispatchPaneEvent, SEARCH_CRITERIA_RESULT_WIDGET_TYPE } from '@spaarke/ai-widgets';
import type { SearchCriteriaResultWidgetData } from '@spaarke/ai-widgets';

const [addToWorkspace, setAddToWorkspace] = React.useState(false); // default OFF
const dispatch = useDispatchPaneEvent();

const handleSearchClick = () => {
  if (addToWorkspace) {
    const widgetData: SearchCriteriaResultWidgetData = { query: criteria.query, domain: criteria.domain, /* ... */ };
    dispatch('workspace', {
      type: 'widget_load',
      widgetType: SEARCH_CRITERIA_RESULT_WIDGET_TYPE,
      widgetData,
      displayName: `Search: ${criteria.domain}`,
    });
    setAddToWorkspace(false); // reset after dispatch — re-affirm each time
  }
  launchSemanticSearch(criteria); // production path runs regardless
};
```

UI: a Fluent v9 `<Checkbox>` labeled "Also add to Workspace" above the primary Search button. Default OFF. Resets to OFF after each dispatch.

---

## How a future Context wizard adopts the pattern

1. **Build (or pick) a receiving widget** in `@spaarke/ai-widgets/src/widgets/workspace/`. If your result already has a suitable viewer (e.g. `DocumentViewerWidget`, `RedlineViewerWidget`), reuse it — don't create a new widget unless the payload shape is genuinely different.
2. **Register the widget** via a one-file registration shim (`register-<your-widget>.ts`). Import it as a side effect from `@spaarke/ai-widgets/src/index.ts`.
3. **Export the type ID constant** and the `WidgetData` interface from the package barrel.
4. **Add an "Also add to Workspace" Checkbox** to your wizard's completion step. Default UNCHECKED. Reset to OFF after each dispatch.
5. **Dispatch on click** if checked — construct the typed payload, call `dispatch('workspace', { type: 'widget_load', widgetType, widgetData, displayName })`.
6. **Add tests** mirroring `SearchCriteriaResultWidget.test.tsx` + `register-search-criteria-result-widget.test.ts` (the 042 sibling tests are equivalent references).

---

## ADR compliance summary

| ADR | How task 043 satisfies it |
|---|---|
| ADR-012 (Shared component library) | Receiving widget lives in `@spaarke/ai-widgets`; context-agnostic; consumed only via the public barrel surface |
| ADR-021 (Fluent design system) | All UI uses Fluent v9 semantic tokens; no hex / rgba / Fluent v8 |
| ADR-022 (React 19 Code Pages) | All new components are React 19 functional with hooks |
| ADR-028 (Spaarke Auth v2) | No token snapshots; no BFF call at the dispatch site (criteria are pure user input) |
| ADR-030 (PaneEventBus) | Reuses `WorkspaceWidgetLoadEvent` from PaneEventTypes.ts (no `any` payloads); reuses the closed four-channel union; dispatches via the public `useDispatchPaneEvent` hook |

---

## Why this isn't deployed yet

Per task spec guardrail in the parent task brief: **"DO NOT DEPLOY — code + build verify only; deploys batched at Phase 7"**. The code builds clean, 17/17 new unit tests pass, and `tsc --noEmit` returns zero errors against my new files. The deploy is scheduled as part of the Phase 7 wave.

---

## Demo walkthrough (once deployed)

1. Open SpaarkeAi (cold load) in the dev environment.
2. In the Context pane (right pane), open the Tools dropdown → select "Semantic Search".
3. Fill the criteria: type "indemnity clauses", pick Documents domain, optionally set filters.
4. Check the new **"Also add to Workspace"** checkbox.
5. Click **Search**.
6. Observe two things:
   - A new tab labeled **"Search: indemnity clauses"** appears in the Workspace pane and shows the criteria summary card.
   - The Semantic Search modal opens (the production path is preserved).
7. Repeat with the checkbox UNCHECKED — verify NO new tab is added.
8. Toggle dark mode (D365 settings) — re-run the demo; verify the criteria summary card renders cleanly in dark mode.
