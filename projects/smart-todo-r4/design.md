# Smart To Do — R4 Design

> **Status**: Draft v2 (UAT feedback resolved 2026-06-10; ready for `/design-to-spec`)
> **Predecessor**: smart-todo-decoupling-r3 (merged via PR #373, squash `e328beaf`; wrap-up PR #374)
> **Note**: Additional enhancements may still be added before formalization — flag them and they'll fold in.

---

## Purpose

R3 decoupled `sprk_todo` from `sprk_event` and stood it up as a first-class entity. UAT surfaced **UX gaps** that block adoption: the SmartTodo Code Page header/toolbar feels inconsistent with the rest of the platform; the side-pane editing pattern has broken save + "Completed" interactions; the regarding picker needs to live on a main form so users can edit `sprk_todo` consistently inside or outside the Code Page; parent records need a visual "Upcoming To Dos" surface; and the SpaarkeAi workspace widget is still wired to the retired `sprk_event.sprk_todoflag` shape.

R4 closes these gaps by:
1. Aligning SmartTodo with the **Semantic Search PCF** pattern that has been validated across the product
2. Switching from side-pane to **modal-hosted MDA main form** (new form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6`)
3. Surfacing the regarding resolver in a **reusable, multi-env-stable** way
4. Adding a universal `< >` record-navigation pattern to all list-launched modals
5. Adding **Visual Host "Upcoming To Dos" cards** to four parent main forms with drill-through to the SmartTodo modal
6. Rebuilding the SpaarkeAi workspace widget following the documented widget structure pattern

---

## In Scope

### A. Workspace widget — rebuild against `sprk_todo`

**Symptom**: The Smart To Do widget on the SpaarkeAi workspace fails to mount with:
> *"Could not find a property named 'sprk_todoflag' on type 'Microsoft.Dynamics.CRM.sprk_event'"*

**Cause**: A deployed widget bundle (likely `@spaarke/ai-widgets` / LegalWorkspace SmartToDo widget) still issues an OData query against `sprk_event` with `$select=sprk_todoflag`. The attribute was removed in R3 task 004.

**R4 work**:
1. **Read [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) first.** Apply the decision tree — likely **Archetype 3 (Pattern D, dual-use)** since SmartTodo is also a standalone Code Page. Calendar widget is the canonical worked example.
2. Identify the failing widget(s). Likely `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/SmartTodoWidget.tsx` (or LegalWorkspace SmartToDo widget — to be confirmed during audit step).
3. Rebuild against `sprk_todo` (NOT `sprk_event`). Query: `sprk_todo` filtered by current workspace's regarding context, statuscode in (Open, In Progress).
4. Comply with the **widget layout structure pattern** documented in:
   - [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper model
   - [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load + layout
   - [`docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](../../docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — the 21 testable MUSTs for embedded hosts
5. Re-export from `@spaarke/ai-widgets` shared lib, rebuild + redeploy via `code-page-deploy` skill.

### B. SmartTodo Code Page header/toolbar — align with Semantic Search PCF + Fluent v9

**Reference pattern**: `SemanticSearchControl` PCF (UAT screenshot).

**Layout requirements** (4 stacked rows):
1. **Page title** "Smart To Do" — top, full-width
2. **Search + actions row** — Search box + `Refresh` + `+ New` icon (right-aligned)
3. **Filter bar** — Facets: `Assigned To Me`, `Status`, `Due`, `Pinned`, etc. Plus `Clear`. (Drops the prior "My Tasks" vs "Assigned to Me" parallel modes — see OD-2 resolution.)
4. **Selection-aware toolbar** — when ≥1 card is selected, contextual actions appear:
   - **Open** (opens modal — see C)
   - **Delete**
   - **Email** (compose + share)
   - **Pin** (toggles `sprk_todopinned`)
5. **List / Card view toggle** (matching Semantic Search PCF's icon set)

**Constraints**:
- **MUST use shared component library** (`@spaarke/ui-components`). Hoist any new primitives that don't yet exist there.
- **MUST follow [`/fluent-v9-component`](../../.claude/skills/fluent-v9-component/SKILL.md) skill** — Griffel `makeStyles` + semantic tokens, no inline styles, no v8 fallbacks.

### C. Modal pattern (replace side-pane) — host the "To Do main form" + universal `< >` record navigation

**Form**: `formid = eca59df4-1364-f111-ab0c-7ced8ddc4cc6` ("To Do main form") — already created in Dataverse.

**Modal must work in TWO contexts** (OD-3):
- **MDA form context**: opened directly from a record (e.g., from a parent record's ribbon or subgrid command)
- **Iframe / Code Page context**: opened from inside the SmartTodo Code Page (or any future widget that lists todos)

**Universal `< >` record-navigation pattern** (UAT screenshot, Document preview modal): When a modal is launched from a list (To Do list, document list, anything), the user should be able to browse `<` previous / `>` next record without close-reopen cycles. Display "N of M" counter in upper-right.

**Adopted architecture (HYBRID — per OQ-1 resolution)**:

A reusable **`<RecordNavigationModalShell>`** Code Page wrapper that:
1. Provides the **outer chrome**: title, `< >` navigation, "N of M" counter, custom action bar slot
2. Iframes the **OOB MDA form** as its content (via `pagetype=entityrecord&navbar=off`)
3. Used **everywhere** a list-launched modal is needed (To Do, future regarding-shaped entities, document preview, etc.)

This pattern wins because it gives us **single consistent navigation UX everywhere** without rebuilding form behavior. The inner OOB form retains native save / business rules / BPF / statuscode controls / per-record permissions — zero maintenance burden for form-designer changes. The outer Code Page chrome owns navigation and styling.

**R4 work**:
1. Extract the navigation logic from `RichFilePreview.tsx` (`currentIndex`, `navigationTotal`, `onNavigate`, `ChevronLeft20Regular` / `ChevronRight20Regular`, `prevDisabled` / `nextDisabled`).
2. Promote it into `<RecordNavigationModalShell>` in `@spaarke/ui-components`.
3. The shell renders an iframe pointing at `mainURL?pagetype=entityrecord&etn=sprk_todo&id={recordId}&formid=eca59df4-1364-f111-ab0c-7ced8ddc4cc6&navbar=off`.
4. Cross-frame messaging contract: the outer shell intercepts `< >` clicks, asks the inner form via `postMessage` whether it's dirty, prompts the user if needed, then navigates iframe `src` to the next record's URL.
5. **Refactor `RichFilePreviewDialog`** to consume the new shell — proves the abstraction works without regressions.
6. Adopt in SmartTodo card-open path (single-click Open icon, double-click body, or selection-toolbar Open).

**Retire**: existing `TodoDetailPanel` side-pane component in `src/solutions/SmartTodo/`. OD-4 issues (no save, "Completed" doesn't work) become moot because the MDA main form's native save + statuscode controls take over.

The `TodoDetail` shared-lib component built in R3 task 011 may still be useful for **inline previews** (where you DON'T want a modal) — review during R4; keep if used.

#### OQ-1 trade-off analysis (the question that led to this hybrid)

| Approach | Advantages | Disadvantages |
|---|---|---|
| **A. Native `Xrm.Navigation.navigateTo`** (no wrapper) | Zero custom chrome to maintain. Native form behavior end-to-end. Less code. Always up-to-date with form designer changes. | No `< >` nav unless MDA's built-in record-browse is enabled — and that's only present in list-context flows; not guaranteed visible (user noted it may be hidden in command bar). Modal sizing platform-controlled. Look-and-feel doesn't necessarily match Fluent v9. |
| **B. Pure custom React wrapper** (our React form, no iframe) | Full control of chrome AND content. Single navigation pattern. Can show summary panels, related actions, breadcrumbs. | Heavy: we re-implement save / validation / business rules / BPF / statuscode flows. Risk of drift from MDA form-runtime. Per-record permissions need custom UX. Form-designer changes require code change + redeploy. |
| **C. HYBRID — Code Page wrapper + iframe-embedded OOB MDA form (ADOPTED)** | Best of both: **outer chrome** = custom `< >` + Fluent v9 styling; **inner content** = native form (save, BPF, rules, statuscode all free). Form-designer changes propagate without code change. Single navigation pattern across To Do, Document preview, future entities. Lower total surface area than B. | A small bridging layer (cross-frame messaging for dirty-check + nav). Some browser-styling coordination needed (iframe border, scrollbar). Not the lowest-code option, but the lowest **maintenance-burden** option once shipped. |

The hybrid (C) **simplifies deployment** vs. B because we never touch the form runtime. It **slightly adds deployment overhead** vs. A (we ship the wrapper) but the wrapper is one shared Code Page used everywhere — amortized across all list-launched modal flows. Net win: one wrapper deploy buys consistent UX for all entities.

### D. Regarding resolver — multi-environment-stable, reusable

**Existing code**: `PolymorphicResolverService.applyResolverFields` (ADR-024) + `TodoRegardingUpdateBuilder` + `AssociateToStep` shared-lib component (R3 task 030 — 11 entity targets) implement the resolver atomically.

**R4 requirement**: surface this on the To Do main form so users can edit the regarding from a form context, not only from the wizard.

**Decision** (OD-1 resolved): **whichever pattern is more resilient & stable across multi-environment deployments wins**. Likely PCF (the modern Spaarke standard) but R4 will:

1. **First, audit existing approaches.** Sub-agent or main session evaluates:
   - Custom PCF field control (probable winner)
   - Web Resource (HTML/JS embedded as form section)
   - Form embedded Code Page (Power Apps modern pattern)
2. Score each against:
   - Solution export/import portability (multi-tenant deploy = key R3 mandate)
   - Form designer ergonomics (can a maker drag it onto a form?)
   - Coupling to BFF (zero would be best — pure client-side, calls Dataverse Web API)
   - Maintenance burden (one shared bundle vs. per-form duplication)
3. Implement the winner.
4. **MUST** wrap `PolymorphicResolverService.applyResolverFields` — don't reimplement the FR-13 mutual-exclusivity logic.
5. **MUST** be reusable: design for both `sprk_todo` AND `sprk_communication` (same shape) AND any future regarding-shaped entity.

If the audit concludes PCF is the right answer (most likely), the component will:
- Render a single-row picker showing the 11 entity targets
- On change: clear the previous `sprk_regarding<X>` lookup, set the new one, atomically populate 4 resolver fields
- Show current state read-only when form is non-edit
- Comply with ADR-022 (PCF platform libraries)

### E. Card affordances

1. **"Open" icon in upper-right of each Kanban card** — single-click opens the modal (see C)
2. **Double-click anywhere on the card body** — also opens the modal
3. **Selection checkbox** in upper-left of card (drives selection-aware toolbar in B)

### F. Vertical Kanban orientation

**Requirement**: toolbar toggle re-orients the Kanban from horizontal columns (current: Today / Tomorrow / Future left-to-right) to **vertical sections** (each column becomes a collapsible vertical section, cards stack within).

**Use cases**:
- SmartTodo as a SpaarkeAi workspace widget (narrow container)
- Mobile / narrow-window standalone Code Page use
- Personal accessibility preference

**MUST** be a feature of the Code Page itself, not just the widget host. Persisted via user preference (likely `sprk_userpreference.preferencetype = "SmartTodoOrientation"`).

### G. NEW — Visual Host "Upcoming To Dos" cards on 4 parent main forms (OD-5)

**Pattern reference**: existing chart definition `UPCOMING TASKS` (id `154bd4a4-f359-f111-a825-3833c5d9bcab`) — already deployed; queries `sprk_event` filtered by event-type-ref ∈ {Task, Deadline, Reminder, To Do}; visualtype "Due Date Card List"; drill-through to `sprk_eventspage.html`.

**R4 work**: Clone the chart definition pattern but:
- **Target entity**: `sprk_todo` (not `sprk_event`)
- **Filter**: statuscode in (Open, In Progress), `sprk_duedate` next-5-days OR pinned
- **Drill-through target**: SmartTodo Code Page modal (the Kanban app) — NOT an entity list view
- **Context field**: matches the parent — `sprk_regardingmatter` for Matter, `sprk_regardingproject` for Project, etc.

**Forms to update**:
1. Matter main form
2. Project main form
3. Invoice main form
4. WorkAssignment main form

For each: add a Visual Host control bound to the new `Upcoming To Dos — <entity>` chart definition.

**Note**: clicking "Open" / drill-through opens the SmartTodo Code Page Kanban as a modal pre-filtered to this parent record. This requires the Code Page to accept a launch-context URL param (`?regardingType=...&regardingId=...`) that filters the Kanban view — R3 task 070b already implements this URL-param parsing for the Outlook flow; R4 reuses it.

---

## Out of Scope (R4)

- **Microsoft To Do bidirectional sync** (R3 Phase 7 — tasks 015 / 016 / 061–066). Still blocked on tenant admin adding AAD `Tasks.ReadWrite` delegated scope. Separate project (R5?).
- **`sprk_eventtodo` entity deletion cleanup** (R3 task 005). 26 appmodulecomponent refs; maker portal cleanup. Not blocking R4.
- **SPE / Graph permission issue** (BFF MI missing from container-type registration). Platform / Microsoft-support issue. Independent of R4.
- **10 deferred parent-form ribbons** (R3 task 040 follow-up). OD-6 resolved: defer; can pick up later if needed.
- **Outlook add-in further enhancements**. R3 shipped Create To Do + LinkedTodosBanner. No additional Outlook work in R4.

---

## Predecessor Context (from R3 — for reviewer continuity)

- `sprk_todo` entity with 44 attributes (11 regarding lookups + 4 resolver fields + 5 sync state + standard Spaarke set)
- Statuscode customized: 1 Open / 659490001 In Progress / 2 Completed / 659490002 Dismissed (FR-24)
- 11 parent forms have a `sprk_todo` subgrid
- Matter has a live "Create To Do" ribbon; 10 other entities' ribbons deferred (OD-6 = defer)
- BFF Office endpoints (`/by-message-id`, `/{commId}/linked-todos`) live
- Outlook add-in extended (manifest 1.0.19.0, APP_VERSION 1.0.18)
- SmartTodo Code Page deployed (`sprk_smarttodo`, last published 2026-06-08)
- `RichFilePreview` shared-lib component has the canonical `< >` navigation pattern (R4 will extract + generalize)
- Visual Host PCF live + UPCOMING TASKS chart definition pattern proven (R4 will clone for `sprk_todo`)

---

## Applicable ADRs (carry-forward from R3 + new)

- **ADR-024** — Polymorphic resolver pattern (the binding contract for regarding shape; PCF in D wraps `applyResolverFields`)
- **ADR-021** — Fluent UI v9 design system (Griffel makeStyles, semantic tokens — applies to B + E + F)
- **ADR-022** — PCF platform libraries (D will likely be a new PCF — must follow library policy)
- **ADR-006** — PCF over Web Resources (informs D decision but doesn't dictate; OD-1 says winner = stability)
- **ADR-028** — Spaarke Auth v2 (OBO + MI — applies anywhere R4 talks to BFF)
- **ADR-032** — Null-Object Kill-Switch (applies if R4 adds any feature-gated BFF service — likely none)

---

## Resolved Decisions (formerly Open Decisions OD-1 through OD-6)

| ID | Question | Resolution |
|---|---|---|
| **OD-1** | PCF vs Web Resource for regarding resolver | **Whichever is more resilient + stable for multi-env deployments wins.** Audit step decides; PCF is the likely answer (modern Spaarke standard). |
| **OD-2** | "My Tasks" vs "Assigned to Me" semantic distinction | **Drop "My Tasks" mode entirely. Use only "Assigned to Me"** (owner field is not important — typically BU-owned). |
| **OD-3** | Modal hosting strategy (Xrm.Navigation vs Code Page iframe) | **HYBRID** — Code Page wrapper `<RecordNavigationModalShell>` provides outer chrome (`< >` nav + Fluent v9 styling); iframe-embeds the OOB MDA form as content (native save + BPF + business rules retained). Used in both MDA + Code Page contexts. See C trade-off analysis. |
| **OD-4** | Side-pane "various issues" — enumerate | **Issues: no save, "Completed" doesn't work.** Resolved by switching to MDA main-form modal (C) — native save + statuscode controls take over. |
| **OD-5** | Other To Do enhancements | **Add NEW item G**: Visual Host "Upcoming To Dos" cards on Matter / Project / Invoice / WorkAssignment main forms, following UPCOMING TASKS chart-def pattern but targeting `sprk_todo` and drilling through to SmartTodo Code Page modal (not list view). |
| **OD-6** | 10 deferred parent-form ribbons | **Defer.** Not needed for R4 acceptance. Can be picked up in a separate small task later. |

---

## Resolved Open Questions (OQ-1 through OQ-3)

| ID | Question | Resolution |
|---|---|---|
| **OQ-1** | Should `< >` navigation work in MDA-context modal too? Trade-offs? | **YES — adopt hybrid Code Page wrapper everywhere.** Single navigation pattern across all modal-launched flows. The wrapper iframes the OOB MDA form so we get native save / BPF / business rules for free, while we own the outer chrome. Lowest **maintenance-burden** option even if not the lowest code; standardizes UX and simplifies long-term deploys (one wrapper, many entities). See C trade-off table. |
| **OQ-2** | Which widget surface fails with `sprk_todoflag` error? | **Source code is clean** (only JSDoc / migration notes, verified by repo-wide grep + chart-def / saved-query / user-query SQL checks). The failure is from **stale deployed bundles** — most likely the LegalWorkspace `SmartToDo` widget (`src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx`) that's loaded both as a "system" workspace section AND when a user adds it via the widget chooser. **R4 audit step**: identify all deployed surfaces still serving the old bundle, rebuild + redeploy. May also catch any PCF whose `bundle.js` was committed pre-R3. |
| **OQ-3** | Add Visual Host "Upcoming To Dos" to Event + Communication too? | **No.** Out of scope for R4. Only Matter / Project / Invoice / WorkAssignment. |

---

## Next Steps

1. **Ready for `/design-to-spec`.** No open questions remaining; OQ-1/2/3 resolved. If you have additional enhancements to add, flag them in the next message and I'll fold them in before running the skill.
2. Run `/design-to-spec projects/smart-todo-r4` → produces `spec.md` (formal FRs/NFRs + acceptance criteria)
3. Run `/project-pipeline projects/smart-todo-r4` → produces `plan.md` + `tasks/` POML decomposition + commits on a new feature branch
4. Begin task execution

---

*Draft v3 — all UAT feedback + open questions resolved. Ready for /design-to-spec unless you add more scope.*
