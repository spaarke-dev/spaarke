# UAT log: Console v4 (task 105)

The UAT script is the prototype's `HANDOFF.md` §1.4 "Rules that must hold" (`spaarke-dev/spaarke-prototype`, `ae1cc9f`,
`projects/2026-10-spaarke-console/HANDOFF.md`). Every rule gets **pass** or **refine** with a date, and every refinement gets a task
id or an issue URL. 105 closes only when the owner says UAT is complete; that statement is recorded at the bottom.

Where to test: the Spaarke Console on dev (spaarkedev1) → Workspace → **Worklist** tab. Open a row's **›** to open the decision
wizard. Run every wizard check once in light mode and once in dark mode (no white surfaces), and once with the keyboard only.

## The checklist

Status key: ✅ pass · ✏️ refine (see Refinements) · ⬜ not yet tested.

### Determinism and honesty

| # | Rule | How to check | Status |
|---|---|---|---|
| R1 | List order comes from rules only: per lane, severity, then oldest, then matter number; a matter's items stay together; **Decide above Do**; no AI-chosen order or "top action". | Look at the Worklist: Decide lane first, then Do; within a lane, severity then age. | ⬜ |
| R2 | Every claim in a sentence maps to a tested clause; classifier-based claims say *"was classified as"*, never state it as fact. | Read the row sentences and *What was found*. The fee/scope item says "was classified as a fee or scope change". | ⬜ |
| R3 | Evidence shown but not tested is labelled *context, not tested*. No graphic implies a comparison the rule didn't make. | *What was found* step. | ⬜ |
| R4 | Missing or stale data shows as **Missing**, never as zero and never left out. | A matter with no budget shows "Missing", not 0. | ⬜ |

### Filters

| # | Rule | How to check | Status |
|---|---|---|---|
| R5 | Each lane's count cards describe the lane's full list; with a filter on, *Showing n of m · Show all* is visible. | Click a count card. | ⬜ |
| R6 | A matter with items outside the filter says *"+1 other open item on this matter, not in this filter"*. | Filter to one rule on a matter that has two items. | ⬜ |
| R7 | Filters are **not** remembered between sessions. | Set a filter, reload the Console. | ⬜ |
| R8 | The Email Review aggregate isn't counted in either lane. | Compare the lane counts with the Email Review count. | ⬜ |

### The wizard

| # | Rule | How to check | Status |
|---|---|---|---|
| R9 | Steps come from data (the rule version's decision plan, its exclusions, the Next-steps set). | Two different rules show different steps. | ⬜ |
| R10 | Each rule action is its own step and can be skipped; an action with invalid required fields can't be taken but can be skipped. | Leave a required field empty: Next is disabled, Skip works. | ⬜ |
| R11 | Two actions that exclude each other never both run: taking the earlier removes the later step. | Fee/scope item: take *Revise budget*, and *Approve variance* disappears (the screen says so). | ⬜ |
| R12 | Skipping every action makes Confirm a dismissal; a reason is required. | Skip all, reach Confirm. | ⬜ |
| R13 | Next steps are created only when the decision is recorded, linked to it (*Created by decision DR-…*), not gated. | Add a Next step (e.g. *Add To Do*), record, open the created To Do. | ⬜ |
| R14 | ‹ N of M › browses the list the wizard was opened from, doesn't wrap, and loses nothing typed. | Type a value, go ›, come back ‹. At the last item, › is disabled. | ⬜ |
| R15 | A decided item opens in the same wizard, read-only, with a status bar on every step; *What was found* shows the record's snapshot. | Open a decided item from *Closed today*. | ⬜ |
| R16 | After recording, the item shows as decided in place and focus moves to the status bar. | Record a decision; watch focus (keyboard). | ⬜ |
| R17 | Escape and the backdrop never close a wizard. × / Cancel / Close with unrecorded choices on any item ask first; with none, they close at once. | Press Esc; click outside; × after typing; × with nothing typed. | ⬜ |
| R18 | The confirmation is rendered inside the wizard; after *Keep deciding* the wizard is still reachable by keyboard and screen reader. | Record, choose *Keep deciding*, Tab through. | ⬜ |

### Decisions and records

| # | Rule | How to check | Status |
|---|---|---|---|
| R19 | One review writes exactly one Decision Record listing every offered action (taken or skipped, with values) and the Next steps created. Cancel, × and browsing away write nothing. | Record one; open the matter's Decision Records. Cancel another; confirm nothing new. | ✅ 2026-10-22 (a DR was created and linked to the matter; owner) |
| R20 | Every human resolution writes one. Record class is derived: *Dismissal* if nothing taken; *Routine* for bare completions of one's own work; *Judgement* otherwise, and always with a Next step. Confirm shows the class first. | Check the class shown on Confirm against what was taken. | ⬜ |
| R21 | One decision may resolve several Decide items on the same matter, only those ticked under *Also resolve*. Do items never resolve each other. | A matter with two Decide items. | ⬜ |
| R22 | A condition that clears on its own closes as *Cleared itself*, with no person and no Decision Record, and never counts toward suppression. | Add a budget to a "no active budget" matter; next evaluation closes it. | ⬜ |
| R23 | Three dismissals of one rule on one matter suppress it there for 30 days; Email Review never suppresses; *Wrong matter* never counts. | Needs three dismissals; can be checked on seeded data. | ⬜ |
| R24 | The Decision Record is append-only: no edit or delete affordance anywhere in the UI. | Open a Decision Record in the Console and in the model-driven app. | ⬜ |

### Carried over from earlier tasks (not in §1.4)

| # | Check | Status |
|---|---|---|
| C1 | 102 wizards: the in-app Create wizards work end to end. | ✅ 2026-10-22 (owner: "they all seem to work") |
| C2 | 102 wizards: keyboard only (Tab / Shift+Tab / Enter / Esc) and a screen-reader pass. | ⬜ |
| C3 | 064 §6.1: a non-admin user sees and uses the Worklist. | ✅ 2026-10-22 (owner) |
| C4 | 055 "Next step To Do": in a decision wizard, tick *Add To Do* on the Next steps step, record, and confirm the To Do exists linked to the Decision Record. | ⬜ |

## Round 1 — 2026-10-22 (owner, dev)

Raw findings as the owner reported them. Each becomes a refinement below once triaged.

1. **Assign Work** (rule "matter created more than 5 days ago with no active budget"): *Assign to* is an inline dropdown; it should use the standard side-pane lookup.
2. **Assign Work** creates a Work Assignment. A Work Assignment is for creating a matter or a project; this should create a Task, or offer the choice.
3. A Decision Record was created and linked to the matter (R19 pass).
4. The wizard has no link to the subject record (the matter), so there's no easy way to navigate to it.
5. **Send budget inquiry**: the *To* field doesn't look anything up, and the step should use the shared email component, not the old wizard's.
6. **Revise budget**: the *Budget to revise* lookup doesn't work. It should populate from the related record, and use the side-pane lookup when a choice is needed.
7. The inquiry email (address typed by hand) was sent, but had no link to the record or the budget, and was sent as the user. Its footer shows a raw `sprk_servicerequest <guid>` and a long `Ref:` token.

## Refinements

| Finding | Refinement | Task / issue |
|---|---|---|
| 1 | *Assign to* (and every Person/Lookup field) opens the side-pane lookup (D-182). | [158](../tasks/158-decision-wizard-side-pane-lookups-and-subject-link.poml) |
| 2 | The Decide action becomes *Assign Task*, which creates a `sprk_event` of type Task. Next steps offer *Add To Do* and *Assign Work* (D-178). | [159](../tasks/159-decide-assign-task-creates-event-task.poml) |
| 3 | None: R19 pass. | — |
| 4 | The subject record in *What was found* is a link that opens it in a modal (D-182). | [158](../tasks/158-decision-wizard-side-pane-lookups-and-subject-link.poml) |
| 5 | Shared EmailComposer, compose-only, side-pane recipient lookup, To pre-filled from the outside firm (D-179). | [160](../tasks/160-budget-inquiry-email-composer-and-record-link.poml) |
| 6 | The lookup failed because the matter has 0 budgets. 0 budgets → *Create budget* (D-180); 1 → pre-selected; several → side pane. | [161](../tasks/161-create-budget-when-matter-has-none.poml), [158](../tasks/158-decision-wizard-side-pane-lookups-and-subject-link.poml) |
| 7 | Link by recipient: app link for internal users, portal `?record=` link for external contacts. The footer names the record; Ref stays. Sending from the user's mailbox is correct (D-179, D-181). | [160](../tasks/160-budget-inquiry-email-composer-and-record-link.poml) |
| 114 | Copy rules failed: the field mapping reads lookup type 100000001 as Text. | [#2281](https://github.com/spaarke-dev/spaarke/issues/2281) (field-mapping project, DI-122) |
| 114 | Uploaded document bound to `sprk_relatedworkassignment`, not `sprk_workassignment`. | [162](../tasks/162-wizard-document-lookup-exact-column.poml) |
| Q | Action steps as configuration, and an AI agent that configures the wizard. | New project spaarke-ontology-platform-r2 (D-183) |

## Owner completion statement

(not yet given)
