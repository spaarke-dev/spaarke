# Current Task — spaarkeai-assistant-enhancements-r3

> **Reset by**: project-pipeline (2026-08-10) at task generation.
> This file tracks ONLY the active task. History lives in `tasks/TASK-INDEX.md` + per-task `.poml`.

---

## Active Task

- **Task**: 001 — Active-item conduit (widget-agnostic `{id,type,label}`)
- **Status**: in-progress (owner go-ahead 2026-08-10: "autonomous where safe")
- **Rigor**: FULL · **Tier**: opus @ xhigh · **Step mode**: directional
- **Next action**: opus implementer subagent realizing the additive new-module conduit; then build-verify + Step 9.5 gates.

## Blocking / pre-execution notes

- **Master re-sync DONE** (2026-08-10): branch merged `origin/master` (was 5 behind → 0), pushed. Precondition cleared.
- **Coordination**: `/conflict-check` before every BFF / `ConversationPane` PR. Consume `Services/Ai/PublicContracts/` seams (no fork).

## Decisions this task
- **2026-08-10 — Conduit placement (§11 + escalation-trigger reconciliation)**: implement the generalized active-item conduit as a **NEW widget-agnostic module in the SpaarkeAi solution**, carrying ONLY `{id,type,label}`. **`composeActionBridge.ts` LEFT UNTOUCHED** (zero exported-shape change → POML escalation "coordinate with compose-r5/r6 before changing the exported shape" does NOT fire; merge-clean vs compose-r5/r6). Compose's bytes path (`activeSourceDocRef`/`docxBridge.ts`) unchanged → no regression. §11 satisfied: the new module is THE canonical selection spine every widget (incl. Compose tab-focus) publishes to — a generalization, not a parallel duplicate. Reason: safest reconciliation of §11 reuse-first with the cross-worktree contention on `composeActionBridge.ts` (compose-r5 + compose-r6 active).

## Steps completed this task
- [x] Step 0.5 hot-path check (SpaarkeAi=Y; INDEX reviewed; contention on composeActionBridge/ConversationPane confirmed)
- [x] Step 1 task loaded; Step 4/5 knowledge+ADRs (015/030/049) loaded; §11 grep (no existing conduit)

## Files modified this task
- (pending subagent report)
