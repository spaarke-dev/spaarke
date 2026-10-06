# Task 147: children created or re-filed in the browser, and outside the product (C10 part 2, client; #1069)

> **Run r1c**: 2026-10-04 · **Branch**: `task/uac-r2-147-r1c` (from `wip/uac-r2-147-r1-restart` @ `0ab6411e1`, the r1 run
> stopped by a machine restart mid-round) · **Rigor**: FULL (opus, high)
> **Base**: the batch-4 integration tip — r1 merged `origin/integ/uac-r2-batch4` @ `b8a1374c2` and the local tip `f54b5b29b`;
> r1c merged `origin/integ/uac-r2-batch4` @ `3aa4ebce6` (`220bce6de`, no conflicts), then — on the main session's binding
> note (round 36) — the local `integ/uac-r2-batch4` carrying tasks 159-164 @ `aa7a13ddd` (merge `a9cf2fd48`: three conflicts
> resolved, both sides kept — §1 item 26) and its docs-only tip `63556192c`.
> **Decisions applied**: owner/main-session rounds 1-36; round 28 (E1 = A1; E2 = BFF-backed commands for in-product native
> creates under a secure parent + the L4 recent pass every 2 minutes with writes on; 169 rebases onto 147), round 34 item 6
> (CreateEventWizard's `sprk_Event` bind), round 35 item 6 (147 Q2 = round 28 item 1 as written), **round 36** (the event's
> ONE re-file route in 159's family, filing only, with the child pass; communications likewise in 161's family).
> **Status: COMPLETED in code.** Nothing deployed; every live action was a GET. Live writes are the manual gates in §8.
>
> **Run r1c-v1** (2026-10-05): branch `task/uac-r2-147-r1c-v1` from `task/uac-r2-147-r1c` @ `149fffd9d`; verifier items 1-15 of
> the r1c verification — §0. Every live action a GET.

## 0. Run r1c-v1 — verifier items 1-15

| # | Item | State |
|---|---|---|
| 1 (HIGH) | Every browser re-file that clears a lookup fails live: `MapWebApiPayloadAsync` turned `{Nav}@odata.bind: null` into `"<lookup logical name>": null`, which the Web API refuses (0x80060888) | **Fixed at the one mapper.** `DataverseWriteItemMapper.MapCoreAsync` (behind both `MapAsync` — the chat tools — and `MapWebApiPayloadAsync` — the browser routes) writes a `null` on a LOOKUP column as its metadata navigation property's null bind (`sprk_RegardingMatter@odata.bind: null`); `ClearedColumns` stays keyed by logical name (the re-file core reads it); a `null` on any other column stays that column's null. A polymorphic lookup is cleared through its first navigation property by ordinal name (deterministic; every one binds the same column) — **live, read-only GET 2026-10-05: on all eleven browser tables (to-do, event, memo, invoice, report card, analysis, document, budget, KPI assessment, billing event, communication) the ONLY polymorphic lookup is `ownerid`, which the routes refuse**, so the browser path never clears one. One value per column: a lookup named twice (navigation properties differing in case, a plain key beside its bind, two clears) is a 400. The browser path reads the table's relationships once (it read them twice). Scope closed: (a) the RegardingResolver's sibling pre-clear on every saved-host selection and its clear (to-do, event, report card forms), (b) Connections' unlink and clear-primary, (c) the event side pane's clears, (d) the chat update tool's clears — and (e) a create payload carrying a clear (the app-only create's fields). **The fake now refuses the broken shape**: `ScriptedUserClient.UndeclaredPropertyIn` — a PATCH or POST as the caller, and the app-only create, whose body names a lookup by its logical name or binds a navigation property the type does not declare (case-sensitive) is refused 400 `0x80060888 Could not find a property named …`, as Dataverse refuses it (`RefusedBodies`). Tests pinning the BODY: `EventFiling_TheRegardingLookupsAndTheResolverFields_…` (the reviewer's probe, now asserted), `ChildRefile_TheResolversSetAndPreClear_ReachDataverseAsNavigationPropertyBinds_NeverALogicalName` (set + clear), `ChildRefile_AClearOnlyMoveOutOfASecureMatter_…_ReturnsTheRowToItsBusinessUnit_AndTakesTheMirrorOff` (clear only, AC3 move out), `CommunicationFiling_AConnectionsUnlink_ClearsThroughTheNavigationProperty`, `UpdateTool_ClearingALookupByItsLogicalName_…`, `UpdateTool_ClearingAPolymorphicLookup_…_TheFirstByName`, `ChildCreate_APayloadClearingALookup_CreatesWithTheNavigationPropertysNullBind`, `ChildRefile_APayloadNamingOneLookupTwice_IsRefused400_…` (×4). Seeds S1 (clears by logical name: **7 fail**), S4 (one-value rule off: **4**), S5 (polymorphic order flipped: **1**). Redundant code removed rather than left unseedable: a second "bound twice" check in `MapWebApiPayloadAsync` (seed S3 survived because the one-value rule already covers it) and a client-navigation-property pass-through (identical to the metadata's on every browser table, live) |
| 2 | `UpdateAsync` maps a 403 row read to the uniform 404, untested (seed V3 survived) | **Pinned.** The fake models a row the caller may not read as Dataverse does (403, `UnreadableRows`) beside a missing row (404). `ARowTheCallerMayNotRead_AnswersExactlyAsARowThatDoesNotExist` × 3 entry points of the ONE re-file (`PATCH /api/v1/child-records/{table}/{id}`, the event filing handler, the communication filing core): same status, title, detail and reason code; nothing written. Seed V3 now bites (**3 fail**) |
| 3 | The watermark holding while > 1,000 rows are carried is untested (seed V5 survived) | **Pinned end to end**: `MoreUnplacedRowsThanTheJobCarries_HoldTheWatermark_SoTheRowPastTheCapIsNeverDropped` — 1,000 events under one record flagged secure but not isolated fill the carried set (`sprk_event` lists before `sprk_todo`), the 1,001st unplaced row is a to-do under a SECOND such record; run 1 `pendingOverflow`, 1,000 carried; run 2 re-lists all 1,001; once both records are isolated, the to-do past the cap is corrected (sweep report-only, manual trigger: nothing else can reach it). Seed V5 bites (**1**), and the variant with run 2's assertion relaxed still fails on the to-do left user-owned — the drop the reviewer named |
| 4 | "A carried record no longer flagged secure is dropped" is untested (seed V9 survived) | **Pinned, and reported**: `ACarriedRecordNoLongerFlaggedSecure_IsDropped_NotReconciledAgain_AndReported` (a refused re-own carried; the record unsecured before the next run; that run reconciles nothing — `ProcessedItems 0`, `examined 0`, no owner write — and carries nothing on). The run report no longer lists such a record under `retriedRoots` (it was not looked at again); it is named in a new additive `recentChanges.droppedRoots` and logged, so a record never leaves the carried set silently. Seed V9 bites (**1**) |
| 5 | `-Apply` imports into pac's ACTIVE profile, not `-EnvironmentUrl` | **Fixed**: `pac solution import --environment $EnvironmentUrl --path … --publish-changes` (pac 1.46 help: `--environment` = target, else the active profile's). Guard `TheDeployScriptsPacCalls_ThatReachAnEnvironment_NameEnvironmentUrlExplicitly` (ArchTests; `pac solution pack` is local and exempt); seed A8 (import without it) bites (**1**). Script parses (PowerShell parser, 0 errors) |
| 6 | The side pane wrote the other fields BEFORE the filing (a refused re-file left a partial save); untested — the package had no jest | **Fixed and tested**: `saveEvent` writes the filing through the BFF FIRST (a refusal writes nothing and shows the server's message), then every other field as the caller (a failure after a saved filing says "What the event is filed under was saved, but the other changes were not: …"); the split is the seam's `splitFilingPayload` (one filing rule). **Jest harness added to the package** (`jest.config.cjs`, devDependencies `jest`, `ts-jest`, `@types/jest` — the DocumentUploadWizard pattern; `@spaarke/ui-components` mapped to the seam module's source, the MSAL transport mocked): `eventService.saveEvent.test.ts` **6/6**. Seeds C6-1 (a refused re-file still writes the rest: **1/6**), C6-2 (rest before filing: **3/6**). `tsc --noEmit` and `vite build` green |
| 7 | Verified clean | — (re-confirmed by this run's final suites, §7) |
| 8 | Seeds that bite | — (V1/V2/V4/V6/V7/V8: code unchanged by this run) |
| 9 | Process: `e19a5f4ab` has no attribution line; `220bce6de` used `--no-verify` | Both are pushed history on the task branch and are not rewritten (no force push). Every r1c-v1 commit ran the pre-commit hook and carries the attribution line |
| 10 | AC3 not met (re-file clears rejected live) | **Met in code** (item 1): a move in, a move out through a set or a CLEAR, and a move between records all reach Dataverse in the shape it accepts, pinned by the body tests against a fake that refuses the broken shape. Live confirmation is G147-4 (§8, now with the clear / unlink / chat-clear steps that would have exposed it) |
| 11 | AC13 not a real guard (no PATCH body checked) | **Met**: the re-parent and clear cases assert the body (item 1) |
| 12 | AC9 not pinned for the child-records PATCH | **Met** (item 2) |
| 13 | AC11 — ui-components full jest has 13 pre-existing failures (#1290) | **Proved not this task's, and closed by the owning fix.** This branch: 3,559 = 3,546 passed + **13 failed in 8 suites** — exactly #1290's set (RecordHeader `configResolution`, WorkspaceShell `buildDynamicWorkspaceConfig`, FilePreview `RichFilePreview`, ConversationView forward / emailInFlow, `todoScoreMappings`, `TimelineComposeBox`, `surfaceLaunchRegistry`), none in a file 147 changes. With PR #1293's fix (`origin/fix/ui-components-jest-1290` @ `230f6091e`) applied in the working tree (not committed): **3,558/3,559** — the one left is `todoScoreMappings`' hash pin, stale on the integration base (it pins a `todoScoring.ts` that master's #1118 changed; #1293 re-pins master's); with master's #1118 `todoScoring.ts` and #1293's pin as well: **3,559/3,559**. So the suite is green as soon as the integration branch takes master + #1293, with no 147 change. Not fixed here: #1293 owns those files (the precedent `8c5297792` withdrew a duplicate fix of the same six files); a second fix would be two mechanisms. Every other changed client package: tests and builds green (§7) |
| 14 | AC5 live grant | Manual gate (live write): G146-1 (applied 2026-10-03) + **G147-5** (HARD pre-deploy), exact commands §8 |
| 15 | AC12 manual live gate | Manual gates G147-2 to G147-6, §8; G147-4 now names the clear / unlink / chat-clear read-backs |

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
| 16 AC3 | re-file in → secure; out → BU team, mirror removed | **Code + tests**: `ChildRefile_EveryCensusTable_MovedUnder…` / `…MovedOutOf…LosesTheMirror` (five tables), event and communication filing, documents PUT (`DocumentPut_…SharesTheDocument…`, `…TakesTheMirroredSharesOff`, `…ReleasesTheAnalysisFiledUnderIt`), the chat update tool (`UpdateTool_…`); ONE after-re-file step, `SecureChildReconciler.AfterRefileAsync` (round 36: the row's mirror AND the pass over what is filed under it — `EventFiling_MovedUnder…ToDo…`, `…MovedOutOf…ReleasesTheToDo…`, `…KeepsAToDoAlsoFiledUnderThatMatterIsolated`, `…NeverIsolated_ReleasesNothing…`, `…BetweenSecureMatters…`); seeds R4-R7, R36-5..R36-9, R36-13, R36-14 |
| 17 AC4 | no client owner write; caller without AppendTo refused | **Met**: no `ownerid@odata.bind` in any changed package (grep, §7); uniform 404 per create table, per re-file table and on communication filing; seed R1 (20 fail) |
| 18 AC6 | memo under an event of a secure project → secure; memo CHILD both sides; parity | **Met**: the memo create route (named team + mirror) and the L4 memo-through-event case; taxonomy item 3; parity test green |
| 19 AC7 | A-route refusal creates nothing, message shown | **Met**: route tests assert nothing created on every refusal; every writer shows the server's message (`ChildRecordWriteError`); side pane + SmartTodo three-field fixed in r1c |
| 20 AC9 | routes delegated, parent-authorized, guarded, uniform 404 | **Met**: `/api/v1/child-records` group `RequireAuthorization`, AppendTo as the caller, `RouteAuthorizationGuardTests` lists the file, uniform-404 tests (missing vs unreadable row and parent) |
| 21 AC10 | schedule with writes on in the deployed template | **Met**: `*/2 * * * *`, registered ENABLED, `RecentChangesWritesEnabled: true` in the template; `TheJobShipsEnabled_EveryTwoMinutes`; seed J4 |
| 22 AC11 | builds, jest, publish size, CVE | **Met** (§7): BFF/ArchTests/both integration suites green; every changed client package builds (`build:prod` for PCFs); every jest suite this task touches passes; publish size measured per CLAUDE.md §10 (§7); CVE scan clean. **Pre-existing failures proven not this task's**: ui-components 8 suites / 13 tests, communication-components 2, daily-briefing 5 suites / 1 test, ai-widgets 1 — each fails identically on the integration base (`3aa4ebce6`) at a dot-free path, in files this task does not touch (§7) |
| 23 AC13 | per-writer secure / ordinary / refusal + re-parent | **Met**: three Theories over every create table, three over every re-file table, filing routes, documents PUT, chat tool; client jest per writer seam (§7) |
| 24 AC5 | tables in the codified set, granted live | Repo: every create/re-file table incl. `sprk_kpiassessment`, `sprk_billingevent` is in `config/secure-record-owner-role.json` (G146-1 applied 2026-10-03). Live grant: covered by G146-1; `sprk_createdbyperson` on the five new stamped tables is **G147-5** |
| 25 AC12 | manual live gate | Pending manual gates G147-2..G147-6 (§8) |
| 26 | Round 36 item 1 — the event's re-file after task 159 deleted `PUT /api/v1/events/{id}` | **Done** (§3a). Merged `integ/uac-r2-batch4` first (159-164): 159's deletion of the general PUT stands; the event's ONE re-file route is `PATCH /api/v1/events/{id}/filing` in 159's family (`EventEndpoints.cs`). **Only the filing**: the `sprk_regarding…` lookups and the ADR-024 resolver fields — a shape filter answers 400 `child_record.not_filing` BEFORE any rights question (and the handler asks again). **159's as-caller checks**: `RecordRouteAccessAuthorizationFilter("write")` on `sprk_events({id})` (no Read → the uniform 404; Read without Write → 403), AppendTo on every new parent in the core; **146's F3** on a move out and **156's re-stamp** (the deleted PUT's two checks, restored through the shared core); then **148/149's child pass** — `SecureChildReconciler.AfterRefileAsync` (the event's mirror, then the pass over everything filed under it when it moved under, out of or between secure records; §3a). **Absence pins**: 159's four exact-path pins already miss `PATCH /{id}/filing` (unchanged); two rows added — `PATCH /{id}` and `PUT /{id}/filing` stay unmapped. **Ledger**: the route's row for task 167 is §12 (167 is not on this branch). Clients: the side pane sends only the filing there (§3b) |
| 27 | Round 36 item 2 — communications | 161's family had no re-file route, so the same rule: `PATCH /api/communications/{id}/filing` now carries 161's identity precondition and a new `CommunicationRecordRoute.Refile` (filing shape first, then Write on the communication; one 403 body for unknown / invisible / unwritable), filing only, the same core, re-stamp and child pass. Connections' "clear primary" resets the association status as the caller's own update after the re-file; unlink / clear name the host's REGARDING lookup (§3b). 161's other routes untouched; ledger row §12 |

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

### 2b. Re-files → the table's ONE route; after every re-file `SecureChildReconciler.AfterRefileAsync` (mirror in / off, then the child pass)

| # | Writer | Route |
|---|---|---|
| R1 | RegardingResolver PCF 1.6.0 (to-do, event, report card forms; a saved host set + clear) | child-records / events filing; a NEW host per item 5 |
| R2 | `ConnectionsWriteHandler.ts` set / unlink / clear-primary (CommunicationConnections 1.7.0, EmailWorkspace, reconciliation surfaces) | `PATCH /api/communications/{id}/filing` — the filing only (round 36): "clear primary" resets `sprk_associationstatus` as the caller's own update after it; unlink / clear name the host's `sprk_regarding…` lookup; refused when not wired |
| R3 | TodoDetail `buildTodoRegardingUpdate` | **Inert** — re-checked r1c: no production caller |
| R4 | EventDetailSidePane `eventService.ts` | the filing (`isFilingKey`: `sprk_regarding…` lookups and regarding fields) → `PATCH /api/v1/events/{id}/filing`; completed by / approved by and every other field stay the caller's `Xrm.WebApi` update (round 36) |
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
  every `@odata.bind` resolved from metadata — an unknown navigation property is a 400; r1c-v1: a clear reaches Dataverse
  as the metadata navigation property's null bind, never the lookup's logical name, and a column named twice is a 400), server-owned / creator-person /
  field-secured columns refused (403), `OwnedChildWrite.CreateAsync` / `RefileAsync`, restamp, mirror. Uniform 404.
  r1c: DI parameters `[FromServices]` on the three handlers.
- **Round 36 — the event's re-file in 159's family**: `PATCH /api/v1/events/{id}/filing` (`EventEndpoints.RefileEventAsync`)
  — `ValidateFilingRequestAsync` (shape, no I/O) → `RecordRouteAccessAuthorizationFilter("write", "sprk_events", "id")` →
  `ChildRecordEndpoints.UpdateAsync(…, filingOnly: true)`. `ChildRecordEndpoints.IsFilingColumn` / `FilingShapeProblem`:
  only `sprk_regarding…` properties (the regarding lookups' `@odata.bind` and the ADR-024 resolver fields; a navigation
  property is its lookup's schema name, live metadata — task 159 note §0.2(b)). **Communications** (161's family):
  `CommunicationAuthorizationFilter` + `CommunicationRecordRoute.Refile` (filing shape, then Write on the communication via
  the existing message-write check; deny `COMMUNICATION_REFILE_FORBIDDEN`).
- `SecureChildReconciler.AfterRefileAsync` (r1c; round 36): the ONE after-re-file step for the browser routes,
  `PUT /api/v1/documents/{id}` and the chat update tool, run AFTER the core-ancestor re-stamp (the stamps on the rows filed
  under the moved row are lookups the ownership rule reads; the documents route and the chat tool now run it after their
  re-stamp too). (1) The row's mirror — `SecureChildShareSynchronizer.AfterRefileAsync`: in; off only for a row that WAS
  isolated (read before the write: `_owningteam_value` as the caller on the routes, `IsSecureTeamOwnedAsync` app-only on
  the documents route and the chat tool); a removal fault retried 3 times, then an ERROR naming the row. (2) When the row
  was isolated before or is now: task 148's pass over the row's OWN descendants (`ReconcileBelowAsync` — the engine's
  `Pass` anchored on the child row: no platform-cascade rows, which only a root has; each isolated descendant mirrored to
  ITS secure roots' sharees through `SyncChildAsync`, so a move between secure records swaps the sharees). A descendant
  is released only when the row itself left isolation in this re-file (the F3 the core asked admitted that move; its
  descendants were isolated only through it) — a row that was never isolated releases nothing (owner round 24 item 2). A
  pass that does not complete runs once more (`RefileChildPassAttempts = 2`), then an ERROR; never thrown; whatever was
  not moved is left as it was.
- `SecureChildReconciliationJob`: `*/2`, enabled, recent pass writes unless `RecentChangesWritesEnabled=false`, carry-forward
  (r1), non-Completed synchronizer = failure (r1), **catch-up** (r1c, §5).
- `RecordCreatorPerson.StampedChildTables` += `sprk_memo`, `sprk_reportcard`, `sprk_budget` (r1), `sprk_kpiassessment`,
  `sprk_billingevent` (r1c); `scripts/Set-ChildRecordCreatorPersonSchema.ps1` `$Tables` likewise (G147-5).
- Task 152 + reminder (r1): `MembershipResolverService` binds `sprk_createdbyperson`; `GrantExpiryReminderJob` granter →
  owner → creator person → createdby.

### 3b. Client

Seam `bffChildWriteAdapter.ts` (`createChildRecordViaBff`, `updateChildRecordViaBff`, `withBffChildWrites` — idempotent, a
decorator that INHERITS from a routed service stays in the call path; fail closed `child_record.bff_not_configured`;
`ChildRecordWriteError` with the ProblemDetails message; r1c `OWNERSHIP_CHILD_TABLES` pinned to the C# list; round 36
`isFilingKey` / `splitFilingPayload` / `FILING_ONLY_REFILE_TABLES` — an event or communication update through the seam
sends the filing to its family route FIRST, then every other column through the inner service; a refused re-file writes
nothing),
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
ordering prerequisite of G147-6). Behaviour pinned by `secureChildRibbonScript.test.ts` (the script in a sandbox, 20 tests;
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
- **Watermark**: moves only past a completed listing, in a run whose recent pass wrote, and not while carried rows overflow
  (r1c-v1: pinned end to end — the row past the 1,000 cap is still corrected).
- **Dropped records (r1c-v1)**: a carried record no longer flagged secure is not reconciled again and not carried on; it is
  named in `recentChanges.droppedRoots` (and no longer listed under `retriedRoots`).
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

**r1c-v1 final suites, run ONCE on `e7f405e1f` (2026-10-05; other agents' suites ran concurrently, no failure needed a re-run):**

| Suite | Result |
|---|---|
| BFF unit (`tests/unit/Sprk.Bff.Api.Tests`, full) | **16,476: 16,422 passed, 54 skipped, 0 failed** |
| NetArchTest (`tests/Spaarke.ArchTests`) | **609/609** (608 + the pac guard) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | **87/87** |
| `tests/integration/Spe.Integration.Tests` (full) | 398: **373 passed, 25 skipped, 0 failed** |
| Affected BFF classes during the round (`SecureChildOwnership*`, `ClientChildFixUp*`, `DataverseUpdate/CreateRecordHandler*`, `EmailDraftToolHandler*`) | **354/354**; `SecureChildNewCommandAgreementTests` 9/9 |
| EventDetailSidePane jest (new harness) | **6/6**; `tsc --noEmit` ✓; `vite build` ✓ |
| `@spaarke/ui-components` full jest | this branch 3,559: 3,546 passed, **13 failed — the 8 #1290 suites only**; with PR #1293's fix applied (uncommitted) **3,558/3,559** (the integ-stale `todoScoreMappings` pin); plus master's #1118 `todoScoring.ts` **3,559/3,559** (§0 item 13). `tsc` build ✓; package-lock unchanged |

**Publish size (CLAUDE.md §10)** — fresh short-path worktrees (`C:\wt147v1m`, `C:\wt147v1b`, removed after), `dotnet restore` +
`dotnet publish -c Release --no-restore`, PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`,
PDBs INCLUDED (4 each): base `149fffd9d` **35.55 MB (37,280,009 B), 190 files**; branch `e7f405e1f` **35.55 MB (37,281,073 B),
190 files**, identical file lists → **delta +1,064 B (+0.00 MB)**; uncompressed +3,664 B. Ceiling ≤ 60 MB met. **CVE**:
`dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` — no vulnerable packages (no BFF package change).

**Final suites of r1c, run ONCE on the merged branch (`e19a5f4ab` + the test fix below):**

| Suite | Result |
|---|---|
| BFF unit (`tests/unit/Sprk.Bff.Api.Tests`, full) | 16,461: **16,406 passed, 54 skipped, 1 failed** → the one failure was a real one of THIS task (below), fixed, and its class re-run: `RecordOwnershipResolverTests` + `RecordCreatorPerson*` + `ChildRecordCreatorPersonSchema*` **101/101** |
| NetArchTest (`tests/Spaarke.ArchTests`) | **608/608** |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` (full) | **87/87** (104 before batch 4: 160/163/164 deleted routes and their tests) |
| `tests/integration/Spe.Integration.Tests` (full) | 398: **373 passed, 25 skipped, 0 failed** |
| Affected BFF classes during the round (`SecureChildOwnership*`, events / communications contract tests, `SecureChildRecon*`, `ClientChildFixUp*`, `SecureChildShare*`) | **539/539** |

**The failure the full run found**: `RecordOwnershipResolverTests.ApplyTo_StampsThePersonOnATableThatCarriesTheColumn_EvenWhenTheRowKeepsItsCreator_AndNowhereElse`
used `sprk_memo` as "a child table the BFF never creates app-only: it carries no column" — true until task 147 r1 put the
memo on the G5 app-only create WITH the column (`StampedChildTables`). The case now uses `sprk_agreement` (a lineage child
without the column) and asserts that premise; seed P3 (`sprk_agreement` added to `StampedChildTables`) makes it fail (1).

**Client** (jest under the `.claude` path needs `--testMatch "**/.claude/**/…"`: micromatch's `**` skips dot directories):

| Package | Result |
|---|---|
| `@spaarke/ui-components` (full) | 3,559: 3,538 passed, **21 failed** under load (the .NET suites ran alongside) = **13 pre-existing** (8 suites — RecordHeader `configResolution`, WorkspaceShell `buildDynamicWorkspaceConfig`, FilePreview `RichFilePreview`, ConversationView ×2, `todoScoreMappings`, `TimelineComposeBox`, `surfaceLaunchRegistry`: the same set fails on the integration base `3aa4ebce6` at a dot-free path, in files this task does not touch; filed by the main session as #1290) + **8 contention** (AccessGrantModal ×2 suites, EmailComposer `templatePicker`, RecordNavigationModalShell — re-run alone: 7 suites, **114/114**). Adapter suites (this task's): 67/67 |
| `@spaarke/communication-components` (connections logic, EmailAssociationsAndTracking, EmailWorkspace) | **75/75** (after the hook's reformat) |
| RegardingResolver PCF | **122/122**; `npm run build:prod` ✓, bundle 1.6.0 copied |
| CommunicationConnections PCF | **43/43**; `npm run build:prod` ✓, bundle 1.7.0 copied |
| Earlier in r1c (unchanged since): CreateEventWizard 59/59, SmartTodo.Components 68/68, Notepad 112/112, SmartTodo 96/96, DocumentUploadWizard 11/11, DailyBriefing (touched) 14/14, AI.Widgets 756/757 (1 pre-existing, fails at base), SpaarkeAi 1,125/1,126 (`WorkspacePane.compose-seed-merge` flaky, passes alone) | |
| Builds | `@spaarke/ui-components` `tsc` ✓, EventDetailSidePane `vite build` ✓ (round 36); r1c earlier: every changed package and code page ✓ |

**Publish size (CLAUDE.md §10)** — `dotnet publish -c Release` from FRESH short-path worktrees, zipped with PowerShell
`Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs INCLUDED (4 each):

| Build | Commit | Zip | Files |
|---|---|---|---|
| master (`C:\wt147m`) | `c2ef1857b` | 45.66 MB | 212 |
| integration tip (`C:\wt147n`) | `63556192c` | 35.52 MB (37,243,155 B) | 190 |
| this branch (`C:\wt147b`) | `e19a5f4ab` | 35.55 MB (37,279,975 B) | 190 |

**Delta of task 147 = +0.04 MB (+36,820 B) against the integration tip it merges into**, identical file lists (no partial
publish). Against master the branch is −10.11 MB — task 162 removed QuestPDF (+ its fonts and native libraries) on the
integration branch; not this task. Ceiling ≤ 60 MB: met. **CVE**: `dotnet list package --vulnerable --include-transitive`
on `Sprk.Bff.Api` — no vulnerable packages.

**Seeds (r1c and round 36; each one mutation, run, restored from a byte copy (compared) and touched; every one BITES):**

| Seed | Mutation | Failing tests observed |
|---|---|---|
| M1 | `IntermediateRootColumns["sprk_memo"]` removed | 3 (`MemoTarget_DerivesTheMemosOwnProjectAncestor`, the taxonomy cases) |
| M2 | `RecordContainerResolver.KindByEntity["sprk_memo"]` removed | 1 |
| J1 | carried records not kept (`stillIncomplete.Add`) | 2 (`ARefusedReown_IsRetriedEveryRun…`, `…CarriedBeforeARestart…CatchUp`) |
| J2 | unplaced rows not carried | 2 |
| J3 | a non-Completed synchronizer answer treated as success | 1 |
| J4 | schedule `*/15` | 1 (`TheJobShipsEnabled_EveryTwoMinutes`) |
| J5 | catch-up never runs | 2 |
| J6 | catch-up on a manual trigger too | 6 |
| J7 | catch-up advances without a write | 1 |
| R1 | AppendTo check off | 20 |
| R2 | server-owned / creator-person column check off | 2 |
| R3 | `sprk_kpiassessment` out of `CreateTables` | 4 |
| R4 / R5 | synchronizer `AfterRefileAsync`: mirror always kept / always removed | 8 / 2 |
| R6 / R7 (r1c, superseded) | the documents route / chat tool skip the after-re-file step — re-seeded on the round-36 code as R36-13 / R36-14 | — |
| P1 | people-targeting ignores `sprk_createdbyperson` | 1 |
| P2 | grant reminder ignores `sprk_createdbyperson` | 2 |
| P3 | `sprk_agreement` added to `StampedChildTables` | 1 (`ApplyTo_StampsThePerson…AndNowhereElse`) |
| A1 | `sprk_budget` out of `CreateTables` | 1 (arch) |
| A2 | ribbon: an empty flag allows the platform "+ New" | 1 (arch) |
| A3 | merge script drops `sprk_billingevent` | 1 (arch) |
| A4 | ribbon treats `sprk_event` as a party | 1 (arch) |
| A5 | "+ Add KPI" enable rule removed | 1 (arch) |
| A6 | client `OWNERSHIP_CHILD_TABLES` drops `sprk_document` | 1 (arch) |
| A7 | ribbon `TABLES` drops `sprk_billingevent` | 2 (arch) |
| C1 | seam: unwired host treated as configured | 1/17 |
| C2 | CreateEventWizard binds `sprk_Event` | 2/2 |
| C3 | RegardingResolver new host with no derivable root allowed | 2/41 |
| C4 | saved ownership-child host without a BFF route allowed | 1/41 |
| C5 | idempotence by own property (inherited decorator lost) | 1/17 |
| W1 | ribbon: anything but `true` allows the platform "+ New" | 7/20 |
| W2 | ribbon reads a flag on a non-root host | 5/20 |
| W3 | KPI assessment not create-then-open | 1/20 |
| **R36-1** | `IsFilingColumn` accepts any column | 10 (event/communication 400s, `FilingShape_…` theory, both contract 400s) |
| **R36-2** | event route without its shape filter | 1 (`FilingRoute_ABodyNamingAnythingButTheFiling_Is400_BeforeAnyRightsQuestion`) |
| **R36-3** | event route without `RecordRouteAccessAuthorizationFilter("write")` | 3 (404, 403, no-token) |
| **R36-4** | handler's filing check off | 5 |
| **R36-5** | child pass never runs | 6 (to-do in / out / between, transient, persistent, document's analysis) |
| **R36-6** | a descendant released although the row was never isolated | 1 (`EventFiling_OfAnEventThatWasNeverIsolated_ReleasesNothingFiledUnderIt`) |
| **R36-6b** | never release | 2 (to-do out, document's analysis) |
| **R36-7** | descendants' per-row mirror skipped | 3 |
| **R36-8** | the root-only cascade step run below a child row | 6 |
| **R36-9** | no retry (`RefileChildPassAttempts = 1`) | 1 (`EventFiling_ATransientFailureMovingTheToDo_IsCompletedByTheSecondPass`) |
| **R36-10** | communications filter without the filing shape | 1 |
| **R36-11** | communications `Refile` not write-checked | 3 (every-route theories for `filing`, read-only 403) |
| **R36-12** | communications route without the record filter | 5 |
| **R36-13** | documents PUT skips the after-re-file step | 3 |
| **R36-14** | chat update tool skips it | 2 |
| **C36-1** | seam does not split an event / communication update | 3/37 |
| **C36-2** | `isFilingKey` accepts anything | 10/37 |
| **C36-3** | the rest written before the filing | 3/37 |
| **C36-4** | association status back inside the re-file payload | 1/16 |
| **C36-5** | unlink / clear pick the first lookup to the table, not the regarding one | 2/16 |

Not seeded, with reason: the documents route / chat tool running the step AFTER the re-stamp is an ordering, not a guard:
no test in the in-memory worlds observes a stamp the rule reads. (r1c's other unseeded item — the side pane's split, then
without a test harness — is seeded in r1c-v1: C6-1, C6-2.)

**r1c-v1 seeds** (each one mutation, run, restored from a byte copy compared with `cmp`, touched; every one BITES):

| Seed | Mutation | Failing tests observed |
|---|---|---|
| S1 | mapper writes a lookup clear by its logical name | 7 (every body test) |
| S4 | one-value-per-column rule off | 4 (`…NamingOneLookupTwice…` ×4) |
| S5 | polymorphic clear picks the LAST navigation property | 1 |
| V3 | a 403 row read answered with the caller's own failure | 3 (`ARowTheCallerMayNotRead…` ×3) |
| V5 | watermark moves while carried rows overflow | 1 (and the variant with run 2's re-list assertion relaxed: 1, the row past the cap left user-owned) |
| V9 | carried records reconciled whether or not still flagged | 1 |
| A8 | `pac solution import` without `--environment` | 1 (arch) |
| C6-1 | side pane: a refused re-file still writes the other fields | 1/6 |
| C6-2 | side pane: the other fields written before the filing | 3/6 |
| S3 (survived → code removed) | `MapWebApiPayloadAsync`'s own "bound twice" check off | 0 — the one-value rule covers it, so the duplicate check was deleted, not kept unseedable |

## 8. Manual live gates (main session; dry run → apply → verify each)

- **G147-5 (HARD pre-deploy)** — `& ./scripts/Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c` (dry run), then `-Apply`, then `-Verify` (exit 0): adds `sprk_createdbyperson` to `sprk_memo`, `sprk_reportcard`, `sprk_budget`, `sprk_kpiassessment`, `sprk_billingevent`. A BFF carrying 147 writes the column on those creates.
- **G147-2 (deploy + schedule)** — PRECONDITION: the task 148 backfill applied and verified in the environment (SECURE-PROJECT-ENVIRONMENT-SETUP §7c.1, `-Verify` exit 0). Deploy the BFF; `/api/admin/jobs/secure-child-reconciliation/status` shows `*/2`, enabled; the first runs report `recentChanges.catchUp.ran = true` until `complete`. Out-of-product probe: as a non-admin test user shared on secure project `65a3fab2`, `POST /api/data/v9.2/sprk_todos` as the user regarding the project; record its owner before and after the next run and the run's `recentChanges`; a non-sharee cannot open it; delete the probe.
- **G147-3 (stamp backfill)** — `pwsh scripts/Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run: expect 4 to write, the two §4 rows unresolvable), `-Apply -AcknowledgeEscalation` (§4 is the review), dry run again (expect 0 to write and the same two root-less rows).
- **G147-4 (per-writer live sequence, POML step 8)** — on each of the three secure root types: each §2a writer → owner read back = Secure Record Owners, sharee can open, non-sharee cannot; RegardingResolver on a saved host and Connections re-file in and out (out: BU team, mirror gone); probes deleted and recorded. **r1c-v1 (verifier item 1) — the clear shapes, each must answer 204 and read back**: (i) RegardingResolver on a SAVED to-do, event and report card: select a different parent (the payload pre-clears every sibling lookup), then use the control's clear; (ii) Connections on a communication: unlink, and clear primary; (iii) the event side pane: clear the regarding and save; (iv) the chat `dataverse.update_record` clearing `sprk_regardingmatter` on a probe to-do (and, as a polymorphic probe, `regardingobjectid` on a probe task). Before r1c-v1 each of these was refused live with `0x80060888 Could not find a property named '<lookup>' …`; record the response of each.
- **G147-6 (E2 ribbon)** — after task 142's helper web resources: `& ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run) → export the dedicated ribbon solution (`SpaarkeSecureChildRibbons`, the nine tables, ribbon only) and `-ExportDir <unpacked> -Apply` → `-Verify` → by hand on a secure and an ordinary record of each root and on one child form (an event of a secure project: its To Do subgrid).
- **G147-1** (A2 write probe) — **withdrawn**: round 28 rejected A2.

## 9. Coordination

- **Task 169 (rebases onto 147, round 28)**: on rebase, add `sprk_memo` to the TypeScript `INTERMEDIATE_ROOT_COLUMNS` (the four
  stamp columns, as C# `IntermediateRootColumns`) and keep `CoreAncestorStampTopologyLockstepTests` green; the C# side and
  `RecordContainerResolver.ChildAncestorLinks` already carry it. `CHILD_RECORD_ENTITIES` is unchanged by 147 r1c.
- **Task 152**: done here (people-targeting reads `sprk_createdbyperson`, else `createdby`).
- **Task 162**: the anchorless analysis of §4 (round 15).
- **field-mapping-server-write-path-r1**: its W3/W4 extend `POST /api/v1/child-records` / `OwnedChildWrite.CreateAsync`.
- **Tasks 159 / 161 (round 36)**: the event and communication filing routes are in their families, carry the families'
  as-caller gates and call `ChildRecordEndpoints.UpdateAsync(filingOnly: true)`; 159's deleted-route pins are unchanged
  (they name exact paths) and two rows were added. Route-ledger rows and the GovernedFiles text: §12.
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
| `SecureChildReconciler.AfterRefileAsync` + private `ReconcileBelowAsync` / `MirrorIsolatedDescendantsAsync`, two pass-throughs (round 36) | the engine's `Pass` (148), `SyncChildAsync` (149) | the existing pass anchored on a child row (one flag skips the root-only cascade step); the reconciler composes the synchronizer, so the after-re-file step moved up one level instead of a second walker | a re-filed event's to-dos stay readable by the business unit under the secure record it moved into until the sweep, and stay isolated for good under the ordinary record it moved out to |
| `PATCH /api/v1/events/{id}/filing` filing-only + `ValidateFilingRequestAsync`; `ChildRecordEndpoints.IsFilingColumn` / `FilingShapeProblem` / `NotFilingCode` (round 36) | 159's `RecordRouteAccessAuthorizationFilter`, its `ValidateCreateEventRequestAsync` pattern | the deleted PUT is NOT restored; one filing route in the family (round 36) | an event has no browser re-file at all (159 deleted the PUT), or a general update comes back ungated |
| `CommunicationRecordRoute.Refile` (round 36 item 2) | 161's `MessageDeactivate` write check | one enum value reusing the message-write check; its own deny code | the communication filing route is gated only inside the handler, outside 161's family contract |
| client `isFilingKey` / `splitFilingPayload` / `FILING_ONLY_REFILE_TABLES`; Connections `regardingNavPropFor` (round 36) | `withBffChildWrites` | three exports on the one seam | an event or communication update with any other field is refused 400 by the filing route |
| catch-up in `SecureChildReconciliationJob` (r1c; two fields, one enum) | the recent pass, the sweep cursor | the same reconcile over the same root list | a restart drops every carried record and row; a refused re-own older than the lookback is never retried |
| config `SecureChild:Reconciliation:RecentChangesWritesEnabled` | `WritesEnabled` (sweep) | a separate switch (round 28) | either the sweep writes unreviewed or the net stays off |
| `sprk_budget`, `sprk_kpiassessment`, `sprk_billingevent` in `CreateTables` / `StampedChildTables` / schema script | codified role set holds all three (G146-1) | entries in existing lists | the ribbon's create-then-open would have no BFF create |
| client `bffChildWriteAdapter.ts` (+ `OWNERSHIP_CHILD_TABLES`), `BffChildRecordDataverseClient.ts`, per-host auth bootstrap files | `bffDataServiceAdapter.ts` (no child route behind it) | one seam of decorators | each writer hand-rolls fetch and error parsing; a host control re-files an ownership child as the user |
| `sprk_secure_child_ribbon.js`, `SecureChildRibbons/`, `Deploy-SecureChildNewCommands.ps1` | AccessRibbons (142/150) | same pattern; child subgrids, not root flyouts | the platform "+ New" creates user-owned children under or through a secure record |
| `SecureChildNewCommandAgreementTests`, `secureChildRibbonScript.test.ts` | none spans the files | — | the four files and the C#/TS lists drift silently |
| r1c-v1: additive `ResultJson.recentChanges.droppedRoots` (job) | `retriedRoots` / `carriedRoots` | one field in the existing report | a carried record unsecured between runs leaves the carried set with no trace, and was listed as "retried" though it was not |
| r1c-v1: EventDetailSidePane jest harness (`jest.config.cjs`; devDependencies `jest`, `ts-jest`, `@types/jest`, the versions DocumentUploadWizard / Notepad / SmartTodo already use) | those packages' harnesses; the shared seam's jest suite (covers the seam, not the pane's own `saveEvent`) | the same pattern in one more package; dev-only, nothing in the bundle | the pane's write order (filing first, a refused re-file writes nothing) stays unpinned — the defect verifier item 6 found was invisible to every test |

## 11. `.claude/**` edits needed

None.

## 12. Route authorization ledger input (task 167 — main session records it at integration)

167 is not on this branch, so — as tasks 159 and 161 did — nothing was added to `RouteAuthorizationGuardTests` for these
routes beyond the endpoint-file census (117 after 160/163/164 + `ChildRecordEndpoints.cs` = 118) and the
`ChildRecordEndpoints.cs` HandlerAuthorized entry. `E` = `Sprk.Bff.Api.Tests.Api.Events.EventEndpointsAuthorizationContractTests`,
`C` = `Sprk.Bff.Api.Tests.Api.Communication.CommunicationRecordAuthorizationContractTests`,
`R` = `Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.SecureChildOwnershipAiToolTests`.

| Route key | Mechanism that decides | Deny tests |
|---|---|---|
| `PATCH /api/v1/events/{id:guid}/filing` (ADDED, round 36) | shape filter `ValidateFilingRequestAsync` (400 before any rights question); filter `AddRecordRouteAccessAuthorizationFilter("write", "sprk_events", "id")`; handler (as the caller): AppendTo on every new parent, F3 on a move out | `E.FilingRoute_CallerWithoutRead_GetsTheUniform404_AndTheReFileCoreNeverRuns`; `E.FilingRoute_CallerWithReadButNotWrite_Is403InsufficientRights_AndTheReFileCoreNeverRuns`; `E.FilingRoute_NoBearerToken_TheRealProbeDenies_AndTheReFileCoreNeverRuns`; `E.FilingRoute_ABodyNamingAnythingButTheFiling_Is400_BeforeAnyRightsQuestion`; `R.ChildRefile_EveryCensusTable_UnderAMatterTheCallerCannotAppendTo_…` (the shared core) |
| `PATCH /api/communications/{id:guid}/filing` (ADDED by 147 r1; gated in 161's family, round 36 item 2) | `CommunicationAuthorizationFilter` + `AddCommunicationRecordAuthorizationFilter(CommunicationRecordRoute.Refile)` (filing shape, then Write on the communication); handler as above | `C.EveryRoute_ACallerWithNoRights_IsDenied_AndNothingDownstreamRuns(route: "filing")`; `C.EveryRoute_ACallerWithNoOidClaim_…("filing")`; `C.EveryRoute_FailsClosed_…("filing", …)`; `C.Filing_ByAReadOnlyCaller_Is403_InTheFamilysOwnShape_AndNothingDownstreamRuns`; `C.Filing_ABodyNamingAnythingButTheFiling_Is400_BeforeAnyRightsQuestion`; `R.CommunicationFiling_UnderARecordTheCallerCannotAppendTo_IsTheUniformNotFound_AndNothingIsWritten` |
| `PUT /api/v1/events/{id:guid}` (stays DELETED, 159) | — | `E.DeletedRoutes_AreNotMapped_AndReachNothing(verb: "PUT", path: "/api/v1/events/{id}")` + the two round-36 rows (`PATCH /api/v1/events/{id}`, `PUT /api/v1/events/{id}/filing`) |

**GovernedFiles text for `Api/Events/EventEndpoints.cs`** (replaces task 159's hand-off text, §5 of its note):
```csharp
        new GovernedFile("Api/Events/EventEndpoints.cs", Scope.RouteLevelGate,
            "/api/v1/events/* — GET /{id}, POST /{id}/complete and PATCH /{id}/filing carry RecordRouteAccessAuthorizationFilter "
            + "on sprk_events({id}) (no Read → uniform 404; Read without the route's right → 403); POST / carries the same "
            + "filter with the Create privilege and AppendTo on the regarding record; GET / runs its query as the caller "
            + "(task 159, #1098). PATCH /{id}/filing (task 147, owner round 36) takes only the filing, its shape checked "
            + "before the filter, and re-files through OwnedChildWrite.RefileAsync (AppendTo on every new parent, F3). "
            + "PUT /{id}, DELETE /{id}, /{id}/cancel and /{id}/logs were deleted (round 10 item 1)."),
```
