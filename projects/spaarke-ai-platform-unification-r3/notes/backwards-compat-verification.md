# Backwards Compatibility + NFR-09 Persistence Verification

> **Task**: 063
> **Phase**: F — Integration & Verification
> **Mode**: Static / read-only verification (no deployed-env access)
> **Date**: 2026-05-20
> **Verifier**: Claude Code (task-execute STANDARD rigor)
> **Scope**: FR-25, NFR-10, NFR-09 + companion FR-21 / FR-14 invariants

---

## Executive Summary

| Verification | Subject | Result |
|---|---|---|
| (a) | Standalone LegalWorkspace shows 9 layout templates (FR-25, FR-14 default) | ✅ PASS |
| (b) | Existing user layouts render identically (FR-25, NFR-10) | ✅ PASS |
| (c) | Non-Home tabs persist across page refresh (NFR-09) | ❌ **GAP FOUND** (UQ-03) |
| (d) | PlaybookGalleryWidget remains registered for non-welcome stages (FR-21) | ✅ PASS |
| (e) | Existing wizard widgets unchanged (FR-25) | ✅ PASS |

**Phase G Gate Status**: ⚠️ **NOT BLOCKED** by this verification per spec UQ-03 / design.md §H acknowledgement. The NFR-09 gap was foreseen and explicitly tracked as a follow-up — recommend opening **task 065** (extend `SessionPersistenceService` to serialize tabs array) for Phase H wrap-up, or accept current behavior as documented limitation.

---

## (a) Standalone LegalWorkspace shows all 9 layout templates (FR-25, FR-14 default)

### Evidence

**File**: `src/solutions/WorkspaceLayoutWizard/src/steps/TemplateStep.tsx` (lines 191-195)

```tsx
const visibleTemplates = React.useMemo(() => {
  if (!templateFilter) return LAYOUT_TEMPLATES;
  const allowed = new Set<LayoutTemplateId>(templateFilter);
  return LAYOUT_TEMPLATES.filter((t) => allowed.has(t.id));
}, [templateFilter]);
```

When `templateFilter` is `undefined` (falsy), the unfiltered `LAYOUT_TEMPLATES` (canonical 9-template list) is returned directly.

**File**: `src/solutions/WorkspaceLayoutWizard/src/main.tsx` (lines 116-126)

```tsx
const templateFilterRaw = parsed.get("templateFilter") || "";
const templateFilter: readonly LayoutTemplateId[] | undefined =
  templateFilterRaw
    ? (templateFilterRaw.split(",").map(...).filter(...) as ...)
    : undefined;
```

When the `templateFilter` URL query parameter is absent (the standalone LegalWorkspace launch path), `templateFilter` is `undefined`, which propagates to `<App>` → `<TemplateStep>` and triggers the unfiltered branch.

### Tests Confirm

**File**: `src/solutions/WorkspaceLayoutWizard/src/steps/__tests__/TemplateStep.test.tsx`

| Test | Assertion |
|---|---|
| `render_NoTemplateFilter_RendersAllNineCanonicalTemplates` (line 55) | `expect(cards).toHaveLength(9)` |
| `render_NoTemplateFilter_RendersEachCanonicalTemplateName` (line 65) | All 9 canonical names present in DOM |
| `render_TemplateFilterUndefined_RendersAllNineTemplates` (line 76) | Explicit `undefined` ≡ omitting prop |
| `canonicalLayoutTemplates_AreExactlyNine` (line 188) | `LAYOUT_TEMPLATES.length === 9` (regression guard) |

**Verdict**: ✅ PASS

---

## (b) Existing user layouts render identically via `/api/workspace/layouts` (FR-25, NFR-10)

### Evidence — Daily Briefing section additive only

**File**: `src/solutions/LegalWorkspace/src/sectionRegistry.ts`

```ts
export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  dailyBriefingRegistration,   // <- task 034 addition
] as const;
```

`git diff master --stat -- src/solutions/LegalWorkspace/` shows:
```
sectionRegistry.ts             |   2 +   (one import line + one array entry)
sections/dailyBriefing/...     | 779 + (entirely new dailyBriefing folder)
sections/index.ts              |   1 +
5 files changed, 782 insertions(+)
```

**Net effect**: 1 new entry appended to `SECTION_REGISTRY`. No existing registration modified. No existing `defaultHeight`, `id`, or `category` changed.

### Evidence — useWorkspaceLayouts unchanged

`git diff master -- src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` → **empty diff**. R3 made zero changes.

### Evidence — WorkspaceShell unchanged

`git diff master -- src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/` → **empty diff**. All 9 WorkspaceShell files (incl. `WorkspaceShell.tsx`, `layoutTemplates.ts`, `SectionPanel.tsx`, `ActionCard.tsx`, etc.) are byte-identical to master.

**Verdict**: ✅ PASS — existing user layouts will render identically because none of the rendering primitives were touched.

---

## (c) Non-Home tabs persist across page refresh (NFR-09) — GAP FOUND

### Evidence — WorkspaceTabManager has no persistence layer

**File**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts`

The class is a plain in-memory state container. `git grep` of the workspace folder confirms:
- No `localStorage` read/write of tabs
- No `sessionStorage` read/write of tabs (the one `sessionStorage` hit in `WorkspacePaneMenu.tsx:339` is for active **layout** pinning, not tabs)
- No write-through call to `SessionPersistenceService`
- No `restoreTabs()` / `persistTabs()` hook
- On reload `WorkspacePane` instantiates a fresh `WorkspaceTabManager` and calls `ensureHomeTab()` only

### Evidence — SessionPersistenceService is server-side BFF/Cosmos, not tabs

Grep results for `SessionPersistenceService` resolve only to:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionPersistenceService.cs` (server)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/StoredSession.cs` (server schema)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionRestoreService.cs` (server)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs` (server)

The R2 service writes AI **session state** (chat history, playbook context, etc.) to Cosmos — it does NOT serialize the SpaarkeAi workspace tabs array. The client never calls a `setTabs([...])` endpoint.

### Spec acknowledgement

The gap was anticipated:
- `spec.md` UQ-03 (line 277): *"does R2's SessionPersistenceService already serialize the tabs list, or only individual widget state? Blocks: NFR-09 verification — may surface a backend sub-task if not covered."*
- `spec.md` A-4 (line 267): *"…the tabs list across reloads is assumed to be covered. If verification (§G #13) reveals gaps, extend SessionPersistenceService to include the tabs array — tracked as design.md §H follow-up."*
- `design.md` R-9 (line 420): *"Session-restore (R2 D-08) currently restores widget state but may not include the tabs-list across reloads… Confirm coverage in §G #9-13; if missing, extend SessionPersistenceService to include tabs array."*
- `README.md` R-5 (line 106): mitigation = "NFR-09 verification step; if gap surfaces, extend SessionPersistenceService (per UQ-03)"
- `plan.md` line 186: *"Session persistence of non-Home tabs (NFR-09 / UQ-03): may require BFF/Cosmos extension. Mitigation: verify in Phase F task 063; opens sub-task if gap surfaces."*

### Recommended Follow-up

Open a new POML task (suggested ID: **065-extend-sessionpersistence-tab-state**) with the following scope:

1. Extend `StoredSession` schema with a `tabs: WorkspaceTab[]` field (id, kind, widgetType, displayName — exclude `Component` and `widgetData` if too large; rely on D-08 data-refreshed restore to rehydrate widget data via re-fetch).
2. Wire `WorkspaceTabManager` to dispatch a write-through call after each `addTab` / `closeTab` / `setActiveTab` / `clearAllTabs` mutation, using `authenticatedFetch` per ADR-028.
3. Wire `WorkspacePane` to read the tabs array from `SessionRestoreService` response on mount and replay `addTab(widgetType, data, displayName)` for each restored tab in canonical order, then `setActiveTab(activeTabId)`.
4. p95 restore latency < 500 ms target inherited from D-08.
5. Acceptance: opening 3 non-Home tabs, refreshing, and observing all 3 tabs + the selected active tab restored within 500 ms.

**Note**: Per the parent task instructions, this follow-up is documented but NOT actually opened by this verification task. The phase-G gate is described in the task POML as conditional — but spec UQ-03 / design.md §H already treat this as Phase H wrap-up scope, not a Phase G blocker. Recommend confirming with project owner whether to:
- (Option 1) Treat as Phase H follow-up and proceed with Phase G (consistent with spec UQ-03 wording).
- (Option 2) Open task 065 now and block Phase G (consistent with task 063 acceptance criterion #5 stricter wording).

**Verdict**: ❌ **GAP FOUND** — tabs are NOT persisted across refresh. This was a known unknown (UQ-03) and the gap is now confirmed.

---

## (d) PlaybookGalleryWidget remains registered for non-welcome stages (FR-21)

### Evidence — Registration retained

**File**: `src/client/shared/Spaarke.AI.Widgets/src/index.ts` (lines 246-255)

```ts
export { default as PlaybookGalleryWidget } from './widgets/context/PlaybookGalleryWidget';
export type {
  PlaybookGalleryData,
  PlaybookSummary,
} from './widgets/context/PlaybookGalleryWidget';

registerContextWidget('playbook-gallery', {
  factory: () =>
    import('./widgets/context/PlaybookGalleryWidget').then((m) => ({ default: m.default })),
});
```

The `'playbook-gallery'` type key is still registered in `ContextWidgetRegistry`. Also redundantly registered in `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` (lines 51-54).

### Evidence — ContextPaneController only swaps the welcome render path

**File**: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` (lines 575-589)

```tsx
// PlaybookGalleryWidget remains REGISTERED in ContextWidgetRegistry (see
// @spaarke/ai-widgets/src/index.ts) so any future server-driven
// context_update that requests 'playbook-gallery' on a non-welcome stage
// resolves correctly. We do NOT auto-load it here anymore — the welcome
// stage is now the GetStarted entry point per FR-18 / the R3 design.
if (currentStage === "welcome") {
  return (
    <div className={styles.content} data-testid="context-pane-welcome">
      <GetStartedCardsWidget onCardClick={handleGetStartedCardClick} />
    </div>
  );
}
```

The welcome-stage swap (FR-18, task 042) only affects the welcome stage render. For non-welcome stages, a server `context_update` event with `contextType: 'playbook-gallery'` would still resolve through `resolveContextWidget('playbook-gallery')` → `PlaybookGalleryWidget`.

**Verdict**: ✅ PASS

---

## (e) Existing wizard widgets unchanged (FR-25)

### Evidence

```
git diff master --stat -- \
  src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/CreateMatterWizardWidget.tsx \
  src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DocumentUploadWizardWidget.tsx
```

Result: **empty diff** — both files are byte-identical to master.

These two widgets are referenced as `'create-matter-wizard'` and `'document-upload-wizard'` widget types in the GetStartedCards mapping (task 041) but their source code was not modified by R3.

Note: R3 added NEW widget wrappers in tasks 043/044/045 (`CreateProjectWizardWidget`, `FindSimilarWizardWidget`, `EmailComposeWidget`, `MeetingScheduleWidget`, `AssignWorkWizardLauncher`) — but these are net-new files, not modifications to existing widgets.

**Verdict**: ✅ PASS

---

## Method Notes & Limitations

1. **Static / read-only verification.** The parent task instructions explicitly requested static verification using source code + git diffs against master, not browser-based exercising of a deployed environment. The original task POML described a deployed-env workflow with screenshots; that interactive workflow is deferred to the Phase G smoke tasks (071, 072, 073, 074) which run in a deployed environment.

2. **NFR-09 (c) cannot be fully validated statically.** Static analysis confirms the architectural absence of a tab persistence pipeline — sufficient to conclude tabs will NOT survive refresh. A definitive end-to-end test would still occur in Phase G smoke. Phase G can either accept the gap as documented or block on task 065 completion.

3. **Daily Briefing addition is additive.** Verified by `git diff --stat` showing only insertions (782 +, 0 −) under LegalWorkspace.

---

## Phase G Gate Verdict

**Recommendation**: ⚠️ **Phase G PROCEED with documented gap**

Rationale:
- (a), (b), (d), (e) all PASS — no regression in standalone LegalWorkspace or existing widgets.
- (c) is a known unknown (UQ-03) explicitly carved out as a Phase H follow-up in spec / design / plan / README.
- The current task's acceptance criterion #5 offers two paths: PASS or "follow-up opened". This verification documents the gap; the project owner can decide whether to open task 065 immediately (block Phase G) or defer to Phase H (proceed to Phase G).

**Recommended Phase H follow-up**: Open task **065** to extend `SessionPersistenceService` with workspace-tabs serialization per scope listed in section (c) above.

---

*Verification complete. Memo author: Claude Code task-execute (STANDARD rigor).*
