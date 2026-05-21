---
name: lavern-followup-2026-05-20
description: Follow-up deep-dive on AnttiHero/lavern v0.15.0 — concrete answers on streaming/viz, cross-check loop, full agent inventory, and legal-reference-data reality.
metadata:
  type: reference
---

# Lavern Follow-Up (2026-05-20)

Supplements [[lavern-multi-agent-legal-system]]. Investigation against fresh shallow clone at `c:/tmp/lavern` (v0.15.0, last push 2026-05-20).

## Streaming transport
- **WebSocket only**, no SSE/polling. `src/api/ws-handler.ts` (lines 63-193) — `attachEventStream(socket, session, fromIndex)` subscribes to `session.events` (a `ShemEventBus`, Node EventEmitter) and forwards every event as `{type:'live', event, index}`. Plus `attachReplayStream` (lines 199-268) for recorded sessions with pause/seek/speed controls. Heartbeat: protocol ping every 30s, terminate after 60s no pong; global cap `MAX_WS_CONNECTIONS` (default 200).
- Late-joining clients get full replay via `getEventsSince(fromIndex)`.

## Event type catalog
`src/events/event-bus.ts` lines 17-76 — single discriminated union `ShemEvent`. ~50 event variants grouped: session lifecycle, workflow_step, agent_start/stop, finding/challenge/response/debate_resolved, gate_requested/decided, verification_pass_started/completed/finding/report_compiled, quality_check_run/result, evaluator_gate_run/result, routing_decision, phase_handoff, claw_* (daemon), pre-engagement, uncertainty_declared, cost_update, tool_used, error.

## Visualization (not a DAG)
- No D3, no React Flow, no Mermaid, no cytoscape. `viz/package.json` deps: `react@19, motion@12, recharts@3, pdfjs-dist`.
- Render path: `viz/src/working/WorkingView.tsx` (861 lines) is the live dashboard. Renders **`PhaseStrip`** (horizontal step indicator, 11 fixed phases hard-coded in `STEP_COLORS`, custom inline CSS — `viz/src/working/components/PhaseStrip.tsx`), **`Timeline`** (153 lines), **`InsightFeed`** (381 lines), and cards: `WorkflowStepCard`, `FindingCard`, `ChallengeCard`, `DebateThreadCard`, `ResolutionCard`, `ResponseCard`, `QualityCheckCard`, `GateCard`, `AgentChip`, `AgentPresenceOrbs`, `AgentThinkingBubble`, `HeartbeatBand`, `ActivityRing`.
- It is **a sequential phase strip + event-streamed cards/feed**, NOT a true DAG. There is no edge/node graph data structure.

## Cross-check loop (Review template)
`src/workflows/templates/review.ts` pipeline: intake → specialist_analysis → evaluator_gate → plain_language_review → verification_pass → final_gate → delivered.
- Specialist posts finding (`post_finding`, evidence-required); orchestrator calls `run_evaluator_gate` (`src/mcp/tools/evaluator-gate.ts`); evaluator agent (MUST be different model tier) returns pass/fail+score+failure_reasons; `record_evaluation_result` increments `gw.revisionCount`; **bounded `DEFAULT_MAX_ITERATIONS = 2`**, then either proceed or escalate human gate.
- Quality-check variant: `src/mcp/tools/quality-check.ts` — same shape, three check types `self|peer|evaluator`, also 2-iteration bound, then "proceed but flag gaps."
- Challengers cannot themselves be challenged via a debate-of-debate primitive; once the orchestrator calls `resolve_debate`, that is final for audit. `Challenge.resolved: boolean` is a flag on the original challenge, not a sub-challenge.
- Persisted: `Finding`, `Challenge`, `Response`, `DebateResolution`, `DebateRound`, `EvaluatorResult`, `QualityCheckResult` — all in SQLite via session-state boundedPush.

## Legal reference data — reality
- **Bundles**: 6 hardcoded doc templates in `src/templates/docs/` (consulting-agreement, nda-mutual, nda-one-way, privacy-policy, saas-agreement, terms-of-service); 7 inline knowledge modules `src/knowledge/*.ts` (pattern-library, ethics-audit, legal-sanity-check, meaning-preservation, persona, plain-language, scoring-rubric) — these are TypeScript string-literal heuristics, not a precedent corpus.
- **User-uploaded KB**: `src/knowledge-base/{indexer,retriever}.ts` — user uploads docs, SQLite FTS5 BM25 search, user-scoped. Bring-your-own.
- **NO external legal MCP servers**. `src/mcp/remote-bridge/` exists but is the **outbound** scaffolding for Anthropic Managed Agents (feature-flagged off by default via `LAVERN_MANAGED_AGENTS_BRIDGE`), NOT an integration with CourtListener/Justia/EUR-Lex/Westlaw. grep for those keywords returns only prompt-text mentions.
- Document parser unchanged: `src/documents/parser.ts` (PDF/DOCX/MD/TXT/RTF/HTML up to 10 MB) + `sanitize-text.ts` (SMAC-L1 strips zero-width/HTML/ANSI before LLM).

## Workflow templates (9)
adversarial, counsel, full-bench, legal-design, pre-engagement, review, roundtable, tabulate, verification (file list in `src/workflows/templates/`).

## Orchestrators
Profiles file is partially renamed — `profiles.ts` declares `orchestrator-conductor`, `orchestrator-closer`, `orchestrator-professor`, `orchestrator-fixer` in addition to the 7 in the prompts dir (orchestrator + adversarial/counsel/full-bench/review/roundtable/tabulate/verification). Some are aliases. There's drift between profiles and prompt files — flag for any port.
