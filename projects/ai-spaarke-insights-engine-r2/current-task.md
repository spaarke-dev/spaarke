# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Reset between tasks; only the CURRENTLY-ACTIVE task lives here.

---

## Status

**Current task**: 003 — Deploy-Playbook.ps1 lint check (action-code + configjson preservation per D-01)
**Task file**: [`tasks/003-deploy-playbook-action-lint.poml`](tasks/003-deploy-playbook-action-lint.poml)
**Wave**: B (Unblock synthesis — path-b re-scope per D-01)
**Status**: not-started — ready to begin
**Started**: —
**Rigor**: STANDARD
**Next action**: Begin Step 1 of task 003 (read Deploy-Playbook.ps1 + identify where lint check goes)

---

## Task 002 — COMPLETED (2026-06-02)

| Acceptance criterion | Status |
|---|---|
| 6 sprk_analysisaction rows queryable in Spaarke Dev | ✅ |
| Each row's sprk_systemprompt is valid JPS JSON conforming to schema | ✅ |
| Each row's sprk_actiontype matches the enum integer | ⚠️ **Field doesn't exist on entity — D-01 Q1 empirical confirmation** |
| Authored via /jps-action-create skill conventions | ✅ (deterministic-node adaptation) |
| notes/drafts/wave-b-action-codes.md ready for task 003 | ✅ |

### Created Guids (for task 003 + 004 consumption)

| Action Code | ActionType | Guid |
|---|---|---|
| **INS-FACT** | LiveFact (80) | `5137365a-825e-f111-a825-6045bdebafa9` |
| **INS-IDXR** | IndexRetrieve (90) | `23939266-825e-f111-a825-6045bdebafa9` |
| **INS-EVID** | EvidenceSufficiency (100) | `6139aa6c-825e-f111-a825-6045bdebafa9` |
| **INS-GRND** | GroundingVerify (70) | `32eafa72-825e-f111-a825-6045bdebafa9` |
| **INS-DECL** | DeclineToFind (110) | `d1121079-825e-f111-a825-6045bdebafa9` |
| **INS-RART** | ReturnInsightArtifact (120) | `96d52e7f-825e-f111-a825-6045bdebafa9` |

### 🔴 Key finding for tasks 003 + 004

**`sprk_actiontype` field is ABSENT on `sprk_analysisaction` entity** (D-01 Q1 confirmed empirically). The FK-path dispatch is closed. **The ONLY dispatch path for our 6 Insights ActionTypes is `sprk_playbooknode.sprk_configjson.__actionType`**. This shifts the emphasis of tasks 003 + 004:

- Task 003 lint: verify deployed `sprk_playbooknode.sprk_configjson.__actionType` matches the JSON spec's `actionType` per node
- Task 004 redeploy: ensure Deploy-Playbook.ps1 writes the correct `__actionType` integer (80/90/100/70/110/120) into each node's configjson — NOT the canvas-Designer-default 0

The 6 action rows still provide value:
- Linkable refs via `sprk_actionid` for audit/UI
- JPS contract documentation in `sprk_systemprompt`
- Future-proof if `sprk_actiontype` field is added in a later schema migration

---

## Project context

- **Project**: `ai-spaarke-insights-engine-r2`
- **Branch**: `work/ai-spaarke-insights-engine-r2`
- **Decision record**: [`decisions/D-01-wave-b-root-cause-corrected.md`](decisions/D-01-wave-b-root-cause-corrected.md) — APPROVED; Q1 empirically confirmed 2026-06-02

---

## Wave sequencing

Wave B FIRST → A → C → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | 🔄 in-progress (001 ✅, 002 ✅; 003 ready) |
| **A** (Foundations) | 010–015 | 🔲 |
| **C** (JPS compliance) | 020–024 | 🔲 |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 |
| Wrap-up | 090 | 🔲 |

---

## Active task: 003 (ready to start)

### Goal

Deploy-Playbook.ps1 has a lint check that rejects playbook JSON whose nodes lack proper action-code references AND verifies that deployed `sprk_playbooknode.sprk_configjson` matches the JSON spec (catches canvas Designer clobbering per D-01 §2.4). Pre-Dataverse-writes. Tested with both passing + deliberately-broken inputs.

### Steps to be executed

1. Read existing Deploy-Playbook.ps1 — identify where lint check goes (post-JSON-load, pre-Dataverse-writes)
2. Implement Test-PlaybookNodeActionWiring function — iterates nodes, returns array of node names missing actionCode (uses task 002 final codes: INS-FACT, INS-IDXR, INS-EVID, INS-GRND, INS-DECL, INS-RART)
3. Implement Test-PlaybookNodeConfigJsonPreservation — post-deploy, queries Dataverse for each node's sprk_configjson and validates against the JSON spec (catches Designer clobbering)
4. Wire into main flow with clear error messages
5. Test with valid JSON (passes) and deliberately broken JSON (fails with informative message)
6. Update scripts/README.md registry entry

---

*Reset on task transition.*
