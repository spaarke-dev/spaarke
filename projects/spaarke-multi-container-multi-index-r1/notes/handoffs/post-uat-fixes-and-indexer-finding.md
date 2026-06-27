# Post-UAT Fixes + AI Search Indexer Finding

> **Date**: 2026-06-07
> **Trigger**: Initial UAT in SPAARKE DEV 1 surfaced 2 bugs + 1 out-of-scope finding.
> **Status**: Bugs 1+2 fixed and redeployed. Finding 3 surfaced for separate follow-up.

---

## Initial UAT Findings (operator-reported)

| Entity | `sprk_containerid` | `sprk_searchindexname` | Issue |
|---|---|---|---|
| Matter (new via wizard) | ✅ populated | ❌ NULL | **Bug 1**: Matter cascade silently failed |
| Project (new via wizard) | ✅ populated | ✅ populated | OK |
| WorkAssignment (new via wizard) | ✅ populated | ✅ populated | OK |
| Document (new via wizard) | N/A (canonical field is `sprk_graphdriveid`) | ✅ populated | OK |
| Document (added to existing parent via DocumentUploadWizard) | N/A (canonical field is `sprk_graphdriveid`) | ❌ NULL | **Bug 2**: Caller-wiring gap |

**Important clarification on Documents and `sprk_containerid`**: Per design INV, the canonical Document container field is `sprk_graphdriveid` — `sprk_containerid` MUST stay NULL on `sprk_document` records. Initial UAT report "did not populate container id" on Documents reflects design-correct behavior, not a bug. MCP-verified: all recent Documents have `sprk_graphdriveid` populated correctly.

---

## Bug 1: Matter wizard cascade silently failing

### Root cause hypothesis

The Matter wizard's `matterService.createMatter` cascade ran inside a `try/catch` that suppressed any error. Two structural differences from the working WorkAssignment pattern:

1. Matter wrapped `IDataService` through a `_toWebApiLike(this._dataService)` adapter, while WorkAssignment passes `this._dataService` directly.
2. Matter used `applyDefaultSearchIndexName` (single field) while WorkAssignment uses `applyUserBuDefaults` (both fields with per-field INV-5).

Either the adapter wrapper introduced a runtime issue not visible at compile time, or the single-field cascade had a code path that swallowed the cascade input. Without browser console access I couldn't diagnose precisely.

### Fix

Aligned Matter's cascade with WorkAssignment's proven pattern verbatim:
- Pass `this._dataService` directly to `EntityCreationService.resolveUserBuDefaults` (IDataService is a structural superset of IWebApiLike — no adapter needed).
- Use `EntityCreationService.applyUserBuDefaults` (per-field INV-5 protects the host-injected `_containerId` already on the entity).
- Logged the cascade result so a future regression would surface in browser console.

**Files changed**:
- `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts` (cascade block, ~20 lines)

**Deploy**: shared lib + `sprk_creatematterwizard` rebuilt and published 2026-06-07.

---

## Bug 2: DocumentUploadWizard (existing-parent path) — `sprk_searchindexname` NULL

### Root cause

Wave 5 task 026 added `resolveSearchIndexNameForRecord(xrm, entityLogicalName, recordId)` — the 3-step chain resolver.
Wave 6 task 027 extended `DocumentRecordService.buildRecordPayload` (and `createDocuments`, `createSingleDocument`) to accept an optional pre-resolved `searchIndexName` parameter.

**No agent wired the caller.** The `DocumentUploadWizard` calls `documentRecordService.createDocuments(uploads, parentContext, formData)` from `uploadOrchestrator.ts` line 267 — without passing `searchIndexName`. The agent for task 027 explicitly noted this in its report: "Caller (DocumentUploadWizard, addressed by downstream wiring task) will invoke `resolveSearchIndexNameForRecord` from task 026 and pass the result to `createDocuments`." That downstream task was never filed.

This is the same kind of caller-wiring gap caught earlier in `CreateProjectWizard/src/main.tsx` (where the `resolveUserBuDefaults` prop was added to the component but not wired by the host until I added it post-Wave 5). Apparently a recurring class of gap when tasks add optional parameters with the assumption that "main session will wire the caller" — when there's no explicit wiring task, it slips.

### Fix

In `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts`:
1. Import `resolveSearchIndexNameForRecord` from `../components/AssociateToStep`.
2. Before calling `documentRecordService.createDocuments`, look up `Xrm.WebApi` via the window-walking pattern (matches the rest of the wizard), call the resolver with `parentContext.parentEntityName` + `parentContext.parentRecordId`, and pass the result as the 4th argument.
3. Wrapped in try/catch — non-fatal; resolver failure falls through to BFF tenant-default chain.

**Files changed**:
- `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts` (+~35 lines, 1 import)

**Deploy**: `sprk_documentuploadwizard` rebuilt and published 2026-06-07.

---

## Finding 3: Files in SPE but not in AI Search index

### Symptom

Files uploaded via the DocumentUploadWizard land in SharePoint Embedded (verified: `sprk_graphdriveid` populated on the Dataverse Document record). However, those files do NOT appear in the Azure AI Search index they should belong to (e.g., `spaarke-knowledge-index-v2` or `spaarke-file-index`).

### Out of scope for this project

This project (`spaarke-multi-container-multi-index-r1`) is scoped to:
- **Routing**: ensure search REQUESTS go to the correct AI Search index based on the record's `sprk_searchindexname` field.
- **Persistence**: populate `sprk_searchindexname` at create time (wizards) and at backfill (Phase F PowerShell).

It is **NOT** scoped to:
- Configure Azure AI Search indexer schedules.
- Configure indexer datasource connections (SPE → index).
- Manage the indexer pipeline that pulls SPE files into the search indexes.

The symptom suggests one of:
1. The AI Search indexer for `spaarke-knowledge-index-v2` (or whichever index the Documents belong to) is not configured to pull from the specific SPE containers in use today.
2. The indexer is configured but its schedule hasn't run yet (most operator-side indexers run on a schedule, not real-time).
3. The indexer datasource is configured for a different SPE container than the one the Documents are landing in.

### Recommended next steps

This requires Azure portal access + Spaarke ops involvement, separate from this project:

1. Operator inspects the AI Search indexer in Azure portal:
   - Confirm indexer exists for each index in `appsettings.AiSearch.AllowedIndexes`
   - Confirm datasource targets the production SPE containers (`b!yLRdWE...` and `b!vzGDfD...`)
   - Trigger an on-demand indexer run; check status
2. If indexer is missing or misconfigured, file a separate project to configure them.
3. Consider whether this project's BFF `IKnowledgeDeploymentService` should call `searchClient.RunIndexer()` after document creation (real-time index trigger). This would change the architecture from "scheduled bulk indexer" to "per-record indexer trigger" — a substantive design decision, not a bug fix.

For this project's wrap-up, this finding is recorded as a **known limitation**: the new routing infrastructure works (verified: PCF sends `searchIndexName` to BFF; BFF resolves and queries the named index), but the index won't return useful results until operator confirms the indexer pipeline is healthy.

---

## Verification protocol for the redeployed fixes

After hard browser refresh in SPAARKE DEV 1, please retest:

1. **Matter wizard**: Create a new Matter. MCP query: `SELECT sprk_containerid, sprk_searchindexname FROM sprk_matter WHERE sprk_matterid = '{new}'`. Both should be populated (containerId from BU/host; searchIndexName from BU = `spaarke-file-index` if owning BU is "Spaarke").
2. **DocumentUploadWizard (existing-parent)**: Open the wizard from a Matter form, upload a file. MCP query the new Document: `sprk_graphdriveid` populated (existing behavior) AND `sprk_searchindexname` now populated (this fix).
