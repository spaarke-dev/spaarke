# Current Task State — spaarkeai-assistant-enhancements-r4

> **Last Updated**: 2026-08-17 (by task-execute — **024 COMPLETE**; autonomous run reached the owner-gated tail)
> **Recovery**: Read "Quick Recovery" first. Tracks the **active task only**; history lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarkeai-assistant-enhancements-r4 — autonomous execution (owner "continue"). **15 done; 3 remain, ALL owner-gated.** |
| **Task** | ✅ **024 COMPLETE** (E2 eval cases, FR-10). Next 🔲 **040** — but it is **NOT autonomous** (needs a `--chrome` live-DOM session + owner). |
| **Status** | 024 committed locally (PR HELD). **Autonomous progress is complete** — 040/080/090 all require owner involvement (see below). |
| **Next Action** | Await owner. To resume: `/task-execute 040` in a **`--chrome`** session (D9 live-DOM repro), OR owner runs 080 (deploy) when ready. |

### 024 outcome (this session)
FR-04/FR-06 guards were already authored WITH their features (ADR-038-preferred): 021a service off-catalog drop, 021b client untyped/unbacked drop, 023 card open-tab gating, 013 AR4-003 dispatch. Per ADR-038 binding anti-scaffolding + §11, did NOT duplicate. Added the one gate-owed guard — FR-04 no-dead-end CONTRACT fact `SuggestFollowupsAction_IsGroundedTypedTwoKindProposer_NoDeadEndFreeString` (**8/8 R4 eval pass, net10**) — + FR-10 coverage map (`Eval/README.md`), register P2✅, and deferred `D-024-01` (typed-SSE endpoint test needs the live-agent streaming harness). Files: `AssistantEnhancementsR4EvalTests.cs`, `Eval/README.md`, `behavior-gap-register.md`, `notes/defer-issues.md` (new), POML/TASK-INDEX. Test+docs only — no BFF source.

---

## Remaining tasks — ALL OWNER-GATED (autonomous run ends here)

| # | Task | Why not autonomous |
|---|---|---|
| **040** | D9 host-proof flex-chain fix (Open-in-Compose viewport clip, FR-11) | Needs a **`--chrome` live-DOM session** to first confirm D9 still reproduces after the merged partial fix (`messageList min-height:0`), then fix host-proof (no measured heights). Owner involvement required. |
| **080** | Deploy + verify (owner-gated) | MUST create the `sprk_groundedtoolallowlist` column on `sprk_analysisaction` + re-seed; deploy BFF + `sprk_spaarkeai` **together**; **021a+021b deploy TOGETHER** (SSE wire string[]→typed); 022 layout GUIDs are spaarkedev1-specific (per-env update). Never deploy BFF from a net8 tree. |
| **090** | Wrap-up + `/test-diet` gate | Deps 080. `/test-diet` reconciles project tests vs the ADR-038 build-vs-maintain classifier. |

## Standing constraints (unchanged)
- **PR HELD** — all commits LOCAL only; do NOT push/PR until owner asks. **File the `D-024-01` GitHub Issue at PR time** (defer-issues.md has a `{URL}` placeholder).
- Measure BFF publish COMPRESSED (≤60 MB); no new HIGH CVE on BFF tasks. ADR-042 memory hard-governance DEFERRED to #616 (trustLevel inert).
- Commit footer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
