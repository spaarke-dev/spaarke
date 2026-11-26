# Project Plan: Specialist Legal Agents R1, Phase 0

> **Last Updated**: 2026-11-26
> **Status**: Ready for Tasks
> **Spec**: `spec.md` (Phase 0 only; Phases 1–4 are child projects, design.md §4)

---

## 1. Executive Summary

**Purpose**: Decide the runtime shape of the specialist-agent program before any Phase 1 build. The main question is G1: does Copilot follow an attorney's profile reliably, or must the profile drive server-side execution while Copilot only routes? G1 decides D-A7.

**Scope**:
- Test kits, synthetic and public test content, and an R4 inventory row for every tenant artifact (FR-01, FR-09).
- Dev-only spike tools on a branch that is never merged (FR-02), and G3 profile resolution (FR-03).
- G1-NDA, the go/no-go (FR-04, FR-05); G5 routing (FR-06); G6 coexistence (FR-07); the G2 remainder (FR-08).
- The Cowork patent-drafting spike (FR-10) and the G1-PAT repeat (FR-11).
- Desk items I-1 to I-4 (FR-12), the go/no-go record (FR-13), and clean-up (FR-14).

**Timeline**: no dates; the owner controls them. **Estimated effort**: about 12–18 working days of agent work, plus the owner's and the test user's prompt runs. The runs are staged, so a variant that fails its pilot stops early.

No product code merges to master.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-028 (A10)**: test agents use the existing dedicated connector client and OAuth registration. Callers are `systemuser` only. The production registration A1 is never changed.
- **ADR-039** (with the 2026-11-25 amendment, PR #3213):
  - one dispatch protocol, closed catalogues; profiles only narrow them;
  - new capability never lands on the frozen node-graph engine;
  - every AI run is recorded on `sprk_playbookrun` through `PlaybookRunRecordStore`. That applies to Phase 1; Phase 0 writes no run records.
- **ADR-040**: the session ledger is erasable. It is not the durable run record.
- **ADR-013 (amended 2026-07-05)** and **ADR-043**: capability invocation is Action + Binding. Declared inputs render into the prompt; undeclared inputs are dropped.
- **ADR-008**: spike tools inherit endpoint-filter authorization from the `/mcp/{area}` chain.
- **ADR-038**: spike tests live on the spike branch only.
- **ADR-052**: no background work in Phase 0.

**From Spec**:
- Spike tools are read-only, model-visible, on `/mcp/legal-operations`, and copied from `ShowDecisionsTool`.
- A caller with no test profile gets `NO_PROFILE`; an unresolved caller gets `CALLER_UNRESOLVED`. Neither ever falls back to another user's profile or to a default attorney profile.
- Request headers are logged by name only. `Authorization`, cookies and tokens are never logged.
- Never put `answer-key/` or the real NDAs anywhere an agent can read.

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| Spike branch, never merged (owner, 2026-11-25) | Phase 0 measures model behaviour; production tools are Phase 1 | No BFF merge; no overlap with mcp-r2's `Api/Mcp/` on master |
| `IActionRunner` with `BoundInputs` over `agreement-review` for variant (c) | It is the action-based model Phase 1 will use (`notes/run-path-review-2026-11-25.md`). The node engine is frozen | The spike is production-shaped, not a stand-in |
| Staged test effort (owner) | Keeps manual prompt-running small | 10-case pilots; a variant below 90% stops; about 40 runs per surface for survivors |
| G1 on both NDA and patents (owner) | NDA content exists; patents are the first archetype (OD-2) | G1-NDA is the go/no-go; G1-PAT confirms or contradicts it |
| AI-run record = `sprk_playbookrun`, one writer `PlaybookRunRecordStore` (owner 2026-11-25; agreed with mcp-r2) | Extend an existing table instead of adding one (CLAUDE.md §11) | I-3 records the decision; Phase 1 adds its columns to the same store |

### Discovered Resources

**Applicable skills:**
- `.claude/skills/task-execute/`: runs every task.
- `.claude/skills/mcp-tool-handler/`: shape and wiring for the spike tools (task 011).
- `.claude/skills/declarative-agent/`: test agent packages, Step 5.5 (task 020).
- `.claude/skills/bff-deploy/`: the spike dev deploy, run only through uac-r2's session (task 015).
- `.claude/skills/code-review/` and `.claude/skills/adr-check/`: gates on task 011 and at wrap-up.

**Knowledge:**
- `docs/architecture/playbook-architecture.md`: what "playbook" means; the Action and coded-workflow model.
- `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` §4.2 and §6.6.
- `notes/agent-package-identity-and-distribution.md`: build rules BR-1 to BR-7.
- `notes/run-path-review-2026-11-25.md` and `notes/playbook-concept-and-run-record-2026-11-25.md`.
- `notes/reuse-scan.md`: the reuse verdicts per capability.
- `phase0/g8/README.md`: test kit and run-log pattern, with App Insights KQL in PowerShell.

**Reusable code** (paths under `src/server/api/Sprk.Bff.Api/`):
- `Api/Mcp/LegalOperations/Decisions/ShowDecisionsTool.cs`: the tool shape to copy.
- `Services/Ai/Context/CallerSystemUserResolver.cs`: caller → `systemuser`; returns Unresolved, never a guess.
- `Services/Ai/Context/ContextBinder.cs` (`ResolveUserFragmentAsync`): the data-guarded profile block style.
- `Services/Ai/LinearConsumers/IActionRunner.cs`: the synchronous structured Action run.
- `infra/dataverse/actions/agreement-review.action.json`: the Action that generalises `nda-review`.

**Accelerator inputs** (read only, branch `origin/work/spaarke-legal-ai-accelerator-r1`):
- `src/accelerators/practices/nda/positions/`, `evals/`, `samples/`;
- `FORBIDDEN_IN_PACKAGE` in `src/accelerators/build/gates.py`;
- T-P2 in `projects/spaarke-legal-ai-accelerator-r1/notes/component-protocol.md`.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Test kits, content and desk items (no tenant, no deploy)
└─ Every kit, profile and corpus exists; desk items I-1 to I-4 written
Phase 2: Spike tools (branch only) and dev deploy
└─ spike_get_profile + spike_review_nda on dev, fail-closed proven
Phase 3: Test runs (owner and test user in the loop)
└─ G3, G1-NDA, G5 + G2, G6, patent spike, G1-PAT recorded
Phase 4: Decision and clean-up
└─ phase0/G1-decision.md; tenant and DEV artifacts removed
Wrap-up (090)
```

### Critical Path

**Blocking dependencies:**
- 011 (spike tools) is blocked by 002 (profiles), 006 (patent profiles) and 010 (Action row check).
- 015 (deploy) is blocked by 011 and by the owner's per-SHA go.
- 020 (packages) is blocked by 015, and owner uploads need its R4 rows.
- 022 (G1-NDA) blocks 026 (G1-PAT); all runs block 030 (decision).

**High-risk items:**
- The `agreement-review` row declares no profile input. Mitigation: a DEV-only `agreement-review-spike` copy, created only on the owner's go and removed in 031.
- The Reasoning tier may exceed 30 s. Mitigation: record per-call latency; if it misses, the spike records the miss as a G1 finding.
- Old G8 agents confuse the tests. Mitigation: they are removed by 2026-11-30, before any run.

---

## 4. Phase Breakdown

### Phase 1: Test kits, content and desk items

**Objectives:** every test can be run from a written script, and the desk questions are answered from code.

**Deliverables:**
- [ ] `phase0/README.md` index and artifact inventory (001)
- [ ] G1-NDA kit with profiles A and B (002); the synthetic NDA corpus and ratchet probes (003)
- [ ] G5 and G2 kits (004); the G6 kit (005)
- [ ] Patent test content plus the patent-spike and G1-PAT kits (006)
- [ ] `notes/phase0-desk-items.md`: I-1 to I-4 (007)

**Inputs:** spec FR-01 and FR-04 to FR-12; the accelerator content (read only); `phase0/g8/README.md`.
**Outputs:** `phase0/{g1-nda,g1-pat,g2,g5,g6,patents}/`, `notes/phase0-desk-items.md`.

### Phase 2: Spike tools and dev deploy

**Objectives:** the two spike tools run on dev and fail closed.

**Deliverables:**
- [ ] The `agreement-review` row is checked; the DEV copy is created only if needed, with the owner's go (010)
- [ ] Branch `spike/specialist-agents-phase0` with the tools, the test-profile files and branch tests (011)
- [ ] Dev deploy through uac-r2 (015)

**Inputs:** profiles from 002 and 006; master including mcp-r2's merged work.
**Outputs:** the spike branch (never merged); a dev BFF exposing `spike_*` tools.

### Phase 3: Test runs

**Objectives:** each gate is recorded against its pass rule.

**Deliverables:**
- [ ] Test agent packages, with R4 rows sent before any upload (020)
- [ ] G3 (021), G1-NDA (022), G5 + G2 (023), G6 (024), patent spike (025), G1-PAT (026)

**Inputs:** the Phase 1 kits; the Phase 2 deploy; Legal for Cowork switched on (024); the owner's and the test user's prompt runs.
**Outputs:** the results sheets in each `phase0/<test>/README.md`, with App Insights evidence.

### Phase 4: Decision and clean-up

**Deliverables:**
- [ ] `phase0/G1-decision.md` with recommendations on D-A7, D-A12 and D-A13, and the Phase 1 inputs (030)
- [ ] Clean-up: removal dates, the DEV Action copy deleted, the inventory rows closed (031)
- [ ] Project wrap-up (090)

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| M365 Copilot licences, owner and test user | In place (T11) | Low | — |
| Legal for Cowork switched on | Owner action | Medium | 024 waits; other runs proceed |
| Dev BFF deploy slot (uac-r2) | Per-SHA owner go | Medium | Kits and desk items do not wait on it |
| COPILOT-PLATFORM-COORDINATION (R4 rows, inventory) | Active | Low | Rows are sent before every upload |
| G8 artifact removal by 2026-11-30 | Owner action | Medium | Runs start after it |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| `ICallerSystemUserResolver` | `src/server/api/Sprk.Bff.Api/Services/Ai/Context/` | Production |
| `IActionRunner` | `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/` | Production |
| `/mcp/legal-operations` auth chain | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpEndpoints.cs` | Production |
| `agreement-review` Action row in DEV | Dataverse | Unverified (task 010) |

---

## 6. Testing Strategy

**Unit tests (spike branch only):** fail-closed (`NO_PROFILE`, `CALLER_UNRESOLVED`); user A never receives user B's profile; header logging records names and never values. No test is added to master (ADR-038).

**Integration tests:** p95 latency of the spike tools on dev, under 30 s.

**Acceptance tests (manual, owner and test user):** the G1, G5 and G6 prompt scripts, scored on the results sheets with App Insights evidence. BFF requests are sampled at about 25%, so each check uses several turns.

---

## 7. Acceptance Criteria

### Technical Acceptance
- [ ] Spike tools fail closed and never leak another user's profile; p95 under 30 s on dev.
- [ ] Every tenant artifact has an R4 row sent before upload, and a removal date.
- [ ] No kit or package references `answer-key/` or a real NDA.

### Business Acceptance
- [ ] G1-NDA has a verdict for each variant and surface: ≥95% adherence, 0 ratchet escapes, 0 bleed; otherwise (c) is the runtime.
- [ ] G5: ≥95% routing and no function invoked for a control. G6: no capture in either direction.
- [ ] The owner records D-A7 in design §12.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Copilot ignores tool-returned instructions (T7), so (a) and (b) fail | High | Medium | Expected; (c) is measured in the same run |
| R2 | The `agreement-review` row has no profile input | Medium | Medium | DEV-only copy on the owner's go (010) |
| R3 | Stale test agents or action caching confound the results | Medium | High | Fresh app id per action change, or a 45-minute wait (BR-2); G8 artifacts removed first |
| R4 | Real NDA or answer-key content leaks into a package | Low | High | `FORBIDDEN_IN_PACKAGE` check in every kit and package task |
| R5 | A spike deploy rolls back master or mcp-r2 work on dev | Low | High | Branch cut from current master; deploy only through uac-r2 with the owner's go |

---

## 9. Next Steps

1. Wave 1: tasks 001–006 in parallel.
2. Wave 2: 007 and 010. 010 stops for the owner's go before any DEV write.
3. Phase 2 continues after the owner's go for the dev deploy.

---

**Status**: Ready for Tasks
**Next Action**: execute Wave 1 (001–006)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
