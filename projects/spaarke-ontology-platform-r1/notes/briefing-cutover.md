# Task 064 - Briefing cutover: the worklist replaces the Daily Briefing tab

Branch `task/ontology-064` (worktree `C:\wts-064`, off `origin/docs/ontology-platform-design` 5a1477dda). Rigor FULL, Sonnet 5.5 @ high,
directional steps. Binding: BR-4, BR-5, FR-35, FR-54, FR-55, FR-61; D-15, D-26, D-27, D-34..D-36, D-43; HANDOFF `ae1cc9f` section 1.4 (reconciliation B.6).

## 1. What the cutover is, and how it is reverted (read this first)

The Console lands on the BFF's default layout: `WorkspaceLayoutService.GetDefaultLayoutAsync` step 2 returns the system
`sprk_workspacelayout` row with `sprk_isdefault = true` (lowest `sprk_sortorder` first). So "which tab opens" is **data**, and the flag in the
POML ("keep the old widget behind a flag") is that one Boolean on two system rows. On spaarkedev1 today (read 2026-10-17):

| Row | Id | Sections | Default |
|---|---|---|---|
| Daily Briefing | `6d41d537-fe55-f111-a824-3833c5ed9c11` | `daily-briefing` | **yes** (sortOrder 0) |
| Worklist | `202bcde0-13c9-f111-a05c-0022482913fc` | `worklist` | no (sortOrder 13; seeded by task 156) |

The `worklist` section (task 156 shim over task 059's `WorklistWidget`) is already in the deployed Console bundle (built from master `a8c3182ec`), so
**the cutover needs no code deploy**: it is one data switch, `scripts/Set-DefaultWorkspaceLayout.ps1` (new in this task). **Run it only after the non-admin gate in section 6.1 passes or the owner waives it in writing.**

```powershell
# cutover (reads only with -WhatIf)
pwsh ./scripts/Set-DefaultWorkspaceLayout.ps1 -To Worklist -WhatIf
pwsh ./scripts/Set-DefaultWorkspaceLayout.ps1 -To Worklist
# REVERT, no deploy (also a read-only no-op if already on the Briefing)
pwsh ./scripts/Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'
```

The script writes only `sprk_isdefault` on system rows. It sets the target to true first, then clears the others (so there is never a moment with no default,
and an interrupted run leaves the Daily Briefing, sortOrder 0, winning). It reads back and throws unless exactly the target is the only system default. It is
idempotent. `-WhatIf` run against dev on 2026-10-17: Worklist direction prints "would PATCH Worklist = True, Daily Briefing = False"; the revert direction prints
"NO CHANGE" (nothing was written). The write and verification path was exercised against an in-memory fake of the two REST calls (cutover, repeat, revert, missing
target), not against dev, because this lane may not write to dev.

What stays installed for the one-release revert window: the `daily-briefing` section registration (`dailyBriefing.registration.ts`, comment added), its
`sectionRegistry.ts` entry, the Daily Briefing layout row, the Briefing BFF routes (`/narrate`, `/render`, `/email`) and the Briefing email. Removal is a later
task, after the release. The Daily Briefing row is **still selectable from the Workspaces dropdown** during that window (deliberately: hiding it would break
the Assistant's "Open Daily Briefing" card, which opens that row by id, and would make the revert two steps).

Repo side: `scripts/system-layouts.json` is **not edited by this task** (fix round 1, coordinator decision). That file is embedded into customer provisioning
(`Sprk.Provisioning.ControlPlane.Core.csproj`, `DataverseWebApiWorkspaceLayoutSeeder`): changing its default would change every new customer's landing tab, and a re-run on an
existing environment would create a second system default. Whether the Worklist should be the default for new customers is an owner scope decision (DI-103 / #2006). The
cutover is therefore a **dev-only data switch** through `Set-DefaultWorkspaceLayout.ps1`; the file still seeds the Daily Briefing as default. Guard (the sections both stay
registered, the seed still points at `daily-briefing`, the script's safety properties): `Spaarke.DailyBriefing.Components/test/briefingCutoverLayouts.test.ts`. Provisioning
seeder tests re-run: `DataverseWebApiWorkspaceLayoutSeederTests` 9/9 pass.

## 2. Gate check (escalation trigger: "if either lane is not rendering real data, STOP") - not fired

Read-only, spaarkedev1 and `spaarke-bff-dev`, 2026-10-17 ~04:00Z, as the owner's identity (a Dataverse admin).

- Policies enabled (6 rows exist): `POL-TASK-OVERDUE` (…6101), `POL-TASK-DUESOON` (…6102), `POL-WA-RESPONSE` (…6103), `POL-TODO-OVERDUE` (…6104) - Do lane;
  `POL-COMMIT-BUDGET` (`4d204810-61bf-f111-aaaf-0022482913fc`) - Decide lane. `POL-MATTER-NOBUDGET` (…6301) is Off, as intended (Know).
- Signals in `sprk_signal` (88 rows): Decide `POL-COMMIT-BUDGET` 2 open (+2 other status); Do `POL-TASK-OVERDUE` 19 open (+2), `POL-TODO-OVERDUE` 44, `POL-WA-RESPONSE` 19.
- The REAL route `GET /api/v1/signals/worklist?set=lanes` returned 200 with **Decide: 2 cards / 2 items** (both matters, "Fee or scope change, no budget revision") and
  **Do: 18 cards / 49 items** (10 Overdue task, 39 Overdue To Do; 13 matter cards, 4 project cards, 1 "Not filed" card with 13 items, which is last).
  That is the shape that motivated "Decide always above Do": 2 decisions and 49 pieces of overdue work. The owner reviewed a Decide item end to end in the deployed
  Console Worklist on 2026-10-16/17 (task 055, D-154: DR-1066, budget revision, inquiry sent, Signal Resolved).
- The narrative path: `POST /api/ai/daily-briefing/narrate` with the counts-only request the widget builds (2 to decide, 49 to do, 49 past due) returned 200 with
  a summary and three bullets. (Note: the answer is the narrator's plain count sentence, "51 notifications across 3 categories ...", not a written paragraph; the live
  `BRIEF-NARRATE-TLDR` row is the one 062 asks the main session to re-sync with `Sync-BriefNarrateOutputSchemas.ps1 -IncludeSystemPrompt`, still pending.)

## 3. Acceptance criteria and evidence

| Criterion | Evidence |
|---|---|
| Console Workspace opens on the worklist with the narrative on | After the switch the BFF default is the `Worklist` row (script read-back). Narrative is on by default: `WorklistWidget` asks `service.narrate` when the host passes none (`narrative === undefined && !anyFilter`), and `worklist.registration.ts` builds the service. Live `/narrate` returns 200 (section 2). **Browser check after the switch: pending, section 6.** |
| Decide above Do | `WorklistWidget.test.tsx` "renders Decide above Do ..." (existing) + the live route (2 Decide cards before 18 Do cards) |
| Many Do, few Decide: Decide stays visible | New test "keeps every Decide line above a long Do lane: 49 overdue Do items ..." (49 Do + 2 Decide: both Decide lines render, no "Showing n of m", the Decide lane precedes the Do lane in the document, the first two lines are the decisions). Mutation proof: reversing the lane order failed it (received 0), restored |
| Negative: old widget reachable behind a flag | The flag is `sprk_isdefault`; `Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'` reverts with no deploy. `briefingCutoverLayouts.test.ts` (4 tests) fails if either section leaves the registry, the provisioning seed stops pointing at `daily-briefing`, or the script loses its `If-Match` / cancelled handling (that test fails against the previous script) |
| Client builds, cutover documented | `vite build` of `src/solutions/SpaarkeAi` (includes LegalWorkspace): exit 0, `index.html` 6,215.98 kB (gzip 1,736.06 kB); this note |
| Non-admin reader sees only own Do work, nothing on an unreadable matter | **NOT verified on dev as a non-admin by this lane** (only an admin identity is available). Server rules are unit-tested in `SignalReadRouteTests` (`EventDoRows_OwnerAssigneeAndCreatorSeeThem_AColleagueDoesNot`, `TodoDoRows_OwnerOrAssigneeSeeThem_AnyoneElseDoesNot`, `SameBuColleagueWhoCannotReadTheMatter_GetsNothing_AndNothingRevealsIt`, `DoRow_OnACoreTheCallerCannotRead_IsAbsentEvenWhenTheSubjectIsTheirOwn`, `WorkAssignment_AssignerAndAssigneeSeeIt_AThirdColleagueDoesNot`) and live in `SignalReadRouteLiveTests.LiveMode_SyntheticSignalsOverRealRecords_RealUsersSeeOnlyWhatTheyMayRead`. The re-run as the non-Console user is DI-83 / #1882 (open). Steps for the owner/main session: section 6 |
| Every HANDOFF 1.4 rule has a pass or a recorded refinement | Section 4 |
| Non-admin: project cards under their project; Not filed only for the owner | As the owner: 4 project cards render under their project (type label "Project") and 1 Not filed card with 13 items, owner only; unit tests `NoCoreSignal_ToDoOwnerGetsItNotFiled_ASameBuColleagueDoesNot`, `EveryCoreType_GoesThroughTheSameCatalogDrivenCheck`. **Non-admin reader (one with and one without a Not filed To Do): not verified here**, section 6. Work-assignment and service-request cards: no WA Signal is in the owner's list (19 WA Signals exist org-wide, none for this reader), so the WA card shape was not seen live; covered by `WorklistWidget.test.tsx` fixtures (work assignment, service request, project, matter, Not filed cards) and the WA routes tests above |

## 4. HANDOFF section 1.4, rule by rule (the cutover check, B.6)

Backend and wizard rows cite the owning task's tests and notes; this lane re-ran only the client suites in section 8. Pass = verified by the evidence named. "Live" = against spaarkedev1/`spaarke-bff-dev` as the owner; "jest" = jsdom unit tests only; no rule below was
eyeballed in a browser by this lane (no deployed build carries the switch yet, no browser in this lane).

**Determinism and honesty**

| Rule | Result | Evidence |
|---|---|---|
| Membership and order from rule evaluation only; severity, oldest, matter number; matter's items together; **Decide above Do**; no model selects or orders; no AI "top action" | Pass | Live: route order, Decide before Do, Not filed last. `SignalReadRouteTests.LaneOrder_IsSeverityThenHighPriorityThenOldestThenRecordNumber_AndRepeatable`; `WorkItemOrderAndMembershipTests` (062: the read route reaches no model code); the widget never sorts (source scan in `WorklistWidget.test.tsx`); `TopAction` removed (062) |
| Every claim in a sentence maps to a tested clause; classifier claims phrased as classifications | Pass with refinement | Headlines are composed from display columns (D-23). **Refinement:** the route does not return the rule's `sentence`, so the wizard's sentence is empty and it says Missing, never an invented one (059 gap 2; #1959). The old Briefing had no such sentence either |
| Evidence shown but not tested is labelled context, not tested | Pass (jest + owner) | Wizard `EvidenceLine` tiers (058); owner reviewed a live Decide item (D-154) |
| Missing or stale data shows Missing, never zero or omitted | Pass (jest) | `EvidenceLine` Missing; the narrative fails closed on a scrubbed term (`acceptNarrative`); the aggregate card reports its count as Missing without Xrm |

**Filters** (all jest: `laneCountFilters.test.tsx`, `WorklistWidget.test.tsx`; not clicked in a browser)

| Rule | Result |
|---|---|
| Count cards describe the lane's full list; "Showing n of m - Show all" while a filter is on | Pass |
| A card with items outside the filter says "+N other open item(s) on this matter, not in this filter" | Pass |
| Filters are not remembered between sessions | Pass ("a new mount opens with no filter") |
| The Email Review aggregate is not in either lane's counts | Pass (jest; the live card needs Xrm) |

**The wizard** (task 058 jest; the Decide path live once by the owner, D-154)

| Rule | Result |
|---|---|
| Steps come from data; no switch on rule/lane/signal shape | Pass (058 tests; live Decide item) |
| Each action its own step, skippable; invalid required fields cannot be taken but can be skipped | Pass (jest; live: Send budget inquiry + Revise budget) |
| Excluding actions never both run | Pass (jest) |
| Skipping every action makes Confirm a dismissal with a required reason | Pass (jest) |
| Next steps created only when recorded, linked to the decision, not gated | Pass (jest). Live: the Next-step To Do was **not exercised** (D-154) - carried to task 105 UAT |
| Browse "N of M" over the list the wizard opened from, no wrap, loses nothing typed | Pass (jest) |
| A decided item opens read-only with a status bar on every step | Pass (jest). **Refinement:** the route does not return the Decision Record for closed items, so a closed item opens read-only without replayed steps (059 gap 3; #1959) |
| After recording, the item shows decided in place and focus moves to the status bar | Pass (jest) |
| Escape/backdrop never close; X/Cancel/Close with unrecorded choices ask first | **Not re-proven here**: the one jest test for it (`DecisionWizard.test.tsx`, Escape/backdrop) fails in this environment, alone and in the folder (section 8; #1961). The other close/discard tests pass. Needs the browser check in 105 UAT |
| Confirmation rendered inside the wizard's tree; keyboard reachable after Keep deciding | Pass (jest) |

**Decisions and records**

| Rule | Result | Evidence |
|---|---|---|
| One review writes exactly one Decision Record, listing every action offered; Cancel/X/browse write nothing | Pass | Live: DR-1066 (one record, Send budget inquiry + Revise budget); 040/043 tests |
| Every human resolution writes one; class derived (Dismissal / Routine / Judgement), shown at Confirm | Pass (jest; live: Judgement) | 040, 043 |
| One decision may resolve several Decide issues on the same matter, only those ticked | Pass (jest) | 058 |
| A condition that clears on its own closes as Cleared itself, no person, no record, never counts toward suppression | Pass | task 033/034 tests |
| Three dismissals of one rule on one matter suppress it for 30 days; a not-count rule never suppresses; Wrong matter never counts | Pass | task 034 tests; D-39 per-item suppression where there is no core record |
| Decision Record is append-only; no edit or delete affordance | Pass | Privilege: task 049 (no human or Spaarke role holds Write/Delete on Decision Records; verified live 2026-10-17); UI: `RecordRow` has none |

**Panes and accessibility**

| Rule | Result |
|---|---|
| Context pane audit-only | Not part of this task's surface; unchanged |
| Light and dark correct, Fluent v9 tokens only; keyboard reachable | Pass in jest (every part renders under the dark theme; no colour literal in the Worklist sources, ADR-021). **No visual eyeball was done** - section 6 |

Refinement of the constraint text: the POML says the old Briefing used a 5-day overdue threshold. It does not any longer: `DailyBriefingCollector` already judges
"overdue" as due before the caller's local today (D-43, 1 day). The real differences between the Briefing and the Do lane are in the next section.

## 5. Before / after counts (D-43), spaarkedev1, 2026-10-17

Org-wide populations, read straight from Dataverse (no per-reader filtering):

| What | Old Briefing channel (collector definition) | New Do lane (open Signals) |
|---|---|---|
| Overdue tasks (Task-type events, Open status) | 19 (due before today). The old 5-day rule would also give 19: all are older than 5 days | 19 `POL-TASK-OVERDUE` (+2 in another status) |
| Overdue To Dos | **0** - the Briefing's To Dos channel lists only To Dos due **today or later** (`OnOrAfter(today)`); the 44 overdue ones never appeared | 44 `POL-TODO-OVERDUE` |
| Tasks coming due (5 days) | 0 | 0 `POL-TASK-DUESOON` (no Task due in the window) |
| Work assignments past their response date | not in the Briefing | 19 `POL-WA-RESPONSE` |
| Total | 19 | **82** (and Decide: 2) |

Why Do volume is higher: not the 5-day to 1-day change (it adds 0 on today's dev data; the collector already used 1 day) but that **overdue To Dos (44) and
work assignments past response (19) were never in the Briefing**. Per reader, the owner's Do lane is 49 items (10 overdue tasks, 39 overdue To Dos) and the Decide
lane 2. Draft text for the cutover note to users: "The Workspace now opens on your Worklist: judgements that need you (Decide) above work Spaarke already holds
that has slipped (Do). Do now includes overdue To Dos and work assignments past their response date, and starts at one day overdue, so the Do list is longer than
the old Briefing's task list. The Briefing is unchanged for now and can be opened from the Workspaces menu. If you already had a Briefing tab open or pinned it stays open next to the Worklist, which opens as well; after the change, the first reload of a browser tab may still show the Briefing, and the next reload opens the Worklist."

## 6. Runbook: gate first, then the switch (not done by this lane)

### 6.1 Gate - the switch runs ONLY after these pass, or the owner waives them in writing

The POML (line 39, D-15/D-26) requires the non-admin reader check *before* the swap; this lane had only an admin identity, so it is not done. Sign in as non-admin readers
(no Dataverse admin role), open the Worklist (or call `GET /api/v1/signals/worklist?set=lanes`) and compare with the owner's list (Decide 2, Do 49, 1 Not filed card of 13):
1. **Non-admin reader on both lanes (D-15/D-26):** the Do lane shows only that reader's own work and no Signal on a matter they cannot read; Decide shows only Signals on matters they
   can read; a decision opens in the in-app wizard. (This is the DI-83 / #1882 re-run, still open.)
2. **Not filed and project cards (D-34/D-35):** project-grouped items appear under their project; Not filed appears only for its owner. Use two readers: one who owns a Not filed To Do and one who does not.
3. **Work-assignment and service-request cards (D-36):** not seen live (no WA Signal in the owner's list; 19 exist org-wide). Check as the assigner or assignee of one of them.

If the owner waives any of 1-3, record the waiver (who, when, which check) in this file before running 6.2.

### 6.2 The switch (main session, after the PR merges and 6.1 is done or waived)

Run the commands in section 1 (`-WhatIf` first). Tell word-add-in (Console change notice) before and after. No deploy is involved. What users see afterwards:
- The first reload in the same browser tab still opens the **cached** Daily Briefing (the layout list is cached in `sessionStorage`; `useWorkspaceLayouts.ts` hydrates from it and
  `WorkspacePane` installs the default once per mount, before the revalidation lands). The next reload opens the Worklist.
- A restored (NFR-09) or pinned Daily Briefing tab does **not** stop the Worklist being added and activated: the install skips only when the new default itself is already open or
  pinned (`WorkspacePane.tsx` default-install effect; `ManageWorkspacesPane.tsx` treats the first pinned workspace as the default). The Briefing tab stays open beside it.
- A person's own default (BFF step 1) still wins for that person.

### 6.2a Switch run (2026-10-17, main session)

- Owner approval 2026-10-17 ("that's fine", D-164). The §6.1 checks were not run first; the owner approved the switch with them moved to 105 UAT.
- Notices sent before and after to word-add-in, R3 and uac-r2 (uac-r2: no objection).
- 14:38Z: `-WhatIf` printed exactly two writes (Worklist = True, Daily Briefing = False); the run PATCHED both and printed
  "VERIFIED: the only system default is 'Worklist' (202bcde0-13c9-f111-a05c-0022482913fc)".

### 6.3 After the switch

The POML's UI tests have nothing to click until the default changes: Workspace opens on the Worklist with the narrative; Decide above Do with seeded overdue tasks and a decision;
revert with the script and see the Briefing; dark mode. Suggested: do these in task 105 UAT with the owner.

## 7. Deviations from the POML (D-106: reported, not fixed)

- **The "flag" is the layout default flag, not a code flag.** A code flag read from `/api/config` would need an App Service setting (an Azure change that needs the
  owner) and a restart; the layout row is how the Console already chooses its tab, needs no new surface (CLAUDE.md section 11), and reverts with one Dataverse write.
  `src/solutions/SpaarkeAi/src/components/workspace` needed no change: the default-tab effect is already data-driven.
- **Know awareness leaves with the Briefing.** 062 deviation 1 / P-1: the Briefing TL;DR carried the Know counts (including "Monitored records"); the worklist
  narrative carries Do/Decide volume only and there is no Know feed for the Context pane. During the revert window the Briefing still has them.
- **The Assistant's "Open Daily Briefing" follow-on card still opens the old Briefing row** (`surfaceLaunchRegistry.ts` `daily-briefing`, by row id
  `6d41d537-...`). Left alone: it is also the revert fallback. Repointing it is a follow-up for after the release.
- Pre-existing, unchanged: six `Spaarke.DailyBriefing.Components` suites cannot compile in a package-only install (062 notes P-2/P-3); 153 tests in the 20 suites that run pass.

## 8. Tests and builds run in this task

- `Spaarke.UI.Components` jest, Worklist folder: 8 suites, 245 tests pass (one new: the 49-Do / 2-Decide test; mutation proof: reversing the lanes failed it).
  DecisionWizard + ConsoleKit + WorkspaceShell: 211 pass, 1 fails: `DecisionWizard.test.tsx` "Escape and the backdrop do nothing ..." (`Unable to find role="textbox"
  and name /^To/`). It is untouched by this task and fails the same when the file runs alone here (059 notes call it a folder-run flake that passes alone; issue #1961).
- `Spaarke.DailyBriefing.Components` jest: 20 suites / 153 tests pass; 6 suites do not compile in a package-only install (pre-existing, 062 P-2/P-3); the new
  `briefingCutoverLayouts.test.ts` (4 tests) passes; the script-safety test fails against the previous script.
- `Sprk.Provisioning.ControlPlane.Tests` filtered to `DataverseWebApiWorkspaceLayoutSeederTests`: 9/9 pass (the seed file is unchanged by this task).
- `Set-DefaultWorkspaceLayout.ps1`: a declined `-Confirm` prints "Cancelled, nothing written." and writes nothing (custom PSHost that answers No); PATCH carries `If-Match: *`.
- Client lint ratchet (`node scripts/quality/client-lint/client-lint.mjs check`): `ui-components` ok, `daily-briefing-components` ok. `legal-workspace-app` not run locally
  (comment-only change there; needs its own install).
- `tsc --noEmit` in `Spaarke.UI.Components`: 9 errors, the same 9 as the 059 notes (unresolved `@spaarke/auth` / `@spaarke/sdap-client`, `FileUploadService.ts`), none in a file this task touched.
- `npm run build` in `src/solutions/SpaarkeAi`: exit 0 (`tsc-surface-gate`: 455 pre-existing shared-lib errors, 0 surface-owned).
- `Set-DefaultWorkspaceLayout.ps1`: `-WhatIf` against dev (reads only) in both directions; write/verify path against an in-memory fake of the REST calls.
