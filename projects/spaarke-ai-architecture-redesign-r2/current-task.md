# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-12 (pre-compact handoff for UAT continuation) — by context-handoff.
> **Recovery**: Read Quick Recovery first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | **All substantive r2 work COMPLETE + merged + deployed.** In the **live-UAT + defect-intake** stage. Resuming: continue the browser UAT and address findings. |
| **Deployed state (spaarkedev1)** | master `07aecc801` (both projects). BFF + SpaarkeAi live. **create-matter seeded live** (Action `63f086d3…` + Binding `89cd91f6…`; healthz Healthy). **DEF-UAT-1 part-1 fix deployed** (SpaarkeAi rebuilt+published `sprk_spaarkeai`). memory.write row Active; `memory-items`+`audit-partitioned` Cosmos live. |
| **Next action on resume** | Continue **CONSOLIDATED-UAT-CHECKLIST** (`notes/CONSOLIDATED-UAT-CHECKLIST.md`): Parts A/B/C1/C3 ready NOW; **re-test "Open in Compose" host context** (part-1 fix just deployed — Assistant should know the host document); C2 (injection) waits on shield. Clean pass → ADR-041 + ADR-042 Accepted → 090 close. |
| **Open PRs** | **#636** (Spaarke AI 101 surface-accuracy fixes) — OPEN, awaiting operator review/merge. #637 (DEF-UAT-1 part-1) — MERGED. #633/#635 merged. |
| **Blocked on operator** | (1) **🔔 PromptShield activation infra decision** — NO ContentSafety resource on dev (only `spaarke-openai-dev` AIServices); needs endpoint choice + MI "Cognitive Services User" role grant; shield ships default-OFF. (2) **UAT run** + findings. (3) Optional: operator wants a written **"Linear vs Multistep / Action vs Binding vs Playbook" product-strategy statement** — offered, not yet requested. |

### Live UAT defects (2026-07-12) — `notes/UAT-defects-launch-context-and-session-2026-07-12.md`
- **DEF-UAT-1 part 1** (host-context param mismatch: launcher emits `entityLogicalName`, app read only `entityType`) — **FIXED (main.tsx reads either) + DEPLOYED + MERGED #637**.
- **DEF-UAT-1 part 2** (Compose/host document TEXT not shared with Assistant — "summarize this document" fails) → **compose-r2** (session-identity surface). Handoff written.
- **DEF-UAT-2** (chat session not context-scoped — home page shows Document's session; single global `sprk_ai2_chatSessionId` localStorage key in AiSessionProvider) → **compose-r2**. Handoff written (`spaarke-wt-spaarkeai-compose-r2/.../HANDOFF-from-core-uat-defects-launch-and-session-2026-07-12.md`).

### Product-strategy alignment (playbooks) — resolved this session
- Terminology: **Linear** (single-step prompted Action path = `Services/Ai/LinearConsumers/`, ActionRunner) vs **Multistep** (composite). (NOT "degenerate" — that was r7's transitional "degenerate 3-node playbook.")
- Ratified (OQ-2, 2026-07-05, architecture doc §4.2): playbook node-graph engine **FROZEN** (Insights only, retired by attrition); **single-node/Linear wrappers dissolve**; dispatch = **Binding → prompted Action** directly (no wrapper); "playbook" = product language + composite container (new composites = `coded`); PlaybookBuilder → BA scope/prompt/Binding editor (no maker graphs).
- **Build is transitional**: chat capabilities (summarize/classify/create-*) = direct Actions; legacy analysis/Insights playbooks (Document Profile, Email Analysis, matter-health, pre-fills, Document Summary, Summarize File) STILL on frozen engine (not yet migrated). Operator's "they moved off" = end-state, not current.
- Not every Action needs a playbook wrapper (that was r7; walked back).

### Spec-vs-built reconciliation (signed) — `notes/spec-vs-built-reconciliation-2026-07-10.md`
65/65 FR/NFR dispositioned; 58 delivered. Agreed-out (operator-signed): #616 retrieval ACL (security project), memory hard-governance group-e (governance project), FR-B-03 memory review/delete UI (API-only), job-aware chat card (invariant delivered on Compose path; card agreed-out), #629 FR-30 (governance project), #592/#591/#612/#594/#617/#619a, Work IQ runtime, close-out groups a/b/c/d/f.

### Remaining to close (090)
After UAT sign-off: flip 049/069 gate rows + 090; file named deferrals (groups a–f) via /defer; lessons-learned; test-diet already done; wrap-up PR citing the signed reconciliation; /repo-cleanup; /devops-project-sync completion.

### Coordination
Compose-r2: joint deploy done (#632/#634); owns DEF-UAT-1 part 2 + DEF-UAT-2 + FR-30/#629 (governance project). Session-identity two-session model: core does NOT unify (confirmed). Deploy rule: merge worktree with master BEFORE any deploy; coordinate SpaarkeAi deploys.

*Resume: "continue" → UAT (Parts A/B/C1/C3 + re-test Open-in-Compose); intake findings; then shield decision + 090 close. Fable session; sub-agents can't write .claude/.*
