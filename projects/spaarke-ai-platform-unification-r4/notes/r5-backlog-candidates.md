# R5 Backlog Candidates (deferred from R4)

> **Date**: 2026-05-26
> **Source**: R4 discoveries that operator decided to defer rather than file as R4 tasks
> **Status**: Not started — capture for R5 scoping

These items surfaced during R4 execution but were intentionally deferred per operator decision (2026-05-26). They are NOT R4 acceptance criteria — file them when R5 is scoped.

---

## 1. Iframe-wizards-as-mount-sources (broader pattern)

**Discovery**: R4 task 043 (W-5 Context → Workspace mount) attempted to wire CreateProjectWizard as the mount-source dispatcher but pivoted to `SemanticSearchCriteriaTool` after discovering CreateProjectWizard runs in a separate web-resource iframe **outside** the SpaarkeAi `<PaneEventBusProvider>` scope. `useDispatchPaneEvent()` would silently no-op.

**Scope of the broader pattern problem**:
- All iframe-based wizards in SpaarkeAi share this limitation
- For wizards to participate in mount-source dispatch, they need EITHER:
  - (a) postMessage bridge from iframe → parent shell, which then dispatches `widget_load`
  - (b) iframe wizards moved to in-process React components (where they can use PaneEventBus directly)
  - (c) Alternative dispatch mechanism (e.g., Xrm.Navigation completed events)

**Why deferred from R4**: 043 worked around it via in-process SemanticSearchCriteriaTool. Designing a broader iframe-wizards strategy is **strategy-level work**, not a single-task implementation. Pattern doc captured at `projects/spaarke-ai-platform-unification-r4/notes/context-workspace-mount-pattern.md`.

**R5 candidate scope**: Design + implement one iframe-bridge prototype (e.g., CreateProjectWizard postMessage → parent dispatcher). Evaluate vs the in-process migration option.

---

## 2. WorkspaceRenderer type-narrowing wrapper (052 cleanup)

**Discovery**: Task 052 (C-4 WorkspaceRenderer interface) required `LegalWorkspaceApp as unknown as WorkspaceRenderer` due to contravariance between `IWebApi` (LegalWorkspace's strict shape, methods REQUIRED) and `WorkspaceRendererWebApi` (shared lib's loose shape, methods OPTIONAL).

**Why deferred from R4**: The cast is correct for today's caller (frame-walked Xrm exposes all methods). The fix is structural: introduce a type-narrowing wrapper that re-asserts `webApi` has the required methods. Not blocking; documented inline + in `notes/c4-interface-design.md`.

**R5 candidate scope**: Implement type-narrowing wrapper `LegalWorkspaceRendererTyped` that accepts loose `WorkspaceRendererWebApi`, narrows to strict `IWebApi`, and delegates to LegalWorkspaceApp. Eliminate the `as unknown as` cast. ~2-3h.

---

## Notes for R5 scoping

When R5 is scoped:
1. Include these two items in initial backlog
2. Re-evaluate priority (both Low at time of R4 close)
3. The iframe-wizards strategy item benefits from waiting — more iframe wizards may surface as candidates after R4 deploys land in dev
4. The type-narrowing wrapper is purely cosmetic; could remain forever as documented technical debt without harm

---

*Maintainer note: this file is the R4-to-R5 handoff for deferred items. R4's Phase 7 wrap-up (task 090) should reference this file in lessons-learned.*
