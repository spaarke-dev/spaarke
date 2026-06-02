# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Milestone snapshot 2026-06-02 — Wave A complete, all 6 design docs landed.

---

## ✅ Closed — Task 020 (Wave C1) — COMPLETE 2026-06-02

**Status**: ALL CODE WORK DONE. Build clean (0 errors). 82 tests pass (11 new + 63 regression + 8 EvidenceSufficiency). Code-review + adr-check pass (0 critical, 0 violations). Deploy + smoke DEFERRED per bff-extensions §F.4 (owner coordination required).

**Deliverables landed in worktree** (NOT YET committed):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/universal-ingest.playbook.json` (NEW, 6-node + parameterSchema)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EvidenceSufficiencyNode.cs` (PATCH Gap #1, +89 lines)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` (PATCH Gap #2, +80 lines)
- `src/server/api/Sprk.Bff.Api/Services/Ai/NodeExecutionContext.cs` (+Parameters property, +10 lines)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs` (+1 line — wire Parameters)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` (+ActionType.Sanitization=130, ObservationEmit=140)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Nodes/SanitizerNodeExecutor.cs` (NEW, 216 lines)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Nodes/ObservationEmitterNodeExecutor.cs` (NEW, 312 lines)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/InsightsIngestModule.cs` (+25 lines — register both executors w/ ADR-030 §F.1 inspection)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/UniversalIngestPlaybookTests.cs` (NEW, 11 tests)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/InsightsNodeTestHelpers.cs` (+Parameters/nodeName overloads, +6 lines)
- `projects/ai-spaarke-insights-engine-r2/tasks/020-universal-ingest-jps-playbook.poml` (status=completed + 7-section <notes> block)
- `projects/ai-spaarke-insights-engine-r2/tasks/TASK-INDEX.md` (row 020 🔲 → ✅)

**Owner action required (per bff-extensions §F.4)**: Coordinate deploy window with parallel projects; then run Deploy-BffApi.ps1 → 4 sprk_analysisaction row creates → Deploy-Playbook.ps1 → smoke. See `tasks/020-universal-ingest-jps-playbook.poml` `<deferred-deploy>` block for full sequence.

**Resume command (NEXT TASK)**: After `/commit` + `/push-to-github`, say "continue" or "work on task 021" — task-execute picks up Wave C2 (C2 task 021 — Migrate prompts from .txt → sprk_analysisaction.sprk_systemprompt).

### Task 020 Quick Recovery

| Field | Value |
|---|---|
| **Task** | 020 — C1 Author universal-ingest@v1 JPS playbook |
| **POML** | `projects/ai-spaarke-insights-engine-r2/tasks/020-universal-ingest-jps-playbook.poml` |
| **Rigor Level** | FULL (BFF code changes + deploy + 8 steps + tags include bff-api/dataverse/deploy) |
| **Estimated effort** | 2d |
| **Wave-item** | C-G2 (serial; gates 021/022/023/024) |
| **Dependencies** | 013 ✅, 014 ✅ |
| **Started** | 2026-06-02 (paused before Step 4) |
| **Next Action** | After `/compact`: invoke task-execute on `tasks/020-universal-ingest-jps-playbook.poml`; protocol picks up at Step 4 (Load Knowledge Files) |

### What's already loaded into Wave A artifacts (read these FIRST when resuming task 020)

1. **`design-a5-universal-ingest-jps.md`** §3 (6-node coalescence: sanitize → layer1Classify → checkLayer2Gate → layer2Extract → groundingVerify → emitObservations)
2. **`design-a5-universal-ingest-jps.md`** §4 per-node design (action codes: INS-SANI, INS-L1C, INS-EVID, INS-L2X, INS-GRND, INS-OBSE)
3. **`design-a5-universal-ingest-jps.md`** §6 parameterization schema (9 properties; 3 required, 6 optional)
4. **`design-a5-universal-ingest-jps.md`** §7 + **`notes/spikes/engine-gap-analysis.md`** — **2 PlaybookExecutionEngine patches MUST be applied before smoke**:
   - Gap #1: `EvidenceSufficiencyNode` lacks `predicate:"in"` membership rule (~15–20 LOC)
   - Gap #2: `PlaybookOrchestrationService.ExecuteNodeAsync` `dependsOn` is AND-only; needs branch-aware skip via `selectedBranch` + per-edge `branch` label (~25–40 LOC)
5. **`design-a4-prompt-variants.md`** §5 — CTRNS Layer 1 worked example; informs Wave C2 row renaming (NOT C1 scope)
6. **`design-a4-prompt-variants.md`** §6 — rollback story (minutes; playbook JSON edit only)

### Task 020 — 8 implementation steps from POML

1. Load A5 + A4 designs (DONE — Wave A landed them)
2. Create 4 NEW `sprk_analysisaction` rows via **`/jps-action-create` skill** (per owner direction): `INS-SANI`, `INS-L1C`, `INS-L2X`, `INS-OBSE`. Reuse `INS-EVID` + `INS-GRND` from Wave B (already in Dataverse).
3. Author `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/universal-ingest.playbook.json` (6-node + parameterSchema + Layer 2 conditional branch)
4. **APPLY 2 ENGINE PATCHES** (per engine-gap-analysis.md) — these are the highest-risk code changes in this task
5. Deploy via `scripts/Deploy-Playbook.ps1` (action-code lint must pass first)
6. Smoke test (fixture document; verify 6 nodes execute end-to-end)
7. Behavior parity check vs r1 `IngestOrchestrator.cs` (document differences as intentional)
8. Quality gates (Step 9.5): code-review + adr-check + `dotnet build --warnaserror`

### Files task 020 will modify (preview from POML + design-a5)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/universal-ingest.playbook.json` (NEW)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Nodes/EvidenceSufficiencyNode.cs` (PATCH #1)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` (PATCH #2)
- Possibly `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Graph/ExecutionGraph.cs` (per-edge `branch` label storage for Gap #2)
- 2 NEW node executors per design-a5: `SanitizerNodeExecutor.cs`, `ObservationEmitterNodeExecutor.cs`
- `tests/integration/Services/Ai/Insights/UniversalIngestPlaybookTests.cs` (NEW)
- 4 new Dataverse rows: `INS-SANI`, `INS-L1C`, `INS-L2X`, `INS-OBSE` in `sprk_analysisaction`
- Updated `tasks/020-universal-ingest-jps-playbook.poml` + `TASK-INDEX.md` row 020

### Deploy coordination (bff-extensions §F.4)

Step 5 deploy hits `spaarke-bff-dev` shared with other projects. Per §F.4, announce intent before deploying. The deploy itself MUST use the hardened `Deploy-BffApi.ps1` with SHA-256 hash verification (silent-failure guard documented in `.claude/skills/bff-deploy/SKILL.md`).

---

## ✅ Closed — Wave A complete (foundations) — 2026-06-02

**All 6 Wave A foundation design docs landed in a single parallel dispatch (6 sub-agents, ONE message).** Zero `.claude/` write boundary hits; zero retries needed. **Committed locally at `3215069c`; not yet pushed to origin.**

### Status

| Dimension | State |
|---|---|
| **Wave B** (Unblock synthesis) | ✅ COMPLETE on master (PR #330, 2026-06-02 16:57 UTC) |
| **Wave A** (Foundations) | ✅ COMPLETE — all 6 tasks closed via parallel dispatch 2026-06-02 |
| **Worktree branch** | ✅ Synced with master; Wave A staged (10 modified, 4 untracked design docs, 1 untracked notes dir) |
| **Wave A sequencing** | Single-shot parallel — 6 sub-agents via Agent tool, ONE message, all completed independently with no inter-agent collisions |
| **Permission boundary** | ✅ Zero `.claude/` writes attempted by sub-agents — design-doc tasks scoped entirely under `projects/ai-spaarke-insights-engine-r2/` + `docs/architecture/` + `docs/guides/` |

### Wave A closeout artifacts (this commit)

| Path | What |
|---|---|
| `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` | UPDATED (A1/010) — §0a Terminology block + Phase 1.5 wave-status table + practice-area-from-ref-table anchor |
| `docs/guides/INSIGHTS-ENGINE-GUIDE.md` | UPDATED (A2/011) — §§4/5/6/7/7A/7B for practice areas, multi-entity, JPS prompt editing, /api/insights/search, intent classifier, Assistant integration |
| `design-a3-2d-taxonomy.md` | NEW (A3/012) — 2D taxonomy design (practice-area × document-type), N:N intersect entity, NULL `sprk_layer2actioncode` = structured gate-fail, 5 high-value pairs recommended |
| `design-a4-prompt-variants.md` | NEW (A4/013) — Hybrid variant pattern + `@vN` versioning + tenant-scoped variant rows; PR-1 invariant preserved |
| `design-a5-universal-ingest-jps.md` | NEW (A5/014) — 6-node universal-ingest playbook + parameterization schema + 2 PlaybookExecutionEngine gap patches surfaced for Wave C1 |
| `design-a6-multi-entity.md` | NEW (A6/015) — Config-catalog subject parser + `IDictionary<string, ILiveFactResolver>` registry + hybrid scope shape for spaarke-insights-index-v2 (NFR-08 back-compat) |
| `notes/spikes/engine-gap-analysis.md` | NEW (A5 supplement) — Ephemeral Wave C1 backlog with the 2 engine patches |
| `spec.md` | UPDATED (A3) — PA-2 added (CTRNS/IPPAT/BNKF), Q-D2-1 resolved |
| 6× task POMLs | UPDATED — status → completed, `<notes>` blocks added with decisions and downstream impact |
| `tasks/TASK-INDEX.md` | UPDATED — rows 010–015 marked ✅ |

### Open follow-ups (carried into Wave C/D — NOT blockers)

| ID | Item | Owner / Wave |
|---|---|---|
| **PA-2-Q1..Q4** | Owner sign-off on initial 3 practice areas (CTRNS / IPPAT / BNKF) — pending before Wave D2 (031) row creation | Owner / Wave D2 prerequisite |
| **Engine Gap #1** | `EvidenceSufficiencyNode` `predicate: "in"` membership rule (~15–20 LOC patch) | Wave C1 (020) implementor |
| **Engine Gap #2** | `PlaybookOrchestrationService.ExecuteNodeAsync` branch-aware skip (~25–40 LOC patch) | Wave C1 (020) implementor |
| **C2 scope clarification** | Wave C2 must RENAME existing 8 Wave B2 `INS-*` rows to `@v1` suffix AND create new `INS-L1-CLASS@v1` for universal-ingest Layer 1 | Wave C2 (021) implementor |
| **Q-A4-1** | Variant pattern choice (parametric default vs. variant-row escape hatch) per task; A4 design supports either — defer per-Insights-action | Wave C2 (021) per-row decision |

### Project state

| Wave | Status |
|---|---|
| **B** (Unblock synthesis) | ✅ COMPLETE — on master |
| **A** (Foundations) | ✅ COMPLETE — all 6 design docs landed via parallel dispatch 2026-06-02 |
| **C** (JPS compliance) | 🔲 NEXT — 5 tasks (020–024); C-G2 (020 serial, ~2d) is the critical-path entry; depends on A4 (013) + A5 (014) — both ✅ |
| **D** (2D taxonomy + multi-entity) | 🔲 — Depends on C + A3 (012 ✅) + A6 (015 ✅). PA-2 owner sign-off needed before D2 (031) |
| **E** (Hybrid + Assistant) | 🔲 — Depends on D6 (035) |
| Wrap-up | 🔲 — After E |

### How to resume in the next session

1. Read this file for milestone state above.
2. Wave A is done. Wave C is next — start with task 020 (C-G2 critical-path; C1 = author universal-ingest@v1 JPS playbook).
3. **Before Wave C1 starts**: implementor MUST read `design-a5-universal-ingest-jps.md` §3 (6-node coalescence), §6 (parameterization), §7 (engine gaps), and `notes/spikes/engine-gap-analysis.md`.
4. **Before Wave D2 starts**: owner sign-off on PA-2 (CTRNS / IPPAT / BNKF as the initial 3) — see `design-a3-2d-taxonomy.md` §8.

---

## Wave sequencing (per owner direction WB-1)

Wave B FIRST ✅ → A ✅ → C 🔲 → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | ✅ COMPLETE |
| **A** (Foundations) | 010–015 | ✅ COMPLETE |
| **C** (JPS compliance) | 020–024 | 🔲 NEXT |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 |
| Wrap-up | 090 | 🔲 |

---

*Compacted 2026-06-02 — milestone: Wave A complete via parallel dispatch (6 sub-agents, ONE message, zero retries). Resume with Wave C1 (task 020).*
