# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Lifecycle**: Milestone snapshot 2026-06-03 — Wave D shipped + Wave E next.

---

## 🎯 Milestone — 2026-06-03 — Wave D shipped (PR #336); Wave E next

### Project status

| Wave | Status | PR | Notes |
|---|---|---|---|
| **B** Unblock synthesis | ✅ Master | #330 | predict-matter-cost@v1 dispatch working live |
| **A** Foundations | ✅ Master | #334 | 6 design docs (a3, a4, a5, a6 + 2 refreshes) |
| **C** JPS compliance | ✅ Master | #334 | universal-ingest@v1 + IInsightsAi rewire + legacy retired |
| **D** 2D taxonomy + multi-entity | 🔄 **PR #336 auto-merging** | #336 | All 7 tasks done; CI re-running after whitespace fix |
| **E** Hybrid + Assistant | 🔲 NEXT | — | 4 tasks (040, 041, 042, 043) |
| Wrap-up | 🔲 Final | — | Task 090 |

### How to resume in the next session

1. Read this file for the milestone state above.
2. Check PR #336 status: `gh pr view 336 --json state,mergedAt`. If MERGED, master has Wave D. If still OPEN, Code Quality re-run hadn't finished — wait or check `gh pr checks 336`.
3. To start Wave E: say **"start Wave E"** — I'll dispatch tasks 040 + 043 in parallel (both have all deps met) per the actual dependency graph (NOT the orchestration group boundaries — see `feedback-actual-deps-not-orchestration-groups` memory).

---

## 🔄 Active PR — #336 (Wave D)

- **Branch**: `work/ai-spaarke-insights-engine-r2-wave-d`
- **HEAD**: `597b1267` (whitespace fix on top of `9190fd93` Wave D-G3+D7)
- **Auto-merge enabled**: MERGE strategy, will fire when CI passes
- **Code Quality issue resolved**: 4 trivial whitespace errors auto-fixed via `dotnet format whitespace Spaarke.sln`
- **CI re-running** as of 2026-06-03 ~21:30 UTC

### PR #336 contents (5 commits)

| Commit | What |
|---|---|
| `0d8f5133` | Cleanup: ADR-030 → ADR-032 renumber + predict-matter-cost.playbook.json `$ref` cleanup |
| `a3e9b01a` | D1 (030) schema: sprk_documenttype_ref + N:N intersect + 12 seed rows + deploy script |
| `ff2fc13b` | D-G2 cluster (031 + 032 + 034 + 035): per-area L1 prompts + per-(area,type) L2 schemas + multi-entity resolvers + index scope migration |
| `9190fd93` | D-G3 + D7 (033 + 036): universal-ingest per-area routing + 19 synthetic test fixtures |
| `597b1267` | Whitespace fix (Code Quality gate) |

---

## 🎯 Action items (post-PR-#336 merge)

### HIGH — Deploy coordination per bff-extensions §F.4

**Critical context**: After Wave C-G4 (already on master via PR #334) + Wave D (PR #336), the BFF has **NO fallback path** for ingest. Misconfigured deploys fail loudly per ADR-004 dead-letter semantics. Bundle Wave C + D deploy atomically.

**Deploy sequence (owner-coordinated)**:

1. **Announce deploy window** in team channel per §F.4 (shared `spaarke-bff-dev` with parallel projects)
2. **`az deployment group create -f infra/insights/main.bicep -p infra/insights/parameters/dev.json -g spe-infrastructure-westus2`** — adds `scope` ComplexType to `spaarke-insights-index` (idempotent PUT)
3. **(Skipped on dev — no Phase 1 data)** `scripts/Migrate-InsightsIndexScopeShape.ps1 -SearchServiceName spaarke-search-dev -DryRun` then drop `-DryRun`
4. **Set App Service config keys**:
   - `Insights:Playbooks:Map:universal_ingest_v1` = Dataverse Guid of universal-ingest@v1 playbook
   - `Insights:Index:DualWriteScopeMatterId` = `true` (default, but explicit for clarity)
5. **`Deploy-BffApi.ps1`** to spaarke-bff-dev (silent-failure SHA-256 guard per `.claude/skills/bff-deploy/SKILL.md`)
6. **`Deploy-Playbook.ps1`** with `universal-ingest.playbook.json` to Spaarke Dev
7. **Live smoke per the 19 fixtures**:
   - Each (area, type) pair fixture → verify per-area Layer 1 hits correct prompt + Layer 2 extraction matches design-a3 §5.2 field sets
   - CTRNS × NDA fixture → verify Layer-1-only Observation (gate-fail), Layer 2 SKIPPED via Wave C1 Gap #2 reuse
   - `_uncovered/ippat-trade-secrets-policy-1.txt` → verify generic INS-L1C@v1 fallback fires

### MEDIUM — Carry-forward decisions

- **PA-2 confirmation**: I assumed CTRNS / IPPAT / BNKF per design-a3 §8 recommendation. Owner can override if different initial 3 desired — would require re-running Wave D-G2 with new areas.
- **D5-Q2**: `sprk_invoice` predicate attribute names — empirical confirmation against Spaarke Dev schema. D5-Q1 (`sprk_project` primary name) was resolved during Wave D7 to `sprk_projectname`. D5-Q2 is similar work.

### LOW — Open follow-ups (NOT blocking Wave E)

- Cache invalidation strategy for `InsightsActionRouter` if matrix changes mid-soak (15-min sliding TTL is fine for Phase 1.5)
- Telemetry event `InsightsActionLookupFailed` for fallback-path observability (router currently logs warnings only)
- Multi-area document handling (Phase 2 — out of 1.5 scope; current design assumes 1 area per matter via `sprk_practicearea`)
- `classification` → `document_type_code` Debug logging when Wave C1 fallback fires in `ExtractDocumentTypeFromLayer1Output`
- Per-area smoke against 3 real Spaarke Dev matters (post-deploy)
- `predict-matter-cost.playbook.json` `$ref` cleanup left as `$comment-prompt-source` documentation only — runtime path uses INS-AGNT@v1.sprk_systemprompt (Wave C2 migration)

---

## 🔲 Wave E preview (when you say "start Wave E")

| Task | Wave-item | Effort | Description | Dependencies |
|---|---|---|---|---|
| 040 | E1 | 1.5d | `POST /api/insights/search` (wraps existing `IRagService`) | 035 ✅ |
| 041 | E2 | 2d | LLM-based intent classifier | 040 |
| 042 | E3 | 1w | Spaarke Assistant integration (contract first; long-running) | 040, 041 |
| 043 | E4 | 4h | Playbook-vs-RAG decision-tree doc | 040, 041 |

**Parallel dispatch plan (per dependency graph, NOT orchestration group label)**:
- **Round 1**: 040 (E1) serial — gates everything else
- **Round 2**: 041 + 043 in parallel (both depend on 040)
- **Round 3**: 042 (E3) serial — long-running with cross-team coordination

Critical path: 040 → 041 → 042 ≈ 1.5d + 2d + 1w ≈ ~10 days.

After Wave E: **090 wrap-up** (4h — lessons-learned + Phase 2 outline + archive).

---

## Key load-bearing technical anchors (survive across sessions)

### Dataverse rows in Spaarke Dev

- **11 base Insights action rows** (Wave B + C): INS-FACT@v1, INS-IDXR@v1, INS-EVID@v1, INS-GRND@v1, INS-DECL@v1, INS-RART@v1, INS-AGNT@v1, INS-SANI@v1, INS-L1C@v1, INS-L2X@v1, INS-OBSE@v1
- **3 per-area Layer 1 rows** (Wave D2): INS-L1C-CTRNS@v1, INS-L1C-IPPAT@v1, INS-L1C-BNKF@v1
- **5 per-(area, type) Layer 2 rows** (Wave D3): INS-L2X-CTRNS-CLOSING@v1, INS-L2X-CTRNS-APA@v1, INS-L2X-IPPAT-PATAPP@v1, INS-L2X-IPPAT-OA@v1, INS-L2X-BNKF-LOAN@v1
- **6 sprk_documenttype_ref seed rows** (Wave D1): CTRNS_CLOSING_STATEMENT, CTRNS_ASSET_PURCHASE_AGREEMENT, IPPAT_PATENT_APPLICATION, IPPAT_OFFICE_ACTION, BNKF_LOAN_AGREEMENT, CTRNS_NDA
- **6 sprk_practicearea_documenttype intersect rows** (Wave D1): 5 high-value pairs with sprk_layer2actioncode populated by Wave D3 + CTRNS × NDA with sprk_layer2actioncode=NULL (structured gate-fail by design)
- **Synthetic test entities** (Wave D7): Matter `da116923-d65a-f111-a825-3833c5d9bcb1` (re-used Wave B5 CTRNS), Project `27845394-8e5f-f111-a825-70a8a59455f4`, Invoice `05c8ef8d-8e5f-f111-a825-70a8a59455f4`

### Schema state (Spaarke Dev)

- `sprk_actioncode` MaxLength = 64 (post schema bump)
- `sprk_documenttype_ref` entity exists (PrimaryName = `sprk_documenttypename`)
- `sprk_practicearea_documenttype` N:N intersect entity exists (5 scalar cols + 2 lookups)
- `sprk_executoractiontype` field on `sprk_analysisactiontype` (from Wave B)

### Spaarke Dev environment

- BFF App Service: `spaarke-bff-dev`
- Dataverse: `spaarkedev1.crm.dynamics.com` (NOT spaarke-dev as some docs imply)
- AI Search: `spaarke-search-dev`
- Resource group: `rg-spaarke-dev` (BFF), `spe-infrastructure-westus2` (search)

### Key code paths

- `IInsightsAi.RunIngestAsync` → `PlaybookOrchestrationService.RunAsync("universal-ingest@v1", ...)` → 6-node playbook
- `PlaybookOrchestrationService.ExecuteNodeAsync` has Wave C1 Gap #2 branch-aware skip (used by Wave D-G3 task 033 for NULL gate-fail)
- `InsightsActionRouter` (Wave D4 task 033) — Layer 1 area routing + Layer 2 (area, type) lookup with `IMemoryCache` 15-min sliding TTL
- 3 `ILiveFactResolver` impls in `IDictionary<string, ILiveFactResolver>` registry (Wave D5 task 034): Matter, Project, Invoice

---

## Session lessons captured (in memory for future sessions)

Saved in `~/.claude/projects/.../memory/`:

- `feedback-parallel-execution` — TASK-INDEX parallel groups = dispatch without asking
- `feedback-actual-deps-not-orchestration-groups` — Use actual deps, not group labels (this lesson cost ~30 min of critical path in Wave D)
- `feedback-detect-stuck-subagents` — `ls -la` output file when sub-agent runs long; 0 bytes = stuck → kill + tighter re-dispatch (this lesson cost **12 hours** in Wave D on task 032; do NOT repeat)
- `reference-dataverse-schema-mutation` — MCP `update_table` only adds columns; column MaxLength/type changes need Web API PUT
- `reference-spaarke-ci-format-gate` — `dotnet format whitespace Spaarke.sln --verify-no-changes` before push to avoid Code Quality round-trip
- `spaarke-unmanaged-solutions` — Spaarke uses unmanaged solutions everywhere (ADR-027 amended)
- `insights-engine-r2-wave-b-complete` — Wave B baseline state

---

*Compacted 2026-06-03 — milestone: Wave D shipped in PR #336 (auto-merging); Wave E (4 tasks) is next phase. Use the action items + Wave E plan above to resume.*
