# Current Task — Spaarke AI Platform Unification R5

> **Purpose**: Active task state tracker. Managed by `task-execute` skill per CLAUDE.md §7.
> **Status**: ⏸️ CHECKPOINT — awaiting PR #345 merge + CI/CD deploy + SME walkthrough
> **Last updated**: 2026-06-04 (PR opened)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **State** | ⏸️ PAUSED — Phase 2 code-complete + PR open. Awaiting external steps (CI + merge + deploy + SME walkthrough). |
| **PR** | **#345**: https://github.com/spaarke-dev/spaarke/pull/345 — `work/spaarke-ai-platform-unification-r5` → `master` |
| **Pipeline status** | ✅ Phase 1 (9/9); ✅ Phase 2 code (20/22 ✅ + 2 partial); ⏸️ Phase 3 (waiting on Phase 2 final close) |
| **P2 gate (code-side)** | GREEN. 6198/6198 BFF tests pass, 45 MB publish (well under 60 MB ceiling), no new CVEs, zero new Program.cs lines, ADR compliance matrix all green (17 ADRs). Evidence: `notes/task-031-phase-2-closeout.md` |
| **Branch head** | `bffa767c` (31 commits ahead of `origin/master`) |
| **Status** | ⏸️ checkpoint — operator handles PR review/merge + post-deploy SME walkthrough at convenience |

### Resume protocol (when ready to continue)

When PR #345 has been merged + CI/CD has deployed to Spaarke Dev:

1. **Pull master** into a fresh workspace (or rebase this branch — but probably easier to start fresh from master since R5 work is now in master)
2. **Schedule + run task 030 SME walkthrough** (~45 min operator + 1 SME):
   - Walkthrough script: [`projects/spaarke-ai-platform-unification-r5/notes/task-030-sme-walkthrough.md`](notes/task-030-sme-walkthrough.md) (template ready; fill in signoff)
   - 15-question matrix: [`tests/integration/Spe.Integration.Tests/fixtures/insights-smoke-matrix.json`](../../tests/integration/Spe.Integration.Tests/fixtures/insights-smoke-matrix.json)
   - Synthetic test entities (Wave D7 GUIDs):
     - Matter: `da116923-d65a-f111-a825-3833c5d9bcb1`
     - Project: `27845394-8e5f-f111-a825-70a8a59455f4`
     - Invoice: `05c8ef8d-8e5f-f111-a825-70a8a59455f4`
3. **Flip task 030 status to ✅** in `tasks/TASK-INDEX.md` once SME signoff captured
4. **Flip task 031 status to ✅** (final P2 closure)
5. **Start Phase 3** — Wave 8 from project-pipeline plan:
   - 040 D3-01 `/analyze` proof point (validates SC-19 platform-extension claim; ≤1 day budget per spec)
   - 041 D3-02 Get Started welcome card "Summarize a Document"
   - 042 D3-03 Telemetry dashboards (App Insights / Kusto queries consuming task 008's `r5.summarize.invocation` event schema)
   - 043 D3-04 Operator-led end-to-end testing (7 surfaces from kickoff doc)
   - 044 D3-05 Lessons-learned + R6 backlog
6. **Wrap-up task 090** — README → Complete; coordination doc §8 entry; final R5 merge ceremony

### Pre-resume verification

When resuming, verify Spaarke Dev BFF actually has the R5 endpoints:

```bash
# Should return HTTP 401 (auth required) NOT 404 (not deployed)
curl -sS -w "HTTP=%{http_code}\n" -X POST "https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions/00000000-0000-0000-0000-000000000000/summarize"

# Should return HTTP 401 (auth required) NOT 404
curl -sS -w "HTTP=%{http_code}\n" -X POST "https://spaarke-bff-dev.azurewebsites.net/api/insights/assistant/query"
```

If both return 404, the deploy didn't happen — investigate CI/CD before proceeding to walkthrough.

---

## Phase 2 final commits (this session)

| Commit | Description |
|---|---|
| `79970ffb` | task 001 — `spaarke-session-files` AI Search index provisioned on Spaarke Dev |
| `84b26f6f` | Wave P1-G2/G3 — RAG sessionId routing + indexing + manifests + SSE FieldDelta (tasks 002-005) |
| `da78b081` | Wave P1-G4/G5 — Structured Outputs streaming + cleanup job + telemetry (tasks 006-008) |
| `4a459381` | task 009 Phase 1 GATE GREEN — closeout verification |
| `be68e70a` | Phase 2 Wave A — Summarize action+playbook deploy + frontend renderer/events/slash (tasks 010, 011, 013, 016, 019) |
| `12cdb447` | Phase 2 Wave B — orchestrator + Workspace widget + Context widget + DocumentViewer upgrade (tasks 012, 017, 018, 022) |
| `0a252355` | docs: chat-agent parallel-build audit (Insights r3 heads-up response) + index rename note |
| `2f9107f6` | Phase 2 Wave C — direct endpoint + agent-tool registration (tasks 014, 015) |
| `48625802` | Phase 2 Wave D — chat-pane orchestration UX + per-file affordance (tasks 020, 021) |
| `477df485` | Phase 2 Wave E1 — Insights tool integration foundation (tasks 023+024+025) |
| `5d48d586` | Phase 2 Wave E2+E3 — Insights response renderer + citations + confidence badge (tasks 026, 027, 028) |
| `65e40629` | Phase 2 Wave E4 — Insights 12 error codes + retry policy + correlation propagation (task 029) |
| `ee620871` | Phase 2 Wave E5 — Insights smoke test scaffold + 15-question matrix (task 030 partial) |
| `bffa767c` | P2 code-side gate GREEN (task 031 partial) |

## Phase 2 by the numbers

| Metric | Value |
|---|---|
| Tasks complete | 20 of 22 ✅ + 1 partial (030 scaffold) + 1 partial (031 code-side) |
| Phase 2 commits | 9 (be68e70a → bffa767c) |
| Total R5 commits | 31 |
| Files created (R5) | ~50+ (.cs + .tsx + .ts + .json + .ps1 + tests + evidence) |
| Files modified (R5) | ~25 |
| Tests added (R5) | +97 BFF unit + ~250+ frontend jest + 15 integration scaffold |
| Sub-agent waves (implementation) | 6 (Wave A → E5) |
| Sub-agents dispatched (implementation) | ~25 |
| Real deploys | 1 AI Search index + 2 Dataverse rows + 1 OpenAI spike call |

## Open / forwarded items

1. **Task 030 SME walkthrough** — operator handles post-deploy (~45 min)
2. **Task 031 final closure** — auto-flips when 030 closes
3. **Insights r3 F-4 backfill**: include `summarize-document-for-chat@v1` in `playbook-embeddings` indexing (forwarded via `notes/insights-r2-coordination.md` §8)
4. **Insights r3 Tier 1.5 wave 1**: `playbook-embeddings` → `spaarke-playbook-index` rename (R5 has zero direct refs)
5. **Capability manifest update** (data-seed concern): add `invoke_summarize_playbook` + `insights.query` to `summarize` / `insights` capability manifest `ToolNames` allow-list for AIPU2-061 narrow routing. Currently falls back to Layer 3 (full capability set returned). R6.
6. **Kiota.Abstractions HIGH CVE**: separate ticket per BFF module CLAUDE.md "Package Management" — not introduced by R5; pre-existing on master.

---

## Reference materials

- **PR**: https://github.com/spaarke-dev/spaarke/pull/345
- **Spec**: `projects/spaarke-ai-platform-unification-r5/spec.md`
- **Design**: `projects/spaarke-ai-platform-unification-r5/design.md`
- **Plan**: `projects/spaarke-ai-platform-unification-r5/plan.md`
- **CLAUDE**: `projects/spaarke-ai-platform-unification-r5/CLAUDE.md`
- **TASK-INDEX**: `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md`
- **P2 closeout**: `projects/spaarke-ai-platform-unification-r5/notes/task-031-phase-2-closeout.md`
- **Audit (Insights r3 response)**: `projects/spaarke-ai-platform-unification-r5/notes/r5-chat-agent-parallel-build-audit.md`
- **Insights coordination**: `projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md`
- **SME walkthrough template**: `projects/spaarke-ai-platform-unification-r5/notes/task-030-sme-walkthrough.md`
- **Smoke evidence template**: `projects/spaarke-ai-platform-unification-r5/notes/task-030-smoke-evidence.md`
- **Integration test matrix**: `tests/integration/Spe.Integration.Tests/fixtures/insights-smoke-matrix.json`

---

*Checkpoint authored 2026-06-04. Resume when PR #345 merges + CI/CD deploys + SME is scheduled.*
