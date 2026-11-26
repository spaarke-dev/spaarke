# Task 035 notes: delete the client mapping engine, add the guard, close the client search-index sends

Branch `task/fm-035` (cut from `41651cece7`). Rigor FULL. Spec FR-22, FR-40 (UAC-r2 condition).

## Part A: what was deleted and what was kept

Deleted (all four files): `Spaarke.UI.Components/src/services/FieldMappingService.ts` (exported only the apply function and its args type),
`services/FieldMappingHandler.ts` (handler, factory, config and result types), `types/FieldMappingTypes.ts` (`FieldMappingType`,
`FieldMappingTypes`, `FieldMappingFieldType`, `FieldMappingCompatibilityMode`, `FieldMappingSyncMode`, `IFieldMappingRule`,
`IFieldMappingProfile`, `IMappingResult`), `services/__tests__/FieldMappingService.test.ts` (21 `it` blocks). Barrel exports and the stale
comments in `services/index.ts` and `types/index.ts` are removed; no alias, no empty re-export.

Importer evidence (re-grepped before deleting, `git grep` over `src/`): no non-test file imported any mapping-only symbol except the two
barrels; the only test importers were the five wizard tests below and the deleted engine test. No STOP condition.

Helpers the engine imported, all KEPT (other users exist): `discoverNavProps` / `findNavProp` / `cleanGuid` (`PolymorphicResolverService`:
RegardingResolver PCF, `applyResolverFields`, several PCFs and code pages), `isApiError` (`utils/thrownFetchError`: ~15 consumers),
`getXrmPage` (`utils/xrmContext`: MatterHeader, RecordHeader, `useRecordHeaderFields`, `OptionSetField`). `createLogger` stays.
Nothing became unused, so nothing else was deleted.

Wizard tests (`eventService.fieldMapping`, `matterService.fieldMapping`, `projectService.fieldMapping`, `todoService.fieldMapping`,
`workAssignmentService.payload`): removed the import, the `jest.mock` of the deleted module, the "never called" assertions, the
`/applyFieldMappings/` regexes and the `mockReset` line; retitled the tests and header comments that named the token. Kept: payload
shape, "no field-mapping profile is read" (`/field-mappings/` URL scan), `/FieldMappingService/` source regex, BU-cascade-not-written
assertions. The token no longer appears in any of them.

Comments reworded (comment-only, no behaviour change): `useRecordHeaderFields.ts`, `utils/xrmContext.ts` (two), `utils/__tests__/xrmContext.test.ts`,
`RegardingResolverApp.tsx`, BFF `CreateTimeFieldMapping.cs`, `FieldMappingPushLookups.cs` (two), `ExternalDataService.cs`,
`TypeCompatibilityValidator.cs` (two; it cited `FieldMappingTypes.ts`), `QuickCreateSourceAccessFilter.cs` ("client engine never had that exposure"),
`WorkAssignmentCreateLiveTests.cs` (one summary line).

NOT edited, deviation: `src/client/webresources/js/sprk_todo_regarding_presave.js:28`. It names `AssociationResolver/handlers/FieldMappingHandler.ts`
(a retired PCF's file, not the deleted UIC one) and does not contain the `applyFieldMappings` token. Rewording it broke
`WebResourceMirrorParityGuardTests` (the SpaarkeMaster export under `src/dataverse/solutions/SpaarkeMaster/WebResources/` must match), so I reverted
it. If the comment is to be fixed, the mirror must be re-exported in the same PR.

Docs: `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` (overview, "current state" note, inventory, VisualHost / interlock text, Data Flow steps 1-6, Integration Points,
Design Decisions, Constraints: client-engine passages rewritten or removed; header dates updated), `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §4.2
(row marked deleted), `testing-and-code-quality.md` (illustrative violation reworded). Not changed and not verified: the "Columns read but unused" table
still says "not referenced by the engine" about the server engine; I changed only the client-engine references in it.

Remaining hits of the token (all outside what the guard scans or deliberately left):
- built artifact `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_spaarkeai` (a bundle of the SpaarkeAi code page: regenerated at the next build/export);
- `.claude/skills/{adr-aware,adr-check,code-review,spaarke-conventions}` (cannot be written by a sub-agent; the main session should reword them: adr-check's
  and code-review's grep lines now find nothing, which is correct, `spaarke-conventions/SKILL.md:234` shows the deleted call as an anti-pattern example);
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:104` (names it as deleted) and project notes under `projects/**` (history).

### ADR-038 Amendment A3 evidence for deleting `services/__tests__/FieldMappingService.test.ts`
It is not under a KEEP path (client jest, not `tests/**`), but the evidence is recorded anyway:
1. Deletion named: that file, 21 `it` blocks, subject `FieldMappingService.ts` (deleted in this task, not moved).
2. Behaviour not continuing elsewhere on the client: the engine is gone by decision (spec FR-22, owner Q4: no client preview).
3. The behaviour lives on the server and is pinned there: `tests/unit/domain/Dataverse/FieldMapping/CreateTimeFieldMappingTests.cs` (Copy / Default / Concat /
   Template, never-throw to warnings, no source==target guard) and the `RecordCreationServiceMappingSeamTests` seam family.
4. Retirement test for the removed capability: `noClientFieldMapping.test.ts` (below) fails if the entry point returns; the five wizard tests still assert no profile read.
5. No dependent tests: `git grep` shows no other file imported the deleted module after the five wizard tests were edited.
6. Verification at code-review (task-execute Step 9.5).

## Part B: the guard

Existing scans checked first: `src/__tests__/wizardLookupPatternGuard.test.ts`, `wizardEmailUploadCleanupGuard.test.ts`, `fieldMappingPush.opensFamily.test.ts`
and `utils/__tests__/xrmCapabilityUsage.guard.test.ts` each scan one package folder for a different rule. None walks the whole repository `src/`
or looks for this identifier, so extending one would mix rules; a new file `src/__tests__/architecture/noClientFieldMapping.test.ts` was the fit.

Design: `findForbidden(files)` (pure) is used by the real scan and by the controls. `resolveRepoSrc()` resolves `<repo>/src` from `__dirname` (6 levels up), and
throws unless the directory is named `src` and holds `client/shared/Spaarke.UI.Components/package.json`; a test proves it throws from a wrong start. The real scan
walks `.ts` / `.tsx`, skips `node_modules`, `dist`, `out`, `bin`, `obj` and dot-directories, does not follow symlinks, and must read at least 500 files (it reads ~3,700)
and must include the guard's own file. Token built from pieces (`'apply' + 'FieldMappings'`), so the file has no literal hit and no self-exclusion. Whole-word:
`(?<![A-Za-z0-9_$])token(?![A-Za-z0-9_$])`: `applyFieldMapping` (singular), `applyFieldMappingsX`, `myapplyFieldMappings` and `IApplyFieldMappingsArgs` do not hit; a hit in a comment
or string does. The scan is one walk in `beforeAll` (first cold read took ~54 s on this Windows machine, ~1 s warm).
Controls: must-fire (fixture with the token in an import and a call, plus a comment hit: both reported with path and line) and must-not-fire (clean fixture plus the near-misses).

Negative demonstration (token appended as a comment to `Spaarke.UI.Components/src/utils/guid.ts`, then reverted, not committed):
```
FAIL src/__tests__/architecture/noClientFieldMapping.test.ts
  × finds no use of the retired client mapping entry point anywhere under src/
    The client field-mapping engine was retired in field-mapping-server-write-path-r1 task 035 (FR-22); creation-time mapping is applied on the server
    (CreateTimeFieldMapping inside the record-creation service). The forbidden name appears in:
      src/client/shared/Spaarke.UI.Components/src/utils/guid.ts:46
    Create through the BFF with the host record as the mapping source instead of mapping on the client.
Tests: 1 failed, 4 passed, 5 total
```
After the revert: `Tests: 5 passed, 5 total`.

**Enforced by: `noClientFieldMapping.test.ts`.**

Does it run in CI? Not automatically as a PR gate. UIC's jest suite runs only in `.github/workflows/client-tests.yml`, a nightly (07:00 UTC) plus manual-dispatch,
advisory baseline (continue-on-error, no `pull_request` trigger, deliberately outside tier 1 and 2). The blocking path for a UIC guard is an explicit step in
`ci-tier1-blocking.yml` job `xrm-capability-guard` (runs on any `src/client/**` or `src/solutions/**` change; it already runs `xrmCapabilityUsage.guard`, `pcfBundleReactRuntime.guard`
and `wizardLookupPatternGuard` this way). The new guard is NOT wired there (workflows were out of scope): add
`npx jest --ci src/__tests__/architecture/noClientFieldMapping.test.ts` as a fourth step to make it block PRs. Until then it fails only in the nightly run and locally.

## Part C: client search-index sends (FR-40)

- `services/sdap/types/index.ts` `IndexFileRequest.searchIndexName` removed (replaced by a one-line why-not comment); `services/sdap/operations/IndexFileOperation.ts` body no longer carries it.
  `Spaarke.SdapClient` does not exist in this repository; the SDAP client is `Spaarke.UI.Components/src/services/sdap`, which is what was changed.
- `EntityCreationService.indexUploadedFiles`: the `searchIndexName` parameter is gone; all four callers (CreateEventWizard, CreateProjectWizard, matterService, workAssignmentService) already passed four arguments.
- Deleted `applyDefaultSearchIndexName`, `applyUserBuDefaults` and the now-unused private `_hasExplicitValue` (no production caller: `git grep` found only tests). A tombstone comment says not to reintroduce them.
- `IUserBuCascadeDefaults`: `searchIndexName` / `searchIndexId` removed. `resolveUserBuDefaults` now selects only `sprk_containerid` from the BU (and `_businessunitid_value` from the user); returns `{ businessUnitId, containerId }`.
  Callers that use the container (Compose, CreateMatterWizard `main.tsx`, `InAppWizardHost.tsx`, `ComposeDirectWidget.tsx`) are unchanged and the Compose / AI.Widgets / DailyBriefing mocks (typed `any`) needed no edit.
- Tests: `EntityCreationService.cascade.test.ts` rewritten (deleted-helper cases removed; new: the BU read selects no index column and returns only `businessUnitId` / `containerId` even when the BU holds index values;
  `indexUploadedFiles` posts a body with exactly `documentId, driveId, fileName, itemId, parentEntity, tenantId`); `SdapApiClient.test.ts` gained `indexFile` (body has no `searchIndexName` even if the request object carries one);
  `projectService.test.ts` and `matterService.cascade.test.ts` lost the cases whose subject (a BU search index) no longer exists and the typed literals that named one.
- Final grep (`src/client` and `src/solutions`, excluding node_modules, dist, bundles, Semantic Search, `/Solution/`, `/WebResources/`, tests and mocks) for `sprk_searchindexname`, `AI_Search_Index`, `searchIndexName`; every hit is a comment or display read, none a create or index-file send:
  - `office-addins/shared/taskpane/hooks/useDocumentProfile.ts` (8, 29, 31, 50-51, 109-110, 147, 158, 192, 331): reads `sprk_searchindexname` of a document to display it; "never written by the add-in" (left alone per the brief).
  - `CreateMatterWizard.tsx:214-221` (JSDoc, rewritten here to say nothing reaches the payload), `matterService.ts:176,226`, `CreateProjectWizard.tsx:813`, `projectService.ts:266`, `reportCardService.ts:21`, `workAssignmentService.ts:373-374`, `EntityCreationService.ts:53-54,317`, `solutions/CreateMatterWizard/src/main.tsx:34` (comment, reworded): all comments saying the column is not written.
  - Tests and mocks not in the grep scope still use the strings only as negative assertions or raw BU mock fields (`workAssignmentService.cascade.test.ts`, `eventService.cascade.test.ts`, `createXrmEmailComposeHandlers.attachmentCreate.test.ts` which still passes two stale index members in an untyped mock).
  - Built bundles (`pcf/*/Solution/**/bundle.js`, `src/dataverse/...`) and Semantic Search are excluded by instruction; the bundles are regenerated at the next build.

## Part D: BFF log level (FR-40 / UAC-r2 condition)

- `Services/RecordCreation/RecordTableRules.cs` `LogSearchIndexDropped`: both `logger.LogInformation(` became `logger.LogWarning(` (the only two log sites for the event; `RecordCreationService.LogDroppedSearchIndex`,
  `DataverseCreateRecordHandler` and `DataverseDocumentsEndpoints` call this method); the summary now says Warning and that any entry means a client regression. Logged fields unchanged. `RecordCreationService.cs` comment did not mention a level: unchanged.
- `tests/integration/data-mutation/RecordOwnership/SearchIndexDefaultTests.cs:291-292`: asserts `LogLevel.Warning` with a new reason. No other test asserts this event's level (grep).
- Failing before: with `RecordTableRules.cs` restored to `41651cece7`, `SearchIndexDefaultTests.ADroppedIndexThatDiffers_...` fails ("Expected lines to contain only items matching ... Warning"); with the change it passes.
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. Publish-size delta not measured (main session).
- Filtered tests (`tests/unit/Sprk.Bff.Api.Tests`, which hosts the data-mutation and seam folders): `SearchIndexDefaultTests | SearchIndexGenericUpdatePathTests | SecureChildOwnershipAiToolTests | RecordCreationServiceMappingSeamTests`: 1180 passed, 0 failed.
- `dotnet test tests/Spaarke.ArchTests` (not `--no-build`): first run 2830 passed / 1 failed (`WebResourceMirrorParityGuardTests`, caused by my webresource comment edit, reverted as above); re-run of that class: 28 passed.

## Verification results

UIC jest, full suite (`npx jest --silent --maxWorkers=4`, ~12 min): 503 suites, 7578 tests passed; one suite (`CreateEventWizard.documentBind`) failed to load once with a Windows file-open error under load and passed alone (2/2). Guard suite: 5/5. Sdap + EntityCreationService + wizard folders re-run after the commit-time prettier pass: 17 suites, 129 tests, all passed. UIC: `npx tsc --noEmit` clean; `npm run build` ok (needs `Spaarke.Auth` built first: environment, not a regression).
Client lint ratchet (`node scripts/quality/client-lint/client-lint.mjs check --package ui-components`): ok, no baseline change needed.
Solution builds (`rm -rf dist node_modules/.vite .vite`, `npm install --legacy-peer-deps`, `npm run build`): CreateMatterWizard, CreateEventWizard, CreateProjectWizard, DocumentUploadWizard, SpaarkeAi all exit 0.
`Spaarke.Compose.Components` `tsc --noEmit`: clean (after building `Spaarke.AI.Widgets` dist, which it imports). `Spaarke.AI.Widgets` `tsc --noEmit`: errors only inside `../Spaarke.Communication.Components` (react / fluent not installed there) and none mention anything changed here: environment.

## Defects and observations (pre-existing unless noted)

1. `FieldMappingHandler.ts:175` selected `sprk_syncmode` from `sprk_fieldmappingprofile` (no such column): removed by the deletion (task 004's note).
2. `EmailComposer.lookupFocus.test.tsx` ("stops retrying when focus moves to another element outside the composer") failed once under a 116-suite parallel run (expected 1 call, got 2) and passed alone (14/14): timing-sensitive, unrelated.
3. `sprk_todo_regarding_presave.js:28` cites a file that does not exist (`pcf/AssociationResolver/handlers/FieldMappingHandler.ts`); fixing it needs the SpaarkeMaster mirror re-exported in the same change (mirror-parity arch test).
4. `.claude/skills/spaarke-conventions/SKILL.md:234`, `adr-aware/SKILL.md:151`, `adr-check/references/adr-validation-rules.md:72`, `code-review/references/review-checklist.md:124` still name the deleted function (sub-agents cannot write `.claude/`).
5. `createXrmEmailComposeHandlers.attachmentCreate.test.ts:41-42` still passes `searchIndexName` / `searchIndexId` into an untyped mock of `resolveUserBuDefaults`; harmless, could be trimmed.
6. `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` "Columns read but unused" rows claim the engine does not read `sprk_isrequired` / `sprk_compatibilitymode`; true of the retired client engine, not re-verified for the server engine.

## Main session: CI, review and gates (2026-11-25)

- **CI (enforcement ladder):** the UI library's jest runs on PRs only in the nightly advisory job, so the guard is added as a step of the blocking `ci-tier1-blocking.yml` job `xrm-capability-guard`, next to `wizardLookupPatternGuard` (89e46641f6).
  - That job's `xrm_guard` filter now also matches `src/**/*.ts` and `src/**/*.tsx`. Before, a change only to `src/dataverse/forms/**/*.ts` skipped the guard (review finding 2).
- **Enforced by:** `src/client/shared/Spaarke.UI.Components/src/__tests__/architecture/noClientFieldMapping.test.ts`, run by Tier 1 (blocking), the nightly client tests and locally.
- Observation 6 checked: the server engine (`Services/**`) reads neither `sprk_isrequired` nor `sprk_compatibilitymode`; only `FieldMappingEndpoints.cs` declares them. The doc row stays true.
- Observation 4: the four `.claude/skills` lines name `applyFieldMappings` as the pattern a reviewer must flag. They stay accurate, so they are unchanged.

### Review (Step 9.5): one adversarial pass, Opus

| # | Class | Finding | Outcome |
|---|---|---|---|
| 1 | F4 | Publish delta and CVE result not recorded | Fixed: recorded below |
| 2 | F2 | The CI job's paths filter missed `src/dataverse/forms/**/*.ts`, which the guard scans | Fixed: `src/**/*.ts` and `src/**/*.tsx` added to `xrm_guard` |
| 3 | F2 | `matterService.fieldMapping.test.ts` (two literals) and `projectService.fieldMapping.test.ts` still set the removed `searchIndexName`/`searchIndexId` on typed `IUserBuCascadeDefaults` literals (TS2353, hidden by transpile-only jest) | Fixed: members removed; the type now prevents what those probes checked. 3 suites, 38 tests pass |
| 4 | F2 | Stale pointers to the deleted engine: `.claude/patterns/CAPABILITY-INDEX.md` Row 5 overlap, `.claude/adr/ADR-024` component table, `docs/INDEX.md`, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-5 ("at Information … task 035 returns it to Warning") | Fixed |
| 5 | F3, pre-existing | Every matter create reads systemuser and businessunit and discards the result | Filed DI-044 / #3190 (wizard logic is outside 035) |
| 6 | K2 → fixed | G7 missed write verbs mid-name, aliases, named arguments, method groups and a new writer field; its must-fire control asserted only a count | Fixed: the detectors cover each shape, an unknown Dataverse-typed field fails, and the control asserts the exact 15 findings. 20/20 pass |
| 7 | K1 | The guard matches the name token only, skips `.js`, and the committed `sprk_spaarkeai` export still holds the token | Known limit by design (POML). The bundle is regenerated at the next SpaarkeMaster export |

No runtime defect was found: no live importer of a deleted symbol, all `indexUploadedFiles` callers pass four arguments, and `resolveUserBuDefaults` consumers read only `.containerId`.

### Gates

- **Publish delta:** master af4cfb4527 vs branch 89e46641f6 via `scripts/publish-delta.ps1`. Branch 43,632,971 bytes vs master 43,632,978 bytes = −7 bytes, 194 = 194 files, `Compress-Archive`, 41.61 MB total.
- **CVE:** `dotnet list package --vulnerable --include-transitive` reports none.
- **ArchTests** (no `--no-build`) on the merged branch: 2834 / 2834.
- The BFF change (log level) ships with the next batched BFF deploy. Client pages pick up the deletion at their next deploy; the deletion changes no runtime behaviour.
