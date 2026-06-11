# Spaarke Multi-Container Multi-Index Routing — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-07
> **Source**: [`design.md`](design.md) (497 lines, 4 review rounds, all §9 questions resolved)
> **Branch**: `work/spaarke-multi-container-multi-index-r1` @ `a227b106`

---

## Executive Summary

Spaarke documents are stored in SharePoint Embedded (SPE) containers and indexed in Azure AI Search. The platform assumed one container + one index per tenant, but operational reality (recent migration + "Protected Matter" requirement) needs **record-scoped routing**: containers and indexes selected per record at create time, with the Business Unit cascading defaults and individual records able to override.

This project extends the 5 existing Spaarke Code Page create wizards (Matter, Project, Invoice, WorkAssignment, Event) and the DocumentUploadWizard to set `sprk_searchindexname` (and fix a latent `sprk_containerid` gap in CreateProjectWizard). It also extends the BFF `IKnowledgeDeploymentService` resolver to accept an explicit index name with allow-list validation, extends the SemanticSearchControl PCF (v1.1.74) and SemanticSearch code page to flow the index identifier end-to-end, and includes a one-time PowerShell backfill to correct historical records. **No Dataverse plugins, no Power Automate, no new field mappings.** Both existing inheritance mechanisms (OOB Dataverse attributemaps + Spaarke `sprk_fieldmappingprofile` framework) remain untouched within their existing scopes.

---

## Scope

### In Scope

- **Phase A**: Extend 5 Spaarke Code Page create wizards + DocumentUploadWizard to set `sprk_searchindexname`. Fix CreateProjectWizard's latent `sprk_containerid` gap.
- **Phase A.5**: Operator pre-deploy BU value setup (populate `sprk_searchindexname` on each BU per §5.0 of design.md).
- **Phase B**: BFF `IKnowledgeDeploymentService.GetSearchClientAsync` signature extension (optional `indexName`) + allow-list validation + request DTO additions.
- **Phase D**: SemanticSearchControl PCF v1.1.74 — one new bound property + send `searchIndexName` in BFF requests and navigateTo envelope.
- **Phase D.1 (folded scope §10.1 of design)**: Full filter parity in PCF → Code Page navigateTo envelope (query, scope, entityId, threshold, searchMode, fileTypes, dateFrom, dateTo, tags, associatedOnly).
- **Phase E**: SemanticSearch code page — extended `parseUrlParams`, App.tsx wires `initialScope` + `initialEntityId` (stop the discards), hooks include `searchIndexName` in requests.
- **Phase F**: One-time PowerShell backfill scripts — parent-record backfill + Document backfill + drift audit. Uses existing-data evidence (each Document's `sprk_graphdriveid` mapped via §5.1 hardcoded table).
- **Phase G**: Operator runbook + brief architecture-doc update.

### Out of Scope

- **Dataverse plugins** (Spaarke convention)
- **Power Automate flows** (Spaarke convention)
- **New Dataverse field mappings** — OOB attributemaps stay untouched (continue cascading `securitybu + securitybuname`)
- **Extension of Spaarke `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` framework** — stays untouched (continues powering "Matter to Event" / "Project to Event" regarding-selection on Event forms)
- **Populating `sprk_containerid` on `sprk_document`** (canonical Document container field is `sprk_graphdriveid`)
- **BU-change auto-sync / fan-out** (coexistence is the desired model per INV-3)
- **Cross-tenant search**
- **End-user index picker UI** (operator-configured via Dataverse + appsettings)
- **Re-indexing API for moving documents between physical indexes** (future epic)
- **Container → index map maintenance UI / config entity** (hardcoded in backfill script; future epic)
- **Orphan Document handling** (dev/test data; out of scope per §9 round-3 resolution)
- **New AI Search index provisioning automation** (operator provisions in Azure; this project routes to existing indexes)

### Affected Areas

#### Wizards (Phase A)
- `src/client/code-pages/CreateMatterWizard/`
- `src/client/code-pages/CreateProjectWizard/` (latent `sprk_containerid` bug fixed)
- `src/client/code-pages/CreateInvoiceWizard/`
- `src/client/code-pages/CreateWorkAssignmentWizard/`
- `src/client/code-pages/CreateEventWizard/`
- `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts` (line 216 pattern reference)
- `src/client/shared/Spaarke.UI.Components/src/components/Create{Project,Invoice,WorkAssignment,Event}Wizard/` service files
- `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts`
- `src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx` (extend `resolveContainerIdForRecord` pattern)
- `src/client/shared/Spaarke.UI.Components/src/services/document-upload/DocumentRecordService.ts` (extend `buildRecordPayload`)

#### BFF (Phase B)
- `src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/KnowledgeDeploymentService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs` (thread through)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` (thread through)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RecordSearch/RecordSearchService.cs` (thread through)
- `src/server/api/Sprk.Bff.Api/Models/Ai/SemanticSearch/SemanticSearchRequest.cs` (+ `RagSearchRequest`, `RecordSearchRequest`)
- `src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` (`AiSearch.AllowedIndexes`)

#### PCF v1.1.74 (Phase D)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/ControlManifest.Input.xml` (new bound property + version)
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx`
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/NavigationService.ts`
- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/SemanticSearchApiService.ts`
- `src/client/pcf/SemanticSearchControl/Solution/Controls/sprk_Sprk.SemanticSearchControl/ControlManifest.xml` (version)
- `src/client/pcf/SemanticSearchControl/Solution/solution.xml` (version)
- `src/client/pcf/SemanticSearchControl/Solution/pack.ps1` (version)
- Footer string in SemanticSearchControl.tsx (version)

#### Code Page (Phase E)
- `src/client/code-pages/SemanticSearch/src/index.tsx`
- `src/client/code-pages/SemanticSearch/src/utils/parseUrlParams.ts`
- `src/client/code-pages/SemanticSearch/src/App.tsx` (stop `void initialScope; void initialEntityId;` discards; seed filter state)
- `src/client/code-pages/SemanticSearch/src/hooks/useSemanticSearch.ts`
- `src/client/code-pages/SemanticSearch/src/hooks/useRecordSearch.ts`
- `src/client/code-pages/SemanticSearch/src/types/index.ts` (extend `AppUrlParams`)

#### Backfill + ops (Phase F + G)
- `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-ParentRecords.ps1` (new)
- `scripts/backfill-multi-container-multi-index/Backfill-MultiContainerMultiIndex-Documents.ps1` (new)
- `scripts/backfill-multi-container-multi-index/Audit-MultiContainerMultiIndex-Drift.ps1` (new)
- `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` (new)
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` (update — mention per-BU routing)

---

## Requirements

### Functional Requirements

#### Phase A — Spaarke Create Wizard extensions

**FR-WIZ-01**: `CreateMatterWizard` must include `sprk_searchindexname` in the Matter create payload, sourced from the current user's owning Business Unit's `sprk_searchindexname` field.
- **Acceptance**: Create a Matter via the wizard from the Spaarke Demo BU. MCP query: `SELECT sprk_searchindexname FROM sprk_matter WHERE sprk_matterid = '{new-id}'` → returns `spaarke-knowledge-index-v2`.

**FR-WIZ-02**: `CreateProjectWizard` must include **BOTH** `sprk_containerid` AND `sprk_searchindexname` in the Project create payload, sourced from the current user's BU. (Fixes latent gap G2.)
- **Acceptance**: Create a Project via the wizard. MCP query: both fields populated with current BU values.

**FR-WIZ-03**: `CreateInvoiceWizard` must include `sprk_containerid` AND `sprk_searchindexname` from BU. Spec phase verifies whether `sprk_containerid` was already being set; if not, fix that gap alongside.
- **Acceptance**: Same MCP-verified pattern as FR-WIZ-02.

**FR-WIZ-04**: `CreateWorkAssignmentWizard` must include `sprk_containerid` AND `sprk_searchindexname` from BU. Same gap verification as FR-WIZ-03.
- **Acceptance**: Same MCP-verified pattern.

**FR-WIZ-05**: `CreateEventWizard` must include `sprk_containerid` AND `sprk_searchindexname` from BU. Same gap verification as FR-WIZ-03.
- **Acceptance**: Same MCP-verified pattern.

**FR-WIZ-06**: `DocumentUploadWizard`'s `AssociateToStep` must implement `resolveSearchIndexNameForRecord(xrm, entityLogicalName, recordId)` that:
- Reads parent record's `sprk_searchindexname` first
- Falls back to parent record's owning BU's `sprk_searchindexname`
- Falls back to empty (server tenant default takes over)
- **Acceptance**: Unit tests cover all three steps of the chain.

**FR-WIZ-07**: `DocumentUploadWizard`'s `DocumentRecordService.buildRecordPayload` must include `sprk_searchindexname` in the Document create payload, alongside the existing `sprk_graphdriveid` (do NOT add `sprk_containerid` per INV / Spaarke convention).
- **Acceptance**: Upload a document under a Matter via the wizard. MCP query the new sprk_document: `sprk_searchindexname` populated; `sprk_containerid` still NULL; `sprk_graphdriveid` populated as today.

**FR-WIZ-08**: All wizard extensions MUST preserve INV-5 (explicit override values are sacred — never overwrite if the create payload already has a non-empty value).
- **Acceptance**: Set a record's field explicitly via the form before the wizard's resolution step; the explicit value persists.

#### Phase A.5 — Operator BU value setup (PREREQUISITE)

**FR-OPS-01**: Operator (or operator-run `.ps1`) populates `sprk_searchindexname` on each BU per §5.0 of design.md.
- Spaarke Demo BU → `spaarke-knowledge-index-v2`
- Spaarke BU → `spaarke-file-index`
- Spaarke Dev 1, Spaarke Test 1 → operator-determined (may stay NULL → tenant default applies)
- **Acceptance**: MCP query confirms BU values set BEFORE wizard deploy.

#### Phase B — BFF resolver extension

**FR-BFF-01**: `IKnowledgeDeploymentService.GetSearchClientAsync` MUST extend its signature to accept optional `string? indexName = null`. Existing callers (no `indexName` param) continue to work unchanged.
- **Acceptance**: Compilation succeeds with no breaking changes; existing BFF test suite passes unmodified.

**FR-BFF-02**: When `indexName` is non-empty, the resolver MUST validate it against `appsettings.AiSearch.AllowedIndexes`. Invalid values return ProblemDetails **400** with type code `INDEX_NOT_ALLOWED` and a descriptive title/detail.
- **Acceptance**: `POST /api/ai/search` with `searchIndexName: "not-a-real-index"` returns 400 with the documented ProblemDetails.

**FR-BFF-03**: When `indexName` is non-empty AND validated, the resolver MUST return a `SearchClient` bound to that index name (resolving via the tenant's Search endpoint + the supplied index name).
- **Acceptance**: Integration test: request specifying `"spaarke-file-index"` returns results scoped to that physical index (verified via Application Insights / BFF log of the issued Azure Search query URL).

**FR-BFF-04**: When `indexName` is empty / null, the resolver MUST fall back to the existing 2-tier chain: `sprk_aiknowledgedeployment` Dataverse entity → `appsettings.AiSearch.KnowledgeIndexName`. Fully backward-compatible.
- **Acceptance**: Existing test suite passes with zero modifications.

**FR-BFF-05**: Request DTOs (`SemanticSearchRequest`, `RagSearchRequest`, `RecordSearchRequest`) MUST add an optional `string? SearchIndexName { get; init; }` property. JSON deserialization MUST be forward-compatible (requests without the field continue to work).
- **Acceptance**: Unit tests cover both forms; existing endpoint test cases unchanged.

**FR-BFF-06**: `appsettings.AiSearch.AllowedIndexes` (string array) MUST be added with default value: `["spaarke-knowledge-index-v2", "spaarke-file-index", "discovery-index", "spaarke-rag-references"]`. Both `appsettings.template.json` and per-environment config files updated.
- **Acceptance**: Appsettings reviewed; BFF startup logs the allow-list at INFO level for verification.

**FR-BFF-07**: Search endpoint (`/api/ai/search` and equivalents) MUST thread the `SearchIndexName` from the request DTO into the resolver call.
- **Acceptance**: End-to-end integration test: client sends `searchIndexName`; server log shows the resolved index name in the issued Azure Search URL.

#### Phase D — PCF SemanticSearchControl v1.1.74

**FR-PCF-01**: PCF manifest MUST add a new bound property `searchIndexName` (SingleLine.Text, bound) representing the scope record's `sprk_searchindexname` field.
- **Acceptance**: ControlManifest.Input.xml inspected; PCF rebuilds with new property bound; maker can wire the field on a form.

**FR-PCF-02**: `SemanticSearchApiService.search()` MUST include `searchIndexName` in the request body when non-empty (omit when empty).
- **Acceptance**: Unit test confirms request body includes the value; network trace of a live call to BFF confirms it.

**FR-PCF-03**: `NavigationService.openSemanticSearchPage()` MUST include `searchIndexName` in the data envelope passed via `Xrm.Navigation.navigateTo({pageType:"webresource", data:"...&searchIndexName=..."})`.
- **Acceptance**: Decode of the envelope URL shows the parameter; integration test on Spaarke Test 1 confirms code page receives it.

**FR-PCF-04**: PCF version bump to **v1.1.74** in 5 locations (per `/pcf-deploy` skill):
1. `ControlManifest.Input.xml` `version="1.1.74"` attribute + description tag
2. `SemanticSearchControl.tsx` footer string `v1.1.74 • Built 2026-XX-XX`
3. `Solution/solution.xml` `<Version>1.1.74</Version>`
4. `Solution/Controls/sprk_Sprk.SemanticSearchControl/ControlManifest.xml` `version="1.1.74"`
5. `Solution/pack.ps1` `$version = "1.1.74"`
- **Acceptance**: All 5 locations updated; `npm run build:prod` produces ≤1 MB bundle; built ZIP imports cleanly; PCF footer shows `v1.1.74`.

#### Phase D.1 — Filter parity (folded scope, design §10.1)

**FR-PARITY-01**: `NavigationService.openSemanticSearchPage()` MUST include ALL of the PCF's current filter state in the navigateTo envelope:
- `query` (string)
- `scope` (string) — when non-`all`
- `entityId` (string) — when set
- `threshold` (number)
- `searchMode` (`hybrid`/`vectorOnly`/`keywordOnly`)
- `fileTypes` (CSV) — when non-empty
- `dateFrom` (ISO string) — when set
- `dateTo` (ISO string) — when set
- `tags` (CSV) — when non-empty
- `associatedOnly` (boolean) — when true
- (plus `theme` and `searchIndexName` from FR-PCF-03)
- **Acceptance**: Decoded envelope contains all set parameters; empty/default values omitted to keep URL short.

**FR-PARITY-02**: Code Page MUST run a search using the same query + scope + filters + index identifier as the PCF was showing. Result-set MUST match the PCF's result-set for the same time-point.
- **Acceptance**: UAT walkthrough: Apply Threshold=50% + Mode=Hybrid + Associated Only=true on a matter-scoped PCF. Click Open in Semantic Search. Modal shows identical document set (same titles, same order, same count).

#### Phase E — SemanticSearch Code Page

**FR-CP-01**: `parseUrlParams` MUST read all envelope parameters: `theme`, `query`, `domain`, `scope`, `entityId`, `savedSearchId`, `searchIndexName`, `threshold`, `searchMode`, `fileTypes`, `dateFrom`, `dateTo`, `tags`, `associatedOnly`. Return shape extends `AppUrlParams`.
- **Acceptance**: Unit tests cover each parameter (present/absent/malformed).

**FR-CP-02**: `App.tsx` MUST stop discarding `initialScope` and `initialEntityId` (remove the `void initialScope; void initialEntityId;` lines). Wire them into `useSemanticSearch` and `useRecordSearch` hook scope.
- **Acceptance**: Code review confirms the void lines are gone; hook receives scope and entity at construction time.

**FR-CP-03**: `App.tsx` MUST seed `filters` state (fileTypes, dateRange, threshold, searchMode, associatedOnly) AND `selectedTags` state from the initial URL params BEFORE the auto-search effect fires.
- **Acceptance**: When opened with filter params, the very first search uses those filters (verified via network trace of first POST).

**FR-CP-04**: `useSemanticSearch` and `useRecordSearch` hooks MUST include `searchIndexName` in their request bodies when non-empty.
- **Acceptance**: Network tab shows `searchIndexName` in POST `/api/ai/search` body.

#### Phase F — Backfill scripts

**FR-BF-01**: Create `Backfill-MultiContainerMultiIndex-ParentRecords.ps1` that, for each Matter / Project / Invoice / WorkAssignment / Event:
1. Queries existing child Documents
2. IF ≥1 children: derives effective container as **majority/mode** of child Documents' `sprk_graphdriveid`
3. IF 0 children: falls back to owner BU's current `sprk_containerid`
4. Fills `sprk_containerid` if empty (never overwrites — INV-5)
5. Maps derived container via §5.1 hardcoded table to set `sprk_searchindexname` if empty (never overwrites)
6. HALTS with surfaceable error on unmapped container (operator must extend the §5.1 map)
- **Acceptance**: Run script against test environment; verify per-record audit log shows expected fills, no overwrites, and a halt-on-unmapped behavior when an unknown container appears.

**FR-BF-02**: Create `Backfill-MultiContainerMultiIndex-Documents.ps1` that, for each `sprk_document` with empty `sprk_searchindexname`:
1. Reads its `sprk_graphdriveid`
2. Maps via §5.1 table → fills `sprk_searchindexname`
3. Logs + skips orphans (Documents with no `sprk_graphdriveid`)
4. Halts on unmapped container
- **Acceptance**: Same shape as FR-BF-01 — audit log + halt behavior verified.

**FR-BF-03**: Create `Audit-MultiContainerMultiIndex-Drift.ps1` (informational, **no writes**) producing a CSV/Markdown report of records where the stored value DIFFERS from the chain-derived value. Distinguishes intentional overrides vs. anomalies.
- **Acceptance**: Report file generated with columns: entity, recordId, currentValue, derivedValue, classification (override/drift/anomaly), recommendation.

**FR-BF-04**: All backfill scripts MUST be:
- **Idempotent** — re-running produces same end-state (no double-writes, no value drift)
- **Resumable** — checkpoint per N records (configurable, default 100); safe to kill mid-run and re-run
- **Paged** — batch size configurable, default 500; avoids Dataverse query timeouts
- **NEVER overwrite existing non-null values** (INV-5)
- **Acceptance**: Manual test: run script against 10K records; kill at 5K; re-run; final state matches single-pass.

#### Phase G — Operator runbook + docs

**FR-DOC-01**: Create `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` containing the §6 content from design.md:
- Pre-deploy BU value setup
- How to assign a new index to a BU
- How to mark a single record as Protected
- Drift coexistence model
- Adding a new physical index
- Document container reference clarification
- Non-wizard creates caveat
- **Acceptance**: Runbook covers all 7 bullets; cross-references the design.md for invariants.

**FR-DOC-02**: Update `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` (or equivalent canonical architecture doc) to mention per-BU container/index routing as a deployed capability.
- **Acceptance**: Architecture doc has a section / paragraph describing the routing model.

### Non-Functional Requirements

**NFR-01**: All changes MUST honor design.md invariants INV-1 through INV-8.
- **Acceptance**: Code review checklist explicitly maps each PR to each invariant.

**NFR-02**: BFF resolver extension MUST be **backward-compatible** with all existing callers (callers that don't pass `searchIndexName` work as today).
- **Acceptance**: Entire existing BFF test suite passes unmodified.

**NFR-03**: PCF v1.1.74 bundle MUST build in production mode (`npm run build:prod`) with size ≤ 1 MB.
- **Acceptance**: Built `bundle.js` measured; size within target (currently ~754 KB for v1.1.73, so headroom is plentiful).

**NFR-04**: Backfill scripts MUST process at least 10K records in a single run without hitting Dataverse throttling or memory pressure.
- **Acceptance**: Load test with 10K synthetic records completes successfully.

**NFR-05**: BFF response time for searches with explicit `searchIndexName` MUST be within **10%** of today's baseline (the allow-list check is essentially free).
- **Acceptance**: Performance test (10 runs, both with and without explicit `searchIndexName`) shows < 10% delta.

**NFR-06**: PCF + Code Page MUST use **Fluent UI v9** only (ADR-021). No `@fluentui/react` v8.
- **Acceptance**: Build-time check confirms no v8 imports.

**NFR-07**: PCF MUST remain React 16-safe (ADR-022). SemanticSearch Code Page uses React 18 (its existing target).
- **Acceptance**: PCF builds against React 16 platform library; no React 18-only APIs in PCF code.

**NFR-08**: BFF endpoint changes MUST follow ProblemDetails for error responses (ADR-019). `400 INDEX_NOT_ALLOWED` returns a valid ProblemDetails JSON with extension members.
- **Acceptance**: Schema test of the 400 response.

**NFR-09**: All client → BFF calls MUST use `authenticatedFetch` from `@spaarke/auth` (ADR-028). No new auth wiring; no `accessToken` props.
- **Acceptance**: Code review confirms no `Bearer ${token}` literals; no `new PublicClientApplication`; all BFF calls use `authenticatedFetch`.

**NFR-10**: PCF deploy MUST follow `/pcf-deploy` skill exactly — including 5-location version bump, production-mode build, and **clean-rebuild of `@spaarke/auth` + `@spaarke/ui-components` `dist/` BEFORE the PCF build** (per saved lesson `feedback_stale-shared-lib-dist-poisons-codepage-bundle`).
- **Acceptance**: Deploy log shows the clean-rebuild steps were run before the PCF build.

**NFR-11**: Code page deploy MUST follow `/code-page-deploy` skill exactly — including clean-rebuild of shared libs and Vite cache clear before each build.
- **Acceptance**: Same pattern as NFR-10 but for the code page.

**NFR-12**: BFF deploy MUST follow `/bff-deploy` skill — production-mode publish, framework-dependent linux-x64, transitive CVE override (per ADR-029).
- **Acceptance**: Publish output verified against ADR-029 hygiene rules.

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-001** | Minimal API pattern (BFF endpoint changes) |
| **ADR-006** | PCF over webresources; Code Page as full-page Power Apps surface |
| **ADR-008** | Endpoint filter authorization (existing BFF auth filter unchanged) |
| **ADR-010** | DI minimalism (BFF resolver registration) |
| **ADR-012** | Shared component library `@spaarke/ui-components` (wizards live here) |
| **ADR-013** | AI architecture / semantic search |
| **ADR-019** | ProblemDetails on BFF errors (`400 INDEX_NOT_ALLOWED`) |
| **ADR-021** | Fluent UI v9 + dark mode + tokens (PCF + Code Page) |
| **ADR-022** | React version boundaries (PCF: 16, Code Page: 18) |
| **ADR-026** | Full-page Code Page standard (`sprk_semanticsearch`) |
| **ADR-028** | Spaarke Auth v2 (`@spaarke/auth.authenticatedFetch`, `initAuth`, `useAuth`) |
| **ADR-029** | BFF publish hygiene (framework-dependent, transitive CVE override, size baseline) |

### MUST Rules

- ✅ **MUST use `@spaarke/auth.authenticatedFetch`** for all BFF calls (ADR-028)
- ✅ **MUST use Fluent UI v9 + `tokens.*`** for all styling (ADR-021) — no hex / no `var(--…)` / no rgb literals
- ✅ **MUST use ProblemDetails** for BFF error responses (ADR-019)
- ✅ **MUST follow existing wizard patterns** — extension, not rewrite (CreateMatterWizard is the canonical reference at [`matterService.ts:216`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts#L216))
- ✅ **MUST validate `searchIndexName`** against `appsettings.AiSearch.AllowedIndexes` before resolving SearchClient
- ✅ **MUST never overwrite** explicit field values during backfill (INV-5)
- ✅ **MUST halt loud** on unmapped container during backfill (no silent default)
- ✅ **MUST honor PCF v1.1.74** in 5 version locations (per `/pcf-deploy` skill)
- ✅ **MUST clean-rebuild** `Spaarke.Auth` + `Spaarke.UI.Components` `dist/` before PCF + Code Page builds (per saved lesson)
- ✅ **MUST cap PCF bundle at ≤ 1 MB** in production mode

- ❌ **MUST NOT introduce Dataverse plugins**
- ❌ **MUST NOT introduce Power Automate flows**
- ❌ **MUST NOT modify existing OOB Dataverse attributemaps** (security_bu + name remain untouched)
- ❌ **MUST NOT extend `sprk_fieldmappingprofile`** / create new profile records (Matter→Event / Project→Event scope stays as-is)
- ❌ **MUST NOT populate `sprk_containerid` on `sprk_document`** (canonical Document container field is `sprk_graphdriveid`)
- ❌ **MUST NOT add container filter** to BFF search OData (multiple containers can share one index)
- ❌ **MUST NOT introduce BU-change auto-sync** (coexistence is the design per INV-3)
- ❌ **MUST NOT use `@fluentui/react` v8**
- ❌ **MUST NOT use React 18-only APIs** in PCF code

### Existing Patterns to Follow

| Pattern | Reference |
|---|---|
| Wizard sets container ID on create payload | [`CreateMatterWizard/matterService.ts:216`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts#L216): `entity['sprk_containerid'] = this._containerId;` |
| Container resolution chain (parent → user-BU fallback) | [`AssociateToStep.tsx:147-163`](../../src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx#L147-L163): `resolveContainerIdForRecord` |
| Document create payload | [`DocumentRecordService.ts:268-293`](../../src/client/shared/Spaarke.UI.Components/src/services/document-upload/DocumentRecordService.ts#L268-L293): `buildRecordPayload` |
| BFF index resolver chain | [`IKnowledgeDeploymentService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs) — already supports per-tenant routing via `sprk_aiknowledgedeployment` with appsettings fallback |
| BFF OData filter — confirms no container filter exists | [`SearchFilterBuilder.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SearchFilterBuilder.cs) |
| PCF deploy + version bump | `/pcf-deploy` skill |
| Code page build + inline | `/code-page-deploy` skill |
| BFF deploy + publish hygiene | `/bff-deploy` skill + ADR-029 |
| Spaarke Auth v2 client contract | `.claude/patterns/auth/spaarke-sso-binding.md` + ADR-028 |

---

## Success Criteria

### Phase Acceptance Gates

| Phase | Gate |
|---|---|
| A — Wizards | All 5 parent-record wizards + DocumentUploadWizard, when used to create a new record, produce records with both `sprk_containerid` AND `sprk_searchindexname` populated. CreateProjectWizard latent bug fixed. |
| A.5 — Operator BU setup | MCP query confirms `sprk_searchindexname` set on Spaarke Demo + Spaarke BUs (other BUs as operator decides). |
| B — BFF resolver | Searches with explicit `searchIndexName` route to that index; without it, fall back to existing tenant chain. Allow-list validation surfaces 400 on bad value. |
| D — PCF v1.1.74 | Footer shows `v1.1.74`; PCF sends `searchIndexName` in API calls; new bound property visible to maker for form wiring. |
| D.1 — Filter parity | Open in Semantic Search from PCF launches modal showing identical documents to PCF (same query + filters + scope + index). |
| E — Code Page | `parseUrlParams` reads all envelope params; App.tsx seeds filter state; hooks include `searchIndexName` in requests. |
| F — Backfill | Scripts run; empty fields filled from existing evidence; explicit values preserved; drift report generated. |
| G — Operator docs | Runbook exists; covers Protected Matter, BU value setup, BU change, drift handling. |
| H — Deploy + UAT (operational) | Coordinated deploy: A.5 → B → A → D → E → F. Smoke + parity tests pass. Protected Matter walkthrough verified. |

### Top-level acceptance (cross-phase)

- [ ] A user creates a new Matter from the sitemap; the new Matter has both `sprk_containerid` AND `sprk_searchindexname` populated matching the user's BU's values. **Verify by MCP query.**
- [ ] An operator sets a Matter's `sprk_searchindexname` explicitly to override; future Documents uploaded under that Matter inherit the override (not the BU default). **Verify by Document MCP query.**
- [ ] An operator changes a BU's `sprk_searchindexname`; existing records keep their original values; new records get the new value (INV-3 coexistence proven). **Verify by before/after MCP query.**
- [ ] PCF SemanticSearchControl on a Protected Matter (explicit `sprk_searchindexname = "spaarke-file-index"`) returns search results from that index only. **Verify by BFF log + result content.**
- [ ] PCF "Open in Semantic Search" launches modal showing the EXACT same result set the PCF was showing. **Verify by side-by-side compare in UAT.**
- [ ] Backfill scripts populate all empty container/index fields per the design's rules without overwriting explicit values. **Verify by drift audit report.**

---

## Dependencies

### Prerequisites

- **Operator MUST populate `sprk_searchindexname` on each BU per §5.0 BEFORE the extended wizards ship** (Phase A.5 runs before Phase A deploy).
- BFF deploy (Phase B) MUST land BEFORE PCF v1.1.74 deploy (Phase D) — PCF requires the BFF to accept the new request field.
- Wizard deploys (Phase A) CAN land independently — wizards work standalone; their writes are visible to all subsequent reads (server-side data).

### External Dependencies

- **Azure AI Search**: indexes `spaarke-knowledge-index-v2` and `spaarke-file-index` must exist in the tenant's Azure subscription (already provisioned per operator).
- **SharePoint Embedded**: containers `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` and `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh` must exist (already provisioned).
- **Dataverse schema**: `businessunit.sprk_searchindexname`, parent entities' `sprk_searchindexname`, `sprk_document.sprk_searchindexname` all exist (MCP-verified ✓).

---

## Owner Clarifications

Captured during 4 rounds of design review (full log in design.md §13):

| Topic | Question | Answer | Implementation Impact |
|---|---|---|---|
| Tenant default storage | Where? | 2-tier: `sprk_aiknowledgedeployment` + `appsettings.AiSearch.KnowledgeIndexName` | BFF resolver chain known |
| BFF allow-list | Validate or trust? | **Validate** (static appsettings) | 400 INDEX_NOT_ALLOWED on miss |
| Backfill mechanism | How? | **One-time PowerShell `.ps1` from Claude Code project** | No scheduled jobs |
| Drift audit cadence | When? | **One-time at deploy** | Not recurring |
| Dataverse setup state | Ready? | Schema exists; values NULL; existing field mappings DON'T inherit container/index; wizards are the actual cascade | Wizards-extension approach is the design |
| Inheritance mechanism | What? | **Spaarke Create Wizards** (NOT field mappings) | Extend the 5 wizards + Doc wizard |
| Both OOB + Spaarke FieldMappingProfile | Coexistence? | Both stay untouched in their scopes | OOB cascades securitybu+name; Spaarke profile cascades Matter→Event regarding |
| PCF version | Bump to? | **v1.1.74** (baseline v1.1.73) | 5-location version bump |
| Container in search request | Needed? | **No** — BFF has no container filter (SearchFilterBuilder verified) | Client sends only `searchIndexName` |
| Backfill source | From BU? | **No — from existing-data evidence** (mode of child Documents' `sprk_graphdriveid`); BU has been changed | Backfill is evidence-driven |
| Backfill tiebreaker | Mode? | **Majority/mode** of child Documents' `sprk_graphdriveid` | Custom logic in script |
| Document container field | Which? | **`sprk_graphdriveid`** (canonical); `sprk_containerid` stays NULL on Documents | No new Document field |
| Orphan Documents | Handle? | **Out of scope** — dev/test data | Backfill logs + skips |
| Re-indexing API | Build? | **Out of scope for R1** | Future epic |
| Container→index map | Maintain how? | **Hardcoded in backfill script** for R1 | Future maybe config entity |

---

## Assumptions

| # | Assumption | Risk if wrong |
|---|---|---|
| A1 | `CreateInvoiceWizard` / `CreateWorkAssignmentWizard` / `CreateEventWizard` follow the same pattern as `CreateMatterWizard` for setting `sprk_containerid`. Spec phase verifies and adjusts. | Implementation effort scales (one extra service file per wizard verified). Backfill catches gaps. |
| A2 | `DocumentUploadWizard`'s existing `resolveContainerIdForRecord` behavior preserved verbatim. New `resolveSearchIndexNameForRecord` is additive. | Container resolution behavior unchanged from today. |
| A3 | Operator has maker-portal access to set BU values. | Without access, Phase A.5 is blocked. |
| A4 | The two known SPE container IDs (`b!yLRd…` and `b!vzGD…`) cover all production data. Backfill HALTS LOUD if a third container appears (operator extends §5.1 map). | Halt-loud behavior catches the unknown case explicitly. |
| A5 | `sprk_aiknowledgedeployment` entity is currently a fallback path that returns NULL for most tenants (so today's effective default is `appsettings.AiSearch.KnowledgeIndexName`). Spec verifies during implementation. | If the entity is widely used, BFF resolver chain ordering matters more. Verifiable via MCP. |

---

## Unresolved Questions

**None blocking.** All §9 design questions resolved per design.md decision log (§13, 11 rows + round-4 entries). Implementation can begin once spec is signed off.

---

## Implementation Notes

- **Single source of truth**: design.md remains the canonical reference for invariants (INV-1..INV-8), the container→index map (§5.1), and the deploy sequence (§11). This spec.md derives concrete FR/NFR from those.
- **Deploy ordering matters** (per §11 of design.md and §Dependencies above): A.5 → B → A → D → E → F. Backfill MUST run AFTER A+B+D+E so the new wizards have a chance to write correct values for new records first, and the BFF can use them.
- **Backward compatibility throughout**: BFF, PCF, Code Page changes are all additive — old callers / configs continue to work. This enables phased deploy without coordination headaches.
- **The new project branch `work/spaarke-multi-container-multi-index-r1`** is the implementation branch. PR will land separately from PR #363 (the v1.1.73 PCF UI tweaks).
- **Saved lessons referenced**:
  - `feedback_stale-shared-lib-dist-poisons-codepage-bundle` — mandatory clean rebuild before PCF / Code Page deploys
  - `feedback_deploy-asks-follow-skill-no-openended-questions` — when deploying, invoke the matching skill verbatim and hand over file paths

---

*AI-optimized specification. Original design: [`design.md`](design.md) (497 lines, 4 review rounds).*
