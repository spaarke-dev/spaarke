# R1 Synopsis — `spaarke-multi-container-multi-index-r1`

> **Compiled**: 2026-06-27
> **Source**: `projects/spaarke-multi-container-multi-index-r1/` (README, lessons-learned, handoffs, TASK-INDEX, upload-indexing checklist) + 30+ commits on `master`
> **Purpose**: Brief for the R2 scope review

---

## 1. What R1 shipped

R1 delivered **per-record container + AI Search index routing** end-to-end across the Spaarke stack. The platform's prior assumption — "one SPE container + one AI Search index per tenant" — is gone. Every Matter / Project / Event / Work Assignment now has its own container + indexed lookup, cascaded from the user's Business Unit and inheritable by uploaded documents. "Protected Matter" isolation works at the routing layer.

### Components delivered

| Surface | Change |
|---|---|
| **BFF API** | `IKnowledgeDeploymentService` extended with lookup-first FetchXml resolver; `DataverseAllowedIndexesProvider` (cached); `SearchIndexName` on 3 request DTOs; 400 `INDEX_NOT_ALLOWED` ProblemDetails; `AiSearch:AllowedIndexes` config |
| **Wizards** (5) | Matter/Project/Event/WorkAssignment + DocumentUploadWizard cascade BOTH `sprk_searchindexname` (text) AND `sprk_ai_search_index` (lookup) from BU; INV-5 explicit-override-preserve enforced |
| **PCF SemanticSearchControl** | v1.1.73 → **v1.1.76**: bound `searchIndexName`, runtime lookup via `context.webAPI`, full filter-parity navigateTo envelope; Find Similar multi-index aware |
| **Code page `sprk_semanticsearch`** | Index dropdown (replaces 4 tabs), `aiSearchIndexService.ts` catalog client, side-pane reorg with info icons, auto-search on launch with entityId, full filter-parity consumption |
| **Dataverse schema** | `sprk_ai_search_index` lookup on 7 entities (BU + 5 parents + Document); `sprk_aisearchindex` catalog table with 8 rows; operator BU values seeded |
| **PowerShell** | 3 backfill scripts (parent records, documents, drift audit) + 1 migration script (text → lookup on 7 entities) |
| **Docs** | `MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`; arch-doc update |

### User-visible outcomes

- Wizards auto-populate index lookup; users see "AI Search Index" on Matter/Project/etc. forms
- Protected Matter isolation: search on a Matter routed to `spaarke-file-index` returns only that index's docs
- PCF → code page filter parity (one click preserves query/threshold/filters/scope)
- Find Similar respects the parent's routed index
- Index dropdown in code page side pane

---

## 2. Scope extensions added during R1

R1 started as a 43-task project. Three significant scope extensions were absorbed mid-flight without re-planning.

### 2.1 Upload-Indexing Centralization (BFF architectural fix)

**Trigger**: Post-UAT discovery that 4 of 5 wizards never enqueued AI Search indexing. Original Tier 3 indexer routing only wired DocumentUploadWizard's `/api/ai/rag/index-file` path. The other wizards uploaded to SPE via `POST /api/spe/containers/.../upload` which never triggered indexing. **Files landed in SPE but never appeared in the search index.** Same gap also discovered on `PersistDocumentAsync` (SprkChat surface).

**Design** (in `notes/upload-indexing-centralization-design.md`): introduce `IPostUploadIndexingEnqueuer` server-side. Wire into 5 BFF upload endpoints. All clients (wizards, External SPA, future Teams app) get indexing automatically — no client-side change.

| Phase | Status | Notes |
|---|---|---|
| Phase 1 — `IPostUploadIndexingEnqueuer` interface + impl + DI + 12 unit tests | ✅ shipped (`fd9dda7d`) | feature-flag, MIME skip-list, size cap, idempotency, non-fatal |
| Phase 2 — Refactor 3 existing post-upload enqueue sites onto helper | ✅ shipped (`71b1af572`) | UploadFinalizationWorker, IncomingCommunicationProcessor, AnalysisResultPersistence |
| Phase 3a — Wire helper into 3 of 5 missing endpoints | ✅ shipped (`d65dcc2fa`) | 3 of the 5 endpoints |
| Phase 3b — Wire remaining 2 endpoints | 🔲 **NOT SHIPPED** | `/api/upload-session/chunk` final-chunk; `/api/ai/chat/sessions/{id}/documents/{docId}/persist` |
| Pattern 4 alignment fix (writer-identity rule: sync OBO for user uploads, async MI for MI-written) | ✅ shipped (`101575239`) | |
| Wizard-side tactical fix — Matter/Project/Event/WorkAssignment call sdap-client directly | ✅ shipped (`3fbda7e2..f0e1f5a8`) | **Bypasses the BFF seam** — pragmatic but undermines the architectural goal |
| Phase 4 — Decommission DocumentUploadWizard's wizard-side `triggerRagIndexing` | 🔲 **NOT SHIPPED** | Now redundant with BFF seam |

**Open mystery**: `RESTART-helper-not-running-mystery.md` — during Phase 3a UAT, helper was deployed but never invoked from `OBOEndpoints.cs` despite source code being correct. May have been resolved by the Pattern 4 fix OR effectively bypassed by the wizard-side tactical fix; **never explicitly closed in writing**.

### 2.2 Phase G — Lookup-Driven Multi-Index Architecture

**Trigger**: R1 originally used `sprk_searchindexname` (text column). Discovered mid-project that a free-text approach is brittle (typos, no referential integrity, no UI dropdown). Designed and added the `sprk_ai_search_index` lookup + `sprk_aisearchindex` catalog table.

| Task | Status |
|---|---|
| 100 — Schema add lookup on 7 entities | ✅ |
| 101 — Seed catalog (8 rows) | ✅ |
| 102 — BFF lookup-first resolver + `DataverseAllowedIndexesProvider` | ✅ |
| 103 — BFF tests + publish + deploy + App Insights verification | 🔄 deployed; verification pending |
| 104 — Data migration script (text → lookup, 7 entities) | ✅ |
| **105 — Lookup→lookup field mappings (6 entity chains)** | 🔲 **NOT SHIPPED** |
| 106 — PCF v1.1.75 runtime lookup via `context.webAPI` | ✅ |
| 107 — PCF deploy + form cleanup | ✅ |
| 108 — Code page UI redesign (dropdown, side-pane reorg) | ✅ |
| 109 — Code page deploy + e2e verification | 🔄 deployed; verification pending |
| **110 — Post-soak cleanup**: drop `sprk_searchindexname` text col + remove BFF text fallback | 🔲 **NOT SHIPPED** (needs UAT pass + 24–48 hr soak) |
| **111 — Phase G wrap-up**: lessons learned, runbook update, Find Similar UAT, pattern doc | 🔲 **NOT SHIPPED** |

### 2.3 Wizard Lookup Cascade Gap (PR #380)

**Trigger**: 2026-06-10 UAT — new Matter created via wizard had `sprk_ai_search_index = null` despite BU having it set. Phase G design assumed Dataverse OOB attributemaps would cascade the new lookup. In practice, attributemaps DON'T fire on Web API creates unless the parent lookup is explicit in the payload.

| Item | Status |
|---|---|
| PR #380 — 4 wizard services + EntityCreationService + DocumentRecordService cascade `sprk_ai_search_index` lookup | ✅ merged 2026-06-11 |
| Backfill `PAT-897705` (the 2026-06-10 UAT subject) + 3 documents | 🔲 optional one-off |

---

## 3. Deferred from original scope

| # | Item | Cost | Why deferred |
|---|---|---|---|
| D1 | Task 023 — `CreateInvoiceWizard` cascade | none | Wizard doesn't exist in codebase |
| D2 | Drift-audit script schema-assumption bug — hardcodes `sprk_name` for all 6 entities; `sprk_matter` uses `sprk_matternumber` | ~10-line fix in `$entityConfigs` block | Surfaced in 053 dry-run; not blocking |
| D3 | Backfill scripts param naming alignment (`-Environment` vs `-EnvironmentUrl`) | trivial | Two sub-agents made different choices |

---

## 4. Hot-fixes during R1 (signal of design fragility)

Eleven post-UAT hot-fix commits indicate the original Phase G design had brittle integration assumptions. Worth examining what kept breaking before R2 commits to a similar shape.

| # | Issue | Commit |
|---|---|---|
| H1 | Code page host context broken in Phase G deploy | `2b74eaf04` |
| H2 | BFF `ChunkIndex` non-nullable → 500s | `2b74eaf04` |
| H3 | PCF nav-property casing wrong (`sprk_AI_Search_Index` PascalCase) | `2b74eaf04` |
| H4 | Code page sent `searchIndexName` wrong; same 6 results regardless of matter | `7e9b40af7` |
| H5 | Code page client-side threshold filter buggy | `17ded8b35` |
| H6 | Code page didn't auto-search on launch with `entityId` (FR-CP-13 gap) | `edd16835f` |
| H7 | Find Similar not multi-index aware | `9c6104a04` |
| H8 | Code page scope mapping → 400 on entity-scoped search | `a30d97ae9` |
| H9 | `sprk_` prefix not stripped on `parentEntityType` | `96301f939` |
| H10 | URL-derived scope leaked into user-initiated searches | `d765079ca` |
| H11 | Matter wizard cascade silent failure (`_toWebApiLike` adapter wrapping) | `22faac8d9` |

**Pattern**: integration drift between PCF / code-page / BFF query-param shapes; casing mismatches across naming systems (lookup col, nav property, OData expand); silent failures from adapter wrappers diverging from proven patterns.

---

## 5. Open issues / blocking items

### 🔴 Blocks R1 graduation (per README graduation criteria)

| Criterion | Blocker |
|---|---|
| Explicit `sprk_searchindexname` override on Matter inherited by uploaded Documents | Awaits in-browser UAT |
| BU's `sprk_searchindexname` can change; existing records retain; new records use new value (INV-3 coexistence) | Awaits operator changing a BU value + MCP verification |
| **PCF on Protected Matter returns only protected-index docs** | **Blocked by F1 (AI Search indexer pipeline)** — files reach SPE but indexer never pulls them into AI Search |
| PCF "Open in Semantic Search" → identical filter state | Awaits side-by-side UAT |

### 🟡 Architectural completeness gaps

| Item | Why it matters |
|---|---|
| Upload-Indexing Centralization Phase 3b (2 endpoints unwired) | External SPA + Teams app + chunked uploads + chat persist don't auto-index |
| Upload-Indexing Centralization Phase 4 (decommission wizard-side trigger) | Wizards bypass the BFF seam; goal was single seam, current state is dual-path |
| `RESTART-helper-not-running-mystery.md` never explicitly closed | Open question: is the seam working, or is the tactical wizard-side bypass masking a deeper bug? |
| Task 105 — Dataverse lookup→lookup field mappings (6 entity chains) | Without these, BU change does NOT cascade to new records' parents/documents through Dataverse — only through wizard code |
| Task 110 — drop text fallback + text column post-soak | Tech debt: dual fields, dual code paths, dual UI, indefinitely |
| Task 111 — Phase G wrap-up (lessons, runbook, pattern doc) | Project knowledge not codified |

### 🔵 External dependency (NOT R1 scope but blocking)

| Item | Impact |
|---|---|
| **AI Search indexer pipeline finding** — files land in SPE; AI Search indexer never pulls them | Blocks Protected-Matter graduation criterion + makes E2E search demonstrably broken |

### ⚪ Out of scope for R1 (candidates for R2 or later)

| # | Item |
|---|---|
| F2 | Container → Index map maintenance UI (hardcoded in backfill for R1) |
| F3 | Re-indexing API for moving documents between physical indexes |
| F4 | AI Search index provisioning automation |
| F5 | End-user index picker UI on records |
| F6 | Orphan document handling |
| F7 | Cross-tenant search |
| F8 | BU-change auto-sync / fan-out (INV-3 coexistence is the R1 design — may want explicit fan-out in R2) |

---

## 6. Lessons learned (should inform R2 design)

From `projects/spaarke-multi-container-multi-index-r1/notes/lessons-learned.md` and observed patterns:

1. **Caller-wiring gap recurred 3 times** (CreateProjectWizard main.tsx, DocumentUploadWizard orchestrator, Matter wizard cascade). When a task adds an optional parameter or new helper, sub-agents stop at the helper; the caller wiring slips. → R2 tasks must decompose "add helper" + "wire caller" as **two explicit tasks**, or each implementation task must produce the caller diff.
2. **Test patterns must match production patterns**. Task 021's INV-5 unit test mocked `IWebApiLike` directly, masking the runtime failure mode where real `IDataService.retrieveRecord` was used. Mocks too clean = false confidence.
3. **Adapter wrappers introduce silent failures**. `_toWebApiLike(this._dataService)` adapter caused Matter cascade silent failure while WorkAssignment's direct pass succeeded. **R2 rule**: when two near-identical code paths give different runtime results, eliminate the divergence; don't paper over it.
4. **TS2786 latent issue** (`@types/react@16` vs `@19`) surfaced only on fresh worktree install. → R2 should add fresh-clone build verification when bumping shared deps.
5. **Two-overload design > optional parameter** for backward-compat (avoids Moq CS0854). Adopt for any R2 BFF interface extensions.
6. **Dataverse XSD rejects apostrophes** in PCF `description-key`. Worth a lint rule.
7. **Spec assumptions can be wrong** (Invoice wizard doesn't exist). R2 agents should be encouraged to do empirical verification before implementing from spec.
8. **Wave structure with explicit dependencies + parallel groups worked**. ~30 sub-agent dispatches across 11 waves, zero merge conflicts. Reproducible pattern for R2.
9. **Skill-driven deploy ordering paid off**. `pcf-deploy`, `bff-deploy`, `code-page-deploy` skills surfaced critical pre-deploy gotchas. R2 should continue to follow them verbatim.

---

## 7. Open architectural questions for R2 scope discussion

These need explicit answers in the R2 spec, since R1 left them ambiguous or unresolved:

1. **Single BFF seam (R1 design) vs dual-path (R1 reality)?** Should R2 finish the Upload-Indexing Centralization (wire Phase 3b + decommission wizard-side trigger), or accept the tactical wizard-side path as canonical and remove the unused BFF seam?
2. **Text field deprecation timing?** Drop `sprk_searchindexname` text column now (Task 110), or maintain dual fields through R2 as well?
3. **Dataverse field mappings (Task 105)?** Add the 6 lookup→lookup field mappings so BU changes cascade to new records via Dataverse, or accept wizard-only cascade as R1's INV-3 design intended?
4. **AI Search indexer pipeline (F1)?** Owned by R2, or spun out to a separate ops/infra project? Without it, R1's graduation criteria can't be checked.
5. **End-user index picker UI (F5)?** Was explicitly out of scope for R1; is it in scope for R2?
6. **Container → Index map maintenance UI (F2)?** Currently hardcoded in backfill scripts; if the catalog grows (more than 8 indexes), this becomes operationally painful.
7. **BU-change fan-out (F8)?** R1 chose INV-3 coexistence (BU change does NOT cascade to existing records). Should R2 add explicit fan-out as an operator-triggered action?
8. **Re-indexing API (F3)?** Moving a document between physical indexes (e.g., promoting a Matter to "Protected") currently requires manual operator intervention. R2 candidate.
9. **Hot-fix pattern (Section 4)** suggests integration-test gaps between PCF / code-page / BFF. Does R2 invest in contract tests across the seam?

---

## 8. Statistics (R1 outcome)

- **43 original tasks**: 39 ✅ + 1 🚫 deferred (Invoice wizard) + 3 🔲 UAT pending
- **3 scope extensions absorbed**: Upload-Indexing Centralization (4 phases, 50% shipped), Phase G Lookup-Driven Multi-Index (12 tasks, ~75% shipped), PR #380 Wizard Cascade Gap (1 PR shipped)
- **11 post-UAT hot-fix commits** (signal of integration-drift fragility)
- **~30 sub-agent dispatches** across 11 waves; **zero merge conflicts**
- **~190 new unit tests** (BFF 6121/0/109 + shared lib + code-page 168 + PCF 54)
- **Surfaces touched**: BFF Services/Ai (3 services + endpoint + new enqueuer), 5 wizard services, DocumentUploadWizard orchestrator, SemanticSearchControl PCF (3 version bumps), SemanticSearch code page, 3 backfill scripts + 1 migration script, 1 operator runbook, arch-doc update
- **No regressions** in pre-existing test suite (NFR-02 verified)

---

## 9. Pointers

- R1 project folder: [`../spaarke-multi-container-multi-index-r1/`](../spaarke-multi-container-multi-index-r1/)
- R1 spec: [`../spaarke-multi-container-multi-index-r1/spec.md`](../spaarke-multi-container-multi-index-r1/spec.md)
- R1 design: [`../spaarke-multi-container-multi-index-r1/design.md`](../spaarke-multi-container-multi-index-r1/design.md)
- R1 lessons: [`../spaarke-multi-container-multi-index-r1/notes/lessons-learned.md`](../spaarke-multi-container-multi-index-r1/notes/lessons-learned.md)
- Upload-Indexing Centralization design: [`../spaarke-multi-container-multi-index-r1/notes/upload-indexing-centralization-design.md`](../spaarke-multi-container-multi-index-r1/notes/upload-indexing-centralization-design.md)
- Upload-Indexing Centralization tracker: [`../spaarke-multi-container-multi-index-r1/notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md`](../spaarke-multi-container-multi-index-r1/notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md)
- Post-UAT fixes + indexer finding: [`../spaarke-multi-container-multi-index-r1/notes/handoffs/post-uat-fixes-and-indexer-finding.md`](../spaarke-multi-container-multi-index-r1/notes/handoffs/post-uat-fixes-and-indexer-finding.md)
- Helper-not-running mystery: [`../spaarke-multi-container-multi-index-r1/notes/handoffs/RESTART-helper-not-running-mystery.md`](../spaarke-multi-container-multi-index-r1/notes/handoffs/RESTART-helper-not-running-mystery.md)
- Master-merge commits ranging from `254302259` (project scaffolding, 2026-06-11) through `7142e06da` (PR #380, 2026-06-11) plus the in-flight Phase G + Upload-Indexing extensions before scaffolding PR

---

*This document is the working brief for the R2 scope discussion. Update as R2 scope decisions are made.*
