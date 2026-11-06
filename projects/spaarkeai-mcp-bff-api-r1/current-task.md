# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-06 (by project-setup, pipeline Step 2 Part 2)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Project pipeline (Step 3: task-create next) |
| **Step** | Step 2 Part 2 done: README.md, plan.md, CLAUDE.md, notes/decisions.md and folders generated; spec.md housekeeping applied |
| **Status** | in-progress |
| **Next Action** | Run task-create for projects/spaarkeai-mcp-bff-api-r1 (Step 3), then Completeness Lint |

### Critical Context
- Owner chose (2026-11-06) to merge the held ontology PRs into this branch: #2580 `9423f37004` (merge `489f6508fc`) and #2579 `ab04492ab7` (merge `026b61554a`). The BFF builds with 0 warnings and 0 errors. Facts about their code: `notes/pre-pipeline-context.md`.
- **STANDING (ontology condition):**
  - NO dev BFF deploy from this branch until ontology's #2580/#2579 pair has deployed (it waits for ebilling task 112).
  - Phase 0 live tests run from a separate spike branch cut from master. Deploys only with the owner's per-SHA go in uac-r2's session.
  - Our master PR merges after their pair. Re-merge if their branches move (they merge, never rebase).
- The D-207 order and replay guard are unchanged. The MCP commit goes only through `DecisionCommitService`.
- Generated artifacts are uncommitted until pipeline Step 4 (commit, INDEX row, push, register under Epic #421).

---

## Open Owner Questions / Blockers
- Billing review in Copilot (next slice) vs EB-D-194. It does not block v1.

## Where the standing decisions live
- Standing directives: project `CLAUDE.md` §3 (binding rules §2, coordination §4, environment §5, gotchas §6).
- Decision log: `notes/decisions.md` (owner decisions O-1 to O-26 in `design.md` §0; naming and cross-project agreements in `notes/coordination/2026-11-06-copilot-naming-and-ownership.md`).
- Issues: `notes/defer-issues.md` (ISS-001 → #2575).
- ADR-028 A10 is on this branch.
- Reviews: ontology reviews `Services/Signals/**`, `Api/Signals/**` and the worklist route; uac-r2 reviews auth diffs (4 checks, spec FR-02).
- Peer sessions: `spaarke-wt-spaarke-ontology-platform-r1-38`, `spaarke-wt-ebilling-r1-c7`, `spaarke-wt-spaarke-legal-ai-accelerator-80`, `spaarke-wt-unified-access-control-r2-20`.
