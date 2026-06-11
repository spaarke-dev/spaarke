# RESTART POINT — Indexer Routing Fix (Tier 3)

> **Created**: 2026-06-08
> **Purpose**: Context-handoff for compaction or new session. Project IS shipped + post-UAT fixed; this restart point covers the in-flight Tier 3 indexer routing fix.

---

## Where we are

**Project `spaarke-multi-container-multi-index-r1`** is operationally complete and deployed (latest commit before this work: `37a23a0a` — design doc for the indexer fix).

**In-flight**: a Tier 3 BFF + wizard fix that threads `searchIndexName` through the RAG indexing pipeline so per-record routing actually reaches Azure AI Search (was the root cause of "files in SPE but not in the routed index" UAT finding).

---

## Active agent (background — wait for completion notification)

**Agent ID**: `ac9a25d5f71ebb06d` (internal — main session has it)
**What it's doing**: implementing Wave A of the Tier 3 fix — ~13 BFF files.
**Authoritative spec the agent is following**: `projects/spaarke-multi-container-multi-index-r1/notes/indexer-routing-fix-design.md`
**User-confirmed decisions baked into the brief**:
- **Tier 3** — covers ALL 3 routes (PCF Wizard OBO + Email-to-Document Service Bus + Office Add-in) + direct `/api/ai/rag/index-documents` endpoint + verbose write logging.
- **Default-fallback on background-job allow-list rejection**: job handler catches `SdapProblemException(INDEX_NOT_ALLOWED)`, logs WARN, retries with `searchIndexName: null` (tenant default). OBO wizard path still hard-fails.

**Expected output**: ~13 files modified + 1 new file (`SearchIndexNameResolver`) + ~4 test files updated. Agent will return a structured report.

---

## What the main session must do after the agent completes

### Step 1: Verify agent's work

- Check `git status --short` — should show ~13-15 BFF .cs files modified + 1 new + some tests.
- Run `dotnet build src/server/api/Sprk.Bff.Api/` — 0 errors required.
- Run `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — baseline 6124/0/109; confirm no NEW failures (NFR-02).
- Inspect the agent's report for the verbose write log line + default-fallback policy implementation.

### Step 2: Wave B — wizard side + remaining tests

After Wave A lands, dispatch one more agent (small) to do:
1. **`src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts`** — add `searchIndexName: resolvedSearchIndexName` (already resolved in earlier post-UAT fix) to the POST body in `triggerRagIndexing` (line ~512). One-line addition.
2. **`src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs`** `IndexFile` handler — verify it correctly passes `request.SearchIndexName` to `fileIndexingService.IndexFileAsync` (the request DTO now carries it from Wave A — likely no code change needed, just verify and add a test).
3. Any test gaps not covered in Wave A.

Wave B brief template:
```
Execute Wave B of the Tier 3 indexer routing fix. Wave A landed (commit {hash}).
1. Wizard: add `searchIndexName: resolvedSearchIndexName || undefined` to the POST body
   of triggerRagIndexing in uploadOrchestrator.ts (right after `parentEntity` field).
2. Verify RagEndpoints.IndexFile passes request.SearchIndexName correctly (no change
   if the DTO is passed through; add test if not already covered).
3. Build + run tests.
DO NOT commit or deploy.
```

### Step 3: Build verification + commit Wave A+B

`git add` the modified BFF + wizard files. Commit message template:
```
fix(multi-container-multi-index-r1): Tier 3 indexer routing — thread searchIndexName through RAG pipeline

Fixes the bug where files land in SPE but chunks aren't routed to the
sprk_searchindexname-named index. Threads searchIndexName end-to-end
through all 3 document-creation routes (PCF Wizard OBO + Email-to-Document
Service Bus + Office Add-in) so the canonical write boundary
RagService.IndexDocumentsBatchAsync uses the 3-arg
GetSearchClientAsync(tenantId, indexName, ct) overload from Phase B.

Per user-confirmed decisions:
- Tier 3: includes direct /api/ai/rag/index-documents endpoint + verbose
  write logging (audit which index each batch landed in).
- Default-fallback for background jobs: SdapProblemException(INDEX_NOT_ALLOWED)
  from a stale Dataverse sprk_searchindexname value → WARN log + retry
  with tenant default. OBO path still hard-fails.

NEW: SearchIndexNameResolver service (server-side 3-step chain mirroring
the wizard's resolveSearchIndexNameForRecord).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```

`git push origin work/spaarke-multi-container-multi-index-r1`.

### Step 4: Deploy

1. **BFF**: `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` (autonomous; hash-verify + healthz; ~1 min). NFR-01 ceiling 60 MB; baseline ~45.5 MB; expected delta minimal.
2. **DocumentUploadWizard**: clean-rebuild shared lib `dist/` (`npm run build` in `src/client/shared/Spaarke.UI.Components/`) THEN rebuild + deploy via `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-WizardCodePages.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"` (only the DocumentUploadWizard will rebuild + deploy since only its dist/ is present).

### Step 5: Verification

After deploys, ask operator to:
1. Hard-refresh browser in SPAARKE DEV 1.
2. Upload a file via DocumentUploadWizard to a Matter under "Spaarke" BU (`sprk_searchindexname = spaarke-file-index`).
3. After ~30 seconds (allow embedding + indexing time), query Application Insights for the BFF App Service:
   ```
   traces
   | where timestamp > ago(10m)
   | where message contains "Indexing batch:"
   | project timestamp, message
   ```
   Expected log line: `Indexing batch: TenantId={...} SearchIndexName=spaarke-file-index ResolvedEndpoint=https://...search.windows.net BatchSize=N` — this is the audit trail the verbose-logging Tier 3 added.
4. Run an Azure Portal search against `spaarke-file-index` directly (Search Service → indexes → spaarke-file-index → Search explorer) — should find the chunks for that file.

If the verbose log shows `SearchIndexName=(tenant-default)` instead of `spaarke-file-index`, something in the call chain dropped the value — debug from there.

### Step 6: Update wrap-up

After verification, update:
- `README.md` graduation criterion #4 (PCF on Protected Matter) → ✅
- `README.md` Changelog entry for the Tier 3 indexer fix
- `notes/lessons-learned.md` — append a section on the indexer-routing fix (what surprised us; why it was missed in original wave decomposition; the shared `SearchIndexNameResolver` pattern as a future-proof point)
- Final commit + push.

---

## Critical state needed to resume

| Item | Value / Location |
|---|---|
| Current branch | `work/spaarke-multi-container-multi-index-r1` |
| Latest commit | `37a23a0a` (design doc) at time of restart-point creation; agent may have its own pending changes uncommitted in working tree |
| Project folder | `projects/spaarke-multi-container-multi-index-r1/` |
| Design doc | `projects/spaarke-multi-container-multi-index-r1/notes/indexer-routing-fix-design.md` (Section 3 has the file-level change list; Section 6 has scope tiers) |
| Earlier wrap-up | `notes/lessons-learned.md` (don't overwrite — append the indexer-fix section after Wave B+deploy) |
| Earlier post-UAT fixes (commits 2fe1cdf9, 9e8a3403) | Already deployed; wizard's `uploadOrchestrator` already calls `resolveSearchIndexNameForRecord` and has the resolved value in scope — just needs to add the field to the POST body |
| Active agent | `ac9a25d5f71ebb06d` (Wave A BFF core); wait for its completion notification |
| Dataverse env | SPAARKE DEV 1 (`https://spaarkedev1.crm.dynamics.com`) |
| BFF env | `spaarke-bff-dev` Azure App Service (`https://spaarke-bff-dev.azurewebsites.net`) |
| PR | #369 (draft) |

---

## Known gotchas to avoid

- **TS2786 fix already applied** at `src/client/pcf/SemanticSearchControl/tsconfig.json` (`paths` mapping for `react`/`react-dom`). Don't undo.
- **CreateInvoiceWizard doesn't exist** — task 023 is 🚫 deferred. Phase F backfill covers historical Invoice records (but there's no Invoice wizard to fix).
- **Backfill checkpoint files** are gitignored — `backfill-parent-records-progress.json` and `backfill-documents-progress.json` MUST NOT be committed.
- **PCF manifest `description-key` cannot contain apostrophes** — Dataverse XSD `noAposStringType` rejects. Already fixed for the `searchIndexName` property; don't add new apostrophes.
- **Shared lib `dist/` must be clean-rebuilt** before code-page or PCF builds (NFR-10/11). `npm run build` in `src/client/shared/Spaarke.UI.Components/` and `src/client/shared/Spaarke.Auth/` if either was touched.
- **Wizard deploy script uses `-DataverseUrl`** but Documents backfill script uses `-Environment` (inconsistency documented; don't auto-correct without testing).
- **`@types/react@19` in shared lib** triggers PCF TS2786; the `paths` mapping is the fix. If you regenerate shared lib's `package-lock.json`, verify PCF still builds.
- **Backfill drift-audit script has a schema bug** (uses `sprk_name` for entity name attribute, which doesn't exist on `sprk_matter`). Documented as follow-up; not in scope for this indexer fix.

---

## How to resume after compaction

1. Open this file: `projects/spaarke-multi-container-multi-index-r1/notes/handoffs/RESTART-POINT-indexer-fix.md`
2. Read the "Where we are" + "Active agent" + "What the main session must do" sections.
3. If the agent has already returned, jump to Step 1 (verify) → Step 2 (Wave B) → Step 3-5.
4. If the agent is still running, wait for the notification; meanwhile read the design doc.
5. Use this restart point + the design doc + the agent's report as your full context — you don't need to re-read the project artifacts or any other waves.

---

*Created at session ~13% remaining context. Wave A agent dispatched immediately before this restart point.*
