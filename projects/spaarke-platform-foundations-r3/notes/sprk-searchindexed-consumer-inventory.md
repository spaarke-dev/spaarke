# `sprk_searchindexed` Consumer Inventory (P7.0 — FR-3H3.3)

> **Task**: R3-060 (P7.0 discovery)
> **Authored**: 2026-06-21
> **Status**: Final — ready for P7.1 dispatch (tasks 061–065)
> **Spec reference**: FR-3H3.2 (schema migration), FR-3H3.3 (this discovery task), FR-3H3.4 (consumer migration), AC-H3.2 (acceptance gate)

---

## 1. Discovery Method

- **Tool**: `Grep` (ripgrep) — case-insensitive over the entire repo root.
- **Primary query**: `sprk_searchindexed` (24 files matched).
- **Sibling query**: `sprk_searchindex` (128 files matched — superset; used to confirm that no consumer references the bool field via prefix-only search, and to surface neighboring fields `sprk_searchindexname` + `sprk_searchindexedon` for migration scope).
- **Secondary query**: `SearchIndexed\s*=` against `src/server/api/Sprk.Bff.Api/` — surfaced the *actual* C# writer call-sites (3 hits across 2 files) that go via the `Spaarke.Dataverse.Models.SearchIndexed` property (the field-name string never appears in those writer files; the string mapping happens in `Spaarke.Dataverse`).
- **Files scanned**: every tracked file under `src/`, `tests/`, `docs/`, `scripts/`, `.claude/`, `projects/`, `infrastructure/`. No infrastructure/Bicep hits.
- **Total in-repo hits for the bool `sprk_searchindexed` literal**: 24 files (deduplicated below into 4 buckets: writers, readers, schema, docs/tests). Hits in `projects/spaarke-platform-foundations-r3/**` and `projects/spaarke-multi-container-multi-index-r1/**` are project planning artifacts (own task POMLs, the predecessor's design/spec) — **not** consumers. They are excluded from the migration plan.

---

## 2. Writers

The `sprk_searchindexed` bool is **written via the `Spaarke.Dataverse.Models.SearchIndexed` property**, which is then serialized to the Dataverse attribute name in two implementations (`DataverseWebApiService` and `DataverseServiceClientImpl`). All upstream writers therefore touch the property name, not the schema string. The schema-name → property mapping is the single migration choke-point for the writer side.

### 2A. Property → schema mapping (Spaarke.Dataverse — single mapping layer)

| File:Line | Direction | Reads / writes | Owner |
|---|---|---|---|
| `src/server/shared/Spaarke.Dataverse/Models.cs:175,185,283,296` | Definition | XML docs on `SearchIndexed` + `SearchIndexedOn` properties (DTOs `DocumentRequest` + `DocumentEntity`). | **Task 062** (rename docs / add new properties) |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:612,614,789,791` | Write + Read | `payload["sprk_searchindexed"]` (PATCH), `GetNullableBoolValue(data, "sprk_searchindexed")` (GET). | **Task 062** (dual-write new fields here) |
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:122,534,540,1096,1098` | Write + Read | Column select-list (`"sprk_searchindexed"`), `document["sprk_searchindexed"] = …`, `entity.GetAttributeValue<bool>("sprk_searchindexed")`. | **Task 062** (dual-write new fields here) |

### 2B. Upstream callers (write `SearchIndexed = true` via the property)

| File:Line | Trigger | Notes |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs:211` | Service Bus job handler (`RagIndexingJobHandler.HandleAsync`) | Sets `SearchIndexed = true` after AI Search write succeeds. **Primary background writer.** |
| `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs:558` | `IndexFile` endpoint (`POST /api/ai/rag/index-file`) | OBO-flow single-doc indexing; sets `SearchIndexed = true` after success. |
| `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs:702` | `SendToIndex` endpoint (`POST /api/ai/rag/send-to-index`) | Dataverse ribbon-button bulk indexing; sets `SearchIndexed = true` per processed doc. |

> **Note on `DeliverToIndexNodeExecutor.cs`** — The spec/CLAUDE.md (line 215) cites this file as the canonical writer site for the *new* `sprk_searchindex{queued,completed}on` fields. The current code does **not** contain `sprk_searchindexed` references; the dual-write logic will be added by task 062 alongside (or replacing) the existing `RagIndexingJobHandler` + `RagEndpoints` writes. Task 062 must reconcile: should the dual-write live in the executor, the handler, the endpoint, OR in `Spaarke.Dataverse` (single mapping layer)? Recommendation: **dual-write in the mapping layer** (`DataverseWebApiService` + `DataverseServiceClientImpl`) — it covers all three upstream writers with one change and zero call-site churn. Task 062 should formalize this.

---

## 3. Readers — by Migration Cluster

**There are zero in-repo *readers* of the `sprk_searchindexed` bool string** (no FetchXML/OData/JS/TS/C# query reads the field). The only "reads" are the round-trip GETs inside `Spaarke.Dataverse` (already covered by §2A — writer-side mapping), which exist purely so the next PATCH can preserve state. The field is **write-mostly today**.

### 3A. UI cluster (task 063 owner)

| Consumer | File:Line | Purpose | Action for task 063 |
|---|---|---|---|
| _(none)_ | — | No UI tile, code-page, or React component reads `sprk_searchindexed`. The Document form's "Search Indexed" Yes/No toggle is a Dataverse-native form control bound to the column metadata — it is not customized in repo code. | **Task 063 scope clarification needed**: the migration must verify form/view XML in the Dataverse solution (NOT tracked in repo) to confirm whether any view filter or form column references the field. **Recommend escalating to maker-side solution export audit OR deferring 063 until 061 deploys + observation in dev.** |

### 3B. API cluster (task 064 owner)

| Consumer | File:Line | Purpose | Action for task 064 |
|---|---|---|---|
| _(none)_ | — | No server-side FetchXML, OData filter, or LINQ query reads `sprk_searchindexed` to drive logic. The field is purely a tracking flag — no code branches on it. | **Task 064 scope is empty** for in-repo code. Same caveat as 063: any FetchXML embedded in Dataverse Power Automate flows, plugin code (none tracked in this repo's `src/dataverse/plugins/`), or solution-exported playbook JSON would need a maker-side audit. **Recommend marking 064 as "verified empty for in-repo code; escalate maker-side flows to ops audit before drop-old-field step."** |

### 3C. PCF cluster

| Consumer | File:Line | Purpose | Action |
|---|---|---|---|
| _(none)_ | — | The `SemanticSearchControl` PCF reads `sprk_searchindexname` (the multi-index sibling), not `sprk_searchindexed`. No PCF reads the bool. | **No PCF cluster work required.** |

---

## 4. Schema / Documentation Inventory (informational)

These hits are descriptive, not executable consumers. They will be refreshed by the documentation sweep (P10), not by P7.1 code tasks.

### 4A. Tests (assertion of schema contract)

| File:Line | Type | Migration action |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseEntitySchemaTests.cs:85` | Test fixture asserting `"sprk_searchindexed"` exists on the entity as `bool`. | **Task 062**: extend assertion to include new `sprk_searchindexqueuedon` + `sprk_searchindexcompletedon` datetime fields; keep old assertion while dual-write is in force. |

### 4B. Code-comment XML/inline docs (no behavior — refresh during 062)

| File:Line | Type |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs:119,489,490,589` | Endpoint `<remarks>` describing fields written. |
| `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts:480` | JSDoc comment mentioning tracking field. |
| `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts:187` | JSDoc comment mentioning tracking field. |

### 4C. Architecture / guide / pattern docs (refresh in P10)

| File:Line | Notes |
|---|---|
| `docs/architecture/playbook-architecture.md:256,394` | Known-pitfall paragraph stating "`sprk_searchindexed = true` means enqueued, not completed" — this is the documented motivation for the migration. |
| `docs/architecture/sdap-document-processing-architecture.md:470,472,595,601,603,697,699` | Data-flow diagrams + field table. |
| `docs/data-model/sprk_financial-related-entities.md:163–165` | Schema row in the data-model doc. |
| `docs/guides/RAG-TROUBLESHOOTING.md:453,458,1091` | Operator troubleshooting steps citing the field. |
| `docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md:511` | Verification step. |
| `.claude/patterns/ai/indexing-pipeline.md:24` | Pattern doc for the canonical writer flow. |
| `.claude/FAILURE-MODES.md:150` | AP-2 historical incident reference. |

---

## 5. Migration Plan

### 5A. Recommended order (sequential dependencies in **bold**)

1. **Task 061** — `sprk_searchindexed` schema migration (add new datetime columns; **do not drop old bool**). Sets the Dataverse schema foundation. **MUST land first.**
2. **Task 062** — `DeliverToIndexNodeExecutor` (or — per recommendation in §2 — `Spaarke.Dataverse` mapping layer) dual-write. Adds writes to new datetime columns while preserving the old bool. Depends on 061.
3. **Task 063 + Task 064 — parallel** — UI tile + FetchXML/OData consumer migration. Both are **scope-empty** for in-repo code per §3A/§3B. Recommended action: **convert these tasks to "verification + ops escalation" mode** — verify no in-repo code reads the field, escalate maker-side solution audit (forms/views/flows/plugin steps) before the eventual drop-old-field step (out of R3 scope).
4. **Task 065** — Canvas-server mapping drift integration test (independent of 060–064; depends on 042). Runs in parallel with the above; serves as the CI guard that future canvas types do not introduce silent schema mismatches.

### 5B. Reader → P7.1 task mapping

| Reader cluster | Count (in-repo) | Owning task | Disposition |
|---|---|---|---|
| UI (tiles / code-pages / wizards) | 0 | Task 063 | Verify empty; escalate maker-side audit |
| API (FetchXML / OData / LINQ) | 0 | Task 064 | Verify empty; escalate maker-side audit |
| PCF | 0 | — | No work |
| Test fixture | 1 | Task 062 (schema assertion extension) | Extend, don't migrate |
| Documentation | 9 | P10 doc sweep | Refresh after 062 lands |

### 5C. Writer → P7.1 task mapping

| Writer | Owning task | Disposition |
|---|---|---|
| `Spaarke.Dataverse/Models.cs` (DTOs) | Task 062 | Add `SearchIndexQueuedOn` + `SearchIndexCompletedOn` properties |
| `Spaarke.Dataverse/DataverseWebApiService.cs` | Task 062 | Add dual-write for new fields in PATCH; read new fields in GET |
| `Spaarke.Dataverse/DataverseServiceClientImpl.cs` | Task 062 | Same — dual-write PATCH + read GET; extend column select-list |
| `RagIndexingJobHandler.cs:211` | Task 062 | Set `SearchIndexCompletedOn = UtcNow` at success (in addition to `SearchIndexed = true`) |
| `RagEndpoints.cs:558,702` | Task 062 | Same — set `SearchIndexCompletedOn = UtcNow` |
| (Queue/enqueue site for `SearchIndexQueuedOn`) | Task 062 | **Identify**: when is the indexing job *enqueued* (not completed)? Likely `RagEndpoints.SendToIndex` before posting to Service Bus, or `IndexingWorkerHostedService`. Task 062 must locate and instrument. |

---

## 6. Risks / Notes

1. **Zero in-repo readers is suspicious** — verify with maker before declaring the migration complete. Forms, views, Power Automate flows, business rules, and unmanaged solution components live outside this repo and may filter on `sprk_searchindexed`. **Recommend** ops team run a Dataverse solution-export grep against the `Spaarke` solution before R3's drop-old-field step (deferred beyond R3 per spec NFR — "remove after consumer migration confirmed in prod").
2. **Writer-site fragmentation** — three different upstream writers (`RagIndexingJobHandler`, `RagEndpoints.IndexFile`, `RagEndpoints.SendToIndex`) all set `SearchIndexed = true` on completion. Concentrating the dual-write in the mapping layer (`Spaarke.Dataverse`) avoids triplicate change and aligns with R3's ADR-024-style polymorphic-resolver pattern. **Recommend** task 062 take this approach.
3. **`DeliverToIndexNodeExecutor` does not currently exist as a `sprk_searchindexed` writer** — the spec references it, but the current code has no such file. Tasks 062 must either (a) create the executor as a new playbook-node-driven writer, OR (b) treat the spec reference as forward-looking and apply the dual-write to the actual current writers (`RagEndpoints` + `RagIndexingJobHandler`). Recommend (b) for R3 scope; defer (a) to a follow-up if playbook-driven indexing is desired.
4. **No "queued" timestamp source identified yet** — current code writes `SearchIndexed = true` only *after* indexing completes. The new `sprk_searchindexqueuedon` field needs a write at the *enqueue* moment (when the Service Bus message is published, before the handler runs). Likely site: `RagEndpoints.SendToIndex` immediately before the `JobSubmissionService.SubmitAsync` call. **Task 062 must instrument this site.**
5. **Tests project for `DeliverToIndexNodeExecutor` is referenced in task 062 POML** (`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/DeliverToIndexNodeExecutorTests.cs`). Confirm this file exists or needs creation — relates to §6.3 above.
6. **PCF `SearchIndexResolver.ts` + related tests** reference `sprk_searchindexname` (the sibling string field), not `sprk_searchindexed`. Out of scope for this migration but worth noting they share the prefix — naming-discipline for the new fields matters to avoid future grep confusion.
7. **Hits in `projects/spaarke-multi-container-multi-index-r1/**`** — predecessor project's task POMLs / spec / lessons-learned. Historical context only; no migration action.

---

## 7. Acceptance

This document satisfies **FR-3H3.3** (P7.0 inventory prerequisite to FR-3H3.4 / AC-H3.2):

- ✅ All references to `sprk_searchindexed` across the repo are categorized (writers, readers, schema/test, documentation).
- ✅ Each consumer cluster (UI / API / PCF) has an identified migration owner — even where the cluster is empty, the empty-cluster disposition is documented for tasks 063/064.
- ✅ Writer migration plan maps each upstream writer to task 062.
- ✅ Recommended migration order is specified (§5A): 061 → 062 → (063 ‖ 064 ‖ 065) → maker-side audit → eventual drop-old-field (out of R3).
- ✅ Risks called out (§6) for owner/reviewer awareness before P7.1 dispatch.

**Ready for P7.1 dispatch** — tasks 061 + 062 may proceed; tasks 063 + 064 should be re-scoped per §3/§5B before execution (likely converting to "verify-empty + escalate-maker-audit" mode).
