# Current Task State — spaarkeai-assistant-enhancements-r4

> **Last Updated**: 2026-08-15 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Tracks the **active task only**; history lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarkeai-assistant-enhancements-r4 — **EXECUTION STARTED 2026-08-15** (owner ran `/task-execute` + "parallel + autonomous where safe") |
| **Task** | ✅ **001, 022, 010 COMPLETE** (2026-08-15/16). Next up: **011** (opus/xhigh — E1 pre-filter boundary). |
| **Status** | Wave 1: 3 of ~6 startable done. Nothing mid-flight — all committed locally (not pushed). |
| **Next Action** | **011** (agent-path consumption: thread the resolved `GroundedToolAllowList` into `AgentToolFilterContext` + a deterministic `AgentToolProjection.PreFilter` narrowing predicate). ⚠️ **This is the ADR-039 "second decider" boundary (opus/xhigh, escalation trigger) — start it with FRESH context, not a depleted one.** Then 012 → 013. Remaining wave-1: 020 (OBO wording, sonnet BFF), 030 (Preference enum, opus BFF) — serialize (BFF dotnet builds). **040 needs a `--chrome` live-DOM session.** |

### Wave-1 completion notes (2026-08-15/16)
- **010** (FR-03): `sprk_groundedtoolallowlist` field as catalog DATA (DTO + 3 `$select` + `AnalysisAction.GroundedToolAllowList` model + 3 materialize sites + `ParseGroundedToolAllowList` fail-closed helper). 12 unit tests PASS · BFF build 0 errors · CVE clean · **Step 9.5 gates CLEAN** (ADR-039/013/038 compliant; field grep-verified inert = zero consumers) · **publish 44.96 MB compressed = baseline, delta +0.00, ≤60 MB** (§10: measure COMPRESSED zip, not the 137 MB raw folder). Agent-path MOUNTING deferred to 011.
- **001** (FR-12/10): behavior-gap register confirmed + `tests/integration/contract/Eval/assistant-r4-eval-cases.json` (template `AR4-001`) + convention docs. `.cs` harness deferred to the FR-01 task. Reused R1 net-new-family precedent.
- **022** (FR-06): 2 `workspace-tab` launch entries (daily-briefing, smart-todo) + 2 tests. ⚠️ **FLAG for owner/task-080**: Briefing + Smart To Do are `sprk_workspacelayout` rows opened via the generic `'workspace'` widget type keyed by a **per-environment auto-generated `layoutId` GUID** — the agent hardcoded live spaarkedev1 GUIDs (mirrors existing `ENTITY_VIEW_CONFIG_IDS`). Multi-env deploy needs these GUIDs updated per environment.
- **OWED before any PR**: `/conflict-check` on Services/Ai (010) + shared lib surfaceLaunchRegistry.ts (022) — compose-r5/r6 + assistant-r3 overlap. **Task 080**: create the `sprk_groundedtoolallowlist` column on `sprk_analysisaction`.

### Parallelism decision (why only 001+022 are background agents)
The three BFF wave-1 tasks (010/020/030) share the `Services/Ai` spine AND would run concurrent `dotnet build` in ONE worktree (bin/obj corruption) → cannot safely parallelize as agents here. Only the two non-BFF `parallel-safe:true` tasks (001 docs/eval, 022 client/npm) run as autonomous background agents; the BFF spine runs sequentially in the main session (Opus 4.8 = correct tier). 040 needs live-DOM → not autonomous.

### 010/011 boundary (LOAD-BEARING — don't re-litigate)
- **010** = the field as catalog **DATA**: `ActionEntity.GroundedToolAllowList` DTO (`sprk_groundedtoolallowlist`, multiline JSON array of grounded-tool ids), added to the 3 `$select` sites, `AnalysisAction.GroundedToolAllowList` model prop (parsed `IReadOnlyList<string>`; **empty = opt-out/ack-tier**), materialized at the 3 constructor sites via `ParseGroundedToolAllowList` (fail-closed → empty), + unit tests. Mirrors `sprk_allowsknowledge` read/materialize shape. **No agent-path edits.**
- **011** = agent-path **consumption**: thread the resolved allow-list into `AgentToolFilterContext` + the deterministic `AgentToolProjection.PreFilter` narrowing predicate (the ADR-039 boundary; has an escalation trigger). 010's "mounts zero/exactly" criteria are DATA-verified in 010, BEHAVIOR-verified in 011.

### Git / baseline state (all clean)
- Branch `work/spaarkeai-assistant-enhancements-r4` @ **`7fbb9f5f9`** — **0 uncommitted, 0 unpushed, 0 behind master**.
- **Runtime = .NET 10** (`global.json` 10.0.100; BFF csproj `net10.0`; `dotnet build -c Release` verified clean, 0 errors). BFF builds/deploys need SDK ≥10.0.100; **never deploy the BFF from a net8 tree**. If `dotnet` can't find the SDK → stale shell, open a fresh terminal (not a code problem).

### Critical Context (for continuation)
- **No code written yet** — only planning artifacts + 17 task POMLs. First real work is task 001.
- Plan was **verified aligned with master** after the BFF + code-quality review merged (2026-08-15): all 20 file anchors + key symbols intact (`sprk_allowsknowledge`, `MemoryFactType` 4-members-no-`Preference`, `spaarke.grid_overview`/`spaarke.daily_briefing_overview`, `list-tasks` registry entry). `output_determinism: advisory` confirmed authorable catalog data (actions JSON; precedent `agreement-review`). The review touched only `SprkChat/hooks/useChatFileAttachment.ts` (security tweak) — no R4-target contract reshaped.
- **Publish size**: re-baseline fresh under net10 (the ~49.63 MB figure was net8) on every BFF task + task 080.

---

## Full State (Detailed)

### What's done (this initialization arc)
1. `/design-to-spec` → `spec.md` (12 FR / 9 NFR / 3 ADR tensions), both open questions resolved with the owner (2026-08-13).
2. `/project-pipeline` (INITIALIZE-ONLY) → README, plan.md, CLAUDE.md, current-task.md, `notes/behavior-gap-register.md`, **17 task POMLs + TASK-INDEX.md** (validator PASS: 0 errors). Registered R4 in `projects/INDEX.md`.
3. net10 readiness: merged net10 master; BFF build clean; net10 notes baked into CLAUDE.md/plan/TASK-INDEX/current-task.
4. Post-review sync: merged master (BFF + code-quality review); alignment verified; fixed seed-script path `scripts/dataverse/Seed-PlaybookConsumers.ps1`.

### Owner decisions (2026-08-13) — binding for execution
- Build approach = **reuse the existing single decider** (advisory mode + pre-filter bounded tools; **no new executor**).
- Preference steering = **narrow closed allow-list → pre-turn tool hints only** (never grants a capability or alters a fact).
- Agenda surfaces = **Tasks only + inline grounded summary + Briefing/Smart-To-Do follow-on cards if not already open**.
- Operator promotion queue = **out of system scope** (CX/product-owner exercise).
- E3 memory = **owned entirely in R4** (redesign-r2 closed).
- Advisory tier = **ADR-016 Reasoning tier, temp ~0.2–0.3**.

### Execution order (from TASK-INDEX)
- **Foundation**: 001 (parallel-safe). 
- **E1 spine** (sequential, opus): 010 → 011(xhigh) → 012 → 013. The P1 value-proving DoD.
- **Wave-1 independent** (alongside E1): 020 (OBO wording), 022 (registry entries), 030 (Preference type), 040 (D9).
- **E2** (sequential SprkChat/ConversationPane spine): 021 → 023 → 024. Deps 012, 022.
- **E3**: 031, 032(xhigh) → 033. Dep 030.
- **Deploy/Wrap**: 080 → 090.

### Coordination before any PR
- `/conflict-check` before every BFF / `ConversationPane` / `SprkChat` PR. Live overlap: **compose-r5/r6** + **assistant-r3** (the review just touched an adjacent SprkChat hook). Memory files have no live contender (redesign-r2 closed).
- All BFF-touching tasks: measure publish ≤60 MB (re-baseline under net10) + no new HIGH CVE.

### Files Modified This Session
- `projects/spaarkeai-assistant-enhancements-r4/current-task.md` — this handoff.
- (Earlier this arc, all committed + pushed: spec.md, README, plan.md, CLAUDE.md, notes/behavior-gap-register.md, 17 task POMLs, TASK-INDEX.md, projects/INDEX.md.)

### Decisions (this session)
- INDEX.md merge conflicts resolved keeping R4 + master's new/updated rows (dotnet-10-upgrade-r1 now ✅ COMPLETE; code-quality-and-assurance-r3 added).
- Seed-script path corrected to `scripts/dataverse/Seed-PlaybookConsumers.ps1`.

---

## To resume in the fresh session
Say **"work on task 001"** (or "continue") → `task-execute` loads CLAUDE.md + the task POML + ADRs and begins. Or "where was I?" → re-read this file.
