# Current Task State — Spaarke Platform Foundations (R3)

> **Last Updated**: 2026-06-22 (post-Wave 19 handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-platform-foundations-r3` |
| **Branch** | `work/spaarke-platform-foundations-r3` |
| **Status** | **54 of 69 tasks complete ✅ (~78%) + 1 blocked-operator (071) + 14 operator/human-gated** |
| **Build** | ✅ `dotnet build Spaarke.sln` 0 errors / 17 warnings (no new) |
| **Tests** | ~265+ new unit + integration tests across the project; all green; full BFF suite 7493/7603 pass |
| **BFF publish-size** | 46.21 MB (+0.56 vs 45.65 baseline; 60 MB ceiling intact) |
| **Next Action** | **OPERATOR-GATED**: Deploy Service Bus topic per task 071 runbook (`notes/operator-followup-task071.md`). Then resume Wave 20+ for Phase 2 (tasks 072-087). Until topic deploy, the remaining 14 tasks are blocked. |

### What's Blocked + Why

| Task(s) | Blocker | Unblocks When |
|---|---|---|
| **071** ❌ | Operator must `az deployment group create` for Service Bus topic Bicep | Operator runs the runbook |
| **072, 073** | Depends on 071 (topic deployed) | After 071 |
| **080-087** | Depends on 071 chain (Phase 2 event-publishing + reconciliation) | After 071+072 |
| **100, 104** | Depends on 087 (Phase 2 E2E tests) | After 087 |
| **095** | Manual UAT in spaarkedev1 — requires human | Operator-driven |
| **110** | Final wrap-up — must run after everything else | At project close |

### What Shipped This Session (Waves 10-19; 27 tasks added)

| Wave | Tasks | Commits |
|---|---|---|
| 10 | 024 + 025 + 041 | bb78dc9c9 + 0bb09ecfa |
| 11 | 042 + 043 + 054 | 0bb09ecfa |
| 12 | 050 + 051 + 052 | 4d9a9d5f0 |
| 13 | 053 + 055 | 550b6e147 |
| 14 | 056 + 060 + 090 | 6f86299a8 |
| 15 | 061 + 065 + 103 | 76d809e0a |
| 16 | 091 + 092 + 093 | 185dac34a |
| 17 | 062 + 094 | 6c7a80507 |
| 18 | 063 + 064 + 066 | (Wave 18 commit) |
| 19 | 102 + 101 | d6ad6e65d |

### Critical Findings (preserve for R4 / operator)

1. **Latent A1-class defect** (tasks 051+052): Two existing playbooks had **no membership filter at all** — would have returned tenant-wide records. Both migrated. R4 should audit ALL solution-exported playbooks for this class of issue.

2. **Zero in-repo readers of `sprk_searchindexed`** (task 060 inventory): All readers live in Dataverse maker-side artifacts (forms/views/flows). Tasks 063 + 064 closed as verify-empty + escalated maker-side audit to operator follow-up. Document the maker-side cleanup before removing the legacy field in a future R-iteration.

3. **Pre-existing G6 drift defect caught by task 065** (CanvasServerMappingDriftTests): `createNotification` canvas type was missing server arms — fixed in-scope. Drift test is now the binding regression guard.

4. **Task 062 re-targeted per 060/061 inventory**: POML named `DeliverToIndexNodeExecutor.cs` but writes actually live in `RagIndexingJobHandler.cs` + `RagEndpoints.cs` + `Spaarke.Dataverse` mapping layer. Implementation matches inventory finding.

5. **Task 071 Bicep** authored + `az bicep build` clean; deferred to operator deploy per `projects/spaarke-platform-foundations-r3/notes/operator-followup-task071.md`. **THIS IS THE PROJECT'S CRITICAL PATH** — until this deploys, 14 tasks remain blocked.

---

## Status Counts

✅ **54 complete** across 19 waves (10-19)
❌ **1 blocked-operator**: 071 (Service Bus topic deploy)
🔲 **14 pending** (all gated by 071 directly or transitively): 072, 073, 080-087, 095, 100, 104, 110

---

## Resumption Protocol — When Operator Deploys Task 071

1. Operator runs `infrastructure/bicep/modules/membership-topic.bicep` deployment per `notes/operator-followup-task071.md`
2. Operator confirms topic + subscription provisioned + BFF MI has Sender/Receiver RBAC
3. Mark task 071 ✅ in TASK-INDEX (was `❌ blocked-operator`)
4. User runs `/continue` or "next task" — autonomous waves resume:
   - **Wave 20**: 072 + 073 (event payload contract + topic smoke test)
   - **Wave 21** (main-session): 080 (event-source endpoint inventory — discovery, main-only)
   - **Wave 22**: 081 + 082 + 083 (matter / document+event / task+opportunity event-publishing clusters)
   - **Wave 23**: 084 + 085 + 086 (junction updater + recon real logic + Redis pub/sub invalidation)
   - **Wave 24**: 087 (Phase 2 E2E integration tests) + 095 (manual UAT — human)
   - **Wave 25** (main-session): 100 + 104 (final ADRs + architecture doc)
   - **Wave 26**: 110 (project wrap-up + lessons-learned)

### How to Construct Wave 20+ Agent Prompts

Prior wave dispatches in this session are the canonical templates. Each self-contained brief must include:
- POML path
- Context read order (CLAUDE.md → project CLAUDE.md → spec → POML → constraints → patterns → reference impls)
- Owner clarifications to honor (Q1-Q6, D2, D3)
- Coordination notes for siblings in same wave
- Hard constraints (no .claude/ writes for sub-agents; no --no-verify; TreatWarningsAsErrors)
- Report format (200-350 word cap; STATUS/FILES/TESTS/BUILD/PUBLISH/CVE/TASK-INDEX/NOTES)

---

## Owner Clarifications (binding for all agent briefs — unchanged)

| # | Decision | Status |
|---|---|---|
| Phase 1D | In-scope (transitive memberships) | ✅ shipped (task 054) |
| AC-1A.5 | App Insights server-side telemetry | Production-side |
| H3 inventory | P7.0 task 060 first | ✅ shipped (zero in-repo readers) |
| Phase 2 | In-scope (junction + topic + recon) | Tasks 070-087 — blocked on 071 |
| D3 | Service Bus **topic** | Bicep authored, deploy gated |
| Q1 | Fresh correlationId per child | ✅ shipped (PlaybookSchedulerJob) |
| Q2 | Fire-and-forget + nightly recon | Pending task 085 |
| Q3 | `includeRelated` 1 hop max | ✅ shipped (MembershipDepthExceededException) |
| Q4 | sprk_assignedlawfirm → sprk_organization | ✅ shipped (tasks 032 + 103) |
| Q5 | Extend existing PlaybookBuilder | ✅ shipped (tasks 091/092/093) |
| Q6 | Existing `SystemAdmin` policy | ✅ shipped (all admin endpoints) |
| task-032 | Option (b) config-driven Lookup | ✅ shipped |

---

## Recovery After Compaction — TL;DR

1. Read this file's Quick Recovery section
2. `dotnet build Spaarke.sln` (expect 0 errors)
3. `git status` (expect clean or just husky hook drift)
4. Verify task 071 status — if still `❌ blocked-operator`, no autonomous waves available; await operator deploy
5. If 071 ✅, dispatch Wave 20 = 072 + 073 in parallel

---

*Initialized 2026-06-20 by `/project-pipeline`. Last updated 2026-06-22 post-Wave 19 (54/69 complete). Branch state: pushed to origin. Project at ~78% — final 22% gated by operator deploy of task 071 Service Bus topic.*
