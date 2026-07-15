# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-15 (UAT Part-A run + PromptShield activation + branch sync) — by session wrap-up.
> **Recovery**: Read Quick Recovery first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | All substantive r2 work COMPLETE + merged + deployed. **Compose-r2 block CLEARED** — compose-r2 merged + **archived** to master (#644–#648; DEF-UAT-1 p2 + DEF-UAT-2 session-identity fixes shipped). **Live-UAT executed 2026-07-15 (Part A) → 4 open defects (A1/A2/A5/A6).** **ADR-041/042 NOT yet Accepted** (Part A did not pass). |
| **Deployed state (spaarkedev1)** | BFF `spaarke-bff-dev` **Healthy** (carries compose round-9 session fixes, deployed 2026-07-14). **PromptShield ACTIVATED 2026-07-15**: `AiSafety__PromptShield__ChatPipelineEnabled=true`; endpoint → `spaarke-openai-dev` (AIServices, eastus — no new resource needed); MI `mi-bff-api-dev` already had **Cognitive Services User**; `shieldPrompt` API verified live. create-matter seed Active (Action `63f086d3…` + Binding `89cd91f6…`, re-verified). memory.write row Active; Cosmos live. NOTE: live build = compose-branch build (behind current master by other projects' work; not core-UAT-critical). |
| **Prerequisites** | P1 ✅ healthy · P2 ✅ create-matter Active · P3 ✅ shield active · P4 ✅ memory · P5 = operator data. |
| **Next action** | **Work the UAT defects** (`notes/UAT-defects-partA-2026-07-15.md`): decide **disposition (A)** patch chat creation vs **(B)** route structured create to a pre-seeded wizard (**recommend B** → `spaarkeai-assistant-enhancements-r1`). Fix A5/A6 (SpaarkeAi/compose surface). **Resolve record-bound launch** so Part B can run. Then re-run UAT → clean pass → ADR-041/042 Accepted → 090 close. |
| **Open PRs** | **#636** (Spaarke AI 101 surface-accuracy fixes) — OPEN, awaiting operator review/merge. |
| **Blocked on operator** | (1) ~~PromptShield~~ **DONE 2026-07-15**. (2) **Disposition A vs B** for chat create-task/create-matter (recommend B). (3) **Record-bound launch** verification (Part B blocked without it). (4) Optional: "Linear vs Multistep" product-strategy statement (offered). |

### UAT defects
**Round 2026-07-12 (RESOLVED):** DEF-UAT-1 p1 (host-context param) fixed #637; DEF-UAT-1 p2 + DEF-UAT-2 (session-identity) **shipped by compose-r2** (DEF-10/UAT-1p2 + DEF-19/UAT-2; merged + archived).

**Round 2026-07-15 — Part A (OPEN) — `notes/UAT-defects-partA-2026-07-15.md`:**
- **A1 / A2** — only one generic `create-task` capability exists (→ creates `sprk_event`); no `create-todo`; over-elicits; no association picker; assign-to-me + attach dropped. Fix direction: **(B) route to pre-seeded wizard** (`spaarkeai-assistant-enhancements-r1`). Data-layer bug (task→Event) needs an owner regardless.
- **A5** — "delete task" also **closed the Compose tab** (SpaarkeAi/compose tab-lifecycle surface).
- **A6** — "draft in Compose editor" **claimed opened but tab didn't open** (UI-action truthfulness; core-ack vs compose DEF-08 triage).
- **create-matter (C1-adjacent)** — LLM resolving closed option-sets (practice area / matter type) **dead-ends**; fix = deterministic resolver + wizard hand-off (assistant-enhancements-r1).
- **Part B BLOCKED** — record-bound launch not available/verified; memory cross-session scenarios can't run until resolved.

Full failure analysis + resolutions (design input for the next project): `projects/spaarkeai-assistant-enhancements-r1/notes/uat-failure-analysis-2026-07-15.md`.

### Product-strategy alignment (playbooks) — resolved
- Terminology: **Linear** (single-step prompted Action path = `Services/Ai/LinearConsumers/`, ActionRunner) vs **Multistep** (composite). (NOT "degenerate.")
- Ratified (OQ-2, 2026-07-05, architecture doc §4.2): playbook node-graph engine **FROZEN** (Insights only, retired by attrition); **single-node/Linear wrappers dissolve**; dispatch = **Binding → prompted Action** directly; "playbook" = product language + composite container (new composites = `coded`); PlaybookBuilder → BA scope/prompt/Binding editor.
- **Build is transitional**: chat capabilities = direct Actions; legacy analysis/Insights playbooks STILL on frozen engine (not yet migrated).

### Spec-vs-built reconciliation (signed) — `notes/spec-vs-built-reconciliation-2026-07-10.md`
65/65 FR/NFR dispositioned; 58 delivered. Agreed-out (operator-signed): #616 retrieval ACL, memory hard-governance (group-e), FR-B-03 review/delete UI (API-only), job-aware chat card, #629 FR-30 (delivered by compose-r2 2026-07-14, `0ac4c260c`), #592/#591/#612/#594/#617/#619a, Work IQ runtime, close-out groups a/b/c/d/f.

### Remaining to close (090)
After UAT sign-off: flip 049/069 gate rows + 090; file named deferrals (groups a–f) via /defer; lessons-learned; test-diet already done; wrap-up PR citing the signed reconciliation; /repo-cleanup; /devops-project-sync completion. **BLOCKED until Part A defects worked + UAT re-run passes.**

### Coordination
Compose-r2: **DONE — merged + archived** (#644–#648; FR-30 durable memory capture shipped). No further coordination needed. A5/A6 defects touch SpaarkeAi/compose surfaces — route to whoever owns the post-compose SpaarkeAi surface.

*Resume: "continue" → work Part-A defects (disposition B) + resolve record-bound + fix A5/A6, then re-run UAT. Fable session; sub-agents can't write .claude/.*
