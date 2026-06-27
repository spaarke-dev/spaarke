# 🚨 RESTART POINT — BFF still 503; DI fix applied locally, NOT yet deployed

> **Created**: 2026-06-08 ~13:10 UTC
> **Critical state**: BFF in SPAARKE DEV 1 is **DOWN (503)**. Fix is committed locally — must be deployed.

---

## TL;DR — what happened

1. Deployed Tier 3 indexer routing fix (commit `60bbc413`) at 12:38.
2. BFF crashed at startup. Real error from Kudu failure log:
   ```
   System.InvalidOperationException: Unable to resolve service for type
   'Sprk.Bff.Api.Services.Ai.ISearchIndexNameResolver' while attempting to activate
   'Sprk.Bff.Api.Services.Jobs.Handlers.RagIndexingJobHandler'
   ```
3. **Root cause**: Wave A agent put `services.AddScoped<ISearchIndexNameResolver, SearchIndexNameResolver>()` INSIDE the `AddNullObjectsForCompoundOff` method in `AnalysisServicesModule.cs` (line ~311). That method ONLY runs on the AI-OFF path. Live env has AI ON → registration never fires → consumers can't resolve.
4. Wrong initial hypothesis was Managed Identity. **MI is fine.** The original failure log's MI error was from a DIFFERENT startup attempt (Wave 11 bits failing on an Azure platform restart at 11:23 — that was self-healing); the actual Tier 3 startup error was the DI registration miss.
5. **Fix applied** in `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs`:
   - Removed the erroneous registration inside `AddNullObjectsForCompoundOff` (replaced with explanatory note)
   - Added unconditional registration at the TOP of `AddAnalysisServicesModule` (after `R5SummarizeTelemetry`)
6. **Build is clean** (`dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors).
7. **Fix NOT YET COMMITTED, NOT YET DEPLOYED.**

## What the next session must do FIRST (in this order)

### Step 1: Check uncommitted state
```bash
git status --short | grep -v husky
```
Expected: `M src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (only file modified).

### Step 2: Commit + push
```bash
git add src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs && git commit -m "$(cat <<'EOF'
fix(multi-container-multi-index-r1): unblock BFF startup — move ISearchIndexNameResolver DI registration to unconditional path

Tier 3 indexer routing fix (commit 60bbc413) registered ISearchIndexNameResolver
inside AnalysisServicesModule.AddNullObjectsForCompoundOff(), which only runs
on the AI-OFF path. In SPAARKE DEV 1 (AI-ON), the registration never fired and
RagIndexingJobHandler + BulkRagIndexingJobHandler + IndexingWorkerHostedService
all crashed at startup with InvalidOperationException: "Unable to resolve service
for type ISearchIndexNameResolver".

Fix: moved AddScoped<ISearchIndexNameResolver, SearchIndexNameResolver>() to the
TOP of AddAnalysisServicesModule (above the documentIntelligence/analysis
conditionals), so it registers on BOTH AI-ON and AI-OFF paths. Old location
replaced with a NOTE comment to prevent regression.

Build: dotnet build src/server/api/Sprk.Bff.Api/ → 0 errors.
Restore service to SPAARKE DEV 1 by redeploying with the bff-deploy skill.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin work/spaarke-multi-container-multi-index-r1
```

### Step 3: Redeploy BFF immediately
```bash
pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1
```
Wait for "All 4 critical files match" + "dev health check passed" (90-120s cold start expected).

### Step 4: Verify
```bash
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz
```
Should return `200`.

### Step 5: Resume operator verification
Continue with the original verification per `notes/handoffs/indexer-fix-deployed-verification.md`:
- Operator hard-refreshes browser
- Uploads file via DocumentUploadWizard to a Matter under "Spaarke" BU
- Checks Application Insights for `Indexing batch: ... SearchIndexName=spaarke-file-index` log line

---

## Coordination with `spaarke-wt-spaarke-ai-platform-unification-r6`

The r6 parallel project also has 7 BFF commits ahead of master. **Only one file overlaps**:
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` ← both projects modify this

When BOTH projects merge to master, there will be a conflict in `AnalysisServicesModule.cs`. Resolution strategy: take BOTH changes (r6's handler/scope registrations + our `ISearchIndexNameResolver` registration at top of method). Neither change overlaps the other's lines — should be a clean merge with careful conflict resolution.

r6's other 24 BFF files (handlers, chat code, ToolExecutionContext, etc.) do NOT conflict with our indexer-routing work.

**Coordination action item for the next session**: when merging either project to master, do an explicit `git diff origin/master..HEAD -- src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` comparison BEFORE merge to identify exact conflict points.

---

## Critical state values

| Item | Value |
|---|---|
| Branch | `work/spaarke-multi-container-multi-index-r1` |
| Last committed on branch | `88ce801b` (operator verification doc) |
| Latest local edits | `AnalysisServicesModule.cs` ONLY — uncommitted DI fix |
| BFF state | **503 — DOWN in SPAARKE DEV 1** |
| Wizard state | ✅ `sprk_documentuploadwizard` already deployed (1075 KB) with `searchIndexName` in POST body |
| MI status | Fine (was a red herring; the original 11:23 MI failure self-healed via platform) |
| r6 worktree | `c:\code_files\spaarke-wt-spaarke-ai-platform-unification-r6` (branch `work/spaarke-ai-platform-unification-r6`) |
| r6 overlapping file | `Infrastructure/DI/AnalysisServicesModule.cs` only |
| Tests | 6140/0/109 (before this DI fix; DI fix is registration-only, no test impact expected) |

---

## Lessons (for the eventual lessons-learned update)

1. **Sub-agents can place registrations in the WRONG method** — Wave A agent saw the file's pattern (Null registrations clustered in `AddNullObjectsForCompoundOff`) and assumed "unconditional" meant "in that method". But that method runs ONLY on the OFF path. Method NAMES + LOCATION are not safe heuristics; the AGENT must trace from the registration's call site backward to the entry point to confirm it actually fires on the LIVE-env code path.
2. **Build + tests passed** because: DI validation isn't run by `dotnet test` (only at app startup). And the consumer paths (RagIndexingJobHandler etc.) WERE being constructed via DI in the test setup, but the tests likely manually provided `ISearchIndexNameResolver` via mocks. The DI resolution path was never validated end-to-end before deployment.
3. **Future Tier 3-style cross-cutting fixes should include**: a deployment smoke test that actually constructs the offending consumers via the live DI container (not mocks) before declaring done.
