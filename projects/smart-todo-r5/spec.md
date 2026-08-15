# Smart To Do R5 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-15
> **Source**: `projects/smart-todo-r5/design.md` (R4 UAT rounds 4-13 follow-ups + 2026-08-14 UAT UX pass + 2026-08-15 resolver/verify pass)

## Executive Summary

Smart To Do R5 completes the To Do experience shipped in R4 by (1) **hoisting** the rich Kanban feature set out of LegalWorkspace-local code into the shared `@spaarke/smart-todo-components` peer package so any consumer can reuse it, (2) adding **Priority** and **Effort** choice fields that auto-populate the existing composite-score inputs, (3) redesigning the Code Page top bar (Filter pill + `+ New Task` + overflow) and the filter pane, (4) polishing the Kanban visuals (subtle urgency coloring, side-by-side-column default, contrast), and (5) making the To Do main-form modal (create + open) behave correctly, with the canonical **RegardingResolver** PCF wired and verified against `sprk_todo`. Test infrastructure and a real-Dataverse smoke gate round out the release.

## Scope

### In Scope
- **Shared-lib hoist** (FU-5): move the 13-file LegalWorkspace-local `SmartToDo/` subtree into `@spaarke/smart-todo-components`.
- **Priority field** (F-4): `sprk_priority` choice → auto-sets existing `sprk_priorityscore`.
- **Effort field** (F-5): `sprk_effort` choice → auto-sets existing `sprk_effortscore` (Option-B "quick-wins-first" mapping, owner-vetoable).
- **Code Page top-bar redesign** (U-3): Filter pill · `+ New Task` · `⋮` overflow (Settings/Layout/Refresh) — subsumes F-3 toolbar + F-6 broken 'Search'.
- **Filter pane** (F-3): Priority / Status / Due-date / Assigned-To categories; default Status = Open + In Progress; Clear-all.
- **Completed status** (F-2): surface `statuscode` Completed in kanban + filter, hidden by default behind a "Show Completed" toggle.
- **Subtle channel coloring + contrast** (U-1 + F-1): subtle red/yellow/green urgency treatment; audit yellow surfaces for WCAG contrast.
- **Widget orientation default** (U-2): SpaarkeAi widget defaults to side-by-side columns (left/center/right).
- **Main-form modal** (U-4/U-5/U-6 + F-7/F-8): `+ New Task` and Open both launch the OOB `sprk_todo` main form as a full-cover modal with its header hidden; Save & Close dismisses and refreshes the kanban.
- **Browse-modal migration** (FU-2): move the SmartTodo preview/browse consumer onto `BrowseModal` (ADR-050).
- **RegardingResolver wiring** (R-11): wire the canonical RegardingResolver PCF on the `sprk_todo` form; place the full regarding field set on the form; real-DV smoke.
- **Test infrastructure** (TEST-1/TEST-2): vitest expansion (+ new-field/filter coverage) and a Playwright NFR suite.
- **Test-honesty + defensive fixes** (R-10): un-skip the jsdom-blocked `handleEmail` test via an injectable navigation seam; RegardingResolver S1/N1 cleanups.
- **Real-DV smoke gate** (PROC-1): a pre-merge checklist step for Dataverse-querying widget work.
- **Ribbon "Create To Do" expansion** (R-9): 5 dedicated ribbon solutions (Project/Event/Invoice/WorkAssignment/Communication) + Matter icon refresh.

### Out of Scope
- Mobile/responsive (<768px), touch-drag kanban, sheet modals — R6+.
- Multi-language (i18n).
- Outlook ribbon parity for the Create flow.
- Notifications integration (due-date push via Daily Briefing).
- Re-creating or resurrecting an `AssociationResolver` PCF (retired SRFR-045; confirmed absent on `master`).
- Changing the composite score **formula** or its weights (locked in `todoScoring.ts`); R5 only sets the choice→score mappings.

### Affected Areas
- `src/client/shared/Spaarke.SmartTodo.Components/**` — hoist target; new priority/effort card UI; scoring mappings; widget orientation default.
- `src/solutions/LegalWorkspace/src/components/SmartToDo/**` — source of the hoisted subtree (becomes a thin shim/re-export post-hoist).
- `src/solutions/SmartTodo/src/**` — Code Page: top bar, filter pane, modal launch (`todo.registration.ts`, `SmartTodoModal.tsx`), Completed status, toolbar (`ToolbarActions.ts`).
- `sprk_todo` Dataverse entity — 2 new choice columns (`sprk_priority`, `sprk_effort`); main-form layout (score fields + regarding field set); form OnChange handler.
- `src/client/pcf/RegardingResolver/**` — form wiring (config, not PCF code); S1/N1 defensive fixes.
- `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/**` — `BrowseModal` consumer migration (no shell changes expected).
- Tests: `src/solutions/SmartTodo/**/__tests__`, shared-lib tests, new Playwright suite.
- Ribbon (FR-19): 5 new per-entity ribbon solutions + `src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml` (Matter icon); shared `src/client/webresources/js/sprk_wizard_commands.js` (already deployed) + `src/client/assets/icons/sprk_ToDoCheckmark{16,32}.svg`.

## Requirements

### Functional Requirements

1. **FR-01 (FU-5 hoist)**: Move the 13-file rich Kanban subtree (`KanbanCard`, `KanbanHeader`, `AddTodoBar`, `DismissedSection`, `ThresholdSettings`, `TodoAISummaryDialog`, `TodoDetailPane`, `PriorityScoreCard`, `EffortScoreCard`, `SmartToDo`, `SmartToDoDialog`, `todoScoringTypes`, `index`) from `src/solutions/LegalWorkspace/src/components/SmartToDo/` into `@spaarke/smart-todo-components`, following the R4-102 host-agnostic parity pattern (no reach-into `src/solutions/...`). — **Acceptance**: components exported from the peer package; LegalWorkspace consumes them via `@spaarke/smart-todo-components` (thin shim only, no duplicated logic); build + existing behavior unchanged (bit-for-bit scoring parity).

2. **FR-02 (F-4 priority)**: Add `sprk_priority` Choice to `sprk_todo` (Urgent=100000000, High=100000001, Medium=100000002, Low=100000003). On selection, auto-set the **existing** `sprk_priorityscore` to Urgent→100 / High→75 / Medium→50 / Low→25 via a single source-of-truth handler (form OnChange), with parity in CreateTodoWizard + quick-add. Card renders a priority glyph. — **Acceptance**: choosing a priority sets the score; the score drives the composite via the unchanged `priorityComponent = score·0.50`; "Medium" reproduces today's null-default (50); card shows the priority indicator.

3. **FR-03 (F-5 effort)**: Add `sprk_effort` Choice to `sprk_todo` (None=100000000, Very High=100000001, High=100000002, Medium=100000003, Low=100000004). On selection, auto-set the **existing** `sprk_effortscore` using the **owner-confirmed Option B (quick-wins-first)** mapping: Low→25, Medium→50, High→75, Very High→100, None→50 (unknown). The `(100 − effortscore)·0.20` formula is unchanged. — **Acceptance**: low-effort tasks receive the higher score contribution (float up); mapping is the single source of truth shared by form handler + wizards + quick-add.

4. **FR-04 (R-11 RegardingResolver)**: Wire the canonical `Spaarke.Controls.RegardingResolver` PCF (v1.4.8) onto the `sprk_todo` main form bound to `sprk_regardingrecordtype` with `entity="sprk_todo"`; ensure the full regarding field set (5 denormalized fields + per-entity lookups) is present on the form so `ResolverWriteHandler` can populate it; ensure the presave staging JS is present. — **Acceptance**: on a To Do created from a parent (subgrid auto-detect) or via manual pick, `sprk_regardingrecordtype/id/name/number/url` + the correct per-entity lookup populate; the field-mapping engine inherits parent fields at creation. No new resolver PCF is created.

5. **FR-05 (U-3 top bar)**: Replace the Code Page top toolbar with the mockup layout (`to-do-header-revision.jpg`): left = checkmark glyph + "Smart To Do"; right = `🔍 Filter` outline pill (opens the filter pane) · `+ New Task` primary button (FR-10) · `⋮` overflow menu. **Overflow actions (owner-confirmed natural mapping)**: **Settings** → the existing `ThresholdSettings` popover (Today/Tomorrow score thresholds, currently TODAY=60 / TOMORROW=30); **Layout** → orientation toggle (stacked ↔ side-by-side columns, shared with FR-09); **Refresh** → reload kanban data. — **Acceptance**: the broken/mislabeled 'Search' box (F-6) is gone; Filter opens the pane; overflow holds Settings/Layout/Refresh wired to those three surfaces; inline actions are only Filter + New Task.

6. **FR-06 (F-3 filter pane)**: The filter pane (opened from the Filter pill) provides expandable categories: **Priority** (multi-select of `sprk_priority`), **Status** (Open / In Progress / Completed), **Due date** (Today / Tomorrow / This week / Overdue), **Assigned To** (typeahead against `contact` via `Xrm.WebApi`). Default filter state = Status {Open, In Progress}; all else unfiltered. "Clear all" affordance. — **Acceptance**: each category filters the kanban predicate; defaults applied on load; Clear-all resets to defaults; filter state survives orientation flip.

7. **FR-07 (F-2 Completed status)**: Surface `statuscode` "Completed" as a valid kanban/filter state, filtered OUT by default with a "Show Completed" toggle in the filter pane. — **Acceptance**: Completed To Dos are hidden by default and appear when the toggle is on; kanban renders them without layout breakage.

8. **FR-08 (U-1 + F-1 visuals)**: Replace full red/yellow/green channel **backgrounds** with a subtle treatment (e.g., thin accent bar or lightly tinted header) preserving red=Today / yellow=Tomorrow / green=later; audit all yellow surfaces (widget + Code Page) to confirm dark foreground on yellow (WCAG). Semantic Fluent tokens only (no hex). — **Acceptance**: urgency still scannable at a glance; no white-on-yellow surface remains; light/dark parity holds.

9. **FR-09 (U-2 orientation default)**: The SpaarkeAi `SmartTodoWidget` defaults to three side-by-side columns (left/center/right). Verify which `orientation` enum value produces side-by-side and set it as the widget default. — **Acceptance**: widget renders columns side-by-side by default; drag-drop + selection state survive an orientation flip (NFR-03).

10. **FR-10 (U-4 New Task modal)**: `+ New Task` opens the `sprk_todo` OOB main form in **create** mode as a modal (`Xrm.Navigation.navigateTo({pageType:'entityrecord', entityName:'sprk_todo'}, {target:2, …})`), pre-filling regarding/context from the launching surface where applicable. — **Acceptance**: the create form opens as a modal showing INFORMATION / TRACKING / RELATED RECORD / TO DO SCORE (incl. FR-02/FR-03 fields); on save, the new To Do appears in the kanban.

11. **FR-11 (U-5 open shares launch)**: Opening an existing To Do uses the **same** launch mechanism, sizing, chrome, and close/refresh contract as FR-10 — one code path for create + open. — **Acceptance**: create and open produce visually identical modals; both refresh the kanban on save.

12. **FR-12 (U-6 hide form header)**: Hide the OOB main form's top header band (record-title + form command bar) in the modal so it reads as a clean full-frame editor. — **Acceptance**: no duplicate title/command bar below the modal's own chrome; native Save/Save & Close still reachable.

13. **FR-13 (F-7 modal sizing)**: Size the inner main-form modal to fully cover the launching surface (per Option 1). — **Acceptance**: the form modal covers the outer frame (no inset "nested" look).

14. **FR-14 (F-8 Save & Close)**: On Save & Close of the inner main-form modal, the modal dismisses AND the outer SmartTodo Code Page kanban refreshes with saved changes — via the parent-side interceptor extended to catch the inner OOB dialog's close. — **Acceptance**: Save & Close closes the dialog (no navigation back to the launch URL) and the kanban reflects the change without a manual reload.

15. **FR-15 (FU-2 BrowseModal)**: Migrate the SmartTodo preview/browse consumer onto `BrowseModal` (ADR-050), forwarding `nav` + `onBeforeNavigate`; retire the direct `RecordNavigationModalShell` usage that caused the duplicate title bar. No new `chromeMode` API is added. — **Acceptance**: single-title-source; browse "N of M" works; no double header.

16. **FR-16 (TEST-1)**: Bring the 22 `useLaunchContext` executable-spec shims to passing tests; add coverage for FR-02/FR-03 (auto-score mappings), FR-06 (filter combination logic, defaults, clear-all), and FR-07 (Completed toggle). — **Acceptance**: vitest suite green; new logic covered.

17. **FR-17 (TEST-2)**: Add a Playwright-based NFR suite: page-load <3s (NFR-02), WCAG 2.1 AA via axe-core + keyboard nav (NFR-01), orientation flip without layout glitch (NFR-03). — **Acceptance**: suite runs in CI-capable form; each NFR has an executable check.

18. **FR-18 (R-10 hygiene)**: (a) Add an injectable navigation seam to `ToolbarActions.ts` (`navigate` fn defaulting to `window.location.href`) and un-skip the `handleEmail` mailto test (78/78). (b) RegardingResolver `handleSelectRecord` selection-generation race guard (S1) + `console.warn`→`console.error` severity fix (N1). — **Acceptance**: no `.skip` remains for handleEmail; defensive fixes applied; bundle S1/N1 into the next RegardingResolver version bump (which R-11 wiring provides).

19. **FR-19 (R-9 ribbon expansion — IN R5, owner-confirmed)**: Add "Create To Do" command-bar buttons to Project/Event/Invoice/WorkAssignment/Communication parent forms via 5 dedicated ribbon solutions (cloned from the Matter pattern; shared `openCreateTodoWizard` JS handler already deployed) + refresh the Matter button to the deployed `sprk_ToDoCheckmark32/16.svg` icons. — **Acceptance**: each parent record's "Create To Do" button opens the CreateTodo wizard with the correct entity context (entityType + entityId in the regarding field); Matter button shows the new icon. Per `/ribbon-edit` convention (small dedicated per-entity solution — NOT added to spaarke_insights/SpaarkeCore).

20. **FR-20 (PROC-1 real-DV smoke gate)**: Add a checklist item to `/push-to-github` or `/merge-to-master`: any widget change querying Dataverse entities requires ≥1 create+read against **real** Dataverse before merge. — **Acceptance**: the gate exists as a documented pre-merge step (skill checklist or lightweight `/real-dv-smoke`); R5's own Dataverse-touching FRs (FR-04, FR-06 Assigned-To) satisfy it.

### Non-Functional Requirements

- **NFR-01 (a11y)**: WCAG 2.1 AA — dark-on-yellow contrast, keyboard navigation of top bar/filter/kanban, screen-reader smoke. Verified via FR-17.
- **NFR-02 (perf)**: Code Page load < 3s. Verified via FR-17.
- **NFR-03 (orientation)**: Vertical↔horizontal (stacked↔side-by-side) flip preserves drag-drop + selection state with no layout glitch. Verified via FR-17.
- **NFR-04 (tokens)**: All new/changed UI uses semantic Fluent v9 tokens — zero hex, zero `'1px'` literals, zero inline color (ADR-021, strengthened by ADR-050).
- **NFR-05 (shared-lib purity)**: Hoisted components are host-agnostic — no imports reaching into `src/solutions/...` (ADR-012).
- **NFR-06 (real-DV)**: Dataverse-querying changes validated against real Dataverse before merge (FR-20 gate); no reliance on prototype mocks for entity-name correctness.

## Technical Constraints

### Applicable ADRs
- **ADR-012** — Shared component library. Governs the FU-5 hoist target and host-agnostic boundary.
- **ADR-021** — Fluent UI v9 design system. Governs all visual work (top bar, coloring, contrast); semantic tokens, dark mode required.
- **ADR-024** — Polymorphic Resolver pattern. Governs RegardingResolver wiring (FR-04); the resolver writes the 5 denormalized regarding fields on the child form.
- **ADR-050** — Canonical Modal Shell. Governs FR-15 (BrowseModal migration) and the modal chrome vocabulary; also the reference for why F-7/F-8 stay OOB (SprkModal does not govern OOB `navigateTo` dialogs).
- **ADR-038** — Testing strategy. Governs FR-16/FR-17 (KEEP-path categories, mock-boundary rules, coverage-as-observation).
- **ADR-028** — Spaarke Auth. Applies where any authenticated fetch is used (function-based contract). The Assigned-To typeahead uses host-context `Xrm.WebApi` (no BFF), so BFF auth surface is not added.
- **ADR-030 / ADR-031** (PaneEventBus / stage lifecycle) — relevant to the SpaarkeAi widget default (FR-09) as the widget mounts through the workspace pane system.

### MUST Rules
- ✅ MUST keep the composite score **formula + weights** in `todoScoring.ts` unchanged; only set choice→score mappings (FR-02/FR-03).
- ✅ MUST place hoisted components in `@spaarke/smart-todo-components` with no `src/solutions/...` reach-in (ADR-012 / NFR-05).
- ✅ MUST use semantic tokens only for all visual changes (ADR-021 / ADR-050 / NFR-04).
- ✅ MUST NOT add a `chromeMode` API to `RecordNavigationModalShell` — use `BrowseModal`'s `nav`/`onBeforeNavigate` (ADR-050).
- ✅ MUST NOT create or resurrect an `AssociationResolver` PCF — RegardingResolver is the sole canonical resolver (confirmed on `master`).
- ✅ MUST route the To Do main-form create/open through **one** launch mechanism (FR-10/FR-11).
- ✅ MUST use host-context `Xrm.WebApi` (not BFF) for the Assigned-To contact typeahead (DATA-ACCESS-DECISION-CRITERIA).

### Existing Patterns to Follow
- `src/client/shared/Spaarke.SmartTodo.Components/src/utils/todoScoring.ts` — the locked scoring contract (bit-for-bit parity).
- `src/client/shared/Spaarke.SmartTodo.Components/src/hooks/useKanbanColumns.ts` — the R4-102 host-agnostic hoist precedent.
- `docs/standards/MODAL-DESIGN-SYSTEM.md` §3/§8 — `BrowseModal` wiring (`nav` + `onBeforeNavigate`).
- `docs/standards/MODAL-DECISION-CRITERIA.md` — OOB `navigateTo` vs proprietary dialog decision (F-7/F-8 stay OOB = Option 1).
- `src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts` + `RegardingResolverApp.tsx` — resolver write path + subgrid auto-detect.
- `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` — resolver ↔ field-mapping-engine interlock.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

- **BFF = N** — no new BFF endpoints/services. The Assigned-To typeahead and all Dataverse reads use host-context `Xrm.WebApi`. No publish-size impact.
- **SpaarkeAi = Y** — the SmartTodo widget renders in the SpaarkeAi/LegalWorkspace workspace; FR-09 changes its default orientation and FR-01 changes the components it mounts. Coordinate via `projects/INDEX.md`.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `sprk_priority` (Choice column) | `sprk_priorityscore` exists (number); no choice field | No — a numeric score can't present a user-selectable priority label/icon | Without it, users can't set priority; auto-score has no input; card can't show a priority glyph |
| `sprk_effort` (Choice column) | `sprk_effortscore` exists (number); no choice field | No — same as above | Without it, effort can't be captured; effort component of the score stays at the null-default 50 forever |
| Filter pane component (F-3) | Broken inline `inlineFilterBox` SearchBox | **Extend/replace** the existing toolbar filter surface | Without it, the kanban cannot be filtered (the current box doesn't work) — FR-06 fails |
| Everything else (hoist, top bar, coloring, modal launch, resolver wiring) | Existing LW-local components / existing Code Page toolbar / existing RegardingResolver PCF | **Yes — extend/move/config** | Modify-only or move-only; no net-new surface |

All other work is modify/move/config, not net-new. Net-new Dataverse columns: **2** (`sprk_priority`, `sprk_effort`).

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-050 | "compose the `SprkModal` family; don't open bespoke dialogs for intents a preset covers" | FR-10/11/12/13/14 keep the **OOB `navigateTo` main form** (not a `FormModal`) for To Do create/open | **A (project-scoped exception)** | Owner requires the real Dataverse main form (native business rules, Save/Save&Close, the FR-02/03 score fields). ADR-050/SprkModal explicitly does **not** govern OOB MDA dialogs, so this is not a shell violation — it is a deliberate choice of the OOB family per MODAL-DECISION-CRITERIA. Documented here + cited at code review. |

> All other listed ADRs apply without exception. FR-15 explicitly complies with ADR-050 (uses `BrowseModal`, adds no forked chrome). The ADR-050 tension above is narrow (one surface, OOB-by-owner-requirement) and does not weaken the shell elsewhere.

## Success Criteria

1. [ ] Rich Kanban components live in `@spaarke/smart-todo-components`; LegalWorkspace consumes them via the package with no duplicated logic — Verify: import graph + build + visual parity.
2. [ ] Setting Priority/Effort auto-populates the score fields per the FR-02/FR-03 mappings; composite score unchanged for existing data — Verify: unit tests + a seeded To Do.
3. [ ] RegardingResolver populates all regarding fields on a real To Do created from a parent — Verify: real-DV create+read (FR-20).
4. [ ] Top bar matches `to-do-header-revision.jpg`; Filter opens the pane; overflow = Settings/Layout/Refresh; old 'Search' gone — Verify: visual + interaction.
5. [ ] Filter categories + defaults + Clear-all work; Completed hidden by default with a working toggle — Verify: unit tests + manual.
6. [ ] No white-on-yellow surface remains; channel coloring is subtle; light/dark parity — Verify: axe-core + visual.
7. [ ] Widget defaults to side-by-side columns; flip preserves drag-drop + selection — Verify: Playwright (FR-17).
8. [ ] `+ New Task` and Open both launch the OOB main form as a full-cover modal with header hidden; Save & Close dismisses + refreshes the kanban — Verify: manual on real MDA.
9. [ ] Browse consumer uses `BrowseModal` with single-title-source — Verify: visual + code.
10. [ ] vitest green (incl. new coverage + un-skipped handleEmail 78/78); Playwright NFR suite runs — Verify: CI.

## Dependencies

### Prerequisites
- **FU-5 hoist (FR-01) before FR-02/FR-03 card UI** — the only hard ordering constraint (don't author priority/effort cards in LW-local then re-hoist).
- `sprk_priority` / `sprk_effort` columns created before their auto-score handlers + card UI.
- RegardingResolver full regarding field set present on the `sprk_todo` form before R-11 smoke (FR-04).

### External Dependencies
- Real Dataverse environment access for FR-04/FR-20 smoke tests.
- Playwright (or equivalent) tooling for FR-17 (framework decision — see Assumptions).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Modal items (FU-2/F-7/F-8) | Re-scope against SprkModal? | Yes — re-scope; FU-2 `chromeMode` obsolete | FR-15 uses BrowseModal; F-7/F-8 stay OOB |
| Sequencing | Hoist before or after F-4/F-5? | Hoist first (order otherwise agnostic — "we have to do all the work") | FR-01 precedes FR-02/03; phases are grouping-only |
| F-5 effort direction | Big-work-first (A) or quick-wins-first (B)? | **B (quick-wins-first) — confirmed** | FR-03 mapping = Low→25 … Very High→100 (locked) |
| U-3 overflow contents | What goes under `⋮`, and what do they do? | Settings → ThresholdSettings popover; Layout → orientation toggle; Refresh → reload | FR-05 overflow wiring (locked) |
| R-9 ribbon expansion | In R5 or spin out? | **In R5** | FR-19 promoted to in-scope |
| Canonical resolver | RegardingResolver or AssociationResolver? | RegardingResolver — confirmed on `master` (no AssociationResolver exists) | FR-04; no new resolver PCF |
| F-1 white-on-yellow | Still broken? | Already fixed in code / deployment | FR-08 downgraded to a verification sweep |

## Assumptions

*Implementation-time TBDs with reasonable defaults (not blocking; resolved during the owning task):*

- **U-2 enum mapping**: the exact `orientation` enum value that yields side-by-side columns will be confirmed at implementation (the code's `'horizontal'`/`'vertical'` naming is ambiguous); implement to the desired end state, not the literal enum name.
- **U-6 mechanism**: hiding the OOB form header will be achieved via form header configuration and/or a `navigateTo` chrome option and/or a dedicated modal form — mechanism decided during FR-12.
- **TEST-2 framework**: assuming **Playwright** + axe-core unless owner prefers another.
- **Priority/effort card glyph (FR-02)**: assuming a Fluent v9 colored dot or flag glyph; exact icon selected during implementation.
- **F-2 Completed treatment**: assuming filtered-OUT by default with a "Show Completed" toggle (per design).
- **R-10 version bump (FR-18b)**: assuming the RegardingResolver S1/N1 fixes ride the same version bump as the FR-04 wiring redeploy.

## Unresolved Questions

> **None.** All three prior open questions were resolved by owner on 2026-08-15 (F-5 = Option B quick-wins-first; R-9 ribbon = in R5; U-3 overflow = Settings→ThresholdSettings / Layout→orientation toggle / Refresh→reload) — see the Owner Clarifications table. The Assumptions above are implementation-detail defaults, not blocking decisions.

---
*AI-optimized specification. Original design: `projects/smart-todo-r5/design.md`.*
