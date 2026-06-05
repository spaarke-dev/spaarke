# Unblock Recommendation — Agent Framework Knowledge R1

> **Source assessment**: [`docs/assessments/agent-framework-fit-assessment-2026-06-03.md`](../../docs/assessments/agent-framework-fit-assessment-2026-06-03.md)
> **Written**: 2026-06-03 by `agent-framework-fit-assessment-r1` task 008
> **Status**: Recommendation only — this file does NOT edit [`SPEC.md`](./SPEC.md). SPEC refinement is a downstream decision after human review of the assessment.

---

## TL;DR

The fit assessment landed: **1 ADOPT (S5B canonical durable HITL legal workflows) · 5 PARTIAL · 4 DON'T ADOPT**. This unblocks knowledge-r1 to resume, but the SPEC should be **re-scoped to match the assessment's adoption boundaries** before task 001 executes.

The knowledge that gets built should support the work Spaarke will actually do — not generic Agent Framework parity for surfaces the assessment says won't adopt.

## Per-surface verdict summary

| Surface | Verdict | Knowledge curation implication |
|---|---|---|
| S1 SprkChat | PARTIAL (gated on Issue #6268; Q7 verification) | **Prioritize** — middleware lift is the canonical pattern |
| S2 JPS playbooks | DON'T ADOPT | **De-prioritize** — no Workflows-vs-JPS comparison needed |
| S3 Builder | PARTIAL (bundles with S1) | **Prioritize** — same middleware-lift pattern |
| S4 Background AI jobs | DON'T ADOPT | **De-prioritize** — no agent-as-background-job curation needed |
| S5A Foundry wrapper | PARTIAL (bundles with S1) | **Prioritize lightly** — wrapper simplification via `AsAIAgent` |
| **S5B Canonical durable HITL** | **ADOPT** ⭐ | **High priority — this is the actual greenfield work** |
| S6 M365 Copilot | DON'T ADOPT (uses M365 Agents SDK) | **De-prioritize** — distinct SDK, out of Agent Framework scope |
| S7 Insights Engine MCP | PARTIAL (deferred to D-A20 contract) | Optional — depends on D-A20 outcome |
| S8a SessionSummarization | DON'T ADOPT | **De-prioritize** — single-call consumer pattern |
| S8b CapabilityRouter | PARTIAL (folds into S1) | Same as S1 |

## Recommended SPEC changes for knowledge-r1

### 1. Prioritize curation for the adopt + partial-lift surfaces

The knowledge that Spaarke engineers will actually reach for:

- **Middleware composition pattern** (`chatClient.AsBuilder().UseFunctionInvocation().UseOpenTelemetry(...).Use*(...).Build()`) — the single biggest migration vector per the assessment §7.1. S1 + S3 + S5A + S8b all consume this pattern. Include per-middleware-tier examples (Agent Run / Function Calling / IChatClient) per [`knowledge/agent-framework/docs/agents.md`](../../knowledge/agent-framework/docs/agents.md) §Middleware.
- **`ChatClientAgent` consumption patterns** — lift from raw `IChatClient` to agent shell. Include `AgentSession` for thread state, `RunAsync<T>` for structured outputs.
- **Tool Approval** — `ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent`. Replaces Spaarke's hand-rolled `CompoundIntentDetector` + two-client `"raw"` keyed pattern.
- **Workflows** — `WorkflowBuilder`, `Executor<TIn,TOut>`, supersteps, checkpoints. **Primary curation depth for S5B.**
- **`RequestPort` / `RequestInfoEvent` HITL** — workflow-level human-in-the-loop. Framework-internal (not Foundry-exclusive).
- **Foundry hosting** — `FoundryHostedAgents` sample tree at `04-hosting/`. **Address F12 evidence-thin gap** flagged in assessment notes/03.
- **`Microsoft.Agents.AI.Hosting.A2A.AspNetCore` + `MapA2A()`** — for any future A2A exposure.
- **OTel observability** — `UseOpenTelemetry(sourceName)` + `WithOpenTelemetry()` + GenAI Semantic Conventions. Mention duplication warning from observability Learn page.

### 2. De-prioritize curation for the don't-adopt surfaces

Material that should NOT consume curation budget:

- **JPS-vs-Workflows comparison depth** — S2 is DON'T ADOPT; no Spaarke engineer should be making this comparison as part of active work.
- **M365 Agents SDK comparison** — S6 uses a distinct SDK (`Microsoft.Agents.Builder` / `Microsoft.Agents.Core`); conflating it with Agent Framework would push the wrong abstraction.
- **Agent-as-background-job patterns** — S4 stays on Service Bus + direct LLM call; no curation needed.
- **Single-call `IChatClient` consumer patterns** — S8a is the textbook anti-fit; no curation guidance needed for "wrap a single LLM call in `ChatClientAgent`."

### 3. Add a Known Issues appendix

The most actionable single piece of guidance for any Spaarke engineer considering adopting `ChatClientAgent.RunStreamingAsync` today:

- **GitHub Issue #6268**: `.NET ChatClientAgent.RunStreamingAsync ends with no assistant text on multi-tool turns` — opened 2026-06-02, status `needs-maintainer-triage` as of 2026-06-03.
- **Reproduction-scope qualifier (from assessment §8 Q7)**: the full bug title qualifies reproduction as "reasoning model + stateless Responses API." Spaarke's S1 uses Azure OpenAI GPT-4o (NOT a reasoning model) via Chat Completions (NOT the Responses API). Whether S1 is actually exposed to #6268's reproduction surface is itself an open question — empirical test recommended before lift commitment.

This appendix turns the assessment's load-bearing red flag into actionable guidance for the knowledge consumer.

### 4. Add S5B prototyping support material

The S5B deployment-model decision (Foundry-hosted vs Workflows-in-BFF vs Workflows-in-Function) requires a 1-2 week prototyping phase per assessment §6.4. Knowledge should include enough hosting-model detail to support that:

- `04-hosting/` upstream sample tree at SHA `afa7834e`
- Devblog D6 ("Durable Workflows in Microsoft Agent Framework") summary
- Issue #6308 ("How to deploy dotnet Hosted agents to Foundry") status tracker
- Foundry-hosted vs in-BFF deployment comparison framework (the three ADR-013-defensible candidates)

## Tasks that may need re-scoping

Recommended re-evaluation pass (do NOT execute task 001 until this happens):

- **Task 003-005 (reference doc snapshots)**: prioritize Workflows + middleware + Tool Approval + Foundry hosting Learn pages. De-prioritize JPS-vs-Workflows comparison fetches.
- **Task 002 (curated samples)**: prioritize samples in `02-agents/`, `03-workflows/`, `04-hosting/`. De-prioritize samples that demonstrate single-call `IChatClient` consumer patterns.
- **Task 007 (NOTES.md rewrite)**: ground commentary on the surfaces that will actually adopt — S1 lift pattern, S5B greenfield pattern. Do NOT spend depth on S2/S4/S6/S8a "for completeness."
- **Task 009 (pattern files)**: write patterns for the middleware lift + Workflows + Tool Approval + Foundry hosting. Do NOT write patterns for JPS-style orchestration.
- **Task 010 (SKILL.md)**: `appliesTo` should trigger on the lift surfaces (chat agent code; workflow authoring; Foundry hosting decisions) — not generic "any AI work."

## What this file is NOT

This file is a **recommendation**, not an edit. Per the fit-assessment project scoping:

- This file does NOT modify [`SPEC.md`](./SPEC.md). SPEC refinement is a follow-up decision after human review of the assessment.
- This file does NOT modify [`TASK-INDEX.md`](./TASK-INDEX.md) task scope.
- This file does NOT pre-commit knowledge-r1 to any specific re-scoping outcome.

The knowledge-r1 owner reviews the assessment + this recommendation, decides what to keep / re-scope / drop, and edits the SPEC accordingly.

## How to resume knowledge-r1

1. Read the [landed assessment](../../docs/assessments/agent-framework-fit-assessment-2026-06-03.md) in full
2. Read this recommendation
3. Decide which re-scoping recommendations apply
4. Edit [`SPEC.md`](./SPEC.md) accordingly
5. Update [`TASK-INDEX.md`](./TASK-INDEX.md) if task scope changes
6. Remove the parking notice from [`README.md`](./README.md)
7. Type `work on task 001` to resume execution
