# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-26
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 001 — Pre-Phase-3 operational verification (🟡 BLOCKED — awaiting AI-1 + AI-2 decisions) |
| **Step** | 10 of 10 (all checks run, evidence captured, findings documented) |
| **Status** | blocked-with-findings |
| **Next Action** | Owner decides AI-1 KV naming + admin-key rotation, AI-2 role-grant path; OR authorize parallel start of Phase 1 docs (002, 004, 005, 007 are NOT blocked) |

### Files Modified This Session

- `projects/spaarke-ai-azure-setup-dev-r1/notes/pre-phase-3-verification.md` — created (10-check evidence file)
- `projects/spaarke-ai-azure-setup-dev-r1/tasks/001-pre-phase-3-verification.poml` — status → blocked-with-findings; added `<notes-results>`
- `projects/spaarke-ai-azure-setup-dev-r1/tasks/TASK-INDEX.md` — task 001 status 🔲 → 🟡

### Critical Context

Task 001 ran all 10 prerequisite checks. **Score: 8/10 PASS.** Two failures:
- **AI-1**: KV admin-key secret `AiSearch--AdminKey` does not exist; the OPERATIONAL secret `ai-search-key` is STALE (holds pre-recreate primary key, different from current LIVE key after 2026-06-25 service recreate); the referenced secret `AzureAISearchApiKey` (used by BFF `AiSearch__ReferencesApiKey`) is also missing → BFF AI-Search calls via that setting are currently broken. **Three different secret names in use** (spec naming vs operational naming vs setting-reference naming) — needs owner decision before Phase 3 deploy + before FR-15 Phase 4 work.
- **AI-2**: BFF MI `mi-bff-api-dev` (principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb`) has ZERO role assignments on `spaarke-search-dev`. Spec recommends "re-run `infrastructure/byok/main.bicep`" but inspection shows the Bicep grants role to `appService.identity.principalId` (system-assigned identity) — BFF App Service uses ONLY user-assigned MI (system-assigned is null), so the Bicep as-written would NOT fix this check. Three remediation options documented.

Redis prereqs (R-1 to R-5) all confirm cutover complete (handoff §1 success log matched exactly at `2026-06-26T12:24:57.2962340+00:00`). AI-3/AI-4/AI-5 all PASS — Service Bus queues exist, `text-embedding-3-large` deployed (Standard cap=120), search service is empty (0 indexes) as spec narrative states.

**Phase 3 (FR-16 schema deploys) is BLOCKED** pending AI-1 + AI-2 remediation. Phase 1 docs (Group A tasks 002, 004, 005, 007) are NOT blocked and can start in parallel while owner reviews findings.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 001 |
| **Task File** | `tasks/001-pre-phase-3-verification.poml` |
| **Title** | Pre-Phase-3 Operational Verification (10 checks per FR-21) |
| **Phase** | 1: Documentation Foundation + Pre-Flight Verification |
| **Status** | blocked-with-findings |
| **Started** | 2026-06-26 |
| **Rigor** | STANDARD |
| **Evidence** | `notes/pre-phase-3-verification.md` |

---

## Progress

### Completed Steps

- [x] Step 1: Created notes/pre-phase-3-verification.md
- [x] Step 2: Ran 5 Redis prereqs (R-1 to R-5 all PASS)
- [x] Step 3: AI-1 KV admin-key check (FAIL — drift + secret naming inconsistency; remediation NOT applied — awaiting owner decision)
- [x] Step 4: AI-2 BFF MI RBAC check (FAIL — role missing; BYOK Bicep targets wrong identity; remediation NOT applied)
- [x] Step 5: AI-3 Service Bus queues (PASS — both Active)
- [x] Step 6: AI-4 OpenAI deployment (PASS — text-embedding-3-large deployed)
- [x] Step 7: AI-5 Empirical search service state (PASS — Succeeded / standard / 0 indexes)
- [x] Step 8: Summary captured in evidence file
- [x] Step 9: TASK-INDEX.md updated (🔲 → 🟡)
- [x] Step 10: current-task.md updated (this file)

### Current Step

Awaiting owner decisions on AI-1 + AI-2 remediation paths. Phase 1 doc tasks 002, 004, 005, 007 can proceed in parallel during the wait.

### Files Modified (All Task)

| File | Purpose |
|---|---|
| `notes/pre-phase-3-verification.md` | New — 10-check evidence with remediation options |
| `tasks/001-pre-phase-3-verification.poml` | Status → blocked-with-findings; results block added |
| `tasks/TASK-INDEX.md` | Task 001 status 🔲 → 🟡 |

### Decisions Made

- **2026-06-26**: Did NOT autonomously apply remediation for AI-1 (KV admin-key) or AI-2 (MI role grant) because both are security-sensitive (CLAUDE.md §6 trigger) AND the AI-1 fix has design implications for FR-15 (KV secret naming convention) that need owner input before locking in.

---

## Next Action

**Blocking question for owner**:

1. **AI-1 remediation** — choose A/B/C from evidence file. Recommendation: **Option B** (create `AiSearch--AdminKey` per spec + also set `AzureAISearchApiKey` to unbreak existing reference) if FR-15 will standardize on `AiSearch--*` naming. Or Option A if FR-15 will keep existing `ai-search-key` naming.
2. **AI-2 remediation** — choose A/B/C from evidence file. Recommendation: **Option A** (direct `az role assignment create`) for immediate unblock; defer Bicep authority fix (Option B) to Phase 4 cleanup.

**Non-blocking parallel work the agent can start now without owner sign-off**:

- Task 002 (catalog), Task 004 (consumer map), Task 005 (stale doc cleanup), Task 007 (.claude/ ADR pointer drift) — do NOT depend on KV naming decisions.
- Task 003 (operational guide) + Task 006 (deployment guide §4.6) reference KV secret names — soft-blocked, but skeleton + non-secret content can be drafted with `{{kv-secret-name-tbd}}` placeholders.

---

## Blockers

**HARD BLOCKER (Phase 3, tasks 050+)**:
- AI-1 KV admin-key remediation
- AI-2 BFF MI role grant

**SOFT BLOCKER (Phase 1, tasks 003 + 006)**:
- AI-1 secret naming convention decision (affects what name to document in the guides)

**NOT BLOCKED**:
- Tasks 002, 004, 005, 007 (Phase 1 docs that don't reference KV secret names)
- Tasks 010–016 (Phase 2 schemas — code/JSON work, no live Azure dependency)
- Tasks 020–021 (Phase 3 script-WRITING — script itself can be authored with KV-secret-name as parameter; actual deploy is Phase 5 which IS blocked by AI-1/AI-2)

---

## Session Notes

### Current Session
- Started: 2026-06-26 (post-compact)
- Focus: Task 001 — Pre-Phase-3 operational verification

### Key Learnings

- BFF MI is **user-assigned only** (`mi-bff-api-dev`, principalId `9fd47efb-7962-492b-ac44-e5ccd0268ebb`); system-assigned identity is null. Any future role-assignment Bicep MUST reference the user-assigned MI, not `appService.identity.principalId`. The current `infrastructure/byok/main.bicep:443-454` is non-authoritative for dev.
- Three different AI-Search secret names exist across spec / KV / App Settings: `AiSearch--AdminKey` (spec FR-21), `ai-search-key` (operational), `AzureAISearchApiKey` (referenced by `AiSearch__ApiKeySecretName` and `AiSearch__ReferencesApiKey`). The `AzureAISearchApiKey` reference is currently BROKEN (missing secret). This is a Phase-4 FR-15 design input.
- The live admin key starts with `LUEBuNyEa...`; KV `ai-search-key` starts with `1GpyI95Hi...` (matches the raw key hardcoded in `DocumentIntelligence__AiSearchKey` and `RecordSync__AiSearchApiKey` App Settings). Drift caused by 2026-06-25 service recreate that rotated the admin key.

### Handoff Notes

For the next session: owner should respond to the AI-1 + AI-2 questions above. Once decided, remediation is:

- **AI-1 (Option B example)**: `az keyvault secret set --vault-name spaarke-spekvcert --name AiSearch--AdminKey --value <LIVE>` + `az keyvault secret set --vault-name spaarke-spekvcert --name AzureAISearchApiKey --value <LIVE>` + (optional) `az keyvault secret set --vault-name spaarke-spekvcert --name ai-search-key --value <LIVE>` to refresh the stale operational secret.
- **AI-2 (Option A example)**: `az role assignment create --assignee 9fd47efb-7962-492b-ac44-e5ccd0268ebb --role "Search Index Data Contributor" --scope /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Search/searchServices/spaarke-search-dev`

After remediation: re-run AI-1 + AI-2 checks to verify; update task 001 to ✅; unblock Phase 3 gate.

---

## Quick Reference

### Project Context
- **Project**: spaarke-ai-azure-setup-dev-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Evidence**: [`notes/pre-phase-3-verification.md`](./notes/pre-phase-3-verification.md)

### Knowledge Files Loaded (task 001)
- `projects/spaarke-ai-azure-setup-dev-r1/spec.md` (FR-21 + NFR-13 verbatim commands)
- `projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md` (Redis prereq §9)
- `infrastructure/byok/main.bicep:430-454` (BYOK role-assignment inspection for AI-2)
- `infrastructure/bicep/customer.json` (Service Bus queue names)
- Project CLAUDE.md (operational rules)

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load evidence file**: `notes/pre-phase-3-verification.md` for full FAIL diagnosis + remediation options
4. **Resume**: Wait for owner decisions on AI-1/AI-2; OR start unblocked Phase 1 tasks (002, 004, 005, 007) in parallel

**Commands**:
- `/project-continue` - Full project context reload + master sync
- `/context-handoff` - Save current state before compaction
- "where was I?" - Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
