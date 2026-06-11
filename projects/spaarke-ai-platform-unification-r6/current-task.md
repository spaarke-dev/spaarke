# Current Task State — R6 (Wave C-G3 WIP checkpoint for laptop handoff)

> **Last Updated**: 2026-06-11 (user traveling; pushed WIP checkpoint for laptop continuation)
> **Recovery**: Read "Quick Recovery" + "Wave C-G3 partial-state inventory" + "CRITICAL: Restart instructions" sections
> **Mode**: AUTONOMOUS — user authorized "run autonomously"; bash only; skip approvals
> **Branch**: `work/spaarke-ai-platform-unification-r6` — pushed to origin including this WIP commit

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | C (tri-directional workspace + memory + visibility) |
| **Build state** | 0 errors (verified before commit) |
| **Last clean commit** | `25fe5292e` (Wave C-G2 complete: tasks 054 + 055 + 056 + 061 + 064 + 065) |
| **WIP checkpoint** | This commit — Wave C-G3 partial (057 + 058 + 062 + 063 + 066, mostly incomplete) |
| **Phase A + B + 053 + Wave C-G2** | ✅ All in master (via PR #375) or this branch |
| **Wave C-G3 status** | All 5 sub-agents either rate-limited, stream-stalled, or stopped by main session; partial code on disk |

### What the WIP commit contains
All partial work from the Wave C-G3 sub-agents. **Compiles clean. Tests not run. Evidence notes mostly missing.** This is checkpoint state for cross-machine handoff, NOT a Wave C-G3 closeout.

---

## 🚨 CRITICAL: Restart instructions for laptop session

When resuming on the laptop, follow these in order. Pay attention to the bolded warnings.

### Step 1 — Verify state

```bash
cd c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r6
git pull origin work/spaarke-ai-platform-unification-r6
git log --oneline -3
# Expect to see the WIP commit on top of 25fe5292e

git status --short
# Should be clean

dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q | tail -3
# Expect: 0 errors
```

### Step 2 — DO NOT re-dispatch sub-agents blindly

**The Wave C-G3 first dispatch had a scope-mismatch failure** on task 058 — my brief described client-side work but the POML is server-side. The sub-agent correctly stop-and-surfaced. Lesson:

**Before dispatching any task, READ THE POML at `projects/spaarke-ai-platform-unification-r6/tasks/{NNN}-*.poml` FIRST and use its `<outputs>` + `<steps>` as the canonical scope.** Do not freelance the brief from memory.

For the autonomous mode the user wants, the dispatch pattern is:
1. Read the POML
2. Write the agent brief based on the POML (copy `<outputs>` + `<steps>` verbatim)
3. Verify your brief doesn't contradict any file paths or "MUST touch" lists in the POML
4. Dispatch

### Step 3 — Wave C-G3 partial-state inventory

Each task below has source files already on disk in the WIP commit. **Most need tests + evidence notes to complete.** Read the POML before extending each.

#### Task 057 — User affordances (Pillar 6b)
- POML: `tasks/057-user-affordances-workspace-actions.poml`
- ✅ ON DISK: `src/solutions/SpaarkeAi/src/components/workspace/SendToWorkspaceButton.tsx`
- ✅ ON DISK: `src/solutions/SpaarkeAi/src/components/workspace/AddToAssistantToggle.tsx`
- ✅ ON DISK: `src/solutions/SpaarkeAi/src/components/workspace/PinToMatterButton.tsx`
- ✅ ON DISK: `__tests__/SendToWorkspaceButton.test.tsx`
- ✅ ON DISK: `__tests__/AddToAssistantToggle.test.tsx`
- ❌ MISSING: `__tests__/PinToMatterButton.test.tsx`
- ❌ MISSING: evidence note `notes/task-057-evidence.md`
- ❌ MISSING: verify all 3 tests pass

#### Task 058 — Q8 conflict resolution server-side wiring
- POML: `tasks/058-conflict-resolution-implementation.poml`
- **WARNING**: my initial brief was wrong (described client-side; POML is server-side). The retry agent also timed out. Stick to the POML.
- POML deliverables:
  1. Persona instruction snippet (Pillar 1 SYS- persona row update) telling the agent the stale-read → re-read → re-attempt loop
  2. Telemetry counter `workspace.conflict_refused` at the stale-read refusal point in `UpdateWorkspaceTabHandler.cs`
  3. Integration test `tests/integration/Spe.Integration.Tests/Workspace/ConflictResolutionTests.cs`
- 🟡 PARTIAL ON DISK: `UpdateWorkspaceTabHandler.cs` MODIFIED — check `git diff` to see if telemetry counter was added
- ❌ MISSING: persona row update
- ❌ MISSING: integration test file
- ❌ MISSING: evidence note `notes/task-058-evidence.md`
- **NOTE for resume**: the task-055 handler returns `ToolResult.Ok` with `Status: "stale_read"` (NOT `ToolResult.Failure`). Use that exact response shape in the integration test setup.

#### Task 062 — Register ExecutionTraceWidget
- POML: `tasks/062-register-trace-widget.poml`
- 🟡 PARTIAL ON DISK: `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` MODIFIED — check `git diff` to see if ExecutionTraceWidget registration was added
- ✅ ON DISK: `src/client/shared/Spaarke.AI.Widgets/src/registry/__tests__/register-execution-trace-widget.test.ts`
- ❌ MISSING: verify tests pass
- ❌ MISSING: evidence note `notes/task-062-evidence.md`

#### Task 063 — Emit `context.*` events from BFF
- POML: `tasks/063-emit-context-events.poml`
- 🟡 PARTIAL ON DISK: `src/client/shared/Spaarke.AI.Widgets/src/__tests__/widget-serialize-restore.test.ts` was modified — that's unusual for a BFF-emit task; verify whether this is real or noise via `git diff`
- ❌ MISSING: nearly everything — SSE event-emission hooks in SprkChatAgentFactory + PlaybookExecutionEngine
- ❌ MISSING: tests
- ❌ MISSING: evidence note `notes/task-063-evidence.md`
- **Recommendation**: re-dispatch this one fresh (the partial work appears minimal/possibly accidental)

#### Task 066 — Pinned context recall via embedding similarity
- POML: `tasks/066-selective-recall-embedding.poml`
- ✅ ON DISK: `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPinnedContextRecallService.cs`
- ✅ ON DISK: `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRecallService.cs`
- ✅ ON DISK: `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRecallOptions.cs`
- ✅ ON DISK: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/PinnedContextRecallServiceTests.cs`
- ✅ ON DISK: `projects/spaarke-ai-platform-unification-r6/notes/task-066-evidence.md`
- 🟡 ON DISK: `Infrastructure/DI/AnalysisServicesModule.cs` MODIFIED — verify registration is correct via `git diff`
- ❌ MISSING: just verify build + tests pass (most likely complete)

### Step 4 — Recommended laptop resumption order

1. **Build verify**: `dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q | tail -3` → expect 0 errors
2. **Test the partial wave**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~PinnedContextRecall" --logger "console;verbosity=minimal"` — confirm task 066 is green
3. **Single focused gap-fill sub-agent** (mirror what worked for Wave C-G2 closeout): give it the inventory above + POML paths, ask it to: write missing tests + complete partial files + write all missing evidence notes + run verifications. Tight scope = high success rate.
4. **After gap-fill returns**: build verify, run all unit tests, update TASK-INDEX (057, 058, 062, 063, 066 → ✅), commit as `feat(r6): Wave C-G3 closeout`, push
5. **Then proceed with Wave C-G4** (task 067)

---

## Wave Cascade (after C-G3 closeout)

### Wave C-G4
- 067 Hierarchical memory composition — depends on 064 + 065 + 066

### Wave C-G5
- 068 Activate MatterMemoryService + token budget tracker — depends on 067

### Wave C-G6 (parallel)
- 069 Remember/forget/always recognition — depends on 065 + 068
- 070 Q7 EXPANSION: Pinned Memory CRUD + Visualization UI — depends on 065 + 069

### Pillar 9 (parallel-safe with C-G3+)
- 072 widget visibility contract per-widget implementations
- 073 prompt-builder gathering visible state
- 074 refine task 053's workspace block with `getAgentVisibleState()`

### Phase C exit
- 079 Phase C integration test

### Phase D
- 080-089 Pillar 8 (command router) + integration + eval baseline

### Wrap-up
- 090

---

## Sub-agent dispatch lessons learned (READ before dispatching)

1. **Always read the POML first** and quote its `<outputs>` + `<steps>` in your brief
2. **Tasks 40+ tool uses long are at risk** of stream-idle-timeout — break large tasks into smaller agents
3. **5/6 agents failed in C-G2 first attempt; 3/5 failed in C-G3**. After failures, gap-fill via ONE focused agent worked for C-G2; same pattern likely works for C-G3
4. **Sub-agents CAN modify `UpdateWorkspaceTabHandler.cs`** for task 058 (telemetry counter). My earlier brief incorrectly forbade this. The POML is the source of truth.

---

## R7 Backlog Carried Forward

- Followup-card playbook orchestration (B12b)
- Deploy-SpaarkeAi.ps1 stale-bundle check
- Persona seed-row directive consolidation
- B-G10b compact-formatting → persona seed-row
- Matter-prefill technical-debt sweep
- Memory UI revisits (Q7) — partially addressed by task 070
- AI.Widgets subpath imports tsconfig fix

---

*Source of truth for resuming autonomous work after any context boundary (including cross-machine handoff).*
