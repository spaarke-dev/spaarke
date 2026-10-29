# Task 045 - Decision Record tab (D-47) and the Decision Record on closed items (#1959 item 2): record

Lane `task/ontology-045` (worktree `C:\wts-045`), off `origin/docs/ontology-platform-design` @ `eb10d114b`. Rigor STANDARD (POML), with the
[uac] re-read and a review-grade test set because the work decides record access. `TASK-INDEX.md`, `current-task.md` and the POML
`<status>` are left to the main session.

## What was built

| Layer | Change |
|---|---|
| BFF | `GET /api/v1/signals/worklist` returns, on each item of `closedToday` / `closedEarlier` / the new `decided` set, a `decision` object: record id, number, class, outcome, decided on, decided by (name), reason (the note), steps (code, taken, values, status) and follow-ons. `Services/Signals/SignalReadService.cs` (step 10), `Services/Signals/WorklistDecisionRecord.cs` (DTO + tolerant reader). |
| Client lib | `Worklist/worklistService.ts` parses `decision`; `Worklist/worklistWizardItem.ts` `resolutionFor` hands the wizard the record, so a closed item REPLAYS read-only (`hasRecordedDecision` needs `steps`). New `DecisionRecords/DecisionRecordsWidget.tsx` (+ `decisionRecordsView.ts`): `RecordRow` list, All / Judgements / Routine / Dismissals filter, Open = `launchDecisionWizard` on that item. |
| Console | `LegalWorkspace/src/sections/decisionRecords.registration.ts` (section `decisionRecords`), added to `sectionRegistry.ts`, `sections/index.ts` and the shared `SECTION_METADATA_CATALOG`. `scripts/seed-decision-records-workspace-layout.ps1` creates the "Decision Records" `sprk_workspacelayout` row (prepared, `-WhatIf` only). |

## Deviations from the POML (each a decision the POML left open or got wrong)

1. **No `sprk_gridconfiguration` row, no DataGrid.** The POML says the list is a grid-configuration row on the DataGrid framework. A DataGrid
   reads through `Xrm.WebApi` as the user, which cannot apply the core-record check (Decision Record read depth is Basic/Deep, and a record
   on a matter the caller cannot open is still in their business unit): the same reason task 050's worklist row is deliberately not a
   `DataGridConfiguration` (D-15). The POML's own constraint ("reads go through a caller-authorized path", "must not read them with a
   service identity", escalation trigger 1) is met only by the BFF route. So the tab reads the Signal read route's new `decided` set and
   the "Dataverse: sprk_gridconfiguration row" output does not exist.
2. **No new route.** `set=decided` extends the existing route, so the route-authorization ledger (uac-r2's `RouteAuthorizationGuardTests.Ledger.cs`)
   is unchanged. The route's `WithDescription` and the 400 message name the new value.
3. **Direct widget not registered** in `Spaarke.AI.Widgets` `register-workspace-widgets.ts` (the POML's "modify" file). Task 156 showed the
   widget registry alone is not a Console path (the dropdown lists `sprk_workspacelayout` rows); the section + layout row is. A second,
   unreachable registration would be surface with no use (CLAUDE.md section 11 question 3).
4. **The tab lists decided ITEMS, one row per Signal, not every Decision Record.** Two kinds of Decision Record have no item to reopen:
   gate records (D-139, task 149: one per communications-gate decision, `resolvedSignalIds: []` - three exist on dev today) and any record
   whose Signals the caller cannot read. Open ("reopens the item in the decision wizard") has nothing to open for them, so they are not
   listed. A record that resolved several Signals appears once per resolved item, with the same number. If the owner wants gate records
   in a Console history, that is a separate read (record-first, no Open) - filed in the report, not built (D-106).
5. **Window.** `decided` reaches back 90 days (`SignalReadService.DecidedWindowDays`); the tab says so. The route refuses (503) past 5 pages
   rather than truncating, as for every other set.
6. **"Who decided" is `sprk_confirmedby`** (the record's own confirming systemuser, name through an outer link), not `sprk_assignedtointernal`
   (D-129's contact column, null when the decider has no linked internal contact). Never `ownerid`. Confirmed live: DR-1064 returns
   `by.fullname` = "Ralph Schroeder"; the two gate records have no confirming user, so their name is null.
7. **Fact snapshot not returned.** It holds witness values, which the Signal's read-time witness rule (D-123/D-142) governs; the POML asks only
   for steps, note, number and decided on.
8. **`sprk_decisionrecord` is selected by code, not by the configuration row.** `ParseFetches` adds the column to the closed and decided
   queries when the live `sprk_gridconfiguration` row lacks it, so no live write is needed for the join to work. `config/worklist.gridconfiguration.json`
   is untouched. The decided query is derived from `closedEarlier` (window widened, has-a-record condition added) and is refused (503) when
   that query has no resolved-on window to widen.

## Live proof requested by the POML (task 040 constraint) - read-only, spaarkedev1, 2026-10-16

Question: which users can read a NON-secure Decision Record? Facts read: every dev Decision Record (3 rows) is owned by the team
"Spaarke Business Unit 1" (owning BU: Spaarke Business Unit 1), as `DecisionRecordWriter` assigns the BU default team (043 F1). The six
business units are siblings under the root "Spaarke"; users: 9 in root "Spaarke", 2 in "Spaarke Business Unit 1", none in the other four.
Read on `sprk_decisionrecord` (043 note): Spaarke Console User = Deep (root copy), Spaarke Basic User = Basic (root and BU 1 copies).

| Reader | Reads a BU-1-team-owned non-secure record? |
|---|---|
| Root-BU user holding Console User (9 users) | Yes: Deep covers every child BU |
| BU-1 user holding Basic User | Yes: Basic depth covers records owned by a team the user belongs to (the BU default team) |
| User in another BU (Dev 1 / Test 1 / Demo / Secure Record) | No, unless a share exists - but none of the four BUs has a user today, and their default teams hold no role |
| A matter member in a different BU than the record's owner team | Not guaranteed - this is the general case 040 flagged |

Result: **nothing is hidden from a real user on dev today.** The general limit stands (a matter member whose BU differs from the owner team's BU
and who is not in the root BU does not see the record; the Signal list has the same BU-trim, step 4 of the route). Per the POML this is a
BU or share decision for the owner, not something the tab widens: it adds no read of its own. No privilege, role or share was changed.

## uac-r2 re-read (origin/master @ `a8c3182ec`, fetched 2026-10-16)

| File | Last commit on master | Used by this task |
|---|---|---|
| `Services/Access/SecureChildLineage.cs` | `c1e16b784` (K-145-3: keeps the DR typed core lookups) | Not changed; `sprk_decisionrecord` is a registered secure child, so Dataverse shares trim the record read |
| `config/secure-record-owner-role.json` | `b2fc3fb781` | Not changed |
| `Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `ba9945149` (D-134) | Not used (read path only) |
| `Services/Dataverse/CoreAncestorResolver.cs` | `6a00d1c47` | `CoreRecordEntities` via the existing core check, unchanged |
| `Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | `c5f71487f4` | Not touched (no route added) |
| `Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` | present | Not touched |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `720200e64` (D-141/D-145a notes; newer than this branch's `163fd9730`) | Not touched. 122 route/census ArchTests pass |

Open uac-r2 PRs checked (`gh pr list --state open`): #2007 (contact bind / invite code) only; no overlap with the route, the lineage, the owner
config or the ledger. Nothing on master invalidates the plan; no uac-r2 file changed.

## Fix round (review of #2014)

- **F1 forged records.** `ReadDecisionRecordsAsync` adds `createdby eq <writer user id>` (`DecisionRecordWriter.WriterUserIdAsync` / `CreatedByColumn`, as `DecisionCommitService.FindRecordAsync` does); the read stays as the caller. Console User can Create a row and Write a Signal's lookup, so any other row could be a forgery. A non-writer record is unreadable: the item stays listed with no `decision`, and `decided` drops it. A WhoAmI fault is a 503.
- **F1 open-Signal records.** In `set=decided` only, the status condition is widened to `in (Acknowledged, Resolved)`, so a review that leaves its Signal open (inquiry sent, reminder, follow-on only) is listed. `closedToday` and `closedEarlier` stay Resolved. The client shows such a row as recorded and still open (`resolutionFor`: `leftOpen`, resolution Acted, or Dismissed for a Dismissal class).
- **F3 who decided.** The test double returns the name only through the outer link `systemuser` on `sprk_confirmedby`; a test asserts the link and the absence of `ownerid`.
- **F2 401 / 403.** Only an actual 403 means "no Read on the table (or on the confirming user the query links to)": the records are omitted and a Warning is logged. A 401 is a fault: 503 (as the witness read).
- **Seeding proof** (four mutations applied together: `createdby` condition removed, link `to` changed to `ownerid`, 401 branch disabled, status `in` changed to `eq`): 9 tests failed, including each targeted test (forged, inquiry-sent, status widening, who-decided link, 401/403). Service restored; 69 pass.

### Disclosed limits (no code change)

- **K1 (one row per Signal).** A later review overwrites `sprk_signal.sprk_decisionrecord`, so the tab shows only the latest record for a Signal reviewed twice. Gate DRs (D-139) have no Signal and are not listed. The fix is a record-first read (list Decision Records, then join to the Signal); the main session files the issue.
- **K1 (Do lane).** Decided Do-lane items follow the subject's current assignee ("mine" rule), so a reassigned To Do moves its record to the new assignee's tab.
- **K3.** `steps[].values` and the note are returned as stored. Not a new disclosure: the caller can already read them in the model-driven app.
- **K4.** "Who decided" is `sprk_confirmedby` (the systemuser who confirmed), per D-145b, never `ownerid`. The D-129 wording is for the main session to raise with the owner.
- **K2.** The outer link to systemuser fails as a whole without `prvReadUser`; the 403 Warning covers it.

## Verification

- BFF `SignalReadRouteTests`: 69 pass (53 before + 16 new). Mutation: removing the decided-set drop and the closed-query column select fails 7 tests. `RouteAuthorizationGuard` + `RecordOwnerAssignmentCensus` ArchTests: 122 pass. `DoLanePrivacy` (static members of the service): 45 pass.
- UI.Components jest: `DecisionRecords` (16) + `Worklist/__tests__/worklistDecision` (10) new; `DecisionWizard.test.tsx` has 1 failure (Discard check `alertdialog`) that also fails with the pre-fix `worklistWizardItem.ts`, in a file this PR does not touch; Worklist, ConsoleKit, WorkspaceShell suites pass (350 total in the three runs). Mutation: reverting `resolutionFor` and the class filter fails 8 tests.
- DailyBriefing.Components jest: `decisionRecordsSectionRegistration` (5 new) + `legalWorkspaceSectionRegistry`: 10 pass.
- `tsc --noEmit` in UI.Components: 9 errors before and after (unresolved `@spaarke/auth`, `@spaarke/sdap-client`, `FileUploadService.ts`), none in a touched file. eslint on the new and changed client files: clean.
- BFF hygiene (section 10): placement = in the BFF, extending the existing Signal read route and service (no new route, DI registration, package or background work; `bff-extensions.md` criteria: the read must run as the caller beside the existing Signal reads). Publish size, fresh worktrees at `C:\wb45m` (branch `79731ff5f`) vs `C:\wb45b` (`origin/docs/ontology-platform-design` @ `eb10d114b`): `dotnet publish -c Release -r linux-x64 --self-contained false`, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included, 192 files each side: 38,953,600 B -> 38,961,006 B (+7,406 B, +0.007 MB; 37.156 MB total). No package changed, so no new CVE (`dotnet list package --vulnerable` not re-run).
- Not run (no browser): the two UI tests in the POML (filter then Open under the status bar; dark mode). The jest tests render both themes and prove the launcher call and the wizard item; the visual check is the owner's, as for 055.

## Live actions prepared, not run

- `pwsh scripts/seed-decision-records-workspace-layout.ps1 -WhatIf` (run: reports "would create"). To apply: the same without `-WhatIf` (creates the "Decision Records" system `sprk_workspacelayout` row, sort order 14).
- Console deploy: CI publishes `sprk_spaarkeai` from master (gotcha 2026-10-16); the BFF route change needs the BFF deploy from master (D-126).
