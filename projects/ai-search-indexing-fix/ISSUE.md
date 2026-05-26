# AI Search Indexing — Issue Overview

> **Project**: `ai-search-indexing-fix`
> **Status**: Open — investigation needed
> **Created**: 2026-05-19
> **Severity**: Medium — silently broken; user-facing "success" message is misleading; no AI Search content is being added to the index
> **Related**: [`projects/sdap-secure-project-module-r2/secure-project-index-issue.md`](../sdap-secure-project-module-r2/secure-project-index-issue.md), [`projects/spaarke-auth-v2-and-hardening/`](../spaarke-auth-v2-and-hardening/)

---

## 1. Issue Reported

When a user uses the **SendToIndex** action on a Document record to add a file to AI Search, the UI displays:

> **"Your file was indexed"**

But the document is **not** actually written to the index:

- `spaarke-knowledge-index-v2` doc count remains unchanged
- The Document record's index-related fields are not updated
- Subsequent semantic searches do not return the file

### Reproducer (2026-05-19, 22:27 UTC)

File: `PAT 109270W-1 - CLAIMS repl for response OA 2025-Oct-9(205565003.1).docx`
Action: User clicked SendToIndex on the related Document record at ~22:27 UTC.
Expected: index doc count increases from 739 → 740; file appears in `fileName` filter search.
Observed: index doc count remained 739; searching for `PAT 109270W-1` returned 5 hits, all from 2026-01-28 (most recent createdAt: `2026-01-28T22:44:07Z`). Nothing from May 2026.

### Symptoms summary

| Surface | Behavior |
|---|---|
| PCF UI | Shows "Your file was indexed" — success |
| HTTP response from BFF | Almost certainly 202 Accepted (indexing is async via Service Bus) |
| Azure AI Search index `spaarke-knowledge-index-v2` | Doc count unchanged; new file not present |
| Dataverse Document record | Index-status fields not updated |
| App Insights | (Unverified — needs investigation) |
| Service Bus dead-letter queue | (Unverified — needs investigation) |

---

## 2. What Has Been Done So Far

### 2.1 Field discovery (2026-05-19 afternoon)

Investigation began as a related issue — a **400 Bad Request** error on SendToIndex with the message:

```
The request is invalid. Details: A null value was found for the property named
'privilege_group_ids', which has the expected type 'Collection(Edm.String)[Nullable=False]'.
```

Audit of the live `spaarke-knowledge-index-v2` showed that field `privilege_group_ids` was **missing entirely** from the live index even though:
- `infrastructure/ai-search/spaarke-knowledge-index-v2.json:228` declared the field correctly
- `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs:204` declared the model property
- `Deploy-IndexSchemas.ps1` was misconfigured (IndexMap targets `spaarke-knowledge-index`, not the actually-used `-v2`) — so the schema file had never been deployed to the live index

### 2.2 Field added via Portal UI (2026-05-19 evening, by user)

User added the missing field via Azure Portal:
- Name: `privilege_group_ids`
- Type: `Collection(Edm.String)`
- Retrievable: ✅ (confirmed via live query)
- Filterable: ❌ false (still wrong — Portal UI behavior unclear)
- Implicit `Nullable=False` per Azure Search Collection-type semantics (not user-configurable)

### 2.3 C# defensive default shipped (2026-05-19 evening)

Root cause of the synchronous 400: `KnowledgeDocument.PrivilegeGroupIds` defaulted to `null`; Azure Search rejected nulls for `Collection(Edm.String)`.

Fix shipped: `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs:205`

```csharp
// Default to empty list — Azure Search Collection(Edm.String) is implicitly Nullable=False
// and rejects null writes (400 Bad Request). Empty list correctly signals "public document"
// to the privilege filter (matches "not privilege_group_ids/any()" clause).
public IList<string>? PrivilegeGroupIds { get; set; } = new List<string>();
```

Deployed to `spe-api-dev-67e2xz` via `Deploy-BffApi.ps1`:
- Package: 75.19 MB
- Hash-verify: PASS (all 6 critical files matched)
- Healthz: PASS

### 2.4 User retested → new symptom emerged (2026-05-19 22:27)

After the fix shipped, the user retested with the file noted in §1. The synchronous 400 stopped, but the **doc never landed in the index** despite the UI saying it did.

This shifts the failure from a synchronous, visible error to a **silent async pipeline failure** — much worse from an operations / observability standpoint because the user has no signal that anything is wrong.

### 2.5 Architectural analysis written (2026-05-19)

A separate analysis covered the broader Secure Project context: [`secure-project-index-issue.md`](../sdap-secure-project-module-r2/secure-project-index-issue.md). Key findings relevant to this issue:

- R1 (Secure Projects) spec promised AI Search isolation via `project_ids` filter; implementation never built it. The codebase has *query-side* privilege filter infrastructure (`PrivilegeFilterBuilder`, `IPrivilegeGroupResolver`, OData filter expressions) but **no code path computes or writes `privilege_group_ids` at index time**. The field is always `[]` on every chunk.
- Deploy script targets the wrong index name (will not update the live `-v2` index even if re-run).
- User direction (2026-05-19): for the Secure Project flow specifically, create a *separate* dedicated index rather than retro-fitting the existing `-v2`. The existing 739 internal-knowledge docs are not the right target for matter-level privilege filtering.

The current issue (SendToIndex silent failure) is a **separate** indexing-pipeline problem on the existing `-v2` index, not the same as the privilege-filtering gap.

---

## 3. What Is Verified-Good (Don't Re-Investigate)

- C# defensive default IS deployed and active (hash-verify confirmed on 2026-05-19).
- Live field `privilege_group_ids` IS present on `spaarke-knowledge-index-v2` (Portal-added).
- Synchronous 400 error IS resolved (no longer the immediate failure mode).
- BFF healthz is passing (`/healthz` returns 200).
- BFF JWT validation IS working (Phase C deploy smoke-tested).
- Exchange ApplicationAccessPolicy IS in place for the BFF MI (separate concern, but confirms MI auth is healthy).

---

## 4. Further Investigation Needed

### 4.1 First — confirm the failure mode

The current hypothesis is "PCF gets 202 Accepted, async indexing job fails silently downstream." Confirm this is what's happening:

1. **Trace the actual BFF endpoint hit by SendToIndex.** The PCF Document operation likely calls `POST /api/...` against the BFF. Need to identify the exact route from `src/client/webresources/js/sprk_DocumentOperations.js` (or wherever the action handler lives). What status does the BFF return? Is the response 202 + correlation ID, or is it 200 with a "queued" body?
2. **Identify the Service Bus queue / topic the job is enqueued to.** Most likely candidates: `RagIndexingPipeline`, `FileIndexingService`, `ReferenceIndexingService` (all in `src/server/api/Sprk.Bff.Api/Services/Ai/`). Trace `Program.cs` DI registration → background service registration → message handler.
3. **Inspect Service Bus dead-letter queue** for any indexing-related messages around 2026-05-19 22:27 UTC.

```bash
# Identify the queue/topic name from BFF code, then:
az servicebus queue show \
  --resource-group <rg> \
  --namespace-name <sbns> \
  --name <queue-name> \
  --query "countDetails"

az servicebus queue list-messages \
  --resource-group <rg> \
  --namespace-name <sbns> \
  --queue-name <queue-name> \
  --properties dead-letter
```

### 4.2 App Insights trace for 22:27 timeframe

```kusto
// Replace {appId} with the App Insights resource App ID
// sprkspaarkedev-aif-insights: 84cc1590-29e2-4b05-abb4-f103a7fc31c6

traces
| where timestamp between (datetime(2026-05-19 22:20) .. datetime(2026-05-19 22:45))
| where operation_Name has "Indexing" or message has "Index" or message has "PAT 109270W-1"
| project timestamp, severityLevel, operation_Name, message, customDimensions
| order by timestamp asc

exceptions
| where timestamp between (datetime(2026-05-19 22:20) .. datetime(2026-05-19 22:45))
| project timestamp, type, outerMessage, innermostMessage, operation_Name
| order by timestamp asc

requests
| where timestamp between (datetime(2026-05-19 22:20) .. datetime(2026-05-19 22:45))
| where url has "index" or operation_Name has "Index"
| project timestamp, name, resultCode, duration, success, customDimensions
| order by timestamp asc
```

### 4.3 Code-path audit (BFF indexing pipeline)

Trace the full write path from "BFF receives indexing request" → "document lands in Azure AI Search":

| File | What to verify |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` | The `/api/ai/rag/*` route(s) PCF calls. Is the SendToIndex endpoint here? What does the request handler do — enqueue to Service Bus, or write directly? |
| `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` | Likely orchestrates SPE file → chunks → embeddings → write. Does it have proper exception handling? Does it call `MergeOrUploadDocumentsAsync` correctly? |
| `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` | Chunk + embed + index pipeline. Does it fan out to knowledge-index + discovery-index? Are both calls succeeding? |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/RagIndexingJobHandler.cs` (if exists) | Service Bus message handler. What does it do on exception — DLQ, retry, log-and-swallow? |
| `src/client/webresources/js/sprk_DocumentOperations.js` | Client-side action. The 2026-05-19 fix corrected the field name from `error` → `errorMessage`; what does the BFF response actually look like? |
| `src/server/api/Sprk.Bff.Api/Api/Ai/AdminKnowledgeEndpoints.cs` | `POST /api/admin/knowledge/index-references` — possibly related to manual indexing actions |

### 4.4 Index-level diagnostics

Run a controlled write against `spaarke-knowledge-index-v2` with the minimum-required fields including the now-defaulted `privilege_group_ids: []`. If THAT succeeds, the issue is upstream in the BFF pipeline (not the index itself):

```bash
# Minimal control write (using admin API key)
curl -X POST "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2/docs/index?api-version=2024-07-01" \
  -H "api-key: <admin-key>" \
  -H "Content-Type: application/json" \
  -d '{
    "value": [{
      "@search.action": "mergeOrUpload",
      "id": "diag-test-2026-05-19_0",
      "tenantId": "<test-tenant-guid>",
      "documentId": "diag-test-2026-05-19",
      "content": "diagnostic test document",
      "privilege_group_ids": [],
      "createdAt": "2026-05-19T22:30:00Z"
    }]
  }'
```

If response is 200 + `"status": true` and doc count goes to 740, the index accepts writes; problem is in the BFF or Service Bus pipeline.

### 4.5 Other candidate causes

| Candidate | How to verify |
|---|---|
| Service Bus message bigger than 256 KB (Standard tier limit) | Check the message size after embedding generation; large docs may produce >256 KB messages |
| Embedding generation (OpenAI) is failing and the pipeline isn't surfacing the error | Check Service Bus DLQ messages for OpenAI exception payloads |
| RID-trimmed publish dropped a native dep used by some part of the pipeline | Unlikely — BFF deploy with `--runtime linux-x64` happened earlier today and healthz passes; but check if there's a code path that loads a native lib lazily |
| MI permissions gap on Azure AI Search write (different from read) | Check MI's RBAC role on the AI Search resource; admin-key writes work but MI may not have `Search Service Contributor` |
| The PCF action calls a deprecated endpoint that no longer enqueues | Trace from `sprk_DocumentOperations.js` to the actual BFF route |
| Tenant ID mismatch causes the doc to land in a partition/filter we're not searching | All 5 prior hits for "PAT 109270W-1" have `tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"` — verify the BFF is writing with the same tenantId |
| The defensive C# fix has a side effect (e.g., changes JSON serialization shape) breaking downstream parsing | Diff old vs new serialized payload; both should be valid JSON with `privilege_group_ids: []` |

---

## 5. Suggested Investigation Sequence

To minimize wasted effort, run in this order:

1. **Quick wins** (~30 min):
   - Identify the BFF endpoint hit by SendToIndex (grep `sprk_DocumentOperations.js`)
   - Identify the Service Bus queue/topic used by that endpoint
   - Run a diagnostic admin-key write (§4.4) to confirm the index accepts writes
2. **App Insights query** (~30 min, requires App Insights `traces` table to actually contain data — earlier check showed zero traces in last hour; need to investigate why):
   - Run §4.2 queries for 22:20–22:45 window
   - If nothing returned, broaden window to last 24h
3. **Service Bus DLQ inspection** (~30 min):
   - List + peek messages in any indexing-related dead-letter queue
   - Capture exception payloads
4. **Code-path audit** (~1–2h, based on findings above):
   - Read the relevant files identified in §4.3
   - Look for swallowed exceptions, missing await, unobserved Task, fire-and-forget patterns
5. **Fix + verify** (varies):
   - Apply targeted fix based on root cause
   - Deploy to dev only
   - Retest with another SendToIndex call
   - Verify doc count increments AND new file is searchable

---

## 6. Out of Scope for This Project

To keep this project focused on the indexing-failure root cause, the following are explicitly OUT OF SCOPE here (they belong to other projects):

- ❌ Building the `privilege_group_ids` write-side (group computation logic) — belongs to `sdap-secure-project-module-r2` per [`secure-project-index-issue.md`](../sdap-secure-project-module-r2/secure-project-index-issue.md)
- ❌ Creating the separate `spaarke-secure-content-index` for Secure Project content — belongs to R2
- ❌ Fixing the `filterable: false` config on the user-added `privilege_group_ids` field — dead weight, low-priority cleanup
- ❌ Fixing the `Deploy-IndexSchemas.ps1` IndexMap target — separate cleanup
- ❌ BFF publish-size debt — belongs to `sdap-bff-api-remediation-fix`
- ❌ Any architectural redesign of the indexing pipeline

This project is narrowly: **why does SendToIndex silently fail when the UI says success, and how do we make it succeed (and make the failure mode loud when it doesn't)?**

---

## 7. Success Criteria

This project is complete when:

1. ✅ Root cause of the 2026-05-19 22:27 silent failure is identified and documented
2. ✅ A targeted code or configuration fix is applied
3. ✅ A controlled retest succeeds — doc count increases by 1, file is searchable in the index
4. ✅ The failure mode is made loud — if indexing fails downstream, the user/PCF receives a meaningful error (not "Your file was indexed")
5. ✅ Observability improved — App Insights traces / Service Bus DLQ are queryable post-deploy so future failures don't go silent
6. ✅ A regression test (or runbook step) is added to catch this failure mode going forward

---

## 8. Open Questions

1. What is the actual BFF endpoint hit by SendToIndex? (Determines which code path to audit.)
2. Is the indexing async via Service Bus, or sync inline in the BFF request handler?
3. Is the PCF response handling brittle (e.g., assuming 202 = "indexed" when it actually means "queued")?
4. Why did App Insights `traces` table show zero rows for the BFF in the last hour earlier today? Is telemetry wired correctly?
5. Has the MI's RBAC role on the Azure AI Search resource been verified end-to-end after the Phase C `Graph__ManagedIdentity__Enabled=true` flip?
6. Is there a regression test for SendToIndex in `Sprk.Bff.Api.Tests/`? If so, what does it cover?

---

## 9. References

- BFF C# fix commit (pending): [`src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs`](../../src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs)
- PCF client action: [`src/client/webresources/js/sprk_DocumentOperations.js`](../../src/client/webresources/js/sprk_DocumentOperations.js)
- RAG indexing pipeline: [`src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs)
- File indexing service: [`src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs)
- Schema file: [`infrastructure/ai-search/spaarke-knowledge-index-v2.json`](../../infrastructure/ai-search/spaarke-knowledge-index-v2.json)
- Deploy script (misconfigured IndexMap): [`scripts/ai-search/Deploy-IndexSchemas.ps1`](../../scripts/ai-search/Deploy-IndexSchemas.ps1)
- Auth v2 context recovery: [`projects/spaarke-auth-v2-and-hardening/current-task.md`](../spaarke-auth-v2-and-hardening/current-task.md)
- Related — Secure Project R2 analysis: [`projects/sdap-secure-project-module-r2/secure-project-index-issue.md`](../sdap-secure-project-module-r2/secure-project-index-issue.md)

---

*Issue overview written 2026-05-19 during Auth v2 close-out. Investigation pending owner authorization to start.*
