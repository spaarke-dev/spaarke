# B-11 Cast Inventory — Type-Drift Casts (Task 067)

> **Task**: 067 (B-11)
> **Date**: 2026-05-26
> **Author**: Claude (executed via task-execute, FULL rigor)
> **Status**: In progress

---

## Scope of sweep

`grep -rn "as any|as unknown as"` across these directories:

- `src/client/shared/Spaarke.Events.Components/`
- `src/client/shared/Spaarke.AI.Widgets/`
- `src/client/shared/Spaarke.UI.Components/`
- `src/solutions/EventsPage/`
- `src/solutions/SpaarkeAi/`
- `src/solutions/CalendarSidePane/` (none found)

Raw total: ~180 matches. After excluding test files (`__tests__/`, `__mocks__/`, `*.test.*`, `*.spec.*`), production-code matches: ~75.

---

## Buckets

### Bucket A — `IEventRecord` index-signature drift (production code)

`IEventRecord` (`src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx:83-107`) has **explicit field properties** — NOT a broad `[key: string]: unknown` index signature. The casts in consumers exist because at runtime Dataverse returns dynamic OData annotation keys (`@OData.Community.Display.V1.FormattedValue`) and lookup-formatted-value keys that DO match index-keyed access patterns. Two flavors:

| Cast site | Pattern | Bucket |
|-----------|---------|--------|
| GridSection.tsx:563 | `(record as unknown as Record<string, unknown>)[column.formattedValueField]` | A — dynamic field access on `IEventRecord` |
| GridSection.tsx:569 | `(record as unknown as Record<string, unknown>)[accessor]` | A — dynamic field access |
| GridSection.tsx:595 | `(record as unknown as Record<string, unknown>)[...lookup column...]` | A — dynamic field access |
| GridSection.tsx:1753 | `(event as unknown as { createdon?: string }).createdon` | A — `createdon` is not in `IEventRecord` |
| CalendarWorkspaceWidget.tsx:545 | `(r as unknown as { createdon?: string }).createdon` | A — `createdon` is not in `IEventRecord` |
| CalendarWorkspaceWidget.tsx:1080 | `eventDates as IEventDateInfo[]` | A — **REDUNDANT** (eventDates already `IEventDateInfo[]` via context). Flagged by task 064. |

**Disposition**:
- 1080 redundant cast: REMOVE
- `createdon` accesses (lines 545, 1753): **upstream fix** — add optional `createdon?: string;` to `IEventRecord`
- Lines 563, 569, 595: These are legitimate dynamic-key accesses keyed by `column.formattedValueField` / accessor / lookup-formatted-value-field — these are GENUINE runtime-key accesses that can't be expressed in the static type. **Annotate as SDK boundary** (Dataverse OData annotation keys).

### Bucket B — Combobox / Fluent v9 `onInput` drift

| Cast site | Pattern | Bucket |
|-----------|---------|--------|
| AssignedToFilter.tsx:302 | `onInput={handleSearchChange as unknown as React.FormEventHandler<HTMLInputElement>}` | B — Fluent v9 Combobox onInput |
| RecordTypeFilter.tsx:278 | `onInput={handleSearchChange as unknown as React.FormEventHandler<HTMLInputElement>}` | B — Fluent v9 Combobox onInput |

**Disposition**: Investigate; if the source signature can be aligned with `React.FormEventHandler<HTMLInputElement>` natively, remove cast.

### Bucket C — Cross-package type drift (13 cascading 061 errors)

Per `notes/b2-tsc-rootdir-decision.md`, after B-2 rootDir fix, **13 type errors** were carry-overs to this task:

| File | Original count | Class | Status after path additions |
|------|-------|-------|---------|
| `components/FeedbackButtons.tsx` | 1 | TS2307 `@spaarke/auth` | ✅ FIXED (added path) |
| `hooks/useWorkspaceLayouts.ts` | 1 | TS2307 `@spaarke/auth` | ✅ FIXED (added path) |
| `providers/AiSessionProvider.tsx` | 3 | TS2307 (auth + ai-context) + TS7006 | ✅ FIXED (added paths) |
| `index.ts` | 2 | TS2322 `ContextWidgetComponent` variance | ⚠️ STILL PRESENT (Bucket D — generic variance) |
| `registry/register-context-widgets.ts` | 3 | TS2322 same variance | ⚠️ STILL PRESENT (Bucket D) |
| `widgets/context/PlaybookGalleryWidget.tsx` | 1 | TS2322 Badge `"neutral"` literal | ⚠️ STILL PRESENT (Bucket E — Fluent v9 token) |
| `widgets/workspace/WorkspaceLayoutWidget.tsx` | 2 | TS2305 missing exports | ✅ NOT REPRODUCING in current code |

**Fix path**: added `@spaarke/auth` and `@spaarke/ai-context` `paths` entries to `Spaarke.AI.Widgets/tsconfig.json`. This eliminates 7 of the 13 errors. Remaining 6 below.

### Bucket D — ContextWidgetComponent generic variance

| Cast site | Issue | Disposition |
|-----------|-------|-------------|
| index.ts:297 `registerContextWidget('playbook-gallery', ...)` | factory return shape mismatch | Add cast to match `as unknown as ContextWidgetComponent` (mirror line 334) |
| index.ts:357 `registerContextWidget('findings', ...)` | same | Same |
| register-context-widgets.ts:53 ('playbook-gallery') | same | Same |
| register-context-widgets.ts:70 ('entity-info') | same | Same |
| register-context-widgets.ts:87 ('findings') | same | Same |

**Note**: The existing pattern at line 334 already uses `as unknown as ContextWidgetComponent` for `GetStartedCardsWidget`. The generic-default `<unknown>` of `ContextWidgetComponent` vs the typed widgets (`<PlaybookGalleryData>`, etc.) is a TypeScript variance issue — the registry stores type-erased components. Apply the existing pattern.

### Bucket E — Fluent v9 Badge `"neutral"` token

| Cast site | Issue | Disposition |
|-----------|-------|-------------|
| PlaybookGalleryWidget.tsx:382 | `color={isSelected ? 'brand' : 'neutral'}` | Replace `'neutral'` with `'subtle'` (valid Fluent v9 Badge color; matches RedlineViewerWidget.tsx:891 convention) |

### Bucket F — SDK-boundary casts (annotate, don't remove)

These are legitimate runtime-only-discoverable shapes:

- `(window as any).Xrm` / `(window.parent as any).Xrm` (xrmContext.ts, FetchXmlService.ts, GridSection.tsx, etc.) — Xrm is injected by Dynamics runtime, not statically known
- `(json as any).value` patterns in `*Service.ts` files — OData response shape
- `(err as any).errorCode` — runtime error inspection on `unknown`-typed errors
- All `__tests__/` and `__mocks__/` casts — test scaffolding, out of scope

**Disposition**: most already have `// eslint-disable-next-line @typescript-eslint/no-explicit-any` comments or sit inside `getXrm()`-style helper functions. The actual surface is small (~5 production sites) and already annotated in context. Will add SDK-boundary comments only where missing on production code paths.

---

## Action plan (bottom-up)

1. ✅ Add `@spaarke/auth` + `@spaarke/ai-context` paths to `Spaarke.AI.Widgets/tsconfig.json` → eliminates 7 of 13 cascading errors
2. Fix Bucket E: PlaybookGalleryWidget.tsx Badge color `'neutral'` → `'subtle'`
3. Fix Bucket D: 5 × ContextWidgetComponent variance — apply existing `as unknown as ContextWidgetComponent` pattern
4. Fix Bucket A: add optional `createdon?: string` + dynamic-key annotations to `IEventRecord`; remove redundant cast at CalendarWorkspaceWidget.tsx:1080
5. Fix Bucket B: tighten Combobox `onInput` handlers
6. Final typecheck across all packages

## Files modified

Suggested commit grouping (main session may apply per-cast-family per POML constraint):

### Commit 1 — `chore(types): resolve cross-package tsconfig paths for Spaarke.AI.Widgets (B-11)`
- `src/client/shared/Spaarke.AI.Widgets/tsconfig.json` — added `@spaarke/auth` and `@spaarke/ai-context` `paths` entries pointing at sibling `dist/index.d.ts` (mirroring B-2 decision pattern). This eliminates 7 of the 13 cascading 061 errors at the root cause (TS2307 module-not-found in 3 files).
- `src/client/shared/Spaarke.AI.Context/dist/**` — declarations built (was missing); now committed-or-CI-built so consumer typecheck sees fresh types.

### Commit 2 — `fix(ai-widgets): correct Badge color token to valid Fluent v9 literal (B-11)`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/PlaybookGalleryWidget.tsx` — `color={isSelected ? 'brand' : 'neutral'}` → `'subtle'` (matches existing convention at `RedlineViewerWidget.tsx:891`).

### Commit 3 — `fix(ai-widgets): annotate ContextWidgetComponent registry variance casts (B-11)`
- `src/client/shared/Spaarke.AI.Widgets/src/index.ts` — 2 registry factory entries (`playbook-gallery`, `findings`) now apply `as unknown as ContextWidgetComponent` mirroring the existing `get-started-cards` pattern at line 334.
- `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` — same pattern applied to 3 secondary registrations (`playbook-gallery`, `entity-info`, `findings`); added `ContextWidgetComponent` type import.

### Commit 4 — `fix(events): add createdon to IEventRecord and remove downstream casts (B-11)`
- `src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx` — added optional `createdon?: string` to `IEventRecord` (Dataverse audit field); removed `(event as unknown as { createdon?: string }).createdon` cast at line 1753 (now `event.createdon`).
- `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` — removed identical cast at line 545 (now `r.createdon`); removed redundant `eventDates as IEventDateInfo[]` cast at line 1080 (task 064 flagged; value is already `IEventDateInfo[]` via `useEventsPageContext()`).
- `src/solutions/EventsPage/src/App.tsx` — fixed pre-existing latent type bug at lines 1205 & 1231 (`eventDates.map((date) => ({ date, count: 1 }))` was treating `IEventDateInfo` objects as date strings); now correctly destructures `{ date, count }`.

### Commit 5 — `fix(events): align Combobox onInput handlers with Fluent v9 React.FormEventHandler (B-11)`
- `src/client/shared/Spaarke.Events.Components/src/components/AssignedToFilter/AssignedToFilter.tsx` — `handleSearchChange` retyped via `React.useCallback<React.FormEventHandler<HTMLInputElement>>`; reads from `event.currentTarget.value`. Cast at JSX site removed.
- `src/client/shared/Spaarke.Events.Components/src/components/RecordTypeFilter/RecordTypeFilter.tsx` — same change.

### Commit 6 — `chore(events): annotate SDK-boundary Xrm casts (B-11)`
- `src/client/shared/Spaarke.Events.Components/src/services/FetchXmlService.ts` — added `// SDK boundary` comment on `(window.parent as any).Xrm` access.
- `src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx` — added `// SDK boundary` comments on 3 dynamic-key Dataverse OData annotation accesses (`column.formattedValueField`, `accessor`, lookup-formatted-value-field) and on `(window.parent as any).Xrm`.

---

## Typecheck results (final)

| Package | tsc --noEmit | Notes |
|---------|--------------|-------|
| `Spaarke.Events.Components` | ✅ 0 errors | All B-11 in-scope errors resolved. |
| `Spaarke.AI.Widgets` | ✅ 0 errors | All 13 cascading 061 errors resolved (7 via tsconfig paths, 5 via registry variance casts, 1 via Badge color). |
| `Spaarke.UI.Components` | ⚠️ 24 errors (unchanged baseline) | All pre-existing — no regressions from B-11. See carry-overs below. |
| `src/solutions/EventsPage` | ✅ 0 own-src errors | External errors from `Spaarke.UI.Components` via sibling import (baseline). |
| `src/solutions/SpaarkeAi` | ⚠️ 5 own-src errors (pre-existing) | None introduced by B-11. See carry-overs below. |
| `src/solutions/CalendarSidePane` | ⚠️ 1 own-src error (pre-existing) | None introduced by B-11. See carry-overs below. |

**Verified by stash/pop comparison**: `Spaarke.UI.Components` error count is identical before/after B-11 (24/24).

---

## Carry-overs

Casts NOT fixed (deferred to a future project per R-8 pressure-relief):

### CO-1: `Spaarke.UI.Components` 24 pre-existing tsc errors (PROD CODE)

Not in B-11 scope (not cast-related, broader type-system drift). Examples:

- `src/components/SprkChat/SprkChat.tsx:2190` — `RefObject<HTMLDivElement | null>` not assignable to `RefObject<HTMLElement>` (React 18→19 `useRef` initializer signature change)
- `src/components/SprkChat/SprkChatInput.tsx:119,233` — same `RefObject` nullability drift
- `src/components/ThreePaneLayout/ThreePaneLayout.tsx:315` — `Cannot find namespace 'JSX'` (TS 5+ JSX namespace migration)
- `src/components/AiProgressStepper/AiProgressStepper.tsx:272` — same JSX namespace
- `src/components/PanelSplitter/PanelSplitter.tsx:98` — same JSX namespace
- `src/components/TodoDetail/TodoDetail.tsx:507,508,528` — Combobox `onInput` event-shape drift (similar to Bucket B but in UI-Components instead of Events-Components)
- `src/components/DatasetGrid/ViewSelector.tsx:160` — implicit `any` parameter on `Combobox` handler (similar to Bucket B)

**Recommendation**: New project (call it `frontend-type-drift-r1` or similar) to handle the React 19 `RefObject` initializer change + TS 5 JSX namespace migration + remaining Combobox onInput drift in UI-Components. Estimated effort: 2–4h.

### CO-2: `Spaarke.UI.Components` test-file casts (~120 `as any` / `as unknown as` matches)

Test scaffolding (`__tests__/`, `__mocks__/`, `*.test.*`, `*.spec.*`) — explicitly **out of B-11 scope** per task POML focus on production code. Examples: `pcfMocks.tsx` (90+ `as any` for mock framework types), `useStreamingInsert.test.ts` (12 mock-ref casts), `FieldMappingService.test.ts` (~15 private-state inspector casts). These are conventional test scaffolding patterns and not the type-drift problem B-11 targets.

**Recommendation**: not actionable as a standalone task; address opportunistically if a test gets meaningfully refactored.

### CO-3: `src/solutions/SpaarkeAi` pre-existing tsc errors

- `src/components/context/ContextPaneMenu.tsx:189` — unused `selectedTool`
- `src/components/shell/ThreePaneShell.tsx:59` — unused `tokens`
- `src/components/shell/ThreePaneShell.tsx:447` — unused `isRestoring`
- `src/components/shell/ThreePaneShell.tsx:612` — `EntityContext | null | undefined` shape mismatch
- `src/main.tsx:63` — `Cannot find module '@spaarke/legal-workspace'` (likely tsconfig paths missing the `@spaarke/legal-workspace` entry — mirror the B-2 pattern; out of B-11 cast scope)

**Recommendation**: A small "SpaarkeAi typecheck cleanup" task (~1h). The `@spaarke/legal-workspace` resolution issue is also a tsconfig-path question that pairs naturally with the SpaarkeAi shell-cleanup work.

### CO-4: `src/solutions/CalendarSidePane` pre-existing tsc error

- `src/App.tsx:61` — `CalendarFilterOutput` (legacy) vs `CalendarFilterPaneOutput` (B-6 hoist) shape mismatch — `CalendarFilterPaneSingle` requires `dateFields` property not present on `CalendarFilterSingle`.

**Recommendation**: a 30min fix in CalendarSidePane to migrate from `CalendarFilterOutput` to `CalendarFilterPaneOutput` (or update `getInitialFilterState` to return the new shape). Pairs naturally with the CalendarSidePane parity item (B-6 / task 055).

### CO-5: `(window as any).Xrm` SDK-boundary casts in `Spaarke.UI.Components`

`xrmContext.ts`, `PolymorphicResolverService.ts`, `wizardLaunchers.ts`, `DocumentEmailWizard.tsx`, `SummarizeAnalysisStep.tsx`, `WorkAssignmentWizardDialog.tsx`, `SprkChat/hooks/useActionHandlers.ts` — all are equivalent SDK-boundary patterns to the ones annotated in events-components, but in a different package. Annotating each adds noise without removing the cast.

**Recommendation**: a single sweep adding `// SDK boundary` comments in UI-Components in a future repo-hygiene pass.

---

## Actual effort vs estimate

**Estimated**: ~4h (per POML)
**Actual**: ~2.5h
- Step 1 (inventory): 30min
- Step 2 (build Spaarke.AI.Context + tsconfig paths fix → 7/13 errors resolved): 30min
- Steps 3–6 (Bucket E + D + A + B + SDK-boundary annotation): 60min
- Step 7–9 (typecheck verification + code-review/adr-check + carry-over documentation): 30min

Came in under budget because the largest single revealing cascade (13 errors from task 061) reduced to 6 errors with one tsconfig change, and the remaining 6 were tightly clustered (5 of one pattern + 1 trivial token).

