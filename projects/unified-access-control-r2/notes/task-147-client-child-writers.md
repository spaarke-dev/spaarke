# Task 147: children created or re-filed in the browser, and outside the product (C10 part 2, client; #1069)

> **Date**: 2026-10-04 · **Branch**: `task/uac-r2-147` · **Rigor**: FULL (opus, high)
> **Base**: `2bd0175ad`, which is `task/uac-r2-148-r2` (`ca991adea`) with `work/unified-access-control-r2` (`001b3b0aa`)
> merged in. The merge was clean: only two docs changed, no conflict.
> **Status: ESCALATED.** Done: the L4 net in code, the `sprk_memo` taxonomy change, the census, and the per-writer
> proposal. Stopped: the client routing and switching on the schedule. Two escalation triggers fired that owner rounds
> 1-13 do not answer (§6, E1 and E2). Nothing is deployed, and nothing was written live (every live action was a GET).

## 1. Outcome

| Part | State |
|---|---|
| Step 1: census of client writers | **Done** (§2). 29 call sites in 21 files: 18 creates and 11 re-files. 12 writers are new since the POML's list, and 21 transport adapters are listed separately |
| Step 2: `sprk_memo` in the CHILD taxonomy, both sides, parity green | **Done.** It is in C# `CoreAncestorResolver.ChildRecordEntities`, TS `CHILD_RECORD_ENTITIES`, **and** the stamp backfill script's `$ChildEntities`. The script was the third copy, and nothing pinned it; it is now pinned by the new `Taxonomy_MatchesTheStampBackfillScript` |
| Step 2: backfill dry run | **Done** (§4). It also found that the script **could never run**: every candidate query returned 400. That is fixed, and so is a second defect in the `-Apply` write shape |
| Step 3: decision per writer | **Proposed for every writer** (§2, §3). It is **not final**: it depends on E1 |
| Step 4: implement each writer | **Stopped** (E1) |
| Step 5: job watermark pass plus fix-up entry point | **Done in code** (§5). It is the recent-changes pass in task 148's job. The entry point is `SecureChildShareSynchronizer.SecureRootsAboveAsync` + `SecureChildReconciler.ReconcileAsync` |
| Step 5: schedule plus writes on in the deployed configuration | **Stopped** (E2). The job stays registered disabled and report-only |
| Step 6: tests | **Done for what was built.** 7 integration tests, 2 C# unit tests and 1 jest test; 8 seeds, every one bites (§7) |
| Step 7: docs, build, tests | **Done.** DATAVERSE-WRITE-PATH-ARCHITECTURE §4.3 + I-2 and SECURE-PROJECT-ENVIRONMENT-SETUP §7c table |
| Step 8: manual live gate | **Pending.** It needs E1 + E2 and a deploy (§8) |

## 2. Census (step 1)

Method: Grep over `src/client` and `src/solutions` for `createRecord(` and `updateRecord(` with a child table or a variable
entity, for raw `fetch` POST/PATCH to `/api/data/`, and for PCF sources (not the built `bundle.js` files). Each hit was
read in context. **Child tables** are the lineage tables of `SecureChildLineage` (the codified Secure Record Owner role
set). "BFF client" means the host already holds an authenticated BFF fetch (`authenticatedFetch` + `bffBaseUrl`, or
`@spaarke/auth`), so a switch needs no new auth plumbing.

### 2a. Creates

| # | Writer (file:line) | Table | Parents it can name | Host | BFF client | Proposed |
|---|---|---|---|---|---|---|
| W1 | `Spaarke.UI.Components/src/components/CreateTodoWizard/todoService.ts:281` | sprk_todo | the host record: any `TODO_REGARDING_CATALOG` type (project, matter, work assignment, event, invoice, communication, analysis, document, …) | Create To Do wizard (code page `src/solutions/CreateTodoWizard`, SpaarkeAi, LegalWorkspace) | yes (field mapping already uses it) | A |
| W2 | `…/CreateEventWizard/eventService.ts:411` | sprk_event | host record | Create Event wizard | yes | A |
| W3 | `…/CreateWorkAssignmentWizard/workAssignmentService.ts:671` | sprk_event (follow-on) | the new work assignment | Create Work Assignment wizard | yes | A. The work assignment itself (`:527`) is a ROOT and task 158's |
| W4 | `…/CreateInvoiceWizard/invoiceService.ts:297` | sprk_invoice | matter / project (host) | Create Invoice wizard | yes | A |
| W5 | `…/CreateReportCardWizard/reportCardService.ts:255` | sprk_reportcard | matter / project (host) | Create Report Card wizard | yes | A |
| W6 | `Spaarke.AI.Widgets/src/widgets/workspace/CreateAnalysisWizardWidget.tsx:918` | sprk_analysis | host record | Create Analysis widget (SpaarkeAi, LegalWorkspace) | yes | A. The chained communication (`:955`) is sent through the BFF (a server writer, task 146): `:955` only feeds field mapping and creates nothing |
| W7 | `Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts:318` | sprk_todo | the briefing item's record | Daily Briefing | yes | A |
| W8 | `Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx:806` | sprk_todo | optional regarding | Smart To Do widget | yes (workspace) | A |
| W9 | `src/solutions/SmartTodo/src/components/SmartToDo.tsx:760` | sprk_todo | optional regarding | SmartTodo code page | yes | A |
| W10 | `src/solutions/SmartTodo/src/services/DataverseService.ts:506` | sprk_todo | regarding fields in the record | SmartTodo code page | yes | A |
| W11 | `src/solutions/LegalWorkspace/src/services/DataverseService.ts:521` | sprk_todo | regarding fields in the record | LegalWorkspace | yes | A |
| W12 | `src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts:446` | sprk_memo | matter, project, event, invoice, budget, work assignment (`SUPPORTED_MEMO_PARENTS`) | Notepad side pane | needs `@spaarke/auth` wiring (Xrm only today) | A |
| W13 | `src/solutions/EventDetailSidePane/src/App.tsx:623` | sprk_memo | the event | Event side pane | needs wiring | A |
| W14 | `src/solutions/EventDetailSidePane/src/App.tsx:672` | sprk_todo | the event | Event side pane | needs wiring | A |
| **W15 new** | `src/solutions/EventDetailSidePane/src/hooks/useRelatedRecord.ts:149` (callers `components/TodoSection.tsx:245`, `components/MemoSection.tsx:143`) | sprk_todo, sprk_memo | the event | Event side pane | needs wiring | A |
| W16 | `Spaarke.UI.Components/src/services/EntityCreationService.ts:752` | sprk_document | the parent bind plus `additionalBinds` (dual-bind) | every wizard's file step | yes | A, or the existing upload pipeline (§3) |
| W17 | `Spaarke.UI.Components/src/services/document-upload/DocumentRecordService.ts:200`, `:261` | sprk_document | the parent record; none for an unfiled save | DocumentUploadWizard code page (`codePageDataverseClient`), PCF hosts (`PcfDataverseClient`), OData client | code page yes; PCF host per control | A. An unfiled save stays as it is (constraint) |
| (seam) | `EntityCreationService.ts:619` `createEntityRecord(entityName, …)` | whatever its caller names | — | — | — | Not a writer of its own. Its callers name roots (matter/project). Any child-table caller is listed by its own row |

### 2b. Re-files (a child moved under, or out of, a record)

| # | Writer (file:line) | Table | What it changes | Host | Proposed |
|---|---|---|---|---|---|
| R1 | `src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts:412` (set), `:532` (clear) | the host child (to-do, event, communication, analysis, …) | regarding pair, typed lookups, FR-26 stamps | PCF on model-driven forms | **Saved host:** A (BFF re-file). **New host:** the payload is staged into the FORM's save, which is an out-of-the-box write, so only the L4 net reaches it (E2) |
| R2 | `Spaarke.Communication.Components/src/logic/connections/ConnectionsWriteHandler.ts:244`, `:301`, `:357`, `:379` | sprk_communication | regarding links (set / unlink) | Communication form (PCF) | A. Overlaps task 161 (communications routes) |
| **R3 new** | TodoDetail save → `buildTodoRegardingUpdate` payload → `src/solutions/SmartTodo/src/services/DataverseService.ts:528`, `src/solutions/LegalWorkspace/src/services/DataverseService.ts:543` | sprk_todo | regarding (re-file) | SmartTodo, LegalWorkspace | A |
| **R4 new** | `src/solutions/EventDetailSidePane/src/services/eventService.ts:387` | sprk_event | lookup payload (any `@odata.bind` the pane edits) | Event side pane | A. Overlaps task 159 (events API) |
| **R5 new** | `src/solutions/SpaarkeAi/src/components/compose/documentAssociationWrite.ts:173` | sprk_document | the document's record link | Compose (SpaarkeAi) | A through the EXISTING `PUT /api/v1/documents/{id}`, which already runs `ReparentAsync` with an as-caller AppendTo check (task 146 r2). No new route |
| **R6 new** | `Spaarke.UI.Components/src/components/CreateMatterWizard/CreateMatterWizard.tsx:157` | sprk_invoice | re-files the SELECTED invoice under the new matter | Create Matter wizard | A |

**Out of scope, checked:** updates that change no parent: SmartTodo status, pin and score updates; EventsPage status;
SemanticSearch workspace flag and document type; `sprk_DocumentOperations.js:523` status; `filePreviewService.ts:55`
flag; `useEmailWorkspaceRecord.ts:102` booleans; Notepad body and name (`useSprkMemoRepository.ts:490`, `:573`);
`agreementTypeLookupWrite.ts:111`; user preferences and nav items. Roots are task 144's and 158's: `matterService`,
`projectService`, `workAssignmentService.ts:527`, `LegalWorkspace CreateMatter/matterService.ts:222`.

**Transport adapters** carry whatever their caller writes, so they are listed, not decided: `xrmDataServiceAdapter.ts:63`,
`solutions/CreateTodoWizard/src/main.tsx:55`, `solutions/DocumentUploadWizard/src/services/codePageDataverseClient.ts:61`,
`document-upload/PcfDataverseClient.ts:31`, `document-upload/ODataDataverseClient.ts:75`,
`pcf/SemanticSearchControl/…/SemanticSearchControl.tsx:851`, `code-pages/DocumentRelationshipViewer/src/App.tsx:473`, and
the wizard-local adapters (`todoService.ts:116`, `TodoWizardDialog.tsx:265`/`:333`, `CreateEventWizard.tsx:241`,
`CreateInvoiceWizard.tsx:170`, `CreateReportCardWizard.tsx:172`, `CreateAnalysisWizardWidget.tsx:388`,
`matterService.ts:144`, `workAssignmentService.ts:147`, `invoiceService.ts:153`, `CreateMatterWizard.tsx:688`,
`CreateProjectWizard.tsx:328`). The switch to the BFF lands in these adapters (the `IDataService` seam, field-mapping-r1
W3's plan), not in each wizard.

**Non-product writes** need no code change and can be reached only by the L4 net: out-of-the-box forms, quick create,
editable grids, Excel import, Data Import and customer flows. They are §5's subject and E2's.

## 3. Per-writer decision (step 3), proposed and NOT final

**Every writer is proposed for (A): route the write through a BFF path that applies the task-146 owner rule inline.**
None is proposed for (B), the client create followed by a fix-up call. The one exception is R1 on a NEW host record,
where the form's own save is out-of-the-box, so only the L4 net can reach it (E2). The reasons:

- **Every create host already reaches the BFF** except the event side pane and Notepad, and both are code pages, so
  `@spaarke/auth` can be wired in. (B) would leave the row user-owned for a round trip, and that is the window trigger
  1 makes an owner decision. (A) has no window under A1 or A2, and a one-request window under A3 (E1).
- **The switch belongs in one seam.** The wizards write through `IDataService`, so a BFF-backed adapter for the child
  tables moves W1-W6 and W16 at once. Field-mapping-r1's W3 plans exactly that ("migrate one wizard at a time behind the
  shared `IDataService` seam"), and this task must not fork that plan (constraint "never two create endpoints for one
  table").
- **Re-files:** the pattern is settled. `ReparentAsync`, after an as-caller check, is task 146's, and owner round 13 item
  7 folded the update tool's re-file into path B. A child moved OUT of a secure record is an unsecure, so F3 applies
  (owner round 10 item 7: Full Access holders plus the creator). R5 can reuse `PUT /api/v1/documents/{id}`. The others
  need a re-file route per table, which overlaps task 159 (events) and task 161 (communications), both of which own those
  route families.

**Why no writer was switched in this task:** every (A) route above needs one decision first: **who creates the row?**
That is E1. The sibling project decided "app-only" for its pipeline (field-mapping-r1 Q2). Applied to a client child
create, that makes `createdby` the application, which is trigger 3. The alternatives keep `createdby` as the human but
need either an Assign the platform may refuse (A2), or a one-request user-owned window (A3, a trigger 1 window). Both are
owner calls and cross-project calls, so a choice made here and built here would be the "second create endpoint" the
constraint forbids, or would fork the sibling's pipeline.

## 4. `sprk_memo` in the CHILD taxonomy, and the stamp backfill (step 2)

- **Changed:** `CoreAncestorResolver.ChildRecordEntities` (C#), `CHILD_RECORD_ENTITIES` (`PolymorphicResolverService.ts`),
  and `$ChildEntities` plus the `-Entities` ValidateSet and default in `scripts/Backfill-CoreAncestorStamps.ps1`. Pinned
  by the existing `Taxonomy_MatchesTheTypeScriptSide` and `ChildRecordEntities_ArePinnedLiterally`, the jest
  `pinsTheChildRecordSetLiterally`, and the NEW `Taxonomy_MatchesTheStampBackfillScript` (the script said it "MUST mirror"
  the resolvers, and nothing checked it). New behaviour tests: `MemoTarget_DerivesTheMemosOwnProjectAncestor` (C#) and
  `derivesTheProjectAncestorFromAMemoTarget` (jest).
- **What it changes:** a record filed UNDER a memo now derives the memo's own `sprk_regarding{core}` stamp. Before, it read
  as `Unclassified`. Live metadata, read-only, 2026-10-04: `sprk_memo` carries all four core-ancestor lookups. No client
  picker offers a memo as a regarding target today (`TODO_REGARDING_CATALOG` has none), and no BFF caller resolves a
  container for a memo (`DocumentAssociationMap` has no memo; the task 155 guard stays green). So the change is the
  taxonomy the owner asked for, with no behaviour change on a shipped path. A memo filed under an EVENT still takes the
  event's stamp as before, because derivation keys on the TARGET's class. Its ownership is §5's
  (`AMemoRegardingAnEventOfASecureProject_IsReownedThroughTheEvent`).
- **Coordination:** task 169's POML says `CHILD_RECORD_ENTITIES` must be "byte-for-byte unchanged" by ITS diff, and treats
  "matching it would require changing the taxonomy" as a stop. This task changes the taxonomy under an owner decision
  (round 2 item 6). If 169 integrates after 147, its base includes `sprk_memo` and its criterion holds against that base.
  The main session should know the order.
- **Backfill dry run (read-only, GET only).** First run: `[400] Could not find a property named 'sprk_regardingproject'`,
  for `sprk_memo` **and for `sprk_todo`**. The script selected and filtered lookups by their logical name, and the Web
  API needs `_<attr>_value`. **The script has never got past its candidate query on any table.** Its `-Apply` also bound
  stamps through the logical name (`sprk_regardingproject@odata.bind`), which the Web API refuses: it needs the
  case-sensitive navigation property. Both are fixed: `_<attr>_value` in the select and filter (candidate and target
  reads), and the navigation property read from `ManyToOneRelationships.ReferencingEntityNavigationPropertyName`.
  **Re-run, all seven tables (dev, 2026-10-04):** memo **0 candidates** (19 memos in dev; none is filed under a child
  with an unstamped ancestor); todo 15 candidates / 2 to write / 2 unresolvable / 11 no-ancestor; communication 7 / 1 /
  1 correct / 2 unresolvable; event 3 / 1 / 2 correct; analysis 775 no-ancestor (filed under documents, the known
  structural gap); invoice and document skipped (no stamp columns). **Total 4 to write.** The run's own escalation gate
  fired: communication has 28.6% unresolvable, over 20%. Log:
  `scratchpad/t147/all-backfill-dryrun.log`. The memo dry run writes nothing, so its `-Apply` is a no-op today (§8 G147-3).

## 5. The L4 net: the recent-changes pass (step 5)

Task 148's `SecureChildReconciliationJob` now starts every run with a **recent-changes pass**:

1. **List.** For every lineage child table (all 23 in `SecureChildLineage`, not only the 8 the client writers touch,
   because a non-product write can land in any of them), the rows with `modifiedon >= watermark` that are NOT owned by
   the Secure Record owner team. Each row is read WITH its lineage lookups and owner, so the walk needs no second read
   of the row (code review fix). Only those rows can need a move INTO isolation. An isolated row's shares are task 149's
   two-minute job, and that job also reports one that has left every secure record (`outsideSecureRoots`). The listing is
   paged, and past the ceiling it fails rather than reconcile part of a window.
2. **Find the records above them.** `SecureChildShareSynchronizer.SecureRootsAboveAsync`, new, is the synchronizer's
   EXISTING upward walk (`LineageOfAsync`, exposed through `Run.LineageOfRowAsync`). It goes through Secure-team-owned
   and user-owned rows, never through an ordinary team's, at most 6 levels. This is the resolver's own rule, so a row is
   placed under a record exactly when the resolver would give it the Secure team. No second walk was written.
3. **Reconcile those records first**, with the same `SecureChildReconciler.ReconcileAsync` and the `Sweep` trigger, so the
   pass never releases an isolated child (round 24 item 2). Then the capped sweep window runs as before, and a record
   already done in step 3 is skipped there.
4. **Watermark** = this run's start minus 1 minute (clock skew and in-flight commits). It is kept in the job singleton
   per instance, like task 148's cursor (ADR-052 §5: never the scheduler's store). After a restart it looks back 60
   minutes, and an older change is reached by the full sweep. **It moves only past a listing that completed, in a run
   that wrote.** A report-only run corrects nothing, so it leaves the watermark where it was (code review fix). The
   first run with writes on therefore still sees every change the report-only runs listed.
5. **Report** (`ResultJson.recentChanges`): `since`, `rowsChanged`, `rootsFound`, `roots`, `undetermined` (rows the walk
   could not place: under a record flagged secure but not isolated, under a missing ancestor, or too deep) and `failure`.
   Either of the last two makes the run unsuccessful, and the `ErrorMessage` names the rows. ADR-003: never a silent skip.

"Single-row fix-up entry point": the two calls above (`SecureRootsAboveAsync` for one row the caller has read with its
lineage lookups, then `ReconcileAsync` per record). No HTTP fix-up route was built, because (B) is not proposed for any writer (§3). If the owner chooses (B) for a
writer, its route is these two calls behind an as-caller check that the caller can write the row.

**Cadence and enablement: NOT changed (E2).** The registration is still `enabled: false`, report-only, with
`DefaultCronSchedule = "*/15 * * * *"`. The exact change, once the owner accepts an interval:
- `ExternalAccessModule.cs`: `AddScheduledJob<SecureChildReconciliationJob>(SecureChildReconciliationJob.DefaultCronSchedule, enabled: true)`
- `SecureChildReconciliationJob.DefaultCronSchedule = "*/2 * * * *"` (proposed: the same 2 minutes the owner accepted
  for 149's share window, round 11 item 2)
- App setting `SecureChild__Reconciliation__WritesEnabled=true` (deployed configuration; the main session's step).

## 6. Escalations: two triggers fired, unanswered in owner rounds 1-13

### E1. Who creates a client-initiated child through the BFF? (triggers 1, 2 and 3; CLAUDE.md §6 / §6.5)

🔔 **Human Input Required**

- **Situation.** Every writer in §2 is proposed for (A). Each (A) route needs an identity model for the create. Owner
  rounds 3b, 7, 13 item 9 and 25 item 2 settled **G5 (check as the user, create as the app, record the person)** for
  BFF-ORIGINATED creates. Field-mapping-r1 (Q2) adopted the same for its W3/W4 pipeline. **None of the rounds addresses
  a create the user makes in the browser today**, where `createdby` is the human, the record is user-owned, and Dataverse
  enforces the user's own privileges and field security. The sibling has no W3/W4 endpoint yet: its branch is
  design-only (`a91c36c02`), and it was messaged 2026-10-04 (§9).
- **Options:**
  - **A1. App-only G5** (field-mapping-r1's Q2 shape, round 7 item 3 pattern). As the caller: Create on the table and
    AppendTo on every named parent. Then an app-only create owned by the resolver's team, with `sprk_createdbyperson` =
    the caller.
    - `createdby` becomes the app for EVERY client-created child. That is trigger 3: Daily Briefing / task 152 counts
      Created By only for human systemusers and would have to read `sprk_createdbyperson`.
    - Non-secure children become business-unit-team-owned. That is I-6, but it changes today's behaviour for them.
    - An app-only create with a client-supplied payload bypasses field-level security. It needs a server column policy:
      refuse field-secured columns, `ownerid`, `sprk_createdbyperson` and the like. Otherwise it is a write path past the
      round-21 FLS locks (`sprk_graphdriveid`, `sprk_issecure`).
  - **A2. Impersonated create with the owner set** (`DataverseImpersonation`, `MSCRMCallerID` = caller,
    `ownerid` = the resolver's team). It is atomic, `createdby` stays the human, and Dataverse enforces the caller's
    privileges and FLS.
    - **Live read (2026-10-04, GET only):** Spaarke Core User holds **Assign at Deep** on todo, event, memo, document,
      invoice, analysis and communication. It holds **nothing on sprk_reportcard**: report cards come from Basic User,
      AppendTo only.
    - Deep reaches the caller's own business unit and its children. The Secure Record business unit is a SIBLING of the
      customer business unit in production (round 5), so Dataverse will very likely refuse a create owned by its team.
      That needs a write probe (G147-1). If it refuses, A2 is out without widening roles, and widening roles is forbidden.
  - **A3. Impersonated create, then an inline app-only Assign** (the round 9 write pattern: the user's write as the user,
    then an app write only where the invariant needs it). The create is impersonated and user-owned. In the same request:
    resolve the owner, Assign to the Secure team if secure, read it back, then mirror the shares (`SyncRootAsync`). If the
    Assign fails, the app deletes the row and the user sees the server's refusal; if the delete also fails, the
    recent-changes pass reports and fixes the row.
    - `createdby` stays the human, and Dataverse enforces the user's privileges and FLS, so no column policy is needed.
    - **The cost: a user-owned window of one request** (milliseconds), before the id is returned. That is a trigger-1
      window, however short.
    - It diverges from the sibling's app-only pipeline (trigger 2).
- **Recommendation: A3.** It keeps every surface that reads `createdby` (briefing, notifications, audit) and today's
  privilege and FLS enforcement unchanged. It needs no hand-kept column policy on a generic create. Its only exposure is
  a window measured in milliseconds that the client never observes, with the L4 net behind it.
  - Sub-choice for non-secure children: keep them user-owned, which is today's behaviour and the task's constraint. Or
    apply I-6 and Assign them to the business-unit team. Recommend keeping them user-owned now, and moving to I-6 when
    field-mapping-r1 lands.
  - Ask field-mapping-r1 to adopt A3's identity for CLIENT-initiated creates in its pipeline. Then there is ONE create
    endpoint per table (its `RecordCreationService`), with the I-2 owner step in its slot.
- **Alternatives considered:**
  - A1 is the strongest consistency argument. It is the right answer if the owner prefers one create identity
    everywhere and accepts trigger 3 (the 152 change) plus a server column policy.
  - (B), client create plus a fix-up call, was rejected: its window is a full client round trip.
  - Reconciliation only was rejected, because that is E2's window for every product write.
- **Impact if A3 is accepted:**
  - One BFF child-create path, built in field-mapping-r1's `RecordCreationService` shape or by that project.
  - BFF re-file routes for todo, event and communication, coordinated with 159 and 161. R5 reuses the existing
    `PUT /api/v1/documents/{id}`.
  - A BFF-backed `IDataService` adapter for the child tables.
  - `@spaarke/auth` wired into Notepad and the event side pane.
  - The UI shows the server's refusal through each wizard's existing MessageBar.
  - A PCF version bump for RegardingResolver (R1) and for the Communication PCFs that bundle ConnectionsWriteHandler (R2).

### E2. The L4 interval for writes outside the product (trigger 4; CLAUDE.md §6)

🔔 **Human Input Required**

- **Situation.** A child created through an out-of-the-box form, quick create, grid, import or flow under a secure
  record is owned by its creator, in the creator's business unit, until the job's recent-changes pass corrects it. Until
  E1's routing ships, the same holds for every client writer in §2. For that interval, anyone in the creator's business
  unit with ordinary depth can read it.
  - Owner round 11 item 2 accepted **2 minutes for SHARES** of new and re-filed children (149). It does not cover this
    **ownership** exposure.
  - Round 3 R3/R4 ("the background job is only a safety net, running at ≤5 min") was about access grants.
- **Options:**
  - (a) Enable the job at **2 minutes** with writes on. Exposure is at most about 2 minutes plus the run time, per table,
    for every non-product child of a secure record.
  - (b) **5 minutes**, which is lighter on Dataverse.
  - (c) (a) plus **removing Create on the child tables from the model-driven app for secure parents** (form and ribbon
    rules), so the non-product path is closed rather than corrected. Imports and flows stay open.
- **Recommendation: (a)**, and record (c) as a hardening item. The pass costs one filtered query per child table per run,
  plus one record pass per CHANGED secure record (rows already isolated are never listed). At dev scale (one secure
  record) that is well inside an in-BFF ADR-036 job. Trigger 5 (ADR-052 placement) does not fire at this volume.
- **Until decided:** the job stays disabled and report-only, exactly as task 148 shipped it.

### Triggers checked and not fired

- Trigger 2 ("field-mapping-r1 is building the same table's endpoint concurrently"): **not fired**. Its branch is
  design-only. It is folded into E1 as the coordination ask.
- Trigger 5 (ADR-052 placement): **not fired** (E2 above).

## 7. Tests (step 6) and seed-and-bite

**New: `tests/integration/data-mutation/RecordOwnership/ClientChildFixUpTests.cs` (7).** These drive the REAL job,
reconciler, resolver and synchronizer over the evaluating in-memory Dataverse (`SecureChildShareWorld`, which now
evaluates `GreaterEqual` on `modifiedon` and offers `Modified(...)` / `ClearQueryFaults()`):

1. A non-product to-do under a secure record is re-owned and mirrored in the next run, OUTSIDE the sweep window (cap 1).
   Decoys are never written: an ordinary record's user-owned to-do and an unfiled document.
2. A memo regarding an event of a secure project is re-owned through the event.
3. Report-only plans the change and writes nothing (owners or shares). A second report-only run still lists the row (the
   watermark did not move), and the first run with writes on corrects it.
4. The watermark advances past a completed run, isolated rows are never listed, and an unmodified row is never listed.
5. A listing that fails is reported, the run is not a success, the watermark does not move, and the next run fixes the
   child.
6. A changed child under a flagged-not-isolated record (outside the sweep window) is reported `undetermined`, fails the
   run, and is not written.
7. A changed child whose re-own Dataverse refuses is reported Failed, and the run is not a success.

**Unit and jest:** `Taxonomy_MatchesTheStampBackfillScript`, `MemoTarget_DerivesTheMemosOwnProjectAncestor`, the
pinned-literal update, and jest `derivesTheProjectAncestorFromAMemoTarget`. The existing `SweepHarness` registers the
synchronizer.

**Seeds** (`scratchpad/t147/seeds.py`). Each is one replacement in production code or the script, then build, run the
affected classes, restore the file's bytes and touch it. `git diff --stat` was unchanged after the batch.

| Seed | Mutation | Failed |
|---|---|---|
| S1 | recent roots never reconciled | 5 |
| S2 | watermark advances past a failed listing | 1 (test 5) |
| S3 | isolated rows listed too | 1 (test 4) |
| S4 | undetermined rows do not fail the run | first run 0: test 6 originally sat inside the sweep window, so the sweep failed it anyway. Test fixed (flagged record outside the window, cap 1); re-seeded: 1 |
| S5 | no `modifiedon` filter | 2 (test 4 + task 148's capped-sweep test) |
| S6 | watermark never set | first run 0: test 4's only listed row had become isolated. Test fixed (a user-owned row of an ordinary record that stays listable); re-seeded: 1 |
| S7 | script taxonomy drops memo | 1 (`Taxonomy_MatchesTheStampBackfillScript`) |
| S8 | TS taxonomy drops memo | 1 (`Taxonomy_MatchesTheTypeScriptSide`) |
| S9 | a report-only run moves the watermark | 1 (test 3) |

All nine were re-run on the FINAL code after the code-review fixes (`scratchpad/t147/seeds-final.out`), and every one
bites. The jest pin was also checked: the TS drop fails `pinsTheChildRecordSetLiterally`.

## 7a. Quality gates (Step 9.5)

**Code review.** Two findings were fixed in place:
- (W) A report-only run moved the watermark, so switching writes on would have skipped the changes it had only planned
  (now gated on `writes`; seed S9).
- (perf) The walk re-read every changed row (the listing now carries the lineage lookups).

Open, for the record:
- (S) The watermark is per instance. Another instance's watermark lags, so it re-lists rows, which is harmless.
- (S) Dataverse-vs-instance clock skew beyond the 1-minute overlap could miss a row until the sweep reaches it.
- (S) The cost of the pass: one query per child table, plus a walk over the CHANGED non-isolated rows' parents (cached
  per run). In an environment with heavy ordinary editing, that walk is the cost to watch (E2).
- The job gained a second, cohesive responsibility: which records to reconcile, now "recently changed" as well as
  "next in order". No decomposition is warranted (COMPONENT-COMPLEXITY).

**ADR check.** ADR-002: no plugin. ADR-003: fail closed, never a silent skip. ADR-010: no interface, no DI line.
ADR-036 A1: rules 3-5 hold; a failed recent listing does not throw, because the sweep can still decide its window.
ADR-052 §5: no scheduler-store dependency; WorkloadPlacementGuardTests is green in the ArchTests run. ADR-038: no
`Mock<HttpMessageHandler>`; the Web API double is the class proxy the provisioning fixture already uses. ADR-028: no new
route or credential. ADR-034: the one-hop derivation is unchanged. A grep of the server diff for
`IMemoryCache|Microsoft.Graph|interface I|BackgroundService|Add(Singleton|Scoped)|WithClientSecret|IOpenAiClient|IPlaybookService`
found nothing. No violations.

**Results (final code, 2026-10-04, run once at the end, no contention failures):**
- Full BFF unit suite: **14,845 = 14,791 passed + 54 skipped (pre-existing), 0 failed**.
- ArchTests: **373/373**.
- `Sprk.Bff.Api.IntegrationTests`: **104/104**.
- `Spe.Integration.Tests`: **428 = 403 passed + 25 skipped, 0 failed**.
- CVE: `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` finds no vulnerable packages.
- `dotnet format whitespace --verify-no-changes` on the changed BFF files is clean.
- `@spaarke/ui-components`:
  - `npm run build` (tsc) is green, once the sibling `file:` packages `@spaarke/auth` and `@spaarke/sdap-client` are
    built locally. Without them, the build fails on 9 environmental TS2307 errors.
  - jest `PolymorphicResolverService*`: 56/56.
  - Full jest: 3,424 passed, 14 failed in 9 suites. The SAME 8 suites and 13 tests fail with the baseline taxonomy
    restored; `templatePicker` is the flaky ninth. These failures are pre-existing and unrelated (communication timeline,
    conversation view, file preview, record header, workspace shell, surface launch registry, to-do score mappings).
- No PCF source changed, so no version bump. Publish-size measurement was skipped, per the brief.

**Not tested, with the reason:** the per-writer client switch and refusal UI, and the re-file routes. They are not
built (E1).

## 8. Manual live gates (main session; round 11 approved dev live steps, each dry run → apply → verify)

- **G147-1 (E1 input, a WRITE probe, on TEST records only).** As `uac.child.user@demo.spaarke.com` (Spaarke Business
  Unit 1, Core User): try an impersonated create of a `sprk_todo` with
  `ownerid@odata.bind=/teams(<Secure Record Owners id>)` and `sprk_RegardingProject@odata.bind` = a test project the user
  can AppendTo. Record whether Dataverse accepts it (A2 feasible) or refuses it (A2 out). Delete the probe and record the
  deletion.
- **G147-2 (after E2; plus BFF deploy).**
  - Set `SecureChild__Reconciliation__WritesEnabled=true` and enable the schedule (§5 diff).
  - With quick create in the model-driven app, as a non-admin test user shared on secure project `65a3fab2`, create a
    to-do under it.
  - Record its owner before and after the next run, and the run's `recentChanges`.
  - Confirm a non-sharee test user cannot open it afterwards.
  - Delete the probe.
- **G147-3 (stamp backfill, memo).** Run
  `pwsh scripts/Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Entities sprk_memo`
  (dry run: expect 0 to write), then `-Apply` (a no-op today). The other four stamps the all-table dry run found are
  outside this task. Decide them with the escalation gate the script raises (communication 28.6% unresolvable).
- **G147-4 (step 8, after E1's build).** The full per-writer live sequence in the POML step 8.

## 9. Coordination sent

- **field-mapping-server-write-path-r1** (session `spaarke-wt-field-mapping-server-write-pa-de`), messaged 2026-10-04 with
  the state and options and two questions: a date for W3/W4, and whether `RecordCreationService` can host the I-2 step.
  Replies go to the UAC-r2 main session.
- **Task 152** (briefing / Created By): if E1 = A1, it must read `sprk_createdbyperson` for app-created children.
- **Tasks 159 / 161**: the re-file routes for events and communications (R2, R4) belong in their route families.
- **Task 169**: the taxonomy order (§4).

## 10. Placement (CLAUDE.md §10) and justification (§11)

**Placement: BFF**, inside the existing task-148 job and task-149 synchronizer (ADR-052 "BFF, schedule"; ADR-036
`IScheduledJob`). No new service, DI registration, endpoint, option, job, column, package or plugin.
`bff-extensions.md` criteria: BFF identity, BFF domain code, low volume. No AI code is touched (ADR-013 n/a).

| New surface | Existing (grep) | Extension | Cost of doing nothing |
|---|---|---|---|
| `SecureChildShareSynchronizer.SecureRootsAboveAsync` + `Run.LineageOfRowAsync` (methods on the existing class) + `SecureRootsAbove` record | the synchronizer's private `LineageOfAsync`, the ONE upward walk with the resolver's follow rule. Grep `SecureRootsAbove`: none before | It exposes the existing walk for rows of any owner. A second walk in the job or the reconciler would be a second copy of the follow rule | the job could reach a non-product child only when its capped sweep window comes round to that child's record: ≥ ceil(secure records / 50) runs |
| The recent-changes pass in `SecureChildReconciliationJob` (a method, a watermark field, two constants, additive `ResultJson.recentChanges`) | task 148's sweep (capped, ordered); 149's share job (Secure-owned rows only) | It extends the ONE job for this invariant (constraint: "no new job class for the same invariant"). Constants rather than options: the lookback and overlap are correctness margins, not tuning | as above, plus a failed or partial window would be silently lost |
| `Taxonomy_MatchesTheStampBackfillScript` guard | the TS parity test pinned two copies of three | it extends the existing test class | the script drifts again; it had been unable to run since it was written |
| Backfill script fixes (`_<attr>_value`, navigation-property bind) | — | bug fixes in place | the stamp backfill (task 053's companion) can never run |

## 11. `.claude/**` edits needed

None.
