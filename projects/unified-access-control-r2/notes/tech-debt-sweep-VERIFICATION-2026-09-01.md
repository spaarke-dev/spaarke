# Adversarial Verification — Client Tech-Debt Sweep 2026-09-01

> **Filed**: 2026-09-01 · **Role**: adversarial verifier of [`client-tech-debt-sweep-2026-09-01.md`](client-tech-debt-sweep-2026-09-01.md)
> **Method**: every claim independently re-derived. Default verdict NOT-DEAD; "safe to delete" only after checking all 10 consumption channels (static import, dynamic `import()`/`React.lazy`, barrel + barrel-consumers, string/registry refs, `window.__*__` globals, webresources/ribbon XML, code-page entry points, PCF `dist` deep-imports, test-only consumers, deployed-artifact possibility). "I could not find a caller" is stated as such wherever it is the actual evidence.
> **Compile evidence**: `npx tsc --noEmit` was run in `src/solutions/LegalWorkspace` (full output reviewed) — the only errors are pre-existing environmental ones (unbuilt linked `@spaarke/*` dist packages, missing jest types); **zero errors reference any deleted file**.

---

## HEADLINE — was commit `144ef43c4` (27-file LegalWorkspace deletion) safe?

**YES. Nothing that was deleted was reachable through any of the 10 channels. Nothing needs to be restored.**

Evidence per channel (all paths under `src/solutions/LegalWorkspace/src/components/{CreateProject,CreateMatter,CreateEvent,FindSimilar}/`):

1. **Static imports** — repo-wide grep for every deleted symbol (`EventWizardDialog`, `ProjectWizardDialog`, `CreateProjectStep`, `ProvisioningProgressStep`, `SecureProjectSection`, `projectFormTypes`, `projectService`, `provisioningService`, `eventService`, `AssignCounselStep`, `AssignResourcesStep`, `CreateRecordStep`, `DraftSummaryStep`, `NextStepsStep`, `RecipientField`, `AiFieldTag`, `WizardStepper`, LW `FindSimilarDialog`, …): every hit outside LW is a **same-named but distinct shared-lib file** (`Spaarke.UI.Components/components/CreateEventWizard/CreateEventStep.tsx`, `CreateRecordWizard/steps/AssignResourcesStep.tsx`, `WizardFollowOns/steps/RecipientField.tsx`, etc.), none of which were touched by the commit. Inside LW, remaining hits are comments and interface names inside the KEPT files only.
2. **Dynamic imports** — LW has exactly 5 `import()` sites (`WorkspaceGrid.tsx:44,50,56,67`, `ActivityFeed.tsx:54`); all target kept/live files. The one into `CreateProject/` targets the **kept** `CloseProjectDialog`.
3. **Barrels** — the deleted `CreateMatter/index.ts` / `FindSimilar/index.ts` had no remaining importers; LW `components/Wizard/index.ts` re-exports **from `@spaarke/ui-components`**, not from deleted files; the LW public barrel (`src/index.ts`) exports none of the four folders; `@spaarke/legal-workspace` (Spaarke.LegalWorkspace) re-exports only that public barrel.
4. **String/registry refs** — `WorkspaceGrid.tsx` wizard handlers (`handleOpenWizard:284`, `handleOpenProjectWizard:315`, `handleOpenEventWizard`, `handleOpenSummarize`, `handleOpenFindSimilar:364`) all open **webresource code pages** (`sprk_creatematterwizard`, `sprk_createprojectwizard`, `sprk_summarizefileswizard`, `sprk_findsimilar`) via `Xrm.Navigation.navigateTo` — separate solutions, untouched.
5. **Window globals** — `__SPAARKE_OPEN_CLOSE_PROJECT__` targets the **kept** CloseProjectDialog.
6. **Webresources / ribbon XML** — `sprk_wizard_commands.js:270` `openFindSimilarDialog` opens the `sprk_findsimilar` code page (`FindSimilarCodePage` solution — verified standalone; its `App.tsx` imports only `OOB_MODAL_SIZES` from the shared lib). No ribbon reference to any deleted file.
7. **Code-page entry** — LW `main.tsx`/`index.ts` compile clean (tsc) with no path into the deleted set.
8. **PCF dist deep-imports** — deleted files lived in a solution (not a dist-published lib); every `@spaarke/ui-components/dist/...` import in `src/client/pcf/**` was enumerated; none resolves into LW.
9. **Tests** — no test imports any deleted file (tsc covered LW tests).
10. **Deployed artifacts** — the deleted files were internal modules of the LW bundle, never deployed individually; a ribbon cannot invoke a React module. No out-of-repo exposure.

**Were the 7 kept files correctly kept? YES**, with one forward-looking nuance:

| Kept file | Live consumer | Status |
|---|---|---|
| `CreateProject/CloseProjectDialog.tsx` | `Shell/WorkspaceGrid.tsx:56` (`React.lazy`) + render `:1034` | correctly kept |
| `CreateProject/closureService.ts` | `CloseProjectDialog.tsx:85` | correctly kept |
| `CreateMatter/matterService.ts` | `FilePreview/FilePreviewDialog.tsx:18` (`searchUsersAsLookup`) — FilePreviewDialog is live via `RecordCards/DocumentCard.tsx:33,334` | correctly kept |
| `CreateMatter/formTypes.ts` | `matterService.ts:18` | correctly kept |
| `CreateMatter/wizardTypes.ts` | `Playbook/DocumentUploadStep.tsx:26` **only** | kept-for-a-dead-chain — see below |
| `CreateMatter/FileUploadZone.tsx` | `Playbook/DocumentUploadStep.tsx:21` **only** | kept-for-a-dead-chain |
| `CreateMatter/UploadedFileList.tsx` | `Playbook/DocumentUploadStep.tsx:22` **only** | kept-for-a-dead-chain |

⚠️ The last three are alive **only** because the LW `Playbook/` chain (sweep item 1.2, confirmed dead below) imports them. If/when `components/Playbook/` is deleted, these three become zero-importer and should go **in the same commit** — but `matterService.ts`/`formTypes.ts`/`CloseProjectDialog.tsx`/`closureService.ts` must stay.

---

## TASK 2 — dead-code claim verdicts

| # | Claim | VERDICT | Channels checked | Decisive evidence | Disposition |
|---|---|---|---|---|---|
| 1.1 | Shared-lib `Spaarke.UI.Components/src/components/FindSimilar/**` (5 files) dead | **CONFIRMED-DEAD** | static, dynamic, barrel (`components/index.ts` exports only `./FindSimilarViewer`; no `./FindSimilar` export), pcf-safe (`FindSimilarDialog` appears only in the stale header comment `pcf-safe.ts:8`), dist deep-imports (both PCF `FindSimilarDialog` mentions import `dist/components/FindSimilarViewer`), webresource (`sprk_wizard_commands.js` opens the separate `sprk_findsimilar` code page), string refs, tests | Only importer was the LW adapter, deleted in `144ef43c4`. All remaining repo hits are comments (`Wizard/wizardShellTypes.ts:288`, `WizardShell.tsx:80,238`, `FilePreview/index.ts:8`, `DocumentUploadWizard` comments) | **safe-to-delete** (5 files) |
| 1.2 | LW `components/Playbook/**` (9 files) — fifth dead chain | **CONFIRMED-DEAD** | static (all LW-wide grep hits for `PlaybookCardGrid`/`ScopeConfigurator`/`ScopeList`/`DocumentUploadStep`/`FollowUpActionsStep`/`playbookService`/`analysisService` are inside the folder), dynamic (none of the 5 LW `import()` sites), barrel (LW `src/index.ts` exports no Playbook; `@spaarke/legal-workspace` facade re-exports only that barrel), registry (GetStarted `createPlaybookHandlers` does not import it) | The folder's own `analysisService.ts:15` imports the **shared** `@spaarke/ui-components/components/Playbook` — a stale adapter layer, exactly as the sweep says | **safe-to-delete** (9 files) — and take the 3 orphaned CreateMatter files with it (see headline) |
| 1.3 | `services/SprkChatBridge.ts` dead; "only importers are its own unit test and StreamingWriteHarness" | **REFUTED-LIVE** (compile-time) | static incl. type-imports — the channel the sweep missed | **Live type imports from live components**: `SprkChat/types.ts:1020` (`bridge?: import('../../services/SprkChatBridge').SprkChatBridge`), `SprkChat/hooks/useSelectionListener.ts:24`, `RichTextEditor/hooks/useDocumentStreamConsumer.ts:27-32` (consumed by `RichTextEditor.tsx`). Also **3** test files, not 1 (`SprkChatBridge.test.ts`, `.security.test.ts`, `.integration.test.ts`). Deleting per the sweep's disposition **breaks the shared-lib build**. Runtime nuance: no production code constructs `new SprkChatBridge` (only tests/harness/doc comments) — the bridge is runtime-dormant but structurally load-bearing | **DO NOT DELETE as prescribed**; needs-owner-decision (a real decommission must also strip the `bridge` prop plumbing from SprkChat + RichTextEditor) |
| 1.4 | `__test-harness__/StreamingWriteHarness.tsx` dead | **CONFIRMED-DEAD** | static/dynamic/barrel/tests | Zero importers repo-wide. (Sweep's framing "it is the only thing keeping 1.3 alive" is wrong — the live type imports above are) | **safe-to-delete** (harness file alone) |
| 1.5 | VisualHost `configurations/matterMainCards.ts` + `matterReportCardTrends.ts` dead | **CONFIRMED-DEAD** | static (the string `configurations/` appears NOWHERE in VisualHost source), repo-wide symbol grep (only self + comments, e.g. `Spaarke.Visuals/src/types/index.ts:131`) | Unimported → not bundled | **safe-to-delete** (2 files) |
| 1.6 | `Spaarke.Visuals/src/components/GradeMetricCard.tsx` dead | **CONFIRMED-DEAD** | repo-wide symbol grep, barrel (`components/index.ts:26` `export * from './GradeMetricCard'` — no consumer of the symbol anywhere), dist | Only refs: dead configs (1.5), barrel line, comments (`cardConfigResolver.ts:55`, `MetricCardMatrix.tsx:8`) | **safe-to-delete** + remove barrel line |
| 1.7 | 7 DatasetGrid-era hooks dead (`useDatasetMode`, `useHeadlessMode`, `useVirtualization`, `useEntityTypeConfig`, `useDirtyFields`, `useOptimisticSave`, `useWriteMode`) | **CONFIRMED-DEAD** | symbol grep (13 files total: own files, 2 own tests, `hooks/index.ts`, `types/index.ts:40-41`, `jest.config.js:11`, one comment in CalendarWorkspaceWidget), PCF dist channel (every `dist/hooks` import enumerated: `RECORDSUMMARY_FIELD`, `useRecordFieldValues`, `useRecordHeaderToolbarActions`, `useRecordHeaderFields`, `toolbarLaunchDefaults` — none of the 7), solutions-alias channel (SmartTodo aliases the hooks path to `useTwoPanelLayout.ts`; Notepad aliases the barrel — barrel must be updated in the same commit), string-command channel (`mode_stream`/`mode_diff`/`mode_auto` ids exist only in the hook + its test) | Note: only **2** of the 7 have test files (`useVirtualization.test.ts`, `useWriteMode.test.ts`) | **safe-to-delete** (7 hooks + 2 tests + barrel entries in `hooks/index.ts` + `types/index.ts:40-41` + `jest.config.js:11-12` entries) |
| 1.8 | Code-page `DocumentRelationshipViewer` `RelationshipNetwork.tsx` + `RelationshipTimeline.tsx` dead | **CONFIRMED-DEAD** | static (imports commented at `App.tsx:47-48`), dynamic (zero `import()` in the code page), tests | No other importer in the page | **safe-to-delete** (2 files, ~1,450 lines) — or consciously reinstate |
| 1.9 | `WizardRegistry/PlaceholderWizard.tsx` dead | **CONFIRMED-DEAD** | static/dynamic/registry (`wizardRegistry.ts:109-119` documents deliberate retention; no registry entry references it) | Zero importers | **leave-with-reason** (documented retention) — agree with sweep |
| 1.10 | `getFeatureFlag()` zero consumers (LW + SpaarkeAi `publicConfig.ts`) | **CONFIRMED** | call-site grep | Only definitions + doc comments (`LW publicConfig.ts:80`, `SpaarkeAi publicConfig.ts:100`, comments in both `main.tsx`) | document / delete getter — agree |
| 1.11 | `pcf/tsconfig.json:22` excludes non-existent `UniversalDatasetGrid/**` | **CONFIRMED** + **UNDERCOUNTED** | Glob/ls | Folder absent (20 real controls verified). **Additional stale excludes the sweep missed**: `AiToolAgent/**`, `AISummaryPanel/**`, `SourceDocumentViewer/**` also point at folders that do not exist | fix (remove 4 lines, not 1) |
| 6A.1a | `code-pages/PlaybookBuilder/src/services/authService.ts` + `src/config/msalConfig.ts` | **CONFIRMED-DEAD** | static | `msalConfig`'s sole importer is `authService.ts:22`; `authService` has zero importers | **safe-to-delete** |
| 6A.1b | `code-pages/DocumentRelationshipViewer/src/services/auth/{MsalAuthProvider,msalConfig}.ts` + `src/types/auth.ts` | **CONFIRMED-DEAD** | static | `MsalAuthProvider.ts:17-18` is the sole importer of the other two; nothing imports `MsalAuthProvider`. Bonus: removes hardcoded dev CLIENT_ID/TENANT_ID | **safe-to-delete** (3 files) |
| 6A.1c | `pcf/EmailProcessingMonitor/control/AuthService.ts` | **CONFIRMED-DEAD** | static (files_with_matches: exactly 1 = itself) | Host uses `./authInit` | **safe-to-delete** |
| 6A.1d | `pcf/SemanticSearchControl/.../services/auth/{msalConfig,index}.ts` | **CONFIRMED-DEAD** | static (zero `services/auth` refs in the control) | Both files are no-export tombstones whose own headers request deletion | **safe-to-delete** (2 files) |
| 6A.1e | `pcf/DocumentRelationshipViewer/.../services/auth/msalConfig.ts` + `types/auth.ts` | **CONFIRMED-DEAD** | static + barrel (`types/index.ts` exports only `./graph` + `./api`, NOT `./auth`) | `msalConfig` body is `export {}`; `auth.ts` unimported | **safe-to-delete** (2 files) |
| 6A.1f | `code-pages/DocumentRelationshipViewer/src/components/NodeActionBar.tsx` | **CONFIRMED-DEAD** | static (`App.tsx:56` comment "NodeActionBar removed") | Zero importers. The PCF twin's NodeActionBar is separate and live — do not confuse them | **safe-to-delete** |
| 3.1 | LW `components/Wizard/**` shims (5 files: `index.ts`, `WizardShell.tsx`, `wizardShellReducer.ts`, `wizardShellTypes.ts`, `WizardSuccessScreen.tsx`) | **CONFIRMED-DEAD** (as of post-144ef43c4) | static (zero LW importers of `components/Wizard` remain), barrel | Each file is a one-line re-export over `@spaarke/ui-components/components/Wizard/*`; their only consumers were the deleted chains | **safe-to-delete** (5 files) |

---

## TASK 3 — route-claim verdicts (R1–R17)

All 17 **route-absence claims are CONFIRMED** — I could not refute a single one server-side (nested `MapGroup` prefixes were resolved in every case). The differentiator the sweep under-delivered is **reachability**, which splits the 17 into shipped-live 404s vs dormant client code. One **live mismatch the sweep missed entirely** was found (R-extra).

| # | Client (file:line, resolved URL) | Server truth | VERDICT | Reachability / user-visible impact |
|---|---|---|---|---|
| R1 | `SprkChat.tsx:1767` — `${apiBaseUrl}/api/ai/chat/sessions/{id}/plan/approve` POST via `readSseStream` | Deleted; comment at `ChatEndpoints.cs:347-350`; replacement `MapPost ".../gates/{gateId}/resolve"` at `:365` | **CONFIRMED-MISMATCH** | **Dormant-LIKELY**: the plan-approve UI renders only on plan SSE events the deleted dispatcher stack used to emit — server never sends them, so the button should be unreachable. Not traced to certainty; treat as dead client code pending Track-B pruning |
| R2 | `SpaarkeAi/components/conversation/usePlaybookOptions.ts:95` — POST `/api/ai/playbook-dispatch/execute` | Removed (`ChatEndpoints.cs:400` comment; `SprkChatAgentFactory.cs:1213`) | **CONFIRMED-MISMATCH** | Dormant — the client file's own header (`:12-18`) documents the leg as dormant wiring kept for the prop contract, deletion owned by task 046/Track-B |
| R3 | `Reporting/src/services/reportingApi.ts:347-349` — GET `${getBffBaseUrl()}/api/reporting/privilege` | `/api/reporting` group (`ReportingEndpoints.cs:40`) serves status/embed-token/reports-CRUD/export — **no `/privilege`** | **CONFIRMED-MISMATCH** | **LIVE** — `useReportingPrivilege` is called at `Reporting/src/App.tsx:163` on every app mount; privilege gating degrades to its error default on a guaranteed 404 |
| R4 | `pcf/EmailProcessingMonitor/control/EmailProcessingDashboard.tsx:183` — GET `/api/admin/email-processing/stats` | Zero `email-processing` hits anywhere server-side | **CONFIRMED-MISMATCH** | **UNSURE reachability** — whether the EPM PCF is deployed/bound is not knowable from the repo (org-side binding possible); in-repo it is excluded from the root pcf tsconfig |
| R5 | `utils/adapters/xrmUploadServiceAdapter.ts:106` — POST `${baseUrl}/api/documents/upload` | No such route (only `/api/compose/upload`; `UploadEndpoints.cs` deleted by task 073) | **CONFIRMED-MISMATCH** | Dormant: the service is constructed and passed as a prop by `useWizardPageBootstrap.ts:138` + `CreateMatterWizard/main.tsx:52` + `CreateProjectWizard/main.tsx:52`, but **no live code ever invokes `.uploadFile()`** — `CreateProjectWizard.tsx:341` destructures it as `_uploadService` (deliberately unused) |
| R6 | same adapter `:177` — GET `/api/containers/{entity}/{id}` | Home was deleted `UploadEndpoints.cs` (task 073; retirement note at `EndpointMappingExtensions.cs:145-170`) | **CONFIRMED-MISMATCH** | Dormant — `.getContainerIdForEntity()` has zero invocations |
| R7-R9 | `Spaarke.SdapClient/src/SdapApiClient.ts:168` GET, `operations/DeleteOperation.ts:16` DELETE, `operations/DownloadOperation.ts` GET `.../content` — `/api/obo/drives/{d}/items/{i}` | Deleted 2026-08-26 task 071 (`OBOEndpoints.cs:378-380`) | **CONFIRMED-MISMATCH** ×3 | Dormant — `SdapApiClient` is constructed by `DocumentUploadWizard/uploadOrchestrator.ts:144` and `EntityCreationService.ts:177` but **only the upload path is called**; zero call sites for `downloadFile`/`deleteFile`/`getFileMetadata` |
| R10-R11 | `external-spa/src/api/web-api-client.ts:433` GET + `:592` POST — `/api/v1/external/projects/{id}/events` | External group (`ExternalProjectDataEndpoints.cs`) serves projects/documents(content)/todos/contacts/organizations — **no `/events`** | **CONFIRMED-MISMATCH** ×2 | **LIVE** — `EventsCalendar` is mounted on the shipped `ProjectPage.tsx:264` Calendar tab; external users' calendar 404s |
| R12 | `web-api-client.ts:601-604` `updateEvent` → PATCH `/api/v1/external/events/{id}` (verb verified: `updateRecord` uses PATCH, `:330`) | Only `PATCH /todos/{id}` exists under external; `/api/v1/events` group (`EventEndpoints.cs`) has PUT not PATCH and no external prefix | **CONFIRMED-MISMATCH** | Dormant — `updateEvent` is **exported but has zero callers** |
| R13-R15 | `external-spa/src/components/DocumentLibrary.tsx:274` GET `.../documents/{id}/versions` · `:584` GET `.../documents/{id}/download` · `:408` POST `.../documents/upload` | No `/documents/*` routes under the external group; live shape is `/projects/{id}/documents/{docId}/content` (`ExternalProjectDataEndpoints.cs:72`) | **CONFIRMED-MISMATCH** ×3 | **LIVE** — DocumentLibrary is mounted on shipped `ProjectPage.tsx:244`; external-user version history, download, and upload all 404 |
| R16 | `external-spa/src/hooks/usePlaybookExecution.ts:123` — POST `/api/v1/external/ai/playbook` | No `/ai/*` under the external group | **CONFIRMED-MISMATCH** | Dormant — sole consumer `AiToolbar.tsx` is **not mounted anywhere** |
| R17 | `utils/adapters/bffDataServiceAdapter.ts:100` — GET `${baseUrl}/api/dataverse/{entity}[/{id}]` | `/api/dataverse` groups serve only `savedquery`/`savedqueries`/`metadata`/`record/{entity}/{id}`/`fetch`/`gridconfigurations` — **no bare-entity collection route, and single-record needs the `record/` segment the adapter omits** | **CONFIRMED-MISMATCH** (upgraded from the sweep's LIKELY) | **LIVE** — `PlaybookLibraryPage` (routed `/playbooks/:entityType/:entityId`, `external-spa App.tsx:145`) feeds this dataService into `PlaybookLibraryShell`, which calls `dataService.retrieveMultipleRecords('sprk_document', …)` (`DocumentSelector.tsx:134`, `PlaybookLibraryShell.tsx:246-251`) → guaranteed 404 |
| **R-extra** (NEW — missed by the sweep) | `utils/adapters/bffUploadServiceAdapter.ts:113` POST `/api/documents/upload` + `:192` GET `/api/containers/{e}/{id}` — **with a LIVE caller**: `external-spa/pages/DocumentUploadPage.tsx:227` calls `uploadService.uploadFile(...)`, routed at `/upload` (`App.tsx:146`) | Same absent routes as R5/R6 | **CONFIRMED-MISMATCH, LIVE** | **The shipped external-spa Document Upload page 404s on every upload.** The sweep listed only the dormant `xrm` twin; the `bff` twin with a live consumer is the worse instance |

**7.2 side-claims re-checked**:
- *"No MapDelete on `/api/spe/containers/{id}`"* — literally true, **but the conclusion "the fake-delete UI cannot be wired without a new endpoint" is REFUTED**: `POST /api/spe/bulk/delete` (`Api/SpeAdmin/BulkOperationEndpoints.cs:50`) is a live bulk soft-delete that `ContainerResultsGrid.tsx:413` already uses (`speApiClient.bulk.enqueuDelete`, `speApiClient.ts:1413`). ContainersPage could be wired to it today.
- *Possible duplicate `close-project` registration (`AmbiguousMatchException`)* — **RESOLVED: NO duplicate.** `ProjectClosureEndpoint.MapProjectClosureEndpoint` (`:46-48`) is an extension method that is **never called**; the only live registration is `ExternalAccessEndpoints.cs:142`. (The uncalled extension is minor dead server code.)
- *closureService twins hit the same live route* — CONFIRMED (`POST /api/v1/external-access/close-project`, one registration).

---

## TASK 4 — mis-wired paths (§4) and binding docs (§5)

### §4 behavioural claims

| # | VERDICT | Evidence |
|---|---|---|
| 4.1 SpeAdmin container Delete is a stage prop | **PARTIALLY CONFIRMED / PARTIALLY REFUTED** | CONFIRMED: `ContainersPage.tsx:1036-1070` `handleDelete` verifiably makes **no server call** — `window.confirm` → optimistic local-state removal → "N containers deleted (moved to Recycle Bin)" success message; its own comments (:1049-1055) admit it. That page's button IS a false compliance signal. REFUTED: the sweep's broader framing ("SpeAdmin's container Delete makes no server call at all", "server has no delete route/cannot be wired") — the search surface `ContainerResultsGrid.tsx:405-425` performs a REAL delete via `POST /api/spe/bulk/delete` (`BulkOperationEndpoints.cs:50`), and `RecycleBinEndpoints.cs` exists. Fix is a small wiring change, not a new endpoint |
| 4.2 Follow-on To Dos skip the Field Mapping engine | **CONFIRMED end-to-end** | `TodoService` ctor (`components/CreateTodoWizard/todoService.ts:78-90`): `_authenticatedFetch`/`_bffBaseUrl` optional, "when omitted, the engine call is skipped"; the gate is real (`:213` `if (this._authenticatedFetch && this._bffBaseUrl)`). `createTodoRegardingChild` (`WizardFollowOns/steps/AddTodoFollowOnStep.tsx`) constructs `new TodoService(dataService)` — deps structurally unthreadable. Live degraded callers verified: `CreateInvoiceWizard.tsx:550`, `CreateReportCardWizard.tsx:542`, `CreateAnalysisWizardWidget.tsx:916`. The widget's comment `:36` ("already Field-Mapping-driven internally") is misleading exactly as claimed. Contrast confirmed: `TodoWizardDialog.tsx:244` passes all three deps |
| 4.3 `?? fetch.bind(window)` degrade trap | **CONFIRMED + UNDERCOUNTED** | Sweep's 3 sites verified (`CreateProjectWizard.tsx:605`, `CreateInvoiceWizard.tsx:529`, `CreateReportCardWizard.tsx:521`) — plus **2 more the sweep missed**: `CreateInvoiceWizard.tsx:485` and `CreateReportCardWizard.tsx:480` (ReportCardService/InvoiceService construction, same pattern). `CreateMatterWizard` confirmed clean (zero `fetch.bind` hits) |
| 4.4 `sprk_registrationribbon.js` env-pinned | **CONFIRMED** | `:26-45`: dev BFF URL const, dev clientId/bffAppId/tenantId, `redirectUri: spaarkedev1`. `:79-90`: hostname switch exists for the **URL only** (and maps `spaarke-demo` to the DEV BFF); MSAL identity block is unconditionally dev. Approve/Reject paths agree with `RegistrationEndpoints.cs` (env-pinning defect, not a 404 — matches 7.2) |
| 4.5 tenant-less reference resolver | **CONFIRMED** | `SpaarkeAi/components/conversation/useCommandRouting.ts:185-188` — `tenantId: ""` with the documented degraded-caching TODO(084) |
| 4.6 `__SPAARKE_OPEN_CLOSE_PROJECT__` has no in-repo caller | **CONFIRMED (in-repo); overall UNSURE — correctly so** | Repo-wide grep: only the registration (`WorkspaceGrid.tsx:450`), its cleanup, JSDoc, and project notes. **Corroborating find**: `projects/sdap-secure-project-module/tasks/062-project-closure-workflow.poml:56` — the global was exposed "for ribbon command integration", which strengthens the hypothesis that an **org-side Ribbon Workbench command** (never round-tripped into the repo) is the intended caller. Do NOT treat the LW CloseProjectDialog as dead UI without an environment query; escalate to owner as the sweep says |

### §6A.2 / §6A.3 additions

| Item | VERDICT | Evidence |
|---|---|---|
| Daily-briefing options silently discarded | **CONFIRMED** | `Spaarke.DailyBriefing.Components/.../dailyBriefing.registration.ts:130-156`: all four options `@deprecated R2.1 — Ignored`; LW caller `sections/dailyBriefing/dailyBriefing.registration.ts:85-86` still passes `onRateLimitError: routeRateLimitTelemetry` + `loadNotificationContext` |
| False `@deprecated` on live `IHostContext` | **CONFIRMED** | `office-addins/shared/adapters/IHostAdapter.ts:193` tags it deprecated; live uses at `taskpane/App.tsx:4,117,146` + `SaveView.test.tsx:12,20`; no replacement exists. An annotation inviting deletion of working code — the reverse trap |
| Office-addins dead branch (`getContentToSave`/`supportsFeature`/`IContentData`/`HostFeature`) | **CONFIRMED** | Implemented at `word/WordHostAdapter.ts:261,282` + `outlook/OutlookHostAdapter.ts:331,353`; the types are `@deprecated` in `IHostAdapter.ts:208,248`, declared in no interface, and have zero call sites. (The adapter CLASSES are live — only these members are dead) |
| `sprk_emailactions.js` two-copy drift | **CONFIRMED** | 732 vs 730 lines; md5 differ (`src/client/webresources/js/` vs `infrastructure/dataverse/ribbon/EmailRibbons/WebResources/`) |

### §5 binding-doc rows (all `.claude/**` + ADR rows verified; others sampled)

| Row | VERDICT | Evidence |
|---|---|---|
| 5.1 `.claude/constraints/pcf.md` MUST → `UniversalDatasetGrid/` | **CONFIRMED** | Line ~69-70 verbatim; folder absent (20 controls enumerated) |
| 5.2 pcf.md tree lists `UniversalDatasetGrid/` + `components/{layout,data,feedback}/` | **CONFIRMED** | Tree at :178-192; all three subfolders confirmed non-existent (`components/` is flat) |
| 5.3 six pattern files point at `UniversalDatasetGrid` paths | **CONFIRMED — all six** | `control-initialization.md:11`, `error-handling.md:11-12`, `theme-management.md:11-12`, `fluent-v9-modern-theming.md:14`, `fluent-v9-canvas-vs-mda-disabled.md:12`, `ui/fluent-v9-theming.md:12` — each names the dead path as its primary exemplar |
| 5.4 `pcf-build-scaffold.md:213` cites retired AssociationResolver | **CONFIRMED** | Verbatim reference present; `AssociationResolver/` absent from pcf tree |
| 5.5 ADR-012 `FindSimilarDialog` rows + `dist/components/FindSimilarDialog` recipe | **CONFIRMED** | :151,178,209,213,216 as claimed; post-#714 the dist path is `FindSimilarViewer` — the copy-pasteable import cannot resolve |
| 5.6 `SPAARKE-REPOSITORY-ARCHITECTURE.md:90-94` PCF catalog | **CONFIRMED** | 4 of 5 listed controls do not exist (only ThemeEnforcer real) |
| 5.8 `MODAL-DECISION-CRITERIA.md` FindSimilarDialog folder link + deprecated FilePreviewDialog | **CONFIRMED** | Both present (~:46 and ~:85); `components/FindSimilarDialog/` does not exist |
| 5.13 `src/client/shared/CLAUDE.md` StatusBadge/usePagination example | **CONFIRMED** | `StatusBadge` appears in the shared tree only in that CLAUDE.md (the `EntityInfoWidget.tsx` hit is a local `getStatusBadgeColor` helper, not a component); no `usePagination` anywhere |
| 5.14 / 3.12 `src/client/pcf/CLAUDE.md` | **CONFIRMED** | `:10` lists UniversalDatasetGrid as live; `:59` imports fictional StatusBadge; `:279` `npm run build -- --mode production`; `:282` `pac pcf push` (while `:362` itself says NOT for releases) — internally contradictory and against root §12/AP-1 |
| 3.3 `pcf-safe.ts:8` misleading example | **CONFIRMED** | `FindSimilarDialog` appears only in the header comment; not exported |

**Sampled error rate for §5**: 10 of 17 rows verified, **10/10 accurate**. The doc sub-sweep looks trustworthy; remaining rows can be actioned with normal (not adversarial) review.

---

## SAFE TO DELETE — verified (executable list)

Each item survived all 10 channels. Barrel/config edits listed are part of the same change.

```
# 1.1 shared FindSimilar wizard family (5 files)
src/client/shared/Spaarke.UI.Components/src/components/FindSimilar/FindSimilarDialog.tsx
src/client/shared/Spaarke.UI.Components/src/components/FindSimilar/FindSimilarResultsStep.tsx
src/client/shared/Spaarke.UI.Components/src/components/FindSimilar/findSimilarService.ts
src/client/shared/Spaarke.UI.Components/src/components/FindSimilar/findSimilarTypes.ts
src/client/shared/Spaarke.UI.Components/src/components/FindSimilar/index.ts

# 1.2 LW Playbook fifth chain (9 files)
src/solutions/LegalWorkspace/src/components/Playbook/PlaybookCardGrid.tsx
src/solutions/LegalWorkspace/src/components/Playbook/ScopeConfigurator.tsx
src/solutions/LegalWorkspace/src/components/Playbook/ScopeList.tsx
src/solutions/LegalWorkspace/src/components/Playbook/DocumentUploadStep.tsx
src/solutions/LegalWorkspace/src/components/Playbook/FollowUpActionsStep.tsx
src/solutions/LegalWorkspace/src/components/Playbook/analysisService.ts
src/solutions/LegalWorkspace/src/components/Playbook/playbookService.ts
src/solutions/LegalWorkspace/src/components/Playbook/types.ts
src/solutions/LegalWorkspace/src/components/Playbook/index.ts

# …and the 3 CreateMatter files orphaned BY that deletion (only in the same commit;
#   they are live until Playbook goes):
src/solutions/LegalWorkspace/src/components/CreateMatter/FileUploadZone.tsx
src/solutions/LegalWorkspace/src/components/CreateMatter/UploadedFileList.tsx
src/solutions/LegalWorkspace/src/components/CreateMatter/wizardTypes.ts

# 3.1 LW Wizard re-export shims (5 files, zero importers post-144ef43c4)
src/solutions/LegalWorkspace/src/components/Wizard/index.ts
src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx
src/solutions/LegalWorkspace/src/components/Wizard/wizardShellReducer.ts
src/solutions/LegalWorkspace/src/components/Wizard/wizardShellTypes.ts
src/solutions/LegalWorkspace/src/components/Wizard/WizardSuccessScreen.tsx

# 1.4 test harness (alone — NOT SprkChatBridge, see DO-NOT-DELETE)
src/client/shared/Spaarke.UI.Components/src/__test-harness__/StreamingWriteHarness.tsx

# 1.5 VisualHost deprecated configs
src/client/pcf/VisualHost/control/configurations/matterMainCards.ts
src/client/pcf/VisualHost/control/configurations/matterReportCardTrends.ts

# 1.6 GradeMetricCard (+ remove barrel line Spaarke.Visuals/src/components/index.ts:26)
src/client/shared/Spaarke.Visuals/src/components/GradeMetricCard.tsx

# 1.7 seven DatasetGrid-era hooks + their 2 tests
src/client/shared/Spaarke.UI.Components/src/hooks/useDatasetMode.ts
src/client/shared/Spaarke.UI.Components/src/hooks/useHeadlessMode.ts
src/client/shared/Spaarke.UI.Components/src/hooks/useVirtualization.ts
src/client/shared/Spaarke.UI.Components/src/hooks/useEntityTypeConfig.ts
src/client/shared/Spaarke.UI.Components/src/hooks/useDirtyFields.ts
src/client/shared/Spaarke.UI.Components/src/hooks/useOptimisticSave.ts
src/client/shared/Spaarke.UI.Components/src/hooks/useWriteMode.ts
src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useVirtualization.test.ts
src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useWriteMode.test.ts
#   + remove their entries from src/hooks/index.ts, src/types/index.ts:40-41,
#     and jest.config.js:11 (leave the useKeyboardShortcuts line — that hook is LIVE)

# 1.8 + 6A.1f DocumentRelationshipViewer code page dead visualizations
src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipNetwork.tsx
src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipTimeline.tsx
src/client/code-pages/DocumentRelationshipViewer/src/components/NodeActionBar.tsx

# 6A.1 zombie auth shims (12 files)
src/client/code-pages/PlaybookBuilder/src/services/authService.ts
src/client/code-pages/PlaybookBuilder/src/config/msalConfig.ts
src/client/code-pages/DocumentRelationshipViewer/src/services/auth/MsalAuthProvider.ts
src/client/code-pages/DocumentRelationshipViewer/src/services/auth/msalConfig.ts
src/client/code-pages/DocumentRelationshipViewer/src/types/auth.ts
src/client/pcf/EmailProcessingMonitor/control/AuthService.ts
src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/auth/msalConfig.ts
src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/auth/index.ts
src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/services/auth/msalConfig.ts
src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/types/auth.ts

# 1.11 config fix (edit, not delete): src/client/pcf/tsconfig.json — remove stale excludes
#   "UniversalDatasetGrid/**", and ALSO "AiToolAgent/**", "AISummaryPanel/**",
#   "SourceDocumentViewer/**" (all four folders do not exist)
```

## DO NOT DELETE — refuted or unsure

| Item | Why it survived |
|---|---|
| `services/SprkChatBridge.ts` (+ its barrel exports) | **REFUTED-LIVE**: type-imported by live `SprkChat/types.ts:1020`, `SprkChat/hooks/useSelectionListener.ts:24`, `RichTextEditor/hooks/useDocumentStreamConsumer.ts:27-32`. Deleting it breaks the shared-lib build. Runtime-dormant, so a decommission is possible — but it is a refactor of SprkChat + RichTextEditor, not a file deletion |
| `SprkChatBridge.test.ts` / `.security.test.ts` / `.integration.test.ts` | Follow the module's fate; there are 3, not 1 |
| LW `CreateProject/CloseProjectDialog.tsx` + `closureService.ts` | LIVE (`WorkspaceGrid.tsx:56` lazy + `:1034` render). Its trigger (`__SPAARKE_OPEN_CLOSE_PROJECT__`) has zero in-repo callers, but POML task 062 exposed it explicitly "for ribbon command integration" — an org-side ribbon caller is plausible. **Environment query required before any conclusion** |
| Shared `CreateProjectWizard/CloseProjectDialog.tsx` + `closureService.ts` twins | Zero external importers CONFIRMED (2.2), but the consolidation direction (which copy survives) is an owner decision entangled with 4.6; both copies call the same live route |
| `CreateMatter/matterService.ts` + `formTypes.ts` | LIVE via `FilePreview/FilePreviewDialog.tsx:18` ← `DocumentCard.tsx` |
| `WizardRegistry/PlaceholderWizard.tsx` | Dead but documented deliberate retention (`wizardRegistry.ts:109-119`) — leave-with-reason |
| `sprk_openSprkChatPane.js` and the other 11 non-XML-referenced webresources | Liveness is only INFERABLE from the repo; form handlers/ribbons live org-side. Environment query before deletion |
| DocumentRelationshipViewer PCF (6A.5) | Retirement gated on a deployed-binding check (`customizations.xml` may bind it); agree with the sweep's gate |
| `hooks/useKeyboardShortcuts`, `useAiPrefill`, `useForceSimulation`, `useTwoPanelLayout`, `useAiSummary`, `useSseStream`, RecordHeader hooks, `toolbarLaunchDefaults` | LIVE siblings of the 1.7 family — verified consumers exist (PageChrome, wizard steps, DocumentGraph, SmartTodo alias, Notepad, DocumentUploadWizard, RecordHeader/MatterHeader PCFs). Do not over-reach when deleting the 7 |

---

## ERROR-RATE SUMMARY

**~48 distinct claims adversarially checked** (Task 1 counted as one compound claim; R1–R17 individually; §4 six; §6A eight; §5 ten; §7.2 three).

| Outcome | Count | Items |
|---|---|---|
| **CONFIRMED as stated** | 40 | 144ef43c4 safety; 1.1, 1.2, 1.4 (file), 1.5, 1.6, 1.7, 1.8, 1.9, 1.10, 3.1, 6A.1×6, R1–R17 route absences, 4.2, 4.4, 4.5, 4.6 (as-stated), 6A.2×3, 6A.3 emailactions, 7.2 same-route, §5×10, 3.3 |
| **REFUTED / materially corrected** | 3 | **1.3 SprkChatBridge** (deletion prescription would break the build — live type imports missed); **4.1** (fake button real, but "no server call at all"/"cannot be wired" halves false — bulk delete exists and is used by the sibling grid); **7.2b** duplicate-registration worry (resolved: no duplicate; the extension method is simply never called) |
| **CONFIRMED but incomplete (sweep undercounted)** | 4 | 1.11 (4 stale tsconfig excludes, not 1); 4.3 (5 `fetch.bind` sites, not 3); 1.3/1.4 (3 bridge tests, not 1); **R-extra: `bffUploadServiceAdapter` live 404 on shipped external-spa `/upload` page — a shipped-broken-feature the sweep missed entirely** |
| **UNSURE (correctly so; needs environment query)** | 3 | R4 reachability (EPM PCF binding), 4.6 org-side ribbon caller, 6A.5 PCF form binding |

**Calibration for the owner**: the sweep's *dead-code* claims are highly reliable (1 refutation in ~20 — but that one, 1.3, would have broken the shared-lib build, vindicating the adversarial pass). Its *route-absence* claims were 17/17 correct, though reachability severity was under-differentiated and one live mismatch was missed. Its *behavioural* (§4) claims are directionally right but prone to over-broad framing (4.1) and undercounting (4.3) — re-derive counts before acting, exactly as `DocumentsEndpoints.cs`'s own note warns: "Re-derive counts; never inherit them."
