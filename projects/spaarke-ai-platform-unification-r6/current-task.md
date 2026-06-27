# Current Task State — R6 Surface Completion (post-UAT 2026-06-25)

> **Last Updated**: 2026-06-25 (after UAT walkthrough)
> **Recovery**: Read "Quick Recovery" first; then "What just happened"; then "Next Action".
> **Branch**: `work/spaarke-ai-platform-unification-r6` (R6 closeout work continues here OR a new `work/r6-surface-completion-r1` branch — decision pending)
> **Mode**: Surface completion planning — backend is shipped + deployed; UAT exposed gaps.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | Closeout — Surface Completion. Backend 100% shipped + deployed; closeout 089/090 deferred. |
| **Status** | R6 was merged to master 2026-06-24 18:43 UTC (PR #401 / commit `8579d6536`); Environment Promotion succeeded 19:06 UTC. UAT 2026-06-25 surfaced real-world gaps. |
| **Last commit** | `0c9375310` — Merge of master into R6 work branch (CI hotfixes from #407/#402/#449) |
| **PR #401** | **MERGED** 2026-06-24; mergeCommit=`8579d6536`; mergedBy=spaarke-dev |
| **Master HEAD** | (varies — many post-R6 merges; R6's work is preserved as ancestor) |
| **Next Action** | **TIER-C diagnostic** (1-2 hours, no code) — discover why LLM has zero workspace visibility despite Pillar 6 backend being shipped. Findings drive the surface completion sprint scope. |

### UAT Result Summary (2026-06-25 walkthrough by user)

| Tier | Result | Detail |
|---|---|---|
| **A — Pillar 8 known slashes** | ❌ Not functional | User: "defer in a focused project for now" |
| **B — Pillar 8 remaining slashes** | ❌ Not functional | Same deferral |
| **C — Workspace ↔ Assistant visibility** | ❌ **CRITICAL** | LLM says "I don't have direct real-time visibility into your workspace tabs". Core feature of the system. |
| **D — Execution trace widget** | ❌ Empty | Context pane shows nothing — task 095 SSE bridge + ExecutionTraceWidget mount not done |
| **E — Add to Assistant per-tab toggle** | ❌ Untestable | UI not rendered — task 098 pending |
| **F — NL backward compat** | ⚠️ Partial | Works to the extent underlying functionality exists; otherwise LLM defaults |
| **G — Dark mode** | ✅ Working | ADR-021 conformance confirmed |
| **Pillar 1 — Persona** | ❌ Untestable | Can't tell; only SYS-DEFAULT reachable until Builder UI lands (task 091) |
| **Pillar 7 — Memory + Pinned** | ⚠️ Partial | LLM holds session-level multi-doc memory; pinned/CRUD UI invisible (tasks 096 / 070-UI not surfaced) |

---

## What Just Happened (2026-06-23/24/25 sessions)

### 2026-06-23 — Pre-merge cleanup
1. Committed researcher memory + current-task tracker snapshot (`e5ebe3126`)
2. Reverted husky drift; deleted stale design-doc mirror
3. Merged `origin/master` (84 commits) into R6 — zero textual conflicts (`4101a8fdc`)
4. Diagnosed CI failure: 9 errors inherited from master regression `c0683feaf` (IGenericEntityService refactor) + 2 R6-specific `commandIntent` references

### 2026-06-24 — Sync + merge
5. Waited for daily-briefing master fix (PR #417, #448, #449 landed)
6. Re-merged master into R6 (`e1957f735`); resolved MEMORY.md (kept both)
7. Renamed `commandIntent:` → `intentHint:` in `CapabilityRouterSoftSlashTests.cs:290,307` (`50881dad8`)
8. Fixed Pillar 8 fixture: registered 4 soft-slash synthetic capabilities in `BuildRouter()` default manifest to satisfy Hotfix #4 empty-manifest guard (`344975b41`)
9. Synced 3 more master commits (PR #407 nightly-health, #402 test-runner retry, #449 continue-on-error) → `0c9375310`
10. **PR #401 merged to master** at 18:43 UTC (merge commit `8579d6536`)
11. **Environment Promotion succeeded** at 19:06 UTC — R6 deployed to spaarke-dev

### 2026-06-25 — UAT + status sync
12. User ran UAT walkthrough Tiers A-G + Pillars 1, 7 (results above)
13. User flagged: "we have invested A LOT of time on this project and the functionality needs to get integrated and surfaced into actual user functionality"
14. Plan formulated: **R6 Surface Completion Sprint** (~20 hours, single branch, one PR, no POML overhead)
15. **TASK-INDEX.md + all 80 POML statuses synced to truth** (this session) — 41 stale POMLs updated to `completed`; closeout audit tasks 091-098 + TIER-C added to TASK-INDEX
16. Project README.md / plan.md / CLAUDE.md status fields updated

---

## Critical Context (1-3 sentences)

R6's backend is fully shipped and deployed to master. UAT confirmed: the backend works but the USER-FACING SURFACE is largely unwired — Pillar 6 workspace ↔ assistant visibility (Tier C) is the most critical gap; trace widget (Tier D), Add-to-Assistant toggle (Tier E), and `/export` history wiring (097b) are smaller gaps. The recommended next step is a tight 20-hour surface completion sprint starting with a 1-2 hour Tier C diagnostic.

---

## Next Action (EXPLICIT)

### Step 1 — TIER-C diagnostic (1-2 hours, NO code changes)

Discover why the LLM has zero workspace visibility. Three suspects ranked by likelihood:

1. **Most likely**: The 3 workspace chat tools (`send_workspace_artifact`, `update_workspace_tab`, `close_workspace_tab`) aren't actually registered in `sprk_analysistool` against spaarke-dev — Phase 1 seed may have missed them OR the chat-routing-redesign-r1 successor merge restructured the registry path
2. **Possible**: Workspace state snapshot in system prompt is computed but empty (no tabs open at test time — verify by checking `WorkspaceStateService` Redis state during user session)
3. **Possible**: Successor merge re-pathed `SprkChatAgentFactory.CreateAgentAsync` and the per-turn workspace snapshot injection was dropped

**Output**: 1-pager naming root cause + concrete fix path. No code yet.

### Step 2 — Surface Completion Sprint (~20 hours, 3 working days)

If TIER-C diagnostic confirms suspect 1 or 3 (small fix scope), proceed with the plan from this conversation:

**Day 1 (8h)**: TIER-C fix + 097b (`getConversationHistory`) + 098 (`AddToAssistantToggle`) + 096 (`PinnedMemoryListWidget` mount)
**Day 2 (6h)**: 095 (trace SSE bridge + widget mount) + integration sweep
**Day 3 (6h)**: Deploy to spaarke-dev + UAT walkthrough + iterate

If TIER-C diagnostic reveals successor merge structurally broke R6 integration: re-scope (may push toward rolling into chat-routing-redesign-r1 successor).

### Step 3 — Builder UI (~2 days, 091 + 093)

Without these, makers can't author personas or Q5 routing. Same branch.

### Step 4 — Closeout (089 + 090, ~1 day)

Run `/code-review` + `/adr-check` + `/repo-cleanup`. Phase D exit-gate. README/plan status flip. Lessons-learned + R7 backlog seed. Merge final PR. R6 declared done.

---

## Decisions Made (2026-06-25)

| # | Decision | Why |
|---|---|---|
| 1 | Tier A/B slash commands deferred to a separate focused project | Per user: entangled with chat-routing-redesign-r1 successor's commandIntent rework; not fixable inside R6 closeout scope |
| 2 | Surface completion done in R6 (not rolled into successor) | Successor is doing architectural rework; surface gaps need fixing NOW for users; mixing concerns slows both |
| 3 | NO new POML files for surface completion sprint | ~20 hours of focused fixes doesn't justify POML overhead; track via TASK-INDEX + this file |
| 4 | Builder UI (091, 093) IN scope for R6 closeout | Without persona + routing UI, platform is shipped but not configurable by makers |
| 5 | TIER-C diagnostic before scoping | Could be small (unwired) or big (structurally broken by successor merge); answer changes the plan |
| 6 | TASK-INDEX + POML statuses synced to truth this session | Was lying about progress; misleading for context recovery; now accurate |

---

## Open Items

| Item | Resolution path |
|---|---|
| TIER-C diagnostic — root cause of LLM ↔ workspace gap | Step 1 (1-2 hours, this session or next) |
| 7 closeout audit tasks (091, 092, 093, 095, 096, 097b, 098) + TIER-C — no POML files for 4 of them | Created stubs for 091, 092, 093; rest tracked in TASK-INDEX only per user direction |
| Slash command focused project — when to launch? | User to decide: parallel to this sprint or after |
| Successor project (chat-routing-redesign-r1) scope impact | TBD post-TIER-C diagnostic |

---

## Failure Modes If Context Is Lost

- **If "is R6 in master?" got muddled** — YES. PR #401 merged 2026-06-24 18:43 UTC, commit `8579d6536`. Deployed 19:06.
- **If "did UAT pass?" got muddled** — NO. Tiers A-E failed; F partial; G passed. See UAT Result Summary table above.
- **If "is R6 done?" got muddled** — NO. Backend shipped + deployed but surface incomplete. R6 cannot close until surface completion + UAT re-test + 089/090.
- **If "what's next?" got muddled** — TIER-C diagnostic. 1-2 hours. No code. See Step 1 above.

---

## Repo State Snapshot (2026-06-25)

### R6 worktree
- Branch: `work/spaarke-ai-platform-unification-r6`
- HEAD: `0c9375310` (post-merge sync with master CI hotfixes)
- Working tree: clean (after this session's housekeeping commit)
- PR #401: **MERGED** to master 2026-06-24 18:43 UTC
- Deploy: Environment Promotion success 19:06 UTC on commit `8579d6536`

### Master
- Master HEAD has moved beyond R6 with multiple subsequent merges (smart-todo-r4 closeout, etc.)
- R6's work is preserved as ancestor

### Successor (`work/spaarke-ai-platform-chat-routing-redesign-r1`)
- Separate worktree at `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\`
- PR #409 already merged to master before R6
- Independent of R6 closeout; no overlap per audit

---

*End of state snapshot. Ready for TIER-C diagnostic OR /compact.*
