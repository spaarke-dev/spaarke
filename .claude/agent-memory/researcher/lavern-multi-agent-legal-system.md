---
name: lavern-multi-agent-legal-system
description: Architecture deep-dive of AnttiHero/lavern — TypeScript multi-agent legal system. Useful when evaluating multi-agent patterns, debate protocols, verification pipelines, or human-in-the-loop gating designs.
metadata:
  type: reference
---

# Lavern (AnttiHero/lavern) — Reference

**What it is:** Apache 2.0, TypeScript (not Python — primary lang TS, only 4.7KB Python in eval scripts). Multi-agent legal review system. 67 agent prompts (59 specialists + 7 workflow-specific orchestrators + 1 base orchestrator). Runs against Anthropic (default), Mistral (EU), Ollama (local), or Anthropic Managed Agents (scaffolded only).

**Repo root facts:**
- v0.15.0, 80 stars, last pushed 2026-05-20, created 2026-05-13.
- 1,677 tests across 105 files. Clean `tsc --noEmit`.
- Built on `@anthropic-ai/claude-agent-sdk` (Claude Code SDK).
- SQLite persistence (BM25/FTS5, no vector/embedding).
- Fastify API + React (Vite) dashboard + SwiftUI macOS menubar.
- 28-module Clawern autonomous daemon for folder-watching.

## Runtime model (the actual mechanics)

- Each "agent" = a TypeScript module exporting a system prompt string PLUS a profile object (NBA2K-style skill ratings, costTier, criticalRules, successMetrics, output schema). See `src/agents/definitions.ts` (defs) and `src/agents/profiles.ts` (62 profiles with avatarExtra DiceBear codes).
- The orchestrator (`src/orchestrator.ts`) is a Claude Code SDK `query()` call with `agents: agentDefinitions`, `mcpServers: { shem: createShemMcpServer(session) }`, hooks (audit, cost, human-gate, halt), and `allowedTools` listing all MCP tools.
- Specialists are dispatched as **SDK subagents** (Anthropic SDK feature) — NOT as separate processes or HTTP calls.
- Inter-agent communication happens via the **MCP "shem" server's debate-board state**, which lives in a per-session `SessionState` object held in-process (not a queue/bus). Findings, challenges, responses are objects appended to `session.debate`.
- Event bus (`src/events/event-bus.ts`) is in-process pub/sub for dashboard streaming via WebSocket.

## Agent organization (59 specialists; categories from `profiles.ts` + dir listing)

**Lawyers — Leadership:** managing-partner, supervising-partner, of-counsel, client-relations-partner, risk-partner, transaction-partner, innovation-partner, litigation-partner
**Lawyers — Corporate/Transactional:** corporate-generalist, ma-specialist, contract-specialist, banking-finance, capital-markets, tech-transactions, startup-counsel, restructuring-specialist, real-estate-counsel
**Lawyers — Disputes:** litigation-associate, arbitration-specialist, dispute-resolution
**Lawyers — Regulatory:** regulatory-counsel, compliance-officer, antitrust-specialist, sanctions-specialist, public-law-counsel
**Lawyers — Specialist:** tax-counsel, ip-specialist, privacy-counsel, employment-counsel, environmental-counsel, healthcare-specialist, fintech-specialist, energy-specialist, media-specialist, international-counsel
**Lawyers — Juniors:** junior-associate, paralegal, legal-intern, legal-researcher
**Experts — Design/Comms:** design-reviewer, service-designer, plain-language-specialist, accessibility-specialist
**Experts — Research/Behavior:** user-researcher, behavioral-scientist, client-proxy
**Experts — Ethics/Quality:** ethics-auditor, ethics-reviewer, evaluator, red-team, meaning-guardian, synthesis-editor, risk-pricer
**Experts — Tech/Data:** legal-engineer, cybersecurity-advisor, ai-ethics-specialist
**Ops:** project-manager, transformation
**Orchestrators (7 workflow-specific + 1 base):** orchestrator, orchestrator-adversarial, orchestrator-counsel, orchestrator-full-bench, orchestrator-review, orchestrator-roundtable, orchestrator-tabulate, orchestrator-verification.

**Specialist prompt structure** (see `src/agents/prompts/contract-reviewer.ts`): pure markdown system prompt — phase context, document-type matrix, "our side" decision tree, risk-scoring rubric (1-5), severity classification (RED/YELLOW/GREEN). Profile-driven enrichment via `enrichPrompt()` appends Critical Rules + Success Metrics + universal "When You Are Not Sure → decline_to_find" guidance. Each definition declares: `description`, `prompt`, `tools` (whitelist), `model` ('opus'|'sonnet'|'haiku'), `maxTurns`, `outputFormat` (Zod schema).

## Debate protocol (mechanically)

`src/mcp/tools/debate-board.ts` exposes MCP tools:
- `post_finding` — agent role, finding_type (29 enum values), content, severity RED/YELLOW/GREEN, **evidence: array, min 1, runtime-guarded** (no evidence → rejected), confidence 0-1.
- `decline_to_find` — explicit "I don't know" path; always YELLOW severity, confidence 0.0, triggers human review.
- `post_challenge` — another agent challenges a finding, also evidence-required.
- `post_response` — challenged agent defends/revises/accepts.
- `resolve_debate` — orchestrator formally closes each debate (first-class auditable event).
- `get_findings`, `get_challenges`, `get_unresolved_debates`, `get_debate_summary` — read views.

It is **not voting**. It is critique-cycles + orchestrator-judge resolution. Agents must cite specific text; challenger must also cite text; orchestrator must call `resolve_debate` on every topic.

## "Three-layer verification" (concretely)

1. **Evaluator Gate** (`src/mcp/tools/evaluator-gate.ts` + `evaluator` agent): MUST use a different model than the specialist (architectural rule in `docs/architecture-spec.md`). Drops weak findings, score 0-1, max 2 revision loops then escalate human.
2. **Adversarial debate** (red-team + ethics-auditor + meaning-guardian agents challenging findings on the debate board).
3. **10-pass Verification Pipeline** (`src/types/verification.ts` defines `VERIFICATION_PASS_NAMES`): context, ux, clarity, structure, accuracy, completeness, risk, formatting, legal_design, delivery. Each pass produces severity-scored findings (critical/major/minor) with weighted overall score; verdict PASS/CONDITIONAL_PASS/FAIL per `computeVerdict()`.

**Separately:** `src/mcp/tools/grounding-verifier.ts` is a mechanical, zero-LLM citation checker. Pure string matching: regex-extracts `Section 5.2 / Clause 3 / Article 12` references and quoted strings (8+ chars), substring-matches against parsed document, with fuzzy sliding-window fallback (capped 10K chars to prevent DoS) and a "common boilerplate" half-credit list (10 phrases like "shall not be liable").

## Human gate

`src/gates/gate-resolver.ts` defines a `GateResolver` interface with 4 implementations:
- `ReadlineGateResolver` — CLI mode: prints separator-bordered banner, `readline.question('a/r/m')`.
- `AsyncGateResolver` — API mode: stores pending Promise; resolved by `POST /api/sessions/:id/gate`; 5-min default timeout → auto-reject for safety.
- `AutoApproveGateResolver` — testing.
- `WebhookGateResolver` — agent-to-agent mode: POSTs to callback URL.

Five gate types: `ethics_critical`, `meaning_critical`, `final_delivery`, `engagement_acceptance`, `team_selection`. The `request_approval` MCP tool (`src/mcp/tools/approval-gate.ts`) delegates to `session.gateResolver.resolve()`. A PreToolUse hook (`src/hooks/human-gate.ts`) tracks `session.triggeredGates` to enforce mandatory gates aren't skipped (currently passive — just records, doesn't block).

## Provider abstraction

`src/providers/types.ts`: `LLMProvider = 'anthropic' | 'mistral' | 'local' | 'managed'`. **Tier-based model resolution**, not direct override: all agents declare `costTier: 'opus' | 'sonnet' | 'haiku'`, and `resolveModel(modelName, provider)` maps each tier to the provider equivalent (e.g., opus → `mistral-large-latest` / `gemma3:27b`). Per-session provider override via API. Separate executor classes (`mistral-executor.ts`, `local-executor.ts`, `managed-agents/executor.ts`) handle the actual API calls; `tool-converter.ts` translates MCP tool definitions to each provider's tool format. Known gap (called out in README): `src/api/routes/challenge.ts` still instantiates Anthropic directly even under `LAVERN_PROVIDER=mistral`.

## Document ingestion + citations

`src/documents/parser.ts` — PDF, DOCX, MD, TXT, RTF, HTML up to 10 MB. Format-specific parsers produce `ParsedDocument` with `sections`, `tables`, `definedTerms`. `sanitize-text.ts` (SMAC-L1) strips zero-width Unicode, HTML comments, ANSI escapes BEFORE any LLM sees the doc, with audit log of what was removed (prompt-injection defense).

Citations are tied to evidence at three points: (1) every `post_finding` must include `evidence: string[]` quoting/referencing the document; (2) `grounding-verifier` mechanically validates those quotes against `session.documents[].fullText` and section headings; (3) `document-reader` MCP tool gives agents `read_document_section`, `search_document`, `get_defined_terms`, `get_document_tables` — agents navigate sections by structure, not page/line numbers (page-level precision is not modelled).

## Notable strengths (worth stealing)

1. **Evidence-required, runtime-enforced citations** — schema demands `min(1)` evidence; handler re-checks because tests/direct callers could bypass Zod. Pattern: belt-and-suspenders for invariants you depend on legally.
2. **Tier-based cross-provider model resolution** — every agent declares an abstract tier ('opus'/'sonnet'/'haiku'), and each provider has its own tier→model map. Switching providers does not require rewriting agent definitions. This is the right abstraction for portable agent specs.
3. **Mechanical grounding verifier next to LLM verification** — fast, zero-cost string-match layer that catches hallucinated citations BEFORE expensive LLM verification runs. Separate concern from semantic correctness.
4. **GateResolver interface with 4 implementations** — same `request_approval` tool works in CLI, API, autotest, and webhook-to-agent modes. Clean separation between "we need a decision" and "how the decision is delivered."
5. **`decline_to_find` as a first-class verb** — explicit affordance for "I don't know" routed to YELLOW + human review, beats coercing low-confidence findings.
6. **Per-phase tool allowlist** in workflow templates (`phasePermissions.{phase}.denyTools` in `verification.ts`) — agents can't post findings during the report-compilation phase, can't approve during intake, etc. Mechanically enforces phase discipline.
7. **Sanitization at ingest with audit log** — strips invisible Unicode before LLM exposure; logs what was removed. Prompt-injection defense as a build-time invariant, not a runtime hope.

## Notable weaknesses for enterprise use

- **No vector / hybrid retrieval.** README explicitly: "BM25-style full-text search (SQLite FTS5). There's no embedding layer, no hybrid retrieval, no semantic precedent search." Will not scale to large precedent corpora.
- **No durable task queue.** Session state is in-process; server restart mid-engagement requires re-kick (their words). No Redis/Service Bus/dead-letter.
- **No multi-tenancy in v0.15.0.** Default LOCAL_MODE is single-user `local-user`. Auth/Stripe/Google OAuth gated behind `LAVERN_AUTH_ENABLED=true` — bolt-on, not foundational.
- **Audit trail is per-session in SQLite** — no append-only ledger, no tamper-evident hashing, no off-machine archival.
- **No telemetry beyond Prometheus metrics for Clawern.** No OpenTelemetry tracing across the orchestrator → subagent → tool calls; debugging multi-agent runs is via the dashboard event stream.
- **Error recovery is "log + throw"** — `handleSessionError` doesn't checkpoint mid-run; on failure the entire engagement re-runs.
- **No agent-level RBAC.** `phasePermissions` controls tool access by workflow phase but not by user/tenant. The 'managed' provider for durable sessions is scaffolded but unwired.
- **Counsel workflow is 5-10 min synchronous** (README admits); no streaming assembly path yet.
- **Citation precision is section-level only.** No page/line offsets in `evidence` strings, no character spans returned by the parser.
- **Test rigor is breadth not depth.** 1,677 tests but no public benchmark; the project itself calls quality "a hypothesis."

## Key file paths
- Top-level structure summary: `README.md`
- Full architecture (best single doc): `docs/architecture-spec.md`
- Orchestrator: `src/orchestrator.ts`
- 67 prompts: `src/agents/prompts/*.ts`
- Definitions: `src/agents/definitions.ts`; Profiles: `src/agents/profiles.ts`
- Debate: `src/mcp/tools/debate-board.ts`; types: `src/types/debate.ts`
- 10-pass verification: `src/workflows/templates/verification.ts`, `src/types/verification.ts`
- Self/cross/score verification: `src/mcp/tools/verification-engine.ts`
- Grounding verifier: `src/mcp/tools/grounding-verifier.ts`
- Evaluator gate: `src/mcp/tools/evaluator-gate.ts`
- Human gate: `src/gates/gate-resolver.ts`, `src/mcp/tools/approval-gate.ts`, `src/hooks/human-gate.ts`
- Provider abstraction: `src/providers/types.ts`, `src/providers/mistral-executor.ts`, `src/providers/local-executor.ts`
- Document ingest: `src/documents/parser.ts`, `src/documents/structure-detector.ts`, `src/documents/sanitize-text.ts`
- Workflow templates: `src/workflows/templates/{adversarial,counsel,full-bench,review,roundtable,tabulate,verification,legal-design,pre-engagement}.ts`
