# Task 147: children created or re-filed in the browser, and outside the product (C10 part 2, client; #1069)

> **Run r1**: 2026-10-04 · **Branch**: `task/uac-r2-147-r1` (from `task/uac-r2-147` @ `1a39f7bdd`) · **Rigor**: FULL (opus, high)
> **Base**: rebased onto the batch-4 integration base — merges `70db1efb1` (`origin/integ/uac-r2-batch4` @ `b8a1374c2`) and
> `e96112845` (local integration tip `f54b5b29b`, which carries task 150's integration). Both clean except
> `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (resolved: the integration's I-1/I-2 rows kept, this task's text re-inserted).
> **Owner decisions applied**: round 28 (E1 = A1; E2 = BFF-backed commands for native creates under a secure parent +
> the L4 recent pass every 2 minutes with writes on; 169 rebases onto 147).
> **Status: COMPLETED in code.** Nothing deployed; every live action was a GET. Live writes are the manual gates in §8.

## 1. Outcome (verifier items 1–23)

| # | Item | State |
|---|---|---|
| 1 | Memo taxonomy breaks on integration | **Fixed.** On the integration base `sprk_memo` is an INTERMEDIATE (`IntermediateRootColumns` = the four stamp columns), a stamp source (`StampSourceColumns`: analysis, communication, document, event, invoice, agreement, budget, report card) with parties (contact, organization, timekeeper), and `RecordContainerResolver` knows it (`KindByEntity` Intermediate; `ByEntity` polymorphic links over the same columns; `sprk_timekeeper` = Party). Lockstep pinned by `CoreAncestorStampTopologyLockstepTests` and the resolver tests. TS side unchanged on this base (169's lockstep obligation, §9) |
| 2 | `RecordContainerResolver` 409 for a memo | **Fixed** (item 1's `ByEntity`/`KindByEntity`). Tests: `Memo_UnderASecureProject…`, `Memo_UnderANonSecure…`, the memo sweep `InlineData`, `Pair_NamingAMemo_OnAToDo_IsRefusedAsUnverifiable` |
| 3 | AC1 overclaimed (R1 new host undecided) | **Closed.** RegardingResolver v1.6.0: a SAVED host is re-filed through the BFF; a NEW host filed under a secure root — or one whose flag cannot be read — is REFUSED before the form save ("create it from that record's New command, or save it first and then file it"), so no new host rides the form's as-the-user save under a secure record. The record's own "New" under a secure record is round 28 E2 (§3c) |
| 4 | L4 recent pass reports problems once, no retries | **Fixed.** A record whose pass came back incomplete and a row the walk could not place are CARRIED to the next run (`_pendingRoots` / `_pendingRows`, at most 1,000 rows; past that the watermark holds), retried first, and reported every run until finished (`retriedRoots`, `retriedRows`, `carriedRoots`, `carriedRows`, `pendingOverflow`) |
| 5 | Seeds survived (synchronizer Failed, job team-refusal untested) | **Fixed.** A synchronizer answer other than Completed fails the recent pass (window stays); new tests `TheSynchronizerAnsweringFailed…`, `TheJobsOwnTeamCheckRefusing…`; seeds J1–J4 bite (§7) |
| 6 | Docs contradict code | **Fixed.** `DATAVERSE-WRITE-PATH-ARCHITECTURE` §4.3 rewritten (no "net below catches"), I-2 Part 2 + gap text and I-6 rewritten for the r1 code; `SECURE-PROJECT-ENVIRONMENT-SETUP` §7c rows (schedule, write modes, carry-forward) |
| 7 | Note named `Run.LineageOfRowAsync`; said 8 seeds | **Fixed.** The method is `Run.LineageOfReadRowAsync`; r0 ran **nine** seeds (S1–S9). r1's seeds are in §7 |
| 8 | E1 framing understated G5 precedents; A3 contradicted #1044 | **Corrected (history, §6).** G5 was settled for BFF-originated creates in rounds 3b, 9 and 25 item 2 and in the #1044 peer agreement (app-only + `sprk_createdbyperson`); r0's A3 recommendation diverged from that agreement. Round 28 chose A1, built here |
| 9 | Backfill dry run: 4 non-memo writes + communication 28.6% need an owner | **Assigned (round 15: no defer).** Owner: **task 147, manual gate G147-3** (main session): `-Apply` over all seven tables writes the 4 stamps; the escalation is analysed in §4 (both "unresolvable" communications sit under analyses filed under documents, which carry no ancestor columns — the known structural gap; nothing to write, ownership unaffected because the L4 walk follows lookups, not stamps) |
| 10 | Docs merge hazard; 169 must rebase onto 147 | **Done.** Resolved on the integration base (header). 169 hand-off in §9 |
| 11 | (verified clean) | — |
| 12 AC1 | each census writer's child under a secure parent → Secure-team-owned, sharees mirrored | **Code + tests**: every create writer → `POST /api/v1/child-records/{table}` (§3); route tests assert owner = named team AND the sharee's mask (`ChildCreate_AToDoUnderASecureMatter…`, memo, budget). Live: G147-4 |
| 13 AC2 | re-file under / out of a secure parent | **Code + tests**: `ChildRefile_AToDoMovedUnderASecureMatter…` (named team + mirror), `…MovedOutOfASecureMatter_ByAFullAccessHolder_ReturnsToTheBusinessUnitTeam_AndLosesTheMirror`, F3 refusal, event and communication filing routes |
| 14 AC3 | no client `ownerid` / share writes; a caller who cannot append gets nothing | **Done.** Client payloads carry no owner (the SmartTodo/LegalWorkspace `ownerid` binds were REMOVED); server refuses owner/server-owned/FLS columns (403) and a record the caller cannot AppendTo (uniform 404) on create and re-file |
| 15 AC4 | every table in the codified set, granted live | Every create table is in `config/secure-record-owner-role.json` (incl. `sprk_budget`, `sprk_memo`, `sprk_reportcard`; G146-1 applied 2026-10-03). `sprk_budget` joined the stamped child tables → G147-5 |
| 16 AC6 | memo regarding an event of a secure project is secure | ClientChildFixUpTests memo case (r0) + memo create route test |
| 17 AC7 | refusal: creates nothing, UI shows the server message | Every writer surfaces `ChildRecordWriteError.message` (ProblemDetails `detail`); no Xrm fallback. Tests per seam (§7) |
| 18 AC8 | non-secure children / unfiled document | **Round 28 changes the premise**: A1 makes a non-secure child BU-team-owned (I-6) — the owner chose it knowingly (round 5 + round 28). An unfiled document save still succeeds (BFF create with no parent → the caller's BU team, `withBffChildCreates` passes it) |
| 19 AC9 | routes delegated-auth, parent-authorized, guarded, uniform 404 | `/api/v1/child-records` group `RequireAuthorization` + AppendTo as the caller; `RouteAuthorizationGuardTests` lists `ChildRecordEndpoints.cs` (HandlerAuthorized; count 122 → 123); uniform-404 tests for missing vs unreadable parent and row |
| 20 AC10 | job on a schedule with writes on, corrects a non-product child | `*/2 * * * *`, registered ENABLED; recent pass writes unless `RecentChangesWritesEnabled=false`; pinned by `TheJobShipsEnabled_EveryTwoMinutes` + the r0/r1 ClientChildFixUpTests |
| 21 AC11 | builds, jest, PCF bumps, publish size, CVE | Builds and jest per package (§7); RegardingResolver 1.5.0 → **1.6.0**, CommunicationConnections 1.6.4 → **1.7.0**, each in all four places; publish size SKIPPED per the brief; CVE §7 |
| 22 AC5 | live grant | Pending manual gate G147-4 (§8) |
| 23 AC12 | live gate sequence | Pending manual gates G147-4/G147-6 (§8) |

## 2. Census (step 1) — corrected for r1

Method as r0 (Grep over `src/client`, `src/solutions` for child-table `createRecord(`/`updateRecord(`, raw POST/PATCH to
`/api/data/`, PCF sources). r1 re-ran it after the switch: **no browser writer of a child table's parent lookup, owner, or
create remains on `Xrm.WebApi`** — the remaining `updateRecord` calls on child tables change no parent (status, pin, body,
name, summary, flags, document type; listed under "out of scope").

### 2a. Creates → `POST /api/v1/child-records/{table}`

| # | Writer | Table | Switched through |
|---|---|---|---|
| W1 | `CreateTodoWizard/todoService.ts` | sprk_todo | `withBffChildWrites(dataService, fetch, base)` |
| W2 | `CreateEventWizard/eventService.ts` | sprk_event | same |
| W3 | `CreateWorkAssignmentWizard/workAssignmentService.ts` follow-on event | sprk_event | the service's `_dataService` is wrapped (the work assignment itself is a ROOT, task 158) |
| W4 | `CreateInvoiceWizard/invoiceService.ts` | sprk_invoice | wrapped `_dataService` |
| W5 | `CreateReportCardWizard/reportCardService.ts` | sprk_reportcard | wrapped |
| W6 | `CreateAnalysisWizardWidget.tsx` (+ its follow-on to-do) | sprk_analysis, sprk_todo | `withBffChildWrites` |
| W7 | `DailyBriefing useInlineTodoCreate.ts` | sprk_todo | `createChildRecordViaBff` (injectable third parameter) |
| W8 | `SmartTodoWidget.tsx` QuickAdd | sprk_todo | `createChildRecordViaBff`; QuickAdd is offered only when the host wires the BFF (LW `todo.registration.ts` does) |
| W9/W10 | SmartTodo `SmartToDo.tsx` + `DataverseService.createTodo` | sprk_todo | `services/childRecordWrites.ts` (lazy `@spaarke/auth`); `ownerid` bind removed |
| W11 | LegalWorkspace `DataverseService.createTodo` | sprk_todo | `createChildRecordViaBff(authenticatedFetch, getBffBaseUrl())`; `ownerid` bind removed |
| W12 | Notepad `useSprkMemoRepository.ts` | sprk_memo | `services/memoWrites.ts` (lazy `@spaarke/auth`) |
| W13–W15 | EventDetailSidePane `App.tsx` memo/to-do, `useRelatedRecord.ts` | sprk_memo, sprk_todo | `services/childRecordWrites.ts` |
| W16 | `EntityCreationService` (`createEntityRecord` child tables, `createDocumentRecords`) | child tables, sprk_document | `createChildRecordViaBff` |
| W17 | `DocumentRecordService` (DocumentUploadWizard) | sprk_document | `withBffChildCreates` around the code page's client (`uploadOrchestrator.ts`) — the only construction site |
| **W18 (r1, missed by r0)** | `createXrmEmailComposeHandlers.ts` → `EntityCreationService` document create | sprk_document | covered by W16 |
| W19 (E2) | the ribbon's "New Budget" | sprk_budget | `sprk_secure_child_ribbon.js` create-then-open; `sprk_budget` added to the route's `CreateTables` |

### 2b. Re-files → the table's ONE route

| # | Writer | Route |
|---|---|---|
| R1 | RegardingResolver PCF (v1.6.0), saved host set + clear | `updateChildRecordViaBff` (child-records / events / communications filing); new host under a secure or unreadable root refused (item 3) |
| R2 | `ConnectionsWriteHandler.ts` set / unlink / clear-primary (CommunicationConnections PCF 1.7.0, EmailWorkspace, reconciliation surfaces, ReconciliationWorkspaceWidget) | `PATCH /api/communications/{id}/filing` via `bffRefile`; refused when not wired. Status advance and override reason stay on `webApi` (plain columns) |
| R3 | TodoDetail re-file (`buildTodoRegardingUpdate`) | **Inert — no production caller.** `buildTodoRegardingUpdate` has no caller; no host wires `onChangeRegarding` / `onSaveTodo` (TodoDetailPanel retired in R4). Nothing to switch; if revived it must use `updateChildRecordViaBff` |
| R4 | EventDetailSidePane `eventService.ts` lookup payload | `PATCH /api/v1/events/{id}/filing` |
| R5 | SpaarkeAi Compose `documentAssociationWrite.ts` | `PUT /api/v1/documents/{id}` (`matterLookup`/`projectLookup`/`invoiceLookup`/`workAssignmentLookup`) via `bffDocumentRefile` |
| R6 | `CreateMatterWizard.associateToRecord` invoice re-file | `PATCH /api/v1/child-records/sprk_invoice/{id}` via `withBffChildWrites` |

**Out of scope, checked:** the r0 list, plus `DocumentRecordService.updateSummary` (summary columns),
`SemanticSearchControl` workspace flag / document type, LW/SmartTodo `DataverseService` status / dismiss / score
updates, `sprk_event_ribbon_commands.js` status changes, `filePreviewService.ts:55`, `sprk_DocumentOperations.js:523`,
user preferences (`sprk_userpreference`, not a child table).

**Coexistence (one create endpoint per table, round 28):** `POST /api/v1/events` (the typed Copilot/AI create) predates
this task and stays — it is a server writer (task 146) with its own typed contract; the browser's generic Web API
payload goes to `child-records`. Both run `OwnedChildWrite`-equivalent ownership through `RecordOwnershipResolver`. If
the main session wants one surface, field-mapping-server-write-path-r1's W3/W4 can host the `child-records` core
(§9) — the core is shared, so moving it is a routing change, not a second rule.

## 3. What was built

### 3a. Server (BFF)

- `Api/ChildRecordEndpoints.cs` (new): `POST /api/v1/child-records/{table}` (G5 create) and
  `PATCH /api/v1/child-records/{table}/{id}` (re-file), plus the shared `UpdateAsync` used by
  `PATCH /api/v1/events/{id}/filing` and `PATCH /api/communications/{id}/filing`. Steps: map the Web API payload as the
  caller (`DataverseWriteItemMapper.MapWebApiPayloadAsync`: `@odata.bind` → lookup via metadata, entity set verified,
  annotations refused, null bind = clear); refuse server-owned / creator-person / field-secured columns (403
  `child_record.denied`); `OwnedChildWrite.CreateAsync` / `RefileAsync`; restamp; mirror (`SecureChildShareSynchronizer.SyncChildAsync`);
  on a move OUT of isolation `RemoveMirrorAsync`. Uniform 404 `child_record.not_found` for a missing or unreadable
  row / bound record (round 9); owner refusals via `ProblemDetailsHelper.RecordOwnerRefused`.
- `OwnedChildWrite`: `RefileAsync` extracted from the AI update handler (ONE re-file core; the handler now calls it),
  `Outcome.ParentUnavailable`, `CallerWriteFailedException` moved here.
- `SecureChildShareSynchronizer`: `SyncChildAsync`, `IsSecureOwnerTeamAsync`, `Run.LoadSecureChildAsync`;
  `SecureRootsAbove.UndeterminedRows`.
- `SecureChildReconciliationJob`: `*/2 * * * *`, enabled; `RecentChangesWritesEnabled` (only explicit `false` stops); the
  sweep runs on a scheduled tick only when its own writes are on; carry-forward (item 4); non-Completed synchronizer →
  failure (item 5).
- `RecordCreatorPerson.StampedChildTables` += `sprk_memo`, `sprk_reportcard`, `sprk_budget`;
  `scripts/Set-ChildRecordCreatorPersonSchema.ps1` `$Tables` likewise (G147-5).
- Task 152 + reminder: `MembershipResolverService` binds `sprk_createdbyperson` (Role `createdBy`) for a human caller
  where the column exists; `GrantExpiryReminderJob` recipient chain granter → owner → creator person → createdby.

### 3b. Client

The seam `bffChildWriteAdapter.ts` (`createChildRecordViaBff`, `updateChildRecordViaBff`, `withBffChildWrites` —
idempotent, fail closed with `child_record.bff_not_configured`, `ChildRecordWriteError` carrying the ProblemDetails
message), `BffChildRecordDataverseClient.ts` (`withBffChildCreates`), and the writers of §2. Notepad, SmartTodo,
EventDetailSidePane and both PCFs bootstrap `@spaarke/auth` lazily (`resolveRuntimeConfig` → `initAuth`, coalesced).
"Created By" readers: Notepad shows `sprk_createdbyperson` else `createdby` (falls back to the old query if the column is
not on `sprk_memo` yet); SmartTodo/LW document-tab queries select `_sprk_createdbyperson_value`.

PCFs: RegardingResolver **1.6.0**, CommunicationConnections **1.7.0** (`ControlManifest.Input.xml`, `index.ts`
`CONTROL_VERSION`, `Solution/.../ControlManifest.xml`, `Solution/solution.xml`, `pack.ps1`); `npm run build:prod`, bundles
copied into `Solution/Controls`. CommunicationConnections' webpack stubs `pdfjs-dist` (the shared barrel reaches SprkChat's
lazy `import('pdfjs-dist')`, which the PCF toolchain's babel cannot parse; the control never previews a PDF).

### 3c. E2 — platform "+ New" under a secure record (round 28 item 2)

**Read-only live inventory (spaarkedev1, 2026-10-04, GET only)** of the three roots' main forms: project — todo, event,
document, invoice, analysis, budget (+ non-child contact/email/kpi/organization/matter/externalaccess); matter — analysis,
budget, communication, invoice, report card (+ contact/kpi/organization/project); work assignment — document, event.
Quick create: `IsQuickCreateEnabled = false` on every child table (forms exist but no subgrid opens them). Form "New" /
"Save & New" on a child form prefill no parent (unfiled create). Native subgrid "+ New" = `Mscrm.AddNewRecordFromSubGridStandard`
(definition read with `RetrieveEntityRibbon`).

Built: `src/client/webresources/js/sprk_secure_child_ribbon.js` (`Spaarke.SecureChild.Ribbon`: `nativeNewAllowed`,
`newChildAvailable`, `newChild`), `infrastructure/dataverse/ribbon/SecureChildRibbons/` (template, idempotent merge
script, README), `scripts/Deploy-SecureChildNewCommands.ps1` (dry run / `-Apply` / `-Verify`). Fail closed: native allowed
only when the host root's `sprk_issecure` was read and is `false`; unreadable, empty, or a non-root host → native hidden,
BFF command shown. Served: todo, event, invoice, report card (wizards), document (upload wizard), communication
(compose page), budget (create-then-open through the BFF). `sprk_analysis` is already covered (AnalysisRibbons hides its
native "+ New"; New Analysis = the BFF-backed wizard). The dry run was executed read-only against dev: platform
definitions match the template on all 7 tables, quick create off; it reports the two task-142 helper web resources as
not yet deployed (an ordering prerequisite for G147-6). Create privilege NOT removed (round 28).

## 4. `sprk_memo` taxonomy, and the stamp backfill (item 9)

r0's taxonomy work stands (C# `ChildRecordEntities`, TS `CHILD_RECORD_ENTITIES`, `$ChildEntities`, pinned). On the
integration base the r1 additions are item 1's (intermediate, stamp source, storage links).

**Backfill dry run (r0, read-only):** 4 stamps to write (todo 2, communication 1, event 1), memo 0. **Escalation gate
(communication 2 of 7 = 28.6% unresolvable) — analysed:** both rows are communications `regarding` an analysis whose own
stamp is empty (`8128d06b…`, `206fed82…`), and those analyses are filed under DOCUMENTS, which carry no ancestor columns
(the 775 "no-ancestor" analyses, the known structural gap). The script cannot derive a stamp from an unstamped target,
and there is nothing upstream to stamp. **Ownership is unaffected**: the L4 walk and the resolver follow the lineage
lookups (document → its project/matter), not the FR-26 stamps. **Owner: task 147, gate G147-3** — apply the 4 writes,
re-run the dry run, and record that the residual unresolvable rows are exactly these structural ones.

## 5. The L4 net (round 28 item 2)

r0's recent-changes pass (watermark, `SecureRootsAboveAsync`, reconcile with the Sweep trigger, never release) plus r1:

- **Schedule**: `DefaultCronSchedule = "*/2 * * * *"`, registered enabled. **Writes**: recent pass on unless
  `SecureChild:Reconciliation:RecentChangesWritesEnabled=false` (emergency stop); the sweep keeps
  `SecureChild:Reconciliation:WritesEnabled` (report-only by default) and a SCHEDULED tick does not run or move it while it
  is report-only, so the §7c.1 backfill runbook's contiguous passes are not disturbed.
- **Carry-forward** (item 4) and **non-Completed synchronizer = failure** (item 5), as §1.
- **Watermark** moves only past a completed listing, in a run whose recent pass wrote, and not while carried rows overflow.
- **Standing correction report**: `ResultJson.changes[]` (each `pass`: `recent` / `sweep`) and `recentChanges.{mode,
  corrected, retriedRoots, retriedRows, carriedRoots, carriedRows, pendingOverflow}`.

## 6. Escalations — r0 history, resolved by round 28

- **E1 (identity of a browser child create)** — r0 put A1/A2/A3 to the owner and recommended A3. **Correction (item 8):**
  the framing understated the precedent: G5 (check as the caller, create as the application, record the person) had been
  settled for BFF-originated creates in owner rounds **3b, 9 and 25 item 2** and in the **#1044 peer agreement** with
  field-mapping-server-write-path-r1 (app-only + `sprk_createdbyperson`); A3 diverged from that agreement. **Round 28:
  E1 = A1**, A2 and A3 rejected. Built (§3).
- **E2 (the L4 interval / native creates)** — **Round 28**: BFF-backed commands replace in-product native creates under a
  secure parent; the recent pass runs every 2 minutes with writes on; MDA Create privilege kept. Built (§3c, §5).
- No new escalation fired in r1. Notepad's record-header-and-notepad-r1 NFR-05/NFR-07 ("memos via Xrm.WebApi only") is
  superseded for the CREATE by round 28 (CLAUDE.md §6.5 path B — an owner decision amending a prior project's NFR);
  recorded here for that project's docs (§10).

## 7. Tests and seed-and-bite (r1)

**New / changed tests (server):** `SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs` (28: create secure / ordinary /
memo / budget, flagged-not-isolated 409, uniform 404 ×2, privilege 403, server-owned and FLS columns, malformed binds,
unsupported tables, re-file under / out (mirror), F3 refusal, invisible-row 404, AppendTo 404, plain PATCH, family tables
400, event filing); `ClientChildFixUpTests` (+9 r1: writes default on, emergency stop, scheduled tick leaves the sweep
cursor, refused re-own retried every run, undetermined row reported every run, missing ancestor, synchronizer Failed,
own-team refusal, `TheJobShipsEnabled_EveryTwoMinutes`); `ChildRecordContainerResolutionTests` / `CoreAncestorResolverTests`
memo cases; `MembershipResolverPeopleTargetingTests` (+3), `GrantExpiryReminderJobTests` (+2);
`SecureChildNewCommandAgreementTests` (ArchTests, 5). Arch guards updated: `RouteAuthorizationGuardTests` (count 123),
`RecordOwnerAssignmentCensusTests` (`ChildRecordEndpoints.cs` unscanned writer; `OwnedChildWrite.RefileAsync` resolver
call; run-as-user regex widened to `user.` with a negative control).

**Client tests:** UI.Components `bffChildWriteAdapter.test.ts` (16) and the shared fake BFF `__mocks__/bffChildWriteFake.ts`
used by the wizard suites (todo, todo upload, event ×4, invoice, report card, EntityCreationService multibind,
AddTodoFollowOn, CreateMatterWizard invoice re-file) — each now proves the create/re-file left through the BFF route and
that an unwired host is refused; DailyBriefing `useInlineTodoCreate` (+2); Notepad hook (+4); RegardingResolver
`ResolverWriteHandler` v1.6.0 block (+8) and App (+1, UPDATE-mode BFF re-file); CommunicationConnections handler (+2);
Communication.Components `clearPrimary` (+1), `RelatedToCell`, `EmailAssociationsAndTracking` (+1); AI.Widgets
CreateAnalysis (BFF route asserted); SpaarkeAi `CreateOnSaveAssociation` (+5, documents PUT). Ribbon script behaviour:
11 checks in a vm harness (scratchpad `ribbon-behavior.mjs`; no web-resource jest harness exists in the repo).

**Seeds (each one mutation, run, restored byte-for-byte and touched):**

| Seed | Mutation | Failed |
|---|---|---|
| M1 | memo dropped from `IntermediateRootColumns` | see §7 run log (seed2.out) |
| M2 | memo storage links removed | " |
| J1 | incomplete roots not carried | " |
| J2 | undetermined rows not carried | " |
| J3 | synchronizer Failed read as nothing to do | " |
| J4 | schedule back to `*/15` | " |
| R1 | AppendTo check skipped | " |
| R2 | server-owned column refusal skipped (route) | " |
| P1 | people-targeting ignores `sprk_createdbyperson` | " |
| P2 | reminder ignores the creator person | " |
| E2a | merge script drops `sprk_budget` | 1 (`TheServedTables_AreTheSame…`) |
| E2b | `nativeNewAllowed` → `flag !== true` (fail open) | 1 (`ThePlatformNew_IsAllowedOnlyFor…FailClosed`) |
| E2c | `sprk_budget` removed from `CreateTables` | 1 (`EveryServedTable_HasACreateSurface…`) |
| C1 | adapter: no fetch → falls to Xrm | adapter test `REFUSES a child write when the host wired no BFF fetch` (written to fail against that shape) |

## 7a. Quality gates (Step 9.5)

**Code review (self, coverage-first).** Fixed in place: the fake BFF parsed a file upload body as JSON (would have broken
the upload suites); Notepad's JSDoc contained `/**/` which closed the comment and broke ts-jest (pre-existing since
`6d5c6f0d06`, fixed to unblock the suites); `@spaarke/ui-components/services` deep import in SmartTodoWidget (no
package export) → root import. Open, recorded: the ribbon script is verified by a scratchpad vm harness only (no
committed JS test runner for web resources); `withBffChildWrites` routes every `updateRecord` of a re-file table through
the BFF PATCH, including status-only updates (the route's not-a-refile branch is the caller's own PATCH — correct, one
extra hop); the BFF base URL is joined by template in the adapter (`buildBffApiUrl` lives in `@spaarke/auth`, which the
UI library does not depend on; host base URLs are host-only, so the result is identical).

**ADR check.** ADR-002 no plugin; ADR-003 fail closed (unwired host refused, unreadable flag hides native, synchronizer
non-Completed fails the run); ADR-008/ADR-028 delegated auth on the new group, ProblemDetails; ADR-010 no new interface or
DI registration (static endpoint class, existing services); ADR-013 no AI types in CRUD code (`OwnedChildWrite` and the
mapper are Dataverse cores beside the chat tools; no `IOpenAiClient`/`IPlaybookService`); ADR-036/052 the existing job,
no new timer; ADR-038 no `Mock<HttpMessageHandler>`, guards seeded; ADR-006 thin ribbon script; ADR-022 PCF platform
libraries unchanged. No violations.

## 8. Manual live gates (main session; dry run → apply → verify each)

- **G147-5 (HARD pre-deploy)** — `& ./scripts/Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c` (dry run), then `-Apply`, then `-Verify` (exit 0): adds `sprk_createdbyperson` to `sprk_memo`, `sprk_reportcard`, `sprk_budget`. A BFF carrying r1 writes the column on those creates.
- **G147-2 (deploy + schedule)** — deploy the BFF; confirm `/api/admin/jobs/secure-child-reconciliation/status` shows `*/2`, enabled; then the out-of-product path: as a non-admin test user shared on secure project `65a3fab2`, create a to-do through the OOB API (`POST /api/data/v9.2/sprk_todos` as the user, regarding the project) and record its owner before and after the next run and the run's `recentChanges`; a non-sharee cannot open it; delete the probe.
- **G147-3 (stamp backfill)** — `pwsh scripts/Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run, all seven tables: expect 4 to write), `-Apply`, dry run again (expect 0 to write; the residual unresolvable rows are §4's structural ones).
- **G147-4 (per-writer live sequence, POML step 8)** — on each of the three secure root types: each §2a writer (wizards, Daily Briefing, Smart To Do, SmartTodo page, Notepad, event side pane, upload, Compose association, RegardingResolver on a saved host, Connections) → owner read back = Secure Record Owners, sharee can open, non-sharee cannot; one re-file out by a Full Access holder → BU team, mirror gone; probes deleted and recorded.
- **G147-6 (E2 ribbon)** — after task 142's helper web resources: `& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run) → export the dedicated ribbon solution (`SpaarkeSecureChildRibbons`, the seven tables, ribbon only) and `-ExportDir <unpacked> -Apply` → `-Verify` → on a secure and an ordinary record of each root: "New &lt;thing&gt;" vs platform "+ New" as README describes.
- **G147-1** (A2 write probe) — **withdrawn**: round 28 rejected A2.

## 9. Coordination

- **Task 169 (rebases onto 147, round 28)**: on rebase, add `sprk_memo` to the TypeScript `INTERMEDIATE_ROOT_COLUMNS`
  (the four stamp columns, as C# `IntermediateRootColumns`) and keep `CoreAncestorStampTopologyLockstepTests` green; the
  C# side and `RecordContainerResolver.ChildAncestorLinks` already carry it.
- **Task 152**: done here (people-targeting reads `sprk_createdbyperson`, else `createdby`).
- **field-mapping-server-write-path-r1**: its W3/W4 extend `POST /api/v1/child-records` / `OwnedChildWrite.CreateAsync`
  (one create core per table) rather than adding a second endpoint.
- **Tasks 159 / 161**: the event and communication filing routes are in their families and call this task's shared
  `ChildRecordEndpoints.UpdateAsync`.

## 10. Placement (CLAUDE.md §10) and justification (§11)

**Placement: BFF** for the routes (they write Dataverse with the app identity after an as-caller check — BFF-only
capability, low volume, no new package, no background work); client changes in the existing seams. `bff-extensions.md`:
BFF identity + domain code; no AI capability consumed. Publish size: skipped per the brief.

| New surface | Existing (grep) | Extension | Cost of doing nothing |
|---|---|---|---|
| `POST/PATCH /api/v1/child-records/{table}` (`ChildRecordEndpoints.cs`) | `OwnedChildWrite` (chat tools), `PUT /api/v1/documents/{id}`, events/communications families; no generic browser create route existed (`/api/dataverse` is read-only) | the route is a thin HTTP face over the EXISTING core; event/communication re-files were added to their own families instead | every browser child under a secure record stays user-owned (readable by the BU) until a reconcile run |
| `PATCH /api/v1/events/{id}/filing`, `PATCH /api/communications/{id}/filing` | the families' PUT/PATCH update routes (typed DTOs, no generic lookup payload) | added to the families (round 28: re-files via existing families) | a re-filed event / communication keeps its old owner |
| `OwnedChildWrite.RefileAsync`, `SecureChildShareSynchronizer.SyncChildAsync` / `IsSecureOwnerTeamAsync`, `DataverseWriteItemMapper.MapWebApiPayloadAsync` | the AI update handler's inline re-file; `SyncRootAsync`; the AI item mapper | extracted / extended, the handler now calls the shared core | two copies of the re-file rule; no inline mirror for one child |
| config key `SecureChild:Reconciliation:RecentChangesWritesEnabled` | `WritesEnabled` (sweep) | a separate switch because round 28 turns the recent pass on while the sweep stays report-only | either the backfill sweep writes unreviewed or the net stays off |
| `sprk_budget` in `CreateTables` / `StampedChildTables` / schema script | the role set already holds budget (G146-1) | one table added to the existing lists | the ribbon's "New Budget" would have no BFF create |
| client `bffChildWriteAdapter.ts`, `BffChildRecordDataverseClient.ts`, per-host `childRecordWrites.ts`/`memoWrites.ts`/`bffWrites.ts` | `bffDataServiceAdapter.ts` (no child create route behind it) | one seam, decorators over the existing `IDataService`/`IDataverseClient`; host files only bootstrap auth | each writer would hand-roll fetch + error parsing |
| `sprk_secure_child_ribbon.js`, `SecureChildRibbons/`, `Deploy-SecureChildNewCommands.ps1` | AccessRibbons (142/150) pattern | same pattern, one template, one script; not added to the Access flyout because it lives on CHILD subgrids, not root forms | the platform "+ New" under a secure record creates user-owned children |
| `SecureChildNewCommandAgreementTests` | none spans the 4 files | — | the four files drift silently |

## 11. `.claude/**` edits needed

None.
