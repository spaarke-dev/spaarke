# Task 032 — Full-cover sizing + hide OOB main-form header band (FR-12/FR-13)

> Date: 2026-08-16 · sonnet/high · FULL rigor · prescriptive steps. Gate: 031 complete.
> ADR-050 Path A stays in force throughout — every option considered stays inside the OOB
> `Xrm.Navigation.navigateTo` family; no proprietary `FormModal`/`SprkModal` was proposed.

## Part A — Full-cover sizing (FR-13 / F-7) — IMPLEMENTED IN CODE

### Files changed
- `src/client/shared/Spaarke.UI.Components/src/utils/adapters/oobModalSizes.ts` — added a 4th
  named OOB size, `fullCover` (100%×100%), via the file's own documented escalation contract
  (extended `OobModalSizeName`, extended the frozen `OOB_MODAL_SIZES` record, doc comment citing
  FR-13 + this notes file).
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts` —
  `navigateToEntityRecordSurfaceAsync`'s OPEN and CREATE branches now both size at `fullCover`
  (were `record` 85%×85% / `createForm` 70%×80%). Also added an optional `formId` passthrough to
  `EntityRecordSurfaceParams` (Part B seam, currently unused by any caller — see Part B).
- `src/client/shared/Spaarke.UI.Components/src/utils/adapters/__tests__/oobModalSizes.test.ts` —
  added `fullCover` value/freeze coverage; updated the "exactly N sanctioned names" test from 3→4.
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/__tests__/wizardLaunchers.test.ts` —
  updated the two size-assertion tests (OPEN 85%×85%→100%×100%, CREATE 70%×80%→100%×100%); added
  2 tests for the `formId` no-op-by-default / passthrough-when-supplied contract.

### The sizing decision, reasoned from code (no live MDA available)

**Governing source**: `projects/smart-todo-r5/design.md` §"F-7 — Open-To-Do inner-modal sizing",
owner decision dated 2026-08-14, **Option 1** (quoted verbatim): *"bump inner dialog to
100%×100% (fully covers the outer 85% modal so it reads as 'replace, not nest')."* This is not a
guess on my part — the value (100%×100%) was already decided by the owner before this task ran;
task 032's job was to locate/apply it, verify it's sufficient for both scenarios, and reconcile it
against the `record`-size invariant.

**Single-modal case** (`SmartTodo` Code Page embedded directly in a workspace pane, not itself a
modal — e.g. a normal LegalWorkspace section or SpaarkeAi widget tab). The launching surface is
the pane, which is far smaller than any OOB dialog percentage. Both the pre-032 sizes (85%×85% /
70%×80%) and the new 100%×100% size are dialogs that overlay the ENTIRE browser viewport (OOB
`navigateTo` dialogs are top-level overlays, not DOM-nested inside the calling pane) — so this
case was ALREADY "full cover" trivially before this task; 100%×100% keeps it that way (more so).
**No regression risk here.**

**Modal-in-modal case** (LegalWorkspace's zero-selection Open path: `todo.registration.ts`
`handleOpenTodo`'s "no selection" branch calls `ctx.onOpenWizard(SMART_TODO_CODE_PAGE_NAME,
undefined, { width: 85%, height: 85% })` — opens the whole SmartTodo Code Page as an OOB
`webresource` dialog at a **hardcoded 85%×85%** literal in that file, outside the
`OOB_MODAL_SIZES` scale entirely and out of this task's declared file scope to touch. From inside
that Code Page, clicking Open on a card fires the SAME shared launcher this task modifies).

Key mechanical fact, confirmed from this codebase's own existing design rationale (not
speculation): `wizardLaunchers.ts`'s `resolveXrmNavigation()` frame-walks `window` →
`window.parent` → `window.top` specifically because "the dialog works regardless of which iframe
layer is calling." OOB `navigateTo` dialogs render as an overlay in the TOP browsing context and
size themselves as a percentage of THAT context's viewport — not relative to whatever iframe/pane
invoked them. Consequence: nesting depth does not change a new dialog's absolute footprint; it
only changes what's visually BEHIND it.

- Pre-032, the OPEN branch (`record`, 85%×85%) roughly matched the outer 85%×85% Code-Page-modal
  — already close to full-cover, which is presumably why the POML's own `<ui-tests>` only exercise
  Open→Open for the modal-in-modal scenario.
- Pre-032, the CREATE branch (`createForm`, 70%×80%) was **smaller** than the outer 85%×85% — if
  a user opened the SmartTodo Code Page as a modal (zero-selection Open) and then clicked **+ New
  Task** inside it, the create dialog would render inset within the outer dialog's frame, visibly
  failing "replace, not nest." This create-nested-in-modal path has no explicit `<ui-tests>` step
  in the POML, but the acceptance criteria generalize across "create or open" × "single-modal or
  modal-in-modal," so it is in scope.

**Conclusion**: `fullCover` (100%×100%) applied to BOTH branches is the correct, evidence-based
fix — it always exceeds any outer dialog percentage regardless of nesting depth or which branch
fires, with zero new plumbing needed to detect "am I nested" (the shared launcher has no such
context today, and inventing one would be scope creep this task doesn't need). This also matches
the POML's own framing: "the inner sprk_todo OOB main-form modal (the ONE shared launcher task
031 established)... create and open, both routed through the shared launcher."

### ADR/pattern tension — CLAUDE.md §6.5 Path A (project-scoped exception)

- **Rule challenged**: `.claude/patterns/ui/record-modal-selection.md` — *"Layout 1 (canonical) —
  ... 85% × 85% for every entity — do NOT vary per-entity (R2 FR-20 binding)."* Also restated in
  ADR-050's MUST NOT list: *"MUST NOT hardcode a per-entity modal size — OOB record open is
  85%×85% for every entity."*
- **Conflict**: this task's owner-directed fix (design.md F-7 Option 1, 2026-08-14) requires
  `sprk_todo`'s inner main-form dialog to size at 100%×100%, not the universal 85%×85% every other
  entity's record-open uses.
- **Path chosen: A (project-scoped exception)**. `navigateToEntityRecordSurfaceAsync` is — and
  has been since task 031 — a DEDICATED launcher exclusively for `sprk_todo`'s OOB main-form
  surface; no other entity's record-open call site imports or calls it (verified: every other
  `record`/`createForm` consumer in the repo — `WorkspaceGrid.tsx`, `QuickSummaryRow.tsx`,
  `DailyBriefingApp.tsx`, `RegardingResolverApp.tsx`, `DataGrid.tsx`, etc. — constructs its own
  inline `navigateTo` call reading `OOB_MODAL_SIZES.record`/`.createForm` directly; none of them
  changed by this task). So this exception does not create "different entities get different
  sizes at the same shared call site" (the anti-pattern the rule exists to prevent) — it gives
  `sprk_todo`'s OWN dedicated launcher its own owner-approved size, while the universal `record`
  invariant remains untouched everywhere else.
- **Rationale**: the owner explicitly decided this (design.md 2026-08-14, "Option 1... via
  U-4/U-5/U-6"), spec.md already carries an ADR Tensions section for this project citing the
  broader ADR-050 Path A exception (OOB family choice for `sprk_todo`), and the modal-in-modal
  full-cover requirement (F-7) cannot be met at 85%×85% without a size that specifically exceeds
  any possible outer dialog frame.
- **Impact**: scoped to the ONE `navigateToEntityRecordSurfaceAsync` launcher; zero change to any
  other `record`/`createForm` consumer or to the `record`/`createForm` constants themselves.
- **Alternative considered and rejected**: leaving CREATE at `createForm` (70%×80%) and OPEN at
  `record` (85%×85%) unchanged, accepting the create-nested-in-modal inset look as an accepted
  gap. Rejected because the acceptance criteria explicitly generalize to "create or open" ×
  "modal-in-modal," and the fix (reusing an already-frozen constant) is cheap and zero-risk to
  every other consumer.

### Known stale comments NOT touched (scope discipline)

`src/solutions/SmartTodo/src/SmartTodoApp.tsx` (8 occurrences) and one comment in
`src/solutions/LegalWorkspace/src/sections/todo.registration.ts` still narrate the pre-032
"Layout 1 (85%×85%)" sizing in prose comments. These files are outside this task's POML
`<relevant-files>` list (only `oobModalSizes.ts` + `wizardLaunchers.ts` + the Dataverse form were
declared in scope) and their BEHAVIOR is unaffected (they delegate to the shared launcher, which
now applies `fullCover` transparently — no call-site change needed). Flagging here as a follow-up
doc-drift cleanup rather than touching files outside this task's declared scope.

---

## Part B — Hide the OOB main-form header band (FR-12 / U-6)

**Outcome: DOCUMENTED-FOR-ORCHESTRATOR-DEPLOY.** Not an escalation — a clean, Microsoft-documented,
supported mechanism was found. It requires a live Dataverse form change this task does not apply
directly (per this task's own instruction and the confirmed silent-no-op precedent from task 013).

### Investigation, in the POML's specified order

**(b) navigateTo dialog/form OPTION — NO clean code option exists.** Fetched the current
`Xrm.Navigation.navigateTo` reference from Microsoft Learn (`ms.date: 2026-04-09`, fetched
2026-08-16):
`https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto`

The full `navigationOptions` parameter table has exactly 5 members: `target`, `width`, `height`,
`position`, `title`. There is no header/command-bar/chrome-suppression option anywhere in
`navigationOptions` or in the `entityrecord` `pageInput` shape (`pageType`, `entityName`,
`entityId`, `createFromEntity`, `data`, `formId`, `isCrossEntityNavigate`, `isOfflineSyncError`,
`processId`, `processInstanceId`, `relationship`, `selectedStageId`, `tabName`). **Confirmed
absent — this option does not exist**, current as of the fetched revision.

**(a) Dataverse form-level "show form header" property — no static toggle; IS a supported runtime
API.** Corroborating web search (carldesouza.com, "Hiding Form Header and Footer Through
JavaScript in Dynamics 365 Power Apps") plus general community consensus confirms header/footer
visibility is controlled by the documented `formContext.ui` Client API, invoked from a form
event handler (typically OnLoad) — **not** a static formxml property and **not** a maker-portal
checkbox:

```js
formContext.ui.headerSection.setBodyVisible(false);        // hides header field body
formContext.ui.headerSection.setCommandBarVisible(false);  // hides header command bar (Save/Save&Close row above the form's own dialog title)
formContext.ui.headerSection.setTabNavigatorVisible(false);// hides the header tab navigator
```

This IS the sanctioned, supported mechanism (official `formContext.ui.headerSection` Client API,
same family as `formContext.ui.tabs`/`formContext.data` used throughout Spaarke's existing form
scripts) — not a DOM hack, not an unsupported trick. It satisfies ADR-050 Path A cleanly: "the
exact main form the maker authored... form scripts" is explicitly what Family 1 (OOB `navigateTo`)
is FOR (`docs/standards/MODAL-DECISION-CRITERIA.md` Family 1).

Confirmed via live `systemform.formxml` read (MCP, formid `eca59df4-1364-f111-ab0c-7ced8ddc4cc6`,
"To Do main form"): the form root is `<form headerdensity="HighReadOnlyValues"
shownavigationbar="false">` — `shownavigationbar` is already `false` (hides the LEFT related-links
nav pane, a DIFFERENT chrome element than the top header/command-bar band this task targets).
There is no sibling formxml attribute for hiding the header — it is JS-only, confirming the
mechanism above is the ONLY supported path.

**(c) A dedicated modal-variant form clone — the correct scoping mechanism, not a rebuild.**
`pageInput.formId` IS a documented, supported parameter (confirmed in the same MS Learn fetch
above: *"formId | String | (Optional) ID of the form instance to display"*), and `sprk_todo`
already has 4 existing System Forms (`systemform` query, 2026-08-16):

| formid | name | type |
|---|---|---|
| `eca59df4-1364-f111-ab0c-7ced8ddc4cc6` | To Do main form | Main (2) — **the live form task 013/030/031 target** |
| `4e86f611-89b5-4074-bf17-d41bf8dcfdca` | Information | Main (2) |
| `dfc9bb95-412c-4ea1-883b-363ecbc75e46` | Information | Quick View Form (6) |
| `aeb7eb30-4488-4038-bd46-0d97e20ee453` | Information | Card (11) |

### Why NOT hide the header unconditionally on the existing "To Do main form"

`docs/architecture/spaarke-todo-architecture.md` (line 65): *"Each parent record's main form has
a 'To Dos' subgrid querying `sprk_todo`..."* — a native Dataverse subgrid. Double-clicking/opening
a row from a NATIVE subgrid uses Dataverse's own default record-open behavior, which is **full-page
navigation** (not a `target:2` dialog) unless a custom ribbon command intercepts it — no such
override exists for `sprk_todo` in this codebase (grepped; only the 3 shared-launcher call sites
construct `pageType:'entityrecord'` for `sprk_todo`). So the SAME "To Do main form" this task's
modal launcher opens is ALSO the form a user lands on when opening a `sprk_todo` row from a parent
record's "To Dos" subgrid in full-page view — where the record title + command bar (Save/Save &
Close, in the FULL PAGE case there's also no dialog chrome at all to substitute for it) must stay
visible. Hiding the header unconditionally on this ONE form would regress that path. **This is a
concrete, sourced finding, not a hypothetical** — it is the reason a dedicated modal-variant form
(selected only via this task's `formId` seam) is the correct mechanism, not a blanket form edit.

### Recipe for the orchestrator (NOT applied by this task — see rationale below)

1. **Clone** "To Do main form" (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`) as a new System Form on
   `sprk_todo` (objecttypecode `10946`), type Main. Maker portal "Save As" on the existing form is
   the simplest path (preserves all tabs/sections/controls, including the
   `sprk_Spaarke.Controls.RegardingResolver` custom control and the 3 existing OnLoad handlers
   verbatim). Suggested name: **"To Do main form - Modal"** (or similar — orchestrator's naming
   call).
2. **Add a 4th OnLoad handler** to the clone (alongside the 3 that already exist —
   `RegardingRecordNumberHyperlink.onLoad`, `RegardingPreSave.onLoad`, `ScoreOnChange.onLoad` —
   confirmed live via `formxml` read, so a 4th coexisting handler is a low-risk, precedented
   addition):
   - New JS web resource (e.g. `sprk_todo_modal_header_hide`):
     ```js
     var Spaarke = Spaarke || {};
     Spaarke.SmartTodo = Spaarke.SmartTodo || {};
     Spaarke.SmartTodo.ModalHeaderHide = Spaarke.SmartTodo.ModalHeaderHide || {};
     Spaarke.SmartTodo.ModalHeaderHide.onLoad = function (executionContext) {
       var formContext = executionContext.getFormContext();
       formContext.ui.headerSection.setBodyVisible(false);
       formContext.ui.headerSection.setCommandBarVisible(false);
       formContext.ui.headerSection.setTabNavigatorVisible(false);
     };
     ```
   - Register: Event=OnLoad, Library=`sprk_todo_modal_header_hide`,
     Function=`Spaarke.SmartTodo.ModalHeaderHide.onLoad`, **Pass execution context as first
     parameter: Yes** (required, matches the existing 3 handlers' convention).
3. **Preserve `DisplayConditions`/role visibility parity.** The original form carries
   `<DisplayConditions Order="1" FallbackForm="true">` scoped to 5 specific security Role IDs.
   Confirm the clone carries the SAME (or intentionally broader) role visibility so the modal
   launcher doesn't silently fail for users who can see the original form but not the clone —
   this is a maker-portal detail the orchestrator should verify at clone time (this task can't
   verify role-membership behavior for `formId`-targeted opens without a live MDA session).
4. **Deploy via the task-013/014 precedent, NOT direct Web API PATCH.** Task 013's note is
   explicit and directly reusable here: *"direct Web API PATCH of `systemform` is a confirmed
   silent no-op in this environment; form changes deploy only via a `pac` solution export/edit/
   import roundtrip."* This task did not touch the live form for exactly that reason, per this
   task's own instructions.
5. **One-line code follow-up once the clone's GUID is known**: `wizardLaunchers.ts`'s
   `EntityRecordSurfaceParams.formId` seam already exists (added by this task, tested, currently
   a no-op since no caller passes it). Once the orchestrator has the new form's GUID, either (a)
   pass `formId: '<new-guid>'` at the 2-3 `navigateToEntityRecordSurfaceAsync({ entityName:
   'sprk_todo', ... })` call sites (`SmartTodoApp.tsx`'s `openSprkTodoAsLayout1`,
   `todo.registration.ts`'s `handleOpenTodo`, `newTaskLauncher.ts`'s `launchNewTaskCreateForm`),
   or (b) hardcode the constant once inside `navigateToEntityRecordSurfaceAsync` itself scoped to
   `entityName === 'sprk_todo'` (fewer call-site edits, more implicit) — orchestrator's call; both
   are small, low-risk diffs once the GUID exists. This task deliberately does NOT pre-wire either
   with a placeholder/fake GUID (would be untested dead code masquerading as done work).

### Why this is not an escalation

The escalation trigger (POML `<escalation>`) fires only if hiding the header "would require a
proprietary rebuild of the form's chrome" or an unsupported DOM hack. Neither is true here:
`formContext.ui.headerSection.*` is official, documented Client API, invoked the same way as
Spaarke's 3 already-live OnLoad handlers on this exact form. The form-config change needed is
ordinary (a form clone + one more OnLoad handler), not a chrome rebuild — ADR-050 Path A is fully
intact.

---

## Verification (2026-08-16)

- **`npx tsc --noEmit`** in `Spaarke.UI.Components`: **3 pre-existing errors, 0 new** — confirmed
  via `git stash`/`git stash pop` before/after diff (identical 3 errors both times: `@spaarke/auth`
  and `@spaarke/sdap-client` module-resolution gaps in files this task never touched).
- **`npx jest`** in `Spaarke.UI.Components` (full suite): baseline (stashed) = 35 failed suites /
  17 failed tests / 2466 passed / 2483 total. After this task's changes = 35 failed suites / 17
  failed tests / **2469 passed / 2486 total**. Exactly +3 passed / +3 total, 0 new failures — the
  3 new tests this task added (`fullCover` size/freeze coverage + 2 `formId` passthrough tests).
  All 35 pre-existing failed suites are unrelated `@spaarke/auth`/`@spaarke/sdap-client`
  module-resolution gaps (same root cause as the tsc errors).
- **`npx jest`** in `src/solutions/SmartTodo` (full suite, unmodified by this task but consumes
  the shared launcher): **7 suites / 128 tests, all green** — zero regressions.
- **`src/solutions/LegalWorkspace`**: no jest runner configured (`package.json` has no `test`
  script; no `jest.config.*` present) — this project has 3 `*.test.ts(x)` files present but no
  wired runner, a pre-existing gap unrelated to this task. `todo.registration.ts`'s call site
  (`navigateToEntityRecordSurfaceAsync({ entityName: "sprk_todo", entityId: todoId })`) remains
  source-compatible with this task's change (the new `formId` param is optional).
- **Hex/rgb grep** on both changed source files (`oobModalSizes.ts`, `wizardLaunchers.ts`): zero
  matches (non-visual constants/logic files).

## ADR / code-review summary

- **ADR-050 Path A**: intact. Both Part A and Part B stay entirely within the OOB
  `Xrm.Navigation.navigateTo` family — no proprietary `FormModal`/`SprkModal` was proposed for
  either the sizing fix or the header-hide fix, per the owner's 2026-08-14 decision.
- **ADR-012 / size-governance (`oobModalSizes.ts`)**: the 4th-size escalation was exercised
  through the file's own documented pattern (extend `OobModalSizeName` + the frozen record +
  doc comment + notes/ entry) rather than an inline literal at any call site — no call site
  outside `wizardLaunchers.ts` was touched.
- **`.claude/patterns/ui/record-modal-selection.md` tension**: surfaced explicitly above (Path A,
  CLAUDE.md §6.5) — a narrow, sourced, single-launcher exception to the "85%×85% for every entity"
  invariant, not a silent violation.
- **CLAUDE.md §11 (component justification)**: no new component was created. `fullCover` extends
  an existing governed constants file via its own anticipated escalation seam; `formId` extends an
  existing interface with one optional field; the header-hide mechanism reuses the existing
  form-clone + OnLoad-handler pattern task 013 already established for this exact form (no new
  pattern invented).
