# Current Task — Agent Framework Fit Assessment R1

> Tracks ACTIVE task only. History lives in `TASK-INDEX.md` and per-task `.poml` files.

---

**Active task**: none — tasks 001 + 002 complete (parallel-group A landed 2026-06-03). Ready for Phase 2.
**Next task**: task 003 (parallel-group B, independent of 001/002, depends only on 000) — could have run in parallel with A; runs alone now.

**How to start**: from a fresh session, type `work on task 003` and the harness will invoke `task-execute` with the POML.

---

## Last completed wave (parallel-group A)

### Task 001 — Inventory Spaarke AI code surfaces (S1-S4 + S8 catch)
- **Output**: `projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md` (5 surfaces + 7 cross-cutting observations)
- **Commit**: `cb883dd9`

### Task 002 — Inventory non-BFF AI touchpoints (S5-S7)
- **Output**: `projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md` (3 surfaces + 3 cross-cutting observations + 6 UNKNOWNs)
- **Commit**: `dae72474`

---

## Critical findings to carry forward to tasks 003 + 004

### From task 001 (in-BFF surfaces)

1. **`Microsoft.Agents.AI` package is referenced in `Sprk.Bff.Api.csproj` but ZERO source files use it** (grep verified) — clean evidence for the SPEC §2 "half-adopted" framing.
2. **S1 SprkChat is the ONLY surface using Extensions.AI primitives** (`IChatClient`, `AIFunction`, `ChatResponseUpdate`, `FunctionCallContent`). S2/S3/S4 use `IOpenAiClient` (Spaarke wrapper) or `OpenAI.Chat` SDK directly.
3. **Middleware decorates `ISprkChatAgent` not `IChatClient`** — Spaarke missed the canonical `chatClient.AsBuilder().Use(...).Build()` idiom. Biggest single migration vector for S1.
4. **Two-client pattern (default + "raw")** drives Spaarke's compound-intent gate; partially subsumed by upstream Tool Approval (task 000 baseline §4 P5/P9/P12).
5. **Two S8 surfaces discovered**: `Services/Ai/Sessions/SessionSummarizationService.cs` and `Services/Ai/Capabilities/CapabilityRouter.cs` — both use `IChatClient` outside Chat/Builder/Jobs. Task 001 recommends folding into S1 perimeter for task 004.
6. **GitHub Issue #6268** (RED FLAG carried from task 000) preserved at S1 — task 004's S1 ADOPT recommendation must condition on resolution; task 007 re-fetches at review time.

### From task 002 (non-BFF surfaces)

7. **S5 is BIMODAL** — SPEC §3 S5 row was factually wrong and has been corrected. Two distinct facets:
   - (a) **Shipped in-BFF Foundry wrapper** at `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` (5 .cs files) using `Azure.AI.Projects.AgentsClient`, default-OFF kill switch per ADR-018, consumed by `AgentServiceRoutingMiddleware` + JPS `AgentServiceNodeExecutor` (ActionType=60). This is NOT a canonical Foundry use case.
   - (b) **Planned canonical durable/HITL legal workflows** — curated-only in `knowledge/foundry-agent-service/`, no Spaarke code yet.
   - Task 004's S5 row must address BOTH facets.
8. **S6 M365 Copilot uses Microsoft.Agents.Builder / Microsoft.Agents.Core** (M365 Agents SDK) — **NOT** Agent Framework. Scaffolded at `Api/Agent/` (14 files), manifests + Bot Service not yet built. MCP server is Tier 3, deferred to R2.
9. **S7 Insights Engine MCP IMPLEMENTATION DEFERRED to Phase 2** — only the contract document (SPEC D-A20) is in Phase 1 scope, and it doesn't yet exist.
10. **Workflow HITL (`RequestPort` / `RequestInfoEvent`) is in agent-framework itself** — shrinks Foundry's exclusivity claim. Foundry retains VM-isolated sessions + per-agent Entra identity + A2A endpoint as differentiators.

### Prior baseline findings (task 000) — still apply

- Upstream SHA: `afa7834e` (2026-06-03, "1.9 release" — shipped at BUILD 2026)
- Agent Framework 1.0 GA April 2026 → production-ready, not preview
- Sample catalog expanded 4 → 50+ across 5 categories vs. 2026-05-14 curated
- Tool Approval is now a framework feature (partly subsumes Spaarke's CompoundIntentDetector + UseFunctionInvocation/raw-client split)

---

## Citation discipline (still binding)

- Every feature claim cites a URL from `notes/00-primary-source-baseline.md` §4 (Learn pages) or §5 (Devblogs) with fetched date
- No claims citing only the curated `knowledge/agent-framework/` snapshot — orientation only
- §10 Sources appendix in the final assessment document is mandatory

---

## Next task (Phase 2 — parallel-group B, standalone)

**Task 003**: Map Agent Framework feature surface vs. Extensions.AI baseline — produces structured findings at `notes/03-agent-framework-feature-map.md`. Depends only on task 000 baseline. The S1-S8 inventory from tasks 001/002 is NOT required input for 003 itself (per TASK-INDEX.md group structure), but task 003's output will be merged with 001/002 inventories in task 004's per-surface decision matrix.

After 003: task 004 (per-surface decision analysis) is sequential and consumes outputs from 001 + 002 + 003.
