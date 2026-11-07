# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-06 (pipeline Step 5 dispatch)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Pipeline Step 5 — Wave 1 done except 010 (owner decision) ; spike dev deploy in progress via uac-r2 |
| **Step** | Spike branch merged with uac-r2 batch master SHA `cc11294300` → new spike HEAD `72e300c40b` (diff == `5c9e301572` only, verified). NOT yet built/pushed/measured. |
| **Status** | in-progress |
| **Next Action** | (1) In `C:/code_files/spaarke-wt-spaarkeai-mcp-phase0-spike`: `dotnet build`, `git push`, PR #2615 CI (Router + Client lint) green; re-measure publish delta (master `cc11294300` vs spike, procedure `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule") + `dotnet list package --vulnerable --include-transitive`; SendMessage uac-r2 (`spaarke-wt-unified-access-control-r2-20`) the SHA + numbers. (2) Owner to answer: task 010 → option C? (see below). |

### Open owner decision — task 010 (asked 2026-11-06)
Investigation recommends **C (defer ICallerContext)**: stateless MCP tools run inside the request (SDK 2.3.1 uses `context.RequestServices`, `PerSessionExecutionContext=false`), so tools call existing decision services exactly as REST does; no change to DataverseUserClient / WhoAmI memo / chat paths; WhoAmI stays 1/commit. If owner says yes: amend spec FR-10 (defer B-4), replace tasks 010+012 with one verification task (tests: tool sees request context + RequestServices; real DataverseUserClient behind fake wire handler; MCP T170 WhoAmIs==1; MCP plan stamp satisfies MCP commit; pin stateless + PerSessionExecutionContext=false; existing suites + 011 baselines unchanged), file ISS for probable defect `ComposeProfileDispatcher.cs:130-134` (sets accessor.HttpContext inside Task.Run → may null the originating request's context), then continue 017 → 013.

### Done this session (2026-11-06/07)
- Wave 1: 011 ✅ (`b9d8f19b47`); 001 built (spike `5c9e301572`, notes `00c4ced7e3`); 010 escalated (no code).
- Spike deploy terms (owner, in uac-r2's session): every uac-r2 dev deploy ships master + `5c9e301572` until we declare Phase 0 done; draft PR #2615 never merged (notes/decisions.md).
- BFF split reassessment: no split now (`notes/drafts/bff-split-reassessment-2026-11-06.md`, `58ac89ed65`); full re-assessment due 2026-11-20 (other owners).
- Leftover `stash@{0}` "lint-staged automatic backup" — owner to drop.

### Critical Context
- Owner chose (2026-11-06) to merge the held ontology PRs into this branch: #2580 `9423f37004` (merge `489f6508fc`) and #2579 `ab04492ab7` (merge `026b61554a`). The BFF builds with 0 warnings and 0 errors. Facts about their code: `notes/pre-pipeline-context.md`.
- **STANDING (ontology condition):**
  - NO dev BFF deploy from this branch until ontology's #2580/#2579 pair has deployed (it waits for ebilling task 112).
  - Phase 0 live tests run from a separate spike branch cut from master. Deploys only with the owner's per-SHA go in uac-r2's session.
  - Our master PR merges after their pair. Re-merge if their branches move (they merge, never rebase).
- The D-207 order and replay guard are unchanged. The MCP commit goes only through `DecisionCommitService`.

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
