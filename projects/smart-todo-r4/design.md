# Smart To Do — R4 Design

> **Status**: Draft (initial scope from R3 UAT)
> **Date**: 2026-06-09
> **Predecessor**: smart-todo-decoupling-r3 (merged via PR #373, squash `e328beaf`)
> **Working note**: This is a starting design. Additional enhancements TBD by user — fold in before /design-to-spec.

---

## Purpose

R3 successfully decoupled `sprk_todo` from `sprk_event` and stood it up as a first-class entity with Kanban Code Page, parent-form subgrids, and Outlook Create To Do ribbon. UAT surfaced **UX gaps** that block adoption: the SmartTodo Code Page header/toolbar feels inconsistent with the rest of the platform; the side-pane editing pattern has unresolved issues; and the regarding picker needs to live on the main form so users can edit `sprk_todo` records consistently inside or outside the Code Page.

R4 closes these gaps by aligning SmartTodo with the **Semantic Search PCF** pattern that has already been validated across the product, switching from side-pane to **modal-with-main-form**, and surfacing the regarding resolver as a **reusable PCF** mountable on any form.

---

## In Scope

### A. Workspace widget bug — legacy `sprk_event.sprk_todoflag` query

**Symptom**: The Smart To Do widget on the SpaarkeAi workspace fails to mount with: *"Could not find a property named 'sprk_todoflag' on type 'Microsoft.Dynamics.CRM.sprk_event'"*.

**Cause**: A deployed widget bundle (likely from a pre-R3 build that has not been rebuilt and redeployed) still issues an OData query against `sprk_event` with `$select=sprk_todoflag`. The `sprk_todoflag` attribute was removed from `sprk_event` in R3 task 004.

**R4 work**: Identify the widget(s) still hitting the legacy shape (likely in `@spaarke/ai-widgets` or LegalWorkspace). Update to query `sprk_todo` first-class entity. Rebuild + redeploy.

### B. SmartTodo Code Page header/toolbar — align with Semantic Search PCF

**Reference pattern**: `SemanticSearchControl` PCF (screenshot referenced in UAT feedback).

**Layout requirements**:
1. **Page title** "Smart To Do" at the top, full-width
2. **Search + actions row** (search box + `Refresh` + `+ New` icon) on the next row
3. **Filter bar** ("All / Assigned Only" + facets like File Type / Date Range / Threshold / Mode / Tags / Clear) on the row after
4. **Selection-aware toolbar**: when a card is selected, a contextual toolbar appears with:
   - **Open** (opens the modal — see C)
   - **Delete**
   - **Email** (compose an email about this todo, or share it)
   - **Pin** (toggle `sprk_todopinned`)
5. **List/Card view toggle** (matching Semantic Search PCF's icon set)

**Open question — UX clarification needed** (item G in UAT feedback): The current SmartTodo has separate "My Tasks" filter mode and an "Assigned to Me" facet. **What's the distinction?** R4 should clarify or consolidate. Hypothesis: "My Tasks" = owner-based (sprk_todo.ownerid = me) vs "Assigned to Me" = assignee-based (sprk_todo.sprk_assignedto = me). If true, present them as two facets within the filter bar rather than parallel modes.

### C. Modal pattern (replace side-pane) using "To Do main form"

**New form**: `formid = eca59df4-1364-f111-ab0c-7ced8ddc4cc6` named **"To Do main form"** (already created by user in Dataverse).

**Mounting strategy**: When a user clicks **Open** on a card (toolbar or card icon or double-click), launch a modal hosting the To Do main form via `Xrm.Navigation.navigateTo` (or a Code Page wrapper that hosts the main form in an iframe — TBD by /design-to-spec).

**Why this matters**: A consistent "edit record" experience across MDA and Code Page contexts. Side pane has unresolved issues (which?  see UAT — to be enumerated). Modal also works well when SmartTodo is mounted as a workspace widget in narrower containers — the modal pops out of the container.

**Retire**: The current `TodoDetailPanel` side-pane component in SmartTodo solution (and any LegalWorkspace remnants). The `TodoDetail` shared-lib component built in R3 task 011 may still be useful for inline previews — review.

### D. Regarding resolver — surface as PCF on the main form

**Existing code**: `PolymorphicResolverService.applyResolverFields` (ADR-024) + `AssociateToStep` shared-lib component (R3 task 030 extended to 11 entity targets) implement the multi-entity resolution pattern. Today these live in `@spaarke/ui-components` and are consumed by Code Page wizards.

**R4 work**: Package the regarding resolver as a **PCF control** that can be added to the "To Do main form" via form designer. The PCF should:
- Render a single-row picker showing the 11 entity targets (Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact, Document)
- On change: clear the previous `sprk_regarding<X>` lookup, set the new one, and atomically populate the 4 resolver fields (`sprk_regardingrecordtype`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`)
- Validate FR-13 mutual-exclusivity (at most one regarding populated)
- Show the current state read-only when the form is non-edit

**Decision needed**: PCF (field control bound to one of the lookups, with side effects on the others) **OR** Web Resource (HTML/JS web resource embedded as a section). PCF is the modern Spaarke standard; Web Resource is simpler but less integrated. Recommend PCF unless there's a strong reason otherwise.

### E. Card affordances

1. **"Open" icon in upper-right of each Kanban card** — single-click opens the modal (see C)
2. **Double-click anywhere on the card body** — also opens the modal
3. Selection checkbox in upper-left (per B)

### F. Vertical Kanban orientation

**Requirement**: A toolbar option (toggle) that re-orients the Kanban from horizontal columns (current — Today / Tomorrow / Future as horizontal columns) to **vertical sections** stacked top-to-bottom.

**Why**: When SmartTodo is mounted as a workspace widget on the SpaarkeAi page, it sits in a narrower container that doesn't fit the 3-column layout well. Vertical sections (each "column" becomes a collapsible vertical section, cards stack within) fit narrow containers better.

**Generality**: This should be a feature of the Code Page itself (not just for the widget host) — users in the full-width Code Page may also prefer vertical for accessibility or personal preference.

### G. UX clarification — `My Tasks` vs `Assigned to Me`

(See B's open question — needs explicit user decision before /design-to-spec.)

---

## Out of Scope (R4)

- Microsoft To Do bidirectional sync (the R3 Phase 7 work — tasks 015/016/061–066 — still pending AAD `Tasks.ReadWrite` scope add)
- `sprk_eventtodo` entity deletion cleanup (R3 task 005 — deferred per user, 26 appmodulecomponent refs)
- SPE / Graph permission issues affecting BFF Save email flow (handled by R6)
- Outlook add-in further enhancements beyond what R3 shipped (Create To Do button + LinkedTodosBanner)

---

## Predecessor Context (from R3)

- `sprk_todo` entity with 44 attributes (11 regarding lookups + 4 resolver fields + 5 sync state + standard Spaarke set)
- Statuscode customized: 1 Open / 659490001 In Progress / 2 Completed / 659490002 Dismissed (FR-24)
- 11 parent forms (Matter / Project / Event / Communication / WorkAssignment / Invoice / Budget / Analysis / Organization / Contact / Document) have a `sprk_todo` subgrid
- Matter has a live "Create To Do" ribbon; 10 other entities have draft XML at `infrastructure/dataverse/ribbon/<Entity>Ribbons/createtodo-button.xml` (deferred per user)
- BFF has 2 new Office endpoints (`/by-message-id/{internetMessageId}`, `/{commId}/linked-todos`)
- Outlook add-in extended with `CreateTodoButton` + `LinkedTodosBanner` (manifest 1.0.19.0, bundle APP_VERSION 1.0.18)

---

## Applicable ADRs (carry-forward from R3)

- **ADR-024** — Polymorphic resolver pattern (the binding contract for regarding shape; PCF in D must implement this)
- **ADR-021** — Fluent UI v9 design system (Griffel makeStyles, semantic tokens — applies to B + E + F)
- **ADR-022** — PCF platform libraries (D will be a new PCF — must follow library policy)
- **ADR-006** — PCF over Web Resources (informs D decision)

---

## Open Decisions (must be resolved before /design-to-spec)

| ID | Question | Notes |
|---|---|---|
| **OD-1** | PCF vs Web Resource for regarding resolver (D) | Recommend PCF; user to confirm |
| **OD-2** | "My Tasks" vs "Assigned to Me" semantic distinction (B + G) | User to confirm or consolidate |
| **OD-3** | Modal hosting strategy: `Xrm.Navigation.navigateTo` with main form vs Code Page wrapper iframing the main form (C) | Affects D — does PCF need to work in both MDA form context AND iframe-hosted context? |
| **OD-4** | Side-pane "various issues" (C) | User to enumerate specific bugs so they're not missed in the rewrite |
| **OD-5** | Other To Do enhancements TBD by user (mentioned in UAT) | Not yet captured |
| **OD-6** | Should R4 fix the 10 deferred parent-form ribbons (R3 task 040 follow-up)? | Or stays deferred? |

---

## Next Steps

1. User adds **OD-5** items and resolves OD-1 through OD-4
2. Run `/design-to-spec projects/smart-todo-r4` → produces `spec.md` (formal FRs/NFRs)
3. Run `/project-pipeline projects/smart-todo-r4` → produces `plan.md` + `tasks/` + commit on a new feature branch
4. Begin task execution

---

*Draft 1 — extend with additional enhancements before formalizing.*
