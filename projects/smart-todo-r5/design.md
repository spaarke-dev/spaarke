# Smart To Do R5 — Design Backlog

> **Status**: Refined backlog — ready to formalize. F-5 effort direction has a vetoable working default (Option B); all other decisions settled.
> **Source**: R4 UAT rounds 4-13 follow-ups + user-stated R5 design items + 2026-08-14 UAT UX pass
> **Next step**: Run `/design-to-spec` (F-5 default can be vetoed up to the F-5 task)

> **Resolver + verify pass — 2026-08-15** (owner): (1) F-1 white-on-yellow confirmed **already fixed in code** (yellow surfaces use `colorNeutralForeground1`) → downgraded to a verification sweep. (2) Added **R-11** — RegardingResolver PCF wiring + verification for `sprk_todo`; **investigation answer: RegardingResolver is the single canonical resolver; AssociationResolver is RETIRED (SRFR-045) and absent from this worktree** (the recalled "association resolver" is the separate email Association Engine). (3) The full 18-column regarding field set was added to the To Do table (owner screenshot) — R-11.2 ensures those fields land on the *form* so the write handler can populate them.
>
> **UAT UX pass — 2026-08-14** (owner): added a **UX/UI Refinements** section (U-1…U-6) from UAT feedback + two mockups (`to-do-header-revision.jpg`, `to-do-main-form-modal.jpg`): subtle channel coloring (U-1), widget side-by-side-column default (U-2), Code Page top-bar redesign (U-3, supersedes F-3 toolbar + F-6), `+ New Task` → OOB main-form modal (U-4), shared open/create launch (U-5), hide main-form header (U-6). These **decide F-7/F-8 toward Option 1** (keep the OOB main form). Phases table updated.
>
> **Refinement pass — 2026-08-14** (owner + Claude): re-grounded the backlog against the codebase as it stands ~7 weeks after capture. Changes this pass:
> - **Modal system landed.** [`SprkModal` + 6 presets / ADR-050](../../docs/standards/MODAL-DESIGN-SYSTEM.md) shipped 2026-08-01. FU-2 / F-7 / F-8 re-framed against it (FU-2's `chromeMode` proposal is now **obsolete** — the shell's single-title-source rule + `BrowseModal.nav`/`onBeforeNavigate` already solve the double-header). See each entry.
> - **Sequencing decision:** FU-5 shared-lib **hoist now runs BEFORE F-4/F-5** — building priority/effort card UI directly in `@spaarke/smart-todo-components` avoids adding new UI to soon-to-retire LegalWorkspace-local code (OC-R4-05). Phases table updated.
> - **NEW-2 deleted** (confirmed closed in R4-110).
> - **F-4/F-5 score mapping:** formula analysed against the locked R4 `todoScoring.ts`; priority mapping accepted; **effort-score direction is an OPEN owner decision** (big-work-first vs quick-wins-first) — see F-5.
> - **Confirmed states:** `@spaarke/smart-todo-components` (`Spaarke.SmartTodo.Components`) exists but holds only hooks/types/scoring + a widget shell; the 13-file rich-Kanban subtree still lives only in LegalWorkspace-local (FU-5 still open). `sprk_priority`/`sprk_effort` not yet created (F-4/F-5 schema is net-new).

---

## Context

R4 shipped (PR #406 merged 2026-06-23 at `80f70a1d4`) with all 13 UAT rounds + structural workspace fixes + documentation. During R4 closeout the user identified items that genuinely belong in a new project rather than stretching R4 scope. This doc captures them so they're not lost between R4 closure and R5 kickoff.

---

## R5 Scope — Functional Enhancements (user-requested)

### F-1 — Visual / accessibility fixes carried from R4

- **Yellow score circles + count pills**: text color must be dark/black on yellow (WCAG contrast).
- **Status 2026-08-15 — LIKELY ALREADY FIXED; DOWNGRADE TO VERIFICATION SWEEP.** Code inspection shows the yellow surfaces already use **dark** foreground: [`KanbanCard.tsx:104`](../../src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanCard.tsx#L104) yellow score circle → `fg: tokens.colorNeutralForeground1`; the yellow "7d" due badge ([line 32](../../src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanCard.tsx#L32)) and `DismissedSection` yellow surfaces likewise use `colorNeutralForeground1`. Only the darker red/orange/green backgrounds use white (`colorNeutralForegroundOnBrand`), which is correct. Owner also reports it "looks fixed in the current deployment." → **F-1 becomes a short audit**: sweep ALL yellow surfaces across widget + Code Page (incl. count pills, any legacy Code Page copies) to confirm none still render white-on-yellow; fix any stragglers with semantic tokens.
- **See U-1** (2026-08-14) — the subtle channel-coloring change touches the same surfaces; solve the F-1 audit + U-1 together with semantic tokens.

### F-2 — Status Reason expansion

- Update the kanban + filter to surface `statuscode` "Completed" as a valid state (currently only Open + In Progress render).
- Decide visual treatment: filtered-OUT by default, with a "Show Completed" toggle in filter pane.

### F-3 — Filter pane redesign

> **Updated 2026-08-14**: the toolbar/entry-point is now specified by **U-3** (the `Filter` pill in the redesigned top bar per `to-do-header-revision.jpg`). F-3 below is the *pane contents/logic*; U-3 is the *chrome*.

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
- **Formula coherence (2026-08-14, ACCEPTED)**: verified against the locked R4 composite in [`todoScoring.ts`](../../src/client/shared/Spaarke.SmartTodo.Components/src/utils/todoScoring.ts): `priorityComponent = sprk_priorityscore · 0.50`. This mapping spaces cleanly (Urgent 50 pt → Low 12.5 pt) and **Medium=50 equals the existing null-default**, so selecting "Medium" is a no-op vs today's behavior. No regression. Priority is the dominant term (50% weight, 0–50 pt swing).
- **Implementation surface**: probably a form OnChange handler (sprk_priority changes → set sprk_priorityscore) + parity in the CreateTodoWizard + quick-add. Decide one source of truth.

### F-5 — Effort field + score auto-set

- **New field**: `sprk_effort` (Choice on sprk_todo):
  - None = 100000000
  - Very High = 100000001
  - High = 100000002
  - Medium = 100000003
  - Low = 100000004
- **Auto-set sprk_effortscore** when the user selects an effort choice.
- **🔔 OPEN DECISION (2026-08-14) — effort-score DIRECTION, not just numbers.** The locked R4 formula is `effortComponent = (100 − sprk_effortscore) · 0.20` — effort is **inverted** and is a minor tiebreaker (20% weight, **0–15 pt total swing**). The direction of the mapping encodes a triage philosophy, and the two are opposites:

  | Effort choice | **(A) Big-work-first** (doc's original numbers) | contribution | **(B) Quick-wins-first** | contribution |
  |---|---|---|---|---|
  | Very High | effortscore 25 | **15 (highest)** | effortscore 100 | 0 |
  | High | 50 | 10 | 75 | 5 |
  | Medium | 75 | 5 | 50 | 10 |
  | Low | 100 | **0 (lowest)** | 25 | **15 (highest)** |

  - **(A) Big-work-first** = the doc's original mapping (VeryHigh→25 … Low→100). Heavy tasks float UP so they don't get perpetually deferred.
  - **(B) Quick-wins-first** = inverted mapping (VeryHigh→100 … Low→25). Low-effort tasks float UP so you clear the board fast.
  - **`None` semantics** also undecided: is None = *trivial/instant* (treat like lowest effort → floats up under B) or *unknown* (default effortscore 50 → mid contribution, 10 pt)?
  - **WORKING DEFAULT (2026-08-14) = (B) quick-wins-first, `None` = unknown (effortscore 50).** Rationale: the original F-5 annotation *stated* "high effort = **low** score contribution" (→ quick wins float up = **B**) but the numbers it listed did the opposite (**A**) — self-contradictory. Option B is the faithful reading of the documented intent. Concretely: `sprk_effort` Low→effortscore 25 (contribution 15, floats up) · Medium→50 (10) · High→75 (5) · Very High→100 (0, sinks) · None→50 (10, treated as unknown). The existing `(100 − effortscore)·0.2` formula is **unchanged** — only the choice→score mapping is set.
  - **Owner-vetoable**: swing is only 0–15 pt so it never overrides priority/urgency ordering. If the owner prefers "big work first," flip to (A). Confirm or veto before the F-5 task ships.
- Same implementation surface considerations as F-4.

---

## R5 Scope — UX/UI Refinements (UAT feedback 2026-08-14)

> Owner-provided UAT feedback with two mockups in this folder: [`to-do-header-revision.jpg`](to-do-header-revision.jpg) (Code Page top bar) and [`to-do-main-form-modal.jpg`](to-do-main-form-modal.jpg) (the OOB `sprk_todo` main form opened as a modal). These refine/land on top of several existing F-items; cross-references noted per entry.

### U-1 — Subtle channel (column) urgency coloring — refines F-1

- **Now**: each kanban channel/column renders a full red / yellow / green **background**.
- **Want**: keep the semantic mapping — **red = Today, yellow = Tomorrow, green = later (>Tomorrow)** — but make it **subtle** (e.g., a thin colored top/left accent bar or a lightly tinted header, not a full saturated column fill). Retain enough differentiation to scan urgency at a glance.
- **Surface**: column/header styling currently in the LW-local subtree ([`KanbanHeader.tsx`](../../src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanHeader.tsx) + `SmartToDo.tsx` column styles). **Because FU-5 hoists this subtree first (R5.1), U-1 lands in `@spaarke/smart-todo-components` post-hoist.**
- **Pairs with F-1** (yellow-on-yellow contrast): the subtle treatment should also fix the WCAG contrast issue F-1 raised — solve both together with semantic Fluent tokens (no hex).

### U-2 — SpaarkeAi widget default orientation: side-by-side columns — relates to NFR-08

- **Now**: in the SpaarkeAi widget the channels render stacked (each channel a horizontal row). The shared [`SmartTodoKanban`](../../src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartTodoKanban/SmartTodoKanban.tsx) already exposes an `orientation` prop (default `'horizontal'`).
- **Want**: the widget defaults to **three side-by-side columns (left / center / right)**.
- **Scope**: flip the [`SmartTodoWidget`](../../src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx) default orientation. ⚠️ **Verify which enum value yields side-by-side columns** — the code's `'horizontal'`/`'vertical'` naming does not obviously map to the user's "left/center/right"; implement against the *desired end state* (columns side by side), not the literal enum name. Must survive the NFR-08 orientation-flip (drag-drop + selection state preserved across the flip).

### U-3 — Code Page top bar redesign (per `to-do-header-revision.jpg`) — supersedes F-3 toolbar + F-6

- **Target layout** (from mockup): left = blue checkmark glyph + **"Smart To Do"** title; right cluster = **`🔍 Filter`** pill (outline button; expands the filter field/pane, same expand behavior as today) · **`+ New Task`** (primary blue button) · **`⋮`** three-dot **overflow menu** (secondary/less-frequent actions).
- **Consolidates**:
  - **F-3** (filter pane) — the `Filter` pill is the entry point; the expandable categories (Priority / Status / Due date / Assigned To) from F-3 open from it.
  - **F-6** (broken 'Search' affordance) — the mislabeled + non-functional 'Search' box is **replaced** by this `Filter` pill; the "rename + actually wire the predicate" work from F-6 folds in here.
  - **U-4** — the `+ New Task` button is the launcher in U-4.
- **Overflow menu (`⋮`) contents (DECIDED 2026-08-14)**: **Settings · Layout · Refresh** (the current lower-frequency toolbar actions). Primary/inline stays `Filter` + `+ New Task`; everything else goes under `⋮`.

### U-4 — '+ New Task' opens the OOB main form as a modal (per `to-do-main-form-modal.jpg`)

- **Want**: clicking **`+ New Task`** opens the `sprk_todo` **OOB main form in create mode as a modal** (the form shown in the mockup — INFORMATION / TRACKING / RELATED RECORD / TO DO SCORE sections). Note the mockup's **TO DO SCORE** section already shows **Priority / Effort / Priority Score / Effort Score** — confirming the F-4/F-5 fields live on this main form.
- **Scope**: launch via `Xrm.Navigation.navigateTo({ pageType:'entityrecord', entityName:'sprk_todo' /* create mode */ }, { target: 2, … })`, mirroring the existing open-record path (U-5). Regarding/context pre-fill from the launching surface where applicable (align with the CreateTodoWizard field-mapping already wired).
- **Ties into F-7/F-8** — see the modal-family note below.

### U-5 — Reuse the same main-form modal for OPEN To Do (already wired)

- Opening an existing To Do already uses the OOB main-form modal (F-7's `handleOpenTodo` path). U-5 just confirms **create (U-4) and open (existing) share ONE launch mechanism** — one code path, one sizing/chrome treatment, one close/refresh contract.

### U-6 — Hide the OOB main form's top header band

- **Want**: in the modal, **hide the main form's top header section** (the record-title band + form command bar duplicated below the modal's own title bar in the mockup). This removes the double-title and makes the OOB form read as a clean full-frame editor inside the modal.
- **Scope**: form-level customization on the `sprk_todo` main form (header visibility) and/or `navigateTo` options. Investigate whether this is achieved via form header configuration, a dedicated "modal" form, or a `navigateTo` chrome option. Coordinate with U-4/U-5 sizing (full-cover per F-7 Option 1).

### 🔑 What U-4/U-5/U-6 decide about F-7/F-8

These three items **effectively choose F-7/F-8 Option 1 (keep the OOB main form)** rather than Option 2 (rebuild as a proprietary `FormModal`): the owner wants the real Dataverse main form (with its native Save/Save&Close, business rules, and the F-4/F-5 score fields) — just sized to fully cover and with its header hidden (U-6). **Consequence**: F-8's Save&Close-should-dismiss-and-refresh coordination still needs the parent-side interceptor work (there is no proprietary-modal shortcut under Option 1). Scope F-7 (full-cover sizing) + U-6 (hide header) + F-8 (interceptor extension for the inner OOB dialog's Save&Close) as **one modal-behavior work item**.

---

## R5 Scope — R4 Follow-ups carried forward

### FU-2 — Double-title-bar in browse modal — ⚠️ MOSTLY RESOLVED by SprkModal (2026-08-01)

**Original problem**: `RecordNavigationModalShell` draws its own title bar with prev/next chrome ("1 of 5", arrows, X). When inner content (RichFilePreview) has its own title bar, a duplicate appears. The doc originally proposed a new `chromeMode='content-only'` shell prop.

**Update 2026-08-14**: The [`SprkModal` modal system](../../docs/standards/MODAL-DESIGN-SYSTEM.md) (shipped 2026-08-01, ADR-050) **already solves this at the shared-lib level**:
- [§3 "single-title-source rule"](../../docs/standards/MODAL-DESIGN-SYSTEM.md#3-header-contract): the shell owns the header; a preset must never nest another chrome component that renders its own title/counter.
- `BrowseModal` = `PreviewModal` + the shell's `nav` prop for the "N of M" chrome, plus an `onBeforeNavigate(dir)` seam so a consumer can still run a cross-frame dirty-check *without* rendering `RecordNavigationModalShell`'s nav chrome a second time.

**Therefore the proposed `chromeMode='content-only'` API is OBSOLETE — do NOT build it.** Remaining R5 work is a migration, not an API addition: **move the SmartTodo preview/browse consumer onto `BrowseModal`** and retire the direct `RecordNavigationModalShell` usage that caused the duplicate. Small, and it aligns SmartTodo with the canonical modal family.

**Why R5**: low priority, no smart-todo functional impact (the iframe-embedded form has no duplicate today). Now a clean shared-lib-alignment task.

### FU-5 — LW Kanban rich-feature hoist (CRITICAL per shared-lib philosophy)

A 13-file subtree of rich Kanban features lives ONLY in LegalWorkspace-local code (`src/solutions/LegalWorkspace/src/components/SmartToDo/`), NOT in the shared `@spaarke/smart-todo-components` peer package. Includes:
- AI summary dialog (`TodoAISummaryDialog.tsx`)
- Dismissed-section (`DismissedSection.tsx`)
- ThresholdSettings popover (`ThresholdSettings.tsx`)
- Advanced card affordances (`KanbanCard.tsx`, `PriorityScoreCard.tsx`, `EffortScoreCard.tsx`, `TodoDetailPane.tsx`, score breakdown)

**Confirmed state 2026-08-14**: `Spaarke.SmartTodo.Components` (`@spaarke/smart-todo-components`) **exists** but currently exports only hooks (`useKanbanColumns`, `useCurrentContactId`), types, `todoScoring.ts`, and a widget shell — the 13-file rich subtree above is still LW-local and does **not** import the peer package. So FU-5 is genuinely open, and the R4-102 `todoScoring.ts` hoist already established the "bit-for-bit parity, host-agnostic" pattern to follow.

**Why R5 + important (now FIRST, not R5.3)**: any future consumer that wants the same Kanban experience (PCF To Do control, Outlook add-in panel, mobile, a different workspace, embedded view in another Code Page) currently has to either reimplement OR couple to LegalWorkspace (which is itself supposed to be retired per OC-R4-05). Per the user's shared-lib elevation philosophy, this lands BEFORE the next consumer is built. **Sequencing decision 2026-08-14: because F-4 + F-5 add per-card priority/effort UI, hoist the card layer FIRST, then build F-4/F-5 directly in the shared lib** — otherwise the new UI is written into soon-to-retire LW-local code and immediately re-hoisted.

### NEW-2 — Structural Workspace height-chain fix — ✅ CLOSED (removed 2026-08-14)

Closed in R4-110 (commit `40ff12224` + follow-up removing `minHeight:0` from `WorkspaceShell.row`); R4 PR #406 merged. Entry retained as a one-line tombstone only — **no R5 scope**.

### F-6 — SmartTodo widget toolbar: 'Search' label should be 'Filter' (and broken)

> **Updated 2026-08-14**: folded into **U-3** — the redesigned top bar replaces the broken 'Search' box with the `Filter` pill; the "rename + wire the predicate" work happens there.

**Surface**: standalone SmartTodo Code Page modal (`sprk_smarttodo`), top toolbar.

**Behavior**: the toolbar currently shows a 'Search' affordance (icon + label) but its actual function is filter (it's the inline filter SearchBox in `SmartTodoWidget.styles.ts:inlineFilterBox`). Beyond the label being wrong, the affordance doesn't actually filter the kanban when typed into.

**Why R5**: pre-existing bug; cosmetic + functional. Not deploy-blocking. Pairs naturally with F-3 (filter pane redesign).

**Scope**: rename label + wire the input to actually drive the kanban's filter predicate. Likely a 1-2 hour fix once F-3 lands (or could be done independently first).

### F-7 — Open-To-Do inner-modal sizing (Smart To Do code page modal-in-modal)

**Surface**: standalone SmartTodo Code Page modal (`sprk_smarttodo`) → click Open on a card → inner record-form dialog launched via `Xrm.Navigation.navigateTo({pageType:'entityrecord', ...}, {target:2, width:80%, height:80%})` in `todo.registration.ts:handleOpenTodo`.

**Behavior**: the inner record dialog renders at 80%×80% of viewport, which is smaller than the outer SmartTodo Code Page modal (85%×85%). Visually, the inner dialog appears inset from the outer modal frame rather than fully covering it. User expectation: inner dialog should cover the outer modal (look like it replaces, not nests).

**Why R5**: nested-modal UX coordination. **Re-framed 2026-08-14**: the inner surface here is an **OOB MDA dialog** (`navigateTo` `target:2`), which `SprkModal` does **NOT** govern — so this is NOT the FU-2 fix. The governing doc is [`MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md) (OOB `navigateTo` vs proprietary Fluent v9 dialog).

> **Owner decision 2026-08-14 (via U-4/U-5/U-6): Option 1 chosen** — keep the OOB main form; size to full-cover (F-7) + hide its header (U-6). F-8's Save&Close coordination therefore still needs the interceptor work.

**Scope — decide the modal family first (this decision is shared with F-8):**
- **Option 1 — keep OOB dialog**: bump inner dialog to 100%×100% (fully covers the outer 85% modal so it reads as "replace, not nest"). Cheap; F-8's Save&Close coordination problem persists (see F-8).
- **Option 2 — switch to proprietary `FormModal`** inside the SprkModal system: the inner record edit renders as a `FormModal`/embedded form the parent controls directly. Sizing and close/refresh (F-8) both become controllable and **F-7 + F-8 dissolve together**. More work, but eliminates the OOB-dialog coordination class entirely.

Recommend deciding Option 1 vs 2 as one call spanning F-7 + F-8.

### F-8 — Open-To-Do inner-modal Save&Close behavior

**Surface**: same as F-7 — inner record dialog opened from SmartTodo Code Page modal.

**Behavior**: on Save & Close of the inner record dialog, the dialog frame stays open but its iframe content navigates back to the launch URL (= the SpaarkeAi Code Page). The user expects the inner dialog to close AND the outer SmartTodo Code Page modal to refresh its kanban with the saved changes.

**Root cause**: the round-9 parent-side `Xrm.Page.ui.close` interceptor in `SmartTodoModal.tsx` is wired for the outer Code Page modal, NOT for the inner record dialog. The inner dialog's Save & Close action triggers MDA's default navigation behavior (back to launch URL) instead of dismissing the dialog.

**Why R5**: this is a coordination problem between the SmartTodoModal interceptor and the inner `Xrm.Navigation.navigateTo` dialog. Solving it requires either (a) extending the parent-side interceptor to also catch inner-dialog close events, or (b) using a different navigation API for the inner record open (e.g., `openForm` with a custom close handler). Both have trade-offs. Investigate during the F-7 redesign — they share the same fix.

**Re-framed 2026-08-14**: this is **F-7 Option 1 vs Option 2** restated. If F-7 keeps the OOB dialog (Option 1), F-8 = interceptor extension (a) — genuinely fiddly, MDA owns the dialog close. If F-7 switches to a proprietary `FormModal` (Option 2), F-8 disappears (the parent owns close + kanban refresh directly). **Decide F-7/F-8 as one modal-family call.** Note `SprkModal` does not govern OOB `navigateTo` dialogs, so Option 2 is the only path that brings this surface *into* the canonical modal system.

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

## R-9 — Ribbon expansion: "Create To Do" on all parent entities (deferred from R4-118)

**Surface**: parent record main forms (Project, Event, Invoice, WorkAssignment, Communication) — currently missing the command bar button. Matter has it but uses a generic OOB icon.

**Background**: R4-118 (2026-06-25) deployed the underlying infrastructure (sprk_wizard_commands.js + 2 icon SVGs) but the ribbon-XML expansion was deferred for time/complexity. The R4 Matter button works today with the OOB `/_imgs/ribbon/newrecord32.png` icon. The new MS-To-Do-style icons (sprk_ToDoCheckmark16.svg + 32.svg, blue #0078D4 + white check) are deployed and ready to reference.

**Scope**:
1. Update `src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml` lines 48-50 to reference `$webresource:sprk_ToDoCheckmark32.svg` + `sprk_ToDoCheckmark16.svg` + add `ModernImage="$webresource:sprk_ToDoCheckmark32.svg"`. Re-deploy `spaarke_insights` solution.
2. Create 5 NEW dedicated entity-ribbon solutions (per `/ribbon-edit` skill convention — small dedicated solution per entity, NOT added to spaarke_insights or SpaarkeCore):
   - `ProjectRibbons` → sprk_project + RibbonDiff with CreateTodo button → `Spaarke.Commands.Wizards.openCreateTodoWizard`
   - `EventRibbons` → sprk_event + RibbonDiff with CreateTodo button
   - `InvoiceRibbons` → sprk_invoice + RibbonDiff with CreateTodo button
   - `WorkAssignmentRibbons` → sprk_workassignment + RibbonDiff with CreateTodo button
   - `CommunicationRibbons` → sprk_communication + RibbonDiff with CreateTodo button
3. Each solution cloned from the Matter pattern (CustomAction + CommandDefinition + LocLabels). JS handler is shared — all 6 entities call the same `openCreateTodoWizard(primaryControl)` function which extracts entity context via `getEntityContext(primaryControl)`.
4. Smoke-test each: open record → click "Create To Do" → wizard opens with correct entity context (entityType + entityId visible in wizard's regarding field).

**Effort**: 2-3 hrs. Each solution needs maker portal creation step (5 min × 5) OR programmatic XML scaffold (15-30 min × 5 with risk of XSD validation errors per entity).

**Why R5 / not R4 closeout**: Infrastructure (JS + icons) is shipped and Matter button works today. This is a polish/expansion item, not a fix. The user kanban + parent-form subgrid path (alternative entry point for creating To Dos from parent records) is functional today.

**References**:
- `projects/smart-todo-r4/tasks/118-deploy-wizard-commands-js.poml` (R4 work this expands)
- `src/client/webresources/js/sprk_wizard_commands.js:221` (openCreateTodoWizard handler — already deployed)
- `src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml` lines 48 + 145 (Matter template to clone)
- `src/client/assets/icons/sprk_ToDoCheckmark16.svg` + `32.svg` (deployed icons to reference)
- `.claude/skills/ribbon-edit/SKILL.md` (deploy workflow)

---

## R-10 — ToolbarActions + RegardingResolver test-honesty + defensive fixes (deferred from R4-114 code review)

**Surface**:
- `src/solutions/SmartTodo/src/components/Toolbar/ToolbarActions.ts handleEmail`
- `src/solutions/SmartTodo/src/components/Toolbar/__tests__/ToolbarActions.test.ts` (1 `.skip`'d test from R4-114)
- `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx handleSelectRecord` (race-condition guard + console-severity normalization)

**Background**: R4-114 wired jest for SmartTodo (77 tests passing) but had to `.skip` one test — `handleEmail composes a mailto:` — because the test relies on stubbing `window.location.href`, which jsdom v22+ blocks ("Cannot redefine property: location"). The fix path documented in the skip comment is correct but was deferred. Per "no shims" rule, the skip can't be permanent.

The R4-112 code review (2026-06-25) also surfaced two defensive items in the RegardingResolver Bug-1 fix that don't block but should be cleaned up: a theoretical race condition (S1) and one console.warn that should be console.error for severity symmetry (N1).

**Scope**:

1. **Make `handleEmail` testable** (un-skip the jsdom-blocked test):
   - Add an injectable navigation seam to `ToolbarActions.ts`: pass `navigate: (href: string) => void = (h) => { window.location.href = h; }` as part of the context. Default behavior identical; tests can inject `jest.fn()`.
   - Update `ToolbarActions.test.ts` to construct the context with `navigate: jest.fn()`, then assert call args. Remove `.skip`.
   - Verify jest run: 78/78 passing (was 77 + 1 skip).
   - Document the seam in the function's docstring as "test-injectable navigation; production uses window.location.href to avoid popup blockers."

2. **RegardingResolver defensive cleanup** (no user-visible change; ship next time PCF redeploys for a real reason):
   - **S1 race-condition guard**: `RegardingResolverApp.tsx handleSelectRecord` — capture `selectionGeneration` on entry, bail if state changed by the time `resolveRecordType` resolves. Currently the lookup dialog is modal so this is unreachable, but if anyone ever makes the picker non-modal this becomes a real bug.
   - **N1 console severity**: line 381 `console.warn(...)` → `console.error(...)` for symmetry with adjacent error logs (lines 386-387).

3. **No PCF version bump needed for #2 alone** — these are defensive/cosmetic and have no user-facing impact. Bundle into the NEXT version bump when a real PCF change ships (e.g., when CREATE-mode UAT surfaces something, or when a new HOST entity needs to be added).

**Effort**: ~1 hr total. #1 is ~30 min (small refactor + test update + jest re-run). #2 is ~15 min (two edits in one file).

**Why R5 / not R4 closeout**:
- The `.skip`'d test does NOT change runtime behavior — handleEmail works in production, only the test couldn't stub jsdom's `window.location`. Fixing it improves test coverage but doesn't fix a bug.
- The S1 race condition is unreachable in current usage (modal lookup dialog blocks reselection).
- The N1 console severity is cosmetic.
- All three are good hygiene items but don't block any user flow.

**References**:
- `projects/smart-todo-r4/tasks/114-fu4-wire-vitest-smart-todo.poml` (R4 work this builds on)
- Code-review report 2026-06-25 (findings S1, S2, N1)
- `src/solutions/SmartTodo/src/components/Toolbar/__tests__/ToolbarActions.test.ts:273-281` (skip rationale documented at the test site)

---

## R-11 — RegardingResolver PCF: wire + verify against To Do records (added 2026-08-15)

> UAT / owner ask: (a) confirm the canonical resolver control, (b) wire it so it works with `sprk_todo`, (c) the full regarding field set was just added to the To Do table (screenshot 2026-08-15).

### R-11.0 — Canonical resolver: **RegardingResolver** (investigation answer)

**There is ONE resolver control: `Spaarke.Controls.RegardingResolver`** (virtual PCF, currently **v1.4.8**). **AssociationResolver is RETIRED** (SRFR-045, 2026-07-05) and does **not** exist in this worktree.

> **Confirmed against `master` 2026-08-15**: `git ls-tree master src/client/pcf/` lists RegardingResolver and **no AssociationResolver** anywhere on master; RegardingResolver on master is v1.4.8, manifest explicitly "Designed for sprk_todo…". Investigation closed — RegardingResolver is the sole canonical resolver.

- Confirmed by [`SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md) (rewritten by `set-regarding-and-field-mapping-resolver-r2`): *"AssociationResolver — RETIRED… its picker duty was 100% redundant with RegardingResolver… There is now one resolver control, not two. Any doc or table still listing AssociationResolver is stale."*
- The RegardingResolver [manifest](../../src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml) is **explicitly designed for `sprk_todo`**: *"Designed for sprk_todo, sprk_event, sprk_invoice, sprk_communication, sprk_kpiassessment child forms."* It binds to the child's `sprk_regardingrecordtype` discriminator lookup and writes the 5 denormalized fields via `applyResolverFields`/`ResolverWriteHandler` (ADR-024) with subgrid auto-detect (SRFR-045).
- **Likely source of the "we recently created an association resolver PCF" recollection**: the **email/communication Association Engine + association picker** (email-r4 / email-r5 / email-communication-intelligence work — AI email→matter *association matching*). That is a **different domain** (email filing), not a polymorphic *regarding* resolver, and it does not replace RegardingResolver. **Advice: build R5 on RegardingResolver; do NOT resurrect or create an AssociationResolver.** If the owner did stand up a new PCF somewhere outside this worktree, surface it before R5 execution so we don't fork the resolver a second time.

### R-11.1 — Wire RegardingResolver onto the `sprk_todo` main form

- Place the virtual PCF on the To Do main form, **bound to `sprk_regardingrecordtype`** (the discriminator lookup) with the `entity="sprk_todo"` input property set. Follows the ADR-024 child-form pattern already used by `sprk_event`/`sprk_communication`.
- **Verify current wiring state**: a repo grep for `RegardingResolver` under `src/solutions/` found **no form reference** — the control may not be placed on the tracked `sprk_todo` form yet (form XML often lives in solution packaging, not `src/solutions`). **First R-11 task = confirm in Dataverse/solution whether the control is already on the form; wire it if not.**
- The presave staging JS (`sprk_todo_regarding_presave.js`, referenced in `RegardingResolverApp.tsx`) must be present so the resolver fields stage into the form's save.

### R-11.2 — Full regarding field set on the form (item 3, screenshot 2026-08-15)

- The `sprk_todo` **table** now carries the full 18-column regarding set: the 5 denormalized fields (`sprk_regardingrecordtype` lookup, `…recordid`, `…recordname`, `…recordnumber`, `…recordurl`) **plus** the per-entity lookups (`sprk_RegardingMatter`, `…Event`, `…Invoice`, `…Communication`, `…Project`, `…Analysis`, `…Budget`, `…Contact`, `…Document`, `…Organization`, `…ReportCard`, `…ServiceRequest`, `…WorkAssignment`).
- **⚠️ Table ≠ form.** Code comments record that historically several of these were **not on the To Do form** (*"SRFR-036 added only `sprk_regardingrecordnumber`"*; *"`sprk_regardingrecordurl` field is not on the sprk_todo form"*). Now that the columns exist, R5 must **place the fields the write handler populates onto the form** (or a hidden form section) so `ResolverWriteHandler` can actually write them, and confirm the handler + subgrid auto-detect cover the full lookup set.

### R-11.3 — Smoke-test with real To Do records (ties to PROC-1)

- Against **real Dataverse** (not the prototype mock — this is exactly the PROC-1 hazard): create a To Do from a parent subgrid (auto-detect path) AND via manual pick; confirm `sprk_regardingrecordtype/id/name/number/url` + the correct per-entity lookup populate, and the field-mapping engine inherits parent fields at creation time.
- **Interlocks with U-4** (the `+ New Task` main-form modal is where the resolver picker + the F-4/F-5 score fields co-exist — same `sprk_todo` form) and with **F-7/F-8** (open path). Validate the resolver inside the modal, not just the standalone form.

**Effort**: wiring + field placement ~0.5–1 day; real-DV smoke ~0.5 day. No new PCF code expected if the control already handles `sprk_todo` (it does per manifest) — this is form configuration + verification, not PCF development. **Component justification (§11): reuse — extends the existing canonical RegardingResolver; net-new surface = zero.**

**References**:
- [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md) (canonical-resolver table §, RegardingResolver ↔ engine interlock)
- [`.claude/adr/ADR-024-polymorphic-resolver-pattern.md`](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)
- `src/client/pcf/RegardingResolver/RegardingResolver/` (manifest, `RegardingResolverApp.tsx`, `handlers/ResolverWriteHandler.ts`)
- `projects/smart-todo-r4/notes/regarding-resolver-audit.md` (prior audit referenced in the handler)

---

## Out-of-scope candidates (mention only — defer to R6+)

- Mobile / responsive (< 768px viewport, touch-drag for kanban, sheet modals)
- Multi-language (i18n)
- Outlook ribbon parity (recent Header changes may have diverged from the Outlook ribbon Create flow)
- Notifications integration (push notification via Daily Briefing when due date approaches)
- Full a11y audit (covered in part by TEST-2 above)

---

## Suggested R5 phases (rough)

**Sequencing note (owner, 2026-08-14): all items are in scope — phase order is not a priority ranking.** The table below is a suggested grouping only. The **one** ordering constraint that remains is technical, not priority: **FU-5 hoist before F-4/F-5 card UI** (don't author the priority/effort cards in LW-local then immediately re-hoist them). Everything else can run in whatever order suits execution.

| Phase | Scope | Effort |
|---|---|---|
| **R5.1 Shared-lib hoist** | FU-5 (LW Kanban 13-file rich-features → `@spaarke/smart-todo-components`, following the R4-102 host-agnostic parity pattern) — do this FIRST so F-4/F-5 card UI lands in the shared lib | 3-4 days |
| **R5.2 Foundation (sprk_todo form + schema)** | F-4 (Priority field + auto-score) + F-5 (Effort field + auto-score) — Dataverse schema + auto-score handler + per-card UI in the hoisted shared lib (F-5 = Option-B default, vetoable). **+ R-11 (RegardingResolver wiring + full regarding-field placement on the To Do form)** — same form-config surface as F-4/F-5's score fields. | 4-6 days |
| **R5.3 Visual + filter + top bar** | **U-3** (Code Page top-bar redesign — subsumes F-3 filter pane + F-6 broken 'Search'), **U-1** (subtle channel coloring) + F-1 (contrast), F-2 (Completed in status), **U-2** (widget side-by-side-column default) | 1–1.5 weeks |
| **R5.4 Test infrastructure** | TEST-1 (vitest expansion + F-3/F-4/F-5/U-3 filter coverage) + TEST-2 (Playwright + NFRs incl. NFR-08 orientation for U-2) | 1 week |
| **R5.5 Modal main-form + polish + cross-cutting** | **U-4/U-5/U-6** + F-7 + F-8 as ONE modal-behavior item (Option 1: OOB main form — full-cover, hidden header, Save&Close dismiss+refresh), FU-2 (migrate browse consumer → `BrowseModal`), R-10 (test-honesty + defensive fixes), PROC-1 (real-DV smoke), **R-9 (ribbon expansion — IN R5 per owner 2026-08-15)** | TBD |

---

*Created 2026-06-23 from R4 UAT rounds 4-13 follow-up review + user-stated R5 design items. To formalize: run `/design-to-spec` to produce spec.md, then `/project-pipeline` to generate task files.*
