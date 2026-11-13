# Package reviews — the 9 grandfathered shared packages (task 030, FR-08)

> Date: 2026-11-13. Branch `work/reuse-governance-tightening-r1` (master merged `265d426d3c`).
> Bar: ADR-012's three tests, all of which must hold to sanction (`.claude/adr/ADR-012-shared-components.md`): **T1** distinct contract (a different feature area alone is not one), **T2** quarantine-worthy dependency, **T3** cross-surface reuse (2+ of PCF / code page / SPA / add-in).
> Evidence: three `code-mapper` inventories (sonnet, low), re-verified from `package.json` files and import sites on 2026-11-13, plus main-session checks of each candidate defect. Recommendations are the main session's judgment. Design §6.2 hypotheses were starting points; two are overturned (feature-area packages; see §2–§4).
> **Owner gate**: tasks 031–034 do not start until each section's *Owner decision* is filled in.

## Decision summary

| # | Package | Recommendation | Hypothesis (design §6.2) | Executed in r1? |
|---|---|---|---|---|
| 1 | `@spaarke/sdap-client` | **Consolidate** into `@spaarke/ui-components` | Sanction or consolidate | No — filed (task 034) |
| 2 | `@spaarke/smart-todo-components` | **Sanction via one ADR-012 amendment** for Pattern D feature packages | Consolidate — **overturned** | No — filed (task 034) |
| 3 | `@spaarke/communication-components` | **Sanction** under the same amendment | Consolidate — **overturned** | No — filed (task 034) |
| 4 | `@spaarke/daily-briefing-components` | **Sanction** under the same amendment | Consolidate — **overturned** | No — filed (task 034) |
| 5 | `@spaarke/compose-components` | **Sanction** (TipTap quarantine) | Sanction | No — filed (task 034) |
| 6 | `@spaarke/document-operations` | **Consolidate** into `@spaarke/ui-components` | Consolidate | **Yes — task 033** |
| 7 | `@spaarke/ai-outputs` | **Consolidate** into `@spaarke/ai-widgets` | Consolidate | No — filed (task 034) |
| 8 | `@spaarke/ai-context` | **Consolidate** (types) into `@spaarke/ai-widgets`; retire the package | Retire or consolidate | **Yes — task 031** |
| 9 | `@spaarke/legal-workspace` | **Retire** the facade; SpaarkeAi depends on the LegalWorkspace solution directly | Retire | **Yes — task 032** |

The three r1-scoped packages (6, 8, 9) get the action the owner scoped (consolidate / retire). Nothing recommends sanction for any of them, so the task's escalation trigger does not fire.

## Findings across all packages (NFR-10)

No finding is a confirmed user-visible defect, so nothing was escalated mid-task. One candidate was checked and ruled out as user-visible: the drifted `provenance.ts` in `src/client/code-pages/CommunicationPage`. That code page is a prototype that no Dataverse solution includes, and no code imports it (email-communication-solution-r5 `notes/task-020-connections-layer1-extraction.md`). Severity: **High** = wrong behaviour a user can see; **Medium** = the same capability implemented more than once, with a behaviour or contract difference or a real chance of drift, or an ADR-012 MUST NOT breach; **Low** = stale configuration or hygiene.

| ID | Sev | Package(s) | Finding | Where |
|---|---|---|---|---|
| P-1 | Medium | document-operations | Open-in-desktop / open-links is implemented **six** ways with different behaviour. The shared hook falls back to `webUrl` when there is no `desktopUrl`; others set `window.location.href`, call `window.open(…,'_self')`, or call their own BFF client. | `Spaarke.DocumentOperations/src/hooks/useDocumentActions.ts:114`; `solutions/LegalWorkspace/src/services/DocumentApiService.ts:69`, `components/RecordCards/DocumentCard.tsx:167`, `components/FilePreview/FilePreviewDialog.tsx:88`; `code-pages/DocumentRelationshipViewer/src/App.tsx:372-378`; `Spaarke.Communication.Components/src/logic/attachments/AttachmentApiService.ts:76`; `pcf/SpeDocumentViewer/control/BffClient.ts:267`; `pcf/SemanticSearchControl/.../SemanticSearchApiService.ts:313` |
| P-2 | Medium | sdap-client, document-operations | Delete and download reach the BFF through two route families: `/api/obo/drives/...` (sdap-client) and `/api/documents/{id}/...` (document-operations). Whether the two give the same result for the same user and document was not verified here. | `Spaarke.SdapClient/src/operations/DeleteOperation.ts:22`, `DownloadOperation.ts:22`; `useDocumentActions.ts:119,181` |
| P-3 | Medium | communication-components | The regarding-write logic exists twice: `ConnectionsWriteHandler` (package) and `ResolverWriteHandler` (RegardingResolver PCF). Both have their own `discoverHostNavProps`, `resolveAllowedCatalog` and `applyRegardingSelection`. They differ by design (Connections files through the BFF `PATCH /api/communications/{id}/filing`; the PCF writes through `webApi` and stamps core ancestors client-side, FR-26), but the shared parts are parallel copies. The weekly duplication sweep (task 020) reports this pair. | `Spaarke.Communication.Components/src/logic/connections/ConnectionsWriteHandler.ts:141,219,268`; `pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts` |
| P-4 | Medium | daily-briefing-components (+6 others) | Nav-prop discovery: `useInlineTodoCreate` has another private copy (same-origin Dataverse metadata fetch). It is one of the seven copies recorded in `notes/reuse-scan-dry-run.md`; none uses the shared resolver. | `Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts:151-165` |
| P-5 | Medium | ai-context | `@spaarke/ai-widgets` imports `@spaarke/ai-context` in 3 files but does not list it in its `package.json`. It resolves only through tsconfig/jest path aliases, so a build of ai-widgets on its own fails to link it. | `Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx:50`; `Spaarke.AI.Widgets/package.json` |
| P-6 | Medium | ai-context, ai-outputs | Pane-event shapes are defined three times: `AiPaneEvent` (ai-context), `OutputPaneEvent` / `SourcePaneEvent` (ai-outputs), and a loose mirror in `SprkChat/types.ts`. `AiPaneEvent` lacks `safety_annotation`, according to a test comment. `IChatSession` is also defined twice. | `Spaarke.AI.Context/src/types/index.ts:45`, `types/chat.ts:15`; `Spaarke.AI.Outputs/src/types/index.ts:166-193`; `Spaarke.UI.Components/src/components/SprkChat/types.ts:178,200`; `Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx:194` |
| P-7 | Medium | ai-outputs | ai-widgets' registry metadata must equal the `OutputWidgetType` enum values by string, not by type import. A rename on one side compiles cleanly and breaks the lookup. | `Spaarke.AI.Widgets/src/registry/register-workspace-widgets.ts:67` |
| P-8 | Medium | smart-todo, communication, daily-briefing, compose | The packages contain hard-coded Dataverse entity / column literals. ADR-012 says shared components MUST NOT hard-code entity names as string literals. Approximate `sprk_*` counts: communication 317 / 47 files, smart-todo 72 / 18, daily-briefing 48 / 17, compose a few (`ComposeWorkspace.tsx:591-594,4003,4025,5419`). | as listed |
| P-9 | Low | sdap-client | The census says "18 consumers". Real direct importers: `@spaarke/ui-components` and the DocumentUploadWizard code page. 16 of 17 solution listings are never imported, and 6 Communication PCFs stub the package out (`'@spaarke/sdap-client$': false`). `TrackingFieldTrio/webpack.config.js:10` says the control uses it, but it has no import. | `Spaarke.UI.Components/src/services/EntityCreationService.ts:40`; `solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts:46` |
| P-10 | Low | several | Listings and aliases disagree: `external-spa` has a tsconfig path and lock entry for sdap-client but no listing; LegalWorkspace has an alias and lock entry for document-operations but no listing or import; smart-todo is listed but unused in 11 solutions; ai-outputs is listed but not imported by LegalWorkspace / SpaarkeAi; daily-briefing is listed but not imported by SpaarkeAi. | per package below |
| P-11 | Low | ai-outputs, ai-context, communication, document-operations, legal-workspace | Some tsconfig `paths` point at a `dist/` that is not built in a clean checkout, so the type check depends on build order: `Spaarke.AI.Widgets/tsconfig.json:25-26,29,31,37-38` and `Spaarke.Compose.Components/tsconfig.json:27-28`. The `legal-workspace` entry (`:29`) targets `solutions/LegalWorkspace/src/index.d.ts`, which does not exist; ai-widgets never imports that package. | as listed |
| P-12 | Low | communication-components | The prototype code page `src/client/code-pages/CommunicationPage` (not deployed, not imported) keeps an older copy of `provenance.ts` without `RecordNameMatch`. It is dead code. | `code-pages/CommunicationPage/src/components/provenance.ts` |
| P-13 | Low | communication-components | `@microsoft/signalr` is a peer dependency, but nothing in the package imports it. | `Spaarke.Communication.Components/package.json` |
| P-14 | Low | compose-components | `mammoth` is pinned at ^1.8.0 while both consumers use ^1.12.0. The client read path was removed in r4.5, so it may be unused. One hard-coded colour sits in an HTML string. | `Spaarke.Compose.Components/package.json`; `src/.../reviewMemoFormatting.ts:297` |

Routing: P-5, P-6 (the ai-context part) and P-11 (ai-context, legal-workspace, document-operations entries) are fixed by tasks 031–033. Every other finding is carried, with its severity, by the GitHub issue that task 034 files for its package, or in P-1's case by its own issue, because it outlives the consolidation of document-operations.

---

## 1. `@spaarke/sdap-client` (`src/client/shared/Spaarke.SdapClient`)

**Evidence**
- **T1 distinct contract — fails.** It is a framework-free BFF client for upload sessions, drive items, download, delete and index-file; `authenticatedFetch` is injected. That is the abstracted-I/O service tier `@spaarke/ui-components` already sanctions ("services with abstracted dependencies", ADR-012 Service Architecture). The only shared importer is ui-components itself (`EntityCreationService`, `FileUploadService`).
- **T2 quarantine — fails.** It has zero runtime dependencies.
- **T3 cross-surface — weak.** Direct importers are one code page (DocumentUploadWizard) and ui-components; the other consumers reach it through ui-components. No PCF imports it, and 6 Communication PCFs explicitly stub it out.

**Consumer count (re-verified)**: 18 `package.json` listings (ui-components + 17 solutions). **2 real importers**: ui-components and DocumentUploadWizard.

**Findings**: P-2 (Medium), P-9 (Low), P-10 (Low).

**Recommendation — Consolidate into `@spaarke/ui-components`** (`src/services/sdap/`). Exclude it from `pcf-safe.ts`, as today. **Migration outline**:
1. Move `src/` into ui-components.
2. Re-point ui-components' own imports and DocumentUploadWizard's.
3. Delete the 17 unused solution listings and the vite aliases.
4. Remove the 6 PCF webpack stubs once nothing resolves the name.
5. Remove the census row and the test entry.
6. Decide P-2 in the same project: one route family for delete and download.

This is out of r1 execution scope; task 034 files it.

**Owner decision**: _pending_

## 2. `@spaarke/smart-todo-components` (`src/client/shared/Spaarke.SmartTodo.Components`)

**Evidence**
- **T1 distinct contract — fails on feature area alone, but there is a real contract difference.** The package is domain-bound: it carries `sprk_*` entity schemas and BFF child-record writes (`SmartTodoWidget.tsx:805-811`). `@spaarke/ui-components` is the domain-agnostic library, which must take entity names through configurable maps (ADR-012 MUST NOT).
- **T2 quarantine — fails.** `@hello-pangea/dnd` is a type-only import and a peer dependency.
- **T3 cross-surface — passes (Pattern D).** The SmartTodo code page and the LegalWorkspace workspace section, which SpaarkeAi also hosts, both import it. No PCF imports it.

**Consumer count (re-verified)**: 15 listings, **2 real importing surfaces** (LegalWorkspace, SmartTodo); 11 listings are unused (P-10).

**Findings**:
- P-8 (Medium, 72 `sprk_*` literals).
- P-10 (Low).
- SmartTodo keeps its own 1,101-LOC `SmartToDo.tsx` beside the package's 754-LOC one while importing the package's `KanbanCard` / `Header` / `useKanbanColumns`. That is a likely duplicate, not diffed here (Medium; the sweep will report it if it is a copy).

**Recommendation — Sanction, through one ADR-012 amendment (Path B) for "Pattern D feature packages"** (covers §2–§4). Consolidating into ui-components, the §6.2 hypothesis, is rejected for two reasons:
- It would move about 31,000 LOC of entity-bound feature code, with about 437 `sprk_*` literals, into the package whose own MUST NOT forbids them. The consolidation would itself break ADR-012.
- It would grow the ui-components barrel that 17 PCFs import (24 barrel imports today), the bundle cost the design flagged.

**Proposed amendment**: a feature package is sanctioned when it is
- (a) consumed by 2+ surfaces (Pattern D),
- (b) domain-bound in a way ui-components may not be,
- (c) reached by PCFs only through deep sub-path imports, never the barrel.

It names these three packages and closes the list; a new one still needs an amendment. This changes ADR-012's bar, so it is the owner's call. The alternative is a strict reading (consolidate), with the costs above. Out of r1 execution scope; task 034 files it.

**Owner decision**: _pending_

## 3. `@spaarke/communication-components` (`src/client/shared/Spaarke.Communication.Components`)

**Evidence**
- **T1** is the same as §2: domain-bound communications workspace, connections, attachments, invoice and actions logic, with 317 `sprk_*` literals.
- **T2 — fails.** The `@microsoft/signalr` peer is unused (P-13).
- **T3 — passes strongly.** 9 surfaces:
  - 3 PCFs (CommunicationActions, CommunicationAttachments, CommunicationConnections), each through deep `logic/*` / `components/*` sub-paths;
  - the Office add-ins (`invoice-attachment-marking`, `logic/connections/provenance`; the Word Email tab uses the shared composer);
  - `@spaarke/ai-widgets`;
  - the CommunicationReconciliation, EmailPage, LegalWorkspace and SpaarkeAi code pages.

**Consumer count (re-verified)**: 8 listings, every one with real imports; 9 importing surfaces.

**Findings**: P-3 (Medium), P-8 (Medium), P-11 (Low), P-12 (Low), P-13 (Low).

**Recommendation — Sanction** under the §2 amendment. It already meets condition (c): the PCFs deep-import, as its header documents. This is the strongest case of the three. Out of r1 execution scope; task 034 files it with P-3 and P-12 (delete the prototype code page).

**Owner decision**: _pending_

## 4. `@spaarke/daily-briefing-components` (`src/client/shared/Spaarke.DailyBriefing.Components`)

**Evidence**
- **T1** is the same as §2, with 48 `sprk_*` literals.
- **T2 — fails.** It has no heavy dependency.
- **T3 — passes (Pattern D).** The DailyBriefing code page and the LegalWorkspace section both import it; SpaarkeAi hosts it through LegalWorkspace. No PCF imports it.

**Consumer count (re-verified)**: 3 listings, **2 real importers** (DailyBriefing, LegalWorkspace); the SpaarkeAi listing is unused.

**Findings**: P-4 (Medium), P-8 (Medium), P-10 (Low).

**Recommendation — Sanction** under the §2 amendment. Out of r1 execution scope; task 034 files it with P-4: replace the private nav-prop discovery with the shared resolver.

**Owner decision**: _pending_

## 5. `@spaarke/compose-components` (`src/client/shared/Spaarke.Compose.Components`)

**Evidence**
- **T1 distinct contract — passes.** It is the Compose document-editing model: a TipTap/ProseMirror document, the docx bridge, checkout / heartbeat lifecycle, comment threads and re-anchoring. That is a contract, not just a feature area, and no other package implements it; `docxBridge.ts` exists only here.
- **T2 quarantine — passes.** 15 `@tiptap/*` packages (plus ProseMirror) are exactly the case ADR-012 T2 describes. Only SpaarkeAi and LegalWorkspace declare TipTap; ui-components (and through it the PCFs) does not.
- **T3 cross-surface — passes narrowly.** It has 2 code-page surfaces (SpaarkeAi with about 51 imports; LegalWorkspace's `composeEditor.registration.ts:80`). The package is marked not PCF-safe, by design.

**Consumer count (re-verified)**: 2 listings, 2 importing surfaces.

**Findings**: P-8 (Medium, few), P-11 (Low), P-14 (Low).

**Recommendation — Sanction** (ADR-012 amendment naming the editing-model contract and the TipTap quarantine). This also settles capability-index row 30 ("two answers" today). Out of r1 execution scope; task 034 files it.

**Owner decision**: _pending_

## 6. `@spaarke/document-operations` (`src/client/shared/Spaarke.DocumentOperations`)

**Evidence**
- **T1 distinct contract — fails.** It holds one hook, `useDocumentActions` (open in web / desktop, download, delete, email link, send to index), which is abstracted I/O through `authenticatedFetch`. That is the same contract as ui-components, which already depends on `@spaarke/auth` (6 import sites).
- **T2 quarantine — fails.** Its only dependency is `@spaarke/auth`.
- **T3 cross-surface — passes.** It is used by the SemanticSearch code page (`App.tsx:53`) and by `@spaarke/compose-components` (and through it SpaarkeAi).

**Consumer count (re-verified)**: 3 listings (SpaarkeAi, SemanticSearch, Compose.Components), 2 real importers (SemanticSearch, Compose.Components). The SpaarkeAi listing is not imported directly; it is reached through Compose.

**Findings**: P-1 (Medium), P-2 (Medium), P-10 (Low, the LegalWorkspace alias), P-11 (Low, the Compose `dist` path).

**Recommendation — Consolidate into `@spaarke/ui-components`** as `src/hooks/useDocumentActions.ts`, exported from the main barrel and **not** from `pcf-safe.ts`. The hook uses no React 18/19-only API (checked), but it depends on browser download plumbing and `@spaarke/auth`, and no PCF needs it. **Migration outline (task 033)**:
1. Move the hook and its test.
2. Re-point SemanticSearch and Compose imports, and their package.json / tsconfig / vite / jest entries (including the Compose `dist` path, P-11, and the 6 Compose test `jest.mock` paths plus `ComposeWorkspace.openInWordError.test.tsx:85`'s relative `requireActual`).
3. Remove the SpaarkeAi and LegalWorkspace aliases.
4. Delete the package folder, census row and test entry.
5. Build SemanticSearch, SpaarkeAi and LegalWorkspace and run their tests.

P-1 is **not** fixed by the move. It needs its own issue: the five other implementations migrate to the hook surface by surface, and each surface's behaviour change is checked by hand.

**Owner decision**: _pending_

## 7. `@spaarke/ai-outputs` (`src/client/shared/Spaarke.AI.Outputs`)

**Evidence**
- **T1 distinct contract — partial.** It holds presentational output and source widgets (13 components and their `*Data` shapes); ai-widgets holds the registries, pane event bus, providers and adapters. The split mirrors visuals vs ui-components, but unlike `@spaarke/visuals` this package has one consumer and still carries its own registry types (P-7).
- **T2 quarantine — fails.** It has no runtime dependencies.
- **T3 cross-surface — fails directly.** Its only importer is `@spaarke/ai-widgets` (12 imports); the code pages reach it only through ai-widgets.

**Consumer count (re-verified)**: 3 listings (ai-widgets, LegalWorkspace, SpaarkeAi); **1 real importer** (ai-widgets).

**Findings**: P-6 (Medium), P-7 (Medium), P-10 (Low), P-11 (Low). Deep imports such as `@spaarke/ai-outputs/output-widgets/X` resolve only through consumer aliases, because the package has no `exports` map (Low).

**Recommendation — Consolidate into `@spaarke/ai-widgets`** (`src/outputs/`). In the same change, make the registry metadata import the enum (fixes P-7) and keep one pane-event definition (fixes the ai-outputs half of P-6). Out of r1 execution scope; task 034 files it.

**Owner decision**: _pending_

## 8. `@spaarke/ai-context` (`src/client/shared/Spaarke.AI.Context`)

**Evidence**
- **T1 — fails.** Since 2026-10-03 it is types only, 183 LOC: `EntityContext`, `EntityType`, `StreamingState`, `AiPaneEvent`, `StreamingCallbacks`, `IChatSession`. It has no tests.
- **T2 — fails.** It has no dependencies.
- **T3 — fails.** One code page (SpaarkeAi, 2 `import type` sites) plus ai-widgets (3 sites, unlisted, P-5).

**Consumer count (re-verified)**: 1 listing (SpaarkeAi); **2 importers** (SpaarkeAi, ai-widgets). The census "1 consumer" missed ai-widgets.

**Findings**: P-5 (Medium), P-6 (Medium), P-11 (Low).

**Recommendation — Consolidate the types into `@spaarke/ai-widgets` (`src/types/aiContext.ts`, exported from its barrel) and retire the package.** ai-widgets is its real shared consumer, and SpaarkeAi already depends on ai-widgets. This matches the owner-scoped "retire or consolidate". **Migration outline (task 031)**:
1. Move the types.
2. Re-point SpaarkeAi's 2 sites and ai-widgets' 3 sites (1 source, 2 tests).
3. Remove the SpaarkeAi listing and the aliases (vite, tsconfig, jest) in SpaarkeAi and ai-widgets.
4. Delete the package, census row and test entry.
5. Build SpaarkeAi and ai-widgets and run their tests.

This fixes P-5 and the ai-context half of P-11. P-6's single-definition fix belongs with the ai-outputs consolidation (§7), so this task does not touch `SprkChat`.

**Owner decision**: _pending_

## 9. `@spaarke/legal-workspace` (`src/client/shared/Spaarke.LegalWorkspace`)

**Evidence**
- **T1 — fails.** The package is an 87-line `src/index.ts` whose last line re-exports `../../../../solutions/LegalWorkspace/src/index.ts`. It has no contract of its own; it is a package-shaped door into a solution.
- **T2 — fails.** It has no dependencies of its own.
- **T3 — fails.** One consumer, the SpaarkeAi code page (`src/main.tsx:79`: `LegalWorkspaceApp`, `createLegalWorkspaceSectionRegistry`, `setLegalWorkspaceRuntimeConfig`).

**Why it exists.** dataset-grid-framework-r2 FR-10 (2026-07-02) replaced SpaarkeAi's raw Vite source alias into the solution (the "alias trap") with a package dependency. It chose *re-export* over *move* for blast radius; its own strategy note records the cost ("why does a shared package re-export from a solution?").

**Consumer count (re-verified)**: 1 listing, 1 importer (SpaarkeAi). ai-widgets has a tsconfig path to it (P-11) but no import.

**Findings**: P-11 (Low). The facade cannot build on its own (by design, per its description); Low.

**Recommendation — Retire the facade.** SpaarkeAi takes a direct `file:` dependency on the LegalWorkspace solution, whose `package.json` is already named `legal-workspace`. That solution gains an `exports` entry for `./src/index.ts`. This keeps FR-10's goal (a declared dependency, not a raw alias) without a "shared" package that one consumer uses. Moving the section registry into a shared package was considered and rejected: it is the larger change FR-10 already declined, and it serves one consumer. **Migration outline (task 032)**:
1. Add `exports` to `solutions/LegalWorkspace/package.json`.
2. Change SpaarkeAi's dependency and its `main.tsx:79` import.
3. Update SpaarkeAi `tsconfig.json:28-29` and the `vite.config.ts:61-66,155-157` path lists.
4. Remove the stray `Spaarke.AI.Widgets/tsconfig.json:29` path.
5. Delete the package, census row and test entry.
6. Build SpaarkeAi and LegalWorkspace and run their tests.

**Risk to check in 032**: Vite dedupe and transpile globs, which list both trees today.

**Owner decision**: _pending_
