> ⚠️ STUB — senior engineer review pending

# NOTES — foundry-agent-service

This file is a structural skeleton. The headings below mirror the "NOTES.md guidance" from the knowledge-base-setup directive. Each section contains only a TODO hint until a senior engineer who has actually built on Foundry Agent Service annotates it. **Do not infer or fabricate content here from the sample files or the snapshotted docs alone** — the value of this file is in lived experience and Spaarke-specific judgment, not in restating Microsoft documentation.

---

## When Foundry Agent Service is the right choice

_TODO: senior engineer to fill in. Cover: when to pick Foundry Agent Service over (a) Agent Framework hosted in Spaarke's own BFF, (b) Copilot Studio agents, (c) raw Azure OpenAI Assistants API. Anchor on the durable / multi-day / HITL / A2A axis. Note the preview status of Workflow agents, Hosted agents, A2A protocol, and the memory tool — and what that means for production readiness of Spaarke's legal workflows._

## Workflow definition syntax and graph patterns

_TODO: senior engineer to fill in. Review [`docs/workflows.md`](./docs/workflows.md) and note: (a) the workflow surface is UI-first with YAML export, not a Python DSL — confirm this matches our current understanding, (b) the three template patterns (Human in the loop, Sequential, Group chat) and which maps to which Spaarke scenario, (c) the Hosted-agent exclusion — workflow designer does not support Hosted agents, so code-driven graph workflows route through Microsoft Agent Framework workflows in a Hosted agent container. Cross-reference `knowledge/agent-framework/` once it's populated._

## HITL primitives — `wait_for_external_event`, approval gates, resumption semantics

_TODO: senior engineer to fill in. The directive asked specifically about `wait_for_external_event`. As of 2026-05-14 we could not locate this primitive in any public Foundry SDK sample — see "Gaps" in [`SOURCE.md`](./SOURCE.md). The two HITL surfaces we **did** find:_

1. _**Workflow-level HITL** (UI builder) — the "Human in the loop" template documented in [`docs/workflows.md`](./docs/workflows.md). Asks user a question, awaits input. Annotate: how this maps to Spaarke approval gates (matter-level approvals, document review gates)._
2. _**Tool-level approval** — `mcp_tool.set_approval_mode("prompt")` + `SubmitToolApprovalAction` run state, demonstrated in [`samples/mcp-tool-binding/sample_agents_mcp.py`](./samples/mcp-tool-binding/sample_agents_mcp.py) and [`samples/mcp-approval-gate/agent_config_template.yaml`](./samples/mcp-approval-gate/agent_config_template.yaml). Annotate: this is the closest current primitive for gating destructive or sensitive MCP calls. Resumption semantics — runs sit in `requires_action` until approval submitted or run cancelled._

_Validate whether `wait_for_external_event` exists in a preview SDK we haven't surfaced, or whether the durable orchestration story is entirely Hosted-agents-plus-Agent-Framework-workflows._

## A2A protocol — how cross-system agent composition works

_TODO: senior engineer to fill in. We have no curated A2A sample (preview surface, no public sample on 2026-05-14 — see [`SOURCE.md`](./SOURCE.md) gaps table). [`docs/overview.md`](./docs/overview.md) and [`docs/hosted-agents.md`](./docs/hosted-agents.md) confirm A2A is a first-class protocol on Hosted agents with endpoint pattern `{project_endpoint}/agents/{name}/endpoint/protocols/a2a`. Annotate: (a) when Spaarke would compose with external A2A endpoints versus calling them as MCP servers, (b) auth model for A2A (Entra agent identity vs. OBO), (c) preview risk for production use._

## Foundry Memory — agent state persistence, custom user-scope headers, cost model

_TODO: senior engineer to fill in. Memory is a preview built-in tool per [`docs/overview.md`](./docs/overview.md), but no dedicated concept doc was published as of 2026-05-14 ([`docs/GAP-memory.md`](./docs/GAP-memory.md)). Annotate once Microsoft publishes the spec: (a) scoping (user / thread / agent), (b) custom user-scope headers, (c) cost model and how it compares to bring-your-own memory (e.g., third-party Mem0 — see the `ai-foundry-agents-samples/examples/mem0/` reference, which we deliberately did NOT curate since it is not the Foundry memory tool)._

## Evaluator integration — how to wire evaluators into agent executions

_TODO: senior engineer to fill in. The overview page lists evaluation as step 4 of the development lifecycle. Annotate: (a) the `azureai-samples/scenarios/evaluate/` content (not curated here — belongs in a future `knowledge/azure-ai-evaluations/` topic), (b) how evaluators bind to Foundry agent runs, (c) Spaarke's evaluator strategy — golden-document accuracy, citation precision, hallucination rate for legal contexts._

## Spaarke's multi-step legal workflows — mapping to Foundry workflows

_TODO: senior engineer to fill in. Three Spaarke scenarios named in the directive — full-matter diligence, NDA negotiation chain, regulatory monitoring. For each, sketch:_

- _Which Foundry agent type (Prompt / Workflow / Hosted) is the right fit and why._
- _Where HITL gates land in the flow (matter assignment, NDA term acceptance, regulatory change publish-to-firm)._
- _Which MCP servers participate (Dataverse MCP for matter records, SPE substrate for documents, Work IQ MCP for collab context, Foundry IQ for curated playbooks)._
- _Durability requirements — which legs are multi-day / multi-week and need Hosted-agent session resume._
- _Cost / preview-risk trade-off — what stays on a stable surface vs. what we accept preview risk on._

---

## When this file becomes useful

This NOTES.md is a stub until a senior engineer who has shipped a Spaarke feature on top of Foundry Agent Service annotates it. The right time to annotate is **after a real project has shaken out the rough edges** — not before. A NOTES.md that pretends to have insight it doesn't is worse than one that admits where the gaps are.

The samples and docs alongside this file are factually accurate and current as of the curation date (see [`SOURCE.md`](./SOURCE.md)). The interpretation layer — when to use what, what to watch out for, how Spaarke's real workflows map — is the missing piece.
