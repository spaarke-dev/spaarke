# Current Task State — spaarkeai-assistant-enhancements-r4

> **Last Updated**: 2026-08-15 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Tracks the **active task only**; history lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarkeai-assistant-enhancements-r4 — initialized, **execution NOT started** (owner-gated) |
| **Task** | none active — ready to begin **task 001** |
| **Status** | Project fully initialized + net10-ready + plan aligned with post-review master. Nothing mid-flight. |
| **Next Action** | In the fresh session: **invoke `task-execute` for task 001** (Phase 0 — behavior-gap register + eval harness). Then the E1 spine `010 → 011 → 012 → 013`. Per repo convention start when the owner says "work on task 001" / "continue". |

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
