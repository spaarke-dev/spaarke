# Current Task — Agent Framework Fit Assessment R1

> Tracks ACTIVE task only. History lives in `TASK-INDEX.md` and per-task `.poml` files.

---

**Active task**: none — Phase 2 complete (tasks 000, 001, 002, 003 all ✅). Ready for Phase 3.
**Next task**: task 004 (sequential — depends on 001+002+003 outputs) — apply SPEC §4 decision criteria to each surface S1-S8.

**How to start**: from a fresh session, type `work on task 004` and the harness will invoke `task-execute` with the POML.

---

## Last completed task

### Task 003 — Map Agent Framework feature surface vs. Extensions.AI baseline
- **Output**: `projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md` (537 lines, 12 features F1-F12, 19 distinct primary-source citations)
- **Commit**: `68d35c73`
- **Recency**: 94.7% within 2026-04-01 floor; no researcher escalations needed

---

## Critical findings to carry forward to task 004 (per-surface decision matrix)

### From task 003 (feature map) — top 5 affecting per-surface decisions

1. **S1 lift is structural fit but GATED**: F1 (AIAgent/ChatClientAgent) + F4 (three-tier middleware) + F10 (observability) + F11 (Tool Approval) each map 1:1 onto Spaarke hand-rolls in `Services/Ai/Chat/` (`ISprkChatAgent`, three middlewares, `AgentTelemetryMiddleware`, `CompoundIntentDetector`). Adoption gated on **GitHub Issue #6268** — Spaarke's canonical workload (multi-tool streaming) is what breaks.
2. **S2 lift to Workflows is binary**: F7 is the only path. Migration replaces JPS-specific node types entirely. The decision is workflow-replaces-JPS or stay; no incremental adoption story.
3. **Tool Approval (F11) unifies HITL**: `ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent` collapses Spaarke's hand-rolled `CompoundIntentDetector` + `UseFunctionInvocation`/raw-client split. Same event model also drives workflow HITL.
4. **S5 Foundry overlap CLARIFIES**: Workflow HITL (`RequestPort` + `RequestInfoEvent`) is in the framework itself — NOT Foundry-exclusive. Foundry adds the **hosting** surface (`FoundryHostedAgents` sample) + VM-isolated sessions + per-agent Entra identity + A2A endpoint. The "use Foundry for HITL" framing is wrong.
5. **S6/S7 are forward-compat, not swap-ins**: F9 A2A + F8 hosted MCP are additive options; Spaarke's MCP/REST exposure already in flight. No urgency to lift these surfaces.

### From task 002 (non-BFF inventory)

6. **S5 is BIMODAL** — (a) shipped in-BFF Foundry wrapper at `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` (5 files, default-OFF per ADR-018), consumed via `AgentServiceRoutingMiddleware` + JPS `AgentServiceNodeExecutor` ActionType=60; (b) planned canonical durable/HITL workflows (curated-only). Task 004's S5 row must address BOTH facets.
7. **S6 M365 Copilot uses Microsoft.Agents.Builder / Microsoft.Agents.Core** (M365 Agents SDK) — NOT Agent Framework. Scaffolded at `Api/Agent/` (14 files); manifests + Bot Service not yet built. MCP server is Tier 3, deferred to R2.
8. **S7 Insights Engine MCP IMPLEMENTATION DEFERRED to Phase 2** — only the SPEC D-A20 contract is in Phase 1 scope, and it doesn't yet exist.

### From task 001 (in-BFF inventory)

9. **`Microsoft.Agents.AI` package referenced in `Sprk.Bff.Api.csproj` but ZERO source usage** — clean evidence for SPEC §2 "half-adopted" framing.
10. **S1 SprkChat is the ONLY surface using Extensions.AI primitives** (`IChatClient`, `AIFunction`, `ChatResponseUpdate`, `FunctionCallContent`). S2/S3/S4 use `IOpenAiClient` wrapper or `OpenAI.Chat` SDK directly.
11. **Middleware decorates `ISprkChatAgent` not `IChatClient`** — Spaarke missed the canonical `chatClient.AsBuilder().Use(...).Build()` idiom. Biggest single migration vector for S1.
12. **Two S8 surfaces discovered**: `Services/Ai/Sessions/SessionSummarizationService.cs` and `Services/Ai/Capabilities/CapabilityRouter.cs` — both use `IChatClient` outside Chat/Builder/Jobs. Task 004 must apply the matrix to them.

### Evidence-thin items for task 005/006 attention

- **F3 Context providers** — no standalone Learn `/agents/context-providers/` page; relied on overview + sample tree. Re-fetch attempt in task 006.
- **F12 Durable hosting** — no dedicated `/hosting/` Learn page; relied on `04-hosting/` sample tree + Devblog D6 + open GitHub Issue #6308. Task 005 (deployment + migration) must address durable-hosting gap explicitly.

### Prior baseline findings (task 000) — still apply

- Upstream SHA `afa7834e` (2026-06-03, "1.9 release" — shipped at BUILD 2026)
- Agent Framework 1.0 GA April 2026 → production-ready, not preview
- Tool Approval is now a framework feature (notes/00 §4 Pages 5/9/12)
- Workflow HITL is framework-internal, not Foundry-exclusive

---

## Citation discipline (still binding)

- Every feature claim cites a URL from `notes/00-primary-source-baseline.md` §4 (Learn pages) or §5 (Devblogs) OR notes/03 feature map (which already enforces this)
- §10 Sources appendix in the final assessment document is mandatory
- ≥80% of primary-source citations dated 2026-04-01 onwards

---

## Next task (Phase 3 — sequential)

**Task 004**: Per-surface decision analysis — apply SPEC §4 decision criteria (technical fit + Agent Framework value + migration cost + deployment-model criteria) to each surface S1-S7, plus the two S8 discoveries (`SessionSummarizationService`, `CapabilityRouter`). Output: `notes/04-per-surface-decision-matrix.md`. This is the project's hardest sequential task — consumes outputs of 001 + 002 + 003 and produces the per-surface yes/no/partial verdicts that drive synthesis in task 006.

After 004: tasks 005 (deployment + migration), 006 (synthesis — write the canonical assessment document), 007 (adversarial review + recency re-check), 008 (sign-off + unblock note).
