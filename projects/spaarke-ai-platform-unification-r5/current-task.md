# Current Task — Spaarke AI Platform Unification R5

> **Purpose**: Active task state tracker. Managed by `task-execute` skill per CLAUDE.md §7.
> **Status**: 🟡 **MID-DEPLOY** — PR #359 (tags=empty-array fix) merged to master; redeploy in progress to Spaarke Dev.
> **Last updated**: 2026-06-05 (~14:30 UTC, post-PR-#359-merge)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **State** | 🟡 Redeploy in flight. Two fixes already merged to master: PR #354 (R5 Phase 2 closeout — tasks 032+033+tid-claim) at `c670f880`, PR #359 (tags-empty-array fix) at `4adcee5c`. Master HEAD = **`4adcee5c`**. |
| **Active deploy** | Background task `bg1jjolo9` running `Deploy-BffApi.ps1`. Last milestone observed: `[3/4] Deploying directly to App Service... Package created: 45.5 MB`. Monitor `boq9ao1rt` armed for further events. |
| **Branch (local WT)** | `fix/r5-tags-empty-array` at `8f0b8a68` (content identical to master `4adcee5c`). Original closeout branch `work/r5-phase2-closeout` is now stale local-only; main work is on master. |
| **Next concrete action** | When deploy completes (hash-verify MATCH + health check passed), **operator retries the SC-18 walkthrough in SpaarkeAi shell on Spaarke Dev**: upload a doc → ask agent to summarize → expect agent to FINALLY see the file (not "I don't see the document"). |

---

## Where we are in the SC-18 SME walkthrough loop

This is the **third diagnostic cycle**. Each surfaced a different layer of the bug:

| Cycle | Symptom | Root cause | Fix |
|---|---|---|---|
| 1 | Upload returned 401 "Tenant identity not found in token claims" | `ChatDocumentEndpoints.cs` only checked `tid` short claim form; Microsoft.Identity.Web renames it to schema URL | Live-patched + included in PR #354 (lines 151, 443) — adds schema URL fallback |
| 2 | Upload returned 200 but agent said *"I don't see the document uploaded yet"* | Upload endpoint wrote only to legacy Redis; never called `IndexSessionFileAsync` or populated `ChatSession.UploadedFiles` | Tasks 032 + 033 — PR #354 wired upload → IndexSessionFileAsync + UpdateSessionCacheAsync + surfaces UploadedFiles in ChatContext |
| 3 (current) | Tasks 032+033 wiring FIRES — but the Azure Search write throws 400 because `tags` is null; defensive catch swallows error; UploadedFiles never populated; agent still says *"I don't see..."* | `BuildKnowledgeDocuments` left `IList<string>? Tags` uninitialized → serialized as `null` → `spaarke-session-files` index requires `Collection(Edm.String)[Nullable=False]` → 400 | **PR #359 (merged):** `Tags = Array.Empty<string>()` in `BuildKnowledgeDocuments`. Build clean. Currently deploying. |

**Cycle 3 evidence** — captured from live `containerStream.log` after the cycle-2 deploy:
```
Azure.RequestFailedException: The request is invalid. Details: A null value was found for the
property named 'tags', which has the expected type 'Collection(Edm.String)[Nullable=False]'.
Status: 400 (Bad Request)
   at ...UploadSessionFileDocumentsAsync ... RagIndexingPipeline.cs:line 604
   at ...IndexSessionFileAsync ... RagIndexingPipeline.cs:line 336
```

---

## Resume protocol (when ready to continue)

### Step 1: Confirm deploy completed
```powershell
# Check background task bg1jjolo9 status
gh run list --limit 1  # ignore, this is GitHub; deploy is local
# Better — read the output file:
cat "C:/Users/$env:USERNAME/AppData/Local/Temp/claude/c--code-files-spaarke-wt-spaarke-ai-platform-unification-r5/a35fa8c7-8200-46b4-a2f5-faaf67fe1f6c/tasks/bg1jjolo9.output" | tail -30
```
Look for: `All 4 critical files match local build (SHA-256 verified)` + `health check passed!` + `Deployment Complete`.

If the deploy is still in progress, just wait for the monitor `boq9ao1rt` to report further milestones (it's persistent until exit).

If the script failed → check the output for the failure mode; re-run with `pwsh .\scripts\Deploy-BffApi.ps1` (script auto-recovers via Kudu zipdeploy on file-lock failures).

### Step 2: Verify Tags fix actually landed in production
```bash
curl -sS -X POST "https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions/{real-session-id}/documents" \
  -H "Authorization: Bearer {real-jwt-from-DevTools}" \
  -F "file=@/path/to/small.pdf"
```
Then watch the BFF log:
```powershell
$mgmtToken = az account get-access-token --resource https://management.azure.com --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $mgmtToken" }
$list = Invoke-RestMethod -Uri "https://spaarke-bff-dev.scm.azurewebsites.net/api/vfs/LogFiles/" -Headers $headers -UseBasicParsing
$latest = $list | Where-Object { $_.name -like "*_containerStream.log" } | Sort-Object mtime -Descending | Select-Object -First 1
$content = (Invoke-WebRequest -Uri "https://spaarke-bff-dev.scm.azurewebsites.net/api/vfs/LogFiles/$($latest.name)" -Headers $headers -UseBasicParsing).Content
$content -split "`n" | Select-Object -Last 100 | Where-Object { $_ -match "(Session-files indexing|R5 session-files|UploadedFiles)" }
```

**Expected (cycle 3 success signal)**:
```
Session-files indexing complete for documentId=... sessionId=...: {N} chunks in {ms}ms
```
**NOT** the cycle-3 failure pattern:
```
R5 session-files indexing OR manifest update failed for DocumentId=...
Azure.RequestFailedException: The request is invalid. Details: A null value was found for...
```

### Step 3: Operator retries the SC-18 UX flow
In SpaarkeAi shell on Spaarke Dev (Power Apps `sprk_SpaarkeAi` Code Page):

1. Hard-refresh the page (Ctrl+Shift+R) to clear any cached frontend state
2. Open a new chat session → say *"summarize a document"*
3. Click `[action:upload]` → pick a small PDF/DOCX → wait for "Document added to context"
4. Type `/summarize` or *"summarize it"* and send

**Cycle 3 expected outcome:**
- Agent invokes the Summarize tool (NOT "I don't see the document")
- Workspace pane streams TL;DR → summary → keywords → entities (structured output)
- Backend log shows `[SUMMARIZE-SESSION] Start tenant=... session=... fileIds={1}`

**If cycle 3 succeeds** → unblocks tasks 034 (frontend auto-trigger) + 035 (SC-18 signoff)
**If cycle 3 still fails** → next layer of bug; capture the exception from live log + diagnose

### Step 4 (conditional): Task 034 — frontend auto-trigger

If the operator wants the *automatic* summarize-on-upload UX (per their UX expectation: "user uploads → summary RUNS AUTOMATICALLY"), execute task 034:
```
projects/spaarke-ai-platform-unification-r5/tasks/034-frontend-auto-trigger-summarize-on-upload.poml
```
Via `task-execute` skill. ~1h estimated. Pattern B (frontend auto-trigger when intent + upload co-occur).

If the operator accepts the current UX (explicit `/summarize` after upload), skip 034.

### Step 5: Task 035 — SC-18 walkthrough re-run + signoff

After Summarize works end-to-end, complete the walkthrough doc at:
```
projects/spaarke-ai-platform-unification-r5/notes/task-030-summarize-walkthrough.md
```
Phases A-E + §7 signoff. Then flip tasks 030, 031, 035 → ✅ in `tasks/TASK-INDEX.md`. Phase 2 closes → Phase 3 unblocked.

---

## Key commits this session

| Commit | Branch | Description |
|---|---|---|
| `01359b36` | master (via PR #345) | R5 main: tasks 001-031, Summarize vertical |
| `d79fbdb2` | master (via PR #349) | CD platform-test fixes (Uri.TryCreate + Path.GetInvalidFileNameChars) |
| `c670f880` | master (via PR #354) | R5 Phase 2 closeout: tasks 032 + 033 + tid-claim fix + integration-test Moq fixup |
| **`4adcee5c`** | **master (via PR #359)** | **tags=Array.Empty<string>() — fixes Azure Search 400 on session-files index** |
| `8f0b8a68` | `fix/r5-tags-empty-array` (local) | Same content as `4adcee5c`; pre-squash version of the fix |

---

## Files modified across closeout work (all on master via PRs above)

**Production code:**
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs` (PR #354) — upload wiring + tid-claim fix
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs` (PR #354) — UpdateSessionCacheAsync usage
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatContext.cs` (PR #354) — additive nullable UploadedFiles field
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` (PR #354) — surface UploadedFiles
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (PR #354) — system-prompt suffix
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/NullSprkChatAgentFactory.cs` (PR #354) — matching override
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IChatContextProvider.cs` (PR #354) — additive parameter
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (PR #354) — pass UploadedFiles at call site
- `src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs` (PR #354) — pass UploadedFiles at call site
- **`src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs`** (PR #359) — `Tags = Array.Empty<string>()` at line ~457

**Tests:**
- `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatDocumentEndpointsTests.cs` (PR #354) — 5 new tests for task 032
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderTests.cs` (PR #354) — task 033 tests
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs` (PR #354) — task 033 tests
- `tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs` (PR #354) — Moq sig fixup
- `tests/integration/Spe.Integration.Tests/Api/Ai/ReAnalysisFlowTests.cs` (PR #354) — Moq sig fixup

**Planning artifacts (notes/tasks):**
- `projects/spaarke-ai-platform-unification-r5/notes/summarize-vertical-remediation-plan.md`
- `projects/spaarke-ai-platform-unification-r5/notes/task-030-summarize-walkthrough.md`
- `projects/spaarke-ai-platform-unification-r5/tasks/032-upload-endpoint-wire-session-files.poml`
- `projects/spaarke-ai-platform-unification-r5/tasks/033-chat-context-surfaces-uploaded-files.poml`
- `projects/spaarke-ai-platform-unification-r5/tasks/034-frontend-auto-trigger-summarize-on-upload.poml`
- `projects/spaarke-ai-platform-unification-r5/tasks/035-sc18-walkthrough-rerun.poml`
- `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` (032/033 marked ✅)

---

## Critical context for resume

1. **The Kudu deploy path is the reliable one.** The first deploy attempt of cycle 2 used `az webapp deploy --type zip` which timed out (Linux Azure App Service was slow to start the site post-stop). The script's hardened recovery path (stop → Kudu zipdeploy → start) DID succeed. Hash-verify confirmed the files were correctly replaced. Don't be alarmed if you see the recovery path trigger again.

2. **404 vs 401 distinction for endpoint health checks.** Spaarke BFF returns **404 for AI endpoints when no bearer token is sent** (security pattern — hide route existence). Always test with `Authorization: Bearer <anything-JWT-shaped-with-dots>` to distinguish "route exists, auth rejected" (401) from "route doesn't exist" (404). My early verification curls without bearer led to a false negative on cycle 2.

3. **The publish folder confusion.** The `bff-deploy` skill says the canonical publish path is `src/server/api/Sprk.Bff.Api/publish/` and forbids paths outside the project tree. The deploy script actually defaults to `deploy/api-publish/`. Despite the inconsistency, the script's hash-verify mechanism catches whether the right bytes land on Azure. We confirmed the deployed DLL is byte-identical to local via Kudu VFS SHA-256 compare.

4. **Why each cycle's tests passed but production broke.** Unit tests mock `SearchClient` so the schema check (server-side) is bypassed. Integration tests don't actually hit Azure Search either. The SC-18 walkthrough on Spaarke Dev is the only place these schema mismatches surface — which is exactly why SC-18 exists per spec. Don't take cycle 3 as a failure of unit testing; take it as proof that SC-18 catches what unit tests can't.

5. **Worktree confusion.** `master` is checked out in another worktree (`spaarke-wt-ai-spaarke-insights-engine-r2`). This local WT can't do `git checkout master`. Either stay on whatever branch + accept that the content is master-equivalent (verified via `git diff HEAD origin/master --stat` = empty), or use `git switch --detach origin/master` to operate on master's content with detached HEAD.

---

## Reference materials

- **PR #354 (closeout)**: https://github.com/spaarke-dev/spaarke/pull/354
- **PR #359 (tags fix)**: https://github.com/spaarke-dev/spaarke/pull/359
- **Remediation plan**: [`notes/summarize-vertical-remediation-plan.md`](notes/summarize-vertical-remediation-plan.md)
- **Walkthrough log**: [`notes/task-030-summarize-walkthrough.md`](notes/task-030-summarize-walkthrough.md)
- **bff-deploy skill**: `.claude/skills/bff-deploy/SKILL.md` (canonical procedure + silent-deploy-failure history)
- **task-execute skill**: `.claude/skills/task-execute/SKILL.md` (for tasks 034 / 035)

---

*Checkpoint authored 2026-06-05 mid-deploy. Resume by tailing the deploy output file (path in §Step 1 above) — script will report `Deployment Complete` when done. Then operator retries SC-18 flow.*
