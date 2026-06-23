# sprk_document — Search-Index Lifecycle Fields

> **Project**: spaarke-platform-foundations-r3 (Part 3 / Workstream H3 — FR-3H3.2)
> **Task**: R3-061 (schema migration) → R3-062 (mapping-layer dual-write) → R3-063/064 (consumer migration; verify-empty per inventory) → future R4 task (drop legacy bool)
> **Created**: 2026-06-21
> **Status**: Phase 1 deployed to spaarkedev1 (new datetime fields added; legacy bool preserved for dual-write transition)
> **Schema script**: [`scripts/Migrate-SprkSearchIndexedSchema.ps1`](../../scripts/Migrate-SprkSearchIndexedSchema.ps1) (idempotent)

> **Note on scope**: This document covers ONLY the search-index lifecycle attributes on `sprk_document`. The full `sprk_document` column catalog lives in [`sprk_financial-related-entities.md`](sprk_financial-related-entities.md) (rows tagged "Document / sprk_document"). The new fields are also reflected there for catalog completeness; this doc is the authoritative migration narrative.

---

## Why this migration

The legacy `sprk_searchindexed` (Boolean / Yes-No) column was authored to track whether a document had been indexed by the Azure AI Search side of the RAG pipeline. In practice it became misleading:

1. **Naming asymmetry vs. semantics** — "searchindexed = true" is read by humans as "indexing has happened," but the writer call-sites (`RagIndexingJobHandler`, `RagEndpoints.IndexFile`, `RagEndpoints.SendToIndex`) only set it **after** the AI Search write completes. There is no field representing "the indexing job is en-route" — so an operator looking at the Dataverse row cannot distinguish "never indexed" from "queued, still running" without inspecting Service Bus.
2. **No completion timestamp** — `sprk_searchindexedon` exists but is not consistently written; the boolean carries the only state signal at most call-sites.
3. **Documented known-pitfall** — Surfaced as a "Known Pitfall" in [`docs/architecture/playbook-architecture.md`](../architecture/playbook-architecture.md) (lines 256, 394: "`sprk_searchindexed = true` means enqueued, not completed").

R3 FR-3H3.2 replaces the single bool with **two explicit DateTime columns** representing the two distinct lifecycle moments:

| Moment | New column | Set by |
|---|---|---|
| **Enqueued** — Service Bus message published, handler has not yet acknowledged | `sprk_searchindexqueuedon` | Producer side (e.g., `RagEndpoints.SendToIndex` immediately before `JobSubmissionService.SubmitAsync`). Task 062 instruments this site. |
| **Completed** — AI Search confirmed successful write of the document body+chunks | `sprk_searchindexcompletedon` | Consumer side (`RagIndexingJobHandler.HandleAsync` after AI Search returns success; `RagEndpoints.IndexFile` for OBO single-doc path). Task 062 instruments these sites. |

If only `sprk_searchindexqueuedon` is set, the document is in-flight. If both are set with `completedon >= queuedon`, the document is indexed. If neither is set, the document has never been submitted.

---

## Search-index lifecycle fields (`sprk_document`)

| # | Column | Type | Required | Format / DateTimeBehavior | Status | Purpose |
|---|---|---|---|---|---|---|
| 1 | `sprk_searchindexqueuedon` | DateTime | Optional | DateAndTime / UserLocal | **NEW** (deployed 2026-06-21 by R3-061) | Set when an indexing job is **enqueued** for this document. Pairs with `sprk_searchindexcompletedon` to give true "enqueued vs done" visibility. |
| 2 | `sprk_searchindexcompletedon` | DateTime | Optional | DateAndTime / UserLocal | **NEW** (deployed 2026-06-21 by R3-061) | Set when the AI Search indexer **confirms successful indexing**. Pairs with `sprk_searchindexqueuedon`. |
| 3 | `sprk_searchindexed` | Boolean (Yes/No) | Optional | — | **LEGACY — preserved for dual-write transition** (DO NOT remove in R3) | Original "is indexed" bool. Will be dual-written by the mapping layer (task 062) for the duration of R3 + one sprint after; removed in a future R4 task once consumer migration is confirmed in prod (per spec assumption, line 366). |
| 4 | `sprk_searchindexedon` | DateTime | Optional | DateAndTime / UserLocal | **LEGACY sibling — preserved** | Original "last indexed on" timestamp. Inconsistently written at call-sites today. Not part of the rename, but listed here for catalog completeness. Functionally superseded by `sprk_searchindexcompletedon` once consumer migration is done. |

---

## Dual-write transition contract

For the duration of R3 + one sprint after merge:

1. **Writers** (task 062, in the `Spaarke.Dataverse` mapping layer per inventory §6.2 recommendation) MUST update **both** the legacy `sprk_searchindexed` bool AND the appropriate new datetime column on every successful index operation:
   - On enqueue → set `sprk_searchindexqueuedon = UtcNow`.
   - On completion → set `sprk_searchindexcompletedon = UtcNow` AND `sprk_searchindexed = true` AND (if maintained) `sprk_searchindexedon = UtcNow`.
2. **Readers** (none in-repo per inventory §3 — verify-empty tasks 063/064; future maker-side audit for Power Automate flows / form columns / view filters) MAY continue reading the legacy bool during the window; they should migrate to the explicit datetime predicate (`sprk_searchindexcompletedon NEQ null`) when convenient.
3. **Drop step** (post-R3, separate task) — Once the consumer migration is confirmed and one sprint has elapsed with no regressions, a follow-up task will:
   - Remove the legacy `sprk_searchindexed` bool from the schema.
   - Remove the dual-write branch from the mapping layer.
   - Update the data-model docs.
   - Update [`docs/architecture/playbook-architecture.md`](../architecture/playbook-architecture.md) to remove the "Known Pitfall" entry (the migration eliminates the pitfall).

---

## Acceptance traceability

| Acceptance criterion | Source | Status |
|---|---|---|
| AC-H3.2 | spec.md line 303 | ✅ Phase 1 (schema migration) deployed; ⏳ consumer migration tracked under tasks 062/063/064 |
| Dual-write transition window | spec.md line 366 (assumption) | ✅ Documented above; binding for R3 |
| Idempotent schema script | CLAUDE.md task-execute protocol; mirror of R3-015 pattern | ✅ Verified — re-run produces "skipped (already exists)" for both new attributes; no destructive operations |

---

## Cross-references

- **Inventory of all consumers** (writers / readers / schema / docs): [`projects/spaarke-platform-foundations-r3/notes/sprk-searchindexed-consumer-inventory.md`](../../projects/spaarke-platform-foundations-r3/notes/sprk-searchindexed-consumer-inventory.md) — produced by R3-060.
- **Spec**: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) §FR-3H3.2, §FR-3H3.3, §FR-3H3.4, §AC-H3.2.
- **Mapping-layer dual-write target** (task 062): [`src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs`](../../src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs) lines 610-614, 787-791 + [`src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`](../../src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs) lines 122, 531-540, 1094-1098.
- **DTO that exposes the property** (will gain two new properties in task 062): [`src/server/shared/Spaarke.Dataverse/Models.cs`](../../src/server/shared/Spaarke.Dataverse/Models.cs) `DocumentRequest.SearchIndexed` (line 177), `DocumentEntity.SearchIndexed` (line 286).
- **Test fixture** (will gain assertions for new fields in task 062): [`tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseEntitySchemaTests.cs`](../../tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseEntitySchemaTests.cs) line 85.
- **Documented known-pitfall** (will be retired post-migration): [`docs/architecture/playbook-architecture.md`](../architecture/playbook-architecture.md) lines 256, 394.
- **Full sprk_document column catalog** (rows updated to add the two new fields): [`docs/data-model/sprk_financial-related-entities.md`](sprk_financial-related-entities.md).
