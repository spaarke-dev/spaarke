# TIER-C Diagnostic — LLM ↔ Workspace Visibility Root Cause

> **Authored**: 2026-06-25 (post-master-sync, post-UAT)
> **Investigator**: Surface-completion sprint diagnostic
> **Time spent**: ~1 hour
> **Verdict**: Root cause is **Dataverse data gap**, NOT a code defect. Fix is ~30 minutes.

---

## TL;DR

When the user asks the LLM "what's in my workspace tabs?", the LLM correctly replies "I don't have direct real-time visibility" because **2 of 4 workspace handler tool rows are missing from `sprk_analysistool` in spaarke-dev**. The LLM has the WRITE tools (send + update) but is missing the READ tool (get) and the CLOSE tool. Without the GET tool, the LLM has no way to query workspace tab content beyond the limited per-turn snapshot in the system prompt.

This is **NOT** a successor-merge regression. It's an audit-mode Phase 1 incomplete deployment. The audit (2026-06-21) said Phase 1 deployment was done, but the successor's `GetWorkspaceTabContentHandler` was added LATER (task 118b, Phase 5R) and the seed script was never re-run.

---

## Evidence

### Code state (post-master-sync 2026-06-25)

| Component | State |
|---|---|
| `CloseWorkspaceTabHandler.cs` | ✅ Exists, implements `IToolHandler` |
| `GetWorkspaceTabContentHandler.cs` | ✅ Exists, implements `IToolHandler` |
| `SendWorkspaceArtifactHandler.cs` | ✅ Exists |
| `UpdateWorkspaceTabHandler.cs` | ✅ Exists |
| DI registration (assembly scanning) | ✅ All 4 auto-registered via `AddToolHandlersFromAssembly` |
| `SprkChatAgentFactory.ResolveTools` (line 821+) | ✅ Loads tools from `sprk_analysistool` rows by `sprk_handlerclass` |
| Per-turn workspace snapshot in system prompt (line 346-367) | ✅ Wired; calls `IWorkspaceStateService.GetTabsAsync` + `BuildWorkspaceStateBlock` (2000-char limit per `WorkspaceStateBlockMaxCharsRich`) |
| Workspace handler unit tests | ✅ 32/32 pass |
| Session continuity + workspace state tests | ✅ 50/50 pass |

**Conclusion**: Code paths are correctly wired. Build clean. Tests green.

### Dataverse spaarke-dev state (queried 2026-06-25 via MCP)

```sql
SELECT sprk_name, sprk_handlerclass FROM sprk_analysistool
WHERE sprk_handlerclass LIKE '%Workspace%'
```

| Row | sprk_handlerclass | statecode |
|---|---|---|
| SYS-Send Workspace Artifact | `SendWorkspaceArtifactHandler` | Active ✅ |
| SYS-Update Workspace Tab | `UpdateWorkspaceTabHandler` | Active ✅ |
| **(missing)** | **`GetWorkspaceTabContentHandler`** | **❌ NOT DEPLOYED** |
| **(missing)** | **`CloseWorkspaceTabHandler`** | **❌ NOT DEPLOYED** |

### Seed script state

`scripts/Seed-TypedHandlers.ps1`:
- ✅ References `GET-WORKSPACE-TAB-CONTENT` (line ~ in workspace handler block)
- ❌ Does **NOT** reference `CLOSE-WORKSPACE-TAB` (string-search miss; the `CloseWorkspaceTabHandler` seed JSON exists at `infra/dataverse/sprk_analysistool-close-workspace-tab-row.json` but is orphaned from the script)
- Script was last modified 2026-06-25 21:57 (added GET-WORKSPACE-TAB-CONTENT block from successor work)

---

## Why the LLM behaves as observed

Two contributing failure modes, both fixable by deploying the 2 missing rows:

### Failure 1 — LLM has no GET tool

When the user asks "what's in my workspace?", the LLM checks its available tools. With `GetWorkspaceTabContentHandler` undeployed, the LLM sees:
- ✅ `send_workspace_artifact` (can write tab)
- ✅ `update_workspace_tab` (can mutate tab)
- ❌ no `get_workspace_tab_content` (cannot read tab)

It must rely on the per-turn snapshot block — which is rich-encoded but capped at 2000 characters and is metadata-only (no widget data). For non-trivial questions ("summarize the content in tab 3"), the LLM has no way to fetch the actual data and must apologize for missing visibility.

### Failure 2 — LLM can write but cannot close

The `/clear` slash command and "close this tab" natural-language requests cannot route to a tool. The LLM has no `close_workspace_tab` tool, so it can't act on close-intents. (User UAT noted Tier A/B slashes "non-functional" — this is part of why for the close/cleanup ones.)

---

## What's NOT the root cause

I verified these candidates and ruled them out:

| Candidate | Verdict |
|---|---|
| Successor merge broke R6 wiring | ❌ Successor (PR #409 + Phase 7) deleted CapabilityRouter but the new per-playbook tool-filter (`SprkChatAgentFactory.ResolveTools`) correctly uses `sprk_analysistool` rows; assembly-scan DI still registers handlers |
| `IToolHandlerRegistry` not finding handlers | ❌ Handlers compile-time-discovered + assembly-scanned + registered; `GetHandler(row.HandlerClass)` would succeed if row existed |
| `WorkspaceStateService` not injected per-turn | ❌ Line 363 injects `IWorkspaceStateService` per session scope and emits a snapshot block; verified by 50 passing workspace state tests |
| `BuildWorkspaceStateBlock` returns empty | ❌ Limit-aware (`WorkspaceStateBlockMaxCharsRich = 2000`); would only return empty if `tabs.Count == 0` (legitimate — no tabs open) |
| `ChatSession.UploadedFiles` not persisting (UAT screenshot showed `SYS-Recall_Session_File` failures) | **Separate issue** — `RecallSessionFileHandler` IS deployed (verified) but session-file persistence may have a separate gap. Track as TIER-C-B follow-up after main fix. |

---

## Fix

### Step 1 — Add `CLOSE-WORKSPACE-TAB` to seed script (~5 min)

`scripts/Seed-TypedHandlers.ps1`, add to the workspace handler block (near `SEND-WORKSPACE-ARTIFACT` and `UPDATE-WORKSPACE-TAB`):

```powershell
# R6 Pillar 6b / D-C-07 / task 056 — CloseWorkspaceTabHandler: single row exposing
# the close_workspace_tab(tabId) chat tool. Removes a tab from
# IWorkspaceStateService for the current chat session.
"CLOSE-WORKSPACE-TAB"              = "$RepoRoot/infra/dataverse/sprk_analysistool-close-workspace-tab-row.json"
```

### Step 2 — Run seed script against spaarke-dev (~10 min)

```powershell
# Deploys both rows (GET already in script; CLOSE freshly added)
./scripts/Seed-TypedHandlers.ps1 -Environment spaarke-dev
```

Script is idempotent (UPSERTs by `sprk_handlerclass` + `sprk_toolcode`), safe to run.

### Step 3 — Verify (~5 min)

```sql
SELECT sprk_name, sprk_handlerclass FROM sprk_analysistool
WHERE sprk_handlerclass LIKE '%Workspace%'
```

Should now show 4 rows, all Active.

### Step 4 — Re-UAT Tier C (~5 min user time)

Open a chat session, create some workspace tabs (via `/summarize` or manual), ask:
- "What's in my workspace?"
- "Summarize the content in tab [name]"
- "Close the [tab name] tab"

Expected: LLM now invokes `get_workspace_tab_content` and `close_workspace_tab` tools.

---

## TIER-C-B follow-up (separate from this fix)

The user's UAT screenshot showed `SYS-Recall_Session_File` repeatedly failing with "document not present in this session". The handler IS deployed (verified). The likely cause is upstream:

- `ChatSession.UploadedFiles[]` may not be persisting between turns (FR-56 binding)
- Or the upload pipeline (R6 + successor WP1.5) isn't writing to the session uploaded-files array
- Or the session ID is rolling between recall calls (unlikely with stable session)

This is independent of TIER-C primary fix. Run UAT after Step 4 first. If recall still fails, open TIER-C-B diagnostic:
- Check `Sprk.Bff.Api.Tests.Services.Ai.Chat.ChatSessionContinuityTests` against deployed code path
- Check `SessionPersistenceService.UpdateUploadedFilesAsync` is being called on upload
- Check Cosmos `chat-sessions` container for `UploadedFiles[]` content on a recent session

---

## Impact summary

| Item | Before fix | After fix |
|---|---|---|
| `GetWorkspaceTabContentHandler` row | ❌ Missing | ✅ Active |
| `CloseWorkspaceTabHandler` row | ❌ Missing | ✅ Active |
| LLM workspace visibility | ❌ Read-blind | ✅ Can fetch tab content |
| Tier C UAT | ❌ Fails | Should pass (modulo TIER-C-B recall issue) |
| Code changes required | (none) | (none) |
| Deploy required | Seed script + Dataverse | (no BFF redeploy) |
| Time investment | — | ~30 min including verification |

---

*End of diagnostic. Ready to execute the fix.*
