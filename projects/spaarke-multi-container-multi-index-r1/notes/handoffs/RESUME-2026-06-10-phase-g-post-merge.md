# 🚦 RESUME POINT — Phase G post-merge (2026-06-10)

> **Created**: 2026-06-10 (post-PR-#369-merge, pre-compaction)
> **Worktree**: `C:/code_files/spaarke-wt-spaarke-multi-container-multi-index-r1`
> **Branch on disk**: `master` @ `254302259` (sync with origin: 0/0)
> **PR**: [#369](https://github.com/spaarke-dev/spaarke/pull/369) — **MERGED** via squash to master as `254302259`
> **Live deploys**: BFF + PCF v1.1.76 + `sprk_semanticsearch` code page all live in `spaarkedev1`

---

## TL;DR

Phase G code work is **done and merged**. The worktree is on master. What remains:

1. **Manual UAT** (4 H-phase tasks + 2 G-phase verifications)
2. **2 bugs found during UAT** that need follow-up (not on the original task list)
3. **24–48hr soak** then task 110 cleanup
4. **Task 111 wrap-up** (lessons + runbook + pattern doc)
5. **TASK-INDEX status flips** for 100/103/105/109 (work done; just not marked ✅)

---

## Current worktree state

| Item | Value |
|---|---|
| Worktree path | `C:/code_files/spaarke-wt-spaarke-multi-container-multi-index-r1` |
| Current branch | `master` |
| HEAD | `254302259` (PR #369 squash-merge) |
| origin/master | `254302259` (in sync 0/0) |
| Work branch `work/spaarke-multi-container-multi-index-r1` | Still exists locally at `c8a8cc0a1` (+1 CI-fix commit ahead of master; pushed to origin) |
| Uncommitted | `.husky/_/*` only — husky-managed git hooks, **ignore** (not feature work) |

**The `c8a8cc0a1` commit on the work branch** is a CI-only fix added post-merge ("build Spaarke.SdapClient in CI before UI.Components tsc"). Not in master. Likely the merge coordinator will handle separately.

---

## ✅ What's been done

### Deploys live in dev (`spaarkedev1.crm.dynamics.com` + `spaarke-bff-dev.azurewebsites.net`)

| Component | Version | Includes |
|---|---|---|
| BFF API | latest (post-merge) | FetchXml link-entity resolver + `DataverseAllowedIndexesProvider` (cached) + ChunkIndex-nullable hotfix |
| PCF `sprk_Sprk.SemanticSearchControl` | **v1.1.76** | Lookup resolution via `context.webAPI` with correct `sprk_AI_Search_Index` PascalCase nav property |
| Code page `sprk_semanticsearch` | latest | Side-pane redesign: Search Index dropdown + relocated Threshold/Mode + InfoPopover + Document-target host-context-preserve hotfix |
| Data | — | 8 `sprk_aisearchindex` catalog rows + 196 records migrated text → lookup |

### Schema/data state (Dataverse `spaarkedev1`)

- `sprk_aisearchindex` table has 7+3 columns including new `sprk_targetentitytype` Choice
- 8 active rows: Development Files 1/2 (Document target), Matters/Projects/Invoices/Events/Work Assignments/All Records (record target)
- `sprk_ai_search_index` lookup column exists on 7 entities: businessunit, sprk_matter, sprk_project, sprk_invoice, sprk_event, sprk_workassignment, sprk_document
- 196 records have lookup populated (BU: 2, Matter: 15, Project: 3, WorkAssignment: 2, Document: 174). Invoice + Event had 0 text-set records → 0 migrations needed.

### App Service config (`spaarke-bff-dev`)

`AiSearch__AllowedIndexes__0=spaarke-file-index` + `AiSearch__AllowedIndexes__1=spaarke-knowledge-index-v2` — still in place as text-fallback for the BFF allow-list (will remove in task 110 post-soak).

---

## 🔲 Outstanding work (in priority order)

### A. TASK-INDEX status flips (5 min, low-risk)

Update [`projects/spaarke-multi-container-multi-index-r1/tasks/TASK-INDEX.md`](../../../tasks/TASK-INDEX.md):

| Task | Current status | Should be | Note |
|---|---|---|---|
| 100 | 🔄 | ✅ | All 7 lookup columns added |
| 103 | 🔄 | ✅ | BFF deployed; tests green; App Insights smoke can verify post-UAT |
| 105 | 🔲 | ✅ | User confirmed field mapping created earlier (BU→Matter etc.) |
| 109 | 🔄 | ✅ | Code page deployed; e2e UAT can verify alongside H-phase smoke |

### B. UAT smoke tests (H phase — manual)

| Task | Verification | Required |
|---|---|---|
| 071 | Wizards smoke — MCP-verify both fields populated on Matter/Project/Invoice/Event/WorkAssignment create | Quick MCP query post-create |
| 072 | PCF smoke on Protected Matter (BFF log verification) | Open a Matter, run search, check App Insights logs |
| 073 | Filter-parity walkthrough (PCF vs code page side-by-side) | Compare PCF panel ↔ code page modal results |
| 074 | BU-change coexistence proof (INV-3) | Confirm BU change doesn't break existing record lookups |

### C. 2 bugs found during UAT (need follow-up work)

#### Bug C1: "Unknown Matter" on returned docs
**Symptom**: Search Index="Development Files 1" Matter (with sprk_ai_search_index → spaarke-knowledge-index-v2) returns 9 docs all displaying "Unknown Matter" in the Parent Entity column.

**Root cause investigation needed**: BFF's `SearchAssociatedOnlyAsync.MapDocumentEntityToSearchResult` reads `doc.MatterName` for `parentName`. If this field is null/empty on the underlying `sprk_document` records, the code page falls back to "Unknown Matter" display. Two possibilities:
1. Source `sprk_document` records don't have `_sprk_matter_value` populated as expected
2. The BFF mapping reads the wrong field name (case sensitivity? typo?)

**File to investigate**: `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs` (`MapDocumentEntityToSearchResult` method)

#### Bug C2: Matter/Project/Invoice/Event/WorkAssignment dropdown rows don't work
**Symptom**: Selecting a non-Document target (e.g., "Matters" or "Projects") in the dropdown sends `scope='entity'` to the BFF without an `entityId`. BFF rejects with 400 `EntityIdRequired`.

**Root cause**: The hotfix in App.tsx (Document-target preserve-host-context) only covers `fragment.entityType === 'document'`. Records-target rows still fall through to the original (broken) fragment path.

**Fix needed**: Map records-target dropdown selections to:
- `scope='all'` (tenant-wide)
- `filters.entityTypes=[<normalized-label>]` (e.g., `['matter']`)
- `searchIndexName=<row's index name>`

**Files to change**:
- `src/client/code-pages/SemanticSearch/src/services/targetEntityNormalize.ts` — change `buildSearchRequestFragment` shape for records targets
- `src/client/code-pages/SemanticSearch/src/hooks/useSemanticSearch.ts` — update request body construction
- Possibly `useRecordSearch.ts` too

### D. Soak (24–48hr) + post-soak cleanup

**Task 110** (FULL rigor — destructive operations):
1. Monitor App Insights for `PhaseG.TextFallback` warnings → expected zero post-migration
2. After clean soak:
   - Drop text-to-text field mappings (inventory in `notes/phase-g/field-mappings-added.md` — not yet created)
   - Drop `sprk_searchindexname` text column from 7 entities
   - Remove BFF text-fallback code (`SearchIndexNameResolver` fallback branches)
   - Remove `AiSearch__AllowedIndexes__*` App Service config
3. Redeploy BFF without fallback path

### E. Phase G wrap-up (task 111)

- Write `projects/spaarke-multi-container-multi-index-r1/notes/phase-g/lessons-learned.md`
- Update `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` (already exists from task 060) with sprk_aisearchindex management section
- Create `.claude/patterns/ai/semantic-vs-relational-views.md` (the "files = AI Search; documents = Dataverse views; no mixing" pattern)
- Find Similar UAT (verify intra-index works, cross-index returns empty gracefully)

---

## 🚫 Intentionally deferred

| Task | Reason |
|---|---|
| 023 — CreateInvoiceWizard | Intentionally deferred per project decision |

---

## 📍 How to resume

### Option 1: Start a new branch off master in this worktree
```bash
cd c:/code_files/spaarke-wt-spaarke-multi-container-multi-index-r1
git checkout master   # Already on master
git pull origin master
git checkout -b work/spaarke-multi-container-multi-index-r1-phase-g-followups
```
Then tackle items in priority order (A → B → C1/C2 → D → E).

### Option 2: Resume on the old work branch
The work branch `work/spaarke-multi-container-multi-index-r1` is still there at `c8a8cc0a1`. If the merge coordinator merges that CI-fix commit separately, you can:
```bash
git checkout work/spaarke-multi-container-multi-index-r1
git pull origin work/spaarke-multi-container-multi-index-r1
```

### Option 3: Just close the project
If you decide UAT + soak + cleanup are out of scope for this session, the worktree is at a clean master state. Phase G is functionally shipped via PR #369.

---

## Key files for resume

| Purpose | Path |
|---|---|
| This resume point | `projects/spaarke-multi-container-multi-index-r1/notes/handoffs/RESUME-2026-06-10-phase-g-post-merge.md` |
| Phase G spec | `projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md` |
| TASK-INDEX | `projects/spaarke-multi-container-multi-index-r1/tasks/TASK-INDEX.md` |
| Latest hotfix #1 commit | `2b74eaf04` (squashed into 254302259) |
| BFF resolver | `src/server/api/Sprk.Bff.Api/Services/Ai/SearchIndexNameResolver.cs` |
| BFF allow-list provider | `src/server/api/Sprk.Bff.Api/Services/Ai/DataverseAllowedIndexesProvider.cs` |
| Code page dropdown | `src/client/code-pages/SemanticSearch/src/services/aiSearchIndexService.ts` |
| Code page normalize util | `src/client/code-pages/SemanticSearch/src/services/targetEntityNormalize.ts` |
| PCF v1.1.76 resolver | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/SearchIndexResolver.ts` |
| Catalog rows (8 active) | Dataverse `sprk_aisearchindex` table |
| Migration script | `scripts/phase-g-lookup-migration/Migrate-SearchIndexLookup.ps1` |

---

## Test results (post-merge baseline)

- BFF: **6255 pass / 0 fail / 109 skipped** (`dotnet test tests/unit/Sprk.Bff.Api.Tests/`)
- Prettier: clean across `src/client/**/*.{ts,tsx}`
- CI on PR #369 (commit `9cde6a145` pre-squash): all green except Code Quality which was still running at merge time

---

## Quick diagnostic commands

```bash
# Verify BFF healthy
curl https://spaarke-bff-dev.azurewebsites.net/healthz

# Check App Insights for PhaseG.TextFallback warnings
az monitor app-insights query --app 6a76b012-46d9-412f-b4ab-4905658a9559 \
  --analytics-query "traces | where timestamp > ago(24h) | where message has 'PhaseG.TextFallback' | count"

# Verify lookup migration counts (MCP via Claude)
SELECT (SELECT COUNT(*) FROM sprk_matter WHERE sprk_searchindexname IS NOT NULL) AS text_set,
       (SELECT COUNT(*) FROM sprk_matter WHERE sprk_ai_search_index IS NOT NULL) AS lookup_set;
# Both should be 15 (or equal); both should be 0 means nothing was migrated

# Re-run migration if needed (idempotent)
pwsh -File scripts/phase-g-lookup-migration/Migrate-SearchIndexLookup.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Apply
```

---

*If returning here cold: read this file first, then `tasks/TASK-INDEX.md`, then the appropriate section above. Spec lives in `notes/phase-g/spec.md` for design context.*
