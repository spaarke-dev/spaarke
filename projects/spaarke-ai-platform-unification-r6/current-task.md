# Current Task State — R6 UAT (post-PR-merge hotfix cycle)

> **Last Updated**: 2026-06-19 ~15:55 UTC (by context-handoff before /compact)
> **Recovery**: Read "Quick Recovery" + "Open issue from THIS turn" sections FIRST
> **Branch**: `work/spaarke-ai-platform-unification-r6` — all 3 UAT hotfixes pushed to origin
> **Mode**: UAT iteration on spaarke-dev (user-driven testing + direct deploys via Deploy-BffApi.ps1 / Deploy-SpaarkeAi.ps1)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | Post-PR #395 merge — UAT in progress; 3 BFF hotfixes shipped this session |
| **Status** | Pillar 8 hard slashes (/clear) + soft slashes (/summarize producing summary) confirmed working. NEW REGRESSION this turn: `/summarize` produces summary in CHAT only, NOT in Workspace tab |
| **Last commit** | `a74ee9fdb` (Hotfix #3 — empty Layer 3 fallback) |
| **Last successful deploy** | BFF on spaarke-dev as of ~15:50 UTC. Frontend on spaarke-dev as of ~13:00 UTC (no frontend changes needed since) |
| **Open issue from THIS turn** | `/summarize` slash → summary in chat only, no Workspace tab opens. NL "summarize this document" earlier opened Workspace tab; needs A/B parity test |
| **Next Action** | Investigate why slash `commandIntent: "summarize"` route delivers chat-only output, while NL summarize opens Workspace tab. Compare BFF logs between the two paths. See "Open issue" section below for concrete commands |

---

## Open issue from THIS turn (read second)

**User report (verbatim)**:
> "the /summarize slash command gives summary only in the chat, does not open in Workspace."

**Context**: 30 minutes ago `/summarize` was producing NO output at all (`toolCount=0` silent stall — fixed by hotfixes #2 + #3). After hotfix #3 deployed at ~15:50 UTC, `/summarize` now produces a summary in chat — but does NOT open a Workspace tab. By contrast, the natural-language "summarize this document" path earlier in the same UAT session WAS observed opening a Workspace tab. So the chat-only output is specific to the slash route.

**Hypothesis**: The Layer 0.5 synthetic capability `invoke_playbook_summarize` (task 082) likely routes the LLM toward a generic `invoke_playbook` tool call where the LLM picks an arbitrary `playbookId` argument. The NL path goes through CapabilityRouter Layer 1 keyword classification → SUM-CHAT@v1 playbook → DeliverOutput node with `destination: "workspace"`. The slash path may resolve to either a different playbook OR the same playbook but with a different DeliverOutput destination node firing.

**Container log clue** from the session that worked (`/summarize`, toolCount=14):
```
routing selected capability 'invoke_playbook_summarize' not found in manifest — skipping tool name expansion.
routing produced empty tool filter — returning full capability set (14 tools)
```

So `invoke_playbook_summarize` is NOT in the capability manifest. The Layer 0.5 emits it as a hint but the manifest can't expand it to specific tool names. Result: agent gets the full 14-tool catalog. From there, the LLM autonomously chooses which to call. It may be calling `invoke_playbook` with the wrong playbookId argument, OR calling a different summarize-adjacent tool that doesn't use the workspace DeliverOutput node.

**Concrete investigation commands**:
```bash
# Pull fresh logs
az webapp log download --name spaarke-bff-dev --resource-group rg-spaarke-dev --log-file /tmp/bff-debug.zip
unzip -o /tmp/bff-debug.zip -d /tmp/bff-debug/

# Find playbook execution events for the slash path
grep -aE "playbook_node_executing|playbook_node_completed|DeliverOutput|destination|SUM-CHAT|invoke_playbook" \
  /tmp/bff-debug/LogFiles/2026_06_19_*containerStream.log | tail -50
```

User-facing test to triangulate:
- Ask user to perform BOTH "/summarize" AND "summarize this document" in the SAME session on a fresh PDF
- Capture both `/messages` SSE streams (DevTools → Network → response)
- The diff will show which tool/playbook each invoked and which destination fired

---

## Hotfix cascade this session (commits)

| # | Commit | What | Why |
|---|---|---|---|
| 1 | `be95dfc7d` | OpenAI tool name sanitization in `ToolHandlerToAIFunctionAdapter.Name` (replaces invalid chars with underscores) + SprkChat remount-on-key for `/clear` UI clear | Existing Dataverse tool names ("SYS-Document Search") have spaces → OpenAI rejected with HTTP 400 on every soft-slash turn that exposed multi-tool catalog. /clear remount restored visible UX (BFF DELETE was already firing) |
| 2 | `35462f807` | Apply same sanitization to `allowedToolNames` HashSet in `BuildAllowedToolNames` (Layer 3 SelectedToolNames branch + Layer 1/2 manifest branch) | Hotfix #1 sanitized `AIFunction.Name` ("SYS-Document_Search") but the per-turn filter compared against raw HashSet ("SYS-Document Search"). NO tool ever matched → `toolCount=0` → silent agent stall |
| 3 | `a74ee9fdb` | Empty `CapabilityRouterOptions.GeneralSupersetFallbackTools` array | Pre-existing latent bug surfaced by hotfix #1/#2: the Layer 3 fallback hardcoded CAPABILITY names ("GenerateSummary") not Dataverse tool names ("SYS-Text Summary") — never matched ANY tool. NL follow-up turns hit this path and dropped to `toolCount=0`. Empty array triggers "empty allowedToolNames → return full set" branch |

**PR #401** opened with hotfix #1 contents — now STALE (commits #2 and #3 on branch but not in PR). **Decision needed at next session**: either update PR #401 to include all 3 hotfixes, or open a fresh consolidated UAT hotfix PR.

---

## UAT scope completed in this session

- ✅ `/clear` (hard slash) — conversation visually clears (after hotfix #1 frontend remount)
- ✅ `/summarize` (soft slash) — produces summary in chat ← **but NEW BUG: no Workspace tab. See "Open issue" above**
- ✅ Frontend Pillar 8 wiring verified in iframe context — all 4 modules (CommandRouter, HardSlashExecutor, SoftSlashRouter, ReferenceResolver) loaded with bundle size 5.88 MB
- ✅ BFF Layer 0.5 routing working — `invoke_playbook_summarize` capability classification fires correctly

## UAT scope NOT YET tested (full Tier A-G plan)

### Tier A — Pillar 8 remaining
- `/draft`, `/extract-entities`, `/analyze` (soft slashes)
- `/new-session`, `/export`, `/save-to-matter`, `/pin` (remaining hard slashes — /save-to-matter + /pin flagged "not critical for this release" by user)
- Reference parsing: `#scope`, `@entity`, `#filename`
- HelpAffordance `?` button click
- `/help` (slash) opens CommandHelpPanel (likely working — not explicitly tested)

### Tier B — Pillar 7 memory (Q7 expansion)
- Voice commands: "remember X" / "forget X" / "always X"
- Cross-session memory recall in NEW session
- Pinned Memory CRUD UI (list + add + edit + delete + filter + search)
- Provenance badge display (stubbed to "Created via UI")

### Tier C — Pillar 6 workspace ↔ assistant
- "What's in Tab 1?" — agent reads workspace state from system prompt
- "Update the summary in Tab 1" — agent invokes update_workspace_tab tool
- "Send to Workspace" affordance on chat messages
- "Add to Assistant" toggle on tabs
- "Pin to Matter" button (Cosmos durable persistence)
- Q8 conflict resolution: stale-read refusal

### Tier D — Pillar 6c execution trace widget
- Run any tool-invoking command
- Inspect Context pane ExecutionTraceWidget — should show ordered timeline
- ADR-015 audit: trace events have NO user content / NO document content

### Tier E — Pillar 9 widget visibility contract
- Open multiple tabs (Summary, DocumentViewer, Dashboard, Table variants)
- Toggle `visibleToAssistant: false` on one — verify it disappears from prompt
- Ask "what's in workspace?" — agent only mentions visible tabs

### Tier F — NFR-11 backward compat
- "summarize this document" still routes via Layer 1 keyword path (no `commandIntent`)
- "make it shorter" follow-up stays conversational (no tool call)
- Wizard pre-fill flow unchanged (NFR-07)

### Tier G — Dark mode (ADR-021)
- Toggle dark mode in D365 settings
- Verify all new R6 widgets: CommandHelpPanel, HelpAffordance, PinnedMemoryListWidget, EditDialog, DeleteConfirmation, ProvenanceBadge, ExecutionTraceWidget

---

## Known outstanding bugs (not yet fixed)

| Bug | Severity | Notes |
|---|---|---|
| `/summarize` slash → summary in chat only, no Workspace tab | HIGH (this turn's bug) | Investigation per "Open issue" section above |
| `POST /api/ai/chat/sessions/{id}/documents` 500 — file upload pipeline | High | Was observed on the original broken session `0ad6efc...`. May only affect corrupted sessions. Pending repro on a fresh session |
| `DELETE /api/ai/chat/sessions/{id}` 500 — session cleanup | Medium | Same session-corruption suspect. The `/clear` UX still works because the frontend remount doesn't depend on the DELETE response succeeding |
| Agent verbose "please confirm" rather than auto-proceeding | Low / cosmetic | Persona prompt tuning — could be addressed by editing `Seed-AiPersonaDefault.ps1` and re-seeding. User has not asked for this fix yet |
| Auto-deploy gap (process learning for task 090) | Documentation | The auto-deploy from master only deploys BFF. SpaarkeAi frontend MUST be deployed separately via `Deploy-SpaarkeAi.ps1`. This wasn't called out anywhere visible during PR merge — caused ~90 min of "why is /clear not working" confusion before diagnosed |

---

## Files modified this session (all committed + pushed)

| File | Purpose | Commit |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` | SanitiseToolName helper + Name property override | `be95dfc7d` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Apply SanitiseToolName to allowedToolNames HashSet | `35462f807` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouterOptions.cs` | Empty GeneralSupersetFallbackTools array | `a74ee9fdb` |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | SprkChat key-remount for /clear UI clear (+ Pillar 8 integration from PR #395) | `be95dfc7d` |

---

## Deploys this session

| Time | What | Method |
|---|---|---|
| ~13:00 UTC | First frontend deploy after PR #395 merge | `Deploy-SpaarkeAi.ps1` (after discovering auto-deploy gap) |
| ~14:30 UTC | BFF hotfix #1 (OpenAI sanitization + remount) | `Deploy-BffApi.ps1` |
| ~15:30 UTC | BFF hotfix #2 (allowedToolNames sanitization) | `Deploy-BffApi.ps1` |
| ~15:50 UTC | BFF hotfix #3 (empty Layer 3 fallback) | `Deploy-BffApi.ps1` |
| - | Frontend hasn't been redeployed since first deploy — none of the BFF hotfixes touched frontend code | - |

---

## How to resume (post-compact)

1. **Read THIS file** end-to-end before responding to user — especially "Open issue from THIS turn" + the hotfix cascade table
2. **Ask user**: "Where were we? You reported `/summarize` showed summary in chat only, no Workspace tab. Want me to investigate the slash-vs-NL routing diff? Or continue with the Tier A-G test plan?"
3. **If user wants to keep debugging slash → workspace**:
   - Pull fresh BFF logs (commands in "Open issue" section)
   - Compare a `/summarize` slash turn vs NL "summarize this document" turn
   - Check `routing selected capability` + `playbook_node_executing` events for nodeType and destination
4. **If user wants to power through testing**: hand them the Tier A-G test plan above, support each tier as they go
5. **If user is ready to close UAT**: prep tasks 089 (Phase D exit gate) + 090 (lessons-learned)
   - Pre-staged headline lessons for 090:
     - **Three cascading UAT hotfixes** (OpenAI tool name regex → tool-name-matching mismatch in filter → Layer 3 fallback using capability names instead of tool names) — the cascade pattern of one fix exposing latent bugs in adjacent layers is a critical lesson
     - **Auto-deploy gap**: BFF-only auto-deploy + frontend manual deploy via Deploy-SpaarkeAi.ps1 — should be called out in PR descriptions going forward
     - **Sub-agent dispatch worked well for parallel R6 implementation**, but UAT debug cycle was main-session only — deep cross-layer investigation requires full context that sub-agents wouldn't have had
     - **F.1 asymmetric-registration anti-pattern** (CLAUDE.md §10) fired again in PR #395 CI hotfix #1 — task POML should mandate the static-scan recipe explicitly

---

## Critical Context (1-3 sentences for fast recovery)

R6 PR #395 merged + deployed to spaarke-dev. UAT surfaced 4 cascading bugs (OpenAI tool name regex, two tool-name-matching mismatches, Layer 3 fallback using capability names instead of tool names). 3 BFF hotfixes shipped via direct `Deploy-BffApi.ps1`. User just reported `/summarize` slash now produces summary in chat but not Workspace tab — next investigation is to diff slash vs NL routing paths.

---

*Continuation point set. Ready for /compact.*
