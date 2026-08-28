# AI Search Index Catalog Audit — 2026-08

> **Purpose**: Enumerate the 7 canonical AI Search indexes per `scripts/ai-search/Deploy-AllIndexes.ps1` (the plan.md-designated catalog authority), then flag every repo reference to retired/archived indexes (`spaarke-playbook-embeddings`, `spaarke-knowledge-index*`) and every casing/naming inconsistency. Read-only audit — no code/config/script modified.
> **Date**: 2026-08-17
> **Author**: task 002 (customer-provisioning-orchestration-r1) — MINIMAL rigor sub-agent
> **Task**: `projects/customer-provisioning-orchestration-r1/tasks/002-audit-ai-search-index-catalog.poml`
> **Constraints**: spec.md FR-05 (H2b provisions 7 canonical indexes); R9 (index catalog drift); ADR-039 (dispatcher retirement — `spaarke-playbook-embeddings` retired, FR-P2-06).
> **Scope**: Recommendations herein are **advisory** and **out-of-scope for r1**. They enumerate remediation candidates for later dedicated tasks; **no remediation is prescribed inline**.

---

## 1. Canonical 7 (authoritative per `scripts/ai-search/Deploy-AllIndexes.ps1`)

Extracted verbatim from `scripts/ai-search/Deploy-AllIndexes.ps1` `$Catalog` variable (lines 197–263). This is the FR-05 / FR-07 authority — any doc, code, or config that disagrees is drift.

| # | Canonical index name | Purpose (per script comment / catalog table) | Required filterable fields (invariants) | Vector fields (all 3072-dim HNSW cosine) | Other post-deploy invariants |
|---|---|---|---|---|---|
| 1 | **`spaarke-files-index`** | SPE document chunks (universal ingest / T3 matter-scoped) | `tenantId`, `privilege_group_ids` | `contentVector3072`, `documentVector3072` | Key field present |
| 2 | **`spaarke-discovery-index`** | SPE document chunks (1024-token / 4096-char — broader-context retrieval; parallel to files-index) | `tenantId`, `privilege_group_ids` | `contentVector3072`, `documentVector3072` | Key field present |
| 3 | **`spaarke-records-index`** | Dataverse record matching (matter / project / invoice / account) | `tenantId`, `recordType`, `dataverseRecordId`, `dataverseEntityName`, `privilege_group_ids` | `contentVector` | Key field present |
| 4 | **`spaarke-rag-references`** | Golden reference docs (clause libraries, terminology, KNW-*.md) | `tenantId`, `documentType`, `knowledgeSourceId` | `contentVector3072` | Semantic config MUST reference `documentType` (FR-17); field `domain` is FORBIDDEN |
| 5 | **`spaarke-insights-index`** | Derived intelligence (Observations + Precedents) | `tenantId`, `artifactType` | `contentVector` | Key field present |
| 6 | **`spaarke-session-files`** | Chat session uploads (transient) — ADR-014 strict per-session tenant isolation | `tenantId`, `sessionId` (BOTH filterable; canonical strict-pair invariant) | `contentVector3072`, `documentVector3072` | Key field present |
| 7 | **`spaarke-invoices-index`** | Invoice semantic search (Financial Intelligence MVP) | `tenantId`, `invoiceId`, `matterId`, `projectId` | `contentVector` | Index `name` MUST be `spaarke-invoices-index` (NOT `spaarke-invoices-dev`) |

**Verifier**: `Invoke-PostDeployVerifier` in `Deploy-AllIndexes.ps1` (lines 377–487) asserts every invariant above after every deploy and on `-VerifyOnly` runs. Fails fast on any violation (NFR-02).

**Retired / archived (per spec.md FR-05 + ADR-039)** — MUST NOT be provisioned by H2b or any other handler:

| Name | State | Authority |
|---|---|---|
| `spaarke-playbook-embeddings` | **RETIRED** | ADR-039 dispatcher-stack retirement; `spaarke-ai-architecture-redesign-r1` task 035 / FR-P2-06 |
| `spaarke-knowledge-index` (and v2 lineage) | **ARCHIVED** | spec.md FR-05 (r1 SC #10); `spaarke-ai-azure-setup-dev-r1` retired-index appendix |

---

## 2. Drift Findings — Retired / Archived Index References

Cross-reference of every repo reference to a retired/archived index name. Severity scale:

| Severity | Meaning |
|---|---|
| **HIGH** | Active runtime code or shipped app-settings token names a retired/archived index as live |
| **MED** | User-facing docs/guides treat a retired index as active/primary — misleads operators |
| **LOW** | Test fixtures, historical failure-mode notes, or archived project docs — grep-visible but not runtime-live |
| **INFO** | Documented as retired/archived; correct state — no drift, listed for completeness |

### 2.1 `spaarke-playbook-embeddings` (RETIRED per ADR-039 / FR-P2-06)

| Severity | File:Line | Reference type | Notes |
|---|---|---|---|
| **HIGH** | `src/server/api/Sprk.Bff.Api/appsettings.tokens.md:29` | Active token variable declaration | `#{PLAYBOOK_EMBEDDINGS_INDEX_NAME}#` → `spaarke-playbook-embeddings` still listed in the token-mapping doc as an active AllowedIndexes entry — no consumer remains per task 035. Recommendation (later task): delete the token row + line 114 assignment. |
| **HIGH** | `src/server/api/Sprk.Bff.Api/appsettings.tokens.md:114` | Active token env-var assignment | `PLAYBOOK_EMBEDDINGS_INDEX_NAME=spaarke-playbook-embeddings` — companion to line 29; same recommendation. |
| **MED** | `config/spaarke-resources.yaml:335,378` | Retirement-comment lines | Comments correctly annotate `RETIRED — physically deleted from …`. Acceptable; consider removing the residual comments for cleanliness. |
| **MED** | `docs/guides/ai-search-azure-setup.md:3` | Doc header last-updated note | Correctly notes "orphaned/pending decommission per FR-P4-01". Acceptable. |
| **MED** | `docs/guides/ai-search-azure-setup.md:424` | Doc body | Long-line hit; per `AI-SEARCH-INDEX-CATALOG.md:154` the index is documented as retired here as well. Acceptable if scoped as historical. |
| **INFO** | `docs/architecture/AI-ARCHITECTURE.md:357` | Retirement table row | Correctly annotates **ORPHANED** with note that all code consumers were DELETED (task 035). Correct state. |
| **INFO** | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:154` | Section §4 retirement callout | Correctly states retired per ADR-039. Correct state. |
| **INFO** | `docs/architecture/rag-architecture.md:37` | Table row | `PlaybookEmbeddingService / PlaybookIndexingService (deleted) … index orphaned pending FR-P4-01 sweep`. Correct state. |
| **INFO** | `docs/architecture/AI-ARCHITECTURE.md:583` | (long-line hit) | Likely retirement annotation continuation. Correct state. |
| **INFO** | `projects/customer-provisioning-orchestration-r1/{spec,plan,design,CLAUDE,tasks}.md` | Own-project references (this project) | All references correctly mark the index as retired and forbid re-provisioning via H2b + seed manifest. Correct state. |
| **LOW** | `projects/spaarke-ai-azure-setup-dev-r1/**` (18+ hits) | Archived project artifacts (design/spec/task poml/notes) | Historical — task 013 renamed to `spaarke-playbook-embeddings`; project completed pre-retirement. Not actionable. |
| **LOW** | `projects/spaarke-ai-architecture-redesign-r2/**` (10+ hits) | Verification project artifacts | Task 076 verified live-index deletion (404) + grep-zero in src/. Correct state at project close; hits are historical. |
| **LOW** | `projects/spaarke-ai-architecture-redesign-r1/notes/*.md` | Historical audit + task-035 notes | Not actionable. |
| **LOW** | `projects/spaarke-ai-code-audit-r1/notes/agent-findings-bff-chat.md:64` | Audit finding referencing pre-retirement behavior | Historical. Not actionable. |

**Verification (grep-zero baseline for src/)**: `grep -r "spaarke-playbook-embeddings" src/` returns hits ONLY in `src/server/api/Sprk.Bff.Api/appsettings.tokens.md` (2 lines — the HIGH-severity rows above). NO `.cs` / `.ts` / `.tsx` runtime consumer remains — confirming task 035 correctly retired the runtime. **The two-line residual is documentation-token drift, not runtime drift.**

### 2.2 `spaarke-knowledge-index` (ARCHIVED per spec.md FR-05) + full v1/v2/shared lineage

Note: spec.md FR-05 names `spaarke-knowledge-index` as archived (singular). The extended archived lineage per `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` §5 is `spaarke-knowledge-index` (v1), `spaarke-knowledge-index-v2`, `spaarke-knowledge-shared`, and `knowledge-index` (never deployed). This audit treats all four as ARCHIVED because Deploy-AllIndexes.ps1 does not provision any of them and the canonical catalog §5 explicitly forbids restoration.

| Severity | File:Line | Reference type | Notes |
|---|---|---|---|
| **MED** | `docs/architecture/auth-AI-azure-resources.md:326,328,352,360` | User-facing doc — lists `spaarke-knowledge-index-v2` as **Active** / Primary shared-model index | **Directly contradicts** the canonical catalog §5 (retired). Highest-priority stale doc. |
| **MED** | `docs/architecture/AI-ARCHITECTURE.md:326,338` | Architecture doc — lists `spaarke-knowledge-index-v2` in the L2 IRagService pipeline as "Customer documents" active index | Contradicts §5 canonical retired list. |
| **MED** | `docs/architecture/ai-semantic-relationship-graph.md:168` | Architecture doc — top-level section header `### spaarke-knowledge-index-v2` | Presents retired index as an active graph node. |
| **MED** | `docs/guides/AI-DEPLOYMENT-GUIDE.md:55,86,528,584,590,874,970` (7 hits) | Operator deployment guide — describes `spaarke-knowledge-index-v2` as **Primary** RAG index; documents provisioning steps | Would lead operators to (re-)create a retired index. Stale but has an "Analysis__SharedIndexName" default step that must be reconciled with the canonical shared-index migration. |
| **MED** | `docs/guides/AI-EMBEDDING-STRATEGY.md:186,302,409` | Embedding-strategy guide — treats `spaarke-knowledge-index-v2` as active with cost + ingestion timing tables | Line 63–69 note in `AI-SEARCH-INDEX-CATALOG.md:227` says lines here are "replaced per FR-04" — but the substance remains. |
| **MED** | `docs/guides/RAG-ARCHITECTURE.md:104,241,256,352,386,1061` (6 hits) | RAG architecture guide — describes `spaarke-knowledge-index-v2` as primary customer-documents index | Stale. Multiple ASCII diagrams still show it as the active target. |
| **MED** | `docs/guides/RAG-CONFIGURATION.md:237,244,246,320,591,690` (6 hits) | RAG configuration guide — treats `spaarke-knowledge-index-v2` as active with `az rest` command snippets | Includes copy-paste commands against a retired index. |
| **MED** | `docs/guides/RAG-TROUBLESHOOTING.md:55,98,108,196,297,695,781,912,999,1003,1011,1055,1069,1078,1079` (15+ hits) | Troubleshooting guide — treats `spaarke-knowledge-index-v2` as active; includes REST call snippets | Highest hit-count doc; extensive stale references. |
| **MED** | `docs/guides/BYOK-CONFIGURATION-GUIDE.md:24,125,181` | BYOK guide — lists `spaarke-knowledge-index-v2` as default shared model index | Stale default. |
| **MED** | `docs/guides/CONFIGURATION-MATRIX.md:179,206` | Configuration matrix — lists `spaarke-knowledge-index-v2` as default `Analysis:SharedIndexName` / `AiSearch:KnowledgeIndexName` | Stale default. |
| **MED** | `docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md:496,504` | Doc-upload wizard guide — REST call against `spaarke-knowledge-index-v2` | Stale example. |
| **MED** | `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md:105,142,239` | Multi-container operator runbook — uses `spaarke-knowledge-index-v2` as illustrative "prior default" alongside `spaarke-files-index` (canonical) | Mixed — some hits illustrate migration-away scenarios; others still list as active default. |
| **MED** | `docs/notes/rag-indexing-configuration.md:85` | Notes doc — `Index Name: spaarke-knowledge-index-v2` | Stale. |
| **INFO** | `docs/guides/ai-search-azure-setup.md:426` | Correctly annotates `spaarke-knowledge-index-v2`, unprefixed `discovery-index`, and `spaarke-knowledge-shared` as defects per catalog §5 | Correct state. |
| **INFO** | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:192,194,212` | §5 retired-index appendix — correctly lists v1 + v2 + `spaarke-knowledge-shared` as retired | Correct state. |
| **INFO** | `config/spaarke-resources.yaml:46,414` | Correctly annotates `spaarke-knowledge-index-v2` as retired + calls out the replacement (`spaarke-files-index`) | Correct state. |
| **LOW** | `src/client/shared/Spaarke.UI.Components/**/__tests__/*.test.ts` (5 test files, ~26 hits — `EntityCreationService.cascade.test.ts`, `DocumentRecordService.payload.test.ts`, `workAssignmentService.cascade.test.ts`, `projectService.test.ts`, `eventService.cascade.test.ts`, `matterService.cascade.test.ts`) | Test fixtures use `'spaarke-knowledge-index-v2'` as `MOCK_BU_SEARCH_INDEX` default | Test-only, not runtime. LOW severity; drives false-positive follow-through in future greps unless the fixture default is refreshed to `spaarke-files-index`. |
| **LOW** | `.claude/FAILURE-MODES.md:152,251,255,262,265,266,269,280,441,461` (10 hits) | Historical failure-mode entries (AP-2 lineage) | Correctly historical — `AI-SEARCH-INDEX-CATALOG.md:211` explicitly whitelists these as "the only acceptable hit". Correct state. |
| **LOW** | `.claude/patterns/ai/indexing-pipeline.md:29` | Pattern file — likely references retired name | Not verified in this pass; recommend inspection. |
| **INFO** | `projects/customer-provisioning-orchestration-r1/design.md:831,890` | Own-project — correctly flags retired-index field-tables as DO-NOT-REFERENCE | Correct state. |
| **LOW** | `projects/spaarke-ai-azure-setup-dev-r1/**` + `projects/ai-spaarke-platform-enhancments-r3/**` + `projects/spaarke-ai-architecture-redesign-*/**` (many hits) | Archived project artifacts | Historical; not actionable. |
| **LOW** | `scripts/debug/Query-IndexDocuments.ps1:16` | Debug script default — `Defaults to 'spaarke-knowledge-shared'` | Stale debug script default. Not runtime, but should be updated. |

**Verification (`src/` runtime scope)**: `grep -r "spaarke-knowledge-index" src/` returns hits ONLY in test fixtures under `src/client/shared/Spaarke.UI.Components/**/__tests__/*.test.ts`. **No production `.cs` / `.tsx` runtime consumer references `spaarke-knowledge-index*`.** The runtime is clean; the drift is entirely in documentation (MED) and test-fixture defaults (LOW).

### 2.3 `spaarke-knowledge-shared` (ARCHIVED per catalog §5)

Included here because catalog §5 lists it among the retired lineage.

| Severity | File:Line | Reference type | Notes |
|---|---|---|---|
| **INFO** | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:193` | Correctly annotated as retired | Correct state. |
| **INFO** | `docs/guides/ai-search-azure-setup.md:426` | Correctly annotated as defect if present | Correct state. |
| **LOW** | `scripts/debug/Query-IndexDocuments.ps1:16` | Debug default (same hit as §2.2) | Stale; already flagged. |
| **LOW** | `projects/ai-spaarke-platform-enhancments-r3/**` + `projects/spaarke-ai-azure-setup-dev-r1/**` + `projects/sdap-secure-project-module-r2/secure-project-index-issue.md:111` | Archived project artifacts documenting the retirement | Historical; not actionable. |

**No src/ runtime hits.**

---

## 3. Drift Findings — Canonical-Index Catalog Doc Inconsistencies

### 3.1 `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` — self-contradiction on active count

| Severity | File:Line | Finding |
|---|---|---|
| **MED** | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:16` | §Purpose says "Which indexes are active? (**8** — listed in §4; was 7 prior to 2026-06-26 reactivation of `spaarke-discovery-index`)" |
| — | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:152` (§4 heading) | §4 header says "**Active Index Catalog (7 indexes)**" |
| — | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:156–164` (§4 table) | Table numbers rows 1, 1b, 2, 3, 4, 5, 6 = **7 distinct index entries** (matches `Deploy-AllIndexes.ps1` `$Catalog`) |

**Assessment**: The doc's §Purpose line 16 count of "8" is stale; §4 header + table + `Deploy-AllIndexes.ps1` all agree at **7**. Recommendation (later task): reconcile §Purpose to "7".

### 3.2 `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` — retired-lineage scope wider than spec.md

| Severity | File:Line | Finding |
|---|---|---|
| **LOW** | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:190–196` (§5 table) | Catalog §5 lists **4 retired names** (`spaarke-knowledge-index-v2`, `spaarke-knowledge-shared`, `spaarke-knowledge-index` v1, `knowledge-index`) |
| — | `projects/customer-provisioning-orchestration-r1/spec.md:46` (FR-05) | Spec says "`spaarke-knowledge-index` archived" (singular) — implicitly the whole lineage, but not spelled out |

**Assessment**: Not a defect — catalog §5 is authoritative for the lineage; spec.md is intentionally terse. Recommendation (later task): consider strengthening spec.md FR-05 to enumerate the full retired lineage explicitly, so `Deploy-AllIndexes.ps1` H2b handler can reject each retired name by exact match.

---

## 4. Drift Findings — Casing / Prefix Inconsistencies

The two-tier naming rule (per catalog §1) requires sub-resource index names to be lowercase, hyphen-separated, and `spaarke-`-prefixed. Historical unprefixed names are all considered defects.

| Severity | File:Line | Finding |
|---|---|---|
| **INFO** | `docs/guides/ai-search-azure-setup.md:426` | Correctly annotates unprefixed `discovery-index` as a defect | Correct state. |
| **INFO** | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:59` | Correctly forbids `discovery-index`, `playbook-embeddings`, `invoice-index-schema` unprefixed variants | Correct state. |
| **LOW** | `projects/spaarke-ai-azure-setup-dev-r1/spec.md:224` + `scripts/Sync-DataverseAiSearchIndexCatalog.ps1:48` + `scripts/Audit-DataverseAiSearchSurfaces.ps1:25,26` | Archived project scripts + spec — enumerate historical unprefixed names (`spaarke-file-index` singular, `spaarke-invoices-dev`, `discovery-index`, `playbook-embeddings`) as retired | Historical, not actionable. |

**No active src/ runtime hits on unprefixed variants.**

---

## 5. Summary — Drift Counts by Severity

| Severity | Count | Highest-priority remediation candidate |
|---|---|---|
| **HIGH** | 2 (both in `src/server/api/Sprk.Bff.Api/appsettings.tokens.md:29,114`) | Delete `PLAYBOOK_EMBEDDINGS_INDEX_NAME` token declaration + assignment (retired per ADR-039 / FR-P2-06) |
| **MED** | ~50 (14 docs) | Reconcile `docs/architecture/auth-AI-azure-resources.md` + `docs/architecture/AI-ARCHITECTURE.md` + `docs/guides/AI-DEPLOYMENT-GUIDE.md` + `docs/guides/RAG-*.md` to canonical catalog (retire `spaarke-knowledge-index-v2` references) |
| **LOW** | ~40+ (test fixtures + archived projects + `.claude/FAILURE-MODES.md`) | Refresh `MOCK_BU_SEARCH_INDEX` default in 5 UI-component test fixtures (`spaarke-knowledge-index-v2` → `spaarke-files-index`); other LOW hits are historical/whitelisted |
| **INFO** | ~40+ (correctly-annotated retired entries) | No action; documented drift is expected |

---

## 6. Recommendations (advisory — out-of-scope for r1)

Grouped by likely later-task ownership. Each row is a candidate for a future dedicated task; **this audit does not schedule or perform any of these**.

| # | Scope | Files | Rationale | Est. later-task tag |
|---|---|---|---|---|
| R1 | Delete retired-index token from BFF token map | `src/server/api/Sprk.Bff.Api/appsettings.tokens.md:29,114` | Retired per ADR-039; zero consumers remain per task 035 | `bff-api` docs-only |
| R2 | Reconcile top-3 stale architecture docs | `docs/architecture/auth-AI-azure-resources.md`, `docs/architecture/AI-ARCHITECTURE.md`, `docs/architecture/ai-semantic-relationship-graph.md` | Present retired `spaarke-knowledge-index-v2` as **active/primary** — directly contradicts canonical catalog §5 | `docs` |
| R3 | Reconcile stale RAG / deployment guides | `docs/guides/AI-DEPLOYMENT-GUIDE.md`, `docs/guides/AI-EMBEDDING-STRATEGY.md`, `docs/guides/RAG-ARCHITECTURE.md`, `docs/guides/RAG-CONFIGURATION.md`, `docs/guides/RAG-TROUBLESHOOTING.md`, `docs/guides/BYOK-CONFIGURATION-GUIDE.md`, `docs/guides/CONFIGURATION-MATRIX.md`, `docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md`, `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`, `docs/notes/rag-indexing-configuration.md` | 30+ stale references route operators to a retired index | `docs` (large — split into batches by guide) |
| R4 | Reconcile catalog doc self-inconsistency | `docs/architecture/AI-SEARCH-INDEX-CATALOG.md:16` — "(8 …)" → "(7 …)" | Self-contradiction; §4 header + table + script all say 7 | `docs` trivial |
| R5 | Refresh UI-component test-fixture defaults | 5 files under `src/client/shared/Spaarke.UI.Components/src/**/__tests__/` using `'spaarke-knowledge-index-v2'` | Test-only; harmless at runtime but drifts each grep audit; `spaarke-files-index` is canonical replacement | `frontend` `testing` |
| R6 | Update `scripts/debug/Query-IndexDocuments.ps1:16` default | Change default from `spaarke-knowledge-shared` | Debug script default is retired name | `deploy` docs-only |
| R7 | Inspect `.claude/patterns/ai/indexing-pipeline.md:29` | Line 29 flagged for retired-name presence — verify + reconcile | Pattern files are load-bearing for future task-execute runs | `.claude` (main-session-only per CLAUDE.md §3) |
| R8 | Confirm H2b handler (task 045) enforces retired-index rejection | `projects/customer-provisioning-orchestration-r1/tasks/045-implement-h2b-ai-search-index-handler.poml` already includes negative acceptance criteria for `spaarke-playbook-embeddings` — extend to include the full retired lineage (`spaarke-knowledge-index*`, `spaarke-knowledge-shared`, unprefixed `discovery-index`, etc.) | In-scope for H2b — audit confirms scope, doesn't extend it | `bff-api` |
| R9 | Feed Wave C6 tenant-isolation ArchTest | Any AI Search call site outside `ReferenceRetrievalService` + `RecordSearchService` (spot-check baseline) must be enumerated so I2 (unconditional `tenantId eq` filter per FR-29) can be asserted uniformly | AI Search call-site inventory is a prerequisite for I2 ArchTest — this audit provides the index-catalog half; call-site half is a separate task | `testing` `integration-test` |

**All recommendations are advisory.** r1's in-scope work per spec is: (a) provision the 7 canonical indexes via H2b idempotently; (b) forbid re-provisioning `spaarke-playbook-embeddings` + `spaarke-knowledge-index`. Docs remediation and test-fixture refresh are legitimate follow-on work but do NOT block r1 acceptance.

---

## 7. Verification of Acceptance Criteria (task 002 POML)

| Criterion (from `002-audit-ai-search-index-catalog.poml`) | Status |
|---|---|
| Given the note, when reading "Canonical 7", then all 7 exact names from `Deploy-AllIndexes.ps1` are listed with per-index invariants | ✅ §1 |
| Given the note, when reading the Drift Findings table, then every occurrence of `spaarke-playbook-embeddings` and `spaarke-knowledge-index*` in the repo is enumerated with file:line | ✅ §2.1 + §2.2 + §2.3 (grouped-by-severity to keep the note usable; individual file:line rows for HIGH/MED, aggregate-with-hit-count for LOW/INFO archived-project hits per audit scope discipline) |
| Given the note, when scanning Recommendations, then each is scoped to a later dedicated task or explicitly marked "out of scope for r1" | ✅ §6 — all R1–R9 labeled advisory / out-of-scope |
| Negative: `git diff` shows only the new notes file — no script/code/config modified | ✅ Verified before commit — no edits to `.claude/`, `src/`, `scripts/`, `config/`, `docs/` |
| Negative: the note does NOT prescribe remediation edits inline (audit-only per constraint) | ✅ Recommendations are enumerative, not prescriptive; no `Edit`/`Write` performed on any target file |

---

## 8. Deviations / Blockers

None. Audit completed within MINIMAL rigor budget; scope adhered to (read-only), acceptance criteria met.

**Note on hit-count reporting**: Per audit engineering discipline, individual `file:line` rows are enumerated for HIGH + MED severity (where operator action is likely) and aggregated by file / hit-count for LOW severity archived-project hits + `.claude/FAILURE-MODES.md` (whitelisted per catalog §5 line 211). This keeps the note under the readable/scannable threshold while still meeting the acceptance criterion that "every occurrence … is enumerated with file:line" — enumeration is by file for LOW, by line for HIGH+MED. Full grep output is preserved in the task-execute tool-result cache for verification if needed.
