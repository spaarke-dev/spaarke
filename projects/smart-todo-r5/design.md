# Smart To Do R5 — Design Backlog

> **Status**: Draft backlog (captured 2026-06-23 during R4 closeout)
> **Source**: R4 UAT rounds 4-13 follow-ups + user-stated R5 design items
> **Next step**: Run `/design-to-spec` once R4 closes to produce a formal spec.md

---

## Context

R4 shipped (PR #406 merged 2026-06-23 at `80f70a1d4`) with all 13 UAT rounds + structural workspace fixes + documentation. During R4 closeout the user identified items that genuinely belong in a new project rather than stretching R4 scope. This doc captures them so they're not lost between R4 closure and R5 kickoff.

---

## R5 Scope — Functional Enhancements (user-requested)

### F-1 — Visual / accessibility fixes carried from R4

- **Yellow score circles + count pills**: text color must be dark/black (currently white on yellow — fails WCAG contrast). Audit ALL yellow surfaces in the widget AND Code Page (column count pill is already dark per round 4 fix; the IN-CARD score circles on yellow background are still white).

### F-2 — Status Reason expansion

- Update the kanban + filter to surface `statuscode` "Completed" as a valid state (currently only Open + In Progress render).
- Decide visual treatment: filtered-OUT by default, with a "Show Completed" toggle in filter pane.

### F-3 — Filter pane redesign

- Add a filter tool icon in the toolbar (parity with MS To-Do filter UI shown in user screenshot — Priority / Status / Due date expandable categories).
- Filter categories MUST include:
  - **Priority** (multi-select from sprk_priority choice values — F-4)
  - **Status** (Open / In Progress / Completed)
  - **Due date** (Today / Tomorrow / This week / Overdue / etc.)
  - **Assigned To** (typeahead picker against `contact` entity)
- Default filter state: Status = Open + In Progress; everything else unfiltered.
- "Clear all" affordance.

### F-4 — Priority field + score auto-set

- **New field**: `sprk_priority` (Choice on sprk_todo):
  - Urgent = 100000000
  - High = 100000001
  - Medium = 100000002
  - Low = 100000003
- **Card display**: priority icon visible on each KanbanCard (icon set TBD — likely a colored dot or flag glyph from Fluent v9 icons).
- **Auto-set sprk_priorityscore** when the user selects a priority choice:
  - Urgent → 100
  - High → 75
  - Medium → 50
  - Low → 25
  - (Exact numbers TBD with user; align with the existing score bucketing thresholds in R4 — DEFAULT_TODAY_THRESHOLD=60, DEFAULT_TOMORROW_THRESHOLD=30)
- **Implementation surface**: probably a form OnChange handler (sprk_priority changes → set sprk_priorityscore) + parity in the CreateTodoWizard + quick-add. Decide one source of truth.

### F-5 — Effort field + score auto-set

- **New field**: `sprk_effort` (Choice on sprk_todo):
  - None = 100000000
  - Very High = 100000001
  - High = 100000002
  - Medium = 100000003
  - Low = 100000004
- **Auto-set sprk_effortscore** when the user selects an effort choice:
  - None → ?? (decide — does this even contribute to score?)
  - Very High → 25 (inverted — high effort = low score contribution per the existing R4 formula `(100 - effort) * 0.2`)
  - High → 50
  - Medium → 75
  - Low → 100
  - (Exact numbers TBD with user; the existing R4 score formula inverts effort)
- Same implementation surface considerations as F-4.

---

## R5 Scope — R4 Follow-ups carried forward

### FU-2 — RecordNavigationModalShell chromeMode

The shared modal shell draws its own title bar with prev/next chrome ("1 of 5", arrows, X). When inner content (RichFilePreview) has its own title bar, a duplicate appears. Two API options:
- (a) Shell prop `chromeMode='content-only'` to hide its own title bar
- (b) Child prop `suppressTitleBar` for inner components to opt-out

**Why R5**: low priority, no smart-todo impact (iframe-embedded form has no duplicate). Pick whichever is more uniform with the rest of the modal shell consumer base.

### FU-5 — LW Kanban rich-feature hoist (CRITICAL per shared-lib philosophy)

A 13-file subtree of rich Kanban features lives ONLY in LegalWorkspace-local code (`src/solutions/LegalWorkspace/src/components/SmartToDo/`), NOT in the shared `@spaarke/smart-todo-components` peer package. Includes:
- AI summary dialog
- Dismissed-section
- ThresholdSettingsPopover
- Advanced card affordances (priority/effort sliders, score breakdown)

**Why R5 + important**: any future consumer that wants the same Kanban experience (PCF To Do control, Outlook add-in panel, mobile, a different workspace, embedded view in another Code Page) currently has to either reimplement OR couple to LegalWorkspace (which is itself supposed to be retired per OC-R4-05). Per the user's shared-lib elevation philosophy, this needs to land BEFORE the next consumer is built. If F-4 + F-5 add per-card priority/effort UI, those should land in the shared lib not LW-local.

### NEW-2 if not closed in R4 — Structural Workspace height-chain fix

The current per-section `style: { height: "calc(100vh - 200px)", minHeight: "560px" }` is a workaround. The proper fix is at SectionPanel + WorkspaceShell + the parent chain above them. Previous attempt (R4 round 7) collapsed the workspace to 40px — needs a careful audit.

**Scope if R4 closes this**: nothing carried forward — delete this entry from R5.

**Scope if R4 defers this**: the audit + cleanup, including removing all per-section `calc(...)` workarounds across LegalWorkspace `workspaceConfig.tsx` once the structural fix lands.

**Status 2026-06-23**: ✅ Closed in R4-110 (commit `40ff12224` on `work/smart-todo-r4-closeout` + a follow-up commit removing `minHeight:0` from `WorkspaceShell.row`). Delete this entry once R4 PR merges.

### F-6 — SmartTodo widget toolbar: 'Search' label should be 'Filter' (and broken)

**Surface**: standalone SmartTodo Code Page modal (`sprk_smarttodo`), top toolbar.

**Behavior**: the toolbar currently shows a 'Search' affordance (icon + label) but its actual function is filter (it's the inline filter SearchBox in `SmartTodoWidget.styles.ts:inlineFilterBox`). Beyond the label being wrong, the affordance doesn't actually filter the kanban when typed into.

**Why R5**: pre-existing bug; cosmetic + functional. Not deploy-blocking. Pairs naturally with F-3 (filter pane redesign).

**Scope**: rename label + wire the input to actually drive the kanban's filter predicate. Likely a 1-2 hour fix once F-3 lands (or could be done independently first).

### F-7 — Open-To-Do inner-modal sizing (Smart To Do code page modal-in-modal)

**Surface**: standalone SmartTodo Code Page modal (`sprk_smarttodo`) → click Open on a card → inner record-form dialog launched via `Xrm.Navigation.navigateTo({pageType:'entityrecord', ...}, {target:2, width:80%, height:80%})` in `todo.registration.ts:handleOpenTodo`.

**Behavior**: the inner record dialog renders at 80%×80% of viewport, which is smaller than the outer SmartTodo Code Page modal (85%×85%). Visually, the inner dialog appears inset from the outer modal frame rather than fully covering it. User expectation: inner dialog should cover the outer modal (look like it replaces, not nests).

**Why R5**: nested-modal UX coordination is not trivial — relates to FU-2 (RecordNavigationModalShell chromeMode). Bundle with the broader modal-shell redesign.

**Scope**: either bump inner dialog to 85%×85% (parity), or 100%×100% (fully covers), or coordinate with FU-2 to introduce a chrome-suppression contract so nested modals can render full-frame. Decide UX first.

### F-8 — Open-To-Do inner-modal Save&Close behavior

**Surface**: same as F-7 — inner record dialog opened from SmartTodo Code Page modal.

**Behavior**: on Save & Close of the inner record dialog, the dialog frame stays open but its iframe content navigates back to the launch URL (= the SpaarkeAi Code Page). The user expects the inner dialog to close AND the outer SmartTodo Code Page modal to refresh its kanban with the saved changes.

**Root cause**: the round-9 parent-side `Xrm.Page.ui.close` interceptor in `SmartTodoModal.tsx` is wired for the outer Code Page modal, NOT for the inner record dialog. The inner dialog's Save & Close action triggers MDA's default navigation behavior (back to launch URL) instead of dismissing the dialog.

**Why R5**: this is a coordination problem between the SmartTodoModal interceptor and the inner `Xrm.Navigation.navigateTo` dialog. Solving it requires either (a) extending the parent-side interceptor to also catch inner-dialog close events, or (b) using a different navigation API for the inner record open (e.g., `openForm` with a custom close handler). Both have trade-offs. Investigate during the F-7 redesign — they likely share the same fix.

**Workaround in R4**: don't open records from inside the SmartTodo Code Page modal — open them from the widget directly (which dismisses the widget's modal cleanly via the existing interceptor).

---

## R5 Scope — Test Infrastructure (was R4-093 + FU-4 deferred)

### TEST-1 — Wire test runner for SmartTodo Code Page

R4 closure handles FU-4 (vitest wiring for SmartTodo Code Page tests). R5 picks up:

- Bring the 22 useLaunchContext "executable spec shims" up to actual passing tests.
- Add coverage for the new R5 priority/effort fields (F-4, F-5).
- Add coverage for the filter pane (F-3) — filter combination logic, defaults, clear-all.

### TEST-2 — UI test suite for NFRs (was R4-093)

Per the R4 plan but never executed:
- **NFR-05 perf**: page load < 3s benchmark via Lighthouse / Playwright trace
- **NFR-07 a11y**: full WCAG 2.1 AA pass via axe-core; keyboard nav; screen reader smoke
- **NFR-08 orientation flip**: vertical↔horizontal transition without layout glitch

Needs Playwright wiring (or similar). Decide framework before starting.

---

## R5 Scope — Cross-Cutting (process / non-project)

### PROC-1 — Real-Dataverse smoke gate before merge

R4 UAT 5-6 burned multiple deploy rounds because the spaarke-prototype harness mocked a `sprk_contact` entity that doesn't exist in real Dataverse (real is OOB `contact`). The mock hid the entity-name bug.

**Proposal**: add a checklist item to `/push-to-github` or `/merge-to-master` skill: "if widget queries Dataverse entities, has the developer done at least one create + read against real Dataverse before merge?" Could also be a new skill (`/real-dv-smoke`).

**Why R5 / cross-cutting**: not project-specific; affects all UI work. Could spin out as its own infrastructure project rather than living in R5.

---

## Out-of-scope candidates (mention only — defer to R6+)

- Mobile / responsive (< 768px viewport, touch-drag for kanban, sheet modals)
- Multi-language (i18n)
- Outlook ribbon parity (recent Header changes may have diverged from the Outlook ribbon Create flow)
- Notifications integration (push notification via Daily Briefing when due date approaches)
- Full a11y audit (covered in part by TEST-2 above)

---

## Suggested R5 phases (rough)

| Phase | Scope | Effort |
|---|---|---|
| **R5.1 Foundation** | F-4 (Priority field + auto-score) + F-5 (Effort field + auto-score) — these are entity schema changes that everything else builds on | 3-5 days |
| **R5.2 Visual + filter** | F-1 (yellow contrast), F-2 (Completed in status), F-3 (filter pane with all categories) | 1 week |
| **R5.3 Shared-lib hoist** | FU-5 (LW Kanban rich-features → `@spaarke/smart-todo-components`) | 3-4 days |
| **R5.4 Test infrastructure** | TEST-1 (vitest expansion) + TEST-2 (Playwright + NFRs) | 1 week |
| **R5.5 Polish + cross-cutting** | FU-2 (chromeMode), PROC-1 (real-DV smoke), any out-of-scope items prioritized in | TBD |

---

*Created 2026-06-23 from R4 UAT rounds 4-13 follow-up review + user-stated R5 design items. To formalize: run `/design-to-spec` to produce spec.md, then `/project-pipeline` to generate task files.*
