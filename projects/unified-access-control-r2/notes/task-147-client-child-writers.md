# Task 147: children created or re-filed in the browser, and outside the product (C10 part 2, client; #1069)

> **Run r1c**: 2026-10-04 · **Branch**: `task/uac-r2-147-r1c` (from `wip/uac-r2-147-r1-restart` @ `0ab6411e1`, the r1 run
> stopped by a machine restart mid-round) · **Rigor**: FULL (opus, high)
> **Base**: the batch-4 integration tip — r1 merged `origin/integ/uac-r2-batch4` @ `b8a1374c2` and the local tip `f54b5b29b`;
> r1c merged `origin/integ/uac-r2-batch4` @ `3aa4ebce6` (`220bce6de`, no conflicts).
> **Decisions applied**: owner/main-session rounds 1-35; round 28 (E1 = A1; E2 = BFF-backed commands for in-product native
> creates under a secure parent + the L4 recent pass every 2 minutes with writes on; 169 rebases onto 147), round 34 item 6
> (CreateEventWizard's `sprk_Event` bind), round 35 item 6 (147 Q2 = round 28 item 1 as written).
> **Status: COMPLETED in code.** Nothing deployed; every live action was a GET. Live writes are the manual gates in §8.

## 1. Outcome — this round's items 1-25

| # | Item | State |
|---|---|---|
| 1 | Finish the interrupted round | **Done.** The WIP commit was read file by file. Sound and kept: the routes, the client seam and the writers, the ribbon set, the job's carry-forward. **Found and fixed**: (a) a SEED LEFT IN PLACE — `ChildRecordEndpoints.UpdateAsync`'s server-owned-column refusal read `FirstOrDefault(c => false)` (the r1 seed "R2", never restored); (b) the event filing route's DI parameters were not `[FromServices]`, so a host without `IDataverseUserClient` failed to START — 11 `SecureChildOwnershipEndpointTests` were red on the WIP; (c) the CreateTodoWizard page's create broadcast was bypassed (§3b); (d) the RegardingResolver new-host check let a target with no derivable root ride the as-the-user save (§3b); (e) the E2 inventory covered only the three root forms (§3c). Every check re-run (§7) |
| 2 | Round 28 in full | **E1**: every census writer creates through `POST /api/v1/child-records/{table}` (G5) and re-files through the table's one route (child-records PATCH, events / communications filing, `PUT /api/v1/documents/{id}`); the client never sets an owner; refusals show the ProblemDetails message (r1c also on the SmartTodo three-field QuickAdd and the event side pane, which only logged); PCFs RegardingResolver **1.6.0** / CommunicationConnections **1.7.0** in all four places, `npm run build:prod`, bundles copied. Task 152's "Created By" matching reads `sprk_createdbyperson` else `createdby` (tests + seed P1). **E2**: §3c. **Round 34 item 6**: `CreateEventWizard` files event documents through `sprk_RelatedEvent` (`createEventDocumentRecords`, `EVENT_DOCUMENT_NAV_PROP`); the BFF create binds `sprk_relatedevent` and REFUSES `sprk_Event@odata.bind` (400) — tests both sides, seed C2 |
| 3 | `sprk_memo` taxonomy on the integration base | **Fixed (r1, `0107ce026`), re-verified**: `IntermediateRootColumns["sprk_memo"]` = the four stamp columns (what the TypeScript `CHILD_RECORD_ENTITIES` gate reads, so C# and TS agree for a memo target until 169 lands), `StampSourceColumns`, and `RecordContainerResolver.ChildAncestorLinks` (verified live links). `CoreAncestorResolverTests` (`EveryChildTaxonomyEntity_IsAnIntermediate`, `Memo_RootColumns_AreTheFourStampColumns`, `MemoTarget_DerivesTheMemosOwnProjectAncestor`) and the 156 lockstep test green; seeds M1 (3 fail) / M2 (1 fail) |
| 4 | Memo container resolution 409 | **Fixed** (item 3's links + `KindByEntity`): memo cases in `ChildRecordContainerResolutionTests` |
| 5 | AC1 overclaimed (R1 new host undecided) | **Decided and tightened**: a SAVED host re-files through the BFF; a NEW host's regarding rides the form's own (as-the-user) save ONLY under a party or a target whose every derived root reads not secure (or is a service request) — a secure root, an unreadable flag, and a target whose ancestry the control cannot see (document, invoice, budget, report card, an event filed only under a document: no stamp) are refused ("save it first, then file it"). A saved ownership-child host with no BFF route (a document) is refused. Live hosts (read-only): to-do, event, report card main forms. Tests + seeds C3/C4 |
| 6 | L4 reports once / no retries | **Fixed**: r1 carries incomplete records and unplaced rows to every next run; **r1c adds the CATCH-UP** — that carried state is in the job's memory, so a start (restart, deploy, scale-out) lost it; a new instance now walks every secure record once on its scheduled ticks (`MaxRootsPerRun` a run, advancing only in a run that wrote, never on a manual trigger, standing aside while the sweep writes). `ARefusedReownCarriedBeforeARestart_IsCorrectedByTheNewInstancesCatchUp` + 2 more; seeds J1/J2/J5/J6/J7 |
| 7 | Seed survived (synchronizer Failed) | **Fixed (r1)**: non-Completed → failure; `TheSynchronizerAnsweringFailed…`, `TheJobsOwnTeamCheckRefusing…`; seed J3 bites |
| 8 | Docs contradict code | **Rewritten** (r1 + r1c): DATAVERSE-WRITE-PATH-ARCHITECTURE §4.3 and I-2 (no "net catches them"; watermark condition incl. "in a run that wrote"; catch-up; the false "keeps its mirrored shares until the next reconcile tick" corrected — §3a), I-6; SECURE-PROJECT-ENVIRONMENT-SETUP §7c; `appsettings.template.json` comment (it still said "registered DISABLED") |
| 9 | Note inconsistencies | **Fixed**: the method is `Run.LineageOfReadRowAsync`; r0 ran nine seeds; r1c's seeds are §7 with their real results |
| 10 | E1 framing understated precedent | **Corrected** (§6): G5 applied to user-initiated creates in rounds 3b, 9 and 25 item 2 and the #1044 peer agreement; r0's A3 ran against it; round 28 chose A1 |
| 11 | Backfill dry-run: 4 writes + communication 28.6% need an owner | **Assigned and analysed** (§4): the 4 writes → task 147, gate **G147-3** (`-AcknowledgeEscalation` justified below). The two "unresolvable" communications were re-read live (GET) today: each sits under an analysis whose lineage has NO core ancestor at all — `8128d06b` under an UNFILED document, `206fed82` an anchorless analysis — so their correct stamp is EMPTY; nothing to write. The anchorless analysis is **task 162's** (owner round 15: classify, fix writers, backfill anchors); once anchored, task 156's stamp job stamps the communication. No owner-less residue |
| 12 | Integration docs conflict; 169 must rebase onto 147 | **Resolved** on the base (header); 169 hand-off §9 |
| 13 | (verified clean) | — |
| 14 AC1 | census, A or B per writer, none undecided | **Met**: §2 — every writer A; R1 new host decided (item 5); r1c census additions decided (KPI assessment, billing event, child-form subgrids, bundle matches, the email "Create" actions, the Assistant create-todo form) |
| 15 AC2 | child of a secure parent → Secure team, sharees mirrored | **Code + tests per table**: `ChildCreate_EveryCensusTable_UnderASecureMatter_…_AndMirrored` (all ten create tables), memo / budget / document cases; live G147-4 |
| 16 AC3 | re-file in → secure; out → BU team, mirror removed | **Code + tests**: `ChildRefile_EveryCensusTable_MovedUnder…` / `…MovedOutOf…LosesTheMirror` (five tables), event and communication filing, documents PUT (`DocumentPut_…SharesTheDocument…`, `…TakesTheMirroredSharesOff`), the chat update tool (`UpdateTool_…`); ONE after-re-file step, `SecureChildShareSynchronizer.AfterRefileAsync`; seeds R4-R7 |
| 17 AC4 | no client owner write; caller without AppendTo refused | **Met**: no `ownerid@odata.bind` in any changed package (grep, §7); uniform 404 per create table, per re-file table and on communication filing; seed R1 (20 fail) |
| 18 AC6 | memo under an event of a secure project → secure; memo CHILD both sides; parity | **Met**: the memo create route (named team + mirror) and the L4 memo-through-event case; taxonomy item 3; parity test green |
| 19 AC7 | A-route refusal creates nothing, message shown | **Met**: route tests assert nothing created on every refusal; every writer shows the server's message (`ChildRecordWriteError`); side pane + SmartTodo three-field fixed in r1c |
| 20 AC9 | routes delegated, parent-authorized, guarded, uniform 404 | **Met**: `/api/v1/child-records` group `RequireAuthorization`, AppendTo as the caller, `RouteAuthorizationGuardTests` lists the file, uniform-404 tests (missing vs unreadable row and parent) |
| 21 AC10 | schedule with writes on in the deployed template | **Met**: `*/2 * * * *`, registered ENABLED, `RecentChangesWritesEnabled: true` in the template; `TheJobShipsEnabled_EveryTwoMinutes`; seed J4 |
| 22 AC11 | builds, jest, publish size, CVE | **Met** (§7): BFF/ArchTests/both integration suites green; every changed client package builds (`build:prod` for PCFs); every jest suite this task touches passes; publish size measured per CLAUDE.md §10 (§7); CVE scan clean. **Pre-existing failures proven not this task's**: ui-components 8 suites / 13 tests, communication-components 2, daily-briefing 5 suites / 1 test, ai-widgets 1 — each fails identically on the integration base (`3aa4ebce6`) at a dot-free path, in files this task does not touch (§7) |
| 23 AC13 | per-writer secure / ordinary / refusal + re-parent | **Met**: three Theories over every create table, three over every re-file table, filing routes, documents PUT, chat tool; client jest per writer seam (§7) |
| 24 AC5 | tables in the codified set, granted live | Repo: every create/re-file table incl. `sprk_kpiassessment`, `sprk_billingevent` is in `config/secure-record-owner-role.json` (G146-1 applied 2026-10-03). Live grant: covered by G146-1; `sprk_createdbyperson` on the five new stamped tables is **G147-5** |
| 25 AC12 | manual live gate | Pending manual gates G147-2..G147-6 (§8) |

## 2. Census — r1c

Method: Grep over `src/client` and `src/solutions` (sources AND built PCF `bundle.js` files) for child-table `createRecord(` /
`updateRecord(`, raw Web API writes, `navigateTo` entity-record creates; a read-only live inventory of every active main
form's subgrids (spaarkedev1, 2026-10-04) and of the forms hosting the RegardingResolver.

### 2a. Creates → `POST /api/v1/child-records/{table}` (decision A for every row)

| # | Writer | Table | Switched through |
|---|---|---|---|
| W1 | `CreateTodoWizard/todoService.ts` | sprk_todo | `withBffChildWrites`; the CreateTodoWizard code page wraps the BFF-routed service in its create broadcast (r1c: the broadcast had been bypassed) and pre-fills the launch record |
| W2 | `CreateEventWizard/eventService.ts` (+ its file step `createEventDocumentRecords`) | sprk_event, sprk_document | `withBffChildWrites`; documents bind `sprk_RelatedEvent` (round 34 item 6) |
| W3 | `CreateWorkAssignmentWizard/workAssignmentService.ts` follow-on event | sprk_event | wrapped `_dataService` (the work assignment is a ROOT, task 158) |
| W4 | `CreateInvoiceWizard/invoiceService.ts` | sprk_invoice | wrapped `_dataService` |
| W5 | `CreateReportCardWizard/reportCardService.ts` | sprk_reportcard | wrapped |
| W6 | `CreateAnalysisWizardWidget.tsx` (+ follow-on to-do) | sprk_analysis, sprk_todo | `withBffChildWrites` |
| W7 | Daily Briefing `useInlineTodoCreate.ts` | sprk_todo | `createChildRecordViaBff` |
| W8 | `SmartTodoWidget.tsx` QuickAdd | sprk_todo | `createChildRecordViaBff`; offered only when the host wires the BFF |
| W9/W10 | SmartTodo `SmartToDo.tsx` (+ three-field QuickAdd) + `DataverseService.createTodo` | sprk_todo | `services/childRecordWrites.ts`; `ownerid` bind removed; refusal shown (r1c) |
| W11 | LegalWorkspace `DataverseService.createTodo` | sprk_todo | `createChildRecordViaBff`; `ownerid` bind removed |
| W12 | Notepad `useSprkMemoRepository.ts` | sprk_memo | `services/memoWrites.ts` |
| W13-W15 | EventDetailSidePane `App.tsx` memo / to-do, `useRelatedRecord.ts` | sprk_memo, sprk_todo | `services/childRecordWrites.ts`; refusal shown in the footer (r1c) |
| W16 | `EntityCreationService` (`createEntityRecord` child tables, `createDocumentRecords`) | child tables, sprk_document | `createChildRecordViaBff` |
| W17 | `DocumentRecordService` (DocumentUploadWizard) | sprk_document | `withBffChildCreates` (`uploadOrchestrator.ts`) |
| W18 | `createXrmEmailComposeHandlers.ts` → W16 (TrackingFieldTrio, unfiled document) | sprk_document | covered by W16 |
| W19 | secure-record ribbon "New Budget" | sprk_budget | create-then-open (E2) |
| **W20 (r1c)** | secure-record ribbon "New KPI Assessment" (matter, project, report card forms) + Spaarke's own "+ Add KPI" quick create | sprk_kpiassessment | create-then-open; "+ Add KPI" carries the secure rule and refuses itself unless the record reads not secure |
| **W21 (r1c)** | secure-record ribbon "New Billing Event" (invoice form) | sprk_billingevent | create-then-open |

### 2b. Re-files → the table's ONE route; after every re-file `AfterRefileAsync` (mirror in / off)

| # | Writer | Route |
|---|---|---|
| R1 | RegardingResolver PCF 1.6.0 (to-do, event, report card forms; a saved host set + clear) | child-records / events filing; a NEW host per item 5 |
| R2 | `ConnectionsWriteHandler.ts` set / unlink / clear-primary (CommunicationConnections 1.7.0, EmailWorkspace, reconciliation surfaces) | `PATCH /api/communications/{id}/filing`; refused when not wired |
| R3 | TodoDetail `buildTodoRegardingUpdate` | **Inert** — re-checked r1c: no production caller |
| R4 | EventDetailSidePane `eventService.ts` lookups | `PATCH /api/v1/events/{id}/filing` |
| R5 | SpaarkeAi Compose `documentAssociationWrite.ts` | `PUT /api/v1/documents/{id}` — r1c: that route now mirrors in / off inline too |
| R6 | `CreateMatterWizard.associateToRecord` invoice | `PATCH /api/v1/child-records/sprk_invoice/{id}` |
| **R7 (r1c, server)** | chat `dataverse.update_record` re-file | the shared `OwnedChildWrite.RefileAsync` already; r1c adds the inline mirror in / off (before: a move out left the old record's sharees on the row for good) |

### 2c. Classified, r1c (found by the bundle grep and the live inventory)

- **Five Communication PCF bundles** (CommunicationTimeline, …Regarding, CommunicationActions, CommunicationMessageActions,
  CommunicationConversationPanel) contain the wizards' `createRecord("sprk_…")` text: barrel inclusions from
  `@spaarke/ui-components`. Their sources import only the timeline / quick-view / theme / `readByRegarding` modules, none
  of which writes a child (verified) — unreachable code, no runtime writer; not rebuilt here.
- **"Create To Do / Create Event / Link Invoice" from an email** (`launchCreate`, owner UAT R3 C11-3) and the Assistant's
  **create-todo** (`oob-form`) open a BLANK model-driven create form (title / description only — no parent prefilled). Round
  28 replaces the parent-PREFILLED native creates; a blank form is a model-driven form save: on the to-do / event / report
  card forms the RegardingResolver refuses to file a NEW record under a secure (or undecidable) parent (item 5); a plain
  parent lookup (an invoice's matter) is a form save outside the product's write path — the recent-changes pass corrects it
  within one run (≤ 2 min + run) and lists it.
- **Summarize Files** creates its documents through the server's `POST /api/v1/documents` (task 146 server writer, unfiled).
- **Child subgrids on non-root forms** (analysis, budget, document, event: to-dos; document: analyses; invoice:
  communications, documents, events, to-dos, billing events) — E2 serves them (§3c).

**Out of scope, checked (no parent change):** the r0/r1 list, plus Notepad body/name, SmartTodo/LW status, pin and column
updates, event ribbon bulk status, document summary / flags / type (`DocumentRecordService.updateSummary`,
`SemanticSearchControl`, `filePreviewService.ts:55`, `sprk_DocumentOperations.js:523`).

**One create endpoint per table.** The browser's generic Web API payload has ONE route per table
(`POST /api/v1/child-records/{table}`). The typed `POST /api/v1/events` (Copilot / AI) and `POST /api/v1/documents`
(unfiled, Summarize Files) predate this task: task 146 server writers with their own contracts, on the same
`RecordOwnershipResolver`. field-mapping-server-write-path-r1's W3/W4 extend `child-records` / `OwnedChildWrite.CreateAsync`.

## 3. What was built

### 3a. Server (BFF)

- `Api/ChildRecordEndpoints.cs`: `POST /api/v1/child-records/{table}` (ten tables) and
  `PATCH /api/v1/child-records/{table}/{id}`, plus the shared `UpdateAsync` used by `PATCH /api/v1/events/{id}/filing` and
  `PATCH /api/communications/{id}/filing`. Payload mapped as the caller (`DataverseWriteItemMapper.MapWebApiPayloadAsync`:
  every `@odata.bind` resolved from metadata — an unknown navigation property is a 400), server-owned / creator-person /
  field-secured columns refused (403), `OwnedChildWrite.CreateAsync` / `RefileAsync`, restamp, mirror. Uniform 404.
  r1c: DI parameters `[FromServices]` on the three handlers.
- `SecureChildShareSynchronizer.AfterRefileAsync` (r1c): the ONE after-re-file step for the browser routes,
  `PUT /api/v1/documents/{id}` and the chat update tool — mirror in; off only for a row that WAS isolated (read before the
  write: `_owningteam_value` as the caller on the routes, `IsSecureTeamOwnedAsync` app-only on the documents route and the
  chat tool); a removal fault retried 3 times, then an ERROR naming the row (the two-minute share job looks only at
  Secure-team-owned rows, so it is no backstop for a row that left).
- `SecureChildReconciliationJob`: `*/2`, enabled, recent pass writes unless `RecentChangesWritesEnabled=false`, carry-forward
  (r1), non-Completed synchronizer = failure (r1), **catch-up** (r1c, §5).
- `RecordCreatorPerson.StampedChildTables` += `sprk_memo`, `sprk_reportcard`, `sprk_budget` (r1), `sprk_kpiassessment`,
  `sprk_billingevent` (r1c); `scripts/Set-ChildRecordCreatorPersonSchema.ps1` `$Tables` likewise (G147-5).
- Task 152 + reminder (r1): `MembershipResolverService` binds `sprk_createdbyperson`; `GrantExpiryReminderJob` granter →
  owner → creator person → createdby.

### 3b. Client

Seam `bffChildWriteAdapter.ts` (`createChildRecordViaBff`, `updateChildRecordViaBff`, `withBffChildWrites` — idempotent, a
decorator that INHERITS from a routed service stays in the call path; fail closed `child_record.bff_not_configured`;
`ChildRecordWriteError` with the ProblemDetails message; r1c `OWNERSHIP_CHILD_TABLES` pinned to the C# list),
`BffChildRecordDataverseClient.ts`, the writers of §2. RegardingResolver 1.6.0 (item 5). CreateTodoWizard page: the
broadcast decorator inherits from the BFF-routed service (r1c — before, `TodoService` re-wrapped the inner service and the
create went to the BFF past the broadcast, so the LegalWorkspace widget never refreshed) and the launch record pre-fills
the regarding. CreateEventWizard: `createEventDocumentRecords`.

"Who created it" readers: product logic and product displays read `sprk_createdbyperson` else `createdby` — people
targeting, the grant reminder, F3 (task 146), Notepad's "Created by", the document tabs' "my documents" filters. The
platform's own **Created By** column in a configurable view or on a model-driven form shows the record's `createdby` (the
application for a BFF create, by round 28's A1); a view that should show the person adds the "Created By (Person)" column.

### 3c. E2 — platform "+ New" under a secure record (round 28 item 2)

**Read-only live inventory (spaarkedev1, 2026-10-04)** of EVERY active main form (r1c; r1 inventoried only the three
roots): 28 subgrids of ownership-child tables — project (to-do, event, document, invoice, analysis, budget, KPI assessment),
matter (analysis, budget, communication, invoice, report card, KPI assessment), work assignment (document, event), report
card (KPI assessment), analysis / budget / document / event (to-do), document (analysis), invoice (communication, document,
event, to-do, billing event), contact and organization (to-do). Quick create: off on every served table (live). The
deploy script's dry run re-reads that inventory and FAILS on an unserved subgrid.

`sprk_secure_child_ribbon.js` (`nativeNewAllowed` / `newChildAvailable` / `newChild`): the platform "+ New" shows only for a
root read **through `Xrm.WebApi`** as not secure (r1c removed the form-attribute shortcut), or a party host (contact,
organization, account — r1c); every other host shows the BFF command (fail closed). Served: to-do, event, invoice, report
card (wizards, filed under the host via `entityType`/`entityId`/`recordName`), document (upload wizard), communication
(compose page), budget / KPI assessment / billing event (create-then-open). `sprk_analysis`: AnalysisRibbons. "+ Add KPI"
(`add-kpi-ribbon.xml`, not live today) carries the rule, the merge script adds it to that command when an export carries it,
and `Spaarke.KpiRibbon` refuses its quick create unless the record reads not secure. Files:
`infrastructure/dataverse/ribbon/SecureChildRibbons/` (template, merge script, README), `scripts/Deploy-SecureChildNewCommands.ps1`
(dry run / `-Apply` / `-Verify`; the dry run was executed read-only today: inventory all served, platform definitions match
the template on all nine tables, quick create off; FAILs only on task 142's two helper web resources not yet deployed — an
ordering prerequisite of G147-6). Behaviour pinned by `secureChildRibbonScript.test.ts` (the script in a sandbox, 21 tests;
it replaces r1's scratchpad harness). MDA Create privilege NOT removed (round 28).

## 4. `sprk_memo` taxonomy, and the stamp backfill (item 11)

Taxonomy: §1 item 3. **Backfill dry run (r0, read-only):** 4 stamps to write (to-do 2, communication 1, event 1), memo 0.
**Escalation gate (communication 2 of 7 = 28.6% "unresolvable") — analysed, read-only, 2026-10-04:** communication
`8428d06b…` → analysis `8128d06b…` → document `7e28d06b…`, which has no project / matter / work assignment (typed or related)
— an unfiled document; communication `a36784ef…` → analysis `206fed82…`, which has no document, no regarding and no stamp —
an anchorless analysis. Neither lineage holds a core ancestor, so their correct stamp is EMPTY; the script counts them
"unresolvable" only because it reads one hop. **Owners:** the 4 writes → task 147 gate G147-3 (`-Apply
-AcknowledgeEscalation`, citing this analysis); the anchorless analysis → task 162 (owner round 15: classify, fix the
writer, backfill the anchor), after which task 156's stamp job stamps it and the communication. Ownership is unaffected:
the L4 walk and the resolver follow lineage lookups, not stamps.

## 5. The L4 net (round 28 item 2)

- **Schedule / writes**: `*/2 * * * *`, enabled; the recent pass writes unless
  `SecureChild:Reconciliation:RecentChangesWritesEnabled=false`; the sweep keeps `WritesEnabled` (report-only by default)
  and a scheduled tick runs it only when it writes.
- **Recent changes**: child rows of every lineage table modified since the per-instance watermark and not Secure-owned →
  the secure records above them (`SecureRootsAboveAsync`) → reconciled first (Sweep trigger, never releases).
- **Carry-forward (r1)**: an incomplete record and an unplaced row are retried and reported every run until finished (≤
  1,000 rows; past that the watermark holds).
- **Catch-up (r1c)**: a new instance (restart, deploy, scale-out) walks every secure record once on scheduled ticks before
  its watermark alone decides — what an earlier instance carried is never lost with its memory. Never on a manual trigger
  (the backfill script's review run); stands aside while the sweep writes; advances only in a run that wrote. **Deploy
  order (G147-2)**: the task 148 backfill is applied and verified in an environment before a task 147 BFF reaches it, so the
  catch-up only ever finds drift.
- **Watermark**: moves only past a completed listing, in a run whose recent pass wrote, and not while carried rows overflow.
- **Standing correction report**: `ResultJson.changes[]` (`pass`: `recent` / `catch-up` / `sweep`) and
  `recentChanges.{mode, corrected, retriedRoots, retriedRows, carriedRoots, carriedRows, pendingOverflow, catchUp}`.

## 6. Escalations — history, resolved

- **E1** — r0 put A1/A2/A3 and recommended A3. **Correction (item 10):** G5 (check as the caller, create as the application
  owned by the team, record the person) had already been applied to user-initiated creates through BFF routes in owner
  rounds **3b** (the user confirming an invoice), **9** (the write pattern for every client-called route) and **25 item 2**
  (`POST /api/ai/analysis/create`), and in the **#1044 peer agreement** with field-mapping-server-write-path-r1 ("impersonated
  creates were rejected, because they would widen roles"); A3 ran against that agreement. **Round 28: E1 = A1** (round 35
  item 6 confirms: a child of a non-secure parent is owned by its business-unit team, I-6). Built.
- **E2** — **Round 28**: BFF-backed commands replace in-product native creates under a secure parent; the recent pass runs
  every 2 minutes with writes on; MDA Create privilege kept. Built (§3c, §5).
- No new escalation in r1c. Notepad's record-header-and-notepad-r1 NFR-05/NFR-07 ("memos via Xrm.WebApi only") is superseded
  for the CREATE by round 28 (CLAUDE.md §6.5 path B, an owner decision amending a prior project's NFR) — recorded for that
  project's docs. Owner UAT R3 C11-3 (`launchCreate` opens the model-driven form) is unchanged (§2c).

## 7. Tests, seeds, quality gates

TESTS_SECTION

**Seeds (r1c; each one mutation, run, restored from HEAD and touched; every one BITES):**

SEEDS_SECTION

## 8. Manual live gates (main session; dry run → apply → verify each)

- **G147-5 (HARD pre-deploy)** — `& ./scripts/Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c` (dry run), then `-Apply`, then `-Verify` (exit 0): adds `sprk_createdbyperson` to `sprk_memo`, `sprk_reportcard`, `sprk_budget`, `sprk_kpiassessment`, `sprk_billingevent`. A BFF carrying 147 writes the column on those creates.
- **G147-2 (deploy + schedule)** — PRECONDITION: the task 148 backfill applied and verified in the environment (SECURE-PROJECT-ENVIRONMENT-SETUP §7c.1, `-Verify` exit 0). Deploy the BFF; `/api/admin/jobs/secure-child-reconciliation/status` shows `*/2`, enabled; the first runs report `recentChanges.catchUp.ran = true` until `complete`. Out-of-product probe: as a non-admin test user shared on secure project `65a3fab2`, `POST /api/data/v9.2/sprk_todos` as the user regarding the project; record its owner before and after the next run and the run's `recentChanges`; a non-sharee cannot open it; delete the probe.
- **G147-3 (stamp backfill)** — `pwsh scripts/Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run: expect 4 to write, the two §4 rows unresolvable), `-Apply -AcknowledgeEscalation` (§4 is the review), dry run again (expect 0 to write and the same two root-less rows).
- **G147-4 (per-writer live sequence, POML step 8)** — on each of the three secure root types: each §2a writer → owner read back = Secure Record Owners, sharee can open, non-sharee cannot; RegardingResolver on a saved host and Connections re-file in and out (out: BU team, mirror gone); probes deleted and recorded.
- **G147-6 (E2 ribbon)** — after task 142's helper web resources: `& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run) → export the dedicated ribbon solution (`SpaarkeSecureChildRibbons`, the nine tables, ribbon only) and `-ExportDir <unpacked> -Apply` → `-Verify` → by hand on a secure and an ordinary record of each root and on one child form (an event of a secure project: its To Do subgrid).
- **G147-1** (A2 write probe) — **withdrawn**: round 28 rejected A2.

## 9. Coordination

- **Task 169 (rebases onto 147, round 28)**: on rebase, add `sprk_memo` to the TypeScript `INTERMEDIATE_ROOT_COLUMNS` (the four
  stamp columns, as C# `IntermediateRootColumns`) and keep `CoreAncestorStampTopologyLockstepTests` green; the C# side and
  `RecordContainerResolver.ChildAncestorLinks` already carry it. `CHILD_RECORD_ENTITIES` is unchanged by 147 r1c.
- **Task 152**: done here (people-targeting reads `sprk_createdbyperson`, else `createdby`).
- **Task 162**: the anchorless analysis of §4 (round 15).
- **field-mapping-server-write-path-r1**: its W3/W4 extend `POST /api/v1/child-records` / `OwnedChildWrite.CreateAsync`.
- **Tasks 159 / 161**: the event and communication filing routes are in their families and call `ChildRecordEndpoints.UpdateAsync`.
- **spaarke-ai-architecture-redesign-r1** (chat tools): the update tool's re-file now runs the inline mirror after the
  write (`AfterRefileAsync`), inside Amendment A-UAC146's app-only steps (owner round 13 item 7) — a follow-on of the same
  re-file, no new identity.

## 10. Placement (CLAUDE.md §10) and justification (§11)

**Placement: BFF** for the routes and the after-re-file step (app-identity Dataverse writes after an as-caller check; the
invariant owner's write path; low volume; no package; no new background work — the catch-up extends the one job).
`bff-extensions.md`: BFF identity + domain code; no AI capability consumed (`OwnedChildWrite` and the mapper are Dataverse
cores; the chat handler calling the synchronizer is AI → CRUD, the permitted direction). No new DI registration or interface.

| New surface | Existing (grep) | Extension | Cost of doing nothing |
|---|---|---|---|
| `POST/PATCH /api/v1/child-records/{table}` | `OwnedChildWrite` (chat tools), `PUT /api/v1/documents/{id}`, events / communications families; `/api/dataverse` read-only | a thin HTTP face over the existing core | every browser child under a secure record stays user-owned until a reconcile run |
| `PATCH /api/v1/events/{id}/filing`, `PATCH /api/communications/{id}/filing` | the families' typed update routes | added to the families (round 28) | a re-filed event / communication keeps its owner |
| `SecureChildShareSynchronizer.AfterRefileAsync` + `IsSecureTeamOwnedAsync` (r1c) | `SyncChildAsync`, `RemoveMirrorAsync` (148) | composes the two existing steps; replaces the route-local copy | a move out by the documents route or the chat tool left the old record's sharees on the row for good |
| catch-up in `SecureChildReconciliationJob` (r1c; two fields, one enum) | the recent pass, the sweep cursor | the same reconcile over the same root list | a restart drops every carried record and row; a refused re-own older than the lookback is never retried |
| config `SecureChild:Reconciliation:RecentChangesWritesEnabled` | `WritesEnabled` (sweep) | a separate switch (round 28) | either the sweep writes unreviewed or the net stays off |
| `sprk_budget`, `sprk_kpiassessment`, `sprk_billingevent` in `CreateTables` / `StampedChildTables` / schema script | codified role set holds all three (G146-1) | entries in existing lists | the ribbon's create-then-open would have no BFF create |
| client `bffChildWriteAdapter.ts` (+ `OWNERSHIP_CHILD_TABLES`), `BffChildRecordDataverseClient.ts`, per-host auth bootstrap files | `bffDataServiceAdapter.ts` (no child route behind it) | one seam of decorators | each writer hand-rolls fetch and error parsing; a host control re-files an ownership child as the user |
| `sprk_secure_child_ribbon.js`, `SecureChildRibbons/`, `Deploy-SecureChildNewCommands.ps1` | AccessRibbons (142/150) | same pattern; child subgrids, not root flyouts | the platform "+ New" creates user-owned children under or through a secure record |
| `SecureChildNewCommandAgreementTests`, `secureChildRibbonScript.test.ts` | none spans the files | — | the four files and the C#/TS lists drift silently |

## 11. `.claude/**` edits needed

None.
