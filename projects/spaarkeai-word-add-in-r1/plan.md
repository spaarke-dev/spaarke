# Implementation Plan — Spaarke Office Add-in (Word + Outlook) r1

> **Source**: [`spec.md`](spec.md) · **Design**: [`design.md`](design.md)
> **Created**: 2026-09-04 by `/project-pipeline`
> **Branch**: `work/spaarkeai-word-add-in-r1`

---

## 1. Executive Summary

**Purpose** — Make the Spaarke Office add-in's save *good*, and turn the pane into a small useful window onto Spaarke while the user drafts in whatever tool they prefer. Fix the UAT-reported save defects, give the pane document identity, complete server-side record creation, and add a Find tab.

**Scope** — 20 functional requirements across the add-in client (`src/client/office-addins/**`), the BFF Office and Documents surfaces, and one authorization gap in the AI visualization surface. Outlook parity throughout. No new deployable; every BFF addition extends an existing route group.

**Estimated effort** — 34 tasks across 5 phases. Roughly 18–26 working days at one operator, less with wave parallelism. Phase 0 gates a meaningful fraction of Phases 1–3, so the estimate widens if spikes come back negative.

**Critical path** — `001 typecheck baseline` → `002 Spike-1 (document.url)` → `012 identity resolver` → `013 client identity wiring` → `023/024 version-save` → `026 record card` → `040 parity` → `042 deploy`. Spike-1 is the keystone: if `document.url` is unusable on Word desktop for SPE files, FR-01's primary path fails and FR-02's stamp becomes the only identity mechanism — which does not work for documents saved before this release.

---

## 2. Architecture Context

### Technology stack

| Layer | Stack |
|---|---|
| Add-in client | React 19 + `createRoot`, Fluent UI v9, webpack, TypeScript strict (`exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`) |
| Host abstraction | Office.js behind `IHostAdapter`; `HostAdapterFactory` (currently dead — see Risk R-4) |
| Auth | NAA via `@spaarke/auth` `OfficeNaaStrategy`, wrapped by `shared/services/AuthService.ts`. **No MSAL construction in this package** (ADR-028, arch-test enforced) |
| BFF | .NET 10 Minimal API, endpoint filters for authorization, Service Bus + `IJobHandler<T>` for async work |
| Data | Dataverse (`sprk_document`, `sprk_matter`, `sprk_project`, `sprk_todo`), SharePoint Embedded via `SpeFileStore`, Azure AI Search (`spaarke-files-index`, `spaarke-records-index`) |
| Deploy | Add-in → Azure Static Web App via `.github/workflows/deploy-office-addins.yml` (CI-only, **not** agent-run). BFF → `bff-deploy` skill. |

### Integration points

- `POST /api/office/save` — the save spine; async job + SSE progress
- `/api/documents/{id}/*` — read/share surface; the identity resolver extends this group
- `POST /api/ai/rag/send-to-index` — the existing OBO re-index route (FR-16 Run Index reuses this; **no new endpoint**)
- `GET /api/ai/visualization/related/{documentId}` — the `documentVector3072` cosine-KNN engine (FR-16 similarity)
- `POST /api/ai/search/records` — record search, query-text driven, with real per-row authorization
- `ContentDedupDetector` + `ComposeService.PromoteIfEphemeralAsync` — the shipped dedup layers

### Discovered resources

**ADRs** — from spec: ADR-001 (Minimal API), ADR-007 (`SpeFileStore` facade), ADR-008 (endpoint-filter authz), ADR-010 (DI minimalism), ADR-012 (shared component library), ADR-021 (Fluent v9 + dark mode), ADR-028 (Auth v2, secret-free), ADR-029 (publish hygiene + size ratchet), ADR-038 (testing strategy), ADR-044 (`cleanGuid` canonicalization), ADR-050 (canonical modal shell), ADR-051 (infinite lazy-scroll).

**Added during discovery** — **ADR-049** (Compose Shadow Document — *the* Word/OOXML ADR, amended 3×, governs the other `.docx` write path; missing from the spec's table), ADR-013 (`Services/Ai/PublicContracts/` facade discipline for any CRUD→AI call), ADR-004/ADR-036 (job contract for the async save spine), ADR-019 (ProblemDetails), ADR-024 (polymorphic regarding resolution).

> ⚠️ **There is no concise `ADR-038` in `.claude/adr/`** — the directory jumps 037→039. Task `<knowledge>` blocks must point at [`docs/adr/ADR-038-testing-strategy.md`](../../docs/adr/ADR-038-testing-strategy.md) or the load silently fails.

**Skills** — `office-addins-deploy`, `fluent-v9-component`, `bff-deploy`, `ui-test`, `code-review`, `adr-check`, `conflict-check`, `test-diet`, `dataverse-mcp-usage`, `spe-integration`, `ci-cd`, `project-defer-issue-tracking`.

**Patterns** — [`ai/indexing-pipeline.md`](../../.claude/patterns/ai/indexing-pipeline.md) (MUST pass `documentId`; lowercase GUIDs; `ChunksIndexed=0` is a failure), [`auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) (INV-1..INV-8), [`auth/spe-writer-identity-matching.md`](../../.claude/patterns/auth/spe-writer-identity-matching.md) (**decides sync-OBO vs Service-Bus-MI dispatch for post-upload indexing**), [`ui/fluent-v9-host-visual-fit.md`](../../.claude/patterns/ui/fluent-v9-host-visual-fit.md) (explicitly covers Office add-ins), [`ui/infinite-scroll-list.md`](../../.claude/patterns/ui/infinite-scroll-list.md) + [`ui/thin-scrollbar.md`](../../.claude/patterns/ui/thin-scrollbar.md), [`api/endpoint-filters.md`](../../.claude/patterns/api/endpoint-filters.md), [`api/endpoint-definition.md`](../../.claude/patterns/api/endpoint-definition.md), [`dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md).

**Canonical implementations to copy, not fork**

| Need | Reference implementation |
|---|---|
| BFF-backed record op from the pane | `OfficeService.CreateTodoAsync` (`Services/Office/OfficeService.cs:1499`) + `POST /api/office/todo` |
| Editable dedup (link/graduate) | `ComposeCreateOnSavePromoter.PromoteIfEphemeralAsync` (`Services/Compose/ComposeCreateOnSavePromoter.cs:76`), esp. `GraduateLinkedCopyIfDivergedAsync:116` |
| Real per-row search authorization | `RecordSearchAuthorizationFilter` + `RecordSearchEndpoints.AuthorizeRowsAsync:242` (the fail-closed forcing function at `:117-129`) |
| `.docx` byte extraction | `word/WordHostAdapter.getCompressedFile()` (`:171-220`) — the UAT-correct path |
| Unified JSON manifest | `outlook/manifest.json` (runtimes + ribbons + `webApplicationInfo`) |
| Index tracking writeback | `PostUploadIndexingEnqueuer.WriteSearchIndexTrackingAsync:358` |
| Client field mapping | `FieldMappingService.applyFieldMappings` (`Spaarke.UI.Components/src/services/FieldMappingService.ts:110`) |
| GUID canonicalization | `cleanGuid` from `@spaarke/ui-components` (`services/PolymorphicResolverService.ts`) |

**Scripts** — `scripts/Deploy-OfficeAddins.ps1` (dev iteration only), `scripts/Deploy-BffApi.ps1` (emits the package-size line for ADR-029), `scripts/Validate-TaskPoml.ps1`, `scripts/ai-search/Deploy-AllIndexes.ps1`, `scripts/Register-EntraAppRegistrations.ps1` (NFR-09).

### Dataverse schema — validated live, no gaps

All spec-claimed fields exist on `sprk_document`. `sprk_searchindexed` is a **BIT** (true/false/null gives FR-16's three states). The two-slot association model FR-09 must respect (`sprk_matter` + `sprk_relatedmatter`; `sprk_project` + `sprk_relatedproject`) is **already in schema**, alongside ten further `sprk_related*` lookups. **No schema-creation task is needed.**

---

## 3. Findings that modify the spec

Discovery verified six spec assumptions as false or mis-sized. Each is bound to the task that owns it; none is silently absorbed.

| ID | Finding | Effect on plan |
|---|---|---|
| **F-a** | **FR-12's premise does not hold.** The shipped collision handling lives on `PUT /api/obo/.../files/{name}` (`OBOEndpoints.cs:558` → default `Fail`), surfaced by `@spaarke/sdap-client` `UploadOperation.put():138` → `UploadNameConflictError`. **The add-in does not use that path and does not depend on `@spaarke/sdap-client` at all** — it saves JSON+base64 via `POST /api/office/save` → `OfficeService.SaveAsync` → job queue → `OfficeStorageUploader`. There may be no typed conflict error to surface. | **Spike-4 (task 005)** resolves it before any FR-12 code. Outcome may trigger a CLAUDE.md §6.5 path decision. Task 025 is authored as gated. |
| **F-b** | **FR-16's engine has no per-row authorization.** `VisualizationAuthorizationFilter:56` authorizes only the *source* document; result rows are trimmed by `tenantId` alone. `POST /related-from-content` has **no auth filter at all**. `VisualizationEndpoints.cs` is absent from the `RouteAuthorizationGuardTests` governed-file census. This is exactly the UAC-r2 442-document failure mode NFR-02 exists to prevent. | **Task 032 (authorization hardening) gates task 033 (the view).** NFR-02's negative test is a gate, not a checkbox. |
| **F-c** | **No single endpoint returns similar documents *and* records.** Vector similarity from a source document → `/api/ai/visualization/related/{documentId}` (documents only; Matter/Project appear as parent *hub nodes* from Dataverse lookups, not vector matches). `POST /api/ai/search/records` is query-**text** driven and accepts no source document. | Task 034 makes an explicit bridge decision (text-seeded second call vs. documents-only in r1) and records it. |
| **F-d** | **FR-11's server hook is inert.** `ExistingDocumentId` (`SaveRequest.cs:369`), `IsNewVersion` (:364), `VersionComment` (:375) exist, but the only server reference is inside the idempotency-key string (`OfficeService.cs:620`) and no client sends them. Declared, unimplemented, both sides. | FR-11 is two tasks (023 server, 024 client), not one wiring change. |
| **F-e** | **FR-04 as written would regress the `.docx` save.** `HostAdapterFactory.registerAdapter()` has zero call sites — the registry is empty and `create()` always throws `INVALID_HOST`. Both taskpanes `new` their adapter directly. And `shared/adapters/WordAdapter.ts:221-270` still uses the **known-broken `body.getOoxml()`** path that `word/WordHostAdapter.ts:126-134` documents as the 2026-09-03 UAT defect. | Task 010 fixes the order: **port `getCompressedFile()` into `WordAdapter` → activate the factory → delete the duplicate.** Prescriptive step mode. |
| **F-f** | **`POST /api/office/save` has zero executing contract coverage.** Both its tests are `[Fact(Skip)]` (`OfficeEndpointsContractTests.cs:48,82`); 13 of 22 tests in the file are skipped. | Task 016 owns the un-skip; every save-path task carries a coverage obligation (ADR-038: new endpoint ⇒ contract test). |
| **F-g** | **`sprk_event` does not exist on `sprk_document`, but shipped code writes it.** Live metadata (MCP, 2026-09-04, corroborated by the maker-portal Columns view) shows **four** direct lookups — `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment` — and **twelve** `sprk_related*` lookups. There is no direct `sprk_event`; the Event column is `sprk_relatedevent`. Yet `EntityAccessFilter.EntitySetByType:126-127` authorizes filing to an Event, `DocumentAssociationMap.TryApply` returns `true` for it, and `DataverseServiceClientImpl.cs:916` writes `document["sprk_event"]`. This violates the lockstep invariant (*"a type belongs in both maps or neither"*) that the coordination doc states and then declares `event` compliant with. The same source also calls `sprk_todo` "unmappable, needs a schema change first" — `sprk_relatedtodo` exists — and says `sprk_document` has no contact lookup — `sprk_relatedcontact` exists. | **Owned by `unified-access-control-r2`** (its 2026-09-03 Q4 widening introduced the `event` entry; `EntityAccessFilter` is its component). r1 does **not** fix it. Tasks 026 and 035 corrected so they do not inherit the false premise; 026 reads `sprk_relatedevent`, never `sprk_event`. Correction appended to `coordination-document-association-map-from-email-r2-2026-09-04.md` §0. Tracked in `notes/defer-issues.md`. |

**Two questions the spec left open are now answered:**

- **Run Index needs no new endpoint.** `POST /api/ai/rag/send-to-index` (`RagEndpoints.cs:119`) is JWT + tenant-filtered, runs **OBO**, takes `{DocumentIds, TenantId}`, and stamps `sprk_searchindexed`/`sprk_searchindexedon` at `:710-722`. Caveat: it resolves the index name via `GetDefaultIndexName()` and ignores per-record `sprk_searchindexname` — an in-handler fix, not a new route.
- **FR-13's halves are not symmetric.** `sprk_projectnumber` is the Project **primary name attribute**, and `projectService.ts` generates no number at all (unlike `matterService.ts:255-273`). Confirmed by sweep: **no server-side creation service with numbering exists anywhere.** Task 031 must state Project semantics explicitly rather than mirroring Matter.

---

## 4. Work Breakdown Structure

### Phase 0 — De-risk and baseline (tasks 001–008)

**Objective**: establish a true typecheck baseline and resolve four unknowns that gate downstream scope. Nothing in Phase 1+ should be sized until this closes.

| Task | Deliverable |
|---|---|
| 001 | `npm install --legacy-peer-deps --no-audit --no-fund` in `src/client/office-addins`; run `npm run typecheck`; record the **real** error count + per-file breakdown to `notes/typecheck-baseline.md` |
| 002 | **Spike-1** — `Office.context.document.url` shape for SPE files in **Word desktop** (documented for web only). The keystone for FR-01. |
| 003 | **Spike-2** — Office Dialog API for record open: (a) MDA form without framing refusal, or must we host a code page? (b) auth via `messageParent`? (c) does the form function at dialog size? (d) does a change propagate back? |
| 004 | **Spike-3** — is there any documented mechanism for a task pane to open the Copilot pane / a named agent? **Timeboxed**; a negative result closes FR-20. |
| 005 | **Spike-4 (F-a)** — does the add-in's save path share the shipped collision semantics? Trace `OfficeStorageUploader` → `SpeFileStore` and determine what `conflictBehavior` it uses. Produce a §6.5 path recommendation. |
| 006–008 | FR-18 typecheck-debt clearance, split by area after 001 sizes it: `shared/taskpane/**` · `shared/adapters` + `shared/services` · `word/**` + `outlook/**`. Includes the two known barrel defects (`ViewType`, `SaveOptions` re-exported but never defined). |

**Gate**: typecheck clean; four spike outcomes documented in `notes/spikes/`; FR-20 and FR-12 scope decided.

**Inputs**: clean worktree. **Outputs**: `notes/typecheck-baseline.md`, four spike reports, revised scope for FR-01/FR-10/FR-12/FR-20.

---

### Phase 1 — Foundation (tasks 010–016)

**Objective**: one Word adapter reached through the factory, a unified manifest, real document identity, and a tab shell that works in both hosts.

| Task | Deliverable |
|---|---|
| 010 | FR-04 adapter consolidation — **prescriptive order per F-e**: port `getCompressedFile()` into `WordAdapter`, register both adapters with `HostAdapterFactory`, route both taskpanes through it, delete `word/WordHostAdapter.ts`. Unit tests cover the `.docx` path. |
| 011 | FR-05 Word unified JSON manifest, following `outlook/manifest.json`. Adds the `WebApplicationInfo` equivalent Word currently lacks and reconciles the `WordApi 1.1` vs adapter-demands-1.3 mismatch. |
| 012 | FR-01 server — document-identity resolver extending `/api/documents`: base64url → Graph `/shares/u!{enc}/driveItem` → `driveId`+`itemId` → `sprk_document` via `sprk_graphitemid_uk`. Endpoint filter for authorization (ADR-008). |
| 013 | FR-01 client — `canGetDocumentUrl` capability + `getDocumentUrl()` on `IHostAdapter`; wire the resolver; identity threads into `App.savedContext`. |
| 014 | FR-02 server-side custom XML part stamp into the uploaded bytes, **forward-only** (owner decision — no retroactive rewrite). Never mutates the user's open document. |
| 015 | FR-03 tab shell — extend `NavigationTab`, enable `showNavigation` for Word, build the Save\|Find frame. `share`/`recent` stay hidden. |
| 016 | Contract coverage (**F-f**) — un-skip the two `POST /api/office/save` tests, add coverage for the new identity route. |

**Gate**: a Spaarke-sourced document is identified end-to-end; both hosts render both tabs; `/api/office/save` has executing tests.

---

### Phase 2 — Save flow (tasks 020–027)

**Objective**: close the UAT defects. Every task here is BFF-touching or save-path and carries publish-size + contract-test obligations.

| Task | Deliverable |
|---|---|
| 020 | FR-06 filename defaults to Document Name, editable in-pane via a pencil affordance; persists to `sprk_documentname` |
| 021 | FR-07 rename "Description" → "Profile"; populate `sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_documenttype`; render the non-complete `sprk_filesummarystatus` states rather than blank fields |
| 022 | FR-08 Generate Profile — `/api/office` trigger mirroring Compose's `refresh-profile`; overwrites without confirmation |
| 023 | FR-11 server (**F-d**) — make `ExistingDocumentId` / `IsNewVersion` / `VersionComment` real in `OfficeService.SaveAsync`; version the existing record instead of creating |
| 024 | FR-11 client — default to version-save when 013 resolved an identity; explicit "Save as new document" override routed through **link/graduate** (NFR-08), mirroring `PromoteIfEphemeralAsync`, never the immutable suppress path |
| 025 | FR-12 collision surfacing — **shape determined by Spike-4 (F-a)**. Blocked until 005 closes. Introduces no new collision logic. |
| 026 | FR-09 related-to record card — respects the **two-slot** model (`sprk_matter` + `sprk_relatedmatter`); scopes which of the 12 `sprk_related*` lookups it reads |
| 027 | FR-10 open record / open Document record — mechanism from Spike-2; Spaarke UI inside the dialog still complies with ADR-050 + ADR-021 |

**Gate**: identified document saves as a version, not a duplicate row; override creates a linked copy; profile displays; record card opens the record.

---

### Phase 3 — Surfacing Spaarke (tasks 030–037)

| Task | Deliverable |
|---|---|
| 030 | FR-13 shared server-side creation service — number generation, owner assignment, Field Mapping Framework population; `QuickCreateAsync` routes through it. **Matter.** Existing wizards untouched. |
| 031 | FR-13 **Project** — per its documented semantics (`sprk_projectnumber` is the primary name attribute; no client-side generator exists today). Not a mirror of Matter. |
| 032 | **FR-16a authorization hardening (F-b)** — per-row trimming on the visualization surface; add `VisualizationEndpoints.cs` to the `RouteAuthorizationGuardTests` census; negative test per NFR-02. **Gates 033.** |
| 033 | FR-16b Find view — three-state gating on `sprk_searchindexed` (null/false/true); **Run Index** via the existing `POST /api/ai/rag/send-to-index`, passing `documentId` per the indexing-pipeline pattern |
| 034 | FR-16c results — lazy-scroll per ADR-051 (no pager), permission-trimmed; **records bridge decision (F-c)** recorded |
| 035 | FR-14 Add To Do — reuse `POST /api/office/todo` + `CreateTodoView`; carry document **and** related record as regarding |
| 036 | FR-15 Send Email via Outlook with document/record links, honoring share-link expiry bounds |
| 037 | FR-17 wire the stubbed `quickSave` / `shareDocument` ribbon commands; follow `outlook/commands/index.ts` as the working reference; add them to the Word manifest (they are compiled today but unreferenced) |

**Gate**: a pane-created Matter is complete; Find returns permission-trimmed results with a passing negative test.

---

### Phase 4 — Parity, deploy, close (tasks 040–042, 090)

| Task | Deliverable |
|---|---|
| 040 | FR-19 Outlook parity pass + capability-gating audit — no host-type conditionals scattered through views (NFR-10) |
| 041 | NFR-09 per-environment Entra SPA redirect registration (`brk-multihub://<swa-host>` + `https://<swa-host>/auth-callback.html`); add this branch to `deploy-office-addins.yml` triggers or use `workflow_dispatch`. **Must not touch the three frozen CI tier files.** |
| 042 | Deploy + UAT — manifest version bumps (4-part), M365 re-registration, `gh run list --workflow=deploy-office-addins.yml` |
| 090 | Wrap-up — lessons learned, `/test-diet` gate, archive |

---

## 5. Dependencies

### Satisfied prerequisites (all verified on `origin/master`)

| Dependency | Evidence |
|---|---|
| #919 document profiling fix | `f5c7687d8` |
| Content dedup layer (`ContentDedupDetector`, `sprk_canonicalhash`, graduate-on-divergence) | on master |
| NAA auth, desktop + web | shipped |
| Upload-collision handling | `9c208a7f2`, `93d5e673e`, `086b9e9ce`, `047c9df8d`, `09025ab39` — ⚠️ but see **F-a**: on a different upload path than the add-in uses |
| `sprk_searchindexed` / `sprk_searchindexedon` | validated live via MCP |
| Manual re-index route | `POST /api/ai/rag/send-to-index` exists |

### External

- Word/Outlook add-in distribution via M365 Admin Center → Integrated Apps (manual, version bump required)
- Per-environment Entra SPA redirect registration (NFR-09) — a provisioning step that must precede deployment to each environment

### Hot-path coordination

`projects/INDEX.md` records 55 of 61 rows as BFF=Y. Relevant peers: `spaarkeai-compose-r8` (BFF+CI, `parallel-safe:false` across the Compose spine, the other `.docx` write path), `unified-access-control-r2` (tasks 094/095, `/api/documents` authorization), `email-communication-intelligence-r2` (authored the current add-in surface). ⚠️ `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` are **frozen** under the shadow-comparison window (open 2026-08-27).

---

## 6. Testing Strategy

Per ADR-038 (integration-heavy; coverage is observation, never a gate) — full text at [`docs/adr/ADR-038-testing-strategy.md`](../../docs/adr/ADR-038-testing-strategy.md), since no concise copy exists.

| Layer | Approach |
|---|---|
| **Contract** (`tests/integration/contract/Api/Office/`) | Every new or modified endpoint gets a contract test. Task 016 un-skips the two `POST /api/office/save` tests that are skipped today. |
| **Integration — data mutation** | Version-save asserts exactly one `sprk_document` row plus an incremented SPE version. The override path asserts `sprk_canonicaldocument` linkage. |
| **Negative / authorization** | NFR-02's gate: a user denied Read on a matter sees none of its documents through Find. Owned by task 032, not deferred to a checklist. |
| **Unit** (`shared/adapters/__tests__/`) | The `.docx` extraction path after consolidation; the three-state Find gating logic. |
| **UI** | `<ui-tests>` on every pane task, including the ADR-021 dark-mode check via Office theme toggle. |
| **Banned** | No `Mock<HttpMessageHandler>`, no DI-registration tests, no ctor null-check tests, no `Stopwatch`+`Task.Delay` (use `TimeProvider`). |

`/test-diet` runs at task 090 before the project is marked complete.

---

## 7. Risk Register

| ID | Risk | Impact | Mitigation |
|---|---|---|---|
| **R-1** | **Spike-1 negative** — `document.url` unusable on Word desktop for SPE files | FR-01's primary path fails; the FR-02 stamp becomes the only identity mechanism and does not work for documents saved before this release | Task 002 runs first. A negative result triggers a scope conversation before Phase 1, not during it. Fallback: identity only for post-release documents, explicitly communicated. |
| **R-2** | **Spike-4 negative (F-a)** — the add-in's save path has no collision semantics to surface | FR-12 becomes new work, contradicting "consume, do not rebuild" | Task 005 produces a §6.5 path recommendation (A/B/C) before task 025 is sized. Coordinate with UAC-r2 task 094. |
| **R-3** | **F-b authorization work is larger than a filter change** | FR-16 slips; or worse, ships permission-leaking results | Task 032 gates 033 structurally. If hardening proves large, Find is cut from r1 rather than shipped unsafe. |
| **R-4** | `HostAdapterFactory` activation surfaces latent breakage in Outlook | Regression in the working host | Task 010 is prescriptive-mode with the Outlook path tested first; `OutlookAdapter` already routes cleanly. |
| **R-5** | Merge collision with `spaarkeai-compose-r8` on the `.docx` / BFF spine | Rework | `/conflict-check` before every BFF PR; ADR-049 read before touching any `.docx` path; never delete `docxBridge.ts`. |
| **R-6** | Typecheck backlog is much larger than ~397 | Phase 0 balloons | Task 001 measures before 006–008 are sized. The ~397 figure traces to a single unverified source repeated in five places, with no committed artifact behind it. |
| **R-7** | BFF publish size drifts past the ceiling | Hard stop at 60 MB | Measure per task **against a fresh build of master**, never the recorded baseline (root CLAUDE.md §10 bullet 4). |

---

## 8. Acceptance Criteria

Graduation criteria live in [`README.md`](README.md). Verification method per criterion:

| Criterion | Verification |
|---|---|
| Spaarke-sourced document resolves to the right record + matter | Manual (Word desktop) + integration test against a seeded document |
| Desktop-sourced document claims no identity | Integration test |
| Stamped document round-trips and self-identifies | End-to-end test |
| Version, not duplicate row | Integration test asserting one row + incremented SPE version |
| Override creates link/graduate record | Integration test asserting `sprk_canonicaldocument` |
| Collision non-destructive, no new logic | Integration test + Spike-4 decision record |
| Profile displays; Generate Profile completes | Manual + contract test |
| Matter complete (number + owner + mapped fields) | Integration test |
| Find permission-trimmed | **Negative test** — denied user sees none of the matter's documents |
| To Do carries both regarding values | Integration test |
| Host parity | Capability-gating checklist |
| Typecheck clean | CI |
| Publish-size within ceiling | Per-task measurement vs. fresh master build |

---

## 9. Next Steps

1. Review this plan and [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).
2. Begin with **task 001** — it is the prerequisite for sizing 006–008 and is fast.
3. Run the four Phase-0 spikes (002–005) in parallel; they are independent.
4. **Do not start Phase 1 until the Phase 0 gate closes** — Spike-1 and Spike-4 outcomes change FR-01 and FR-12 scope.

Invoke via `task-execute` (never read POML files and implement manually).
